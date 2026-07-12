#!/usr/bin/env bash
set -Eeuo pipefail

# Creates a temporary private Rocky Linux 9 restore runner in West US 3.
# The VM has no public IP and is administered with Azure Run Command.
# The script never falls back to Ubuntu or another Linux distribution.

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
REQUIRED_OS_ID="rocky"
REQUIRED_OS_MAJOR="9"

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
LOG="$LOG_DIR/az05c2a-private-rocky-restore-runner-$STAMP.log"
CONFIG_FILE="$CONFIG_DIR/postgresql-restore-runner.env"
IMAGE_DIAGNOSTIC="$LOG_DIR/az05c2a-rocky-image-discovery-$STAMP.txt"

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
    for item in items:
        if item.get("name") != candidate:
            continue

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

select_rocky_image() {
    local image_json
    image_json="$(mktemp)"

    echo "Querying Marketplace images for Rocky Linux 9 in $LOCATION..." >&2

    az vm image list \
        --location "$LOCATION" \
        --all \
        --output json > "$image_json"

    python3 - "$image_json" "$IMAGE_DIAGNOSTIC" <<'PY'
import json
import re
import sys
from pathlib import Path

items = json.loads(Path(sys.argv[1]).read_text())
diagnostic_path = Path(sys.argv[2])


def natural_version(value):
    parts = re.split(r"([0-9]+)", str(value))
    return tuple(int(part) if part.isdigit() else part.lower() for part in parts)


def is_rocky_9(item):
    publisher = str(item.get("publisher") or "")
    offer = str(item.get("offer") or "")
    sku = str(item.get("sku") or "")
    urn = str(item.get("urn") or "")
    combined = " ".join([publisher, offer, sku, urn]).lower()

    rocky = "rocky" in combined or publisher.lower() == "resf"
    x86 = not any(token in combined for token in ["arm64", "aarch64", "-arm", "_arm"])
    major_9 = bool(
        re.search(r"(^|[^0-9])9([^0-9]|$)", offer)
        or re.search(r"(^|[^0-9])9([^0-9]|$)", sku)
        or "rockylinux-9" in combined
        or "rocky-linux-9" in combined
    )

    return rocky and x86 and major_9 and bool(urn)


candidates = [item for item in items if is_rocky_9(item)]

lines = [
    "Rocky Linux 9 x86-64 image candidates:",
]

for item in candidates:
    lines.append(
        " | ".join(
            [
                str(item.get("publisher") or ""),
                str(item.get("offer") or ""),
                str(item.get("sku") or ""),
                str(item.get("version") or ""),
                str(item.get("urn") or ""),
            ]
        )
    )

diagnostic_path.write_text("\n".join(lines) + "\n")

if not candidates:
    raise SystemExit(
        "No Rocky Linux 9 x86-64 Marketplace image was found. "
        f"Review {diagnostic_path}. No VM was created."
    )


def score(item):
    publisher = str(item.get("publisher") or "").lower()
    offer = str(item.get("offer") or "").lower()
    sku = str(item.get("sku") or "").lower()
    urn = str(item.get("urn") or "").lower()
    combined = " ".join([publisher, offer, sku, urn])

    value = 0

    if publisher == "resf":
        value += 100
    elif "rocky" in publisher:
        value += 80

    if "x86_64" in offer or "x86-64" in offer:
        value += 25

    if "base" in sku:
        value += 20

    if "gen2" in sku or "gen2" in offer:
        value += 10

    if "free" in combined:
        value += 5

    return value, natural_version(item.get("version") or "")


selected = sorted(candidates, key=score)[-1]

print(
    "|".join(
        [
            str(selected.get("publisher") or ""),
            str(selected.get("offer") or ""),
            str(selected.get("sku") or ""),
            str(selected.get("version") or ""),
            str(selected.get("urn") or ""),
        ]
    )
)
PY

    rm -f "$image_json"
}

