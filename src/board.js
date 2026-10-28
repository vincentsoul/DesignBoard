import { createDocument, createElement, cloneDocument, documentBounds, getElementBounds } from './model.js';
import { icon } from './icons.js';
import { zoomAt, wheelZoom } from './viewport.js';
import { downloadJson, downloadPng, loadLocal, readJsonFile, saveLocal } from './io.js';
import { boardsApi, createFileSync } from './sync.js';
import { refreshConnectors } from './board-ops.mjs';
import { boardClipboardHtml, centerOn, clipboardPlainText, cloneForPaste, elementsBounds, parseBoardClipboard, readBoardClipboardHtml, serializeBoardClipboard, textElementSize } from './clipboard.js';

const viewportEl = document.querySelector('#board-viewport');
const worldEl = document.querySelector('#world');
const svgWorldEl = document.querySelector('#svg-world');
const svgLayerEl = document.querySelector('#svg-layer');
const selectionLayerEl = document.querySelector('#selection-layer');
const marqueeEl = document.querySelector('#marquee');
const emptyStateEl = document.querySelector('#empty-state');
const inspectorEl = document.querySelector('#inspector');
const inspectorEmptyEl = document.querySelector('#inspector-empty');
const inspectorContentEl = document.querySelector('#inspector-content');
const selectionCountEl = document.querySelector('#selection-count');
const zoomValueEl = document.querySelector('#zoom-value');
const saveStatusEl = document.querySelector('#save-status');
const imageInput = document.querySelector('#image-input');
const jsonInput = document.querySelector('#json-input');
const toastRegion = document.querySelector('#toast-region');

const COLORS = {
  fill: ['#ffd66b', '#f7b5a4', '#c9f1e1', '#c7d7fb', '#e8c9ef', '#fffefb', '#344554'],
  stroke: ['#c79019', '#c15f4b', '#2d8068', '#526fa5', '#815c8a', '#9eaba9', '#344554'],
};

const state = {
  doc: createDocument('Workshop board'),
  boardId: null,
  booting: true,
  switching: false,
  activeTool: 'select',
  selectedIds: [],
  history: [],
  redo: [],
  drag: null,
  dirty: false,
  spacePressed: false,
  saveTimer: null,
  pointer: null,
  lastPaste: null,
  lastCopy: null,
};

const boardNameInput = document.querySelector('#board-name');
const boardMenuButton = document.querySelector('#board-menu-button');
const boardMenu = document.querySelector('#board-menu');
const boardListEl = document.querySelector('#board-list');
const LAST_BOARD_KEY = 'canvasroom-last-board';

function showToast(message, type = '') {
  const toast = document.createElement('div');
  toast.className = `toast ${type}`;
  toast.textContent = message;
  toastRegion.append(toast);
  setTimeout(() => toast.remove(), 3200);
}

function scheduleSave() {
  state.dirty = true;
  setSaveStatus('pending');
  clearTimeout(state.saveTimer);
  state.saveTimer = setTimeout(() => {
    state.saveTimer = null;
    state.dirty = false;
    if (fileSync.enabled) fileSync.push();
    else if (saveLocal(state.doc)) setSaveStatus('saved');
    else showToast('浏览器拒绝了本地保存', 'error');
  }, 500);
}

function setSaveStatus(kind, detail) {
  const labels = {
    pending: '未保存',
    saved: fileSync.enabled ? '已同步到文件' : '已保存到浏览器',
    offline: '同步服务未连接',
    error: detail ? `同步失败：${detail}` : '同步失败',
  };
  saveStatusEl.classList.toggle('unsaved', kind !== 'saved');
  saveStatusEl.replaceChildren(document.createElement('i'), document.createTextNode(` ${labels[kind]}`));
}

function isEditingText() {
  return document.activeElement?.isContentEditable === true;
}

function adoptRemoteDocument(doc, conflict) {
  const keepViewport = state.doc.viewport;
  state.history.push(cloneDocument(state.doc));
  if (state.history.length > 80) state.history.shift();
  state.redo = [];
  state.doc = { ...doc, viewport: keepViewport };
  state.selectedIds = state.selectedIds.filter((id) => doc.elements.some((element) => element.id === id));
  if (document.activeElement !== boardNameInput) boardNameInput.value = state.doc.name;
  render();
  setSaveStatus('saved');
  showToast(conflict ? '画布文件已被外部修改，已载入最新内容' : '画布已从文件更新');
}

const fileSync = createFileSync({
  getDoc: () => state.doc,
  isBusy: () => state.switching || Boolean(state.drag) || state.dirty || state.saveTimer !== null || isEditingText(),
  onRemoteChange: adoptRemoteDocument,
  onStatus: setSaveStatus,
  onMissing: (id) => { if (id === state.boardId && !state.switching) withSwitch(() => openFallbackBoard('当前画布文件已被删除，已切换到其他画布')); },
});

async function withSwitch(task) {
  if (state.switching) return;
  state.switching = true;
  try {
    await task();
  } catch (error) {
    showToast(error.message || '画布操作失败', 'error');
  } finally {
    state.switching = false;
  }
}

function rememberBoard(id) {
  try { localStorage.setItem(LAST_BOARD_KEY, id); } catch { /* storage unavailable */ }
  history.replaceState(null, '', `#board=${id}`);
}

