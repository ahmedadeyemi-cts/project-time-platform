const ADMIN_ROLES = new Set(['SUPER_ADMINISTRATOR', 'ADMINISTRATOR']);
const PROJECT_MANAGEMENT_ROLES = new Set([
  'PROJECT_MANAGER',
  'PROJECT_MANAGEMENT',
  'PROJECT_MANAGEMENT_LEAD',
  'PROJECT_MANAGEMENT_TEAM_LEAD',
  'PM_TEAM_LEAD'
]);
const ACCOUNTING_ROLES = new Set(['ACCOUNTING']);
const BILLING_ROLES = new Set(['ACCOUNTING_BILLING', 'BILLING', 'FINANCE']);
const SALES_ROLES = new Set(['SALES', 'ACCOUNT_EXECUTIVE', 'ACCOUNT_EXECUTIVES', 'SALES_MANAGER']);
const INSIDE_SALES_ROLES = new Set(['INSIDE_SALES', 'RESALE']);
const COORDINATOR_ROLES = new Set(['PROJECT_TEAM_COORDINATOR', 'PROJECT_COORDINATOR', 'PTC']);
const ENGINEERING_OR_OPERATIONS_ROLES = new Set([
  'ENGINEER',
  'ENGINEERING',
  'ENGINEERING_LEAD',
  'ENGINEERING_MANAGER',
  'ENGINEERING_TEAM_LEAD',
  'MANAGER',
  'PEOPLE_MANAGER',
  'PROJECT_TEAM_COORDINATOR',
  'PROJECT_COORDINATOR',
  'PTC',
  'SOLUTION_ARCHITECT',
  'ARCHITECT',
  'SA',
  'SAA'
]);

export const ROLE_WORKSPACE_BASELINES = Object.freeze({
  projectManagement: Object.freeze([
    'timesheet',
    'manager-approval',
    'holiday-admin',
    'project-allocation-info',
    'project-workload',
    'project-workspace',
    'project-intake',
    'customer-directory',
    'cost-alerts',
    'signed-handoff',
    'reporting',
    'project-closeout',
    'closeout-email',
    'work-register',
    'contracts',
    'project-forge',
    'project-flowhive',
    'qualifications-certifications',
    'user-guide'
  ]),
  accounting: Object.freeze([
    'workflow',
    'audit-history',
    'customer-directory',
    'reporting',
    'financial-operations-workbench',
    'notification-delivery-monitor',
    'certify-integration',
    'billing-readiness',
    'project-closeout',
    'closeout-email',
    'invoice-billing-center',
    'contracts',
    'user-guide'
  ]),
  billing: Object.freeze([
    'workflow',
    'customer-directory',
    'reporting',
    'financial-operations-workbench',
    'notification-delivery-monitor',
    'certify-integration',
    'billing-readiness',
    'project-closeout',
    'closeout-email',
    'invoice-billing-center',
    'contracts',
    'user-guide'
  ]),
  sales: Object.freeze([
    'project-intake',
    'sales-intake',
    'sow-generator',
    'crm-integration',
    'signed-handoff',
    'sales-insights',
    'customer-directory',
    'contracts',
    'opportunities',
    'reporting',
    'sales-coverage-alignment',
    'oem-vendor-directory',
    'user-guide'
  ]),
  coordinator: Object.freeze([
    'entra-secret-administration'
  ])
});

export const PROJECT_MANAGEMENT_DENIED_ROUTES = Object.freeze([
  'utilization',
  'ai-time-entry',
  'calendar-capacity',
  'capacity-pipeline-forecast',
  'oncall-scheduling'
]);

export const BUSINESS_ROLE_DENIED_ROUTES = Object.freeze([
  'timesheet',
  'manager-approval',
  'utilization',
  'holiday-admin',
  'project-allocation-info',
  'project-workload',
  'project-workspace',
  'work-task-builder',
  'ai-time-entry',
  'calendar-capacity',
  'project-flowhive',
  'qualifications-certifications',
  'capacity-pipeline-forecast',
  'oncall-scheduling'
]);

function normalizeRoleCode(value) {
  return String(value ?? '').trim().toUpperCase();
}

export function roleCodesFrom(value) {
  const source = Array.isArray(value)
    ? value
    : (value?.roles ?? value?.roleCodes ?? []);
  const entries = Array.isArray(source)
    ? source
    : String(source ?? '').split(/[;,|]+/);

  return entries
    .flatMap((item) => {
      const raw = item?.roleCode ?? item;
      return Array.isArray(raw) ? raw : String(raw ?? '').split(/[;,|]+/);
    })
    .map(normalizeRoleCode)
    .filter(Boolean)
    .filter((roleCode, index, values) => values.indexOf(roleCode) === index);
}

function hasAny(roleCodes, candidates) {
  return roleCodes.some((roleCode) => candidates.has(roleCode));
}

