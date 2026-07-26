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
const rejectAll = (source, values, label) => {
  for (const value of values) {
    if (source.includes(value)) throw new Error(`${label} contains forbidden contract: ${value}`);
  }
};

const [jsonResponse, authoritative, bridge, roleModel, matrixModel, portal, catalog, gate, recovery, css, main, backend, ptcBackend, project] = await Promise.all([
  text('src/frontend/project-time-web/src/api-json-response.js'),
  text('src/frontend/project-time-web/src/projectpulse-authoritative-api.js'),
  text('src/frontend/project-time-web/src/runtime-data-compatibility.js'),
  text('src/frontend/project-time-web/src/role-permission-model.js'),
  text('src/frontend/project-time-web/src/role-permission-matrix-model.js'),
  text('src/frontend/project-time-web/src/module001/PtcTimesheetManagementPortal.jsx'),
  text('src/frontend/project-time-web/src/module001/PtcRuntimeTaskCatalog.jsx'),
  text('src/frontend/project-time-web/src/module001/PtcTimeStewardGate.jsx'),
  text('src/frontend/project-time-web/src/module001/Module001ActiveTimerRecoveryPortal.jsx'),
  text('src/frontend/project-time-web/src/module001/ptc-runtime-task-catalog.css'),
  text('src/frontend/project-time-web/src/main.jsx'),
  optionalText('src/backend/ProjectTime.Api/Modules/RuntimeDataCompatibilityModule.cs'),
  optionalText('src/backend/ProjectTime.Api/Modules/Module001PtcTimesheetManagement.cs'),
  optionalText('src/backend/ProjectTime.Api/ProjectTime.Api.csproj')
]);

requireAll(jsonResponse, [
  'unwrapApiPayload',
  'readApiJson',
  "'data', 'Data', 'result', 'Result', 'value', 'Value', 'payload', 'Payload'",
  'returned non-JSON content instead of ProjectPulse API data',
  'responsePreview'
], 'Strict API JSON response handling');

requireAll(authoritative, [
  "const DIAGNOSTIC_MARKER = 'projectpulse-authoritative-xhr-v1'",
  'new XMLHttpRequest()',
  'request.open(method, path, true)',
  'request.withCredentials = true',
  "request.setRequestHeader('X-ProjectPulse-Session', token)",
  "request.setRequestHeader('X-Project-Pulse-Session', token)",
  "request.setRequestHeader('X-Session-Token', token)",
  "request.setRequestHeader('Authorization', `Bearer ${token}`)",
  "request.setRequestHeader('X-ProjectPulse-View-As-User', viewAsUserId)",
  'requiredCollections',
  'collectionCounts',
  'projectpulse:authoritative-api-diagnostic',
  '__projectPulseAuthoritativeApiDiagnostics'
], 'Wrapper-independent authoritative XHR client');
rejectAll(authoritative, ['window.fetch(', 'fetch(path'], 'Wrapper-independent authoritative XHR client');

requireAll(bridge, [
  "import { authoritativeApi } from './projectpulse-authoritative-api.js';",
  '/api/runtime/v2/role-policy/summary',
  '/api/runtime/v2/role-policy/catalog',
  '/api/runtime/v2/role-policy/versions',
  '/api/runtime/v2/role-policy/matrix',
  '/api/timesheet/ptc/users',
  '/entries',
  'expectedCollections',
  'normalizePtcWorkspace',
  'allActiveUsersAllowed: true',
  'projectpulse-authoritative-xhr-compatibility-v2',
  'projectpulse:ptc-runtime-users',
  'projectpulse:ptc-runtime-workspace',
  "requestMethod(input, init) !== 'GET'",
  'responseKeys: error?.diagnostic?.responseKeys'
], 'Runtime data authoritative bridge');
rejectAll(bridge, [
  'window.__projectPulseOriginalFetch',
  'directTransport(previousFetch)',
  'const raw = await response.text()'
], 'Runtime data authoritative bridge');

requireAll(roleModel, [
  "'/api/role-policy/summary': '/api/runtime/role-policy/summary'",
  "'/api/role-policy/catalog': '/api/runtime/role-policy/catalog'",
  "'/api/role-policy/versions': '/api/runtime/role-policy/versions'",
  "'/api/role-policy/matrix': '/api/runtime/role-policy/matrix'",
  "'/api/runtime/role-policy/roles/'",
  'const requestPath = readPath(path, method)'
], 'Module 012 compatibility caller');

requireAll(matrixModel, [
  "'/api/role-policy/catalog': '/api/runtime/role-policy/catalog'",
  "'/api/role-policy/matrix': '/api/runtime/role-policy/matrix'",
  'const requestPath = runtimePath(path)'
], 'Module 037 compatibility caller');

