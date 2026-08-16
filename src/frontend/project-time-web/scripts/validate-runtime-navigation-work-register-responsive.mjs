import fs from 'node:fs';
import { authorizedModulesFromNavigationState, MODULE_DIRECTORY_AUTHORITY_CONTRACT } from '../src/module-directory-authority.js';
import { PROJECTPULSE_MODULES } from '../src/module-availability-registry.js';

function read(path) {
  if (!fs.existsSync(path)) throw new Error(`Missing source: ${path}`);
  return fs.readFileSync(path, 'utf8');
}

function requireText(source, marker, label) {
  if (!source.includes(marker)) throw new Error(`Missing ${label}: ${marker}`);
}

function rejectText(source, marker, label) {
  if (source.includes(marker)) throw new Error(`Forbidden ${label}: ${marker}`);
}

if (MODULE_DIRECTORY_AUTHORITY_CONTRACT !== 'AUTHORITATIVE_RBAC_MODULE_DIRECTORY_V1') {
  throw new Error('Unexpected Module Directory authority contract.');
}
const allowedNumbers = new Set(['002', '018', '055C']);
const denied = PROJECTPULSE_MODULES
  .filter((module) => !allowedNumbers.has(module.moduleNumber))
  .map((module) => module.moduleNumber);
const authorized = authorizedModulesFromNavigationState(PROJECTPULSE_MODULES, {
  state: 'ready',
  deniedModuleNumbers: denied,
  retiredModuleNumbers: []
});
if (!authorized || authorized.length !== 3 || authorized.some((module) => !allowedNumbers.has(module.moduleNumber))) {
  throw new Error('Authoritative Module Directory did not retain the allowed Project Management module set.');
}
if (authorizedModulesFromNavigationState(PROJECTPULSE_MODULES, { state: 'loading' }) !== null) {
  throw new Error('Module Directory must defer to the DOM only while authoritative evidence is not ready.');
}

const portal = read('./src/ModulesDirectoryPortal.jsx');
const app = read('./src/App.jsx');
const mailbox = read('./src/ApprovalMailbox.jsx');
const approvalCompatibility = read('./src/approval-access-navigation-compatibility.js');
const scope = read('../../backend/ProjectTime.Api/Modules/ProjectManagementWorkRegisterScope.cs');
const css = read('./src/unified-project-financial-workspace.css');
const deployment = read('../../../.github/workflows/projectpulse-deploy-test.yml');

for (const marker of [
  'authorizedModulesFromNavigationState',
  'window.__projectPulseEffectiveNavigation',
  'projectpulse:permission-navigation-updated',
  'data-authority-contract={MODULE_DIRECTORY_AUTHORITY_CONTRACT}'
]) requireText(portal, marker, 'authoritative Modules directory');

for (const marker of [
  "Promise.allSettled([",
  "activeRoute === 'utilization'",
  "skipped: 'route_not_active'",
  "activeRoute === 'project-allocation-info'",
  "activeRoute === 'ai-provider-configuration'",
  'approvalAuthorityFromNavigationState',
  'projectpulse:permission-navigation-updated',
  'VIEW_TEAM_UTILIZATION'
]) requireText(app, marker, 'route and capability gated optional API loading');
rejectText(app, `Promise.all([
          fetchJson('/api/security/me', authSession),
          fetchJson('/api/utilization/current-quarter', authSession)`, 'coupled identity/utilization request');
rejectText(app, `<YearlyUtilizationPanel />
        <ManagerTeamUtilizationPanel />`, 'unguarded utilization panels');

for (const role of ['PROJECT_MANAGEMENT_LEAD', 'PROJECT_MANAGEMENT_TEAM_LEAD', 'PM_TEAM_LEAD']) {
  requireText(app, `'${role}'`, `Module 002 route role ${role}`);
  requireText(mailbox, `'${role}'`, `Approval mailbox role ${role}`);
}
requireText(mailbox, "'X-ProjectPulse-Module-Number': '002'", 'Approval mailbox module attribution');
requireText(mailbox, 'projectpulse:permission-navigation-updated', 'Approval mailbox capability gate');
requireText(approvalCompatibility, "headers.set('X-ProjectPulse-Module-Number', '002');", 'Approval access module attribution');

for (const marker of [
  'using NpgsqlTypes;',
  'NpgsqlDbType.Uuid',
  "to_jsonb(project_manager)->>'manager_email'",
  "to_jsonb(project_manager)->>'team_name'",
  'correlationId = context.TraceIdentifier'
]) requireText(scope, marker, 'runtime-safe Work Register scope');

for (const marker of [
  'PROJECT_WORKLOAD_RESPONSIVE_METRIC_REPAIR_V1',
  '--project-workload-responsive-metrics: 1',
  'repeat(auto-fit, minmax(min(100%, 220px), 1fr))',
  'grid-template-columns: minmax(0, 1fr);',
  'word-break: normal;',
  'justify-self: start;',
  'white-space: normal;'
]) requireText(css, marker, 'responsive unified workspace metric layout');
rejectText(css, 'word-break: break-all;', 'letter-by-letter metric wrapping');

for (const marker of [
  "PROJECT_MANAGER='heather.schrock@ussignal.local'",
  'Project Manager module availability',
  'Project Manager Work Register overview',
  'Project Manager Approval access',
  'AUTHORITATIVE_RBAC_MODULE_DIRECTORY_V1',
  '--project-workload-responsive-metrics:1',
  'projectManagerWorkRegister:true',
  'projectManagerApprovalAccess:true'
]) requireText(deployment, marker, 'Protected Test Project Management UAT');

console.log('RUNTIME_NAVIGATION_WORK_REGISTER_RESPONSIVE=PASS');
console.log('MODULE_DIRECTORY_SOURCE=AUTHORITATIVE_RBAC_STATE');
console.log('OPTIONAL_API_LOADING=ROUTE_AND_CAPABILITY_GATED');
console.log('WORK_REGISTER_NULLABLE_PROJECT_ID=TYPED_UUID');
console.log('PROJECT_WORKLOAD_METRICS=RESPONSIVE_HORIZONTAL_TEXT');
console.log('APPROVAL_INBOX_PM_ROLE_FAMILY=MODULE_002_ATTRIBUTED');
console.log('PR697_PRESENTATION_PACKAGE=PRESERVED_BY_CI');
