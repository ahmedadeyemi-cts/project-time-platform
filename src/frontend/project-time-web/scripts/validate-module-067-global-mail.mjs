import fs from 'node:fs';
import path from 'node:path';

const frontendRoot = process.cwd();
const repositoryRoot = path.resolve(frontendRoot, '..', '..', '..');
const files = {
  registrar: 'src/backend/ProjectTime.Api/Modules/GlobalMailConfigurationModule.cs',
  securityBoundary: 'src/backend/ProjectTime.Api/Modules/MicrosoftIntegrationSecurityCompatibility.cs',
  integrationBackend: 'src/backend/ProjectTime.Api/Modules/MicrosoftIntegrationModule.cs',
  ssoBackend: 'src/backend/ProjectTime.Api/Modules/MicrosoftSsoConnectionProfilesModule.cs',
  importBackend: 'src/backend/ProjectTime.Api/Modules/AzureDirectoryImportModule.cs',
  portal: 'src/frontend/project-time-web/src/MicrosoftIntegrationDualConnectionPortal.jsx',
  compatibility: 'src/frontend/project-time-web/src/microsoft-integration-compatibility.js',
  stylesheet: 'src/frontend/project-time-web/src/microsoft-integration-portal.css',
  dualStylesheet: 'src/frontend/project-time-web/src/microsoft-integration-dual-connections.css',
  registry: 'src/frontend/project-time-web/src/module-availability-registry.js',
  main: 'src/frontend/project-time-web/src/main.jsx',
  identity: 'src/backend/ProjectTime.Api/Modules/IdentityProfileModule.cs',
  calendar: 'src/backend/ProjectTime.Api/Modules/CalendarCapacityModule.cs',
  migration045: 'database/migrations/045_microsoft_integration_consolidation.sql',
  rollback045: 'database/rollback/045_microsoft_integration_consolidation_rollback.sql',
  migration046: 'database/migrations/046_microsoft_sso_connection_profiles.sql',
  rollback046: 'database/rollback/046_microsoft_sso_connection_profiles_rollback.sql',
  package: 'src/frontend/project-time-web/package.json'
};

const absolute = (relative) => path.join(repositoryRoot, relative);
const exists = (relative) => fs.existsSync(absolute(relative));
const read = (relative) => fs.readFileSync(absolute(relative), 'utf8');
const checks = [];

function assert(name, condition, evidence) {
  checks.push({ name, condition, evidence });
  console.log(`MICROSOFT_INTEGRATION_${name}=${condition ? 'PASSED' : 'FAILED'} — ${evidence}`);
}

for (const name of ['registrar', 'portal', 'compatibility', 'stylesheet', 'dualStylesheet', 'registry', 'main', 'identity', 'calendar', 'package']) {
  assert(`${name.toUpperCase()}_EXISTS`, exists(files[name]), files[name]);
}

const registrar = read(files.registrar);
const portal = read(files.portal);
const compatibility = read(files.compatibility);
const stylesheet = read(files.stylesheet);
const dualStylesheet = read(files.dualStylesheet);
const registry = read(files.registry);
const main = read(files.main);
const identity = read(files.identity);
const calendar = read(files.calendar);
const packageJson = JSON.parse(read(files.package));
const hasSecurityBoundary = exists(files.securityBoundary);
const securityBoundary = hasSecurityBoundary ? read(files.securityBoundary) : '';
const hasBackendImplementation = exists(files.integrationBackend) && exists(files.importBackend) && exists(files.ssoBackend);
const integrationBackend = hasBackendImplementation ? read(files.integrationBackend) : '';
const ssoBackend = hasBackendImplementation ? read(files.ssoBackend) : '';
const importBackend = hasBackendImplementation ? read(files.importBackend) : '';
const backend = `${registrar}\n${securityBoundary}\n${integrationBackend}\n${ssoBackend}\n${importBackend}`;

assert('REGISTRATION_PRESERVED', registrar.includes('MapGlobalMailConfigurationEndpoints')
  && registrar.includes('UseMicrosoftIntegrationSecurityCompatibility')
  && registrar.includes('MicrosoftIntegrationModule.MapEndpoints(app)')
  && registrar.includes('MapMicrosoftSsoConnectionProfileEndpoints')
  && registrar.includes('AzureDirectoryImportModule.MapEndpoints(app)'),
'existing Program.cs registration point maps Graph, separate SSO, and Module 010 import endpoints');

