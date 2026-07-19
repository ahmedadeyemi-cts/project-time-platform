import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const repositoryRoot = path.resolve(scriptDirectory, '../../../..');
const moduleDirectory = path.join(repositoryRoot, 'docs/modules/module-066-project-flowhive');
const backendDirectory = path.join(repositoryRoot, 'src/backend/ProjectTime.Api/Modules');

const paths = {
  backend: path.join(backendDirectory, 'ProjectFlowHiveModule.cs'),
  contracts: path.join(backendDirectory, 'ProjectFlowHivePlanningContracts.cs'),
  schedule: path.join(backendDirectory, 'ProjectFlowHiveScheduleEngine.cs'),
  ai: path.join(backendDirectory, 'ProjectFlowHiveAiRequestFactory.cs'),
  brand: path.join(backendDirectory, 'ProjectFlowHiveBrandAssets.cs'),
  artifacts: path.join(backendDirectory, 'ProjectFlowHiveArtifactRenderer.cs'),
  frontend: path.join(repositoryRoot, 'src/frontend/project-time-web/src/ProjectFlowHiveCenter.jsx'),
  stylesheet: path.join(repositoryRoot, 'src/frontend/project-time-web/src/project-flowhive-center.css'),
  logoJpeg: path.join(repositoryRoot, 'src/frontend/project-time-web/brand/ussignal.jpg'),
  logoPng: path.join(repositoryRoot, 'src/frontend/project-time-web/brand/ussignal.png'),
  readme: path.join(moduleDirectory, 'README.md'),
  matrix: path.join(moduleDirectory, 'CAPABILITY-MATRIX.md'),
  contract: path.join(moduleDirectory, 'API-CONTRACT.md'),
  authorization: path.join(moduleDirectory, 'AUTHORIZATION-AND-SECURITY.md'),
  persistence: path.join(moduleDirectory, 'PERSISTENCE-DESIGN.md'),
  scheduling: path.join(moduleDirectory, 'SCHEDULE-ENGINE.md'),
  aiDoc: path.join(moduleDirectory, 'AI-INTEGRATION.md'),
  artifactsDoc: path.join(moduleDirectory, 'ARTIFACTS-AND-SHARING.md'),
  overlap: path.join(moduleDirectory, 'OVERLAP-AND-RELEASE-GATES.md'),
  evidence: path.join(moduleDirectory, 'VALIDATION-EVIDENCE.md'),
  program: path.join(repositoryRoot, 'src/backend/ProjectTime.Api/Program.cs'),
  app: path.join(repositoryRoot, 'src/frontend/project-time-web/src/App.jsx'),
  packageJson: path.join(repositoryRoot, 'src/frontend/project-time-web/package.json'),
  webDockerfile: path.join(repositoryRoot, 'deployment/containers/web/Dockerfile'),
  catalog: path.join(repositoryRoot, 'docs/MODULE-CATALOG.md'),
  register: path.join(repositoryRoot, 'docs/MODULE-WORK-REGISTER.md'),
  tracker: path.join(repositoryRoot, 'docs/production-readiness/AUGUST_PRODUCTION_READINESS_TRACKER.md'),
  calculationProject: path.join(repositoryRoot, 'scripts/module-066-validation/ProjectPulse.Module066.Validation.csproj'),
  calculationProgram: path.join(repositoryRoot, 'scripts/module-066-validation/Program.cs')
};

const assertions = [];

function assertInvariant(name, condition, detail) {
  assertions.push({ name, condition, detail });
  console.log(`${name}=${condition ? 'PASSED' : 'FAILED'}${detail ? ` — ${detail}` : ''}`);
}

function readRequired(name, filePath) {
  const exists = fs.existsSync(filePath);
  assertInvariant(`MODULE_066_${name}_EXISTS`, exists, path.relative(repositoryRoot, filePath));
  return exists ? fs.readFileSync(filePath, 'utf8') : '';
}

const backend = readRequired('BACKEND', paths.backend);
const contracts = readRequired('CONTRACTS', paths.contracts);
const schedule = readRequired('SCHEDULE_ENGINE', paths.schedule);
const ai = readRequired('AI_REQUEST_FACTORY', paths.ai);
const brand = readRequired('BRAND_ASSETS', paths.brand);
const artifacts = readRequired('ARTIFACT_RENDERER', paths.artifacts);
const frontend = readRequired('FRONTEND', paths.frontend);
const stylesheet = readRequired('STYLESHEET', paths.stylesheet);
const logoJpeg = fs.existsSync(paths.logoJpeg) ? fs.readFileSync(paths.logoJpeg) : Buffer.alloc(0);
const logoPngExists = fs.existsSync(paths.logoPng);
assertInvariant('MODULE_066_US_SIGNAL_LOGO_PNG_EXISTS', logoPngExists, path.relative(repositoryRoot, paths.logoPng));

