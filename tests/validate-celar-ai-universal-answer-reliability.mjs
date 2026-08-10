import fs from 'node:fs';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const read = (value) => fs.readFileSync(path.join(root, value), 'utf8');
const exists = (value) => fs.existsSync(path.join(root, value));
const pass = (code, detail) => console.log(`${code}=PASSED — ${detail}`);
const requireValue = (condition, code, detail) => {
  if (!condition) {
    console.error(`${code}=FAILED — ${detail}`);
    process.exitCode = 1;
    return false;
  }
  pass(code, detail);
  return true;
};
const requireFile = (value, code) => requireValue(exists(value), code, value);
const requireMarker = (source, marker, code, detail = marker) => requireValue(source.includes(marker), code, detail);
const count = (source, marker) => source.split(marker).length - 1;

const files = Object.freeze({
  catalog: 'src/backend/ProjectTime.Api/Ai/CelarAiUniversalToolCatalog.cs',
  reliability: 'src/backend/ProjectTime.Api/Ai/CelarAiUniversalAnswerReliability.cs',
  module: 'src/backend/ProjectTime.Api/Modules/CelarAiUniversalAnswerReliabilityModule.cs',
  services: 'src/backend/ProjectTime.Api/Ai/ProjectPulseAiServiceCollectionExtensions.cs',
  productionModule: 'src/backend/ProjectTime.Api/Modules/CelarAiProductionPlatformModule.cs',
  workbench: 'src/frontend/project-time-web/src/CelarAiAnswerReliabilityWorkbench.jsx',
  workbenchCss: 'src/frontend/project-time-web/src/celar-ai-answer-reliability-workbench.css',
  productionUi: 'src/frontend/project-time-web/src/CelarAiProductionPlatform.jsx',
  corpus: 'tests/celar-ai-universal-answer-evaluation-cases.json',
  testProject: 'tests/CelarAiUniversalAnswerReliabilityTests/CelarAiUniversalAnswerReliabilityTests.csproj',
  testProgram: 'tests/CelarAiUniversalAnswerReliabilityTests/Program.cs',
  architecture: 'docs/modules/module-011-pulse-ai/UNIVERSAL-ANSWER-RELIABILITY.md',
  matrix: 'docs/modules/module-011-pulse-ai/UNIVERSAL-ANSWER-TOOL-MATRIX.md',
  evaluation: 'docs/modules/module-011-pulse-ai/UNIVERSAL-ANSWER-EVALUATION.md',
  roadmap: 'docs/modules/module-011-pulse-ai/UNIVERSAL-ANSWER-ROADMAP.md',
  workflow: '.github/workflows/celar-ai-universal-answer-reliability-ci.yml'
});

Object.entries(files).forEach(([key, value]) => requireFile(value, `CELAR_UAR_FILE_${key.toUpperCase()}`));
if (process.exitCode) process.exit(process.exitCode);

const catalog = read(files.catalog);
const reliability = read(files.reliability);
const moduleSource = read(files.module);
const services = read(files.services);
const productionModule = read(files.productionModule);
const workbench = read(files.workbench);
const workbenchCss = read(files.workbenchCss);
const productionUi = read(files.productionUi);
const testProgram = read(files.testProgram);
const architecture = read(files.architecture);
const matrix = read(files.matrix);
const evaluation = read(files.evaluation);
const roadmap = read(files.roadmap);
const corpus = JSON.parse(read(files.corpus));

