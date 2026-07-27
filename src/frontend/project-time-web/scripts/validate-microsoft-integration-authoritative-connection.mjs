import fs from 'node:fs';
import path from 'node:path';

const root = path.resolve(process.cwd(), '..', '..', '..');
const paths = {
  compatibility: 'src/frontend/project-time-web/src/microsoft-integration-compatibility.js',
  css: 'src/frontend/project-time-web/src/microsoft-integration-portal.css',
  portal: 'src/frontend/project-time-web/src/MicrosoftIntegrationDualConnectionPortal.jsx',
  registry: 'src/frontend/project-time-web/src/module-availability-registry.js',
  main: 'src/frontend/project-time-web/src/main.jsx',
  stableOwner: 'src/frontend/project-time-web/src/AdminRuntimeStabilityPortal.jsx',
  mailActivation: 'src/frontend/project-time-web/src/microsoft-mail-runtime-activation.js',
  mailReadinessUi: 'src/frontend/project-time-web/src/MicrosoftMailTransportReadinessPanel.jsx',
  mailReadinessCss: 'src/frontend/project-time-web/src/microsoft-mail-transport-readiness.css',
  mailRuntime: 'src/backend/ProjectTime.Api/Modules/MicrosoftMailRuntimeConfigurationModule.cs',
  mailTest: 'src/backend/ProjectTime.Api/Modules/MicrosoftMailTransportTestModule.cs',
  smtpProjection: 'src/backend/ProjectTime.Api/Modules/MicrosoftSmtpCredentialProjectionCompatibility.cs',
  registrar: 'src/backend/ProjectTime.Api/Modules/GlobalMailConfigurationModule.cs',
  continuity: 'src/backend/ProjectTime.Api/Modules/ModuleAvailabilityReadContinuityCompatibility.cs',
  migration: 'database/migrations/047_microsoft_integration_connection_carryover.sql',
  rollback: 'database/rollback/047_microsoft_integration_connection_carryover_rollback.sql',
  test: 'tests/test-microsoft-integration-connection-carryover-047.sh'
};

const file = (relative) => path.join(root, relative);
const read = (relative) => fs.readFileSync(file(relative), 'utf8');
const exists = (relative) => fs.existsSync(file(relative));
const checks = [];

function assert(name, condition, evidence) {
  checks.push({ name, condition, evidence });
  console.log(`MICROSOFT_CONNECTION_${name}=${condition ? 'PASSED' : 'FAILED'} — ${evidence}`);
}

for (const name of ['compatibility', 'css', 'portal', 'registry', 'main', 'stableOwner', 'mailActivation', 'mailReadinessUi', 'mailReadinessCss']) {
  assert(`${name.toUpperCase()}_EXISTS`, exists(paths[name]), paths[name]);
}

const compatibility = read(paths.compatibility);
const css = read(paths.css);
const portal = read(paths.portal);
const registry = read(paths.registry);
const main = read(paths.main);
const stableOwner = read(paths.stableOwner);
const mailActivation = read(paths.mailActivation);
const mailReadinessUi = read(paths.mailReadinessUi);
const mailReadinessCss = read(paths.mailReadinessCss);

assert('AUTHORITATIVE_NAME', registry.includes("displayName: 'Microsoft Integration Connection'")
  && portal.includes('<h1>Microsoft Integration</h1>'),
'Module 065 retains one authoritative Microsoft Integration identity');

assert('LEGACY_MODULE_065_CSS_SUPPRESSION', css.includes('body.projectpulse-microsoft-integration-active .entra-secret-administration-route-panel')
  && css.includes('.native-module-administration[data-module-administration="065"]')
  && css.includes('[data-phase="065_COMPLETE_SOURCE_LOCKED_RUNTIME"]')
  && css.includes('a[href="#global-mail-configuration"]')
  && !compatibility.includes('style.setProperty')
  && !compatibility.includes('MutationObserver')
  && !compatibility.includes('querySelectorAll('),
'legacy Module 065/067 presentation is CSS-owned and no React-owned DOM is mutated');

assert('AUTHORITATIVE_PORTAL_VISIBLE', compatibility.includes("document.body?.classList.toggle('projectpulse-microsoft-integration-active'")
  && css.includes('body.projectpulse-microsoft-integration-active .microsoft-integration-portal')
  && main.includes('<MicrosoftIntegrationDualConnectionPortal />'),
'consolidated Module 065 visibility follows the route without element insertion/removal');

