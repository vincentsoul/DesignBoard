// Apply a guarded language-default overlay to the deployed v5.7.8 Web client.
// Usage: node scripts/quantdinger-zh-default.mjs ORIGINAL_HTML ORIGINAL_JS OUTPUT_DIR
// Generated client assets belong in the private deployment directory, not Git.
import assert from 'node:assert/strict';
import {readFile, writeFile, mkdir} from 'node:fs/promises';
import path from 'node:path';

const [htmlFile, jsFile, outputDir] = process.argv.slice(2);
assert(htmlFile && jsFile && outputDir, 'Expected original HTML, JS, and output directory');
let html = await readFile(htmlFile, 'utf8');
const original = await readFile(jsFile, 'utf8');
const originalName = path.basename(jsFile);
assert.match(originalName, /^index-[\w-]+\.js$/);
const needle = 'g.get(Re,"en-US")';
assert.equal(original.split(needle).length - 1, 1, 'Unexpected client version; inspect before patching');
assert(original.includes('Re="app_language"'), 'Language preference contract changed');
assert.equal(html.split(`./assets/${originalName}`).length - 1, 1, 'Unexpected entrypoint reference');
const bootstrap = `// Native locale preference only; authentication storage is untouched.
(() => {
  try {
    const marker = 'quantdinger.default-zh.v1';
    const preference = 'app_language';
    let selected;
    try { selected = JSON.parse(localStorage.getItem(preference)); } catch {}
    if (!selected || (!localStorage.getItem(marker) && selected === 'en-US')) {
      localStorage.setItem(preference, JSON.stringify('zh-CN'));
    }
    localStorage.setItem(marker, '1');
  } catch {}
})();
`;
const moduleTag = /<script\b[^>]*type="module"[^>]*>/;
assert(moduleTag.test(html), 'Missing application module entrypoint');
html = html.replace(moduleTag, tag => `<script src="./assets/quantdinger-zh-default-v1.js"></script>\n    ${tag}`)
  .replace('<html lang="en-US">', '<html lang="zh-CN">')
  .replace('AI quantitative trading infrastructure', 'AI 量化交易基础设施')
  .replace('Loading workspace...', '正在加载工作台…');
assert(html.includes('<html lang="zh-CN">'));
await mkdir(outputDir, {recursive: true});
await writeFile(path.join(outputDir, 'index.html'), html);
await writeFile(path.join(outputDir, 'quantdinger-zh-default-v1.js'), bootstrap);
console.log('Prepared Chinese default overlay; original application bundles unchanged.');
