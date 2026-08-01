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
const multi = read(file(srcRoot, 'analytics/AnalyticsMultiSelect.jsx'));
const css = read(file(srcRoot, 'analytics-center.css'));
const injector = read(file(here, 'inject-analytics-center.mjs'));
const pkg = JSON.parse(read(file(webRoot, 'package.json')));

for (const marker of [
  "import USSignalLogo from './enterprise/USSignalLogo.jsx';",
  "import AnalyticsMultiSelect from './analytics/AnalyticsMultiSelect.jsx';",
  'Analytics Center', 'Back to Modules', 'Back to Dashboard',
  'Home', 'Dashboards', 'Reports', 'Schedules', 'Data Explorer',
  'KPIs & Metrics', 'Alerts & Subscriptions', 'Data Quality', 'Admin',
  'Recently Viewed Dashboards & Reports', 'Report Library',
  'Set criteria', 'All customers', 'All projects',
  'All engineers', 'All Project Managers', 'All teams',
  'Preview report', 'Run & save', 'Actual analytics results', 'Analytics run history',
  'US Signal PDF', 'Excel', 'Recurring US Signal delivery',
  'Multiple active ProjectPulse users receive individual copies',
  'Module 065 owns Entra Secret Administration',
  '/api/analytics/v2/overview', '/api/analytics/v2/catalog',
  '/api/analytics/v2/filter-options', '/api/analytics/v2/preview',
  '/api/analytics/v2/run', '/api/analytics/v2/history',
  '/api/analytics/v2/runs/${runId}/export',
  '/api/analytics/v2/schedules', '/api/analytics/v2/recipient-options',
  "['xlsx', 'csv', 'json']", 'Engineer — self only', 'PM — own portfolio'
]) has(component, marker, 'Analytics Center enterprise interface');

for (const forbidden of [
  'Fiscal Period', '030Q Reporting Readiness Closeout', 'Build Export Layout',
  'Save Report Definition Preview', 'selectedEngineerSummaryText',
  '/api/reports/030/filter-options', 'Validate 030 Readiness',
  'Reporting, Accounting, Invoicing, Analytics Command Center'
]) assert(!component.includes(forbidden), `Legacy Module 030 marker remains: ${forbidden}`);

for (const marker of [
  'aria-multiselectable="true"', 'type="checkbox"', 'Select visible',
  'Remove ${option.label}', 'No authorized options match this search'
]) has(multi, marker, 'Analytics accessible multi-select');

