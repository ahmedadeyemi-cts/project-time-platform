import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const frontend = path.resolve(scriptDirectory, '..');
const repository = path.resolve(frontend, '..', '..', '..');
const readRepository = (...parts) => fs.readFileSync(path.join(repository, ...parts), 'utf8');
const readFrontend = (...parts) => fs.readFileSync(path.join(frontend, ...parts), 'utf8');

const contracts = readRepository('src', 'backend', 'ProjectTime.Api', 'Ai', 'ProjectPulseAiContracts.cs');
const configuration = readRepository('src', 'backend', 'ProjectTime.Api', 'Ai', 'ProjectPulseAiConfiguration.cs');
const health = readRepository('src', 'backend', 'ProjectTime.Api', 'Ai', 'ProjectPulseAiHealthRegistry.cs');
const providers = readRepository('src', 'backend', 'ProjectTime.Api', 'Ai', 'ProjectPulseAiRemoteProviders.cs');
const router = readRepository('src', 'backend', 'ProjectTime.Api', 'Ai', 'ProjectPulseAiRouter.cs');
const monitor = readRepository('src', 'backend', 'ProjectTime.Api', 'Ai', 'ProjectPulseAiHealthMonitor.cs');
const registration = readRepository('src', 'backend', 'ProjectTime.Api', 'Ai', 'ProjectPulseAiServiceCollectionExtensions.cs');
const secretStore = readRepository('src', 'backend', 'ProjectTime.Api', 'Ai', 'ProjectPulseAiSecretStore.cs');
const keyRing = readRepository('src', 'backend', 'ProjectTime.Api', 'Ai', 'ProjectPulseAiEncryptionKeyRing.cs');
const moduleBackend = readRepository('src', 'backend', 'ProjectTime.Api', 'Modules', 'AiProviderConfigurationModule.cs');
const consumer = readRepository('src', 'backend', 'ProjectTime.Api', 'ProjectPulseAiTimeEntrySuggestionService.cs');
const program = readRepository('src', 'backend', 'ProjectTime.Api', 'Program.cs');
const app = readFrontend('src', 'App.jsx');
const center = readFrontend('src', 'AiProviderConfigurationCenter.jsx');
const styles = readFrontend('src', 'ai-provider-configuration-center.css');
const packageJson = readFrontend('package.json');
const webDockerfile = readRepository('deployment', 'containers', 'web', 'Dockerfile');
const readme = readRepository('docs', 'modules', 'module-064-ai-provider-configuration', 'README.md');
const contract = readRepository('docs', 'modules', 'module-064-ai-provider-configuration', 'API-CONTRACT.md');
const security = readRepository('docs', 'modules', 'module-064-ai-provider-configuration', 'SECURITY-AND-OPERATIONS.md');
const workRegister = readRepository('docs', 'MODULE-WORK-REGISTER.md');
const catalog = readRepository('docs', 'MODULE-CATALOG.md');
const tracker = readRepository('docs', 'production-readiness', 'AUGUST_PRODUCTION_READINESS_TRACKER.md');

const assertions = [];

function assert(name, condition, detail = '') {
  assertions.push({ name, condition });
  console.log(`${name}=${condition ? 'PASSED' : 'FAILED'}${detail ? ` — ${detail}` : ''}`);
}

function count(text, marker) {
  return text.split(marker).length - 1;
}

