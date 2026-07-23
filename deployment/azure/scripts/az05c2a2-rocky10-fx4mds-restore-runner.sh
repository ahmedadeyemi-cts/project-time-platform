#!/usr/bin/env bash
set -Eeuo pipefail

PRODUCT_NAME="Project Health Dashboard"
ENVIRONMENT="test"
LOCATION="westus3"
RG_DATA="rg-project-health-dashboard-test-data-westus3"
VM_NAME="vm-phd-test-db-migrate-w3"
NIC_NAME="nic-phd-test-db-migrate-w3"
VM_SIZE="Standard_FX4mds"
ADMIN_USERNAME="azureuser"
ROCKY_PUBLISHER="resf"
ROCKY_OFFER="rockylinux-x86_64"
ROCKY_SKU="10-base"
STORAGE_ACCOUNT="stphdtest7825cc"
KEY_VAULT="kv-phd-t-w3-7825cc"
POSTGRES_FQDN="pg-phd-test-w3-7825cc.postgres.database.azure.com"
BASE_DIR="$HOME/project-health-dashboard-azure"
CONFIG_DIR="$BASE_DIR/config"
LOG_DIR="$BASE_DIR/logs"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
LOG="$LOG_DIR/az05c2a2-rocky10-fx4mds-runner-$STAMP.log"
CONFIG_FILE="$CONFIG_DIR/postgresql-restore-runner.env"
mkdir -p "$CONFIG_DIR" "$LOG_DIR"

section(){ echo; echo "============================================================"; echo "$1"; echo "============================================================"; }

ensure_role(){
    local principal_id="$1" role_name="$2" scope="$3"
    if [ "$(az role assignment list --assignee "$principal_id" --scope "$scope" --role "$role_name" --query 'length(@)' -o tsv 2>/dev/null || echo 0)" != "0" ]; then
        echo "Existing role assignment: $role_name"
        return 0
    fi
    for attempt in $(seq 1 18); do
        if az role assignment create --assignee-object-id "$principal_id" --assignee-principal-type ServicePrincipal --role "$role_name" --scope "$scope" --only-show-errors -o none; then
            echo "Created role assignment: $role_name"
            return 0
        fi
        [ "$attempt" = 18 ] && { echo "ERROR: Could not create role assignment: $role_name"; return 1; }
        sleep 10
    done
}

show_retail_price(){
    python3 - "$LOCATION" "$VM_SIZE" <<'PY' || true
import json
import sys
import urllib.parse
import urllib.request

region, sku = sys.argv[1:]
base = "https://prices.azure.com/api/retail/prices"
flt = (
    f"armRegionName eq '{region}' and "
    f"armSkuName eq '{sku}' and "
    "priceType eq 'Consumption'"
)
url = base + "?" + urllib.parse.urlencode({"$filter": flt})
try:
    with urllib.request.urlopen(url, timeout=30) as response:
        data = json.load(response)
except Exception as exc:
    print(f"WARNING: Azure retail price lookup failed: {exc}")
    raise SystemExit(0)

rows = []
for item in data.get("Items") or []:
    product = str(item.get("productName") or "")
    meter = str(item.get("meterName") or "")
    if "windows" in product.lower():
        continue
    if "spot" in meter.lower() or "low priority" in meter.lower():
        continue
    if str(item.get("type") or "").lower() != "consumption":
        continue
    rows.append(item)

if not rows:
    print("WARNING: No Linux pay-as-you-go retail-price row was returned.")
    raise SystemExit(0)

rows.sort(key=lambda item: float(item.get("retailPrice") or 0))
item = rows[0]
price = float(item.get("retailPrice") or 0)
print(f"RETAIL_PRICE_USD_PER_HOUR={price:.6f}")
print(f"RETAIL_PRICE_METER={item.get('meterName')}")
print(f"RETAIL_PRICE_PRODUCT={item.get('productName')}")
print(f"THREE_HOUR_COMPUTE_ESTIMATE_USD={price * 3:.2f}")
print("Retail price excludes managed disk, network, storage, and applicable taxes or discounts.")
PY
}

