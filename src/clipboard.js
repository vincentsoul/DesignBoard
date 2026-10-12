// Clipboard payloads and placement helpers for copy, paste, and duplicate.
import { getElementBounds, makeId, parseDocument } from './model.js';

const PAYLOAD_ATTR = 'data-canvasroom';

export function serializeBoardClipboard(elements) {
  return JSON.stringify({ canvasroom: 1, elements });
}

export function parseBoardClipboard(text) {
  if (!text) return null;
  try {
    const value = JSON.parse(text);
    if (value?.canvasroom !== 1 || !Array.isArray(value.elements) || !value.elements.length) return null;
    // Reuse document validation so a malformed payload can never reach the board.
    return parseDocument(JSON.stringify({ version: 1, name: '', viewport: { x: 0, y: 0, zoom: 1 }, elements: value.elements })).elements;
  } catch {
    return null;
  }
}

function escapeHtml(text) {
  return text.replace(/[&<>"]/g, (char) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' })[char]);
}

// Board elements travel inside text/html so other apps still receive readable text.
export function boardClipboardHtml(elements) {
  const payload = encodeURIComponent(serializeBoardClipboard(elements));
  return `<div ${PAYLOAD_ATTR}="${payload}">${escapeHtml(clipboardPlainText(elements))}</div>`;
}

export function readBoardClipboardHtml(html) {
  const match = html?.match(/data-canvasroom="([^"]+)"/);
  if (!match) return null;
  try {
    return parseBoardClipboard(decodeURIComponent(match[1]));
  } catch {
    return null;
  }
}

export function clipboardPlainText(elements) {
  return elements.map((element) => element.data?.text || element.data?.title || '').filter(Boolean).join('\n');
}

function boundsOf(element) {
  const points = element.type === 'path' ? element.data?.points : null;
  if (!points?.length) return getElementBounds(element);
  const xs = points.map((point) => point.x);
  const ys = points.map((point) => point.y);
  return { x: Math.min(...xs), y: Math.min(...ys), width: Math.max(...xs) - Math.min(...xs), height: Math.max(...ys) - Math.min(...ys) };
}

export function elementsBounds(elements) {
  const boxes = elements.map(boundsOf);
  const left = Math.min(...boxes.map((box) => box.x));
  const top = Math.min(...boxes.map((box) => box.y));
  const right = Math.max(...boxes.map((box) => box.x + box.width));
  const bottom = Math.max(...boxes.map((box) => box.y + box.height));
  return { x: left, y: top, width: right - left, height: bottom - top };
}

export function centerOn(elements, point) {
  const bounds = elementsBounds(elements);
  const dx = point.x - (bounds.x + bounds.width / 2);
  const dy = point.y - (bounds.y + bounds.height / 2);
  for (const element of elements) {
    element.x += dx;
    element.y += dy;
    if (element.type === 'path' && Array.isArray(element.data.points)) {
      element.data.points = element.data.points.map((p) => ({ x: p.x + dx, y: p.y + dy }));
    }
  }
  return elements;
}

// Copies get fresh ids and groups; connectors stay attached only if both ends were copied too.
export function cloneForPaste(elements, point) {
  const copies = JSON.parse(JSON.stringify(elements));
  const ids = new Map(copies.map((element) => [element.id, makeId()]));
  const groups = new Map();
  for (const element of copies) {
    element.id = ids.get(element.id);
    element.locked = false;
    const groupId = element.data?.groupId;
    if (groupId) {
      if (!groups.has(groupId)) groups.set(groupId, `group_${makeId().slice(3, 15)}`);
      element.data.groupId = groups.get(groupId);
    }
    if (element.type === 'line' && (element.data.from || element.data.to)) {
      if (ids.has(element.data.from) && ids.has(element.data.to)) {
        element.data.from = ids.get(element.data.from);
        element.data.to = ids.get(element.data.to);
      } else {
        delete element.data.from;
        delete element.data.to;
      }
    }
  }
  return centerOn(copies, point);
}

const WIDE_CHAR = /[\u1100-\u115f\u2e80-\ua4cf\uac00-\ud7a3\uf900-\ufaff\ufe30-\ufe4f\uff00-\uff60\uffe0-\uffe6]/;

// Estimates a text box that fits pasted text, wrapping at maxWidth.
export function textElementSize(text, fontSize = 22, maxWidth = 420) {
  const padding = 8;
  const inner = maxWidth - padding;
  const lineHeight = fontSize * 1.38;
  let widest = 0;
  let lines = 0;
  for (const paragraph of String(text).split('\n')) {
    let width = 0;
    for (const char of paragraph) width += WIDE_CHAR.test(char) ? fontSize : fontSize * 0.56;
    widest = Math.max(widest, width);
    lines += Math.max(1, Math.ceil(width / (inner * 0.92)));
  }
  return {
    width: Math.ceil(Math.min(maxWidth, Math.max(60, widest + padding + fontSize * 0.5))),
    height: Math.ceil(lines * lineHeight + padding),
  };
}
