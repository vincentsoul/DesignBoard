import { constants, createPublicKey, publicEncrypt, randomBytes } from 'node:crypto';
import { TextDecoder } from 'node:util';
import { encodeFrame } from './frame-codec.mjs';
import { createPacketCipher } from './packet-cipher.mjs';

const utf8 = new TextDecoder('utf-8', { fatal: true });
function object(body) {
  const value = JSON.parse(utf8.decode(body));
  if (!value || typeof value !== 'object' || Array.isArray(value)) throw new TypeError('JSON object required');
  return value;
}

// Transport experiment only: no SDK authentication, login response, or game routing.
// Public key must belong to the local test client; no APK keys are embedded here.
export class LegacySession {
  #publicKey;
  #key;
  #cipher;
  #state = 'awaiting_session';
  constructor({ publicKey, sessionKey = randomBytes(16).toString('hex') }) {
    this.#publicKey = createPublicKey(publicKey);
    if (this.#publicKey.asymmetricKeyType !== 'rsa') throw new TypeError('RSA public key required');
    if (typeof sessionKey !== 'string' || !sessionKey.length) throw new TypeError('Session key string required');
    this.#key = sessionKey;
  }
  get state() { return this.#state; }
  receive(frame) {
    if (this.#state === 'closed') throw new Error('Session closed');
    try {
      if (this.#state === 'awaiting_session') {
        if (frame.opcode !== 1) throw new Error('GetSession must precede encrypted messages');
        const request = object(frame.body);
        if ((!Number.isInteger(request.timestmp) || request.timestmp < -0x80000000 || request.timestmp > 0x7fffffff) || typeof request.salt !== 'string') throw new TypeError('Invalid GetSession fields');
        const key = Buffer.from(this.#key, 'utf8');
        // KeyLen is byte length in this harness; native receiver consumes SessionKey.
        const payload = Buffer.from(JSON.stringify({ KeyLen: key.length, SessionKey: this.#key }));
        const body = publicEncrypt({ key: this.#publicKey, padding: constants.RSA_PKCS1_OAEP_PADDING, oaepHash: 'sha1' }, payload);
        // Echoing the request sequence is a harness convention awaiting client capture.
        const reply = encodeFrame({ opcode: 1, sequence: frame.sequence, body });
        this.#cipher = createPacketCipher(key);
        this.#key = undefined;
        this.#state = 'established';
        return { reply };
      }
      if (frame.opcode === 1) throw new Error('Duplicate session initialization');
      return { message: { opcode: frame.opcode, sequence: frame.sequence, payload: object(this.#cipher(frame.body)) } };
    } catch (error) {
      this.close();
      throw error;
    }
  }
  encodeReply({ opcode, sequence, payload }) {
    if (this.#state !== 'established') throw new Error('Session is not established');
    if (opcode === 1) throw new Error('Use the initialization response for opcode 1');
    if (!payload || typeof payload !== 'object' || Array.isArray(payload)) throw new TypeError('JSON object required');
    return encodeFrame({ opcode, sequence, body: this.#cipher(Buffer.from(JSON.stringify(payload))) });
  }
  close() {
    this.#state = 'closed';
    this.#key = undefined;
    this.#cipher = undefined;
  }
}