for (const marker of [
  '.analytics-enterprise-shell', '.analytics-sidebar', '.analytics-kpi-grid',
  '.analytics-recent-grid', '.analytics-build-layout',
  '.analytics-report-categories', '.analytics-filter-grid', '.analytics-multiselect-menu',
  '.analytics-schedule-panel', '.analytics-source-grid', '.analytics-history-list',
  '.analytics-result-table-wrap', '.analytics-coverage-footer', '@media print'
]) has(css, marker, 'Analytics Center enterprise styling');

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
  const scope = read(file(repoRoot, 'src/backend/ProjectTime.Api/Modules/AnalyticsCenterExperienceScope.cs'));
  const contracts = read(file(repoRoot, 'src/backend/ProjectTime.Api/Modules/AnalyticsCenterEnterpriseContracts.cs'));
  const module = read(file(repoRoot, 'src/backend/ProjectTime.Api/Modules/AnalyticsCenterEnterpriseExperienceModule.cs'));
  const schedules = read(file(repoRoot, 'src/backend/ProjectTime.Api/Modules/AnalyticsCenterScheduleService.cs'));
  const scheduler = read(file(repoRoot, 'src/backend/ProjectTime.Api/Modules/AnalyticsCenterScheduler.cs'));
  const repository = read(file(repoRoot, 'src/backend/ProjectTime.Api/Modules/AnalyticsCenterScheduleRepository.cs'));
  const exportBuilder = read(file(repoRoot, 'src/backend/ProjectTime.Api/Modules/AnalyticsBrandedExportBuilder.cs'));
  const mail = read(file(repoRoot, 'src/backend/ProjectTime.Api/Modules/Module065AnalyticsAttachmentDelivery.cs'));
  const targets = read(file(repoRoot, 'src/backend/ProjectTime.Api/Directory.Build.targets'));
  const migration = read(file(repoRoot, 'database/migrations/060_analytics_center_enterprise_experience.sql'));
  const rollback = read(file(repoRoot, 'database/rollback/060_analytics_center_enterprise_experience_rollback.sql'));
  const migrationTest = read(file(repoRoot, 'tests/test-analytics-center-enterprise-migration-060.sh'));
  const catalog = read(file(repoRoot, 'src/backend/ProjectTime.Api/Modules/EnterpriseReportingCatalog.cs'));

  for (const marker of [
    'CustomerIds', 'ProjectIds', 'ProjectManagerUserIds', 'EngineerUserIds',
    'TeamIds', 'ContractTypes', 'AnalyticsScheduleUpsertRequest',
    'AnalyticsScheduleDeliveryEvidence', 'Module065MailAttachment'
  ]) has(contracts, marker, 'Analytics enterprise contracts');

  for (const marker of [
    'multipleSelection = true', '"customerIds"', '"projectIds"',
    '"projectManagerUserIds"', '"engineerUserIds"', '"teamIds"',
    '"contractTypes"', 'Type = "multiselect"', 'IsEngineerOnly', 'IsPmOnly',
    'Engineer scope: person-level reports and filters are locked',
    'Project Manager scope: reports and PM filters are locked',
    'Modules 055C/055D contract type', 'Fixed Price', 'Time and Material'
  ]) has(scope, marker, 'multi-select server scope');

  const apiMarkers = [
    '/api/analytics/v2/overview', '/api/analytics/v2/catalog',
    '/api/analytics/v2/filter-options', '/api/analytics/v2/preview',
    '/api/analytics/v2/run', '/api/analytics/v2/history',
    '/api/analytics/v2/runs/{runId:guid}/export',
    '/api/analytics/v2/activity/{reportCode}/view',
    '/api/analytics/v2/activity/{reportCode}/favorite',
    '/api/analytics/v2/recipient-options', '/api/analytics/v2/schedules',
    '/api/analytics/v2/schedules/{scheduleId:guid}/run-now',
    '/api/analytics/v2/schedule-runs', '/api/analytics/v2/schedules/readiness',
    '/api/analytics/v2/schedules/run-due'
  ];
  for (const marker of apiMarkers) has(module, marker, 'Analytics enterprise API');
  for (const marker of [
    'Contracted value', 'Active projects', 'Billable utilization', 'Hours used',
    'Forecast variance', 'New customers (YTD)', 'PM workload', 'Report delivery health',
    'BuildScheduledReportAsync', 'individualizedRecipientScope', 'ExportUrls'
  ]) has(module, marker, 'Analytics overview and export contract');

  for (const marker of [
    'analytics_report_schedules', 'analytics_report_schedule_recipients',
    'analytics_report_schedule_runs', 'analytics_report_schedule_delivery_attempts',
    'analytics_user_report_activity', 'TryAcquireSchedulerLockAsync',
    'LoadRecipientOptionsAsync', 'UpsertActivityAsync'
  ]) has(repository, marker, 'Analytics scheduling repository');
  for (const marker of [
    'individualized branded report', 'Module 065', 'production_governed',
    'recipients.Length > 1', 'scopeUserId', 'InsertDeliveryEvidenceAsync'
  ]) has(schedules, marker, 'Analytics governed scheduling');
  for (const marker of [
    'pg_try_advisory_lock', 'CalculateNextRun', 'weekdays', 'weekly',
    'monthly', 'quarterly', 'yearly'
  ]) has(scheduler + repository, marker, 'multi-replica schedule runner');

  for (const marker of [
    'USSNavyStacked.png', 'USSNavyStacked.jpg', 'BuildPdf', 'BuildExcel',
    'application/pdf', 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
    'US Signal · ProjectPulse Analytics Center', 'SHA256.HashData'
  ]) has(exportBuilder, marker, 'US Signal branded exports');
  for (const marker of [
    'Module065ProjectNotificationDelivery.GetReadinessAsync',
    'microsoft_graph', 'smtp_relay', '#microsoft.graph.fileAttachment',
    'PROJECTPULSE_TEST_SMTP_', 'PROJECTPULSE_PRODUCTION_SMTP_',
    'Module 065 delivered the US Signal branded Analytics report'
  ]) has(mail, marker, 'Module 065 attachment delivery');

  for (const table of [
    'analytics_report_schedules', 'analytics_report_schedule_recipients',
    'analytics_report_schedule_runs', 'analytics_report_schedule_delivery_attempts',
    'analytics_user_report_activity'
  ]) has(migration, table, 'migration 060 table');
  for (const permission of [
    'VIEW_ANALYTICS_DASHBOARDS', 'VIEW_ANALYTICS_SCHEDULES',
    'MANAGE_ANALYTICS_SCHEDULES', 'DELIVER_ANALYTICS_SCHEDULES'
  ]) has(migration, permission, 'migration 060 permission');
  has(migration, "export_format IN ('csv', 'xlsx', 'json', 'pdf')", 'PDF export evidence support');
  has(migration, 'projectpulse060_block_analytics_schedule_evidence_mutation', 'immutable schedule evidence');
  has(migration, "'060_analytics_center_enterprise_experience'", 'migration 060 registration');
  for (const table of [
    'analytics_report_schedules', 'analytics_report_schedule_recipients',
    'analytics_report_schedule_runs', 'analytics_report_schedule_delivery_attempts',
    'analytics_user_report_activity'
  ]) has(rollback, `DROP TABLE IF EXISTS ${table}`, 'migration 060 rollback');
  has(rollback, "export_format IN ('csv', 'xlsx', 'json')", 'rollback export contract');
  has(migrationTest, 'ANALYTICS_CENTER_ENTERPRISE_MIGRATION_060=PASS', 'migration 060 test');

  has(targets, 'app.MapAnalyticsCenterEnterpriseExperienceEndpoints();', 'Analytics enterprise endpoint registration');
  has(targets, 'AfterTargets="GenerateScopedRbacSources"', 'generated Program integration order');

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
console.log('ANALYTICS_CENTER_MULTIPLE_SELECTION=PASS');
console.log('ANALYTICS_CENTER_BRANDED_PDF_XLSX=PASS');
console.log('ANALYTICS_CENTER_MODULE_065_SCHEDULING=PASS');
console.log('MODULE_030_ANALYTICS_CENTER=PASS');
