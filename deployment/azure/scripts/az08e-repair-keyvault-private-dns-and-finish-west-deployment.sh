#!/usr/bin/env bash
set -Eeuo pipefail

SUBSCRIPTION_ID="cd32baeb-7b71-4bc0-8ea3-9f23a50903fe"
RG_SHARED="rg-project-health-dashboard-shared-global"
RG_NETWORK="rg-project-health-dashboard-test-network-westus3"
RG_APP="rg-project-health-dashboard-test-app-westus3"
RG_DATA="rg-project-health-dashboard-test-data-westus3"
VNET_NAME="vnet-phd-test-westus3"
CONTAINERAPPS_ENVIRONMENT="cae-phd-test-westus3"
WEST_IDENTITY="id-phd-test-app-westus3"
KEY_VAULT_NAME="kv-phd-t-w3-7825cc"
KEY_VAULT_PRIVATE_ENDPOINT="pe-phd-test-kv-westus3"
KEY_VAULT_PRIVATE_DNS_ZONE="privatelink.vaultcore.azure.net"
KEY_VAULT_ZONE_GROUP="default"
KEY_VAULT_ZONE_CONFIG="keyvault"
KEY_VAULT_LINK_NAME="link-phd-test-keyvault-westus3"
POSTGRES_SERVER="pg-phd-test-w3-7825cc"
POSTGRES_DATABASE="project_health_dashboard"
POSTGRES_ADMIN="phdpgadmin"
APP_INSIGHTS="appi-phd-test-westus3"
ACR_NAME="acrphdtest7825cc"
API_REPOSITORY="project-health-dashboard-api"
WEB_REPOSITORY="project-health-dashboard-web"
IMAGE_TAG="abf45bf82474"
SOURCE_COMMIT="abf45bf824747767282f68fa5bd50909f9751eb0"
API_APP="ca-phd-test-api-westus3"
WEB_APP="ca-phd-test-web-westus3"

BASE_DIR="$HOME/project-health-dashboard-azure"
CONFIG_DIR="$BASE_DIR/config"
LOG_DIR="$BASE_DIR/logs"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
LOG="$LOG_DIR/az08e-repair-keyvault-private-dns-and-finish-west-deployment-$STAMP.log"
STATE_FILE="$CONFIG_DIR/az08e-west-application-deployment.env"
WORK_DIR="$(mktemp -d /tmp/phd-az08e-XXXXXX)"

mkdir -p "$CONFIG_DIR" "$LOG_DIR"
chmod 700 "$WORK_DIR"
trap 'rm -rf "$WORK_DIR"' EXIT

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

wait_for_provisioning() {
    local app_name="$1"
    local attempt state
    for attempt in $(seq 1 60); do
        state="$(az containerapp show -g "$RG_APP" -n "$app_name" --query properties.provisioningState -o tsv 2>/dev/null || true)"
        echo "PROVISIONING_CHECK[$app_name][$attempt]=${state:-not-found}"
        if [ "$state" = "Succeeded" ]; then
            return 0
        fi
        sleep 10
    done
    return 1
}

wait_for_revision_health() {
    local app_name="$1"
    local attempt health running
    for attempt in $(seq 1 60); do
        health="$(az containerapp revision list -g "$RG_APP" -n "$app_name" --query "[?properties.active].properties.healthState | [0]" -o tsv 2>/dev/null || true)"
        running="$(az containerapp revision list -g "$RG_APP" -n "$app_name" --query "[?properties.active].properties.runningState | [0]" -o tsv 2>/dev/null || true)"
        echo "REVISION_CHECK[$app_name][$attempt]=health:${health:-unknown},running:${running:-unknown}"
        if [ "$health" = "Healthy" ] && [ "$running" = "Running" ]; then
            return 0
        fi
        sleep 10
    done
    return 1
}

wait_for_absence() {
    local app_name="$1"
    local attempt
    for attempt in $(seq 1 36); do
        if ! az containerapp show -g "$RG_APP" -n "$app_name" --output none >/dev/null 2>&1; then
            echo "CONTAINER_APP_REMOVED[$app_name]=yes"
            return 0
        fi
        sleep 5
    done
    return 1
}

