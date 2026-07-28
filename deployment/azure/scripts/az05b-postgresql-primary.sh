#!/usr/bin/env bash
set -Eeuo pipefail

PRODUCT_NAME="Project Health Dashboard"
ENVIRONMENT="test"

PRIMARY_LOCATION="westus3"
SECONDARY_LOCATION="eastus"

RG_SHARED="rg-project-health-dashboard-shared-global"
RG_PRIMARY_NETWORK="rg-project-health-dashboard-test-network-westus3"
RG_PRIMARY_DATA="rg-project-health-dashboard-test-data-westus3"
RG_SECONDARY_NETWORK="rg-project-health-dashboard-test-network-eastus"
RG_SECONDARY_DATA="rg-project-health-dashboard-test-data-eastus"

PRIMARY_VNET="vnet-phd-test-westus3"
SECONDARY_VNET="vnet-phd-test-eastus"
POSTGRES_SUBNET="snet-postgresql"
POSTGRES_PRIVATE_DNS_ZONE="phd-test.postgres.database.azure.com"

WEST_KEYVAULT_RESOURCE_GROUP="rg-project-health-dashboard-test-data-westus3"
EAST_KEYVAULT_RESOURCE_GROUP="rg-project-health-dashboard-test-data-eastus"

DATABASE_NAME="project_health_dashboard"
ADMIN_USER="phdpgadmin"
POSTGRES_VERSION="16"
STORAGE_GIB="32"
BACKUP_RETENTION_DAYS="35"

BASE_DIR="$HOME/project-health-dashboard-azure"
CONFIG_DIR="$BASE_DIR/config"
LOG_DIR="$BASE_DIR/logs"

STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
LOG="$LOG_DIR/az05b-postgresql-primary-$STAMP.log"
CONFIG_FILE="$CONFIG_DIR/postgresql-primary.env"

mkdir -p "$CONFIG_DIR" "$LOG_DIR"

SUBSCRIPTION_ID="$(az account show --query id --output tsv)"
UNIQUE_SUFFIX="$(printf '%s' "$SUBSCRIPTION_ID" | sha256sum | cut -c1-6)"

PRIMARY_SERVER="pg-phd-test-w3-${UNIQUE_SUFFIX}"
PLANNED_REPLICA="pg-phd-test-eus-${UNIQUE_SUFFIX}"

WEST_KEYVAULT="kv-phd-t-w3-${UNIQUE_SUFFIX}"
EAST_KEYVAULT="kv-phd-t-eus-${UNIQUE_SUFFIX}"

SECRET_ADMIN_USER="postgres-admin-username"
SECRET_ADMIN_PASSWORD="postgres-admin-password"
SECRET_PRIMARY_HOST="postgres-primary-host"
SECRET_DATABASE_NAME="postgres-database-name"
SECRET_PORT="postgres-port"
SECRET_SSL_MODE="postgres-ssl-mode"

section() {
    echo
    echo "============================================================"
    echo "$1"
    echo "============================================================"
}

secret_exists() {
    local vault="$1"
    local secret_name="$2"

    az keyvault secret show \
        --vault-name "$vault" \
        --name "$secret_name" \
        --query id \
        --output tsv \
        >/dev/null 2>&1
}

set_secret_both_regions() {
    local secret_name="$1"
    local secret_value="$2"

    az keyvault secret set \
        --vault-name "$WEST_KEYVAULT" \
        --name "$secret_name" \
        --value "$secret_value" \
        --only-show-errors \
        --output none

    az keyvault secret set \
        --vault-name "$EAST_KEYVAULT" \
        --name "$secret_name" \
        --value "$secret_value" \
        --only-show-errors \
        --output none
}

select_common_sku() {
    local west_file east_file

    west_file="$(mktemp)"
    east_file="$(mktemp)"

    trap 'rm -f "$west_file" "$east_file"' RETURN

    az postgres flexible-server list-skus \
        --location "$PRIMARY_LOCATION" \
        --output json > "$west_file"

    az postgres flexible-server list-skus \
        --location "$SECONDARY_LOCATION" \
        --output json > "$east_file"

    python3 - "$west_file" "$east_file" <<'PY'
import json
import sys
from pathlib import Path

west = json.loads(Path(sys.argv[1]).read_text())
east = json.loads(Path(sys.argv[2]).read_text())

candidates = [
    "Standard_D2ds_v5",
    "Standard_D2ads_v5",
    "Standard_D2s_v5",
    "Standard_D2ds_v4",
    "Standard_D2s_v3",
]

def collect_strings(value):
    result = set()
    if isinstance(value, dict):
        for key, child in value.items():
            if key in {"name", "skuName"} and isinstance(child, str):
                result.add(child)
            result.update(collect_strings(child))
    elif isinstance(value, list):
        for child in value:
            result.update(collect_strings(child))
    return result

west_names = collect_strings(west)
east_names = collect_strings(east)

for candidate in candidates:
    if candidate in west_names and candidate in east_names:
        print(candidate)
        raise SystemExit(0)

raise SystemExit(
    "No preferred two-vCore General Purpose SKU was found in both regions."
)
PY
}

