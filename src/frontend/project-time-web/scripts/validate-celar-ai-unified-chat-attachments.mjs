import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = fileURLToPath(new URL('../../../../', import.meta.url));
const read = (relative) => fs.readFileSync(path.join(root, relative), 'utf8');
const checks = [];

function check(name, condition, evidence) {
  checks.push({ name, condition });
  console.log(`CELAR_AI_UNIFIED_${name}=${condition ? 'PASSED' : 'FAILED'} — ${evidence}`);
}

const all = (source, markers) => markers.every((marker) => source.includes(marker));
const section = (source, start, end) => {
  const first = source.indexOf(start);
  if (first < 0) return '';
  const last = source.indexOf(end, first + start.length);
  return last < 0 ? source.slice(first) : source.slice(first, last);
};

const main = read('src/frontend/project-time-web/src/main.jsx');
const app = read('src/frontend/project-time-web/src/App.jsx');
const help = read('src/frontend/project-time-web/src/HelpAssistant.jsx');
const platform = read('src/frontend/project-time-web/src/CelarAiProductionPlatform.jsx');
const module011Mount = read('src/frontend/project-time-web/src/WorkTaskBuilderPanel.jsx');
const brandModule = read('src/backend/ProjectTime.Api/Modules/CelarAiBrandModule.cs');
const productionModule = read('src/backend/ProjectTime.Api/Modules/CelarAiProductionPlatformModule.cs');
const backendBuild = read('src/backend/ProjectTime.Api/Directory.Build.targets');
const attachmentModule = read('src/backend/ProjectTime.Api/Modules/CelarAiConversationAttachmentModule.cs');
const attachmentContracts = read('src/backend/ProjectTime.Api/Ai/CelarAiConversationAttachmentContracts.cs');
const attachmentRepository = read('src/backend/ProjectTime.Api/Ai/CelarAiConversationAttachmentRepository.cs');
const attachmentService = read('src/backend/ProjectTime.Api/Ai/CelarAiConversationAttachmentService.cs');
const sourceResolver = read('src/backend/ProjectTime.Api/Ai/PulseAiPrivateRuntimeSourceResolver.cs');
const retrieval = read('src/backend/ProjectTime.Api/Ai/PulseAiPrivateRagRepository.cs');
const reauthorization = read('src/backend/ProjectTime.Api/Ai/PulseAiPrivateRetrievalAuthorizationService.cs');
const retrievalService = read('src/backend/ProjectTime.Api/Ai/PulseAiPrivateRetrievalService.cs');
const ragService = read('src/backend/ProjectTime.Api/Ai/PulseAiPrivateRagService.cs');
const ragModule = read('src/backend/ProjectTime.Api/Modules/PulseAiPrivateRagModule.cs');
const systemService = read('src/backend/ProjectTime.Api/Ai/PulseAiSystemIntelligenceService.cs');
const systemRepository = read('src/backend/ProjectTime.Api/Ai/PulseAiSystemIntelligenceRepository.cs');
const routing = read('src/backend/ProjectTime.Api/Ai/CelarAiCapabilityRouting.cs');
const runtime = read('src/backend/ProjectTime.Api/Ai/PulseAiPrivateDocumentRuntimeService.cs');
const runtimeRepository = read('src/backend/ProjectTime.Api/Ai/PulseAiPrivateDocumentRuntimeRepository.cs');
const retentionWorker = read('src/backend/ProjectTime.Api/Ai/CelarAiConversationAttachmentRetentionWorker.cs');
const services = read('src/backend/ProjectTime.Api/Ai/ProjectPulseAiServiceCollectionExtensions.cs');
const projectWorkspace = read('src/backend/ProjectTime.Api/Modules/ProjectWorkspaceModule.cs');
const projectIntake = read('src/backend/ProjectTime.Api/Modules/ProjectIntakeModule.cs');
const securityHardening = read('src/backend/ProjectTime.Api/Modules/SecurityHardeningModule.cs');
const migration = read('database/migrations/072_celar_ai_conversation_attachments.sql');
const rollback = read('database/rollback/072_celar_ai_conversation_attachments_rollback.sql');
const migrationTest = read('tests/test-celar-ai-conversation-attachments-migration-072.sh');

