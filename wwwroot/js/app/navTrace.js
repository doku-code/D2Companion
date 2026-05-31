import { NAV_TRACE_STORAGE_KEYS } from "./storageKeys.js";

const DEBUG_PARAM = "debugNav";

let enabled = null;
let panel = null;
let hookedErrors = false;
let clickTracingPages = new Set();
const startedAt = typeof performance !== "undefined" ? performance.now() : Date.now();

export function isNavTraceEnabled() {
  if (enabled !== null) return enabled;
  try {
    enabled = new URLSearchParams(window.location.search).get(DEBUG_PARAM) === "1";
  } catch {
    enabled = false;
  }
  return enabled;
}

export function createPageTrace(pageName) {
  hookGlobalErrors(pageName);
  initClickTrace(pageName);
  return {
    enabled: isNavTraceEnabled(),
    log(stage, data = {}) {
      traceNav(pageName, stage, data);
    },
    error(stage, error, data = {}) {
      traceNav(pageName, stage, {
        ...data,
        message: (error && error.message) || String(error),
        stack: (error && error.stack) || "",
      }, "error");
    },
  };
}

export function traceNav(pageName, stage, data = {}, level = "info") {
  if (!isNavTraceEnabled()) return;
  const payload = {
    ms: Math.round((typeof performance !== "undefined" ? performance.now() : Date.now()) - startedAt),
    page: pageName,
    stage,
    url: window.location.href,
    storage: readTraceStorage(),
    ...data,
  };
  const line = `[D2NavTrace] ${level} ${JSON.stringify(payload)}`;
  if (level === "error") console.error(line, payload);
  else console.log(line, payload);
  writePanel(level, payload);
}

export function initClickTrace(pageName) {
  if (!isNavTraceEnabled() || clickTracingPages.has(pageName)) return;
  clickTracingPages.add(pageName);
  document.addEventListener("click", event => {
    const before = describeClick(event);
    traceNav(pageName, "click.capture", before);
    window.setTimeout(() => {
      traceNav(pageName, "click.after-default", {
        ...before,
        defaultPreventedAfterHandlers: event.defaultPrevented,
      });
    }, 0);
  }, true);
}

export function describeClick(event) {
  const target = event.target;
  const anchor = target && target.closest ? target.closest("a[href]") : null;
  return {
    tagName: (target && target.tagName) || null,
    id: (target && target.id) || null,
    className: target && typeof target.className === "string" ? target.className : null,
    text: cleanText(target && target.textContent),
    closestAnchorHref: (anchor && anchor.href) || null,
    defaultPrevented: event.defaultPrevented,
    button: event.button,
    ctrlKey: event.ctrlKey,
    metaKey: event.metaKey,
    shiftKey: event.shiftKey,
    altKey: event.altKey,
  };
}

export function readTraceStorage() {
  const values = {};
  for (const key of NAV_TRACE_STORAGE_KEYS) {
    try {
      const value = localStorage.getItem(key);
      if (value !== null) values[key] = value;
    } catch {
      values[key] = "<unavailable>";
    }
  }
  return values;
}

function hookGlobalErrors(pageName) {
  if (!isNavTraceEnabled() || hookedErrors) return;
  hookedErrors = true;
  window.addEventListener("error", event => traceNav(pageName, "window.error", {
    message: event.message,
    source: event.filename,
    line: event.lineno,
    column: event.colno,
    error: String((event.error && event.error.stack) || event.error || ""),
  }, "error"));
  window.addEventListener("unhandledrejection", event => traceNav(pageName, "window.unhandledrejection", {
    reason: String((event.reason && event.reason.stack) || event.reason || ""),
  }, "error"));
}

function writePanel(level, payload) {
  const host = ensurePanel();
  const text = `[${new Date().toLocaleTimeString()}] ${level} ${payload.page}.${payload.stage} +${payload.ms}ms\n${JSON.stringify(payload, null, 2)}`;
  host.textContent = `${text}\n\n${host.textContent}`.slice(0, 28000);
}

function ensurePanel() {
  if (panel && document.body.contains(panel)) return panel;
  panel = document.createElement("aside");
  panel.id = "appExecutionTrace";
  panel.setAttribute("aria-live", "polite");
  panel.style.cssText = [
    "position:fixed",
    "right:12px",
    "bottom:38px",
    "z-index:10000",
    "width:min(520px,calc(100vw - 24px))",
    "max-height:48vh",
    "overflow:auto",
    "padding:10px",
    "border:1px solid #c89b46",
    "background:rgba(0,0,0,.88)",
    "color:#f6d27a",
    "font:12px/1.35 Consolas,Menlo,monospace",
    "white-space:pre-wrap",
    "pointer-events:auto",
  ].join(";");
  panel.textContent = "debugNav=1 execution trace enabled\n\n";
  document.body.appendChild(panel);
  return panel;
}

function cleanText(value) {
  const text = String(value || "").replace(/\s+/g, " ").trim();
  return text.length > 120 ? `${text.slice(0, 117)}...` : text;
}