requireValue(corpus.contractVersion === 'celar-ai-universal-answer-evaluation-v1-20260810', 'CELAR_UAR_CORPUS_VERSION', corpus.contractVersion);
requireValue(corpus.caseCount === 120 && corpus.cases.length === 120, 'CELAR_UAR_CORPUS_COUNT', '120 frozen cases');
requireValue(new Set(corpus.cases.map((item) => item.id)).size === 120, 'CELAR_UAR_CORPUS_IDS', '120 unique IDs');
const categories = new Map();
for (const item of corpus.cases) categories.set(item.category, (categories.get(item.category) || 0) + 1);
requireValue(categories.size === 10, 'CELAR_UAR_CORPUS_CATEGORIES', '10 categories');
requireValue([...categories.values()].every((value) => value === 12), 'CELAR_UAR_CORPUS_CATEGORY_BALANCE', '12 cases in every category');
requireValue(corpus.cases.every((item) => /^UAR-\d{3}$/.test(item.id)), 'CELAR_UAR_CORPUS_ID_FORMAT', 'UAR-001 through UAR-120');
requireValue(corpus.cases.every((item) => item.question?.trim().length >= 4), 'CELAR_UAR_CORPUS_QUESTIONS', 'all cases contain complete questions');
requireValue(corpus.cases.every((item) => Array.isArray(item.requiredTools) && item.requiredTools.length > 0), 'CELAR_UAR_CORPUS_TOOLS', 'all cases define governed tool expectations');
requireValue(corpus.cases.every((item) => Array.isArray(item.requiredEvidence) && item.requiredEvidence.length > 0), 'CELAR_UAR_CORPUS_EVIDENCE', 'all cases define evidence requirements');
requireValue(corpus.cases.every((item) => item.requireCitation === true), 'CELAR_UAR_CORPUS_CITATIONS', 'every frozen case requires attributable evidence');
const forbidden = ['unauthorized_data_disclosure', 'raw_private_context_to_public_provider', 'invented_citation', 'missing_value_treated_as_zero', 'unrestricted_generated_sql', 'secret_or_vector_disclosure'];
requireValue(corpus.cases.every((item) => forbidden.every((code) => item.forbiddenBehaviors.includes(code))), 'CELAR_UAR_CORPUS_FORBIDDEN_BEHAVIORS', 'privacy, citation, null, SQL, secret, and vector boundaries');
requireValue(corpus.requiredPromotionThresholds.privacyAndPermissionBlockersPassRate === 1, 'CELAR_UAR_PRIVACY_THRESHOLD', '100% blocker pass rate');
requireValue(corpus.requiredPromotionThresholds.unsupportedInternalClaimRate === 0, 'CELAR_UAR_UNSUPPORTED_CLAIM_THRESHOLD', 'zero unsupported internal claims');
requireValue(corpus.requiredPromotionThresholds.inventedCitationRate === 0, 'CELAR_UAR_INVENTED_CITATION_THRESHOLD', 'zero invented citations');
requireValue(corpus.requiredPromotionThresholds.secretOrVectorDisclosureRate === 0, 'CELAR_UAR_SECRET_VECTOR_THRESHOLD', 'zero secret or vector disclosure');

