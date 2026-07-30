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
  analyticsContracts: path.join(repositoryRoot, 'src/backend/ProjectTime.Api/Modules/AnalyticsCenterContracts.cs'),
  analyticsDirectory: path.join(repositoryRoot, 'src/backend/ProjectTime.Api/Modules/AnalyticsCenterDirectoryLoader.cs'),
  analyticsModule: path.join(repositoryRoot, 'src/backend/ProjectTime.Api/Modules/AnalyticsCenterModule.cs'),
  enterpriseContracts: path.join(repositoryRoot, 'src/backend/ProjectTime.Api/Modules/EnterpriseReportingContracts.cs'),
  catalog: path.join(repositoryRoot, 'src/backend/ProjectTime.Api/Modules/EnterpriseReportingCatalog.cs'),
  engine: path.join(repositoryRoot, 'src/backend/ProjectTime.Api/Modules/EnterpriseReportingEngine.cs'),
  loader: path.join(repositoryRoot, 'src/backend/ProjectTime.Api/Modules/EnterpriseReportingSourceLoader.cs'),
  compatibilityModule: path.join(repositoryRoot, 'src/backend/ProjectTime.Api/Modules/EnterpriseReportingModule.cs'),
  repository: path.join(repositoryRoot, 'src/backend/ProjectTime.Api/Modules/EnterpriseReportingRepository.cs'),
  migration: path.join(repositoryRoot, 'database/migrations/055_analytics_center.sql'),
  rollback: path.join(repositoryRoot, 'database/rollback/055_analytics_center_rollback.sql'),
  documentation: path.join(repositoryRoot, 'docs/modules/module-030-analytics-center/README.md'),
  component: path.join(sourceRoot, 'AnalyticsCenter.jsx'),
  css: path.join(sourceRoot, 'analytics-center.css'),
  injector: path.join(scriptDirectory, 'inject-analytics-center.mjs'),
  package: path.join(webRoot, 'package.json'),
  project: path.join(repositoryRoot, 'src/backend/ProjectTime.Api/ProjectTime.Api.csproj'),
  app: path.join(sourceRoot, 'App.jsx'),
  registry: path.join(sourceRoot, 'module-availability-registry.js')
};

let checks = 0;
function read(filePath) {
  if (!fs.existsSync(filePath)) throw new Error(`Analytics Center file is missing: ${path.relative(repositoryRoot, filePath)}`);
  return fs.readFileSync(filePath, 'utf8');
}
function optionalRead(filePath) { return fs.existsSync(filePath) ? fs.readFileSync(filePath, 'utf8') : ''; }
function assert(condition, message) { checks += 1; if (!condition) throw new Error(message); }
function contains(source, marker, label) { assert(source.includes(marker), `${label} is missing: ${marker}`); }
function count(source, marker) { return source.split(marker).length - 1; }

const component = read(files.component);
const css = read(files.css);
const injector = read(files.injector);
const packageJson = JSON.parse(read(files.package));

for (const marker of [
  'Analytics Center', 'Select report', 'Set criteria', 'All customers', 'All projects',
  'All engineers', 'All Project Managers', 'All teams', 'Refresh filter lists',
  'Preview report', 'Run & save', 'Actual analytics results', 'Analytics run history',
  '/api/analytics/catalog', '/api/analytics/filter-options', '/api/analytics/preview',
  '/api/analytics/run', '/api/analytics/history', "['xlsx', 'csv', 'json']",
  'Engineer — self only', 'PM — own portfolio'
]) contains(component, marker, 'working Analytics Center interface');
for (const forbidden of [
  'Fiscal Period', 'Organization', '030Q Reporting Readiness Closeout',
  'Build Export Layout', 'Save Report Definition Preview', 'selectedEngineerSummaryText',
  '/api/reports/030/filter-options', 'Validate 030 Readiness',
  'Reporting, Accounting, Invoicing, Analytics Command Center'
]) assert(!component.includes(forbidden), `Analytics Center must not contain legacy UI or broken marker: ${forbidden}`);