{
    section "AZ-05C2A - Private Rocky Linux PostgreSQL Restore Runner"

    echo "Product: $PRODUCT_NAME"
    echo "Environment: $ENVIRONMENT"
    echo "Location: $LOCATION"
    echo "VM: $VM_NAME"
    echo "Required operating system: Rocky Linux 9 x86-64"
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

    section "Selecting temporary VM size and Rocky Linux image"

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

        VM_IMAGE="existing-vm"
        ROCKY_PUBLISHER="existing-vm"
        ROCKY_OFFER="existing-vm"
        ROCKY_SKU="existing-vm"
        ROCKY_VERSION="existing-vm"

        echo "Existing restore runner found."
    else
        VM_SIZE="$(select_vm_size)"
        IMAGE_SELECTION="$(select_rocky_image)"

        IFS='|' read -r \
            ROCKY_PUBLISHER \
            ROCKY_OFFER \
            ROCKY_SKU \
            ROCKY_VERSION \
            VM_IMAGE <<< "$IMAGE_SELECTION"

        if [ -z "$VM_IMAGE" ]; then
            echo "ERROR: Rocky Linux image selection returned an empty URN."
            exit 1
        fi
    fi

    echo "Selected VM size: $VM_SIZE"
    echo "Selected Rocky publisher: $ROCKY_PUBLISHER"
    echo "Selected Rocky offer: $ROCKY_OFFER"
    echo "Selected Rocky SKU: $ROCKY_SKU"
    echo "Selected Rocky version: $ROCKY_VERSION"
    echo "Selected Rocky image: $VM_IMAGE"

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
                "operating-system=rocky-linux-9" \
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

    section "Creating temporary private Rocky Linux restore VM"

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
        IMAGE_PLAN="$(
            az vm image show \
                --urn "$VM_IMAGE" \
                --query plan \
                --output tsv 2>/dev/null || true
        )"

        if [ -n "$IMAGE_PLAN" ]; then
            echo "ERROR: The selected Rocky image requires Marketplace terms."
            echo "Review and accept the image terms before creating the VM."
            echo "Image: $VM_IMAGE"
            exit 1
        fi

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
                "operating-system=rocky-linux-9" \
                "public-access=disabled" \
                "delete-after=migration-validation" \
            --only-show-errors \
            --output none

        echo "Created temporary private Rocky Linux restore VM: $VM_NAME"
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

    section "Validating Rocky Linux, private connectivity, and identity"

    CONNECTIVITY_SCRIPT="$(mktemp)"

    cat > "$CONNECTIVITY_SCRIPT" <<EOF
#!/usr/bin/env bash
set -Eeuo pipefail

. /etc/os-release

echo "Operating system: \${PRETTY_NAME:-unknown}"

echo "OS ID: \${ID:-unknown}"
echo "OS version: \${VERSION_ID:-unknown}"

if [ "\${ID:-}" != "$REQUIRED_OS_ID" ]; then
    echo "ERROR: Expected Rocky Linux, but OS ID is '\${ID:-unknown}'."
    exit 1
fi

case "\${VERSION_ID:-}" in
    9|9.*)
        ;;
    *)
        echo "ERROR: Expected Rocky Linux 9, but version is '\${VERSION_ID:-unknown}'."
        exit 1
        ;;
esac

if ! command -v curl >/dev/null 2>&1; then
    dnf -y install curl
fi

if ! command -v timeout >/dev/null 2>&1; then
    dnf -y install coreutils
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

echo "ROCKY LINUX PRIVATE RESTORE RUNNER CONNECTIVITY READY"
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

RESTORE_RUNNER_OS=Rocky_Linux_9
RESTORE_RUNNER_IMAGE_PUBLISHER=$ROCKY_PUBLISHER
RESTORE_RUNNER_IMAGE_OFFER=$ROCKY_OFFER
RESTORE_RUNNER_IMAGE_SKU=$ROCKY_SKU
RESTORE_RUNNER_IMAGE_VERSION=$ROCKY_VERSION
RESTORE_RUNNER_IMAGE_URN=$VM_IMAGE

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

    echo "Temporary Rocky Linux 9 restore VM: configured"
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
    echo "Rocky image diagnostic: $IMAGE_DIAGNOSTIC"
    echo
    echo "************************************************************"
    echo "ROCKY LINUX PRIVATE POSTGRESQL RESTORE RUNNER READY"
    echo "************************************************************"

} 2>&1 | tee "$LOG"

echo
echo "Configuration file: $CONFIG_FILE"
echo "Deployment log:     $LOG"
