import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const webRoot = path.resolve(scriptDirectory, '..');
const sourceRoot = path.join(webRoot, 'src');
const importLine = "import UnifiedProjectFinancialWorkspace from './UnifiedProjectFinancialWorkspace.jsx';";
const markerStart = 'GROUP_3_UNIFIED_PROJECT_FINANCIAL_WORKSPACES_START';
const markerEnd = 'GROUP_3_UNIFIED_PROJECT_FINANCIAL_WORKSPACES_END';

const installations = [
  {
    file: 'ProjectManagerWorkloadCenter.jsx',
    importAnchor: "import './project-manager-workload-center.css';",
    rootAnchor: '    <section className="pm-workload-center">',
    mount: '      <UnifiedProjectFinancialWorkspace workspace="pm" projectManagerUserId={selectedProjectManagerUserId} />'
  },
  {
    file: 'ProjectWorkspaceCenter.jsx',
    importAnchor: "import './project-workspace-center.css';",
    rootAnchor: '    <section className="project-workspace-center">',
    mount: '      <UnifiedProjectFinancialWorkspace workspace="engineering" />'
  },
  {
    file: 'SalesInsightsDashboard.jsx',
    importAnchor: "import './sales-insights-dashboard.css';",
    rootAnchor: '    <section className="sales-insights-dashboard">',
    mount: '      <UnifiedProjectFinancialWorkspace workspace="sales" />'
  },
  {
    file: 'RateCardAdministrationCenter.jsx',
    importAnchor: "import './rate-card-administration-center.css';",
    rootAnchor: '    <section className="rate-card-admin-center">',
    mount: '      <UnifiedProjectFinancialWorkspace workspace="rate-card" />'
  }
];

function count(source, needle) {
  return source.split(needle).length - 1;
}

function install(configuration) {
  const filePath = path.join(sourceRoot, configuration.file);
  if (!fs.existsSync(filePath)) {
    throw new Error(`Group 3 injection target is missing: ${configuration.file}`);
  }

  let source = fs.readFileSync(filePath, 'utf8');

  if (!source.includes(importLine)) {
    if (!source.includes(configuration.importAnchor)) {
      throw new Error(`Group 3 import anchor is missing in ${configuration.file}.`);
    }
    source = source.replace(
      configuration.importAnchor,
      `${configuration.importAnchor}\n${importLine}`
    );
  }

  const panelMarkup = [
    configuration.rootAnchor,
    `      {/* ${markerStart} */}`,
    configuration.mount,
    `      {/* ${markerEnd} */}`
  ].join('\n');

  if (!source.includes(markerStart)) {
    if (!source.includes(configuration.rootAnchor)) {
      throw new Error(`Group 3 root anchor is missing in ${configuration.file}.`);
    }
    source = source.replace(configuration.rootAnchor, panelMarkup);
  }

  if (count(source, importLine) !== 1) {
    throw new Error(`Group 3 import must appear exactly once in ${configuration.file}.`);
  }
  if (count(source, markerStart) !== 1 || count(source, markerEnd) !== 1) {
    throw new Error(`Group 3 markers must appear exactly once in ${configuration.file}.`);
  }
  if (count(source, configuration.mount.trim()) !== 1) {
    throw new Error(`Group 3 workspace mount is not unique in ${configuration.file}.`);
  }

  fs.writeFileSync(filePath, source.endsWith('\n') ? source : `${source}\n`, 'utf8');
  return configuration.file;
}

const installed = installations.map(install);
console.log(`GROUP_3_UNIFIED_PROJECT_FINANCIAL_INJECTION=PASS files=${installed.join(',')}`);
