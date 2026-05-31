import { STORAGE_KEYS } from "./storageKeys.js";

const font16 = {
  charWidth: 14,
  charHeight: 16,
  lineHeight: 16,
  scale: 1,
  canvasPadX: 3,
  source: "/assets/fonts/font16.png",
  image: null,
  loading: null
};

// Official Font16 kerning table from ItemScreenshot's font16.js.
const font16Kerning = [
  10, 12, 10, 12, 10, 12, 10, 12, 10, 12, 10, 12, 10, 12, 10, 12, 10, 12, 10, 12, 10, 12, 10, 12, 10, 12, 10, 12, 10, 12, 10, 12,
  10, 12, 10, 12, 10, 12, 10, 12, 10, 12, 10, 12, 10, 12, 10, 12, 10, 12, 10, 12, 10, 12, 10, 12, 10, 12, 10, 12, 10, 12, 10, 12,
  10, 8, 10, 8, 10, 7, 10, 8, 10, 8, 10, 13, 10, 12, 10, 4, 10, 5, 10, 5, 10, 6, 10, 8, 10, 5, 10, 5, 10, 5, 10, 9,
  10, 12, 10, 5, 10, 9, 10, 8, 10, 9, 10, 9, 10, 8, 10, 8, 10, 7, 10, 8, 10, 5, 10, 5, 10, 6, 10, 7, 10, 6, 10, 8,
  10, 11, 10, 12, 10, 7, 10, 9, 10, 10, 10, 8, 10, 8, 10, 10, 10, 9, 10, 5, 10, 5, 10, 9, 10, 8, 10, 12, 10, 10, 10, 11,
  10, 9, 10, 12, 10, 10, 10, 7, 10, 11, 10, 12, 10, 13, 10, 16, 10, 12, 10, 12, 10, 10, 10, 5, 10, 9, 10, 5, 10, 5, 10, 9,
  10, 5, 10, 10, 10, 7, 10, 8, 10, 8, 10, 7, 10, 7, 10, 9, 10, 7, 10, 4, 10, 4, 10, 8, 10, 7, 10, 10, 10, 9, 10, 10,
  10, 7, 10, 10, 10, 9, 10, 7, 10, 9, 10, 10, 10, 10, 10, 13, 10, 10, 10, 10, 10, 7, 10, 6, 10, 3, 10, 6, 10, 6,
  10, 12, 10, 12, 10, 12, 10, 12, 10, 12, 10, 12, 10, 12, 10, 12, 10, 12, 10, 12, 10, 12, 10, 12, 10, 12, 10, 12, 10, 12, 10, 12,
  10, 12, 10, 12, 10, 5, 10, 6, 10, 12, 10, 12, 10, 12, 10, 12, 10, 12, 10, 12, 10, 12, 10, 12, 10, 12, 10, 12, 10, 12, 10, 12,
  10, 12, 10, 8, 10, 8, 10, 7, 10, 8, 10, 7, 10, 12, 10, 3, 10, 6, 10, 6, 10, 11, 10, 9, 10, 7, 10, 10, 10, 4, 10, 11,
  10, 9, 10, 7, 10, 9, 10, 7, 10, 7, 10, 5, 10, 13, 10, 9, 10, 7, 10, 7, 10, 3, 10, 8, 10, 8, 10, 11, 10, 13, 10, 12,
  10, 8, 10, 12, 10, 12, 10, 12, 10, 12, 10, 12, 10, 12, 10, 11, 10, 10, 10, 8, 10, 7, 10, 8, 10, 8, 10, 5, 10, 5, 10, 5,
  10, 7, 10, 11, 10, 11, 10, 11, 10, 11, 10, 11, 10, 12, 10, 11, 10, 10, 10, 11, 10, 13, 10, 13, 10, 13, 10, 12, 10, 12, 10, 8,
  10, 9, 10, 11, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 8, 10, 7, 10, 6, 10, 7, 10, 7, 10, 4, 10, 5, 10, 4,
  10, 5, 10, 8, 10, 9, 10, 10, 10, 9, 10, 9, 10, 10, 10, 10, 10, 8, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 7, 10, 10
];

