#!/usr/bin/env bash
set -Eeuo pipefail

EXPECTED_RELEASE_COMMIT="289628ded2ec91ea0710d3cb7ee7cf16bca1f012"
RESOURCE_GROUP="${AZURE_RESOURCE_GROUP:-}"
API_APP="${AZURE_API_APP:-}"
ACR_NAME="${AZURE_ACR_NAME:-}"
MIGRATION_IMAGE="${CURRENT_MAIN_MIGRATION_IMAGE:-}"
DATABASE_URL="${PROJECTPULSE_TEST_DATABASE_URL:-}"
JOB_NAME="${CURRENT_MAIN_MIGRATION_JOB_NAME:-}"
MODE="${PROJECTPULSE_CURRENT_MAIN_MIGRATION_MODE:-verify}"

fail() { echo "ERROR: $*" >&2; exit 1; }
normalize() { case "${1:-}" in ""|None|null) return 1 ;; *) printf '%s\n' "$1" ;; esac; }
mask() { local value="$1"; value="${value//%/%25}"; printf '::add-mask::%s\n' "$value"; }

[[ -n "$RESOURCE_GROUP" ]] || fail "AZURE_RESOURCE_GROUP is not configured."
[[ -n "$API_APP" ]] || fail "AZURE_API_APP is not configured."
[[ -n "$ACR_NAME" ]] || fail "AZURE_ACR_NAME is not configured."
[[ -n "$MIGRATION_IMAGE" ]] || fail "CURRENT_MAIN_MIGRATION_IMAGE is not configured."
[[ -n "$DATABASE_URL" ]] || fail "PROJECTPULSE_TEST_DATABASE_URL is not configured."
[[ "$MODE" == apply || "$MODE" == verify ]] || fail "PROJECTPULSE_CURRENT_MAIN_MIGRATION_MODE must be apply or verify."
[[ "$JOB_NAME" =~ ^[a-z][a-z0-9-]{0,30}[a-z0-9]$ ]] || fail "CURRENT_MAIN_MIGRATION_JOB_NAME is invalid."
[[ "$MIGRATION_IMAGE" == "$ACR_NAME.azurecr.io/"*@sha256:* ]] || fail "Migration image must be an immutable approved-ACR digest."
command -v jq >/dev/null || fail "jq is required."

mask "$DATABASE_URL"
ENVIRONMENT_ID="$(az containerapp show -g "$RESOURCE_GROUP" -n "$API_APP" --query properties.managedEnvironmentId -o tsv --only-show-errors)"
normalize "$ENVIRONMENT_ID" >/dev/null || fail "The Test API app has no managed Container Apps environment."

ACR_SERVER="$ACR_NAME.azurecr.io"
REGISTRY_IDENTITY="$(az containerapp show -g "$RESOURCE_GROUP" -n "$API_APP" --query "properties.configuration.registries[?server=='$ACR_SERVER'].identity | [0]" -o tsv --only-show-errors)"
normalize "$REGISTRY_IDENTITY" >/dev/null || fail "The Test API app does not expose a reusable ACR managed identity."

