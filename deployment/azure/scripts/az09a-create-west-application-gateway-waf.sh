#!/usr/bin/env bash
set -Eeuo pipefail

SUBSCRIPTION_ID="cd32baeb-7b71-4bc0-8ea3-9f23a50903fe"
LOCATION="westus3"

RG_NETWORK="rg-project-health-dashboard-test-network-westus3"
RG_APP="rg-project-health-dashboard-test-app-westus3"
VNET_NAME="vnet-phd-test-westus3"
APPGW_SUBNET="snet-application-gateway"
APPGW_SUBNET_PREFIX="10.30.8.0/24"
INGRESS_PUBLIC_IP="pip-phd-test-ingress-westus3"

CONTAINERAPPS_ENVIRONMENT="cae-phd-test-westus3"
WEB_APP="ca-phd-test-web-westus3"

APP_GATEWAY="agw-phd-test-westus3"
WAF_POLICY="waf-phd-test-westus3"
HEALTH_PROBE="probe-phd-test-web-health"
PUBLIC_DNS_LABEL="phd-test-westus3-7825cc"

BASE_DIR="$HOME/project-health-dashboard-azure"
CONFIG_DIR="$BASE_DIR/config"
LOG_DIR="$BASE_DIR/logs"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
LOG="$LOG_DIR/az09a-west-application-gateway-waf-$STAMP.log"
STATE_FILE="$CONFIG_DIR/az09a-west-application-gateway-waf.env"

mkdir -p "$CONFIG_DIR" "$LOG_DIR"

section() {
    echo
    echo "============================================================"
    echo "$1"
    echo "============================================================"
}

fail() {
    echo "ERROR: $*" >&2
    return 1
}

wait_for_gateway() {
    local attempt state
    for attempt in $(seq 1 80); do
        state="$(az network application-gateway show \
            -g "$RG_NETWORK" \
            -n "$APP_GATEWAY" \
            --query provisioningState \
            -o tsv 2>/dev/null || true)"
        echo "APP_GATEWAY_PROVISIONING_CHECK[$attempt]=${state:-not-found}"
        if [ "$state" = "Succeeded" ]; then
            return 0
        fi
        if [ "$state" = "Failed" ]; then
            return 1
        fi
        sleep 30
    done
    return 1
}

wait_for_backend_health() {
    local attempt health
    for attempt in $(seq 1 30); do
        health="$(az network application-gateway show-backend-health \
            -g "$RG_NETWORK" \
            -n "$APP_GATEWAY" \
            --query 'backendAddressPools[0].backendHttpSettingsCollection[0].servers[0].health' \
            -o tsv 2>/dev/null || true)"
        echo "BACKEND_HEALTH_CHECK[$attempt]=${health:-unknown}"
        if [ "$health" = "Healthy" ]; then
            return 0
        fi
        sleep 20
    done
    return 1
}

wait_for_public_http() {
    local url="$1"
    local attempt status
    for attempt in $(seq 1 30); do
        status="$(curl -sS -o /dev/null -w '%{http_code}' --connect-timeout 10 --max-time 20 "$url/" 2>/dev/null || true)"
        echo "PUBLIC_HTTP_CHECK[$attempt]=${status:-000}"
        if [ "$status" = "200" ]; then
            return 0
        fi
        sleep 20
    done
    return 1
}

