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
const portal = read('src/frontend/project-time-web/src/module001/TimesheetEnhancementPortal.jsx');
const timerView = read('src/frontend/project-time-web/src/module001/TimesheetTimerView.jsx');
const queueCard = read('src/frontend/project-time-web/src/module001/TimesheetWorkQueueCard.jsx');
const durationSource = read('src/frontend/project-time-web/src/module001/timesheet-duration.js');
const css = read('src/frontend/project-time-web/src/module001/timesheet-prep.css');
const packageJson = read('src/frontend/project-time-web/package.json');
const module002Validator = read('src/frontend/project-time-web/scripts/validate-module-002-approval-center.mjs');
const module059Validator = read('src/frontend/project-time-web/scripts/validate-module-059-global.mjs');

for (const view of ['Weekly Grid', 'Daily Focus', 'My Work Queue', 'Quick Entry List', 'Calendar / Timeline']) {
  requireText(app, view, 'existing Timesheet view preservation');
}
requireText(portal, 'Start / Stop Timer', 'sixth Timesheet view');
requireText(app, "route: 'timesheet'", 'Module 001 route');
requireText(app, "title: 'Timesheet'", 'Module 001 user-facing name');

requireText(generator, 'buildTimesheetPayload()', 'shared canonical weekly draft');
requireText(generator, 'canonicalCalendarEntries', 'shared Calendar projection');
requireText(generator, 'projectpulse:module001-state', 'canonical state event');
requireText(generator, 'projectpulse:module001-action', 'canonical state action');
requireText(generated, 'MODULE_001_CANONICAL_STATE_BRIDGE_START', 'generated App bridge');
requireText(generated, 'draftPayload: buildTimesheetPayload()', 'generated canonical payload');
requireText(generated, "./SystemUserGuide.Module001.g.jsx", 'generated user-guide import');
requireText(main, "./App.Module001.g.jsx", 'generated App import');
requireText(main, '<TimesheetEnhancementPortal />', 'portal root integration');
for (const guideContract of ['Start / Stop Timer', 'Mobile mode', 'Module 002 Approval Inbox', 'server-authoritative UTC timestamps']) {
  requireText(generatedGuide, guideContract, 'Module 999 Timesheet guide');
}

for (const contract of ['/api/timesheet/work-queue', '/api/timesheet/work-queue/', 'assignmentId', 'Add to Timesheet', 'Start timer', 'Open task', 'authoritativeSource']) {
  requireText(`${portal}\n${queueCard}`, contract, 'Work Queue task association');
}
for (const contract of ['Calendar / Timeline', 'Description required', 'Task association required', '/api/timesheet/entries/', 'open-entry', 'Remove draft']) {
  requireText(portal, contract, 'Calendar task association');
}
for (const contract of ['/api/timesheet/timers/active', '/api/timesheet/timers/start', '/stop', '/discard', 'maximumDurationSeconds', 'startedAtUtc', 'autoStopped']) {
  requireText(`${portal}\n${timerView}`, contract, 'timer frontend contract');
}
requireText(portal, 'projectPulseModule001MobileMode', 'mobile preference');
requireText(portal, 'Mobile mode', 'mobile selector label');
requireText(css, '#timesheet.module001-mobile-mode', 'mobile presentation');
requireText(css, 'min-height: 44px', 'touch targets');
requireText(css, '.module001-calendar-grid', 'task-aware Calendar layout');
requireText(css, '.module001-work-grid', 'task-aware Work Queue layout');

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
  const migration = read(backendPaths[5]);
  const rollback = read(backendPaths[6]);
  const allBackend = `${contracts}\n${data}\n${engine}\n${submission}\n${endpoints}`;

  requireText(allBackend, 'ScopedAuthorizationEvaluator.EvaluateAsync', 'backend scoped authorization');
  requireText(allBackend, 'actor.EffectiveUserId', 'authenticated effective user');
  requireText(allBackend, 'TIME_EDIT_OWN', 'self-only edit action');
  requireText(allBackend, 'TIME_SUBMIT', 'submission action');
  requireText(allBackend, 'AutoStopModule001TimerAsync', 'server auto-stop');
  requireText(allBackend, 'Module001BuildSegments', 'midnight and week segmentation');
  requireText(allBackend, 'Module001RoundedMinutes', 'single authoritative rounding');
  requireText(allBackend, 'project_assignments', 'authoritative task source');
  requireText(allBackend, 'timesheet_day_statuses', 'Module 002 daily-status handoff');
  requireText(allBackend, "status = 'submitted'", 'submitted status');
  requireText(allBackend, 'SUBMISSION_VALIDATION_FAILED', 'validation audit');
  requireText(allBackend, 'meaningful work description is required', 'description requirement');
  rejectText(endpoints, 'Module001TimerStartRequest(Guid UserId', 'browser-supplied timer identity');

  requireText(migration, 'ux_module001_one_running_timer_per_user', 'one running timer constraint');
  requireText(migration, 'rounded_minutes % 15 = 0', 'quarter-hour database constraint');
  requireText(migration, 'BETWEEN 0 AND 43200', '12-hour seconds constraint');
  requireText(migration, 'BETWEEN 0 AND 720', '12-hour rounded-minutes constraint');
  requireText(migration, 'module001_timer_audit_events', 'immutable timer audit');
  requireText(migration, 'module001_weekly_task_lines', 'durable weekly task association');
  requireText(rollback, 'rollback blocked', 'fail-closed rollback');
  requireText(rollback, 'DROP TABLE IF EXISTS module001_timer_sessions', 'reviewed rollback');
}

console.log(`MODULE_001_TIMESHEET_TIMER_MOBILE_VALIDATION=PASS roundingCases=${roundingCases.length} backend=${backendAvailable ? 'full' : 'frontend-container'}`);
