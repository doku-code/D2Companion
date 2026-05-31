import { initD2Cursor } from "../app/cursor.js";
import { initTopSessionStatus } from "../app/sessionStatus.js";
import { formatClassLabel, formatRealmLabel } from "../app/formatters.js";
import { buildCharacterGearUrl } from "../app/routes.js";
import { displayName, escapeAttr, escapeHtml, fmtDateTime, fmtRealm, observedKey } from "../observed/shared.js";

initD2Cursor();
initTopSessionStatus();

const config = window.d2CompanionConfig || {};
const endpoint = config.catalogEndpoint || "/api/catalog";
const myTab = document.getElementById("archivesMyTab");
const observedTab = document.getElementById("archivesObservedTab");
const myRoot = document.getElementById("archivesMyAccounts");
const observedRoot = document.getElementById("archivesObservedAccounts");
const deleteDialog = document.getElementById("archivesDeleteDialog");
const deleteMessage = document.getElementById("archivesDeleteMessage");
const deleteError = document.getElementById("archivesDeleteError");
const deleteConfirm = document.getElementById("archivesDeleteConfirm");
const deleteCancel = document.getElementById("archivesDeleteCancel");
const archiveTabs = Array.from(document.querySelectorAll("[data-archives-tab]"));
const archivePanels = Array.from(document.querySelectorAll("[data-archives-panel]"));

let pendingDelete = null;
let collapsedArchiveAccounts = new Set();

function plural(count, one, many) {
  return `${count} ${count === 1 ? one : many}`;
}

function fmtDate(iso) {
  if (!iso) return "Unknown";
  try {
    const d = new Date(iso);
    return Number.isNaN(d.getTime()) ? "Unknown" : d.toLocaleDateString(undefined, { year: "numeric", month: "short", day: "2-digit" });
  } catch {
    return "Unknown";
  }
}

function fmtMode(c) {
  const parts = [];
  parts.push(c.hardcore ? "HC" : "SC");
  if (c.expansion) parts.push("LoD");
  if (c.ladder) parts.push("Ladder");
  return parts.join(" · ");
}

