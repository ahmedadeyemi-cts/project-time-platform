import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const failures = [];
const read = (relative) => fs.readFileSync(path.join(root, relative), 'utf8');
const requireText = (source, text, label) => {
  if (!source.includes(text)) failures.push(`${label}: missing ${JSON.stringify(text)}`);
};
const rejectText = (source, text, label) => {
  if (source.includes(text)) failures.push(`${label}: forbidden ${JSON.stringify(text)}`);
};

const resolver = read('src/backend/ProjectTime.Api/Modules/ProjectPlanningDocumentResolver.cs');
const orchestrator = read('src/backend/ProjectTime.Api/Modules/ProjectPlanningAiOrchestrator.cs');
const flowHive = read('src/backend/ProjectTime.Api/Modules/ProjectFlowHiveAiPlannerOrchestrationModule.cs');
const flowHiveEnterprise = read('src/backend/ProjectTime.Api/Modules/ProjectFlowHiveEnterpriseModule.cs');
const forge = read('src/backend/ProjectTime.Api/Modules/ProjectForgeModule.cs');
const forgeUi = read('src/frontend/project-time-web/src/ProjectForgeCenter.jsx');
const builder = read('src/backend/ProjectTime.Api/Modules/ProjectFlowHiveDetailedPlanBuilder.cs');
const categories = read('src/backend/ProjectTime.Api/Ai/PulseAiPrivateRagContracts.cs');
const privateRag = read('src/backend/ProjectTime.Api/Ai/PulseAiPrivateRagService.cs');
const deployment = read('.github/workflows/projectpulse-deploy-test.yml');
const migrationRunner = read('scripts/release-test/run-systemwide-enterprise-reliability-migrations-job.sh');
const program = read('src/backend/ProjectTime.Api/Program.cs');
const enterpriseService = read('src/backend/ProjectTime.Api/Ai/CelarAiEnterprisePlatformService.cs');
const uploadStorage = read('src/backend/ProjectTime.Api/Ai/ProjectPulseUploadStorage.cs');
const flowHiveUi = read('src/frontend/project-time-web/src/ProjectFlowHiveCenter.jsx');
const flowHivePanels = read('src/frontend/project-time-web/src/ProjectFlowHiveEnterprisePanels.jsx');
const runtimeVerifier = read('scripts/release-test/verify-runtime.mjs');


const candidateFenceIndex = program.indexOf('app.UseProjectPulseAiCandidateRequestFence();');
const workRegisterAuthorizationIndex = program.indexOf('app.UseWorkRegisterAuthorization();');
const transientFailureMiddlewareIndex = program.indexOf('app.UseMiddleware<ProjectTime.Api.Modules.CelarAiTransientFailureMiddleware>();');
if (!(candidateFenceIndex >= 0
  && workRegisterAuthorizationIndex > candidateFenceIndex
  && transientFailureMiddlewareIndex > workRegisterAuthorizationIndex)) {
  failures.push('Celar transient failure middleware must run after candidate and authorization fences');
}

for (const source of [flowHive, forge]) {
  requireText(source, 'ProjectPlanningDocumentResolver.ResolveAndPrepareAsync', 'shared project document resolver');
  requireText(source, 'ProjectPlanningAiOrchestrator.GenerateAsync', 'shared AI planning orchestrator');
}

for (const token of [
  'project_intake_documents',
  'work_register_documents',
  'work_register_document_id',
  'upload_source',
  'stored_file_path',
  'project_id=@project_id',
  'pulse_ai_document_processing_jobs',
  'project_planning_ai_automatic',
  'projectpulse094_reconcile_ready_work_register_sow',
  'StatementOfWork',
  'GeneralSolutionDesign',
  'SupplementalDocuments',
  'ScopeCitationCount',
  'ReadyForGeneration'
]) requireText(resolver, token, 'project-scoped document authority');

