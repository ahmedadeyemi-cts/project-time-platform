#!/usr/bin/env bash
set -Eeuo pipefail

PRIMARY_LOCATION="westus3"
SECONDARY_LOCATION="eastus"

RG_PRIMARY_DATA="rg-project-health-dashboard-test-data-westus3"
RG_SECONDARY_DATA="rg-project-health-dashboard-test-data-eastus"
RG_SECONDARY_NETWORK="rg-project-health-dashboard-test-network-eastus"
RG_SHARED="rg-project-health-dashboard-shared-global"
RG_MIGRATION="rg-project-health-dashboard-test-migration-eastus"

PRIMARY_SERVER="pg-phd-test-w3-7825cc"
REPLICA_SERVER="pg-phd-test-eus-7825cc"
DATABASE_NAME="project_health_dashboard"
EXPECTED_VERSION="16"
EXPECTED_SKU="Standard_D2ds_v4"
EXPECTED_TIER="GeneralPurpose"
EXPECTED_STORAGE_GIB="128"

SECONDARY_VNET="vnet-phd-test-eastus"
POSTGRES_SUBNET="snet-postgresql"
POSTGRES_PRIVATE_DNS_ZONE="phd-test.postgres.database.azure.com"
MIGRATION_VM="vm-phd-test-db-migrate-eus"

BASE_DIR="$HOME/project-health-dashboard-azure"
CONFIG_DIR="$BASE_DIR/config"
LOG_DIR="$BASE_DIR/logs"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
LOG="$LOG_DIR/az05c3a2-postgresql-eastus-replica-preflight-corrected-$STAMP.log"
CONFIG_FILE="$CONFIG_DIR/az05c3a2-postgresql-eastus-replica-preflight.env"
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

require_ci() {
    local actual="$1"
    local expected="$2"
    local label="$3"

    [ "${actual,,}" = "${expected,,}" ] || fail "$label is '$actual'; expected '$expected'."
}

