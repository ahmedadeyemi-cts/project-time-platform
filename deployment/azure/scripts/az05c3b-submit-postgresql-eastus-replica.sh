#!/usr/bin/env bash
set -Eeuo pipefail

BASE_DIR="$HOME/project-health-dashboard-azure"
CONFIG_DIR="$BASE_DIR/config"
LOG_DIR="$BASE_DIR/logs"
PREFLIGHT_FILE="$CONFIG_DIR/az05c3a2-postgresql-eastus-replica-preflight.env"
STATE_FILE="$CONFIG_DIR/az05c3b-postgresql-eastus-replica.env"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
LOG="$LOG_DIR/az05c3b-submit-postgresql-eastus-replica-$STAMP.log"
WORK_DIR="$(mktemp -d)"

mkdir -p "$CONFIG_DIR" "$LOG_DIR"
trap 'rm -rf "$WORK_DIR"' EXIT

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

{
    section "AZ-05C3B - Submit East US PostgreSQL Read Replica"

    echo "TIME=$(date -u -Is)"
    echo "BILLABLE_RESOURCE_ACTION=true"
    echo "REPLICA_CREATION_APPROVAL=${PHD_CREATE_BILLABLE_REPLICA:-not-set}"

    [ "${PHD_CREATE_BILLABLE_REPLICA:-}" = "YES" ] \
        || fail "Set PHD_CREATE_BILLABLE_REPLICA=YES only after reviewing the corrected preflight output."

    [ -s "$PREFLIGHT_FILE" ] || fail "Corrected replica preflight state is missing: $PREFLIGHT_FILE"

    # shellcheck disable=SC1090
    source "$PREFLIGHT_FILE"

    [ "${PREFLIGHT_SCRIPT:-}" = "az05c3a2" ] || fail "Preflight state was not produced by az05c3a2."
    [ "${EXISTING_REPLICA_COUNT:-}" = "0" ] || fail "Preflight recorded an existing replica."
    [ "${FAST_PROVISIONING_MATCH_COUNT:-0}" -gt 0 ] || fail "Preflight did not confirm an East US capability match."

    section "Revalidating current topology and East US capability"

    CURRENT_PRIMARY_STATE="$(az postgres flexible-server show \
        --resource-group "$POSTGRES_PRIMARY_RESOURCE_GROUP" \
        --name "$POSTGRES_PRIMARY_SERVER" \
        --query state \
        -o tsv)"

    [ "$CURRENT_PRIMARY_STATE" = "Ready" ] || fail "Primary is not Ready: $CURRENT_PRIMARY_STATE"

    CURRENT_PRIMARY_ID="$(az postgres flexible-server show \
        --resource-group "$POSTGRES_PRIMARY_RESOURCE_GROUP" \
        --name "$POSTGRES_PRIMARY_SERVER" \
        --query id \
        -o tsv)"

    [ "$CURRENT_PRIMARY_ID" = "$POSTGRES_PRIMARY_SERVER_ID" ] \
        || fail "Primary resource ID differs from corrected preflight state."

    CURRENT_REPLICA_COUNT="$(az postgres flexible-server replica list \
        --resource-group "$POSTGRES_PRIMARY_RESOURCE_GROUP" \
        --name "$POSTGRES_PRIMARY_SERVER" \
        --query 'length(@)' \
        -o tsv)"

    [ "$CURRENT_REPLICA_COUNT" = "0" ] || fail "Primary already has $CURRENT_REPLICA_COUNT replica(s)."

    if az postgres flexible-server show \
        --resource-group "$POSTGRES_REPLICA_RESOURCE_GROUP" \
        --name "$POSTGRES_REPLICA_SERVER" \
        --output none >/dev/null 2>&1; then
        fail "Planned replica already exists: $POSTGRES_REPLICA_SERVER"
    fi

    SKU_JSON="$WORK_DIR/eastus-postgres-skus.json"
    az postgres flexible-server list-skus \
        --location "$POSTGRES_REPLICA_LOCATION" \
        --output json > "$SKU_JSON"

    LIVE_MATCH_COUNT="$(python3 - \
        "$SKU_JSON" \
        "$POSTGRES_REPLICA_SKU" \
        "$POSTGRES_REPLICA_TIER" \
        "16" \
        "$POSTGRES_REPLICA_STORAGE_GIB" <<'PY'
import json
import sys
from pathlib import Path

obj = json.loads(Path(sys.argv[1]).read_text())
wanted_sku = sys.argv[2].lower()
wanted_tier = sys.argv[3].lower()
wanted_version = int(sys.argv[4])
wanted_storage = int(sys.argv[5])
blocked = {"disabled", "unavailable", "restricted", "notavailable"}
count = 0

if not isinstance(obj, list) or not obj:
    raise SystemExit("East US capability response is not a non-empty list.")

for root in obj:
    for entry in (root or {}).get("supportedFastProvisioningEditions") or []:
        if (
            str(entry.get("supportedSku") or "").lower() == wanted_sku
            and str(entry.get("supportedTier") or "").lower() == wanted_tier
            and int(entry.get("supportedServerVersions") or 0) == wanted_version
            and int(entry.get("supportedStorageGb") or 0) >= wanted_storage
            and str(entry.get("status") or "").lower() not in blocked
        ):
            count += 1

print(count)
PY
    )"

    [ "$LIVE_MATCH_COUNT" -gt 0 ] \
        || fail "East US no longer advertises the planned replica configuration."

    echo "PRIMARY_STATE=$CURRENT_PRIMARY_STATE"
    echo "CURRENT_REPLICA_COUNT=$CURRENT_REPLICA_COUNT"
    echo "LIVE_FAST_PROVISIONING_MATCH_COUNT=$LIVE_MATCH_COUNT"
    echo "PLANNED_REPLICA=$POSTGRES_REPLICA_SERVER"
    echo "PLANNED_LOCATION=$POSTGRES_REPLICA_LOCATION"
    echo "PLANNED_SKU=$POSTGRES_REPLICA_SKU"
    echo "PLANNED_TIER=$POSTGRES_REPLICA_TIER"
    echo "PLANNED_STORAGE_GIB=$POSTGRES_REPLICA_STORAGE_GIB"
    echo "PLANNED_SUBNET_ID=$POSTGRES_REPLICA_SUBNET_ID"
    echo "PLANNED_PRIVATE_DNS_ZONE_ID=$POSTGRES_PRIVATE_DNS_ZONE_ID"

    section "Submitting billable East US PostgreSQL replica"

    az postgres flexible-server replica create \
        --resource-group "$POSTGRES_REPLICA_RESOURCE_GROUP" \
        --name "$POSTGRES_REPLICA_SERVER" \
        --source-server "$POSTGRES_PRIMARY_SERVER_ID" \
        --location "$POSTGRES_REPLICA_LOCATION" \
        --sku-name "$POSTGRES_REPLICA_SKU" \
        --tier "$POSTGRES_REPLICA_TIER" \
        --storage-size "$POSTGRES_REPLICA_STORAGE_GIB" \
        --subnet "$POSTGRES_REPLICA_SUBNET_ID" \
        --private-dns-zone "$POSTGRES_PRIVATE_DNS_ZONE_ID" \
        --tags \
            project=project-health-dashboard \
            environment=test \
            role=postgresql-read-replica \
            region=eastus \
            migrationPhase=AZ-05C3 \
        --yes \
        --no-wait \
        --only-show-errors \
        --output none

    cat > "$STATE_FILE" <<EOF
