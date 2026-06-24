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
import re

app_file = Path('/opt/project-time-platform/app/project-time-platform/src/frontend/project-time-web/src/App.jsx')
css_file = Path('/opt/project-time-platform/app/project-time-platform/src/frontend/project-time-web/src/timesheet.css')
app = app_file.read_text()
css = css_file.read_text()

# Add security context state.
if "const [securityContext, setSecurityContext]" not in app:
    app = app.replace(
        "  const [apiHealth, setApiHealth] = useState({ loading: true, data: null, error: null });",
        "  const [apiHealth, setApiHealth] = useState({ loading: true, data: null, error: null });\n  const [securityContext, setSecurityContext] = useState({ loading: true, data: null, error: null });",
        1,
    )

# Add security fetch effect after theme effect.
if "fetchJson('/api/security/me')" not in app:
    marker = "  useEffect(() => {\n    let cancelled = false;"
    security_effect = r'''

  useEffect(() => {
    let cancelled = false;

    async function loadSecurityContext() {
      try {
        const result = await fetchJson('/api/security/me');
        if (!cancelled) setSecurityContext({ loading: false, data: result, error: null });
      } catch (error) {
        if (!cancelled) setSecurityContext({ loading: false, data: null, error: error instanceof Error ? error.message : 'Unable to load security context' });
      }
    }

    loadSecurityContext();

    return () => {
      cancelled = true;
    };
  }, []);
'''
    app = app.replace(marker, security_effect + "\n" + marker, 1)

# Add helpers before return.
if "function hasPermission(permissionCode)" not in app:
    app = app.replace(
        "  return (\n    <main className=\"app-shell\">",
        r'''  function hasPermission(permissionCode) {
    return securityContext.data?.permissions?.includes(permissionCode) ?? false;
  }

  function canSeeAny(permissionCodes) {
    return permissionCodes.some((permissionCode) => hasPermission(permissionCode));
  }

  const roleNames = securityContext.data?.roles?.map((role) => role.roleName).join(', ') || 'No role assigned';
  const workspaceFeatures = securityContext.data?.features ?? [];
  const canManageHolidays = hasPermission('MANAGE_HOLIDAYS') || hasPermission('MANAGE_ALL');
  const canViewHolidayCalendar = hasPermission('VIEW_HOLIDAYS') || canManageHolidays;
  const canViewPsaModules = canSeeAny(['VIEW_PROJECT_INTAKE', 'VIEW_RESOURCE_SCHEDULING', 'VIEW_EXPENSES', 'VIEW_EXECUTIVE_REPORTING', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL']);

  return (
    <main className="app-shell">''',
        1,
    )

# Replace nav with role-aware navigation.
app = re.sub(
    r'''        <nav aria-label="Primary navigation">\n          <a href="#dashboard">Dashboard</a>\n          <a href="#timesheet">Timesheet</a>\n          <a href="#utilization">Utilization</a>\n          <a href="#workflow">Workflow</a>\n        </nav>''',
    r'''        <nav aria-label="Primary navigation">
          <a href="#dashboard">Dashboard</a>
          {hasPermission('VIEW_TIME_ENTRY') ? <a href="#timesheet">Timesheet</a> : null}
          {hasPermission('VIEW_OWN_UTILIZATION') || hasPermission('VIEW_TEAM_UTILIZATION') ? <a href="#utilization">Utilization</a> : null}
          {canViewHolidayCalendar ? <a href="#holiday-admin">Holidays</a> : null}
          {canViewPsaModules ? <a href="#psa-modules">Modules</a> : null}
          <a href="#workflow">Workflow</a>
        </nav>''',
    app,
    count=1,
)

