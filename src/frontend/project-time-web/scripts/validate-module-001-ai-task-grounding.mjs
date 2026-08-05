import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const webRoot = path.resolve(scriptDirectory, '..');
const repositoryRoot = path.resolve(webRoot, '..', '..', '..');
const readRepository = (...parts) => fs.readFileSync(path.join(repositoryRoot, ...parts), 'utf8');
const readWeb = (...parts) => fs.readFileSync(path.join(webRoot, ...parts), 'utf8');

const app = readWeb('src', 'App.jsx');
const timerAssistant = readWeb('src', 'module001', 'TimesheetAiDescriptionAssistant.jsx');
const timerPortalV2 = readWeb('src', 'module001', 'TimesheetEnhancementPortalV2.jsx');
const timerView = readWeb('src', 'module001', 'TimesheetTimerView.jsx');
const packageJson = readWeb('package.json');

const backendRoot = ['src', 'backend', 'ProjectTime.Api'];
const readBackend = (...parts) => readRepository(...backendRoot, ...parts);
const program = readBackend('Program.cs');
const timesheetResolver = readBackend('Ai', 'ProjectPulseAiTimesheetContextResolver.cs');
const serviceRegistration = readBackend('Ai', 'ProjectPulseAiServiceCollectionExtensions.cs');
const timesheetSuggestion = readBackend('ProjectPulseAiTimeEntrySuggestionService.cs');
const capabilityRouting = readBackend('Ai', 'CelarAiCapabilityRouting.cs');
const legacyRouter = readBackend('Ai', 'ProjectPulseAiRouter.cs');
const providerHealth = readBackend('Ai', 'ProjectPulseAiHealthRegistry.cs');
const providerContracts = readBackend('Ai', 'ProjectPulseAiContracts.cs');
const privateRagContracts = readBackend('Ai', 'PulseAiPrivateRagContracts.cs');
const privateRagService = readBackend('Ai', 'PulseAiPrivateRagService.cs');
const privateRagRepository = readBackend('Ai', 'PulseAiPrivateRagRepository.cs');
const privateRetrieval = readBackend('Ai', 'PulseAiPrivateRetrievalService.cs');
const groundingContracts = readBackend('Ai', 'PulseAiDeepIntelligenceContracts.cs');
const groundingService = readBackend('Ai', 'PulseAiDocumentGroundingService.cs');
const groundingGenerated = readBackend('PulseAiDocumentGroundingService.g.cs');
const privatePipelineContracts = readBackend('Ai', 'PulseAiPrivateDocumentPipelineContracts.cs');
const uploadStorage = readBackend('Ai', 'ProjectPulseUploadStorage.cs');
const projectIntake = readBackend('Modules', 'ProjectIntakeModule.cs');
const brandModule = readBackend('Modules', 'CelarAiBrandModule.cs');
const secretStore = readBackend('Ai', 'ProjectPulseAiSecretStore.cs');
const aiDatabaseConnection = readBackend('Ai', 'ProjectPulseAiDatabaseConnection.cs');
const buildTransforms = readBackend('Directory.Build.targets');

const checks = [];

function check(name, condition, detail) {
  checks.push({ name, condition });
  console.log(`${name}=${condition ? 'PASSED' : 'FAILED'}${detail ? ` — ${detail}` : ''}`);
}

function containsAll(source, values) {
  return values.every((value) => source.includes(value));
}

function section(source, startMarker, endMarker) {
  const start = source.indexOf(startMarker);
  if (start < 0) return '';
  const end = source.indexOf(endMarker, start + startMarker.length);
  return end < 0 ? source.slice(start) : source.slice(start, end);
}

function ordered(source, values) {
  let cursor = -1;
  for (const value of values) {
    cursor = source.indexOf(value, cursor + 1);
    if (cursor < 0) return false;
  }
  return true;
}

