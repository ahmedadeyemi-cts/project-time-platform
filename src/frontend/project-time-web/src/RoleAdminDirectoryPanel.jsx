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

export default function RoleAdminDirectoryPanel() {
  const [payload, setPayload] = useState({ loading: true, data: null, error: null });
  const [validationPayload, setValidationPayload] = useState({ loading: true, data: null, error: null });
  const [selectedRoleCode, setSelectedRoleCode] = useState('all');
  const [permissionSearch, setPermissionSearch] = useState('');

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
          <h2>Role Directory & Permission Visibility</h2>
          <p className="section-copy">
            Review what each role means, who is assigned to it, and which permissions are granted by module. The module summary is read-only; role assignment changes remain controlled by the existing administration workflow below.
          </p>
        </div>
        <button type="button" className="secondary-action" onClick={loadDirectory}>Refresh</button>
      </div>

      {payload.error ? <div className="error-text">{payload.error}</div> : null}

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

      <div className="role-directory-section-heading">
        <div>
          <h3>Role Directory</h3>
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


      <section className="role-validation-matrix-panel">
        <div className="role-directory-section-heading">
          <div>
            <p className="eyebrow">042D</p>
            <h3>Role Enforcement Validation Matrix</h3>
            <p className="section-copy">
              Validate route permission contracts against key production roles. This is a read-only security evidence view that helps confirm restricted routes stay blocked for roles that should not reach them.
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
              <span>Allowed means the role is explicitly allowed or has a required permission. Blocked means the contract restricts the role or the role lacks required access. Review means the route contract needs more detail.</span>
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
    </section>
  );
}
