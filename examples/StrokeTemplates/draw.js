"use strict";
const pad = document.getElementById("pad"), context = pad.getContext("2d");
const method = document.getElementById("method"), compare = document.getElementById("compare");
const clear = document.getElementById("clear"), download = document.getElementById("download");
const flip = document.getElementById("flip"), status = document.getElementById("status");
const result = document.getElementById("result"), inspection = document.getElementById("inspection");
let config, points = [], activePointer = null, started = 0, sampleId = "", busy = false;

function message(text, error = false) { status.textContent = text; status.dataset.error = String(error); }
function validStroke() { return points.length > 1 && points.some(p => p.x !== points[0].x || p.y !== points[0].y); }
function controls() {
  compare.disabled = download.disabled = !config || busy || activePointer !== null || !validStroke();
  method.disabled = !config || busy; clear.disabled = flip.disabled = busy;
}
function hideResult() { result.hidden = true; inspection.removeAttribute("src"); }
function render() {
  context.clearRect(0, 0, pad.width, pad.height);
  if (!points.length) return;
  context.lineWidth = 2.5; context.lineCap = context.lineJoin = "round"; context.strokeStyle = "#b63442";
  context.beginPath(); points.forEach((p, i) => i ? context.lineTo(p.x, p.y) : context.moveTo(p.x, p.y)); context.stroke();
  context.fillStyle = "#b63442"; context.beginPath(); context.arc(points[0].x, points[0].y, 4, 0, 2 * Math.PI); context.fill();
}
function append(event) {
  const bounds = pad.getBoundingClientRect();
  const point = { x: (event.clientX - bounds.left) * pad.width / bounds.width,
    y: (event.clientY - bounds.top) * pad.height / bounds.height, t: performance.now() - started };
  const previous = points.at(-1);
  if (!previous || point.x !== previous.x || point.y !== previous.y) points.push(point);
  if (points.length > config.maxPoints) {
    cancel("Too many points. Clear and draw a shorter stroke.");
    return false;
  }
  return true;
}
function cancel(text) {
  const pointer = activePointer; activePointer = null; points = [];
  if (pointer !== null && pad.hasPointerCapture(pointer)) pad.releasePointerCapture(pointer);
  render(); controls(); message(text, true);
}
pad.addEventListener("pointerdown", event => {
  if (!config || busy || activePointer !== null || event.button !== 0 || !event.isPrimary) return;
  if (points.length) { message("Clear the current stroke before drawing another; separate strokes cannot be joined.", true); return; }
  event.preventDefault(); hideResult(); activePointer = event.pointerId; started = performance.now();
  sampleId = "external-" + crypto.randomUUID(); pad.setPointerCapture(event.pointerId);
  append(event); render(); controls(); message("Drawing… Lift to finish.");
});
pad.addEventListener("pointermove", event => {
  if (event.pointerId !== activePointer) return;
  const coalesced = event.getCoalescedEvents?.() ?? [];
  for (const point of coalesced.length ? coalesced : [event]) if (!append(point)) return;
  render();
});
pad.addEventListener("pointerup", event => {
  if (event.pointerId !== activePointer) return;
  if (!append(event)) return;
  activePointer = null; pad.releasePointerCapture(event.pointerId); render(); controls();
  message(validStroke() ? `${points.length} points captured. Ready to compare.` : "A click is not a stroke. Clear and draw a line.", !validStroke());
});
pad.addEventListener("pointercancel", event => { if (event.pointerId === activePointer) cancel("Drawing was interrupted. Please draw again."); });
pad.addEventListener("lostpointercapture", event => { if (event.pointerId === activePointer) cancel("Drawing was interrupted. Please draw again."); });
clear.addEventListener("click", () => { cancel("Draw a stroke to begin."); hideResult(); message("Draw a stroke to begin."); });
flip.addEventListener("change", () => { hideResult(); message(flip.checked ? "Query will use Y up (y = canvas height - captured y)." : "Query will use captured screen coordinates (Y down)."); });
method.addEventListener("change", hideResult);

function query() {
  return { sampleId, dataset: config.dataset, split: "external", label: "", supported: true,
    strokes: [points.map(p => ({ x: p.x, y: flip.checked ? pad.height - p.y : p.y, t: p.t }))] };
}
download.addEventListener("click", () => {
  const url = URL.createObjectURL(new Blob([JSON.stringify(query(), null, 2) + "\n"], { type: "application/json" }));
  const anchor = document.createElement("a"); anchor.href = url; anchor.download = sampleId + ".json"; anchor.click();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
});
compare.addEventListener("click", async () => {
  busy = true; controls(); hideResult(); message("Comparing against the frozen template bank…");
  try {
    const response = await fetch("/compare", { method: "POST", headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ query: query(), method: method.value }) });
    const reply = await response.json();
    if (!response.ok) throw new Error(reply.error || "Comparison failed.");
    document.getElementById("log").textContent = reply.summary;
    document.getElementById("result-link").href = reply.resultUrl;
    inspection.src = reply.resultUrl; result.hidden = false;
    message("Comparison complete. The captured JSON and inspection page were saved locally.");
  } catch (error) { message(error.message, true); }
  finally { busy = false; controls(); }
});
fetch("/config").then(async response => {
  if (!response.ok) throw new Error("Could not load the local bank settings.");
  config = await response.json();
  const names = { rms: "RMS", area: "Area", combined: "RMS + area", protractor: "Protractor", dtw: "DTW" };
  for (const value of config.methods) method.add(new Option(names[value], value));
  document.getElementById("bank").textContent = config.dataset === "pendigits"
    ? "Digits · 50 frozen templates · seed 1729 · one stroke only"
    : "$1 gestures · 48 frozen templates · seed 1729 · first frozen writer bank (s02)";
  controls();
}).catch(error => message(error.message, true));