{
    section "AZ-05C3A2 - Corrected East US PostgreSQL Replica Read-only Preflight"

    echo "TIME=$(date -u -Is)"
    echo "PRIMARY_SERVER=$PRIMARY_SERVER"
    echo "PLANNED_REPLICA=$REPLICA_SERVER"
    echo "REPLICA_LOCATION=$SECONDARY_LOCATION"
    echo "READ_ONLY_PREFLIGHT=true"
    echo "BILLABLE_REPLICA_CREATED=false"

    SUBSCRIPTION_ID="$(az account show --query id -o tsv)"
    [ -n "$SUBSCRIPTION_ID" ] || fail "Azure subscription ID is empty."

    section "Validating imported PostgreSQL primary"

    PRIMARY_JSON="$WORK_DIR/primary.json"
    az postgres flexible-server show \
        --resource-group "$RG_PRIMARY_DATA" \
        --name "$PRIMARY_SERVER" \
        --output json > "$PRIMARY_JSON"

    readarray -t PRIMARY_FIELDS < <(
        python3 - "$PRIMARY_JSON" <<'PY'
import json
import sys
from pathlib import Path

obj = json.loads(Path(sys.argv[1]).read_text())
print(obj.get("id") or "")
print(obj.get("state") or "")
print(obj.get("version") or "")
print((obj.get("sku") or {}).get("name") or "")
print((obj.get("sku") or {}).get("tier") or "")
print((obj.get("storage") or {}).get("storageSizeGb") or "")
print(obj.get("fullyQualifiedDomainName") or "")
print((obj.get("network") or {}).get("publicNetworkAccess") or "")
print(obj.get("replicationRole") or "")
PY
    )

    PRIMARY_ID="${PRIMARY_FIELDS[0]}"
    PRIMARY_STATE="${PRIMARY_FIELDS[1]}"
    PRIMARY_VERSION="${PRIMARY_FIELDS[2]}"
    PRIMARY_SKU="${PRIMARY_FIELDS[3]}"
    PRIMARY_TIER="${PRIMARY_FIELDS[4]}"
    PRIMARY_STORAGE="${PRIMARY_FIELDS[5]}"
    PRIMARY_FQDN="${PRIMARY_FIELDS[6]}"
    PRIMARY_PUBLIC_ACCESS="${PRIMARY_FIELDS[7]}"
    PRIMARY_REPLICATION_ROLE="${PRIMARY_FIELDS[8]}"

    [ -n "$PRIMARY_ID" ] || fail "Primary server ID is empty."
    require_ci "$PRIMARY_STATE" "Ready" "Primary state"
    [[ "$PRIMARY_VERSION" == 16* ]] || fail "Primary version is '$PRIMARY_VERSION'; expected PostgreSQL 16."
    require_ci "$PRIMARY_SKU" "$EXPECTED_SKU" "Primary SKU"
    require_ci "$PRIMARY_TIER" "$EXPECTED_TIER" "Primary tier"
    [ "${PRIMARY_STORAGE:-0}" -ge "$EXPECTED_STORAGE_GIB" ] \
        || fail "Primary storage is ${PRIMARY_STORAGE:-0} GiB; expected at least $EXPECTED_STORAGE_GIB GiB."
    require_ci "$PRIMARY_PUBLIC_ACCESS" "Disabled" "Primary public network access"

    case "${PRIMARY_REPLICATION_ROLE,,}" in
        ""|primary|none) ;;
        *) fail "Unexpected primary replication role: $PRIMARY_REPLICATION_ROLE" ;;
    esac

    az postgres flexible-server db show \
        --resource-group "$RG_PRIMARY_DATA" \
        --server-name "$PRIMARY_SERVER" \
        --name "$DATABASE_NAME" \
        --output none

    echo "PRIMARY_STATE=$PRIMARY_STATE"
    echo "PRIMARY_VERSION=$PRIMARY_VERSION"
    echo "PRIMARY_SKU=$PRIMARY_SKU"
    echo "PRIMARY_TIER=$PRIMARY_TIER"
    echo "PRIMARY_STORAGE_GIB=$PRIMARY_STORAGE"
    echo "PRIMARY_PUBLIC_ACCESS=$PRIMARY_PUBLIC_ACCESS"
    echo "PRIMARY_REPLICATION_ROLE=${PRIMARY_REPLICATION_ROLE:-not-reported}"
    echo "IMPORTED_DATABASE_PRESENT=$DATABASE_NAME"

    section "Validating current replica topology"

    REPLICA_LIST_JSON="$WORK_DIR/replicas.json"
    az postgres flexible-server replica list \
        --resource-group "$RG_PRIMARY_DATA" \
        --name "$PRIMARY_SERVER" \
        --output json > "$REPLICA_LIST_JSON"

    EXISTING_REPLICA_COUNT="$(python3 - "$REPLICA_LIST_JSON" <<'PY'
import json
import sys
from pathlib import Path

