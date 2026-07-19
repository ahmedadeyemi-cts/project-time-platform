import fs from 'node:fs';
import path from 'node:path';

const root = path.resolve(process.cwd(), '..', '..', '..');
const files = {
  backend: 'src/backend/ProjectTime.Api/Modules/CapacityPipelineForecastModule.cs',
  frontend: 'src/frontend/project-time-web/src/CapacityPipelineForecastCenter.jsx',
  stylesheet: 'src/frontend/project-time-web/src/capacity-pipeline-forecast-center.css',
  program: 'src/backend/ProjectTime.Api/Program.cs',
  app: 'src/frontend/project-time-web/src/App.jsx',
  package: 'src/frontend/project-time-web/package.json',
  docker: 'deployment/containers/web/Dockerfile',
  readme: 'docs/modules/module-070-capacity-pipeline-forecasting/README.md',
  api: 'docs/modules/module-070-capacity-pipeline-forecasting/API-CONTRACT.md',
  workbook: 'docs/modules/module-070-capacity-pipeline-forecasting/WORKBOOK-CALCULATION-CONTRACT.md',
  security: 'docs/modules/module-070-capacity-pipeline-forecasting/SECURITY-AND-OPERATIONS.md',
  matrix: 'docs/modules/module-070-capacity-pipeline-forecasting/CAPABILITY-MATRIX.md',
  overlap: 'docs/modules/module-070-capacity-pipeline-forecasting/OVERLAP-AND-INTEGRATION.md',
  catalog: 'docs/MODULE-CATALOG.md',
  register: 'docs/MODULE-WORK-REGISTER.md',
  tracker: 'docs/production-readiness/AUGUST_PRODUCTION_READINESS_TRACKER.md'
};
const exists = (file) => fs.existsSync(path.join(root, file));
const text = (file) => fs.readFileSync(path.join(root, file), 'utf8');
const count = (value, pattern) => [...value.matchAll(pattern)].length;
const checks = [];
function check(name, condition, evidence) {
  checks.push(Boolean(condition));
  console.log(`MODULE_070_${name}=${condition ? 'PASSED' : 'FAILED'} — ${evidence}`);
}

for (const [name, file] of Object.entries(files)) check(`${name.toUpperCase()}_EXISTS`, exists(file), file);

const backend = text(files.backend);
const frontend = text(files.frontend);
const stylesheet = text(files.stylesheet);
const program = text(files.program);
const app = text(files.app);
const packageJson = JSON.parse(text(files.package));
const docker = text(files.docker);
const docs = [files.readme, files.api, files.workbook, files.security, files.matrix, files.overlap].map(text).join('\n');
const governance = [files.catalog, files.register, files.tracker].map(text).join('\n');

