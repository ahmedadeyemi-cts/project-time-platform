#!/usr/bin/env bash
set -Eeuo pipefail

SUBSCRIPTION_ID="cd32baeb-7b71-4bc0-8ea3-9f23a50903fe"
RG_SHARED="rg-project-health-dashboard-shared-global"
RG_APP="rg-project-health-dashboard-test-app-westus3"
RG_WEST_NETWORK="rg-project-health-dashboard-test-network-westus3"
RG_EAST_NETWORK="rg-project-health-dashboard-test-network-eastus"
ENVIRONMENT_NAME="cae-phd-test-westus3"
WEST_VNET="vnet-phd-test-westus3"
EAST_VNET="vnet-phd-test-eastus"
WEST_LINK="link-phd-test-westus3"
EAST_LINK="link-phd-test-eastus"

BASE_DIR="$HOME/project-health-dashboard-azure"
CONFIG_DIR="$BASE_DIR/config"
LOG_DIR="$BASE_DIR/logs"
STATE_FILE="$CONFIG_DIR/az06c-west-container-apps-private-dns.env"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
LOG="$LOG_DIR/az06c-configure-west-container-apps-private-dns-$STAMP.log"
mkdir -p "$CONFIG_DIR" "$LOG_DIR"

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

ensure_link() {
    local zone="$1"
    local link_name="$2"
    local vnet_id="$3"

    if az network private-dns link vnet show \
        --resource-group "$RG_SHARED" \
        --zone-name "$zone" \
        --name "$link_name" \
        --output none >/dev/null 2>&1; then
        echo "Existing VNet link: $zone/$link_name"
    else
        az network private-dns link vnet create \
            --resource-group "$RG_SHARED" \
            --zone-name "$zone" \
            --name "$link_name" \
            --virtual-network "$vnet_id" \
            --registration-enabled false \
            --only-show-errors \
            --output none
        echo "Created VNet link: $zone/$link_name"
    fi
}

