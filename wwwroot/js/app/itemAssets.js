const PLACEHOLDER_IMAGE = '/assets/items/box.png';

export function itemImage(item) {
  if (Number.isInteger(item.itemColor) && item.itemColor >= 0) {
    return `/assets/gfx/${item.image}/${item.itemColor}.png`;
  }
  return fallbackImage(item);
}

export function fallbackImage(item) {
  return `/assets/items/${item.image}.png`;
}

export function itemImageOnerror(item) {
  const primary = itemImage(item);
  const fallback = fallbackImage(item);
  if (primary === fallback) {
    return `this.onerror=null;this.src='${PLACEHOLDER_IMAGE}';`;
  }
  return `this.onerror=function(){this.onerror=null;this.src='${PLACEHOLDER_IMAGE}';};this.src='${fallback}';`;
}

export function itemKey(item) {
  return `${item.sourceFile}|${item.gid}|${item.classid}|${item.location}|${item.x}|${item.y}|${item.title}`;
}
