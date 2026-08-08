import { existsSync } from 'node:fs';
import { readFile } from 'node:fs/promises';
import { resolve } from 'node:path';

const root = resolve(process.cwd(), '../../..');
const absolute = (path) => resolve(root, path);
const text = (path) => readFile(absolute(path), 'utf8');
const optionalText = async (path) => existsSync(absolute(path)) ? text(path) : '';
const requireAll = (source, values, label) => {
  for (const value of values) if (!source.includes(value)) throw new Error(`${label} missing contract: ${value}`);
};
const rejectAll = (source, values, label) => {
  for (const value of values) if (source.includes(value)) throw new Error(`${label} contains retired contract: ${value}`);
};
const requireOneOf = (source, values, label) => {
  if (!values.some((value) => source.includes(value))) {
    throw new Error(`${label} missing equivalent contract: ${values.join(' OR ')}`);
  }
};

const paths = {
  ui: 'src/frontend/project-time-web/src/RoleAdminDirectoryPanel.jsx',
  model: 'src/frontend/project-time-web/src/role-permission-model.js',
  compatibility: 'src/frontend/project-time-web/src/scoped-rbac-catalog-compatibility.js',
  navigation: 'src/frontend/project-time-web/src/module-availability-bridge.js',
  main: 'src/frontend/project-time-web/src/main.jsx',
  backend: 'src/backend/ProjectTime.Api/Modules/ScopedRolePolicyModule.cs',
  dynamicBackend: 'src/backend/ProjectTime.Api/Modules/DynamicRbacAdministrationModule.cs',
  registrar: 'src/backend/ProjectTime.Api/Modules/GlobalMailConfigurationModule.cs',
  writes: 'src/backend/ProjectTime.Api/Modules/ScopedRolePolicyWrites.cs',
  persistence: 'src/backend/ProjectTime.Api/Modules/ScopedRolePolicyPersistence.cs',
  evaluator: 'src/backend/ProjectTime.Api/Modules/ScopedAuthorizationEvaluator.cs',
  css: 'src/frontend/project-time-web/src/role-permission-workbench.css',
  dynamicCss: 'src/frontend/project-time-web/src/dynamic-rbac-administration.css',
  project: 'src/backend/ProjectTime.Api/ProjectTime.Api.csproj'
};

const [
  ui, model, compatibility, navigation, main, backend, dynamicBackend, registrar,
  writes, persistence, evaluator, css, dynamicCss, project
] = await Promise.all([
  text(paths.ui), text(paths.model), text(paths.compatibility), text(paths.navigation), text(paths.main),
  optionalText(paths.backend), optionalText(paths.dynamicBackend), optionalText(paths.registrar), optionalText(paths.writes),
  optionalText(paths.persistence), optionalText(paths.evaluator), text(paths.css), text(paths.dynamicCss), optionalText(paths.project)
]);

const navigationPolicy = await text('src/frontend/project-time-web/src/module-navigation-access-policy.js');

requireAll(ui, [
  'Module 012',
  'Role-Based Access Control',
  'Assign users to roles',
  'Active roles',
  'Active modules',
  'Super Administrator',
  'Permanent organization-wide Full Control',
  'New modules',
  'Default to No Access',
  'Role membership',
  'explicit denials take precedence',
  'Role permissions',
  'Role members',
  'Module catalog',
  'Policy history',
  'Role purpose',
  'Access boundary',
  'Detailed permissions',
  'What it allows',
  'Data scope',
  'Safeguards',
  'Current policy session',
  'Effective roles',
  'Publishing',
  'Assigned users',
  'Validate changes',
  'Publish new policy version',
  "api('/api/rbac/v1/bootstrap')",
  'api(`/api/rbac/v1/roles/',
  "api('/api/rbac/v1/policies/validate'",
  "api('/api/rbac/v1/policies/publish'",
  '/api/rbac/v1/role-memberships/',
  "api('/api/rbac/v1/modules/register'",
  '/api/rbac/v1/modules/${encodeURIComponent(module.moduleCode)}/',
  "actionCode === 'TIME_SUBMIT'",
  'PTC_DENIED_ACTIONS.has(action.actionCode)',
  "effect: 'DENY'"
], 'Module 012 dynamic RBAC UI');

