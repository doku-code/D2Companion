/**
 * @param {string} query - Lowercase search string (empty = match all)
 * @param {string} storageFilter - Storage location key, or "all"
 * @returns {(item: ItemRecord) => boolean}
 */
export function makeItemFilter(query, storageFilter) {
  return item => {
    if (storageFilter !== "all" && item.storage !== storageFilter) return false;
    if (!query) return true;
    const text = `${item.account} ${item.character} ${item.title} ${item.description ?? ""} ${item.storage ?? ""}`;
    return text.toLowerCase().includes(query);
  };
}

/**
 * @param {ItemRecord[]} items
 * @param {{ account: string, name: string, realm?: string|null } | null} character
 * @returns {ItemRecord[]}
 */
export function itemsBelongingTo(items, character) {
  if (!character) return [];
  return items.filter(i =>
    i.account === character.account &&
    i.character === character.name &&
    String(i.realm || "") === String(character.realm || ""));
}
