import fs from 'node:fs';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const absolute = (value) => path.join(root, value);
const exists = (value) => fs.existsSync(absolute(value));
const read = (value) => fs.readFileSync(absolute(value), 'utf8');
const count = (source, marker) => source.split(marker).length - 1;
let failures = 0;
let checks = 0;

function requireValue(condition, code, detail) {
  checks++;
  if (condition) {
    console.log(`${code}=PASSED — ${detail}`);
    return true;
  }
  failures++;
  console.error(`${code}=FAILED — ${detail}`);
  return false;
}

function requireFile(file, code) {
  return requireValue(exists(file), code, file);
}

function requireMarker(source, marker, code, detail = marker) {
  return requireValue(source.includes(marker), code, detail);
}

function requireNoMarker(source, marker, code, detail = marker) {
  return requireValue(!source.includes(marker), code, detail);
}

const files = Object.freeze({
  migration: 'database/migrations/084_module_076_celar_ai_defect_operations.sql',
  rollback: 'database/rollback/084_module_076_celar_ai_defect_operations_rollback.sql',
  contracts: 'src/backend/ProjectTime.Api/Ai/CelarAiOperationsContracts.cs',
  service: 'src/backend/ProjectTime.Api/Ai/CelarAiDefectOrchestrationService.cs',
  monitor: 'src/backend/ProjectTime.Api/Ai/CelarAiAvailabilityMonitorService.cs',
  module: 'src/backend/ProjectTime.Api/Modules/CelarAiOperationsModule.cs',
  generator: 'src/backend/ProjectTime.Api/build/generate-celar-ai-universal-answer-reliability.awk',
  frontend: 'src/frontend/project-time-web/src/CelarAiAskOperations.jsx',
  frontendCss: 'src/frontend/project-time-web/src/celar-ai-ask-operations.css',
  injector: 'src/frontend/project-time-web/scripts/inject-celar-ai-ask-operations.mjs',
  injectorChain: 'src/frontend/project-time-web/scripts/inject-celar-ai-universal-answer-reliability.mjs',
  corpus: 'tests/celar-ai-operations-evaluation-cases.json',
  policyProject: 'tests/CelarAiOperationsPolicyTests/CelarAiOperationsPolicyTests.csproj',
  policyTests: 'tests/CelarAiOperationsPolicyTests/Program.cs',
  migrationTest: 'tests/test-module-076-celar-ai-defect-operations-migration-084.sh'
});

for (const [name, file] of Object.entries(files)) requireFile(file, `CELAR_AIOPS_FILE_${name.toUpperCase()}`);
if (failures) process.exit(1);

const migration = read(files.migration);
const rollback = read(files.rollback);
const contracts = read(files.contracts);
const service = read(files.service);
const monitor = read(files.monitor);
const moduleSource = read(files.module);
const generator = read(files.generator);
const frontend = read(files.frontend);
const frontendCss = read(files.frontendCss);
const injector = read(files.injector);
const injectorChain = read(files.injectorChain);
const policyTests = read(files.policyTests);
const migrationTest = read(files.migrationTest);
const corpus = JSON.parse(read(files.corpus));

