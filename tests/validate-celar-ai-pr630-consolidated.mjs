import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const absolute = (value) => path.join(root, value);
const read = (value) => fs.readFileSync(absolute(value), 'utf8');
let failures = 0;
let checks = 0;
const requireValue = (condition, code, detail = '') => {
  checks++;
  if (condition) return console.log(`${code}=PASSED${detail ? ` — ${detail}` : ''}`);
  failures++;
  console.error(`${code}=FAILED${detail ? ` — ${detail}` : ''}`);
};
const marker = (source, value, code) => requireValue(source.includes(value), code, value);
const noMarker = (source, value, code) => requireValue(!source.includes(value), code, value);
const count = (source, value) => source.split(value).length - 1;

const requiredFiles = [
  'database/migrations/084_module_076_celar_ai_defect_operations.sql',
  'database/rollback/084_module_076_celar_ai_defect_operations_rollback.sql',
  'src/backend/ProjectTime.Api/Ai/CelarAiOperationalFeatureFlags.cs',
  'src/backend/ProjectTime.Api/Ai/CelarAiRealProbeService.cs',
  'src/backend/ProjectTime.Api/Ai/CelarAiUniversalAnswerReliability.cs',
  'src/backend/ProjectTime.Api/Ai/CelarAiUniversalToolCatalog.cs',
  'src/backend/ProjectTime.Api/Modules/CelarAiDefectQueryModule.cs',
  'src/backend/ProjectTime.Api/Modules/CelarAiOperationsModule.cs',
  'src/backend/ProjectTime.Api/build/generate-celar-ai-operations-governance.py',
  'src/backend/ProjectTime.Api/build/generate-celar-ai-universal-answer-reliability.awk',
  'src/frontend/project-time-web/src/CelarAiAnswerReliabilityWorkbench.jsx',
  'src/frontend/project-time-web/src/CelarAiAskOperations.jsx',
  'tests/celar-ai-operations-evaluation-cases.json',
  'tests/celar-ai-universal-answer-evaluation-cases.json'
];
for (const file of requiredFiles) requireValue(fs.existsSync(absolute(file)), `CELAR_PR630_FILE_${file.replaceAll(/[^A-Za-z0-9]+/g, '_').toUpperCase()}`, file);
if (failures) process.exit(1);

const migration = read(requiredFiles[0]);
const rollback = read(requiredFiles[1]);
const flags = read(requiredFiles[2]);
const probes = read(requiredFiles[3]);
const reliability = read(requiredFiles[4]);
const catalog = read(requiredFiles[5]);
const queryModule = read(requiredFiles[6]);
const operationsModule = read(requiredFiles[7]);
const operationsGenerator = read(requiredFiles[8]);
const universalGeneratorPath = requiredFiles[9];
const reliabilityUi = read(requiredFiles[10]);
const operationsUi = read(requiredFiles[11]);
const operationsCorpus = JSON.parse(read(requiredFiles[12]));
const universalCorpus = JSON.parse(read(requiredFiles[13]));

requireValue(operationsCorpus.caseCount === 60 && operationsCorpus.cases.length === 60, 'CELAR_PR630_OPERATIONS_CORPUS', '60 cases');
requireValue(universalCorpus.caseCount === 120 && universalCorpus.cases.length === 120, 'CELAR_PR630_UNIVERSAL_CORPUS', '120 cases');
requireValue(operationsCorpus.cases.every((item) => item.askCelarAiPrimarySurface === true && item.module076SystemOfRecord === true), 'CELAR_PR630_OWNERSHIP', 'Ask Celar AI primary; Module 076 durable');

for (const table of ['module076_defects', 'module076_defect_events', 'module076_defect_evidence', 'module076_monitor_policies', 'module076_probe_results', 'module076_notification_outbox']) marker(migration, `CREATE TABLE IF NOT EXISTS ${table}`, `CELAR_PR630_TABLE_${table.toUpperCase()}`);
marker(migration, 'machine_creation_enabled BOOLEAN NOT NULL DEFAULT FALSE', 'CELAR_PR630_OBSERVE_ONLY_DEFAULT');
marker(migration, 'raw_private_content_stored BOOLEAN NOT NULL DEFAULT FALSE CHECK (raw_private_content_stored=FALSE)', 'CELAR_PR630_PRIVATE_CONTENT_BLOCK');
marker(migration, 'contains_secret BOOLEAN NOT NULL DEFAULT FALSE CHECK (contains_secret=FALSE)', 'CELAR_PR630_SECRET_BLOCK');
marker(migration, 'pulse084_append_only_defect_evidence', 'CELAR_PR630_APPEND_ONLY_EVIDENCE');
marker(rollback, 'rollback refused because durable evidence exists', 'CELAR_PR630_GUARDED_ROLLBACK');

