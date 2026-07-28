import fs from 'node:fs';
import path from 'node:path';

const webRoot = path.resolve(path.dirname(new URL(import.meta.url).pathname), '..');
const generatedAppPath = path.join(webRoot, 'src', 'App.Module001.g.jsx');

if (!fs.existsSync(generatedAppPath)) {
  throw new Error('Generate App.Module001.g.jsx before injecting Module 001 extension slots.');
}

let source = fs.readFileSync(generatedAppPath, 'utf8');
const timesheetMarker = '<section id="timesheet" className="panel timesheet-page">';
const timesheetIndex = source.indexOf(timesheetMarker);
if (timesheetIndex < 0) throw new Error('Module 001 slot injector could not locate the Timesheet route.');

const slotIds = [
  'module001-view-tab-host',
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

if (source.includes('module001-toolbar-host')) {
  throw new Error('The retired runtime-created Module 001 toolbar host must not be present.');
}

if (source.includes('MODULE_001_REACT_OWNED_EXTENSION_SLOTS')) {
  throw new Error('Module 001 extension-slot marker was already injected unexpectedly.');
}
source = source.replace(
  '/* MODULE_001_GENERATOR_ALREADY_APPLIED - generated; do not edit */',
  '/* MODULE_001_GENERATOR_ALREADY_APPLIED - generated; do not edit */\n/* MODULE_001_REACT_OWNED_EXTENSION_SLOTS */'
);

fs.writeFileSync(generatedAppPath, source, 'utf8');
console.log(`MODULE_001_REACT_OWNED_EXTENSION_SLOTS=PASS slots=${slotIds.length} runtimeDomInsertion=0`);
