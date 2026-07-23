#!/usr/bin/env bash
set -Eeuo pipefail

LOCATION="eastus"
RG_MIGRATION="rg-project-health-dashboard-test-migration-eastus"
VM_NAME="vm-phd-test-db-migrate-eus"
PREP_RUN_COMMAND="phd-prepare-rocky10"
FAILED_RESTORE_RUN_COMMAND="phd-restore-postgresql13-seed"

STORAGE_ACCOUNT="stphdtest7825cc"
STORAGE_CONTAINER="database-exports"
RESULT_PREFIX_ROOT="restore-results"

BASE_DIR="$HOME/project-health-dashboard-azure"
CONFIG_DIR="$BASE_DIR/config"
LOG_DIR="$BASE_DIR/logs"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
STAMP_LOWER="${STAMP,,}"
RESTORE_RUN_COMMAND="phdrestoreseedretry${STAMP_LOWER}"
STATE_ID="az05c2b1h-${STAMP_LOWER}"
RESULT_PREFIX="$RESULT_PREFIX_ROOT/retry-$STAMP"
RUN_LOG_PATH="/var/log/phd-${STATE_ID}-restore-validation.log"
LOG="$LOG_DIR/az05c2b1h-submit-clean-restore-retry-$STAMP.log"
CONFIG_FILE="$CONFIG_DIR/az05c2b1h-restore-retry.env"
SOURCE_GUEST_SCRIPT="${1:-/tmp/phd-azure-az05c2b1-guest-postgresql-restore-validation.sh}"
TRANSFORMED_GUEST_SCRIPT="/tmp/phd-azure-az05c2b1h-guest-postgresql-restore-validation-$STAMP.sh"

