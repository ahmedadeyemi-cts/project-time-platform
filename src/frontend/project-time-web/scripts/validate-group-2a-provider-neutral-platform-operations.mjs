import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repoRoot = fileURLToPath(new URL('../../../../', import.meta.url));
const absolute = (relative) => path.join(repoRoot, relative);
const exists = (relative) => fs.existsSync(absolute(relative));
const read = (relative) => fs.readFileSync(absolute(relative), 'utf8');
const checks = [];

function check(name, condition, evidence) {
  checks.push({ name, condition, evidence });
  console.log(`GROUP_2A_${name}=${condition ? 'PASSED' : 'FAILED'} — ${evidence}`);
}

const paths = {
  contracts: 'src/backend/ProjectTime.Api/Modules/PlatformOperationsContracts.cs',
  operations: 'src/backend/ProjectTime.Api/Modules/PlatformOperationsModule.cs',
  architecture: 'src/backend/ProjectTime.Api/Modules/PlatformOperationsArchitecture.cs',
  project: 'src/backend/ProjectTime.Api/ProjectTime.Api.csproj',
  module013: 'src/frontend/project-time-web/src/ServiceControlCenter.jsx',
  module013Css: 'src/frontend/project-time-web/src/service-control-center.css',
  module016: 'src/frontend/project-time-web/src/BackupRetentionCenter.jsx',
  evidence: 'src/frontend/project-time-web/src/OperationalEvidenceCenter.jsx',
  evidenceCss: 'src/frontend/project-time-web/src/operational-evidence-center.css',
  legacyBackup: 'src/frontend/project-time-web/src/LegacyBackupRetentionCenter.jsx',
  module068: 'src/frontend/project-time-web/src/SystemArchitectureCenter.jsx',
  module068Css: 'src/frontend/project-time-web/src/system-architecture-center.css',
  registry: 'src/frontend/project-time-web/src/module-availability-registry.js',
  compatibilityValidator: 'src/frontend/project-time-web/scripts/validate-module-068-system-architecture.mjs',
  app: 'src/frontend/project-time-web/src/App.jsx'
};

for (const [key, relative] of Object.entries(paths)) {
  check(`FILE_${key.toUpperCase()}`, exists(relative), relative);
}

const contracts = read(paths.contracts);
const operations = read(paths.operations);
const architecture = read(paths.architecture);
const project = read(paths.project);
const module013 = read(paths.module013);
const module013Css = read(paths.module013Css);
const module016 = read(paths.module016);
const evidence = read(paths.evidence);
const evidenceCss = read(paths.evidenceCss);
const legacyBackup = read(paths.legacyBackup);
const module068 = read(paths.module068);
const module068Css = read(paths.module068Css);
const registry = read(paths.registry);
const compatibilityValidator = read(paths.compatibilityValidator);
const app = read(paths.app);

check('SHARED_ADAPTER_CONTRACT',
  contracts.includes('private interface IPlatformAdapter')
    && contracts.includes('PlatformIdentity')
    && contracts.includes('ResourceSnapshot')
    && contracts.includes('DependencySnapshot')
    && contracts.includes('IntegrationStatus[]')
    && contracts.includes('DeploymentEntry[]')
    && contracts.includes('ReplicaEntry[]')
    && contracts.includes('ProviderSpecificDetails'),
  'generic provider, runtime, resources, dependencies, integrations, deployments, replicas, and details are one contract');
check('PROVIDER_SELECTION',
  contracts.includes('"azure_adapter"')
    && contracts.includes('"opencloud_adapter"')
    && contracts.includes('"generic_cloud_adapter"')
    && contracts.includes('"generic_container_adapter"')
    && contracts.includes('"local_runtime_adapter"'),
  'Azure, OpenCloud, future cloud, container, and local/server adapters share the same boundary');
