#!/usr/bin/env bash
set -Eeuo pipefail

LOCATION="eastus"
RG_MIGRATION="rg-project-health-dashboard-test-migration-eastus"
VM_NAME="vm-phd-test-db-migrate-eus"
FAILED_RESTORE_COMMAND="phd-restore-postgresql13-seed"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
EVIDENCE_COMMAND="phdrestoreevidence${STAMP,,}"

BASE_DIR="$HOME/project-health-dashboard-azure"
LOG_DIR="$BASE_DIR/logs"
LOG="$LOG_DIR/az05c2b1e-failed-restore-evidence-$STAMP.log"

mkdir -p "$LOG_DIR"

section() {
    echo
    echo "============================================================"
    echo "$1"
    echo "============================================================"
}

{
    section "AZ-05C2B1E - Collect Failed Restore Evidence"

    FAILED_STATE="$(
        az vm run-command show \
            -g "$RG_MIGRATION" \
            --vm-name "$VM_NAME" \
            --run-command-name "$FAILED_RESTORE_COMMAND" \
            --instance-view \
            --query instanceView.executionState \
            -o tsv
    )"

    FAILED_EXIT="$(
        az vm run-command show \
            -g "$RG_MIGRATION" \
            --vm-name "$VM_NAME" \
            --run-command-name "$FAILED_RESTORE_COMMAND" \
            --instance-view \
            --query instanceView.exitCode \
            -o tsv
    )"

    echo "FAILED_RESTORE_EXECUTION_STATE=$FAILED_STATE"
    echo "FAILED_RESTORE_EXIT_CODE=$FAILED_EXIT"
    echo "EVIDENCE_RUN_COMMAND=$EVIDENCE_COMMAND"

    GUEST_SCRIPT="$(cat <<'EOF'
#!/usr/bin/env bash
set -Eeuo pipefail

STATE_DIR="/var/lib/project-health-dashboard/az05c2b1"
SOURCE_DIR="$STATE_DIR/source"
RESULT_DIR="$STATE_DIR/results"
RUN_LOG="/var/log/phd-az05c2b1-restore-validation.log"

printf 'EVIDENCE_TIME=%s\n' "$(date -u -Is)"

printf 'ACTIVE_RESTORE_PROCESSES_BEGIN\n'
ps -eo pid=,etimes=,args= \
    | grep -E '[a]zcopy copy|[p]g_restore|[p]sql|phd-restore-postgresql13-seed' \
    || true
printf 'ACTIVE_RESTORE_PROCESSES_END\n'

if [ -f "$RUN_LOG" ]; then
    printf 'RUN_LOG_BYTES=%s\n' "$(stat -c '%s' "$RUN_LOG")"
    printf 'RUN_LOG_MODIFIED=%s\n' "$(stat -c '%y' "$RUN_LOG")"
    printf 'RUN_LOG_TAIL_BEGIN\n'
    tail -n 120 "$RUN_LOG"
    printf 'RUN_LOG_TAIL_END\n'
else
    echo 'RUN_LOG_MISSING=true'
fi

printf 'SOURCE_FILE_COUNT=%s\n' "$(find "$SOURCE_DIR" -maxdepth 1 -type f 2>/dev/null | wc -l | tr -d ' ')"
printf 'SOURCE_TOTAL_BYTES=%s\n' "$(find "$SOURCE_DIR" -maxdepth 1 -type f -printf '%s\n' 2>/dev/null | awk '{s+=$1} END{print s+0}')"
printf 'RESULT_FILE_COUNT=%s\n' "$(find "$RESULT_DIR" -maxdepth 1 -type f 2>/dev/null | wc -l | tr -d ' ')"
printf 'RESULT_TOTAL_BYTES=%s\n' "$(find "$RESULT_DIR" -maxdepth 1 -type f -printf '%s\n' 2>/dev/null | awk '{s+=$1} END{print s+0}')"

printf 'RESULT_FILES_BEGIN\n'
find "$RESULT_DIR" -maxdepth 1 -type f -printf '%f %s bytes\n' 2>/dev/null | sort || true
printf 'RESULT_FILES_END\n'

if [ -f "$RESULT_DIR/validation-summary.txt" ]; then
    printf 'VALIDATION_SUMMARY_BEGIN\n'
    cat "$RESULT_DIR/validation-summary.txt"
    printf 'VALIDATION_SUMMARY_END\n'
else
    echo 'VALIDATION_SUMMARY_MISSING=true'
fi

LATEST_AZCOPY_LOG="$(find "$STATE_DIR/azcopy-logs" -type f -name '*.log' -printf '%T@ %p\n' 2>/dev/null | sort -n | tail -n 1 | cut -d' ' -f2- || true)"

if [ -n "$LATEST_AZCOPY_LOG" ]; then
    echo "LATEST_AZCOPY_LOG=$LATEST_AZCOPY_LOG"
    printf 'AZCOPY_LOG_TAIL_BEGIN\n'
    tail -n 120 "$LATEST_AZCOPY_LOG" || true
    printf 'AZCOPY_LOG_TAIL_END\n'
else
    echo 'LATEST_AZCOPY_LOG=none'
fi

printf 'FAILED_RESTORE_EVIDENCE_COMPLETE\n'
EOF
)"

    az vm run-command create \
        -g "$RG_MIGRATION" \
        --vm-name "$VM_NAME" \
        --run-command-name "$EVIDENCE_COMMAND" \
        --location "$LOCATION" \
        --async-execution false \
        --script "$GUEST_SCRIPT" \
        --timeout-in-seconds 300 \
        --only-show-errors \
        -o none

    section "Failed restore evidence"

    az vm run-command show \
        -g "$RG_MIGRATION" \
        --vm-name "$VM_NAME" \
        --run-command-name "$EVIDENCE_COMMAND" \
        --instance-view \
        --query '{RunCommand:name,Execution:instanceView.executionState,ExitCode:instanceView.exitCode,Output:instanceView.output,Error:instanceView.error}' \
        -o jsonc

    echo
    echo "FAILED_RESTORE_EVIDENCE_COLLECTION_COMPLETE"
    echo "EVIDENCE_RUN_COMMAND=$EVIDENCE_COMMAND"

} 2>&1 | tee "$LOG"

echo
echo "Evidence log: $LOG"
