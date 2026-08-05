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
const apiProject = read('src/backend/ProjectTime.Api/ProjectTime.Api.csproj');
const routing = read('src/backend/ProjectTime.Api/Ai/CelarAiCapabilityRouting.cs');
const module064 = read('src/backend/ProjectTime.Api/Modules/CelarAiCapabilityRoutingModule.cs');
const runtimeOptions = read('src/backend/ProjectTime.Api/Ai/PulseAiPrivateRuntimeContracts.cs');
const runtimeService = read('src/backend/ProjectTime.Api/Ai/PulseAiPrivateDocumentRuntimeService.cs');
const runtimeWorker = read('src/backend/ProjectTime.Api/Ai/PulseAiPrivateDocumentRuntimeWorker.cs');
const runtimeModule = read('src/backend/ProjectTime.Api/Modules/PulseAiPrivateRuntimeModule.cs');
const ragRepository = read('src/backend/ProjectTime.Api/Ai/PulseAiPrivateRagRepository.cs');
const systemRepository = read('src/backend/ProjectTime.Api/Ai/PulseAiSystemIntelligenceRepository.cs');
const providerConfigurationModule = read('src/backend/ProjectTime.Api/Modules/AiProviderConfigurationModule.cs');
const systemModule = read('src/backend/ProjectTime.Api/Modules/PulseAiSystemIntelligenceModule.cs');
const productionModule = read('src/backend/ProjectTime.Api/Modules/CelarAiProductionPlatformModule.cs');
const projectForgeModule = read('src/backend/ProjectTime.Api/Modules/ProjectForgeModule.cs');
const panel = read('src/frontend/project-time-web/src/CelarAiCapabilityRoutingPanel.jsx');

assert(
  'EXACT_SOURCE_FENCE',
  policy.includes('PROJECTPULSE_AI_RELEASE_SCOPED_MODE')
    && policy.includes('PROJECTPULSE_AI_RELEASE_CONFIG_SOURCE_COMMIT')
    && policy.includes('PROJECTPULSE_SOURCE_COMMIT')
    && policy.includes('EmbeddedSourceCommitMetadataKey = "ProjectPulseSourceRevision"')
    && policy.includes('GetCustomAttributes<AssemblyMetadataAttribute>()')
    && policy.includes('does not match the immutable commit embedded in the API assembly')
    && policy.includes('must be exactly true or false when supplied')
    && policy.includes('runningSourceCommit.Length != 40')
    && policy.includes('configurationSourceCommit.Length != 40')
    && policy.includes('does not match the running application source commit')
    && policy.includes('ProjectPulseAiReleaseRuntimeGuard')
    && apiProject.includes('<AssemblyMetadata Include="ProjectPulseSourceRevision" Value="$(ProjectPulseSourceRevision)" />')
    && apiProject.includes('-p:ProjectPulseSourceRevision=<sha>'),
  'candidate configuration is bound to immutable assembly metadata and malformed mode values fail startup',
);

assert(
  'FULL_CATALOG_IMMUTABLE_ROUTES',
  policy.includes('PROJECTPULSE_AI_RELEASE_ROUTE_ORDER')
    && policy.includes('CelarAiCapabilityCatalog.Definitions.Count != 8')
    && routing.includes('if (release.Active)')
    && routing.includes('release.RouteOrder')
    && routing.includes('DeploymentManaged = true')
    && routing.includes('ProjectPulseAiReleaseRuntimePolicy.RejectMutation("Capability route mutation")'),
  'one validated deployment route order is applied to all eight central capabilities while database routes are bypassed',
);

assert(
  'DEPLOYMENT_MANAGED_PRIVATE_PROFILE',
  routing.includes('return EnvironmentProfile() with')
    && routing.includes('ConfigurationSourceCommit = release.ConfigurationSourceCommit')
    && routing.includes('(!profile.Persisted && !profile.DeploymentManaged)')
    && module064.includes('configurationAuthority = release.ConfigurationAuthority')
    && module064.includes('allCentralCapabilityRoutesReady = routesReady')
    && module064.includes('databaseEvidenceWritten = !release.Active'),
  'the exact release loads its Key-Vault-injected environment profile, ignores the database profile, and keeps provider evidence process-local',
);

assert(
  'CANDIDATE_READ_ONLY',
  policy.includes('PROJECTPULSE_AI_CANDIDATE_READ_ONLY')
    && runtimeOptions.includes('PROJECTPULSE_PULSE_AI_PRIVATE_RUNTIME_WORKER_ENABLED')
    && runtimeOptions.includes('PROJECTPULSE_PULSE_AI_AUTO_QUEUE_ELIGIBLE_DOCUMENTS')
    && runtimeService.includes('return Empty("release_candidate_read_only", "release_candidate_read_only")')
    && runtimeWorker.includes('ProjectPulseAiReleaseRuntimePolicy.RequireValid().Active')
    && runtimeModule.includes('workerExecutionBlocked = release.Active')
    && runtimeModule.includes('automaticQueueExecutionBlocked = release.Active')
    && runtimeModule.includes('CandidateMutationBlocked')
    && ragRepository.includes('RequireValid().Active) return Guid.NewGuid()')
    && ragRepository.includes('RequireValid().Active) return;')
    && systemRepository.includes('RequireValid().Active) return null;')
    && systemRepository.includes('RequireValid().Active) return (Guid.Empty, 0);')
    && providerConfigurationModule.includes('Public-provider secrets, models, and enabled state cannot be changed')
    && systemModule.includes('Durable conversation creation is disabled')
    && productionModule.includes('lifecycle mutations are disabled')
    && productionModule.includes('RequireValid().Active) return;')
    && projectForgeModule.includes('if (CandidateAiDraftMutationBlocked() is { } blocked) return blocked;')
    && projectForgeModule.indexOf('CandidateAiDraftMutationBlocked()') < projectForgeModule.indexOf('enterprise.ComposeAsync(')
    && projectForgeModule.includes('Project Forge AI draft generation and persistence are disabled')
    && projectForgeModule.includes('StatusCodes.Status423Locked'),
  'candidate revisions cannot mutate provider configuration, documents, AI lifecycle/audit data, conversations, or Project Forge drafts',
);

assert(
  'MODULE064_READ_ONLY_EXPERIENCE',
  module064.includes('deployment_managed_configuration_read_only')
    && module064.includes('StatusCodes.Status423Locked')
    && panel.includes('Release candidate configuration is deployment-managed and read-only')
    && panel.includes("deploymentManaged ? 'Read-only'")
    && panel.includes("deploymentManaged ? 'Deployment-managed'"),
  'Module 064 reports the deployment authority and disables configuration controls while retaining read-only provider testing',
);

const failed = checks.filter((check) => !check.condition);
console.log(`CELAR_AI_RELEASE_SCOPED_SUMMARY=${checks.length - failed.length}/${checks.length} passed`);
if (failed.length) process.exit(1);
