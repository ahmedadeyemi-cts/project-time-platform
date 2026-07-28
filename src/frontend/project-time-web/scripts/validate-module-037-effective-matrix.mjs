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

const [ui, model, css, legacyBackend, dynamicBackend] = await Promise.all([
  text('src/frontend/project-time-web/src/RolesPermissionsMatrix.jsx'),
  text('src/frontend/project-time-web/src/role-permission-matrix-model.js'),
  text('src/frontend/project-time-web/src/role-permission-matrix-v2.css'),
  optionalText('src/backend/ProjectTime.Api/Modules/ScopedRolePolicyModule.cs'),
  text('src/backend/ProjectTime.Api/Modules/DynamicRbacAdministrationModule.cs')
]);

requireAll(ui, [
  'Module 037',
  'Roles and Permissions Matrix',
  'Read-only confirmation',
  "api('/api/rbac/v1/matrix')",
  'data-rbac-contract="projectpulse-rbac-v1"',
  'data-read-only="true"',
  'no fixed module count is required',
  'Super Administrator is always Full Control',
  'Permission Matrix',
  'Role Reference',
  'Permission Levels',
  'Active modules',
  'Active roles',
  'Configured pairs',
  'Unconfigured pairs',
  'Dynamic database catalog',
  'No 70-module requirement',
  'Export matrix',
  '<th>Page</th><th>Permission</th><th>Description</th>',
  "if (state === 'ALLOW') return 'rpm-decision rpm-decision-allow'",
  "if (state === 'DENY') return 'rpm-decision rpm-decision-deny'",
  "return 'rpm-decision rpm-decision-not-set'",
  "decision.state === 'ALLOW' ? 'Allow'",
  "decision.state === 'DENY' ? 'No Access' : 'Not Set'",
  'Permission explanation',
  'Permission code',
  'Last modified',
  'policy evidence',
  "role.roleCode === 'PROJECT_TEAM_COORDINATOR' ? 'ptc-reference' : ''",
  'projectpulse-dynamic-rbac-matrix.csv'
], 'Module 037 dynamic spreadsheet UI');

rejectAll(ui, [
  'const REQUIRED_ROLE_COUNT',
  'const REQUIRED_MODULE_COUNT',
  'Expected ${REQUIRED_ROLE_COUNT} roles and ${REQUIRED_MODULE_COUNT} modules',
  'The published permission matrix is incomplete',
  "api('/api/role-policy/matrix')",
  "api('/api/role-policy/catalog')"
], 'Module 037 retired fixed-count and legacy transport');

requireAll(model, [
  'PERMISSION_LEVELS',
  'ROLE_REFERENCE',
  "code: 'Not Set'",
  "code: 'No Access'",
  "code: 'View'",
  "code: 'Create/Edit'",
  "code: 'Approve'",
  "code: 'Manage'",
  "code: 'Administer'",
  "code: 'Full Control'",
  "code: 'Custom'",
  "roleCode === 'SUPER_ADMINISTRATOR'",
  "return 'Full Control'",
  "grant.inherited || grant.actionCode === 'LEGACY_FALLBACK'",
  "defaultScope: 'MANAGED_TEAM'",
  "defaultScope: 'ORGANIZATION'",
  'Serve as the operational time steward',
  'does not submit another user’s timesheet',
  'session?.sessionToken || session?.token || session?.accessToken'
], 'Module 037 role reference model');

requireAll(css, [
  '.role-permission-matrix-v2',
  '.rpm-permission-table-wrap',
  '.rpm-permission-table',
  '.rpm-permission-table th:nth-child(1)',
  '.rpm-permission-table th:nth-child(2)',
  '.rpm-permission-table th:nth-child(3)',
  'position: sticky',
  '.rpm-role-heading',
  '.rpm-decision-allow button',
  '.rpm-decision-deny button',
  '.rpm-decision-not-set button',
  '.rpm-reference-grid',
  '.ptc-reference',
  '.rpm-level-reference',
  '.rpm-detail-overlay'
], 'Module 037 styling');

requireAll(dynamicBackend, [
  '"/api/rbac/v1/matrix"',
  'DynamicRbacMatrixAsync',
  'fixedModuleCountRequired = false',
  'readOnly = true',
  'writeEndpoints = Array.Empty<string>()',
  'superAdministratorFullControl = true',
  'legacyFallback = unconfigured',
  'No explicit RBAC decision exists',
  'roles.Count == 0 || modules.Count == 0 || version is null'
], 'Dynamic Module 037 backend');
rejectAll(dynamicBackend, [
  'modules.Count != 70',
  'moduleCount == 70',
  'Expected 12 roles and 70 modules'
], 'Dynamic Module 037 backend fixed-count gate');

if (legacyBackend) {
  requireAll(legacyBackend, [
    'app.MapGet("/api/role-policy/matrix"',
    'app.MapGet("/api/role-policy/explain"',
    'readOnly = true',
    'writeEndpoints = Array.Empty<string>()',
    'legacyAuthorizationPreserved = true'
  ], 'Legacy Module 037 compatibility backend');
}

for (const forbidden of [
  "method: 'POST'", 'method: "POST"', "method: 'PUT'", 'method: "PUT"',
  "method: 'PATCH'", 'method: "PATCH"', "method: 'DELETE'", 'method: "DELETE"',
  '/api/rbac/v1/policies/publish', '/api/rbac/v1/policies/validate',
  '/api/rbac/v1/role-memberships/', '/api/rbac/v1/modules/register'
]) {
  if (ui.includes(forbidden)) throw new Error(`Module 037 must remain strictly read-only: ${forbidden}`);
}

if (/<input[^>]+type=["']checkbox["']|<textarea|contentEditable|onSubmit=/i.test(ui)) {
  throw new Error('Module 037 contains an editing control or submission handler.');
}

console.log('MODULE_037_DYNAMIC_PERMISSION_MATRIX=PASS mode=database-dynamic fixedModuleCount=false readOnly=true decisionStyles=aligned');
