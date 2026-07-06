import { useEffect, useMemo, useState } from 'react';
import './role-admin-directory-panel.css';

function getProjectPulseAuthHeaders() {
  try {
    const rawSession = window.localStorage.getItem('projectPulseAuthSession');
    if (!rawSession) return {};
    const session = JSON.parse(rawSession);
    return session?.sessionToken ? { 'X-ProjectPulse-Session': session.sessionToken } : {};
  } catch {
    return {};
  }
}

async function readApiErrorMessage(response, path) {
  const raw = await response.text();
  if (!raw) return `${path} returned HTTP ${response.status}`;

  try {
    const parsed = JSON.parse(raw);
    return `${path} returned HTTP ${response.status}: ${parsed.message || parsed.detail || parsed.status || raw}`;
  } catch {
    return `${path} returned HTTP ${response.status}: ${raw}`;
  }
}

async function fetchJson(path) {
  const response = await fetch(path, { headers: getProjectPulseAuthHeaders() });

  if (response.status === 403) {
    return { canViewRoleDirectory: false };
  }

  if (!response.ok) throw new Error(await readApiErrorMessage(response, path));
  return response.json();
}

function formatModuleLabel(value) {
  return String(value || 'Unassigned')
    .replaceAll('_', ' ')
    .replace(/\b\w/g, (letter) => letter.toUpperCase());
}


/* 042D_ROLE_ENFORCEMENT_VALIDATION_MATRIX_START */
const ROLE_VALIDATION_COLUMNS = [
  { roleCode: 'SUPER_ADMINISTRATOR', label: 'Admin', aliases: ['SUPER_ADMINISTRATOR', 'ADMINISTRATOR'] },
  { roleCode: 'PROJECT_TEAM_COORDINATOR', label: 'PTC', aliases: ['PROJECT_TEAM_COORDINATOR'] },
  { roleCode: 'PROJECT_MANAGEMENT', label: 'PM', aliases: ['PROJECT_MANAGEMENT', 'PROJECT_MANAGER', 'PROJECT_MANAGEMENT_LEAD', 'PM_TEAM_LEAD', 'PROJECT_MANAGEMENT_TEAM_LEAD'] },
  { roleCode: 'MANAGER', label: 'Manager', aliases: ['MANAGER', 'ENGINEERING_LEAD', 'ENGINEERING_TEAM_LEAD'] },
  { roleCode: 'ENGINEERING', label: 'Engineer', aliases: ['ENGINEERING', 'ENGINEER'] },
  { roleCode: 'EXECUTIVE', label: 'Executive', aliases: ['EXECUTIVE'] },
  { roleCode: 'ACCOUNTING', label: 'Accounting', aliases: ['ACCOUNTING', 'BILLING', 'FINANCE'] }
];

const ROLE_VALIDATION_ROUTE_PRIORITY = [
  '/api/security/effective-session',
  '/api/security/role-enforcement-smoke',
  '/api/security/role-access-matrix',
  '/api/security/route-permission-contracts',
  '/api/admin/user-admin/users',
  '/api/admin/user-admin/users/roles',
  '/api/workflow/approval-items',
  '/api/workflow/operations-center',
  '/api/time-exports',
  '/api/export-packages/readiness-summary',
  '/api/audit-history/summary',
  '/api/project-closeout/email/send'
];

function normalizeStringArray(value) {
  if (!value) return [];

  if (Array.isArray(value)) {
    return value
      .map((item) => String(item ?? '').trim())
      .filter(Boolean);
  }

  if (typeof value === 'string') {
    return value
      .split(',')
      .map((item) => item.trim())
      .filter(Boolean);
  }

  return [];
}

function normalizeContractRows(payload) {
  const contracts = payload?.contracts ?? payload?.items ?? payload?.routes ?? [];
  if (!Array.isArray(contracts)) return [];

  return contracts.map((contract, index) => {
    const routePath = contract.routePath
      ?? contract.path
      ?? contract.endpoint
      ?? contract.route
      ?? contract.route_key
      ?? contract.routeKey
      ?? `contract-${index + 1}`;

    const routeKey = contract.routeKey
      ?? contract.route_key
      ?? contract.moduleKey
      ?? contract.module
      ?? routePath;

    return {
      routeKey: String(routeKey || routePath),
      routePath: String(routePath || routeKey),
      method: String(contract.method ?? contract.httpMethod ?? contract.routeMethod ?? contract.route_method ?? 'GET').toUpperCase(),
      contractStatus: String(contract.contractStatus ?? contract.contract_status ?? contract.status ?? 'active'),
      requiredPermissions: normalizeStringArray(contract.requiredPermissions ?? contract.required_permissions),
      allowedRoles: normalizeStringArray(contract.allowedRoles ?? contract.allowed_roles),
      restrictedRoles: normalizeStringArray(contract.restrictedRoles ?? contract.restricted_roles)
    };
  });
}

function normalizeRoleAccessRows(payload) {
  const rows = payload?.matrix ?? payload?.roles ?? payload?.roleCoverage ?? [];
  if (!Array.isArray(rows)) return [];

  return rows.map((role) => ({
    roleCode: String(role.roleCode ?? role.role_code ?? '').trim().toUpperCase(),
    roleName: String(role.roleName ?? role.role_name ?? role.roleCode ?? role.role_code ?? '').trim(),
    permissions: normalizeStringArray(role.permissions ?? role.permissionCodes ?? role.permission_codes)
  })).filter((role) => role.roleCode);
}