requireAll(portal, [
  '/api/runtime/timesheet/steward/users?weekStart=',
  '/api/runtime/timesheet/steward/users/${encodeURIComponent(selectedUserId)}/workspace?weekStart=',
  'publishUsers(payload)',
  'publishWorkspace(payload)',
  'projectpulse:ptc-runtime-users',
  'projectpulse:ptc-runtime-workspace'
], 'PTC runtime user and workspace caller');

requireAll(catalog, [
  'Available work for selected user',
  'User scope',
  'All active users',
  'Active user',
  'Choose any active ProjectPulse user',
  'Requests / Service Requests',
  'Project Tasks',
  'Non-Project Time',
  'snapshotCategories',
  'requestTask',
  'roleNames',
  'groupLabel',
  'selectionLabel',
  'projectpulse:ptc-runtime-users',
  'projectpulse:ptc-runtime-workspace',
  'PtcRuntimeTaskCatalog'
], 'PTC grouped all-active-user task catalog');

requireAll(gate, [
  'PtcTimeStewardGate',
  "'PROJECT_TEAM_COORDINATOR'",
  "'SUPER_ADMINISTRATOR'",
  "localStorage.getItem('projectPulseViewAsUser')",
  'if (state.active && !state.allowed) return null',
  '<PtcTimesheetManagementPortal />',
  '<PtcRuntimeTaskCatalog />'
], 'Effective-role runtime PTC gate');

requireAll(recovery, [
  "import { authoritativeApi } from '../projectpulse-authoritative-api.js';",
  "document.querySelector('#timesheet')",
  '/api/timesheet/timers/active',
  'window.setInterval(() => void load(), 5000)',
  'Timer status check failed',
  'Try timer check again',
  'Running timer recovered',
  'Timer automatically stopped',
  'Stop timer',
  'Discard'
], 'Persistent active timer recovery');

requireAll(css, [
  '.ptc-runtime-task-catalog',
  '.ptc-runtime-groups',
  '.ptc-runtime-target-card',
  '.ptc-runtime-role-boundary',
  '.ptc-entry-table select'
], 'PTC runtime task catalog styling');

requireAll(main, [
  "import './projectpulse-authoritative-api.js';",
  "import './runtime-data-compatibility.js';",
  "import Module001ActiveTimerRecoveryPortal from './module001/Module001ActiveTimerRecoveryPortal.jsx';",
  "import PtcTimeStewardGate from './module001/PtcTimeStewardGate.jsx';",
  '<Module001ActiveTimerRecoveryPortal />',
  '<PtcTimeStewardGate />'
], 'Gated runtime data and recovery application mount');
for (const forbidden of [
  "import PtcRuntimeTaskCatalog from './module001/PtcRuntimeTaskCatalog.jsx';",
  '<PtcRuntimeTaskCatalog />'
]) {
  if (main.includes(forbidden)) throw new Error(`Main must not bypass the PTC effective-role gate: ${forbidden}`);
}

const externalAvailable = backend.length > 0 && ptcBackend.length > 0 && project.length > 0;
if (externalAvailable) {
  requireAll(backend, [
    'MapRuntimeDataCompatibilityEndpoints',
    '/api/runtime/role-policy/summary',
    '/api/runtime/role-policy/matrix',
    '/api/runtime/timesheet/steward/users',
    '/api/runtime/timesheet/steward/users/{targetUserId:guid}/workspace',
    'PtcManagedRoleAliases',
    'eligibleRoleCodes',
    'roleNames',
    'Requests / Service Requests',
    'Project Tasks',
    'Non-Project Time',
    "to_jsonb(pt)->>'work_task_category'",
    "to_jsonb(pt)->>'service_request_number'",
    'nonProjectCategories',
    'canSubmitOnBehalf = false'
  ], 'Runtime role-policy and PTC backend');

  requireAll(ptcBackend, [
    'app.MapGet("/api/timesheet/ptc/users"',
    'app.MapGet("/api/timesheet/ptc/users/{targetUserId:guid}/entries"',
    'WHERE u.is_active = TRUE',
    'ActiveUserExistsAsync',
    'canSubmitOnBehalf = false'
  ], 'All-active-user Module 001 PTC backend');

  requireAll(project, [
    'app.MapRuntimeDataCompatibilityEndpoints();',
    'app.MapModule001PtcTimesheetManagementEndpoints();'
  ], 'Runtime endpoint registration');
} else {
  console.log('RUNTIME_ROLE_POLICY_PTC_EXTERNAL_CHECK=SKIPPED_MINIMAL_WEB_CONTEXT');
}

for (const forbidden of [
  'app.MapPost("/api/runtime/role-policy',
  'app.MapPut("/api/runtime/role-policy',
  'app.MapPatch("/api/runtime/role-policy',
  'app.MapDelete("/api/runtime/role-policy'
]) {
  if (backend.includes(forbidden)) throw new Error(`Runtime role-policy aliases must remain read-only: ${forbidden}`);
}

console.log('WRAPPER_INDEPENDENT_RUNTIME_ROLE_POLICY_PTC_CONTRACTS=PASS');