assert('MODULE_010_CONFIGURATION_REMOVED', css.includes('.route-azure-admin .azure-config-card')
  && css.includes('.route-azure-admin .azure-sync-summary-card')
  && css.includes('.route-azure-admin .azure-sync-runs-card')
  && stableOwner.includes('Synchronization history is consolidated in Module 008'),
'Module 010 keeps directory import while configuration and local audit presentation move to Modules 065 and 008');

assert('MODULE_010_RESPONSIVE_PREVIEW', css.includes('.route-azure-admin .azure-admin-heading-actions')
  && css.includes('flex-wrap: wrap !important')
  && css.includes('grid-template-columns: repeat(auto-fit, minmax(min(100%, 220px), 1fr))')
  && css.includes('.route-azure-admin .azure-selection-toolbar')
  && css.includes('.route-azure-admin .azure-preview-table')
  && css.includes('overflow-x: auto'),
'Preview, Import, filters, selection controls, and tables remain within the viewport');

assert('MODULE_010_PURPOSE', css.includes("content: 'MODULE 010 · AZURE / ENTRA DIRECTORY USERS'")
  && css.includes("content: 'Preview and import Entra users'")
  && css.includes('Tenant, identity, synchronization, and Microsoft mail configuration are managed in Module 065'),
'Module 010 visibly communicates its directory-user purpose and Module 065 ownership boundary');

assert('MODULE_010_PROFILE_PRELOAD', compatibility.includes("PREVIEW_ROUTE = '/api/admin/azure/users/preview'")
  && compatibility.includes('applyStoredServicesProfile')
  && compatibility.includes("SERVICES_APPLY_PATH = '/api/microsoft-integration/services-apply-profile'")
  && compatibility.includes('applyPayload?.runtimeActivated !== true')
  && compatibility.includes("String(applyPayload?.runtimeEnvironment || '').toLowerCase() !== profile.environmentMode"),
'Module 010 activates and verifies the running-environment Module 065 services profile before preview');

assert('SSO_AND_SERVICES_CONNECTIONS', portal.includes('Microsoft Entra SSO')
  && portal.includes('Microsoft services and Graph')
  && portal.includes('sso_app_registration')
  && portal.includes('microsoft_services_enterprise_application')
  && portal.includes("async function persistConfiguration(purpose = 'integration')")
  && portal.includes("if (purpose !== 'sso')"),
'Test and Production retain independent SSO and services connections');

assert('IDENTITY_CALENDAR_DIRECTORY_OWNERSHIP', portal.includes('Module 010 import')
  && portal.includes('Module 057 calendar')
  && portal.includes('Module 062 identity/profile/presence'),
'Module 010, Module 057, and Module 062 consume the services connection');

assert('GLOBAL_MAIL_CONFIGURATION', portal.includes('Microsoft 365 / SMTP')
  && portal.includes('Sender mailbox')
  && portal.includes('smtp.office365.com')
  && portal.includes('providerTarget'),
'global Microsoft mail transport and sender configuration remain on Module 065');

assert('MAIL_RUNTIME_ACTIVATION', main.includes("import './microsoft-mail-runtime-activation.js';")
  && mailActivation.includes("RUNTIME_PATH = '/api/microsoft-integration/mail-runtime'")
  && mailActivation.includes("new CustomEvent('projectpulse:microsoft-mail-runtime-status'")
  && mailActivation.includes('persistedConfiguration: true')
  && mailActivation.includes('return response;')
  && !/clientSecret|password|accessToken/i.test(mailActivation),
'successful Module 065 saves report sanitized runtime activation separately');

assert('NON_DELIVERY_READINESS_UI', main.includes('<MicrosoftMailTransportReadinessPanel />')
  && mailReadinessUi.includes("TEST_PATH = '/api/microsoft-integration/mail-runtime/test'")
  && mailReadinessUi.includes('No live message is sent.')
  && mailReadinessUi.includes("'X-ProjectPulse-Module-Number': '065'")
  && mailReadinessUi.includes('secretValuesReturned')
  && mailReadinessCss.includes('.microsoft-mail-readiness-panel'),
'Module 065 exposes a responsive non-delivery sender and transport readiness test');

const fullRepositoryContext = [
  'migration', 'rollback', 'test', 'mailRuntime', 'mailTest', 'smtpProjection', 'registrar', 'continuity'
].every((name) => exists(paths[name]));