export function getRoleWorkspaceName(value) {
  const roleCodes = roleCodesFrom(value);
  if (hasAny(roleCodes, ADMIN_ROLES)) return 'Administrator';
  if (roleCodes.some((code) => ['PROJECT_MANAGEMENT_LEAD', 'PROJECT_MANAGEMENT_TEAM_LEAD', 'PM_TEAM_LEAD'].includes(code))) {
    return 'Project Management Lead';
  }
  if (hasAny(roleCodes, PROJECT_MANAGEMENT_ROLES)) return 'Project Management';
  if (hasAny(roleCodes, ACCOUNTING_ROLES)) return 'Accounting';
  if (hasAny(roleCodes, BILLING_ROLES)) return 'Billing';
  if (hasAny(roleCodes, INSIDE_SALES_ROLES)) return 'Inside Sales';
  if (hasAny(roleCodes, SALES_ROLES)) return 'Sales';
  if (hasAny(roleCodes, COORDINATOR_ROLES)) return 'Project Team Coordinator';
  if (roleCodes.includes('EXECUTIVE')) return 'Executive';
  if (roleCodes.some((code) => ['MANAGER', 'PEOPLE_MANAGER'].includes(code))) return 'Management';
  if (roleCodes.some((code) => ['ENGINEERING_LEAD', 'ENGINEERING_MANAGER', 'ENGINEERING_TEAM_LEAD'].includes(code))) {
    return 'Engineering Lead';
  }
  if (roleCodes.some((code) => ['SOLUTION_ARCHITECT', 'ARCHITECT', 'SA', 'SAA'].includes(code))) {
    return 'Solution Architecture';
  }
  if (roleCodes.some((code) => ['ENGINEER', 'ENGINEERING'].includes(code))) return 'Engineering';
  return roleCodes.length ? roleCodes.map((code) => code.replaceAll('_', ' ')).join(' + ') : 'Workspace';
}

export function getRoleWorkspaceLabel(value) {
  return `${getRoleWorkspaceName(value)} workspace`;
}

function canUseRegistryModule(module, assignedRoleCodes) {
  const strictRoleCodes = (module?.strictRoleCodes ?? []).map(normalizeRoleCode);
  return strictRoleCodes.length === 0 || strictRoleCodes.some((roleCode) => assignedRoleCodes.includes(roleCode));
}

function activeRegistryModules(moduleRegistry) {
  return (moduleRegistry ?? []).filter((module) => (
    module?.isRetired !== true && module?.lifecycle !== 'retired'
  ));
}

export function applyRoleWorkspaceGovernance(user, permissionFilteredModules, moduleRegistry) {
  const roleCodes = roleCodesFrom(user);
  if (roleCodes.length === 0) return permissionFilteredModules ?? [];

  // An actual Super Administrator has permanent organization-wide Full Control.
  // When Administrator View-As is active, the effective user's non-admin roles are
  // supplied here instead, so this full-catalog branch never transfers authority.
  if (hasAny(roleCodes, ADMIN_ROLES)) return activeRegistryModules(moduleRegistry);

  const routes = new Set((permissionFilteredModules ?? []).map((module) => module.route));
  const projectManagement = hasAny(roleCodes, PROJECT_MANAGEMENT_ROLES);
  const accounting = hasAny(roleCodes, ACCOUNTING_ROLES);
  const billing = hasAny(roleCodes, BILLING_ROLES);
  const accountingBilling = accounting || billing;
  const sales = hasAny(roleCodes, SALES_ROLES) || hasAny(roleCodes, INSIDE_SALES_ROLES);
  const coordinator = hasAny(roleCodes, COORDINATOR_ROLES);

  const baselineRoutes = new Set();
  if (projectManagement) ROLE_WORKSPACE_BASELINES.projectManagement.forEach((route) => baselineRoutes.add(route));
  if (accounting) ROLE_WORKSPACE_BASELINES.accounting.forEach((route) => baselineRoutes.add(route));
  if (billing) ROLE_WORKSPACE_BASELINES.billing.forEach((route) => baselineRoutes.add(route));
  if (sales) ROLE_WORKSPACE_BASELINES.sales.forEach((route) => baselineRoutes.add(route));
  if (coordinator) ROLE_WORKSPACE_BASELINES.coordinator.forEach((route) => baselineRoutes.add(route));

  for (const module of moduleRegistry ?? []) {
    const coordinatorAcknowledgementModule = coordinator && module.route === 'entra-secret-administration';
    if (baselineRoutes.has(module.route)
        && (coordinatorAcknowledgementModule || canUseRegistryModule(module, roleCodes))) {
      routes.add(module.route);
    }
  }

  const hasOperationalRole = hasAny(roleCodes, ENGINEERING_OR_OPERATIONS_ROLES);
  const pureProjectManagement = projectManagement
    && !hasOperationalRole
    && !accountingBilling
    && !sales
    && !coordinator;
  const pureAccounting = accounting
    && !billing
    && !hasOperationalRole
    && !projectManagement
    && !sales
    && !coordinator;
  const pureBilling = billing
    && !accounting
    && !hasOperationalRole
    && !projectManagement
    && !sales
    && !coordinator;

  if (pureProjectManagement || pureAccounting || pureBilling) {
    return activeRegistryModules(moduleRegistry).filter((module) => (
      baselineRoutes.has(module.route) && routes.has(module.route)
    ));
  }

  const denied = projectManagement && !hasOperationalRole
    ? PROJECT_MANAGEMENT_DENIED_ROUTES
    : (accountingBilling || sales) && !hasOperationalRole && !projectManagement
      ? BUSINESS_ROLE_DENIED_ROUTES
      : [];

  denied.forEach((route) => routes.delete(route));
  return activeRegistryModules(moduleRegistry).filter((module) => routes.has(module.route));
}
