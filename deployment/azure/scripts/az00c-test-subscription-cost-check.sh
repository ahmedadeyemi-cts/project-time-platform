#!/usr/bin/env bash
set -Eeuo pipefail

# Read-only daily Azure Cost Management review for the Project Health Dashboard
# test subscription. This script creates, updates, and deletes no Azure resources.

BUDGET_USD=200
WARNING_USD=150
CRITICAL_USD=180
EMERGENCY_USD=195
API_VERSION="2025-03-01"

BASE_DIR="$HOME/project-health-dashboard-azure"
LOG_DIR="$BASE_DIR/logs"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
LOG="$LOG_DIR/az00c-test-subscription-cost-check-$STAMP.log"
WORK_DIR="$(mktemp -d)"

mkdir -p "$LOG_DIR"
trap 'rm -rf "$WORK_DIR"' EXIT

section() {
    echo
    echo "============================================================"
    echo "$1"
    echo "============================================================"
}

SUBSCRIPTION_ID="$(az account show --query id --output tsv)"
SUBSCRIPTION_NAME="$(az account show --query name --output tsv)"
SCOPE="/subscriptions/$SUBSCRIPTION_ID"
QUERY_URI="https://management.azure.com${SCOPE}/providers/Microsoft.CostManagement/query?api-version=${API_VERSION}"
FORECAST_URI="https://management.azure.com${SCOPE}/providers/Microsoft.CostManagement/forecast?api-version=${API_VERSION}"
MONTH_START="$(date -u +%Y-%m-01T00:00:00Z)"
NEXT_MONTH_START="$(date -u -d "$(date -u +%Y-%m-01) +1 month" +%Y-%m-%dT00:00:00Z)"

ACTUAL_STATUS_FILE="$WORK_DIR/actual-status.txt"
ACTUAL_TOTAL_FILE="$WORK_DIR/actual-total.txt"
FORECAST_STATUS_FILE="$WORK_DIR/forecast-status.txt"
FORECAST_TOTAL_FILE="$WORK_DIR/forecast-total.txt"
printf '%s\n' unavailable > "$ACTUAL_STATUS_FILE"
printf '%s\n' 0 > "$ACTUAL_TOTAL_FILE"
printf '%s\n' unavailable > "$FORECAST_STATUS_FILE"
printf '%s\n' 0 > "$FORECAST_TOTAL_FILE"

cat > "$WORK_DIR/actual-request.json" <<'JSON'
{
  "type": "ActualCost",
  "timeframe": "MonthToDate",
  "dataset": {
    "granularity": "None",
    "aggregation": {
      "totalCost": {"name": "PreTaxCost", "function": "Sum"}
    },
    "grouping": [
      {"type": "Dimension", "name": "ResourceGroup"}
    ]
  }
}
JSON

python3 - "$MONTH_START" "$NEXT_MONTH_START" "$WORK_DIR/forecast-request.json" <<'PY'
import json
import sys
from pathlib import Path

start, end, output = sys.argv[1:]
payload = {
    "type": "ActualCost",
    "timeframe": "Custom",
    "timePeriod": {"from": start, "to": end},
    "includeActualCost": True,
    "includeFreshPartialCost": True,
    "dataset": {
        "granularity": "Daily",
        "aggregation": {
            "totalCost": {"name": "PreTaxCost", "function": "Sum"}
        },
    },
}
Path(output).write_text(json.dumps(payload))
PY

