import fs from 'node:fs';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const abs = (value) => path.join(root, value);
const read = (value) => fs.readFileSync(abs(value), 'utf8');
const exists = (value) => fs.existsSync(abs(value));
const count = (source, marker) => source.split(marker).length - 1;
let failures = 0;
let checks = 0;

function requireValue(value, code, detail = '') {
  checks++;
  if (value) return console.log(`${code}=PASSED${detail ? ` — ${detail}` : ''}`);
  failures++;
  console.error(`${code}=FAILED${detail ? ` — ${detail}` : ''}`);
}
function requireFile(value) { requireValue(exists(value), `CELAR_AIOPS_FILE_${value.replaceAll(/[^A-Za-z0-9]+/g, '_').toUpperCase()}`, value); }
function marker(source, value, code) { requireValue(source.includes(value), code, value); }
function noMarker(source, value, code) { requireValue(!source.includes(value), code, value); }

const files = [
  'database/migrations/084_module_076_celar_ai_defect_operations.sql',
  'database/rollback/084_module_076_celar_ai_defect_operations_rollback.sql',
  'src/backend/ProjectTime.Api/Ai/CelarAiOperationsContracts.cs',
  'src/backend/ProjectTime.Api/Ai/CelarAiDefectOrchestrationService.cs',
  'src/backend/ProjectTime.Api/Ai/CelarAiDefectQueryService.cs',
  'src/backend/ProjectTime.Api/Ai/CelarAiAvailabilityMonitorService.cs',
  'src/backend/ProjectTime.Api/Modules/CelarAiOperationsModule.cs',
  'src/backend/ProjectTime.Api/Modules/CelarAiDefectQueryModule.cs',
  'src/backend/ProjectTime.Api/build/generate-celar-ai-universal-answer-reliability.awk',
  'src/frontend/project-time-web/src/CelarAiAskOperations.jsx',
  'src/frontend/project-time-web/src/celar-ai-ask-operations.css',
  'src/frontend/project-time-web/scripts/inject-celar-ai-ask-operations.mjs',
  'src/frontend/project-time-web/scripts/inject-module-076-celar-ai-operations.mjs',
  'src/frontend/project-time-web/scripts/inject-celar-ai-universal-answer-reliability.mjs',
  'tests/celar-ai-operations-evaluation-cases.json',
  'tests/CelarAiOperationsPolicyTests/CelarAiOperationsPolicyTests.csproj',
  'tests/CelarAiOperationsPolicyTests/Program.cs',
  'tests/test-module-076-celar-ai-defect-operations-migration-084.sh',
  'docs/modules/module-011-pulse-ai/ASK-CELAR-AI-OPERATIONS.md',
  'docs/modules/module-076-defect-tracker/CELAR-AI-GUIDED-INTAKE.md',
  'docs/modules/module-078-observability-slo-health/CELAR-AI-AUTOMATIC-DEFECTS.md',
  'docs/modules/module-083-full-future-loop/CELAR-AI-DEFECT-ADAPTER-ACTIVATION.md'
];
files.forEach(requireFile);
if (failures) process.exit(1);

const migration = read(files[0]);
const rollback = read(files[1]);
const contracts = read(files[2]);
const service = read(files[3]);
const query = read(files[4]);
const monitor = read(files[5]);
const moduleSource = read(files[6]);
const queryModule = read(files[7]);
const generator = read(files[8]);
const frontend = read(files[9]);
const css = read(files[10]);
const injector = read(files[11]);
const module076Injector = read(files[12]);
const injectorChain = read(files[13]);
const policyTests = read(files[16]);
const migrationTest = read(files[17]);
const architecture = read(files[18]);
const defectGuide = read(files[19]);
const monitorGuide = read(files[20]);
const adapterGuide = read(files[21]);
const corpus = JSON.parse(read(files[14]));

