import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const frontendRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const repositoryRoot = path.resolve(frontendRoot, '..', '..', '..');
const absolute = (relativePath) => path.join(repositoryRoot, relativePath);
const exists = (relativePath) => fs.existsSync(absolute(relativePath));
const read = (relativePath) => fs.readFileSync(absolute(relativePath), 'utf8');
const optional = (relativePath) => exists(relativePath) ? read(relativePath) : '';

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

function objectArrayBlock(content, propertyName, label) {
  const pattern = new RegExp(`${propertyName}: Object\\.freeze\\(\\[([\\s\\S]*?)\\]\\),`);
  const match = content.match(pattern);
  if (!match) throw new Error(`${label} is missing ${propertyName}.`);
  return match[1];
}

const roleGovernance = read('src/frontend/project-time-web/src/role-workspace-governance.js');
const welcome = read('src/frontend/project-time-web/src/RoleWelcomeDashboard.jsx');
const app = read('src/frontend/project-time-web/src/App.jsx');
const main = read('src/frontend/project-time-web/src/main.jsx');
const identityCompatibility = read('src/frontend/project-time-web/src/role-workspace-effective-identity-compatibility.js');
const moduleAvailabilityBridge = read('src/frontend/project-time-web/src/module-availability-bridge.js');
const packageFile = read('src/frontend/project-time-web/package.json');
const crmCenter = read('src/frontend/project-time-web/src/CrmErpIntegrationCenter.jsx');
const crmPanel = read('src/frontend/project-time-web/src/CrmErpTokenPersistencePanel.jsx');
const entraPanel = read('src/frontend/project-time-web/src/EntraSecretExpirationGovernancePanel.jsx');

const pmBaseline = objectArrayBlock(roleGovernance, 'projectManagement', 'role workspace governance');
const accountingBaseline = objectArrayBlock(roleGovernance, 'accounting', 'role workspace governance');
const billingBaseline = objectArrayBlock(roleGovernance, 'billing', 'role workspace governance');
const pmActions = objectArrayBlock(welcome, 'projectManagement', 'role welcome dashboard');
const accountingActions = objectArrayBlock(welcome, 'accounting', 'role welcome dashboard');
const billingActions = objectArrayBlock(welcome, 'billing', 'role welcome dashboard');

requireIncludes(roleGovernance, [
  "const ADMIN_ROLES = new Set(['SUPER_ADMINISTRATOR', 'ADMINISTRATOR']);",
  "const BILLING_ROLES = new Set(['ACCOUNTING_BILLING', 'BILLING', 'FINANCE']);",
  "return 'Project Management';",
  "return 'Project Management Lead';",
  "return 'Accounting';",
  "return 'Billing';",
  'if (hasAny(roleCodes, ADMIN_ROLES)) return activeRegistryModules(moduleRegistry);',
  'const pureProjectManagement = projectManagement',
  'const pureAccounting = accounting',
  'const pureBilling = billing',
  'roleCodesFrom(value)',
  "String(source ?? '').split(/[;,|]+/)",
], 'role workspace governance');

requireIncludes(pmBaseline, [
  "'timesheet'",
  "'manager-approval'",
  "'holiday-admin'",
  "'project-allocation-info'",
  "'project-workload'",
  "'project-workspace'",
  "'project-intake'",
  "'customer-directory'",
  "'cost-alerts'",
  "'signed-handoff'",
  "'reporting'",
  "'project-closeout'",
  "'closeout-email'",
  "'work-register'",
  "'contracts'",
  "'project-flowhive'",
  "'qualifications-certifications'",
  "'user-guide'",
], 'Project Management module baseline');

requireIncludes(accountingBaseline, [
  "'workflow'",
  "'audit-history'",
  "'financial-operations-workbench'",
  "'billing-readiness'",
  "'invoice-billing-center'",
], 'Accounting module baseline');

requireIncludes(billingBaseline, [
  "'workflow'",
  "'customer-directory'",
  "'reporting'",
  "'financial-operations-workbench'",
  "'notification-delivery-monitor'",
  "'certify-integration'",
  "'billing-readiness'",
  "'project-closeout'",
  "'closeout-email'",
  "'invoice-billing-center'",
  "'contracts'",
  "'user-guide'",
], 'Billing module baseline');
requireExcludes(billingBaseline, ["'audit-history'"], 'Billing Module 008 exclusion');

