#!/usr/bin/env bash
set -Eeuo pipefail

PRODUCT_NAME="Project Health Dashboard"
ENVIRONMENT="test"
LOCATION="eastus"

RG_MIGRATION="rg-project-health-dashboard-test-migration-eastus"
RG_NETWORK="rg-project-health-dashboard-test-network-eastus"
VNET_NAME="vnet-phd-test-eastus"
SUBNET_NAME="snet-management"
NAT_GATEWAY="nat-phd-test-aca-eastus"

WEST_RG_NETWORK="rg-project-health-dashboard-test-network-westus3"
WEST_VNET_NAME="vnet-phd-test-westus3"
DNS_RG="rg-project-health-dashboard-shared-global"
DNS_ZONE="phd-test.postgres.database.azure.com"
DNS_LINK="link-phd-test-eastus"

VM_NAME="vm-phd-test-db-migrate-eus"
NIC_NAME="nic-phd-test-db-migrate-eus"
OS_DISK_NAME="osdisk-phd-test-db-migrate-eus"
ADMIN_USERNAME="azureuser"
VM_SIZE="Standard_D2alds_v7"
VM_FAMILY="StandardDaldsv7Family"
REQUIRED_VCPUS="2"
IMAGE_URN="resf:rockylinux-x86_64:10-base:10.2.20260525"

BASE_DIR="$HOME/project-health-dashboard-azure"
LOG_DIR="$BASE_DIR/logs"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
LOG="$LOG_DIR/az05c2a9-submit-eastus-rocky10-runner-$STAMP.log"
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
    section "AZ-05C2A9 - Submit East US Rocky Linux 10 Restore Runner"

    SUBSCRIPTION_ID="$(az account show --query id --output tsv)"
    SUBSCRIPTION_NAME="$(az account show --query name --output tsv)"

    echo "TIME=$(date -u -Is)"
    echo "Subscription: $SUBSCRIPTION_NAME"
    echo "Subscription ID: $SUBSCRIPTION_ID"
    echo "Location: $LOCATION"
    echo "Migration resource group: $RG_MIGRATION"
    echo "VM: $VM_NAME"
    echo "VM size: $VM_SIZE"
    echo "Image: $IMAGE_URN"
    echo
    echo "This execution creates a billable temporary VM when deployment succeeds."
    echo "The VM has no public IP and deployment is submitted asynchronously."

    section "Validating registered providers"

    for provider in Microsoft.Compute Microsoft.Network; do
        state="$(az provider show --namespace "$provider" --query registrationState --output tsv)"
        echo "${provider}_STATE=$state"
        [ "$state" = "Registered" ] || fail "$provider is not Registered."
    done

    section "Validating East US regional and family quota"

    USAGE_JSON="$WORK_DIR/eastus-usage.json"
    az vm list-usage --location "$LOCATION" --output json > "$USAGE_JSON"

    python3 - "$USAGE_JSON" "$VM_FAMILY" "$REQUIRED_VCPUS" <<'PY'
import json
import sys
from pathlib import Path

items = json.loads(Path(sys.argv[1]).read_text())
wanted_family = sys.argv[2].lower()
required = int(sys.argv[3])
family = None
regional = None

for item in items:
    name = item.get("name") or {}
    value = str(name.get("value") or "")
    localized = str(name.get("localizedValue") or value)
    row = {
        "value": value,
        "localized": localized,
        "used": int(item.get("currentValue") or 0),
        "limit": int(item.get("limit") or 0),
    }

    if value.lower() == wanted_family:
        family = row

    if value.lower() in {"cores", "standardcores"} or localized.strip().lower() == "total regional vcpus":
        regional = row

if family is None:
    raise SystemExit(f"ERROR: Quota row was not found for {sys.argv[2]}.")
if regional is None:
    raise SystemExit("ERROR: Total Regional vCPUs quota row was not found.")

family_remaining = family["limit"] - family["used"]
regional_remaining = regional["limit"] - regional["used"]

