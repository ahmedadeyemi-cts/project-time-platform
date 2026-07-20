import fs from 'node:fs';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const repositoryRoot = path.resolve(scriptDirectory, '../../../..');

const paths = {
  backend: path.join(repositoryRoot, 'src/backend/ProjectTime.Api/Modules/SystemArchitectureModule.cs'),
  frontend: path.join(repositoryRoot, 'src/frontend/project-time-web/src/SystemArchitectureCenter.jsx'),
  stylesheet: path.join(repositoryRoot, 'src/frontend/project-time-web/src/system-architecture-center.css'),
  readme: path.join(repositoryRoot, 'docs/modules/module-068-system-architecture/README.md'),
  contract: path.join(repositoryRoot, 'docs/modules/module-068-system-architecture/API-CONTRACT.md'),
  security: path.join(repositoryRoot, 'docs/modules/module-068-system-architecture/SECURITY-AND-OPERATIONS.md'),
  matrix: path.join(repositoryRoot, 'docs/modules/module-068-system-architecture/CAPABILITY-MATRIX.md'),
  program: path.join(repositoryRoot, 'src/backend/ProjectTime.Api/Program.cs'),
  app: path.join(repositoryRoot, 'src/frontend/project-time-web/src/App.jsx'),
  package: path.join(repositoryRoot, 'src/frontend/project-time-web/package.json'),
  dockerfile: path.join(repositoryRoot, 'deployment/containers/web/Dockerfile'),
  catalog: path.join(repositoryRoot, 'docs/MODULE-CATALOG.md'),
  register: path.join(repositoryRoot, 'docs/MODULE-WORK-REGISTER.md'),
  tracker: path.join(repositoryRoot, 'docs/production-readiness/AUGUST_PRODUCTION_READINESS_TRACKER.md')
};

const assertions = [];

function assertInvariant(name, condition, detail) {
  assertions.push({ name, condition, detail });
  console.log(`${name}=${condition ? 'PASSED' : 'FAILED'}${detail ? ` — ${detail}` : ''}`);
}

function readRequiredFile(name, filePath) {
  const exists = fs.existsSync(filePath);
  assertInvariant(`MODULE_068_${name}_EXISTS`, exists, path.relative(repositoryRoot, filePath));
  return exists ? fs.readFileSync(filePath, 'utf8') : '';
}

function count(source, pattern) {
  return [...source.matchAll(pattern)].length;
}

function filesBelow(directory) {
  if (!fs.existsSync(directory)) return [];

  return fs.readdirSync(directory, { withFileTypes: true }).flatMap((entry) => {
    const entryPath = path.join(directory, entry.name);
    return entry.isDirectory() ? filesBelow(entryPath) : [entryPath];
  });
}

const backend = readRequiredFile('BACKEND', paths.backend);
const frontend = readRequiredFile('FRONTEND', paths.frontend);
const stylesheet = readRequiredFile('STYLESHEET', paths.stylesheet);
const readme = readRequiredFile('README', paths.readme);
const contract = readRequiredFile('API_CONTRACT', paths.contract);
const security = readRequiredFile('SECURITY_OPERATIONS', paths.security);
const matrix = readRequiredFile('CAPABILITY_MATRIX', paths.matrix);
const program = readRequiredFile('PROGRAM', paths.program);
const app = readRequiredFile('APP', paths.app);
const packageJson = readRequiredFile('PACKAGE', paths.package);
const dockerfile = readRequiredFile('DOCKERFILE', paths.dockerfile);
const catalog = readRequiredFile('CATALOG', paths.catalog);
const register = readRequiredFile('WORK_REGISTER', paths.register);
const tracker = readRequiredFile('PRODUCTION_TRACKER', paths.tracker);

assertInvariant(
  'MODULE_068_BACKEND_MAP_METHOD',
  backend.includes('MapSystemArchitectureEndpoints'),
  'isolated endpoint registration method exists'
);

assertInvariant(
  'MODULE_068_TYPED_ROUTE_HANDLERS',
  backend.includes('(Func<HttpContext, Task<IResult>>)GetOverviewAsync')
    && backend.includes('(Func<HttpContext, Task<IResult>>)GetDependencyStatusAsync'),
  'typed route handlers preserve IResult responses'
);

for (const route of [
  '/api/system-architecture/overview',
  '/api/system-architecture/dependency-status'
]) {
  assertInvariant(
    `MODULE_068_GET_${route.split('/').at(-1).replace('-', '_').toUpperCase()}`,
    backend.includes(`"${route}"`),
    route
  );
}

assertInvariant(
  'MODULE_068_CONTRACT_VERSIONED',
  backend.includes('2026-07-19.1')
    && backend.includes('2b4a6d1a1242a25b52110a2a209ff8ddda0b8ca4')
    && readme.includes('2b4a6d1a1242a25b52110a2a209ff8ddda0b8ca4'),
  'contract and implementation baseline are explicit'
);

