import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const repository = fileURLToPath(new URL('../', import.meta.url));
const frontend = path.join(repository, 'src/frontend/project-time-web');
const { createServer } = await import(pathToFileURL(path.join(frontend, 'node_modules/vite/dist/node/index.js')));
const { default: react } = await import(pathToFileURL(path.join(frontend, 'node_modules/@vitejs/plugin-react/dist/index.js')));
const { chromium } = await import(pathToFileURL(process.env.MODULE064_PLAYWRIGHT_PATH || path.join(process.env.CODEX_PRIMARY_RUNTIME_NODE_MODULES, 'playwright/index.mjs')));
const fixture = await fs.mkdtemp(path.join(frontend, '.module064-browser-'));
await fs.writeFile(path.join(fixture, 'index.html'), '<html><head><meta name="viewport" content="width=device-width,initial-scale=1" /></head><body style="margin:0;background:#edf2f6;font:16px Arial"><div id="root" style="max-width:1280px;margin:24px auto"></div><script type="module" src="/entry.jsx"></script></body></html>');
await fs.writeFile(path.join(fixture, 'entry.jsx'), `
import React, {useState} from 'react';
import {createRoot} from 'react-dom/client';
import {AiProviderModelSelector, GeminiHealthNotice} from '../src/AiProviderConfigurationCenter.jsx';
import Routing from '../src/CelarAiCapabilityRoutingPanel.jsx';
function Fixture() {
  const [provider, setProvider] = useState({code:'gemini', displayName:'Gemini', configured:true, model:'gemini-current', approvedModels:['gemini-current'], secret:{version:'fixture-1'}});
  const [notice, setNotice] = useState('');
  return <div className="ai-provider-center">
    <section style={{maxWidth:600}}>
      <h1>Gemini provider</h1><p data-testid="active-model">Active: {provider.model}</p><p role="status" data-testid="notice">{notice}</p>
      <AiProviderModelSelector provider={provider} onNotice={setNotice} onSaved={async () => setProvider(await (await fetch('/api/fixture/provider')).json())}/>
      <GeminiHealthNotice health={{probeStatus:'degraded', status:'circuit_open',lastProbeFailureCode:'gemini_http_429_daily_quota',lastProbeFailureMessage:'Google reports the daily model quota is exhausted.',probeFailureCount:9,failureCount:0,retryAfterUtc:'2026-09-20T20:00:00Z'}}/>
      <div data-testid="generation-quota"><GeminiHealthNotice health={{probeStatus:'available', status:'circuit_open',lastFailureCode:'module025_external_gemini_http_429_rate_limit',lastFailureMessage:'Google reports a per-minute model limit.',failureCount:1}}/></div>
      <div data-testid="recovered-health"><GeminiHealthNotice health={{probeStatus:'available', status:'available',lastProbeFailureCode:'gemini_http_429',lastFailureCode:'module025_external_gemini_http_429'}}/></div>
    </section>
    <Routing/>
  </div>;
}
createRoot(document.getElementById('root')).render(<Fixture/>);
`);
const server = await createServer({ configFile: false, root: fixture, plugins: [react()], server: { host: '127.0.0.1', port: 0, fs: { allow: [frontend] } } });
await server.listen();
let browser;
try {
  browser = await chromium.launch({ headless: true, ...(process.env.MODULE064_CHROMIUM_PATH ? { executablePath: process.env.MODULE064_CHROMIUM_PATH, args: ['--no-sandbox', '--disable-dev-shm-usage'] } : {}) });
} catch (error) {
  await server.close();
  await fs.rm(fixture, { recursive: true, force: true });
  throw error;
}
const page = await browser.newPage({ viewport: { width: 1380, height: 1100 } });
const errors = [];
page.on('pageerror', (error) => errors.push(error.message));
let modelReads = 0;
let modelWrites = 0;
let modelWriteFailure = true;
let catalogStatus = 'available';
let availableIds = ['gemini-current', 'gemini-economy', 'gemini-other'];
let currentModel = 'gemini-current';
let routeWrites = [];
let conflict = false;
let readOnly = false;
let approved = false;
let revision = 3;
const targets = ['gemini', 'claude', 'openai', 'deepseek_v4', 'celar_ai', 'local_template'];
const sowRoute = () => ({
  feature: 'sow_gsd_planning', displayName: 'SOW / GSD planning', consumerModules: ['011', '025'],
  targets, revision, persisted: true, contextClassification: 'restricted_commercial_document', externalContextPolicy: 'sanitized_generic_only',
  sanitizedExternalGenerationApproved: approved, externalGenerationApprovalEditable: !readOnly,
  effectiveTargets: approved ? targets : ['deepseek_v4', 'celar_ai', 'gemini', 'claude', 'openai', 'local_template'],
  executionPolicy: { status: approved ? 'saved_order' : 'external_generation_blocked',
    blockers: approved ? [] : ['sanitized_external_generation_approval_required'],
    message: approved ? 'Approved technical capsules follow the saved provider order.' : 'External generation approval is required.',
    generationMode: 'validated_structured_phases' },
});
await page.route('**/api/**', async (route) => {
  const request = route.request();
  const url = new URL(request.url());
  const json = (value, status = 200) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(value) });
  if (url.pathname.endsWith('/providers/gemini/models')) {
    modelReads += 1;
    assert.equal(request.method(), 'GET');
    assert.equal(request.postData(), null, 'model discovery never sends a browser credential');
    return json({ provider: 'gemini', status: catalogStatus, checkedAt: '2026-09-20T19:00:00Z', activeModel: currentModel,
      message: catalogStatus === 'available' ? 'Compatible account models loaded.' : 'Google temporarily limited model discovery.',
      diagnostic: catalogStatus === 'available' ? null : 'gemini_model_catalog_http_429',
      models: catalogStatus === 'available' ? availableIds.map((id) => ({ id, displayName: id, inputTokenLimit: 32768, outputTokenLimit: 8192, costGuidance: 'Pricing is not returned by model discovery; compare official prices.' })) : [] });
  }
  if (url.pathname.endsWith('/providers/gemini/model')) {
    modelWrites += 1;
    assert.equal(request.method(), 'PUT');
    assert.deepEqual(request.postDataJSON(), { model: 'gemini-economy' });
    if (modelWriteFailure) return json({ message: 'Gemini quota prevented verification. The previous model remains active.' }, 429);
    currentModel = request.postDataJSON().model;
    return json({ message: 'Model saved and tested.' });
  }
  if (url.pathname === '/api/fixture/provider') return json({ code: 'gemini', displayName: 'Gemini', configured: true, model: currentModel, secret: { version: 'fixture-1' } });
  if (url.pathname === '/api/ai-configuration/routes') return json({ controls: { readOnly }, routes: [sowRoute(), {
    feature: 'project_flowhive_plan', displayName: 'Project FlowHive plan', consumerModules: ['011', '066'], targets, revision: 1,
    effectiveTargets: ['deepseek_v4', 'celar_ai', 'gemini', 'claude', 'openai', 'local_template'],
    executionPolicy: { status: 'private_first_policy', message: 'Private document policy applies.', blockers: [], generationMode: 'private_evidence_wbs_with_generic_external_assistance' },
  }] });
  if (url.pathname.startsWith('/api/ai-configuration/routes/') && request.method() === 'PUT') {
    const body = request.postDataJSON();
    routeWrites.push(body);
    if (conflict) return json({ message: 'The route changed in another session. Refresh routing and review the saved policy.' }, 409);
    approved = body.sanitizedExternalGenerationApproved ?? approved;
    revision += 1;
    return json({ message: 'Capability route saved.' });
  }
  if (url.pathname === '/api/ai-configuration/private-model') return json({ profile: { revision: 1 }, productionReadiness: {} });
  if (url.pathname === '/api/ai-configuration/consumers') return json({ consumers: [] });
  if (url.pathname === '/api/ai-configuration/knowledge-fabric') return json({ knowledgeFabric: {} });
  throw new Error(`Unexpected fixture request: ${request.method()} ${url.pathname}`);
});

