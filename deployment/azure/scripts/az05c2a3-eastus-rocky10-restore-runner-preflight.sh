#!/usr/bin/env bash
set -Eeuo pipefail

LOCATION="eastus"
RG_NETWORK="rg-project-health-dashboard-test-network-eastus"
VNET_NAME="vnet-phd-test-eastus"
MANAGEMENT_SUBNET="snet-management"
NAT_GATEWAY="nat-phd-test-aca-eastus"
WEST_RG_NETWORK="rg-project-health-dashboard-test-network-westus3"
WEST_VNET_NAME="vnet-phd-test-westus3"
DNS_RG="rg-project-health-dashboard-shared-global"
DNS_ZONE="phd-test.postgres.database.azure.com"
DNS_LINK="link-phd-test-eastus"
ROCKY_PUBLISHER="resf"
ROCKY_OFFER="rockylinux-x86_64"
ROCKY_SKU="10-base"

BASE_DIR="$HOME/project-health-dashboard-azure"
LOG_DIR="$BASE_DIR/logs"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
LOG="$LOG_DIR/az05c2a3-eastus-rocky10-preflight-$STAMP.log"
WORK_DIR="$(mktemp -d)"

mkdir -p "$LOG_DIR"
trap 'rm -rf "$WORK_DIR"' EXIT

section() {
    echo
    echo "============================================================"
    echo "$1"
    echo "============================================================"
}

{
    section "AZ-05C2A3 - East US Rocky Linux 10 Restore Runner Preflight"
    echo "TIME=$(date -u -Is)"
    echo "Subscription: $(az account show --query name -o tsv)"
    echo "Subscription ID: $(az account show --query id -o tsv)"
    echo "Candidate region: $LOCATION"

    section "East US management subnet and NAT"

    SUBNET_JSON="$WORK_DIR/subnet.json"
    az network vnet subnet show \
        --resource-group "$RG_NETWORK" \
        --vnet-name "$VNET_NAME" \
        --name "$MANAGEMENT_SUBNET" \
        --output json > "$SUBNET_JSON"

    python3 - "$SUBNET_JSON" "$NAT_GATEWAY" <<'PY'
import json, sys
from pathlib import Path

path, expected_nat = sys.argv[1:]
data = json.loads(Path(path).read_text())
nat_id = ((data.get("natGateway") or {}).get("id") or "")
print(f"SUBNET_ID={data.get('id','')}")
print(f"SUBNET_PREFIX={data.get('addressPrefix') or data.get('addressPrefixes')}")
print(f"SUBNET_PROVISIONING_STATE={data.get('provisioningState','')}")
print(f"SUBNET_NAT_GATEWAY_ID={nat_id}")
if not nat_id.lower().endswith('/' + expected_nat.lower()):
    raise SystemExit(f"ERROR: Expected NAT gateway {expected_nat} is not attached to the management subnet.")
PY

    az network nat gateway show \
        --resource-group "$RG_NETWORK" \
        --name "$NAT_GATEWAY" \
        --query '{Name:name,Location:location,State:provisioningState,PublicIPs:publicIpAddresses[].id}' \
        --output table

    section "Global VNet peering"

    echo "East US peerings:"
    az network vnet peering list \
        --resource-group "$RG_NETWORK" \
        --vnet-name "$VNET_NAME" \
        --query '[].{Name:name,State:peeringState,Sync:peeringSyncLevel,RemoteVnet:remoteVirtualNetwork.id}' \
        --output table

    echo
    echo "West US 3 peerings:"
    az network vnet peering list \
        --resource-group "$WEST_RG_NETWORK" \
        --vnet-name "$WEST_VNET_NAME" \
        --query '[].{Name:name,State:peeringState,Sync:peeringSyncLevel,RemoteVnet:remoteVirtualNetwork.id}' \
        --output table

    EAST_CONNECTED="$(az network vnet peering list -g "$RG_NETWORK" --vnet-name "$VNET_NAME" --query "length([?peeringState=='Connected'])" -o tsv)"
    WEST_CONNECTED="$(az network vnet peering list -g "$WEST_RG_NETWORK" --vnet-name "$WEST_VNET_NAME" --query "length([?peeringState=='Connected'])" -o tsv)"

    if [ "${EAST_CONNECTED:-0}" -lt 1 ] || [ "${WEST_CONNECTED:-0}" -lt 1 ]; then
        echo "ERROR: Bidirectional connected VNet peering was not confirmed."
        exit 1
    fi

    echo "GLOBAL_VNET_PEERING=connected"

    section "PostgreSQL private DNS link"

    az network private-dns link vnet show \
        --resource-group "$DNS_RG" \
        --zone-name "$DNS_ZONE" \
        --name "$DNS_LINK" \
        --query '{Name:name,State:virtualNetworkLinkState,Provisioning:provisioningState,Vnet:virtualNetwork.id,Registration:registrationEnabled}' \
        --output table

    DNS_STATE="$(az network private-dns link vnet show -g "$DNS_RG" -z "$DNS_ZONE" -n "$DNS_LINK" --query virtualNetworkLinkState -o tsv)"
    [ "$DNS_STATE" = "Completed" ] || {
        echo "ERROR: East US PostgreSQL private DNS link is not completed."
        exit 1
    }

    echo "POSTGRES_PRIVATE_DNS_LINK=completed"

    section "Official Rocky Linux 10 image"

    IMAGE_VERSION="$(
        az vm image list \
            --location "$LOCATION" \
            --publisher "$ROCKY_PUBLISHER" \
            --offer "$ROCKY_OFFER" \
            --sku "$ROCKY_SKU" \
            --architecture x64 \
            --all \
            --query 'sort_by(@,&version)[-1].version' \
            --output tsv
    )"

    [ -n "$IMAGE_VERSION" ] || {
        echo "ERROR: Official RESF Rocky Linux 10 image was not found in East US."
        exit 1
    }

    IMAGE_URN="$ROCKY_PUBLISHER:$ROCKY_OFFER:$ROCKY_SKU:$IMAGE_VERSION"
    az vm image show --location "$LOCATION" --urn "$IMAGE_URN" --output none
    echo "ROCKY_IMAGE_URN=$IMAGE_URN"

    section "East US VM SKU and quota evaluation"

    az vm list-skus \
        --location "$LOCATION" \
        --resource-type virtualMachines \
        --all \
        --output json > "$WORK_DIR/skus.json"

    az vm list-usage \
        --location "$LOCATION" \
        --output json > "$WORK_DIR/usage.json"

    python3 - "$WORK_DIR/skus.json" "$WORK_DIR/usage.json" <<'PY'
