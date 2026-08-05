import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repoRoot = fileURLToPath(new URL('../../../../', import.meta.url));
const read = (relative) => fs.readFileSync(path.join(repoRoot, relative), 'utf8');
const checks = [];

function assert(name, condition, evidence) {
  checks.push({ name, condition, evidence });
  console.log(`CELAR_AI_PRODUCTION_${name}=${condition ? 'PASSED' : 'FAILED'} — ${evidence}`);
}

const routing = read('src/backend/ProjectTime.Api/Ai/CelarAiCapabilityRouting.cs');
const contracts = read('src/backend/ProjectTime.Api/Ai/PulseAiPrivateRuntimeContracts.cs');
const ragContracts = read('src/backend/ProjectTime.Api/Ai/PulseAiPrivateRagContracts.cs');
const storage = read('src/backend/ProjectTime.Api/Ai/ProjectPulseUploadStorage.cs');
const repository = read('src/backend/ProjectTime.Api/Ai/PulseAiPrivateDocumentRuntimeRepository.cs');
const runtime = read('src/backend/ProjectTime.Api/Ai/PulseAiPrivateDocumentRuntimeService.cs');
const releasePolicy = read('src/backend/ProjectTime.Api/Ai/ProjectPulseAiReleaseRuntimePolicy.cs');
const scanner = read('src/backend/ProjectTime.Api/Ai/PulseAiPrivateMalwareScanner.cs');
const snapshot = read('src/backend/ProjectTime.Api/Ai/PulseAiImmutableDocumentSnapshot.cs');
const ocr = read('src/backend/ProjectTime.Api/Ai/PulseAiPrivateOcrClient.cs');
const embeddings = read('src/backend/ProjectTime.Api/Ai/PulseAiPrivateEmbeddingClient.cs');
const model = read('src/backend/ProjectTime.Api/Ai/PulseAiPrivateModelClient.cs');
const refusalFixtures = JSON.parse(read('tests/fixtures/celar-ai-private-model-refusal-responses.json'));
const retrieval = read('src/backend/ProjectTime.Api/Ai/PulseAiPrivateRagRepository.cs');
const reauthorization = read('src/backend/ProjectTime.Api/Ai/PulseAiPrivateRetrievalAuthorizationService.cs');
const module064 = read('src/backend/ProjectTime.Api/Modules/CelarAiCapabilityRoutingModule.cs');
const runtimeModule = read('src/backend/ProjectTime.Api/Modules/PulseAiPrivateRuntimeModule.cs');
const services = read('src/backend/ProjectTime.Api/Ai/ProjectPulseAiServiceCollectionExtensions.cs');
const panel = read('src/frontend/project-time-web/src/CelarAiCapabilityRoutingPanel.jsx');
const keyRing = read('src/backend/ProjectTime.Api/Ai/ProjectPulseAiEncryptionKeyRing.cs');
const rotation = read('src/backend/ProjectTime.Api/Ai/ProjectPulseAiEncryptionRotationService.cs');
const secretStore = read('src/backend/ProjectTime.Api/Ai/ProjectPulseAiSecretStore.cs');
const providerConfiguration = read('src/backend/ProjectTime.Api/Ai/ProjectPulseAiConfiguration.cs');
const providerHealth = read('src/backend/ProjectTime.Api/Ai/ProjectPulseAiHealthRegistry.cs');
const migration071 = read('database/migrations/071_ai_runtime_production_hardening.sql');
const rollback071 = read('database/rollback/071_ai_runtime_production_hardening_rollback.sql');
const rotationIntegration = read('tests/CelarAiProductionHardeningTests/Program.cs');
const hardeningCi = read('.github/workflows/celar-ai-production-hardening-ci.yml');
const externalProbeHandler = module064.slice(
  module064.indexOf('private static async Task<IResult> TestSanitizedExternalFallbackAsync'),
  module064.indexOf('private static async Task<IResult> RotateEncryptionKeyAsync')
);
const externalProbeRuntime = routing.slice(
  routing.indexOf('public async Task<CelarAiExternalFallbackProductionProbeResult> ProbeSanitizedExternalFallbackAsync'),
  routing.indexOf('public Task<ProjectPulseAiRouteResult> GenerateExternalAsync')
);
const hardeningPrerequisites = read('tests/fixtures/celar-ai-production-hardening-prerequisites.sql');