let tooltipRenderId = 0;
const tooltipFontStorageKey = STORAGE_KEYS.tooltipFont;
const tooltipFontModes = new Set(["d2", "modern"]);

export function initTooltipFontPreference() {
  const mode = readTooltipFontMode();
  applyTooltipFontMode(mode);
  return mode;
}

export function getTooltipFontMode() {
  return readTooltipFontMode();
}

export function setTooltipFontMode(mode) {
  const normalized = tooltipFontModes.has(mode) ? mode : "d2";
  try {
    localStorage.setItem(tooltipFontStorageKey, normalized);
  } catch {}
  applyTooltipFontMode(normalized);
  return normalized;
}

export function toggleTooltipFontMode() {
  return setTooltipFontMode(readTooltipFontMode() === "modern" ? "d2" : "modern");
}

export function parseDescription(description) {
  const clean = String(description || "").split("$")[0];
  return clean.split("\n").map(line => {
    let color = "0";
    let text = line
      .replace(/^\\xffc([0-9])/, (_, c) => {
        color = c;
        return "";
      })
      .replace(/^\\xffc([0-9])/, "")
      .replace(/\\xffc([0-9])/g, "")
      .replace(/^ÿc([0-9])/, (_, c) => {
        color = c;
        return "";
      })
      .replace(/ÿc([0-9])/g, "")
      .replace(/\\/g, "");
    return { color, text };
  });
}

export function showTooltip(tooltip, item, anchor) {
  if (!item) return;
  const lines = parseDescription(item.description);
  const renderId = ++tooltipRenderId;
  tooltip.hidden = true;

  if (readTooltipFontMode() === "modern") {
    tooltip.dataset.tooltipFont = "modern";
    tooltip.replaceChildren(renderModernTooltip(lines));
    tooltip.hidden = false;
    placeTooltip(tooltip, anchor);
    return;
  }

  tooltip.dataset.tooltipFont = "d2";

  ensureFont16().then(() => {
    if (renderId !== tooltipRenderId) return;
    tooltip.replaceChildren(renderBitmapTooltip(lines));
    tooltip.hidden = false;
    placeTooltip(tooltip, anchor);
  }).catch(() => {
    if (renderId !== tooltipRenderId) return;
    tooltip.textContent = lines.map(line => line.text).join("\n");
    tooltip.hidden = false;
    placeTooltip(tooltip, anchor);
  });
}

export function hideTooltip(tooltip) {
  tooltipRenderId++;
  tooltip.hidden = true;
}

function ensureFont16() {
  if (font16.image) return Promise.resolve();
  if (font16.loading) return font16.loading;

  font16.loading = new Promise((resolve, reject) => {
    const image = new Image();
    image.onload = () => {
      font16.image = image;
      resolve();
    };
    image.onerror = reject;
    image.src = font16.source;
  });

  return font16.loading;
}

function renderBitmapTooltip(lines) {
  const normalized = lines.map(line => ({
    color: normalizeColor(line.color),
    text: line.text || " "
  }));
  const measurements = normalized.map(line => measureBitmapText(line.text));
  const width = Math.max(1, ...measurements);
  const height = Math.max(font16.charHeight, normalized.length * font16.lineHeight);
  const canvas = document.createElement("canvas");
  const context = canvas.getContext("2d");
  const scale = font16.scale;
  const padX = font16.canvasPadX;

  canvas.className = "tooltip-bitmap";
  canvas.width = Math.ceil((width + padX * 2) * scale);
  canvas.height = Math.ceil(height * scale);
  canvas.style.width = `${canvas.width}px`;
  canvas.style.height = `${canvas.height}px`;

  context.imageSmoothingEnabled = false;
  normalized.forEach((line, index) => {
    const lineWidth = measurements[index];
    drawBitmapText(context, padX + Math.round((width - lineWidth) / 2), index * font16.lineHeight, line.text, line.color, scale);
  });

  return canvas;
}

function renderModernTooltip(lines) {
  const fragment = document.createDocumentFragment();
  for (const line of lines) {
    const node = document.createElement("span");
    node.className = `line c${normalizeColor(line.color)}`;
    node.textContent = line.text || " ";
    fragment.appendChild(node);
  }
  return fragment;
}