{
    section "AZ-06C - Configure West Container Apps Private DNS"
    echo "TIME=$(date -u -Is)"
    echo "SUBSCRIPTION_ID=$SUBSCRIPTION_ID"
    echo "ENVIRONMENT_NAME=$ENVIRONMENT_NAME"
    echo "PRIVATE_DNS_WRITE_ACTION=true"
    echo "APPLICATION_CONTAINER_DEPLOYMENT=false"
    echo "EAST_POSTGRESQL_REPLICA_CREATED=false"

    [ "${PHD_CREATE_BILLABLE_WEST_ACA_DNS:-}" = "YES" ] \
        || fail "Set PHD_CREATE_BILLABLE_WEST_ACA_DNS=YES only when ready to create the private DNS zone and records."

    az account set --subscription "$SUBSCRIPTION_ID"
    CURRENT_SUBSCRIPTION_ID="$(az account show --query id -o tsv)"
    [ "$CURRENT_SUBSCRIPTION_ID" = "$SUBSCRIPTION_ID" ] \
        || fail "Current Azure subscription does not match the intended subscription."

    echo "CURRENT_SUBSCRIPTION_MATCH=yes"

    section "Reading West Container Apps environment"

    ENV_JSON="$(az containerapp env show \
        --resource-group "$RG_APP" \
        --name "$ENVIRONMENT_NAME" \
        --only-show-errors \
        --output json)"

    readarray -t ENV_FIELDS < <(python3 - "$ENV_JSON" <<'PY'
import json
import sys
obj = json.loads(sys.argv[1])
props = obj.get("properties") or obj
vnet = props.get("vnetConfiguration") or {}
print(obj.get("provisioningState") or props.get("provisioningState") or "")
print(props.get("defaultDomain") or obj.get("defaultDomain") or "")
print(props.get("staticIp") or obj.get("staticIp") or "")
print(str(vnet.get("internal")).lower())
PY
    )

    PROVISIONING_STATE="${ENV_FIELDS[0]:-}"
    DEFAULT_DOMAIN="${ENV_FIELDS[1]:-}"
    STATIC_IP="${ENV_FIELDS[2]:-}"
    INTERNAL_ENVIRONMENT="${ENV_FIELDS[3]:-}"

    [ "${PROVISIONING_STATE,,}" = "succeeded" ] \
        || fail "Container Apps environment is not Succeeded: $PROVISIONING_STATE"
    [ "$INTERNAL_ENVIRONMENT" = "true" ] \
        || fail "Container Apps environment is not internal."
    [ -n "$DEFAULT_DOMAIN" ] || fail "Container Apps default domain is empty."
    [ -n "$STATIC_IP" ] || fail "Container Apps static IP is empty."

    echo "WEST_CONTAINER_APPS_PROVISIONING_STATE=$PROVISIONING_STATE"
    echo "WEST_CONTAINER_APPS_INTERNAL=$INTERNAL_ENVIRONMENT"
    echo "WEST_CONTAINER_APPS_DEFAULT_DOMAIN=$DEFAULT_DOMAIN"
    echo "WEST_CONTAINER_APPS_STATIC_IP=$STATIC_IP"

    WEST_VNET_ID="$(az network vnet show \
        --resource-group "$RG_WEST_NETWORK" \
        --name "$WEST_VNET" \
        --query id \
        --output tsv)"

    EAST_VNET_ID="$(az network vnet show \
        --resource-group "$RG_EAST_NETWORK" \
        --name "$EAST_VNET" \
        --query id \
        --output tsv)"

    section "Creating or confirming private DNS zone"

    if az network private-dns zone show \
        --resource-group "$RG_SHARED" \
        --name "$DEFAULT_DOMAIN" \
        --output none >/dev/null 2>&1; then
        echo "Existing private DNS zone: $DEFAULT_DOMAIN"
    else
        az network private-dns zone create \
            --resource-group "$RG_SHARED" \
            --name "$DEFAULT_DOMAIN" \
            --tags \
                "application=Project Health Dashboard" \
                "environment=test" \
                "resource-function=container-apps-private-dns" \
                "region-role=primary" \
            --only-show-errors \
            --output none
        echo "Created private DNS zone: $DEFAULT_DOMAIN"
    fi

    section "Linking regional VNets"
    ensure_link "$DEFAULT_DOMAIN" "$WEST_LINK" "$WEST_VNET_ID"
    ensure_link "$DEFAULT_DOMAIN" "$EAST_LINK" "$EAST_VNET_ID"

    section "Creating or confirming wildcard A record"

    EXISTING_IPS="$(az network private-dns record-set a show \
        --resource-group "$RG_SHARED" \
        --zone-name "$DEFAULT_DOMAIN" \
        --name "*" \
        --query 'aRecords[].ipv4Address' \
        --output tsv 2>/dev/null || true)"

    if grep -Fxq "$STATIC_IP" <<< "$EXISTING_IPS"; then
        echo "Existing wildcard A record: *.$DEFAULT_DOMAIN -> $STATIC_IP"
    else
        if [ -n "$EXISTING_IPS" ]; then
            while IFS= read -r OLD_IP; do
                [ -n "$OLD_IP" ] || continue
                az network private-dns record-set a remove-record \
                    --resource-group "$RG_SHARED" \
                    --zone-name "$DEFAULT_DOMAIN" \
                    --record-set-name "*" \
                    --ipv4-address "$OLD_IP" \
                    --only-show-errors \
                    --output none
            done <<< "$EXISTING_IPS"
        fi

        az network private-dns record-set a add-record \
            --resource-group "$RG_SHARED" \
            --zone-name "$DEFAULT_DOMAIN" \
            --record-set-name "*" \
            --ipv4-address "$STATIC_IP" \
            --only-show-errors \
            --output none

        echo "Created wildcard A record: *.$DEFAULT_DOMAIN -> $STATIC_IP"
    fi

    section "Validation"

    ZONE_ID="$(az network private-dns zone show \
        --resource-group "$RG_SHARED" \
        --name "$DEFAULT_DOMAIN" \
        --query id \
        --output tsv)"

    WEST_LINK_STATE="$(az network private-dns link vnet show \
        --resource-group "$RG_SHARED" \
        --zone-name "$DEFAULT_DOMAIN" \
        --name "$WEST_LINK" \
        --query virtualNetworkLinkState \
        --output tsv)"

    EAST_LINK_STATE="$(az network private-dns link vnet show \
        --resource-group "$RG_SHARED" \
        --zone-name "$DEFAULT_DOMAIN" \
        --name "$EAST_LINK" \
        --query virtualNetworkLinkState \
        --output tsv)"

    WILDCARD_IP="$(az network private-dns record-set a show \
        --resource-group "$RG_SHARED" \
        --zone-name "$DEFAULT_DOMAIN" \
        --name "*" \
        --query 'aRecords[0].ipv4Address' \
        --output tsv)"

    [ "$WILDCARD_IP" = "$STATIC_IP" ] \
        || fail "Wildcard DNS record does not match the environment static IP."

    cat > "$STATE_FILE" <<EOF
AZURE_SUBSCRIPTION_ID=$SUBSCRIPTION_ID
WEST_CONTAINER_APPS_ENVIRONMENT=$ENVIRONMENT_NAME
WEST_CONTAINER_APPS_DEFAULT_DOMAIN=$DEFAULT_DOMAIN
WEST_CONTAINER_APPS_STATIC_IP=$STATIC_IP
WEST_CONTAINER_APPS_PRIVATE_DNS_ZONE_ID=$ZONE_ID
WEST_CONTAINER_APPS_PRIVATE_DNS_WEST_LINK=$WEST_LINK
WEST_CONTAINER_APPS_PRIVATE_DNS_EAST_LINK=$EAST_LINK
WEST_CONTAINER_APPS_PRIVATE_DNS_WILDCARD_IP=$WILDCARD_IP
WEST_CONTAINER_APPS_PRIVATE_DNS_READY_AT=$(date -u -Is)
EOF
    chmod 600 "$STATE_FILE"

    echo "PRIVATE_DNS_ZONE=$DEFAULT_DOMAIN"
    echo "PRIVATE_DNS_WEST_LINK_STATE=${WEST_LINK_STATE:-not-reported}"
    echo "PRIVATE_DNS_EAST_LINK_STATE=${EAST_LINK_STATE:-not-reported}"
    echo "PRIVATE_DNS_WILDCARD_RECORD=*.$DEFAULT_DOMAIN"
    echo "PRIVATE_DNS_WILDCARD_IP=$WILDCARD_IP"
    echo "PRIVATE_DNS_STATE_FILE=$STATE_FILE"
    echo "WEST_CONTAINER_APPS_PRIVATE_DNS_RESULT=READY"

    echo
    echo "************************************************************"
    echo "WEST CONTAINER APPS PRIVATE DNS READY"
    echo "************************************************************"

} 2>&1 | tee "$LOG"

echo
echo "Private DNS log: $LOG"
