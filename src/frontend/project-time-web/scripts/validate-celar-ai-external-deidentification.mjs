import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const webRoot = path.resolve(scriptDirectory, '..');
const repositoryRoot = path.resolve(webRoot, '..', '..', '..');
const read = (...parts) => fs.readFileSync(path.join(repositoryRoot, ...parts), 'utf8');

const sanitizer = read('src', 'backend', 'ProjectTime.Api', 'Ai', 'PulseAiEscalationSanitizer.cs');
const routing = read('src', 'backend', 'ProjectTime.Api', 'Ai', 'CelarAiCapabilityRouting.cs');
const privateModel = read('src', 'backend', 'ProjectTime.Api', 'Ai', 'PulseAiPrivateModelClient.cs');
const privateRag = read('src', 'backend', 'ProjectTime.Api', 'Ai', 'PulseAiPrivateRagService.cs');
const timesheet = read('src', 'backend', 'ProjectTime.Api', 'ProjectPulseAiTimeEntrySuggestionService.cs');
const enterpriseContracts = read('src', 'backend', 'ProjectTime.Api', 'Ai', 'CelarAiEnterprisePlatformContracts.cs');
const enterpriseExternal = read('src', 'backend', 'ProjectTime.Api', 'Ai', 'CelarAiExternalReasoningService.cs');
const enterpriseService = read('src', 'backend', 'ProjectTime.Api', 'Ai', 'CelarAiEnterprisePlatformService.cs');
const helpService = read('src', 'backend', 'ProjectTime.Api', 'Ai', 'PulseAiSystemIntelligenceService.cs');
const helpContracts = read('src', 'backend', 'ProjectTime.Api', 'Ai', 'PulseAiSystemIntelligenceContracts.cs');
const brandModule = read('src', 'backend', 'ProjectTime.Api', 'Modules', 'CelarAiBrandModule.cs');
const routingModule = read('src', 'backend', 'ProjectTime.Api', 'Modules', 'CelarAiCapabilityRoutingModule.cs');
const flowHiveFactory = read('src', 'backend', 'ProjectTime.Api', 'Modules', 'ProjectFlowHiveAiRequestFactory.cs');
const projectForge = read('src', 'backend', 'ProjectTime.Api', 'Modules', 'ProjectForgeModule.cs');
const helpUi = read('src', 'frontend', 'project-time-web', 'src', 'HelpAssistant.jsx');
const transforms = read('src', 'backend', 'ProjectTime.Api', 'Directory.Build.targets');
const packageJson = fs.readFileSync(path.join(webRoot, 'package.json'), 'utf8');

const checks = [];
function check(name, condition, detail) {
  checks.push({ name, condition });
  console.log(`${name}=${condition ? 'PASSED' : 'FAILED'} — ${detail}`);
}

function containsAll(source, values) {
  return values.every((value) => source.includes(value));
}

function section(source, start, end) {
  const startIndex = source.indexOf(start);
  if (startIndex < 0) return '';
  const endIndex = source.indexOf(end, startIndex + start.length);
  return endIndex < 0 ? source.slice(startIndex) : source.slice(startIndex, endIndex);
}

check(
  'CELAR_PRIVATE_TERMINAL_REFUSAL_SEMANTICS',
  containsAll(privateModel, [
    'PulseAiPrivateModelResponsePolicy.IsSafetyRefusalErrorAsync(',
    'PulseAiPrivateModelResponsePolicy.IsSafetyRefusal(json.RootElement)',
    'private_model_safety_refusal'
  ])
    && containsAll(routing, [
      'PulseAiPrivateModelResponsePolicy.IsSafetyRefusalErrorAsync(',
      'PulseAiPrivateModelResponsePolicy.IsSafetyRefusal(json.RootElement)',
      'ProjectPulseAiOutcomes.Refusal',
      'celar_ai_private_http_'
    ]),
  'private Celar AI structured safety refusals stop routing while ordinary private-provider failures remain unavailable'
);

