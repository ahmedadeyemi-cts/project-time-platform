import assert from 'node:assert/strict';
import {
  applyRoleWorkspaceGovernance,
  getRoleWorkspaceName,
  roleCodesFrom
} from '../src/role-workspace-governance.js';

const moduleRegistry = [
  ['timesheet', '001'],
  ['manager-approval', '002'],
  ['utilization', '003'],
  ['holiday-admin', '004'],
  ['project-allocation-info', '005'],
  ['workflow', '007'],
  ['audit-history', '008'],
  ['project-workload', '018'],
  ['project-workspace', '019'],
  ['project-intake', '020'],
  ['customer-directory', '021'],
  ['cost-alerts', '022'],
  ['sales-intake', '024'],
  ['sow-generator', '025'],
  ['crm-integration', '026'],
  ['signed-handoff', '027'],
  ['reporting', '030'],
  ['financial-operations-workbench', '031'],
  ['notification-delivery-monitor', '032'],
  ['sales-insights', '036'],
  ['certify-integration', '038'],
  ['billing-readiness', '039'],
  ['project-closeout', '040'],
  ['closeout-email', '041'],
  ['invoice-billing-center', '042'],
  ['work-register', '055C'],
  ['create-work-register', '055D'],
  ['calendar-capacity', '057'],
  ['contracts', '060'],
  ['opportunities', '063'],
  ['ai-provider-configuration', '064'],
  ['entra-secret-administration', '065'],
  ['project-flowhive', '066'],
  ['qualifications-certifications', '069'],
  ['capacity-pipeline-forecast', '070'],
  ['oncall-scheduling', '071'],
  ['sales-coverage-alignment', '073'],
  ['oem-vendor-directory', '074'],
  ['user-guide', '999'],
  ['retired-example', 'R01', true]
].map(([route, moduleNumber, isRetired = false]) => ({
  route,
  moduleNumber,
  href: `#${route}`,
  title: route,
  permissions: [],
  roleCodes: [],
  strictRoleCodes: [],
  isRetired
}));

const byRoute = new Map(moduleRegistry.map((module) => [module.route, module]));
const modules = (...routes) => routes.map((route) => {
  const module = byRoute.get(route);
  assert.ok(module, `Test registry is missing ${route}.`);
  return module;
});
const routesOf = (items) => new Set(items.map((item) => item.route));
const requireRoutes = (actual, expected, label) => {
  const routes = routesOf(actual);
  for (const route of expected) {
    assert.ok(routes.has(route), `${label} is missing ${route}.`);
  }
};
const rejectRoutes = (actual, expected, label) => {
  const routes = routesOf(actual);
  for (const route of expected) {
    assert.ok(!routes.has(route), `${label} must not include ${route}.`);
  }
};
const governed = (roleValue, permissionRoutes = []) =>
  applyRoleWorkspaceGovernance(
    roleValue,
    modules(...permissionRoutes),
    moduleRegistry
  );

// Role shape and alias normalization used by actual sessions and View-As records.
assert.deepEqual(
  roleCodesFrom({ roleCodes: 'PROJECT_MANAGEMENT;BILLING|FINANCE' }),
  ['PROJECT_MANAGEMENT', 'BILLING', 'FINANCE']
);
assert.deepEqual(
  roleCodesFrom({ roles: [{ roleCode: 'ENGINEERING' }, 'MANAGER'] }),
  ['ENGINEERING', 'MANAGER']
);
assert.equal(getRoleWorkspaceName({ roleCodes: ['PROJECT_MANAGER'] }), 'Project Management');
assert.equal(getRoleWorkspaceName({ roleCodes: ['ACCOUNTING'] }), 'Accounting');
assert.equal(getRoleWorkspaceName({ roleCodes: ['BILLING'] }), 'Billing');

// Actual administrator: complete active catalog, with retired modules excluded.
for (const roleCode of ['SUPER_ADMINISTRATOR', 'ADMINISTRATOR']) {
  const actual = governed({ roleCodes: [roleCode] });
  assert.equal(actual.length, moduleRegistry.filter((module) => !module.isRetired).length);
  rejectRoutes(actual, ['retired-example'], `${roleCode} active catalog`);
  requireRoutes(actual, [
    'crm-integration',
    'entra-secret-administration',
    'ai-provider-configuration',
    'audit-history',
    'qualifications-certifications'
  ], `${roleCode} permanent Full Control`);
}

// Project Management aliases: hybrid self-service plus assigned-project operations.
for (const roleCode of [
  'PROJECT_MANAGER',
  'PROJECT_MANAGEMENT',
  'PROJECT_MANAGEMENT_LEAD',
  'PROJECT_MANAGEMENT_TEAM_LEAD',
  'PM_TEAM_LEAD'
]) {
  const actual = governed({ roleCodes: [roleCode] });
  requireRoutes(actual, [
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
    'project-flowhive',
    'qualifications-certifications',
    'user-guide'
  ], `${roleCode} workspace`);
  rejectRoutes(actual, [
    'utilization',
    'ai-provider-configuration',
    'entra-secret-administration',
    'capacity-pipeline-forecast',
    'oncall-scheduling'
  ], `${roleCode} least privilege`);
}

