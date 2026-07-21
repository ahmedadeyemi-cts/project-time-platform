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
assert('MODULE_064_CLAUDE_FIRST_DEFAULT', configuration.includes('[ProjectPulseAiProviders.Claude, ProjectPulseAiProviders.OpenAi, ProjectPulseAiProviders.Local]'));
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
assert('MODULE_064_MODEL_ALLOWLISTS', providers.includes('IsModelApproved') && configuration.includes('APPROVED_MODELS'));
assert('MODULE_064_SANITIZED_REMOTE_ERRORS', !providers.includes('Exception.Message') && !router.includes('exception.Message'));
assert('MODULE_064_SECRET_VALUES_NOT_RETURNED', configuration.includes('valueReturned = false') && configuration.includes('apiKeysReturned = false'));
assert('MODULE_064_SHARED_SERVICE_REGISTRATION', registration.includes('AddProjectPulseAi') && registration.includes('AddHostedService<ProjectPulseAiHealthMonitor>'));
assert('MODULE_064_EXISTING_AI_CONSUMER_MIGRATED', consumer.includes('ProjectPulseAiRouter') && consumer.includes('ProjectPulseAiFeatures.TimesheetDescription'));
assert('MODULE_064_CONSUMER_HAS_NO_DIRECT_CLIENT', !consumer.includes('new HttpClient') && !consumer.includes('PROJECTPULSE_CLAUDE_API_KEY'));
assert('MODULE_064_PROGRAM_DI', program.includes('builder.Services.AddProjectPulseAi();') && program.includes('ProjectPulseAiTimeEntrySuggestionService aiService'));
assert('MODULE_064_BACKEND_ENDPOINTS', moduleBackend.includes('"/api/ai-configuration"') && moduleBackend.includes('"/api/ai-configuration/health"'));
assert('MODULE_064_ADMIN_AUTHORITY', moduleBackend.includes('ProjectPulseActualUserId') && moduleBackend.includes('AdministratorRoles'));
assert('MODULE_064_WRITE_ONLY_SECRET_ENDPOINT', moduleBackend.includes('MapPut(') && moduleBackend.includes('/providers/{providerCode}/secret') && moduleBackend.includes('valueReturned = false'));
assert('MODULE_064_ENCRYPTED_SECRET_STORE', secretStore.includes('AesGcm') && secretStore.includes('PROJECTPULSE_AI_SECRET_ENCRYPTION_KEY') && secretStore.includes('CryptographicOperations.ZeroMemory'));
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
assert('MODULE_064_TIMESHEET_PROVIDER_LABELS', app.includes("openai: 'OpenAI'") && app.includes("local_template: 'Governed local template fallback'"));
assert('MODULE_064_BUILD_GUARD', packageJson.includes('validate:module064') && packageJson.includes('npm run validate:module064'));
assert(
  'MODULE_064_CONTAINER_BUILD_CONTEXT',
  webDockerfile.includes('src/backend/ProjectTime.Api/Ai/') &&
    webDockerfile.includes('AiProviderConfigurationModule.cs') &&
    webDockerfile.includes('docs/modules/module-064-ai-provider-configuration/') &&
    webDockerfile.includes('AUGUST_PRODUCTION_READINESS_TRACKER.md') &&
    webDockerfile.includes('COPY deployment/containers/web/Dockerfile'),
);
assert('MODULE_064_DOCUMENTATION_SET', readme.includes('Module 064') && contract.includes('/providers/{providerCode}/secret') && security.includes('AES-256-GCM'));
assert('MODULE_064_GOVERNANCE_REGISTERED', workRegister.includes('| 064 |') && catalog.includes('| 064 |'));
assert('MODULE_064_TRACKER_AI_017', tracker.includes('AI-017') && tracker.includes('Module 064'));
assert('MODULE_064_NO_DATABASE_ARTIFACT', !fs.existsSync(path.join(repository, 'database', 'module-064')) && !fs.existsSync(path.join(repository, 'src', 'backend', 'ProjectTime.Api', 'Migrations', 'Module064')));

const failed = assertions.filter((assertion) => !assertion.condition);
console.log(`\nMODULE_064_VALIDATION_CHECKS=${assertions.length}`);
console.log('MODULE_064_ROUTING=CLAUDE_OPENAI_LOCAL');
console.log('MODULE_064_SAFETY_REFUSAL_FAILOVER=BLOCKED');
console.log('MODULE_064_SECRET_MUTATION=ADMIN_WRITE_ONLY_ENCRYPTED');
console.log(`MODULE_064_CONTRACT=${failed.length === 0 ? 'PASSED' : 'FAILED'}`);

if (failed.length > 0) process.exitCode = 1;
