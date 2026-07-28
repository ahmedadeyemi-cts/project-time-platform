#!/usr/bin/env bash
set -Eeuo pipefail

PRODUCT_NAME="Project Health Dashboard"
ENVIRONMENT="test"
WEST_LOCATION="westus3"
EAST_LOCATION="eastus"

RG_SHARED="rg-project-health-dashboard-shared-global"
RG_EDGE="rg-project-health-dashboard-test-edge-global"
RG_WEST_NETWORK="rg-project-health-dashboard-test-network-westus3"
RG_WEST_APP="rg-project-health-dashboard-test-app-westus3"
RG_WEST_DATA="rg-project-health-dashboard-test-data-westus3"
RG_EAST_NETWORK="rg-project-health-dashboard-test-network-eastus"
RG_EAST_APP="rg-project-health-dashboard-test-app-eastus"
RG_EAST_DATA="rg-project-health-dashboard-test-data-eastus"

WEST_VNET="vnet-phd-test-westus3"
EAST_VNET="vnet-phd-test-eastus"
WEST_SPACE="10.30.0.0/16"
EAST_SPACE="10.40.0.0/16"

BASE_DIR="$HOME/project-health-dashboard-azure"
CONFIG_DIR="$BASE_DIR/config"
LOG_DIR="$BASE_DIR/logs"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
LOG="$LOG_DIR/az03-network-foundation-$STAMP.log"
CONFIG_FILE="$CONFIG_DIR/network-foundation.env"
mkdir -p "$CONFIG_DIR" "$LOG_DIR"

PROVIDERS=(
  Microsoft.App Microsoft.OperationalInsights Microsoft.Insights
  Microsoft.ContainerRegistry Microsoft.DBforPostgreSQL Microsoft.Network
  Microsoft.Storage Microsoft.KeyVault Microsoft.Cdn Microsoft.ManagedIdentity
  Microsoft.EventGrid Microsoft.AlertsManagement
)

DNS_ZONES=(
  phd-test.postgres.database.azure.com
  privatelink.azurecr.io
  privatelink.blob.core.windows.net
  privatelink.file.core.windows.net
  privatelink.vaultcore.azure.net
  privatelink.westus3.azurecontainerapps.io
  privatelink.eastus.azurecontainerapps.io
)

section(){ echo; echo "============================================================"; echo "$1"; echo "============================================================"; }

ensure_rg(){
  az group create --name "$1" --location "$2" --tags \
    "application=$PRODUCT_NAME" "environment=$ENVIRONMENT" \
    "resource-function=$3" "architecture=multi-region" "managed-by=azure-cli" \
    --output none
  echo "Confirmed resource group: $1"
}

ensure_nsg(){
  if ! az network nsg show -g "$1" -n "$2" --output none >/dev/null 2>&1; then
    az network nsg create -g "$1" -n "$2" -l "$3" --tags \
      "application=$PRODUCT_NAME" "environment=$ENVIRONMENT" "resource-function=$4" --output none
  fi
  echo "Confirmed NSG: $2"
}

ensure_rt(){
  if ! az network route-table show -g "$1" -n "$2" --output none >/dev/null 2>&1; then
    az network route-table create -g "$1" -n "$2" -l "$3" \
      --disable-bgp-route-propagation false --tags \
      "application=$PRODUCT_NAME" "environment=$ENVIRONMENT" "resource-function=$4" --output none
  fi
  echo "Confirmed empty route table: $2"
}

ensure_subnet(){
  local rg="$1" vnet="$2" name="$3" prefix="$4" nsg="${5:-}" rt="${6:-}" delegation="${7:-}" pe="${8:-false}"
  if az network vnet subnet show -g "$rg" --vnet-name "$vnet" -n "$name" --output none >/dev/null 2>&1; then
    echo "Existing subnet: $vnet/$name"
    return
  fi
  args=(az network vnet subnet create -g "$rg" --vnet-name "$vnet" -n "$name" --address-prefixes "$prefix")
  [ -n "$nsg" ] && args+=(--network-security-group "$nsg")
  [ -n "$rt" ] && args+=(--route-table "$rt")
  [ -n "$delegation" ] && args+=(--delegations "$delegation")
  "${args[@]}" --output none
  if [ "$pe" = true ]; then
    az network vnet subnet update -g "$rg" --vnet-name "$vnet" -n "$name" \
      --private-endpoint-network-policies Disabled --output none
  fi
  echo "Created subnet: $vnet/$name [$prefix]"
}

ensure_peering(){
  local rg="$1" vnet="$2" name="$3" remote="$4"
  if az network vnet peering show -g "$rg" --vnet-name "$vnet" -n "$name" --output none >/dev/null 2>&1; then
    az network vnet peering update -g "$rg" --vnet-name "$vnet" -n "$name" --set \
      allowVirtualNetworkAccess=true allowForwardedTraffic=true allowGatewayTransit=false useRemoteGateways=false --output none
  else
    az network vnet peering create -g "$rg" --vnet-name "$vnet" -n "$name" \
      --remote-vnet "$remote" --allow-vnet-access --allow-forwarded-traffic --output none
  fi
  echo "Confirmed peering: $name"
}