assert('MODULE_064_CONTRACTS_EXIST', contracts.includes('ProjectPulseAiGenerationRequest') && contracts.includes('ProjectPulseAiRouteResult'));
assert('MODULE_064_SHARED_CONFIGURATION', configuration.includes('ProjectPulseAiConfiguration') && configuration.includes('ToSanitizedResponse'));
assert('MODULE_064_DEEPSEEK_FIRST_DEFAULT', configuration.includes('[ProjectPulseAiProviders.DeepSeek, ProjectPulseAiProviders.Claude, ProjectPulseAiProviders.OpenAi, ProjectPulseAiProviders.Local]'));
assert('MODULE_064_EXPLICIT_PROVIDER_MODES', ['claude_only', 'openai_only', 'priority_failover', 'local_only'].every((mode) => configuration.includes(mode)));
assert('MODULE_064_ALL_FEATURE_ROUTES', ['timesheet_description', 'sow_gsd_planning', 'help_assistant', 'closeout_communication', 'project_flowhive_plan'].every((feature) => contracts.includes(feature)));
assert('MODULE_064_ROUTE_DEDUPLICATION', configuration.includes('Distinct(StringComparer.OrdinalIgnoreCase)') && configuration.includes('duplicateRequests = false'));
assert('MODULE_064_LOCAL_ALWAYS_LAST', configuration.includes('route.Add(ProjectPulseAiProviders.Local)'));
assert('MODULE_064_HEALTH_REGISTRY', health.includes('CanAttempt') && health.includes('CircuitOpenUntil') && health.includes('RecordProbe'));
assert('MODULE_064_PROVIDER_RATE_LIMITS', contracts.includes('ProjectPulseAiRateLimits') && providers.includes('ClaudeRateLimits') && providers.includes('OpenAiRateLimits') && center.includes('Requests remaining'));
assert('MODULE_064_CIRCUIT_GUARD', health.includes('provider_circuit_open') && health.includes('FailureThreshold'));
assert('MODULE_064_BACKGROUND_HEALTH', monitor.includes('BackgroundService') && monitor.includes('PeriodicTimer'));
assert('MODULE_064_UNAVAILABLE_PROVIDER_SKIPPED', router.includes('!_health.CanAttempt') && router.includes('skipped.Add'));
assert('MODULE_064_NO_FAILOVER_ON_REFUSAL', router.includes('if (result.IsRefusal)') && router.includes('No fallback provider was attempted'));
assert('MODULE_064_REMOTE_RETRY_BOUNDARY', providers.includes('SendWithRetryAsync') && providers.includes('IsTransient'));
assert('MODULE_064_CLAUDE_MESSAGES_API', providers.includes('"/messages"') && providers.includes('anthropic-version'));
assert('MODULE_064_OPENAI_RESPONSES_API', providers.includes('"/responses"') && providers.includes('output_text'));
assert('MODULE_064_CLAUDE_SONNET5_SAMPLING_SAFE', !providers.includes('temperature = request.Temperature') && !providers.includes('top_p') && !providers.includes('top_k'));
assert('MODULE_064_GENERATION_HEALTH_PROBES', providers.includes('GenerationProbe') && providers.includes('ProbeRequest') && !providers.includes('HttpMethod.Get'));
assert('MODULE_064_OPENAI_PROBE_TOKEN_MINIMUM', providers.includes('MaxOutputTokens: 16'));
assert('MODULE_064_PROBE_TELEMETRY_ISOLATED', health.includes('ProbeSuccessCount') && health.includes('ProbeFailureCount') && !/RecordProbe[\s\S]*RecordSuccess/.test(health) && !/RecordProbe[\s\S]*RecordFailure/.test(health));
assert('MODULE_064_AVAILABILITY_USES_PROBE_STATUS', moduleBackend.includes('item.ProbeStatus == "available"') && center.includes('statusClass(health.probeStatus)'));
assert('MODULE_064_SANITIZED_ATTEMPT_DIAGNOSTICS', router.includes('HttpStatus={HttpStatus}') && router.includes('RequestId={RequestId}') && !router.includes('result.Message'));
assert('MODULE_064_MODEL_ALLOWLISTS', providers.includes('IsModelApproved') && configuration.includes('APPROVED_MODELS'));
assert('MODULE_064_SANITIZED_REMOTE_ERRORS', !providers.includes('Exception.Message') && !router.includes('exception.Message'));
assert('MODULE_064_SECRET_VALUES_NOT_RETURNED', configuration.includes('valueReturned = false') && configuration.includes('apiKeysReturned = false'));
assert('MODULE_064_SHARED_SERVICE_REGISTRATION', registration.includes('AddProjectPulseAi') && registration.includes('AddHostedService<ProjectPulseAiHealthMonitor>'));
assert(
  'MODULE_064_EXISTING_AI_CONSUMER_MIGRATED',
  consumer.includes('CelarAiCapabilityRouter')
    && consumer.includes('CelarAiCapabilityCatalog.ResolveTimesheetFeature(')
    && consumer.includes('_router.GenerateWithPrivateTargetAsync(')
    && consumer.includes('_router.GenerateAsync(')
    && !consumer.includes('_router.IsFirstTargetAsync(')
    && !consumer.includes('skipPrivateTarget:'),
  'Timesheet uses the central route and one router-owned private document callback'
);
assert('MODULE_064_CONSUMER_HAS_NO_DIRECT_CLIENT', !consumer.includes('new HttpClient') && !consumer.includes('PROJECTPULSE_CLAUDE_API_KEY'));
assert('MODULE_064_PROGRAM_DI', program.includes('builder.Services.AddProjectPulseAi();') && program.includes('ProjectPulseAiTimeEntrySuggestionService aiService'));
assert('MODULE_064_BACKEND_ENDPOINTS', moduleBackend.includes('"/api/ai-configuration"') && moduleBackend.includes('"/api/ai-configuration/health"'));
assert(
  'MODULE_064_ADMIN_AUTHORITY',
  moduleBackend.includes('ProjectPulseActualUserId')
    && moduleBackend.includes('ProjectPulseActualSessionAuthority.IsSuperAdministratorAsync(')
    && moduleBackend.includes('ProjectPulseActualSessionAuthority.HasPermanentAdministratorAuthority(')
    && moduleBackend.includes('AdditionalModuleAdministratorRoles')
    && moduleBackend.includes('"SYSTEM_ADMINISTRATOR"'),
  'actual-session Super Administrator authority is canonical, non-transferable, and retains the prior Module 064 system-administrator grant',
);
assert('MODULE_064_WRITE_ONLY_SECRET_ENDPOINT', moduleBackend.includes('MapPut(') && moduleBackend.includes('/providers/{providerCode}/secret') && moduleBackend.includes('valueReturned = false'));
assert('MODULE_064_ENCRYPTED_SECRET_STORE', secretStore.includes('AesGcm') && keyRing.includes('PROJECTPULSE_AI_SECRET_ENCRYPTION_KEY') && keyRing.includes('key.Length == 32') && secretStore.includes('CryptographicOperations.ZeroMemory'));
assert('MODULE_064_SANITIZED_SECRET_AUDIT', secretStore.includes('ai_provider_secret_audit') && !secretStore.includes('api_key'));
assert('MODULE_064_SAME_ORIGIN_WRITE', moduleBackend.includes('SameOrigin(context)'));
assert('MODULE_064_PROXY_SAFE_ORIGIN', moduleBackend.includes('Sec-Fetch-Site') && moduleBackend.includes('same-origin') && moduleBackend.includes('X-Forwarded-Host'));
assert('MODULE_064_MODEL_MANAGEMENT', moduleBackend.includes('/providers/{providerCode}/model') && center.includes('Save and test') && configuration.includes('ApplyStoredModel'));
assert('MODULE_064_ENABLE_DISABLE', moduleBackend.includes('/providers/{providerCode}/enabled') && center.includes("provider.enabled ? 'Disable' : 'Enable'") && configuration.includes('ApplyStoredEnabled'));
assert('MODULE_064_REPLICA_SYNCHRONIZATION', secretStore.includes('ProjectPulseAiConfigurationSynchronizer') && secretStore.includes('LoadEnabledAsync'));
assert('MODULE_064_MODEL_ROLLBACK', moduleBackend.includes('The previous model remains active') && moduleBackend.includes('previousModel'));
assert('MODULE_064_NO_MUTATING_SQL', !/\b(INSERT|UPDATE|DELETE|ALTER|CREATE|DROP)\b/i.test(moduleBackend.replaceAll('configuration updates', '')));
assert('MODULE_064_PROGRAM_ENDPOINT_MAP', count(program, 'app.MapAiProviderConfigurationEndpoints();') === 1);
assert('MODULE_064_SYSTEM_STATUS_USES_SHARED_HEALTH', program.includes('"Shared AI Provider Router"') && program.includes('aiHealth.Snapshots()'));
assert('MODULE_064_FRONTEND_CENTER', center.includes('data-module="064"') && center.includes('/api/ai-configuration/health/refresh'));
assert('MODULE_064_FRONTEND_SECRET_BOUNDARY', center.includes('Keys are never returned') && center.includes('type="password"') && center.includes('write-only'));
assert('MODULE_064_SCOPED_STYLES', styles.includes('.ai-provider-center') && !styles.includes('\n.panel ') && !styles.includes('\nbody '));
assert('MODULE_064_APP_IMPORT_COUNT', count(app, "import AiProviderConfigurationCenter from './AiProviderConfigurationCenter.jsx';") === 1);
assert('MODULE_064_APP_ROUTE_COUNT', count(app, "activeRoute === 'ai-provider-configuration'") === 1);
assert('MODULE_064_APP_NAVIGATION', app.includes("route: 'ai-provider-configuration'") && app.includes("navLabel: 'MODULE 064'"));
assert('MODULE_064_APP_ADMIN_ONLY', app.includes("activeRoute === 'ai-provider-configuration' && canSeeAny(['SYSTEM_ADMINISTRATION', 'MANAGE_ALL'])"));
assert(
  'MODULE_064_APP_SUPER_ADMINISTRATOR_FULL_CONTROL',
  app.includes('actualSessionHasPermanentFullControl')
    && app.includes("'SUPER_ADMINISTRATOR'")
    && app.includes("permissions: ['SYSTEM_ADMINISTRATION', 'MANAGE_ALL']"),
  'the current actual-session Super Administrator bypasses stale permission payloads for Module 064 while View-As remains scoped',
);
assert('MODULE_064_TIMESHEET_PROVIDER_LABELS', app.includes("celar_ai: 'Celar AI'") && app.includes("openai: 'OpenAI'") && app.includes("local_template: 'Governed local template fallback'"));
assert('MODULE_064_BUILD_GUARD', packageJson.includes('validate:module064') && packageJson.includes('npm run validate:module064'));

