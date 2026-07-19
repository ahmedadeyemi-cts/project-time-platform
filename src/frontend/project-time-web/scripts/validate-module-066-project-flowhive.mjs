import fs from 'node:fs';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const repositoryRoot = path.resolve(scriptDirectory, '../../../..');

const paths = {
  backend: path.join(repositoryRoot, 'src/backend/ProjectTime.Api/Modules/ProjectFlowHiveModule.cs'),
  frontend: path.join(repositoryRoot, 'src/frontend/project-time-web/src/ProjectFlowHiveCenter.jsx'),
  stylesheet: path.join(repositoryRoot, 'src/frontend/project-time-web/src/project-flowhive-center.css'),
  readme: path.join(repositoryRoot, 'docs/modules/module-066-project-flowhive/README.md'),
  matrix: path.join(repositoryRoot, 'docs/modules/module-066-project-flowhive/CAPABILITY-MATRIX.md'),
  contract: path.join(repositoryRoot, 'docs/modules/module-066-project-flowhive/API-CONTRACT.md'),
  register: path.join(repositoryRoot, 'docs/MODULE-WORK-REGISTER.md'),
  catalog: path.join(repositoryRoot, 'docs/MODULE-CATALOG.md'),
  program: path.join(repositoryRoot, 'src/backend/ProjectTime.Api/Program.cs'),
  app: path.join(repositoryRoot, 'src/frontend/project-time-web/src/App.jsx')
};

const assertions = [];

function assertInvariant(name, condition, detail) {
  assertions.push({ name, condition, detail });
  console.log(`${name}=${condition ? 'PASSED' : 'FAILED'}${detail ? ` — ${detail}` : ''}`);
}

function readRequiredFile(name, filePath) {
  const exists = fs.existsSync(filePath);
  assertInvariant(`MODULE_066_${name}_EXISTS`, exists, path.relative(repositoryRoot, filePath));
  return exists ? fs.readFileSync(filePath, 'utf8') : '';
}

const backend = readRequiredFile('BACKEND', paths.backend);
const frontend = readRequiredFile('FRONTEND', paths.frontend);
const stylesheet = readRequiredFile('STYLESHEET', paths.stylesheet);
const readme = readRequiredFile('README', paths.readme);
const matrix = readRequiredFile('CAPABILITY_MATRIX', paths.matrix);
const contract = readRequiredFile('API_CONTRACT', paths.contract);
const register = readRequiredFile('WORK_REGISTER', paths.register);
const catalog = readRequiredFile('CATALOG', paths.catalog);
const program = readRequiredFile('PROGRAM', paths.program);
const app = readRequiredFile('APP', paths.app);

assertInvariant(
  'MODULE_066_BACKEND_MAP_METHOD',
  backend.includes('MapProjectFlowHiveEndpoints'),
  'module exposes its isolated endpoint registration method'
);

assertInvariant(
  'MODULE_066_TYPED_ROUTE_HANDLERS',
  backend.includes('(Func<HttpContext, IResult>)GetCapabilities') &&
    backend.includes('(Func<HttpContext, Task<IResult>>)GetPortfolioAsync'),
  'route handlers preserve their IResult response contract'
);

for (const route of [
  '/api/project-flowhive/capabilities',
  '/api/project-flowhive/portfolio'
]) {
  assertInvariant(
    `MODULE_066_GET_${route.split('/').at(-1).toUpperCase()}`,
    backend.includes(`"${route}"`),
    route
  );
}

assertInvariant(
  'MODULE_066_SESSION_SCOPE',
  backend.includes('ProjectPulseEffectiveUserId') && backend.includes('ProjectPulseSessionUserId'),
  'uses the established ProjectPulse effective-session contract'
);

assertInvariant(
  'MODULE_066_CANONICAL_ROLE_SCOPE',
  backend.includes('projectpulse_team_scope_assignments') &&
    backend.includes('PROJECT_TEAM_COORDINATOR') &&
    backend.includes('PROJECT_MANAGEMENT') &&
    backend.includes('ENGINEERING_LEAD') &&
    backend.includes('EXECUTIVE') &&
    !backend.includes('PROFESSIONAL_SERVICES_MANAGER') &&
    !backend.includes('PS_MANAGER'),
  'uses established role/team scope without introducing a new broad-scope role'
);

