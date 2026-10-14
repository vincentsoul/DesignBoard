const $ = (selector) => document.querySelector(selector);
let boards = [];
let authEnabled = true;
let loading = false;
async function api(path, method = 'GET', body) {
  const response = await fetch(path, { method, cache: 'no-store', headers: body ? { 'content-type': 'application/json' } : undefined, body: body ? JSON.stringify(body) : undefined });
  const data = await response.json();
  if (!response.ok) {
    if (response.status === 401 && path !== '/api/auth/login') showLogin('登录已过期，请重新登录');
    throw new Error(data.error || '请求失败，请稍后重试');
  }
  return data;
}
function showLogin(message = '') {
  $('#boot-status').hidden = true;
  $('#login-view').hidden = false;
  $('#boards-view').hidden = true;
  $('#account').hidden = true;
  $('#login-error').textContent = message;
  $('#password').value = '';
  $('#password').type = 'password';
  $('#toggle-password').textContent = '显示';
  $('#toggle-password').setAttribute('aria-label', '显示密码');
  $('#toggle-password').setAttribute('aria-pressed', 'false');
  boards = [];
  $('#board-grid').replaceChildren();
  if ($('#create-dialog').open) $('#create-dialog').close();
}
function showBoards(username) {
  $('#boot-status').hidden = true;
  $('#login-view').hidden = true;
  $('#boards-view').hidden = false;
  $('#account').hidden = !authEnabled;
  $('#account-name').textContent = username || '';
  $('#password').value = '';
}
function renderBoards() {
  const query = $('#board-search').value.trim().toLocaleLowerCase();
  const visible = boards.filter((board) => board.name.toLocaleLowerCase().includes(query));
  $('#board-count').textContent = boards.length;
  $('#board-grid').replaceChildren(...visible.map((board) => {
    const link = document.createElement('a');
    link.className = 'board-card';
    link.href = `/editor.html#board=${encodeURIComponent(board.id)}`;
    link.setAttribute('aria-label', `打开白板：${board.name}`);
    const preview = document.createElement('div'); preview.className = 'board-preview'; preview.setAttribute('aria-hidden', 'true');
    for (let i = 0; i < 3; i++) preview.append(document.createElement('i'));
    const details = document.createElement('div'); details.className = 'board-details';
    const title = document.createElement('h2'); title.textContent = board.name; title.title = board.name;
    const meta = document.createElement('div'); meta.className = 'board-meta';
    const count = document.createElement('span'); count.textContent = `${board.elementCount} 个元素`;
    const updated = document.createElement('span'); updated.textContent = `更新于 ${new Date(board.updatedAt).toLocaleDateString('zh-CN', { month: 'short', day: 'numeric' })}`;
    meta.append(count, updated);
    const open = document.createElement('div'); open.className = 'open-label'; open.append('继续编辑');
    const arrow = document.createElement('span'); arrow.textContent = '↗'; open.append(arrow);
    details.append(title, meta, open); link.append(preview, details); return link;
  }));
  $('#empty-state').hidden = visible.length > 0;
  $('#empty-title').textContent = query ? '没有找到这张白板' : '第一张白板，从这里开始';
  $('#empty-copy').textContent = query ? '换一个名称试试，或创建一张新白板。' : '点击“新建白板”，记录你的第一个想法。';
}
async function refresh() {
  if (loading) return;
  loading = true; $('#refresh-button').disabled = true;
  $('#boards-error').textContent = ''; $('#list-status').textContent = '正在读取白板…';
  try { boards = (await api('/api/boards')).boards; renderBoards(); }
  catch (error) { $('#boards-error').textContent = error.message; }
  finally { loading = false; $('#refresh-button').disabled = false; $('#list-status').textContent = ''; }
}
$('#login-form').addEventListener('submit', async (event) => {
  event.preventDefault(); const button = $('#login-submit'); button.disabled = true; button.textContent = '正在登录…'; $('#login-error').textContent = '';
  try {
    const data = await api('/api/auth/login', 'POST', { username: $('#username').value.trim(), password: $('#password').value });
    showBoards(data.username); await refresh();
  } catch (error) { $('#login-error').textContent = error.message; }
  finally { button.disabled = false; button.textContent = '进入我的空间 →'; }
});
$('#toggle-password').addEventListener('click', () => {
  const visible = $('#password').type === 'password'; $('#password').type = visible ? 'text' : 'password'; $('#toggle-password').textContent = visible ? '隐藏' : '显示'; $('#toggle-password').setAttribute('aria-label', visible ? '隐藏密码' : '显示密码'); $('#toggle-password').setAttribute('aria-pressed', String(visible));
});
$('#logout-button').addEventListener('click', async () => {
  const button = $('#logout-button'); button.disabled = true;
  try { await api('/api/auth/logout', 'POST', {}); showLogin(); }
  catch (error) { $('#boards-error').textContent = error.message; }
  finally { button.disabled = false; }
});
$('#board-search').addEventListener('input', renderBoards);
$('#refresh-button').addEventListener('click', refresh);
$('#new-board-button').addEventListener('click', () => { $('#new-board-name').value = ''; $('#create-error').textContent = ''; $('#create-dialog').showModal(); $('#new-board-name').focus(); });
$('#close-create').addEventListener('click', () => $('#create-dialog').close());
$('#create-form').addEventListener('submit', async (event) => {
  event.preventDefault(); const name = $('#new-board-name').value.trim(); if (!name) { $('#create-error').textContent = '请输入白板名称'; return; }
  const button = $('#create-submit'); button.disabled = true; $('#create-error').textContent = '';
  try { const board = await api('/api/boards', 'POST', { name }); location.assign(`/editor.html#board=${encodeURIComponent(board.id)}`); }
  catch (error) { $('#create-error').textContent = error.message; button.disabled = false; }
});
async function boot() {
  try { const session = await api('/api/auth/session'); authEnabled = session.enabled; if (!authEnabled || session.username) { showBoards(session.username); await refresh(); } else showLogin(); }
  catch { showLogin('连接服务失败，请刷新页面重试'); }
}
window.addEventListener('pageshow', (event) => { if (event.persisted) boot(); });
boot();
