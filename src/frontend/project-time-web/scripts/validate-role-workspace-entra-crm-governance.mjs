import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const frontendRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const repositoryRoot = path.resolve(frontendRoot, '..', '..', '..');

function read(relativePath) {
  return fs.readFileSync(path.join(repositoryRoot, relativePath), 'utf8');
}

function readOptional(relativePath) {
  const absolutePath = path.join(repositoryRoot, relativePath);
  return fs.existsSync(absolutePath) ? fs.readFileSync(absolutePath, 'utf8') : '';
}

function requireIncludes(content, values, label) {
  for (const value of values) {
    if (!content.includes(value)) {
      throw new Error(`${label} is missing required contract: ${value}`);
    }
  }
}

function requireExcludes(content, values, label) {
  for (const value of values) {
    if (content.includes(value)) {
      throw new Error(`${label} contains prohibited contract: ${value}`);
    }
  }
}

function requireCount(content, value, expected, label) {
  const count = content.split(value).length - 1;
  if (count !== expected) {
    throw new Error(`${label} expected ${expected} occurrence(s) of ${value}, found ${count}.`);
  }
}

function requireOrdered(content, values, label) {
  let cursor = -1;
  for (const value of values) {
    const next = content.indexOf(value, cursor + 1);
    if (next < 0) {
      throw new Error(`${label} is missing ordered contract: ${value}`);
    }
    cursor = next;
  }
}

const roleGovernance = read('src/frontend/project-time-web/src/role-workspace-governance.js');
const app = read('src/frontend/project-time-web/src/App.jsx');
const welcome = read('src/frontend/project-time-web/src/RoleWelcomeDashboard.jsx');
const moduleAvailabilityBridge = read('src/frontend/project-time-web/src/module-availability-bridge.js');
const entraCenter = read('src/frontend/project-time-web/src/EntraSecretAdministrationCenter.jsx');
const entraPanel = read('src/frontend/project-time-web/src/EntraSecretExpirationGovernancePanel.jsx');
const entraWarning = read('src/frontend/project-time-web/src/EntraSecretExpirationGlobalWarning.jsx');
const crmCenter = read('src/frontend/project-time-web/src/CrmErpIntegrationCenter.jsx');
const crmPanel = read('src/frontend/project-time-web/src/CrmErpTokenPersistencePanel.jsx');
const entraBackend = readOptional('src/backend/ProjectTime.Api/Modules/EntraSecretExpirationGovernanceModule.cs');
const crmBackend = readOptional('src/backend/ProjectTime.Api/Modules/CrmErpOAuthPersistence.cs');
const migration = readOptional('database/migrations/056_role_workspace_entra_crm_governance.sql');
const rollback = readOptional('database/rollback/056_role_workspace_entra_crm_governance_rollback.sql');
const migrationTest = readOptional('tests/test-role-workspace-entra-crm-governance-migration-056.sh');
const projectFile = read('src/backend/ProjectTime.Api/ProjectTime.Api.csproj');
const packageFile = read('src/frontend/project-time-web/package.json');

requireIncludes(roleGovernance, [
  "return 'Project Management';",
  "return 'Project Management Lead';",
  "return 'Accounting';",
  "return 'Billing';",
  "return 'Inside Sales';",
  "return 'Sales';",
  "'manager-approval'",
  "'project-workload'",
  "'project-workspace'",
  "'billing-readiness'",
  "'invoice-billing-center'",
  "'crm-integration'",
  "'opportunities'",
  'PROJECT_MANAGEMENT_DENIED_ROUTES',
  'BUSINESS_ROLE_DENIED_ROUTES',
  "'timesheet'",
  "'utilization'",
  "'project-allocation-info'",
  "'qualifications-certifications'",
  "'oncall-scheduling'",
], 'role workspace governance');

requireIncludes(app, [
  'ROLE_WORKSPACE_ENTRA_CRM_GOVERNANCE_IMPORTS',
  'ROLE_WORKSPACE_GOVERNANCE_VISIBLE_MODULES',
  'applyRoleWorkspaceGovernance(user, permissionFilteredModules, roleWorkspaceModules)',
  'return getRoleWorkspaceName(user);',
  'ROLE_WORKSPACE_SIGNED_IN_USER',
  'Signed in as',
  'ENTRA_SECRET_EXPIRATION_GLOBAL_WARNING_MOUNT',
  '<EntraSecretExpirationGlobalWarning authSession={authSession} />',
], 'App workspace governance');
requireCount(app, 'ROLE_WORKSPACE_GOVERNANCE_VISIBLE_MODULES', 1, 'App workspace governance');
requireCount(app, 'ENTRA_SECRET_EXPIRATION_GLOBAL_WARNING_MOUNT', 1, 'App expiration warning');

