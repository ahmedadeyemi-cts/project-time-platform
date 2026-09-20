import assert from 'node:assert/strict';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { createRequire } from 'node:module';
import { formatTargetDate, isTrackingOverdue, trackingIssues, validTargetDate } from '../src/frontend/project-time-web/src/module025/work-tracking.js';

assert.equal(validTargetDate('2026-02-29'), false);
assert.equal(validTargetDate('2028-02-29'), true);
assert.equal(isTrackingOverdue('2026-09-19', '2026-09-19'), false);
assert.equal(isTrackingOverdue('2026-09-18', '2026-09-19'), true);
assert.equal(formatTargetDate(''), 'Not set');
const base = { targetDate: '', priority: 'normal', blockerReason: '', blockerOwnerUserId: '', authoringHours: '' };
assert.deepEqual(trackingIssues(base), []);
assert.deepEqual(trackingIssues({ ...base, authoringHours: '0' }), []);
assert.ok(trackingIssues({ ...base, authoringHours: '-1' }).length);
assert.ok(trackingIssues({ ...base, authoringHours: '0.001' }).length);
assert.ok(trackingIssues({ ...base, blockerReason: 'Missing input' }).length);
assert.ok(trackingIssues({ ...base, blockerOwnerUserId: 'manager' }).length);

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const web = path.join(root, 'src/frontend/project-time-web');
const require = createRequire(path.join(web, 'package.json'));
const { createServer } = await import(path.join(web, 'node_modules/vite/dist/node/index.js'));
const { default: react } = await import(path.join(web, 'node_modules/@vitejs/plugin-react/dist/index.js'));
const { chromium } = require(process.env.PLAYWRIGHT_MODULE_PATH || 'playwright');
const harness = `<div id="root"></div><script type="module">
import React,{useState}from"react";import{createRoot}from"react-dom/client";import Panel from"/src/module025/WorkTrackingPanel.jsx";
async function request(url,options){const r=await fetch(url,options);const data=await r.json();if(!r.ok){const e=new Error(data.message);e.status=r.status;throw e;}return data;}
function Harness(){const[dirty,setDirty]=useState(false),[busy,setBusy]=useState(false),[saved,setSaved]=useState(''),[identity,setIdentity]=useState('owner'),[visible,setVisible]=useState(true);
return React.createElement(React.Fragment,null,React.createElement('output',{id:'parent-state'},JSON.stringify({dirty,busy,saved})),React.createElement('button',{onClick:()=>setIdentity('view-as')},'Switch to View-As'),React.createElement('button',{onClick:()=>setVisible(false)},'Unmount panel'),visible&&React.createElement(Panel,{engagementId:'tracking-fixture',identityKey:identity,request,readOnly:identity==='view-as',onDirtyChanged:setDirty,onBusyChanged:setBusy,onSaved:r=>setSaved(String(r.tracking.revision))}));}
createRoot(document.getElementById('root')).render(React.createElement(Harness));</script>`;
const server = await createServer({ configFile: false, root: web, cacheDir: path.join(web, 'node_modules/.vite-module025-tracking-harness'), plugins: [react(), {
  name: 'work-tracking-harness', configureServer(vite) {
    vite.middlewares.use('/__tracking', async (req, res) => { res.setHeader('Content-Type', 'text/html'); res.end(await vite.transformIndexHtml('/__tracking', harness)); });
  }
}], server: { host: '127.0.0.1', port: 0 } });
await server.listen();
const browser = await chromium.launch({ headless: true, ...(process.env.MODULE025_TEST_CHROMIUM ? { executablePath: process.env.MODULE025_TEST_CHROMIUM, args: ['--no-sandbox', '--disable-dev-shm-usage'] } : {}) });
try {
  const context = await browser.newContext({ locale: 'en-US', timezoneId: 'Pacific/Honolulu' });
  const page = await context.newPage();
  await page.clock.install({ time: new Date('2026-09-20T00:30:00Z') });
  const errors = [], writes = [];
  page.on('pageerror', error => errors.push(error.message));
  let tracking = { revision: 2, targetDate: '2026-09-19', priority: 'normal', blockerReason: '', blockerOwnerUserId: null,
    authoringHours: null, updatedAt: '2026-09-18T12:00:00Z', workflowIdleDays: 2, lastWorkflowActivityAt: '2026-09-17T12:00:00Z' };
  let canEdit = true, conflictNext = false, releaseSave, markSaveStarted;
  const saveStarted = new Promise(resolve => { markSaveStarted = resolve; });
  const payload = () => ({ schemaReady: true, canEdit, tracking, workflowIdleDays: 2, lastWorkflowActivityAt: '2026-09-17T12:00:00Z', blockerOwners: [{ userId: 'owner', displayName: 'Owning SA' }, { userId: 'manager', displayName: 'Reporting manager' }],
    history: [{ ...tracking, actorDisplayName: 'Owning SA' }] });
  await page.route('**/api/**', async route => {
    const req = route.request();
    assert.equal(new URL(req.url()).pathname, '/api/module025/sow-gsd/tracking-fixture/work-tracking', 'tracking never writes document content');
    if (req.method() === 'PUT') {
      const body = req.postDataJSON(); writes.push(body);
      if (conflictNext) { conflictNext = false; await route.fulfill({ status: 409, contentType: 'application/json', body: JSON.stringify({ message: 'Tracking revision changed.' }) }); return; }
      assert.equal(body.expectedRevision, tracking.revision, 'tracking revision is independent from document revisions');
      if (writes.length === 1) await new Promise(resolve => { releaseSave = resolve; markSaveStarted(); });
      tracking = { ...tracking, ...body, blockerOwnerDisplayName: body.blockerOwnerUserId === 'manager' ? 'Reporting manager' : null, revision: tracking.revision + 1, updatedAt: '2026-09-20T00:30:00Z' };
    } else assert.equal(req.method(), 'GET');
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(payload()) });
  });
  await page.goto(`http://127.0.0.1:${server.httpServer.address().port}/__tracking`);
  const panel = page.getByRole('region', { name: 'Work tracking', exact: true });
  await panel.getByText('Tracking revision 2', { exact: true }).waitFor();
  assert.ok((await panel.locator('.m025-tracking__summary').innerText()).includes('Sep 19, 2026'), 'date-only target retains its day west of UTC');
  assert.equal(await panel.locator('.is-overdue').count(), 0, 'local target date today is not overdue');
  assert.equal(await panel.getByLabel('Remaining SA authoring effort (hours)', { exact: true }).inputValue(), '', 'unknown authoring effort stays blank');
  assert.ok((await panel.innerText()).includes('separate from project delivery LOE'));
  const target = panel.getByLabel('Target date', { exact: true });
  const priority = panel.getByLabel(/^Priority/);
  const blocker = panel.getByLabel('What is blocking progress?', { exact: true });
  const responsible = panel.getByLabel(/^Person responsible for resolving the blocker/);
  const save = panel.getByRole('button', { name: 'Save work tracking', exact: true });
  await target.fill('2026-09-18');
  await priority.selectOption('urgent');
  await panel.getByLabel('Remaining SA authoring effort (hours)', { exact: true }).fill('2.75');
  await blocker.fill('Waiting for the customer maintenance-window decision.');
  await save.click();
  await panel.getByRole('alert').getByText(/both an explanation and a responsible person/).waitFor();
  assert.equal(writes.length, 0, 'incomplete blocker does not reach the server');
  await responsible.selectOption('manager');
  await save.click();
  await saveStarted;
  assert.equal(await target.isDisabled(), true, 'inputs freeze while a tracking save is pending');
  await page.locator('#parent-state').filter({ hasText: '"busy":true' }).waitFor();
  let parent = JSON.parse(await page.locator('#parent-state').textContent());
  assert.deepEqual({ dirty: parent.dirty, busy: parent.busy }, { dirty: true, busy: true });
  assert.deepEqual(writes[0], { expectedRevision: 2, targetDate: '2026-09-18', priority: 'urgent', blockerReason: 'Waiting for the customer maintenance-window decision.', blockerOwnerUserId: 'manager', authoringHours: 2.75 });
  releaseSave();
  await panel.getByText('Work tracking saved. The SOW/GSD document revision is unchanged.', { exact: true }).waitFor();
  parent = JSON.parse(await page.locator('#parent-state').textContent());
  assert.deepEqual(parent, { dirty: false, busy: false, saved: '3' });
  assert.ok((await panel.locator('.is-overdue').innerText()).includes('Overdue'));
  await panel.getByText('Recent tracking history (1, up to 30 shown)', { exact: true }).click();
  await panel.locator('.m025-tracking__history').getByText('Owning SA', { exact: true }).waitFor();
  assert.ok((await panel.locator('.m025-tracking__history').innerText()).includes('Blocker owner: Reporting manager'));
  assert.ok((await panel.locator('.m025-tracking__history').innerText()).includes('SA authoring remaining: 2.75h'));

  conflictNext = true;
  tracking = { ...tracking, revision: 4, priority: 'high' }; // A manager saved another revision.
  await priority.selectOption('low');
  await save.click();
  await panel.getByRole('alert').getByText(/Your entries are still shown/).waitFor();
  assert.equal(await priority.inputValue(), 'low', 'a conflict preserves the local proposal for inspection');
  assert.equal(await save.isDisabled(), true, 'conflicted revision cannot be blindly resubmitted');
  await panel.getByRole('button', { name: 'Reload saved tracking (discard my entries)', exact: true }).click();
  await panel.getByText('Tracking revision 4', { exact: true }).waitFor();
  assert.equal(await priority.inputValue(), 'high', 'explicit reload adopts committed revision');
  await panel.getByRole('button', { name: 'Clear blocker', exact: true }).click();
  await panel.getByLabel('Remaining SA authoring effort (hours)', { exact: true }).fill('0');
  await save.click();
  await panel.getByText('Tracking revision 5', { exact: true }).waitFor();
  assert.equal(tracking.blockerReason, ''); assert.equal(tracking.blockerOwnerUserId, null);
  assert.equal(tracking.authoringHours, 0, 'explicit zero remains distinct from unknown');
  await panel.getByText('Recent tracking history (1, up to 30 shown)', { exact: true }).click();
  assert.ok((await panel.locator('.m025-tracking__history').innerText()).includes('SA authoring remaining: 0h'),'history preserves explicit zero');
  assert.ok((await panel.locator('.m025-tracking__history').innerText()).includes('No blocker recorded.'),'history exposes blocker clearance');

  await blocker.fill('Unsaved owner note');
  await page.locator('#parent-state').filter({hasText:'"dirty":true'}).waitFor();
  assert.equal(JSON.parse(await page.locator('#parent-state').textContent()).dirty, true);
  canEdit = false;
  await page.getByRole('button', { name: 'Switch to View-As', exact: true }).click();
  await panel.getByText('Tracking revision 5', { exact: true }).waitFor();
  assert.equal(await blocker.inputValue(), '', 'identity switch clears prior identity edits');
  assert.equal(await target.isDisabled(), true); assert.equal(await save.isDisabled(), true);
  assert.equal(JSON.parse(await page.locator('#parent-state').textContent()).dirty, false);
  const countBeforeReadOnly = writes.length;
  await page.getByRole('button', { name: 'Unmount panel', exact: true }).click();
  parent = JSON.parse(await page.locator('#parent-state').textContent());
  assert.equal(parent.dirty, false); assert.equal(parent.busy, false);
  assert.equal(writes.length, countBeforeReadOnly, 'View-As and unmount do not mutate tracking');
  assert.deepEqual(errors, []);
  console.log('MODULE025_WORK_TRACKING=PASS localDates=stable unknownHours=preserved metadataRevision=independent conflict=reviewable busyGuard=verified viewAsWrites=0');
  await context.close();
} finally { await browser.close(); await server.close(); }
