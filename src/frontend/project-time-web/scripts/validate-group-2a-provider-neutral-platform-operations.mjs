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
const routeSources = `${operations}\n${architecture}`;
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

check('SHARED_ADAPTER_CONTRACT', [
  'private interface IPlatformAdapter',
  'PlatformIdentity',
  'ResourceSnapshot',
  'DependencySnapshot',
  'IntegrationStatus[]',
  'DeploymentEntry[]',
  'ReplicaEntry[]',
  'ProviderSpecificDetails'
].every((value) => contracts.includes(value)),
'generic platform, runtime, resource, dependency, integration, deployment, replica, and provider-detail concepts share one contract');

check('PROVIDER_SELECTION', [
  '"azure_adapter"',
  '"opencloud_adapter"',
  '"generic_cloud_adapter"',
  '"generic_container_adapter"',
  '"local_runtime_adapter"'
].every((value) => contracts.includes(value)),
'Azure, OpenCloud, future cloud, container, and local/server adapters use the same boundary');

check('PRIMARY_MODEL_NOT_AZURE_SPECIFIC', [
  'string Provider',
  'string Region',
  'string WorkloadKind',
  'string Deployment',
  'Dictionary<string, string> ProviderSpecificDetails'
].every((value) => contracts.includes(value)),
'Azure-only values stay inside provider-specific details instead of the required primary model');

check('RESOURCE_METRICS', [
  'process.WorkingSet64',
  'process.PrivateMemorySize64',
  'process.TotalProcessorTime',
  'GC.GetTotalMemory(false)',
  'ReadMemory()',
  'ReadDrives()'
].every((value) => operations.includes(value))
  && contracts.includes('/proc/meminfo')
  && contracts.includes('/sys/fs/cgroup/memory.current'),
'CPU, process/container memory, total/available RAM, and disk metrics use provider-neutral runtime sources');

check('DEPENDENCY_HEALTH', [
  'CheckDatabaseAsync',
  'CheckStorage',
  'LoadIntegrationsAsync',
  'crm_integration_providers',
  'Microsoft Integration',
  'Global mail delivery'
].every((value) => operations.includes(value)),
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
    routeSources.includes(`"${route}"`), route);
}

check('ALL_ENDPOINT_DISCOVERY', [
  'GetServices<EndpointDataSource>()',
  'SelectMany(source => source.Endpoints)',
  'OfType<RouteEndpoint>()',
  'HttpMethodMetadata',
  'BuildApiInventory(context)'
].every((value) => operations.includes(value)),
'running ASP.NET endpoint metadata drives the API inventory');

check('API_DIAGNOSTIC_FIELDS', [
  'AuthenticationRequirement',
  'PermissionRequirement',
  'LastSuccessfulRequestAt',
  'LastFailureAt',
  'LastErrorCode',
  'CorrelationId'
].every((value) => contracts.includes(value))
  && operations.includes('DependenciesFor(path)')
  && operations.includes('PurposeFor(path, endpoint.DisplayName)'),
'method, path, owner, purpose, auth, permissions, dependencies, status, latency, success/failure, and correlation fields exist');

check('BOUNDED_SANITIZED_TELEMETRY', [
  'UsePlatformOperationsTelemetry',
  'ConcurrentQueue<OperationalEvidence>',
  'ConcurrentDictionary<string, ApiObservation>',
  'MaximumEvidenceEvents = 2000',
  'SanitizeRequestPath',
  'X-ProjectPulse-Correlation-Id',
  'requestBodiesCaptured = false',
  'queryStringsCaptured = false',
  'providerCredentialsCaptured = false',
  'rawExceptionMessagesReturned = false'
].every((value) => contracts.includes(value))
  && !contracts.includes('Request.Body.Read')
  && !contracts.includes('exception.Message'),
'bounded evidence excludes bodies, query strings, credentials, and raw exception messages');

check('OWN_SESSION_SECURITY', [
  'ProjectPulseActualUserId',
  'ProjectPulseSessionUserId',
  'SUPER_ADMINISTRATOR',
  'ADMINISTRATOR',
  'SYSTEM_ADMINISTRATION',
  'MANAGE_ALL',
  'requireOwnSession && IsViewAs(context)'
].every((value) => contracts.includes(value)),
'actual-session administrator authorization and View-As retest denial are server enforced');

check('SAFE_RETEST', [
  'Only safe read-only GET routes can be retested',
  'SameOrigin(context)',
  'responseBodyRead = false',
  'X-ProjectPulse-Diagnostic-Retest',
  'path.Contains("callback"',
  'path.Contains("download"',
  'path.Contains("export"'
].every((value) => operations.includes(value)),
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
'shared middleware and endpoints are registered exactly once without editing oversized Program.cs');

check('MODULE_013_EXPERIENCE', [
  'System Health &amp; API Diagnostics',
  '/api/platform-operations/overview',
  '/api/platform-operations/apis',
  'api-diagnostic-drawer',
  'Suggested troubleshooting',
  'Recent failures and logs',
  'Retest API',
  'Not supported by the current deployment model'
].every((value) => module013.includes(value)),
'Module 013 is the first-response health and per-API troubleshooting workspace');

check('MODULE_013_RESPONSIVE', [
  '.platform-identity-strip',
  '.resource-metric-grid',
  '.api-filter-grid',
  '.api-diagnostic-drawer',
  '@media (max-width: 620px)'
].every((value) => module013Css.includes(value)),
'Module 013 is searchable, readable, bounded, and responsive');

check('MODULE_016_EVIDENCE',
  module016.includes('<OperationalEvidenceCenter')
    && [
      'Operational Evidence &amp; Diagnostic History',
      '/api/platform-operations/evidence?',
      '/api/platform-operations/evidence/export',
      'Dependency timeline',
      'Workers and scheduled jobs',
      'Correlation ID'
    ].every((value) => evidence.includes(value)),
'Module 016 provides logs, failures, dependencies, workers, jobs, correlations, and export');

check('MODULE_016_LEGACY_BACKUP_PRESERVED',
  module016.includes('<LegacyBackupRetentionCenter')
    && legacyBackup.includes('/api/system/backup-retention/status')
    && legacyBackup.includes('/api/system/backup-retention/delete')
    && legacyBackup.includes('restore-point protection'),
'previous backup-retention inventory and guarded deletion remain available');

check('MODULE_016_RESPONSIVE', [
  '.operational-evidence-filter',
  '.operational-evidence-table-wrap',
  '.operational-evidence-two-column',
  '@media (max-width: 620px)'
].every((value) => evidenceCss.includes(value)),
'Module 016 has a responsive evidence workspace');

check('MODULE_068_LIVE_ARCHITECTURE', [
  '/api/platform-operations/architecture',
  'ProjectPulse Platform Operations',
  'Azure adapter',
  'OpenCloud adapter',
  'Other provider adapter',
  'moduleApiRelationships',
  'externalDataFlows',
  'redundancy'
].every((value) => module068.includes(value)),
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

check('MODULE_068_RESPONSIVE', [
  '.provider-adapter-map',
  '.system-architecture-layers',
  '.module-api-relationship-list',
  '@media (max-width: 700px)'
].every((value) => module068Css.includes(value)),
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