assert(
  'STABLE_WRITE_ONLY_ENCRYPTION_KEY',
  keyRing.includes('PROJECTPULSE_AI_SECRET_ENCRYPTION_KEY')
    && keyRing.includes('PROJECTPULSE_AI_SECRET_ENCRYPTION_KEY_ID')
    && keyRing.includes('PROJECTPULSE_AI_SECRET_ENCRYPTION_KEY_RING')
    && keyRing.includes('key.Length == 32')
    && routing.includes('SecretEncryptionAvailable => _keyRing.Available')
    && routing.includes('bearerTokenReturned = false')
    && !keyRing.includes('RandomNumberGenerator.GetBytes(32)'),
  'the runtime accepts a supplied AES-256 key ring with stable IDs, never generates an encryption key, and never returns secrets'
);

assert(
  'MIGRATION_OWNED_SCHEMA',
  migration071.includes('CREATE TABLE IF NOT EXISTS ai_provider_secrets')
    && migration071.includes('ai_provider_probe_evidence')
    && migration071.includes('lease_generation')
    && migration071.includes("conrelid = 'public.ai_provider_secrets'::regclass")
    && migration071.includes('ck_ai_provider_secrets_provider_code')
    && migration071.includes('ck_ai_provider_settings_provider_code')
    && migration071.includes('ck_ai_private_profile_endpoint_nonce_length')
    && migration071.includes('ck_ai_private_profile_endpoint_tag_length')
    && migration071.includes('ck_ai_private_profile_token_nonce_length')
    && migration071.includes('ck_ai_private_profile_token_tag_length')
    && secretStore.includes("migration_id = '071_ai_runtime_production_hardening'")
    && routing.includes("migration_id = '071_ai_runtime_production_hardening'")
    && !secretStore.includes('CREATE TABLE IF NOT EXISTS')
    && !routing.slice(routing.indexOf('private async Task EnsureSchemaAsync'), routing.indexOf('private CelarAiPrivateModelProfile EnvironmentProfile')).includes('CREATE TABLE'),
  'Migration 071 owns provider/profile schema and runtime code performs validation only'
);

assert(
  'ATOMIC_KEY_ROTATION',
  rotation.includes('IsolationLevel.Serializable')
    && rotation.includes('ROTATE-PROJECTPULSE-AI-ENCRYPTION-KEY')
    && rotation.includes('encryption_key_rotated')
    && rotation.includes('CryptographicOperations.ZeroMemory')
    && rotation.includes('await transaction.CommitAsync')
    && module064.includes('/api/ai-configuration/encryption-key/rotate')
    && module064.includes('AuthorizeAdministratorAsync(context, requireSameOrigin: true')
    && module064.includes('secretValuesReturned = false')
    && rollback071.includes('rollback refused')
    && rollback071.includes('EXISTS (SELECT 1 FROM ai_provider_secret_audit)')
    && rollback071.includes('EXISTS (SELECT 1 FROM ai_private_model_profile_audit)')
    && rotationIntegration.includes('CELAR_AI_KEY_RING_ROTATION_INTEGRATION=PASSED')
    && rotationIntegration.includes('rotated ciphertext decrypts only with active key')
    && rotationIntegration.includes('atomic rollback preserves exact ciphertext nonce tag key IDs and audit rows across stores')
    && rotationIntegration.includes('old key cannot decrypt rotated ciphertext')
    && rotationIntegration.includes('both public providers')
    && rotationIntegration.includes('celar_ai_private_endpoint')
    && rotationIntegration.includes('celar_ai_private_token')
    && hardeningCi.includes('Validate atomic old-and-new key-ring rotation'),
  'one exact-confirmation admin path atomically rotates public and private ciphertext with key-ID fences and guarded rollback'
);