{
    section "AZ-08E - Repair Key Vault Private DNS and Finish West Deployment"
    echo "TIME=$(date -u -Is)"
    echo "ACR_IMAGE_REBUILD=false"
    echo "REUSE_EXISTING_ACR_IMAGES=true"
    echo "SOURCE_COMMIT=$SOURCE_COMMIT"
    echo "EAST_POSTGRESQL_REPLICA_CREATED=false"

    [ "${PHD_REPAIR_KEYVAULT_DNS_AND_FINISH:-}" = "YES" ] \
        || fail "Set PHD_REPAIR_KEYVAULT_DNS_AND_FINISH=YES to authorize the DNS repair and deployment continuation."

    command -v az >/dev/null 2>&1 || fail "Azure CLI is required."

    az account set --subscription "$SUBSCRIPTION_ID"
    CURRENT_SUBSCRIPTION="$(az account show --query id -o tsv)"
    [ "$CURRENT_SUBSCRIPTION" = "$SUBSCRIPTION_ID" ] || fail "Active subscription does not match."
    echo "CURRENT_SUBSCRIPTION_MATCH=yes"

    az config set extension.use_dynamic_install=yes_without_prompt >/dev/null
    az extension add --name containerapp --upgrade --only-show-errors --output none
    echo "CONTAINERAPP_EXTENSION_VERSION=$(az extension show --name containerapp --query version -o tsv)"

    section "Inspecting West Key Vault private endpoint"

    KV_ZONE_ID="$(az network private-dns zone show -g "$RG_SHARED" -n "$KEY_VAULT_PRIVATE_DNS_ZONE" --query id -o tsv)"
    WEST_VNET_ID="$(az network vnet show -g "$RG_NETWORK" -n "$VNET_NAME" --query id -o tsv)"
    KV_PE_STATE="$(az network private-endpoint show -g "$RG_NETWORK" -n "$KEY_VAULT_PRIVATE_ENDPOINT" --query provisioningState -o tsv)"
    KV_PE_CONNECTION="$(az network private-endpoint show -g "$RG_NETWORK" -n "$KEY_VAULT_PRIVATE_ENDPOINT" --query 'privateLinkServiceConnections[0].privateLinkServiceConnectionState.status' -o tsv)"
    KV_PE_NIC_ID="$(az network private-endpoint show -g "$RG_NETWORK" -n "$KEY_VAULT_PRIVATE_ENDPOINT" --query 'networkInterfaces[0].id' -o tsv)"
    KV_PE_IP="$(az network nic show --ids "$KV_PE_NIC_ID" --query 'ipConfigurations[0].privateIPAddress' -o tsv)"

    [ -n "$KV_ZONE_ID" ] || fail "Key Vault private DNS zone ID is empty."
    [ -n "$WEST_VNET_ID" ] || fail "West VNet ID is empty."
    [ "$KV_PE_STATE" = "Succeeded" ] || fail "Key Vault private endpoint state is $KV_PE_STATE."
    [ "$KV_PE_CONNECTION" = "Approved" ] || fail "Key Vault private endpoint connection is $KV_PE_CONNECTION."
    [ -n "$KV_PE_IP" ] || fail "Key Vault private endpoint IP is empty."

    echo "KEY_VAULT_PRIVATE_ENDPOINT_STATE=$KV_PE_STATE"
    echo "KEY_VAULT_PRIVATE_ENDPOINT_CONNECTION=$KV_PE_CONNECTION"
    echo "KEY_VAULT_PRIVATE_ENDPOINT_IP=$KV_PE_IP"

    section "Ensuring Key Vault private DNS zone group"

    ZONE_GROUP_ACTION="existing"
    ZONE_GROUP_JSON="$WORK_DIR/keyvault-zone-group.json"

    if ! az network private-endpoint dns-zone-group show \
        -g "$RG_NETWORK" \
        --endpoint-name "$KEY_VAULT_PRIVATE_ENDPOINT" \
        -n "$KEY_VAULT_ZONE_GROUP" \
        -o json > "$ZONE_GROUP_JSON" 2>/dev/null; then
        az network private-endpoint dns-zone-group create \
            -g "$RG_NETWORK" \
            --endpoint-name "$KEY_VAULT_PRIVATE_ENDPOINT" \
            -n "$KEY_VAULT_ZONE_GROUP" \
            --zone-name "$KEY_VAULT_ZONE_CONFIG" \
            --private-dns-zone "$KV_ZONE_ID" \
            --only-show-errors \
            -o none
        ZONE_GROUP_ACTION="created"
    fi

    az network private-endpoint dns-zone-group show \
        -g "$RG_NETWORK" \
        --endpoint-name "$KEY_VAULT_PRIVATE_ENDPOINT" \
        -n "$KEY_VAULT_ZONE_GROUP" \
        -o json > "$ZONE_GROUP_JSON"

    CORRECT_ZONE_COUNT="$(python3 - "$ZONE_GROUP_JSON" "$KV_ZONE_ID" <<'PY'
import json
import sys
from pathlib import Path
obj = json.loads(Path(sys.argv[1]).read_text())
wanted = sys.argv[2].lower()
print(sum(1 for item in (obj.get("privateDnsZoneConfigs") or []) if str(item.get("privateDnsZoneId") or "").lower() == wanted))
PY
)"

    if [ "$CORRECT_ZONE_COUNT" != "1" ]; then
        az network private-endpoint dns-zone-group delete \
            -g "$RG_NETWORK" \
            --endpoint-name "$KEY_VAULT_PRIVATE_ENDPOINT" \
            -n "$KEY_VAULT_ZONE_GROUP" \
            --yes \
            --only-show-errors

        az network private-endpoint dns-zone-group create \
            -g "$RG_NETWORK" \
            --endpoint-name "$KEY_VAULT_PRIVATE_ENDPOINT" \
            -n "$KEY_VAULT_ZONE_GROUP" \
            --zone-name "$KEY_VAULT_ZONE_CONFIG" \
            --private-dns-zone "$KV_ZONE_ID" \
            --only-show-errors \
            -o none
        ZONE_GROUP_ACTION="recreated"
    fi

    ZONE_GROUP_STATE="$(az network private-endpoint dns-zone-group show -g "$RG_NETWORK" --endpoint-name "$KEY_VAULT_PRIVATE_ENDPOINT" -n "$KEY_VAULT_ZONE_GROUP" --query provisioningState -o tsv)"
    [ "$ZONE_GROUP_STATE" = "Succeeded" ] || fail "Key Vault DNS zone group did not succeed."

    echo "KEY_VAULT_DNS_ZONE_GROUP_ACTION=$ZONE_GROUP_ACTION"
    echo "KEY_VAULT_DNS_ZONE_GROUP_STATE=$ZONE_GROUP_STATE"

    section "Ensuring West VNet link to Key Vault private DNS"

    LINKS_JSON="$WORK_DIR/keyvault-vnet-links.json"
    az network private-dns link vnet list -g "$RG_SHARED" -z "$KEY_VAULT_PRIVATE_DNS_ZONE" -o json > "$LINKS_JSON"

    MATCHING_LINK="$(python3 - "$LINKS_JSON" "$WEST_VNET_ID" <<'PY'
