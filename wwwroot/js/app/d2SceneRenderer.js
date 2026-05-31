import { gridConfig } from "./state.js";
import { escapeAttr, escapeHtml } from "./html.js";
import { itemImage, itemImageOnerror, itemKey } from "./itemAssets.js";
import { sceneArtInlineStyle, sceneBgInlineStyle, sceneInlineStyle, storagePanelArtwork } from "./d2SceneConstants.js";
import { fitsGrid, getSocketPositions, isHiddenByWeaponSet, scenePosition } from "./d2SceneGeometry.js";
import { detectMercenaryIconType, mercenaryIconAssets } from "./d2SceneMercenary.js";

export function renderD2Scene(tab, items, state, options = {}) {
  const baseItems = options.mercenaryOpen ? items.filter(item => item.storage !== "stash") : items;
  const visibleItems = options.cubeOpen ? baseItems.filter(item => item.storage !== "stash") : baseItems;
  const sceneSourceItems = options.mercenaryOpen ? [...visibleItems, ...(options.mercenaryItems || [])] : visibleItems;
  const sceneItems = sceneSourceItems.map(item => renderSceneItem(tab, item, state)).filter(Boolean).join("");
  const overflow = sceneSourceItems.filter(item => !isHiddenByWeaponSet(item, state) && !scenePosition(tab, item, state));
  const hasCube = Boolean(options.cubeItems?.length) || items.some(isCubeItem);
  const weaponButtons = renderWeaponSetButtons(state.weaponSet);
  // the panel-heading view-tools as a clearly-labeled text button —
  const mercenaryOverlay = options.mercenaryOpen ? renderMercenaryOverlay() : "";
  const cubeOverlay = options.cubeOpen ? renderCubeOverlay(options.cubeItems || []) : "";

  return `
    <div class="d2-scene-wrap">
      <div class="d2-scene ${tab}-scene" style="${sceneInlineStyle}">
        <span class="d2-scene-art" style="${sceneArtInlineStyle}" aria-hidden="true"></span>
        <img class="d2-scene-bg" style="${sceneBgInlineStyle}" src="${storagePanelArtwork}" alt="">
        ${mercenaryOverlay}
        ${sceneItems}
        ${weaponButtons}
        ${renderScenePanelControls(options)}
        ${cubeOverlay}
      </div>
    </div>
    ${overflow.length ? `<h3 class="overflow-title">Overflow / unknown position</h3>${renderList(overflow)}` : ""}
  `;
}

export function renderList(items) {
  if (!items.length) return `<div class="empty">No items in this view.</div>`;
  return `<div class="list-view">${items.map(item => renderItem(item, "list-card")).join("")}</div>`;
}

export function renderObservedEquipmentScene(items, state = {}) {
  const sceneItems = items
    .filter(item => item.storage === "equipped")
    .map(item => renderSceneItem("storage", item, state))
    .filter(Boolean)
    .join("");
  const weaponButtons = renderWeaponSetButtons(state.weaponSet || 1);

  return `
    <div class="d2-scene-wrap observed-equipment-scene-wrap">
      <div class="d2-scene storage-scene observed-equipment-scene" style="${sceneInlineStyle}">
        <span class="d2-scene-art" style="${sceneArtInlineStyle}" aria-hidden="true"></span>
        <img class="d2-scene-bg" style="${sceneBgInlineStyle}" src="${storagePanelArtwork}" alt="">
        ${sceneItems}
        ${weaponButtons}
      </div>
    </div>
  `;
}

export function isCubeItem(item) {
  return /Horadric Cube/i.test(`${item.title || ""}
${item.description || ""}`) || item.image === "box";
}