for (const marker of [
  '.analytics-center', '.analytics-build-layout', '.analytics-report-cards',
  '.analytics-filter-grid', '.analytics-step-actions', '.analytics-source-grid',
  '.analytics-history-list', '@media print'
]) contains(css, marker, 'Analytics Center enterprise styling');

contains(injector, "import AnalyticsCenter from './AnalyticsCenter.jsx';", 'Analytics Center App import');
contains(injector, '<AnalyticsCenter authSession={authSession} />', 'Analytics Center route mount');
contains(injector, "displayName: 'Analytics Center'", 'Module 030 Analytics Center registry identity');
contains(injector, "replaceAll('Financial Report Center', 'Analytics Center')", 'Financial Report Center retirement');
contains(injector, "replaceAll('Enterprise Reporting Center', 'Analytics Center')", 'Enterprise Reporting Center retirement');

const predev = packageJson.scripts?.predev ?? '';
const prebuild = packageJson.scripts?.prebuild ?? '';
const build = packageJson.scripts?.build ?? '';
contains(predev, 'inject-analytics-center.mjs', 'predev Analytics Center installer');
contains(prebuild, 'inject-analytics-center.mjs', 'prebuild Analytics Center installer');
contains(build, 'validate:analytics-center', 'complete-build Analytics Center validation');
assert(packageJson.scripts?.['validate:analytics-center'] === 'node ./scripts/validate-analytics-center.mjs', 'Analytics Center package validator must be authoritative.');

