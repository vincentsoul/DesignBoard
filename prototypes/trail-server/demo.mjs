import assert from 'node:assert/strict';
import { randomUUID } from 'node:crypto';

export async function runDemo(baseUrl = 'http://127.0.0.1:4317') {
  const base = new URL(baseUrl);
  if (!['127.0.0.1', 'localhost', '[::1]'].includes(base.hostname) || base.protocol !== 'http:') throw new Error('Use a local HTTP prototype URL');
  const request = async (path, { token, input, key, method = input === undefined ? 'GET' : 'POST' } = {}) => {
    const response = await fetch(new URL(path, base), {
      method, headers: { 'Content-Type': 'application/json', ...(token ? { Authorization: `Bearer ${token}` } : {}), ...(key ? { 'Idempotency-Key': key } : {}) },
      ...(input === undefined ? {} : { body: JSON.stringify(input) }),
    });
    const value = await response.json();
    if (!response.ok || !value.ok) throw new Error(`${response.status}: ${JSON.stringify(value.error)}`);
    return value.result;
  };
  const health = await request('/health');
  const alice = await request('/api/session', { input: { name: 'Alice' } });
  const bob = await request('/api/session', { input: { name: 'Bob' } });
  const command = async (session, operation, args = {}) => {
    const result = await request('/api/commands', { token: session.token, key: randomUUID(), input: { operation, expectedRevision: session.player.revision, args } });
    session.player = result.player;
    return result;
  };
  await command(alice, 'craft.make', { recipeId: 'rope', quantity: 2 });
  await command(alice, 'inventory.move', { itemId: 'wood', quantity: 4, from: 'backpack', to: 'storage' });
  await command(alice, 'shop.buy', { itemId: 'berry', quantity: 2 });
  const { town } = await command(alice, 'town.create', { name: '本地测试镇', rules: '共同建设' });
  await command(bob, 'town.join', { townId: town.id });
  await command(bob, 'town.contribute', { amount: 10 });
  const { listing } = await command(alice, 'trade.list', { itemId: 'rope', quantity: 2, unitPrice: 7 });
  await command(bob, 'trade.buy', { listingId: listing.id });
  // Selling changes the seller's revision, so reload before the next mutation.
  alice.player = await request('/api/player', { token: alice.token });
  const weeklyInput = { operation: 'reward.weekly', expectedRevision: alice.player.revision, args: {} };
  const weeklyKey = randomUUID();
  const reward = await request('/api/commands', { token: alice.token, key: weeklyKey, input: weeklyInput });
  const replay = await request('/api/commands', { token: alice.token, key: weeklyKey, input: weeklyInput });
  assert.equal(replay.replayed, true);
  assert.equal(replay.player.chits, reward.player.chits);
  alice.player = reward.player;
  const currentTown = await request(`/api/towns/${town.id}`, { token: alice.token });
  await command(alice, 'town.update', { townId: town.id, expectedTownRevision: currentTown.revision, announcement: '本地原型业务流程验证完成' });
  await command(bob, 'inventory.use', { itemId: 'berry', quantity: 1 });
  await command(bob, 'player.checkpoint', { campId: 'camp-2', position: { x: 12, y: 6 } });
  const ranking = await request(`/api/towns/${town.id}/ranking`, { token: alice.token });
  assert.equal(bob.player.containers.backpack.rope, 2);
  assert.equal(alice.player.containers.storage.wood, 4);
  assert.equal(ranking[0].playerId, bob.player.id);
  return { health, alice: alice.player, bob: bob.player, town: await request(`/api/towns/${town.id}`, { token: alice.token }), weeklyRewardReplayedWithoutDuplication: replay.replayed, ranking };
}

if (process.argv[1]?.endsWith('/demo.mjs') || process.argv[1] === 'demo.mjs') {
  runDemo(process.argv[2]).then(result => console.log(JSON.stringify(result, null, 2))).catch(error => { console.error(error.message); process.exitCode = 1; });
}
