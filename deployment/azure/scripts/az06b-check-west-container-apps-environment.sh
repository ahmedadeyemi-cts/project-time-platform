#!/usr/bin/env bash
set -Eeuo pipefail

SUBSCRIPTION_ID="cd32baeb-7b71-4bc0-8ea3-9f23a50903fe"
RG_APP="rg-project-health-dashboard-test-app-westus3"
ENVIRONMENT_NAME="cae-phd-test-westus3"
EXPECTED_LOCATION="westus3"
EXPECTED_SUBNET_ID="/subscriptions/${SUBSCRIPTION_ID}/resourceGroups/rg-project-health-dashboard-test-network-westus3/providers/Microsoft.Network/virtualNetworks/vnet-phd-test-westus3/subnets/snet-aca-infrastructure"

BASE_DIR="$HOME/project-health-dashboard-azure"
CONFIG_DIR="$BASE_DIR/config"
LOG_DIR="$BASE_DIR/logs"
STATE_FILE="$CONFIG_DIR/az06a-west-container-apps-environment.env"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
LOG="$LOG_DIR/az06b-check-west-container-apps-environment-$STAMP.log"
JSON_FILE="$LOG_DIR/az06b-west-container-apps-environment-$STAMP.json"
mkdir -p "$CONFIG_DIR" "$LOG_DIR"

section() {
    echo
    echo "============================================================"
    echo "$1"
    echo "============================================================"
}

{
    section "AZ-06B - Check West Container Apps Environment"
    echo "TIME=$(date -u -Is)"
    echo "SUBSCRIPTION_ID=$SUBSCRIPTION_ID"
    echo "ENVIRONMENT_NAME=$ENVIRONMENT_NAME"
    echo "READ_ONLY_AZURE_QUERY=true"

    az account set --subscription "$SUBSCRIPTION_ID"

    CURRENT_SUBSCRIPTION_ID="$(az account show --query id -o tsv)"
    if [ "$CURRENT_SUBSCRIPTION_ID" != "$SUBSCRIPTION_ID" ]; then
        echo "ERROR: Current Azure subscription does not match the intended subscription."
        exit 1
    fi

    echo "CURRENT_SUBSCRIPTION_MATCH=yes"
    echo "SUBMISSION_STATE_FILE_PRESENT=$([ -s "$STATE_FILE" ] && echo true || echo false)"

    SHOW_ERROR="$LOG_DIR/az06b-west-container-apps-environment-show-$STAMP.stderr"

    set +e
    az containerapp env show \
        --resource-group "$RG_APP" \
        --name "$ENVIRONMENT_NAME" \
        --only-show-errors \
        --output json > "$JSON_FILE" 2> "$SHOW_ERROR"
    SHOW_RC=$?
    set -e

    echo "ENVIRONMENT_SHOW_RC=$SHOW_RC"
    echo "ENVIRONMENT_JSON_BYTES=$([ -f "$JSON_FILE" ] && wc -c < "$JSON_FILE" || echo 0)"
    echo "ENVIRONMENT_STDERR_BYTES=$([ -f "$SHOW_ERROR" ] && wc -c < "$SHOW_ERROR" || echo 0)"

    if [ "$SHOW_RC" -ne 0 ] || [ ! -s "$JSON_FILE" ]; then
        echo "WEST_CONTAINER_APPS_ENVIRONMENT_RESULT=NOT_FOUND_OR_NOT_VISIBLE"
        echo "WEST_CONTAINER_APPS_ENVIRONMENT_READY=false"
        if [ -s "$SHOW_ERROR" ]; then
            echo "ENVIRONMENT_SHOW_ERROR_BEGIN"
            cat "$SHOW_ERROR"
            echo "ENVIRONMENT_SHOW_ERROR_END"
        fi
        echo
        echo "************************************************************"
        echo "WEST CONTAINER APPS ENVIRONMENT NOT READY"
        echo "************************************************************"
        exit 0
    fi

    python3 - "$JSON_FILE" "$EXPECTED_LOCATION" "$EXPECTED_SUBNET_ID" <<'PY'
import json
import re
import sys
from pathlib import Path

obj = json.loads(Path(sys.argv[1]).read_text())
expected_location = sys.argv[2]
expected_subnet = sys.argv[3].lower()
props = obj.get("properties") or obj
vnet = props.get("vnetConfiguration") or {}
state = str(obj.get("provisioningState") or props.get("provisioningState") or "")
location = str(obj.get("location") or "")
subnet = str(vnet.get("infrastructureSubnetId") or "")
internal = vnet.get("internal")
default_domain = str(props.get("defaultDomain") or obj.get("defaultDomain") or "")
static_ip = str(props.get("staticIp") or obj.get("staticIp") or "")
environment_id = str(obj.get("id") or "")

def normalize_location(value: str) -> str:
    return re.sub(r"[^a-z0-9]", "", value.lower())

location_match = normalize_location(location) == normalize_location(expected_location)
subnet_match = subnet.lower() == expected_subnet

print(f"WEST_CONTAINER_APPS_ENVIRONMENT_ID={environment_id}")
print(f"WEST_CONTAINER_APPS_PROVISIONING_STATE={state}")
print(f"WEST_CONTAINER_APPS_LOCATION={location}")
print(f"WEST_CONTAINER_APPS_INTERNAL={str(internal).lower() if internal is not None else 'not-reported'}")
print(f"WEST_CONTAINER_APPS_SUBNET_ID={subnet}")
print(f"WEST_CONTAINER_APPS_DEFAULT_DOMAIN={default_domain or 'not-yet-reported'}")
print(f"WEST_CONTAINER_APPS_STATIC_IP={static_ip or 'not-yet-reported'}")
print(f"LOCATION_MATCH={'yes' if location_match else 'no'}")
print(f"SUBNET_MATCH={'yes' if subnet_match else 'no'}")

ready = (
    state.lower() == "succeeded"
    and location_match
    and subnet_match
    and internal is True
    and bool(default_domain)
    and bool(static_ip)
)

if ready:
    print("WEST_CONTAINER_APPS_ENVIRONMENT_RESULT=READY")
    print("WEST_CONTAINER_APPS_ENVIRONMENT_READY=true")
    print("NEXT_ACTION=CONFIGURE_PRIVATE_DNS")
elif state.lower() in {"failed", "canceled", "cancelled"}:
    print("WEST_CONTAINER_APPS_ENVIRONMENT_RESULT=FAILED")
    print("WEST_CONTAINER_APPS_ENVIRONMENT_READY=false")
    print("NEXT_ACTION=COLLECT_DEPLOYMENT_FAILURE")
else:
    print("WEST_CONTAINER_APPS_ENVIRONMENT_RESULT=PENDING")
    print("WEST_CONTAINER_APPS_ENVIRONMENT_READY=false")
    print("NEXT_ACTION=CHECK_STATUS_AGAIN")
PY

    PROVISIONING_STATE="$(python3 - "$JSON_FILE" <<'PY'
import json
import sys
from pathlib import Path
obj = json.loads(Path(sys.argv[1]).read_text())
props = obj.get("properties") or obj
print(obj.get("provisioningState") or props.get("provisioningState") or "")
PY
)"

    DEFAULT_DOMAIN="$(python3 - "$JSON_FILE" <<'PY'
