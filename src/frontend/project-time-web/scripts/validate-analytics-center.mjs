import fs from 'node:fs';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const webRoot = path.resolve(here, '..');
const repoRoot = path.resolve(webRoot, '../../..');
const srcRoot = path.join(webRoot, 'src');
const fullRepo = fs.existsSync(path.join(repoRoot, '.git'))
  || fs.existsSync(path.join(repoRoot, '.github/workflows/projectpulse-ci.yml'));

const file = (...parts) => path.join(...parts);
const read = (target) => {
  if (!fs.existsSync(target)) throw new Error(`Analytics Center file is missing: ${path.relative(repoRoot, target)}`);
  return fs.readFileSync(target, 'utf8');
};
let checks = 0;
const assert = (condition, message) => { checks += 1; if (!condition) throw new Error(message); };
const has = (source, marker, label) => assert(source.includes(marker), `${label} is missing: ${marker}`);
const count = (source, marker) => source.split(marker).length - 1;

const component = read(file(srcRoot, 'AnalyticsCenter.jsx'));
const css = read(file(srcRoot, 'analytics-center.css'));
const injector = read(file(here, 'inject-analytics-center.mjs'));
const pkg = JSON.parse(read(file(webRoot, 'package.json')));

for (const marker of [
  'Analytics Center', 'Select report', 'Set criteria', 'All customers', 'All projects',
  'All engineers', 'All Project Managers', 'All teams', 'Refresh filter lists',
  'Preview report', 'Run & save', 'Actual analytics results', 'Analytics run history',
  '/api/analytics/catalog', '/api/analytics/filter-options', '/api/analytics/${persisted',
  '/api/analytics/history', '/api/analytics/runs/${runId}/export',
  "['xlsx', 'csv', 'json']", 'Engineer — self only', 'PM — own portfolio'
]) has(component, marker, 'Analytics Center interface');

for (const forbidden of [
  'Fiscal Period', '030Q Reporting Readiness Closeout', 'Build Export Layout',
  'Save Report Definition Preview', 'selectedEngineerSummaryText',
  '/api/reports/030/filter-options', 'Validate 030 Readiness',
  'Reporting, Accounting, Invoicing, Analytics Command Center'
]) assert(!component.includes(forbidden), `Legacy Module 030 marker remains: ${forbidden}`);

for (const marker of [
  '.analytics-center', '.analytics-build-layout', '.analytics-report-cards',
  '.analytics-filter-grid', '.analytics-source-grid', '.analytics-history-list', '@media print'
]) has(css, marker, 'Analytics Center styling');

for (const marker of [
  "import AnalyticsCenter from './AnalyticsCenter.jsx';",
  '<AnalyticsCenter authSession={authSession} />',
  "displayName: 'Analytics Center'",
  "replaceAll('Financial Report Center', 'Analytics Center')",
  "replaceAll('Enterprise Reporting Center', 'Analytics Center')"
]) has(injector, marker, 'Analytics generated integration');

has(pkg.scripts?.predev ?? '', 'inject-analytics-center.mjs', 'predev Analytics installer');
has(pkg.scripts?.prebuild ?? '', 'inject-analytics-center.mjs', 'prebuild Analytics installer');
has(pkg.scripts?.build ?? '', 'validate:analytics-center', 'full-build Analytics validation');
assert(pkg.scripts?.['validate:analytics-center'] === 'node ./scripts/validate-analytics-center.mjs', 'Analytics package validator is not authoritative.');

