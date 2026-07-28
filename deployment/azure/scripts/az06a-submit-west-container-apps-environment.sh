#!/usr/bin/env bash
set -Eeuo pipefail

SUBSCRIPTION_ID="cd32baeb-7b71-4bc0-8ea3-9f23a50903fe"
LOCATION="westus3"
RG_APP="rg-project-health-dashboard-test-app-westus3"
RG_NETWORK="rg-project-health-dashboard-test-network-westus3"
RG_DATA="rg-project-health-dashboard-test-data-westus3"
RG_SHARED="rg-project-health-dashboard-shared-global"
VNET_NAME="vnet-phd-test-westus3"
SUBNET_NAME="snet-aca-infrastructure"
ENVIRONMENT_NAME="cae-phd-test-westus3"
INFRASTRUCTURE_RG="rg-project-health-dashboard-test-aca-infra-westus3"
LOG_WORKSPACE="log-phd-test-westus3"
APP_IDENTITY="id-phd-test-app-westus3"
ACR_NAME="acrphdtest7825cc"
KEY_VAULT="kv-phd-t-w3-7825cc"
POSTGRES_SERVER="pg-phd-test-w3-7825cc"

BASE_DIR="$HOME/project-health-dashboard-azure"
CONFIG_DIR="$BASE_DIR/config"
LOG_DIR="$BASE_DIR/logs"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
LOG="$LOG_DIR/az06a-submit-west-container-apps-environment-$STAMP.log"
STATE_FILE="$CONFIG_DIR/az06a-west-container-apps-environment.env"
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

provider_ready() {
    local namespace="$1"
    local state

    state="$(az provider show --namespace "$namespace" --query registrationState -o tsv 2>/dev/null || true)"

    if [ "$state" != "Registered" ]; then
        echo "Registering provider: $namespace"
        az provider register --namespace "$namespace" --wait --only-show-errors
        state="$(az provider show --namespace "$namespace" --query registrationState -o tsv)"
    fi

    [ "$state" = "Registered" ] || fail "Provider is not Registered: $namespace ($state)"
    echo "PROVIDER_${namespace//./_}=Registered"
}

{
    section "AZ-06A - Submit West Container Apps Environment"
    echo "TIME=$(date -u -Is)"
    echo "SUBSCRIPTION_ID=$SUBSCRIPTION_ID"
    echo "LOCATION=$LOCATION"
    echo "ENVIRONMENT_NAME=$ENVIRONMENT_NAME"
    echo "INTERNAL_ENVIRONMENT=true"
    echo "WORKLOAD_PROFILES_ENABLED=true"
    echo "BILLABLE_AZURE_DEPLOYMENT=true"
    echo "APPLICATION_IMAGES_DEPLOYED=false"
    echo "EAST_POSTGRESQL_REPLICA_CREATED=false"

    [ "${PHD_CREATE_BILLABLE_WEST_ENVIRONMENT:-}" = "YES" ] \
        || fail "Set PHD_CREATE_BILLABLE_WEST_ENVIRONMENT=YES only when ready to create the West Container Apps environment."

    az account set --subscription "$SUBSCRIPTION_ID"

    CURRENT_SUBSCRIPTION_ID="$(az account show --query id -o tsv)"
    [ "$CURRENT_SUBSCRIPTION_ID" = "$SUBSCRIPTION_ID" ] \
        || fail "Current Azure subscription does not match the intended subscription."

    echo "CURRENT_SUBSCRIPTION_MATCH=yes"

    section "Preparing Azure Container Apps CLI"

    az config set extension.use_dynamic_install=yes_without_prompt >/dev/null
    az extension add --name containerapp --upgrade --only-show-errors >/dev/null
    CONTAINERAPP_EXTENSION_VERSION="$(az extension show --name containerapp --query version -o tsv)"
    echo "CONTAINERAPP_EXTENSION_VERSION=$CONTAINERAPP_EXTENSION_VERSION"

    provider_ready Microsoft.App
    provider_ready Microsoft.OperationalInsights
    provider_ready Microsoft.ContainerService

    section "Validating West foundation"

    az group show --name "$RG_APP" --output none
    az group show --name "$RG_NETWORK" --output none
    az group show --name "$RG_DATA" --output none
    az group show --name "$RG_SHARED" --output none

    SUBNET_JSON="$(az network vnet subnet show \
        --resource-group "$RG_NETWORK" \
        --vnet-name "$VNET_NAME" \
        --name "$SUBNET_NAME" \
        --output json)"

    SUBNET_ID="$(python3 - "$SUBNET_JSON" <<'PY'
import json
import sys
obj = json.loads(sys.argv[1])
print(obj.get("id") or "")
PY
)"

    SUBNET_PREFIX="$(python3 - "$SUBNET_JSON" <<'PY'
import json
import sys
obj = json.loads(sys.argv[1])
prefixes = obj.get("addressPrefixes") or []
print(prefixes[0] if prefixes else obj.get("addressPrefix") or "")
PY
)"

    SUBNET_DELEGATION_COUNT="$(python3 - "$SUBNET_JSON" <<'PY'
