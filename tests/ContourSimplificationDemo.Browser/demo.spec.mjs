import { expect, test } from "@playwright/test";
import { readFile } from "node:fs/promises";

// Controlled API measurements exercise the served app.js through the actual UI.
// Selection is asserted against explicit answers, without importing its helpers.
const original = [[0, 0], [10, 0], [10, 8], [6, 10], [0, 10], [-1, 5]];
const chosenPoints = [[1, 2], [9, 2], [4, 9]];
const candidates = [
  { index: 0, epsilon: 0, points: original, vertexCount: 6, error: 0, changedArea: 0, unionArea: 100 },
  { index: 1, epsilon: 1, points: original.slice(0, 5), vertexCount: 5, error: 0.005, changedArea: 0.5, unionArea: 100 },
  { index: 2, epsilon: 2, points: original.slice(0, 4), vertexCount: 4, error: 0.008, changedArea: 0.8, unionArea: 100 },
  { index: 3, epsilon: 3, points: chosenPoints, vertexCount: 3, error: 0.007, changedArea: 0.7, unionArea: 100 },
  { index: 4, epsilon: 4, points: [[2, 3], [8, 3], [5, 8]], vertexCount: 3, error: 0.012, changedArea: 1.2, unionArea: 100 }
];
const measured = { fillRule: "NonZero", original, originalArea: 100, candidates, skipped: [] };
const presets = [
  { id: "BGR", name: "Boundary fixture", description: "Controlled measurements", points: original },
  { id: "NEXT", name: "Next contour", description: "Newer input", points: original.map(([x, y]) => [x + 100, y]) }
];

async function downloadText(page, button) {
  const downloadPromise = page.waitForEvent("download");
  await page.locator(button).click();
  const download = await downloadPromise;
  expect(await download.failure()).toBeNull();
  return readFile(await download.path(), "utf8");
}

test("the real server loads the default contour and measures candidates", async ({ page }) => {
  const responsePromise = page.waitForResponse(response => response.url().endsWith("/api/candidates"));
  await page.goto("/");
  const response = await responsePromise;
  expect(response.ok()).toBe(true);
  const result = await response.json();
  expect(result.fillRule).toBe("NonZero");
  expect(result.original.length).toBeGreaterThan(3);
  expect(result.candidates.length).toBeGreaterThan(1);
  await expect(page.locator("#preset")).toHaveValue("BGR");
  await expect(page.locator("#status")).toHaveAttribute("data-state", "ready");
  await expect(page.locator("#contour-svg")).toHaveAttribute("aria-busy", "false");
  await expect(page.locator("#simplified-outline")).toHaveAttribute("d", /^M.+ Z$/);
  await expect(page.locator("#export-json")).toBeEnabled();
});

