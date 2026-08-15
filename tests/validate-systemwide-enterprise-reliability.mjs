import fs from 'node:fs';

const read = (path) => fs.readFileSync(path, 'utf8');
const requireText = (source, marker, label) => {
  if (!source.includes(marker)) throw new Error(`Missing ${label}: ${marker}`);
};
const rejectText = (source, marker, label) => {
  if (source.includes(marker)) throw new Error(`Unexpected ${label}: ${marker}`);
};

const program = read('src/backend/ProjectTime.Api/Program.cs');
const audit = read('src/backend/ProjectTime.Api/Modules/AdminAuditTelemetryMiddleware.cs');
const intake = read('src/backend/ProjectTime.Api/Modules/ProjectIntakeModule.cs');
const reliability = read('src/backend/ProjectTime.Api/Modules/CelarAiProductionPlatformModule.cs');
const currentFacts = read('src/backend/ProjectTime.Api/Ai/CelarAiAuthoritativePublicFactService.cs');
const main = read('src/frontend/project-time-web/src/main.jsx');
const enterprise = read('src/frontend/project-time-web/src/EnterpriseExperienceController.jsx');
const shell = read('src/frontend/project-time-web/src/pulse-shell-frontend-compatibility.js');
const enterpriseCss = read('src/frontend/project-time-web/src/enterprise-systemwide-reliability.css');
const flowhive = read('src/frontend/project-time-web/src/ProjectFlowHiveCenter.jsx');
const costAlerts = read('src/frontend/project-time-web/src/CostOverrunAlertCenter.jsx');
const sow = read('src/frontend/project-time-web/src/enterprise/SalesDeliveryWorkflowCenter.jsx');
const billing = read('src/frontend/project-time-web/src/BillingReadinessCenter.jsx');
const help = read('src/frontend/project-time-web/src/HelpAssistant.jsx');
const auditUi = read('src/frontend/project-time-web/src/AuditHistoryPanel.jsx');
const migration = read('database/migrations/088_systemwide_enterprise_reliability.sql');
const rollback = read('database/rollback/088_systemwide_enterprise_reliability_rollback.sql');
const auditModule = read('src/backend/ProjectTime.Api/Modules/AdminAuditHistoryModule.cs');
const migrationRunner = read('scripts/release-test/run-systemwide-enterprise-reliability-migrations-job.sh');
const deployment = read('.github/workflows/projectpulse-deploy-test.yml');

requireText(program, 'app.UseAdminAuditTelemetry();', 'system-wide audit middleware registration');
requireText(program, 'pr.probability_score', 'Migration 077 risk probability');
requireText(program, 'pr.overall_impact_score', 'Migration 077 impact score');
requireText(program, 'project_management_schema_unavailable', 'project-management readiness response');
rejectText(program, 'pr.probability, pr.impact', 'retired project risk columns');

for (const marker of [
  'No request body, password, token, query string, private AI prompt, document content, or response body retained.',
  'auth_login_events',
  'dependency_unavailable',
  'record_created_or_action_started',
  'session_extended',
  'logout_succeeded'
]) requireText(audit, marker, 'audit telemetry contract');
requireText(audit, '? "failure"', 'normalized central audit failure status');
requireText(auditModule, 'normalized is "true" or "ok"', 'audit status normalization');
requireText(auditModule, 'invalid_credentials', 'failed-login audit normalization');

requireText(intake, 'project_intake_overview_partial', 'partial Project Intake response');
requireText(intake, 'project_intake_source_degraded', 'Project Intake degraded audit');
requireText(intake, 'LoadSourceAsync', 'independent Project Intake source loading');

requireText(currentFacts, 'www.whitehouse.gov/administration/', 'official White House source');
requireText(currentFacts, 'ussignal.com/why-us-signal/leadership/', 'official US Signal leadership source');
requireText(currentFacts, 'authoritative_public_web', 'authoritative public source type');
requireText(currentFacts, 'model memory', 'fail-closed model-memory boundary');
requireText(reliability, 'Provider/model responses excluded from evidence', 'provider exclusion trust reason');
requireText(reliability, 'IsAuthoritativeTrustSource', 'authoritative trust source filter');
requireText(reliability, 'Deterministic evidence strength, not model accuracy or probability of truth.', 'evidence-score meaning');

requireText(main, "import './runtime-browser-compatibility.js';", 'browser UUID compatibility import');
requireText(main, 'ProjectForgeFlowHiveSyncPortal', 'FlowHive/Forge synchronization portal');
requireText(main, "import './enterprise-systemwide-reliability.css';", 'system-wide enterprise CSS');
requireText(enterprise, 'pulse-display-utility-dock', 'display utility dock');
requireText(shell, 'data-pulse-header-theme-switcher', 'theme switcher migration');
requireText(enterpriseCss, "grid-template-areas:", 'responsive header grid');
requireText(enterpriseCss, "color: #ffffff !important", 'active control contrast');
requireText(enterpriseCss, 'overflow-x: auto !important', 'table/header responsive overflow');

