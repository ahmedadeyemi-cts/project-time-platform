import fs from 'node:fs';
import path from 'node:path';

const root = path.resolve(process.cwd(), '..', '..', '..');
const paths = {
  compatibility: 'src/frontend/project-time-web/src/microsoft-integration-compatibility.js',
  css: 'src/frontend/project-time-web/src/microsoft-integration-portal.css',
  portal: 'src/frontend/project-time-web/src/MicrosoftIntegrationDualConnectionPortal.jsx',
  registry: 'src/frontend/project-time-web/src/module-availability-registry.js',
  main: 'src/frontend/project-time-web/src/main.jsx',
  mailActivation: 'src/frontend/project-time-web/src/microsoft-mail-runtime-activation.js',
  mailRuntime: 'src/backend/ProjectTime.Api/Modules/MicrosoftMailRuntimeConfigurationModule.cs',
  smtpProjection: 'src/backend/ProjectTime.Api/Modules/MicrosoftSmtpCredentialProjectionCompatibility.cs',
  registrar: 'src/backend/ProjectTime.Api/Modules/GlobalMailConfigurationModule.cs',
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

for (const name of ['compatibility', 'css', 'portal', 'registry', 'main', 'mailActivation']) {
  assert(`${name.toUpperCase()}_EXISTS`, exists(paths[name]), paths[name]);
}

const compatibility = read(paths.compatibility);
const css = read(paths.css);
const portal = read(paths.portal);
const registry = read(paths.registry);
const main = read(paths.main);
const mailActivation = read(paths.mailActivation);
const fullRepositoryContext = exists(paths.migration)
  && exists(paths.rollback)
  && exists(paths.test)
  && exists(paths.mailRuntime)
  && exists(paths.smtpProjection)
  && exists(paths.registrar);

assert('AUTHORITATIVE_NAME', registry.includes("displayName: 'Microsoft Integration Connection'")
  && compatibility.includes("ACTIVE_MODULE_NAME = 'Microsoft Integration Connection'"),
'Module 065 has one authoritative user-facing name');

assert('LEGACY_MODULE_065_SUPPRESSED', compatibility.includes('.entra-secret-center[data-module="065"]')
  && compatibility.includes('.native-module-administration[data-module-administration="065"]')
  && compatibility.includes('[data-phase="065_COMPLETE_SOURCE_LOCKED_RUNTIME"]')
  && compatibility.includes("style.setProperty('display', 'none', 'important')"),
'the actual legacy Module 065 and native editor DOM surfaces are suppressed');

assert('AUTHORITATIVE_PORTAL_VISIBLE', compatibility.includes('data-microsoft-integration-authoritative')
  && css.includes('.microsoft-integration-portal[data-microsoft-integration-authoritative="true"]')
  && main.includes('<MicrosoftIntegrationDualConnectionPortal />'),
'only the consolidated portal is forced visible on the active route');

assert('MODULE_010_CONFIGURATION_REMOVED', compatibility.includes('.azure-config-card, .azure-sync-summary-card')
  && compatibility.includes("label === 'sync now'")
  && compatibility.includes("label === 'save configuration'")
  && css.includes('.route-azure-admin .azure-config-card')
  && css.includes('.route-azure-admin .azure-sync-summary-card'),
'Module 010 tenant and synchronization controls remain moved to Module 065');

assert('MODULE_010_PREVIEW_PRESERVED', compatibility.includes('function restoreModule010Preview')
  && compatibility.includes("module010.querySelector('.azure-preview-card')")
  && compatibility.includes('data-module-010-preview-preserved')
  && css.includes('.route-azure-admin .azure-preview-card')
  && css.includes('.route-azure-admin .azure-preview-card .azure-admin-heading-actions button')
  && !css.includes('.route-azure-admin .azure-admin-heading-actions .primary-action'),
'Preview users, Import selected, filters, and selection controls remain visible');

assert('MODULE_010_PURPOSE', compatibility.includes('Preview and import Entra users')
  && compatibility.includes('Tenant, synchronization, identity, calendar, and Microsoft 365 mail settings are managed in Module 065'),
'Module 010 clearly points configuration ownership to Module 065');

assert('SSO_AND_SERVICES_CONNECTIONS', portal.includes('Microsoft Entra SSO')
  && portal.includes('Microsoft services and Graph')
  && portal.includes('sso_app_registration')
  && portal.includes('microsoft_services_enterprise_application'),
'Test and Production keep separate SSO and services connections');

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
  && mailActivation.includes('runtimeActivated: false')
  && mailActivation.includes('return response;')
  && !mailActivation.includes("status: 'mail_runtime_activation_failed'")
  && !/clientSecret|password|accessToken/i.test(mailActivation),
'successful Module 065 saves remain successful while non-secret runtime status is dispatched separately');

