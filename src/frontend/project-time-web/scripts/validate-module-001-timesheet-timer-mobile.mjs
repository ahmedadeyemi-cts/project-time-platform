import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

const webRoot = path.resolve(path.dirname(new URL(import.meta.url).pathname), '..');
const repoRoot = path.resolve(webRoot, '..', '..', '..');
const read = (relative, required = true) => {
  const absolute = path.join(repoRoot, relative);
  if (!fs.existsSync(absolute)) {
    if (required) throw new Error(`Missing required Module 001 source: ${relative}`);
    return '';
  }
  return fs.readFileSync(absolute, 'utf8');
};
const requireText = (source, value, label) => assert.ok(source.includes(value), `${label}: missing ${value}`);
const rejectText = (source, value, label) => assert.ok(!source.includes(value), `${label}: forbidden ${value}`);

const app = read('src/frontend/project-time-web/src/App.jsx');
const generated = read('src/frontend/project-time-web/src/App.Module001.g.jsx');
const generatedGuide = read('src/frontend/project-time-web/src/SystemUserGuide.Module001.g.jsx');
const generator = read('src/frontend/project-time-web/scripts/generate-module-001-integrated-app.mjs');
const main = read('src/frontend/project-time-web/src/main.jsx');
const portalEntry = read('src/frontend/project-time-web/src/module001/TimesheetEnhancementPortal.jsx');
const portal = read('src/frontend/project-time-web/src/module001/TimesheetEnhancementPortalV2.jsx');
const timerView = read('src/frontend/project-time-web/src/module001/TimesheetTimerView.jsx');
const taskPicker = read('src/frontend/project-time-web/src/module001/TimesheetTaskPicker.jsx');
const durationSource = read('src/frontend/project-time-web/src/module001/timesheet-duration.js');
const css = read('src/frontend/project-time-web/src/module001/timesheet-prep.css');
const timerRecoveryCss = read('src/frontend/project-time-web/src/module001/timesheet-timer-recovery.css');
const packageJson = read('src/frontend/project-time-web/package.json');
const module002Validator = read('src/frontend/project-time-web/scripts/validate-module-002-approval-center.mjs');
const module059Validator = read('src/frontend/project-time-web/scripts/validate-module-059-global.mjs');

for (const view of ['Weekly Grid', 'Daily Focus', 'Quick Entry List']) {
  requireText(generated, view, 'streamlined Timesheet view');
}
requireText(portalEntry, "./TimesheetEnhancementPortalV2.jsx", 'timer portal entrypoint');
requireText(portal, 'Start / Stop Timer', 'timer Timesheet view');
rejectText(generated, "{ key: 'queue', label: 'My Work Queue'", 'retired My Work Queue tab');
rejectText(generated, "{ key: 'calendar', label: 'Calendar / Timeline'", 'retired Calendar tab');
requireText(generator, "setTimesheetView('weekly')", 'legacy hidden-view reset');
requireText(generator, 'retiredTabsRemoved=2', 'retired-tab generation evidence');
rejectText(generatedGuide, 'My Work Queue loads actual tasks', 'retired Queue guide workflow');
rejectText(generatedGuide, 'Calendar / Timeline shows', 'retired Calendar guide workflow');
requireText(app, "route: 'timesheet'", 'Module 001 route');
requireText(app, "title: 'Timesheet'", 'Module 001 user-facing name');

requireText(generator, 'buildTimesheetPayload()', 'shared canonical weekly draft');
requireText(generator, 'projectpulse:module001-state', 'canonical state event');
requireText(generator, 'assignedTasks: assignedOpenTasks', 'canonical assigned-task source');
requireText(generator, 'nonProjectCategories: categories', 'canonical non-project source');
rejectText(generator, 'assignedTasks.data', 'undefined assignedTasks reference');
rejectText(generator, 'nonProjectCategories.data', 'undefined nonProjectCategories reference');
requireText(app, 'const assignedOpenTasks = openTasks.data?.tasks ?? [];', 'canonical assigned task declaration');
requireText(app, 'const categories = timesheet.data?.nonProjectCategories ?? [];', 'canonical non-project declaration');
requireText(generated, 'MODULE_001_CANONICAL_STATE_BRIDGE_START', 'generated App bridge');
requireText(generated, 'assignedTasks: assignedOpenTasks', 'generated assigned tasks');
requireText(generated, 'nonProjectCategories: categories', 'generated non-project categories');
requireText(main, './App.Module001.g.jsx', 'generated App import');
requireText(main, '<TimesheetEnhancementPortal />', 'portal root integration');

