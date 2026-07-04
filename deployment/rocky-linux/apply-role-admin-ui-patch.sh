#!/usr/bin/env bash
set -euo pipefail

REPO_DIR="/opt/project-time-platform/app/project-time-platform"
APP_FILE="$REPO_DIR/src/frontend/project-time-web/src/App.jsx"
CSS_FILE="$REPO_DIR/src/frontend/project-time-web/src/timesheet.css"
DIST_DIR="$REPO_DIR/src/frontend/project-time-web/dist"

if [ ! -f "$APP_FILE" ]; then
  echo "ERROR: Missing $APP_FILE"
  exit 1
fi

if [ ! -f "$CSS_FILE" ]; then
  echo "ERROR: Missing $CSS_FILE"
  exit 1
fi

python3 - <<'PY'
from pathlib import Path

app_file = Path('/opt/project-time-platform/app/project-time-platform/src/frontend/project-time-web/src/App.jsx')
css_file = Path('/opt/project-time-platform/app/project-time-platform/src/frontend/project-time-web/src/timesheet.css')
app = app_file.read_text()
css = css_file.read_text()

# Add role admin state.
if "const [roleAdminUsers, setRoleAdminUsers]" not in app:
    app = app.replace(
        "  const [apiHealth, setApiHealth] = useState({ loading: true, data: null, error: null });",
        "  const [apiHealth, setApiHealth] = useState({ loading: true, data: null, error: null });\n  const [roleAdminUsers, setRoleAdminUsers] = useState({ loading: true, data: null, error: null });\n  const [roleAdminRoles, setRoleAdminRoles] = useState({ loading: true, data: null, error: null });\n  const [roleAdminStatus, setRoleAdminStatus] = useState('No role changes yet');",
        1,
    )

# Add role admin loader and updater.
if "async function loadRoleAdminData" not in app:
    anchor = "  function hasPermission(permissionCode) {"
    insert = r'''  async function loadRoleAdminData() {
    try {
      const [usersResult, rolesResult] = await Promise.all([
        fetchJson('/api/admin/users'),
        fetchJson('/api/admin/roles')
      ]);
      setRoleAdminUsers({ loading: false, data: usersResult, error: null });
      setRoleAdminRoles({ loading: false, data: rolesResult, error: null });
    } catch (error) {
      const message = error instanceof Error ? error.message : 'Unable to load role administration data';
      setRoleAdminUsers((current) => ({ ...current, loading: false, error: message }));
      setRoleAdminRoles((current) => ({ ...current, loading: false, error: message }));
    }
  }

  async function updateUserRole(email, roleCode) {
    setRoleAdminStatus(`Updating ${email}...`);
    try {
      await postJson('/api/admin/users/roles', {
        email,
        roleCodes: [roleCode],
        reason: 'Updated from Project Pulse role administration screen'
      });
      setRoleAdminStatus(`Updated ${email} to ${roleCode}`);
      await loadRoleAdminData();
    } catch (error) {
      setRoleAdminStatus(error instanceof Error ? error.message : 'Role update failed');
    }
  }

'''
    if anchor in app:
      app = app.replace(anchor, insert + anchor, 1)

# Trigger loading for role admin after security context loads.
if "loadRoleAdminData();" not in app:
    app = app.replace(
        "        if (!cancelled) setSecurityContext({ loading: false, data: result, error: null });",
        "        if (!cancelled) {\n          setSecurityContext({ loading: false, data: result, error: null });\n          if ((result.permissions ?? []).includes('SYSTEM_ADMINISTRATION') || (result.permissions ?? []).includes('MANAGE_ALL')) {\n            void loadRoleAdminData();\n          }\n        }",
        1,
    )

# Add nav link.
if '<a href="#role-admin">Role Admin</a>' not in app:
    app = app.replace(
        "          {canViewPsaModules ? <a href=\"#psa-modules\">Modules</a> : null}",
        "          {canViewPsaModules ? <a href=\"#psa-modules\">Modules</a> : null}\n          {hasPermission('SYSTEM_ADMINISTRATION') || hasPermission('MANAGE_ALL') ? <a href=\"#role-admin\">Role Admin</a> : null}",
        1,
    )

