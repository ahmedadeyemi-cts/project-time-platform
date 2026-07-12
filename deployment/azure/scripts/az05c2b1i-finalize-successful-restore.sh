#!/usr/bin/env bash
set -Eeuo pipefail

LOCATION="eastus"
BASE_DIR="$HOME/project-health-dashboard-azure"
CONFIG_FILE="$BASE_DIR/config/az05c2b1h-restore-retry.env"
LOG_DIR="$BASE_DIR/logs"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
STAMP_LOWER="${STAMP,,}"
LOG="$LOG_DIR/az05c2b1i-finalize-successful-restore-$STAMP.log"
PROBE_RUN_COMMAND="phdrestoreevidence${STAMP_LOWER}"
STORAGE_ACCOUNT="stphdtest7825cc"
STORAGE_CONTAINER="database-exports"
SUCCESS_MARKER="POSTGRESQL INITIAL SEED RESTORE RETRY VALIDATION PASSED"

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
    section "AZ-05C2B1I - Finalize Successful PostgreSQL Restore"

    [ -s "$CONFIG_FILE" ] || fail "Restore retry state file is missing: $CONFIG_FILE"

    # shellcheck disable=SC1090
    source "$CONFIG_FILE"

    echo "TIME=$(date -u -Is)"
    echo "Resource group: $RESTORE_VM_RESOURCE_GROUP"
    echo "VM: $RESTORE_VM_NAME"
    echo "Restore Run Command: $RESTORE_RUN_COMMAND"
    echo "Result prefix: $RESTORE_RESULT_PREFIX"
    echo "Evidence probe: $PROBE_RUN_COMMAND"

    section "Confirming terminal successful restore"

    RESTORE_JSON="$(
        az vm run-command show \
            -g "$RESTORE_VM_RESOURCE_GROUP" \
            --vm-name "$RESTORE_VM_NAME" \
            --run-command-name "$RESTORE_RUN_COMMAND" \
            --instance-view \
            -o json
    )"

    RESTORE_STATE="$(jq -r '.instanceView.executionState // empty' <<< "$RESTORE_JSON")"
    RESTORE_EXIT="$(jq -r '.instanceView.exitCode // empty' <<< "$RESTORE_JSON")"
    RESTORE_OUTPUT="$(jq -r '.instanceView.output // empty' <<< "$RESTORE_JSON")"

    echo "RESTORE_EXECUTION_STATE=$RESTORE_STATE"
    echo "RESTORE_EXIT_CODE=$RESTORE_EXIT"

    [ "$RESTORE_STATE" = "Succeeded" ] || fail "Restore retry is not Succeeded."
    [ "$RESTORE_EXIT" = "0" ] || fail "Restore retry exit code is not zero."
    grep -Fq "$SUCCESS_MARKER" <<< "$RESTORE_OUTPUT" \
        || fail "Restore retry success marker was not found."

    echo "RESTORE_SUCCESS_MARKER=confirmed"

    section "Confirming runner is available for final evidence verification"

    VM_STATE="$(az vm show -g "$RESTORE_VM_RESOURCE_GROUP" -n "$RESTORE_VM_NAME" --query provisioningState -o tsv)"
    POWER_STATE="$(
        az vm get-instance-view \
            -g "$RESTORE_VM_RESOURCE_GROUP" \
            -n "$RESTORE_VM_NAME" \
            --query "instanceView.statuses[?starts_with(code, 'PowerState/')].displayStatus | [0]" \
            -o tsv
    )"

    echo "VM_PROVISIONING_STATE=$VM_STATE"
    echo "VM_POWER_STATE=$POWER_STATE"

    [ "$VM_STATE" = "Succeeded" ] || fail "Restore runner provisioning state is not Succeeded."
    [ "$POWER_STATE" = "VM running" ] || fail "Restore runner is not running."

    section "Verifying uploaded nonsecret restore evidence"

    GUEST_SCRIPT="$(cat <<'GUEST'
#!/usr/bin/env bash
set -Eeuo pipefail

STORAGE_ACCOUNT="stphdtest7825cc"
STORAGE_CONTAINER="database-exports"
RESULT_PREFIX="${PHD_RESULT_PREFIX:?PHD_RESULT_PREFIX is required}"
WORK_DIR="$(mktemp -d)"
LIST_XML="$WORK_DIR/list.xml"
SUMMARY_FILE="$WORK_DIR/validation-summary.txt"
COMPARISON_FILE="$WORK_DIR/validation-comparison.json"
trap 'rm -rf "$WORK_DIR"' EXIT

TOKEN_JSON="$(
    curl -fsS \
        --max-time 15 \
        -H Metadata:true \
        'http://169.254.169.254/metadata/identity/oauth2/token?api-version=2018-02-01&resource=https%3A%2F%2Fstorage.azure.com%2F'
)"
ACCESS_TOKEN="$(jq -r '.access_token // empty' <<< "$TOKEN_JSON")"
unset TOKEN_JSON