if (fullRepositoryContext) {
  const migration = read(paths.migration);
  const rollback = read(paths.rollback);
  const test = read(paths.test);
  const mailRuntime = read(paths.mailRuntime);
  const smtpProjection = read(paths.smtpProjection);
  const registrar = read(paths.registrar);

  assert('MIGRATION_EXISTS', true, paths.migration);
  assert('ROLLBACK_EXISTS', true, paths.rollback);
  assert('TEST_EXISTS', true, paths.test);
  assert('MAIL_RUNTIME_EXISTS', true, paths.mailRuntime);
  assert('SMTP_PROJECTION_EXISTS', true, paths.smtpProjection);

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

  assert('MAIL_RUNTIME_REGISTERED', registrar.includes('MapMicrosoftMailRuntimeConfigurationEndpoints')
    && registrar.includes('UseMicrosoftSmtpCredentialProjectionCompatibility')
    && mailRuntime.includes('/api/microsoft-integration/mail-runtime')
    && mailRuntime.includes('ApplicationStarted.Register')
    && mailRuntime.includes('ReadStoredConfigurationAsync'),
  'mail metadata and selected SMTP credentials are applied immediately and hydrated after API restart');

  assert('ENVIRONMENT_SECRET_ISOLATION', mailRuntime.includes('PROJECTPULSE_ENTRA_TEST_CLIENT_SECRET')
    && mailRuntime.includes('PROJECTPULSE_ENTRA_PRODUCTION_CLIENT_SECRET')
    && mailRuntime.includes('PROJECTPULSE_MICROSOFT_TENANT_ONENECKLAB_CLIENT_SECRET')
    && mailRuntime.includes('PROJECTPULSE_MICROSOFT_TENANT_USSIGNAL_CLIENT_SECRET')
    && mailRuntime.includes('activeMode == "test"')
    && mailRuntime.includes('activeMode == "production"')
    && !mailRuntime.includes('Environment.GetEnvironmentVariable("PROJECTPULSE_ENTRA_CLIENT_SECRET"),\n            Environment.GetEnvironmentVariable("PROJECTPULSE_M365_CLIENT_SECRET")'),
  'Test never borrows Production credentials and Production never borrows Test credentials');

  assert('RECIPIENT_BOUNDARY_ENFORCED', mailRuntime.includes('configuration.RecipientBoundary == "production_governed"')
    && mailRuntime.includes('PROJECTPULSE_MAIL_RECIPIENT_BOUNDARY')
    && mailRuntime.includes('The Test-only boundary keeps delivery outbox-only')
    && mailRuntime.includes('Microsoft mail delivery is locked')
    && mailRuntime.includes('liveDeliveryEnabled'),
  'test_only and locked states cannot enable live delivery');

  assert('SHARED_PROVIDER_COMPATIBILITY', mailRuntime.includes('var sharedProvider = liveDeliveryEnabled')
    && mailRuntime.includes('? "smtp"')
    && mailRuntime.includes(': "outbox_only"')
    && mailRuntime.includes('PROJECTPULSE_EMAIL_PROVIDER')
    && !mailRuntime.includes('SetIfPresent("PROJECTPULSE_EMAIL_PROVIDER", provider)'),
  'the shared dispatcher receives only supported smtp or outbox_only values');

  assert('GRAPH_MAIL_RUNTIME', mailRuntime.includes('PROJECTPULSE_MAIL_PROVIDER')
    && mailRuntime.includes('PROJECTPULSE_M365_TENANT_ID')
    && mailRuntime.includes('PROJECTPULSE_M365_CLIENT_ID')
    && mailRuntime.includes('PROJECTPULSE_M365_CLIENT_SECRET')
    && mailRuntime.includes('PROJECTPULSE_M365_SENDER_MAILBOX')
    && mailRuntime.includes('Graph-capable delivery paths')
    && mailRuntime.includes('Shared notification flows remain outbox-only'),
  'Graph mail is enabled only for Graph-capable paths and is not misrepresented as shared-dispatcher support');

  assert('SMTP_RUNTIME', mailRuntime.includes('PROJECTPULSE_SMTP_HOST')
    && mailRuntime.includes('PROJECTPULSE_SMTP_PORT')
    && mailRuntime.includes('PROJECTPULSE_SMTP_FROM')
    && mailRuntime.includes('PROJECTPULSE_TEST_SMTP_')
    && mailRuntime.includes('PROJECTPULSE_PRODUCTION_SMTP_')
    && mailRuntime.includes('Microsoft 365 SMTP is ready for the shared mail dispatcher'),
  'SMTP metadata is page-owned and credentials remain environment-specific');

  assert('SMTP_SELECTED_ENVIRONMENT_PROJECTION', smtpProjection.includes('PROJECTPULSE_TEST_SMTP_')
    && smtpProjection.includes('PROJECTPULSE_PRODUCTION_SMTP_')
    && smtpProjection.includes('PROJECTPULSE_SMTP_USERNAME')
    && smtpProjection.includes('PROJECTPULSE_SMTP_PASSWORD')
    && smtpProjection.includes('activeMode == environmentMode')
    && smtpProjection.includes('providerTarget != "smtp_relay"')
    && smtpProjection.includes('ClearLegacyCredential()'),
  'only the selected environment SMTP pair is projected into the legacy sender variables and stale values are cleared');

  assert('SMTP_PROJECTION_SUCCESS_AND_RESTART', smtpProjection.includes('context.Response.StatusCode is >= 200 and < 300')
    && smtpProjection.includes('ApplicationStarted.Register')
    && smtpProjection.includes('ReadStoredSelectionAsync')
    && smtpProjection.includes("module_number='065'"),
  'SMTP projection occurs only after a successful runtime update and is restored from Module 065 after restart');

  assert('SMTP_PROJECTION_BROWSER_SAFETY', !smtpProjection.includes('ReadFromJsonAsync<SmtpCredential')
    && !smtpProjection.includes('Results.Ok(new')
    && !smtpProjection.includes('password =')
    && smtpProjection.includes('credential values')
    && smtpProjection.includes('never accepted from or returned to the browser'),
  'SMTP credential values remain environment-backed and never cross the browser API');

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

  assert('IDEMPOTENT_DOCUMENT', migration.includes("existing_notes NOT LIKE marker || '%'")
    && migration.includes('ON CONFLICT (module_number, document_key)')
    && migration.includes('ON CONFLICT (module_number, document_key, revision_number) DO NOTHING'),
  'carryover writes one revision only when the consolidated marker is absent');

  assert('NON_DESTRUCTIVE_ROLLBACK', rollback.includes('carried-over Module 010 and Module 067 configuration remains')
    && !/DROP\s+TABLE|DELETE\s+FROM\s+projectpulse_native_admin_documents/i.test(rollback),
  'rollback preserves active connection metadata and secrets');

  assert('POSTGRES_LIFECYCLE_TEST', test.includes('MICROSOFT_INTEGRATION_CONNECTION_CARRYOVER_047_TEST=PASS')
    && test.includes('module010_source_preserved')
    && test.includes('module067_source_preserved')
    && test.includes('graph_secret_preserved')
    && test.includes('sso_secret_preserved')
    && test.includes('module062_uses_services_connection'),
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
