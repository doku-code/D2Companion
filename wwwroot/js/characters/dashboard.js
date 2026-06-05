// Account Dashboard expiration view.
// Groups characters by account into collapsible sections, sorts by
// nearest expiration first, and links each character directly to the
// Gear Viewer with an explicit ?account=X&character=Y URL. Dashboard
// clicks do not write one-shot localStorage navigation state.
//
// The custom D2 hand cursor is initialised here too so it stays
// visible on /characters (the gear viewer's app.js doesn't load on
// this page).
//
// Observed players are NOT shown here — they get their own summary
// (without expiration) on the gear viewer side.

import { initD2Cursor } from "../app/cursor.js";
import { initTopSessionStatus } from "../app/sessionStatus.js";
import { createPageTrace } from "../app/navTrace.js";
import { buildCharacterGearUrl } from "../app/routes.js";
import { formatClassLabel, formatRealmLabel, realmSortKey } from "../app/formatters.js";
import { addSseListener } from "../app/eventStream.js";

initD2Cursor();
initTopSessionStatus();
const navTrace = createPageTrace("account-dashboard");

const config = window.d2CompanionConfig || {};
const endpoint = config.catalogEndpoint || "/api/catalog";

const SAMPLE_BANNER = document.getElementById("sampleDataBanner");
const SAMPLE_BANNER_REASON = document.getElementById("sampleDataBannerReason");
const SEARCH_INPUT = document.getElementById("charSearch");
const REALM_FILTER = document.getElementById("charRealmFilter");
const CLASS_FILTER = document.getElementById("charClassFilter");
const LEVEL_FILTER = document.getElementById("charLevelFilter");
const STATUS_FILTER = document.getElementById("charStatusFilter");
const MODE_FILTER = document.getElementById("charModeFilter");
const GROUPS_ROOT = document.getElementById("charactersGroups");
const EXPAND_ALL_BTN = document.getElementById("charExpandAll");
const COLLAPSE_ALL_BTN = document.getElementById("charCollapseAll");
const IMPORT_MULES_BTN = document.getElementById("charImportMules");
const IMPORT_MULES_DIALOG = document.getElementById("importMulesDialog");
const IMPORT_MULES_FORM = document.getElementById("importMulesForm");
const IMPORT_MULES_PATH = document.getElementById("importMulesPath");
const IMPORT_MULES_STATUS = document.getElementById("importMulesStatus");
const IMPORT_MULES_SUBMIT = document.getElementById("importMulesSubmit");
const IMPORT_MULES_CANCEL = document.getElementById("importMulesCancel");
const ARCHIVE_DIALOG = document.getElementById("archiveCharacterDialog");
const ARCHIVE_MESSAGE = document.getElementById("archiveCharacterMessage");
const ARCHIVE_ERROR = document.getElementById("archiveCharacterError");
const ARCHIVE_CONFIRM = document.getElementById("archiveCharacterConfirm");
const ARCHIVE_CANCEL = document.getElementById("archiveCharacterCancel");

const STATUS_MAP = { 0: "unknown", 1: "critical", 2: "warning", 3: "safe" };
const STATUS_LABELS = {
  unknown: "Unknown",
  critical: "Critical",
  warning: "Warning",
  safe: "Safe",
};
const STATUS_RANK = { unknown: 4, safe: 3, warning: 2, critical: 1 };
let allCharacters = [];
let collapsedAccounts = new Set();
let collapsedRealms = new Set();
let pendingArchive = null;
let isSampleCatalog = false;
let didAutoCollapseSafeOnly = false;

function statusKey(c) { return STATUS_MAP[c.expirationStatus] || "unknown"; }

function fmtMode(c) {
  if (c.mode) return c.mode;
  const parts = [];
  parts.push(c.hardcore ? "HC" : "SC");
  if (c.expansion) parts.push("LoD");
  if (c.ladder) parts.push("Ladder");
  return parts.join(" · ");
}

function fmtDate(iso) {
  if (!iso) return "—";
  try {
    const d = new Date(iso);
    if (Number.isNaN(d.getTime())) return "—";
    return d.toLocaleDateString(undefined, { year: "numeric", month: "short", day: "2-digit" });
  } catch { return "—"; }
}

