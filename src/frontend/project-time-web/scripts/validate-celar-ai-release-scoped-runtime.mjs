import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repoRoot = fileURLToPath(new URL('../../../../', import.meta.url));
const read = (relative) => fs.readFileSync(path.join(repoRoot, relative), 'utf8');
const checks = [];
const assert = (name, condition, evidence) => {
  checks.push({ name, condition });
  console.log(`CELAR_AI_RELEASE_SCOPED_${name}=${condition ? 'PASSED' : 'FAILED'} — ${evidence}`);
};

const policy = read('src/backend/ProjectTime.Api/Ai/ProjectPulseAiReleaseRuntimePolicy.cs');
const fence = read('src/backend/ProjectTime.Api/Ai/ProjectPulseAiCandidateRequestFence.cs');
const program = read('src/backend/ProjectTime.Api/Program.cs');
const apiProject = read('src/backend/ProjectTime.Api/ProjectTime.Api.csproj');
const routing = read('src/backend/ProjectTime.Api/Ai/CelarAiCapabilityRouting.cs');
const module064 = read('src/backend/ProjectTime.Api/Modules/CelarAiCapabilityRoutingModule.cs');
const storage = read('src/backend/ProjectTime.Api/Ai/ProjectPulseUploadStorage.cs');
const database = read('src/backend/ProjectTime.Api/Ai/ProjectPulseAiDatabaseConnection.cs');
const behavior = read('tests/CelarAiProductionHardeningTests/ReleaseRuntimeBehavior.cs');
const secretStore = read('src/backend/ProjectTime.Api/Ai/ProjectPulseAiSecretStore.cs');
const rotationService = read('src/backend/ProjectTime.Api/Ai/ProjectPulseAiEncryptionRotationService.cs');
const healthMonitor = read('src/backend/ProjectTime.Api/Ai/ProjectPulseAiHealthMonitor.cs');
const runtimeService = read('src/backend/ProjectTime.Api/Ai/PulseAiPrivateDocumentRuntimeService.cs');
const runtimeWorker = read('src/backend/ProjectTime.Api/Ai/PulseAiPrivateDocumentRuntimeWorker.cs');
const ragRepository = read('src/backend/ProjectTime.Api/Ai/PulseAiPrivateRagRepository.cs');
const systemRepository = read('src/backend/ProjectTime.Api/Ai/PulseAiSystemIntelligenceRepository.cs');
const providerModule = read('src/backend/ProjectTime.Api/Modules/AiProviderConfigurationModule.cs');
const productionPlatformModule = read('src/backend/ProjectTime.Api/Modules/CelarAiProductionPlatformModule.cs');
const projectForge = read('src/backend/ProjectTime.Api/Modules/ProjectForgeModule.cs');

assert(
  'EXPLICIT_RELEASE_PHASES',
  policy.includes('PROJECTPULSE_AI_RELEASE_PHASE')
    && policy.includes('ProjectPulseAiReleasePhase { Disabled, Candidate, Active }')
    && policy.includes('IsCandidate')
    && policy.includes('IsActiveRelease')
    && policy.includes('IsReleaseScoped')
    && !policy.includes('PROJECTPULSE_AI_CANDIDATE_READ_ONLY')
    && !policy.includes('PROJECTPULSE_AI_RELEASE_SCOPED_MODE'),
  'candidate read-only behavior is intrinsic to an explicit disabled|candidate|active phase',
);

assert(
  'IMMUTABLE_RELEASE_ENVELOPE',
  policy.includes('PROJECTPULSE_AI_RELEASE_SOURCE_COMMIT')
    && policy.includes('PROJECTPULSE_AI_RELEASE_CONTROL_COMMIT')
    && policy.includes('PROJECTPULSE_AI_RELEASE_CONFIG_SHA256')
    && policy.includes('ProjectPulseSourceRevision')
    && policy.includes('ComputeSafeConfigurationDigest')
    && policy.includes('projectpulse-ai-release-config-v2')
    && policy.includes('RequirePinnedSecretReference')
    && policy.includes('VersionedSecretReference')
    && policy.includes('PROJECTPULSE_CLAUDE_APPROVED_ORIGINS')
    && policy.includes('PROJECTPULSE_OPENAI_APPROVED_ORIGINS')
    && policy.includes('RejectLegacySecretAlias("ANTHROPIC_API_KEY"')
    && policy.includes('must be exactly celar_ai,claude,openai,local_template')
    && apiProject.includes('<AssemblyMetadata Include="ProjectPulseSourceRevision" Value="$(ProjectPulseSourceRevision)" />'),
  'source metadata, controller commit, canonical safe configuration, secret versions, and route order fail closed',
);

