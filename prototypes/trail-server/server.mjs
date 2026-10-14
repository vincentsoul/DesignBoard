import { createServer } from 'node:http';
import { mkdirSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { Game, GameError } from './game.mjs';
import { catalog } from './catalog.mjs';

function body(request) {
  return new Promise((resolveBody, reject) => {
    let chunks = [], bytes = 0, done = false;
    const fail = error => { if (!done) { done = true; chunks = []; reject(error); } };
    request.on('data', chunk => {
      if (done) return;
      bytes += chunk.length;
      if (bytes > 65536) return fail(new GameError(413, 'body_too_large', 'JSON body exceeds 64 KiB'));
      chunks.push(chunk);
    });
    request.on('end', () => {
      if (done) return;
      done = true;
      try {
        const value = chunks.length ? JSON.parse(Buffer.concat(chunks).toString('utf8')) : {};
        if (!value || typeof value !== 'object' || Array.isArray(value)) throw new Error('JSON object required');
        resolveBody(value);
      } catch { reject(new GameError(400, 'invalid_json', 'A JSON object is required')); }
    });
    request.on('aborted', () => fail(new GameError(400, 'request_aborted', 'Request aborted')));
    request.on('error', fail);
  });
}
function send(response, status, data) {
  const value = JSON.stringify(data);
  response.writeHead(status, { 'Content-Type': 'application/json; charset=utf-8', 'Cache-Control': 'no-store', 'X-Content-Type-Options': 'nosniff', 'Content-Length': Buffer.byteLength(value) });
  response.end(value);
}
export function createGameServer({ game = new Game(), onError = error => console.error(error.message) } = {}) {
  const server = createServer(async (request, response) => {
    try {
      const url = new URL(request.url, 'http://localhost');
      const path = url.pathname;
      let result;
      if (request.method === 'GET' && path === '/health') result = { status: 'ok', mode: 'local-prototype', serverTime: game.now() };
      else if (request.method === 'GET' && path === '/api/catalog') result = catalog;
      else if (request.method === 'GET' && path === '/api/capabilities') result = game.capabilities();
      else if (request.method === 'POST' && path === '/api/session') {
        result = game.createSession(await body(request));
        return send(response, 201, { ok: true, result });
      } else {
        const playerId = game.authenticate(request.headers.authorization);
        if (request.method === 'GET' && path === '/api/player') result = game.snapshot(playerId);
        else if (request.method === 'GET' && path === '/api/trades') result = game.listTrades();
        else if (request.method === 'GET' && path === '/api/towns') result = game.listTowns(url.searchParams.get('search') ?? '');
        else if (request.method === 'GET' && path === '/api/events') {
          const value = url.searchParams.get('after') ?? '0';
          if (!/^\d+$/.test(value)) throw new GameError(400, 'invalid_input', 'after must be a nonnegative integer');
          result = game.eventsSince(playerId, Number(value));
        } else if (request.method === 'GET' && /^\/api\/towns\/[^/]+(?:\/ranking)?$/.test(path)) {
          const id = decodeURIComponent(path.split('/')[3]);
          result = path.endsWith('/ranking') ? game.ranking(id) : game.townView(id);
        } else if (request.method === 'POST' && path === '/api/heartbeat') result = { serverTime: game.now(), playerRevision: game.readPlayer(playerId).revision };
        else if (request.method === 'POST' && path === '/api/commands') result = game.execute(playerId, request.headers['idempotency-key'], await body(request));
        else throw new GameError(404, 'not_found', 'Endpoint not found');
      }
      send(response, 200, { ok: true, result });
    } catch (error) {
      if (!(error instanceof GameError)) onError(error);
      send(response, error instanceof GameError ? error.status : 500, {
        ok: false, error: { code: error instanceof GameError ? error.code : 'internal_error', message: error instanceof GameError ? error.message : 'Internal server error', ...(error.details ? { details: error.details } : {}) },
      });
    }
  });
  server.requestTimeout = 30000;
  server.headersTimeout = 15000;
  return { server, game };
}

if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  const host = process.env.LOCAL_GAME_HOST ?? '127.0.0.1';
  if (!['127.0.0.1', '::1', 'localhost'].includes(host)) throw new Error('This prototype binds to loopback only');
  const port = Number(process.env.LOCAL_GAME_PORT ?? 4317);
  if (!Number.isInteger(port) || port < 0 || port > 65535) throw new Error('LOCAL_GAME_PORT must be 0–65535');
  const root = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
  const dbPath = resolve(process.env.LOCAL_GAME_DB ?? resolve(root, 'outputs/trail-server/game.sqlite'));
  mkdirSync(dirname(dbPath), { recursive: true });
  const { server, game } = createGameServer({ game: new Game(dbPath) });
  server.on('error', error => { console.error(error.message); game.close(); process.exitCode = 1; });
  server.listen(port, host, () => console.log(`Local game prototype: http://${host.includes(':') ? `[${host}]` : host}:${server.address().port}`));
  const stop = () => server.close(() => { game.close(); process.exit(0); });
  process.once('SIGINT', stop);
  process.once('SIGTERM', stop);
}
