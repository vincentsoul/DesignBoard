import test from 'node:test';
import assert from 'node:assert/strict';
import { randomUUID } from 'node:crypto';
import { mkdtempSync, rmSync } from 'node:fs';
import { join } from 'node:path';
import { tmpdir } from 'node:os';
import { Game } from '../game.mjs';
import { createGameServer } from '../server.mjs';
import { runDemo } from '../demo.mjs';

async function fixture(t, options = {}) {
  const { server, game } = createGameServer({ game: new Game(':memory:', options), onError: error => { throw error; } });
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  t.after(async () => { await new Promise(resolve => server.close(resolve)); game.close(); });
  const base = `http://127.0.0.1:${server.address().port}`;
  const request = async (path, session, input, key, extra = {}) => {
    const response = await fetch(`${base}${path}`, {
      method: input === undefined ? 'GET' : 'POST',
      headers: { 'Content-Type': 'application/json', ...(session ? { Authorization: `Bearer ${session.token}` } : {}), ...(key ? { 'Idempotency-Key': key } : {}) },
      ...(input === undefined ? {} : { body: JSON.stringify(input) }), ...extra,
    });
    return { status: response.status, ...await response.json() };
  };
  const session = async name => (await request('/api/session', null, { name })).result;
  const command = async (session, operation, args = {}, key = randomUUID(), revision = session.player.revision) => {
    const result = await request('/api/commands', session, { operation, args, expectedRevision: revision }, key);
    if (result.ok) session.player = result.result.player;
    return result;
  };
  return { game, base, request, session, command };
}

test('full HTTP demo exercises craft, storage, trade, town, reward and checkpoint', async t => {
  const { base } = await fixture(t);
  const result = await runDemo(base);
  assert.equal(result.weeklyRewardReplayedWithoutDuplication, true);
  assert.equal(result.alice.chits, 133);
  assert.equal(result.bob.chits, 76);
  assert.equal(result.town.funds, 10);
  assert.equal(result.bob.checkpoint.campId, 'camp-2');
});

test('auth, invalid JSON, body limits and client-supplied balance changes are rejected', async t => {
  const { request, session, command } = await fixture(t);
  assert.equal((await request('/api/player')).status, 401);
  assert.equal((await request('/api/session', null, {}, null, { body: '{oops' })).status, 400);
  assert.equal((await request('/api/session', null, { name: 'x'.repeat(70000) })).status, 413);
  const alice = await session('Alice');
  assert.equal((await command(alice, 'player.rename', { name: 'Other', chits: 999999 })).status, 400);
  assert.equal((await request('/api/player', alice)).result.chits, 100);
  assert.equal((await command(alice, 'inventory.drop', { itemId: 'wood', quantity: -1 })).status, 400);
  assert.equal((await command(alice, 'inventory.drop', { itemId: '__proto__', quantity: 1 })).status, 400);
});

test('idempotent replay survives a stale revision and key reuse cannot change the payload', async t => {
  const { session, request, command } = await fixture(t);
  const alice = await session('Alice');
  const payload = { operation: 'shop.buy', expectedRevision: 0, args: { itemId: 'wood', quantity: 2 } };
  const first = await request('/api/commands', alice, payload, 'purchase-1');
  const replay = await request('/api/commands', alice, { args: payload.args, expectedRevision: 0, operation: 'shop.buy' }, 'purchase-1');
  assert.equal(first.result.player.chits, 96);
  assert.equal(replay.result.replayed, true);
  assert.equal((await request('/api/player', alice)).result.containers.backpack.wood, 12);
  assert.equal((await request('/api/commands', alice, { ...payload, args: { itemId: 'wood', quantity: 3 } }, 'purchase-1')).error.code, 'idempotency_conflict');
  assert.equal((await command(alice, 'player.rename', { name: 'Stale' })).error.code, 'revision_conflict');
});

