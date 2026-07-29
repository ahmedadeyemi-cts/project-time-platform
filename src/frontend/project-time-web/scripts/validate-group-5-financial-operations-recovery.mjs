import fs from 'node:fs';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const webRoot = path.resolve(scriptDirectory, '..');
const repositoryRoot = path.resolve(webRoot, '../../..');
const sourceRoot = path.join(webRoot, 'src');
const fullRepositoryContext = fs.existsSync(path.join(repositoryRoot, '.git'))
  || fs.existsSync(path.join(repositoryRoot, '.github/workflows/projectpulse-ci.yml'));

const files = {
  bridge: path.join(repositoryRoot, 'src/backend/ProjectTime.Api/Modules/ProjectFinancialTruthReportingBridge.cs'),
  contracts: path.join(repositoryRoot, 'src/backend/ProjectTime.Api/Modules/FinancialOperationsContracts.cs'),
  sourceLoader: path.join(repositoryRoot, 'src/backend/ProjectTime.Api/Modules/FinancialOperationsSourceLoader.cs'),
  reportEngine: path.join(repositoryRoot, 'src/backend/ProjectTime.Api/Modules/FinancialOperationsReportEngine.cs'),
  repository: path.join(repositoryRoot, 'src/backend/ProjectTime.Api/Modules/FinancialOperationsRepository.cs'),
  workItems: path.join(repositoryRoot, 'src/backend/ProjectTime.Api/Modules/FinancialOperationsWorkItemFactory.cs'),
  module: path.join(repositoryRoot, 'src/backend/ProjectTime.Api/Modules/FinancialOperationsRecoveryModule.cs'),
  project: path.join(repositoryRoot, 'src/backend/ProjectTime.Api/ProjectTime.Api.csproj'),
  migration: path.join(repositoryRoot, 'database/migrations/051_financial_operations_reporting_recovery.sql'),
  rollback: path.join(repositoryRoot, 'database/rollback/051_financial_operations_reporting_recovery_rollback.sql'),
  documentation: path.join(repositoryRoot, 'docs/modules/group-5-financial-operations/README.md'),
  component: path.join(sourceRoot, 'FinancialOperationsRecoveryWorkspace.jsx'),
  css: path.join(sourceRoot, 'financial-operations-recovery-workspace.css'),
  injector: path.join(scriptDirectory, 'inject-group-5-financial-operations-recovery.mjs'),
  package: path.join(webRoot, 'package.json'),
  app: path.join(sourceRoot, 'App.jsx'),
  registry: path.join(sourceRoot, 'module-availability-registry.js')
};

let checks = 0;
function read(filePath) {
  if (!fs.existsSync(filePath)) throw new Error(`Required Group 5 file is missing: ${path.relative(repositoryRoot, filePath)}`);
  return fs.readFileSync(filePath, 'utf8');
}
function assert(condition, message) { checks += 1; if (!condition) throw new Error(message); }
function contains(source, marker, label) { assert(source.includes(marker), `${label} is missing: ${marker}`); }
function count(source, marker) { return source.split(marker).length - 1; }

const component = read(files.component);
const css = read(files.css);
const injector = read(files.injector);
const packageJson = JSON.parse(read(files.package));

contains(component, "import { usSignalLogoDataUrl } from './assets/usSignalLogoData.js';", 'official US Signal branding');
contains(component, 'data-projectpulse-group5="financial-recovery"', 'Group 5 UI identity');
for (const marker of [
  'Actual report catalog',
  'Search, preview, run, and export',
  'Report run history',
  'Source health and recovery',
  'Financial Operations Workbench',
  'canRetrySources',
  'Closeout Notification Recovery',
  'Invoice & Billing Recovery',
  'Current expense drill-down',
  'Group 4 routing and Module 065 delivery',
  'Module 005 remains a separate upload workspace'
]) contains(component, marker, 'Group 5 enterprise workspace');
for (const api of [
  '/api/financial-operations/reports/catalog',
  '/api/financial-operations/reports/${persisted',
  '/api/financial-operations/reports/history',
  '/api/financial-operations/reports/runs/${runId}/export',
  '/api/financial-operations/sources/',
  '/api/financial-operations/workbench',
  '/api/financial-operations/modules/'
]) contains(component, api, 'Group 5 frontend API');
assert(!component.includes('CertifyIntegrationCenter'), 'Group 5 workspace must not mount or replace Module 038.');
assert(!component.includes('ProjectAllocationInfoPanel'), 'Module 042 recovery must not duplicate Module 005.');

for (const marker of [
  '.group5-financial-operations',
  '.group5-hero',
  '.group5-report-picker',
  '.group5-table-wrap',
  '.group5-source-grid',
  '.group5-work-item-list',
  '.group5-module-project-grid',
  '@media print'
]) contains(css, marker, 'Group 5 styling');

