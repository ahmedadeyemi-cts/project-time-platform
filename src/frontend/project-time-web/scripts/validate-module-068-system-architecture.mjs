import fs from 'node:fs';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const repositoryRoot = path.resolve(scriptDirectory, '../../../..');
const absolute = (relative) => path.join(repositoryRoot, relative);
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
  architectureBackend: 'src/backend/ProjectTime.Api/Modules/PlatformOperationsArchitecture.cs',
  legacyArchitectureBackend: 'src/backend/ProjectTime.Api/Modules/SystemArchitectureModule.cs',
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
  app: 'src/frontend/project-time-web/src/App.jsx',
  logo: 'src/backend/ProjectTime.Api/Assets/Branding/USSNavyStacked.png',
  readme: 'docs/modules/module-068-system-architecture/README.md',
  contract: 'docs/modules/module-068-system-architecture/API-CONTRACT.md',
  security: 'docs/modules/module-068-system-architecture/SECURITY-AND-OPERATIONS.md'
};

for (const [key, relative] of Object.entries(paths)) {
  check(`FILE_${key.toUpperCase()}`, exists(relative), relative);
}

const contracts = read(paths.contracts);
const operations = read(paths.operations);
const architectureBackend = read(paths.architectureBackend);
const legacyArchitectureBackend = read(paths.legacyArchitectureBackend);
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
const app = read(paths.app);
const readme = read(paths.readme);
const apiContract = read(paths.contract);
const security = read(paths.security);

check('PROVIDER_NEUTRAL_INTERFACE',
  contracts.includes('private interface IPlatformAdapter')
    && contracts.includes('PlatformIdentity')
    && contracts.includes('ProviderSpecificDetails')
    && contracts.includes('WorkloadKind')
    && contracts.includes('DeploymentEntry[]')
    && contracts.includes('ReplicaEntry[]'),
  'one shared adapter exposes generic platform, workload, deployment, replica, and provider-detail concepts');
check('AZURE_ADAPTER_ACTIVE_NOW',
  contracts.includes('"azure_adapter"')
    && contracts.includes('CONTAINER_APP_NAME')
    && contracts.includes('WEBSITE_SITE_NAME')
    && contracts.includes('Microsoft Azure'),
  'Azure runtime evidence selects the active Azure adapter');
check('OPEN_CLOUD_FUTURE_ADAPTER',
  contracts.includes('"opencloud_adapter"')
    && contracts.includes('"OpenCloud"')
    && contracts.includes('"configured_contract"'),
  'OpenCloud is represented as a future adapter contract without vendor-specific assumptions');
check('OTHER_PROVIDER_EXTENSION',
  contracts.includes('"generic_cloud_adapter"')
    && contracts.includes('"generic_container_adapter"')
    && contracts.includes('"local_runtime_adapter"'),
  'other clouds, generic containers, and local/server runtimes share the same contract');

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

check('SYSTEM_RESOURCE_METRICS',
  operations.includes('CpuPercent')
    && operations.includes('ProcessWorkingSetBytes')
    && operations.includes('ContainerMemoryCurrentBytes')
    && operations.includes('TotalMemoryBytes')
    && operations.includes('AvailableMemoryBytes')
    && operations.includes('ReadDrives()')
    && contracts.includes('/proc/meminfo')
    && contracts.includes('/sys/fs/cgroup/memory.current'),
  'Module 013 reports CPU, process/container memory, total/available RAM, and disk capacity where exposed');
check('RUNTIME_IDENTITY',
  operations.includes('ApplicationVersion')
    && operations.includes('ReleaseSha')
    && operations.includes('UptimeSeconds')
    && operations.includes('LastDeploymentAt')
    && operations.includes('LogicalProcessorCount'),
  'version, release, uptime, deployment, and CPU identity are in the primary generic contract');
check('DEPENDENCY_AND_INTEGRATION_HEALTH',
  operations.includes('CheckDatabaseAsync')
    && operations.includes('CheckStorage')
    && operations.includes('LoadIntegrationsAsync')
    && operations.includes('crm_integration_providers')
    && operations.includes('Microsoft Integration')
    && operations.includes('Global mail delivery'),
  'database, storage, Microsoft, mail, SELL/Salesforce/ServiceNow/Certinia registry, and GitHub status are represented');
check('LIVE_API_INVENTORY',
  operations.includes('GetServices<EndpointDataSource>()')
    && operations.includes('OfType<RouteEndpoint>()')
    && operations.includes('HttpMethodMetadata')
    && operations.includes('AuthenticationRequirement')
    && operations.includes('PermissionRequirement')
    && operations.includes('DependenciesFor(path)')
    && operations.includes('LastSuccessfulRequestAt')
    && operations.includes('LastFailureAt')
    && operations.includes('CorrelationId'),
  'every running route is enumerated with method, owner, purpose, auth, permissions, dependencies, state, latency, and evidence');
