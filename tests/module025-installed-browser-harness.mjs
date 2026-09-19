import { createServer } from '../src/frontend/project-time-web/node_modules/vite/dist/node/index.js';
import react from '../src/frontend/project-time-web/node_modules/@vitejs/plugin-react/dist/index.js';
import path from 'node:path';
import fs from 'node:fs';
import crypto from 'node:crypto';
import { fileURLToPath } from 'node:url';
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const engagement = {
  engagementId: 'fixture-025', engagementNumber: 'SOW-TEST-025', customerName: 'Synthetic customer',
  ownerUserId: 'sa', ownerDisplayName: 'Test SA', status: 'confirmed', isActive: true, revision: 5,
  serviceOverview: 'Synthetic CUCM upgrade.', customerEntryMode: 'manual', commercialModel: 'time_and_materials',
  customerProgram: 'standard', phases: [], lastGeneratedAt: '2026-09-17T00:00:00Z'
};
const requests = [];
const registerMode = process.env.MODULE025_TEST_REGISTER === 'true';
const session = { sessionToken: 'synthetic-session-only', username: 'demo.manager@ussignal.local', loginMethod: 'local', provider: 'LOCAL', expiresAt: '2099-01-01T00:00:00Z', mustChangePassword: false };
const bytes = label => Buffer.from(process.env['MODULE025_TEST_' + label.toUpperCase()], 'base64');
const digest = data => crypto.createHash('sha256').update(data).digest('hex');
const dist = path.join(root, 'src/frontend/project-time-web/dist');
if (!fs.existsSync(path.join(dist, 'index.html'))) throw new Error('Build the complete application before the installed browser test.');
const user = { userId: 'sa', username: 'synthetic.local', displayName: 'Test SA', roles: [{ roleCode: 'SOLUTION_ARCHITECT' }], permissions: ['VIEW_SOW_GENERATOR', 'MANAGE_SOW_GENERATOR'] };
if (registerMode) user.roles = [{ roleCode: 'MANAGER' }];
let denyModule = false;
let denyNavigation = false;
let reportFailure = '';
let bootstrapReady = Promise.resolve();
let releaseBootstrap = () => {};
const server = await createServer({ configFile: false, root: path.join(root, 'src/frontend/project-time-web'),
  plugins: [react(), { name: 'installed-browser-regression', configureServer(vite) {
    vite.middlewares.use(async (req, res, next) => {
      const url = new URL(req.url, 'http://127.0.0.1');
      if (url.pathname === '/') {
        res.setHeader('Content-Type', 'text/html');
        res.end(fs.readFileSync(path.join(dist, 'index.html'))); return;
      }
      if (url.pathname.startsWith('/assets/')) {
        const asset = path.resolve(dist, '.' + url.pathname);
        if (!asset.startsWith(dist + path.sep) || !fs.existsSync(asset)) { res.statusCode = 404; res.end(); return; }
        res.setHeader('Content-Type', asset.endsWith('.js') ? 'text/javascript' : asset.endsWith('.css') ? 'text/css' : 'application/octet-stream');
        res.end(fs.readFileSync(asset)); return;
      }
      if (url.pathname === '/__state') {
        res.setHeader('Content-Type', 'application/json'); res.end(JSON.stringify({ engagement, requests })); return;
      }
      if (url.pathname === '/__release-bootstrap' && req.method === 'GET') {
        releaseBootstrap(); res.end('{}'); return;
      }
      if (url.pathname === '/__reset' && req.method === 'POST') {
        const chunks = []; for await (const chunk of req) chunks.push(chunk);
        const { status, denyDownload, deniedModule, deniedNavigation, failReport, holdBootstrap } = JSON.parse(Buffer.concat(chunks).toString());
        denyModule = deniedModule === true;
        denyNavigation = deniedNavigation === true;
        reportFailure = failReport || '';
        releaseBootstrap();
        bootstrapReady = holdBootstrap ? new Promise(resolve => { releaseBootstrap = resolve; }) : Promise.resolve();
        engagement.status = status; engagement.serviceOverview = 'Synthetic CUCM upgrade.';
        requests.length = 0; process.env.MODULE025_TEST_DENY_DOWNLOAD = denyDownload || '';
        res.end('{}'); return;
      }
      if (!url.pathname.startsWith('/api/')) { next(); return; }
      if (url.pathname === '/api/module025/sow-gsd/bootstrap' && req.headers['sec-fetch-mode']) await bootstrapReady;
      if (process.env.MODULE025_TEST_DELAY_STARTUP === 'true'
          && ['/api/security/me', '/api/users/me', '/api/module025/sow-gsd/bootstrap'].includes(url.pathname)) {
        await new Promise(resolve => setTimeout(resolve, url.pathname.endsWith('/bootstrap') ? 1200 : 700));
      }
      const authenticated = req.headers.authorization === 'Bearer synthetic-session-only' && req.headers['x-projectpulse-session'] === 'synthetic-session-only';
      const fixture = req.headers['x-projectpulse-module025-uat-run'] === '12345-1' && req.headers.origin === `http://${req.headers.host}`;
      requests.push({ method: req.method, path: url.pathname, ...(registerMode ? { fixture, authenticated,
        runHeaderPresent: Boolean(req.headers['x-projectpulse-module025-uat-run']) } : {}) });
      let body;
      if (registerMode && url.pathname === '/api/auth/local/login') {
        const chunks = []; for await (const chunk of req) chunks.push(chunk);
        const login = JSON.parse(Buffer.concat(chunks).toString());
        if (login.username !== 'demo.manager@ussignal.local' || login.password !== 'synthetic-password-only') { res.statusCode = 401; body = {}; }
        else body = session;
      }
      else if (registerMode && url.pathname.startsWith('/api/module025/') && (!authenticated || !fixture || denyModule)) {
        res.statusCode = authenticated ? 403 : 401; body = { message: 'Fixture/session required' };
      }
      else if (url.pathname === '/api/module025/sow-gsd/bootstrap') body = { currentUser: { userId: 'sa' }, access: { canCreate: true, isSolutionArchitect: true, ...(registerMode ? { isManager: true, managerScopeReadOnly: true, protectedTestUatRoleFixture: true } : {}) },
        solutionArchitects: [{ userId: 'sa', displayName: 'Test SA' }], commercialModels: [], customerPrograms: [] };
      else if (registerMode && url.pathname === '/api/module025/sow-register') {
        if (reportFailure === 'api' || (reportFailure === 'browser' && req.headers['sec-fetch-mode'])) {
          res.statusCode = reportFailure === 'api' ? 500 : 403;
          res.setHeader('Content-Type', 'application/json');
          res.end(JSON.stringify({ message: 'Synthetic report denial; never log this body.' })); return;
        }
        if (url.searchParams.get('format') === 'csv') {
          const data = Buffer.from('\uFEFFRuntime environment,From UTC date,Through UTC date,ownerUserId,ownerDisplayName,recordsCreated,uniqueSowsGenerated,successfulGenerationRuns,failedGenerationRuns,releasedVersions,uniqueSowsSent,successfulVersionSubmissions,submissionRequests,blockedSubmissions\r\ntest,All time,All time,sa,Test SA,1,1,1,0,1,0,0,0,0\r\n');
          res.setHeader('Content-Type', 'text/csv'); res.setHeader('X-Content-Sha256', digest(data)); res.end(data); return;
        }
        body = { status: 'module025_sow_register', records: [{ ...engagement, latestVersionNumber: 1 }], statistics: [], totalRecords: 1, runtimeEnvironment: 'test' };
      }
      else if (registerMode && url.pathname.endsWith('/versions')) body = {
        ...engagement, runtimeEnvironment: 'test', currentContentReleased: true, latestVersionId: 'version-1',
        versions: [{ versionId: 'version-1', versionNumber: 1, sourceRevision: 5,
          sowSha256: digest(bytes('sow')), gsdSha256: digest(bytes('gsd')), submissions: [] }] };
      else if (registerMode && url.pathname.endsWith('/history')) body = { events: [] };
      else if (/\/(sow\.docx|gsd\.xlsx)$/.test(url.pathname)) {
        if (process.env.MODULE025_TEST_DENY_DOWNLOAD === 'true' || req.headers.authorization !== 'Bearer synthetic-session-only' || req.headers['x-projectpulse-session'] !== 'synthetic-session-only') {
          res.statusCode = 401; body = { message: 'Session required' };
        } else {
          const label = url.pathname.endsWith('sow.docx') ? 'sow' : 'gsd';
          res.setHeader('Content-Type', 'application/octet-stream');
          res.end(bytes(label)); return;
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
      else if (['/api/users/me', '/api/security/me'].includes(url.pathname)) body = { ...user, isViewAs: false };
      else if (url.pathname.startsWith('/api/rbac/v1/')) body = { roles: user.roles,
        modules: [{ moduleCode: '025', moduleNumber: '025', isActive: true }], actor: { roleCodes: user.roles.map(role => role.roleCode) },
        grants: denyModule || denyNavigation ? [{ moduleCode: '025', roleCode: user.roles[0].roleCode, actionCode: 'MODULE_ACCESS', grantEffect: 'DENY' }] : [], legacyFallback: [] };
      else if (url.pathname === '/api/module-availability/overrides') body = { states: [], access: {} };
      else if (url.pathname === '/api/module-availability') body = { modules: [] };
      else if (url.pathname.startsWith('/api/module025/')) { res.statusCode = 500; body = { message: 'Unexpected Module 025 test request' }; }
      else body = {}; // Unrelated read-only shell widgets use empty synthetic data.

      res.setHeader('Content-Type', 'application/json'); res.end(JSON.stringify(body));
    });
  }}], server: { host: '127.0.0.1', port: 0 } });
await server.listen();
// Vite may log dependency optimization before listening, especially after the
// separate document-actions fixture. Readiness must have its own marker.
console.log('MODULE025_HARNESS_STARTUP: synthetic log before readiness');
console.log('MODULE025_HARNESS_READY=' + JSON.stringify({ origin: `http://127.0.0.1:${server.httpServer.address().port}` }));
process.on('SIGTERM', async () => { await server.close(); process.exit(0); });