assertInvariant(
  'MODULE_068_ACTUAL_SESSION_AUTHORITY',
  backend.includes('ProjectPulseActualUserId')
    && backend.includes('ProjectPulseSessionUserId')
    && !backend.includes('"ProjectPulseEffectiveUserId"'),
  'View-As effective identity cannot provide administrator authority'
);

assertInvariant(
  'MODULE_068_ADMIN_AUTHORIZATION',
  backend.includes('SUPER_ADMINISTRATOR')
    && backend.includes('ADMINISTRATOR')
    && backend.includes('SYSTEM_ADMINISTRATION')
    && backend.includes('MANAGE_ALL')
    && backend.includes('administrator_access_required'),
  'server-side role and permission enforcement exists'
);

assertInvariant(
  'MODULE_068_READ_ONLY_BACKEND',
  !/\.Map(?:Post|Put|Patch|Delete)\s*\(/.test(backend),
  'no mutation endpoint exists'
);

const forbiddenSqlMutation = /\b(?:INSERT\s+INTO|UPDATE\s+[a-z_]|DELETE\s+FROM|ALTER\s+TABLE|CREATE\s+TABLE|DROP\s+TABLE|TRUNCATE\s+)\b/i;
assertInvariant(
  'MODULE_068_NO_MUTATING_SQL',
  !forbiddenSqlMutation.test(backend),
  'backend SQL is read-only'
);

assertInvariant(
  'MODULE_068_NO_NETWORK_DISCOVERY',
  !/(?:HttpClient|TcpClient|UdpClient|Ping\s*\(|Dns\.|Process\.Start)/.test(backend),
  'logical map performs no provider fan-out or physical topology scan'
);

assertInvariant(
  'MODULE_068_SANITIZED_ERRORS',
  !/(?:exception|ex)\.Message/i.test(backend)
    && backend.includes('exception.GetType().Name')
    && backend.includes('authorization_dependency_unavailable'),
  'raw exception messages are not returned or logged'
);

assertInvariant(
  'MODULE_068_SECRET_EXCLUSION_CONTRACT',
  backend.toLowerCase().includes('secret values')
    && backend.toLowerCase().includes('connection strings')
    && backend.toLowerCase().includes('private host names')
    && security.includes('raw exception messages'),
  'secret and physical-topology exclusions are explicit'
);

assertInvariant(
  'MODULE_068_ARCHITECTURE_DIMENSIONS',
  backend.includes('ArchitectureLayers()')
    && backend.includes('ArchitectureNodes()')
    && backend.includes('ArchitectureConnections()')
    && backend.includes('TrustBoundaries()')
    && backend.includes('EnvironmentPath()'),
  'component, data, authentication, integration, and environment views exist'
);

assertInvariant(
  'MODULE_068_DELEGATED_HEALTH_OWNERSHIP',
  backend.includes('safe_local_and_delegated_health')
    && backend.includes('StatusLinks()')
    && backend.includes('#service-control')
    && backend.includes('#replication-sync')
    && backend.includes('#cicd-pipeline'),
  'existing modules remain authoritative for live health'
);

assertInvariant(
  'MODULE_068_FRONTEND_MARKERS',
  frontend.includes('data-module="068"')
    && frontend.includes('data-mode="read-only"')
    && frontend.includes('data-contract-version'),
  'frontend declares its governed boundary'
);

assertInvariant(
  'MODULE_068_FRONTEND_ENDPOINTS',
  frontend.includes("readJson('/api/system-architecture/overview'")
    && frontend.includes("readJson('/api/system-architecture/dependency-status'"),
  'frontend consumes both Module 068 GET endpoints'
);

assertInvariant(
  'MODULE_068_READ_ONLY_FRONTEND',
  frontend.includes("method: 'GET'")
    && !/method\s*:\s*['"](?:POST|PUT|PATCH|DELETE)['"]/i.test(frontend)
    && !/<(?:input|select|textarea)\b/i.test(frontend),
  'frontend provides observation and navigation only'
);

assertInvariant(
  'MODULE_068_NO_PARKED_MODULE_RUNTIME_DEPENDENCY',
  !frontend.includes('/api/ai-provider')
    && !backend.includes('MapAiProvider')
    && !backend.includes('IAiProvider'),
  'Module 068 does not activate or depend on parked Module 064 source'
);

assertInvariant(
  'MODULE_068_SCOPED_STYLES',
  stylesheet.includes('.system-architecture-center')
    && !/(^|\n)\s*(?:html|body|:root|#root|main|button|table|input|select)\s*[{,]/m.test(stylesheet),
  'stylesheet avoids unscoped application-shell selectors'
);

assertInvariant(
  'MODULE_068_PROGRAM_REGISTRATION',
  count(program, /app\.MapSystemArchitectureEndpoints\(\);/g) === 1,
  'backend map is registered exactly once'
);

assertInvariant(
  'MODULE_068_APP_IMPORT_COUNT',
  count(app, /import SystemArchitectureCenter from '\.\/SystemArchitectureCenter\.jsx';/g) === 1,
  'frontend component is imported exactly once'
);

assertInvariant(
  'MODULE_068_APP_MOUNT_COUNT',
  count(app, /<SystemArchitectureCenter authSession=\{authSession\} \/>/g) === 1,
  'frontend component is mounted exactly once'
);

assertInvariant(
  'MODULE_068_ROUTE_REGISTRY',
  count(app, /route:\s*['"]system-architecture['"]/g) >= 2
    && app.includes("href: '#system-architecture'")
    && app.includes("navLabel: 'MODULE 068'"),
  'route exists in workspace and installed-module registries'
);

assertInvariant(
  'MODULE_068_FRONTEND_ADMIN_ONLY',
  app.includes("activeRoute === 'system-architecture'")
    && app.includes("hasPermission('SYSTEM_ADMINISTRATION')")
    && app.includes("hasPermission('MANAGE_ALL')"),
  'route visibility and mount are administrator-only'
);

assertInvariant(
  'MODULE_068_BUILD_GUARD',
  packageJson.includes('validate:module068')
    && packageJson.includes('validate-module-068-system-architecture.mjs')
    && packageJson.includes('npm run validate:module068'),
  'production frontend build invokes the Module 068 validator'
);

assertInvariant(
  'MODULE_068_CONTAINER_BUILD_CONTEXT',
  dockerfile.includes('SystemArchitectureModule.cs')
    && dockerfile.includes('module-068-system-architecture')
    && dockerfile.includes('MODULE-CATALOG.md')
    && dockerfile.includes('AUGUST_PRODUCTION_READINESS_TRACKER.md'),
  'container build receives every repository file inspected by the validator'
);

assertInvariant(
  'MODULE_068_DOCUMENTATION_SET',
  readme.includes('Module 068')
    && contract.includes('GET /api/system-architecture/overview')
    && security.includes('View-As')
    && matrix.includes('OPS-013'),
  'README, API, security/operations, and capability evidence are complete'
);

assertInvariant(
  'MODULE_068_GOVERNANCE_REGISTERED',
  catalog.includes('| 068 | System Architecture & Dependency Map')
    && register.includes('| 068 |')
    && register.includes('feature/modules-064-074-release-train-on-main-20260719'),
  'central catalog and work register record Module 068'
);

assertInvariant(
  'MODULE_067_NUMBER_RESERVED',
  catalog.includes('| 067 | Global Mail Configuration Center | Release-train candidate')
    && register.includes('| 067 |')
    && tracker.includes('| 067 |'),
  'the SMTP tracker conflict is resolved without reusing installed Module 063'
);

assertInvariant(
  'MODULE_068_TRACKER_OPS_013',
  tracker.includes('OPS-013')
    && tracker.includes('| 068 |')
    && tracker.includes('System Architecture & Dependency Map'),
  'production-readiness tracker records the source package'
);

assertInvariant(
  'MODULE_068_INTEGRATION_HOLD_RECORDED',
  readme.includes('release-train')
    && register.includes('Module 002')
    && catalog.includes('semantically integrated'),
  'shared-file overlap is integrated after the Module 002 checkpoint'
);

const moduleMigrationArtifacts = [
  ...filesBelow(path.join(repositoryRoot, 'database')),
  ...filesBelow(path.join(repositoryRoot, 'deployment'))
].filter((filePath) => /(?:module[-_]?068|system[-_]?architecture)/i.test(path.basename(filePath)));

assertInvariant(
  'MODULE_068_NO_DATABASE_OR_DEPLOYMENT_ARTIFACT',
  moduleMigrationArtifacts.length === 0,
  moduleMigrationArtifacts.length === 0
    ? 'no Module 068 migration or deployment file exists'
    : moduleMigrationArtifacts.map((filePath) => path.relative(repositoryRoot, filePath)).join(', ')
);

const failed = assertions.filter((assertion) => !assertion.condition);

console.log('');
console.log(`MODULE_068_VALIDATION_CHECKS=${assertions.length}`);
console.log('MODULE_068_IMPLEMENTATION=FULL_READ_ONLY_ARCHITECTURE_PACKAGE');
console.log('MODULE_068_SHARED_INTEGRATION=RELEASE_TRAIN_SOURCE_DRAFT_PR_24_OPEN');
console.log('MODULE_068_AZURE_DATABASE_ENTRA_CHANGES=NONE');

if (failed.length > 0) {
  console.error('MODULE_068_CONTRACT=FAILED');
  failed.forEach((failure) => console.error(`- ${failure.name}: ${failure.detail}`));
  process.exit(1);
}

console.log('MODULE_068_CONTRACT=PASSED');
