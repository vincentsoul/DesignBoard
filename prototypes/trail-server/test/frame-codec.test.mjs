import test from 'node:test';
import assert from 'node:assert/strict';
import { encodeFrame, FrameDecoder, MAX_FRAME_BYTES } from '../legacy/frame-codec.mjs';

test('native-observed field order matches an independently specified big-endian byte vector', () => {
  const packet = encodeFrame({ opcode: 10, sequence: 1, body: Buffer.from('{}') });
  assert.equal(packet.toString('hex'), '0000000e0000000a000000017b7d');
  const decoder = new FrameDecoder();
  const [frame] = decoder.push(Buffer.from('000000100000032100000007000102ff', 'hex'));
  assert.equal(frame.opcode, 801);
  assert.equal(frame.sequence, 7);
  assert.equal(frame.body.toString('hex'), '000102ff');
  decoder.finish();
});

test('TCP fragmentation at every byte boundary preserves opaque payloads', () => {
  const payload = Buffer.from('背包\u0000\ufffd');
  const packet = encodeFrame({ opcode: 801, sequence: 9, body: payload });
  for (let boundary = 0; boundary <= packet.length; boundary++) {
    const decoder = new FrameDecoder();
    const frames = [...decoder.push(packet.subarray(0, boundary)), ...decoder.push(packet.subarray(boundary))];
    assert.equal(frames.length, 1);
    assert.deepEqual(frames[0].body, payload);
    decoder.finish();
  }
});

test('coalesced frames, including an empty body, are delivered separately', () => {
  const decoder = new FrameDecoder();
  const frames = decoder.push(Buffer.concat([
    encodeFrame({ opcode: 1, sequence: 1, body: Buffer.alloc(0) }),
    encodeFrame({ opcode: 2, sequence: 2, body: Buffer.from([3]) }),
  ]));
  assert.deepEqual(frames.map(frame => [frame.opcode, frame.sequence, frame.body.length]), [[1, 1, 0], [2, 2, 1]]);
  decoder.finish();
});

test('a valid coalesced batch may exceed the maximum individual packet length', () => {
  const packet = encodeFrame({ opcode: 10, sequence: 1, body: Buffer.alloc(MAX_FRAME_BYTES - 12, 0xa5) });
  const decoder = new FrameDecoder();
  const frames = decoder.push(Buffer.concat([packet, packet]));
  assert.equal(frames.length, 2);
  assert.equal(frames[1].body.length, MAX_FRAME_BYTES - 12);
  assert.equal(frames[1].body.at(-1), 0xa5);
  decoder.finish();
});

test('corrupt, undersized and 1 MiB packet declarations close the decoder', () => {
  for (const length of [-1, 0, 11, 0x100000, 0x7fffffff]) {
    const header = Buffer.alloc(12);
    header.writeInt32BE(length);
    const decoder = new FrameDecoder();
    assert.throws(() => decoder.push(header), RangeError);
    assert.throws(() => decoder.push(Buffer.alloc(0)), /closed/);
  }
});

test('unfinished TCP headers and bodies are reported as truncated', () => {
  for (const bytes of [Buffer.from([0, 0, 0]), Buffer.from('000000140000000a00000001aabb', 'hex')]) {
    const decoder = new FrameDecoder();
    assert.equal(decoder.push(bytes).length, 0);
    assert.throws(() => decoder.finish(), /truncated/);
  }
  assert.throws(() => encodeFrame({ opcode: 10, sequence: 1, body: Buffer.alloc(MAX_FRAME_BYTES) }), RangeError);
});
