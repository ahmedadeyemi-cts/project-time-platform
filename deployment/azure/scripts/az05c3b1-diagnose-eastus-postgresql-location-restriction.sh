#!/usr/bin/env bash
set -Eeuo pipefail

LOCATION="eastus"
RG_SECONDARY_DATA="rg-project-health-dashboard-test-data-eastus"
REPLICA_SERVER="pg-phd-test-eus-7825cc"
EXPECTED_SKU="Standard_D2ds_v4"
EXPECTED_TIER="GeneralPurpose"
EXPECTED_VERSION="16"
EXPECTED_STORAGE_GIB="128"

BASE_DIR="$HOME/project-health-dashboard-azure"
LOG_DIR="$BASE_DIR/logs"
CONFIG_DIR="$BASE_DIR/config"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
LOG="$LOG_DIR/az05c3b1-eastus-postgresql-location-restriction-$STAMP.log"
RAW_CAPABILITIES="$LOG_DIR/az05c3b1-eastus-postgresql-capabilities-$STAMP.json"
RAW_ACTIVITY="$LOG_DIR/az05c3b1-eastus-postgresql-activity-$STAMP.json"
STATE_FILE="$CONFIG_DIR/az05c3b1-eastus-location-restriction.env"
START_TIME="$(date -u -d '3 hours ago' +%Y-%m-%dT%H:%M:%SZ)"

mkdir -p "$LOG_DIR" "$CONFIG_DIR"

section() {
    echo
    echo "============================================================"
    echo "$1"
    echo "============================================================"
}

{
    section "AZ-05C3B1 - Diagnose East US PostgreSQL Location Restriction"

    echo "TIME=$(date -u -Is)"
    echo "LOCATION=$LOCATION"
    echo "REPLICA_SERVER=$REPLICA_SERVER"
    echo "READ_ONLY_DIAGNOSTIC=true"
    echo "BILLABLE_REPLICA_CREATED=false"

    section "Confirming no replica resource exists"

    if az postgres flexible-server show \
        --resource-group "$RG_SECONDARY_DATA" \
        --name "$REPLICA_SERVER" \
        --output none >/dev/null 2>&1; then
        echo "REPLICA_EXISTS=true"
    else
        echo "REPLICA_EXISTS=false"
    fi

    if [ -s "$CONFIG_DIR/az05c3b-postgresql-eastus-replica.env" ]; then
        echo "REPLICA_SUBMISSION_STATE_FILE_PRESENT=true"
    else
        echo "REPLICA_SUBMISSION_STATE_FILE_PRESENT=false"
    fi

    section "Capturing East US PostgreSQL capability root"

    az postgres flexible-server list-skus \
        --location "$LOCATION" \
        --output json > "$RAW_CAPABILITIES"

    python3 - \
        "$RAW_CAPABILITIES" \
        "$EXPECTED_SKU" \
        "$EXPECTED_TIER" \
        "$EXPECTED_VERSION" \
        "$EXPECTED_STORAGE_GIB" <<'PY'
import json
import sys
from pathlib import Path

path = Path(sys.argv[1])
expected_sku = sys.argv[2].lower()
expected_tier = sys.argv[3].lower()
expected_version = int(sys.argv[4])
expected_storage = int(sys.argv[5])

obj = json.loads(path.read_text())
roots = obj if isinstance(obj, list) else []

print(f"CAPABILITY_ROOT_COUNT={len(roots)}")

for index, root in enumerate(roots):
    root = root or {}
    print(f"CAPABILITY_ROOT_{index}_NAME={root.get('name') or ''}")
    print(f"CAPABILITY_ROOT_{index}_STATUS={root.get('status') or ''}")
    print(f"CAPABILITY_ROOT_{index}_RESTRICTED={root.get('restricted')}")
    reason = str(root.get('reason') or '').replace('\n', ' ').replace('\r', ' ')
    print(f"CAPABILITY_ROOT_{index}_REASON={reason}")

blocked = {"disabled", "unavailable", "restricted", "notavailable"}
match_count = 0
for root in roots:
    for entry in (root or {}).get("supportedFastProvisioningEditions") or []:
        if (
            str(entry.get("supportedSku") or "").lower() == expected_sku
            and str(entry.get("supportedTier") or "").lower() == expected_tier
            and int(entry.get("supportedServerVersions") or 0) == expected_version
            and int(entry.get("supportedStorageGb") or 0) >= expected_storage
            and str(entry.get("status") or "").lower() not in blocked
        ):
            match_count += 1

print(f"FAST_PROVISIONING_MATCH_COUNT={match_count}")
PY

    echo "RAW_CAPABILITY_METADATA=$RAW_CAPABILITIES"

    section "Collecting failed PostgreSQL control-plane activity"

    az monitor activity-log list \
        --resource-group "$RG_SECONDARY_DATA" \
        --start-time "$START_TIME" \
        --status Failed \
        --max-events 100 \
        --output json > "$RAW_ACTIVITY" || true

    python3 - "$RAW_ACTIVITY" "$REPLICA_SERVER" <<'PY'
import json
import sys
from pathlib import Path

path = Path(sys.argv[1])
replica = sys.argv[2].lower()

try:
    events = json.loads(path.read_text())
except Exception:
    events = []

if not isinstance(events, list):
    events = []

selected = []
for event in events:
    text = json.dumps(event).lower()
    operation = str((event.get("operationName") or {}).get("value") or event.get("operationName") or "").lower()
    if replica in text or "microsoft.dbforpostgresql/flexibleservers/write" in operation:
        selected.append(event)

selected.sort(key=lambda item: str(item.get("eventTimestamp") or item.get("submissionTimestamp") or ""), reverse=True)
print(f"FAILED_POSTGRES_ACTIVITY_MATCH_COUNT={len(selected)}")

if selected:
    event = selected[0]
    op = event.get("operationName") or {}
    status = event.get("status") or {}
    substatus = event.get("subStatus") or {}
    props = event.get("properties") or {}
    message = props.get("statusMessage") or props.get("message") or ""
    if not isinstance(message, str):
        message = json.dumps(message, separators=(",", ":"))
    message = message.replace("\n", " ").replace("\r", " ")

    print(f"LATEST_FAILED_EVENT_TIME={event.get('eventTimestamp') or ''}")
    print(f"LATEST_FAILED_OPERATION={(op.get('value') if isinstance(op, dict) else op) or ''}")
    print(f"LATEST_FAILED_STATUS={(status.get('value') if isinstance(status, dict) else status) or ''}")
    print(f"LATEST_FAILED_SUBSTATUS={(substatus.get('value') if isinstance(substatus, dict) else substatus) or ''}")
    print(f"LATEST_FAILED_CORRELATION_ID={event.get('correlationId') or ''}")
    print(f"LATEST_FAILED_RESOURCE_ID={event.get('resourceId') or ''}")
    print(f"LATEST_FAILED_STATUS_MESSAGE={message}")
else:
    print("LATEST_FAILED_ACTIVITY=not-yet-available")
PY

    echo "RAW_ACTIVITY_LOG=$RAW_ACTIVITY"

    section "Diagnostic decision"

    ROOT_RESTRICTED="$(python3 - "$RAW_CAPABILITIES" <<'PY'
import json
import sys
from pathlib import Path
obj = json.loads(Path(sys.argv[1]).read_text())
roots = obj if isinstance(obj, list) else []
print("true" if any(bool((root or {}).get("restricted")) for root in roots) else "false")
PY
    )"

    ROOT_REASON="$(python3 - "$RAW_CAPABILITIES" <<'PY'
