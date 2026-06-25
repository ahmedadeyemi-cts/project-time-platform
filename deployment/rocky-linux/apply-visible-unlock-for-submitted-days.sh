#!/usr/bin/env bash
set -euo pipefail

REPO_DIR="/opt/project-time-platform/app/project-time-platform"
APP_FILE="$REPO_DIR/src/frontend/project-time-web/src/App.jsx"

if [ ! -f "$APP_FILE" ]; then
  echo "ERROR: Missing $APP_FILE"
  exit 1
fi

python3 - <<'PY'
from pathlib import Path

app_file = Path('/opt/project-time-platform/app/project-time-platform/src/frontend/project-time-web/src/App.jsx')
app = app_file.read_text()

# Submitted days must still be clickable so the modal can show "Unlock this day".
# The modal fields remain disabled for submitted days, so the user cannot edit until unlocking.
app = app.replace(
'''                              disabled={!dayIsEditable}
                            >
                              {formatHoursValue(entry.hours)}
''',
'''                              disabled={false}
                            >
                              {formatHoursValue(entry.hours)}
''')

# Older patch variants may have used a different expression. Normalize those too.
app = app.replace(
'''                              disabled={!isDayEditable(day.date)}
                            >
                              {formatHoursValue(entry.hours)}
''',
'''                              disabled={false}
                            >
                              {formatHoursValue(entry.hours)}
''')

app_file.write_text(app)
PY

echo "==> Visible unlock fix applied"
echo "==> Submitted-day cells are now clickable so the Unlock button can appear in the modal."
echo "==> Validate with: grep -n \"disabled={false}\|Unlock this day\" src/frontend/project-time-web/src/App.jsx"
