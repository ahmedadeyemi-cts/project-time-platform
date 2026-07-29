import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const webRoot = path.resolve(scriptDirectory, '..');
const sourceRoot = path.join(webRoot, 'src');
const appPath = path.join(sourceRoot, 'App.jsx');
const registryPath = path.join(sourceRoot, 'module-availability-registry.js');
const importLine = "import FinancialOperationsRecoveryWorkspace from './FinancialOperationsRecoveryWorkspace.jsx';";

function count(source, needle) {
  return source.split(needle).length - 1;
}

function write(filePath, source) {
  fs.writeFileSync(filePath, source.endsWith('\n') ? source : `${source}\n`, 'utf8');
}

function installImport(app) {
  if (app.includes(importLine)) return app;
  const anchor = "import InvoiceBillingCenter from './InvoiceBillingCenter.jsx';";
  if (!app.includes(anchor)) throw new Error('Group 5 App import anchor is missing.');
  return app.replace(anchor, `${anchor}\n${importLine}`);
}

function installNavigationEntries(app) {
  const primaryAnchor = `  {
    route: "reporting",
    href: "#reporting",
    title: "Reporting / Accounting / Invoicing / Analytics",
    navLabel: "MODULE 030",
    description: "Provide operational, accounting, invoicing, workflow, system, and executive reporting.",
    permissions: ["VIEW_REPORTS", "MANAGE_REPORTS", "VIEW_EXECUTIVE_REPORTING", "VIEW_ACCOUNT_RECONCILIATION", "EXPORT_TIME_EXCEL", "EXPORT_TIME_PDF", "SYSTEM_ADMINISTRATION", "MANAGE_ALL"],
    roleCodes: ["ACCOUNTING", "PROJECT_TEAM_COORDINATOR", "EXECUTIVE", "EXECUTIVE_LEADERSHIP", "PROJECT_MANAGER", "PROJECT_MANAGEMENT", "ENGINEER", "ENGINEERING", "MANAGER", "SALES", "ACCOUNT_EXECUTIVE"],
  },`;
  const primaryReplacement = `  {
    route: "reporting",
    href: "#reporting",
    title: "Financial Report Center",
    navLabel: "MODULE 030",
    description: "Search, preview, run, export, and review history for actual role-scoped financial reports with independent source recovery.",
    permissions: ["VIEW_FINANCIAL_REPORT_CENTER", "RUN_FINANCIAL_REPORTS", "VIEW_REPORTS", "MANAGE_REPORTS", "VIEW_EXECUTIVE_REPORTING", "VIEW_ACCOUNT_RECONCILIATION", "EXPORT_TIME_EXCEL", "EXPORT_TIME_PDF", "SYSTEM_ADMINISTRATION", "MANAGE_ALL"],
    roleCodes: ["ACCOUNTING", "PROJECT_TEAM_COORDINATOR", "EXECUTIVE", "EXECUTIVE_LEADERSHIP", "PROJECT_MANAGER", "PROJECT_MANAGEMENT", "ENGINEER", "ENGINEERING", "MANAGER", "SALES", "INSIDE_SALES", "ACCOUNT_EXECUTIVE", "SOLUTION_ARCHITECT"],
  },
  {
    route: "financial-operations-workbench",
    href: "#financial-operations-workbench",
    title: "Financial Operations Workbench",
    navLabel: "MODULE 031",
    description: "One accountable queue for financial-source failures, billing blockers, closeout blockers, reconciliation exceptions, notification failures, retry, and resolution evidence.",
    permissions: ["VIEW_FINANCIAL_OPERATIONS_WORKBENCH", "MANAGE_FINANCIAL_OPERATIONS_RECOVERY", "RETRY_FINANCIAL_SOURCES", "SYSTEM_ADMINISTRATION", "MANAGE_ALL"],
    roleCodes: ["ACCOUNTING", "PROJECT_TEAM_COORDINATOR", "EXECUTIVE", "EXECUTIVE_LEADERSHIP", "PROJECT_MANAGER", "PROJECT_MANAGEMENT", "PROJECT_MANAGEMENT_LEAD"],
  },`;

  if (!app.includes('route: "financial-operations-workbench"')) {
    if (!app.includes(primaryAnchor)) throw new Error('Group 5 primary navigation anchor is missing.');
    app = app.replace(primaryAnchor, primaryReplacement);
  }

  const installedAnchor = `    {
      route: "reporting",
      href: "#reporting",
      title: "Reporting / Accounting / Invoicing / Analytics",
      navLabel: "MODULE 030",
      group: "Reports & Workflow",
      description: "Provide operational, accounting, invoicing, workflow, system, and executive reporting.",
      permissions: ["VIEW_REPORTS", "MANAGE_REPORTS", "VIEW_EXECUTIVE_REPORTING", "VIEW_ACCOUNT_RECONCILIATION", "EXPORT_TIME_EXCEL", "EXPORT_TIME_PDF", "SYSTEM_ADMINISTRATION", "MANAGE_ALL"],
    },`;
  const installedReplacement = `    {
      route: "reporting",
      href: "#reporting",
      title: "Financial Report Center",
      navLabel: "MODULE 030",
      group: "Reports & Workflow",
      description: "Search, preview, run, export, and review history for actual role-scoped financial reports with independent source recovery.",
      permissions: ["VIEW_FINANCIAL_REPORT_CENTER", "RUN_FINANCIAL_REPORTS", "VIEW_REPORTS", "MANAGE_REPORTS", "VIEW_EXECUTIVE_REPORTING", "VIEW_ACCOUNT_RECONCILIATION", "EXPORT_TIME_EXCEL", "EXPORT_TIME_PDF", "SYSTEM_ADMINISTRATION", "MANAGE_ALL"],
    },
    {
      route: "financial-operations-workbench",
      href: "#financial-operations-workbench",
      title: "Financial Operations Workbench",
      navLabel: "MODULE 031",
      group: "Reports & Workflow",
      description: "One accountable queue for financial-source failures, billing blockers, closeout blockers, reconciliation exceptions, notification failures, retry, and resolution evidence.",
      permissions: ["VIEW_FINANCIAL_OPERATIONS_WORKBENCH", "MANAGE_FINANCIAL_OPERATIONS_RECOVERY", "RETRY_FINANCIAL_SOURCES", "SYSTEM_ADMINISTRATION", "MANAGE_ALL"],
    },`;

  if (count(app, 'route: "financial-operations-workbench"') < 2) {
    if (!app.includes(installedAnchor)) throw new Error('Group 5 installed-module navigation anchor is missing.');
    app = app.replace(installedAnchor, installedReplacement);
  }

  return app;
}

