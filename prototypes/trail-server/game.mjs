import { DatabaseSync } from 'node:sqlite';
import { randomBytes, randomUUID, createHash } from 'node:crypto';
import { catalog, capabilities } from './catalog.mjs';

export class GameError extends Error {
  constructor(status, code, message, details) {
    super(message);
    Object.assign(this, { status, code, details });
  }
}
const reject = (status, code, message, details) => { throw new GameError(status, code, message, details); };
const hash = value => createHash('sha256').update(value).digest('hex');
const integer = (value, name, min = 1, max = 1000) => {
  if (!Number.isSafeInteger(value) || value < min || value > max) reject(400, 'invalid_input', `${name} must be an integer from ${min} to ${max}`);
  return value;
};
const text = (value, name, max = 64, empty = false) => {
  if (typeof value !== 'string' || value.length > max || /[\u0000-\u001f\u007f]/u.test(value) || (!empty && !value.trim())) reject(400, 'invalid_input', `${name} is invalid`);
  return value.trim();
};
const shape = (value, keys) => {
  if (!value || typeof value !== 'object' || Array.isArray(value) || Object.keys(value).some(key => !keys.includes(key))) reject(400, 'invalid_input', 'Unknown or invalid fields');
};
const item = id => {
  if (typeof id !== 'string' || !Object.hasOwn(catalog.items, id)) reject(400, 'unknown_item', 'Unknown item');
  return catalog.items[id];
};
const container = (player, name) => {
  if (!['backpack', 'storage'].includes(name)) reject(400, 'unknown_container', 'Unknown inventory container');
  return player.containers[name];
};
const addItems = (bag, id, quantity) => {
  item(id);
  const next = (bag[id] ?? 0) + quantity;
  if (next > catalog.maxStack) reject(409, 'stack_full', 'Inventory stack limit reached');
  bag[id] = next;
};
const removeItems = (bag, id, quantity) => {
  item(id);
  if ((bag[id] ?? 0) < quantity) reject(409, 'insufficient_items', 'Not enough items');
  bag[id] -= quantity;
  if (!bag[id]) delete bag[id];
};
const spend = (player, amount) => {
  if (player.chits < amount) reject(409, 'insufficient_chits', 'Not enough chits');
  player.chits -= amount;
};
const credit = (player, amount) => {
  if (player.chits + amount > catalog.maxChits) reject(409, 'balance_limit', 'Balance limit reached');
  player.chits += amount;
};
const canonical = value => {
  if (Array.isArray(value)) return value.map(canonical);
  if (value && typeof value === 'object') return Object.fromEntries(Object.keys(value).sort().map(key => [key, canonical(value[key])]));
  return value;
};

export const commands = [
  'player.rename', 'player.checkpoint', 'inventory.move', 'inventory.drop',
  'inventory.use', 'craft.make', 'shop.buy', 'reward.weekly', 'trade.list',
  'trade.buy', 'trade.cancel', 'town.create', 'town.join', 'town.update',
  'town.contribute', 'town.leave', 'town.dissolve',
];

