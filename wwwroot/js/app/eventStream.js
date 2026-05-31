import { traceNav } from "./navTrace.js";

const EVENT_URL = "/api/events";
let source = null;
let reconnectTimer = null;
let sourceId = 0;
const listeners = new Map();
const attachedEvents = new Set();
let lifecycleHooked = false;

export function addSseListener(eventName, handler) {
  if (!listeners.has(eventName)) listeners.set(eventName, new Set());
  listeners.get(eventName).add(handler);
  traceNav("event-stream", "listener.add", {
    eventName,
    listenerCount: listeners.get(eventName).size,
    activeEvents: [...listeners.keys()],
    activeSourceCount: source ? 1 : 0,
  });
  attachEvent(eventName);
  ensureConnected();

  return () => {
    const set = listeners.get(eventName);
    if (!set) return;
    set.delete(handler);
    if (set.size === 0) listeners.delete(eventName);
    traceNav("event-stream", "listener.remove", {
      eventName,
      remaining: set.size,
      activeEvents: [...listeners.keys()],
      activeSourceCount: source ? 1 : 0,
    });
  };
}

function ensureConnected() {
  hookLifecycle();
  if (source || reconnectTimer) return;

  const id = ++sourceId;
  source = new EventSource(EVENT_URL);
  traceNav("event-stream", "source.open", {
    sourceId: id,
    url: EVENT_URL,
    activeEvents: [...listeners.keys()],
    activeSourceCount: 1,
  });
  attachedEvents.clear();
  for (const eventName of listeners.keys()) attachEvent(eventName);

  source.onerror = () => {
    traceNav("event-stream", "source.error", { sourceId: id });
    closeSource("error");
    source = null;
    reconnectTimer = window.setTimeout(() => {
      reconnectTimer = null;
      ensureConnected();
    }, 1000);
  };
}

function attachEvent(eventName) {
  if (!source || attachedEvents.has(eventName)) return;
  source.addEventListener(eventName, dispatch);
  attachedEvents.add(eventName);
  traceNav("event-stream", "event.attach", {
    eventName,
    activeEvents: [...attachedEvents],
    activeSourceCount: source ? 1 : 0,
  });
}

function dispatch(event) {
  const set = listeners.get(event.type);
  if (!set) return;

  for (const handler of [...set]) {
    handler(event);
  }
}

function hookLifecycle() {
  if (lifecycleHooked) return;
  lifecycleHooked = true;
  window.addEventListener("pagehide", () => closeSource("pagehide"));
  window.addEventListener("pageshow", event => {
    traceNav("event-stream", "pageshow", {
      persisted: event.persisted,
      listenerEventCount: listeners.size,
      activeSourceCount: source ? 1 : 0,
    });
    if (listeners.size > 0) ensureConnected();
  });
}

function closeSource(reason) {
  if (reconnectTimer) {
    window.clearTimeout(reconnectTimer);
    reconnectTimer = null;
  }
  if (!source) return;
  traceNav("event-stream", "source.close", {
    reason,
    activeSourceCount: 0,
  });
  source.close();
  source = null;
  attachedEvents.clear();
}