rejectAll(ui, [
  'const REQUIRED_ROLE_COUNT',
  'const REQUIRED_MODULE_COUNT',
  'Expected ${REQUIRED_ROLE_COUNT} roles and ${REQUIRED_MODULE_COUNT} modules',
  'Role-policy foundation is incomplete'
], 'Module 012 fixed-count gate');

requireAll(model, [
  'ROLE_GUIDANCE',
  'ACTION_GUIDANCE',
  'PTC_TIME_STEWARD_ACTIONS',
  "PROJECT_MANAGEMENT_LEAD: 'MANAGED_TEAM'",
  "PROJECT_TEAM_COORDINATOR: 'ORGANIZATION'",
  "'TIME_VIEW_ON_BEHALF'",
  "'TIME_UNSUBMIT'",
  "'TIME_CORRECT_ON_BEHALF'",
  "'TIME_REASSIGN'",
  "'TIME_DELETE_ON_BEHALF'",
  "'TIME_TASK_CREATE'",
  "'TIME_TASK_ASSIGN'",
  "'TIME_SUBMIT'",
  "'TIME_DELETE_PERMANENT'",
  'operationalTimeSteward',
  "role === 'SUPER_ADMINISTRATOR'",
  "level = 'Full Control'",
  'session?.sessionToken || session?.token || session?.accessToken',
  "credentials: 'include'"
], 'Permission model');

if (dynamicBackend) {
  requireAll(dynamicBackend, [
    'DynamicRbacContractVersion',
    'MapDynamicRbacAdministrationEndpoints',
    '"/api/rbac/v1/bootstrap"',
    '"/api/rbac/v1/matrix"',
    '"/api/rbac/v1/roles/{roleCode}"',
    '"/api/rbac/v1/users"',
    '"/api/rbac/v1/modules"',
    '"/api/rbac/v1/policies/validate"',
    '"/api/rbac/v1/policies/publish"',
    '"/api/rbac/v1/role-memberships/assign"',
    '"/api/rbac/v1/role-memberships/remove"',
    '"/api/rbac/v1/modules/register"',
    'fixedModuleCountRequired = false',
    'moduleCatalogMode = "database_dynamic"',
    'permanentFullControl = true',
    'reducible = false',
    'NO_ACCESS_FOR_NON_SUPER_ADMINISTRATORS',
    'super_administrator_self_lockout_blocked',
    'final_super_administrator_removal_blocked',
    'RequireOwnSessionSuperAdministratorAsync',
    'ROLE_MEMBERSHIP_ASSIGNED',
    'ROLE_MEMBERSHIP_REMOVED',
    'MODULE_RETIRED',
    'ProtectedGovernanceModules'
  ], 'Dynamic RBAC backend');
  rejectAll(dynamicBackend, [
    'modules.Count != 70',
    'moduleCount == 70',
    'Expected 12 roles and 70 modules'
  ], 'Dynamic RBAC backend fixed-count gate');
} else {
  console.log('MODULE_012_DYNAMIC_BACKEND_CHECK=SKIPPED_MINIMAL_WEB_CONTEXT');
}

if (registrar) {
  requireAll(registrar, ['app.MapDynamicRbacAdministrationEndpoints();'], 'Dynamic RBAC endpoint registration');
} else {
  console.log('MODULE_012_DYNAMIC_REGISTRAR_CHECK=SKIPPED_MINIMAL_WEB_CONTEXT');
}
if (project) {
  requireAll(project, [
    '<Compile Remove="Modules/DynamicRbacAdministrationModule.cs" />',
    '<DynamicRbacGeneratedModule>',
    '<Compile Include="$(DynamicRbacGeneratedModule)" />',
    'app.MapScopedRolePolicyEndpoints();'
  ], 'Dynamic RBAC compile and scoped RBAC registration');
} else {
  console.log('MODULE_012_DYNAMIC_PROJECT_CHECK=SKIPPED_MINIMAL_WEB_CONTEXT');
}

requireAll(css, [
  '.rpw-role-first',
  '.rpw-permission-table',
  '.rpw-session-status',
  '.rpw-level-grid',
  '.rpw-scope-section',
  '.rpw-publish'
], 'Module 012 base styling');
requireAll(dynamicCss, [
  '.dynamic-rbac-admin',
  '.dynamic-rbac-invariant',
  '.dynamic-rbac-tabs',
  '.dynamic-rbac-members',
  '.dynamic-rbac-modules'
], 'Module 012 dynamic styling');