{
    section "AZ-09A - West Application Gateway WAF Public Entry"
    echo "TIME=$(date -u -Is)"
    echo "BILLABLE_APPLICATION_GATEWAY_WAF_V2=true"
    echo "ONGOING_GATEWAY_AND_CAPACITY_CHARGES=true"
    echo "ACR_IMAGE_REBUILD=false"
    echo "CONTAINER_APP_REDEPLOY=false"
    echo "DATABASE_CHANGE=false"
    echo "EAST_POSTGRESQL_REPLICA_CREATED=false"
    echo "INITIAL_PUBLIC_LISTENER_PROTOCOL=HTTP"
    echo "TLS_AND_CUSTOM_DOMAIN_PENDING=true"

    [ "${PHD_CREATE_WEST_APP_GATEWAY_WAF:-}" = "YES" ] \
        || fail "Set PHD_CREATE_WEST_APP_GATEWAY_WAF=YES to authorize the billable gateway creation."

    command -v az >/dev/null 2>&1 || fail "Azure CLI is required."
    command -v curl >/dev/null 2>&1 || fail "curl is required."

    az account set --subscription "$SUBSCRIPTION_ID"
    CURRENT_SUBSCRIPTION="$(az account show --query id -o tsv)"
    [ "$CURRENT_SUBSCRIPTION" = "$SUBSCRIPTION_ID" ] || fail "Active subscription does not match."
    echo "CURRENT_SUBSCRIPTION_MATCH=yes"

    section "Validating existing West resources"

    ENV_INTERNAL="$(az containerapp env show -g "$RG_APP" -n "$CONTAINERAPPS_ENVIRONMENT" --query properties.vnetConfiguration.internal -o tsv)"
    ENV_STATE="$(az containerapp env show -g "$RG_APP" -n "$CONTAINERAPPS_ENVIRONMENT" --query properties.provisioningState -o tsv)"
    WEB_STATE="$(az containerapp show -g "$RG_APP" -n "$WEB_APP" --query properties.provisioningState -o tsv)"
    WEB_FQDN="$(az containerapp show -g "$RG_APP" -n "$WEB_APP" --query properties.configuration.ingress.fqdn -o tsv)"
    WEB_HEALTH="$(az containerapp revision list -g "$RG_APP" -n "$WEB_APP" --query '[?properties.active].properties.healthState | [0]' -o tsv)"
    SUBNET_PREFIX="$(az network vnet subnet show -g "$RG_NETWORK" --vnet-name "$VNET_NAME" -n "$APPGW_SUBNET" --query addressPrefix -o tsv)"
    PIP_SKU="$(az network public-ip show -g "$RG_NETWORK" -n "$INGRESS_PUBLIC_IP" --query sku.name -o tsv)"
    PIP_ALLOCATION="$(az network public-ip show -g "$RG_NETWORK" -n "$INGRESS_PUBLIC_IP" --query publicIPAllocationMethod -o tsv)"
    PIP_ASSOCIATION="$(az network public-ip show -g "$RG_NETWORK" -n "$INGRESS_PUBLIC_IP" --query ipConfiguration.id -o tsv 2>/dev/null || true)"

    echo "CONTAINERAPPS_ENVIRONMENT_INTERNAL=$ENV_INTERNAL"
    echo "CONTAINERAPPS_ENVIRONMENT_STATE=$ENV_STATE"
    echo "WEB_CONTAINER_APP_STATE=$WEB_STATE"
    echo "WEB_ACTIVE_REVISION_HEALTH=$WEB_HEALTH"
    echo "WEB_CONTAINER_APP_FQDN=$WEB_FQDN"
    echo "APPLICATION_GATEWAY_SUBNET_PREFIX=$SUBNET_PREFIX"
    echo "INGRESS_PUBLIC_IP_SKU=$PIP_SKU"
    echo "INGRESS_PUBLIC_IP_ALLOCATION=$PIP_ALLOCATION"
    echo "INGRESS_PUBLIC_IP_ALREADY_ASSOCIATED=$([ -n "$PIP_ASSOCIATION" ] && echo yes || echo no)"

    [ "$ENV_INTERNAL" = "true" ] || fail "Container Apps environment is not internal."
    [ "$ENV_STATE" = "Succeeded" ] || fail "Container Apps environment is not ready."
    [ "$WEB_STATE" = "Succeeded" ] || fail "Web Container App is not ready."
    [ "$WEB_HEALTH" = "Healthy" ] || fail "Web Container App active revision is not healthy."
    [ -n "$WEB_FQDN" ] || fail "Web Container App FQDN is empty."
    [ "$SUBNET_PREFIX" = "$APPGW_SUBNET_PREFIX" ] || fail "Application Gateway subnet prefix is $SUBNET_PREFIX, expected $APPGW_SUBNET_PREFIX."
    [ "$PIP_SKU" = "Standard" ] || fail "Ingress public IP is not Standard SKU."
    [ "$PIP_ALLOCATION" = "Static" ] || fail "Ingress public IP is not static."

    if ! az network application-gateway show -g "$RG_NETWORK" -n "$APP_GATEWAY" --output none >/dev/null 2>&1; then
        [ -z "$PIP_ASSOCIATION" ] || fail "Ingress public IP is already associated with another resource."
    fi

    section "Assigning public DNS label"

    az network public-ip update \
        -g "$RG_NETWORK" \
        -n "$INGRESS_PUBLIC_IP" \
        --dns-name "$PUBLIC_DNS_LABEL" \
        --only-show-errors \
        --output none

    PUBLIC_IP="$(az network public-ip show -g "$RG_NETWORK" -n "$INGRESS_PUBLIC_IP" --query ipAddress -o tsv)"
    PUBLIC_FQDN="$(az network public-ip show -g "$RG_NETWORK" -n "$INGRESS_PUBLIC_IP" --query dnsSettings.fqdn -o tsv)"
    [ -n "$PUBLIC_IP" ] || fail "Ingress public IP address is empty."
    [ -n "$PUBLIC_FQDN" ] || fail "Ingress public DNS FQDN is empty."

    echo "WEST_INGRESS_PUBLIC_IP=$PUBLIC_IP"
    echo "WEST_INGRESS_PUBLIC_FQDN=$PUBLIC_FQDN"

    section "Creating or validating WAF policy"

    if ! az network application-gateway waf-policy show -g "$RG_NETWORK" -n "$WAF_POLICY" --output none >/dev/null 2>&1; then
        az network application-gateway waf-policy create \
            -g "$RG_NETWORK" \
            -n "$WAF_POLICY" \
            -l "$LOCATION" \
            --type OWASP \
            --version 3.2 \
            --tags \
                application="Project Health Dashboard" \
                environment=test \
                region-role=primary \
                security-control=waf \
            --only-show-errors \
            --output none
        echo "WAF_POLICY_ACTION=created"
    else
        echo "WAF_POLICY_ACTION=existing"
    fi

    az network application-gateway waf-policy policy-setting update \
        -g "$RG_NETWORK" \
        --policy-name "$WAF_POLICY" \
        --state Enabled \
        --mode Detection \
        --request-body-check true \
        --only-show-errors \
        --output none

    WAF_POLICY_ID="$(az network application-gateway waf-policy show -g "$RG_NETWORK" -n "$WAF_POLICY" --query id -o tsv)"
    WAF_MODE="$(az network application-gateway waf-policy policy-setting list -g "$RG_NETWORK" --policy-name "$WAF_POLICY" --query mode -o tsv)"
    echo "WAF_POLICY_ID=$WAF_POLICY_ID"
    echo "WAF_POLICY_MODE=$WAF_MODE"
    [ "$WAF_MODE" = "Detection" ] || fail "WAF policy is not in Detection mode."

    section "Creating West Application Gateway WAF_v2"

    if ! az network application-gateway show -g "$RG_NETWORK" -n "$APP_GATEWAY" --output none >/dev/null 2>&1; then
        az network application-gateway create \
            -g "$RG_NETWORK" \
            -n "$APP_GATEWAY" \
            -l "$LOCATION" \
            --sku WAF_v2 \
            --min-capacity 0 \
            --max-capacity 2 \
            --zones 1 2 3 \
            --vnet-name "$VNET_NAME" \
            --subnet "$APPGW_SUBNET" \
            --public-ip-address "$INGRESS_PUBLIC_IP" \
            --frontend-port 80 \
            --http-settings-cookie-based-affinity Disabled \
            --http-settings-port 443 \
            --http-settings-protocol Https \
            --routing-rule-type Basic \
            --priority 100 \
            --servers "$WEB_FQDN" \
            --waf-policy "$WAF_POLICY_ID" \
            --tags \
                application="Project Health Dashboard" \
                environment=test \
                region-role=primary \
                network-purpose=regional-ingress \
            --no-wait \
            --only-show-errors \
            --output none
        echo "APPLICATION_GATEWAY_ACTION=submitted"
    else
        echo "APPLICATION_GATEWAY_ACTION=existing"
    fi

    wait_for_gateway || fail "Application Gateway provisioning did not reach Succeeded."

    APPGW_SKU="$(az network application-gateway show -g "$RG_NETWORK" -n "$APP_GATEWAY" --query sku.name -o tsv)"
    APPGW_STATE="$(az network application-gateway show -g "$RG_NETWORK" -n "$APP_GATEWAY" --query provisioningState -o tsv)"
    APPGW_ID="$(az network application-gateway show -g "$RG_NETWORK" -n "$APP_GATEWAY" --query id -o tsv)"

    echo "APPLICATION_GATEWAY_SKU=$APPGW_SKU"
    echo "APPLICATION_GATEWAY_STATE=$APPGW_STATE"
    [ "$APPGW_SKU" = "WAF_v2" ] || fail "Application Gateway SKU is not WAF_v2."
    [ "$APPGW_STATE" = "Succeeded" ] || fail "Application Gateway is not ready."

    section "Configuring Container Apps backend and health probe"

    BACKEND_POOL_NAME="$(az network application-gateway address-pool list -g "$RG_NETWORK" --gateway-name "$APP_GATEWAY" --query '[0].name' -o tsv)"
    HTTP_SETTINGS_NAME="$(az network application-gateway http-settings list -g "$RG_NETWORK" --gateway-name "$APP_GATEWAY" --query '[0].name' -o tsv)"

    [ -n "$BACKEND_POOL_NAME" ] || fail "Application Gateway backend pool name is empty."
    [ -n "$HTTP_SETTINGS_NAME" ] || fail "Application Gateway HTTP settings name is empty."

    az network application-gateway address-pool update \
        -g "$RG_NETWORK" \
        --gateway-name "$APP_GATEWAY" \
        -n "$BACKEND_POOL_NAME" \
        --servers "$WEB_FQDN" \
        --only-show-errors \
        --output none

    if az network application-gateway probe show \
        -g "$RG_NETWORK" \
        --gateway-name "$APP_GATEWAY" \
        -n "$HEALTH_PROBE" \
        --output none >/dev/null 2>&1; then
        az network application-gateway probe update \
            -g "$RG_NETWORK" \
            --gateway-name "$APP_GATEWAY" \
            -n "$HEALTH_PROBE" \
            --protocol Https \
            --host "$WEB_FQDN" \
            --path /health \
            --interval 30 \
            --timeout 20 \
            --threshold 3 \
            --match-status-codes 200-399 \
            --only-show-errors \
            --output none
        echo "APPLICATION_GATEWAY_HEALTH_PROBE_ACTION=updated"
    else
        az network application-gateway probe create \
            -g "$RG_NETWORK" \
            --gateway-name "$APP_GATEWAY" \
            -n "$HEALTH_PROBE" \
            --protocol Https \
            --host "$WEB_FQDN" \
            --path /health \
            --interval 30 \
            --timeout 20 \
            --threshold 3 \
            --match-status-codes 200-399 \
            --only-show-errors \
            --output none
        echo "APPLICATION_GATEWAY_HEALTH_PROBE_ACTION=created"
    fi

    az network application-gateway http-settings update \
        -g "$RG_NETWORK" \
        --gateway-name "$APP_GATEWAY" \
        -n "$HTTP_SETTINGS_NAME" \
        --port 443 \
        --protocol Https \
        --cookie-based-affinity Disabled \
        --host-name-from-backend-pool true \
        --sni-name "$WEB_FQDN" \
        --probe "$HEALTH_PROBE" \
        --timeout 60 \
        --only-show-errors \
        --output none

    az network application-gateway update \
        -g "$RG_NETWORK" \
        -n "$APP_GATEWAY" \
        --set firewallPolicy.id="$WAF_POLICY_ID" \
        --only-show-errors \
        --output none

    wait_for_gateway || fail "Application Gateway configuration update did not reach Succeeded."
    wait_for_backend_health || fail "Application Gateway backend did not become healthy."

    section "Validating public browser endpoint"

    PUBLIC_URL="http://$PUBLIC_FQDN"
    wait_for_public_http "$PUBLIC_URL" || fail "Public Application Gateway endpoint did not return HTTP 200."

    ROOT_STATUS="$(curl -sS -o /dev/null -w '%{http_code}' --connect-timeout 10 --max-time 30 "$PUBLIC_URL/")"
    HEALTH_STATUS="$(curl -sS -o /dev/null -w '%{http_code}' --connect-timeout 10 --max-time 30 "$PUBLIC_URL/health")"

    echo "PUBLIC_WEB_URL=$PUBLIC_URL"
    echo "PUBLIC_ROOT_HTTP_STATUS=$ROOT_STATUS"
    echo "PUBLIC_HEALTH_HTTP_STATUS=$HEALTH_STATUS"

    [ "$ROOT_STATUS" = "200" ] || fail "Public root endpoint did not return HTTP 200."
    [ "$HEALTH_STATUS" = "200" ] || fail "Public health endpoint did not return HTTP 200."

    cat > "$STATE_FILE" <<EOF
APPLICATION_GATEWAY=$APP_GATEWAY
APPLICATION_GATEWAY_ID=$APPGW_ID
APPLICATION_GATEWAY_SKU=$APPGW_SKU
APPLICATION_GATEWAY_STATE=$APPGW_STATE
WAF_POLICY=$WAF_POLICY
WAF_POLICY_MODE=$WAF_MODE
BACKEND_CONTAINER_APP_FQDN=$WEB_FQDN
BACKEND_HEALTH=Healthy
WEST_INGRESS_PUBLIC_IP=$PUBLIC_IP
WEST_INGRESS_PUBLIC_FQDN=$PUBLIC_FQDN
PUBLIC_WEB_URL=$PUBLIC_URL
PUBLIC_ROOT_HTTP_STATUS=$ROOT_STATUS
PUBLIC_HEALTH_HTTP_STATUS=$HEALTH_STATUS
TLS_AND_CUSTOM_DOMAIN_PENDING=true
CREATED_AT=$(date -u -Is)
EOF
    chmod 600 "$STATE_FILE"

    echo "WEST_PUBLIC_ENTRY_RESULT=READY"
    echo "WEST_PUBLIC_ENTRY_STATE_FILE=$STATE_FILE"
    echo "EAST_POSTGRESQL_REPLICA_CREATED=false"

    echo
    echo "************************************************************"
    echo "WEST APPLICATION GATEWAY WAF PUBLIC ENTRY READY"
    echo "************************************************************"

} 2>&1 | tee "$LOG"

echo
echo "Application Gateway deployment log: $LOG"
