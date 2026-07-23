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
  ui: 'src/frontend/project-time-web/src/RolesPermissionsMatrix.jsx',
  css: 'src/frontend/project-time-web/src/scoped-role-policy-matrix.css',
  backend: 'src/backend/ProjectTime.Api/Modules/ScopedRolePolicyModule.cs'
};

const [ui, css, backend] = await Promise.all(Object.values(paths).map(text));

requireAll(ui, [
  'Module 037',
  'Strictly read-only representation',
  "api('/api/role-policy/matrix')",
  '/api/role-policy/explain?',
  "method: 'GET'",
  'data-read-only="true"',
  'This module has no permission-editing controls or write endpoint.',
  'Export CSV',
  'Permission / action',
  'Explicit denials',
  'Legacy fallbacks',
  'Delegated grants',
  'Reason required',
  'Audit required',
  'Policy evidence',
  'Last modified by'
], 'Module 037 UI');

requireAll(backend, [
  'app.MapGet("/api/role-policy/matrix"',
  'app.MapGet("/api/role-policy/explain"',
  'readOnly = true',
  'writeEndpoints = Array.Empty<string>()',
  'legacyAuthorizationPreserved = true',
  'No scoped decision exists for this action. Existing legacy authorization is preserved.'
], 'Module 037 backend');

requireAll(css, [
  '.roles-matrix-cell-button',
  '[data-read-only="true"]'
], 'Module 037 styling');

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

console.log('Module 037 read-only effective scoped matrix contracts passed.');
