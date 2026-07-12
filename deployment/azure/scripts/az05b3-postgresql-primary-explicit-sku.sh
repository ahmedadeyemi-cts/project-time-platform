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
SKU_NAME="Standard_D2ds_v4"
STORAGE_GIB="128"
BACKUP_RETENTION_DAYS="35"

BASE_DIR="$HOME/project-health-dashboard-azure"
CONFIG_DIR="$BASE_DIR/config"
LOG_DIR="$BASE_DIR/logs"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
LOG="$LOG_DIR/az05b3-postgresql-primary-$STAMP.log"
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

require_ci() {
    local actual="$1"
    local expected="$2"
    local label="$3"

    if [ "${actual,,}" != "${expected,,}" ]; then
        echo "ERROR: $label is '$actual'; expected '$expected'."
        exit 1
    fi
}

validate_regional_capabilities() {
    local west_json east_json

    west_json="$(mktemp)"
    east_json="$(mktemp)"

    az postgres flexible-server list-skus \
        --location "$PRIMARY_LOCATION" \
        --output json > "$west_json"

    az postgres flexible-server list-skus \
        --location "$SECONDARY_LOCATION" \
        --output json > "$east_json"

    python3 - "$west_json" "$east_json" "$SKU_NAME" "$POSTGRES_VERSION" "$STORAGE_GIB" <<'PY'
import json
import sys
from pathlib import Path

west = json.loads(Path(sys.argv[1]).read_text())
east = json.loads(Path(sys.argv[2]).read_text())
sku_name = sys.argv[3]
version = int(sys.argv[4])
storage_gib = int(sys.argv[5])


def root(document):
    if not isinstance(document, list) or not document:
        raise SystemExit("Capability response was not a non-empty list.")
    return document[0]


def blocked(status):
    return str(status or "").lower() in {
        "disabled",
        "unavailable",
        "restricted",
        "notavailable",
    }

west_root = root(west)
east_root = root(east)

west_match = None
for edition in west_root.get("supportedServerEditions", []):
    if str(edition.get("name", "")).lower() != "generalpurpose":
        continue
    for sku in edition.get("supportedServerSkus", []):
        if str(sku.get("name", "")).lower() == sku_name.lower():
            west_match = sku
            break

if not west_match:
    raise SystemExit(f"{sku_name} was not found in West US 3 GeneralPurpose capabilities.")

if int(west_match.get("vCores") or 0) != 2:
    raise SystemExit(f"{sku_name} is not a two-vCore SKU in West US 3.")

if blocked(west_match.get("status")):
    raise SystemExit(f"{sku_name} is blocked in West US 3: {west_match.get('status')}")

ha_modes = {str(value).lower() for value in west_match.get("supportedHaMode", [])}
if "zoneredundant" not in ha_modes:
    raise SystemExit(f"{sku_name} does not advertise ZoneRedundant HA in West US 3.")

east_match = None
for entry in east_root.get("supportedFastProvisioningEditions", []):
    if str(entry.get("supportedSku", "")).lower() != sku_name.lower():
        continue
    if str(entry.get("supportedTier", "")).lower() != "generalpurpose":
        continue
    if int(entry.get("supportedServerVersions") or 0) != version:
        continue
    if int(entry.get("supportedStorageGb") or 0) < storage_gib:
        continue
    if blocked(entry.get("status")):
        continue
    east_match = entry
    break

if not east_match:
    raise SystemExit(
        f"{sku_name}, PostgreSQL {version}, and {storage_gib} GiB were not found "
        "in East US fast-provisioning capabilities."
    )

print("Regional PostgreSQL capability validation passed.")
print(f"West primary SKU: {sku_name}")
print(f"West HA modes: {', '.join(west_match.get('supportedHaMode', []))}")
print(f"East replica-compatible SKU: {east_match.get('supportedSku')}")
print(f"East replica-compatible storage: {east_match.get('supportedStorageGb')} GiB")
PY

    rm -f "$west_json" "$east_json"
}

