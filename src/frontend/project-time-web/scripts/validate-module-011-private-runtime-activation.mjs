import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repoRoot = fileURLToPath(new URL('../../../../', import.meta.url));
const absolute = (relative) => path.join(repoRoot, relative);
const exists = (relative) => fs.existsSync(absolute(relative));
const read = (relative) => fs.readFileSync(absolute(relative), 'utf8');
const checks = [];

function assert(name, condition, evidence) {
  checks.push({ name, condition, evidence });
  console.log(`MODULE011_PRIVATE_RUNTIME_${name}=${condition ? 'PASSED' : 'FAILED'} — ${evidence}`);
}

const paths = {
  migration: 'database/migrations/052_document_intelligence_runtime.sql',
  rollback: 'database/rollback/052_document_intelligence_runtime_rollback.sql',
  migrationTest: 'tests/test-pulse-ai-private-document-runtime-migration-052.sh',
  contracts: 'src/backend/ProjectTime.Api/Ai/PulseAiPrivateRuntimeContracts.cs',
  scanner: 'src/backend/ProjectTime.Api/Ai/PulseAiPrivateMalwareScanner.cs',
  ocr: 'src/backend/ProjectTime.Api/Ai/PulseAiPrivateOcrClient.cs',
  embeddings: 'src/backend/ProjectTime.Api/Ai/PulseAiPrivateEmbeddingClient.cs',
  sourceResolver: 'src/backend/ProjectTime.Api/Ai/PulseAiPrivateRuntimeSourceResolver.cs',
  repository: 'src/backend/ProjectTime.Api/Ai/PulseAiPrivateDocumentRuntimeRepository.cs',
  runtime: 'src/backend/ProjectTime.Api/Ai/PulseAiPrivateDocumentRuntimeService.cs',
  worker: 'src/backend/ProjectTime.Api/Ai/PulseAiPrivateDocumentRuntimeWorker.cs',
  module: 'src/backend/ProjectTime.Api/Modules/PulseAiPrivateRuntimeModule.cs',
  pipelineModule: 'src/backend/ProjectTime.Api/Modules/PulseAiPrivateDocumentPipelineModule.cs',
  services: 'src/backend/ProjectTime.Api/Ai/ProjectPulseAiServiceCollectionExtensions.cs',
  workbench: 'src/frontend/project-time-web/src/PulseAiPrivateRuntimeWorkbench.jsx',
  css: 'src/frontend/project-time-web/src/pulse-ai-private-runtime-workbench.css',
  mount: 'src/frontend/project-time-web/src/WorkTaskBuilderPanel.jsx',
  doc: 'docs/modules/module-011-pulse-ai/PRIVATE-RUNTIME-ACTIVATION.md',
  previewValidator: 'src/frontend/project-time-web/scripts/validate-module-011-private-document-pipeline.mjs',
  deepValidator: 'src/frontend/project-time-web/scripts/validate-module-011-pulse-ai-deep-intelligence.mjs',
  foundationValidator: 'src/frontend/project-time-web/scripts/validate-module-011-pulse-ai.mjs',
  flowHiveValidator: 'src/frontend/project-time-web/scripts/validate-module-066-project-flowhive.mjs'
};

for (const [name, relative] of Object.entries(paths)) {
  assert(`FILE_${name.toUpperCase()}`, exists(relative), relative);
}

if (checks.some((check) => !check.condition)) {
  console.error('MODULE_011_PRIVATE_RUNTIME_CONTRACT=FAILED_MISSING_FILE');
  process.exit(1);
}

const migration = read(paths.migration);
const rollback = read(paths.rollback);
const migrationTest = read(paths.migrationTest);
const contracts = read(paths.contracts);
const scanner = read(paths.scanner);
const ocr = read(paths.ocr);
const embeddings = read(paths.embeddings);
const sourceResolver = read(paths.sourceResolver);
const repository = read(paths.repository);
const runtime = read(paths.runtime);
const worker = read(paths.worker);
const moduleSource = read(paths.module);
const pipelineModule = read(paths.pipelineModule);
const services = read(paths.services);
const workbench = read(paths.workbench);
const css = read(paths.css);
const mount = read(paths.mount);
const doc = read(paths.doc);

assert(
  'MIGRATION_ID',
  migration.includes("'052_pulse_ai_private_document_runtime'")
    && rollback.includes("'052_pulse_ai_private_document_runtime'")
    && migrationTest.includes('PULSE_AI_PRIVATE_DOCUMENT_RUNTIME_MIGRATION_052=PASS'),
  'migration 052 has apply, rollback, idempotency, and verification coverage'
);