import json
import sys
from pathlib import Path
obj = json.loads(Path(sys.argv[1]).read_text())
roots = obj if isinstance(obj, list) else []
reasons = [str((root or {}).get("reason") or "").strip() for root in roots]
reasons = [item for item in reasons if item]
print(" | ".join(reasons).replace("\n", " ").replace("\r", " "))
PY
    )"

    if [ "$ROOT_RESTRICTED" = "true" ]; then
        LOCATION_ACCESS_DECISION="SUBSCRIPTION_REGION_RESTRICTED"
    else
        LOCATION_ACCESS_DECISION="CONTROL_PLANE_RESTRICTION_REQUIRES_SUPPORT_REVIEW"
    fi

    cat > "$STATE_FILE" <<EOF
LOCATION=$LOCATION
REPLICA_SERVER=$REPLICA_SERVER
CAPABILITY_ROOT_RESTRICTED=$ROOT_RESTRICTED
CAPABILITY_ROOT_REASON=$ROOT_REASON
LOCATION_ACCESS_DECISION=$LOCATION_ACCESS_DECISION
REPLICA_CREATED=false
DIAGNOSTIC_COMPLETED_AT=$STAMP
EOF

    chmod 600 "$STATE_FILE"

    echo "CAPABILITY_ROOT_RESTRICTED=$ROOT_RESTRICTED"
    echo "CAPABILITY_ROOT_REASON=${ROOT_REASON:-not-reported}"
    echo "LOCATION_ACCESS_DECISION=$LOCATION_ACCESS_DECISION"
    echo "REPLICA_CREATED=false"
    echo "RETRY_CREATION_NOW=false"
    echo "DIAGNOSTIC_STATE_FILE=$STATE_FILE"
    echo
    echo "************************************************************"
    echo "EASTUS POSTGRESQL LOCATION RESTRICTION DIAGNOSTIC COMPLETE"
    echo "************************************************************"

} 2>&1 | tee "$LOG"

echo
echo "Diagnostic log: $LOG"
