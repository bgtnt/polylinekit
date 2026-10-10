const byId = id => document.getElementById(id);
const controls = {
  preset: byId("preset"), draw: byId("draw"), clear: byId("clear"),
  range: byId("budget-range"), number: byId("budget-number"),
  svgExport: byId("export-svg"), jsonExport: byId("export-json")
};
const svg = byId("contour-svg");
const geometry = byId("geometry");
const state = {
  version: 0, controller: null, presets: [], original: [], candidates: [], skipped: [],
  selected: null, label: "Contour", budget: 0.007, budgetValid: true,
  drawMode: false, pointer: null, stroke: [], capped: false, pending: false
};
const percentage = value => `${new Intl.NumberFormat("en", { maximumFractionDigits: 4 }).format(value * 100)}%`;
const number = value => new Intl.NumberFormat("en", { maximumSignificantDigits: 8 }).format(value);

// Every usable candidate is inspected: its measured error need not increase with epsilon.
export function selectCandidate(candidates, budget) {
  return candidates.filter(candidate => Number.isFinite(candidate.error) && candidate.error >= 0 && candidate.error <= budget)
    .sort((a, b) => a.vertexCount - b.vertexCount || a.error - b.error || a.epsilon - b.epsilon || a.index - b.index)[0] ?? null;
}

function samePoint(a, b) { return a[0] === b[0] && a[1] === b[1]; }
export function normalizeDrawnPoints(points) {
  const cleaned = [];
  for (const point of points) {
    if (cleaned.length === 0 || !samePoint(cleaned[cleaned.length - 1], point)) cleaned.push([point[0], point[1]]);
  }
  if (cleaned.length > 1 && samePoint(cleaned[0], cleaned[cleaned.length - 1])) cleaned.pop();
  return cleaned;
}

function validPoints(points) {
  return Array.isArray(points) && points.length >= 3 && points.length <= 1024 && points.every(point =>
    Array.isArray(point) && point.length === 2 && point.every(value => Number.isFinite(value) && Math.abs(value) <= 1e100));
}

function pathText(points) {
  return points.length ? points.map((point, index) => `${index ? "L" : "M"}${point[0]},${point[1]}`).join(" ") + " Z" : "";
}

function bounds(points) {
  if (!points.length) return { x: 0, y: 0, width: 1000, height: 700, flip: 700 };
  let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;
  for (const [x, y] of points) { minX = Math.min(minX, x); minY = Math.min(minY, y); maxX = Math.max(maxX, x); maxY = Math.max(maxY, y); }
  const span = Math.max(maxX - minX, maxY - minY);
  const padding = span > 0 ? span * 0.1 : 1;
  return { x: minX - padding, y: minY - padding, width: maxX - minX + 2 * padding,
    height: maxY - minY + 2 * padding, flip: minY + maxY };
}

function setBounds(element, box) {
  for (const attribute of ["x", "y", "width", "height"]) element.setAttribute(attribute, box[attribute]);
}

function renderGeometry(original, simplified = [], fixedDrawingView = false) {
  const box = bounds(fixedDrawingView ? [] : original.concat(simplified));
  svg.setAttribute("viewBox", `${box.x} ${box.y} ${box.width} ${box.height}`);
  geometry.setAttribute("transform", `translate(0 ${box.flip}) scale(1 -1)`);
  const first = pathText(original), second = pathText(simplified);
  for (const id of ["original-fill", "original-outline", "removed-original", "added-original"]) byId(id).setAttribute("d", first);
  for (const id of ["simplified-fill", "simplified-outline", "removed-simplified", "added-simplified"]) byId(id).setAttribute("d", second);
  for (const id of ["removed-mask", "added-mask", "removed-mask-background", "added-mask-background", "removed-layer", "added-layer"]) setBounds(byId(id), box);
  byId("removed-layer").setAttribute("visibility", simplified.length ? "visible" : "hidden");
  byId("added-layer").setAttribute("visibility", simplified.length ? "visible" : "hidden");
  byId("canvas-placeholder").hidden = original.length > 0;
}

function setStatus(message, kind = "ready") {
  byId("status-text").textContent = message;
  byId("status").dataset.state = kind;
}

function setPlaceholder(title, note) {
  const placeholder = byId("canvas-placeholder");
  placeholder.querySelector("strong").textContent = title;
  placeholder.querySelector("span:last-child").textContent = note;
  placeholder.hidden = false;
}

