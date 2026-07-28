#!/usr/bin/env bash
set -Eeuo pipefail

PRODUCT_NAME="Project Health Dashboard"
ENVIRONMENT="test"
LOCATION="westus3"
RG_NETWORK="rg-project-health-dashboard-test-network-westus3"
RG_DATA="rg-project-health-dashboard-test-data-westus3"
VNET_NAME="vnet-phd-test-westus3"
MANAGEMENT_SUBNET="snet-management"
NAT_GATEWAY="nat-phd-test-aca-westus3"
VM_NAME="vm-phd-test-db-migrate-w3"
NIC_NAME="nic-phd-test-db-migrate-w3"
ADMIN_USERNAME="azureuser"
ROCKY_PUBLISHER="resf"
ROCKY_OFFER="rockylinux-x86_64"
ROCKY_SKU="10-base"
STORAGE_ACCOUNT="stphdtest7825cc"
STORAGE_CONTAINER="database-exports"
REMOTE_PREFIX="source-postgresql13/20260712T023119Z"
DUMP_BLOB="$REMOTE_PREFIX/ProjectPulse-pg13-20260712T023119Z.dump"
KEY_VAULT="kv-phd-t-w3-7825cc"
POSTGRES_FQDN="pg-phd-test-w3-7825cc.postgres.database.azure.com"
BASE_DIR="$HOME/project-health-dashboard-azure"
CONFIG_DIR="$BASE_DIR/config"
LOG_DIR="$BASE_DIR/logs"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
LOG="$LOG_DIR/az05c2a1-rocky10-runner-continuation-$STAMP.log"
CONFIG_FILE="$CONFIG_DIR/postgresql-restore-runner.env"
SIZE_DIAGNOSTIC="$LOG_DIR/az05c2a1-vm-sizes-$STAMP.tsv"
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

select_vm_size(){
    local sku_json rc selected
    sku_json="$(mktemp)"
    rc=0
    az vm list-skus --location "$LOCATION" --resource-type virtualMachines --all -o json > "$sku_json"
    selected="$(python3 - "$sku_json" "$SIZE_DIAGNOSTIC" <<'PY' || rc=$?
import json, sys
from pathlib import Path
items=json.loads(Path(sys.argv[1]).read_text())
diag=Path(sys.argv[2])
preferred=[
"Standard_D2ds_v4","Standard_D2s_v4","Standard_D2as_v4","Standard_D2ads_v4",
"Standard_D2ds_v5","Standard_D2s_v5","Standard_D2as_v5","Standard_D2ads_v5",
"Standard_D2ds_v6","Standard_D2s_v6","Standard_D2as_v6","Standard_D2ads_v6",
"Standard_E2ds_v4","Standard_E2s_v4","Standard_E2as_v4","Standard_E2ads_v4",
"Standard_E2ds_v5","Standard_E2s_v5","Standard_E2as_v5","Standard_E2ads_v5",
"Standard_D4ds_v4","Standard_D4s_v4","Standard_D4as_v4","Standard_D4ads_v4",
"Standard_D4ds_v5","Standard_D4s_v5","Standard_D4as_v5","Standard_D4ads_v5",
]
rows=[]
available=set()
for item in items:
    name=str(item.get("name") or "")
    restrictions=item.get("restrictions") or []
    blocked=any(
        str(r.get("reasonCode") or "") == "NotAvailableForSubscription"
        or str(r.get("type") or "").lower() == "location"
        for r in restrictions
    )
    if name.startswith(("Standard_D","Standard_E")):
        rows.append(f"{name}\t{'blocked' if blocked else 'available'}")
    if not blocked:
        available.add(name)
diag.write_text("size\tstatus\n"+"\n".join(sorted(rows))+"\n")
for name in preferred:
    if name in available:
        print(name)
        raise SystemExit(0)
print(f"No approved Rocky Linux 10 VM size is available. Review {diag}", file=sys.stderr)
raise SystemExit(1)
PY
)"
    rm -f "$sku_json"
    [ "$rc" -eq 0 ] || return "$rc"
    [ -n "$selected" ] || { echo "ERROR: VM-size selector returned an empty value." >&2; return 1; }
    printf '%s\n' "$selected"
}