const submit = section(help, 'async function submitQuestion', 'function handleQuestionKeyDown');
const externalPreparation = section(
  routing,
  'private ProjectPulseAiGenerationRequest? PrepareExternalRequest(',
  'private static string BuildFallbackWarning('
);
const retrievalEventPersistence = section(
  retrieval,
  'public async Task SaveRetrievalEventAsync(',
  'public async Task<bool> CompleteAnswerRunAsync('
);
const answerCompletion = section(
  retrieval,
  'public async Task<bool> CompleteAnswerRunAsync(',
  'public async Task<bool> SaveFeedbackAsync('
);
const feedbackPersistence = section(
  retrieval,
  'public async Task<bool> SaveFeedbackAsync(',
  'public async Task<object?> GetAnswerAuditAsync('
);
const messagePersistence = section(
  systemRepository,
  'public async Task<(Guid MessageId, int SequenceNumber)> AppendMessageAsync(',
  'public async Task<Guid> CreateInquiryRunAsync('
);

check(
  'ONE_CHAT_OWNER',
  (main.match(/<HelpAssistant\s*\/>/g) || []).length === 1
    && !app.includes('<HelpAssistant />')
    && app.includes("'celar-ai': 'work-task-builder'")
    && app.includes("href: '#celar-ai'")
    && module011Mount.includes('return <CelarAiProductionPlatform />;')
    && platform.includes("projectpulse:open-celar-ai-chat")
    && help.includes("window.addEventListener('projectpulse:open-celar-ai-chat'"),
  'global Ask and Module 011 open one HelpAssistant owner and one durable conversation store'
);

check(
  'CANONICAL_V2_ROUTE_NO_SILENT_LEGACY_FALLBACK',
  submit.includes("const path = '/api/celar-ai/v2/chat';")
    && submit.includes('postJson(path, {')
    && !submit.includes('/api/help/plan')
    && !submit.includes('/api/celar-ai/v1/chat')
    && brandModule.includes('endpoints.MapCelarAiEnterprisePlatformEndpoints();')
    && backendBuild.includes("grep -Fq 'endpoints.MapCelarAiEnterprisePlatformEndpoints();'")
    && backendBuild.includes('DestinationFiles="$(CelarAiBrandGenerated)"')
    && !backendBuild.includes('print &quot;        endpoints.MapCelarAiEnterprisePlatformEndpoints();&quot;')
    && productionModule.includes('endpoints.MapPost(ChatRoute,'),
  'the checked-in client and compiled startup use v2 directly and surface failures instead of masking them with v1'
);

check(
  'MODULE064_ROUTE_AUTHORITY',
  routing.includes('public static readonly string[] DefaultOrder = [CelarAi, Claude, OpenAi, Local];')
    && productionModule.includes('routing.LoadRouteAsync(CelarAiCapabilityCatalog.HelpAssistant')
    && productionModule.includes('providerConfiguration = helpRoute.ToPublicResponse()')
    && productionModule.includes('module064Authority = true'),
  'Module 011 readiness and answers expose the effective persisted Help route from Module 064'
);

check(
  'PRIVATE_ANSWER_PROMOTION_AND_QUALITY_GATES',
  all(systemService, [
    'PromotePrivateAnswer(',
    'answer.Answer.Confidence >= options.MinimumConfidence',
    'answer.CoverageScore >= options.MinimumEvidenceScore',
    'answer.CitationCoverageScore > 0m',
    'answer.Answer.CitationIds.Count > 0',
    'acceptedPrivateRagAnswer = privateAnswerPassedQualityGate',
    'BuildSources(\n                        relevantApis,\n                        toolResults,\n                        acceptedPrivateRagAnswer)',
    'finalAnswer = deterministic;',
    'PrivateCitations: acceptedPrivateRagAnswer?.Citations ?? []',
    'none of its answer text or citations were promoted'
  ])
    && !systemService.includes('!string.Equals(privateRagAnswer.Status, "completed"')
    && ragService.includes('options.MinimumEvidenceScore,'),
  'a local private answer is visible only with governed confidence, evidence, and citation coverage'
);

check(
  'ONE_AUTHORITATIVE_INTENT_PLAN',
  all(productionModule, [
    'return ToIntent(PulseAiSystemKnowledgeCatalog.Analyze(question));',
    'var intentPlan = ResolveIntentPlan(question, intent);',
    'ResolvedIntentContextItem] = intentPlan'
  ])
    && all(systemService, [
      'resolvedIntentValue as PulseAiSystemIntentPlan',
      'var plan = ApplyRequestControls(',
      'var requestedMode = plan.Mode;'
    ])
    && !systemService.includes('AlignIntentPlan(')
    && !systemService.includes('NormalizeMode(request.Mode, plan.Mode)'),
  'the v2 decision, tool plan, document policy, persistence mode, trust, and fallback purpose consume one backend-resolved intent plan'
);