import json
import sys
from pathlib import Path
obj = json.loads(Path(sys.argv[1]).read_text())
props = obj.get("properties") or obj
print(props.get("defaultDomain") or obj.get("defaultDomain") or "")
PY
)"

    STATIC_IP="$(python3 - "$JSON_FILE" <<'PY'
import json
import sys
from pathlib import Path
obj = json.loads(Path(sys.argv[1]).read_text())
props = obj.get("properties") or obj
print(props.get("staticIp") or obj.get("staticIp") or "")
PY
)"

    if [ "${PROVISIONING_STATE,,}" = "succeeded" ] && [ -n "$DEFAULT_DOMAIN" ] && [ -n "$STATIC_IP" ]; then
        cat >> "$STATE_FILE" <<EOF
WEST_CONTAINER_APPS_LAST_OBSERVED_STATE=$PROVISIONING_STATE
WEST_CONTAINER_APPS_DEFAULT_DOMAIN=$DEFAULT_DOMAIN
WEST_CONTAINER_APPS_STATIC_IP=$STATIC_IP
WEST_CONTAINER_APPS_READY_AT=$(date -u -Is)
EOF
        chmod 600 "$STATE_FILE"

        echo
        echo "************************************************************"
        echo "WEST CONTAINER APPS ENVIRONMENT READY"
        echo "************************************************************"
    else
        echo
        echo "************************************************************"
        echo "WEST CONTAINER APPS ENVIRONMENT DEPLOYMENT PENDING"
        echo "************************************************************"
    fi

} 2>&1 | tee "$LOG"

echo
echo "Status log: $LOG"
echo "Environment JSON: $JSON_FILE"
