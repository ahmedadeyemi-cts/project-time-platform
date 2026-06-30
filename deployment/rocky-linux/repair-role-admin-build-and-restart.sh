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

path = Path('/opt/project-time-platform/app/project-time-platform/src/frontend/project-time-web/src/App.jsx')
text = path.read_text()

# The previous role-admin UI patch exposed a JSX syntax issue in the holiday admin
# conditional. The true branch has both a textarea and a toolbar div, so it must
# be wrapped in a fragment.
text = text.replace(
"""        {canManageHolidays ? (
        <textarea
""",
"""        {canManageHolidays ? (
          <>
        <textarea
"""
)

text = text.replace(
"""        <div className="toolbar-actions holiday-upload-actions">
          <button type="button" className="primary-action" onClick={importHolidayCsv}>Import holidays</button>
          <span className="muted">{holidayUploadStatus}</span>
        </div>
        ) : (
""",
"""        <div className="toolbar-actions holiday-upload-actions">
          <button type="button" className="primary-action" onClick={importHolidayCsv}>Import holidays</button>
          <span className="muted">{holidayUploadStatus}</span>
        </div>
          </>
        ) : (
"""
)

# Also handle a more-indented variant if a later patch shifted whitespace.
text = text.replace(
"""          {canManageHolidays ? (
          <textarea
""",
"""          {canManageHolidays ? (
            <>
          <textarea
"""
)

text = text.replace(
"""          <div className="toolbar-actions holiday-upload-actions">
            <button type="button" className="primary-action" onClick={importHolidayCsv}>Import holidays</button>
            <span className="muted">{holidayUploadStatus}</span>
          </div>
          ) : (
""",
"""          <div className="toolbar-actions holiday-upload-actions">
            <button type="button" className="primary-action" onClick={importHolidayCsv}>Import holidays</button>
            <span className="muted">{holidayUploadStatus}</span>
          </div>
            </>
          ) : (
"""
)

path.write_text(text)
PY

if [ -d "$DIST_DIR" ]; then
  echo "==> Removing stale frontend dist"
  rm -rf "$DIST_DIR"
fi

echo "==> Role admin frontend build syntax repair applied"
