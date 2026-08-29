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
    if (source.includes(value)) throw new Error(`${label} contains retired contract: ${value}`);
  }
};

const paths = {
  portal: 'src/frontend/project-time-web/src/module001/PtcTimesheetManagementPortal.jsx',
  gate: 'src/frontend/project-time-web/src/module001/PtcTimeStewardGate.jsx',
  css: 'src/frontend/project-time-web/src/module001/ptc-timesheet-management.css',
  runtimeCss: 'src/frontend/project-time-web/src/module001/module001-runtime-v2.css',
  model: 'src/frontend/project-time-web/src/role-permission-model.js',
  backend: 'src/backend/ProjectTime.Api/Modules/Module001PtcTimesheetManagement.cs',
  backendV2: 'src/backend/ProjectTime.Api/Modules/Module001TimeStewardV2Module.cs',
  resultExecution: 'src/backend/ProjectTime.Api/Modules/Module001ResultExecutionCompatibility.cs',
  runtimeBackend: 'src/backend/ProjectTime.Api/Modules/RuntimeDataCompatibilityModule.cs',
  boundary: 'src/backend/ProjectTime.Api/Modules/PtcTimeStewardRoleBoundary.cs',
  migration: 'database/migrations/043_ptc_time_steward_permissions.sql',
  rollback: 'database/rollback/043_ptc_time_steward_permissions_rollback.sql',
  main: 'src/frontend/project-time-web/src/main.jsx',
  project: 'src/backend/ProjectTime.Api/ProjectTime.Api.csproj'
};

const [
  portal, gate, css, runtimeCss, model, backend, backendV2, resultExecution,
  runtimeBackend, boundary, migration, rollback, main, project
] = await Promise.all([
  text(paths.portal),
  text(paths.gate),
  text(paths.css),
  text(paths.runtimeCss),
  text(paths.model),
  optionalText(paths.backend),
  optionalText(paths.backendV2),
  optionalText(paths.resultExecution),
  optionalText(paths.runtimeBackend),
  optionalText(paths.boundary),
  optionalText(paths.migration),
  optionalText(paths.rollback),
  text(paths.main),
  optionalText(paths.project)
]);

requireAll(portal, [
  "import { authoritativeApi } from '../projectpulse-authoritative-api.js';",
  'Project Team Coordinator · Time Steward',
  'Manage time for other users',
  'No submission on behalf',
  'The selected user reviews and submits the corrected week',
  '/api/runtime/timesheet/steward/v2/users?weekStart=',
  '/api/runtime/timesheet/steward/v2/users/${encodeURIComponent(selectedUserId)}/workspace',
  '/api/runtime/timesheet/steward/v2/entries/${entry.timeEntryId}/move',
  "requiredCollections: ['users']",
  "requiredCollections: ['entries', 'moveTargets', 'nonProjectCategories', 'availableProjects']",
  'Engineering, Engineering Lead, Project Management, and Project Management Lead',
  'Return week to draft',
  'Edit entry',
  'Move time',
  'Create replacement task',
  'Remove draft entry',
  'Requests / Service Requests',
  'Project Tasks',
  'Non-Project Time',
  'assignment will be created',
  '<optgroup key={group.name} label={group.name}>',
  'Required reason',
  'immutable audit history',
  'data-projectpulse-time-steward-contract="module001-time-steward-v2"'
], 'React-owned PTC time steward portal');

rejectAll(portal, [
  'document.createElement',
  '.appendChild(',
  '.insertBefore(',
  '.prepend(',
  '.replaceChildren(',
  '.innerHTML =',
  'ptcSubmissionHidden',
  'hideSubmissionControls'
], 'PTC React DOM ownership');

