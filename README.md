# DesignBoard

这是一个提供给产品经理/游戏设计师的产品想法头脑风暴的白板应用。你可以放置任意截图、文字等信息，用于产品设计的头脑风暴初期阶段。

DesignBoard (app name: Canvasroom) is a local, single-user FigJam-style whiteboard. It runs in the browser with no build step or npm dependencies; it needs only Node.js 18 or newer.

## Run locally

```powershell
npm run start
```

Open [http://127.0.0.1:4173](http://127.0.0.1:4173).

## NAS / Docker 部署

在 NAS 上安装支持 Compose 的 Docker / Container Manager，把仓库放到 NAS 的项目目录，然后执行：

```sh
docker compose up -d --build
docker compose ps
```

浏览器打开 `http://<NAS 的局域网 IP>:4173`。默认镜像使用 Node 24 Alpine，以非 root 用户运行；基础镜像支持常见的 amd64 和 arm64 NAS。旧款 32 位 NAS 需另行确认兼容性。

默认使用 Docker 命名卷 `board-data` 保存画布，包括删除后的 `.trash`。容器重建和普通 `docker compose down` 会保留数据，**不要使用 `docker compose down -v`，它会删除数据卷**。本地仓库的 `boards/` 不会自动复制到容器，可以通过网页导入 JSON。

修改对外端口，在项目目录创建 `.env`：

```env
DESIGNBOARD_PORT=8080
```

之后重新执行 `docker compose up -d`，通过 NAS 的 8080 端口访问。

如果希望用 NAS 文件夹直接管理和备份数据，把 Compose 中的卷映射改成：

```yaml
    volumes:
      - /你的/NAS/目录/designboard/boards:/data/boards
```

先创建这个目录，并确保容器用户 UID/GID `1000:1000` 有读写权限。命名卷和文件夹映射之间切换不会自动迁移数据。备份时先停止服务，再复制整个数据目录（包括隐藏的 `.trash`）；恢复后启动服务。

更新代码及容器：

```sh
git pull --ff-only
docker compose up -d --build
docker compose logs --tail=100 designboard
```

服务提供 `GET /api/health` 健康检查。Docker 内监听 `0.0.0.0`；本地运行仍默认监听 `127.0.0.1`，可用 `HOST` 修改。

默认 Compose 部署用于可信局域网，访问者共享所有画布，尚无应用内账号和权限隔离。外网部署使用下方的 HTTPS 和认证配置。浏览器的“粘贴”按钮依赖安全上下文，使用 NAS IP 的普通 HTTP 时可改用 Ctrl/Cmd+V 或图片上传；HTTPS 可以支持剪贴板 API。

### 登录主页、白板列表与 HTTPS

首页现在是页面登录表单；登录后进入白板选择界面，可以搜索、刷新和新建白板。选择白板后进入 `editor.html#board=<id>` 编辑，点击“全部白板”会等待保存成功再返回列表。旧首页的 `#board=` 链接仍会打开入口，但不会绕过白板选择步骤。

`compose.secure.yaml` 启动独立 Caddy 网关负责 HTTPS 和证书自动续期。白板后端撤掉宿主机端口，应用自身保护全部画布 API 和编辑器；不再使用浏览器原生 Basic Auth 弹窗。

需要 Compose 2.24.4+，以及包含所用 DNS 提供商模块的 Caddy 镜像。当前 NAS 使用 `lighttab-caddy-alidns:local`，通过阿里云 DNS 验证，无需额外开放公网 80/443。

`.env` 示例（不包含密码）：

```env
DESIGNBOARD_DOMAIN=evantra.cloud
DESIGNBOARD_PORT=4173
DESIGNBOARD_CADDY_IMAGE=lighttab-caddy-alidns:local
```

创建权限为 700 的 `secure/` 目录，放入两个不提交 Git 的受限文件：

- `dns.caddy`：DNS 验证模块配置，比如 `dns alidns { ... }`，仅供网关读取。
- `auth.json`：应用账号配置，格式为 `{"username":"admin","salt":"<64 位十六进制随机盐>","passwordHash":"<128 位十六进制 scrypt 哈希>"}`。使用 Node 的 `scryptSync(password, salt, 64)` 生成哈希，不保存明文密码。此文件应归 UID/GID `1000:1000` 所有、权限 600，以便非 root 的白板进程读取。

这些文件、`.env` 和 NAS 使用的 `compose.override.yaml` 均排除在 Git 和镜像构建上下文之外。安全配置要求认证文件存在且有效，缺少配置会停止启动，不会开放无认证访问。

安装后启用自动覆盖，确保普通 Compose 更新仍保留保护：

```sh
cp compose.secure.yaml compose.override.yaml
docker compose config --quiet
docker compose up -d --build
```

飞牛 NAS 的 `admin` 用户若无 Docker 权限，以上 Docker 命令需加 `sudo`。打开 `https://evantra.cloud:4173/`，填写白板账号和专用密码。旧 `http://` 地址不再提供白板。

会话使用随机令牌和 `HttpOnly; Secure; SameSite=Strict` Cookie，不存入浏览器 localStorage；会话最长 12 小时，关闭浏览器会话或重启后端后需要重新登录。退出登录立即撤销当前会话。连续五次错误登录会限制该来源 15 分钟。单账号登录者仍共享全部画布，没有按用户划分的画布权限。

会话过期时，白板会显示保存错误并保留当前编辑内容，可以先导出 JSON，再回首页重新登录，不会自动切换成浏览器本地保存。

更新继续使用 `docker compose up -d --build`，不要删除覆盖文件或持久化卷。修改账号哈希后，重新创建后端以读取新配置并撤销旧会话：

```sh
docker compose up -d --force-recreate --no-deps designboard
```

MCP 目前使用 stdio，不能直接通过 NAS 的 HTTP 地址连接。需要在 NAS 上运行 MCP 进程并让 `CANVASROOM_BOARDS_DIR` 指向同一数据目录；远程 MCP 接入属于后续开发。

项目结构和后续方向见 [开发说明](docs/development.md)。

## Boards and file sync

Each board is one JSON file in the `boards/` folder, for example `boards/sprint-retro.json`. The file name comes from the board name when it is created and does not change when you rename the board.

The menu next to the board name lists every board with its file name, element count, and last change, and lets you create a blank board, duplicate the current one, or delete it. Deleted boards are moved to `boards/.trash` so they can be recovered by hand. Importing JSON files adds each one as a new board (several files can be picked at once). The open board is remembered and also appears in the address bar as `#board=<id>`, so a link can open a specific board.

The server keeps the open board in sync with its file:

- Edits in the browser are written to the file about half a second after you stop.
- Changes made to the file by something else (the MCP server, or a hand edit) show up in the open tab within about two seconds.
- If the file changed while you were editing, the file wins and your previous state goes onto the undo stack.
- The viewport (pan and zoom) in an open tab is kept when the file changes.

Set `PORT` to change the port and `CANVASROOM_BOARDS_DIR` to use a different boards folder. If the page is opened without this server, it falls back to a single board stored in the browser.

## MCP server

`mcp-server.mjs` is a stdio MCP server that reads and edits the same board files, so an agent can work on a board while it is open in the browser. Every tool except `list_boards` and `create_board` takes an optional `board` id; without it, the tool acts on the most recently changed board.

| Tool | What it does |
| --- | --- |
| `list_boards` | Lists every board with id, name, element count, and last change |
| `create_board` | Creates a board, optionally with starting elements |
| `get_board` | Lists every element with id, type, position, size, text, and color |
| `add_elements` | Adds stickies, text, shapes, frames, lines, arrows, and connectors |
| `update_elements` | Moves, resizes, recolors, edits text, locks or unlocks by id |
| `delete_elements` | Deletes by id; locked elements are kept |
| `rename_board` | Renames the board |

Register it in Codex by adding this to `config.toml` in your Codex home, then restart Codex:

```toml
[mcp_servers.canvasroom]
command = "node"
args = ['/absolute/path/to/DesignBoard/mcp-server.mjs']
```

The MCP server does not need the web server to be running; it works on the files directly.

## Included in v1

- Infinite-feeling canvas with pan, zoom, fit-to-screen, and a grid
- Sticky notes, text, rectangles/circles/diamonds, frames, lines, connectors, freehand paths, and images
- Selection, multi-selection, marquee selection, move, resize, rotate, delete, grouping, layer order, and lock
- Fill, border, opacity, text size, weight, and alignment controls
- Undo/redo and keyboard shortcuts
- Clipboard: paste screenshots, image files, and text with Ctrl/Cmd+V or the paste button; copy, cut, and paste board elements (including between tabs); Ctrl/Cmd+D duplicates the selection
- Debounced persistence to the browser and to `boards/board.json`
- JSON import/export and PNG export

This version intentionally excludes accounts, cloud sync, multiplayer collaboration, comments, permissions, version history, voting, timers, AI, plugins, and third-party integrations.

## Verify

```powershell
npm test
```

The tests cover the document model, board operations, the board file store, the sync API (including stale-write rejection), and the MCP server.

## MVMap 地图编辑与 NAS 同步

登录后可从首页进入 `/mvmap/` 使用中文地图编辑器。地图以独立 JSON 文件保存在现有数据卷的 `mvmap/` 子目录，支持自动保存、跨设备读取、刷新、版本冲突保护、本地备份恢复与删除回收站。现有 Docker 部署更新镜像即可启用，无需增加端口或数据库。详见 [MVMap NAS 部署说明](docs/mvmap-nas部署.md)。

## LennethMap 横版纵深迷宫编辑器

首页新增 `/lenneth-map/`，用于设计《北欧女神：蕾娜斯》式横版房间、前后景通路和机关条件。支持显式六向出口、AND/OR 条件、单向落下、4× 室内灰盒、可达性预览、JSON/PNG 和 NAS 同步。内置地图为原创结构示例，不是原作关卡复刻。章节和难度仅为元数据；不模拟角色物理和旋转机关。

[调研与完整说明](prototypes/lenneth-map/manual.html)。数据存放在现有数据卷的 `lennethmap/` 子目录，复用既有登录和并发冲突保护，与白板及 MVMap 分开。
