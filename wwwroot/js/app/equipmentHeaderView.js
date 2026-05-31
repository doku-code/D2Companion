import { formatRealmLabel } from "./formatters.js";

export function renderEquipmentHeader(els, state) {
  if (els.summary && state.catalog?.totals) {
    const t = state.catalog.totals;
    els.summary.textContent = `${t.accounts} account${t.accounts === 1 ? "" : "s"} · ${t.characters} character${t.characters === 1 ? "" : "s"} · ${t.items} item${t.items === 1 ? "" : "s"}`;
  }

  if (state.tab === "observed") {
    const observed = state.catalog?.observedPlayers ?? [];
    els.characterTitle.textContent = "Observed Players";
    els.characterMeta.textContent = observed.length
      ? `${observed.length} player${observed.length === 1 ? "" : "s"} captured from recent Styx snapshots.`
      : "No observed players captured yet.";
    els.contextLabel.textContent = "Players in Game";
    return;
  }

  if (!state.character) {
    els.characterTitle.textContent = "Select a character";
    els.characterMeta.textContent = "Pick a character from the sidebar or the Account Dashboard.";
    els.contextLabel.textContent = "My Characters";
    return;
  }

  const character = state.character;
  els.contextLabel.textContent = character.account;
  els.characterTitle.textContent = character.name;
  els.characterMeta.textContent = characterMetaLine(character);
}

export function characterMetaLine(character) {
  return [
    displayClass(character),
    displayLevel(character),
    displayRealm(character)
  ].join(" · ");
}

export function displayLevel(character) {
  const level = Number(character?.level);
  return Number.isFinite(level) && level > 0 ? `Level ${level}` : "Level Unknown";
}

export function displayClass(character) {
  const name = String(character?.className || "").trim();
  return name.length ? name : "Class Unknown";
}

export function displayRealm(character) {
  return formatRealmLabel(character?.realm);
}
