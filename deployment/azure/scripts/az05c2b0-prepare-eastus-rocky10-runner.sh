#!/usr/bin/env bash
set -Eeuo pipefail

LOCATION="eastus"
RG_MIGRATION="rg-project-health-dashboard-test-migration-eastus"
VM_NAME="vm-phd-test-db-migrate-eus"
NIC_NAME="nic-phd-test-db-migrate-eus"
RUN_COMMAND_NAME="phd-prepare-rocky10"

STORAGE_ACCOUNT="stphdtest7825cc"
KEY_VAULT="kv-phd-t-eus-7825cc"
POSTGRES_FQDN="pg-phd-test-w3-7825cc.postgres.database.azure.com"

BASE_DIR="$HOME/project-health-dashboard-azure"
LOG_DIR="$BASE_DIR/logs"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
LOG="$LOG_DIR/az05c2b0-prepare-eastus-rocky10-runner-$STAMP.log"
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

ensure_role() {
    local principal_id="$1"
    local role_name="$2"
    local scope="$3"
    local count

    count="$(
        az role assignment list \
            --assignee "$principal_id" \
            --scope "$scope" \
            --role "$role_name" \
            --query 'length(@)' \
            --output tsv \
            2>/dev/null || echo 0
    )"

    if [ "${count:-0}" != "0" ]; then
        echo "ROLE_ASSIGNMENT_${role_name// /_}=existing"
        return 0
    fi

    az role assignment create \
        --assignee-object-id "$principal_id" \
        --assignee-principal-type ServicePrincipal \
        --role "$role_name" \
        --scope "$scope" \
        --only-show-errors \
        --output none

    echo "ROLE_ASSIGNMENT_${role_name// /_}=created"
}

{
    section "AZ-05C2B0 - Prepare East US Rocky Linux 10 Restore Runner"

    echo "TIME=$(date -u -Is)"
    echo "Location: $LOCATION"
    echo "Resource group: $RG_MIGRATION"
    echo "VM: $VM_NAME"
    echo "NIC: $NIC_NAME"
    echo "Run Command: $RUN_COMMAND_NAME"

    section "Checking VM provisioning state"

    if ! az vm show \
        --resource-group "$RG_MIGRATION" \
        --name "$VM_NAME" \
        --output none \
        >/dev/null 2>&1; then

        fail "The East US restore-runner VM does not exist."
    fi

    VM_PROVISIONING_STATE="$(
        az vm show \
            --resource-group "$RG_MIGRATION" \
            --name "$VM_NAME" \
            --query provisioningState \
            --output tsv
    )"

    VM_POWER_STATE="$(
        az vm get-instance-view \
            --resource-group "$RG_MIGRATION" \
            --name "$VM_NAME" \
            --query "instanceView.statuses[?starts_with(code, 'PowerState/')].displayStatus | [0]" \
            --output tsv
    )"

    VM_PRIVATE_IP="$(
        az network nic show \
            --resource-group "$RG_MIGRATION" \
            --name "$NIC_NAME" \
            --query 'ipConfigurations[0].privateIPAddress' \
            --output tsv
    )"

    VM_PUBLIC_IP_ID="$(
        az network nic show \
            --resource-group "$RG_MIGRATION" \
            --name "$NIC_NAME" \
            --query 'ipConfigurations[0].publicIPAddress.id' \
            --output tsv
    )"

    echo "VM_PROVISIONING_STATE=$VM_PROVISIONING_STATE"
    echo "VM_POWER_STATE=$VM_POWER_STATE"
    echo "VM_PRIVATE_IP=$VM_PRIVATE_IP"
    echo "VM_PUBLIC_IP=${VM_PUBLIC_IP_ID:-none}"

    [ -z "$VM_PUBLIC_IP_ID" ] || fail "The restore runner unexpectedly has a public IP."

    if [ "$VM_PROVISIONING_STATE" != "Succeeded" ]; then
        echo "PREPARATION_DECISION=WAIT_FOR_VM"
        echo "No role assignment or guest command was submitted."
        echo
        echo "************************************************************"
        echo "EASTUS ROCKY 10 RESTORE RUNNER NOT READY YET"
        echo "************************************************************"
        exit 0
    fi

    section "Resolving the VM managed identity and access scopes"

    VM_PRINCIPAL_ID="$(
        az vm identity show \
            --resource-group "$RG_MIGRATION" \
            --name "$VM_NAME" \
            --query principalId \
            --output tsv
    )"

    [ -n "$VM_PRINCIPAL_ID" ] || fail "VM system-managed identity principal ID is empty."

    STORAGE_ACCOUNT_ID="$(
        az resource list \
            --name "$STORAGE_ACCOUNT" \
            --resource-type Microsoft.Storage/storageAccounts \
            --query '[0].id' \
            --output tsv
    )"

    KEY_VAULT_ID="$(
        az resource list \
            --name "$KEY_VAULT" \
            --resource-type Microsoft.KeyVault/vaults \
            --query '[0].id' \
            --output tsv
    )"

    [ -n "$STORAGE_ACCOUNT_ID" ] || fail "Storage account resource ID could not be resolved."
    [ -n "$KEY_VAULT_ID" ] || fail "East US Key Vault resource ID could not be resolved."

    echo "VM_PRINCIPAL_ID=$VM_PRINCIPAL_ID"
    echo "STORAGE_ACCOUNT_ID=$STORAGE_ACCOUNT_ID"
    echo "KEY_VAULT_ID=$KEY_VAULT_ID"

    section "Assigning least-privilege managed-identity roles"

    ensure_role \
        "$VM_PRINCIPAL_ID" \
        "Storage Blob Data Reader" \
        "$STORAGE_ACCOUNT_ID"

    ensure_role \
        "$VM_PRINCIPAL_ID" \
        "Key Vault Secrets User" \
        "$KEY_VAULT_ID"

    section "Building Rocky Linux 10 guest preparation command"

    GUEST_SCRIPT="$WORK_DIR/prepare-rocky10.sh"

    cat > "$GUEST_SCRIPT" <<EOF
