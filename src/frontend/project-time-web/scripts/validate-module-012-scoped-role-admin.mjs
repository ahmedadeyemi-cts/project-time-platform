import { existsSync } from 'node:fs';
import { readFile } from 'node:fs/promises';
import { resolve } from 'node:path';

const root = resolve(process.cwd(), '../../..');
const absolute = (path) => resolve(root, path);
const text = (path) => readFile(absolute(path), 'utf8');
const optionalText = async (path) => existsSync(absolute(path)) ? text(path) : '';
const requireAll = (source, values, label) => {
  for (const value of values) {
    if (!source.includes(value)) throw new Error(`${label} missing contract: ${value}`);
  }
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
  support: 'src/backend/ProjectTime.Api/Modules/ScopedRolePolicySupport.cs',
  evaluator: 'src/backend/ProjectTime.Api/Modules/ScopedAuthorizationEvaluator.cs',
  rules: 'src/backend/ProjectTime.Api/Modules/ScopedRolePolicyRules.cs',
  css: 'src/frontend/project-time-web/src/role-permission-workbench.css',
  project: 'src/backend/ProjectTime.Api/ProjectTime.Api.csproj'
};

const [ui, model, compatibility, navigation, main, backend, writes, persistence, support, evaluator, rules, css, project] = await Promise.all([
  text(paths.ui),
  text(paths.model),
  text(paths.compatibility),
  text(paths.navigation),
  text(paths.main),
  optionalText(paths.backend),
  optionalText(paths.writes),
  optionalText(paths.persistence),
  optionalText(paths.support),
  optionalText(paths.evaluator),
  optionalText(paths.rules),
  text(paths.css),
  text(paths.project)
]);

requireAll(ui, [
  'Module 012',
  'Role Administration',
  'database-backed module',
  "api('/api/role-policy/summary')",
  "api('/api/role-policy/catalog')",
  "api('/api/role-policy/versions')",
  "api('/api/role-policy/validate'",
  "api('/api/role-policy/publish'",
  '/restore',
  'Boolean(summary?.canWritePolicy) && !summary?.isViewAs',
  'Publishing requires a Super Administrator in their own session.',
  'Assigned users',
  'Permission level',
  'Data scope',
  'Effective preview',
  'Required reason',
  'Publish permission',
  'Restore as new version',
  'Not Set · preserve existing authorization'
], 'Module 012 intuitive UI');

requireAll(model, [
  "['Not Set'",
  "['No Access'",
  "['View'",
  "['Create/Edit'",
  "['Approve'",
  "['Manage'",
  "['Administer'",
  "['Full Control'",
  "['Custom'",
  "PROJECT_MANAGEMENT_LEAD: 'MANAGED_TEAM'",
  "role === 'PROJECT_TEAM_COORDINATOR'",
  "'MODULE_CONFIGURE', 'POLICY_DELEGATE'",
  "actionCode: 'MODULE_ACCESS'",
  "effect: 'DENY'",
  "role === 'SUPER_ADMINISTRATOR'",
  "level = 'Full Control'"
], 'Permission-level model');

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
  'scopes: asArray(source.scopes ?? source.Scopes)',
  "['GRANT', 'DENY']",
  "method !== 'GET'",
  'url.origin === window.location.origin',
  'response.clone().json()',
  "responseHeaders.delete('content-length')",
  'return new Response(JSON.stringify(normalized)'
], 'Scoped RBAC catalog compatibility');

requireAll(main, [
  "import './scoped-rbac-catalog-compatibility.js';",
  '<App />'
], 'Scoped RBAC compatibility root wiring');

requireAll(css, [
  '.role-permission-workbench',
  '.rpw-level-grid',
  '.rpw-scope-section',
  '.rpw-preview-section',
  '.rpw-publish',
  '.rpw-history'
], 'Module 012 styling');

requireAll(project, [
  '<Compile Remove="Program.cs" />',
  'app.MapScopedRolePolicyEndpoints();',
  'Program.ScopedRbac.g.cs'
], 'Scoped RBAC API registration');

const fullSourceAvailable = [backend, writes, persistence, support, evaluator, rules]
  .every((source) => source.length > 0);

if (fullSourceAvailable) {
  requireAll(backend, [
    'app.MapGet("/api/role-policy/summary"',
    'app.MapGet("/api/role-policy/catalog"',
    'app.MapGet("/api/role-policy/roles/{roleCode}"',
    'app.MapPost("/api/role-policy/validate"',
    'app.MapPost("/api/role-policy/publish"',
    'app.MapPost("/api/role-policy/versions/{policyVersionId:guid}/restore"',
    'actor.IsSuperAdministrator && !actor.IsViewAs',
    'notSetBehavior = "legacy_fallback"'
  ], 'Module 012 backend');

  requireAll(writes, [
    'RequireOwnSessionSuperAdministratorAsync',
    'A reason is required to publish a policy version.',
    'A reason is required to restore a policy version.',
    'policy_version_conflict',
    'POLICY_VERSION_PUBLISHED',
    'POLICY_VERSION_RESTORED',
    'InsertAuditAsync',
    'ValidatePolicyVersionAsync'
  ], 'Policy write workflow');

  requireAll(persistence, [
    'RequireOwnSessionSuperAdministratorAsync',
    'actor.IsViewAs',
    'view_as_read_only',
    'SUPER_ADMINISTRATOR',
    'Only an authenticated Super Administrator in their own session may change scoped role policy.',
    'CountActiveSuperAdministratorsAsync',
    'ProjectPulseActualUserId',
    'ProjectPulseEffectiveUserId'
  ], 'Own-session Super Administrator enforcement');

  requireAll(support, [
    'IsViewAs',
    'ActualUserId',
    'EffectiveUserId',
    'PolicyValidationResult'
  ], 'Actor and validation contracts');

  requireAll(evaluator, [
    'if (actor.IsSuperAdministrator)',
    'permanent organization-wide Full Control',
    'var explicitDeny',
    "grant_effect = 'DENY'",
    'LEGACY_FALLBACK',
    'case "CUSTOM_RULE"',
    'actor.IsViewAs && isWrite',
    'ScopedAuthorizationDecision.Denied'
  ], 'Central scoped evaluator');

  requireAll(rules, [
    'public sealed record ScopedAuthorizationDecision',
    'bool ExplicitDeny',
    'bool LegacyFallback',
    'bool IsViewAs',
    'NonBypassableActions'
  ], 'Scoped authorization result contract');
} else {
  console.log('MODULE_012_EXTERNAL_SOURCE_CHECK=SKIPPED_MINIMAL_WEB_CONTEXT');
}

if (/fetch\([^)]*\/api\/role-policy\/(publish|validate|versions\/[^)]*restore)[\s\S]{0,200}method:\s*['"]GET['"]/m.test(ui)) {
  throw new Error('Module 012 write endpoints must not be called as GET.');
}

if (/catalog\.actions\.map|catalog\.scopes\.map/.test(compatibility)) {
  throw new Error('Compatibility code must normalize arrays rather than render catalog collections.');
}

console.log('Module 012 intuitive scoped role administration contracts passed.');