const requestContract = section(
  program,
  'record ProjectPulseAiTimeEntrySuggestionRequest(',
  'record ProjectPulseAiTimeEntrySuggestionResult('
);
const suggestionEndpoint = section(
  program,
  'app.MapPost("/api/timesheets/ai-description-suggestions"',
  'app.MapGet("/api/assignments/open-tasks"'
);
const openAssignedTasksLoader = section(
  program,
  'static async Task<List<object>> LoadOpenAssignedProjectTasksAsync(',
  'static async Task<Guid> GetOrCreateDevelopmentUserIdAsync('
);
const timesheetFeatureResolver = section(
  capabilityRouting,
  'public static string ResolveTimesheetFeature(',
  'public static IReadOnlyList<string> ValidateTargets('
);
const remotePrompt = section(
  timesheetSuggestion,
  'private static string BuildRemotePromptWithoutPrivateDocuments(',
  '\n}'
);
const privateTimesheetContract = section(
  privateRagContracts,
  'public sealed record PulseAiPrivateTimesheetRequest(',
  'public sealed record PulseAiPrivateFlowHiveRequest('
);
const groundingInputContract = section(
  groundingContracts,
  'public sealed record PulseAiTimesheetGroundingInput(',
  'public sealed record PulseAiFlowHiveGroundingInput('
);
const privateAttemptRecorder = section(
  capabilityRouting,
  'public void RecordAlreadyExecutedPrivateAttempt(',
  'public Task<ProjectPulseAiRouteResult> GenerateExternalAsync('
);
const targetDecisionMerge = section(
  timesheetSuggestion,
  'private static IReadOnlyList<ProjectPulseAiTargetDecision>? MergeTargetDecisions(',
  'private static string BuildLocalSuggestion('
);

// The engineer selects a visible row; canonical identifiers travel invisibly with that row.
check(
  'MODULE001_AI_FRONTEND_PROJECT_TASK_IDS',
  containsAll(app, [
    'assignmentId: task.assignmentId ?? task.projectAssignmentId ?? null',
    'projectId: task.projectId',
    'taskId: task.taskId'
  ]),
  'assigned task rows retain assignment, project, and task identifiers'
);
check(
  'MODULE001_AI_FRONTEND_CATEGORY_ID',
  app.includes('nonProjectTimeCategoryId: category.nonProjectTimeCategoryId')
    && app.includes('category.categoryId'),
  'non-project rows retain the API category identifier'
);
check(
  'MODULE001_AI_FRONTEND_SAVED_ENTRY_ID',
  app.includes('timeEntryId: entry.timeEntryId ?? entry.id ?? null'),
  'saved cells retain their authoritative time-entry identifier'
);
check(
  'MODULE001_AI_FRONTEND_REQUEST_IDS',
  containsAll(app, [
    'timeEntryId: selectedEntry.timeEntryId ?? null',
    'assignmentId: isProjectTask ? (selectedRow.assignmentId ?? null) : null',
    'projectId: isProjectTask ? (selectedRow.projectId ?? null) : null',
    'taskId: isProjectTask ? (selectedRow.taskId ?? null) : null',
    'nonProjectTimeCategoryId: isProjectTask ? null'
  ]),
  'the standard Timesheet AI request sends canonical hidden IDs automatically'
);
check(
  'MODULE001_AI_TIMER_REQUEST_IDS',
  containsAll(timerAssistant, [
    'assignmentId: nonProject ? null : (assignmentId || null)',
    'projectId: nonProject ? null : (primaryTarget.projectId || null)',
    'taskId: nonProject ? null : (primaryTarget.taskId || null)',
    'nonProjectTimeCategoryId: nonProject ? (nonProjectTimeCategoryId || null) : null'
  ]),
  'the timer assistant uses the same hidden-identity contract'
);
check(
  'MODULE001_AI_TIMER_PROVIDER_LABEL',
  timerAssistant.includes("celar_ai: 'Celar AI'")
    && app.includes("celar_ai: 'Celar AI'"),
  'private-model responses are labelled separately from local templates'
);
check(
  'MODULE001_AI_V2_CATEGORY_ID_AUTHORITY',
  ordered(timerPortalV2, [
    'if (UUID_PATTERN.test(categoryId))',
    'if (CATEGORY_CODE_PATTERN.test(code))'
  ])
    && timerPortalV2.includes('nonProjectTimeCategoryId: categoryId')
    && timerView.includes('nonProjectTimeCategoryId: timer?.nonProjectCategoryId'),
  'the optional V2 timer portal prefers the opaque category ID over its display code'
);

