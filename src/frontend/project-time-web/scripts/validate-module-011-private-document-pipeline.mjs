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

assert(
  'CONTRACT_VERSION',
  contracts.includes('pulse-ai-private-document-pipeline-v1-20260729')
    && contracts.includes('private_pulse_runtime_only')
    && contracts.includes('raw_document_extraction_chunks_and_embeddings_never_sent_to_external_provider'),
  'the private processing and external-transmission boundaries are versioned'
);

const requiredExtensions = ['.pdf', '.docx', '.pptx', '.xlsx', '.txt', '.md', '.csv', '.json', '.xml', '.html', '.htm'];
assert(
  'FORMAT_ALLOWLIST',
  requiredExtensions.every((extension) => contracts.includes(`"${extension}"`)),
  'PDF, Open XML, HTML/XML, and text-family formats are explicitly allowlisted'
);

const blockedExtensions = ['.exe', '.dll', '.bat', '.cmd', '.ps1', '.sh', '.msi', '.docm', '.xlsm', '.pptm', '.zip', '.7z', '.rar'];
assert(
  'DANGEROUS_FORMAT_BLOCKLIST',
  blockedExtensions.every((extension) => contracts.includes(`"${extension}"`)),
  'executables, scripts, macro-enabled Office files, and general archives are blocked'
);

assert(
  'CONFIGURATION_FAILS_CLOSED',
  contracts.includes('ExtractionPreviewEnabled: Boolean("PROJECTPULSE_PULSE_AI_DOCUMENT_EXTRACTION_PREVIEW_ENABLED", false)')
    && contracts.includes('MalwareScanAttested: Boolean("PROJECTPULSE_PULSE_AI_DOCUMENT_MALWARE_SCAN_ATTESTED", false)')
    && contracts.includes('OcrEndpointConfigured: HasValue("PROJECTPULSE_PRIVATE_OCR_ENDPOINT")')
    && contracts.includes('PrivateEmbeddingEndpointConfigured')
    && contracts.includes('PrivateVectorIndexConfigured'),
  'extraction, scan attestation, OCR, embeddings, and indexing are disabled or unavailable by default'
);

assert(
  'PATH_CONFINEMENT',
  extractor.includes('Path.GetFullPath(source.StoragePath)')
    && extractor.includes('Path.GetFullPath(options.UploadRoot)')
    && extractor.includes('fullPath.StartsWith')
    && extractor.includes('Stored path is outside the configured private upload root')
    && pipeline.includes('StoredPathConfined'),
  'stored documents must remain inside the configured private upload root'
);

assert(
  'FILESYSTEM_LINK_DEFENSE',
  extractor.includes('FileAttributes.ReparsePoint')
    && extractor.includes('Symbolic links and reparse points are not accepted')
    && extractor.includes('IsRegularFile'),
  'reparse points, links, and non-regular files are rejected'
);

assert(
  'SIGNATURE_AND_SIZE_ADMISSION',
  extractor.includes('DetectFormat(header, extension)')
    && extractor.includes('SignatureMatches(extension, detected)')
    && extractor.includes('File signature')
    && extractor.includes('options.MaximumFileBytes')
    && extractor.includes('SizeWithinLimit'),
  'extension, file signature, and size are validated before parsing'
);

assert(
  'ARCHIVE_EXPANSION_DEFENSE',
  extractor.includes('InspectArchiveRisk')
    && extractor.includes('archive.Entries.Count > 20_000')
    && extractor.includes('expanded > options.MaximumFileBytes * 12')
    && extractor.includes('ratio > 250')
    && extractor.includes('ArchiveBombRiskDetected'),
  'Open XML packages are bounded by entry count, expansion size, and compression ratio'
);

assert(
  'MALWARE_SCAN_GATE',
  extractor.includes('A verifiable malware-scan result is required before parsing document content')
    && extractor.includes('MalwareScanAttested')
    && extractor.includes('AllowedForPreview')
    && phaseDoc.includes('A verifiable malware-scan attestation is required before content parsing'),
  'content parsing requires verifiable malware-scan evidence'
);

assert(
  'PDF_PRIVATE_EXTRACTION',
  project.includes('<PackageReference Include="PdfPig" Version="0.1.10" />')
    && extractor.includes('PdfDocument.Open(source.StoragePath)')
    && extractor.includes('ContentOrderTextExtractor.GetText(page)')
    && extractor.includes('$"page:{page.Number}"'),
  'PDF text remains private and retains page-level citation anchors'
);