for (const marker of [
  'GROUP_5_FINANCIAL_OPERATIONS_ROUTES_START',
  'GROUP_5_MODULE_039_RECOVERY_PANEL',
  'GROUP_5_MODULE_040_RECOVERY_PANEL',
  'GROUP_5_MODULE_041_RECOVERY_PANEL',
  'GROUP_5_MODULE_042_RECOVERY_PANEL',
  "moduleNumber: '031'",
  "moduleNumber: '030'"
]) contains(injector, marker, 'Group 5 injector');
assert(!/CertifyIntegrationCenter|certify-integration|Module038/.test(injector), 'Group 5 injector must not target Module 038.');
assert(!injector.includes("import CertifyIntegrationCenter"), 'Group 5 must not import Module 038.');

const predev = packageJson.scripts?.predev ?? '';
const prebuild = packageJson.scripts?.prebuild ?? '';
const build = packageJson.scripts?.build ?? '';
contains(predev, 'inject-group-4-project-notification-automation.mjs', 'Group 4 predev preservation');
contains(predev, 'inject-group-5-financial-operations-recovery.mjs', 'Group 5 predev installation');
contains(prebuild, 'inject-group-4-project-notification-automation.mjs', 'Group 4 prebuild preservation');
contains(prebuild, 'inject-group-5-financial-operations-recovery.mjs', 'Group 5 prebuild installation');
contains(build, 'validate:group4-project-notifications', 'Group 4 validation preservation');
contains(build, 'validate:group5-financial-operations', 'Group 5 complete-build validation');
assert(packageJson.scripts?.['validate:group5-financial-operations'] === 'node ./scripts/validate-group-5-financial-operations-recovery.mjs', 'Group 5 package validator must be authoritative.');

if (fullRepositoryContext) {
  const bridge = read(files.bridge);
  const contracts = read(files.contracts);
  const sourceLoader = read(files.sourceLoader);
  const reportEngine = read(files.reportEngine);
  const repository = read(files.repository);
  const workItems = read(files.workItems);
  const module = read(files.module);
  const project = read(files.project);
  const migration = read(files.migration);
  const rollback = read(files.rollback);
  const documentation = read(files.documentation);

  contains(bridge, 'public static partial class ProjectFinancialTruthModule', 'Group 3 partial bridge');
  contains(bridge, 'BuildAsync(context, "rate-card")', 'authoritative Group 3 calculation consumption');
  contains(bridge, 'FinancialOperationsSourceState', 'source-state bridge');
  assert(!bridge.includes('SELECT '), 'The Group 3 reporting bridge must not create a competing financial query model.');

  for (const marker of [
    'FinancialReportRequest',
    'FinancialReportDefinition',
    'FinancialOperationsTruthSnapshot',
    'FinancialOperationsSourceState',
    'FinancialOperationsWorkItem',
    'FinancialOperationsAction'
  ]) contains(contracts, marker, 'Group 5 contracts');

  for (const marker of [
    'approved_time_entries',
    'billing_readiness_reviews',
    'project_closeout_records',
    'project_notification_dispatches',
    'This source is unavailable. Other healthy financial content remains visible',
    '/api/financial-operations/sources/'
  ]) contains(sourceLoader, marker, 'source-isolated loader');

  for (const report of [
    'project_financial_health',
    'project_hours_consumption',
    'project_expense_status',
    'billing_readiness',
    'project_closeout_readiness',
    'notification_delivery'
  ]) contains(reportEngine, report, 'actual report catalog');
  for (const state of ['"complete"', '"partial"', '"no_data"', '"source_unavailable"']) contains(reportEngine, state, 'report result state');
  contains(reportEngine, 'FilterProjects', 'report filters');
  contains(reportEngine, 'Approved hours', 'approved-time reporting');
  contains(reportEngine, 'Current Module 005 expense total', 'intentional expense summary');

  for (const marker of [
    'SaveReportRunAsync',
    'LoadReportHistoryAsync',
    'MarkReportExportedAsync',
    'UpsertWorkItemsAsync',
    'UpdateWorkItemAsync',
    'RecordActionAsync'
  ]) contains(repository, marker, 'durable reporting and recovery repository');
  contains(workItems, 'source_failure', 'source-failure work item');
  contains(workItems, 'billing_readiness', 'billing work item');
  contains(workItems, 'closeout_incomplete', 'closeout work item');
  contains(workItems, 'notification:', 'notification recovery work item');

  const endpoints = [
    '/api/financial-operations/reports/catalog',
    '/api/financial-operations/reports/preview',
    '/api/financial-operations/reports/run',
    '/api/financial-operations/reports/history',
    '/api/financial-operations/reports/runs/{runId:guid}/export',
    '/api/financial-operations/sources',
    '/api/financial-operations/sources/{sourceKey}/retry',
    '/api/financial-operations/workbench',
    '/api/financial-operations/workbench/refresh',
    '/api/financial-operations/workbench/{workItemId:guid}/{action}',
    '/api/financial-operations/modules/{moduleCode}'
  ];
  for (const endpoint of endpoints) contains(module, endpoint, 'Group 5 API');
  contains(module, 'actualSessionVerified = true', 'actual-session attribution');
  contains(module, 'effectiveSessionVerified = true', 'effective-session attribution');
  contains(module, 'healthySourcesRemainVisible = true', 'partial-source continuity');
  contains(module, 'Group 4 routing and Module 065 delivery', 'Module 041 ownership');
  contains(module, 'Module 005 is not mounted or duplicated', 'Module 042 boundary');
  contains(module, 'module038 = "regression_only_unchanged"', 'Module 038 regression-only declaration');
  assert(!/Map(Post|Put|Patch|Delete).*certify|CertifyIntegration/i.test(module), 'Group 5 API must not mutate Module 038.');

  for (const table of [
    'financial_report_runs',
    'financial_operations_work_items',
    'financial_operations_actions'
  ]) contains(migration, table, 'migration 051 table');
  for (const permission of [
    'VIEW_FINANCIAL_REPORT_CENTER',
    'RUN_FINANCIAL_REPORTS',
    'EXPORT_FINANCIAL_REPORTS',
    'VIEW_FINANCIAL_OPERATIONS_WORKBENCH',
    'MANAGE_FINANCIAL_OPERATIONS_RECOVERY',
    'RETRY_FINANCIAL_SOURCES',
    'VIEW_ACCOUNTING_RECONCILIATION_RECOVERY',
    'VIEW_PROJECT_CLOSEOUT_RECOVERY',
    'VIEW_CLOSEOUT_NOTIFICATION_RECOVERY',
    'VIEW_BILLING_RECOVERY'
  ]) contains(migration, permission, 'migration 051 permission');
  contains(migration, 'projectpulse051_block_financial_action_mutation', 'immutable action evidence');
  contains(migration, "'051_financial_operations_reporting_recovery'", 'migration registration');
  assert(!/certify_connection_profiles|certify_expense_import_runs|MANAGE_CERTIFY_CONNECTION/.test(migration), 'Migration 051 must not alter Certify configuration.');
  for (const table of ['financial_report_runs', 'financial_operations_work_items', 'financial_operations_actions']) contains(rollback, `DROP TABLE IF EXISTS ${table}`, 'migration 051 rollback');

  contains(project, 'app.MapProjectNotificationAutomationEndpoints();', 'Group 4 registration preservation');
  contains(project, 'app.MapFinancialOperationsRecoveryEndpoints();', 'Group 5 registration');
  contains(project, 'ProjectFinancialTruthGeneratedModule', 'Group 3 partial generated source');
  contains(project, 's/public static class ProjectFinancialTruthModule/public static partial class ProjectFinancialTruthModule/', 'Group 3 non-destructive partial conversion');
  assert(count(project, 'app.MapFinancialOperationsRecoveryEndpoints();') === 1, 'Group 5 endpoint registration must appear exactly once.');

  for (const marker of [
    'Module 030',
    'Module 031',
    'Module 039',
    'Module 040',
    'Module 041',
    'Module 042',
    'Module 038 is regression-only',
    'No deployment',
    'migration 051'
  ]) contains(documentation, marker, 'Group 5 documentation');
} else {
  console.log('GROUP_5_BACKEND_MIGRATION_DOCUMENTATION=SKIPPED_FRONTEND_CONTAINER_CONTEXT');
}

