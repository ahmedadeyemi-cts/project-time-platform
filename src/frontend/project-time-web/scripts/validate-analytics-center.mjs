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

const paths = {
  component: path.join(sourceRoot, 'AnalyticsCenter.jsx'),
  css: path.join(sourceRoot, 'analytics-center.css'),
  injector: path.join(scriptDirectory, 'inject-analytics-center.mjs'),
  package: path.join(webRoot, 'package.json'),
  app: path.join(sourceRoot, 'App.jsx'),
  registry: path.join(sourceRoot, 'module-availability-registry.js'),
  contracts: path.join(repositoryRoot, 'src/backend/ProjectTime.Api/Modules/AnalyticsCenterContracts.cs'),
  directory: path.join(repositoryRoot, 'src/backend/ProjectTime.Api/Modules/AnalyticsCenterDirectoryLoader.cs'),
  module: path.join(repositoryRoot, 'src/backend/ProjectTime.Api/Modules/AnalyticsCenterModule.cs'),
  catalog: path.join(repositoryRoot, 'src/backend/ProjectTime.Api/Modules/EnterpriseReportingCatalog.cs'),
  engine: path.join(repositoryRoot, 'src/backend/ProjectTime.Api/Modules/EnterpriseReportingEngine.cs'),
  loader: path.join(repositoryRoot, 'src/backend/ProjectTime.Api/Modules/EnterpriseReportingSourceLoader.cs'),
  repository: path.join(repositoryRoot, 'src/backend/ProjectTime.Api/Modules/EnterpriseReportingRepository.cs'),
  compatibility: path.join(repositoryRoot, 'src/backend/ProjectTime.Api/Modules/EnterpriseReportingModule.cs'),
  migration: path.join(repositoryRoot, 'database/migrations/055_analytics_center.sql'),
  rollback: path.join(repositoryRoot, 'database/rollback/055_analytics_center_rollback.sql'),
  project: path.join(repositoryRoot, 'src/backend/ProjectTime.Api/ProjectTime.Api.csproj'),
  docs: path.join(repositoryRoot, 'docs/modules/module-030-analytics-center/README.md')
};

let checks = 0;
function read(filePath) {
  if (!fs.existsSync(filePath)) throw new Error(`Analytics Center file is missing: ${path.relative(repositoryRoot, filePath)}`);
  return fs.readFileSync(filePath, 'utf8');
}
function assert(condition, message) { checks += 1; if (!condition) throw new Error(message); }
function contains(source, marker, label) { assert(source.includes(marker), `${label} is missing: ${marker}`); }
function count(source, marker) { return source.split(marker).length - 1; }

const component = read(paths.component);
const css = read(paths.css);
const injector = read(paths.injector);
const packageJson = JSON.parse(read(paths.package));

for (const marker of [
  'Analytics Center', 'Select report', 'Set criteria', 'All customers', 'All projects',
  'All engineers', 'All Project Managers', 'All teams', 'Refresh filter lists',
  'Preview report', 'Run & save', 'Actual analytics results', 'Analytics run history',
  '/api/analytics/catalog', '/api/analytics/filter-options',
  '/api/analytics/${persisted', '/api/analytics/history',
  '/api/analytics/runs/${runId}/export', "['xlsx', 'csv', 'json']",
  'Engineer — self only', 'PM — own portfolio'
]) contains(component, marker, 'working Analytics Center interface');

for (const forbidden of [
  'Fiscal Period', '030Q Reporting Readiness Closeout', 'Build Export Layout',
  'Save Report Definition Preview', 'selectedEngineerSummaryText',
  '/api/reports/030/filter-options', 'Validate 030 Readiness',
  'Reporting, Accounting, Invoicing, Analytics Command Center'
]) assert(!component.includes(forbidden), `Analytics Center must not contain legacy marker: ${forbidden}`);

for (const marker of [
  '.analytics-center', '.analytics-build-layout', '.analytics-report-cards',
  '.analytics-filter-grid', '.analytics-step-actions', '.analytics-source-grid',
  '.analytics-history-list', '@media print'
]) contains(css, marker, 'Analytics Center styling');

