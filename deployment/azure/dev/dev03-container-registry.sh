#!/usr/bin/env bash
set -Eeuo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=lib/common.sh
source "${SCRIPT_DIR}/lib/common.sh"

STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
LOG="${LOG_DIR}/dev03-container-registry-${STAMP}.log"

{
    section "DEV-03 - Pulse DEV Container Registry (ACR Basic)"
    echo "TIME=$(date -u -Is)"
    echo "Resource group: $RG_DEV"
    echo "Registry name: $ACR_NAME"

    require_subscription
    echo "Subscription confirmed: $SUBSCRIPTION_ID"

    az group show --name "$RG_DEV" --output none \
        || fail "Resource group $RG_DEV does not exist. Run dev01-resource-group.sh first."

    provider_ready Microsoft.ContainerRegistry

    section "Registry"

    if az acr show --resource-group "$RG_DEV" --name "$ACR_NAME" --output none >/dev/null 2>&1; then
        EXISTING_SKU="$(az acr show --resource-group "$RG_DEV" --name "$ACR_NAME" --query sku.tier --output tsv)"
        EXISTING_LOCATION="$(az acr show --resource-group "$RG_DEV" --name "$ACR_NAME" --query location --output tsv)"
        [ "$EXISTING_SKU" = "Basic" ] \
            || fail "Existing ACR $ACR_NAME is not Basic tier (found: $EXISTING_SKU)."
        [ "${EXISTING_LOCATION,,}" = "${LOCATION,,}" ] \
            || fail "Existing ACR $ACR_NAME is in unexpected location: $EXISTING_LOCATION (expected $LOCATION)"
        echo "Existing registry confirmed: $ACR_NAME (Basic, $EXISTING_LOCATION)"
    else
        NAME_AVAILABLE="$(az acr check-name --name "$ACR_NAME" --query nameAvailable --output tsv)"
        [ "$NAME_AVAILABLE" = "True" ] \
            || fail "ACR name is not available: $ACR_NAME"

        az acr create \
            --resource-group "$RG_DEV" \
            --name "$ACR_NAME" \
            --location "$LOCATION" \
            --sku Basic \
            --admin-enabled false \
            --tags \
                "application=$PRODUCT_NAME" \
                "environment=$ENVIRONMENT" \
                "resource-function=container-registry" \
                "architecture=single-region-scale-to-zero" \
            --output none

        echo "Created registry: $ACR_NAME"
    fi

    ACR_LOGIN_SERVER="$(az acr show --resource-group "$RG_DEV" --name "$ACR_NAME" --query loginServer --output tsv)"

    record_config ACR_NAME "$ACR_NAME"
    record_config ACR_LOGIN_SERVER "$ACR_LOGIN_SERVER"

    section "Validation"

    az acr show --resource-group "$RG_DEV" --name "$ACR_NAME" \
        --query '{name:name,loginServer:loginServer,location:location,sku:sku.name,adminEnabled:adminUserEnabled,state:provisioningState}' \
        --output table

    section "DEV-03 complete"
    echo "CONTAINER REGISTRY READY"
} 2>&1 | tee "$LOG"

echo
echo "Log: $LOG"
echo "Config: $DEV_CONFIG_FILE"