assert(
  'PRODUCTION_HARDENING_SOURCE_CONTROL',
  hardeningCi.includes('pull_request:\n    branches:\n      - main')
    && !hardeningCi.includes('pull_request:\n    paths:')
    && hardeningCi.includes('CELAR_AI_PRODUCTION_HARDENING=NOT_APPLICABLE')
    && hardeningCi.includes("if: needs.classify.outputs.applicable == 'true'")
    && hardeningCi.includes('CELAR_AI_PRODUCTION_HARDENING_SCOPE=EXACT_45_SOURCE_FILES')
    && hardeningCi.includes('MISSING_SOURCE=')
    && hardeningCi.includes('UNEXPECTED=')
    && !hardeningCi.includes('DEPENDENCIES=')
    && hardeningCi.includes('git diff --name-status --no-renames')
    && hardeningCi.includes('git ls-tree HEAD')
    && hardeningCi.includes('Verify tracked generated sources from canonical transforms')
    && hardeningCi.includes('cmp "$TEMP_ROOT/Program.ScopedRbac.g.cs"')
    && hardeningCi.includes('cmp "$TEMP_ROOT/PulseAiDocumentGroundingService.g.cs"')
    && hardeningCi.includes('src/backend/ProjectTime.Api/Ai/ProjectPulseAiCandidateRequestFence.cs')
    && hardeningCi.includes('src/backend/ProjectTime.Api/Ai/ProjectPulseAiHealthMonitor.cs')
    && hardeningCi.includes('src/backend/ProjectTime.Api/Ai/PulseAiPrivateDocumentRuntimeService.cs')
    && hardeningCi.includes('src/backend/ProjectTime.Api/Ai/PulseAiPrivateDocumentRuntimeWorker.cs')
    && hardeningCi.includes('src/backend/ProjectTime.Api/Ai/ProjectPulseAiSecretStore.cs')
    && hardeningCi.includes('src/backend/ProjectTime.Api/Ai/ProjectPulseAiEncryptionRotationService.cs')
    && hardeningCi.includes('src/backend/ProjectTime.Api/ProjectTime.Api.csproj')
    && hardeningCi.includes('src/backend/ProjectTime.Api/Ai/PulseAiPrivateRuntimeContracts.cs')
    && hardeningCi.includes('src/backend/ProjectTime.Api/Ai/PulseAiImmutableDocumentSnapshot.cs')
    && hardeningCi.includes('src/backend/ProjectTime.Api/Modules/AiProviderConfigurationModule.cs')
    && hardeningCi.includes('src/backend/ProjectTime.Api/Modules/PulseAiPrivateRuntimeModule.cs')
    && hardeningCi.includes('src/backend/ProjectTime.Api/Modules/PulseAiPrivateRagModule.cs')
    && hardeningCi.includes('src/backend/ProjectTime.Api/Modules/PulseAiSystemIntelligenceModule.cs')
    && hardeningCi.includes('src/backend/ProjectTime.Api/Modules/CelarAiProductionPlatformModule.cs')
    && hardeningCi.includes('src/backend/ProjectTime.Api/Modules/ProjectForgeModule.cs')
    && hardeningCi.includes('src/frontend/project-time-web/src/CelarAiCapabilityRoutingPanel.jsx')
    && hardeningCi.includes('src/frontend/project-time-web/package.json')
    && hardeningCi.includes('docs/modules/module-064-ai-provider-configuration/CELAR-AI-PRODUCTION-HARDENING.md'),
  'every main PR receives a lightweight classification while security-family branches require the exact 45-file, mode-safe source manifest before heavy validation'
);

assert(
  'PRIVATE_PROFILE_AUTHENTICATION',
  routing.includes('AuthMode.Equals("bearer"')
    && routing.includes('AuthenticationConfigured')
    && routing.includes('Save the write-only private bearer token before enabling')
    && routing.includes('Private inference must remain required for document-grounded answers')
    && routing.includes('AuthenticationHeaderValue("Bearer"'),
  'Celar AI requires a persisted model, write-only bearer token, enabled target, and private-document enforcement'
);

const refusalFixtureNames = new Set(refusalFixtures.map((fixture) => fixture.name));
const refusalCodes = new Set([
  'contentfilter',
  'contentpolicyviolation',
  'jailbreakdetected',
  'moderationblocked',
  'policyviolation',
  'responsibleaipolicyviolation',
  'safetyrefusal',
  'safetyviolation'
]);
const compactCode = (value) => String(value ?? '').slice(0, 80).toLowerCase().replace(/[^a-z0-9]/g, '');
const fixtureHasSafeErrorCode = (fixture) => {
  if (![400, 403, 422].includes(fixture.status)) return false;
  const error = fixture.body?.error ?? {};
  const inner = error.innererror ?? {};
  return [error.code, error.type, inner.code, inner.type]
    .some((value) => refusalCodes.has(compactCode(value)));
};
const fixtureHas200Refusal = (fixture) => fixture.status === 200
  && (fixture.body?.choices ?? []).some((choice) =>
    choice?.finish_reason?.toLowerCase() === 'content_filter'
    || Boolean(choice?.message?.refusal)
    || (choice?.message?.content ?? []).some((item) =>
      item?.type?.toLowerCase() === 'refusal' || Boolean(item?.refusal)));
