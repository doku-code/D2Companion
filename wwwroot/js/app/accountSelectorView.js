import { escapeAttr, escapeHtml } from "./html.js";
import { makeItemFilter, itemsBelongingTo } from "./filterItems.js";
import { formatRealmLabel, realmSortKey } from "./formatters.js";

const EXPIRATION_STATUS_KEY = { 0: "unknown", 1: "critical", 2: "warning", 3: "safe" };

export function expStatusKey(character) {
  return EXPIRATION_STATUS_KEY[character?.expirationStatus] || "unknown";
}

export function renderAccountsHtml(state) {
  const filter = makeItemFilter(state.query, state.storageFilter);
  const hasTextQuery = Boolean(state.query);
  const hasItemFilter = Boolean(state.storageFilter && state.storageFilter !== "all");
  const forceOpen = hasTextQuery || hasItemFilter;
  const accounts = state.catalog.accounts
    .filter(account => {
      if (!hasTextQuery && !hasItemFilter) return account.characters.length > 0;
      const realmText = formatRealmLabel(account.realm);
      const accountText = `${realmText} ${account.name} ${account.characters.map(c => c.name).join(" ")}`;
      return (hasTextQuery && accountText.toLowerCase().includes(state.query)) ||
        account.characters.some(char => itemsBelongingTo(state.catalog.items, char).some(filter));
    })
    .slice(0, 160)
    .map(account => ({
      account,
      nearest: nearestCharacter(account.characters),
      characters: account.characters.filter(char => {
        if (!hasTextQuery && !hasItemFilter) return true;
        const nameText = `${formatRealmLabel(char.realm || account.realm)} ${account.name} ${char.name}`;
        return (hasTextQuery && nameText.toLowerCase().includes(state.query)) ||
          itemsBelongingTo(state.catalog.items, char).some(filter);
      })
    }))
    .sort(compareAccountEntries);

  const realms = new Map();
  for (const entry of accounts) {
    const realm = formatRealmLabel(entry.account.realm || entry.characters[0]?.realm);
    if (!realms.has(realm)) realms.set(realm, []);
    realms.get(realm).push(entry);
  }

  const html = [...realms.entries()]
    .sort(([a], [b]) => realmSortKey(a) - realmSortKey(b) || a.localeCompare(b))
    .map(([realm, entries]) => realmHtml(state, realm, entries, forceOpen))
    .join("");

  return html || `<div class="empty">No account found.</div>`;
}

export function nearestCharacter(chars) {
  if (!chars || chars.length === 0) return null;
  return [...chars].sort((a, b) => compareCharactersByExpiration(a, b, a.name, b.name))[0];
}

function realmHtml(state, realm, entries, forceOpen) {
  const chars = entries.flatMap(entry => entry.characters);
  const collapsed = !forceOpen && state.collapsedRealms.has(realm);
  const arrow = collapsed ? ">" : "v";

  return `
    <section class="realm-group account-selector-realm ${collapsed ? "is-collapsed" : ""}" data-realm="${escapeAttr(realm)}">
      <button class="realm-group__header account-selector-realm__header" type="button" data-toggle-realm="${escapeAttr(realm)}">
        <span class="realm-group__arrow" aria-hidden="true">${arrow}</span>
        <span class="realm-group__name">${escapeHtml(realm)}</span>
        <span class="realm-group__summary">${escapeHtml(plural(chars.length, "character", "characters"))}</span>
      </button>
      <div class="realm-group__body account-selector-realm__body" ${collapsed ? "hidden" : ""}>
        ${entries.map(entry => accountHtml(state, entry, forceOpen)).join("")}
      </div>
    </section>
  `;
}

function accountHtml(state, { account, nearest, characters }, forceOpen) {
  const accountKey = accountKeyFor(account);
  const collapsed = accountIsCollapsed(state, account, forceOpen);
  const arrow = collapsed ? ">" : "v";
  const nearestStatus = expStatusKey(nearest);
  const nearestText = accountNearestText(nearest);
  const sortedChars = [...characters].sort((a, b) => compareCharactersByExpiration(a, b, a.name, b.name));
  const chars = sortedChars.map(char => {
    const status = expStatusKey(char);
    return `
      <button class="character-button ${state.character === char ? "active" : ""}" data-account="${escapeAttr(account.name)}" data-character="${escapeAttr(char.name)}" data-realm="${escapeAttr(char.realm || account.realm || "")}">
        <span class="character-name">${escapeHtml(char.name)}</span>
        <span class="character-days char-badge char-badge--${status}">${escapeHtml(expBadgeText(char))}</span>
      </button>`;
  }).join("");

  return `
    <article class="account ${collapsed ? "is-collapsed" : ""}">
      <button class="account-button" type="button" data-account="${escapeAttr(account.name)}" data-realm="${escapeAttr(account.realm || "")}" data-toggle-account="${escapeAttr(accountKey)}">
        <span class="account-arrow" aria-hidden="true">${arrow}</span>
        <span class="account-name">${escapeHtml(account.name)}</span>
        <span class="account-count">${characters.length}</span>
        <span class="account-nearest char-badge char-badge--${nearestStatus}">${escapeHtml(nearestText)}</span>
      </button>
      <div class="characters" ${collapsed ? "hidden" : ""}>${chars}</div>
    </article>
  `;
}

function expBadgeText(character) {
  if (character?.daysRemaining == null) return "\u2014";
  if (character.daysRemaining === 0) return "Expired";
  return `${character.daysRemaining}d`;
}

function accountNearestText(character) {
  if (!character) return "";
  if (character.daysRemaining == null) return "Unknown";
  return expBadgeText(character);
}

function accountIsCollapsed(state, account, forceOpen = false) {
  if (forceOpen) return false;
  if (state.character && state.character.account === account.name && String(state.character.realm || "") === String(account.realm || "")) return false;
  return state.collapsedAccounts.has(accountKeyFor(account));
}

function accountKeyFor(account) {
  return `${account.realm || ""}\u001f${account.name}`;
}

function favoriteRank(account) {
  const rank = Number(account?.favoriteRank);
  return Number.isFinite(rank) && rank > 0 ? rank : null;
}

function compareAccountEntries(a, b) {
  const favA = favoriteRank(a.account);
  const favB = favoriteRank(b.account);
  if (favA && favB && favA !== favB) return favA - favB;
  if (favA !== favB) return favA ? -1 : 1;
  return compareCharactersByExpiration(a.nearest, b.nearest, a.account.name, b.account.name);
}

function compareCharactersByExpiration(a, b, fallbackA, fallbackB) {
  const av = a?.daysRemaining;
  const bv = b?.daysRemaining;
  if (av == null && bv == null) return fallbackA.localeCompare(fallbackB);
  if (av == null) return 1;
  if (bv == null) return -1;
  return av - bv || fallbackA.localeCompare(fallbackB);
}

function plural(count, one, many) {
  return `${count} ${count === 1 ? one : many}`;
}