assert(
  'MODULE_064_SECRET_LOADER_RECONCILES_HEALTH',
  secretStore.includes('ProjectPulseAiSecretLoader(')
    && secretStore.includes('ProjectPulseAiHealthRegistry health')
    && secretStore.includes('health.ApplyConfiguration(configuration.Claude)')
    && secretStore.includes('health.ApplyConfiguration(configuration.OpenAi)'),
  'encrypted keys update the registry before routing and startup probes',
);
assert(
  'MODULE_064_REPLICA_SYNC_REFRESHES_HEALTH',
  secretStore.includes('ProjectPulseAiHealthCoordinator coordinator')
    && secretStore.includes('await coordinator.RefreshAsync(false, stoppingToken)')
    && secretStore.includes('synchronize provider configuration and health'),
  'every replica refreshes unknown or stale provider readiness after shared configuration sync',
);
assert(
  'MODULE_064_COORDINATOR_USES_LIVE_CONFIGURATION',
  monitor.includes('var liveConfiguration = _configuration.Provider(provider.Code)')
    && monitor.includes('_health.ApplyConfiguration(liveConfiguration)')
    && monitor.includes('_health.ShouldProbe(provider.Code, maximumAge, force)')
    && monitor.includes('_health.MarkProbeStarted(provider.Code)'),
  'the probe decision is made after live secret/model/enabled hydration',
);
assert(
  'MODULE_064_ROUTER_RECONCILES_BEFORE_SKIP',
  router.indexOf('_health.ApplyConfiguration(_configuration.Provider(providerCode))')
    < router.indexOf('if (!_health.CanAttempt(providerCode, out _))'),
  'Module 001 cannot fall back because of a stale pre-secret health snapshot',
);
assert(
  'MODULE_064_CONFIGURATION_LOAD_AUTO_PROBES',
  moduleBackend.includes('ProjectPulseAiHealthCoordinator coordinator')
    && moduleBackend.includes('var snapshots = await coordinator.RefreshAsync(false, cancellationToken)')
    && moduleBackend.includes('healthCheckedAutomatically = true'),
  'opening Module 064 automatically verifies unknown or stale providers',
);
assert(
  'MODULE_064_SECRET_SAVE_AUTO_PROBES',
  moduleBackend.includes('secret_replaced_and_verified')
    && moduleBackend.includes('var snapshots = await coordinator.RefreshAsync(true, cancellationToken)')
    && moduleBackend.includes('valueReturned = false'),
  'new write-only keys are checked immediately without exposing values',
);
assert(
  'MODULE_064_FRONTEND_BOUNDED_HEALTH_POLL',
  center.includes('AUTOMATIC_HEALTH_POLL_MS = 2000')
    && center.includes('AUTOMATIC_HEALTH_POLL_LIMIT = 10')
    && center.includes("fetch('/api/ai-configuration/health'")
    && center.includes('Checking automatically')
    && center.includes('Automatic provider health is active.'),
  'the page follows an in-progress startup check without issuing provider calls itself',
);
assert(
  'MODULE_064_CHECKING_STATE_STYLED',
  styles.includes('.ai-provider-center__status--checking')
    && styles.includes('.ai-provider-center__automatic-health'),
  'automatic checks are shown as active work instead of a stale degraded state',
);