execFileSync(process.execPath, [files.injector], { cwd: webRoot, stdio: 'inherit' });
const generatedApp = read(files.app);
const generatedRegistry = read(files.registry);
assert(count(generatedApp, "import FinancialOperationsRecoveryWorkspace from './FinancialOperationsRecoveryWorkspace.jsx';") === 1, 'Generated App must import Group 5 exactly once.');
assert(count(generatedApp, 'GROUP_5_FINANCIAL_OPERATIONS_ROUTES_START') === 1, 'Generated App must contain one Group 5 route block.');
assert(count(generatedApp, '<FinancialOperationsRecoveryWorkspace mode="reporting" authSession={authSession} />') === 1, 'Module 030 mount must be unique.');
assert(count(generatedApp, '<FinancialOperationsRecoveryWorkspace mode="workbench" authSession={authSession} />') === 1, 'Module 031 mount must be unique.');
for (const moduleCode of ['039', '040', '041', '042']) assert(count(generatedApp, `<FinancialOperationsRecoveryWorkspace moduleCode="${moduleCode}" authSession={authSession} />`) === 1, `Module ${moduleCode} recovery panel must be unique.`);
assert(count(generatedRegistry, "moduleNumber: '031'") === 1, 'Generated Module 031 registry entry must be unique.');
assert(count(generatedRegistry, "moduleNumber: '030'") === 1, 'Generated Module 030 registry entry must remain unique.');
assert(!generatedApp.includes('GROUP_5_MODULE_038'), 'Generated App must not include a Group 5 Module 038 mount.');

console.log(`GROUP_5_VALIDATION_CHECKS=${checks}`);
console.log(`GROUP_5_FULL_REPOSITORY_CONTEXT=${fullRepositoryContext ? 'YES' : 'NO'}`);
console.log('GROUP_5_FINANCIAL_OPERATIONS_RECOVERY=PASS');
