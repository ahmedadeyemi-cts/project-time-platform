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
LOG="$LOG_DIR/az05b1-postgresql-primary-repair-$STAMP.log"
CONFIG_FILE="$CONFIG_DIR/postgresql-primary.env"
SKU_DIAGNOSTIC="$LOG_DIR/az05b1-common-skus-$STAMP.txt"

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

set_secret_both_regions() {
    local name="$1"
    local value="$2"

    az keyvault secret set \
        --vault-name "$WEST_KEYVAULT" \
        --name "$name" \
        --value "$value" \
        --only-show-errors \
        --output none

    az keyvault secret set \
        --vault-name "$EAST_KEYVAULT" \
        --name "$name" \
        --value "$value" \
        --only-show-errors \
        --output none
}

select_common_sku() {
    local west_file east_file
    west_file="$(mktemp)"
    east_file="$(mktemp)"

    az postgres flexible-server list-skus \
        --location "$PRIMARY_LOCATION" \
        --output json > "$west_file"

    az postgres flexible-server list-skus \
        --location "$SECONDARY_LOCATION" \
        --output json > "$east_file"

    python3 - "$west_file" "$east_file" "$SKU_DIAGNOSTIC" <<'PY'
import json
import re
import sys
from pathlib import Path

west = json.loads(Path(sys.argv[1]).read_text())
east = json.loads(Path(sys.argv[2]).read_text())
diagnostic = Path(sys.argv[3])

sku_pattern = re.compile(r"^Standard_[A-Za-z0-9_]+$")

def collect_strings(value):
    result = set()
    if isinstance(value, dict):
        for child in value.values():
            result.update(collect_strings(child))
    elif isinstance(value, list):
        for child in value:
            result.update(collect_strings(child))
    elif isinstance(value, str) and sku_pattern.match(value):
        result.add(value)
    return result

west_skus = collect_strings(west)
east_skus = collect_strings(east)
common = sorted(west_skus & east_skus)
common_d2 = [sku for sku in common if re.match(r"^Standard_D2", sku)]

preferred = [
    "Standard_D2ds_v6",
    "Standard_D2ads_v6",
    "Standard_D2s_v6",
    "Standard_D2pds_v6",
    "Standard_D2ps_v6",
    "Standard_D2ds_v5",
    "Standard_D2ads_v5",
    "Standard_D2s_v5",
    "Standard_D2d_v5",
    "Standard_D2_v5",
    "Standard_D2ds_v4",
    "Standard_D2s_v3",
]

lines = [
    "Common SKU strings discovered in West US 3 and East US:",
    *common,
    "",
    "Common D2 SKU strings:",
    *common_d2,
]
diagnostic.write_text("\n".join(lines) + "\n")

for sku in preferred:
    if sku in common_d2:
        print(sku)
        raise SystemExit(0)

if common_d2:
    print(common_d2[0])
    raise SystemExit(0)

print(
    "No common Standard_D2 General Purpose SKU string was found. "
    f"Review {diagnostic}",
    file=sys.stderr,
)
raise SystemExit(1)
PY

    local rc=$?
    rm -f "$west_file" "$east_file"
    return "$rc"
}

validate_server() {
    local json_file="$1"

    python3 - "$json_file" <<'PY'
import json
import sys
from pathlib import Path

server = json.loads(Path(sys.argv[1]).read_text())
storage = server.get("storage") or {}
backup = server.get("backup") or {}
ha = server.get("highAvailability") or {}
network = server.get("network") or {}
errors = []

if str(server.get("state", "")).lower() != "ready":
    errors.append(f"server state is {server.get('state')!r}, expected Ready")
if not str(server.get("version", "")).startswith("16"):
    errors.append(f"PostgreSQL version is {server.get('version')!r}, expected 16")
if str(storage.get("autoGrow", "")).lower() != "enabled":
    errors.append("storage autogrow is not enabled")
if int(storage.get("storageSizeGb") or 0) < 32:
    errors.append("storage is smaller than 32 GiB")
if int(backup.get("backupRetentionDays") or 0) != 35:
    errors.append("backup retention is not 35 days")
if str(backup.get("geoRedundantBackup", "")).lower() != "enabled":
    errors.append("geo-redundant backup is not enabled")
if str(ha.get("mode", "")).lower() != "zoneredundant":
    errors.append(f"HA mode is {ha.get('mode')!r}, expected ZoneRedundant")
if not network.get("delegatedSubnetResourceId"):
    errors.append("delegated subnet is missing")
if not network.get("privateDnsZoneArmResourceId"):
    errors.append("private DNS zone is missing")

if errors:
    for error in errors:
        print(f"ERROR: {error}", file=sys.stderr)
    raise SystemExit(1)

print("Primary PostgreSQL validation passed.")
PY
}

