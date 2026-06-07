export function parseCatalogUpdateEvent(event) {
  try {
    const payload = JSON.parse(event?.data || "{}");
    return {
      area: normalizeArea(payload.area),
      realm: payload.realm || null,
      account: payload.account || null,
      character: payload.character || null,
      source: payload.source || null,
    };
  } catch {
    return { area: "catalog", realm: null, account: null, character: null, source: null };
  }
}

export function affectsMyCharacters(update) {
  return update.area === "my" || update.area === "catalog";
}

export function affectsObservedPlayers(update) {
  return update.area === "observed" || update.area === "catalog";
}

function normalizeArea(area) {
  const value = String(area || "").trim().toLowerCase();
  return value === "my" || value === "observed" || value === "catalog"
    ? value
    : "catalog";
}