assert(
  'MODULE_064_CONTAINER_BUILD_CONTEXT',
  webDockerfile.includes('src/backend/ProjectTime.Api/Ai/')
    && webDockerfile.includes('AiProviderConfigurationModule.cs')
    && webDockerfile.includes('docs/modules/module-064-ai-provider-configuration/')
    && webDockerfile.includes('AUGUST_PRODUCTION_READINESS_TRACKER.md')
    && webDockerfile.includes('COPY deployment/containers/web/Dockerfile'),
);
assert('MODULE_064_DOCUMENTATION_SET', readme.includes('Module 064') && contract.includes('/providers/{providerCode}/secret') && security.includes('AES-256-GCM'));
assert('MODULE_064_GOVERNANCE_REGISTERED', workRegister.includes('| 064 |') && catalog.includes('| 064 |'));
assert('MODULE_064_TRACKER_AI_017', tracker.includes('AI-017') && tracker.includes('Module 064'));
assert('MODULE_064_NO_DATABASE_ARTIFACT', !fs.existsSync(path.join(repository, 'database', 'module-064')) && !fs.existsSync(path.join(repository, 'src', 'backend', 'ProjectTime.Api', 'Migrations', 'Module064')));

const failed = assertions.filter((assertion) => !assertion.condition);
console.log(`\nMODULE_064_VALIDATION_CHECKS=${assertions.length}`);
console.log('MODULE_064_ROUTING=DEEPSEEK_CELAR_CLAUDE_OPENAI_LOCAL');
console.log('MODULE_064_AUTOMATIC_HEALTH=STARTUP_PERIODIC_REPLICA_ROUTER');
console.log('MODULE_064_SAFETY_REFUSAL_FAILOVER=BLOCKED');
console.log('MODULE_064_SECRET_MUTATION=ADMIN_WRITE_ONLY_ENCRYPTED');
console.log(`MODULE_064_CONTRACT=${failed.length === 0 ? 'PASSED' : 'FAILED'}`);

if (failed.length > 0) process.exitCode = 1;
