#!/usr/bin/env bash
set -Eeuo pipefail

RG_SHARED="rg-project-health-dashboard-shared-global"
RG_EAST_NETWORK="rg-project-health-dashboard-test-network-eastus"
VNET_NAME="vnet-phd-test-eastus"
KV_ZONE="privatelink.vaultcore.azure.net"
ZONE_GROUP_NAME="default"
ZONE_CONFIG_NAME="keyvault"
LINK_NAME="link-phd-test-keyvault-eastus"
PRIVATE_ENDPOINT="pe-phd-test-kv-eastus"
KEY_VAULT="kv-phd-t-eus-7825cc"

BASE_DIR="$HOME/project-health-dashboard-azure"
LOG_DIR="$BASE_DIR/logs"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
LOG="$LOG_DIR/az05c2b1f-repair-eastus-keyvault-private-dns-$STAMP.log"
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
    section "AZ-05C2B1F - Repair East US Key Vault Private DNS Zone Group and VNet Link"

    echo "TIME=$(date -u -Is)"
    echo "Private DNS zone: $KV_ZONE"
    echo "East VNet: $VNET_NAME"
    echo "East Key Vault private endpoint: $PRIVATE_ENDPOINT"
    echo "Key Vault: $KEY_VAULT"

    ZONE_ID="$(az network private-dns zone show -g "$RG_SHARED" -n "$KV_ZONE" --query id -o tsv)"
    VNET_ID="$(az network vnet show -g "$RG_EAST_NETWORK" -n "$VNET_NAME" --query id -o tsv)"
    PE_NIC_ID="$(az network private-endpoint show -g "$RG_EAST_NETWORK" -n "$PRIVATE_ENDPOINT" --query 'networkInterfaces[0].id' -o tsv)"
    PE_IP="$(az network nic show --ids "$PE_NIC_ID" --query 'ipConfigurations[0].privateIPAddress' -o tsv)"
    PE_STATE="$(az network private-endpoint show -g "$RG_EAST_NETWORK" -n "$PRIVATE_ENDPOINT" --query provisioningState -o tsv)"
    PE_CONNECTION="$(az network private-endpoint show -g "$RG_EAST_NETWORK" -n "$PRIVATE_ENDPOINT" --query 'privateLinkServiceConnections[0].privateLinkServiceConnectionState.status' -o tsv)"

    [ -n "$ZONE_ID" ] || fail "Key Vault private DNS zone ID is empty."
    [ -n "$VNET_ID" ] || fail "East VNet ID is empty."
    [ -n "$PE_NIC_ID" ] || fail "Key Vault private endpoint NIC ID is empty."
    [ -n "$PE_IP" ] || fail "Key Vault private endpoint IP is empty."
    [ "$PE_STATE" = "Succeeded" ] || fail "Key Vault private endpoint state is $PE_STATE."
    [ "$PE_CONNECTION" = "Approved" ] || fail "Key Vault private endpoint connection is $PE_CONNECTION."

    echo "KEYVAULT_PRIVATE_DNS_ZONE_ID=$ZONE_ID"
    echo "EAST_VNET_ID=$VNET_ID"
    echo "KEYVAULT_PRIVATE_ENDPOINT_IP=$PE_IP"

    section "Repairing private endpoint DNS zone-group association"

    ZONE_GROUP_JSON="$WORK_DIR/zone-group.json"

    if az network private-endpoint dns-zone-group show \
        -g "$RG_EAST_NETWORK" \
        --endpoint-name "$PRIVATE_ENDPOINT" \
        -n "$ZONE_GROUP_NAME" \
        -o json > "$ZONE_GROUP_JSON" 2>/dev/null; then
        ZONE_GROUP_ACTION="existing"
    else
        az network private-endpoint dns-zone-group create \
            -g "$RG_EAST_NETWORK" \
            --endpoint-name "$PRIVATE_ENDPOINT" \
            -n "$ZONE_GROUP_NAME" \
            --zone-name "$ZONE_CONFIG_NAME" \
            --private-dns-zone "$ZONE_ID" \
            --only-show-errors \
            -o none
        ZONE_GROUP_ACTION="created-with-zone"
    fi

    az network private-endpoint dns-zone-group show \
        -g "$RG_EAST_NETWORK" \
        --endpoint-name "$PRIVATE_ENDPOINT" \
        -n "$ZONE_GROUP_NAME" \
        -o json > "$ZONE_GROUP_JSON"

    CORRECT_COUNT="$(python3 - "$ZONE_GROUP_JSON" "$ZONE_ID" <<'PY'
