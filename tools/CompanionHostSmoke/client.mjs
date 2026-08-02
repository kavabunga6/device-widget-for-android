import crypto from 'node:crypto';
import tls from 'node:tls';

const [portText, expectedFingerprint, pairingCode, installationId = 'smoke-device-0001', label = 'device'] = process.argv.slice(2);
const port = Number(portText);
const socket = tls.connect({ host: '127.0.0.1', port, rejectUnauthorized: false, minVersion: 'TLSv1.2' });
let buffer = Buffer.alloc(0);
let phase = 'http';
let resolveHandshake;
let rejectHandshake;
const handshake = new Promise((resolve, reject) => {
  resolveHandshake = resolve;
  rejectHandshake = reject;
});
const frames = [];
const frameWaiters = [];

socket.on('error', error => {
  rejectHandshake?.(error);
  while (frameWaiters.length > 0) frameWaiters.shift().reject(error);
});

socket.on('data', chunk => {
  buffer = Buffer.concat([buffer, chunk]);
  if (phase === 'http') {
    const end = buffer.indexOf('\r\n\r\n');
    if (end < 0) return;
    const response = buffer.subarray(0, end).toString('utf8');
    buffer = buffer.subarray(end + 4);
    if (!response.startsWith('HTTP/1.1 101')) {
      rejectHandshake(new Error(`Unexpected upgrade response: ${response}`));
      return;
    }
    phase = 'websocket';
    resolveHandshake();
  }
  pumpFrames();
});

await new Promise((resolve, reject) => {
  socket.once('secureConnect', resolve);
  socket.once('error', reject);
});
const certificate = socket.getPeerCertificate(true);
const actualFingerprint = crypto.createHash('sha256').update(certificate.raw).digest('hex');
if (!sameHex(actualFingerprint, expectedFingerprint)) throw new Error('TLS certificate fingerprint mismatch.');

const websocketKey = crypto.randomBytes(16).toString('base64');
socket.write([
  'GET /companion HTTP/1.1',
  'Host: localhost',
  'Upgrade: websocket',
  'Connection: Upgrade',
  `Sec-WebSocket-Key: ${websocketKey}`,
  'Sec-WebSocket-Version: 13',
  '',
  ''
].join('\r\n'));
await handshake;

sendJson({
  protocolVersion: 1,
  mode: 'pair',
  device: {
    installationId,
    displayName: 'Smoke phone',
    manufacturer: 'Android',
    model: 'Virtual',
    androidVersion: '16',
    apiLevel: 36
  },
  credential: pairingCode
});
const hello = JSON.parse((await nextFrame()).payload.toString('utf8'));
if (!hello.accepted || typeof hello.authToken !== 'string' || hello.authToken.length < 20)
  throw new Error(`Pairing rejected: ${JSON.stringify(hello)}`);

sendJson({ type: 'status', batteryPercent: 73, isCharging: false, isScreenOn: true, isLocked: false,
  sentAtUnixMilliseconds: Date.now() });
sendJson({ type: 'notification', notificationId: `smoke-${label}`, packageName: 'dev.androidwidget.smoke',
  appName: 'Messages', title: 'Alex', preview: `Companion protocol works: ${label}`,
  postedAtUnixMilliseconds: Date.now() });
await new Promise(resolve => setTimeout(resolve, 150));
sendFrame(0x8, Buffer.from([0x03, 0xe8]));
const close = await nextFrame();
if (close.opcode !== 0x8) throw new Error('Server did not acknowledge the close frame.');
socket.end();
console.log('TLS client: PASS');

function sendJson(value) {
  sendFrame(0x1, Buffer.from(JSON.stringify(value), 'utf8'));
}

function sendFrame(opcode, payload) {
  const lengthBytes = payload.length < 126 ? 0 : 2;
  if (payload.length > 0xffff) throw new Error('Smoke frame is too large.');
  const header = Buffer.alloc(2 + lengthBytes + 4);
  header[0] = 0x80 | opcode;
  if (lengthBytes === 0) header[1] = 0x80 | payload.length;
  else {
    header[1] = 0x80 | 126;
    header.writeUInt16BE(payload.length, 2);
  }
  const maskOffset = 2 + lengthBytes;
  const mask = crypto.randomBytes(4);
  mask.copy(header, maskOffset);
  const masked = Buffer.alloc(payload.length);
  for (let index = 0; index < payload.length; index++) masked[index] = payload[index] ^ mask[index % 4];
  socket.write(Buffer.concat([header, masked]));
}

function nextFrame() {
  if (frames.length > 0) return Promise.resolve(frames.shift());
  return new Promise((resolve, reject) => frameWaiters.push({ resolve, reject }));
}

function pumpFrames() {
  while (phase === 'websocket' && buffer.length >= 2) {
    const opcode = buffer[0] & 0x0f;
    let length = buffer[1] & 0x7f;
    let headerLength = 2;
    if (length === 126) {
      if (buffer.length < 4) return;
      length = buffer.readUInt16BE(2);
      headerLength = 4;
    } else if (length === 127) throw new Error('Unexpected 64-bit smoke frame.');
    if ((buffer[1] & 0x80) !== 0) throw new Error('Server frames must not be masked.');
    if (buffer.length < headerLength + length) return;
    const frame = { opcode, payload: buffer.subarray(headerLength, headerLength + length) };
    buffer = buffer.subarray(headerLength + length);
    if (frameWaiters.length > 0) frameWaiters.shift().resolve(frame);
    else frames.push(frame);
  }
}

function sameHex(left, right) {
  const a = Buffer.from(left, 'hex');
  const b = Buffer.from(right, 'hex');
  return a.length === b.length && crypto.timingSafeEqual(a, b);
}
