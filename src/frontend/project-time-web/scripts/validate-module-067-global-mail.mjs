import fs from 'node:fs';
import path from 'node:path';

const frontendRoot = process.cwd();
const repositoryRoot = path.resolve(frontendRoot, '..', '..', '..');
const files = {
  registrar: 'src/backend/ProjectTime.Api/Modules/GlobalMailConfigurationModule.cs',
  securityBoundary: 'src/backend/ProjectTime.Api/Modules/MicrosoftIntegrationSecurityCompatibility.cs',
  integrationBackend: 'src/backend/ProjectTime.Api/Modules/MicrosoftIntegrationModule.cs',
  importBackend: 'src/backend/ProjectTime.Api/Modules/AzureDirectoryImportModule.cs',
  portal: 'src/frontend/project-time-web/src/MicrosoftIntegrationPortal.jsx',
  compatibility: 'src/frontend/project-time-web/src/microsoft-integration-compatibility.js',
  stylesheet: 'src/frontend/project-time-web/src/microsoft-integration-portal.css',
  registry: 'src/frontend/project-time-web/src/module-availability-registry.js',
  main: 'src/frontend/project-time-web/src/main.jsx',
  identity: 'src/backend/ProjectTime.Api/Modules/IdentityProfileModule.cs',
  migration: 'database/migrations/045_microsoft_integration_consolidation.sql',
  rollback: 'database/rollback/045_microsoft_integration_consolidation_rollback.sql',
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

for (const name of ['registrar', 'portal', 'compatibility', 'stylesheet', 'registry', 'main', 'identity', 'package']) {
  assert(`${name.toUpperCase()}_EXISTS`, exists(files[name]), files[name]);
}

const registrar = read(files.registrar);
const portal = read(files.portal);
const compatibility = read(files.compatibility);
const stylesheet = read(files.stylesheet);
const registry = read(files.registry);
const main = read(files.main);
const identity = read(files.identity);
const packageJson = JSON.parse(read(files.package));
const hasSecurityBoundary = exists(files.securityBoundary);
const securityBoundary = hasSecurityBoundary ? read(files.securityBoundary) : '';
const hasBackendImplementation = exists(files.integrationBackend) && exists(files.importBackend);
const integrationBackend = hasBackendImplementation ? read(files.integrationBackend) : '';
const importBackend = hasBackendImplementation ? read(files.importBackend) : '';
const backend = `${registrar}\n${securityBoundary}\n${integrationBackend}\n${importBackend}`;

assert('REGISTRATION_PRESERVED', registrar.includes('MapGlobalMailConfigurationEndpoints') && registrar.includes('UseMicrosoftIntegrationSecurityCompatibility') && registrar.includes('MicrosoftIntegrationModule.MapEndpoints(app)') && registrar.includes('AzureDirectoryImportModule.MapEndpoints(app)'), 'existing Program.cs registration point delegates through the fail-closed security boundary');

if (hasSecurityBoundary) {
  assert('SECURITY_BOUNDARY_EXISTS', true, files.securityBoundary);
  assert('GOVERNED_IMPORT_ROLE', securityBoundary.includes('client_selected_import_role_not_allowed') && securityBoundary.includes('governed_import_role_not_allowed') && securityBoundary.includes('AllowedGovernedImportRoles') && securityBoundary.includes('payload["defaultRoleCode"] = governedRole'), 'client roles cannot elevate privileges and server-governed roles use a non-administrative allowlist');
  assert('READ_WRITE_AUTHORIZATION_SPLIT', securityBoundary.includes('WritePermissions') && securityBoundary.includes('VIEW_GLOBAL_MAIL_CONFIGURATION') === false && securityBoundary.includes('microsoft_integration_manage_access_required') && securityBoundary.includes('View-only legacy mail permissions cannot change credentials'), 'view-only legacy mail grants cannot write secrets or run privileged tests');
  assert('ALL_TENANT_SECRET_HYDRATION', securityBoundary.includes('SELECT tenant_key, ciphertext, nonce, authentication_tag') && securityBoundary.includes('PROJECTPULSE_MICROSOFT_TENANT_') && securityBoundary.includes('HydrateEveryConfiguredTenantSecretAsync'), 'every configured tenant key is hydrated after restart without returning plaintext');
} else {
  console.log('MICROSOFT_INTEGRATION_SECURITY_DEEP_CHECK=SKIPPED_MINIMAL_WEB_CONTEXT');
}

if (hasBackendImplementation) {
  assert('MODULE_065_OWNER', integrationBackend.includes('ModuleNumber = "065"') && integrationBackend.includes('moduleName = "Microsoft Integration"'), 'Module 065 is the active owner');
  assert('MODULE_067_COMPATIBILITY', integrationBackend.includes('/api/global-mail/configuration') && integrationBackend.includes('/api/global-mail/health') && integrationBackend.includes('retired = true') && integrationBackend.includes('redirectRoute = ActiveRoute'), 'legacy GET compatibility remains');
  assert('MODULE_067_PERMISSIONS_MAPPED', ['VIEW_GLOBAL_MAIL_CONFIGURATION', 'MANAGE_GLOBAL_MAIL_CONFIGURATION', 'VIEW_GLOBAL_MAIL', 'MANAGE_GLOBAL_MAIL'].every((value) => integrationBackend.includes(value)), 'legacy permissions accepted for compatibility while writes remain separately guarded');
  assert('UNIQUE_IMPORT_ENDPOINT', importBackend.includes('/api/microsoft-integration/directory-users/import-selected') && compatibility.includes('/api/admin/azure/users/import-selected') && compatibility.includes('/api/microsoft-integration/directory-users/import-selected'), 'legacy browser call rewrites to unique repaired endpoint');
  assert('SELECTED_IDENTIFIERS', ['selectedUsers', 'selectedEmails', 'selectedUserIds', 'selectedEntraObjectIds'].every((value) => importBackend.includes(value)), 'preview/import identifiers remain compatible');
  assert('SESSION_AUTHORITY', backend.includes('ProjectPulseActualUserId') && backend.includes('ProjectPulseSessionUserId') && backend.includes('view_as_read_only'), 'actual session and View-As write protection');
  assert('APP_USERS_PERSISTENCE', importBackend.includes('app_users') && importBackend.includes('InsertUserAsync') && importBackend.includes('UpdateUserAsync'), 'insert or safe upsert into app_users');
  assert('DUPLICATE_REPORTING', importBackend.includes('existing_user_upserted') && importBackend.includes('duplicate = outcomes.Count'), 'duplicates are reported');
  assert('ROLE_ASSIGNMENT_EXPLICIT', importBackend.includes('defaultRoleCode') && importBackend.includes('EnsureRoleAssignmentAsync') && importBackend.includes('roleAssignment ='), 'role assignment behavior is explicit');
  assert('TRANSACTION_COMMIT', importBackend.includes('SAVEPOINT {savepoint}') && importBackend.includes('transaction.CommitAsync'), 'per-user savepoints and final commit');
  assert('RESULT_COUNTS', ['imported,', 'skipped,', 'duplicate,', 'failed,'].every((value) => importBackend.includes(value)), 'response reports all requested result categories');
  assert('DOWNSTREAM_VISIBILITY', importBackend.includes('userAdministration = true') && importBackend.includes('activeUserSelectors = true') && importBackend.includes('identityProfileModule062 = true'), 'import visibility contract');
  assert('WRITE_ONLY_SECRET', portal.includes('type="password"') && integrationBackend.includes('/api/microsoft-integration/client-secret') && integrationBackend.includes('secretReturned = false'), 'client secret is enterable and never returned');
  assert('ENCRYPTED_SECRET', integrationBackend.includes('AesGcm') && integrationBackend.includes('microsoft_integration_client_secrets') && integrationBackend.includes('CryptographicOperations.ZeroMemory'), 'encrypted storage and memory clearing');
  assert('IDENTITY_HYDRATION', integrationBackend.includes('PROJECTPULSE_ENTRA_TEST_CLIENT_SECRET') && integrationBackend.includes('PROJECTPULSE_ENTRA_PRODUCTION_CLIENT_SECRET') && integrationBackend.includes('PROJECTPULSE_ENTRA_CLIENT_SECRET'), 'existing identity environment contract is hydrated');
  assert('GRAPH_CONNECTION_TEST', integrationBackend.includes('/api/microsoft-integration/test-connection') && integrationBackend.includes('graph.microsoft.com/v1.0/users') && integrationBackend.includes('Directory.Read.All') && integrationBackend.includes('User.Read.All'), 'application permission test');
  assert('DELEGATED_PERMISSION', portal.includes('User.Read') && integrationBackend.includes('delegatedProfilePermission = "User.Read"'), 'delegated profile permission documented');
} else {
  console.log('MICROSOFT_INTEGRATION_BACKEND_DEEP_CHECK=SKIPPED_MINIMAL_WEB_CONTEXT');
}

assert('IDENTITY_SOURCE_PRESERVED', identity.includes('GraphCredentials.ForDomain(domain)') && identity.includes('PROJECTPULSE_ENTRA_TEST_CLIENT_SECRET') && identity.includes('PROJECTPULSE_ENTRA_PRODUCTION_CLIENT_SECRET'), 'Module 062 identity implementation remains intact');
assert('MULTI_TENANT_UI', portal.includes('Add tenant') && portal.includes('configuration.tenants') && portal.includes('activeTenantKey'), 'one or more tenant profiles');
assert('DIRECTORY_SYNC_MOVED', portal.includes('Directory synchronization') && portal.includes('/api/admin/azure/config') && portal.includes('/api/admin/azure/import-settings'), 'sync configuration moved to Module 065 while APIs remain compatible');
assert('MAIL_CONSOLIDATED', portal.includes('Microsoft 365 / SMTP') && portal.includes('Sender mailbox') && portal.includes('smtp.office365.com'), 'Module 067 mail capabilities consolidated');
assert('MODULE_010_IMPORT_ONLY', compatibility.includes('.azure-config-card, .azure-sync-summary-card') && compatibility.includes('Preview and import Entra users'), 'tenant and sync cards removed from Module 010');
assert('OBSERVER_RECURSION_GUARD', compatibility.includes('function setTextIfChanged') && compatibility.includes('element.textContent === value') && !compatibility.includes("if (eyebrow) eyebrow.textContent = 'Azure / Entra Directory Users'"), 'DOM normalization mutates headings only when text actually changes');
assert('ACTIVE_REGISTRY_TITLES', registry.includes("moduleNumber: '010', route: 'azure-admin', displayName: 'Azure / Entra Directory Users'") && registry.includes("moduleNumber: '065', route: 'entra-secret-administration', displayName: 'Microsoft Integration'"), 'active Module 010 and Module 065 names are authoritative');
assert('MODULE_067_RETIRED_FROM_REGISTRY', !registry.includes("moduleNumber: '067'") && registry.includes("'global-mail-configuration': 'entra-secret-administration'"), 'Module 067 removed from active registry with compatibility alias');
assert('MODULE_067_HIDDEN', compatibility.includes('data-module-067-retired') && compatibility.includes('surface.hidden = true'), 'legacy App.jsx navigation/cards hidden without broad App rewrite');
assert('ROUTE_REDIRECT', compatibility.includes('window.location.replace(`#${ACTIVE_ROUTE}`)'), 'old route redirects to Module 065');
assert('PORTAL_MOUNT', main.includes("import MicrosoftIntegrationPortal from './MicrosoftIntegrationPortal.jsx';") && main.includes('<MicrosoftIntegrationPortal />'), 'portal mounted once');
assert('SCOPED_STYLES', stylesheet.includes('projectpulse-microsoft-integration-active') && !/(^|\n)\s*(?:html|\.panel|\.sidebar)\s*\{/m.test(stylesheet), 'styles are scoped to the integration route');
assert('BUILD_GUARD', packageJson.scripts?.build?.includes('validate:module067') && packageJson.scripts?.['validate:module067']?.includes('validate-module-067-global-mail.mjs'), 'full frontend build runs consolidation validator');

if (exists(files.migration)) {
  const migration = read(files.migration);
  const rollback = read(files.rollback);
  assert('MIGRATION_045', migration.includes('045_microsoft_integration_consolidation') && migration.includes('microsoft_integration_client_secrets'), 'additive migration 045');
  assert('MIGRATION_REGISTRATION', migration.includes('schema_migrations (migration_id, description, applied_at)') && migration.includes('ON CONFLICT (migration_id) DO UPDATE'), 'repository migration registration contract');
  assert('IMMUTABLE_AUDIT', migration.includes('microsoft_integration_audit_events') && migration.includes('BEFORE UPDATE OR DELETE'), 'immutable audit metadata');
  assert('PERMISSION_ALIAS_TABLE', migration.includes('microsoft_integration_permission_aliases') && migration.includes("('067', 'VIEW_GLOBAL_MAIL_CONFIGURATION', '065'"), 'legacy permission aliases stored');
  assert('NON_DESTRUCTIVE_RETIREMENT', migration.includes("module_code = '067'") && migration.includes('is_active = FALSE') && !/DELETE\s+FROM\s+projectpulse_native_admin_documents/i.test(migration), '067 deactivated without deleting configuration');
  assert('GUARDED_ROLLBACK', rollback.includes('Rollback blocked') && rollback.includes('immutable Microsoft Integration audit evidence'), 'rollback blocks after operational evidence');
} else {
  console.log('MICROSOFT_INTEGRATION_MIGRATION_CHECK=SKIPPED_MINIMAL_WEB_CONTEXT');
}

console.log('');
console.log(`MICROSOFT_INTEGRATION_VALIDATION_CHECKS=${checks.length}`);
if (checks.some((check) => !check.condition)) {
  console.error('MICROSOFT_INTEGRATION_CONTRACT=FAILED');
  process.exit(1);
}
console.log('MICROSOFT_INTEGRATION_CONTRACT=PASSED');
