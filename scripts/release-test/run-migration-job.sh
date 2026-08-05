#!/usr/bin/env bash
set -Eeuo pipefail

EXPECTED_RELEASE_COMMIT="e83340b5a4215ea63901cea98ea17596444f96b7"
RESOURCE_GROUP="${AZURE_RESOURCE_GROUP:-}"
API_APP="${AZURE_API_APP:-}"
ACR_NAME="${AZURE_ACR_NAME:-}"
MIGRATION_IMAGE="${MAIN_RELEASE_MIGRATION_IMAGE:-}"
DATABASE_URL="${PROJECTPULSE_TEST_DATABASE_URL:-}"
JOB_NAME="${MAIN_RELEASE_MIGRATION_JOB_NAME:-}"
MODE="${MAIN_RELEASE_MIGRATION_MODE:-verify}"

fail() { echo "ERROR: $*" >&2; exit 1; }
normalize() { case "${1:-}" in ""|None|null) return 1 ;; *) printf '%s\n' "$1" ;; esac; }
mask() { local value="$1"; value="${value//%/%25}"; printf '::add-mask::%s\n' "$value"; }

[[ -n "$RESOURCE_GROUP" ]] || fail "AZURE_RESOURCE_GROUP is not configured."
[[ -n "$API_APP" ]] || fail "AZURE_API_APP is not configured."
[[ -n "$ACR_NAME" ]] || fail "AZURE_ACR_NAME is not configured."
[[ -n "$MIGRATION_IMAGE" ]] || fail "MAIN_RELEASE_MIGRATION_IMAGE is not configured."
[[ -n "$DATABASE_URL" ]] || fail "PROJECTPULSE_TEST_DATABASE_URL is not configured."
[[ "$MODE" == apply || "$MODE" == verify ]] || fail "MAIN_RELEASE_MIGRATION_MODE must be apply or verify."
[[ "$JOB_NAME" =~ ^[a-z][a-z0-9-]{0,30}[a-z0-9]$ ]] || fail "MAIN_RELEASE_MIGRATION_JOB_NAME is invalid."
[[ "$MIGRATION_IMAGE" == "$ACR_NAME.azurecr.io/"*@sha256:* ]] ||
  fail "Migration image must be an immutable digest in the approved ACR."

command -v az >/dev/null || fail "Azure CLI is required."
mask "$DATABASE_URL"

ENVIRONMENT_ID="$(az containerapp show \
  -g "$RESOURCE_GROUP" -n "$API_APP" \
  --query properties.managedEnvironmentId -o tsv --only-show-errors)"
normalize "$ENVIRONMENT_ID" >/dev/null ||
  fail "The Test API app has no managed Container Apps environment."

ACR_SERVER="$ACR_NAME.azurecr.io"
REGISTRY_IDENTITY="$(az containerapp show \
  -g "$RESOURCE_GROUP" -n "$API_APP" \
  --query "properties.configuration.registries[?server=='$ACR_SERVER'].identity | [0]" \
  -o tsv --only-show-errors)"
normalize "$REGISTRY_IDENTITY" >/dev/null ||
  fail "The Test API app does not expose an ACR managed identity."

registry_args=(--registry-server "$ACR_SERVER" --registry-identity "$REGISTRY_IDENTITY")
identity_args=()
case "${REGISTRY_IDENTITY,,}" in
  system-environment) ;;
  /subscriptions/*) identity_args+=(--mi-user-assigned "$REGISTRY_IDENTITY") ;;
  *) fail "Unsupported ACR managed identity reference." ;;
esac

JOB_CREATED=0
EXECUTION_NAME=""
cleanup() {
  local status=$?
  trap - EXIT INT TERM
  unset DATABASE_URL
  if [[ -n "$EXECUTION_NAME" ]]; then
    execution_status="$(az containerapp job execution list \
      -g "$RESOURCE_GROUP" -n "$JOB_NAME" \
      --query "[?name=='$EXECUTION_NAME'].properties.status | [0]" \
      -o tsv --only-show-errors 2>/dev/null || true)"
    case "$execution_status" in
      Succeeded|Failed|Stopped|Canceled) ;;
      *)
        az containerapp job stop -g "$RESOURCE_GROUP" -n "$JOB_NAME" \
          --job-execution-name "$EXECUTION_NAME" --output none --only-show-errors 2>/dev/null || true
        ;;
    esac
  fi
  if (( JOB_CREATED == 1 )); then
    az containerapp job delete -g "$RESOURCE_GROUP" -n "$JOB_NAME" \
      --yes --output none --only-show-errors || status=1
    if az containerapp job show -g "$RESOURCE_GROUP" -n "$JOB_NAME" \
      --output none --only-show-errors 2>/dev/null; then
      echo "ERROR: Temporary migration job still exists after cleanup." >&2
      status=1
    else
      echo "MAIN_RELEASE_MIGRATION_JOB_CLEANUP=COMPLETE"
    fi
  fi
  exit "$status"
}
trap cleanup EXIT INT TERM

if az containerapp job show -g "$RESOURCE_GROUP" -n "$JOB_NAME" \
  --output none --only-show-errors 2>/dev/null; then
  fail "A Container Apps Job already exists with guarded name $JOB_NAME."
fi

az containerapp job create \
  -g "$RESOURCE_GROUP" \
  -n "$JOB_NAME" \
  --environment "$ENVIRONMENT_ID" \
  --trigger-type Manual \
  --replica-timeout 1800 \
  --replica-retry-limit 0 \
  --replica-completion-count 1 \
  --parallelism 1 \
  --image "$MIGRATION_IMAGE" \
  --cpu 0.5 \
  --memory 1.0Gi \
  "${identity_args[@]}" \
  --secrets "main-release-db-url=$DATABASE_URL" \
  --env-vars \
    PROJECTPULSE_TEST_DATABASE_URL=secretref:main-release-db-url \
    MAIN_RELEASE_MIGRATION_MODE="$MODE" \
  --tags \
    projectpulse-scope=current-main-071-073-test \
    projectpulse-release="$EXPECTED_RELEASE_COMMIT" \
    projectpulse-mode="$MODE" \
  "${registry_args[@]}" \
  --output none \
  --only-show-errors
JOB_CREATED=1
unset DATABASE_URL

EXECUTION_NAME="$(az containerapp job start \
  -g "$RESOURCE_GROUP" -n "$JOB_NAME" \
  --query name -o tsv --only-show-errors)"
normalize "$EXECUTION_NAME" >/dev/null || fail "Azure did not return the migration execution name."
echo "MAIN_RELEASE_MIGRATION_JOB_EXECUTION=$EXECUTION_NAME mode=$MODE"

for attempt in $(seq 1 180); do
  STATUS="$(az containerapp job execution list \
    -g "$RESOURCE_GROUP" -n "$JOB_NAME" \
    --query "[?name=='$EXECUTION_NAME'].properties.status | [0]" \
    -o tsv --only-show-errors)"
  case "$STATUS" in
    Succeeded)
      echo "MAIN_RELEASE_MIGRATION_JOB_STATUS=SUCCEEDED mode=$MODE"
      exit 0
      ;;
    Failed|Stopped|Canceled|Degraded)
      echo "MAIN_RELEASE_MIGRATION_JOB_STATUS=$STATUS mode=$MODE" >&2
      break
      ;;
  esac
  sleep 5
done

az containerapp job logs show \
  -g "$RESOURCE_GROUP" -n "$JOB_NAME" \
  --execution "$EXECUTION_NAME" \
  --container "$JOB_NAME" \
  --tail 250 --only-show-errors >&2 || true
fail "The private-network migration job did not succeed in $MODE mode."
