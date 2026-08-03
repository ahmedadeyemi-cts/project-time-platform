import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const webRoot = path.resolve(scriptDirectory, '..');
const repositoryRoot = path.resolve(webRoot, '../../..');
const read = (relativePath) => fs.readFileSync(path.join(repositoryRoot, relativePath), 'utf8');
const requireText = (source, value, label) => assert.ok(source.includes(value), `${label} is missing: ${value}`);
const rejectText = (source, value, label) => assert.ok(!source.includes(value), `${label} still contains retired content: ${value}`);
const count = (source, value) => source.split(value).length - 1;

const closeout = read('src/frontend/project-time-web/src/ProjectCloseoutCenter.jsx');
const styles = read('src/frontend/project-time-web/src/project-closeout-center.css');
const app = read('src/frontend/project-time-web/src/App.jsx');
const workRegister = read('src/frontend/project-time-web/src/WorkRegisterCenter.jsx');
const workRegisterValidator = read('src/frontend/project-time-web/scripts/validate-work-register-055c-055d.mjs');

for (const marker of [
  '/api/financial-operations/modules/040',
  '/api/work-lifecycle/projects/${projectId}',
  '/closeout/request',
  '/closeout/complete',
  '/closeout/reopen',
  'Record and complete project closeout',
  'What you need to do now',
  'Start here and complete the steps in order',
  'Project identity',
  'Project manager',
  'Project Team Coordinator',
  'Project assignments',
  'Solution Architect',
  'Account Executive',
  'Current project financials',
  'Financial context and closeout readiness',
  'Commercial data-completeness',
  'do not block closeout by themselves',
  'Contracted value',
  'Expense budget',
  'SELL association',
  'Authoritative server blockers',
  'Only the server-evaluated list block closeout.',
  'closeoutBlockers',
  'getCloseoutBlockers',
  'Permissions for this project',
  'canRequestCloseout',
  'canCompleteCloseout',
  'canReopenProject',
  'Resolve blockers',
  'Record the PM closeout request',
  'PTC or Administrator finalizes closeout',
  'Project closeout actions',
  'requestCloseout',
  'completeCloseout',
  'reopenProject',
  'Enter REOPEN',
  'Billing disposition',
  'Delivery is complete',
  'Customer acceptance is complete',
  'Final time and expenses have been reviewed',
  'Billing is complete',
  'Audit reason',
  'Document why this project is ready to close.',
  'Request project closeout',
  'Complete project closeout',
  'Closeout status',
  'Billing readiness',
  'Open alerts',
  'Customer notes',
  'Invoice summary',
  'Closeout decision history',
  'Immutable audit evidence',
  'Module 055C',
  'Start closeout from the selected project in the edit workspace.'
]) requireText(closeout, marker, 'Module 040 guided closeout');

for (const retiredEndpoint of [
  '/api/financial-operations/truth',
  '/api/financial-operations/sources/005/recover',
  '/api/financial-operations/sources/026/recover',
  '/api/financial-operations/sources/036/recover',
  '/api/financial-operations/sources/039/recover',
  '/api/financial-operations/sources/040/recover',
  '/api/financial-operations/sources/042/recover'
]) rejectText(closeout, retiredEndpoint, 'Module 040 runtime contract');

for (const marker of [
  '.project-closeout-hero',
  'linear-gradient(135deg, var(--m040-navy-950)',
  '.project-closeout-step-list',
  '.project-closeout-next-action',
  '.project-closeout-blocker-list',
  '.project-closeout-confirmations',
  '.project-closeout-source-health',
  '@media (max-width: 720px)',
  '@media print'
]) requireText(styles, marker, 'Module 040 guided closeout styling');

requireText(app, '<ProjectCloseoutCenter />', 'Module 040 canonical route mount');
assert.equal(count(app, '<ProjectCloseoutCenter />'), 1, 'Module 040 guided closeout mount must be unique.');
rejectText(app, '<FinancialOperationsRecoveryWorkspace moduleCode="040"', 'Module 040 canonical route');
rejectText(app, 'GROUP_5_MODULE_040_RECOVERY_PANEL', 'Module 040 canonical route');

for (const marker of [
  'projectPulseProjectCloseoutHandoff',
  'projectPulseProjectCloseoutRequested',
  'work_register_closeout_handoff',
  '#project-closeout',
  'Start closeout from the selected project in the edit workspace.'
]) requireText(workRegister, marker, `Work Register closeout handoff ${marker}`);

requireText(workRegisterValidator, 'projectPulseProjectCloseoutHandoff', 'Work Register handoff validator');
requireText(workRegisterValidator, 'ProjectCloseoutCenter', 'Work Register closeout mount validator');
requireText(workRegisterValidator, 'removeItem(', 'Work Register handoff consumption validator');

console.log('MODULE_040_GUIDED_CLOSEOUT_VALIDATION=PASS sources=isolated blockers=server_authoritative actions=role_scoped handoff=055C responsive=true');