const readme = readRequired('README', paths.readme);
const matrix = readRequired('CAPABILITY_MATRIX', paths.matrix);
const apiContract = readRequired('API_CONTRACT', paths.contract);
const authorization = readRequired('AUTHORIZATION_SECURITY', paths.authorization);
const persistence = readRequired('PERSISTENCE_DESIGN', paths.persistence);
const scheduling = readRequired('SCHEDULE_DOCUMENT', paths.scheduling);
const aiDoc = readRequired('AI_DOCUMENT', paths.aiDoc);
const artifactsDoc = readRequired('ARTIFACTS_DOCUMENT', paths.artifactsDoc);
const overlap = readRequired('OVERLAP_GATES', paths.overlap);
const evidence = readRequired('VALIDATION_EVIDENCE', paths.evidence);
const program = readRequired('PROGRAM', paths.program);
const app = readRequired('APP', paths.app);
const packageJson = readRequired('PACKAGE', paths.packageJson);
const webDockerfile = readRequired('WEB_DOCKERFILE', paths.webDockerfile);
const catalog = readRequired('CATALOG', paths.catalog);
const register = readRequired('WORK_REGISTER', paths.register);
const tracker = readRequired('PRODUCTION_TRACKER', paths.tracker);
const calculationProject = readRequired('CALCULATION_PROJECT', paths.calculationProject);
const calculationProgram = readRequired('CALCULATION_PROGRAM', paths.calculationProgram);

const moduleBackend = [backend, contracts, schedule, ai, brand, artifacts].join('\n');
const moduleDocs = [readme, matrix, apiContract, authorization, persistence, scheduling, aiDoc, artifactsDoc, overlap, evidence].join('\n');

assertInvariant(
  'MODULE_066_TYPED_MAP_METHOD',
  backend.includes('MapProjectFlowHiveEndpoints') &&
    backend.includes('(Func<HttpContext, IResult>)GetCapabilities') &&
    backend.includes('(Func<ProjectFlowHivePlanRequest, HttpContext, IResult>)CalculateSchedule'),
  'isolated route registration and typed handlers'
);

for (const route of [
  '/api/project-flowhive/capabilities',
  '/api/project-flowhive/portfolio',
  '/api/project-flowhive/readiness',
  '/api/project-flowhive/planning/validate',
  '/api/project-flowhive/schedule/calculate',
  '/api/project-flowhive/plans/drafts',
  '/api/project-flowhive/plans/{planId:guid}/baseline',
  '/api/project-flowhive/ai/request-preview',
  '/api/project-flowhive/artifacts/readiness',
  '/api/project-flowhive/artifacts/pdf-preview',
  '/api/project-flowhive/artifacts/excel-preview'
]) {
  assertInvariant(
    `MODULE_066_ROUTE_${route.replaceAll(/[^a-z0-9]+/gi, '_').toUpperCase()}`,
    backend.includes(`"${route}"`),
    route
  );
}

assertInvariant(
  'MODULE_066_EFFECTIVE_AND_ACTUAL_IDENTITY',
  backend.includes('ProjectPulseEffectiveUserId') &&
    backend.includes('ProjectPulseActualUserId') &&
    frontend.includes("useIdentityProfile") &&
    frontend.includes('IdentityAvatar') &&
    backend.includes('resource.user_id AS resource_user_id'),
  'Module 062 identity and actual/effective user boundaries'
);

assertInvariant(
  'MODULE_066_CANONICAL_READ_SCOPE_PRESERVED',
  backend.includes('projectpulse_team_scope_assignments') &&
    backend.includes('reporting_relationships') &&
    backend.includes('PROJECT_TEAM_COORDINATOR') &&
    backend.includes('ENGINEERING_TEAM_LEAD'),
  'backend role, team, reporting, and assignment scope'
);

assertInvariant(
  'MODULE_066_PERSISTENCE_HARD_LOCK',
  contracts.includes('LockedProjectFlowHivePlanRepository') &&
    contracts.includes('WritesEnabled => false') &&
    backend.includes('StatusCodes.Status423Locked') &&
    backend.includes('persistence_locked') &&
    backend.includes('baseline_locked'),
  'no registered planning write repository'
);

