import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdir, readdir, rm, utimes, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { createDocument } from '../src/model.js';
import { addElements, updateElements, deleteElements, summarizeBoard } from '../src/board-ops.mjs';
import { createBoardStore, readBoard, writeBoard } from '../src/board-store.mjs';
import { createCanvasroomServer } from '../server.mjs';
import { handleMessage } from '../mcp-server.mjs';

const projectRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const scratch = path.join(projectRoot, 'work', 'test-tmp');

async function scratchFile(name) {
  await mkdir(scratch, { recursive: true });
  const file = path.join(scratch, name);
  await rm(file, { force: true });
  return file;
}

async function scratchDir(name) {
  const dir = path.join(scratch, name);
  await rm(dir, { recursive: true, force: true });
  await mkdir(dir, { recursive: true });
  return dir;
}

function toolJson(response) {
  return JSON.parse(response.result.content[0].text);
}

test('adds stickies with text and color, auto-placing them when no position is given', () => {
  const doc = createDocument();
  const added = addElements(doc, [{ type: 'sticky', text: 'A', color: '#c9f1e1' }, { type: 'sticky', text: 'B' }]);
  assert.equal(doc.elements.length, 2);
  assert.equal(added[0].data.text, 'A');
  assert.equal(added[0].style.fill, '#c9f1e1');
  assert.notDeepEqual([added[0].x, added[0].y], [added[1].x, added[1].y]);
});

test('connects two elements with an arrow between their facing edges', () => {
  const doc = createDocument();
  const [a, b] = addElements(doc, [
    { type: 'shape', x: 0, y: 0, width: 100, height: 100 },
    { type: 'shape', x: 300, y: 0, width: 100, height: 100 },
  ]);
  const [line] = addElements(doc, [{ type: 'connector', from: a.id, to: b.id }]);
  assert.equal(line.type, 'line');
  assert.deepEqual([line.x, line.y], [100, 50]);
  assert.deepEqual([line.data.x2, line.data.y2], [200, 0]);
  assert.equal(line.data.connector, true);
});

test('rejects unknown element types and missing connector targets', () => {
  const doc = createDocument();
  assert.throws(() => addElements(doc, [{ type: 'video' }]), /Unsupported element type/);
  assert.throws(() => addElements(doc, [{ type: 'connector', from: 'nope', to: 'nada' }]), /not found/);
  assert.equal(doc.elements.length, 0);
});

test('updates elements by id and reports unknown ids', () => {
  const doc = createDocument();
  const [sticky] = addElements(doc, [{ type: 'sticky', text: 'Old', x: 0, y: 0 }]);
  const result = updateElements(doc, [{ id: sticky.id, text: 'New', x: 40, color: '#f7b5a4' }, { id: 'missing' }]);
  assert.deepEqual(result, { updated: [sticky.id], locked: [], missing: ['missing'] });
  assert.equal(sticky.data.text, 'New');
  assert.equal(sticky.x, 40);
  assert.equal(sticky.style.fill, '#f7b5a4');
});

test('deletes elements but keeps locked ones', () => {
  const doc = createDocument();
  const [a, b] = addElements(doc, [{ type: 'sticky' }, { type: 'sticky', locked: true }]);
  const result = deleteElements(doc, [a.id, b.id, 'missing']);
  assert.deepEqual(result, { deleted: [a.id], locked: [b.id], missing: ['missing'] });
  assert.deepEqual(doc.elements.map((element) => element.id), [b.id]);
});

test('re-routes attached connectors on move and removes them with their endpoint', () => {
  const doc = createDocument();
  const [a, b, line] = addElements(doc, [
    { type: 'shape', key: 'a', x: 0, y: 0, width: 100, height: 100 },
    { type: 'shape', key: 'b', x: 300, y: 0, width: 100, height: 100 },
    { type: 'connector', from: 'a', to: 'b' },
  ]);
  updateElements(doc, [{ id: b.id, x: 0, y: 300 }]);
  assert.deepEqual([line.x, line.y, line.data.x2, line.data.y2], [50, 100, 0, 200]);
  const result = deleteElements(doc, [a.id]);
  assert.deepEqual(result.deleted, [a.id, line.id]);
  assert.deepEqual(doc.elements.map((element) => element.id), [b.id]);
});

test('summarizes the board for agents without embedding image data', () => {
  const doc = createDocument('Retro');
  addElements(doc, [{ type: 'sticky', text: 'Went well', x: 10.4, y: 20.6 }]);
  doc.elements.push({ ...doc.elements[0], id: 'img', type: 'image', data: { src: 'data:image/png;base64,AAAA', name: 'shot.png' } });
  const summary = summarizeBoard(doc);
  assert.equal(summary.name, 'Retro');
  assert.equal(summary.elementCount, 2);
  assert.equal(summary.elements[0].text, 'Went well');
  assert.equal(summary.elements[0].x, 10);
  assert.equal(JSON.stringify(summary).includes('base64'), false);
});

