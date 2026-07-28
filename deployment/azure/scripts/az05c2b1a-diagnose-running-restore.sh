#!/usr/bin/env bash
set -Eeuo pipefail

LOCATION="eastus"
RG_MIGRATION="rg-project-health-dashboard-test-migration-eastus"
VM_NAME="vm-phd-test-db-migrate-eus"
DIAG_RUN_COMMAND="phd-restore-progress-diagnostic"
RESTORE_RUN_COMMAND="phd-restore-postgresql13-seed"

BASE_DIR="$HOME/project-health-dashboard-azure"
LOG_DIR="$BASE_DIR/logs"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
LOG="$LOG_DIR/az05c2b1a-restore-progress-diagnostic-$STAMP.log"
WORK_DIR="$(mktemp -d)"

mkdir -p "$LOG_DIR"
trap 'rm -rf "$WORK_DIR"' EXIT

section() {
    echo
    echo "============================================================"
    echo "$1"
    echo "============================================================"
}

{
    section "AZ-05C2B1A - Read-only restore progress diagnostic"

    RESTORE_STATE="$(
        az vm run-command show \
            -g "$RG_MIGRATION" \
            --vm-name "$VM_NAME" \
            --run-command-name "$RESTORE_RUN_COMMAND" \
            --instance-view \
            --query instanceView.executionState \
            -o tsv
    )"

    echo "RESTORE_EXECUTION_STATE=$RESTORE_STATE"
    echo "This diagnostic does not stop, update, or resubmit the restore command."

    DIAG_SCRIPT="$WORK_DIR/diagnose.sh"

    cat > "$DIAG_SCRIPT" <<'EOF'
#!/usr/bin/env bash
set -u

STATE_DIR="/var/lib/project-health-dashboard/az05c2b1"
SOURCE_DIR="$STATE_DIR/source"
RESULT_DIR="$STATE_DIR/results"
RUN_LOG="/var/log/phd-az05c2b1-restore-validation.log"

printf 'DIAGNOSTIC_TIME=%s\n' "$(date -u -Is)"

echo "ACTIVE_RESTORE_PROCESSES_BEGIN"
pgrep -af 'azcopy|pg_restore|psql|sha256sum|phd-az05c2b1|restore-postgresql13' || true
echo "ACTIVE_RESTORE_PROCESSES_END"

printf 'RUN_LOG_BYTES=%s\n' "$(stat -c '%s' "$RUN_LOG" 2>/dev/null || echo 0)"
printf 'RUN_LOG_MODIFIED=%s\n' "$(stat -c '%y' "$RUN_LOG" 2>/dev/null || echo missing)"

echo "RUN_LOG_TAIL_BEGIN"
tail -n 45 "$RUN_LOG" 2>/dev/null || true
echo "RUN_LOG_TAIL_END"

printf 'SOURCE_FILE_COUNT=%s\n' "$(find "$SOURCE_DIR" -maxdepth 1 -type f 2>/dev/null | wc -l | tr -d ' ')"
printf 'SOURCE_TOTAL_BYTES=%s\n' "$(du -sb "$SOURCE_DIR" 2>/dev/null | awk '{print $1}' || echo 0)"
printf 'RESULT_FILE_COUNT=%s\n' "$(find "$RESULT_DIR" -maxdepth 1 -type f 2>/dev/null | wc -l | tr -d ' ')"
printf 'RESULT_TOTAL_BYTES=%s\n' "$(du -sb "$RESULT_DIR" 2>/dev/null | awk '{print $1}' || echo 0)"

echo "SOURCE_FILES_BEGIN"
find "$SOURCE_DIR" -maxdepth 1 -type f -printf '%f %s bytes\n' 2>/dev/null | sort || true
echo "SOURCE_FILES_END"

LATEST_AZCOPY_LOG="$(find "$STATE_DIR/azcopy-logs" -maxdepth 1 -type f -name '*.log' -printf '%T@ %p\n' 2>/dev/null | sort -nr | head -n 1 | cut -d' ' -f2-)"
printf 'LATEST_AZCOPY_LOG=%s\n' "${LATEST_AZCOPY_LOG:-none}"

if [ -n "${LATEST_AZCOPY_LOG:-}" ] && [ -f "$LATEST_AZCOPY_LOG" ]; then
    echo "AZCOPY_LOG_TAIL_BEGIN"
    tail -n 35 "$LATEST_AZCOPY_LOG" || true
    echo "AZCOPY_LOG_TAIL_END"
fi

if [ -f "$RESULT_DIR/validation-summary.txt" ]; then
    echo "VALIDATION_SUMMARY_BEGIN"
    cat "$RESULT_DIR/validation-summary.txt"
    echo "VALIDATION_SUMMARY_END"
fi

echo "READ_ONLY_RESTORE_DIAGNOSTIC_COMPLETE"
EOF

    SCRIPT_CONTENT="$(cat "$DIAG_SCRIPT")"

    if az vm run-command show \
        -g "$RG_MIGRATION" \
        --vm-name "$VM_NAME" \
        --run-command-name "$DIAG_RUN_COMMAND" \
        --output none >/dev/null 2>&1; then

        az vm run-command update \
            -g "$RG_MIGRATION" \
            --vm-name "$VM_NAME" \
            --run-command-name "$DIAG_RUN_COMMAND" \
            --location "$LOCATION" \
            --script "$SCRIPT_CONTENT" \
            --timeout-in-seconds 120 \
            --only-show-errors \
            --output none

        echo "DIAGNOSTIC_ACTION=updated-and-executed"
    else
        az vm run-command create \
            -g "$RG_MIGRATION" \
            --vm-name "$VM_NAME" \
            --run-command-name "$DIAG_RUN_COMMAND" \
            --location "$LOCATION" \
            --script "$SCRIPT_CONTENT" \
            --timeout-in-seconds 120 \
            --only-show-errors \
            --output none

        echo "DIAGNOSTIC_ACTION=created-and-executed"
    fi

    section "Diagnostic output"

    az vm run-command show \
        -g "$RG_MIGRATION" \
        --vm-name "$VM_NAME" \
        --run-command-name "$DIAG_RUN_COMMAND" \
        --instance-view \
        --query '{Execution:instanceView.executionState,ExitCode:instanceView.exitCode,Output:instanceView.output,Error:instanceView.error}' \
        --output jsonc

    echo
    echo "READ-ONLY RESTORE PROGRESS DIAGNOSTIC COMPLETE"

} 2>&1 | tee "$LOG"

echo
echo "Diagnostic log: $LOG"