check(
  'CLAUDE_OPENAI_SANITIZED_FALLBACK',
  routing.includes('DefaultOrder = [CelarAi, Claude, OpenAi, Local]')
    && all(externalPreparation, [
      'execution.ExternalProblemStatement',
      'sanitizedProblem.ExternalExecutionAuthorized',
      'Answer only as general, unverified guidance.',
      'Content: fixedCapsule.Capsule'
    ])
    && !externalPreparation.includes('request.UserPrompt')
    && all(systemService, [
      'BuildExternalProblemStatement(',
      'ContainsPrivateDocuments: privateDocumentContextRequested',
      'ContainsPeopleRecords: ContainsPeopleContext(plan)',
      'ContainsFinancialValues:',
      'external guidance is supplementary and unverified'
    ])
    && help.includes('Supplementary external guidance (unverified)'),
  'public fallback can address a closed server-owned topic but stays separate and receives no enterprise evidence'
);

check(
  'ANSWER_PREFERENCES_SERIALIZED',
  all(help, [
    'includeRepositoryContext: answerPreferences.includeRepositoryContext',
    'includeAssumptions: answerPreferences.includeAssumptions',
    'includeSourceCitations: answerPreferences.includeSourceCitations',
    'answerPreferenceSource: answerPreferences.preferenceSource'
  ])
    && all(productionModule, [
      'request.IncludeRepositoryContext',
      'request.IncludeAssumptions',
      'request.IncludeSourceCitations',
      'ApplyAnswerPreferences(result, request)'
    ]),
  'the same saved answer profile controls the unified client and v2 response presentation'
);

check(
  'MULTI_FILE_PRIVATE_UPLOAD',
  all(help, [
    'type="file" multiple',
    'new FormData()',
    "body.append('files', file, file.name)",
    'attachmentIds: selectedAttachmentIds.filter',
    '.pdf,.docx,.pptx,.xlsx,.txt,.md,.csv,.json,.xml,.html,.htm'
  ])
    && all(attachmentModule, [
      'ReadFormAsync(cancellationToken)',
      'form.Files',
      'StatusCodes.Status202Accepted',
      'rawDocumentSentToClaudeOrOpenAi = false'
    ])
    && !submit.includes('readAsDataURL')
    && !submit.includes('arrayBuffer('),
  'files use a separate multipart private-pipeline upload and chat JSON carries only selected attachment IDs'
);

check(
  'OWNER_VIEW_AS_AND_LIMITS',
  all(attachmentModule, [
    'identity.Value.Actual != identity.Value.Effective',
    'ViewAsForbidden()',
    'CanAttach(access)',
    'MaximumMultipartBodyBytes'
  ])
    && all(attachmentContracts, [
      'MaximumFilesPerRequest = 10',
      'MaximumActiveFilesPerConversation = 25',
      'MaximumActiveProcessingAttachmentsPerUser = 10',
      'MaximumActiveBytesPerConversation = 100L * 1024L * 1024L',
      'RetentionDays = 90',
      'restricted_conversation_attachment'
    ])
    && all(attachmentRepository, [
      'FOR UPDATE;',
      'effective_user_id = @user_id',
      'attachment.retention_until > NOW()',
      'document.is_active = TRUE'
    ])
    && all(systemService, [
      'attachmentIds.Length > 0 && actualUserId != effectiveUserId',
      'attachmentIds.Length > 0 && !access.CanAttachDocuments',
      'view_as_attachment_access_blocked',
      'attachment_permission_required'
    ])
    && all(productionModule, [
      'attachmentIds.Length > 0 && identity.Value.Actual != identity.Value.Effective',
      'attachmentIds.Length > 0 && !access.CanAttachDocuments'
    ]),
  'uploads are bounded, conversation-owner-only, inaccessible through View-As, and race-safe at creation'
);

check(
  'PRIVATE_PIPELINE_REUSE',
  all(attachmentService, [
    'PulseAiPrivateDocumentPipelinePolicy.SupportedExtensions',
    'ProjectPulseUploadStorage.InspectProductionReadiness()',
    'genericPreScanAttestationAcceptedForDirectUploads = false',
    'if (!runtime.ClamAvConfigured)',
    'A live private ClamAV scanner is required for direct Celar AI chat uploads',
    '_runtimeRepository.EnqueueAsync(',
    'CopyBoundedAsync(',
    'EnsureConfined(root, finalPath)'
  ])
    && all(services, [
      'CelarAiConversationAttachmentRepository',
      'CelarAiConversationAttachmentService',
      'AddHostedService<CelarAiConversationAttachmentRetentionWorker>()'
    ])
    && all(runtime, [
      'scan.Infected',
      'DeleteQuarantinedConversationAttachment(source)',
      'quarantine_storage_path_rejected'
    ])
    && all(retentionWorker, [
      'PurgeExpiredAsync(stoppingToken)',
      'TimeSpan.FromMinutes(15)',
      'without logging attachment paths or content'
    ])
    && all(attachmentRepository, [
      'ClaimPurgeCandidatesAsync(',
      'FOR UPDATE OF attachment SKIP LOCKED',
      'storage_purged_at IS NULL',
      'RecordStoragePurgeAsync('
    ])
    && all(attachmentService, [
      'PurgeOrphanFilesAsync(root, cancellationToken)',
      'AttributesToSkip = FileAttributes.ReparsePoint'
    ]),
  'attachments reuse shared persistent storage, scanning, extraction/OCR, indexing, and remove infected chat files'
);

