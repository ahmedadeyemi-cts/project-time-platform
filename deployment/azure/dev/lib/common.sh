#!/usr/bin/env bash
# Shared constants and helper functions for the Pulse DEV Azure environment
# scripts (deployment/azure/dev/dev0N-*.sh).
#
# This file is meant to be sourced, not executed directly. It follows the
# naming standard documented in deployment/azure/README.md (full product
# name "Pulse", short Azure prefix "phd") and reuses the generated-files
# convention ($HOME/project-health-dashboard-azure/{config,logs}) already
# used by deployment/azure/scripts/az0*.
#
# Scope: single-region (westus3), scale-to-zero DEV environment per
# docs/adr/0001-single-region-scale-to-zero-aca-topology-for-dev-environment.md.
# Do not add production-shaped resources (VNet, NAT gateway, HA, etc.) here.

set -Eeuo pipefail

PRODUCT_NAME="Pulse"
ENVIRONMENT="dev"
LOCATION="westus3"

# US Signal Azure test subscription, tenant 535941da-... (see
# docs/adr/0002-deploy-dev-environment-to-azure-container-apps-via-interactive.md
# and .verity/deploy-access.md). Hardcoded and verified at runtime (see
# require_subscription) rather than trusted from `az account show` alone,
# so a script never provisions into the wrong subscription because the
# operator forgot to switch context after an interactive az login.
SUBSCRIPTION_ID="cd32baeb-7b71-4bc0-8ea3-9f23a50903fe"

RG_DEV="rg-project-health-dashboard-dev-westus3"
ACA_ENV_NAME="cae-phd-dev-westus3"

# Matches the az04/az05b convention of deriving a short, globally-unique
# suffix from the subscription ID for resources that need a DNS-unique name
# (ACR, PostgreSQL Flexible Server).
UNIQUE_SUFFIX="$(printf '%s' "$SUBSCRIPTION_ID" | sha256sum | cut -c1-6)"

ACR_NAME="acrphddev${UNIQUE_SUFFIX}"

POSTGRES_SERVER="pg-phd-dev-w3-${UNIQUE_SUFFIX}"
POSTGRES_DATABASE="project_health_dashboard"
POSTGRES_ADMIN_USER="phdpgadmin"
POSTGRES_VERSION="16"
POSTGRES_SKU="Standard_B1ms"
POSTGRES_STORAGE_GIB="32"

BASE_DIR="${HOME}/project-health-dashboard-azure"
CONFIG_DIR="${BASE_DIR}/config"
LOG_DIR="${BASE_DIR}/logs"
DEV_CONFIG_FILE="${CONFIG_DIR}/dev-environment.env"
DEV_POSTGRES_PASSWORD_FILE="${CONFIG_DIR}/dev-postgres-admin-password.txt"

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

# require_subscription: point the CLI at the DEV test subscription and fail
# loudly if the signed-in context ends up somewhere else. Every dev0N script
# calls this before touching any resource.
require_subscription() {
    az account set --subscription "$SUBSCRIPTION_ID" --output none
    local current
    current="$(az account show --query id --output tsv)"
    [ "$current" = "$SUBSCRIPTION_ID" ] \
        || fail "Current Azure subscription ($current) does not match the expected DEV test subscription ($SUBSCRIPTION_ID). Run 'az login' then 'az account set --subscription $SUBSCRIPTION_ID'."
}

# provider_ready NAMESPACE: register a resource provider if needed and block
# until it reports Registered, matching az06a-submit-west-container-apps-environment.sh.
provider_ready() {
    local namespace="$1"
    local state
    state="$(az provider show --namespace "$namespace" --query registrationState --output tsv 2>/dev/null || true)"
    if [ "$state" != "Registered" ]; then
        echo "Registering provider: $namespace"
        az provider register --namespace "$namespace" --wait --only-show-errors
        state="$(az provider show --namespace "$namespace" --query registrationState --output tsv)"
    fi
    [ "$state" = "Registered" ] || fail "Provider is not Registered: $namespace ($state)"
    echo "Confirmed provider registered: $namespace"
}

# record_config KEY VALUE: upsert a KEY=VALUE line in the DEV config file so
# later scripts/stages can read back resource names without re-querying
# Azure. Never call this with secret values; see DEV_POSTGRES_PASSWORD_FILE
# for the one credential this workflow handles.
record_config() {
    local key="$1" value="$2"
    touch "$DEV_CONFIG_FILE"
    if grep -q "^${key}=" "$DEV_CONFIG_FILE" 2>/dev/null; then
        local tmp
        tmp="$(mktemp)"
        awk -F= -v k="$key" -v v="$value" '$1==k {$0=k"="v} {print}' "$DEV_CONFIG_FILE" > "$tmp"
        mv "$tmp" "$DEV_CONFIG_FILE"
    else
        echo "${key}=${value}" >> "$DEV_CONFIG_FILE"
    fi
    chmod 600 "$DEV_CONFIG_FILE"
}
