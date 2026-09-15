#!/usr/bin/env bash
set -Eeuo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=lib/common.sh
source "${SCRIPT_DIR}/lib/common.sh"

STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
LOG="${LOG_DIR}/dev02-container-apps-environment-${STAMP}.log"

{
    section "DEV-02 - Pulse DEV Container Apps environment"
    echo "TIME=$(date -u -Is)"
    echo "Resource group: $RG_DEV"
    echo "Environment name: $ACA_ENV_NAME"
    echo "VNet: none (managed/default ingress per ADR 0001)"

    require_subscription
    echo "Subscription confirmed: $SUBSCRIPTION_ID"

    az group show --name "$RG_DEV" --output none \
        || fail "Resource group $RG_DEV does not exist. Run dev01-resource-group.sh first."

    section "Container Apps CLI extension"

    az config set extension.use_dynamic_install=yes_without_prompt --output none
    az extension add --name containerapp --upgrade --only-show-errors --output none
    CONTAINERAPP_EXTENSION_VERSION="$(az extension show --name containerapp --query version --output tsv)"
    echo "containerapp extension version: $CONTAINERAPP_EXTENSION_VERSION"

    provider_ready Microsoft.App
    provider_ready Microsoft.OperationalInsights

    section "Checking for an existing environment"

    if az containerapp env show --resource-group "$RG_DEV" --name "$ACA_ENV_NAME" --output none >/dev/null 2>&1; then
        EXISTING_JSON="$(az containerapp env show --resource-group "$RG_DEV" --name "$ACA_ENV_NAME" --output json)"
        EXISTING_LOCATION="$(python3 - "$EXISTING_JSON" <<'PY'
import json, sys
obj = json.loads(sys.argv[1])
print(obj.get("location") or "")
PY
)"
        EXISTING_VNET="$(python3 - "$EXISTING_JSON" <<'PY'
import json, sys
obj = json.loads(sys.argv[1])
props = obj.get("properties") or obj
print(bool((props.get("vnetConfiguration") or {}).get("infrastructureSubnetId")))
PY
)"
        [ "${EXISTING_LOCATION,,}" = "${LOCATION,,}" ] \
            || fail "Existing Container Apps environment $ACA_ENV_NAME is in unexpected location: $EXISTING_LOCATION (expected $LOCATION)"
        [ "$EXISTING_VNET" = "False" ] \
            || fail "Existing Container Apps environment $ACA_ENV_NAME unexpectedly has a VNet configured; ADR 0001 requires no VNet for DEV."
        echo "Existing Container Apps environment confirmed: $ACA_ENV_NAME ($EXISTING_LOCATION, no VNet)"
    else
        section "Creating the environment"

        # No --infrastructure-subnet-resource-id / --internal-only: this is a
        # default, publicly-reachable, managed-ingress environment with no
        # VNet, per ADR 0001. Not passing --logs-workspace-id lets Azure
        # provision its own managed Log Analytics workspace for the
        # environment; DEV does not need the two-region shared workspace
        # pattern used by az04-shared-services.sh.
        az containerapp env create \
            --subscription "$SUBSCRIPTION_ID" \
            --resource-group "$RG_DEV" \
            --name "$ACA_ENV_NAME" \
            --location "$LOCATION" \
            --tags \
                "application=$PRODUCT_NAME" \
                "environment=$ENVIRONMENT" \
                "resource-function=container-apps-environment" \
                "architecture=single-region-scale-to-zero" \
            --only-show-errors \
            --output none

        echo "Submitted Container Apps environment: $ACA_ENV_NAME"
    fi

    section "Waiting for the environment to be ready"

    STATE="unknown"
    for _ in $(seq 1 60); do
        STATE="$(az containerapp env show --resource-group "$RG_DEV" --name "$ACA_ENV_NAME" --query provisioningState --output tsv 2>/dev/null || true)"
        [ "${STATE,,}" = "succeeded" ] && break
        [ "${STATE,,}" = "failed" ] && fail "Container Apps environment provisioning failed: $ACA_ENV_NAME"
        sleep 10
    done
    [ "${STATE,,}" = "succeeded" ] || fail "Container Apps environment did not reach Succeeded within the wait budget (last state: $STATE)"
    echo "Provisioning state: $STATE"

    ENV_JSON="$(az containerapp env show --resource-group "$RG_DEV" --name "$ACA_ENV_NAME" --output json)"
    ENV_ID="$(python3 - "$ENV_JSON" <<'PY'
import json, sys
print(json.loads(sys.argv[1]).get("id") or "")
PY
)"
    ENV_DEFAULT_DOMAIN="$(python3 - "$ENV_JSON" <<'PY'
import json, sys
obj = json.loads(sys.argv[1])
props = obj.get("properties") or obj
print(props.get("defaultDomain") or obj.get("defaultDomain") or "")
PY
)"
    ENV_STATIC_IP="$(python3 - "$ENV_JSON" <<'PY'
import json, sys
obj = json.loads(sys.argv[1])
props = obj.get("properties") or obj
print(props.get("staticIp") or obj.get("staticIp") or "")
PY
)"

    record_config ACA_ENV_NAME "$ACA_ENV_NAME"
    record_config ACA_ENV_ID "$ENV_ID"
    record_config ACA_ENV_DEFAULT_DOMAIN "$ENV_DEFAULT_DOMAIN"
    record_config ACA_ENV_STATIC_IP "$ENV_STATIC_IP"

    section "Validation"

    az containerapp env show --resource-group "$RG_DEV" --name "$ACA_ENV_NAME" \
        --query '{name:name,location:location,provisioningState:provisioningState,defaultDomain:properties.defaultDomain,staticIp:properties.staticIp,vnet:properties.vnetConfiguration}' \
        --output table

    echo
    echo "NOTE: staticIp above is the environment's inbound/ingress IP, used for"
    echo "custom-domain DNS records. It is NOT a usable outbound/egress IP."
    echo "See dev04-postgresql-flexible-server.sh and README.md for why the"
    echo "PostgreSQL firewall rule cannot be scoped to a per-environment"
    echo "outbound IP for a VNet-less Consumption environment."

    section "DEV-02 complete"
    echo "CONTAINER APPS ENVIRONMENT READY"
} 2>&1 | tee "$LOG"

echo
echo "Log: $LOG"
echo "Config: $DEV_CONFIG_FILE"