{
section "AZ-05C2A2 - Rocky Linux 10 FX4mds Restore Runner"
echo "TIME=$(date -u -Is)"
echo "VM: $VM_NAME"
echo "Required VM size: $VM_SIZE"
echo "Reason: only compatible unrestricted x64 candidate discovered in West US 3"
echo "This VM is billable and must be used immediately, then deallocated."

section "Confirming current partial state"
if az vm show -g "$RG_DATA" -n "$VM_NAME" -o none >/dev/null 2>&1; then
    EXISTING_SIZE="$(az vm show -g "$RG_DATA" -n "$VM_NAME" --query hardwareProfile.vmSize -o tsv)"
    [ "$EXISTING_SIZE" = "$VM_SIZE" ] || { echo "ERROR: Existing VM uses unexpected size: $EXISTING_SIZE"; exit 1; }
    VM_EXISTS=true
    echo "Existing VM confirmed: $VM_NAME ($EXISTING_SIZE)"
else
    VM_EXISTS=false
    echo "No VM exists."
fi

az network nic show -g "$RG_DATA" -n "$NIC_NAME" -o none >/dev/null 2>&1 || { echo "ERROR: Expected NIC is missing: $NIC_NAME"; exit 1; }
NIC_ID="$(az network nic show -g "$RG_DATA" -n "$NIC_NAME" --query id -o tsv)"
PUBLIC_IP_ID="$(az network nic show -g "$RG_DATA" -n "$NIC_NAME" --query 'ipConfigurations[0].publicIPAddress.id' -o tsv)"
[ -z "$PUBLIC_IP_ID" ] || { echo "ERROR: NIC unexpectedly has a public IP."; exit 1; }
echo "Private NIC confirmed: $NIC_NAME"

section "Validating FX4mds availability"
SKU_JSON="$(mktemp)"
az vm list-skus --location "$LOCATION" --resource-type virtualMachines --all -o json > "$SKU_JSON"
python3 - "$SKU_JSON" "$VM_SIZE" <<'PY'
import json, sys
from pathlib import Path
items=json.loads(Path(sys.argv[1]).read_text())
name=sys.argv[2]
matches=[item for item in items if item.get("name")==name]
if not matches:
    raise SystemExit(f"ERROR: {name} is not published in this region.")
item=matches[0]
restrictions=item.get("restrictions") or []
if restrictions:
    raise SystemExit(f"ERROR: {name} is restricted: {json.dumps(restrictions)}")
print(f"VM_SIZE_AVAILABILITY={name}:unrestricted")
for cap in item.get("capabilities") or []:
    if cap.get("name") in {"vCPUs","MemoryGB","CpuArchitectureType","HyperVGenerations"}:
        print(f"VM_CAPABILITY_{cap.get('name')}={cap.get('value')}")
PY
rm -f "$SKU_JSON"

section "Current retail-price estimate"
show_retail_price

section "Selecting latest official Rocky Linux 10 image"
IMAGE_VERSION="$(az vm image list --location "$LOCATION" --publisher "$ROCKY_PUBLISHER" --offer "$ROCKY_OFFER" --sku "$ROCKY_SKU" --architecture x64 --all --query 'sort_by(@,&version)[-1].version' -o tsv)"
[ -n "$IMAGE_VERSION" ] || { echo "ERROR: Rocky Linux 10 image was not found."; exit 1; }
IMAGE_URN="$ROCKY_PUBLISHER:$ROCKY_OFFER:$ROCKY_SKU:$IMAGE_VERSION"
az vm image show --location "$LOCATION" --urn "$IMAGE_URN" -o none
echo "Selected image: $IMAGE_URN"

section "Creating or starting temporary VM"
if [ "$VM_EXISTS" = true ]; then
    az vm start -g "$RG_DATA" -n "$VM_NAME" --only-show-errors -o none
else
    az vm create \
        -g "$RG_DATA" -n "$VM_NAME" -l "$LOCATION" \
        --nics "$NIC_ID" \
        --image "$IMAGE_URN" \
        --size "$VM_SIZE" \
        --admin-username "$ADMIN_USERNAME" \
        --assign-identity \
        --generate-ssh-keys \
        --authentication-type ssh \
        --storage-sku StandardSSD_LRS \
        --os-disk-size-gb 64 \
        --tags \
            "application=$PRODUCT_NAME" \
            "environment=$ENVIRONMENT" \
            "resource-function=temporary-database-migration" \
            "operating-system=rocky-linux-10" \
            "public-access=disabled" \
            "delete-after=migration-validation" \
            "cost-control=deallocate-after-restore" \
        --only-show-errors -o none
fi
az vm wait -g "$RG_DATA" -n "$VM_NAME" --created --interval 15 --timeout 1200

VM_ID="$(az vm show -g "$RG_DATA" -n "$VM_NAME" --query id -o tsv)"
VM_PRINCIPAL_ID="$(az vm identity show -g "$RG_DATA" -n "$VM_NAME" --query principalId -o tsv)"
VM_PRIVATE_IP="$(az network nic show -g "$RG_DATA" -n "$NIC_NAME" --query 'ipConfigurations[0].privateIPAddress' -o tsv)"
[ -n "$VM_PRINCIPAL_ID" ] || { echo "ERROR: VM managed identity is missing."; exit 1; }

section "Assigning managed-identity access"
STORAGE_ACCOUNT_ID="$(az storage account show -g "$RG_DATA" -n "$STORAGE_ACCOUNT" --query id -o tsv)"
KEY_VAULT_ID="$(az keyvault show -g "$RG_DATA" -n "$KEY_VAULT" --query id -o tsv)"
ensure_role "$VM_PRINCIPAL_ID" "Storage Blob Data Reader" "$STORAGE_ACCOUNT_ID"
ensure_role "$VM_PRINCIPAL_ID" "Storage Blob Data Contributor" "$STORAGE_ACCOUNT_ID"
ensure_role "$VM_PRINCIPAL_ID" "Key Vault Secrets User" "$KEY_VAULT_ID"

section "Validating Rocky Linux 10, CPU baseline, and connectivity"
VALIDATION_SCRIPT="$(mktemp)"
cat > "$VALIDATION_SCRIPT" <<EOF2
#!/usr/bin/env bash
set -Eeuo pipefail
source /etc/os-release
[ "\${ID:-}" = rocky ] || { echo "ERROR: ID=\${ID:-unknown}"; exit 1; }
case "\${VERSION_ID:-}" in 10|10.*) ;; *) echo "ERROR: VERSION_ID=\${VERSION_ID:-unknown}"; exit 1;; esac
dnf -y upgrade --refresh
dnf -y install curl bind-utils iproute procps-ng python3
source /etc/os-release
case "\${VERSION_ID:-}" in 10|10.*) ;; *) echo "ERROR: Rocky major version is not 10"; exit 1;; esac
LOADER="/lib64/ld-linux-x86-64.so.2"
[ -x "\$LOADER" ] || { echo "ERROR: glibc loader not found"; exit 1; }
"\$LOADER" --help | grep -q 'x86-64-v3.*supported' || { echo "ERROR: x86-64-v3 CPU baseline is not supported"; exit 1; }
echo "PHD_ROCKY_VERSION=\$VERSION_ID"
echo "PHD_CPU_BASELINE=x86-64-v3-supported"
echo "Architecture: \$(uname -m)"
getent ahostsv4 "$POSTGRES_FQDN"
timeout 10 bash -c "cat < /dev/null > /dev/tcp/$POSTGRES_FQDN/5432"
curl -fsS -H Metadata:true 'http://169.254.169.254/metadata/identity/oauth2/token?api-version=2018-02-01&resource=https%3A%2F%2Fstorage.azure.com%2F' >/dev/null
curl -fsS -H Metadata:true 'http://169.254.169.254/metadata/identity/oauth2/token?api-version=2018-02-01&resource=https%3A%2F%2Fvault.azure.net' >/dev/null
echo "PRIVATE ROCKY 10 RESTORE RUNNER CONNECTIVITY READY"
EOF2

