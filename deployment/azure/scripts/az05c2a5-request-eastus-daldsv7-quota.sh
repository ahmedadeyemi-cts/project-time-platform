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

quota_limit_from_json() {
    python3 - <<'PY'
import json
import sys

data = json.load(sys.stdin)
props = data.get("properties") or {}
limit = props.get("limit") or {}
value = limit.get("value")
print("" if value is None else value)
PY
}

{
    section "AZ-05C2A5 - Request East US Daldsv7 VM-Family Quota"

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
    echo "This submits an Azure quota request."
    echo "It does not create a VM or another billable Azure resource."

    section "Validating Microsoft.Quota registration"

    PROVIDER_STATE="$(az provider show --namespace Microsoft.Quota --query registrationState --output tsv)"
    echo "MICROSOFT_QUOTA_PROVIDER_STATE=$PROVIDER_STATE"

    if [ "$PROVIDER_STATE" != "Registered" ]; then
        echo "ERROR: Microsoft.Quota is not registered."
        exit 1
    fi

    section "Ensuring Azure CLI quota extension"

    az extension add \
        --name quota \
        --upgrade \
        --yes \
        --only-show-errors \
        --output none

    QUOTA_EXTENSION_VERSION="$(az extension show --name quota --query version --output tsv)"
    echo "AZURE_CLI_QUOTA_EXTENSION_VERSION=$QUOTA_EXTENSION_VERSION"

    section "Checking current family quota"

    CURRENT_JSON="$(mktemp)"
    CURRENT_ERROR="$(mktemp)"

    if az quota show \
        --resource-name "$RESOURCE_NAME" \
        --scope "$SCOPE" \
        --output json > "$CURRENT_JSON" 2> "$CURRENT_ERROR"; then

        CURRENT_LIMIT="$(quota_limit_from_json < "$CURRENT_JSON")"
        echo "CURRENT_QUOTA_LIMIT=${CURRENT_LIMIT:-unknown}"
    else
        CURRENT_LIMIT=""
        echo "CURRENT_QUOTA_LIMIT=not-returned"
        if [ -s "$CURRENT_ERROR" ]; then
            echo "Initial quota lookup message:"
            sed -n '1,12p' "$CURRENT_ERROR"
        fi
    fi

    rm -f "$CURRENT_JSON" "$CURRENT_ERROR"

    if [ -n "$CURRENT_LIMIT" ] && [ "$CURRENT_LIMIT" -ge "$REQUESTED_LIMIT" ]; then
        echo "QUOTA_REQUEST_ACTION=not-required"
        echo "QUOTA_DECISION=DEPLOYMENT_ALLOWED"
        echo "APPROVED_QUOTA_LIMIT=$CURRENT_LIMIT"
        echo
        echo "************************************************************"
        echo "EASTUS DALDSV7 COMPUTE QUOTA READY"
        echo "************************************************************"
        exit 0
    fi

    section "Submitting quota request"

    REQUEST_OUTPUT="$(mktemp)"
    REQUEST_ERROR="$(mktemp)"

    if az quota create \
        --resource-name "$RESOURCE_NAME" \
        --scope "$SCOPE" \
        --limit-object "value=$REQUESTED_LIMIT" \
        --resource-type "$RESOURCE_TYPE" \
        --no-wait true \
        --only-show-errors \
        --output json > "$REQUEST_OUTPUT" 2> "$REQUEST_ERROR"; then

        echo "QUOTA_REQUEST_ACTION=submitted"
        if [ -s "$REQUEST_OUTPUT" ]; then
            python3 - "$REQUEST_OUTPUT" <<'PY'
import json
import sys
from pathlib import Path

path = Path(sys.argv[1])
try:
    data = json.loads(path.read_text())
except Exception:
    print("QUOTA_REQUEST_RESPONSE=accepted")
    raise SystemExit(0)

print(f"QUOTA_REQUEST_RESOURCE_NAME={data.get('name') or ''}")
props = data.get("properties") or {}
limit = props.get("limit") or {}
if limit.get("value") is not None:
    print(f"QUOTA_REQUESTED_LIMIT={limit.get('value')}")
PY
        fi
    else
        echo "ERROR: Azure rejected the quota request submission."
        if [ -s "$REQUEST_ERROR" ]; then
            cat "$REQUEST_ERROR"
        fi
        rm -f "$REQUEST_OUTPUT" "$REQUEST_ERROR"
        exit 1
    fi

    rm -f "$REQUEST_OUTPUT" "$REQUEST_ERROR"

    section "Waiting for quota approval"

    APPROVED_LIMIT=""

    for attempt in $(seq 1 40); do
        SHOW_JSON="$(mktemp)"
        SHOW_ERROR="$(mktemp)"

        if az quota show \
            --resource-name "$RESOURCE_NAME" \
            --scope "$SCOPE" \
            --output json > "$SHOW_JSON" 2> "$SHOW_ERROR"; then

            APPROVED_LIMIT="$(quota_limit_from_json < "$SHOW_JSON")"
            echo "ATTEMPT=$attempt QUOTA_LIMIT=${APPROVED_LIMIT:-unknown}"

            rm -f "$SHOW_JSON" "$SHOW_ERROR"

            if [ -n "$APPROVED_LIMIT" ] && [ "$APPROVED_LIMIT" -ge "$REQUESTED_LIMIT" ]; then
                break
            fi
        else
            echo "ATTEMPT=$attempt QUOTA_LIMIT=pending"
            rm -f "$SHOW_JSON" "$SHOW_ERROR"
        fi

        sleep 30
    done

    section "Quota request result"

    if [ -n "$APPROVED_LIMIT" ] && [ "$APPROVED_LIMIT" -ge "$REQUESTED_LIMIT" ]; then
        echo "QUOTA_DECISION=DEPLOYMENT_ALLOWED"
        echo "APPROVED_QUOTA_LIMIT=$APPROVED_LIMIT"
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
        echo "************************************************************"
        echo "EASTUS DALDSV7 QUOTA REQUEST SUBMITTED"
        echo "************************************************************"
    fi

} 2>&1 | tee "$LOG"

echo
echo "Quota request log: $LOG"