mkdir -p "$CONFIG_DIR" "$LOG_DIR"

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
    section "AZ-05C2B1H - Submit Clean PostgreSQL Restore Retry"

    [ -s "$SOURCE_GUEST_SCRIPT" ] || fail "Canonical guest restore script is missing: $SOURCE_GUEST_SCRIPT"
    bash -n "$SOURCE_GUEST_SCRIPT"

    echo "TIME=$(date -u -Is)"
    echo "Resource group: $RG_MIGRATION"
    echo "VM: $VM_NAME"
    echo "Retry Run Command: $RESTORE_RUN_COMMAND"
    echo "Retry state ID: $STATE_ID"
    echo "Result prefix: $RESULT_PREFIX"

    section "Validating restore runner and terminal first attempt"

    VM_STATE="$(az vm show -g "$RG_MIGRATION" -n "$VM_NAME" --query provisioningState -o tsv)"
    POWER_STATE="$(
        az vm get-instance-view \
            -g "$RG_MIGRATION" \
            -n "$VM_NAME" \
            --query "instanceView.statuses[?starts_with(code, 'PowerState/')].displayStatus | [0]" \
            -o tsv
    )"

    PREP_STATE="$(
        az vm run-command show \
            -g "$RG_MIGRATION" \
            --vm-name "$VM_NAME" \
            --run-command-name "$PREP_RUN_COMMAND" \
            --instance-view \
            --query instanceView.executionState \
            -o tsv
    )"

    PREP_EXIT="$(
        az vm run-command show \
            -g "$RG_MIGRATION" \
            --vm-name "$VM_NAME" \
            --run-command-name "$PREP_RUN_COMMAND" \
            --instance-view \
            --query instanceView.exitCode \
            -o tsv
    )"

    PREP_OUTPUT="$(
        az vm run-command show \
            -g "$RG_MIGRATION" \
            --vm-name "$VM_NAME" \
            --run-command-name "$PREP_RUN_COMMAND" \
            --instance-view \
            --query instanceView.output \
            -o tsv
    )"

    FAILED_RESTORE_STATE="$(
        az vm run-command show \
            -g "$RG_MIGRATION" \
            --vm-name "$VM_NAME" \
            --run-command-name "$FAILED_RESTORE_RUN_COMMAND" \
            --instance-view \
            --query instanceView.executionState \
            -o tsv
    )"

    FAILED_RESTORE_EXIT="$(
        az vm run-command show \
            -g "$RG_MIGRATION" \
            --vm-name "$VM_NAME" \
            --run-command-name "$FAILED_RESTORE_RUN_COMMAND" \
            --instance-view \
            --query instanceView.exitCode \
            -o tsv
    )"

    echo "VM_PROVISIONING_STATE=$VM_STATE"
    echo "VM_POWER_STATE=$POWER_STATE"
    echo "PREP_EXECUTION_STATE=$PREP_STATE"
    echo "PREP_EXIT_CODE=$PREP_EXIT"
    echo "FIRST_RESTORE_EXECUTION_STATE=$FAILED_RESTORE_STATE"
    echo "FIRST_RESTORE_EXIT_CODE=$FAILED_RESTORE_EXIT"

    [ "$VM_STATE" = "Succeeded" ] || fail "Restore runner provisioning state is not Succeeded."
    [ "$POWER_STATE" = "VM running" ] || fail "Restore runner is not running."
    [ "$PREP_STATE" = "Succeeded" ] || fail "Rocky Linux preparation is not Succeeded."
    [ "$PREP_EXIT" = "0" ] || fail "Rocky Linux preparation returned a nonzero exit code."
    grep -q 'PRIVATE ROCKY 10 RESTORE RUNNER PREPARATION READY' <<< "$PREP_OUTPUT" \
        || fail "Preparation success marker was not found."

    case "$FAILED_RESTORE_STATE" in
        Failed|Succeeded|Canceled|TimedOut)
            ;;
        *)
            fail "First restore attempt is not terminal: $FAILED_RESTORE_STATE"
            ;;
    esac

    if az vm run-command show \
        -g "$RG_MIGRATION" \
        --vm-name "$VM_NAME" \
        --run-command-name "$RESTORE_RUN_COMMAND" \
        --output none \
        >/dev/null 2>&1; then
        fail "Generated retry Run Command name already exists: $RESTORE_RUN_COMMAND"
    fi

    section "Confirming temporary result-upload permission"

    VM_PRINCIPAL_ID="$(az vm identity show -g "$RG_MIGRATION" -n "$VM_NAME" --query principalId -o tsv)"
    STORAGE_ACCOUNT_ID="$(
        az resource list \
            --name "$STORAGE_ACCOUNT" \
            --resource-type Microsoft.Storage/storageAccounts \
            --query '[0].id' \
            -o tsv
    )"
    CONTAINER_SCOPE="$STORAGE_ACCOUNT_ID/blobServices/default/containers/$STORAGE_CONTAINER"

    [ -n "$VM_PRINCIPAL_ID" ] || fail "VM managed identity principal ID is empty."
    [ -n "$STORAGE_ACCOUNT_ID" ] || fail "Storage account resource ID is empty."

    TEMP_CONTRIBUTOR_ASSIGNMENT_ID="$(
        az role assignment list \
            --assignee "$VM_PRINCIPAL_ID" \
            --role "Storage Blob Data Contributor" \
            --scope "$CONTAINER_SCOPE" \
            --query '[0].id' \
            -o tsv \
            2>/dev/null || true
    )"

    [ -n "$TEMP_CONTRIBUTOR_ASSIGNMENT_ID" ] \
        || fail "Temporary result-upload role is missing. Do not submit without evidence-upload access."

    echo "TEMP_RESULT_UPLOAD_ROLE=existing"
    echo "TEMP_RESULT_UPLOAD_SCOPE=$CONTAINER_SCOPE"

    section "Building isolated guest restore script"

    python3 - \
        "$SOURCE_GUEST_SCRIPT" \
        "$TRANSFORMED_GUEST_SCRIPT" \
        "$STATE_ID" \
        "$RUN_LOG_PATH" <<'PY'
import sys
from pathlib import Path

source_path = Path(sys.argv[1])
target_path = Path(sys.argv[2])
state_id = sys.argv[3]
run_log = sys.argv[4]

text = source_path.read_text(encoding="utf-8")

replacements = [
    (
        'STATE_DIR="/var/lib/project-health-dashboard/az05c2b1"',
        f'STATE_DIR="/var/lib/project-health-dashboard/{state_id}"',
    ),
    (
        'RUN_LOG="/var/log/phd-az05c2b1-restore-validation.log"',
        f'RUN_LOG="{run_log}"',
    ),
    (
        'echo "AZ-05C2B1 guest restore started at $(date -u -Is)"',
        'echo "AZ-05C2B1H clean retry guest restore started at $(date -u -Is)"',
    ),
    (
        'POSTGRESQL INITIAL SEED RESTORE VALIDATION PASSED',
        'POSTGRESQL INITIAL SEED RESTORE RETRY VALIDATION PASSED',
    ),
    (
        'POSTGRESQL INITIAL SEED RESTORE VALIDATION FAILED',
        'POSTGRESQL INITIAL SEED RESTORE RETRY VALIDATION FAILED',
    ),
]