ensure_dns_link(){
  local zone="$1" name="$2" vnet_id="$3"
  if ! az network private-dns link vnet show -g "$RG_SHARED" -z "$zone" -n "$name" --output none >/dev/null 2>&1; then
    az network private-dns link vnet create -g "$RG_SHARED" -z "$zone" -n "$name" \
      --virtual-network "$vnet_id" --registration-enabled false --output none
  fi
  echo "Confirmed DNS link: $zone/$name"
}

{
  section "AZ-03 - Two-region network foundation"
  echo "TIME=$(date -u -Is)"
  echo "Product=$PRODUCT_NAME"

  old="$(az group list --query "[?starts_with(name, 'rg-projectpulse')].name" -o tsv)"
  if [ -n "$old" ]; then
    echo "ERROR: obsolete ProjectPulse resource groups exist:"; echo "$old"; exit 1
  fi

  section "Providers"
  for p in "${PROVIDERS[@]}"; do
    state="$(az provider show -n "$p" --query registrationState -o tsv 2>/dev/null || true)"
    [ "$state" = Registered ] || az provider register -n "$p" --output none
  done
  for p in "${PROVIDERS[@]}"; do
    for _ in $(seq 1 180); do
      state="$(az provider show -n "$p" --query registrationState -o tsv 2>/dev/null || true)"
      [ "$state" = Registered ] && break
      sleep 5
    done
    [ "$state" = Registered ] || { echo "ERROR: provider not registered: $p"; exit 1; }
    echo "$p Registered"
  done

  section "Regions"
  for loc in "$WEST_LOCATION" "$EAST_LOCATION"; do
    [ "$(az account list-locations --query "[?name=='$loc'] | length(@)" -o tsv)" = 1 ] || { echo "ERROR: unavailable region $loc"; exit 1; }
    echo "Available: $loc"
  done

  section "Resource groups"
  ensure_rg "$RG_SHARED" "$WEST_LOCATION" shared-services
  ensure_rg "$RG_EDGE" "$WEST_LOCATION" global-edge
  ensure_rg "$RG_WEST_NETWORK" "$WEST_LOCATION" networking
  ensure_rg "$RG_WEST_APP" "$WEST_LOCATION" application
  ensure_rg "$RG_WEST_DATA" "$WEST_LOCATION" data
  ensure_rg "$RG_EAST_NETWORK" "$EAST_LOCATION" networking
  ensure_rg "$RG_EAST_APP" "$EAST_LOCATION" application
  ensure_rg "$RG_EAST_DATA" "$EAST_LOCATION" data

  section "VNets"
  az network vnet create -g "$RG_WEST_NETWORK" -n "$WEST_VNET" -l "$WEST_LOCATION" --address-prefixes "$WEST_SPACE" --output none
  az network vnet create -g "$RG_EAST_NETWORK" -n "$EAST_VNET" -l "$EAST_LOCATION" --address-prefixes "$EAST_SPACE" --output none

  section "NSGs and route tables"
  ensure_nsg "$RG_WEST_NETWORK" nsg-phd-test-aca-westus3 "$WEST_LOCATION" container-apps
  ensure_nsg "$RG_WEST_NETWORK" nsg-phd-test-postgresql-westus3 "$WEST_LOCATION" postgresql
  ensure_nsg "$RG_WEST_NETWORK" nsg-phd-test-management-westus3 "$WEST_LOCATION" management
  ensure_nsg "$RG_EAST_NETWORK" nsg-phd-test-aca-eastus "$EAST_LOCATION" container-apps
  ensure_nsg "$RG_EAST_NETWORK" nsg-phd-test-postgresql-eastus "$EAST_LOCATION" postgresql
  ensure_nsg "$RG_EAST_NETWORK" nsg-phd-test-management-eastus "$EAST_LOCATION" management
  ensure_rt "$RG_WEST_NETWORK" rt-phd-test-aca-westus3 "$WEST_LOCATION" container-apps-egress
  ensure_rt "$RG_WEST_NETWORK" rt-phd-test-management-westus3 "$WEST_LOCATION" management-egress
  ensure_rt "$RG_EAST_NETWORK" rt-phd-test-aca-eastus "$EAST_LOCATION" container-apps-egress
  ensure_rt "$RG_EAST_NETWORK" rt-phd-test-management-eastus "$EAST_LOCATION" management-egress

  section "West subnets"
  ensure_subnet "$RG_WEST_NETWORK" "$WEST_VNET" snet-aca-infrastructure 10.30.0.0/23 nsg-phd-test-aca-westus3 rt-phd-test-aca-westus3 Microsoft.App/environments
  ensure_subnet "$RG_WEST_NETWORK" "$WEST_VNET" snet-postgresql 10.30.4.0/24 nsg-phd-test-postgresql-westus3 "" Microsoft.DBforPostgreSQL/flexibleServers
  ensure_subnet "$RG_WEST_NETWORK" "$WEST_VNET" snet-private-endpoints 10.30.5.0/24 "" "" "" true
  ensure_subnet "$RG_WEST_NETWORK" "$WEST_VNET" AzureFirewallSubnet 10.30.6.0/26
  ensure_subnet "$RG_WEST_NETWORK" "$WEST_VNET" GatewaySubnet 10.30.6.64/27
  ensure_subnet "$RG_WEST_NETWORK" "$WEST_VNET" snet-management 10.30.7.0/24 nsg-phd-test-management-westus3 rt-phd-test-management-westus3

  section "East subnets"
  ensure_subnet "$RG_EAST_NETWORK" "$EAST_VNET" snet-aca-infrastructure 10.40.0.0/23 nsg-phd-test-aca-eastus rt-phd-test-aca-eastus Microsoft.App/environments
  ensure_subnet "$RG_EAST_NETWORK" "$EAST_VNET" snet-postgresql 10.40.4.0/24 nsg-phd-test-postgresql-eastus "" Microsoft.DBforPostgreSQL/flexibleServers
  ensure_subnet "$RG_EAST_NETWORK" "$EAST_VNET" snet-private-endpoints 10.40.5.0/24 "" "" "" true
  ensure_subnet "$RG_EAST_NETWORK" "$EAST_VNET" AzureFirewallSubnet 10.40.6.0/26
  ensure_subnet "$RG_EAST_NETWORK" "$EAST_VNET" GatewaySubnet 10.40.6.64/27
  ensure_subnet "$RG_EAST_NETWORK" "$EAST_VNET" snet-management 10.40.7.0/24 nsg-phd-test-management-eastus rt-phd-test-management-eastus

  section "Global peering"
  west_id="$(az network vnet show -g "$RG_WEST_NETWORK" -n "$WEST_VNET" --query id -o tsv)"
  east_id="$(az network vnet show -g "$RG_EAST_NETWORK" -n "$EAST_VNET" --query id -o tsv)"
  ensure_peering "$RG_WEST_NETWORK" "$WEST_VNET" peer-westus3-to-eastus "$east_id"
  ensure_peering "$RG_EAST_NETWORK" "$EAST_VNET" peer-eastus-to-westus3 "$west_id"

  section "Private DNS"
  for zone in "${DNS_ZONES[@]}"; do
    az network private-dns zone show -g "$RG_SHARED" -n "$zone" --output none >/dev/null 2>&1 || \
      az network private-dns zone create -g "$RG_SHARED" -n "$zone" --output none
    ensure_dns_link "$zone" link-phd-test-westus3 "$west_id"
    ensure_dns_link "$zone" link-phd-test-eastus "$east_id"
  done

  cat > "$CONFIG_FILE" <<EOF
PROJECT_NAME=$PRODUCT_NAME
ENVIRONMENT=$ENVIRONMENT
PRIMARY_LOCATION=$WEST_LOCATION
SECONDARY_LOCATION=$EAST_LOCATION
RG_SHARED=$RG_SHARED
RG_EDGE=$RG_EDGE
RG_WEST_NETWORK=$RG_WEST_NETWORK
RG_WEST_APP=$RG_WEST_APP
RG_WEST_DATA=$RG_WEST_DATA
RG_EAST_NETWORK=$RG_EAST_NETWORK
RG_EAST_APP=$RG_EAST_APP
RG_EAST_DATA=$RG_EAST_DATA
WEST_VNET=$WEST_VNET
WEST_VNET_ID=$west_id
EAST_VNET=$EAST_VNET
EAST_VNET_ID=$east_id
EOF
  chmod 600 "$CONFIG_FILE"

  section "Validation"
  az network vnet show -g "$RG_WEST_NETWORK" -n "$WEST_VNET" --query '{name:name,location:location,addressSpace:addressSpace.addressPrefixes[0],state:provisioningState}' -o table
  az network vnet show -g "$RG_EAST_NETWORK" -n "$EAST_VNET" --query '{name:name,location:location,addressSpace:addressSpace.addressPrefixes[0],state:provisioningState}' -o table
  az network vnet peering list -g "$RG_WEST_NETWORK" --vnet-name "$WEST_VNET" --query '[].{name:name,state:peeringState}' -o table
  az network vnet peering list -g "$RG_EAST_NETWORK" --vnet-name "$EAST_VNET" --query '[].{name:name,state:peeringState}' -o table

  section "AZ-03 complete"
  echo "NETWORK FOUNDATION READY"
} 2>&1 | tee "$LOG"

echo "Configuration: $CONFIG_FILE"
echo "Log: $LOG"