// Program accepts the identifiers and resolves them before any model or template executes.
check(
  'MODULE001_AI_PROGRAM_ID_CONTRACT',
  containsAll(requestContract, [
    'Guid? TimeEntryId',
    'Guid? AssignmentId',
    'Guid? ProjectId',
    'Guid? TaskId',
    'Guid? NonProjectTimeCategoryId',
    'string? CustomerName'
  ]),
  'the API request contract accepts every canonical Timesheet identity'
);
check(
  'MODULE001_AI_RESOLVER_PRECEDES_GENERATION',
  ordered(suggestionEndpoint, [
    'contextResolver.ResolveAsync(',
    'if (!contextResolution.Succeeded || contextResolution.Request is null)',
    'aiService.GenerateAsync(contextResolution.Request'
  ]),
  'unauthorized or ambiguous context fails before AI generation'
);
check(
  'MODULE001_AI_API_EXPOSES_HIDDEN_IDS',
  containsAll(program, [
    'assignmentId = reader.GetGuid(O("assignment_id"))',
    'projectId = reader.GetGuid(O("project_id"))',
    'taskId = reader.GetGuid(O("task_id"))',
    'categoryId = reader.GetGuid(0)',
    'timeEntryId = reader.GetGuid(0)'
  ]),
  'task, category, and saved-entry APIs expose IDs without engineer input'
);
check(
  'MODULE001_AI_OPEN_TASKS_WEEK_SCOPE',
  containsAll(openAssignedTasksLoader, [
    'pa.effective_start_date <= @week_end',
    'pa.effective_end_date IS NULL OR pa.effective_end_date >= @week_start',
    'command.Parameters.AddWithValue("week_start", weekStart)',
    'command.Parameters.AddWithValue("week_end", weekEnd)'
  ]),
  'the visible task picker uses inclusive assignment overlap for the requested Timesheet week'
);
check(
  'MODULE001_AI_RESOLVER_REGISTERED',
  serviceRegistration.includes('AddSingleton<ProjectPulseAiTimesheetContextResolver>()'),
  'the authoritative resolver is available to the endpoint'
);

// Authorization is deliberately fail-closed and server-owned.
check(
  'MODULE001_AI_RESOLVER_USER_DATE_SCOPE',
  containsAll(timesheetResolver, [
    'te.user_id = @user_id',
    'te.work_date = @work_date',
    'pa.user_id = @user_id',
    'pa.effective_start_date <= @work_date',
    'pa.effective_end_date IS NULL OR pa.effective_end_date >= @work_date'
  ]),
  'saved entries and assignments are scoped to user and work date'
);
check(
  'MODULE001_AI_RESOLVER_FAIL_CLOSED_CODES',
  containsAll(timesheetResolver, [
    'time_entry_not_authorized',
    'project_task_assignment_not_authorized',
    'non_project_category_not_authorized',
    'work_item_identity_mismatch',
    'project_task_assignment_ambiguous',
    'work_item_identity_required'
  ]),
  'missing, mismatched, unauthorized, and ambiguous work identity cannot fall through'
);
check(
  'MODULE001_AI_GROUNDING_FAIL_CLOSED_SCOPE',
  containsAll(groundingService, [
    'CanAccessProjectAsync(',
    'project_outside_effective_user_scope',
    'task_or_assignment_not_resolved',
    'pa.user_id = @user_id'
  ]),
  'private document retrieval repeats effective-user project and assignment authorization'
);
check(
  'MODULE001_AI_FACTUAL_NOTE_REQUIRED',
  suggestionEndpoint.includes('roughNoteCharacters < 12')
    && suggestionEndpoint.includes('roughNoteFactualCharacters < 8')
    && suggestionEndpoint.includes('more_detail_required'),
  'generation cannot fabricate work from an empty or punctuation-only row selection'
);
check(
  'MODULE001_AI_INPUT_LENGTH_BOUND',
  suggestionEndpoint.includes('roughNote.Length > 4_000')
    && suggestionEndpoint.includes('cannot exceed 4,000 characters')
    && timesheetSuggestion.includes('MaximumEngineerNoteCharacters = 4_000')
    && timesheetSuggestion.includes('var note = BoundedEngineerNote(value)')
    && app.includes('maxLength={4000}')
    && timerView.includes('maxLength={4000}'),
  'direct callers cannot send an unbounded note through sanitization or provider routing'
);