import json
import sys
from pathlib import Path
links = json.loads(Path(sys.argv[1]).read_text())
wanted = sys.argv[2].lower()
matches = [str(item.get("name") or "") for item in links if str((item.get("virtualNetwork") or {}).get("id") or "").lower() == wanted]
if len(matches) > 1:
    raise SystemExit("ERROR: Multiple Key Vault private DNS links reference the West VNet.")
print(matches[0] if matches else "")
PY
)"

    if [ -n "$MATCHING_LINK" ]; then
        EFFECTIVE_LINK="$MATCHING_LINK"
        LINK_ACTION="existing"
    else
        az network private-dns link vnet create \
            -g "$RG_SHARED" \
            -z "$KEY_VAULT_PRIVATE_DNS_ZONE" \
            -n "$KEY_VAULT_LINK_NAME" \
            -v "$WEST_VNET_ID" \
            -e false \
            --only-show-errors \
            -o none
        EFFECTIVE_LINK="$KEY_VAULT_LINK_NAME"
        LINK_ACTION="created"
    fi

    LINK_STATE="$(az network private-dns link vnet show -g "$RG_SHARED" -z "$KEY_VAULT_PRIVATE_DNS_ZONE" -n "$EFFECTIVE_LINK" --query virtualNetworkLinkState -o tsv)"
    LINK_PROVISIONING="$(az network private-dns link vnet show -g "$RG_SHARED" -z "$KEY_VAULT_PRIVATE_DNS_ZONE" -n "$EFFECTIVE_LINK" --query provisioningState -o tsv)"
    [ "$LINK_STATE" = "Completed" ] || fail "Key Vault VNet link state is $LINK_STATE."
    [ "$LINK_PROVISIONING" = "Succeeded" ] || fail "Key Vault VNet link provisioning is $LINK_PROVISIONING."

    echo "KEY_VAULT_DNS_LINK_ACTION=$LINK_ACTION"
    echo "KEY_VAULT_DNS_LINK_NAME=$EFFECTIVE_LINK"
    echo "KEY_VAULT_DNS_LINK_STATE=$LINK_STATE"

    section "Repairing Key Vault private DNS A record"

    EXISTING_RECORD_IPS="$(az network private-dns record-set a show -g "$RG_SHARED" -z "$KEY_VAULT_PRIVATE_DNS_ZONE" -n "$KEY_VAULT_NAME" --query 'aRecords[].ipv4Address' -o tsv 2>/dev/null || true)"

    if ! grep -Fxq "$KV_PE_IP" <<< "$EXISTING_RECORD_IPS" || [ "$(sed '/^$/d' <<< "$EXISTING_RECORD_IPS" | wc -l | tr -d ' ')" != "1" ]; then
        az network private-dns record-set a delete \
            -g "$RG_SHARED" \
            -z "$KEY_VAULT_PRIVATE_DNS_ZONE" \
            -n "$KEY_VAULT_NAME" \
            --yes \
            --only-show-errors \
            >/dev/null 2>&1 || true

        az network private-dns record-set a create \
            -g "$RG_SHARED" \
            -z "$KEY_VAULT_PRIVATE_DNS_ZONE" \
            -n "$KEY_VAULT_NAME" \
            --ttl 10 \
            --only-show-errors \
            -o none

        az network private-dns record-set a add-record \
            -g "$RG_SHARED" \
            -z "$KEY_VAULT_PRIVATE_DNS_ZONE" \
            -n "$KEY_VAULT_NAME" \
            -a "$KV_PE_IP" \
            --only-show-errors \
            -o none
        RECORD_ACTION="repaired"
    else
        RECORD_ACTION="existing"
    fi

    VALIDATED_RECORD_IPS="$(az network private-dns record-set a show -g "$RG_SHARED" -z "$KEY_VAULT_PRIVATE_DNS_ZONE" -n "$KEY_VAULT_NAME" --query 'aRecords[].ipv4Address' -o tsv)"
    grep -Fxq "$KV_PE_IP" <<< "$VALIDATED_RECORD_IPS" || fail "Key Vault private DNS record does not contain $KV_PE_IP."

    echo "KEY_VAULT_PRIVATE_DNS_RECORD_ACTION=$RECORD_ACTION"
    echo "KEY_VAULT_PRIVATE_DNS_RECORD=$KEY_VAULT_NAME.$KEY_VAULT_PRIVATE_DNS_ZONE"
    echo "KEY_VAULT_PRIVATE_DNS_RECORD_IP=$KV_PE_IP"
    echo "KEY_VAULT_PRIVATE_DNS_REPAIR_RESULT=READY"

    echo "Waiting 90 seconds for Key Vault DNS propagation and resolver cache expiration."
    sleep 90

    section "Resolving existing app, identity, images, and secrets"

    IDENTITY_ID="$(az identity show -g "$RG_APP" -n "$WEST_IDENTITY" --query id -o tsv)"
    IDENTITY_PRINCIPAL_ID="$(az identity show -g "$RG_APP" -n "$WEST_IDENTITY" --query principalId -o tsv)"
    KEY_VAULT_ID="$(az keyvault show -g "$RG_DATA" -n "$KEY_VAULT_NAME" --query id -o tsv)"
    KV_ROLE_COUNT="$(az role assignment list --assignee "$IDENTITY_PRINCIPAL_ID" --scope "$KEY_VAULT_ID" --role "Key Vault Secrets User" --query 'length(@)' -o tsv 2>/dev/null || echo 0)"
    [ "$KV_ROLE_COUNT" != "0" ] || fail "Managed identity is missing Key Vault Secrets User."

    ACR_LOGIN_SERVER="$(az acr show -g "$RG_SHARED" -n "$ACR_NAME" --query loginServer -o tsv)"
    API_DIGEST="$(az acr repository show -n "$ACR_NAME" --image "$API_REPOSITORY:$IMAGE_TAG" --query digest -o tsv)"
    WEB_DIGEST="$(az acr repository show -n "$ACR_NAME" --image "$WEB_REPOSITORY:$IMAGE_TAG" --query digest -o tsv)"
    API_IMAGE="$ACR_LOGIN_SERVER/$API_REPOSITORY@$API_DIGEST"
    WEB_IMAGE="$ACR_LOGIN_SERVER/$WEB_REPOSITORY@$WEB_DIGEST"

    POSTGRES_FQDN="$(az postgres flexible-server show -g "$RG_DATA" -n "$POSTGRES_SERVER" --query fullyQualifiedDomainName -o tsv)"
    PASSWORD_SECRET_URI="https://${KEY_VAULT_NAME}.vault.azure.net/secrets/postgres-admin-password"
    CONNECTION_SECRET_URI="https://${KEY_VAULT_NAME}.vault.azure.net/secrets/postgres-connection-string"
    APP_INSIGHTS_CONNECTION="$(az monitor app-insights component show -g "$RG_APP" -a "$APP_INSIGHTS" --query connectionString -o tsv 2>/dev/null || true)"

    az keyvault secret show --vault-name "$KEY_VAULT_NAME" --name postgres-admin-password --query id -o tsv >/dev/null
    az keyvault secret show --vault-name "$KEY_VAULT_NAME" --name postgres-connection-string --query id -o tsv >/dev/null

    [ -n "$API_DIGEST" ] || fail "API image digest is missing."
    [ -n "$WEB_DIGEST" ] || fail "Web image digest is missing."
    [ -n "$POSTGRES_FQDN" ] || fail "PostgreSQL FQDN is missing."

    echo "KEY_VAULT_SECRETS_USER_ROLE_COUNT=$KV_ROLE_COUNT"
    echo "API_IMAGE_DIGEST=$API_DIGEST"
    echo "WEB_IMAGE_DIGEST=$WEB_DIGEST"
    echo "KEY_VAULT_SECRET_METADATA_VALIDATED=yes"

    section "Attaching Key Vault references to existing API app"

    API_STATE="$(az containerapp show -g "$RG_APP" -n "$API_APP" --query properties.provisioningState -o tsv 2>/dev/null || true)"
    [ -n "$API_STATE" ] || fail "API Container App does not exist."
    echo "API_STATE_BEFORE_SECRET_REPAIR=$API_STATE"

    az containerapp secret set \
        --resource-group "$RG_APP" \
        --name "$API_APP" \
        --secrets \
            "postgres-admin-password=keyvaultref:$PASSWORD_SECRET_URI,identityref:$IDENTITY_ID" \
            "postgres-connection-string=keyvaultref:$CONNECTION_SECRET_URI,identityref:$IDENTITY_ID" \
        --only-show-errors \
        --output none

    echo "API_KEY_VAULT_REFERENCES_ATTACHED=yes"

    az containerapp update \
        --resource-group "$RG_APP" \
        --name "$API_APP" \
        --set-env-vars \
            "PTP_DB_PASSWORD=secretref:postgres-admin-password" \
            "ConnectionStrings__DefaultConnection=secretref:postgres-connection-string" \
            "ConnectionStrings__ProjectPulse=secretref:postgres-connection-string" \
            "ConnectionStrings__ProjectTime=secretref:postgres-connection-string" \
            "PROJECTPULSE_CONNECTION_STRING=secretref:postgres-connection-string" \
            "PROJECTTIME_DATABASE_CONNECTION=secretref:postgres-connection-string" \
        --only-show-errors \
        --output none

    wait_for_provisioning "$API_APP" || fail "API database configuration did not reach Succeeded."
    wait_for_revision_health "$API_APP" || fail "API revision did not become healthy and running."

    API_FQDN="$(az containerapp show -g "$RG_APP" -n "$API_APP" --query properties.configuration.ingress.fqdn -o tsv)"
    echo "API_DATABASE_REVISION_HEALTH=Healthy"
    echo "API_CONTAINER_APP_FQDN=$API_FQDN"

    section "Deploying West web Container App"

    if az containerapp show -g "$RG_APP" -n "$WEB_APP" --output none >/dev/null 2>&1; then
        EXISTING_WEB_STATE="$(az containerapp show -g "$RG_APP" -n "$WEB_APP" --query properties.provisioningState -o tsv)"
        echo "EXISTING_WEB_STATE=$EXISTING_WEB_STATE"
        if [ "$EXISTING_WEB_STATE" != "Succeeded" ]; then
            az containerapp delete -g "$RG_APP" -n "$WEB_APP" --yes --only-show-errors
            wait_for_absence "$WEB_APP" || fail "Timed out waiting for failed web app removal."
        fi
    fi

    if ! az containerapp show -g "$RG_APP" -n "$WEB_APP" --output none >/dev/null 2>&1; then
        az containerapp create \
            --resource-group "$RG_APP" \
            --name "$WEB_APP" \
            --environment "$CONTAINERAPPS_ENVIRONMENT" \
            --image "$WEB_IMAGE" \
            --user-assigned "$IDENTITY_ID" \
            --registry-server "$ACR_LOGIN_SERVER" \
            --registry-identity "$IDENTITY_ID" \
            --ingress external \
            --target-port 8080 \
            --transport auto \
            --revisions-mode single \
            --min-replicas 1 \
            --max-replicas 2 \
            --cpu 0.5 \
            --memory 1.0Gi \
            --env-vars "API_UPSTREAM=https://$API_FQDN" \
            --tags application="Project Health Dashboard" environment=test region-role=primary source-commit="$SOURCE_COMMIT" \
            --only-show-errors \
            --output none
    fi

    wait_for_provisioning "$WEB_APP" || fail "Web Container App provisioning failed."
    wait_for_revision_health "$WEB_APP" || fail "Web revision did not become healthy and running."

    WEB_FQDN="$(az containerapp show -g "$RG_APP" -n "$WEB_APP" --query properties.configuration.ingress.fqdn -o tsv)"
    API_HEALTH="$(az containerapp revision list -g "$RG_APP" -n "$API_APP" --query "[?properties.active].properties.healthState | [0]" -o tsv)"
    WEB_HEALTH="$(az containerapp revision list -g "$RG_APP" -n "$WEB_APP" --query "[?properties.active].properties.healthState | [0]" -o tsv)"

    cat > "$STATE_FILE" <<EOF