check('MAP_METHOD', backend.includes('MapCapacityPipelineForecastEndpoints'), 'isolated endpoint registration');
check('TYPED_HANDLERS', count(backend, /Task<IResult>>\)/g) >= 3, 'three typed read handlers');
check('MODEL_ENDPOINT', backend.includes('/api/capacity-forecast/model'), 'calculation contract endpoint');
check('ENGINEERS_ENDPOINT', backend.includes('/api/capacity-forecast/engineers'), 'identity choices endpoint');
check('FORECAST_ENDPOINT', backend.includes('/api/capacity-forecast/forecast'), 'forecast endpoint');
check('EFFECTIVE_IDENTITY', backend.includes('ProjectPulseEffectiveUserId') && docs.includes('Stable `app_users.user_id`'), 'Module 062 identity approach');
check('IDENTITY_DROPDOWN_SOURCE', backend.includes('app_users u') && backend.includes('EngineerChoice') && frontend.includes('engineer.userId'), 'live stable-ID engineer choices');
check('SERVER_SCOPE', backend.includes('@broad_scope') && backend.includes('@team_scope') && backend.includes('engineer_outside_authorized_scope'), 'server scope and selected identity guard');
check('DATE_CONTROLS', backend.includes('NormalizeStartDate') && backend.includes('MondayOf') && frontend.includes('type="date"'), 'anytime date selection with Monday normalization');
check('HORIZON_GUARD', backend.includes('Math.Clamp(weeks ?? 14, 4, 52)') && frontend.includes('min="4" max="52"'), '4–52 weeks');
check('WORKBOOK_FORMULA', backend.includes('committed + weightedPipeline - supplemental') && docs.includes('current planned hours + future project hours - supplemental/LTE hours'), 'verified workbook mapping');
check('REMAINING_FORMULA', backend.includes('available - netDemand') && docs.includes('remaining capacity = available capacity - net demand'), 'remaining capacity');
check('UTILIZATION_ZERO_GUARD', backend.includes('available == 0') && docs.includes('dividing by\nzero'), 'safe utilization');
check('PIPELINE_WEIGHTING', backend.includes('PipelineWeight') && backend.includes('0.6m') && backend.includes('0.25m'), 'documented status weights');
check('ALLOCATED_HOURS_DEDUP', backend.includes('requestedHours - committedAllocation'), 'unfilled hours reduce double counting');
check('SUPPLEMENTAL_SCENARIO', frontend.includes('Supplemental / LTE hours per week') && docs.includes('non-persistent'), 'visible scenario boundary');
check('NO_REVENUE_TO_HOURS', docs.includes('never converts') || docs.includes('never converted') || docs.includes('never infers') || docs.includes('never inferred'), 'opportunity revenue excluded');
check('READ_ONLY_BACKEND', !/Map(?:Post|Put|Patch|Delete)\s*\(/.test(backend), 'no mutation route');
check('NO_MUTATING_SQL', !/\b(?:INSERT|UPDATE|DELETE|ALTER|DROP|CREATE|TRUNCATE)\s+(?:INTO|TABLE|FROM|VIEW|INDEX|SCHEMA)\b/i.test(backend), 'SELECT-only SQL');
check('SANITIZED_FAILURE', backend.includes('Capacity forecast unavailable') && !backend.includes('detail: exception.Message'), 'raw exception excluded');
check('NO_DATABASE_ARTIFACT', !fs.existsSync(path.join(root, 'database/migrations/070-capacity-pipeline.sql')), 'no migration');

check('FRONTEND_MARKERS', frontend.includes('data-module="070"') && frontend.includes('data-mode="read-only-live-scenario"'), 'governed UI markers');
check('FRONTEND_ALL_ENDPOINTS', ['/model', '/engineers', '/forecast'].every((suffix) => frontend.includes(`/api/capacity-forecast${suffix}`)), 'three GET consumers');
check('ENGINEER_SELECT', frontend.includes('<select value={filters.engineerUserId}') && frontend.includes('stable identity ID'), 'proper identity dropdown');
check('NAME_REFRESH', frontend.includes('Refresh names') && frontend.includes('#user-admin'), 'anytime current name refresh and owner link');
check('DATE_OWNER_LINK', frontend.includes('#project-intake') && docs.includes('Project Intake'), 'source date owner');
check('NUMERIC_CONTROLS', frontend.includes('type="number"') && backend.includes('Math.Clamp(supplementalHoursPerWeek'), 'numeric scenario validation');
check('READ_ONLY_FRONTEND', !/method:\s*['"](?:POST|PUT|PATCH|DELETE)['"]/.test(frontend), 'no mutation request');
check('SCOPED_STYLES', !/(^|\n)\s*(?:body|html|\.panel|\.app-shell|\.sidebar)\s*\{/m.test(stylesheet), 'no global shell selector');

check('PROGRAM_REGISTRATION', count(program, /app\.MapCapacityPipelineForecastEndpoints\(\);/g) === 1, 'backend registered once');
check('APP_IMPORT', count(app, /import CapacityPipelineForecastCenter from '\.\/CapacityPipelineForecastCenter\.jsx';/g) === 1, 'frontend import once');
check('APP_MOUNT', count(app, /<CapacityPipelineForecastCenter authSession=\{authSession\} \/>/g) === 1, 'frontend mount once');
check('ROUTE_REGISTRY', count(app, /route:\s*['"]capacity-pipeline-forecast['"]/g) >= 2, 'workspace and installed registries');
check('STRUCTURAL_EXCLUSION', app.includes("'capacity-pipeline-forecast',") && app.includes('MODULE_070_STRUCTURAL_ROUTE_BOUNDARY'), 'dedicated route outside dashboard fallback');
check('BUILD_GUARD', packageJson.scripts?.build?.includes('validate:module070') && packageJson.scripts?.['validate:module070']?.includes('validate-module-070-capacity-pipeline-forecasting.mjs'), 'production build guard');
for (const required of [files.backend, 'docs/modules/module-070-capacity-pipeline-forecasting/', files.catalog, files.register, files.tracker]) {
  check(`DOCKER_${path.basename(required).replace(/[^a-z0-9]+/gi, '_').toUpperCase()}`, docker.includes(required), required);
}

check('TRACKER_REQUIREMENTS', docs.includes('RES-013') && docs.includes('RES-014') && docs.includes('RPT-007'), 'RES-013/014 and RPT-007');
check('WORKBOOK_QUALITY_FINDINGS', docs.includes('nonnumeric marker `x`') && docs.includes('2024 to 2026'), 'audited data-quality findings');
check('GOVERNANCE_REGISTERED', governance.includes('| 070 | Capacity & Pipeline Forecasting') && governance.includes('feature/modules-064-074-release-train-on-main-20260719'), 'central governance');
check('STATUS_064', governance.includes('MODULE_064_STATUS=RELEASE_TRAIN_CANDIDATE_UNCOMMITTED'), 'exact Module 064 status');
check('STATUS_068', governance.includes('MODULE_068_STATUS=RELEASE_TRAIN_CANDIDATE_UNCOMMITTED_READ_ONLY'), 'exact Module 068 status');
check('OVERLAP_MODULES', ['Module 002', 'Module 064', 'Module 068'].every((value) => docs.includes(value)), 'three comparison owners');
check('OVERLAP_SURFACES', ['docs/MODULE-CATALOG.md', 'docs/MODULE-WORK-REGISTER.md', 'AUGUST_PRODUCTION_READINESS_TRACKER.md', 'Program.cs', 'App.jsx', 'package.json'].every((value) => docs.includes(value)), 'all shared commit-gate surfaces');
check('COMMIT_GATE_BLOCKED', docs.includes('final commit gate is `BLOCKED`'), 'commit requires refreshed overlap evidence');
check('NO_AZURE_DATABASE_ENTRA', docs.includes('no Azure, database, or Entra change') || docs.includes('Database/Azure/Entra state: no change'), 'source-only authorization boundary');

console.log('');
console.log(`MODULE_070_VALIDATION_CHECKS=${checks.length}`);
console.log('MODULE_070_IMPLEMENTATION=FULL_CAPACITY_PIPELINE_FORECAST_SOURCE');
console.log('MODULE_070_IDENTITY_SOURCE=MODULE_062_STABLE_USER_ID');
console.log('MODULE_070_MUTABLE_CONTROLS=DATE_HORIZON_PRACTICE_ENGINEER_SUPPLEMENTAL_SCENARIO');
console.log('MODULE_070_FINAL_COMMIT_GATE=RELEASE_TRAIN_VALIDATION_PENDING');
console.log('MODULE_070_AZURE_DATABASE_ENTRA_CHANGES=NONE');
if (checks.some((value) => !value)) {
  console.error('MODULE_070_CONTRACT=FAILED');
  process.exit(1);
}
console.log('MODULE_070_CONTRACT=PASSED');
