export function normalizeSelectionToken(value) {
  return String(value ?? "").trim();
}

export function findCharacterSelection(catalog, accountName, characterName, realm = null) {
  if (!accountName || !characterName || !catalog?.accounts) return null;
  const requestedAccount = normalizeSelectionToken(accountName);
  const requestedCharacter = normalizeSelectionToken(characterName);
  const requestedRealm = realm === null || realm === undefined ? null : normalizeSelectionToken(realm);
  const account = catalog.accounts.find(a =>
    normalizeSelectionToken(a.name) === requestedAccount &&
    (requestedRealm === null || normalizeSelectionToken(a.realm || "") === requestedRealm));
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

export function readStoredSelection(accountKey, characterKey, realmKey = null, storage = localStorage) {
  try {
    const account = storage.getItem(accountKey);
    const character = storage.getItem(characterKey);
    const realm = realmKey ? storage.getItem(realmKey) : null;
    return account && character ? { account, character, realm } : null;
  } catch {
    return null;
  }
}

export function persistStoredSelection(accountKey, characterKey, accountName, characterName, realm = "", realmKey = null, storage = localStorage) {
  try {
    storage.setItem(accountKey, accountName);
    storage.setItem(characterKey, characterName);
    if (realmKey) storage.setItem(realmKey, realm || "");
  } catch {}
}

export function clearStoredSelection(accountKey, characterKey, realmKey = null, storage = localStorage) {
  try {
    storage.removeItem(accountKey);
    storage.removeItem(characterKey);
    if (realmKey) storage.removeItem(realmKey);
  } catch {}
}

export function clearNoSelectionUrlMarker(accountName, characterName, realm = "", locationRef = window.location, historyRef = window.history) {
  try {
    const url = new URL(locationRef.href);
    if (url.searchParams.get("selection") !== "none") return;
    url.searchParams.delete("selection");
    url.searchParams.set("account", accountName);
    url.searchParams.set("character", characterName);
    if (realm) url.searchParams.set("realm", realm);
    historyRef.replaceState(null, "", `${url.pathname}${url.search}${url.hash}`);
  } catch {}
}
