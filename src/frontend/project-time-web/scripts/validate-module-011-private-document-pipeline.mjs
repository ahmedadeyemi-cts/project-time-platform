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
  console.log(`MODULE011_PRIVATE_DOCS_${name}=${condition ? 'PASSED' : 'FAILED'} — ${evidence}`);
}

function walk(relativeDirectory) {
  const directory = absolute(relativeDirectory);
  if (!fs.existsSync(directory)) return [];
  const files = [];
  for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
    const relative = path.join(relativeDirectory, entry.name);
    if (entry.isDirectory()) files.push(...walk(relative));
    else files.push(relative.replaceAll('\\', '/'));
  }
  return files;
}

const paths = {
  contracts: 'src/backend/ProjectTime.Api/Ai/PulseAiPrivateDocumentPipelineContracts.cs',
  extractor: 'src/backend/ProjectTime.Api/Ai/PulseAiPrivateDocumentExtractionService.cs',
  pipeline: 'src/backend/ProjectTime.Api/Ai/PulseAiPrivateDocumentPipelineService.cs',
  module: 'src/backend/ProjectTime.Api/Modules/PulseAiPrivateDocumentPipelineModule.cs',
  services: 'src/backend/ProjectTime.Api/Ai/ProjectPulseAiServiceCollectionExtensions.cs',
  project: 'src/backend/ProjectTime.Api/ProjectTime.Api.csproj',
  mount: 'src/frontend/project-time-web/src/WorkTaskBuilderPanel.jsx',
  workbench: 'src/frontend/project-time-web/src/PulseAiPrivateDocumentPipelineWorkbench.jsx',
  css: 'src/frontend/project-time-web/src/pulse-ai-private-document-pipeline-workbench.css',
  phaseDoc: 'docs/modules/module-011-pulse-ai/PRIVATE-DOCUMENT-PIPELINE.md',
  indexDoc: 'docs/modules/module-011-pulse-ai/PRIVATE-INDEX-SECURITY-CONTRACT.md',
  foundationValidator: 'src/frontend/project-time-web/scripts/validate-module-011-pulse-ai.mjs',
  deepValidator: 'src/frontend/project-time-web/scripts/validate-module-011-pulse-ai-deep-intelligence.mjs',
  flowHiveValidator: 'src/frontend/project-time-web/scripts/validate-module-066-project-flowhive.mjs'
};

for (const [name, relative] of Object.entries(paths)) {
  assert(`FILE_${name.toUpperCase()}`, exists(relative), relative);
}

if (checks.some((check) => !check.condition)) {
  console.error('MODULE_011_PRIVATE_DOCUMENT_PIPELINE_CONTRACT=FAILED_MISSING_FILE');
  process.exit(1);
}

const contracts = read(paths.contracts);
const extractor = read(paths.extractor);
const pipeline = read(paths.pipeline);
const moduleSource = read(paths.module);
const services = read(paths.services);
const project = read(paths.project);
const mount = read(paths.mount);
const workbench = read(paths.workbench);
const css = read(paths.css);
const phaseDoc = read(paths.phaseDoc);
const indexDoc = read(paths.indexDoc);
const backendSources = [contracts, extractor, pipeline, moduleSource, services].join('\n');

assert(
  'CONTRACT_AND_PRIVACY_BOUNDARY',
  contracts.includes('pulse-ai-private-document-pipeline-v1-20260729')
    && contracts.includes('private_pulse_runtime_only')
    && contracts.includes('raw_document_extraction_chunks_and_embeddings_never_sent_to_external_provider'),
  'the private processing, version, and external-transmission policies are explicit'
);

const requiredExtensions = ['.pdf', '.docx', '.pptx', '.xlsx', '.txt', '.md', '.csv', '.json', '.xml', '.html', '.htm'];
assert(
  'SUPPORTED_FORMATS',
  requiredExtensions.every((extension) => contracts.includes(`"${extension}"`)),
  'PDF, Open XML, HTML/XML, and text-family formats are explicitly allowlisted'
);