for (const marker of [
  "import AnalyticsCenter from './AnalyticsCenter.jsx';",
  '<AnalyticsCenter authSession={authSession} />',
  "displayName: 'Analytics Center'",
  "replaceAll('Financial Report Center', 'Analytics Center')",
  "replaceAll('Enterprise Reporting Center', 'Analytics Center')"
]) contains(injector, marker, 'Analytics Center generated integration');

contains(packageJson.scripts?.predev ?? '', 'inject-analytics-center.mjs', 'predev Analytics installer');
contains(packageJson.scripts?.prebuild ?? '', 'inject-analytics-center.mjs', 'prebuild Analytics installer');
contains(packageJson.scripts?.build ?? '', 'validate:analytics-center', 'full-build Analytics validation');
assert(packageJson.scripts?.['validate:analytics-center'] === 'node ./scripts/validate-analytics-center.mjs', 'Analytics package validator must be authoritative.');

if (fullRepositoryContext) {
  const contracts = read(paths.contracts);
  const directory = read(paths.directory);
  const module = read(paths.module);
  const catalog = read(paths.catalog);
  const engine = read(paths.engine);
  const loader = read(paths.loader);
  const repository = read(paths.repository);
  const compatibility = read(paths.compatibility);
  const migration = read(paths.migration);
  const rollback = read(paths.rollback);
  const project = read(paths.project);
  const docs = read(paths.docs);

  for (const marker of [
    'AnalyticsReportRequest', 'CustomerId', 'ProjectId', 'ProjectManagerUserId',
    'EngineerUserId', 'TeamId', 'DateFrom', 'DateTo', 'AnalyticsDirectorySnapshot'
  ]) contains(contracts, marker, 'Analytics request contract');

  for (const marker of [
    'FROM clients client', 'FROM teams team', 'JOIN team_memberships',
    'app_user.is_active = TRUE', '@broad OR client.client_id = ANY(@client_ids)',
    '@broad OR membership.user_id = ANY(@visible_user_ids)',
    "to_jsonb(client)->>'client_code'", 'DIRECTORY_CONFIGURATION_UNAVAILABLE'
  ]) contains(directory, marker, 'role-scoped directory loader');

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
  for (const code of reportCodes) contains(catalog, `"${code}"`, 'Analytics report catalog');
  for (const marker of ['Customer()', 'Project()', 'ProjectManager()', 'Engineer()', 'DateFrom()', 'DateTo()']) contains(catalog, marker, 'report-specific filter catalog');

  for (const marker of [
    '/api/analytics/catalog', '/api/analytics/filter-options', '/api/analytics/preview',
    '/api/analytics/run', '/api/analytics/history', '/api/analytics/runs/{runId:guid}/export',
    'moduleName = "Analytics Center"', 'fiscalPeriodFilterPresent = false',
    'organizationFilterPresent = false', 'CustomerOptions', 'ProjectOptions',
    'ProjectManagerOptions', 'EngineerOptions', 'TeamOptions', 'ScopeProjectsForOptions',
    'ApplyDirectoryFilters', 'CanonicalContractTypes', 'Fixed Price', 'Time and Material',
    'Pre-Sales', 'Non-billable', 'contractTypesAlignedToModules055C055D = true',
    'engineerReportsLockedToSelf', 'projectManagerReportsLockedToOwnPortfolio'
  ]) contains(module, marker, 'Analytics API and filter behavior');
  contains(module, '.Where(filter => filter.Key is not ("fiscalPeriod" or "organization" or "cadence"))', 'removed redundant criteria');

  for (const marker of [
    'Engineer scope: report data and person filters are locked',
    'Project Manager scope: report data and Project Manager filters are locked',
    'EngineerUserId = engineerOnly ? context.Actor.EffectiveUserId',
    'ProjectManagerUserId = pmOnly ? context.Actor.EffectiveUserId',
    'FilterProjects', 'TimeEntryDetail', 'EngineerUtilization', 'ProjectManagerPortfolio'
  ]) contains(engine, marker, 'server scope enforcement');

  for (const marker of [
    'time_entries', 'project_expense_uploads', 'work_billing_readiness_reviews',
    'work_closeout_records', 'project_notification_dispatches', 'resource_qualifications',
    'module076_items', 'operational_control_history', 'secure_project_information_requests',
    'pmo_control_items', 'SOURCE_SCOPE_RESTRICTED', 'row_to_json(source)::text',
    'AllowedUserIds', 'project_id'
  ]) contains(loader, marker, 'source-isolated loader');
  assert(!loader.includes('SELECT * FROM'), 'Analytics source loader must not use unrestricted SELECT *.');

  for (const marker of [
    'SaveRunAsync', 'LoadHistoryAsync', 'LoadRunAsync', 'RecordExportAsync',
    'LoadSavedViewsAsync', 'SaveViewAsync', 'DeleteSavedViewAsync', 'SHA256.HashData'
  ]) contains(repository, marker, 'Analytics persistence');
  for (const marker of ['BuildExcel', 'BuildCsv', 'BuildJson']) contains(compatibility, marker, 'Analytics export compatibility');

  for (const table of ['enterprise_report_runs', 'enterprise_report_saved_views', 'enterprise_report_exports']) contains(migration, table, 'migration 055 table');
  contains(migration, 'projectpulse055_block_analytics_evidence_mutation', 'immutable analytics evidence');
  contains(migration, "'055_analytics_center'", 'migration 055 registration');
  contains(migration, "'ANALYTICS_CENTER'", 'Analytics feature identity');
  contains(migration, "WHERE feature_code = 'FINANCIAL_REPORT_CENTER'", 'legacy feature retirement');
  for (const table of ['enterprise_report_runs', 'enterprise_report_saved_views', 'enterprise_report_exports']) contains(rollback, `DROP TABLE IF EXISTS ${table}`, 'migration 055 rollback');

  contains(project, 'app.MapEnterpriseReportingEndpoints();', 'compatibility API registration');
  contains(project, 'app.MapAnalyticsCenterEndpoints();', 'Analytics API registration');
  assert(count(project, 'app.MapAnalyticsCenterEndpoints();') === 1, 'Analytics endpoints must register once.');
  contains(project, 's/054_enterprise_reporting_center/055_analytics_center/g', 'migration 055 repository normalization');
  contains(project, 's/migration_054_required/migration_055_required/g', 'migration 055 response normalization');

  for (const marker of [
    'Analytics Center', '24 report types', 'Customer Directory', 'Modules 055C and 055D',
    'Engineer', 'Project Manager', 'Team', 'immutable',
    'migration `055_analytics_center`', 'No deployment', 'Module 030'
  ]) contains(docs, marker, 'Analytics documentation');
}

