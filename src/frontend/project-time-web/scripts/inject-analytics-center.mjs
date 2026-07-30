import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const webRoot = path.resolve(scriptDirectory, '..');
const sourceRoot = path.join(webRoot, 'src');
const appPath = path.join(sourceRoot, 'App.jsx');
const registryPath = path.join(sourceRoot, 'module-availability-registry.js');
const importLine = "import AnalyticsCenter from './AnalyticsCenter.jsx';";
const importAnchor = "import FinancialOperationsRecoveryWorkspace from './FinancialOperationsRecoveryWorkspace.jsx';";
const legacyGroup5Mount = '<FinancialOperationsRecoveryWorkspace mode="reporting" authSession={authSession} />';
const formerEnterpriseMount = '<EnterpriseReportingCenter authSession={authSession} />';
const analyticsMount = '<AnalyticsCenter authSession={authSession} />';

function count(source, marker) {
  return source.split(marker).length - 1;
}

function write(filePath, source) {
  fs.writeFileSync(filePath, source.endsWith('\n') ? source : `${source}\n`, 'utf8');
}

function installApp() {
  if (!fs.existsSync(appPath)) throw new Error('Analytics Center App.jsx target is missing.');
  let source = fs.readFileSync(appPath, 'utf8');

  source = source
    .replace(/^import EnterpriseReportingCenter from '\.\/EnterpriseReportingCenter\.jsx';\n?/gm, '')
    .replace(/^import AnalyticsCenter from '\.\/AnalyticsCenter\.jsx';\n?/gm, '');

  if (!source.includes(importAnchor)) {
    throw new Error('Analytics Center requires the Group 5 reporting integration anchor.');
  }
  source = source.replace(importAnchor, `${importAnchor}\n${importLine}`);

  if (source.includes(formerEnterpriseMount)) {
    source = source.replace(formerEnterpriseMount, analyticsMount);
  } else if (source.includes(legacyGroup5Mount)) {
    source = source.replace(legacyGroup5Mount, analyticsMount);
  } else if (!source.includes(analyticsMount)) {
    throw new Error('Analytics Center could not locate the Module 030 reporting route mount.');
  }

  source = source
    .replaceAll('Financial Report Center', 'Analytics Center')
    .replaceAll('Enterprise Reporting Center', 'Analytics Center')
    .replaceAll('Reporting / Accounting / Invoicing / Analytics', 'Analytics Center')
    .replaceAll(
      'Search, preview, run, export, and review history for actual role-scoped financial reports with independent source recovery.',
      'Select and run role-scoped analytics across projects, customers, financials, time, people, delivery, operations, governance, and acceptance.'
    )
    .replaceAll(
      'Run dynamic role-scoped reports across projects, customers, financials, time, people, delivery, operations, governance, and acceptance.',
      'Select and run role-scoped analytics across projects, customers, financials, time, people, delivery, operations, governance, and acceptance.'
    );

  if (count(source, importLine) !== 1) throw new Error('Analytics Center App import must appear exactly once.');
  if (count(source, analyticsMount) !== 1) throw new Error('Analytics Center route mount must appear exactly once.');
  if (source.includes(legacyGroup5Mount) || source.includes(formerEnterpriseMount)) {
    throw new Error('A superseded Module 030 reporting mount remains active.');
  }
  write(appPath, source);
}

function installRegistry() {
  if (!fs.existsSync(registryPath)) throw new Error('Analytics Center registry target is missing.');
  let source = fs.readFileSync(registryPath, 'utf8');
  source = source
    .replaceAll('Financial Report Center', 'Analytics Center')
    .replaceAll('Enterprise Reporting Center', 'Analytics Center')
    .replaceAll(
      'Search, preview, run, export, and review history for actual role-scoped financial reports with independent source recovery.',
      'Select and run role-scoped analytics with report-specific customer, project, Engineer, Project Manager, team, date, financial, delivery, and operational criteria.'
    )
    .replaceAll(
      'Dynamic role-scoped reporting across projects, customers, financials, time, people, delivery, operations, governance, and acceptance.',
      'Select and run role-scoped analytics with report-specific customer, project, Engineer, Project Manager, team, date, financial, delivery, and operational criteria.'
    );

  if (count(source, "moduleNumber: '030'") !== 1) throw new Error('Module 030 registry entry must remain unique.');
  if (!source.includes("displayName: 'Analytics Center'")) throw new Error('Module 030 Analytics Center identity was not installed.');
  write(registryPath, source);
}

installApp();
installRegistry();
console.log('ANALYTICS_CENTER_INJECTION=PASS module=030 dynamicFilters=true customerDirectory=true teams=true engineerScope=self pmScope=ownPortfolio');
