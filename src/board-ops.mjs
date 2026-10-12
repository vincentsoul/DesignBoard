// Board operations shared by the browser app, the sync server, and the MCP server.
import { createElement, getElementBounds } from './model.js';

const SPEC_TYPES = new Set(['sticky', 'text', 'shape', 'frame', 'line', 'arrow', 'connector']);
const SHAPES = new Set(['rectangle', 'rounded', 'circle', 'diamond']);
const GAP = 32;
const PER_ROW = 4;

function colorKey(type) {
  if (type === 'text') return 'color';
  if (type === 'line' || type === 'path') return 'stroke';
  return 'fill';
}

function isNumber(value) {
  return typeof value === 'number' && Number.isFinite(value);
}

function assertShape(shape) {
  if (shape !== undefined && !SHAPES.has(shape)) throw new Error(`Unsupported shape: ${shape}`);
}

export function connectorGeometry(from, to) {
  const a = getElementBounds(from);
  const b = getElementBounds(to);
  const ca = { x: a.x + a.width / 2, y: a.y + a.height / 2 };
  const cb = { x: b.x + b.width / 2, y: b.y + b.height / 2 };
  const dx = cb.x - ca.x;
  const dy = cb.y - ca.y;
  let start;
  let end;
  if (Math.abs(dx) >= Math.abs(dy)) {
    start = { x: dx >= 0 ? a.x + a.width : a.x, y: ca.y };
    end = { x: dx >= 0 ? b.x : b.x + b.width, y: cb.y };
  } else {
    start = { x: ca.x, y: dy >= 0 ? a.y + a.height : a.y };
    end = { x: cb.x, y: dy >= 0 ? b.y : b.y + b.height };
  }
  return { x: start.x, y: start.y, x2: end.x - start.x, y2: end.y - start.y };
}

function applyLineGeometry(element, geometry) {
  element.x = geometry.x;
  element.y = geometry.y;
  element.data.x2 = geometry.x2;
  element.data.y2 = geometry.y2;
  element.width = Math.abs(geometry.x2);
  element.height = Math.abs(geometry.y2);
}

function buildElement(spec, resolve) {
  if (!spec || typeof spec !== 'object') throw new Error('Each element must be an object');
  if (!SPEC_TYPES.has(spec.type)) throw new Error(`Unsupported element type: ${spec.type}`);
  const modelType = spec.type === 'arrow' || spec.type === 'connector' ? 'line' : spec.type;
  const overrides = { style: {}, data: {} };
  for (const key of ['x', 'y', 'width', 'height', 'rotation']) if (isNumber(spec[key])) overrides[key] = spec[key];
  if (typeof spec.locked === 'boolean') overrides.locked = spec.locked;
  if (typeof spec.color === 'string') overrides.style[colorKey(modelType)] = spec.color;
  if (isNumber(spec.fontSize)) overrides.style.fontSize = spec.fontSize;

  if (spec.type === 'sticky' || spec.type === 'text' || spec.type === 'shape') {
    if (typeof spec.text === 'string') overrides.data.text = spec.text;
  }
  if (spec.type === 'shape') {
    assertShape(spec.shape);
    if (spec.shape) overrides.data.shape = spec.shape;
  }
  if (spec.type === 'frame') {
    const title = spec.title ?? spec.text;
    if (typeof title === 'string') overrides.data.title = title;
  }

  const element = createElement(modelType, overrides);
  if (spec.type === 'line' || spec.type === 'arrow') {
    if (![spec.x, spec.y, spec.endX, spec.endY].every(isNumber)) throw new Error(`${spec.type} requires numeric x, y, endX, endY`);
    applyLineGeometry(element, { x: spec.x, y: spec.y, x2: spec.endX - spec.x, y2: spec.endY - spec.y });
    element.data.arrow = spec.type === 'arrow';
  }
  if (spec.type === 'connector') {
    const from = resolve(spec.from);
    const to = resolve(spec.to);
    if (!from || !to) throw new Error(`Connector endpoint not found: ${!from ? spec.from : spec.to}`);
    applyLineGeometry(element, connectorGeometry(from, to));
    Object.assign(element.data, { arrow: true, connector: true, from: from.id, to: to.id });
  }
  return element;
}

function placementCursor(doc) {
  const boxes = doc.elements.map(getElementBounds);
  if (!boxes.length) return { originX: 0, x: 0, y: 0, count: 0, rowHeight: 0 };
  const right = Math.max(...boxes.map((box) => box.x + box.width));
  const top = Math.min(...boxes.map((box) => box.y));
  return { originX: right + 80, x: right + 80, y: top, count: 0, rowHeight: 0 };
}

function autoPlace(element, cursor) {
  if (cursor.count > 0 && cursor.count % PER_ROW === 0) {
    cursor.x = cursor.originX;
    cursor.y += cursor.rowHeight + GAP;
    cursor.rowHeight = 0;
  }
  element.x = cursor.x;
  element.y = cursor.y;
  cursor.x += element.width + GAP;
  cursor.rowHeight = Math.max(cursor.rowHeight, element.height);
  cursor.count += 1;
}