requireAll(navigation, [
  'installPermissionNavigationGuard',
  "nativeFetch('/api/rbac/v1/bootstrap'",
  "nativeFetch('/api/rbac/v1/matrix'",
  "nativeFetch('/api/rbac/v1/modules?includeInactive=true'",
  'resolveModuleNavigationAccess',
  'const sequence = ++refreshSequence;',
  'sequence !== refreshSequence',
  "window.addEventListener('hashchange', applyVisibility);",
  'inactiveDynamicModuleNumbers',
  'legacyFallbackModuleNumbers',
  'unregisteredLegacyModuleNumbers',
  "window.location.hash = '#dashboard'",
  "const SUPER_ADMINISTRATOR_ROLE_CODES = new Set(['SUPER_ADMINISTRATOR', 'ADMINISTRATOR']);",
  'const actualSuperAdministrator = !viewAs',
  'actorRoles.some((roleCode) => SUPER_ADMINISTRATOR_ROLE_CODES.has(roleCode))',
  'if (!actualSuperAdministrator) {',
  'permanentFullControl: actualSuperAdministrator',
  "headers['X-ProjectPulse-View-As-User']"
], 'No Access navigation enforcement');

rejectAll(navigation, [
  '!activeModuleNumbers.has(number)',
  "window.addEventListener('hashchange', () => {\n    applyVisibility();\n    void refreshPermissions();"
], 'View-As navigation false-deny and refresh-race contract');

requireAll(navigationPolicy, [
  "canonicalRoleCode(grant?.actionCode ?? grant?.ActionCode) !== 'MODULE_ACCESS'",
  "effect === 'DENY'",
  'explicitDeniedModuleNumbers.add(moduleCode)',
  'inactiveDynamicModuleNumbers',
  'legacyFallbackModuleNumbers',
  'unregisteredLegacyModuleNumbers',
  'actualSessionPermanentFullControl'
], 'Shared View-As module navigation policy');

requireAll(compatibility, [
  "SCOPED_RBAC_CATALOG_PATH = '/api/role-policy/catalog'",
  'projectpulse-scoped-rbac-catalog-normalized',
  'actions: asArray(source.actions ?? source.Actions)',
  'scopes: asArray(source.scopes ?? source.Scopes)'
], 'Legacy catalog compatibility');
requireAll(main, ["import './scoped-rbac-catalog-compatibility.js';", '<App />'], 'Application wiring');

if ([backend, writes, persistence, evaluator].every(Boolean)) {
  requireAll(backend, [
    'app.MapGet("/api/role-policy/summary"',
    'app.MapGet("/api/role-policy/catalog"',
    'app.MapPost("/api/role-policy/validate"',
    'app.MapPost("/api/role-policy/publish"',
    'actor.IsSuperAdministrator && !actor.IsViewAs'
  ], 'Legacy Module 012 compatibility backend');
  requireAll(writes, ['RequireOwnSessionSuperAdministratorAsync', 'POLICY_VERSION_PUBLISHED', 'POLICY_VERSION_RESTORED'], 'Immutable policy writes');
  requireAll(persistence, ['view_as_read_only', 'SUPER_ADMINISTRATOR', 'CountActiveSuperAdministratorsAsync'], 'Own-session enforcement');
  requireAll(evaluator, ['if (actor.IsSuperAdministrator)', 'permanent organization-wide Full Control', 'actor.IsViewAs && isWrite'], 'Central evaluator');
} else {
  console.log('MODULE_012_LEGACY_BACKEND_CHECKS=SKIPPED_MINIMAL_WEB_CONTEXT');
}

if (/fetch\([^)]*\/api\/rbac\/v1\/(policies|role-memberships|modules)[^)]*[\s\S]{0,200}method:\s*['"]GET['"]/m.test(ui)) {
  throw new Error('Dynamic RBAC write endpoints must not be called as GET.');
}

console.log('MODULE_012_DYNAMIC_RBAC_ADMINISTRATION=PASS mode=database-dynamic superAdmin=permanent-full-control roleMemberships=audited moduleCatalog=flexible contextAwareWebBuild=true');