# Insert role workspace panel after status grid.
role_panel = r'''

      <section className="panel role-workspace-panel" aria-label="Role-based workspace">
        <div className="section-header compact">
          <div>
            <p className="eyebrow">Role-based workspace</p>
            <h2>{securityContext.loading ? 'Loading workspace access...' : roleNames}</h2>
            <p className="muted">Views and actions are personalized by assigned role. Engineers see their time and utilization, managers see approvals and team reporting, project roles see project operations, and administrators see all modules.</p>
          </div>
          <span className="pill">{workspaceFeatures.length} available views</span>
        </div>
        {securityContext.error ? <p className="error-text">{securityContext.error}</p> : null}
        <div className="role-feature-grid">
          {workspaceFeatures.map((feature) => (
            <a className="role-feature-card" href={feature.routeAnchor ?? '#dashboard'} key={feature.featureCode}>
              <strong>{feature.featureName}</strong>
              <span>{feature.description}</span>
            </a>
          ))}
        </div>
      </section>
'''
if 'role-workspace-panel' not in app:
    app = app.replace('      </section>\n\n      <section id="timesheet"', '      </section>' + role_panel + '\n      <section id="timesheet"', 1)

# Gate holiday admin with role access. Hide admin controls from engineers but allow read-only holiday list.
if 'holiday-admin-readonly-note' not in app:
    app = app.replace(
        '<p className="muted">View uploaded holidays by year, import a CSV, and keep company-paid holidays ready for automatic 8.00-hour Holiday entries.</p>',
        '<p className="muted">View uploaded holidays by year. Holiday import is available only to Managers, Project and Team Coordinators, and Administrators.</p>'
    )
    app = app.replace(
        '<textarea\n          className="holiday-upload-textarea"',
        '{canManageHolidays ? (\n        <textarea\n          className="holiday-upload-textarea"'
    )
    app = app.replace(
        '''        <div className="toolbar-actions holiday-upload-actions">
          <button type="button" className="primary-action" onClick={importHolidayCsv}>Import holidays</button>
          <span className="muted">{holidayUploadStatus}</span>
        </div>

        <div className="holiday-list-card compact-holiday-list">''',
        '''        <div className="toolbar-actions holiday-upload-actions">
          <button type="button" className="primary-action" onClick={importHolidayCsv}>Import holidays</button>
          <span className="muted">{holidayUploadStatus}</span>
        </div>
        ) : (
          <p className="muted holiday-admin-readonly-note">You have read-only access to the holiday calendar. Contact an administrator or manager to upload yearly holidays.</p>
        )}

        <div className="holiday-list-card compact-holiday-list">'''
    )

# Hide entire holiday section if user cannot even view holiday calendar.
if 'className={`panel holiday-admin-panel' not in app:
    app = app.replace('className="panel holiday-admin-panel"', 'className={`panel holiday-admin-panel ${canViewHolidayCalendar ? \'\' : \'access-hidden\'}`}', 1)

# Hide PSA module section for roles without project/reporting permissions.
if 'className={`panel module-foundation-panel' not in app:
    app = app.replace('className="panel module-foundation-panel"', 'className={`panel module-foundation-panel ${canViewPsaModules ? \'\' : \'access-hidden\'}`}', 1)

# Add CSS.
if '.role-workspace-panel' not in css:
    css += r'''

.role-workspace-panel {
  padding: clamp(1.2rem, 2vw, 2rem) !important;
}

.role-feature-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(210px, 1fr));
  gap: 0.85rem;
  margin-top: 1rem;
}

.role-feature-card {
  display: grid;
  gap: 0.35rem;
  border: 1px solid var(--border-color, #d8dee8);
  border-radius: 1rem;
  padding: 0.9rem 1rem;
  background: var(--card-background, #fff);
  color: inherit;
  text-decoration: none;
  transition: transform 0.15s ease, box-shadow 0.15s ease;
}

.role-feature-card:hover {
  transform: translateY(-1px);
  box-shadow: 0 14px 34px rgba(15, 23, 42, 0.08);
}

.role-feature-card strong {
  color: var(--brand-blue, #005792);
}

.role-feature-card span,
.holiday-admin-readonly-note {
  color: var(--muted-text, #5b6b89);
  line-height: 1.4;
}

.access-hidden {
  display: none !important;
}
'''

app_file.write_text(app)
css_file.write_text(css)
PY

if [ -d "$DIST_DIR" ]; then
  rm -rf "$DIST_DIR"
fi

echo "==> Role-aware UI patch applied"
echo "==> UI now reads /api/security/me and hides/views modules by role permissions."