try {
  await page.goto(server.resolvedUrls.local[0]);
  const picker = page.getByLabel('Active model', { exact: true });
  await page.getByText('Compatible account models loaded.', { exact: false }).waitFor();
  assert.equal(modelReads, 1, 'catalog loads automatically once');
  assert.equal(modelWrites, 0, 'discovery does not change the active model');
  assert.equal(await picker.inputValue(), 'gemini-current');
  assert.equal(await picker.locator('option').count(), 3);
  assert.equal(await page.getByRole('button', { name: 'Save and test', exact: true }).isDisabled(), true);
  await page.getByText('9 failed readiness probes are separate from SOW generation attempts. Generation failures: 0.', { exact: true }).waitFor();
  await page.getByText('Google reports the daily model quota is exhausted.', { exact: true }).waitFor();
  await page.getByTestId('generation-quota').getByText('Google reports a per-minute model limit.', { exact: true }).waitFor();
  assert.equal(await page.getByTestId('recovered-health').innerText(), '', 'old quota failures are hidden after health recovers');
  assert.equal(await page.getByRole('link', { name: 'Compare official Gemini pricing' }).getAttribute('href'), 'https://ai.google.dev/gemini-api/docs/pricing');

  await picker.selectOption('gemini-economy');
  await page.getByRole('button', { name: 'Save and test', exact: true }).click();
  await page.getByText('Gemini quota prevented verification. The previous model remains active.', { exact: true }).waitFor();
  assert.equal(await page.getByTestId('active-model').innerText(), 'Active: gemini-current', 'failed probe keeps previous active model');
  modelWriteFailure = false;
  await page.getByRole('button', { name: 'Save and test', exact: true }).click();
  await page.getByText('Active: gemini-economy', { exact: true }).waitFor();
  assert.equal(modelWrites, 2);
  assert.equal(modelReads, 1, 'model save does not create another catalog or generation call');

  await picker.selectOption('gemini-other');
  availableIds = ['gemini-current'];
  await page.getByRole('button', { name: 'Refresh models', exact: true }).click();
  await page.getByText('The selected model is no longer listed. Choose another available model before saving.', { exact: true }).waitFor();
  assert.equal(await picker.inputValue(), 'gemini-other', 'refresh never selects a replacement automatically');
  assert.equal(await page.getByRole('button', { name: 'Save and test', exact: true }).isDisabled(), true);
  await picker.selectOption('gemini-economy');
  assert.equal(await page.getByTestId('active-model').innerText(), 'Active: gemini-economy');
  catalogStatus = 'unavailable';
  await page.getByRole('button', { name: 'Refresh models', exact: true }).click();
  await page.getByText('Google temporarily limited model discovery.', { exact: false }).waitFor();
  assert.equal(await picker.inputValue(), 'gemini-economy');
  assert.equal(await page.getByRole('button', { name: 'Save and test', exact: true }).isDisabled(), true);
  assert.equal(modelWrites, 2);

  const card = page.locator('.celar-ai-routing__route-card').filter({ has: page.getByText('SOW / GSD planning', { exact: true }) });
  const approval = card.getByLabel('Authorize paid providers for validated, sanitized SOW/GSD generation');
  assert.equal(await approval.isChecked(), false, 'approval is never implied by provider inclusion or priority');
  const policy = card.getByRole('region', { name: 'SOW / GSD planning saved execution policy' });
  assert.match(await policy.innerText(), /DeepSeek v4 → Celar AI → Gemini/);
  await approval.check();
  assert.match(await policy.innerText(), /External generation approval is required/, 'unsaved approval does not misrepresent execution');
  await card.getByRole('button', { name: 'Save route', exact: true }).click();
  await card.getByText('Approved technical capsules follow the saved provider order.', { exact: true }).waitFor();
  assert.deepEqual(routeWrites[0], { targets, expectedRevision: 3, sanitizedExternalGenerationApproved: true });
  assert.match(await policy.innerText(), /Gemini → Claude → OpenAI → DeepSeek v4 → Celar AI/);
  await page.getByText('External providers offer generic planning guidance for FlowHive. Detailed WBS generation still uses the private planning runtime.', { exact: true }).waitFor();

  conflict = true;
  await approval.uncheck();
  await card.getByRole('button', { name: 'Save route', exact: true }).click();
  await page.getByText('The route changed in another session. Refresh routing and review the saved policy.', { exact: true }).waitFor();
  assert.match(await policy.innerText(), /Approved technical capsules/, 'revision conflict preserves saved effective policy');
  conflict = false;
  readOnly = true;
  await page.getByRole('button', { name: 'Refresh routing', exact: true }).click();
  await page.waitForFunction(() => document.querySelector('.celar-ai-routing__external-approval input')?.disabled);
  assert.equal(await approval.isChecked(), true);
  assert.equal(await approval.isDisabled(), true);
  assert.equal(await card.getByRole('button', { name: 'Read-only', exact: true }).isDisabled(), true);

  await page.setViewportSize({ width: 390, height: 844 });
  const layout = await page.evaluate(() => ({ fits: document.documentElement.scrollWidth <= window.innerWidth,
    overflowing: [...document.querySelectorAll('body *')].filter((element) => element.getBoundingClientRect().right > window.innerWidth + 1).map((element) => element.className).slice(0, 15) }));
  assert.ok(layout.fits, `provider and route controls fit a mobile viewport: ${JSON.stringify(layout.overflowing)}`);
  if (process.env.MODULE064_SCREENSHOT_PATH) {
    await page.setViewportSize({ width: 1380, height: 1100 });
    await page.screenshot({ path: process.env.MODULE064_SCREENSHOT_PATH, fullPage: true });
  }
  assert.deepEqual(errors, [], 'no browser runtime errors');
  console.log('MODULE064_PROVIDER_MODELS_BROWSER=PASS automatic discovery, explicit model selection, quota failure rollback, unavailable discovery, safe approval, saved effective order, revision conflict, read-only controls, mobile layout');
} finally {
  await browser.close();
  await server.close();
  await fs.rm(fixture, { recursive: true, force: true });
}
