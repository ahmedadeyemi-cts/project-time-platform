import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const webRoot = path.resolve(scriptDirectory, '..');
const sourceRoot = path.join(webRoot, 'src');
const importLine = "import PlatformResiliencePlanningPanel from './PlatformResiliencePlanningPanel.jsx';";
const markerStart = 'GROUP_2B_PROVIDER_NEUTRAL_RESILIENCE_START';
const markerEnd = 'GROUP_2B_PROVIDER_NEUTRAL_RESILIENCE_END';

const installations = [
  {
    file: 'BackupDrCenter.jsx',
    moduleCode: '014',
    importAnchor: "import './backup-dr-center.css';",
    rootAnchor: '    <section id="backup-dr-center" className="panel backup-dr-center">'
  },
  {
    file: 'RestoreValidationCenter.jsx',
    moduleCode: '015',
    importAnchor: "import './restore-validation-center.css';",
    rootAnchor: '    <section id="restore-validation-center" className="panel timesheet-page restore-validation-page">'
  },
  {
    file: 'ReplicationSyncStatusCenter.jsx',
    moduleCode: '017',
    importAnchor: "import './replication-sync-status-center.css';",
    rootAnchor: '    <section id="replication-sync-center" className="panel timesheet-page replication-sync-page">'
  }
];

function occurrences(source, needle) {
  return source.split(needle).length - 1;
}

function installPanel(configuration) {
  const filePath = path.join(sourceRoot, configuration.file);
  if (!fs.existsSync(filePath)) {
    throw new Error(`Group 2B injection target is missing: ${configuration.file}`);
  }

  let source = fs.readFileSync(filePath, 'utf8');

  if (!source.includes(importLine)) {
    if (!source.includes(configuration.importAnchor)) {
      throw new Error(`Group 2B import anchor is missing in ${configuration.file}.`);
    }
    source = source.replace(
      configuration.importAnchor,
      `${configuration.importAnchor}\n${importLine}`
    );
  }

  const panelMarkup = [
    configuration.rootAnchor,
    `      {/* ${markerStart} */}`,
    `      <PlatformResiliencePlanningPanel moduleCode="${configuration.moduleCode}" authSession={authSession} />`,
    `      {/* ${markerEnd} */}`
  ].join('\n');

  if (!source.includes(markerStart)) {
    if (!source.includes(configuration.rootAnchor)) {
      throw new Error(`Group 2B root anchor is missing in ${configuration.file}.`);
    }
    source = source.replace(configuration.rootAnchor, panelMarkup);
  }

  if (occurrences(source, importLine) !== 1) {
    throw new Error(`Group 2B panel import must appear exactly once in ${configuration.file}.`);
  }
  if (occurrences(source, markerStart) !== 1 || occurrences(source, markerEnd) !== 1) {
    throw new Error(`Group 2B panel markers must appear exactly once in ${configuration.file}.`);
  }
  if (occurrences(source, `<PlatformResiliencePlanningPanel moduleCode="${configuration.moduleCode}" authSession={authSession} />`) !== 1) {
    throw new Error(`Group 2B Module ${configuration.moduleCode} panel mount is not unique in ${configuration.file}.`);
  }

  fs.writeFileSync(filePath, source.endsWith('\n') ? source : `${source}\n`, 'utf8');
  return configuration.file;
}

const installed = installations.map(installPanel);
console.log(`GROUP_2B_PROVIDER_NEUTRAL_INJECTION=PASS files=${installed.join(',')}`);