// Accounting retains Audit History; Billing aliases explicitly do not.
const accounting = governed({ roleCodes: ['ACCOUNTING'] });
requireRoutes(accounting, [
  'workflow',
  'audit-history',
  'financial-operations-workbench',
  'billing-readiness',
  'invoice-billing-center'
], 'Accounting workspace');

for (const roleCode of ['BILLING', 'ACCOUNTING_BILLING', 'FINANCE']) {
  const actual = governed({ roleCodes: [roleCode] });
  requireRoutes(actual, [
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
  ], `${roleCode} workspace`);
  rejectRoutes(actual, [
    'audit-history',
    'timesheet',
    'manager-approval',
    'utilization',
    'ai-provider-configuration',
    'entra-secret-administration'
  ], `${roleCode} least privilege`);
}

// Sales families receive only their business baseline unless another role grants more.
for (const roleCode of [
  'SALES',
  'ACCOUNT_EXECUTIVE',
  'ACCOUNT_EXECUTIVES',
  'SALES_MANAGER',
  'INSIDE_SALES',
  'RESALE'
]) {
  const actual = governed({ roleCodes: [roleCode] });
  requireRoutes(actual, [
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
  ], `${roleCode} workspace`);
  rejectRoutes(actual, [
    'timesheet',
    'manager-approval',
    'audit-history',
    'entra-secret-administration'
  ], `${roleCode} least privilege`);
}

// Roles driven by ordinary permission evidence retain that evidence and do not
// become empty workspaces merely because they lack a special business baseline.
const ordinaryRoleCases = [
  ['ENGINEERING', ['timesheet', 'utilization', 'holiday-admin', 'project-workspace', 'qualifications-certifications']],
  ['ENGINEERING_LEAD', ['timesheet', 'manager-approval', 'utilization', 'project-workspace', 'qualifications-certifications']],
  ['MANAGER', ['manager-approval', 'utilization', 'project-workload', 'reporting']],
  ['SOLUTION_ARCHITECT', ['timesheet', 'holiday-admin', 'project-intake', 'project-workspace', 'sow-generator', 'reporting']],
  ['EXECUTIVE', ['reporting', 'project-workload', 'billing-readiness']],
  ['PROJECT_TEAM_COORDINATOR', ['manager-approval', 'workflow', 'project-workspace', 'billing-readiness', 'invoice-billing-center']]
];
for (const [roleCode, permissionRoutes] of ordinaryRoleCases) {
  const actual = governed({ roleCodes: [roleCode] }, permissionRoutes);
  requireRoutes(actual, permissionRoutes, `${roleCode} permission-driven workspace`);
  assert.ok(actual.length > 0, `${roleCode} must not receive an empty workspace.`);
}

// PTC receives Module 065 acknowledgement visibility but not administrator
// configuration authority. Endpoint authorization remains authoritative.
const ptc = governed({ roleCodes: ['PROJECT_TEAM_COORDINATOR'] }, ['manager-approval', 'workflow']);
requireRoutes(ptc, ['manager-approval', 'workflow', 'entra-secret-administration'], 'PTC workspace');
rejectRoutes(ptc, ['ai-provider-configuration', 'audit-history'], 'PTC administrator boundary');

// Administrator View-As is modeled by passing only the selected effective role.
// The underlying administrator role is deliberately absent and cannot leak into
// the selected user’s module result.
const viewAsBilling = governed({ roleCodes: ['BILLING'] });
rejectRoutes(viewAsBilling, [
  'audit-history',
  'crm-integration',
  'entra-secret-administration',
  'ai-provider-configuration'
], 'Administrator View-As Billing');
const viewAsProjectManager = governed({ roleCodes: 'PROJECT_MANAGEMENT' });
requireRoutes(viewAsProjectManager, ['timesheet', 'manager-approval', 'project-workspace'], 'Administrator View-As Project Management');
rejectRoutes(viewAsProjectManager, ['entra-secret-administration', 'ai-provider-configuration'], 'Administrator View-As Project Management');

console.log('ROLE_WORKSPACE_ACCESS_MATRIX=PASS');
console.log('ROLE_WORKSPACE_CANONICAL_ROLES_TESTED=SUPER_ADMINISTRATOR,ADMINISTRATOR,PROJECT_MANAGEMENT,ACCOUNTING,BILLING,SALES,INSIDE_SALES,ENGINEERING,ENGINEERING_LEAD,MANAGER,SOLUTION_ARCHITECT,EXECUTIVE,PROJECT_TEAM_COORDINATOR');
console.log('ROLE_WORKSPACE_VIEW_AS_ADMIN_AUTHORITY_TRANSFER=NONE');
console.log('ROLE_WORKSPACE_BILLING_MODULE_008=DENIED');
