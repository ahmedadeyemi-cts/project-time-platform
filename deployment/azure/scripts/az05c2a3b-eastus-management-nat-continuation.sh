#!/usr/bin/env bash
set -Eeuo pipefail

LOCATION="eastus"
RG_NETWORK="rg-project-health-dashboard-test-network-eastus"
VNET_NAME="vnet-phd-test-eastus"
MANAGEMENT_SUBNET="snet-management"
NAT_GATEWAY="nat-phd-test-aca-eastus"

BASE_DIR="$HOME/project-health-dashboard-azure"
LOG_DIR="$BASE_DIR/logs"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
LOG="$LOG_DIR/az05c2a3b-eastus-management-nat-$STAMP.log"

mkdir -p "$LOG_DIR"

section() {
    echo
    echo "============================================================"
    echo "$1"
    echo "============================================================"
}

{
    section "AZ-05C2A3B - Attach Existing East US NAT to Management Subnet"

    echo "TIME=$(date -u -Is)"
    echo "Location: $LOCATION"
    echo "Resource group: $RG_NETWORK"
    echo "VNet: $VNET_NAME"
    echo "Subnet: $MANAGEMENT_SUBNET"
    echo "NAT Gateway: $NAT_GATEWAY"
    echo
    echo "This continuation creates no new Azure resource."
    echo "It attaches the existing East US NAT Gateway to the existing management subnet."

    section "Validating existing NAT Gateway"

    NAT_GATEWAY_ID="$(
        az network nat gateway show \
            --resource-group "$RG_NETWORK" \
            --name "$NAT_GATEWAY" \
            --query id \
            --output tsv
    )"

    NAT_STATE="$(
        az network nat gateway show \
            --resource-group "$RG_NETWORK" \
            --name "$NAT_GATEWAY" \
            --query provisioningState \
            --output tsv
    )"

    [ -n "$NAT_GATEWAY_ID" ] || {
        echo "ERROR: Existing NAT Gateway ID was not returned."
        exit 1
    }

    [ "$NAT_STATE" = "Succeeded" ] || {
        echo "ERROR: Existing NAT Gateway is not in Succeeded state: $NAT_STATE"
        exit 1
    }

    az network nat gateway show \
        --resource-group "$RG_NETWORK" \
        --name "$NAT_GATEWAY" \
        --query '{Name:name,Location:location,State:provisioningState,PublicIPs:publicIpAddresses[].id}' \
        --output table

    section "Checking current subnet attachment"

    CURRENT_NAT_ID="$(
        az network vnet subnet show \
            --resource-group "$RG_NETWORK" \
            --vnet-name "$VNET_NAME" \
            --name "$MANAGEMENT_SUBNET" \
            --query 'natGateway.id' \
            --output tsv
    )"

    if [ "$CURRENT_NAT_ID" = "$NAT_GATEWAY_ID" ]; then
        echo "Existing attachment already correct."
    elif [ -n "$CURRENT_NAT_ID" ]; then
        echo "ERROR: Management subnet is attached to a different NAT Gateway."
        echo "Current NAT: $CURRENT_NAT_ID"
        echo "Expected NAT: $NAT_GATEWAY_ID"
        exit 1
    else
        echo "Management subnet currently has no NAT Gateway attachment."

        section "Attaching existing NAT Gateway"

        az network vnet subnet update \
            --resource-group "$RG_NETWORK" \
            --vnet-name "$VNET_NAME" \
            --name "$MANAGEMENT_SUBNET" \
            --nat-gateway "$NAT_GATEWAY_ID" \
            --only-show-errors \
            --output none
    fi

    section "Validating completed attachment"

    SUBNET_JSON="$(
        az network vnet subnet show \
            --resource-group "$RG_NETWORK" \
            --vnet-name "$VNET_NAME" \
            --name "$MANAGEMENT_SUBNET" \
            --output json
    )"

    python3 - "$NAT_GATEWAY_ID" <<'PY' <<< "$SUBNET_JSON"
import json
import sys

expected_nat = sys.argv[1]
data = json.load(sys.stdin)
actual_nat = ((data.get("natGateway") or {}).get("id") or "")
state = str(data.get("provisioningState") or "")

print(f"SUBNET_ID={data.get('id','')}")
print(f"SUBNET_PREFIX={data.get('addressPrefix') or data.get('addressPrefixes')}")
print(f"SUBNET_PROVISIONING_STATE={state}")
print(f"SUBNET_NAT_GATEWAY_ID={actual_nat}")

if state != "Succeeded":
    raise SystemExit(f"ERROR: Subnet provisioning state is {state!r}, not 'Succeeded'.")

if actual_nat.lower() != expected_nat.lower():
    raise SystemExit("ERROR: East US NAT Gateway attachment did not validate.")
PY

    section "AZ-05C2A3B completed successfully"

    echo "No new Azure resource was created."
    echo "Existing NAT Gateway attached: $NAT_GATEWAY"
    echo "Management subnet: $MANAGEMENT_SUBNET"
    echo
    echo "************************************************************"
    echo "EASTUS MANAGEMENT SUBNET NAT ATTACHMENT READY"
    echo "************************************************************"

} 2>&1 | tee "$LOG"

echo
echo "Continuation log: $LOG"