import json
import sys
obj = json.loads(sys.argv[1])
print(sum(1 for item in (obj.get("delegations") or []) if str((item.get("serviceName") or "")).lower() == "microsoft.app/environments"))
PY
)"

    NAT_GATEWAY_ID="$(python3 - "$SUBNET_JSON" <<'PY'
import json
import sys
obj = json.loads(sys.argv[1])
print(((obj.get("natGateway") or {}).get("id")) or "")
PY
)"

    [ -n "$SUBNET_ID" ] || fail "Container Apps infrastructure subnet ID is empty."
    [ "$SUBNET_PREFIX" = "10.30.0.0/23" ] || fail "Unexpected Container Apps subnet prefix: $SUBNET_PREFIX"
    [ "$SUBNET_DELEGATION_COUNT" = "1" ] || fail "Container Apps subnet delegation is missing or duplicated."
    [ -n "$NAT_GATEWAY_ID" ] || fail "Container Apps subnet has no NAT Gateway association."

    WORKSPACE_ID="$(az monitor log-analytics workspace show \
        --resource-group "$RG_APP" \
        --workspace-name "$LOG_WORKSPACE" \
        --query customerId \
        --output tsv)"

    WORKSPACE_RESOURCE_ID="$(az monitor log-analytics workspace show \
        --resource-group "$RG_APP" \
        --workspace-name "$LOG_WORKSPACE" \
        --query id \
        --output tsv)"

    WORKSPACE_KEY="$(az monitor log-analytics workspace get-shared-keys \
        --resource-group "$RG_APP" \
        --workspace-name "$LOG_WORKSPACE" \
        --query primarySharedKey \
        --output tsv)"

    [ -n "$WORKSPACE_ID" ] || fail "Log Analytics customer ID is empty."
    [ -n "$WORKSPACE_RESOURCE_ID" ] || fail "Log Analytics resource ID is empty."
    [ -n "$WORKSPACE_KEY" ] || fail "Log Analytics shared key retrieval failed."

    IDENTITY_ID="$(az identity show \
        --resource-group "$RG_APP" \
        --name "$APP_IDENTITY" \
        --query id \
        --output tsv)"

    ACR_STATE="$(az acr show \
        --resource-group "$RG_SHARED" \
        --name "$ACR_NAME" \
        --query provisioningState \
        --output tsv)"

    KEY_VAULT_STATE="$(az keyvault show \
        --resource-group "$RG_DATA" \
        --name "$KEY_VAULT" \
        --query properties.provisioningState \
        --output tsv)"

    POSTGRES_STATE="$(az postgres flexible-server show \
        --resource-group "$RG_DATA" \
        --name "$POSTGRES_SERVER" \
        --query state \
        --output tsv)"

    [ -n "$IDENTITY_ID" ] || fail "West application managed identity is missing."
    [ "$ACR_STATE" = "Succeeded" ] || fail "ACR is not ready: $ACR_STATE"
    [ "$KEY_VAULT_STATE" = "Succeeded" ] || fail "Key Vault is not ready: $KEY_VAULT_STATE"
    [ "$POSTGRES_STATE" = "Ready" ] || fail "PostgreSQL primary is not Ready: $POSTGRES_STATE"

    echo "SUBNET_PREFIX=$SUBNET_PREFIX"
    echo "SUBNET_DELEGATION=microsoft.app/environments"
    echo "SUBNET_NAT_GATEWAY_ATTACHED=yes"
    echo "LOG_ANALYTICS_WORKSPACE_READY=yes"
    echo "WEST_APP_IDENTITY_READY=yes"
    echo "ACR_STATE=$ACR_STATE"
    echo "KEY_VAULT_STATE=$KEY_VAULT_STATE"
    echo "POSTGRES_STATE=$POSTGRES_STATE"

    CREATE_HELP="$(az containerapp env create --help 2>/dev/null || true)"

    for required_flag in \
        --infrastructure-subnet-resource-id \
        --internal-only \
        --enable-workload-profiles \
        --logs-workspace-id \
        --logs-workspace-key \
        --no-wait; do
        grep -q -- "$required_flag" <<<"$CREATE_HELP" \
            || fail "Installed Container Apps CLI does not advertise required flag: $required_flag"
    done

    section "Checking existing environment"

    if az containerapp env show \
        --resource-group "$RG_APP" \
        --name "$ENVIRONMENT_NAME" \
        --output none >/dev/null 2>&1; then

        EXISTING_JSON="$(az containerapp env show \
            --resource-group "$RG_APP" \
            --name "$ENVIRONMENT_NAME" \
            --output json)"

        EXISTING_STATE="$(python3 - "$EXISTING_JSON" <<'PY'
import json
import sys
obj = json.loads(sys.argv[1])
print(obj.get("provisioningState") or (obj.get("properties") or {}).get("provisioningState") or "")
PY
)"

        EXISTING_SUBNET="$(python3 - "$EXISTING_JSON" <<'PY'
import json
import sys
obj = json.loads(sys.argv[1])
props = obj.get("properties") or obj
print(((props.get("vnetConfiguration") or {}).get("infrastructureSubnetId")) or "")
PY
)"

        [ "${EXISTING_SUBNET,,}" = "${SUBNET_ID,,}" ] \
            || fail "Existing Container Apps environment uses an unexpected subnet."

        cat > "$STATE_FILE" <<EOF