const forbiddenSqlMutation = /\b(?:INSERT\s+INTO|UPDATE\s+[a-z_]|DELETE\s+FROM|ALTER\s+TABLE|CREATE\s+TABLE|DROP\s+TABLE|TRUNCATE\s+TABLE)\b/i;
assertInvariant(
  'MODULE_066_NO_MUTATING_SQL',
  !forbiddenSqlMutation.test(moduleBackend),
  'module backend contains no mutating SQL or schema statement'
);

assertInvariant(
  'MODULE_066_SCHEDULE_DEPENDENCY_TYPES',
  ['"FS"', '"SS"', '"FF"', '"SF"'].every((marker) => schedule.includes(marker)) &&
    schedule.includes('StartOffset') &&
    schedule.includes('LagWorkingDays'),
  'FS, SS, FF, SF and lead/lag source'
);

assertInvariant(
  'MODULE_066_SCHEDULE_VALIDATION',
  schedule.includes('duplicate_wbs') &&
    schedule.includes('parent_required') &&
    schedule.includes('parent_hierarchy_mismatch') &&
    schedule.includes('self_dependency') &&
    schedule.includes('duplicate_dependency') &&
    schedule.includes('dependency_cycle') &&
    schedule.includes('assignment_identity_required'),
  'WBS, hierarchy, dependency, cycle, and identity validation'
);

assertInvariant(
  'MODULE_066_SCHEDULE_NORMALIZATION',
  schedule.includes('string.Equals(Clean(row.SuccessorWbs), wbs') &&
    schedule.includes('string.Equals(Clean(row.PredecessorWbs), predecessor') &&
    schedule.includes('StartOffset(string? type') &&
    schedule.includes('Clean(type)?.ToUpperInvariant()'),
  'blank dependency types default safely to FS and trimmed WBS references remain effective'
);

assertInvariant(
  'MODULE_066_EXECUTABLE_VALIDATION_SUITE',
  calculationProject.includes('<TargetFramework>net10.0</TargetFramework>') &&
    calculationProgram.includes('MODULE_066_TEST_FS') &&
    calculationProgram.includes('MODULE_066_TEST_SS') &&
    calculationProgram.includes('MODULE_066_TEST_FF') &&
    calculationProgram.includes('MODULE_066_TEST_SF') &&
    calculationProgram.includes('MODULE_066_TEST_PDF_LOGO') &&
    calculationProgram.includes('MODULE_066_TEST_XLSX_LOGO_HASH'),
  'calculation, cycle, calendar, AI-lock, and branded-artifact execution tests exist'
);

assertInvariant(
  'MODULE_066_CRITICAL_PATH_AND_FLOAT',
  schedule.includes('TopologicalOrder') &&
    contracts.includes('LatestStartIndex') &&
    schedule.includes('latest[successor] - weight') &&
    contracts.includes('TotalFloatWorkingDays') &&
    schedule.includes('totalFloat == 0') &&
    schedule.includes('freeFloat'),
  'forward/reverse pass, total/free float, and critical task marker'
);

assertInvariant(
  'MODULE_066_WEEKDAY_PREVIEW_BOUNDARY',
  schedule.includes('DayOfWeek.Saturday') &&
    schedule.includes('DayOfWeek.Sunday') &&
    schedule.includes('weekday_preview_module_057_not_applied') &&
    scheduling.includes('Module 057'),
  'preview does not claim holiday/resource calendar authority'
);

assertInvariant(
  'MODULE_066_SHARED_AI_ONLY',
  ai.includes('requiredService = "ProjectPulseAiRouter"') &&
    ai.includes('feature = "project_flowhive_plan"') &&
    ai.includes('new[] { "claude", "openai", "local_template" }') &&
    ai.includes('executionEnabled = false') &&
    !/new\s+HttpClient|IHttpClientFactory|api\.anthropic|api\.openai|ANTHROPIC_API_KEY|OPENAI_API_KEY/i.test(moduleBackend),
  'Module 064 contract with no direct client or secret read'
);

assertInvariant(
  'MODULE_066_AI_REFUSAL_AND_DRAFT_GUARDS',
  ai.includes('refusalFailover = "blocked"') &&
    ai.includes('AI output is a draft') &&
    aiDoc.includes('safety refusal terminates routing') &&
    aiDoc.includes('Claude') && aiDoc.includes('OpenAI') && aiDoc.includes('local'),
  'refusal does not fail over and output cannot baseline itself'
);

