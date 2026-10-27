/* NAS adapter: the editor still works as a standalone offline HTML directory. */
window.createMVMapNASStore = function (local) {
  let remote = false;
  let queue = Promise.resolve();
  const revisions = new Map();
  const listed = new WeakMap();
  let failed = false;
  const notice = message => {
    document.querySelector('#sync-notice').textContent = message;
  };
  async function request(url, options = {}) {
    const r = await fetch(url, { cache: 'no-store', ...options });
    if (!r.ok) {
      const data = await r.json().catch(() => ({}));
      throw Object.assign(new Error(r.status === 401 ? '登录已过期，请在首页重新登录后点击重试。' : data.error || `NAS 请求失败 (${r.status})`), { status: r.status });
    }
    return r.json();
  }
  return {
    get mode() { return remote ? 'nas' : local.mode; },
    get failed() { return failed; },
    async init() {
      await local.init();
      // Only a server-served editor opts in; file:// and arbitrary static hosts stay offline.
      if (location.pathname !== '/mvmap/' && location.pathname !== '/mvmap') return;
      remote = true;
      document.querySelector('#btn-sync').hidden = false;
      document.querySelector('#btn-retry').hidden = false;
      document.querySelector('#btn-copy').hidden = false;
      notice('NAS 同步：自动保存到服务器；其他设备编辑后，点击「刷新 NAS」获取。');
    },
    accept(doc) {
      if (listed.has(doc)) { revisions.set(doc.id, listed.get(doc)); failed = false; }
    },
    async all() {
      if (!remote) return local.all();
      const { maps } = await request('/api/mvmaps');
      return maps.map(({ doc, revision }) => {
        listed.set(doc, revision);
        if (!revisions.has(doc.id)) revisions.set(doc.id, revision);
        return doc;
      });
    },
    put(doc) {
      const copy = JSON.parse(JSON.stringify(doc));
      // Serialize saves so an older slow request cannot overwrite a later edit.
      const next = queue.catch(() => {}).then(async () => {
        await local.put(copy);
        if (!remote) return;
        try {
          const result = await request(`/api/mvmaps/${encodeURIComponent(copy.id)}`, {
            method: 'PUT', headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ doc: copy, baseRevision: revisions.get(copy.id) || null }),
          });
          revisions.set(copy.id, result.revision);
          failed = false;
          notice('已同步到 NAS。其他设备可从地图库打开，或点击「刷新 NAS」。');
        } catch (e) {
          failed = true;
          notice(e.status === 409 ? '版本冲突，未覆盖 NAS。请导出本地 JSON，或点击「另存副本」；刷新 NAS 会读取服务器版本。' : `尚未同步：${e.message} 本地副本已保留，请重试或导出 JSON。`);
          throw e;
        }
      });
      queue = next;
      return next;
    },
    async del(id) {
      await queue.catch(() => {});
      if (!remote) return local.del(id);
      await request(`/api/mvmaps/${encodeURIComponent(id)}`, { method: 'DELETE', headers: { 'If-Match': revisions.get(id) || '' } });
      revisions.delete(id);
      failed = false;
      await local.del(id);
    },
    async flush() { await queue.catch(() => {}); },
    notice,
  };
};
