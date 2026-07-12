#!/usr/bin/env bash
set -Eeuo pipefail

LOCATION="eastus"
RG_MIGRATION="rg-project-health-dashboard-test-migration-eastus"
VM_NAME="vm-phd-test-db-migrate-eus"
RESTORE_RUN_COMMAND="phd-restore-postgresql13-seed"
GUEST_SCRIPT_PATH="${1:-/tmp/phd-azure-az05c2b1g-guest-keyvault-access-probe.sh}"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
PROBE_RUN_COMMAND="phdkvprobe${STAMP,,}"

BASE_DIR="$HOME/project-health-dashboard-azure"
LOG_DIR="$BASE_DIR/logs"
LOG="$LOG_DIR/az05c2b1g-unique-keyvault-probe-$STAMP.log"

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
    section "AZ-05C2B1G - Unique Read-only Key Vault Access Probe"

    [ -s "$GUEST_SCRIPT_PATH" ] || fail "Guest Key Vault probe script is missing: $GUEST_SCRIPT_PATH"
    bash -n "$GUEST_SCRIPT_PATH"

    RESTORE_STATE="$(az vm run-command show \
        -g "$RG_MIGRATION" \
        --vm-name "$VM_NAME" \
        --run-command-name "$RESTORE_RUN_COMMAND" \
        --instance-view \
        --query instanceView.executionState \
        -o tsv 2>/dev/null || true)"

    echo "RESTORE_EXECUTION_STATE=${RESTORE_STATE:-unknown}"
    echo "UNIQUE_KEYVAULT_PROBE_RUN_COMMAND=$PROBE_RUN_COMMAND"
    echo "This probe does not stop, update, or resubmit the restore command."

    SCRIPT_CONTENT="$(cat "$GUEST_SCRIPT_PATH")"

    section "Creating uniquely named Key Vault probe command"

    set +e
    az vm run-command create \
        -g "$RG_MIGRATION" \
        --vm-name "$VM_NAME" \
        --run-command-name "$PROBE_RUN_COMMAND" \
        --location "$LOCATION" \
        --async-execution false \
        --script "$SCRIPT_CONTENT" \
        --timeout-in-seconds 180 \
        --only-show-errors \
        -o none
    CREATE_RC=$?
    set -e

    echo "PROBE_CREATE_EXIT_CODE=$CREATE_RC"

    EXECUTION_STATE=""

    for attempt in 1 2 3 4 5 6 7 8 9 10 11 12; do
        EXECUTION_STATE="$(az vm run-command show \
            -g "$RG_MIGRATION" \
            --vm-name "$VM_NAME" \
            --run-command-name "$PROBE_RUN_COMMAND" \
            --instance-view \
            --query instanceView.executionState \
            -o tsv 2>/dev/null || true)"

        case "$EXECUTION_STATE" in
            Succeeded|Failed|Canceled|TimedOut)
                break
                ;;
        esac

        echo "Probe state=${EXECUTION_STATE:-not-yet-available}; attempt $attempt/12."
        sleep 5
    done

    section "Unique Key Vault probe output"

    az vm run-command show \
        -g "$RG_MIGRATION" \
        --vm-name "$VM_NAME" \
        --run-command-name "$PROBE_RUN_COMMAND" \
        --instance-view \
        --query '{RunCommand:name,Execution:instanceView.executionState,ExitCode:instanceView.exitCode,Output:instanceView.output,Error:instanceView.error}' \
        -o jsonc

    echo
    echo "UNIQUE_KEYVAULT_ACCESS_PROBE_COMPLETE"
    echo "UNIQUE_KEYVAULT_PROBE_RUN_COMMAND=$PROBE_RUN_COMMAND"

} 2>&1 | tee "$LOG"

echo
echo "Unique Key Vault probe log: $LOG"