function buildPermissionSetByRole(roleRows) {
  const byRole = new Map();

  roleRows.forEach((role) => {
    const permissionSet = new Set(role.permissions.map((permission) => permission.toUpperCase()));
    byRole.set(role.roleCode.toUpperCase(), {
      roleCode: role.roleCode,
      roleName: role.roleName,
      permissions: permissionSet
    });
  });

  return byRole;
}

function roleAliasSet(column) {
  return new Set([column.roleCode, ...(column.aliases ?? [])].map((role) => String(role ?? '').toUpperCase()));
}

function roleHasAnyPermission(roleAccess, permissionCodes) {
  const required = normalizeStringArray(permissionCodes).map((permission) => permission.toUpperCase());
  if (required.length === 0) return false;
  return required.some((permission) => roleAccess?.permissions?.has(permission));
}

function contractMentionsRole(roleCodes, roleList) {
  const allowed = new Set(normalizeStringArray(roleList).map((role) => role.toUpperCase()));
  return [...roleCodes].some((roleCode) => allowed.has(roleCode));
}

function evaluateRoleRouteAccess(contract, column, permissionByRole) {
  const aliases = roleAliasSet(column);
  const primaryRole = String(column.roleCode).toUpperCase();
  const roleAccess = permissionByRole.get(primaryRole) ?? [...aliases].map((alias) => permissionByRole.get(alias)).find(Boolean);

  if (contractMentionsRole(aliases, contract.restrictedRoles)) {
    return {
      status: 'blocked',
      label: 'Blocked',
      reason: 'Role is explicitly restricted by route contract.'
    };
  }

  if (contractMentionsRole(aliases, contract.allowedRoles)) {
    return {
      status: 'allowed',
      label: 'Allowed',
      reason: 'Role is explicitly allowed by route contract.'
    };
  }

  if (roleHasAnyPermission(roleAccess, contract.requiredPermissions)) {
    return {
      status: 'allowed',
      label: 'Allowed',
      reason: 'Role has at least one required permission.'
    };
  }

  if (contract.requiredPermissions.length > 0 || contract.allowedRoles.length > 0) {
    return {
      status: 'blocked',
      label: 'Blocked',
      reason: 'Role lacks required route permission or allowed-role mapping.'
    };
  }

  return {
    status: 'review',
    label: 'Review',
    reason: 'Route contract does not declare allowed roles or required permissions.'
  };
}

function routePriorityScore(contract) {
  const path = String(contract.routePath ?? '');
  const exactIndex = ROLE_VALIDATION_ROUTE_PRIORITY.findIndex((route) => path === route);
  if (exactIndex >= 0) return exactIndex;

  const partialIndex = ROLE_VALIDATION_ROUTE_PRIORITY.findIndex((route) => path.includes(route) || route.includes(path));
  if (partialIndex >= 0) return partialIndex + 100;

  return 1000;
}

function buildRoleValidationMatrix(payload) {
  const contracts = normalizeContractRows(payload?.contractsPayload);
  const roleRows = normalizeRoleAccessRows(payload?.roleAccessPayload);
  const permissionByRole = buildPermissionSetByRole(roleRows);

  const activeContracts = contracts
    .filter((contract) => !contract.contractStatus || contract.contractStatus.toLowerCase() === 'active')
    .sort((a, b) => routePriorityScore(a) - routePriorityScore(b) || String(a.routePath).localeCompare(String(b.routePath)));

  const focusedContracts = [
    ...activeContracts.filter((contract) => routePriorityScore(contract) < 1000),
    ...activeContracts.filter((contract) => routePriorityScore(contract) >= 1000).slice(0, 10)
  ];

  const rows = focusedContracts.map((contract) => ({
    ...contract,
    verdicts: ROLE_VALIDATION_COLUMNS.map((column) => ({
      roleCode: column.roleCode,
      label: column.label,
      ...evaluateRoleRouteAccess(contract, column, permissionByRole)
    }))
  }));

  const allowedCount = rows.reduce((total, row) => total + row.verdicts.filter((verdict) => verdict.status === 'allowed').length, 0);
  const blockedCount = rows.reduce((total, row) => total + row.verdicts.filter((verdict) => verdict.status === 'blocked').length, 0);
  const reviewCount = rows.reduce((total, row) => total + row.verdicts.filter((verdict) => verdict.status === 'review').length, 0);

  return {
    rows,
    summary: {
      contractCount: contracts.length,
      activeContractCount: activeContracts.length,
      roleCount: roleRows.length,
      allowedCount,
      blockedCount,
      reviewCount,
      smokeCheckCount: Array.isArray(payload?.smokePayload?.checks) ? payload.smokePayload.checks.length : 0
    }
  };
}
/* 042D_ROLE_ENFORCEMENT_VALIDATION_MATRIX_END */

