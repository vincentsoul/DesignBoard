import test from 'node:test';
import assert from 'node:assert/strict';
import { createFileSync } from '../src/sync.js';

async function scenario(run) {
  const original = globalThis.fetch;
  let puts = 0;
  let writeStatus = 200;
  let holdWrite;
  let timeout = false;
  const statuses = [];
  globalThis.fetch = async (url, options) => {
    if (options.method === 'PUT') {
      puts++;
      assert.ok(options.signal instanceof AbortSignal);
      if (timeout) throw Object.assign(new Error('timeout'), { name: 'TimeoutError' });
      if (holdWrite) await holdWrite;
      return Response.json(writeStatus === 200 ? { revision: `r${puts}` } : { error: 'Board is too large' }, { status: writeStatus });
    }
    return Response.json(url === '/api/boards' ? { boards: [] } : { doc: { name: 'Image-heavy board', elements: [] }, revision: 'r0' });
  };
  try {
    const sync = createFileSync({ getDoc: () => ({ name: 'Current edits', elements: [] }), isBusy: () => false, onRemoteChange() {}, onMissing() {}, onStatus: (...args) => statuses.push(args) });
    await sync.probe(); await sync.open('large-board');
    await run({ sync, statuses, puts: () => puts, rejectWrites: () => { writeStatus = 413; }, acceptWrites: () => { writeStatus = 200; }, hold: (promise) => { holdWrite = promise; }, timeout: () => { timeout = true; } });
  } finally { globalThis.fetch = original; }
}

test('leaving an already-synced image-heavy board never uploads it again', async () => scenario(async ({ sync, puts, rejectWrites }) => {
  rejectWrites();
  assert.equal(await sync.saveBeforeLeave(false), true);
  assert.equal(puts(), 0);
}));
test('pending edits are saved, and failed autosaves block navigation until retried successfully', async () => scenario(async ({ sync, puts, rejectWrites, acceptWrites }) => {
  rejectWrites();
  await sync.push();
  assert.equal(await sync.saveBeforeLeave(false), false);
  acceptWrites();
  assert.equal(await sync.saveBeforeLeave(false), true);
  assert.equal(puts(), 3);
  assert.equal(await sync.saveBeforeLeave(false), true);
  assert.equal(puts(), 3);
  assert.equal(await sync.saveBeforeLeave(true), true);
  assert.equal(puts(), 4);
}));
test('navigation waits for an active autosave without issuing a duplicate upload', async () => scenario(async ({ sync, puts, hold }) => {
  let finish; hold(new Promise(resolve => { finish = resolve; }));
  const saving = sync.push();
  let left = false;
  const leaving = sync.saveBeforeLeave(false).then(result => { left = result; });
  await Promise.resolve(); assert.equal(left, false);
  finish(); await saving; await leaving;
  assert.equal(left, true); assert.equal(puts(), 1);
}));
test('write timeouts keep unsaved changes and report an actionable error', async () => scenario(async ({ sync, statuses, timeout }) => {
  timeout();
  assert.equal(await sync.saveBeforeLeave(true), false);
  assert.equal(statuses.at(-1)[0], 'error');
  assert.match(statuses.at(-1)[1], /保存超时/);
}));
