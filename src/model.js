const TYPE_DEFAULTS = {
  sticky: {
    width: 188,
    height: 168,
    style: { fill: '#ffd66b', stroke: '#c79019', strokeWidth: 1, opacity: 1 },
    data: { text: 'New idea' },
  },
  text: {
    width: 240,
    height: 56,
    style: { color: '#202b37', fontSize: 22, fontWeight: 500, align: 'left', opacity: 1 },
    data: { text: 'Double-click to edit' },
  },
  shape: {
    width: 180,
    height: 120,
    style: { fill: '#c9f1e1', stroke: '#2d8068', strokeWidth: 2, opacity: 1 },
    data: { shape: 'rectangle', text: '' },
  },
  line: {
    width: 180,
    height: 0,
    style: { stroke: '#344554', strokeWidth: 3, opacity: 1 },
    data: { x2: 180, y2: 0, arrow: false },
  },
  path: {
    width: 1,
    height: 1,
    style: { stroke: '#344554', strokeWidth: 4, opacity: 1, fill: 'none' },
    data: { points: [] },
  },
  image: {
    width: 240,
    height: 180,
    style: { opacity: 1 },
    data: { src: '', name: 'Image' },
  },
  frame: {
    width: 420,
    height: 280,
    style: { fill: '#ffffff', stroke: '#a8b5c1', strokeWidth: 2, opacity: 0.9 },
    data: { title: 'Section' },
  },
};

const KNOWN_TYPES = new Set(Object.keys(TYPE_DEFAULTS));

export function makeId() {
  if (globalThis.crypto?.randomUUID) return `el_${globalThis.crypto.randomUUID()}`;
  return `el_${Date.now().toString(36)}_${Math.random().toString(36).slice(2, 8)}`;
}

export function createDocument(name = 'Untitled board') {
  return {
    version: 1,
    name,
    viewport: { x: 0, y: 0, zoom: 1 },
    elements: [],
  };
}

export function createElement(type, overrides = {}) {
  if (!KNOWN_TYPES.has(type)) throw new Error(`Unknown element type: ${type}`);
  const defaults = TYPE_DEFAULTS[type];
  return {
    id: overrides.id || makeId(),
    type,
    x: overrides.x ?? 0,
    y: overrides.y ?? 0,
    width: overrides.width ?? defaults.width,
    height: overrides.height ?? defaults.height,
    rotation: overrides.rotation ?? 0,
    locked: overrides.locked ?? false,
    style: { ...defaults.style, ...(overrides.style || {}) },
    data: { ...defaults.data, ...(overrides.data || {}) },
  };
}

function assertDocument(value) {
  if (!value || typeof value !== 'object') throw new Error('Document must be an object');
  if (value.version !== 1) throw new Error('Unsupported document version');
  if (!value.viewport || typeof value.viewport !== 'object') throw new Error('viewport is required');
  if (!Array.isArray(value.elements)) throw new Error('elements must be an array');
  if (typeof value.name !== 'string') throw new Error('name must be a string');
  for (const element of value.elements) {
    if (!element || typeof element !== 'object' || !KNOWN_TYPES.has(element.type)) {
      throw new Error('Invalid element');
    }
    if (typeof element.id !== 'string') throw new Error('Element id must be a string');
  }
  return value;
}

export function serializeDocument(document) {
  assertDocument(document);
  return JSON.stringify(document, null, 2);
}

export function parseDocument(serialized) {
  let value;
  try {
    value = JSON.parse(serialized);
  } catch {
    throw new Error('Invalid JSON document');
  }
  assertDocument(value);
  return value;
}

export function cloneDocument(document) {
  return parseDocument(serializeDocument(document));
}

export function getElementBounds(element) {
  if (element.type === 'line') {
    const x2 = Number(element.data.x2 ?? element.width);
    const y2 = Number(element.data.y2 ?? element.height);
    return {
      x: Math.min(element.x, element.x + x2),
      y: Math.min(element.y, element.y + y2),
      width: Math.abs(x2),
      height: Math.abs(y2),
    };
  }
  return { x: element.x, y: element.y, width: element.width, height: element.height };
}

export function documentBounds(document, padding = 80) {
  if (!document.elements.length) return { x: -padding, y: -padding, width: padding * 2, height: padding * 2 };
  const bounds = document.elements.map(getElementBounds);
  const left = Math.min(...bounds.map((item) => item.x));
  const top = Math.min(...bounds.map((item) => item.y));
  const right = Math.max(...bounds.map((item) => item.x + item.width));
  const bottom = Math.max(...bounds.map((item) => item.y + item.height));
  return { x: left - padding, y: top - padding, width: right - left + padding * 2, height: bottom - top + padding * 2 };
}