assert(
  'RELEASE_CONFIGURATION_AUTHORITY',
  routing.includes('if (release.IsReleaseScoped)')
    && routing.includes('DeploymentManaged = true')
    && routing.includes('RejectReleaseConfigurationMutation("Capability route mutation")')
    && routing.includes('RejectReleaseConfigurationMutation("Private-model settings mutation")')
    && secretStore.match(/RejectReleaseConfigurationMutation/g)?.length >= 3
    && secretStore.match(/RequireValid\(\)\.IsReleaseScoped/g)?.length >= 3
    && rotationService.includes('RejectReleaseConfigurationMutation("AI encryption-key rotation")')
    && providerModule.includes('ReleaseConfigurationMutationBlocked')
    && providerModule.includes('store.Available && !release.IsReleaseScoped')
    && providerModule.includes('candidate or active release phases'),
  'candidate and active revisions both consume immutable deployment-managed routes and provider profiles',
);

assert(
  'EXPLICIT_PRIVATE_DESTINATION_BINDING',
  policy.includes('RequireNonEmpty("PROJECTPULSE_PRIVATE_ENDPOINT_HOST_ALLOWLIST"')
    && policy.includes('IsApprovedReleasePrivateInferenceDestination')
    && policy.includes('private_endpoint_must_use_dns_hostname')
    && policy.includes('built_in_private_endpoint_suffix_prohibited')
    && policy.includes('private_endpoint_hostname_not_allowlisted')
    && routing.includes('EnvironmentProfile(allowDefaultAllowlist: false)')
    && routing.includes('allowDefaultAllowlist ? PulseAiPrivateRuntimePolicy.PrivateHostSuffixDefaults : []')
    && behavior.includes('empty private endpoint allowlist is rejected')
    && behavior.includes('default-only private endpoint allowlist is rejected')
    && behavior.includes('mixed built-in and deployment-specific private endpoint allowlist is rejected')
    && behavior.includes('private inference IP literal is rejected')
    && behavior.includes('unmatched private inference hostname is rejected')
    && behavior.includes('exact private inference hostname allowlist match is accepted')
    && behavior.includes('leading-dot private inference hostname suffix match is accepted'),
  'release startup requires an explicit deployment allowlist and binds a DNS-only Celar endpoint by exact host or leading-dot suffix',
);

assert(
  'RELEASE_TOOL_ORIGIN_AND_LOOPBACK_BINDING',
  policy.includes('"PROJECTPULSE_PULSE_AI_SYSTEM_TOOL_BASE_URI"')
    && policy.includes('"PROJECTPULSE_AI_ALLOW_INSECURE_LOOPBACK_ENDPOINTS"')
    && policy.includes('IsApprovedReleaseSystemToolOrigin')
    && policy.includes('system_tool_origin_not_allowlisted')
    && policy.includes('PROJECTPULSE_AI_ALLOW_INSECURE_LOOPBACK_ENDPOINTS=true is prohibited')
    && behavior.includes('release digest changes with the insecure-loopback policy flag')
    && behavior.includes('release digest changes with the system-tool base URI')
    && behavior.includes('release policy rejects the insecure-loopback endpoint flag')
    && behavior.includes('exact HTTPS system-tool origin and host allowlist are accepted')
    && behavior.includes('unmatched system-tool origin and host allowlist are rejected'),
  'the release digest binds the trusted HTTPS system-tool origin and fails closed on insecure loopback',
);

assert(
  'RELEASE_MALWARE_SCANNER_CONFIGURATION',
  policy.includes('IsApprovedReleaseMalwareScannerConfiguration')
    && policy.includes('clamav_tcp_configuration_incomplete')
    && policy.includes('pre_scanned_attestation_not_release_approved')
    && policy.includes('malware_scanner_mode_invalid')
    && behavior.includes('missing release malware scanner mode is rejected')
    && behavior.includes('invalid legacy release malware scanner mode is rejected')
    && behavior.includes('incomplete ClamAV release scanner configuration is rejected')
    && behavior.includes('ClamAV release scanner configuration without signature evidence is rejected')
    && behavior.includes('complete ClamAV release scanner configuration is accepted')
    && behavior.includes('incomplete pre-scan release scanner attestation is rejected')
    && behavior.includes('complete global pre-scan attestation is rejected for release')
    && behavior.includes('Set("PROJECTPULSE_PULSE_AI_DOCUMENT_MALWARE_SCANNER_MODE", "clamav_tcp")')
    && behavior.includes('Set("PROJECTPULSE_PULSE_AI_CLAMAV_HOST", "clamav.internal")')
    && behavior.includes('immutable snapshot hash matches exact copied bytes')
    && behavior.includes('replacing the original does not replace immutable snapshot bytes')
    && behavior.includes('guardian and verified modes block snapshot writes')
    && behavior.includes('guardian and sealed directory block snapshot deletion')
    && behavior.includes('snapshot disposal removes private copied bytes')
    && behavior.includes('live exact snapshot lease is preserved by cleanup')
    && behavior.includes('definitively orphaned snapshot lease is deleted by cleanup'),
  'release startup accepts only complete ClamAV configuration with explicit signature-version evidence',
);

