# FigJam Core Whiteboard Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a zero-dependency, local single-user whiteboard with core FigJam creation, editing, persistence, import, and export workflows.

**Architecture:** Use a browser ES-module app with a serializable document model in `src/model.js`, DOM/SVG rendering and pointer interactions in `src/board.js`, and a focused UI shell in `index.html` plus `styles.css`. HTML elements render editable board objects; one SVG layer renders connectors and freehand paths. Local storage, JSON, and PNG export are implemented as model-adjacent utilities so they can be tested independently.

**Tech Stack:** HTML, CSS, vanilla JavaScript ES modules, browser APIs (`localStorage`, `FileReader`, `Blob`, `Canvas`, Pointer Events), Node's built-in test runner for pure model tests.

---

### Task 1: Add the test harness and document model tests

**Files:**
- Create: `package.json`
- Create: `tests/model.test.mjs`
- Create: `src/model.js`

- [ ] **Step 1: Write the failing model tests**

Create tests for default document shape, element creation, document validation, and JSON round-trip. The tests import `src/model.js` functions before they exist.

```js
import test from 'node:test';
import assert from 'node:assert/strict';
import { createDocument, createElement, serializeDocument, parseDocument } from '../src/model.js';

test('creates an empty document with a centered viewport', () => {
  const doc = createDocument('Workshop board');
  assert.equal(doc.version, 1);
  assert.equal(doc.name, 'Workshop board');
  assert.deepEqual(doc.viewport, { x: 0, y: 0, zoom: 1 });
  assert.deepEqual(doc.elements, []);
});

test('creates stable ids and serializable element defaults', () => {
  const sticky = createElement('sticky', { x: 40, y: 60, data: { text: 'Idea' } });
  assert.match(sticky.id, /^el_/);
  assert.equal(sticky.type, 'sticky');
  assert.equal(sticky.x, 40);
  assert.equal(sticky.data.text, 'Idea');
  assert.equal(sticky.locked, false);
});

test('round trips a valid document through JSON', () => {
  const doc = createDocument();
  doc.elements.push(createElement('shape', { data: { shape: 'circle' } }));
  assert.deepEqual(parseDocument(serializeDocument(doc)), doc);
});

test('rejects invalid documents without mutating input', () => {
  assert.throws(() => parseDocument('{"version":2,"elements":[]}'), /Unsupported document version/);
  assert.throws(() => parseDocument('{"version":1,"elements":"bad"}'), /elements must be an array/);
});
```

- [ ] **Step 2: Run the model test to verify it fails**

Run `npm test`. Expected: FAIL because `src/model.js` is not defined.

- [ ] **Step 3: Implement the minimal model module**

Implement `createDocument`, `createElement`, `serializeDocument`, and `parseDocument`; use `crypto.randomUUID()` when available and a timestamp fallback for ids. Keep element defaults deterministic by type.

- [ ] **Step 4: Run the model test to verify it passes**

Run `npm test`. Expected: all four tests pass with zero failures.

### Task 2: Build the board shell and visual system

**Files:**
- Create: `index.html`
- Create: `styles.css`
- Create: `src/icons.js`

- [ ] **Step 1: Add the semantic application shell**

Create a top bar, left tool rail, central board viewport, SVG overlay, world layer, right inspector, hidden file inputs, toast region, and mobile inspector toggle. Keep all controls accessible with labels and tooltips.

- [ ] **Step 2: Add the visual system**

Use a warm paper canvas, dark ink UI, coral active accent, pale mint and yellow object colors, Fraunces for display text and DM Sans for controls. Add a subtle grid, compact 8px-radius surfaces, visible keyboard focus rings, and responsive rules that collapse the inspector below 900px.

- [ ] **Step 3: Add icon helpers**

Create a small inline SVG icon map for select, sticky, text, shape, connector, pen, frame, image, undo, redo, zoom, fit, import, export, group, lock, and delete. Buttons use icons plus accessible labels.

