import { readFile } from 'node:fs/promises';
import { resolve } from 'node:path';

const root = resolve(process.cwd(), '../../..');
const text = (path) => readFile(resolve(root, path), 'utf8');
const requireAll = (source, values, label) => {
  for (const value of values) {
    if (!source.includes(value)) throw new Error(`${label} missing contract: ${value}`);
  }
};

const paths = {
  ui: 'src/frontend/project-time-web/src/RoleAdminDirectoryPanel.jsx',
  backend: 'src/backend/ProjectTime.Api/Modules/ScopedRolePolicyModule.cs',
  writes: 'src/backend/ProjectTime.Api/Modules/ScopedRolePolicyWrites.cs',
  persistence: 'src/backend/ProjectTime.Api/Modules/ScopedRolePolicyPersistence.cs',
  support: 'src/backend/ProjectTime.Api/Modules/ScopedRolePolicySupport.cs',
  evaluator: 'src/backend/ProjectTime.Api/Modules/ScopedAuthorizationEvaluator.cs',
  rules: 'src/backend/ProjectTime.Api/Modules/ScopedRolePolicyRules.cs',
  css: 'src/frontend/project-time-web/src/scoped-role-policy-admin.css',
  project: 'src/backend/ProjectTime.Api/ProjectTime.Api.csproj'
};

const [ui, backend, writes, persistence, support, evaluator, rules, css, project] = await Promise.all(
  Object.values(paths).map(text)
);

requireAll(ui, [
  'Module 012',
  'Authoritative, versioned administration',
  "api('/api/role-policy/summary')",
  "api('/api/role-policy/catalog')",
  "api('/api/role-policy/versions')",
  "api('/api/role-policy/validate'",
  "api('/api/role-policy/publish'",
  '/restore',
  'Boolean(summary?.canWritePolicy) && !summary?.isViewAs',
  'Policy writes require an authenticated Super Administrator in their own session.',
  'Assigned users',
  'Granular actions and scopes',
  'Delegated authority',
  'Reason required',
  'Audit required',
  'Publish new policy version',
  'Restore as new version',
  'Existing authorization remains in effect'
], 'Module 012 UI');

requireAll(backend, [
  'app.MapGet("/api/role-policy/summary"',
  'app.MapGet("/api/role-policy/catalog"',
  'app.MapGet("/api/role-policy/roles/{roleCode}"',
  'app.MapPost("/api/role-policy/validate"',
  'app.MapPost("/api/role-policy/publish"',
  'app.MapPost("/api/role-policy/versions/{policyVersionId:guid}/restore"',
  'actor.IsSuperAdministrator && !actor.IsViewAs',
  'notSetBehavior = "legacy_fallback"',
  'nonBypassableSafetyControlsRemainSeparate = true'
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
  'var explicitDeny',
  'grant_effect = \'DENY\'',
  'LEGACY_FALLBACK',
  'case "CUSTOM_RULE"',
  'actor.IsViewAs && isWrite',
  'non-bypassable safety control',
  'ScopedAuthorizationDecision.Denied'
], 'Central scoped evaluator');

requireAll(rules, [
  'public sealed record ScopedAuthorizationDecision',
  'bool ExplicitDeny',
  'bool LegacyFallback',
  'bool IsViewAs',
  'NonBypassableActions'
], 'Scoped authorization result contract');

requireAll(css, [
  '.role-policy-admin',
  '.role-policy-grant',
  '.role-policy-publish',
  '.role-policy-history'
], 'Module 012 styling');

requireAll(project, [
  '<Compile Remove="Program.cs" />',
  'app.MapScopedRolePolicyEndpoints();',
  'Program.ScopedRbac.g.cs'
], 'Scoped RBAC API registration');

if (/fetch\([^)]*\/api\/role-policy\/(publish|validate|versions\/[^)]*restore)[\s\S]{0,200}method:\s*['"]GET['"]/m.test(ui)) {
  throw new Error('Module 012 write endpoints must not be called as GET.');
}

console.log('Module 012 authoritative scoped role administration contracts passed.');
