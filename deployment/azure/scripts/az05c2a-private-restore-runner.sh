#!/usr/bin/env bash
set -Eeuo pipefail

PRODUCT_NAME="Project Health Dashboard"
ENVIRONMENT="test"
LOCATION="westus3"

RG_SHARED="rg-project-health-dashboard-shared-global"
RG_NETWORK="rg-project-health-dashboard-test-network-westus3"
RG_DATA="rg-project-health-dashboard-test-data-westus3"

VNET_NAME="vnet-phd-test-westus3"
MANAGEMENT_SUBNET="snet-management"
NAT_GATEWAY="nat-phd-test-aca-westus3"

VM_NAME="vm-phd-test-db-migrate-w3"
NIC_NAME="nic-phd-test-db-migrate-w3"
ADMIN_USERNAME="azureuser"
VM_IMAGE="Canonical:0001-com-ubuntu-minimal-jammy:minimal-22_04-lts-gen2:latest"

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
LOG="$LOG_DIR/az05c2a-private-restore-runner-$STAMP.log"
CONFIG_FILE="$CONFIG_DIR/postgresql-restore-runner.env"

mkdir -p "$CONFIG_DIR" "$LOG_DIR"

section() {
    echo
    echo "============================================================"
    echo "$1"
    echo "============================================================"
}

ensure_role() {
    local principal_id="$1"
    local role_name="$2"
    local scope="$3"

    if [ "$(
        az role assignment list \
            --assignee "$principal_id" \
            --scope "$scope" \
            --role "$role_name" \
            --query 'length(@)' \
            --output tsv 2>/dev/null || echo 0
    )" != "0" ]; then
        echo "Existing role assignment: $role_name"
        return
    fi

    for attempt in $(seq 1 18); do
        if az role assignment create \
            --assignee-object-id "$principal_id" \
            --assignee-principal-type ServicePrincipal \
            --role "$role_name" \
            --scope "$scope" \
            --only-show-errors \
            --output none; then

            echo "Created role assignment: $role_name"
            return
        fi

        if [ "$attempt" = "18" ]; then
            echo "ERROR: Could not create role assignment: $role_name"
            exit 1
        fi

        sleep 10
    done
}

select_vm_size() {
    local sku_json
    sku_json="$(mktemp)"

    az vm list-skus \
        --location "$LOCATION" \
        --resource-type virtualMachines \
        --all \
        --output json > "$sku_json"

    python3 - "$sku_json" <<'PY'
import json
import sys
from pathlib import Path

items = json.loads(Path(sys.argv[1]).read_text())
preferred = [
    "Standard_B2s",
    "Standard_B2als_v2",
    "Standard_D2s_v5",
    "Standard_D2as_v5",
]

for candidate in preferred:
    matches = [item for item in items if item.get("name") == candidate]

    for item in matches:
        restrictions = item.get("restrictions") or []
        blocked = False

        for restriction in restrictions:
            reason = str(restriction.get("reasonCode") or "")
            restriction_type = str(restriction.get("type") or "")

            if reason == "NotAvailableForSubscription":
                blocked = True
                break

            if restriction_type.lower() == "location":
                blocked = True
                break

        if not blocked:
            print(candidate)
            raise SystemExit(0)

raise SystemExit(
    "No preferred temporary migration VM size is available in West US 3."
)
PY

    rm -f "$sku_json"
}

