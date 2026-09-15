#!/usr/bin/env bash
set -Eeuo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=lib/common.sh
source "${SCRIPT_DIR}/lib/common.sh"

STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
LOG="${LOG_DIR}/dev01-resource-group-${STAMP}.log"

{
    section "DEV-01 - Pulse DEV resource group"
    echo "TIME=$(date -u -Is)"
    echo "Product: $PRODUCT_NAME"
    echo "Environment: $ENVIRONMENT"
    echo "Location: $LOCATION"
    echo "Resource group: $RG_DEV"

    require_subscription
    echo "Subscription confirmed: $SUBSCRIPTION_ID"

    section "Resource group"

    if az group show --name "$RG_DEV" --output none >/dev/null 2>&1; then
        EXISTING_LOCATION="$(az group show --name "$RG_DEV" --query location --output tsv)"
        [ "${EXISTING_LOCATION,,}" = "${LOCATION,,}" ] \
            || fail "Resource group $RG_DEV already exists in unexpected location: $EXISTING_LOCATION (expected $LOCATION)"
        echo "Existing resource group confirmed: $RG_DEV ($EXISTING_LOCATION)"
    else
        az group create \
            --name "$RG_DEV" \
            --location "$LOCATION" \
            --tags \
                "application=$PRODUCT_NAME" \
                "environment=$ENVIRONMENT" \
                "resource-function=dev-environment" \
                "architecture=single-region-scale-to-zero" \
                "managed-by=azure-cli" \
            --output none
        echo "Created resource group: $RG_DEV"
    fi

    record_config RG_DEV "$RG_DEV"
    record_config LOCATION "$LOCATION"
    record_config SUBSCRIPTION_ID "$SUBSCRIPTION_ID"

    section "Validation"

    az group show --name "$RG_DEV" \
        --query '{name:name,location:location,provisioningState:properties.provisioningState}' \
        --output table

    section "DEV-01 complete"
    echo "RESOURCE GROUP READY"
} 2>&1 | tee "$LOG"

echo
echo "Log: $LOG"
echo "Config: $DEV_CONFIG_FILE"