rejectText(resolver, 'FileName.Contains', 'filename guessing must not establish SOW authority');
rejectText(resolver, 'original_file_name ILIKE', 'filename guessing must not establish SOW authority');
requireText(flowHiveEnterprise, 'LEFT JOIN work_register_documents work_register', 'FlowHive evidence workspace Work Register authority');
requireText(flowHiveEnterprise, "work_register.upload_source='local_file'", 'FlowHive evidence workspace durable local source');
rejectText(flowHiveEnterprise, "original_file_name ILIKE '%statement%of%work%'", 'FlowHive evidence workspace filename guessing');
rejectText(flowHiveEnterprise, "original_file_name ~* '(^|[^a-z])sow([^a-z]|$)'", 'FlowHive evidence workspace filename regex');

for (const category of [
  'sow', 'statement_of_work', 'gsd', 'global_solution_design',
  'architecture', 'design', 'order', 'proposal', 'requirements',
  'technical_specification', 'implementation_plan', 'runbook', 'supporting'
]) requireText(categories, `"${category}"`, 'shared planning category');

// Exact selected-project identity must survive every private planning boundary.
for (const token of ['Guid? ProjectId = null', 'Guid? TaskId = null', 'Guid? AssignmentId = null'])
  requireText(categories, token, 'private FlowHive identity contract');
for (const token of ['ProjectId: request.ProjectId', 'TaskId: request.TaskId', 'AssignmentId: request.AssignmentId'])
  requireText(enterpriseService, token, 'Celar enterprise planning identity propagation');
const privateFlowHiveMethod = privateRag.slice(
  privateRag.indexOf('public async Task<PulseAiPrivateRagAnswer> GenerateFlowHivePlanAsync'),
  privateRag.indexOf('public async Task<bool> SaveFeedbackAsync')
);
for (const token of ['projectId: request.ProjectId', 'taskId: request.TaskId', 'assignmentId: request.AssignmentId'])
  requireText(privateFlowHiveMethod, token, 'private retrieval exact identity');
rejectText(privateFlowHiveMethod, 'projectId: null', 'private FlowHive retrieval must not discard project identity');

// Module 055C durable download is a prerequisite for private planning.
for (const token of ['ResolveExistingStoredFile', 'work-register-documents', 'documentId.ToString("N")', 'FileAttributes.ReparsePoint'])
  requireText(uploadStorage, token, 'durable project document relocation');
for (const token of ['ProjectPulseUploadStorage.ResolveExistingStoredFile', 'storageRootFingerprint', 'durable file is not available'])
  requireText(program, token, 'Module 055C durable download route');
for (const token of ['DurableFileAvailable', 'durable file cannot be downloaded', 'Restore the Module 055C document file'])
  requireText(resolver, token, 'project evidence durable-file gate');

// AI Planner owns project seed creation and the browser never supplies a fake plan.
for (const token of ['LoadProjectSeedAsync', 'FROM projects project', 'FlowHive AI working draft'])
  requireText(flowHive, token, 'server-owned Planner seed');
rejectText(flowHive, 'requires the selected project\'s current Planner seed', 'browser-created seed dependency');
requireText(flowHiveUi, 'async function runAiPlannerOperation()', 'server-owned AI Planner client');
rejectText(flowHiveUi, 'plan: seedPlan', 'browser-supplied AI Planner seed');
rejectText(flowHiveUi, 'disabled={!draftPlan || busy || !canAdministerPlanner}', 'AI Planner browser draft dependency');
for (const token of ['AI Planning Workspace', 'Start AI Planner', 'No pasted excerpt, duplicate upload, or manual preparation step is required'])
  requireText(flowHiveUi, token, 'evidence-only AI Planning Workspace');
for (const token of ['Retry automatic processing', 'AI Planner will automatically start or resume private processing'])
  requireText(flowHivePanels, token, 'automatic document processing UI');
for (const forbidden of ['AI draft studio', 'Optional approved SOW excerpt', 'Optional approved GSD excerpt', 'Prepare / queue processing', 'Generate and auto-fill detailed plan'])
  rejectText(`${flowHiveUi}\n${flowHivePanels}`, forbidden, 'legacy duplicate AI planning interface');

// Exact Protected-UAT validation proves download durability and server-owned generation.
for (const token of ['module055cSowDownload', 'requestBinary(', 'persistedAcrossApiRevision: true', 'body: { requestedOutcome:'])
  requireText(runtimeVerifier, token, 'Protected UAT FlowHive V2 evidence');