function installStandaloneRoutes(app) {
  if (app.includes('GROUP_5_FINANCIAL_OPERATIONS_ROUTES_START')) return app;
  const anchor = `      {(activeRoute === 'project-closeout' && canSeeAny(['VIEW_PROJECT_WORKSPACE', 'VIEW_PROJECT_INTAKE', 'VIEW_APPROVAL_WORKFLOW', 'PROJECT_TIME_APPROVAL', 'VIEW_ACCOUNT_RECONCILIATION', 'VIEW_EXPENSES', 'EXPORT_TIME_EXCEL', 'DOWNLOAD_TIME_EXPORT_PACKAGE', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'])) ? (`;
  const routes = [
    `      {/* GROUP_5_FINANCIAL_OPERATIONS_ROUTES_START */}`,
    `      {(activeRoute === 'reporting' && canSeeAny(['VIEW_FINANCIAL_REPORT_CENTER', 'RUN_FINANCIAL_REPORTS', 'VIEW_REPORTS', 'MANAGE_REPORTS', 'VIEW_EXECUTIVE_REPORTING', 'VIEW_ACCOUNT_RECONCILIATION', 'EXPORT_TIME_EXCEL', 'EXPORT_TIME_PDF', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'])) ? (`,
    `        <section id="reporting" className="panel financial-report-center-route-panel">`,
    `          <FinancialOperationsRecoveryWorkspace mode="reporting" authSession={authSession} />`,
    `        </section>`,
    `      ) : null}`,
    ``,
    `      {(activeRoute === 'financial-operations-workbench' && canSeeAny(['VIEW_FINANCIAL_OPERATIONS_WORKBENCH', 'MANAGE_FINANCIAL_OPERATIONS_RECOVERY', 'RETRY_FINANCIAL_SOURCES', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'])) ? (`,
    `        <section id="financial-operations-workbench" className="panel financial-operations-workbench-route-panel">`,
    `          <FinancialOperationsRecoveryWorkspace mode="workbench" authSession={authSession} />`,
    `        </section>`,
    `      ) : null}`,
    `      {/* GROUP_5_FINANCIAL_OPERATIONS_ROUTES_END */}`,
    ``
  ].join('\n');
  if (!app.includes(anchor)) throw new Error('Group 5 standalone route anchor is missing.');
  return app.replace(anchor, `${routes}${anchor}`);
}

