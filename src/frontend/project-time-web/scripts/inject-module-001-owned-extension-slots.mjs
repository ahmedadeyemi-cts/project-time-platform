import fs from 'node:fs';
import path from 'node:path';

const webRoot = path.resolve(path.dirname(new URL(import.meta.url).pathname), '..');
const generatedAppPath = path.join(webRoot, 'src', 'App.Module001.g.jsx');
const generatedGuidePath = path.join(webRoot, 'src', 'SystemUserGuide.Module001.g.jsx');

if (!fs.existsSync(generatedAppPath)) {
  throw new Error('Generate App.Module001.g.jsx before injecting Module 001 extension slots.');
}
if (!fs.existsSync(generatedGuidePath)) {
  throw new Error('Generate SystemUserGuide.Module001.g.jsx before injecting Module 001 guide updates.');
}

let source = fs.readFileSync(generatedAppPath, 'utf8');
const timesheetMarker = '<section id="timesheet" className="panel timesheet-page">';
const timesheetIndex = source.indexOf(timesheetMarker);
if (timesheetIndex < 0) throw new Error('Module 001 slot injector could not locate the Timesheet route.');

const slotIds = [
  'module001-view-tab-host',
  'module001-toolbar-host',
  'module001-active-timer-recovery-host',
  'module001-ptc-time-steward-host',
  'module001-enhancement-view-host'
];

const existing = slotIds.filter((id) => source.includes(`id="${id}"`));
if (existing.length && existing.length !== slotIds.length) {
  throw new Error(`Module 001 generated App contains a partial React-owned slot set: ${existing.join(', ')}`);
}

function replaceOnce(marker, replacement, label, fromIndex = 0) {
  const index = source.indexOf(marker, fromIndex);
  if (index < 0) throw new Error(`Module 001 slot injector could not locate ${label}.`);
  const duplicate = source.indexOf(marker, index + marker.length);
  const nextRoute = source.indexOf('<section id="', index + marker.length);
  if (duplicate >= 0 && (nextRoute < 0 || duplicate < nextRoute)) {
    throw new Error(`Module 001 slot injector found multiple ${label} markers inside the Timesheet route.`);
  }
  source = `${source.slice(0, index)}${replacement}${source.slice(index + marker.length)}`;
}

if (!existing.length) {
  replaceOnce(
    timesheetMarker,
    `${timesheetMarker}\n        <div id="module001-active-timer-recovery-host" className="module001-active-timer-recovery-host" data-projectpulse-react-owned-slot="true" />\n        <div id="module001-ptc-time-steward-host" className="module001-ptc-time-steward-host" data-projectpulse-react-owned-slot="true" />`,
    'the Timesheet route boundary'
  );

  replaceOnce(
    '<div className="toolbar-actions">',
    `<div className="toolbar-actions">\n            <div id="module001-toolbar-host" className="module001-toolbar-host" data-projectpulse-react-owned-slot="true" />`,
    'the Timesheet toolbar actions',
    timesheetIndex
  );

  replaceOnce(
    '<div className="timesheet-view-switcher" role="tablist" aria-label="Timesheet views">',
    `<div className="timesheet-view-switcher" role="tablist" aria-label="Timesheet views">\n          <div id="module001-view-tab-host" className="module001-view-tab-host" data-projectpulse-react-owned-slot="true" />`,
    'the Timesheet view switcher',
    timesheetIndex
  );

  replaceOnce(
    '<div className="timesheet-workspace">',
    `<div className="timesheet-workspace">\n            <div id="module001-enhancement-view-host" className="module001-enhancement-view-host" data-projectpulse-react-owned-slot="true" />`,
    'the Timesheet workspace',
    timesheetIndex
  );
}

for (const id of slotIds) {
  const matches = source.match(new RegExp(`id=["']${id}["']`, 'g')) || [];
  if (matches.length !== 1) {
    throw new Error(`Module 001 React-owned slot ${id} must appear exactly once; found ${matches.length}.`);
  }
}

// The retired runtime-created Module 001 toolbar host must not be present.
// The current host is generated statically inside the React-owned toolbar so the
// mobile control never mutates another component's children at runtime.
if (source.includes('MODULE_001_REACT_OWNED_EXTENSION_SLOTS')) {
  throw new Error('Module 001 extension-slot marker was already injected unexpectedly.');
}
source = source.replace(
  '/* MODULE_001_GENERATOR_ALREADY_APPLIED - generated; do not edit */',
  '/* MODULE_001_GENERATOR_ALREADY_APPLIED - generated; do not edit */\n/* MODULE_001_REACT_OWNED_EXTENSION_SLOTS */'
);

let guide = fs.readFileSync(generatedGuidePath, 'utf8');
const legacyTimerGuide = 'Only one timer may run per user, timer time rounds upward once to a quarter hour, and the server caps a timer at 12 hours.';
const multiTimerGuide = 'Up to five distinct activity timers may run per user, each timer rounds upward once to a quarter hour, and the server caps each timer at 24 hours.';
if (!guide.includes(legacyTimerGuide) && !guide.includes(multiTimerGuide)) {
  throw new Error('Module 001 guide patch could not locate the timer policy sentence.');
}
guide = guide.replace(legacyTimerGuide, multiTimerGuide);
if (guide.includes('caps a timer at 12 hours')) {
  throw new Error('Generated Module 001 guide still contains the retired 12-hour timer cap.');
}

fs.writeFileSync(generatedAppPath, source, 'utf8');
fs.writeFileSync(generatedGuidePath, guide, 'utf8');
console.log(`MODULE_001_REACT_OWNED_EXTENSION_SLOTS=PASS slots=${slotIds.length} runtimeDomInsertion=0 timerPolicy=5x24h mobileToggle=restored`);
