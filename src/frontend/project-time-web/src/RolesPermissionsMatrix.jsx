import { useEffect, useMemo, useState } from 'react';
import './roles-permissions-matrix.css';

const recommendedAccessGroups = [
  {
    groupName: 'Executive / Sales Leadership',
    purpose: 'Read-only visibility into sold work, launch blockers, customer status, and delivery readiness.',
    suggestedRole: 'Sales / Executive Viewer',
    permissions: ['VIEW_PROJECT_INTAKE', 'VIEW_CUSTOMERS', 'VIEW_PROJECT_WORKLOAD', 'VIEW_RESOURCE_SCHEDULING']
  },
  {
    groupName: 'Sales Executive',
    purpose: 'Track sold projects after handoff without changing delivery assignments or administrative settings.',
    suggestedRole: 'Sales Insights User',
    permissions: ['VIEW_PROJECT_INTAKE', 'VIEW_CUSTOMERS', 'VIEW_RESOURCE_SCHEDULING']
  },
  {
    groupName: 'Project Management',
    purpose: 'Own delivery, intake handoff, workspace readiness, resource demand, project workload, and PM approval activities.',
    suggestedRole: 'Project Manager / Project Management',
    permissions: ['VIEW_PROJECT_WORKLOAD', 'VIEW_PROJECT_WORKSPACE', 'VIEW_PROJECT_INTAKE', 'MANAGE_PROJECT_INTAKE', 'VIEW_RESOURCE_SCHEDULING', 'MANAGE_RESOURCE_SCHEDULING', 'PROJECT_TIME_APPROVAL']
  },
  {
    groupName: 'Project Team Coordinator',
    purpose: 'Coordinate customer records, intake readiness, project documentation, and operational handoff hygiene.',
    suggestedRole: 'Project Team Coordinator',
    permissions: ['VIEW_PROJECT_INTAKE', 'MANAGE_PROJECT_INTAKE', 'VIEW_CUSTOMERS', 'MANAGE_CUSTOMERS', 'VIEW_PROJECT_WORKSPACE']
  },
  {
    groupName: 'Engineering Manager / Team Lead',
    purpose: 'Review team utilization, assignment readiness, engineer workload, and approval activity for the team.',
    suggestedRole: 'Engineering Team Lead',
    permissions: ['VIEW_TEAM_UTILIZATION', 'VIEW_INDIVIDUAL_UTILIZATION', 'VIEW_PROJECT_WORKSPACE', 'VIEW_RESOURCE_SCHEDULING', 'APPROVE_TIME']
  },
  {
    groupName: 'Engineer',
    purpose: 'Enter time, review assigned work, view relevant project documents, and track personal utilization.',
    suggestedRole: 'Engineer',
    permissions: ['VIEW_TIME_ENTRY', 'VIEW_OWN_UTILIZATION', 'VIEW_PROJECT_WORKSPACE', 'VIEW_ENGINEERING_PROJECT_DOCUMENTS', 'VIEW_HOLIDAYS']
  },
  {
    groupName: 'Accounting / Billing',
    purpose: 'Review approved work, export evidence, reconcile periods, and support invoicing readiness without changing security.',
    suggestedRole: 'Accounting Reviewer',
    permissions: ['VIEW_APPROVAL_WORKFLOW', 'VIEW_ACCOUNT_RECONCILIATION', 'EXPORT_TIME_EXCEL', 'EXPORT_TIME_PDF', 'DOWNLOAD_TIME_EXPORT_PACKAGE']
  },
  {
    groupName: 'System Administrator',
    purpose: 'Full platform administration, identity, resilience, service controls, role governance, and audit visibility.',
    suggestedRole: 'Administrator',
    permissions: ['SYSTEM_ADMINISTRATION', 'MANAGE_ALL', 'VIEW_AUDIT_TRAIL', 'VIEW_USER_ADMIN', 'MANAGE_USER_ADMIN']
  }
];

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
    .replaceAll('-', ' ')
    .replace(/\b\w/g, (letter) => letter.toUpperCase());
}