const fixtureClassification = (fixture) =>
  fixtureHas200Refusal(fixture) || fixtureHasSafeErrorCode(fixture)
    ? 'private_model_safety_refusal'
    : `private_model_http_${fixture.status}`;
assert(
  'PRIVATE_MODEL_TERMINAL_REFUSAL_CONTRACT',
  refusalFixtures.length === 7
    && refusalFixtureNames.has('openai-compatible-200-message-refusal')
    && refusalFixtureNames.has('openai-compatible-200-refusal-content-item')
    && refusalFixtureNames.has('openai-compatible-200-content-filter-finish')
    && refusalFixtureNames.has('azure-openai-safe-refusal-error-code')
    && refusalFixtureNames.has('openai-camel-case-content-filter-code')
    && refusalFixtureNames.has('private-provider-camel-case-safety-violation-code')
    && refusalFixtureNames.has('generic-provider-unavailable')
    && refusalFixtures.filter((fixture) => fixture.expected === 'private_model_safety_refusal').length === 6
    && refusalFixtures.every((fixture) => fixtureClassification(fixture) === fixture.expected)
    && refusalFixtures.find((fixture) => fixture.name === 'generic-provider-unavailable')?.expected === 'private_model_http_503'
    && model.includes('internal static class PulseAiPrivateModelResponsePolicy')
    && model.includes('MaximumResponseBytes = 1_000_000')
    && model.includes('HasNonEmptyProperty(message, "refusal")')
    && model.includes('HasRefusalContentItem(message, "content")')
    && model.includes('StringEquals(choice, "finish_reason", "content_filter")')
    && model.includes('responsibleaipolicyviolation')
    && model.includes('contentfilter')
    && model.includes('safetyviolation')
    && model.includes('status is not (400 or 403 or 422)')
    && model.includes('SafetyRefusalDiagnostic = "private_model_safety_refusal"')
    && routing.includes('ProjectPulseAiOutcomes.Refusal')
    && routing.includes('return Refusal(requestId, (int)response.StatusCode)')
    && routing.includes('celar_ai_private_http_'),
  'bounded private-provider parsing treats only structured safety signals as terminal refusals and preserves generic HTTP unavailability'
);

assert(
  'PRIVATE_DNS_AND_TRANSPORT',
  contracts.includes('VerifyResolvedPrivateEndpointAsync')
    && contracts.includes('private_dns_resolved_public_address')
    && contracts.includes('userinfo_not_allowed')
    && contracts.includes('https_required')
    && contracts.includes('IsValidAllowlistEntry')
    && !contracts.includes('(bytes[0] == 169 && bytes[1] == 254)')
    && contracts.includes('address.IsIPv6LinkLocal')
    && contracts.includes('!address.IsIPv6SiteLocal')
    && routing.includes('allowLoopback: false')
    && model.includes('allowLoopback: false')
    && ocr.includes('allowLoopback: false')
    && embeddings.includes('allowLoopback: false')
    && contracts.includes('IsConnectablePrivateAddress')
    && contracts.includes('address.IsIPv6LinkLocal')
    && contracts.includes('address.IsIPv6Multicast')
    && services.includes('SocketsHttpHandler')
    && services.includes('ConnectCallback = ConnectToPinnedPrivateEndpointAsync')
    && services.includes('context.InitialRequestMessage.RequestUri?.Scheme')
    && services.includes('Private AI transports require HTTPS.')
    && services.includes('Dns.GetHostAddressesAsync(host, cancellationToken)')
    && services.includes('addresses.Any(address => !PulseAiPrivateEndpointPolicy.IsConnectablePrivateAddress(address))')
    && services.includes('new IPEndPoint(address, context.DnsEndPoint.Port)')
    && services.includes('UseProxy = false')
    && services.includes('AllowAutoRedirect = false')
    && services.includes('UseCookies = false')
    && !services.includes('DangerousAcceptAnyServerCertificateValidator'),
  'private calls require HTTPS and an allowlisted host, then re-resolve and pin a private socket while default TLS hostname validation, redirect rejection, and cookie isolation remain enforced'
);

