import fs from 'node:fs';
import path from 'node:path';
import assert from 'node:assert/strict';
import { pathToFileURL } from 'node:url';

const root = process.argv[2] ? path.resolve(process.argv[2]) : process.cwd();
const required = [
  'docs/modules/module-001-timesheet/TIMESHEET-TIMER-REQUIREMENTS.md',
  'docs/modules/module-001-timesheet/TASK-SOURCE-MAPPING.md',
  'docs/modules/module-001-timesheet/TIMER-API-CONTRACT.md',
  'docs/modules/module-001-timesheet/TIMER-DATABASE-DESIGN.md',
  'docs/modules/module-001-timesheet/MOBILE-MODE-DESIGN.md',
  'docs/modules/module-001-timesheet/UAT-PLAN.md',
  'docs/modules/module-001-timesheet/INTEGRATION-AFTER-RBAC.md',
  'src/frontend/project-time-web/src/module001/TimesheetTimerView.jsx',
  'src/frontend/project-time-web/src/module001/TimesheetTaskPicker.jsx',
  'src/frontend/project-time-web/src/module001/TimesheetMobileMode.jsx',
  'src/frontend/project-time-web/src/module001/TimesheetCalendarEntryPanel.jsx',
  'src/frontend/project-time-web/src/module001/TimesheetWorkQueueCard.jsx',
  'src/frontend/project-time-web/src/module001/timesheet-duration.js',
  'src/frontend/project-time-web/src/module001/timesheet-prep.css'
];

for (const relative of required) assert.ok(fs.existsSync(path.join(root, relative)), `missing ${relative}`);

const forbidden = [
  '.github/workflows/projectpulse-ci.yml',
  'src/frontend/project-time-web/package.json',
  'src/frontend/project-time-web/src/main.jsx',
  'src/frontend/project-time-web/src/App.jsx',
  'src/backend/ProjectTime.Api/ProjectTime.Api.csproj'
];
for (const relative of forbidden) assert.ok(!fs.existsSync(path.join(root, relative)), `forbidden Phase 0 file present: ${relative}`);

const durationModule = await import(pathToFileURL(path.join(root, 'src/frontend/project-time-web/src/module001/timesheet-duration.js')).href);
const cases = [
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
for (const [seconds, expected] of cases) assert.equal(durationModule.roundSecondsUpToQuarterHour(seconds), expected, `${seconds} seconds`);
assert.equal(durationModule.formatElapsedSeconds(4 * 3600 + 1), '04:00:01');
assert.equal(durationModule.formatElapsedSeconds(13 * 3600), '12:00:00');

const allFiles = [];
function walk(directory) {
  for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
    const absolute = path.join(directory, entry.name);
    if (entry.isDirectory()) walk(absolute); else allFiles.push(path.relative(root, absolute));
  }
}
walk(root);
assert.ok(!allFiles.some((file) => file.startsWith('database/migrations/') || file.startsWith('database/rollback/')), 'Phase 0 cannot contain migrations or rollback files');
assert.ok(!allFiles.some((file) => file.includes('ScopedRolePolicy') || file.endsWith('ScopedAuthorizationEvaluator.cs')), 'Phase 0 cannot modify scoped RBAC');
assert.ok(!allFiles.some((file) => file.includes('ApprovalCenter')), 'Phase 0 cannot modify Module 002');

console.log(`MODULE_001_PHASE0_VALIDATION=PASS checks=${required.length + forbidden.length + cases.length + 5}`);
