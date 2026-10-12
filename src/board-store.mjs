import { mkdir, readdir, readFile, rename, rm, stat, writeFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { parseDocument, serializeDocument } from './model.js';

export const projectRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
export const DEFAULT_BOARDS_DIR = path.join(projectRoot, 'boards');

export function boardsDirPath() {
  return process.env.CANVASROOM_BOARDS_DIR ? path.resolve(process.env.CANVASROOM_BOARDS_DIR) : DEFAULT_BOARDS_DIR;
}

function revisionOf(text) {
  return createHash('sha1').update(text).digest('hex').slice(0, 16);
}

export async function readBoardWithRevision(file) {
  let text;
  try {
    text = await readFile(file, 'utf8');
  } catch (error) {
    if (error.code === 'ENOENT') return { doc: null, revision: null };
    throw error;
  }
  return { doc: parseDocument(text), revision: revisionOf(text) };
}

export async function readBoard(file) {
  return (await readBoardWithRevision(file)).doc;
}

// Writes through a temp file so readers never see a half-written board.
export async function writeBoard(doc, file) {
  const text = serializeDocument(doc);
  await mkdir(path.dirname(file), { recursive: true });
  const temp = `${file}.${process.pid}.${Date.now()}.tmp`;
  await writeFile(temp, text, 'utf8');
  for (let attempt = 0; ; attempt += 1) {
    try {
      await rename(temp, file);
      break;
    } catch (error) {
      // Windows can briefly refuse the replace while another process reads the file.
      if (attempt >= 5 || !['EPERM', 'EBUSY', 'EACCES'].includes(error.code)) {
        await rm(temp, { force: true });
        throw error;
      }
      await new Promise((resolve) => setTimeout(resolve, 40 * (attempt + 1)));
    }
  }
  return revisionOf(text);
}

// Ids double as file names, so they stay short, lowercase, and free of path characters.
const ID_PATTERN = /^[a-z0-9][a-z0-9-]{0,63}$/;

export function assertBoardId(id) {
  if (typeof id !== 'string' || !ID_PATTERN.test(id)) throw Object.assign(new Error(`Invalid board id: ${id}`), { status: 400 });
  return id;
}

function slugify(name) {
  const slug = String(name || '').toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '').slice(0, 48).replace(/-+$/, '');
  return slug || `board-${Date.now().toString(36)}`;
}

export function createBoardStore(dir = boardsDirPath()) {
  const fileFor = (id) => path.join(dir, `${assertBoardId(id)}.json`);

  async function exists(id) {
    return Boolean(await stat(fileFor(id)).catch(() => null));
  }

  async function uniqueId(name) {
    const base = slugify(name);
    for (let n = 1; ; n += 1) {
      const id = n === 1 ? base : `${base.slice(0, 60)}-${n}`;
      if (!(await exists(id))) return id;
    }
  }

  async function list() {
    const names = await readdir(dir).catch((error) => (error.code === 'ENOENT' ? [] : Promise.reject(error)));
    const boards = [];
    for (const fileName of names) {
      const id = fileName.endsWith('.json') ? fileName.slice(0, -5) : null;
      if (!id || !ID_PATTERN.test(id)) continue;
      try {
        const file = path.join(dir, fileName);
        const [{ doc }, info] = await Promise.all([readBoardWithRevision(file), stat(file)]);
        boards.push({ id, name: doc.name, elementCount: doc.elements.length, updatedAt: info.mtime.toISOString() });
      } catch {
        // Skip files that are not valid boards rather than failing the whole list.
      }
    }
    return boards.sort((a, b) => b.updatedAt.localeCompare(a.updatedAt) || a.id.localeCompare(b.id));
  }

  return {
    dir,
    exists,
    list,
    read: async (id) => readBoardWithRevision(fileFor(id)),
    write: async (id, doc) => writeBoard(doc, fileFor(id)),
    async create(doc) {
      const id = await uniqueId(doc.name);
      const revision = await writeBoard(doc, fileFor(id));
      return { id, revision };
    },
    // Deleted boards move to .trash so a mistaken delete can be recovered by hand.
    async remove(id) {
      if (!(await exists(id))) throw Object.assign(new Error(`Board not found: ${id}`), { status: 404 });
      const trash = path.join(dir, '.trash');
      await mkdir(trash, { recursive: true });
      await rename(fileFor(id), path.join(trash, `${id}-${Date.now()}.json`));
    },
    async mostRecent() {
      return (await list())[0]?.id ?? null;
    },
  };
}