#!/usr/bin/env bash
set -Eeuo pipefail

STATE_DIR="/var/lib/project-health-dashboard"
LOG_FILE="/var/log/phd-az05c2b0-prepare.log"
MARKER="\$STATE_DIR/az05c2b0-ready.txt"
POSTGRES_FQDN="$POSTGRES_FQDN"

mkdir -p "\$STATE_DIR"
exec > >(tee -a "\$LOG_FILE") 2>&1

echo "AZ-05C2B0 guest preparation started at \$(date -u -Is)"

source /etc/os-release

if [ "\${ID:-}" != "rocky" ]; then
    echo "ERROR: Expected Rocky Linux; detected ID=\${ID:-unknown}."
    exit 1
fi

case "\${VERSION_ID:-}" in
    10|10.*)
        ;;
    *)
        echo "ERROR: Expected Rocky Linux 10.x; detected VERSION_ID=\${VERSION_ID:-unknown}."
        exit 1
        ;;
esac

if [ "\$(uname -m)" != "x86_64" ]; then
    echo "ERROR: Expected x86_64 architecture; detected \$(uname -m)."
    exit 1
fi

echo "ROCKY_INITIAL_VERSION=\$VERSION_ID"
echo "ARCHITECTURE=\$(uname -m)"

dnf -y upgrade --refresh

dnf -y install \
    bind-utils \
    curl \
    gzip \
    jq \
    procps-ng \
    tar \
    unzip

source /etc/os-release

echo "ROCKY_VALIDATED_VERSION=\$VERSION_ID"
echo "KERNEL=\$(uname -r)"

echo "PRIVATE_ADDRESSES=\$(hostname -I)"

echo "PostgreSQL DNS resolution:"
getent ahostsv4 "\$POSTGRES_FQDN"

echo "PostgreSQL TCP 5432 connectivity:"
timeout 10 bash -c "cat < /dev/null > /dev/tcp/\$POSTGRES_FQDN/5432"
echo "POSTGRES_TCP_5432=reachable"

echo "Managed identity storage token test:"
curl -fsS \
    -H Metadata:true \
    'http://169.254.169.254/metadata/identity/oauth2/token?api-version=2018-02-01&resource=https%3A%2F%2Fstorage.azure.com%2F' \
    >/dev/null
echo "MANAGED_IDENTITY_STORAGE_TOKEN=success"

echo "Managed identity Key Vault token test:"
curl -fsS \
    -H Metadata:true \
    'http://169.254.169.254/metadata/identity/oauth2/token?api-version=2018-02-01&resource=https%3A%2F%2Fvault.azure.net' \
    >/dev/null
echo "MANAGED_IDENTITY_KEYVAULT_TOKEN=success"

echo "Outbound HTTPS test:"
curl -fsS --max-time 20 -o /dev/null https://packages.microsoft.com
echo "OUTBOUND_HTTPS=success"

cat > "\$MARKER" <<MARKER
AZ05C2B0_READY=true
COMPLETED_AT=\$(date -u -Is)
ROCKY_VERSION=\$VERSION_ID
ARCHITECTURE=\$(uname -m)
POSTGRES_FQDN=\$POSTGRES_FQDN
POSTGRES_TCP_5432=reachable
MANAGED_IDENTITY_STORAGE_TOKEN=success
MANAGED_IDENTITY_KEYVAULT_TOKEN=success
OUTBOUND_HTTPS=success
MARKER

chmod 600 "\$MARKER"

echo "PRIVATE ROCKY 10 RESTORE RUNNER PREPARATION READY"
EOF

    SCRIPT_CONTENT="$(cat "$GUEST_SCRIPT")"

    section "Submitting asynchronous managed Run Command"

    if az vm run-command show \
        --resource-group "$RG_MIGRATION" \
        --vm-name "$VM_NAME" \
        --run-command-name "$RUN_COMMAND_NAME" \
        --output none \
        >/dev/null 2>&1; then

        az vm run-command update \
            --resource-group "$RG_MIGRATION" \
            --vm-name "$VM_NAME" \
            --run-command-name "$RUN_COMMAND_NAME" \
            --location "$LOCATION" \
            --async-execution true \
            --script "$SCRIPT_CONTENT" \
            --timeout-in-seconds 3600 \
            --no-wait \
            --only-show-errors \
            --output none

        echo "RUN_COMMAND_ACTION=updated-and-submitted"
    else
        az vm run-command create \
            --resource-group "$RG_MIGRATION" \
            --vm-name "$VM_NAME" \
            --run-command-name "$RUN_COMMAND_NAME" \
            --location "$LOCATION" \
            --async-execution true \
            --script "$SCRIPT_CONTENT" \
            --timeout-in-seconds 3600 \
            --no-wait \
            --only-show-errors \
            --output none

        echo "RUN_COMMAND_ACTION=created-and-submitted"
    fi

    echo "RUN_COMMAND_NAME=$RUN_COMMAND_NAME"
    echo "PREPARATION_DECISION=SUBMITTED"
    echo
    echo "Azure will continue guest preparation independently of Cloud Shell."
    echo
    echo "************************************************************"
    echo "EASTUS ROCKY 10 RESTORE RUNNER PREPARATION SUBMITTED"
    echo "************************************************************"

} 2>&1 | tee "$LOG"

echo
echo "Preparation submission log: $LOG"
