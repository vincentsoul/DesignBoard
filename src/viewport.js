export function zoomAt(viewport, requestedZoom, point) {
  const zoom = Math.max(.25, Math.min(3, requestedZoom));
  const ratio = zoom / viewport.zoom;
  return { x: point.x - (point.x - viewport.x) * ratio, y: point.y - (point.y - viewport.y) * ratio, zoom };
}

export function wheelZoom(zoom, deltaY, deltaMode, height) {
  const pixels = deltaY * (deltaMode === 1 ? 16 : deltaMode === 2 ? height : 1);
  return zoom * Math.exp(-Math.max(-240, Math.min(240, pixels)) * .002);
}
