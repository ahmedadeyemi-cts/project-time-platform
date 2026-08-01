import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const webRoot = path.resolve(scriptDirectory, '..');
const repositoryRoot = path.resolve(webRoot, '../../..');
const read = (relativePath) => fs.readFileSync(path.join(repositoryRoot, relativePath), 'utf8');
const requireText = (source, value, label) => assert.ok(source.includes(value), `${label} is missing: ${value}`);
const rejectText = (source, value, label) => assert.ok(!source.includes(value), `${label} still contains prohibited legacy dependency: ${value}`);

const component = read('src/frontend/project-time-web/src/ProjectCloseoutCenter.jsx');
const css = read('src/frontend/project-time-web/src/project-closeout-center.css');
const app = read('src/frontend/project-time-web/src/App.jsx');
const lifecycle = read('src/backend/ProjectTime.Api/Modules/WorkLifecycleModule.cs');
const financialRecovery = read('src/backend/ProjectTime.Api/Modules/FinancialOperationsRecoveryModule.cs');

for (const marker of [
  "import { usSignalLogoDataUrl } from './assets/usSignalLogoData.js';",
  '/api/financial-operations/modules/040',
  '/api/work-lifecycle/projects/${projectId}',
  '/closeout/request',
  '/closeout/complete',
  '/closeout/reopen',
  "credentials: 'include'",
  "'X-ProjectPulse-Session'",
  "'X-Project-Pulse-Session'",
  "'X-Session-Token'",
  'projectPulseProjectCloseoutHandoff',
  'Module 055C handoff',
  'Exactly what happens next',
  'What you need to do now',
  'Server-validated blockers',
  'Required Project Manager confirmations',
  'PTC / Administrator',
  'One unavailable supporting source no longer replaces the entire page with a generic access error',
  'Assigned Project Managers request closeout',
  'View-As never transfers mutation authority'
]) requireText(component, marker, 'Module 040 guided closeout');

for (const legacyEndpoint of [
  '/api/project-workspace/overview',
  '/api/project-intake/overview',
  '/api/customers/overview',
  '/api/manager/approvals',
  '/api/manager/approval-count',
  '/api/certify/expenses/staged',
  '/api/certify/exceptions'
]) rejectText(component, legacyEndpoint, 'Module 040 guided closeout');

for (const marker of [
  ".project-closeout-route-panel > .group5-financial-operations[data-module-code='040']",
  '.project-closeout-hero',
  'linear-gradient(135deg, var(--m040-navy-950)',
  '.project-closeout-step-list',
  '.project-closeout-next-action',
  '.project-closeout-blocker-list',
  '.project-closeout-confirmations',
  '.project-closeout-source-health',
  '@media (max-width: 720px)',
  '@media print'
]) requireText(css, marker, 'Module 040 guided closeout styling');

requireText(app, '<ProjectCloseoutCenter />', 'Module 040 route mount');
requireText(app, 'GROUP_5_MODULE_040_RECOVERY_PANEL', 'Module 040 compatibility mount marker');

for (const marker of [
  '/api/work-lifecycle/projects/{projectId:guid}',
  '/closeout/request',
  '/closeout/complete',
  '/closeout/reopen',
  'BuildCloseoutBlockersAsync',
  'CanRequestCloseout',
  'CanCompleteCloseout',
  'CanReopenProject'
]) requireText(lifecycle, marker, 'Work lifecycle closeout API');

for (const marker of [
  '/api/financial-operations/modules/{moduleCode}',
  '"040" => actor.Broad',
  'project_closeout_records',
  'approved_time_entries',
  'billing_readiness_reviews',
  'healthySourcesRemainVisible = true'
]) requireText(financialRecovery, marker, 'Module 040 recovery API');

console.log('MODULE_040_GUIDED_CLOSEOUT_VALIDATION=PASS authoritativeApis=2 legacyFanout=removed module055cHandoff=preserved pmGuidance=enabled heroContrast=restored');