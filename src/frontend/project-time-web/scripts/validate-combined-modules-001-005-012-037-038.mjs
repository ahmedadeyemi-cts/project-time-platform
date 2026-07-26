import { readFile } from 'node:fs/promises';
import { resolve } from 'node:path';

const root = resolve(process.cwd(), '../../..');
const read = (value) => readFile(resolve(root, value), 'utf8');
const requireAll = (source, values, label) => {
  for (const value of values) {
    if (!source.includes(value)) throw new Error(`${label} missing contract: ${value}`);
  }
};
const rejectAll = (source, values, label) => {
  for (const value of values) {
    if (source.includes(value)) throw new Error(`${label} contains forbidden contract: ${value}`);
  }
};

const [
  csproj,
  combined,
  publicReadiness,
  safeExpense,
  authoritative,
  compatibility,
  roleAdmin,
  matrix,
  ptcPortal,
  timerTargetsPortal,
  timerRecovery,
  main,
  routeBoundary,
  routeCss,
  expensePanel,
  certifyCenter,
  startupTest
] = await Promise.all([
  read('src/backend/ProjectTime.Api/ProjectTime.Api.csproj'),
  read('src/backend/ProjectTime.Api/Modules/CombinedModuleRuntimeModule.cs'),
  read('src/backend/ProjectTime.Api/Modules/CombinedModulePublicReadiness.cs'),
  read('src/backend/ProjectTime.Api/Modules/Module005ProjectExpenseSafeEndpoints.cs'),
  read('src/frontend/project-time-web/src/projectpulse-authoritative-api.js'),
  read('src/frontend/project-time-web/src/runtime-data-compatibility.js'),
  read('src/frontend/project-time-web/src/RoleAdminDirectoryPanel.jsx'),
  read('src/frontend/project-time-web/src/RolesPermissionsMatrix.jsx'),
  read('src/frontend/project-time-web/src/module001/PtcTimesheetManagementPortal.jsx'),
  read('src/frontend/project-time-web/src/module001/TimesheetEnhancementPortal.jsx'),
  read('src/frontend/project-time-web/src/module001/Module001ActiveTimerRecoveryPortal.jsx'),
  read('src/frontend/project-time-web/src/main.jsx'),
  read('src/frontend/project-time-web/src/CriticalRoutePresentationBoundary.jsx'),
  read('src/frontend/project-time-web/src/critical-route-presentation.css'),
  read('src/frontend/project-time-web/src/ProjectAllocationInfoPanel.jsx'),
  read('src/frontend/project-time-web/src/CertifyIntegrationCenter.jsx'),
  read('tests/test-projectpulse-api-startup.sh')
]);

requireAll(csproj, [
  'app.MapCombinedModuleRuntimeEndpoints();',
  'app.MapCombinedModulePublicReadinessEndpoint();',
  'app.MapModule005ProjectExpenseUploadEndpointsSafe();',
  'app.MapModule038CertifyConnectionEndpoints();'
], 'API generated registration');
rejectAll(csproj, [
  'app.MapModule005ProjectExpenseUploadEndpoints();'
], 'API generated registration');

requireAll(combined, [
  '/api/runtime/v2/readiness',
  '/api/runtime/v2/role-policy/summary',
  '/api/runtime/v2/role-policy/catalog',
  '/api/runtime/v2/role-policy/versions',
  '/api/runtime/v2/role-policy/matrix',
  '/api/runtime/v2/timesheet/steward/users',
  'combined-modules-001-005-012-037-038-v2',
  'roleCount == 12',
  'moduleCount == 70',
  'eligibleUserCount > 0',
  'PROJECT_TEAM_COORDINATOR',
  'SUPER_ADMINISTRATOR',
  'authoritative_role_policy_summary_loaded',
  'authoritative_permission_matrix_loaded',
  'eligible_time_steward_users_missing',
  'emptyCollectionsAllowed = false'
], 'Combined backend runtime');
requireAll(publicReadiness, [
  'MapCombinedModulePublicReadinessEndpoint',
  '/health/combined-modules',
  '/api/public/combined-modules/readiness',
  'CombinedPublicReadinessAsync',
  'combined-modules-001-005-012-037-038-public-v1',
  'roleContractReady',
  'moduleContractReady',
  'eligibleUserContractReady',
  'operatorContractReady',
  'foundationalMigrationsReady',
  'expenseMigrationsReady',
  'expenseTablesReady',
  'operationalCountsReturned = false'
], 'Public combined readiness');
rejectAll(publicReadiness, [
  'app.MapGet("/api/public/combined-modules/readiness", CombinedRuntimeReadinessAsync);',
  'app.MapGet("/health/combined-modules", CombinedRuntimeReadinessAsync);'
], 'Public combined readiness');

