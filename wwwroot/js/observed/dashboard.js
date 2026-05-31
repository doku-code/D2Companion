import { initD2Cursor } from "../app/cursor.js";
import { initTopSessionStatus } from "../app/sessionStatus.js";
import { createPageTrace } from "../app/navTrace.js";
import { CLASS_FILTER_OPTIONS, realmSortKey } from "../app/formatters.js";
import { buildObservedGearUrl } from "../app/routes.js";
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
const navTrace = createPageTrace("observed-dashboard");

const config = window.d2CompanionConfig || {};
const endpoint = config.catalogEndpoint || "/api/catalog";
const search = document.getElementById("observedSearch");
const realmFilter = document.getElementById("observedRealmFilter");
const classFilter = document.getElementById("observedClassFilter");
const levelFilter = document.getElementById("observedLevelFilter");
const rows = document.getElementById("observedRows");
const counts = document.getElementById("observedCounts");
const expandAll = document.getElementById("observedExpandAll");
const collapseAll = document.getElementById("observedCollapseAll");
const archiveDialog = document.getElementById("archiveObservedDialog");
const archiveMessage = document.getElementById("archiveObservedMessage");
const archiveError = document.getElementById("archiveObservedError");
const archiveConfirm = document.getElementById("archiveObservedConfirm");
const archiveCancel = document.getElementById("archiveObservedCancel");

let players = [];
let collapsedRealms = new Set();
let pendingArchive = null;
let isSampleCatalog = false;

function plural(count, one, many) {
  return `${count} ${count === 1 ? one : many}`;
}

function classFilterValue(player) {
  const label = String(player.className ?? "").trim() || "Unknown";
  return label === "Unknown" ? "unknown" : label.toLowerCase();
}

function activeLevelFilter() {
  const value = Number.parseInt(levelFilter?.value || "", 10);
  return Number.isFinite(value) ? value : null;
}

function filteredPlayers() {
  const query = (search?.value || "").trim().toLowerCase();
  const realm = realmFilter?.value || "all";
  const classValue = classFilter?.value || "all";
  const minLevel = activeLevelFilter();

  return players.filter(player => {
    if (!matchesObservedSearch(player, query)) return false;
    if (realm !== "all" && fmtRealm(player.realm).toLowerCase() !== realm) return false;
    if (classValue !== "all" && classFilterValue(player) !== classValue) return false;
    const level = Number(player.level);
    if (minLevel !== null && !(Number.isFinite(level) && level > minLevel)) return false;
    return true;
  });
}

function rowHtml(player) {
  const key = observedKey(player);
  const seenBy = [player.observedByAccount, player.observedByCharacter].filter(Boolean).join(" / ") || "Unknown";
  const archiveTitle = isSampleCatalog
    ? "Sample data cannot be archived. Import MuleLogger files or capture live data first."
    : `Archive ${displayName(player)}`;
  return `
    <tr class="observed-row" data-observed-player="${escapeAttr(key)}">
      <td>
        <span class="observed-name">${escapeHtml(displayName(player))}</span>
        <span class="observed-debug">uid ${escapeHtml(player.shortPlayerUid || "unknown")}</span>
      </td>
      <td>${escapeHtml(player.level ?? "Unknown")}</td>
      <td>${escapeHtml(fmtUnknown(player.className))}</td>
      <td>${escapeHtml(fmtDateTime(player.seenAt))}</td>
      <td>${escapeHtml(seenBy)}</td>
      <td class="num">${escapeHtml(player.snapshotCount || 0)}</td>
      <td class="observed-actions">
        <button
          type="button"
          class="observed-action-button observed-action-button--archive"
          title="${escapeAttr(archiveTitle)}"
          aria-label="${escapeAttr(archiveTitle)}"
          data-archive-observed
          data-observed-key="${escapeAttr(key)}"
          data-observed-name="${escapeAttr(displayName(player))}"
          ${isSampleCatalog ? "disabled" : ""}>Archive</button>
      </td>
    </tr>
  `;
}

function realmHtml(realm, realmPlayers) {
  const isCollapsed = collapsedRealms.has(realm);
  const arrow = isCollapsed ? ">" : "v";
  return `
    <section class="realm-group observed-realm-group ${isCollapsed ? "is-collapsed" : ""}" data-realm="${escapeAttr(realm)}">
      <button class="realm-group__header observed-realm-group__header" type="button" data-toggle-observed-realm="${escapeAttr(realm)}">
        <span class="realm-group__arrow" aria-hidden="true">${arrow}</span>
        <span class="realm-group__name">${escapeHtml(realm)}</span>
        <span class="realm-group__summary">${escapeHtml(plural(realmPlayers.length, "observed player", "observed players"))} seen</span>
      </button>
      <div class="realm-group__body" ${isCollapsed ? "hidden" : ""}>
        <section class="observed-table-wrap">
          <table class="observed-table">
            <thead>
              <tr>
                <th>Player / Character Name</th>
                <th>Level</th>
                <th>Class</th>
                <th>Last Seen</th>
                <th>Seen By</th>
                <th class="num">Observations</th>
                <th class="observed-actions-heading">Actions</th>
              </tr>
            </thead>
            <tbody>${realmPlayers.map(rowHtml).join("")}</tbody>
          </table>
        </section>
      </div>
    </section>
  `;
}

