#!/usr/bin/env bash
set -Eeuo pipefail

PRODUCT_NAME="Project Health Dashboard"
ENVIRONMENT="test"
WEST_LOCATION="westus3"
EAST_LOCATION="eastus"

RG_SHARED="rg-project-health-dashboard-shared-global"
RG_WEST_NETWORK="rg-project-health-dashboard-test-network-westus3"
RG_WEST_APP="rg-project-health-dashboard-test-app-westus3"
RG_WEST_DATA="rg-project-health-dashboard-test-data-westus3"
RG_EAST_NETWORK="rg-project-health-dashboard-test-network-eastus"
RG_EAST_APP="rg-project-health-dashboard-test-app-eastus"

WEST_VNET="vnet-phd-test-westus3"
EAST_VNET="vnet-phd-test-eastus"
PRIVATE_ENDPOINT_SUBNET="snet-private-endpoints"
WEST_APP_IDENTITY="id-phd-test-app-westus3"
EAST_APP_IDENTITY="id-phd-test-app-eastus"
BLOB_DNS_ZONE="privatelink.blob.core.windows.net"

BASE_DIR="$HOME/project-health-dashboard-azure"
CONFIG_DIR="$BASE_DIR/config"
LOG_DIR="$BASE_DIR/logs"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
LOG="$LOG_DIR/az05a-storage-foundation-$STAMP.log"
CONFIG_FILE="$CONFIG_DIR/storage-foundation.env"
LIFECYCLE_FILE="$CONFIG_DIR/storage-lifecycle-policy.json"
mkdir -p "$CONFIG_DIR" "$LOG_DIR"

SUBSCRIPTION_ID="$(az account show --query id -o tsv)"
UNIQUE_SUFFIX="$(printf '%s' "$SUBSCRIPTION_ID" | sha256sum | cut -c1-6)"
STORAGE_ACCOUNT="stphdtest${UNIQUE_SUFFIX}"
WEST_STORAGE_PE="pe-phd-test-blob-westus3"
EAST_STORAGE_PE="pe-phd-test-blob-eastus"

CONTAINERS=(
  project-documents
  work-register-intake
  migration-staging
  database-exports
  application-backups
)

section(){ echo; echo "============================================================"; echo "$1"; echo "============================================================"; }

ensure_role(){
  local principal="$1" principal_type="$2" role="$3" scope="$4"
  count="$(az role assignment list --assignee "$principal" --scope "$scope" --role "$role" --query 'length(@)' -o tsv 2>/dev/null || echo 0)"
  [ "$count" != 0 ] && { echo "Existing role: $role"; return; }
  for attempt in $(seq 1 18); do
    if az role assignment create --assignee-object-id "$principal" --assignee-principal-type "$principal_type" \
      --role "$role" --scope "$scope" --output none 2>/tmp/phd-az05a-role-error.txt; then
      echo "Created role: $role"; return
    fi
    [ "$attempt" = 18 ] && { cat /tmp/phd-az05a-role-error.txt; return 1; }
    sleep 10
  done
}

ensure_pe(){
  local rg="$1" name="$2" location="$3" vnet="$4" resource_id="$5"
  if ! az network private-endpoint show -g "$rg" -n "$name" --output none >/dev/null 2>&1; then
    az network private-endpoint create -g "$rg" -n "$name" -l "$location" \
      --vnet-name "$vnet" --subnet "$PRIVATE_ENDPOINT_SUBNET" \
      --private-connection-resource-id "$resource_id" --group-id blob \
      --connection-name "${name}-connection" --tags \
      "application=$PRODUCT_NAME" "environment=$ENVIRONMENT" \
      "resource-function=blob-private-endpoint" "architecture=multi-region" --output none
  fi
  echo "Confirmed private endpoint: $name"
}

ensure_zone_group(){
  local rg="$1" endpoint="$2" zone_id="$3"
  if ! az network private-endpoint dns-zone-group show -g "$rg" --endpoint-name "$endpoint" -n default --output none >/dev/null 2>&1; then
    az network private-endpoint dns-zone-group create -g "$rg" --endpoint-name "$endpoint" -n default \
      --private-dns-zone "$zone_id" --zone-name blob --output none
  fi
  echo "Confirmed DNS zone group: $endpoint/default"
}