{
    section "AZ-05B3 - Project Health Dashboard PostgreSQL Primary"

    echo "Product: $PRODUCT_NAME"
    echo "Environment: $ENVIRONMENT"
    echo "Primary region: $PRIMARY_LOCATION"
    echo "Secondary region: $SECONDARY_LOCATION"
    echo "Primary server: $PRIMARY_SERVER"
    echo "Planned replica: $PLANNED_REPLICA"
    echo "SKU: $SKU_NAME"
    echo "Storage: ${STORAGE_GIB} GiB"
    echo "Database: $DATABASE_NAME"
    echo "TIME=$(date -u -Is)"

    section "Validating network and Key Vault prerequisites"

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

    az keyvault show \
        --name "$WEST_KEYVAULT" \
        --query '{name:name,location:location,state:properties.provisioningState}' \
        --output table

    az keyvault show \
        --name "$EAST_KEYVAULT" \
        --query '{name:name,location:location,state:properties.provisioningState}' \
        --output table

    echo "Primary subnet: $PRIMARY_SUBNET_ID"
    echo "Replica subnet reserved: $SECONDARY_SUBNET_ID"
    echo "Private DNS zone: $PRIVATE_DNS_ZONE_ID"

    section "Validating explicit cross-region-compatible SKU"
    validate_regional_capabilities

    section "Retrieving existing administrator secret"

    ADMIN_PASSWORD="$(
        az keyvault secret show \
            --vault-name "$WEST_KEYVAULT" \
            --name "$SECRET_ADMIN_PASSWORD" \
            --query value \
            --output tsv
    )"

    if [ -z "$ADMIN_PASSWORD" ]; then
        echo "ERROR: PostgreSQL administrator password is missing."
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
        --output none \
        >/dev/null 2>&1; then

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

    section "Waiting for PostgreSQL primary readiness"

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
        --output none \
        >/dev/null 2>&1; then

        echo "Existing application database confirmed: $DATABASE_NAME"
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

    PRIMARY_FQDN="$(
        az postgres flexible-server show \
            --resource-group "$RG_PRIMARY_DATA" \
            --name "$PRIMARY_SERVER" \
            --query fullyQualifiedDomainName \
            --output tsv
    )"

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
    echo "WARNING: Enhanced activity metrics were unavailable."

    section "Validating PostgreSQL primary"

    STATE="$(az postgres flexible-server show -g "$RG_PRIMARY_DATA" -n "$PRIMARY_SERVER" --query state -o tsv)"
    VERSION="$(az postgres flexible-server show -g "$RG_PRIMARY_DATA" -n "$PRIMARY_SERVER" --query version -o tsv)"
    ACTUAL_SKU="$(az postgres flexible-server show -g "$RG_PRIMARY_DATA" -n "$PRIMARY_SERVER" --query sku.name -o tsv)"
    AUTO_GROW="$(az postgres flexible-server show -g "$RG_PRIMARY_DATA" -n "$PRIMARY_SERVER" --query storage.autoGrow -o tsv)"
    STORAGE_SIZE="$(az postgres flexible-server show -g "$RG_PRIMARY_DATA" -n "$PRIMARY_SERVER" --query storage.storageSizeGb -o tsv)"
    BACKUP_RETENTION="$(az postgres flexible-server show -g "$RG_PRIMARY_DATA" -n "$PRIMARY_SERVER" --query backup.backupRetentionDays -o tsv)"
    GEO_BACKUP="$(az postgres flexible-server show -g "$RG_PRIMARY_DATA" -n "$PRIMARY_SERVER" --query backup.geoRedundantBackup -o tsv)"
    HA_MODE="$(az postgres flexible-server show -g "$RG_PRIMARY_DATA" -n "$PRIMARY_SERVER" --query highAvailability.mode -o tsv)"

    require_ci "$STATE" "Ready" "server state"
    require_ci "$ACTUAL_SKU" "$SKU_NAME" "server SKU"
    require_ci "$AUTO_GROW" "Enabled" "storage autogrow"
    require_ci "$GEO_BACKUP" "Enabled" "geo-redundant backup"
    require_ci "$HA_MODE" "ZoneRedundant" "HA mode"

    if [[ "$VERSION" != 16* ]]; then
        echo "ERROR: PostgreSQL version is '$VERSION'; expected 16."
        exit 1
    fi

    if [ "${STORAGE_SIZE:-0}" -lt "$STORAGE_GIB" ]; then
        echo "ERROR: Storage is ${STORAGE_SIZE:-0} GiB; expected at least $STORAGE_GIB GiB."
        exit 1
    fi

    if [ "$BACKUP_RETENTION" != "$BACKUP_RETENTION_DAYS" ]; then
        echo "ERROR: Backup retention is $BACKUP_RETENTION days; expected $BACKUP_RETENTION_DAYS."
        exit 1
    fi

    az postgres flexible-server db show \
        --resource-group "$RG_PRIMARY_DATA" \
        --server-name "$PRIMARY_SERVER" \
        --name "$DATABASE_NAME" \
        --query '{name:name,charset:charset,collation:collation}' \
        --output table

    az postgres flexible-server show \
        --resource-group "$RG_PRIMARY_DATA" \
        --name "$PRIMARY_SERVER" \
        --query '{name:name,fqdn:fullyQualifiedDomainName,location:location,state:state,version:version,tier:sku.tier,sku:sku.name,storageGiB:storage.storageSizeGb,autogrow:storage.autoGrow,backupRetention:backup.backupRetentionDays,geoBackup:backup.geoRedundantBackup,haMode:highAvailability.mode,haState:highAvailability.state,primaryZone:availabilityZone,standbyZone:highAvailability.standbyAvailabilityZone,delegatedSubnet:network.delegatedSubnetResourceId,privateDnsZone:network.privateDnsZoneArmResourceId,publicAccess:network.publicNetworkAccess}' \
        --output table

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

    section "AZ-05B3 completed successfully"

    echo "PostgreSQL 16 primary: configured"
    echo "Application database: $DATABASE_NAME"
    echo "Cross-region-compatible SKU: $SKU_NAME"
    echo "Starting storage: ${STORAGE_GIB} GiB"
    echo "Storage autogrow: enabled"
    echo "Zone-redundant HA: enabled"
    echo "Backup retention: ${BACKUP_RETENTION_DAYS} days"
    echo "Geo-redundant backup: enabled"
    echo "Private networking and DNS: configured"
    echo "Administrator credentials: stored in both Key Vaults"
    echo
    echo "The East US replica remains deferred until database import and validation."
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
