#!/usr/bin/env bash
set -Eeuo pipefail

LOCATION="eastus"
RG_MIGRATION="rg-project-health-dashboard-test-migration-eastus"
VM_NAME="vm-phd-test-db-migrate-eus"
PREP_RUN_COMMAND="phd-prepare-rocky10"
RESTORE_RUN_COMMAND="phd-restore-postgresql13-seed"

STORAGE_ACCOUNT="stphdtest7825cc"
STORAGE_CONTAINER="database-exports"
RESULT_PREFIX_ROOT="restore-results"

BASE_DIR="$HOME/project-health-dashboard-azure"
CONFIG_DIR="$BASE_DIR/config"
LOG_DIR="$BASE_DIR/logs"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
LOG="$LOG_DIR/az05c2b1-submit-postgresql-restore-validation-v2-$STAMP.log"
CONFIG_FILE="$CONFIG_DIR/az05c2b1-restore-validation.env"
GUEST_SCRIPT_PATH="${1:-/tmp/phd-azure-az05c2b1-guest-postgresql-restore-validation.sh}"

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
    section "AZ-05C2B1 V2 - Submit PostgreSQL Initial-Seed Restore and Validation"

    [ -s "$GUEST_SCRIPT_PATH" ] || fail "Guest restore script is missing: $GUEST_SCRIPT_PATH"
    bash -n "$GUEST_SCRIPT_PATH"

    SUBSCRIPTION_ID="$(az account show --query id --output tsv)"
    RESULT_PREFIX="$RESULT_PREFIX_ROOT/$STAMP"

    echo "TIME=$(date -u -Is)"
    echo "Subscription ID: $SUBSCRIPTION_ID"
    echo "Resource group: $RG_MIGRATION"
    echo "VM: $VM_NAME"
    echo "Result prefix: $RESULT_PREFIX"

    section "Validating prepared restore runner"

    VM_STATE="$(az vm show -g "$RG_MIGRATION" -n "$VM_NAME" --query provisioningState -o tsv)"
    POWER_STATE="$(
        az vm get-instance-view \
            -g "$RG_MIGRATION" \
            -n "$VM_NAME" \
            --query "instanceView.statuses[?starts_with(code, 'PowerState/')].displayStatus | [0]" \
            -o tsv
    )"

    echo "VM_PROVISIONING_STATE=$VM_STATE"
    echo "VM_POWER_STATE=$POWER_STATE"

    [ "$VM_STATE" = "Succeeded" ] || fail "Restore runner provisioning state is not Succeeded."
    [ "$POWER_STATE" = "VM running" ] || fail "Restore runner is not running."

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

    echo "PREP_EXECUTION_STATE=$PREP_STATE"
    echo "PREP_EXIT_CODE=$PREP_EXIT"

    [ "$PREP_STATE" = "Succeeded" ] || fail "Rocky Linux preparation did not succeed."
    [ "$PREP_EXIT" = "0" ] || fail "Rocky Linux preparation returned a nonzero exit code."
    grep -q 'PRIVATE ROCKY 10 RESTORE RUNNER PREPARATION READY' <<< "$PREP_OUTPUT" \
        || fail "Preparation success marker was not found."

    section "Assigning temporary result-upload permission"

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

    EXISTING_CONTRIBUTOR_ID="$(
        az role assignment list \
            --assignee "$VM_PRINCIPAL_ID" \
            --role "Storage Blob Data Contributor" \
            --scope "$CONTAINER_SCOPE" \
            --query '[0].id' \
            -o tsv \
            2>/dev/null || true
    )"

    TEMP_CONTRIBUTOR_CREATED="false"

    if [ -n "$EXISTING_CONTRIBUTOR_ID" ]; then
        TEMP_CONTRIBUTOR_ASSIGNMENT_ID="$EXISTING_CONTRIBUTOR_ID"
        echo "TEMP_RESULT_UPLOAD_ROLE=existing"
    else
        ROLE_ASSIGNMENT_NAME="$(
            python3 - "$VM_PRINCIPAL_ID" "$CONTAINER_SCOPE" <<'PY'
import sys
import uuid

print(uuid.uuid5(uuid.NAMESPACE_URL, sys.argv[1] + "|" + sys.argv[2] + "|phd-restore-results"))
PY
        )"

        TEMP_CONTRIBUTOR_ASSIGNMENT_ID="$(
            az role assignment create \
                --name "$ROLE_ASSIGNMENT_NAME" \
                --assignee-object-id "$VM_PRINCIPAL_ID" \
                --assignee-principal-type ServicePrincipal \
                --role "Storage Blob Data Contributor" \
                --scope "$CONTAINER_SCOPE" \
                --query id \
                -o tsv
        )"

        TEMP_CONTRIBUTOR_CREATED="true"
        echo "TEMP_RESULT_UPLOAD_ROLE=created"
    fi

    echo "TEMP_RESULT_UPLOAD_SCOPE=$CONTAINER_SCOPE"

    section "Submitting asynchronous restore managed Run Command"

    SCRIPT_CONTENT="$(
        printf 'export PHD_RESULT_PREFIX=%q\n' "$RESULT_PREFIX"
        cat "$GUEST_SCRIPT_PATH"
    )"

    if az vm run-command show \
        -g "$RG_MIGRATION" \
        --vm-name "$VM_NAME" \
        --run-command-name "$RESTORE_RUN_COMMAND" \
        --output none \
        >/dev/null 2>&1; then

        az vm run-command update \
            -g "$RG_MIGRATION" \
            --vm-name "$VM_NAME" \
            --run-command-name "$RESTORE_RUN_COMMAND" \
            --location "$LOCATION" \
            --async-execution true \
            --script "$SCRIPT_CONTENT" \
            --timeout-in-seconds 7200 \
            --no-wait \
            --only-show-errors \
            --output none

        RUN_COMMAND_ACTION="updated-and-submitted"
    else
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
            --output none

        RUN_COMMAND_ACTION="created-and-submitted"
    fi

    cat > "$CONFIG_FILE" <<CONFIG
RESTORE_RUN_COMMAND=$RESTORE_RUN_COMMAND
RESTORE_RESULT_PREFIX=$RESULT_PREFIX
RESTORE_VM_RESOURCE_GROUP=$RG_MIGRATION
RESTORE_VM_NAME=$VM_NAME
TEMP_CONTRIBUTOR_CREATED=$TEMP_CONTRIBUTOR_CREATED
TEMP_CONTRIBUTOR_ASSIGNMENT_ID=$TEMP_CONTRIBUTOR_ASSIGNMENT_ID
TEMP_CONTRIBUTOR_SCOPE=$CONTAINER_SCOPE
CONFIG

    chmod 600 "$CONFIG_FILE"

    echo "RUN_COMMAND_ACTION=$RUN_COMMAND_ACTION"
    echo "RUN_COMMAND_NAME=$RESTORE_RUN_COMMAND"
    echo "RESULT_PREFIX=$RESULT_PREFIX"
    echo "TEMP_CONTRIBUTOR_CREATED=$TEMP_CONTRIBUTOR_CREATED"
    echo "RESTORE_DECISION=SUBMITTED"
    echo
    echo "Azure will continue restore and validation independently of Cloud Shell."
    echo
    echo "************************************************************"
    echo "POSTGRESQL INITIAL SEED RESTORE VALIDATION SUBMITTED"
    echo "************************************************************"

} 2>&1 | tee "$LOG"

echo
echo "Restore submission log: $LOG"
echo "Restore state file: $CONFIG_FILE"
