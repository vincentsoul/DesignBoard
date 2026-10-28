// Talks to the local server: the board list, and keeping the open board in sync with its file.
const POLL_MS = 1500;

async function request(method, url, body) {
  const response = await fetch(url, {
    method,
    cache: 'no-store',
    headers: body ? { 'content-type': 'application/json' } : undefined,
    body: body ? JSON.stringify(body) : undefined,
    signal: method === 'PUT' ? AbortSignal.timeout(30000) : undefined,
  });
  const data = await response.json().catch(() => ({}));
  if (response.status === 401) {
    const error = new Error('登录已过期，请返回首页重新登录；未保存内容可先导出 JSON');
    error.status = 401;
    throw error;
  }
  return { status: response.status, data };
}

function boardUrl(id) {
  return `/api/boards/${encodeURIComponent(id)}`;
}

async function expectStatus(status, promise) {
  const result = await promise;
  if (result.status !== status) throw new Error(result.data?.error || '本地服务出错');
  return result.data;
}

export const boardsApi = {
  async list() {
    return (await expectStatus(200, request('GET', '/api/boards'))).boards;
  },
  // Pass a name for a blank board, a doc to save a copy of the current board, or a sourceId to duplicate.
  async create(body) {
    return expectStatus(201, request('POST', '/api/boards', body));
  },
  async remove(id) {
    return expectStatus(200, request('DELETE', boardUrl(id)));
  },
};

export function createFileSync({ getDoc, isBusy, onRemoteChange, onStatus, onMissing }) {
  let boardId = null;
  let revision = null;
  let enabled = false;
  let pending = false;
  let inFlight = null;
  let unsaved = false;

  // False when the page is served without the sync server; the app then stays browser-only.
  async function probe() {
    try {
      enabled = (await request('GET', '/api/boards')).status === 200;
    } catch (error) {
      if (error.status === 401) throw error;
      enabled = false;
    }
    return enabled;
  }

  async function open(id) {
    await flush();
    const { status, data } = await request('GET', boardUrl(id));
    if (status !== 200 || !data.doc) throw new Error(data?.error || `Board not found: ${id}`);
    boardId = id;
    revision = data.revision;
    unsaved = false;
    return data.doc;
  }

  async function push() {
    if (!enabled || !boardId) return;
    if (inFlight) { pending = true; return; }
    const id = boardId;
    unsaved = true;
    inFlight = (async () => {
      try {
        const { status, data } = await request('PUT', boardUrl(id), { baseRevision: revision, doc: getDoc() });
        if (id !== boardId) return;
        if (status === 200) {
          revision = data.revision;
          unsaved = false;
          onStatus('saved');
        } else if (status === 409) {
          if (data.doc) {
            // The file changed underneath us (usually an MCP edit): adopt it.
            revision = data.revision;
            onRemoteChange(data.doc, true);
            unsaved = false;
          } else onMissing(id);
        } else {
          onStatus('error', data.error);
        }
      } catch (error) {
        onStatus(error.status === 401 || error.name === 'TimeoutError' ? 'error' : 'offline', error.name === 'TimeoutError' ? '保存超时，请重试或先导出 JSON 备份' : error.message);
      }
    })();
    await inFlight;
    inFlight = null;
    if (pending) { pending = false; await push(); }
  }

  async function flush() {
    while (inFlight) await inFlight;
  }

  async function saveBeforeLeave(hasPendingEdits) {
    // A synced image-heavy board must not be uploaded again just to navigate.
    // Wait for autosave first, and retain failed writes for an explicit retry.
    await flush();
    if (hasPendingEdits || unsaved) await push();
    await flush();
    return !unsaved;
  }

  async function poll() {
    if (!enabled || !boardId || inFlight || isBusy()) return;
    const id = boardId;
    try {
      const { status, data } = await request('GET', boardUrl(id));
      if (id !== boardId || inFlight) return;
      if (status === 404) return onMissing(id);
      if (status === 200 && data.revision !== revision) {
        revision = data.revision;
        if (data.doc) onRemoteChange(data.doc, false);
      }
    } catch (error) {
      onStatus(error.status === 401 ? 'error' : 'offline', error.message);
    }
  }

  function start() {
    setInterval(poll, POLL_MS);
    document.addEventListener('visibilitychange', () => { if (!document.hidden) poll(); });
  }

  return {
    probe,
    open,
    push,
    flush,
    saveBeforeLeave,
    start,
    get enabled() { return enabled; },
    get boardId() { return boardId; },
  };
}
