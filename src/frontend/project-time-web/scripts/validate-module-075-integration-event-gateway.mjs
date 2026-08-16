import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repoRoot = fileURLToPath(new URL('../../../../', import.meta.url));
const repoPath = (relativePath) => path.join(repoRoot, relativePath);
const read = (relativePath) => fs.readFileSync(repoPath(relativePath), 'utf8');

const files = {
  backend: 'src/backend/ProjectTime.Api/Modules/IntegrationEventGatewayModule.cs',
  frontend: 'src/frontend/project-time-web/src/IntegrationEventGatewayCenter.jsx',
  styles: 'src/frontend/project-time-web/src/integration-event-gateway-center.css',
  governedBackend: 'src/backend/ProjectTime.Api/Modules/GovernedOperationsReadModule.cs',
  governedFrontend: 'src/frontend/project-time-web/src/GovernedOperationalReadCenter.jsx',
  governedStyles: 'src/frontend/project-time-web/src/governed-operational-read-center.css',
  program: 'src/backend/ProjectTime.Api/Program.cs',
  app: 'src/frontend/project-time-web/src/App.jsx',
  registry: 'src/frontend/project-time-web/src/module-availability-registry.js',
  workspaceRegistry: 'src/frontend/project-time-web/src/workspace-registry.js',
  package: 'src/frontend/project-time-web/package.json',
  docker: 'deployment/containers/web/Dockerfile',
  readme: 'docs/modules/module-075-integration-event-gateway/README.md',
  apiContract: 'docs/modules/module-075-integration-event-gateway/API-CONTRACT.md',
  authorization: 'docs/modules/module-075-integration-event-gateway/AUTHORIZATION-AND-SECURITY.md',
  overlap: 'docs/modules/module-075-integration-event-gateway/OVERLAP-AND-RELEASE-GATES.md'
};

let checks = 0;
let failures = 0;

function test(name, condition, evidence = '') {
  checks += 1;
  if (!condition) failures += 1;
  console.log(`MODULE_075_${name}=${condition ? 'PASSED' : 'FAILED'}${evidence ? ` — ${evidence}` : ''}`);
  return Boolean(condition);
}

for (const relativePath of [
  files.backend,
  files.frontend,
  files.styles,
  files.readme,
  files.apiContract,
  files.authorization,
  files.overlap
]) {
  test(
    `FILE_${path.basename(relativePath).replace(/\W/g, '_').toUpperCase()}`,
    fs.existsSync(repoPath(relativePath)),
    relativePath
  );
}

const backend = read(files.backend) + read(files.governedBackend);
const frontend = read(files.frontend) + read(files.governedFrontend);
const styles = read(files.styles) + read(files.governedStyles);

