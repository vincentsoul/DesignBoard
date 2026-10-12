// Minimal stdio MCP server (newline-delimited JSON-RPC 2.0) for reading and editing board files.
import path from 'node:path';
import readline from 'node:readline';
import { pathToFileURL } from 'node:url';
import { createDocument } from './src/model.js';
import { addElements, deleteElements, summarizeBoard, updateElements } from './src/board-ops.mjs';
import { createBoardStore } from './src/board-store.mjs';

const ELEMENT_FIELDS = {
  text: { type: 'string', description: 'Text for sticky/text/shape; title for frame.' },
  x: { type: 'number' },
  y: { type: 'number' },
  width: { type: 'number' },
  height: { type: 'number' },
  rotation: { type: 'number', description: 'Degrees.' },
  color: { type: 'string', description: 'Hex color. Fill for sticky/shape/frame, text color for text, stroke for lines.' },
  fontSize: { type: 'number' },
  shape: { type: 'string', enum: ['rectangle', 'rounded', 'circle', 'diamond'] },
  locked: { type: 'boolean' },
};

const BOARD_FIELD = { board: { type: 'string', description: 'Board id from list_boards. Defaults to the most recently changed board.' } };

const NEW_ELEMENTS = {
  type: 'array',
  minItems: 1,
  items: {
    type: 'object',
    required: ['type'],
    properties: {
      type: { type: 'string', enum: ['sticky', 'text', 'shape', 'frame', 'line', 'arrow', 'connector'] },
      key: { type: 'string', description: 'Temporary name for referencing this element from a connector in the same call.' },
      title: { type: 'string', description: 'Frame title.' },
      endX: { type: 'number' },
      endY: { type: 'number' },
      from: { type: 'string', description: 'Connector start: element id or key.' },
      to: { type: 'string', description: 'Connector end: element id or key.' },
      ...ELEMENT_FIELDS,
    },
  },
};

const TOOLS = [
  {
    name: 'list_boards',
    description: 'List every Canvasroom whiteboard with its id, name, element count, and last change time, most recently changed first.',
    inputSchema: { type: 'object', properties: {} },
  },
  {
    name: 'create_board',
    description: 'Create a new whiteboard, optionally with starting elements (same format as add_elements). Returns the new board id.',
    inputSchema: { type: 'object', required: ['name'], properties: { name: { type: 'string', minLength: 1 }, elements: NEW_ELEMENTS } },
  },
  {
    name: 'get_board',
    description: 'Read a whiteboard: name and every element with id, type, position, size, text, color, and connector endpoints. Image data is omitted.',
    inputSchema: { type: 'object', properties: { ...BOARD_FIELD } },
  },
  {
    name: 'add_elements',
    description: 'Add elements to a whiteboard. Omit x/y to auto-place new items to the right of existing content. Give an element a "key" so a connector in the same call can reference it in from/to; from/to also accept existing element ids. Lines and arrows need x, y, endX, endY.',
    inputSchema: { type: 'object', required: ['elements'], properties: { ...BOARD_FIELD, elements: NEW_ELEMENTS } },
  },
  {
    name: 'update_elements',
    description: 'Change existing elements by id: move, resize, rotate, recolor, edit text, change shape, or lock/unlock. Locked elements only accept unlocking. Attached connectors re-route automatically.',
    inputSchema: {
      type: 'object',
      required: ['updates'],
      properties: {
        ...BOARD_FIELD,
        updates: {
          type: 'array',
          minItems: 1,
          items: { type: 'object', required: ['id'], properties: { id: { type: 'string' }, title: { type: 'string' }, ...ELEMENT_FIELDS } },
        },
      },
    },
  },
  {
    name: 'delete_elements',
    description: 'Delete elements by id. Locked elements are kept. Connectors attached to deleted elements are removed too.',
    inputSchema: { type: 'object', required: ['ids'], properties: { ...BOARD_FIELD, ids: { type: 'array', minItems: 1, items: { type: 'string' } } } },
  },
  {
    name: 'rename_board',
    description: 'Rename a whiteboard. Its id stays the same.',
    inputSchema: { type: 'object', required: ['name'], properties: { ...BOARD_FIELD, name: { type: 'string', minLength: 1 } } },
  },
];

