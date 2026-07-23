#!/usr/bin/env bash
set -Eeuo pipefail

LOCATION="eastus"
RESOURCE_NAME="StandardDaldsv7Family"
REQUESTED_LIMIT="2"

BASE_DIR="$HOME/project-health-dashboard-azure"
LOG_DIR="$BASE_DIR/logs"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
LOG="$LOG_DIR/az05c2a5c-check-eastus-daldsv7-quota-$STAMP.log"

mkdir -p "$LOG_DIR"

section() {
    echo
    echo "============================================================"
    echo "$1"
    echo "============================================================"
}

is_integer() {
    [[ "${1:-}" =~ ^[0-9]+$ ]]
}

{
    section "AZ-05C2A5C - Check East US Daldsv7 VM-Family Quota"

    SUBSCRIPTION_ID="$(az account show --query id --output tsv)"
    SUBSCRIPTION_NAME="$(az account show --query name --output tsv)"
    SCOPE="/subscriptions/$SUBSCRIPTION_ID/providers/Microsoft.Compute/locations/$LOCATION"

    echo "TIME=$(date -u -Is)"
    echo "Subscription: $SUBSCRIPTION_NAME"
    echo "Subscription ID: $SUBSCRIPTION_ID"
    echo "Location: $LOCATION"
    echo "Quota scope: $SCOPE"
    echo "VM family: $RESOURCE_NAME"
    echo "Required limit: $REQUESTED_LIMIT vCPUs"
    echo
    echo "This check is read-only and normally completes in seconds."

    section "Validating prerequisites"

    PROVIDER_STATE="$(az provider show --namespace Microsoft.Quota --query registrationState --output tsv)"
    echo "MICROSOFT_QUOTA_PROVIDER_STATE=$PROVIDER_STATE"

    if [ "$PROVIDER_STATE" != "Registered" ]; then
        echo "ERROR: Microsoft.Quota is not registered."
        exit 1
    fi

    if ! az extension show --name quota --output none >/dev/null 2>&1; then
        az extension add \
            --name quota \
            --yes \
            --only-show-errors \
            --output none
    fi

    echo "AZURE_CLI_QUOTA_EXTENSION_VERSION=$(az extension show --name quota --query version --output tsv)"

    section "Reading current approved quota"

    CURRENT_LIMIT="$(
        az quota show \
            --resource-name "$RESOURCE_NAME" \
            --scope "$SCOPE" \
            --query 'properties.limit.value' \
            --output tsv \
            2>/dev/null || true
    )"

    echo "CURRENT_QUOTA_LIMIT=${CURRENT_LIMIT:-not-returned}"

    if is_integer "$CURRENT_LIMIT" && [ "$CURRENT_LIMIT" -ge "$REQUESTED_LIMIT" ]; then
        echo "QUOTA_DECISION=DEPLOYMENT_ALLOWED"
        echo "APPROVED_QUOTA_LIMIT=$CURRENT_LIMIT"
        echo "RECOMMENDED_VM_SIZE=Standard_D2alds_v7"
        echo
        echo "************************************************************"
        echo "EASTUS DALDSV7 COMPUTE QUOTA READY"
        echo "************************************************************"
    else
        echo "QUOTA_DECISION=REQUEST_PENDING_OR_MANUAL_REVIEW"
        echo "REQUESTED_QUOTA_LIMIT=$REQUESTED_LIMIT"
        echo "No VM was created."
        echo
        echo "Recent quota-request status, when available:"
        az quota request status list \
            --scope "$SCOPE" \
            --output table \
            2>/dev/null | tail -n 12 || true
        echo
        echo "************************************************************"
        echo "EASTUS DALDSV7 QUOTA NOT READY"
        echo "************************************************************"
    fi

} 2>&1 | tee "$LOG"

echo
echo "Quota status log: $LOG"