requireIncludes(welcome, [
  'ROLE_WORKSPACE_WELCOME_IMPORT',
  'ROLE_WORKSPACE_TIME_ENTRY_EXCLUSIONS',
  '...PROJECT_MANAGEMENT_ROLES',
  "return 'projectManagement';",
  "return 'accounting';",
  "return 'billing';",
  'const showTimeEntry = clientShowTimeEntry && Boolean(timesheetHref);',
  'getRoleWorkspaceLabel(normalizedRoles)',
  'getRoleWorkspaceName(normalizedRoles)',
], 'role welcome dashboard');
requireIncludes(pmActions, [
  "['Add Time', 'timesheet']",
  "['Approval Center', 'manager-approval']",
  "['Project Expense Upload', 'project-allocation-info']",
  "['Project Workspace', 'project-workspace']",
  "['Qualifications & Certifications', 'qualifications-certifications']",
], 'Project Management dashboard actions');
requireIncludes(accountingActions, ["['Audit History', 'audit-history']"], 'Accounting dashboard actions');
requireExcludes(billingActions, ['audit-history', 'Audit History'], 'Billing dashboard actions');
requireIncludes(billingActions, [
  "['Financial Operations', 'financial-operations-workbench']",
  "['Billing Readiness', 'billing-readiness']",
  "['Invoice & Billing', 'invoice-billing-center']",
  "['Analytics Center', 'reporting']",
], 'Billing dashboard actions');
requireExcludes(welcome, [
  'Time entry is not part of this role.',
  "if (hasAny(roleCodes, ['ACCOUNTING', 'ACCOUNTING_BILLING', 'BILLING', 'FINANCE'])) return 'billing';",
], 'obsolete role assumptions');

requireIncludes(identityCompatibility, [
  '/api/utilization/current-quarter',
  'response.status !== 403',
  'not_applicable_for_effective_role',
  'grantsUtilizationAccess: false',
  'viewAsMutationAuthority: false',
], 'effective identity load isolation');
requireIncludes(main, [
  "import './role-workspace-effective-identity-compatibility.js';",
], 'effective identity startup wiring');
requireCount(main, "import './role-workspace-effective-identity-compatibility.js';", 1, 'effective identity startup wiring');

requireIncludes(app, [
  'ROLE_WORKSPACE_ENTRA_CRM_GOVERNANCE_IMPORTS',
  'ROLE_WORKSPACE_GOVERNANCE_VISIBLE_MODULES',
  'applyRoleWorkspaceGovernance(user, permissionFilteredModules, roleWorkspaceModules)',
  'ROLE_WORKSPACE_SIGNED_IN_USER',
  '<RoleWelcomeDashboard',
], 'App workspace governance');
requireCount(app, 'ROLE_WORKSPACE_GOVERNANCE_VISIBLE_MODULES', 1, 'App workspace governance');

requireIncludes(moduleAvailabilityBridge, [
  "const SUPER_ADMINISTRATOR_ROLE_CODES = new Set(['SUPER_ADMINISTRATOR', 'ADMINISTRATOR']);",
  'const actualSuperAdministrator = !viewAs',
  'permanentFullControl: actualSuperAdministrator',
  'permanentFullControl: Boolean(effectiveActor.permanentFullControl)',
], 'client navigation administrator invariant');

requireIncludes(crmCenter, [
  'MODULE 026 · CRM/ERP INTEGRATIONS',
  '/api/integrations/026/providers',
  'state.payload?.access?.canManage',
], 'Module 026 administration UI');
requireIncludes(crmPanel, [
  '/api/integrations/026/token-refresh/status',
  '/refresh-token',
  'access tokens are never displayed',
], 'Module 026 OAuth persistence UI');
requireIncludes(entraPanel, [
  '/api/entra-secret-expiration/profile',
  '/api/entra-secret-expiration/acknowledge',
  '/api/entra-secret-expiration/reminders/run',
  'secret value remains outside this browser',
], 'Module 065 expiration UI');