import json
import sys
from pathlib import Path

obj = json.loads(Path(sys.argv[1]).read_text())
wanted = sys.argv[2].lower()
configs = obj.get("privateDnsZoneConfigs") or []
print(sum(1 for item in configs if str(item.get("privateDnsZoneId") or "").lower() == wanted))
PY
)"

    if [ "$CORRECT_COUNT" = "0" ]; then
        CONFIG_NAMES="$(python3 - "$ZONE_GROUP_JSON" <<'PY'
import json
import sys
from pathlib import Path

obj = json.loads(Path(sys.argv[1]).read_text())
for item in obj.get("privateDnsZoneConfigs") or []:
    name = str(item.get("name") or "").strip()
    if name:
        print(name)
PY
)"

        while IFS= read -r config_name; do
            [ -n "$config_name" ] || continue
            az network private-endpoint dns-zone-group remove \
                -g "$RG_EAST_NETWORK" \
                --endpoint-name "$PRIVATE_ENDPOINT" \
                -n "$ZONE_GROUP_NAME" \
                --zone-name "$config_name" \
                --only-show-errors \
                -o none
        done <<< "$CONFIG_NAMES"

        az network private-endpoint dns-zone-group add \
            -g "$RG_EAST_NETWORK" \
            --endpoint-name "$PRIVATE_ENDPOINT" \
            -n "$ZONE_GROUP_NAME" \
            --zone-name "$ZONE_CONFIG_NAME" \
            --private-dns-zone "$ZONE_ID" \
            --only-show-errors \
            -o none

        ZONE_GROUP_ACTION="repaired-zone-association"
    fi

    az network private-endpoint dns-zone-group show \
        -g "$RG_EAST_NETWORK" \
        --endpoint-name "$PRIVATE_ENDPOINT" \
        -n "$ZONE_GROUP_NAME" \
        -o json > "$ZONE_GROUP_JSON"

    CORRECT_COUNT="$(python3 - "$ZONE_GROUP_JSON" "$ZONE_ID" <<'PY'
import json
import sys
from pathlib import Path

obj = json.loads(Path(sys.argv[1]).read_text())
wanted = sys.argv[2].lower()
configs = obj.get("privateDnsZoneConfigs") or []
print(sum(1 for item in configs if str(item.get("privateDnsZoneId") or "").lower() == wanted))
PY
)"

    ZONE_GROUP_STATE="$(az network private-endpoint dns-zone-group show \
        -g "$RG_EAST_NETWORK" \
        --endpoint-name "$PRIVATE_ENDPOINT" \
        -n "$ZONE_GROUP_NAME" \
        --query provisioningState \
        -o tsv)"

    [ "$CORRECT_COUNT" = "1" ] || fail "Correct Key Vault zone-group association count is $CORRECT_COUNT."
    [ "$ZONE_GROUP_STATE" = "Succeeded" ] || fail "Key Vault DNS zone-group state is $ZONE_GROUP_STATE."

    echo "KEYVAULT_DNS_ZONE_GROUP_ACTION=$ZONE_GROUP_ACTION"
    echo "KEYVAULT_DNS_ZONE_GROUP_STATE=$ZONE_GROUP_STATE"
    echo "KEYVAULT_DNS_ZONE_GROUP_ASSOCIATION=confirmed"

    section "Ensuring East VNet link to Key Vault private DNS zone"

    LINKS_JSON="$WORK_DIR/vnet-links.json"
    az network private-dns link vnet list -g "$RG_SHARED" -z "$KV_ZONE" -o json > "$LINKS_JSON"

    MATCHING_LINK="$(python3 - "$LINKS_JSON" "$VNET_ID" <<'PY'