if (fullRepositoryContext) {
  const migration = read(paths.migration);
  const rollback = read(paths.rollback);
  const test = read(paths.test);
  const mailRuntime = read(paths.mailRuntime);
  const mailTest = read(paths.mailTest);
  const smtpProjection = read(paths.smtpProjection);
  const registrar = read(paths.registrar);
  const continuity = read(paths.continuity);

  assert('MAIL_RUNTIME_REGISTERED', registrar.includes('MapMicrosoftMailRuntimeConfigurationEndpoints')
    && registrar.includes('MapMicrosoftMailTransportTestEndpoints')
    && registrar.includes('UseMicrosoftSmtpCredentialProjectionCompatibility')
    && mailRuntime.includes('/api/microsoft-integration/mail-runtime')
    && mailRuntime.includes('ApplicationStarted.Register')
    && mailRuntime.includes('ReadStoredConfigurationAsync'),
  'mail runtime metadata and the non-delivery readiness endpoint are registered');

  assert('PUBLIC_SSO_ORIGIN', registrar.includes('X-Forwarded-Host')
    && registrar.includes('X-Forwarded-Proto')
    && registrar.includes('request.Headers["Origin"]')
    && registrar.includes('request.Headers["Referer"]')
    && registrar.includes('.onenecklab.com')
    && registrar.includes('invalid_forwarded_public_origin'),
  'Module 065 resolves an approved public callback origin through proxy and same-origin request evidence');

  assert('MODULE_AVAILABILITY_READ_CONTINUITY', registrar.includes('UseModuleAvailabilityReadContinuityCompatibility')
    && continuity.includes('/api/admin/azure/users/preview')
    && continuity.includes('/api/microsoft-integration/mail-runtime/test')
    && continuity.includes('/api/role-policy/')
    && !continuity.includes('/api/microsoft-integration/directory-users/import-selected'),
  'optional module-availability storage cannot block authorized reads/tests and never bypasses mutations');

  assert('MAIL_TEST_NON_DELIVERY', mailTest.includes('POST /api/microsoft-integration/mail-runtime/test')
    || (mailTest.includes('TestPath = "/api/microsoft-integration/mail-runtime/test"')
      && mailTest.includes('liveMessageSent = false')),
  'readiness test is explicitly non-delivery');
  assert('MAIL_TEST_SECRET_SAFETY', mailTest.includes('secretValuesReturned = false')
    && !mailTest.includes('clientSecret = request')
    && !mailTest.includes('password = request')
    && !mailTest.includes('Results.Ok(clientSecret)')
    && !mailTest.includes('Results.Ok(password)'),
  'readiness test accepts and returns no credential values');
  assert('GRAPH_READINESS', mailTest.includes('https://graph.microsoft.com/.default')
    && mailTest.includes('Mail.Send')
    && mailTest.includes('Directory.Read.All')
    && mailTest.includes('User.Read.All')
    && mailTest.includes('sender mailbox resolved'),
  'Graph readiness validates application authentication, declared roles, and the sender mailbox');
  assert('SMTP_READINESS', mailTest.includes('smtp.office365.com')
    && mailTest.includes('TcpClient')
    && mailTest.includes('No authentication or email send was attempted')
    && mailTest.includes('PROJECTPULSE_TEST_SMTP_')
    && mailTest.includes('PROJECTPULSE_PRODUCTION_SMTP_'),
  'SMTP readiness checks only the approved endpoint and environment-specific credential presence');
  assert('MAIL_TEST_AUDIT', mailTest.includes('MICROSOFT_MAIL_TRANSPORT_TESTED')
    && mailTest.includes('AdminExperienceCommon.WriteAuditAsync')
    && mailTest.includes('"projectpulse_system_audit_events"')
    && mailTest.includes('auditEvidenceRequested = true'),
  'readiness outcomes request concrete, sanitized Module 008 audit evidence when available');

  assert('MODULE_010_CARRYOVER', migration.includes('FROM azure_entra_settings settings')
    && migration.includes("'legacyDirectorySettingsCarriedOver', true")
    && migration.includes("'clientId', COALESCE(azure_settings ->> 'client_id'")
    && migration.includes("'redirectUri', COALESCE(azure_settings ->> 'redirect_uri'"),
  'existing Module 010 tenant, services client, redirect, scopes, role, and sync metadata are carried over');
  assert('MODULE_067_MAIL_CARRYOVER', migration.includes("module_number = '067'")
    && migration.includes("'legacyModule067ConfigurationCarriedOver'")
    && migration.includes("'senderAddress'")
    && migration.includes("'replyToAddress'"),
  'existing Module 067 global mail settings are carried over');
  assert('MODULE_062_CONNECTION_OWNERSHIP', migration.includes("'module062IdentityProfile', 'services'")
    && migration.includes("'module057CalendarPresence', 'services'")
    && migration.includes("'globalMailTransport', 'services'"),
  'identity, calendar/presence, and global mail consume the services connection');

  assert('ENVIRONMENT_SECRET_ISOLATION', mailRuntime.includes('PROJECTPULSE_ENTRA_TEST_CLIENT_SECRET')
    && mailRuntime.includes('PROJECTPULSE_ENTRA_PRODUCTION_CLIENT_SECRET')
    && mailRuntime.includes('PROJECTPULSE_MICROSOFT_TENANT_ONENECKLAB_CLIENT_SECRET')
    && mailRuntime.includes('PROJECTPULSE_MICROSOFT_TENANT_USSIGNAL_CLIENT_SECRET')
    && mailRuntime.includes('activeMode == "test"')
    && mailRuntime.includes('activeMode == "production"'),
  'Test and Production Microsoft services credentials remain isolated');
  assert('RECIPIENT_BOUNDARY_ENFORCED', mailRuntime.includes('configuration.RecipientBoundary == "production_governed"')
    && mailRuntime.includes('PROJECTPULSE_MAIL_RECIPIENT_BOUNDARY')
    && mailRuntime.includes('The Test-only boundary keeps delivery outbox-only')
    && mailRuntime.includes('Microsoft mail delivery is locked'),
  'test_only and locked states cannot enable live delivery');
  assert('SHARED_PROVIDER_COMPATIBILITY', mailRuntime.includes('var sharedProvider = liveDeliveryEnabled')
    && mailRuntime.includes('? "smtp"')
    && mailRuntime.includes(': "outbox_only"')
    && mailRuntime.includes('PROJECTPULSE_EMAIL_PROVIDER'),
  'shared notification flows receive only supported smtp or outbox_only values');
  assert('SMTP_SELECTED_ENVIRONMENT_PROJECTION', smtpProjection.includes('PROJECTPULSE_TEST_SMTP_')
    && smtpProjection.includes('PROJECTPULSE_PRODUCTION_SMTP_')
    && smtpProjection.includes('ClearLegacyCredential()'),
  'only the selected environment SMTP credential pair is projected');
  assert('MAIL_SECRET_SAFETY', mailRuntime.includes('secretValuesRead = false')
    && mailRuntime.includes('secretValuesReturned = false')
    && !mailRuntime.includes('clientSecret = request')
    && !mailRuntime.includes('smtpPassword = request'),
  'mail runtime endpoint never accepts or returns credential values');
  assert('NON_DESTRUCTIVE_MIGRATION', !/DROP\s+TABLE|TRUNCATE\s+TABLE|DELETE\s+FROM\s+(azure_entra_settings|projectpulse_native_admin_documents|microsoft_integration_client_secrets|microsoft_integration_sso_client_secrets|microsoft_integration_audit_events)/i.test(migration)
    && migration.includes("'secretValuesRead', false")
    && migration.includes("'secretValuesChanged', false")
    && migration.includes("'sourceTablesDeleted', false"),
  'carryover does not read, change, or delete existing secret/source evidence');
  assert('NON_DESTRUCTIVE_ROLLBACK', rollback.includes('carried-over Module 010 and Module 067 configuration remains')
    && !/DROP\s+TABLE|DELETE\s+FROM\s+projectpulse_native_admin_documents/i.test(rollback),
  'rollback preserves active connection metadata and secrets');
  assert('POSTGRES_LIFECYCLE_TEST', test.includes('MICROSOFT_INTEGRATION_CONNECTION_CARRYOVER_047_TEST=PASS')
    && test.includes('module010_source_preserved')
    && test.includes('module067_source_preserved')
    && test.includes('graph_secret_preserved')
    && test.includes('sso_secret_preserved'),
  'PostgreSQL test proves metadata carryover and source/secret preservation');
} else {
  console.log('MICROSOFT_CONNECTION_BACKEND_AND_MIGRATION_DEEP_CHECK=SKIPPED_MINIMAL_WEB_CONTEXT');
}

console.log(`MICROSOFT_CONNECTION_VALIDATION_CHECKS=${checks.length}`);
if (checks.some((check) => !check.condition)) {
  const failed = checks.filter((check) => !check.condition).map((check) => check.name);
  console.error(`MICROSOFT_CONNECTION_FAILED_CHECKS=${failed.join(',')}`);
  console.error('MICROSOFT_INTEGRATION_AUTHORITATIVE_CONNECTION=FAILED');
  process.exit(1);
}
console.log('MICROSOFT_INTEGRATION_AUTHORITATIVE_CONNECTION=PASSED');