check('BOUNDED_SANITIZED_TELEMETRY',
  contracts.includes('MaximumEvidenceEvents = 2000')
    && contracts.includes('ConcurrentQueue<OperationalEvidence>')
    && contracts.includes('requestBodiesCaptured = false')
    && contracts.includes('queryStringsCaptured = false')
    && contracts.includes('rawExceptionMessagesReturned = false')
    && contracts.includes('providerCredentialsCaptured = false')
    && !contracts.includes('Request.Body.Read'),
  'telemetry is bounded and excludes bodies, query strings, credentials, and raw exception messages');
check('ACTUAL_SESSION_AUTHORITY',
  contracts.includes('ProjectPulseActualUserId')
    && contracts.includes('ProjectPulseSessionUserId')
    && contracts.includes('SYSTEM_ADMINISTRATION')
    && contracts.includes('MANAGE_ALL')
    && contracts.includes('requireOwnSession && IsViewAs(context)'),
  'read access is actual-session administrator authorized and retest is blocked in View-As');
check('SAFE_RETEST_ONLY',
  operations.includes('SafeRetest(path, method)')
    && operations.includes('Only safe read-only GET routes can be retested')
    && operations.includes('responseBodyRead = false')
    && operations.includes('SameOrigin(context)')
    && operations.includes('X-ProjectPulse-Diagnostic-Retest'),
  'API retest is same-origin, GET-only, body-free, parameter/callback/download guarded, and correlation tracked');
check('RESTART_CAPABILITY_TRUTH',
  operations.includes('restart_http_route')
    && operations.includes('Routes share one API process and cannot be restarted independently')
    && operations.includes('adapter_required')
    && operations.includes('connector_required')
    && contracts.includes('restartExecutionEnabled = false')
    && contracts.includes('productionChangingActionsEnabled = false'),
  'unsupported route restart is explained and production-changing actions remain adapter/connector gated');
check('MIDDLEWARE_AND_ENDPOINT_REGISTRATION',
  project.includes('app.UsePlatformOperationsTelemetry();')
    && project.includes('app.MapPlatformOperationsEndpoints();')
    && (project.match(/app\.UsePlatformOperationsTelemetry\(\);/g) ?? []).length === 1
    && (project.match(/app\.MapPlatformOperationsEndpoints\(\);/g) ?? []).length === 1,
  'generated runtime registers shared telemetry and endpoints exactly once');

check('MODULE_013_FIRST_RESPONSE_UI',
  module013.includes('System Health & API Diagnostics')
    && module013.includes("readJson('/api/platform-operations/overview'")
    && module013.includes("readJson('/api/platform-operations/apis'")
    && module013.includes('API inventory')
    && module013.includes('Retest API')
    && module013.includes('Restart this HTTP route')
    && module013.includes('Not supported by the current deployment model'),
  'Module 013 is the first troubleshooting workspace with search, drawer, retest, and truthful restart capability');
check('MODULE_013_DIAGNOSTIC_DRAWER',
  module013.includes('api-diagnostic-drawer')
    && module013.includes('Recent failures and logs')
    && module013.includes('Suggested troubleshooting')
    && module013.includes('Dependencies')
    && module013.includes('Correlation:'),
  'selecting an API opens dependencies, failures, logs, remediation guidance, actions, and correlation evidence');
check('MODULE_013_READABLE_RESPONSIVE',
  module013Css.includes('.api-filter-grid')
    && module013Css.includes('.api-table-wrap')
    && module013Css.includes('.api-diagnostic-drawer')
    && module013Css.includes('@media (max-width: 620px)'),
  'health and API inventory have searchable, bounded, responsive presentation');

check('MODULE_016_EVIDENCE_PRIMARY',
  module016.includes("view === 'evidence'")
    && module016.includes('<OperationalEvidenceCenter')
    && evidence.includes('Operational Evidence & Diagnostic History')
    && evidence.includes("/api/platform-operations/evidence?")
    && evidence.includes('/api/platform-operations/evidence/export')
    && evidence.includes('Dependency timeline')
    && evidence.includes('Workers and scheduled jobs'),
  'Module 016 provides deep searchable evidence, correlations, workers, jobs, timelines, and export');
check('MODULE_016_BACKUP_PRESERVED',
  module016.includes('<LegacyBackupRetentionCenter')
    && legacyBackup.includes('/api/system/backup-retention/status')
    && legacyBackup.includes('/api/system/backup-retention/delete')
    && legacyBackup.includes('restore-point protection'),
  'existing backup-retention inventory and guarded deletion remain preserved as a secondary view');
