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

export default function RoleAdminDirectoryPanel() {
  const [payload, setPayload] = useState({ loading: true, data: null, error: null });
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

  useEffect(() => {
    loadDirectory();
  }, []);

  const data = payload.data;
  const roles = data?.roles ?? [];
  const moduleTotals = data?.moduleTotals ?? [];

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
              This read-only summary shows how many role-permission grants exist in each platform module. It helps explain which areas of Project Pulse are controlled by role permissions.
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
    </section>
  );
}
