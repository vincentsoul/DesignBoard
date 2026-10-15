import test from 'node:test';
import assert from 'node:assert/strict';
import { once } from 'node:events';
import { Game } from '../game.mjs';
import { createGameServer } from '../server.mjs';
import { clientMessage, clientCommands as cmd } from '../client-bridge.mjs';

test('local APK guest, role list, and enter use authenticated HTTP messages', async t => {
  const events = [];
  const { server, game } = createGameServer({ onClientEvent: event => events.push(event) });
  server.listen(0, '127.0.0.1');
  await once(server, 'listening');
  t.after(() => { server.closeAllConnections(); server.close(); game.close(); });
  const url = `http://127.0.0.1:${server.address().port}/api/client/message`;
  let token;
  async function send(command, payload, form = false) {
    const message = JSON.stringify({ command, payload });
    const response = await fetch(url, { method: 'POST', headers: {
      'Content-Type': form ? 'application/x-www-form-urlencoded' : 'application/json', ...(token ? { Authorization: `Bearer ${token}` } : {}),
    }, body: form ? new URLSearchParams({ message }) : message });
    return { status: response.status, ...await response.json() };
  }
  assert.equal((await send(cmd.roles, {})).status, 401);
  assert.equal((await send(cmd.auth, { account_name: 'original-account', sdkuid: 'original-account' })).status, 400);
  const auth = await send(cmd.auth, { account_name: 'local-apk-guest', sdkuid: 'local-apk-guest' }, true);
  assert.equal(auth.status, 200);
  token = auth.result.token;
  assert.equal(auth.result.messages[0].command, 364);
  const roles = await send(cmd.roles, {});
  const role = roles.result.messages[0].payload.role_list[0];
  assert.equal(role.role_name, '本地测试旅人');
  assert.equal((await send(cmd.enter, { role_id: role.role_id, server_id: role.server_id })).result.messages[0].command, 360);
  assert.equal((await send(cmd.enter, { role_id: 999, server_id: 1 })).status, 400);
  assert.equal((await send(1234567, {})).status, 501);
  assert.equal(JSON.stringify(events).includes(token), false);
  assert.deepEqual(events.map(e => e.event), ['client.auth', 'client.roles', 'client.enter', 'client.unsupported']);
});

test('client bridge rejects malformed commands and expired sessions', () => {
  let now = Date.now();
  const game = new Game(':memory:', { now: () => now });
  try {
    for (const input of [null, {}, { command: 258, payload: [] }]) {
      assert.throws(() => clientMessage(game, undefined, input), /required/);
    }
    const auth = clientMessage(game, undefined, { command: cmd.auth, payload: { account_name: 'local-apk-guest', sdkuid: 'local-apk-guest' } });
    now += 24 * 60 * 60 * 1000 + 1;
    assert.throws(() => clientMessage(game, `Bearer ${auth.token}`, { command: cmd.roles, payload: {} }), /expired/);
  } finally { game.close(); }
});
