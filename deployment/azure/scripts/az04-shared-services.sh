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
RG_EAST_DATA="rg-project-health-dashboard-test-data-eastus"

WEST_VNET="vnet-phd-test-westus3"
EAST_VNET="vnet-phd-test-eastus"
PRIVATE_ENDPOINT_SUBNET="snet-private-endpoints"
WEST_LOG_WORKSPACE="log-phd-test-westus3"
EAST_LOG_WORKSPACE="log-phd-test-eastus"
WEST_APP_INSIGHTS="appi-phd-test-westus3"
EAST_APP_INSIGHTS="appi-phd-test-eastus"
WEST_APP_IDENTITY="id-phd-test-app-westus3"
EAST_APP_IDENTITY="id-phd-test-app-eastus"
ACR_DNS_ZONE="privatelink.azurecr.io"
KV_DNS_ZONE="privatelink.vaultcore.azure.net"

BASE_DIR="$HOME/project-health-dashboard-azure"
CONFIG_DIR="$BASE_DIR/config"
LOG_DIR="$BASE_DIR/logs"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
LOG="$LOG_DIR/az04-shared-services-$STAMP.log"
CONFIG_FILE="$CONFIG_DIR/shared-services.env"
mkdir -p "$CONFIG_DIR" "$LOG_DIR"

SUBSCRIPTION_ID="$(az account show --query id -o tsv)"
TENANT_ID="$(az account show --query tenantId -o tsv)"
SIGNED_IN_USER="$(az account show --query user.name -o tsv)"
UNIQUE_SUFFIX="$(printf '%s' "$SUBSCRIPTION_ID" | sha256sum | cut -c1-6)"

ACR_NAME="acrphdtest${UNIQUE_SUFFIX}"
WEST_KEYVAULT="kv-phd-t-w3-${UNIQUE_SUFFIX}"
EAST_KEYVAULT="kv-phd-t-eus-${UNIQUE_SUFFIX}"
WEST_ACR_PE="pe-phd-test-acr-westus3"
EAST_ACR_PE="pe-phd-test-acr-eastus"
WEST_KV_PE="pe-phd-test-kv-westus3"
EAST_KV_PE="pe-phd-test-kv-eastus"

section(){ echo; echo "============================================================"; echo "$1"; echo "============================================================"; }

ensure_workspace(){
  local rg="$1" name="$2" location="$3"
  if az monitor log-analytics workspace show -g "$rg" -n "$name" --output none >/dev/null 2>&1; then
    az monitor log-analytics workspace update -g "$rg" -n "$name" --retention-time 30 --output none
  else
    az monitor log-analytics workspace create -g "$rg" -n "$name" -l "$location" \
      --sku PerGB2018 --retention-time 30 --tags \
      "application=$PRODUCT_NAME" "environment=$ENVIRONMENT" \
      "resource-function=monitoring" "architecture=multi-region" --output none
  fi
  echo "Confirmed Log Analytics workspace: $name"
}

ensure_appi(){
  local rg="$1" name="$2" location="$3" workspace="$4"
  if az monitor app-insights component show -g "$rg" -a "$name" --output none >/dev/null 2>&1; then
    az monitor app-insights component update -g "$rg" -a "$name" --workspace "$workspace" --output none
  else
    az monitor app-insights component create -g "$rg" -a "$name" -l "$location" \
      --workspace "$workspace" --application-type web --kind web --tags \
      "application=$PRODUCT_NAME" "environment=$ENVIRONMENT" \
      "resource-function=application-monitoring" "architecture=multi-region" --output none
  fi
  echo "Confirmed Application Insights: $name"
}

ensure_identity(){
  local rg="$1" name="$2" location="$3"
  az identity show -g "$rg" -n "$name" --output none >/dev/null 2>&1 || \
    az identity create -g "$rg" -n "$name" -l "$location" --tags \
      "application=$PRODUCT_NAME" "environment=$ENVIRONMENT" \
      "resource-function=application-identity" "architecture=multi-region" --output none
  echo "Confirmed managed identity: $name"
}

