import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const webRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const read = (relativePath) => fs.readFileSync(path.join(webRoot, relativePath), 'utf8');
const exists = (relativePath) => fs.existsSync(path.join(webRoot, relativePath));

const files = {
  generated: 'src/App.Module001.g.jsx',
  index: 'index.html',
  distIndex: 'dist/index.html',
  chain: 'scripts/inject-celar-ai-enterprise-chat-context.mjs',
  injector: 'scripts/inject-live-ui-route-authority.mjs',
  celarPlatform: 'src/CelarAiEnterprisePlatform.jsx',
  architecture: 'src/CelarAiArchitectureOverview.jsx',
  mailPanel: 'src/MicrosoftMailTransportReadinessPanel.jsx'
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
const index = read(files.index);
const chain = read(files.chain);
const injector = read(files.injector);
const celarPlatform = read(files.celarPlatform);
const architecture = read(files.architecture);
const mailPanel = read(files.mailPanel);

const requiredAliases = [
  "'celar-ai': 'work-task-builder'",
  "'pulse-ai': 'work-task-builder'",
  "'analytics': 'reporting'",
  "'analytics-center': 'reporting'",
  "'reports': 'reporting'",
  "'financial-report-center': 'reporting'",
  "'crm': 'crm-integration'",
  "'crm-erp': 'crm-integration'",
  "'microsoft-integration': 'entra-secret-administration'"
];

test('BUILD_CHAIN', chain.includes("await import('./inject-live-ui-route-authority.mjs');"));
test('INJECTOR_READS_GENERATED_APP', injector.includes("App.Module001.g.jsx"));
test('ROUTE_ALIASES', requiredAliases.every((marker) => generated.includes(marker)), requiredAliases.join(', '));
test('ROUTE_QUERY_NORMALIZATION', generated.includes(".replace(/^#/, '').split('?')[0].trim()"));
test('SUPER_ADMIN_ROUTE_AUTHORITY', generated.includes('function hasActualAdministratorAuthority()')
  && generated.includes('!securityContext.data?.isViewAs && userIsAdministrator(securityContext.data)')
  && generated.includes('return hasActualAdministratorAuthority()')
  && generated.includes('|| permissionCodes.some((permissionCode) => hasPermission(permissionCode));'));
test('VIEW_AS_REMAINS_READ_ONLY', generated.includes('!securityContext.data?.isViewAs'));
test('ANALYTICS_NATIVE_MOUNT', generated.includes('<AnalyticsCenter authSession={authSession} />')
  && generated.includes('data-authoritative-module="030"'));
test('ANALYTICS_SINGLE_MOUNT', generated.split('<AnalyticsCenter authSession={authSession} />').length - 1 === 1);
test('LEGACY_ANALYTICS_OVERLAY_DISABLED', index.includes('MODULE_030_NATIVE_REACT_ROUTE')
  && index.includes('window.__projectPulse030NativeReactRoute = true')
  && index.includes('removeRetiredModule030Overlay')
  && index.includes('#projectpulse-030-shell,#projectpulse-030-reporting-card{display:none!important}'));
test('CRM_NATIVE_MOUNT', generated.includes('<CrmErpIntegrationCenter />'));
test('CELAR_NATIVE_MOUNT', generated.includes('<WorkTaskBuilderPanel />'));
test('CELAR_ARCHITECTURE_MOUNT', celarPlatform.includes('<CelarAiArchitectureOverview />')
  && architecture.includes('Celar AI Architecture Overview')
  && architecture.includes('<svg'));
test('GUIDED_CLOSEOUT_ONLY', generated.includes('<ProjectCloseoutCenter />')
  && !generated.includes('<FinancialOperationsRecoveryWorkspace moduleCode="040"'));
test('MODULE065_READINESS_VISIBLE', mailPanel.includes("const ROUTE = 'entra-secret-administration'")
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
test('PRODUCTION_BUNDLE_CELAR_ARCHITECTURE', bundle.includes('Celar AI Architecture Overview'));
test('PRODUCTION_BUNDLE_SUPERADMIN', bundle.includes('Full Control · Organization-wide'));
test('PRODUCTION_BUNDLE_SMTP_READINESS', bundle.includes('Test sender and transport'));

console.log(`LIVE_UI_VALIDATION_CHECKS=${checks}`);
console.log(`LIVE_UI_ROUTE_AUTHORITY_CONTRACT=${failures ? 'FAILED' : 'PASSED'}`);
process.exitCode = failures ? 1 : 0;