{
  section "AZ-05A - Storage foundation"
  echo "TIME=$(date -u -Is)"
  echo "Storage account=$STORAGE_ACCOUNT"

  west_principal="$(az identity show -g "$RG_WEST_APP" -n "$WEST_APP_IDENTITY" --query principalId -o tsv)"
  east_principal="$(az identity show -g "$RG_EAST_APP" -n "$EAST_APP_IDENTITY" --query principalId -o tsv)"
  blob_zone_id="$(az network private-dns zone show -g "$RG_SHARED" -n "$BLOB_DNS_ZONE" --query id -o tsv)"

  az network vnet subnet show -g "$RG_WEST_NETWORK" --vnet-name "$WEST_VNET" -n "$PRIVATE_ENDPOINT_SUBNET" --output none
  az network vnet subnet show -g "$RG_EAST_NETWORK" --vnet-name "$EAST_VNET" -n "$PRIVATE_ENDPOINT_SUBNET" --output none

  section "RA-GZRS storage account"
  if ! az storage account show -g "$RG_WEST_DATA" -n "$STORAGE_ACCOUNT" --output none >/dev/null 2>&1; then
    [ "$(az storage account check-name -n "$STORAGE_ACCOUNT" --query nameAvailable -o tsv)" = true ] || { echo "ERROR: unavailable storage name $STORAGE_ACCOUNT"; exit 1; }
    args=(az storage account create -g "$RG_WEST_DATA" -n "$STORAGE_ACCOUNT" -l "$WEST_LOCATION" \
      --kind StorageV2 --sku Standard_RAGZRS --access-tier Hot --https-only true \
      --min-tls-version TLS1_2 --allow-blob-public-access false --allow-shared-key-access false \
      --default-action Allow --bypass AzureServices --tags \
      "application=$PRODUCT_NAME" "environment=$ENVIRONMENT" \
      "resource-function=document-storage" "architecture=multi-region" \
      "data-classification=project-data" "network-lockdown=pending-migration")
    help="$(az storage account create --help 2>/dev/null || true)"
    grep -q -- '--public-network-access' <<<"$help" && args+=(--public-network-access Enabled)
    "${args[@]}" --output none
  fi

  storage_id="$(az storage account show -g "$RG_WEST_DATA" -n "$STORAGE_ACCOUNT" --query id -o tsv)"
  blob_endpoint="$(az storage account show -g "$RG_WEST_DATA" -n "$STORAGE_ACCOUNT" --query primaryEndpoints.blob -o tsv)"
  secondary_blob_endpoint="$(az storage account show -g "$RG_WEST_DATA" -n "$STORAGE_ACCOUNT" --query secondaryEndpoints.blob -o tsv)"

  section "Storage RBAC"
  ensure_role "$west_principal" ServicePrincipal "Storage Blob Data Contributor" "$storage_id"
  ensure_role "$east_principal" ServicePrincipal "Storage Blob Data Contributor" "$storage_id"
  signed_in_object="$(az ad signed-in-user show --query id -o tsv)"
  ensure_role "$signed_in_object" User "Storage Blob Data Owner" "$storage_id"

  section "Blob protection"
  az storage account blob-service-properties update -g "$RG_WEST_DATA" \
    --account-name "$STORAGE_ACCOUNT" --enable-versioning true \
    --enable-delete-retention true --delete-retention-days 30 \
    --enable-container-delete-retention true --container-delete-retention-days 30 \
    --enable-change-feed true --change-feed-retention-days 90 \
    --enable-last-access-tracking true --output none

  section "Private containers"
  ready=0
  for attempt in $(seq 1 30); do
    if az storage container list --account-name "$STORAGE_ACCOUNT" --auth-mode login --query 'length(@)' -o tsv >/dev/null 2>&1; then
      ready=1; break
    fi
    echo "Waiting for Blob RBAC: attempt $attempt"; sleep 10
  done
  [ "$ready" = 1 ] || { echo "ERROR: Blob data-plane access unavailable"; exit 1; }
  for container in "${CONTAINERS[@]}"; do
    az storage container create --account-name "$STORAGE_ACCOUNT" -n "$container" --auth-mode login --output none
    echo "Confirmed container: $container"
  done

  section "Lifecycle policy"
  cat > "$LIFECYCLE_FILE" <<'JSON'
{
  "rules": [
    {
      "enabled": true,
      "name": "tier-project-documents",
      "type": "Lifecycle",
      "definition": {
        "actions": {
          "baseBlob": {"tierToCool": {"daysAfterModificationGreaterThan": 90}},
          "version": {"delete": {"daysAfterCreationGreaterThan": 365}}
        },
        "filters": {
          "blobTypes": ["blockBlob"],
          "prefixMatch": ["project-documents/", "work-register-intake/"]
        }
      }
    },
    {
      "enabled": true,
      "name": "tier-migration-and-backup-data",
      "type": "Lifecycle",
      "definition": {
        "actions": {
          "baseBlob": {"tierToCool": {"daysAfterModificationGreaterThan": 30}},
          "version": {"delete": {"daysAfterCreationGreaterThan": 180}}
        },
        "filters": {
          "blobTypes": ["blockBlob"],
          "prefixMatch": ["migration-staging/", "database-exports/", "application-backups/"]
        }
      }
    }
  ]
}
JSON
  az storage account management-policy show -g "$RG_WEST_DATA" --account-name "$STORAGE_ACCOUNT" --output none >/dev/null 2>&1 && \
    az storage account management-policy delete -g "$RG_WEST_DATA" --account-name "$STORAGE_ACCOUNT" --output none || true
  az storage account management-policy create -g "$RG_WEST_DATA" --account-name "$STORAGE_ACCOUNT" \
    --policy "@$LIFECYCLE_FILE" --output none

  section "Private endpoints"
  ensure_pe "$RG_WEST_NETWORK" "$WEST_STORAGE_PE" "$WEST_LOCATION" "$WEST_VNET" "$storage_id"
  ensure_pe "$RG_EAST_NETWORK" "$EAST_STORAGE_PE" "$EAST_LOCATION" "$EAST_VNET" "$storage_id"
  ensure_zone_group "$RG_WEST_NETWORK" "$WEST_STORAGE_PE" "$blob_zone_id"
  ensure_zone_group "$RG_EAST_NETWORK" "$EAST_STORAGE_PE" "$blob_zone_id"

  cat > "$CONFIG_FILE" <<EOF
PROJECT_NAME=$PRODUCT_NAME
ENVIRONMENT=$ENVIRONMENT
STORAGE_ACCOUNT_NAME=$STORAGE_ACCOUNT
STORAGE_ACCOUNT_ID=$storage_id
STORAGE_PRIMARY_BLOB_ENDPOINT=$blob_endpoint
STORAGE_SECONDARY_BLOB_ENDPOINT=$secondary_blob_endpoint
STORAGE_DOCUMENT_CONTAINER=project-documents
STORAGE_INTAKE_CONTAINER=work-register-intake
STORAGE_MIGRATION_CONTAINER=migration-staging
STORAGE_DATABASE_EXPORT_CONTAINER=database-exports
STORAGE_APPLICATION_BACKUP_CONTAINER=application-backups
WEST_STORAGE_PRIVATE_ENDPOINT=$WEST_STORAGE_PE
EAST_STORAGE_PRIVATE_ENDPOINT=$EAST_STORAGE_PE
EOF
  chmod 600 "$CONFIG_FILE"

  section "Validation"
  az storage account show -g "$RG_WEST_DATA" -n "$STORAGE_ACCOUNT" --query \
    '{name:name,kind:kind,sku:sku.name,primaryLocation:primaryLocation,secondaryLocation:secondaryLocation,primaryStatus:statusOfPrimary,secondaryStatus:statusOfSecondary,blobPublicAccess:allowBlobPublicAccess,sharedKeyAccess:allowSharedKeyAccess,publicNetworkAccess:publicNetworkAccess,minimumTls:minimumTlsVersion,state:provisioningState}' -o table
  az storage account blob-service-properties show -g "$RG_WEST_DATA" --account-name "$STORAGE_ACCOUNT" --query \
    '{versioning:isVersioningEnabled,blobSoftDelete:deleteRetentionPolicy.enabled,blobRetentionDays:deleteRetentionPolicy.days,containerSoftDelete:containerDeleteRetentionPolicy.enabled,containerRetentionDays:containerDeleteRetentionPolicy.days,changeFeed:changeFeed.enabled,lastAccessTracking:lastAccessTimeTrackingPolicy.enable}' -o table
  az storage container list --account-name "$STORAGE_ACCOUNT" --auth-mode login --query '[].{name:name,lastModified:properties.lastModified}' -o table

  section "AZ-05A complete"
  echo "STORAGE FOUNDATION READY"
} 2>&1 | tee "$LOG"

echo "Configuration: $CONFIG_FILE"
echo "Lifecycle policy: $LIFECYCLE_FILE"
echo "Log: $LOG"
