import fs from 'node:fs';
import path from 'node:path';

const webRoot = path.resolve(path.dirname(new URL(import.meta.url).pathname), '..');
const sourcePath = path.join(webRoot, 'src', 'App.jsx');
const outputDirectory = path.join(webRoot, 'src', '.module001-generated');
const outputPath = path.join(outputDirectory, 'App.Module001.g.jsx');

const original = fs.readFileSync(sourcePath, 'utf8');
let generated = original;

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
    const snapshot = {
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
  'timesheetView',
  'async function handleSubmit()'
]) {
  if (!generated.includes(required)) throw new Error(`Generated App is missing required contract: ${required}`);
}

if (generated.includes('MODULE_001_GENERATOR_ALREADY_APPLIED')) {
  throw new Error('The canonical App source appears to contain generated integration code.');
}

fs.mkdirSync(outputDirectory, { recursive: true });
fs.writeFileSync(
  outputPath,
  `/* MODULE_001_GENERATOR_ALREADY_APPLIED - generated; do not edit */\n${generated}`,
  'utf8'
);

console.log(`MODULE_001_APP_GENERATION=PASS output=${path.relative(webRoot, outputPath)} draftGuardsRemoved=2`);