import json
import re
import sys
from pathlib import Path

skus = json.loads(Path(sys.argv[1]).read_text())
usage = json.loads(Path(sys.argv[2]).read_text())

usage_by_value = {}
for item in usage:
    name = item.get("name") or {}
    value = str(name.get("value") or "")
    localized = str(name.get("localizedValue") or value)
    usage_by_value[value.lower()] = {
        "value": value,
        "localized": localized,
        "current": int(item.get("currentValue") or 0),
        "limit": int(item.get("limit") or 0),
    }

def cap(item, name):
    for entry in item.get("capabilities") or []:
        if entry.get("name") == name:
            return str(entry.get("value") or "")
    return ""

def restricted(item):
    restrictions = item.get("restrictions") or []
    return any(
        str(r.get("reasonCode") or "") == "NotAvailableForSubscription"
        or str(r.get("type") or "").lower() == "location"
        for r in restrictions
    )

rows = []
for item in skus:
    size = str(item.get("name") or "")
    family = str(item.get("family") or "")

    if restricted(item):
        continue
    if not re.match(r"^Standard_[DEF]", size):
        continue
    if "Promo" in size:
        continue

    architecture = cap(item, "CpuArchitectureType") or "unknown"
    if architecture.lower() not in {"x64", "x86_64", "amd64", "unknown"}:
        continue

    generations = cap(item, "HyperVGenerations")
    if generations and "V2" not in generations:
        continue

    try:
        vcpus = int(float(cap(item, "vCPUs") or 0))
        memory = float(cap(item, "MemoryGB") or 0)
    except ValueError:
        continue

    if not 2 <= vcpus <= 8:
        continue
    if memory < 4:
        continue

    # Rocky Linux 10 requires x86-64-v3. Prefer current D/E/F v4+ families.
    version_match = re.search(r"_v(\d+)$", size)
    version = int(version_match.group(1)) if version_match else 0
    if version and version < 4:
        continue

    quota = usage_by_value.get(family.lower())
    if not quota:
        quota_status = "quota-row-missing"
        remaining = -1
        limit = -1
        current = -1
    else:
        current = quota["current"]
        limit = quota["limit"]
        remaining = limit - current
        quota_status = "quota-ok" if remaining >= vcpus else "quota-blocked"

    rows.append({
        "size": size,
        "family": family,
        "vcpus": vcpus,
        "memory": memory,
        "arch": architecture,
        "gen": generations or "unknown",
        "current": current,
        "limit": limit,
        "remaining": remaining,
        "quota_status": quota_status,
    })