check(
  'TWO_STAGE_RETRIEVAL_AUTHORIZATION',
  [retrieval, reauthorization].every((source) => all(source, [
    'pulse_ai_conversation_attachments',
    'attachment.pulse_ai_conversation_id = @conversation_id',
    'attachment.uploaded_by_user_id = @user_id',
    'attachment.revoked_at IS NULL',
    'attachment.retention_until > NOW()',
    "document.upload_source = 'celar_ai_chat_attachment'",
    "conversation.status = 'active'"
  ]))
    && sourceResolver.includes('conversation.effective_user_id = @user_id')
    && sourceResolver.includes("attachment.retention_until > NOW()"),
  'conversation attachments are authorized at source resolution, retrieval, and prompt assembly'
);

check(
  'ATTACHMENT_SCOPE_AND_LEGACY_ROUTE_ISOLATION',
  all(retrievalService, [
    'query.AttachmentIds.Count > 0 && query.ActualUserId != query.EffectiveUserId',
    'query.AttachmentIds.Count > 0 && !access.CanAttachDocuments',
    'view_as_attachment_access_blocked',
    'attachment_permission_required'
  ])
    && all(ragModule, [
      'hasAttachments && identities.Value.Actual != identities.Value.Effective',
      'hasAttachments && !access.CanAttachDocuments'
    ])
    && retrieval.includes('WHERE @include_project_documents = TRUE')
    && reauthorization.includes('AND @include_project_documents = TRUE')
    && help.includes('includeAuthorizedProjectDocuments: answerPreferences.includeRepositoryContext')
    && ragService.includes('includeProjectDocuments: request.IncludeAuthorizedProjectDocuments')
    && systemService.includes('&& request.IncludeRepositoryContext')
    && !systemService.includes('|| (request.AttachmentIds?.Count ?? 0) > 0')
    && (projectWorkspace.match(/celar_ai_chat_attachment/g) || []).length >= 2
    && projectIntake.includes("COALESCE(upload_source, '') <> 'celar_ai_chat_attachment'")
    && securityHardening.includes("COALESCE(d.upload_source, '') <> 'celar_ai_chat_attachment'")
    && runtimeRepository.includes("COALESCE(d.upload_source, '') <> 'celar_ai_chat_attachment'"),
  'View-As and revoked permissions fail closed, selected files do not mix ambient project evidence, and legacy document routes cannot expose chat uploads'
);

check(
  'COMPLETE_PRIVATE_CONTENT_RETENTION',
  all(attachmentRepository, [
    'FinalizeStoragePurgeAsync(',
    'INSERT INTO pulse_ai_conversation_attachment_purge_audit',
    'UPDATE pulse_ai_conversation_messages message',
    'UPDATE pulse_ai_answer_feedback feedback',
    'UPDATE pulse_ai_answer_runs answer_run',
    'DELETE FROM pulse_ai_answer_citations citation',
    'DELETE FROM project_intake_documents document',
    'private_attachment_retention_purged'
  ])
    && !attachmentRepository.includes('DELETE FROM pulse_ai_answer_runs')
    && all(migrationTest, [
      'immutable_retrieval_event_preserved',
      'answer_run_content_redacted_in_place',
      'attachment_citations_removed',
      'attachment_feedback_redacted',
      'attachment_messages_redacted',
      'attachment_conversation_title_redacted',
      'uncited_attachment_answer_run_redacted_from_request_scope',
      'purge_removes_extracted_section_text',
      'purge_removes_chunks_and_embeddings',
      'conversation_deletable_after_content_purge'
    ]),
  'retention removes files, extracted text, embeddings, citations, messages, and feedback while preserving immutable content-free retrieval audit evidence'
);

