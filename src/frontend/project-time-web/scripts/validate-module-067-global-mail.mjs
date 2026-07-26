import fs from 'node:fs';
import path from 'node:path';

const frontendRoot = process.cwd();
const repositoryRoot = path.resolve(frontendRoot, '..', '..', '..');
const files = {
  backend: 'src/backend/ProjectTime.Api/Modules/GlobalMailConfigurationModule.cs',
  portal: 'src/frontend/project-time-web/src/MicrosoftIntegrationPortal.jsx',
  compatibility: 'src/frontend/project-time-web/src/microsoft-integration-compatibility.js',
  stylesheet: 'src/frontend/project-time-web/src/microsoft-integration-portal.css',
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

for (const [name, relative] of Object.entries(files)) {
  if (name === 'migration' || name === 'rollback') continue;
  assert(`${name.toUpperCase()}_EXISTS`, exists(relative), relative);
}

const backend = read(files.backend);
const portal = read(files.portal);
const compatibility = read(files.compatibility);
const stylesheet = read(files.stylesheet);
const main = read(files.main);
const identity = read(files.identity);
const packageJson = JSON.parse(read(files.package));

assert('MODULE_065_OWNER', backend.includes('ActiveModuleNumber = "065"') && backend.includes('moduleName = "Microsoft Integration"'), 'Module 065 is the active owner');
assert('MODULE_067_COMPATIBILITY', backend.includes('/api/global-mail/configuration') && backend.includes('/api/global-mail/health') && backend.includes('retired = true') && backend.includes('redirectRoute = ActiveRoute'), 'legacy GET compatibility remains');
assert('MODULE_067_PERMISSIONS_MAPPED', ['VIEW_GLOBAL_MAIL_CONFIGURATION', 'MANAGE_GLOBAL_MAIL_CONFIGURATION', 'VIEW_GLOBAL_MAIL', 'MANAGE_GLOBAL_MAIL'].every((value) => backend.includes(value)), 'legacy permissions accepted by Module 065');
assert('UNIQUE_IMPORT_ENDPOINT', backend.includes('/api/microsoft-integration/directory-users/import-selected') && compatibility.includes('/api/admin/azure/users/import-selected') && compatibility.includes('/api/microsoft-integration/directory-users/import-selected'), 'legacy browser call rewrites to unique repaired endpoint');
assert('SELECTED_IDENTIFIERS', ['selectedUsers', 'selectedEmails', 'selectedUserIds', 'selectedEntraObjectIds'].every((value) => backend.includes(value)), 'preview/import identifiers remain compatible');
assert('SESSION_AUTHORITY', backend.includes('ProjectPulseActualUserId') && backend.includes('ProjectPulseSessionUserId') && backend.includes('view_as_read_only'), 'actual session and View-As write protection');
assert('APP_USERS_PERSISTENCE', backend.includes('app_users') && backend.includes('InsertAppUserAsync') && backend.includes('UpdateAppUserAsync'), 'insert or safe upsert into app_users');
assert('DUPLICATE_REPORTING', backend.includes('existing_user_upserted') && backend.includes('duplicate = outcomes.Count'), 'duplicates are reported');
assert('ROLE_ASSIGNMENT_EXPLICIT', backend.includes('defaultRoleCode') && backend.includes('EnsureRoleAssignmentAsync') && backend.includes('roleAssignment ='), 'role assignment behavior is explicit');
assert('TRANSACTION_COMMIT', backend.includes('SAVEPOINT module010_import_') || (backend.includes('SAVEPOINT {savepoint}') && backend.includes('transaction.CommitAsync')), 'per-user savepoints and final commit');
assert('RESULT_COUNTS', ['imported,', 'skipped,', 'duplicate,', 'failed,'].every((value) => backend.includes(value)), 'response reports all requested result categories');
assert('DOWNSTREAM_VISIBILITY', backend.includes('userAdministration = true') && backend.includes('activeUserSelectors = true') && backend.includes('identityProfileModule062 = true'), 'import visibility contract');

assert('WRITE_ONLY_SECRET', portal.includes('type="password"') && backend.includes('/api/microsoft-integration/client-secret') && backend.includes('secretReturned = false'), 'client secret is enterable and never returned');
assert('ENCRYPTED_SECRET', backend.includes('AesGcm') && backend.includes('microsoft_integration_client_secrets') && backend.includes('CryptographicOperations.ZeroMemory'), 'encrypted storage and memory clearing');
assert('IDENTITY_HYDRATION', backend.includes('PROJECTPULSE_ENTRA_TEST_CLIENT_SECRET') && backend.includes('PROJECTPULSE_ENTRA_PRODUCTION_CLIENT_SECRET') && backend.includes('PROJECTPULSE_ENTRA_CLIENT_SECRET'), 'existing identity environment contract is hydrated');
assert('IDENTITY_SOURCE_PRESERVED', identity.includes('GraphCredentials.ForDomain(domain)') && identity.includes('PROJECTPULSE_ENTRA_TEST_CLIENT_SECRET') && identity.includes('PROJECTPULSE_ENTRA_PRODUCTION_CLIENT_SECRET'), 'Module 062 identity implementation remains intact');
assert('GRAPH_CONNECTION_TEST', backend.includes('/api/microsoft-integration/test-connection') && backend.includes('graph.microsoft.com/v1.0/users') && backend.includes('Directory.Read.All') && backend.includes('User.Read.All'), 'application permission test');
assert('DELEGATED_PERMISSION', portal.includes('User.Read') && backend.includes('delegatedProfilePermission = "User.Read"'), 'delegated profile permission documented');

assert('MULTI_TENANT_UI', portal.includes('Add tenant') && portal.includes('configuration.tenants') && portal.includes('activeTenantKey'), 'one or more tenant profiles');
assert('DIRECTORY_SYNC_MOVED', portal.includes('Directory synchronization') && portal.includes('/api/admin/azure/config') && portal.includes('/api/admin/azure/import-settings'), 'sync configuration moved to Module 065 while APIs remain compatible');
assert('MAIL_CONSOLIDATED', portal.includes('Microsoft 365 / SMTP') && portal.includes('Sender mailbox') && portal.includes('smtp.office365.com'), 'Module 067 mail capabilities consolidated');
assert('MODULE_010_IMPORT_ONLY', compatibility.includes('.azure-config-card, .azure-sync-summary-card') && compatibility.includes('Preview and import Entra users'), 'tenant and sync cards removed from Module 010');
assert('MODULE_067_HIDDEN', compatibility.includes('data-module-067-retired') && compatibility.includes('surface.hidden = true'), 'retired module hidden from active navigation/cards');
assert('ROUTE_REDIRECT', compatibility.includes('window.location.replace(`#${ACTIVE_ROUTE}`)'), 'old route redirects to Module 065');
assert('PORTAL_MOUNT', main.includes("import MicrosoftIntegrationPortal from './MicrosoftIntegrationPortal.jsx';") && main.includes('<MicrosoftIntegrationPortal />'), 'portal mounted once');
assert('SCOPED_STYLES', stylesheet.includes('projectpulse-microsoft-integration-active') && !/(^|\n)\s*(?:html|\.panel|\.sidebar)\s*\{/m.test(stylesheet), 'styles are scoped to the integration route');
assert('BUILD_GUARD', packageJson.scripts?.build?.includes('validate:module067') && packageJson.scripts?.['validate:module067']?.includes('validate-module-067-global-mail.mjs'), 'full frontend build runs consolidation validator');

if (exists(files.migration)) {
  const migration = read(files.migration);
  const rollback = read(files.rollback);
  assert('MIGRATION_045', migration.includes('045_microsoft_integration_consolidation') && migration.includes('microsoft_integration_client_secrets'), 'additive migration 045');
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