function exportEnabled(enabled) {
  controls.svgExport.disabled = !enabled;
  controls.jsonExport.disabled = !enabled;
}

function resetMetrics() {
  for (const id of ["vertices", "vertices-removed", "measured-change", "detail-changed", "detail-union", "detail-epsilon", "detail-candidates"]) byId(id).textContent = "—";
  byId("vertices-note").textContent = "Before → after";
  byId("change-note").textContent = "Jaccard distance";
  byId("selection-badge").hidden = true;
  byId("skipped-diagnostics").hidden = true;
}

function invalidate() {
  state.version++;
  state.controller?.abort();
  state.controller = null;
  state.original = [];
  state.candidates = [];
  state.skipped = [];
  state.selected = null;
  state.pending = false;
  exportEnabled(false);
  resetMetrics();
  svg.setAttribute("aria-busy", "false");
  return state.version;
}

function setDrawMode(enabled) {
  state.drawMode = enabled;
  byId("canvas").classList.toggle("draw-mode", enabled);
  controls.draw.setAttribute("aria-pressed", String(enabled));
  byId("drawing-count").hidden = !enabled;
}

function updateSelection() {
  state.selected = state.budgetValid ? selectCandidate(state.candidates, state.budget) : null;
  exportEnabled(Boolean(state.selected) && !state.pending);
  if (!state.selected) {
    if (state.candidates.length) {
      resetMetrics();
      renderGeometry(state.original);
      setStatus(state.budgetValid ? "No measured candidate is within this budget." : "Enter an area budget between 0 and 5%.", "error");
    }
    return;
  }
  const selected = state.selected, before = state.original.length;
  renderGeometry(state.original, selected.points);
  byId("vertices").textContent = `${before} → ${selected.vertexCount}`;
  byId("vertices-removed").textContent = percentage((before - selected.vertexCount) / before);
  byId("measured-change").textContent = percentage(selected.error);
  byId("change-note").textContent = `Within the ${percentage(state.budget)} budget`;
  byId("vertices-note").textContent = selected.vertexCount < before ? "Before → after" : "No reduction at this budget";
  byId("selection-badge").hidden = false;
  byId("detail-changed").textContent = `${number(selected.changedArea)} units²`;
  byId("detail-union").textContent = `${number(selected.unionArea)} units²`;
  byId("detail-epsilon").textContent = `${number(selected.epsilon)} units`;
  byId("detail-candidates").textContent = `${state.candidates.length} measured${state.skipped.length ? `, ${state.skipped.length} skipped` : ""}`;
  byId("skipped-diagnostics").hidden = state.skipped.length === 0;
  byId("skipped-list").replaceChildren(...state.skipped.map(item => {
    const li = document.createElement("li");
    li.textContent = `Candidate ${item.index}, tolerance ${number(item.epsilon)}: ${item.reason}`;
    return li;
  }));
  setStatus(`${state.candidates.length} candidates measured against the original.${state.skipped.length ? ` ${state.skipped.length} unsuitable candidates skipped; see details.` : ""}${state.capped ? " Recording stopped at the 1,024-point limit." : ""}`);
}

async function commitContour(points, label, description) {
  const version = invalidate();
  setDrawMode(false);
  state.label = label;
  byId("contour-title").textContent = label;
  byId("preset-description").title = description;
  byId("preset-description").textContent = controls.preset.value === "MIXED"
    ? "Equal-sized additions and removals can cancel in total area. Try a 5% budget."
    : controls.preset.value === "custom"
      ? "Your unchanged, recorded contour is the reference for every candidate."
      : "A Natural Earth border in normalized map coordinates; area units are not square kilometres.";
  const cleaned = normalizeDrawnPoints(points);
  if (!validPoints(cleaned)) {
    renderGeometry(cleaned);
    setStatus("A contour needs at least three finite recorded points, with at most 1,024 points.", "error");
    return;
  }
  state.original = Object.freeze(cleaned.map(point => Object.freeze(point)));
  renderGeometry(state.original);
  state.pending = true;
  svg.setAttribute("aria-busy", "true");
  setStatus("Measuring a bounded series of simplifications against the original…", "loading");
  const controller = new AbortController();
  state.controller = controller;
  try {
    const response = await fetch("/api/candidates", {
      method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ points: state.original }), signal: controller.signal
    });
    const result = await response.json();
    if (version !== state.version) return;
    if (!response.ok) throw new Error(result.error || result.detail || result.title || "The local calculation failed.");
    if (result.fillRule !== "NonZero" || !validPoints(result.original) || !Number.isFinite(result.originalArea) || result.originalArea <= 0 || !Array.isArray(result.candidates)) {
      throw new Error("The server returned an invalid candidate response.");
    }
    for (const candidate of result.candidates) {
      if (!validPoints(candidate.points) || candidate.vertexCount !== candidate.points.length ||
          !Number.isInteger(candidate.index) || !Number.isFinite(candidate.epsilon) || candidate.epsilon < 0 ||
          !Number.isFinite(candidate.error) || candidate.error < 0 || candidate.error > 1 ||
          !Number.isFinite(candidate.changedArea) || candidate.changedArea < 0 || !Number.isFinite(candidate.unionArea) || candidate.unionArea <= 0) {
        throw new Error("The server returned an invalid measured candidate; no result was exported.");
      }
    }
    state.original = Object.freeze(result.original.map(point => Object.freeze([point[0], point[1]])));
    state.candidates = result.candidates;
    state.skipped = Array.isArray(result.skipped) ? result.skipped : [];
    state.pending = false;
    updateSelection();
  } catch (error) {
    if (version !== state.version || error.name === "AbortError") return;
    state.pending = false;
    state.selected = null;
    exportEnabled(false);
    setStatus(error.message || "The local server could not complete the calculation. Try another contour.", "error");
  } finally {
    if (version === state.version) {
      svg.setAttribute("aria-busy", "false");
      state.controller = null;
    }
  }
}