check(
  'RETENTION_RACE_AND_IMMUTABLE_AUDIT',
  all(answerCompletion, [
    'FOR UPDATE OF attachment',
    'pulse_ai_conversation_attachment_purge_audit',
    'lockedAttachmentIds.Count != attachmentIds.Length',
    'if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)',
    'return false;'
  ])
    && all(messagePersistence, [
      'IReadOnlyCollection<Guid>? requiredAttachmentIds = null',
      'FOR UPDATE OF attachment',
      'pulse_ai_conversation_attachment_purge_audit',
      'lockedAttachmentIds.Count != attachmentIds.Length',
      'return (Guid.Empty, 0);'
    ])
    && (systemService.match(/requiredAttachmentIds: attachmentIds/g) || []).length >= 3
    && (systemService.match(/assistantMessage\.MessageId == Guid\.Empty/g) || []).length >= 2
    && all(systemService, [
      'userMessage.MessageId == Guid.Empty',
      'assistantMessage.MessageId == Guid.Empty',
      'private_attachment_retention_purged',
      'persisted && assistantMessage.MessageId != Guid.Empty'
    ])
    && all(feedbackPersistence, [
      'BeginTransactionAsync(cancellationToken)',
      'FOR UPDATE;',
      'actual_user_id = @actual_user_id',
      'effective_user_id = @effective_user_id',
      'private_attachment_retention_purged',
      'transaction.CommitAsync(cancellationToken)'
    ])
    && all(retrievalEventPersistence, [
      'missingEvidenceCount = retrieval.MissingEvidence.Count',
      'conflictCount = retrieval.Conflicts.Count',
      'displayStringsLogged = false',
      'attachmentFileNamesLogged = false'
    ])
    && !retrievalEventPersistence.includes('missingEvidence = retrieval.MissingEvidence')
    && !retrievalEventPersistence.includes('conflicts = retrieval.Conflicts')
    && attachmentRepository.includes("SET title = 'Celar AI private attachment conversation'")
    && all(migration, [
      'pulse_ai_072_block_purged_answer_resurrection',
      'Purged Celar AI attachment answer content cannot be restored.',
      'pulse_ai_072_guard_purged_answer_feedback',
      'Feedback cannot restore purged Celar AI attachment content.'
    ])
    && all(migrationTest, [
      'immutable_retrieval_event_excludes_private_filename',
      'attachment_conversation_title_redacted',
      'purged_answer_content_cannot_be_resurrected',
      'purged_answer_rejects_late_feedback',
      'purged_answer_feedback_cannot_be_restored'
    ]),
  'purge, completion, messages, and feedback serialize before retention; late writes fail closed, titles are neutralized, and immutable events contain only codes/counts'
);

check(
  'MIGRATION_AND_GUARDED_ROLLBACK',
  all(migration, [
    "'072_celar_ai_conversation_attachments'",
    'CREATE TABLE IF NOT EXISTS pulse_ai_conversation_attachments',
    'REFERENCES pulse_ai_conversations(pulse_ai_conversation_id) ON DELETE RESTRICT',
    'REFERENCES app_users(user_id) ON DELETE RESTRICT',
    'ck_project_intake_documents_origin_owner',
    "'ATTACH_CELAR_AI_CHAT_DOCUMENTS'",
    "ask_permission.permission_code = 'ASK_PULSE_AI_SYSTEM_INTELLIGENCE'",
    'Celar AI attachment ownership evidence is immutable.',
    'Celar AI conversation ownership cannot change while private attachments exist.',
    'Celar AI chat documents require a governed purge-audit tombstone before deletion.',
    'storage_purged_at TIMESTAMPTZ NULL',
    'ix_pulse_ai_conversation_attachments_pending_purge'
  ])
    && all(rollback, [
      'Refusing migration 072 rollback',
      'WHERE project_intake_request_id IS NULL',
      'ALTER COLUMN project_intake_request_id SET NOT NULL'
    ])
    && all(migrationTest, [
      'ownerless_non_chat_document_rejected',
      'immutable_attachment_ownership',
      'conversation_attachment_owner_reassignment_rejected',
      'document_delete_requires_governed_purge',
      'conversation_delete_requires_attachment_cleanup',
      'guarded_rollback_with_live_attachment',
      'CELAR_AI_CONVERSATION_ATTACHMENTS_MIGRATION_072=PASS'
    ]),
  'one additive schema change owns attachment metadata, permission inheritance, immutable ownership, and safe rollback'
);

const failed = checks.filter((item) => !item.condition);
if (failed.length) {
  console.error(`CELAR_AI_UNIFIED_CHAT_ATTACHMENTS=FAILED (${failed.length}/${checks.length})`);
  process.exit(1);
}

console.log(`CELAR_AI_UNIFIED_CHAT_ATTACHMENTS=PASSED (${checks.length}/${checks.length})`);
