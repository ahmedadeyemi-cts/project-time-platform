#!/usr/bin/env bash
set -euo pipefail

REPO_DIR="/opt/project-time-platform/app/project-time-platform"
APP_FILE="$REPO_DIR/src/frontend/project-time-web/src/App.jsx"
DIST_DIR="$REPO_DIR/src/frontend/project-time-web/dist"

if [ ! -f "$APP_FILE" ]; then
  echo "ERROR: Missing $APP_FILE"
  exit 1
fi

python3 - <<'PY'
from pathlib import Path

app_file = Path('/opt/project-time-platform/app/project-time-platform/src/frontend/project-time-web/src/App.jsx')
app = app_file.read_text()

helper = r'''
  function getRowWorkflowState(rowId) {
    const rowEntries = Object.entries(entries)
      .filter(([key]) => key.startsWith(`${rowId}|`))
      .map(([, entry]) => entry)
      .filter((entry) => Number.parseFloat(entry.hours) > 0);

    if (rowEntries.length === 0) return 'Draft';

    const statuses = new Set(rowEntries.map((entry) => entry.savedStatus ?? 'draft'));

    if (statuses.has('manager_declined')) return 'Correction';

    const activeStatuses = new Set([
      'submitted',
      'manager_approved',
      'pm_approved',
      'accounting_ready',
      'reconciled',
      'locked'
    ]);

    if ([...statuses].some((status) => activeStatuses.has(status))) return 'Active';

    return 'Draft';
  }

'''

if 'function getRowWorkflowState(rowId)' not in app:
    anchor = '  function getCellHours(rowId, date, type) {'
    if anchor not in app:
        raise SystemExit('ERROR: Could not find insertion anchor for getRowWorkflowState.')
    app = app.replace(anchor, helper + anchor, 1)

app = app.replace(
    '<div role="cell"><span className="state-dot">•</span> {row.state}</div>',
    '<div role="cell"><span className="state-dot">•</span> {getRowWorkflowState(row.id)}</div>'
)

# Remove stale build so the browser cannot keep serving old assets after a failed build.
app_file.write_text(app)
PY

if [ -d "$DIST_DIR" ]; then
  echo "==> Removing stale frontend dist"
  rm -rf "$DIST_DIR"
fi

echo "==> Dynamic row state label repair applied"
echo "==> Row state behavior: Draft for unsaved/draft, Active for submitted/approved/accounting/reconciled/locked, Correction for manager-declined."
