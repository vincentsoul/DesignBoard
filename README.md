# DesignBoard

这是一个提供给产品经理/游戏设计师的产品想法头脑风暴的白板应用。你可以放置任意截图、文字等信息，用于产品设计的头脑风暴初期阶段。

DesignBoard (app name: Canvasroom) is a local, single-user FigJam-style whiteboard. It runs in the browser with no build step or npm dependencies; it needs only Node.js 18 or newer.

## Run locally

```powershell
npm run start
```

Open [http://127.0.0.1:4173](http://127.0.0.1:4173).

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
