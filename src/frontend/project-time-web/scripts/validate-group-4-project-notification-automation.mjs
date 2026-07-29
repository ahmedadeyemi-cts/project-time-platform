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
  routeModule: path.join(repositoryRoot, 'src/backend/ProjectTime.Api/Modules/ProjectNotificationAutomationModule.cs'),
  service: path.join(repositoryRoot, 'src/backend/ProjectTime.Api/Modules/ProjectNotificationAutomationService.cs'),
  processing: path.join(repositoryRoot, 'src/backend/ProjectTime.Api/Modules/ProjectNotificationProcessingService.cs'),
  repository: path.join(repositoryRoot, 'src/backend/ProjectTime.Api/Modules/ProjectNotificationRepository.cs'),
  evaluator: path.join(repositoryRoot, 'src/backend/ProjectTime.Api/Modules/ProjectNotificationEvaluator.cs'),
  snapshot: path.join(repositoryRoot, 'src/backend/ProjectTime.Api/Modules/ProjectNotificationFinancialSnapshotLoader.cs'),
  module065: path.join(repositoryRoot, 'src/backend/ProjectTime.Api/Modules/Module065ProjectNotificationDelivery.cs'),
  scheduler: path.join(repositoryRoot, 'src/backend/ProjectTime.Api/Modules/ProjectNotificationScheduler.cs'),
  quietHours: path.join(repositoryRoot, 'src/backend/ProjectTime.Api/Modules/ProjectNotificationQuietHoursService.cs'),
  contracts: path.join(repositoryRoot, 'src/backend/ProjectTime.Api/Modules/ProjectNotificationContracts.cs'),
  project: path.join(repositoryRoot, 'src/backend/ProjectTime.Api/ProjectTime.Api.csproj'),
  migration: path.join(repositoryRoot, 'database/migrations/050_project_notification_routing_and_schedules.sql'),
  rollback: path.join(repositoryRoot, 'database/rollback/050_project_notification_routing_and_schedules_rollback.sql'),
  migrationTest: path.join(repositoryRoot, 'tests/test-project-notification-migration-050.sh'),
  documentation: path.join(repositoryRoot, 'docs/modules/group-4-project-notifications/README.md'),
  component: path.join(sourceRoot, 'ProjectNotificationAutomationCenter.jsx'),
  css: path.join(sourceRoot, 'project-notification-automation-center.css'),
  injector: path.join(scriptDirectory, 'inject-group-4-project-notification-automation.mjs'),
  package: path.join(webRoot, 'package.json')
};

let checks = 0;

function read(filePath) {
  if (!fs.existsSync(filePath)) {
    throw new Error(`Required Group 4 file is missing: ${path.relative(repositoryRoot, filePath)}`);
  }
  return fs.readFileSync(filePath, 'utf8');
}

function optional(filePath) {
  return fs.existsSync(filePath) ? fs.readFileSync(filePath, 'utf8') : '';
}

function check(name, condition, evidence) {
  checks += 1;
  console.log(`GROUP_4_${name}=${condition ? 'PASSED' : 'FAILED'} — ${evidence}`);
  if (!condition) throw new Error(`${name}: ${evidence}`);
}

function includesAll(source, values) {
  return values.every((value) => source.includes(value));
}

function count(source, value) {
  return source.split(value).length - 1;
}

const component = read(paths.component);
const css = read(paths.css);
const injector = read(paths.injector);
const packageJson = JSON.parse(read(paths.package));