obj = json.loads(Path(sys.argv[1]).read_text())
print(len(obj) if isinstance(obj, list) else 0)
PY
    )"

    [ "$EXISTING_REPLICA_COUNT" = "0" ] \
        || fail "Primary already has $EXISTING_REPLICA_COUNT replica(s)."

    if az postgres flexible-server show \
        --resource-group "$RG_SECONDARY_DATA" \
        --name "$REPLICA_SERVER" \
        --output none >/dev/null 2>&1; then
        fail "Planned East US replica already exists: $REPLICA_SERVER"
    fi

    echo "EXISTING_REPLICA_COUNT=$EXISTING_REPLICA_COUNT"
    echo "PLANNED_REPLICA_EXISTS=false"

    section "Validating East US delegated PostgreSQL subnet"

    SECONDARY_VNET_ID="$(az network vnet show \
        --resource-group "$RG_SECONDARY_NETWORK" \
        --name "$SECONDARY_VNET" \
        --query id \
        -o tsv)"

    SECONDARY_SUBNET_JSON="$WORK_DIR/subnet.json"
    az network vnet subnet show \
        --resource-group "$RG_SECONDARY_NETWORK" \
        --vnet-name "$SECONDARY_VNET" \
        --name "$POSTGRES_SUBNET" \
        --output json > "$SECONDARY_SUBNET_JSON"

    readarray -t SUBNET_FIELDS < <(
        python3 - "$SECONDARY_SUBNET_JSON" <<'PY'
import json
import sys
from pathlib import Path

obj = json.loads(Path(sys.argv[1]).read_text())
services = [str((item or {}).get("serviceName") or "").lower() for item in obj.get("delegations") or []]
print(obj.get("id") or "")
print(sum(1 for value in services if value == "microsoft.dbforpostgresql/flexibleservers"))
print(obj.get("privateEndpointNetworkPolicies") or "")
PY
    )

    SECONDARY_SUBNET_ID="${SUBNET_FIELDS[0]}"
    POSTGRES_DELEGATION_COUNT="${SUBNET_FIELDS[1]}"
    SUBNET_PRIVATE_ENDPOINT_POLICIES="${SUBNET_FIELDS[2]}"

    [ -n "$SECONDARY_VNET_ID" ] || fail "East VNet ID is empty."
    [ -n "$SECONDARY_SUBNET_ID" ] || fail "East PostgreSQL subnet ID is empty."
    [ "$POSTGRES_DELEGATION_COUNT" = "1" ] \
        || fail "East PostgreSQL subnet does not have exactly one Flexible Server delegation."

    echo "SECONDARY_VNET_ID=$SECONDARY_VNET_ID"
    echo "SECONDARY_SUBNET_ID=$SECONDARY_SUBNET_ID"
    echo "POSTGRES_SUBNET_DELEGATION=confirmed"
    echo "SUBNET_PRIVATE_ENDPOINT_POLICIES=${SUBNET_PRIVATE_ENDPOINT_POLICIES:-not-reported}"

    section "Validating PostgreSQL private DNS zone and East VNet link"

    DNS_ZONE_ID="$(az network private-dns zone show \
        --resource-group "$RG_SHARED" \
        --name "$POSTGRES_PRIVATE_DNS_ZONE" \
        --query id \
        -o tsv)"

    DNS_LINKS_JSON="$WORK_DIR/dns-links.json"
    az network private-dns link vnet list \
        --resource-group "$RG_SHARED" \
        --zone-name "$POSTGRES_PRIVATE_DNS_ZONE" \
        --output json > "$DNS_LINKS_JSON"

    EAST_LINK_COUNT="$(python3 - "$DNS_LINKS_JSON" "$SECONDARY_VNET_ID" <<'PY'
import json
import sys
from pathlib import Path

links = json.loads(Path(sys.argv[1]).read_text())
wanted = sys.argv[2].lower()
print(sum(
    1 for link in links
    if str((link.get("virtualNetwork") or {}).get("id") or "").lower() == wanted
    and str(link.get("provisioningState") or "").lower() == "succeeded"
    and str(link.get("virtualNetworkLinkState") or "").lower() == "completed"
))
PY
    )"

    [ -n "$DNS_ZONE_ID" ] || fail "PostgreSQL private DNS zone ID is empty."
    [ "$EAST_LINK_COUNT" = "1" ] \
        || fail "PostgreSQL private DNS zone does not have exactly one completed East VNet link."

    echo "POSTGRES_PRIVATE_DNS_ZONE_ID=$DNS_ZONE_ID"
    echo "POSTGRES_PRIVATE_DNS_EAST_LINK=confirmed"

    section "Validating East US PostgreSQL capability response"

    SKU_JSON="$WORK_DIR/eastus-postgres-skus.json"
    az postgres flexible-server list-skus \
        --location "$SECONDARY_LOCATION" \
        --output json > "$SKU_JSON"

    FAST_MATCH_COUNT="$(python3 - \
        "$SKU_JSON" \
        "$EXPECTED_SKU" \
        "$EXPECTED_TIER" \
        "$EXPECTED_VERSION" \
        "$EXPECTED_STORAGE_GIB" <<'PY'
import json
import sys
from pathlib import Path

obj = json.loads(Path(sys.argv[1]).read_text())
wanted_sku = sys.argv[2].lower()
wanted_tier = sys.argv[3].lower()
wanted_version = int(sys.argv[4])
wanted_storage = int(sys.argv[5])

if not isinstance(obj, list) or not obj:
    raise SystemExit("East US capability response is not a non-empty list.")

blocked = {"disabled", "unavailable", "restricted", "notavailable"}
count = 0

