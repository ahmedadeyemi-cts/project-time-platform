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

DATABASE_NAME="project_health_dashboard"
ADMIN_USER="phdpgadmin"
POSTGRES_VERSION="16"
STORAGE_GIB="32"
BACKUP_RETENTION_DAYS="35"

BASE_DIR="$HOME/project-health-dashboard-azure"
CONFIG_DIR="$BASE_DIR/config"
LOG_DIR="$BASE_DIR/logs"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
LOG="$LOG_DIR/az05b2-postgresql-primary-$STAMP.log"
CONFIG_FILE="$CONFIG_DIR/postgresql-primary.env"
SKU_DIAGNOSTIC="$LOG_DIR/az05b2-common-skus-$STAMP.txt"
mkdir -p "$CONFIG_DIR" "$LOG_DIR"

SUBSCRIPTION_ID="$(az account show --query id -o tsv)"
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

    az keyvault secret set --vault-name "$WEST_KEYVAULT" --name "$name" --value "$value" --only-show-errors -o none
    az keyvault secret set --vault-name "$EAST_KEYVAULT" --name "$name" --value "$value" --only-show-errors -o none
}

select_common_sku() {
    local west_json east_json rc
    west_json="$(mktemp)"
    east_json="$(mktemp)"
    rc=0

    az postgres flexible-server list-skus --location "$PRIMARY_LOCATION" -o json > "$west_json"
    az postgres flexible-server list-skus --location "$SECONDARY_LOCATION" -o json > "$east_json"

    python3 - "$west_json" "$east_json" "$SKU_DIAGNOSTIC" <<'PY' || rc=$?
import json
import re
import sys
from pathlib import Path

west = json.loads(Path(sys.argv[1]).read_text())
east = json.loads(Path(sys.argv[2]).read_text())
diag = Path(sys.argv[3])


def collect(value):
    found = set()
    if isinstance(value, dict):
        for child in value.values():
            found |= collect(child)
    elif isinstance(value, list):
        for child in value:
            found |= collect(child)
    elif isinstance(value, str):
        text = value.strip()
        if re.fullmatch(r"Standard_[A-Za-z0-9_]+", text):
            found.add(text)
        elif re.fullmatch(r"D[0-9]+[A-Za-z0-9_]+", text):
            found.add("Standard_" + text)
    return found

west_skus = collect(west)
east_skus = collect(east)
common = sorted(west_skus & east_skus)
common_d2 = [x for x in common if re.match(r"^Standard_D2", x)]

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

diag.write_text(
    "Common SKU strings:\n" + "\n".join(common) +
    "\n\nCommon two-vCore candidates:\n" + "\n".join(common_d2) + "\n"
)

for sku in preferred:
    if sku in common_d2:
        print(sku)
        raise SystemExit(0)

if common_d2:
    print(common_d2[0])
    raise SystemExit(0)

print(f"No common two-vCore SKU found. Review {diag}", file=sys.stderr)
raise SystemExit(1)
PY

    rm -f "$west_json" "$east_json"
    return "$rc"
}

require_ci() {
    local actual="$1"
    local expected="$2"
    local label="$3"
    if [ "${actual,,}" != "${expected,,}" ]; then
        echo "ERROR: $label is '$actual'; expected '$expected'."
        exit 1
    fi
}