const executionSanitizer = section(
  sanitizer,
  'private static PulseAiSanitizationResult SanitizeInternal(',
  'private static string Replace('
);
const externalPreparation = section(
  routing,
  'private ProjectPulseAiGenerationRequest? PrepareExternalRequest(',
  'private static string BuildFallbackWarning('
);
const externalCapsuleCatalog = section(
  routing,
  'public static class CelarAiExternalCapsuleCatalog',
  'public sealed record CelarAiCapabilityDefinition('
);
const centralRouteExecution = section(
  routing,
  'private async Task<ProjectPulseAiRouteResult> GenerateInternalAsync(',
  'private ProjectPulseAiGenerationRequest? PrepareExternalRequest('
);
const sanitizedExternalProductionProbe = section(
  routing,
  'private async Task<CelarAiExternalFallbackProbeTargetResult> ProbeSanitizedExternalTargetAsync(',
  'private static CelarAiExternalFallbackProbeTargetResult PolicyBlockedProbeTarget('
);
const externalSuccess = section(
  routing,
  'result = await provider.GenerateAsync(externalRequest, cancellationToken);',
  'if (result.IsRefusal)'
);
const remotePrompt = section(
  timesheet,
  'private static string BuildRemotePromptWithoutPrivateDocuments(',
  'private static string BoundedEngineerNote('
);
const purposeBuiltFacts = section(
  timesheet,
  'private static IReadOnlyList<string> BuildPurposeBuiltExternalFactCodes(',
  'private static bool HasFactualEngineerNote('
);
const serverOwnedExternalCapsules = section(
  enterpriseExternal,
  'public static class CelarAiExternalReasoningPurposeCatalog',
  'public sealed class CelarAiExternalReasoningService'
);
const enterpriseExternalExecution = section(
  enterpriseExternal,
  'public async Task<CelarAiExternalReasoningResult> TryGenerateAsync(',
  'private static CelarAiExternalReasoningResult Blocked('
);