AZURE_SUBSCRIPTION_ID=$SUBSCRIPTION_ID
WEST_CONTAINER_APPS_ENVIRONMENT=$ENVIRONMENT_NAME
WEST_CONTAINER_APPS_ENVIRONMENT_RESOURCE_GROUP=$RG_APP
WEST_CONTAINER_APPS_ENVIRONMENT_LOCATION=$LOCATION
WEST_CONTAINER_APPS_INFRASTRUCTURE_RESOURCE_GROUP=$INFRASTRUCTURE_RG
WEST_CONTAINER_APPS_SUBNET_ID=$SUBNET_ID
WEST_CONTAINER_APPS_LOG_WORKSPACE_ID=$WORKSPACE_RESOURCE_ID
WEST_CONTAINER_APPS_SUBMISSION_RESULT=ALREADY_EXISTS
WEST_CONTAINER_APPS_LAST_OBSERVED_STATE=$EXISTING_STATE
EOF
        chmod 600 "$STATE_FILE"

        echo "WEST_CONTAINER_APPS_SUBMISSION_RESULT=ALREADY_EXISTS"
        echo "WEST_CONTAINER_APPS_PROVISIONING_STATE=${EXISTING_STATE:-not-reported}"
        echo "WEST_CONTAINER_APPS_STATE_FILE=$STATE_FILE"
        echo "RETRY_CREATION=false"
        echo
        echo "************************************************************"
        echo "WEST CONTAINER APPS ENVIRONMENT ALREADY EXISTS"
        echo "************************************************************"
        exit 0
    fi

    if az group show --name "$INFRASTRUCTURE_RG" --output none >/dev/null 2>&1; then
        fail "Infrastructure resource group already exists while the Container Apps environment does not: $INFRASTRUCTURE_RG"
    fi

    section "Submitting internal West Container Apps environment"

    az containerapp env create \
        --subscription "$SUBSCRIPTION_ID" \
        --resource-group "$RG_APP" \
        --name "$ENVIRONMENT_NAME" \
        --location "$LOCATION" \
        --infrastructure-resource-group "$INFRASTRUCTURE_RG" \
        --infrastructure-subnet-resource-id "$SUBNET_ID" \
        --internal-only true \
        --enable-workload-profiles true \
        --logs-destination log-analytics \
        --logs-workspace-id "$WORKSPACE_ID" \
        --logs-workspace-key "$WORKSPACE_KEY" \
        --tags \
            "application=Project Health Dashboard" \
            "environment=test" \
            "resource-function=container-apps-environment" \
            "region-role=primary" \
            "architecture=multi-region" \
        --no-wait \
        --only-show-errors \
        --output none

    cat > "$STATE_FILE" <<EOF
AZURE_SUBSCRIPTION_ID=$SUBSCRIPTION_ID
WEST_CONTAINER_APPS_ENVIRONMENT=$ENVIRONMENT_NAME
WEST_CONTAINER_APPS_ENVIRONMENT_RESOURCE_GROUP=$RG_APP
WEST_CONTAINER_APPS_ENVIRONMENT_LOCATION=$LOCATION
WEST_CONTAINER_APPS_INFRASTRUCTURE_RESOURCE_GROUP=$INFRASTRUCTURE_RG
WEST_CONTAINER_APPS_SUBNET_ID=$SUBNET_ID
WEST_CONTAINER_APPS_LOG_WORKSPACE_ID=$WORKSPACE_RESOURCE_ID
WEST_CONTAINER_APPS_SUBMISSION_RESULT=ACCEPTED
WEST_CONTAINER_APPS_SUBMITTED_AT=$(date -u -Is)
EOF
    chmod 600 "$STATE_FILE"

    OBSERVED_STATE="not-yet-visible"
    for _ in $(seq 1 6); do
        OBSERVED_STATE="$(az containerapp env show \
            --resource-group "$RG_APP" \
            --name "$ENVIRONMENT_NAME" \
            --query provisioningState \
            --output tsv 2>/dev/null || true)"
        [ -n "$OBSERVED_STATE" ] && break
        OBSERVED_STATE="not-yet-visible"
        sleep 5
    done

    echo "WEST_CONTAINER_APPS_SUBMISSION_RESULT=ACCEPTED"
    echo "WEST_CONTAINER_APPS_ENVIRONMENT=$ENVIRONMENT_NAME"
    echo "WEST_CONTAINER_APPS_PROVISIONING_STATE=$OBSERVED_STATE"
    echo "WEST_CONTAINER_APPS_STATE_FILE=$STATE_FILE"
    echo "APPLICATION_IMAGE_BUILD_STARTED=false"
    echo "REPLICA_CREATION_RETRIED=false"
    echo
    echo "************************************************************"
    echo "WEST CONTAINER APPS ENVIRONMENT SUBMISSION ACCEPTED"
    echo "************************************************************"

} 2>&1 | tee "$LOG"

echo
echo "Submission log: $LOG"
