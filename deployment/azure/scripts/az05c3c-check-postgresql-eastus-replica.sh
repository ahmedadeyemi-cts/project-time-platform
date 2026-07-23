#!/usr/bin/env bash
set -Eeuo pipefail

BASE_DIR="$HOME/project-health-dashboard-azure"
CONFIG_FILE="$BASE_DIR/config/az05c3b-postgresql-eastus-replica.env"

[ -s "$CONFIG_FILE" ] || {
    echo "ERROR: Replica submission state file is missing: $CONFIG_FILE" >&2
    exit 1
}

# shellcheck disable=SC1090
source "$CONFIG_FILE"

echo "PRIMARY_SERVER=$POSTGRES_PRIMARY_SERVER"
echo "REPLICA_SERVER=$POSTGRES_REPLICA_SERVER"
echo "REPLICA_RESOURCE_GROUP=$POSTGRES_REPLICA_RESOURCE_GROUP"
echo "REPLICA_LOCATION=$POSTGRES_REPLICA_LOCATION"

if ! az postgres flexible-server show \
    --resource-group "$POSTGRES_REPLICA_RESOURCE_GROUP" \
    --name "$POSTGRES_REPLICA_SERVER" \
    --output none >/dev/null 2>&1; then

    echo "REPLICA_VISIBLE=false"
    echo "REPLICA_STATUS=SUBMISSION_ACCEPTED_RESOURCE_NOT_VISIBLE"
    exit 0
fi

REPLICA_JSON="/tmp/phd-az05c3c-replica-status.json"

az postgres flexible-server show \
    --resource-group "$POSTGRES_REPLICA_RESOURCE_GROUP" \
    --name "$POSTGRES_REPLICA_SERVER" \
    --output json > "$REPLICA_JSON"

python3 - \
    "$REPLICA_JSON" \
    "$POSTGRES_PRIMARY_SERVER_ID" \
    "$POSTGRES_REPLICA_SKU" \
    "$POSTGRES_REPLICA_TIER" \
    "$POSTGRES_REPLICA_STORAGE_GIB" \
    "$POSTGRES_REPLICA_SUBNET_ID" \
    "$POSTGRES_PRIVATE_DNS_ZONE_ID" <<'PY'
import json
import sys
from pathlib import Path

obj = json.loads(Path(sys.argv[1]).read_text())
expected_source = sys.argv[2].lower()
expected_sku = sys.argv[3].lower()
expected_tier = sys.argv[4].lower()
expected_storage = int(sys.argv[5])
expected_subnet = sys.argv[6].lower()
expected_dns = sys.argv[7].lower()

state = str(obj.get("state") or "unknown")
role = str(obj.get("replicationRole") or "unknown")
source = str(obj.get("sourceServerResourceId") or "")
sku = str((obj.get("sku") or {}).get("name") or "")
tier = str((obj.get("sku") or {}).get("tier") or "")
storage = int((obj.get("storage") or {}).get("storageSizeGb") or 0)
fqdn = str(obj.get("fullyQualifiedDomainName") or "")
location = str(obj.get("location") or "")
public_access = str((obj.get("network") or {}).get("publicNetworkAccess") or "")
subnet = str((obj.get("network") or {}).get("delegatedSubnetResourceId") or "")
dns = str((obj.get("network") or {}).get("privateDnsZoneArmResourceId") or "")

print("REPLICA_VISIBLE=true")
print(f"REPLICA_STATE={state}")
print(f"REPLICA_ROLE={role}")
print(f"REPLICA_SOURCE_SERVER_ID={source}")
print(f"REPLICA_LOCATION={location}")
print(f"REPLICA_SKU={sku}")
print(f"REPLICA_TIER={tier}")
print(f"REPLICA_STORAGE_GIB={storage}")
print(f"REPLICA_FQDN={fqdn}")
print(f"REPLICA_PUBLIC_ACCESS={public_access}")
print(f"REPLICA_SUBNET_ID={subnet}")
print(f"REPLICA_PRIVATE_DNS_ZONE_ID={dns}")

errors = []
if source.lower() != expected_source:
    errors.append("source-server-id")
if sku.lower() != expected_sku:
    errors.append("sku")
if tier.lower() != expected_tier:
    errors.append("tier")
if storage < expected_storage:
    errors.append("storage")
if public_access.lower() != "disabled":
    errors.append("public-access")
if subnet.lower() != expected_subnet:
    errors.append("subnet")
if dns.lower() != expected_dns:
    errors.append("private-dns-zone")

print(f"REPLICA_CONFIGURATION_ERRORS={len(errors)}")
if errors:
    print("REPLICA_CONFIGURATION_ERROR_FIELDS=" + ",".join(errors))

state_l = state.lower()
role_l = role.lower()

if errors:
    print("REPLICA_STATUS=CONFIGURATION_MISMATCH")
elif state_l == "ready" and role_l in {"replica", "asyncreplica"}:
    print("REPLICA_STATUS=READY_REPLICA")
elif state_l in {"creating", "updating", "starting", "restarting"}:
    print("REPLICA_STATUS=PROVISIONING")
elif state_l in {"failed", "inaccessible", "dropping", "disabled"}:
    print("REPLICA_STATUS=FAILED_OR_UNHEALTHY")
else:
    print("REPLICA_STATUS=WAITING_OR_UNKNOWN")
PY

if az postgres flexible-server db show \
    --resource-group "$POSTGRES_REPLICA_RESOURCE_GROUP" \
    --server-name "$POSTGRES_REPLICA_SERVER" \
    --name "project_health_dashboard" \
    --output none >/dev/null 2>&1; then
    echo "REPLICA_DATABASE_PRESENT=project_health_dashboard"
else
    echo "REPLICA_DATABASE_PRESENT=not-yet-visible"
fi