check(
  'CELAR_EXTERNAL_DLP_REGEX_TIMEOUT',
  sanitizer.includes('RegexTimeout = TimeSpan.FromMilliseconds(250)')
    && sanitizer.includes('RegexOptions.CultureInvariant'),
  'all de-identification regexes have a bounded execution policy'
);
check(
  'CELAR_EXTERNAL_DLP_DIRECT_IDENTIFIERS',
  containsAll(sanitizer, [
    'SecretAssignment',
    'HighEntropyToken',
    'Email',
    'Phone',
    'SocialSecurityNumber',
    'PostalAddress',
    'CalendarDate',
    'GuidValue',
    'UserOrAccountIdentifier'
  ]),
  'credentials, contact data, government IDs, addresses, dates, and record IDs are removed'
);
check(
  'CELAR_EXTERNAL_DLP_NETWORK_IDENTIFIERS',
  containsAll(sanitizer, ['Url', 'HostName', 'Ipv4', 'Ipv6', 'MacAddress']),
  'URLs, internal hostnames, IP addresses, and MAC addresses are removed'
);
check(
  'CELAR_EXTERNAL_DLP_IDENTITY_CONTEXT',
  containsAll(sanitizer, [
    'CustomerOrOrganizationLabel',
    'PersonRoleLabel',
    'OrganizationName',
    'HonorificName',
    'RelationshipIdentity',
    'LeadingNamedActor',
    'CustomerContextName',
    'LocationOrFacilityLabel',
    'NamedLocationContext',
    'ReplaceUnknownProperNouns'
  ]),
  'known and uncertain personal, customer, organization, and location entities are removed'
);
check(
  'CELAR_EXTERNAL_TIMESHEET_OUTPUT_GRAMMAR',
  containsAll(sanitizer, [
    'ApprovedSentenceStarters',
    'IsApprovedCapitalizedWord',
    'LeadingNamedActor'
  ])
    && containsAll(externalCapsuleCatalog, [
      'Begin every sentence',
      'approved generic work verbs',
      'Provided, Performed, Reviewed, Analyzed'
    ])
    && !externalCapsuleCatalog.includes('or Resolved')
    && !sanitizer.includes('"Resolved"')
    && !sanitizer.includes('"Completed"'),
  'Claude/OpenAI Timesheet prose uses a closed generic sentence grammar while identities and unsupported outcome claims remain blocked'
);
check(
  'CELAR_EXTERNAL_DLP_NO_CROSS_LINE_IDENTITY_MATCH',
  sanitizer.includes('Identity labels never use \\s around separators')
    && sanitizer.includes('[ \\t]*[:=][ \\t]*')
    && !sanitizer.includes('\\s*(?:name)?\\s*[:=]'),
  'an Engineer-note label cannot consume the next line as an identity value'
);
check(
  'CELAR_EXTERNAL_DLP_AUTHORITATIVE_TERM_BOUNDARIES',
  containsAll(sanitizer, [
    'SensitiveTermExpression(string term)',
    'Distinct(StringComparer.OrdinalIgnoreCase)',
    'OrderByDescending(value => value.Length)',
    '(?<![\\p{L}\\p{N}])',
    '(?![\\p{L}\\p{N}])'
  ]),
  'server-resolved names and identifiers are removed as bounded complete terms'
);
check(
  'CELAR_EXTERNAL_DLP_FAILS_CLOSED',
  containsAll(executionSanitizer, [
    'invalidSensitiveTermInventory',
    'HasResidualSensitiveData(current, sensitiveTerms)',
    'may remain after de-identification',
    'ExternalExecutionAuthorized: authorized'
  ]),
  'invalid inventories or residual identity evidence cannot authorize a provider call'
);
check(
  'CELAR_EXTERNAL_PRIVATE_SOURCE_NEVER_SANITIZED_INTO_PUBLIC_ROUTE',
  containsAll(externalPreparation, [
    'if (execution.ContainsPrivateDocuments && !isolatedServerOwnedCapsule)',
    'private_document_context_external_blocked',
    'if (execution.ContainsPeopleRecords && !isolatedServerOwnedCapsule)',
    'people_record_context_external_blocked',
    'if (execution.ContainsFinancialValues && !isolatedServerOwnedCapsule)',
    'financial_context_external_blocked'
  ])
    && externalPreparation.includes('Content: fixedCapsule.Capsule')
    && !externalPreparation.includes('request.UserPrompt')
    && containsAll(executionSanitizer, [
      'PrivateDocumentOrCommercialMarker.IsMatch(original)',
      'request must be rebuilt from non-document facts before external execution'
    ]),
  'raw private documents, commercial markers, people-record datasets, and financial data are structurally ineligible'
);
check(
  'CELAR_EXTERNAL_CUSTOMER_IDENTITY_INVENTORY_REQUIRED',
  routing.includes('IReadOnlyList<string>? IdentityTerms = null')
    && containsAll(externalPreparation, [
      'execution.IdentityTerms ?? []',
      'execution.ContainsCustomerIdentity && identityTerms.Count == 0',
      'sanitized_external_identity_inventory_missing'
    ])
    && containsAll(timesheet, [
      'ContainsCustomerIdentity: !string.IsNullOrWhiteSpace(request.CustomerName)',
      'IdentityTerms: IdentityTerms(request)',
      'SensitiveTerms: SensitiveTerms(request)',
      'ExternalFactCodes: externalFactCodes'
    ])
    && containsAll(transforms, [
      'GenerateWithPrivateTargetAsync',
      'ExternalFactCodes: externalFactCodes',
      'DestinationFiles="$(CelarAiTimesheetGenerated)"'
    ]),
  'a resolved customer name must be present in the backend-only removal inventory'
);
check(
  'CELAR_EXTERNAL_TIMESHEET_MINIMAL_CAPSULE',
  containsAll(timesheet, [
    'BuildPurposeBuiltExternalFactCodes(request)',
    'ExternalCapsulePurpose: CelarAiExternalCapsuleCatalog.TimesheetCustomerDescription',
    'ExternalFactCodes: externalFactCodes',
    'ExternalProblemStatement: !hasAssociatedDocuments',
    'BoundedEngineerNote(request.CurrentDescription)',
    '.Select(signal => signal.Code)',
    'TimesheetActivityUserProvidedWork',
    'ExternalWorkClassificationCode(request)'
  ])
    && containsAll(externalCapsuleCatalog, [
      'Create a customer-ready time-entry description using only these approved identity-free facts:',
      'separately sanitized note supplied by',
      'Do not claim completion,',
      'TimesheetFactLabels[code]',
      'classificationCount != 1',
      'activityOrDomainCount == 0'
    ])
    && containsAll(externalPreparation, [
      'timesheetProblemIncluded',
      'Content: execution.ExternalProblemStatement',
      '_sanitizer.SanitizeForExecution(',
      'sanitizedProblem.SanitizedCapsule'
    ])
    && !externalCapsuleCatalog.includes('request.CurrentDescription')
    && !externalCapsuleCatalog.includes('request.CustomerName')
    && !externalCapsuleCatalog.includes('request.ProjectName'),
  'Claude/OpenAI receive fixed backend labels plus only the separately de-identified Engineer note; private documents and structured identity remain excluded'
);
check(
  'CELAR_EXTERNAL_LOWERCASE_UNLABELED_IDENTITY_SAFE',
  containsAll(purposeBuiltFacts, [
    '.Where(signal => signal.Pattern.IsMatch(note))',
    '.Select(signal => signal.Code)',
    '.Distinct(StringComparer.Ordinal)',
    '.Take(10)'
  ])
    && !/(?:match\.Value|Groups\[|Substring\(|note\[|string\.Join\([^\n]*note)/.test(purposeBuiltFacts)
    && externalPreparation.includes('execution.SensitiveTerms.Concat(execution.IdentityTerms ?? [])')
    && externalPreparation.includes('execution.IdentityTerms ?? []')
    && externalPreparation.includes('sanitizedProblem.Redactions.Count > 0'),
  'free text is independently bounded and de-identified; known identities and sanitizer-detected protected tokens cannot be copied'
);
check(
  'CELAR_EXTERNAL_ANY_MODULE_CLOSED_CAPSULE',
  containsAll(externalCapsuleCatalog, [
    'public const string HelpTroubleshooting',
    'public const string SowScopeQuality',
    'public const string ProjectPlanQuality',
    'public const string CloseoutCommunication',
    'public const string TimesheetCustomerDescription',
    'private static readonly IReadOnlyDictionary<string, string> TimesheetFactLabels',
    'supplied.Distinct(StringComparer.Ordinal).Count() != supplied.Length',
    'TimesheetFactLabels.ContainsKey(code)',
    'activityOrDomainCount == 0',
    'GenericSystemPrompt',
    'TryResolve(string? purposeCode, out CelarAiExternalCapsuleDefinition definition)',
    'IReadOnlyList<string>? factCodes'
  ])
    && containsAll(externalPreparation, [
      'execution.ExternalCapsulePurpose',
      'execution.ExternalFactCodes',
      'sanitized_external_closed_purpose_required',
      'Content: fixedCapsule.Capsule',
      'SystemPrompt = fixedCapsule.SystemPrompt'
    ])
    && !routing.includes('PurposeBuiltExternalCapsule')
    && !routing.includes('PurposeBuiltExternalSystemPrompt')
    && containsAll(serverOwnedExternalCapsules, [
    'SowScopeQuality',
    'ProjectPlanQuality',
    'ProjectTimelineSequencing',
    'ProjectDiagramGovernance',
    'expectedCategory.Length == 0',
    'string.Equals(category?.Trim(), expectedCategory, StringComparison.Ordinal)',
    'capsule = string.Empty;',
    'return false;'
  ])
    && containsAll(enterpriseExternalExecution, [
      'TryBuildServerOwnedCapsule(',
      'if (!serverOwnedCapsuleReady)',
      'Content: serverOwnedCapsule',
      'SensitiveTerms: []',
      'UserPrompt: sanitized.SanitizedCapsule',
      '_router.GenerateExternalAsync(',
      'ExternalCapsulePurpose: serverOwnedPurposeCategory'
    ])
    && enterpriseContracts.includes('string PurposeCategory')
    && !enterpriseContracts.includes('string GenericProblem')
    && !enterpriseContracts.includes('IReadOnlyList<string> SensitiveTerms')
    && !enterpriseExternal.includes('request.GenericProblem')
    && !enterpriseExternal.includes('request.SensitiveTerms')
    && !enterpriseService.includes('GenericExternalProblem(')
    && containsAll(transforms, [
      "grep -Fq 'CelarAiCapabilityRouter _router'",
      "grep -Fq 'ExternalCapsulePurpose: serverOwnedPurposeCategory'",
      'DestinationFiles="$(CelarAiExternalReasoningGenerated)"'
    ]),
  'every non-Timesheet Module 011 fallback uses only a fixed backend capsule selected by a closed category; arbitrary lowercase or unlabeled input such as "reviewed acme rollout with john doe" has no outbound field'
);
check(
  'CELAR_EXTERNAL_STORED_ROUTE_AND_PRIVATE_DOCUMENT_ORDER',
  containsAll(centralRouteExecution, [
    '_store.LoadRouteAsync(feature, cancellationToken)',
    '_store.LoadPrivateModelProfileAsync(cancellationToken)',
    'privatePolicyProfile?.RequirePrivateModelForDocuments == true',
    'new[] { CelarAiCapabilityTargets.CelarAi }',
    '.Concat(route.Targets.Where(',
    '"deferred"',
    '"private_document_private_target_mandatory"',
    'privateTargetOverride is not null',
    'mandatoryConsumerPrivateTarget',
    'skipPrivateTarget\n                && !requirePrivateTargetBeforeExternal'
  ]),
  'stored order remains authoritative for generic work while persisted document policy forces Celar before public targets without disabling the private RAG callback'
);
check(
  'CELAR_EXTERNAL_REFUSAL_TERMINAL_AND_ASSURANCE_ONCE',
  containsAll(centralRouteExecution, [
    'if (privateResult.IsRefusal)',
    'ProjectPulseAiOutcomes.Refusal',
    'No later target was attempted.',
    'var privateFailureCode = DecisionCode(',
    '_health.RecordFailure(',
    'decisions.Add(new(target, "failed", privateFailureCode));'
  ])
    && !section(
      centralRouteExecution,
      'var privateFailureCode = DecisionCode(',
      'decisions.Add(new(target, "failed", privateFailureCode));'
    ).includes('_assurance.Record('),
  'a private safety refusal stops routing, while an unavailable private target records health but leaves one terminal consumer-assurance record to the selected later target'
);
check(
  'CELAR_EXTERNAL_ADMIN_PROBE_DOES_NOT_MUTATE_CONSUMER_ASSURANCE',
  containsAll(sanitizedExternalProductionProbe, [
    '_health.RecordSuccess(',
    '_health.RecordFailure(',
    '_health.RecordRefusal(',
    '_health.RecordProbe('
  ])
    && !sanitizedExternalProductionProbe.includes('_assurance.Record('),
  'Claude/OpenAI administrator readiness probes retain provider health and probe telemetry without being recorded as consumer inference'
);
check(
  'CELAR_EXTERNAL_BACKEND_AUTOMATIC_POLICY',
  containsAll(externalPreparation, [
    'if (!fixedCapsuleReady)',
    'sanitized_external_closed_purpose_required',
    'PROJECTPULSE_AI_ALLOW_SANITIZED_EXTERNAL_ESCALATION',
    'PROJECTPULSE_CELAR_AI_SANITIZED_EXTERNAL_FALLBACK_ENABLED'
  ])
    && !externalPreparation.includes('execution.AllowSanitizedExternalAssistance')
    && containsAll(helpService, [
      'AllowSanitizedExternalAssistance: false',
      'ExternalCapsulePurpose: externalCapsulePurpose'
    ])
    && containsAll(enterpriseService, [
      'AllowSanitizedExternalAssistance: false',
      'ExternalCapsulePurpose: externalCapsulePurpose'
    ])
    && containsAll(routingModule, [
      'AllowSanitizedExternalAssistance: false',
      'ExternalCapsulePurpose: CelarAiExternalCapsuleCatalog.CloseoutCommunication'
    ])
    && !enterpriseService.includes('request.AllowSanitizedExternalFallback'),
  'closed-capsule fallback is automatic from persisted routing plus both runtime privacy flags and cannot be authorized or disabled by a UI checkbox'
);
check(
  'CELAR_EXTERNAL_HELP_PRIVATE_PROMPT_ISOLATION',
  containsAll(helpService, [
    'BuildPrivateRouterPrompt(',
    'TryResolveHelpCapsulePurpose(',
    '_router.GenerateWithPrivateTargetAsync(',
    '_router.GenerateAsync(',
    'IsPrivateSafetyRefusal(answer)',
    'ProjectPulseAiOutcomes.Refusal',
    'externalAssistance = Limit(',
    'const string externalProblemStatement = "";',
    'ExternalProblemStatement: externalProblemStatement',
    'PublicGeneralQuestion: plan.IntentCode == "general_knowledge"',
    'PublicQuestion: plan.IntentCode == "general_knowledge"',
    '? CelarAiExternalCapsuleCatalog.GeneralKnowledge',
    'SafetyRefusalAnswer(plan, correlationId, routed.Provider)',
    'routeOutcome == ProjectPulseAiOutcomes.Refusal',
    'No later AI target or governed local answer was used.',
    "it did not receive the user's question, private documents, attachment text, tool results, customer/project context, people records, financial values, or identifiers"
  ])
    && !helpService.includes('BuildExternalProblemStatement(')
    && !section(
      helpService,
      'else if ((routed.Provider is CelarAiCapabilityTargets.Claude',
      'if (!string.IsNullOrWhiteSpace(routed.Warning))'
    ).includes('MergeModelAnswer(')
    && containsAll(helpContracts, ['externalAssistance = ExternalAssistance'])
    && containsAll(helpUi, [
      'Supplementary external guidance (unverified)',
      'closed server-owned topic plus a backend-owned purpose capsule',
      'the user’s wording was not sent',
      'No attachment text, private document content, tool results, customer or project context'
    ])
    && containsAll(brandModule, [
      'attemptedTargets = result.AttemptedTargets ?? []',
      'targetDecisions = result.TargetDecisions ?? []',
      'externalProviderCalled = (result.AttemptedTargets ?? []).Any'
    ]),
  'Help keeps raw enterprise evidence private, exposes truthful route telemetry, and displays only sanitized general guidance separately from the grounded answer'
);
check(
  'CELAR_EXTERNAL_ENTERPRISE_PRIVATE_CALLBACK_AND_SEPARATION',
  containsAll(enterpriseService, [
    '_router.GenerateWithPrivateTargetAsync(',
    'privateResult = await ExecutePrivateComposeAsync(',
    'return PrivateComposeTargetResult(privateResult);',
    'IsPrivateSafetyRefusal(answer)',
    'if (routed.Provider is CelarAiCapabilityTargets.Claude or CelarAiCapabilityTargets.OpenAi',
    'external = ToExternalAssistance(routed);',
    'routed.Outcome == ProjectPulseAiOutcomes.Refusal',
    '"celar_ai_solution_draft_refused"',
    '"safety_refusal"',
    '"private_celar_rag_with_sanitized_generic_module064_assistance"',
    '"private_evidence_composer_after_governed_local_route"',
    'SelectedTarget: routed.Provider',
    'TargetDecisions: routed.TargetDecisions ?? []'
  ])
    && !enterpriseService.includes('request.AllowSanitizedExternalFallback')
    && !section(
      enterpriseService,
      'CelarAiExternalReasoningResult? external = null;',
      'var status = routed.Provider'
    ).includes('BuildSowDraft(external')
    && containsAll(routingModule, [
      'ExternalCapsulePurpose: CelarAiExternalCapsuleCatalog.CloseoutCommunication',
      'ContainsPrivateDocuments: true',
      'externalAssistance = externalSelected && !refused ? routed.Content : string.Empty',
      'externalSelected\n                    ? BuildCloseoutFallback(request)',
      'fixed backend-owned identity-free closeout structure and tone capsule'
    ]),
  'SOW/plan/diagram and closeout requests interleave private RAG with stored routing while public guidance and local terminal context remain separate from private structured output'
);
check(
  'CELAR_COMPREHENSIVE_CROSS_MODULE_OUTPUTS',
  containsAll(timesheet, [
    'detailed, accurate, evidence-based, customer-facing professional services timesheet descriptions',
    'two to four complete',
    'preferably 75 to 150 words when the evidence supports that detail',
    'The entry should be reviewed before submission'
  ])
    && containsAll(enterpriseContracts, [
      'string? DetailLevel = "comprehensive"',
      'CelarAiSowDraft('
    ])
    && containsAll(enterpriseService, [
      'DetailLevel: request.DetailLevel ?? "comprehensive"',
      'FeatureCode: CelarAiCapabilityCatalog.SowGsdPlanning',
      'WorkPackages: workPackages'
    ])
    && containsAll(privateRag, [
      'Create a comprehensive, cited, customer-ready delivery draft',
      'Automatically fill every supported section.',
      'Every detailed step must identify the actor, action, required input or prerequisite, expected output, validation or evidence, and completion condition.'
    ])
    && containsAll(flowHiveFactory, [
      'Produce a detailed cited draft only.',
      'Return a comprehensive draft with source citations and unresolved conflicts preserved.'
    ])
    && containsAll(projectForge, [
      'Create a comprehensive, reviewable project plan with WBS tasks, dependencies, roles, durations, and engineering estimates',
      'DetailLevel: Clean(request.DetailLevel, 40, "comprehensive")'
    ])
    && containsAll(routingModule, [
      'Produce a comprehensive communication with a subject, concise executive opening, verified completion,',
      'Create comprehensive, structured, factual professional-services closeout communication drafts.',
      'Verified completion summary',
      'Outstanding items, owners, and risks',
      'Next actions and review boundary'
    ])
    && helpService.includes('Be extremely detailed and comprehensive; do not return a surface summary'),
  'Timesheet, SOW, FlowHive, Project Forge, closeout, and Ask Celar preserve comprehensive governed outputs while leading with the useful answer'
);
check(
  'CELAR_EXTERNAL_SANITIZED_CAPSULE_ONLY',
  containsAll(externalPreparation, [
    '_sanitizer.SanitizeForExecution(',
    'if (!fixedCapsuleReady)',
    'sanitized_external_closed_purpose_required',
    'Classification: "internal_generic"',
    'UserPrompt = sanitized.SanitizedCapsule',
    'Content: fixedCapsule.Capsule',
    'SystemPrompt = fixedCapsule.SystemPrompt'
  ])
    && !externalPreparation.includes('request.UserPrompt')
    && !externalPreparation.includes('request.SystemPrompt'),
  'the original system prompt and user prompt are replaced at the public-provider boundary'
);
check(
  'CELAR_EXTERNAL_OUTPUT_REVALIDATED',
  sanitizer.includes('public bool IsExternalOutputSafe(')
    && sanitizer.includes('public bool IsTimesheetExternalOutputSafe(')
    && sanitizer.includes('public bool IsPublicExternalOutputSafe(')
    && sanitizer.includes('public bool TryPreparePublicQuestion(')
    && containsAll(externalSuccess, [
      '_sanitizer.IsExternalOutputSafe(',
      '_sanitizer.IsTimesheetExternalOutputSafe(',
      '_sanitizer.IsPublicExternalOutputSafe(',
      'out outputDecisionCode',
      '_health.RecordFailure(target, outputDecisionCode',
      'continue;'
    ]),
  'untrusted Claude/OpenAI output is discarded if it reintroduces protected data; the isolated public-question path applies its separate credential and identifier boundary'
);
check(
  'CELAR_EXTERNAL_OUTPUT_UNSUPPORTED_OUTCOME_BLOCKED',
  containsAll(sanitizer, [
    'UnsupportedOutcomeClaim',
    'UnsupportedOutcomeClaim.IsMatch(',
    'IsTimesheetExternalOutputSafe(',
    'external_output_unsupported_outcome_claim'
  ]),
  'customer-facing Timesheet provider output cannot assert an unsupported completion, resolution, or terminal outcome while other routes may restate approved facts'
);
check(
  'CELAR_EXTERNAL_DEIDENTIFICATION_DIAGNOSTICS',
  containsAll(routing, [
    'sanitized_external_request_ready_after_deidentification',
    'generation_succeeded_after_deidentification',
    'sanitized_external_residual_identity_blocked',
    'sanitized_external_sensitive_term_inventory_invalid',
    'backend could not prove complete customer and personal-identity removal'
  ]),
  'target decisions distinguish deidentified success from each fail-closed privacy outcome'
);
check(
  'CELAR_EXTERNAL_DEIDENTIFICATION_BUILD_GATE',
  packageJson.includes('"validate:celar-ai-external-deidentification"')
    && packageJson.includes('npm run validate:celar-ai-external-deidentification'),
  'the privacy contract runs in the production frontend build gate'
);

const failed = checks.filter(({ condition }) => !condition);
console.log(`\nCELAR_EXTERNAL_DEIDENTIFICATION_CHECKS=${checks.length}`);
console.log('CELAR_EXTERNAL_DOCUMENTS=PRIVATE_ONLY');
console.log('CELAR_EXTERNAL_INPUT=BACKEND_DEIDENTIFIED_FAIL_CLOSED');
console.log('CELAR_EXTERNAL_OUTPUT=PRIVACY_REVALIDATED');
console.log(`CELAR_EXTERNAL_DEIDENTIFICATION=${failed.length === 0 ? 'PASSED' : 'FAILED'}`);

if (failed.length > 0) {
  console.error(`CELAR_EXTERNAL_DEIDENTIFICATION_FAILURES=${failed.map(({ name }) => name).join(',')}`);
  process.exitCode = 1;
}
