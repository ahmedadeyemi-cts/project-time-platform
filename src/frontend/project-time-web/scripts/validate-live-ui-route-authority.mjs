import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const webRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const read = (relativePath) => fs.readFileSync(path.join(webRoot, relativePath), 'utf8');
const exists = (relativePath) => fs.existsSync(path.join(webRoot, relativePath));

const files = {
  generated: 'src/App.Module001.g.jsx',
  distIndex: 'dist/index.html',
  chain: 'scripts/inject-celar-ai-enterprise-chat-context.mjs',
  injector: 'scripts/inject-live-ui-route-authority.mjs',
  main: 'src/main.jsx',
  viewAsCompatibility: 'src/view-as-storage-compatibility.js',
  analyticsAuthority: 'src/legacy-analytics-overlay-authority.js',
  celarPlatform: 'src/CelarAiEnterprisePlatform.jsx',
  architecture: 'src/CelarAiArchitectureOverview.jsx',
  microsoftPortal: 'src/MicrosoftIntegrationDualConnectionPortal.jsx',
  mailPanel: 'src/MicrosoftMailTransportReadinessPanel.jsx',
  pageContext: 'src/PageContextGuide.jsx',
  moduleAvailabilityBridge: 'src/module-availability-bridge.js',
  moduleNavigationAccessPolicy: 'src/module-navigation-access-policy.js'
};

let checks = 0;
let failures = 0;
function test(name, condition, evidence = '') {
  checks += 1;
  if (!condition) failures += 1;
  console.log(`LIVE_UI_${name}=${condition ? 'PASSED' : 'FAILED'}${evidence ? ` — ${evidence}` : ''}`);
}

for (const [name, relativePath] of Object.entries(files)) {
  test(`FILE_${name.toUpperCase()}`, exists(relativePath), relativePath);
}

const generated = read(files.generated);
const chain = read(files.chain);
const injector = read(files.injector);
const main = read(files.main);
const viewAsCompatibility = read(files.viewAsCompatibility);
const analyticsAuthority = read(files.analyticsAuthority);
const celarPlatform = read(files.celarPlatform);
const architecture = read(files.architecture);
const microsoftPortal = read(files.microsoftPortal);
const mailPanel = read(files.mailPanel);
const pageContext = read(files.pageContext);
const moduleAvailabilityBridge = read(files.moduleAvailabilityBridge);
const moduleNavigationAccessPolicy = read(files.moduleNavigationAccessPolicy);

const requiredAliases = [
  "'celar-ai': 'work-task-builder'",
  "'pulse-ai': 'work-task-builder'",
  "'analytics': 'reporting'",
  "'analytics-center': 'reporting'",
  "'reports': 'reporting'",
  "'financial-report-center': 'reporting'",
  "'crm': 'crm-integration'",
  "'crm-erp': 'crm-integration'",
  "'microsoft-integration': 'entra-secret-administration'",
  "'module-065': 'entra-secret-administration'"
];

test('BUILD_CHAIN', chain.includes("await import('./inject-live-ui-route-authority.mjs');"));
test('INJECTOR_READS_GENERATED_APP', injector.includes('App.Module001.g.jsx'));
test('ROUTE_ALIASES', requiredAliases.every((marker) => generated.includes(marker)), requiredAliases.join(', '));
test('ROUTE_QUERY_NORMALIZATION', generated.includes(".replace(/^#/, '').split('?')[0].trim()"));
test('SUPER_ADMIN_ROUTE_AUTHORITY',
  generated.includes('/* LIVE_AUTHENTICATED_ROUTE_AUTHORITY_START */')
    && generated.includes('actualSessionHasPermanentFullControl')
    && generated.includes("'SUPER_ADMINISTRATOR'")
    && generated.includes("'ADMINISTRATOR'")
    && generated.includes('securityContext.data?.permanentFullControl === true')
    && generated.includes('return actualSessionHasPermanentFullControl')
    && generated.includes('|| permissionCodes.some((permissionCode) => hasPermission(permissionCode));'),
  'shared permanent actual-session authority gate');
test('VIEW_AS_REMAINS_READ_ONLY',
  generated.includes('const actualSessionIsViewAs = Boolean(securityContext.data?.isViewAs) || localViewAsIsActive();')
    && generated.includes('const actualSessionHasPermanentFullControl = !actualSessionIsViewAs')
    && main.indexOf("import './view-as-storage-compatibility.js';") >= 0
    && main.indexOf("import './view-as-storage-compatibility.js';") < main.indexOf("import App from './App.Module001.g.jsx';")
    && viewAsCompatibility.includes("const LEGACY_VIEW_AS_KEY = 'projectPulseViewAsUserId';")
    && viewAsCompatibility.includes('window.localStorage.removeItem(LEGACY_VIEW_AS_KEY);'),
  'current and legacy View-As state fail closed before App renders');
test('ANALYTICS_NATIVE_MOUNT', generated.includes('<AnalyticsCenter authSession={authSession} />')
  && generated.includes('data-authoritative-module="030"'));
test('ANALYTICS_SINGLE_MOUNT', generated.split('<AnalyticsCenter authSession={authSession} />').length - 1 === 1);
test('ANALYTICS_RUNTIME_AUTHORITY_IMPORTED', main.includes("import './legacy-analytics-overlay-authority.js';"));
test('ANALYTICS_RUNTIME_AUTHORITY_PRECEDES_PORTALS',
  main.indexOf("import './legacy-analytics-overlay-authority.js';") >= 0
  && main.indexOf("import './legacy-analytics-overlay-authority.js';") < main.indexOf('import MicrosoftIntegrationDualConnectionPortal'));