requireValue(corpus.contractVersion === 'celar-ai-operations-evaluation-v1-20260810', 'CELAR_AIOPS_CORPUS_VERSION', corpus.contractVersion);
requireValue(corpus.caseCount === 60 && corpus.cases.length === 60, 'CELAR_AIOPS_CORPUS_COUNT', '60 cases');
requireValue(corpus.categoryCount === 6, 'CELAR_AIOPS_CORPUS_CATEGORY_COUNT', '6 categories');
const categoryCounts = new Map();
for (const item of corpus.cases) categoryCounts.set(item.category, (categoryCounts.get(item.category) || 0) + 1);
requireValue(categoryCounts.size === 6 && [...categoryCounts.values()].every((value) => value === 10), 'CELAR_AIOPS_CORPUS_BALANCE', '10 cases per category');
requireValue(corpus.cases.every((item, index) => item.id === `AIOPS-${String(index + 1).padStart(3, '0')}`), 'CELAR_AIOPS_CORPUS_ID_SEQUENCE', 'AIOPS-001 through AIOPS-060');
requireValue(corpus.cases.every((item) => item.askCelarAiPrimarySurface === true), 'CELAR_AIOPS_CORPUS_PRIMARY_SURFACE', 'Ask Celar AI is primary in all cases');
requireValue(corpus.cases.every((item) => item.module076SystemOfRecord === true), 'CELAR_AIOPS_CORPUS_SYSTEM_OF_RECORD', 'Module 076 is durable in all cases');
const requiredForbidden = ['view_as_mutation', 'ai_as_requesting_authority', 'secret_or_cookie_storage', 'raw_private_document_storage', 'embedding_vector_storage', 'unrestricted_generated_sql', 'duplicate_automatic_defect', 'production_automatic_activation'];
requireValue(corpus.cases.every((item) => requiredForbidden.every((value) => item.forbiddenOutcomes.includes(value))), 'CELAR_AIOPS_CORPUS_FORBIDDEN_OUTCOMES', 'all privacy, authority, duplication, and Production boundaries');
requireValue(corpus.cases.filter((item) => item.requiredAssigneeEmail).every((item) => item.requiredAssigneeEmail === 'ahmed.adeyemi@ussignal.com'), 'CELAR_AIOPS_CORPUS_DEFAULT_ASSIGNEE', 'Ahmed email exact');
for (const [key, value] of Object.entries({
  askCelarAiPrimarySurfaceRate: 1,
  defaultAssigneeCorrectness: 1,
  duplicateAutomaticDefects: 0,
  lostThresholdCrossingIncidents: 0,
  unauthorizedDefectDisclosure: 0,
  secretOrPrivateContentDisclosure: 0,
  viewAsMutationSuccess: 0,
  incorrectAutomaticClosure: 0,
  recoveryEvidenceCorrectness: 1
})) requireValue(corpus.requiredPromotionThresholds[key] === value, `CELAR_AIOPS_THRESHOLD_${key.toUpperCase()}`, String(value));

for (const table of [
  'module076_defects',
  'module076_defect_comments',
  'module076_defect_events',
  'module076_defect_evidence',
  'module076_intake_sessions',
  'module076_incident_occurrences',
  'module076_monitor_policies',
  'module076_probe_results',
  'module076_monitor_suppressions',
  'module076_notification_outbox'
]) requireMarker(migration, `CREATE TABLE IF NOT EXISTS ${table}`, `CELAR_AIOPS_MIGRATION_${table.toUpperCase()}`);
requireMarker(migration, "'084_module_076_celar_ai_defect_operations'", 'CELAR_AIOPS_MIGRATION_LEDGER');
requireMarker(migration, 'module076_defect_number_sequence', 'CELAR_AIOPS_SERVER_DEFECT_SEQUENCE');
requireMarker(migration, 'DEF-{YYYY}-{SEQUENCE:000000}', 'CELAR_AIOPS_DEFECT_NUMBER_CONTRACT');
requireMarker(migration, 'uq_module076_active_machine_fingerprint', 'CELAR_AIOPS_ACTIVE_FINGERPRINT_UNIQUENESS');
requireMarker(migration, 'raw_private_content_stored BOOLEAN NOT NULL DEFAULT FALSE CHECK (raw_private_content_stored=FALSE)', 'CELAR_AIOPS_DATABASE_RAW_PRIVATE_CONTENT_BLOCK');
requireMarker(migration, 'contains_secret BOOLEAN NOT NULL DEFAULT FALSE CHECK (contains_secret=FALSE)', 'CELAR_AIOPS_DATABASE_SECRET_BLOCK');
requireMarker(migration, "machine_creation_enabled BOOLEAN NOT NULL DEFAULT FALSE", 'CELAR_AIOPS_DATABASE_OBSERVE_ONLY_DEFAULT');
requireMarker(migration, "('all_ai_targets','All Celar AI answer targets','all_ai_targets','test',TRUE,3,300,3,900,'Critical'", 'CELAR_AIOPS_ALL_AI_THRESHOLD');
requireMarker(migration, "('github_actions','GitHub Actions during release','github_actions','test',TRUE,2,300,3,900,'Critical'", 'CELAR_AIOPS_GITHUB_ACTIONS_THRESHOLD');
requireMarker(migration, "('module067','Module 067 notification delivery','module067','test',TRUE,5,900,3,900,'High'", 'CELAR_AIOPS_MODULE067_THRESHOLD');
requireMarker(migration, 'pulse084_append_only_defect_evidence', 'CELAR_AIOPS_APPEND_ONLY_EVIDENCE');
requireMarker(rollback, 'rollback refused because durable evidence exists', 'CELAR_AIOPS_ROLLBACK_REFUSAL');
requireMarker(rollback, "DELETE FROM schema_migrations", 'CELAR_AIOPS_CLEAN_ROLLBACK_LEDGER');

