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

const [support, boundary, project, gate, main, timerPortal, timerBackend, queries] = await Promise.all([
  optional('src/backend/ProjectTime.Api/Modules/ScopedRolePolicySupport.cs'),
  optional('src/backend/ProjectTime.Api/Modules/PtcTimeStewardRoleBoundary.cs'),
  optional('src/backend/ProjectTime.Api/ProjectTime.Api.csproj'),
  read('src/frontend/project-time-web/src/module001/PtcTimeStewardGate.jsx'),
  read('src/frontend/project-time-web/src/main.jsx'),
  read('src/frontend/project-time-web/src/module001/TimesheetEnhancementPortal.jsx'),
  optional('src/backend/ProjectTime.Api/Modules/Module001TimerTargets.cs'),
  optional('src/backend/ProjectTime.Api/Modules/ScopedRolePolicyQueries.cs')
]);

if (support) {
  requireAll(support, [
    'PTP_DB_HOST',
    'PTP_DB_PORT',
    'PTP_DB_NAME',
    'PTP_DB_USER',
    'PTP_DB_PASSWORD',
    'ProjectPulse PTP database configuration is incomplete',
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

if (project) requireAll(project, ['app.UsePtcTimeStewardRoleBoundary();'], 'Generated API registration');

requireAll(gate, [
  'PtcTimeStewardGate',
  "'PROJECT_TEAM_COORDINATOR'",
  "'SUPER_ADMINISTRATOR'",
  "'projectPulseViewAsUser'",
  'roleCodes',
  'if (state.active && !state.allowed) return null'
], 'Effective-role PTC UI gate');
requireAll(main, [
  "import PtcTimeStewardGate from './module001/PtcTimeStewardGate.jsx';",
  '<PtcTimeStewardGate />'
], 'Gated PTC application mount');
if (main.includes("import PtcTimesheetManagementPortal from './module001/PtcTimesheetManagementPortal.jsx';")) {
  throw new Error('Main still bypasses the PTC effective-role gate.');
}

requireAll(timerPortal, [
  '/api/timesheet/timers/targets?weekStart=',
  'assignedTasks,',
  "target.groupLabel === 'Regular Tasks'",
  "target.groupLabel === 'Service Request Tasks'",
  'timerTargetLoadError',
  'projectpulse:module001-timer-targets',
  'returned an incomplete timer-target payload'
], 'Authoritative Module 001 timer target integration');
for (const forbidden of [
  '/api/assignments/available-tasks',
  '/api/timesheet/work-queue',
  'The canonical Timesheet snapshot remains usable if the assignment join refresh fails.'
]) {
  if (timerPortal.includes(forbidden)) throw new Error(`Legacy silent timer target path remains: ${forbidden}`);
}

if (timerBackend) {
  requireAll(timerBackend, [
    '/api/timesheet/timers/targets',
    'project_assignments',
    'project_tasks',
    "to_jsonb(pt)->>'work_task_category'",
    "to_jsonb(pt)->>'service_request_number'",
    'Regular Tasks',
    'Service Request Tasks',
    'Non-Project Time'
  ], 'Authoritative timer target backend');
}

if (queries) {
  requireAll(queries, [
    'foreach (var roleCode in CanonicalRoleOrder)',
    'scoped_role_policy_modules',
    'WHERE is_active = TRUE'
  ], 'Role and module query foundation');
}

console.log('Role permission and timesheet stabilization contracts passed.');