/* 042G_ROLE_CAPABILITY_MATRIX_START */
const ROLE_CAPABILITY_COLUMNS = [
  { roleCode: 'ENGINEERING', label: 'Engineer', aliases: ['ENGINEERING', 'ENGINEER'] },
  { roleCode: 'MANAGER', label: 'Manager', aliases: ['MANAGER', 'ENGINEERING_LEAD', 'ENGINEERING_TEAM_LEAD'] },
  { roleCode: 'PROJECT_MANAGEMENT', label: 'PM', aliases: ['PROJECT_MANAGEMENT', 'PROJECT_MANAGER', 'PROJECT_MANAGEMENT_LEAD', 'PM_TEAM_LEAD', 'PROJECT_MANAGEMENT_TEAM_LEAD'] },
  { roleCode: 'PROJECT_TEAM_COORDINATOR', label: 'PTC', aliases: ['PROJECT_TEAM_COORDINATOR'] },
  { roleCode: 'ACCOUNTING', label: 'Accounting', aliases: ['ACCOUNTING', 'BILLING', 'FINANCE'] },
  { roleCode: 'EXECUTIVE', label: 'Executive', aliases: ['EXECUTIVE'] },
  { roleCode: 'SUPER_ADMINISTRATOR', label: 'Admin', aliases: ['SUPER_ADMINISTRATOR', 'ADMINISTRATOR'] }
];

