#!/usr/bin/env bash
set -Eeuo pipefail

PRODUCT_NAME="Project Health Dashboard"
ENVIRONMENT="test"
WEST_LOCATION="westus3"
EAST_LOCATION="eastus"

RG_WEST_NETWORK="rg-project-health-dashboard-test-network-westus3"
RG_EAST_NETWORK="rg-project-health-dashboard-test-network-eastus"
WEST_VNET="vnet-phd-test-westus3"
EAST_VNET="vnet-phd-test-eastus"

WEST_APPGW_SUBNET="snet-application-gateway"
EAST_APPGW_SUBNET="snet-application-gateway"
WEST_APPGW_PREFIX="10.30.8.0/24"
EAST_APPGW_PREFIX="10.40.8.0/24"
WEST_ACA_SUBNET="snet-aca-infrastructure"
EAST_ACA_SUBNET="snet-aca-infrastructure"

WEST_INGRESS_PIP="pip-phd-test-ingress-westus3"
EAST_INGRESS_PIP="pip-phd-test-ingress-eastus"
WEST_EGRESS_PIP="pip-phd-test-egress-westus3"
EAST_EGRESS_PIP="pip-phd-test-egress-eastus"
WEST_NAT="nat-phd-test-aca-westus3"
EAST_NAT="nat-phd-test-aca-eastus"

BASE_DIR="$HOME/project-health-dashboard-azure"
CONFIG_DIR="$BASE_DIR/config"
LOG_DIR="$BASE_DIR/logs"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
LOG="$LOG_DIR/az03b-public-ip-egress-$STAMP.log"
CONFIG_FILE="$CONFIG_DIR/public-ip-foundation.env"
mkdir -p "$CONFIG_DIR" "$LOG_DIR"

section(){ echo; echo "============================================================"; echo "$1"; echo "============================================================"; }

ensure_subnet(){
  local rg="$1" vnet="$2" name="$3" prefix="$4"
  if az network vnet subnet show -g "$rg" --vnet-name "$vnet" -n "$name" --output none >/dev/null 2>&1; then
    current="$(az network vnet subnet show -g "$rg" --vnet-name "$vnet" -n "$name" --query addressPrefix -o tsv)"
    [ "$current" = "$prefix" ] || { echo "ERROR: $name uses $current, expected $prefix"; exit 1; }
  else
    az network vnet subnet create -g "$rg" --vnet-name "$vnet" -n "$name" --address-prefixes "$prefix" --output none
  fi
  echo "Confirmed subnet: $vnet/$name [$prefix]"
}

ensure_pip(){
  local rg="$1" name="$2" location="$3" purpose="$4"
  if ! az network public-ip show -g "$rg" -n "$name" --output none >/dev/null 2>&1; then
    az network public-ip create -g "$rg" -n "$name" -l "$location" \
      --sku Standard --tier Regional --allocation-method Static --version IPv4 \
      --zone 1 2 3 --idle-timeout 15 --tags \
      "application=$PRODUCT_NAME" "environment=$ENVIRONMENT" \
      "network-purpose=$purpose" "architecture=multi-region" --output none
  fi
  echo "Confirmed static public IP: $name"
}

ensure_nat(){
  local rg="$1" name="$2" location="$3" pip="$4"
  pip_id="$(az network public-ip show -g "$rg" -n "$pip" --query id -o tsv)"
  if az network nat gateway show -g "$rg" -n "$name" --output none >/dev/null 2>&1; then
    az network nat gateway update -g "$rg" -n "$name" --public-ip-addresses "$pip_id" --idle-timeout 10 --output none
  else
    az network nat gateway create -g "$rg" -n "$name" -l "$location" \
      --public-ip-addresses "$pip_id" --idle-timeout 10 --tags \
      "application=$PRODUCT_NAME" "environment=$ENVIRONMENT" \
      "network-purpose=container-apps-egress" "architecture=multi-region" --output none
  fi
  echo "Confirmed NAT Gateway: $name"
}

attach_nat(){
  local rg="$1" vnet="$2" subnet="$3" nat="$4"
  nat_id="$(az network nat gateway show -g "$rg" -n "$nat" --query id -o tsv)"
  az network vnet subnet update -g "$rg" --vnet-name "$vnet" -n "$subnet" --nat-gateway "$nat_id" --output none
  echo "Attached $nat to $vnet/$subnet"
}