// Validates the whole batch before touching the document, so a bad spec adds nothing.
export function addElements(doc, specs) {
  if (!Array.isArray(specs) || !specs.length) throw new Error('elements must be a non-empty array');
  const created = [];
  const byKey = new Map();
  const resolve = (ref) => byKey.get(ref) || created.find((item) => item.id === ref) || doc.elements.find((item) => item.id === ref);
  const cursor = placementCursor(doc);
  for (const spec of specs) {
    const element = buildElement(spec, resolve);
    const positioned = ['line', 'arrow', 'connector'].includes(spec.type) || (isNumber(spec.x) && isNumber(spec.y));
    if (!positioned) autoPlace(element, cursor);
    if (typeof spec.key === 'string') byKey.set(spec.key, element);
    created.push(element);
  }
  doc.elements.push(...created);
  return created;
}

function applyPatch(element, patch) {
  for (const key of ['x', 'y', 'width', 'height', 'rotation']) if (isNumber(patch[key])) element[key] = patch[key];
  if (typeof patch.locked === 'boolean') element.locked = patch.locked;
  if (typeof patch.color === 'string') element.style[colorKey(element.type)] = patch.color;
  if (isNumber(patch.fontSize)) element.style.fontSize = patch.fontSize;
  if (typeof patch.text === 'string') {
    if (element.type === 'frame') element.data.title = patch.text;
    else element.data.text = patch.text;
  }
  if (typeof patch.title === 'string') element.data.title = patch.title;
  if (patch.shape !== undefined && element.type === 'shape') {
    assertShape(patch.shape);
    element.data.shape = patch.shape;
  }
}

export function updateElements(doc, patches) {
  if (!Array.isArray(patches) || !patches.length) throw new Error('updates must be a non-empty array');
  const result = { updated: [], locked: [], missing: [] };
  for (const patch of patches) {
    const element = doc.elements.find((item) => item.id === patch?.id);
    if (!element) { result.missing.push(patch?.id); continue; }
    const editsContent = Object.keys(patch).some((key) => key !== 'id' && key !== 'locked');
    if (element.locked && editsContent) { result.locked.push(element.id); continue; }
    applyPatch(element, patch);
    result.updated.push(element.id);
  }
  refreshConnectors(doc);
  return result;
}

export function deleteElements(doc, ids) {
  if (!Array.isArray(ids) || !ids.length) throw new Error('ids must be a non-empty array');
  const result = { deleted: [], locked: [], missing: [] };
  const removing = new Set();
  for (const id of ids) {
    const element = doc.elements.find((item) => item.id === id);
    if (!element) result.missing.push(id);
    else if (element.locked) result.locked.push(id);
    else { removing.add(id); result.deleted.push(id); }
  }
  doc.elements = doc.elements.filter((item) => !removing.has(item.id));
  result.deleted.push(...refreshConnectors(doc));
  return result;
}

// Re-routes attached connectors and drops ones whose endpoint element is gone. Returns removed ids.
export function refreshConnectors(doc) {
  const byId = new Map(doc.elements.map((item) => [item.id, item]));
  const removed = [];
  doc.elements = doc.elements.filter((item) => {
    if (item.type !== 'line' || !item.data.connector) return true;
    const { from, to } = item.data;
    if ((from && !byId.has(from)) || (to && !byId.has(to))) { removed.push(item.id); return false; }
    if (from && to) applyLineGeometry(item, connectorGeometry(byId.get(from), byId.get(to)));
    return true;
  });
  return removed;
}

export function summarizeBoard(doc) {
  const round = (value) => Math.round(Number(value) || 0);
  return {
    name: doc.name,
    elementCount: doc.elements.length,
    elements: doc.elements.map((element) => {
      const out = { id: element.id, type: element.type, x: round(element.x), y: round(element.y), width: round(element.width), height: round(element.height) };
      const { data, style } = element;
      if (data.text) out.text = data.text;
      if (data.title) out.title = data.title;
      if (element.type === 'shape') out.shape = data.shape;
      if (element.type === 'line') {
        out.endX = round(element.x + Number(data.x2 || 0));
        out.endY = round(element.y + Number(data.y2 || 0));
        if (data.arrow) out.arrow = true;
        if (data.from) out.from = data.from;
        if (data.to) out.to = data.to;
      }
      if (element.type === 'image') out.name = data.name;
      if (element.type === 'path') out.pointCount = (data.points || []).length;
      const color = style[colorKey(element.type)];
      if (color && color !== 'none') out.color = color;
      if (element.rotation) out.rotation = element.rotation;
      if (element.locked) out.locked = true;
      if (data.groupId) out.groupId = data.groupId;
      return out;
    }),
  };
}