const permanentAuthority = optional('src/backend/ProjectTime.Api/Modules/ProjectPulseActualSessionAuthority.cs');
const permanentCompatibility = optional('src/backend/ProjectTime.Api/Modules/PermanentRoleAuthorityCompatibility.cs');
const moduleOverrides = optional('src/backend/ProjectTime.Api/Modules/ModuleAvailabilityOverridesModule.cs');
const roleAudit = optional('src/backend/ProjectTime.Api/Modules/RoleAccessAuditModule.cs');
const crmOAuth = optional('src/backend/ProjectTime.Api/Modules/CrmErpOAuthPersistence.cs');
const crmAdministration = optional('src/backend/ProjectTime.Api/Modules/CrmErpAdministrationExperience.cs');
const entraAdministration = optional('src/backend/ProjectTime.Api/Modules/EntraSecretAdministrationModule.cs');
const microsoftSecurity = optional('src/backend/ProjectTime.Api/Modules/MicrosoftIntegrationSecurityCompatibility.cs');
const projectFile = optional('src/backend/ProjectTime.Api/ProjectTime.Api.csproj');
const migration061 = optional('database/migrations/061_super_administrator_permanent_full_control.sql');
const rollback061 = optional('database/rollback/061_super_administrator_permanent_full_control_rollback.sql');
const test061 = optional('tests/test-super-administrator-permanent-full-control-migration-061.sh');
const migration062 = optional('database/migrations/062_project_management_billing_role_access_repair.sql');
const rollback062 = optional('database/rollback/062_project_management_billing_role_access_repair_rollback.sql');
const test062 = optional('tests/test-project-management-billing-role-access-migration-062.sh');

if (permanentAuthority) {
  requireIncludes(permanentAuthority, [
    '"SUPER_ADMINISTRATOR"',
    '"ADMINISTRATOR"',
    'ReadActualEmail(context)',
    'lower(app_user.email) = lower(@email)',
    'ProjectPulsePermanentFullControl',
    'actual_session_super_administrator',
    'if (IsViewAs(context)) return false;',
  ], 'permanent actual-session authority');
}

if (permanentCompatibility) {
  requireIncludes(permanentCompatibility, [
    'UsePermanentRoleAuthorityCompatibility',
    'ProjectPulseActualSessionAuthority.IsSuperAdministratorAsync',
    'IsAuditHistoryPath',
    'IsPureBillingEffectiveActorAsync',
    'billing_audit_history_not_authorized',
    'Billing does not have access to Audit History.',
  ], 'cross-module authority compatibility');
}

if (moduleOverrides) {
  requireIncludes(moduleOverrides, [
    'app.UsePermanentRoleAuthorityCompatibility();',
    'app.MapRoleAccessAuditEndpoints();',
    'app.MapQualificationsCertificationSelfServiceEndpoints();',
    'permanentFullControl',
    'actual_session_super_administrator',
    'missingOverrideBehavior = "ENABLED"',
  ], 'module availability and endpoint wiring');
}

if (roleAudit) {
  requireIncludes(roleAudit, [
    '/api/admin/role-access-audit',
    'SUPER_ADMIN_PERMISSION_COVERAGE',
    'PROJECT_MANAGEMENT_REQUIRED_PERMISSIONS',
    'BILLING_AUDIT_HISTORY_EXCLUSION',
    'SUPER_ADMIN_DYNAMIC_DENY_INVARIANT',
    'writeOperationsPerformed = false',
  ], 'role and module audit endpoint');
}

if (crmOAuth && crmAdministration) {
  requireIncludes(crmOAuth, [
    'ResolveManageAuthorityAsync',
    'actual_session_administrator_or_permission',
    'if (IsViewAs(context))',
  ], 'Module 026 actual-session authority');
  requireIncludes(crmAdministration, [
    'ResolveManageAuthorityPolicyFirstAsync',
    'requiredPermission = "MANAGE_INTEGRATIONS_026"',
  ], 'Module 026 policy diagnostics');
}

if (entraAdministration) {
  requireIncludes(entraAdministration, [
    'roles.Contains("SUPER_ADMINISTRATOR")',
    'roles.Contains("ADMINISTRATOR")',
    'permissions.Contains(DelegatedPermission)',
    'View-As cannot grant Entra credential-mutation authority.',
  ], 'Module 065 actual-session authorization');
}