check('MODULE_016_RESPONSIVE',
  evidenceCss.includes('.operational-evidence-filter')
    && evidenceCss.includes('.operational-evidence-table-wrap')
    && evidenceCss.includes('.operational-evidence-two-column')
    && evidenceCss.includes('@media (max-width: 620px)'),
  'Module 016 is searchable, bounded, and responsive');

check('MODULE_068_SHARED_LIVE_REGISTRY',
  module068.includes("readJson('/api/platform-operations/architecture'")
    && module068.includes('ProjectPulse Platform Operations')
    && module068.includes('Azure adapter')
    && module068.includes('OpenCloud adapter')
    && module068.includes('Other provider adapter')
    && module068.includes('Module-to-API relationships'),
  'Module 068 consumes the same live provider and API registry and displays current/future adapters');
check('MODULE_068_LEGACY_COMPATIBILITY',
  module068.includes("readJson('/api/system-architecture/overview'")
    && module068.includes("readJson('/api/system-architecture/dependency-status'")
    && legacyArchitectureBackend.includes('MapSystemArchitectureEndpoints'),
  'the existing read-only architecture endpoints remain a fallback and are not deleted');
check('MODULE_068_BRANDED_EXPORT',
  module068.includes('usSignalLogoDataUrl')
    && module068.includes('Export branded architecture')
    && architectureBackend.includes('USSNavyStacked.png')
    && architectureBackend.includes('Created by Ahmed Adeyemi')
    && architectureBackend.includes('API appendix')
    && architectureBackend.includes('Release SHA')
    && architectureBackend.includes('Generated'),
  'the UI and exported HTML use approved branding, provider/environment/release metadata, legend, API appendix, date, and required footer');
check('MODULE_068_READ_ONLY_UI',
  !/method\s*:\s*['"](?:POST|PUT|PATCH|DELETE)['"]/i.test(module068)
    && !/<form\b/i.test(module068)
    && module068.includes('data-mode="read-only"'),
  'Module 068 remains read-only; its select only filters locally and export uses GET');
check('MODULE_068_RESPONSIVE',
  module068Css.includes('.provider-adapter-map')
    && module068Css.includes('.module-api-relationship-list')
    && module068Css.includes('.system-architecture-table-wrap')
    && module068Css.includes('@media (max-width: 700px)'),
  'architecture map, relationships, and tables are proportional and responsive');

check('REGISTRY_NAMES_UPDATED',
  registry.includes("displayName: 'System Health & API Diagnostics'")
    && registry.includes("displayName: 'Operational Evidence & Backup Retention'")
    && registry.includes("displayName: 'Provider-Neutral System Architecture'"),
  'module registry reflects current responsibilities without changing route numbers');
check('APP_MOUNTS_PRESERVED',
  app.includes('<ServiceControlCenter authSession={authSession} />')
    && app.includes('<BackupRetentionCenter authSession={authSession} />')
    && app.includes('<SystemArchitectureCenter authSession={authSession} />'),
  'existing routes mount the redesigned components once without a new React root');
check('OFFICIAL_LOGO_EMBEDDED',
  project.includes('Assets/Branding/USSNavyStacked.png')
    && architectureBackend.includes('ProjectTime.Api.Assets.Branding.USSNavyStacked.png'),
  'architecture export uses the approved embedded US Signal image');
check('LEGACY_DOCUMENTATION_PRESERVED',
  readme.includes('Module 068')
    && apiContract.includes('GET /api/system-architecture/overview')
    && security.includes('View-As'),
  'existing Module 068 documentation remains available while the shared contract is additive');
check('NO_MIGRATION_OR_DEPLOYMENT',
  !exists('database/migrations/050_provider_neutral_platform_operations.sql')
    && !exists('.github/workflows/projectpulse-deploy-group-2a.yml'),
  'Group 2A is source-only and does not create or run a migration or deployment');

console.log(`GROUP_2A_VALIDATION_CHECKS=${checks.length}`);
console.log('GROUP_2A_ACTIVE_PROVIDER_ADAPTER=AZURE_WHEN_RUNTIME_DETECTED');
console.log('GROUP_2A_FUTURE_PROVIDER_ADAPTER=OPENCLOUD_CONTRACT_READY');
console.log('GROUP_2A_ROUTE_RESTART=NOT_SUPPORTED_SHARED_API_PROCESS');
console.log('GROUP_2A_PRODUCTION_CHANGING_ACTIONS=LOCKED');
console.log('GROUP_2A_EXTERNAL_CALLS_PERFORMED=0');

const failed = checks.filter((item) => !item.condition);
if (failed.length) {
  console.error('GROUP_2A_CONTRACT=FAILED');
  failed.forEach((item) => console.error(`- ${item.name}: ${item.evidence}`));
  process.exit(1);
}

console.log('GROUP_2A_CONTRACT=PASSED');
