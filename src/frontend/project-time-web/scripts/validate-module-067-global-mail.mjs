import fs from 'node:fs';
import path from 'node:path';

const frontendRoot = process.cwd();
const repositoryRoot = path.resolve(frontendRoot, '..', '..', '..');
const files = {
  registrar: 'src/backend/ProjectTime.Api/Modules/GlobalMailConfigurationModule.cs',
  securityBoundary: 'src/backend/ProjectTime.Api/Modules/MicrosoftIntegrationSecurityCompatibility.cs',
  integrationBackend: 'src/backend/ProjectTime.Api/Modules/MicrosoftIntegrationModule.cs',
  ssoBackend: 'src/backend/ProjectTime.Api/Modules/MicrosoftSsoConnectionProfilesModule.cs',
  ssoInteractive: 'src/backend/ProjectTime.Api/Modules/MicrosoftSsoInteractiveStartActivation.cs',
  environmentResolver: 'src/backend/ProjectTime.Api/Modules/MicrosoftEnvironmentRuntimeResolver.cs',
  mailRuntime: 'src/backend/ProjectTime.Api/Modules/MicrosoftMailRuntimeConfigurationModule.cs',
  mailTest: 'src/backend/ProjectTime.Api/Modules/MicrosoftMailTransportTestModule.cs',
  importBackend: 'src/backend/ProjectTime.Api/Modules/AzureDirectoryImportModule.cs',
  portal: 'src/frontend/project-time-web/src/MicrosoftIntegrationDualConnectionPortal.jsx',
  mailReadiness: 'src/frontend/project-time-web/src/MicrosoftMailTransportReadinessPanel.jsx',
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

for (const name of ['registrar', 'portal', 'mailReadiness', 'compatibility', 'stylesheet', 'dualStylesheet', 'registry', 'main', 'identity', 'package']) {
  assert(`${name.toUpperCase()}_EXISTS`, exists(files[name]), files[name]);
}

const registrar = read(files.registrar);
const portal = read(files.portal);
const mailReadiness = read(files.mailReadiness);
const compatibility = read(files.compatibility);
const stylesheet = read(files.stylesheet);
const dualStylesheet = read(files.dualStylesheet);
const registry = read(files.registry);
const main = read(files.main);
const identity = read(files.identity);
const packageJson = JSON.parse(read(files.package));
const hasCalendar = exists(files.calendar);
const calendar = hasCalendar ? read(files.calendar) : '';
const hasSecurityBoundary = exists(files.securityBoundary);
const securityBoundary = hasSecurityBoundary ? read(files.securityBoundary) : '';
const backendFiles = ['integrationBackend', 'ssoBackend', 'importBackend'];
const hasBackendImplementation = backendFiles.every((name) => exists(files[name]));
const integrationBackend = hasBackendImplementation ? read(files.integrationBackend) : '';
const ssoBackend = hasBackendImplementation ? read(files.ssoBackend) : '';
const importBackend = hasBackendImplementation ? read(files.importBackend) : '';
const backend = `${registrar}\n${securityBoundary}\n${integrationBackend}\n${ssoBackend}\n${importBackend}`;
const fullRuntimeContext = ['environmentResolver', 'ssoInteractive', 'mailRuntime', 'mailTest']
  .every((name) => exists(files[name]));
const environmentResolver = fullRuntimeContext ? read(files.environmentResolver) : '';
const ssoInteractive = fullRuntimeContext ? read(files.ssoInteractive) : '';
const mailRuntime = fullRuntimeContext ? read(files.mailRuntime) : '';
const mailTest = fullRuntimeContext ? read(files.mailTest) : '';
const fullRepositoryContext = hasBackendImplementation
  && hasCalendar
  && fullRuntimeContext
  && exists(files.migration046)
  && exists(files.rollback046);

assert('REGISTRATION_PRESERVED', registrar.includes('MapGlobalMailConfigurationEndpoints')
  && registrar.includes('UseMicrosoftIntegrationSecurityCompatibility')
  && registrar.includes('MicrosoftIntegrationModule.MapEndpoints(app)')
  && registrar.includes('MapMicrosoftSsoConnectionProfileEndpoints')
  && registrar.includes('AzureDirectoryImportModule.MapEndpoints(app)'),
'existing startup registration maps Graph, separate SSO, Module 010 import, and mail endpoints');

assert('TRUSTED_ENVIRONMENT_ORDER', registrar.includes('UseProjectPulsePublicOriginCompatibility')
  && registrar.includes('UseMicrosoftEnvironmentRuntimeCompatibility')
  && registrar.includes('UseMicrosoftSsoInteractiveStartActivation')
  && registrar.indexOf('UseProjectPulsePublicOriginCompatibility') < registrar.indexOf('UseMicrosoftEnvironmentRuntimeCompatibility')
  && registrar.indexOf('UseMicrosoftEnvironmentRuntimeCompatibility') < registrar.indexOf('UseMicrosoftSsoInteractiveStartActivation'),
'trusted public origin and Test/Production environment resolve before interactive SSO');

if (hasSecurityBoundary) {
  assert('GOVERNED_IMPORT_ROLE', securityBoundary.includes('client_selected_import_role_not_allowed')
    && securityBoundary.includes('governed_import_role_not_allowed')
    && securityBoundary.includes('AllowedGovernedImportRoles')
    && securityBoundary.includes('payload["defaultRoleCode"] = governedRole'),
  'client roles cannot elevate privileges and server-governed roles use a non-administrative allowlist');
  assert('READ_WRITE_AUTHORIZATION_SPLIT', securityBoundary.includes('WritePermissions')
    && !securityBoundary.includes('VIEW_GLOBAL_MAIL_CONFIGURATION')
    && securityBoundary.includes('microsoft_integration_manage_access_required'),
  'view-only legacy mail grants cannot write secrets or run privileged tests');
  assert('ALL_TENANT_GRAPH_SECRET_HYDRATION', securityBoundary.includes('SELECT tenant_key, ciphertext, nonce, authentication_tag')
    && securityBoundary.includes('PROJECTPULSE_MICROSOFT_TENANT_')
    && securityBoundary.includes('HydrateEveryConfiguredTenantSecretAsync'),
  'every configured services/Graph tenant secret remains hydrated');
} else {
  console.log('MICROSOFT_INTEGRATION_SECURITY_DEEP_CHECK=SKIPPED_MINIMAL_WEB_CONTEXT');
}

if (hasBackendImplementation) {
  assert('MODULE_065_OWNER', integrationBackend.includes('ModuleNumber = "065"')
    && integrationBackend.includes('moduleName = "Microsoft Integration"'),
  'Module 065 remains the active Microsoft owner');
  assert('MODULE_067_COMPATIBILITY', integrationBackend.includes('/api/global-mail/configuration')
    && integrationBackend.includes('/api/global-mail/health')
    && integrationBackend.includes('retired = true'),
  'legacy Module 067 GET compatibility remains');
  assert('UNIQUE_IMPORT_ENDPOINT', importBackend.includes('/api/microsoft-integration/directory-users/import-selected')
    && compatibility.includes('/api/admin/azure/users/import-selected'),
  'legacy browser import rewrites to the repaired Module 010 endpoint');
  assert('SELECTED_IDENTIFIERS', ['selectedUsers', 'selectedEmails', 'selectedUserIds', 'selectedEntraObjectIds']
    .every((value) => importBackend.includes(value)),
  'preview/import identifiers remain compatible');
  assert('SESSION_AUTHORITY', backend.includes('ProjectPulseActualUserId')
    && backend.includes('ProjectPulseSessionUserId')
    && backend.includes('view_as_read_only'),
  'actual session and View-As write protection remain');
  assert('APP_USERS_PERSISTENCE', importBackend.includes('app_users')
    && importBackend.includes('InsertUserAsync')
    && importBackend.includes('UpdateUserAsync'),
  'selected imports insert or safely update app_users');
  assert('DUPLICATE_REPORTING', importBackend.includes('existing_user_upserted')
    && importBackend.includes('duplicate = outcomes.Count'),
  'duplicates remain explicitly reported');
  assert('ROLE_ASSIGNMENT_EXPLICIT', importBackend.includes('defaultRoleCode')
    && importBackend.includes('EnsureRoleAssignmentAsync'),
  'role assignment remains explicit and governed');
  assert('TRANSACTION_COMMIT', importBackend.includes('SAVEPOINT {savepoint}')
    && importBackend.includes('transaction.CommitAsync'),
  'per-user savepoints and final transaction commit remain');
  assert('GRAPH_SECRET_STORE_PRESERVED', integrationBackend.includes('microsoft_integration_client_secrets')
    && integrationBackend.includes('PROJECTPULSE_ENTRA_TEST_CLIENT_SECRET')
    && integrationBackend.includes('PROJECTPULSE_ENTRA_PRODUCTION_CLIENT_SECRET'),
  'Graph/services secrets remain environment-specific');
  assert('SEPARATE_SSO_ENDPOINTS', ssoBackend.includes('/api/microsoft-integration/sso-readiness')
    && ssoBackend.includes('/api/microsoft-integration/sso-client-secret')
    && ssoBackend.includes('/api/microsoft-integration/sso-test'),
  'SSO metadata, secret, and readiness remain separate endpoints');
  assert('SEPARATE_SSO_SECRET_STORE', ssoBackend.includes('microsoft_integration_sso_client_secrets')
    && ssoBackend.includes('ProjectPulse:{ModuleNumber}:SSO:')
    && ssoBackend.includes('CryptographicOperations.ZeroMemory'),
  'SSO secrets remain encrypted with a separate associated-data boundary');
  assert('TEST_PRODUCTION_SSO_HYDRATION', [
    'PROJECTPULSE_ENTRA_TEST_SSO_CLIENT_SECRET',
    'PROJECTPULSE_ENTRA_PRODUCTION_SSO_CLIENT_SECRET',
    'PROJECTPULSE_SSO_CLIENT_SECRET'
  ].every((value) => ssoBackend.includes(value)),
  'Test and Production SSO secrets hydrate independently');
  assert('GRAPH_CONNECTION_TEST', integrationBackend.includes('/api/microsoft-integration/test-connection')
    && integrationBackend.includes('graph.microsoft.com/v1.0/users'),
  'Graph application connection test remains intact');
} else {
  console.log('MICROSOFT_INTEGRATION_BACKEND_DEEP_CHECK=SKIPPED_MINIMAL_WEB_CONTEXT');
}

assert('IDENTITY_SOURCE_PRESERVED', identity.includes('GraphCredentials.ForDomain(domain)')
  && identity.includes('PROJECTPULSE_ENTRA_TEST_CLIENT_ID')
  && identity.includes('PROJECTPULSE_ENTRA_PRODUCTION_CLIENT_ID')
  && identity.includes('PROJECTPULSE_ENTRA_TEST_CLIENT_SECRET')
  && identity.includes('PROJECTPULSE_ENTRA_PRODUCTION_CLIENT_SECRET'),
'Module 062 continues using explicit Test/Production Graph services credentials');

if (hasCalendar) {
  assert('CALENDAR_GRAPH_CONTRACT_PRESERVED', calendar.includes('PROJECTPULSE_ENTRA_CLIENT_ID')
    && calendar.includes('PROJECTPULSE_ENTRA_CLIENT_SECRET')
    && calendar.includes('graph.microsoft.com'),
  'Module 057 calendar remains on the active services/Graph environment contract');
} else {
  console.log('MICROSOFT_INTEGRATION_CALENDAR_DEEP_CHECK=SKIPPED_MINIMAL_WEB_CONTEXT');
}

assert('FOUR_CONNECTION_MODEL', portal.includes("const ENVIRONMENTS = ['test', 'production']")
  && portal.includes('sso_app_registration')
  && portal.includes('microsoft_services_enterprise_application')
  && portal.includes('Two independent connections'),
'Test and Production each expose SSO and Microsoft services connections');
assert('LEGACY_GRAPH_CARRYOVER', (
  portal.includes('source.serviceClientId || source.clientId')
  || portal.includes('raw?.serviceClientId || raw?.clientId')
) && portal.includes('services.clientId || services.applicationId'),
'legacy clientId remains carried into the services profile, never silently moved to SSO');
assert('MODULE_010_USES_SERVICES', portal.includes('clientId: activeTenant.services.clientId')
  && portal.includes('Module 010')
  && portal.includes('services connection'),
'Module 010 preview/import remains wired to the services application');
assert('SEPARATE_SECRET_FORMS', portal.includes("saveSecret('sso')")
  && portal.includes("saveSecret('services')")
  && portal.includes('/api/microsoft-integration/sso-client-secret')
  && portal.includes('/api/microsoft-integration/client-secret'),
'SSO and services secrets cannot overwrite one another');
assert('TEST_PRODUCTION_DOMAINS', portal.includes('onenecklab.com,onitdemo.com')
  && portal.includes('ussignal.com'),
'current Test and Production domain defaults are present');
assert('DIRECTORY_SYNC_MODES', portal.includes("'automatic' : 'manual'")
  && portal.includes('Manual only')
  && portal.includes('Automatic and manual')
  && portal.includes('syncFrequencyHours')
  && portal.includes('between 1 and 168 hours')
  && portal.includes('/api/admin/azure/config')
  && portal.includes('/api/admin/azure/import-settings'),
'Module 065 exposes manual/automatic directory sync and a configurable 1–168 hour interval');
assert('PER_ENVIRONMENT_MAIL', portal.includes('mail: defaultMail(environmentMode)')
  && portal.includes('mail: { ...tenant.mail }')
  && portal.includes("mailConfigurationScope: 'per_environment'")
  && portal.includes('Test and Production maintain independent')
  && portal.includes('configured transport'),
'Test and Production maintain independent provider, sender, SMTP, and recipient-boundary settings');
assert('MAIL_READINESS_ENVIRONMENT', mailReadiness.includes('environmentMode')
  && mailReadiness.includes('configuredProvider')
  && mailReadiness.includes('activeDeliveryProvider')
  && mailReadiness.includes('configuredTransportReady')
  && mailReadiness.includes('No live message is sent.'),
'non-delivery readiness tests the selected environment and distinguishes configured from active delivery');
assert('MODULE_010_RUNNING_ENVIRONMENT', compatibility.includes('function runtimeEnvironmentMode()')
  && compatibility.includes("String(tenant?.environmentMode || '').toLowerCase() === runtimeEnvironment")
  && compatibility.includes("status: 'module_065_services_profile_not_active'")
  && compatibility.includes('applyPayload?.runtimeActivated !== true'),
'Module 010 selects and verifies the running-environment Module 065 services profile before preview');
assert('MODULE_010_IMPORT_ONLY', stylesheet.includes('.route-azure-admin .azure-config-card')
  && stylesheet.includes('.route-azure-admin .azure-sync-summary-card')
  && stylesheet.includes('.route-azure-admin .azure-sync-runs-card')
  && stylesheet.includes("content: 'Preview and import Entra users'"),
'tenant and sync configuration remain in Module 065 while Module 010 keeps preview/import');
assert('OBSERVER_RECURSION_GUARD', !compatibility.includes('MutationObserver')
  && !compatibility.includes('querySelectorAll(')
  && !compatibility.includes('style.setProperty')
  && !compatibility.includes('.hidden =')
  && compatibility.includes("document.body?.classList.toggle('projectpulse-module010-directory-active'"),
'Module 010 compatibility does not mutate React-owned content');
assert('ACTIVE_REGISTRY_TITLES', registry.includes("moduleNumber: '010', route: 'azure-admin', displayName: 'Azure / Entra Directory Users'")
  && registry.includes("moduleNumber: '065', route: 'entra-secret-administration', displayName: 'Microsoft Integration Connection'"),
'active module names remain authoritative');
assert('MODULE_067_RETIRED_FROM_REGISTRY', !registry.includes("moduleNumber: '067'")
  && registry.includes("'global-mail-configuration': 'entra-secret-administration'"),
'Module 067 remains retired with route compatibility');
assert('PORTAL_MOUNT', main.includes("import MicrosoftIntegrationDualConnectionPortal from './MicrosoftIntegrationDualConnectionPortal.jsx';")
  && main.includes('<MicrosoftIntegrationDualConnectionPortal />')
  && !main.includes('<MicrosoftIntegrationPortal />'),
'dual portal is mounted once');
assert('SCOPED_STYLES', stylesheet.includes('projectpulse-microsoft-integration-active')
  && dualStylesheet.includes('.microsoft-environment-switcher')
  && dualStylesheet.includes('.microsoft-directory-sync-card')
  && dualStylesheet.includes('.microsoft-mail-environment-card'),
'styles remain scoped to Module 065 environment-specific sections');
assert('BUILD_GUARD', packageJson.scripts?.build?.includes('validate:module067')
  && packageJson.scripts?.['validate:module067']?.includes('validate-module-067-global-mail.mjs'),
'full frontend build runs the Microsoft Integration validator');

if (fullRuntimeContext) {
  assert('TRUSTED_HOST_PRECEDENCE', environmentResolver.includes('PROJECTPULSE_MICROSOFT_ENVIRONMENT')
    && environmentResolver.indexOf('var hostMode = FromHost') < environmentResolver.indexOf('"PROJECTPULSE_ENVIRONMENT"')
    && environmentResolver.includes('.onenecklab.com')
    && environmentResolver.includes('.ussignal.com')
    && environmentResolver.includes('ASPNETCORE_ENVIRONMENT'),
  'trusted public host selects Test/Production before generic application or ASP.NET runtime modes');
  assert('INTERACTIVE_SSO_HYDRATION', ssoInteractive.includes('StartPath = "/api/auth/sso/start"')
    && ssoInteractive.includes('ReadStoredProfileAsync(environmentMode)')
    && ssoInteractive.includes('PROJECTPULSE_SSO_CLIENT_SECRET')
    && ssoInteractive.includes('sso_redirect_host_mismatch')
    && ssoInteractive.includes('correlationId = context.TraceIdentifier'),
  'interactive SSO hydrates the matching profile and returns actionable sanitized errors');
  assert('MAIL_CONFIGURED_VS_ACTIVE', mailRuntime.includes('ConfiguredProvider')
    && mailRuntime.includes('activeDeliveryProvider')
    && mailRuntime.includes('PROJECTPULSE_MAIL_CONFIGURED_PROVIDER')
    && mailRuntime.includes('liveDeliveryEnabled')
    && mailRuntime.includes('configuredTransportReady'),
  'mail runtime separates configured transport readiness from live-delivery activation');
  assert('MAIL_BOUNDARY_ENFORCED', mailRuntime.includes('configuration.RecipientBoundary == "production_governed"')
    && mailRuntime.includes('moduleProvider = liveDeliveryEnabled')
    && mailRuntime.includes('sharedProvider = liveDeliveryEnabled')
    && mailRuntime.includes('"outbox_only"'),
  'Test-only and Locked boundaries cannot activate live delivery');
  assert('MAIL_TEST_SELECTED_PROFILE', mailTest.includes('MailTestRequest')
    && mailTest.includes('ReadStoredProfileAsync')
    && mailTest.includes('configuredProvider = profile.Provider')
    && mailTest.includes('configuredTransportReady = ready')
    && mailTest.includes('liveMessageSent = false')
    && mailTest.includes('secretValuesReturned = false'),
  'non-delivery test evaluates the selected stored profile without exposing secrets or sending email');
} else {
  console.log('MICROSOFT_INTEGRATION_RUNTIME_DEEP_CHECK=SKIPPED_MINIMAL_WEB_CONTEXT');
}

if (exists(files.migration045)) {
  const migration045 = read(files.migration045);
  const rollback045 = read(files.rollback045);
  assert('MIGRATION_045_PRESERVED', migration045.includes('045_microsoft_integration_consolidation')
    && migration045.includes('microsoft_integration_client_secrets'),
  'Graph/services migration 045 remains unchanged');
  assert('MIGRATION_045_GUARDED_ROLLBACK', rollback045.includes('Rollback blocked')
    && rollback045.includes('immutable Microsoft Integration audit evidence'),
  'migration 045 rollback remains guarded');
} else {
  console.log('MICROSOFT_INTEGRATION_MIGRATION_045_CHECK=SKIPPED_MINIMAL_WEB_CONTEXT');
}

if (exists(files.migration046) && exists(files.rollback046)) {
  const migration046 = read(files.migration046);
  const rollback046 = read(files.rollback046);
  assert('MIGRATION_046', migration046.includes('046_microsoft_sso_connection_profiles')
    && migration046.includes('microsoft_integration_sso_client_secrets'),
  'additive SSO-only migration 046 remains');
  assert('MIGRATION_046_ENVIRONMENTS', migration046.includes("environment_mode IN ('test', 'production')"),
  'SSO storage remains explicitly separated by Test and Production');
  assert('MIGRATION_046_GRAPH_UNCHANGED', migration046.includes('Microsoft services/Graph secrets remain')
    && !migration046.includes('DROP TABLE'),
  'migration 046 does not rewrite Graph/services storage');
  assert('MIGRATION_046_GUARDED_ROLLBACK', rollback046.includes('Rollback blocked: Microsoft SSO App Registration secret metadata exists.'),
  'SSO rollback remains guarded after a secret is saved');
} else if (fullRepositoryContext) {
  assert('MIGRATION_046_EXISTS', false, files.migration046);
} else {
  console.log('MICROSOFT_INTEGRATION_MIGRATION_046_CHECK=SKIPPED_MINIMAL_WEB_CONTEXT');
}

console.log('');
console.log(`MICROSOFT_INTEGRATION_VALIDATION_CHECKS=${checks.length}`);
if (checks.some((check) => !check.condition)) {
  const failed = checks.filter((check) => !check.condition).map((check) => check.name);
  console.error(`MICROSOFT_INTEGRATION_FAILED_CHECKS=${failed.join(',')}`);
  console.error('MICROSOFT_INTEGRATION_CONTRACT=FAILED');
  process.exit(1);
}
console.log('MICROSOFT_INTEGRATION_CONTRACT=PASSED');