validate_server_json() {
    local json_file="$1"

    python3 - "$json_file" <<'PY'
import json
import sys
from pathlib import Path

server = json.loads(Path(sys.argv[1]).read_text())
errors = []

state = str(server.get("state", ""))
version = str(server.get("version", ""))
storage = server.get("storage") or {}
backup = server.get("backup") or {}
ha = server.get("highAvailability") or {}
network = server.get("network") or {}

if state.lower() != "ready":
    errors.append(f"server state is {state!r}, expected 'Ready'")
if not version.startswith("16"):
    errors.append(f"PostgreSQL version is {version!r}, expected version 16")
if str(storage.get("autoGrow", "")).lower() != "enabled":
    errors.append("storage autogrow is not enabled")
if int(storage.get("storageSizeGb") or 0) < 32:
    errors.append("storage is smaller than 32 GiB")
if int(backup.get("backupRetentionDays") or 0) != 35:
    errors.append("backup retention is not 35 days")
if str(backup.get("geoRedundantBackup", "")).lower() != "enabled":
    errors.append("geo-redundant backup is not enabled")
if str(ha.get("mode", "")).lower() != "zoneredundant":
    errors.append(f"HA mode is {ha.get('mode')!r}, expected 'ZoneRedundant'")
if not network.get("delegatedSubnetResourceId"):
    errors.append("delegated subnet is missing")
if not network.get("privateDnsZoneArmResourceId"):
    errors.append("private DNS zone is missing")

if errors:
    for item in errors:
        print(f"ERROR: {item}", file=sys.stderr)
    raise SystemExit(1)

print("Primary PostgreSQL validation passed.")
PY
}

{
    section "AZ-05B - Project Health Dashboard PostgreSQL Primary"

    echo "Product: $PRODUCT_NAME"
    echo "Environment: $ENVIRONMENT"
    echo "Primary region: $PRIMARY_LOCATION"
    echo "Secondary region: $SECONDARY_LOCATION"
    echo "Primary server: $PRIMARY_SERVER"
    echo "Planned replica: $PLANNED_REPLICA"
    echo "Database: $DATABASE_NAME"
    echo "TIME=$(date -u -Is)"

    section "Validating Azure context and prerequisites"

    az account show \
        --query '{subscription:name, subscriptionId:id, tenantId:tenantId, state:state}' \
        --output table

    az keyvault show \
        --resource-group "$WEST_KEYVAULT_RESOURCE_GROUP" \
        --name "$WEST_KEYVAULT" \
        --query '{name:name, location:location, rbac:properties.enableRbacAuthorization, state:properties.provisioningState}' \
        --output table

    az keyvault show \
        --resource-group "$EAST_KEYVAULT_RESOURCE_GROUP" \
        --name "$EAST_KEYVAULT" \
        --query '{name:name, location:location, rbac:properties.enableRbacAuthorization, state:properties.provisioningState}' \
        --output table

    PRIMARY_SUBNET_ID="$(
        az network vnet subnet show \
            --resource-group "$RG_PRIMARY_NETWORK" \
            --vnet-name "$PRIMARY_VNET" \
            --name "$POSTGRES_SUBNET" \
            --query id \
            --output tsv
    )"

    SECONDARY_SUBNET_ID="$(
        az network vnet subnet show \
            --resource-group "$RG_SECONDARY_NETWORK" \
            --vnet-name "$SECONDARY_VNET" \
            --name "$POSTGRES_SUBNET" \
            --query id \
            --output tsv
    )"

    PRIVATE_DNS_ZONE_ID="$(
        az network private-dns zone show \
            --resource-group "$RG_SHARED" \
            --name "$POSTGRES_PRIVATE_DNS_ZONE" \
            --query id \
            --output tsv
    )"

    echo "Primary subnet: $PRIMARY_SUBNET_ID"
    echo "Secondary subnet reserved for replica: $SECONDARY_SUBNET_ID"
    echo "Private DNS zone: $PRIVATE_DNS_ZONE_ID"

    section "Selecting a common General Purpose SKU"

    SKU_NAME="$(select_common_sku)"
    echo "Selected SKU available in both regions: $SKU_NAME"

    section "Preparing PostgreSQL administrator secrets"

    if secret_exists "$WEST_KEYVAULT" "$SECRET_ADMIN_PASSWORD"; then
        ADMIN_PASSWORD="$(
            az keyvault secret show \
                --vault-name "$WEST_KEYVAULT" \
                --name "$SECRET_ADMIN_PASSWORD" \
                --query value \
                --output tsv
        )"
        echo "Existing PostgreSQL administrator password secret found."
    else
        ADMIN_PASSWORD="$(python3 - <<'PY'
