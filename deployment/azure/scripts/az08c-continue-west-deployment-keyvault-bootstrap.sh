#!/usr/bin/env bash
set -Eeuo pipefail

SUBSCRIPTION_ID="cd32baeb-7b71-4bc0-8ea3-9f23a50903fe"
RG_SHARED="rg-project-health-dashboard-shared-global"
RG_APP="rg-project-health-dashboard-test-app-westus3"
RG_DATA="rg-project-health-dashboard-test-data-westus3"
RG_NETWORK="rg-project-health-dashboard-test-network-westus3"
CONTAINERAPPS_ENVIRONMENT="cae-phd-test-westus3"
WEST_IDENTITY="id-phd-test-app-westus3"
ACR_NAME="acrphdtest7825cc"
KEY_VAULT_NAME="kv-phd-t-w3-7825cc"
POSTGRES_SERVER="pg-phd-test-w3-7825cc"
POSTGRES_DATABASE="project_health_dashboard"
POSTGRES_ADMIN="phdpgadmin"
APP_INSIGHTS="appi-phd-test-westus3"
API_APP="ca-phd-test-api-westus3"
WEB_APP="ca-phd-test-web-westus3"
API_REPOSITORY="project-health-dashboard-api"
WEB_REPOSITORY="project-health-dashboard-web"
IMAGE_TAG="abf45bf82474"
SOURCE_COMMIT="abf45bf824747767282f68fa5bd50909f9751eb0"

BASE_DIR="$HOME/project-health-dashboard-azure"
CONFIG_DIR="$BASE_DIR/config"
LOG_DIR="$BASE_DIR/logs"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
LOG="$LOG_DIR/az08c-continue-west-deployment-keyvault-bootstrap-$STAMP.log"
STATE_FILE="$CONFIG_DIR/az08c-west-application-deployment.env"

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
    fail "Timed out waiting for container app removal: $app_name"
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
        if [ "$state" = "Failed" ]; then
            return 1
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