test('MAP_METHOD', backend.includes('MapIntegrationEventGatewayEndpoints'));
test(
  'READ_SURFACES',
  ['overview', 'sources', 'contracts', 'deliveries', 'dead-letter-policy', 'security-policy']
    .every((route) => backend.includes(`"/${route}"`))
);
test('LOCKED', backend.includes('423Locked') && backend.includes('requestBodyRead = false'));
test('ACTUAL_SESSION', backend.includes('ProjectPulseActualUserId'));
test('VIEW_AS_BLOCKED', backend.includes('IsViewAs(context)'));
test('NO_HTTP_CLIENT', !backend.includes('HttpClient'));
test('NO_MUTATING_SQL', !/(INSERT|UPDATE|DELETE|MERGE)\s/i.test(backend));
test(
  'GET_ONLY_UI',
  frontend.includes('fetch(') && !/method:\s*["'](?:POST|PUT|PATCH|DELETE)/.test(frontend)
);
test('US_SIGNAL_BRAND', frontend.includes('ussignal.png') && styles.includes('#0077c8'));

const program = read(files.program);
const app = read(files.app);
const registry = read(files.registry);
const workspaceRegistry = read(files.workspaceRegistry);
const packageJson = JSON.parse(read(files.package));
const docker = read(files.docker);

const registryOwnsRouteNumber =
  registry.includes("moduleNumber: '075'")
  && registry.includes("route: 'integration-event-gateway'")
  && app.includes("route: 'integration-event-gateway'")
  && app.includes("navLabel: 'MODULE 075'");

const sharedWorkspaceRegistryUsesCurrentAuthority =
  workspaceRegistry.includes("import { PROJECTPULSE_MODULES, canonicalModuleRoute }")
  && workspaceRegistry.includes('export function toWorkspace')
  && workspaceRegistry.includes('PROJECTPULSE_MODULES.map(toWorkspace)')
  && workspaceRegistry.includes('WORKSPACE_BY_NUMBER')
  && workspaceRegistry.includes('WORKSPACE_BY_ROUTE')
  && workspaceRegistry.includes('moduleNumber')
  && workspaceRegistry.includes('route');

const completeFrontendContainerContext =
  docker.includes('# COPY src/frontend/project-time-web/')
  && docker.includes('COPY . /workspace/')
  && docker.includes('WORKDIR /workspace/src/frontend/project-time-web')
  && docker.includes('RUN npm run build');

const runtimeChecks = {
  BACKEND_MAP_ONCE:
    program.split('app.MapIntegrationEventGatewayEndpoints();').length - 1 === 1,
  APP_IMPORT_ONCE:
    (app.match(/import IntegrationEventGatewayCenter from ['"]\.\/IntegrationEventGatewayCenter(?:\.jsx)?['"];/g) || []).length === 1,
  APP_ROUTE_NUMBER:
    registryOwnsRouteNumber,
  APP_ROUTE_CONDITION:
    app.includes("activeRoute === 'integration-event-gateway'"),
  APP_COMPONENT_MOUNT:
    app.includes('<IntegrationEventGatewayCenter'),
  APP_ROLE_NAVIGATION:
    app.includes("route: 'integration-event-gateway'") && app.includes("navLabel: 'MODULE 075'"),
  INSTALLED_REGISTRY_ENTRY:
    registry.includes("moduleNumber: '075'") && registry.includes("route: 'integration-event-gateway'"),
  SHARED_WORKSPACE_REGISTRY:
    sharedWorkspaceRegistryUsesCurrentAuthority,
  FOCUSED_VALIDATOR_SCRIPT:
    typeof packageJson.scripts?.['validate:module075'] === 'string'
      && packageJson.scripts['validate:module075'].includes('validate-module-075-integration-event-gateway.mjs'),
  CROSS_RUNTIME_VALIDATOR_SCRIPT:
    typeof packageJson.scripts?.['validate:modules075080-runtime'] === 'string'
      && packageJson.scripts['validate:modules075080-runtime'].includes('validate-modules-075-080-runtime-integration.mjs'),
  COMPLETE_BUILD_CHAIN:
    typeof packageJson.scripts?.build === 'string'
      && packageJson.scripts.build.includes('validate:module075')
      && packageJson.scripts.build.includes('validate:modules075080-runtime'),
  CONTAINER_BACKEND_SOURCE:
    docker.includes('IntegrationEventGatewayModule.cs') && docker.includes('COPY . /workspace/'),
  CONTAINER_FRONTEND_SOURCE:
    completeFrontendContainerContext
};

for (const [name, condition] of Object.entries(runtimeChecks)) {
  const evidence = condition
    ? name === 'APP_ROUTE_NUMBER'
      ? 'module number resolved from the canonical module registry'
      : name === 'SHARED_WORKSPACE_REGISTRY'
        ? 'workspace metadata derives from PROJECTPULSE_MODULES'
        : name === 'CONTAINER_FRONTEND_SOURCE'
          ? 'complete repository context is copied before the production build'
          : 'current governed source contract'
    : 'runtime source did not converge';
  test(`RUNTIME_${name}`, condition, evidence);
}

test(
  'SHARED_RUNTIME_INTEGRATED',
  Object.values(runtimeChecks).every(Boolean),
  'backend map, route mount, canonical module/workspace registries, build chain, and complete container context'
);

console.log(`MODULE_075_VALIDATION_CHECKS=${checks}`);
console.log('MODULE_075_PHASE=RUNTIME_REGISTERED_FAIL_CLOSED');
console.log(`MODULE_075_CONTRACT=${failures ? 'FAILED' : 'PASSED'}`);
process.exitCode = failures ? 1 : 0;
