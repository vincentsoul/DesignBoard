import net from 'node:net';
import { constants, generateKeyPairSync, privateDecrypt } from 'node:crypto';
import { once } from 'node:events';
import { resolve } from 'node:path';
import { pathToFileURL } from 'node:url';
import { FrameDecoder, encodeFrame } from './frame-codec.mjs';
import { createPacketCipher } from './packet-cipher.mjs';
import { LegacySession } from './session.mjs';

// A synthetic client and server exercise real TCP; this never runs the APK.
export async function runWireDemo() {
  const { publicKey, privateKey } = generateKeyPairSync('rsa', { modulusLength: 2048 });
  const sockets = new Set();
  const steps = [];
  let serverFailure;
  const server = net.createServer(socket => {
    sockets.add(socket);
    const decoder = new FrameDecoder();
    const session = new LegacySession({ publicKey: publicKey.export({ type: 'spki', format: 'pem' }) });
    socket.on('close', () => { sockets.delete(socket); session.close(); });
    socket.on('error', error => { serverFailure = error; });
    socket.on('data', chunk => {
      try {
        for (const frame of decoder.push(chunk)) {
          const result = session.receive(frame);
          if (result.reply) { socket.write(result.reply); steps.push('rsa_session_response_sent'); }
          else {
            if (result.message.opcode !== 801 || Object.keys(result.message.payload).length) throw new Error('Expected synthetic empty inventory request');
            steps.push('encrypted_inventory_request_received');
            socket.write(session.encodeReply({ opcode: 801, sequence: frame.sequence, payload: { status: 0, items: [] } }));
          }
        }
      } catch (error) { serverFailure = error; socket.destroy(); }
    });
  });
  let client;
  try {
    server.listen(0, '127.0.0.1');
    await once(server, 'listening');
    client = net.createConnection({ host: '127.0.0.1', port: server.address().port });
    const complete = new Promise((resolveDemo, reject) => {
      const decoder = new FrameDecoder();
      let cipher;
      client.setTimeout(5000, () => reject(new Error('Wire demo timed out')));
      client.on('error', reject);
      client.on('end', () => reject(serverFailure ?? new Error('Peer ended before completion')));
      client.on('data', chunk => {
        try {
          for (const frame of decoder.push(chunk)) {
            if (!cipher) {
              if (frame.opcode !== 1 || frame.sequence !== 1) throw new Error('Unexpected session reply');
              const payload = privateDecrypt({ key: privateKey, padding: constants.RSA_PKCS1_OAEP_PADDING, oaepHash: 'sha1' }, frame.body);
              const session = JSON.parse(payload.toString('utf8'));
              if (session.KeyLen !== Buffer.byteLength(session.SessionKey)) throw new Error('Key length mismatch');
              cipher = createPacketCipher(Buffer.from(session.SessionKey));
              steps.push('rsa_session_response_decrypted');
              const request = encodeFrame({ opcode: 801, sequence: 2, body: cipher(Buffer.from('{}')) });
              client.write(request.subarray(0, 5));
              client.write(request.subarray(5));
            } else {
              if (frame.opcode !== 801 || frame.sequence !== 2) throw new Error('Unexpected inventory reply');
              const response = JSON.parse(cipher(frame.body).toString('utf8'));
              if (response.status !== 0 || !Array.isArray(response.items) || response.items.length) throw new Error('Unexpected fixture response');
              steps.push('encrypted_inventory_response_decrypted');
              decoder.finish();
              resolveDemo({ mode: 'synthetic-wire-harness', originalClientFlowVerified: false, sdkLogin: 'not_implemented', steps });
            }
          }
        } catch (error) { reject(error); }
      });
    });
    await once(client, 'connect');
    client.write(encodeFrame({ opcode: 1, sequence: 1, body: Buffer.from(JSON.stringify({ timestmp: 1, salt: 'local-test' })) }));
    return await complete;
  } finally {
    client?.destroy();
    for (const socket of sockets) socket.destroy();
    if (server.listening) await new Promise(resolveClose => server.close(resolveClose));
  }
}

if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  console.log(JSON.stringify(await runWireDemo(), null, 2));
}