assert(
  'REQUIRED_MIGRATIONS',
  contracts.includes('052_pulse_ai_private_document_runtime')
    && ragContracts.includes('053_pulse_ai_private_rag_orchestration')
    && repository.includes('061_celar_ai_capability_routing')
    && repository.includes('HardeningMigrationApplied')
    && runtime.includes('ProductionMigrationsApplied')
    && runtime.includes('Migration 071 has not been applied.')
    && module064.includes('allRequiredApplied = runtimeReadiness.ProductionMigrationsApplied')
    && hardeningCi.includes('-f database/migrations/052_document_intelligence_runtime.sql')
    && hardeningCi.includes('-f database/migrations/053_intelligence_answer_orchestration.sql')
    && hardeningCi.includes('-f database/migrations/061_celar_ai_capability_routing.sql')
    && hardeningCi.includes('BEFORE_PROJECT=')
    && hardeningCi.includes('BEFORE_DOCUMENT=')
    && hardeningCi.includes('BEFORE_JOB=')
    && hardeningCi.includes('BEFORE_PROVIDER_SECRET=')
    && hardeningCi.includes('BEFORE_PROVIDER_SETTING=')
    && !hardeningCi.includes('CI prerequisite fixture')
    && /image:\s*postgres:16-alpine@sha256:[0-9a-f]{64}/.test(hardeningCi)
    && hardeningPrerequisites.includes('CREATE TABLE project_intake_documents')
    && hardeningPrerequisites.includes('CREATE TABLE ai_provider_secrets')
    && hardeningPrerequisites.includes("'legacy-ci'")
    && hardeningPrerequisites.includes("'sow',")
    && hardeningPrerequisites.includes("'/mnt/projectpulse-ci/synthetic-sow.pdf'"),
  'readiness and CI apply the real 052, 053, 061, and 071 migrations in order while preserving representative project, SOW, and job rows'
);

assert(
  'SHARED_PERSISTENT_UPLOAD_ROOT',
  storage.includes('PROJECTPULSE_UPLOAD_ROOT')
    && storage.includes('PROJECTPULSE_UPLOAD_ROOT_SHARED_PERSISTENT')
    && storage.includes('ProbeWriteAndDelete')
    && storage.includes('return !File.Exists(probe)')
    && storage.includes('VerifyReadOnlyAttestation')
    && storage.includes('IsKnownEphemeral')
    && ['/tmp', '/var/tmp', '/dev/shm', '/run'].every((value) => storage.includes(`"${value}"`))
    && storage.includes('Legacy {LegacyEnvironmentVariable} is not accepted')
    && runtime.includes('InspectProductionReadiness'),
  'production readiness requires an explicit, writable, attested shared mount and rejects known ephemeral roots'
);

assert(
  'SYSTEM_PRINCIPAL_AUTOMATIC_ADMISSION',
  contracts.includes('AutoQueueEligibleDocuments: Boolean("PROJECTPULSE_PULSE_AI_AUTO_QUEUE_ELIGIBLE_DOCUMENTS", false)')
    && contracts.includes('PROJECTPULSE_PULSE_AI_DOCUMENT_SERVICE_PRINCIPAL_USER_ID')
    && repository.includes('InspectDocumentServicePrincipalAsync')
    && repository.includes("service_permission.permission_code = 'QUEUE_PULSE_AI_DOCUMENT_PROCESSING'")
    && repository.includes('COALESCE(service_user.is_active, FALSE) = TRUE')
    && repository.includes('service_assignment.is_active = TRUE')
    && runtime.includes('servicePrincipal.Authorized')
    && runtime.includes('DocumentServicePrincipalQueuePermissionGranted')
    && runtime.includes('DocumentServicePrincipalAuthorized')
    && repository.includes('EnqueueNextEligibleDocumentAsync')
    && repository.includes('service_principal_user_id')
    && repository.includes('document_automatically_queued')
    && runtime.includes('a human identity is never substituted'),
  'automatic SOW admission is off by default and readiness revalidates an active application identity with the exact queue permission'
);

