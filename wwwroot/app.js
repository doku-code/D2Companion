import { bootstrapCompanionApp } from "./js/app/appController.js";
import { initD2Cursor } from "./js/app/cursor.js";
import { initTopSessionStatus } from "./js/app/sessionStatus.js";
import { initD2SceneDebug } from "./js/app/d2SceneDebug.js";
import { addSseListener } from "./js/app/eventStream.js";

bootstrapCompanionApp(window.d2CompanionConfig || {});
initD2Cursor();
initTopSessionStatus();
initD2SceneDebug();
initStyxStatusBar();

const SNAPSHOT_ONLINE_MS = 5 * 60 * 1000;

function initStyxStatusBar() {
  const dot   = document.getElementById("styxDot");
  const label = document.getElementById("styxLabel");
  const sync  = document.getElementById("styxLastSync");
  if (!dot || !label) return;

  function render(data) {
    const online = isStyxOnline(data);
    const hasError = typeof data.lastError === "string" && data.lastError.length > 0;

    // States, in order of precedence:
    //   1. Online (running OR recent snapshot) — always wins; snapshots are
    //      arriving so the user is functionally fine, even if a managed Styx
    //      crashed earlier and left an error string behind.
    //   2. Error (lastError set, not online) — surface the message so the
    //      user knows why no snapshots are arriving, e.g. port conflict.
    //   3. Offline (no recent activity, no recorded error).
    if (online) {
      dot.className = "styx-dot styx-dot--running";
      label.textContent = "Styx: Online";
    } else if (hasError) {
      dot.className = "styx-dot styx-dot--stopped";
      label.textContent = "Styx: Error";
    } else {
      dot.className = "styx-dot styx-dot--stopped";
      label.textContent = "Styx: Offline";
    }

    if (online && data.lastSnapshotAt) {
      const d = new Date(data.lastSnapshotAt);
      sync.textContent = `Last sync: ${d.toLocaleTimeString()} · ${data.totalItemsReceived} items captured`;
    } else if (hasError) {
      sync.textContent = data.lastError;
    } else if (data.lastSnapshotAt) {
      const d = new Date(data.lastSnapshotAt);
      sync.textContent = `Last sync: ${d.toLocaleTimeString()} · ${data.totalItemsReceived} items captured`;
    } else {
      sync.textContent = "No snapshot yet — join a D2 game";
    }
  }

  function isStyxOnline(data, now = Date.now()) {
    if (data.styxRunning === true) return true;
    if (!data.lastSnapshotAt) return false;

    const lastSnapshot = Date.parse(data.lastSnapshotAt);
    return Number.isFinite(lastSnapshot) && now - lastSnapshot <= SNAPSHOT_ONLINE_MS;
  }

  // 1. Initial state — one REST call so the bar has something on first paint.
  fetch("/api/status")
    .then(res => res.json())
    .then(render)
    .catch(() => {
      dot.className = "styx-dot styx-dot--stopped";
      label.textContent = "Styx: Error";
    });

  // 2. Live updates — pushed by the server via SSE whenever StyxStatus changes
  //    (proxy started/stopped, snapshot ingested). No polling.
  addSseListener("styx-status", e => {
    try { render(JSON.parse(e.data)); } catch { /* ignore malformed frame */ }
  });
}
