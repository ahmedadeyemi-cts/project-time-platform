import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const webRoot = path.resolve(scriptDirectory, '..');
const sourceRoot = path.join(webRoot, 'src');
const appPath = path.join(sourceRoot, 'App.jsx');
const registryPath = path.join(sourceRoot, 'module-availability-registry.js');
const importLine = "import EnterpriseReportingCenter from './EnterpriseReportingCenter.jsx';";
const importAnchor = "import FinancialOperationsRecoveryWorkspace from './FinancialOperationsRecoveryWorkspace.jsx';";
const legacyMount = '<FinancialOperationsRecoveryWorkspace mode="reporting" authSession={authSession} />';
const enterpriseMount = '<EnterpriseReportingCenter authSession={authSession} />';

function count(source, marker) {
  return source.split(marker).length - 1;
}

function write(filePath, source) {
  fs.writeFileSync(filePath, source.endsWith('\n') ? source : `${source}\n`, 'utf8');
}

function installApp() {
  if (!fs.existsSync(appPath)) throw new Error('Enterprise Reporting App.jsx target is missing.');
  let source = fs.readFileSync(appPath, 'utf8');
  if (!source.includes(importLine)) {
    if (!source.includes(importAnchor)) throw new Error('Enterprise Reporting requires the Group 5 reporting import anchor.');
    source = source.replace(importAnchor, `${importAnchor}\n${importLine}`);
  }

  if (!source.includes('ENTERPRISE_REPORTING_CENTER_MOUNT')) {
    if (!source.includes(legacyMount)) throw new Error('Enterprise Reporting Group 5 route mount anchor is missing.');
    source = source.replace(legacyMount, [
      '{/* ENTERPRISE_REPORTING_CENTER_MOUNT */}',
      enterpriseMount
    ].join('\n          '));
  }

  source = source.replaceAll('Financial Report Center', 'Enterprise Reporting Center');
  source = source.replaceAll(
    'Search, preview, run, export, and review history for actual role-scoped financial reports with independent source recovery.',
    'Run dynamic role-scoped reports across projects, customers, financials, time, people, delivery, operations, governance, and acceptance.'
  );

  if (count(source, importLine) !== 1) throw new Error('Enterprise Reporting App import must appear exactly once.');
  if (count(source, enterpriseMount) !== 1) throw new Error('Enterprise Reporting route mount must appear exactly once.');
  if (source.includes(legacyMount)) throw new Error('The legacy Module 030 Group 5 mount remains active.');
  write(appPath, source);
}

function installRegistry() {
  if (!fs.existsSync(registryPath)) throw new Error('Enterprise Reporting registry target is missing.');
  let source = fs.readFileSync(registryPath, 'utf8');
  source = source.replaceAll('Financial Report Center', 'Enterprise Reporting Center');
  source = source.replaceAll(
    'Search, preview, run, export, and review history for actual role-scoped financial reports with independent source recovery.',
    'Dynamic role-scoped reporting across projects, customers, financials, time, people, delivery, operations, governance, and acceptance.'
  );
  if (count(source, "moduleNumber: '030'") !== 1) throw new Error('Module 030 registry entry must remain unique.');
  if (!source.includes("displayName: 'Enterprise Reporting Center'")) throw new Error('Module 030 enterprise reporting identity was not installed.');
  write(registryPath, source);
}

installApp();
installRegistry();
console.log('ENTERPRISE_REPORTING_INJECTION=PASS module=030 dynamicFilters=true engineerScope=self pmScope=ownPortfolio');
