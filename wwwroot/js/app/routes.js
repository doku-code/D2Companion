const DEBUG_NAV_PARAM = "debugNav";

export function appendDebugNavIfPresent(params, search = window.location.search) {
  try {
    if (new URLSearchParams(search).get(DEBUG_NAV_PARAM) === "1") {
      params.set(DEBUG_NAV_PARAM, "1");
    }
  } catch {}
  return params;
}

export function buildCharacterGearUrl(account, character, realm = "", search = window.location.search) {
  const params = appendDebugNavIfPresent(new URLSearchParams({ account, character }), search);
  if (realm) params.set("realm", realm);
  return `/?${params.toString()}`;
}

export function buildObservedGearUrl(playerKey, search = window.location.search) {
  const params = appendDebugNavIfPresent(new URLSearchParams({ player: playerKey }), search);
  return `/observed/gear?${params.toString()}`;
}