if (hasSecurityBoundary) {
  assert('SECURITY_BOUNDARY_EXISTS', true, files.securityBoundary);
  assert('GOVERNED_IMPORT_ROLE', securityBoundary.includes('client_selected_import_role_not_allowed')
    && securityBoundary.includes('governed_import_role_not_allowed')
    && securityBoundary.includes('AllowedGovernedImportRoles')
    && securityBoundary.includes('payload["defaultRoleCode"] = governedRole'),
  'client roles cannot elevate privileges and server-governed roles use a non-administrative allowlist');
  assert('READ_WRITE_AUTHORIZATION_SPLIT', securityBoundary.includes('WritePermissions')
    && securityBoundary.includes('VIEW_GLOBAL_MAIL_CONFIGURATION') === false
    && securityBoundary.includes('microsoft_integration_manage_access_required'),
  'view-only legacy mail grants cannot write Graph secrets or run privileged tests');
  assert('ALL_TENANT_GRAPH_SECRET_HYDRATION', securityBoundary.includes('SELECT tenant_key, ciphertext, nonce, authentication_tag')
    && securityBoundary.includes('PROJECTPULSE_MICROSOFT_TENANT_')
    && securityBoundary.includes('HydrateEveryConfiguredTenantSecretAsync'),
  'every existing services/Graph tenant secret remains hydrated');
} else {
  console.log('MICROSOFT_INTEGRATION_SECURITY_DEEP_CHECK=SKIPPED_MINIMAL_WEB_CONTEXT');
}

if (hasBackendImplementation) {
  assert('MODULE_065_OWNER', integrationBackend.includes('ModuleNumber = "065"') && integrationBackend.includes('moduleName = "Microsoft Integration"'), 'Module 065 is the active owner');
  assert('MODULE_067_COMPATIBILITY', integrationBackend.includes('/api/global-mail/configuration') && integrationBackend.includes('/api/global-mail/health') && integrationBackend.includes('retired = true'), 'legacy GET compatibility remains');
  assert('UNIQUE_IMPORT_ENDPOINT', importBackend.includes('/api/microsoft-integration/directory-users/import-selected') && compatibility.includes('/api/admin/azure/users/import-selected'), 'legacy browser call rewrites to repaired endpoint');
  assert('SELECTED_IDENTIFIERS', ['selectedUsers', 'selectedEmails', 'selectedUserIds', 'selectedEntraObjectIds'].every((value) => importBackend.includes(value)), 'preview/import identifiers remain compatible');
  assert('SESSION_AUTHORITY', backend.includes('ProjectPulseActualUserId') && backend.includes('ProjectPulseSessionUserId') && backend.includes('view_as_read_only'), 'actual session and View-As write protection');
  assert('APP_USERS_PERSISTENCE', importBackend.includes('app_users') && importBackend.includes('InsertUserAsync') && importBackend.includes('UpdateUserAsync'), 'insert or safe upsert into app_users');
  assert('DUPLICATE_REPORTING', importBackend.includes('existing_user_upserted') && importBackend.includes('duplicate = outcomes.Count'), 'duplicates are reported');
  assert('ROLE_ASSIGNMENT_EXPLICIT', importBackend.includes('defaultRoleCode') && importBackend.includes('EnsureRoleAssignmentAsync'), 'role assignment behavior is explicit');
  assert('TRANSACTION_COMMIT', importBackend.includes('SAVEPOINT {savepoint}') && importBackend.includes('transaction.CommitAsync'), 'per-user savepoints and final commit');
  assert('RESULT_COUNTS', ['imported,', 'skipped,', 'duplicate,', 'failed,'].every((value) => importBackend.includes(value)), 'response reports result categories');

  assert('GRAPH_SECRET_STORE_PRESERVED', integrationBackend.includes('microsoft_integration_client_secrets')
    && integrationBackend.includes('PROJECTPULSE_ENTRA_TEST_CLIENT_SECRET')
    && integrationBackend.includes('PROJECTPULSE_ENTRA_PRODUCTION_CLIENT_SECRET'),
  'migration 045 Graph/services secrets and environment contracts remain unchanged');
  assert('SEPARATE_SSO_ENDPOINTS', ssoBackend.includes('/api/microsoft-integration/sso-readiness')
    && ssoBackend.includes('/api/microsoft-integration/sso-client-secret')
    && ssoBackend.includes('/api/microsoft-integration/sso-test'),
  'SSO metadata, secret, and readiness have separate endpoints');
  assert('SEPARATE_SSO_SECRET_STORE', ssoBackend.includes('microsoft_integration_sso_client_secrets')
    && ssoBackend.includes('ProjectPulse:{ModuleNumber}:SSO:')
    && ssoBackend.includes('CryptographicOperations.ZeroMemory'),
  'SSO secrets are encrypted with a separate associated-data boundary');
  assert('TEST_PRODUCTION_SSO_HYDRATION', ['PROJECTPULSE_ENTRA_TEST_SSO_CLIENT_SECRET', 'PROJECTPULSE_ENTRA_PRODUCTION_SSO_CLIENT_SECRET', 'PROJECTPULSE_SSO_CLIENT_SECRET'].every((value) => ssoBackend.includes(value)), 'Test and Production SSO secrets hydrate independently');
  assert('SSO_METADATA_HYDRATION', ['PROJECTPULSE_ENTRA_TEST_SSO_', 'PROJECTPULSE_ENTRA_PRODUCTION_SSO_', 'PROJECTPULSE_SSO_TENANT_ID', 'PROJECTPULSE_SSO_CLIENT_ID'].every((value) => ssoBackend.includes(value)), 'SSO metadata is additive and environment-specific');
  assert('SSO_INTERACTIVE_UAT_REQUIRED', ssoBackend.includes('interactiveSignInRequired = true') && ssoBackend.includes('OpenID metadata'), 'SSO readiness does not falsely claim an interactive sign-in passed');
  assert('GRAPH_CONNECTION_TEST', integrationBackend.includes('/api/microsoft-integration/test-connection') && integrationBackend.includes('graph.microsoft.com/v1.0/users'), 'Graph application connection test remains intact');
} else {
  console.log('MICROSOFT_INTEGRATION_BACKEND_DEEP_CHECK=SKIPPED_MINIMAL_WEB_CONTEXT');
}

