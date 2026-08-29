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
    if (source.toLowerCase().includes(value.toLowerCase())) {
      throw new Error(`${label} contains retired/forbidden contract: ${value}`);
    }
  }
};

const paths = {
  portal: 'src/frontend/project-time-web/src/module001/PtcTimesheetManagementPortal.jsx',
  gate: 'src/frontend/project-time-web/src/module001/PtcTimeStewardGate.jsx',
  css: 'src/frontend/project-time-web/src/module001/ptc-timesheet-management.css',
  runtimeCss: 'src/frontend/project-time-web/src/module001/module001-runtime-v2.css',
  legacyMovePortal: 'src/frontend/project-time-web/src/module001/PtcGuidedMovePortal.jsx',
  legacyMoveCss: 'src/frontend/project-time-web/src/module001/ptc-guided-move.css',
  legacyRetirementCss: 'src/frontend/project-time-web/src/module001/module001b-reallocation-retirement.css',
  module001bPortal: 'src/frontend/project-time-web/src/module001b/Module001BTimeReallocationPortal.jsx',
  module001bGate: 'src/frontend/project-time-web/src/module001b/Module001BTimeReallocationGate.jsx',
  module001bCss: 'src/frontend/project-time-web/src/module001b/module001b-time-reallocation.css',
  module001bBackend: 'src/backend/ProjectTime.Api/Modules/Module001BTimeReallocationModule.cs',
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

for (const retiredPath of [paths.legacyMovePortal, paths.legacyMoveCss, paths.legacyRetirementCss]) {
  if (existsSync(absolute(retiredPath))) {
    throw new Error(`Module 001 must not retain allocation-change artifact: ${retiredPath}`);
  }
}

const [
  portal, gate, css, runtimeCss, module001bPortal, module001bGate, module001bCss,
  module001bBackend, model, backend, backendV2, resultExecution, runtimeBackend,
  boundary, migration, rollback, main, project
] = await Promise.all([
  text(paths.portal),
  text(paths.gate),
  text(paths.css),
  text(paths.runtimeCss),
  text(paths.module001bPortal),
  text(paths.module001bGate),
  text(paths.module001bCss),
  text(paths.module001bBackend),
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
  'Manage ordinary time for other users',
  'No submission on behalf',
  '/api/runtime/timesheet/steward/v2/users?weekStart=',
  '/api/runtime/timesheet/steward/v2/users/${encodeURIComponent(selectedUserId)}/workspace',
  "requiredCollections: ['users']",
  "requiredCollections: ['entries']",
  'Return week to draft',
  'Save correction',
  'Remove',
  'Submitted and approved entries remain read-only in Module 001.',
  'Required reason',
  'immutable audit history',
  'data-projectpulse-time-steward-contract="module001-time-steward-v2"'
], 'React-owned Module 001 time steward portal');

rejectAll(portal, [
  '/move',
  'move time',
  'move to',
  'moveselections',
  'movetargets',
  'destination',
  'reallocat',
  'Module001B',
  'time-reallocation',
  'Create replacement task',
  'Create and assign replacement task',
  'document.createElement',
  '.appendChild(',
  '.insertBefore(',
  '.prepend(',
  '.replaceChildren(',
  '.innerHTML ='
], 'Module 001 allocation boundary');

requireAll(gate, [
  'PtcTimeStewardGate',
  "'PROJECT_TEAM_COORDINATOR'",
  "'SUPER_ADMINISTRATOR'",
  "from '../effective-role-authority.js'",
  'EFFECTIVE_ROLE_AUTHORITY_EVENTS',
  'hasAnyEffectiveRole',
  'readEffectiveRoleAuthority',
  'if (!authority.ready) return null',
  '<PtcTimesheetManagementPortal />'
], 'Effective-role Module 001 frontend gate');

rejectAll(gate, [
  'Module001B',
  'time-reallocation',
  'reallocat',
  'move time',
  'PtcGuidedMovePortal'
], 'Module 001 gate allocation boundary');

requireAll(module001bGate, [
  'Module001BTimeReallocationGate',
  "'PROJECT_TEAM_COORDINATOR'",
  "'SUPER_ADMINISTRATOR'",
  'Module001BTimeReallocationPortal',
  'hasAnyEffectiveRole(authority, MODULE001B_ROLES)'
], 'Independent Module 001B gate');

requireAll(module001bPortal, [
  "import './module001b-time-reallocation.css';",
  "moduleNumber: '001B'",
  'Time Reallocation &amp; Corrections',
  'Reallocate time',
  'No worker resubmission, Manager approval, or Project Manager approval is required.',
  '/api/runtime/timesheet/steward/001b/reallocation/entries/',
  "destinationType: 'assignment'",
  "destinationType: 'project_task'",
  "destinationType: 'non_project'",
  'Create new billable / non-billable task',
  '/api/timesheet/ptc/tasks'
], 'Module 001B reallocation portal');

rejectAll(module001bPortal, [
  '../module001/ptc-guided-move.css',
  "moduleNumber: '001'"
], 'Module 001B frontend ownership');

requireAll(module001bCss, [
  '.module001b-shell',
  '.module001b-workspace',
  '.module001b-section',
  '.module001b-choice'
], 'Module 001B dedicated styling');

requireAll(module001bBackend, [
  'Module001BTimeReallocationRequest',
  'public Guid TargetUserId { get; init; }',
  'public string? DestinationType { get; init; }',
  'public Guid? AssignmentId { get; init; }',
  'public Guid? ProjectId { get; init; }',
  'public Guid? TaskId { get; init; }',
  'public Guid? NonProjectTimeCategoryId { get; init; }',
  'status = "target_user_required"',
  'submissionStatePreserved = true',
  'userMustResubmit = false',
  'managerApprovalRequired = false',
  'projectManagerApprovalRequired = false'
], 'Module 001B JSON binding and preservation contract');

rejectAll(module001bBackend, [
  'Module001PtcMoveV2Request request'
], 'Module 001B legacy request-model coupling');

requireAll(css, [
  '.ptc-time-steward-portal',
  '.ptc-no-submit',
  '.ptc-toolbar',
  '.ptc-user-summary',
  '.ptc-entry-table',
  '.ptc-modal'
], 'Module 001 base styling');

requireAll(runtimeCss, [
  '.ptc-select-user-prompt',
  '[data-projectpulse-react-owned-slot="true"]'
], 'Module 001 runtime ownership styling');

requireAll(model, [
  'PTC_TIME_STEWARD_ACTIONS',
  "'TIME_VIEW_ON_BEHALF'",
  "'TIME_UNSUBMIT'",
  "'TIME_CORRECT_ON_BEHALF'",
  "'TIME_DELETE_ON_BEHALF'",
  "'TIME_SUBMIT'",
  "'TIME_DELETE_PERMANENT'",
  'operationalTimeSteward',
  'does not submit a timesheet for another user'
], 'PTC permission model');

requireAll(main, [
  "import PtcTimeStewardGate from './module001/PtcTimeStewardGate.jsx';",
  "import Module001BTimeReallocationGate from './module001b/Module001BTimeReallocationGate.jsx';",
  '<PtcTimeStewardGate />',
  '<Module001BTimeReallocationGate />'
], 'Independent Module 001 / 001B mounts');

rejectAll(main, [
  "import PtcTimesheetManagementPortal from './module001/PtcTimesheetManagementPortal.jsx';",
  "import PtcRuntimeTaskCatalog from './module001/PtcRuntimeTaskCatalog.jsx';",
  '<PtcRuntimeTaskCatalog />'
], 'Main PTC ownership bypass');

if (portal.includes('Submit selected user') || portal.includes('Submit on behalf')) {
  throw new Error('Module 001 workspace must not contain a submission-on-behalf action.');
}

const externalAvailable = [
  backend, backendV2, resultExecution, runtimeBackend, boundary,
  migration, rollback, project
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
    'PROJECT_MANAGEMENT',
    'PROJECT_MANAGER',
    'RuntimePtcUsersAsync',
    'eligibleRoleCodes',
    'roleNames'
  ], 'Role-filtered eligible user source');

  requireAll(resultExecution, [
    'UseModule001ResultExecutionCompatibility',
    'RuntimePtcUsersAsync(context)',
    'RuntimePtcWorkspaceAsync(targetUserId, context)',
    'X-ProjectPulse-Module001-Result-Execution',
    'await result.ExecuteAsync(context);'
  ], 'Explicit Module 001 result execution');

  // The legacy backend endpoint remains compiled only as a 410-protected tombstone path.
  requireAll(backendV2, [
    'MapModule001TimeStewardV2Endpoints',
    '/api/runtime/timesheet/steward/v2/users',
    '/api/runtime/timesheet/steward/v2/users/{targetUserId:guid}/workspace',
    '/api/runtime/timesheet/steward/v2/entries/{timeEntryId:guid}/move',
    'TIME_REASSIGN',
    'submissionOnBehalf = false'
  ], 'Legacy server compatibility retained behind 410 boundary');

  requireAll(backend, [
    'MapModule001PtcTimesheetManagementEndpoints',
    '/weeks/{weekStart}/unsubmit',
    'app.MapPatch("/api/timesheet/ptc/entries/{timeEntryId:guid}"',
    '/remove"',
    'TIME_UNSUBMIT',
    'TIME_CORRECT_ON_BEHALF',
    'TIME_DELETE_ON_BEHALF',
    'canSubmitOnBehalf = false',
    'submissionOnBehalf = false',
    'scoped_time_management_events'
  ], 'Module 001 governed ordinary correction API');

  for (const forbidden of ['"TIME_SUBMIT"', 'TIME_DELETE_PERMANENT", actor', 'USER_IMPERSONATE", actor']) {
    if (backend.includes(forbidden) || backendV2.includes(forbidden)) {
      throw new Error(`PTC API must not execute protected action: ${forbidden}`);
    }
  }

  requireAll(migration, [
    '043_ptc_time_steward_permissions',
    'TIME_VIEW_ON_BEHALF',
    'TIME_UNSUBMIT',
    'TIME_DELETE_ON_BEHALF',
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
    'app.MapModule001PtcTimesheetManagementEndpoints();',
    'app.MapModule001BTimeReallocationEndpoints();'
  ], 'Backend registration');
} else {
  console.log('MODULE_001_PTC_EXTERNAL_SOURCE_CHECK=SKIPPED_MINIMAL_WEB_CONTEXT');
}

console.log('MODULE_001_PTC_TIME_STEWARD=PASS ordinaryTimeOnly=true module001bIndependent=true legacyMoveUi=false');
