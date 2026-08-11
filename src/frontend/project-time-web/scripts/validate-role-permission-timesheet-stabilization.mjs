import { existsSync } from 'node:fs';
import { readFile } from 'node:fs/promises';
import { resolve } from 'node:path';

const root = resolve(process.cwd(), '../../..');
const absolute = (path) => resolve(root, path);
const read = (path) => readFile(absolute(path), 'utf8');
const optional = async (path) => existsSync(absolute(path)) ? read(path) : '';
const requireAll = (source, values, label) => {
  for (const value of values) {
    if (!source.includes(value)) throw new Error(`${label} missing contract: ${value}`);
  }
};
const rejectAll = (source, values, label) => {
  for (const value of values) {
    if (source.includes(value)) throw new Error(`${label} contains retired contract: ${value}`);
  }
};

const [
  support,
  boundary,
  project,
  gate,
  main,
  authoritative,
  timerPortal,
  timerPicker,
  timerBackend,
  resultExecution,
  stewardV2,
  queries
] = await Promise.all([
  optional('src/backend/ProjectTime.Api/Modules/ScopedRolePolicySupport.cs'),
  optional('src/backend/ProjectTime.Api/Modules/PtcTimeStewardRoleBoundary.cs'),
  optional('src/backend/ProjectTime.Api/ProjectTime.Api.csproj'),
  read('src/frontend/project-time-web/src/module001/PtcTimeStewardGate.jsx'),
  read('src/frontend/project-time-web/src/main.jsx'),
  read('src/frontend/project-time-web/src/projectpulse-authoritative-api.js'),
  read('src/frontend/project-time-web/src/module001/TimesheetEnhancementPortal.jsx'),
  read('src/frontend/project-time-web/src/module001/TimesheetTaskPicker.jsx'),
  optional('src/backend/ProjectTime.Api/Modules/Module001TimerTargets.cs'),
  optional('src/backend/ProjectTime.Api/Modules/Module001ResultExecutionCompatibility.cs'),
  optional('src/backend/ProjectTime.Api/Modules/Module001TimeStewardV2Module.cs'),
  optional('src/backend/ProjectTime.Api/Modules/ScopedRolePolicyQueries.cs')
]);

if (support) {
  requireAll(support, [
    'PTP_DB_HOST',
    'PTP_DB_PORT',
    'PTP_DB_NAME',
    'PTP_DB_USER',
    'PTP_DB_PASSWORD',
    'Pulse PTP database configuration is incomplete',
    'ConnectionStrings__DefaultConnection'
  ], 'Unified scoped-role database resolver');
  const ptpPosition = support.indexOf('PTP_DB_HOST');
  const fallbackPosition = support.indexOf('ConnectionStrings__DefaultConnection');
  if (ptpPosition < 0 || fallbackPosition < 0 || ptpPosition > fallbackPosition) {
    throw new Error('PTP_DB_* must take precedence over legacy connection-string variables.');
  }
}

if (boundary) {
  requireAll(boundary, [
    'PROJECT_TEAM_COORDINATOR',
    'SUPER_ADMINISTRATOR',
    '/api/timesheet/ptc',
    '/api/runtime/timesheet/steward',
    '/api/scoped-time/',
    'time_steward_role_required',
    "Only Project Team Coordinator or Super Administrator may manage another user's time"
  ], 'Time-steward hard role boundary');
}

if (project) {
  requireAll(project, [
    'app.UsePtcTimeStewardRoleBoundary();',
    'app.UseModule001ResultExecutionCompatibility();',
    'app.MapModule001TimeStewardV2Endpoints();'
  ], 'Generated API registration');
}

requireAll(gate, [
  'PtcTimeStewardGate',
  "'PROJECT_TEAM_COORDINATOR'",
  "'SUPER_ADMINISTRATOR'",
  "'projectPulseViewAsUser'",
  'roleCodes',
  'if (state.active && !state.allowed) return null',
  '<PtcTimesheetManagementPortal />'
], 'Effective-role PTC UI gate');
rejectAll(gate, ['PtcRuntimeTaskCatalog'], 'single PTC portal owner');

