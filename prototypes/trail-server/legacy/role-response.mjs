import { encodeCharacterData } from './character-codec.mjs';

// Static contract builder, not SDK authentication or a complete spawn response.
// All location, state, stamina, and asset IDs must be chosen by the caller.
const ints = ['ID', 'State', 'campLocation', 'furthestCampLoc', 'townID', 'homeTownID',
  'homeTownPlotIndex', 'stamina', 'infinite_pos', 'body_condition', 'isTrading',
  'Gender', 'currentSpeed', 'player_house_level', 'WeekFirstLoginTimestamp'];
const strings = ['name', 'gamerID', 'timeUpdated'];
const arrays = ['recipes', 'UnlockRiverCampIds'];
const fields = new Set([...ints, ...strings, ...arrays, 'FurthestCampLocInOtherArea', 'appearance', 'barber', 'dataStore']);
const int32 = n => Number.isInteger(n) && n >= -0x80000000 && n <= 0x7fffffff;
export function buildLoginResponse(role) {
  if (!role || typeof role !== 'object' || Array.isArray(role)) throw new TypeError('Role object required');
  for (const name of Object.keys(role)) if (!fields.has(name)) throw new Error(`Unmodeled role field: ${name}`);
  // These are fixture requirements, not a claim that all are mandatory in every client branch.
  for (const name of ['ID', 'State', 'campLocation', 'name']) if (!Object.hasOwn(role, name)) throw new Error(`Explicit ${name} required`);
  const result = { status: 0 };
  for (const name of ints) {
    if (!Object.hasOwn(role, name)) continue;
    if (!int32(role[name])) throw new TypeError(`${name} must be int32`);
    result[name] = role[name];
  }
  for (const name of strings) {
    if (!Object.hasOwn(role, name)) continue;
    if (typeof role[name] !== 'string') throw new TypeError(`${name} must be a string`);
    result[name] = role[name];
  }
  for (const name of arrays) {
    if (!Object.hasOwn(role, name)) continue;
    if (!Array.isArray(role[name]) || role[name].some(v => !int32(v))) throw new TypeError(`${name} must contain int32 values`);
    result[name] = [...role[name]];
  }
  if (Object.hasOwn(role, 'FurthestCampLocInOtherArea')) {
    if (!Array.isArray(role.FurthestCampLocInOtherArea)) throw new TypeError('Area locations must be an array');
    result.FurthestCampLocInOtherArea = role.FurthestCampLocInOtherArea.map(row => {
      if (!row || !int32(row.AreaType) || !int32(row.FurthestCampLoc)) throw new TypeError('Invalid area location');
      return { AreaType: row.AreaType, FurthestCampLoc: row.FurthestCampLoc };
    });
  }
  if (!role.dataStore || ![Object.prototype, null].includes(Object.getPrototypeOf(role.dataStore))) throw new TypeError('dataStore object required for JSON string encoding');
  result.dataStore = JSON.stringify(role.dataStore);
  result.characterData = encodeCharacterData({ appearance: role.appearance, barber: role.barber });
  return result;
}