requireAll(gate, [
  'PtcTimeStewardGate',
  "'PROJECT_TEAM_COORDINATOR'",
  "'SUPER_ADMINISTRATOR'",
  "'projectPulseViewAsUser'",
  "from '../effective-role-authority.js'",
  'EFFECTIVE_ROLE_AUTHORITY_EVENTS',
  'hasAnyEffectiveRole',
  'readEffectiveRoleAuthority',
  'if (!authority.ready) return null',
  'const MODULE001B_ROLES = new Set([',
  'const canUseModule001B = hasAnyEffectiveRole(authority, MODULE001B_ROLES);',
  '<Module001BTimeReallocationPortal allowed={canUseModule001B} />',
  '<PtcTimesheetManagementPortal />'
], 'Effective-role PTC frontend gate');
rejectAll(gate, ['PtcRuntimeTaskCatalog'], 'single PTC portal ownership');

requireAll(css, [
  '.ptc-time-steward-portal',
  '.ptc-no-submit',
  '.ptc-toolbar',
  '.ptc-user-summary',
  '.ptc-entry-table',
  '.ptc-modal',
  '.ptc-entry-table button.danger'
], 'PTC base styling');
requireAll(runtimeCss, [
  '.ptc-destination-catalog',
  '.ptc-destination-groups',
  '.ptc-select-user-prompt',
  '[data-projectpulse-react-owned-slot="true"]'
], 'PTC v2 destination and ownership styling');

requireAll(model, [
  'PTC_TIME_STEWARD_ACTIONS',
  "'TIME_VIEW_ON_BEHALF'",
  "'TIME_UNSUBMIT'",
  "'TIME_CORRECT_ON_BEHALF'",
  "'TIME_REASSIGN'",
  "'TIME_DELETE_ON_BEHALF'",
  "'TIME_TASK_CREATE'",
  "'TIME_TASK_ASSIGN'",
  "'TIME_SUBMIT'",
  "'TIME_DELETE_PERMANENT'",
  'operationalTimeSteward',
  'does not submit a timesheet for another user'
], 'PTC permission model');

requireAll(main, [
  "import PtcTimeStewardGate from './module001/PtcTimeStewardGate.jsx';",
  '<PtcTimeStewardGate />'
], 'Gated frontend mount');
rejectAll(main, [
  "import PtcTimesheetManagementPortal from './module001/PtcTimesheetManagementPortal.jsx';",
  "import PtcRuntimeTaskCatalog from './module001/PtcRuntimeTaskCatalog.jsx';",
  '<PtcRuntimeTaskCatalog />'
], 'Main PTC ownership bypass');

if (portal.includes('Submit selected user') || portal.includes('Submit on behalf')) {
  throw new Error('PTC workspace must not contain a submission-on-behalf action.');
}

const externalAvailable = [
  backend,
  backendV2,
  resultExecution,
  runtimeBackend,
  boundary,
  migration,
  rollback,
  project
].every(Boolean);