requireIncludes(welcome, [
  'ROLE_WORKSPACE_WELCOME_IMPORT',
  'welcomeDisplayName',
  'getRoleWorkspaceLabel(normalizedRoles)',
  'getRoleWorkspaceName(normalizedRoles)',
  "'ACCOUNTING'",
  "'ACCOUNTING_BILLING'",
  "'BILLING'",
  "'FINANCE'",
], 'role welcome dashboard');
requireExcludes(welcome, [
  "String(displayName || 'there').trim().split(/\\s+/)[0]",
  '<small>{titleCase(persona)} workspace</small>',
], 'role welcome dashboard');

requireIncludes(moduleAvailabilityBridge, [
  "const SUPER_ADMINISTRATOR_ROLE_CODES = new Set(['SUPER_ADMINISTRATOR', 'ADMINISTRATOR']);",
  'const actualSuperAdministrator = !viewAs',
  '&& actorRoles.some((roleCode) => SUPER_ADMINISTRATOR_ROLE_CODES.has(roleCode));',
  'if (!actualSuperAdministrator) {',
  'permanentFullControl: actualSuperAdministrator',
  'permanentFullControl: Boolean(effectiveActor.permanentFullControl)',
], 'Super Administrator full-control navigation invariant');
requireOrdered(moduleAvailabilityBridge, [
  'const actualSuperAdministrator = !viewAs',
  'if (!actualSuperAdministrator) {',
  'for (const module of PROJECTPULSE_MODULES)',
  'matrix.grants',
  'deniedModuleNumbers = denied;',
  'permanentFullControl: actualSuperAdministrator',
], 'Super Administrator full-control navigation invariant');
requireExcludes(moduleAvailabilityBridge, [
  "const actualSuperAdministrator = !viewAs && roleSet.has('SUPER_ADMINISTRATOR');",
], 'Super Administrator full-control navigation invariant');

requireIncludes(entraCenter, [
  'ENTRA_EXPIRATION_GOVERNANCE_PANEL_IMPORT',
  'ENTRA_EXPIRATION_GOVERNANCE_PANEL_MOUNT',
  '<EntraSecretExpirationGovernancePanel />',
], 'Module 065 frontend mount');
requireIncludes(entraPanel, [
  '/api/entra-secret-expiration/profile',
  '/api/entra-secret-expiration/acknowledge',
  '/api/entra-secret-expiration/reminders/run',
  'Secret version identifier',
  'Client secret expiration date and time',
  'Project Team Coordinator acknowledgement status',
  'secret value remains outside this browser',
], 'Module 065 expiration workspace');
requireIncludes(entraWarning, [
  '/api/entra-secret-expiration/status',
  'showGlobalWarning',
  'Microsoft Integration client secret',
  'Open Module 065',
], 'Module 065 global warning');

requireIncludes(crmCenter, [
  'CRM_ERP_TOKEN_PERSISTENCE_PANEL_IMPORT',
  'CRM_ERP_TOKEN_PERSISTENCE_PANEL_MOUNT',
  '<CrmErpTokenPersistencePanel',
], 'Module 026 persistence mount');
requireIncludes(crmPanel, [
  '/api/integrations/026/token-refresh/status',
  '/refresh-token',
  'Automatic OAuth token renewal',
  'access tokens are never displayed',
], 'Module 026 persistence panel');

