import assert from 'node:assert/strict';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { createRequire } from 'node:module';
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const web = path.join(root, 'src/frontend/project-time-web');
const require = createRequire(path.join(web, 'package.json'));
const { createServer } = await import(path.join(web, 'node_modules/vite/dist/node/index.js'));
const { default: react } = await import(path.join(web, 'node_modules/@vitejs/plugin-react/dist/index.js'));
const { chromium } = require(process.env.PLAYWRIGHT_MODULE_PATH || 'playwright');
const harness = '<div id="root"></div><script type="module">import React from "react"; import {createRoot} from "react-dom/client"; import Workspace from "/src/module025/SowGsdWorkspace.jsx"; createRoot(document.getElementById("root")).render(<Workspace/>);</script>';
const server = await createServer({ configFile: false, root: web, plugins: [react(), {
  name: 'module025-harness', configureServer(vite) {
    vite.middlewares.use('/__module025', async (req, res) => {
      res.setHeader('Content-Type', 'text/html');
      res.end(await vite.transformIndexHtml('/__module025', harness.replace('render(<Workspace/>);', 'render(React.createElement(Workspace));')));
    });
  }
}], server: { host: '127.0.0.1', port: 0 } });
await server.listen();
const origin = `http://127.0.0.1:${server.httpServer.address().port}`;
const browser = await chromium.launch({ headless: true });
try {
  for (const state of ['draft', 'review_ready', 'confirmed', 'archived']) {
    const context = await browser.newContext({ acceptDownloads: true });
    await context.addInitScript(() => localStorage.setItem('projectPulseAuthSession', JSON.stringify({ sessionToken: 'synthetic-session-only' })));
    const page = await context.newPage();
    const errors = [], writes = [], downloadRequests = [];
    page.on('pageerror', error => errors.push(error.message));
    const engagement = { engagementId: 'fixture-025', engagementNumber: 'SOW-TEST-025', customerName: 'Synthetic customer', ownerUserId: 'sa', ownerDisplayName: 'Test SA', status: state, isActive: state !== 'archived', revision: 1, phases: [], lastGeneratedAt: state === 'draft' ? null : '2026-09-17T00:00:00Z' };
    await page.route('**/api/**', async route => {
      const req = route.request(), url = new URL(req.url());
      if (!['GET', 'HEAD'].includes(req.method())) writes.push(req.method() + url.pathname);
      let body;
      if (url.pathname.endsWith('/bootstrap')) body = { currentUser: { userId: 'sa' }, access: { canCreate: true, isSolutionArchitect: true }, solutionArchitects: [{ userId: 'sa', displayName: 'Test SA' }], commercialModels: [], customerPrograms: [] };
      else if (/\/(sow\.docx|gsd\.xlsx)$/.test(url.pathname)) {
        downloadRequests.push({ path: url.pathname, headers: req.headers() });
        await route.fulfill({ status: 200, contentType: 'application/octet-stream', body: 'retained-synthetic-document-bytes' }); return;
      }
      else if (url.pathname.endsWith('/versions')) body = { ...engagement, canWrite: state !== 'archived', currentContentReleased: state === 'confirmed', latestVersionId: 'v1', sellReadiness: { ready: false, message: 'Automatic document upload is not connected.' }, versions: state === 'confirmed' || state === 'archived' ? [{ versionId: 'v1', versionNumber: 1, submissions: [] }] : [] };
      else if (url.pathname.endsWith('/history')) body = { engagementId: engagement.engagementId, events: [] };
      else if (url.pathname.endsWith('/sow-register')) body = { records: [], statistics: [] };
      else if (url.pathname.endsWith('/fixture-025')) body = { engagement, access: { canEdit: true, canArchive: true } };
      else body = { engagements: [engagement] };
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) });
    });
    await page.goto(origin + '/__module025');
    const actions = page.getByRole('region', { name: 'Documents and SELL', exact: true });
    for (const name of ['Download SOW (.docx)', 'Download GSD (.xlsx)', 'Send to ConnectWise SELL', 'Version history']) {
      await actions.getByRole('button', { name, exact: true }).waitFor();
      assert.equal(await actions.getByRole('button', { name, exact: true }).isDisabled(), true, `No record: ${name}`);
    }
    await page.locator('.m025-work-card').click();
    await page.locator('.m025-editor-panel').getByText('SOW-TEST-025', { exact: true }).waitFor();
    for (const name of ['Download SOW (.docx)', 'Download GSD (.xlsx)'])
      assert.equal(await actions.getByRole('button', { name, exact: true }).isEnabled(), state === 'confirmed', `${state}: ${name}`);
    if (state === 'confirmed') {
      for (const name of ['Download SOW (.docx)', 'Download GSD (.xlsx)']) {
        const pending = page.waitForEvent('download');
        await actions.getByRole('button', { name, exact: true }).click();
        const download = await pending;
        assert.equal(await download.failure(), null);
      }
      assert.equal(downloadRequests.length, 2);
      for (const req of downloadRequests) {
        assert.equal(req.headers.authorization, 'Bearer synthetic-session-only');
        assert.equal(req.headers['x-projectpulse-session'], 'synthetic-session-only');
      }
    }
    await actions.getByRole('button', { name: 'Send to ConnectWise SELL', exact: true }).click();
    const register = page.locator('[data-module025-sow-register="true"]');
    await register.getByRole('heading', { name: /SOW-TEST-025/ }).waitFor();
    await register.getByText('Automatic SELL publication is not enabled', { exact: true }).waitFor();
    assert.equal(await register.getByRole('button', { name: state === 'confirmed' || state === 'archived' ? 'Push to ConnectWise SELL' : 'Send to ConnectWise SELL', exact: true }).isDisabled(), true);
    await page.getByRole('tab', { name: 'SOW Authoring', exact: true }).click();
    await page.locator('.m025-editor-panel').getByText('SOW-TEST-025', { exact: true }).waitFor();
    assert.deepEqual(writes, [], 'Viewing actions or SELL readiness must not generate, save, or publish');
    assert.deepEqual(errors, []);
    await context.close();
    console.log(`MODULE025_DOCUMENT_ACTIONS=${state}:PASS visible=all authenticatedDownloads=${downloadRequests.length} writes=0`);
  }
} finally { await browser.close(); await server.close(); }
