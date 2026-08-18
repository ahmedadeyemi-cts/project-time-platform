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
  'OWNER_CATALOG_READ_THROUGH_FOR_AUTHENTICATED_USERS_V1',
  'MODULE_DIRECTORY_AUTHORITY_RETRY_MS',
  'projectpulse:auth-session-ready',
  'projectpulse:permissions-changed',
  'role_not_applicable',
  'authorization_pending'
].forEach((contract) => requireText(backgroundGate, contract, 'immediate module authority'));
rejectText(
  backgroundGate,
  "matches: (path) => path === '/api/module-catalog/owners'",
  'authenticated owner catalog reads must reach the backend'
);
rejectText(backgroundGate, "case 'owners':", 'owner catalog must not be replaced with an empty client payload');

const modulesPortal = read('src/frontend/project-time-web/src/ModulesDirectoryPortal.jsx');
[
  'MODULE_DIRECTORY_NONBLOCKING_AUTHORITY_V2',
  'directoryResolvedRef',
  'module_directory_unresolved_authority',
  'module_directory_route_activated',
  'directoryResolved={directoryResolved}'
].forEach((contract) => requireText(modulesPortal, contract, 'nonblocking Modules directory hydration'));

const moduleManagement = read('src/frontend/project-time-web/src/ModuleManagementTableView.jsx');
[
  'OWNER_CATALOG_READ_THROUGH_FOR_AUTHENTICATED_USERS_V1',
  'OWNER_LOAD_RETRY_DELAYS_MS',
  "window.addEventListener('projectpulse:auth-session-ready', refresh)",
  "window.addEventListener('projectpulse:permission-navigation-updated', refresh)",
  'Loading owner…',
  'Retrieving saved owner',
  '!directoryResolved ? ('
].forEach((contract) => requireText(moduleManagement, contract, 'owner catalog hydration'));
rejectText(
  moduleManagement,
  'if (!tableMode) return undefined;\n    void loadOwnership();',
  'owner metadata must load independently of visual layout mode'
);

const closeoutUi = read('src/frontend/project-time-web/src/EngineerTaskCloseoutCenter.jsx');
[
  'const PAGE_SIZE = 50;',
  'MODULE001A_VISIBLE_REQUEST_FAMILIES_V2',
  'compareCloseoutItems',
  'rightDate - leftDate',
  "'projectpulse:timesheet-work-queue-changed'",
  "'projectpulse:work-register-assignment-changed'",
  '.sort(compareCloseoutItems)'
].forEach((contract) => requireText(closeoutUi, contract, 'Module 001A visible assigned requests'));

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
  'module_catalog_reconciliation_093_owner_repair_evidence',
  'projectpulse093_required_owner_catalog',
  "('031', 'Financial Operations Workbench', 'financial-operations-workbench', 'Reports & Workflow')",
  "('032', 'Notification Delivery Monitor', 'notification-delivery-monitor', 'Reports & Workflow')",
  "('033', 'Project Forge', 'project-forge', 'Project Delivery')",
  'evidence.was_present = FALSE',
  'GROUP BY module.owner_user_id',
  'ORDER BY COUNT(*) DESC, module.owner_user_id',
  'module catalog repair did not restore active canonical row(s)',
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

const ownershipApi = read('src/backend/ProjectTime.Api/Modules/ModuleCatalogOwnershipModule.cs');
[
  'developer_super_administrator_only',
  'DeveloperOwnerRoleCodes',
  'IsDeveloperModuleOwnerAsync',
  'app_user_role_assignments owner_assignment',
  '@developer_owner_role_codes',
  'Only an actual developer Super Administrator session can change module ownership.',
  'The selected owner must be an active developer Super Administrator.'
].forEach((contract) => requireText(
  ownershipApi,
  contract,
  'developer Super Administrator module-owner policy'
));
rejectText(
  ownershipApi,
  'WHERE app_user.is_active = TRUE\n                    ORDER BY display_name, preferred_email',
  'owner candidates must not include ordinary active users'
);

const ownerCatalogRegression = read('tests/test-module-catalog-owner-repair-migration-093.sh');
[
  'owner_repair_target_count',
  'inserted_modules_inherit_default_owner',
  'preexisting_owner_preserved',
  'rerun_preserves_changed_owner',
  'repair_evidence_preserved'
].forEach((contract) => requireText(
  ownerCatalogRegression,
  contract,
  'Migration 093 owner-catalog executable regression'
));

const timesheet = read('src/backend/ProjectTime.Api/Modules/Module001TimesheetEnhancementModule.cs');
requireText(timesheet, 'FROM project_assignments pa', 'Module 001 canonical assignment source');
requireText(timesheet, 'pa.user_id = @user_id', 'Module 001 Engineer scope');

const closeout = read('src/backend/ProjectTime.Api/Modules/Module001AEngineerTaskCloseoutModule.cs');
requireText(closeout, 'FROM project_assignments pa', 'Module 001A canonical assignment source');
requireText(closeout, 'pa.user_id = @engineer_user_id', 'Module 001A Engineer scope');
requireText(closeout, 'pa.effective_start_date DESC', 'Module 001A recent assignment ordering');
requireText(closeout, "CASE WHEN p.project_code ~* '^(SR|PRES|INT)-' THEN p.project_code ELSE '' END", 'Module 001A durable request reference');

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
requireText(
  workflow,
  'tests/test-module-catalog-owner-repair-migration-093.sh',
  'focused CI owner-catalog regression path'
);
requireText(
  workflow,
  'bash tests/test-module-catalog-owner-repair-migration-093.sh',
  'focused CI owner-catalog regression execution'
);
requireText(
  workflow,
  'bash -n scripts/release-test/run-assigned-work-protected-test-uat.sh',
  'focused CI assigned-work UAT syntax validation'
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
  'requestWorkTypes.has(workType)',
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
  'module001aVisibleRequestReference:true',
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
console.log('module_owner_catalog_031_032_033_repaired=true');
console.log('timesheet_week_authority=sunday_through_saturday');
console.log('request_family_classification=sr_pres_int_durable_identifiers');
console.log('module001a_response_body=explicit_iresult');
console.log('module001a_request_visibility=recent_first_all_current_assignments');
console.log('module_directory_authority=nonblocking_identity_scoped_refresh');
console.log('module_owner_catalog=authenticated_read_through');
console.log('authenticated_assigned_work_uat=registered');
