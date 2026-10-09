#!/usr/bin/env bash
# Read-only Protected Test audit. Does not perform Azure mutation.
set -Eeuo pipefail
EXPECTED_SUBSCRIPTION='cd32baeb-7b71-4bc0-8ea3-9f23a50903fe'
EXPECTED_RESOURCE_GROUP='rg-project-health-dashboard-test-app-westus3'
EXPECTED_APP='ca-phd-test-api-westus3'
CURRENT_SUBSCRIPTION="$(az account show --query id -o tsv --only-show-errors)"
[[ "$CURRENT_SUBSCRIPTION" == "$EXPECTED_SUBSCRIPTION" ]] || { echo 'DENIED: wrong Azure subscription' >&2; exit 1; }
python3 scripts/resilience/test-core-test-revision-snapshot.py
az containerapp show --only-show-errors --resource-group "$EXPECTED_RESOURCE_GROUP" --name "$EXPECTED_APP" -o json | python3 scripts/resilience/verify-core-test-revision-snapshot.py
echo 'PULSE_CORE_TEST_SNAPSHOT_AUDIT=PASS'
echo 'AZURE_MUTATION=NONE'
