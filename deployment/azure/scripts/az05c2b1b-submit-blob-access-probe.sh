#!/usr/bin/env bash
set -Eeuo pipefail

LOCATION="eastus"
RG_MIGRATION="rg-project-health-dashboard-test-migration-eastus"
VM_NAME="vm-phd-test-db-migrate-eus"
RUN_COMMAND_NAME="phd-diagnose-blob-access"
GUEST_SCRIPT_PATH="${1:-/tmp/phd-azure-az05c2b1b-guest-blob-access-probe.sh}"

BASE_DIR="$HOME/project-health-dashboard-azure"
LOG_DIR="$BASE_DIR/logs"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
LOG="$LOG_DIR/az05c2b1b-blob-access-probe-$STAMP.log"

mkdir -p "$LOG_DIR"

section() {
    echo
    echo "============================================================"
    echo "$1"
    echo "============================================================"
}

fail() {
    echo "ERROR: $*" >&2
    exit 1
}

{
    section "AZ-05C2B1B - Read-only Blob access probe"

    [ -s "$GUEST_SCRIPT_PATH" ] || fail "Guest probe script is missing: $GUEST_SCRIPT_PATH"
    bash -n "$GUEST_SCRIPT_PATH"

    RESTORE_STATE="$(
        az vm run-command show \
            -g "$RG_MIGRATION" \
            --vm-name "$VM_NAME" \
            --run-command-name "phd-restore-postgresql13-seed" \
            --instance-view \
            --query instanceView.executionState \
            -o tsv
    )"

    echo "RESTORE_EXECUTION_STATE=$RESTORE_STATE"
    echo "This probe does not stop, update, or resubmit the restore command."

    SCRIPT_CONTENT="$(cat "$GUEST_SCRIPT_PATH")"

    if az vm run-command show \
        -g "$RG_MIGRATION" \
        --vm-name "$VM_NAME" \
        --run-command-name "$RUN_COMMAND_NAME" \
        --output none \
        >/dev/null 2>&1; then

        az vm run-command update \
            -g "$RG_MIGRATION" \
            --vm-name "$VM_NAME" \
            --run-command-name "$RUN_COMMAND_NAME" \
            --location "$LOCATION" \
            --async-execution false \
            --script "$SCRIPT_CONTENT" \
            --timeout-in-seconds 180 \
            --only-show-errors \
            --output none

        echo "PROBE_ACTION=updated-and-executed"
    else
        az vm run-command create \
            -g "$RG_MIGRATION" \
            --vm-name "$VM_NAME" \
            --run-command-name "$RUN_COMMAND_NAME" \
            --location "$LOCATION" \
            --async-execution false \
            --script "$SCRIPT_CONTENT" \
            --timeout-in-seconds 180 \
            --only-show-errors \
            --output none

        echo "PROBE_ACTION=created-and-executed"
    fi

    section "Blob access probe output"

    az vm run-command show \
        -g "$RG_MIGRATION" \
        --vm-name "$VM_NAME" \
        --run-command-name "$RUN_COMMAND_NAME" \
        --instance-view \
        --query '{Execution:instanceView.executionState,ExitCode:instanceView.exitCode,Output:instanceView.output,Error:instanceView.error}' \
        --output jsonc

    echo
    echo "READ-ONLY BLOB ACCESS PROBE COMPLETE"

} 2>&1 | tee "$LOG"

echo
echo "Probe log: $LOG"
