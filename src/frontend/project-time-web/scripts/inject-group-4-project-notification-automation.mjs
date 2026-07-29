import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const webRoot = path.resolve(scriptDirectory, '..');
const sourceRoot = path.join(webRoot, 'src');
const appPath = path.join(sourceRoot, 'App.jsx');
const registryPath = path.join(sourceRoot, 'module-availability-registry.js');
const importLine = "import ProjectNotificationAutomationCenter from './ProjectNotificationAutomationCenter.jsx';";

function count(source, needle) {
  return source.split(needle).length - 1;
}

function write(filePath, source) {
  fs.writeFileSync(filePath, source.endsWith('\n') ? source : `${source}\n`, 'utf8');
}

function installApp() {
  if (!fs.existsSync(appPath)) throw new Error('Group 4 App.jsx target is missing.');
  let source = fs.readFileSync(appPath, 'utf8');

  if (!source.includes(importLine)) {
    const importAnchor = "import CostOverrunAlertCenter from './CostOverrunAlertCenter.jsx';";
    if (!source.includes(importAnchor)) throw new Error('Group 4 App import anchor is missing.');
    source = source.replace(importAnchor, `${importAnchor}\n${importLine}`);
  }

  const module032Navigation = [
    '  {',
    '    route: "notification-delivery-monitor",',
    '    href: "#notification-delivery-monitor",',
    '    title: "Notification Delivery Monitor",',
    '    navLabel: "MODULE 032",',
    '    description: "Monitor project notification dispatches, automatically derived recipients, source failures, retry evidence, and Module 065 governed delivery.",',
    '    permissions: ["VIEW_NOTIFICATION_DELIVERY_MONITOR", "MANAGE_NOTIFICATION_DELIVERY", "SYSTEM_ADMINISTRATION", "MANAGE_ALL"],',
    '    roleCodes: ["PROJECT_MANAGER", "PROJECT_MANAGEMENT", "PROJECT_MANAGEMENT_LEAD", "ACCOUNTING", "ACCOUNTING_BILLING", "BILLING", "FINANCE", "EXECUTIVE", "ENGINEERING", "ENGINEERING_LEAD", "SALES", "INSIDE_SALES", "SOLUTION_ARCHITECT", "MANAGER", "PROJECT_TEAM_COORDINATOR", "ADMINISTRATOR", "SUPER_ADMINISTRATOR"],',
    '  },'
  ].join('\n');

  if (!source.includes('route: "notification-delivery-monitor"')) {
    const navAnchor = '  {\n    route: "reporting",';
    if (!source.includes(navAnchor)) throw new Error('Group 4 primary navigation anchor is missing.');
    source = source.replace(navAnchor, `${module032Navigation}\n${navAnchor}`);
  }

  const installedEntry = [
    '    {',
    '      route: "notification-delivery-monitor",',
    '      href: "#notification-delivery-monitor",',
    '      title: "Notification Delivery Monitor",',
    '      navLabel: "MODULE 032",',
    '      group: "Reports & Workflow",',
    '      description: "Monitor project notification dispatches, automatically derived recipients, source failures, retry evidence, and Module 065 governed delivery.",',
    '      permissions: ["VIEW_NOTIFICATION_DELIVERY_MONITOR", "MANAGE_NOTIFICATION_DELIVERY", "SYSTEM_ADMINISTRATION", "MANAGE_ALL"],',
    '    },'
  ].join('\n');

  if (count(source, 'route: "notification-delivery-monitor"') < 2) {
    const installedAnchor = '    {\n      route: "reporting",';
    if (!source.includes(installedAnchor)) throw new Error('Group 4 installed module anchor is missing.');
    source = source.replace(installedAnchor, `${installedEntry}\n${installedAnchor}`);
  }

  if (!source.includes('GROUP_4_NOTIFICATION_DELIVERY_MONITOR_ROUTE')) {
    const routeAnchor = `      {(activeRoute === 'project-closeout' && canSeeAny(['VIEW_PROJECT_WORKSPACE', 'VIEW_PROJECT_INTAKE', 'VIEW_APPROVAL_WORKFLOW', 'PROJECT_TIME_APPROVAL', 'VIEW_ACCOUNT_RECONCILIATION', 'VIEW_EXPENSES', 'EXPORT_TIME_EXCEL', 'DOWNLOAD_TIME_EXPORT_PACKAGE', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'])) ? (`;
    if (!source.includes(routeAnchor)) throw new Error('Group 4 route mount anchor is missing.');
    const routeBlock = [
      '      {/* GROUP_4_NOTIFICATION_DELIVERY_MONITOR_ROUTE */}',
      `      {(activeRoute === 'notification-delivery-monitor' && canSeeAny(['VIEW_NOTIFICATION_DELIVERY_MONITOR', 'MANAGE_NOTIFICATION_DELIVERY', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'])) ? (`,
      '        <section id="notification-delivery-monitor" className="panel notification-delivery-monitor-route-panel">',
      '          <ProjectNotificationAutomationCenter mode="delivery-monitor" authSession={authSession} />',
      '        </section>',
      '      ) : null}',
      ''
    ].join('\n');
    source = source.replace(routeAnchor, `${routeBlock}${routeAnchor}`);
  }

  if (!source.includes('GROUP_4_MODULE_022_CONFIGURABLE_RULES')) {
    const module022Anchor = '        <section id="cost-alerts" className="panel cost-alert-route-panel">';
    if (!source.includes(module022Anchor)) throw new Error('Group 4 Module 022 anchor is missing.');
    source = source.replace(module022Anchor, [
      module022Anchor,
      '          {/* GROUP_4_MODULE_022_CONFIGURABLE_RULES */}',
      '          <ProjectNotificationAutomationCenter mode="routing-rules" authSession={authSession} />'
    ].join('\n'));
  }

  if (!source.includes('GROUP_4_MODULE_023_CONFIGURABLE_SCHEDULES')) {
    const module023Anchor = '        <section id="time-compliance" className="panel time-compliance-route-panel">';
    if (!source.includes(module023Anchor)) throw new Error('Group 4 Module 023 anchor is missing.');
    source = source.replace(module023Anchor, [
      module023Anchor,
      '          {/* GROUP_4_MODULE_023_CONFIGURABLE_SCHEDULES */}',
      '          <ProjectNotificationAutomationCenter mode="schedules" authSession={authSession} />'
    ].join('\n'));
  }

  if (count(source, importLine) !== 1) throw new Error('Group 4 import must appear exactly once.');
  if (count(source, 'GROUP_4_NOTIFICATION_DELIVERY_MONITOR_ROUTE') !== 1) throw new Error('Group 4 Module 032 route marker must appear once.');
  if (count(source, 'GROUP_4_MODULE_022_CONFIGURABLE_RULES') !== 1) throw new Error('Group 4 Module 022 marker must appear once.');
  if (count(source, 'GROUP_4_MODULE_023_CONFIGURABLE_SCHEDULES') !== 1) throw new Error('Group 4 Module 023 marker must appear once.');
  if (count(source, 'route: "notification-delivery-monitor"') !== 2) throw new Error('Group 4 Module 032 navigation entries must appear exactly twice.');

  write(appPath, source);
}

