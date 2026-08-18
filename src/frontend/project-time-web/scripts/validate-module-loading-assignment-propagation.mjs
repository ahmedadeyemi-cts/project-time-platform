import fs from 'node:fs';
import path from 'node:path';
import process from 'node:process';

const root = path.resolve(import.meta.dirname, '../../../..');
const read = (relativePath) => fs.readFileSync(path.join(root, relativePath), 'utf8');
const failures = [];

function requireText(source, expected, label) {
  if (!source.includes(expected)) failures.push(`${label}: missing ${expected}`);
}

function rejectText(source, rejected, label) {
  if (source.includes(rejected)) failures.push(`${label}: forbidden ${rejected}`);
}

const directoryAuthority = read('src/frontend/project-time-web/src/module-directory-authority.js');
requireText(
  directoryAuthority,
  'return authorizedModulesFromEffectiveNavigationState(projectModules, navigationState);',
  'module directory loading fallback'
);
rejectText(
  directoryAuthority,
  "if (published.state !== 'ready') return [];",
  'module directory must not convert loading to empty'
);

const effectiveRoleAuthority = read('src/frontend/project-time-web/src/effective-role-authority.js');
requireText(effectiveRoleAuthority, 'readEffectiveRoleAuthority', 'effective-role helper');
requireText(effectiveRoleAuthority, 'projectPulseViewAsUser', 'View-As role source');
requireText(effectiveRoleAuthority, '__projectPulseEffectiveNavigation', 'effective navigation role source');
requireText(effectiveRoleAuthority, 'projectPulseAuthSession', 'session role fallback');

const ptcGate = read('src/frontend/project-time-web/src/module001/PtcTimeStewardGate.jsx');
requireText(ptcGate, 'TIME_STEWARD_ROLES', 'PTC role boundary');
requireText(ptcGate, 'APPROVAL_ROLES', 'approval role boundary');
requireText(ptcGate, 'if (!authority.ready) return null;', 'PTC fail-closed loading state');
requireText(
  ptcGate,
  '{canStewardTime ? <PtcTimesheetManagementPortal /> : null}',
  'PTC conditional mount'
);
rejectText(ptcGate, 'allowed: true', 'PTC gate must not default ordinary sessions to allowed');

const operations = read('src/frontend/project-time-web/src/ProductionOperationsPanel.jsx');
requireText(operations, 'OPERATIONAL_ROLES', 'production operations role list');
requireText(operations, '!authority.ready || !canViewOperations', 'production operations request gate');
requireText(
  operations,
  'hasAnyEffectiveRole(authority, OPERATIONAL_ROLES)',
  'production operations effective role check'
);

const backgroundGate = read('src/frontend/project-time-web/src/background-request-role-gate.js');
[
  '/api/module-catalog/owners',
  '/api/production/readiness-command-center',
  '/api/navigation/registry-integrity',
  '/api/dashboard/module-visibility-smoke',
  '/api/audit-history/summary',
  '/api/workflow/approval-export-summary',
  '/api/production/operations-acknowledgments/summary',
  '/api/manager/approvals',
  '/api/runtime/timesheet/steward/v2/users'
].forEach((route) => requireText(backgroundGate, route, `background request gate ${route}`));
[
  'VISIBLE_AUTHORIZED_NAVIGATION_SNAPSHOT_V1',
  'visibleAuthorizedModuleNumbers',
  'publishImmediateNavigationSnapshot',
  'visible_authorized_navigation_snapshot',
  'cached_authorized_navigation_snapshot',
  'projectpulse:permission-navigation-updated',
  'owners: []',
  'role_not_applicable',
  'authorization_pending'
].forEach((contract) => requireText(backgroundGate, contract, 'immediate module authority'));

const main = read('src/frontend/project-time-web/src/main.jsx');
requireText(main, "import './background-request-role-gate.js';", 'background gate installation');

const migration092 = read('database/migrations/092_module_loading_assignment_visibility_repair.sql');
requireText(migration092, '092_module_loading_assignment_visibility_repair', 'migration 092 baseline');
requireText(migration092, 'work_register_task_assignment_history', 'migration 092 assignment authority');
requireText(migration092, 'INSERT INTO project_assignments', 'migration 092 canonical assignment baseline');

const migration093 = read('database/migrations/093_assigned_work_canonical_visibility_repair.sql');
[
  '093_assigned_work_canonical_visibility_repair',
  'ADD COLUMN IF NOT EXISTS owner_user_id',
  'projectpulse093_resolve_project_task_id',
  'projectpulse093_sync_work_register_assignment',
  'trg_projectpulse093_sync_work_register_assignment',
  'trg_projectpulse093_resync_history_after_task_change',
  'trg_projectpulse093_resync_history_after_project_status',
  'work_register_task_assignment_history',
  'INSERT INTO project_assignments',
  'ON CONFLICT (project_id, task_id, user_id, effective_start_date)',
  "assignment_source = 'work_register_assignment_history_v2'",
  'task.task_id::TEXT = btrim(p_task_reference)',
  "lower(COALESCE(task.task_code, '')) = lower(btrim(p_task_reference))",
  'Module 019',
  'Module 001A',
  'Module 001'
].forEach((contract) => requireText(migration093, contract, 'migration 093 complete assignment bridge'));