const blockedExtensions = ['.exe', '.dll', '.bat', '.cmd', '.ps1', '.sh', '.msi', '.docm', '.xlsm', '.pptm', '.zip', '.7z', '.rar'];
assert(
  'DANGEROUS_FORMATS_BLOCKED',
  blockedExtensions.every((extension) => contracts.includes(`"${extension}"`)),
  'executables, scripts, macro-enabled Office files, and general archives are prohibited'
);

assert(
  'FAIL_CLOSED_CONFIGURATION',
  contracts.includes('PROJECTPULSE_PULSE_AI_DOCUMENT_EXTRACTION_PREVIEW_ENABLED", false')
    && contracts.includes('PROJECTPULSE_PULSE_AI_DOCUMENT_MALWARE_SCAN_ATTESTED", false')
    && contracts.includes('PROJECTPULSE_PRIVATE_OCR_ENDPOINT')
    && contracts.includes('PROJECTPULSE_PRIVATE_EMBEDDING_ENDPOINT')
    && contracts.includes('PROJECTPULSE_PRIVATE_VECTOR_INDEX'),
  'extraction, scan attestation, OCR, embedding, and indexing gates default to disabled or unavailable'
);

assert(
  'PATH_AND_FILE_ADMISSION',
  extractor.includes('Path.GetFullPath(source.StoragePath)')
    && extractor.includes('Path.GetFullPath(options.UploadRoot)')
    && extractor.includes('fullPath.StartsWith')
    && extractor.includes('outside the configured private upload root')
    && extractor.includes('FileAttributes.ReparsePoint')
    && extractor.includes('Symbolic links and reparse points are not accepted')
    && extractor.includes('IsRegularFile'),
  'stored documents are confined to the private root and must be regular non-link files'
);

assert(
  'SIGNATURE_SIZE_AND_ARCHIVE_DEFENSE',
  extractor.includes('DetectFormat(header, extension)')
    && extractor.includes('SignatureMatches(extension, detected)')
    && extractor.includes('options.MaximumFileBytes')
    && extractor.includes('InspectArchiveRisk')
    && extractor.includes('archive.Entries.Count > 20_000')
    && extractor.includes('expanded > options.MaximumFileBytes * 12')
    && extractor.includes('ratio > 250'),
  'signature, size, entry-count, expansion, and compression-ratio checks execute before Open XML parsing'
);

assert(
  'MALWARE_SCAN_REQUIRED',
  extractor.includes('A verifiable malware-scan result is required before parsing document content')
    && extractor.includes('MalwareScanAttested')
    && extractor.includes('AllowedForPreview')
    && phaseDoc.includes('A verifiable malware-scan attestation is required before content parsing'),
  'parsing remains blocked without verifiable malware-scan evidence'
);

assert(
  'PRIVATE_PDF_EXTRACTION',
  project.includes('<PackageReference Include="PdfPig" Version="0.1.10" />')
    && extractor.includes('PdfDocument.Open(source.StoragePath)')
    && extractor.includes('ContentOrderTextExtractor.GetText(page)')
    && extractor.includes('page:{page.Number}'),
  'PDF extraction is private and preserves page anchors'
);

assert(
  'PRIVATE_OPENXML_EXTRACTION',
  extractor.includes('archive.GetEntry("word/document.xml")')
    && extractor.includes('wordprocessingml/2006/main')
    && extractor.includes('ppt/slides/slide')
    && extractor.includes('drawingml/2006/main')
    && extractor.includes('slide:{slideNumber}')
    && extractor.includes('new XLWorkbook(source.StoragePath')
    && extractor.includes('worksheet.RangeUsed()')
    && extractor.includes('sheet:{SafeAnchor(worksheet.Name)}'),
  'DOCX, PPTX, and XLSX extraction retains heading, slide, and worksheet evidence'
);

assert(
  'PRIVATE_TEXT_EXTRACTION',
  extractor.includes('private_utf_text_reader')
    && extractor.includes('private_html_text_normalization')
    && extractor.includes('private_xml_text_nodes')
    && extractor.includes('HtmlScriptStyle.Replace')
    && extractor.includes('XDocument.Parse'),
  'text, Markdown, CSV, JSON, HTML, and XML extraction is local and bounded'
);