const ROLE_CAPABILITY_DEFINITIONS = [
  {
    key: 'dashboard',
    area: 'Workspace',
    capability: 'View dashboard',
    engineerIntent: 'Engineers should be able to view the dashboard.',
    expectedRoles: ['ENGINEERING', 'MANAGER', 'PROJECT_MANAGEMENT', 'PROJECT_TEAM_COORDINATOR', 'ACCOUNTING', 'EXECUTIVE', 'SUPER_ADMINISTRATOR'],
    requiredPermissions: [],
    acceptablePermissions: ['VIEW_TIME_ENTRY', 'VIEW_PROJECT_WORKSPACE', 'VIEW_OWN_UTILIZATION', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
    note: 'Dashboard visibility is baseline workspace access. It may not have a dedicated permission code.'
  },
  {
    key: 'time-entry',
    area: 'Time',
    capability: 'Enter and submit own time',
    engineerIntent: 'Engineers must be able to put in their own time.',
    expectedRoles: ['ENGINEERING', 'MANAGER', 'PROJECT_MANAGEMENT', 'PROJECT_TEAM_COORDINATOR', 'SUPER_ADMINISTRATOR'],
    requiredPermissions: ['VIEW_TIME_ENTRY'],
    acceptablePermissions: ['EDIT_OWN_TIME', 'SUBMIT_TIME', 'MANAGE_ALL', 'SYSTEM_ADMINISTRATION'],
    note: 'Engineer should have time-entry visibility and own-time edit/submit capability.'
  },
  {
    key: 'assigned-projects',
    area: 'Projects',
    capability: 'See projects assigned to them',
    engineerIntent: 'Engineers should only see projects assigned to them or scoped to their engineering work.',
    expectedRoles: ['ENGINEERING', 'MANAGER', 'PROJECT_MANAGEMENT', 'PROJECT_TEAM_COORDINATOR', 'SUPER_ADMINISTRATOR'],
    requiredPermissions: ['VIEW_PROJECT_WORKSPACE'],
    acceptablePermissions: ['VIEW_ENGINEERING_PROJECT_DOCUMENTS', 'VIEW_PROJECT_INTAKE', 'MANAGE_ALL', 'SYSTEM_ADMINISTRATION'],
    note: 'Engineer access should be scoped to assigned project workspace content.'
  },
  {
    key: 'project-documents',
    area: 'Projects',
    capability: 'View assigned project documents',
    engineerIntent: 'Engineers should view project documents only when assigned to the project.',
    expectedRoles: ['ENGINEERING', 'MANAGER', 'PROJECT_MANAGEMENT', 'PROJECT_TEAM_COORDINATOR', 'SUPER_ADMINISTRATOR'],
    requiredPermissions: ['VIEW_ENGINEERING_PROJECT_DOCUMENTS'],
    acceptablePermissions: ['VIEW_PROJECT_WORKSPACE', 'VIEW_PROJECT_ALLOCATION_INFO', 'MANAGE_ALL', 'SYSTEM_ADMINISTRATION'],
    note: 'Document access should stay assignment-scoped.'
  },
  {
    key: 'calendar-read-only',
    area: 'Calendar',
    capability: 'View calendar without modifying it',
    engineerIntent: 'Engineers should see the calendar but should not be able to modify calendar settings.',
    expectedRoles: ['ENGINEERING', 'MANAGER', 'PROJECT_MANAGEMENT', 'PROJECT_TEAM_COORDINATOR', 'EXECUTIVE', 'SUPER_ADMINISTRATOR'],
    requiredPermissions: ['VIEW_HOLIDAYS'],
    acceptablePermissions: ['VIEW_CALENDAR', 'VIEW_RESOURCE_SCHEDULING', 'MANAGE_ALL', 'SYSTEM_ADMINISTRATION'],
    restrictedPermissions: ['MANAGE_HOLIDAYS', 'MANAGE_CALENDAR', 'MANAGE_RESOURCE_SCHEDULING'],
    note: 'Engineer should be read-only for calendar-style views.'
  },
  {
    key: 'calendar-sync',
    area: 'Calendar',
    capability: 'Sync calendar',
    engineerIntent: 'Engineers should be able to sync their calendar.',
    expectedRoles: ['ENGINEERING', 'MANAGER', 'PROJECT_MANAGEMENT', 'PROJECT_TEAM_COORDINATOR', 'SUPER_ADMINISTRATOR'],
    requiredPermissions: ['SYNC_CALENDAR'],
    acceptablePermissions: ['SYNC_OWN_CALENDAR', 'MANAGE_CALENDAR_SYNC', 'MANAGE_ALL', 'SYSTEM_ADMINISTRATION'],
    note: 'If this shows Gap, the permission or backend sync route still needs to be created or mapped.'
  },
  {
    key: 'ai-time-entry',
    area: 'AI / Time',
    capability: 'Use AI with time entry',
    engineerIntent: 'Engineers should be able to use AI assistance while entering time.',
    expectedRoles: ['ENGINEERING', 'MANAGER', 'PROJECT_MANAGEMENT', 'PROJECT_TEAM_COORDINATOR', 'SUPER_ADMINISTRATOR'],
    requiredPermissions: ['USE_TIME_ENTRY_AI'],
    acceptablePermissions: ['USE_AI_TIME_ENTRY', 'AI_TIME_ENTRY_ASSIST', 'MANAGE_ALL', 'SYSTEM_ADMINISTRATION'],
    note: 'If this shows Gap, the AI time-entry permission or route contract still needs mapping.'
  },
  {
    key: 'own-utilization',
    area: 'Utilization',
    capability: 'View own utilization',
    engineerIntent: 'Engineers should see utilization for themselves only.',
    expectedRoles: ['ENGINEERING', 'MANAGER', 'PROJECT_MANAGEMENT', 'PROJECT_TEAM_COORDINATOR', 'EXECUTIVE', 'SUPER_ADMINISTRATOR'],
    requiredPermissions: ['VIEW_OWN_UTILIZATION'],
    acceptablePermissions: ['VIEW_INDIVIDUAL_UTILIZATION', 'VIEW_UTILIZATION', 'MANAGE_ALL', 'SYSTEM_ADMINISTRATION'],
    restrictedPermissions: ['VIEW_TEAM_UTILIZATION'],
    note: 'Engineer should not automatically receive team-wide utilization unless also a lead/manager.'
  },
  {
    key: 'expense-upload',
    area: 'Expenses',
    capability: 'Upload expenses via CSV or Excel',
    engineerIntent: 'Engineers should be able to upload their expense entries from CSV or Excel.',
    expectedRoles: ['ENGINEERING', 'MANAGER', 'PROJECT_MANAGEMENT', 'PROJECT_TEAM_COORDINATOR', 'ACCOUNTING', 'SUPER_ADMINISTRATOR'],
    requiredPermissions: ['UPLOAD_EXPENSES'],
    acceptablePermissions: ['IMPORT_EXPENSES_CSV', 'IMPORT_EXPENSES_EXCEL', 'MANAGE_EXPENSES', 'MANAGE_ALL', 'SYSTEM_ADMINISTRATION'],
    note: 'If this shows Gap, the expense upload permission or import route still needs mapping.'
  }
];

function normalizeCapabilityPermission(value) {
  return String(value ?? '').trim().toUpperCase();
}

function collectRolePermissionCodes(role) {
  const codes = new Set();

  (role?.permissionsByModule ?? []).forEach((module) => {
    (module?.permissions ?? []).forEach((permission) => {
      const code = normalizeCapabilityPermission(permission.permissionCode ?? permission.permission_code ?? permission);
      if (code) codes.add(code);
    });
  });

  return codes;
}

function roleMatchesCapabilityColumn(role, column) {
  const roleCode = normalizeCapabilityPermission(role?.roleCode ?? role?.role_code);
  const aliases = new Set([column.roleCode, ...(column.aliases ?? [])].map(normalizeCapabilityPermission));
  return aliases.has(roleCode);
}

function buildRolePermissionIndex(roles) {
  const index = new Map();

  ROLE_CAPABILITY_COLUMNS.forEach((column) => {
    const permissionSet = new Set();

    (roles ?? [])
      .filter((role) => roleMatchesCapabilityColumn(role, column))
      .forEach((role) => {
        collectRolePermissionCodes(role).forEach((permission) => permissionSet.add(permission));
      });

    index.set(column.roleCode, permissionSet);
  });

  return index;
}

function roleIsExpectedForCapability(capability, column) {
  const expected = new Set((capability.expectedRoles ?? []).map(normalizeCapabilityPermission));
  const aliases = new Set([column.roleCode, ...(column.aliases ?? [])].map(normalizeCapabilityPermission));
  return [...aliases].some((roleCode) => expected.has(roleCode));
}

function permissionSetHasAny(permissionSet, permissions) {
  return normalizeStringArray(permissions).some((permission) => permissionSet.has(normalizeCapabilityPermission(permission)));
}

function evaluateRoleCapability(capability, column, permissionIndex) {
  const expected = roleIsExpectedForCapability(capability, column);
  const permissions = permissionIndex.get(column.roleCode) ?? new Set();
  const required = normalizeStringArray(capability.requiredPermissions);
  const acceptable = normalizeStringArray(capability.acceptablePermissions);
  const restricted = normalizeStringArray(capability.restrictedPermissions);
  const hasRequired = required.length === 0 || permissionSetHasAny(permissions, required);
  const hasAcceptable = acceptable.length === 0 || permissionSetHasAny(permissions, acceptable);
  const hasRestricted = permissionSetHasAny(permissions, restricted);
  const hasAdminOverride = permissionSetHasAny(permissions, ['MANAGE_ALL', 'SYSTEM_ADMINISTRATION']);

  if (!expected) {
    return {
      status: 'not-expected',
      label: 'Not expected',
      detail: 'This role is not intended for this capability.'
    };
  }

  if (hasRestricted && !hasAdminOverride) {
    return {
      status: 'overscoped',
      label: 'Over-scoped',
      detail: 'Role has a permission that should stay restricted for this capability.'
    };
  }

  if (required.length === 0 && (hasAcceptable || acceptable.length === 0)) {
    return {
      status: 'expected',
      label: 'Expected',
      detail: capability.note || 'Baseline role behavior.'
    };
  }

  if (hasRequired || hasAcceptable || hasAdminOverride) {
    return {
      status: 'configured',
      label: 'Configured',
      detail: 'Current permission grants support this capability.'
    };
  }

  return {
    status: 'gap',
    label: 'Gap',
    detail: `Missing one of: ${[...required, ...acceptable].filter(Boolean).join(', ') || 'capability permission mapping'}`
  };
}

function buildRoleCapabilityMatrix(roles) {
  const permissionIndex = buildRolePermissionIndex(roles ?? []);

  const rows = ROLE_CAPABILITY_DEFINITIONS.map((capability) => ({
    ...capability,
    verdicts: ROLE_CAPABILITY_COLUMNS.map((column) => ({
      roleCode: column.roleCode,
      label: column.label,
      ...evaluateRoleCapability(capability, column, permissionIndex)
    }))
  }));

  const engineerColumn = ROLE_CAPABILITY_COLUMNS.find((column) => column.roleCode === 'ENGINEERING');
  const engineerVerdicts = rows.map((row) => row.verdicts.find((verdict) => verdict.roleCode === engineerColumn.roleCode)).filter(Boolean);

  return {
    rows,
    summary: {
      capabilityCount: rows.length,
      engineerConfiguredCount: engineerVerdicts.filter((verdict) => ['configured', 'expected'].includes(verdict.status)).length,
      engineerGapCount: engineerVerdicts.filter((verdict) => verdict.status === 'gap').length,
      engineerOverScopedCount: engineerVerdicts.filter((verdict) => verdict.status === 'overscoped').length,
      roleCount: ROLE_CAPABILITY_COLUMNS.length
    }
  };
}
/* 042G_ROLE_CAPABILITY_MATRIX_END */


export default function RoleAdminDirectoryPanel() {
  const [payload, setPayload] = useState({ loading: true, data: null, error: null });
  const [validationPayload, setValidationPayload] = useState({ loading: true, data: null, error: null });
  const [selectedRoleCode, setSelectedRoleCode] = useState('all');
  const [permissionSearch, setPermissionSearch] = useState('');
  const [activeSecuritySection, setActiveSecuritySection] = useState('overview');

  async function loadDirectory() {
    setPayload((current) => ({ ...current, loading: true, error: null }));

    try {
      const data = await fetchJson('/api/role-admin/summary');
      setPayload({ loading: false, data, error: null });
    } catch (error) {
      setPayload({
        loading: false,
        data: null,
        error: error instanceof Error ? error.message : 'Unable to load role directory.'
      });
    }
  }


  async function loadRoleValidationMatrix() {
    setValidationPayload((current) => ({ ...current, loading: true, error: null }));

    try {
      const [roleAccessPayload, contractsPayload, smokePayload] = await Promise.all([
        fetchJson('/api/security/role-access-matrix'),
        fetchJson('/api/security/route-permission-contracts'),
        fetchJson('/api/security/role-enforcement-smoke')
      ]);

      setValidationPayload({
        loading: false,
        data: {
          roleAccessPayload,
          contractsPayload,
          smokePayload
        },
        error: null
      });
    } catch (error) {
      setValidationPayload({
        loading: false,
        data: null,
        error: error instanceof Error ? error.message : 'Unable to load role enforcement validation matrix.'
      });
    }
  }

  useEffect(() => {
    loadDirectory();
    loadRoleValidationMatrix();
  }, []);

  const data = payload.data;
  const roles = data?.roles ?? [];
  const moduleTotals = data?.moduleTotals ?? [];

  const roleValidationMatrix = useMemo(
    () => buildRoleValidationMatrix(validationPayload.data ?? {}),
    [validationPayload.data]
  );

  const roleCapabilityMatrix = useMemo(
    () => buildRoleCapabilityMatrix(roles),
    [roles]
  );

  const filteredRoles = useMemo(() => {
    const search = permissionSearch.trim().toLowerCase();

    return roles.filter((role) => {
      const roleMatches = selectedRoleCode === 'all' || role.roleCode === selectedRoleCode;
      if (!roleMatches) return false;

      if (!search) return true;

      const text = [
        role.roleCode,
        role.roleName,
        role.plainLanguageDefinition,
        ...(role.assignedUsers ?? []).flatMap((user) => [user.displayName, user.email, user.teamName]),
        ...(role.permissionsByModule ?? []).flatMap((module) => [
          module.moduleCode,
          ...(module.permissions ?? []).flatMap((permission) => [
            permission.permissionCode,
            permission.permissionName,
            permission.permissionDescription
          ])
        ])
      ].join(' ').toLowerCase();

      return text.includes(search);
    });
  }, [roles, selectedRoleCode, permissionSearch]);

  if (payload.loading) return null;

  if (!payload.error && !data?.canViewRoleDirectory) {
    return null;
  }

  return (
    <section className="role-directory-panel">
      <div className="section-heading">
        <div>
          <p className="eyebrow">019M-AQ</p>
          <h2>Role / Security Administration</h2>
          <p className="section-copy">
            Review users, roles, permission coverage, and restricted route enforcement in focused sections. The restricted route matrix is security evidence only; normal role capabilities are reviewed separately from administrative guardrails.
          </p>
        </div>
        <button type="button" className="secondary-action" onClick={loadDirectory}>Refresh</button>
      </div>

      {payload.error ? <div className="error-text">{payload.error}</div> : null}

      {/* 042F_LEGACY_ROLE_ASSIGNMENT_REMOVAL_START */}
      {/* 042E_ROLE_ADMIN_UX_CLEANUP_START */}
      <div className="role-admin-section-tabs" role="tablist" aria-label="Role administration sections">
        <button
          type="button"
          className={activeSecuritySection === 'overview' ? 'active' : ''}
          onClick={() => setActiveSecuritySection('overview')}
        >
          Overview
        </button>
        <button
          type="button"
          className={activeSecuritySection === 'roles' ? 'active' : ''}
          onClick={() => setActiveSecuritySection('roles')}
        >
          Roles & Permissions
        </button>
        <button
          type="button"
          className={activeSecuritySection === 'capabilities' ? 'active' : ''}
          onClick={() => setActiveSecuritySection('capabilities')}
        >
          Role Capabilities
        </button>
        <button
          type="button"
          className={activeSecuritySection === 'restricted-routes' ? 'active' : ''}
          onClick={() => setActiveSecuritySection('restricted-routes')}
        >
          Restricted Route Enforcement
        </button>
        <a href="#user-admin">Open User Administration</a>
      </div>
      {/* 042E_ROLE_ADMIN_UX_CLEANUP_END */}
      {/* 042F_LEGACY_ROLE_ASSIGNMENT_REMOVAL_END */}

      {activeSecuritySection === 'overview' ? (
        <>
          <div className="role-admin-overview-callout-grid">
            <article>
              <span>Step 1</span>
              <strong>Assign users to the right role</strong>
              <p>User role changes are managed in the dedicated User Administration workflow, where user profile, team, department, manager, login, and role updates stay together.</p>
              <a href="#user-admin">Open User Administration</a>
            </article>
            <article>
              <span>Step 2</span>
              <strong>Review role permissions</strong>
              <p>Use Roles & Permissions to inspect what each role means, who has it, and which permissions are granted.</p>
              <button type="button" onClick={() => setActiveSecuritySection('roles')}>Open roles</button>
            </article>
            <article>
              <span>Step 3</span>
              <strong>Review role capabilities</strong>
              <p>Use Role Capabilities to confirm normal work access, starting with the Engineer baseline for time, assigned projects, read-only calendar, utilization, expense upload, AI time entry, and assigned documents.</p>
              <button type="button" onClick={() => setActiveSecuritySection('capabilities')}>Open capabilities</button>
            </article>
            <article>
              <span>Step 4</span>
              <strong>Validate restricted routes</strong>
              <p>Use Restricted Route Enforcement to confirm administrative, security, workflow, and export routes are blocked for roles that should not reach them.</p>
              <button type="button" onClick={() => setActiveSecuritySection('restricted-routes')}>Open restricted routes</button>
            </article>
          </div>

      <div className="role-directory-summary-grid">
        <article>
          <span>Roles</span>
          <strong>{data?.summary?.roleCount ?? 0}</strong>
          <small>{data?.summary?.activeRoleCount ?? 0} active</small>
        </article>
        <article>
          <span>User-role assignments</span>
          <strong>{data?.summary?.assignedUserRoleCount ?? 0}</strong>
          <small>Active users assigned to roles</small>
        </article>
        <article>
          <span>Permission grants</span>
          <strong>{data?.summary?.permissionGrantCount ?? 0}</strong>
          <small>{data?.summary?.moduleCount ?? 0} modules represented</small>
        </article>
      </div>

      <div className="role-directory-module-summary">
        <div className="section-heading compact">
          <div>
            <h3>Permission Modules Summary</h3>
            <p className="section-copy">
              This read-only summary shows how many role-permission grants exist in each platform module. It helps explain which areas of Project Health Dashboard are controlled by role permissions.
            </p>
          </div>
        </div>

        <div className="role-module-summary-list">
          {moduleTotals.map((module) => (
            <div key={module.moduleCode}>
              <span>{formatModuleLabel(module.moduleCode)}</span>
              <strong>{module.permissionGrantCount}</strong>
            </div>
          ))}
        </div>
      </div>

        </>
      ) : null}

      {activeSecuritySection === 'roles' ? (
        <>
      <div className="role-directory-section-heading">
        <div>
          <h3>Roles & Permissions Directory</h3>
          <p className="section-copy">
            Each role below shows its plain-language purpose, assigned team members, and the exact permissions granted by module.
          </p>
        </div>
      </div>

      <div className="role-directory-toolbar">
        <label>
          Role
          <select value={selectedRoleCode} onChange={(event) => setSelectedRoleCode(event.target.value)}>
            <option value="all">All roles</option>
            {roles.map((role) => (
              <option value={role.roleCode} key={role.roleCode}>{role.roleName}</option>
            ))}
          </select>
        </label>
        <label>
          Search role, person, team, module, or permission
          <input
            value={permissionSearch}
            onChange={(event) => setPermissionSearch(event.target.value)}
            placeholder="Example: utilization, engineer, MANAGE_ALL"
          />
        </label>
      </div>

      <div className="role-directory-grid">
        {filteredRoles.map((role) => (
          <article className="role-directory-card" key={role.roleCode}>
            <div className="role-directory-card-header">
              <div>
                <p className="eyebrow">{role.roleCode}</p>
                <h3>{role.roleName}</h3>
                <p>{role.plainLanguageDefinition}</p>
              </div>
              <span className={role.isActive ? 'badge' : 'badge muted'}>{role.isActive ? 'Active' : 'Inactive'}</span>
            </div>

            <div className="role-directory-metrics">
              <span><strong>{role.activeUserCount}</strong> assigned users</span>
              <span><strong>{role.permissionCount}</strong> permissions</span>
              <span><strong>{role.permissionsByModule?.length ?? 0}</strong> modules</span>
            </div>

            <div className="role-directory-section">
              <h4>Assigned team members</h4>
              {(role.assignedUsers ?? []).length > 0 ? (
                <div className="role-user-list">
                  {role.assignedUsers.map((user) => (
                    <div key={`${role.roleCode}-${user.userId}`}>
                      <strong>{user.displayName}</strong>
                      <span>{user.email}</span>
                      <small>{user.teamName}</small>
                    </div>
                  ))}
                </div>
              ) : (
                <p className="section-copy">No active users are assigned to this role.</p>
              )}
            </div>

            <div className="role-directory-section">
              <h4>Permissions by module</h4>
              <div className="permission-module-list">
                {(role.permissionsByModule ?? []).map((module) => (
                  <details key={`${role.roleCode}-${module.moduleCode}`}>
                    <summary>
                      <span>{formatModuleLabel(module.moduleCode)}</span>
                      <strong>{module.permissionCount}</strong>
                    </summary>
                    <div className="permission-chip-list">
                      {(module.permissions ?? []).map((permission) => (
                        <span key={permission.permissionCode} title={permission.permissionDescription}>
                          {permission.permissionCode}
                        </span>
                      ))}
                    </div>
                  </details>
                ))}
              </div>
            </div>
          </article>
        ))}
      </div>

      {filteredRoles.length === 0 ? (
        <div className="manager-empty-state">No roles match the current filter.</div>
      ) : null}


        </>
      ) : null}

      {activeSecuritySection === 'capabilities' ? (
        <section className="role-capability-matrix-panel">
          <div className="role-directory-section-heading">
            <div>
              <p className="eyebrow">042G</p>
              <h3>Role Capability Matrix</h3>
              <p className="section-copy">
                Review what each role should be able to do during normal work. This matrix is separate from restricted-route enforcement. The Engineer baseline follows the required logic for time entry, assigned projects, read-only calendar, calendar sync, AI-assisted time entry, own utilization, expense upload, dashboard access, and assigned project documents.
              </p>
            </div>
          </div>

          <div className="role-capability-summary-grid">
            <article>
              <span>Capabilities</span>
              <strong>{roleCapabilityMatrix.summary.capabilityCount}</strong>
              <small>Normal work capabilities tracked</small>
            </article>
            <article>
              <span>Roles compared</span>
              <strong>{roleCapabilityMatrix.summary.roleCount}</strong>
              <small>Engineer, Manager, PM, PTC, Accounting, Executive, Admin</small>
            </article>
            <article>
              <span>Engineer configured</span>
              <strong>{roleCapabilityMatrix.summary.engineerConfiguredCount}</strong>
              <small>Configured or baseline expected</small>
            </article>
            <article>
              <span>Engineer gaps</span>
              <strong>{roleCapabilityMatrix.summary.engineerGapCount}</strong>
              <small>Permission or route mapping still needed</small>
            </article>
            <article>
              <span>Engineer over-scope</span>
              <strong>{roleCapabilityMatrix.summary.engineerOverScopedCount}</strong>
              <small>Restricted permission detected</small>
            </article>
          </div>

          <div className="role-capability-engineer-baseline">
            <strong>Engineer baseline</strong>
            <span>Engineers should enter their own time, see assigned projects, view calendar read-only, sync calendar, use AI with time entry, see only their own utilization, view dashboard, upload expenses through CSV/Excel, and view assigned project documents.</span>
          </div>

          <div className="role-capability-scroll-note">
            <span>Configured means current permissions support the capability. Gap means a permission or backend route mapping still needs to be created or assigned. Over-scoped means the role has a permission that should remain restricted for that capability.</span>
          </div>

          <div className="role-capability-table-wrap">
            <table className="role-capability-table">
              <thead>
                <tr>
                  <th>Capability</th>
                  <th>Engineer intent</th>
                  <th>Permission signals</th>
                  {ROLE_CAPABILITY_COLUMNS.map((role) => (
                    <th key={`capability-head-${role.roleCode}`}>{role.label}</th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {roleCapabilityMatrix.rows.map((row) => (
                  <tr key={row.key}>
                    <td>
                      <strong>{row.capability}</strong>
                      <small>{row.area}</small>
                    </td>
                    <td>{row.engineerIntent}</td>
                    <td>
                      <div className="role-capability-chip-list">
                        {[...(row.requiredPermissions ?? []), ...(row.acceptablePermissions ?? [])].map((permission) => (
                          <span key={`${row.key}-${permission}`}>{permission}</span>
                        ))}
                        {(!row.requiredPermissions?.length && !row.acceptablePermissions?.length) ? <small>Baseline workspace access</small> : null}
                      </div>
                    </td>
                    {row.verdicts.map((verdict) => (
                      <td key={`${row.key}-${verdict.roleCode}`}>
                        <span className={`role-capability-verdict ${verdict.status}`} title={verdict.detail}>
                          {verdict.label}
                        </span>
                        <small>{verdict.detail}</small>
                      </td>
                    ))}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </section>
      ) : null}

      {activeSecuritySection === 'restricted-routes' ? (
      <section className="role-validation-matrix-panel">
        <div className="role-directory-section-heading">
          <div>
            <p className="eyebrow">042D</p>
            <h3>Restricted Route Enforcement Matrix</h3>
            <p className="section-copy">
              Validate administrative, security, workflow, export, and production-readiness route contracts against key production roles. This is not a full capability matrix; Engineer may be blocked here while still retaining normal access to time entry, assigned work, and engineering workspace areas.
            </p>
          </div>
          <button type="button" className="secondary-action" onClick={loadRoleValidationMatrix}>Refresh validation</button>
        </div>

        {validationPayload.loading ? (
          <div className="manager-empty-state">Loading role enforcement validation evidence...</div>
        ) : validationPayload.error ? (
          <div className="error-text">{validationPayload.error}</div>
        ) : (
          <>
            <div className="role-validation-summary-grid">
              <article>
                <span>Route contracts</span>
                <strong>{roleValidationMatrix.summary.activeContractCount}</strong>
                <small>{roleValidationMatrix.summary.contractCount} total contract(s)</small>
              </article>
              <article>
                <span>Roles checked</span>
                <strong>{ROLE_VALIDATION_COLUMNS.length}</strong>
                <small>{roleValidationMatrix.summary.roleCount} role(s) in backend matrix</small>
              </article>
              <article>
                <span>Allowed verdicts</span>
                <strong>{roleValidationMatrix.summary.allowedCount}</strong>
                <small>Explicit role or permission match</small>
              </article>
              <article>
                <span>Blocked verdicts</span>
                <strong>{roleValidationMatrix.summary.blockedCount}</strong>
                <small>Denied by contract or missing permission</small>
              </article>
              <article>
                <span>Review verdicts</span>
                <strong>{roleValidationMatrix.summary.reviewCount}</strong>
                <small>Contract needs more detail</small>
              </article>
              <article>
                <span>Smoke checks</span>
                <strong>{roleValidationMatrix.summary.smokeCheckCount}</strong>
                <small>Backend role-enforcement checks</small>
              </article>
            </div>

            <div className="role-validation-scroll-note">
              <span>This matrix focuses on restricted routes only. Allowed means the role is explicitly allowed or has a required permission. Blocked means the role is intentionally excluded from that protected route or lacks required access. Review means the route contract needs more detail.</span>
            </div>

            <div className="role-validation-table-wrap">
              <table className="role-validation-table">
                <thead>
                  <tr>
                    <th>Route / security area</th>
                    <th>Method</th>
                    <th>Required permissions</th>
                    {ROLE_VALIDATION_COLUMNS.map((role) => (
                      <th key={`validation-head-${role.roleCode}`}>{role.label}</th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {roleValidationMatrix.rows.map((row) => (
                    <tr key={`${row.method}-${row.routePath}`}>
                      <td>
                        <strong>{row.routePath}</strong>
                        <small>{row.routeKey}</small>
                      </td>
                      <td>{row.method}</td>
                      <td>
                        {row.requiredPermissions.length > 0 ? (
                          <div className="role-validation-chip-list">
                            {row.requiredPermissions.map((permission) => (
                              <span key={`${row.routePath}-${permission}`}>{permission}</span>
                            ))}
                          </div>
                        ) : (
                          <small>No explicit permission listed</small>
                        )}
                      </td>
                      {row.verdicts.map((verdict) => (
                        <td key={`${row.routePath}-${verdict.roleCode}`}>
                          <span className={`role-validation-verdict ${verdict.status}`} title={verdict.reason}>
                            {verdict.label}
                          </span>
                        </td>
                      ))}
                    </tr>
                  ))}
                </tbody>
              </table>

              {roleValidationMatrix.rows.length === 0 ? (
                <div className="manager-empty-state">No active route permission contracts were returned.</div>
              ) : null}
            </div>
          </>
        )}
      </section>
      ) : null}
    </section>
  );
}