[ -n "$ACCESS_TOKEN" ] || {
    echo 'EVIDENCE_MANAGED_IDENTITY_TOKEN=failed'
    exit 1
}

echo 'EVIDENCE_MANAGED_IDENTITY_TOKEN=success'
REQUEST_DATE="$(LC_ALL=C date -u '+%a, %d %b %Y %H:%M:%S GMT')"
BLOB_HOST="${STORAGE_ACCOUNT}.blob.core.windows.net"

HTTP_CODE="$(
    curl -sS \
        --max-time 30 \
        --output "$LIST_XML" \
        --write-out '%{http_code}' \
        --get \
        -H "Authorization: Bearer $ACCESS_TOKEN" \
        -H "x-ms-date: $REQUEST_DATE" \
        -H 'x-ms-version: 2023-11-03' \
        "https://${BLOB_HOST}/${STORAGE_CONTAINER}" \
        --data 'restype=container' \
        --data 'comp=list' \
        --data-urlencode "prefix=${RESULT_PREFIX}/"
)"

echo "EVIDENCE_LIST_HTTP_STATUS=$HTTP_CODE"
[ "$HTTP_CODE" = "200" ] || exit 1

BLOB_COUNT="$(grep -o '<Blob>' "$LIST_XML" | wc -l | tr -d ' ')"
echo "EVIDENCE_BLOB_COUNT=$BLOB_COUNT"

REQUIRED_FILES=(
    checksum-verification.txt
    restore-validation.log
    result-manifest.sha256
    target-connection.txt
    target-extensions.csv
    target-pg-restore-toc.txt
    target-row-counts.csv
    target-schemas.csv
    target-sequences.csv
    target-tables.csv
    validation-comparison.json
    validation-summary.txt
)

for required_file in "${REQUIRED_FILES[@]}"; do
    grep -Fq "<Name>${RESULT_PREFIX}/${required_file}</Name>" "$LIST_XML" || {
        echo "EVIDENCE_REQUIRED_FILE_MISSING=$required_file"
        exit 1
    }
done

echo "EVIDENCE_REQUIRED_FILES=${#REQUIRED_FILES[@]}"
[ "$BLOB_COUNT" = "${#REQUIRED_FILES[@]}" ] || {
    echo "ERROR: Evidence Blob count does not equal the required file count."
    exit 1
}

curl -fsS \
    --max-time 30 \
    -H "Authorization: Bearer $ACCESS_TOKEN" \
    -H "x-ms-date: $REQUEST_DATE" \
    -H 'x-ms-version: 2023-11-03' \
    "https://${BLOB_HOST}/${STORAGE_CONTAINER}/${RESULT_PREFIX}/validation-summary.txt" \
    -o "$SUMMARY_FILE"

curl -fsS \
    --max-time 30 \
    -H "Authorization: Bearer $ACCESS_TOKEN" \
    -H "x-ms-date: $REQUEST_DATE" \
    -H 'x-ms-version: 2023-11-03' \
    "https://${BLOB_HOST}/${STORAGE_CONTAINER}/${RESULT_PREFIX}/validation-comparison.json" \
    -o "$COMPARISON_FILE"

unset ACCESS_TOKEN

grep -Fxq 'STATUS=PASSED' "$SUMMARY_FILE" || {
    echo 'EVIDENCE_VALIDATION_SUMMARY=failed'
    exit 1
}

COMPARISON_STATUS="$(jq -r '.status // empty' "$COMPARISON_FILE")"
COMPARISON_ERRORS="$(jq -r '(.errors // []) | length' "$COMPARISON_FILE")"
COMPARISON_WARNINGS="$(jq -r '(.warnings // []) | length' "$COMPARISON_FILE")"
SCHEMA_COUNT="$(jq -r '.counts.schemas // empty' "$COMPARISON_FILE")"
TABLE_COUNT="$(jq -r '.counts.tables // empty' "$COMPARISON_FILE")"
EXTENSION_COUNT="$(jq -r '.counts.extensions // empty' "$COMPARISON_FILE")"
SEQUENCE_COUNT="$(jq -r '.counts.sequences // empty' "$COMPARISON_FILE")"

[ "$COMPARISON_STATUS" = "PASSED" ] || exit 1
[ "$COMPARISON_ERRORS" = "0" ] || exit 1