check('PRIMARY_MODEL_NOT_AZURE_SPECIFIC',
  contracts.includes('string Provider')
    && contracts.includes('string Region')
    && contracts.includes('string WorkloadKind')
    && contracts.includes('string Deployment')
    && contracts.includes('Dictionary<string, string> ProviderSpecificDetails'),
  'provider-specific Azure fields are isolated from the primary model');
check('RESOURCE_METRICS',
  operations.includes('process.WorkingSet64')
    && operations.includes('process.PrivateMemorySize64')
    && operations.includes('process.TotalProcessorTime')
    && operations.includes('GC.GetTotalMemory(false)')
    && operations.includes('ReadMemory()')
    && operations.includes('ReadDrives()')
    && contracts.includes('/proc/meminfo')
    && contracts.includes('/sys/fs/cgroup/memory.current'),
  'CPU, process/container memory, total/available RAM, and disk metrics use cross-platform sources');
check('DEPENDENCY_HEALTH',
  operations.includes('CheckDatabaseAsync')
    && operations.includes('CheckStorage')
    && operations.includes('LoadIntegrationsAsync')
    && operations.includes('crm_integration_providers')
    && operations.includes('Microsoft Integration')
    && operations.includes('Global mail delivery'),
  'database, storage, Microsoft, mail, CRM/ERP, and GitHub dependencies are represented without secret readback');

for (const route of [
  '/api/platform-operations/overview',
  '/api/platform-operations/apis',
  '/api/platform-operations/apis/{apiId}',
  '/api/platform-operations/apis/{apiId}/retest',
  '/api/platform-operations/evidence',
  '/api/platform-operations/evidence/export',
  '/api/platform-operations/architecture',
  '/api/platform-operations/architecture/export'
]) {
  check(`ROUTE_${route.replace(/[^a-z0-9]+/gi, '_').toUpperCase()}`,
    operations.includes(`"${route}"`), route);
}

check('ALL_ENDPOINT_DISCOVERY',
  operations.includes('GetServices<EndpointDataSource>()')
    && operations.includes('SelectMany(source => source.Endpoints)')
    && operations.includes('OfType<RouteEndpoint>()')
    && operations.includes('HttpMethodMetadata')
    && operations.includes('BuildApiInventory(context)'),
  'running ASP.NET endpoint metadata drives the API inventory');
check('API_DIAGNOSTIC_FIELDS',
  contracts.includes('AuthenticationRequirement')
    && contracts.includes('PermissionRequirement')
    && contracts.includes('LastSuccessfulRequestAt')
    && contracts.includes('LastFailureAt')
    && contracts.includes('LastErrorCode')
    && contracts.includes('CorrelationId')
    && operations.includes('DependenciesFor(path)')
    && operations.includes('PurposeFor(path, endpoint.DisplayName)'),
  'method, path, owner, purpose, auth, permissions, dependencies, status, latency, success/failure, and correlation fields exist');
check('TELEMETRY_MIDDLEWARE',
  contracts.includes('UsePlatformOperationsTelemetry')
    && contracts.includes('ConcurrentQueue<OperationalEvidence>')
    && contracts.includes('ConcurrentDictionary<string, ApiObservation>')
    && contracts.includes('MaximumEvidenceEvents = 2000')
    && contracts.includes('SanitizeRequestPath')
    && contracts.includes('X-ProjectPulse-Correlation-Id'),
  'bounded sanitized request telemetry and per-API observations are collected');
check('NO_SENSITIVE_TELEMETRY',
  contracts.includes('requestBodiesCaptured = false')
    && contracts.includes('queryStringsCaptured = false')
    && contracts.includes('providerCredentialsCaptured = false')
    && contracts.includes('rawExceptionMessagesReturned = false')
    && !contracts.includes('Request.Body.Read')
    && !contracts.includes('exception.Message'),
  'request bodies, query strings, credentials, and raw exception messages are excluded');