import secrets
import string

alphabet = string.ascii_letters + string.digits + "!@#%*-_=+"
while True:
    value = "".join(secrets.choice(alphabet) for _ in range(40))
    if (
        any(c.islower() for c in value)
        and any(c.isupper() for c in value)
        and any(c.isdigit() for c in value)
        and any(c in "!@#%*-_=+" for c in value)
    ):
        print(value)
        break
PY
        )"

        set_secret_both_regions "$SECRET_ADMIN_PASSWORD" "$ADMIN_PASSWORD"
        echo "Generated and stored PostgreSQL administrator password in both Key Vaults."
    fi

    set_secret_both_regions "$SECRET_ADMIN_USER" "$ADMIN_USER"
    set_secret_both_regions "$SECRET_DATABASE_NAME" "$DATABASE_NAME"
    set_secret_both_regions "$SECRET_PORT" "5432"
    set_secret_both_regions "$SECRET_SSL_MODE" "Require"

    section "Creating PostgreSQL 16 primary"

    if az postgres flexible-server show \
        --resource-group "$RG_PRIMARY_DATA" \
        --name "$PRIMARY_SERVER" \
        --output none \
        >/dev/null 2>&1; then

        echo "Existing primary server confirmed: $PRIMARY_SERVER"
    else
        az postgres flexible-server create \
            --resource-group "$RG_PRIMARY_DATA" \
            --name "$PRIMARY_SERVER" \
            --location "$PRIMARY_LOCATION" \
            --admin-user "$ADMIN_USER" \
            --admin-password "$ADMIN_PASSWORD" \
            --database-name "$DATABASE_NAME" \
            --version "$POSTGRES_VERSION" \
            --tier GeneralPurpose \
            --sku-name "$SKU_NAME" \
            --storage-type Premium_LRS \
            --storage-size "$STORAGE_GIB" \
            --storage-auto-grow Enabled \
            --backup-retention "$BACKUP_RETENTION_DAYS" \
            --geo-redundant-backup Enabled \
            --zonal-resiliency Enabled \
            --subnet "$PRIMARY_SUBNET_ID" \
            --private-dns-zone "$PRIVATE_DNS_ZONE_ID" \
            --password-auth Enabled \
            --tags \
                "application=$PRODUCT_NAME" \
                "environment=$ENVIRONMENT" \
                "resource-function=postgresql-primary" \
                "architecture=multi-region" \
                "region-role=primary" \
                "planned-replica=$PLANNED_REPLICA" \
            --yes \
            --only-show-errors \
            --output none

        echo "Created PostgreSQL primary: $PRIMARY_SERVER"
    fi

    unset ADMIN_PASSWORD

    section "Waiting for PostgreSQL primary readiness"

    az postgres flexible-server wait \
        --resource-group "$RG_PRIMARY_DATA" \
        --name "$PRIMARY_SERVER" \
        --custom "state=='Ready'" \
        --interval 30 \
        --timeout 3600

    PRIMARY_FQDN="$(
        az postgres flexible-server show \
            --resource-group "$RG_PRIMARY_DATA" \
            --name "$PRIMARY_SERVER" \
            --query fullyQualifiedDomainName \
            --output tsv
    )"

    set_secret_both_regions "$SECRET_PRIMARY_HOST" "$PRIMARY_FQDN"

    section "Allow-listing required PostgreSQL extensions"

    az postgres flexible-server parameter set \
        --resource-group "$RG_PRIMARY_DATA" \
        --server-name "$PRIMARY_SERVER" \
        --name azure.extensions \
        --value "PGCRYPTO" \
        --only-show-errors \
        --output none

    echo "Allowed extension: pgcrypto"

    section "Enabling enhanced database activity metrics"

    az postgres flexible-server parameter set \
        --resource-group "$RG_PRIMARY_DATA" \
        --server-name "$PRIMARY_SERVER" \
        --name metrics.collector_database_activity \
        --value "ON" \
        --only-show-errors \
        --output none || \
    echo "WARNING: Enhanced database activity metric parameter was unavailable."

    section "Saving non-secret PostgreSQL configuration"

    PRIMARY_SERVER_ID="$(
        az postgres flexible-server show \
            --resource-group "$RG_PRIMARY_DATA" \
            --name "$PRIMARY_SERVER" \
            --query id \
            --output tsv
    )"

    cat > "$CONFIG_FILE" <<EOF
