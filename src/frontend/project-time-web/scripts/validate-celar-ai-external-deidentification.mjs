import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const webRoot = path.resolve(scriptDirectory, '..');
const repositoryRoot = path.resolve(webRoot, '..', '..', '..');
const read = (...parts) => fs.readFileSync(path.join(repositoryRoot, ...parts), 'utf8');

const sanitizer = read('src', 'backend', 'ProjectTime.Api', 'Ai', 'PulseAiEscalationSanitizer.cs');
const routing = read('src', 'backend', 'ProjectTime.Api', 'Ai', 'CelarAiCapabilityRouting.cs');
const timesheet = read('src', 'backend', 'ProjectTime.Api', 'ProjectPulseAiTimeEntrySuggestionService.cs');
const enterpriseContracts = read('src', 'backend', 'ProjectTime.Api', 'Ai', 'CelarAiEnterprisePlatformContracts.cs');
const enterpriseExternal = read('src', 'backend', 'ProjectTime.Api', 'Ai', 'CelarAiExternalReasoningService.cs');
const enterpriseService = read('src', 'backend', 'ProjectTime.Api', 'Ai', 'CelarAiEnterprisePlatformService.cs');
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
  'private static string BuildPurposeBuiltExternalActivityFacts(',
  'private static bool HasPurposeBuiltExternalActivityFacts('
);
const serverOwnedExternalCapsules = section(
  enterpriseExternal,
  'public static bool TryBuildServerOwnedCapsule(',
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
    'if (execution.ContainsPrivateDocuments)',
    'private_document_context_external_blocked',
    'if (execution.ContainsPeopleRecords)',
    'people_record_context_external_blocked',
    'if (execution.ContainsFinancialValues)',
    'financial_context_external_blocked'
  ])
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
    && containsAll(transforms, [
      'ContainsCustomerIdentity: !string.IsNullOrWhiteSpace(request.CustomerName)',
      'IdentityTerms: new[] { request.CustomerName ?? string.Empty }',
      'PurposeBuiltDeidentifiedInput: true',
      'DeidentifiedFactsAvailable: HasPurposeBuiltExternalActivityFacts(request.CurrentDescription)'
    ]),
  'a resolved customer name must be present in the backend-only removal inventory'
);
check(
  'CELAR_EXTERNAL_TIMESHEET_MINIMAL_CAPSULE',
  containsAll(remotePrompt, [
    'No customer name, project name, project code, task name, task code, person name, internal identifier, work date, or location is included as structured context.',
    'Backend-derived identity-free activity categories:',
    'BuildPurposeBuiltExternalActivityFacts(request.CurrentDescription)',
    'backend-derived activity categories below as the only factual evidence',
    "never imply that you saw the Engineer's note",
    'If the safe categories are sparse, remain concise and general.',
    'Generic work classification: {ExternalWorkClassification(request)}'
  ])
    && timesheet.includes('no captured token, name, identifier, or substring is copied')
    && !containsAll(remotePrompt, ['Project code: {request.ProjectCode', 'Project name: {request.ProjectName'])
    && !remotePrompt.includes('Work date: {request.WorkDate}')
    && !remotePrompt.includes('Task name: {request.TaskName}')
    && !remotePrompt.includes('{BoundedEngineerNote(request.CurrentDescription)}'),
  'Claude/OpenAI receive only fixed backend category labels, never raw free text or structured identity fields'
);
check(
  'CELAR_EXTERNAL_LOWERCASE_UNLABELED_IDENTITY_SAFE',
  containsAll(purposeBuiltFacts, [
    '.Where(signal => signal.Pattern.IsMatch(note))',
    '.Select(signal => signal.Label)',
    '.Distinct(StringComparer.Ordinal)',
    '.Take(10)'
  ])
    && !/(?:match\.Value|Groups\[|Substring\(|note\[|string\.Join\([^\n]*note)/.test(purposeBuiltFacts),
  'free text is used only for boolean signal detection; no lowercase or unlabeled token can be copied'
);
check(
  'CELAR_EXTERNAL_ANY_MODULE_CLOSED_CAPSULE',
  containsAll(serverOwnedExternalCapsules, [
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
      'UserPrompt: sanitized.SanitizedCapsule'
    ])
    && enterpriseContracts.includes('string PurposeCategory')
    && !enterpriseContracts.includes('string GenericProblem')
    && !enterpriseContracts.includes('IReadOnlyList<string> SensitiveTerms')
    && !enterpriseExternal.includes('request.GenericProblem')
    && !enterpriseExternal.includes('request.SensitiveTerms')
    && !enterpriseService.includes('GenericExternalProblem(')
    && containsAll(transforms, [
      'SensitiveTerms: [],',
      'IdentityTerms: [],',
      'DeidentifiedFactsAvailable: serverOwnedCapsuleReady'
    ]),
  'every non-Timesheet Module 011 fallback uses only a fixed backend capsule selected by a closed category; arbitrary lowercase or unlabeled input such as "reviewed acme rollout with john doe" has no outbound field'
);
check(
  'CELAR_EXTERNAL_SANITIZED_CAPSULE_ONLY',
  containsAll(externalPreparation, [
    '_sanitizer.SanitizeForExecution(',
    'if (!execution.PurposeBuiltDeidentifiedInput)',
    'sanitized_external_purpose_built_capsule_required',
    'Classification: "internal_generic"',
    'UserPrompt = sanitized.SanitizedCapsule',
    'Treat every [REDACTED_*] marker as omitted data'
  ]),
  'the original system prompt and user prompt are replaced at the public-provider boundary'
);
check(
  'CELAR_EXTERNAL_OUTPUT_REVALIDATED',
  sanitizer.includes('public bool IsExternalOutputSafe(')
    && containsAll(externalSuccess, [
      '_sanitizer.IsExternalOutputSafe(',
      'out var outputDecisionCode',
      '_health.RecordFailure(target, outputDecisionCode',
      'continue;'
    ]),
  'untrusted Claude/OpenAI output is discarded if it reintroduces protected data'
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