if (externalAvailable) {
  requireAll(boundary, [
    'UsePtcTimeStewardRoleBoundary',
    'PROJECT_TEAM_COORDINATOR',
    'SUPER_ADMINISTRATOR',
    '/api/timesheet/ptc',
    '/api/runtime/timesheet/steward',
    'time_steward_role_required',
    'No Access. Module 001B is restricted to Project Team Coordinator and Super Administrator.',
    'view_as_read_only',
    'legacyModule001Move',
    'StatusCodes.Status410Gone',
    'module_001b_reallocation_required'
  ], 'Non-bypassable time-steward role boundary');

  requireAll(runtimeBackend, [
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
  ], 'Role-filtered eligible user source');

  requireAll(resultExecution, [
    'UseModule001ResultExecutionCompatibility',
    'RuntimePtcUsersAsync(context)',
    'RuntimePtcWorkspaceAsync(targetUserId, context)',
    'Module001TimerTargetsAsync(context)',
    'Module001ActiveTimerAsync(context)',
    'Module001TimerHistoryAsync(context)',
    'X-ProjectPulse-Module001-Result-Execution',
    'await result.ExecuteAsync(context);'
  ], 'Explicit Module 001 result execution');

  requireAll(backendV2, [
    'MapModule001TimeStewardV2Endpoints',
    'module001-time-steward-v2-2026-07-28',
    '/api/runtime/timesheet/steward/v2/users',
    '/api/runtime/timesheet/steward/v2/users/{targetUserId:guid}/workspace',
    '/api/runtime/timesheet/steward/v2/entries/{timeEntryId:guid}/move',
    'Requests / Service Requests',
    'Project Tasks',
    'Non-Project Time',
    'canAssignExistingProjectTaskDuringMove = true',
    'canMoveToNonProjectTime = true',
    'TIME_REASSIGN',
    'TIME_TASK_ASSIGN',
    'Module001EnsurePtcAssignmentV2Async',
    'non_project_time_category_id = @category_id',
    "association_source = 'PTC_TIME_STEWARD'",
    'crossActivityTypeMove = true',
    'submissionOnBehalf = false'
  ], 'Flexible PTC v2 workspace and legacy move implementation retained behind 410 retirement boundary');

  requireAll(backend, [
    'MapModule001PtcTimesheetManagementEndpoints',
    '/weeks/{weekStart}/unsubmit',
    'app.MapPatch("/api/timesheet/ptc/entries/{timeEntryId:guid}"',
    '/remove"',
    'app.MapPost("/api/timesheet/ptc/tasks"',
    'TIME_UNSUBMIT',
    'TIME_CORRECT_ON_BEHALF',
    'TIME_DELETE_ON_BEHALF',
    'TIME_TASK_CREATE',
    'TIME_TASK_ASSIGN',
    'canSubmitOnBehalf = false',
    'submissionOnBehalf = false',
    'userMustResubmit = true',
    'scoped_time_management_events',
    'Timer-generated entries cannot be removed'
  ], 'Existing governed correction API');

  for (const forbidden of [
    '"TIME_SUBMIT"',
    'TIME_DELETE_PERMANENT", actor',
    'USER_IMPERSONATE", actor'
  ]) {
    if (backend.includes(forbidden) || backendV2.includes(forbidden)) {
      throw new Error(`PTC API must not execute protected action: ${forbidden}`);
    }
  }

  requireAll(migration, [
    '043_ptc_time_steward_permissions',
    'TIME_VIEW_ON_BEHALF',
    'TIME_UNSUBMIT',
    'TIME_DELETE_ON_BEHALF',
    'TIME_TASK_CREATE',
    'TIME_TASK_ASSIGN',
    'scoped_time_management_events',
    "'PROJECT_TEAM_COORDINATOR'",
    "'TIME_SUBMIT'",
    "'TIME_DELETE_PERMANENT'",
    "'DENY'",
    'immutableAuditRequired'
  ], 'Migration 043');

  requireAll(rollback, [
    'Rollback 043 is blocked because PTC time-management audit evidence exists.',
    'Rollback 043 is blocked because later policy versions exist.',
    "source_name = '043_ptc_time_steward_permissions'",
    'DROP TABLE IF EXISTS scoped_time_management_events',
    "migration_id = '043_ptc_time_steward_permissions'"
  ], 'Migration 043 rollback');

  requireAll(project, [
    'app.UsePtcTimeStewardRoleBoundary();',
    'app.UseModule001ResultExecutionCompatibility();',
    'app.MapModule001TimeStewardV2Endpoints();',
    'app.MapModule001PtcTimesheetManagementEndpoints();'
  ], 'Backend registration');
} else {
  console.log('MODULE_001_PTC_EXTERNAL_SOURCE_CHECK=SKIPPED_MINIMAL_WEB_CONTEXT');
}

console.log('MODULE_001_PTC_TIME_STEWARD=PASS eligibleRoles=4 destinationGroups=3 submissionOnBehalf=false module001b=strict-reallocation');