const expectedLogoHash = 'c4fc4b33f744d065deeec531f393aa39996273e51eb946a452b1319e6e529183';
const actualLogoHash = crypto.createHash('sha256').update(logoJpeg).digest('hex');
const base64Block = brand.match(/LogoJpegBase64\s*=\s*([\s\S]*?);/);
const embeddedBase64 = base64Block
  ? [...base64Block[1].matchAll(/"([A-Za-z0-9+/=]+)"/g)].map((match) => match[1]).join('')
  : '';
const embeddedLogoHash = embeddedBase64
  ? crypto.createHash('sha256').update(Buffer.from(embeddedBase64, 'base64')).digest('hex')
  : '';
assertInvariant(
  'MODULE_066_US_SIGNAL_LOGO_EXACT',
  actualLogoHash === expectedLogoHash &&
    embeddedLogoHash === expectedLogoHash &&
    brand.includes(expectedLogoHash),
  `repository=${actualLogoHash || 'missing'}, embedded=${embeddedLogoHash || 'missing'}`
);

assertInvariant(
  'MODULE_066_BRANDED_ARTIFACTS',
  artifacts.includes('BuildPdf') &&
    artifacts.includes('BuildExcel') &&
    artifacts.includes('ProjectFlowHiveBrandAssets.LogoJpeg') &&
    artifacts.includes('INTERNAL DRAFT — NOT A CUSTOMER BASELINE') &&
    backend.includes('customer_export_locked'),
  'US Signal branded internal PDF/XLSX source and customer lock'
);

