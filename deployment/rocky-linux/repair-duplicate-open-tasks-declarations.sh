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
lines = app_file.read_text().splitlines()
needle = 'const assignedOpenTasks = openTasks.data?.tasks ?? [];'

cleaned = []
removed = 0
previous_was_needle = False

for line in lines:
    is_needle = line.strip() == needle
    if is_needle and previous_was_needle:
        removed += 1
        continue
    cleaned.append(line)
    previous_was_needle = is_needle

app_file.write_text('\n'.join(cleaned) + '\n')
print(f"Removed {removed} duplicate assignedOpenTasks declaration(s).")
PY

echo "==> Duplicate Open Tasks declaration repair complete"
echo "==> Remaining declarations:"
grep -n "const assignedOpenTasks = openTasks.data?.tasks" "$APP_FILE" || true