{
    section "AZ-05B.1 - PostgreSQL Primary Repair and Continuation"

    echo "Product: $PRODUCT_NAME"
    echo "Environment: $ENVIRONMENT"
    echo "Primary server: $PRIMARY_SERVER"
    echo "Database: $DATABASE_NAME"
    echo "TIME=$(date -u -Is)"

    section "Confirming no partial PostgreSQL server exists"

    if az postgres flexible-server show \
        --resource-group "$RG_PRIMARY_DATA" \
        --name "$PRIMARY_SERVER" \
        --output none >/dev/null 2>&1; then
        echo "Existing server found; this script will validate and continue it."
    else
        echo "No PostgreSQL server exists. Safe to continue creation."
    fi

    section "Validating network and Key Vault prerequisites"

    PRIMARY_SUBNET_ID="$(az network vnet subnet show \
        --resource-group "$RG_PRIMARY_NETWORK" \
        --vnet-name "$PRIMARY_VNET" \
        --name "$POSTGRES_SUBNET" \
        --query id --output tsv)"

    SECONDARY_SUBNET_ID="$(az network vnet subnet show \
        --resource-group "$RG_SECONDARY_NETWORK" \
        --vnet-name "$SECONDARY_VNET" \
        --name "$POSTGRES_SUBNET" \
        --query id --output tsv)"

    PRIVATE_DNS_ZONE_ID="$(az network private-dns zone show \
        --resource-group "$RG_SHARED" \
        --name "$POSTGRES_PRIVATE_DNS_ZONE" \
        --query id --output tsv)"

    az keyvault show \
        --resource-group "$WEST_KEYVAULT_RESOURCE_GROUP" \
        --name "$WEST_KEYVAULT" \
        --query '{name:name,location:location,state:properties.provisioningState}' \
        --output table

    az keyvault show \
        --resource-group "$EAST_KEYVAULT_RESOURCE_GROUP" \
        --name "$EAST_KEYVAULT" \
        --query '{name:name,location:location,state:properties.provisioningState}' \
        --output table

    echo "Primary subnet: $PRIMARY_SUBNET_ID"
    echo "Secondary replica subnet: $SECONDARY_SUBNET_ID"
    echo "Private DNS zone: $PRIVATE_DNS_ZONE_ID"

    section "Selecting a common two-vCore SKU"

    if ! SKU_NAME="$(select_common_sku)"; then
        echo "ERROR: Could not select a common two-vCore SKU."
        echo "Diagnostic: $SKU_DIAGNOSTIC"
        exit 1
    fi

    if [ -z "$SKU_NAME" ]; then
        echo "ERROR: SKU selection returned an empty value."
        echo "Diagnostic: $SKU_DIAGNOSTIC"
        exit 1
    fi

    echo "Selected SKU: $SKU_NAME"
    echo "SKU diagnostic: $SKU_DIAGNOSTIC"

    section "Loading existing PostgreSQL administrator secret"

    ADMIN_PASSWORD="$(az keyvault secret show \
        --vault-name "$WEST_KEYVAULT" \
        --name "$SECRET_ADMIN_PASSWORD" \
        --query value --output tsv)"

    if [ -z "$ADMIN_PASSWORD" ]; then
        echo "ERROR: PostgreSQL administrator password secret is empty."
        exit 1
    fi

    set_secret_both_regions "$SECRET_ADMIN_USER" "$ADMIN_USER"
    set_secret_both_regions "$SECRET_DATABASE_NAME" "$DATABASE_NAME"
    set_secret_both_regions "$SECRET_PORT" "5432"
    set_secret_both_regions "$SECRET_SSL_MODE" "Require"

    section "Creating PostgreSQL 16 primary"

    if az postgres flexible-server show \
        --resource-group "$RG_PRIMARY_DATA" \
        --name "$PRIMARY_SERVER" \
        --output none >/dev/null 2>&1; then
        echo "Existing primary confirmed: $PRIMARY_SERVER"
    else
        az postgres flexible-server create \
            --resource-group "$RG_PRIMARY_DATA" \
            --name "$PRIMARY_SERVER" \
            --location "$PRIMARY_LOCATION" \
            --admin-user "$ADMIN_USER" \
            --admin-password "$ADMIN_PASSWORD" \
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

    section "Waiting for server readiness"

    az postgres flexible-server wait \
        --resource-group "$RG_PRIMARY_DATA" \
        --name "$PRIMARY_SERVER" \
        --custom "state=='Ready'" \
        --interval 30 \
        --timeout 3600

    section "Creating application database separately"

    if az postgres flexible-server db show \
        --resource-group "$RG_PRIMARY_DATA" \
        --server-name "$PRIMARY_SERVER" \
        --name "$DATABASE_NAME" \
        --output none >/dev/null 2>&1; then
        echo "Existing database confirmed: $DATABASE_NAME"
    else
        az postgres flexible-server db create \
            --resource-group "$RG_PRIMARY_DATA" \
            --server-name "$PRIMARY_SERVER" \
            --name "$DATABASE_NAME" \
            --charset UTF8 \
            --only-show-errors \
            --output none

        echo "Created application database: $DATABASE_NAME"
    fi

    PRIMARY_FQDN="$(az postgres flexible-server show \
        --resource-group "$RG_PRIMARY_DATA" \
        --name "$PRIMARY_SERVER" \
        --query fullyQualifiedDomainName --output tsv)"

    set_secret_both_regions "$SECRET_PRIMARY_HOST" "$PRIMARY_FQDN"

    section "Configuring PostgreSQL parameters"

    az postgres flexible-server parameter set \
        --resource-group "$RG_PRIMARY_DATA" \
        --server-name "$PRIMARY_SERVER" \
        --name azure.extensions \
        --value PGCRYPTO \
        --only-show-errors \
        --output none

    az postgres flexible-server parameter set \
        --resource-group "$RG_PRIMARY_DATA" \
        --server-name "$PRIMARY_SERVER" \
        --name metrics.collector_database_activity \
        --value ON \
        --only-show-errors \
        --output none ||
    echo "WARNING: Enhanced database activity metrics were unavailable."

    section "Saving non-secret PostgreSQL configuration"

    PRIMARY_SERVER_ID="$(az postgres flexible-server show \
        --resource-group "$RG_PRIMARY_DATA" \
        --name "$PRIMARY_SERVER" \
        --query id --output tsv)"

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

    section "Validating PostgreSQL primary"

    SERVER_JSON="$(mktemp)"
    az postgres flexible-server show \
        --resource-group "$RG_PRIMARY_DATA" \
        --name "$PRIMARY_SERVER" \
        --output json > "$SERVER_JSON"

    validate_server "$SERVER_JSON"
    rm -f "$SERVER_JSON"

    az postgres flexible-server show \
        --resource-group "$RG_PRIMARY_DATA" \
        --name "$PRIMARY_SERVER" \
        --query '{name:name,fqdn:fullyQualifiedDomainName,location:location,state:state,version:version,tier:sku.tier,sku:sku.name,storageGiB:storage.storageSizeGb,storageAutogrow:storage.autoGrow,backupRetention:backup.backupRetentionDays,geoBackup:backup.geoRedundantBackup,haMode:highAvailability.mode,haState:highAvailability.state,primaryZone:availabilityZone,standbyZone:highAvailability.standbyAvailabilityZone,delegatedSubnet:network.delegatedSubnetResourceId,privateDnsZone:network.privateDnsZoneArmResourceId,publicAccess:network.publicNetworkAccess}' \
        --output table

    echo
    az postgres flexible-server db show \
        --resource-group "$RG_PRIMARY_DATA" \
        --server-name "$PRIMARY_SERVER" \
        --name "$DATABASE_NAME" \
        --query '{database:name,charset:charset,collation:collation}' \
        --output table

    section "AZ-05B.1 completed successfully"

    echo "PostgreSQL 16 primary: configured"
    echo "Application database: configured"
    echo "Compute SKU: $SKU_NAME"
    echo "Storage autogrow: enabled"
    echo "Zone-redundant HA: enabled"
    echo "Geo-redundant backup: enabled"
    echo "Private networking and DNS: configured"
    echo "Credentials and host metadata: stored in both Key Vaults"
    echo
    echo "The East US replica remains intentionally deferred until after import."
    echo "No Container Apps, Application Gateway, or Cloudflare changes were made."
    echo
    echo "Configuration: $CONFIG_FILE"
    echo "************************************************************"
    echo "POSTGRESQL PRIMARY FOUNDATION READY"
    echo "************************************************************"
} 2>&1 | tee "$LOG"

echo
echo "Configuration file: $CONFIG_FILE"
echo "SKU diagnostic:     $SKU_DIAGNOSTIC"
echo "Deployment log:     $LOG"
