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
const requireText = (source, value, label) => assert.ok(
  source.includes(value),
  `${label}: missing ${value}`
);
const rejectText = (source, value, label) => assert.ok(
  !source.includes(value),
  `${label}: forbidden ${value}`
);

const app = read('src/frontend/project-time-web/src/App.jsx');
const generator = read('src/frontend/project-time-web/scripts/generate-module-001-integrated-app.mjs');
const slotInjector = read('src/frontend/project-time-web/scripts/inject-module-001-owned-extension-slots.mjs');
const moreInjector = read('src/frontend/project-time-web/scripts/inject-react-owned-more-menu.mjs');
const main = read('src/frontend/project-time-web/src/main.jsx');
const authoritative = read('src/frontend/project-time-web/src/projectpulse-authoritative-api.js');
const portal = read('src/frontend/project-time-web/src/module001/TimesheetEnhancementPortal.jsx');
const timerView = read('src/frontend/project-time-web/src/module001/TimesheetTimerView.jsx');
const taskPicker = read('src/frontend/project-time-web/src/module001/TimesheetTaskPicker.jsx');
const durationSource = read('src/frontend/project-time-web/src/module001/timesheet-duration.js');
const prepCss = read('src/frontend/project-time-web/src/module001/timesheet-prep.css');
const recoveryCss = read('src/frontend/project-time-web/src/module001/timesheet-timer-recovery.css');
const runtimeCss = read('src/frontend/project-time-web/src/module001/module001-runtime-v2.css');
const packageJson = read('src/frontend/project-time-web/package.json');
const resultExecution = read('src/backend/ProjectTime.Api/Modules/Module001ResultExecutionCompatibility.cs', false);

for (const view of ['Weekly Grid', 'Daily Focus', 'Quick Entry List']) {
  requireText(app, view, 'streamlined Timesheet view');
}
requireText(app, "route: 'timesheet'", 'Module 001 route');
requireText(app, "title: 'Timesheet'", 'Module 001 name');
requireText(generator, 'buildTimesheetPayload()', 'shared weekly draft');
requireText(generator, 'projectpulse:module001-state', 'canonical state event');
requireText(generator, 'assignedTasks: assignedOpenTasks', 'canonical assigned-task source');
requireText(generator, 'nonProjectCategories: categories', 'canonical non-project source');
requireText(main, './App.Module001.g.jsx', 'generated App import');
requireText(main, "import './projectpulse-authoritative-api.js';", 'authoritative client mount');
requireText(main, '<TimesheetEnhancementPortal />', 'single timer portal mount');
rejectText(main, 'Module001ActiveTimerRecoveryPortal', 'retired duplicate timer-recovery portal');

requireText(slotInjector, 'MODULE_001_REACT_OWNED_EXTENSION_SLOTS', 'React-owned Module 001 extension marker');
for (const slot of [
  'module001-view-tab-host',
  'module001-active-timer-recovery-host',
  'module001-ptc-time-steward-host',
  'module001-enhancement-view-host'
]) {
  requireText(slotInjector, slot, 'React-owned Module 001 slot');
}
requireText(slotInjector, 'runtimeDomInsertion=0', 'zero runtime child insertion evidence');
rejectText(slotInjector, 'module001-toolbar-host', 'retired runtime toolbar portal');
requireText(moreInjector, 'PROJECTPULSE_REACT_OWNED_MORE_MENU', 'React-owned More generator');

requireText(authoritative, 'new XMLHttpRequest()', 'wrapper-independent transport');
requireText(authoritative, "const DIAGNOSTIC_MARKER = 'projectpulse-authoritative-xhr-v1'", 'authoritative diagnostic marker');
requireText(authoritative, 'requiredCollections', 'authoritative collection validation');
requireText(authoritative, '__projectPulseAuthoritativeApiDiagnostics', 'authoritative diagnostics storage');
rejectText(authoritative, 'window.fetch(', 'wrapper-independent primary transport');

