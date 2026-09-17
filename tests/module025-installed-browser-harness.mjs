import { createServer } from '../src/frontend/project-time-web/node_modules/vite/dist/node/index.js';
import react from '../src/frontend/project-time-web/node_modules/@vitejs/plugin-react/dist/index.js';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const engagement = {
  engagementId: 'fixture-025', engagementNumber: 'SOW-TEST-025', customerName: 'Synthetic customer',
  ownerUserId: 'sa', ownerDisplayName: 'Test SA', status: 'confirmed', isActive: true, revision: 5,
  serviceOverview: 'Synthetic CUCM upgrade.', customerEntryMode: 'manual', commercialModel: 'time_and_materials',
  customerProgram: 'standard', phases: [], lastGeneratedAt: '2026-09-17T00:00:00Z'
};
const requests = [];
const html = `<div id="root"></div><script type="module">
import React from 'react'; import {createRoot} from 'react-dom/client';
import Workspace from '/src/module025/SowGsdWorkspace.jsx';
createRoot(document.getElementById('root')).render(React.createElement(Workspace));</script>`;
const server = await createServer({ configFile: false, root: path.join(root, 'src/frontend/project-time-web'),
  plugins: [react(), { name: 'installed-browser-regression', configureServer(vite) {
    vite.middlewares.use(async (req, res, next) => {
      const url = new URL(req.url, 'http://127.0.0.1');
      if (url.pathname === '/') {
        res.setHeader('Content-Type', 'text/html');
        res.end(await vite.transformIndexHtml('/', html)); return;
      }
      if (url.pathname === '/__state') {
        res.setHeader('Content-Type', 'application/json'); res.end(JSON.stringify({ engagement, requests })); return;
      }
      if (url.pathname === '/__reset' && req.method === 'POST') {
        const chunks = []; for await (const chunk of req) chunks.push(chunk);
        const { status, denyDownload } = JSON.parse(Buffer.concat(chunks).toString());
        engagement.status = status; engagement.serviceOverview = 'Synthetic CUCM upgrade.';
        requests.length = 0; process.env.MODULE025_TEST_DENY_DOWNLOAD = denyDownload || '';
        res.end('{}'); return;
      }
      if (!url.pathname.startsWith('/api/')) { next(); return; }
      requests.push({ method: req.method, path: url.pathname });
      let body;
      if (url.pathname.endsWith('/bootstrap')) body = { currentUser: { userId: 'sa' }, access: { canCreate: true, isSolutionArchitect: true },
        solutionArchitects: [{ userId: 'sa', displayName: 'Test SA' }], commercialModels: [], customerPrograms: [] };
      else if (/\/(sow\.docx|gsd\.xlsx)$/.test(url.pathname)) {
        if (process.env.MODULE025_TEST_DENY_DOWNLOAD === 'true' || req.headers.authorization !== 'Bearer synthetic-session-only' || req.headers['x-projectpulse-session'] !== 'synthetic-session-only') {
          res.statusCode = 401; body = { message: 'Session required' };
        } else {
          const label = url.pathname.endsWith('sow.docx') ? 'sow' : 'gsd';
          res.setHeader('Content-Type', 'application/octet-stream');
          res.end(Buffer.from(process.env['MODULE025_TEST_' + label.toUpperCase()], 'base64')); return;
        }
      }
      else if (url.pathname.endsWith('/reopen') && req.method === 'POST') {
        engagement.status = 'review_ready'; engagement.revision++; body = { status: 'module025_reopened' };
      }
      else if (url.pathname.endsWith('/fixture-025') && req.method === 'PUT') {
        const chunks = []; for await (const chunk of req) chunks.push(chunk);
        const update = JSON.parse(Buffer.concat(chunks).toString());
        Object.assign(engagement, update, { status: 'draft', revision: engagement.revision + 1 });
        body = { engagement: { engagement }, requiresRegeneration: true };
      }
      else if (url.pathname.endsWith('/fixture-025')) body = { engagement, access: { canEdit: true, canArchive: true } };
      else if (url.pathname.endsWith('/sow-gsd')) body = { engagements: [engagement] };
      else { res.statusCode = 500; body = { message: 'Unexpected test request' }; }
      res.setHeader('Content-Type', 'application/json'); res.end(JSON.stringify(body));
    });
  }}], server: { host: '127.0.0.1', port: 0 } });
await server.listen();
// Vite may log dependency optimization before listening, especially after the
// separate document-actions fixture. Readiness must have its own marker.
console.log('MODULE025_HARNESS_STARTUP: synthetic log before readiness');
console.log('MODULE025_HARNESS_READY=' + JSON.stringify({ origin: `http://127.0.0.1:${server.httpServer.address().port}` }));
process.on('SIGTERM', async () => { await server.close(); process.exit(0); });
