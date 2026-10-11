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

function jsonFile(name, text) {
  return { name, mimeType: "application/json", buffer: Buffer.from(text) };
}

async function allowLateCandidateResponses(page) {
  // Let the transport finish after abort, independently testing the input-version guard.
  await page.addInitScript(() => {
    const fetch = window.fetch.bind(window);
    window.__candidateResponsesRead = 0;
    window.__candidateAborts = 0;
    window.fetch = async (url, options) => {
      const isCandidate = url === "/api/candidates";
      if (isCandidate) options.signal.addEventListener("abort", () => window.__candidateAborts++, { once: true });
      const response = await fetch(url, isCandidate ? { ...options, signal: undefined } : options);
      if (isCandidate) {
        const json = response.json.bind(response);
        response.json = async () => {
          const result = await json();
          // Observe completion after the app's await/continuation, not before it.
          setTimeout(() => window.__candidateResponsesRead++, 0);
          return result;
        };
      }
      return response;
    };
  });
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
  await allowLateCandidateResponses(page);
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

test("local JSON coordinates round-trip through the real API and actual export", async ({ page }) => {
  const requests = [];
  page.on("request", request => {
    if (request.url().endsWith("/api/candidates")) requests.push(request.postDataJSON().points);
  });
  await page.goto("/");
  await expect(page.getByRole("button", { name: "Load JSON" })).toBeEnabled();
  await page.locator("#budget-number").fill("0");
  await page.locator("#draw").click();

  const points = [[120.25, -30.5], [128.25, -30.5], [128.25, -22.5], [120.25, -22.5]];
  const name = "My <img src=x> & XY contour 📐";
  const chooserPromise = page.waitForEvent("filechooser");
  await page.getByRole("button", { name: "Load JSON" }).click();
  await (await chooserPromise).setFiles(jsonFile("own.json", JSON.stringify({
    name, fillRule: "NonZero", points: [...points, points[0]],
    budget: 0.05, measuredJaccardDistance: 1, epsilon: 999 // Export metadata is recomputed.
  })));
  await expect(page.locator("#contour-title")).toHaveText(name);
  await expect(page.locator("#contour-title img")).toHaveCount(0);
  await expect(page.locator("#draw")).toHaveAttribute("aria-pressed", "false");
  await expect(page.locator("#status")).toHaveAttribute("data-state", "ready");
  await expect(page.locator("#budget-number")).toHaveValue("0");
  expect(requests[1]).toEqual(points);
  const exported = await downloadText(page, "#export-json");
  expect(JSON.parse(exported)).toEqual({
    name, fillRule: "NonZero", budget: 0, measuredJaccardDistance: 0,
    originalVertexCount: 4, simplifiedVertexCount: 4, epsilon: 0, points
  });
  const svg = await downloadText(page, "#export-svg");
  const exportedTitle = await page.evaluate(text => {
    const document = new DOMParser().parseFromString(text, "image/svg+xml");
    return { valid: !document.querySelector("parsererror"), title: document.querySelector("title")?.textContent };
  }, svg);
  expect(exportedTitle).toEqual({ valid: true, title: `${name} — simplified contour` });

  await page.locator("#json-file").setInputFiles(jsonFile("round-trip.json", exported));
  await expect.poll(() => requests.length).toBe(3);
  await expect(page.locator("#status")).toHaveAttribute("data-state", "ready");
  expect(JSON.parse(await downloadText(page, "#export-json"))).toEqual(JSON.parse(exported));

  // Exactly 1,024 supplied points are allowed, before duplicate cleanup.
  // Importing while a pointer is captured must end the old drawing gesture.
  await page.locator("#draw").click();
  const canvas = await page.locator("#contour-svg").boundingBox();
  await page.mouse.move(canvas.x + canvas.width / 3, canvas.y + canvas.height / 3);
  await page.mouse.down();
  await page.mouse.move(canvas.x + canvas.width / 2, canvas.y + canvas.height / 2);
  const supplied = [...points, ...Array.from({ length: 1020 }, () => points[3])];
  await page.locator("#json-file").setInputFiles(jsonFile("bare-array.json", JSON.stringify(supplied)));
  await expect(page.locator("#contour-title")).toHaveText("Imported contour");
  await expect(page.locator("#status")).toHaveAttribute("data-state", "ready");
  await expect(page.locator("#draw")).toHaveAttribute("aria-pressed", "false");
  await page.mouse.up();
  const bare = JSON.parse(await downloadText(page, "#export-json"));
  expect(bare.points).toEqual(points);
  expect(requests[3]).toEqual(points);
  expect(requests).toHaveLength(4);
});

test("invalid JSON files preserve the usable contour and send no API request", async ({ page }) => {
  let requests = 0;
  await page.route("**/api/presets", route => route.fulfill({ json: presets }));
  await page.route("**/api/candidates", route => { requests++; return route.fulfill({ json: measured }); });
  await page.goto("/");
  await expect(page.getByRole("button", { name: "Load JSON" })).toBeEnabled();
  const invalid = [
    ["malformed.json", "{", "not valid JSON"],
    ["missing.json", "{}", "points array"],
    ["extra-coordinate.json", "[[0,0,9],[3,0],[0,3]]", "numeric [x, y]"],
    ["string-coordinate.json", '[["0",0],[3,0],[0,3]]', "numeric [x, y]"],
    ["null-coordinate.json", "[[null,0],[3,0],[0,3]]", "numeric [x, y]"],
    ["nonfinite.json", "[[1e309,0],[3,0],[0,3]]", "finite numeric"],
    ["magnitude.json", "[[1e101,0],[3,0],[0,3]]", "magnitude"],
    ["fill-rule.json", JSON.stringify({ fillRule: "EvenOdd", points: chosenPoints }), "only the NonZero"],
    ["null-fill-rule.json", JSON.stringify({ fillRule: null, points: chosenPoints }), "only the NonZero"],
    ["control-name.json", JSON.stringify({ name: "A\u0001B", points: chosenPoints }), "valid XML text"],
    ["surrogate-name.json", JSON.stringify({ name: "A\uD800B", points: chosenPoints }), "valid XML text"],
    ["duplicates.json", "[[0,0],[0,0],[0,0]]", "duplicate removal"],
    ["too-many.json", JSON.stringify([...chosenPoints, ...Array.from({ length: 1022 }, () => chosenPoints[2])]), "1,024 supplied"],
    ["oversized.json", " ".repeat(128 * 1024 + 1), "128 KiB"]
  ];
  for (const [name, text, message] of invalid) {
    await page.locator("#json-file").setInputFiles(jsonFile(name, text));
    await expect(page.locator("#import-error")).toBeVisible();
    await expect(page.locator("#import-error")).toContainText(message);
    await expect(page.locator("#contour-title")).toHaveText("Boundary fixture");
    await expect(page.locator("#vertices")).toHaveText("6 → 3");
    await expect(page.locator("#simplified-outline")).toHaveAttribute("d", "M1,2 L9,2 L4,9 Z");
    await expect(page.locator("#export-json")).toBeEnabled();
    await expect(page.locator("#export-svg")).toBeEnabled();
    expect(requests).toBe(1);
  }
  const exported = JSON.parse(await downloadText(page, "#export-json"));
  expect(exported.name).toBe("Boundary fixture");
  expect(exported.points).toEqual(chosenPoints);
});

test("a server-rejected imported contour restores the prior usable result", async ({ page }) => {
  await page.goto("/");
  await expect(page.getByRole("button", { name: "Load JSON" })).toBeEnabled();
  const before = await downloadText(page, "#export-json");
  const outline = await page.locator("#original-outline").getAttribute("d");
  const title = await page.locator("#contour-title").textContent();
  const responsePromise = page.waitForResponse(response => response.url().endsWith("/api/candidates"));
  await page.locator("#json-file").setInputFiles(jsonFile("collinear.json", "[[0,0],[1,1],[2,2]]"));
  expect((await responsePromise).status()).toBe(400);
  await expect(page.locator("#status-text")).toContainText("Previous contour kept.");
  await expect(page.locator("#contour-svg")).toHaveAttribute("aria-busy", "false");
  await expect(page.locator("#contour-title")).toHaveText(title);
  await expect(page.locator("#original-outline")).toHaveAttribute("d", outline);
  await expect(page.locator("#preset")).toHaveValue("BGR");
  await expect(page.locator("#export-svg")).toBeEnabled();
  expect(JSON.parse(await downloadText(page, "#export-json"))).toEqual(JSON.parse(before));
});

test("overlapping imports restore the last success with the current budget and ignore the late response", async ({ page }) => {
  await allowLateCandidateResponses(page);
  let pendingRoute;
  let requests = 0;
  await page.route("**/api/presets", route => route.fulfill({ json: presets }));
  await page.route("**/api/candidates", route => {
    requests++;
    if (requests === 1) return route.fulfill({ json: measured });
    if (requests === 2) { pendingRoute = route; return; }
    return route.continue(); // Rejected imports use the real server's geometry validation.
  });
  await page.goto("/");
  await expect(page.getByRole("button", { name: "Load JSON" })).toBeEnabled();
  const description = await page.locator("#preset-description").textContent();
  const descriptionTitle = await page.locator("#preset-description").getAttribute("title");
  await page.locator("#json-file").setInputFiles(jsonFile("pending-b.json", JSON.stringify({
    name: "Pending B", points: presets[1].points
  })));
  await expect.poll(() => requests).toBe(2);
  await expect(page.locator("#contour-svg")).toHaveAttribute("aria-busy", "true");
  await expect(page.locator("#export-json")).toBeDisabled();
  await page.locator("#budget-number").fill("0.5");
  const rejectedResponse = page.waitForResponse(response => response.url().endsWith("/api/candidates"));
  await page.locator("#json-file").setInputFiles(jsonFile("rejected-c.json", "[[0,0],[1,1],[2,2]]"));
  expect((await rejectedResponse).status()).toBe(400);

  await expect(page.locator("#status-text")).toContainText("Previous contour kept.");
  await expect(page.locator("#contour-title")).toHaveText("Boundary fixture");
  await expect(page.locator("#preset")).toHaveValue("BGR");
  await expect(page.locator("#preset-description")).toHaveText(description);
  await expect(page.locator("#preset-description")).toHaveAttribute("title", descriptionTitle);
  await expect(page.locator("#vertices")).toHaveText("6 → 5");
  await expect(page.locator("#measured-change")).toHaveText("0.5%");
  await expect(page.locator("#contour-svg")).toHaveAttribute("aria-busy", "false");
  await expect(page.locator("#original-outline")).toHaveAttribute("d", "M0,0 L10,0 L10,8 L6,10 L0,10 L-1,5 Z");
  await expect(page.locator("#simplified-outline")).toHaveAttribute("d", "M0,0 L10,0 L10,8 L6,10 L0,10 Z");
  const restoredJson = await downloadText(page, "#export-json");
  expect(JSON.parse(restoredJson)).toEqual({
    name: "Boundary fixture", fillRule: "NonZero", budget: 0.005,
    measuredJaccardDistance: 0.005, originalVertexCount: 6, simplifiedVertexCount: 5,
    epsilon: 1, points: original.slice(0, 5)
  });
  const restoredSvg = await downloadText(page, "#export-svg");
  expect(restoredSvg).toContain("Boundary fixture — simplified contour");
  expect(restoredSvg).toContain('d="M0,0 L10,0 L10,8 L6,10 L0,10 Z"');
  expect(await page.evaluate(() => window.__candidateAborts)).toBe(1);

  await pendingRoute.fulfill({ json: {
    ...measured, original: presets[1].points,
    candidates: [{ ...candidates[0], points: presets[1].points }]
  } });
  await expect.poll(() => page.evaluate(() => window.__candidateResponsesRead)).toBe(3);
  await expect(page.locator("#status-text")).toContainText("Previous contour kept.");
  expect(await downloadText(page, "#export-json")).toBe(restoredJson);
  expect(await downloadText(page, "#export-svg")).toBe(restoredSvg);

  // The late B response must not poison the fallback used by another rejected import.
  await page.locator("#json-file").setInputFiles(jsonFile("rejected-after-late-b.json", "[[0,0],[1,1],[2,2]]"));
  await expect.poll(() => page.evaluate(() => window.__candidateResponsesRead)).toBe(4);
  await expect(page.locator("#status-text")).toContainText("Previous contour kept.");
  expect(await downloadText(page, "#export-json")).toBe(restoredJson);

  // A valid current result replaces A even while an invalid budget disables selection.
  await page.locator("#budget-number").fill("");
  const newestPoints = [[20, 0], [28, 0], [28, 8], [20, 8]];
  await page.locator("#json-file").setInputFiles(jsonFile("new-success.json", JSON.stringify({
    name: "Newest successful contour", points: newestPoints
  })));
  await expect.poll(() => page.evaluate(() => window.__candidateResponsesRead)).toBe(5);
  await expect(page.locator("#contour-title")).toHaveText("Newest successful contour");
  await expect(page.locator("#export-json")).toBeDisabled();
  await page.locator("#json-file").setInputFiles(jsonFile("rejected-d.json", "[[0,0],[1,1],[2,2]]"));
  await expect.poll(() => page.evaluate(() => window.__candidateResponsesRead)).toBe(6);
  await expect(page.locator("#status-text")).toContainText("Previous contour kept.");
  await expect(page.locator("#contour-title")).toHaveText("Newest successful contour");
  await expect(page.locator("#budget-number")).toHaveValue("");
  await expect(page.locator("#export-json")).toBeDisabled();
  await page.locator("#budget-number").fill("0.5");
  const newestExport = JSON.parse(await downloadText(page, "#export-json"));
  expect(newestExport.name).toBe("Newest successful contour");
  expect(newestExport.points).toEqual(newestPoints);
  expect(newestExport.budget).toBe(0.005);
  expect(requests).toBe(6);
});

for (const replacement of ["clear", "preset"]) {
  test(`an explicit ${replacement} retires the import rollback contour`, async ({ page }) => {
    await allowLateCandidateResponses(page);
    let pendingRoute;
    let requests = 0;
    await page.route("**/api/presets", route => route.fulfill({ json: presets }));
    await page.route("**/api/candidates", route => {
      requests++;
      if (requests === 1) return route.fulfill({ json: measured });
      if (replacement === "preset" && requests === 2) { pendingRoute = route; return; }
      return route.continue();
    });
    await page.goto("/");
    await expect(page.getByRole("button", { name: "Load JSON" })).toBeEnabled();
    if (replacement === "preset") {
      await page.locator("#preset").selectOption("NEXT");
      await expect.poll(() => requests).toBe(2);
    } else await page.locator("#clear").click();
    await page.locator("#json-file").setInputFiles(jsonFile("rejected.json", JSON.stringify({
      name: "Rejected after replacement", points: [[0, 0], [1, 1], [2, 2]]
    })));
    await expect(page.locator("#contour-svg")).toHaveAttribute("aria-busy", "false");
    await expect(page.locator("#status-text")).toContainText("positive finite NonZero filled area");
    await expect(page.locator("#status-text")).not.toContainText("Previous contour kept.");
    await expect(page.locator("#contour-title")).toHaveText("Rejected after replacement");
    await expect(page.locator("#export-json")).toBeDisabled();
    await expect(page.locator("#export-svg")).toBeDisabled();
    if (pendingRoute) {
      await pendingRoute.fulfill({ json: { ...measured, original: presets[1].points } });
      await expect.poll(() => page.evaluate(() => window.__candidateResponsesRead)).toBe(3);
      await expect(page.locator("#contour-title")).toHaveText("Rejected after replacement");
      await expect(page.locator("#export-json")).toBeDisabled();
    }
  });
}

test("a delayed file read cannot replace a newer preset or newer file selection", async ({ page }) => {
  await page.addInitScript(() => {
    const text = File.prototype.text;
    window.__releaseImportReads = {};
    window.__finishedImportReads = 0;
    File.prototype.text = function () {
      if (!this.name.startsWith("slow-")) return text.call(this);
      return new Promise(resolve => {
        window.__releaseImportReads[this.name] = async () => {
          resolve(await text.call(this));
          setTimeout(() => window.__finishedImportReads++, 0);
        };
      });
    };
  });
  const nextPoints = [[101, 2], [109, 2], [104, 9]];
  const next = { ...measured, original: presets[1].points, candidates: [{ ...candidates[3], points: nextPoints }] };
  let requests = 0;
  await page.route("**/api/presets", route => route.fulfill({ json: presets }));
  await page.route("**/api/candidates", route => {
    requests++;
    return route.fulfill({ json: requests === 1 ? measured : next });
  });
  await page.goto("/");
  await expect(page.getByRole("button", { name: "Load JSON" })).toBeEnabled();
  await page.locator("#json-file").setInputFiles(jsonFile("slow-preset.json", JSON.stringify(chosenPoints)));
  await page.waitForFunction(() => Boolean(window.__releaseImportReads["slow-preset.json"]));
  await page.locator("#preset").selectOption("NEXT");
  await expect(page.locator("#simplified-outline")).toHaveAttribute("d", "M101,2 L109,2 L104,9 Z");
  await page.evaluate(() => window.__releaseImportReads["slow-preset.json"]());
  await expect.poll(() => page.evaluate(() => window.__finishedImportReads)).toBe(1);
  await expect(page.locator("#contour-title")).toHaveText("Next contour");
  await expect(page.locator("#import-error")).toBeHidden();
  expect(requests).toBe(2);

  // A newer invalid file leaves the contour version unchanged, so this case
  // also needs the file-selection serial rather than the contour version alone.
  await page.locator("#json-file").setInputFiles(jsonFile("slow-import.json", JSON.stringify(chosenPoints)));
  await page.waitForFunction(() => Boolean(window.__releaseImportReads["slow-import.json"]));
  await page.locator("#json-file").setInputFiles(jsonFile("newer-invalid.json", "{"));
  await expect(page.locator("#import-error")).toContainText("not valid JSON");
  await page.evaluate(() => window.__releaseImportReads["slow-import.json"]());
  await expect.poll(() => page.evaluate(() => window.__finishedImportReads)).toBe(2);
  await expect(page.locator("#contour-title")).toHaveText("Next contour");
  await expect(page.locator("#simplified-outline")).toHaveAttribute("d", "M101,2 L109,2 L104,9 Z");
  await expect(page.locator("#import-error")).toContainText("not valid JSON");
  expect(JSON.parse(await downloadText(page, "#export-json")).points).toEqual(nextPoints);
  expect(requests).toBe(2);
});