function csvEscape(value) {
  const text = String(value ?? '');
  return /[",\n]/.test(text) ? `"${text.replaceAll('"', '""')}"` : text;
}

function downloadTextFile(filename, content, type = 'text/plain') {
  const blob = new Blob([content], { type });
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = filename;
  document.body.appendChild(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(url);
}

export default function RolesPermissionsMatrix() {
  const [payload, setPayload] = useState({ loading: true, data: null, error: null });
  const [selectedRoleCode, setSelectedRoleCode] = useState('all');
  const [selectedModuleCode, setSelectedModuleCode] = useState('all');
  const [searchTerm, setSearchTerm] = useState('');
  const [actionStatus, setActionStatus] = useState('');

  async function loadMatrix() {
    setPayload((current) => ({ ...current, loading: true, error: null }));

    try {
      const data = await fetchJson('/api/role-admin/summary');
      setPayload({ loading: false, data, error: null });
    } catch (error) {
      setPayload({
        loading: false,
        data: null,
        error: error instanceof Error ? error.message : 'Unable to load roles and permissions matrix.'
      });
    }
  }

  useEffect(() => {
    loadMatrix();
  }, []);

  const data = payload.data;
  const roles = data?.roles ?? [];
  const moduleTotals = data?.moduleTotals ?? [];

  const permissionRows = useMemo(() => {
    const rowsByCode = new Map();

    roles.forEach((role) => {
      (role.permissionsByModule ?? []).forEach((module) => {
        (module.permissions ?? []).forEach((permission) => {
          const code = permission.permissionCode || 'UNKNOWN_PERMISSION';
          const existing = rowsByCode.get(code) ?? {
            permissionCode: code,
            permissionName: permission.permissionName || formatModuleLabel(code),
            permissionDescription: permission.permissionDescription || '',
            moduleCode: module.moduleCode || 'UNASSIGNED',
            roleCodes: new Set(),
            roleNames: new Set()
          };

          existing.roleCodes.add(role.roleCode);
          existing.roleNames.add(role.roleName);
          rowsByCode.set(code, existing);
        });
      });
    });

    return [...rowsByCode.values()]
      .map((row) => ({
        ...row,
        roleCodes: [...row.roleCodes],
        roleNames: [...row.roleNames]
      }))
      .sort((a, b) => String(a.moduleCode).localeCompare(String(b.moduleCode)) || String(a.permissionCode).localeCompare(String(b.permissionCode)));
  }, [roles]);

  const moduleOptions = useMemo(() => {
    const values = new Map();

    moduleTotals.forEach((module) => {
      if (module.moduleCode) values.set(module.moduleCode, formatModuleLabel(module.moduleCode));
    });

    permissionRows.forEach((row) => {
      if (row.moduleCode) values.set(row.moduleCode, formatModuleLabel(row.moduleCode));
    });

    return [...values.entries()]
      .map(([moduleCode, moduleName]) => ({ moduleCode, moduleName }))
      .sort((a, b) => a.moduleName.localeCompare(b.moduleName));
  }, [moduleTotals, permissionRows]);

  const visibleRoles = useMemo(() => {
    const sortedRoles = [...roles].sort((a, b) => String(a.roleName ?? '').localeCompare(String(b.roleName ?? '')));
    if (selectedRoleCode === 'all') return sortedRoles;
    return sortedRoles.filter((role) => role.roleCode === selectedRoleCode);
  }, [roles, selectedRoleCode]);

  const filteredPermissionRows = useMemo(() => {
    const search = searchTerm.trim().toLowerCase();
    const visibleRoleCodes = new Set(visibleRoles.map((role) => role.roleCode));

    return permissionRows.filter((row) => {
      const moduleMatches = selectedModuleCode === 'all' || row.moduleCode === selectedModuleCode;
      if (!moduleMatches) return false;

      if (selectedRoleCode !== 'all' && !row.roleCodes.some((roleCode) => visibleRoleCodes.has(roleCode))) {
        return false;
      }

      if (!search) return true;

      const haystack = [
        row.permissionCode,
        row.permissionName,
        row.permissionDescription,
        row.moduleCode,
        formatModuleLabel(row.moduleCode),
        ...row.roleNames
      ].join(' ').toLowerCase();

      return haystack.includes(search);
    });
  }, [permissionRows, searchTerm, selectedModuleCode, selectedRoleCode, visibleRoles]);

  const broadAccessRoles = useMemo(() => {
    return roles.filter((role) => {
      const codes = new Set((role.permissionsByModule ?? []).flatMap((module) => (module.permissions ?? []).map((permission) => permission.permissionCode)));
      return codes.has('MANAGE_ALL') || codes.has('SYSTEM_ADMINISTRATION');
    });
  }, [roles]);

  const unassignedRoles = useMemo(() => {
    return roles.filter((role) => Number(role.activeUserCount ?? 0) === 0);
  }, [roles]);

  const matrixSummary = [
    {
      label: 'Roles',
      value: data?.summary?.roleCount ?? roles.length,
      detail: `${data?.summary?.activeRoleCount ?? roles.filter((role) => role.isActive).length} active`
    },
    {
      label: 'Permission grants',
      value: data?.summary?.permissionGrantCount ?? permissionRows.reduce((total, row) => total + row.roleCodes.length, 0),
      detail: `${permissionRows.length} unique permission code(s)`
    },
    {
      label: 'Modules represented',
      value: data?.summary?.moduleCount ?? moduleOptions.length,
      detail: `${moduleOptions.length} module grouping(s)`
    },
    {
      label: 'User-role assignments',
      value: data?.summary?.assignedUserRoleCount ?? roles.reduce((total, role) => total + Number(role.activeUserCount ?? 0), 0),
      detail: 'Active user role mappings'
    },
    {
      label: 'Broad access roles',
      value: broadAccessRoles.length,
      detail: 'MANAGE_ALL or SYSTEM_ADMINISTRATION'
    },
    {
      label: 'Unassigned roles',
      value: unassignedRoles.length,
      detail: 'No active users assigned'
    }
  ];

  function buildMatrixCsv() {
    const headers = ['Module', 'Permission Code', 'Permission Name', 'Description', ...visibleRoles.map((role) => role.roleName || role.roleCode)];
    const rows = filteredPermissionRows.map((row) => [
      formatModuleLabel(row.moduleCode),
      row.permissionCode,
      row.permissionName,
      row.permissionDescription,
      ...visibleRoles.map((role) => row.roleCodes.includes(role.roleCode) ? 'Granted' : '')
    ]);

    return [headers, ...rows].map((row) => row.map(csvEscape).join(',')).join('\n');
  }

  function exportMatrixCsv() {
    downloadTextFile('phd-roles-permissions-matrix.csv', buildMatrixCsv(), 'text/csv');
    setActionStatus('Roles and permissions matrix exported as CSV.');
  }

  async function copyMatrixSummary() {
    const text = [
      'PHD Roles and Permissions Matrix Summary',
      `Roles: ${matrixSummary[0].value}`,
      `Permission grants: ${matrixSummary[1].value}`,
      `Modules represented: ${matrixSummary[2].value}`,
      `User-role assignments: ${matrixSummary[3].value}`,
      '',
      'Recommended access groups:',
      ...recommendedAccessGroups.map((group) => `- ${group.groupName}: ${group.suggestedRole} (${group.permissions.join(', ')})`)
    ].join('\n');

    try {
      await navigator.clipboard.writeText(text);
      setActionStatus('Matrix summary copied to clipboard.');
    } catch {
      setActionStatus('Unable to copy summary automatically. Use export CSV instead.');
    }
  }

  if (payload.loading) {
    return (
      <section className="roles-permissions-matrix">
        <div className="roles-matrix-header">
          <div>
            <p className="eyebrow">Module 037</p>
            <h2>Roles and Permissions Matrix</h2>
            <p className="muted">Loading role and permission data...</p>
          </div>
        </div>
      </section>
    );
  }

  if (!payload.error && !data?.canViewRoleDirectory) {
    return (
      <section className="roles-permissions-matrix">
        <div className="roles-matrix-header">
          <div>
            <p className="eyebrow">Module 037</p>
            <h2>Roles and Permissions Matrix</h2>
            <p className="muted">You do not have access to view the role and permission matrix.</p>
          </div>
        </div>
      </section>
    );
  }

  return (
    <section className="roles-permissions-matrix">
      <div className="roles-matrix-header">
        <div>
          <p className="eyebrow">Module 037</p>
          <h2>Roles and Permissions Matrix</h2>
          <p className="muted">
            Read-only matrix showing which roles have each permission, how permissions group by module, and which access groups should exist for Sales, PM, Engineering, Accounting, and Administration.
          </p>
        </div>
        <div className="roles-matrix-actions">
          <button type="button" className="secondary-action" onClick={loadMatrix}>Refresh</button>
          <button type="button" className="secondary-action" onClick={copyMatrixSummary}>Copy summary</button>
          <button type="button" className="primary-action" onClick={exportMatrixCsv}>Export CSV</button>
        </div>
      </div>

      {payload.error ? <div className="roles-matrix-error">{payload.error}</div> : null}
      {actionStatus ? <div className="roles-matrix-alert">{actionStatus}</div> : null}

      <div className="roles-matrix-summary-grid">
        {matrixSummary.map((item) => (
          <article key={item.label}>
            <span>{item.label}</span>
            <strong>{item.value}</strong>
            <small>{item.detail}</small>
          </article>
        ))}
      </div>

      <article className="roles-matrix-panel recommended-access-panel">
        <div className="roles-matrix-section-heading">
          <div>
            <h3>Recommended access groups</h3>
            <p className="muted">
              Suggested role groupings for the platform. These are read-only recommendations and do not change current role assignments.
            </p>
          </div>
        </div>

        <div className="recommended-access-grid">
          {recommendedAccessGroups.map((group) => (
            <article key={group.groupName}>
              <span>{group.groupName}</span>
              <strong>{group.suggestedRole}</strong>
              <p>{group.purpose}</p>
              <div>
                {group.permissions.map((permission) => (
                  <small key={`${group.groupName}-${permission}`}>{permission}</small>
                ))}
              </div>
            </article>
          ))}
        </div>
      </article>

      <article className="roles-matrix-panel">
        <div className="roles-matrix-section-heading">
          <div>
            <h3>Permission matrix</h3>
            <p className="muted">
              Use this table to see exactly which roles have each permission. Filter by role, module, or permission text.
            </p>
          </div>
          <span className="roles-matrix-count">{filteredPermissionRows.length} permissions shown</span>
        </div>

        <div className="roles-matrix-toolbar">
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
            Module
            <select value={selectedModuleCode} onChange={(event) => setSelectedModuleCode(event.target.value)}>
              <option value="all">All modules</option>
              {moduleOptions.map((module) => (
                <option value={module.moduleCode} key={module.moduleCode}>{module.moduleName}</option>
              ))}
            </select>
          </label>

          <label>
            Search
            <input
              value={searchTerm}
              onChange={(event) => setSearchTerm(event.target.value)}
              placeholder="Permission, role, module, MANAGE_ALL..."
            />
          </label>
        </div>

        <div className="roles-matrix-table-wrap">
          <table className="roles-matrix-table">
            <thead>
              <tr>
                <th>Module</th>
                <th>Permission</th>
                <th>Description</th>
                {visibleRoles.map((role) => (
                  <th key={`head-${role.roleCode}`} title={role.roleCode}>{role.roleName}</th>
                ))}
              </tr>
            </thead>
            <tbody>
              {filteredPermissionRows.map((row) => (
                <tr key={row.permissionCode}>
                  <td>{formatModuleLabel(row.moduleCode)}</td>
                  <td>
                    <strong>{row.permissionCode}</strong>
                    <small>{row.permissionName}</small>
                  </td>
                  <td>{row.permissionDescription || 'No description recorded.'}</td>
                  {visibleRoles.map((role) => {
                    const granted = row.roleCodes.includes(role.roleCode);
                    return (
                      <td className={granted ? 'granted' : 'not-granted'} key={`${row.permissionCode}-${role.roleCode}`}>
                        {granted ? '✓' : '—'}
                      </td>
                    );
                  })}
                </tr>
              ))}
            </tbody>
          </table>
        </div>

        {filteredPermissionRows.length === 0 ? (
          <div className="roles-matrix-empty">No permissions match the current filters.</div>
        ) : null}
      </article>

      <div className="roles-matrix-two-column">
        <article className="roles-matrix-panel">
          <div className="roles-matrix-section-heading">
            <div>
              <h3>Role breakdown</h3>
              <p className="muted">Each role with assigned users, permission count, and module coverage.</p>
            </div>
          </div>

          <div className="roles-breakdown-list">
            {roles.map((role) => (
              <article key={role.roleCode}>
                <div>
                  <p className="eyebrow">{role.roleCode}</p>
                  <h4>{role.roleName}</h4>
                  <p>{role.plainLanguageDefinition || 'No plain-language definition recorded.'}</p>
                </div>
                <div className="role-breakdown-metrics">
                  <span><strong>{role.activeUserCount ?? 0}</strong> users</span>
                  <span><strong>{role.permissionCount ?? 0}</strong> permissions</span>
                  <span><strong>{role.permissionsByModule?.length ?? 0}</strong> modules</span>
                </div>
              </article>
            ))}
          </div>
        </article>

        <article className="roles-matrix-panel">
          <div className="roles-matrix-section-heading">
            <div>
              <h3>Governance signals</h3>
              <p className="muted">Areas to review when tightening access or cleaning up role assignments.</p>
            </div>
          </div>

          <div className="governance-signal-list">
            <article>
              <span>Broad access roles</span>
              <strong>{broadAccessRoles.length}</strong>
              <p>{broadAccessRoles.length > 0 ? broadAccessRoles.map((role) => role.roleName).join(', ') : 'No broad access roles detected from current permission grants.'}</p>
            </article>

            <article>
              <span>Unassigned roles</span>
              <strong>{unassignedRoles.length}</strong>
              <p>{unassignedRoles.length > 0 ? unassignedRoles.map((role) => role.roleName).join(', ') : 'All visible roles have at least one active user assigned.'}</p>
            </article>

            <article>
              <span>Module concentration</span>
              <strong>{moduleOptions.length}</strong>
              <p>Permissions are grouped across the visible platform modules. Review modules with high grant counts for least-privilege alignment.</p>
            </article>
          </div>

          <div className="module-coverage-list">
            <h4>Module permission coverage</h4>
            {moduleTotals.map((module) => (
              <div key={module.moduleCode}>
                <span>{formatModuleLabel(module.moduleCode)}</span>
                <strong>{module.permissionGrantCount}</strong>
              </div>
            ))}
          </div>
        </article>
      </div>
    </section>
  );
}
