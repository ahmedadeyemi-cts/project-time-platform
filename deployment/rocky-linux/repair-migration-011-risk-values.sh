#!/usr/bin/env bash
set -euo pipefail

REPO_DIR="/opt/project-time-platform/app/project-time-platform"
MIGRATION_FILE="$REPO_DIR/database/migrations/011_remaining_psa_module_foundation.sql"

if [ ! -f "$MIGRATION_FILE" ]; then
  echo "ERROR: Missing $MIGRATION_FILE"
  exit 1
fi

python3 - <<'PY'
from pathlib import Path

path = Path('/opt/project-time-platform/app/project-time-platform/database/migrations/011_remaining_psa_module_foundation.sql')
sql = path.read_text()

sql = sql.replace(
"('Scope expansion', 'Remaining PSA modules may expand beyond the original minimum validation scope.', 'medium', 'medium', 'Use phased delivery gates and keep backlog items separated from validated functionality.'),",
"('Scope expansion', 'Remaining PSA modules may expand beyond the original minimum validation scope.', 'medium', 'medium', 'open', 'Use phased delivery gates and keep backlog items separated from validated functionality.'),"
)

sql = sql.replace(
"('Public validation exposure', 'Temporary public validation access must remain restricted to approved source IPs.', 'low', 'high', 'Expose only the frontend proxy and restrict source IP at OCI and OS firewall layers.')",
"('Public validation exposure', 'Temporary public validation access must remain restricted to approved source IPs.', 'low', 'high', 'open', 'Expose only the frontend proxy and restrict source IP at OCI and OS firewall layers.')"
)

path.write_text(sql)
PY

echo "==> Migration 011 risk VALUES repair applied"
grep -n "Scope expansion\|Public validation exposure" "$MIGRATION_FILE"