assertInvariant(
  'MODULE_066_NO_EXTERNAL_CUSTOMER_LINK',
  !/Map(?:Post|Put|Patch)\([^\n]+(?:share|customer-link|public-link|token)/i.test(backend) &&
    backend.includes('customerSharingEnabled = false') &&
    artifactsDoc.includes('There is no customer token'),
  'no customer link/token/delivery endpoint'
);

assertInvariant(
  'MODULE_066_FRONTEND_FULL_PHASES',
  frontend.includes('data-phase="066A.1-066E"') &&
    frontend.includes('Portfolio') &&
    frontend.includes('Planner') &&
    frontend.includes('Timeline & risk') &&
    frontend.includes('AI draft studio') &&
    frontend.includes('Branded exports') &&
    frontend.includes('Governance'),
  'phase-aware full workspace source'
);

assertInvariant(
  'MODULE_066_FRONTEND_BROWSER_LOCAL_DRAFT',
  frontend.includes('Local planning draft created in browser memory') &&
    frontend.includes('Save draft — locked') &&
    frontend.includes('Establish baseline — locked') &&
    !frontend.includes("postJson('/api/project-flowhive/plans/drafts'") &&
    !frontend.includes("postJson('/api/project-flowhive/plans/") &&
    !/localStorage\.setItem\([^)]*flowhive/i.test(frontend),
  'no hidden browser or server persistence'
);

assertInvariant(
  'MODULE_066_FRONTEND_IDENTITY_DROPDOWN',
  frontend.includes('identityOptions') &&
    frontend.includes('resourceUserId') &&
    frontend.includes('Assigned identity') &&
    frontend.includes('useIdentityProfile'),
  'assignments preserve Module 062-backed user IDs'
);

assertInvariant(
  'MODULE_066_FRONTEND_COMPUTE_AND_ARTIFACT_ROUTES',
  frontend.includes("postJson('/api/project-flowhive/planning/validate'") &&
    frontend.includes("postJson('/api/project-flowhive/schedule/calculate'") &&
    frontend.includes("postJson('/api/project-flowhive/ai/request-preview'") &&
    frontend.includes('/api/project-flowhive/artifacts/${format}-preview'),
  'only side-effect-free active frontend actions'
);

assertInvariant(
  'MODULE_066_SCOPED_STYLES',
  stylesheet.includes('.project-flowhive-center') &&
    stylesheet.includes('.flowhive-timeline') &&
    stylesheet.includes('.flowhive-ai-layout') &&
    stylesheet.includes('.flowhive-export-grid') &&
    !/(^|\n)\s*(?:html|body|:root|#root|main|button|table|input|select|textarea)\s*[{,]/m.test(stylesheet),
  'no unscoped application shell selector'
);

for (const requirement of ['GOV-015', 'RBAC-019', 'WRK-011', 'AI-008', 'AI-019', 'RPT-013']) {
  assertInvariant(
    `MODULE_066_REQUIREMENT_${requirement.replace('-', '_')}`,
    matrix.includes(requirement),
    'capability matrix maps the tracker requirement'
  );
}

for (const phase of ['066A.1', '066B', '066C', '066D', '066E']) {
  assertInvariant(
    `MODULE_066_PHASE_${phase.replace('.', '_')}`,
    readme.includes(phase) && matrix.includes(phase),
    `${phase} source and gate documented`
  );
}

assertInvariant(
  'MODULE_066_BASE_AND_MODULE002_RECORDED',
  [readme, overlap, backend].every((content) => content.includes('2b4a6d1a1242a25b52110a2a209ff8ddda0b8ca4')) &&
    [readme, overlap, backend].every((content) => content.includes('f5ede8f6717b01c8f4bf7905b433fead38210007')),
  'verified Module 002 source and merge baseline'
);

assertInvariant(
  'MODULE_066_CENTRAL_GOVERNANCE_OWNERSHIP',
  register.includes('feature/module-066-complete-integrated-on-main-20260719') &&
    register.includes('2b4a6d1a1242a25b52110a2a209ff8ddda0b8ca4') &&
    catalog.includes('066A.1–066E') &&
    tracker.includes('Module 066 — Project FlowHive'),
  'catalog, work register, and production tracker record the consolidated source package'
);

const backendRegistrationCount = (program.match(/\bapp\.MapProjectFlowHiveEndpoints\(\);/g) ?? []).length;
assertInvariant(
  'MODULE_066_SHARED_BACKEND_REGISTRATION_ACTIVATED',
  backendRegistrationCount === 1 &&
    program.includes('MODULE_066A1_PROJECT_FLOWHIVE_ENDPOINT_MAP_START') &&
    program.includes('MODULE_066A1_PROJECT_FLOWHIVE_ENDPOINT_MAP_END'),
  `found ${backendRegistrationCount}, expected exactly one guarded Program.cs registration`
);

const frontendImportCount = (app.match(/import ProjectFlowHiveCenter from ['"]\.\/ProjectFlowHiveCenter\.jsx['"];/g) ?? []).length;
const frontendRouteDefinitionCount = (app.match(/route:\s*['"]project-flowhive['"]/g) ?? []).length;
const frontendMountCount = (app.match(/<ProjectFlowHiveCenter\s*\/>/g) ?? []).length;

assertInvariant(
  'MODULE_066_SHARED_FRONTEND_IMPORT_ACTIVATED',
  frontendImportCount === 1,
  `found ${frontendImportCount}, expected exactly one ProjectFlowHiveCenter import`
);

assertInvariant(
  'MODULE_066_ROLE_AWARE_ROUTE_REGISTRATION',
  frontendRouteDefinitionCount === 2 &&
    app.includes("href: '#project-flowhive'") &&
    app.includes("navLabel: 'MODULE 066'") &&
    app.includes('MODULE_066A1_PROJECT_FLOWHIVE_NAV_START') &&
    app.includes("roleCodes: ['ENGINEER'") &&
    app.includes("'PROJECT_TEAM_COORDINATOR'") &&
    app.includes("'ENGINEERING_TEAM_LEAD'") &&
    app.includes("'EXECUTIVE'") &&
    app.includes("'SYSTEM_ADMINISTRATION'") &&
    app.includes("'MANAGE_ALL'"),
  `found ${frontendRouteDefinitionCount}, expected one role navigation record and one installed-module registry record`
);

assertInvariant(
  'MODULE_066_INSTALLED_MODULE_REGISTRY',
  app.includes('MODULE_066A1_PROJECT_FLOWHIVE_INSTALLED_REGISTRY_START') &&
    app.includes('MODULE_066A1_PROJECT_FLOWHIVE_INSTALLED_REGISTRY_END') &&
    app.includes("group: 'Project Delivery'"),
  'dashboard and Module 999 can enumerate the source-integrated route'
);

assertInvariant(
  'MODULE_066_ROLE_AWARE_ROUTE_MOUNT',
  frontendMountCount === 1 &&
    app.includes("const canViewProjectFlowHive = visibleRoleModules.some((module) => module.route === 'project-flowhive');") &&
    app.includes("activeRoute === 'project-flowhive' && canViewProjectFlowHive") &&
    app.includes('MODULE_066A1_PROJECT_FLOWHIVE_ROUTE_START'),
  `found ${frontendMountCount}, expected one authorized route mount`
);

let parsedPackage = {};
try {
  parsedPackage = JSON.parse(packageJson);
} catch {
  parsedPackage = {};
}

assertInvariant(
  'MODULE_066_PACKAGE_VALIDATOR_WIRING',
  parsedPackage.scripts?.['validate:module066'] === 'node ./scripts/validate-module-066-project-flowhive.mjs' &&
    parsedPackage.scripts?.build?.includes('npm run validate:module059') &&
    parsedPackage.scripts?.build?.includes('npm run validate:module062') &&
    parsedPackage.scripts?.build?.includes('npm run validate:module002') &&
    parsedPackage.scripts?.build?.includes('npm run validate:module066') &&
    parsedPackage.scripts?.build?.endsWith('vite build'),
  'production build preserves protected validators and adds Module 066 before Vite'
);

for (const backendFile of [
  'ProjectFlowHiveModule.cs',
  'ProjectFlowHivePlanningContracts.cs',
  'ProjectFlowHiveScheduleEngine.cs',
  'ProjectFlowHiveAiRequestFactory.cs',
  'ProjectFlowHiveBrandAssets.cs',
  'ProjectFlowHiveArtifactRenderer.cs'
]) {
  assertInvariant(
    `MODULE_066_CONTAINER_${backendFile.replaceAll(/[^a-z0-9]+/gi, '_').toUpperCase()}`,
    webDockerfile.includes(`src/backend/ProjectTime.Api/Modules/${backendFile}`),
    `web build context includes ${backendFile}`
  );
}

assertInvariant(
  'MODULE_066_CONTAINER_GOVERNANCE_CONTEXT',
  webDockerfile.includes('docs/modules/module-066-project-flowhive/') &&
    webDockerfile.includes('docs/MODULE-CATALOG.md') &&
    webDockerfile.includes('docs/MODULE-WORK-REGISTER.md') &&
    webDockerfile.includes('docs/production-readiness/AUGUST_PRODUCTION_READINESS_TRACKER.md') &&
    webDockerfile.includes('scripts/module-066-validation/') &&
    webDockerfile.includes('src/backend/ProjectTime.Api/Modules/IdentityProfileModule.cs'),
  'container validation receives complete Module 066 and protected Module 062 evidence'
);

function filesBelow(directory) {
  if (!fs.existsSync(directory)) return [];
  return fs.readdirSync(directory, { withFileTypes: true }).flatMap((entry) => {
    const target = path.join(directory, entry.name);
    return entry.isDirectory() ? filesBelow(target) : [target];
  });
}

const activeExternalArtifacts = [
  ...filesBelow(path.join(repositoryRoot, 'database')),
  ...filesBelow(path.join(repositoryRoot, 'deployment'))
].filter((filePath) => /(?:module[-_]?066|flowhive)/i.test(path.basename(filePath)));
assertInvariant(
  'MODULE_066_NO_DATABASE_OR_DEPLOYMENT_ARTIFACT',
  activeExternalArtifacts.length === 0,
  activeExternalArtifacts.length ? activeExternalArtifacts.map((filePath) => path.relative(repositoryRoot, filePath)).join(', ') : 'none'
);

assertInvariant(
  'MODULE_066_STATUS_SOURCE_INTEGRATED_NOT_DEPLOYED',
  readme.includes('source-integrated') &&
    readme.includes('not merged, deployed, or runtime-verified') &&
    matrix.includes('No capability in this source-only package is labeled active') &&
    !/status\s*=\s*"(?:active|deployed)"/i.test(backend),
  'source integration is not represented as merge, deployment, or runtime completion'
);

const failed = assertions.filter((assertion) => !assertion.condition);
console.log('');
console.log(`MODULE_066_VALIDATION_CHECKS=${assertions.length}`);
console.log('MODULE_066_IMPLEMENTATION_PHASES=066A.1_066B_066C_066D_066E');
console.log('MODULE_066_PERSISTENCE=LOCKED_NOT_APPLIED');
console.log('MODULE_066_AI_EXECUTION=LOCKED_TO_MODULE_064');
console.log('MODULE_066_CUSTOMER_SHARING=LOCKED');
console.log('MODULE_066_SHARED_INTEGRATION=ACTIVATED_SOURCE_UNCOMMITTED');

if (failed.length) {
  console.error('MODULE_066_COMPLETE_SOURCE_CONTRACT=FAILED');
  failed.forEach((failure) => console.error(`- ${failure.name}: ${failure.detail}`));
  process.exit(1);
}

console.log('MODULE_066_COMPLETE_SOURCE_CONTRACT=PASSED');