PROJECT_NAME=$PRODUCT_NAME
ENVIRONMENT=$ENVIRONMENT

POSTGRES_PRIMARY_SERVER=$PRIMARY_SERVER
POSTGRES_PRIMARY_SERVER_ID=$PRIMARY_SERVER_ID
POSTGRES_PRIMARY_FQDN=$PRIMARY_FQDN
POSTGRES_PRIMARY_LOCATION=$PRIMARY_LOCATION
POSTGRES_DATABASE=$DATABASE_NAME
POSTGRES_PORT=5432
POSTGRES_VERSION=$POSTGRES_VERSION
POSTGRES_SKU=$SKU_NAME
POSTGRES_STORAGE_GIB=$STORAGE_GIB
POSTGRES_STORAGE_AUTOGROW=Enabled
POSTGRES_BACKUP_RETENTION_DAYS=$BACKUP_RETENTION_DAYS
POSTGRES_GEO_REDUNDANT_BACKUP=Enabled
POSTGRES_HIGH_AVAILABILITY=ZoneRedundant
POSTGRES_PRIVATE_DNS_ZONE=$POSTGRES_PRIVATE_DNS_ZONE
POSTGRES_PRIMARY_SUBNET_ID=$PRIMARY_SUBNET_ID

POSTGRES_PLANNED_REPLICA=$PLANNED_REPLICA
POSTGRES_PLANNED_REPLICA_LOCATION=$SECONDARY_LOCATION
POSTGRES_PLANNED_REPLICA_SUBNET_ID=$SECONDARY_SUBNET_ID

POSTGRES_ADMIN_USERNAME_SECRET=$SECRET_ADMIN_USER
POSTGRES_ADMIN_PASSWORD_SECRET=$SECRET_ADMIN_PASSWORD
POSTGRES_PRIMARY_HOST_SECRET=$SECRET_PRIMARY_HOST
POSTGRES_DATABASE_NAME_SECRET=$SECRET_DATABASE_NAME
EOF

    chmod 600 "$CONFIG_FILE"

    section "PostgreSQL primary validation"

    SERVER_JSON="$(mktemp)"
    trap 'rm -f "$SERVER_JSON"' EXIT

    az postgres flexible-server show \
        --resource-group "$RG_PRIMARY_DATA" \
        --name "$PRIMARY_SERVER" \
        --output json > "$SERVER_JSON"

    validate_server_json "$SERVER_JSON"

    az postgres flexible-server show \
        --resource-group "$RG_PRIMARY_DATA" \
        --name "$PRIMARY_SERVER" \
        --query '{
            name:name,
            fqdn:fullyQualifiedDomainName,
            location:location,
            state:state,
            version:version,
            tier:sku.tier,
            sku:sku.name,
            storageGiB:storage.storageSizeGb,
            storageAutogrow:storage.autoGrow,
            backupRetention:backup.backupRetentionDays,
            geoBackup:backup.geoRedundantBackup,
            haMode:highAvailability.mode,
            haState:highAvailability.state,
            primaryZone:availabilityZone,
            standbyZone:highAvailability.standbyAvailabilityZone,
            delegatedSubnet:network.delegatedSubnetResourceId,
            privateDnsZone:network.privateDnsZoneArmResourceId,
            publicAccess:network.publicNetworkAccess
        }' \
        --output table

    section "AZ-05B completed successfully"

    echo "PostgreSQL 16 primary: configured"
    echo "General Purpose compute: $SKU_NAME"
    echo "Starting storage: ${STORAGE_GIB} GiB"
    echo "Storage autogrow: enabled"
    echo "Zone-redundant HA: enabled"
    echo "Backup retention: ${BACKUP_RETENTION_DAYS} days"
    echo "Geo-redundant backup: enabled"
    echo "Private delegated subnet: configured"
    echo "Private DNS: configured"
    echo "pgcrypto allow-list: configured"
    echo "Administrator credentials: stored in both Key Vaults"
    echo
    echo "The East US read replica was intentionally not created yet."
    echo "Create it only after the source database import and validation."
    echo
    echo "No Container Apps environment was created."
    echo "No Application Gateway was created."
    echo "No Cloudflare DNS record was changed."
    echo
    echo "Configuration: $CONFIG_FILE"
    echo
    echo "************************************************************"
    echo "POSTGRESQL PRIMARY FOUNDATION READY"
    echo "************************************************************"

} 2>&1 | tee "$LOG"

echo
echo "Configuration file: $CONFIG_FILE"
echo "Deployment log:     $LOG"