assert(
  'SANITIZED_EXTERNAL_PROVIDER_READINESS',
  module064.includes('PROJECTPULSE_AI_ALLOW_SANITIZED_EXTERNAL_ESCALATION')
    && module064.includes('PROJECTPULSE_CELAR_AI_SANITIZED_EXTERNAL_FALLBACK_ENABLED')
    && module064.includes('providerConfiguration.Claude.Enabled')
    && module064.includes('providerConfiguration.Claude.Configured')
    && module064.includes('providerConfiguration.OpenAi.Enabled')
    && module064.includes('providerConfiguration.OpenAi.Configured')
    && module064.includes('ProviderModelApproved')
    && module064.includes('configuration.ApprovedModels.Contains')
    && module064.includes('ExternalProviderProductionReady')
    && module064.includes('health.LastProbeSuccessAt')
    && module064.includes('health.ProbeStatus, "available"')
    && providerConfiguration.includes('ProjectPulseAiProviderConfiguration Claude')
    && providerConfiguration.includes('ProjectPulseAiProviderConfiguration OpenAi')
    && providerHealth.includes('ApplyConfiguration'),
  'when sanitized fallback is requested, Module 064 requires both policy flags and fresh available Claude and OpenAI probes'
);

assert(
  'SANITIZED_EXTERNAL_PRODUCTION_PROBE',
  module064.includes('/api/ai-configuration/sanitized-external-fallback/production-test')
    && externalProbeHandler.includes('AuthorizeAdministratorAsync')
    && externalProbeHandler.includes('requireSameOrigin: true')
    && externalProbeHandler.includes('fixedServerAuthoredCapsule = true')
    && externalProbeHandler.includes('callerContentAccepted = false')
    && externalProbeHandler.includes('providerContentReturned = false')
    && externalProbeHandler.includes('sharedRouteChanged = false')
    && externalProbeHandler.includes('target.Provider')
    && externalProbeHandler.includes('target.Status')
    && externalProbeHandler.includes('target.DiagnosticCode')
    && externalProbeHandler.includes('target.RequestId')
    && !externalProbeHandler.includes('target.Content')
    && !externalProbeHandler.includes('result.Content')
    && externalProbeRuntime.includes('fixedGenericCapsule')
    && externalProbeRuntime.indexOf('CelarAiCapabilityTargets.Claude')
      < externalProbeRuntime.indexOf('CelarAiCapabilityTargets.OpenAi')
    && externalProbeRuntime.includes('_sanitizer.SanitizeForExecution')
    && externalProbeRuntime.includes('_sanitizer.IsExternalOutputSafe')
    && externalProbeRuntime.includes('sanitized_external_fallback_production_probe_succeeded'),
  'a same-origin non-View-As admin operation tests fixed deidentified generations through Claude then OpenAI and returns no provider content'
);