test('writes and reads a board file', async () => {
  const file = await scratchFile('store.json');
  assert.equal(await readBoard(file), null);
  const doc = createDocument('Stored');
  addElements(doc, [{ type: 'text', text: 'hello' }]);
  await writeBoard(doc, file);
  assert.deepEqual(await readBoard(file), doc);
});

test('board store derives unique ids from names and rejects unsafe ids', async () => {
  const store = createBoardStore(await scratchDir('store-ids'));
  const first = await store.create(createDocument('Sprint Retro'));
  const second = await store.create(createDocument('Sprint Retro'));
  const chinese = await store.create(createDocument('项目复盘'));
  assert.equal(first.id, 'sprint-retro');
  assert.equal(second.id, 'sprint-retro-2');
  assert.match(chinese.id, /^board-[a-z0-9]+$/);
  assert.equal((await store.read(chinese.id)).doc.name, '项目复盘');
  for (const bad of ['../package', 'a/b', '', '.trash', 'x'.repeat(80)]) {
    await assert.rejects(store.read(bad), /Invalid board id/);
  }
});

test('board store lists boards newest first, skips broken files, and moves deleted boards to trash', async () => {
  const dir = await scratchDir('store-list');
  const store = createBoardStore(dir);
  const doc = createDocument('Old');
  addElements(doc, [{ type: 'sticky' }]);
  await store.write('old', doc);
  await store.write('new', createDocument('New'));
  await utimes(path.join(dir, 'old.json'), new Date('2026-01-01'), new Date('2026-01-01'));
  await writeFile(path.join(dir, 'broken.json'), 'not json', 'utf8');

  const boards = await store.list();
  assert.deepEqual(boards.map((board) => board.id), ['new', 'old']);
  assert.equal(boards[1].name, 'Old');
  assert.equal(boards[1].elementCount, 1);
  assert.ok(boards[0].updatedAt > boards[1].updatedAt);
  assert.equal(await store.mostRecent(), 'new');

  await store.remove('old');
  assert.equal((await store.read('old')).doc, null);
  const trash = await readdir(path.join(dir, '.trash'));
  assert.equal(trash.length, 1);
  assert.match(trash[0], /^old-\d+\.json$/);
  await assert.rejects(store.remove('old'), /not found/);
});

test('server lists, creates, saves, and deletes boards', async () => {
  const dir = await scratchDir('server');
  const store = createBoardStore(dir);
  await store.write('alpha', createDocument('Alpha'));
  const server = createCanvasroomServer({ root: projectRoot, boardsDir: dir });
  await new Promise((resolve) => server.listen(0, '127.0.0.1', resolve));
  const base = `http://127.0.0.1:${server.address().port}`;
  const send = (method, url, body) => fetch(`${base}${url}`, { method, headers: body ? { 'content-type': 'application/json' } : undefined, body: body ? JSON.stringify(body) : undefined });
  try {
    const listed = await (await send('GET', '/api/boards')).json();
    const crossSite = await fetch(`${base}/api/boards`, { method: 'POST', headers: { origin: 'https://other.example', 'content-type': 'application/json' }, body: JSON.stringify({ name: 'Unwanted' }) });
    assert.equal(crossSite.status, 403);
    assert.equal((await fetch(`${base}/api/boards/alpha`, { method: 'DELETE', headers: { origin: 'null' } })).status, 403);
    assert.equal((await fetch(`${base}/api/boards/alpha`, { method: 'DELETE', headers: { 'sec-fetch-site': 'cross-site' } })).status, 403);
    assert.equal((await fetch(`${base}/api/boards`, { method: 'POST', headers: { 'content-type': 'text/plain' }, body: JSON.stringify({ name: 'Unwanted' }) })).status, 415);
    const sameSite = await fetch(`${base}/api/boards`, { method: 'POST', headers: { origin: base, 'content-type': 'application/json' }, body: JSON.stringify({ name: 'Same origin' }) });
    assert.equal(sameSite.status, 201);
    assert.deepEqual(listed.boards.map((board) => [board.id, board.name, board.elementCount]), [['alpha', 'Alpha', 0]]);

    const created = await send('POST', '/api/boards', { name: 'Sprint Retro' });
    assert.equal(created.status, 201);
    assert.deepEqual(await created.json().then(({ id, name }) => [id, name]), ['sprint-retro', 'Sprint Retro']);

    const copy = await (await send('POST', '/api/boards', { sourceId: 'alpha' })).json();
    assert.equal(copy.name, 'Alpha 副本');
    assert.equal((await store.read(copy.id)).doc.name, 'Alpha 副本');

    const imported = createDocument('Imported');
    addElements(imported, [{ type: 'text', text: 'hi' }]);
    const importedRes = await (await send('POST', '/api/boards', { doc: imported })).json();
    assert.equal((await store.read(importedRes.id)).doc.elements.length, 1);

    assert.equal((await send('POST', '/api/boards', { doc: { version: 9 } })).status, 400);
    assert.equal((await send('POST', '/api/boards', { sourceId: 'missing' })).status, 404);
    assert.equal((await send('GET', '/api/boards/missing')).status, 404);
    assert.equal((await send('GET', '/api/boards/..%2Fpackage')).status, 400);

    const first = await (await send('GET', '/api/boards/alpha')).json();
    const saved = await send('PUT', '/api/boards/alpha', { baseRevision: first.revision, doc: { ...first.doc, name: 'Renamed' } });
    assert.equal(saved.status, 200);
    const { revision } = await saved.json();
    assert.equal((await store.read('alpha')).doc.name, 'Renamed');

    const external = (await store.read('alpha')).doc;
    external.name = 'External';
    await store.write('alpha', external);
    const stale = await send('PUT', '/api/boards/alpha', { baseRevision: revision, doc: { ...first.doc, name: 'Mine' } });
    assert.equal(stale.status, 409);
    assert.equal((await store.read('alpha')).doc.name, 'External');
    const invalid = await send('PUT', '/api/boards/alpha', { baseRevision: (await stale.json()).revision, doc: { version: 9 } });
    assert.equal(invalid.status, 400);

    assert.equal((await send('DELETE', '/api/boards/sprint-retro')).status, 200);
    assert.equal((await send('GET', '/api/boards/sprint-retro')).status, 404);
    assert.equal((await send('DELETE', '/api/boards/sprint-retro')).status, 404);
    const after = await (await send('GET', '/api/boards')).json();
    assert.equal(after.boards.some((board) => board.id === 'sprint-retro'), false);

    assert.equal((await fetch(`${base}/index.html`)).status, 200);
    assert.equal((await fetch(`${base}/src/board.js`)).status, 200);
    assert.equal((await fetch(`${base}/src/board-ops.mjs`)).status, 200);
    assert.deepEqual(await (await fetch(`${base}/api/health`)).json(), { status: 'ok' });
    for (const file of ['/package.json', '/server.mjs', '/src/board-store.mjs', '/boards/.gitkeep', '/.git/config']) {
      assert.equal((await fetch(`${base}${file}`)).status, 404, file);
    }
    assert.equal((await fetch(`${base}/..%2Fpackage.json`)).status, 404);
  } finally {
    server.close();
    server.closeAllConnections();
  }
});