execFileSync(process.execPath, [paths.injector], { cwd: webRoot, stdio: 'inherit' });
const generatedApp = read(paths.app);
const generatedRegistry = read(paths.registry);
assert(count(generatedApp, "import AnalyticsCenter from './AnalyticsCenter.jsx';") === 1, 'Generated App must import Analytics Center once.');
assert(count(generatedApp, '<AnalyticsCenter authSession={authSession} />') === 1, 'Generated App must mount Analytics Center once.');
assert(!generatedApp.includes('<EnterpriseReportingCenter authSession={authSession} />'), 'Former Enterprise Reporting mount must be absent.');
assert(!generatedApp.includes('<FinancialOperationsRecoveryWorkspace mode="reporting" authSession={authSession} />'), 'Legacy Financial Report Center mount must be absent.');
assert(!generatedApp.includes('selectedEngineerSummaryText'), 'Generated App must not retain the legacy Engineer-render exception.');
assert(count(generatedRegistry, "moduleNumber: '030'") === 1, 'Module 030 registry entry must remain unique.');
contains(generatedRegistry, "displayName: 'Analytics Center'", 'generated Module 030 identity');

console.log(`ANALYTICS_CENTER_VALIDATION_CHECKS=${checks}`);
console.log(`ANALYTICS_CENTER_FULL_REPOSITORY_CONTEXT=${fullRepositoryContext ? 'YES' : 'NO'}`);
console.log('ANALYTICS_CENTER_REPORT_COUNT=24');
console.log('MODULE_030_ANALYTICS_CENTER=PASS');