print(f"FAMILY_QUOTA_NAME={family['localized']}")
print(f"FAMILY_QUOTA_USED={family['used']}")
print(f"FAMILY_QUOTA_LIMIT={family['limit']}")
print(f"FAMILY_QUOTA_REMAINING={family_remaining}")
print(f"REGIONAL_QUOTA_NAME={regional['localized']}")
print(f"REGIONAL_QUOTA_USED={regional['used']}")
print(f"REGIONAL_QUOTA_LIMIT={regional['limit']}")
print(f"REGIONAL_QUOTA_REMAINING={regional_remaining}")

if family_remaining < required:
    raise SystemExit("ERROR: Insufficient Daldsv7 family quota.")
if regional_remaining < required:
    raise SystemExit("ERROR: Insufficient Total Regional vCPU quota.")

print("COMPUTE_QUOTA_VALIDATION=passed")
PY

    section "Validating selected VM size and Rocky Linux image"

    SKU_JSON="$WORK_DIR/sku.json"
    az vm list-skus \
        --location "$LOCATION" \
        --resource-type virtualMachines \
        --size "$VM_SIZE" \
        --all \
        --output json > "$SKU_JSON"

    python3 - "$SKU_JSON" "$VM_SIZE" <<'PY'
import json
import sys
from pathlib import Path

items = json.loads(Path(sys.argv[1]).read_text())
wanted = sys.argv[2]
matches = [item for item in items if item.get("name") == wanted]

if len(matches) != 1:
    raise SystemExit(f"ERROR: Expected one SKU record for {wanted}; found {len(matches)}.")

item = matches[0]
restrictions = item.get("restrictions") or []
blocked = [
    r for r in restrictions
    if str(r.get("reasonCode") or "") == "NotAvailableForSubscription"
    or str(r.get("type") or "").lower() == "location"
]

if blocked:
    raise SystemExit(f"ERROR: {wanted} is restricted for this subscription in East US: {blocked}")

