import fs from 'node:fs';
import path from 'node:path';

const root = path.resolve(process.cwd(), '..', '..', '..');
const files = {
  backend: 'src/backend/ProjectTime.Api/Modules/QualificationsCertificationModule.cs',
  frontend: 'src/frontend/project-time-web/src/QualificationsCertificationCenter.jsx',
  stylesheet: 'src/frontend/project-time-web/src/qualifications-certification-center.css',
  program: 'src/backend/ProjectTime.Api/Program.cs',
  app: 'src/frontend/project-time-web/src/App.jsx',
  package: 'src/frontend/project-time-web/package.json',
  docker: 'deployment/containers/web/Dockerfile',
  readme: 'docs/modules/module-069-qualifications-certifications/README.md',
  api: 'docs/modules/module-069-qualifications-certifications/API-CONTRACT.md',
  matrix: 'docs/modules/module-069-qualifications-certifications/CAPABILITY-MATRIX.md',
  security: 'docs/modules/module-069-qualifications-certifications/SECURITY-AND-OPERATIONS.md',
  overlap: 'docs/modules/module-069-qualifications-certifications/OVERLAP-AND-INTEGRATION.md',
  catalog: 'docs/MODULE-CATALOG.md',
  register: 'docs/MODULE-WORK-REGISTER.md',
  tracker: 'docs/production-readiness/AUGUST_PRODUCTION_READINESS_TRACKER.md'
};
const text = (file) => fs.readFileSync(path.join(root, file), 'utf8');
const exists = (file) => fs.existsSync(path.join(root, file));
const count = (value, pattern) => [...value.matchAll(pattern)].length;
const checks = [];
function check(name, condition, evidence) {
  checks.push(condition);
  console.log(`MODULE_069_${name}=${condition ? 'PASSED' : 'FAILED'} — ${evidence}`);
}

for (const [name, file] of Object.entries(files)) check(`${name.toUpperCase()}_EXISTS`, exists(file), file);

const backend = text(files.backend);
const frontend = text(files.frontend);
const stylesheet = text(files.stylesheet);
const program = text(files.program);
const app = text(files.app);
const packageJson = JSON.parse(text(files.package));
const docker = text(files.docker);
const docs = [files.readme, files.api, files.matrix, files.security, files.overlap].map(text).join('\n');
const governance = [files.catalog, files.register, files.tracker].map(text).join('\n');

