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
  compatibility,
  roleAdmin,
  matrix,
  ptcPortal,
  timerRecovery,
  main,
  expensePanel,
  certifyCenter,
  certifyCss,
  startupTest
] = await Promise.all([
  read('src/backend/ProjectTime.Api/ProjectTime.Api.csproj'),
  read('src/backend/ProjectTime.Api/Modules/CombinedModuleRuntimeModule.cs'),
  read('src/backend/ProjectTime.Api/Modules/CombinedModulePublicReadiness.cs'),
  read('src/backend/ProjectTime.Api/Modules/Module005ProjectExpenseSafeEndpoints.cs'),
  read('src/frontend/project-time-web/src/runtime-data-compatibility.js'),
  read('src/frontend/project-time-web/src/RoleAdminDirectoryPanel.jsx'),
  read('src/frontend/project-time-web/src/RolesPermissionsMatrix.jsx'),
  read('src/frontend/project-time-web/src/module001/PtcTimesheetManagementPortal.jsx'),
  read('src/frontend/project-time-web/src/module001/Module001ActiveTimerRecoveryPortal.jsx'),
  read('src/frontend/project-time-web/src/main.jsx'),
  read('src/frontend/project-time-web/src/ProjectAllocationInfoPanel.jsx'),
  read('src/frontend/project-time-web/src/CertifyIntegrationCenter.jsx'),
  read('src/frontend/project-time-web/src/certify-integration-center.css'),
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

requireAll(compatibility, [
  'projectpulse-critical-runtime-direct-2026-07-26',
  "'/api/runtime/role-policy/summary': '/api/runtime/v2/role-policy/summary'",
  "'/api/runtime/role-policy/matrix': '/api/runtime/v2/role-policy/matrix'",
  "'/api/runtime/timesheet/steward/users': '/api/timesheet/ptc/users'",
  "'/api/runtime/v2/timesheet/steward/users': '/api/timesheet/ptc/users'",
  '/api/timesheet/ptc/users/',
  'normalizePtcWorkspace',
  'allActiveUsersAllowed: true',
  'window.__projectPulseOriginalFetch',
  'x-projectpulse-authoritative-path',
  'direct authoritative response'
], 'Frontend direct authoritative runtime bridge');

requireAll(roleAdmin, [
  'REQUIRED_ROLE_COUNT = 12',
  'REQUIRED_MODULE_COUNT = 70',
  '/api/role-policy/summary',
  'Role-policy data did not load'
], 'Module 012 UI');
requireAll(matrix, [
  'REQUIRED_ROLE_COUNT = 12',
  'REQUIRED_MODULE_COUNT = 70',
  '/api/role-policy/matrix',
  'Permission matrix did not load'
], 'Module 037 UI');
requireAll(ptcPortal, [
  '/api/runtime/timesheet/steward/users?weekStart=',
  'Project Team Coordinator · Time Steward',
  'No submission on behalf'
], 'Module 001 time-steward UI');
requireAll(timerRecovery, [
  'Module001ActiveTimerRecoveryPortal',
  '/api/timesheet/timers/active',
  'window.setInterval(load, 5000)',
  'window.setInterval(() => setClock(new Date()), 1000)',
  'Running timer recovered',
  'Stop timer',
  'Discard'
], 'Module 001 persistent active timer recovery');
requireAll(main, [
  "import Module001ActiveTimerRecoveryPortal from './module001/Module001ActiveTimerRecoveryPortal.jsx';",
  '<Module001ActiveTimerRecoveryPortal />'
], 'Module 001 recovery mount');
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
requireAll(certifyCss, [
  'main.app-shell.route-certify-integration .certify-integration-center',
  'max-height:calc(100dvh - 15rem)',
  'overflow-y:auto',
  'overscroll-behavior:contain'
], 'Module 038 bounded scrolling');
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

console.log('COMBINED_MODULES_001_005_012_037_038_CONTRACTS=PASS');
