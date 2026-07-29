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
  migration: path.join(repositoryRoot, 'database/migrations/050_project_notification_routing_and_schedules.sql'),
  rollback: path.join(repositoryRoot, 'database/rollback/050_project_notification_routing_and_schedules_rollback.sql'),
  module065: path.join(repositoryRoot, 'src/backend/ProjectTime.Api/Modules/Module065ProjectNotificationDelivery.cs'),
  module: path.join(repositoryRoot, 'src/backend/ProjectTime.Api/Modules/ProjectNotificationAutomationModule.cs'),
  service: path.join(repositoryRoot, 'src/backend/ProjectTime.Api/Modules/ProjectNotificationAutomationService.cs'),
  contracts: path.join(repositoryRoot, 'src/backend/ProjectTime.Api/Modules/ProjectNotificationContracts.cs'),
  evaluator: path.join(repositoryRoot, 'src/backend/ProjectTime.Api/Modules/ProjectNotificationEvaluator.cs'),
  snapshot: path.join(repositoryRoot, 'src/backend/ProjectTime.Api/Modules/ProjectNotificationFinancialSnapshotLoader.cs'),
  processing: path.join(repositoryRoot, 'src/backend/ProjectTime.Api/Modules/ProjectNotificationProcessingService.cs'),
  quietHours: path.join(repositoryRoot, 'src/backend/ProjectTime.Api/Modules/ProjectNotificationQuietHoursService.cs'),
  repository: path.join(repositoryRoot, 'src/backend/ProjectTime.Api/Modules/ProjectNotificationRepository.cs'),
  scheduler: path.join(repositoryRoot, 'src/backend/ProjectTime.Api/Modules/ProjectNotificationScheduler.cs'),
  project: path.join(repositoryRoot, 'src/backend/ProjectTime.Api/ProjectTime.Api.csproj'),
  documentation: path.join(repositoryRoot, 'docs/modules/group-4-project-notifications/README.md'),
  component: path.join(sourceRoot, 'ProjectNotificationAutomationCenter.jsx'),
  css: path.join(sourceRoot, 'project-notification-automation-center.css'),
  injector: path.join(scriptDirectory, 'inject-group-4-project-notification-automation.mjs'),
  package: path.join(webRoot, 'package.json'),
  app: path.join(sourceRoot, 'App.jsx'),
  registry: path.join(sourceRoot, 'module-availability-registry.js')
};

let checks = 0;
function read(filePath) {
  if (!fs.existsSync(filePath)) {
    throw new Error(`Required Group 4 file is missing: ${path.relative(repositoryRoot, filePath)}`);
  }
  return fs.readFileSync(filePath, 'utf8');
}
function assert(condition, message) {
  checks += 1;
  if (!condition) throw new Error(message);
}
function contains(source, marker, label) {
  assert(source.includes(marker), `${label} is missing: ${marker}`);
}
function count(source, marker) {
  return source.split(marker).length - 1;
}

const component = read(files.component);
const css = read(files.css);
const injector = read(files.injector);
const packageJson = JSON.parse(read(files.package));

contains(component, "import { usSignalLogoDataUrl } from './assets/usSignalLogoData.js';", 'US Signal branding');
contains(component, 'data-projectpulse-group4="project-notifications"', 'Group 4 UI identity');
for (const marker of [
  'Project Cost Alert Routing',
  'Configurable Notification Schedules',
  'Notification Delivery Monitor',
  'Module 065 is the only mail-delivery authority',
  'Automatically derived recipients',
  'Recent delivery attempts',
  'Quiet hours',
  'Month-end',
  'Escalation',
  'Test only',
  'Production governed'
]) contains(component, marker, 'Group 4 enterprise workspace');
for (const api of [
  '/api/project-notifications/routing-rules',
  '/api/project-notifications/schedules',
  '/api/project-notifications/module-065-readiness',
  '/api/project-notifications/evaluate',
  '/api/project-notifications/dispatches',
  '/api/project-notifications/delivery-monitor',
  '/api/project-notifications/run-due'
]) contains(component, api, 'Group 4 frontend API');
assert(!component.includes('GlobalMailConfigurationCenter'), 'Group 4 UI must not consume retired Module 067.');
assert(!component.includes('CertifyIntegrationCenter'), 'Group 4 UI must not modify Module 038.');