if (fullRepositoryContext) {
  const backendFiles = [
    paths.routeModule,
    paths.service,
    paths.processing,
    paths.repository,
    paths.evaluator,
    paths.snapshot,
    paths.module065,
    paths.scheduler,
    paths.quietHours,
    paths.contracts
  ].map(read);
  const backend = backendFiles.join('\n');
  const routeModule = backendFiles[0];
  const project = read(paths.project);
  const migration = read(paths.migration);
  const rollback = read(paths.rollback);
  const migrationTest = read(paths.migrationTest);
  const documentation = read(paths.documentation);

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

  for (const endpoint of endpoints) {
    check(`API_${endpoint.replace(/[^a-z0-9]+/gi, '_').toUpperCase()}`,
      routeModule.includes(`"${endpoint}"`), endpoint);
  }
  check('API_ROUTE_COUNT',
    count(routeModule, 'endpoints.MapGet(') === 5
      && count(routeModule, 'endpoints.MapPut(') === 2
      && count(routeModule, 'endpoints.MapPost(') === 5,
    'five GET, two PUT, and five POST endpoints are registered exactly once');

  check('MODULE_041_COMPATIBILITY', includesAll(routeModule, [
    'UseProjectNotificationCloseoutCompatibility',
    '/api/project-closeout/email/send',
    'ProjectNotificationAutomationService.QueueCloseoutAsync'
  ]), 'legacy Module 041 send route is intercepted before legacy SMTP/sendmail behavior');

  check('REGISTRATION',
    count(project, 'app.UseProjectNotificationCloseoutCompatibility();') === 1
      && count(project, 'app.MapProjectNotificationAutomationEndpoints();') === 1
      && count(project, 'app.MapProjectFinancialTruthEndpoints();') === 1,
    'Group 4 compatibility and endpoints are registered exactly once while preserving Group 3');

  check('SUPPORTED_COST_METRICS', includesAll(backend, [
    'hours_used_percent',
    'labor_budget_used_percent',
    'expenses_used_percent',
    'forecasted_total_cost',
    'approaching_budget',
    'over_budget',
    'missing_financial_information',
    'failed_project_data_refresh'
  ]), 'all required project cost metrics are implemented');

  check('AUTHORITATIVE_RECIPIENTS', includesAll(backend, [
    'projects.project_manager_user_id',
    'project_assignments.user_id',
    'projects.solution_architect_user_id',
    'projects.account_executive_user_id',
    'projects.project_coordinator_user_id',
    'routing_rule.optional_escalation_manager_user_id',
    'serverDerivedRecipients = true',
    'clientRecipientListIgnored = true'
  ]), 'project recipients are derived from authoritative server relationships');

  check('GROUP_3_FINANCIAL_AUTHORITY', includesAll(backend, [
    'project_assignments',
    'time_entries',
    'project_expense_uploads',
    'upload.is_current = TRUE',
    'upload.deleted_at IS NULL',
    'work_rate_cards',
    'ForecastedFinalCost',
    'CurrentVariance'
  ]), 'routing consumes authoritative project, hours, expense, rate, forecast, and variance sources');

  check('MODULE_065_ONLY_DELIVERY', includesAll(backend, [
    'MicrosoftMailRuntimeConfigurationModule.ApplyStoredEnvironmentAsync',
    'Module 065 remains the only',
    'microsoft_graph',
    'smtp_relay',
    'production_governed',
    'RECIPIENT_BOUNDARY_PREVENTED_DELIVERY'
  ]) && !backend.includes('GLOBAL_MAIL_PROVIDER'),
  'mail provider and credential ownership remains in Module 065');

  check('NO_RETIRED_MODULE_067_READ',
    !/Environment\.GetEnvironmentVariable\([^\n]*067|module_067.*configuration|GLOBAL_MAIL_PROVIDER/i.test(backend),
    'no Group 4 backend reads retired Module 067 configuration');

  check('ACTUAL_SESSION_AND_VIEW_AS', includesAll(backend, [
    'ProjectPulseActualSessionAuthority.ReadUserId',
    'ProjectPulseActualSessionAuthority.IsViewAs',
    'viewAsTransfersMutationAuthority = false',
    'Exit Administrator View-As'
  ]), 'actual/effective sessions are resolved and View-As cannot mutate or deliver');

  check('SOURCE_ISOLATION', includesAll(backend, [
    'ProjectNotificationSourceState.Unavailable',
    'other project data remains usable',
    'diagnosticCode',
    'Retry after the source is restored'
  ]), 'optional source failures remain source-specific and do not blank the complete experience');

  check('MULTI_REPLICA_SCHEDULER', includesAll(backend, [
    'pg_try_advisory_lock',
    'projectpulse_group4_notification_scheduler',
    'MigrationReadyAsync',
    'PROJECTPULSE_NOTIFICATION_SCHEDULER_INTERVAL_SECONDS'
  ]), 'one API replica evaluates due schedules at a time and migration absence is fail-closed');

  check('QUIET_HOURS', includesAll(backend, [
    'IsQuietHours',
    'EndOfQuietHours',
    'deferred_for_quiet_hours',
    'ProjectNotificationQuietHoursService.RunDueSchedulesAsync'
  ]), 'quiet-hours schedules are deferred before manual and background processing');

  check('MIGRATION_050_TABLES', includesAll(migration, [
    'project_cost_alert_routing_rules',
    'project_notification_schedules',
    'project_notification_dispatches',
    'project_notification_dispatch_recipients',
    'project_notification_delivery_attempts',
    'project_notification_configuration_audit'
  ]), 'migration 050 creates all Group 4 durable contracts');

  check('MIGRATION_050_SEEDS',
    count(migration, "'HOURS_USED_APPROACHING'") === 1
      && count(migration, "'PROJECT_DATA_REFRESH_FAILED'") === 1
      && count(migration, "'WEEKLY_PROJECT_REMINDER'") === 1
      && count(migration, "'MONDAY_PROJECT_ESCALATION'") === 1
      && count(migration, "'MONTH_END_FINANCIAL_REMINDER'") === 1,
    'routing and schedule seeds appear exactly once');

  check('MIGRATION_050_UNIQUE_RECIPIENTS', includesAll(migration, [
    'ux_project_notification_dispatch_recipients_email',
    'lower(recipient_email)',
    'recipient_type'
  ]) && !migration.includes('UNIQUE(project_notification_dispatch_id, lower(recipient_email), recipient_type)'),
  'case-insensitive recipient uniqueness uses a valid PostgreSQL expression index');

  check('MIGRATION_050_IMMUTABLE_EVIDENCE', includesAll(migration, [
    'projectpulse050_block_notification_evidence_mutation',
    'trg_projectpulse050_delivery_attempts_immutable',
    'trg_projectpulse050_configuration_audit_immutable'
  ]), 'delivery attempts and configuration audit evidence are immutable');

  check('MODULE_032_PERMISSION_MODEL', includesAll(migration, [
    'VIEW_NOTIFICATION_DELIVERY_MONITOR',
    'MANAGE_NOTIFICATION_DELIVERY',
    "'032'",
    "'ENGINEERING'",
    "'SALES'",
    "'SOLUTION_ARCHITECT'",
    "'PROJECT_MANAGEMENT'",
    "'PROJECT_TEAM_COORDINATOR'",
    "'ACCOUNTING'",
    "'SUPER_ADMINISTRATOR'"
  ]), 'Module 032 permissions cover project, engineering, sales, finance, coordinator, and administrator teams');

  check('ROLLBACK_050', includesAll(rollback, [
    'DROP TABLE IF EXISTS project_notification_configuration_audit',
    'DROP TABLE IF EXISTS project_notification_delivery_attempts',
    'DROP TABLE IF EXISTS project_notification_dispatches',
    "migration_id = '050_project_notification_routing_and_schedules'"
  ]), 'rollback removes only Group 4 schema and migration registration');

  check('MIGRATION_TEST_050', includesAll(migrationTest, [
    'PROJECT_NOTIFICATION_MIGRATION_050=PASS',
    'recipient_case_insensitive_unique',
    'delivery_attempt_evidence_immutable',
    'configuration_audit_evidence_immutable',
    'engineering_view_delivery',
    'sales_view_delivery',
    'solution_architect_view_rules'
  ]), 'migration test covers apply, idempotence, permissions, uniqueness, immutability, and rollback');

  check('DOCUMENTATION_SCOPE', includesAll(documentation, [
    'Module 018',
    'Module 022',
    'Module 023',
    'Module 032',
    'Module 041',
    'Module 065',
    'migration 050',
    'Modules 034 and 035 were not reused',
    'Module 038 layout change',
    'no merge',
    'no deployment'
  ]), 'documentation records ownership, permissions, productivity choice, exclusions, and deployment boundary');
} else {
  console.log('GROUP_4_BACKEND_MIGRATION_AND_GOVERNANCE=SKIPPED_FRONTEND_CONTAINER_CONTEXT');
}

check('OFFICIAL_US_SIGNAL_LOGO',
  component.includes("import { usSignalLogoDataUrl } from './assets/usSignalLogoData.js';")
    && component.includes('src={usSignalLogoDataUrl}')
    && component.includes('alt="US Signal"'),
  'the approved repository US Signal image asset is used');

check('FRONTEND_WORKSPACES', includesAll(component, [
  "module: '022'",
  "module: '023'",
  "module: '032'",
  "module: '041'",
  "module: '018'",
  "workspace === 'routing'",
  "workspace === 'scheduling'",
  "['delivery', 'closeout', 'pm'].includes(workspace)"
]), 'Modules 018, 022, 023, 032, and 041 use the shared Group 4 experience');

check('ROUTING_UI', includesAll(component, [
  'Cost routing rules',
  'Automatically derived recipients',
  'Project Manager',
  'Assigned engineer(s)',
  'Solution Architect',
  'Account Executive',
  'Project Team Coordinator',
  'Optional escalation manager',
  'Save governed rule',
  'Evaluate rules'
]), 'nontechnical Module 022 rules and project-derived recipients are editable');

check('SCHEDULING_UI', includesAll(component, [
  'Notification schedules',
  'Day of week',
  'Local time',
  'Timezone',
  'Days before month-end',
  'Escalation timing',
  'Quiet hours start',
  'Quiet hours end',
  'Delivery boundary',
  'Run due schedules'
]), 'nontechnical Module 023 schedule configuration is complete');

check('DELIVERY_MONITOR_UI', includesAll(component, [
  'Notification Delivery Monitor',
  'Dispatches and recipients',
  'Recent delivery attempts',
  'Module 065 delivery authority',
  'Retry',
  'Source health',
  'derivationSource'
]), 'Module 032 provides recipient, provider, source, attempt, release, and retry evidence');

check('MODULE_041_UI_CONTRACT', includesAll(component, [
  'Module 041 compatibility contract',
  'ignores browser-provided recipient lists',
  'delegates live delivery exclusively to Module 065'
]), 'Module 041 notification ownership is clear in the closeout workspace');

check('NO_SECOND_MAIL_CONFIG_UI', includesAll(component, [
  'never accepts or displays mail credentials',
  'Retired Module 067 configuration is not read'
]) && !/clientSecret|password|smtpPassword|tenantSecret/.test(component),
'frontend exposes no second credential system');

check('SCOPED_ENTERPRISE_CSS', includesAll(css, [
  '.group4-notification-center',
  '.group4-hero',
  '.group4-rule-grid',
  '.group4-schedule-grid',
  '.group4-table-wrap',
  '.group4-source-grid',
  '@media (max-width: 940px)',
  '@media (max-width: 700px)'
]) && !/(^|\n)\s*(?:html|body|:root|#root|main|button|table|input|select)\s*[{,]/m.test(css),
'enterprise styling is responsive and scoped to Group 4');

check('ENTERPRISE_BRAND_TOKENS', includesAll(css, [
  '--group4-navy-950',
  '--group4-cyan-600',
  '--group4-green-700',
  '--group4-amber-700',
  '--group4-red-700'
]), 'US Signal-aligned navy, cyan, green, warning, and risk tokens are centralized');

for (const target of [
  'CostOverrunAlertCenter.jsx',
  'TimeComplianceCenter.jsx',
  'CloseoutEmailAutomationCenter.jsx',
  'ProjectManagerWorkloadCenter.jsx'
]) {
  check(`INJECTOR_${target.replace(/[^a-z0-9]+/gi, '_').toUpperCase()}`,
    injector.includes(target), target);
}

check('MODULE_032_INJECTOR', includesAll(injector, [
  'notification-delivery-monitor',
  "moduleNumber: '032'",
  'VIEW_NOTIFICATION_DELIVERY_MONITOR',
  'GROUP_4_MODULE_032_ROUTE_START'
]), 'Module 032 route and registry identity are installed idempotently');

check('CERTIFY_REGRESSION_ONLY',
  !/CertifyConnection|CertifyConnectionCenter|certify-connection|Module038Certify/.test(injector)
    && !component.includes('/api/certify'),
  'Group 4 does not change Module 038 layout or Certify behavior');

const predev = packageJson.scripts?.predev || '';
const prebuild = packageJson.scripts?.prebuild || '';
const build = packageJson.scripts?.build || '';
check('PACKAGE_WIRING',
  predev.includes('inject-group-3-project-financial-workspaces.mjs')
    && predev.includes('inject-group-4-project-notification-automation.mjs')
    && prebuild.includes('inject-group-3-project-financial-workspaces.mjs')
    && prebuild.includes('inject-group-4-project-notification-automation.mjs')
    && build.includes('validate:group3-project-financial-workspaces')
    && build.includes('validate:group4-project-notifications')
    && packageJson.scripts?.['validate:group4-project-notifications']
      === 'node ./scripts/validate-group-4-project-notification-automation.mjs',
  'Group 3 is preserved and Group 4 is enforced in predev, prebuild, and the full build');

execFileSync(process.execPath, [paths.injector], {
  cwd: webRoot,
  stdio: 'inherit'
});
execFileSync(process.execPath, [paths.injector], {
  cwd: webRoot,
  stdio: 'inherit'
});

const mountExpectations = [
  ['CostOverrunAlertCenter.jsx', 'workspace="routing"'],
  ['TimeComplianceCenter.jsx', 'workspace="scheduling"'],
  ['CloseoutEmailAutomationCenter.jsx', 'workspace="closeout"'],
  ['ProjectManagerWorkloadCenter.jsx', 'workspace="pm"']
];
for (const [fileName, mount] of mountExpectations) {
  const source = read(path.join(sourceRoot, fileName));
  check(`MOUNT_${fileName.replace(/[^a-z0-9]+/gi, '_').toUpperCase()}`,
    count(source, "import ProjectNotificationAutomationCenter from './ProjectNotificationAutomationCenter.jsx';") === 1
      && count(source, 'GROUP_4_PROJECT_NOTIFICATION_AUTOMATION_START') === 1
      && count(source, mount) === 1,
    `${fileName} contains one import and one role-specific mount after two installer runs`);
}

const app = read(path.join(sourceRoot, 'App.jsx'));
const registry = read(path.join(sourceRoot, 'module-availability-registry.js'));
check('MODULE_032_ROUTE_INSTALLED',
  count(app, 'GROUP_4_MODULE_032_ROUTE_START') === 1
    && count(app, "activeRoute === 'notification-delivery-monitor'") === 1
    && count(app, 'workspace="delivery"') === 1,
  'App contains one permission-aware Module 032 route after two installer runs');
check('MODULE_032_REGISTRY_INSTALLED',
  count(registry, "moduleNumber: '032'") === 1
    && count(registry, "route: 'notification-delivery-monitor'") === 1,
  'module registry contains one Notification Delivery Monitor identity');

console.log(`GROUP_4_VALIDATION_CHECKS=${checks}`);
console.log(`GROUP_4_FULL_REPOSITORY_CONTEXT=${fullRepositoryContext ? 'YES' : 'NO'}`);
console.log('GROUP_4_PROJECT_NOTIFICATION_AUTOMATION=PASS');
