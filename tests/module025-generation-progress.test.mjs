import assert from 'node:assert/strict';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { createRequire } from 'node:module';
import { generationPhases, generationSeconds } from '../src/frontend/project-time-web/src/module025/generation-progress.js';

const phases = ['Plan', 'Design', 'Implement', 'Validate', 'Release'];
const active = { generationId: 'job-1', terminal: false, elapsedSeconds: 100, phaseTimeline: [
  { phase: 'Plan', status: 'completed', startedAt: '2026-09-20T00:00:00Z', elapsedSeconds: 30 },
  { phase: 'Design', status: 'running', startedAt: '2026-09-20T00:00:30Z', elapsedSeconds: 10 }
] };
assert.equal(generationSeconds(active, 1000, 3500), 102, 'overall timer advances from server duration, independent of server clock offset');
assert.equal(generationSeconds({ ...active, terminal: true }, 1000, 3500), 100, 'terminal overall duration freezes');
assert.equal(generationPhases(active, 1000, 3500)[0].seconds, 30, 'saved phase timer freezes');
assert.equal(generationPhases(active, 1000, 3500)[1].seconds, 12, 'current phase advances');
assert.equal(generationPhases(active, 1000, 3500, false)[1].seconds, 10, 'unobserved phase does not claim continuing execution');
assert.equal(generationPhases(active, 1000, 3500)[2].seconds, null, 'pending phase has no invented duration');
assert.equal(generationPhases({ phaseTimeline: [{ phase: 'Plan', status: 'resumed', elapsedSeconds: null }] }, 0)[0].seconds, null, 'reused checkpoint has no invented duration');

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const web = path.join(root, 'src/frontend/project-time-web');
const require = createRequire(path.join(web, 'package.json'));
const { createServer } = await import(path.join(web, 'node_modules/vite/dist/node/index.js'));
const { default: react } = await import(path.join(web, 'node_modules/@vitejs/plugin-react/dist/index.js'));
const { chromium } = require(process.env.PLAYWRIGHT_MODULE_PATH || 'playwright');
const harness = '<div id="root"></div><script type="module">import React from"react";import{createRoot}from"react-dom/client";import Workspace from"/src/module025/SowGsdAuthoringWorkspace.jsx";window.testRoot=createRoot(document.getElementById("root"));window.testRoot.render(React.createElement(Workspace));</script>';
const server = await createServer({ configFile: false, root: web, plugins: [react(), {
  name: 'module025-generation-harness', configureServer(vite) {
    vite.middlewares.use('/__generation', async (req, res) => { res.setHeader('Content-Type', 'text/html'); res.end(await vite.transformIndexHtml('/__generation', harness)); });
  }
}], server: { host: '127.0.0.1', port: 0 } });
await server.listen();
const browser = await chromium.launch({ headless: true, ...(process.env.MODULE025_TEST_CHROMIUM ? { executablePath: process.env.MODULE025_TEST_CHROMIUM, args: ['--no-sandbox', '--disable-dev-shm-usage'] } : {}) });
try {
  const context = await browser.newContext();
  const page = await context.newPage();
  await page.clock.install({ time: new Date('2026-09-20T01:00:00Z') });
  const errors = [], writes = [];
  page.on('pageerror', error => errors.push(error.message));
  let record = { engagementId: 'fixture-025', engagementNumber: 'SOW-TEST-025', projectName: 'CUCM upgrade', customerName: 'Synthetic customer',
    ownerUserId: 'sa', ownerDisplayName: 'Test SA', status: 'draft', isActive: true, revision: 1, phases: [],
    serviceOverview: 'Upgrade Cisco CUCM from 14.0 to 15.0', lastGeneratedAt: null };
  let job = null, viewAs = false, statusFails = false, statusReads = 0, detailReads = 0, completeDuringDiscovery = false, includeSecondRecord = false, holdPoll = false, releaseHeldPoll, markPollHeld;
  const secondRecord = { ...record, engagementId: 'fixture-026', engagementNumber: 'SOW-TEST-026', projectName: 'Second record' };
  const pollHeld = new Promise(resolve => { markPollHeld = resolve; });
  const timeline = (index, resumed = false) => phases.map((phase, i) => ({ phase, ordinal: i + 1,
    status: i < index ? resumed && i === 0 ? 'resumed' : 'completed' : i === index ? 'running' : 'pending',
    startedAt: i <= index && !(resumed && i === 0) ? '2026-09-20T00:00:00Z' : null,
    elapsedSeconds: i < index ? resumed && i === 0 ? null : 30 : i === index ? 10 : null, resumed: resumed && i === 0 }));
  const running = (index, generationId = 'job-1', resumed = false) => ({ generationId, engagementId: record.engagementId,
    status: 'module025_detailed_scope_generation_running', stage: 'phase', terminal: false, currentRevision: record.revision,
    currentPhase: phases[index], phaseTimeline: timeline(index, resumed), elapsedSeconds: 100,
    serverNow: '2026-09-20T00:02:00Z', deadlineAt: '2026-09-20T00:40:00Z' });
  await page.route('**/api/**', async route => {
    const req = route.request(), pathname = new URL(req.url()).pathname;
    let body;
    if (req.method() !== 'GET') writes.push({ method: req.method(), pathname });
    if (pathname.endsWith('/bootstrap')) body = { currentUser: { userId: 'sa' }, access: { canCreate: !viewAs, isSolutionArchitect: true, isViewAs: viewAs }, solutionArchitects: [{ userId: 'sa', displayName: 'Test SA' }], commercialModels: [], customerPrograms: [] };
    else if (pathname.endsWith('/generate')) { assert.equal(req.method(), 'POST'); job = running(writes.length === 1 ? 0 : 1, `job-${writes.length}`, writes.length > 1); body = { ...job, status: 'module025_detailed_scope_generation_queued' }; }
    else if (pathname.includes('/generations/')) {
      statusReads++;
      if (holdPoll && !pathname.endsWith('/latest')) { await new Promise(resolve => { releaseHeldPoll = resolve; markPollHeld(); }); }
      if (pathname.includes('/fixture-026/')) { await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ status: 'module025_no_generation', generationId: null, terminal: true }) }); return; }
      if (statusFails) { await route.fulfill({ status: 503, contentType: 'application/json', body: JSON.stringify({ message: 'Synthetic status interruption.' }) }); return; }
      body = job || { status: 'module025_no_generation', generationId: null, terminal: true, currentRevision: record.revision, phaseTimeline: [] };
    }
    else if (pathname.endsWith('/transfer-options')) body = { canTransfer: false, teamMembers: [] };
    else if (pathname.endsWith('/fixture-026')) body = { engagement: secondRecord, access: { canEdit: false, isViewAs: true } };
    else if (pathname.endsWith('/fixture-025')) { detailReads++; body = { engagement: record, access: { canEdit: !viewAs, isViewAs: viewAs, canArchive: !viewAs } }; if (completeDuringDiscovery) { completeDuringDiscovery = false; record = { ...record, revision: record.revision + 1 }; job = { ...job, currentRevision: record.revision }; } }
    else body = { engagements: includeSecondRecord ? [record, secondRecord] : [record] };
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) });
  });
  const open = async () => { await page.goto(`http://127.0.0.1:${server.httpServer.address().port}/__generation`); await page.locator('.m025-work-card').filter({ hasText: 'SOW-TEST-025' }).click(); };
  const progress = page.getByRole('region', { name: 'AI generation progress', exact: true });
  const generate = page.getByRole('button', { name: 'Generate detailed scope', exact: true });
  await open();
  await progress.getByText('One scope. Five connected phases.', { exact: true }).waitFor();
  assert.equal(writes.length, 0, 'opening a draft never starts paid AI work');
  assert.equal(await progress.locator('li').count(), 5);
  await generate.click();
  await progress.getByText('Plan · 1 of 5', { exact: true }).waitFor();
  await progress.getByRole('timer', { name: 'Plan elapsed time', exact: true }).getByText('0m 10s', { exact: true }).waitFor();
  assert.equal(await page.locator('textarea.m025-service-overview').isDisabled(), true, 'scope is immutable while job is running');
  await page.clock.fastForward(2000);
  assert.equal(await progress.getByRole('timer', { name: 'Plan elapsed time', exact: true }).innerText(), '0m 12s');
  assert.equal(writes.length, 1, 'one Generate click queues one job');
  job = running(1);
  await page.clock.fastForward(5000);
  await progress.getByText('Design · 2 of 5', { exact: true }).waitFor();
  assert.equal(await progress.getByRole('timer', { name: 'Plan elapsed time', exact: true }).innerText(), '0m 30s');
  if (process.env.MODULE025_PROGRESS_SCREENSHOT) await progress.screenshot({ path: process.env.MODULE025_PROGRESS_SCREENSHOT });

  // Reopening discovers the same job using GET only and resumes its timers.
  await open();
  await progress.getByText('Design · 2 of 5', { exact: true }).waitFor();
  assert.equal(writes.length, 1);
  job = { ...job, status: 'module025_detailed_scope_failed', terminal: true, stage: 'failed', canResume: true, canRetry: true,
    phaseTimeline: timeline(1).map(row => row.phase === 'Design' ? { ...row, status: 'failed', elapsedSeconds: 21 } : row), elapsedSeconds: 121, message: 'Synthetic provider deadline.' };
  await page.clock.fastForward(5000);
  const resume = progress.getByRole('button', { name: 'Resume remaining phases', exact: true });
  await resume.waitFor();
  await page.clock.fastForward(3000);
  assert.equal(await progress.getByRole('timer', { name: 'Design elapsed time', exact: true }).innerText(), '0m 21s', 'failed timer freezes');
  assert.equal(writes.length, 1, 'failed discovery never restarts AI work');
  await resume.click();
  await progress.getByText('Design · 2 of 5', { exact: true }).waitFor();
  await progress.getByText('Saved phase reused', { exact: true }).waitFor();
  assert.equal(await progress.getByRole('timer', { name: 'Plan elapsed time', exact: true }).count(), 0);
  assert.equal(writes.length, 2, 'resume is explicit');
  for (let i = 2; i < 5; i++) {
    job = running(i, 'job-2', true);
    await page.clock.fastForward(5000);
    await progress.getByText(`${phases[i]} · ${i + 1} of 5`, { exact: true }).waitFor();
  }
  job = { ...job, stage: 'assembly', phaseTimeline: timeline(5, true) };
  await page.clock.fastForward(5000);
  await progress.getByText('Preparing SOW and GSD', { exact: true }).waitFor();
  assert.equal(await page.getByRole('button', { name: 'Generating detailed scope…', exact: true }).isDisabled(), true, 'all saved phases are not completion until assembly finishes');
  const beforeCompletion = detailReads;
  record = { ...record, revision: 2, lastGeneratedAt: '2026-09-20T00:04:00Z', status: 'review_ready' };
  job = { ...job, terminal: true, stage: 'completed', status: 'module025_detailed_scope_generated', currentRevision: 2, elapsedSeconds: 240 };
  await page.clock.fastForward(5000);
  await progress.getByText('SOW and GSD draft ready for review', { exact: true }).waitFor();
  await page.getByText(/Owned by Test SA · Revision 2/).waitFor();
  assert.equal(detailReads, beforeCompletion + 1, 'successful terminal poll refreshes saved draft exactly once');
  await page.clock.fastForward(10000);
  assert.equal(detailReads, beforeCompletion + 1);
  assert.equal(writes.length, 2, 'phase progression and document assembly do not cause more frontend AI requests');

  // Completion between detail GET and latest GET must refresh stale editor content once.
  completeDuringDiscovery = true;
  const beforeRace = detailReads;
  await open();
  await page.getByText(/Owned by Test SA · Revision 3/).waitFor();
  assert.equal(detailReads, beforeRace + 2, 'discovery refreshes a newer completed draft once');
  assert.equal(writes.length, 2);

  // Normal review/confirmation edits preserve historical success without a false error.
  job = { ...job, status: 'module025_previous_generation_completed', stage: 'obsolete', previousGenerationCompleted: true, canResume: false, canRetry: false };
  await open();
  await progress.getByText('Previous generation', { exact: true }).waitFor();
  assert.equal(await progress.getByText('Generation needs attention', { exact: true }).count(), 0);
  assert.equal(await progress.getByRole('button', { name: 'Resume remaining phases', exact: true }).count(), 0);
  job = { ...job, status: 'module025_detailed_scope_generated', stage: 'completed', previousGenerationCompleted: false };

  // An unavailable status endpoint must not strand users or hide retained documents.
  statusFails = true; record = { ...record, status: 'confirmed' };
  await open();
  await progress.getByRole('alert').waitFor();
  assert.equal(await page.getByRole('button', { name: 'Download SOW (.docx)', exact: true }).isEnabled(), true);
  assert.equal(await page.getByRole('button', { name: 'Archived', exact: true }).isEnabled(), true);
  statusFails = false;
  await progress.getByRole('button', { name: 'Check generation status', exact: true }).click();
  await progress.getByText('SOW and GSD draft ready for review', { exact: true }).waitFor();
  assert.equal(writes.length, 2);

  // A read-only identity may inspect a failed run but cannot resume it.
  viewAs = true; record = { ...record, status: 'draft' };
  job = { ...job, status: 'module025_detailed_scope_failed', stage: 'failed', canResume: true, canRetry: true };
  await open();
  await resume.waitFor();
  assert.equal(await resume.isDisabled(), true);
  assert.equal(writes.length, 2);

  // A late response from another record must never repaint the current record.
  includeSecondRecord = true; holdPoll = true; job = running(2);
  await open();
  await pollHeld;
  await page.locator('.m025-work-card').filter({ hasText: 'SOW-TEST-026' }).click();
  await progress.getByText('One scope. Five connected phases.', { exact: true }).waitFor();
  releaseHeldPoll(); holdPoll = false;
  await page.clock.fastForward(1000);
  assert.equal(await progress.getByText('Implement · 3 of 5', { exact: true }).count(), 0, 'late old-record response is ignored');
  assert.equal(writes.length, 2);
  includeSecondRecord = false;

  // Leaving a running record must cancel future polling.
  job = running(2); await open();
  await progress.getByText('Implement · 3 of 5', { exact: true }).waitFor();
  await page.evaluate(() => window.testRoot.unmount());
  const beforeUnmount = statusReads;
  await page.clock.fastForward(20000);
  assert.equal(statusReads, beforeUnmount, 'unmount cleans up polling');
  assert.deepEqual(errors, []);
  await context.close();
  console.log('MODULE025_GENERATION_PROGRESS=PASS sequential=5 timers=durable resume=explicit refresh=read-only assembly=distinct viewAsWrites=0 cleanup=verified');
} finally { await browser.close(); await server.close(); }