for (const marker of [
  '.group4-notification-center',
  '.group4-rule-grid',
  '.group4-schedule-grid',
  '.group4-table-wrap',
  '.group4-delivery-layout',
  '@media (max-width: 620px)'
]) contains(css, marker, 'Group 4 styling');

for (const marker of [
  'GROUP_4_NOTIFICATION_DELIVERY_MONITOR_ROUTE',
  'GROUP_4_MODULE_022_CONFIGURABLE_RULES',
  'GROUP_4_MODULE_023_CONFIGURABLE_SCHEDULES',
  "moduleNumber: '032'",
  'Notification Delivery Monitor',
  'financialReportAnchor',
  'legacyAnchor'
]) contains(injector, marker, 'Group 4 injector');
assert(!/CertifyIntegrationCenter|certify-integration|Module038/.test(injector), 'Group 4 injector must not target Module 038.');
assert(injector.includes("displayName: 'Reporting'")
  && injector.includes("displayName: 'Financial Report Center'"),
  'Group 4 injector must support both the original and Group 5 Module 030 identities.');

const predev = packageJson.scripts?.predev ?? '';
const prebuild = packageJson.scripts?.prebuild ?? '';
const build = packageJson.scripts?.build ?? '';
contains(predev, 'inject-group-4-project-notification-automation.mjs', 'Group 4 predev installation');
contains(prebuild, 'inject-group-4-project-notification-automation.mjs', 'Group 4 prebuild installation');
contains(build, 'validate:group4-project-notifications', 'Group 4 complete-build validation');
assert(packageJson.scripts?.['validate:group4-project-notifications']
  === 'node ./scripts/validate-group-4-project-notification-automation.mjs',
  'Group 4 package validator must be authoritative.');

