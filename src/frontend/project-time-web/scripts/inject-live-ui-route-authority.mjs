import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const webRoot = path.resolve(scriptDirectory, '..');
const generatedAppPath = path.join(webRoot, 'src', 'App.Module001.g.jsx');
const indexPath = path.join(webRoot, 'index.html');

const APP_MARKER = '/* LIVE_UI_ROUTE_AUTHORITY_COMPATIBILITY */';
const INDEX_MARKER = 'MODULE_030_NATIVE_REACT_ROUTE';

function count(source, marker) {
  return source.split(marker).length - 1;
}

function write(filePath, source) {
  fs.writeFileSync(filePath, source.endsWith('\n') ? source : `${source}\n`, 'utf8');
}

function installGeneratedAppCorrection() {
  if (!fs.existsSync(generatedAppPath)) {
    throw new Error('Generate App.Module001.g.jsx before applying live UI route authority.');
  }

  let source = fs.readFileSync(generatedAppPath, 'utf8');

  const legacyNormalizer = `function normalizeRoute(hash) {
  const cleaned = (hash || window.location.hash || '#dashboard').replace('#', '').trim();
  return cleaned || 'dashboard';
}`;

  const governedNormalizer = `const PROJECTPULSE_RUNTIME_ROUTE_ALIASES = Object.freeze({
  'celar-ai': 'work-task-builder',
  'pulse-ai': 'work-task-builder',
  'analytics': 'reporting',
  'analytics-center': 'reporting',
  'reports': 'reporting',
  'executive-reporting': 'reporting',
  'financial-report-center': 'reporting',
  'enterprise-reporting': 'reporting',
  'crm': 'crm-integration',
  'crm-erp': 'crm-integration',
  'crm-erp-integration': 'crm-integration',
  'crm-integration-center': 'crm-integration',
  'microsoft-integration': 'entra-secret-administration',
  'module-065': 'entra-secret-administration',
  'psa-modules': 'toyota-hyundai-pipelines',
  'project-register': 'toyota-hyundai-pipelines',
  'project-manager-workload': 'project-workload',
  'project-management-workload': 'project-workload',
  'resource-assignment-handoff': 'signed-handoff',
  'global-mail-configuration': 'entra-secret-administration'
});

function normalizeRoute(hash) {
  const cleaned = (hash || window.location.hash || '#dashboard').replace(/^#/, '').split('?')[0].trim();
  return PROJECTPULSE_RUNTIME_ROUTE_ALIASES[cleaned] || cleaned || 'dashboard';
}`;

  if (!source.includes(governedNormalizer)) {
    if (!source.includes(legacyNormalizer)) {
      throw new Error('Live UI correction could not locate the generated route normalizer.');
    }
    source = source.replace(legacyNormalizer, governedNormalizer);
  }

  const legacyPermissionGate = `  function hasPermission(permissionCode) {
    return securityContext.data?.permissions?.includes(permissionCode) ?? false;
  }

  function canSeeAny(permissionCodes) {
    return permissionCodes.some((permissionCode) => hasPermission(permissionCode));
  }`;

  const governedPermissionGate = `  function hasActualAdministratorAuthority() {
    return !securityContext.data?.isViewAs && userIsAdministrator(securityContext.data);
  }

  function hasPermission(permissionCode) {
    return hasActualAdministratorAuthority()
      || (securityContext.data?.permissions?.includes(permissionCode) ?? false);
  }

  function canSeeAny(permissionCodes) {
    return hasActualAdministratorAuthority()
      || permissionCodes.some((permissionCode) => hasPermission(permissionCode));
  }`;

  if (!source.includes(governedPermissionGate)) {
    if (!source.includes(legacyPermissionGate)) {
      throw new Error('Live UI correction could not locate the generated permission gate.');
    }
    source = source.replace(legacyPermissionGate, governedPermissionGate);
  }

  source = source.replace(
    '<section id="reporting" className="panel financial-report-center-route-panel">',
    '<section id="reporting" className="analytics-center-route-panel" data-authoritative-module="030">'
  );

  const legacyCloseoutRecovery = `          {/* GROUP_5_MODULE_040_RECOVERY_PANEL */}
          <FinancialOperationsRecoveryWorkspace moduleCode="040" authSession={authSession} />
`;
  source = source.replace(legacyCloseoutRecovery, '');

  if (!source.includes(APP_MARKER)) {
    source = source.replace(
      '/* MODULE_001_GENERATOR_ALREADY_APPLIED - generated; do not edit */',
      `/* MODULE_001_GENERATOR_ALREADY_APPLIED - generated; do not edit */\n${APP_MARKER}`
    );
  }

  for (const required of [
    APP_MARKER,
    "'celar-ai': 'work-task-builder'",
    "'analytics': 'reporting'",
    "'financial-report-center': 'reporting'",
    "'crm-erp': 'crm-integration'",
    "'microsoft-integration': 'entra-secret-administration'",
    'function hasActualAdministratorAuthority()',
    '!securityContext.data?.isViewAs && userIsAdministrator(securityContext.data)',
    'data-authoritative-module="030"',
    '<AnalyticsCenter authSession={authSession} />',
    '<CrmErpIntegrationCenter />',
    '<WorkTaskBuilderPanel />',
    '<ProjectCloseoutCenter />'
  ]) {
    if (!source.includes(required)) throw new Error(`Generated runtime is missing: ${required}`);
  }

  if (source.includes('<FinancialOperationsRecoveryWorkspace moduleCode="040"')) {
    throw new Error('Legacy Module 040 recovery surface remains mounted beside the guided closeout.');
  }
  if (count(source, '<AnalyticsCenter authSession={authSession} />') !== 1) {
    throw new Error('The authoritative Analytics Center must be mounted exactly once.');
  }
  if (count(source, APP_MARKER) !== 1) {
    throw new Error('Live UI generated-app marker must appear exactly once.');
  }

  write(generatedAppPath, source);
}

