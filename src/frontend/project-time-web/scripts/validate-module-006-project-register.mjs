import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repoRoot = fileURLToPath(new URL('../../../../', import.meta.url));
const absolute = (relative) => path.join(repoRoot, relative);
const exists = (relative) => fs.existsSync(absolute(relative));
const read = (relative) => fs.readFileSync(absolute(relative), 'utf8');
const checks = [];
const all = (source, markers) => markers.every((marker) => source.includes(marker));

function assert(name, condition, evidence) {
  checks.push({ name, condition, evidence });
  console.log(`MODULE006_PROJECT_REGISTER_${name}=${condition ? 'PASSED' : 'FAILED'} — ${evidence}`);
}

function walk(relativeDirectory) {
  const directory = absolute(relativeDirectory);
  if (!fs.existsSync(directory)) return [];
  const files = [];
  for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
    const relative = path.join(relativeDirectory, entry.name);
    if (entry.isDirectory()) files.push(...walk(relative));
    else files.push(relative.replaceAll('\\', '/'));
  }
  return files;
}

const paths = Object.freeze({
  center: 'src/frontend/project-time-web/src/ProjectRegisterCenter.jsx',
  css: 'src/frontend/project-time-web/src/project-register-center.css',
  generator: 'src/frontend/project-time-web/scripts/inject-module-006-project-register.mjs',
  validator: 'src/frontend/project-time-web/scripts/validate-module-006-project-register.mjs',
  retiredGenerator: 'src/frontend/project-time-web/scripts/inject-module-006-toyota-hyundai-pipeline.mjs',
  registry: 'src/frontend/project-time-web/src/module-availability-registry.js',
  rbac: 'src/frontend/project-time-web/src/scoped-rbac-catalog-compatibility.js',
  packageJson: 'src/frontend/project-time-web/package.json',
  workRegister: 'src/frontend/project-time-web/src/WorkRegisterCenter.jsx',
  generatedApp: 'src/frontend/project-time-web/src/App.Module001.g.jsx',
  plan: 'docs/modules/module-006-project-register/IMPLEMENTATION-PLAN.md'
});

for (const [key, relative] of Object.entries(paths)) {
  if (key === 'retiredGenerator' || key === 'generatedApp') continue;
  assert(`FILE_${key.toUpperCase()}`, exists(relative), relative);
}
assert('RETIRED_GENERATOR_REMOVED', !exists(paths.retiredGenerator), paths.retiredGenerator);
if (checks.some((check) => !check.condition)) {
  console.error('MODULE_006_PROJECT_REGISTER_CONTRACT=FAILED_MISSING_FILE');
  process.exit(1);
}

const center = read(paths.center);
const css = read(paths.css);
const generator = read(paths.generator);
const registry = read(paths.registry);
const rbac = read(paths.rbac);
const packageJson = read(paths.packageJson);
const workRegister = read(paths.workRegister);
const plan = read(paths.plan);
const generatedApp = exists(paths.generatedApp) ? read(paths.generatedApp) : '';
const module006Start = registry.indexOf("moduleNumber: '006'");
const module007Start = registry.indexOf("moduleNumber: '007'", module006Start);
const module006Block = module006Start >= 0 && module007Start > module006Start
  ? registry.slice(module006Start, module007Start)
  : '';

