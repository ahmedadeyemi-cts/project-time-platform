#!/usr/bin/env bash
set -Eeuo pipefail

SUBSCRIPTION_ID="cd32baeb-7b71-4bc0-8ea3-9f23a50903fe"
SOURCE_REPOSITORY="ahmedadeyemi-cts/project-time-platform"
SOURCE_BRANCH="source/work-register-billing-lifecycle-20260712"
EXPECTED_SOURCE_COMMIT="abf45bf824747767282f68fa5bd50909f9751eb0"
IMAGE_TAG="${EXPECTED_SOURCE_COMMIT:0:12}"

RG_SHARED="rg-project-health-dashboard-shared-global"
RG_APP="rg-project-health-dashboard-test-app-westus3"
RG_DATA="rg-project-health-dashboard-test-data-westus3"
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

BASE_DIR="$HOME/project-health-dashboard-azure"
CONFIG_DIR="$BASE_DIR/config"
LOG_DIR="$BASE_DIR/logs"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
LOG="$LOG_DIR/az08b-build-and-deploy-west-application-$STAMP.log"
STATE_FILE="$CONFIG_DIR/az08b-west-application-deployment.env"
WORK_DIR="$(mktemp -d /tmp/phd-az08b-XXXXXX)"
SOURCE_DIR="$WORK_DIR/source"

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

ensure_role() {
    local principal_id="$1"
    local role_name="$2"
    local scope="$3"
    local count

    count="$(az role assignment list \
        --assignee "$principal_id" \
        --scope "$scope" \
        --role "$role_name" \
        --query 'length(@)' \
        --output tsv 2>/dev/null || echo 0)"

    if [ "$count" != "0" ]; then
        echo "ROLE[$role_name]=existing"
        return 0
    fi

    az role assignment create \
        --assignee-object-id "$principal_id" \
        --assignee-principal-type ServicePrincipal \
        --role "$role_name" \
        --scope "$scope" \
        --only-show-errors \
        --output none

    echo "ROLE[$role_name]=created"
}

