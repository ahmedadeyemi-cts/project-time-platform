#!/usr/bin/env bash
set -euo pipefail

REPO_DIR="/opt/project-time-platform/app/project-time-platform"
API_FILE="$REPO_DIR/src/backend/ProjectTime.Api/Program.cs"

if [ ! -f "$API_FILE" ]; then
  echo "ERROR: Missing $API_FILE"
  exit 1
fi

python3 - <<'PY'
from pathlib import Path
import re

api_file = Path('/opt/project-time-platform/app/project-time-platform/src/backend/ProjectTime.Api/Program.cs')
api = api_file.read_text()

# The all-time-entry visibility repair accidentally ordered saved project-task
# rows by pt.display_order. The current project_tasks table does not have that
# column, which causes /api/timesheets/week to return HTTP 500 when saved project
# task rows are loaded.
api = api.replace(
    "ORDER BY te.work_date, te.time_type, COALESCE(npt.display_order, pt.display_order, 999), COALESCE(npt.category_name, pt.task_name, p.project_name);",
    "ORDER BY te.work_date, te.time_type, COALESCE(npt.display_order, 999), COALESCE(npt.category_name, pt.task_name, p.project_name);"
)

# Also handle the same expression if whitespace changed.
api = re.sub(
    r"ORDER BY\s+te\.work_date,\s*te\.time_type,\s*COALESCE\(npt\.display_order,\s*pt\.display_order,\s*999\),\s*COALESCE\(npt\.category_name,\s*pt\.task_name,\s*p\.project_name\);",
    "ORDER BY te.work_date, te.time_type, COALESCE(npt.display_order, 999), COALESCE(npt.category_name, pt.task_name, p.project_name);",
    api,
    flags=re.S,
)

api = re.sub(r'version = "0\.[0-9]+\.[0-9]+"', 'version = "0.5.0"', api)
api_file.write_text(api)
PY

echo "==> Timesheet week HTTP 500 ORDER BY repair applied"
echo "==> Expected API version after redeploy: 0.5.0"
