# FigJam Core Whiteboard v1 Design Spec

## Goal

Build a local, single-user whiteboard that covers FigJam's core spatial organization and content creation workflow. The first version validates creating, arranging, saving, and exporting board content without accounts, cloud sync, or team collaboration.

## Scope

### Included

- Infinite canvas with pan, zoom, and fit-to-screen
- Select, multi-select, marquee select, move, resize, rotate, and delete
- Sticky notes, text, rectangles, circles, lines, arrows, and connectors
- Freehand drawing and eraser
- Image upload
- Fill, border, text styling, and opacity
- Layer order, grouping, ungrouping, and locking
- Frame / Section regions
- Undo, redo, and common keyboard shortcuts
- Browser-local persistence
- JSON import, JSON export, and PNG export

### Excluded from v1

Login, cloud sync, multiplayer editing, live cursors, comments, mentions, share permissions, team spaces, notifications, version history, audio/video, voting, timer, AI, plugins, and third-party integrations.

## Approach

### DOM elements plus an SVG connection layer (chosen)

HTML elements render sticky notes, text, shapes, and images. SVG renders connectors, arrows, and freehand paths. A transformed world container provides pan and zoom.

This keeps text editing, images, accessibility, events, JSON persistence, and local development simple. It is sufficient for a single-user MVP. Hundreds of elements may eventually need a dedicated canvas engine, but complex boolean geometry and high-fidelity brush behavior are outside v1.

Alternative approaches were considered: a single Canvas renderer would improve raw rendering performance but make text editing, accessibility, selection, and export more expensive; an existing whiteboard engine would accelerate interactions but add dependency and build constraints to this empty project.

## Information architecture

1. Top bar: document name, save state, undo/redo, import/export, and zoom state.
2. Left toolbar: select, sticky, text, shape, connector, pen, frame, and image.
3. Central canvas: pannable and zoomable world space.
4. Right inspector: style, text, opacity, layer, group, and lock controls for the current selection.

On narrow screens, the toolbar becomes horizontally scrollable and the inspector becomes a bottom sheet. The canvas remains usable.

## Data model

```ts
type BoardDocument = {
  version: 1;
  name: string;
  viewport: { x: number; y: number; zoom: number };
  elements: BoardElement[];
};

type BoardElement = {
  id: string;
  type: "sticky" | "text" | "shape" | "line" | "path" | "image" | "frame";
  x: number;
  y: number;
  width: number;
  height: number;
  rotation: number;
  locked: boolean;
  style: Record<string, string | number>;
  data: Record<string, unknown>;
};
```

Every user operation enters an undo stack. Only `BoardDocument` is persisted; selection and drag state remain transient.

## Interaction rules

- Space plus drag or middle-button drag pans the canvas.
- Wheel zooms around the pointer position.
- Select supports single selection, Shift multi-selection, and marquee selection.
- Delete and Backspace remove selected elements.
- Shift constrains proportions or horizontal/vertical movement.
- Double-clicking a sticky or text enters in-place editing.
- Connector endpoints snap to sticky and shape edges.
- Locked elements cannot move, edit, or delete until unlocked.
- Images are saved as Data URLs so a local document does not depend on external assets.

## Persistence and export

- Debounced changes write to `localStorage`; refresh restores the latest local document.
- JSON import validates the version and required fields before replacing the current document.
- JSON export downloads the complete document.
- PNG export renders the current board content; v1 supports stickies, text, shapes, lines, and images.

## Error handling

- If `localStorage` is unavailable, editing continues and the top bar shows an unsaved state.
- Oversized or unreadable images are rejected with a dismissible error.
- Invalid imports leave the current document untouched.
- Failed exports leave the document, selection, and undo stack intact.

## Acceptance criteria

1. A blank board can immediately create stickies, text, shapes, lines, and freehand paths.
2. Pan and zoom preserve element positions and sizes.
3. Elements support select, multi-select, move, resize, rotate, delete, lock, and group.
4. Refresh restores the latest local save.
5. Exported JSON can be imported to restore elements and viewport.
6. PNG export includes the main visible elements.
7. Desktop and narrow layouts do not overlap the toolbar, inspector, and canvas.
8. Core controls expose visible selected, hover, disabled, and error states.

## Verification

- Manually exercise the primary interaction paths in a browser.
- Check import, export, and local-storage failure paths in the browser console.
- Add lightweight assertions for serialization and validation without introducing a test framework in v1.
- Inspect both desktop and narrow viewports for overflow and usable hit targets.
