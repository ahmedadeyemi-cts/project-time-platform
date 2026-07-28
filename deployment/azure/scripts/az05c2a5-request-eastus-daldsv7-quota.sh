#!/usr/bin/env bash
set -Eeuo pipefail

LOCATION="eastus"
RESOURCE_NAME="StandardDaldsv7Family"
REQUESTED_LIMIT="2"
RESOURCE_TYPE="dedicated"

BASE_DIR="$HOME/project-health-dashboard-azure"
LOG_DIR="$BASE_DIR/logs"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
LOG="$LOG_DIR/az05c2a5-request-eastus-daldsv7-quota-$STAMP.log"

mkdir -p "$LOG_DIR"

section() {
    echo
    echo "============================================================"
    echo "$1"
    echo "============================================================"
}

read_current_limit() {
    az quota show \
        --resource-name "$RESOURCE_NAME" \
        --scope "$SCOPE" \
        --query 'properties.limit.value' \
        --output tsv \
        2>/dev/null || true
}

is_integer() {
    [[ "${1:-}" =~ ^[0-9]+$ ]]
}

{
    section "AZ-05C2A5 - Submit East US Daldsv7 VM-Family Quota Request"

    SUBSCRIPTION_ID="$(az account show --query id --output tsv)"
    SUBSCRIPTION_NAME="$(az account show --query name --output tsv)"
    SCOPE="/subscriptions/$SUBSCRIPTION_ID/providers/Microsoft.Compute/locations/$LOCATION"

    echo "TIME=$(date -u -Is)"
    echo "Subscription: $SUBSCRIPTION_NAME"
    echo "Subscription ID: $SUBSCRIPTION_ID"
    echo "Location: $LOCATION"
    echo "Quota scope: $SCOPE"
    echo "VM family: $RESOURCE_NAME"
    echo "Requested limit: $REQUESTED_LIMIT vCPUs"
    echo
    echo "This submits the quota request and exits immediately."
    echo "It does not create a VM or another billable Azure resource."

    section "Validating Microsoft.Quota registration"

    PROVIDER_STATE="$(az provider show --namespace Microsoft.Quota --query registrationState --output tsv)"
    echo "MICROSOFT_QUOTA_PROVIDER_STATE=$PROVIDER_STATE"

    if [ "$PROVIDER_STATE" != "Registered" ]; then
        echo "ERROR: Microsoft.Quota is not registered."
        exit 1
    fi

    section "Ensuring Azure CLI quota extension"

    if az extension show --name quota --output none >/dev/null 2>&1; then
        echo "Azure CLI quota extension already installed."
    else
        az extension add \
            --name quota \
            --yes \
            --only-show-errors \
            --output none
        echo "Azure CLI quota extension installed."
    fi

    QUOTA_EXTENSION_VERSION="$(az extension show --name quota --query version --output tsv)"
    echo "AZURE_CLI_QUOTA_EXTENSION_VERSION=$QUOTA_EXTENSION_VERSION"

    section "Checking current family quota"

    CURRENT_LIMIT="$(read_current_limit)"
    echo "CURRENT_QUOTA_LIMIT=${CURRENT_LIMIT:-not-returned}"

    if is_integer "$CURRENT_LIMIT" && [ "$CURRENT_LIMIT" -ge "$REQUESTED_LIMIT" ]; then
        echo "QUOTA_REQUEST_ACTION=not-required"
        echo "QUOTA_DECISION=DEPLOYMENT_ALLOWED"
        echo "APPROVED_QUOTA_LIMIT=$CURRENT_LIMIT"
        echo "RECOMMENDED_VM_SIZE=Standard_D2alds_v7"
        echo
        echo "************************************************************"
        echo "EASTUS DALDSV7 COMPUTE QUOTA READY"
        echo "************************************************************"
        exit 0
    fi

    section "Submitting nonblocking quota request"

    REQUEST_ERROR="$(mktemp)"
    trap 'rm -f "$REQUEST_ERROR"' EXIT

    if az quota create \
        --resource-name "$RESOURCE_NAME" \
        --scope "$SCOPE" \
        --limit-object "value=$REQUESTED_LIMIT" \
        --resource-type "$RESOURCE_TYPE" \
        --no-wait true \
        --only-show-errors \
        --output none \
        2>"$REQUEST_ERROR"; then

        echo "QUOTA_REQUEST_ACTION=submitted"
    elif grep -Eiq \
        'already|existing|in.progress|pending|conflict|request.*active' \
        "$REQUEST_ERROR"; then

        echo "QUOTA_REQUEST_ACTION=already-pending"
        echo "Azure reports an existing or pending quota request."
        sed -n '1,12p' "$REQUEST_ERROR"
    else
        echo "ERROR: Azure rejected the quota request submission."
        cat "$REQUEST_ERROR"
        exit 1
    fi

    rm -f "$REQUEST_ERROR"
    trap - EXIT

    section "Immediate post-submission check"

    sleep 5
    AFTER_LIMIT="$(read_current_limit)"
    echo "POST_SUBMISSION_QUOTA_LIMIT=${AFTER_LIMIT:-not-returned}"

    if is_integer "$AFTER_LIMIT" && [ "$AFTER_LIMIT" -ge "$REQUESTED_LIMIT" ]; then
        echo "QUOTA_DECISION=DEPLOYMENT_ALLOWED"
        echo "APPROVED_QUOTA_LIMIT=$AFTER_LIMIT"
        echo "RECOMMENDED_VM_SIZE=Standard_D2alds_v7"
        echo
        echo "************************************************************"
        echo "EASTUS DALDSV7 COMPUTE QUOTA READY"
        echo "************************************************************"
    else
        echo "QUOTA_DECISION=REQUEST_PENDING_OR_MANUAL_REVIEW"
        echo "REQUESTED_QUOTA_LIMIT=$REQUESTED_LIMIT"
        echo "No VM was created."
        echo "Run az05c2a5c-check-eastus-daldsv7-quota.sh in a new Cloud Shell session."
        echo
        echo "************************************************************"
        echo "EASTUS DALDSV7 QUOTA REQUEST SUBMITTED"
        echo "************************************************************"
    fi

} 2>&1 | tee "$LOG"

echo
echo "Quota request log: $LOG"
