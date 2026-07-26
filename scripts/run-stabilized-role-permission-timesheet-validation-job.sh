#!/usr/bin/env bash
set -Eeuo pipefail

EXPECTED_RELEASE_COMMIT="2216bfadaca76858fe07e8d1228df888688fd786"
RESOURCE_GROUP="${AZURE_RESOURCE_GROUP:-}"
API_APP="${AZURE_API_APP:-}"
ACR_NAME="${AZURE_ACR_NAME:-}"
VALIDATION_IMAGE="${STABILIZED_VALIDATION_IMAGE:-}"
DATABASE_URL="${PROJECTPULSE_TEST_DATABASE_URL:-}"
JOB_NAME="${STABILIZED_VALIDATION_JOB_NAME:-}"

fail() { echo "ERROR: $*" >&2; exit 1; }
normalize() { case "${1:-}" in ""|None|null) return 1 ;; *) printf '%s\n' "$1" ;; esac; }
mask() { local value="${1//%/%25}"; printf '::add-mask::%s\n' "$value"; }

[[ -n "$RESOURCE_GROUP" ]] || fail "AZURE_RESOURCE_GROUP is not configured."
[[ -n "$API_APP" ]] || fail "AZURE_API_APP is not configured."
[[ -n "$ACR_NAME" ]] || fail "AZURE_ACR_NAME is not configured."
[[ -n "$VALIDATION_IMAGE" ]] || fail "STABILIZED_VALIDATION_IMAGE is not configured."
[[ -n "$DATABASE_URL" ]] || fail "PROJECTPULSE_TEST_DATABASE_URL is not configured."
[[ "$JOB_NAME" =~ ^[a-z][a-z0-9-]{0,30}[a-z0-9]$ ]] || fail "Invalid Container Apps Job name: $JOB_NAME"
[[ "$VALIDATION_IMAGE" == "$ACR_NAME.azurecr.io/"*@sha256:* ]] || fail "Validation image must use an immutable digest from the approved ACR."
mask "$DATABASE_URL"

ENVIRONMENT_ID="$(az containerapp show -g "$RESOURCE_GROUP" -n "$API_APP" --query properties.managedEnvironmentId -o tsv --only-show-errors)"
normalize "$ENVIRONMENT_ID" >/dev/null || fail "The API app has no managed Container Apps environment."
if az containerapp job show -g "$RESOURCE_GROUP" -n "$JOB_NAME" --output none --only-show-errors 2>/dev/null; then
  fail "A validation job already exists with name $JOB_NAME."
fi

ACR_SERVER="$ACR_NAME.azurecr.io"
REGISTRY_USERNAME="00000000-0000-0000-0000-000000000000"
REGISTRY_PASSWORD="$(az acr login --name "$ACR_NAME" --expose-token --query accessToken -o tsv --only-show-errors)"
normalize "$REGISTRY_PASSWORD" >/dev/null || fail "Azure did not issue an ephemeral ACR token."
mask "$REGISTRY_USERNAME"
mask "$REGISTRY_PASSWORD"

echo 'STABILIZED_VALIDATION_REGISTRY_AUTH=EPHEMERAL_AZURE_TOKEN'
JOB_CREATED=0
cleanup() {
  local status=$?
  trap - EXIT INT TERM
  unset DATABASE_URL REGISTRY_PASSWORD
  if (( JOB_CREATED == 1 )); then
    az containerapp job delete -g "$RESOURCE_GROUP" -n "$JOB_NAME" --yes --output none --only-show-errors || true
    echo 'STABILIZED_VALIDATION_JOB_CLEANUP=COMPLETE'
  fi
  exit "$status"
}
trap cleanup EXIT INT TERM

az containerapp job create \
  -g "$RESOURCE_GROUP" \
  -n "$JOB_NAME" \
  --environment "$ENVIRONMENT_ID" \
  --trigger-type Manual \
  --replica-timeout 900 \
  --replica-retry-limit 0 \
  --replica-completion-count 1 \
  --parallelism 1 \
  --image "$VALIDATION_IMAGE" \
  --cpu 0.25 \
  --memory 0.5Gi \
  --registry-server "$ACR_SERVER" \
  --registry-username "$REGISTRY_USERNAME" \
  --registry-password "$REGISTRY_PASSWORD" \
  --secrets "stabilized-db-url=$DATABASE_URL" \
  --env-vars PROJECTPULSE_TEST_DATABASE_URL=secretref:stabilized-db-url \
  --tags projectpulse-scope=stabilized-role-permission-timesheet projectpulse-release="$EXPECTED_RELEASE_COMMIT" \
  --output none --only-show-errors
JOB_CREATED=1
unset REGISTRY_PASSWORD DATABASE_URL

EXECUTION_NAME="$(az containerapp job start -g "$RESOURCE_GROUP" -n "$JOB_NAME" --query name -o tsv --only-show-errors)"
normalize "$EXECUTION_NAME" >/dev/null || fail "Azure did not return a validation execution name."
echo "STABILIZED_VALIDATION_JOB_EXECUTION=$EXECUTION_NAME"

for attempt in $(seq 1 90); do
  STATUS="$(az containerapp job execution list -g "$RESOURCE_GROUP" -n "$JOB_NAME" --query "[?name=='$EXECUTION_NAME'].properties.status | [0]" -o tsv --only-show-errors)"
  case "$STATUS" in
    Succeeded)
      echo 'STABILIZED_VALIDATION_JOB_STATUS=Succeeded'
      az containerapp job logs show -g "$RESOURCE_GROUP" -n "$JOB_NAME" --execution "$EXECUTION_NAME" --container "$JOB_NAME" --tail 200 --only-show-errors || true
      exit 0
      ;;
    Failed|Canceled)
      echo "STABILIZED_VALIDATION_JOB_STATUS=$STATUS" >&2
      break
      ;;
  esac
  sleep 5
done

az containerapp job logs show -g "$RESOURCE_GROUP" -n "$JOB_NAME" --execution "$EXECUTION_NAME" --container "$JOB_NAME" --tail 200 --only-show-errors >&2 || true
fail "The private stabilized role and timesheet data validation job did not succeed."
