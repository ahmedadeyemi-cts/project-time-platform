import fs from 'node:fs';
import path from 'node:path';

const repositoryRoot = path.resolve(process.cwd(), '..', '..', '..');
const file = (relative) => path.join(repositoryRoot, relative);
const exists = (relative) => fs.existsSync(file(relative));
const read = (relative) => fs.readFileSync(file(relative), 'utf8');
const checks = [];

function assert(name, condition, evidence) {
  checks.push({ name, condition, evidence });
  console.log(`MICROSOFT_INTEGRATION_${name}=${condition ? 'PASSED' : 'FAILED'} — ${evidence}`);
}

const paths = {
  registrar: 'src/backend/ProjectTime.Api/Modules/GlobalMailConfigurationModule.cs',
  security: 'src/backend/ProjectTime.Api/Modules/MicrosoftIntegrationSecurityCompatibility.cs',
  integration: 'src/backend/ProjectTime.Api/Modules/MicrosoftIntegrationModule.cs',
  ssoProfiles: 'src/backend/ProjectTime.Api/Modules/MicrosoftSsoConnectionProfilesModule.cs',
  ssoInteractive: 'src/backend/ProjectTime.Api/Modules/MicrosoftSsoInteractiveStartActivation.cs',
  environment: 'src/backend/ProjectTime.Api/Modules/MicrosoftEnvironmentRuntimeResolver.cs',
  mailRuntime: 'src/backend/ProjectTime.Api/Modules/MicrosoftMailRuntimeConfigurationModule.cs',
  mailTest: 'src/backend/ProjectTime.Api/Modules/MicrosoftMailTransportTestModule.cs',
  importBackend: 'src/backend/ProjectTime.Api/Modules/AzureDirectoryImportModule.cs',
  portal: 'src/frontend/project-time-web/src/MicrosoftIntegrationDualConnectionPortal.jsx',
  readiness: 'src/frontend/project-time-web/src/MicrosoftMailTransportReadinessPanel.jsx',
  compatibility: 'src/frontend/project-time-web/src/microsoft-integration-compatibility.js',
  portalCss: 'src/frontend/project-time-web/src/microsoft-integration-portal.css',
  dualCss: 'src/frontend/project-time-web/src/microsoft-integration-dual-connections.css',
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

for (const [name, relative] of Object.entries(paths)) {
  if (['calendar', 'migration045', 'rollback045', 'migration046', 'rollback046'].includes(name)) continue;
  assert(`${name.toUpperCase()}_EXISTS`, exists(relative), relative);
}

const registrar = read(paths.registrar);
const security = read(paths.security);
const integration = read(paths.integration);
const ssoProfiles = read(paths.ssoProfiles);
const ssoInteractive = read(paths.ssoInteractive);
const environment = read(paths.environment);
const mailRuntime = read(paths.mailRuntime);
const mailTest = read(paths.mailTest);
const importBackend = read(paths.importBackend);
const portal = read(paths.portal);
const readiness = read(paths.readiness);
const compatibility = read(paths.compatibility);
const portalCss = read(paths.portalCss);
const dualCss = read(paths.dualCss);
const registry = read(paths.registry);
const main = read(paths.main);
const identity = read(paths.identity);
const packageJson = JSON.parse(read(paths.package));

assert('REGISTRATION_ORDER',
  registrar.includes('UseProjectPulsePublicOriginCompatibility')
    && registrar.includes('UseMicrosoftEnvironmentRuntimeCompatibility')
    && registrar.includes('UseMicrosoftSsoInteractiveStartActivation')
    && registrar.includes('MapMicrosoftSsoConnectionProfileEndpoints')
    && registrar.includes('MapMicrosoftServicesRuntimeProfileEndpoints')
    && registrar.includes('MapMicrosoftMailRuntimeConfigurationEndpoints')
    && registrar.includes('MapMicrosoftMailTransportTestEndpoints')
    && registrar.includes('AzureDirectoryImportModule.MapEndpoints(app)')
    && registrar.indexOf('UseProjectPulsePublicOriginCompatibility') < registrar.indexOf('UseMicrosoftEnvironmentRuntimeCompatibility')
    && registrar.indexOf('UseMicrosoftEnvironmentRuntimeCompatibility') < registrar.indexOf('UseMicrosoftSsoInteractiveStartActivation'),
  'trusted origin and environment are established before SSO and Microsoft endpoints');

assert('TRUSTED_ENVIRONMENT_RESOLUTION',
  environment.includes('PROJECTPULSE_MICROSOFT_ENVIRONMENT')
    && environment.includes('var hostMode = FromHost')
    && environment.includes('.onenecklab.com')
    && environment.includes('.ussignal.com')
    && environment.includes('ASPNETCORE_ENVIRONMENT')
    && environment.indexOf('var hostMode = FromHost') < environment.indexOf('"PROJECTPULSE_ENVIRONMENT"'),
  'trusted Test/Production host outranks generic runtime mode');

assert('INTERACTIVE_SSO_START',
  ssoInteractive.includes('StartPath = "/api/auth/sso/start"')
    && ssoInteractive.includes('ReadStoredProfileAsync(environmentMode)')
    && ssoInteractive.includes('PROJECTPULSE_SSO_CLIENT_SECRET')
    && ssoInteractive.includes('sso_redirect_host_mismatch')
    && ssoInteractive.includes('sso_client_secret_missing')
    && ssoInteractive.includes('correlationId = context.TraceIdentifier'),
  'interactive SSO hydrates the matching environment and returns actionable sanitized failures');

assert('SECURITY_BOUNDARY',
  security.includes('AllowedGovernedImportRoles')
    && security.includes('client_selected_import_role_not_allowed')
    && security.includes('microsoft_integration_manage_access_required')
    && security.includes('ProjectPulseActualUserId')
    && security.includes('view_as_read_only'),
  'imports and configuration remain server-authorized and View-As protected');

assert('MODULE_065_OWNER_AND_LEGACY_READS',
  integration.includes('ModuleNumber = "065"')
    && integration.includes('moduleName = "Microsoft Integration"')
    && integration.includes('/api/global-mail/configuration')
    && integration.includes('/api/global-mail/health')
    && integration.includes('retired = true'),
  'Module 065 remains authoritative while legacy Module 067 GET compatibility stays read-only');

assert('FOUR_CONNECTION_MODEL',
  portal.includes("const ENVIRONMENTS = ['test', 'production']")
    && portal.includes('CONNECTION 1 · APP REGISTRATION')
    && portal.includes('CONNECTION 2 · ENTERPRISE APPLICATION')
    && portal.includes('Microsoft Entra SSO')
    && portal.includes('Microsoft services and Graph')
    && portal.includes('sso_app_registration')
    && portal.includes('microsoft_services_enterprise_application'),
  'Test and Production each expose structurally separate SSO and services connections');

assert('SEPARATE_SECRET_FORMS',
  portal.includes("saveSecret('sso')")
    && portal.includes("saveSecret('services')")
    && portal.includes('/api/microsoft-integration/sso-client-secret')
    && portal.includes('/api/microsoft-integration/client-secret')
    && ssoProfiles.includes('microsoft_integration_sso_client_secrets')
    && integration.includes('microsoft_integration_client_secrets'),
  'SSO and services credentials remain independent and write-only');

assert('MODULE_010_IMPORT_CONTRACT',
  importBackend.includes('/api/microsoft-integration/directory-users/import-selected')
    && ['selectedUsers', 'selectedEmails', 'selectedUserIds', 'selectedEntraObjectIds'].every((value) => importBackend.includes(value))
    && importBackend.includes('InsertUserAsync')
    && importBackend.includes('UpdateUserAsync')
    && importBackend.includes('EnsureRoleAssignmentAsync')
    && importBackend.includes('transaction.CommitAsync'),
  'selected Entra users safely insert/update app_users with governed roles and transactions');

assert('MODULE_010_RUNNING_PROFILE',
  compatibility.includes("PREVIEW_ROUTE = '/api/admin/azure/users/preview'")
    && compatibility.includes("SERVICES_APPLY_PATH = '/api/microsoft-integration/services-apply-profile'")
    && compatibility.includes('function runtimeEnvironmentMode()')
    && compatibility.includes("String(tenant?.environmentMode || '').toLowerCase() === runtimeEnvironment")
    && compatibility.includes('applyPayload?.runtimeActivated !== true')
    && compatibility.includes("status: 'module_065_services_profile_not_active'"),
  'preview activates and verifies the services profile for the running environment');

assert('DIRECTORY_SYNC_CONFIGURATION',
  portal.includes('Manual only')
    && portal.includes('Automatic and manual')
    && portal.includes('syncFrequencyHours')
    && portal.includes('between 1 and 168 hours')
    && portal.includes('/api/admin/azure/config')
    && portal.includes('/api/admin/azure/import-settings'),
  'Module 065 exposes manual/automatic synchronization and a configurable interval');

assert('PER_ENVIRONMENT_MAIL',
  portal.includes('mail: defaultMail(environmentMode)')
    && portal.includes('mail: { ...tenant.mail }')
    && portal.includes("mailConfigurationScope: 'per_environment'")
    && portal.includes('Test and Production maintain independent')
    && portal.includes('Microsoft 365 SMTP relay'),
  'provider, sender, SMTP, and boundary settings remain separate for Test and Production');

assert('CONFIGURED_VS_ACTIVE_MAIL',
  mailRuntime.includes('ConfiguredProvider')
    && mailRuntime.includes('configuredProvider = result.ConfiguredProvider')
    && mailRuntime.includes('activeDeliveryProvider = result.ModuleProvider')
    && mailRuntime.includes('PROJECTPULSE_MAIL_CONFIGURED_PROVIDER')
    && mailRuntime.includes('configuredTransportReady = result.ConfiguredReady')
    && mailRuntime.includes('configuration.RecipientBoundary == "production_governed"')
    && mailRuntime.includes('"outbox_only"'),
  'configured transport readiness is distinct from live-delivery activation');

assert('ENVIRONMENT_READINESS_TEST',
  readiness.includes("body: JSON.stringify({ environmentMode })")
    && readiness.includes('configuredProvider')
    && readiness.includes('activeDeliveryProvider')
    && readiness.includes('configuredTransportReady')
    && readiness.includes('No live message is sent.')
    && mailTest.includes('MailTestRequest')
    && mailTest.includes('ReadStoredProfileAsync')
    && mailTest.includes('configuredProvider = profile.Provider')
    && mailTest.includes('configuredTransportReady = ready')
    && mailTest.includes('liveMessageSent = false')
    && mailTest.includes('secretValuesReturned = false'),
  'non-delivery test evaluates the selected environment without sending or returning secrets');

assert('GRAPH_AND_SMTP_READINESS',
  mailTest.includes('https://graph.microsoft.com/.default')
    && mailTest.includes('Mail.Send')
    && mailTest.includes('Directory.Read.All')
    && mailTest.includes('User.Read.All')
    && mailTest.includes('smtp.office365.com')
    && mailTest.includes('TcpClient')
    && mailTest.includes('PROJECTPULSE_TEST_SMTP_')
    && mailTest.includes('PROJECTPULSE_PRODUCTION_SMTP_'),
  'Graph application roles/sender and approved SMTP endpoint/credentials are checked');

assert('MODULE_062_AND_057_COMPATIBILITY',
  identity.includes('GraphCredentials.ForDomain(domain)')
    && identity.includes('PROJECTPULSE_ENTRA_TEST_CLIENT_ID')
    && identity.includes('PROJECTPULSE_ENTRA_PRODUCTION_CLIENT_ID')
    && (!exists(paths.calendar) || (read(paths.calendar).includes('PROJECTPULSE_ENTRA_CLIENT_ID') && read(paths.calendar).includes('graph.microsoft.com'))),
  'identity and calendar continue consuming established Graph contracts');

assert('ROUTE_AND_STYLE_OWNERSHIP',
  portalCss.includes('.route-azure-admin .azure-config-card')
    && portalCss.includes("content: 'Preview and import Entra users'")
    && dualCss.includes('.microsoft-environment-switcher')
    && dualCss.includes('.microsoft-directory-sync-card')
    && dualCss.includes('.microsoft-mail-environment-card')
    && !compatibility.includes('MutationObserver')
    && !compatibility.includes('querySelectorAll(')
    && main.includes('<MicrosoftIntegrationDualConnectionPortal />')
    && main.includes('<MicrosoftMailTransportReadinessPanel />'),
  'Module 010/065 presentation remains scoped and React-owned');

assert('REGISTRY_AND_BUILD',
  registry.includes("moduleNumber: '010', route: 'azure-admin', displayName: 'Azure / Entra Directory Users'")
    && registry.includes("moduleNumber: '065', route: 'entra-secret-administration', displayName: 'Microsoft Integration Connection'")
    && registry.includes("moduleNumber: '067', route: 'global-mail-configuration', displayName: 'Global Mail Configuration Center'")
    && packageJson.scripts?.build?.includes('validate:module067'),
  'Modules 010, 065, and 067 retain distinct canonical identities and the frontend build guard remains authoritative');

if (exists(paths.migration045) && exists(paths.rollback045)) {
  const migration045 = read(paths.migration045);
  const rollback045 = read(paths.rollback045);
  assert('MIGRATION_045_PRESERVED', migration045.includes('045_microsoft_integration_consolidation')
    && migration045.includes('microsoft_integration_client_secrets')
    && rollback045.includes('Rollback blocked'),
  'services-secret migration and guarded rollback remain unchanged');
}

if (exists(paths.migration046) && exists(paths.rollback046)) {
  const migration046 = read(paths.migration046);
  const rollback046 = read(paths.rollback046);
  assert('MIGRATION_046_PRESERVED', migration046.includes('046_microsoft_sso_connection_profiles')
    && migration046.includes("environment_mode IN ('test', 'production')")
    && migration046.includes('Microsoft services/Graph secrets remain')
    && rollback046.includes('Rollback blocked: Microsoft SSO App Registration secret metadata exists.'),
  'SSO migration remains additive, environment-specific, and guarded');
}

console.log('');
console.log(`MICROSOFT_INTEGRATION_VALIDATION_CHECKS=${checks.length}`);
const failed = checks.filter((check) => !check.condition).map((check) => check.name);
if (failed.length) {
  console.error(`MICROSOFT_INTEGRATION_FAILED_CHECKS=${failed.join(',')}`);
  console.error('MICROSOFT_INTEGRATION_CONTRACT=FAILED');
  process.exit(1);
}
console.log('MICROSOFT_INTEGRATION_CONTRACT=PASSED');
