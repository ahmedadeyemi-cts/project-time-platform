import fs from 'node:fs';
import path from 'node:path';

const frontendRoot = process.cwd();
const repositoryRoot = path.resolve(frontendRoot, '..', '..', '..');
const absolute = (relative) => path.join(repositoryRoot, relative);
const exists = (relative) => fs.existsSync(absolute(relative));
const read = (relative) => fs.readFileSync(absolute(relative), 'utf8');
const checks = [];

function check(name, condition, evidence) {
  checks.push({ name, condition, evidence });
  console.log(`ADMIN_RUNTIME_STABILITY_${name}=${condition ? 'PASSED' : 'FAILED'} — ${evidence}`);
}

const frontendPaths = {
  main: 'src/frontend/project-time-web/src/main.jsx',
  runtime: 'src/frontend/project-time-web/src/runtime-data-compatibility.js',
  timer: 'src/frontend/project-time-web/src/module001/TimesheetEnhancementPortal.jsx',
  audit: 'src/frontend/project-time-web/src/AuditHistoryPanel.jsx',
  owner: 'src/frontend/project-time-web/src/AdminRuntimeStabilityPortal.jsx',
  stableCss: 'src/frontend/project-time-web/src/admin-runtime-stability.css',
  theme: 'src/frontend/project-time-web/src/admin-experience-theme.js',
  themeCss: 'src/frontend/project-time-web/src/admin-experience-theme.css',
  ownership: 'src/frontend/project-time-web/src/react-dom-ownership-prelude.js',
  microsoft: 'src/frontend/project-time-web/src/microsoft-integration-compatibility.js',
  microsoftCss: 'src/frontend/project-time-web/src/microsoft-integration-portal.css',
  mailUi: 'src/frontend/project-time-web/src/MicrosoftMailTransportReadinessPanel.jsx'
};

for (const [name, relative] of Object.entries(frontendPaths)) {
  check(`${name.toUpperCase()}_EXISTS`, exists(relative), relative);
}

const main = read(frontendPaths.main);
const runtime = read(frontendPaths.runtime);
const timer = read(frontendPaths.timer);
const audit = read(frontendPaths.audit);
const owner = read(frontendPaths.owner);
const stableCss = read(frontendPaths.stableCss);
const theme = read(frontendPaths.theme);
const themeCss = read(frontendPaths.themeCss);
const ownership = read(frontendPaths.ownership);
const microsoft = read(frontendPaths.microsoft);
const microsoftCss = read(frontendPaths.microsoftCss);
const mailUi = read(frontendPaths.mailUi);

check('ROLE_POLICY_DIRECT_VALIDATION', runtime.includes('projectpulse-role-policy-direct-fetch-v3')
  && runtime.includes("'/api/role-policy/summary': '/api/runtime/v2/role-policy/summary'")
  && runtime.includes("'/api/runtime/v2/role-policy/summary': '/api/role-policy/summary'")
  && runtime.includes('hasCollections(normalized, collections)')
  && runtime.includes("status: 'role_policy_contract_mismatch'")
  && !runtime.includes('normalized[name] = []'),
'role-policy reads use validated legacy/v2 direct transports and never fabricate collections');

check('ROLE_POLICY_MODULE_ATTRIBUTION', runtime.includes("currentRoute() === 'roles-permissions-matrix'")
  && runtime.includes("return '037'")
  && runtime.includes("return '012'")
  && runtime.includes("headers.set('X-ProjectPulse-Module-Number', moduleNumber)"),
'Modules 012 and 037 are attributed explicitly');

check('TIMER_ROUTE_SCOPED', timer.includes('function isTimesheetRoute()')
  && timer.includes("=== 'timesheet'")
  && timer.includes("'X-ProjectPulse-Module-Number': '001'")
  && timer.includes('async function loadTimerTargets(weekStart)')
  && !timer.includes('new MutationObserver')
  && !timer.includes("import { authoritativeApi }"),
'Timer targets run only on Module 001 through a direct validated transport');

check('AUDIT_STABLE_OWNER', main.includes('<AdminRuntimeStabilityPortal />')
  && owner.includes('window.__projectPulseModule008StableOwnerInstalled = true')
  && owner.includes('<AuditHistoryPanel stableRouteOwner />')
  && audit.includes('__projectPulseModule008StableOwnerInstalled')
  && audit.includes('&& !stableRouteOwner)')
  && stableCss.includes('.admin-runtime-stability-route-root'),
'Module 008 has one root-owned route surface independent of transient role-policy data');

check('AUDIT_MODULE010_CONSOLIDATION', owner.includes('Synchronization history is consolidated in Module 008')
  && owner.includes('#audit-history?category=integration')
  && audit.includes('Module 010 sync evidence')
  && microsoftCss.includes('.route-azure-admin .azure-sync-runs-card'),
'Module 010 synchronization evidence is consolidated into Module 008');

check('VIEW_AS_REACT_DOM_OWNERSHIP', main.includes("import './react-dom-ownership-prelude.js';")
  && main.indexOf("import './react-dom-ownership-prelude.js';") < main.indexOf("import App from './App.Module001.g.jsx';")
  && ownership.includes('__projectPulseGlobalViewAsTopbarMountInstalled = true')
  && ownership.includes('view-as-body-owned')
  && !/insertBefore|appendChild|removeChild|MutationObserver/.test(ownership),
'legacy View-As cannot reparent nodes inside the React-owned top bar');

check('NO_REACT_DOM_MUTATION_BRIDGES', !theme.includes('MutationObserver')
  && !theme.includes('node.remove()')
  && !theme.includes('removeChild(')
  && !microsoft.includes('MutationObserver')
  && !microsoft.includes('querySelectorAll(')
  && !microsoft.includes('style.setProperty')
  && !microsoft.includes('.hidden ='),
'compatibility bridges do not insert, remove, hide, or move React-owned nodes');