assert(
  'DOCX_PRIVATE_EXTRACTION',
  extractor.includes('archive.GetEntry("word/document.xml")')
    && extractor.includes('XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main"')
    && extractor.includes('pStyle')
    && extractor.includes('docx_openxml_paragraph_order'),
  'DOCX extraction preserves paragraph and heading order without executing Office automation'
);

assert(
  'PPTX_PRIVATE_EXTRACTION',
  extractor.includes('^ppt/slides/slide[0-9]+\\.xml$')
    && extractor.includes('XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main"')
    && extractor.includes('$"slide:{slideNumber}"')
    && extractor.includes('pptx_openxml_slide_text'),
  'PPTX extraction retains slide-level citations'
);

assert(
  'XLSX_PRIVATE_EXTRACTION',
  extractor.includes('new XLWorkbook(source.StoragePath')
    && extractor.includes('worksheet.RangeUsed()')
    && extractor.includes('$"sheet:{SafeAnchor(worksheet.Name)}"')
    && extractor.includes('xlsx_closedxml_formatted_cells'),
  'XLSX extraction retains worksheet-level citations without running Excel'
);

assert(
  'TEXT_HTML_XML_EXTRACTION',
  extractor.includes('private_utf_text_reader')
    && extractor.includes('private_html_text_normalization')
    && extractor.includes('private_xml_text_nodes')
    && extractor.includes('HtmlScriptStyle.Replace')
    && extractor.includes('XDocument.Parse'),
  'text, HTML, and XML extraction are local and bounded'
);

assert(
  'OCR_DETECTION_NOT_EXECUTION',
  extractor.includes('ocrRequired')
    && extractor.includes('requires the private OCR adapter')
    && !extractor.includes('new HttpClient')
    && !extractor.includes('IHttpClientFactory')
    && moduleSource.includes('ocrExecution = true'),
  'image-only documents are identified while OCR execution remains locked'
);

assert(
  'DETERMINISTIC_CHUNKING',
  extractor.includes('DefaultChunkCharacters') === false
    && extractor.includes('options.ChunkCharacters')
    && extractor.includes('options.ChunkOverlapCharacters')
    && extractor.includes('DeterministicChunkId')
    && extractor.includes('TextSha256')
    && extractor.includes('SourceSha256')
    && extractor.includes('FindNaturalBoundary'),
  'chunks preserve citations, overlap, deterministic IDs, and checksums'
);

assert(
  'INDEX_SECURITY_METADATA',
  ['ChunkId', 'DocumentId', 'ProjectId', 'ProjectCode', 'CustomerName', 'DocumentCategory', 'DocumentVersion', 'Classification', 'EngineeringVisible', 'AiTimesheetContextEnabled', 'AccessScope', 'CitationAnchor', 'SourceSha256', 'TextSha256', 'EmbeddingStatus', 'IndexStatus']
    .every((field) => contracts.includes(`${field}:`) || contracts.includes(`string ${field}`) || contracts.includes(`Guid ${field}`) || contracts.includes(`bool ${field}`)),
  'index projections carry project, classification, purpose, citation, and checksum evidence'
);

assert(
  'NO_VECTOR_OR_RAW_TEXT_PUBLIC_RESPONSE',
  contracts.includes('vectorReturned = false')
    && contracts.includes('rawTextReturned = false')
    && contracts.includes('rawDocumentTextReturned = false')
    && contracts.includes('rawDocumentTextSentExternally = false')
    && moduleSource.includes('storagePathsReturned = false')
    && moduleSource.includes('contextSummariesReturned = false')
    && moduleSource.includes('embeddingsReturned = false'),
  'browser responses expose evidence but not storage paths, source text, chunks, summaries, embeddings, or vectors'
);

assert(
  'EFFECTIVE_USER_AUTHORIZATION',
  pipeline.includes('LoadAccessAsync')
    && pipeline.includes('IsBroadDocumentScope')
    && pipeline.includes('p.project_manager_user_id = @user_id')
    && pipeline.includes('FROM project_assignments pa')
    && pipeline.includes('FROM engineering_resource_requests err')
    && moduleSource.includes('ProjectPulseEffectiveUserId')
    && moduleSource.includes('mutationAuthorityTransferred = false'),
  'inventory and processing use effective-user project scope and read-only View-As behavior'
);

