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
const generator = read('src/frontend/project-time-web/scripts/generate-module-001-integrated-app.mjs');
const main = read('src/frontend/project-time-web/src/main.jsx');
const authoritative = read('src/frontend/project-time-web/src/projectpulse-authoritative-api.js');
const portalEntry = read('src/frontend/project-time-web/src/module001/TimesheetEnhancementPortal.jsx');
const portal = read('src/frontend/project-time-web/src/module001/TimesheetEnhancementPortalV2.jsx');
const timerView = read('src/frontend/project-time-web/src/module001/TimesheetTimerView.jsx');
const timerRecovery = read('src/frontend/project-time-web/src/module001/Module001ActiveTimerRecoveryPortal.jsx');
const taskPicker = read('src/frontend/project-time-web/src/module001/TimesheetTaskPicker.jsx');
const durationSource = read('src/frontend/project-time-web/src/module001/timesheet-duration.js');
const css = read('src/frontend/project-time-web/src/module001/timesheet-prep.css');
const timerRecoveryCss = read('src/frontend/project-time-web/src/module001/timesheet-timer-recovery.css');
const persistentRecoveryCss = read('src/frontend/project-time-web/src/module001/module001-active-timer-recovery.css');
const uatCss = read('src/frontend/project-time-web/src/module001/module001-uat-fixes.css');
const packageJson = read('src/frontend/project-time-web/package.json');

for (const view of ['Weekly Grid', 'Daily Focus', 'Quick Entry List']) requireText(generated, view, 'streamlined Timesheet view');
requireText(portalEntry, './TimesheetEnhancementPortalV2.jsx', 'timer portal implementation');
requireText(portal, 'Start / Stop Timer', 'timer Timesheet view');
requireText(app, "route: 'timesheet'", 'Module 001 route');
requireText(app, "title: 'Timesheet'", 'Module 001 name');
requireText(generator, 'buildTimesheetPayload()', 'shared weekly draft');
requireText(generator, 'projectpulse:module001-state', 'canonical state event');
requireText(generator, 'assignedTasks: assignedOpenTasks', 'canonical assigned-task source');
requireText(generator, 'nonProjectCategories: categories', 'canonical non-project source');
requireText(main, './App.Module001.g.jsx', 'generated App import');
requireText(main, "import './projectpulse-authoritative-api.js';", 'wrapper-independent authoritative client mount');
requireText(main, '<TimesheetEnhancementPortal />', 'portal root integration');
requireText(main, "import Module001ActiveTimerRecoveryPortal from './module001/Module001ActiveTimerRecoveryPortal.jsx';", 'persistent timer recovery import');
requireText(main, '<Module001ActiveTimerRecoveryPortal />', 'persistent timer recovery mount');
requireText(main, './module001/module001-uat-fixes.css', 'Module 001 UAT repair styling');

requireText(authoritative, 'new XMLHttpRequest()', 'wrapper-independent transport');
requireText(authoritative, "const DIAGNOSTIC_MARKER = 'projectpulse-authoritative-xhr-v1'", 'authoritative diagnostic marker');
requireText(authoritative, 'requiredCollections', 'authoritative collection validation');
requireText(authoritative, '__projectPulseAuthoritativeApiDiagnostics', 'authoritative diagnostics storage');
rejectText(authoritative, 'window.fetch(', 'wrapper-independent transport');

