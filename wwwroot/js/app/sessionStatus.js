import { addSseListener } from "./eventStream.js";
import { traceNav } from "./navTrace.js";
import { STORAGE_KEYS } from "./storageKeys.js";

const STORAGE_KEY = STORAGE_KEYS.sessionStatus;

export function initTopSessionStatus() {
  const dot = document.getElementById("appStatusDot");
  const message = document.getElementById("appStatusMessage");
  const styxToggle = document.getElementById("styxPowerToggle");
  const styxMessage = document.getElementById("styxPowerMessage");
  const startupDialog = document.getElementById("styxStartupDialog");
  const startupRemember = document.getElementById("styxStartupRemember");
  const startupYes = document.getElementById("styxStartupYes");
  const startupNo = document.getElementById("styxStartupNo");
  const startupError = document.getElementById("styxStartupError");
  if (!dot || !message) return;

  let current = readCachedStatus();
  let timerId = null;
  let startupPromptHandled = false;

  function setStatus(data) {
    current = normalizeStatus(data);
    traceNav("session-status", "set", {
      state: current.state,
      characterName: current.characterName,
      gameName: current.gameName,
      gameStartedAt: current.gameStartedAt,
      styxRunning: current.styxRunning,
    });
    writeCachedStatus(current);
    render();
    maybeShowStartupPrompt();

    if (timerId) clearInterval(timerId);
    timerId = current.state === "in-game" ? setInterval(render, 1000) : null;
  }

  function render() {
    const view = toView(current);
    dot.className = `app-status-dot app-status-dot--${view.color}`;
    message.textContent = view.text;
    renderStyxControl();
  }

  function renderStyxControl() {
    if (!styxToggle || !styxMessage) return;
    const isRunning = current.styxRunning === true;
    const hasError = !!current.lastError;
    styxToggle.textContent = isRunning ? "Stop Styx" : "Start Styx";
    styxToggle.dataset.styxState = hasError ? "error" : isRunning ? "running" : "off";
    styxMessage.textContent = hasError
      ? "Error / Node missing"
      : isRunning
        ? "Live capture running"
        : "Live capture off";
  }

  async function startStyx() {
    if (startupError) {
      startupError.textContent = "";
      startupError.hidden = true;
    }
    if (styxToggle) styxToggle.textContent = "Starting";
    const res = await fetch("/api/styx/start", { method: "POST", headers: { "Accept": "application/json" } });
    const payload = await res.json().catch(() => ({}));
    if (!res.ok || payload.ok === false) {
      throw new Error(payload.error || `HTTP ${res.status}`);
    }
    if (payload.status) setStatus(payload.status);
  }

  async function stopStyx() {
    if (current.state === "in-game" || current.state === "waiting" || current.state === "connecting" || current.state === "character-selection" || current.state === "lobby") {
      const ok = window.confirm("Stopping Styx can disconnect Diablo II from Battle.net if the game is currently routed through the local proxy.");
      if (!ok) return;
    }
    const res = await fetch("/api/styx/stop", { method: "POST", headers: { "Accept": "application/json" } });
    const payload = await res.json().catch(() => ({}));
    if (!res.ok || payload.ok === false) {
      throw new Error(payload.error || `HTTP ${res.status}`);
    }
    if (payload.status) setStatus(payload.status);
  }

  function readStartupPreference() {
    try { return localStorage.getItem(STORAGE_KEYS.styxStartupPreference); } catch { return null; }
  }

  function writeStartupPreference(value) {
    try { localStorage.setItem(STORAGE_KEYS.styxStartupPreference, value); } catch {}
  }

  function maybeShowStartupPrompt() {
    if (startupPromptHandled || !startupDialog) return;
    startupPromptHandled = true;
    const pref = readStartupPreference();
    if (pref === "start") {
      startStyx().catch(error => {
        traceNav("session-status", "styx.start.error", { message: error.message }, "error");
      });
      return;
    }
    if (pref === "off") return;
    if (startupDialog.showModal) startupDialog.showModal();
  }

  render();

  fetch("/api/status")
    .then(res => res.json())
    .then(setStatus)
    .catch(error => {
      traceNav("session-status", "fetch.error", { message: error.message }, "error");
      setStatus({ sessionState: "none" });
    });

  addSseListener("styx-status", event => {
    try { setStatus(JSON.parse(event.data)); } catch { /* ignore malformed frame */ }
  });

  styxToggle?.addEventListener("click", () => {
    const action = current.styxRunning ? stopStyx : startStyx;
    action().catch(error => {
      traceNav("session-status", "styx.toggle.error", { message: error.message }, "error");
      setStatus({ ...current, lastError: error.message, styxRunning: false });
    });
  });

  startupYes?.addEventListener("click", () => {
    if (startupRemember?.checked) writeStartupPreference("start");
    startStyx()
      .then(() => startupDialog?.close())
      .catch(error => {
        if (startupError) {
          startupError.textContent = error.message || String(error);
          startupError.hidden = false;
        }
      });
  });

  startupNo?.addEventListener("click", () => {
    if (startupRemember?.checked) writeStartupPreference("off");
    startupDialog?.close();
  });
}

export function normalizeStatus(data) {
  const rawState = typeof data?.sessionState === "string" ? data.sessionState : "";
  const state = ["in-game", "lobby", "character-selection", "connecting", "waiting"].includes(rawState)
    ? rawState
    : "none";

  return {
    state,
    accountName: clean(data?.accountName),
    characterName: clean(data?.characterName),
    gameName: clean(data?.gameName),
    gameStartedAt: clean(data?.gameStartedAt),
    styxRunning: data?.styxRunning === true,
    lastError: clean(data?.lastError)
  };
}

export function toView(status, now = Date.now()) {
  if (status?.lastError) {
    return { color: "red", text: "Live Capture Error" };
  }

  if (status?.state === "in-game") {
    const name = status.characterName || "Character";
    const game = status.gameName || "Game";
    return {
      color: "green",
      text: `${name} In Game ${game} ${formatElapsed(status.gameStartedAt, now)}`
    };
  }

  if (status?.state === "lobby") {
    return {
      color: "yellow",
      text: status.characterName ? `${status.characterName} In Lobby` : "Lobby"
    };
  }

  if (status?.state === "character-selection") {
    return {
      color: "yellow",
      text: status.accountName ? `Character Selection ${status.accountName}` : "Character Selection"
    };
  }

  if (status?.state === "connecting") {
    return { color: "yellow", text: "Connecting to Battle.net" };
  }

  if (status?.state === "waiting") {
    return { color: "yellow", text: "Waiting To Join A Game" };
  }

  return { color: "red", text: "No Character Connected" };
}

export function formatElapsed(startedAt, now = Date.now()) {
  const start = Date.parse(startedAt || "");
  const elapsed = Number.isFinite(start) ? Math.max(0, Math.floor((now - start) / 1000)) : 0;
  const hours = Math.floor(elapsed / 3600);
  const minutes = Math.floor((elapsed % 3600) / 60);
  const seconds = elapsed % 60;
  return `${pad2(hours)}:${pad2(minutes)}:${pad2(seconds)}`;
}

function clean(value) {
  return typeof value === "string" && value.trim().length > 0 ? value.trim() : null;
}

function pad2(value) {
  return String(value).padStart(2, "0");
}

function readCachedStatus() {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    return raw ? normalizeStatus(JSON.parse(raw)) : { state: "none" };
  } catch {
    return { state: "none" };
  }
}

function writeCachedStatus(status) {
  try { localStorage.setItem(STORAGE_KEY, JSON.stringify(status)); } catch {}
}