test("nonmonotone candidates, inclusive budget and exports use the chosen contour", async ({ page }) => {
  let requests = 0;
  await page.route("**/api/presets", route => route.fulfill({ json: presets }));
  await page.route("**/api/candidates", route => {
    requests++;
    return route.fulfill({ json: measured });
  });
  await page.goto("/");

  // A later, smaller candidate fits even though the earlier candidate exceeds 0.7%.
  await expect(page.locator("#export-json")).toBeEnabled();
  await page.locator("#budget-number").fill("0.7");
  await expect(page.locator("#vertices")).toHaveText("6 → 3");
  await expect(page.locator("#measured-change")).toHaveText("0.7%");
  await expect(page.locator("#simplified-outline")).toHaveAttribute("d", "M1,2 L9,2 L4,9 Z");

  await page.locator("#budget-number").fill("0.6999");
  await expect(page.locator("#vertices")).toHaveText("6 → 5");
  await expect(page.locator("#measured-change")).toHaveText("0.5%");
  await page.locator("#budget-number").fill("0.7001");
  await expect(page.locator("#vertices")).toHaveText("6 → 3");
  await page.locator("#budget-number").fill("0.5");
  await expect(page.locator("#vertices")).toHaveText("6 → 5");
  const halfPercent = JSON.parse(await downloadText(page, "#export-json"));
  expect(halfPercent.budget).toBe(0.005);
  await page.locator("#budget-number").fill("0.4999");
  await expect(page.locator("#vertices")).toHaveText("6 → 6");
  await page.locator("#budget-number").fill("5e-7");
  const scientific = JSON.parse(await downloadText(page, "#export-json"));
  expect(scientific.budget).toBe(5e-9);
  expect(scientific.points).toEqual(original);
  await page.locator("#budget-number").fill("0");
  await expect(page.locator("#vertices")).toHaveText("6 → 6");
  // Exercise the range's production input listener at the same exact boundary.
  await page.locator("#budget-range").evaluate(input => {
    input.value = "0.7";
    input.dispatchEvent(new Event("input", { bubbles: true }));
  });
  await expect(page.locator("#budget-number")).toHaveValue("0.7");
  await expect(page.locator("#vertices")).toHaveText("6 → 3");

  const json = JSON.parse(await downloadText(page, "#export-json"));
  expect(json).toEqual({
    name: "Boundary fixture", fillRule: "NonZero", budget: 0.007,
    measuredJaccardDistance: 0.007, originalVertexCount: 6, simplifiedVertexCount: 3,
    epsilon: 3, points: chosenPoints
  });
  const svg = await downloadText(page, "#export-svg");
  const exported = await page.evaluate(text => {
    const document = new DOMParser().parseFromString(text, "image/svg+xml");
    return {
      valid: !document.querySelector("parsererror"),
      namespace: document.documentElement.namespaceURI,
      title: document.querySelector("title")?.textContent,
      paths: [...document.querySelectorAll("path")].map(path => ({
        d: path.getAttribute("d"), fillRule: path.getAttribute("fill-rule")
      }))
    };
  }, svg);
  expect(exported).toEqual({
    valid: true, namespace: "http://www.w3.org/2000/svg",
    title: "Boundary fixture — simplified contour",
    paths: [{ d: "M1,2 L9,2 L4,9 Z", fillRule: "nonzero" }]
  });
  expect(requests).toBe(1); // Budget edits use the already measured response.
});

test("an older HTTP response cannot replace the newer contour or its export", async ({ page }) => {
  // Simulate a transport that completes after abort: the production version guard
  // must protect the DOM and downloads independently of AbortController.
  await page.addInitScript(() => {
    const fetch = window.fetch.bind(window);
    window.__candidateResponsesRead = 0;
    window.fetch = async (url, options) => {
      const isCandidate = url === "/api/candidates";
      const response = await fetch(url, isCandidate ? { ...options, signal: undefined } : options);
      if (isCandidate) {
        const json = response.json.bind(response);
        response.json = async () => {
          const result = await json();
          // Run after the app's await/continuation, so the final assertions cannot
          // pass prematurely while the stale response is still being processed.
          setTimeout(() => window.__candidateResponsesRead++, 0);
          return result;
        };
      }
      return response;
    };
  });
  const newerPoints = [[101, 2], [109, 2], [104, 9]];
  const newer = {
    ...measured, original: presets[1].points,
    candidates: [{ ...candidates[3], points: newerPoints }]
  };
  let firstRoute;
  let requests = 0;
  await page.route("**/api/presets", route => route.fulfill({ json: presets }));
  await page.route("**/api/candidates", route => {
    if (++requests === 1) { firstRoute = route; return; }
    return route.fulfill({ json: newer });
  });
  await page.goto("/");
  await expect.poll(() => requests).toBe(1);
  await expect(page.locator("#contour-svg")).toHaveAttribute("aria-busy", "true");
  await expect(page.locator("#export-json")).toBeDisabled();
  await expect(page.locator("#export-svg")).toBeDisabled();
  await page.locator("#preset").selectOption("NEXT");
  await expect(page.locator("#simplified-outline")).toHaveAttribute("d", "M101,2 L109,2 L104,9 Z");
  await firstRoute.fulfill({ json: measured });
  await expect.poll(() => page.evaluate(() => window.__candidateResponsesRead)).toBe(2);

  await expect(page.locator("#contour-title")).toHaveText("Next contour");
  await expect(page.locator("#original-outline")).toHaveAttribute("d", "M100,0 L110,0 L110,8 L106,10 L100,10 L99,5 Z");
  await expect(page.locator("#simplified-outline")).toHaveAttribute("d", "M101,2 L109,2 L104,9 Z");
  await expect(page.locator("#status")).toHaveAttribute("data-state", "ready");
  await expect(page.locator("#contour-svg")).toHaveAttribute("aria-busy", "false");
  const json = JSON.parse(await downloadText(page, "#export-json"));
  expect(json.name).toBe("Next contour");
  expect(json.points).toEqual(newerPoints);
  expect(requests).toBe(2);
});