import json
import sys
from pathlib import Path

links = json.loads(Path(sys.argv[1]).read_text())
wanted = sys.argv[2].lower()
matches = []
for link in links:
    vnet = link.get("virtualNetwork") or {}
    if str(vnet.get("id") or "").lower() == wanted:
        matches.append(str(link.get("name") or ""))
if len(matches) > 1:
    raise SystemExit("ERROR: Multiple Key Vault private DNS links reference the East VNet.")
print(matches[0] if matches else "")
PY
)"

    if [ -n "$MATCHING_LINK" ]; then
        EFFECTIVE_LINK="$MATCHING_LINK"
        LINK_ACTION="existing"
    else
        az network private-dns link vnet create \
            -g "$RG_SHARED" \
            -z "$KV_ZONE" \
            -n "$LINK_NAME" \
            -v "$VNET_ID" \
            -e false \
            --only-show-errors \
            -o none
        EFFECTIVE_LINK="$LINK_NAME"
        LINK_ACTION="created"
    fi

    LINK_STATE="$(az network private-dns link vnet show \
        -g "$RG_SHARED" \
        -z "$KV_ZONE" \
        -n "$EFFECTIVE_LINK" \
        --query virtualNetworkLinkState \
        -o tsv)"

    LINK_PROVISIONING="$(az network private-dns link vnet show \
        -g "$RG_SHARED" \
        -z "$KV_ZONE" \
        -n "$EFFECTIVE_LINK" \
        --query provisioningState \
        -o tsv)"

    [ "$LINK_STATE" = "Completed" ] || fail "Key Vault DNS VNet link state is $LINK_STATE."
    [ "$LINK_PROVISIONING" = "Succeeded" ] || fail "Key Vault DNS VNet link provisioning is $LINK_PROVISIONING."

    echo "KEYVAULT_DNS_LINK_ACTION=$LINK_ACTION"
    echo "KEYVAULT_DNS_LINK_NAME=$EFFECTIVE_LINK"
    echo "KEYVAULT_DNS_LINK_STATE=$LINK_STATE"

    section "Validating Key Vault private DNS A record"

    RECORD_IPS=""
    for attempt in 1 2 3 4 5 6; do
        RECORD_IPS="$(az network private-dns record-set a show \
            -g "$RG_SHARED" \
            -z "$KV_ZONE" \
            -n "$KEY_VAULT" \
            --query 'aRecords[].ipv4Address' \
            -o tsv 2>/dev/null || true)"

        if grep -Fxq "$PE_IP" <<< "$RECORD_IPS"; then
            break
        fi

        echo "Waiting for Key Vault private DNS A record update (attempt $attempt/6)."
        sleep 5
    done

    [ -n "$RECORD_IPS" ] || fail "Key Vault private DNS A record is missing."
    grep -Fxq "$PE_IP" <<< "$RECORD_IPS" || fail "Key Vault A record does not contain $PE_IP."

    echo "KEYVAULT_PRIVATE_DNS_RECORD=$KEY_VAULT.$KV_ZONE"
    echo "KEYVAULT_PRIVATE_DNS_RECORD_IPS=$(tr '\n' ',' <<< "$RECORD_IPS" | sed 's/,$//')"
    echo "KEYVAULT_PRIVATE_DNS_RECORD_MATCH=passed"

    section "AZ-05C2B1F complete"

    echo "No VM, database, role assignment, Key Vault secret, or storage data was modified."
    echo
    echo "************************************************************"
    echo "EASTUS KEYVAULT PRIVATE DNS ZONE GROUP AND LINK READY"
    echo "************************************************************"

} 2>&1 | tee "$LOG"

echo
echo "Key Vault DNS repair log: $LOG"
