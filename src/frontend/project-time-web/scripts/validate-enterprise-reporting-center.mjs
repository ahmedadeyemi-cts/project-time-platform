import fs from 'node:fs';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const webRoot = path.resolve(scriptDirectory, '..');
const repositoryRoot = path.resolve(webRoot, '../../..');
const sourceRoot = path.join(webRoot, 'src');

const files = {
  contracts: path.join(repositoryRoot, 'src/backend/ProjectTime.Api/Modules/EnterpriseReportingContracts.cs'),
  catalog: path.join(repositoryRoot, 'src/backend/ProjectTime.Api/Modules/EnterpriseReportingCatalog.cs'),
  engine: path.join(repositoryRoot, 'src/backend/ProjectTime.Api/Modules/EnterpriseReportingEngine.cs'),
  loader: path.join(repositoryRoot, 'src/backend/ProjectTime.Api/Modules/EnterpriseReportingSourceLoader.cs'),
  repository: path.join(repositoryRoot, 'src/backend/ProjectTime.Api/Modules/EnterpriseReportingRepository.cs'),
  module: path.join(repositoryRoot, 'src/backend/ProjectTime.Api/Modules/EnterpriseReportingModule.cs'),
  migration: path.join(repositoryRoot, 'database/migrations/054_enterprise_reporting_center.sql'),
  rollback: path.join(repositoryRoot, 'database/rollback/054_enterprise_reporting_center_rollback.sql'),
  documentation: path.join(repositoryRoot, 'docs/modules/module-030-enterprise-reporting/README.md'),
  component: path.join(sourceRoot, 'EnterpriseReportingCenter.jsx'),
  css: path.join(sourceRoot, 'enterprise-reporting-center.css'),
  injector: path.join(scriptDirectory, 'inject-enterprise-reporting-center.mjs'),
  package: path.join(webRoot, 'package.json'),
  project: path.join(repositoryRoot, 'src/backend/ProjectTime.Api/ProjectTime.Api.csproj'),
  app: path.join(sourceRoot, 'App.jsx'),
  registry: path.join(sourceRoot, 'module-availability-registry.js')
};

let checks = 0;
function read(filePath) {
  if (!fs.existsSync(filePath)) throw new Error(`Enterprise Reporting file is missing: ${path.relative(repositoryRoot, filePath)}`);
  return fs.readFileSync(filePath, 'utf8');
}
function assert(condition, message) { checks += 1; if (!condition) throw new Error(message); }
function contains(source, marker, label) { assert(source.includes(marker), `${label} is missing: ${marker}`); }
function count(source, marker) { return source.split(marker).length - 1; }

const contracts = read(files.contracts);
const catalog = read(files.catalog);
const engine = read(files.engine);
const loader = read(files.loader);
const repository = read(files.repository);
const module = read(files.module);
const migration = read(files.migration);
const rollback = read(files.rollback);
const documentation = read(files.documentation);
const component = read(files.component);
const css = read(files.css);
const injector = read(files.injector);
const project = read(files.project);
const packageJson = JSON.parse(read(files.package));

for (const marker of [
  'EnterpriseReportRequest', 'EnterpriseReportFilterDefinition', 'EnterpriseReportDefinition',
  'EnterpriseReportFilterOptions', 'EnterpriseReportResult', 'EnterpriseReportRunRecord',
  'EnterpriseSavedViewRecord', 'EnterpriseReportingContext'
]) contains(contracts, marker, 'report contract');

