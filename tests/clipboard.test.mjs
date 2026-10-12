import test from 'node:test';
import assert from 'node:assert/strict';
import { createElement } from '../src/model.js';
import { boardClipboardHtml, cloneForPaste, clipboardPlainText, parseBoardClipboard, readBoardClipboardHtml, serializeBoardClipboard, textElementSize } from '../src/clipboard.js';

test('round trips copied elements through the clipboard payload', () => {
  const sticky = createElement('sticky', { x: 10, y: 20, data: { text: 'Idea' } });
  assert.deepEqual(parseBoardClipboard(serializeBoardClipboard([sticky])), [sticky]);
});

test('ignores clipboard text that is not a board payload', () => {
  assert.equal(parseBoardClipboard(''), null);
  assert.equal(parseBoardClipboard('just some text'), null);
  assert.equal(parseBoardClipboard('{"elements":[]}'), null);
  assert.equal(parseBoardClipboard('{"canvasroom":1,"elements":[{"id":"a","type":"video"}]}'), null);
  assert.equal(parseBoardClipboard('{"canvasroom":1,"elements":[]}'), null);
});

test('carries board elements inside clipboard HTML, alongside readable text', () => {
  const sticky = createElement('sticky', { data: { text: 'Fish & <chips> "now"' } });
  const html = `<meta charset="utf-8">${boardClipboardHtml([sticky])}`;
  assert.deepEqual(readBoardClipboardHtml(html), [sticky]);
  assert.ok(html.includes('Fish &amp; &lt;chips&gt; &quot;now&quot;'));
  assert.equal(readBoardClipboardHtml('<p>hello</p>'), null);
  assert.equal(readBoardClipboardHtml(''), null);
});

test('pasted copies get new ids, are unlocked, and are centered on the target point', () => {
  const a = createElement('sticky', { x: 0, y: 0, width: 100, height: 100, locked: true });
  const b = createElement('shape', { x: 300, y: 100, width: 100, height: 100 });
  const copies = cloneForPaste([a, b], { x: 1000, y: 500 });
  assert.equal(copies.length, 2);
  assert.notEqual(copies[0].id, a.id);
  assert.notEqual(copies[1].id, b.id);
  assert.equal(copies[0].locked, false);
  assert.deepEqual([copies[0].x, copies[0].y], [800, 400]);
  assert.deepEqual([copies[1].x, copies[1].y], [1100, 500]);
  assert.deepEqual([a.x, a.y, a.locked], [0, 0, true]);
});

test('pasted copies keep groups and connectors pointing at the copies', () => {
  const a = createElement('shape', { x: 0, y: 0, data: { groupId: 'g1' } });
  const b = createElement('shape', { x: 300, y: 0, data: { groupId: 'g1' } });
  const line = createElement('line', { x: 180, y: 60, data: { x2: 120, y2: 0, connector: true, from: a.id, to: b.id } });
  const [ca, cb, cl] = cloneForPaste([a, b, line], { x: 0, y: 0 });
  assert.equal(ca.data.groupId, cb.data.groupId);
  assert.notEqual(ca.data.groupId, 'g1');
  assert.deepEqual([cl.data.from, cl.data.to], [ca.id, cb.id]);
});

test('a connector pasted without its endpoints becomes a free line', () => {
  const a = createElement('shape');
  const line = createElement('line', { data: { x2: 50, y2: 0, connector: true, from: a.id, to: 'elsewhere' } });
  const [copy] = cloneForPaste([line], { x: 0, y: 0 });
  assert.equal(copy.data.from, undefined);
  assert.equal(copy.data.to, undefined);
});

test('pasted freehand paths move their points with them', () => {
  const path = createElement('path', { x: 0, y: 0, data: { points: [{ x: 0, y: 0 }, { x: 10, y: 10 }] } });
  const [copy] = cloneForPaste([path], { x: 100, y: 100 });
  const dx = copy.x - path.x;
  const dy = copy.y - path.y;
  assert.deepEqual(copy.data.points, [{ x: dx, y: dy }, { x: 10 + dx, y: 10 + dy }]);
});

test('plain text of copied elements joins their text and titles', () => {
  const sticky = createElement('sticky', { data: { text: 'One' } });
  const frame = createElement('frame', { data: { title: 'Two' } });
  const image = createElement('image');
  assert.equal(clipboardPlainText([sticky, frame, image]), 'One\nTwo');
});

test('sizes a pasted text block to its content, wrapping long lines', () => {
  const short = textElementSize('Hi', 22);
  const long = textElementSize('word '.repeat(60), 22);
  const multi = textElementSize('a\nb\nc', 22);
  assert.ok(short.width < long.width);
  assert.ok(long.width <= 420);
  assert.ok(long.height > short.height);
  assert.ok(multi.height > short.height);
});