role_admin_section = r'''
      {(hasPermission('SYSTEM_ADMINISTRATION') || hasPermission('MANAGE_ALL')) ? (
        <section id="role-admin" className="panel role-admin-panel">
          <div className="section-header compact">
            <div>
              <p className="eyebrow">Administration</p>
              <h2>User role administration</h2>
              <p className="muted">Assign each user to the workspace role that controls their available views and actions. PMO and PM/Project Manager have been consolidated as Project Management.</p>
            </div>
            <span className="pill">{roleAdminUsers.data?.count ?? 0} users</span>
          </div>

          {roleAdminUsers.error || roleAdminRoles.error ? <p className="error-text">{roleAdminUsers.error ?? roleAdminRoles.error}</p> : null}
          <p className="muted">{roleAdminStatus}</p>

          <div className="role-admin-table" role="table" aria-label="User role assignments">
            <div className="role-admin-row role-admin-header" role="row">
              <div role="columnheader">User</div>
              <div role="columnheader">Current role</div>
              <div role="columnheader">Assign role</div>
            </div>
            {(roleAdminUsers.data?.users ?? []).map((user) => (
              <div className="role-admin-row" role="row" key={user.email}>
                <div role="cell">
                  <strong>{user.displayName}</strong>
                  <span>{user.email}</span>
                  <small>{user.jobTitle || 'No title'}{user.department ? ` • ${user.department}` : ''}</small>
                </div>
                <div role="cell">
                  <span className="role-chip">{user.roleNames?.length ? user.roleNames.join(', ') : 'No role assigned'}</span>
                </div>
                <div role="cell">
                  <select
                    value={user.roleCodes?.[0] ?? ''}
                    onChange={(event) => void updateUserRole(user.email, event.target.value)}
                  >
                    <option value="" disabled>Select role</option>
                    {(roleAdminRoles.data?.roles ?? []).map((role) => (
                      <option value={role.roleCode} key={role.roleCode}>{role.roleName}</option>
                    ))}
                  </select>
                </div>
              </div>
            ))}
          </div>
        </section>
      ) : null}
'''

if 'id="role-admin"' not in app:
    if '      <section id="workflow"' in app:
        app = app.replace('      <section id="workflow"', role_admin_section + '\n      <section id="workflow"', 1)
    else:
        app = app.replace('    </main>', role_admin_section + '\n    </main>', 1)

if '.role-admin-panel' not in css:
    css += r'''

.role-admin-panel {
  scroll-margin-top: 110px;
}

.role-admin-table {
  display: grid;
  border: 1px solid var(--border-color, #d8dee8);
  border-radius: 1rem;
  overflow: hidden;
  margin-top: 1rem;
}

.role-admin-row {
  display: grid;
  grid-template-columns: minmax(240px, 1.3fr) minmax(220px, 1fr) minmax(220px, 0.8fr);
  gap: 1rem;
  align-items: center;
  padding: 0.85rem 1rem;
  border-top: 1px solid var(--border-color, #d8dee8);
  background: var(--card-background, #fff);
}

.role-admin-row:first-child {
  border-top: 0;
}

.role-admin-header {
  background: var(--surface-soft, #f8fafc);
  font-weight: 900;
  color: var(--muted-text, #5b6b89);
  text-transform: uppercase;
  letter-spacing: 0.06em;
  font-size: 0.8rem;
}

.role-admin-row div {
  display: grid;
  gap: 0.2rem;
}

.role-admin-row span,
.role-admin-row small {
  color: var(--muted-text, #5b6b89);
}

.role-admin-row select {
  width: 100%;
  border: 1px solid var(--border-color, #d8dee8);
  border-radius: 0.75rem;
  padding: 0.65rem 0.75rem;
  background: var(--card-background, #fff);
  color: var(--text-color, #172033);
  font: inherit;
  font-weight: 800;
}

.role-chip {
  width: fit-content;
  border-radius: 999px;
  padding: 0.35rem 0.65rem;
  background: rgba(0, 87, 146, 0.08);
  color: var(--brand-blue, #005792) !important;
  font-weight: 900;
}

@media (max-width: 820px) {
  .role-admin-row {
    grid-template-columns: 1fr;
  }

  .role-admin-header {
    display: none;
  }
}
'''

app_file.write_text(app)
css_file.write_text(css)
PY

if [ -d "$DIST_DIR" ]; then
  rm -rf "$DIST_DIR"
fi

echo "==> Role administration UI patch applied"
echo "==> Admin users can assign Engineer, Manager, Project Management, Project and Team Coordinator, or Administrator from the UI."