{
    section "AZ-08C - Continue West Deployment with Two-Stage Key Vault Bootstrap"
    echo "TIME=$(date -u -Is)"
    echo "REUSE_EXISTING_ACR_IMAGES=true"
    echo "ACR_IMAGE_REBUILD=false"
    echo "SOURCE_COMMIT=$SOURCE_COMMIT"
    echo "IMAGE_TAG=$IMAGE_TAG"
    echo "EAST_POSTGRESQL_REPLICA_CREATED=false"

    [ "${PHD_CONTINUE_WEST_DEPLOYMENT:-}" = "YES" ] \
        || fail "Set PHD_CONTINUE_WEST_DEPLOYMENT=YES to authorize the continuation."

    command -v az >/dev/null 2>&1 || fail "Azure CLI is required."

    az account set --subscription "$SUBSCRIPTION_ID"
    CURRENT_SUBSCRIPTION="$(az account show --query id -o tsv)"
    [ "$CURRENT_SUBSCRIPTION" = "$SUBSCRIPTION_ID" ] || fail "Active subscription does not match."
    echo "CURRENT_SUBSCRIPTION_MATCH=yes"

    az config set extension.use_dynamic_install=yes_without_prompt >/dev/null
    az extension add --name containerapp --upgrade --only-show-errors --output none
    echo "CONTAINERAPP_EXTENSION_VERSION=$(az extension show --name containerapp --query version -o tsv)"

    section "Resolving existing images and identity"

    ACR_LOGIN_SERVER="$(az acr show -g "$RG_SHARED" -n "$ACR_NAME" --query loginServer -o tsv)"
    API_DIGEST="$(az acr repository show -n "$ACR_NAME" --image "$API_REPOSITORY:$IMAGE_TAG" --query digest -o tsv)"
    WEB_DIGEST="$(az acr repository show -n "$ACR_NAME" --image "$WEB_REPOSITORY:$IMAGE_TAG" --query digest -o tsv)"
    [ -n "$API_DIGEST" ] || fail "Existing API image digest was not found."
    [ -n "$WEB_DIGEST" ] || fail "Existing web image digest was not found."

    API_IMAGE="$ACR_LOGIN_SERVER/$API_REPOSITORY@$API_DIGEST"
    WEB_IMAGE="$ACR_LOGIN_SERVER/$WEB_REPOSITORY@$WEB_DIGEST"

    IDENTITY_ID="$(az identity show -g "$RG_APP" -n "$WEST_IDENTITY" --query id -o tsv)"
    IDENTITY_PRINCIPAL_ID="$(az identity show -g "$RG_APP" -n "$WEST_IDENTITY" --query principalId -o tsv)"
    KEY_VAULT_ID="$(az keyvault show -g "$RG_DATA" -n "$KEY_VAULT_NAME" --query id -o tsv)"
    ACR_ID="$(az acr show -g "$RG_SHARED" -n "$ACR_NAME" --query id -o tsv)"

    echo "API_IMAGE_DIGEST=$API_DIGEST"
    echo "WEB_IMAGE_DIGEST=$WEB_DIGEST"
    echo "WEST_IDENTITY_ID=$IDENTITY_ID"

    ACR_ROLE_COUNT="$(az role assignment list --assignee "$IDENTITY_PRINCIPAL_ID" --scope "$ACR_ID" --role AcrPull --query 'length(@)' -o tsv 2>/dev/null || echo 0)"
    KV_ROLE_COUNT="$(az role assignment list --assignee "$IDENTITY_PRINCIPAL_ID" --scope "$KEY_VAULT_ID" --role "Key Vault Secrets User" --query 'length(@)' -o tsv 2>/dev/null || echo 0)"

    echo "ACR_PULL_ROLE_COUNT=$ACR_ROLE_COUNT"
    echo "KEY_VAULT_SECRETS_USER_ROLE_COUNT=$KV_ROLE_COUNT"
    [ "$ACR_ROLE_COUNT" != "0" ] || fail "Managed identity is missing AcrPull."
    [ "$KV_ROLE_COUNT" != "0" ] || fail "Managed identity is missing Key Vault Secrets User."

    section "Key Vault diagnostics"

    KV_RBAC="$(az keyvault show -g "$RG_DATA" -n "$KEY_VAULT_NAME" --query properties.enableRbacAuthorization -o tsv)"
    KV_PUBLIC="$(az keyvault show -g "$RG_DATA" -n "$KEY_VAULT_NAME" --query properties.publicNetworkAccess -o tsv)"
    KV_DEFAULT_ACTION="$(az keyvault show -g "$RG_DATA" -n "$KEY_VAULT_NAME" --query properties.networkAcls.defaultAction -o tsv)"
    KV_BYPASS="$(az keyvault show -g "$RG_DATA" -n "$KEY_VAULT_NAME" --query properties.networkAcls.bypass -o tsv)"

    echo "KEY_VAULT_RBAC_ENABLED=$KV_RBAC"
    echo "KEY_VAULT_PUBLIC_NETWORK_ACCESS=$KV_PUBLIC"
    echo "KEY_VAULT_DEFAULT_ACTION=$KV_DEFAULT_ACTION"
    echo "KEY_VAULT_BYPASS=$KV_BYPASS"

    az network private-endpoint list -g "$RG_NETWORK" \
        --query "[?contains(name, 'kv')].{Name:name,State:provisioningState,Connection:privateLinkServiceConnections[0].privateLinkServiceConnectionState.status}" \
        --output table || true

    POSTGRES_FQDN="$(az postgres flexible-server show -g "$RG_DATA" -n "$POSTGRES_SERVER" --query fullyQualifiedDomainName -o tsv)"
    PASSWORD_SECRET_URI="https://${KEY_VAULT_NAME}.vault.azure.net/secrets/postgres-admin-password"
    CONNECTION_SECRET_URI="https://${KEY_VAULT_NAME}.vault.azure.net/secrets/postgres-connection-string"
    APP_INSIGHTS_CONNECTION="$(az monitor app-insights component show -g "$RG_APP" -a "$APP_INSIGHTS" --query connectionString -o tsv 2>/dev/null || true)"

    [ -n "$POSTGRES_FQDN" ] || fail "PostgreSQL FQDN was not resolved."
    az keyvault secret show --vault-name "$KEY_VAULT_NAME" --name postgres-admin-password --query id -o tsv >/dev/null
    az keyvault secret show --vault-name "$KEY_VAULT_NAME" --name postgres-connection-string --query id -o tsv >/dev/null
    echo "KEY_VAULT_SECRET_METADATA_VALIDATED=yes"

    section "Removing failed API shell when present"

    if az containerapp show -g "$RG_APP" -n "$API_APP" --output none >/dev/null 2>&1; then
        EXISTING_API_STATE="$(az containerapp show -g "$RG_APP" -n "$API_APP" --query properties.provisioningState -o tsv)"
        echo "EXISTING_API_STATE=$EXISTING_API_STATE"
        if [ "$EXISTING_API_STATE" != "Succeeded" ]; then
            az containerapp delete -g "$RG_APP" -n "$API_APP" --yes --only-show-errors
            wait_for_absence "$API_APP"
        fi
    fi

    section "Stage 1 - Create API app with managed identity"

    if ! az containerapp show -g "$RG_APP" -n "$API_APP" --output none >/dev/null 2>&1; then
        API_BASE_ENV=(
            "ASPNETCORE_HTTP_PORTS=5080"
            "PTP_DB_HOST=$POSTGRES_FQDN"
            "PTP_DB_PORT=5432"
            "PTP_DB_NAME=$POSTGRES_DATABASE"
            "PTP_DB_USER=$POSTGRES_ADMIN"
            "PROJECTPULSE_DATA_DIR=/tmp/project-health-dashboard/data"
            "PROJECT_PULSE_UPLOAD_ROOT=/tmp/project-health-dashboard/uploads"
        )
        if [ -n "$APP_INSIGHTS_CONNECTION" ]; then
            API_BASE_ENV+=("APPLICATIONINSIGHTS_CONNECTION_STRING=$APP_INSIGHTS_CONNECTION")
        fi

        az containerapp create \
            --resource-group "$RG_APP" \
            --name "$API_APP" \
            --environment "$CONTAINERAPPS_ENVIRONMENT" \
            --image "$API_IMAGE" \
            --user-assigned "$IDENTITY_ID" \
            --registry-server "$ACR_LOGIN_SERVER" \
            --registry-identity "$IDENTITY_ID" \
            --ingress internal \
            --target-port 5080 \
            --transport auto \
            --revisions-mode single \
            --min-replicas 1 \
            --max-replicas 2 \
            --cpu 0.5 \
            --memory 1.0Gi \
            --env-vars "${API_BASE_ENV[@]}" \
            --tags application="Project Health Dashboard" environment=test region-role=primary source-commit="$SOURCE_COMMIT" \
            --only-show-errors \
            --output none
    fi

    wait_for_provisioning "$API_APP" || fail "API identity bootstrap provisioning failed."
    API_FQDN="$(az containerapp show -g "$RG_APP" -n "$API_APP" --query properties.configuration.ingress.fqdn -o tsv)"
    echo "API_IDENTITY_BOOTSTRAP_STATE=Succeeded"
    echo "API_CONTAINER_APP_FQDN=$API_FQDN"

    echo "Waiting 60 seconds for managed identity binding propagation."
    sleep 60

    section "Stage 2 - Attach Key Vault references and database configuration"

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

    wait_for_provisioning "$API_APP" || fail "API database configuration revision failed."
    wait_for_revision_health "$API_APP" || fail "API revision did not become healthy and running."
    echo "API_DATABASE_REVISION_HEALTH=Healthy"

    section "Deploying West web Container App"

    if az containerapp show -g "$RG_APP" -n "$WEB_APP" --output none >/dev/null 2>&1; then
        EXISTING_WEB_STATE="$(az containerapp show -g "$RG_APP" -n "$WEB_APP" --query properties.provisioningState -o tsv)"
        echo "EXISTING_WEB_STATE=$EXISTING_WEB_STATE"
        if [ "$EXISTING_WEB_STATE" != "Succeeded" ]; then
            az containerapp delete -g "$RG_APP" -n "$WEB_APP" --yes --only-show-errors
            wait_for_absence "$WEB_APP"
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
    echo "WEST APPLICATION DEPLOYMENT CONTINUATION COMPLETE"
    echo "************************************************************"

} 2>&1 | tee "$LOG"

echo
echo "Continuation log: $LOG"