for (const marker of [
  'celar-ai-ask-operations-v1-20260810',
  '084_module_076_celar_ai_defect_operations',
  'ahmed.adeyemi@ussignal.com',
  'PROJECTPULSE_CELAR_AI_AUTOMATIC_DEFECTS_ENABLED',
  'PROJECTPULSE_CELAR_AI_SYNTHETIC_FAILURES_ENABLED',
  'SanitizeOperationalDetail',
  'IsTroubleshootingIntent',
  'IsDefectIntent',
  'Production'
]) requireMarker(contracts, marker, `CELAR_AIOPS_CONTRACT_${marker.replaceAll(/[^A-Za-z0-9]+/g, '_').toUpperCase()}`);
requireMarker(contracts, 'IsTest && Boolean("PROJECTPULSE_CELAR_AI_AUTOMATIC_DEFECTS_ENABLED", false)', 'CELAR_AIOPS_PRODUCTION_AUTO_BLOCK');
requireMarker(contracts, 'IsTest && Boolean("PROJECTPULSE_CELAR_AI_SYNTHETIC_FAILURES_ENABLED", false)', 'CELAR_AIOPS_PRODUCTION_SYNTHETIC_BLOCK');
requireMarker(contracts, '[REDACTED]', 'CELAR_AIOPS_REDACTION_MARKER');

for (const marker of [
  'TroubleshootAsync',
  'CreateIntakeSessionAsync',
  'UpdateIntakeSessionAsync',
  'SubmitIntakeSessionAsync',
  'FindMatchingDefectsAsync',
  'AddEvidenceAsync',
  'RunScheduledProbesAsync',
  'RunSyntheticFailureAsync',
  'ResolveIdentityByEmailAsync',
  'CREATE DEFECT',
  'ahmed.adeyemi@ussignal.com',
  'AutomaticDefectRateLimitReachedAsync',
  'IsSuppressedAsync',
  'IsRecoveryStableAsync',
  'QueueNotificationAsync',
  'UseProxy = false'
]) requireMarker(service, marker, `CELAR_AIOPS_SERVICE_${marker.replaceAll(/[^A-Za-z0-9]+/g, '_').toUpperCase()}`);
requireMarker(service, 'machineCreated: false', 'CELAR_AIOPS_USER_DEFECT_NOT_MACHINE');
requireMarker(service, 'machineCreated: true', 'CELAR_AIOPS_MACHINE_DEFECT_PATH');
requireMarker(service, "status='Resolved'", 'CELAR_AIOPS_MACHINE_RECOVERY');
requireMarker(service, 'actualUserId != effectiveUserId', 'CELAR_AIOPS_VIEW_AS_BLOCK');
requireMarker(service, 'rawPrivateContentStored = false', 'CELAR_AIOPS_EVIDENCE_PRIVATE_BOUNDARY');
requireNoMarker(service, 'Console.WriteLine', 'CELAR_AIOPS_NO_CONSOLE_SECRET_OUTPUT');
requireNoMarker(service, 'Process.Start', 'CELAR_AIOPS_NO_PROCESS_EXECUTION');
requireNoMarker(service, 'AllowAutoRedirect = true', 'CELAR_AIOPS_NO_REDIRECT_ENABLE');

requireMarker(monitor, 'AutomaticMonitoringEnabled', 'CELAR_AIOPS_MONITOR_TEST_GATE');
requireMarker(monitor, 'RunScheduledProbesAsync', 'CELAR_AIOPS_MONITOR_EXECUTION');
requireMarker(monitor, 'never sends prompts, private documents', 'CELAR_AIOPS_MONITOR_PRIVACY_BOUNDARY');

for (const route of [
  '/api/celar-ai/v1/operations',
  '/readiness',
  '/troubleshoot',
  '/defects/intake-sessions',
  '/defects/matches',
  '/defects/{defectNumber}',
  '/monitor-policies',
  '/synthetic-failures'
]) requireMarker(moduleSource, route, `CELAR_AIOPS_ROUTE_${route.replaceAll(/[^A-Za-z0-9]+/g, '_').toUpperCase()}`);
requireMarker(moduleSource, 'askCelarAiIsPrimaryExperience', 'CELAR_AIOPS_PRIMARY_EXPERIENCE');
requireMarker(moduleSource, 'durableSystemOfRecord = "Module 076"', 'CELAR_AIOPS_MODULE076_SYSTEM_OF_RECORD');
requireMarker(moduleSource, 'Exit Administrator View-As', 'CELAR_AIOPS_VIEW_AS_UI_MESSAGE');
requireMarker(moduleSource, 'defaultAssigneeApplied', 'CELAR_AIOPS_DEFAULT_ASSIGNMENT_EVIDENCE');
requireMarker(moduleSource, 'productionChanged = false', 'CELAR_AIOPS_NO_PRODUCTION_CHANGE');