function boardIdFromHash() {
  return location.hash.match(/^#board=([a-z0-9-]+)$/)?.[1] ?? null;
}

function lastBoardId() {
  try { return localStorage.getItem(LAST_BOARD_KEY); } catch { return null; }
}

function isDefaultViewport({ x, y, zoom }) {
  return x === 0 && y === 0 && zoom === 1;
}

// Writes any pending edit to the board being left, then loads the requested board.
async function openBoard(id) {
  if (state.saveTimer) {
    clearTimeout(state.saveTimer);
    state.saveTimer = null;
    state.dirty = false;
    await fileSync.push();
  }
  const doc = await fileSync.open(id);
  state.boardId = id;
  state.doc = doc;
  state.selectedIds = [];
  state.history = [];
  state.redo = [];
  state.drag = null;
  state.lastPaste = null;
  boardNameInput.value = doc.name;
  render();
  // Boards made outside the app (e.g. over MCP) have no saved view yet.
  if (doc.elements.length && isDefaultViewport(doc.viewport)) fitBoard();
  rememberBoard(id);
  setSaveStatus('saved');
}

async function openFallbackBoard(message) {
  const boards = await boardsApi.list();
  const next = boards.find((board) => board.id !== state.boardId)?.id ?? (await boardsApi.create({ name: '未命名画布' })).id;
  await openBoard(next);
  if (message) showToast(message);
}

async function resolveStartBoard() {
  const boards = await boardsApi.list();
  const wanted = [boardIdFromHash(), lastBoardId()].find((id) => id && boards.some((board) => board.id === id));
  if (wanted) return wanted;
  if (boards.length) return boards[0].id;
  // First run with the server: keep whatever this browser had as the first board.
  const legacy = loadLocal();
  return (await boardsApi.create(legacy ? { doc: legacy } : { name: '我的第一个画布' })).id;
}

function relativeTime(iso) {
  const seconds = Math.max(0, (Date.now() - Date.parse(iso)) / 1000);
  if (seconds < 60) return '刚刚';
  if (seconds < 3600) return `${Math.floor(seconds / 60)} 分钟前`;
  if (seconds < 86400) return `${Math.floor(seconds / 3600)} 小时前`;
  if (seconds < 86400 * 7) return `${Math.floor(seconds / 86400)} 天前`;
  return new Date(iso).toLocaleDateString('zh-CN');
}

function renderBoardList(boards) {
  if (!boards.length) {
    boardListEl.innerHTML = '<li class="board-list-note">还没有画布</li>';
    return;
  }
  boardListEl.replaceChildren(...boards.map((board) => {
    const current = board.id === state.boardId;
    const item = document.createElement('li');
    const button = document.createElement('button');
    button.type = 'button';
    button.className = `board-item${current ? ' current' : ''}`;
    button.dataset.boardId = board.id;
    if (current) button.setAttribute('aria-current', 'true');
    const name = document.createElement('span');
    name.className = 'board-item-name';
    name.textContent = current ? state.doc.name : board.name;
    const meta = document.createElement('span');
    meta.className = 'board-item-meta';
    const count = current ? state.doc.elements.length : board.elementCount;
    meta.textContent = `${board.id}.json · ${count} 个元素 · ${relativeTime(board.updatedAt)}`;
    button.append(name, meta);
    item.append(button);
    return item;
  }));
}

async function openBoardMenu() {
  boardMenu.hidden = false;
  boardMenuButton.setAttribute('aria-expanded', 'true');
  boardListEl.innerHTML = '<li class="board-list-note">正在读取…</li>';
  try {
    renderBoardList(await boardsApi.list());
  } catch {
    boardListEl.innerHTML = '<li class="board-list-note error">读取画布列表失败</li>';
  }
}

function closeBoardMenu() {
  boardMenu.hidden = true;
  boardMenuButton.setAttribute('aria-expanded', 'false');
}

async function runBoardAction(action) {
  closeBoardMenu();
  if (action === 'new') {
    await withSwitch(async () => {
      await openBoard((await boardsApi.create({ name: '未命名画布' })).id);
      boardNameInput.focus();
      boardNameInput.select();
    });
  } else if (action === 'duplicate') {
    await withSwitch(async () => {
      const { id } = await boardsApi.create({ doc: { ...state.doc, name: `${state.doc.name} 副本` } });
      await openBoard(id);
      showToast('已创建副本');
    });
  } else if (action === 'delete') {
    if (!window.confirm(`删除画布“${state.doc.name}”？\n文件会移到 boards/.trash 文件夹，需要时可以手动找回。`)) return;
    await withSwitch(async () => {
      clearTimeout(state.saveTimer);
      state.saveTimer = null;
      state.dirty = false;
      await fileSync.flush();
      await boardsApi.remove(state.boardId);
      await openFallbackBoard('画布已删除');
    });
  }
}

async function importBoardFiles(files) {
  if (!files.length) return;
  const results = await Promise.allSettled(files.map(readJsonFile));
  const docs = results.filter((result) => result.status === 'fulfilled').map((result) => result.value);
  const failed = results.length - docs.length;
  if (failed) showToast(files.length === 1 ? (results[0].reason?.message || '导入失败') : `${failed} 个文件不是有效的画布`, 'error');
  if (!docs.length) return;
  if (!fileSync.enabled) {
    // Browser-only mode keeps a single board, so the import replaces it.
    beginMutation();
    state.doc = docs[0];
    state.selectedIds = [];
    boardNameInput.value = docs[0].name;
    render();
    scheduleSave();
    showToast('画布已导入');
    return;
  }
  await withSwitch(async () => {
    let lastId = null;
    for (const doc of docs) lastId = (await boardsApi.create({ doc })).id;
    await openBoard(lastId);
    showToast(docs.length === 1 ? `已导入为新画布“${docs[0].name}”` : `已导入 ${docs.length} 个画布`);
  });
}

function beginMutation() {
  state.history.push(cloneDocument(state.doc));
  if (state.history.length > 80) state.history.shift();
  state.redo = [];
}

function mutate(callback) {
  beginMutation();
  callback();
  render();
  scheduleSave();
}

function getSelectedElements() {
  return state.doc.elements.filter((element) => state.selectedIds.includes(element.id));
}

function select(ids, additive = false) {
  const next = additive ? [...new Set([...state.selectedIds, ...ids])] : ids;
  state.selectedIds = next;
  renderSelection();
  updateInspector();
}

function selectOne(id, additive = false) {
  if (!id) return select([], false);
  select([id], additive);
}

function toWorldPoint(event) {
  const rect = viewportEl.getBoundingClientRect();
  return {
    x: (event.clientX - rect.left - state.doc.viewport.x) / state.doc.viewport.zoom,
    y: (event.clientY - rect.top - state.doc.viewport.y) / state.doc.viewport.zoom,
  };
}

function toScreenPoint(point) {
  return {
    x: point.x * state.doc.viewport.zoom + state.doc.viewport.x,
    y: point.y * state.doc.viewport.zoom + state.doc.viewport.y,
  };
}

function refreshTransform() {
  const { x, y, zoom } = state.doc.viewport;
  const transform = `translate(${x}px, ${y}px) scale(${zoom})`;
  worldEl.style.transform = transform;
  selectionLayerEl.style.transform = transform;
  svgWorldEl.setAttribute('transform', `translate(${x} ${y}) scale(${zoom})`);
  zoomValueEl.textContent = `${Math.round(zoom * 100)}%`;
}

function setZoom(zoom, point) {
  if (state.booting || state.switching || state.drag) return;
  const rect = viewportEl.getBoundingClientRect();
  const next = zoomAt(state.doc.viewport, zoom, point || { x: rect.width / 2, y: rect.height / 2 });
  if (next.zoom === state.doc.viewport.zoom) return;
  state.doc.viewport = next;
  refreshTransform();
  scheduleSave();
}

function onWheel(event) {
  if (!event.ctrlKey && event.target.closest?.('[contenteditable="true"]')) return;
  event.preventDefault();
  const rect = viewportEl.getBoundingClientRect();
  setZoom(wheelZoom(state.doc.viewport.zoom, event.deltaY, event.deltaMode, rect.height), { x: event.clientX - rect.left, y: event.clientY - rect.top });
}

function styleElementNode(node, element) {
  const style = element.style || {};
  node.style.left = `${element.x}px`;
  node.style.top = `${element.y}px`;
  node.style.width = `${Math.max(1, element.width)}px`;
  node.style.height = `${Math.max(1, element.height)}px`;
  node.style.transform = `rotate(${element.rotation || 0}deg)`;
  node.style.opacity = style.opacity ?? 1;
  node.style.zIndex = element.type === 'frame' ? 0 : String(2 + state.doc.elements.indexOf(element));
  if (element.type === 'sticky' || element.type === 'shape' || element.type === 'frame') {
    node.style.background = style.fill || 'transparent';
    node.style.border = `${style.strokeWidth || 1}px solid ${style.stroke || 'transparent'}`;
  }
  if (element.type === 'sticky') node.style.color = style.color || '#35434d';
  if (element.type === 'text' || element.type === 'sticky' || element.type === 'shape') {
    const text = node.querySelector('.element-text');
    if (text) {
      text.style.color = style.color || '#213342';
      text.style.fontSize = `${style.fontSize || (element.type === 'sticky' ? 18 : 22)}px`;
      text.style.fontWeight = style.fontWeight || 500;
      text.style.textAlign = style.align || (element.type === 'shape' ? 'center' : 'left');
    }
  }
  if (element.type === 'shape') {
    node.style.borderRadius = element.data.shape === 'circle' ? '50%' : element.data.shape === 'rounded' ? '18px' : '8px';
    if (element.data.shape === 'diamond') node.style.clipPath = 'polygon(50% 0, 100% 50%, 50% 100%, 0 50%)';
  }
}

function createDomElement(element) {
  const node = document.createElement('div');
  node.className = `board-element ${element.type}${element.locked ? ' locked' : ''}${state.selectedIds.includes(element.id) ? ' selected' : ''}`;
  node.dataset.elementId = element.id;
  styleElementNode(node, element);
  if (element.type === 'image') {
    const image = document.createElement('img');
    image.alt = element.data.name || 'Uploaded image';
    image.src = element.data.src;
    node.append(image);
  } else if (element.type === 'frame') {
    const title = document.createElement('div');
    title.className = 'frame-title';
    title.textContent = element.data.title || 'Section';
    node.append(title);
  } else if (element.type === 'sticky' || element.type === 'text' || element.type === 'shape') {
    const text = document.createElement('div');
    text.className = 'element-text';
    text.textContent = element.data.text || '';
    node.append(text);
  }
  return node;
}

function createSvgLine(element) {
  const node = document.createElementNS('http://www.w3.org/2000/svg', 'line');
  const x2 = Number(element.data.x2 ?? element.width);
  const y2 = Number(element.data.y2 ?? element.height);
  node.setAttribute('x1', element.x);
  node.setAttribute('y1', element.y);
  node.setAttribute('x2', element.x + x2);
  node.setAttribute('y2', element.y + y2);
  node.setAttribute('stroke', element.style.stroke || '#344554');
  node.setAttribute('stroke-width', element.style.strokeWidth || 3);
  node.setAttribute('stroke-linecap', 'round');
  node.setAttribute('data-element-id', element.id);
  node.classList.add('svg-element');
  if (state.selectedIds.includes(element.id)) {
    node.setAttribute('stroke', '#ec775e');
    node.setAttribute('stroke-width', Number(element.style.strokeWidth || 3) + 2);
  }
  if (element.data.arrow || element.data.connector) node.setAttribute('marker-end', `url(#${state.selectedIds.includes(element.id) ? 'arrow-selected' : 'arrow-end'})`);
  return node;
}

function createSvgPath(element) {
  const node = document.createElementNS('http://www.w3.org/2000/svg', 'polyline');
  const points = element.data.points || [];
  node.setAttribute('points', points.map((point) => `${point.x},${point.y}`).join(' '));
  node.setAttribute('fill', 'none');
  node.setAttribute('stroke', state.selectedIds.includes(element.id) ? '#ec775e' : (element.style.stroke || '#344554'));
  node.setAttribute('stroke-width', element.style.strokeWidth || 4);
  node.setAttribute('stroke-linecap', 'round');
  node.setAttribute('stroke-linejoin', 'round');
  node.setAttribute('data-element-id', element.id);
  node.classList.add('svg-element');
  return node;
}

function render() {
  refreshConnectors(state.doc);
  worldEl.replaceChildren();
  svgWorldEl.replaceChildren();
  state.doc.elements.forEach((element) => {
    if (element.type === 'line') svgWorldEl.append(createSvgLine(element));
    else if (element.type === 'path') svgWorldEl.append(createSvgPath(element));
    else worldEl.append(createDomElement(element));
  });
  emptyStateEl.hidden = state.booting || state.doc.elements.length > 0;
  refreshTransform();
  renderSelection();
  updateInspector();
  updateButtons();
}

function renderSelection() {
  selectionLayerEl.replaceChildren();
  if (state.selectedIds.length !== 1) return;
  const element = getSelectedElements()[0];
  if (!element || element.type === 'line' || element.type === 'path' || element.type === 'image' && !element.width) return;
  const bounds = getElementBounds(element);
  const box = document.createElement('div');
  box.className = 'selection-box';
  box.style.left = `${bounds.x}px`;
  box.style.top = `${bounds.y}px`;
  box.style.width = `${Math.max(1, bounds.width)}px`;
  box.style.height = `${Math.max(1, bounds.height)}px`;
  box.style.transform = `rotate(${element.rotation || 0}deg)`;
  for (const handle of ['nw', 'ne', 'sw', 'se']) {
    const node = document.createElement('button');
    node.className = `selection-handle ${handle}`;
    node.type = 'button';
    node.dataset.handle = handle;
    node.dataset.elementId = element.id;
    node.setAttribute('aria-label', `调整大小 ${handle}`);
    box.append(node);
  }
  const rotate = document.createElement('button');
  rotate.className = 'rotate-handle'; rotate.type = 'button'; rotate.dataset.handle = 'rotate'; rotate.dataset.elementId = element.id; rotate.innerHTML = icon('rotate', 12);
  rotate.setAttribute('aria-label', '旋转'); box.append(rotate);
  selectionLayerEl.append(box);
}

function updateButtons() {
  document.querySelector('#undo-button').disabled = state.history.length === 0;
  document.querySelector('#redo-button').disabled = state.redo.length === 0;
  document.querySelectorAll('[data-tool]').forEach((button) => button.classList.toggle('active', button.dataset.tool === state.activeTool));
  selectionCountEl.textContent = state.selectedIds.length ? `${state.selectedIds.length} 个元素已选择` : '未选择';
}

function updateInspector() {
  const selected = getSelectedElements();
  inspectorEmptyEl.hidden = selected.length > 0;
  inspectorContentEl.hidden = selected.length === 0;
  if (selected.length && window.innerWidth <= 860) inspectorEl.classList.add('open');
  if (!selected.length) return;
  const element = selected[0];
  document.querySelector('#inspector-type').textContent = element.type.toUpperCase();
  const fillSwatches = document.querySelector('#fill-swatches');
  const strokeSwatches = document.querySelector('#stroke-swatches');
  fillSwatches.replaceChildren(); strokeSwatches.replaceChildren();
  COLORS.fill.forEach((color) => fillSwatches.append(createSwatch('fill', color, element.style.fill)));
  COLORS.stroke.forEach((color) => strokeSwatches.append(createSwatch('stroke', color, element.style.stroke)));
  const opacity = Number(element.style.opacity ?? 1);
  document.querySelector('#opacity-control').value = opacity;
  document.querySelector('#opacity-value').textContent = `${Math.round(opacity * 100)}%`;
  document.querySelector('#font-size-control').value = String(element.style.fontSize || 20);
  document.querySelectorAll('.text-controls').forEach((node) => { node.hidden = !['text', 'sticky', 'shape'].includes(element.type); });
  document.querySelectorAll('[data-format]').forEach((button) => button.classList.toggle('active', button.dataset.format === 'bold' && Number(element.style.fontWeight) >= 700));
}

function createSwatch(kind, color, activeColor) {
  const swatch = document.createElement('button');
  swatch.className = `swatch${color === activeColor ? ' active' : ''}`;
  swatch.style.background = color;
  swatch.type = 'button';
  swatch.dataset.colorKind = kind;
  swatch.dataset.color = color;
  swatch.setAttribute('aria-label', `${kind === 'fill' ? '填充' : '边框'} ${color}`);
  return swatch;
}

function addAtPoint(type, point, overrides = {}) {
  const element = createElement(type, { x: point.x, y: point.y, ...overrides });
  state.doc.elements.push(element);
  state.selectedIds = [element.id];
  state.activeTool = 'select';
  render();
  scheduleSave();
  return element;
}

const MAX_IMAGE_BYTES = 8 * 1024 * 1024;
const MAX_IMAGE_SIDE = 480;

function readFileAsDataUrl(file) {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(reader.result);
    reader.onerror = () => reject(reader.error);
    reader.readAsDataURL(file);
  });
}

