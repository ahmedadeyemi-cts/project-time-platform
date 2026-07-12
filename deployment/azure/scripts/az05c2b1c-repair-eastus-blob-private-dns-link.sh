#!/usr/bin/env bash
set -Eeuo pipefail

RG_SHARED="rg-project-health-dashboard-shared-global"
RG_EAST_NETWORK="rg-project-health-dashboard-test-network-eastus"
VNET_NAME="vnet-phd-test-eastus"
BLOB_ZONE="privatelink.blob.core.windows.net"
LINK_NAME="link-phd-test-blob-eastus"
PRIVATE_ENDPOINT="pe-phd-test-blob-eastus"
STORAGE_ACCOUNT="stphdtest7825cc"

BASE_DIR="$HOME/project-health-dashboard-azure"
LOG_DIR="$BASE_DIR/logs"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
LOG="$LOG_DIR/az05c2b1c-repair-eastus-blob-private-dns-link-$STAMP.log"
WORK_DIR="$(mktemp -d)"

mkdir -p "$LOG_DIR"
trap 'rm -rf "$WORK_DIR"' EXIT

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
    section "AZ-05C2B1C - Repair East US Blob Private DNS VNet Link"

    echo "TIME=$(date -u -Is)"
    echo "Private DNS zone: $BLOB_ZONE"
    echo "East VNet: $VNET_NAME"
    echo "East Blob private endpoint: $PRIVATE_ENDPOINT"
    echo "Storage account: $STORAGE_ACCOUNT"

    section "Validating existing Azure resources"

    az network private-dns zone show \
        --resource-group "$RG_SHARED" \
        --name "$BLOB_ZONE" \
        --output none

    VNET_ID="$(
        az network vnet show \
            --resource-group "$RG_EAST_NETWORK" \
            --name "$VNET_NAME" \
            --query id \
            --output tsv
    )"

    [ -n "$VNET_ID" ] || fail "East US VNet ID is empty."

    PE_STATE="$(
        az network private-endpoint show \
            --resource-group "$RG_EAST_NETWORK" \
            --name "$PRIVATE_ENDPOINT" \
            --query provisioningState \
            --output tsv
    )"

    PE_CONNECTION_STATE="$(
        az network private-endpoint show \
            --resource-group "$RG_EAST_NETWORK" \
            --name "$PRIVATE_ENDPOINT" \
            --query 'privateLinkServiceConnections[0].privateLinkServiceConnectionState.status' \
            --output tsv
    )"

    PE_NIC_ID="$(
        az network private-endpoint show \
            --resource-group "$RG_EAST_NETWORK" \
            --name "$PRIVATE_ENDPOINT" \
            --query 'networkInterfaces[0].id' \
            --output tsv
    )"

    [ "$PE_STATE" = "Succeeded" ] || fail "Blob private endpoint provisioning state is $PE_STATE."
    [ "$PE_CONNECTION_STATE" = "Approved" ] || fail "Blob private endpoint connection state is $PE_CONNECTION_STATE."
    [ -n "$PE_NIC_ID" ] || fail "Blob private endpoint NIC ID is empty."

    PE_IP="$(
        az network nic show \
            --ids "$PE_NIC_ID" \
            --query 'ipConfigurations[0].privateIPAddress' \
            --output tsv
    )"

    [ -n "$PE_IP" ] || fail "Blob private endpoint private IP is empty."

    echo "EAST_VNET_ID=$VNET_ID"
    echo "BLOB_PRIVATE_ENDPOINT_STATE=$PE_STATE"
    echo "BLOB_PRIVATE_ENDPOINT_CONNECTION=$PE_CONNECTION_STATE"
    echo "BLOB_PRIVATE_ENDPOINT_IP=$PE_IP"

    section "Checking Blob private DNS zone group"

    ZONE_GROUP_IDS="$(
        az network private-endpoint dns-zone-group show \
            --resource-group "$RG_EAST_NETWORK" \
            --endpoint-name "$PRIVATE_ENDPOINT" \
            --name default \
            --query 'privateDnsZoneConfigs[].privateDnsZoneId' \
            --output tsv
    )"

    grep -Fqi "/privateDnsZones/$BLOB_ZONE" <<< "$ZONE_GROUP_IDS" \
        || fail "East Blob private endpoint DNS zone group does not reference $BLOB_ZONE."

    echo "BLOB_PRIVATE_DNS_ZONE_GROUP=confirmed"

    section "Ensuring East VNet is linked to Blob private DNS zone"

    LINKS_JSON="$WORK_DIR/blob-zone-links.json"

    az network private-dns link vnet list \
        --resource-group "$RG_SHARED" \
        --zone-name "$BLOB_ZONE" \
        --output json > "$LINKS_JSON"

    MATCHING_LINK_NAME="$(
        python3 - "$LINKS_JSON" "$VNET_ID" <<'PY'
