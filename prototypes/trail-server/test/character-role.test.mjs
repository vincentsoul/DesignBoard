import test from 'node:test';
import assert from 'node:assert/strict';
import { encodeCharacterProto, decodeCharacterProto, encodeCharacterData, decodeCharacterData } from '../legacy/character-codec.mjs';
import { buildLoginResponse } from '../legacy/role-response.mjs';

const appearance = { gender: false, legs: 150 };
const barber = [{ nItemId: 1, nModuleID: 2, nModuleIDEx: 3, nColorValue: 4, nColorValueEx: 5, nSize: 6 }];
const role = () => ({ ID: 1001, name: '旅人', State: 4, campLocation: 1, appearance, barber, dataStore: { country: 'local-test' } });

test('known protobuf bytes encode false, multibyte int, negative int32 and tag 22', () => {
  const value = { gender: false, legs: 150, body: -1, nose: 300 };
  const bytes = Buffer.from('080010960118ffffffffffffffffff01b001ac02', 'hex');
  assert.deepEqual(encodeCharacterProto(value), bytes);
  assert.deepEqual(decodeCharacterProto(bytes), value);
});

test('V2 envelope matches an independent ASCII/base64 vector with six barber values', () => {
  const encoded = 'CAAQlgE;V2MSwyLDMsNCw1LDY7';
  assert.equal(encodeCharacterData({ appearance, barber }), encoded);
  assert.deepEqual(decodeCharacterData(encoded), { appearance, barber });
  assert.equal(encodeCharacterData({ appearance: { gender: false } }), 'CAA;V2');
  assert.deepEqual(decodeCharacterData('CAA;V2'), { appearance: { gender: false }, barber: [] });
});

test('opaque DogData bytes preserve the recovered length-delimited tag 20', () => {
  const dog = Buffer.from('0a0178', 'hex');
  const bytes = Buffer.from('a201030a0178', 'hex');
  assert.deepEqual(encodeCharacterProto({ dog }), bytes);
  assert.deepEqual(decodeCharacterProto(bytes), { dog });
});

test('truncation, overflow, wrong wire types and unmodeled fields are rejected', () => {
  for (const hex of ['08', '00', '0d00000000', 'a20102ff', '08ffffffffffffffffffff02', '980601']) {
    assert.throws(() => decodeCharacterProto(Buffer.from(hex, 'hex')));
  }
  assert.throws(() => encodeCharacterProto({ legs: 1.2 }));
  assert.throws(() => encodeCharacterProto({ gender: 0 }));
  assert.throws(() => encodeCharacterProto({ unknown: 1 }));
  assert.throws(() => encodeCharacterProto({ dog: 'not-bytes' }));
});

test('invalid or empty envelopes and duplicate barber records fail explicitly', () => {
  for (const value of [';V2', 'A;V2', 'CB;V2', 'CA==;V2', 'CAA;V2a;V2b', '{}']) assert.throws(() => decodeCharacterData(value));
  assert.throws(() => encodeCharacterData({ appearance: {} }));
  assert.throws(() => encodeCharacterData({ appearance, barber: [...barber, ...barber] }));
  assert.throws(() => decodeCharacterData('CAA;V2' + Buffer.from('1,2,3,4,5;').toString('base64url')));
  assert.throws(() => decodeCharacterData('CAA;V2' + Buffer.from('1,2,3,4,5,6').toString('base64url')));
});

test('login builder emits string envelopes and preserves protocol casing and integer arrays', () => {
  const input = { ...role(), stamina: 12345, infinite_pos: -100, recipes: [3, 8],
    FurthestCampLocInOtherArea: [{ AreaType: 2, FurthestCampLoc: 9 }] };
  const response = buildLoginResponse(input);
  assert.equal(response.status, 0);
  assert.equal(response.ID, 1001);
  assert.equal(response.State, 4);
  assert.equal(response.characterData, 'CAAQlgE;V2MSwyLDMsNCw1LDY7');
  assert.equal(response.dataStore, '{"country":"local-test"}');
  assert.equal(response.stamina, 12345); // No guessed conversion from prototype energy units.
  assert.equal(response.infinite_pos, -100);
  input.recipes.push(99);
  input.FurthestCampLocInOtherArea[0].AreaType = 99;
  assert.deepEqual(response.recipes, [3, 8]);
  assert.deepEqual(response.FurthestCampLocInOtherArea, [{ AreaType: 2, FurthestCampLoc: 9 }]);
  assert.equal(Object.hasOwn(response, 'appearance'), false);
});

test('login builder requires explicit location and rejects SDK reflection and wrong field types', () => {
  for (const patch of [{ ID: undefined }, { State: 0.5 }, { stamina: 1.5 }, { campLocation: '1' },
    { dataStore: '{}' }, { dataStore: new Date() }, { recipes: ['3'] }, { sauth_json: 'should-not-be-reflected' },
    { FurthestCampLocInOtherArea: [{ AreaType: '2', FurthestCampLoc: 9 }] }]) {
    assert.throws(() => buildLoginResponse({ ...role(), ...patch }));
  }
  const missing = role(); delete missing.campLocation;
  assert.throws(() => buildLoginResponse(missing), /Explicit campLocation/);
});
