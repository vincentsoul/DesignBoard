import test from 'node:test';
import assert from 'node:assert/strict';
import { zoomAt, wheelZoom } from '../src/viewport.js';

test('zoom preserves the world point under the cursor even when clamped', () => {
  const view = { x: -135, y: 80, zoom: .8 };
  const point = { x: 420, y: 230 };
  for (const zoom of [.01, .5, 1.4, 10]) {
    const next = zoomAt(view, zoom, point);
    for (const axis of ['x', 'y']) assert.ok(Math.abs((point[axis] - next[axis]) / next.zoom - (point[axis] - view[axis]) / view.zoom) < 1e-9);
    assert.ok(next.zoom >= .25 && next.zoom <= 3);
  }
});
test('normalizes mouse and trackpad wheel units and bounds large jumps', () => {
  assert.equal(wheelZoom(1, 16, 0, 800), wheelZoom(1, 1, 1, 800));
  assert.equal(wheelZoom(1, 80, 0, 800), wheelZoom(1, .1, 2, 800));
  assert.ok(wheelZoom(1, -100, 0, 800) > 1);
  assert.ok(wheelZoom(1, 100, 0, 800) < 1);
  assert.equal(wheelZoom(1, 100000, 0, 800), wheelZoom(1, 240, 0, 800));
});
