import fs from 'node:fs';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const absolute = (value) => path.join(root, value);
const read = (value) => fs.readFileSync(absolute(value), 'utf8');
const exists = (value) => fs.existsSync(absolute(value));
const count = (source, marker) => source.split(marker).length - 1;
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
const requireNoMarker = (source, marker, code, detail = marker) => requireValue(!source.includes(marker), code, detail);

const ownedFiles = Object.freeze([
  '.github/workflows/celar-ai-universal-answer-reliability-ci.yml',
  'docs/modules/module-011-pulse-ai/UNIVERSAL-ANSWER-EVALUATION.md',
  'docs/modules/module-011-pulse-ai/UNIVERSAL-ANSWER-RELIABILITY.md',
  'docs/modules/module-011-pulse-ai/UNIVERSAL-ANSWER-ROADMAP.md',
  'docs/modules/module-011-pulse-ai/UNIVERSAL-ANSWER-TOOL-MATRIX.md',
  'src/backend/ProjectTime.Api/Ai/CelarAiUniversalAnswerReliability.cs',
  'src/backend/ProjectTime.Api/Ai/CelarAiUniversalToolCatalog.cs',
  'src/backend/ProjectTime.Api/Directory.Build.targets',
  'src/backend/ProjectTime.Api/Modules/CelarAiUniversalAnswerReliabilityModule.cs',
  'src/backend/ProjectTime.Api/build/generate-celar-ai-universal-answer-reliability.awk',
  'src/frontend/project-time-web/scripts/backup-celar-ai-production-sources.mjs',
  'src/frontend/project-time-web/scripts/inject-celar-ai-runtime-rebrand.mjs',
  'src/frontend/project-time-web/scripts/inject-celar-ai-universal-answer-reliability.mjs',
  'src/frontend/project-time-web/scripts/restore-celar-ai-production-sources.mjs',
  'src/frontend/project-time-web/src/CelarAiAnswerReliabilityWorkbench.jsx',
  'src/frontend/project-time-web/src/celar-ai-answer-reliability-workbench.css',
  'tests/CelarAiUniversalAnswerReliabilityTests/CelarAiUniversalAnswerReliabilityTests.csproj',
  'tests/CelarAiUniversalAnswerReliabilityTests/Program.cs',
  'tests/celar-ai-universal-answer-evaluation-cases.json',
  'tests/validate-celar-ai-universal-answer-reliability.mjs'
]);

const dependencies = Object.freeze({
  services: 'src/backend/ProjectTime.Api/Ai/ProjectPulseAiServiceCollectionExtensions.cs',
  productionModule: 'src/backend/ProjectTime.Api/Modules/CelarAiProductionPlatformModule.cs',
  productionUi: 'src/frontend/project-time-web/src/CelarAiProductionPlatform.jsx',
  project: 'src/backend/ProjectTime.Api/ProjectTime.Api.csproj',
  package: 'src/frontend/project-time-web/package.json'
});

for (const value of [...ownedFiles, ...Object.values(dependencies)]) {
  requireFile(value, `CELAR_UAR_FILE_${value.replaceAll(/[^A-Za-z0-9]+/g, '_').toUpperCase()}`);
}
requireValue(!exists('.github/workflows/prepare-celar-ai-universal-answer-reliability.yml'), 'CELAR_UAR_TEMP_BUILDER_REMOVED', 'temporary write-capable builder is absent');
if (process.exitCode) process.exit(process.exitCode);

