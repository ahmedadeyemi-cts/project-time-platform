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
const harness = '<div id="root"></div><output id="opened-register"></output><script type="module">import React from "react";import{createRoot}from"react-dom/client";import Workspace from"/src/module025/SowGsdAuthoringWorkspace.jsx";createRoot(document.getElementById("root")).render(React.createElement(Workspace,{onOpenRegister:id=>{document.getElementById("opened-register").textContent=id}}));</script>';
const server = await createServer({ configFile: false, root: web, cacheDir: path.join(web, 'node_modules/.vite-module025-team-harness'), plugins: [react(), {
  name: 'team-workspace-harness', configureServer(vite) {
    vite.middlewares.use('/__team', async (req, res) => {
      res.setHeader('Content-Type', 'text/html');
      res.end(await vite.transformIndexHtml('/__team', harness));
    });
  }
}], server: { host: '127.0.0.1', port: 0 } });
await server.listen();
const browser = await chromium.launch({ headless: true, ...(process.env.MODULE025_TEST_CHROMIUM ? { executablePath: process.env.MODULE025_TEST_CHROMIUM, args: ['--no-sandbox', '--disable-dev-shm-usage'] } : {}) });
try {
  const context = await browser.newContext();
  const page = await context.newPage();
  const errors = [], writes = [], lists = [], details = [], openedOptions = [];
  page.on('pageerror', error => errors.push(error.message));
  const saA = { userId: '11111111-1111-4111-8111-111111111111', displayName: 'Alex System', teamName: 'System SA' };
  const saB = { userId: '22222222-2222-4222-8222-222222222222', displayName: 'Bea System', teamName: 'System SA' };
  let isViewAs = false, isOwnerSession = false;
  let completeDeferredTransfer, signalDeferredTransfer;
  const deferredTransferStarted = new Promise(resolve => { signalDeferredTransfer = resolve; });
  let record = {
    engagementId: '33333333-3333-4333-8333-333333333333', engagementNumber: 'SOW-TEAM-001', ownerUserId: saA.userId,
    ownerDisplayName: saA.displayName, ownerTeamName: 'System SA', customerName: 'Synthetic customer', projectName: 'Datacenter migration',
    customerEntryMode: 'manual', commercialModel: 'fixed', customerProgram: 'standard', accountExecutiveName: 'Example AE',
    serviceOverview: 'Prepare the customer datacenter migration scope and delivery estimate.',
    status: 'review_ready', isActive: true, revision: 7, updatedAt: '2026-09-20T00:00:00Z', lastGeneratedAt: '2026-09-19T00:00:00Z',
    phases: []
  };
  const other = { ...record, engagementId: '44444444-4444-4444-8444-444444444444', engagementNumber: 'SOW-TEAM-002', ownerUserId: saB.userId, ownerDisplayName: saB.displayName, projectName: 'Storage discovery' };
  await page.route('**/api/**', async route => {
    const req = route.request(), url = new URL(req.url());
    let body;
    if (req.method() !== 'GET') {
      writes.push({ path: url.pathname, method: req.method(), body: req.postDataJSON() });
      assert.equal(isViewAs, false, 'View-As performs no writes');
      assert.equal(req.method(), 'POST');
      assert.equal(url.pathname, `/api/module025/sow-gsd/${record.engagementId}/transfer`);
      const destination = isOwnerSession ? saA : saB;
      assert.deepEqual(req.postDataJSON(), { targetOwnerUserId: destination.userId, expectedRevision: record.revision,
        reason: isOwnerSession ? 'Specialist takeover: continue the reviewed scope.' : 'PTO coverage: complete the customer scope review.' });
      if (isOwnerSession) {
        await new Promise(resolve => { completeDeferredTransfer = resolve; signalDeferredTransfer(); });
      }
      record = { ...record, ownerUserId: destination.userId, ownerDisplayName: destination.displayName, revision: record.revision + 1 };
      body = { status: 'module025_ownership_transferred', engagementId: record.engagementId, ownerUserId: destination.userId, ownerDisplayName: destination.displayName, revision: record.revision, stateChanged: true };
    } else if (url.pathname.endsWith('/bootstrap')) {
      body = { currentUser: { userId: isOwnerSession ? saB.userId : '55555555-5555-4555-8555-555555555555', displayName: isOwnerSession ? saB.displayName : 'System SA manager' },
        access: { canCreate: isOwnerSession && !isViewAs, isManager: !isOwnerSession, isSolutionArchitect: isOwnerSession, managerScopeReadOnly: !isOwnerSession, isViewAs },
        solutionArchitects: [saA, saB], commercialModels: [], customerPrograms: [] };
    } else if (url.pathname.endsWith('/transfer-options')) {
      openedOptions.push(url.pathname);
      body = { engagementId: record.engagementId, revision: record.revision, canTransfer: !isViewAs && ['draft','review_ready'].includes(record.status), scope: 'same_reporting_manager',
        blockedReason: isViewAs ? 'Your role or current view does not allow transferring this record.' : null,
        destinations: isViewAs ? [] : [record.ownerUserId === saA.userId ? saB : saA] };
    } else if (url.pathname.endsWith('/history')) {
      body = { events: [] };
    } else if (url.pathname === `/api/module025/sow-gsd/${record.engagementId}`) {
      details.push(record.engagementId);
      body = { engagement: record, access: { canEdit: isOwnerSession && !isViewAs, canArchive: isOwnerSession && !isViewAs, readOnlyManagerView: !isOwnerSession, isViewAs } };
    } else if (url.pathname.endsWith('/team-work') || url.pathname === '/api/module025/sow-gsd') {
      lists.push(url.pathname + url.search);
      const owner = url.searchParams.get('ownerUserId'), search = (url.searchParams.get('search') || '').toLowerCase();
      const rows = [record, other].filter(item => (item.status === 'archived') === (url.searchParams.get('state') === 'archived') && (!owner || item.ownerUserId === owner)
        && (!search || `${item.projectName} ${item.engagementNumber} ${item.customerName}`.toLowerCase().includes(search)));
      body = { engagements: rows, totalCount: rows.length, truncated: false };
    } else throw new Error(`Unexpected request: ${req.method()} ${url.pathname}`);
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) });
  });

  await page.goto(`http://127.0.0.1:${server.httpServer.address().port}/__team`);
  const audience = page.getByRole('navigation', { name: 'Workspace audience' });
  await page.locator('.m025-work-card').filter({ hasText: 'SOW-TEAM-001' }).waitFor();
  assert.equal(await audience.getByRole('button', { name: 'Team Work', exact: true }).getAttribute('aria-pressed'), 'true', 'manager starts in Team Work');
  assert.ok(lists.some(url => url.startsWith('/api/module025/sow-gsd/team-work?') && !url.includes('ownerUserId')), 'team request uses server-scoped team endpoint');
  assert.equal(await page.locator('.m025-work-card').count(), 2);
  const owner = page.locator('.m025-filters select');
  assert.deepEqual(await owner.locator('option').allTextContents(), ['All authorized team members', 'Alex System', 'Bea System'], 'filter contains only supplied authorized SAs');
  await owner.selectOption(saB.userId);
  await page.locator('.m025-work-card').filter({ hasText: 'SOW-TEAM-001' }).waitFor({ state: 'detached' });
  assert.equal(await page.locator('.m025-work-card').count(), 1);
  assert.ok(lists.some(url => url.includes(`ownerUserId=${saB.userId}`)));
  await owner.selectOption('__team__');
  await page.locator('.m025-work-card').filter({ hasText: 'SOW-TEAM-001' }).waitFor();
  await page.getByPlaceholder('SOW-2026-000123 or customer…').fill('Datacenter');
  await page.locator('.m025-work-card').filter({ hasText: 'SOW-TEAM-002' }).waitFor({ state: 'detached' });
  assert.ok(lists.some(url => url.includes('search=Datacenter')));
  await page.getByPlaceholder('SOW-2026-000123 or customer…').fill('');
  await page.locator('.m025-work-card').filter({ hasText: 'SOW-TEAM-002' }).waitFor();
  await page.locator('.m025-work-card').filter({ hasText: 'SOW-TEAM-001' }).click();
  const editor = page.locator('.m025-editor-panel');
  await editor.getByText('Manager view · read only', { exact: true }).waitFor();
  assert.equal(await editor.getByLabel(/^Project Name/).isDisabled(), true, 'manager can inspect content without editing it');
  const actions = page.getByRole('region', { name: 'Documents and ConnectWise SELL', exact: true });
  for (const name of ['Download draft SOW', 'Download draft GSD', 'Download SOW (.docx)', 'Download GSD (.xlsx)', 'Send to ConnectWise SELL', 'Version history'])
    await actions.getByRole('button', { name, exact: true }).waitFor();
  await actions.getByRole('button', { name: 'Version history', exact: true }).click();
  assert.equal(await page.locator('#opened-register').textContent(), record.engagementId, 'handoff/history opens same immutable record');
  assert.equal(await editor.getByRole('button', { name: 'Archive SOW / GSD', exact: true }).isDisabled(), true);
  assert.equal(await editor.getByRole('button', { name: 'Review Requirements to Confirm', exact: true }).isDisabled(), true);
  const transfer = page.locator('.m025-transfer');
  await transfer.locator('summary').click();
  const target = transfer.getByLabel(/^New responsible Solution Architect/);
  await target.waitFor();
  assert.deepEqual(await target.locator('option').allTextContents(), ['Choose a teammate', 'Bea System · System SA']);
  const submit = transfer.getByRole('button', { name: 'Transfer this SOW / GSD', exact: true });
  assert.equal(await submit.isDisabled(), true, 'destination and reason required');
  await target.selectOption(saB.userId);
  assert.equal(await submit.isDisabled(), true, 'reason required even after choosing destination');
  await transfer.getByLabel('Handoff reason and context', { exact: true }).fill('  PTO coverage: complete the customer scope review.  ');
  await submit.click();
  await page.getByText('Ownership transferred to Bea System. The same record and history are retained.', { exact: true }).waitFor();
  await editor.getByText('Select a SOW/GSD package', { exact: true }).waitFor();
  await page.locator('.m025-work-card').filter({ hasText: 'SOW-TEAM-001' }).getByText(/SA: Bea System/).waitFor();
  assert.equal(writes.length, 1, 'one audited transfer; no content save or generation');
  await page.locator('.m025-work-card').filter({ hasText: 'SOW-TEAM-001' }).click();
  await editor.getByText(/Owned by Bea System · Revision 8/).waitFor();
  assert.ok(details.length >= 2 && details.every(id => id === record.engagementId), 'transfer refresh preserves stable engagement ID');
  assert.ok(openedOptions.length >= 2, 'permissions/options reload after transfer');

  // Canonical lifecycle controls remain discoverable for each record state.
  record = { ...record, status: 'draft', lastGeneratedAt: null };
  await page.reload(); await page.locator('.m025-work-card').filter({ hasText: 'SOW-TEAM-001' }).click();
  assert.equal(await editor.getByRole('button', { name: 'Delete Draft', exact: true }).isDisabled(), true);
  record = { ...record, status: 'confirmed', lastGeneratedAt: '2026-09-19T00:00:00Z' };
  await page.reload(); await page.locator('.m025-work-card').filter({ hasText: 'SOW-TEAM-001' }).click();
  assert.equal(await editor.getByRole('button', { name: 'Reopen for editing', exact: true }).isDisabled(), true);
  assert.equal(await actions.getByRole('button', { name: 'Download SOW (.docx)', exact: true }).isEnabled(), true);
  record = { ...record, status: 'archived', isActive: false };
  await page.reload(); await page.getByRole('button', { name: 'Archived', exact: true }).click();
  await page.locator('.m025-work-card').filter({ hasText: 'SOW-TEAM-001' }).click();
  assert.equal(await editor.getByRole('button', { name: 'Return to Active', exact: true }).isDisabled(), true);

  isViewAs = true;
  record = { ...record, status: 'review_ready', isActive: true };
  await page.reload(); await page.locator('.m025-work-card').filter({ hasText: 'SOW-TEAM-001' }).click();
  await page.getByText('Administrator View-As is read-only', { exact: true }).waitFor();
  await transfer.locator('summary').click();
  await transfer.getByText('Your role or current view does not allow transferring this record.', { exact: true }).waitFor();
  assert.equal(await transfer.getByRole('button', { name: 'Transfer this SOW / GSD', exact: true }).count(), 0);
  assert.equal(await editor.getByLabel(/^Project Name/).isDisabled(), true);
  assert.equal(writes.length, 1, 'View-As inspection issues no writes');

  // An owner can hand off their draft, and a pending transfer freezes every edit path.
  isViewAs = false; isOwnerSession = true;
  await page.reload(); await page.locator('.m025-work-card').filter({ hasText: 'SOW-TEAM-001' }).click();
  assert.equal(await audience.getByRole('button', { name: 'My Work', exact: true }).getAttribute('aria-pressed'), 'true');
  assert.equal(await audience.getByRole('button', { name: 'Team Work', exact: true }).count(), 0);
  assert.equal(await editor.getByLabel(/^Project Name/).isEnabled(), true);
  await transfer.locator('summary').click();
  await target.selectOption(saA.userId);
  await transfer.getByLabel('Handoff reason and context', { exact: true }).fill('Specialist takeover: continue the reviewed scope.');
  await submit.click();
  await deferredTransferStarted;
  assert.equal(await editor.getByLabel(/^Project Name/).isDisabled(), true, 'owner editor locked while transfer is pending');
  assert.equal(await audience.getByRole('button', { name: 'Templates', exact: true }).isDisabled(), true, 'template navigation locked while transferring');
  assert.equal(await audience.getByRole('button', { name: 'My Work', exact: true }).isDisabled(), true, 'queue navigation locked while transferring');
  assert.equal(await page.getByRole('button', { name: 'New SOW / GSD', exact: true }).isDisabled(), true);
  assert.equal(await actions.getByRole('button', { name: 'Download draft GSD', exact: true }).isDisabled(), true);
  assert.equal(await page.locator('.m025-work-card').filter({ hasText: 'SOW-TEAM-002' }).isDisabled(), true, 'other records cannot open during transfer');
  completeDeferredTransfer();
  await page.getByText('Ownership transferred to Alex System. The same record and history are retained.', { exact: true }).waitFor();
  await page.locator('.m025-work-card').filter({ hasText: 'SOW-TEAM-001' }).waitFor({ state: 'detached' });
  assert.equal(writes.length, 2, 'only the two explicit transfers changed state');
  assert.deepEqual(errors, []);
  console.log('MODULE025_TEAM_WORKSPACE=PASS managerDefault=team filters=serverScoped transfer=stableRecord lifecycle=retained viewAsWrites=0 transferLock=verified');
  await context.close();
} finally { await browser.close(); await server.close(); }