SOURCE_COMMIT=$SOURCE_COMMIT
IMAGE_TAG=$IMAGE_TAG
API_IMAGE=$API_IMAGE
WEB_IMAGE=$WEB_IMAGE
API_CONTAINER_APP=$API_APP
API_FQDN=$API_FQDN
API_ACTIVE_REVISION_HEALTH=$API_HEALTH
WEB_CONTAINER_APP=$WEB_APP
WEB_FQDN=$WEB_FQDN
WEB_ACTIVE_REVISION_HEALTH=$WEB_HEALTH
KEY_VAULT_PRIVATE_DNS_RECORD=$KEY_VAULT_NAME.$KEY_VAULT_PRIVATE_DNS_ZONE
KEY_VAULT_PRIVATE_ENDPOINT_IP=$KV_PE_IP
WEST_APPLICATION_DEPLOYMENT_RESULT=DEPLOYED
DEPLOYED_AT=$(date -u -Is)
EOF
    chmod 600 "$STATE_FILE"

    echo "API_ACTIVE_REVISION_HEALTH=$API_HEALTH"
    echo "WEB_ACTIVE_REVISION_HEALTH=$WEB_HEALTH"
    echo "WEB_CONTAINER_APP_FQDN=$WEB_FQDN"
    echo "WEST_APPLICATION_DEPLOYMENT_RESULT=DEPLOYED"
    echo "WEST_APPLICATION_STATE_FILE=$STATE_FILE"
    echo "EAST_POSTGRESQL_REPLICA_CREATED=false"

    echo
    echo "************************************************************"
    echo "KEY VAULT DNS REPAIRED AND WEST APPLICATION DEPLOYED"
    echo "************************************************************"

} 2>&1 | tee "$LOG"

echo
echo "Deployment log: $LOG"