for root in obj:
    for entry in (root or {}).get("supportedFastProvisioningEditions") or []:
        sku = str(entry.get("supportedSku") or "").lower()
        tier = str(entry.get("supportedTier") or "").lower()
        version = int(entry.get("supportedServerVersions") or 0)
        storage = int(entry.get("supportedStorageGb") or 0)
        status = str(entry.get("status") or "").lower()
        if (
            sku == wanted_sku
            and tier == wanted_tier
            and version == wanted_version
            and storage >= wanted_storage
            and status not in blocked
        ):
            count += 1

print(count)
PY
    )"

    [ "$FAST_MATCH_COUNT" -gt 0 ] \
        || fail "East US does not currently advertise the expected PostgreSQL replica configuration."

    echo "EASTUS_EXPECTED_SKU=$EXPECTED_SKU"
    echo "EASTUS_EXPECTED_TIER=$EXPECTED_TIER"
    echo "EASTUS_EXPECTED_VERSION=$EXPECTED_VERSION"
    echo "EASTUS_EXPECTED_STORAGE_GIB=$EXPECTED_STORAGE_GIB"
    echo "FAST_PROVISIONING_MATCH_COUNT=$FAST_MATCH_COUNT"
    echo "EASTUS_SKU_PREFLIGHT=passed"

    section "Confirming migration-runner cost cleanup"

    if az vm show \
        --resource-group "$RG_MIGRATION" \
        --name "$MIGRATION_VM" \
        --output none >/dev/null 2>&1; then

        MIGRATION_VM_POWER="$(az vm get-instance-view \
            --resource-group "$RG_MIGRATION" \
            --name "$MIGRATION_VM" \
            --query "instanceView.statuses[?starts_with(code, 'PowerState/')].displayStatus | [0]" \
            -o tsv)"

        require_ci "$MIGRATION_VM_POWER" "VM deallocated" "Migration VM power state"
        echo "MIGRATION_VM_POWER_STATE=$MIGRATION_VM_POWER"
    else
        echo "MIGRATION_VM_POWER_STATE=deleted"
    fi

    section "Writing nonsecret replica preflight state"

    cat > "$CONFIG_FILE" <<EOF
AZURE_SUBSCRIPTION_ID=$SUBSCRIPTION_ID
POSTGRES_PRIMARY_SERVER=$PRIMARY_SERVER
POSTGRES_PRIMARY_SERVER_ID=$PRIMARY_ID
POSTGRES_PRIMARY_RESOURCE_GROUP=$RG_PRIMARY_DATA
POSTGRES_PRIMARY_LOCATION=$PRIMARY_LOCATION
POSTGRES_PRIMARY_FQDN=$PRIMARY_FQDN
POSTGRES_DATABASE=$DATABASE_NAME
POSTGRES_REPLICA_SERVER=$REPLICA_SERVER
POSTGRES_REPLICA_RESOURCE_GROUP=$RG_SECONDARY_DATA
POSTGRES_REPLICA_LOCATION=$SECONDARY_LOCATION
POSTGRES_REPLICA_SKU=$EXPECTED_SKU
POSTGRES_REPLICA_TIER=$EXPECTED_TIER
POSTGRES_REPLICA_STORAGE_GIB=$EXPECTED_STORAGE_GIB
POSTGRES_REPLICA_SUBNET_ID=$SECONDARY_SUBNET_ID
POSTGRES_PRIVATE_DNS_ZONE_ID=$DNS_ZONE_ID
EXISTING_REPLICA_COUNT=$EXISTING_REPLICA_COUNT
FAST_PROVISIONING_MATCH_COUNT=$FAST_MATCH_COUNT
PREFLIGHT_COMPLETED_AT=$STAMP
PREFLIGHT_SCRIPT=az05c3a2
EOF

    chmod 600 "$CONFIG_FILE"

    echo "PREFLIGHT_STATE_FILE=$CONFIG_FILE"
    echo "REPLICA_CREATION_DECISION=READY_NOT_CREATED"
    echo
    echo "No replica was created by this preflight."
    echo
    echo "************************************************************"
    echo "EASTUS POSTGRESQL REPLICA CORRECTED PREFLIGHT PASSED"
    echo "************************************************************"

} 2>&1 | tee "$LOG"

echo
echo "Corrected replica preflight log: $LOG"