print(f"VM_SIZE_AVAILABILITY={wanted}:available")
print(f"VM_SIZE_FAMILY={item.get('family') or ''}")
PY

    az vm image show \
        --location "$LOCATION" \
        --urn "$IMAGE_URN" \
        --query '{Publisher:publisher,Offer:offer,Sku:sku,Version:version,Architecture:architecture}' \
        --output table

    section "Validating East US private network path"

    SUBNET_ID="$(
        az network vnet subnet show \
            --resource-group "$RG_NETWORK" \
            --vnet-name "$VNET_NAME" \
            --name "$SUBNET_NAME" \
            --query id \
            --output tsv
    )"

    SUBNET_NAT_ID="$(
        az network vnet subnet show \
            --resource-group "$RG_NETWORK" \
            --vnet-name "$VNET_NAME" \
            --name "$SUBNET_NAME" \
            --query natGateway.id \
            --output tsv
    )"

    [ -n "$SUBNET_ID" ] || fail "East US management subnet ID is empty."
    [[ "${SUBNET_NAT_ID,,}" == */"${NAT_GATEWAY,,}" ]] \
        || fail "Expected NAT gateway $NAT_GATEWAY is not attached to $SUBNET_NAME."

    EAST_CONNECTED="$(
        az network vnet peering list \
            --resource-group "$RG_NETWORK" \
            --vnet-name "$VNET_NAME" \
            --query "length([?peeringState=='Connected'])" \
            --output tsv
    )"

    WEST_CONNECTED="$(
        az network vnet peering list \
            --resource-group "$WEST_RG_NETWORK" \
            --vnet-name "$WEST_VNET_NAME" \
            --query "length([?peeringState=='Connected'])" \
            --output tsv
    )"

    [ "${EAST_CONNECTED:-0}" -ge 1 ] || fail "East-to-West VNet peering is not connected."
    [ "${WEST_CONNECTED:-0}" -ge 1 ] || fail "West-to-East VNet peering is not connected."

    DNS_STATE="$(
        az network private-dns link vnet show \
            --resource-group "$DNS_RG" \
            --zone-name "$DNS_ZONE" \
            --name "$DNS_LINK" \
            --query virtualNetworkLinkState \
            --output tsv
    )"

    [ "$DNS_STATE" = "Completed" ] || fail "East US PostgreSQL private DNS link is not completed."

    echo "SUBNET_ID=$SUBNET_ID"
    echo "SUBNET_NAT_GATEWAY=$NAT_GATEWAY"
    echo "GLOBAL_VNET_PEERING=connected"
    echo "POSTGRES_PRIVATE_DNS_LINK=completed"

    section "Creating temporary migration resource group"

    az group create \
        --name "$RG_MIGRATION" \
        --location "$LOCATION" \
        --tags \
            "application=$PRODUCT_NAME" \
            "environment=$ENVIRONMENT" \
            "resource-function=temporary-database-migration" \
            "delete-after=migration-validation" \
        --only-show-errors \
        --output none

    section "Creating private network interface"

    if az network nic show \
        --resource-group "$RG_MIGRATION" \
        --name "$NIC_NAME" \
        --output none >/dev/null 2>&1; then

        echo "NIC_ACTION=existing"
    else
        az network nic create \
            --resource-group "$RG_MIGRATION" \
            --name "$NIC_NAME" \
            --location "$LOCATION" \
            --subnet "$SUBNET_ID" \
            --ip-forwarding false \
            --accelerated-networking false \
            --tags \
                "application=$PRODUCT_NAME" \
                "environment=$ENVIRONMENT" \
                "resource-function=temporary-database-migration" \
                "public-access=disabled" \
            --only-show-errors \
            --output none

        echo "NIC_ACTION=created"
    fi

    NIC_ID="$(
        az network nic show \
            --resource-group "$RG_MIGRATION" \
            --name "$NIC_NAME" \
            --query id \
            --output tsv
    )"

    PUBLIC_IP_ID="$(
        az network nic show \
            --resource-group "$RG_MIGRATION" \
            --name "$NIC_NAME" \
            --query 'ipConfigurations[0].publicIPAddress.id' \
            --output tsv
    )"

    [ -z "$PUBLIC_IP_ID" ] || fail "The migration NIC unexpectedly has a public IP."

    echo "NIC_ID=$NIC_ID"
    echo "NIC_PUBLIC_IP=none"

    section "Submitting Rocky Linux 10 VM deployment"

    if az vm show \
        --resource-group "$RG_MIGRATION" \
        --name "$VM_NAME" \
        --output none >/dev/null 2>&1; then

        echo "VM_DEPLOYMENT_ACTION=existing"
        echo "VM_PROVISIONING_STATE=$(az vm show -g "$RG_MIGRATION" -n "$VM_NAME" --query provisioningState -o tsv)"
    else
        az vm create \
            --resource-group "$RG_MIGRATION" \
            --name "$VM_NAME" \
            --location "$LOCATION" \
            --nics "$NIC_ID" \
            --image "$IMAGE_URN" \
            --size "$VM_SIZE" \
            --admin-username "$ADMIN_USERNAME" \
            --assign-identity \
            --generate-ssh-keys \
            --authentication-type ssh \
            --storage-sku StandardSSD_LRS \
            --os-disk-name "$OS_DISK_NAME" \
            --os-disk-size-gb 64 \
            --tags \
                "application=$PRODUCT_NAME" \
                "environment=$ENVIRONMENT" \
                "resource-function=temporary-database-migration" \
                "operating-system=rocky-linux-10" \
                "public-access=disabled" \
                "delete-after=migration-validation" \
            --no-wait \
            --only-show-errors \
            --output none

        echo "VM_DEPLOYMENT_ACTION=submitted"
    fi

    section "AZ-05C2A9 submission complete"

    echo "MIGRATION_RESOURCE_GROUP=$RG_MIGRATION"
    echo "RESTORE_RUNNER_VM=$VM_NAME"
    echo "RESTORE_RUNNER_NIC=$NIC_NAME"
    echo "RESTORE_RUNNER_LOCATION=$LOCATION"
    echo "RESTORE_RUNNER_SIZE=$VM_SIZE"
    echo "RESTORE_RUNNER_IMAGE=$IMAGE_URN"
    echo "RESTORE_RUNNER_PUBLIC_IP=none"
    echo
    echo "Azure continues the VM deployment independently of this Cloud Shell session."
    echo
    echo "************************************************************"
    echo "EASTUS ROCKY 10 RESTORE RUNNER DEPLOYMENT SUBMITTED"
    echo "************************************************************"

} 2>&1 | tee "$LOG"

echo
echo "Submission log: $LOG"