// Non-project must be an exact route, never a substring match on the word "project".
check(
  'MODULE001_AI_EXACT_NONPROJECT_ROUTE',
  containsAll(timesheetFeatureResolver, [
    'normalizedRowType is "nonproject" or "non_project" or "non_project_time"',
    'or "category" or "categorycode" or "category_code"',
    'return TimesheetNonProject;'
  ])
    && !timesheetFeatureResolver.includes('row.Contains("project"')
    && !timesheetFeatureResolver.includes('normalizedRowType.Contains("project"'),
  'nonProject and non_project cannot be misclassified as project tasks'
);
check(
  'MODULE001_AI_EXACT_ROUTE_ORDER',
  ordered(timesheetFeatureResolver, [
    'normalizedRowType is "nonproject"',
    'normalizedRowType is "service_request"',
    'normalizedRowType is "project"'
  ]),
  'explicit non-project, service-request, and project-task cases run before label heuristics'
);
check(
  'MODULE001_AI_FRONTEND_EXACT_NONPROJECT',
  containsAll(timerAssistant, [
    "targetType === 'categorycode'",
    "targetType === 'category_code'",
    "targetType === 'nonproject'",
    "targetType === 'non_project'"
  ])
    && app.includes("rowType: isProjectTask ? (isServiceRequest ? 'service_request' : 'project_task') : 'non_project'"),
  'both Timesheet entry surfaces emit canonical route types'
);

// IDs, not display text, anchor SOW/GSD retrieval and task-level grounding.
check(
  'MODULE001_AI_PRIVATE_RAG_ID_CONTRACT',
  containsAll(privateTimesheetContract, [
    'Guid? ProjectId = null',
    'Guid? TaskId = null',
    'Guid? AssignmentId = null'
  ])
    && containsAll(privateRagContracts, [
      'Guid? ProjectId,',
      'Guid? TaskId,',
      'Guid? AssignmentId,'
    ]),
  'private Timesheet and retrieval contracts carry canonical identities'
);
check(
  'MODULE001_AI_PRIVATE_RAG_IDS_THREADED',
  containsAll(timesheetSuggestion, [
    'ProjectId: request.ProjectId',
    'TaskId: request.TaskId',
    'AssignmentId: request.AssignmentId'
  ])
    && containsAll(privateRagService, [
      'projectId: request.ProjectId',
      'taskId: request.TaskId',
      'assignmentId: request.AssignmentId'
    ])
    && containsAll(privateRetrieval, [
      'query.ProjectId',
      'query.TaskId',
      'query.AssignmentId'
    ]),
  'resolved IDs reach private RAG query construction and retrieval'
);
check(
  'MODULE001_AI_PRIVATE_RAG_ID_SQL',
  containsAll(privateRagRepository, [
    '@project_id IS NULL OR p.project_id = @project_id',
    'identity_task.task_id = @task_id',
    'identity_assignment.project_assignment_id = @assignment_id',
    'command.Parameters.AddWithValue("project_id", query.ProjectId'
  ]),
  'private RAG resolves projects, tasks, and assignments by identifier'
);
check(
  'MODULE001_AI_GROUNDING_ID_CONTRACT',
  containsAll(groundingInputContract, [
    'Guid? ProjectId = null',
    'Guid? TaskId = null',
    'Guid? AssignmentId = null'
  ])
    && containsAll(groundingService, [
      'projectId: input.ProjectId',
      'taskId: input.TaskId',
      'assignmentId: input.AssignmentId',
      '@project_id IS NULL OR p.project_id = @project_id',
      'identity_task.task_id = @task_id',
      'identity_assignment.project_assignment_id = @assignment_id'
    ]),
  'document grounding resolves the same canonical project/task/assignment'
);

