// Compatibility with the observed native RC4.Init + EncryptNoRound branch.
// Each body restarts at the post-KSA state; this is not a persistent RC4 stream.
// Legacy compatibility only. Do not use this cipher for new protocols.
export function createPacketCipher(key) {
  if (!Buffer.isBuffer(key) || key.length === 0) throw new TypeError('key must be a nonempty Buffer');
  const initial = Uint8Array.from({ length: 256 }, (_, i) => i);
  let j = 0;
  for (let i = 0; i < 256; i++) {
    j = (j + initial[i] + key[i % key.length]) & 255;
    [initial[i], initial[j]] = [initial[j], initial[i]];
  }
  return function transform(body) {
    if (!Buffer.isBuffer(body)) throw new TypeError('body must be a Buffer');
    const box = initial.slice();
    const result = Buffer.alloc(body.length);
    let x = 0;
    let y = 0;
    for (let n = 0; n < body.length; n++) {
      x = (x + 1) & 255;
      y = (y + box[x]) & 255;
      [box[x], box[y]] = [box[y], box[x]];
      result[n] = body[n] ^ box[(box[x] + box[y]) & 255];
    }
    return result;
  };
}