const reportCodes = [
  'project_portfolio', 'project_financial_health', 'project_budget_forecast',
  'project_hours_consumption', 'time_entry_detail', 'engineer_workload',
  'engineer_utilization', 'project_manager_portfolio', 'project_team_assignments',
  'customer_project_summary', 'expense_detail', 'sell_delivery_context',
  'billing_readiness', 'project_closeout_readiness', 'notification_delivery',
  'qualification_expiration', 'oncall_coverage', 'issue_feature_lifecycle',
  'release_deployment_readiness', 'service_health_slo', 'data_governance_retention',
  'customer_delivery_acceptance', 'secure_project_information', 'pmo_project_controls'
];
for (const code of reportCodes) contains(catalog, `"${code}"`, 'enterprise report catalog');
assert(reportCodes.length >= 24, 'Enterprise report catalog must cover at least 24 system facets.');
for (const marker of ['Customer()', 'Project()', 'ProjectManager()', 'Engineer()', 'DateFrom()', 'DateTo()', 'WorkflowStatus()', 'Severity()', 'ModuleCode()']) contains(catalog, marker, 'dynamic filter catalog');
contains(catalog, 'ForContext(EnterpriseReportingContext context)', 'role-aware report catalog');
contains(catalog, 'financialVisible', 'financial report visibility');
contains(catalog, 'control_plane', 'operational-control report boundary');

for (const marker of [
  'Engineer scope: report data and person filters are locked',
  'Project Manager scope: report data and Project Manager filters are locked',
  'EngineerUserId = engineerOnly ? context.Actor.EffectiveUserId',
  'ProjectManagerUserId = pmOnly ? context.Actor.EffectiveUserId',
  'reportPermissionDoesNotExpandRecordOrFieldScope = true'
]) contains(engine, marker, 'server scope enforcement');
for (const marker of ['BuildFilterOptions', 'FilterProjects', 'TimeEntryDetail', 'EngineerUtilization', 'ProjectManagerPortfolio', 'GenericRows']) contains(engine, marker, 'report engine capability');
for (const state of ['"complete"', '"partial"', '"no_data"', '"source_unavailable"']) contains(engine, state, 'report result state');

for (const marker of [
  'time_entries', 'project_expense_uploads', 'work_billing_readiness_reviews',
  'work_closeout_records', 'project_notification_dispatches', 'resource_qualifications',
  'module076_items', 'operational_control_history', 'secure_project_information_requests',
  'pmo_control_items', 'SOURCE_SCOPE_RESTRICTED', 'row_to_json(source)::text'
]) contains(loader, marker, 'source-isolated reporting loader');
contains(loader, 'AllowedUserIds', 'person-scope source filtering');
contains(loader, 'project_id', 'project-scope source filtering');
assert(!loader.includes('SELECT * FROM'), 'Reporting source loader must not use unrestricted SELECT *.');

for (const marker of [
  'SaveRunAsync', 'LoadHistoryAsync', 'LoadRunAsync', 'RecordExportAsync',
  'LoadSavedViewsAsync', 'SaveViewAsync', 'DeleteSavedViewAsync', 'SHA256.HashData'
]) contains(repository, marker, 'report persistence and audit');

const endpoints = [
  '/api/enterprise-reporting/catalog', '/api/enterprise-reporting/filter-options',
  '/api/enterprise-reporting/preview', '/api/enterprise-reporting/run',
  '/api/enterprise-reporting/history', '/api/enterprise-reporting/runs/{runId:guid}/export',
  '/api/enterprise-reporting/saved-views', '/api/enterprise-reporting/saved-views/{savedViewId:guid}'
];
for (const endpoint of endpoints) contains(module, endpoint, 'enterprise reporting API');
for (const marker of ['BuildCsv', 'BuildJson', 'BuildExcel', 'XLWorkbook', 'ViewAsReadOnly', 'CanExport', 'IsEngineerOnly', 'IsPmOnly']) contains(module, marker, 'report execution and export');