if (fullRepo) {
  const contracts = read(file(repoRoot, 'src/backend/ProjectTime.Api/Modules/AnalyticsCenterContracts.cs'));
  const directory = read(file(repoRoot, 'src/backend/ProjectTime.Api/Modules/AnalyticsCenterDirectoryLoader.cs'));
  const module = read(file(repoRoot, 'src/backend/ProjectTime.Api/Modules/AnalyticsCenterModule.cs'));
  const catalog = read(file(repoRoot, 'src/backend/ProjectTime.Api/Modules/EnterpriseReportingCatalog.cs'));
  const engine = read(file(repoRoot, 'src/backend/ProjectTime.Api/Modules/EnterpriseReportingEngine.cs'));
  const loader = read(file(repoRoot, 'src/backend/ProjectTime.Api/Modules/EnterpriseReportingSourceLoader.cs'));
  const repository = read(file(repoRoot, 'src/backend/ProjectTime.Api/Modules/EnterpriseReportingRepository.cs'));
  const compatibility = read(file(repoRoot, 'src/backend/ProjectTime.Api/Modules/EnterpriseReportingModule.cs'));
  const migration = read(file(repoRoot, 'database/migrations/055_analytics_center.sql'));
  const rollback = read(file(repoRoot, 'database/rollback/055_analytics_center_rollback.sql'));
  const project = read(file(repoRoot, 'src/backend/ProjectTime.Api/ProjectTime.Api.csproj'));
  const docs = read(file(repoRoot, 'docs/modules/module-030-analytics-center/README.md'));

  for (const marker of ['AnalyticsReportRequest', 'CustomerId', 'ProjectId', 'ProjectManagerUserId', 'EngineerUserId', 'TeamId', 'DateFrom', 'DateTo']) has(contracts, marker, 'Analytics request contract');
  for (const marker of ['FROM clients client', 'FROM teams team', 'JOIN team_memberships', '@broad OR client.client_id = ANY(@client_ids)', '@broad OR membership.user_id = ANY(@visible_user_ids)']) has(directory, marker, 'role-scoped directory loader');

  const reports = [
    'project_portfolio', 'project_financial_health', 'project_budget_forecast',
    'project_hours_consumption', 'time_entry_detail', 'engineer_workload',
    'engineer_utilization', 'project_manager_portfolio', 'project_team_assignments',
    'customer_project_summary', 'expense_detail', 'sell_delivery_context',
    'billing_readiness', 'project_closeout_readiness', 'notification_delivery',
    'qualification_expiration', 'oncall_coverage', 'issue_feature_lifecycle',
    'release_deployment_readiness', 'service_health_slo', 'data_governance_retention',
    'customer_delivery_acceptance', 'secure_project_information', 'pmo_project_controls'
  ];
  for (const report of reports) has(catalog, `"${report}"`, 'Analytics report catalog');
  for (const marker of ['Customer()', 'Project()', 'ProjectManager()', 'Engineer()', 'DateFrom()', 'DateTo()']) has(catalog, marker, 'report-specific filters');

  for (const marker of [
    '/api/analytics/catalog', '/api/analytics/filter-options', '/api/analytics/preview',
    '/api/analytics/run', '/api/analytics/history', '/api/analytics/runs/{runId:guid}/export',
    'moduleName = "Analytics Center"', 'fiscalPeriodFilterPresent = false',
    'organizationFilterPresent = false', 'CustomerOptions', 'ProjectOptions',
    'ProjectManagerOptions', 'EngineerOptions', 'TeamOptions', 'CanonicalContractTypes',
    'Fixed Price', 'Time and Material', 'Pre-Sales', 'Non-billable',
    'contractTypesAlignedToModules055C055D = true', 'engineerReportsLockedToSelf',
    'projectManagerReportsLockedToOwnPortfolio'
  ]) has(module, marker, 'Analytics API and criteria behavior');

  for (const marker of [
    'Engineer scope: report data and person filters are locked',
    'Project Manager scope: report data and Project Manager filters are locked',
    'EngineerUserId = engineerOnly ? context.Actor.EffectiveUserId',
    'ProjectManagerUserId = pmOnly ? context.Actor.EffectiveUserId',
    'TimeEntryDetail', 'EngineerUtilization', 'ProjectManagerPortfolio'
  ]) has(engine, marker, 'server scope enforcement');

  for (const marker of [
    'time_entries', 'project_expense_uploads', 'work_billing_readiness_reviews',
    'work_closeout_records', 'project_notification_dispatches', 'resource_qualifications',
    'module076_items', 'operational_control_history', 'secure_project_information_requests',
    'pmo_control_items', 'SOURCE_SCOPE_RESTRICTED', 'row_to_json(source)::text'
  ]) has(loader, marker, 'source-isolated loader');
  assert(!loader.includes('SELECT * FROM'), 'Analytics source loader uses unrestricted SELECT *.');

  for (const marker of ['SaveRunAsync', 'LoadHistoryAsync', 'LoadRunAsync', 'RecordExportAsync', 'LoadSavedViewsAsync', 'SaveViewAsync', 'DeleteSavedViewAsync', 'SHA256.HashData']) has(repository, marker, 'Analytics persistence');
  for (const marker of ['BuildExcel', 'BuildCsv', 'BuildJson']) has(compatibility, marker, 'Analytics export compatibility');

  for (const table of ['enterprise_report_runs', 'enterprise_report_saved_views', 'enterprise_report_exports']) has(migration, table, 'migration 055 table');
  has(migration, 'projectpulse055_block_analytics_evidence_mutation', 'immutable Analytics evidence');
  has(migration, "'055_analytics_center'", 'migration 055 registration');
  has(migration, "'ANALYTICS_CENTER'", 'Analytics feature identity');
  for (const table of ['enterprise_report_runs', 'enterprise_report_saved_views', 'enterprise_report_exports']) has(rollback, `DROP TABLE IF EXISTS ${table}`, 'migration 055 rollback');

  has(project, 'app.MapEnterpriseReportingEndpoints();', 'compatibility API registration');
  has(project, 'app.MapAnalyticsCenterEndpoints();', 'Analytics API registration');
  assert(count(project, 'app.MapAnalyticsCenterEndpoints();') === 1, 'Analytics endpoints must register once.');
  has(project, 's/054_enterprise_reporting_center/055_analytics_center/g', 'migration 055 repository normalization');
  has(project, 's/migration_054_required/migration_055_required/g', 'migration 055 response normalization');

  for (const marker of ['Analytics Center', '24 report types', 'Customer Directory', 'Modules 055C and 055D', 'Engineer', 'Project Manager', 'Team', 'immutable', 'Migration `055_analytics_center`', 'No deployment']) has(docs, marker, 'Analytics documentation');
}