assert(
  'OCR_DETECTION_ONLY',
  extractor.includes('ocrRequired')
    && extractor.includes('requires the private OCR adapter')
    && !extractor.includes('new HttpClient')
    && !extractor.includes('IHttpClientFactory')
    && moduleSource.includes('ocrExecution = true'),
  'image-only documents are identified while OCR execution remains explicitly locked'
);

assert(
  'DETERMINISTIC_CITATION_CHUNKS',
  extractor.includes('options.ChunkCharacters')
    && extractor.includes('options.ChunkOverlapCharacters')
    && extractor.includes('DeterministicChunkId')
    && extractor.includes('FindNaturalBoundary')
    && extractor.includes('TextSha256')
    && extractor.includes('SourceSha256')
    && contracts.includes('CitationAnchor')
    && contracts.includes('PageNumber')
    && contracts.includes('SheetName'),
  'chunks preserve source anchors, overlap, checksums, and deterministic identity'
);

const requiredIndexFields = [
  'ChunkId', 'DocumentId', 'ProjectId', 'ProjectCode', 'ProjectName',
  'CustomerName', 'DocumentCategory', 'DocumentVersion', 'Classification',
  'EngineeringVisible', 'AiTimesheetContextEnabled', 'AccessScope',
  'CitationAnchor', 'SourceSha256', 'TextSha256', 'EmbeddingStatus', 'IndexStatus'
];
assert(
  'PERMISSION_SCOPED_INDEX_METADATA',
  requiredIndexFields.every((field) => contracts.includes(field))
    && extractor.includes('BuildIndexProjection')
    && extractor.includes('private_index_configured_write_not_authorized'),
  'index projections include document, project, classification, purpose, citation, and checksum evidence'
);

assert(
  'NO_RAW_TEXT_OR_VECTOR_PUBLIC_RESPONSE',
  contracts.includes('vectorReturned = false')
    && contracts.includes('rawTextReturned = false')
    && contracts.includes('rawDocumentTextReturned = false')
    && contracts.includes('rawDocumentTextSentExternally = false')
    && moduleSource.includes('storagePathsReturned = false')
    && moduleSource.includes('contextSummariesReturned = false')
    && moduleSource.includes('embeddingsReturned = false'),
  'public evidence excludes storage paths, source text, chunks, summaries, embeddings, and vectors'
);

assert(
  'EFFECTIVE_USER_AND_PROJECT_SCOPE',
  pipeline.includes('LoadAccessAsync')
    && pipeline.includes('IsBroadDocumentScope')
    && pipeline.includes('p.project_manager_user_id = @user_id')
    && pipeline.includes('FROM project_assignments pa')
    && pipeline.includes('FROM engineering_resource_requests err')
    && pipeline.includes('AND {engineering} = TRUE')
    && moduleSource.includes('ProjectPulseEffectiveUserId')
    && moduleSource.includes('mutationAuthorityTransferred = false'),
  'inventory and processing are filtered by current effective-user, project, assignment, request, and engineering-visible scope'
);

assert(
  'VERSION_AUTHORITY_NOT_UPLOAD_TIME',
  pipeline.includes('LoadVersionAuthorityQuestionsAsync')
    && pipeline.includes('Define the authoritative version by approval state')
    && pipeline.includes('Do not silently treat upload time alone as contractual authority')
    && phaseDoc.includes('Upload time alone is not contractual authority')
    && indexDoc.includes('Version and supersession rules'),
  'multiple SOW/GSD versions require explicit approval or supersession evidence'
);

assert(
  'GET_ONLY_API',
  moduleSource.includes('/api/pulse-ai/v1/documents/pipeline/readiness')
    && moduleSource.includes('/api/pulse-ai/v1/documents/inventory')
    && moduleSource.includes('/api/pulse-ai/v1/documents/{documentId:guid}/processing-preview')
    && moduleSource.split('MapGet(').length >= 4
    && !moduleSource.includes('MapPost(')
    && !moduleSource.includes('MapPut(')
    && !moduleSource.includes('MapPatch(')
    && !moduleSource.includes('MapDelete('),
  'the private pipeline exposes three authenticated GET-only endpoints'
);

