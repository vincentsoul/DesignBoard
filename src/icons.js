const paths = {
  select: '<path d="m5 3 14 8-6 2-3 6-5-16Z"/><path d="m12 13 5 5"/>',
  sticky: '<rect x="4" y="4" width="16" height="16" rx="2"/><path d="M8 9h8M8 13h6M8 17h3"/>',
  text: '<path d="M5 5h14M12 5v14M8 19h8"/>',
  shape: '<rect x="4" y="4" width="16" height="16" rx="3"/><circle cx="9" cy="10" r="2"/><path d="m5 18 5-5 3 3 2-2 5 4"/>',
  connector: '<path d="M5 18 18 5M12 5h6v6"/><circle cx="5" cy="18" r="2"/><circle cx="18" cy="5" r="2"/>',
  pen: '<path d="m5 19 1.2-4.2L15.8 5.2a2 2 0 0 1 2.8 2.8L9 17.8 5 19Z"/><path d="m13 8 3 3"/>',
  frame: '<rect x="4" y="5" width="16" height="14" rx="1"/><path d="M4 9h16"/><path d="M8 5v4"/>',
  image: '<rect x="4" y="4" width="16" height="16" rx="2"/><circle cx="9" cy="9" r="1.5"/><path d="m5 17 4-4 3 3 2-2 5 4"/>',
  undo: '<path d="M9 7 4 12l5 5"/><path d="M5 12h8a6 6 0 0 1 6 6"/>',
  redo: '<path d="m15 7 5 5-5 5"/><path d="M19 12h-8a6 6 0 0 0-6 6"/>',
  zoomIn: '<circle cx="10.5" cy="10.5" r="6.5"/><path d="m16 16 4 4M10.5 7v7M7 10.5h7"/>',
  fit: '<path d="M8 4H4v4M16 4h4v4M20 16v4h-4M4 16v4h4"/><path d="M9 9h6v6H9z"/>',
  download: '<path d="M12 4v11M8 11l4 4 4-4M5 20h14"/>',
  upload: '<path d="M12 16V5M8 9l4-4 4 4M5 20h14"/>',
  group: '<rect x="4" y="5" width="8" height="8" rx="1"/><rect x="12" y="11" width="8" height="8" rx="1"/>',
  lock: '<rect x="5" y="10" width="14" height="10" rx="2"/><path d="M8 10V7a4 4 0 0 1 8 0v3"/>',
  unlock: '<rect x="5" y="10" width="14" height="10" rx="2"/><path d="M8 10V7a4 4 0 0 1 7-2"/>',
  trash: '<path d="M5 7h14M10 11v5M14 11v5M7 7l1 13h8l1-13M9 7l1-3h4l1 3"/>',
  more: '<circle cx="5" cy="12" r="1"/><circle cx="12" cy="12" r="1"/><circle cx="19" cy="12" r="1"/>',
  rotate: '<path d="M5 8a7 7 0 1 1-1 6"/><path d="M4 4v4h4"/>',
  paste: '<rect x="8" y="3" width="8" height="4" rx="1"/><path d="M16 5h2a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V7a2 2 0 0 1 2-2h2"/>',
  chevron: '<path d="m6 9 6 6 6-6"/>',
  plus: '<path d="M12 5v14M5 12h14"/>',
  copy: '<rect x="8" y="8" width="12" height="12" rx="2"/><path d="M16 8V6a2 2 0 0 0-2-2H6a2 2 0 0 0-2 2v8a2 2 0 0 0 2 2h2"/>',
};

export function icon(name, size = 18) {
  return `<svg class="icon" width="${size}" height="${size}" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${paths[name] || paths.more}</svg>`;
}