for (const table of ['enterprise_report_runs', 'enterprise_report_saved_views', 'enterprise_report_exports']) contains(migration, table, 'migration 054 table');
for (const permission of ['VIEW_ENTERPRISE_REPORTING', 'RUN_ENTERPRISE_REPORTING', 'EXPORT_ENTERPRISE_REPORTING', 'MANAGE_ENTERPRISE_REPORTING']) contains(migration, permission, 'migration 054 permission');
contains(migration, 'projectpulse054_block_enterprise_report_evidence_mutation', 'immutable report evidence');
contains(migration, "'054_enterprise_reporting_center'", 'migration 054 registration');
for (const table of ['enterprise_report_runs', 'enterprise_report_saved_views', 'enterprise_report_exports']) contains(rollback, `DROP TABLE IF EXISTS ${table}`, 'migration 054 rollback');

for (const marker of [
  'EnterpriseModulePage', 'Enterprise Reporting Center', 'Report catalog',
  'Choose a report first; only filters relevant to that report appear.',
  '/api/enterprise-reporting/catalog', '/api/enterprise-reporting/filter-options',
  '/api/enterprise-reporting/preview', '/api/enterprise-reporting/run',
  '/api/enterprise-reporting/history', '/api/enterprise-reporting/saved-views',
  'Engineer — self only', 'PM — own portfolio', 'Source accountability',
  "['xlsx', 'csv', 'json']", 'Run and record'
]) contains(component, marker, 'enterprise reporting UI');
assert(!component.includes('All report filters are shown'), 'The interface must not show one static filter set for every report.');
for (const marker of ['.enterprise-reporting-center', '.enterprise-reporting-catalog-grid', '.enterprise-reporting-command-card', '.enterprise-reporting-source-grid', '.enterprise-reporting-history-list', '@media print']) contains(css, marker, 'enterprise reporting styles');

contains(injector, "import EnterpriseReportingCenter from './EnterpriseReportingCenter.jsx';", 'reporting App import');
contains(injector, '<EnterpriseReportingCenter authSession={authSession} />', 'reporting route mount');
contains(injector, 'Enterprise Reporting Center', 'Module 030 identity');

contains(project, 'app.MapEnterpriseReportingEndpoints();', 'API endpoint registration');
const predev = packageJson.scripts?.predev ?? '';
const prebuild = packageJson.scripts?.prebuild ?? '';
const build = packageJson.scripts?.build ?? '';
contains(predev, 'inject-enterprise-reporting-center.mjs', 'predev reporting installer');
contains(prebuild, 'inject-enterprise-reporting-center.mjs', 'prebuild reporting installer');
contains(build, 'validate:enterprise-reporting', 'complete-build reporting validation');
assert(packageJson.scripts?.['validate:enterprise-reporting'] === 'node ./scripts/validate-enterprise-reporting-center.mjs', 'Reporting validator package registration must be authoritative.');

for (const marker of [
  '24 report types', 'Engineer', 'Project Manager', 'report-specific filters',
  'immutable', 'migration 054', 'No deployment', 'Module 030'
]) contains(documentation, marker, 'reporting documentation');

execFileSync(process.execPath, [files.injector], { cwd: webRoot, stdio: 'inherit' });
const generatedApp = read(files.app);
const generatedRegistry = read(files.registry);
assert(count(generatedApp, "import EnterpriseReportingCenter from './EnterpriseReportingCenter.jsx';") === 1, 'Generated App must import Enterprise Reporting once.');
assert(count(generatedApp, '<EnterpriseReportingCenter authSession={authSession} />') === 1, 'Generated App must mount Enterprise Reporting once.');
assert(!generatedApp.includes('<FinancialOperationsRecoveryWorkspace mode="reporting" authSession={authSession} />'), 'Legacy Module 030 reporting mount must be replaced.');
assert(count(generatedRegistry, "moduleNumber: '030'") === 1, 'Module 030 registry entry must remain unique.');
contains(generatedRegistry, "displayName: 'Enterprise Reporting Center'", 'generated Module 030 identity');

console.log(`ENTERPRISE_REPORTING_VALIDATION_CHECKS=${checks}`);
console.log(`ENTERPRISE_REPORTING_REPORT_COUNT=${reportCodes.length}`);
console.log('ENTERPRISE_REPORTING_CENTER=PASS');