if (fullRepositoryContext) {
  const migration = read(files.migration);
  const rollback = read(files.rollback);
  const module065 = read(files.module065);
  const module = read(files.module);
  const service = read(files.service);
  const contracts = read(files.contracts);
  const evaluator = read(files.evaluator);
  const snapshot = read(files.snapshot);
  const processing = read(files.processing);
  const quietHours = read(files.quietHours);
  const repository = read(files.repository);
  const scheduler = read(files.scheduler);
  const project = read(files.project);
  const documentation = read(files.documentation);

  for (const table of [
    'project_cost_alert_routing_rules',
    'project_notification_schedules',
    'project_notification_dispatches',
    'project_notification_dispatch_recipients',
    'project_notification_delivery_attempts',
    'project_notification_configuration_audit'
  ]) contains(migration, table, 'migration 050 table');
  for (const metric of [
    'hours_used_percent',
    'labor_budget_used_percent',
    'expenses_used_percent',
    'forecasted_total_cost',
    'approaching_budget',
    'over_budget',
    'missing_financial_information',
    'failed_project_data_refresh'
  ]) contains(migration, metric, 'Module 022 metric');
  for (const schedule of [
    'weekly_reminder',
    'monday_reminder',
    'month_end_reminder',
    'escalation',
    'timezone_name',
    'days_before_month_end',
    'quiet_hours_start',
    'quiet_hours_end',
    'delivery_boundary'
  ]) contains(migration, schedule, 'Module 023 scheduling contract');
  for (const recipient of [
    'project_manager',
    'assigned_engineers',
    'solution_architect',
    'account_executive',
    'project_team_coordinator',
    'optional_escalation_manager_user_id'
  ]) contains(migration, recipient, 'automatic recipient contract');
  for (const permission of [
    'VIEW_COST_ALERT_ROUTING_RULES',
    'MANAGE_COST_ALERT_ROUTING_RULES',
    'VIEW_NOTIFICATION_SCHEDULES',
    'MANAGE_NOTIFICATION_SCHEDULES',
    'VIEW_NOTIFICATION_DELIVERY_MONITOR',
    'MANAGE_NOTIFICATION_DELIVERY',
    'VIEW_CLOSEOUT_NOTIFICATION_ROUTING',
    'DELIVER_PROJECT_NOTIFICATIONS'
  ]) contains(migration, permission, 'migration 050 permission');
  contains(migration, 'projectpulse050_block_notification_evidence_mutation', 'immutable notification evidence');
  contains(migration, 'ux_project_notification_dispatch_recipients_email', 'case-insensitive recipient uniqueness');
  contains(migration, "'050_project_notification_routing_and_schedules'", 'migration 050 registration');
  assert(!/certify_connection_profiles|certify_expense_import_runs|MANAGE_CERTIFY_CONNECTION/.test(migration),
    'Migration 050 must not alter Certify configuration.');

  for (const table of [
    'project_notification_delivery_attempts',
    'project_notification_dispatch_recipients',
    'project_notification_dispatches',
    'project_notification_schedules',
    'project_cost_alert_routing_rules'
  ]) contains(rollback, `DROP TABLE IF EXISTS ${table}`, 'migration 050 rollback');
  contains(rollback, "'050_project_notification_routing_and_schedules'", 'migration 050 rollback registration');

  const endpoints = [
    '/api/project-notifications/routing-rules',
    '/api/project-notifications/routing-rules/{ruleId:guid}',
    '/api/project-notifications/schedules',
    '/api/project-notifications/schedules/{scheduleId:guid}',
    '/api/project-notifications/module-065-readiness',
    '/api/project-notifications/evaluate',
    '/api/project-notifications/dispatches',
    '/api/project-notifications/delivery-monitor',
    '/api/project-notifications/dispatches/{dispatchId:guid}/release',
    '/api/project-notifications/dispatches/{dispatchId:guid}/retry',
    '/api/project-notifications/run-due',
    '/api/project-notifications/closeout/queue'
  ];
  for (const endpoint of endpoints) contains(module, endpoint, 'Group 4 API');
  contains(module, '/api/project-closeout/email/send', 'Module 041 compatibility route');
  contains(module, 'browser-provided', 'server-authoritative recipient boundary');
  contains(module, 'Module 065', 'Module 065 ownership declaration');

  for (const marker of [
    'GetRoutingRulesAsync',
    'UpdateRoutingRuleAsync',
    'GetSchedulesAsync',
    'UpdateScheduleAsync',
    'EvaluateAsync',
    'ReleaseDispatchAsync',
    'RetryDispatchAsync',
    'QueueCloseoutAsync'
  ]) contains(service, marker, 'Group 4 application service');
  for (const marker of [
    'ProjectCostRoutingRuleUpdateRequest',
    'ProjectNotificationScheduleUpdateRequest',
    'ProjectNotificationEvaluationRequest',
    'ProjectCloseoutNotificationRequest',
    'ProjectNotificationDispatchRow'
  ]) contains(contracts, marker, 'Group 4 contracts');
  for (const marker of [
    'hours_used_percent',
    'labor_budget_used_percent',
    'expenses_used_percent',
    'forecasted_total_cost',
    'missing_financial_information',
    'failed_project_data_refresh'
  ]) contains(evaluator, marker, 'Group 4 evaluator');
  for (const marker of [
    'project_manager_user_id',
    'project_assignments',
    'solution_architect_user_id',
    'account_executive_user_id',
    'project_coordinator_user_id'
  ]) contains(snapshot, marker, 'authoritative recipient source');
  contains(snapshot, 'Module 005', 'current expense source');
  contains(processing, 'RunDueSchedulesAsync', 'due schedule processing');
  contains(processing, 'CreateDispatchAsync', 'durable dispatch processing');
  contains(quietHours, 'IsQuietHours', 'quiet-hours enforcement');
  contains(quietHours, 'EndOfQuietHours', 'quiet-hours deferral');
  contains(repository, 'MigrationReadyAsync', 'migration readiness guard');
  contains(repository, 'TryAcquireSchedulerLockAsync', 'multi-replica scheduler lock');
  contains(repository, 'LoadDispatchesAsync', 'delivery monitor source');
  contains(scheduler, 'ApplicationStarted', 'bounded scheduler startup');
  contains(scheduler, 'TryAcquireSchedulerLockAsync', 'scheduler advisory lock usage');

  for (const marker of [
    'MicrosoftMailRuntimeConfigurationModule.ApplyStoredEnvironmentAsync',
    'production_governed',
    'test_only',
    'module_065',
    'Mail.Send',
    'microsoft_graph',
    'smtp_relay',
    'secretValuesReturned = false'
  ]) contains(module065, marker, 'Module 065 governed delivery');
  assert(!/PROJECTPULSE_GLOBAL_MAIL|module_067|global_mail_configuration/i.test(module065),
    'Group 4 delivery must not read retired Module 067 configuration.');

  contains(project, 'app.UseProjectNotificationCloseoutCompatibility();', 'Module 041 compatibility registration');
  contains(project, 'app.MapProjectNotificationAutomationEndpoints();', 'Group 4 endpoint registration');
  assert(count(project, 'app.UseProjectNotificationCloseoutCompatibility();') === 1,
    'Group 4 closeout compatibility must be registered exactly once.');
  assert(count(project, 'app.MapProjectNotificationAutomationEndpoints();') === 1,
    'Group 4 endpoints must be registered exactly once.');

  for (const marker of [
    'Module 022',
    'Module 023',
    'Module 032',
    'Module 041',
    'Module 065',
    'Migration 050',
    'Module 038 is regression-only',
    'No deployment'
  ]) contains(documentation, marker, 'Group 4 documentation');
} else {
  console.log('GROUP_4_BACKEND_MIGRATION_DOCUMENTATION=SKIPPED_FRONTEND_CONTAINER_CONTEXT');
}

