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

repo = Path('/opt/project-time-platform/app/project-time-platform')
app_file = repo / 'src/frontend/project-time-web/src/App.jsx'
css_file = repo / 'src/frontend/project-time-web/src/timesheet.css'
app = app_file.read_text()
css = css_file.read_text()

if "const [remainingModules" not in app:
    app = app.replace(
        "  const [openTasks, setOpenTasks] = useState({ loading: true, data: null, error: null });",
        "  const [openTasks, setOpenTasks] = useState({ loading: true, data: null, error: null });\n  const [remainingModules, setRemainingModules] = useState({ loading: true, data: null, error: null });"
    )

# Expand the main startup Promise.all to include remaining module summary endpoints.
if "projectIntakeResult" not in app:
    app = app.replace(
        "fetchJson(`/api/assignments/open-tasks?weekStart=${selectedWeekStart}`)\n        ]);",
        "fetchJson(`/api/assignments/open-tasks?weekStart=${selectedWeekStart}`),\n          fetchJson('/api/project-intake/summary'),\n          fetchJson('/api/project-management/summary'),\n          fetchJson(`/api/resource-scheduling/capacity?weekStart=${selectedWeekStart}`),\n          fetchJson('/api/expenses/summary'),\n          fetchJson('/api/invoicing/summary'),\n          fetchJson('/api/reporting/executive-dashboard')\n        ]);"
    )

    app = app.replace(
        "const [healthResult, dbResult, schemaResult, timesheetResult, groupResult, locationsResult, policyResult, targetsResult, openTasksResult] = await Promise.all([",
        "const [healthResult, dbResult, schemaResult, timesheetResult, groupResult, locationsResult, policyResult, targetsResult, openTasksResult, projectIntakeResult, projectManagementResult, resourceCapacityResult, expenseSummaryResult, invoicingSummaryResult, executiveDashboardResult] = await Promise.all(["
    )

    app = app.replace(
        "setOpenTasks({ loading: false, data: openTasksResult, error: null });",
        "setOpenTasks({ loading: false, data: openTasksResult, error: null });\n          setRemainingModules({\n            loading: false,\n            error: null,\n            data: {\n              projectIntake: projectIntakeResult,\n              projectManagement: projectManagementResult,\n              resourceCapacity: resourceCapacityResult,\n              expenses: expenseSummaryResult,\n              invoicing: invoicingSummaryResult,\n              executiveDashboard: executiveDashboardResult\n            }\n          });"
    )

    app = app.replace(
        "setOpenTasks((current) => ({ ...current, loading: false, error: message }));",
        "setOpenTasks((current) => ({ ...current, loading: false, error: message }));\n          setRemainingModules((current) => ({ ...current, loading: false, error: message }));"
    )

# Add derived module data after totals if not present.
if "const moduleData = remainingModules.data" not in app:
    app = app.replace(
        "  const normalTotal = grandTotal - afterhoursTotal;\n",
        "  const normalTotal = grandTotal - afterhoursTotal;\n  const moduleData = remainingModules.data ?? {};\n  const intakeCount = moduleData.projectIntake?.count ?? 0;\n  const milestoneCount = moduleData.projectManagement?.milestoneCount ?? 0;\n  const riskCount = moduleData.projectManagement?.riskCount ?? 0;\n  const capacityCount = moduleData.resourceCapacity?.count ?? 0;\n  const expenseCount = moduleData.expenses?.count ?? 0;\n  const invoiceCount = moduleData.invoicing?.count ?? 0;\n  const executiveMetricCount = moduleData.executiveDashboard?.count ?? 0;\n"
    )

