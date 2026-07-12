#!/usr/bin/env bash
set -Eeuo pipefail

BASE_DIR="$HOME/project-health-dashboard-azure"
CONFIG_FILE="$BASE_DIR/config/az05c2b1h-restore-retry.env"
STATUS_JSON="/tmp/phd-az05c2b1h-restore-retry-status.json"

[ -s "$CONFIG_FILE" ] || {
    echo "ERROR: Restore retry state file is missing: $CONFIG_FILE" >&2
    exit 1
}

# shellcheck disable=SC1090
source "$CONFIG_FILE"

az vm run-command show \
    --resource-group "$RESTORE_VM_RESOURCE_GROUP" \
    --vm-name "$RESTORE_VM_NAME" \
    --run-command-name "$RESTORE_RUN_COMMAND" \
    --instance-view \
    --output json > "$STATUS_JSON"

python3 - \
    "$STATUS_JSON" \
    "$RESTORE_RUN_COMMAND" \
    "$RESTORE_RESULT_PREFIX" \
    "$RESTORE_STATE_DIRECTORY" <<'PY'
import json
import sys
from pathlib import Path

status_path = Path(sys.argv[1])
run_command = sys.argv[2]
result_prefix = sys.argv[3]
state_directory = sys.argv[4]

data = json.loads(status_path.read_text(encoding="utf-8"))
view = data.get("instanceView") or {}
execution = str(view.get("executionState") or "unknown")
exit_code = view.get("exitCode")
output = str(view.get("output") or "")
error = str(view.get("error") or "")

success_marker = "POSTGRESQL INITIAL SEED RESTORE RETRY VALIDATION PASSED"
failure_marker = "POSTGRESQL INITIAL SEED RESTORE RETRY VALIDATION FAILED"

print(f"RUN_COMMAND_NAME={run_command}")
print(f"RESULT_PREFIX={result_prefix}")
print(f"STATE_DIRECTORY={state_directory}")
print(f"EXECUTION_STATE={execution}")
print(f"EXIT_CODE={exit_code}")
print(f"ERROR_PRESENT={'yes' if error.strip() else 'no'}")
print()
print("LAST_OUTPUT_LINES:")
print("-" * 72)
for line in output.splitlines()[-80:]:
    print(line)
print("-" * 72)

if success_marker in output and execution.lower() == "succeeded" and exit_code == 0:
    print("RESTORE_RETRY_RESULT=PASSED")
elif failure_marker in output or execution.lower() in {"failed", "canceled", "timedout"}:
    print("RESTORE_RETRY_RESULT=FAILED")
elif execution.lower() == "running":
    print("RESTORE_RETRY_RESULT=STILL_RUNNING")
else:
    print("RESTORE_RETRY_RESULT=TERMINAL_OR_UNKNOWN")
PY