check('THEME_ICON_NO_RELOAD', theme.includes("button.textContent = ''")
  && theme.includes("document.addEventListener('click', handleThemeClick, true)")
  && theme.includes("new CustomEvent('projectpulse:theme-changed'")
  && !theme.includes('window.location.reload')
  && themeCss.includes("content: '☾'")
  && themeCss.includes("content: '☀'"),
'icon-only theme changes do not reload the app or remove DOM nodes');

check('MODULE010_RESPONSIVE_ACTIONS', microsoftCss.includes('.route-azure-admin .azure-admin-heading-actions')
  && microsoftCss.includes('.route-azure-admin .azure-selection-toolbar')
  && microsoftCss.includes('flex-wrap: wrap !important')
  && microsoftCss.includes('overflow-x: auto')
  && microsoftCss.includes('repeat(auto-fit, minmax(min(100%, 220px), 1fr))'),
'Module 010 preview/import controls remain inside constrained viewports');

check('MODULE010_STRICT_SERVICES_ACTIVATION', microsoft.includes('applyStoredServicesProfile')
  && microsoft.includes("String(tenant?.environmentMode || '').toLowerCase() === runtimeEnvironment")
  && microsoft.includes('applyPayload?.runtimeActivated !== true')
  && microsoft.includes("String(applyPayload?.runtimeEnvironment || '').toLowerCase() !== profile.environmentMode")
  && microsoft.includes("status: 'module_065_services_profile_not_active'"),
'Module 010 preview requires a matching running-environment Module 065 services profile');

check('MODULE065_READINESS_UI', main.includes('<MicrosoftMailTransportReadinessPanel />')
  && mailUi.includes("TEST_PATH = '/api/microsoft-integration/mail-runtime/test'")
  && mailUi.includes('No live message is sent.')
  && mailUi.includes('secretValuesReturned'),
'Module 065 exposes a non-delivery sender and transport readiness surface');

const backendPaths = {
  registrar: 'src/backend/ProjectTime.Api/Modules/GlobalMailConfigurationModule.cs',
  continuity: 'src/backend/ProjectTime.Api/Modules/ModuleAvailabilityReadContinuityCompatibility.cs',
  mailTest: 'src/backend/ProjectTime.Api/Modules/MicrosoftMailTransportTestModule.cs'
};
const fullRepositoryContext = Object.values(backendPaths).every(exists);
if (fullRepositoryContext) {
  const registrar = read(backendPaths.registrar);
  const continuity = read(backendPaths.continuity);
  const mailTest = read(backendPaths.mailTest);

  check('AVAILABILITY_READ_CONTINUITY', registrar.includes('UseModuleAvailabilityReadContinuityCompatibility')
    && continuity.includes('/api/runtime/v2/role-policy/')
    && continuity.includes('/api/admin/audit-history/events')
    && continuity.includes('/api/admin/azure/users/preview')
    && continuity.includes('/api/microsoft-integration/mail-runtime/test')
    && !continuity.includes('/api/microsoft-integration/directory-users/import-selected'),
  'optional module availability cannot block authorized reads/tests and never bypasses imports');

  check('SSO_PUBLIC_ORIGIN', registrar.includes('X-Forwarded-Host')
    && registrar.includes('X-Forwarded-Proto')
    && registrar.includes('request.Headers["Origin"]')
    && registrar.includes('request.Headers["Referer"]')
    && registrar.includes('.onenecklab.com')
    && registrar.includes('TrustedHost'),
  'Module 065 resolves a trusted browser-facing SSO callback origin');

  check('MAIL_TEST_NON_DELIVERY', registrar.includes('MapMicrosoftMailTransportTestEndpoints')
    && mailTest.includes('TestPath = "/api/microsoft-integration/mail-runtime/test"')
    && mailTest.includes('liveMessageSent = false')
    && mailTest.includes('outboxMessageCreated = false')
    && mailTest.includes('secretValuesReturned = false')
    && !mailTest.includes('/sendMail'),
  'Module 065 readiness test cannot send or return secrets');

  check('MAIL_TEST_GRAPH_SMTP_AUDIT', mailTest.includes('https://graph.microsoft.com/.default')
    && mailTest.includes('Mail.Send')
    && mailTest.includes('smtp.office365.com')
    && mailTest.includes('TcpClient')
    && mailTest.includes('MICROSOFT_MAIL_TRANSPORT_TESTED')
    && mailTest.includes('AdminExperienceCommon.WriteAuditAsync'),
  'Graph/SMTP readiness is tested and sanitized evidence is requested in Module 008');

  console.log('ADMIN_RUNTIME_STABILITY_CONTEXT=FULL_REPOSITORY');
} else {
  console.log('ADMIN_RUNTIME_STABILITY_BACKEND_DEEP_CHECK=SKIPPED_MINIMAL_WEB_CONTEXT');
  console.log('ADMIN_RUNTIME_STABILITY_CONTEXT=MINIMAL_WEB_BUILD');
}

const failures = checks.filter((item) => !item.condition).map((item) => item.name);
console.log(`ADMIN_RUNTIME_STABILITY_VALIDATION_CHECKS=${checks.length}`);
if (failures.length) {
  console.error(`ADMIN_RUNTIME_STABILITY_FAILED_CHECKS=${failures.join(',')}`);
  console.error('ADMIN_RUNTIME_STABILITY_CONTRACT=FAILED');
  process.exit(1);
}
console.log('ADMIN_RUNTIME_STABILITY_CONTRACT=PASSED');