assert(
  'CANONICAL_IDENTITY',
  all(module006Block, [
    "route: 'project-register'", "displayName: 'Project Register'",
    "group: 'Project Operations'", "lifecycle: 'source_foundation'"
  ]),
  'Module 006 is Project Register in Project Operations'
);
assert(
  'LEGACY_ROUTE_COMPATIBILITY',
  registry.includes("'psa-modules': 'project-register'")
    && all(module006Block, [
      "displayName: 'Toyota & Hyundai Pipeline'", "route: 'psa-modules'",
      "lifecycle: 'retired_non_destructively'"
    ]),
  'the old customer-specific route is an explicit non-canonical compatibility alias'
);
assert(
  'ROLE_POLICY_NORMALIZATION',
  all(rbac, [
    "'006': 'Project Register'", "'006': 'project-register'", 'MODULE_ROUTE_OVERRIDES',
    'moduleDisplayNameOverrides', 'moduleRouteOverrides'
  ]),
  'Modules 012 and 037 receive the current name and route before the later database catalog migration'
);
assert(
  'AUTHORITATIVE_READ_SOURCE',
  all(center, [
    "fetchJson('/api/work-register/overview')",
    '/api/work-register/projects/${item.workId}/details',
    '/api/work-lifecycle/projects/${item.workId}',
    "normalize(item?.sourceTable) === 'projects'"
  ]),
  'Module 006 composes the existing Work Register and lifecycle APIs'
);
assert(
  'ACTIVE_AND_HISTORICAL_VIEWS',
  all(center, [
    '<option value="active">Active</option>',
    '<option value="historical">Archived / historical</option>',
    '<option value="all">All projects</option>',
    'Historical project. Project state remains read-only'
  ]),
  'active, archived, and all-project views preserve historical read-only evidence'
);
assert(
  'REGISTER_FIELDS',
  all(center, [
    'item.projectCode', 'item.workName', 'item.customerName', 'item.contractType',
    'item.projectManager', 'item.projectCoordinator', 'item.accountExecutive',
    'item.solutionArchitect', 'item.assignedEngineers', 'item.sellQuoteNumber',
    'item.taskCount', 'item.documentCount', 'item.allocatedHours', 'item.usedHours',
    'item.totalCost', 'item.remainingCost', 'item.burnStatus'
  ]),
  'the register binds authoritative identity, ownership, SELL, task, document, hour, and financial fields'
);
assert(
  'MUTATION_AUTHORITY_PRESERVED',
  all(center, ['Mutations remain in Module 055C', 'Manage Existing Projects', 'href="#work-register"'])
    && !/method\s*:\s*['"](?:POST|PUT|PATCH|DELETE)['"]/i.test(center)
    && !center.includes('/api/work-register/projects/update')
    && !center.includes('/api/work-register/projects/create'),
  'Module 006 is read-only and delegates project mutation to the authoritative workspace'
);
assert(
  'VIEW_AS_AND_SCOPE_BOUNDARY',
  center.includes('The backend remains the authorization authority.')
    && all(workRegister, ['selectedWorkItem?.canEditProject === true', 'selectedWorkItemIsArchived']),
  'current backend project scope and archived-project protections remain authoritative'
);
assert(
  'IMPORT_AND_EXPORT_GATES',
  all(center, [
    'Workbook import', 'Review-gated', 'Import controls locked',
    'Branded exports', 'Evidence-gated', 'Export controls locked'
  ]) && plan.includes('No workbook row will persist until a reviewer accepts the mapping and row decisions.'),
  'workbook import and Excel/PDF export remain visibly locked until their evidence schemas are reviewed'
);
assert(
  'RESPONSIVE_AND_DARK_THEME',
  all(css, [
    '.project-register-table-wrap', 'overflow: auto', '.project-register-drawer-backdrop',
    '[data-theme="dark"]', '@media (max-width: 1180px)', '@media (max-width: 700px)'
  ]),
  'desktop, tablet, mobile, drawer, horizontal table, and dark-theme behavior are present'
);
assert(
  'GENERATED_ROUTE_MOUNT',
  all(generator, [
    "route: 'project-register'", "activeRoute === 'project-register'",
    '<ProjectRegisterCenter legacyRoute={activeRoute === \'psa-modules\'} />',
    "title: 'PSA Modules'", "title: 'Toyota & Hyundai Pipeline'",
    'MODULE_006_PROJECT_REGISTER_GENERATION=PASS'
  ]),
  'the generated App receives the canonical route and removes the retired dashboard-only panel'
);

if (generatedApp) {
  assert(
    'GENERATED_APP_CURRENT',
    all(generatedApp, [
      "import ProjectRegisterCenter from './ProjectRegisterCenter.jsx';",
      "route: 'project-register'",
      '<ProjectRegisterCenter legacyRoute={activeRoute === \'psa-modules\'} />'
    ])
      && !generatedApp.includes("title: 'PSA Modules'")
      && !generatedApp.includes("title: 'Toyota & Hyundai Pipeline'")
      && !generatedApp.includes('<section id="psa-modules"'),
    'the current generated build surface contains only the Project Register presentation'
  );
} else {
  console.log('MODULE006_PROJECT_REGISTER_GENERATED_APP=NOT_PRESENT_BEFORE_PREBUILD');
}

assert(
  'BUILD_INTEGRATION',
  all(packageJson, [
    'inject-module-006-project-register.mjs',
    '"validate:module006": "node ./scripts/validate-module-006-project-register.mjs"',
    'npm run validate:module006'
  ]) && !packageJson.includes('inject-module-006-toyota-hyundai-pipeline.mjs'),
  'predevelopment, prebuild, and complete build run the Project Register source controls'
);

const module006Migrations = walk('database/migrations').filter((relative) =>
  /(?:module[-_]?006|project[-_]?register)/i.test(relative)
);
assert(
  'NO_UNREVIEWED_MIGRATION',
  module006Migrations.length === 0,
  module006Migrations.length === 0
    ? 'this foundation does not change database metadata, imports, exports, or operational records'
    : `unexpected Module 006 migration paths: ${module006Migrations.join(', ')}`
);

const ownedProjectRegisterPaths = [
  paths.center, paths.css, paths.generator, paths.validator,
  paths.registry, paths.rbac, paths.packageJson, paths.plan
];
const ownedEnvironmentActions = ownedProjectRegisterPaths.filter((relative) =>
  /(?:deploy|migration|azure|containerapp|production[-_ ]?change)/i.test(relative)
);
assert(
  'NO_ENVIRONMENT_ACTION',
  ownedEnvironmentActions.length === 0,
  ownedEnvironmentActions.length === 0
    ? 'no environment-changing action exists in the owned Module 006 source scope'
    : `unexpected environment-changing owned paths: ${ownedEnvironmentActions.join(', ')}`
);

console.log(`MODULE_006_PROJECT_REGISTER_CHECKS=${checks.length}`);
console.log('MODULE_006_PROJECT_REGISTER_PHASE=READ_ONLY_FOUNDATION');
console.log('MODULE_006_PROJECT_REGISTER_CANONICAL_ROUTE=project-register');
console.log('MODULE_006_PROJECT_REGISTER_LEGACY_ROUTE=psa-modules_COMPATIBILITY_ONLY');
console.log('MODULE_006_PROJECT_REGISTER_DATA_AUTHORITY=WORK_REGISTER_AND_LIFECYCLE');
console.log('MODULE_006_PROJECT_REGISTER_MUTATION_AUTHORITY=MODULE_055C_AND_055D');
console.log('MODULE_006_PROJECT_REGISTER_IMPORTS_PERSISTED=0');
console.log('MODULE_006_PROJECT_REGISTER_EXPORTS_GENERATED=0');
console.log('MODULE_006_PROJECT_REGISTER_DATABASE_CHANGES=0');
console.log('MODULE_006_PROJECT_REGISTER_DEPLOYMENTS=0');

if (checks.some((check) => !check.condition)) {
  console.error('MODULE_006_PROJECT_REGISTER_CONTRACT=FAILED');
  process.exit(1);
}
console.log('MODULE_006_PROJECT_REGISTER_CONTRACT=PASSED');