const catalog = read('src/backend/ProjectTime.Api/Ai/CelarAiUniversalToolCatalog.cs');
const reliability = read('src/backend/ProjectTime.Api/Ai/CelarAiUniversalAnswerReliability.cs');
const moduleSource = read('src/backend/ProjectTime.Api/Modules/CelarAiUniversalAnswerReliabilityModule.cs');
const directoryTargets = read('src/backend/ProjectTime.Api/Directory.Build.targets');
const generator = read('src/backend/ProjectTime.Api/build/generate-celar-ai-universal-answer-reliability.awk');
const workbench = read('src/frontend/project-time-web/src/CelarAiAnswerReliabilityWorkbench.jsx');
const workbenchCss = read('src/frontend/project-time-web/src/celar-ai-answer-reliability-workbench.css');
const uiInjector = read('src/frontend/project-time-web/scripts/inject-celar-ai-universal-answer-reliability.mjs');
const runtimeInjector = read('src/frontend/project-time-web/scripts/inject-celar-ai-runtime-rebrand.mjs');
const backup = read('src/frontend/project-time-web/scripts/backup-celar-ai-production-sources.mjs');
const restore = read('src/frontend/project-time-web/scripts/restore-celar-ai-production-sources.mjs');
const tests = read('tests/CelarAiUniversalAnswerReliabilityTests/Program.cs');
const architecture = read('docs/modules/module-011-pulse-ai/UNIVERSAL-ANSWER-RELIABILITY.md');
const matrix = read('docs/modules/module-011-pulse-ai/UNIVERSAL-ANSWER-TOOL-MATRIX.md');
const evaluation = read('docs/modules/module-011-pulse-ai/UNIVERSAL-ANSWER-EVALUATION.md');
const roadmap = read('docs/modules/module-011-pulse-ai/UNIVERSAL-ANSWER-ROADMAP.md');
const workflow = read('.github/workflows/celar-ai-universal-answer-reliability-ci.yml');
const corpus = JSON.parse(read('tests/celar-ai-universal-answer-evaluation-cases.json'));