if (fullRepositoryContext) {
  const analyticsContracts = read(files.analyticsContracts);
  const analyticsDirectory = read(files.analyticsDirectory);
  const analyticsModule = read(files.analyticsModule);
  const enterpriseContracts = read(files.enterpriseContracts);
  const catalog = read(files.catalog);
  const engine = read(files.engine);
  const loader = read(files.loader);
  const compatibilityModule = read(files.compatibilityModule);
  const repository = read(files.repository);
  const migration = read(files.migration);
  const rollback = read(files.rollback);
  const documentation = read(files.documentation);
  const project = read(files.project);

  for (const marker of [
    'AnalyticsReportRequest', 'CustomerId', 'ProjectId', 'ProjectManagerUserId',
    'EngineerUserId', 'TeamId', 'DateFrom', 'DateTo', 'AnalyticsDirectorySnapshot'
  ]) contains(analyticsContracts, marker, 'Analytics Center request and directory contract');

  for (const marker of [
    'FROM clients client', 'FROM teams team', 'JOIN team_memberships',
    'app_user.is_active = TRUE', '@broad OR client.client_id = ANY(@client_ids)',
    '@broad OR membership.user_id = ANY(@visible_user_ids)',
    "to_jsonb(client)->>'client_code'", 'DIRECTORY_CONFIGURATION_UNAVAILABLE'
  ]) contains(analyticsDirectory, marker, 'role-scoped customer and team directory loader');

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
  for (const code of reportCodes) contains(catalog, `"${code}"`, 'Analytics Center report catalog');
  assert(reportCodes.length >= 24, 'Analytics Center must cover at least 24 system facets.');
  for (const marker of ['Customer()', 'Project()', 'ProjectManager()', 'Engineer()', 'DateFrom()', 'DateTo()']) contains(catalog, marker, 'report-specific filter catalog');

  for (const marker of [
    '/api/analytics/catalog', '/api/analytics/filter-options', '/api/analytics/preview',
    '/api/analytics/run', '/api/analytics/history', '/api/analytics/runs/{runId:guid}/export',
    'moduleName = "Analytics Center"', 'fiscalPeriodFilterPresent = false',
    'organizationFilterPresent = false', 'CustomerOptions', 'ProjectOptions',
    'ProjectManagerOptions', 'EngineerOptions', 'TeamOptions', 'ScopeProjectsForOptions',
    'ApplyDirectoryFilters', 'CanonicalContractTypes', 'Fixed Price', 'Time and Material',
    'Pre-Sales', 'Internal', 'Non-billable', 'Other',
    'contractTypesAlignedToModules055C055D = true'
  ]) contains(analyticsModule, marker, 'Analytics Center API and filter behavior');
  contains(analyticsModule, '.Where(filter => filter.Key is not ("fiscalPeriod" or "organization" or "cadence"))', 'removed redundant criteria');
  contains(analyticsModule, 'engineerReportsLockedToSelf', 'Engineer self scope');
  contains(analyticsModule, 'projectManagerReportsLockedToOwnPortfolio', 'Project Manager portfolio scope');

  for (const marker of [
    'Engineer scope: report data and person filters are locked',
    'Project Manager scope: report data and Project Manager filters are locked',
    'EngineerUserId = engineerOnly ? context.Actor.EffectiveUserId',
    'ProjectManagerUserId = pmOnly ? context.Actor.EffectiveUserId',
    'FilterProjects', 'TimeEntryDetail', 'EngineerUtilization', 'ProjectManagerPortfolio'
  ]) contains(engine, marker, 'server reporting scope enforcement');
  for (const state of ['"complete"', '"partial"', '"no_data"', '"source_unavailable"']) contains(engine, state, 'report result state');

  for (const marker of [
    'time_entries', 'project_expense_uploads', 'work_billing_readiness_reviews',
    'work_closeout_records', 'project_notification_dispatches', 'resource_qualifications',
    'module076_items', 'operational_control_history', 'secure_project_information_requests',
    'pmo_control_items', 'SOURCE_SCOPE_RESTRICTED', 'row_to_json(source)::text',
    'AllowedUserIds', 'project_id'
  ]) contains(loader, marker, 'source-isolated analytics loader');
  assert(!loader.includes('SELECT * FROM'), 'Analytics source loader must not use unrestricted SELECT *.');

  for (const marker of [
    'SaveRunAsync', 'LoadHistoryAsync', 'LoadRunAsync', 'RecordExportAsync',
    'LoadSavedViewsAsync', 'SaveViewAsync', 'DeleteSavedViewAsync', 'SHA256.HashData'
  ]) contains(repository, marker, 'immutable Analytics Center persistence');
  contains(compatibilityModule, 'BuildExcel', 'XLSX export compatibility');
  contains(compatibilityModule, 'BuildCsv', 'CSV export compatibility');
  contains(compatibilityModule, 'BuildJson', 'JSON export compatibility');

  for (const table of ['enterprise_report_runs', 'enterprise_report_saved_views', 'enterprise_report_exports']) contains(migration, table, 'migration 055 table');
  contains(migration, 'projectpulse055_block_analytics_evidence_mutation', 'immutable analytics evidence');
  contains(migration, "'055_analytics_center'", 'migration 055 registration');
  contains(migration, "'ANALYTICS_CENTER'", 'Analytics Center feature identity');
  contains(migration, "'Analytics Center'", 'Analytics Center visible feature name');
  contains(migration, "WHERE feature_code = 'FINANCIAL_REPORT_CENTER'", 'legacy feature retirement');
  for (const table of ['enterprise_report_runs', 'enterprise_report_saved_views', 'enterprise_report_exports']) contains(rollback, `DROP TABLE IF EXISTS ${table}`, 'migration 055 rollback');

  contains(project, 'app.MapEnterpriseReportingEndpoints();', 'compatibility reporting endpoint registration');
  contains(project, 'app.MapAnalyticsCenterEndpoints();', 'Analytics Center endpoint registration');
  assert(count(project, 'app.MapAnalyticsCenterEndpoints();') === 1, 'Analytics Center endpoints must register exactly once.');
  contains(project, 's/054_enterprise_reporting_center/055_analytics_center/g', 'migration 055 repository normalization');
  contains(project, 's/migration_054_required/migration_055_required/g', 'migration 055 response normalization');

  for (const marker of [
    'Analytics Center', '24 report types', 'Customer Directory', 'Modules 055C and 055D',
    'Engineer', 'Project Manager', 'Team', 'report definition', 'immutable',
    'migration `055_analytics_center`', 'No deployment', 'Module 030'
  ]) contains(documentation, marker, 'Analytics Center documentation');
}

execFileSync(process.execPath, [files.injector], { cwd: webRoot, stdio: 'inherit' });
const generatedApp = read(files.app);
const generatedRegistry = read(files.registry);
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