requireText(portalEntry, "const TIMER_TARGET_CLIENT = 'projectpulse-timer-target-direct-fetch-v1';", 'direct timer-target client marker');
requireText(portalEntry, 'function isTimesheetRoute()', 'route-scoped target loading');
requireText(portalEntry, "=== 'timesheet'", 'Timesheet-only target loading');
requireText(portalEntry, 'async function loadTimerTargets(weekStart)', 'direct timer target request');
requireText(portalEntry, '/api/timesheet/timers/targets?weekStart=', 'authoritative timer target endpoint');
requireText(portalEntry, "'X-ProjectPulse-Module-Number': '001'", 'explicit Module 001 attribution');
requireText(portalEntry, "'X-ProjectPulse-Timer-Target-Client': TIMER_TARGET_CLIENT", 'direct target client evidence');
requireText(portalEntry, 'normalizeTimerTargetPayload(payload)', 'timer target collection validation');
requireText(portalEntry, "target.targetType === 'assignment'", 'assigned target extraction');
requireText(portalEntry, 'mergeByKey(snapshot.assignedTasks, authoritativeAssignments)', 'assigned task preservation');
requireText(portalEntry, 'snapshot.nonProjectCategories', 'non-project preservation');
requireText(portalEntry, "target.targetType === 'category' || target.targetType === 'categoryCode'", 'non-project target extraction');
requireText(portalEntry, "target.groupLabel === 'Service Request Tasks' || target.groupLabel === 'Requests / Service Requests'", 'service-request collection');
requireText(portalEntry, 'regularAssignedTasks', 'regular-task collection');
requireText(portalEntry, 'requestAssignedTasks', 'request-task collection');
requireText(portalEntry, 'timerTargetCounts', 'authoritative target counts');
requireText(portalEntry, 'timerTargetAuthoritativeSources', 'authoritative target evidence');
requireText(portalEntry, 'timerTargetLoadError', 'visible timer target failure evidence');
requireText(portalEntry, 'Existing Timesheet activities remain available', 'fail-safe target preservation');
requireText(portalEntry, 'projectpulse:module001-timer-targets', 'timer target state evidence');
requireText(portalEntry, 'synchronizeViewButtons', 'single active Timesheet view');
requireText(portalEntry, "button.setAttribute('aria-selected', active ? 'true' : 'false')", 'active view accessibility state');
rejectText(portalEntry, "import { authoritativeApi } from '../projectpulse-authoritative-api.js';", 'retired XHR timer target transport');
rejectText(portalEntry, 'new MutationObserver', 'route-wide target DOM observation');
rejectText(portalEntry, '/api/assignments/available-tasks', 'retired available-task enrichment');
rejectText(portalEntry, '/api/timesheet/work-queue', 'retired work-queue enrichment');

for (const contract of ['/api/timesheet/timers/active', '/api/timesheet/timers/start', '/api/timesheet/timers/start-by-code', '/stop', '/discard']) {
  requireText(`${portal}\n${timerView}\n${timerRecovery}`, contract, 'timer frontend contract');
}
requireText(portal, 'snapshot?.assignedTasks', 'timer assigned-task source');
requireText(portal, 'snapshot?.nonProjectCategories', 'timer non-project source');
requireText(portal, 'categoryTarget', 'category normalization');
requireText(portal, "targetType: 'categoryCode'", 'category-code construction');
requireText(portal, 'nonProjectCategoryCode: target.targetCode', 'category-code start payload');
requireText(portal, 'assignmentId: target.targetType ===', 'assignment timer start payload');
requireText(portal, 'window.setInterval(refresh, 5000)', 'active timer polling');
requireText(portal, "error?.status === 409", 'running-timer recovery');
requireText(portal, "groupLabel: isServiceRequestTask(task) ? 'Service Request Tasks' : 'Regular Tasks'", 'assigned task grouping');
requireText(portal, "groupLabel: 'Non-Project Time'", 'non-project grouping');
requireText(portal, 'projectPulseModule001MobileMode', 'mobile preference');
requireText(portal, 'Mobile mode', 'mobile selector');

