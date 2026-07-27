import fs from 'node:fs';
import path from 'node:path';

const frontendRoot = process.cwd();
const repositoryRoot = path.resolve(frontendRoot, '..', '..', '..');
const file = (relative) => path.join(repositoryRoot, relative);
const read = (relative) => fs.readFileSync(file(relative), 'utf8');
const exists = (relative) => fs.existsSync(file(relative));
const checks = [];

function check(name, condition, evidence) {
  checks.push({ name, condition, evidence });
  console.log(`ADMIN_RUNTIME_STABILITY_${name}=${condition ? 'PASSED' : 'FAILED'} — ${evidence}`);
}

const paths = {
  main: 'src/frontend/project-time-web/src/main.jsx',
  runtime: 'src/frontend/project-time-web/src/runtime-data-compatibility.js',
  timer: 'src/frontend/project-time-web/src/module001/TimesheetEnhancementPortal.jsx',
  audit: 'src/frontend/project-time-web/src/AuditHistoryPanel.jsx',
  stableOwner: 'src/frontend/project-time-web/src/AdminRuntimeStabilityPortal.jsx',
  stableCss: 'src/frontend/project-time-web/src/admin-runtime-stability.css',
  theme: 'src/frontend/project-time-web/src/admin-experience-theme.js',
  themeCss: 'src/frontend/project-time-web/src/admin-experience-theme.css',
  microsoftCompat: 'src/frontend/project-time-web/src/microsoft-integration-compatibility.js',
  microsoftCss: 'src/frontend/project-time-web/src/microsoft-integration-portal.css',
  mailUi: 'src/frontend/project-time-web/src/MicrosoftMailTransportReadinessPanel.jsx',
  mailTest: 'src/backend/ProjectTime.Api/Modules/MicrosoftMailTransportTestModule.cs',
  registrar: 'src/backend/ProjectTime.Api/Modules/GlobalMailConfigurationModule.cs',
  continuity: 'src/backend/ProjectTime.Api/Modules/ModuleAvailabilityReadContinuityCompatibility.cs',
  package: 'src/frontend/project-time-web/package.json'
};

for (const [name, relative] of Object.entries(paths)) {
  check(`${name.toUpperCase()}_EXISTS`, exists(relative), relative);
}

const main = read(paths.main);
const runtime = read(paths.runtime);
const timer = read(paths.timer);
const audit = read(paths.audit);
const stableOwner = read(paths.stableOwner);
const stableCss = read(paths.stableCss);
const theme = read(paths.theme);
const themeCss = read(paths.themeCss);
const microsoftCompat = read(paths.microsoftCompat);
const microsoftCss = read(paths.microsoftCss);
const mailUi = read(paths.mailUi);
const mailTest = read(paths.mailTest);
const registrar = read(paths.registrar);
const continuity = read(paths.continuity);
const packageJson = JSON.parse(read(paths.package));

check('ROLE_POLICY_DIRECT_VALIDATION', runtime.includes("DIRECT_ROLE_POLICY_MARKER = 'projectpulse-role-policy-direct-fetch-v3'")
  && runtime.includes("'/api/role-policy/summary': '/api/runtime/v2/role-policy/summary'")
  && runtime.includes("'/api/runtime/v2/role-policy/summary': '/api/role-policy/summary'")
  && runtime.includes('hasCollections(normalized, collections)')
  && runtime.includes("status: 'role_policy_contract_mismatch'")
  && runtime.includes('attempts'),
'role-policy reads use direct session transport, validated legacy/v2 fallbacks, and explicit mismatch evidence');

check('ROLE_POLICY_MODULE_ATTRIBUTION', runtime.includes("currentRoute() === 'roles-permissions-matrix'")
  && runtime.includes("return '037'")
  && runtime.includes("return '012'")
  && runtime.includes("headers.set('X-ProjectPulse-Module-Number', moduleNumber)"),
'Modules 012 and 037 are attributed explicitly on direct role-policy reads');

check('ROLE_POLICY_NO_EMPTY_COLLECTION_FABRICATION', !runtime.includes('collections.map((name) => [name, []])')
  && !runtime.includes('normalized[name] = []')
  && !runtime.includes('return { roles: [], modules: [] }'),
'missing role-policy collections are reported and never fabricated');

check('TIMER_ROUTE_SCOPED', timer.includes('function isTimesheetRoute()')
  && timer.includes("=== 'timesheet'")
  && timer.includes("'X-ProjectPulse-Module-Number': '001'")
  && timer.includes('async function loadTimerTargets(weekStart)')
  && !timer.includes('new MutationObserver')
  && !timer.includes("import { authoritativeApi }"),
'Timer-target requests run only on Module 001 through a validated direct transport');

check('AUDIT_STABLE_OWNER', main.includes('<AdminRuntimeStabilityPortal />')
  && stableOwner.includes('window.__projectPulseModule008StableOwnerInstalled = true')
  && stableOwner.includes('<AuditHistoryPanel stableRouteOwner />')
  && audit.includes('window.__projectPulseModule008StableOwnerInstalled')
  && audit.includes('&& !stableRouteOwner)')
  && audit.includes('return null;')
  && stableCss.includes('.admin-runtime-stability-route-root'),
'Module 008 has one root-owned stable route surface independent of transient permission collections');

check('AUDIT_MODULE010_CONSOLIDATION', stableOwner.includes('Synchronization history is consolidated in Module 008')
  && stableOwner.includes('#audit-history?category=integration')
  && audit.includes('Module 010 sync evidence')
  && microsoftCss.includes('.route-azure-admin .azure-sync-runs-card'),
'Module 010 local sync history presentation is consolidated into Module 008');

