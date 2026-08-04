#!/usr/bin/env bash
set -Eeuo pipefail

EXPECTED_RELEASE_COMMIT="a536e33b48c41bf1dd867d7319e88f98e8aa152c"
RESOURCE_GROUP="${AZURE_RESOURCE_GROUP:-}"
API_APP="${AZURE_API_APP:-}"
ACR_NAME="${AZURE_ACR_NAME:-}"
MIGRATION_IMAGE="${PROJECTPULSE_PR467_MIGRATION_IMAGE:-}"
DATABASE_URL="${PROJECTPULSE_TEST_DATABASE_URL:-}"
JOB_NAME="${PROJECTPULSE_PR467_MIGRATION_JOB_NAME:-}"
LOG_DIR="${PROJECTPULSE_PR467_MIGRATION_LOG_DIR:-}"
MODE="${PROJECTPULSE_PR467_MIGRATION_MODE:-verify}"
ROLLBACK_CONFIRMATION="${PROJECTPULSE_PR467_ROLLBACK_CONFIRMATION:-}"

fail() {
  echo "ERROR: $*" >&2
  exit 1
}

normalize() {
  case "${1:-}" in
    ""|None|null) return 1 ;;
    *) printf '%s\n' "$1" ;;
  esac
}

mask() {
  local value="$1"
  value="${value//%/%25}"
  printf '::add-mask::%s\n' "$value"
}

[[ -n "$RESOURCE_GROUP" ]] || fail "AZURE_RESOURCE_GROUP is not configured."
[[ -n "$API_APP" ]] || fail "AZURE_API_APP is not configured."
[[ -n "$ACR_NAME" ]] || fail "AZURE_ACR_NAME is not configured."
[[ -n "$MIGRATION_IMAGE" ]] || fail "PROJECTPULSE_PR467_MIGRATION_IMAGE is not configured."
[[ -n "$DATABASE_URL" ]] || fail "PROJECTPULSE_TEST_DATABASE_URL is not configured."
[[ "$MODE" == apply || "$MODE" == verify || "$MODE" == rollback ]] ||
  fail "PROJECTPULSE_PR467_MIGRATION_MODE must be apply, verify, or rollback."
[[ "$JOB_NAME" =~ ^[a-z][a-z0-9-]{0,30}[a-z0-9]$ ]] ||
  fail "PROJECTPULSE_PR467_MIGRATION_JOB_NAME is invalid."
[[ "$MIGRATION_IMAGE" == "$ACR_NAME.azurecr.io/"*@sha256:* ]] ||
  fail "Migration image must be an immutable approved-ACR digest."
if [[ "$MODE" == rollback ]]; then
  [[ "$ROLLBACK_CONFIRMATION" == "ROLLBACK-MIGRATION-067-A536E33B" ]] ||
    fail "The exact guarded rollback confirmation is required."
fi

mask "$DATABASE_URL"
ENVIRONMENT_ID="$(az containerapp show \
  -g "$RESOURCE_GROUP" \
  -n "$API_APP" \
  --query properties.managedEnvironmentId \
  -o tsv \
  --only-show-errors)"
normalize "$ENVIRONMENT_ID" >/dev/null ||
  fail "The Test API app has no managed Container Apps environment."

ACR_SERVER="$ACR_NAME.azurecr.io"
REGISTRY_IDENTITY="$(az containerapp show \
  -g "$RESOURCE_GROUP" \
  -n "$API_APP" \
  --query "properties.configuration.registries[?server=='$ACR_SERVER'].identity | [0]" \
  -o tsv \
  --only-show-errors)"
normalize "$REGISTRY_IDENTITY" >/dev/null ||
  fail "The Test API app does not expose a reusable ACR managed identity."