modules_section = r'''
      <section id="psa-modules" className="panel module-foundation-panel">
        <div className="section-header compact">
          <div>
            <p className="eyebrow">PSA platform modules</p>
            <h2>Remaining sections foundation</h2>
            <p className="muted">These sections prepare the rest of Project Pulse beyond time entry: intake, project management, resource scheduling, expenses, invoicing, reporting, and administrative workflow.</p>
          </div>
          <span className="pill">Foundation ready</span>
        </div>

        {remainingModules.error ? <p className="error-text">{remainingModules.error}</p> : null}

        <div className="module-grid">
          <article className="module-card">
            <span className="status-label">Project intake</span>
            <strong>{intakeCount} request{intakeCount === 1 ? '' : 's'}</strong>
            <small>Sales handoff, client intake, project templates, and PM assignment.</small>
          </article>
          <article className="module-card">
            <span className="status-label">Project management</span>
            <strong>{milestoneCount} milestones</strong>
            <small>{riskCount} tracked risk{riskCount === 1 ? '' : 's'} for project delivery governance.</small>
          </article>
          <article className="module-card">
            <span className="status-label">Resource scheduling</span>
            <strong>{capacityCount} capacity rows</strong>
            <small>Weekly availability, assigned hours, and utilization capacity planning.</small>
          </article>
          <article className="module-card">
            <span className="status-label">Expense management</span>
            <strong>{expenseCount} report{expenseCount === 1 ? '' : 's'}</strong>
            <small>Expense report shell, receipt tracking, reimbursable expenses, and approval state.</small>
          </article>
          <article className="module-card">
            <span className="status-label">Invoicing</span>
            <strong>{invoiceCount} invoice{invoiceCount === 1 ? '' : 's'}</strong>
            <small>Draft client invoice staging for labor, expenses, export, and accounting review.</small>
          </article>
          <article className="module-card">
            <span className="status-label">Executive reporting</span>
            <strong>{executiveMetricCount} metrics</strong>
            <small>Snapshot-based executive dashboard foundation for operational reporting.</small>
          </article>
        </div>

        <div className="module-detail-grid">
          <article>
            <h3>Project milestones</h3>
            {(moduleData.projectManagement?.milestones ?? []).slice(0, 7).map((milestone) => (
              <div className="module-list-row" key={`${milestone.projectCode}-${milestone.name}`}>
                <strong>{milestone.name}</strong>
                <span>{milestone.projectCode} • {milestone.status} • due {milestone.dueDate ?? 'TBD'}</span>
              </div>
            ))}
          </article>
          <article>
            <h3>Resource capacity</h3>
            {(moduleData.resourceCapacity?.capacity ?? []).map((capacity) => (
              <div className="module-list-row" key={`${capacity.resourceEmail}-${capacity.weekStart}`}>
                <strong>{capacity.resourceName}</strong>
                <span>{capacity.weekStart}: {formatNumber(capacity.assignedHours)} assigned / {formatNumber(capacity.availableHours)} available • {capacity.status}</span>
              </div>
            ))}
          </article>
        </div>
      </section>
'''

if 'id="psa-modules"' not in app:
    app = app.replace('      <section id="utilization" className="panel">', modules_section + '\n      <section id="utilization" className="panel">', 1)

if '.module-foundation-panel' not in css:
    css += r'''

.module-foundation-panel {
  scroll-margin-top: 110px;
}

.module-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
  gap: 1rem;
  margin-top: 1.2rem;
}

.module-card {
  border: 1px solid var(--border-color);
  border-radius: 1rem;
  padding: 1rem;
  background: var(--card-background);
  box-shadow: var(--card-shadow);
}

.module-card strong {
  display: block;
  font-size: 1.35rem;
  margin: 0.4rem 0;
}

.module-card small {
  color: var(--muted-text);
  line-height: 1.45;
}

.module-detail-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(320px, 1fr));
  gap: 1rem;
  margin-top: 1.25rem;
}

.module-detail-grid article {
  border: 1px solid var(--border-color);
  border-radius: 1rem;
  padding: 1rem;
  background: var(--surface-soft);
}

.module-list-row {
  display: grid;
  gap: 0.25rem;
  padding: 0.75rem 0;
  border-top: 1px solid var(--border-color);
}

.module-list-row:first-of-type {
  border-top: 0;
}

.module-list-row span {
  color: var(--muted-text);
  font-size: 0.9rem;
}
'''

app_file.write_text(app)
css_file.write_text(css)
PY

if [ -d "$DIST_DIR" ]; then
  echo "==> Removing stale frontend dist"
  rm -rf "$DIST_DIR"
fi

echo "==> Remaining PSA module UI patch applied"