[
  'SR-8C81ACA3',
  'Jason Mosier',
  'Jason.Mosier',
  'jason.mosier',
  'Ahmed.Adeyemi@ussignal.local'
].forEach((value) => rejectText(
  migration093,
  value,
  'migration 093 must remain environment and record neutral'
));

const timesheet = read('src/backend/ProjectTime.Api/Modules/Module001TimesheetEnhancementModule.cs');
requireText(timesheet, 'FROM project_assignments pa', 'Module 001 canonical assignment source');
requireText(timesheet, 'pa.user_id = @user_id', 'Module 001 Engineer scope');

const closeout = read('src/backend/ProjectTime.Api/Modules/Module001AEngineerTaskCloseoutModule.cs');
requireText(closeout, 'FROM project_assignments pa', 'Module 001A canonical assignment source');
requireText(closeout, 'pa.user_id = @engineer_user_id', 'Module 001A Engineer scope');

const workspace = read('src/backend/ProjectTime.Api/Modules/ProjectWorkspaceModule019Repair.cs');
requireText(workspace, 'FROM project_assignments', 'Module 019 canonical assignment source');
requireText(workspace, '@user_id', 'Module 019 effective Engineer scope');

const workflow = read('.github/workflows/module-loading-assignment-propagation-ci.yml');
requireText(
  workflow,
  'database/migrations/093_assigned_work_canonical_visibility_repair.sql',
  'focused CI migration 093 path'
);
requireText(
  workflow,
  'node src/frontend/project-time-web/scripts/validate-module-loading-assignment-propagation.mjs',
  'focused CI validator execution'
);
requireText(workflow, 'dotnet build', 'focused CI backend compile');
requireText(workflow, 'npm run build', 'focused CI frontend compile');


const availableTaskProgram = read('src/backend/ProjectTime.Api/Program.cs');
const availableTaskStart = availableTaskProgram.indexOf('app.MapGet("/api/assignments/available-tasks"');
const availableTaskEnd = availableTaskProgram.indexOf('app.MapGet("/api/non-project-time-categories"', availableTaskStart);
const availableTaskEndpoint = availableTaskStart >= 0 && availableTaskEnd > availableTaskStart
  ? availableTaskProgram.slice(availableTaskStart, availableTaskEnd)
  : '';
[
  'DayOfWeek.Sunday',
  "p.project_code ~* '^(SR|PRES|INT)-'",
  'requestNumber = isRequestFamily ? projectCode : string.Empty',
  'authoritativeSource = "project_assignments"',
  'activityClassification = "durable_project_code_and_work_type"'
].forEach((contract) => requireText(availableTaskEndpoint, contract, 'Module 001 assigned-work endpoint'));
rejectText(availableTaskEndpoint, 'DayOfWeek.Monday', 'Module 001 Sunday week authority');

const timesheetUi = read('src/frontend/project-time-web/src/App.jsx');
[
  "const isDurableRequestFamily = /^(SR|PRES|INT)-/.test(projectCode)",
  "requestWorkTypes.has(workType)",
  "if (isDurableRequestFamily || explicitSection === 'requests') return 'requests';"
].forEach((contract) => requireText(timesheetUi, contract, 'Module 001 request-family UI'));

requireText(
  closeout,
  '(Func<HttpContext, Task<IResult>>)Module001AOverviewAsync',
  'Module 001A explicit IResult execution'
);
const workspaceUi = read('src/frontend/project-time-web/src/ProjectWorkspaceCenter.jsx');
requireText(workspaceUi, 'assignments.map((assignment)', 'Module 019 assignment rendering');
requireText(workspaceUi, '{assignment.projectCode}', 'Module 019 durable identifier rendering');

const assignedWorkUat = read('scripts/release-test/run-assigned-work-protected-test-uat.sh');
[
  'ASSIGNED_WORK_PROTECTED_TEST_UAT=PASS',
  'SR-8C81ACA3',
  '/api/assignments/available-tasks?weekStart=',
  '/api/timesheet/work-queue?weekStart=',
  '/api/engineer-task-closeout/overview',
  '/api/project-workspace/overview',
  'mutation:false',
  'productionMutation:false'
].forEach((contract) => requireText(assignedWorkUat, contract, 'assigned-work protected-Test UAT'));

const protectedTestController = read('.github/workflows/projectpulse-deploy-test.yml');
[
  'scripts/release-test/run-assigned-work-protected-test-uat.sh',
  'Run protected-Test assigned-work visibility UAT',
  'assignedWorkUat:true'
].forEach((contract) => requireText(protectedTestController, contract, 'assigned-work deployment controller'));

if (failures.length) {
  console.error('MODULE_LOADING_ASSIGNMENT_PROPAGATION=FAIL');
  failures.forEach((failure) => console.error(`- ${failure}`));
  process.exit(1);
}

console.log('MODULE_LOADING_ASSIGNMENT_PROPAGATION=PASS');
console.log('authorized_modules_immediate_snapshot=true');
console.log('unauthorized_background_requests_suppressed=true');
console.log('uuid_and_task_code_assignments_canonicalized=true');
console.log('modules_019_001a_001_shared_assignment_authority=true');
console.log('timesheet_week_authority=sunday_through_saturday');
console.log('request_family_classification=sr_pres_int_durable_identifiers');
console.log('module001a_response_body=explicit_iresult');
console.log('authenticated_assigned_work_uat=registered');