marker(flags, 'PROJECTPULSE_CELAR_AI_MONITORING_ENABLED', 'CELAR_PR630_OBSERVE_FLAG');
marker(flags, 'AutomaticDefectsEnabled', 'CELAR_PR630_AUTOMATIC_DEFECT_FLAG');
marker(flags, 'productionAutomaticDefectsAllowed = false', 'CELAR_PR630_PRODUCTION_MACHINE_BLOCK');
marker(probes, 'All external targets are exact allowlisted HTTPS endpoints.', 'CELAR_PR630_PROBE_ALLOWLIST');
noMarker(probes, 'Process.Start', 'CELAR_PR630_NO_PROCESS_EXECUTION');
marker(queryModule, '/defects/matches', 'CELAR_PR630_SCOPED_MATCH_ROUTE');
marker(queryModule, 'effective_user_reported_or_assigned_defects', 'CELAR_PR630_EFFECTIVE_USER_SCOPE');

for (const value of ['REMOVE_UNSCOPED_MATCH_ROUTE', 'EFFECTIVE_USER_DEFECT_READ', 'CelarAiOperationalFeatureFlags.MonitoringEnabled', '_realProbes.RunAsync']) marker(operationsGenerator, value, `CELAR_PR630_OPERATIONS_GENERATOR_${value.replaceAll(/[^A-Za-z0-9]+/g, '_').toUpperCase()}`);
const temp = fs.mkdtempSync(path.join(os.tmpdir(), 'celar-pr630-'));
try {
  const generatedModule = path.join(temp, 'CelarAiOperationsModule.g.cs');
  execFileSync('python3', [absolute(requiredFiles[8]), '--mode', 'module', '--input', absolute(requiredFiles[7]), '--output', generatedModule], { cwd: root, stdio: 'pipe' });
  const generated = fs.readFileSync(generatedModule, 'utf8');
  requireValue(count(generated, 'group.MapGet("/defects/matches"') === 0, 'CELAR_PR630_UNSCOPED_ROUTE_REMOVED', 'generated compiler source');
  marker(generated, 'access.Effective', 'CELAR_PR630_EFFECTIVE_READ_AUTHORITY');
} finally {
  fs.rmSync(temp, { recursive: true, force: true });
}