function naturalImageSize(src) {
  return new Promise((resolve) => {
    const image = new Image();
    image.onload = () => resolve({ width: image.naturalWidth, height: image.naturalHeight });
    image.onerror = () => resolve(null);
    image.src = src;
  });
}

// Builds an image element that keeps the picture's aspect ratio.
async function imageElementFromFile(file) {
  if (!file?.type?.startsWith('image/')) throw new Error('请选择图片文件');
  if (file.size > MAX_IMAGE_BYTES) throw new Error('图片不能超过 8 MB');
  const src = await readFileAsDataUrl(file);
  const natural = await naturalImageSize(src);
  if (!natural?.width || !natural?.height) throw new Error('图片读取失败');
  const scale = Math.min(1, MAX_IMAGE_SIDE / Math.max(natural.width, natural.height));
  return createElement('image', {
    width: Math.max(24, Math.round(natural.width * scale)),
    height: Math.max(24, Math.round(natural.height * scale)),
    data: { src, name: file.name || '粘贴的图片' },
  });
}

function insertElements(elements, point) {
  if (point) centerOn(elements, point);
  mutate(() => {
    state.doc.elements.push(...elements);
    state.selectedIds = elements.map((element) => element.id);
  });
  setTool('select');
}

async function addImage(file, point) {
  try {
    insertElements([await imageElementFromFile(file)], point);
  } catch (error) {
    showToast(error.message || '图片读取失败', 'error');
  }
}

