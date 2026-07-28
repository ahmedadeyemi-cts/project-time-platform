#!/usr/bin/env bash
set -Eeuo pipefail

EXPECTED_RELEASE_COMMIT="e678cdaacc020ccd2ee7726d6e77f0276fae38ce"
RESOURCE_GROUP="${AZURE_RESOURCE_GROUP:-}"
API_APP="${AZURE_API_APP:-}"
ACR_NAME="${AZURE_ACR_NAME:-}"
MIGRATION_IMAGE="${OPEN_PR_RECONCILIATION_MIGRATION_IMAGE:-}"
DATABASE_URL="${PROJECTPULSE_TEST_DATABASE_URL:-}"
JOB_NAME="${OPEN_PR_RECONCILIATION_MIGRATION_JOB_NAME:-}"

fail() { echo "ERROR: $*" >&2; exit 1; }
normalize_az_value() { case "${1:-}" in ""|None|null) return 1 ;; *) printf '%s\n' "$1" ;; esac; }
mask_value() { local value="$1"; value="${value//%/%25}"; printf '::add-mask::%s\n' "$value"; }

[[ -n "$RESOURCE_GROUP" ]] || fail "AZURE_RESOURCE_GROUP is not configured."
[[ -n "$API_APP" ]] || fail "AZURE_API_APP is not configured."
[[ -n "$ACR_NAME" ]] || fail "AZURE_ACR_NAME is not configured."
[[ -n "$MIGRATION_IMAGE" ]] || fail "OPEN_PR_RECONCILIATION_MIGRATION_IMAGE is not configured."
[[ -n "$DATABASE_URL" ]] || fail "PROJECTPULSE_TEST_DATABASE_URL is not configured."
[[ "$JOB_NAME" =~ ^[a-z][a-z0-9-]{0,30}[a-z0-9]$ ]] ||
  fail "OPEN_PR_RECONCILIATION_MIGRATION_JOB_NAME must be a valid name no longer than 32 characters."
[[ "$MIGRATION_IMAGE" == "$ACR_NAME.azurecr.io/"*@sha256:* ]] ||
  fail "The migration image must be an immutable digest from the approved ACR."

mask_value "$DATABASE_URL"
ENVIRONMENT_ID="$(az containerapp show \
  --resource-group "$RESOURCE_GROUP" \
  --name "$API_APP" \
  --query properties.managedEnvironmentId \
  --output tsv \
  --only-show-errors)"
normalize_az_value "$ENVIRONMENT_ID" >/dev/null ||
  fail "The test API app does not expose a managed Container Apps environment."

if az containerapp job show \
  --resource-group "$RESOURCE_GROUP" \
  --name "$JOB_NAME" \
  --output none \
  --only-show-errors 2>/dev/null; then
  fail "A Container Apps Job already exists with the guarded name $JOB_NAME."
fi

ACR_SERVER="$ACR_NAME.azurecr.io"
REGISTRY_USERNAME="00000000-0000-0000-0000-000000000000"
REGISTRY_PASSWORD="$(az acr login \
  --name "$ACR_NAME" \
  --expose-token \
  --query accessToken \
  --output tsv \
  --only-show-errors)"
normalize_az_value "$REGISTRY_PASSWORD" >/dev/null ||
  fail "Azure did not issue a short-lived ACR access token."
mask_value "$REGISTRY_USERNAME"
mask_value "$REGISTRY_PASSWORD"
echo "OPEN_PR_RECONCILIATION_MIGRATION_REGISTRY_AUTH=EPHEMERAL_AZURE_TOKEN"

JOB_CREATED=0
cleanup() {
  local status=$?
  trap - EXIT INT TERM
  unset DATABASE_URL REGISTRY_PASSWORD
  if (( JOB_CREATED == 1 )); then
    echo "OPEN_PR_RECONCILIATION_MIGRATION_JOB_CLEANUP=STARTED"
    az containerapp job delete \
      --resource-group "$RESOURCE_GROUP" \
      --name "$JOB_NAME" \
      --yes \
      --output none \
      --only-show-errors || true
    echo "OPEN_PR_RECONCILIATION_MIGRATION_JOB_CLEANUP=COMPLETE"
  fi
  exit "$status"
}
trap cleanup EXIT INT TERM

az containerapp job create \
  --resource-group "$RESOURCE_GROUP" \
  --name "$JOB_NAME" \
  --environment "$ENVIRONMENT_ID" \
  --trigger-type Manual \
  --replica-timeout 1200 \
  --replica-retry-limit 0 \
  --replica-completion-count 1 \
  --parallelism 1 \
  --image "$MIGRATION_IMAGE" \
  --cpu 0.25 \
  --memory 0.5Gi \
  --registry-server "$ACR_SERVER" \
  --registry-username "$REGISTRY_USERNAME" \
  --registry-password "$REGISTRY_PASSWORD" \
  --secrets "projectpulse-db-url=$DATABASE_URL" \
  --env-vars PROJECTPULSE_TEST_DATABASE_URL=secretref:projectpulse-db-url \
  --tags projectpulse-scope=open-pr-reconciliation-test projectpulse-release="$EXPECTED_RELEASE_COMMIT" projectpulse-migration=049 \
  --output none \
  --only-show-errors
JOB_CREATED=1
unset REGISTRY_PASSWORD DATABASE_URL

echo "OPEN_PR_RECONCILIATION_MIGRATION_JOB_CREATE=COMPLETE"
EXECUTION_NAME="$(az containerapp job start \
  --resource-group "$RESOURCE_GROUP" \
  --name "$JOB_NAME" \
  --query name \
  --output tsv \
  --only-show-errors)"
normalize_az_value "$EXECUTION_NAME" >/dev/null ||
  fail "Azure did not return the migration job execution name."
echo "OPEN_PR_RECONCILIATION_MIGRATION_JOB_EXECUTION=$EXECUTION_NAME"

EXECUTION_STATUS=""
for attempt in $(seq 1 120); do
  EXECUTION_STATUS="$(az containerapp job execution list \
    --resource-group "$RESOURCE_GROUP" \
    --name "$JOB_NAME" \
    --query "[?name=='$EXECUTION_NAME'].properties.status | [0]" \
    --output tsv \
    --only-show-errors)"
  case "$EXECUTION_STATUS" in
    Succeeded)
      echo "OPEN_PR_RECONCILIATION_MIGRATION_JOB_STATUS=Succeeded"
      exit 0
      ;;
    Failed|Canceled)
      echo "OPEN_PR_RECONCILIATION_MIGRATION_JOB_STATUS=$EXECUTION_STATUS" >&2
      break
      ;;
  esac
  sleep 5
done

az containerapp job logs show \
  --resource-group "$RESOURCE_GROUP" \
  --name "$JOB_NAME" \
  --execution "$EXECUTION_NAME" \
  --container "$JOB_NAME" \
  --tail 250 \
  --only-show-errors >&2 || true
fail "The private-network migration 049 job did not succeed."