function installRegistry() {
  if (!fs.existsSync(registryPath)) throw new Error('Group 4 module registry target is missing.');
  let source = fs.readFileSync(registryPath, 'utf8');
  const legacyAnchor = "  Object.freeze({ moduleNumber: '030', route: 'reporting', displayName: 'Reporting', group: 'Reports & Workflow' }),";
  const financialReportAnchor = "  Object.freeze({ moduleNumber: '030', route: 'reporting', displayName: 'Financial Report Center', group: 'Reports & Workflow', description: 'Search, preview, run, export, and review history for actual role-scoped financial reports with independent source recovery.' }),";
  const module032 = "  Object.freeze({ moduleNumber: '032', route: 'notification-delivery-monitor', displayName: 'Notification Delivery Monitor', group: 'Reports & Workflow', description: 'Operational inbox for project notification dispatches, recipient derivation, Module 065 readiness, source failures, release, retry, and delivery evidence.' }),";
  if (!source.includes("moduleNumber: '032'")) {
    const anchor = source.includes(financialReportAnchor)
      ? financialReportAnchor
      : source.includes(legacyAnchor)
        ? legacyAnchor
        : null;
    if (!anchor) throw new Error('Group 4 registry anchor is missing.');
    source = source.replace(anchor, `${anchor}\n${module032}`);
  }
  if (count(source, "moduleNumber: '032'") !== 1) throw new Error('Group 4 Module 032 registry entry must appear exactly once.');
  write(registryPath, source);
}

installApp();
installRegistry();
console.log('GROUP_4_NOTIFICATION_AUTOMATION_INJECTION=PASS files=App.jsx,module-availability-registry.js modules=022,023,032,041,065');
