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
      'Quick Entry List provides compact activity entry against the same weekly draft.',
      'Start / Stop Timer tracks assigned tasks or authorized non-project activities using server-authoritative UTC timestamps.',
      'Only one timer may run per user, timer time rounds upward once to a quarter hour, and the server caps a timer at 12 hours.',
      'Mobile mode provides a manually selectable single-column presentation while preserving the four streamlined views and actions.',
      'Normal and Afterhours hours remain separate in every entry view.',
      'Save draft persists editable entries without submitting them, including incomplete descriptions that must be corrected later.',
      'Submit week saves the shared draft, validates descriptions and task associations, requires confirmation, and routes valid time to Module 002 Approval Inbox.',
      'Submitted or approved time follows the existing return, reopen, correction, and approval rules.'
    ],
    steps: [
      'Choose the correct week.',
      'Select or add an assigned task or authorized non-project activity from the Activities panel.',
      'Enter time in Weekly Grid, Daily Focus, or Quick Entry List, or start and stop the task timer.',
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

const yearlyUtilizationImport = "import YearlyUtilizationPanel from './YearlyUtilizationPanel.jsx';";
if (!generated.includes(yearlyUtilizationImport)) {
  throw new Error('Generated app could not locate the Module 003 utilization import.');
}
generated = generated.replace(
  yearlyUtilizationImport,
  `${yearlyUtilizationImport}\nimport { getRollingYearOptions } from './rolling-year-window.js';`
);

const holidayYearOptionsBlock = `  const holidayYearOptions = useMemo(() => {
    const currentYear = new Date().getFullYear();
    return Array.from({ length: 11 }, (_, index) => String(currentYear + index));
  }, []);`;
if (!generated.includes(holidayYearOptionsBlock)) {
  throw new Error('Generated app could not locate the Module 004 hard-coded holiday year window.');
}
generated = generated.replace(
  holidayYearOptionsBlock,
  '  const holidayYearOptions = getRollingYearOptions().map(String);'
);

const removedViewDefinitions = [
  "            { key: 'queue', label: 'My Work Queue', description: 'Assigned tasks and requests' },\n",
  "            { key: 'calendar', label: 'Calendar / Timeline', description: 'Week-at-a-glance totals' }\n"
];
for (const definition of removedViewDefinitions) {
  if (!generated.includes(definition)) {
    throw new Error(`Module 001 generator could not locate retired view definition: ${definition.trim()}`);
  }
  generated = generated.replace(definition, '');
}

generated = generated.replace(
  "            { key: 'quick', label: 'Quick Entry List', description: 'Compact activity entry' },\n          ].map((view) => (",
  "            { key: 'quick', label: 'Quick Entry List', description: 'Compact activity entry' }\n          ].map((view) => ("
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
    if (timesheetView === 'queue' || timesheetView === 'calendar') {
      setTimesheetView('weekly');
      window.localStorage.setItem('projectPulseTimesheetView', 'weekly');
    }
  }, [timesheetView]);

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
      assignedTasks: assignedOpenTasks,
      nonProjectCategories: categories,
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
    openTasks.data?.tasks,
    timesheet.data?.nonProjectCategories,
    securityContext.data?.isViewAs
  ]);

  useEffect(() => {
    const handleModule001Action = (event) => {
      const detail = event?.detail ?? {};
      if (detail.type === 'add-assignment') {
        const task = assignedOpenTasks.find((item) =>
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
  }, [openTasks.data?.tasks, activeRows, entries, selectedWeekStart]);
  /* MODULE_001_CANONICAL_STATE_BRIDGE_END */`;

generated = `${generated.slice(0, authIndex)}${bridge}${generated.slice(authIndex)}`;

for (const required of [
  'MODULE_001_CANONICAL_STATE_BRIDGE_START',
  'projectpulse:module001-state',
  'projectpulse:module001-action',
  'buildTimesheetPayload()',
  'canonicalCalendarEntries',
  "setTimesheetView('weekly')",
  "./SystemUserGuide.Module001.g.jsx",
  "import { getRollingYearOptions } from './rolling-year-window.js';",
  'const holidayYearOptions = getRollingYearOptions().map(String);',
  'timesheetView',
  'async function handleSubmit()'
]) {
  if (!generated.includes(required)) throw new Error(`Generated App is missing required contract: ${required}`);
}

for (const retired of [
  "{ key: 'queue', label: 'My Work Queue'",
  "{ key: 'calendar', label: 'Calendar / Timeline'",
  'Array.from({ length: 11 }, (_, index) => String(currentYear + index))'
]) {
  if (generated.includes(retired)) throw new Error(`Generated App still exposes retired contract: ${retired}`);
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

console.log(`MODULE_001_APP_GENERATION=PASS app=${path.relative(webRoot, appOutputPath)} guide=${path.relative(webRoot, guideOutputPath)} draftGuardsRemoved=2 retiredTabsRemoved=2 rollingYears=modules003004`);