marker(reliability, 'celar-ai-universal-answer-reliability-v2-20260810', 'CELAR_PR630_RELIABILITY_V2');
marker(reliability, 'external model response has no authorized internal evidence', 'CELAR_PR630_EXTERNAL_INTERNAL_BOUNDARY');
marker(catalog, 'MutationAllowed: false', 'CELAR_PR630_TOOL_MUTATION_BLOCK');
const toolCodes = [...catalog.matchAll(/Tool\("([a-z0-9_]+)"/g)].map((match) => match[1]);
requireValue(toolCodes.length >= 37 && new Set(toolCodes).size === toolCodes.length, 'CELAR_PR630_TOOL_CATALOG', `${toolCodes.length} unique governed tools`);

const generatedServices = execFileSync('awk', ['-v', 'mode=services', '-f', absolute(universalGeneratorPath), absolute('src/backend/ProjectTime.Api/Ai/ProjectPulseAiServiceCollectionExtensions.cs')], { cwd: root, encoding: 'utf8' });
const generatedProduction = execFileSync('awk', ['-v', 'mode=production', '-f', absolute(universalGeneratorPath), absolute('src/backend/ProjectTime.Api/Modules/CelarAiProductionPlatformModule.cs')], { cwd: root, encoding: 'utf8' });
requireValue(count(generatedServices, 'AddSingleton<CelarAiUniversalAnswerReliabilityService>()') === 1, 'CELAR_PR630_SINGLE_RELIABILITY_SERVICE');
requireValue(count(generatedProduction, 'MapCelarAiUniversalAnswerReliabilityEndpoints();') === 1, 'CELAR_PR630_SINGLE_RELIABILITY_MAP');
requireValue(count(generatedProduction, 'universalReliability.Enforce(') === 1, 'CELAR_PR630_SINGLE_POST_ANSWER_GATE');

for (const source of [reliabilityUi, operationsUi]) {
  noMarker(source, 'localStorage', 'CELAR_PR630_UI_NO_LOCAL_STORAGE');
  noMarker(source, 'sessionStorage', 'CELAR_PR630_UI_NO_SESSION_STORAGE');
  noMarker(source, 'dangerouslySetInnerHTML', 'CELAR_PR630_UI_NO_UNSAFE_HTML');
}

let changed = [];
try {
  changed = execFileSync('git', ['diff', '--name-only', 'origin/main...HEAD'], { cwd: root, encoding: 'utf8' }).split(/\r?\n/).filter(Boolean);
} catch (error) {
  requireValue(false, 'CELAR_PR630_GIT_DIFF', error.message);
}
const allowedPrefixes = [
  '.github/workflows/celar-ai-',
  'database/migrations/084_module_076_',
  'database/rollback/084_module_076_',
  'docs/modules/module-011-pulse-ai/ASK-CELAR-AI-',
  'docs/modules/module-011-pulse-ai/UNIVERSAL-ANSWER-',
  'docs/modules/module-076-defect-tracker/CELAR-AI-',
  'docs/modules/module-078-observability-slo-health/CELAR-AI-',
  'docs/modules/module-083-full-future-loop/CELAR-AI-',
  'src/backend/ProjectTime.Api/Ai/CelarAi',
  'src/backend/ProjectTime.Api/Modules/CelarAi',
  'src/backend/ProjectTime.Api/build/generate-celar-ai-',
  'src/frontend/project-time-web/scripts/backup-celar-ai-',
  'src/frontend/project-time-web/scripts/restore-celar-ai-',
  'src/frontend/project-time-web/scripts/inject-celar-ai-',
  'src/frontend/project-time-web/scripts/inject-module-076-',
  'src/frontend/project-time-web/src/CelarAi',
  'src/frontend/project-time-web/src/celar-ai-',
  'tests/CelarAiOperationsPolicyTests/',
  'tests/CelarAiUniversalAnswerReliabilityTests/',
  'tests/celar-ai-operations-',
  'tests/celar-ai-universal-answer-',
  'tests/test-module-076-',
  'tests/validate-celar-ai-'
];
const allowedExact = new Set(['src/backend/ProjectTime.Api/Directory.Build.targets']);
const unexpected = changed.filter((file) => !allowedExact.has(file) && !allowedPrefixes.some((prefix) => file.startsWith(prefix)));
requireValue(unexpected.length === 0, 'CELAR_PR630_SOURCE_SCOPE', unexpected.length ? unexpected.join(', ') : `${changed.length} governed files`);
requireValue(changed.includes(requiredFiles[0]) && changed.includes(requiredFiles[1]), 'CELAR_PR630_MIGRATION_SCOPE', 'Migration 084 and guarded rollback');
requireValue(!changed.includes('.github/workflows/celar-ai-source-snapshot-temp.yml'), 'CELAR_PR630_TEMP_SNAPSHOT_REMOVED');
requireValue(changed.every((file) => !file.startsWith('deployment/') && !file.includes('projectpulse-deploy-') && !file.includes('oracle-test-runtime-deploy')), 'CELAR_PR630_NO_DEPLOYMENT_CONTROLLER');
requireValue(changed.every((file) => !file.endsWith('.env') && !file.includes('/secrets/') && !file.toLowerCase().includes('runtime-token')), 'CELAR_PR630_NO_SECRET_FILE');

if (failures) {
  console.error(`CELAR_AI_PR630_CONSOLIDATED_FAILURES=${failures}`);
  process.exit(1);
}
console.log(`CELAR_AI_PR630_CONSOLIDATED_CHECKS=${checks}`);
console.log('CELAR_AI_PR630_CONSOLIDATED_SOURCE=PASS');
console.log('CELAR_AI_PR630_TEST_DEPLOYMENTS=0');
console.log('CELAR_AI_PR630_PRODUCTION_MUTATIONS=0');
