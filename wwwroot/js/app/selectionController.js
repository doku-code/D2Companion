export function normalizeSelectionToken(value) {
  return String(value ?? "").trim();
}

export function findCharacterSelection(catalog, accountName, characterName) {
  if (!accountName || !characterName || !catalog?.accounts) return null;
  const requestedAccount = normalizeSelectionToken(accountName);
  const requestedCharacter = normalizeSelectionToken(characterName);
  const account = catalog.accounts.find(a => normalizeSelectionToken(a.name) === requestedAccount);
  const character = account?.characters.find(c => normalizeSelectionToken(c.name) === requestedCharacter) ?? null;
  return account && character ? { account, character } : null;
}

export function firstCharacterSelection(catalog) {
  for (const account of catalog?.accounts ?? []) {
    const character = account.characters?.[0];
    if (character) return { account, character };
  }
  return null;
}

export function readStoredSelection(accountKey, characterKey, storage = localStorage) {
  try {
    const account = storage.getItem(accountKey);
    const character = storage.getItem(characterKey);
    return account && character ? { account, character } : null;
  } catch {
    return null;
  }
}

export function persistStoredSelection(accountKey, characterKey, accountName, characterName, storage = localStorage) {
  try {
    storage.setItem(accountKey, accountName);
    storage.setItem(characterKey, characterName);
  } catch {}
}

export function clearStoredSelection(accountKey, characterKey, storage = localStorage) {
  try {
    storage.removeItem(accountKey);
    storage.removeItem(characterKey);
  } catch {}
}

export function clearNoSelectionUrlMarker(accountName, characterName, locationRef = window.location, historyRef = window.history) {
  try {
    const url = new URL(locationRef.href);
    if (url.searchParams.get("selection") !== "none") return;
    url.searchParams.delete("selection");
    url.searchParams.set("account", accountName);
    url.searchParams.set("character", characterName);
    historyRef.replaceState(null, "", `${url.pathname}${url.search}${url.hash}`);
  } catch {}
}