{
    section "AZ-05B2 - Project Health Dashboard PostgreSQL Primary"
    echo "Product: $PRODUCT_NAME"
    echo "Environment: $ENVIRONMENT"
    echo "Primary server: $PRIMARY_SERVER"
    echo "Planned replica: $PLANNED_REPLICA"
    echo "TIME=$(date -u -Is)"

    section "Validating prerequisites"

    PRIMARY_SUBNET_ID="$(az network vnet subnet show -g "$RG_PRIMARY_NETWORK" --vnet-name "$PRIMARY_VNET" -n "$POSTGRES_SUBNET" --query id -o tsv)"
    SECONDARY_SUBNET_ID="$(az network vnet subnet show -g "$RG_SECONDARY_NETWORK" --vnet-name "$SECONDARY_VNET" -n "$POSTGRES_SUBNET" --query id -o tsv)"
    PRIVATE_DNS_ZONE_ID="$(az network private-dns zone show -g "$RG_SHARED" -n "$POSTGRES_PRIVATE_DNS_ZONE" --query id -o tsv)"

    az keyvault show -n "$WEST_KEYVAULT" --query '{name:name,location:location,state:properties.provisioningState}' -o table
    az keyvault show -n "$EAST_KEYVAULT" --query '{name:name,location:location,state:properties.provisioningState}' -o table

    if az postgres flexible-server show -g "$RG_PRIMARY_DATA" -n "$PRIMARY_SERVER" -o none >/dev/null 2>&1; then
        echo "Existing PostgreSQL server found; creation will be skipped."
        SKU_NAME="$(az postgres flexible-server show -g "$RG_PRIMARY_DATA" -n "$PRIMARY_SERVER" --query sku.name -o tsv)"
    else
        section "Selecting a common regional two-vCore SKU"
        if ! SKU_NAME="$(select_common_sku)"; then
            echo "ERROR: SKU discovery failed."
            echo "Diagnostic file: $SKU_DIAGNOSTIC"
            exit 1
        fi
        if [ -z "$SKU_NAME" ]; then
            echo "ERROR: SKU discovery returned an empty value."
            exit 1
        fi
        echo "Selected SKU: $SKU_NAME"
    fi

    section "Loading PostgreSQL administrator secret"

    ADMIN_PASSWORD="$(az keyvault secret show --vault-name "$WEST_KEYVAULT" --name "$SECRET_ADMIN_PASSWORD" --query value -o tsv)"
    if [ -z "$ADMIN_PASSWORD" ]; then
        echo "ERROR: PostgreSQL administrator password secret is empty or missing."
        exit 1
    fi

    set_secret_both_regions "$SECRET_ADMIN_USER" "$ADMIN_USER"
    set_secret_both_regions "$SECRET_DATABASE_NAME" "$DATABASE_NAME"
    set_secret_both_regions "$SECRET_PORT" "5432"
    set_secret_both_regions "$SECRET_SSL_MODE" "Require"

    section "Creating PostgreSQL 16 primary"

    if az postgres flexible-server show -g "$RG_PRIMARY_DATA" -n "$PRIMARY_SERVER" -o none >/dev/null 2>&1; then
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
            -o none
        echo "Created PostgreSQL primary: $PRIMARY_SERVER"
    fi

    unset ADMIN_PASSWORD

    section "Waiting for primary readiness"
    az postgres flexible-server wait -g "$RG_PRIMARY_DATA" -n "$PRIMARY_SERVER" --custom "state=='Ready'" --interval 30 --timeout 3600

    section "Creating application database"
    if az postgres flexible-server db show -g "$RG_PRIMARY_DATA" -s "$PRIMARY_SERVER" -d "$DATABASE_NAME" -o none >/dev/null 2>&1; then
        echo "Existing application database confirmed: $DATABASE_NAME"
    else
        az postgres flexible-server db create -g "$RG_PRIMARY_DATA" -s "$PRIMARY_SERVER" -d "$DATABASE_NAME" --charset UTF8 --only-show-errors -o none
        echo "Created application database: $DATABASE_NAME"
    fi

    PRIMARY_FQDN="$(az postgres flexible-server show -g "$RG_PRIMARY_DATA" -n "$PRIMARY_SERVER" --query fullyQualifiedDomainName -o tsv)"
    set_secret_both_regions "$SECRET_PRIMARY_HOST" "$PRIMARY_FQDN"

    section "Configuring required PostgreSQL extension allow-list"
    az postgres flexible-server parameter set -g "$RG_PRIMARY_DATA" --server-name "$PRIMARY_SERVER" -n azure.extensions --value PGCRYPTO --only-show-errors -o none

    az postgres flexible-server parameter set -g "$RG_PRIMARY_DATA" --server-name "$PRIMARY_SERVER" -n metrics.collector_database_activity --value ON --only-show-errors -o none || echo "WARNING: Enhanced activity metrics were unavailable."

    section "Validating PostgreSQL primary"

    STATE="$(az postgres flexible-server show -g "$RG_PRIMARY_DATA" -n "$PRIMARY_SERVER" --query state -o tsv)"
    VERSION="$(az postgres flexible-server show -g "$RG_PRIMARY_DATA" -n "$PRIMARY_SERVER" --query version -o tsv)"
    AUTO_GROW="$(az postgres flexible-server show -g "$RG_PRIMARY_DATA" -n "$PRIMARY_SERVER" --query storage.autoGrow -o tsv)"
    STORAGE_SIZE="$(az postgres flexible-server show -g "$RG_PRIMARY_DATA" -n "$PRIMARY_SERVER" --query storage.storageSizeGb -o tsv)"
    BACKUP_RETENTION="$(az postgres flexible-server show -g "$RG_PRIMARY_DATA" -n "$PRIMARY_SERVER" --query backup.backupRetentionDays -o tsv)"
    GEO_BACKUP="$(az postgres flexible-server show -g "$RG_PRIMARY_DATA" -n "$PRIMARY_SERVER" --query backup.geoRedundantBackup -o tsv)"
    HA_MODE="$(az postgres flexible-server show -g "$RG_PRIMARY_DATA" -n "$PRIMARY_SERVER" --query highAvailability.mode -o tsv)"

    require_ci "$STATE" "Ready" "server state"
    require_ci "$AUTO_GROW" "Enabled" "storage autogrow"
    require_ci "$GEO_BACKUP" "Enabled" "geo-redundant backup"
    require_ci "$HA_MODE" "ZoneRedundant" "HA mode"

    if [[ "$VERSION" != 16* ]]; then
        echo "ERROR: PostgreSQL version is '$VERSION'; expected version 16."
        exit 1
    fi
    if [ "${STORAGE_SIZE:-0}" -lt 32 ]; then
        echo "ERROR: Storage is ${STORAGE_SIZE:-0} GiB; expected at least 32 GiB."
        exit 1
    fi
    if [ "$BACKUP_RETENTION" != "35" ]; then
        echo "ERROR: Backup retention is '$BACKUP_RETENTION'; expected 35 days."
        exit 1
    fi

    az postgres flexible-server db show -g "$RG_PRIMARY_DATA" -s "$PRIMARY_SERVER" -d "$DATABASE_NAME" --query '{name:name,charset:charset,collation:collation}' -o table

    az postgres flexible-server show -g "$RG_PRIMARY_DATA" -n "$PRIMARY_SERVER" --query '{name:name,fqdn:fullyQualifiedDomainName,location:location,state:state,version:version,tier:sku.tier,sku:sku.name,storageGiB:storage.storageSizeGb,autogrow:storage.autoGrow,backupRetention:backup.backupRetentionDays,geoBackup:backup.geoRedundantBackup,haMode:highAvailability.mode,haState:highAvailability.state,primaryZone:availabilityZone,standbyZone:highAvailability.standbyAvailabilityZone,delegatedSubnet:network.delegatedSubnetResourceId,privateDnsZone:network.privateDnsZoneArmResourceId,publicAccess:network.publicNetworkAccess}' -o table

    PRIMARY_SERVER_ID="$(az postgres flexible-server show -g "$RG_PRIMARY_DATA" -n "$PRIMARY_SERVER" --query id -o tsv)"

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

    section "AZ-05B2 completed successfully"
    echo "PostgreSQL 16 primary: configured"
    echo "Application database: $DATABASE_NAME"
    echo "SKU: $SKU_NAME"
    echo "Storage autogrow: enabled"
    echo "Zone-redundant HA: enabled"
    echo "Backup retention: 35 days"
    echo "Geo-redundant backup: enabled"
    echo "Private networking and DNS: configured"
    echo "East US replica: intentionally deferred until after import"
    echo "Configuration: $CONFIG_FILE"
    echo
    echo "************************************************************"
    echo "POSTGRESQL PRIMARY FOUNDATION READY"
    echo "************************************************************"
} 2>&1 | tee "$LOG"

echo
echo "Configuration file: $CONFIG_FILE"
echo "Deployment log:     $LOG"
