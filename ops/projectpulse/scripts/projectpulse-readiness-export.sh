#!/usr/bin/env bash
set -Eeuo pipefail

STATE_DIR="/opt/project-time-platform/state"
STATE_FILE="$STATE_DIR/nginx-readiness.json"
TMP_FILE="$(mktemp)"

mkdir -p "$STATE_DIR"

set +e
OUTPUT="$(nginx -t 2>&1)"
EXIT_CODE=$?
set -e

if [ "$EXIT_CODE" -eq 0 ]; then
  STATUS="ready"
  MESSAGE="Nginx configuration test passed."
else
  STATUS="action_required"
  MESSAGE="Nginx configuration test failed."
fi

python3 - "$TMP_FILE" "$STATUS" "$EXIT_CODE" "$MESSAGE" "$OUTPUT" <<'PY'
import json
import sys
from datetime import datetime, timezone

path, status, exit_code, message, output = sys.argv[1:]

payload = {
    "status": status,
    "message": message,
    "checkedAt": datetime.now(timezone.utc).isoformat(),
    "command": "nginx -t",
    "exitCode": int(exit_code),
    "output": output
}

with open(path, "w", encoding="utf-8") as handle:
    json.dump(payload, handle, indent=2)
PY

mv "$TMP_FILE" "$STATE_FILE"
chmod 644 "$STATE_FILE"