test('RAW_HASH_CANONICALIZATION', analyticsAuthority.includes('function canonicalizeRuntimeHash()')
  && analyticsAuthority.includes('window.history.replaceState')
  && analyticsAuthority.includes("'celar-ai': 'work-task-builder'")
  && analyticsAuthority.includes("'analytics-center': 'reporting'")
  && analyticsAuthority.includes("'crm-erp': 'crm-integration'")
  && analyticsAuthority.includes("'microsoft-integration': 'entra-secret-administration'")
  && analyticsAuthority.includes("'module-065': 'entra-secret-administration'")
  && analyticsAuthority.includes("'global-mail-configuration': 'entra-secret-administration'")
  && analyticsAuthority.includes('projectpulse:route-canonicalized'));
test('LEGACY_ANALYTICS_OVERLAY_DISABLED', analyticsAuthority.includes('MODULE_030_NATIVE_REACT_ROUTE')
  && analyticsAuthority.includes('MutationObserver')
  && analyticsAuthority.includes("'projectpulse-030-shell'")
  && analyticsAuthority.includes("'projectpulse-030-reporting-card'")
  && analyticsAuthority.includes('display: none !important')
  && analyticsAuthority.includes('removeRetiredModule030Overlay')
  && analyticsAuthority.includes("window.addEventListener('hashchange', scheduleCleanup)"));
test('CRM_NATIVE_MOUNT', generated.includes('<CrmErpIntegrationCenter />'));
test('CELAR_NATIVE_MOUNT', generated.includes('<WorkTaskBuilderPanel />'));
test('CELAR_ARCHITECTURE_MOUNT', celarPlatform.includes('<CelarAiArchitectureOverview />')
  && architecture.includes('Celar AI Architecture Overview')
  && architecture.includes('<svg'));
test('CELAR_PAGE_CONTEXT', pageContext.includes("'work-task-builder': {")
  && pageContext.includes("page: 'Celar AI — Module 011'")
  && pageContext.includes('/api/celar-ai/v2/chat')
  && pageContext.includes('/api/project-flowhive/ai/production-generate'));
test('MODULE_NAVIGATION_AUTHORITY_CONVERGED',
  moduleAvailabilityBridge.includes("nativeFetch('/api/security/me', request)")
    && moduleAvailabilityBridge.includes('security?.permanentFullControl === true')
    && moduleAvailabilityBridge.includes('canonicalRoleCode')
    && moduleAvailabilityBridge.includes("'GLOBAL_ADMINISTRATOR'")
    && moduleAvailabilityBridge.includes('isViewAs: effectiveViewAs')
    && moduleAvailabilityBridge.includes("authoritySource: effectiveActor.authoritySource || ''")
    && moduleAvailabilityBridge.includes('resolveModuleNavigationAccess')
    && moduleNavigationAccessPolicy.includes('roleSet.has(roleCodeOf(grant))'),
  'Module directory links share actual-session authority with route mounting');
test('GUIDED_CLOSEOUT_ONLY', generated.includes('<ProjectCloseoutCenter />')
  && !generated.includes('<FinancialOperationsRecoveryWorkspace moduleCode="040"'));
test('MICROSOFT_PORTAL_CANONICAL_ROUTE', microsoftPortal.includes("const ACTIVE_ROUTE = 'entra-secret-administration'")
  && analyticsAuthority.includes("'microsoft-integration': 'entra-secret-administration'")
  && analyticsAuthority.includes("'module-065': 'entra-secret-administration'"));
test('MODULE065_READINESS_VISIBLE', mailPanel.includes("const ROUTE = 'entra-secret-administration'")
  && mailPanel.includes("'microsoft-integration': ROUTE")
  && mailPanel.includes('Test sender and transport')
  && mailPanel.includes('No live message is sent.'));

let bundle = '';
if (exists(files.distIndex)) {
  const distIndex = read(files.distIndex);
  const match = distIndex.match(/src="([^"]+\.js)"/);
  if (match) {
    const relative = match[1].replace(/^\//, '');
    const bundlePath = path.join(webRoot, 'dist', relative);
    if (fs.existsSync(bundlePath)) bundle = fs.readFileSync(bundlePath, 'utf8');
  }
}

test('PRODUCTION_BUNDLE_FOUND', bundle.length > 0);
test('PRODUCTION_BUNDLE_ANALYTICS', bundle.includes('Analytics Center'));
test('PRODUCTION_BUNDLE_ANALYTICS_AUTHORITY', bundle.includes('MODULE_030_NATIVE_REACT_ROUTE'));
test('PRODUCTION_BUNDLE_HASH_CANONICALIZATION', bundle.includes('projectpulse:route-canonicalized'));
test('PRODUCTION_BUNDLE_CELAR_ARCHITECTURE', bundle.includes('Celar AI Architecture Overview'));
test('PRODUCTION_BUNDLE_SUPERADMIN', bundle.includes('Full Control · Organization-wide'));
test('PRODUCTION_BUNDLE_SMTP_READINESS', bundle.includes('Test sender and transport'));

console.log(`LIVE_UI_VALIDATION_CHECKS=${checks}`);
console.log(`LIVE_UI_ROUTE_AUTHORITY_CONTRACT=${failures ? 'FAILED' : 'PASSED'}`);
process.exitCode = failures ? 1 : 0;