function renderMercenaryOverlay() {
  // The .d2-panel-control base class (in d2-scene.css) owns the
  // center-anchored positioning system; .mercenary-close only sets
  // its (--cx, --cy) for the bottom-right close-X slot of the
  // mercenary-panel.png artwork. Same pattern as .cube-close below.
  return `
    <div class="mercenary-popover">
      <img class="mercenary-panel-bg" src="/assets/d2ui/mercenary-panel.png" alt="">
      <button class="d2-panel-control mercenary-close" type="button" data-close-mercenary aria-label="Close mercenary"></button>
    </div>
  `;
}

function renderWeaponSetButtons(weaponSet) {
  const weaponSetSpritePatches = weaponSet === 2 ? `
    <span class="weapon-tab-patch left-set set-one inactive-one"></span>
    <span class="weapon-tab-patch left-set set-two active-two"></span>
    <span class="weapon-tab-patch right-set set-one inactive-one"></span>
    <span class="weapon-tab-patch right-set set-two active-two"></span>
  ` : "";

  return `
    ${weaponSetSpritePatches}
    <button class="weapon-swap-toggle left-set" type="button" data-toggle-weapon-set aria-label="Toggle left weapon set"></button>
    <button class="weapon-swap-toggle right-set" type="button" data-toggle-weapon-set aria-label="Toggle right weapon set"></button>
  `;
}

function renderScenePanelControls(options = {}) {
  if (options.mercenaryOpen) return "";
  if (!options.hasMercenaryData) return renderScenePanelControlPlaceholders(options);

  const mercenaryIconType = detectMercenaryIconType(options);
  const mercenaryIcon = mercenaryIconAssets[mercenaryIconType] || mercenaryIconAssets.unknown;
  return `
    <button class="d2-scene-mercenary-opener" type="button" data-toggle-mercenary aria-label="Open mercenary" title="Mercenary" data-mercenary-icon="${escapeAttr(mercenaryIconType)}">
      <span class="d2-scene-mercenary-opener__portrait" aria-hidden="true">
        <img src="${escapeAttr(mercenaryIcon)}" alt="">
      </span>
      <span class="d2-scene-mercenary-opener__label">Mercenary</span>
    </button>
    ${options.cubeOpen ? `
      <span class="d2-panel-control d2-panel-control--placeholder d2-panel-control--right-slot" aria-hidden="true"></span>
    ` : `
      <span class="d2-panel-control d2-panel-control--placeholder d2-panel-control--right-slot" aria-hidden="true"></span>
      <span class="d2-panel-control d2-panel-control--placeholder d2-panel-control--inventory-slot" aria-hidden="true"></span>
    `}
  `;
}

function renderScenePanelControlPlaceholders(options = {}) {
  return options.cubeOpen ? `
    <span class="d2-panel-control d2-panel-control--placeholder d2-panel-control--right-slot" aria-hidden="true"></span>
  ` : `
    <span class="d2-panel-control d2-panel-control--placeholder d2-panel-control--right-slot" aria-hidden="true"></span>
    <span class="d2-panel-control d2-panel-control--placeholder d2-panel-control--inventory-slot" aria-hidden="true"></span>
  `;
}

function renderCubeOverlay(items) {
  const cubeItems = items.map(item => renderSceneItem("cube", item)).filter(Boolean).join("");
  const overflow = items.filter(item => !scenePosition("cube", item));
  return `
    <div class="cube-popover">
      <img class="cube-panel-bg" src="/assets/d2ui/cube-panel.png" alt="">
      ${cubeItems}
      <button class="d2-panel-control cube-close" type="button" data-close-cube aria-label="Close cube"></button>
      ${overflow.length ? `<div class="cube-overflow">${overflow.length} overflow</div>` : ""}
    </div>
  `;
}

function renderCubeCells(items) {
  const config = gridConfig.cube;
  const occupied = new Set();
  const cells = [];
  for (let y = 0; y < config.rows; y++) {
    for (let x = 0; x < config.cols; x++) {
      const cellKey = `${x}:${y}`;
      if (occupied.has(cellKey)) continue;
      const item = items.find(candidate => candidate.x === x && candidate.y === y && fitsGrid(candidate, config));
      if (item) {
        markOccupied(occupied, item, config);
        cells.push(renderItem(item, "item placed"));
      } else {
        cells.push(`<div class="slot"></div>`);
      }
    }
  }
  return cells.join("");
}

