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
LOG="$LOG_DIR/az05c2a-private-rocky10-restore-runner-$STAMP.log"
CONFIG_FILE="$CONFIG_DIR/postgresql-restore-runner.env"
IMAGE_DIAGNOSTIC="$LOG_DIR/az05c2a-rocky10-images-$STAMP.txt"

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

# Rocky Linux 10 requires x86-64-v3. Prefer current-generation
# Intel or AMD D-series VM families and do not use older B-series sizes.
preferred = [
    "Standard_D2s_v6",
    "Standard_D2as_v6",
    "Standard_D2ds_v6",
    "Standard_D2ads_v6",
    "Standard_D2s_v5",
    "Standard_D2as_v5",
    "Standard_D2ds_v5",
    "Standard_D2ads_v5",
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
    "No approved current-generation two-vCore VM size is available "
    "for the Rocky Linux 10 restore runner in West US 3."
)
PY

    rm -f "$sku_json"
}

select_rocky10_image() {
    local image_json
    local rc

    image_json="$(mktemp)"
    rc=0

    az vm image list \
        --location "$LOCATION" \
        --publisher "$ROCKY_PUBLISHER" \
        --architecture x64 \
        --all \
        --output json > "$image_json"

    python3 - "$image_json" "$IMAGE_DIAGNOSTIC" <<'PY' || rc=$?
import json
import re
import sys
from pathlib import Path

items = json.loads(Path(sys.argv[1]).read_text())
diagnostic = Path(sys.argv[2])


def version_key(value):
    numbers = [int(part) for part in re.findall(r"\d+", value or "")]
    return tuple(numbers + [0] * (6 - len(numbers)))


candidates = []
all_rows = []

for item in items:
    publisher = str(item.get("publisher") or "")
    offer = str(item.get("offer") or "")
    sku = str(item.get("sku") or "")
    version = str(item.get("version") or "")
    urn = str(item.get("urn") or "")
    architecture = str(item.get("architecture") or "x64")

    row = f"{publisher}\t{offer}\t{sku}\t{version}\t{architecture}\t{urn}"
    all_rows.append(row)

    if publisher.lower() != "resf":
        continue

    if architecture and architecture.lower() not in {"x64", "x86_64", "amd64"}:
        continue

    text = f"{offer} {sku} {urn}".lower()

    if "rocky" not in text:
        continue

    if any(term in text for term in ("aarch64", "arm64", "hpc", "sap")):
        continue

    if not re.search(r"(^|[^0-9])10([^0-9]|$)", f"{offer} {sku}"):
        continue

    score = 100

    if "x86_64" in text or "x64" in text:
        score += 30

    if re.search(r"(^|[^0-9])10([^0-9]|$)", sku):
        score += 25

    if "base" in text:
        score += 20

    if "gen2" in text:
        score += 10

    if "minimal" in text:
        score -= 5

    candidates.append(
        {
            "score": score,
            "version_key": version_key(version),
            "publisher": publisher,
            "offer": offer,
            "sku": sku,
            "version": version,
            "urn": urn,
        }
    )

diagnostic.write_text(
    "publisher\toffer\tsku\tversion\tarchitecture\turn\n"
    + "\n".join(sorted(all_rows))
    + "\n"
)

if not candidates:
    print(
        f"No official RESF Rocky Linux 10 x86-64 image was found. "
        f"Review {diagnostic}",
        file=sys.stderr,
    )
    raise SystemExit(1)

selected = sorted(
    candidates,
    key=lambda item: (item["score"], item["version_key"]),
    reverse=True,
)[0]

print(
    "\t".join(
        [
            selected["urn"],
            selected["publisher"],
            selected["offer"],
            selected["sku"],
            selected["version"],
        ]
    )
)
PY

    rm -f "$image_json"
    return "$rc"
}

