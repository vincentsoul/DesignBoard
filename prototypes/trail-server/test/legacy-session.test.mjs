import test from 'node:test';
import assert from 'node:assert/strict';
import { constants, generateKeyPairSync, privateDecrypt } from 'node:crypto';
import { LegacySession } from '../legacy/session.mjs';
import { FrameDecoder } from '../legacy/frame-codec.mjs';
import { createPacketCipher } from '../legacy/packet-cipher.mjs';
import { runWireDemo } from '../legacy/wire-demo.mjs';

const keys = generateKeyPairSync('rsa', { modulusLength: 2048 });
const publicKey = keys.publicKey.export({ type: 'spki', format: 'pem' });
const request = (payload = { timestmp: 1, salt: 'synthetic' }) => ({ opcode: 1, sequence: 9, body: Buffer.from(JSON.stringify(payload)) });
const make = options => new LegacySession({ publicKey, sessionKey: 'synthetic-session', ...options });
const packet = frame => new FrameDecoder().push(frame)[0];

test('GetSession response uses RSA OAEP-SHA1 and UTF-8 session bytes', () => {
  const session = make({ sessionKey: '本地-key' });
  const response = packet(session.receive(request()).reply);
  assert.equal(response.opcode, 1);
  assert.equal(response.sequence, 9); // Harness convention, not a captured server response.
  assert.equal(response.body.length, 256);
  const plaintext = privateDecrypt({ key: keys.privateKey, padding: constants.RSA_PKCS1_OAEP_PADDING, oaepHash: 'sha1' }, response.body);
  assert.deepEqual(JSON.parse(plaintext), { KeyLen: Buffer.byteLength('本地-key'), SessionKey: '本地-key' });
  assert.throws(() => privateDecrypt({ key: keys.privateKey, padding: constants.RSA_PKCS1_OAEP_PADDING, oaepHash: 'sha256' }, response.body));
  assert.equal(session.state, 'established');
});

test('independent sessions decrypt only their own messages and replies', () => {
  const a = make({ sessionKey: 'session-a' });
  const b = make({ sessionKey: 'session-b' });
  a.receive(request()); b.receive(request());
  const cipherA = createPacketCipher(Buffer.from('session-a'));
  const frame = { opcode: 801, sequence: 10, body: cipherA(Buffer.from('{}')) };
  assert.deepEqual(a.receive(frame).message, { opcode: 801, sequence: 10, payload: {} });
  assert.throws(() => b.receive(frame));
  assert.equal(b.state, 'closed');
  const response = packet(a.encodeReply({ opcode: 801, sequence: 10, payload: { status: 0, items: [] } }));
  assert.deepEqual(JSON.parse(cipherA(response.body)), { status: 0, items: [] });
  assert.equal(a.state, 'established');
});

test('missing handshake, duplicate initialization, and closed sessions fail', () => {
  const early = make();
  assert.throws(() => early.encodeReply({ opcode: 801, sequence: 1, payload: {} }), /not established/);
  assert.throws(() => early.receive({ ...request(), opcode: 801 }), /must precede/);
  assert.equal(early.state, 'closed');
  assert.throws(() => early.receive(request()), /closed/);
  const duplicate = make();
  duplicate.receive(request());
  assert.throws(() => duplicate.receive(request()), /Duplicate/);
  assert.equal(duplicate.state, 'closed');
});

test('malformed handshake and encrypted JSON close the transport session', () => {
  for (const body of [Buffer.from('[]'), Buffer.from('{'), Buffer.from([0xff]), Buffer.from('{}'), Buffer.from('{"timestmp":1,"salt":2}'), Buffer.from('{"timestmp":2147483648,"salt":"x"}')]) {
    const session = make();
    assert.throws(() => session.receive({ ...request(), body }));
    assert.equal(session.state, 'closed');
  }
  const session = make();
  session.receive(request());
  const cipher = createPacketCipher(Buffer.from('synthetic-session'));
  assert.throws(() => session.receive({ opcode: 801, sequence: 10, body: cipher(Buffer.from('null')) }));
  assert.equal(session.state, 'closed');
});

test('RSA payload overflow cannot leave an established session', () => {
  const session = make({ sessionKey: 'x'.repeat(512) });
  assert.throws(() => session.receive(request()));
  assert.equal(session.state, 'closed');
});

test('real loopback TCP completes synthetic RSA handshake and encrypted inventory exchange', { timeout: 10000 }, async () => {
  const report = await runWireDemo();
  assert.equal(report.originalClientFlowVerified, false);
  assert.equal(report.sdkLogin, 'not_implemented');
  assert.deepEqual(report.steps, ['rsa_session_response_sent', 'rsa_session_response_decrypted', 'encrypted_inventory_request_received', 'encrypted_inventory_response_decrypted']);
});