function installLegacyAnalyticsSuppression() {
  if (!fs.existsSync(indexPath)) throw new Error('The frontend index.html target is missing.');
  let source = fs.readFileSync(indexPath, 'utf8');
  if (source.includes(INDEX_MARKER)) return;

  const anchor = `<script id="projectpulse-030-script">
(function () {
  if (window.__projectPulse030Installed) return;
  window.__projectPulse030Installed = true;`;

  const replacement = `<script id="projectpulse-030-script">
(function () {
  // ${INDEX_MARKER}: Module 030 is owned by the React AnalyticsCenter route.
  window.__projectPulse030Installed = true;
  window.__projectPulse030NativeReactRoute = true;

  var nativeStyle = document.getElementById('projectpulse-030-native-react-route-style');
  if (!nativeStyle) {
    nativeStyle = document.createElement('style');
    nativeStyle.id = 'projectpulse-030-native-react-route-style';
    nativeStyle.textContent = '#projectpulse-030-shell,#projectpulse-030-reporting-card{display:none!important}';
    document.head.appendChild(nativeStyle);
  }

  function removeRetiredModule030Overlay() {
    var shell = document.getElementById('projectpulse-030-shell');
    var card = document.getElementById('projectpulse-030-reporting-card');
    if (shell) shell.remove();
    if (card) card.remove();
  }

  removeRetiredModule030Overlay();
  document.addEventListener('DOMContentLoaded', removeRetiredModule030Overlay);
  window.addEventListener('hashchange', removeRetiredModule030Overlay);
  return;

  if (window.__projectPulse030Installed) return;
  window.__projectPulse030Installed = true;`;

  if (!source.includes(anchor)) {
    throw new Error('Live UI correction could not locate the legacy Module 030 overlay installer.');
  }
  source = source.replace(anchor, replacement);

  for (const required of [
    INDEX_MARKER,
    'window.__projectPulse030NativeReactRoute = true',
    'removeRetiredModule030Overlay',
    '#projectpulse-030-shell,#projectpulse-030-reporting-card{display:none!important}'
  ]) {
    if (!source.includes(required)) throw new Error(`Legacy Analytics suppression is missing: ${required}`);
  }
  if (count(source, INDEX_MARKER) !== 1) {
    throw new Error('Module 030 native React marker must appear exactly once.');
  }

  write(indexPath, source);
}

installGeneratedAppCorrection();
installLegacyAnalyticsSuppression();

console.log('LIVE_UI_ROUTE_AUTHORITY=PASS');
console.log('LIVE_UI_ANALYTICS_OWNER=REACT_MODULE_030');
console.log('LIVE_UI_CELAR_AI_ARCHITECTURE_ROUTE=WORK_TASK_BUILDER');
console.log('LIVE_UI_SUPER_ADMIN_ROUTE_AUTHORITY=PERMANENT_FULL_CONTROL');
console.log('LIVE_UI_VIEW_AS_MUTATION_AUTHORITY=DENIED');
console.log('LIVE_UI_MODULE_040_DUPLICATE_RECOVERY=REMOVED');