requireText(flowhive, 'flowhive-enterprise-readiness-error', 'FlowHive explicit degraded state');
requireText(flowhive, 'Use the Add task action on the Plan, Design, Implement, Validate, or Release phase header.', 'single phase add-task instruction');
rejectText(flowhive, 'Add {phase.name} task', 'duplicate global phase add-task controls');
requireText(flowhive, 'x-projectpulse-correlation-id', 'FlowHive correlation evidence');

for (const marker of [
  'Approved budget',
  'Forecast at completion',
  'Data incomplete',
  'Missing authoritative evidence',
  '/api/project-financials/portfolio?workspace=pm&limit=250'
]) requireText(costAlerts, marker, 'Module 022 financial alert contract');
rejectText(costAlerts, 'completionPercentage) * 100', 'double-scaled project completion');

requireText(sow, '/api/customers/overview', 'SOW Customer Directory');
requireText(sow, '/api/opportunities?scope=all', 'SOW opportunity directory');
requireText(sow, 'Select or type a customer', 'editable customer selector');
requireText(sow, 'Select or type an opportunity', 'editable opportunity selector');

requireText(billing, 'fulfilledSourceWarnings', 'partial Billing Readiness source handling');
requireText(billing, 'supporting source condition(s) require attention', 'specific source status presentation');
requireText(help, 'Evidence score', 'Celar AI evidence score label');
requireText(help, 'authoritative source(s)', 'Celar AI authoritative source label');
rejectText(help, 'Confidence {confidence}', 'misleading trust confidence label');
requireText(auditUi, 'Login, logout & sessions', 'authentication audit quick view');
requireText(auditUi, 'Failures & denied requests', 'failure audit quick view');
requireText(auditUi, 'Dependency outages', 'dependency audit quick view');

requireText(migration, '088_systemwide_enterprise_reliability', 'Migration 088 registration');
requireText(migration, 'account_executive_user_id', 'intake ownership columns');
requireText(migration, 'idx_projectpulse_system_audit_events_correlation', 'audit correlation index');
requireText(rollback, 'deliberately preserves', 'conservative rollback rationale');
rejectText(rollback, 'DROP COLUMN IF EXISTS account_executive_user_id', 'destructive rollback');

for (const marker of [
  'SYSTEMWIDE_RELIABILITY_MIGRATIONS_PRIVATE_NETWORK_JOB=SUCCEEDED',
  'projectpulse-migration"] == "086-088"',
  'main-db-password',
  'PROJECTPULSE_ENVIRONMENT'
]) requireText(migrationRunner, marker, 'protected Test private-network migration runner');
for (const marker of [
  'environment: test',
  'group: projectpulse-deploy-test',
  'queue: max',
  'Migrations 086 and 088',
  'PROJECTPULSE_CELAR_AI_CURRENT_PUBLIC_FACTS_ENABLED=true',
  'Project Management summary',
  'FlowHive enterprise workspace',
  'failed-login UAT',
  'Run protected-Test utilization role-scoping UAT',
  'Who is the current President of the United States?',
  'Who is the CEO of US Signal?',
  'Production mutation: none'
]) requireText(deployment, marker, 'governed protected Test deployment contract');
rejectText(deployment, 'AZURE_PRODUCTION', 'Production deployment path');


const authGetStart = deployment.indexOf('          auth_get() {');
const authPostStart = deployment.indexOf('          auth_post() {', authGetStart);
const requireGetStart = deployment.indexOf('          require_get() {', authPostStart);
const firstRequiredGet = deployment.indexOf("          require_get 'Project Management summary'", requireGetStart);
if (authGetStart < 0 || authPostStart < 0 || requireGetStart < 0 || firstRequiredGet < 0) {
  throw new Error('Protected-Test authenticated GET UAT helpers are incomplete.');
}
const authGet = deployment.slice(authGetStart, authPostStart);
const requireGet = deployment.slice(requireGetStart, firstRequiredGet);
rejectText(authGet, '|| true', 'swallowed authenticated GET transport failure');
for (const marker of [
  '--http1.1',
  '--dump-header "$headers"',
  'curl_exit=$?',
  'printf \'%s|%s\\n\''
]) requireText(authGet, marker, 'authenticated GET transport diagnostics');
for (const marker of [
  'for attempt in 1 2 3',
  'uat-http-diagnostics.ndjson',
  'uat-http-errors.log',
  'body_bytes',
  'content_type'
]) requireText(requireGet, marker, 'authenticated GET retry and evidence contract');
if (fs.existsSync('.github/workflows/systemwide-enterprise-reliability-test-deployment.yml')) {
  throw new Error('Unregistered duplicate system-wide deployment workflow must remain retired.');
}
if (fs.existsSync('.github/workflows/temporary-source-snapshot-20260814.yml')) {
  throw new Error('Temporary source snapshot workflow must be removed before release.');
}

console.log('SYSTEMWIDE_ENTERPRISE_RELIABILITY_SOURCE=PASS governedController=projectpulse-deploy-test');