requireMarker(generator, 'AddSingleton<CelarAiDefectOrchestrationService>()', 'CELAR_AIOPS_DI_REGISTRATION');
requireMarker(generator, 'AddHostedService<CelarAiAvailabilityMonitorService>()', 'CELAR_AIOPS_MONITOR_REGISTRATION');
requireMarker(generator, 'MapCelarAiOperationsEndpoints();', 'CELAR_AIOPS_ENDPOINT_REGISTRATION');
requireValue(count(generator, 'MapCelarAiOperationsEndpoints();') === 1, 'CELAR_AIOPS_SINGLE_ENDPOINT_REGISTRATION', 'one source insertion');

for (const marker of [
  'Troubleshoot, verify, and create a Module 076 defect',
  "projectpulse:celar-ai-open-defect-intake",
  "projectpulse:celar-ai-open-operations",
  "projectpulse:celar-ai-open-health-automation",
  'Defect questionnaire',
  'Health & automation',
  'Create defect in Module 076',
  'ahmed.adeyemi@ussignal.com',
  '/api/celar-ai/v1/operations/troubleshoot',
  '/api/celar-ai/v1/operations/defects/intake-sessions',
  '/api/celar-ai/v1/operations/monitor-policies',
  '/api/celar-ai/v1/operations/synthetic-failures'
]) requireMarker(frontend, marker, `CELAR_AIOPS_UI_${marker.replaceAll(/[^A-Za-z0-9]+/g, '_').toUpperCase()}`);
requireNoMarker(frontend, 'localStorage', 'CELAR_AIOPS_UI_NO_LOCAL_STORAGE');
requireNoMarker(frontend, 'sessionStorage', 'CELAR_AIOPS_UI_NO_SESSION_STORAGE');
requireNoMarker(frontend, 'dangerouslySetInnerHTML', 'CELAR_AIOPS_UI_NO_UNSAFE_HTML');
requireMarker(frontendCss, "html[data-theme='dark']", 'CELAR_AIOPS_UI_DARK_MODE');
requireMarker(frontendCss, '@media (max-width: 680px)', 'CELAR_AIOPS_UI_MOBILE');

for (const marker of [
  "import CelarAiAskOperations from './CelarAiAskOperations.jsx';",
  'isDefectIntakeQuestion',
  'isTroubleshootingQuestion',
  'celar-ai-answer-operational-actions',
  'Troubleshoot with Ask Celar AI',
  'Open guided defect questionnaire',
  '<CelarAiAskOperations />',
  'CELAR_AI_ASK_OPERATIONS_PRIMARY_SURFACE=Ask Celar AI'
]) requireMarker(injector, marker, `CELAR_AIOPS_INJECTOR_${marker.replaceAll(/[^A-Za-z0-9]+/g, '_').toUpperCase()}`);
requireMarker(injectorChain, "await import('./inject-celar-ai-ask-operations.mjs');", 'CELAR_AIOPS_INJECTOR_CHAIN');

for (const marker of [
  'PRODUCTION_AUTOMATIC_DEFECTS_BLOCKED',
  'PRODUCTION_SYNTHETIC_FAILURES_BLOCKED',
  'BEARER_REDACTED',
  'COOKIE_REDACTED',
  'CONNECTION_STRING_REDACTED',
  'DEFAULT_ASSIGNEE_EMAIL',
  'DEFECT_INTENT',
  'TROUBLESHOOT_INTENT',
  'AUTOMATIC_DEFECT_RATE_LIMIT'
]) requireMarker(policyTests, marker, `CELAR_AIOPS_POLICY_TEST_${marker}`);
for (const marker of [
  'MIGRATION_084_LEDGER_TIMESTAMP_STABILITY=PASSED',
  'MIGRATION_084_APPEND_ONLY_EVIDENCE=PASSED',
  'MIGRATION_084_ROLLBACK_REFUSES_DURABLE_EVIDENCE=PASSED',
  'MODULE_076_CELAR_AI_DEFECT_OPERATIONS_MIGRATION_084=PASS'
]) requireMarker(migrationTest, marker, `CELAR_AIOPS_MIGRATION_TEST_${marker.replaceAll(/[^A-Za-z0-9]+/g, '_').toUpperCase()}`);

