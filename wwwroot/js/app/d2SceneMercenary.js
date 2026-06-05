export const mercenaryIconAssets = {
  rogue: "/assets/mercenary/rogue-scout.png",
  desert: "/assets/mercenary/desert-mercenary.png",
  ironWolf: "/assets/mercenary/iron-wolf.png",
  barbarian: "/assets/mercenary/barbarian.png",
  unknown: "/assets/mercenary/unknown-mercenary.png"
};

export function detectMercenaryIconType(options) {
  const rawType = normalizeMercenaryType(options.mercenaryType);
  if (rawType) return rawType;

  const actType = mercenaryTypeFromAct(options.mercenaryAct);
  if (actType) return actType;

  const classType = mercenaryTypeFromClassId(options.mercenaryClassId);
  if (classType) return classType;

  const kindType = mercenaryTypeFromKind(options.mercenaryKind);
  if (kindType) return kindType;

  return detectMercenaryIconTypeFromEquipment(options.mercenaryIconItems || options.mercenaryItems || []);
}

export function hasCapturedMercenary(character, mercenaryItems = []) {
  if (mercenaryItems?.length > 0) return true;
  if (!character) return false;

  if (!hasValue(character.mercenaryTypeSource)) return false;

  return Boolean(
    normalizeMercenaryType(character.mercenaryType)
    || mercenaryTypeFromAct(character.mercenaryAct)
    || mercenaryTypeFromClassId(character.mercenaryClassId)
    || mercenaryTypeFromKind(character.mercenaryKind));
}

function hasValue(value) {
  return value !== null && value !== undefined && String(value).trim() !== "";
}

function normalizeMercenaryType(value) {
  const key = String(value || "").trim().toLowerCase().replace(/[\s_-]+/g, "");
  if (!key) return null;
  if (key === "rogue" || key === "roguescout" || key === "act1") return "rogue";
  if (key === "desert" || key === "desertmercenary" || key === "act2") return "desert";
  if (key === "ironwolf" || key === "easternsorceror" || key === "act3") return "ironWolf";
  if (key === "barbarian" || key === "act5") return "barbarian";
  return null;
}

function mercenaryTypeFromAct(act) {
  switch (Number(act)) {
  case 1: return "rogue";
  case 2: return "desert";
  case 3: return "ironWolf";
  case 5: return "barbarian";
  default: return null;
  }
}

function mercenaryTypeFromClassId(classId) {
  switch (Number(classId)) {
  case 271: return "rogue";
  case 338: return "desert";
  case 359: return "ironWolf";
  case 561: return "barbarian";
  default: return null;
  }
}

function mercenaryTypeFromKind(kind) {
  const numeric = Number(kind);
  if (!Number.isFinite(numeric)) return null;
  if (numeric >= 0 && numeric <= 5) return "rogue";
  if (numeric >= 6 && numeric <= 14) return "desert";
  if (numeric >= 15 && numeric <= 23) return "ironWolf";
  if (numeric >= 24 && numeric <= 29) return "barbarian";
  return mercenaryTypeFromClassId(numeric);
}

function detectMercenaryIconTypeFromEquipment(mercenaryItems) {
  if (!mercenaryItems?.length) return "unknown";
  if (mercenaryItems.some(item => item.x === 5)) return "ironWolf";

  const weaponText = mercenaryItems
    .filter(item => item.x === 4 || item.x === 5)
    .map(item => `${item.title || ""} ${item.image || ""}`.toLowerCase())
    .join(" ");

  if (/\b(bow|crossbow|short bow|long bow|reflex|stag bow|matriarchal bow|amazon bow)\b/.test(weaponText)) return "rogue";
  if (/\b(sword|blade|sabre|saber|scimitar|falchion|zweihander|colossus)\b/.test(weaponText)) return "barbarian";
  if (/\b(polearm|spear|pike|halberd|partizan|thresher|cryptic axe|war scythe|yari|lance|voulge)\b/.test(weaponText)) return "desert";

  return "unknown";
}
