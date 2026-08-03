import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const webRoot = path.resolve(scriptDirectory, '..');
const generatedAppPath = path.join(webRoot, 'src', 'App.Module001.g.jsx');
const viewAsCompatibilityPath = path.join(webRoot, 'src', 'view-as-storage-compatibility.js');
const APP_MARKER = '/* LIVE_UI_ROUTE_AUTHORITY_COMPATIBILITY */';

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
  const viewAsCompatibility = fs.existsSync(viewAsCompatibilityPath)
    ? fs.readFileSync(viewAsCompatibilityPath, 'utf8')
    : '';

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

  const governedPermissionGate = `  function localViewAsIsActive() {
    try {
      const legacyUserId = window.localStorage.getItem('projectPulseViewAsUserId')?.trim();
      const raw = window.localStorage.getItem('projectPulseViewAsUser');
      if (!raw) return Boolean(legacyUserId);
      return Boolean(JSON.parse(raw)?.userId || legacyUserId);
    } catch {
      return true;
    }
  }

  function hasActualAdministratorAuthority() {
    if (securityContext.data?.isViewAs || localViewAsIsActive()) return false;
    const canonicalRoleCode = (value) => String(value ?? '')
      .trim()
      .toUpperCase()
      .replace(/[^A-Z0-9]+/g, '_')
      .replace(/^_+|_+$/g, '');
    const roleCodes = securityContext.data?.roles?.map((role) =>
      canonicalRoleCode(role?.roleCode ?? role?.roleName)
    ) ?? [];
    const permissions = securityContext.data?.permissions?.map((permission) =>
      String(permission ?? '').toUpperCase()
    ) ?? [];
    return securityContext.data?.permanentFullControl === true
      || roleCodes.includes('SUPER_ADMINISTRATOR')
      || roleCodes.includes('SUPERADMINISTRATOR')
      || roleCodes.includes('GLOBAL_ADMINISTRATOR')
      || roleCodes.includes('GLOBALADMINISTRATOR')
      || roleCodes.includes('ADMINISTRATOR')
      || permissions.includes('SYSTEM_ADMINISTRATION')
      || permissions.includes('MANAGE_ALL');
  }

  function hasPermission(permissionCode) {
    return hasActualAdministratorAuthority()
      || (securityContext.data?.permissions?.includes(permissionCode) ?? false);
  }

  function canSeeAny(permissionCodes) {
    return hasActualAdministratorAuthority()
      || permissionCodes.some((permissionCode) => hasPermission(permissionCode));
  }`;

  const sharedAuthorityGatePresent = [
    '/* LIVE_AUTHENTICATED_ROUTE_AUTHORITY_START */',
    '/* LIVE_AUTHENTICATED_ROUTE_AUTHORITY_END */',
    'actualSessionHasPermanentFullControl',
    "window.localStorage.getItem('projectPulseViewAsUser')"
  ].every((marker) => source.includes(marker));
  const legacyViewAsCompatibilityPresent = [
    "const LEGACY_VIEW_AS_KEY = 'projectPulseViewAsUserId';",
    'window.localStorage.removeItem(LEGACY_VIEW_AS_KEY);'
  ].every((marker) => viewAsCompatibility.includes(marker));

  if (!sharedAuthorityGatePresent && !source.includes(governedPermissionGate)) {
    if (!source.includes(legacyPermissionGate)) {
      throw new Error('Live UI correction could not locate a supported generated permission gate.');
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
    "window.localStorage.getItem('projectPulseViewAsUser')",
    'data-authoritative-module="030"',
    '<AnalyticsCenter authSession={authSession} />',
    '<CrmErpIntegrationCenter />',
    '<WorkTaskBuilderPanel />',
    '<ProjectCloseoutCenter />'
  ]) {
    if (!source.includes(required)) throw new Error(`Generated runtime is missing: ${required}`);
  }

  const authorityContractPresent = sharedAuthorityGatePresent
    || source.includes('function hasActualAdministratorAuthority()');
  if (!authorityContractPresent) {
    throw new Error('Generated runtime is missing the governed actual-session authority contract.');
  }
  if (!legacyViewAsCompatibilityPresent && !source.includes("window.localStorage.getItem('projectPulseViewAsUserId')")) {
    throw new Error('Generated runtime is missing fail-closed legacy View-As compatibility.');
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

installGeneratedAppCorrection();

console.log('LIVE_UI_ROUTE_AUTHORITY=PASS');
console.log('LIVE_UI_ANALYTICS_OWNER=REACT_MODULE_030');
console.log('LIVE_UI_CELAR_AI_ARCHITECTURE_ROUTE=WORK_TASK_BUILDER');
console.log('LIVE_UI_SUPER_ADMIN_ROUTE_AUTHORITY=PERMANENT_FULL_CONTROL');
console.log('LIVE_UI_VIEW_AS_MUTATION_AUTHORITY=DENIED');
console.log('LIVE_UI_MODULE_040_DUPLICATE_RECOVERY=REMOVED');
