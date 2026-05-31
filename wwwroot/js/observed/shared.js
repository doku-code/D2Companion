import { formatRealmLabel, formatUnknown as formatUnknownValue } from "../app/formatters.js";

export function observedKey(player) {
  return player?.observedKey || player?.playerUid || "";
}

export function displayName(player) {
  return player?.displayName || player?.playerName || `Unknown Player ${player?.shortPlayerUid || ""}`.trim();
}

export function fmtUnknown(value) {
  return formatUnknownValue(value);
}

export function fmtRealm(value) {
  return formatRealmLabel(value);
}

export function fmtDateTime(iso) {
  if (!iso) return "Unknown";
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return "Unknown";
  return date.toLocaleString(undefined, {
    year: "numeric",
    month: "short",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
  });
}

export function escapeHtml(value) {
  return String(value ?? "").replace(/[&<>"']/g, char => ({
    "&": "&amp;",
    "<": "&lt;",
    ">": "&gt;",
    '"': "&quot;",
    "'": "&#39;",
  }[char]));
}

export function escapeAttr(value) {
  return escapeHtml(value);
}

export function observedPlayers(catalog) {
  return [...(catalog?.observedPlayers || [])].sort((a, b) =>
    Date.parse(b.seenAt || 0) - Date.parse(a.seenAt || 0));
}

export function matchesObservedSearch(player, query) {
  if (!query) return true;
  const haystack = [
    displayName(player),
    player.playerName,
    player.realm,
    fmtRealm(player.realm),
    player.className,
    player.gameName,
    player.observedByAccount,
    player.observedByCharacter,
    player.shortPlayerUid,
  ].join(" ").toLowerCase();
  return haystack.includes(query);
}

export function renderSampleDataBanner(catalog) {
  const banner = document.getElementById("sampleDataBanner");
  const reason = document.getElementById("sampleDataBannerReason");
  if (!banner) return;
  if (catalog?.isSampleData === true) {
    if (reason) reason.textContent = catalog.sampleDataReason || "Sample / demo data active.";
    banner.hidden = false;
  } else {
    banner.hidden = true;
  }
}