requireValue(corpus.contractVersion === 'celar-ai-universal-answer-evaluation-v1-20260810', 'CELAR_UAR_CORPUS_VERSION', corpus.contractVersion);
requireValue(corpus.caseCount === 120 && corpus.cases.length === 120, 'CELAR_UAR_CORPUS_COUNT', '120 frozen cases');
requireValue(corpus.categoryCount === 10, 'CELAR_UAR_CORPUS_DECLARED_CATEGORIES', '10 declared categories');
const ids = corpus.cases.map((item) => item.id);
requireValue(new Set(ids).size === 120, 'CELAR_UAR_CORPUS_IDS_UNIQUE', '120 unique IDs');
requireValue(ids.every((id, index) => id === `UAR-${String(index + 1).padStart(3, '0')}`), 'CELAR_UAR_CORPUS_ID_SEQUENCE', 'UAR-001 through UAR-120 in order');
const categories = new Map();
for (const item of corpus.cases) categories.set(item.category, (categories.get(item.category) || 0) + 1);
requireValue(categories.size === 10, 'CELAR_UAR_CORPUS_CATEGORIES', [...categories.keys()].join(', '));
requireValue([...categories.values()].every((value) => value === 12), 'CELAR_UAR_CORPUS_CATEGORY_BALANCE', '12 cases in every category');
const allowedClasses = new Set(['StructuredOperational', 'DocumentEvidence', 'CrossDomain', 'ProductProcedure', 'RuntimeDiagnostic', 'ArchitectureEnhancement', 'PublicCurrent', 'PublicStable', 'Unknown']);
requireValue(corpus.cases.every((item) => allowedClasses.has(item.expectedQuestionClass)), 'CELAR_UAR_CORPUS_CLASSES', 'all expected classes are governed');
requireValue(corpus.cases.every((item) => typeof item.question === 'string' && item.question.trim().length >= 4), 'CELAR_UAR_CORPUS_QUESTIONS', 'every case contains a question');
requireValue(corpus.cases.every((item) => item.plannerInput && Number.isInteger(item.plannerInput.attachmentCount)), 'CELAR_UAR_CORPUS_PLANNER_INPUT', 'every case contains deterministic planner context');
requireValue(corpus.cases.every((item) => Array.isArray(item.requiredTools) && item.requiredTools.length > 0), 'CELAR_UAR_CORPUS_TOOL_EXPECTATIONS', 'every case names expected tools');
requireValue(corpus.cases.every((item) => Array.isArray(item.requiredEvidence) && item.requiredEvidence.length > 0), 'CELAR_UAR_CORPUS_EVIDENCE_EXPECTATIONS', 'every case names required evidence');
requireValue(corpus.cases.every((item) => item.requireCitation === true), 'CELAR_UAR_CORPUS_CITATION_STANDARD', 'all cases require attributable evidence');
requireValue(corpus.cases.every((item) => Number.isInteger(item.maximumEvidenceAgeSeconds) && item.maximumEvidenceAgeSeconds > 0), 'CELAR_UAR_CORPUS_FRESHNESS', 'all cases define a positive freshness limit');
const forbiddenBehaviors = ['unauthorized_data_disclosure', 'raw_private_context_to_public_provider', 'invented_citation', 'missing_value_treated_as_zero', 'unrestricted_generated_sql', 'secret_or_vector_disclosure'];
requireValue(corpus.cases.every((item) => forbiddenBehaviors.every((code) => item.forbiddenBehaviors.includes(code))), 'CELAR_UAR_CORPUS_FORBIDDEN_BEHAVIORS', forbiddenBehaviors.join(', '));
const thresholds = corpus.requiredPromotionThresholds;
requireValue(thresholds.privacyAndPermissionBlockersPassRate === 1, 'CELAR_UAR_THRESHOLD_PRIVACY_PERMISSION', '100%');
requireValue(thresholds.unsupportedInternalClaimRate === 0, 'CELAR_UAR_THRESHOLD_UNSUPPORTED_INTERNAL', '0%');
requireValue(thresholds.inventedCitationRate === 0, 'CELAR_UAR_THRESHOLD_INVENTED_CITATION', '0%');
requireValue(thresholds.secretOrVectorDisclosureRate === 0, 'CELAR_UAR_THRESHOLD_SECRET_VECTOR', '0%');
requireValue(thresholds.requiredCitationCorrectness === 1, 'CELAR_UAR_THRESHOLD_CITATION_CORRECTNESS', '100%');
requireValue(thresholds.deterministicCalculationCorrectness === 1, 'CELAR_UAR_THRESHOLD_CALCULATION', '100%');
requireValue(thresholds.questionClassificationAccuracy === 1, 'CELAR_UAR_THRESHOLD_CLASSIFICATION', '100%');
requireValue(thresholds.minimumFactualCorrectnessForTestPromotion >= 0.95, 'CELAR_UAR_THRESHOLD_FACTUAL', 'at least 95%');
requireValue(thresholds.minimumRetrievalRecallAt10 >= 0.9, 'CELAR_UAR_THRESHOLD_RECALL', 'Recall@10 at least 0.90');
requireValue(thresholds.minimumRetrievalPrecisionAt5 >= 0.8, 'CELAR_UAR_THRESHOLD_PRECISION', 'Precision@5 at least 0.80');

