import http from 'node:http';
import { readFile, stat } from 'node:fs/promises';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { createDocument, parseDocument } from './src/model.js';
import { boardsDirPath, createBoardStore, projectRoot } from './src/board-store.mjs';
import { createAuth, loadAuthConfig } from './src/auth.mjs';

const MIME = {
  '.html': 'text/html; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.mjs': 'text/javascript; charset=utf-8',
  '.json': 'application/json; charset=utf-8',
  '.svg': 'image/svg+xml',
  '.png': 'image/png',
  '.ico': 'image/x-icon',
};
const MAX_BODY = 30 * 1024 * 1024;
const PUBLIC_FILES = new Set(['index.html', 'editor.html', 'portal.css', 'styles.css', 'src/portal.js', 'src/model.js', 'src/clipboard.js', 'src/sync.js', 'src/board.js', 'src/board-ops.mjs', 'src/icons.js', 'src/io.js', 'src/viewport.js']);

function sendJson(res, status, body) {
  res.writeHead(status, { 'content-type': MIME['.json'], 'cache-control': 'no-store' });
  res.end(JSON.stringify(body));
}

async function readJsonBody(req, limit = MAX_BODY) {
  const chunks = [];
  let size = 0;
  for await (const chunk of req) {
    size += chunk.length;
    if (size > limit) throw Object.assign(new Error('Request body is too large'), { status: 413 });
    chunks.push(chunk);
  }
  try {
    return JSON.parse(Buffer.concat(chunks).toString('utf8'));
  } catch {
    throw Object.assign(new Error('Request body must be JSON'), { status: 400 });
  }
}

function validDoc(value) {
  try {
    return parseDocument(JSON.stringify(value));
  } catch (error) {
    throw Object.assign(new Error(error.message), { status: 400 });
  }
}

async function handleCollection(req, res, store) {
  if (req.method === 'GET') return sendJson(res, 200, { boards: await store.list() });
  if (req.method !== 'POST') return sendJson(res, 405, { error: 'Method not allowed' });
  const body = await readJsonBody(req);
  let doc;
  if (body.doc) doc = validDoc(body.doc);
  else if (body.sourceId) {
    const source = (await store.read(body.sourceId)).doc;
    if (!source) return sendJson(res, 404, { error: `Board not found: ${body.sourceId}` });
    doc = { ...source, name: body.name?.trim() || `${source.name} 副本` };
  } else doc = createDocument(body.name?.trim() || '未命名画布');
  const { id, revision } = await store.create(doc);
  return sendJson(res, 201, { id, name: doc.name, revision });
}

async function handleBoard(req, res, store, id) {
  if (req.method === 'DELETE') {
    await store.remove(id);
    return sendJson(res, 200, { deleted: id });
  }
  const current = await store.read(id);
  if (req.method === 'GET') return current.doc ? sendJson(res, 200, current) : sendJson(res, 404, { error: `Board not found: ${id}` });
  if (req.method !== 'PUT') return sendJson(res, 405, { error: 'Method not allowed' });
  const body = await readJsonBody(req);
  // Reject writes based on an older file so agent edits are never silently overwritten.
  if ((body.baseRevision ?? null) !== current.revision) return sendJson(res, 409, current);
  return sendJson(res, 200, { revision: await store.write(id, validDoc(body.doc)) });
}

async function serveStatic(req, res, root) {
  let pathname;
  try {
    pathname = decodeURIComponent(new URL(req.url, 'http://localhost').pathname);
  } catch {
    res.writeHead(400).end();
    return;
  }
  if (pathname === '/') pathname = '/index.html';
  if (!PUBLIC_FILES.has(pathname.slice(1))) {
    res.writeHead(404, { 'content-type': 'text/plain; charset=utf-8' }).end('Not found');
    return;
  }
  const file = path.resolve(root, `.${pathname}`);
  const inside = file.startsWith(root + path.sep);
  const info = inside ? await stat(file).catch(() => null) : null;
  if (!info?.isFile()) {
    res.writeHead(404, { 'content-type': 'text/plain; charset=utf-8' }).end('Not found');
    return;
  }
  res.writeHead(200, { 'content-type': MIME[path.extname(file)] || 'application/octet-stream', 'cache-control': 'no-store' });
  res.end(await readFile(file));
}