check('MAP_METHOD', backend.includes('MapQualificationsCertificationEndpoints'), 'isolated endpoint registration');
check('GET_CAPABILITIES', backend.includes('/api/qualifications/capabilities'), 'capability contract');
check('GET_MATRIX', backend.includes('/api/qualifications/matrix'), 'matrix contract');
check('TYPED_HANDLERS', count(backend, /Func<[^>]*Task<IResult>>/g) >= 1 && backend.includes('Task<IResult>>)GetMatrixAsync'), 'typed minimal API handlers');
check('EFFECTIVE_IDENTITY', backend.includes('ProjectPulseEffectiveUserId') && docs.includes('Stable `app_users.user_id`'), 'shared identity approach');
check('SERVER_SCOPE', backend.includes('broad_scope') && backend.includes('team_scope') && backend.includes('u.user_id = @user_id'), 'organization/team/self scope');
check('PARAMETERIZED_FILTERS', backend.includes('command.Parameters.AddWithValue("search"') && backend.includes('command.Parameters.AddWithValue("category"'), 'server filter parameters');
check('EXPIRATION_CALCULATION', backend.includes("CURRENT_DATE + 90") && backend.includes("'expiring'") && backend.includes("'expired'"), '90-day lifecycle');
check('READ_ONLY_BACKEND', !/Map(?:Post|Put|Patch|Delete)\s*\(/.test(backend), 'no mutation route');
check('NO_MUTATING_SQL', !/\b(?:INSERT|UPDATE|DELETE|ALTER|DROP|CREATE|TRUNCATE)\s+(?:INTO|TABLE|FROM|VIEW|INDEX|SCHEMA)\b/i.test(backend), 'SELECT-only module');
check('SANITIZED_FAILURE', backend.includes('Qualifications matrix unavailable') && !backend.includes('detail: exception.Message'), 'raw exception excluded');
check('NO_DATABASE_ARTIFACT', !fs.existsSync(path.join(root, 'database/migrations/069-qualifications.sql')), 'no migration');

check('FRONTEND_MARKERS', frontend.includes('data-module="069"') && frontend.includes('data-mode="read-only-matrix"'), 'governed UI markers');
check('FRONTEND_ENDPOINTS', frontend.includes('/api/qualifications/capabilities') && frontend.includes('/api/qualifications/matrix'), 'both GET consumers');
check('FRONTEND_FILTERS', frontend.includes('All categories') && frontend.includes('Expiring') && frontend.includes('Unrecorded'), 'category/lifecycle filters');
check('FRONTEND_IDENTITY', frontend.includes('row.displayName') && frontend.includes('row.email') && frontend.includes('row.userId'), 'identity-backed rows');
check('READ_ONLY_FRONTEND', !/method:\s*['"](?:POST|PUT|PATCH|DELETE)['"]/.test(frontend), 'no mutation request');
check('SCOPED_STYLES', !/(^|\n)\s*(?:body|html|\.panel|\.app-shell|\.sidebar)\s*\{/m.test(stylesheet), 'no global shell selector');

check('PROGRAM_REGISTRATION', count(program, /app\.MapQualificationsCertificationEndpoints\(\);/g) === 1, 'backend registered once');
check('APP_IMPORT', count(app, /import QualificationsCertificationCenter from '\.\/QualificationsCertificationCenter\.jsx';/g) === 1, 'frontend import once');
check('APP_MOUNT', count(app, /<QualificationsCertificationCenter authSession=\{authSession\} \/>/g) === 1, 'frontend mount once');
check('ROUTE_REGISTRY', count(app, /route:\s*['"]qualifications-certifications['"]/g) >= 2, 'workspace and installed registries');
check('BUILD_GUARD', packageJson.scripts?.build?.includes('validate:module069') && packageJson.scripts?.['validate:module069']?.includes('validate-module-069-qualifications.mjs'), 'production build guard');
for (const required of [files.backend, 'docs/modules/module-069-qualifications-certifications/', files.catalog, files.register, files.tracker]) check(`DOCKER_${path.basename(required).replace(/[^a-z0-9]+/gi, '_').toUpperCase()}`, docker.includes(required), required);

check('TRACKER_REQUIREMENTS', docs.includes('RES-007') && docs.includes('RES-012'), 'RES-007 through RES-012');
check('MUTATION_BOUNDARY', docs.includes('No mutation endpoint') || docs.includes('no mutation endpoint'), 'deferred writes documented');
check('GOVERNANCE_REGISTERED', governance.includes('| 069 | Qualifications & Certification Matrix') && governance.includes('feature/modules-064-074-release-train-on-main-20260719'), 'central governance');
check('MODULE_011_PRESERVED', governance.includes('Module 011') && governance.includes('Work Task Builder'), 'existing module not reused');
check('MODULE_002_HOLD', governance.includes('Module 002') && governance.includes('semantic'), 'shared-file integration hold');
check('STATUS_064', governance.includes('MODULE_064_STATUS=RELEASE_TRAIN_CANDIDATE_UNCOMMITTED'), 'exact Module 064 status');
check('STATUS_068', governance.includes('MODULE_068_STATUS=RELEASE_TRAIN_CANDIDATE_UNCOMMITTED_READ_ONLY'), 'exact Module 068 status');
check('OVERLAP_MODULES', ['Module 002', 'Module 064', 'Module 068'].every((value) => docs.includes(value)), '002/064/068 comparison owners');
check('OVERLAP_SURFACES', ['docs/MODULE-CATALOG.md', 'docs/MODULE-WORK-REGISTER.md', 'AUGUST_PRODUCTION_READINESS_TRACKER.md', 'Program.cs', 'App.jsx', 'package.json'].every((value) => docs.includes(value)), 'mandatory shared surfaces');
check('FINAL_COMMIT_BLOCKED', docs.includes('final commit gate is `BLOCKED`'), 'refreshed overlap evidence required');
check('NO_AZURE_DATABASE_ENTRA', docs.includes('No change') && docs.includes('undeployed'), 'source-only authorization boundary');

console.log('');
console.log(`MODULE_069_VALIDATION_CHECKS=${checks.length}`);
console.log('MODULE_069_IMPLEMENTATION=FULL_READ_ONLY_QUALIFICATIONS_CERTIFICATION_MATRIX');
console.log('MODULE_069_SHARED_INTEGRATION=RELEASE_TRAIN_SOURCE_REGISTERED_UNCOMMITTED');
console.log('MODULE_069_AZURE_DATABASE_ENTRA_CHANGES=NONE');
if (checks.some((value) => !value)) {
  console.error('MODULE_069_CONTRACT=FAILED');
  process.exit(1);
}
console.log('MODULE_069_CONTRACT=PASSED');