{
    section "AZ-05C2A - Rocky Linux 10 Private PostgreSQL Restore Runner"

    echo "Product: $PRODUCT_NAME"
    echo "Environment: $ENVIRONMENT"
    echo "Location: $LOCATION"
    echo "VM: $VM_NAME"
    echo "Required operating system: Rocky Linux 10.x"
    echo "Required publisher: $ROCKY_PUBLISHER"
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

    section "Attaching fixed outbound NAT to management subnet"

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

    section "Selecting Rocky Linux 10 image and VM size"

    if az vm show \
        --resource-group "$RG_DATA" \
        --name "$VM_NAME" \
        --output none \
        >/dev/null 2>&1; then

        echo "Existing restore-runner VM found."

        VM_SIZE="$(
            az vm show \
                --resource-group "$RG_DATA" \
                --name "$VM_NAME" \
                --query hardwareProfile.vmSize \
                --output tsv
        )"

        IMAGE_PUBLISHER="$(
            az vm show \
                --resource-group "$RG_DATA" \
                --name "$VM_NAME" \
                --query storageProfile.imageReference.publisher \
                --output tsv
        )"

        IMAGE_OFFER="$(
            az vm show \
                --resource-group "$RG_DATA" \
                --name "$VM_NAME" \
                --query storageProfile.imageReference.offer \
                --output tsv
        )"

        IMAGE_SKU="$(
            az vm show \
                --resource-group "$RG_DATA" \
                --name "$VM_NAME" \
                --query storageProfile.imageReference.sku \
                --output tsv
        )"

        IMAGE_VERSION="$(
            az vm show \
                --resource-group "$RG_DATA" \
                --name "$VM_NAME" \
                --query storageProfile.imageReference.version \
                --output tsv
        )"

        IMAGE_URN="${IMAGE_PUBLISHER}:${IMAGE_OFFER}:${IMAGE_SKU}:${IMAGE_VERSION}"

        if [ "${IMAGE_PUBLISHER,,}" != "resf" ]; then
            echo "ERROR: Existing VM is not from the official RESF publisher."
            echo "Existing publisher: $IMAGE_PUBLISHER"
            exit 1
        fi

        if ! grep -Eiq '(^|[^0-9])10([^0-9]|$)' <<<"$IMAGE_OFFER $IMAGE_SKU"; then
            echo "ERROR: Existing VM is not a Rocky Linux 10 image."
            echo "Existing image: $IMAGE_URN"
            exit 1
        fi
    else
        VM_SIZE="$(select_vm_size)"

        if ! IMAGE_SELECTION="$(select_rocky10_image)"; then
            echo "ERROR: Rocky Linux 10 image discovery failed."
            echo "Diagnostic file: $IMAGE_DIAGNOSTIC"
            exit 1
        fi

        IFS=$'\t' read -r \
            IMAGE_URN \
            IMAGE_PUBLISHER \
            IMAGE_OFFER \
            IMAGE_SKU \
            IMAGE_VERSION <<< "$IMAGE_SELECTION"

        if [ -z "$IMAGE_URN" ]; then
            echo "ERROR: Rocky Linux image discovery returned an empty URN."
            exit 1
        fi

        az vm image show \
            --location "$LOCATION" \
            --urn "$IMAGE_URN" \
            --output none

        if TERMS_JSON="$(
            az vm image terms show \
                --urn "$IMAGE_URN" \
                --output json 2>/dev/null
        )"; then
            TERMS_ACCEPTED="$(
                python3 -c 'import json,sys; print(str(json.load(sys.stdin).get("accepted", False)).lower())' \
                    <<< "$TERMS_JSON"
            )"

            if [ "$TERMS_ACCEPTED" != "true" ]; then
                az vm image terms accept \
                    --urn "$IMAGE_URN" \
                    --only-show-errors \
                    --output none

                echo "Accepted required Marketplace terms for the Rocky image."
            fi
        fi
    fi

    echo "Selected VM size: $VM_SIZE"
    echo "Selected image publisher: $IMAGE_PUBLISHER"
    echo "Selected image offer: $IMAGE_OFFER"
    echo "Selected image SKU: $IMAGE_SKU"
    echo "Selected image version: $IMAGE_VERSION"
    echo "Selected image URN: $IMAGE_URN"

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
                "operating-system=rocky-linux-10" \
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

    section "Creating temporary Rocky Linux 10 restore VM"

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
                "image-publisher=$IMAGE_PUBLISHER" \
                "public-access=disabled" \
                "delete-after=migration-validation" \
            --only-show-errors \
            --output none

        echo "Created temporary Rocky Linux 10 restore VM: $VM_NAME"
    fi

    az vm wait \
        --resource-group "$RG_DATA" \
        --name "$VM_NAME" \
        --created \
        --interval 15 \
        --timeout 1200

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

    section "Validating Rocky Linux 10 and private connectivity"

    CONNECTIVITY_SCRIPT="$(mktemp)"

    cat > "$CONNECTIVITY_SCRIPT" <<EOF
#!/usr/bin/env bash
set -Eeuo pipefail

source /etc/os-release

if [ "\${ID:-}" != "rocky" ]; then
    echo "ERROR: Restore runner is not Rocky Linux."
    echo "Detected ID=\${ID:-unknown}"
    exit 1
fi

case "\${VERSION_ID:-}" in
    10|10.*)
        ;;
    *)
        echo "ERROR: Restore runner is not Rocky Linux 10.x."
        echo "Detected VERSION_ID=\${VERSION_ID:-unknown}"
        exit 1
        ;;
esac

echo "Initial Rocky version: \$VERSION_ID"

dnf -y upgrade --refresh

dnf -y install \
    curl \
    bind-utils \
    iproute \
    procps-ng

source /etc/os-release

if [ "\${ID:-}" != "rocky" ]; then
    echo "ERROR: Distribution changed unexpectedly after update."
    exit 1
fi