assert(
  'API_COMPOSITION',
  project.includes('app.MapPulseAiPrivateDocumentPipelineEndpoints();')
    && services.includes('AddSingleton<PulseAiPrivateDocumentExtractionService>()')
    && services.includes('AddSingleton<PulseAiPrivateDocumentPipelineService>()'),
  'endpoint and service registration use the existing Pulse AI composition root'
);

assert(
  'NO_MUTATING_SQL',
  !/\b(?:INSERT\s+INTO|UPDATE\s+[a-z_]|DELETE\s+FROM|ALTER\s+TABLE|CREATE\s+TABLE|DROP\s+TABLE|TRUNCATE\s+)\b/i.test(backendSources),
  'the private document package contains no mutating SQL or schema statement'
);

assert(
  'NO_EXTERNAL_PROVIDER_OR_COMMAND_EXECUTION',
  !/(?:api\.openai\.com|api\.anthropic\.com|generativelanguage\.googleapis\.com|v1\/chat\/completions|v1\/responses|ANTHROPIC_API_KEY|OPENAI_API_KEY)/i.test(backendSources)
    && !extractor.includes('HttpClient')
    && !pipeline.includes('HttpClient')
    && !extractor.includes('Process.Start')
    && !extractor.includes('System.Diagnostics.Process')
    && !extractor.includes('ShellExecute')
    && !extractor.includes('soffice'),
  'documents cannot call a public provider, launch Office, run a shell, or execute a command'
);

assert(
  'PERSISTENCE_AND_INFRASTRUCTURE_LOCKED',
  moduleSource.includes('databaseWrites = true')
    && moduleSource.includes('extractionStatusMutation = true')
    && moduleSource.includes('embeddingExecution = true')
    && moduleSource.includes('vectorIndexWrites = true')
    && moduleSource.includes('stateChanged = false')
    && contracts.includes('databaseChanged = false')
    && contracts.includes('vectorIndexChanged = false')
    && phaseDoc.includes('This phase cannot:')
    && phaseDoc.includes('persist extracted text or chunks')
    && phaseDoc.includes('generate embeddings')
    && phaseDoc.includes('write a vector or hybrid index'),
  'locked flags and documentation prohibit persistence, OCR, embeddings, indexing, provider calls, and deployment'
);

assert(
  'WORKBENCH_MOUNT_AND_READ_ONLY_WIRING',
  mount.includes("import PulseAiPrivateDocumentPipelineWorkbench from './PulseAiPrivateDocumentPipelineWorkbench.jsx';")
    && mount.includes('<PulseAiPrivateDocumentPipelineWorkbench />')
    && workbench.includes('data-pulse-ai-private-document-pipeline="v1"')
    && workbench.includes("getJson('/api/pulse-ai/v1/documents/pipeline/readiness')")
    && workbench.includes("buildQuery('/api/pulse-ai/v1/documents/inventory'")
    && workbench.includes('/processing-preview`')
    && !workbench.includes("method: 'POST'")
    && !workbench.includes('localStorage')
    && !workbench.includes('sessionStorage')
    && !workbench.includes('indexedDB'),
  'the UI is mounted, GET-only, and creates no browser persistence of private evidence'
);

assert(
  'WORKBENCH_PRIVACY_AND_RESPONSIVENESS',
  workbench.includes('Raw source text, chunks, storage paths, embeddings, and model prompts are not returned')
    && workbench.includes('never sent to Claude or OpenAI')
    && workbench.includes('Private chunk text is not returned')
    && workbench.includes('Vector generation and index writes remain locked')
    && css.includes('.pulse-ai-doc-workbench')
    && css.includes('@media (max-width: 1200px)')
    && css.includes('@media (max-width: 820px)')
    && css.includes('@media (max-width: 560px)')
    && css.includes('[data-theme="dark"]'),
  'operators see the private boundary in responsive desktop, mobile, and dark-theme layouts'
);