print(f"{'Size':38} {'Family':28} {'vCPU':>5} {'GB':>7} {'Arch':>8} {'Gen':>8} {'Used':>6} {'Limit':>6} {'Remain':>7} {'Status':>15}")
print("-" * 145)
for row in sorted(rows, key=lambda r: (r['quota_status'] != 'quota-ok', r['vcpus'], r['memory'], r['size'])):
    print(
        f"{row['size']:38} {row['family']:28} {row['vcpus']:>5} {row['memory']:>7.1f} "
        f"{row['arch']:>8} {row['gen']:>8} {row['current']:>6} {row['limit']:>6} "
        f"{row['remaining']:>7} {row['quota_status']:>15}"
    )

eligible = [row for row in rows if row['quota_status'] == 'quota-ok']
preferred = [
    "Standard_D2ds_v5", "Standard_D2s_v5", "Standard_D2as_v5", "Standard_D2ads_v5",
    "Standard_D2ds_v4", "Standard_D2s_v4", "Standard_D2as_v4", "Standard_D2ads_v4",
    "Standard_E2ds_v5", "Standard_E2s_v5", "Standard_E2as_v5", "Standard_E2ads_v5",
]

selected = None
for candidate in preferred:
    selected = next((row for row in eligible if row['size'] == candidate), None)
    if selected:
        break

if not selected and eligible:
    selected = sorted(eligible, key=lambda r: (r['vcpus'], r['memory'], r['size']))[0]

print()
if selected:
    print(f"RECOMMENDED_VM_SIZE={selected['size']}")
    print(f"RECOMMENDED_VM_FAMILY={selected['family']}")
    print(f"RECOMMENDED_VM_VCPUS={selected['vcpus']}")
    print(f"RECOMMENDED_VM_MEMORY_GB={selected['memory']:.1f}")
    print(f"RECOMMENDED_VM_FAMILY_QUOTA_REMAINING={selected['remaining']}")
else:
    print("RECOMMENDED_VM_SIZE=none")
    raise SystemExit("ERROR: No small x64 Gen2 D/E/F VM has sufficient family quota in East US.")
PY

    section "West US 3 partial deployment state"

    if az vm show \
        --resource-group "rg-project-health-dashboard-test-data-westus3" \
        --name "vm-phd-test-db-migrate-w3" \
        --output none >/dev/null 2>&1; then
        echo "WESTUS3_VM_STATE=exists"
    else
        echo "WESTUS3_VM_STATE=not-created"
    fi

    az network nic show \
        --resource-group "rg-project-health-dashboard-test-data-westus3" \
        --name "nic-phd-test-db-migrate-w3" \
        --query '{Name:name,PrivateIP:ipConfigurations[0].privateIPAddress,PublicIP:ipConfigurations[0].publicIPAddress.id,State:provisioningState}' \
        --output table

    section "Preflight result"
    echo "No Azure resources were created or changed by this preflight."
    echo
    echo "************************************************************"
    echo "EASTUS ROCKY 10 RESTORE RUNNER PREFLIGHT COMPLETE"
    echo "************************************************************"

} 2>&1 | tee "$LOG"

echo
echo "Preflight log: $LOG"