{
    section "AZ-00C - Test Subscription Daily Cost Review"
    echo "Subscription: $SUBSCRIPTION_NAME"
    echo "Subscription ID: $SUBSCRIPTION_ID"
    echo "Monthly credit ceiling: USD $BUDGET_USD"
    echo "Warning threshold: USD $WARNING_USD"
    echo "Critical threshold: USD $CRITICAL_USD"
    echo "Emergency threshold: USD $EMERGENCY_USD"
    echo "Build policy: continue with daily cost monitoring"
    echo "TIME=$(date -u -Is)"

    section "Month-to-date actual cost by resource group"

    if az rest \
        --method post \
        --uri "$QUERY_URI" \
        --headers Content-Type=application/json \
        --body @"$WORK_DIR/actual-request.json" \
        --output json > "$WORK_DIR/actual-response.json"; then

        python3 - "$WORK_DIR/actual-response.json" \
            "$ACTUAL_STATUS_FILE" "$ACTUAL_TOTAL_FILE" <<'PY'
import json
import sys
from pathlib import Path

response, status_path, total_path = sys.argv[1:]
data = json.loads(Path(response).read_text())
props = data.get("properties") or {}
columns = [column.get("name") for column in props.get("columns") or []]
rows = props.get("rows") or []

if not rows:
    print("No cost rows were returned. Cost data has not posted yet.")
    raise SystemExit(0)

cost_index = columns.index("PreTaxCost") if "PreTaxCost" in columns else 0
rg_index = columns.index("ResourceGroup") if "ResourceGroup" in columns else None
currency_index = columns.index("Currency") if "Currency" in columns else None

total = 0.0
parsed = []
for row in rows:
    cost = float(row[cost_index] or 0)
    total += cost
    resource_group = str(row[rg_index] or "(unassigned)") if rg_index is not None else "(all)"
    currency = str(row[currency_index] or "USD") if currency_index is not None else "USD"
    parsed.append((cost, resource_group, currency))

for cost, resource_group, currency in sorted(parsed, reverse=True):
    print(f"{resource_group:70} {cost:12.2f} {currency}")

currency = parsed[0][2] if parsed else "USD"
print()
print(f"MONTH_TO_DATE_ACTUAL={total:.2f}")
print(f"ACTUAL_CURRENCY={currency}")
Path(status_path).write_text("available\n")
Path(total_path).write_text(f"{total:.2f}\n")
PY
    else
        echo "WARNING: Azure actual-cost query failed."
        echo "Do not treat a failed query as zero cost."
    fi

    section "Azure forecast through end of current month"

    if az rest \
        --method post \
        --uri "$FORECAST_URI" \
        --headers Content-Type=application/json \
        --body @"$WORK_DIR/forecast-request.json" \
        --output json > "$WORK_DIR/forecast-response.json" \
        2> "$WORK_DIR/forecast-error.txt"; then

        python3 - "$WORK_DIR/forecast-response.json" \
            "$FORECAST_STATUS_FILE" "$FORECAST_TOTAL_FILE" <<'PY'
import json
import sys
from pathlib import Path

response, status_path, total_path = sys.argv[1:]
data = json.loads(Path(response).read_text())
props = data.get("properties") or {}
columns = [column.get("name") for column in props.get("columns") or []]
rows = props.get("rows") or []

if not rows:
    print("No forecast rows were returned.")
    raise SystemExit(0)

cost_index = columns.index("PreTaxCost") if "PreTaxCost" in columns else 0
status_index = columns.index("CostStatus") if "CostStatus" in columns else None
currency_index = columns.index("Currency") if "Currency" in columns else None
actual = 0.0
forecast = 0.0
currency = "USD"

for row in rows:
    cost = float(row[cost_index] or 0)
    status = str(row[status_index] or "Forecast") if status_index is not None else "Forecast"
    if currency_index is not None and row[currency_index]:
        currency = str(row[currency_index])
    if status.lower() == "actual":
        actual += cost
    else:
        forecast += cost

month_total = actual + forecast
print(f"FORECAST_INCLUDED_ACTUAL={actual:.2f}")
print(f"FORECAST_REMAINING={forecast:.2f}")
print(f"FORECAST_MONTH_TOTAL={month_total:.2f}")
print(f"FORECAST_CURRENCY={currency}")
Path(status_path).write_text("available\n")
Path(total_path).write_text(f"{month_total:.2f}\n")
PY
    else
        echo "Forecast is unavailable for this offer or billing scope."
        sed -n '1,20p' "$WORK_DIR/forecast-error.txt"
    fi

    section "High-cost resource inventory"

    echo "PostgreSQL Flexible Servers:"
    az postgres flexible-server list \
        --query "[].{Name:name,Location:location,State:state,Tier:sku.tier,SKU:sku.name,HA:highAvailability.mode,StorageGiB:storage.storageSizeGb}" \
        --output table

    echo
    echo "Container Registries:"
    az acr list \
        --query "[].{Name:name,Location:location,SKU:sku.name,ZoneRedundant:zoneRedundancy}" \
        --output table

    mapfile -t ACR_NAMES < <(az acr list --query '[].name' --output tsv)
    for registry in "${ACR_NAMES[@]}"; do
        [ -n "$registry" ] || continue
        replication_count="$(
            az acr replication list \
                --registry "$registry" \
                --query 'length(@)' \
                --output tsv 2>/dev/null || echo unknown
        )"
        echo "ACR_REPLICATION_COUNT[$registry]=$replication_count"
    done

    echo
    echo "NAT Gateways:"
    az network nat gateway list \
        --query "[].{Name:name,ResourceGroup:resourceGroup,Location:location,State:provisioningState}" \
        --output table

    echo
    echo "Public IP addresses:"
    az network public-ip list \
        --query "[?starts_with(resourceGroup, 'rg-project-health-dashboard')].{Name:name,ResourceGroup:resourceGroup,Location:location,SKU:sku.name,Allocation:publicIPAllocationMethod,IPAddress:ipAddress}" \
        --output table

    echo
    echo "Private endpoints:"
    az network private-endpoint list \
        --query "[?starts_with(resourceGroup, 'rg-project-health-dashboard')].{Name:name,ResourceGroup:resourceGroup,Location:location,State:provisioningState}" \
        --output table

    echo
    echo "Virtual Machines:"
    az vm list -d \
        --query "[].{Name:name,ResourceGroup:resourceGroup,Location:location,Size:hardwareProfile.vmSize,PowerState:powerState,PublicIP:publicIps}" \
        --output table

    section "Daily cost decision"

    ACTUAL_STATUS="$(cat "$ACTUAL_STATUS_FILE")"
    ACTUAL_TOTAL="$(cat "$ACTUAL_TOTAL_FILE")"
    FORECAST_STATUS="$(cat "$FORECAST_STATUS_FILE")"
    FORECAST_TOTAL="$(cat "$FORECAST_TOTAL_FILE")"

    echo "ACTUAL_COST_DATA=$ACTUAL_STATUS"
    echo "FORECAST_COST_DATA=$FORECAST_STATUS"

    python3 - "$ACTUAL_STATUS" "$ACTUAL_TOTAL" "$FORECAST_STATUS" \
        "$FORECAST_TOTAL" "$WARNING_USD" "$CRITICAL_USD" \
        "$EMERGENCY_USD" "$BUDGET_USD" <<'PY'
