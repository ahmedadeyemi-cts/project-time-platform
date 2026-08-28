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

const migration = read('database/migrations/078_module_001a_engineer_request_closeout.sql');
const rollback = read('database/rollback/078_module_001a_engineer_request_closeout_rollback.sql');
const catalogMigration = read('database/migrations/089_module_catalog_role_administration_reconciliation.sql');
const module001bCatalogMigration = read('database/migrations/098_module001b_role_catalog_registration.sql');
const module001bCatalogRollback = read('database/rollback/098_module001b_role_catalog_registration_rollback.sql');
const catalogRegistrationSource = [catalogMigration, module001bCatalogMigration].join('\n');
const catalogRollback = read('database/rollback/089_module_catalog_role_administration_reconciliation_rollback.sql');
const backend = read('src/backend/ProjectTime.Api/Modules/Module001AEngineerTaskCloseoutModule.cs');
const notificationRepository = read('src/backend/ProjectTime.Api/Modules/EnterpriseNotificationRepository.cs');
const backendAvailability = read('src/backend/ProjectTime.Api/Modules/ModuleAvailabilityModule.cs');
const program = read('src/backend/ProjectTime.Api/Program.cs');
const timesheetData = read('src/backend/ProjectTime.Api/Modules/Module001TimesheetData.cs');
const timesheetModule = read('src/backend/ProjectTime.Api/Modules/Module001TimesheetEnhancementModule.cs');
const app = read('src/frontend/project-time-web/src/App.jsx');
const registry = read('src/frontend/project-time-web/src/module-availability-registry.js');
const rolePermissionModel = read('src/frontend/project-time-web/src/role-permission-model.js');
const navigationPolicy = read('src/frontend/project-time-web/src/module-navigation-access-policy.js');
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
requireText(migration, 'projectpulse078_block_closed_assignment_time', 'database billing lock');
requireText(migration, 'BEFORE INSERT OR UPDATE OF user_id, project_id, task_id, hours ON time_entries', 'database billing boundary');
requireText(migration, 'projectpulse078_immutable_closeout_event', 'immutable evidence');
requireText(migration, 'projectpulse078_finalize_project_closeouts', 'Module 055C project finalization');
requireText(migration, 'projectpulse078_finalize_task_closeout', 'Module 055C task finalization');
requireText(migration, 'VIEW_ENGINEER_TASK_CLOSEOUT_001A', 'view permission');
requireText(migration, 'MANAGE_OWN_ENGINEER_TASK_CLOSEOUT_001A', 'manage permission');
requireText(migration, "'#engineer-task-closeout'", 'feature registration');

