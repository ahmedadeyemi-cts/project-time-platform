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

const [
  jsonResponse,
  authoritative,
  bridge,
  roleModel,
  matrixModel,
  portal,
  gate,
  roleAuthority,
  timerPortal,
  timerView,
  picker,
  main,
  runtimeBackend,
  ptcBackend,
  ptcBackendV2,
  resultExecution,
  project
] = await Promise.all([
  text('src/frontend/project-time-web/src/api-json-response.js'),
  text('src/frontend/project-time-web/src/projectpulse-authoritative-api.js'),
  text('src/frontend/project-time-web/src/runtime-data-compatibility.js'),
  text('src/frontend/project-time-web/src/role-permission-model.js'),
  text('src/frontend/project-time-web/src/role-permission-matrix-model.js'),
  text('src/frontend/project-time-web/src/module001/PtcTimesheetManagementPortal.jsx'),
  text('src/frontend/project-time-web/src/module001/PtcTimeStewardGate.jsx'),
  text('src/frontend/project-time-web/src/effective-role-authority.js'),
  text('src/frontend/project-time-web/src/module001/TimesheetEnhancementPortal.jsx'),
  text('src/frontend/project-time-web/src/module001/TimesheetTimerView.jsx'),
  text('src/frontend/project-time-web/src/module001/TimesheetTaskPicker.jsx'),
  text('src/frontend/project-time-web/src/main.jsx'),
  optionalText('src/backend/ProjectTime.Api/Modules/RuntimeDataCompatibilityModule.cs'),
  optionalText('src/backend/ProjectTime.Api/Modules/Module001PtcTimesheetManagement.cs'),
  optionalText('src/backend/ProjectTime.Api/Modules/Module001TimeStewardV2Module.cs'),
  optionalText('src/backend/ProjectTime.Api/Modules/Module001ResultExecutionCompatibility.cs'),
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
  '__projectPulseAuthoritativeApiDiagnostics',
  'nativeFetchAuthoritative',
  'xhr-success-missing-collections'
], 'Wrapper-independent authoritative API client');
rejectAll(authoritative, ['window.fetch(', 'fetch(path'], 'Wrapper-independent authoritative XHR client');

// Legacy read compatibility remains fail-closed for older panels. Modules 012
// and 037 themselves now use /api/rbac/v1; this bridge must never synthesize
// missing arrays or turn an incomplete success into an empty valid contract.
requireAll(bridge, [
  "import { authoritativeApi } from './projectpulse-authoritative-api.js';",
  "DIRECT_ROLE_POLICY_MARKER = 'projectpulse-role-policy-direct-fetch-v3'",
  '/api/runtime/v2/role-policy/summary',
  '/api/runtime/v2/role-policy/catalog',
  '/api/runtime/v2/role-policy/versions',
  '/api/runtime/v2/role-policy/matrix',
  'expectedCollections',
  'directRolePolicyResponse',
  'hasCollections(normalized, collections)',
  "status: 'role_policy_contract_mismatch'",
  "requestMethod(input, init) !== 'GET'",
  'responseKeys: error?.diagnostic?.responseKeys'
], 'Fail-closed legacy role-policy compatibility');
rejectAll(bridge, [
  'requiredCollections.map((name) => [name, []])',
  'normalized[name] = []'
], 'Runtime data bridge safety');

requireAll(roleModel, [
  "'/api/role-policy/summary': '/api/runtime/role-policy/summary'",
  "'/api/role-policy/catalog': '/api/runtime/role-policy/catalog'",
  "'/api/role-policy/versions': '/api/runtime/role-policy/versions'"
], 'Legacy Module 012 compatibility caller');
requireAll(matrixModel, [
  "'/api/role-policy/catalog': '/api/runtime/role-policy/catalog'",
  "'/api/role-policy/matrix': '/api/runtime/role-policy/matrix'"
], 'Legacy Module 037 compatibility caller');

requireAll(portal, [
  "import { authoritativeApi } from '../projectpulse-authoritative-api.js';",
  '/api/runtime/timesheet/steward/v2/users?weekStart=',
  '/api/runtime/timesheet/steward/v2/users/${encodeURIComponent(selectedUserId)}/workspace',
  '/api/runtime/timesheet/steward/v2/entries/${entry.timeEntryId}/move',
  "requiredCollections: ['users']",
  "requiredCollections: ['entries', 'moveTargets', 'nonProjectCategories', 'availableProjects']",
  'Engineering, Engineering Lead, Project Management, and Project Management Lead',
  'Requests / Service Requests',
  'Project Tasks',
  'Non-Project Time',
  'assignment will be created',
  'No submission on behalf'
], 'Wrapper-independent PTC v2 caller');
rejectAll(portal, [
  'publishUsers(payload)',
  'publishWorkspace(payload)',
  'PtcRuntimeTaskCatalog',
  'document.createElement',
  '.insertBefore('
], 'Retired PTC compatibility portal behavior');

requireAll(gate, [
  'PtcTimeStewardGate',
  "from '../effective-role-authority.js'",
  'EFFECTIVE_ROLE_AUTHORITY_EVENTS',
  'hasAnyEffectiveRole',
  'readEffectiveRoleAuthority',
  "'PROJECT_TEAM_COORDINATOR'",
  "'SUPER_ADMINISTRATOR'",
  'if (!authority.ready) return null',
  'if (!canStewardTime && !canReviewApprovals) return null',
  '<PtcTimesheetManagementPortal />'
], 'Effective-role PTC gate');
rejectAll(gate, ['PtcRuntimeTaskCatalog'], 'single PTC portal owner');