check('OWN_SESSION_SECURITY',
  contracts.includes('ProjectPulseActualUserId')
    && contracts.includes('ProjectPulseSessionUserId')
    && contracts.includes('SUPER_ADMINISTRATOR')
    && contracts.includes('ADMINISTRATOR')
    && contracts.includes('SYSTEM_ADMINISTRATION')
    && contracts.includes('MANAGE_ALL')
    && contracts.includes('requireOwnSession && IsViewAs(context)'),
  'actual-session administrator authorization and View-As retest denial are server enforced');
check('SAFE_RETEST',
  operations.includes('Only safe read-only GET routes can be retested')
    && operations.includes('SameOrigin(context)')
    && operations.includes('responseBodyRead = false')
    && operations.includes('X-ProjectPulse-Diagnostic-Retest')
    && operations.includes('path.Contains("callback"')
    && operations.includes('path.Contains("download"')
    && operations.includes('path.Contains("export"'),
  'same-origin GET-only retest excludes parameters, callbacks, downloads, exports, and response bodies');
check('RESTART_TRUTHFULNESS',
  operations.includes('restart_http_route')
    && operations.includes('Routes share one API process and cannot be restarted independently')
    && operations.includes('adapter_required')
    && operations.includes('connector_required')
    && contracts.includes('restartExecutionEnabled = false')
    && contracts.includes('productionChangingActionsEnabled = false'),
  'unsupported route restart is explicit and production actions remain locked');
check('GENERATED_REGISTRATION',
  project.includes('app.UsePlatformOperationsTelemetry();')
    && project.includes('app.MapPlatformOperationsEndpoints();')
    && (project.match(/app\.UsePlatformOperationsTelemetry\(\);/g) ?? []).length === 1
    && (project.match(/app\.MapPlatformOperationsEndpoints\(\);/g) ?? []).length === 1,
  'shared middleware and routes are registered exactly once without editing oversized Program.cs');

check('MODULE_013_EXPERIENCE',
  module013.includes('System Health & API Diagnostics')
    && module013.includes("/api/platform-operations/overview")
    && module013.includes("/api/platform-operations/apis")
    && module013.includes('api-diagnostic-drawer')
    && module013.includes('Suggested troubleshooting')
    && module013.includes('Recent failures and logs')
    && module013.includes('Retest API')
    && module013.includes('Not supported by the current deployment model'),
  'Module 013 is the first-response health and per-API troubleshooting point');
check('MODULE_013_RESPONSIVE',
  module013Css.includes('.platform-identity-strip')
    && module013Css.includes('.resource-metric-grid')
    && module013Css.includes('.api-filter-grid')
    && module013Css.includes('.api-diagnostic-drawer')
    && module013Css.includes('@media (max-width: 620px)'),
  'Module 013 is searchable, readable, bounded, and responsive');

check('MODULE_016_EVIDENCE',
  module016.includes('<OperationalEvidenceCenter')
    && evidence.includes('Operational Evidence & Diagnostic History')
    && evidence.includes('/api/platform-operations/evidence?')
    && evidence.includes('/api/platform-operations/evidence/export')
    && evidence.includes('Dependency timeline')
    && evidence.includes('Workers and scheduled jobs')
    && evidence.includes('Correlation ID'),
  'Module 016 provides logs, failures, dependencies, workers, jobs, correlations, and export');
check('MODULE_016_LEGACY_BACKUP_PRESERVED',
  module016.includes('<LegacyBackupRetentionCenter')
    && legacyBackup.includes('/api/system/backup-retention/status')
    && legacyBackup.includes('/api/system/backup-retention/delete')
    && legacyBackup.includes('restore-point protection'),
  'previous backup-retention inventory and guarded deletion remain available');
check('MODULE_016_RESPONSIVE',
  evidenceCss.includes('.operational-evidence-filter')
    && evidenceCss.includes('.operational-evidence-table-wrap')
    && evidenceCss.includes('.operational-evidence-two-column')
    && evidenceCss.includes('@media (max-width: 620px)'),
  'Module 016 has a responsive evidence workspace');