check('NO_REACT_DOM_MUTATION_BRIDGES', !theme.includes('MutationObserver')
  && !theme.includes('node.remove()')
  && !theme.includes('removeChild(')
  && !microsoftCompat.includes('MutationObserver')
  && !microsoftCompat.includes('querySelectorAll(')
  && !microsoftCompat.includes('style.setProperty')
  && !microsoftCompat.includes('.hidden ='),
'compatibility bridges no longer insert, remove, hide, or move React-owned nodes');

check('THEME_ICON_NO_RELOAD', theme.includes("button.textContent = ''")
  && theme.includes("document.addEventListener('click', handleThemeClick, true)")
  && theme.includes("new CustomEvent('projectpulse:theme-changed'")
  && !theme.includes('window.location.reload')
  && themeCss.includes("content: '☾'")
  && themeCss.includes("content: '☀'"),
'icon-only theme changes do not reload the application or remove DOM nodes');

check('MODULE010_RESPONSIVE_ACTIONS', microsoftCss.includes('.route-azure-admin .azure-admin-heading-actions')
  && microsoftCss.includes('.route-azure-admin .azure-selection-toolbar')
  && microsoftCss.includes('flex-wrap: wrap !important')
  && microsoftCss.includes('overflow-x: auto')
  && microsoftCss.includes('repeat(auto-fit, minmax(min(100%, 220px), 1fr))'),
'Module 010 preview/import controls and tables remain visible at constrained widths');

check('MODULE010_STRICT_SERVICES_ACTIVATION', microsoftCompat.includes('applyStoredServicesProfile')
  && microsoftCompat.includes("String(tenant?.environmentMode || '').toLowerCase() === runtimeEnvironment")
  && microsoftCompat.includes('applyPayload?.runtimeActivated !== true')
  && microsoftCompat.includes("String(applyPayload?.runtimeEnvironment || '').toLowerCase() !== profile.environmentMode")
  && microsoftCompat.includes("status: 'module_065_services_profile_not_active'"),
'Module 010 preview stops unless Module 065 activates the matching running-environment services profile');

check('AVAILABILITY_READ_CONTINUITY', registrar.includes('UseModuleAvailabilityReadContinuityCompatibility')
  && continuity.includes('/api/runtime/v2/role-policy/')
  && continuity.includes('/api/admin/audit-history/events')
  && continuity.includes('/api/admin/azure/users/preview')
  && continuity.includes('/api/microsoft-integration/mail-runtime/test')
  && !continuity.includes('/api/admin/azure/users/import-selected')
  && !continuity.includes('/api/microsoft-integration/directory-users/import-selected'),
'optional module availability storage cannot block authorized read/test flows and never bypasses writes');

check('SSO_PUBLIC_ORIGIN', registrar.includes('X-Forwarded-Host')
  && registrar.includes('X-Forwarded-Proto')
  && registrar.includes('request.Headers["Origin"]')
  && registrar.includes('request.Headers["Referer"]')
  && registrar.includes('.onenecklab.com')
  && registrar.includes('TrustedHost'),
'Module 065 resolves and validates the browser-facing SSO callback origin');

check('MAIL_TEST_REGISTERED', registrar.includes('MapMicrosoftMailTransportTestEndpoints')
  && mailTest.includes('TestPath = "/api/microsoft-integration/mail-runtime/test"')
  && main.includes('<MicrosoftMailTransportReadinessPanel />')
  && mailUi.includes("TEST_PATH = '/api/microsoft-integration/mail-runtime/test'"),
'Module 065 exposes the non-delivery sender and transport test through API and UI');

check('MAIL_TEST_NO_DELIVERY_OR_SECRET_RETURN', mailTest.includes('liveMessageSent = false')
  && mailTest.includes('outboxMessageCreated = false')
  && mailTest.includes('secretValuesReturned = false')
  && mailUi.includes('No live message is sent.')
  && !mailTest.includes('SendMail')
  && !mailTest.includes('/sendMail')
  && !mailTest.includes('SmtpClient.Send'),
'readiness testing cannot send email or return secret values');

check('MAIL_TEST_GRAPH_AND_SMTP', mailTest.includes('https://graph.microsoft.com/.default')
  && mailTest.includes('Mail.Send')
  && mailTest.includes('Directory.Read.All')
  && mailTest.includes('User.Read.All')
  && mailTest.includes('smtp.office365.com')
  && mailTest.includes('TcpClient')
  && mailTest.includes('No authentication or email send was attempted'),
'Graph and SMTP readiness are tested without live delivery');

check('MAIL_TEST_AUDITED', mailTest.includes('MICROSOFT_MAIL_TRANSPORT_TESTED')
  && mailTest.includes('AdminExperienceCommon.WriteAuditAsync')
  && mailTest.includes('Module 008'),
'sanitized readiness evidence is submitted to Module 008 when available');

check('BUILD_GUARD', packageJson.scripts?.build?.includes('validate:admin-runtime-stability')
  && packageJson.scripts?.['validate:admin-runtime-stability']?.includes('validate-admin-runtime-stability.mjs'),
'integrated runtime stability validation is permanent in the production build');

const failures = checks.filter((item) => !item.condition).map((item) => item.name);
console.log(`ADMIN_RUNTIME_STABILITY_VALIDATION_CHECKS=${checks.length}`);
if (failures.length) {
  console.error(`ADMIN_RUNTIME_STABILITY_FAILED_CHECKS=${failures.join(',')}`);
  console.error('ADMIN_RUNTIME_STABILITY_CONTRACT=FAILED');
  process.exit(1);
}
console.log('ADMIN_RUNTIME_STABILITY_CONTRACT=PASSED');