test('MCP server works across multiple boards', async () => {
  const dir = await scratchDir('mcp');
  process.env.CANVASROOM_BOARDS_DIR = dir;
  const call = (id, name, args = {}) => handleMessage({ jsonrpc: '2.0', id, method: 'tools/call', params: { name, arguments: args } });
  try {
    const init = await handleMessage({ jsonrpc: '2.0', id: 1, method: 'initialize', params: { protocolVersion: '2025-06-18', capabilities: {}, clientInfo: { name: 'test', version: '1' } } });
    assert.equal(init.result.serverInfo.name, 'canvasroom');
    assert.equal(await handleMessage({ jsonrpc: '2.0', method: 'notifications/initialized' }), null);

    const list = await handleMessage({ jsonrpc: '2.0', id: 2, method: 'tools/list' });
    const names = list.result.tools.map((tool) => tool.name);
    for (const name of ['list_boards', 'create_board', 'get_board', 'add_elements', 'update_elements', 'delete_elements', 'rename_board']) assert.ok(names.includes(name), name);

    const created = toolJson(await call(3, 'create_board', { name: 'Ideas', elements: [{ type: 'sticky', text: 'First' }] }));
    assert.equal(created.board, 'ideas');

    const defaulted = toolJson(await call(4, 'add_elements', { elements: [{ type: 'sticky', text: 'Default target' }] }));
    assert.equal(defaulted.board, 'ideas');

    assert.equal(toolJson(await call(5, 'create_board', { name: 'Plan' })).board, 'plan');
    await call(6, 'add_elements', { board: 'ideas', elements: [{ type: 'sticky', text: 'Explicit' }] });

    const boards = toolJson(await call(7, 'list_boards')).boards;
    assert.deepEqual(boards.map((board) => board.id).sort(), ['ideas', 'plan']);
    const ideas = toolJson(await call(8, 'get_board', { board: 'ideas' }));
    assert.equal(ideas.board, 'ideas');
    assert.deepEqual(ideas.elements.map((element) => element.text), ['First', 'Default target', 'Explicit']);
    assert.equal(toolJson(await call(9, 'get_board', { board: 'plan' })).elementCount, 0);

    const missing = await call(10, 'add_elements', { board: 'nope', elements: [{ type: 'sticky' }] });
    assert.equal(missing.result.isError, true);
    assert.match(missing.result.content[0].text, /list_boards/);
    const bad = await call(11, 'add_elements', { board: 'ideas', elements: [{ type: 'video' }] });
    assert.equal(bad.result.isError, true);
    assert.equal((await createBoardStore(dir).read('ideas')).doc.elements.length, 3);

    const unknown = await handleMessage({ jsonrpc: '2.0', id: 12, method: 'nope' });
    assert.equal(unknown.error.code, -32601);
  } finally {
    delete process.env.CANVASROOM_BOARDS_DIR;
  }
});
