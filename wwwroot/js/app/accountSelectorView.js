import { escapeAttr, escapeHtml } from "./html.js";
import { makeItemFilter, itemsBelongingTo } from "./filterItems.js";

const EXPIRATION_STATUS_KEY = { 0: "unknown", 1: "critical", 2: "warning", 3: "safe" };

export function expStatusKey(character) {
  return EXPIRATION_STATUS_KEY[character?.expirationStatus] || "unknown";
}

export function renderAccountsHtml(state) {
  const filter = makeItemFilter(state.query, state.storageFilter);
  const accounts = state.catalog.accounts
    .filter(account => {
      const accountText = `${account.name} ${account.characters.map(c => c.name).join(" ")}`;
      return (state.query && accountText.toLowerCase().includes(state.query)) ||
        account.characters.some(char => itemsBelongingTo(state.catalog.items, char).some(filter));
    })
    .slice(0, 160)
    .map(account => ({
      account,
      nearest: nearestCharacter(account.characters),
      characters: account.characters.filter(char => {
        const nameText = `${account.name} ${char.name}`;
        return (state.query && nameText.toLowerCase().includes(state.query)) ||
          itemsBelongingTo(state.catalog.items, char).some(filter);
      })
    }))
    .sort((a, b) => compareCharactersByExpiration(a.nearest, b.nearest, a.account.name, b.account.name));

  const html = accounts.map(({ account, nearest, characters }) => {
    const collapsed = accountIsCollapsed(state, account.name);
    const arrow = collapsed ? "▸" : "▾";
    const nearestStatus = expStatusKey(nearest);
    const nearestText = accountNearestText(nearest);
    const sortedChars = [...characters].sort((a, b) => compareCharactersByExpiration(a, b, a.name, b.name));
    const chars = sortedChars.map(char => {
      const status = expStatusKey(char);
      return `
        <button class="character-button ${state.character === char ? "active" : ""}" data-account="${escapeAttr(account.name)}" data-character="${escapeAttr(char.name)}">
          <span class="character-name">${escapeHtml(char.name)}</span>
          <span class="character-days char-badge char-badge--${status}">${escapeHtml(expBadgeText(char))}</span>
        </button>`;
    }).join("");

    return `
      <article class="account ${collapsed ? "is-collapsed" : ""}">
        <button class="account-button" type="button" data-account="${escapeAttr(account.name)}" data-toggle-account="${escapeAttr(account.name)}">
          <span class="account-arrow" aria-hidden="true">${arrow}</span>
          <span class="account-name">${escapeHtml(account.name)}</span>
          <span class="account-count">${characters.length}</span>
          <span class="account-nearest char-badge char-badge--${nearestStatus}">${escapeHtml(nearestText)}</span>
        </button>
        <div class="characters" ${collapsed ? "hidden" : ""}>${chars}</div>
      </article>
    `;
  }).join("");

  return html || `<div class="empty">No account found.</div>`;
}

export function nearestCharacter(chars) {
  if (!chars || chars.length === 0) return null;
  return [...chars].sort((a, b) => compareCharactersByExpiration(a, b, a.name, b.name))[0];
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

function accountIsCollapsed(state, account) {
  if (state.character && state.character.account === account) return false;
  return state.collapsedAccounts.has(account);
}

function compareCharactersByExpiration(a, b, fallbackA, fallbackB) {
  const av = a?.daysRemaining;
  const bv = b?.daysRemaining;
  if (av == null && bv == null) return fallbackA.localeCompare(fallbackB);
  if (av == null) return 1;
  if (bv == null) return -1;
  return av - bv || fallbackA.localeCompare(fallbackB);
}
