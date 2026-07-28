#!/usr/bin/env bash
set -Eeuo pipefail

MODE="${1:-discover}"
LOCATION="eastus"
VM_SIZE="Standard_D2alds_v7"
REQUESTED_LIMIT="2"
API_VERSION="2025-09-01"

BASE_DIR="$HOME/project-health-dashboard-azure"
LOG_DIR="$BASE_DIR/logs"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
LOG="$LOG_DIR/az05c2a8-eastus-daldsv7-quota-$MODE-$STAMP.log"

mkdir -p "$LOG_DIR"

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

case "$MODE" in
    discover|--discover)
        MODE="discover"
        ;;
    submit|--submit)
        MODE="submit"
        ;;
    *)
        fail "Usage: $0 [--discover|--submit]"
        ;;
esac

TMP_DIR="$(mktemp -d)"
trap 'rm -rf "$TMP_DIR"' EXIT

{
    section "AZ-05C2A8 - East US Daldsv7 Quota Discovery and Request"

    SUBSCRIPTION_ID="$(az account show --query id --output tsv)"
    SUBSCRIPTION_NAME="$(az account show --query name --output tsv)"
    SCOPE="/subscriptions/$SUBSCRIPTION_ID/providers/Microsoft.Compute/locations/$LOCATION"

    echo "TIME=$(date -u -Is)"
    echo "MODE=$MODE"
    echo "Subscription: $SUBSCRIPTION_NAME"
    echo "Subscription ID: $SUBSCRIPTION_ID"
    echo "Location: $LOCATION"
    echo "VM size: $VM_SIZE"
    echo "Requested family limit: $REQUESTED_LIMIT vCPUs"
    echo "Quota scope: $SCOPE"

    section "Validating providers and quota extension"

    for provider in Microsoft.Compute Microsoft.Quota; do
        state="$(az provider show --namespace "$provider" --query registrationState --output tsv)"
        echo "${provider}_STATE=$state"
        [ "$state" = "Registered" ] || fail "$provider is not Registered."
    done

    if ! az extension show --name quota --output none >/dev/null 2>&1; then
        fail "Azure CLI quota extension is not installed. Run: az extension add --name quota --yes"
    fi

    echo "AZURE_CLI_QUOTA_EXTENSION_VERSION=$(az extension show --name quota --query version --output tsv)"

    section "Resolving the exact Azure VM-family identifier"

    SKU_JSON="$TMP_DIR/sku.json"
    az vm list-skus \
        --location "$LOCATION" \
        --size "$VM_SIZE" \
        --all \
        --output json > "$SKU_JSON"

    SKU_FAMILY="$(python3 - "$SKU_JSON" "$VM_SIZE" <<'PY'
import json
import sys
from pathlib import Path

path = Path(sys.argv[1])
vm_size = sys.argv[2]
items = json.loads(path.read_text())

for item in items:
    if item.get("name") == vm_size:
        family = item.get("family") or ""
        if family:
            print(family)
            raise SystemExit(0)

raise SystemExit(1)
PY
    )" || fail "Could not resolve the VM family for $VM_SIZE in $LOCATION."

    echo "SKU_FAMILY=$SKU_FAMILY"

    section "Reading all East US Compute quota records"

    ALL_QUOTAS_JSON="$TMP_DIR/all-quotas.json"
    printf '[]\n' > "$ALL_QUOTAS_JSON"

    NEXT_URL="https://management.azure.com${SCOPE}/providers/Microsoft.Quota/quotas?api-version=${API_VERSION}"
    PAGE_NUMBER=0

    while [ -n "$NEXT_URL" ]; do
        PAGE_NUMBER=$((PAGE_NUMBER + 1))
        [ "$PAGE_NUMBER" -le 25 ] || fail "Quota pagination exceeded 25 pages."

        PAGE_JSON="$TMP_DIR/quota-page-${PAGE_NUMBER}.json"
        az rest \
            --method get \
            --url "$NEXT_URL" \
            --output json > "$PAGE_JSON"

        python3 - "$ALL_QUOTAS_JSON" "$PAGE_JSON" <<'PY'
import json
import sys
from pathlib import Path

all_path = Path(sys.argv[1])
page_path = Path(sys.argv[2])

all_items = json.loads(all_path.read_text())
page = json.loads(page_path.read_text())
page_items = page.get("value") or []

if not isinstance(page_items, list):
    raise SystemExit("Quota page value is not a list")

all_items.extend(page_items)
all_path.write_text(json.dumps(all_items))
PY

        NEXT_URL="$(python3 - "$PAGE_JSON" <<'PY'
import json
import sys
from pathlib import Path

page = json.loads(Path(sys.argv[1]).read_text())
print(page.get("nextLink") or "")
PY
        )"
    done

    QUOTA_COUNT="$(python3 - "$ALL_QUOTAS_JSON" <<'PY'
import json
import sys
from pathlib import Path
print(len(json.loads(Path(sys.argv[1]).read_text())))
PY
    )"
    echo "QUOTA_RECORD_COUNT=$QUOTA_COUNT"

    section "Matching the VM family to the exact quota resource name"

    MATCH_JSON="$TMP_DIR/match.json"
    python3 - "$ALL_QUOTAS_JSON" "$SKU_FAMILY" > "$MATCH_JSON" <<'PY'