function render() {
  const filtered = filteredPlayers();
  counts.textContent = `${filtered.length} observed player${filtered.length === 1 ? "" : "s"}`;

  if (!filtered.length) {
    rows.innerHTML = `<p class="empty-row">No observed players match the current filters.</p>`;
    return;
  }

  const realms = new Map();
  for (const player of filtered) {
    const realm = fmtRealm(player.realm);
    if (!realms.has(realm)) realms.set(realm, []);
    realms.get(realm).push(player);
  }

  rows.innerHTML = [...realms.entries()]
    .sort(([a], [b]) => realmSortKey(a) - realmSortKey(b) || a.localeCompare(b))
    .map(([realm, realmPlayers]) => realmHtml(realm, realmPlayers))
    .join("");
}

function populateFilters() {
  if (realmFilter) {
    const current = realmFilter.value || "all";
    const realms = new Set(["USEast", "USWest", "Europe", "Asia"]);
    if (players.some(player => fmtRealm(player.realm) === "Unknown")) realms.add("Unknown");
    for (const player of players) {
      const label = fmtRealm(player.realm);
      if (label && label !== "Unknown") realms.add(label);
    }
    realmFilter.innerHTML = `<option value="all">All</option>` + [...realms]
      .sort((a, b) => realmSortKey(a) - realmSortKey(b) || a.localeCompare(b))
      .map(realm => `<option value="${escapeAttr(realm.toLowerCase())}">${escapeHtml(realm)}</option>`)
      .join("");
    realmFilter.value = [...realmFilter.options].some(option => option.value === current) ? current : "all";
  }

  if (classFilter) {
    const current = classFilter.value || "all";
    classFilter.innerHTML = `
      <option value="all">All</option>
      ${CLASS_FILTER_OPTIONS.map(([value, label]) => `<option value="${escapeAttr(value)}">${escapeHtml(label)}</option>`).join("")}
    `;
    classFilter.value = [...classFilter.options].some(option => option.value === current) ? current : "all";
  }
}

function observedGearUrl(playerKey) {
  return buildObservedGearUrl(playerKey);
}

rows.addEventListener("click", event => {
  const realmButton = event.target.closest("[data-toggle-observed-realm]");
  if (realmButton) {
    const realm = realmButton.dataset.toggleObservedRealm;
    if (collapsedRealms.has(realm)) collapsedRealms.delete(realm);
    else collapsedRealms.add(realm);
    render();
    return;
  }

  const archiveButton = event.target.closest("[data-archive-observed]");
  if (archiveButton) {
    event.preventDefault();
    event.stopPropagation();
    openArchiveDialog(archiveButton.dataset.observedKey, archiveButton.dataset.observedName);
    return;
  }

  const row = event.target.closest("[data-observed-player]");
  if (!row) return;
  const href = observedGearUrl(row.dataset.observedPlayer);
  navTrace.log("row.click", {
    observedPlayer: row.dataset.observedPlayer,
    href,
  });
  try { localStorage.setItem(STORAGE_KEYS.observedPlayer, row.dataset.observedPlayer); } catch {}
  window.location.href = href;
});

function openArchiveDialog(observedKeyValue, observedName) {
  pendingArchive = { observedKey: observedKeyValue, observedName };
  if (archiveMessage) archiveMessage.textContent = `Archive observed player ${observedName}?`;
  if (archiveError) {
    archiveError.textContent = "";
    archiveError.hidden = true;
  }
  if (archiveConfirm) archiveConfirm.disabled = false;
  if (archiveDialog?.showModal) {
    archiveDialog.showModal();
  } else if (window.confirm(`Archive observed player ${observedName}?`)) {
    archivePendingObservedPlayer();
  }
}

function closeArchiveDialog() {
  pendingArchive = null;
  if (archiveDialog?.close) archiveDialog.close();
}

async function archivePendingObservedPlayer() {
  if (!pendingArchive || !archiveConfirm) return;
  archiveConfirm.disabled = true;
  if (archiveError) {
    archiveError.textContent = "";
    archiveError.hidden = true;
  }

  try {
    const res = await fetch("/api/observed-players/archive", {
      method: "POST",
      headers: {
        "Accept": "application/json",
        "Content-Type": "application/json",
      },
      body: JSON.stringify({ observedKey: pendingArchive.observedKey }),
    });
    const payload = await res.json().catch(() => ({}));
    if (!res.ok || payload.ok === false) {
      throw new Error(payload.error || `HTTP ${res.status}`);
    }

    players = players.filter(player => observedKey(player) !== pendingArchive.observedKey);
    try {
      if (localStorage.getItem(STORAGE_KEYS.observedPlayer) === pendingArchive.observedKey) {
        localStorage.removeItem(STORAGE_KEYS.observedPlayer);
      }
    } catch {}
    closeArchiveDialog();
    render();
  } catch (err) {
    if (archiveError) {
      archiveError.textContent = `Could not archive observed player: ${err.message || err}`;
      archiveError.hidden = false;
    }
    archiveConfirm.disabled = false;
  }
}

search?.addEventListener("input", render);
realmFilter?.addEventListener("change", render);
classFilter?.addEventListener("change", render);
levelFilter?.addEventListener("input", render);
expandAll?.addEventListener("click", () => {
  collapsedRealms.clear();
  render();
});
collapseAll?.addEventListener("click", () => {
  collapsedRealms = new Set(players.map(player => fmtRealm(player.realm)));
  render();
});
archiveCancel?.addEventListener("click", closeArchiveDialog);
archiveConfirm?.addEventListener("click", archivePendingObservedPlayer);

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
    isSampleCatalog = catalog?.isSampleData === true;
    players = observedPlayers(catalog);
    populateFilters();
    render();
  } catch (error) {
    navTrace.error("catalog.fetch.error", error);
    rows.innerHTML = `<p class="empty-row">Could not load observed players: ${escapeHtml(error.message)}</p>`;
  }
}

addSseListener("items-updated", () => loadCatalog("items-updated"));
loadCatalog();