{
  section "AZ-03B - Public IP and egress foundation"
  echo "TIME=$(date -u -Is)"

  az network vnet show -g "$RG_WEST_NETWORK" -n "$WEST_VNET" --output none
  az network vnet show -g "$RG_EAST_NETWORK" -n "$EAST_VNET" --output none
  az network vnet subnet show -g "$RG_WEST_NETWORK" --vnet-name "$WEST_VNET" -n "$WEST_ACA_SUBNET" --output none
  az network vnet subnet show -g "$RG_EAST_NETWORK" --vnet-name "$EAST_VNET" -n "$EAST_ACA_SUBNET" --output none

  section "Application Gateway subnets"
  ensure_subnet "$RG_WEST_NETWORK" "$WEST_VNET" "$WEST_APPGW_SUBNET" "$WEST_APPGW_PREFIX"
  ensure_subnet "$RG_EAST_NETWORK" "$EAST_VNET" "$EAST_APPGW_SUBNET" "$EAST_APPGW_PREFIX"

  section "Static public IPs"
  ensure_pip "$RG_WEST_NETWORK" "$WEST_INGRESS_PIP" "$WEST_LOCATION" regional-ingress
  ensure_pip "$RG_EAST_NETWORK" "$EAST_INGRESS_PIP" "$EAST_LOCATION" regional-ingress
  ensure_pip "$RG_WEST_NETWORK" "$WEST_EGRESS_PIP" "$WEST_LOCATION" container-apps-egress
  ensure_pip "$RG_EAST_NETWORK" "$EAST_EGRESS_PIP" "$EAST_LOCATION" container-apps-egress

  section "NAT Gateways"
  ensure_nat "$RG_WEST_NETWORK" "$WEST_NAT" "$WEST_LOCATION" "$WEST_EGRESS_PIP"
  ensure_nat "$RG_EAST_NETWORK" "$EAST_NAT" "$EAST_LOCATION" "$EAST_EGRESS_PIP"
  attach_nat "$RG_WEST_NETWORK" "$WEST_VNET" "$WEST_ACA_SUBNET" "$WEST_NAT"
  attach_nat "$RG_EAST_NETWORK" "$EAST_VNET" "$EAST_ACA_SUBNET" "$EAST_NAT"

  west_ingress="$(az network public-ip show -g "$RG_WEST_NETWORK" -n "$WEST_INGRESS_PIP" --query ipAddress -o tsv)"
  east_ingress="$(az network public-ip show -g "$RG_EAST_NETWORK" -n "$EAST_INGRESS_PIP" --query ipAddress -o tsv)"
  west_egress="$(az network public-ip show -g "$RG_WEST_NETWORK" -n "$WEST_EGRESS_PIP" --query ipAddress -o tsv)"
  east_egress="$(az network public-ip show -g "$RG_EAST_NETWORK" -n "$EAST_EGRESS_PIP" --query ipAddress -o tsv)"

  cat > "$CONFIG_FILE" <<EOF
PROJECT_NAME=$PRODUCT_NAME
ENVIRONMENT=$ENVIRONMENT
WEST_INGRESS_PUBLIC_IP_NAME=$WEST_INGRESS_PIP
WEST_INGRESS_PUBLIC_IP=$west_ingress
EAST_INGRESS_PUBLIC_IP_NAME=$EAST_INGRESS_PIP
EAST_INGRESS_PUBLIC_IP=$east_ingress
WEST_EGRESS_PUBLIC_IP_NAME=$WEST_EGRESS_PIP
WEST_EGRESS_PUBLIC_IP=$west_egress
EAST_EGRESS_PUBLIC_IP_NAME=$EAST_EGRESS_PIP
EAST_EGRESS_PUBLIC_IP=$east_egress
WEST_NAT_GATEWAY=$WEST_NAT
EAST_NAT_GATEWAY=$EAST_NAT
WEST_APPLICATION_GATEWAY_SUBNET=$WEST_APPGW_SUBNET
WEST_APPLICATION_GATEWAY_SUBNET_PREFIX=$WEST_APPGW_PREFIX
EAST_APPLICATION_GATEWAY_SUBNET=$EAST_APPGW_SUBNET
EAST_APPLICATION_GATEWAY_SUBNET_PREFIX=$EAST_APPGW_PREFIX
EOF
  chmod 600 "$CONFIG_FILE"

  section "Validation"
  az network public-ip list --query "[?contains(name, 'pip-phd-test')].{name:name,resourceGroup:resourceGroup,location:location,ipAddress:ipAddress,sku:sku.name,zones:join(',',zones),purpose:tags.\"network-purpose\"}" -o table
  az network nat gateway list --query "[?contains(name, 'nat-phd-test')].{name:name,resourceGroup:resourceGroup,location:location,state:provisioningState}" -o table

  section "Future Cloudflare records"
  echo "Do not create until the Application Gateways are healthy."
  printf '%-32s %-6s %s\n' phd-west-test.onenecklab.com A "$west_ingress"
  printf '%-32s %-6s %s\n' phd-east-test.onenecklab.com A "$east_ingress"

  section "AZ-03B complete"
  echo "West ingress: $west_ingress"
  echo "East ingress: $east_ingress"
  echo "West egress: $west_egress"
  echo "East egress: $east_egress"
  echo "REGIONAL IP FOUNDATION READY"
} 2>&1 | tee "$LOG"

echo "Configuration: $CONFIG_FILE"
echo "Log: $LOG"