requireAll(roleAuthority, [
  'normalizeProjectPulseRoleCodes',
  'readEffectiveRoleAuthority',
  'hasAnyEffectiveRole',
  "readJsonStorage('projectPulseViewAsUser')",
  "readJsonStorage('projectPulseAuthSession')",
  'window.__projectPulseEffectiveNavigation',
  'roleCodes',
  "source: 'view_as'",
  "source: 'effective_navigation'",
  "source: 'session'"
], 'Central effective-role authority');

const viewAsPosition = roleAuthority.indexOf("readJsonStorage('projectPulseViewAsUser')");
const navigationPosition = roleAuthority.indexOf('window.__projectPulseEffectiveNavigation');
const sessionPosition = roleAuthority.indexOf("readJsonStorage('projectPulseAuthSession')");
if (
  viewAsPosition < 0
  || navigationPosition < 0
  || sessionPosition < 0
  || viewAsPosition > navigationPosition
  || navigationPosition > sessionPosition
) {
  throw new Error('Effective-role authority must prefer View-As, then effective navigation, then authenticated session.');
}

requireAll(timerPortal, [
  "import { authoritativeApi } from '../projectpulse-authoritative-api.js';",
  '/api/timesheet/timers/targets',
  '/api/timesheet/timers/active',
  '/api/timesheet/timers/history',
  "requiredCollections: ['targets']",
  "requiredCollections: ['timers']",
  'window.setInterval(refresh, 5000)',
  'window.setInterval(() => setClock(new Date()), 1000)',
  'The server continues tracking it through refreshes, sign-out, and session expiration.',
  'module001-server-timer-recovery'
], 'Persistent integrated timer recovery');
requireAll(timerView, [
  'Timer history',
  'history.map',
  'window.setInterval(() => setClock(new Date()), 1000)'
], 'Visible timer history and live clock');
requireAll(picker, [
  "const GROUP_ORDER = ['Requests / Service Requests', 'Project Tasks', 'Non-Project Time']",
  'role="combobox"',
  'Search activity, task, project, customer, or request'
], 'Three-group timer activity picker');

requireAll(main, [
  "import './projectpulse-authoritative-api.js';",
  "import './runtime-data-compatibility.js';",
  "import TimesheetEnhancementPortal from './module001/TimesheetEnhancementPortal.jsx';",
  "import PtcTimeStewardGate from './module001/PtcTimeStewardGate.jsx';",
  '<TimesheetEnhancementPortal />',
  '<PtcTimeStewardGate />'
], 'Integrated Module 001 application mount');
rejectAll(main, [
  'Module001ActiveTimerRecoveryPortal',
  'PtcRuntimeTaskCatalog'
], 'Duplicate Module 001 runtime mount');

const externalAvailable = [
  runtimeBackend,
  ptcBackend,
  ptcBackendV2,
  resultExecution,
  project
].every(Boolean);

if (externalAvailable) {
  requireAll(runtimeBackend, [
    'MapRuntimeDataCompatibilityEndpoints',
    'PtcManagedRoleAliases',
    'ENGINEERING',
    'ENGINEER',
    'ENGINEERING_LEAD',
    'ENGINEERING_TEAM_LEAD',
    'PROJECT_MANAGEMENT',
    'PROJECT_MANAGER',
    'PROJECT_MANAGEMENT_LEAD',
    'PROJECT_MANAGEMENT_TEAM_LEAD',
    'PM_TEAM_LEAD',
    'RuntimePtcUsersAsync',
    'eligibleRoleCodes',
    'roleNames'
  ], 'Role-filtered runtime PTC backend');

  requireAll(ptcBackendV2, [
    'MapModule001TimeStewardV2Endpoints',
    '/api/runtime/timesheet/steward/v2/users',
    '/api/runtime/timesheet/steward/v2/users/{targetUserId:guid}/workspace',
    '/api/runtime/timesheet/steward/v2/entries/{timeEntryId:guid}/move',
    'Requests / Service Requests',
    'Project Tasks',
    'Non-Project Time',
    'canAssignExistingProjectTaskDuringMove = true',
    'canMoveToNonProjectTime = true',
    'Module001EnsurePtcAssignmentV2Async',
    'crossActivityTypeMove = true'
  ], 'PTC v2 backend');

  requireAll(resultExecution, [
    'UseModule001ResultExecutionCompatibility',
    'Module001TimerTargetsAsync(context)',
    'Module001ActiveTimerAsync(context)',
    'Module001TimerHistoryAsync(context)',
    'X-ProjectPulse-Module001-Result-Execution',
    'await result.ExecuteAsync(context);'
  ], 'Module 001 GET result execution');

  requireAll(ptcBackend, [
    'app.MapPost("/api/timesheet/ptc/users/{targetUserId:guid}/weeks/{weekStart}/unsubmit"',
    'app.MapPatch("/api/timesheet/ptc/entries/{timeEntryId:guid}"',
    'app.MapPost("/api/timesheet/ptc/entries/{timeEntryId:guid}/remove"',
    'app.MapPost("/api/timesheet/ptc/tasks"',
    'canSubmitOnBehalf = false'
  ], 'Governed PTC mutation backend');

  requireAll(project, [
    'app.UseModule001ResultExecutionCompatibility();',
    'app.MapModule001TimeStewardV2Endpoints();',
    'app.MapModule001PtcTimesheetManagementEndpoints();',
    'app.MapModule001TimerTargetEndpoints();'
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
  if (runtimeBackend.includes(forbidden)) {
    throw new Error(`Runtime role-policy aliases must remain read-only: ${forbidden}`);
  }
}

console.log('RUNTIME_ROLE_POLICY_PTC_DATA=PASS pTCTransport=v2-authoritative timerPersistence=server-authoritative');