ensure_role(){
  local principal="$1" principal_type="$2" role="$3" scope="$4"
  count="$(az role assignment list --assignee "$principal" --scope "$scope" --role "$role" --query 'length(@)' -o tsv 2>/dev/null || echo 0)"
  [ "$count" != 0 ] && { echo "Existing role: $role"; return; }
  for attempt in $(seq 1 18); do
    if az role assignment create --assignee-object-id "$principal" --assignee-principal-type "$principal_type" \
      --role "$role" --scope "$scope" --output none 2>/tmp/phd-az04-role-error.txt; then
      echo "Created role: $role"; return
    fi
    [ "$attempt" = 18 ] && { cat /tmp/phd-az04-role-error.txt; return 1; }
    sleep 10
  done
}

ensure_keyvault(){
  local rg="$1" name="$2" location="$3"
  if ! az keyvault show -g "$rg" -n "$name" --output none >/dev/null 2>&1; then
    az keyvault create -g "$rg" -n "$name" -l "$location" --sku standard \
      --enable-rbac-authorization true --retention-days 90 --enable-purge-protection true \
      --public-network-access Enabled --tags \
      "application=$PRODUCT_NAME" "environment=$ENVIRONMENT" \
      "resource-function=secrets" "architecture=multi-region" \
      "network-lockdown=pending-migration" --output none
  fi
  echo "Confirmed Key Vault: $name"
}

ensure_pe(){
  local rg="$1" name="$2" location="$3" vnet="$4" resource_id="$5" group_id="$6"
  if ! az network private-endpoint show -g "$rg" -n "$name" --output none >/dev/null 2>&1; then
    az network private-endpoint create -g "$rg" -n "$name" -l "$location" \
      --vnet-name "$vnet" --subnet "$PRIVATE_ENDPOINT_SUBNET" \
      --private-connection-resource-id "$resource_id" --group-id "$group_id" \
      --connection-name "${name}-connection" --tags \
      "application=$PRODUCT_NAME" "environment=$ENVIRONMENT" "architecture=multi-region" --output none
  fi
  echo "Confirmed private endpoint: $name"
}

ensure_zone_group(){
  local rg="$1" endpoint="$2" zone_id="$3" zone_name="$4"
  if ! az network private-endpoint dns-zone-group show -g "$rg" --endpoint-name "$endpoint" -n default --output none >/dev/null 2>&1; then
    az network private-endpoint dns-zone-group create -g "$rg" --endpoint-name "$endpoint" -n default \
      --private-dns-zone "$zone_id" --zone-name "$zone_name" --output none
  fi
  echo "Confirmed DNS zone group: $endpoint/default"
}