assert(
  'RELEASE_DOCUMENT_SERVICE_PRINCIPAL',
  policy.includes('IsApprovedReleaseDocumentServicePrincipal')
    && policy.includes('document_service_principal_identifier_invalid')
    && behavior.includes('missing release document service principal is rejected')
    && behavior.includes('invalid release document service principal is rejected')
    && behavior.includes('empty release document service principal UUID is rejected')
    && behavior.includes('valid release document service principal UUID is accepted'),
  'release startup requires a non-empty UUID before automatic document admission can start; combined readiness retains database authorization proof',
);

assert(
  'RELEASE_TRAINING_PROHIBITION',
  policy.includes('"PROJECTPULSE_CELAR_AI_TRAINING_ENABLED"')
    && policy.includes('IsApprovedReleaseTrainingConfiguration')
    && policy.includes('release_training_must_be_explicitly_disabled')
    && policy.includes('release_training_configuration_prohibited')
    && behavior.includes('release digest changes with the Celar AI training toggle')
    && behavior.includes('enabled release training configuration is rejected')
    && behavior.includes('configured release training endpoint is rejected while training is disabled')
    && behavior.includes('raw release training bearer token is rejected'),
  'this release digest binds an explicitly false training toggle and prohibits endpoints, allowlists, and raw training tokens',
);

assert(
  'GLOBAL_CANDIDATE_HTTP_FENCE',
  program.indexOf('app.UseProjectPulseAiCandidateRequestFence();') > program.indexOf('var app = builder.Build();')
    && program.indexOf('app.UseProjectPulseAiCandidateRequestFence();') < program.indexOf('app.Use(async')
    && fence.includes('"/health"')
    && fence.includes('VerificationPath')
    && fence.includes('StatusCodes.Status423Locked')
    && fence.includes('if (health || verify)')
    && !fence.includes('StartsWith'),
  'candidate requests are closed before authentication and modules except exact health and combined verification paths',
);

assert(
  'READ_ONLY_CANDIDATE_SESSION',
  program.includes('if (!ProjectPulseAiReleaseRuntimePolicy.RequireValid().IsCandidate)')
    && program.includes('Candidate session validation requires the canonical AI database connection.')
    && program.includes('ProjectPulseAiDatabaseConnection.Resolve()')
    && program.includes('UPDATE auth_sessions')
    && program.indexOf('if (!ProjectPulseAiReleaseRuntimePolicy.RequireValid().IsCandidate)')
      < program.lastIndexOf('UPDATE auth_sessions'),
  'candidate authentication validates the existing session without updating last_seen_at',
);

assert(
  'SINGLE_PROCESS_COMBINED_VERIFICATION',
  module064.includes('ProjectPulseAiCandidateRequestFence.VerificationPath')
    && module064.includes('VerifyReleaseCandidateAsync')
    && module064.includes('InspectCandidateDatabaseAsync')
    && module064.includes('recordHealthEvidence: false')
    && module064.includes('PROJECTPULSE_AI_RELEASE_SOW_DOCUMENT_ID')
    && module064.includes('PROJECTPULSE_AI_RELEASE_SOW_VERSION_ID')
    && module064.includes('PROJECTPULSE_AI_RELEASE_SOW_PROJECT_ID')
    && module064.includes('PROJECTPULSE_AI_RELEASE_SOW_SOURCE_SHA256')
    && module064.includes('exactChunkSetReady')
    && module064.includes('ProbeExactAsync')
    && routing.includes('DeriveContentChallenge')
    && routing.includes('ResponseMatchesDerivedContentChallenge')
    && routing.includes('challenge.ExpectedAnswer')
    && !routing.includes('PROJECTPULSE_EXACT_SOW_READY')
    && behavior.includes('constant exact-token stub cannot satisfy the derived private SOW challenge')
    && behavior.includes('derived private SOW challenge response is content-dependent')
    && module064.includes('responseContentReturned = false')
    && module064.includes('mutableTableCount == 0')
    && module064.includes('sessionLastSeenUpdated = false')
    && module064.includes('sharedProbeEvidenceWrites = 0'),
  'one replica-local request proves providers, read-only database identity, content-derived exact SOW inference, migrations, routes, and storage without persistence',
);