{
    section "AZ-08B - Build and Deploy West Application"
    echo "TIME=$(date -u -Is)"
    echo "SOURCE_BRANCH=$SOURCE_BRANCH"
    echo "EXPECTED_SOURCE_COMMIT=$EXPECTED_SOURCE_COMMIT"
    echo "IMAGE_TAG=$IMAGE_TAG"
    echo "BILLABLE_ACR_BUILD=true"
    echo "BILLABLE_CONTAINER_APPS_DEPLOYMENT=true"
    echo "EAST_POSTGRESQL_REPLICA_CREATED=false"

    [ "${PHD_BUILD_AND_DEPLOY_WEST_APPLICATION:-}" = "YES" ] \
        || fail "Set PHD_BUILD_AND_DEPLOY_WEST_APPLICATION=YES to authorize the build and deployment."

    command -v az >/dev/null 2>&1 || fail "Azure CLI is required."
    command -v gh >/dev/null 2>&1 || fail "GitHub CLI is required."
    command -v git >/dev/null 2>&1 || fail "Git is required."

    az account set --subscription "$SUBSCRIPTION_ID"
    CURRENT_SUBSCRIPTION="$(az account show --query id --output tsv)"
    echo "CURRENT_SUBSCRIPTION_MATCH=$([ "$CURRENT_SUBSCRIPTION" = "$SUBSCRIPTION_ID" ] && echo yes || echo no)"
    [ "$CURRENT_SUBSCRIPTION" = "$SUBSCRIPTION_ID" ] || fail "The active Azure subscription does not match."

    az config set extension.use_dynamic_install=yes_without_prompt >/dev/null
    az extension add --name containerapp --upgrade --only-show-errors --output none
    echo "CONTAINERAPP_EXTENSION_VERSION=$(az extension show --name containerapp --query version --output tsv)"

    section "Validating West foundation"

    ENV_STATE="$(az containerapp env show -g "$RG_APP" -n "$CONTAINERAPPS_ENVIRONMENT" --query properties.provisioningState -o tsv)"
    ACR_STATE="$(az acr show -g "$RG_SHARED" -n "$ACR_NAME" --query provisioningState -o tsv)"
    PG_STATE="$(az postgres flexible-server show -g "$RG_DATA" -n "$POSTGRES_SERVER" --query state -o tsv)"

    echo "CONTAINERAPPS_ENVIRONMENT_STATE=$ENV_STATE"
    echo "ACR_STATE=$ACR_STATE"
    echo "POSTGRES_STATE=$PG_STATE"

    [ "$ENV_STATE" = "Succeeded" ] || fail "West Container Apps environment is not ready."
    [ "$ACR_STATE" = "Succeeded" ] || fail "ACR is not ready."
    [ "$PG_STATE" = "Ready" ] || fail "West PostgreSQL primary is not ready."

    if az containerapp show -g "$RG_APP" -n "$API_APP" --output none >/dev/null 2>&1; then
        fail "Container app already exists: $API_APP. This one-time script will not overwrite it."
    fi

    if az containerapp show -g "$RG_APP" -n "$WEB_APP" --output none >/dev/null 2>&1; then
        fail "Container app already exists: $WEB_APP. This one-time script will not overwrite it."
    fi

    section "Retrieving exact versioned source"

    gh auth status >/dev/null 2>&1 || fail "GitHub CLI is not authenticated."
    gh repo clone "$SOURCE_REPOSITORY" "$SOURCE_DIR" -- \
        --branch "$SOURCE_BRANCH" \
        --single-branch \
        --depth 1

    SOURCE_HEAD="$(git -C "$SOURCE_DIR" rev-parse HEAD)"
    SOURCE_STATUS_COUNT="$(git -C "$SOURCE_DIR" status --short --untracked-files=all | sed '/^$/d' | wc -l | tr -d ' ')"

    echo "SOURCE_HEAD=$SOURCE_HEAD"
    echo "SOURCE_HEAD_MATCH=$([ "$SOURCE_HEAD" = "$EXPECTED_SOURCE_COMMIT" ] && echo yes || echo no)"
    echo "SOURCE_STATUS_ENTRY_COUNT=$SOURCE_STATUS_COUNT"

    [ "$SOURCE_HEAD" = "$EXPECTED_SOURCE_COMMIT" ] || fail "Source branch head changed after deployment preparation."
    [ "$SOURCE_STATUS_COUNT" = "0" ] || fail "Cloned source worktree is not clean."
    [ -f "$SOURCE_DIR/deployment/containers/api/Dockerfile" ] || fail "API Dockerfile is missing."
    [ -f "$SOURCE_DIR/deployment/containers/web/Dockerfile" ] || fail "Web Dockerfile is missing."
    [ -f "$SOURCE_DIR/deployment/containers/web/default.conf.template" ] || fail "Web proxy template is missing."

    section "Building API image in ACR"

    az acr build \
        --registry "$ACR_NAME" \
        --file deployment/containers/api/Dockerfile \
        --image "$API_REPOSITORY:$IMAGE_TAG" \
        --image "$API_REPOSITORY:test-latest" \
        --only-show-errors \
        "$SOURCE_DIR"

    echo "API_ACR_BUILD=passed"

    section "Building web image in ACR"

    az acr build \
        --registry "$ACR_NAME" \
        --file deployment/containers/web/Dockerfile \
        --image "$WEB_REPOSITORY:$IMAGE_TAG" \
        --image "$WEB_REPOSITORY:test-latest" \
        --only-show-errors \
        "$SOURCE_DIR"

    echo "WEB_ACR_BUILD=passed"

    ACR_LOGIN_SERVER="$(az acr show -g "$RG_SHARED" -n "$ACR_NAME" --query loginServer -o tsv)"
    API_DIGEST="$(az acr repository show -n "$ACR_NAME" --image "$API_REPOSITORY:$IMAGE_TAG" --query digest -o tsv)"
    WEB_DIGEST="$(az acr repository show -n "$ACR_NAME" --image "$WEB_REPOSITORY:$IMAGE_TAG" --query digest -o tsv)"

    [ -n "$API_DIGEST" ] || fail "API image digest could not be resolved."
    [ -n "$WEB_DIGEST" ] || fail "Web image digest could not be resolved."

    API_IMAGE="$ACR_LOGIN_SERVER/$API_REPOSITORY@$API_DIGEST"
    WEB_IMAGE="$ACR_LOGIN_SERVER/$WEB_REPOSITORY@$WEB_DIGEST"

    echo "ACR_LOGIN_SERVER=$ACR_LOGIN_SERVER"
    echo "API_IMAGE_DIGEST=$API_DIGEST"
    echo "WEB_IMAGE_DIGEST=$WEB_DIGEST"

    section "Preparing identity and database secrets"

    IDENTITY_ID="$(az identity show -g "$RG_APP" -n "$WEST_IDENTITY" --query id -o tsv)"
    IDENTITY_PRINCIPAL_ID="$(az identity show -g "$RG_APP" -n "$WEST_IDENTITY" --query principalId -o tsv)"
    ACR_ID="$(az acr show -g "$RG_SHARED" -n "$ACR_NAME" --query id -o tsv)"
    KEY_VAULT_ID="$(az keyvault show -g "$RG_DATA" -n "$KEY_VAULT_NAME" --query id -o tsv)"

    ensure_role "$IDENTITY_PRINCIPAL_ID" AcrPull "$ACR_ID"
    ensure_role "$IDENTITY_PRINCIPAL_ID" "Key Vault Secrets User" "$KEY_VAULT_ID"

    POSTGRES_FQDN="$(az postgres flexible-server show -g "$RG_DATA" -n "$POSTGRES_SERVER" --query fullyQualifiedDomainName -o tsv)"
    [ -n "$POSTGRES_FQDN" ] || fail "PostgreSQL FQDN could not be resolved."

    POSTGRES_PASSWORD="$(az keyvault secret show --vault-name "$KEY_VAULT_NAME" --name postgres-admin-password --query value -o tsv)"
    [ -n "$POSTGRES_PASSWORD" ] || fail "PostgreSQL administrator password secret is empty."

    POSTGRES_CONNECTION_STRING="Host=$POSTGRES_FQDN;Port=5432;Database=$POSTGRES_DATABASE;Username=$POSTGRES_ADMIN;Password=$POSTGRES_PASSWORD;SSL Mode=VerifyFull;Timeout=15;Command Timeout=60;"

    az keyvault secret set \
        --vault-name "$KEY_VAULT_NAME" \
        --name postgres-connection-string \
        --value "$POSTGRES_CONNECTION_STRING" \
        --only-show-errors \
        --output none

    unset POSTGRES_PASSWORD POSTGRES_CONNECTION_STRING

    PASSWORD_SECRET_URI="https://${KEY_VAULT_NAME}.vault.azure.net/secrets/postgres-admin-password"
    CONNECTION_SECRET_URI="https://${KEY_VAULT_NAME}.vault.azure.net/secrets/postgres-connection-string"
    APP_INSIGHTS_CONNECTION="$(az monitor app-insights component show -g "$RG_APP" -a "$APP_INSIGHTS" --query connectionString -o tsv 2>/dev/null || true)"

    echo "POSTGRES_FQDN=$POSTGRES_FQDN"
    echo "POSTGRES_CONNECTION_SECRET_READY=yes"
    echo "APP_INSIGHTS_CONNECTION_AVAILABLE=$([ -n "$APP_INSIGHTS_CONNECTION" ] && echo yes || echo no)"

    section "Deploying internal API Container App"

    API_ENV_VARS=(
        "ASPNETCORE_HTTP_PORTS=5080"
        "PTP_DB_HOST=$POSTGRES_FQDN"
        "PTP_DB_PORT=5432"
        "PTP_DB_NAME=$POSTGRES_DATABASE"
        "PTP_DB_USER=$POSTGRES_ADMIN"
        "PTP_DB_PASSWORD=secretref:postgres-admin-password"
        "ConnectionStrings__DefaultConnection=secretref:postgres-connection-string"
        "ConnectionStrings__ProjectPulse=secretref:postgres-connection-string"
        "ConnectionStrings__ProjectTime=secretref:postgres-connection-string"
        "PROJECTPULSE_CONNECTION_STRING=secretref:postgres-connection-string"
        "PROJECTTIME_DATABASE_CONNECTION=secretref:postgres-connection-string"
        "PROJECTPULSE_DATA_DIR=/tmp/project-health-dashboard/data"
        "PROJECT_PULSE_UPLOAD_ROOT=/tmp/project-health-dashboard/uploads"
    )

    if [ -n "$APP_INSIGHTS_CONNECTION" ]; then
        API_ENV_VARS+=("APPLICATIONINSIGHTS_CONNECTION_STRING=$APP_INSIGHTS_CONNECTION")
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
        --secrets \
            "postgres-admin-password=keyvaultref:$PASSWORD_SECRET_URI,identityref:$IDENTITY_ID" \
            "postgres-connection-string=keyvaultref:$CONNECTION_SECRET_URI,identityref:$IDENTITY_ID" \
        --env-vars "${API_ENV_VARS[@]}" \
        --tags \
            application="Project Health Dashboard" \
            environment=test \
            region-role=primary \
            source-commit="$EXPECTED_SOURCE_COMMIT" \
        --only-show-errors \
        --output none

    API_PROVISIONING_STATE="$(az containerapp show -g "$RG_APP" -n "$API_APP" --query properties.provisioningState -o tsv)"
    API_FQDN="$(az containerapp show -g "$RG_APP" -n "$API_APP" --query properties.configuration.ingress.fqdn -o tsv)"

    echo "API_CONTAINER_APP_STATE=$API_PROVISIONING_STATE"
    echo "API_CONTAINER_APP_FQDN=$API_FQDN"

    [ "$API_PROVISIONING_STATE" = "Succeeded" ] || fail "API Container App provisioning did not succeed."
    [ -n "$API_FQDN" ] || fail "API Container App FQDN is empty."

    section "Deploying West web Container App"

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
        --tags \
            application="Project Health Dashboard" \
            environment=test \
            region-role=primary \
            source-commit="$EXPECTED_SOURCE_COMMIT" \
        --only-show-errors \
        --output none

    WEB_PROVISIONING_STATE="$(az containerapp show -g "$RG_APP" -n "$WEB_APP" --query properties.provisioningState -o tsv)"
    WEB_FQDN="$(az containerapp show -g "$RG_APP" -n "$WEB_APP" --query properties.configuration.ingress.fqdn -o tsv)"

    echo "WEB_CONTAINER_APP_STATE=$WEB_PROVISIONING_STATE"
    echo "WEB_CONTAINER_APP_FQDN=$WEB_FQDN"

    [ "$WEB_PROVISIONING_STATE" = "Succeeded" ] || fail "Web Container App provisioning did not succeed."
    [ -n "$WEB_FQDN" ] || fail "Web Container App FQDN is empty."

    section "Revision health summary"

    az containerapp revision list -g "$RG_APP" -n "$API_APP" \
        --query "[].{Revision:name,Active:properties.active,Health:properties.healthState,Running:properties.runningState,Replicas:properties.replicas}" \
        --output table

    az containerapp revision list -g "$RG_APP" -n "$WEB_APP" \
        --query "[].{Revision:name,Active:properties.active,Health:properties.healthState,Running:properties.runningState,Replicas:properties.replicas}" \
        --output table

    API_HEALTH_STATE="$(az containerapp revision list -g "$RG_APP" -n "$API_APP" --query "[?properties.active].properties.healthState | [0]" -o tsv)"
    WEB_HEALTH_STATE="$(az containerapp revision list -g "$RG_APP" -n "$WEB_APP" --query "[?properties.active].properties.healthState | [0]" -o tsv)"

    echo "API_ACTIVE_REVISION_HEALTH=${API_HEALTH_STATE:-unknown}"
    echo "WEB_ACTIVE_REVISION_HEALTH=${WEB_HEALTH_STATE:-unknown}"

    cat > "$STATE_FILE" <<EOF
SOURCE_BRANCH=$SOURCE_BRANCH
SOURCE_COMMIT=$EXPECTED_SOURCE_COMMIT
IMAGE_TAG=$IMAGE_TAG
API_IMAGE=$API_IMAGE
WEB_IMAGE=$WEB_IMAGE
API_CONTAINER_APP=$API_APP
API_FQDN=$API_FQDN
API_PROVISIONING_STATE=$API_PROVISIONING_STATE
API_ACTIVE_REVISION_HEALTH=${API_HEALTH_STATE:-unknown}
WEB_CONTAINER_APP=$WEB_APP
WEB_FQDN=$WEB_FQDN
WEB_PROVISIONING_STATE=$WEB_PROVISIONING_STATE
WEB_ACTIVE_REVISION_HEALTH=${WEB_HEALTH_STATE:-unknown}
DEPLOYED_AT=$(date -u -Is)
EOF
    chmod 600 "$STATE_FILE"

    echo "WEST_APPLICATION_DEPLOYMENT_RESULT=DEPLOYED"
    echo "WEST_APPLICATION_STATE_FILE=$STATE_FILE"
    echo "EAST_POSTGRESQL_REPLICA_CREATED=false"

    echo
    echo "************************************************************"
    echo "WEST APPLICATION IMAGES BUILT AND CONTAINER APPS DEPLOYED"
    echo "************************************************************"

} 2>&1 | tee "$LOG"

echo
echo "Deployment log: $LOG"
