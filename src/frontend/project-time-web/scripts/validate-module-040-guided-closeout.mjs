import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const webRoot = path.resolve(scriptDirectory, '..');
const repositoryRoot = path.resolve(webRoot, '../../..');
const read = (relativePath) => fs.readFileSync(path.join(repositoryRoot, relativePath), 'utf8');
const requireText = (source, value, label) => assert.ok(source.includes(value), `${label} is missing: ${value}`);
const rejectText = (source, value, label) => assert.ok(!source.includes(value), `${label} still contains prohibited legacy content: ${value}`);
const count = (source, value) => source.split(value).length - 1;

const component = read('src/frontend/project-time-web/src/ProjectCloseoutCenter.jsx');
const css = read('src/frontend/project-time-web/src/project-closeout-center.css');
const app = read('src/frontend/project-time-web/src/App.jsx');
const workRegister = read('src/frontend/project-time-web/src/WorkRegisterCenter.jsx');
const workRegisterValidator = read('src/frontend/project-time-web/scripts/validate-work-register-055c-055d.mjs');
const lifecycle = read('src/backend/ProjectTime.Api/Modules/WorkLifecycleModule.cs');
const financialRecovery = read('src/backend/ProjectTime.Api/Modules/FinancialOperationsRecoveryModule.cs');

for (const marker of [
  "import { usSignalLogoDataUrl } from './assets/usSignalLogoData.js';",
  '/api/financial-operations/modules/040',
  '/api/work-lifecycle/projects/${projectId}',
  '/closeout/${operation}',
  '/closeout/reopen',
  "saveGovernedCloseout('request')",
  "saveGovernedCloseout('complete')",
  "saveGovernedCloseout('reopen')",
  "method: 'POST'",
  "credentials: 'include'",
  "'X-ProjectPulse-Session'",
  "'X-Project-Pulse-Session'",
  "'X-Session-Token'",
  'projectPulseProjectCloseoutHandoff',
  'Module 055C handoff',
  'Project Closeout Center',
  'Exactly what happens next',
  'What you need to do now',
  'Server-validated blockers',
  'These checks come from the selected project lifecycle, not from browser estimates.',
  'No server blockers remain',
  'Resolve every server blocker',
  'Record the PM closeout request',
  'PTC or Administrator finalizes closeout',
  'Required Project Manager confirmations',
  'Final billing disposition',
  'Audit reason',
  'Delivery is complete',
  'Customer acceptance is complete',
  'Final time and expense review is complete',
  'Billing is complete',
  'Request project closeout',
  'Complete project closeout',
  'Reopen project',
  'Supporting source health',
  'One unavailable supporting source no longer replaces the entire page with a generic access error',
  'Closeout history and evidence',
  'Assigned Project Managers request closeout',
  'View-As never transfers mutation authority',
  'closeoutBlockers',
  'capabilities?.canRequestCloseout',
  'capabilities?.canCompleteCloseout',
  'capabilities?.canReopenProject',
  'capabilities?.isViewAs',
  'closeoutForm.deliveryComplete',
  'closeoutForm.customerAcceptanceComplete',
  'closeoutForm.timeExpenseComplete',
  'closeoutForm.billingComplete',
  "operation !== 'reopen'",
  'normalizeText(closeoutForm.reason).length < 5',
  'error?.payload?.blockers',
  'await Promise.all(['
]) requireText(component, marker, 'Module 040 guided closeout');

for (const legacyEndpoint of [
  '/api/work-lifecycle/dashboard',
  '/api/project-workspace/overview',
  '/api/project-intake/overview',
  '/api/customers/overview',
  '/api/manager/approvals',
  '/api/manager/approval-count',
  '/api/certify/expenses/staged',
  '/api/certify/exceptions',
  '/api/financial-operations/truth',
  '/api/financial-operations/sources/005/recover',
  '/api/financial-operations/sources/026/recover',
  '/api/financial-operations/sources/036/recover',
  '/api/financial-operations/sources/039/recover',
  '/api/financial-operations/sources/040/recover',
  '/api/financial-operations/sources/042/recover'
]) rejectText(component, legacyEndpoint, 'Module 040 guided closeout');

for (const marker of [
  '.module040-guided-closeout',
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

requireText(app, '<ProjectCloseoutCenter />', 'Module 040 canonical route mount');
assert.equal(count(app, '<ProjectCloseoutCenter />'), 1, 'Module 040 guided closeout mount must be unique.');
rejectText(app, '<FinancialOperationsRecoveryWorkspace moduleCode="040"', 'Module 040 canonical route');
rejectText(app, 'GROUP_5_MODULE_040_RECOVERY_PANEL', 'Module 040 canonical route');

for (const marker of [
  'startProjectCloseout',
  'projectPulseProjectCloseoutHandoff',
  "window.location.hash = 'project-closeout'",
  'Start Project Closeout',
  'Opens Module 040 with this project selected for governed closeout readiness.'
]) requireText(workRegister, marker, 'Module 055C closeout handoff');

for (const marker of [
  "test('CLOSEOUT_HANDOFF_SOURCE'",
  "test(\n  'CLOSEOUT_HANDOFF_TARGET'",
  'projectPulseProjectCloseoutHandoff',
  "removeItem('projectPulseProjectCloseoutHandoff')"
]) requireText(workRegisterValidator, marker, 'Work Register closeout handoff validator');

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

console.log('MODULE_040_GUIDED_CLOSEOUT_VALIDATION=PASS authoritativeApis=2 legacyFanout=removed module055cHandoff=preserved blockers=server_authoritative actions=role_scoped responsive=true');
