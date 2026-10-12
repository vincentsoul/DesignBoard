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

test('preserves image Data URLs in a local document', () => {
  const doc = createDocument();
  doc.elements.push(createElement('image', { data: { src: 'data:image/png;base64,AAAA', name: 'sketch.png' } }));
  const restored = parseDocument(serializeDocument(doc));
  assert.equal(restored.elements[0].data.src, 'data:image/png;base64,AAAA');
  assert.equal(restored.elements[0].data.name, 'sketch.png');
});

test('rejects invalid documents without mutating input', () => {
  assert.throws(() => parseDocument('{"version":2,"elements":[]}'), /Unsupported document version/);
  assert.throws(() => parseDocument('{"version":1,"name":"Board","viewport":{"x":0,"y":0,"zoom":1},"elements":"bad"}'), /elements must be an array/);
});