assert(
  'MALWARE_OCR_AND_EMBEDDING_GATES',
  runtime.indexOf('_malwareScanner.ScanAsync') < runtime.indexOf('_extractor.ExtractAsync')
    && scanner.includes('ResolvePrivateHostAsync')
    && scanner.includes('hostReadiness.ApprovedAddresses')
    && scanner.includes('ConnectAsync(scannerAddress, options.MalwareScannerPort, token)')
    && !scanner.includes('ConnectAsync(options.MalwareScannerHost')
    && contracts.includes('HostResolutionResult')
    && contracts.includes('ApprovedAddresses')
    && contracts.includes('string SourceSha256')
    && !contracts.slice(
      contracts.indexOf('public object ToPublicEvidence()'),
      contracts.indexOf('public sealed record PulseAiPrivateOcrResult')
    ).includes('sourceSha256')
    && scanner.includes('SourceSha256: sourceSha256')
    && releasePolicy.includes('pre_scanned_attestation_not_release_approved')
    && releasePolicy.includes('!string.IsNullOrWhiteSpace(signatureVersion)')
    && snapshot.includes('FileMode.CreateNew')
    && snapshot.includes('FileShare.None')
    && snapshot.includes('FileShare.Read')
    && snapshot.includes('RandomNumberGenerator.GetBytes(16)')
    && snapshot.includes('IncrementalHash.CreateHash(HashAlgorithmName.SHA256)')
    && snapshot.includes('copied > maximumFileBytes')
    && snapshot.includes('File.Move(partialPath, snapshotPath)')
    && snapshot.includes('UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute')
    && snapshot.includes('private const UnixFileMode ReadOnlyFileMode = UnixFileMode.UserRead')
    && snapshot.includes('File.GetUnixFileMode(path) != PrivateDirectoryMode')
    && snapshot.includes('File.GetUnixFileMode(path) != SealedDirectoryMode')
    && snapshot.includes('File.GetUnixFileMode(path) != ReadOnlyFileMode')
    && snapshot.includes('FileAttributes.ReadOnly')
    && snapshot.includes('PulseAiPrivateDocumentPipelinePolicy.SupportedExtensions.Contains')
    && snapshot.includes('FileAttributes.ReparsePoint')
    && snapshot.includes('await _guardian.DisposeAsync()')
    && snapshot.includes('Cleanup(Source.StoragePath')
    && snapshot.includes('StoragePath = snapshotPath')
    && snapshot.includes('CleanupOrphansAsync')
    && snapshot.includes('maximumDirectories = Math.Clamp(maximumDirectories, 1, 128)')
    && snapshot.includes('TryParseAttemptDirectory')
    && snapshot.includes('leaseToken:N')
    && snapshot.includes('DeleteVerifiedSnapshotDirectory')
    && !snapshot.includes('recursive: true')
    && !snapshot.includes('ToPublicEvidence')
    && repository.includes('HasLiveSnapshotLeaseAsync')
    && repository.includes("job_status IN ('scanning','extracting','embedding','indexing','cancel_requested')")
    && repository.includes('lease_token = @lease_token')
    && repository.includes('lease_generation = @lease_generation')
    && repository.includes('lease_expires_at > NOW()')
    && runtime.indexOf('CleanupOrphansAsync') < runtime.indexOf('ClaimNextAsync')
    && runtime.includes('document_snapshot_cleanup_unavailable')
    && runtime.indexOf('source = immutableSnapshot.Source') < runtime.indexOf('_malwareScanner.ScanAsync')
    && runtime.includes('_malwareScanner.ScanAsync(source.StoragePath, options')
    && runtime.includes('_extractor.ExtractAsync(source, pipelineOptions, cancellationToken)')
    && runtime.includes('_ocrClient.ExtractAsync(\n                    source,')
    && runtime.includes('IsSha256(scan.SourceSha256)')
    && runtime.includes('immutableSnapshot.SourceSha256')
    && runtime.includes('extraction.SourceSha256')
    && runtime.includes('await SourceStillMatchesAsync(')
    && runtime.includes('document_snapshot_integrity_changed')
    && runtime.includes('immutableSnapshotIntegrityVerified = false')
    && runtime.indexOf('extraction.SourceSha256') < runtime.indexOf('_ocrClient.ExtractAsync')
    && runtime.indexOf('_ocrClient.ExtractAsync') < runtime.indexOf('await SourceStillMatchesAsync(')
    && runtime.indexOf('await SourceStillMatchesAsync(') < runtime.indexOf('_extractor.CreateChunks')
    && runtime.indexOf('await SourceStillMatchesAsync(') < runtime.indexOf('_embeddingClient.GenerateAsync')
    && runtime.indexOf('await SourceStillMatchesAsync(') < runtime.indexOf('PersistProcessedDocumentAsync')
    && runtime.lastIndexOf('await SourceStillMatchesAsync(') > runtime.indexOf('_embeddingClient.GenerateAsync')
    && runtime.lastIndexOf('await SourceStillMatchesAsync(') < runtime.indexOf('PersistProcessedDocumentAsync')
    && contracts.includes('PROJECTPULSE_PULSE_AI_DOCUMENT_MALWARE_SCAN_APPROVAL_REFERENCE')
    && contracts.includes('PROJECTPULSE_PULSE_AI_DOCUMENT_MALWARE_SIGNATURE_VERSION')
    && runtime.includes('AwaitingOcr')
    && runtime.includes('OcrEndpointPrivate')
    && runtime.includes('EmbeddingEndpointPrivate'),
  'scan, extraction, OCR, chunking, embedding, and persistence are bound to one guarded immutable snapshot while ClamAV pins a validated private address'
);

assert(
  'EXPLICIT_LEXICAL_ONLY_APPROVAL',
  contracts.includes('PROJECTPULSE_PULSE_AI_ALLOW_LEXICAL_ONLY_COMPLETION')
    && contracts.includes('PROJECTPULSE_PULSE_AI_LEXICAL_ONLY_APPROVAL_REFERENCE')
    && contracts.includes('AllowLexicalOnlyCompletion && LexicalOnlyApprovalReference.Length > 0')
    && runtime.includes('LexicalOnlyCompletionApproved'),
  'lexical-only operation is fail-closed unless both the switch and an approval reference are present'
);

