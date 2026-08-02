import fs from 'node:fs';
import path from 'node:path';

const root = path.resolve(process.cwd(), '..', '..', '..');
const files = {
  backend: 'src/backend/ProjectTime.Api/Modules/QualificationsCertificationModule.cs',
  selfServiceBackend: 'src/backend/ProjectTime.Api/Modules/QualificationsCertificationSelfServiceModule.cs',
  registration: 'src/backend/ProjectTime.Api/Modules/ModuleAvailabilityOverridesModule.cs',
  frontend: 'src/frontend/project-time-web/src/QualificationsCertificationCenter.jsx',
  stylesheet: 'src/frontend/project-time-web/src/qualifications-certification-center.css',
  selfServiceStylesheet: 'src/frontend/project-time-web/src/qualifications-self-service.css',
  app: 'src/frontend/project-time-web/src/App.jsx',
  package: 'src/frontend/project-time-web/package.json',
  migration: 'database/migrations/062_project_management_billing_role_access_repair.sql',
  rollback: 'database/rollback/062_project_management_billing_role_access_repair_rollback.sql',
  migrationTest: 'tests/test-project-management-billing-role-access-migration-062.sh',
  readme: 'docs/modules/module-069-qualifications-certifications/README.md',
  api: 'docs/modules/module-069-qualifications-certifications/API-CONTRACT.md',
  security: 'docs/modules/module-069-qualifications-certifications/SECURITY-AND-OPERATIONS.md'
};

const absolute = (file) => path.join(root, file);
const exists = (file) => fs.existsSync(absolute(file));
const text = (file) => fs.readFileSync(absolute(file), 'utf8');
const optional = (file) => exists(file) ? text(file) : '';
const count = (value, pattern) => [...value.matchAll(pattern)].length;
const checks = [];

function check(name, condition, evidence) {
  checks.push(Boolean(condition));
  console.log(`MODULE_069_${name}=${condition ? 'PASSED' : 'FAILED'} — ${evidence}`);
}

for (const required of [
  files.backend,
  files.frontend,
  files.stylesheet,
  files.selfServiceStylesheet,
  files.app,
  files.package,
  files.readme,
  files.api,
  files.security
]) {
  check(`FILE_${path.basename(required).replace(/[^a-z0-9]+/gi, '_').toUpperCase()}`, exists(required), required);
}

const backend = text(files.backend);
const frontend = text(files.frontend);
const stylesheet = text(files.stylesheet);
const selfServiceStylesheet = text(files.selfServiceStylesheet);
const app = text(files.app);
const packageJson = JSON.parse(text(files.package));
const docs = [files.readme, files.api, files.security].map(text).join('\n');