requireAll(main, [
  "import './projectpulse-authoritative-api.js';",
  "import PtcTimeStewardGate from './module001/PtcTimeStewardGate.jsx';",
  '<PtcTimeStewardGate />',
  '<TimesheetEnhancementPortal />'
], 'Gated PTC and timer application mount');
rejectAll(main, [
  "import PtcTimesheetManagementPortal from './module001/PtcTimesheetManagementPortal.jsx';",
  'Module001ActiveTimerRecoveryPortal',
  'PtcRuntimeTaskCatalog'
], 'Main Module 001 ownership bypass');

requireAll(authoritative, [
  'new XMLHttpRequest()',
  'requiredCollections',
  'collectionCounts',
  'projectpulse:authoritative-api-diagnostic',
  'nativeFetchAuthoritative'
], 'Wrapper-independent authoritative API retained for protected mutation and recovery flows');

requireAll(timerPortal, [
  "import { authoritativeApi } from '../projectpulse-authoritative-api.js';",
  'function isTimesheetRoute()',
  "=== 'timesheet'",
  '/api/timesheet/timers/targets?weekStart=',
  '/api/timesheet/timers/active',
  '/api/timesheet/timers/history?weekStart=',
  "requiredCollections: ['targets']",
  "requiredCollections: ['timers']",
  'window.setInterval(refresh, 5000)',
  'window.setInterval(() => setClock(new Date()), 1000)',
  'The server continues tracking it through refreshes, sign-out, and session expiration.',
  'module001-server-timer-recovery',
  'data-projectpulse-react-owned-slot="true"'
], 'Module 001 timer target, history, and persistent recovery integration');
rejectAll(timerPortal, [
  '/api/assignments/available-tasks',
  '/api/timesheet/work-queue',
  'document.createElement',
  '.insertBefore(',
  '.replaceChildren('
], 'Legacy timer target and DOM mutation paths');

requireAll(timerPicker, [
  "const GROUP_ORDER = ['Requests / Service Requests', 'Project Tasks', 'Non-Project Time']",
  "if (label === 'Service Request Tasks') return 'Requests / Service Requests'",
  "if (label === 'Regular Tasks') return 'Project Tasks'",
  'role="combobox"'
], 'Three-group timer picker');

if (timerBackend) {
  requireAll(timerBackend, [
    '/api/timesheet/timers/targets',
    'project_assignments',
    'project_tasks',
    "to_jsonb(pt)->>'work_task_category'",
    "to_jsonb(pt)->>'service_request_number'",
    'regularTaskCount',
    'serviceRequestTaskCount',
    'nonProjectCount',
    'Regular Tasks',
    'Service Request Tasks',
    'Non-Project Time'
  ], 'Authoritative timer target backend');
}

if (resultExecution) {
  requireAll(resultExecution, [
    'Module001TimerTargetsAsync(context)',
    'Module001ActiveTimerAsync(context)',
    'Module001TimerHistoryAsync(context)',
    'X-ProjectPulse-Module001-Result-Execution',
    'await result.ExecuteAsync(context);'
  ], 'Explicit Module 001 GET result execution');
}

if (stewardV2) {
  requireAll(stewardV2, [
    '/api/runtime/timesheet/steward/v2/users',
    '/api/runtime/timesheet/steward/v2/users/{targetUserId:guid}/workspace',
    '/api/runtime/timesheet/steward/v2/entries/{timeEntryId:guid}/move',
    'ENGINEERING',
    'ENGINEERING_LEAD',
    'PROJECT_MANAGEMENT',
    'PROJECT_MANAGEMENT_LEAD',
    'Requests / Service Requests',
    'Project Tasks',
    'Non-Project Time',
    'Module001EnsurePtcAssignmentV2Async',
    'crossActivityTypeMove = true'
  ], 'Flexible PTC v2 source');
}

if (queries) {
  requireAll(queries, [
    'foreach (var roleCode in CanonicalRoleOrder)',
    'scoped_role_policy_modules',
    'WHERE is_active = TRUE'
  ], 'Role and module query foundation');
}

console.log('ROLE_PERMISSION_TIMESHEET_STABILIZATION=PASS database=PTP timer=server-authoritative pTC=v2 domOwnership=react-owned');