if (entraBackend) {
  requireIncludes(entraBackend, [
    'MapEntraSecretExpirationGovernanceEndpoints',
    'UseEntraSecretExpirationGovernance',
    '/api/entra-secret-expiration/status',
    '/api/entra-secret-expiration/profile',
    '/api/entra-secret-expiration/acknowledge',
    '/api/entra-secret-expiration/reminders/run',
    'PROJECT_TEAM_COORDINATOR',
    'RECIPIENT_ACKNOWLEDGED',
    'reminder_interval_hours',
    'Module065ProjectNotificationDelivery.DeliverAsync',
    'secretValueStored = false',
    'criticalWarningDismissed = false',
  ], 'Module 065 backend');
  requireExcludes(entraBackend, [
    'ClientSecret =',
    'client_secret_value',
  ], 'Module 065 backend secret boundary');
} else {
  console.log('ROLE_WORKSPACE_ENTRA_CRM_MODULE_065_BACKEND_CHECK=SKIPPED_MINIMAL_WEB_CONTEXT');
}

if (crmBackend) {
  requireIncludes(crmBackend, [
    'ResolveManageAuthorityAsync',
    'HasManageAuthorityLegacyAsync',
    'ResolveManageAuthorityPolicyFirstAsync',
    'actual_session_administrator_or_permission',
    'MapCrmErpOAuthPersistenceEndpoints',
    'UseCrmErpOAuthPersistence',
    '/token-refresh/status',
    '/providers/{providerKey}/refresh-token',
    'grant_type',
    'refresh_token',
    'SaveCredentialAsync',
    'pg_try_advisory_lock',
    'crm_integration_token_refresh_events',
    'accessTokenReturned = false',
    'refreshTokenReturned = false',
    'clientSecretReturned = false',
  ], 'Module 026 OAuth persistence backend');
} else {
  console.log('ROLE_WORKSPACE_ENTRA_CRM_MODULE_026_BACKEND_CHECK=SKIPPED_MINIMAL_WEB_CONTEXT');
}

if (migration && rollback && migrationTest) {
  requireIncludes(migration, [
    '056_role_workspace_entra_crm_governance',
    'entra_secret_expiration_profile_versions',
    'entra_secret_expiration_recipients',
    'entra_secret_expiration_acknowledgements',
    'entra_secret_expiration_reminder_claims',
    'entra_secret_expiration_reminder_events',
    'entra_secret_expiration_audit_events',
    'crm_integration_token_refresh_events',
    'role_workspace_permission_changes_056',
    'projectpulse_056_block_immutable_mutation',
    'VIEW_ENTRA_SECRET_EXPIRATION',
    'MANAGE_ENTRA_SECRET_EXPIRATION',
    'ACKNOWLEDGE_ENTRA_SECRET_EXPIRATION',
    "'PROJECT_MANAGER', '002'",
    "'ACCOUNTING', '039'",
    "'INSIDE_SALES', '026'",
    "upper(role.role_code) = 'SUPER_ADMINISTRATOR'",
  ], 'migration 056');
  requireIncludes(rollback, [
    "change_kind = 'removed'",
    "change_kind = 'granted'",
    'DROP TABLE IF EXISTS entra_secret_expiration_profile_versions',
    'DROP TABLE IF EXISTS crm_integration_token_refresh_events',
    "migration_id = '056_role_workspace_entra_crm_governance'",
  ], 'migration 056 rollback');
  requireIncludes(migrationTest, [
    'apply_migration',
    'expiration_profile_immutable',
    'acknowledgement_immutable',
    'oauth_refresh_evidence_immutable',
    'rollback_restored_removed_access',
    'ROLE_WORKSPACE_ENTRA_CRM_GOVERNANCE_MIGRATION_056=PASS',
  ], 'migration 056 test');
} else {
  console.log('ROLE_WORKSPACE_ENTRA_CRM_MIGRATION_056_CHECK=SKIPPED_MINIMAL_WEB_CONTEXT');
}

requireIncludes(projectFile, [
  'CrmErpAdministrationExperienceGenerated',
  'ResolveManageAuthorityPolicyFirstAsync',
  'MapCrmErpOAuthPersistenceEndpoints',
  'UseCrmErpOAuthPersistence',
  'MapEntraSecretExpirationGovernanceEndpoints',
  'UseEntraSecretExpirationGovernance',
], 'API project integration');
requireIncludes(packageFile, [
  'inject-role-workspace-entra-crm-governance.mjs',
  'validate:role-workspace-entra-crm-governance',
  'validate-role-workspace-entra-crm-governance.mjs',
], 'frontend package integration');

console.log('ROLE_WORKSPACE_ENTRA_CRM_GOVERNANCE_VALIDATION=PASS');
