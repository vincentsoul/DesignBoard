import { randomBytes, scrypt, timingSafeEqual, createHash } from 'node:crypto';
import { readFileSync } from 'node:fs';
import { promisify } from 'node:util';

const derive = promisify(scrypt);
const digest = (value) => createHash('sha256').update(value).digest();
const COOKIE = 'canvasroom_session';
const TTL = 12 * 60 * 60 * 1000;
const WINDOW = 15 * 60 * 1000;

export function loadAuthConfig() {
  const file = process.env.CANVASROOM_AUTH_FILE;
  if (!file) {
    if (process.env.CANVASROOM_REQUIRE_AUTH === '1') throw new Error('Authentication configuration is required');
    return null;
  }
  const config = JSON.parse(readFileSync(file, 'utf8'));
  if (typeof config.username !== 'string' || !config.username || !/^[a-f0-9]{64}$/.test(config.salt) || !/^[a-f0-9]{128}$/.test(config.passwordHash)) {
    throw new Error('Invalid authentication configuration');
  }
  return config;
}

export function createAuth(config, { secure = true, trustProxy = false } = {}) {
  if (!config) return null;
  const sessions = new Map();
  const attempts = new Map();
  function token(req) {
    const value = req.headers.cookie?.split(';').map((part) => part.trim()).find((part) => part.startsWith(`${COOKIE}=`))?.slice(COOKIE.length + 1);
    return value && /^[A-Za-z0-9_-]{43}$/.test(value) ? digest(value).toString('hex') : null;
  }
  function cookie(value) {
    return `${COOKIE}=${value}; Path=/; HttpOnly; SameSite=Strict${secure ? '; Secure' : ''}${value ? '' : '; Max-Age=0'}`;
  }
  function user(req) {
    const key = token(req);
    const expiry = key && sessions.get(key);
    if (!expiry || expiry <= Date.now()) { if (key) sessions.delete(key); return null; }
    return config.username;
  }
  return {
    user,
    async login(req, res, body) {
      const now = Date.now();
      const ip = (trustProxy && req.headers['x-forwarded-for']?.split(',').at(-1)?.trim()) || req.socket.remoteAddress;
      for (const [key, value] of attempts) if (value.until <= now) attempts.delete(key);
      if (!attempts.has(ip) && attempts.size >= 10000) return { status: 429, error: '登录请求过多，请稍后重试' };
      const attempt = attempts.get(ip) || { count: 0, until: now + WINDOW };
      if (attempt.count >= 5) {
        res.setHeader('Retry-After', Math.ceil((attempt.until - now) / 1000));
        return { status: 429, error: '尝试次数过多，请 15 分钟后重试' };
      }
      attempt.count += 1;
      attempts.set(ip, attempt);
      if (!body || typeof body.username !== 'string' || typeof body.password !== 'string' || body.password.length > 256 || body.username.length > 128) {
        return { status: 400, error: '请输入用户名和密码' };
      }
      const actual = await derive(body.password, config.salt, 64);
      if (!timingSafeEqual(actual, Buffer.from(config.passwordHash, 'hex')) || !timingSafeEqual(digest(body.username), digest(config.username))) {
        return { status: 401, error: '用户名或密码不正确' };
      }
      attempts.delete(ip);
      const old = token(req);
      if (old) sessions.delete(old);
      for (const [key, expiry] of sessions) if (expiry <= now) sessions.delete(key);
      if (sessions.size >= 1000) sessions.delete(sessions.keys().next().value);
      const value = randomBytes(32).toString('base64url');
      sessions.set(digest(value).toString('hex'), now + TTL);
      res.setHeader('Set-Cookie', cookie(value));
      return { status: 200, username: config.username };
    },
    logout(req, res) {
      const key = token(req);
      if (key) sessions.delete(key);
      res.setHeader('Set-Cookie', cookie(''));
    },
  };
}