assert(
  'ENGINEERING_VISIBLE_ONLY',
  pipeline.includes('AND {engineering} = TRUE')
    && pipeline.includes('Only active engineering-visible documents') === false
    && moduleSource.includes('Only active engineering-visible project documents are eligible'),
  'private AI inventory excludes documents outside engineering-visible scope'
);

assert(
  'VERSION_AUTHORITY_CONFLICTS',
  pipeline.includes('LoadVersionAuthorityQuestionsAsync')
    && pipeline.includes('Define the authoritative version by approval state')
    && pipeline.includes('Do not silently treat upload time alone as contractual authority')
    && phaseDoc.includes('Upload time alone is not contractual authority')
    && indexDoc.includes('Version and supersession rules'),
  'multiple active SOW/GSD versions are surfaced instead of silently selected'
);

assert(
  'READ_ONLY_ENDPOINTS',
  moduleSource.includes('MapGet(')
    && moduleSource.includes('/api/pulse-ai/v1/documents/pipeline/readiness')
    && moduleSource.includes('/api/pulse-ai/v1/documents/inventory')
    && moduleSource.includes('/api/pulse-ai/v1/documents/{documentId:guid}/processing-preview')
    && !moduleSource.includes('MapPost(')
    && !moduleSource.includes('MapPut(')
    && !moduleSource.includes('MapPatch(')
    && !moduleSource.includes('MapDelete('),
  'the private pipeline exposes three authenticated GET-only endpoints'
);

assert(
  'ENDPOINT_REGISTRATION',
  project.includes('app.MapPulseAiPrivateDocumentPipelineEndpoints();')
    && services.includes('AddSingleton<PulseAiPrivateDocumentExtractionService>()')
    && services.includes('AddSingleton<PulseAiPrivateDocumentPipelineService>()'),
  'the API registers the new module and private services exactly through the existing AI composition root'
);

const backendSources = [contracts, extractor, pipeline, moduleSource, services].join('\n');
assert(
  'NO_MUTATING_SQL',
  !/\b(?:INSERT\s+INTO|UPDATE\s+[a-z_]|DELETE\s+FROM|ALTER\s+TABLE|CREATE\s+TABLE|DROP\s+TABLE|TRUNCATE\s+)\b/i.test(backendSources),
  'the source package contains no mutating SQL or schema statement'
);

assert(
  'NO_DIRECT_EXTERNAL_PROVIDER',
  !/(?:api\.openai\.com|api\.anthropic\.com|generativelanguage\.googleapis\.com|v1\/chat\/completions|v1\/responses|ANTHROPIC_API_KEY|OPENAI_API_KEY)/i.test(backendSources)
    && !extractor.includes('HttpClient')
    && !pipeline.includes('HttpClient')
    && moduleSource.includes('externalProviderCalled = false'),
  'private document processing cannot call a public model or manage provider credentials'
);

assert(
  'NO_PROCESS_EXECUTION',
  !extractor.includes('Process.Start')
    && !extractor.includes('System.Diagnostics.Process')
    && !extractor.includes('LibreOffice')
    && !extractor.includes('soffice')
    && !extractor.includes('ShellExecute'),
  'documents cannot launch Office, scripts, shells, or operating-system commands'
);

assert(
  'NO_PERSISTENCE',
  moduleSource.includes('databaseWrites = true')
    && moduleSource.includes('extractionStatusMutation = true')
    && moduleSource.includes('embeddingExecution = true')
    && moduleSource.includes('vectorIndexWrites = true')
    && moduleSource.includes('stateChanged = false')
    && contracts.includes('databaseChanged = false')
    && contracts.includes('vectorIndexChanged = false')
    && phaseDoc.includes('This phase does not write extraction text, summaries, chunks, embeddings, or index records'),
  'locked flags explicitly identify operations that are prohibited in this phase'
);

assert(
  'WORKBENCH_MOUNT',
  mount.includes("import PulseAiPrivateDocumentPipelineWorkbench from './PulseAiPrivateDocumentPipelineWorkbench.jsx';")
    && mount.includes('<PulseAiPrivateDocumentPipelineWorkbench />')
    && workbench.includes('data-pulse-ai-private-document-pipeline="v1"'),
  'the private pipeline workbench is mounted inside the established Module 011 compatibility route'
);