assertInvariant(
  'MODULE_066_READ_ONLY_BACKEND',
  backend.includes('databaseMutationEnabled = false') &&
    !/\.Map(?:Post|Put|Patch|Delete)\s*\(/.test(backend),
  'no Module 066 mutation endpoint is present'
);

const forbiddenSqlMutation = /\b(?:INSERT\s+INTO|UPDATE\s+[a-z_]|DELETE\s+FROM|ALTER\s+TABLE|CREATE\s+TABLE|DROP\s+TABLE)\b/i;
assertInvariant(
  'MODULE_066_NO_MUTATING_SQL',
  !forbiddenSqlMutation.test(backend),
  'backend contains read-only SELECT queries only'
);

assertInvariant(
  'MODULE_066_FRONTEND_MARKERS',
  frontend.includes('data-module="066"') &&
    frontend.includes('data-phase="066A"') &&
    frontend.includes('data-mode="read-only"'),
  'component identifies the governed 066A boundary'
);

assertInvariant(
  'MODULE_066_FRONTEND_ENDPOINTS',
  frontend.includes("getJson('/api/project-flowhive/capabilities')") &&
    frontend.includes("getJson('/api/project-flowhive/portfolio')"),
  'component consumes both read-only Module 066 endpoints'
);

assertInvariant(
  'MODULE_066_READ_ONLY_FRONTEND',
  !/method\s*:\s*['"](?:POST|PUT|PATCH|DELETE)['"]/i.test(frontend),
  'component sends no mutation request'
);

assertInvariant(
  'MODULE_066_SCOPED_STYLES',
  stylesheet.includes('.project-flowhive-center') &&
    stylesheet.includes('.flowhive-hero') &&
    !/(^|\n)\s*(?:html|body|:root|#root|main|button|table|input|select)\s*[{,]/m.test(stylesheet),
  'stylesheet avoids unscoped application-shell selectors'
);

for (const requirement of ['GOV-015', 'RBAC-019', 'WRK-011', 'AI-008', 'AI-019', 'RPT-013']) {
  assertInvariant(
    `MODULE_066_REQUIREMENT_${requirement.replace('-', '_')}`,
    matrix.includes(requirement),
    'capability matrix records the tracker requirement'
  );
}

assertInvariant(
  'MODULE_066_PHASE_BOUNDARY_DOCUMENTED',
  readme.includes('066A') &&
    readme.includes('read-only') &&
    readme.includes('No database migration') &&
    contract.includes('GET /api/project-flowhive/capabilities') &&
    contract.includes('GET /api/project-flowhive/portfolio'),
  'README and API contract describe the approved read-only phase'
);

assertInvariant(
  'MODULE_066_CENTRAL_GOVERNANCE_OWNERSHIP',
  register.includes('Module 066') &&
    register.includes('feature/module-066-project-flowhive-foundation-20260719') &&
    catalog.includes('Module 066') &&
    catalog.includes('Project FlowHive'),
  'central work register and module catalog record ownership'
);

assertInvariant(
  'MODULE_066_BASELINE_RECORDED',
  register.includes('92c0964afdc26dede72e09bf2c8d7c0629126bc0') &&
    readme.includes('92c0964afdc26dede72e09bf2c8d7c0629126bc0'),
  'approved implementation baseline is explicit'
);

assertInvariant(
  'MODULE_066_SHARED_BACKEND_REGISTRATION_DEFERRED',
  !program.includes('MapProjectFlowHiveEndpoints'),
  'Program.cs remains untouched until the shared-file checkpoint'
);

assertInvariant(
  'MODULE_066_SHARED_FRONTEND_REGISTRATION_DEFERRED',
  !app.includes('ProjectFlowHiveCenter'),
  'App.jsx remains untouched until the shared-file checkpoint'
);

function filesBelow(directory) {
  if (!fs.existsSync(directory)) return [];

  return fs.readdirSync(directory, { withFileTypes: true }).flatMap((entry) => {
    const entryPath = path.join(directory, entry.name);
    return entry.isDirectory() ? filesBelow(entryPath) : [entryPath];
  });
}

const migrationFiles = [
  ...filesBelow(path.join(repositoryRoot, 'database')),
  ...filesBelow(path.join(repositoryRoot, 'deployment'))
].filter((filePath) => /(?:module[-_]?066|flowhive)/i.test(path.basename(filePath)));

assertInvariant(
  'MODULE_066_NO_DATABASE_OR_DEPLOYMENT_FILE',
  migrationFiles.length === 0,
  migrationFiles.length === 0
    ? 'no Module 066 database or deployment artifact exists'
    : migrationFiles.map((filePath) => path.relative(repositoryRoot, filePath)).join(', ')
);

const failed = assertions.filter((assertion) => !assertion.condition);

console.log('');
console.log(`MODULE_066_VALIDATION_CHECKS=${assertions.length}`);
console.log('MODULE_066_IMPLEMENTATION_PHASE=066A_READ_ONLY_FOUNDATION');
console.log('MODULE_066_SHARED_INTEGRATION=DEFERRED');

if (failed.length > 0) {
  console.error('MODULE_066_FOUNDATION_CONTRACT=FAILED');
  failed.forEach((failure) => console.error(`- ${failure.name}: ${failure.detail}`));
  process.exit(1);
}

console.log('MODULE_066_FOUNDATION_CONTRACT=PASSED');