assert('IDENTITY_SOURCE_PRESERVED', identity.includes('GraphCredentials.ForDomain(domain)')
  && identity.includes('PROJECTPULSE_ENTRA_TEST_CLIENT_ID')
  && identity.includes('PROJECTPULSE_ENTRA_PRODUCTION_CLIENT_ID')
  && identity.includes('PROJECTPULSE_ENTRA_TEST_CLIENT_SECRET')
  && identity.includes('PROJECTPULSE_ENTRA_PRODUCTION_CLIENT_SECRET'),
'Module 062 continues using explicit Test/Production Graph services credentials');
assert('CALENDAR_GRAPH_CONTRACT_PRESERVED', calendar.includes('PROJECTPULSE_ENTRA_CLIENT_ID')
  && calendar.includes('PROJECTPULSE_ENTRA_CLIENT_SECRET')
  && calendar.includes('graph.microsoft.com'),
'Module 057 calendar remains on the existing services/Graph environment contract');

assert('FOUR_CONNECTION_MODEL', portal.includes("const ENVIRONMENTS = ['test', 'production']")
  && portal.includes('sso_app_registration')
  && portal.includes('microsoft_services_enterprise_application')
  && portal.includes('Two independent connections'),
'Test and Production each expose SSO and Microsoft services connections');
assert('LEGACY_GRAPH_CARRYOVER', portal.includes('raw?.serviceClientId || raw?.clientId')
  && portal.includes('The original Module 065 clientId is intentionally carried forward as the services/Graph application'),
'legacy clientId is carried into the services profile, never silently moved to SSO');
assert('MODULE_010_USES_SERVICES', portal.includes('Module 010 continues to use the Microsoft services/Graph application, never the SSO App Registration')
  && portal.includes('clientId: activeTenant.services.clientId'),
'Module 010 preview/import remains wired to the services application');
assert('SEPARATE_SECRET_FORMS', portal.includes("saveSecret('sso')")
  && portal.includes("saveSecret('services')")
  && portal.includes('/api/microsoft-integration/sso-client-secret')
  && portal.includes('/api/microsoft-integration/client-secret'),
'SSO and services secrets cannot overwrite one another');
assert('TEST_PRODUCTION_DOMAINS', portal.includes('onenecklab.com,onitdemo.com') && portal.includes('ussignal.com'), 'current Test and Production domain defaults are present');
assert('DIRECTORY_SYNC_MOVED', portal.includes('directorySyncEnabled') && portal.includes('/api/admin/azure/config') && portal.includes('/api/admin/azure/import-settings'), 'sync configuration remains in Module 065 while APIs stay compatible');
assert('MAIL_CONSOLIDATED', portal.includes('Microsoft 365 / SMTP') && portal.includes('Sender mailbox') && portal.includes('smtp.office365.com'), 'Module 067 mail capabilities remain consolidated');
assert('MODULE_010_IMPORT_ONLY', compatibility.includes('.azure-config-card, .azure-sync-summary-card') && compatibility.includes('Preview and import Entra users'), 'tenant and sync cards remain removed from Module 010');
assert('OBSERVER_RECURSION_GUARD', compatibility.includes('function setTextIfChanged') && compatibility.includes('element.textContent === value'), 'Module 010 DOM normalization does not self-trigger');
assert('ACTIVE_REGISTRY_TITLES', registry.includes("moduleNumber: '010', route: 'azure-admin', displayName: 'Azure / Entra Directory Users'") && registry.includes("moduleNumber: '065', route: 'entra-secret-administration', displayName: 'Microsoft Integration'"), 'active module names remain authoritative');
assert('MODULE_067_RETIRED_FROM_REGISTRY', !registry.includes("moduleNumber: '067'") && registry.includes("'global-mail-configuration': 'entra-secret-administration'"), 'Module 067 remains retired with route compatibility');
assert('PORTAL_MOUNT', main.includes("import MicrosoftIntegrationDualConnectionPortal from './MicrosoftIntegrationDualConnectionPortal.jsx';") && main.includes('<MicrosoftIntegrationDualConnectionPortal />') && !main.includes('<MicrosoftIntegrationPortal />'), 'dual portal is mounted once');
assert('SCOPED_STYLES', stylesheet.includes('projectpulse-microsoft-integration-active') && dualStylesheet.includes('.microsoft-environment-switcher') && dualStylesheet.includes('.microsoft-connection-card'), 'styles remain scoped to Module 065');
assert('BUILD_GUARD', packageJson.scripts?.build?.includes('validate:module067') && packageJson.scripts?.['validate:module067']?.includes('validate-module-067-global-mail.mjs'), 'full frontend build runs Microsoft Integration validator');