requireText(timerRecovery, "import { authoritativeApi } from '../projectpulse-authoritative-api.js';", 'recovery authoritative client');
requireText(timerRecovery, "document.querySelector('#timesheet')", 'recovery actual Timesheet root');
requireText(timerRecovery, '/api/timesheet/timers/active', 'snapshot-independent active timer recovery');
requireText(timerRecovery, 'window.setInterval(() => void load(), 5000)', 'recovery polling');
requireText(timerRecovery, 'window.setInterval(() => setClock(new Date()), 1000)', 'recovery live clock');
requireText(timerRecovery, 'Timer status check failed', 'visible recovery failure');
requireText(timerRecovery, 'Try timer check again', 'recovery retry action');
requireText(timerRecovery, 'Running timer recovered', 'recovery banner');
requireText(timerRecovery, 'Timer automatically stopped', 'auto-stop evidence');
requireText(timerRecovery, 'Stop timer', 'recovery stop action');
requireText(timerRecovery, 'Discard', 'recovery discard action');
rejectText(timerRecovery, "document.querySelector('#timesheet.timesheet-page')", 'overly strict timer root selector');
rejectText(timerRecovery, 'const response = await fetch(path', 'wrapped recovery fetch');
requireText(persistentRecoveryCss, '.module001-active-timer-recovery', 'recovery surface styling');
requireText(persistentRecoveryCss, '.module001-active-timer-recovery-clock', 'recovery clock styling');

requireText(timerView, 'Ready to start', 'explicit timer pre-start state');
requireText(timerView, 'Select Start timer to begin the live clock.', 'timer action guidance');
requireText(timerView, 'setClock(new Date())', 'immediate live clock reset');
requireText(timerView, 'window.setInterval(() => setClock(new Date()), 1000)', 'one-second live clock');
requireText(uatCss, '#timesheet.module001-timer-mode .timesheet-view-button:not(#module001-start-stop-tab)', 'inactive view override');
requireText(uatCss, '#timesheet.module001-timer-mode #module001-start-stop-tab', 'active timer view styling');
requireText(uatCss, '.module001-ready-to-start', 'timer ready-state styling');

requireText(taskPicker, 'role="combobox"', 'autocomplete combobox');
requireText(taskPicker, 'aria-autocomplete="list"', 'autocomplete mode');
requireText(taskPicker, 'Regular Tasks', 'regular task group');
requireText(taskPicker, 'Service Request Tasks', 'service request group');
requireText(taskPicker, 'Non-Project Time', 'non-project group');
rejectText(taskPicker, '<select', 'legacy task selector');
requireText(timerRecoveryCss, '#timesheet.module001-timer-mode .module001-enhancement-view-host', 'timer host visibility');
requireText(timerRecoveryCss, '.module001-task-results', 'autocomplete panel styling');
requireText(css, '#timesheet.module001-mobile-mode', 'mobile presentation');
requireText(css, 'min-height: 44px', 'touch targets');

for (const contract of ['/api/timesheets/week/draft', '/validate-submission', '/submit', 'Module 002 Approval Inbox', 'Confirm and submit week']) {
  requireText(portal, contract, 'weekly submission frontend');
}
requireText(portal, 'snapshot.isViewAs', 'View-As read-only mode');
requireText(packageJson, 'validate:module001-enhancement', 'protected validator registration');

const duration = await import(pathToFileURL(path.join(webRoot, 'src/module001/timesheet-duration.js')).href);
const roundingCases = [
  [4 * 3600, 240], [4 * 3600 + 1, 255], [4 * 3600 + 14 * 60 + 59, 255],
  [4 * 3600 + 15 * 60, 255], [4 * 3600 + 15 * 60 + 1, 270], [1, 15],
  [11 * 3600 + 59 * 60 + 59, 720], [12 * 3600, 720], [13 * 3600, 720]
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
  requireText(projectFile, 'app.MapModule001TimerTargetEndpoints();', 'endpoint registration');
  requireText(migration, 'ux_module001_one_running_timer_per_user', 'one timer constraint');
  requireText(migration, 'rounded_minutes % 15 = 0', 'quarter-hour constraint');
  requireText(migration, 'module001_timer_audit_events', 'timer audit');
  requireText(rollback, 'rollback blocked', 'fail-closed rollback');
}

console.log(`MODULE_001_FUNCTIONAL_TIMER_TARGET_VALIDATION=PASS roundingCases=${roundingCases.length} backend=${backendAvailable ? 'full' : 'frontend-container'} directScopedTransport=true visibleRecoveryErrors=true targetCounts=true`);