function fmtDays(c) {
  if (c.daysRemaining == null) return "—";
  if (c.daysRemaining === 0) return "Expired";
  return `${c.daysRemaining}`;
}

function fmtRealm(c) {
  return formatRealmLabel(c.realm);
}

function fmtLevel(c) {
  return Number.isFinite(c.level) && c.level > 0 ? String(c.level) : "Unknown";
}

function fmtClass(c) {
  return formatClassLabel(c.className, c.classId);
}

function toPositiveInt(value) {
  const n = Number(value);
  return Number.isFinite(n) && n > 0 ? Math.floor(n) : null;
}

function toNullableInt(value) {
  if (value === null || value === undefined || value === "") return null;
  const n = Number(value);
  return Number.isFinite(n) ? Math.trunc(n) : null;
}

function escapeHtml(s) {
  return String(s ?? "").replace(/[&<>"']/g, m => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[m]));
}
function escapeAttr(s) { return escapeHtml(s); }

function gearViewerUrl(account, character, realm = "") {
  return buildCharacterGearUrl(account, character, realm);
}

function rowHtml(c) {
  const status = statusKey(c);
  const label = STATUS_LABELS[status];
  const url = gearViewerUrl(c.account, c.name, c.realm || "");
  const archiveTitle = c.isSampleData
    ? "Sample data cannot be archived. Import MuleLogger files first."
    : `Archive ${c.account} / ${c.name}`;
  const sampleDisabled = c.isSampleData ? "disabled" : "";
  return `
    <tr class="char-row" data-account="${escapeAttr(c.account)}" data-character="${escapeAttr(c.name)}" data-href="${escapeAttr(url)}">
      <td><a class="char-row__link" href="${escapeAttr(url)}">${escapeHtml(c.name)}</a></td>
      <td class="num">${escapeHtml(fmtLevel(c))}</td>
      <td>${escapeHtml(fmtClass(c))}</td>
      <td>${escapeHtml(fmtMode(c))}</td>
      <td class="num">${c.itemCount ?? 0}</td>
      <td>${escapeHtml(fmtDate(c.lastSeenAt))}</td>
      <td>${escapeHtml(fmtDate(c.expiresAt))}</td>
      <td class="num">${escapeHtml(fmtDays(c))}</td>
      <td><span class="char-badge char-badge--${status}">${label}</span></td>
      <td class="char-row__actions">
        <button
          type="button"
          class="characters-toolbar-button characters-toolbar-button--archive"
          title="${escapeAttr(archiveTitle)}"
          aria-label="${escapeAttr(archiveTitle)}"
          data-archive-character
          data-account="${escapeAttr(c.account)}"
          data-character="${escapeAttr(c.name)}"
          data-realm="${escapeAttr(c.realm || "")}"
          ${sampleDisabled}>Archive</button>
      </td>
    </tr>`;
}

function compareChar(a, b) {
  // Within an account, sort by days remaining ascending (nulls last).
  const av = a.daysRemaining;
  const bv = b.daysRemaining;
  if (av == null && bv == null) return a.name.localeCompare(b.name);
  if (av == null) return 1;
  if (bv == null) return -1;
  if (av !== bv) return av - bv;
  return a.name.localeCompare(b.name);
}

function plural(count, one, many) {
  return `${count} ${count === 1 ? one : many}`;
}

function accountKeyFor(realm, account) {
  return `${realm}\u001f${account}`;
}

function groupHtml(account, chars) {
  const realm = chars[0]?.realmLabel || "Unknown";
  const accountRealm = chars[0]?.realm || "";
  const accountKey = accountKeyFor(realm, account);
  const mostUrgent = chars[0];
  const mostUrgentStatus = mostUrgent ? statusKey(mostUrgent) : "unknown";
  const isCollapsed = collapsedAccounts.has(accountKey);
  const arrow = isCollapsed ? ">" : "v";
  const isFavorite = chars.some(c => c.isFavorite);
  const favoriteRank = chars.find(c => c.favoriteRank)?.favoriteRank ?? null;
  const favoriteRankClass = favoriteRank ? ` char-favorite--rank-${Math.min(favoriteRank, 3)}` : "";
  const isSampleAccount = chars.every(c => c.isSampleData);
  const favoriteTitle = isSampleAccount
    ? "Sample data cannot be favorited. Import MuleLogger files first."
    : `${isFavorite ? `Unfavorite rank ${favoriteRank}` : "Favorite"} ${account}`;
  const archiveTitle = isSampleAccount
    ? "Sample data cannot be archived. Import MuleLogger files first."
    : `Archive every active character on ${account}`;
  const urgentText = mostUrgent
    ? (mostUrgent.daysRemaining == null
        ? "Unknown"
        : (mostUrgent.daysRemaining === 0
            ? "Expired"
            : `Nearest: ${mostUrgent.daysRemaining}d (${mostUrgent.name})`))
    : "";
  return `
    <section class="char-group ${isCollapsed ? "is-collapsed" : ""}" data-account="${escapeAttr(account)}">
      <div class="char-group__header-row">
        <button class="char-group__header-toggle" type="button" data-account-key="${escapeAttr(accountKey)}" data-account="${escapeAttr(account)}">
          <span class="char-group__arrow" aria-hidden="true">${arrow}</span>
          <span class="char-group__name">${escapeHtml(account)}</span>
        </button>
        <div class="char-group__header-actions">
          <span class="char-group__meta">${escapeHtml(plural(chars.length, "character", "characters"))}</span>
          <span class="char-group__urgent char-badge--${mostUrgentStatus}">${escapeHtml(urgentText)}</span>
          <button type="button" class="char-favorite ${isFavorite ? `is-favorite${favoriteRankClass}` : ""}" data-favorite-account data-account="${escapeAttr(account)}" data-realm="${escapeAttr(accountRealm)}" data-favorite="${isFavorite ? "false" : "true"}" title="${escapeAttr(favoriteTitle)}" aria-label="${escapeAttr(favoriteTitle)}" ${isSampleAccount ? "disabled" : ""}>${isFavorite ? `#${favoriteRank}` : "+"}</button>
          <button type="button" class="characters-toolbar-button characters-toolbar-button--archive" data-archive-account data-account="${escapeAttr(account)}" data-realm="${escapeAttr(accountRealm)}" title="${escapeAttr(archiveTitle)}" aria-label="${escapeAttr(archiveTitle)}" ${isSampleAccount ? "disabled" : ""}>Archive Account</button>
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
              <th>Last Seen</th>
              <th>Expires On</th>
              <th class="num">Days Remaining</th>
              <th>Status</th>
              <th class="char-row__actions-heading">Actions</th>
            </tr>
          </thead>
          <tbody>${chars.map(rowHtml).join("")}</tbody>
        </table>
      </div>
    </section>`;
}

function realmHtml(realm, accountEntries) {
  const chars = accountEntries.flatMap(([, list]) => list);
  const accountCount = accountEntries.length;
  const isCollapsed = collapsedRealms.has(realm);
  const arrow = isCollapsed ? ">" : "v";
  return `
    <section class="realm-group ${isCollapsed ? "is-collapsed" : ""}" data-realm="${escapeAttr(realm)}">
      <button class="realm-group__header" type="button" data-toggle-realm="${escapeAttr(realm)}">
        <span class="realm-group__arrow" aria-hidden="true">${arrow}</span>
        <span class="realm-group__name">${escapeHtml(realm)}</span>
        <span class="realm-group__summary">${escapeHtml(plural(chars.length, "character", "characters"))} across ${escapeHtml(plural(accountCount, "account", "accounts"))}</span>
      </button>
      <div class="realm-group__body" ${isCollapsed ? "hidden" : ""}>
        ${accountEntries.map(([acc, charsForAccount]) => groupHtml(acc, charsForAccount)).join("")}
      </div>
    </section>`;
}

function applyFilters() {
  const q = (SEARCH_INPUT.value || "").trim().toLowerCase();
  const realm = REALM_FILTER.value;
  const charClass = CLASS_FILTER.value;
  const minLevel = toPositiveInt(LEVEL_FILTER.value);
  const status = STATUS_FILTER.value;
  const mode = MODE_FILTER.value;

  const filtered = allCharacters.filter(c => {
    const haystack = [c.account, c.name, fmtRealm(c), fmtClass(c), fmtLevel(c)]
      .join(" ")
      .toLowerCase();
    if (q && !haystack.includes(q)) return false;
    if (realm !== "all" && fmtRealm(c).toLowerCase() !== realm) return false;
    if (charClass !== "all" && fmtClass(c).toLowerCase() !== charClass) return false;
    if (minLevel !== null && !(Number.isFinite(c.level) && c.level > minLevel)) return false;
    if (status !== "all" && statusKey(c) !== status) return false;
    if (mode !== "all" && (c.mode || "").toLowerCase() !== mode) return false;
    return true;
  });

  // Group by realm, then account.
  const realms = new Map();
  for (const c of filtered) {
    c.realmLabel = fmtRealm(c);
    if (!realms.has(c.realmLabel)) realms.set(c.realmLabel, new Map());
    const accounts = realms.get(c.realmLabel);
    if (!accounts.has(c.account)) accounts.set(c.account, []);
    accounts.get(c.account).push(c);
  }

  const sortedRealms = [...realms.entries()]
    .map(([realmName, accounts]) => {
      for (const [, chars] of accounts) chars.sort(compareChar);
      const sortedAccounts = [...accounts.entries()].sort(([a, ca], [b, cb]) => {
        const favA = ca.find(c => c.favoriteRank)?.favoriteRank ?? null;
        const favB = cb.find(c => c.favoriteRank)?.favoriteRank ?? null;
        if (favA && favB && favA !== favB) return favA - favB;
        if (favA !== favB) return favA ? -1 : 1;
        const ua = ca[0]?.daysRemaining;
        const ub = cb[0]?.daysRemaining;
        if (ua == null && ub == null) return a.localeCompare(b);
        if (ua == null) return 1;
        if (ub == null) return -1;
        return ua - ub || a.localeCompare(b);
      });
      return [realmName, sortedAccounts];
    })
    .sort(([a], [b]) => realmSortKey(a) - realmSortKey(b) || a.localeCompare(b));

  if (sortedRealms.length === 0) {
    GROUPS_ROOT.innerHTML = `<p class="empty-row">No characters match the current filters.</p>`;
  } else {
    GROUPS_ROOT.innerHTML = sortedRealms.map(([realmName, accounts]) => realmHtml(realmName, accounts)).join("");
    wireGroupHandlers();
  }
}

function wireGroupHandlers() {
  GROUPS_ROOT.querySelectorAll("[data-toggle-realm]").forEach(btn => {
    btn.addEventListener("click", () => {
      const realm = btn.dataset.toggleRealm;
      if (collapsedRealms.has(realm)) collapsedRealms.delete(realm);
      else collapsedRealms.add(realm);
      applyFilters();
    });
  });
  GROUPS_ROOT.querySelectorAll("[data-account-key]").forEach(btn => {
    btn.addEventListener("click", () => {
      const accountKey = btn.dataset.accountKey;
      if (collapsedAccounts.has(accountKey)) collapsedAccounts.delete(accountKey);
      else collapsedAccounts.add(accountKey);
      applyFilters();
    });
  });
  GROUPS_ROOT.querySelectorAll("[data-archive-character]").forEach(btn => {
    btn.addEventListener("click", event => {
      event.preventDefault();
      event.stopPropagation();
      openArchiveDialog({ kind: "character", account: btn.dataset.account, character: btn.dataset.character, realm: btn.dataset.realm || "" });
    });
  });
  GROUPS_ROOT.querySelectorAll("[data-archive-account]").forEach(btn => {
    btn.addEventListener("click", event => {
      event.preventDefault();
      event.stopPropagation();
      openArchiveDialog({ kind: "account", account: btn.dataset.account, realm: btn.dataset.realm || "" });
    });
  });
  GROUPS_ROOT.querySelectorAll("[data-favorite-account]").forEach(btn => {
    btn.addEventListener("click", event => {
      event.preventDefault();
      event.stopPropagation();
      toggleFavorite(btn.dataset.account, btn.dataset.realm || "", btn.dataset.favorite === "true");
    });
  });
  GROUPS_ROOT.querySelectorAll(".char-row").forEach(tr => {
    tr.addEventListener("click", event => {
      if (event.target.closest("a,button")) return;
      navTrace.log("row.click", {
        account: tr.dataset.account,
        character: tr.dataset.character,
        href: tr.dataset.href,
      });
      window.location.assign(tr.dataset.href);
    });
  });
}

function populateRealmFilter() {
  const current = REALM_FILTER.value || "all";
  const realms = new Set(["USEast", "USWest", "Europe", "Asia"]);
  const hasUnknownRealm = allCharacters.some(c => fmtRealm(c) === "Unknown");
  if (hasUnknownRealm) realms.add("Unknown");
  for (const c of allCharacters) {
    const label = fmtRealm(c);
    if (label && label !== "Unknown") realms.add(label);
  }
  REALM_FILTER.querySelectorAll("option:not([value='all'])").forEach(o => o.remove());
  for (const r of [...realms]) {
    const opt = document.createElement("option");
    opt.value = r.toLowerCase();
    opt.textContent = r;
    REALM_FILTER.appendChild(opt);
  }
  REALM_FILTER.value = [...REALM_FILTER.options].some(option => option.value === current) ? current : "all";
}

function populateModeFilter() {
  const current = MODE_FILTER.value || "all";
  const modes = new Set();
  for (const c of allCharacters) if (c.mode) modes.add(c.mode);
  MODE_FILTER.querySelectorAll("option:not([value='all'])").forEach(o => o.remove());
  for (const m of [...modes].sort()) {
    const opt = document.createElement("option");
    opt.value = m.toLowerCase();
    opt.textContent = m;
    MODE_FILTER.appendChild(opt);
  }
  MODE_FILTER.value = [...MODE_FILTER.options].some(option => option.value === current) ? current : "all";
}

function renderSampleDataBanner(catalog) {
  if (!SAMPLE_BANNER) return;
  if (catalog && catalog.isSampleData === true) {
    if (SAMPLE_BANNER_REASON && catalog.sampleDataReason) {
      SAMPLE_BANNER_REASON.textContent = catalog.sampleDataReason + " Days-remaining values are unknown for sample characters.";
    }
    SAMPLE_BANNER.hidden = false;
  } else {
    SAMPLE_BANNER.hidden = true;
  }
}

function autoCollapseSafeOnly() {
  // First load: if an account has at least one Critical or Warning
  // character, leave it expanded; if every character is Safe or
  // Unknown, collapse it by default so the page focuses on what
  // needs attention.
  const byAccount = new Map();
  for (const c of allCharacters) {
    const realm = fmtRealm(c);
    const key = accountKeyFor(realm, c.account);
    if (!byAccount.has(key)) byAccount.set(key, []);
    byAccount.get(key).push(c);
  }
  for (const [accountKey, chars] of byAccount) {
    const hasUrgent = chars.some(c => {
      const k = statusKey(c);
      return k === "critical" || k === "warning";
    });
    if (!hasUrgent) collapsedAccounts.add(accountKey);
  }
}

function setImportStatus(message, kind = "") {
  if (!IMPORT_MULES_STATUS) return;
  IMPORT_MULES_STATUS.textContent = message || "";
  IMPORT_MULES_STATUS.hidden = !message;
  IMPORT_MULES_STATUS.classList.toggle("is-error", kind === "error");
  IMPORT_MULES_STATUS.classList.toggle("is-success", kind === "success");
}

function openImportDialog() {
  setImportStatus("");
  if (IMPORT_MULES_SUBMIT) IMPORT_MULES_SUBMIT.disabled = false;
  if (IMPORT_MULES_DIALOG?.showModal) {
    IMPORT_MULES_DIALOG.showModal();
    IMPORT_MULES_PATH?.focus();
  } else {
    const sourcePath = window.prompt("Paste a MuleLogger file or folder path:");
    if (sourcePath) submitMuleImport(sourcePath);
  }
}

function closeImportDialog() {
  if (IMPORT_MULES_DIALOG?.close) IMPORT_MULES_DIALOG.close();
}

function importSummaryText(summary) {
  const lines = [
    `Imported ${summary.importedCharacters ?? 0} character(s), ${summary.importedItems ?? 0} item(s), across ${summary.importedAccounts ?? 0} account(s).`,
  ];
  if (summary.skippedFiles) lines.push(`Skipped ${summary.skippedFiles} file(s).`);
  if (summary.warnings?.length) {
    lines.push("");
    lines.push(summary.warnings.slice(0, 5).join("\n"));
  }
  return lines.join("\n");
}

async function submitMuleImport(sourcePath) {
  const path = (sourcePath ?? IMPORT_MULES_PATH?.value ?? "").trim();
  if (!path) {
    setImportStatus("Choose a MuleLogger file or folder path.", "error");
    IMPORT_MULES_PATH?.focus();
    return;
  }

  if (IMPORT_MULES_SUBMIT) IMPORT_MULES_SUBMIT.disabled = true;
  setImportStatus("Importing MuleLogger files...");

  try {
    const res = await fetch("/api/import/mule-files", {
      method: "POST",
      headers: {
        "Accept": "application/json",
        "Content-Type": "application/json",
      },
      body: JSON.stringify({ sourcePath: path }),
    });
    const summary = await res.json().catch(() => ({}));
    if (!res.ok || summary.success === false) {
      const message = summary.errors?.length
        ? summary.errors.join("\n")
        : `Import failed with HTTP ${res.status}.`;
      throw new Error(message);
    }

    setImportStatus(importSummaryText(summary), "success");
    await loadCatalog();
  } catch (err) {
    setImportStatus(`Could not import MuleLogger files: ${err.message || err}`, "error");
  } finally {
    if (IMPORT_MULES_SUBMIT) IMPORT_MULES_SUBMIT.disabled = false;
  }
}

function openArchiveDialog(request) {
  pendingArchive = request;
  if (ARCHIVE_MESSAGE) {
    ARCHIVE_MESSAGE.textContent = request.kind === "account"
      ? `Archive every active character on ${request.account}?`
      : `Archive ${request.account} / ${request.character}?`;
  }
  if (ARCHIVE_ERROR) {
    ARCHIVE_ERROR.textContent = "";
    ARCHIVE_ERROR.hidden = true;
  }
  if (ARCHIVE_CONFIRM) ARCHIVE_CONFIRM.disabled = false;
  if (ARCHIVE_DIALOG?.showModal) {
    ARCHIVE_DIALOG.showModal();
  } else if (window.confirm(ARCHIVE_MESSAGE?.textContent || "Archive this record?")) {
    archivePending();
  }
}

function closeArchiveDialog() {
  pendingArchive = null;
  if (ARCHIVE_DIALOG?.close) ARCHIVE_DIALOG.close();
}

async function archivePending() {
  if (!pendingArchive || !ARCHIVE_CONFIRM) return;
  ARCHIVE_CONFIRM.disabled = true;
  if (ARCHIVE_ERROR) {
    ARCHIVE_ERROR.textContent = "";
    ARCHIVE_ERROR.hidden = true;
  }

  try {
    const account = pendingArchive.account;
    const character = pendingArchive.character;
    const isAccountArchive = pendingArchive.kind === "account";
    const realm = pendingArchive.realm || "";
    const res = await fetch(isAccountArchive ? "/api/accounts/archive" : "/api/characters/archive", {
      method: "POST",
      headers: {
        "Accept": "application/json",
        "Content-Type": "application/json",
      },
      body: JSON.stringify(isAccountArchive ? { account, realm } : { account, character, realm }),
    });
    const payload = await res.json().catch(() => ({}));
    if (!res.ok || payload.ok === false) {
      throw new Error(payload.error || `HTTP ${res.status}`);
    }

    closeArchiveDialog();
    await loadCatalog();
  } catch (err) {
    if (ARCHIVE_ERROR) {
      ARCHIVE_ERROR.textContent = `Could not archive: ${err.message || err}`;
      ARCHIVE_ERROR.hidden = false;
    }
    ARCHIVE_CONFIRM.disabled = false;
  }
}

async function toggleFavorite(account, realm, isFavorite) {
  const res = await fetch("/api/accounts/favorite", {
    method: "POST",
    headers: { "Accept": "application/json", "Content-Type": "application/json" },
    body: JSON.stringify({ account, realm, isFavorite }),
  });
  const payload = await res.json().catch(() => ({}));
  if (!res.ok || payload.ok === false) throw new Error(payload.error || `HTTP ${res.status}`);
  await loadCatalog();
}

async function loadCatalog(reason = "initial") {
  try {
    const startedAt = performance.now();
    const previousScrollY = window.scrollY;
    navTrace.log("catalog.fetch.start", { endpoint, reason });
    const res = await fetch(endpoint, { headers: { Accept: "application/json" } });
    if (!res.ok) throw new Error(`HTTP ${res.status}`);
    const catalog = await res.json();
    navTrace.log("catalog.fetch.complete", {
      reason,
      elapsedMs: Math.round(performance.now() - startedAt),
      accounts: catalog.accounts?.length ?? 0,
      characters: (catalog.accounts || []).reduce((n, account) => n + (account.characters?.length || 0), 0),
      items: catalog.items?.length ?? 0,
      observedPlayers: catalog.observedPlayers?.length ?? 0,
    });
    renderSampleDataBanner(catalog);
    isSampleCatalog = catalog?.isSampleData === true;

    const list = [];
    for (const acct of catalog.accounts || []) {
      for (const ch of acct.characters || []) {
        list.push({
          account: acct.name,
          name: ch.name,
          realm: ch.realm || "",
          level: toPositiveInt(ch.level),
          classId: toNullableInt(ch.classId),
          className: ch.className || "",
          mode: ch.mode || "",
          hardcore: !!ch.hardcore,
          expansion: !!ch.expansion,
          ladder: !!ch.ladder,
          itemCount: ch.itemCount || 0,
          isFavorite: !!acct.isFavorite,
          favoriteRank: acct.favoriteRank ?? null,
          isSampleData: isSampleCatalog,
          lastSeenAt: ch.lastSeenAt || null,
          expiresAt: ch.expiresAt || null,
          daysRemaining: ch.daysRemaining ?? null,
          expirationStatus: ch.expirationStatus ?? 0,
        });
      }
    }
    allCharacters = list;
    populateRealmFilter();
    populateModeFilter();
    if (!didAutoCollapseSafeOnly) {
      autoCollapseSafeOnly();
      didAutoCollapseSafeOnly = true;
    }
    applyFilters();
    if (reason !== "initial") window.scrollTo({ top: previousScrollY });
  } catch (err) {
    navTrace.error("catalog.fetch.error", err);
    GROUPS_ROOT.innerHTML = `<p class="empty-row">Could not load characters: ${escapeHtml(err.message)}</p>`;
  }
}

SEARCH_INPUT.addEventListener("input", applyFilters);
REALM_FILTER.addEventListener("change", applyFilters);
CLASS_FILTER.addEventListener("change", applyFilters);
LEVEL_FILTER.addEventListener("input", applyFilters);
STATUS_FILTER.addEventListener("change", applyFilters);
MODE_FILTER.addEventListener("change", applyFilters);
EXPAND_ALL_BTN.addEventListener("click", () => { collapsedAccounts = new Set(); applyFilters(); });
COLLAPSE_ALL_BTN.addEventListener("click", () => {
  collapsedAccounts = new Set(allCharacters.map(c => accountKeyFor(fmtRealm(c), c.account)));
  applyFilters();
});
IMPORT_MULES_BTN?.addEventListener("click", openImportDialog);
IMPORT_MULES_FORM?.addEventListener("submit", event => {
  event.preventDefault();
  submitMuleImport();
});
IMPORT_MULES_CANCEL?.addEventListener("click", closeImportDialog);
ARCHIVE_CANCEL?.addEventListener("click", closeArchiveDialog);
ARCHIVE_CONFIRM?.addEventListener("click", archivePending);

addSseListener("items-updated", () => loadCatalog("items-updated"));
loadCatalog();