import json
import sys
from pathlib import Path

links = json.loads(Path(sys.argv[1]).read_text())
wanted = sys.argv[2].lower()
matches = []

for link in links:
    network = link.get("virtualNetwork") or {}
    if str(network.get("id") or "").lower() == wanted:
        matches.append(str(link.get("name") or ""))

if len(matches) > 1:
    raise SystemExit("ERROR: Multiple Blob private DNS links reference the East VNet.")

print(matches[0] if matches else "")
PY
    )"

    if [ -n "$MATCHING_LINK_NAME" ]; then
        LINK_ACTION="existing"
        EFFECTIVE_LINK_NAME="$MATCHING_LINK_NAME"
    else
        az network private-dns link vnet create \
            --resource-group "$RG_SHARED" \
            --zone-name "$BLOB_ZONE" \
            --name "$LINK_NAME" \
            --virtual-network "$VNET_ID" \
            --registration-enabled false \
            --only-show-errors \
            --output none

        LINK_ACTION="created"
        EFFECTIVE_LINK_NAME="$LINK_NAME"
    fi

    LINK_PROVISIONING_STATE="$(
        az network private-dns link vnet show \
            --resource-group "$RG_SHARED" \
            --zone-name "$BLOB_ZONE" \
            --name "$EFFECTIVE_LINK_NAME" \
            --query provisioningState \
            --output tsv
    )"

    LINK_STATE="$(
        az network private-dns link vnet show \
            --resource-group "$RG_SHARED" \
            --zone-name "$BLOB_ZONE" \
            --name "$EFFECTIVE_LINK_NAME" \
            --query virtualNetworkLinkState \
            --output tsv
    )"

    [ "$LINK_PROVISIONING_STATE" = "Succeeded" ] \
        || fail "Blob private DNS VNet link provisioning state is $LINK_PROVISIONING_STATE."

    [ "$LINK_STATE" = "Completed" ] \
        || fail "Blob private DNS VNet link state is $LINK_STATE."

    echo "BLOB_DNS_LINK_ACTION=$LINK_ACTION"
    echo "BLOB_DNS_LINK_NAME=$EFFECTIVE_LINK_NAME"
    echo "BLOB_DNS_LINK_PROVISIONING=$LINK_PROVISIONING_STATE"
    echo "BLOB_DNS_LINK_STATE=$LINK_STATE"

    section "Validating storage private DNS A record"

    RECORD_IPS="$(
        az network private-dns record-set a show \
            --resource-group "$RG_SHARED" \
            --zone-name "$BLOB_ZONE" \
            --name "$STORAGE_ACCOUNT" \
            --query 'aRecords[].ipv4Address' \
            --output tsv
    )"

    [ -n "$RECORD_IPS" ] || fail "Storage private DNS A record is missing."

    grep -Fxq "$PE_IP" <<< "$RECORD_IPS" \
        || fail "Storage private DNS A record does not contain the East private endpoint IP $PE_IP."

    echo "BLOB_PRIVATE_DNS_RECORD=$STORAGE_ACCOUNT.$BLOB_ZONE"
    echo "BLOB_PRIVATE_DNS_RECORD_IPS=$(tr '\n' ',' <<< "$RECORD_IPS" | sed 's/,$//')"
    echo "BLOB_PRIVATE_DNS_RECORD_MATCH=passed"

    section "AZ-05C2B1C complete"

    echo "The East US VNet can now use the Blob private DNS zone."
    echo "No VM, database, role assignment, private endpoint, or storage data was modified."
    echo
    echo "************************************************************"
    echo "EASTUS BLOB PRIVATE DNS LINK READY"
    echo "************************************************************"

} 2>&1 | tee "$LOG"

echo
echo "DNS repair log: $LOG"