function renderSceneItem(tab, item, state) {
  const pos = scenePosition(tab, item, state);
  if (!pos) return "";
  const itemW = Math.max(1, item.width || pos.w);
  const itemH = Math.max(1, item.height || pos.h);
  const imgW = Math.min(itemW, pos.w - 2);
  const imgH = Math.min(itemH, pos.h - 2);
  const imgX = Math.round((pos.w - imgW) / 2);
  const isEquipmentSlotItem = item.storage === "equipped" || item.storage === "mercenary";
  const imgY = isEquipmentSlotItem ? Math.round((pos.h - imgH) / 2) : Math.max(0, pos.h - imgH - 1);
  const style = `left:${pos.x}px;top:${pos.y}px;width:${pos.w}px;height:${pos.h}px;--img-x:${imgX}px;--img-y:${imgY}px;--img-w:${imgW}px;--img-h:${imgH}px;`;
  const cubeAttr = isCubeItem(item) ? " data-open-cube" : "";

  return `
    <div class="d2-scene-item storage-${escapeAttr(item.storage || "unknown")} ${isEtherealItem(item) ? "ethereal" : ""} ${isCubeItem(item) ? "cube-item" : ""}" style="${style}" data-item-key="${escapeAttr(itemKey(item))}"${cubeAttr}>
      <span class="d2-item-highlight"></span>
      <img src="${itemImage(item)}" alt="" onerror="${itemImageOnerror(item)}">
      ${renderSocketOverlay(item)}
    </div>
  `;
}

function renderSocketOverlay(item) {
  if (!item.sockets?.length) return "";
  const width = item.gridWidth || 1;
  const height = item.gridHeight || 1;
  return getSocketPositions(width, height, item.sockets.length).map((pos, index) => {
    const socket = item.sockets[index] || "gemsocket";
    const gem = socket !== "gemsocket" ? `<img class="socket-gem" src="/assets/items/${escapeAttr(socket)}.png" alt="">` : "";
    const left = `calc(var(--img-x) + ${pos.x - 1}px)`;
    const top = `calc(var(--img-y) + ${pos.y + 1}px)`;
    return `
      <span class="socket-mark" style="left:${left};top:${top};">
        <img class="socket-hole" src="/assets/d2ui/gemsocket-0.png" alt="">
        ${gem}
      </span>
    `;
  }).join("");
}

function renderItem(item, className) {
  const socketText = item.sockets?.length ? `${item.sockets.length} socket${item.sockets.length === 1 ? "" : "s"}` : item.storage;
  const style = className.includes("placed")
    ? `style="grid-column: span ${item.gridWidth || 1}; grid-row: span ${item.gridHeight || 1};"`
    : "";
  return `
    <div class="${className} ${isEtherealItem(item) ? "ethereal" : ""}" data-item-key="${escapeAttr(itemKey(item))}" ${style}>
      <img src="${itemImage(item)}" alt="" onerror="${itemImageOnerror(item)}">
      ${className === "list-card" ? `<div><strong>${escapeHtml(item.title)}</strong><span>${escapeHtml(socketText)}</span></div>` : ""}
    </div>
  `;
}

function markOccupied(occupied, item, config) {
  for (let y = item.y; y < Math.min(config.rows, item.y + item.gridHeight); y++) {
    for (let x = item.x; x < Math.min(config.cols, item.x + item.gridWidth); x++) {
      if (x === item.x && y === item.y) continue;
      occupied.add(`${x}:${y}`);
    }
  }
}

function isEtherealItem(item) {
  return item && item.ethereal === true;
}