{
    section "AZ-05C2A - Private PostgreSQL Restore Runner"

    echo "Product: $PRODUCT_NAME"
    echo "Environment: $ENVIRONMENT"
    echo "Location: $LOCATION"
    echo "VM: $VM_NAME"
    echo "Blob prefix: $REMOTE_PREFIX"
    echo "TIME=$(date -u -Is)"

    section "Validating uploaded source package"

    BLOB_COUNT="$(
        az storage blob list \
            --account-name "$STORAGE_ACCOUNT" \
            --container-name "$STORAGE_CONTAINER" \
            --prefix "$REMOTE_PREFIX" \
            --auth-mode login \
            --query 'length(@)' \
            --output tsv
    )"

    if [ "${BLOB_COUNT:-0}" -lt 15 ]; then
        echo "ERROR: Only ${BLOB_COUNT:-0} migration artifacts were found."
        exit 1
    fi

    DUMP_SIZE="$(
        az storage blob show \
            --account-name "$STORAGE_ACCOUNT" \
            --container-name "$STORAGE_CONTAINER" \
            --name "$DUMP_BLOB" \
            --auth-mode login \
            --query properties.contentLength \
            --output tsv
    )"

    if [ "${DUMP_SIZE:-0}" -le 0 ]; then
        echo "ERROR: PostgreSQL dump Blob is missing or empty."
        exit 1
    fi

    echo "Migration artifacts: $BLOB_COUNT"
    echo "PostgreSQL archive bytes: $DUMP_SIZE"

    section "Attaching fixed egress NAT to management subnet"

    NAT_GATEWAY_ID="$(
        az network nat gateway show \
            --resource-group "$RG_NETWORK" \
            --name "$NAT_GATEWAY" \
            --query id \
            --output tsv
    )"

    MANAGEMENT_SUBNET_ID="$(
        az network vnet subnet show \
            --resource-group "$RG_NETWORK" \
            --vnet-name "$VNET_NAME" \
            --name "$MANAGEMENT_SUBNET" \
            --query id \
            --output tsv
    )"

    az network vnet subnet update \
        --resource-group "$RG_NETWORK" \
        --vnet-name "$VNET_NAME" \
        --name "$MANAGEMENT_SUBNET" \
        --nat-gateway "$NAT_GATEWAY_ID" \
        --only-show-errors \
        --output none

    echo "Management subnet egress uses: $NAT_GATEWAY"

    section "Selecting temporary VM size"

    if az vm show \
        --resource-group "$RG_DATA" \
        --name "$VM_NAME" \
        --output none \
        >/dev/null 2>&1; then

        VM_SIZE="$(
            az vm show \
                --resource-group "$RG_DATA" \
                --name "$VM_NAME" \
                --query hardwareProfile.vmSize \
                --output tsv
        )"

        echo "Existing restore runner found."
    else
        VM_SIZE="$(select_vm_size)"
    fi

    echo "Selected VM size: $VM_SIZE"

    section "Creating private network interface"

    if az network nic show \
        --resource-group "$RG_DATA" \
        --name "$NIC_NAME" \
        --output none \
        >/dev/null 2>&1; then

        echo "Existing NIC confirmed: $NIC_NAME"
    else
        az network nic create \
            --resource-group "$RG_DATA" \
            --name "$NIC_NAME" \
            --location "$LOCATION" \
            --subnet "$MANAGEMENT_SUBNET_ID" \
            --ip-forwarding false \
            --accelerated-networking false \
            --tags \
                "application=$PRODUCT_NAME" \
                "environment=$ENVIRONMENT" \
                "resource-function=temporary-database-migration" \
                "public-access=disabled" \
            --only-show-errors \
            --output none

        echo "Created NIC without a public IP: $NIC_NAME"
    fi

    NIC_ID="$(
        az network nic show \
            --resource-group "$RG_DATA" \
            --name "$NIC_NAME" \
            --query id \
            --output tsv
    )"

    section "Creating temporary private restore VM"

    if az vm show \
        --resource-group "$RG_DATA" \
        --name "$VM_NAME" \
        --output none \
        >/dev/null 2>&1; then

        echo "Existing VM confirmed: $VM_NAME"

        az vm start \
            --resource-group "$RG_DATA" \
            --name "$VM_NAME" \
            --only-show-errors \
            --output none
    else
        az vm create \
            --resource-group "$RG_DATA" \
            --name "$VM_NAME" \
            --location "$LOCATION" \
            --nics "$NIC_ID" \
            --image "$VM_IMAGE" \
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
                "public-access=disabled" \
                "delete-after=migration-validation" \
            --only-show-errors \
            --output none

        echo "Created temporary private restore VM: $VM_NAME"
    fi

    az vm wait \
        --resource-group "$RG_DATA" \
        --name "$VM_NAME" \
        --created \
        --interval 15 \
        --timeout 900

    VM_ID="$(
        az vm show \
            --resource-group "$RG_DATA" \
            --name "$VM_NAME" \
            --query id \
            --output tsv
    )"

    VM_PRINCIPAL_ID="$(
        az vm identity show \
            --resource-group "$RG_DATA" \
            --name "$VM_NAME" \
            --query principalId \
            --output tsv
    )"

    VM_PRIVATE_IP="$(
        az network nic show \
            --resource-group "$RG_DATA" \
            --name "$NIC_NAME" \
            --query 'ipConfigurations[0].privateIPAddress' \
            --output tsv
    )"

    PUBLIC_IP_ID="$(
        az network nic show \
            --resource-group "$RG_DATA" \
            --name "$NIC_NAME" \
            --query 'ipConfigurations[0].publicIPAddress.id' \
            --output tsv
    )"

    if [ -n "$PUBLIC_IP_ID" ]; then
        echo "ERROR: The restore runner unexpectedly has a public IP."
        exit 1
    fi

    echo "VM private IP: $VM_PRIVATE_IP"
    echo "VM public IP: none"

    section "Assigning managed-identity access"

    STORAGE_ACCOUNT_ID="$(
        az storage account show \
            --resource-group "$RG_DATA" \
            --name "$STORAGE_ACCOUNT" \
            --query id \
            --output tsv
    )"

    KEY_VAULT_ID="$(
        az keyvault show \
            --resource-group "$RG_DATA" \
            --name "$KEY_VAULT" \
            --query id \
            --output tsv
    )"

    ensure_role \
        "$VM_PRINCIPAL_ID" \
        "Storage Blob Data Reader" \
        "$STORAGE_ACCOUNT_ID"

    ensure_role \
        "$VM_PRINCIPAL_ID" \
        "Key Vault Secrets User" \
        "$KEY_VAULT_ID"

    section "Validating private connectivity and managed identity"

    CONNECTIVITY_SCRIPT="$(mktemp)"

    cat > "$CONNECTIVITY_SCRIPT" <<EOF