{
  section "AZ-04 - Shared services"
  echo "TIME=$(date -u -Is)"
  echo "Subscription=$SUBSCRIPTION_ID"
  echo "Signed-in user=$SIGNED_IN_USER"

  az config set extension.use_dynamic_install=yes_without_prompt >/dev/null
  az extension show -n application-insights --output none >/dev/null 2>&1 || \
    az extension add -n application-insights --upgrade --output none

  section "Monitoring"
  ensure_workspace "$RG_WEST_APP" "$WEST_LOG_WORKSPACE" "$WEST_LOCATION"
  ensure_workspace "$RG_EAST_APP" "$EAST_LOG_WORKSPACE" "$EAST_LOCATION"
  west_ws="$(az monitor log-analytics workspace show -g "$RG_WEST_APP" -n "$WEST_LOG_WORKSPACE" --query id -o tsv)"
  east_ws="$(az monitor log-analytics workspace show -g "$RG_EAST_APP" -n "$EAST_LOG_WORKSPACE" --query id -o tsv)"
  ensure_appi "$RG_WEST_APP" "$WEST_APP_INSIGHTS" "$WEST_LOCATION" "$west_ws"
  ensure_appi "$RG_EAST_APP" "$EAST_APP_INSIGHTS" "$EAST_LOCATION" "$east_ws"

  section "Managed identities"
  ensure_identity "$RG_WEST_APP" "$WEST_APP_IDENTITY" "$WEST_LOCATION"
  ensure_identity "$RG_EAST_APP" "$EAST_APP_IDENTITY" "$EAST_LOCATION"
  west_identity_id="$(az identity show -g "$RG_WEST_APP" -n "$WEST_APP_IDENTITY" --query id -o tsv)"
  east_identity_id="$(az identity show -g "$RG_EAST_APP" -n "$EAST_APP_IDENTITY" --query id -o tsv)"
  west_principal="$(az identity show -g "$RG_WEST_APP" -n "$WEST_APP_IDENTITY" --query principalId -o tsv)"
  east_principal="$(az identity show -g "$RG_EAST_APP" -n "$EAST_APP_IDENTITY" --query principalId -o tsv)"

  section "Premium ACR"
  if ! az acr show -g "$RG_SHARED" -n "$ACR_NAME" --output none >/dev/null 2>&1; then
    [ "$(az acr check-name -n "$ACR_NAME" --query nameAvailable -o tsv)" = true ] || { echo "ERROR: unavailable ACR name $ACR_NAME"; exit 1; }
    args=(az acr create -g "$RG_SHARED" -n "$ACR_NAME" -l "$WEST_LOCATION" --sku Premium --admin-enabled false --tags \
      "application=$PRODUCT_NAME" "environment=$ENVIRONMENT" "resource-function=container-registry" \
      "architecture=multi-region" "network-lockdown=pending-image-build")
    help="$(az acr create --help 2>/dev/null || true)"
    grep -q -- '--public-network-enabled' <<<"$help" && args+=(--public-network-enabled true)
    grep -q -- '--allow-trusted-services' <<<"$help" && args+=(--allow-trusted-services true)
    grep -q -- '--zone-redundancy' <<<"$help" && args+=(--zone-redundancy Enabled)
    "${args[@]}" --output none
  fi
  acr_id="$(az acr show -g "$RG_SHARED" -n "$ACR_NAME" --query id -o tsv)"
  acr_login="$(az acr show -g "$RG_SHARED" -n "$ACR_NAME" --query loginServer -o tsv)"

  replica_count="$(az acr replication list -g "$RG_SHARED" -r "$ACR_NAME" --query "[?location=='$EAST_LOCATION'] | length(@)" -o tsv)"
  if [ "$replica_count" = 0 ]; then
    az acr replication create -g "$RG_SHARED" -r "$ACR_NAME" -l "$EAST_LOCATION" \
      --zone-redundancy Enabled --tags \
      "application=$PRODUCT_NAME" "environment=$ENVIRONMENT" \
      "region-role=secondary" "architecture=multi-region" --output none
  fi

  section "Key Vaults and RBAC"
  ensure_keyvault "$RG_WEST_DATA" "$WEST_KEYVAULT" "$WEST_LOCATION"
  ensure_keyvault "$RG_EAST_DATA" "$EAST_KEYVAULT" "$EAST_LOCATION"
  west_kv_id="$(az keyvault show -g "$RG_WEST_DATA" -n "$WEST_KEYVAULT" --query id -o tsv)"
  east_kv_id="$(az keyvault show -g "$RG_EAST_DATA" -n "$EAST_KEYVAULT" --query id -o tsv)"
  ensure_role "$west_principal" ServicePrincipal AcrPull "$acr_id"
  ensure_role "$east_principal" ServicePrincipal AcrPull "$acr_id"
  ensure_role "$west_principal" ServicePrincipal "Key Vault Secrets User" "$west_kv_id"
  ensure_role "$east_principal" ServicePrincipal "Key Vault Secrets User" "$east_kv_id"
  signed_in_object="$(az ad signed-in-user show --query id -o tsv)"
  ensure_role "$signed_in_object" User "Key Vault Administrator" "$west_kv_id"
  ensure_role "$signed_in_object" User "Key Vault Administrator" "$east_kv_id"

  section "Private endpoints"
  ensure_pe "$RG_WEST_NETWORK" "$WEST_ACR_PE" "$WEST_LOCATION" "$WEST_VNET" "$acr_id" registry
  ensure_pe "$RG_EAST_NETWORK" "$EAST_ACR_PE" "$EAST_LOCATION" "$EAST_VNET" "$acr_id" registry
  ensure_pe "$RG_WEST_NETWORK" "$WEST_KV_PE" "$WEST_LOCATION" "$WEST_VNET" "$west_kv_id" vault
  ensure_pe "$RG_EAST_NETWORK" "$EAST_KV_PE" "$EAST_LOCATION" "$EAST_VNET" "$east_kv_id" vault
  acr_zone_id="$(az network private-dns zone show -g "$RG_SHARED" -n "$ACR_DNS_ZONE" --query id -o tsv)"
  kv_zone_id="$(az network private-dns zone show -g "$RG_SHARED" -n "$KV_DNS_ZONE" --query id -o tsv)"
  ensure_zone_group "$RG_WEST_NETWORK" "$WEST_ACR_PE" "$acr_zone_id" acr
  ensure_zone_group "$RG_EAST_NETWORK" "$EAST_ACR_PE" "$acr_zone_id" acr
  ensure_zone_group "$RG_WEST_NETWORK" "$WEST_KV_PE" "$kv_zone_id" keyvault
  ensure_zone_group "$RG_EAST_NETWORK" "$EAST_KV_PE" "$kv_zone_id" keyvault

  cat > "$CONFIG_FILE" <<EOF
PROJECT_NAME=$PRODUCT_NAME
ENVIRONMENT=$ENVIRONMENT
AZURE_SUBSCRIPTION_ID=$SUBSCRIPTION_ID
AZURE_TENANT_ID=$TENANT_ID
ACR_NAME=$ACR_NAME
ACR_ID=$acr_id
ACR_LOGIN_SERVER=$acr_login
WEST_LOG_WORKSPACE=$WEST_LOG_WORKSPACE
WEST_LOG_WORKSPACE_ID=$west_ws
WEST_APP_INSIGHTS=$WEST_APP_INSIGHTS
EAST_LOG_WORKSPACE=$EAST_LOG_WORKSPACE
EAST_LOG_WORKSPACE_ID=$east_ws
EAST_APP_INSIGHTS=$EAST_APP_INSIGHTS
WEST_APP_IDENTITY=$WEST_APP_IDENTITY
WEST_APP_IDENTITY_ID=$west_identity_id
WEST_APP_IDENTITY_PRINCIPAL_ID=$west_principal
EAST_APP_IDENTITY=$EAST_APP_IDENTITY
EAST_APP_IDENTITY_ID=$east_identity_id
EAST_APP_IDENTITY_PRINCIPAL_ID=$east_principal
WEST_KEYVAULT=$WEST_KEYVAULT
WEST_KEYVAULT_ID=$west_kv_id
EAST_KEYVAULT=$EAST_KEYVAULT
EAST_KEYVAULT_ID=$east_kv_id
EOF
  chmod 600 "$CONFIG_FILE"

  section "Validation"
  az acr show -g "$RG_SHARED" -n "$ACR_NAME" --query '{name:name,loginServer:loginServer,location:location,sku:sku.name,adminEnabled:adminUserEnabled,publicAccess:publicNetworkAccess,zoneRedundancy:zoneRedundancy,state:provisioningState}' -o table
  az acr replication list -g "$RG_SHARED" -r "$ACR_NAME" --query '[].{name:name,location:location,state:provisioningState,zoneRedundancy:zoneRedundancy}' -o table
  az keyvault show -g "$RG_WEST_DATA" -n "$WEST_KEYVAULT" --query '{name:name,location:location,rbac:properties.enableRbacAuthorization,purgeProtection:properties.enablePurgeProtection,publicAccess:properties.publicNetworkAccess}' -o table
  az keyvault show -g "$RG_EAST_DATA" -n "$EAST_KEYVAULT" --query '{name:name,location:location,rbac:properties.enableRbacAuthorization,purgeProtection:properties.enablePurgeProtection,publicAccess:properties.publicNetworkAccess}' -o table

  section "AZ-04 complete"
  echo "SHARED SERVICES FOUNDATION READY"
} 2>&1 | tee "$LOG"

echo "Configuration: $CONFIG_FILE"
echo "Log: $LOG"
