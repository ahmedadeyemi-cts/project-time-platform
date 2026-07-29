import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const webRoot = path.resolve(scriptDirectory, '..');
const sourceRoot = path.join(webRoot, 'src');
const importLine = "import ProjectNotificationAutomationCenter from './ProjectNotificationAutomationCenter.jsx';";
const markerStart = 'GROUP_4_PROJECT_NOTIFICATION_AUTOMATION_START';
const markerEnd = 'GROUP_4_PROJECT_NOTIFICATION_AUTOMATION_END';

const moduleTargets = [
  {
    file: 'CostOverrunAlertCenter.jsx',
    importAnchor: "import './cost-overrun-alert-center.css';",
    rootAnchor: '    <section className="cost-alert-center">',
    mount: '      <ProjectNotificationAutomationCenter workspace="routing" />'
  },
  {
    file: 'TimeComplianceCenter.jsx',
    importAnchor: "import './time-compliance-center.css';",
    rootAnchor: '    <section id="time-compliance" className="time-compliance-center">',
    mount: '      <ProjectNotificationAutomationCenter workspace="scheduling" />'
  },
  {
    file: 'CloseoutEmailAutomationCenter.jsx',
    importAnchor: "import './closeout-email-automation-center.css';",
    rootAnchor: '    <div className="closeout-email-center">',
    mount: '      <ProjectNotificationAutomationCenter workspace="closeout" />'
  },
  {
    file: 'ProjectManagerWorkloadCenter.jsx',
    importAnchor: "import './project-manager-workload-center.css';",
    rootAnchor: '    <section className="pm-workload-center">',
    mount: "      <ProjectNotificationAutomationCenter workspace={'pm'} />"
  }
];

function count(source, needle) {
  return source.split(needle).length - 1;
}

function write(filePath, source) {
  fs.writeFileSync(filePath, source.endsWith('\n') ? source : `${source}\n`, 'utf8');
}

function installModulePanel(configuration) {
  const filePath = path.join(sourceRoot, configuration.file);
  if (!fs.existsSync(filePath)) throw new Error(`Group 4 target is missing: ${configuration.file}`);
  let source = fs.readFileSync(filePath, 'utf8');

  if (!source.includes(importLine)) {
    if (!source.includes(configuration.importAnchor)) throw new Error(`Group 4 import anchor is missing in ${configuration.file}.`);
    source = source.replace(configuration.importAnchor, `${configuration.importAnchor}\n${importLine}`);
  }

  if (!source.includes(markerStart)) {
    if (!source.includes(configuration.rootAnchor)) throw new Error(`Group 4 root anchor is missing in ${configuration.file}.`);
    source = source.replace(configuration.rootAnchor, [
      configuration.rootAnchor,
      `      {/* ${markerStart} */}`,
      configuration.mount,
      `      {/* ${markerEnd} */}`
    ].join('\n'));
  }

  if (count(source, importLine) !== 1) throw new Error(`Group 4 import must appear exactly once in ${configuration.file}.`);
  if (count(source, markerStart) !== 1 || count(source, markerEnd) !== 1) throw new Error(`Group 4 markers must appear exactly once in ${configuration.file}.`);
  if (count(source, configuration.mount.trim()) !== 1) throw new Error(`Group 4 mount must appear exactly once in ${configuration.file}.`);
  write(filePath, source);
}

function installStandaloneModule032() {
  const appPath = path.join(sourceRoot, 'App.jsx');
  let app = fs.readFileSync(appPath, 'utf8');
  const appImportAnchor = "import CostOverrunAlertCenter from './CostOverrunAlertCenter.jsx';";
  const appImport = "import ProjectNotificationAutomationCenter from './ProjectNotificationAutomationCenter.jsx';";
  const routeAnchor = `      {(activeRoute === 'cost-alerts' && canSeeAny(['VIEW_COST_ALERTS', 'MANAGE_COST_ALERTS', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'])) ? (`;
  const routeBlock = [
    `      {/* GROUP_4_MODULE_032_ROUTE_START */}`,
    `      {(activeRoute === 'notification-delivery-monitor' && canSeeAny(['VIEW_NOTIFICATION_DELIVERY_MONITOR', 'MANAGE_NOTIFICATION_DELIVERY', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'])) ? (`,
    `        <section id="notification-delivery-monitor" className="panel notification-delivery-monitor-route-panel">`,
    `          <ProjectNotificationAutomationCenter workspace="delivery" />`,
    `        </section>`,
    `      ) : null}`,
    `      {/* GROUP_4_MODULE_032_ROUTE_END */}`,
    ``
  ].join('\n');

  if (!app.includes(appImport)) {
    if (!app.includes(appImportAnchor)) throw new Error('Group 4 App import anchor is missing.');
    app = app.replace(appImportAnchor, `${appImportAnchor}\n${appImport}`);
  }
  if (!app.includes('GROUP_4_MODULE_032_ROUTE_START')) {
    if (!app.includes(routeAnchor)) throw new Error('Group 4 App route anchor is missing.');
    app = app.replace(routeAnchor, `${routeBlock}${routeAnchor}`);
  }
  if (count(app, appImport) !== 1) throw new Error('Group 4 App import is not unique.');
  if (count(app, 'GROUP_4_MODULE_032_ROUTE_START') !== 1) throw new Error('Group 4 Module 032 route is not unique.');
  write(appPath, app);

  const registryPath = path.join(sourceRoot, 'module-availability-registry.js');
  let registry = fs.readFileSync(registryPath, 'utf8');
  const registryAnchor = "  Object.freeze({ moduleNumber: '030', route: 'reporting', displayName: 'Reporting', group: 'Reports & Workflow' }),";
  const registryEntry = "  Object.freeze({ moduleNumber: '032', route: 'notification-delivery-monitor', displayName: 'Notification Delivery Monitor', group: 'Reports & Workflow', description: 'Operational inbox for project notification dispatches, automatically derived recipients, Module 065 readiness, source failures, retries, and immutable delivery evidence.' }),";
  if (!registry.includes(registryEntry)) {
    if (!registry.includes(registryAnchor)) throw new Error('Group 4 registry anchor is missing.');
    registry = registry.replace(registryAnchor, `${registryAnchor}\n${registryEntry}`);
  }
  if (count(registry, registryEntry) !== 1) throw new Error('Group 4 Module 032 registry entry is not unique.');
  write(registryPath, registry);
}

moduleTargets.forEach(installModulePanel);
installStandaloneModule032();
console.log(`GROUP_4_PROJECT_NOTIFICATION_INJECTION=PASS files=${moduleTargets.map((item) => item.file).join(',')},App.jsx,module-availability-registry.js`);
