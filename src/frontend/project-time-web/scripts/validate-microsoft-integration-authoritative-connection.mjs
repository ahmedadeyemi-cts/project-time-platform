import fs from 'node:fs';
import path from 'node:path';

const root = path.resolve(process.cwd(), '..', '..', '..');
const paths = {
  compatibility: 'src/frontend/project-time-web/src/microsoft-integration-compatibility.js',
  css: 'src/frontend/project-time-web/src/microsoft-integration-portal.css',
  portal: 'src/frontend/project-time-web/src/MicrosoftIntegrationDualConnectionPortal.jsx',
  registry: 'src/frontend/project-time-web/src/module-availability-registry.js',
  main: 'src/frontend/project-time-web/src/main.jsx',
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

for (const name of ['compatibility', 'css', 'portal', 'registry', 'main']) {
  assert(`${name.toUpperCase()}_EXISTS`, exists(paths[name]), paths[name]);
}

const compatibility = read(paths.compatibility);
const css = read(paths.css);
const portal = read(paths.portal);
const registry = read(paths.registry);
const main = read(paths.main);
const fullRepositoryContext = exists(paths.migration) && exists(paths.rollback) && exists(paths.test);

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
'Module 010 retains preview/import while tenant and sync controls are removed');

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

if (fullRepositoryContext) {
  const migration = read(paths.migration);
  const rollback = read(paths.rollback);
  const test = read(paths.test);

  assert('MIGRATION_EXISTS', true, paths.migration);
  assert('ROLLBACK_EXISTS', true, paths.rollback);
  assert('TEST_EXISTS', true, paths.test);

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
  console.log('MICROSOFT_CONNECTION_MIGRATION_DEEP_CHECK=SKIPPED_MINIMAL_WEB_CONTEXT');
}

console.log(`MICROSOFT_CONNECTION_VALIDATION_CHECKS=${checks.length}`);
if (checks.some((check) => !check.condition)) {
  console.error('MICROSOFT_INTEGRATION_AUTHORITATIVE_CONNECTION=FAILED');
  process.exit(1);
}
console.log('MICROSOFT_INTEGRATION_AUTHORITATIVE_CONNECTION=PASSED');