#!/usr/bin/env bash
set -Eeuo pipefail

if ! command -v curl >/dev/null 2>&1; then
    apt-get update -qq
    DEBIAN_FRONTEND=noninteractive apt-get install -y -qq curl
fi

echo "Hostname: \$(hostname -f 2>/dev/null || hostname)"
echo "Private addresses:"
hostname -I

echo "PostgreSQL DNS:"
getent ahostsv4 "$POSTGRES_FQDN"

echo "PostgreSQL TCP 5432:"
timeout 10 bash -c "cat < /dev/null > /dev/tcp/$POSTGRES_FQDN/5432"
echo "TCP connection succeeded."

echo "Managed identity token endpoint:"
curl -fsS \
    -H Metadata:true \
    'http://169.254.169.254/metadata/identity/oauth2/token?api-version=2018-02-01&resource=https%3A%2F%2Fstorage.azure.com%2F' \
    >/dev/null
echo "Managed identity token acquisition succeeded."

echo "Outbound package endpoint:"
curl -fsSI --max-time 20 https://packages.microsoft.com >/dev/null
echo "Outbound HTTPS succeeded."

echo "PRIVATE RESTORE RUNNER CONNECTIVITY READY"
EOF

    az vm run-command invoke \
        --resource-group "$RG_DATA" \
        --name "$VM_NAME" \
        --command-id RunShellScript \
        --scripts @"$CONNECTIVITY_SCRIPT" \
        --query 'value[0].message' \
        --output tsv

    rm -f "$CONNECTIVITY_SCRIPT"

    section "Saving non-secret restore-runner configuration"

    cat > "$CONFIG_FILE" <<EOF
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

RESTORE_RUNNER_SUBNET_ID=$MANAGEMENT_SUBNET_ID
RESTORE_RUNNER_NAT_GATEWAY=$NAT_GATEWAY

SOURCE_EXPORT_STORAGE_ACCOUNT=$STORAGE_ACCOUNT
SOURCE_EXPORT_CONTAINER=$STORAGE_CONTAINER
SOURCE_EXPORT_PREFIX=$REMOTE_PREFIX
SOURCE_EXPORT_DUMP_BLOB=$DUMP_BLOB

TARGET_POSTGRES_FQDN=$POSTGRES_FQDN
TARGET_KEY_VAULT=$KEY_VAULT
EOF

    chmod 600 "$CONFIG_FILE"

    section "AZ-05C2A completed successfully"

    echo "Temporary restore VM: configured"
    echo "Public IP: none"
    echo "Private IP: $VM_PRIVATE_IP"
    echo "Fixed outbound NAT: $NAT_GATEWAY"
    echo "Storage access: Storage Blob Data Reader"
    echo "Key Vault access: Key Vault Secrets User"
    echo "Private PostgreSQL DNS and TCP: validated"
    echo
    echo "This VM is billable while running."
    echo "It will be deallocated after restore validation."
    echo
    echo "Configuration: $CONFIG_FILE"
    echo
    echo "************************************************************"
    echo "PRIVATE POSTGRESQL RESTORE RUNNER READY"
    echo "************************************************************"

} 2>&1 | tee "$LOG"

echo
echo "Configuration file: $CONFIG_FILE"
echo "Deployment log:     $LOG"