requireValue(corpus.contractVersion === 'celar-ai-operations-evaluation-v1-20260810', 'CELAR_AIOPS_CORPUS_VERSION');
requireValue(corpus.caseCount === 60 && corpus.cases.length === 60, 'CELAR_AIOPS_CORPUS_COUNT', '60');
const categories = new Map();
corpus.cases.forEach((item) => categories.set(item.category, (categories.get(item.category) || 0) + 1));
requireValue(categories.size === 6 && [...categories.values()].every((value) => value === 10), 'CELAR_AIOPS_CORPUS_BALANCE', '6 categories × 10');
requireValue(corpus.cases.every((item, index) => item.id === `AIOPS-${String(index + 1).padStart(3, '0')}`), 'CELAR_AIOPS_CORPUS_IDS');
requireValue(corpus.cases.every((item) => item.askCelarAiPrimarySurface && item.module076SystemOfRecord), 'CELAR_AIOPS_CORPUS_OWNERSHIP');
requireValue(corpus.cases.filter((item) => item.requiredAssigneeEmail).every((item) => item.requiredAssigneeEmail === 'ahmed.adeyemi@ussignal.com'), 'CELAR_AIOPS_CORPUS_ASSIGNEE');
const forbidden = ['view_as_mutation','ai_as_requesting_authority','secret_or_cookie_storage','raw_private_document_storage','embedding_vector_storage','unrestricted_generated_sql','duplicate_automatic_defect','production_automatic_activation'];
requireValue(corpus.cases.every((item) => forbidden.every((code) => item.forbiddenOutcomes.includes(code))), 'CELAR_AIOPS_CORPUS_BOUNDARIES');

for (const table of ['module076_defects','module076_defect_comments','module076_defect_events','module076_defect_evidence','module076_intake_sessions','module076_incident_occurrences','module076_monitor_policies','module076_probe_results','module076_monitor_suppressions','module076_notification_outbox']) marker(migration, `CREATE TABLE IF NOT EXISTS ${table}`, `CELAR_AIOPS_SCHEMA_${table.toUpperCase()}`);
marker(migration, 'module076_defect_number_sequence', 'CELAR_AIOPS_SEQUENCE');
marker(migration, 'uq_module076_active_machine_fingerprint', 'CELAR_AIOPS_FINGERPRINT_UNIQUENESS');
marker(migration, 'contains_secret BOOLEAN NOT NULL DEFAULT FALSE CHECK (contains_secret=FALSE)', 'CELAR_AIOPS_SECRET_CONSTRAINT');
marker(migration, 'raw_private_content_stored BOOLEAN NOT NULL DEFAULT FALSE CHECK (raw_private_content_stored=FALSE)', 'CELAR_AIOPS_PRIVATE_CONTENT_CONSTRAINT');
marker(migration, 'machine_creation_enabled BOOLEAN NOT NULL DEFAULT FALSE', 'CELAR_AIOPS_OBSERVE_ONLY_DEFAULT');
marker(migration, "('all_ai_targets','All Celar AI answer targets','all_ai_targets','test',TRUE,3,300,3,900,'Critical'", 'CELAR_AIOPS_AI_THRESHOLD');
marker(migration, "('github_actions','GitHub Actions during release','github_actions','test',TRUE,2,300,3,900,'Critical'", 'CELAR_AIOPS_GITHUB_THRESHOLD');
marker(migration, "('module067','Module 067 notification delivery','module067','test',TRUE,5,900,3,900,'High'", 'CELAR_AIOPS_MAIL_THRESHOLD');
marker(rollback, 'rollback refused because durable evidence exists', 'CELAR_AIOPS_ROLLBACK_GUARD');

for (const value of ['celar-ai-ask-operations-v1-20260810','084_module_076_celar_ai_defect_operations','ahmed.adeyemi@ussignal.com','PROJECTPULSE_CELAR_AI_AUTOMATIC_DEFECTS_ENABLED','PROJECTPULSE_CELAR_AI_SYNTHETIC_FAILURES_ENABLED','IsTroubleshootingIntent','IsDefectIntent','SanitizeOperationalDetail','[REDACTED]']) marker(contracts, value, `CELAR_AIOPS_CONTRACT_${value.replaceAll(/[^A-Za-z0-9]+/g, '_').toUpperCase()}`);
marker(contracts, 'IsTest && Boolean("PROJECTPULSE_CELAR_AI_AUTOMATIC_DEFECTS_ENABLED", false)', 'CELAR_AIOPS_TEST_ONLY_AUTOMATIC');
marker(contracts, 'IsTest && Boolean("PROJECTPULSE_CELAR_AI_SYNTHETIC_FAILURES_ENABLED", false)', 'CELAR_AIOPS_TEST_ONLY_SYNTHETIC');

