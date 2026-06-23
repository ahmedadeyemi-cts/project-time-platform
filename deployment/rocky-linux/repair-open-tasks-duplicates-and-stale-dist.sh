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
lines = app_file.read_text().splitlines()
needle = 'const assignedOpenTasks = openTasks.data?.tasks ?? [];'

cleaned = []
removed = 0
previous_was_needle = False

for line in lines:
    stripped = line.strip()
    if stripped == needle and previous_was_needle:
        removed += 1
        continue
    cleaned.append(line)
    previous_was_needle = stripped == needle

app_file.write_text('\n'.join(cleaned) + '\n')
print(f'Removed {removed} duplicate assignedOpenTasks declaration(s).')
PY

echo "==> Current assignedOpenTasks declarations:"
grep -n "const assignedOpenTasks = openTasks.data?.tasks" "$APP_FILE" || true

if [ -d "$DIST_DIR" ]; then
  echo "==> Removing stale frontend dist folder so a failed build cannot serve old assets"
  rm -rf "$DIST_DIR"
fi

echo "==> Repair complete. Run deployment/rocky-linux/build-frontend.sh next."