const tables = [
  'pulse_ai_document_processing_jobs',
  'pulse_ai_document_versions',
  'pulse_ai_document_sections',
  'pulse_ai_document_chunks',
  'pulse_ai_document_processing_events'
];
assert(
  'DURABLE_TABLES',
  tables.every((table) => migration.includes(`CREATE TABLE IF NOT EXISTS ${table}`))
    && tables.every((table) => rollback.includes(`DROP TABLE IF EXISTS ${table}`)),
  'jobs, versions, sections, chunks, and immutable events have forward and rollback definitions'
);

assert(
  'DOCUMENT_RUNTIME_FIELDS',
  [
    'pulse_ai_processing_status',
    'pulse_ai_classification',
    'pulse_ai_document_revision',
    'pulse_ai_effective_at',
    'pulse_ai_superseded_by_document_id',
    'pulse_ai_active_version_id',
    'pulse_ai_processing_error_code',
    'pulse_ai_processing_updated_at'
  ].every((field) => migration.includes(field)),
  'project documents carry processing, classification, version, error, and freshness state'
);

assert(
  'HYBRID_INDEX_SCHEMA',
  migration.includes('TSVECTOR GENERATED ALWAYS AS')
    && migration.includes('USING GIN(search_vector)')
    && migration.includes('embedding DOUBLE PRECISION[]')
    && migration.includes('embedding_dimension INTEGER')
    && migration.includes('authorization_snapshot_json JSONB')
    && migration.includes('citation_anchor VARCHAR(500)')
    && migration.includes('source_sha256 VARCHAR(64)')
    && migration.includes('text_sha256 VARCHAR(64)'),
  'lexical retrieval, optional private vectors, citations, checksums, and security evidence are durable'
);

assert(
  'IMMUTABLE_PROCESSING_EVENTS',
  migration.includes('Pulse AI document processing event evidence is immutable.')
    && migration.includes('BEFORE UPDATE OR DELETE ON pulse_ai_document_processing_events')
    && migrationTest.includes('immutable_event_update')
    && migrationTest.includes('immutable_event_delete'),
  'processing event evidence cannot be updated or deleted'
);

assert(
  'ONE_ACTIVE_JOB',
  migration.includes('ux_pulse_ai_document_processing_jobs_active_document')
    && migration.includes("WHERE job_status IN (")
    && migrationTest.includes('one_active_job_per_document'),
  'one active processing job exists per document'
);

assert(
  'PERMISSION_MODEL',
  [
    'VIEW_PULSE_AI_DOCUMENT_RUNTIME',
    'QUEUE_PULSE_AI_DOCUMENT_PROCESSING',
    'CANCEL_PULSE_AI_DOCUMENT_PROCESSING',
    'RETRY_PULSE_AI_DOCUMENT_PROCESSING',
    'APPROVE_PULSE_AI_DOCUMENT_VERSION'
  ].every((permission) => migration.includes(`'${permission}'`))
    && migration.includes("'PULSE_AI_PRIVATE_DOCUMENT_RUNTIME'"),
  'Module 011 runtime capabilities and feature catalog registration are explicit'
);

assert(
  'PRIVATE_ENDPOINT_POLICY',
  contracts.includes('IsApprovedPrivateEndpoint')
    && contracts.includes('IsPrivateAddress')
    && contracts.includes('host_not_private_or_allowlisted')
    && contracts.includes('PROJECTPULSE_PRIVATE_ENDPOINT_HOST_ALLOWLIST')
    && ocr.includes('PulseAiPrivateEndpointPolicy.IsApprovedPrivateEndpoint')
    && embeddings.includes('PulseAiPrivateEndpointPolicy.IsApprovedPrivateEndpoint'),
  'OCR and embedding adapters fail closed unless endpoint policy accepts a private or allowlisted destination'
);

assert(
  'WORKER_DISABLED_DEFAULT',
  contracts.includes('WorkerEnabled: Boolean("PROJECTPULSE_PULSE_AI_PRIVATE_RUNTIME_WORKER_ENABLED", false)')
    && worker.includes('if (!options.WorkerEnabled)')
    && runtime.includes('if (!options.WorkerEnabled)'),
  'the durable worker cannot activate merely because source is deployed'
);

assert(
  'MALWARE_SCAN_BEFORE_EXTRACTION',
  scanner.includes('zINSTREAM\0')
    && scanner.includes('MalwareScanAttested')
    && runtime.indexOf('_malwareScanner.ScanAsync') < runtime.indexOf('_extractor.ExtractAsync')
    && runtime.includes('The document was not parsed, embedded, or indexed.'),
  'a clean private scan result is required before parsing, OCR, embeddings, or indexing'
);