// The cross-user matrix remains a read-only, server-scoped source.
check('CORE_MAP_METHOD', backend.includes('MapQualificationsCertificationEndpoints'), 'read-model endpoint registration');
check('CORE_GET_CAPABILITIES', backend.includes('/api/qualifications/capabilities'), 'capability contract');
check('CORE_GET_MATRIX', backend.includes('/api/qualifications/matrix'), 'matrix contract');
check('CORE_TYPED_HANDLERS', count(backend, /Func<[^>]*Task<IResult>>/g) >= 1 && backend.includes('Task<IResult>>)GetMatrixAsync'), 'typed minimal API handlers');
check('CORE_EFFECTIVE_IDENTITY', backend.includes('ProjectPulseEffectiveUserId'), 'effective identity scope');
check('CORE_SERVER_SCOPE', backend.includes('broad_scope') && backend.includes('team_scope') && backend.includes('u.user_id = @user_id'), 'organization/team/self scope');
check('CORE_PARAMETERIZED_FILTERS', backend.includes('command.Parameters.AddWithValue("search"') && backend.includes('command.Parameters.AddWithValue("category"'), 'parameterized matrix filters');
check('CORE_EXPIRATION_CALCULATION', backend.includes('CURRENT_DATE + 90') && backend.includes("'expiring'") && backend.includes("'expired'"), '90-day lifecycle');
check('CORE_READ_ONLY', !/Map(?:Post|Put|Patch|Delete)\s*\(/.test(backend), 'cross-user matrix exposes no mutation route');
check('CORE_NO_MUTATING_SQL', !/\b(?:INSERT|UPDATE|DELETE|ALTER|DROP|CREATE|TRUNCATE)\s+(?:INTO|TABLE|FROM|VIEW|INDEX|SCHEMA)\b/i.test(backend), 'cross-user matrix executes SELECT only');
check('CORE_SANITIZED_FAILURE', backend.includes('Qualifications matrix unavailable') && !backend.includes('detail: exception.Message'), 'raw exception excluded');

const selfServiceAvailable = exists(files.selfServiceBackend);
if (selfServiceAvailable) {
  const selfService = text(files.selfServiceBackend);
  const registration = text(files.registration);
  const migration = text(files.migration);
  const rollback = text(files.rollback);
  const migrationTest = text(files.migrationTest);

  check('SELF_SERVICE_MAP_METHOD', selfService.includes('MapQualificationsCertificationSelfServiceEndpoints'), 'isolated self-service registration');
  check('SELF_SERVICE_ENDPOINTS', [
    '/api/qualifications/self-service',
    'MapPost(',
    'MapPut('
  ].every((value) => selfService.includes(value)), 'GET, POST, and PUT contracts');
  check('SELF_SERVICE_NO_DELETE', !/MapDelete\s*\(/.test(selfService), 'no delete endpoint');
  check('SELF_SERVICE_VIEW_AS_BLOCK', selfService.includes('view_as_read_only') && selfService.includes('ProjectPulseActualSessionAuthority.IsViewAs(context)'), 'View-As write protection');
  check('SELF_SERVICE_OWN_SESSION', selfService.includes('access.ActualUserId != access.EffectiveUserId') && selfService.includes('own_session_required'), 'actual/effective identity match');
  check('SELF_SERVICE_OWN_ROW_PREDICATE', selfService.includes('AND user_id = @user_id') && selfService.includes('WHERE user_id = @user_id'), 'own-row SQL predicates');
  check('SELF_SERVICE_SERVER_BINDS_USER', !selfService.includes('request.UserId') && selfService.includes('access.Context.ActualUserId'), 'client cannot choose another user');
  check('SELF_SERVICE_INPUT_VALIDATION', selfService.includes('YearsOfExperience is < 0 or > 99.99m') && selfService.includes('EffectiveEndDate < start'), 'experience and date validation');
  check('SELF_SERVICE_AUDIT', selfService.includes('SecurityDiagnosticsOperations.WriteAuditAsync') && selfService.includes('selfService = true'), 'sanitized audit evidence');
  check('SELF_SERVICE_PM_ROLE_ALIASES', [
    'PROJECT_MANAGER',
    'PROJECT_MANAGEMENT',
    'PROJECT_MANAGEMENT_LEAD',
    'PROJECT_MANAGEMENT_TEAM_LEAD',
    'PM_TEAM_LEAD'
  ].every((value) => selfService.includes(`"${value}"`)), 'all Project Management aliases');
  check('SELF_SERVICE_REGISTERED', registration.includes('app.MapQualificationsCertificationSelfServiceEndpoints();'), 'endpoint wiring');

  check('MIGRATION_062_PERMISSIONS', migration.includes('VIEW_QUALIFICATIONS_069') && migration.includes('MANAGE_OWN_QUALIFICATIONS_069'), 'Module 069 permissions');
  check('MIGRATION_062_PM_SCOPE', migration.includes("'VIEW_TIME_ENTRY'") && migration.includes("'PROJECT_TIME_APPROVAL'") && migration.includes("'VIEW_HOLIDAYS'") && migration.includes("'MANAGE_EXPENSES'"), 'PM operational permission repair');
  check('MIGRATION_062_BILLING_AUDIT_EXCLUSION', migration.includes("upper(role.role_code) IN ('BILLING', 'ACCOUNTING_BILLING', 'FINANCE')") && migration.includes("upper(COALESCE(permission.module_code, '')) = '008'"), 'Billing Module 008 exclusion');
  check('ROLLBACK_062_SCOPED', rollback.includes('role_access_repair_062_permission_grants') && rollback.includes('role_access_repair_062_permission_removals'), 'reversible evidence-based rollback');
  check('MIGRATION_062_TEST', migrationTest.includes('PROJECT_MANAGEMENT_BILLING_ROLE_ACCESS_MIGRATION_062=PASS') && migrationTest.includes('billing_audit_removed') && migrationTest.includes('accounting_audit_preserved'), 'apply, rollback, and reapply coverage');
} else {
  console.log('MODULE_069_SELF_SERVICE_BACKEND_CHECK=SKIPPED_MINIMAL_WEB_CONTEXT');
  console.log('MODULE_069_MIGRATION_062_CHECK=SKIPPED_MINIMAL_WEB_CONTEXT');
}

check('FRONTEND_MARKERS', frontend.includes('data-module="069"') && frontend.includes("'self-service' : 'read-only-matrix'"), 'matrix and self-service modes');
check('FRONTEND_READ_ENDPOINTS', frontend.includes('/api/qualifications/capabilities') && frontend.includes('/api/qualifications/matrix'), 'matrix consumers');
check('FRONTEND_SELF_SERVICE_ENDPOINTS', frontend.includes('/api/qualifications/self-service') && frontend.includes("method: form.qualificationId ? 'PUT' : 'POST'"), 'own-profile POST and PUT');
check('FRONTEND_VIEW_AS_BOUNDARY', frontend.includes('Exit Administrator View-As to change qualification records.'), 'read-only preview guidance');
check('FRONTEND_FORM_FIELDS', ['Category', 'Qualification or certification', 'Years of experience', 'Expiration or end date'].every((value) => frontend.includes(value)), 'self-service fields');
check('FRONTEND_FILTERS', frontend.includes('All categories') && frontend.includes('Expiring') && frontend.includes('Unrecorded'), 'matrix filters');
check('FRONTEND_IDENTITY', frontend.includes('row.displayName') && frontend.includes('row.email') && frontend.includes('row.userId'), 'identity-backed rows');
check('FRONTEND_NO_DELETE', !/method:\s*['"]DELETE['"]/.test(frontend), 'no delete request');
check('SCOPED_STYLES', !/(^|\n)\s*(?:body|html|\.panel|\.app-shell|\.sidebar)\s*\{/m.test(stylesheet + '\n' + selfServiceStylesheet), 'no global shell selector');

check('APP_IMPORT', count(app, /import QualificationsCertificationCenter from '\.\/QualificationsCertificationCenter\.jsx';/g) === 1, 'frontend import once');
check('APP_MOUNT', count(app, /<QualificationsCertificationCenter authSession=\{authSession\} \/>/g) === 1, 'frontend mount once');
check('ROUTE_REGISTRY', count(app, /route:\s*['"]qualifications-certifications['"]/g) >= 2, 'workspace and installed registries');
check('BUILD_GUARD', packageJson.scripts?.build?.includes('validate:module069') && packageJson.scripts?.['validate:module069']?.includes('validate-module-069-qualifications.mjs'), 'production build guard');

check('DOC_SELF_SERVICE', docs.includes('self-service') && docs.includes('actual authenticated user') && docs.includes('View-As'), 'self-service ownership and View-As boundary');
check('DOC_NO_DELETE', docs.includes('no delete endpoint') || docs.includes('There is no delete endpoint') || docs.includes('There is no delete route'), 'no-delete boundary');
check('DOC_NO_EXTERNAL_OPERATION', docs.includes('No Azure') && docs.includes('external provider'), 'provider and infrastructure isolation');

console.log('');
console.log(`MODULE_069_VALIDATION_CHECKS=${checks.length}`);
console.log('MODULE_069_IMPLEMENTATION=ROLE_SCOPED_MATRIX_WITH_GOVERNED_OWN_PROFILE_SELF_SERVICE');
console.log('MODULE_069_VIEW_AS_MUTATION_AUTHORITY=NONE');
console.log('MODULE_069_EXTERNAL_SYSTEM_CHANGES=NONE');
if (checks.some((value) => !value)) {
  console.error('MODULE_069_CONTRACT=FAILED');
  process.exit(1);
}
console.log('MODULE_069_CONTRACT=PASSED');