for (const value of ['TroubleshootAsync','CreateIntakeSessionAsync','UpdateIntakeSessionAsync','SubmitIntakeSessionAsync','RunScheduledProbesAsync','RunSyntheticFailureAsync','ResolveIdentityByEmailAsync','CREATE DEFECT','AutomaticDefectRateLimitReachedAsync','IsSuppressedAsync','IsRecoveryStableAsync','QueueNotificationAsync','machineCreated: false','machineCreated: true',"status='Resolved'",'actualUserId != effectiveUserId','rawPrivateContentStored = false']) marker(service, value, `CELAR_AIOPS_SERVICE_${value.replaceAll(/[^A-Za-z0-9]+/g, '_').toUpperCase()}`);
noMarker(service, 'Process.Start', 'CELAR_AIOPS_NO_PROCESS_EXECUTION');
noMarker(service, 'Console.WriteLine', 'CELAR_AIOPS_NO_CONSOLE_OUTPUT');
marker(query, 'actual_reporter_user_id=@user', 'CELAR_AIOPS_QUERY_REPORTER_SCOPE');
marker(query, 'assignee_user_id=@user', 'CELAR_AIOPS_QUERY_ASSIGNEE_SCOPE');
marker(query, '@all=TRUE', 'CELAR_AIOPS_QUERY_MANAGER_SCOPE');
noMarker(query, 'INSERT ', 'CELAR_AIOPS_QUERY_NO_INSERT');
noMarker(query, 'UPDATE ', 'CELAR_AIOPS_QUERY_NO_UPDATE');
noMarker(query, 'DELETE ', 'CELAR_AIOPS_QUERY_NO_DELETE');
marker(monitor, 'AutomaticMonitoringEnabled', 'CELAR_AIOPS_MONITOR_GATE');
marker(monitor, 'never sends prompts, private documents', 'CELAR_AIOPS_MONITOR_PRIVACY');

for (const value of ['/api/celar-ai/v1/operations','/readiness','/troubleshoot','/defects/intake-sessions','/monitor-policies','/synthetic-failures','durableSystemOfRecord = "Module 076"','defaultAssigneeApplied','productionChanged = false','Exit Administrator View-As']) marker(moduleSource, value, `CELAR_AIOPS_API_${value.replaceAll(/[^A-Za-z0-9]+/g, '_').toUpperCase()}`);
marker(queryModule, 'MapCelarAiDefectQueryEndpoints', 'CELAR_AIOPS_QUERY_ENDPOINT_MAP');
marker(queryModule, 'actual_user_reported_or_assigned_defects', 'CELAR_AIOPS_QUERY_PUBLIC_SCOPE');
marker(queryModule, '/defects/matches', 'CELAR_AIOPS_SCOPED_MATCH_ROUTE');
requireValue(count(moduleSource, 'group.MapGet("/defects/matches"') === 0, 'CELAR_AIOPS_UNSCOPED_MATCH_ROUTE_REMOVED');

for (const value of ['AddSingleton<CelarAiDefectOrchestrationService>()','AddSingleton<CelarAiDefectQueryService>()','AddHostedService<CelarAiAvailabilityMonitorService>()','MapCelarAiOperationsEndpoints();','MapCelarAiDefectQueryEndpoints();']) marker(generator, value, `CELAR_AIOPS_GENERATOR_${value.replaceAll(/[^A-Za-z0-9]+/g, '_').toUpperCase()}`);

for (const value of ['Troubleshoot, verify, and create a Module 076 defect','Defect questionnaire','Health & automation','Create defect in Module 076','ahmed.adeyemi@ussignal.com','/api/celar-ai/v1/operations/troubleshoot','/api/celar-ai/v1/operations/defects/intake-sessions','/api/celar-ai/v1/operations/monitor-policies','/api/celar-ai/v1/operations/synthetic-failures']) marker(frontend, value, `CELAR_AIOPS_UI_${value.replaceAll(/[^A-Za-z0-9]+/g, '_').toUpperCase()}`);
noMarker(frontend, 'localStorage', 'CELAR_AIOPS_UI_NO_LOCAL_STORAGE');
noMarker(frontend, 'dangerouslySetInnerHTML', 'CELAR_AIOPS_UI_NO_UNSAFE_HTML');
marker(css, "html[data-theme='dark']", 'CELAR_AIOPS_UI_DARK');
marker(css, '@media (max-width: 680px)', 'CELAR_AIOPS_UI_MOBILE');

