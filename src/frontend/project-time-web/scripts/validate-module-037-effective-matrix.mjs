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
  ui: 'src/frontend/project-time-web/src/RolesPermissionsMatrix.jsx',
  model: 'src/frontend/project-time-web/src/role-permission-matrix-model.js',
  css: 'src/frontend/project-time-web/src/role-permission-matrix-v2.css',
  backend: 'src/backend/ProjectTime.Api/Modules/ScopedRolePolicyModule.cs'
};

const [ui, model, css, backend] = await Promise.all([
  text(paths.ui),
  text(paths.model),
  text(paths.css),
  optionalText(paths.backend)
]);

requireAll(ui, [
  'Module 037',
  'Roles and Permissions Matrix',
  'Read-only confirmation',
  "api('/api/role-policy/matrix')",
  'data-read-only="true"',
  'Permission Matrix',
  'Role Reference',
  'Permission Levels',
  'Database modules',
  'Export matrix',
  'Policy evidence',
  'Refresh this page'
], 'Module 037 UI');

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
  "return 'Not Set'",
  "defaultScope: 'MANAGED_TEAM'",
  "defaultScope: 'ORGANIZATION'"
], 'Module 037 permission and role reference model');

requireAll(css, [
  '.role-permission-matrix-v2',
  '.rpm-table',
  '.rpm-level-no-access',
  '.rpm-level-view',
  '.rpm-level-full-control',
  '.rpm-reference-grid',
  '.rpm-level-reference'
], 'Module 037 styling');

if (backend) {
  requireAll(backend, [
    'app.MapGet("/api/role-policy/matrix"',
    'app.MapGet("/api/role-policy/explain"',
    'readOnly = true',
    'writeEndpoints = Array.Empty<string>()',
    'legacyAuthorizationPreserved = true',
    'No scoped decision exists for this action. Existing legacy authorization is preserved.'
  ], 'Module 037 backend');
} else {
  console.log('MODULE_037_BACKEND_CHECK=SKIPPED_MINIMAL_WEB_CONTEXT');
}

for (const forbidden of [
  "method: 'POST'",
  'method: "POST"',
  "method: 'PUT'",
  'method: "PUT"',
  "method: 'PATCH'",
  'method: "PATCH"',
  "method: 'DELETE'",
  'method: "DELETE"',
  '/api/role-policy/publish',
  '/api/role-policy/validate',
  '/api/role-policy/versions/'
]) {
  if (ui.includes(forbidden)) {
    throw new Error(`Module 037 must remain strictly read-only: ${forbidden}`);
  }
}

if (/<input[^>]+type=["']checkbox["']|<textarea|contentEditable|onSubmit=/i.test(ui)) {
  throw new Error('Module 037 contains an editing control or submission handler.');
}

console.log('Module 037 read-only visual permission matrix contracts passed.');