echo 'EVIDENCE_VALIDATION_SUMMARY=passed'
echo "EVIDENCE_COMPARISON_STATUS=$COMPARISON_STATUS"
echo "EVIDENCE_COMPARISON_ERRORS=$COMPARISON_ERRORS"
echo "EVIDENCE_COMPARISON_WARNINGS=$COMPARISON_WARNINGS"
echo "EVIDENCE_SCHEMA_COUNT=$SCHEMA_COUNT"
echo "EVIDENCE_TABLE_COUNT=$TABLE_COUNT"
echo "EVIDENCE_EXTENSION_COUNT=$EXTENSION_COUNT"
echo "EVIDENCE_SEQUENCE_COUNT=$SEQUENCE_COUNT"
echo 'POSTGRESQL RESTORE EVIDENCE VERIFIED'
GUEST
)"

    SCRIPT_CONTENT="$(
        printf 'export PHD_RESULT_PREFIX=%q\n' "$RESTORE_RESULT_PREFIX"
        printf '%s\n' "$GUEST_SCRIPT"
    )"

    az vm run-command create \
        -g "$RESTORE_VM_RESOURCE_GROUP" \
        --vm-name "$RESTORE_VM_NAME" \
        --run-command-name "$PROBE_RUN_COMMAND" \
        --location "$LOCATION" \
        --async-execution false \
        --script "$SCRIPT_CONTENT" \
        --timeout-in-seconds 300 \
        --only-show-errors \
        -o none

    PROBE_JSON="$(
        az vm run-command show \
            -g "$RESTORE_VM_RESOURCE_GROUP" \
            --vm-name "$RESTORE_VM_NAME" \
            --run-command-name "$PROBE_RUN_COMMAND" \
            --instance-view \
            -o json
    )"

    PROBE_STATE="$(jq -r '.instanceView.executionState // empty' <<< "$PROBE_JSON")"
    PROBE_EXIT="$(jq -r '.instanceView.exitCode // empty' <<< "$PROBE_JSON")"
    PROBE_OUTPUT="$(jq -r '.instanceView.output // empty' <<< "$PROBE_JSON")"
    PROBE_ERROR="$(jq -r '.instanceView.error // empty' <<< "$PROBE_JSON")"

    echo "$PROBE_OUTPUT"
    [ -z "$PROBE_ERROR" ] || echo "EVIDENCE_PROBE_ERROR=$PROBE_ERROR"

    echo "EVIDENCE_PROBE_STATE=$PROBE_STATE"
    echo "EVIDENCE_PROBE_EXIT_CODE=$PROBE_EXIT"

    [ "$PROBE_STATE" = "Succeeded" ] || fail "Evidence verification Run Command did not succeed."
    [ "$PROBE_EXIT" = "0" ] || fail "Evidence verification returned a nonzero exit code."
    grep -Fq 'POSTGRESQL RESTORE EVIDENCE VERIFIED' <<< "$PROBE_OUTPUT" \
        || fail "Evidence verification marker was not found."

    section "Removing temporary result-upload role"

    VM_PRINCIPAL_ID="$(az vm identity show -g "$RESTORE_VM_RESOURCE_GROUP" -n "$RESTORE_VM_NAME" --query principalId -o tsv)"
    [ -n "$VM_PRINCIPAL_ID" ] || fail "VM managed identity principal ID is empty."

    ROLE_MATCH_COUNT="$(
        az role assignment list \
            --assignee "$VM_PRINCIPAL_ID" \
            --role "Storage Blob Data Contributor" \
            --scope "$TEMP_CONTRIBUTOR_SCOPE" \
            --query "[?id=='$TEMP_CONTRIBUTOR_ASSIGNMENT_ID'] | length(@)" \
            -o tsv \
            2>/dev/null || echo 0
    )"

    if [ "$ROLE_MATCH_COUNT" = "1" ]; then
        az role assignment delete \
            --ids "$TEMP_CONTRIBUTOR_ASSIGNMENT_ID" \
            --only-show-errors
        echo "TEMP_RESULT_UPLOAD_ROLE_ACTION=deleted"
    else
        echo "TEMP_RESULT_UPLOAD_ROLE_ACTION=already-absent"
    fi

    REMAINING_CONTRIBUTOR_COUNT="$(
        az role assignment list \
            --assignee "$VM_PRINCIPAL_ID" \
            --role "Storage Blob Data Contributor" \
            --scope "$TEMP_CONTRIBUTOR_SCOPE" \
            --query 'length(@)' \
            -o tsv \
            2>/dev/null || echo 0
    )"

    echo "TEMP_RESULT_UPLOAD_ROLE_REMAINING=$REMAINING_CONTRIBUTOR_COUNT"
    [ "$REMAINING_CONTRIBUTOR_COUNT" = "0" ] \
        || fail "Temporary Blob Contributor role still exists."

    section "Submitting restore-runner deallocation"

    az vm deallocate \
        -g "$RESTORE_VM_RESOURCE_GROUP" \
        -n "$RESTORE_VM_NAME" \
        --no-wait

    echo "VM_DEALLOCATION_ACTION=submitted"
    echo "VM=$RESTORE_VM_NAME"
    echo "RESOURCE_GROUP=$RESTORE_VM_RESOURCE_GROUP"
    echo
    echo "************************************************************"
    echo "POSTGRESQL RESTORE FINALIZATION AND VM DEALLOCATION SUBMITTED"
    echo "************************************************************"

} 2>&1 | tee "$LOG"

echo
echo "Finalization log: $LOG"