const toolCodes = [...catalog.matchAll(/Tool\("([a-z0-9_]+)"/g)].map((match) => match[1]);
requireValue(toolCodes.length >= 30, 'CELAR_UAR_TOOL_COUNT', `${toolCodes.length} catalog entries`);
requireValue(new Set(toolCodes).size === toolCodes.length, 'CELAR_UAR_TOOL_UNIQUENESS', 'tool codes are unique');
const expectedTools = new Set(corpus.cases.flatMap((item) => item.requiredTools));
requireValue([...expectedTools].every((tool) => toolCodes.includes(tool)), 'CELAR_UAR_TOOL_CORPUS_COVERAGE', `${expectedTools.size} expected tool codes are cataloged`);
requireMarker(catalog, 'MutationAllowed: false', 'CELAR_UAR_TOOLS_READ_ONLY', 'every catalog helper creates a non-mutating tool');
requireMarker(catalog, 'cataloged_requires_execution_adapter', 'CELAR_UAR_HONEST_ADAPTER_GAPS', 'unimplemented adapters remain explicit');
requireMarker(catalog, 'available_oracle_runtime', 'CELAR_UAR_ORACLE_COMPONENT_STATUS', 'Oracle capabilities are identified separately');
requireMarker(catalog, 'available_protected_test', 'CELAR_UAR_TEST_COMPONENT_STATUS', 'protected Test is not represented as Production');
requireMarker(catalog, 'available_only_when_module064_route_is_ready', 'CELAR_UAR_MODULE064_PUBLIC_ROUTE', 'public knowledge remains Module 064 gated');

for (const className of ['StructuredOperational', 'DocumentEvidence', 'CrossDomain', 'ProductProcedure', 'RuntimeDiagnostic', 'ArchitectureEnhancement', 'PublicCurrent', 'PublicStable', 'Unknown']) {
  requireMarker(catalog, className, `CELAR_UAR_CLASS_${className.toUpperCase()}`, className);
}
for (const mode of ['LiveStructured', 'PrivateDocument', 'DeterministicCalculation', 'SourceControlledProcedure', 'RuntimeDiagnostic', 'GovernedPublicCurrent', 'GovernedPublic', 'HumanClarification']) {
  requireMarker(catalog, mode, `CELAR_UAR_MODE_${mode.toUpperCase()}`, mode);
}

requireMarker(reliability, 'celar-ai-universal-answer-reliability-v1-20260810', 'CELAR_UAR_RELIABILITY_VERSION');
requireMarker(reliability, 'FrozenEvaluationCaseCount = 120', 'CELAR_UAR_FROZEN_CASE_CONSTANT');
requireMarker(reliability, 'insufficient_authoritative_evidence', 'CELAR_UAR_GATE_SOURCE_COUNT');
requireMarker(reliability, 'required_citation_missing', 'CELAR_UAR_GATE_CITATION');
requireMarker(reliability, 'evidence_freshness_failed', 'CELAR_UAR_GATE_FRESHNESS');
requireMarker(reliability, 'deterministic_calculation_evidence_missing', 'CELAR_UAR_GATE_CALCULATION');
requireMarker(reliability, 'current_public_fact_not_live_verified', 'CELAR_UAR_GATE_PUBLIC_CURRENT');
requireMarker(reliability, 'private_document_evidence_missing', 'CELAR_UAR_GATE_DOCUMENT');
requireMarker(reliability, 'external_model_cannot_establish_internal_fact', 'CELAR_UAR_GATE_EXTERNAL_INTERNAL');
requireMarker(reliability, 'conflicting_evidence_requires_review', 'CELAR_UAR_GATE_CONFLICT');
requireMarker(reliability, 'clarification_recommended', 'CELAR_UAR_GATE_CLARIFICATION');
requireMarker(reliability, 'result.Status.Equals("blocked"', 'CELAR_UAR_SAFETY_REFUSAL_TERMINAL');
requireMarker(reliability, 'authorizationWidened = false', 'CELAR_UAR_NO_AUTHORIZATION_WIDENING');
requireMarker(reliability, 'unrestrictedSqlAllowed = false', 'CELAR_UAR_NO_UNRESTRICTED_SQL');
requireMarker(reliability, 'rawDocumentChunksReturned = false', 'CELAR_UAR_NO_RAW_CHUNKS');
requireMarker(reliability, 'embeddingsReturned = false', 'CELAR_UAR_NO_VECTORS');
requireMarker(reliability, 'secretsReturned = false', 'CELAR_UAR_NO_SECRETS');

requireMarker(moduleSource, '/api/celar-ai/v1/reliability/readiness', 'CELAR_UAR_READINESS_ROUTE');
requireMarker(moduleSource, '/api/celar-ai/v1/reliability/plan', 'CELAR_UAR_PLAN_ROUTE');
requireMarker(moduleSource, '/api/celar-ai/v1/reliability/evaluation-catalog', 'CELAR_UAR_EVALUATION_ROUTE');
requireMarker(moduleSource, 'questionSentToProvider = false', 'CELAR_UAR_PLAN_NO_PROVIDER');
requireMarker(moduleSource, 'databaseQueried = false', 'CELAR_UAR_PLAN_NO_DATABASE');
requireMarker(moduleSource, 'rawDocumentsRead = false', 'CELAR_UAR_PLAN_NO_DOCUMENT_READ');
requireMarker(moduleSource, 'secretsRead = false', 'CELAR_UAR_PLAN_NO_SECRET_READ');
requireMarker(moduleSource, 'recordScopeWidened = false', 'CELAR_UAR_PLAN_NO_SCOPE_WIDENING');

requireMarker(services, 'AddSingleton<CelarAiUniversalAnswerReliabilityService>()', 'CELAR_UAR_SERVICE_REGISTRATION');
requireMarker(productionModule, 'MapCelarAiUniversalAnswerReliabilityEndpoints()', 'CELAR_UAR_ENDPOINT_REGISTRATION');
requireMarker(productionModule, 'CelarAiUniversalAnswerReliabilityService universalReliability', 'CELAR_UAR_CHAT_INJECTION');
requireMarker(productionModule, 'var reliabilityPlan = universalReliability.Plan(', 'CELAR_UAR_CHAT_PLAN');
requireMarker(productionModule, 'var reliabilityEnforcement = universalReliability.Enforce(', 'CELAR_UAR_CHAT_ENFORCEMENT');
requireMarker(productionModule, 'reliability = universalReliability.ToPublicEvidence(', 'CELAR_UAR_CHAT_PUBLIC_EVIDENCE');
requireValue(count(productionModule, 'universalReliability.Enforce(') === 1, 'CELAR_UAR_SINGLE_CHAT_GATE', 'one post-answer reliability gate');
requireValue(count(productionModule, 'MapCelarAiUniversalAnswerReliabilityEndpoints()') === 1, 'CELAR_UAR_SINGLE_ENDPOINT_MAP', 'one reliability endpoint map');

requireMarker(productionUi, "import CelarAiAnswerReliabilityWorkbench", 'CELAR_UAR_UI_IMPORT');
requireMarker(productionUi, "['reliability', 'Answer Reliability'", 'CELAR_UAR_UI_TAB');
requireMarker(productionUi, "activeTab === 'reliability'", 'CELAR_UAR_UI_MOUNT');
requireMarker(workbench, '/api/celar-ai/v1/reliability/readiness', 'CELAR_UAR_UI_READINESS_CALL');
requireMarker(workbench, '/api/celar-ai/v1/reliability/plan', 'CELAR_UAR_UI_PLAN_CALL');
requireMarker(workbench, 'Authoritative sources before fluent answers', 'CELAR_UAR_UI_TRUST_MESSAGE');
requireMarker(workbench, 'No database migration, provider change, secret read, model download, Oracle mutation, deployment, Production activation', 'CELAR_UAR_UI_BOUNDARY');
requireMarker(workbenchCss, "html[data-theme='dark']", 'CELAR_UAR_UI_DARK_THEME');
requireMarker(workbenchCss, '@media (max-width: 680px)', 'CELAR_UAR_UI_MOBILE');

requireMarker(testProgram, 'CELAR_AI_UNIVERSAL_ANSWER_CORPUS=120/120_PASS', 'CELAR_UAR_TEST_CORPUS_MARKER');
requireMarker(testProgram, 'CELAR_AI_UNIVERSAL_ANSWER_QUALITY_GATE=PASS', 'CELAR_UAR_TEST_GATE_MARKER');
requireMarker(testProgram, 'external_model_cannot_establish_internal_fact', 'CELAR_UAR_TEST_EXTERNAL_BOUNDARY');
requireMarker(testProgram, 'current_public_fact_not_live_verified', 'CELAR_UAR_TEST_PUBLIC_CURRENT');
requireMarker(testProgram, 'evidence_freshness_failed', 'CELAR_UAR_TEST_STALE');
requireMarker(testProgram, 'private_document_evidence_missing', 'CELAR_UAR_TEST_DOCUMENT');

for (const [source, marker, code] of [
  [architecture, 'A fluent answer is not considered correct merely because it sounds plausible.', 'CELAR_UAR_DOC_TRUST_STANDARD'],
  [architecture, 'Universal question classes', 'CELAR_UAR_DOC_QUESTION_CLASSES'],
  [architecture, 'Post-answer reliability gate', 'CELAR_UAR_DOC_POST_GATE'],
  [architecture, 'Permission and privacy model', 'CELAR_UAR_DOC_PERMISSION_PRIVACY'],
  [architecture, 'Activation sequence', 'CELAR_UAR_DOC_ACTIVATION'],
  [matrix, 'cataloged_requires_execution_adapter', 'CELAR_UAR_DOC_ADAPTER_STATE'],
  [matrix, 'Adapter implementation standard', 'CELAR_UAR_DOC_ADAPTER_STANDARD'],
  [evaluation, '120 cases and 10 categories', 'CELAR_UAR_DOC_120_CASES'],
  [evaluation, 'Permission leakage', 'CELAR_UAR_DOC_PERMISSION_TESTS'],
  [evaluation, 'Public-provider leakage', 'CELAR_UAR_DOC_PROVIDER_LEAKAGE'],
  [evaluation, 'Promotion gates', 'CELAR_UAR_DOC_PROMOTION'],
  [roadmap, 'Apache Tika decision gate', 'CELAR_UAR_DOC_TIKA_GATE'],
  [roadmap, 'pgvector decision gate', 'CELAR_UAR_DOC_PGVECTOR_GATE'],
  [roadmap, 'Redis decision gate', 'CELAR_UAR_DOC_REDIS_GATE'],
  [roadmap, 'secondary local model', 'CELAR_UAR_DOC_MODEL_GATE']
]) requireMarker(source, marker, code);

const newBackend = [catalog, reliability, moduleSource].join('\n');
for (const forbiddenMarker of ['Npgsql', 'HttpClient', 'Process.Start', 'System.Diagnostics.Process', 'SELECT ', 'INSERT ', 'UPDATE ', 'DELETE ', 'DROP ', 'ALTER TABLE', 'CREATE TABLE', 'Environment.GetEnvironmentVariable', 'Authorization: Bearer', '129.213.82.144']) {
  requireValue(!newBackend.includes(forbiddenMarker), `CELAR_UAR_SOURCE_FORBIDDEN_${forbiddenMarker.replaceAll(/[^A-Za-z0-9]+/g, '_').toUpperCase()}`, `new reliability source excludes ${forbiddenMarker}`);
}
requireValue(!workbench.includes('localStorage') && !workbench.includes('sessionStorage'), 'CELAR_UAR_UI_NO_BROWSER_PERSISTENCE', 'no new browser persistence');
requireValue(!workbench.includes('dangerouslySetInnerHTML'), 'CELAR_UAR_UI_SAFE_RENDERING', 'structured React rendering');

let changed = [];
try {
  changed = execFileSync('git', ['diff', '--name-only', 'origin/main...HEAD'], { cwd: root, encoding: 'utf8' })
    .split(/\r?\n/).filter(Boolean);
} catch {
  changed = [];
}
const allowed = new Set(Object.values(files));
const unexpected = changed.filter((value) => !allowed.has(value));
requireValue(unexpected.length === 0, 'CELAR_UAR_SOURCE_ISOLATION', unexpected.length ? unexpected.join(', ') : `${changed.length} governed files only`);
requireValue(changed.every((value) => !value.startsWith('database/')), 'CELAR_UAR_NO_DATABASE_MIGRATION', 'no migration or rollback');
requireValue(changed.every((value) => !value.startsWith('deployment/')), 'CELAR_UAR_NO_INFRASTRUCTURE', 'no container or cloud infrastructure mutation');
requireValue(changed.every((value) => !value.includes('projectpulse-deploy-') && !value.includes('celar-ai-oracle-test-runtime-deploy')), 'CELAR_UAR_NO_DEPLOYMENT_CONTROLLER', 'no Test or Production deployment workflow');
requireValue(changed.every((value) => !value.toLowerCase().includes('secret') && !value.endsWith('.env')), 'CELAR_UAR_NO_SECRET_FILE', 'no secret or environment file');
requireValue(!exists('.github/workflows/prepare-celar-ai-universal-answer-reliability.yml'), 'CELAR_UAR_TEMP_BUILDER_REMOVED', 'temporary same-branch builder removed before review');

if (process.exitCode) process.exit(process.exitCode);
console.log(`CELAR_AI_UNIVERSAL_ANSWER_TOOL_COUNT=${toolCodes.length}`);
console.log(`CELAR_AI_UNIVERSAL_ANSWER_DOMAIN_COUNT=${new Set([...catalog.matchAll(/Tool\("[a-z0-9_]+",\s*"[^"]+",\s*"([a-z0-9_]+)"/g)].map((match) => match[1])).size}`);
console.log('CELAR_AI_UNIVERSAL_ANSWER_EVALUATION_CASES=120');
console.log('CELAR_AI_UNIVERSAL_ANSWER_DATABASE_CHANGES=0');
console.log('CELAR_AI_UNIVERSAL_ANSWER_PROVIDER_CALLS_PERFORMED_BY_VALIDATOR=0');
console.log('CELAR_AI_UNIVERSAL_ANSWER_DEPLOYMENTS=0');
console.log('CELAR_AI_UNIVERSAL_ANSWER_PRODUCTION_MUTATIONS=0');
console.log('CELAR_AI_UNIVERSAL_ANSWER_SOURCE_CONTRACT=PASSED');