for (const contract of ['/api/timesheet/timers/active', '/api/timesheet/timers/start', '/api/timesheet/timers/start-by-code', '/stop', '/discard']) {
  requireText(`${portal}\n${timerView}`, contract, 'timer frontend contract');
}
requireText(portal, 'snapshot?.assignedTasks', 'timer assigned-task source');
requireText(portal, 'snapshot?.nonProjectCategories', 'timer visible category source');
requireText(portal, 'categoryTarget', 'category normalization');
requireText(portal, "targetType: 'categoryCode'", 'category-code target construction');
requireText(portal, "category-code:${code}", 'category-code selector value');
requireText(portal, 'nonProjectCategoryCode: target.targetCode', 'category-code start payload');
requireText(portal, 'Promise.allSettled', 'independent timer runtime loading');
requireText(portal, 'window.setInterval(refresh, 5000)', 'active-timer polling');
requireText(portal, "error?.status === 409", 'running-timer conflict recovery');
requireText(portal, 'error?.payload?.activeTimer', 'running-timer response adoption');
requireText(portal, "groupLabel: isServiceRequestTask(task) ? 'Service Request Tasks' : 'Regular Tasks'", 'assigned task grouping');
requireText(portal, "groupLabel: 'Non-Project Time'", 'non-project grouping');
rejectText(portal, '/api/timesheet/timers/targets?weekStart=', 'empty target endpoint dependency');
rejectText(portal, 'TimesheetWorkQueueCard', 'duplicate enhanced Queue implementation');
rejectText(portal, 'CalendarEnhancement', 'duplicate enhanced Calendar implementation');
rejectText(portal, 'CurrentTimesheetActivityCard', 'duplicate active-row Queue implementation');

requireText(taskPicker, 'type="search"', 'timer search input');
requireText(taskPicker, 'Search activity, task, project, customer, or request', 'timer search prompt');
requireText(taskPicker, 'Non-Project Time', 'non-project target group');
requireText(taskPicker, 'Regular Tasks', 'regular task group');
requireText(taskPicker, 'Service Request Tasks', 'service request group');
requireText(taskPicker, '<optgroup', 'grouped timer selector');
requireText(taskPicker, 'No authorized timer activity available', 'empty timer safeguard');
requireText(timerView, 'category-code:', 'category-code start eligibility');
requireText(timerView, 'validSelectedTarget', 'timer start eligibility');
requireText(timerView, "onClick={() => validSelectedTarget && onStart()}", 'guarded timer start');
requireText(timerView, './timesheet-timer-recovery.css', 'timer recovery layout import');
requireText(timerRecoveryCss, '#timesheet .timesheet-workspace > .module001-enhancement-view-host', 'inactive enhancement host');
requireText(timerRecoveryCss, 'display: none;', 'inactive host hidden');
requireText(timerRecoveryCss, '#timesheet.module001-timer-mode .module001-enhancement-view-host', 'timer host visibility');
requireText(timerRecoveryCss, 'grid-column: 1 / -1;', 'full-width timer host');
requireText(timerRecoveryCss, '.module001-task-search', 'timer search styling');
requireText(portal, 'projectPulseModule001MobileMode', 'mobile preference');
requireText(portal, 'Mobile mode', 'mobile selector label');
requireText(css, '#timesheet.module001-mobile-mode', 'mobile presentation');
requireText(css, 'min-height: 44px', 'touch targets');

for (const contract of ['/api/timesheets/week/draft', '/validate-submission', '/submit', 'Module 002 Approval Inbox', 'Confirm and submit week']) {
  requireText(portal, contract, 'weekly submission frontend');
}
requireText(portal, 'snapshot.isViewAs', 'View-As frontend read-only');
requireText(packageJson, 'validate:module001-enhancement', 'protected Module 001 validator registration');
requireText(packageJson, 'validate:module002', 'Module 002 validator preservation');
requireText(packageJson, 'validate:module059', 'Module 059 validator preservation');
assert.ok(module002Validator.length > 100, 'Module 002 validator must remain present');
assert.ok(module059Validator.length > 100, 'Module 059 global validator must remain present');

const duration = await import(pathToFileURL(path.join(webRoot, 'src/module001/timesheet-duration.js')).href);
const roundingCases = [
  [4 * 3600, 240], [4 * 3600 + 1, 255], [4 * 3600 + 14 * 60 + 59, 255],
  [4 * 3600 + 15 * 60, 255], [4 * 3600 + 15 * 60 + 1, 270], [1, 15],
  [11 * 3600 + 59 * 60 + 59, 720], [12 * 3600, 720], [13 * 3600, 720]
];
for (const [seconds, expectedMinutes] of roundingCases) {
  assert.equal(duration.roundSecondsUpToQuarterHour(seconds), expectedMinutes, `rounding ${seconds}`);
}
requireText(durationSource, 'MAX_TIMER_SECONDS', 'integer 12-hour cap');
requireText(durationSource, 'QUARTER_HOUR_SECONDS', 'integer quarter-hour duration');

