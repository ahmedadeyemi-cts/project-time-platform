#!/usr/bin/env bash
set -Eeuo pipefail

PRODUCT_NAME="Project Health Dashboard"
ENVIRONMENT="test"

PRIMARY_LOCATION="westus3"
SECONDARY_LOCATION="eastus"

RG_PRIMARY_DATA="rg-project-health-dashboard-test-data-westus3"
RG_PRIMARY_NETWORK="rg-project-health-dashboard-test-network-westus3"
RG_SECONDARY_NETWORK="rg-project-health-dashboard-test-network-eastus"
RG_SHARED="rg-project-health-dashboard-shared-global"

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
LOG="$LOG_DIR/az05b5-postgresql-primary-continuation-$STAMP.log"
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

require_value() {
    local actual="$1"
    local expected="$2"
    local label="$3"

    if [ "${actual,,}" != "${expected,,}" ]; then
        echo "ERROR: $label is '$actual'; expected '$expected'."
        exit 1
    fi
}

{
    section "AZ-05B5 - PostgreSQL Primary Continuation"

    echo "Product: $PRODUCT_NAME"
    echo "Environment: $ENVIRONMENT"
    echo "Primary server: $PRIMARY_SERVER"
    echo "Database: $DATABASE_NAME"
    echo "TIME=$(date -u -Is)"

    section "Confirming the existing PostgreSQL primary"

    if ! az postgres flexible-server show \
        --resource-group "$RG_PRIMARY_DATA" \
        --name "$PRIMARY_SERVER" \
        --output none \
        >/dev/null 2>&1; then

        echo "ERROR: Expected PostgreSQL server does not exist:"
        echo "$PRIMARY_SERVER"
        exit 1
    fi

    az postgres flexible-server wait \
        --resource-group "$RG_PRIMARY_DATA" \
        --name "$PRIMARY_SERVER" \
        --custom "state=='Ready'" \
        --interval 30 \
        --timeout 3600

    az postgres flexible-server show \
        --resource-group "$RG_PRIMARY_DATA" \
        --name "$PRIMARY_SERVER" \
        --query '{name:name,location:location,state:state,version:version,sku:sku.name,storageGiB:storage.storageSizeGb,haMode:highAvailability.mode,haState:highAvailability.state}' \
        --output table

    section "Creating the application database with server defaults"

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

    section "Saving connection metadata in both Key Vaults"

    set_secret_both_regions "$SECRET_ADMIN_USER" "$ADMIN_USER"
    set_secret_both_regions "$SECRET_PRIMARY_HOST" "$PRIMARY_FQDN"
    set_secret_both_regions "$SECRET_DATABASE_NAME" "$DATABASE_NAME"
    set_secret_both_regions "$SECRET_PORT" "5432"
    set_secret_both_regions "$SECRET_SSL_MODE" "Require"

    section "Configuring PostgreSQL parameters"

    az postgres flexible-server parameter set \
        --resource-group "$RG_PRIMARY_DATA" \
        --server-name "$PRIMARY_SERVER" \
        --name azure.extensions \
        --value PGCRYPTO \
        --only-show-errors \
        --output none

    echo "Allowed extension: pgcrypto"

    az postgres flexible-server parameter set \
        --resource-group "$RG_PRIMARY_DATA" \
        --server-name "$PRIMARY_SERVER" \
        --name metrics.collector_database_activity \
        --value ON \
        --only-show-errors \
        --output none ||
    echo "WARNING: Enhanced database activity metrics were unavailable."

    section "Validating PostgreSQL primary and database"

    STATE="$(az postgres flexible-server show --resource-group "$RG_PRIMARY_DATA" --name "$PRIMARY_SERVER" --query state --output tsv)"
    VERSION="$(az postgres flexible-server show --resource-group "$RG_PRIMARY_DATA" --name "$PRIMARY_SERVER" --query version --output tsv)"
    ACTUAL_SKU="$(az postgres flexible-server show --resource-group "$RG_PRIMARY_DATA" --name "$PRIMARY_SERVER" --query sku.name --output tsv)"
    STORAGE_SIZE="$(az postgres flexible-server show --resource-group "$RG_PRIMARY_DATA" --name "$PRIMARY_SERVER" --query storage.storageSizeGb --output tsv)"
    AUTO_GROW="$(az postgres flexible-server show --resource-group "$RG_PRIMARY_DATA" --name "$PRIMARY_SERVER" --query storage.autoGrow --output tsv)"
    BACKUP_RETENTION="$(az postgres flexible-server show --resource-group "$RG_PRIMARY_DATA" --name "$PRIMARY_SERVER" --query backup.backupRetentionDays --output tsv)"
    GEO_BACKUP="$(az postgres flexible-server show --resource-group "$RG_PRIMARY_DATA" --name "$PRIMARY_SERVER" --query backup.geoRedundantBackup --output tsv)"
    HA_MODE="$(az postgres flexible-server show --resource-group "$RG_PRIMARY_DATA" --name "$PRIMARY_SERVER" --query highAvailability.mode --output tsv)"
    HA_STATE="$(az postgres flexible-server show --resource-group "$RG_PRIMARY_DATA" --name "$PRIMARY_SERVER" --query highAvailability.state --output tsv)"
    PRIMARY_ZONE="$(az postgres flexible-server show --resource-group "$RG_PRIMARY_DATA" --name "$PRIMARY_SERVER" --query availabilityZone --output tsv)"
    STANDBY_ZONE="$(az postgres flexible-server show --resource-group "$RG_PRIMARY_DATA" --name "$PRIMARY_SERVER" --query highAvailability.standbyAvailabilityZone --output tsv)"

    require_value "$STATE" "Ready" "server state"
    require_value "$ACTUAL_SKU" "$SKU_NAME" "server SKU"
    require_value "$AUTO_GROW" "Enabled" "storage autogrow"
    require_value "$GEO_BACKUP" "Enabled" "geo-redundant backup"

    if [[ "$VERSION" != 16* ]]; then
        echo "ERROR: PostgreSQL version is '$VERSION'; expected version 16."
        exit 1
    fi

    if [ "${STORAGE_SIZE:-0}" -lt "$STORAGE_GIB" ]; then
        echo "ERROR: Storage is ${STORAGE_SIZE:-0} GiB; expected at least ${STORAGE_GIB} GiB."
        exit 1
    fi

    if [ "$BACKUP_RETENTION" != "$BACKUP_RETENTION_DAYS" ]; then
        echo "ERROR: Backup retention is '$BACKUP_RETENTION'; expected ${BACKUP_RETENTION_DAYS}."
        exit 1
    fi

    case "${HA_MODE,,}" in
        zoneredundant)
            echo "Zone-redundant HA is active."
            ;;
        samezone)
            echo "WARNING: Same-zone HA is active because cross-zone capacity was unavailable."
            ;;
        *)
            echo "ERROR: Unexpected HA mode: $HA_MODE"
            exit 1
            ;;
    esac

    DB_CHARSET="$(
        az postgres flexible-server db show \
            --resource-group "$RG_PRIMARY_DATA" \
            --server-name "$PRIMARY_SERVER" \
            --name "$DATABASE_NAME" \
            --query charset \
            --output tsv
    )"

    if [ "${DB_CHARSET^^}" != "UTF8" ]; then
        echo "ERROR: Database charset is '$DB_CHARSET'; expected UTF8."
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

    PRIMARY_SERVER_ID="$(az postgres flexible-server show --resource-group "$RG_PRIMARY_DATA" --name "$PRIMARY_SERVER" --query id --output tsv)"
    PRIMARY_SUBNET_ID="$(az network vnet subnet show --resource-group "$RG_PRIMARY_NETWORK" --vnet-name "$PRIMARY_VNET" --name "$POSTGRES_SUBNET" --query id --output tsv)"
    SECONDARY_SUBNET_ID="$(az network vnet subnet show --resource-group "$RG_SECONDARY_NETWORK" --vnet-name "$SECONDARY_VNET" --name "$POSTGRES_SUBNET" --query id --output tsv)"

    section "Saving non-secret PostgreSQL configuration"

    cat > "$CONFIG_FILE" <<EOF
