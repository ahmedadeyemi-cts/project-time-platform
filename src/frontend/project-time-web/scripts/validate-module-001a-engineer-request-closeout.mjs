import fs from 'node:fs';
import path from 'node:path';

const root = path.resolve(path.dirname(new URL(import.meta.url).pathname), '../../../..');
const read = (relative) => fs.readFileSync(path.join(root, relative), 'utf8');
const failures = [];

function requireText(source, text, label) {
  if (!source.includes(text)) failures.push(`${label}: missing ${JSON.stringify(text)}`);
}

function requirePattern(source, pattern, label) {
  if (!pattern.test(source)) failures.push(`${label}: missing pattern ${pattern}`);
}

const migration = read('database/migrations/076_module_001a_engineer_request_closeout.sql');
const rollback = read('database/rollback/076_module_001a_engineer_request_closeout_rollback.sql');
const backend = read('src/backend/ProjectTime.Api/Modules/Module001AEngineerTaskCloseoutModule.cs');
const backendAvailability = read('src/backend/ProjectTime.Api/Modules/ModuleAvailabilityModule.cs');
const program = read('src/backend/ProjectTime.Api/Program.cs');
const timesheetData = read('src/backend/ProjectTime.Api/Modules/Module001TimesheetData.cs');
const timesheetModule = read('src/backend/ProjectTime.Api/Modules/Module001TimesheetEnhancementModule.cs');
const app = read('src/frontend/project-time-web/src/App.jsx');
const registry = read('src/frontend/project-time-web/src/module-availability-registry.js');
const ui = read('src/frontend/project-time-web/src/EngineerTaskCloseoutCenter.jsx');
const css = read('src/frontend/project-time-web/src/engineer-task-closeout-center.css');
const docs = read('docs/modules/module-001a-engineer-request-closeout/README.md');
const catalog = read('docs/MODULE-CATALOG.md');

for (const table of [
  'module001a_engineer_task_closeouts',
  'module001a_engineer_task_closeout_events'
]) requireText(migration, table, 'migration schema');

for (const status of ['engineer_closed', 'reopened', 'ptc_final_closed']) {
  requireText(migration, status, 'migration lifecycle');
}
requireText(migration, 'projectpulse076_block_closed_assignment_time', 'database billing lock');
requireText(migration, 'BEFORE INSERT OR UPDATE OF user_id, project_id, task_id, hours ON time_entries', 'database billing boundary');
requireText(migration, 'projectpulse076_immutable_closeout_event', 'immutable evidence');
requireText(migration, 'projectpulse076_finalize_project_closeouts', 'Module 055C project finalization');
requireText(migration, 'projectpulse076_finalize_task_closeout', 'Module 055C task finalization');
requireText(migration, 'VIEW_ENGINEER_TASK_CLOSEOUT_001A', 'view permission');
requireText(migration, 'MANAGE_OWN_ENGINEER_TASK_CLOSEOUT_001A', 'manage permission');
requireText(migration, "'#engineer-task-closeout'", 'feature registration');

requireText(rollback, 'Rollback refused: Module 001A closeout records exist.', 'guarded rollback');
requireText(rollback, 'Rollback refused: Module 001A immutable transition evidence exists.', 'immutable rollback guard');

for (const endpoint of [
  '/api/engineer-task-closeout/overview',
  '/api/engineer-task-closeout/assignments/{assignmentId:guid}/close',
  '/api/engineer-task-closeout/assignments/{assignmentId:guid}/reopen'
]) requireText(backend, endpoint, 'backend endpoint');
requireText(backend, 'pa.user_id = @engineer_user_id', 'own-assignment server scope');
requireText(backend, 'reason.Length < 10', 'required reopen reason');
requireText(backend, 'ptc_final_close_blocks_reopen', 'server-side final-close reopen guard');
requireText(backend, "recipient.Type == \"to\"", 'PTC To recipient');
requireText(backend, '"assigned_engineer",\n                "cc"', 'Engineer CC recipient');
requireText(backend, 'source_module, source_status', 'Module 065 dispatch source');
requireText(backend, "'001A', @source_status", 'Module 065 source identity');
requireText(backend, 'notification_dispatch_id,', 'immutable event notification evidence');
requireText(backend, 'UPDATE module001_weekly_task_lines', 'weekly-line removal');
if (/UPDATE module001a_engineer_task_closeout_events/.test(backend)) {
  failures.push('immutable evidence: backend must never update a closeout event after insert');
}

const billingProjection = "COALESCE(NULLIF(to_jsonb(pa)->>'module001a_closeout_status', ''), 'active') = 'active'";
requireText(program, billingProjection, 'available-task billing filter');
requireText(timesheetData, billingProjection, 'timer/task target billing filter');
requireText(timesheetModule, billingProjection, 'work-queue billing filter');
requireText(program, 'app.MapModule001AEngineerTaskCloseoutEndpoints();', 'backend registration');
requireText(backendAvailability, '["001A"] = Module("001A", "engineer-task-closeout", "Engineer Request Closeout", "Time Management")', 'backend availability registry');

requireText(ui, "import { usSignalLogoDataUrl }", 'official logo source');
requireText(ui, 'Engineer Request Closeout', 'enterprise UI title');
requireText(ui, "setTab('history')", 'Historical workflow');
requireText(ui, 'Reopen and notify', 'reopen interaction');
requireText(ui, 'Required reopen reason', 'reopen reason UX');
requireText(ui, 'Project Team Coordinator', 'PTC handoff UX');
requireText(ui, "projectpulse:timesheet-work-queue-changed", 'Module 001 refresh event');
requirePattern(css, /@media \(max-width: 720px\)/, 'responsive UI');
requireText(css, '.engineer-closeout-dialog-backdrop', 'accessible transition dialog presentation');

requireText(app, "import EngineerTaskCloseoutCenter from './EngineerTaskCloseoutCenter.jsx';", 'App UI import');
requireText(app, "route: 'engineer-task-closeout'", 'role navigation');
requireText(app, '<EngineerTaskCloseoutCenter />', 'route mount');
requireText(app, "window.addEventListener('projectpulse:timesheet-work-queue-changed'", 'timesheet live refresh');
requireText(registry, "moduleNumber: '001A'", 'availability registry');
requireText(catalog, '| 001A | Engineer Request Closeout |', 'module catalog');
requireText(docs, 'Module 055C remains the final request and task lifecycle authority', 'workflow documentation');

if (failures.length) {
  console.error('Module 001A Engineer Request Closeout validation failed:');
  failures.forEach((failure) => console.error(`- ${failure}`));
  process.exit(1);
}

console.log('Module 001A Engineer Request Closeout validation passed.');