RUN_OUTPUT=""
for attempt in $(seq 1 20); do
    if RUN_OUTPUT="$(az vm run-command invoke -g "$RG_DATA" -n "$VM_NAME" --command-id RunShellScript --scripts @"$VALIDATION_SCRIPT" --query 'value[0].message' -o tsv)"; then
        break
    fi
    [ "$attempt" = 20 ] && { rm -f "$VALIDATION_SCRIPT"; echo "ERROR: Run Command did not become available."; exit 1; }
    sleep 30
done
rm -f "$VALIDATION_SCRIPT"
printf '%s\n' "$RUN_OUTPUT"
grep -q 'PRIVATE ROCKY 10 RESTORE RUNNER CONNECTIVITY READY' <<< "$RUN_OUTPUT" || { echo "ERROR: Validation did not complete."; exit 1; }
ROCKY_VERSION="$(sed -n 's/.*PHD_ROCKY_VERSION=//p' <<< "$RUN_OUTPUT" | tail -n1 | tr -d '\r')"

section "Saving non-secret configuration"
cat > "$CONFIG_FILE" <<EOF2
PROJECT_NAME=$PRODUCT_NAME
ENVIRONMENT=$ENVIRONMENT
RESTORE_RUNNER_VM=$VM_NAME
RESTORE_RUNNER_VM_ID=$VM_ID
RESTORE_RUNNER_NIC=$NIC_NAME
RESTORE_RUNNER_PRIVATE_IP=$VM_PRIVATE_IP
RESTORE_RUNNER_LOCATION=$LOCATION
RESTORE_RUNNER_SIZE=$VM_SIZE
RESTORE_RUNNER_PUBLIC_IP=none
RESTORE_RUNNER_PRINCIPAL_ID=$VM_PRINCIPAL_ID
RESTORE_RUNNER_OS=Rocky Linux
RESTORE_RUNNER_OS_MAJOR=10
RESTORE_RUNNER_OS_VERSION=$ROCKY_VERSION
RESTORE_RUNNER_IMAGE_PUBLISHER=$ROCKY_PUBLISHER
RESTORE_RUNNER_IMAGE_OFFER=$ROCKY_OFFER
RESTORE_RUNNER_IMAGE_SKU=$ROCKY_SKU
RESTORE_RUNNER_IMAGE_VERSION=$IMAGE_VERSION
RESTORE_RUNNER_IMAGE_URN=$IMAGE_URN
TARGET_POSTGRES_FQDN=$POSTGRES_FQDN
TARGET_KEY_VAULT=$KEY_VAULT
EOF2
chmod 600 "$CONFIG_FILE"

section "Deallocating validated restore runner"
az vm deallocate -g "$RG_DATA" -n "$VM_NAME" --only-show-errors -o none
POWER_STATE="$(az vm get-instance-view -g "$RG_DATA" -n "$VM_NAME" --query "instanceView.statuses[?starts_with(code, 'PowerState/')].displayStatus | [0]" -o tsv 2>/dev/null || true)"
echo "VM power state: ${POWER_STATE:-unknown}"

section "AZ-05C2A2 completed"
echo "VM size: $VM_SIZE"
echo "Private IP: $VM_PRIVATE_IP"
echo "Public IP: none"
echo "Configuration: $CONFIG_FILE"
echo "The validated restore runner is deallocated to stop compute charges."
echo "AZ-05C2B will start it only for the restore and deallocate it again."
echo "PRIVATE ROCKY 10 POSTGRESQL RESTORE RUNNER READY AND DEALLOCATED"
} 2>&1 | tee "$LOG"

echo "Deployment log: $LOG"