for (const value of ["import CelarAiAskOperations from './CelarAiAskOperations.jsx';",'isDefectIntakeQuestion','isTroubleshootingQuestion','celar-ai-answer-operational-actions','Troubleshoot with Ask Celar AI','Open guided defect questionnaire','<CelarAiAskOperations />']) marker(injector, value, `CELAR_AIOPS_INJECTOR_${value.replaceAll(/[^A-Za-z0-9]+/g, '_').toUpperCase()}`);
for (const value of ["'/api/celar-ai/v1/operations/defects?limit=200'",'Continue in Ask Celar AI','Module 076 is the durable defect system of record.','defect.defectNumber || defect.defectId']) marker(module076Injector, value, `CELAR_AIOPS_MODULE076_UI_${value.replaceAll(/[^A-Za-z0-9]+/g, '_').toUpperCase()}`);
marker(injectorChain, "await import('./inject-celar-ai-ask-operations.mjs');", 'CELAR_AIOPS_CHAIN_OPERATIONS');
marker(injectorChain, "await import('./inject-module-076-celar-ai-operations.mjs');", 'CELAR_AIOPS_CHAIN_MODULE076');

for (const value of ['PRODUCTION_AUTOMATIC_DEFECTS_BLOCKED','PRODUCTION_SYNTHETIC_FAILURES_BLOCKED','BEARER_REDACTED','COOKIE_REDACTED','CONNECTION_STRING_REDACTED','DEFAULT_ASSIGNEE_EMAIL','AUTOMATIC_DEFECT_RATE_LIMIT']) marker(policyTests, value, `CELAR_AIOPS_POLICY_TEST_${value}`);
for (const value of ['MIGRATION_084_LEDGER_TIMESTAMP_STABILITY=PASSED','MIGRATION_084_APPEND_ONLY_EVIDENCE=PASSED','MIGRATION_084_ROLLBACK_REFUSES_DURABLE_EVIDENCE=PASSED','MODULE_076_CELAR_AI_DEFECT_OPERATIONS_MIGRATION_084=PASS']) marker(migrationTest, value, `CELAR_AIOPS_MIGRATION_TEST_${value.replaceAll(/[^A-Za-z0-9]+/g, '_').toUpperCase()}`);

for (const [source, value, code] of [[architecture,'Ask Celar AI is the user-facing entry point','CELAR_AIOPS_DOC_PRIMARY'],[architecture,'Protected Test activation sequence','CELAR_AIOPS_DOC_ACTIVATION'],[defectGuide,'Module 076 is the durable defect system of record','CELAR_AIOPS_DOC_DEFECT_OWNERSHIP'],[monitorGuide,'Two-key activation','CELAR_AIOPS_DOC_MONITOR_GATE'],[adapterGuide,'Out-of-band watchdog','CELAR_AIOPS_DOC_WATCHDOG_GAP']]) marker(source, value, code);

for (const script of ['src/frontend/project-time-web/scripts/inject-celar-ai-ask-operations.mjs','src/frontend/project-time-web/scripts/inject-module-076-celar-ai-operations.mjs','src/frontend/project-time-web/scripts/inject-celar-ai-universal-answer-reliability.mjs','tests/validate-celar-ai-ask-operations-source.mjs']) {
  try { execFileSync('node', ['--check', abs(script)], { cwd: root, stdio: 'pipe' }); requireValue(true, `CELAR_AIOPS_NODE_${path.basename(script).replaceAll(/[^A-Za-z0-9]+/g, '_').toUpperCase()}`); }
  catch (error) { requireValue(false, `CELAR_AIOPS_NODE_${path.basename(script).replaceAll(/[^A-Za-z0-9]+/g, '_').toUpperCase()}`, String(error.stderr || error.message)); }
}

