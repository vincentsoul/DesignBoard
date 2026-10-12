import { parseDocument, serializeDocument, documentBounds } from './model.js';

const STORAGE_KEY = 'canvasroom-board-v1';

export function saveLocal(document) {
  try {
    localStorage.setItem(STORAGE_KEY, serializeDocument(document));
    return true;
  } catch {
    return false;
  }
}

export function loadLocal() {
  try {
    const saved = localStorage.getItem(STORAGE_KEY);
    return saved ? parseDocument(saved) : null;
  } catch {
    return null;
  }
}

function downloadBlob(blob, name) {
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = name;
  anchor.click();
  setTimeout(() => URL.revokeObjectURL(url), 800);
}

export function downloadJson(boardDocument) {
  downloadBlob(new Blob([serializeDocument(boardDocument)], { type: 'application/json' }), `${safeFileName(boardDocument.name)}.json`);
}

export async function readJsonFile(file) {
  return parseDocument(await file.text());
}

function safeFileName(name) {
  return (name || 'board').trim().replace(/[^a-z0-9\u4e00-\u9fff_-]+/gi, '-').replace(/^-+|-+$/g, '') || 'board';
}

function wrapText(ctx, text, maxWidth) {
  const lines = [];
  for (const paragraph of String(text || '').split('\n')) {
    let line = '';
    for (const char of paragraph) {
      const next = line + char;
      if (line && ctx.measureText(next).width > maxWidth) {
        lines.push(line);
        line = char;
      } else line = next;
    }
    lines.push(line);
  }
  return lines;
}

function drawCanvasText(ctx, text, x, y, width, height, style, padding = 16) {
  const fontWeight = style.fontWeight || 500;
  const fontSize = Number(style.fontSize || 18);
  ctx.font = `${fontWeight} ${fontSize}px DM Sans, Arial, sans-serif`;
  ctx.fillStyle = style.color || '#213342';
  ctx.textBaseline = 'top';
  const lines = wrapText(ctx, text, Math.max(20, width - padding * 2));
  const lineHeight = fontSize * 1.38;
  let offset = padding;
  for (const line of lines) {
    if (offset + lineHeight > height - padding) break;
    let tx = x + padding;
    if (style.align === 'center') tx = x + width / 2 - ctx.measureText(line).width / 2;
    if (style.align === 'right') tx = x + width - padding - ctx.measureText(line).width;
    ctx.fillText(line, tx, y + offset);
    offset += lineHeight;
  }
}

function drawElement(ctx, element, origin) {
  const x = element.x - origin.x;
  const y = element.y - origin.y;
  const width = element.width;
  const height = element.height;
  const style = element.style || {};
  ctx.save();
  ctx.globalAlpha = Number(style.opacity ?? 1);
  if (element.type === 'line') {
    ctx.strokeStyle = style.stroke || '#344554';
    ctx.lineWidth = Number(style.strokeWidth || 3);
    ctx.lineCap = 'round';
    ctx.beginPath();
    ctx.moveTo(x, y);
    ctx.lineTo(x + Number(element.data.x2 ?? width), y + Number(element.data.y2 ?? height));
    ctx.stroke();
    ctx.restore();
    return;
  }
  if (element.type === 'path') {
    ctx.strokeStyle = style.stroke || '#344554';
    ctx.lineWidth = Number(style.strokeWidth || 4);
    ctx.lineCap = 'round';
    ctx.lineJoin = 'round';
    ctx.beginPath();
    (element.data.points || []).forEach((point, index) => index ? ctx.lineTo(point.x - origin.x, point.y - origin.y) : ctx.moveTo(point.x - origin.x, point.y - origin.y));
    ctx.stroke();
    ctx.restore();
    return;
  }
  ctx.translate(x + width / 2, y + height / 2);
  ctx.rotate((Number(element.rotation) || 0) * Math.PI / 180);
  ctx.translate(-width / 2, -height / 2);
  if (element.type === 'image') {
    ctx.fillStyle = '#e9eeea';
    ctx.fillRect(0, 0, width, height);
    if (element.data.src) {
      const image = new Image();
      image.src = element.data.src;
      if (image.complete) ctx.drawImage(image, 0, 0, width, height);
    }
  } else if (element.type === 'shape' || element.type === 'sticky' || element.type === 'frame') {
    ctx.fillStyle = style.fill || '#ffffff';
    ctx.strokeStyle = style.stroke || 'transparent';
    ctx.lineWidth = Number(style.strokeWidth || 1);
    const shape = element.data.shape;
    ctx.beginPath();
    if (shape === 'circle') {
      ctx.ellipse(width / 2, height / 2, width / 2, height / 2, 0, 0, Math.PI * 2);
    } else if (shape === 'diamond') {
      ctx.moveTo(width / 2, 0); ctx.lineTo(width, height / 2); ctx.lineTo(width / 2, height); ctx.lineTo(0, height / 2); ctx.closePath();
    } else {
      ctx.roundRect(0, 0, width, height, element.type === 'sticky' ? 3 : 8);
    }
    ctx.fill(); ctx.stroke();
    if (element.type === 'frame') {
      ctx.font = '600 15px DM Sans, Arial, sans-serif'; ctx.fillStyle = '#6e827b'; ctx.fillText(element.data.title || 'Section', 0, -25);
    }
    drawCanvasText(ctx, element.data.text || '', 0, 0, width, height, style, element.type === 'sticky' ? 20 : 14);
  } else if (element.type === 'text') {
    drawCanvasText(ctx, element.data.text || '', 0, 0, width, height, style, 4);
  }
  ctx.restore();
}

export async function downloadPng(boardDocument) {
  const origin = documentBounds(boardDocument, 48);
  const scale = 2;
  const canvas = document.createElement('canvas');
  canvas.width = Math.max(1, Math.ceil(origin.width * scale));
  canvas.height = Math.max(1, Math.ceil(origin.height * scale));
  const ctx = canvas.getContext('2d');
  ctx.scale(scale, scale);
  ctx.fillStyle = '#f6f5f0';
  ctx.fillRect(0, 0, origin.width, origin.height);
  for (const element of boardDocument.elements) drawElement(ctx, element, origin);
  const blob = await new Promise((resolve) => canvas.toBlob(resolve, 'image/png'));
  if (!blob) throw new Error('PNG export failed');
  downloadBlob(blob, `${safeFileName(boardDocument.name)}.png`);
}
