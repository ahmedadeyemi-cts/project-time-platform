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

replacements = {
    'Project Time Platform</strong>': 'Project Pulse</strong>',
    'US Signal Project Time Platform': 'US Signal Project Pulse',
    'Time, approval, utilization, and accounting workflow foundation': 'Project Pulse: time, approval, utilization, and accounting workflow',
    'Project Time Platform API': 'Project Pulse API',
}

for old, new in replacements.items():
    app = app.replace(old, new)

app_file.write_text(app)
PY

echo "==> Project Pulse branding patch applied"
echo "==> Validate with: grep -R \"Project Pulse\" src/frontend/project-time-web/src/App.jsx src/frontend/project-time-web/index.html"
