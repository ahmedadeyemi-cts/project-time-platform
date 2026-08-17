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
requireText(directoryAuthority, 'return authorizedModulesFromEffectiveNavigationState(projectModules, navigationState);', 'module directory loading fallback');
rejectText(directoryAuthority, "if (published.state !== 'ready') return [];", 'module directory must not convert loading to empty');

const effectiveRoleAuthority = read('src/frontend/project-time-web/src/effective-role-authority.js');
requireText(effectiveRoleAuthority, 'readEffectiveRoleAuthority', 'effective-role helper');
requireText(effectiveRoleAuthority, 'projectPulseViewAsUser', 'View-As role source');
requireText(effectiveRoleAuthority, '__projectPulseEffectiveNavigation', 'effective navigation role source');
requireText(effectiveRoleAuthority, 'projectPulseAuthSession', 'session role fallback');

const ptcGate = read('src/frontend/project-time-web/src/module001/PtcTimeStewardGate.jsx');
requireText(ptcGate, 'TIME_STEWARD_ROLES', 'PTC role boundary');
requireText(ptcGate, 'APPROVAL_ROLES', 'approval role boundary');
requireText(ptcGate, 'if (!authority.ready) return null;', 'PTC fail-closed loading state');
requireText(ptcGate, '{canStewardTime ? <PtcTimesheetManagementPortal /> : null}', 'PTC conditional mount');
rejectText(ptcGate, 'allowed: true', 'PTC gate must not default ordinary sessions to allowed');

const operations = read('src/frontend/project-time-web/src/ProductionOperationsPanel.jsx');
requireText(operations, 'OPERATIONAL_ROLES', 'production operations role list');
requireText(operations, '!authority.ready || !canViewOperations', 'production operations request gate');
requireText(operations, 'hasAnyEffectiveRole(authority, OPERATIONAL_ROLES)', 'production operations effective role check');

const backgroundGate = read('src/frontend/project-time-web/src/background-request-role-gate.js');
requireText(backgroundGate, "/api/module-catalog/owners", 'optional owner metadata gate');
requireText(backgroundGate, "/api/production/operations-acknowledgments/summary", 'operations acknowledgment gate');
requireText(backgroundGate, 'owners: []', 'non-admin owner response');

const main = read('src/frontend/project-time-web/src/main.jsx');
requireText(main, "import './background-request-role-gate.js';", 'background gate installation');

const migration = read('database/migrations/092_module_loading_assignment_visibility_repair.sql');
requireText(migration, '092_module_loading_assignment_visibility_repair', 'migration registration');
requireText(migration, 'ADD COLUMN IF NOT EXISTS owner_user_id', 'owner schema repair');
requireText(migration, 'projectpulse092_sync_work_register_assignment', 'assignment synchronization function');
requireText(migration, 'trg_projectpulse092_sync_work_register_assignment', 'assignment synchronization trigger');
requireText(migration, 'work_register_task_assignment_history', 'Work Register assignment authority');
requireText(migration, 'INSERT INTO project_assignments', 'canonical assignment insert/backfill');
requireText(migration, 'ON CONFLICT (project_id, task_id, user_id, effective_start_date)', 'idempotent canonical assignment upsert');
requireText(migration, "assignment_source = 'work_register_assignment_history'", 'canonical bridge ownership');
rejectText(migration, 'Ahmed.Adeyemi@ussignal.local', 'migration must remain environment neutral');

const timesheet = read('src/backend/ProjectTime.Api/Modules/Module001TimesheetEnhancementModule.cs');
requireText(timesheet, 'FROM project_assignments pa', 'Module 001 canonical assignment source');
requireText(timesheet, 'pa.user_id = @user_id', 'Module 001 Engineer scope');

if (failures.length) {
  console.error('MODULE_LOADING_ASSIGNMENT_PROPAGATION=FAIL');
  failures.forEach((failure) => console.error(`- ${failure}`));
  process.exit(1);
}

console.log('MODULE_LOADING_ASSIGNMENT_PROPAGATION=PASS');
console.log('authorized_modules_loading_fallback=true');
console.log('unauthorized_background_requests_suppressed=true');
console.log('work_register_assignments_canonicalized=true');