if (exists(files.migration045)) {
  const migration045 = read(files.migration045);
  const rollback045 = read(files.rollback045);
  assert('MIGRATION_045_PRESERVED', migration045.includes('045_microsoft_integration_consolidation') && migration045.includes('microsoft_integration_client_secrets'), 'Graph/services migration 045 remains unchanged');
  assert('MIGRATION_045_GUARDED_ROLLBACK', rollback045.includes('Rollback blocked') && rollback045.includes('immutable Microsoft Integration audit evidence'), 'migration 045 rollback remains guarded');
}

if (exists(files.migration046)) {
  const migration046 = read(files.migration046);
  const rollback046 = read(files.rollback046);
  assert('MIGRATION_046', migration046.includes('046_microsoft_sso_connection_profiles') && migration046.includes('microsoft_integration_sso_client_secrets'), 'additive SSO-only migration 046');
  assert('MIGRATION_046_ENVIRONMENTS', migration046.includes("environment_mode IN ('test', 'production')"), 'SSO storage is explicitly separated by Test and Production');
  assert('MIGRATION_046_GRAPH_UNCHANGED', migration046.includes('Microsoft services/Graph secrets remain') && !migration046.includes('DROP TABLE'), 'migration 046 does not rewrite Graph/services storage');
  assert('MIGRATION_046_GUARDED_ROLLBACK', rollback046.includes('Rollback blocked: Microsoft SSO App Registration secret metadata exists.'), 'SSO rollback blocks after a secret is saved');
} else {
  assert('MIGRATION_046_EXISTS', false, files.migration046);
}

console.log('');
console.log(`MICROSOFT_INTEGRATION_VALIDATION_CHECKS=${checks.length}`);
if (checks.some((check) => !check.condition)) {
  console.error('MICROSOFT_INTEGRATION_CONTRACT=FAILED');
  process.exit(1);
}
console.log('MICROSOFT_INTEGRATION_CONTRACT=PASSED');