function viewportCenter() {
  const rect = viewportEl.getBoundingClientRect();
  return toWorldPoint({ clientX: rect.left + rect.width / 2, clientY: rect.top + rect.height / 2 });
}

// Pastes land under the pointer; repeated pastes without moving step down-right so they don't stack.
function nextPastePoint() {
  const base = state.pointer || viewportCenter();
  const repeat = state.lastPaste && Math.hypot(state.lastPaste.x - base.x, state.lastPaste.y - base.y) < 1;
  const count = repeat ? state.lastPaste.count + 1 : 0;
  state.lastPaste = { x: base.x, y: base.y, count };
  const step = 24 / state.doc.viewport.zoom;
  return { x: base.x + count * step, y: base.y + count * step };
}

function boardElementsFromClipboard(html, text) {
  const fromPayload = readBoardClipboardHtml(html) || parseBoardClipboard(text);
  if (fromPayload) return fromPayload;
  // Some clipboard readers strip our HTML payload; fall back to the last in-app copy.
  if (state.lastCopy && text && text === state.lastCopy.text) return state.lastCopy.elements;
  return null;
}

async function pasteContent({ files = [], html = '', text = '' }) {
  const point = nextPastePoint();
  const boardElements = boardElementsFromClipboard(html, text);
  if (boardElements) return insertElements(cloneForPaste(boardElements, point), point);

  const images = files.filter((file) => file.type?.startsWith('image/'));
  if (images.length) {
    const results = await Promise.allSettled(images.map(imageElementFromFile));
    const elements = results.filter((result) => result.status === 'fulfilled').map((result) => result.value);
    const failed = results.find((result) => result.status === 'rejected');
    if (failed) showToast(failed.reason?.message || '图片读取失败', 'error');
    let x = 0;
    for (const element of elements) { element.x = x; element.y = 0; x += element.width + 24; }
    if (elements.length) insertElements(elements, point);
    return;
  }
  if (files.length) return showToast('只能粘贴图片文件', 'error');

  const clean = text.replace(/\r\n?/g, '\n').trim();
  if (clean) {
    const size = textElementSize(clean);
    return insertElements([createElement('text', { ...size, data: { text: clean } })], point);
  }
  showToast('剪贴板里没有可以粘贴的内容');
}

