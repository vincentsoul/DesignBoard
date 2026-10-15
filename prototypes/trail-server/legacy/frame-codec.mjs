// Framing recovered from ServerEntity.SendToServer/GetPkgLen/OnPkgRecvFin.
// Payloads are opaque: this is not an RSA/RC4 session or an APK-compatible server.
export const HEADER_BYTES = 12;
export const MAX_FRAME_BYTES = 0xfffff;

function int32(value, name) {
  if (!Number.isInteger(value) || value < -0x80000000 || value > 0x7fffffff) throw new RangeError(`${name} must be int32`);
}
function frameLength(header) {
  const length = header.readInt32BE(0);
  if (length < HEADER_BYTES || length > MAX_FRAME_BYTES) throw new RangeError('Invalid legacy frame length');
  return length;
}

export function encodeFrame({ opcode, sequence, body }) {
  int32(opcode, 'opcode');
  int32(sequence, 'sequence');
  if (!Buffer.isBuffer(body)) throw new TypeError('body must be an opaque Buffer');
  const length = HEADER_BYTES + body.length;
  if (length > MAX_FRAME_BYTES) throw new RangeError('Legacy frame exceeds the observed limit');
  const frame = Buffer.allocUnsafe(length);
  frame.writeInt32BE(length, 0);
  frame.writeInt32BE(opcode, 4);
  frame.writeInt32BE(sequence, 8);
  body.copy(frame, HEADER_BYTES);
  return frame;
}

export class FrameDecoder {
  constructor() {
    this.header = Buffer.alloc(HEADER_BYTES);
    this.headerRead = 0;
    this.body = null;
    this.bodyRead = 0;
    this.failed = false;
  }
  push(chunk) {
    if (this.failed) throw new Error('Decoder is closed after an invalid frame');
    if (!Buffer.isBuffer(chunk)) throw new TypeError('chunk must be a Buffer');
    const frames = [];
    let offset = 0;
    while (offset < chunk.length) {
      if (this.headerRead < HEADER_BYTES) {
        const count = Math.min(HEADER_BYTES - this.headerRead, chunk.length - offset);
        chunk.copy(this.header, this.headerRead, offset, offset + count);
        offset += count;
        this.headerRead += count;
        if (this.headerRead !== HEADER_BYTES) continue;
        try { this.body = Buffer.allocUnsafe(frameLength(this.header) - HEADER_BYTES); }
        catch (error) { this.failed = true; throw error; }
      }
      const count = Math.min(this.body.length - this.bodyRead, chunk.length - offset);
      chunk.copy(this.body, this.bodyRead, offset, offset + count);
      offset += count;
      this.bodyRead += count;
      if (this.bodyRead === this.body.length) {
        frames.push({ opcode: this.header.readInt32BE(4), sequence: this.header.readInt32BE(8), body: this.body });
        this.headerRead = 0;
        this.bodyRead = 0;
        this.body = null;
      }
    }
    return frames;
  }
  finish() {
    if (this.failed || this.headerRead || this.bodyRead) throw new Error('Stream ended with an invalid or truncated frame');
  }
}