for (const script of [files.injector, files.injectorChain, 'tests/validate-celar-ai-ask-operations.mjs']) {
  try {
    execFileSync('node', ['--check', absolute(script)], { cwd: root, stdio: 'pipe' });
    requireValue(true, `CELAR_AIOPS_NODE_SYNTAX_${path.basename(script).replaceAll(/[^A-Za-z0-9]+/g, '_').toUpperCase()}`, script);
  } catch (error) {
    requireValue(false, `CELAR_AIOPS_NODE_SYNTAX_${path.basename(script).replaceAll(/[^A-Za-z0-9]+/g, '_').toUpperCase()}`, String(error.stderr || error.message));
  }
}

let generatedServices = '';
let generatedProduction = '';
try {
  generatedServices = execFileSync('awk', ['-v', 'mode=services', '-f', absolute(files.generator), absolute('src/backend/ProjectTime.Api/Ai/ProjectPulseAiServiceCollectionExtensions.cs')], { cwd: root, encoding: 'utf8' });
  generatedProduction = execFileSync('awk', ['-v', 'mode=production', '-f', absolute(files.generator), absolute('src/backend/ProjectTime.Api/Modules/CelarAiProductionPlatformModule.cs')], { cwd: root, encoding: 'utf8' });
  requireValue(true, 'CELAR_AIOPS_GENERATOR_EXECUTION', 'service and production compiler copies generated');
} catch (error) {
  requireValue(false, 'CELAR_AIOPS_GENERATOR_EXECUTION', String(error.stderr || error.message));
}
if (generatedServices && generatedProduction) {
  requireValue(count(generatedServices, 'AddSingleton<CelarAiDefectOrchestrationService>()') === 1, 'CELAR_AIOPS_GENERATED_SINGLE_SERVICE', 'one defect orchestration service');
  requireValue(count(generatedServices, 'AddHostedService<CelarAiAvailabilityMonitorService>()') === 1, 'CELAR_AIOPS_GENERATED_SINGLE_MONITOR', 'one availability monitor');
  requireValue(count(generatedProduction, 'MapCelarAiOperationsEndpoints();') === 1, 'CELAR_AIOPS_GENERATED_SINGLE_MAP', 'one operations endpoint map');
  requireMarker(generatedProduction, '/api/celar-ai/v2/chat', 'CELAR_AIOPS_EXISTING_CHAT_ROUTE_PRESERVED');
}

let changed = [];
try {
  changed = execFileSync('git', ['diff', '--name-only', 'origin/feature/celar-ai-universal-answer-reliability-20260810...HEAD'], { cwd: root, encoding: 'utf8' })
    .split(/\r?\n/)
    .filter(Boolean);
} catch (error) {
  requireValue(false, 'CELAR_AIOPS_GIT_DIFF', error.message);
}
const allowedExact = new Set(Object.values(files));
const allowedPrefixes = [
  '.github/workflows/celar-ai-ask-operations',
  'docs/modules/module-011-pulse-ai/ASK-CELAR-AI-',
  'docs/modules/module-076-defect-tracker/CELAR-AI-',
  'docs/modules/module-078-observability-slo-health/CELAR-AI-',
  'docs/modules/module-083-full-future-loop/CELAR-AI-'
];
const unexpected = changed.filter((value) => !allowedExact.has(value) && !allowedPrefixes.some((prefix) => value.startsWith(prefix)));
requireValue(unexpected.length === 0, 'CELAR_AIOPS_SOURCE_ISOLATION', unexpected.length ? unexpected.join(', ') : `${changed.length} governed files`);
requireValue(changed.includes(files.migration) && changed.includes(files.rollback), 'CELAR_AIOPS_MIGRATION_SCOPE', 'migration and guarded rollback present');
requireValue(changed.every((value) => !value.includes('production-deploy') && !value.includes('oracle-test-runtime-deploy')), 'CELAR_AIOPS_NO_DEPLOYMENT_CONTROLLER', 'source and validation only');
requireValue(changed.every((value) => !value.endsWith('.env') && !value.toLowerCase().includes('runtime-token') && !value.toLowerCase().includes('/secrets/')), 'CELAR_AIOPS_NO_SECRET_FILE', 'no secret material');

if (failures) {
  console.error(`CELAR_AI_ASK_OPERATIONS_VALIDATION_FAILURES=${failures}`);
  process.exit(1);
}
console.log(`CELAR_AI_ASK_OPERATIONS_VALIDATION_CHECKS=${checks}`);
console.log('CELAR_AI_ASK_OPERATIONS_EVALUATION_CASES=60');
console.log('CELAR_AI_ASK_OPERATIONS_PRIMARY_SURFACE=ASK_CELAR_AI');
console.log('CELAR_AI_ASK_OPERATIONS_DURABLE_SYSTEM=MODULE_076');
console.log('CELAR_AI_ASK_OPERATIONS_SOURCE_VALIDATION=PASS');
