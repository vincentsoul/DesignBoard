import test from 'node:test';
import assert from 'node:assert/strict';
import { scryptSync } from 'node:crypto';
import { mkdtemp, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { createCanvasroomServer } from '../server.mjs';
import { loadAuthConfig } from '../src/auth.mjs';
const salt = '01'.repeat(32);
const password = 'test-only-password';
const authConfig = { username: 'admin', salt, passwordHash: scryptSync(password, salt, 64).toString('hex') };
async function serverTest(callback) {
  const dir = await mkdtemp(path.join(tmpdir(), 'designboard-auth-'));
  const server = createCanvasroomServer({ boardsDir: dir, authConfig });
  await new Promise((resolve) => server.listen(0, '127.0.0.1', resolve));
  const base = `http://127.0.0.1:${server.address().port}`;
  const request = (url, { method = 'GET', body, cookie, headers = {} } = {}) => fetch(base + url, { method, redirect: 'manual', headers: { ...(body ? { 'content-type': 'application/json' } : {}), ...(cookie ? { cookie } : {}), ...headers }, body: body ? JSON.stringify(body) : undefined });
  try { await callback(request, base); } finally { server.close(); server.closeAllConnections(); await rm(dir, { recursive: true, force: true }); }
}
test('page login protects board CRUD, rotates sessions, and revokes logout', async () => serverTest(async (request, base) => {
  assert.equal((await request('/')).status, 200);
  const editor = await request('/editor.html'); assert.equal(editor.status, 302); assert.equal(editor.headers.get('location'), '/');
  assert.equal((await request('/%65ditor.html')).status, 302);
  assert.equal((await request('/%')).status, 400);
  assert.deepEqual(await (await request('/api/auth/session')).json(), { enabled: true, username: null });
  for (const method of ['GET', 'POST', 'PUT', 'DELETE']) assert.equal((await request('/api/boards', { method, body: method === 'POST' || method === 'PUT' ? { name: 'Blocked' } : undefined })).status, 401);
  assert.equal((await request('/api/auth/login', { method: 'POST', body: { username: 'admin', password: 'wrong' } })).status, 401);
  assert.equal((await request('/api/auth/login', { method: 'POST', body: { username: 'admin', password }, headers: { origin: 'https://other.example' } })).status, 403);
  assert.equal((await request('/api/auth/login', { method: 'POST', body: { username: 'admin', password: 'x'.repeat(9000) } })).status, 413);
  const login = await request('/api/auth/login', { method: 'POST', body: { username: 'admin', password }, headers: { origin: base } });
  assert.equal(login.status, 200);
  const header = login.headers.get('set-cookie');
  for (const flag of ['HttpOnly', 'SameSite=Strict', 'Secure', 'Path=/']) assert.ok(header.includes(flag), flag);
  assert.ok(!header.includes('Max-Age'));
  const cookie = header.split(';')[0];
  assert.equal((await request('/editor.html', { cookie })).status, 200);
  assert.equal((await request('/api/auth/session', { cookie })).headers.get('cache-control'), 'no-store');
  assert.equal((await (await request('/api/auth/session', { cookie })).json()).username, 'admin');
  const create = await request('/api/boards', { method: 'POST', cookie, body: { name: 'Private' } }); assert.equal(create.status, 201);
  const id = (await create.json()).id;
  assert.equal((await request('/api/boards/' + id)).status, 401);
  const current = await (await request('/api/boards/' + id, { cookie })).json();
  current.doc.name = 'Saved';
  assert.equal((await request('/api/boards/' + id, { method: 'PUT', cookie, body: { baseRevision: current.revision, doc: current.doc } })).status, 200);
  assert.equal((await request('/api/boards/' + id, { cookie: 'canvasroom_session=' + 'a'.repeat(43) })).status, 401);
  assert.equal((await request('/api/auth/logout', { method: 'POST', cookie, body: {}, headers: { origin: 'https://other.example' } })).status, 403);
  const relogin = await request('/api/auth/login', { method: 'POST', cookie, body: { username: 'admin', password } });
  const nextCookie = relogin.headers.get('set-cookie').split(';')[0]; assert.notEqual(nextCookie, cookie);
  assert.equal((await request('/api/boards', { cookie })).status, 401);
  assert.equal((await request('/api/boards/' + id, { method: 'DELETE', cookie: nextCookie })).status, 200);
  const logout = await request('/api/auth/logout', { method: 'POST', cookie: nextCookie, body: {} });
  assert.equal(logout.status, 200); assert.ok(logout.headers.get('set-cookie').includes('Max-Age=0'));
  assert.equal((await request('/api/boards', { cookie: nextCookie })).status, 401);
}));
test('limits failed logins including a correct password after the limit', async () => serverTest(async (request) => {
  for (let i = 0; i < 5; i++) assert.equal((await request('/api/auth/login', { method: 'POST', body: { username: 'admin', password: 'wrong' } })).status, 401);
  const limited = await request('/api/auth/login', { method: 'POST', body: { username: 'admin', password } });
  assert.equal(limited.status, 429); assert.ok(Number(limited.headers.get('retry-after')) > 0);
}));
test('required authentication fails closed when the configuration is missing', () => {
  const oldRequired = process.env.CANVASROOM_REQUIRE_AUTH; const oldFile = process.env.CANVASROOM_AUTH_FILE;
  try { process.env.CANVASROOM_REQUIRE_AUTH = '1'; delete process.env.CANVASROOM_AUTH_FILE; assert.throws(loadAuthConfig, /required/); }
  finally { if (oldRequired === undefined) delete process.env.CANVASROOM_REQUIRE_AUTH; else process.env.CANVASROOM_REQUIRE_AUTH = oldRequired; if (oldFile === undefined) delete process.env.CANVASROOM_AUTH_FILE; else process.env.CANVASROOM_AUTH_FILE = oldFile; }
});