function isTypingTarget(target) {
  return Boolean(target?.closest?.('input, textarea, select, [contenteditable="true"]'));
}

function handlePaste(event) {
  const editable = event.target?.closest?.('[contenteditable="true"]');
  if (editable) {
    // Keep in-place text editing plain, without pasted formatting or images.
    event.preventDefault();
    document.execCommand('insertText', false, event.clipboardData?.getData('text/plain') || '');
    return;
  }
  if (isTypingTarget(event.target) || !event.clipboardData) return;
  event.preventDefault();
  const data = event.clipboardData;
  pasteContent({ files: [...data.files], html: data.getData('text/html'), text: data.getData('text/plain') });
}

function handleCopy(event, cut = false) {
  if (isTypingTarget(event.target)) return;
  const selected = getSelectedElements();
  if (!selected.length || !event.clipboardData) return;
  event.preventDefault();
  const text = clipboardPlainText(selected) || serializeBoardClipboard(selected);
  event.clipboardData.setData('text/html', boardClipboardHtml(selected));
  event.clipboardData.setData('text/plain', text);
  state.lastCopy = { text, elements: JSON.parse(JSON.stringify(selected)) };
  if (cut) deleteSelected();
}

function duplicateSelection() {
  const selected = getSelectedElements();
  if (!selected.length) return;
  const bounds = elementsBounds(selected);
  const step = 24 / state.doc.viewport.zoom;
  const point = { x: bounds.x + bounds.width / 2 + step, y: bounds.y + bounds.height / 2 + step };
  insertElements(cloneForPaste(selected, point), point);
}

async function pasteFromClipboardButton() {
  try {
    if (!navigator.clipboard?.read) {
      await pasteContent({ text: await navigator.clipboard.readText() });
      return;
    }
    const files = [];
    let html = '';
    let text = '';
    for (const item of await navigator.clipboard.read()) {
      for (const type of item.types) {
        if (type.startsWith('image/')) files.push(new File([await item.getType(type)], `clipboard.${type.split('/')[1]}`, { type }));
        else if (type === 'text/html' && !html) html = await (await item.getType(type)).text();
        else if (type === 'text/plain' && !text) text = await (await item.getType(type)).text();
      }
    }
    await pasteContent({ files, html, text });
  } catch {
    showToast('浏览器没有允许读取剪贴板，可以直接按 Ctrl/Cmd + V 粘贴', 'error');
  }
}

function deleteSelected() {
  if (!state.selectedIds.length || !getSelectedElements().some((element) => !element.locked)) return;
  mutate(() => { state.doc.elements = state.doc.elements.filter((element) => !state.selectedIds.includes(element.id) || element.locked); state.selectedIds = []; });
}

function moveSelected(dx, dy) {
  getSelectedElements().forEach((element) => { if (!element.locked) { element.x += dx; element.y += dy; } });
}

function snapPoint(point) {
  let closest = point;
  let distance = 18 / state.doc.viewport.zoom;
  for (const element of state.doc.elements) {
    if (state.selectedIds.includes(element.id) || ['line', 'path', 'frame'].includes(element.type)) continue;
    const bounds = getElementBounds(element);
    const candidates = [
      { x: bounds.x + bounds.width / 2, y: bounds.y },
      { x: bounds.x + bounds.width, y: bounds.y + bounds.height / 2 },
      { x: bounds.x + bounds.width / 2, y: bounds.y + bounds.height },
      { x: bounds.x, y: bounds.y + bounds.height / 2 },
    ];
    for (const candidate of candidates) {
      const nextDistance = Math.hypot(candidate.x - point.x, candidate.y - point.y);
      if (nextDistance < distance) { closest = candidate; distance = nextDistance; }
    }
  }
  return closest;
}

function finishDrag() {
  if (!state.drag) return;
  if (state.drag.kind === 'marquee') {
    const box = state.drag.box;
    const ids = state.doc.elements.filter((element) => {
      if (element.type === 'line' || element.type === 'path') return false;
      const bounds = getElementBounds(element);
      return bounds.x < box.x + box.width && bounds.x + bounds.width > box.x && bounds.y < box.y + box.height && bounds.y + bounds.height > box.y;
    }).map((element) => element.id);
    select(ids, state.drag.additive);
  }
  state.drag = null;
  marqueeEl.hidden = true;
  viewportEl.classList.remove('panning');
  if (state.dirty) scheduleSave();
}