function installModulePanel(app, configuration) {
  if (app.includes(configuration.marker)) return app;
  if (!app.includes(configuration.anchor)) throw new Error(`Group 5 Module ${configuration.moduleCode} route anchor is missing.`);
  return app.replace(configuration.anchor, [
    configuration.anchor,
    `          {/* ${configuration.marker} */}`,
    `          <FinancialOperationsRecoveryWorkspace moduleCode="${configuration.moduleCode}" authSession={authSession} />`
  ].join('\n'));
}

function installApp() {
  let app = fs.readFileSync(appPath, 'utf8');
  app = installImport(app);
  app = installNavigationEntries(app);
  app = installStandaloneRoutes(app);
  app = installModulePanel(app, {
    moduleCode: '039',
    marker: 'GROUP_5_MODULE_039_RECOVERY_PANEL',
    anchor: '        <section id="billing-readiness" className="panel billing-readiness-route-panel">'
  });
  app = installModulePanel(app, {
    moduleCode: '040',
    marker: 'GROUP_5_MODULE_040_RECOVERY_PANEL',
    anchor: '        <section id="project-closeout" className="panel project-closeout-route-panel">'
  });
  app = installModulePanel(app, {
    moduleCode: '041',
    marker: 'GROUP_5_MODULE_041_RECOVERY_PANEL',
    anchor: '        <section id="closeout-email" className="panel closeout-email-route-panel">'
  });
  app = installModulePanel(app, {
    moduleCode: '042',
    marker: 'GROUP_5_MODULE_042_RECOVERY_PANEL',
    anchor: '        <section id="invoice-billing-center" className="panel invoice-billing-center-route-panel">'
  });

  if (count(app, importLine) !== 1) throw new Error('Group 5 App import must appear exactly once.');
  if (count(app, 'GROUP_5_FINANCIAL_OPERATIONS_ROUTES_START') !== 1) throw new Error('Group 5 standalone routes must appear exactly once.');
  for (const moduleCode of ['039', '040', '041', '042']) {
    if (count(app, `GROUP_5_MODULE_${moduleCode}_RECOVERY_PANEL`) !== 1) throw new Error(`Group 5 Module ${moduleCode} panel must appear exactly once.`);
  }
  if (count(app, 'route: "financial-operations-workbench"') !== 2) throw new Error('Group 5 Module 031 navigation entries must appear exactly twice.');
  write(appPath, app);
}

function installRegistry() {
  let registry = fs.readFileSync(registryPath, 'utf8');
  const anchor = "  Object.freeze({ moduleNumber: '030', route: 'reporting', displayName: 'Reporting', group: 'Reports & Workflow' }),";
  const replacement = [
    "  Object.freeze({ moduleNumber: '030', route: 'reporting', displayName: 'Financial Report Center', group: 'Reports & Workflow', description: 'Search, preview, run, export, and review history for actual role-scoped financial reports with independent source recovery.' }),",
    "  Object.freeze({ moduleNumber: '031', route: 'financial-operations-workbench', displayName: 'Financial Operations Workbench', group: 'Reports & Workflow', description: 'Accountable queue for source failures, billing and closeout blockers, reconciliation exceptions, notification failures, retry, and resolution evidence.' }),"
  ].join('\n');
  if (!registry.includes("moduleNumber: '031'")) {
    if (!registry.includes(anchor)) throw new Error('Group 5 module registry anchor is missing.');
    registry = registry.replace(anchor, replacement);
  }
  if (count(registry, "moduleNumber: '031'") !== 1) throw new Error('Group 5 Module 031 registry entry must appear exactly once.');
  if (count(registry, "moduleNumber: '030'") !== 1) throw new Error('Group 5 Module 030 registry entry must remain unique.');
  write(registryPath, registry);
}

installApp();
installRegistry();
console.log('GROUP_5_FINANCIAL_OPERATIONS_INJECTION=PASS files=App.jsx,module-availability-registry.js modules=030,031,039,040,041,042 module038=unchanged');
