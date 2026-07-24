import fs from 'node:fs';
import path from 'node:path';

const webRoot = path.resolve(path.dirname(new URL(import.meta.url).pathname), '..');
const sourceDirectory = path.join(webRoot, 'src');
const appSourcePath = path.join(sourceDirectory, 'App.jsx');
const appOutputPath = path.join(sourceDirectory, 'App.Module001.g.jsx');
const guideSourcePath = path.join(sourceDirectory, 'SystemUserGuide.jsx');
const guideOutputPath = path.join(sourceDirectory, 'SystemUserGuide.Module001.g.jsx');

const guideOriginal = fs.readFileSync(guideSourcePath, 'utf8');
const guideBlockPattern = /  timesheet: \{[\s\S]*?\n  \},\n  'manager-approval': \{/;
const guideMatches = guideOriginal.match(guideBlockPattern);
if (!guideMatches) {
  throw new Error('Module 001 generator could not locate the Timesheet guide block.');
}

const guideBlock = `  timesheet: {
    category: 'Time & Approvals',
    audience: ['Everyone', 'Engineer', 'Manager'],
    purpose: 'Enter, review, time, save, and submit assigned project-task and authorized non-project work.',
    functions: [
      'Weekly Grid preserves the complete seven-day entry grid.',
      'Daily Focus provides a day-centered, mobile-friendly entry view.',
      'My Work Queue loads actual tasks assigned to the authenticated engineer and preserves customer, project, task, and assignment identifiers.',
      'Quick Entry List provides compact activity entry against the same weekly draft.',
      'Calendar / Timeline shows the project, task, classification, status, description completeness, and rounded hours behind every entry.',
      'Start / Stop Timer tracks assigned tasks or authorized non-project activities using server-authoritative UTC timestamps.',
      'Only one timer may run per user, timer time rounds upward once to a quarter hour, and the server caps a timer at 12 hours.',
      'Mobile mode provides a manually selectable single-column presentation while preserving all six views and actions.',
      'Normal and Afterhours hours remain separate in every view.',
      'Save draft persists editable entries without submitting them, including incomplete descriptions that must be corrected later.',
      'Submit week saves the shared draft, validates descriptions and task associations, requires confirmation, and routes valid time to Module 002 Approval Inbox.',
      'Submitted or approved time follows the existing return, reopen, correction, and approval rules.'
    ],
    steps: [
      'Choose the correct week.',
      'Select or add an assigned task or authorized non-project activity.',
      'Enter time directly or start and stop the task timer.',
      'Review the same entries in Weekly Grid, Daily Focus, My Work Queue, Quick Entry List, or Calendar / Timeline.',
      'Save the draft, then complete every positive-hour description and task association.',
      'Select Submit week, review the validation summary, and confirm the Module 002 handoff.'
    ],
    statuses: ['Draft', 'Submitted', 'Manager declined / Correction', 'Manager approved', 'PM approved', 'PTC final review', 'Accounting ready', 'Reconciled', 'Locked'],
    notes: [
      'Vacation is used for PTO; Holiday is reserved for company-paid holidays and the floating holiday.',
      'A draft may be saved without a description, but every positive-hour entry requires a meaningful description before submission.',
      'View-As is read-only and cannot start, stop, discard, edit, or submit another user’s time.',
      'Timer raw timestamps and actual seconds remain auditable; only the rounded duration populates Timesheet hours.'
    ]
  },
  'manager-approval': {`;

const guideGenerated = guideOriginal.replace(guideBlockPattern, guideBlock);
for (const required of [
  'Start / Stop Timer',
  'Mobile mode',
  'Module 002 Approval Inbox',
  'server-authoritative UTC timestamps'
]) {
  if (!guideGenerated.includes(required)) {
    throw new Error(`Generated Module 999 guide is missing: ${required}`);
  }
}

const appOriginal = fs.readFileSync(appSourcePath, 'utf8');
let generated = appOriginal;
const guideImport = "import SystemUserGuide from './SystemUserGuide.jsx';";
if (!generated.includes(guideImport)) {
  throw new Error('Module 001 generator could not locate the SystemUserGuide import.');
}
generated = generated.replace(
  guideImport,
  "import SystemUserGuide from './SystemUserGuide.Module001.g.jsx';"
);

const handleSubmitMarker = '  async function handleSubmit() {';
const handleSubmitIndex = generated.indexOf(handleSubmitMarker);
if (handleSubmitIndex < 0) throw new Error('Module 001 generator could not locate handleSubmit.');

const draftPrefix = generated.slice(0, handleSubmitIndex);
const submitSuffix = generated.slice(handleSubmitIndex);
const missingDescriptionGuard = /\n\s*const missingDescriptions = getEntriesMissingDescriptions\(payload\.entries\);\n\s*if \(missingDescriptions\.length > 0\) \{\n\s*setSaveStatus\(getMissingDescriptionMessage\(missingDescriptions\)\);\n\s*return;\n\s*\}\n/g;
const matches = [...draftPrefix.matchAll(missingDescriptionGuard)];
if (matches.length !== 2) {
  throw new Error(`Expected two draft description guards before submission; found ${matches.length}.`);
}
generated = draftPrefix.replace(missingDescriptionGuard, '\n') + submitSuffix;

const authMarker = '\n\n  if (!authSession) {';
const authIndex = generated.indexOf(authMarker);
if (authIndex < 0 || generated.indexOf(authMarker, authIndex + authMarker.length) >= 0) {
  throw new Error('Module 001 generator requires one authenticated-shell marker.');
}

const bridge = `

  /* MODULE_001_CANONICAL_STATE_BRIDGE_START */
  useEffect(() => {
    const canonicalCalendarEntries = activeRows.flatMap((row) =>
      days.flatMap((day) =>
        timeTypes.map((type) => ({
          row,
          day,
          timeType: type,
          entry: getEntry(row.id, day.date, type.key)
        }))
      )
    );

    const snapshot = {
      selectedWeekStart,
      days,
      timeTypes,
      activeRows,
      entries,
      timesheetView,
      focusedDayDate,
      draftPayload: buildTimesheetPayload(),
      calendarEntries: canonicalCalendarEntries,
      grandTotal,
      normalTotal,
      afterhoursTotal,
      submissionStatus,
      saveStatus,
      isSaving,
      isAnyDayEditable,
      assignedTasks: assignedTasks.data?.tasks ?? assignedTasks.data ?? [],
      nonProjectCategories: nonProjectCategories.data?.categories ?? nonProjectCategories.data ?? [],
      isViewAs: Boolean(securityContext.data?.isViewAs)
    };

    window.__projectPulseModule001Snapshot = snapshot;
    window.dispatchEvent(new CustomEvent('projectpulse:module001-state', { detail: snapshot }));
  }, [
    selectedWeekStart,
    days,
    timeTypes,
    activeRows,
    entries,
    timesheetView,
    focusedDayDate,
    grandTotal,
    normalTotal,
    afterhoursTotal,
    submissionStatus,
    saveStatus,
    isSaving,
    isAnyDayEditable,
    assignedTasks.data,
    nonProjectCategories.data,
    securityContext.data?.isViewAs
  ]);

  useEffect(() => {
    const handleModule001Action = (event) => {
      const detail = event?.detail ?? {};
      if (detail.type === 'add-assignment') {
        const tasks = assignedTasks.data?.tasks ?? assignedTasks.data ?? [];
        const task = tasks.find((item) =>
          String(item.assignmentId ?? item.projectAssignmentId ?? '') === String(detail.assignmentId ?? '')
          || (
            String(item.projectId ?? '') === String(detail.projectId ?? '')
            && String(item.taskId ?? '') === String(detail.taskId ?? '')
          )
        );
        if (task) addTask(task);
      }

      if (detail.type === 'open-entry' && detail.rowId && detail.workDate && detail.timeType) {
        openEntryDetails(detail.rowId, detail.workDate, detail.timeType);
      }
    };

    window.addEventListener('projectpulse:module001-action', handleModule001Action);
    return () => window.removeEventListener('projectpulse:module001-action', handleModule001Action);
  }, [assignedTasks.data, activeRows, entries, selectedWeekStart]);
  /* MODULE_001_CANONICAL_STATE_BRIDGE_END */`;

generated = `${generated.slice(0, authIndex)}${bridge}${generated.slice(authIndex)}`;

for (const required of [
  'MODULE_001_CANONICAL_STATE_BRIDGE_START',
  'projectpulse:module001-state',
  'projectpulse:module001-action',
  'buildTimesheetPayload()',
  'canonicalCalendarEntries',
  "./SystemUserGuide.Module001.g.jsx",
  'timesheetView',
  'async function handleSubmit()'
]) {
  if (!generated.includes(required)) throw new Error(`Generated App is missing required contract: ${required}`);
}

if (generated.includes('MODULE_001_GENERATOR_ALREADY_APPLIED')) {
  throw new Error('The canonical App source appears to contain generated integration code.');
}

fs.writeFileSync(
  guideOutputPath,
  `/* MODULE_001_GENERATED_GUIDE - generated; do not edit */\n${guideGenerated}`,
  'utf8'
);
fs.writeFileSync(
  appOutputPath,
  `/* MODULE_001_GENERATOR_ALREADY_APPLIED - generated; do not edit */\n${generated}`,
  'utf8'
);

console.log(`MODULE_001_APP_GENERATION=PASS app=${path.relative(webRoot, appOutputPath)} guide=${path.relative(webRoot, guideOutputPath)} draftGuardsRemoved=2`);