### Task 3: Implement rendering and core pointer interaction

**Files:**
- Create: `src/board.js`
- Modify: `index.html`

- [ ] **Step 1: Add render state and coordinate transforms**

Maintain `document`, `selection`, `activeTool`, `viewport`, and transient drag state. Convert screen coordinates to world coordinates and apply `translate(x y) scale(zoom)` to the world layer and SVG overlay.

- [ ] **Step 2: Render document elements**

Render stickies, text, shapes, frames, and images as positioned DOM nodes. Render line, connector, and path elements in SVG. Add selected outlines and handles without changing layout dimensions.

- [ ] **Step 3: Implement tool creation**

Clicking the canvas with sticky, text, shape, connector, pen, or frame creates the corresponding element. Image tool opens the file picker and stores an image Data URL.

- [ ] **Step 4: Implement selection and movement**

Support click selection, Shift multi-selection, marquee selection, pointer drag movement, Delete/Backspace, Escape, and space-drag panning. Ignore locked elements in mutation handlers.

- [ ] **Step 5: Implement resize, rotate, and connector endpoints**

Add selection handles for one element, keep minimum dimensions, update rotation from the corner handle, and snap connector endpoints to nearby board element bounds.

### Task 4: Add inspector controls, grouping, and history

**Files:**
- Modify: `src/board.js`
- Modify: `index.html`
- Modify: `styles.css`

- [ ] **Step 1: Add inspector bindings**

Show common style controls for the selection: fill swatches, border swatches, opacity, text size, bold, alignment, and shape variant. Apply edits through a single `mutateDocument` path.

- [ ] **Step 2: Add layer and organization actions**

Implement bring forward, send backward, bring to front, send to back, group, ungroup, lock, and unlock. Group membership is stored in `data.groupId` while preserving the flat element list.

- [ ] **Step 3: Add undo and redo**

Record immutable document snapshots before every user mutation, cap history at 80 entries, and wire top-bar buttons plus Ctrl/Cmd+Z and Ctrl/Cmd+Shift+Z.

### Task 5: Add persistence and file export/import

**Files:**
- Create: `src/io.js`
- Modify: `src/board.js`
- Modify: `index.html`

- [ ] **Step 1: Add failing IO tests**

Extend `tests/model.test.mjs` with a test that serializes a document containing a Data URL and verifies the parsed value is unchanged.

- [ ] **Step 2: Run tests to verify the new test fails for the missing IO boundary**

Run `npm test`; expected failure identifies the not-yet-exported IO helper.

- [ ] **Step 3: Implement local persistence and JSON IO**

Create `saveLocal`, `loadLocal`, `downloadJson`, `readJsonFile`, and `downloadPng` helpers. Debounce local saves by 500ms and show a saved/unsaved indicator. Invalid imports must leave the current document untouched.

- [ ] **Step 4: Implement PNG export**

Use an offscreen canvas to draw the board bounding box with a grid-free paper background, shapes, text, stickies, paths, and images. Download the result as `board.png`.

- [ ] **Step 5: Run all tests**

Run `npm test`. Expected: all model and IO tests pass.

### Task 6: Verify the user workflow and responsive layout

**Files:**
- Modify: `src/board.js`
- Modify: `styles.css`
- Create: `README.md`

- [ ] **Step 1: Add a local launch command**

Define `npm run start` using the available local static server command and document it in `README.md`.

- [ ] **Step 2: Exercise the primary workflow in a browser**

Launch the app and verify: create sticky/text/shape/line/path/frame, select and move, style, lock/unlock, group/ungroup, undo/redo, refresh restore, JSON export/import, and PNG download.

- [ ] **Step 3: Exercise the narrow viewport**

Check 390px width for toolbar overflow, canvas reachability, inspector bottom sheet behavior, readable labels, and no overlapping controls.

- [ ] **Step 4: Run final verification**

Run `npm test` and inspect the browser console for errors while repeating the core workflow. Report any unsupported collaboration features explicitly.