function measureBitmapText(text) {
  return [...text].reduce((width, char) => width + glyphAdvance(char), 0);
}

function drawBitmapText(context, x, y, text, color, scale) {
  let cursor = Math.round(x * scale);
  const destY = Math.round(y * scale);
  const sourceY = (color > 0 ? color + 1 : 0) * font16.charHeight;

  for (const char of text) {
    const code = char.charCodeAt(0);
    if (code < 256 && char !== " ") {
      context.drawImage(
        font16.image,
        code * font16.charWidth,
        sourceY,
        font16.charWidth,
        font16.charHeight,
        cursor,
        destY,
        Math.round(font16.charWidth * scale),
        Math.round(font16.charHeight * scale)
      );
    }
    cursor += Math.round(glyphAdvance(char) * scale);
  }
}

function glyphAdvance(char) {
  const code = char.charCodeAt(0);
  if (code >= 256) return 8;
  return font16Kerning[code * 2 + 1] || 8;
}

function normalizeColor(color) {
  const value = Number.parseInt(color, 10);
  return Number.isFinite(value) ? Math.min(13, Math.max(0, value)) : 0;
}

function readTooltipFontMode() {
  try {
    const mode = localStorage.getItem(tooltipFontStorageKey);
    return tooltipFontModes.has(mode) ? mode : "d2";
  } catch {
    return "d2";
  }
}

function applyTooltipFontMode(mode) {
  document.documentElement.dataset.tooltipFont = mode;
}

function placeTooltip(tooltip, anchor) {
  const pad = 12;
  const margin = 8;
  const anchorRect = anchor.getBoundingClientRect();
  const rect = tooltip.getBoundingClientRect();
  const viewport = { left: margin, top: margin, right: window.innerWidth - margin, bottom: window.innerHeight - margin };
  const centerX = anchorRect.left + anchorRect.width / 2;
  const centerY = anchorRect.top + anchorRect.height / 2;
  const sceneRect = anchor.closest(".d2-scene")?.getBoundingClientRect();
  const preferLeftSide = sceneRect ? centerX < sceneRect.left + sceneRect.width / 2 : centerX < window.innerWidth / 2;
  const sideOrder = preferLeftSide ? ["left", "right"] : ["right", "left"];
  const positions = {
    top: { x: centerX - rect.width / 2, y: anchorRect.top - rect.height - pad },
    bottom: { x: centerX - rect.width / 2, y: anchorRect.bottom + pad },
    left: { x: anchorRect.left - rect.width - pad, y: centerY - rect.height / 2 },
    right: { x: anchorRect.right + pad, y: centerY - rect.height / 2 }
  };
  const candidates = [
    "top",
    "bottom",
    ...sideOrder
  ].map((side, index) => {
    const candidate = positions[side];
    const x = clamp(candidate.x, viewport.left, viewport.right - rect.width);
    const y = clamp(candidate.y, viewport.top, viewport.bottom - rect.height);
    const tooltipRect = { left: x, top: y, right: x + rect.width, bottom: y + rect.height };
    return { side, order: index, x, y, overflow: overflowArea(tooltipRect, viewport), overlap: overlapArea(tooltipRect, anchorRect) };
  });

  const clean = candidates.find(candidate => candidate.overlap === 0 && candidate.overflow === 0);
  const best = clean || candidates.sort((a, b) => (a.overlap - b.overlap) || (a.overflow - b.overflow) || (a.order - b.order))[0];
  tooltip.style.left = `${best.x}px`;
  tooltip.style.top = `${best.y}px`;
}

function clamp(value, min, max) {
  if (max < min) return min;
  return Math.min(max, Math.max(min, value));
}

function overlapArea(a, b) {
  const width = Math.max(0, Math.min(a.right, b.right) - Math.max(a.left, b.left));
  const height = Math.max(0, Math.min(a.bottom, b.bottom) - Math.max(a.top, b.top));
  return width * height;
}

function overflowArea(rect, bounds) {
  const left = Math.max(0, bounds.left - rect.left);
  const right = Math.max(0, rect.right - bounds.right);
  const top = Math.max(0, bounds.top - rect.top);
  const bottom = Math.max(0, rect.bottom - bounds.bottom);
  return left + right + top + bottom;
}