{
section "AZ-05C2A1 - Rocky Linux 10 Restore Runner Continuation"
echo "TIME=$(date -u -Is)"
echo "VM: $VM_NAME"
echo "Existing NIC will be reused when present: $NIC_NAME"

section "Confirming partial deployment state"
if az vm show -g "$RG_DATA" -n "$VM_NAME" -o none >/dev/null 2>&1; then
    echo "Existing VM found; it will be validated and reused."
    VM_EXISTS=true
else
    echo "No VM exists, as expected after the failed size-selection attempt."
    VM_EXISTS=false
fi

if az network nic show -g "$RG_DATA" -n "$NIC_NAME" -o none >/dev/null 2>&1; then
    echo "Existing private NIC confirmed: $NIC_NAME"
else
    echo "ERROR: Expected private NIC is missing. Run the canonical foundation script first."
    exit 1
fi

PUBLIC_IP_ID="$(az network nic show -g "$RG_DATA" -n "$NIC_NAME" --query 'ipConfigurations[0].publicIPAddress.id' -o tsv)"
[ -z "$PUBLIC_IP_ID" ] || { echo "ERROR: NIC unexpectedly has a public IP."; exit 1; }
NIC_ID="$(az network nic show -g "$RG_DATA" -n "$NIC_NAME" --query id -o tsv)"

section "Selecting compatible VM size"
if [ "$VM_EXISTS" = true ]; then
    VM_SIZE="$(az vm show -g "$RG_DATA" -n "$VM_NAME" --query hardwareProfile.vmSize -o tsv)"
else
    if ! VM_SIZE="$(select_vm_size)"; then
        echo "ERROR: No approved VM size could be selected."
        echo "Diagnostic: $SIZE_DIAGNOSTIC"
        exit 1
    fi
fi
[ -n "$VM_SIZE" ] || { echo "ERROR: VM_SIZE is empty; refusing to continue."; exit 1; }
echo "Selected VM size: $VM_SIZE"

section "Selecting latest official Rocky Linux 10 image"
IMAGE_VERSION="$(az vm image list --location "$LOCATION" --publisher "$ROCKY_PUBLISHER" --offer "$ROCKY_OFFER" --sku "$ROCKY_SKU" --architecture x64 --all --query 'sort_by(@,&version)[-1].version' -o tsv)"
[ -n "$IMAGE_VERSION" ] || { echo "ERROR: Rocky Linux 10 image version was not found."; exit 1; }
IMAGE_URN="$ROCKY_PUBLISHER:$ROCKY_OFFER:$ROCKY_SKU:$IMAGE_VERSION"
az vm image show --location "$LOCATION" --urn "$IMAGE_URN" -o none
echo "Selected image: $IMAGE_URN"

section "Creating or starting Rocky Linux 10 VM"
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
            "image-publisher=$ROCKY_PUBLISHER" \
            "public-access=disabled" \
            "delete-after=migration-validation" \
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
dnf -y install curl bind-utils iproute procps-ng
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
SOURCE_EXPORT_STORAGE_ACCOUNT=$STORAGE_ACCOUNT
SOURCE_EXPORT_CONTAINER=$STORAGE_CONTAINER
SOURCE_EXPORT_PREFIX=$REMOTE_PREFIX
SOURCE_EXPORT_DUMP_BLOB=$DUMP_BLOB
TARGET_POSTGRES_FQDN=$POSTGRES_FQDN
TARGET_KEY_VAULT=$KEY_VAULT
EOF2
chmod 600 "$CONFIG_FILE"

section "AZ-05C2A1 completed successfully"
echo "VM size: $VM_SIZE"
echo "Rocky Linux: $ROCKY_VERSION"
echo "Private IP: $VM_PRIVATE_IP"
echo "Public IP: none"
echo "Configuration: $CONFIG_FILE"
echo "This VM is billable while running and must be deleted after restore validation."
echo "************************************************************"
echo "PRIVATE ROCKY 10 POSTGRESQL RESTORE RUNNER READY"
echo "************************************************************"
} 2>&1 | tee "$LOG"

echo
echo "VM-size diagnostic: $SIZE_DIAGNOSTIC"
echo "Deployment log:     $LOG"