function clearForDrawing() {
  invalidate();
  state.label = "Own contour";
  state.stroke = [];
  state.pointer = null;
  state.capped = false;
  controls.preset.value = "custom";
  byId("contour-title").textContent = "Own contour";
  byId("preset-description").textContent = "Your recorded points are kept. There is no hidden resampling or initial simplification.";
  byId("preset-description").removeAttribute("title");
  byId("drawing-count").textContent = "0 / 1,024 points";
  renderGeometry([], [], true);
  setDrawMode(true);
  setPlaceholder("Draw one closed contour", "Drag with a mouse, pen or finger. Release to measure its simplifications.");
  setStatus("Ready to draw. The closing segment is included in the contour.", "draw");
}

function pointerPoint(event) {
  const matrix = geometry.getScreenCTM();
  if (!matrix) return null;
  const point = new DOMPoint(event.clientX, event.clientY).matrixTransform(matrix.inverse());
  return Number.isFinite(point.x) && Number.isFinite(point.y) ? [point.x, point.y] : null;
}

function recordPoint(event) {
  if (state.stroke.length >= 1024) { state.capped = true; return; }
  const point = pointerPoint(event);
  if (point && (!state.stroke.length || !samePoint(state.stroke[state.stroke.length - 1], point))) state.stroke.push(point);
  if (state.stroke.length === 1024) {
    state.capped = true;
    setStatus("The 1,024-point limit is reached. Recording has stopped; release to measure this contour.", "draw");
  }
}

svg.addEventListener("pointerdown", event => {
  if (!state.drawMode || state.pointer !== null || !event.isPrimary || event.button !== 0) return;
  event.preventDefault();
  invalidate();
  state.stroke = [];
  state.capped = false;
  state.pointer = event.pointerId;
  svg.setPointerCapture(event.pointerId);
  recordPoint(event);
  renderGeometry(state.stroke, [], true);
  setStatus("Drawing… Release to close the contour and measure it.", "draw");
});

svg.addEventListener("pointermove", event => {
  if (state.pointer !== event.pointerId) return;
  event.preventDefault();
  const events = typeof event.getCoalescedEvents === "function" ? event.getCoalescedEvents() : [];
  for (const sample of events.length ? events : [event]) recordPoint(sample);
  renderGeometry(state.stroke, [], true);
  byId("drawing-count").textContent = `${state.stroke.length.toLocaleString("en")} / 1,024 points`;
});

svg.addEventListener("pointerup", event => {
  if (state.pointer !== event.pointerId) return;
  event.preventDefault();
  recordPoint(event);
  state.pointer = null;
  if (svg.hasPointerCapture(event.pointerId)) svg.releasePointerCapture(event.pointerId);
  const points = normalizeDrawnPoints(state.stroke);
  if (points.length < 3) {
    renderGeometry(points, [], true);
    setStatus("That gesture has fewer than three distinct points. Draw a longer contour.", "error");
    return;
  }
  void commitContour(points, "Own contour", "One locally recorded gesture, closed automatically. The original coordinates are retained for every comparison.");
});