check('MODULE_068_LIVE_ARCHITECTURE',
  module068.includes('/api/platform-operations/architecture')
    && module068.includes('ProjectPulse Platform Operations')
    && module068.includes('Azure adapter')
    && module068.includes('OpenCloud adapter')
    && module068.includes('Other provider adapter')
    && module068.includes('moduleApiRelationships')
    && module068.includes('externalDataFlows')
    && module068.includes('redundancy'),
  'Module 068 displays provider adapters, components, integrations, API ownership, data flows, regions, and redundancy');
check('MODULE_068_EXPORT',
  module068.includes('usSignalLogoDataUrl')
    && module068.includes('Export branded architecture')
    && architecture.includes('ProjectTime.Api.Assets.Branding.USSNavyStacked.png')
    && architecture.includes('Created by Ahmed Adeyemi')
    && architecture.includes('API appendix')
    && architecture.includes('Release SHA')
    && architecture.includes('Generated'),
  'official logo, environment/provider/release, legend, API appendix, date, and requested footer are exported');
check('MODULE_068_READ_ONLY',
  !/method\s*:\s*['"](?:POST|PUT|PATCH|DELETE)['"]/i.test(module068)
    && !/<form\b/i.test(module068)
    && module068.includes('data-mode="read-only"'),
  'architecture observation and export remain read-only');
check('MODULE_068_RESPONSIVE',
  module068Css.includes('.provider-adapter-map')
    && module068Css.includes('.system-architecture-layers')
    && module068Css.includes('.module-api-relationship-list')
    && module068Css.includes('@media (max-width: 700px)'),
  'architecture map and API appendix are proportional and responsive');

check('REGISTRY_RESPONSIBILITIES',
  registry.includes("displayName: 'System Health & API Diagnostics'")
    && registry.includes("displayName: 'Operational Evidence & Backup Retention'")
    && registry.includes("displayName: 'Provider-Neutral System Architecture'"),
  'registry names reflect current responsibilities without changing route numbers');
check('SINGLE_EXISTING_MOUNTS',
  (app.match(/<ServiceControlCenter authSession=\{authSession\} \/>/g) ?? []).length === 1
    && (app.match(/<BackupRetentionCenter authSession=\{authSession\} \/>/g) ?? []).length === 1
    && (app.match(/<SystemArchitectureCenter authSession=\{authSession\} \/>/g) ?? []).length === 1,
  'existing module routes mount once without a secondary React root');
check('CONTAINER_COMPATIBILITY_VALIDATOR',
  compatibilityValidator.includes('SKIPPED_FRONTEND_CONTAINER_CONTEXT')
    && compatibilityValidator.includes('FULL_PROVIDER_CONTRACT'),
  'frontend-only container builds validate presentation while full CI validates backend source');
check('NO_MIGRATION_OR_DEPLOYMENT',
  !exists('database/migrations/050_provider_neutral_platform_operations.sql')
    && !exists('.github/workflows/projectpulse-deploy-group-2a.yml'),
  'source issue creates no migration or deployment action');

console.log(`GROUP_2A_VALIDATION_CHECKS=${checks.length}`);
console.log('GROUP_2A_ACTIVE_PROVIDER=AZURE_WHEN_DETECTED');
console.log('GROUP_2A_FUTURE_PROVIDER=OPENCLOUD_ADAPTER_CONTRACT');
console.log('GROUP_2A_ROUTE_RESTART=NOT_SUPPORTED_SHARED_PROCESS');
console.log('GROUP_2A_PRODUCTION_ACTIONS=LOCKED');
console.log('GROUP_2A_EXTERNAL_CALLS_PERFORMED=0');

const failed = checks.filter((item) => !item.condition);
if (failed.length) {
  console.error('GROUP_2A_CONTRACT=FAILED');
  failed.forEach((item) => console.error(`- ${item.name}: ${item.evidence}`));
  process.exit(1);
}
console.log('GROUP_2A_CONTRACT=PASSED');