const backendPaths = [
  'src/backend/ProjectTime.Api/Modules/Module001TimesheetContracts.cs',
  'src/backend/ProjectTime.Api/Modules/Module001TimesheetData.cs',
  'src/backend/ProjectTime.Api/Modules/Module001TimesheetTimerEngine.cs',
  'src/backend/ProjectTime.Api/Modules/Module001TimesheetSubmission.cs',
  'src/backend/ProjectTime.Api/Modules/Module001TimesheetEnhancementModule.cs',
  'src/backend/ProjectTime.Api/Modules/Module001TimerTargets.cs',
  'src/backend/ProjectTime.Api/ProjectTime.Api.csproj',
  'database/migrations/041_module_001_timesheet_timer_and_task_association.sql',
  'database/rollback/041_module_001_timesheet_timer_and_task_association_rollback.sql'
];
const backendAvailable = backendPaths.every((relative) => fs.existsSync(path.join(repoRoot, relative)));
if (backendAvailable) {
  const contracts = read(backendPaths[0]);
  const data = read(backendPaths[1]);
  const engine = read(backendPaths[2]);
  const submission = read(backendPaths[3]);
  const endpoints = read(backendPaths[4]);
  const timerTargets = read(backendPaths[5]);
  const projectFile = read(backendPaths[6]);
  const migration = read(backendPaths[7]);
  const rollback = read(backendPaths[8]);
  const allBackend = `${contracts}\n${data}\n${engine}\n${submission}\n${endpoints}\n${timerTargets}`;

  requireText(allBackend, 'ScopedAuthorizationEvaluator.EvaluateAsync', 'backend scoped authorization');
  requireText(allBackend, 'actor.EffectiveUserId', 'authenticated effective user');
  requireText(allBackend, 'TIME_EDIT_OWN', 'self-only edit action');
  requireText(allBackend, 'TIME_SUBMIT', 'submission action');
  requireText(allBackend, 'AutoStopModule001TimerAsync', 'server auto-stop');
  requireText(allBackend, 'Module001BuildSegments', 'midnight and week segmentation');
  requireText(allBackend, 'Module001RoundedMinutes', 'single authoritative rounding');
  requireText(allBackend, 'maximumDurationSeconds', 'server timer maximum response');
  requireText(data, 'if (forUpdate) sql += " FOR UPDATE OF t";', 'timer-row-only PostgreSQL lock');
  rejectText(data, 'if (forUpdate) sql += " FOR UPDATE";', 'outer-join-wide PostgreSQL lock');
  rejectText(endpoints, 'Module001TimerStartRequest(Guid UserId', 'browser-supplied timer identity');

  requireText(timerTargets, '/api/timesheet/timers/start-by-code', 'category-code start route');
  requireText(timerTargets, 'Module001TimerStartByCodeRequest', 'category-code request contract');
  requireText(timerTargets, 'UPPER(category_code) = @category_code', 'active category-code resolution');
  requireText(timerTargets, 'TIME_EDIT_OWN', 'category-code write authorization');
  requireText(timerTargets, 'new Module001TimerStartRequest(', 'shared timer-start execution');
  requireText(projectFile, 'app.MapModule001TimerTargetEndpoints();', 'generated Program endpoint registration');

  requireText(migration, 'ux_module001_one_running_timer_per_user', 'one running timer constraint');
  requireText(migration, 'rounded_minutes % 15 = 0', 'quarter-hour database constraint');
  requireText(migration, 'BETWEEN 0 AND 43200', '12-hour seconds constraint');
  requireText(migration, 'BETWEEN 0 AND 720', '12-hour rounded-minutes constraint');
  requireText(migration, 'module001_timer_audit_events', 'immutable timer audit');
  requireText(migration, 'module001_weekly_task_lines', 'durable weekly task association');
  requireText(rollback, 'rollback blocked', 'fail-closed rollback');
  requireText(rollback, 'DROP TABLE IF EXISTS module001_timer_sessions', 'reviewed rollback');
}

console.log(`MODULE_001_TIMESHEET_TIMER_MOBILE_VALIDATION=PASS roundingCases=${roundingCases.length} backend=${backendAvailable ? 'full' : 'frontend-container'} architecture=streamlined timerSource=visibleActivities searchableGroups=3 activeConflictRecovery=true`);