case "\${VERSION_ID:-}" in
    10|10.*)
        ;;
    *)
        echo "ERROR: Rocky major version is not 10 after update."
        exit 1
        ;;
esac

echo "PHD_ROCKY_VERSION=\$VERSION_ID"
echo "Kernel: \$(uname -r)"
echo "Architecture: \$(uname -m)"

if [ "\$(uname -m)" != "x86_64" ]; then
    echo "ERROR: Restore runner architecture is not x86_64."
    exit 1
fi

if systemctl is-active --quiet waagent; then
    echo "Azure Linux Agent: active (waagent)"
elif systemctl is-active --quiet walinuxagent; then
    echo "Azure Linux Agent: active (walinuxagent)"
else
    echo "WARNING: Azure Linux Agent service name was not detected."
fi

echo "Private addresses:"
hostname -I

echo "PostgreSQL DNS:"
getent ahostsv4 "$POSTGRES_FQDN"

echo "PostgreSQL TCP 5432:"
timeout 10 bash -c "cat < /dev/null > /dev/tcp/$POSTGRES_FQDN/5432"
echo "TCP connection succeeded."

echo "Managed identity storage token:"
curl -fsS \
    -H Metadata:true \
    'http://169.254.169.254/metadata/identity/oauth2/token?api-version=2018-02-01&resource=https%3A%2F%2Fstorage.azure.com%2F' \
    >/dev/null
echo "Storage token acquisition succeeded."

echo "Managed identity Key Vault token:"
curl -fsS \
    -H Metadata:true \
    'http://169.254.169.254/metadata/identity/oauth2/token?api-version=2018-02-01&resource=https%3A%2F%2Fvault.azure.net' \
    >/dev/null
echo "Key Vault token acquisition succeeded."

echo "Outbound HTTPS:"
curl -fsS --max-time 20 -o /dev/null https://packages.microsoft.com
echo "Outbound HTTPS succeeded."

echo "PRIVATE ROCKY 10 RESTORE RUNNER CONNECTIVITY READY"
EOF

    RUN_OUTPUT=""

    for attempt in $(seq 1 20); do
        if RUN_OUTPUT="$(
            az vm run-command invoke \
                --resource-group "$RG_DATA" \
                --name "$VM_NAME" \
                --command-id RunShellScript \
                --scripts @"$CONNECTIVITY_SCRIPT" \
                --query 'value[0].message' \
                --output tsv
        )"; then
            break
        fi

        if [ "$attempt" = "20" ]; then
            echo "ERROR: Azure Run Command did not become available."
            rm -f "$CONNECTIVITY_SCRIPT"
            exit 1
        fi

        echo "Run Command is not ready yet; retrying in 30 seconds."
        sleep 30
    done

    rm -f "$CONNECTIVITY_SCRIPT"

    printf '%s\n' "$RUN_OUTPUT"

    if ! grep -q \
        'PRIVATE ROCKY 10 RESTORE RUNNER CONNECTIVITY READY' \
        <<< "$RUN_OUTPUT"; then

        echo "ERROR: Rocky Linux 10 connectivity validation did not finish."
        exit 1
    fi

    ROCKY_VERSION="$(
        sed -n 's/.*PHD_ROCKY_VERSION=//p' <<< "$RUN_OUTPUT" |
            tail -n 1 |
            tr -d '\r'
    )"

    if [[ "$ROCKY_VERSION" != 10* ]]; then
        echo "ERROR: Validated Rocky version is '$ROCKY_VERSION'."
        exit 1
    fi

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

RESTORE_RUNNER_OS=Rocky Linux
RESTORE_RUNNER_OS_MAJOR=10
RESTORE_RUNNER_OS_VERSION=$ROCKY_VERSION
RESTORE_RUNNER_IMAGE_PUBLISHER=$IMAGE_PUBLISHER
RESTORE_RUNNER_IMAGE_OFFER=$IMAGE_OFFER
RESTORE_RUNNER_IMAGE_SKU=$IMAGE_SKU
RESTORE_RUNNER_IMAGE_VERSION=$IMAGE_VERSION
RESTORE_RUNNER_IMAGE_URN=$IMAGE_URN

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
    echo "Operating system: Rocky Linux $ROCKY_VERSION"
    echo "Image publisher: $IMAGE_PUBLISHER"
    echo "Image offer: $IMAGE_OFFER"
    echo "Image SKU: $IMAGE_SKU"
    echo "Image version: $IMAGE_VERSION"
    echo "VM size: $VM_SIZE"
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
    echo "PRIVATE ROCKY 10 POSTGRESQL RESTORE RUNNER READY"
    echo "************************************************************"

} 2>&1 | tee "$LOG"

echo
echo "Configuration file: $CONFIG_FILE"
echo "Image diagnostic:  $IMAGE_DIAGNOSTIC"
echo "Deployment log:    $LOG"