assert(
  'WORKBENCH_API_WIRING',
  workbench.includes("getJson('/api/pulse-ai/v1/documents/pipeline/readiness')")
    && workbench.includes("buildQuery('/api/pulse-ai/v1/documents/inventory'")
    && workbench.includes('/processing-preview`')
    && !workbench.includes("method: 'POST'")
    && !workbench.includes('localStorage')
    && !workbench.includes('sessionStorage')
    && !workbench.includes('indexedDB'),
  'the browser uses GET-only read surfaces and creates no local or durable private-data cache'
);

assert(
  'WORKBENCH_PRIVACY_COPY',
  workbench.includes('Raw source text, chunks, storage paths, embeddings, and model prompts are not returned')
    && workbench.includes('never sent to Claude or OpenAI')
    && workbench.includes('Private chunk text is not returned')
    && workbench.includes('Vector generation and index writes remain locked'),
  'operators can distinguish private evidence from raw document content and active processing'
);

assert(
  'RESPONSIVE_UI',
  css.includes('.pulse-ai-doc-workbench')
    && css.includes('@media (max-width: 1200px)')
    && css.includes('@media (max-width: 820px)')
    && css.includes('@media (max-width: 560px)')
    && css.includes('[data-theme="dark"]'),
  'the new workbench supports desktop, mobile, and dark-theme layouts'
);

assert(
  'RETRIEVAL_REAUTHORIZATION_CONTRACT',
  indexDoc.includes('Retrieval-time authorization sequence')
    && indexDoc.includes('Apply those filters before keyword, semantic, vector, reranking, or result-fusion operations')
    && indexDoc.includes('Revalidate the source record and document status before prompt assembly')
    && indexDoc.includes('If any required authorization dependency is unavailable, retrieval fails closed'),
  'the index is never treated as the authorization authority'
);

assert(
  'REVOCATION_CONTRACT',
  indexDoc.includes('Revocation and deletion')
    && indexDoc.includes('Access changes must propagate to the retrieval layer without retraining a model')
    && indexDoc.includes('immediate logical revocation')
    && indexDoc.includes('physical deletion'),
  'role, assignment, visibility, retention, and deletion changes revoke retrieval eligibility'
);

assert(
  'PROMPT_INJECTION_CONTRACT',
  indexDoc.includes('Prompt-injection defense')
    && indexDoc.includes('treat document text as evidence only')
    && indexDoc.includes('never execute commands found in a document')
    && indexDoc.includes('use allowlisted tools with validated arguments'),
  'retrieved documents cannot become trusted model instructions or command sources'
);

assert(
  'COMPREHENSIVE_EVALUATION_CONTRACT',
  indexDoc.includes('Authorization isolation')
    && indexDoc.includes('Retrieval quality')
    && indexDoc.includes('Security')
    && indexDoc.includes('Lifecycle')
    && phaseDoc.includes('Acceptance criteria for the next activation phase'),
  'activation is gated by authorization, retrieval, citation, security, and lifecycle evaluations'
);

const migrationMatches = walk('database/migrations').filter((relative) => /(?:module[-_]?011|pulse[-_]?ai|private[-_]?document|vector[-_]?index)/i.test(relative));
assert(
  'NO_MIGRATION',
  migrationMatches.length === 0,
  migrationMatches.length === 0
    ? 'no Module 011 private-document, embedding, or vector-index migration exists'
    : `unexpected migration paths: ${migrationMatches.join(', ')}`
);

const deploymentMatches = [
  ...walk('.github/workflows'),
  ...walk('scripts'),
  ...walk('deployment')
].filter((relative) => /(?:module[-_]?011|pulse[-_]?ai|private[-_]?document).*(?:deploy|migration|azure|entra)/i.test(relative));
assert(
  'NO_DEPLOYMENT_OR_ENVIRONMENT_ACTION',
  deploymentMatches.length === 0,
  deploymentMatches.length === 0
    ? 'no private-document deployment, migration, Azure, or Entra action exists'
    : `unexpected environment-changing paths: ${deploymentMatches.join(', ')}`
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