rejectText(runtimeVerifier, 'body: { plan: seedPlan', 'Protected UAT browser-seed simulation');

for (const token of [
  'CelarAiCapabilityCatalog.ProjectFlowHivePlan',
  'CelarAiCapabilityCatalog.ProjectForgePlanEstimate',
  'ProjectFlowHiveDetailedPlanBuilder.Build',
  'ProjectFlowHiveScheduleEngine.Validate',
  'ProjectFlowHiveScheduleEngine.Calculate',
  'current active Work Register SOW',
  'No generic plan was substituted',
  'Never fabricate missing information',
  'project_end_exceeded'
]) requireText(`${flowHive}\n${forge}\n${orchestrator}`, token, 'shared source-grounded planning');

for (const phase of ['Plan', 'Design', 'Implement', 'Validate', 'Release']) {
  requireText(builder, `"${phase}"`, `five-phase ${phase}`);
}
rejectText(builder, 'FitPackageChainsToSelectedWindow', 'silent schedule compression');
requireText(privateRag, 'Products = List(task.Products', 'structured AI planning field normalization');
requireText(builder, 'AddDistinct(Products, task.Products)', 'structured source field preservation');
requireText(builder, 'Products: Combine(24, 1_000, package.Products', 'structured Planner task population');

requireText(forge, 'automaticBaselineCreated = false', 'Forge no automatic baseline response');
rejectText(orchestrator, 'EstablishBaseline', 'shared orchestrator must not baseline');

const forgeMethod = forge.slice(
  forge.indexOf('private static async Task<IResult> GenerateAiDraftAsync'),
  forge.indexOf('private static async Task<IResult> AssignReviewerAsync')
);
requireText(forgeMethod, 'Status202Accepted', 'Forge document-processing progress');
requireText(forgeMethod, 'document_grounded_review_draft_created', 'Forge review draft persistence');
requireText(forgeMethod, 'canonicalTasksCreated = false', 'Forge canonical mutation boundary');
rejectText(forgeMethod, 'Results.UnprocessableEntity', 'Forge evidence processing must not terminate with HTTP 422');
for (const token of [
  'for (let attempt = 1; attempt <= 60; attempt += 1)',
  "status === 'project_planning_documents_processing'",
  'waitForProjectPlanning(',
  "disabled={!currentProjectId || busy === 'ai'}"
]) requireText(forgeUi, token, 'Forge automatic document-processing polling');
rejectText(forgeUi, "projectEvidenceMissing ? 'Project evidence required'", 'Forge client-side pre-processing block');

for (const token of [
  '094_flowhive_canonical_sow_authority.sql',
  '095_project_planning_collaboration_access.sql',
  '096_project_planning_document_authority.sql',
  'MIGRATION_094=APPLIED_AND_VERIFIED',
  'MIGRATION_095=APPLIED_AND_VERIFIED',
  'MIGRATION_096=APPLIED_AND_VERIFIED'
]) requireText(`${deployment}\n${migrationRunner}`, token, 'Protected Test migration wiring');
for (const token of [
  'FLOWHIVE_AI_PLANNER_UAT=PASSED',
  'PROJECT_FORGE_AI_PLANNER_UAT=PASSED',
  'CELAR_AI_STABILITY_UAT=PASSED',
  'flowHiveWorkingCopyOnly:true',
  'projectForgeReviewOnly:true',
  'estimatesCompressed:false'
]) requireText(deployment, token, 'Protected Test authenticated planning UAT');
requireText(deployment, 'environment: test', 'Protected Test boundary');
rejectText(deployment, 'environment: production', 'Production deployment boundary');

if (failures.length) {
  console.error('Shared project-document planning validation failed:');
  failures.forEach((failure) => console.error(`- ${failure}`));
  process.exit(1);
}

console.log('SHARED_PROJECT_DOCUMENT_PLANNING=PASS');
console.log('document_authority=project_id_and_work_register');
console.log('flowhive_persistence=mutable_working_copy_only');
console.log('forge_persistence=review_draft_only');
console.log('production_mutation=none');