registry_args=(--registry-server "$ACR_SERVER" --registry-identity "$REGISTRY_IDENTITY")
identity_args=()
case "${REGISTRY_IDENTITY,,}" in
  system-environment) ;;
  /subscriptions/*) identity_args+=(--mi-user-assigned "$REGISTRY_IDENTITY") ;;
  *) fail "Unsupported ACR managed identity reference." ;;
esac

JOB_CREATED=0
EXECUTION_NAME=""
emit_logs() {
  [[ -n "$LOG_DIR" && -n "$EXECUTION_NAME" ]] || return 0
  mkdir -p "$LOG_DIR"
  az containerapp job logs show \
    -g "$RESOURCE_GROUP" \
    -n "$JOB_NAME" \
    --execution "$EXECUTION_NAME" \
    --container "$JOB_NAME" \
    --tail 300 \
    --only-show-errors \
    > "$LOG_DIR/${JOB_NAME}-${MODE}.log" 2>&1 || true
}

cleanup() {
  local status=$?
  trap - EXIT INT TERM
  unset DATABASE_URL
  if (( JOB_CREATED == 1 )); then
    emit_logs
    az containerapp job delete \
      -g "$RESOURCE_GROUP" \
      -n "$JOB_NAME" \
      --yes \
      --output none \
      --only-show-errors || true
    echo "PROJECTPULSE_PR467_MIGRATION_JOB_CLEANUP=COMPLETE"
  fi
  exit "$status"
}
trap cleanup EXIT INT TERM

if az containerapp job show \
  -g "$RESOURCE_GROUP" \
  -n "$JOB_NAME" \
  --output none \
  --only-show-errors 2>/dev/null; then
  fail "A Container Apps Job already exists with guarded name $JOB_NAME."
fi

env_args=(
  PROJECTPULSE_TEST_DATABASE_URL=secretref:pr467-db-url
  PROJECTPULSE_PR467_MIGRATION_MODE="$MODE"
)
if [[ "$MODE" == rollback ]]; then
  env_args+=(PROJECTPULSE_PR467_ROLLBACK_CONFIRMATION="$ROLLBACK_CONFIRMATION")
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
  --cpu 0.25 \
  --memory 0.5Gi \
  "${identity_args[@]}" \
  --secrets "pr467-db-url=$DATABASE_URL" \
  --env-vars "${env_args[@]}" \
  --tags \
    projectpulse-scope="migration-067-test" \
    projectpulse-release="$EXPECTED_RELEASE_COMMIT" \
    projectpulse-mode="$MODE" \
  "${registry_args[@]}" \
  --output none \
  --only-show-errors
JOB_CREATED=1
unset DATABASE_URL

echo "PROJECTPULSE_PR467_MIGRATION_JOB_CREATE=COMPLETE mode=$MODE"
EXECUTION_NAME="$(az containerapp job start \
  -g "$RESOURCE_GROUP" \
  -n "$JOB_NAME" \
  --query name \
  -o tsv \
  --only-show-errors)"
normalize "$EXECUTION_NAME" >/dev/null ||
  fail "Azure did not return the migration execution name."
echo "PROJECTPULSE_PR467_MIGRATION_JOB_EXECUTION=$EXECUTION_NAME"

for attempt in $(seq 1 180); do
  STATUS="$(az containerapp job execution list \
    -g "$RESOURCE_GROUP" \
    -n "$JOB_NAME" \
    --query "[?name=='$EXECUTION_NAME'].properties.status | [0]" \
    -o tsv \
    --only-show-errors)"
  case "$STATUS" in
    Succeeded)
      emit_logs
      if [[ -n "$LOG_DIR" && -f "$LOG_DIR/${JOB_NAME}-${MODE}.log" ]]; then
        cat "$LOG_DIR/${JOB_NAME}-${MODE}.log"
      fi
      echo "PROJECTPULSE_PR467_MIGRATION_JOB_STATUS=SUCCEEDED mode=$MODE"
      exit 0
      ;;
    Failed|Stopped|Canceled|Degraded)
      emit_logs
      echo "PROJECTPULSE_PR467_MIGRATION_JOB_STATUS=$STATUS mode=$MODE" >&2
      break
      ;;
  esac
  sleep 5
done

emit_logs
if [[ -n "$LOG_DIR" ]]; then
  cat "$LOG_DIR/${JOB_NAME}-${MODE}.log" >&2 2>/dev/null || true
fi
fail "The private-network Migration 067 job did not succeed in $MODE mode."
