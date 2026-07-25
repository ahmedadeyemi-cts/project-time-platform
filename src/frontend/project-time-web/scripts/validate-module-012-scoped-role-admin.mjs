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

const paths = {
  ui: 'src/frontend/project-time-web/src/RoleAdminDirectoryPanel.jsx',
  model: 'src/frontend/project-time-web/src/role-permission-model.js',
  compatibility: 'src/frontend/project-time-web/src/scoped-rbac-catalog-compatibility.js',
  navigation: 'src/frontend/project-time-web/src/module-availability-bridge.js',
  main: 'src/frontend/project-time-web/src/main.jsx',
  backend: 'src/backend/ProjectTime.Api/Modules/ScopedRolePolicyModule.cs',
  writes: 'src/backend/ProjectTime.Api/Modules/ScopedRolePolicyWrites.cs',
  persistence: 'src/backend/ProjectTime.Api/Modules/ScopedRolePolicyPersistence.cs',
  evaluator: 'src/backend/ProjectTime.Api/Modules/ScopedAuthorizationEvaluator.cs',
  css: 'src/frontend/project-time-web/src/role-permission-workbench.css',
  project: 'src/backend/ProjectTime.Api/ProjectTime.Api.csproj'
};

const [ui, model, compatibility, navigation, main, backend, writes, persistence, evaluator, css, project] = await Promise.all([
  text(paths.ui), text(paths.model), text(paths.compatibility), text(paths.navigation), text(paths.main),
  optionalText(paths.backend), optionalText(paths.writes), optionalText(paths.persistence), optionalText(paths.evaluator),
  text(paths.css), text(paths.project)
]);

requireAll(ui, [
  'Module 012',
  'Role Administration',
  'Select a role first',
  'Role purpose',
  'Access boundary',
  'Recommended starting point',
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
  'Policy version history',
  'Role-policy foundation is incomplete',
  'Expected ${REQUIRED_ROLE_COUNT} roles and ${REQUIRED_MODULE_COUNT} modules',
  "api('/api/role-policy/summary')",
  "api('/api/role-policy/catalog')",
  "api('/api/role-policy/versions')",
  "api('/api/role-policy/validate'",
  "api('/api/role-policy/publish'",
  'Project Team Coordinator · Time Steward',
  'Manage other users’ time without submitting for them',
  'Can reopen and unsubmit time for correction',
  'Cannot submit a timesheet on another user’s behalf',
  "actionCode === 'TIME_SUBMIT'",
  "PTC_DENIED_ACTIONS.has(action.actionCode)",
  "effect: 'DENY'"
], 'Module 012 role-first UI');

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

requireAll(css, [
  '.rpw-role-first',
  '.rpw-ptc-steward',
  '.rpw-permission-table',
  '.rpw-session-status',
  '.rpw-level-grid',
  '.rpw-scope-section',
  '.rpw-publish'
], 'Module 012 styling');

requireAll(navigation, [
  'installPermissionNavigationGuard',
  "actionCode || '').toUpperCase() === 'MODULE_ACCESS'",
  "grantEffect || '').toUpperCase() === 'DENY'",
  "window.location.hash = '#dashboard'",
  "actorRoles.has('SUPER_ADMINISTRATOR')"
], 'No Access navigation enforcement');

requireAll(compatibility, [
  "SCOPED_RBAC_CATALOG_PATH = '/api/role-policy/catalog'",
  'projectpulse-scoped-rbac-catalog-normalized',
  'actions: asArray(source.actions ?? source.Actions)',
  'scopes: asArray(source.scopes ?? source.Scopes)'
], 'Catalog compatibility');

requireAll(main, ["import './scoped-rbac-catalog-compatibility.js';", '<App />'], 'Application wiring');
requireAll(project, ['<Compile Remove="Program.cs" />', 'app.MapScopedRolePolicyEndpoints();', 'Program.ScopedRbac.g.cs'], 'Scoped RBAC registration');

if ([backend, writes, persistence, evaluator].every(Boolean)) {
  requireAll(backend, [
    'app.MapGet("/api/role-policy/summary"',
    'app.MapGet("/api/role-policy/catalog"',
    'app.MapPost("/api/role-policy/validate"',
    'app.MapPost("/api/role-policy/publish"',
    'actor.IsSuperAdministrator && !actor.IsViewAs'
  ], 'Module 012 backend');
  requireAll(writes, ['RequireOwnSessionSuperAdministratorAsync', 'POLICY_VERSION_PUBLISHED', 'POLICY_VERSION_RESTORED'], 'Immutable policy writes');
  requireAll(persistence, ['view_as_read_only', 'SUPER_ADMINISTRATOR', 'CountActiveSuperAdministratorsAsync'], 'Own-session enforcement');
  requireAll(evaluator, ['if (actor.IsSuperAdministrator)', 'permanent organization-wide Full Control', 'actor.IsViewAs && isWrite'], 'Central evaluator');
} else {
  console.log('MODULE_012_EXTERNAL_SOURCE_CHECK=SKIPPED_MINIMAL_WEB_CONTEXT');
}

if (/fetch\([^)]*\/api\/role-policy\/(publish|validate|versions\/[^)]*restore)[\s\S]{0,200}method:\s*['"]GET['"]/m.test(ui)) {
  throw new Error('Module 012 write endpoints must not be called as GET.');
}

console.log('Module 012 role-first intuitive permission administration contracts passed.');