test('failed crafting and purchases roll back deductions and revisions', async t => {
  const { game, session, command, request } = await fixture(t);
  const alice = await session('Alice');
  const setup = game.readPlayer(alice.player.id);
  setup.containers.backpack.board = 9999;
  game.savePlayer(setup);
  alice.player = setup;
  const failed = await command(alice, 'craft.make', { recipeId: 'board', quantity: 1 });
  assert.equal(failed.error.code, 'stack_full');
  const current = (await request('/api/player', alice)).result;
  assert.equal(current.containers.backpack.wood, 10);
  assert.equal(current.revision, 1);
  assert.equal((await command(alice, 'shop.buy', { itemId: 'board', quantity: 1 })).error.code, 'stack_full');
  assert.equal((await request('/api/player', alice)).result.chits, 100);
});

test('two buyers race for one escrow listing; balances and item counts remain conserved', async t => {
  const { game, session, command } = await fixture(t);
  const seller = await session('Seller'), buyer1 = await session('Buyer1'), buyer2 = await session('Buyer2');
  const { listing } = (await command(seller, 'trade.list', { itemId: 'wood', quantity: 3, unitPrice: 4 })).result;
  const results = await Promise.all([command(buyer1, 'trade.buy', { listingId: listing.id }), command(buyer2, 'trade.buy', { listingId: listing.id })]);
  assert.deepEqual(results.map(r => r.status).sort(), [200, 409]);
  const players = [seller, buyer1, buyer2].map(s => game.snapshot(s.player.id));
  assert.equal(players.reduce((sum, p) => sum + p.chits, 0), 300);
  assert.equal(players.reduce((sum, p) => sum + (p.containers.backpack.wood ?? 0), 0), 30);
  assert.equal(players[0].chits, 112);
  assert.equal(game.listing(listing.id).status, 'sold');
});

test('seller cancellation is authorized and returns escrow exactly once', async t => {
  const { session, command, request } = await fixture(t);
  const alice = await session('Alice'), bob = await session('Bob');
  const { listing } = (await command(alice, 'trade.list', { itemId: 'wood', quantity: 4, unitPrice: 2 })).result;
  assert.equal((await command(bob, 'trade.cancel', { listingId: listing.id })).status, 403);
  const oldRevision = alice.player.revision;
  const cancelled = await command(alice, 'trade.cancel', { listingId: listing.id }, 'cancel', oldRevision);
  assert.equal(cancelled.result.player.containers.backpack.wood, 10);
  const replay = await command(alice, 'trade.cancel', { listingId: listing.id }, 'cancel', oldRevision);
  assert.equal(replay.result.replayed, true);
  assert.equal((await request('/api/player', alice)).result.containers.backpack.wood, 10);
});

test('failed buyer inventory credit leaves seller, listing and buyer untouched', async t => {
  const { game, session, command } = await fixture(t);
  const seller = await session('Seller'), buyer = await session('Buyer');
  const { listing } = (await command(seller, 'trade.list', { itemId: 'wood', quantity: 2, unitPrice: 5 })).result;
  const setup = game.readPlayer(buyer.player.id);
  setup.containers.backpack.wood = 9999;
  game.savePlayer(setup);
  buyer.player = setup;
  assert.equal((await command(buyer, 'trade.buy', { listingId: listing.id })).error.code, 'stack_full');
  assert.equal(game.readPlayer(buyer.player.id).chits, 100);
  assert.equal(game.readPlayer(seller.player.id).chits, 100);
  assert.equal(game.listing(listing.id).status, 'open');
});