let generatedServices = '';
let generatedProduction = '';
try {
  generatedServices = execFileSync('awk', ['-v','mode=services','-f',abs('src/backend/ProjectTime.Api/build/generate-celar-ai-universal-answer-reliability.awk'),abs('src/backend/ProjectTime.Api/Ai/ProjectPulseAiServiceCollectionExtensions.cs')], { cwd: root, encoding: 'utf8' });
  generatedProduction = execFileSync('awk', ['-v','mode=production','-f',abs('src/backend/ProjectTime.Api/build/generate-celar-ai-universal-answer-reliability.awk'),abs('src/backend/ProjectTime.Api/Modules/CelarAiProductionPlatformModule.cs')], { cwd: root, encoding: 'utf8' });
  requireValue(true, 'CELAR_AIOPS_GENERATOR_EXECUTION');
} catch (error) { requireValue(false, 'CELAR_AIOPS_GENERATOR_EXECUTION', String(error.stderr || error.message)); }
if (generatedServices && generatedProduction) {
  requireValue(count(generatedServices, 'AddSingleton<CelarAiDefectOrchestrationService>()') === 1, 'CELAR_AIOPS_GENERATED_SERVICE');
  requireValue(count(generatedServices, 'AddSingleton<CelarAiDefectQueryService>()') === 1, 'CELAR_AIOPS_GENERATED_QUERY');
  requireValue(count(generatedServices, 'AddHostedService<CelarAiAvailabilityMonitorService>()') === 1, 'CELAR_AIOPS_GENERATED_MONITOR');
  requireValue(count(generatedProduction, 'MapCelarAiOperationsEndpoints();') === 1, 'CELAR_AIOPS_GENERATED_OPERATIONS_MAP');
  requireValue(count(generatedProduction, 'MapCelarAiDefectQueryEndpoints();') === 1, 'CELAR_AIOPS_GENERATED_QUERY_MAP');
  marker(generatedProduction, '/api/celar-ai/v2/chat', 'CELAR_AIOPS_CHAT_ROUTE_PRESERVED');
}

let changed = [];
try {
  changed = execFileSync('git', ['diff','--name-only','origin/feature/celar-ai-universal-answer-reliability-20260810...HEAD'], { cwd: root, encoding: 'utf8' }).split(/\r?\n/).filter(Boolean);
} catch (error) { requireValue(false, 'CELAR_AIOPS_DIFF_READ', error.message); }
const allowed = [
  '.github/workflows/celar-ai-ask-operations',
  'database/migrations/084_',
  'database/rollback/084_',
  'docs/modules/module-011-pulse-ai/ASK-CELAR-AI-',
  'docs/modules/module-076-defect-tracker/CELAR-AI-',
  'docs/modules/module-078-observability-slo-health/CELAR-AI-',
  'docs/modules/module-083-full-future-loop/CELAR-AI-',
  'src/backend/ProjectTime.Api/Ai/CelarAi',
  'src/backend/ProjectTime.Api/Modules/CelarAi',
  'src/backend/ProjectTime.Api/build/generate-celar-ai-universal-answer-reliability.awk',
  'src/frontend/project-time-web/scripts/backup-celar-ai-production-sources.mjs',
  'src/frontend/project-time-web/scripts/restore-celar-ai-production-sources.mjs',
  'src/frontend/project-time-web/scripts/inject-celar-ai-',
  'src/frontend/project-time-web/scripts/inject-module-076-',
  'src/frontend/project-time-web/src/CelarAiAskOperations.jsx',
  'src/frontend/project-time-web/src/celar-ai-ask-operations.css',
  'tests/CelarAiOperationsPolicyTests/',
  'tests/celar-ai-operations-evaluation-cases.json',
  'tests/test-module-076-celar-ai-defect-operations-migration-084.sh',
  'tests/validate-celar-ai-ask-operations-source.mjs'
];
const unexpected = changed.filter((file) => !allowed.some((prefix) => file.startsWith(prefix)));
requireValue(unexpected.length === 0, 'CELAR_AIOPS_SOURCE_SCOPE', unexpected.length ? unexpected.join(', ') : `${changed.length} governed files`);
requireValue(changed.every((file) => !file.endsWith('.env') && !file.toLowerCase().includes('runtime-token') && !file.toLowerCase().includes('/secrets/')), 'CELAR_AIOPS_NO_SECRET_FILE');
requireValue(changed.every((file) => !file.includes('production-deploy') && !file.includes('oracle-test-runtime-deploy')), 'CELAR_AIOPS_NO_DEPLOYMENT_CONTROLLER');

if (failures) {
  console.error(`CELAR_AI_ASK_OPERATIONS_SOURCE_FAILURES=${failures}`);
  process.exit(1);
}
console.log(`CELAR_AI_ASK_OPERATIONS_SOURCE_CHECKS=${checks}`);
console.log('CELAR_AI_ASK_OPERATIONS_EVALUATION_CASES=60');
console.log('CELAR_AI_ASK_OPERATIONS_PRIMARY_SURFACE=ASK_CELAR_AI');
console.log('CELAR_AI_ASK_OPERATIONS_SYSTEM_OF_RECORD=MODULE_076');
console.log('CELAR_AI_ASK_OPERATIONS_SOURCE_VALIDATION=PASS');
