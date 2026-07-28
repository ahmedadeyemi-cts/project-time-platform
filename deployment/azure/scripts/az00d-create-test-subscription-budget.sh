#!/usr/bin/env bash
set -Eeuo pipefail

# Creates or updates a free Azure Cost Management budget for the test subscription.
# This script creates no billable infrastructure.

BUDGET_NAME="project-health-dashboard-test-monthly-200"
BUDGET_AMOUNT="200"
BUDGET_EMAIL="${BUDGET_EMAIL:-Ahmed.Adeyemi@ussignal.com}"
API_VERSION="2024-08-01"

SUBSCRIPTION_ID="$(az account show --query id --output tsv)"
SUBSCRIPTION_NAME="$(az account show --query name --output tsv)"
SCOPE="/subscriptions/$SUBSCRIPTION_ID"
URI="https://management.azure.com${SCOPE}/providers/Microsoft.Consumption/budgets/${BUDGET_NAME}?api-version=${API_VERSION}"
START_DATE="$(date -u +%Y-%m-01T00:00:00Z)"
END_DATE="$(date -u -d '+10 years' +%Y-12-31T00:00:00Z)"
PAYLOAD="$(mktemp)"
trap 'rm -f "$PAYLOAD"' EXIT

python3 - "$PAYLOAD" "$BUDGET_AMOUNT" "$BUDGET_EMAIL" "$START_DATE" "$END_DATE" <<'PY'
import json
import sys
from pathlib import Path

path, amount, email, start, end = sys.argv[1:]
notifications = {
    "Actual_GreaterThan_75_Percent": {
        "enabled": True,
        "operator": "GreaterThan",
        "threshold": 75,
        "thresholdType": "Actual",
        "contactEmails": [email],
        "contactRoles": [],
        "contactGroups": [],
        "locale": "en-us",
    },
    "Actual_GreaterThan_90_Percent": {
        "enabled": True,
        "operator": "GreaterThan",
        "threshold": 90,
        "thresholdType": "Actual",
        "contactEmails": [email],
        "contactRoles": [],
        "contactGroups": [],
        "locale": "en-us",
    },
    "Actual_GreaterThan_97_5_Percent": {
        "enabled": True,
        "operator": "GreaterThan",
        "threshold": 97.5,
        "thresholdType": "Actual",
        "contactEmails": [email],
        "contactRoles": [],
        "contactGroups": [],
        "locale": "en-us",
    },
    "Actual_GreaterThan_100_Percent": {
        "enabled": True,
        "operator": "GreaterThan",
        "threshold": 100,
        "thresholdType": "Actual",
        "contactEmails": [email],
        "contactRoles": [],
        "contactGroups": [],
        "locale": "en-us",
    },
    "Forecast_GreaterThan_90_Percent": {
        "enabled": True,
        "operator": "GreaterThan",
        "threshold": 90,
        "thresholdType": "Forecasted",
        "contactEmails": [email],
        "contactRoles": [],
        "contactGroups": [],
        "locale": "en-us",
    },
}

payload = {
    "properties": {
        "amount": float(amount),
        "category": "Cost",
        "timeGrain": "Monthly",
        "timePeriod": {"startDate": start, "endDate": end},
        "notifications": notifications,
    }
}

Path(path).write_text(json.dumps(payload, indent=2) + "\n")
PY

echo "Subscription: $SUBSCRIPTION_NAME"
echo "Subscription ID: $SUBSCRIPTION_ID"
echo "Monthly budget: USD $BUDGET_AMOUNT"
echo "Notification email: $BUDGET_EMAIL"

az rest \
  --method put \
  --uri "$URI" \
  --headers Content-Type=application/json \
  --body @"$PAYLOAD" \
  --query '{Name:name,Amount:properties.amount,TimeGrain:properties.timeGrain,Start:properties.timePeriod.startDate,End:properties.timePeriod.endDate,CurrentSpend:properties.currentSpend.amount,CurrentSpendUnit:properties.currentSpend.unit}' \
  --output table

echo
az rest \
  --method get \
  --uri "$URI" \
  --query 'properties.notifications' \
  --output json

echo
printf '%s\n' 'TEST SUBSCRIPTION USD 200 BUDGET READY'