execFileSync(process.execPath, [file(here, 'inject-analytics-center.mjs')], { cwd: webRoot, stdio: 'inherit' });
const generatedApp = read(file(srcRoot, 'App.jsx'));
const generatedRegistry = read(file(srcRoot, 'module-availability-registry.js'));
assert(count(generatedApp, "import AnalyticsCenter from './AnalyticsCenter.jsx';") === 1, 'Generated App must import Analytics Center once.');
assert(count(generatedApp, '<AnalyticsCenter authSession={authSession} />') === 1, 'Generated App must mount Analytics Center once.');
assert(!generatedApp.includes('<EnterpriseReportingCenter authSession={authSession} />'), 'Former Enterprise Reporting mount remains.');
assert(!generatedApp.includes('<FinancialOperationsRecoveryWorkspace mode="reporting" authSession={authSession} />'), 'Legacy Financial Report Center mount remains.');
assert(!generatedApp.includes('selectedEngineerSummaryText'), 'Generated App retains the legacy Engineer-render exception.');
assert(count(generatedRegistry, "moduleNumber: '030'") === 1, 'Module 030 registry entry is not unique.');
has(generatedRegistry, "displayName: 'Analytics Center'", 'generated Module 030 identity');

console.log(`ANALYTICS_CENTER_VALIDATION_CHECKS=${checks}`);
console.log(`ANALYTICS_CENTER_FULL_REPOSITORY_CONTEXT=${fullRepo ? 'YES' : 'NO'}`);
console.log('ANALYTICS_CENTER_REPORT_COUNT=24');
console.log('MODULE_030_ANALYTICS_CENTER=PASS');