const registryModules = [...registry.matchAll(
  /Object\.freeze\(\{\s*moduleNumber:\s*'([^']+)'\s*,\s*route:\s*'([^']+)'\s*,\s*displayName:\s*'([^']+)'\s*,\s*group:\s*'([^']+)'/gs
)].map((match) => ({ moduleCode: match[1].toUpperCase(), route: match[2], moduleName: match[3], group: match[4] }));
if (registryModules.length < 70) {
  failures.push(`module catalog reconciliation: expected at least 70 canonical modules, found ${registryModules.length}`);
}
if (new Set(registryModules.map((module) => module.moduleCode)).size !== registryModules.length) {
  failures.push('module catalog reconciliation: canonical module numbers must be unique');
}
const sqlQuote = (value) => String(value).replaceAll("'", "''");
for (const module of registryModules) {
  requireText(
    catalogRegistrationSource,
    `('${sqlQuote(module.moduleCode)}', '${sqlQuote(module.moduleName)}', '${sqlQuote(module.route)}', '${sqlQuote(module.group)}')`,
    `Role Administration catalog registration for Module ${module.moduleCode}`
  );
}
for (const roleCode of ['ENGINEER', 'ENGINEERING', 'ENGINEERING_LEAD', 'ENGINEERING_TEAM_LEAD']) {
  requireText(catalogMigration, `('${roleCode}', 'VIEW_ENGINEER_TASK_CLOSEOUT_001A')`, `${roleCode} view permission repair`);
  requireText(catalogMigration, `('${roleCode}', 'MANAGE_OWN_ENGINEER_TASK_CLOSEOUT_001A')`, `${roleCode} manage permission repair`);
}
for (const roleCode of ['ENGINEERING', 'ENGINEERING_LEAD']) {
  requireText(catalogMigration, `('${roleCode}', 'MODULE_ACCESS', 'ORGANIZATION', FALSE)`, `${roleCode} module access grant`);
  requireText(catalogMigration, `('${roleCode}', 'WORKFLOW_MANAGE', 'SELF', FALSE)`, `${roleCode} self-scoped closeout workflow grant`);
}
requireText(catalogMigration, 'migration_089_module_catalog_role_administration_reconciliation', 'immutable policy source');
requireText(catalogMigration, "'allowedWorkTypes', jsonb_build_array('SERVICE_REQUEST', 'PRESALES', 'INTERNAL')", 'eligible request types');
requireText(catalogMigration, 'engineerOwnedOnly', 'own-assignment policy evidence');
requireText(catalogRollback, 'Rollback 089 refused: a newer scoped role-policy version', 'guarded policy rollback');
requireText(module001bCatalogMigration, "('001B', 'Time Reallocation & Corrections', 'time-reallocation', 'Time Management')", 'Module 001B Role Administration catalog registration');
requireText(module001bCatalogMigration, '098_module001b_role_catalog_registration', 'Module 001B catalog migration identity');
requireText(module001bCatalogMigration, 'Project Team Coordinator and Super Administrator only', 'Module 001B catalog authorization boundary');
requireText(module001bCatalogRollback, 'Rollback 098 refused: active scoped role-policy grants exist for Module 001B.', 'Module 001B guarded catalog rollback');
requireText(rolePermissionModel, "'001A': {", 'Module 001A intuitive permission preset');
requireText(rolePermissionModel, "actions = [...new Set(['MODULE_ACCESS', ...actions])]", 'non-No Access presets grant module visibility');
requireText(rolePermissionModel, "actionCode === 'MODULE_ACCESS' ? 'ORGANIZATION' : scope", 'organization module-access scope');
requireText(navigationPolicy, "['MODULE_ACCESS', 'MODULE_VIEW'].includes(actionCode)", 'legacy published Module View visibility compatibility');

requireText(rollback, 'Rollback refused: Module 001A closeout records exist.', 'guarded rollback');
requireText(rollback, 'Rollback refused: Module 001A immutable transition evidence exists.', 'immutable rollback guard');

for (const endpoint of [
  '/api/engineer-task-closeout/overview',
  '/api/engineer-task-closeout/assignments/{assignmentId:guid}/close',
  '/api/engineer-task-closeout/assignments/{assignmentId:guid}/reopen'
]) requireText(backend, endpoint, 'backend endpoint');
requireText(backend, 'pa.user_id = @engineer_user_id', 'own-assignment server scope');
for (const roleCode of ['ENGINEER', 'ENGINEERING', 'ENGINEERING_LEAD', 'ENGINEERING_TEAM_LEAD']) {
  requireText(backend, `"${roleCode}"`, `${roleCode} runtime access`);
}
for (const normalizedWorkType of ["'servicerequest'", "'presales'", "'internal'"]) {
  requireText(backend, normalizedWorkType, `${normalizedWorkType} closeout eligibility`);
}
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
requireText(backendAvailability, '["001B"] = Module("001B", "time-reallocation", "Time Reallocation & Corrections", "Time Management")', 'Module 001B backend availability registry');

requireText(ui, "import { usSignalLogoDataUrl }", 'official logo source');
requireText(ui, 'Engineer Request Closeout', 'enterprise UI title');
requireText(ui, "setTab('history')", 'Historical workflow');
requireText(ui, 'Reopen and notify', 'reopen interaction');
requireText(ui, 'Required reopen reason', 'reopen reason UX');
requireText(ui, 'Project Team Coordinator', 'PTC handoff UX');
requireText(ui, "projectpulse:timesheet-work-queue-changed", 'Module 001 refresh event');
requireText(ui, 'function sessionHeaders(authSession', 'authenticated API requests');
requireText(ui, "'X-ProjectPulse-Session': token", 'session token header');
requireText(ui, 'const PAGE_SIZE = 20', 'bounded task pagination');
requireText(ui, 'visibleItems.map', 'paginated task rendering');
requirePattern(css, /@media \(max-width: 720px\)/, 'responsive UI');
requireText(css, '.engineer-closeout-dialog-backdrop', 'accessible transition dialog presentation');
requireText(css, '.engineer-closeout-pagination', 'bounded task navigation styling');

requireText(app, "import EngineerTaskCloseoutCenter from './EngineerTaskCloseoutCenter.jsx';", 'App UI import');
requireText(app, "route: 'engineer-task-closeout'", 'role navigation');
requireText(app, '<EngineerTaskCloseoutCenter authSession={authSession} />', 'authenticated route mount');
requireText(app, "window.addEventListener('projectpulse:timesheet-work-queue-changed'", 'timesheet live refresh');
requireText(app, 'const results = await Promise.allSettled([', 'independent core data loading');
requireText(app, "'customer-delivery-acceptance',\n        'engineer-task-closeout',\n        'lab-equipment-tracker'", 'standalone Module 001A route boundary');
requirePattern(
  notificationRepository,
  /await using \(var reader = await command\.ExecuteReaderAsync\(cancellationToken\)\)[\s\S]*?while \(await reader\.ReadAsync\(cancellationToken\)\)[\s\S]*?\}\s*await transaction\.CommitAsync/,
  'notification reader closes before transaction commit'
);
requireText(registry, "moduleNumber: '001A'", 'availability registry');
requireText(registry, "moduleNumber: '001B'", 'Module 001B availability registry');
requireText(catalog, '| 001A | Engineer Request Closeout |', 'module catalog');
requireText(docs, 'Module 055C remains the final request and task lifecycle authority', 'workflow documentation');

if (failures.length) {
  console.error('Module 001A Engineer Request Closeout validation failed:');
  failures.forEach((failure) => console.error(`- ${failure}`));
  process.exit(1);
}

console.log('Module 001A Engineer Request Closeout validation passed.');