function renderMyAccounts(accounts) {
  if (!accounts.length) {
    myRoot.innerHTML = `<p class="empty-row">No archived My Characters.</p>`;
    return;
  }

  myRoot.innerHTML = accounts.map(account => {
    const isCollapsed = collapsedArchiveAccounts.has(account.name);
    const arrow = isCollapsed ? ">" : "v";
    const favoriteRank = account.favoriteRank ?? null;
    const favoriteRankClass = favoriteRank ? ` char-favorite--rank-${Math.min(favoriteRank, 3)}` : "";
    return `
    <section class="char-group ${isCollapsed ? "is-collapsed" : ""}" data-archive-account="${escapeAttr(account.name)}">
      <div class="char-group__header-row archives-account-header">
        <button class="char-group__header-toggle archives-account-header__toggle" type="button" data-toggle-archive-account="${escapeAttr(account.name)}">
          <span class="char-group__arrow" aria-hidden="true">${arrow}</span>
          <span class="char-group__name">${escapeHtml(account.name)}</span>
        </button>
        <div class="char-group__header-actions archives-account-header__actions">
          <span class="char-group__meta">${escapeHtml(plural(account.characters?.length || 0, "character", "characters"))}</span>
          <span class="char-group__urgent char-badge--unknown">Archived</span>
          <span class="char-favorite archives-account-favorite ${favoriteRank ? `is-favorite${favoriteRankClass}` : ""}" title="${favoriteRank ? `Favorite rank ${escapeAttr(favoriteRank)}` : "Not favorited"}" aria-label="${favoriteRank ? `Favorite rank ${escapeAttr(favoriteRank)}` : "Not favorited"}">${favoriteRank ? `#${escapeHtml(favoriteRank)}` : "+"}</span>
          <button type="button" class="characters-toolbar-button characters-toolbar-button--restore" data-restore-archived-account data-account="${escapeAttr(account.name)}" title="Restore archived account" aria-label="Restore archived account ${escapeAttr(account.name)}">Restore Account</button>
          <button type="button" class="characters-toolbar-button characters-toolbar-button--danger" data-delete-archived-account data-account="${escapeAttr(account.name)}" title="Delete archived account permanently" aria-label="Delete archived account ${escapeAttr(account.name)} permanently">Delete Account</button>
        </div>
      </div>
      <div class="char-group__body" ${isCollapsed ? "hidden" : ""}>
        <table class="characters-table">
          <thead>
            <tr>
              <th>Character</th>
              <th class="num">Level</th>
              <th>Class</th>
              <th>Mode</th>
              <th class="num">Items</th>
              <th>Realm</th>
              <th>Archived</th>
              <th class="char-row__actions-heading">Actions</th>
            </tr>
          </thead>
          <tbody>${(account.characters || []).map(c => `
            <tr class="char-row">
              <td><a class="char-row__link" href="${escapeAttr(buildCharacterGearUrl(c.account, c.name))}">${escapeHtml(c.name)}</a></td>
              <td class="num">${escapeHtml(c.level ?? "Unknown")}</td>
              <td>${escapeHtml(formatClassLabel(c.className, c.classId))}</td>
              <td>${escapeHtml(fmtMode(c))}</td>
              <td class="num">${escapeHtml(c.itemCount ?? 0)}</td>
              <td>${escapeHtml(formatRealmLabel(c.realm))}</td>
              <td>${escapeHtml(fmtDate(c.archivedAt || c.deletedAt))}</td>
              <td class="char-row__actions">
                <div class="archives-row-actions">
                  <button type="button" class="characters-toolbar-button characters-toolbar-button--restore" data-restore-archived-character data-account="${escapeAttr(account.name)}" data-character="${escapeAttr(c.name)}" title="Restore archived character" aria-label="Restore archived character ${escapeAttr(account.name)} / ${escapeAttr(c.name)}">Restore</button>
                  <button type="button" class="characters-toolbar-button characters-toolbar-button--danger" data-delete-archived-character data-account="${escapeAttr(account.name)}" data-character="${escapeAttr(c.name)}" title="Delete archived character permanently" aria-label="Delete archived character ${escapeAttr(account.name)} / ${escapeAttr(c.name)} permanently">Delete</button>
                </div>
              </td>
            </tr>`).join("")}</tbody>
        </table>
      </div>
    </section>`;
  }).join("");
}

function renderObserved(players) {
  if (!players.length) {
    observedRoot.innerHTML = `<p class="empty-row">No archived Observed Characters.</p>`;
    return;
  }

  observedRoot.innerHTML = `
    <section class="observed-table-wrap">
      <table class="observed-table">
        <thead>
          <tr>
            <th>Player / Character Name</th>
            <th>Level</th>
            <th>Class</th>
            <th>Realm</th>
            <th>Last Seen</th>
            <th>Archived</th>
            <th class="num">Items</th>
            <th class="observed-actions-heading">Actions</th>
          </tr>
        </thead>
        <tbody>${players.map(player => `
          <tr class="observed-row">
            <td>
              <span class="observed-name">${escapeHtml(displayName(player))}</span>
              <span class="observed-debug">uid ${escapeHtml(player.shortPlayerUid || "unknown")}</span>
            </td>
            <td>${escapeHtml(player.level ?? "Unknown")}</td>
            <td>${escapeHtml(player.className || "Unknown")}</td>
            <td>${escapeHtml(fmtRealm(player.realm))}</td>
            <td>${escapeHtml(fmtDateTime(player.seenAt))}</td>
            <td>${escapeHtml(fmtDate(player.archivedAt))}</td>
            <td class="num">${escapeHtml(player.itemCount || player.items?.length || 0)}</td>
            <td class="observed-actions">
              <div class="archives-row-actions">
                <button type="button" class="observed-action-button observed-action-button--restore" data-restore-archived-observed data-observed-key="${escapeAttr(observedKey(player))}" data-observed-name="${escapeAttr(displayName(player))}" title="Restore archived observed player" aria-label="Restore archived observed player ${escapeAttr(displayName(player))}">Restore</button>
                <button type="button" class="observed-action-button observed-action-button--danger" data-delete-archived-observed data-observed-key="${escapeAttr(observedKey(player))}" data-observed-name="${escapeAttr(displayName(player))}" title="Delete archived observed player permanently" aria-label="Delete archived observed player ${escapeAttr(displayName(player))} permanently">Delete</button>
              </div>
            </td>
          </tr>`).join("")}</tbody>
      </table>
    </section>`;
}

function setScope(scope) {
  const isObserved = scope === "observed";
  myTab.classList.toggle("is-active", !isObserved);
  observedTab.classList.toggle("is-active", isObserved);
  myRoot.hidden = isObserved;
  observedRoot.hidden = !isObserved;
}

function setArchiveTab(tab) {
  const target = tab || "accounts";
  for (const button of archiveTabs) {
    const active = button.dataset.archivesTab === target;
    button.classList.toggle("is-active", active);
    if (active) button.setAttribute("aria-current", "page");
    else button.removeAttribute("aria-current");
  }
  for (const panel of archivePanels) {
    panel.hidden = panel.dataset.archivesPanel !== target;
  }
}

function openDelete(kind, data) {
  pendingDelete = { kind, ...data };
  if (deleteMessage) {
    deleteMessage.textContent = kind === "observed"
      ? `Delete archived observed player ${data.name} permanently?`
      : kind === "account"
        ? `Delete every archived character on ${data.account} permanently? Active characters on that account are not affected.`
        : `Delete archived character ${data.account} / ${data.character} permanently?`;
  }
  if (deleteError) {
    deleteError.textContent = "";
    deleteError.hidden = true;
  }
  if (deleteConfirm) deleteConfirm.disabled = false;
  if (deleteDialog?.showModal) deleteDialog.showModal();
}

function closeDelete() {
  pendingDelete = null;
  if (deleteDialog?.close) deleteDialog.close();
}

async function deletePending() {
  if (!pendingDelete || !deleteConfirm) return;
  deleteConfirm.disabled = true;

  try {
    const url = pendingDelete.kind === "observed"
      ? "/api/observed-players"
      : pendingDelete.kind === "account"
        ? "/api/archives/accounts"
        : "/api/archives/characters";
    const body = pendingDelete.kind === "observed"
      ? { observedKey: pendingDelete.observedKey }
      : pendingDelete.kind === "account"
        ? { account: pendingDelete.account }
        : { account: pendingDelete.account, character: pendingDelete.character };
    const res = await fetch(url, {
      method: "DELETE",
      headers: { "Accept": "application/json", "Content-Type": "application/json" },
      body: JSON.stringify(body),
    });
    const payload = await res.json().catch(() => ({}));
    if (!res.ok || payload.ok === false) throw new Error(payload.error || `HTTP ${res.status}`);
    closeDelete();
    await loadCatalog();
  } catch (err) {
    if (deleteError) {
      deleteError.textContent = `Could not delete archived record: ${err.message || err}`;
      deleteError.hidden = false;
    }
    deleteConfirm.disabled = false;
  }
}

async function restoreArchived(kind, data) {
  const url = kind === "observed"
    ? "/api/observed-players/restore"
    : kind === "account"
      ? "/api/accounts/restore"
      : "/api/characters/restore";
  const body = kind === "observed"
    ? { observedKey: data.observedKey }
    : kind === "account"
      ? { account: data.account }
      : { account: data.account, character: data.character };
  const res = await fetch(url, {
    method: "POST",
    headers: { "Accept": "application/json", "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });
  const payload = await res.json().catch(() => ({}));
  if (!res.ok || payload.ok === false) throw new Error(payload.error || `HTTP ${res.status}`);
  await loadCatalog();
}

async function loadCatalog() {
  const res = await fetch(endpoint, { headers: { Accept: "application/json" } });
  if (!res.ok) throw new Error(`HTTP ${res.status}`);
  const catalog = await res.json();
  renderMyAccounts(catalog.archivedAccounts || []);
  renderObserved(catalog.archivedObservedPlayers || []);
}

myTab?.addEventListener("click", () => setScope("my"));
observedTab?.addEventListener("click", () => setScope("observed"));
for (const tab of archiveTabs) {
  tab.addEventListener("click", () => setArchiveTab(tab.dataset.archivesTab));
}
deleteCancel?.addEventListener("click", closeDelete);
deleteConfirm?.addEventListener("click", deletePending);
document.addEventListener("click", event => {
  const accountToggle = event.target.closest("[data-toggle-archive-account]");
  if (accountToggle) {
    event.preventDefault();
    event.stopPropagation();
    const account = accountToggle.dataset.toggleArchiveAccount;
    if (collapsedArchiveAccounts.has(account)) collapsedArchiveAccounts.delete(account);
    else collapsedArchiveAccounts.add(account);
    loadCatalog().catch(error => {
      myRoot.innerHTML = `<p class="empty-row">Could not load archives: ${escapeHtml(error.message)}</p>`;
    });
    return;
  }

  const restoreAccountButton = event.target.closest("[data-restore-archived-account]");
  if (restoreAccountButton) {
    event.preventDefault();
    event.stopPropagation();
    restoreArchived("account", { account: restoreAccountButton.dataset.account }).catch(error => {
      myRoot.innerHTML = `<p class="empty-row">Could not restore archived account: ${escapeHtml(error.message)}</p>`;
    });
    return;
  }

  const accountButton = event.target.closest("[data-delete-archived-account]");
  if (accountButton) {
    event.preventDefault();
    event.stopPropagation();
    openDelete("account", { account: accountButton.dataset.account });
    return;
  }

  const restoreCharacterButton = event.target.closest("[data-restore-archived-character]");
  if (restoreCharacterButton) {
    event.preventDefault();
    event.stopPropagation();
    restoreArchived("character", { account: restoreCharacterButton.dataset.account, character: restoreCharacterButton.dataset.character }).catch(error => {
      myRoot.innerHTML = `<p class="empty-row">Could not restore archived character: ${escapeHtml(error.message)}</p>`;
    });
    return;
  }

  const characterButton = event.target.closest("[data-delete-archived-character]");
  if (characterButton) {
    event.preventDefault();
    event.stopPropagation();
    openDelete("character", { account: characterButton.dataset.account, character: characterButton.dataset.character });
    return;
  }
  const restoreObservedButton = event.target.closest("[data-restore-archived-observed]");
  if (restoreObservedButton) {
    event.preventDefault();
    event.stopPropagation();
    restoreArchived("observed", { observedKey: restoreObservedButton.dataset.observedKey }).catch(error => {
      observedRoot.innerHTML = `<p class="empty-row">Could not restore archived observed player: ${escapeHtml(error.message)}</p>`;
    });
    return;
  }

  const observedButton = event.target.closest("[data-delete-archived-observed]");
  if (observedButton) {
    event.preventDefault();
    event.stopPropagation();
    openDelete("observed", { observedKey: observedButton.dataset.observedKey, name: observedButton.dataset.observedName });
  }
});

loadCatalog().catch(error => {
  myRoot.innerHTML = `<p class="empty-row">Could not load archives: ${escapeHtml(error.message)}</p>`;
});