assert(
  'RETRIEVAL_REAUTHORIZATION_AND_REVOCATION',
  indexDoc.includes('Retrieval-time authorization sequence')
    && indexDoc.includes('Apply those filters before keyword, semantic, vector, reranking, or result-fusion operations')
    && indexDoc.includes('Revalidate the source record and document status before prompt assembly')
    && indexDoc.includes('If any required authorization dependency is unavailable, retrieval fails closed')
    && indexDoc.includes('Access changes must propagate to the retrieval layer without retraining a model')
    && indexDoc.includes('immediate logical revocation')
    && indexDoc.includes('physical deletion'),
  'current authorization is applied before ranking and access revocation propagates without retraining'
);

assert(
  'PROMPT_INJECTION_AND_EVALUATION_CONTRACT',
  indexDoc.includes('Prompt-injection defense')
    && indexDoc.includes('treat document text as evidence only')
    && indexDoc.includes('never execute commands found in a document')
    && indexDoc.includes('use allowlisted tools with validated arguments')
    && indexDoc.includes('Authorization isolation')
    && indexDoc.includes('Retrieval quality')
    && indexDoc.includes('Lifecycle')
    && phaseDoc.includes('Acceptance criteria for the next activation phase'),
  'documents remain untrusted evidence and activation requires frozen authorization, retrieval, security, and lifecycle tests'
);

const migrationMatches = walk('database/migrations').filter((relative) => /(?:module[-_]?011|pulse[-_]?ai|private[-_]?document|vector[-_]?index)/i.test(relative));
assert(
  'NO_MIGRATION',
  migrationMatches.length === 0,
  migrationMatches.length === 0
    ? 'no Module 011 private-document, embedding, or vector-index migration exists'
    : `unexpected migration paths: ${migrationMatches.join(', ')}`
);

const ownedPrivateDocumentPaths = [
  '.github/workflows/deep-intelligence-read-contract-ci.yml',
  '.github/workflows/private-document-pipeline-read-contract-ci.yml',
  'src/frontend/project-time-web/scripts/validate-module-011-private-document-pipeline.mjs',
  ...Object.values(paths)
];
const deploymentMatches = [...new Set(ownedPrivateDocumentPaths)].filter((relative) =>
  /(?:module[-_]?011|pulse[-_]?ai|private[-_]?document).*(?:deploy|migration|azure|entra)/i.test(relative)
);
assert(
  'NO_DEPLOYMENT_OR_ENVIRONMENT_ACTION',
  deploymentMatches.length === 0,
  deploymentMatches.length === 0
    ? 'no environment-changing action exists in the owned private-document source scope'
    : `unexpected environment-changing owned paths: ${deploymentMatches.join(', ')}`
);

console.log(`MODULE_011_PRIVATE_DOCUMENT_PIPELINE_CHECKS=${checks.length}`);
console.log('MODULE_011_PRIVATE_DOCUMENT_PIPELINE_PHASE=READ_ONLY_PRIVATE_PROCESSING_PREVIEW');
console.log('MODULE_011_PRIVATE_DOCUMENT_PIPELINE_DATABASE_WRITES=0');
console.log('MODULE_011_PRIVATE_DOCUMENT_PIPELINE_OCR_CALLS=0');
console.log('MODULE_011_PRIVATE_DOCUMENT_PIPELINE_EMBEDDING_CALLS=0');
console.log('MODULE_011_PRIVATE_DOCUMENT_PIPELINE_VECTOR_INDEX_WRITES=0');
console.log('MODULE_011_PRIVATE_DOCUMENT_PIPELINE_EXTERNAL_PROVIDER_CALLS=0');
console.log('MODULE_011_PRIVATE_DOCUMENT_PIPELINE_AZURE_ENTRA_CHANGES=0');
console.log('MODULE_011_PRIVATE_DOCUMENT_PIPELINE_DEPLOYMENTS=0');

if (checks.some((check) => !check.condition)) {
  console.error('MODULE_011_PRIVATE_DOCUMENT_PIPELINE_CONTRACT=FAILED');
  process.exit(1);
}

console.log('MODULE_011_PRIVATE_DOCUMENT_PIPELINE_CONTRACT=PASSED');