for old, new in replacements:
    if old not in text:
        raise SystemExit(f"ERROR: Required canonical guest-script pattern not found: {old}")
    text = text.replace(old, new, 1)

preflight_anchor = 'FAILURE_STAGE="downloading-source-package"\n'
preflight = '''FAILURE_STAGE="validating-private-dns-preflight"

for required_host in \
    "${STORAGE_ACCOUNT}.blob.core.windows.net" \
    "${KEY_VAULT}.vault.azure.net" \
    "$POSTGRES_FQDN"; do
    echo "DNS_PREFLIGHT_HOST=$required_host"
    getent ahostsv4 "$required_host" || {
        echo "ERROR: Required private hostname does not resolve: $required_host"
        exit 1
    }
done

echo "PRIVATE_DNS_PREFLIGHT=passed"

FAILURE_STAGE="downloading-source-package"
'''

if preflight_anchor not in text:
    raise SystemExit("ERROR: Download-stage anchor was not found in canonical guest script.")
text = text.replace(preflight_anchor, preflight, 1)

target_path.write_text(text, encoding="utf-8")
PY

    chmod 700 "$TRANSFORMED_GUEST_SCRIPT"
    bash -n "$TRANSFORMED_GUEST_SCRIPT"

    grep -Fq "STATE_DIR=\"/var/lib/project-health-dashboard/$STATE_ID\"" "$TRANSFORMED_GUEST_SCRIPT" \
        || fail "Transformed guest script does not contain the isolated state directory."
    grep -Fq "RUN_LOG=\"$RUN_LOG_PATH\"" "$TRANSFORMED_GUEST_SCRIPT" \
        || fail "Transformed guest script does not contain the isolated run log."

    echo "TRANSFORMED_GUEST_SCRIPT=$TRANSFORMED_GUEST_SCRIPT"
    echo "GUEST_STATE_DIRECTORY=/var/lib/project-health-dashboard/$STATE_ID"
    echo "GUEST_RUN_LOG=$RUN_LOG_PATH"
    echo "PRIVATE_DNS_PREFLIGHT=injected"

    section "Submitting asynchronous clean restore retry"

    SCRIPT_CONTENT="$(
        printf 'export PHD_RESULT_PREFIX=%q\n' "$RESULT_PREFIX"
        cat "$TRANSFORMED_GUEST_SCRIPT"
    )"

    az vm run-command create \
        -g "$RG_MIGRATION" \
        --vm-name "$VM_NAME" \
        --run-command-name "$RESTORE_RUN_COMMAND" \
        --location "$LOCATION" \
        --async-execution true \
        --script "$SCRIPT_CONTENT" \
        --timeout-in-seconds 7200 \
        --no-wait \
        --only-show-errors \
        -o none

    cat > "$CONFIG_FILE" <<CONFIG
RESTORE_RUN_COMMAND=$RESTORE_RUN_COMMAND
RESTORE_RESULT_PREFIX=$RESULT_PREFIX
RESTORE_STATE_ID=$STATE_ID
RESTORE_STATE_DIRECTORY=/var/lib/project-health-dashboard/$STATE_ID
RESTORE_GUEST_LOG=$RUN_LOG_PATH
RESTORE_VM_RESOURCE_GROUP=$RG_MIGRATION
RESTORE_VM_NAME=$VM_NAME
TEMP_CONTRIBUTOR_ASSIGNMENT_ID=$TEMP_CONTRIBUTOR_ASSIGNMENT_ID
TEMP_CONTRIBUTOR_SCOPE=$CONTAINER_SCOPE
CONFIG

    chmod 600 "$CONFIG_FILE"

    echo "RUN_COMMAND_ACTION=created-and-submitted"
    echo "RUN_COMMAND_NAME=$RESTORE_RUN_COMMAND"
    echo "RESULT_PREFIX=$RESULT_PREFIX"
    echo "RESTORE_STATE_ID=$STATE_ID"
    echo "RESTORE_DECISION=SUBMITTED"
    echo
    echo "Azure will continue the clean restore retry independently of Cloud Shell."
    echo
    echo "************************************************************"
    echo "POSTGRESQL CLEAN RESTORE RETRY SUBMITTED"
    echo "************************************************************"

} 2>&1 | tee "$LOG"

echo
echo "Retry submission log: $LOG"
echo "Retry state file: $CONFIG_FILE"