function onPointerDown(event) {
  const handle = event.target.closest?.('[data-handle]');
  if (handle && state.selectedIds.length === 1) {
    const element = getSelectedElements()[0];
    if (element && !element.locked) {
      beginMutation();
      state.drag = { kind: handle.dataset.handle === 'rotate' ? 'rotate' : 'resize', start: toWorldPoint(event), element: cloneDocument({ ...createDocument(), elements: [element] }).elements[0], handle: handle.dataset.handle };
      event.stopPropagation(); event.preventDefault();
    }
    return;
  }
  if (event.button === 1 || state.spacePressed) {
    state.drag = { kind: 'pan', start: { x: event.clientX, y: event.clientY }, viewport: { ...state.doc.viewport } };
    viewportEl.classList.add('panning'); event.preventDefault(); return;
  }
  const elementNode = event.target.closest?.('[data-element-id]');
  const id = elementNode?.dataset.elementId;
  const point = toWorldPoint(event);
  if (state.activeTool === 'select') {
    if (id) {
      const element = state.doc.elements.find((item) => item.id === id);
      selectOne(id, event.shiftKey);
      if (element && !element.locked) {
        beginMutation();
        state.drag = { kind: 'move', start: point, positions: getSelectedElements().map((item) => ({ id: item.id, x: item.x, y: item.y })) };
      }
    } else {
      state.drag = { kind: 'marquee', start: point, box: { x: point.x, y: point.y, width: 0, height: 0 }, additive: event.shiftKey };
      if (!event.shiftKey) select([], false);
    }
    event.preventDefault(); return;
  }
  if (state.activeTool === 'sticky') { beginMutation(); addAtPoint('sticky', { x: point.x - 94, y: point.y - 84 }); return; }
  if (state.activeTool === 'text') { beginMutation(); addAtPoint('text', { x: point.x - 110, y: point.y - 25 }); return; }
  if (state.activeTool === 'shape') { beginMutation(); addAtPoint('shape', { x: point.x - 90, y: point.y - 60 }); return; }
  if (state.activeTool === 'frame') { beginMutation(); addAtPoint('frame', { x: point.x - 210, y: point.y - 140 }); return; }
  if (state.activeTool === 'image') { imageInput.value = ''; imageInput.click(); state.drag = { kind: 'image', point }; return; }
  if (state.activeTool === 'connector' || state.activeTool === 'pen') {
    beginMutation();
    const element = createElement(state.activeTool === 'pen' ? 'path' : 'line', { x: point.x, y: point.y, data: state.activeTool === 'pen' ? { points: [point] } : { x2: 0, y2: 0, arrow: true, connector: state.activeTool === 'connector' } });
    state.doc.elements.push(element); state.selectedIds = [element.id];
    state.drag = { kind: state.activeTool === 'pen' ? 'path' : 'line', id: element.id, start: point };
    event.preventDefault();
  }
}

function onPointerMove(event) {
  if (!state.drag) return;
  if (state.drag.kind === 'pan') {
    state.doc.viewport.x = state.drag.viewport.x + event.clientX - state.drag.start.x;
    state.doc.viewport.y = state.drag.viewport.y + event.clientY - state.drag.start.y;
    refreshTransform(); return;
  }
  const point = toWorldPoint(event);
  if (state.drag.kind === 'move') {
    const dx = point.x - state.drag.start.x; const dy = point.y - state.drag.start.y;
    state.drag.positions.forEach((position) => { const element = state.doc.elements.find((item) => item.id === position.id); if (element) { element.x = position.x + dx; element.y = position.y + dy; } });
    render(); return;
  }
  if (state.drag.kind === 'line') {
    const element = state.doc.elements.find((item) => item.id === state.drag.id); if (!element) return;
    const end = snapPoint(point); element.data.x2 = end.x - element.x; element.data.y2 = end.y - element.y; element.width = Math.abs(element.data.x2); element.height = Math.abs(element.data.y2); render(); return;
  }
  if (state.drag.kind === 'path') {
    const element = state.doc.elements.find((item) => item.id === state.drag.id); if (!element) return;
    element.data.points.push(point); render(); return;
  }
  if (state.drag.kind === 'marquee') {
    const start = state.drag.start;
    state.drag.box = { x: Math.min(start.x, point.x), y: Math.min(start.y, point.y), width: Math.abs(point.x - start.x), height: Math.abs(point.y - start.y) };
    const topLeft = toScreenPoint(state.drag.box); marqueeEl.hidden = false; marqueeEl.style.left = `${topLeft.x}px`; marqueeEl.style.top = `${topLeft.y}px`; marqueeEl.style.width = `${state.drag.box.width * state.doc.viewport.zoom}px`; marqueeEl.style.height = `${state.drag.box.height * state.doc.viewport.zoom}px`; return;
  }
  if (state.drag.kind === 'resize') {
    const element = state.doc.elements.find((item) => item.id === state.selectedIds[0]); if (!element) return;
    const start = state.drag.start; const original = state.drag.element; const dx = point.x - start.x; const dy = point.y - start.y; const handle = state.drag.handle;
    const min = 24;
    if (handle.includes('e')) element.width = Math.max(min, original.width + dx);
    if (handle.includes('s')) element.height = Math.max(min, original.height + dy);
    if (handle.includes('w')) { const next = Math.max(min, original.width - dx); element.x = original.x + original.width - next; element.width = next; }
    if (handle.includes('n')) { const next = Math.max(min, original.height - dy); element.y = original.y + original.height - next; element.height = next; }
    render(); return;
  }
  if (state.drag.kind === 'rotate') {
    const element = state.doc.elements.find((item) => item.id === state.selectedIds[0]); if (!element) return;
    const original = state.drag.element; const center = { x: original.x + original.width / 2, y: original.y + original.height / 2 }; element.rotation = Math.round(Math.atan2(point.y - center.y, point.x - center.x) * 180 / Math.PI + 90); render();
  }
}

function onPointerUp() {
  const drag = state.drag;
  finishDrag();
  if (drag?.kind === 'line' || drag?.kind === 'path') state.activeTool = 'select';
  updateButtons();
  if (drag && ['move', 'line', 'path', 'resize', 'rotate'].includes(drag.kind)) scheduleSave();
}