export function createCanvasroomServer({ root = projectRoot, boardsDir, authConfig = loadAuthConfig(), secureCookie = process.env.CANVASROOM_SECURE_COOKIE !== '0', trustProxy = process.env.CANVASROOM_TRUST_PROXY === '1' } = {}) {
  const resolvedRoot = path.resolve(root);
  const store = createBoardStore(boardsDir ?? boardsDirPath());
  const auth = createAuth(authConfig, { secure: secureCookie, trustProxy });
  return http.createServer(async (req, res) => {
    try {
      let pathname;
      try { pathname = decodeURIComponent(new URL(req.url, 'http://localhost').pathname); }
      catch { return sendJson(res, 400, { error: 'Invalid request path' }); }
      if (pathname.startsWith('/api/') && !['GET', 'HEAD', 'OPTIONS'].includes(req.method)) {
        // Browser credentials are sent automatically; reject cross-site writes.
        const origin = req.headers.origin;
        let sameOrigin = !origin;
        if (origin) {
          try { sameOrigin = new URL(origin).host === req.headers.host; } catch { sameOrigin = false; }
        }
        if (req.headers['sec-fetch-site'] === 'cross-site' || !sameOrigin) {
          return sendJson(res, 403, { error: 'Cross-origin writes are not allowed' });
        }
        if (['POST', 'PUT'].includes(req.method) && req.headers['content-type']?.split(';')[0].trim().toLowerCase() !== 'application/json') {
          return sendJson(res, 415, { error: 'Content-Type must be application/json' });
        }
      }
      if (pathname === '/api/health' && req.method === 'GET') return sendJson(res, 200, { status: 'ok' });
      if (pathname === '/api/auth/session' && req.method === 'GET') return sendJson(res, 200, { enabled: Boolean(auth), username: auth?.user(req) || null });
      if (pathname === '/api/auth/login' && req.method === 'POST') {
        if (!auth) return sendJson(res, 200, { enabled: false });
        const { status, ...result } = await auth.login(req, res, await readJsonBody(req, 8192));
        return sendJson(res, status, result);
      }
      if (pathname === '/api/auth/logout' && req.method === 'POST') {
        auth?.logout(req, res);
        return sendJson(res, 200, { loggedOut: true });
      }
      if (auth && !auth.user(req)) {
        if (pathname === '/editor.html') { res.writeHead(302, { location: '/', 'cache-control': 'no-store' }).end(); return; }
        if (pathname.startsWith('/api/')) return sendJson(res, 401, { error: '请先登录' });
      }
      if (pathname === '/api/boards') return await handleCollection(req, res, store);
      if (pathname.startsWith('/api/boards/')) return await handleBoard(req, res, store, decodeURIComponent(pathname.slice('/api/boards/'.length)));
      await serveStatic(req, res, resolvedRoot);
    } catch (error) {
      if (!res.headersSent) sendJson(res, error.status || 500, { error: error.message });
      else res.end();
    }
  });
}

if (process.argv[1] && pathToFileURL(path.resolve(process.argv[1])).href === import.meta.url) {
  const port = Number(process.env.PORT || 4173);
  const host = process.env.HOST || '127.0.0.1';
  const server = createCanvasroomServer();
  server.listen(port, host, () => {
    console.log(`Canvasroom: http://${host}:${port}`);
    console.log(`Boards folder: ${boardsDirPath()}`);
  });
  for (const signal of ['SIGTERM', 'SIGINT']) {
    process.once(signal, () => {
      server.close(() => process.exit(0));
      server.closeIdleConnections();
      setTimeout(() => process.exit(1), 10000).unref();
    });
  }
}