svg.addEventListener("pointercancel", event => {
  if (state.pointer !== event.pointerId) return;
  state.pointer = null;
  invalidate();
  renderGeometry(state.stroke, [], true);
  setStatus("The gesture was interrupted. Draw again to create a complete contour.", "error");
});

controls.preset.addEventListener("change", () => {
  const preset = state.presets.find(item => item.id === controls.preset.value);
  if (!preset) return;
  state.pointer = null;
  state.capped = false;
  void commitContour(preset.points, preset.name, preset.description);
});
controls.draw.addEventListener("click", clearForDrawing);
controls.clear.addEventListener("click", clearForDrawing);

controls.range.addEventListener("input", () => {
  controls.number.value = controls.range.value;
  controls.number.removeAttribute("aria-invalid");
  state.budgetValid = true;
  state.budget = Number(controls.range.value) / 100;
  updateSelection();
});
controls.number.addEventListener("input", () => {
  const value = controls.number.valueAsNumber;
  state.budgetValid = Number.isFinite(value) && value >= 0 && value <= 5;
  controls.number.setAttribute("aria-invalid", String(!state.budgetValid));
  if (state.budgetValid) { state.budget = value / 100; controls.range.value = String(value); }
  updateSelection();
});

function xmlEscape(value) { return value.replace(/[&<>"']/g, character => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&apos;" })[character]); }
export function svgDocument(candidate, label) {
  const box = bounds(candidate.points);
  return `<?xml version="1.0" encoding="UTF-8"?>\n<svg xmlns="http://www.w3.org/2000/svg" viewBox="${box.x} ${box.y} ${box.width} ${box.height}">\n  <title>${xmlEscape(label)} — simplified contour</title>\n  <g transform="translate(0 ${box.flip}) scale(1 -1)">\n    <path d="${pathText(candidate.points)}" fill="#177e7d" fill-rule="nonzero"/>\n  </g>\n</svg>\n`;
}
export function jsonDocument(candidate, originalVertexCount, budget, label) {
  return JSON.stringify({ name: label, fillRule: "NonZero", budget, measuredJaccardDistance: candidate.error,
    originalVertexCount, simplifiedVertexCount: candidate.vertexCount, epsilon: candidate.epsilon, points: candidate.points }, null, 2) + "\n";
}

function download(content, type, extension) {
  const url = URL.createObjectURL(new Blob([content], { type }));
  const anchor = document.createElement("a");
  anchor.href = url;
  anchor.download = `${state.label.toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-|-$/g, "") || "contour"}-simplified.${extension}`;
  document.body.append(anchor);
  anchor.click();
  anchor.remove();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}
controls.svgExport.addEventListener("click", () => {
  if (state.selected && !state.pending && state.budgetValid) download(svgDocument(state.selected, state.label), "image/svg+xml;charset=utf-8", "svg");
});
controls.jsonExport.addEventListener("click", () => {
  if (state.selected && !state.pending && state.budgetValid) download(jsonDocument(state.selected, state.original.length, state.budget, state.label), "application/json;charset=utf-8", "json");
});

async function initialize() {
  const initialVersion = state.version;
  renderGeometry([]);
  try {
    const response = await fetch("/api/presets");
    if (!response.ok) throw new Error("The local examples could not be loaded. Refresh after the server has started.");
    const presets = await response.json();
    if (!Array.isArray(presets) || presets.length === 0 || presets.some(preset => !validPoints(preset.points))) throw new Error("The local preset data is invalid.");
    state.presets = presets;
    controls.preset.replaceChildren(...presets.map(preset => {
      const option = document.createElement("option"); option.value = preset.id; option.textContent = preset.name; return option;
    }));
    const custom = document.createElement("option"); custom.value = "custom"; custom.textContent = "Own contour"; custom.disabled = true; controls.preset.append(custom);
    controls.preset.disabled = false;
    if (state.version !== initialVersion) {
      controls.preset.value = "custom";
      return;
    }
    const initial = presets.find(preset => preset.id.toUpperCase() === "BGR") ?? presets[0];
    controls.preset.value = initial.id;
    await commitContour(initial.points, initial.name, initial.description);
  } catch (error) {
    if (state.version !== initialVersion) return;
    svg.setAttribute("aria-busy", "false");
    setPlaceholder("The local server is unavailable", "Reload this page after starting the demo. You can still record a contour with Draw.");
    setStatus(error.message || "The local examples could not be loaded.", "error");
  }
}

void initialize();