function beginEditing(element, node) {
  if (element.locked || !node) return;
  const text = node.querySelector('.element-text'); if (!text) return;
  beginMutation();
  text.contentEditable = 'true'; text.focus();
  const range = document.createRange(); range.selectNodeContents(text); range.collapse(false);
  const selection = window.getSelection(); selection.removeAllRanges(); selection.addRange(range);
  const finish = () => { element.data.text = text.textContent; text.contentEditable = 'false'; text.removeEventListener('blur', finish); render(); scheduleSave(); };
  text.addEventListener('blur', finish);
}

function undo() {
  if (!state.history.length) return;
  state.redo.push(cloneDocument(state.doc)); state.doc = state.history.pop(); state.selectedIds = []; document.querySelector('#board-name').value = state.doc.name; render(); scheduleSave();
}

function redo() {
  if (!state.redo.length) return;
  state.history.push(cloneDocument(state.doc)); state.doc = state.redo.pop(); state.selectedIds = []; render(); scheduleSave();
}

function fitBoard() {
  const bounds = documentBounds(state.doc, 80); const rect = viewportEl.getBoundingClientRect();
  const zoom = Math.min(1.35, Math.max(.25, Math.min((rect.width - 100) / bounds.width, (rect.height - 100) / bounds.height)));
  state.doc.viewport.zoom = zoom; state.doc.viewport.x = (rect.width - bounds.width * zoom) / 2 - bounds.x * zoom; state.doc.viewport.y = (rect.height - bounds.height * zoom) / 2 - bounds.y * zoom; refreshTransform(); scheduleSave();
}

function setTool(tool) {
  state.activeTool = tool;
  viewportEl.className = `board-viewport tool-${tool}`;
  updateButtons();
}

function applyStyle(callback) {
  if (!state.selectedIds.length) return;
  mutate(() => getSelectedElements().forEach((element) => { if (!element.locked) callback(element); }));
}

function organize(action) {
  const selected = getSelectedElements();
  if (!selected.length) return;
  if (action === 'group') {
    if (selected.length < 2) return showToast('至少选择两个元素才能组合');
    const groupId = `group_${Date.now().toString(36)}`;
    applyStyle((element) => { element.data.groupId = groupId; }); return;
  }
  if (action === 'ungroup') return applyStyle((element) => { delete element.data.groupId; });
  if (action === 'lock') return applyStyle((element) => { element.locked = true; });
  if (action === 'unlock') return applyStyle((element) => { element.locked = false; });
  const indexes = selected.map((element) => state.doc.elements.indexOf(element)).sort((a, b) => a - b);
  mutate(() => {
    if (action === 'front') { const moving = state.doc.elements.filter((element) => state.selectedIds.includes(element.id)); state.doc.elements = state.doc.elements.filter((element) => !state.selectedIds.includes(element.id)).concat(moving); }
    if (action === 'back') { const moving = state.doc.elements.filter((element) => state.selectedIds.includes(element.id)); state.doc.elements = moving.concat(state.doc.elements.filter((element) => !state.selectedIds.includes(element.id))); }
    if (action === 'forward') indexes.slice().reverse().forEach((index) => { if (index < state.doc.elements.length - 1) [state.doc.elements[index], state.doc.elements[index + 1]] = [state.doc.elements[index + 1], state.doc.elements[index]]; });
    if (action === 'backward') indexes.forEach((index) => { if (index > 0) [state.doc.elements[index], state.doc.elements[index - 1]] = [state.doc.elements[index - 1], state.doc.elements[index]]; });
  });
}

