import { gridConfig } from "./state.js";
import {
  cubeGridOrigin,
  equippedPixelSlots,
  equippedRingSlots,
  equippedSlotOffset,
  mercenaryPixelSlots
} from "./d2SceneLayout.js";

export function scenePosition(tab, item, state) {
  if (tab === "cube" && item.storage === "cube" && fitsGrid(item, gridConfig.cube)) {
    return gridPosition(item, cubeGridOrigin.x, cubeGridOrigin.y);
  }
  if (item.storage === "inventory" && fitsGrid(item, gridConfig.inventory)) return gridPosition(item, 419, 316);
  if (item.storage === "stash" && fitsGrid(item, gridConfig.stash)) return gridPosition(item, 154, 143);
  if (item.storage === "mercenary") return mercenaryPosition(item);
  if (item.storage === "equipped") return equippedPosition(item, state);
  return null;
}

export function mercenaryPosition(item) {
  const slot = mercenaryPixelSlots[item.x];
  if (!slot) return null;
  return slot;
}

export function gridPosition(item, baseX, baseY) {
  return {
    x: baseX + item.x * 29,
    y: baseY + item.y * 29,
    w: (item.gridWidth || 1) * 29,
    h: (item.gridHeight || 1) * 29
  };
}

export function equippedPosition(item, state) {
  if (isHiddenByWeaponSet(item, state)) return null;
  const slot = equippedPixelSlots[item.x];
  if (!slot) return null;
  const ringXAdjustment = equippedRingSlots.has(item.x) ? -1 : 0;
  return { ...slot, x: slot.x + equippedSlotOffset.x + ringXAdjustment, y: slot.y + equippedSlotOffset.y };
}

export function isHiddenByWeaponSet(item, state) {
  if (item.storage !== "equipped") return false;
  return (state.weaponSet === 1 && [11, 12].includes(item.x))
    || (state.weaponSet === 2 && [4, 5].includes(item.x));
}

export function fitsGrid(item, config) {
  return item.x >= 0
    && item.y >= 0
    && item.x < config.cols
    && item.y < config.rows
    && item.x + (item.gridWidth || 1) <= config.cols
    && item.y + (item.gridHeight || 1) <= config.rows;
}

export function getSocketPositions(width, height, socketCount) {
  const key = `${width}x${height}:${Math.min(socketCount, 6)}`;
  const table = {
    "1x2:1": [[0,14]], "1x2:2": [[0,0],[0,29]],
    "1x3:1": [[0,29]], "1x3:2": [[0,14],[0,43]], "1x3:3": [[0,0],[0,29],[0,58]],
    "1x4:1": [[0,43]], "1x4:2": [[0,29],[0,58]], "1x4:3": [[0,14],[0,43],[0,72]], "1x4:4": [[0,0],[0,29],[0,58],[0,87]],
    "2x2:1": [[14,14]], "2x2:2": [[14,0],[14,29]], "2x2:3": [[0,0],[29,0],[14,29]], "2x2:4": [[0,0],[29,29],[0,29],[29,0]],
    "2x3:1": [[14,29]], "2x3:2": [[14,14],[14,43]], "2x3:3": [[14,0],[14,29],[14,58]], "2x3:4": [[0,14],[29,14],[0,43],[29,43]], "2x3:5": [[0,0],[29,0],[14,29],[0,58],[29,58]], "2x3:6": [[0,0],[29,0],[0,29],[29,29],[0,58],[29,58]],
    "2x4:1": [[14,43]], "2x4:2": [[14,14],[14,72]], "2x4:3": [[14,14],[14,43],[14,72]], "2x4:4": [[14,0],[14,29],[14,58],[14,87]], "2x4:5": [[0,14],[29,14],[14,43],[0,72],[29,72]], "2x4:6": [[0,14],[29,14],[0,43],[29,43],[0,72],[29,72]]
  };
  return (table[key] || Array.from({ length: Math.min(socketCount, 6) }, () => [0, 0])).map(([x, y]) => ({ x, y }));
}