requireAll(safeExpense, [
  'MapModule005ProjectExpenseUploadEndpointsSafe',
  '/api/project-expenses/readiness',
  '/api/public/project-expenses/readiness',
  'GetPublicProjectExpenseReadinessAsync',
  'project-expense-certify-public-v1',
  'migrationContractReady',
  'tableContractReady',
  'safeProfileReady',
  'permissionContractReady',
  'operationalCountsReturned = false',
  'DeleteUploadFromRequestAsync',
  'JsonSerializer.DeserializeAsync<ExpenseDeleteRequest>',
  'project_expense_runtime_ready',
  'automaticSyncEnabled = false',
  'secretsReturned = false'
], 'Module 005 startup-safe endpoints');
rejectAll(safeExpense, [
  'MapDelete("/api/project-expenses/uploads/{uploadId:guid}", (Func<Guid, ExpenseDeleteRequest',
  'app.MapGet("/api/public/project-expenses/readiness", (Func<Task<IResult>>)GetProjectExpenseReadinessAsync);'
], 'Module 005 startup-safe endpoints');

requireAll(authoritative, [
  "const DIAGNOSTIC_MARKER = 'projectpulse-authoritative-xhr-v1'",
  'new XMLHttpRequest()',
  'request.open(method, path, true)',
  'request.withCredentials = true',
  "request.setRequestHeader('Authorization', `Bearer ${token}`)",
  "request.setRequestHeader('X-ProjectPulse-Session', token)",
  "request.setRequestHeader('X-Project-Pulse-Session', token)",
  "request.setRequestHeader('X-Session-Token', token)",
  "request.setRequestHeader('X-ProjectPulse-View-As-User', viewAsUserId)",
  'requiredCollections',
  'collectionCounts',
  'projectpulse:authoritative-api-diagnostic',
  '__projectPulseAuthoritativeApiDiagnostics'
], 'Wrapper-independent authoritative API client');
rejectAll(authoritative, ['window.fetch(', 'fetch(path'], 'Wrapper-independent authoritative API client');

requireAll(compatibility, [
  "import { authoritativeApi } from './projectpulse-authoritative-api.js';",
  "'/api/role-policy/summary': '/api/runtime/v2/role-policy/summary'",
  "'/api/role-policy/matrix': '/api/runtime/v2/role-policy/matrix'",
  "'/api/runtime/timesheet/steward/users': '/api/timesheet/ptc/users'",
  "'/api/runtime/v2/timesheet/steward/users': '/api/timesheet/ptc/users'",
  '/api/timesheet/ptc/users/',
  'expectedCollections',
  'normalizePtcWorkspace',
  'allActiveUsersAllowed: true',
  'projectpulse-authoritative-xhr-compatibility-v2',
  'responseKeys: error?.diagnostic?.responseKeys'
], 'Frontend wrapper-independent runtime bridge');
rejectAll(compatibility, [
  'projectpulse-critical-runtime-direct-2026-07-26',
  'window.__projectPulseOriginalFetch',
  'directTransport(previousFetch)',
  'const raw = await response.text()'
], 'Frontend wrapper-independent runtime bridge');

requireAll(roleAdmin, [
  'REQUIRED_ROLE_COUNT = 12',
  'REQUIRED_MODULE_COUNT = 70',
  '/api/role-policy/summary',
  '/api/role-policy/catalog',
  '/api/role-policy/versions',
  'Role-policy data did not load'
], 'Module 012 UI');
requireAll(matrix, [
  'REQUIRED_ROLE_COUNT = 12',
  'REQUIRED_MODULE_COUNT = 70',
  '/api/role-policy/matrix',
  '/api/role-policy/catalog',
  'Permission matrix did not load'
], 'Module 037 UI');
requireAll(ptcPortal, [
  '/api/runtime/timesheet/steward/users?weekStart=',
  '/api/runtime/timesheet/steward/users/${encodeURIComponent(selectedUserId)}/workspace?weekStart=',
  'Project Team Coordinator · Time Steward',
  'No submission on behalf'
], 'Module 001 time-steward UI');

