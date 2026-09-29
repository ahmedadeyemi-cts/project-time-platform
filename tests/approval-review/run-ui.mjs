import { build } from 'esbuild';
import { unlink } from 'node:fs/promises';
const outfile = new URL('./.ui-test.mjs', import.meta.url);
try {
  await build({ entryPoints: [new URL('./ui.test.jsx', import.meta.url).pathname], outfile: outfile.pathname, bundle: true, platform: 'node', format: 'esm', packages: 'external', jsx: 'automatic', loader: {'.css':'empty'} });
  await import(outfile.href);
} finally { await unlink(outfile).catch(() => {}); }
