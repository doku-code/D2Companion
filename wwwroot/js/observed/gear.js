import { initD2Cursor } from "../app/cursor.js";
import { initTopSessionStatus } from "../app/sessionStatus.js";
import { getTooltipFontMode, hideTooltip, initTooltipFontPreference, showTooltip, toggleTooltipFontMode } from "../app/tooltip.js";
import { itemKey } from "../app/itemAssets.js";
import { renderObservedEquipmentScene } from "../app/d2SceneRenderer.js";
import { createPageTrace } from "../app/navTrace.js";
import { STORAGE_KEYS } from "../app/storageKeys.js";
import { addSseListener } from "../app/eventStream.js";
import {
  displayName,
  escapeAttr,
  escapeHtml,
  fmtDateTime,
  fmtRealm,
  fmtUnknown,
  matchesObservedSearch,
  observedKey,
  observedPlayers,
  renderSampleDataBanner,
} from "./shared.js";

initD2Cursor();
initTopSessionStatus();
const navTrace = createPageTrace("observed-gear");

const config = window.d2CompanionConfig || {};
const endpoint = config.catalogEndpoint || "/api/catalog";
const search = document.getElementById("observedSearch");
const list = document.getElementById("observedPlayerList");
const view = document.getElementById("observedGearView");
const title = document.getElementById("observedTitle");
const meta = document.getElementById("observedMeta");
const hint = document.getElementById("observedHint");
const tooltip = document.getElementById("tooltip");
const tooltipFontToggle = document.getElementById("tooltipFontToggle");

const state = {
  players: [],
  selectedKey: null,
  weaponSet: 1,
};

function selectedPlayer() {
  return state.players.find(player => observedKey(player) === state.selectedKey) || state.players[0] || null;
}

function visiblePlayers() {
  const query = (search?.value || "").trim().toLowerCase();
  return state.players.filter(player => matchesObservedSearch(player, query));
}

function renderSelector() {
  const players = visiblePlayers();
  if (!players.length) {
    list.innerHTML = `<div class="empty">No observed players found.</div>`;
    return;
  }

  list.innerHTML = players.map(player => {
    const key = observedKey(player);
    return `
      <button class="character-button observed-player-button ${key === state.selectedKey ? "active" : ""}" data-observed-player="${escapeAttr(key)}">
        <span class="character-name">${escapeHtml(displayName(player))}</span>
        <span class="character-days char-badge char-badge--unknown">${escapeHtml(fmtDateTime(player.seenAt))}</span>
      </button>
    `;
  }).join("");
}

function renderGear() {
  const player = selectedPlayer();
  if (!player) {
    title.textContent = "No observed players";
    meta.textContent = "Join a game with other players to capture equipped gear.";
    hint.textContent = "No observed gear available.";
    view.innerHTML = `<div class="empty">No observed players captured yet.</div>`;
    return;
  }

  state.selectedKey = observedKey(player);
  const equipped = (player.items || []).filter(item => item.storage === "equipped");
  const seenBy = [player.observedByAccount, player.observedByCharacter].filter(Boolean).join(" / ") || "Unknown";

  title.textContent = displayName(player);
  meta.textContent = observedMetaLine(player, seenBy);
  hint.textContent = `${equipped.length} equipped item${equipped.length === 1 ? "" : "s"} captured. Hover an item to inspect it.`;
  const renderStartedAt = performance.now();
  navTrace.log("renderObservedGear.start", {
    player: displayName(player),
    key: state.selectedKey,
    itemCount: equipped.length,
    weaponSet: state.weaponSet,
  });
  view.innerHTML = equipped.length
    ? renderObservedEquipmentScene(equipped, { weaponSet: state.weaponSet })
    : `<div class="empty">No equipped gear captured for this player.</div>`;
  navTrace.log("renderObservedGear.complete", {
    elapsedMs: Math.round(performance.now() - renderStartedAt),
    scenePresent: Boolean(view.querySelector(".d2-scene")),
    itemCount: view.querySelectorAll(".d2-scene-item").length,
  });
  bindItemEvents(player);
}