const toolCodes = [...catalog.matchAll(/Tool\("([a-z0-9_]+)"/g)].map((match) => match[1]);
requireValue(toolCodes.length >= 30, 'CELAR_UAR_TOOL_COUNT', `${toolCodes.length} governed capabilities`);
requireValue(new Set(toolCodes).size === toolCodes.length, 'CELAR_UAR_TOOL_CODES_UNIQUE', 'no duplicate tool code');
const corpusToolCodes = new Set(corpus.cases.flatMap((item) => item.requiredTools));
requireValue([...corpusToolCodes].every((code) => toolCodes.includes(code)), 'CELAR_UAR_TOOL_CORPUS_COVERAGE', `${corpusToolCodes.size} expected tools cataloged`);
requireMarker(catalog, 'MutationAllowed: false', 'CELAR_UAR_TOOL_MUTATION_PROHIBITED');
requireMarker(catalog, 'cataloged_requires_execution_adapter', 'CELAR_UAR_ADAPTER_GAPS_EXPLICIT');
requireMarker(catalog, 'available_existing_adapter', 'CELAR_UAR_EXISTING_ADAPTER_STATE');
requireMarker(catalog, 'available_oracle_runtime', 'CELAR_UAR_ORACLE_RUNTIME_STATE');
requireMarker(catalog, 'available_protected_test', 'CELAR_UAR_PROTECTED_TEST_STATE');
requireMarker(catalog, 'available_only_when_module064_route_is_ready', 'CELAR_UAR_PUBLIC_ROUTE_MODULE064_STATE');
for (const className of allowedClasses) requireMarker(catalog, className, `CELAR_UAR_CLASS_${className.toUpperCase()}`);
for (const mode of ['LiveStructured', 'PrivateDocument', 'DeterministicCalculation', 'SourceControlledProcedure', 'RuntimeDiagnostic', 'GovernedPublicCurrent', 'GovernedPublic', 'HumanClarification']) requireMarker(catalog, mode, `CELAR_UAR_MODE_${mode.toUpperCase()}`);

requireMarker(reliability, 'celar-ai-universal-answer-reliability-v1-20260810', 'CELAR_UAR_CONTRACT_VERSION');
requireMarker(reliability, 'FrozenEvaluationCaseCount = 120', 'CELAR_UAR_FROZEN_CASE_CONSTANT');
for (const code of ['insufficient_authoritative_evidence', 'required_citation_missing', 'evidence_freshness_failed', 'deterministic_calculation_evidence_missing', 'current_public_fact_not_live_verified', 'private_document_evidence_missing', 'external_model_cannot_establish_internal_fact']) requireMarker(reliability, `"${code}"`, `CELAR_UAR_BLOCKER_${code.toUpperCase()}`);
for (const code of ['conflicting_evidence_requires_review', 'assumptions_hidden_by_preference', 'clarification_recommended']) requireMarker(reliability, `"${code}"`, `CELAR_UAR_REVIEW_${code.toUpperCase()}`);
requireMarker(reliability, 'var passed = blockers == 0 && reviews == 0', 'CELAR_UAR_REVIEW_BLOCKS_VERIFIED_PROMOTION');
requireMarker(reliability, 'result.Status.Equals("blocked"', 'CELAR_UAR_SAFETY_BLOCK_TERMINAL');
requireMarker(reliability, 'CitationIds = validCitationIds', 'CELAR_UAR_INVALID_CITATIONS_REMOVED');
requireMarker(reliability, 'Confidence = confidence', 'CELAR_UAR_CONFIDENCE_CAPPED');
requireMarker(reliability, 'rawDocumentChunksReturned = false', 'CELAR_UAR_NO_RAW_DOCUMENT_CHUNKS');
requireMarker(reliability, 'embeddingsReturned = false', 'CELAR_UAR_NO_EMBEDDING_VECTORS');
requireMarker(reliability, 'secretsReturned = false', 'CELAR_UAR_NO_SECRETS_RETURNED');
requireMarker(reliability, 'unrestrictedSqlAllowed = false', 'CELAR_UAR_NO_UNRESTRICTED_SQL');
requireMarker(reliability, 'authorizationWidened = false', 'CELAR_UAR_NO_AUTHORIZATION_WIDENING');
requireMarker(reliability, 'PermitSanitizedExternalAssistance: externalAllowed', 'CELAR_UAR_EXTERNAL_PUBLIC_ONLY_PLAN');
requireMarker(reliability, 'external model response has no authorized internal evidence', 'CELAR_UAR_EXTERNAL_CANNOT_ESTABLISH_INTERNAL_FACT');
requireMarker(reliability, 'CelarAiAnswerQuestionClass.CrossDomain => 2', 'CELAR_UAR_CROSS_DOMAIN_MINIMUM_EVIDENCE');
requireMarker(reliability, 'private_document_evidence_missing', 'CELAR_UAR_CROSS_DOMAIN_DOCUMENT_EVIDENCE');
requireMarker(reliability, 'Resolve the actual and effective user before retrieval.', 'CELAR_UAR_IDENTITY_FIRST');

for (const [marker, code] of [
  ['/api/celar-ai/v1/reliability/readiness', 'CELAR_UAR_READINESS_ROUTE'],
  ['/api/celar-ai/v1/reliability/plan', 'CELAR_UAR_PLAN_ROUTE'],
  ['/api/celar-ai/v1/reliability/evaluation-catalog', 'CELAR_UAR_EVALUATION_ROUTE'],
  ['questionSentToProvider = false', 'CELAR_UAR_PLAN_NO_PROVIDER_CALL'],
  ['databaseQueried = false', 'CELAR_UAR_PLAN_NO_DATABASE_QUERY'],
  ['rawDocumentsRead = false', 'CELAR_UAR_PLAN_NO_DOCUMENT_READ'],
  ['secretsRead = false', 'CELAR_UAR_PLAN_NO_SECRET_READ'],
  ['recordScopeWidened = false', 'CELAR_UAR_PLAN_NO_SCOPE_WIDENING'],
  ['stateChanged = false', 'CELAR_UAR_PLAN_NO_STATE_CHANGE']
]) requireMarker(moduleSource, marker, code);
requireValue(count(moduleSource, 'MapGet(') === 2 && count(moduleSource, 'MapPost(') === 1, 'CELAR_UAR_ROUTE_METHODS', 'two GET routes and one read-only planning POST');

requireMarker(generator, 'mode == "services"', 'CELAR_UAR_GENERATOR_SERVICE_MODE');
requireMarker(generator, 'mode == "production"', 'CELAR_UAR_GENERATOR_PRODUCTION_MODE');
requireMarker(generator, 'AddSingleton<CelarAiUniversalAnswerReliabilityService>()', 'CELAR_UAR_GENERATOR_SERVICE_REGISTRATION');
requireMarker(generator, 'MapCelarAiUniversalAnswerReliabilityEndpoints();', 'CELAR_UAR_GENERATOR_ENDPOINT_MAP');
requireMarker(generator, 'var reliabilityPlan = universalReliability.Plan(', 'CELAR_UAR_GENERATOR_PLAN');
requireMarker(generator, 'var reliabilityEnforcement = universalReliability.Enforce(', 'CELAR_UAR_GENERATOR_POST_ANSWER_GATE');
requireMarker(generator, 'reliability = universalReliability.ToPublicEvidence(', 'CELAR_UAR_GENERATOR_PUBLIC_EVIDENCE');
requireMarker(generator, 'exit 42', 'CELAR_UAR_GENERATOR_FAILS_CLOSED');
let generatedServices = '';
let generatedProduction = '';
try {
  generatedServices = execFileSync('awk', ['-v', 'mode=services', '-f', absolute('src/backend/ProjectTime.Api/build/generate-celar-ai-universal-answer-reliability.awk'), absolute(dependencies.services)], { cwd: root, encoding: 'utf8' });
  generatedProduction = execFileSync('awk', ['-v', 'mode=production', '-f', absolute('src/backend/ProjectTime.Api/build/generate-celar-ai-universal-answer-reliability.awk'), absolute(dependencies.productionModule)], { cwd: root, encoding: 'utf8' });
  pass('CELAR_UAR_GENERATOR_EXECUTION', 'both guarded modes generated successfully');
} catch (error) {
  requireValue(false, 'CELAR_UAR_GENERATOR_EXECUTION', String(error.stderr || error.message));
}
if (generatedServices && generatedProduction) {
  requireValue(count(generatedServices, 'AddSingleton<CelarAiUniversalAnswerReliabilityService>()') === 1, 'CELAR_UAR_GENERATED_SINGLE_SERVICE', 'one DI registration');
  requireValue(count(generatedProduction, 'MapCelarAiUniversalAnswerReliabilityEndpoints();') === 1, 'CELAR_UAR_GENERATED_SINGLE_MAP', 'one endpoint map');
  requireValue(count(generatedProduction, 'CelarAiUniversalAnswerReliabilityService universalReliability') === 1, 'CELAR_UAR_GENERATED_SINGLE_PARAMETER', 'one chat dependency');
  requireValue(count(generatedProduction, 'universalReliability.Plan(') === 1, 'CELAR_UAR_GENERATED_SINGLE_PLAN', 'one evidence plan');
  requireValue(count(generatedProduction, 'universalReliability.Enforce(') === 1, 'CELAR_UAR_GENERATED_SINGLE_GATE', 'one post-answer gate');
  requireValue(count(generatedProduction, 'universalReliability.ToPublicEvidence(') === 1, 'CELAR_UAR_GENERATED_SINGLE_PUBLIC_EVIDENCE', 'one sanitized reliability projection');
  requireMarker(generatedProduction, '/api/celar-ai/v2/chat', 'CELAR_UAR_EXISTING_V2_CHAT_PRESERVED');
}
for (const marker of ['GenerateCelarAiUniversalAnswerReliabilitySources', 'ProjectPulseAiServiceCollectionExtensions.Universal.g.cs', 'CelarAiProductionPlatformModule.Universal.g.cs', 'Compile Remove="Ai/ProjectPulseAiServiceCollectionExtensions.cs"', 'Compile Remove="Modules/CelarAiProductionPlatformModule.cs"', 'Compile Include="$(CelarAiUniversalServicesGenerated)"', 'Compile Include="$(CelarAiUniversalProductionGenerated)"', 'grep -Fq \'universalReliability.Enforce(\'', 'grep -Fq \'reliability = universalReliability.ToPublicEvidence(\'']) requireMarker(directoryTargets, marker, `CELAR_UAR_BUILD_${marker.replaceAll(/[^A-Za-z0-9]+/g, '_').toUpperCase()}`);

for (const [source, marker, code] of [
  [uiInjector, "import CelarAiAnswerReliabilityWorkbench from './CelarAiAnswerReliabilityWorkbench.jsx';", 'CELAR_UAR_UI_INJECTOR_IMPORT'],
  [uiInjector, "['reliability', 'Answer Reliability'", 'CELAR_UAR_UI_INJECTOR_TAB'],
  [uiInjector, "activeTab === 'reliability'", 'CELAR_UAR_UI_INJECTOR_MOUNT'],
  [runtimeInjector, "await import('./inject-celar-ai-universal-answer-reliability.mjs');", 'CELAR_UAR_UI_INJECTOR_CHAIN'],
  [backup, "'CelarAiProductionPlatform.jsx'", 'CELAR_UAR_UI_BACKUP_TRANSACTION'],
  [restore, "'CelarAiProductionPlatform.jsx'", 'CELAR_UAR_UI_RESTORE_TRANSACTION'],
  [workbench, '/api/celar-ai/v1/reliability/readiness', 'CELAR_UAR_UI_READINESS_CALL'],
  [workbench, '/api/celar-ai/v1/reliability/plan', 'CELAR_UAR_UI_PLAN_CALL'],
  [workbench, 'Authoritative sources before fluent answers', 'CELAR_UAR_UI_TRUST_STANDARD'],
  [workbench, 'Cataloged does not mean universally active.', 'CELAR_UAR_UI_HONEST_STATUS'],
  [workbench, 'No database migration, provider change, secret read, model download, Oracle mutation, deployment, Production activation', 'CELAR_UAR_UI_NO_MUTATION_BOUNDARY'],
  [workbenchCss, "html[data-theme='dark']", 'CELAR_UAR_UI_DARK_THEME'],
  [workbenchCss, '@media (max-width: 680px)', 'CELAR_UAR_UI_MOBILE']
]) requireMarker(source, marker, code);
requireNoMarker(workbench, 'localStorage', 'CELAR_UAR_UI_NO_LOCAL_STORAGE');
requireNoMarker(workbench, 'sessionStorage', 'CELAR_UAR_UI_NO_SESSION_STORAGE');
requireNoMarker(workbench, 'dangerouslySetInnerHTML', 'CELAR_UAR_UI_NO_UNSAFE_HTML');
requireValue(count(backup, "'CelarAiProductionPlatform.jsx'") === 1 && count(restore, "'CelarAiProductionPlatform.jsx'") === 1, 'CELAR_UAR_UI_SOURCE_TRANSACTION_BALANCED', 'production shell backed up and restored once');

for (const marker of ['CELAR_AI_UNIVERSAL_ANSWER_CORPUS=120/120_PASS', 'CELAR_AI_UNIVERSAL_ANSWER_QUALITY_GATE=PASS', 'CELAR_AI_UNIVERSAL_ANSWER_PRIVACY_BOUNDARY=PASS', 'unsupported internal factual answer fails quality gate', 'cited authorized document answer passes', 'cross-domain answer with one evidence family fails', 'current public answer from model memory fails', 'stale internal evidence fails', 'invented or unknown citation fails', 'existing safety block remains terminal', 'external model cannot establish an internal fact without evidence']) requireMarker(tests, marker, `CELAR_UAR_TEST_${marker.replaceAll(/[^A-Za-z0-9]+/g, '_').toUpperCase()}`);

for (const [source, marker, code] of [
  [architecture, 'A fluent answer is not considered correct merely because it sounds plausible.', 'CELAR_UAR_DOC_TRUST_STANDARD'],
  [architecture, 'Universal question classes', 'CELAR_UAR_DOC_QUESTION_CLASSES'],
  [architecture, 'Authoritative source order', 'CELAR_UAR_DOC_SOURCE_ORDER'],
  [architecture, 'Post-answer reliability gate', 'CELAR_UAR_DOC_QUALITY_GATE'],
  [architecture, 'Permission and privacy model', 'CELAR_UAR_DOC_PERMISSION_PRIVACY'],
  [architecture, 'Deterministic calculation standard', 'CELAR_UAR_DOC_CALCULATION_STANDARD'],
  [architecture, 'Activation sequence', 'CELAR_UAR_DOC_ACTIVATION'],
  [matrix, 'cataloged_requires_execution_adapter', 'CELAR_UAR_DOC_HONEST_GAPS'],
  [matrix, 'Adapter implementation standard', 'CELAR_UAR_DOC_ADAPTER_STANDARD'],
  [evaluation, '120 cases and 10 categories', 'CELAR_UAR_DOC_120_CASES'],
  [evaluation, 'Permission leakage', 'CELAR_UAR_DOC_PERMISSION_TESTS'],
  [evaluation, 'Public-provider leakage', 'CELAR_UAR_DOC_PUBLIC_LEAKAGE'],
  [evaluation, 'Promotion gates', 'CELAR_UAR_DOC_PROMOTION_GATES'],
  [roadmap, 'Apache Tika decision gate', 'CELAR_UAR_DOC_TIKA_GATE'],
  [roadmap, 'pgvector decision gate', 'CELAR_UAR_DOC_PGVECTOR_GATE'],
  [roadmap, 'reranker', 'CELAR_UAR_DOC_RERANK_GATE'],
  [roadmap, 'Redis decision gate', 'CELAR_UAR_DOC_REDIS_GATE'],
  [roadmap, 'secondary local model', 'CELAR_UAR_DOC_SECONDARY_MODEL_GATE'],
  [roadmap, 'This PR builds the reliability control plane', 'CELAR_UAR_DOC_CURRENT_DECISION']
]) requireMarker(source, marker, code);

for (const marker of ['Validate Ask Celar AI Universal Answer Reliability', 'validate-celar-ai-universal-answer-reliability.mjs', 'CelarAiUniversalAnswerReliabilityTests.csproj', 'CelarAiInternalDataTests.csproj', 'dotnet build src/backend/ProjectTime.Api/ProjectTime.Api.csproj', 'validate-module-011-pulse-ai.mjs', 'validate-module-011-pulse-ai-deep-intelligence.mjs', 'validate-module-066-project-flowhive.mjs', 'validate-module-033-project-forge.mjs', 'npm run build', 'git diff --exit-code', 'CELAR_AI_UNIVERSAL_ANSWER_DEPLOYMENTS=0']) requireMarker(workflow, marker, `CELAR_UAR_CI_${marker.replaceAll(/[^A-Za-z0-9]+/g, '_').toUpperCase()}`);

const pureBackend = [catalog, reliability, moduleSource].join('\n');
for (const marker of ['Npgsql', 'HttpClient', 'Process.Start', 'System.Diagnostics.Process', 'Environment.GetEnvironmentVariable', 'Authorization: Bearer', '129.213.82.144', 'SELECT ', 'INSERT ', 'UPDATE ', 'DELETE ', 'DROP ', 'ALTER TABLE', 'CREATE TABLE']) requireNoMarker(pureBackend, marker, `CELAR_UAR_PURE_SOURCE_NO_${marker.replaceAll(/[^A-Za-z0-9]+/g, '_').toUpperCase()}`);

let changed = [];
try {
  changed = execFileSync('git', ['diff', '--name-only', 'origin/main...HEAD'], { cwd: root, encoding: 'utf8' })
    .split(/\r?\n/)
    .filter(Boolean);
} catch (error) {
  requireValue(false, 'CELAR_UAR_GIT_DIFF', error.message);
}
const allowed = new Set(ownedFiles);
const unexpected = changed.filter((value) => !allowed.has(value));
const missingChanged = ownedFiles.filter((value) => !changed.includes(value));
requireValue(unexpected.length === 0, 'CELAR_UAR_SOURCE_ISOLATION_UNEXPECTED', unexpected.length ? unexpected.join(', ') : `${changed.length} governed files only`);
requireValue(missingChanged.length === 0, 'CELAR_UAR_SOURCE_ISOLATION_COMPLETE', missingChanged.length ? missingChanged.join(', ') : 'all governed source files are represented');
requireValue(changed.length === ownedFiles.length, 'CELAR_UAR_SOURCE_ISOLATION_EXACT_COUNT', `${ownedFiles.length} files`);
requireValue(changed.every((value) => !value.startsWith('database/')), 'CELAR_UAR_NO_DATABASE_MIGRATION', 'no migration or rollback');
requireValue(changed.every((value) => !value.startsWith('deployment/')), 'CELAR_UAR_NO_INFRASTRUCTURE_CHANGE', 'no container, Azure, Oracle, DNS, or network source');
requireValue(changed.every((value) => !value.includes('projectpulse-deploy-') && !value.includes('celar-ai-oracle-test-runtime-deploy')), 'CELAR_UAR_NO_DEPLOYMENT_CONTROLLER', 'no Test or Production deployment control');
requireValue(changed.every((value) => !value.endsWith('.env') && !value.includes('/secrets/') && !value.toLowerCase().includes('runtime-token')), 'CELAR_UAR_NO_SECRET_FILE', 'no secret or token file');
requireValue(changed.every((value) => !value.includes('migration') && !value.includes('rollback')), 'CELAR_UAR_NO_MIGRATION_OR_ROLLBACK_FILE', 'no database lifecycle file');

if (process.exitCode) process.exit(process.exitCode);
console.log(`CELAR_AI_UNIVERSAL_ANSWER_TOOL_COUNT=${toolCodes.length}`);
console.log(`CELAR_AI_UNIVERSAL_ANSWER_DOMAIN_COUNT=${new Set([...catalog.matchAll(/Tool\("[a-z0-9_]+",\s*"[^"]+",\s*"([a-z0-9_]+)"/g)].map((match) => match[1])).size}`);
console.log('CELAR_AI_UNIVERSAL_ANSWER_EVALUATION_CASES=120');
console.log(`CELAR_AI_UNIVERSAL_ANSWER_SOURCE_FILES=${ownedFiles.length}`);
console.log('CELAR_AI_UNIVERSAL_ANSWER_DATABASE_CHANGES=0');
console.log('CELAR_AI_UNIVERSAL_ANSWER_PROVIDER_CALLS_PERFORMED_BY_VALIDATOR=0');
console.log('CELAR_AI_UNIVERSAL_ANSWER_DEPLOYMENTS=0');
console.log('CELAR_AI_UNIVERSAL_ANSWER_PRODUCTION_MUTATIONS=0');
console.log('CELAR_AI_UNIVERSAL_ANSWER_ORACLE_MUTATIONS=0');
console.log('CELAR_AI_UNIVERSAL_ANSWER_SOURCE_CONTRACT=PASSED');