function requireName(name) {
  if (typeof name !== 'string' || !name.trim()) throw new Error('name must be a non-empty string');
  return name.trim();
}

async function openBoard(store, board) {
  const id = board ?? (await store.mostRecent());
  if (!id) throw new Error('No boards exist yet. Create one with create_board.');
  const { doc } = await store.read(id);
  if (!doc) throw new Error(`Board not found: ${id}. Use list_boards to see available boards.`);
  return { id, doc };
}

async function editBoard(board, edit) {
  const store = createBoardStore();
  const { id, doc } = await openBoard(store, board);
  const result = edit(doc);
  await store.write(id, doc);
  return { board: id, ...result };
}

const describeAdded = (elements) => elements.map((item) => ({ id: item.id, type: item.type, x: Math.round(item.x), y: Math.round(item.y) }));

const HANDLERS = {
  list_boards: async () => {
    const store = createBoardStore();
    return { folder: store.dir, boards: await store.list() };
  },
  create_board: async ({ name, elements }) => {
    const doc = createDocument(requireName(name));
    const added = elements?.length ? describeAdded(addElements(doc, elements)) : [];
    const { id } = await createBoardStore().create(doc);
    return { board: id, name: doc.name, added };
  },
  get_board: async ({ board }) => {
    const { id, doc } = await openBoard(createBoardStore(), board);
    return { board: id, ...summarizeBoard(doc) };
  },
  add_elements: ({ board, elements }) => editBoard(board, (doc) => ({ added: describeAdded(addElements(doc, elements)) })),
  update_elements: ({ board, updates }) => editBoard(board, (doc) => updateElements(doc, updates)),
  delete_elements: ({ board, ids }) => editBoard(board, (doc) => deleteElements(doc, ids)),
  rename_board: ({ board, name }) => editBoard(board, (doc) => {
    doc.name = requireName(name);
    return { name: doc.name };
  }),
};

async function callTool(params = {}) {
  const handler = HANDLERS[params.name];
  if (!handler) return { content: [{ type: 'text', text: `Unknown tool: ${params.name}` }], isError: true };
  try {
    const result = await handler(params.arguments || {});
    return { content: [{ type: 'text', text: JSON.stringify(result, null, 2) }] };
  } catch (error) {
    return { content: [{ type: 'text', text: error.message }], isError: true };
  }
}

export async function handleMessage(message) {
  const { id, method, params } = message || {};
  if (id === undefined || id === null) return null;
  const reply = (result) => ({ jsonrpc: '2.0', id, result });
  switch (method) {
    case 'initialize':
      return reply({
        protocolVersion: params?.protocolVersion || '2025-06-18',
        capabilities: { tools: {} },
        serverInfo: { name: 'canvasroom', version: '1.0.0' },
        instructions: 'Tools for local Canvasroom whiteboards. Use list_boards to see boards, then pass a board id to the other tools; without one they act on the most recently changed board. Call get_board to see element ids. Changes appear in an open browser tab within a couple of seconds.',
      });
    case 'ping':
      return reply({});
    case 'tools/list':
      return reply({ tools: TOOLS });
    case 'tools/call':
      return reply(await callTool(params));
    default:
      return { jsonrpc: '2.0', id, error: { code: -32601, message: `Method not found: ${method}` } };
  }
}

if (process.argv[1] && pathToFileURL(path.resolve(process.argv[1])).href === import.meta.url) {
  let queue = Promise.resolve();
  const lines = readline.createInterface({ input: process.stdin });
  lines.on('line', (line) => {
    if (!line.trim()) return;
    // Handle requests one at a time so concurrent edits never race on the board file.
    queue = queue.then(async () => {
      let message;
      try {
        message = JSON.parse(line);
      } catch {
        process.stdout.write(`${JSON.stringify({ jsonrpc: '2.0', id: null, error: { code: -32700, message: 'Parse error' } })}\n`);
        return;
      }
      const response = await handleMessage(message);
      if (response) process.stdout.write(`${JSON.stringify(response)}\n`);
    });
  });
}