function bindItemEvents(player) {
  const itemMap = new Map((player.items || []).map(item => [itemKey(item), item]));
  view.querySelectorAll("[data-item-key]").forEach(node => {
    const show = event => showTooltip(tooltip, itemMap.get(event.currentTarget.dataset.itemKey), event.currentTarget);
    node.addEventListener("mouseenter", show);
    node.addEventListener("mouseover", show);
    node.addEventListener("pointerenter", show);
    node.addEventListener("pointerover", show);
    node.addEventListener("mouseleave", () => hideTooltip(tooltip));
    node.addEventListener("pointerleave", () => hideTooltip(tooltip));
  });
}

function observedMetaLine(player, seenBy) {
  return [
    fmtUnknown(player.className),
    player.level ? `Level ${player.level}` : "Level Unknown",
    fmtRealm(player.realm),
    `Last Seen ${fmtDateTime(player.seenAt)}`,
    `Seen By ${seenBy}`,
  ].join(" \u00b7 ");
}

function renderTooltipFontToggle() {
  if (!tooltipFontToggle) return;
  const mode = getTooltipFontMode();
  tooltipFontToggle.textContent = mode === "modern" ? "Tooltip Font: Exocet" : "Tooltip Font: D2";
  tooltipFontToggle.setAttribute("aria-pressed", mode === "modern" ? "true" : "false");
}

function render() {
  renderSelector();
  renderGear();
}

list.addEventListener("click", event => {
  const button = event.target.closest("[data-observed-player]");
  if (!button) return;
  state.selectedKey = button.dataset.observedPlayer;
  navTrace.log("selector.click", { selectedKey: state.selectedKey });
  try { localStorage.setItem(STORAGE_KEYS.observedPlayer, state.selectedKey); } catch {}
  render();
});

view.addEventListener("click", event => {
  if (!event.target.closest("[data-toggle-weapon-set]")) return;
  state.weaponSet = state.weaponSet === 2 ? 1 : 2;
  renderGear();
});

search?.addEventListener("input", renderSelector);
tooltipFontToggle?.addEventListener("click", () => {
  toggleTooltipFontMode();
  hideTooltip(tooltip);
  renderTooltipFontToggle();
});

initTooltipFontPreference();
renderTooltipFontToggle();

async function loadCatalog(reason = "initial") {
  const fetchStartedAt = performance.now();
  navTrace.log("catalog.fetch.start", { endpoint, reason });
  try {
    const response = await fetch(endpoint);
    const catalog = await response.json();
    navTrace.log("catalog.fetch.complete", {
      reason,
      elapsedMs: Math.round(performance.now() - fetchStartedAt),
      accounts: catalog.accounts?.length ?? 0,
      characters: (catalog.accounts || []).reduce((n, account) => n + (account.characters?.length || 0), 0),
      items: catalog.items?.length ?? 0,
      observedPlayers: catalog.observedPlayers?.length ?? 0,
    });
    renderSampleDataBanner(catalog);
    state.players = observedPlayers(catalog);
    const params = new URLSearchParams(window.location.search);
    state.selectedKey = params.get("player");
    if (!state.selectedKey) {
      try { state.selectedKey = localStorage.getItem(STORAGE_KEYS.observedPlayer); } catch {}
    }
    if (!state.players.some(player => observedKey(player) === state.selectedKey)) {
      state.selectedKey = observedKey(state.players[0]);
    }
    render();
  } catch (error) {
    navTrace.error("catalog.fetch.error", error);
    view.innerHTML = `<div class="empty">Could not load observed gear: ${escapeHtml(error.message)}</div>`;
  }
}

addSseListener("items-updated", () => loadCatalog("items-updated"));
loadCatalog();