assert(
  'NONMUTATING_CANDIDATE_STORAGE',
  storage.includes('candidate_read_only_platform_attested')
    && storage.includes('PROJECTPULSE_UPLOAD_ROOT_ATTESTATION_FILE')
    && storage.includes('PROJECTPULSE_UPLOAD_ROOT_ATTESTATION_SHA256')
    && storage.includes('VerifyReadOnlyAttestation')
    && storage.includes('FileAccess.Read')
    && storage.includes('after.LastWriteTimeUtc == beforeWrite')
    && storage.includes('!candidate && exists && ProbeWriteAndDelete(root)')
    && storage.includes('return !File.Exists(probe)'),
  'candidate readiness reads only one protected canary while active readiness proves both storage write and delete',
);

assert(
  'CANONICAL_DATABASE_AND_BEHAVIORAL_PROOF',
  database.includes('ProjectPulseAiDatabaseConnectionEvidence')
    && database.includes('Conflicting AI database declarations were rejected')
    && database.includes('CoreCredentialFingerprint')
    && database.includes('FullConnectionFingerprint')
    && database.includes('DatabaseFingerprint')
    && database.includes('ConfiguredRoleFingerprint')
    && behavior.includes('candidate fence blocks mutation before downstream execution')
    && behavior.includes('active phase preserves the normal data plane')
    && behavior.includes('conflicting database aliases fail closed')
    && behavior.includes('equivalent full alias and PTP_DB_* deployment contracts are accepted together')
    && behavior.includes('PTP_DB_* credential conflicts with a full alias still fail closed')
    && behavior.includes('canonical database resolver accepts the complete PTP_DB_* contract')
    && behavior.includes('release digest normalizes sets, booleans, and endpoint trailing slash'),
  'one canonical DB resolver rejects ambiguity and executable tests cover phase, digest, DB, fence, and no-write behavior',
);

assert(
  'CANONICAL_DATABASE_CONSUMERS',
  providerModule.includes('ProjectPulseAiDatabaseConnection.Resolve()')
    && productionPlatformModule.includes('ProjectPulseAiDatabaseConnection.Resolve()')
    && !providerModule.includes('"PTP_DB_HOST"')
    && !productionPlatformModule.includes('"PTP_DB_HOST"')
    && !productionPlatformModule.includes('new NpgsqlConnectionStringBuilder'),
  'provider administration and the active Celar platform resolve the same conflict-detecting database authority as candidate verification',
);

assert(
  'CANDIDATE_ACTIVE_DATA_PLANE_SPLIT',
  runtimeWorker.includes('RequireValid().IsCandidate')
    && runtimeService.includes('if (release.IsCandidate)')
    && ragRepository.match(/RequireValid\(\)\.IsCandidate/g)?.length >= 4
    && systemRepository.match(/RequireValid\(\)\.IsCandidate/g)?.length >= 6
    && projectForge.includes('if (!release.IsCandidate) return null;')
    && !runtimeWorker.includes('RequireValid().Active')
    && !ragRepository.includes('RequireValid().Active')
    && !systemRepository.includes('RequireValid().Active'),
  'candidate blocks document and AI data mutations while active releases retain the normal data plane',
);

assert(
  'CANDIDATE_BACKGROUND_AND_CONFIG_FREEZE',
  secretStore.match(/if \(release\.IsReleaseScoped\)/g)?.length >= 2
    && secretStore.includes('database provider loading is frozen')
    && secretStore.includes('database provider synchronization is frozen')
    && healthMonitor.includes('RequireValid().IsCandidate')
    && healthMonitor.includes('combined verification request owns all candidate probes')
    && routing.includes('bool recordHealthEvidence = true')
    && routing.includes('if (recordHealthEvidence)'),
  'database configuration refresh is frozen for releases and candidate probes run only inside the combined request',
);

const failed = checks.filter((check) => !check.condition);
console.log(`CELAR_AI_RELEASE_SCOPED_SUMMARY=${checks.length - failed.length}/${checks.length} passed`);
if (failed.length) process.exit(1);