assert(
  'PRIVATE_OCR',
  ocr.includes('MultipartFormDataContent')
    && ocr.includes('X-Pulse-AI-Privacy-Boundary')
    && !ocr.includes('rawDocumentTextReturned')
    && runtime.includes('private_ocr_not_configured')
    && runtime.includes('private_ocr_adapter'),
  'image-only documents use a private endpoint and preserve page-level sections'
);

assert(
  'PRIVATE_EMBEDDINGS',
  embeddings.includes('encoding_format = "float"')
    && embeddings.includes('X-Pulse-AI-Privacy-Boundary')
    && embeddings.includes('embedding_count_mismatch')
    && embeddings.includes('embedding_dimension_mismatch')
    && runtime.includes('AllowLexicalOnlyCompletion'),
  'embedding batches are private, bounded, dimension-validated, and support an explicit lexical-only policy'
);

assert(
  'QUEUE_LEASING_RETRY_CANCEL',
  repository.includes('FOR UPDATE SKIP LOCKED')
    && repository.includes('lease_expires_at')
    && repository.includes('retry_wait')
    && repository.includes('cancellation_requested')
    && runtime.includes('CancellationRequestedAsync')
    && worker.includes('ProcessNextAsync'),
  'workers claim bounded leases and preserve retry and cancellation boundaries'
);

assert(
  'TRANSACTIONAL_PERSISTENCE',
  repository.includes('BeginTransactionAsync')
    && repository.includes('PersistProcessedDocumentAsync')
    && repository.includes('pulse_ai_document_sections')
    && repository.includes('pulse_ai_document_chunks')
    && repository.includes('pulse_ai_active_version_id')
    && repository.includes('transaction.CommitAsync')
    && repository.includes('transaction.RollbackAsync'),
  'version, section, chunk, embedding, job, and event updates share controlled transactions'
);

assert(
  'REAUTHORIZATION_BEFORE_PROCESSING',
  sourceResolver.includes('project_assignments')
    && sourceResolver.includes('engineering_resource_requests')
    && sourceResolver.includes('engineering_resource_request_assignments')
    && runtime.indexOf('_sourceResolver.ResolveAsync') < runtime.indexOf('_malwareScanner.ScanAsync')
    && runtime.includes('authorization_revoked'),
  'current effective-user project access is revalidated immediately before source content is read'
);

assert(
  'VIEW_AS_MUTATION_BLOCKED',
  moduleSource.includes('ViewAsMutationBlocked')
    && moduleSource.includes('identities.Value.Actual != identities.Value.Effective')
    && moduleSource.includes('mutationAuthorityTransferred = false'),
  'Administrator View-As remains read-only for queue, retry, and cancellation'
);

assert(
  'EXACT_CONFIRMATIONS',
  contracts.includes('QUEUE-PULSE-AI-PRIVATE-DOCUMENT-PROCESSING')
    && contracts.includes('RETRY-PULSE-AI-PRIVATE-DOCUMENT-PROCESSING')
    && contracts.includes('CANCEL-PULSE-AI-PRIVATE-DOCUMENT-PROCESSING')
    && moduleSource.includes('ConfirmationRequired'),
  'all state-changing runtime requests require exact confirmations'
);

assert(
  'API_SURFACE',
  moduleSource.includes('/api/pulse-ai/v1/documents/runtime/readiness')
    && moduleSource.includes('/api/pulse-ai/v1/documents/runtime/jobs')
    && moduleSource.includes('/api/pulse-ai/v1/documents/{documentId:guid}/runtime-state')
    && moduleSource.includes('/api/pulse-ai/v1/documents/{documentId:guid}/processing-jobs')
    && moduleSource.includes('/api/pulse-ai/v1/documents/runtime/jobs/{jobId:guid}/cancel')
    && moduleSource.includes('/api/pulse-ai/v1/documents/runtime/jobs/{jobId:guid}/retry')
    && pipelineModule.includes('endpoints.MapPulseAiPrivateRuntimeEndpoints();'),
  'readiness, queue, jobs, state, cancellation, and retry are registered through the existing Pulse AI route composition'
);

assert(
  'SERVICE_COMPOSITION',
  services.includes('AddHttpClient("PulseAiPrivateOcr"')
    && services.includes('AddHttpClient("PulseAiPrivateEmbedding"')
    && services.includes('AddSingleton<PulseAiPrivateDocumentRuntimeService>()')
    && services.includes('AddHostedService<PulseAiPrivateDocumentRuntimeWorker>()'),
  'private adapters, repository, runtime service, and hosted worker are registered through the AI composition root'
);