registry_args=(--registry-server "$ACR_SERVER" --registry-identity "$REGISTRY_IDENTITY")
identity_args=()
case "${REGISTRY_IDENTITY,,}" in
  system-environment) ;;
  /subscriptions/*) identity_args+=(--mi-user-assigned "$REGISTRY_IDENTITY") ;;
  *) fail "Unsupported ACR managed identity reference." ;;
esac

job_exists() {
  az containerapp job show \
    -g "$RESOURCE_GROUP" \
    -n "$JOB_NAME" \
    --output none \
    --only-show-errors >/dev/null 2>&1
}

wait_for_job_absent() {
  for _ in $(seq 1 40); do
    if ! job_exists; then return 0; fi
    sleep 3
  done
  return 1
}

delete_partial_job() {
  if ! job_exists; then return 0; fi
  echo "CURRENT_MAIN_MIGRATION_JOB_PARTIAL_CLEANUP=STARTED"
  az containerapp job delete \
    -g "$RESOURCE_GROUP" \
    -n "$JOB_NAME" \
    --yes \
    --output none \
    --only-show-errors || true
  wait_for_job_absent || fail "A partial Container Apps Job could not be removed: $JOB_NAME"
  echo "CURRENT_MAIN_MIGRATION_JOB_PARTIAL_CLEANUP=COMPLETE"
}

JOB_CREATED=0
cleanup() {
  local status=$?
  trap - EXIT INT TERM
  unset DATABASE_URL
  if (( JOB_CREATED == 1 )) || job_exists; then
    az containerapp job delete -g "$RESOURCE_GROUP" -n "$JOB_NAME" --yes --output none --only-show-errors || true
    wait_for_job_absent || true
    echo "CURRENT_MAIN_MIGRATION_JOB_CLEANUP=COMPLETE"
  fi
  exit "$status"
}
trap cleanup EXIT INT TERM

# Azure CLI may return a transient IdentityDoesNotExist immediately after it
# creates a Container Apps Job and attaches an existing user-assigned ACR
# identity. The identity already has AcrPull; the workflow identity is not
# permitted to create role assignments. Treat a fully provisioned job with the
# exact immutable image as success, otherwise remove the partial job and retry.
delete_partial_job

CREATE_SUCCEEDED=0
for create_attempt in $(seq 1 4); do
  CREATE_LOG="${RUNNER_TEMP:-/tmp}/current-main-job-create-${JOB_NAME}-${create_attempt}.log"
  set +e
  az containerapp job create \
    -g "$RESOURCE_GROUP" \
    -n "$JOB_NAME" \
    --environment "$ENVIRONMENT_ID" \
    --trigger-type Manual \
    --replica-timeout 1500 \
    --replica-retry-limit 0 \
    --replica-completion-count 1 \
    --parallelism 1 \
    --image "$MIGRATION_IMAGE" \
    --cpu 0.25 \
    --memory 0.5Gi \
    "${identity_args[@]}" \
    --secrets "current-main-db-url=$DATABASE_URL" \
    --env-vars PROJECTPULSE_TEST_DATABASE_URL=secretref:current-main-db-url PROJECTPULSE_CURRENT_MAIN_MIGRATION_MODE="$MODE" \
    --tags projectpulse-scope="migrations-051a-052-053-test" projectpulse-release="$EXPECTED_RELEASE_COMMIT" projectpulse-mode="$MODE" \
    "${registry_args[@]}" \
    --output none \
    --only-show-errors >"$CREATE_LOG" 2>&1
  CREATE_STATUS=$?
  set -e

  for provision_attempt in $(seq 1 20); do
    JOB_JSON="$(az containerapp job show -g "$RESOURCE_GROUP" -n "$JOB_NAME" -o json --only-show-errors 2>/dev/null || true)"
    if [[ -n "$JOB_JSON" ]]; then
      PROVISIONING_STATE="$(jq -r '.properties.provisioningState // empty' <<<"$JOB_JSON")"
      PROVISIONED_IMAGE="$(jq -r '.properties.template.containers[0].image // empty' <<<"$JOB_JSON")"
      if [[ "$PROVISIONING_STATE" == Succeeded && "$PROVISIONED_IMAGE" == "$MIGRATION_IMAGE" ]]; then
        JOB_CREATED=1
        CREATE_SUCCEEDED=1
        echo "CURRENT_MAIN_MIGRATION_JOB_PROVISIONING=SUCCEEDED attempt=$create_attempt cli_status=$CREATE_STATUS"
        break
      fi
      if [[ "$PROVISIONING_STATE" == Failed ]]; then break; fi
    fi
    sleep 3
  done

  if (( CREATE_SUCCEEDED == 1 )); then
    [[ ! -s "$CREATE_LOG" ]] || cat "$CREATE_LOG"
    break
  fi

  echo "CURRENT_MAIN_MIGRATION_JOB_CREATE_ATTEMPT=$create_attempt status=$CREATE_STATUS" >&2
  [[ ! -s "$CREATE_LOG" ]] || cat "$CREATE_LOG" >&2
  delete_partial_job
  sleep $((create_attempt * 10))
done

(( CREATE_SUCCEEDED == 1 )) || fail "The guarded Container Apps migration job could not be provisioned after retries."
unset DATABASE_URL

echo "CURRENT_MAIN_MIGRATION_JOB_CREATE=COMPLETE mode=$MODE"
EXECUTION_NAME=""
for start_attempt in $(seq 1 12); do
  set +e
  EXECUTION_NAME="$(az containerapp job start -g "$RESOURCE_GROUP" -n "$JOB_NAME" --query name -o tsv --only-show-errors 2>/dev/null)"
  START_STATUS=$?
  set -e
  if (( START_STATUS == 0 )) && normalize "$EXECUTION_NAME" >/dev/null; then
    break
  fi
  EXECUTION_NAME=""
  sleep 5
done
normalize "$EXECUTION_NAME" >/dev/null || fail "Azure did not start the migration job after identity propagation retries."
echo "CURRENT_MAIN_MIGRATION_JOB_EXECUTION=$EXECUTION_NAME"

for attempt in $(seq 1 150); do
  STATUS="$(az containerapp job execution list -g "$RESOURCE_GROUP" -n "$JOB_NAME" --query "[?name=='$EXECUTION_NAME'].properties.status | [0]" -o tsv --only-show-errors)"
  case "$STATUS" in
    Succeeded)
      echo "CURRENT_MAIN_MIGRATION_JOB_STATUS=SUCCEEDED mode=$MODE"
      exit 0
      ;;
    Failed|Stopped|Canceled|Degraded)
      echo "CURRENT_MAIN_MIGRATION_JOB_STATUS=$STATUS mode=$MODE" >&2
      break
      ;;
  esac
  sleep 5
done

az containerapp job logs show -g "$RESOURCE_GROUP" -n "$JOB_NAME" --execution "$EXECUTION_NAME" --container "$JOB_NAME" --tail 350 --only-show-errors >&2 || true
fail "The private-network current-main migration job did not succeed in $MODE mode."