execFileSync(process.execPath, [files.injector], {
  cwd: webRoot,
  stdio: 'inherit'
});

const generatedApp = read(files.app);
const generatedRegistry = read(files.registry);
assert(count(generatedApp, "import ProjectNotificationAutomationCenter from './ProjectNotificationAutomationCenter.jsx';") === 1,
  'Generated App must import Group 4 exactly once.');
assert(count(generatedApp, 'GROUP_4_NOTIFICATION_DELIVERY_MONITOR_ROUTE') === 1,
  'Generated App must contain one Module 032 route.');
assert(count(generatedApp, '<ProjectNotificationAutomationCenter mode="routing-rules" authSession={authSession} />') === 1,
  'Module 022 Group 4 panel must be unique.');
assert(count(generatedApp, '<ProjectNotificationAutomationCenter mode="schedules" authSession={authSession} />') === 1,
  'Module 023 Group 4 panel must be unique.');
assert(count(generatedApp, '<ProjectNotificationAutomationCenter mode="delivery-monitor" authSession={authSession} />') === 1,
  'Module 032 Group 4 route must be unique.');
assert(count(generatedRegistry, "moduleNumber: '032'") === 1,
  'Generated Module 032 registry entry must be unique.');
assert(!generatedApp.includes('GROUP_4_MODULE_038'),
  'Generated App must not include a Group 4 Module 038 mount.');

console.log(`GROUP_4_VALIDATION_CHECKS=${checks}`);
console.log(`GROUP_4_FULL_REPOSITORY_CONTEXT=${fullRepositoryContext ? 'YES' : 'NO'}`);
console.log('GROUP_4_PROJECT_NOTIFICATION_AUTOMATION=PASS');
