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

const paths = {
  portal: 'src/frontend/project-time-web/src/module001/PtcTimesheetManagementPortal.jsx',
  css: 'src/frontend/project-time-web/src/module001/ptc-timesheet-management.css',
  model: 'src/frontend/project-time-web/src/role-permission-model.js',
  backend: 'src/backend/ProjectTime.Api/Modules/Module001PtcTimesheetManagement.cs',
  migration: 'database/migrations/043_ptc_time_steward_permissions.sql',
  rollback: 'database/rollback/043_ptc_time_steward_permissions_rollback.sql',
  main: 'src/frontend/project-time-web/src/main.jsx',
  project: 'src/backend/ProjectTime.Api/ProjectTime.Api.csproj'
};

const [portal, css, model, backend, migration, rollback, main, project] = await Promise.all([
  text(paths.portal),
  text(paths.css),
  text(paths.model),
  optionalText(paths.backend),
  optionalText(paths.migration),
  optionalText(paths.rollback),
  text(paths.main),
  optionalText(paths.project)
]);

requireAll(portal, [
  'Project Team Coordinator · Time Steward',
  'Manage time for other users',
  'No submission on behalf',
  'The selected user reviews and submits the corrected week',
  'Return week to draft',
  'Edit entry',
  'Move time',
  'Create replacement task',
  'Remove draft entry',
  '/api/timesheet/ptc/users',
  '/unsubmit',
  '/api/timesheet/ptc/entries/',
  '/move',
  '/remove',
  '/api/timesheet/ptc/tasks',
  'Required reason',
  'immutable audit history',
  'submit week',
  'submit timesheet',
  'ptcSubmissionHidden',
  'user must review and submit it again'
], 'PTC time steward portal');

requireAll(css, [
  '.ptc-time-steward-portal',
  '.ptc-no-submit',
  '.ptc-toolbar',
  '.ptc-user-summary',
  '.ptc-entry-table',
  '.ptc-modal',
  '.ptc-entry-table button.danger',
  '.ptc-time-steward-active'
], 'PTC time steward styling');

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
  "import PtcTimesheetManagementPortal from './module001/PtcTimesheetManagementPortal.jsx';",
  '<PtcTimesheetManagementPortal />'
], 'Frontend mount');

if (portal.includes('Submit selected user') || portal.includes('Submit on behalf')) {
  throw new Error('PTC workspace must not contain a submission-on-behalf action.');
}

const externalAvailable = [backend, migration, rollback, project].every(Boolean);
if (externalAvailable) {
  requireAll(backend, [
    'MapModule001PtcTimesheetManagementEndpoints',
    'app.MapGet("/api/timesheet/ptc/users"',
    'app.MapGet("/api/timesheet/ptc/users/{targetUserId:guid}/entries"',
    '/weeks/{weekStart}/unsubmit',
    'app.MapPatch("/api/timesheet/ptc/entries/{timeEntryId:guid}"',
    '/move"',
    '/remove"',
    'app.MapPost("/api/timesheet/ptc/tasks"',
    'TIME_VIEW_ON_BEHALF',
    'TIME_UNSUBMIT',
    'TIME_CORRECT_ON_BEHALF',
    'TIME_REASSIGN',
    'TIME_DELETE_ON_BEHALF',
    'TIME_TASK_CREATE',
    'TIME_TASK_ASSIGN',
    'canSubmitOnBehalf = false',
    'submissionOnBehalf = false',
    'userMustResubmit = true',
    'scoped_time_management_events',
    'Timer-generated entries cannot be removed',
    "status='draft'",
    'project_assignments',
    'project_tasks'
  ], 'PTC time steward API');

  for (const forbidden of [
    '"TIME_SUBMIT"',
    'TIME_DELETE_PERMANENT", actor',
    'USER_IMPERSONATE", actor'
  ]) {
    if (backend.includes(forbidden)) throw new Error(`PTC API must not execute protected action: ${forbidden}`);
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
    "'ORGANIZATION'",
    "'TIME_SUBMIT'",
    "'TIME_DELETE_PERMANENT'",
    "'USER_IMPERSONATE'",
    "'SYSTEM_CONFIGURE'",
    "'DENY'",
    'submitOnBehalfAllowed',
    'immutableAuditRequired',
    'projectpulse040_block_immutable_audit_mutation',
    "source_name = '043_ptc_time_steward_permissions'",
    "policy_status = 'RETIRED'",
    "policy_status = 'PUBLISHED'"
  ], 'Migration 043');

  requireAll(rollback, [
    'Rollback 043 is blocked because PTC time-management audit evidence exists.',
    'Rollback 043 is blocked because later policy versions exist.',
    "source_name = '043_ptc_time_steward_permissions'",
    'DROP TRIGGER IF EXISTS trg_projectpulse040_published_grants_immutable',
    'DELETE FROM scoped_role_policy_versions',
    'DROP TABLE IF EXISTS scoped_time_management_events',
    "migration_id = '043_ptc_time_steward_permissions'"
  ], 'Migration 043 rollback');

  requireAll(project, ['app.MapModule001PtcTimesheetManagementEndpoints();'], 'Backend registration');
} else {
  console.log('MODULE_001_PTC_EXTERNAL_SOURCE_CHECK=SKIPPED_MINIMAL_WEB_CONTEXT');
}

console.log('Module 001 Project Team Coordinator time steward contracts passed.');