import json
import re
import sys
from pathlib import Path

items = json.loads(Path(sys.argv[1]).read_text())
wanted = sys.argv[2]

def norm(value):
    return re.sub(r"[^a-z0-9]", "", str(value or "").lower())

wanted_norm = norm(wanted)
matches = []

for item in items:
    props = item.get("properties") or {}
    resource_name = item.get("name") or ""
    prop_name = props.get("name") or {}
    value_name = prop_name.get("value") or ""
    localized_name = prop_name.get("localizedValue") or ""

    candidates = [resource_name, value_name]
    if any(norm(candidate) == wanted_norm for candidate in candidates if candidate):
        limit_obj = props.get("limit") or {}
        matches.append({
            "resourceName": resource_name or value_name,
            "valueName": value_name,
            "localizedName": localized_name,
            "currentLimit": limit_obj.get("value"),
            "isQuotaApplicable": props.get("isQuotaApplicable"),
            "unit": props.get("unit"),
            "id": item.get("id") or "",
        })

if len(matches) != 1:
    print(json.dumps({"matchCount": len(matches), "matches": matches}, indent=2))
    raise SystemExit(2)

print(json.dumps(matches[0], indent=2))
PY
    MATCH_STATUS=$?

    if [ "$MATCH_STATUS" -ne 0 ]; then
        cat "$MATCH_JSON"
        fail "Expected exactly one exact quota match for SKU family $SKU_FAMILY."
    fi

    cat "$MATCH_JSON"

    QUOTA_RESOURCE_NAME="$(python3 - "$MATCH_JSON" <<'PY'
import json
import sys
from pathlib import Path
print(json.loads(Path(sys.argv[1]).read_text()).get("resourceName") or "")
PY
    )"
    CURRENT_LIMIT="$(python3 - "$MATCH_JSON" <<'PY'
import json
import sys
from pathlib import Path
value = json.loads(Path(sys.argv[1]).read_text()).get("currentLimit")
print("" if value is None else value)
PY
    )"
    QUOTA_APPLICABLE="$(python3 - "$MATCH_JSON" <<'PY'
import json
import sys
from pathlib import Path
value = json.loads(Path(sys.argv[1]).read_text()).get("isQuotaApplicable")
print("" if value is None else str(value).lower())
PY
    )"

    [ -n "$QUOTA_RESOURCE_NAME" ] || fail "Exact quota resource name is empty."

    echo "EXACT_QUOTA_RESOURCE_NAME=$QUOTA_RESOURCE_NAME"
    echo "CURRENT_QUOTA_LIMIT=${CURRENT_LIMIT:-unknown}"
    echo "QUOTA_APPLICABLE=${QUOTA_APPLICABLE:-unknown}"

    if [ "$QUOTA_APPLICABLE" = "false" ]; then
        fail "Azure reports that this quota is not applicable to the subscription."
    fi

    if [[ "$CURRENT_LIMIT" =~ ^[0-9]+$ ]] && [ "$CURRENT_LIMIT" -ge "$REQUESTED_LIMIT" ]; then
        echo "QUOTA_DECISION=DEPLOYMENT_ALLOWED"
        echo "APPROVED_QUOTA_LIMIT=$CURRENT_LIMIT"
        echo "RECOMMENDED_VM_SIZE=$VM_SIZE"
        echo
        echo "************************************************************"
        echo "EASTUS DALDSV7 COMPUTE QUOTA READY"
        echo "************************************************************"
        exit 0
    fi

    if [ "$MODE" = "discover" ]; then
        echo "QUOTA_DECISION=READY_TO_REQUEST"
        echo "REQUESTED_QUOTA_LIMIT=$REQUESTED_LIMIT"
        echo "No Azure resource or quota was changed."
        echo
        echo "To submit the exact discovered quota request, rerun this script with --submit."
        echo
        echo "************************************************************"
        echo "EASTUS DALDSV7 EXACT QUOTA RESOURCE DISCOVERED"
        echo "************************************************************"
        exit 0
    fi

    section "Submitting the exact quota request without polling"

    az quota update \
        --resource-name "$QUOTA_RESOURCE_NAME" \
        --scope "$SCOPE" \
        --limit-object "value=$REQUESTED_LIMIT" \
        --resource-type dedicated \
        --no-wait true \
        --only-show-errors \
        --output none

    echo "QUOTA_REQUEST_ACTION=submitted"
    echo "QUOTA_REQUEST_RESOURCE_NAME=$QUOTA_RESOURCE_NAME"
    echo "REQUESTED_QUOTA_LIMIT=$REQUESTED_LIMIT"
    echo "QUOTA_DECISION=REQUEST_SUBMITTED"
    echo "No VM was created."
    echo
    echo "************************************************************"
    echo "EASTUS DALDSV7 QUOTA REQUEST SUBMITTED"
    echo "************************************************************"

} 2>&1 | tee "$LOG"

echo
echo "Quota workflow log: $LOG"
