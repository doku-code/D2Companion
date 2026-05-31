export const REALM_LABELS = {
  "0": "Unknown",
  "1": "USEast",
  "2": "USWest",
  "3": "Europe",
  "4": "Asia",
  useast: "USEast",
  east: "USEast",
  uswest: "USWest",
  west: "USWest",
  europe: "Europe",
  asia: "Asia",
};

export const REALM_SORT_ORDER = { USEast: 1, USWest: 2, Europe: 3, Asia: 4, Unknown: 99 };

export const CLASS_LABELS = {
  "0": "Amazon",
  "1": "Sorceress",
  "2": "Necromancer",
  "3": "Paladin",
  "4": "Barbarian",
  "5": "Druid",
  "6": "Assassin",
  amazon: "Amazon",
  assassin: "Assassin",
  barbarian: "Barbarian",
  druid: "Druid",
  necromancer: "Necromancer",
  paladin: "Paladin",
  sorceress: "Sorceress",
};

export const CLASS_FILTER_OPTIONS = [
  ["amazon", "Amazon"],
  ["assassin", "Assassin"],
  ["barbarian", "Barbarian"],
  ["druid", "Druid"],
  ["necromancer", "Necromancer"],
  ["paladin", "Paladin"],
  ["sorceress", "Sorceress"],
];

export function formatUnknown(value) {
  const text = String(value ?? "").trim();
  return text.length ? text : "Unknown";
}

export function formatRealmLabel(value) {
  const text = String(value ?? "").trim();
  if (!text.length) return "Unknown";
  return REALM_LABELS[text.toLowerCase()] || text;
}

export function realmSortKey(realm) {
  return REALM_SORT_ORDER[realm] || 50;
}

export function formatClassLabel(value, classId = null) {
  const className = String(value ?? "").trim();
  if (className.length) return CLASS_LABELS[className.toLowerCase()] || className;
  if (Number.isFinite(classId)) return CLASS_LABELS[String(classId)] || "Unknown";
  return "Unknown";
}