function wireUI() {
  document.querySelector('#back-to-boards').addEventListener('click', async () => {
    if (state.switching) return;
    state.switching = true;
    const button = document.querySelector('#back-to-boards');
    const label = button.textContent;
    button.disabled = true;
    button.textContent = '正在保存…';
    try {
      const hasPendingEdits = state.dirty || state.saveTimer !== null;
      if (state.saveTimer) { clearTimeout(state.saveTimer); state.saveTimer = null; state.dirty = false; }
      if (fileSync.enabled) {
        if (!await fileSync.saveBeforeLeave(hasPendingEdits)) throw new Error('保存未成功，已保留当前内容。请重试或先导出 JSON 备份，再返回主页');
      } else if ((hasPendingEdits || saveStatusEl.classList.contains('unsaved')) && !saveLocal(state.doc)) throw new Error('保存失败，请先导出 JSON');
      location.assign('/');
    } catch (error) { showToast(error.message, 'error'); }
    finally { state.switching = false; button.disabled = false; button.textContent = label; }
  });
  document.querySelectorAll('[data-tool]').forEach((button) => button.addEventListener('click', () => setTool(button.dataset.tool)));
  document.querySelector('#undo-button').innerHTML = icon('undo'); document.querySelector('#redo-button').innerHTML = icon('redo'); document.querySelector('#fit-button').innerHTML = icon('fit');
  document.querySelectorAll('.tool-button[data-tool]').forEach((button) => { const names = { select: 'select', sticky: 'sticky', text: 'text', shape: 'shape', connector: 'connector', pen: 'pen', frame: 'frame', image: 'image' }; button.innerHTML = icon(names[button.dataset.tool]); });
  document.querySelector('#undo-button').addEventListener('click', undo); document.querySelector('#redo-button').addEventListener('click', redo); document.querySelector('#fit-button').addEventListener('click', fitBoard);
  document.querySelector('#zoom-in').addEventListener('click', () => setZoom(state.doc.viewport.zoom + .1));
  document.querySelector('#zoom-out').addEventListener('click', () => setZoom(state.doc.viewport.zoom - .1));
  document.querySelector('#board-name').addEventListener('input', (event) => { state.doc.name = event.target.value; scheduleSave(); });
  document.querySelector('#delete-button').addEventListener('click', deleteSelected);
  document.querySelector('#export-json-button').addEventListener('click', () => { try { downloadJson(state.doc); showToast('JSON 已导出'); } catch { showToast('导出失败', 'error'); } });
  document.querySelector('#export-png-button').addEventListener('click', async () => { try { await downloadPng(state.doc); showToast('PNG 已导出'); } catch { showToast('PNG 导出失败', 'error'); } });
  document.querySelector('#import-button').addEventListener('click', () => { jsonInput.value = ''; jsonInput.click(); });
  jsonInput.addEventListener('change', () => importBoardFiles([...(jsonInput.files || [])]));
  boardMenuButton.innerHTML = icon('chevron', 16);
  boardMenuButton.addEventListener('click', () => (boardMenu.hidden ? openBoardMenu() : closeBoardMenu()));
  boardListEl.addEventListener('click', (event) => {
    const id = event.target.closest('[data-board-id]')?.dataset.boardId;
    if (!id) return;
    closeBoardMenu();
    if (id !== state.boardId) withSwitch(() => openBoard(id));
  });
  boardMenu.querySelectorAll('[data-board-action]').forEach((button) => {
    button.insertAdjacentHTML('afterbegin', icon({ new: 'plus', duplicate: 'copy', delete: 'trash' }[button.dataset.boardAction], 15));
    button.addEventListener('click', () => runBoardAction(button.dataset.boardAction));
  });
  document.addEventListener('pointerdown', (event) => {
    if (!boardMenu.hidden && !event.target.closest('.board-switcher')) closeBoardMenu();
  });
  window.addEventListener('hashchange', () => {
    const id = boardIdFromHash();
    if (fileSync.enabled && id && id !== state.boardId) withSwitch(() => openBoard(id));
  });
  imageInput.addEventListener('change', () => { if (state.drag?.kind === 'image') addImage(imageInput.files?.[0], state.drag.point); state.drag = null; });
  document.querySelectorAll('[data-action]').forEach((button) => button.addEventListener('click', () => organize(button.dataset.action)));
  document.querySelector('#opacity-control').addEventListener('input', (event) => { document.querySelector('#opacity-value').textContent = `${Math.round(Number(event.target.value) * 100)}%`; applyStyle((element) => { element.style.opacity = Number(event.target.value); }); });
  document.querySelector('#font-size-control').addEventListener('change', (event) => applyStyle((element) => { element.style.fontSize = Number(event.target.value); }));
  document.querySelector('#fill-swatches').addEventListener('click', (event) => { const button = event.target.closest('[data-color]'); if (button) applyStyle((element) => { element.style.fill = button.dataset.color; }); });
  document.querySelector('#stroke-swatches').addEventListener('click', (event) => { const button = event.target.closest('[data-color]'); if (button) applyStyle((element) => { element.style.stroke = button.dataset.color; }); });
  document.querySelectorAll('[data-format]').forEach((button) => button.addEventListener('click', () => applyStyle((element) => { if (button.dataset.format === 'bold') element.style.fontWeight = Number(element.style.fontWeight) >= 700 ? 500 : 700; if (button.dataset.format.startsWith('align-')) element.style.align = button.dataset.format.replace('align-', ''); })));
  document.querySelector('#close-inspector').addEventListener('click', () => inspectorEl.classList.remove('open'));
  document.querySelector('#help-button').addEventListener('click', () => document.querySelector('#shortcuts-dialog').showModal());
  document.querySelector('#close-shortcuts').addEventListener('click', () => document.querySelector('#shortcuts-dialog').close());
  viewportEl.addEventListener('pointerdown', onPointerDown); viewportEl.addEventListener('pointermove', onPointerMove); viewportEl.addEventListener('pointerup', onPointerUp); viewportEl.addEventListener('pointercancel', onPointerUp);
  viewportEl.addEventListener('wheel', onWheel, { passive: false });
  viewportEl.addEventListener('dblclick', (event) => { const node = event.target.closest?.('.board-element'); if (!node) return; const element = state.doc.elements.find((item) => item.id === node.dataset.elementId); beginEditing(element, node); });
  viewportEl.addEventListener('contextmenu', (event) => event.preventDefault());
  viewportEl.addEventListener('pointermove', (event) => { state.pointer = toWorldPoint(event); });
  viewportEl.addEventListener('pointerleave', () => { state.pointer = null; });
  document.addEventListener('paste', handlePaste);
  document.addEventListener('copy', (event) => handleCopy(event));
  document.addEventListener('cut', (event) => handleCopy(event, true));
  const pasteButton = document.querySelector('#paste-button');
  pasteButton.innerHTML = icon('paste');
  pasteButton.addEventListener('click', pasteFromClipboardButton);
  window.addEventListener('keydown', (event) => {
    if (event.code === 'Space' && !event.repeat) { state.spacePressed = true; viewportEl.classList.add('panning'); }
    if (event.key === 'Escape') { state.drag = null; marqueeEl.hidden = true; setTool('select'); closeBoardMenu(); }
    if (event.key === 'Delete' || event.key === 'Backspace') { const active = document.activeElement; if (active?.contentEditable !== 'true' && active?.tagName !== 'INPUT') { event.preventDefault(); deleteSelected(); } }
    const modifier = event.metaKey || event.ctrlKey;
    if (modifier && event.key.toLowerCase() === 'z') { event.preventDefault(); event.shiftKey ? redo() : undo(); }
    if (modifier && event.key.toLowerCase() === 'd' && !isTypingTarget(event.target)) { event.preventDefault(); duplicateSelection(); }
    if (!modifier && !event.altKey && document.activeElement?.tagName !== 'INPUT' && document.activeElement?.contentEditable !== 'true') { const tool = { v: 'select', s: 'sticky', t: 'text', r: 'shape', c: 'connector', p: 'pen', f: 'frame', i: 'image' }[event.key.toLowerCase()]; if (tool) setTool(tool); }
  });
  window.addEventListener('keyup', (event) => { if (event.code === 'Space') { state.spacePressed = false; if (!state.drag) viewportEl.classList.remove('panning'); } });
}

async function boot() {
  wireUI();
  render();
  let online;
  try { online = await fileSync.probe(); } catch { location.replace('/'); return; }
  if (online) {
    state.switching = true;
    try {
      await openBoard(await resolveStartBoard());
    } catch (error) {
      showToast(error.message || '读取画布失败', 'error');
    } finally {
      state.switching = false;
    }
    fileSync.start();
  } else {
    // Opened without the local server: a single board kept in this browser.
    boardMenuButton.hidden = true;
    state.doc = loadLocal() || state.doc;
    boardNameInput.value = state.doc.name;
  }
  state.booting = false;
  render();
}

boot();
