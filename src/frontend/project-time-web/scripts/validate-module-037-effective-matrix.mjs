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

const [ui, model, css, backend] = await Promise.all([
  text('src/frontend/project-time-web/src/RolesPermissionsMatrix.jsx'),
  text('src/frontend/project-time-web/src/role-permission-matrix-model.js'),
  text('src/frontend/project-time-web/src/role-permission-matrix-v2.css'),
  optionalText('src/backend/ProjectTime.Api/Modules/ScopedRolePolicyModule.cs')
]);

requireAll(ui, [
  'Module 037',
  'Roles and Permissions Matrix',
  'Read-only confirmation',
  "api('/api/role-policy/matrix')",
  "api('/api/role-policy/catalog')",
  'data-read-only="true"',
  'Permission Matrix',
  'Role Reference',
  'Permission Levels',
  'Database modules',
  'Canonical roles',
  'Export matrix',
  'The Module, Permission, and Description columns stay pinned',
  '<th>Module</th><th>Permission</th><th>Description</th>',
  '✓ Allow',
  '× Deny',
  '— Not set',
  'Permission code',
  'Policy evidence',
  'Project Team Coordinator',
  'Time-steward boundary',
  'does not submit their timesheets',
  'The published permission matrix is incomplete',
  'Expected ${REQUIRED_ROLE_COUNT} roles and ${REQUIRED_MODULE_COUNT} modules'
], 'Module 037 spreadsheet UI');

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
  'serve as the operational time steward',
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
  '.rpm-decision-allow',
  '.rpm-decision-deny',
  '.rpm-decision-not-set',
  '.rpm-reference-grid',
  '.ptc-reference',
  '.rpm-level-reference',
  '.rpm-detail-overlay'
], 'Module 037 styling');

if (backend) {
  requireAll(backend, [
    'app.MapGet("/api/role-policy/matrix"',
    'app.MapGet("/api/role-policy/explain"',
    'readOnly = true',
    'writeEndpoints = Array.Empty<string>()',
    'legacyAuthorizationPreserved = true'
  ], 'Module 037 backend');
} else {
  console.log('MODULE_037_BACKEND_CHECK=SKIPPED_MINIMAL_WEB_CONTEXT');
}

for (const forbidden of [
  "method: 'POST'", 'method: "POST"', "method: 'PUT'", 'method: "PUT"',
  "method: 'PATCH'", 'method: "PATCH"', "method: 'DELETE'", 'method: "DELETE"',
  '/api/role-policy/publish', '/api/role-policy/validate', '/api/role-policy/versions/'
]) {
  if (ui.includes(forbidden)) throw new Error(`Module 037 must remain strictly read-only: ${forbidden}`);
}

if (/<input[^>]+type=["']checkbox["']|<textarea|contentEditable|onSubmit=/i.test(ui)) {
  throw new Error('Module 037 contains an editing control or submission handler.');
}

console.log('Module 037 read-only spreadsheet permission matrix contracts passed.');