export class Game {
  constructor(dbPath = ':memory:', { now = Date.now } = {}) {
    this.now = now;
    this.db = new DatabaseSync(dbPath);
    this.db.exec(`
      PRAGMA foreign_keys = ON;
      PRAGMA journal_mode = WAL;
      PRAGMA busy_timeout = 5000;
      CREATE TABLE IF NOT EXISTS players(id TEXT PRIMARY KEY, data TEXT NOT NULL, revision INTEGER NOT NULL) STRICT;
      CREATE TABLE IF NOT EXISTS sessions(token_hash TEXT PRIMARY KEY, player_id TEXT NOT NULL REFERENCES players(id), expires_at INTEGER NOT NULL) STRICT;
      CREATE TABLE IF NOT EXISTS towns(id TEXT PRIMARY KEY, data TEXT NOT NULL, revision INTEGER NOT NULL) STRICT;
      CREATE TABLE IF NOT EXISTS members(player_id TEXT PRIMARY KEY REFERENCES players(id), town_id TEXT NOT NULL REFERENCES towns(id) ON DELETE CASCADE, plot INTEGER NOT NULL, role TEXT NOT NULL CHECK(role IN ('mayor','member')), contribution INTEGER NOT NULL DEFAULT 0 CHECK(contribution >= 0), UNIQUE(town_id, plot)) STRICT;
      CREATE TABLE IF NOT EXISTS listings(id TEXT PRIMARY KEY, seller_id TEXT NOT NULL REFERENCES players(id), item_id TEXT NOT NULL, quantity INTEGER NOT NULL CHECK(quantity > 0), unit_price INTEGER NOT NULL CHECK(unit_price > 0), status TEXT NOT NULL CHECK(status IN ('open','sold','cancelled')), buyer_id TEXT REFERENCES players(id), created_at INTEGER NOT NULL) STRICT;
      CREATE TABLE IF NOT EXISTS claims(player_id TEXT NOT NULL REFERENCES players(id), period TEXT NOT NULL, PRIMARY KEY(player_id, period)) STRICT;
      CREATE TABLE IF NOT EXISTS receipts(player_id TEXT NOT NULL REFERENCES players(id), request_key TEXT NOT NULL, fingerprint TEXT NOT NULL, result TEXT NOT NULL, PRIMARY KEY(player_id, request_key)) STRICT;
      CREATE TABLE IF NOT EXISTS events(id INTEGER PRIMARY KEY AUTOINCREMENT, player_id TEXT NOT NULL REFERENCES players(id), kind TEXT NOT NULL, data TEXT NOT NULL, created_at INTEGER NOT NULL) STRICT;
      CREATE INDEX IF NOT EXISTS events_player_id ON events(player_id, id);
      CREATE INDEX IF NOT EXISTS listings_status ON listings(status, created_at);
    `);
  }
  close() { this.db.close(); }
  transaction(fn) {
    this.db.exec('BEGIN IMMEDIATE');
    try {
      const result = fn();
      this.db.exec('COMMIT');
      return result;
    } catch (error) {
      this.db.exec('ROLLBACK');
      throw error;
    }
  }
  event(playerId, kind, data) {
    this.db.prepare('INSERT INTO events(player_id,kind,data,created_at) VALUES(?,?,?,?)').run(playerId, kind, JSON.stringify(data), this.now());
  }
  createSession(input) {
    shape(input, ['name']);
    const name = input.name === undefined ? '本地旅人' : text(input.name, 'name', 32);
    return this.transaction(() => {
      const player = {
        id: randomUUID(), name, revision: 0, chits: catalog.startingChits, energy: 50,
        containers: { backpack: { wood: 10, fiber: 8, berry: 5 }, storage: {} },
        recipes: Object.keys(catalog.recipes), checkpoint: { campId: 'camp-1', position: { x: 0, y: 0 } },
      };
      this.db.prepare('INSERT INTO players VALUES(?,?,?)').run(player.id, JSON.stringify(player), 0);
      const token = randomBytes(32).toString('hex');
      const expiresAt = this.now() + 24 * 60 * 60 * 1000;
      this.db.prepare('INSERT INTO sessions VALUES(?,?,?)').run(hash(token), player.id, expiresAt);
      this.event(player.id, 'player.created', { revision: 0 });
      return { token, expiresAt, player: this.snapshot(player.id), mode: 'local-guest' };
    });
  }
  authenticate(authorization) {
    if (typeof authorization !== 'string' || !/^Bearer [a-f0-9]{64}$/.test(authorization)) reject(401, 'unauthorized', 'A local guest bearer token is required');
    const row = this.db.prepare('SELECT * FROM sessions WHERE token_hash=?').get(hash(authorization.slice(7)));
    if (!row || row.expires_at <= this.now()) reject(401, 'unauthorized', 'Session is missing or expired');
    return row.player_id;
  }
  readPlayer(id) {
    const row = this.db.prepare('SELECT data FROM players WHERE id=?').get(id);
    if (!row) reject(404, 'not_found', 'Player not found');
    return JSON.parse(row.data);
  }
  savePlayer(player) {
    const previous = player.revision;
    player.revision += 1;
    const result = this.db.prepare('UPDATE players SET data=?,revision=? WHERE id=? AND revision=?').run(JSON.stringify(player), player.revision, player.id, previous);
    if (result.changes !== 1) reject(409, 'revision_conflict', 'Player changed during this operation');
  }
  snapshot(id) {
    return { ...this.readPlayer(id), town: this.db.prepare('SELECT town_id AS id,role,plot,contribution FROM members WHERE player_id=?').get(id) ?? null };
  }
  readTown(id) {
    const row = this.db.prepare('SELECT data FROM towns WHERE id=?').get(id);
    if (!row) reject(404, 'not_found', 'Town not found');
    return JSON.parse(row.data);
  }
  saveTown(town) {
    const previous = town.revision;
    town.revision += 1;
    const result = this.db.prepare('UPDATE towns SET data=?,revision=? WHERE id=? AND revision=?').run(JSON.stringify(town), town.revision, town.id, previous);
    if (result.changes !== 1) reject(409, 'town_revision_conflict', 'Town changed during this operation');
  }
  townView(id) {
    return { ...this.readTown(id), members: this.db.prepare(`SELECT m.player_id AS playerId,json_extract(p.data,'$.name') AS name,m.role,m.plot,m.contribution FROM members m JOIN players p ON p.id=m.player_id WHERE m.town_id=? ORDER BY m.plot`).all(id) };
  }
  mayor(playerId, townId) {
    const member = this.db.prepare('SELECT * FROM members WHERE player_id=? AND town_id=?').get(playerId, townId);
    if (member?.role !== 'mayor') reject(403, 'forbidden', 'Only the town mayor can do this');
  }
  week() {
    const day = new Intl.DateTimeFormat('en-CA', { timeZone: 'Asia/Shanghai', year: 'numeric', month: '2-digit', day: '2-digit' }).format(new Date(this.now()));
    const date = new Date(`${day}T00:00:00Z`);
    date.setUTCDate(date.getUTCDate() - (date.getUTCDay() + 6) % 7);
    return date.toISOString().slice(0, 10);
  }
  listing(id) {
    const row = this.db.prepare('SELECT * FROM listings WHERE id=?').get(id);
    if (!row) reject(404, 'not_found', 'Trade listing not found');
    return row;
  }
  listTrades() {
    return this.db.prepare('SELECT * FROM listings WHERE status=\'open\' ORDER BY created_at,id LIMIT 100').all();
  }
  listTowns(search = '') {
    text(search, 'search', 64, true);
    return this.db.prepare('SELECT data FROM towns ORDER BY id').all().map(row => JSON.parse(row.data)).filter(town => town.name.toLowerCase().includes(search.toLowerCase())).slice(0, 100);
  }
  ranking(id) {
    this.readTown(id);
    return this.townView(id).members.sort((a, b) => b.contribution - a.contribution || a.playerId.localeCompare(b.playerId));
  }
  eventsSince(playerId, after = 0) {
    integer(after, 'after', 0, Number.MAX_SAFE_INTEGER);
    const events = this.db.prepare('SELECT * FROM events WHERE player_id=? AND id>? ORDER BY id LIMIT 100').all(playerId, after).map(row => ({ id: row.id, kind: row.kind, data: JSON.parse(row.data), createdAt: row.created_at }));
    return { events, nextCursor: events.at(-1)?.id ?? after };
  }
  execute(playerId, key, command) {
    shape(command, ['operation', 'expectedRevision', 'args']);
    if (!commands.includes(command.operation)) reject(400, 'unknown_command', 'Unknown operation');
    if (typeof key !== 'string' || !/^[A-Za-z0-9_-]{1,128}$/.test(key)) reject(400, 'idempotency_key_required', 'Use an Idempotency-Key of 1–128 letters, digits, dashes or underscores');
    integer(command.expectedRevision, 'expectedRevision', 0, Number.MAX_SAFE_INTEGER);
    const fingerprint = hash(JSON.stringify(canonical(command)));
    return this.transaction(() => {
      const receipt = this.db.prepare('SELECT fingerprint,result FROM receipts WHERE player_id=? AND request_key=?').get(playerId, key);
      if (receipt) {
        if (receipt.fingerprint !== fingerprint) reject(409, 'idempotency_conflict', 'This request key was used for a different command');
        return { ...JSON.parse(receipt.result), replayed: true };
      }
      const player = this.readPlayer(playerId);
      if (player.revision !== command.expectedRevision) reject(409, 'revision_conflict', 'Fetch the current player state before changing it', { currentRevision: player.revision });
      const details = this.perform(command.operation, command.args ?? {}, player);
      this.savePlayer(player);
      this.event(playerId, command.operation, { revision: player.revision, ...details });
      const result = { operation: command.operation, player: this.snapshot(playerId), ...details, replayed: false };
      this.db.prepare('INSERT INTO receipts VALUES(?,?,?,?)').run(playerId, key, fingerprint, JSON.stringify(result));
      return result;
    });
  }
  perform(operation, args, player) {
    switch (operation) {
      case 'player.rename':
        shape(args, ['name']);
        player.name = text(args.name, 'name', 32);
        return {};
      case 'player.checkpoint':
        shape(args, ['campId', 'position']);
        if (!['camp-1', 'camp-2', 'camp-3'].includes(args.campId)) reject(400, 'unknown_camp', 'Unknown demo camp');
        shape(args.position, ['x', 'y']);
        if (![args.position.x, args.position.y].every(value => Number.isFinite(value) && Math.abs(value) <= 100000)) reject(400, 'invalid_input', 'Position is invalid');
        player.checkpoint = { campId: args.campId, position: { ...args.position } };
        return {};
      case 'inventory.move': {
        shape(args, ['itemId', 'quantity', 'from', 'to']);
        const quantity = integer(args.quantity, 'quantity');
        if (args.from === args.to) reject(400, 'invalid_input', 'Source and destination must differ');
        const from = container(player, args.from), to = container(player, args.to);
        removeItems(from, args.itemId, quantity);
        addItems(to, args.itemId, quantity);
        return {};
      }
      case 'inventory.drop':
        shape(args, ['itemId', 'quantity', 'container']);
        removeItems(container(player, args.container ?? 'backpack'), args.itemId, integer(args.quantity, 'quantity'));
        return {};
      case 'inventory.use': {
        shape(args, ['itemId', 'quantity']);
        const definition = item(args.itemId), quantity = integer(args.quantity, 'quantity');
        if (!definition.energy) reject(400, 'not_consumable', 'This item cannot be consumed');
        removeItems(player.containers.backpack, args.itemId, quantity);
        player.energy = Math.min(100, player.energy + definition.energy * quantity);
        return {};
      }
      case 'craft.make': {
        shape(args, ['recipeId', 'quantity']);
        const recipe = Object.hasOwn(catalog.recipes, args.recipeId ?? '') && catalog.recipes[args.recipeId];
        if (!recipe || !player.recipes.includes(args.recipeId)) reject(400, 'unknown_recipe', 'Recipe is not known');
        const quantity = integer(args.quantity, 'quantity');
        for (const [id, amount] of Object.entries(recipe.inputs)) removeItems(player.containers.backpack, id, amount * quantity);
        for (const [id, amount] of Object.entries(recipe.output)) if (amount) addItems(player.containers.backpack, id, amount * quantity);
        return { crafted: { recipeId: args.recipeId, quantity } };
      }
      case 'shop.buy': {
        shape(args, ['itemId', 'quantity']);
        const definition = item(args.itemId), quantity = integer(args.quantity, 'quantity');
        const cost = definition.buyPrice * quantity;
        spend(player, cost);
        addItems(player.containers.backpack, args.itemId, quantity);
        return { cost };
      }
      case 'reward.weekly': {
        shape(args, []);
        const period = this.week();
        if (this.db.prepare('SELECT 1 FROM claims WHERE player_id=? AND period=?').get(player.id, period)) reject(409, 'already_claimed', 'This week’s reward was already claimed');
        credit(player, catalog.weeklyReward.chits);
        for (const [id, amount] of Object.entries(catalog.weeklyReward.items)) addItems(player.containers.backpack, id, amount);
        this.db.prepare('INSERT INTO claims VALUES(?,?)').run(player.id, period);
        return { period, reward: catalog.weeklyReward };
      }
      case 'trade.list': {
        shape(args, ['itemId', 'quantity', 'unitPrice']);
        const quantity = integer(args.quantity, 'quantity'), price = integer(args.unitPrice, 'unitPrice', 1, 100000);
        removeItems(player.containers.backpack, args.itemId, quantity);
        const id = randomUUID();
        this.db.prepare('INSERT INTO listings VALUES(?,?,?,?,?,\'open\',NULL,?)').run(id, player.id, args.itemId, quantity, price, this.now());
        return { listing: this.listing(id) };
      }
      case 'trade.buy': {
        shape(args, ['listingId']);
        const listing = this.listing(text(args.listingId, 'listingId'));
        if (listing.status !== 'open') reject(409, 'listing_closed', 'Listing is no longer open');
        if (listing.seller_id === player.id) reject(400, 'self_trade', 'Cannot buy your own listing');
        const cost = listing.unit_price * listing.quantity;
        spend(player, cost);
        addItems(player.containers.backpack, listing.item_id, listing.quantity);
        const seller = this.readPlayer(listing.seller_id);
        credit(seller, cost);
        this.savePlayer(seller);
        this.db.prepare('UPDATE listings SET status=\'sold\',buyer_id=? WHERE id=?').run(player.id, listing.id);
        this.event(seller.id, 'trade.sold', { listingId: listing.id, chitsReceived: cost, revision: seller.revision });
        return { listing: this.listing(listing.id), cost };
      }
      case 'trade.cancel': {
        shape(args, ['listingId']);
        const listing = this.listing(text(args.listingId, 'listingId'));
        if (listing.seller_id !== player.id) reject(403, 'forbidden', 'Only the seller can cancel this listing');
        if (listing.status !== 'open') reject(409, 'listing_closed', 'Listing is no longer open');
        addItems(player.containers.backpack, listing.item_id, listing.quantity);
        this.db.prepare('UPDATE listings SET status=\'cancelled\' WHERE id=?').run(listing.id);
        return { listing: this.listing(listing.id) };
      }
      case 'town.create': {
        shape(args, ['name', 'rules']);
        if (this.snapshot(player.id).town) reject(409, 'already_in_town', 'Leave the current town first');
        const town = { id: randomUUID(), name: text(args.name, 'name', 32), rules: text(args.rules ?? '', 'rules', 200, true), announcement: '', funds: 0, revision: 0, capacity: catalog.townCapacity };
        this.db.prepare('INSERT INTO towns VALUES(?,?,?)').run(town.id, JSON.stringify(town), 0);
        this.db.prepare('INSERT INTO members(player_id,town_id,plot,role) VALUES(?,?,0,\'mayor\')').run(player.id, town.id);
        return { town: this.townView(town.id) };
      }
      case 'town.join': {
        shape(args, ['townId']);
        if (this.snapshot(player.id).town) reject(409, 'already_in_town', 'Leave the current town first');
        const town = this.townView(text(args.townId, 'townId'));
        const plot = Array.from({ length: town.capacity }, (_, index) => index).find(index => !town.members.some(member => member.plot === index));
        if (plot === undefined) reject(409, 'town_full', 'No vacant town plot');
        this.db.prepare('INSERT INTO members(player_id,town_id,plot,role) VALUES(?,?,?,\'member\')').run(player.id, town.id, plot);
        delete town.members;
        this.saveTown(town);
        return { town: this.townView(town.id) };
      }
      case 'town.update': {
        shape(args, ['townId', 'expectedTownRevision', 'name', 'rules', 'announcement']);
        const town = this.readTown(text(args.townId, 'townId'));
        this.mayor(player.id, town.id);
        integer(args.expectedTownRevision, 'expectedTownRevision', 0, Number.MAX_SAFE_INTEGER);
        if (town.revision !== args.expectedTownRevision) reject(409, 'town_revision_conflict', 'Fetch the current town state', { currentRevision: town.revision });
        if (args.name !== undefined) town.name = text(args.name, 'name', 32);
        if (args.rules !== undefined) town.rules = text(args.rules, 'rules', 200, true);
        if (args.announcement !== undefined) town.announcement = text(args.announcement, 'announcement', 500, true);
        this.saveTown(town);
        return { town: this.townView(town.id) };
      }
      case 'town.contribute': {
        shape(args, ['amount']);
        const amount = integer(args.amount, 'amount', 1, 100000);
        const member = this.snapshot(player.id).town;
        if (!member) reject(409, 'not_in_town', 'Join a town first');
        const town = this.readTown(member.id);
        if (town.funds + amount > catalog.maxChits || member.contribution + amount > catalog.maxChits) reject(409, 'balance_limit', 'Contribution limit reached');
        spend(player, amount);
        town.funds += amount;
        this.saveTown(town);
        this.db.prepare('UPDATE members SET contribution=contribution+? WHERE player_id=?').run(amount, player.id);
        return { town: this.townView(town.id), ranking: this.ranking(town.id) };
      }
      case 'town.leave': {
        shape(args, []);
        const member = this.snapshot(player.id).town;
        if (!member) reject(409, 'not_in_town', 'Not in a town');
        if (member.role === 'mayor') reject(409, 'mayor_must_dissolve', 'The mayor must dissolve the town instead');
        this.db.prepare('DELETE FROM members WHERE player_id=?').run(player.id);
        const town = this.readTown(member.id);
        this.saveTown(town);
        return {};
      }
      case 'town.dissolve': {
        shape(args, []);
        const member = this.snapshot(player.id).town;
        if (!member) reject(409, 'not_in_town', 'Not in a town');
        this.mayor(player.id, member.id);
        this.db.prepare('DELETE FROM towns WHERE id=?').run(member.id);
        return { dissolvedTownId: member.id };
      }
      default: reject(400, 'unknown_command', 'Unknown operation');
    }
  }
  capabilities() { return { compatibility: 'prototype-http-json', clientWireCompatibility: false, commands, modules: capabilities }; }
}