// Raw private document context never becomes an external-provider prompt.
check(
  'MODULE001_AI_CONFIGURED_ROUTE_AUTHORITY',
  containsAll(timesheetSuggestion, [
    'var capability = CelarAiCapabilityCatalog.ResolveTimesheetFeature(',
    'var privateTargetFirst = await _router.IsFirstTargetAsync(',
    'var privateRag = privateTargetFirst',
    'var grounding = privateTargetFirst',
    '_router.GenerateAsync('
  ])
    && containsAll(capabilityRouting, [
      'public async Task<bool> IsFirstTargetAsync(',
      'var route = await _store.LoadRouteAsync(',
      'route.Targets.FirstOrDefault()'
    ])
    && legacyRouter.includes('public Task<bool> IsFirstTargetAsync('),
  'private SOW inference runs first only when Celar AI is first in the persisted Module 064 route'
);
check(
  'MODULE001_AI_PRIVATE_TARGET_NOT_RETRIED',
  containsAll(timesheetSuggestion, [
    'var privateCelarAttempted = privateTargetFirst && privateRag?.Citations.Count > 0',
    '_router.RecordAlreadyExecutedPrivateAttempt(',
    'skipPrivateTarget: privateCelarAttempted',
    'MergeTargetDecisions(privateRagDecision, routed.TargetDecisions)'
  ])
    && containsAll(capabilityRouting, [
      'bool skipPrivateTarget,',
      'if (skipPrivateTarget && target == CelarAiCapabilityTargets.CelarAi)',
      'private_target_skipped_by_caller'
    ])
    && containsAll(targetDecisionMerge, [
      '.Where(item =>',
      'item.Target, CelarAiCapabilityTargets.CelarAi',
      'item.ReasonCode, "private_target_skipped_by_caller"'
    ]),
  'a document-backed Celar attempt is recorded once before routing continues to later targets'
);
check(
  'MODULE001_AI_PRIVATE_ATTEMPT_TELEMETRY_EXACTLY_ONCE',
  (timesheetSuggestion.match(/_router\.RecordAlreadyExecutedPrivateAttempt\(/g) ?? []).length === 1
    && containsAll(privateAttemptRecorder, [
      '_health.RecordSuccess(',
      '_health.RecordFailure(',
      '_assurance.Record(',
      'ProjectPulseAiOutcomes.Success',
      'ProjectPulseAiOutcomes.Unavailable'
    ])
    && containsAll(providerHealth, [
      'TryGetRecordableState(provider, out var state)',
      'string.Equals(provider, "celar_ai"',
      'ProviderState.PrivateTarget'
    ])
    && legacyRouter.includes('public void RecordAlreadyExecutedPrivateAttempt('),
  'document-backed private execution records one router-owned health and assurance outcome'
);
check(
  'MODULE001_AI_EXTERNAL_PROMPT_EXCLUDES_PRIVATE_DOCUMENTS',
  timesheetSuggestion.includes('BuildRemotePromptWithoutPrivateDocuments(request)')
    && containsAll(remotePrompt, [
      'No restricted source material, commercial detail, architecture detail, or extracted evidence is included',
      'No customer name, project name, project code, task name, task code, person name, internal identifier, work date, or location is included as structured context.',
      'Use only the backend-derived identity-free activity categories and generic work classification below.',
      'Backend-derived identity-free activity categories:',
      'BuildPurposeBuiltExternalActivityFacts(request.CurrentDescription)'
    ])
    && !/grounding\.|privateRag\.|ContextSummary|ScopeThemes/.test(remotePrompt)
    && !remotePrompt.includes('BoundedEngineerNote(request.CurrentDescription)')
    && !/request\.(?:CustomerName|ProjectCode|ProjectName|TaskCode|TaskName|RowLabel|CategoryCode|WorkDate)/.test(remotePrompt),
  'the remote prompt contains only server-authored activity categories and generic work classification'
);
check(
  'MODULE001_AI_EXTERNAL_SANITIZER_BOUNDARY',
  containsAll(buildTransforms, [
    'ContainsPrivateDocuments: ContainsPrivateDocumentMarkers(request.CurrentDescription)',
    'ContainsCustomerIdentity: !string.IsNullOrWhiteSpace(request.CustomerName)',
    'AllowSanitizedExternalAssistance: true',
    'SensitiveTerms: new[] { request.CustomerName',
    'request.TaskCode ?? string.Empty',
    'IdentityTerms: new[] { request.CustomerName ?? string.Empty }',
    'PurposeBuiltDeidentifiedInput: true',
    'DeidentifiedFactsAvailable: HasPurposeBuiltExternalActivityFacts(request.CurrentDescription)'
  ])
    && containsAll(capabilityRouting, [
      'PrepareExternalRequest(',
      '_sanitizer.SanitizeForExecution(',
      'UserPrompt = sanitized.SanitizedCapsule'
    ]),
  'external targets receive a sanitizer-produced capsule, never the private evidence payload'
);
check(
  'MODULE001_AI_EXTERNAL_FREE_TEXT_GUARD',
  timesheetSuggestion.includes('ContainsPrivateDocumentMarkers(string? value)')
    && containsAll(timesheetSuggestion, [
      'statement\\s+of\\s+work',
      'global\\s+solution\\s+design',
      'rate\\s*card'
    ])
    && timesheetResolver.includes('CustomerName = reader.GetString(O("customer_name"))')
    && timesheetResolver.includes('CustomerName = row.CustomerName'),
  'document/commercial markers block external routing and the resolved customer name is explicitly redacted'
);
check(
  'MODULE001_AI_PRIVATE_DOCUMENT_POLICY',
  privatePipelineContracts.includes('raw_document_extraction_chunks_and_embeddings_never_sent_to_external_provider')
    && timesheetSuggestion.includes('no private document text was sent to Claude or OpenAI'),
  'the document pipeline and response evidence state the same privacy boundary'
);

// Provider decisions explain why Celar/Claude/OpenAI did or did not run.
check(
  'MODULE001_AI_PROVIDER_DECISION_CONTRACT',
  providerContracts.includes('IReadOnlyList<ProjectPulseAiTargetDecision>? TargetDecisions')
    && containsAll(providerContracts, [
      'public sealed record ProjectPulseAiTargetDecision(',
      'string Target,',
      'string Outcome,',
      'string ReasonCode'
    ])
    && requestContract.length > 0
    && program.includes('targetDecisions = result.TargetDecisions ?? []'),
  'target, outcome, and reason code are preserved for every route decision'
);
check(
  'MODULE001_AI_PROVIDER_DECISION_CODES',
  containsAll(capabilityRouting, [
    'celar_ai_private_model_not_configured',
    'celar_ai_private_model_disabled',
    'provider_not_registered',
    'provider_circuit_open',
    'sanitized_external_policy_disabled',
    'sanitized_external_request_blocked',
    'local_fallback',
    'generation_succeeded'
  ]),
  'configuration, health, privacy, success, and fallback paths have stable diagnostics'
);
check(
  'MODULE001_AI_PRIVATE_PROVIDER_ATTRIBUTION',
  containsAll(timesheetSuggestion, [
    'var usedPrivateInference = UsedPrivateInference(privateRag)',
    'if (usedPrivateInference)',
    'CelarAiCapabilityTargets.CelarAi',
    'private_document_grounding_succeeded',
    'private_context_withheld_from_external_route',
    'PrivateProviderDecision(',
    'private_model_completed',
    'deterministic_private_fallback',
    'routed.TargetDecisions'
  ]),
  'Celar AI is reported only for genuine private inference; deterministic scaffolds continue through the configured route'
);
check(
  'MODULE001_AI_SAFETY_REFUSAL_PRESERVED',
  timesheetSuggestion.includes('routed.Outcome == ProjectPulseAiOutcomes.Refusal')
    && timesheetSuggestion.includes('? string.Empty')
    && suggestionEndpoint.includes('"ai_suggestion_refused"')
    && capabilityRouting.includes('"provider_safety_refusal"')
    && capabilityRouting.includes('No later target was attempted.'),
  'a provider safety refusal cannot be converted into local prose under the refusing provider name'
);

// Customer-visible prose is detailed when supported and mechanically sentence-safe.
check(
  'MODULE001_AI_EVIDENCE_BASED_PROSE',
  timesheetSuggestion.includes('detailed, accurate, evidence-based, customer-facing')
    && timesheetSuggestion.includes('backend-derived activity categories below as the only factual evidence')
    && timesheetSuggestion.includes('never imply that you saw the Engineer\'s note')
    && privateRagService.includes("rough note is the primary evidence of work actually performed")
    && privateRagService.includes('cannot prove unreported work occurred'),
  'the engineer note remains evidence of work; SOW scope cannot fabricate activity'
);
check(
  'MODULE001_AI_DETAIL_TARGET',
  remotePrompt.includes('two to four sentences and approximately 75 to 150 words')
    && privateRagService.includes('two to four sentences and approximately 75 to 150 words')
    && remotePrompt.includes('Never add generic filler merely to reach the target length'),
  'supported responses target customer-ready detail without filler'
);
check(
  'MODULE001_AI_LENGTH_AND_SENTENCE_GUARDS',
  containsAll(timesheetSuggestion, [
    'MaximumSuggestionCharacters = 1_500',
    "LastIndexOfAny(['.', '!', '?'])",
    'return AsSentence(cleaned)',
    "sentence.EndsWith('.') || sentence.EndsWith('!') || sentence.EndsWith('?')",
    ': sentence + "."',
    'FinalizeCustomerSuggestion(',
    'Regex.Split(cleaned, "(?<=[.!?])\\\\s+")',
    '.Take(4)',
    'if (sentences.Count < 2)'
  ]),
  'all providers and fallbacks are normalized to two-to-four bounded, terminally punctuated sentences'
);
check(
  'MODULE001_AI_UNSUPPORTED_DETAIL_REFUSAL',
  timesheetSuggestion.includes('Additional factual detail about the work performed is required')
    && privateRagService.includes('additional factual work detail is required instead of inventing activity'),
  'insufficient evidence produces a factual-detail request instead of invented customer prose'
);

// Every uploader and the private document pipeline resolve one canonical storage root.
check(
  'MODULE001_AI_UNIFIED_UPLOAD_ROOT',
  containsAll(uploadStorage, [
    'CanonicalEnvironmentVariable = "PROJECTPULSE_UPLOAD_ROOT"',
    'LegacyEnvironmentVariable = "PROJECT_PULSE_UPLOAD_ROOT"',
    'DefaultRoot = "/opt/project-time-platform/uploads"',
    'public static string ResolveRoot()'
  ])
    && program.includes('return ProjectPulseUploadStorage.ResolveRoot();')
    && projectIntake.includes('ProjectPulseUploadStorage.ResolveRoot()')
    && privatePipelineContracts.includes('UploadRoot: ProjectPulseUploadStorage.ResolveRoot()'),
  'legacy uploads and private ingestion converge on one canonical root resolver'
);
check(
  'MODULE001_AI_NO_PARALLEL_PROGRAM_UPLOAD_ENV',
  !program.includes('Environment.GetEnvironmentVariable("PROJECT_PULSE_UPLOAD_ROOT")')
    && !program.includes('Environment.GetEnvironmentVariable("PROJECTPULSE_UPLOAD_ROOT")'),
  'Program cannot silently choose a different upload root from private ingestion'
);
check(
  'MODULE001_AI_CONFIGURATION_DATABASE_FALLBACK',
  containsAll(aiDatabaseConnection, [
    'ConnectionStrings__DefaultConnection',
    'PTP_DB_HOST',
    'PTP_DB_PORT',
    'PTP_DB_NAME',
    'PTP_DB_USER',
    'PTP_DB_PASSWORD',
    'NpgsqlConnectionStringBuilder'
  ])
    && secretStore.includes('ProjectPulseAiDatabaseConnection.Resolve()')
    && capabilityRouting.includes('ProjectPulseAiDatabaseConnection.Resolve()'),
  'provider secrets and capability routes use the API database contract in Container Apps'
);
check(
  'MODULE001_AI_PERSISTED_PRIVATE_PROFILE_READINESS',
  brandModule.includes('CelarAiPrivateModelRuntime.Apply(PulseAiPrivateRagOptions.FromEnvironment())'),
  'the provider bridge reports the persisted Module 064 private profile rather than environment-only defaults'
);
check(
  'MODULE001_AI_GROUNDING_GENERATED_CONVERGENCE',
  groundingGenerated === groundingService.replaceAll(
    'roles: access.RoleCodes,',
    'roles: access.RoleCodes.OrderBy(value => value).ToArray(),'
  ),
  'the tracked compiler copy exactly matches the canonical ID-aware grounding source transform'
);

// Generated compile transforms must never relabel a local template as Celar AI.
check(
  'MODULE001_AI_NO_BLIND_LOCAL_TO_CELAR_SED',
  !/ProjectPulseAiProviders\.Local\/s\/\/CelarAiCapabilityTargets\.CelarAi/.test(buildTransforms)
    && !/sed[^\n]*ProjectPulseAiProviders\.Local[^\n]*CelarAiCapabilityTargets\.CelarAi/.test(buildTransforms),
  'provider attribution is explicit runtime logic, not a source rewrite'
);
check(
  'MODULE001_AI_BUILD_GATE',
  packageJson.includes('"validate:module001-ai-task-grounding": "node ./scripts/validate-module-001-ai-task-grounding.mjs"')
    && packageJson.includes('npm run validate:module001-ai-task-grounding'),
  'the focused contract runs in the normal frontend production build'
);

const failed = checks.filter(({ condition }) => !condition);
console.log(`\nMODULE001_AI_TASK_GROUNDING_CHECKS=${checks.length}`);
console.log('MODULE001_AI_IDENTITY=SERVER_RESOLVED_FAIL_CLOSED');
console.log('MODULE001_AI_DOCUMENTS=PRIVATE_ID_GROUNDED');
console.log('MODULE001_AI_EXTERNAL_CONTEXT=SANITIZED_NON_DOCUMENT_ONLY');
console.log('MODULE001_AI_RESPONSE=EVIDENCE_BASED_CUSTOMER_READY');
console.log(`MODULE001_AI_TASK_GROUNDING=${failed.length === 0 ? 'PASSED' : 'FAILED'}`);

if (failed.length > 0) {
  console.error(`MODULE001_AI_TASK_GROUNDING_FAILURES=${failed.map(({ name }) => name).join(',')}`);
  process.exitCode = 1;
}