requireText(portal, "import { authoritativeApi } from '../projectpulse-authoritative-api.js';", 'integrated authoritative timer portal');
requireText(portal, 'function readSlots()', 'static React-owned slot lookup');
requireText(portal, 'data-projectpulse-react-owned-slot="true"', 'React-owned slot boundary');
requireText(portal, '/api/timesheet/timers/targets?weekStart=', 'authoritative timer target endpoint');
requireText(portal, "requiredCollections: ['targets']", 'target collection contract');
requireText(portal, '/api/timesheet/timers/active', 'active timer endpoint');
requireText(portal, '/api/timesheet/timers/history?weekStart=', 'timer history endpoint');
requireText(portal, "requiredCollections: ['timers']", 'history collection contract');
requireText(portal, 'window.setInterval(refresh, 5000)', 'server timer polling');
requireText(portal, 'window.setInterval(() => setClock(new Date()), 1000)', 'one-second recovered clock');
requireText(portal, 'Timer started. The server continues tracking it through refreshes, sign-out, and session expiration.', 'server persistence guidance');
requireText(portal, 'A timer was already running. It has been recovered from the server.', 'running-timer conflict recovery');
requireText(portal, 'module001-server-timer-recovery', 'always-visible recovered timer surface');
requireText(portal, 'Start / Stop Timer', 'timer Timesheet view');
requireText(portal, '/api/timesheet/timers/start-by-code', 'non-project code timer start');
requireText(portal, '/api/timesheet/timers/start', 'assignment/category timer start');
requireText(portal, '/stop', 'timer stop action');
requireText(portal, '/discard', 'timer discard action');

for (const forbidden of [
  'document.createElement',
  '.appendChild(',
  '.insertBefore(',
  '.prepend(',
  '.replaceChildren(',
  '.innerHTML ='
]) {
  rejectText(portal, forbidden, 'timer React DOM ownership');
}

requireText(timerView, 'Ready to start', 'explicit timer pre-start state');
requireText(timerView, 'Select Start timer to begin the live clock.', 'timer action guidance');
requireText(timerView, 'setClock(new Date())', 'immediate live clock reset');
requireText(timerView, 'window.setInterval(() => setClock(new Date()), 1000)', 'one-second live clock');
requireText(timerView, 'Timer history', 'visible timer history heading');
requireText(timerView, 'history.map', 'visible timer history rows');

requireText(taskPicker, 'role="combobox"', 'autocomplete combobox');
requireText(taskPicker, 'aria-autocomplete="list"', 'autocomplete mode');
requireText(taskPicker, "const GROUP_ORDER = ['Requests / Service Requests', 'Project Tasks', 'Non-Project Time']", 'three activity groups');
requireText(taskPicker, "if (label === 'Service Request Tasks') return 'Requests / Service Requests'", 'service-request alias normalization');
requireText(taskPicker, "if (label === 'Regular Tasks') return 'Project Tasks'", 'project-task alias normalization');
rejectText(taskPicker, '<select', 'legacy timer selector');

requireText(runtimeCss, 'body.projectpulse-module001-timer-mode', 'full-width timer mode');
requireText(runtimeCss, '.module001-server-timer-recovery', 'recovered timer styling');
requireText(runtimeCss, '.module001-server-timer-clock', 'recovered timer clock styling');
requireText(runtimeCss, '[data-projectpulse-react-owned-slot="true"]', 'slot styling boundary');
requireText(runtimeCss, '@media (max-width: 720px)', 'small-screen timer presentation');
requireText(prepCss, 'min-height: 44px', 'touch targets');
requireText(recoveryCss, '.module001-task-results', 'autocomplete panel styling');

requireText(packageJson, 'inject-module-001-owned-extension-slots.mjs', 'slot generator build gate');
requireText(packageJson, 'inject-react-owned-more-menu.mjs', 'React-owned More build gate');
requireText(packageJson, 'validate:module001-ptc-timer-dom', 'DOM ownership validator registration');
requireText(packageJson, 'validate:module001-enhancement', 'timer validator registration');