test('town permissions, optimistic updates, rankings and dissolution maintain membership', async t => {
  const { game, session, command, request } = await fixture(t);
  const mayor = await session('Mayor'), member = await session('Member');
  const { town } = (await command(mayor, 'town.create', { name: 'Town' })).result;
  await command(member, 'town.join', { townId: town.id });
  assert.equal((await command(member, 'town.update', { townId: town.id, expectedTownRevision: 1, name: 'Hijack' })).status, 403);
  assert.equal((await command(mayor, 'town.update', { townId: town.id, expectedTownRevision: 0, name: 'Stale' })).error.code, 'town_revision_conflict');
  await command(member, 'town.contribute', { amount: 20 });
  const ranking = (await request(`/api/towns/${town.id}/ranking`, mayor)).result;
  assert.equal(ranking[0].playerId, member.player.id);
  assert.equal(game.townView(town.id).funds, 20);
  assert.equal((await command(mayor, 'town.leave')).error.code, 'mayor_must_dissolve');
  await command(mayor, 'town.dissolve');
  assert.equal(game.snapshot(member.player.id).town, null);
});

test('town capacity rejects overflow without partially joining', async t => {
  const { game, session, command } = await fixture(t);
  const mayor = await session('Mayor');
  const { town } = (await command(mayor, 'town.create', { name: 'FullTown' })).result;
  for (let index = 0; index < 7; index++) await command(await session(`Member${index}`), 'town.join', { townId: town.id });
  const extra = await session('Extra');
  assert.equal((await command(extra, 'town.join', { townId: town.id })).error.code, 'town_full');
  assert.equal(game.snapshot(extra.player.id).town, null);
  assert.equal(game.snapshot(extra.player.id).revision, 0);
});

test('weekly claims use Shanghai Monday boundaries and cannot be repeated with a new key', async t => {
  let now = Date.parse('2026-10-18T15:59:59Z');
  const { session, command } = await fixture(t, { now: () => now });
  const alice = await session('Alice');
  const first = await command(alice, 'reward.weekly');
  assert.equal(first.result.period, '2026-10-12');
  assert.equal((await command(alice, 'reward.weekly')).error.code, 'already_claimed');
  now += 2000;
  assert.equal((await command(alice, 'reward.weekly')).result.period, '2026-10-19');
  assert.equal(alice.player.chits, 150);
});

test('sessions expire and event cursors expose only the authenticated player events', async t => {
  let now = Date.parse('2026-10-14T08:00:00Z');
  const { game, session, command, request } = await fixture(t, { now: () => now });
  const alice = await session('Alice'), bob = await session('Bob');
  await command(alice, 'player.rename', { name: 'NewAlice' });
  const a = (await request('/api/events', alice)).result;
  const b = (await request('/api/events', bob)).result;
  assert.equal(a.events.length, 2);
  assert.equal(b.events.length, 1);
  assert.equal((await request(`/api/events?after=${a.nextCursor}`, alice)).result.events.length, 0);
  now += 24 * 60 * 60 * 1000;
  assert.equal((await request('/api/player', alice)).status, 401);
  assert.equal(game.readPlayer(alice.player.id).name, 'NewAlice');
});

test('SQLite restart preserves sessions, state, reward claims and idempotency receipts', () => {
  const directory = mkdtempSync(join(tmpdir(), 'trail-prototype-test-'));
  const path = join(directory, 'game.sqlite');
  let game;
  try {
    const now = () => Date.parse('2026-10-14T08:00:00Z');
    game = new Game(path, { now });
    const session = game.createSession({ name: 'Persistent' });
    const command = { operation: 'reward.weekly', expectedRevision: 0, args: {} };
    game.execute(session.player.id, 'weekly', command);
    game.close();
    game = new Game(path, { now });
    const id = game.authenticate(`Bearer ${session.token}`);
    assert.equal(game.snapshot(id).chits, 125);
    assert.equal(game.execute(id, 'weekly', command).replayed, true);
    assert.throws(() => game.execute(id, 'another-weekly', { ...command, expectedRevision: 1 }), { code: 'already_claimed' });
    assert.equal(game.snapshot(id).chits, 125);
  } finally { game?.close(); rmSync(directory, { recursive: true, force: true }); }
});
