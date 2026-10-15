// Tags recovered from the APK's ProtoMemberAttribute constructors, not field order.
// This codec constructs data; appearance asset IDs still require client verification.
export const CHARACTER_FIELDS = Object.freeze({
  gender: 1, legs: 2, body: 3, hands: 4, feet: 5, hair: 6, face: 7, jaw: 8,
  ears: 9, backpack: 10, facialhair: 11, fatness: 12, eyetype: 13,
  mouthtype: 14, browtype: 15, pupiltype: 16, hat: 17, skincolour: 18,
  haircolour: 19, dog: 20, whiskers: 21, nose: 22,
});
const names = new Map(Object.entries(CHARACTER_FIELDS).map(([name, tag]) => [tag, name]));
const barberFields = ['nItemId', 'nModuleID', 'nModuleIDEx', 'nColorValue', 'nColorValueEx', 'nSize'];
const MAX_BYTES = 65536;
const utf8 = new TextDecoder('utf-8', { fatal: true });
const int32 = value => Number.isInteger(value) && value >= -0x80000000 && value <= 0x7fffffff;
function varint(value) {
  let n = BigInt.asUintN(64, BigInt(value));
  const bytes = [];
  do { bytes.push(Number(n & 127n) | (n > 127n ? 128 : 0)); n >>= 7n; } while (n);
  return Buffer.from(bytes);
}
function unvarint(buffer, cursor) {
  let result = 0n;
  for (let n = 0; n < 10; n++) {
    if (cursor.offset >= buffer.length) throw new Error('Truncated protobuf varint');
    const byte = buffer[cursor.offset++];
    if (n === 9 && byte > 1) throw new Error('Protobuf varint exceeds uint64');
    result |= BigInt(byte & 127) << BigInt(n * 7);
    if (!(byte & 128)) return result;
  }
  throw new Error('Unterminated protobuf varint');
}
function fromUrl(value) {
  if (typeof value !== 'string' || value.length > MAX_BYTES * 2 || !/^[A-Za-z0-9_-]*$/.test(value) || value.length % 4 === 1) throw new Error('Invalid base64url payload');
  const bytes = Buffer.from(value, 'base64url');
  if (bytes.toString('base64url') !== value || bytes.length > MAX_BYTES) throw new Error('Noncanonical or oversized base64url payload');
  return bytes;
}

export function encodeCharacterProto(appearance) {
  if (!appearance || typeof appearance !== 'object' || Array.isArray(appearance)) throw new TypeError('Appearance object required');
  const chunks = [];
  for (const name of Object.keys(appearance)) if (!Object.hasOwn(CHARACTER_FIELDS, name)) throw new Error(`Unknown appearance field: ${name}`);
  for (const [name, tag] of Object.entries(CHARACTER_FIELDS)) {
    if (!Object.hasOwn(appearance, name)) continue;
    const value = appearance[name];
    if (name === 'dog') {
      if (!Buffer.isBuffer(value) || value.length > MAX_BYTES) throw new TypeError('dog must contain opaque DogData protobuf bytes');
      chunks.push(varint(tag * 8 + 2), varint(value.length), value);
    } else {
      if (name === 'gender' ? typeof value !== 'boolean' : !int32(value)) throw new TypeError(`Invalid ${name}`);
      chunks.push(varint(tag * 8), varint(name === 'gender' ? Number(value) : value));
    }
  }
  const bytes = Buffer.concat(chunks);
  if (bytes.length > MAX_BYTES) throw new RangeError('Character protobuf too large');
  return bytes;
}
export function decodeCharacterProto(bytes) {
  if (!Buffer.isBuffer(bytes) || bytes.length > MAX_BYTES) throw new TypeError('Bounded protobuf Buffer required');
  const result = {};
  const cursor = { offset: 0 };
  while (cursor.offset < bytes.length) {
    const key = unvarint(bytes, cursor);
    const tag = Number(key >> 3n);
    const wire = Number(key & 7n);
    const name = names.get(tag);
    if (!name) throw new Error('Unmodeled CharacterData tag');
    if (name === 'dog') {
      if (wire !== 2) throw new Error('DogData requires a length-delimited field');
      const length = Number(unvarint(bytes, cursor));
      if (!Number.isSafeInteger(length) || length > bytes.length - cursor.offset) throw new Error('Truncated DogData');
      result.dog = Buffer.from(bytes.subarray(cursor.offset, cursor.offset + length));
      cursor.offset += length;
    } else {
      if (wire !== 0) throw new Error('Scalar CharacterData requires a varint field');
      const value = unvarint(bytes, cursor);
      result[name] = name === 'gender' ? value !== 0n : Number(BigInt.asIntN(32, value));
    }
  }
  return result;
}
export function encodeCharacterData({ appearance, barber = [] }) {
  const bytes = encodeCharacterProto(appearance);
  // Native Split(RemoveEmptyEntries) cannot recover an empty protobuf prefix.
  if (!bytes.length) throw new Error('Provide at least one explicit appearance field');
  if (!Array.isArray(barber) || barber.length > 1024) throw new TypeError('Bounded barber array required');
  const ids = new Set();
  const records = barber.map(row => {
    if (!row || barberFields.some(name => !int32(row[name]))) throw new TypeError('Six int32 barber values required');
    if (ids.has(row.nItemId)) throw new Error('Duplicate barber item');
    ids.add(row.nItemId);
    return barberFields.map(name => row[name]).join(',') + ';';
  }).join('');
  if (Buffer.byteLength(records) > MAX_BYTES) throw new RangeError('Barber payload too large');
  return `${bytes.toString('base64url')};V2${Buffer.from(records).toString('base64url')}`;
}
export function decodeCharacterData(value) {
  if (typeof value !== 'string' || value.length > MAX_BYTES * 4) throw new TypeError('Bounded characterData string required');
  const parts = value.split(';V2');
  if (parts.length > 2 || !parts[0]) throw new Error('Invalid V2 character envelope');
  const appearance = decodeCharacterProto(fromUrl(parts[0]));
  const barber = [];
  if (parts.length === 2 && parts[1]) {
    const text = utf8.decode(fromUrl(parts[1]));
    if (!text.endsWith(';')) throw new Error('Barber records must end with a semicolon');
    const ids = new Set();
    for (const record of text.slice(0, -1).split(';')) {
      const values = record.split(',');
      if (values.length !== 6 || values.some(v => !/^-?\d+$/.test(v) || !int32(Number(v)))) throw new Error('Invalid barber record');
      const row = Object.fromEntries(barberFields.map((name, i) => [name, Number(values[i])]));
      if (ids.has(row.nItemId)) throw new Error('Duplicate barber item');
      ids.add(row.nItemId);
      barber.push(row);
      if (barber.length > 1024) throw new Error('Too many barber records');
    }
  }
  return { appearance, barber };
}
