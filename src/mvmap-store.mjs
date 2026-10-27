import { mkdir, readFile, readdir, rename, writeFile, rm } from 'node:fs/promises';
import { createHash, randomUUID } from 'node:crypto';
import path from 'node:path';

const fail = (message, status = 400) => Object.assign(new Error(message), { status });
const idPattern = /^[a-zA-Z0-9_-]{1,100}$/;
export function validateMap(doc, id = doc?.id) {
  if (!idPattern.test(id || '') || doc?.id !== id) throw fail('地图 ID 无效');
  if (doc.version !== 1 || typeof doc.name !== 'string' || doc.name.length > 1000) throw fail('地图格式无效');
  for (const key of ['areas', 'rooms', 'doors', 'locks', 'keys']) {
    if (!Array.isArray(doc[key]) || doc[key].length > 100000) throw fail(`地图字段无效：${key}`);
    const ids = new Set();
    for (const item of doc[key]) {
      if (!item || !idPattern.test(item.id || '') || ids.has(item.id)) throw fail(`地图对象 ID 无效：${key}`);
      ids.add(item.id);
    }
  }
  if (!doc.areas.length || !doc.detail || typeof doc.detail !== 'object' || Array.isArray(doc.detail)) throw fail('缺少区域或灰盒数据');
  for (const item of [...doc.areas, ...doc.rooms, ...doc.keys, ...doc.locks]) {
    if (typeof item.name !== 'string') throw fail('对象名称无效');
  }
  for (const room of doc.rooms) {
    if (!Array.isArray(room.cells) || room.cells.some(c => typeof c !== 'string' || !/^-?\d+,-?\d+$/.test(c))) throw fail('房间网格无效');
  }
  for (const lock of doc.locks) if (!Array.isArray(lock.keyIds) || lock.keyIds.some(k => typeof k !== 'string')) throw fail('钥匙关联无效');
  for (const key of doc.keys) if (![key.x, key.y].every(Number.isSafeInteger)) throw fail('钥匙坐标无效');
  for (const door of doc.doors) {
    if (!['v', 'h'].includes(door.o) || !['door', 'passage', 'oneway'].includes(door.kind) || ![door.x, door.y].every(Number.isSafeInteger) || ![1, -1].includes(door.dir)) throw fail('门数据无效');
  }
  for (const detail of Object.values(doc.detail)) {
    if (!detail || ![4, 8, 16].includes(detail.sub) || !Array.isArray(detail.boxes)) throw fail('灰盒数据无效');
    for (const b of detail.boxes) {
      if (![b.x, b.y, b.w, b.h].every(Number.isFinite) || b.w <= 0 || b.h <= 0 || typeof b.label !== 'string' || !['solid','platform','hazard','water','ladder','exit','item','enemy','note'].includes(b.type)) throw fail('灰盒图形无效');
    }
  }
  return doc;
}
const revision = text => createHash('sha256').update(text).digest('hex');

// One process owns the data directory. Per-map queues make revision checks and writes atomic.
export function createMVMapStore(dir, { validate = validateMap } = {}) {
  const queues = new Map();
  function file(id) {
    if (!idPattern.test(id || '')) throw fail('地图 ID 无效');
    return path.join(dir, `${id}.json`);
  }
  async function read(id) {
    try {
      const text = await readFile(file(id), 'utf8');
      return { doc: validate(JSON.parse(text), id), revision: revision(text) };
    } catch (e) { if (e.code === 'ENOENT') return { doc: null, revision: null }; throw e; }
  }
  function exclusive(id, fn) {
    const next = (queues.get(id) || Promise.resolve()).catch(() => {}).then(fn);
    queues.set(id, next);
    next.finally(() => { if (queues.get(id) === next) queues.delete(id); }).catch(() => {});
    return next;
  }
  return {
    read,
    async all() {
      const names = await readdir(dir).catch(e => { if (e.code === 'ENOENT') return []; throw e; });
      const result = [];
      for (const name of names) {
        if (!name.endsWith('.json') || !idPattern.test(name.slice(0,-5))) continue;
        result.push(await read(name.slice(0,-5)));
      }
      return result.sort((a,b) => (b.doc.updated || 0) - (a.doc.updated || 0));
    },
    put(id, doc, baseRevision) {
      validate(doc, id);
      return exclusive(id, async () => {
        const old = await read(id);
        if (baseRevision !== old.revision) throw fail('此地图已在其他设备修改，请刷新或另存副本。', 409);
        await mkdir(dir, { recursive: true });
        const text = JSON.stringify(doc, null, 2) + '\n';
        const temp = path.join(dir, `${id}.${randomUUID()}.tmp`);
        try { await writeFile(temp, text, { mode: 0o600 }); await rename(temp, file(id)); }
        finally { await rm(temp, { force: true }); }
        return { revision: revision(text) };
      });
    },
    del(id, baseRevision) {
      return exclusive(id, async () => {
        const old = await read(id);
        if (!old.doc) throw fail('地图不存在', 404);
        if (old.revision !== baseRevision) throw fail('地图已在其他设备修改，请刷新后再删除。', 409);
        const trash = path.join(dir, '.trash');
        await mkdir(trash, { recursive: true });
        await rename(file(id), path.join(trash, `${id}-${randomUUID()}.json`));
      });
    },
  };
}
