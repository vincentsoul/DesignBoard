import test from 'node:test';
import assert from 'node:assert/strict';
import { createPacketCipher } from '../legacy/packet-cipher.mjs';

test('post-KSA PRGA matches independent RC4 known-answer vectors', () => {
  for (const [key, plaintext, ciphertext] of [
    ['Key', 'Plaintext', 'bbf316e8d940af0ad3'],
    ['Wiki', 'pedia', '1021bf0420'],
    ['Secret', 'Attack at dawn', '45a01f645fc35b383552544b9bf5'],
  ]) {
    assert.equal(createPacketCipher(Buffer.from(key))(Buffer.from(plaintext)).toString('hex'), ciphertext);
  }
});

test('packets restart state, preserve inputs, and decrypt symmetrically', () => {
  const key = Buffer.from('local-test-key');
  const originalKey = Buffer.from(key);
  const cipher = createPacketCipher(key);
  const body = Buffer.from('{"status":0,"items":[]}');
  const copy = Buffer.from(body);
  const first = cipher(body);
  cipher(Buffer.alloc(4096));
  assert.deepEqual(cipher(body), first);
  assert.deepEqual(cipher(first), body);
  assert.deepEqual(body, copy);
  assert.deepEqual(key, originalKey);
  key.fill(0); // The schedule is already derived, so caller mutation has no effect.
  assert.deepEqual(cipher(body), first);
  assert.deepEqual(cipher(Buffer.alloc(0)), Buffer.alloc(0));
});

test('invalid keys and payloads fail before transformation', () => {
  assert.throws(() => createPacketCipher(Buffer.alloc(0)), TypeError);
  assert.throws(() => createPacketCipher('key'), TypeError);
  assert.throws(() => createPacketCipher(Buffer.from('key'))('body'), TypeError);
});

test('encrypted inventory frame survives TCP splits and recovers JSON', async () => {
  const { encodeFrame, FrameDecoder } = await import('../legacy/frame-codec.mjs');
  const cipher = createPacketCipher(Buffer.from('synthetic-local-session'));
  const response = { status: 0, items: [] };
  const frame = encodeFrame({ opcode: 801, sequence: 7, body: cipher(Buffer.from(JSON.stringify(response))) });
  const decoder = new FrameDecoder();
  const packets = [];
  for (let i = 0; i < frame.length; i += 3) packets.push(...decoder.push(frame.subarray(i, i + 3)));
  decoder.finish();
  assert.equal(packets.length, 1);
  assert.equal(packets[0].opcode, 801);
  assert.equal(packets[0].sequence, 7);
  assert.deepEqual(JSON.parse(cipher(packets[0].body).toString('utf8')), response);
});
