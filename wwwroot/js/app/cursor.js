import { traceNav } from "./navTrace.js";

let activeCursorCleanup = null;
let pageshowHooked = false;
const LAST_CURSOR_POSITION_KEY = "d2-companion-cursor-position";

export function initD2Cursor() {
  const cursor = document.querySelector("#d2Cursor");
  if (!cursor || !window.matchMedia("(pointer: fine)").matches) {
    traceNav("cursor", "init.skip", { hasCursor: Boolean(cursor) });
    return;
  }
  if (activeCursorCleanup) activeCursorCleanup();
  if (!pageshowHooked) {
    window.addEventListener("pageshow", event => {
      traceNav("cursor", "pageshow", { persisted: event.persisted });
      if (event.persisted) initD2Cursor();
    });
    pageshowHooked = true;
  }
  traceNav("cursor", "init", {});

  const frameUrls = Array.from({ length: 8 }, (_, index) => `/assets/d2ui/protate-${index}.png`);
  let frames = frameUrls;
  let frame = 0;
  let lastX = 0;
  let lastY = 0;
  let visible = false;
  let disposed = false;
  let timerId = null;
  const originalParent = cursor.parentNode;
  const originalNextSibling = cursor.nextSibling;
  const restoredPosition = readLastCursorPosition();

  document.documentElement.classList.add("d2-custom-cursor-ready");
  cursor.classList.add("hidden");
  if (restoredPosition) {
    lastX = restoredPosition.x;
    lastY = restoredPosition.y;
    cursor.style.left = `${lastX}px`;
    cursor.style.top = `${lastY}px`;
    cursor.classList.remove("hidden");
    visible = true;
  }

  preloadFrames(frameUrls).then(loadedFrames => {
    if (disposed) return;
    frames = loadedFrames;
    cursor.src = frames[0];
  });

  const onMouseMove = event => {
    if (disposed) return;
    lastX = event.clientX;
    lastY = event.clientY;
    syncCursorLayer();
    if (!visible) {
      cursor.classList.remove("hidden");
      visible = true;
    }
    cursor.style.left = `${lastX}px`;
    cursor.style.top = `${lastY}px`;
    writeLastCursorPosition(lastX, lastY);
  };

  const onMouseLeave = () => {
    if (disposed) return;
    cursor.classList.add("hidden");
    visible = false;
  };

  const onMouseEnter = () => {
    if (disposed) return;
    cursor.classList.remove("hidden");
    visible = true;
  };

  const syncCursorLayerAfterInteraction = () => {
    window.setTimeout(syncCursorLayer, 0);
  };

  window.addEventListener("mousemove", onMouseMove, { passive: true });
  window.addEventListener("mouseleave", onMouseLeave);
  window.addEventListener("mouseenter", onMouseEnter);
  window.addEventListener("pagehide", dispose, { once: true });
  document.addEventListener("click", syncCursorLayerAfterInteraction, true);
  document.addEventListener("keyup", syncCursorLayerAfterInteraction, true);
  document.addEventListener("toggle", syncCursorLayer, true);
  document.addEventListener("close", syncCursorLayer, true);

  timerId = window.setInterval(() => {
    if (disposed) return;
    frame = (frame + 1) % frames.length;
    cursor.src = frames[frame];
  }, 215);

  function dispose() {
    if (disposed) return;
    disposed = true;
    activeCursorCleanup = null;
    traceNav("cursor", "dispose", {});
    document.documentElement.classList.remove("d2-custom-cursor-ready");
    cursor.classList.add("hidden");
    window.removeEventListener("mousemove", onMouseMove);
    window.removeEventListener("mouseleave", onMouseLeave);
    window.removeEventListener("mouseenter", onMouseEnter);
    document.removeEventListener("click", syncCursorLayerAfterInteraction, true);
    document.removeEventListener("keyup", syncCursorLayerAfterInteraction, true);
    document.removeEventListener("toggle", syncCursorLayer, true);
    document.removeEventListener("close", syncCursorLayer, true);
    restoreCursorParent();
    if (timerId != null) {
      window.clearInterval(timerId);
      timerId = null;
    }
  }

  activeCursorCleanup = dispose;

  function syncCursorLayer() {
    const dialog = document.querySelector("dialog[open]");
    const target = dialog || originalParent || document.body;
    if (target && cursor.parentNode !== target) {
      target.appendChild(cursor);
      traceNav("cursor", "layer.sync", { inDialog: Boolean(dialog) });
    }
  }

  function restoreCursorParent() {
    if (!originalParent || !originalParent.isConnected || cursor.parentNode === originalParent) return;
    const before = originalNextSibling && originalNextSibling.parentNode === originalParent ? originalNextSibling : null;
    originalParent.insertBefore(cursor, before);
  }
}

async function preloadFrames(frameUrls) {
  const loaded = await Promise.all(frameUrls.map(async url => {
    try {
      const response = await fetch(url, { cache: "force-cache" });
      if (!response.ok) throw new Error(`HTTP ${response.status}`);
      const blob = await response.blob();
      return URL.createObjectURL(blob);
    } catch {
      return preloadImage(url);
    }
  }));
  return loaded;
}

function preloadImage(url) {
  return new Promise(resolve => {
    const image = new Image();
    image.onload = () => resolve(url);
    image.onerror = () => resolve(url);
    image.src = url;
  });
}

function readLastCursorPosition() {
  try {
    const raw = sessionStorage.getItem(LAST_CURSOR_POSITION_KEY);
    if (!raw) return null;
    const parsed = JSON.parse(raw);
    const x = Number(parsed.x);
    const y = Number(parsed.y);
    if (!Number.isFinite(x) || !Number.isFinite(y)) return null;
    if (x < 0 || y < 0 || x > window.innerWidth || y > window.innerHeight) return null;
    return { x, y };
  } catch {
    return null;
  }
}

function writeLastCursorPosition(x, y) {
  try {
    sessionStorage.setItem(LAST_CURSOR_POSITION_KEY, JSON.stringify({ x, y }));
  } catch {
    // Best-effort only; cursor animation must never depend on storage.
  }
}