requireAll(timerTargetsPortal, [
  "import { authoritativeApi } from '../projectpulse-authoritative-api.js';",
  '/api/timesheet/timers/targets?weekStart=',
  "requiredCollections: ['targets']",
  'regularAssignedTasks',
  'requestAssignedTasks',
  'timerTargetCounts',
  'timerTargetAuthoritativeSources',
  'timerTargetLoadError',
  'Existing Timesheet activities remain available'
], 'Module 001 authoritative timer target collection');
rejectAll(timerTargetsPortal, [
  'const response = await fetch(path',
  'const raw = await response.text()'
], 'Module 001 authoritative timer target collection');

requireAll(timerRecovery, [
  "import { authoritativeApi } from '../projectpulse-authoritative-api.js';",
  "document.querySelector('#timesheet')",
  '/api/timesheet/timers/active',
  'window.setInterval(() => void load(), 5000)',
  'window.setInterval(() => setClock(new Date()), 1000)',
  'Timer status check failed',
  'Try timer check again',
  'Running timer recovered',
  'Timer automatically stopped',
  'Stop timer',
  'Discard'
], 'Module 001 persistent active timer recovery');
rejectAll(timerRecovery, [
  "document.querySelector('#timesheet.timesheet-page')",
  'const response = await fetch(path'
], 'Module 001 persistent active timer recovery');

requireAll(main, [
  "import './projectpulse-authoritative-api.js';",
  "import CriticalRoutePresentationBoundary from './CriticalRoutePresentationBoundary.jsx';",
  "import Module001ActiveTimerRecoveryPortal from './module001/Module001ActiveTimerRecoveryPortal.jsx';",
  '<CriticalRoutePresentationBoundary />',
  '<Module001ActiveTimerRecoveryPortal />'
], 'Functional runtime application mounts');
requireAll(routeBoundary, [
  "const PREFIX = 'projectpulse-route-'",
  "window.location.hash.replace(/^#/, '').split('?')[0]",
  'document.body.classList.add',
  "window.addEventListener('hashchange', apply)"
], 'Explicit route presentation boundary');
requireAll(routeCss, [
  'body.projectpulse-route-certify-integration .module-grid[aria-label="Core workflow modules"]',
  'display:none!important',
  'body.projectpulse-route-certify-integration .certify-integration-center',
  'max-height:calc(100dvh - 13.5rem)',
  'overflow-y:auto',
  'overscroll-behavior:contain',
  'scrollbar-gutter:stable',
  'body.projectpulse-route-certify-integration .certify-sync-control-card',
  'position:sticky'
], 'Module 038 explicit bounded route presentation');

requireAll(expensePanel, [
  'Project Expense Upload',
  'Select customer',
  'Select project',
  'Select expense owner',
  'Upload CSV / Excel',
  'Import from Certify',
  '/api/project-expenses/upload',
  '/api/project-expenses/import/certify',
  'Module 067 Global Mail Configuration'
], 'Module 005 UI');
requireAll(certifyCenter, [
  'Certify Connection &amp; Sync Center',
  '/api/certify/connection',
  '/api/certify/connection/test',
  'Enable automatic sync',
  'Test connection to unlock',
  'automationAllowed',
  'Secret values remain in environment configuration'
], 'Module 038 UI');
requireAll(startupTest, [
  'PROJECTPULSE_API_STARTUP_SMOKE=PASS',
  '/health',
  '/api/version',
  '/health/combined-modules',
  '/api/public/combined-modules/readiness',
  '/api/public/project-expenses/readiness',
  '/api/runtime/v2/readiness',
  '"operationalCountsReturned":false',
  'publicReadinessContracts=ready',
  'protectedAuthBoundary=ready',
  'operationalCountsSuppressed=true'
], 'API startup smoke test');

console.log('FUNCTIONAL_RUNTIME_UAT_001_012_037_038_CONTRACTS=PASS');