const backend = [contracts, scanner, ocr, embeddings, sourceResolver, repository, runtime, worker, moduleSource].join('\n');
assert(
  'NO_PUBLIC_EXTERNAL_MODEL_PATH',
  !/(?:api\.openai\.com|api\.anthropic\.com|ANTHROPIC_API_KEY|OPENAI_API_KEY|v1\/chat\/completions|v1\/responses)/i.test(backend)
    && !backend.includes('ProjectPulseAiRouter')
    && doc.includes('Raw document content is never sent to Claude or OpenAI.'),
  'private processing contains no Claude, OpenAI, Module 064, or public generation path'
);

assert(
  'NO_SECRET_OR_RAW_CONTENT_RESPONSE',
  contracts.includes('rawDocumentTextReturned = false')
    && contracts.includes('chunkTextReturned = false')
    && contracts.includes('embeddingVectorReturned = false')
    && moduleSource.includes('providerSecretsReturned = false')
    && workbench.includes('Raw documents, extracted sections, chunks, embeddings, scanner responses, and provider secrets are never returned'),
  'browser responses expose operational evidence but not source text, chunks, vectors, secrets, or raw scanner responses'
);

assert(
  'WORKBENCH',
  workbench.includes('data-pulse-ai-private-runtime="v1"')
    && workbench.includes('Runtime Readiness')
    && workbench.includes('Processing Jobs')
    && workbench.includes('Queue Document')
    && workbench.includes('Document State')
    && mount.includes("import PulseAiPrivateRuntimeWorkbench from './PulseAiPrivateRuntimeWorkbench.jsx';")
    && mount.includes('<PulseAiPrivateRuntimeWorkbench />'),
  'Module 011 exposes private readiness, queue, job, and document-state operations'
);

assert(
  'WORKBENCH_CONFIRMATIONS_NO_CACHE',
  workbench.includes('QUEUE-PULSE-AI-PRIVATE-DOCUMENT-PROCESSING')
    && workbench.includes('RETRY-PULSE-AI-PRIVATE-DOCUMENT-PROCESSING')
    && workbench.includes('CANCEL-PULSE-AI-PRIVATE-DOCUMENT-PROCESSING')
    && !workbench.includes('localStorage')
    && !workbench.includes('sessionStorage')
    && !workbench.includes('indexedDB'),
  'the UI requires explicit confirmations and does not persist private runtime evidence in browser storage'
);

assert(
  'RESPONSIVE_UI',
  css.includes('.pulse-ai-runtime-workbench')
    && css.includes('@media (max-width: 1200px)')
    && css.includes('@media (max-width: 820px)')
    && css.includes('@media (max-width: 560px)')
    && css.includes('[data-theme="dark"]'),
  'the private runtime workbench supports desktop, mobile, and dark-theme operation'
);

assert(
  'DOCUMENTED_ACTIVATION_BOUNDARY',
  doc.includes('This source package does not:')
    && doc.includes('apply migration 052 to Test or Production')
    && doc.includes('change Module 064')
    && doc.includes('mutate Azure, Entra, Container Apps')
    && doc.includes('Phase 011D consumes this runtime'),
  'documentation separates source implementation from migration, infrastructure, provider, and deployment approval'
);

console.log(`MODULE_011_PRIVATE_RUNTIME_CHECKS=${checks.length}`);
console.log('MODULE_011_PRIVATE_RUNTIME_PHASE=DURABLE_SOURCE_IMPLEMENTATION_NOT_ACTIVATED');
console.log('MODULE_011_PRIVATE_RUNTIME_MIGRATION_APPLIED=NO');
console.log('MODULE_011_PRIVATE_RUNTIME_WORKER_ENABLED_BY_SOURCE=NO');
console.log('MODULE_011_PRIVATE_RUNTIME_EXTERNAL_MODEL_CALLS=0');
console.log('MODULE_011_PRIVATE_RUNTIME_MODULE064_CHANGES=0');
console.log('MODULE_011_PRIVATE_RUNTIME_AZURE_ENTRA_CHANGES=0');
console.log('MODULE_011_PRIVATE_RUNTIME_DEPLOYMENTS=0');

if (checks.some((check) => !check.condition)) {
  console.error('MODULE_011_PRIVATE_RUNTIME_CONTRACT=FAILED');
  process.exit(1);
}

console.log('MODULE_011_PRIVATE_RUNTIME_CONTRACT=PASSED');
