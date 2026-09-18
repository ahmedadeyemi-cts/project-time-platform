import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import path from 'node:path';
import { pathToFileURL, fileURLToPath } from 'node:url';
const repo = fileURLToPath(new URL('../', import.meta.url));
const web = path.join(repo, 'src/frontend/project-time-web');
const { createServer } = await import(pathToFileURL(path.join(web, 'node_modules/vite/dist/node/index.js')));
const { default: react } = await import(pathToFileURL(path.join(web, 'node_modules/@vitejs/plugin-react/dist/index.js')));
const { chromium } = await import(pathToFileURL(process.env.MODULE019_PLAYWRIGHT_PATH || path.join(process.env.CODEX_PRIMARY_RUNTIME_NODE_MODULES, 'playwright/index.mjs')));
const temp = await fs.mkdtemp(path.join(web, '.module019-browser-'));
await fs.writeFile(path.join(temp, 'index.html'), '<html><head><meta name="viewport" content="width=device-width,initial-scale=1" /></head><body style="margin:0;background:#edf2f6;font:16px Arial"><div id="root" style="max-width:1280px;margin:24px auto"></div><script type="module" src="/entry.jsx"></script></body></html>');
await fs.writeFile(path.join(temp, 'entry.jsx'), `import React from 'react'; import {createRoot} from 'react-dom/client'; import Workspace from '../src/ProjectWorkspaceCenter.jsx'; createRoot(document.getElementById('root')).render(<Workspace/>);`);
const server = await createServer({ configFile: false, root: temp, plugins: [react()], server: { host: '127.0.0.1', port: 5197, strictPort: true, fs: { allow: [web] } } });
await server.listen();
const browser = await chromium.launch({ headless: true, ...(process.env.MODULE019_CHROMIUM_PATH ? { executablePath: process.env.MODULE019_CHROMIUM_PATH, args: ['--no-sandbox', '--disable-dev-shm-usage'] } : {}) });
const page = await browser.newPage({ viewport: { width: 1360, height: 1020 } });
const errors = [];
page.on('pageerror', error => errors.push(error.message));
let invoiceReads = 0;
let overviewStatus = 200;
let downloadStatus = 200;
let invoiceStatus = 200;
let overviewOverride = null;
const data = {
  access: { userId: 'u1', isViewAs: false },
  projects: [{ id: 'p1', projectCode: 'PRO-001', projectName: 'Voice migration', clientName: 'Example Customer', status: 'active', projectManagerName: 'Project Manager', solutionArchitectName: 'Solution Architect' }, { id: 'p2', projectCode: 'PRO-002', projectName: 'Storage upgrade', clientName: 'Second Customer', status: 'active' }],
  resourceRequests: [{ requestNumber: 'SR-003', sourceName: 'Network assessment', projectIntakeRequestId: 'i3', requestedHours: 8, assignedEngineers: 'Engineer One', status: 'assigned' }],
  assignments: [
    { id: 'a1', projectId: 'p1', userId: 'u1', taskId: 't1', engineerName: 'Engineer One', taskCode: 'PLAN', taskName: 'Plan the migration', assignedHours: 10, usedHours: 12 },
    { id: 'a2', projectId: 'p1', userId: 'u2', taskId: 't2', engineerName: 'Engineer Two', taskCode: 'BUILD', taskName: 'Configure the platform', assignedHours: 10, usedHours: 8 },
    { id: 'a3', projectId: 'p2', userId: 'u1', taskId: 't3', engineerName: 'Engineer One', taskCode: 'UPGRADE', taskName: 'Upgrade storage', assignedHours: 25, usedHours: 5 }],
  teamHours: [{ projectId: 'p1', userId: 'u1', engineerName: 'Engineer One', loggedHours: 12 }, { projectId: 'p1', userId: 'u2', engineerName: 'Engineer Two', loggedHours: 8 }, { projectId: 'p1', userId: 'u3', engineerName: 'Previous Engineer', loggedHours: 3 }, { projectId: 'p2', userId: 'u1', engineerName: 'Engineer One', loggedHours: 5 }],
  documents: [{ id: 'd1', projectId: 'p1', originalFileName: 'Voice_Migration_SOW.docx', documentCategory: 'sow', sizeBytes: 5120, downloadUrl: '/api/project-workspace/documents/d1/download' }, { id: 'd2', projectId: 'p2', originalFileName: 'Storage_GSD.pdf', documentCategory: 'gsd', sizeBytes: 1024, downloadUrl: '/api/project-workspace/documents/d2/download' }, { id: 'd3', projectIntakeRequestId: 'i3', originalFileName: 'Assessment_Brief.docx', downloadUrl: '/api/project-workspace/documents/d3/download' }]
};
await page.addInitScript(() => localStorage.setItem('projectPulseAuthSession', JSON.stringify({ sessionToken: 'synthetic-test-only' })));
await page.route('**/api/**', async route => {
  const url = new URL(route.request().url());
  const headers = route.request().headers();
  const preview = Boolean(headers['x-projectpulse-view-as-user']);
  const json = value => route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(value) });
  if (url.pathname.endsWith('/view-as/users')) return json({ users: [{ userId: 'u2', displayName: 'Engineer Two' }] });
  if (url.pathname.endsWith('/overview')) {
    if (overviewStatus !== 200) return route.fulfill({ status: overviewStatus });
    return json(overviewOverride || { ...data, access: { userId: preview ? 'u2' : 'u1', isViewAs: preview } });
  }
  if (url.pathname.startsWith('/api/project-financials/')) {
    if (url.pathname.includes('/p2')) return route.fulfill({ status: 503 });
    return json({ project: { visibility: { commercial: false }, budgetStatus: 'over_budget', laborCost: null }, sources: [] });
  }
  if (url.pathname.includes('/invoices')) { invoiceReads++; if (invoiceStatus !== 200) return route.fulfill({ status: invoiceStatus }); return json({ invoices: [{ billingInvoiceId: 'inv1', invoiceNumber: 'INV-001', invoiceDate: '2026-09-18', invoiceStatus: 'finalized', totalAmount: 1200 }] }); }
  if (url.pathname.endsWith('/download')) {
    assert.equal(headers['x-projectpulse-session'], 'synthetic-test-only');
    if (downloadStatus !== 200) return route.fulfill({ status: downloadStatus, contentType: 'application/json', body: JSON.stringify({ message: 'This file is no longer available.' }) });
    return route.fulfill({ status: 200, contentType: 'application/octet-stream', headers: { 'Content-Disposition': 'attachment; filename="project-document.docx"' }, body: 'synthetic document' });
  }
  throw new Error(`Unexpected API request: ${url.pathname}`);
});
try {
  await page.goto('http://127.0.0.1:5197');
  await page.getByLabel('Select assigned work').selectOption('project:p1');
  await page.getByRole('heading', { name: 'Engineering assignments', exact: true }).waitFor();
  assert.equal(await page.getByText('Plan the migration', { exact: true }).count(), 1);
  assert.equal(await page.getByText('Configure the platform', { exact: true }).count(), 0);
  await page.getByRole('button', { name: 'All project tasks', exact: true }).click();
  await page.getByText('Configure the platform', { exact: true }).waitFor();
  assert.match(await page.locator('.workspace-notice').last().innerText(), /3 hours over/);
  assert.equal(await page.getByText('Source health', { exact: true }).count(), 0);
  assert.equal(await page.getByText('Project Workspace Readiness', { exact: true }).count(), 0);
  await page.screenshot({ path: '/tmp/module019-desktop.png', fullPage: true });
  await page.getByRole('button', { name: 'Project team', exact: true }).click();
  await page.getByText('Previous Engineer', { exact: true }).waitFor();
  await page.getByRole('button', { name: 'Documents', exact: true }).click();
  await page.getByText('Voice_Migration_SOW.docx', { exact: true }).waitFor();
  assert.equal(await page.getByText('Storage_GSD.pdf', { exact: true }).count(), 0);
  const download = page.waitForEvent('download');
  await page.getByRole('button', { name: 'Download', exact: true }).click();
  assert.equal((await download).suggestedFilename(), 'project-document.docx');
  await page.getByRole('button', { name: 'View', exact: true }).click();
  await page.getByText('Use Download to open this file in its native application.').waitFor();
  await page.keyboard.press('Escape');
  assert.equal(await page.getByRole('dialog').count(), 0);
  await page.getByRole('button', { name: 'Cost & billing', exact: true }).click();
  await page.getByText('INV-001', { exact: true }).waitFor();
  assert.equal(await page.getByText('Restricted or unavailable', { exact: true }).count(), 0);
  await page.getByLabel('Select assigned work').selectOption('project:p2');
  await page.getByText('Cost details are unavailable for this project in your current access.').waitFor();
  await page.getByRole('button', { name: 'Documents', exact: true }).click();
  await page.getByText('Storage_GSD.pdf', { exact: true }).waitFor();
  assert.equal(await page.getByText('Voice_Migration_SOW.docx', { exact: true }).count(), 0);
  await page.getByLabel('Select assigned work').selectOption('request:SR-003');
  await page.getByText('Assessment_Brief.docx', { exact: true }).waitFor();
  await page.getByLabel('Find a project or service request').fill('no such project');
  await page.getByText('No work matches your search.', { exact: true }).waitFor();
  assert.equal(await page.getByText('Assessment_Brief.docx', { exact: true }).count(), 0);
  await page.getByLabel('Find a project or service request').fill('');
  await page.getByLabel('Select assigned work').selectOption('project:p1');
  await page.getByRole('button', { name: 'Tasks & hours', exact: true }).click();
  await page.setViewportSize({ width: 390, height: 844 });
  await page.screenshot({ path: '/tmp/module019-mobile.png', fullPage: true });
  assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth), 'no page-level horizontal overflow');
  await page.getByText('Administrator user preview', { exact: true }).click();
  await page.getByLabel('View as', { exact: true }).selectOption('u2');
  await page.getByLabel('Select assigned work').selectOption('project:p1');
  await page.getByText('Configure the platform', { exact: true }).waitFor();
  const before = invoiceReads;
  await page.getByRole('button', { name: 'Cost & billing', exact: true }).click();
  await page.getByText('Invoice history is not available in user preview. Sign in as the user to verify billing access.').waitFor();
  assert.equal(invoiceReads, before, 'View-As must never request signed-in administrator invoice data');
  await page.getByRole('button', { name: 'Exit preview', exact: true }).click();
  await page.getByLabel('Select assigned work').selectOption('project:p1');
  await page.getByRole('button', { name: 'Documents', exact: true }).click();
  downloadStatus = 404;
  await page.getByRole('button', { name: 'Download', exact: true }).click();
  await page.getByText('This file is no longer available.', { exact: true }).waitFor();
  assert.equal(await page.getByText('Voice_Migration_SOW.docx', { exact: true }).count(), 1);
  downloadStatus = 200;
  invoiceStatus = 403;
  await page.getByRole('button', { name: 'Cost & billing', exact: true }).click();
  await page.getByText('Invoice history is unavailable in your current access. Ask the project manager or Billing for the billed amount.').waitFor();
  assert.equal(await page.getByText('INV-001', { exact: true }).count(), 0, 'denied billing never retains a previous invoice');
  overviewStatus = 401;
  await page.getByRole('button', { name: 'Refresh', exact: true }).click();
  await page.getByRole('alert').waitFor();
  assert.equal(await page.getByLabel('Select assigned work').count(), 0, 'expired session clears all prior work');
  assert.equal(await page.getByText('No active projects or service requests are assigned in your current access.', { exact: true }).count(), 0, 'failed read is not an empty portfolio');
  overviewStatus = 200;
  overviewOverride = { access: { userId: 'unassigned' }, projects: [], documents: [], assignments: [], teamHours: [], resourceRequests: [] };
  await page.getByRole('button', { name: 'Retry', exact: true }).click();
  await page.getByText('No active projects or service requests are assigned in your current access.', { exact: true }).waitFor();
  overviewOverride = { ...data, access: { userId: 'lead', scope: 'engineering_team_lead_scope' }, teamHours: null };
  await page.reload();
  await page.getByLabel('Select assigned work').selectOption('project:p1');
  await page.getByText('Configure the platform', { exact: true }).waitFor();
  await page.getByText('Project time is unavailable. Remaining hours cannot be confirmed.', { exact: true }).waitFor();
  assert.equal(await page.getByRole('button', { name: 'All project tasks', exact: true }).getAttribute('aria-pressed'), 'true');
  // Linking an intake to the selected project brings its existing authorized files into that project.
  overviewOverride = { ...data, resourceRequests: [{ ...data.resourceRequests[0], projectId: 'p1' }] };
  await page.reload();
  await page.getByLabel('Select assigned work').selectOption('project:p1');
  await page.getByRole('button', { name: 'Documents', exact: true }).click();
  await page.getByText('Assessment_Brief.docx', { exact: true }).waitFor();
  assert.equal(await page.locator('option[value="request:SR-003"]').count(), 0, 'linked request is consolidated into its project');
  assert.deepEqual(errors, []);
  console.log('Module 019 browser scenarios: PASS (selection, tasks, team totals, documents, authenticated download, preview, source failure, service request, search, mobile, View-As, missing files, billing denial, expired session, unassigned user, lead defaults, missing totals, linked intake)');
} finally {
  await browser.close(); await server.close(); await fs.rm(temp, { recursive: true, force: true });
}