PROJECT_NAME=$PRODUCT_NAME
ENVIRONMENT=$ENVIRONMENT
POSTGRES_PRIMARY_SERVER=$PRIMARY_SERVER
POSTGRES_PRIMARY_SERVER_ID=$PRIMARY_SERVER_ID
POSTGRES_PRIMARY_FQDN=$PRIMARY_FQDN
POSTGRES_PRIMARY_LOCATION=$PRIMARY_LOCATION
POSTGRES_DATABASE=$DATABASE_NAME
POSTGRES_DATABASE_CHARSET=$DB_CHARSET
POSTGRES_PORT=5432
POSTGRES_VERSION=$POSTGRES_VERSION
POSTGRES_SKU=$SKU_NAME
POSTGRES_STORAGE_GIB=$STORAGE_GIB
POSTGRES_STORAGE_AUTOGROW=Enabled
POSTGRES_BACKUP_RETENTION_DAYS=$BACKUP_RETENTION_DAYS
POSTGRES_GEO_REDUNDANT_BACKUP=Enabled
POSTGRES_HA_DESIRED_MODE=ZoneRedundant
POSTGRES_HA_ACTUAL_MODE=$HA_MODE
POSTGRES_HA_STATE=$HA_STATE
POSTGRES_HA_SAME_ZONE_FALLBACK=Allowed
POSTGRES_PRIMARY_ZONE=$PRIMARY_ZONE
POSTGRES_STANDBY_ZONE=$STANDBY_ZONE
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

    section "AZ-05B5 completed successfully"

    echo "PostgreSQL 16 primary: confirmed"
    echo "Application database: $DATABASE_NAME"
    echo "Database charset: $DB_CHARSET"
    echo "SKU: $SKU_NAME"
    echo "Storage: ${STORAGE_GIB} GiB"
    echo "Storage autogrow: enabled"
    echo "Backup retention: ${BACKUP_RETENTION_DAYS} days"
    echo "Geo-redundant backup: enabled"
    echo "Actual HA mode: $HA_MODE"
    echo "HA state: $HA_STATE"
    echo "Primary zone: ${PRIMARY_ZONE:-not-reported}"
    echo "Standby zone: ${STANDBY_ZONE:-not-reported}"
    echo "Configuration: $CONFIG_FILE"
    echo
    echo "The East US replica remains deferred until database import."
    echo
    echo "************************************************************"
    echo "POSTGRESQL PRIMARY FOUNDATION READY"
    echo "************************************************************"

} 2>&1 | tee "$LOG"

echo
echo "Configuration file: $CONFIG_FILE"
echo "Deployment log:     $LOG"