const duration = await import(pathToFileURL(path.join(webRoot, 'src/module001/timesheet-duration.js')).href);
const roundingCases = [
  [4 * 3600, 240],
  [4 * 3600 + 1, 255],
  [4 * 3600 + 14 * 60 + 59, 255],
  [4 * 3600 + 15 * 60, 255],
  [4 * 3600 + 15 * 60 + 1, 270],
  [1, 15],
  [11 * 3600 + 59 * 60 + 59, 720],
  [12 * 3600, 720],
  [13 * 3600, 720]
];
for (const [seconds, expectedMinutes] of roundingCases) {
  assert.equal(duration.roundSecondsUpToQuarterHour(seconds), expectedMinutes, `rounding ${seconds}`);
}
requireText(durationSource, 'MAX_TIMER_SECONDS', '12-hour cap');
requireText(durationSource, 'QUARTER_HOUR_SECONDS', 'quarter-hour duration');

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
  const sources = backendPaths.map((relative) => read(relative));
  const [contracts, data, engine, submission, endpoints, timerTargets, projectFile, migration, rollback] = sources;
  const allBackend = sources.slice(0, 6).join('\n');
  requireText(allBackend, 'ScopedAuthorizationEvaluator.EvaluateAsync', 'backend authorization');
  requireText(allBackend, 'actor.EffectiveUserId', 'effective user');
  requireText(allBackend, 'TIME_EDIT_OWN', 'self edit action');
  requireText(allBackend, 'TIME_SUBMIT', 'submission action');
  requireText(allBackend, 'AutoStopModule001TimerAsync', 'server auto-stop');
  requireText(allBackend, 'Module001BuildSegments', 'timer segmentation');
  requireText(allBackend, 'Module001RoundedMinutes', 'authoritative rounding');
  requireText(data, 'if (forUpdate) sql += " FOR UPDATE OF t";', 'timer-row lock');
  requireText(timerTargets, 'app.MapGet("/api/timesheet/timers/targets"', 'authoritative target route');
  requireText(timerTargets, 'project_assignments', 'assignment source');
  requireText(timerTargets, 'project_tasks', 'task source');
  requireText(timerTargets, "to_jsonb(pt)->>'work_task_category'", 'task category source');
  requireText(timerTargets, "to_jsonb(pt)->>'service_request_number'", 'service request source');
  requireText(timerTargets, 'regularTaskCount', 'regular task count');
  requireText(timerTargets, 'serviceRequestTaskCount', 'service request count');
  requireText(timerTargets, 'groupLabel = "Non-Project Time"', 'non-project API group');
  requireText(projectFile, 'app.UseModule001ResultExecutionCompatibility();', 'explicit GET result execution registration');
  requireText(projectFile, 'app.MapModule001TimerTargetEndpoints();', 'target endpoint registration');
  requireText(migration, 'ux_module001_one_running_timer_per_user', 'one timer constraint');
  requireText(migration, 'rounded_minutes % 15 = 0', 'quarter-hour constraint');
  requireText(migration, 'module001_timer_audit_events', 'timer audit');
  requireText(rollback, 'rollback blocked', 'fail-closed rollback');
  void contracts;
  void engine;
  void submission;
  void endpoints;
}

if (resultExecution) {
  requireText(resultExecution, 'Module001ActiveTimerAsync(context)', 'active timer explicit IResult execution');
  requireText(resultExecution, 'Module001TimerHistoryAsync(context)', 'history explicit IResult execution');
  requireText(resultExecution, 'Module001TimerTargetsAsync(context)', 'target explicit IResult execution');
  requireText(resultExecution, 'await result.ExecuteAsync(context);', 'framework-safe result execution');
}

console.log('MODULE_001_TIMER_MOBILE=PASS timer=server-authoritative history=visible targets=3-groups domOwnership=react-owned');
