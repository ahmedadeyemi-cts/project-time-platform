import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { createRequire } from 'node:module';
const require = createRequire(import.meta.url);
const { build } = require(path.resolve(process.env.COMPLETION_ESBUILD_MODULE));
const web = fileURLToPath(new URL('../', import.meta.url));
const at = process.argv.indexOf('--output');
if (at < 0 || !process.argv[at + 1]) throw new Error('Provide --output directory.');
const output = path.resolve(process.argv[at + 1]);
fs.mkdirSync(output, { recursive: true });
const index = fs.readFileSync(path.join(web, 'dist/index.html'), 'utf8');
const links = [...index.matchAll(/<link\b[^>]*rel=["']stylesheet["'][^>]*>/g)].map(m => m[0]);
if (!links.length) throw new Error('Full production CSS is required.');
for (const link of links) {
  const href = link.match(/href=["']([^"']+)["']/)?.[1];
  if (!href?.startsWith('/assets/') || href.includes('..')) throw new Error('Unexpected CSS path.');
  const target = path.join(output, href);
  fs.mkdirSync(path.dirname(target), { recursive: true });
  fs.copyFileSync(path.join(web, 'dist', href), target);
}
const fixture = `import React from 'react';
import { createRoot } from 'react-dom/client';
import ProjectCompletionChecklist from './src/ProjectCompletionChecklist.jsx';
window.__completionIdentity=(name='ptc',preview=false)=>{
 localStorage.setItem('projectPulseAuthSession',JSON.stringify({userId:'offline-'+name,sessionToken:'synthetic-offline-fixture',roleCodes:[name]}));
 if(preview)localStorage.setItem('projectPulseViewAsUser',JSON.stringify({userId:'offline-view'}));else localStorage.removeItem('projectPulseViewAsUser');
 window.dispatchEvent(new Event('projectpulse:auth-session-ready'));
};
window.__completionIdentity();
createRoot(document.getElementById('root')).render(<ProjectCompletionChecklist projectId="11111111-1111-4111-8111-111111111111"/>);`;
await build({ stdin: { contents: fixture, sourcefile: 'completion-offline-fixture.jsx', resolveDir: web, loader: 'jsx' }, bundle: true, format: 'iife', jsx: 'automatic', loader: { '.css': 'empty' }, outfile: path.join(output, 'app.js'), define: { 'process.env.NODE_ENV': '"production"' } });
fs.writeFileSync(path.join(output, 'index.html'), `<!doctype html><html data-theme="light" data-pulse-experience="enterprise" data-pulse-layout="table"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">${links.join('')}</head><body><main><div id="root"></div></main><script src="/app.js"></script></body></html>`);