assert(
  'SOW_REPROCESS_AND_READY_EVIDENCE',
  repository.includes("authority_status IN ('approved','canonical')")
    && retrieval.includes("v.authority_status IN ('approved','canonical')")
    && reauthorization.includes("v.authority_status IN ('approved','canonical')")
    && repository.includes('RecoverExpiredLeasesAsync')
    && repository.includes('WHEN attempt_count >= maximum_attempts THEN 0')
    && repository.includes('version.source_sha256 = @expected_source_sha256')
    && contracts.includes('APPROVE-PULSE-AI-PRIVATE-DOCUMENT-VERSION')
    && contracts.includes('string? ExpectedSourceSha256')
    && runtime.includes('request.ExpectedSourceSha256')
    && runtimeModule.includes('/versions/{versionId:guid}/approve')
    && module064.includes('readySowDocumentCount')
    && module064.includes('pendingSowDocumentCount')
    && module064.includes('atLeastOneAuthorizedSowReady'),
  'operators can reprocess failed work, approve only the exact active source hash, and observe authorized SOW readiness in Module 064'
);

assert(
  'CROSS_REPLICA_PROBE_EVIDENCE',
  migration071.includes('profile_revision')
    && migration071.includes('expires_at')
    && routing.includes('SavePrivateProbeEvidenceAsync')
    && routing.includes('LoadPrivateProbeEvidenceAsync')
    && routing.includes('expires_at > NOW()')
    && module064.includes('database_shared_profile_revision')
    && module064.includes('persistedProbe'),
  'private-model readiness consumes database-shared evidence tied to the exact profile revision and TTL'
);

assert(
  'FENCED_HEARTBEAT_LEASE',
  repository.includes('lease_token = @lease_token')
    && repository.includes('lease_generation = lease_generation + 1')
    && repository.includes('RenewLeaseAsync')
    && repository.includes('lease_generation = @lease_generation')
    && repository.includes('lost its fenced lease')
    && (repository.match(/lease_expires_at = NOW\(\) \+ \(@lease_seconds \* INTERVAL '1 second'\)/g) ?? []).length >= 2
    && !repository.includes("lease_expires_at = NOW() + INTERVAL '5 minutes'")
    && runtime.includes('MaintainLeaseAsync')
    && (runtime.match(/options\.LeaseSeconds,\s*cancellationToken\)/g) ?? []).length >= 4
    && runtime.includes('processingStop.Cancel()'),
  'scanner, extraction, OCR, embedding, and index work run under a renewable token/generation fence and fail closed on ownership loss'
);

assert(
  'EXACT_TIMESHEET_TARGET_ORDER',
  module064.includes('route.Targets.SequenceEqual')
    && module064.includes('CelarAiCapabilityTargets.DefaultOrder')
    && routing.includes('DefaultOrder = [CelarAi, Claude, OpenAi, Local]'),
  'production readiness requires Celar AI, Claude, OpenAI, then governed local for every Timesheet capability'
);

assert(
  'MODULE064_PRODUCTION_GATE_VISIBLE',
  module064.includes('celar_ai_private_platform_production_ready')
    && module064.includes('privateTargetVerificationFresh')
    && module064.includes('shared successful probe evidence for this exact profile revision within the last 15 minutes')
    && module064.includes('health.RecordProbe(result)')
    && module064.includes('privateModelReady')
    && module064.includes('privateDocumentRuntimeReady')
    && module064.includes('blockers = blockers.Distinct')
    && panel.includes('productionReadiness')
    && panel.includes('production-readiness item'),
  'Module 064 exposes end-to-end readiness and actionable blockers without returning paths or secrets'
);

const failed = checks.filter((check) => !check.condition);
console.log(`CELAR_AI_PRODUCTION_CHECKS=${checks.length}`);
console.log(`CELAR_AI_PRODUCTION_SOURCE_CONTRACT=${failed.length === 0 ? 'PASSED' : 'FAILED'}`);
console.log('CELAR_AI_PRODUCTION_RUNTIME_ACTIVATED_BY_VALIDATOR=NO');
console.log('CELAR_AI_PRODUCTION_SECRETS_PRINTED=0');
console.log('CELAR_AI_PRODUCTION_DEPLOYMENTS=0');
if (failed.length > 0) process.exit(1);