if (microsoftSecurity) {
  requireIncludes(microsoftSecurity, [
    'roles.Contains("SUPER_ADMINISTRATOR") || roles.Contains("ADMINISTRATOR")',
    'Exit Administrator View-As before changing or testing Microsoft Integration credentials.',
  ], 'Microsoft Integration administrator authority');
}

if (projectFile) {
  requireIncludes(projectFile, [
    'app.MapModuleAvailabilityOverrideEndpoints();',
    'app.MapCrmErpOAuthPersistenceEndpoints();',
    'app.UseCrmErpOAuthPersistence();',
    'app.MapEntraSecretExpirationGovernanceEndpoints();',
  ], 'API registration');
}

if (migration061 && rollback061 && test061) {
  requireIncludes(migration061, [
    '061_super_administrator_permanent_full_control',
    "upper(role_code) IN ('SUPER_ADMINISTRATOR', 'ADMINISTRATOR')",
    'role_access_repair_061_assignment_changes',
    'role_access_repair_061_permission_changes',
    'missing_permission_relationships',
  ], 'migration 061');
  requireIncludes(rollback061, [
    'role_access_repair_061_assignment_changes',
    'role_access_repair_061_permission_changes',
    "migration_id = '061_super_administrator_permanent_full_control'",
  ], 'rollback 061');
  requireIncludes(test061, [
    'SUPER_ADMINISTRATOR_PERMANENT_FULL_CONTROL_MIGRATION_061=PASS',
    'migration_registered_once',
    'rollback_preserved_preexisting_permission',
  ], 'migration 061 test');
} else {
  console.log('ROLE_WORKSPACE_SUPER_ADMIN_MIGRATION_061_CHECK=SKIPPED_MINIMAL_WEB_CONTEXT');
}

if (migration062 && rollback062 && test062) {
  requireIncludes(migration062, [
    '062_project_management_billing_role_access_repair',
    'VIEW_QUALIFICATIONS_069',
    'MANAGE_OWN_QUALIFICATIONS_069',
    "'VIEW_TIME_ENTRY'",
    "'PROJECT_TIME_APPROVAL'",
    "'VIEW_HOLIDAYS'",
    "'MANAGE_EXPENSES'",
    "upper(role.role_code) IN ('BILLING', 'ACCOUNTING_BILLING', 'FINANCE')",
    "upper(COALESCE(permission.module_code, '')) = '008'",
  ], 'migration 062');
  requireIncludes(rollback062, [
    'role_access_repair_062_permission_grants',
    'role_access_repair_062_permission_removals',
    'rollback 062 restoration',
  ], 'rollback 062');
  requireIncludes(test062, [
    'PROJECT_MANAGEMENT_BILLING_ROLE_ACCESS_MIGRATION_062=PASS',
    'pm_required_permissions_complete',
    'billing_audit_removed',
    'accounting_audit_preserved',
    'pm_scope_restored',
  ], 'migration 062 test');
} else {
  console.log('ROLE_WORKSPACE_PM_BILLING_MIGRATION_062_CHECK=SKIPPED_MINIMAL_WEB_CONTEXT');
}

requireIncludes(packageFile, [
  'validate:role-workspace-entra-crm-governance',
  'validate-role-workspace-entra-crm-governance.mjs',
], 'frontend package integration');

console.log('ROLE_WORKSPACE_PROJECT_MANAGEMENT_TIME_ENTRY=PASS');
console.log('ROLE_WORKSPACE_PROJECT_MANAGEMENT_PROJECT_APPROVAL=PASS');
console.log('ROLE_WORKSPACE_PROJECT_MANAGEMENT_EXPENSE_HOLIDAY_QUALIFICATIONS=PASS');
console.log('ROLE_WORKSPACE_BILLING_AUDIT_HISTORY_EXCLUDED=PASS');
console.log('ROLE_WORKSPACE_ACCOUNTING_AUDIT_HISTORY_PRESERVED=PASS');
console.log('ROLE_WORKSPACE_SUPER_ADMIN_PERMANENT_FULL_CONTROL=PASS');
console.log('ROLE_WORKSPACE_VIEW_AS_WRITE_AUTHORITY=NONE');
console.log('ROLE_WORKSPACE_ENTRA_CRM_GOVERNANCE_VALIDATION=PASS');
