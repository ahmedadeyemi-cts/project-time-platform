/** Real Chromium CSS-cascade regression checks; no app login or API writes.
 * Run after the normal frontend build: node scripts/validate-theme-contrast.mjs
 * CHROME_BIN may select Chromium/Chrome. --css FILE may be repeated for a
 * deliberately limited source-level fixture run (reported as such).
 */
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { spawn, spawnSync } from 'node:child_process';
import { existsSync, mkdirSync, mkdtempSync, readFileSync, readdirSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { setTimeout as delay } from 'node:timers/promises';

const root = fileURLToPath(new URL('../', import.meta.url));
const output = path.resolve(root, process.env.CONTRAST_OUTPUT_DIR || 'artifacts/theme-contrast');
mkdirSync(output, { recursive: true });
const explicitCss = [];
for (let i = 2; i < process.argv.length; i++) {
  assert.equal(process.argv[i], '--css', `Unknown argument: ${process.argv[i]}`);
  assert.ok(process.argv[++i], '--css requires a filename');
  explicitCss.push(path.resolve(process.argv[i]));
}
const dist = path.join(root, 'dist');
const cssFiles = explicitCss.length ? explicitCss : [...readFileSync(path.join(dist, 'index.html'), 'utf8')
  .matchAll(/<link\b[^>]*href=["']([^"']+\.css)["'][^>]*>/g)]
  .map((match) => path.join(dist, match[1].replace(/^\//, '')));
assert.ok(cssFiles.length, 'No built CSS found. Run npm run build first.');
for (const file of cssFiles) assert.ok(existsSync(file), `Missing CSS: ${file}`);

const roles = ['Engineering', 'Project Management', 'Engineering Lead', 'Project Management Lead',
  'Manager', 'Solution Architect', 'Project Team Coordinator', 'Finance', 'Sales',
  'Administrator', 'Super Admin', 'Read Only'];
const headings = roles.map((name, i) => `<th><div class="rpm-role-heading"><strong data-check="role-${i}">${name}</strong><small data-check="role-code-${i}">${name.toUpperCase().replaceAll(' ', '_')}</small></div></th>`).join('');
const leaf = (tag, name, text = name) => `<${tag} data-check="${name}">${text}</${tag}>`;
const fixture = `
<section class="role-welcome-dashboard">
  <header class="welcome-dashboard-hero" data-enterprise-page-header="true">
    <div><p class="eyebrow" data-check="eyebrow">PULSE</p>${leaf('h1', 'greeting', 'Good morning, Demo User')}${leaf('p', 'hero-copy', 'Here is what needs your attention today.')}</div>
    <div class="welcome-dashboard-date">${leaf('span', 'date', 'Tuesday, September 22')}${leaf('small', 'workspace', 'Administrator workspace')}</div>
    <nav class="welcome-quick-actions"><a href="#work-register" data-check="quick-action">Work Register</a></nav>
  </header>
  <article class="welcome-card"><div class="welcome-card-heading">${leaf('span', 'card-label', 'THIS WEEK')}${leaf('h2', 'card-heading', 'Assigned work')}</div>
    <div class="welcome-week-days"><div>${leaf('span', 'day-label', 'Monday')}${leaf('strong', 'day-hours', '8 hours')}</div></div>
    <p class="welcome-card-copy" data-check="card-copy">Current project summary.</p>
    <a class="welcome-card-link" href="#projects" data-check="card-link">View projects</a>
  </article>
</section>
<section class="projectpulse-module-standard uss-enterprise-module-page">
  <header data-enterprise-page-header="true"><h2 data-check="module-title">Module workspace</h2>
    ${leaf('span', 'module-span', 'Assigned team')}${leaf('small', 'module-small', 'Updated today')}
    ${leaf('strong', 'module-strong', 'Ready for review')}${leaf('code', 'module-code', 'MODULE_025')}
    <a href="#help" data-check="header-link">Help documentation</a>
    <button class="primary-action" data-hover><span data-check="primary-label">Generate</span></button>
    <button data-hover><strong data-check="secondary-label">Review</strong></button>
    <button class="primary-action" disabled><span data-check="disabled-label">Unavailable</span></button>
    <label data-check="input-label">Search<input placeholder="Find an item" value="Example" data-check="header-input" data-placeholder data-hover></label>
    <select data-check="header-select"><option>All projects</option></select>
    <div data-contrast-surface="surface"><strong data-check="nested-card">Nested normal surface</strong><small data-check="nested-helper">Normal helper text</small></div>
  </header>
  <p data-check="normal-copy">Normal page text outside the banner.</p>
  <button class="primary-action" data-hover><span data-check="page-primary">Save changes</span></button>
  <input placeholder="Search outside header" data-check="page-input" data-placeholder value="Value" data-hover>
</section>
<section class="role-permission-matrix-v2">
  <div class="rpm-toolbar"><label><span data-check="filter-label">Module</span><input value="Timesheet" data-check="filter-input" placeholder="Find module" data-placeholder></label></div>
  <div class="rpm-scroll-note"><strong data-check="scroll-tip">Matrix viewing tip</strong><span data-check="scroll-copy">Scroll horizontally to compare roles.</span></div>
  <div class="rpm-permission-table-wrap"><table class="rpm-permission-table"><thead><tr><th data-check="page-th">Page</th><th data-check="permission-th">Permission</th><th data-check="description-th">Description</th>${headings}</tr></thead>
  <tbody><tr data-hover><td>${leaf('strong', 'row-title', '001: Timesheet')}</td><td>${leaf('code', 'row-code', 'MODULE_VIEW')}</td><td>${leaf('span', 'row-description', 'Open the workspace')}</td>
  <td class="rpm-decision rpm-decision-allow"><button data-hover>${leaf('strong', 'allow', 'Allow')}${leaf('small', 'allow-scope', 'SELF')}</button></td>
  <td class="rpm-decision rpm-decision-deny"><button data-hover>${leaf('strong', 'deny', 'No Access')}${leaf('small', 'deny-scope', 'MANAGED_PROJECTS')}</button></td>
  <td class="rpm-decision rpm-decision-not-set"><button data-hover>${leaf('strong', 'not-set', 'Not Set')}${leaf('small', 'not-set-scope', 'Unconfigured')}</button></td></tr></tbody></table></div>
</section>
<table class="uss-table"><thead><tr><th><span data-check="shared-table-heading">Another module</span><button data-hover><span data-check="sort-button">Sort</span></button></th></tr></thead><tbody><tr><td>Value</td></tr></tbody></table>
`;

/* Evaluate the rendered colors, including transparency and gradient layers.
 * For gradients use ALL stop combinations. This conservative envelope is
 * stricter than sampling a favorable point and never assumes a white backing.
 */
function browserChecks() {
  function splitLayers(value) {
    const parts = []; let depth = 0; let start = 0;
    for (let i = 0; i < value.length; i++) {
      if (value[i] === '(') depth++;
      if (value[i] === ')') depth--;
      if (value[i] === ',' && depth === 0) { parts.push(value.slice(start, i)); start = i + 1; }
    }
    parts.push(value.slice(start)); return parts;
  }
  function rgba(value) {
    if (value === 'transparent') return [0, 0, 0, 0];
    const match = value.match(/^rgba?\(([^)]+)\)$/);
    if (match) { const numbers = match[1].split(/[\s,\/]+/).filter(Boolean).map(Number); return [...numbers.slice(0, 3), numbers[3] ?? 1]; }
    const srgb = value.match(/^color\(srgb\s+([\d.]+)\s+([\d.]+)\s+([\d.]+)(?:\s*\/\s*([\d.]+))?\)$/);
    if (srgb) return [+srgb[1] * 255, +srgb[2] * 255, +srgb[3] * 255, +(srgb[4] ?? 1)];
    throw new Error(`Unsupported color: ${value}`);
  }
  const over = (fg, bg) => [...fg.slice(0, 3).map((c, i) => c * fg[3] + bg[i] * (1 - fg[3])), 1];
  const luminance = (rgb) => rgb.slice(0, 3).map((c) => {
    const v = c / 255; return v <= 0.04045 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4;
  }).reduce((sum, c, i) => sum + c * [0.2126, 0.7152, 0.0722][i], 0);
  const ratio = (a, b) => { const x = luminance(a); const y = luminance(b); return (Math.max(x, y) + 0.05) / (Math.min(x, y) + 0.05); };
  function backgrounds(element) {
    const chain = []; for (let node = element; node; node = node.parentElement) chain.unshift(node);
    let candidates = [[255, 255, 255, 1]];
    for (const node of chain) {
      const style = getComputedStyle(node);
      candidates = candidates.map((bg) => over(rgba(style.backgroundColor), bg));
      if (style.backgroundImage !== 'none') {
        for (const layer of splitLayers(style.backgroundImage).reverse()) {
          if (!/(?:linear|radial)-gradient\(/.test(layer)) throw new Error('Image-backed text needs separate visual review');
          const stops = [...layer.matchAll(/rgba?\([^)]+\)|color\(srgb[^)]+\)|transparent/g)].map((m) => rgba(m[0]));
          if (!stops.length) throw new Error(`Unparsed gradient: ${layer}`);
          candidates = candidates.flatMap((bg) => stops.map((stop) => over(stop, bg)));
        }
      }
      /* Opacity on an ancestor composites the whole subtree; report this
       * explicitly rather than incorrectly counting it as fully opaque. */
      if (+style.opacity !== 1) throw new Error(`Ancestor opacity requires visual review: ${node.className || node.tagName}`);
    }
    return candidates;
  }
  const results = [];
  for (const element of document.querySelectorAll('[data-check]')) {
    const checks = [null, ...(element.hasAttribute('data-placeholder') ? ['::placeholder'] : [])];
    for (const pseudo of checks) {
      const name = element.dataset.check + (pseudo || '');
      const previousValue = element.value;
      if (pseudo) element.value = '';
      try {
        const style = getComputedStyle(element, pseudo);
        const box = element.getBoundingClientRect();
        if (!box.width || !box.height || style.visibility === 'hidden' || style.display === 'none') throw new Error('Expected fixture is not rendered');
        const text = rgba(style.webkitTextFillColor || style.color);
        text[3] *= +style.opacity;
        const minimum = Math.min(...backgrounds(element).map((bg) => ratio(over(text, bg), bg)));
        results.push({ name, ratio: +minimum.toFixed(4), pass: minimum >= 4.5, color: style.color, fill: style.webkitTextFillColor });
      } catch (error) { results.push({ name, pass: false, error: error.message }); }
      finally { if (pseudo) element.value = previousValue; }
    }
  }
  return results;
}

const html = `<!doctype html><html data-theme="light" data-pulse-experience="enterprise"><head><meta charset="utf-8"><title>Surface contrast regression fixture</title>
${cssFiles.map((f) => `<style>${readFileSync(f, 'utf8').replaceAll('</style', '<\\/style')}</style>`).join('\n')}
<style>body{margin:0;font-family:Arial,sans-serif}main{padding:24px}section{margin-bottom:24px}[data-check]{scroll-margin:20px}</style></head><body><main class="app-shell enterprise-nav-enabled">${fixture}</main></body></html>`;
const fixturePath = path.join(output, 'fixture.html');
writeFileSync(fixturePath, html);
const binaries = [process.env.CHROME_BIN, 'chromium', 'chromium-browser', 'google-chrome', 'google-chrome-stable'].filter(Boolean);
const chrome = binaries.find((binary) => spawnSync(binary, ['--version'], { timeout: 5000 }).status === 0);
assert.ok(chrome, 'Chromium is required. Set CHROME_BIN to its executable.');
const profile = mkdtempSync(path.join(tmpdir(), 'pulse-contrast-'));
const browser = spawn(chrome, ['--headless=new', '--no-sandbox', '--disable-dev-shm-usage', '--disable-gpu',
  '--remote-debugging-port=0', `--user-data-dir=${profile}`, '--window-size=1680,1200', 'about:blank'], { stdio: 'ignore' });
let socket;
const pending = new Map(); let nextId = 0;
function send(method, params = {}) {
  return new Promise((resolve, reject) => {
    const id = ++nextId;
    const timer = setTimeout(() => { pending.delete(id); reject(new Error(`CDP timeout: ${method}`)); }, 20000);
    pending.set(id, { resolve: (result) => { clearTimeout(timer); resolve(result); }, reject: (error) => { clearTimeout(timer); reject(error); } });
    socket.send(JSON.stringify({ id, method, params }));
  });
}
async function evaluate(expression) {
  const response = await send('Runtime.evaluate', { expression, returnByValue: true, awaitPromise: true });
  assert.ok(!response.exceptionDetails, JSON.stringify(response.exceptionDetails));
  return response.result.value;
}
try {
  const portFile = path.join(profile, 'DevToolsActivePort');
  for (let i = 0; !existsSync(portFile) && i < 100; i++) await delay(100);
  assert.ok(existsSync(portFile), 'Chromium did not start its debugging endpoint');
  const port = readFileSync(portFile, 'utf8').split('\n')[0];
  const targets = await (await fetch(`http://127.0.0.1:${port}/json/list`)).json();
  const target = targets.find((item) => item.type === 'page');
  assert.ok(target, 'No Chromium page target');
  socket = new WebSocket(target.webSocketDebuggerUrl);
  await new Promise((resolve, reject) => { socket.addEventListener('open', resolve, { once: true }); socket.addEventListener('error', reject, { once: true }); });
  socket.addEventListener('message', (event) => {
    const message = JSON.parse(event.data); const request = pending.get(message.id);
    if (!request) return; pending.delete(message.id);
    if (message.error) request.reject(new Error(JSON.stringify(message.error))); else request.resolve(message.result);
  });
  await send('Page.enable'); await send('DOM.enable'); await send('CSS.enable');
  const { frameTree } = await send('Page.getFrameTree');
  await send('Page.setDocumentContent', { frameId: frameTree.frame.id, html });
  for (let i = 0; i < 100; i++) { if (await evaluate('document.readyState === "complete" && !!document.querySelector("[data-check]")')) break; await delay(100); }
  const { root: documentRoot } = await send('DOM.getDocument', { depth: -1 });
  const { nodeIds: stateNodes } = await send('DOM.querySelectorAll', { nodeId: documentRoot.nodeId, selector: '[data-hover]' });
  const scenarios = [];
  for (const experience of ['classic', 'enterprise']) {
    /* Change modes in the same document: catches stale theme inheritance. */
    for (const theme of ['light', 'dark', 'light']) {
      await evaluate(`document.documentElement.dataset.pulseExperience=${JSON.stringify(experience)};document.documentElement.dataset.theme=${JSON.stringify(theme)};document.body.dataset.theme=${JSON.stringify(theme)};true`);
      for (const state of ['normal', 'hover', 'focus-visible']) {
        for (const nodeId of stateNodes) await send('CSS.forcePseudoState', { nodeId, forcedPseudoClasses: state === 'normal' ? [] : [state] });
        const checks = await evaluate(`(${browserChecks.toString()})()`);
        assert.ok(checks.length >= 65, `Fixture unexpectedly lost coverage: ${checks.length}`);
        scenarios.push({ experience, theme, state, checks });
      }
      for (const nodeId of stateNodes) await send('CSS.forcePseudoState', { nodeId, forcedPseudoClasses: [] });
      const screenshot = await send('Page.captureScreenshot', { format: 'png', captureBeyondViewport: false });
      writeFileSync(path.join(output, `${experience}-${theme}.png`), Buffer.from(screenshot.data, 'base64'));
    }
  }
  const inventory = [];
  function scan(directory) {
    for (const entry of readdirSync(directory, { withFileTypes: true })) {
      const file = path.join(directory, entry.name);
      if (entry.isDirectory()) scan(file);
      else if (entry.name.endsWith('.css')) {
        const source = readFileSync(file, 'utf8');
        inventory.push({ file: path.relative(root, file), backgroundDeclarations: (source.match(/\bbackground(?:-color|-image)?\s*:/g) || []).length,
          colorDeclarations: (source.match(/\bcolor\s*:/g) || []).length });
      }
    }
  }
  scan(path.join(root, 'src'));
  const failures = scenarios.flatMap((scenario) => scenario.checks.filter((check) => !check.pass).map((check) => ({ experience: scenario.experience, theme: scenario.theme, state: scenario.state, ...check })));
  const report = { coverage: explicitCss.length ? 'Explicit CSS fixture (not a full application build)' : 'Real production CSS bundle with representative surface fixtures; not authenticated end-to-end route testing',
    css: cssFiles.map((file) => ({ file: path.relative(root, file), sha256: createHash('sha256').update(readFileSync(file)).digest('hex') })),
    stylesheetInventory: inventory, scenarios, failures };
  writeFileSync(path.join(output, 'report.json'), JSON.stringify(report, null, 2));
  console.log(`THEME_CONTRAST: ${scenarios.length} scenarios, ${scenarios.reduce((sum, s) => sum + s.checks.length, 0)} rendered checks, ${failures.length} failures; ${inventory.length} source stylesheets inventoried.`);
  if (failures.length) console.error(JSON.stringify(failures.slice(0, 40), null, 2));
  assert.equal(failures.length, 0, `Contrast failures. See ${path.join(output, 'report.json')}`);
} finally {
  socket?.close(); browser.kill('SIGKILL');
  for (const request of pending.values()) request.reject(new Error('Browser closed'));
  await delay(200);
  rmSync(profile, { recursive: true, force: true, maxRetries: 5, retryDelay: 100 });
}