import sys

actual_status, actual_value, forecast_status, forecast_value = sys.argv[1:5]
warning, critical, emergency, budget = map(float, sys.argv[5:9])
actual = float(actual_value)
forecast = float(forecast_value)

if actual_status != "available":
    actual_level = "DATA_PENDING"
elif actual >= budget:
    actual_level = "CEILING_REACHED"
elif actual >= emergency:
    actual_level = "EMERGENCY"
elif actual >= critical:
    actual_level = "CRITICAL"
elif actual >= warning:
    actual_level = "WARNING"
else:
    actual_level = "BELOW_WARNING_THRESHOLD"

if forecast_status != "available":
    forecast_level = "DATA_PENDING"
elif forecast >= budget:
    forecast_level = "PROJECTED_OVER_CEILING"
elif forecast >= emergency:
    forecast_level = "PROJECTED_EMERGENCY"
elif forecast >= critical:
    forecast_level = "PROJECTED_CRITICAL"
elif forecast >= warning:
    forecast_level = "PROJECTED_WARNING"
else:
    forecast_level = "PROJECTED_BELOW_WARNING_THRESHOLD"

print(f"ACTUAL_THRESHOLD_STATUS={actual_level}")
print(f"FORECAST_THRESHOLD_STATUS={forecast_level}")
print(f"REMAINING_TO_200={max(budget-actual, 0):.2f}" if actual_status == "available" else "REMAINING_TO_200=unknown")
print("BUILD_POLICY=CONTINUE_WITH_DAILY_MONITORING")
print("ROCKY_RESTORE_VM_APPROVAL=APPROVED_TEMPORARY")
PY

    echo
    echo "Continue development while reviewing this report daily."
    echo "At USD 150 begin optimization; at USD 180 perform a critical"
    echo "review; at USD 195 deallocate or delete nonessential resources;"
    echo "at USD 200 treat the monthly credit as exhausted."
    echo "Temporary compute must still be deallocated or deleted promptly."
    echo
    echo "************************************************************"
    echo "TEST SUBSCRIPTION DAILY COST REVIEW COMPLETE"
    echo "************************************************************"

} 2>&1 | tee "$LOG"

echo
echo "Cost-check log: $LOG"