POSTGRES_PRIMARY_SERVER=$POSTGRES_PRIMARY_SERVER
POSTGRES_PRIMARY_SERVER_ID=$POSTGRES_PRIMARY_SERVER_ID
POSTGRES_PRIMARY_RESOURCE_GROUP=$POSTGRES_PRIMARY_RESOURCE_GROUP
POSTGRES_REPLICA_SERVER=$POSTGRES_REPLICA_SERVER
POSTGRES_REPLICA_RESOURCE_GROUP=$POSTGRES_REPLICA_RESOURCE_GROUP
POSTGRES_REPLICA_LOCATION=$POSTGRES_REPLICA_LOCATION
POSTGRES_REPLICA_SKU=$POSTGRES_REPLICA_SKU
POSTGRES_REPLICA_TIER=$POSTGRES_REPLICA_TIER
POSTGRES_REPLICA_STORAGE_GIB=$POSTGRES_REPLICA_STORAGE_GIB
POSTGRES_REPLICA_SUBNET_ID=$POSTGRES_REPLICA_SUBNET_ID
POSTGRES_PRIVATE_DNS_ZONE_ID=$POSTGRES_PRIVATE_DNS_ZONE_ID
REPLICA_SUBMITTED_AT=$STAMP
REPLICA_CREATION_STATUS=Submitted
EOF

    chmod 600 "$STATE_FILE"

    echo "REPLICA_CREATION_ACTION=submitted"
    echo "REPLICA_SERVER=$POSTGRES_REPLICA_SERVER"
    echo "REPLICA_RESOURCE_GROUP=$POSTGRES_REPLICA_RESOURCE_GROUP"
    echo "REPLICA_STATE_FILE=$STATE_FILE"
    echo "BILLING_STATUS=starts-when-Azure-provisions-replica"
    echo
    echo "************************************************************"
    echo "EASTUS POSTGRESQL REPLICA CREATION SUBMITTED"
    echo "************************************************************"

} 2>&1 | tee "$LOG"

echo
echo "Replica submission log: $LOG"
