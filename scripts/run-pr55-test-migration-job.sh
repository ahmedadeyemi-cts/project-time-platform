#!/usr/bin/env bash
set -Eeuo pipefail

EXPECTED_RELEASE_COMMIT="5b4debe8218560de357f37e567f38aa497482d69"

RESOURCE_GROUP="${AZURE_RESOURCE_GROUP:-}"
API_APP="${AZURE_API_APP:-}"
ACR_NAME="${AZURE_ACR_NAME:-}"
MIGRATION_IMAGE="${PR55_MIGRATION_IMAGE:-}"
DATABASE_URL="${PROJECTPULSE_TEST_DATABASE_URL:-}"
JOB_NAME="${PR55_MIGRATION_JOB_NAME:-}"

fail() {
  echo "ERROR: $*" >&2
  exit 1
}

require_command() {
  command -v "$1" >/dev/null 2>&1 || fail "Required command not found: $1"
}

normalize_az_value() {
  case "${1:-}" in
    ""|None|null) return 1 ;;
    *) printf '%s\n' "$1" ;;
  esac
}

mask_value() {
  local value="$1"
  value="${value//%/%25}"
  printf '::add-mask::%s\n' "$value"
}

[[ -n "$RESOURCE_GROUP" ]] || fail "AZURE_RESOURCE_GROUP is not configured."
[[ -n "$API_APP" ]] || fail "AZURE_API_APP is not configured."
[[ -n "$ACR_NAME" ]] || fail "AZURE_ACR_NAME is not configured."
[[ -n "$MIGRATION_IMAGE" ]] || fail "PR55_MIGRATION_IMAGE is not configured."
[[ -n "$DATABASE_URL" ]] || fail "PROJECTPULSE_TEST_DATABASE_URL is not configured."
[[ "$JOB_NAME" =~ ^[a-z][a-z0-9-]{0,30}[a-z0-9]$ ]] ||
  fail "PR55_MIGRATION_JOB_NAME must be a valid Container Apps Job name no longer than 32 characters."
[[ "$MIGRATION_IMAGE" == "$ACR_NAME.azurecr.io/"*@sha256:* ]] ||
  fail "The migration image must be an immutable digest from the approved ACR."

require_command az

mask_value "$DATABASE_URL"

ENVIRONMENT_ID="$(az containerapp show \
  --resource-group "$RESOURCE_GROUP" \
  --name "$API_APP" \
  --query properties.managedEnvironmentId \
  --output tsv \
  --only-show-errors)"
normalize_az_value "$ENVIRONMENT_ID" >/dev/null ||
  fail "The test API Container App does not expose a managed Container Apps environment."
[[ "$ENVIRONMENT_ID" == /subscriptions/*/resourceGroups/*/providers/Microsoft.App/managedEnvironments/* ]] ||
  fail "The test API managed-environment resource ID is not valid."

if az containerapp job show \
  --resource-group "$RESOURCE_GROUP" \
  --name "$JOB_NAME" \
  --output none \
  --only-show-errors 2>/dev/null; then
  fail "A Container Apps Job already exists with the guarded name $JOB_NAME."
fi

JOB_CREATED=0
cleanup() {
  local status=$?
  trap - EXIT INT TERM
  unset DATABASE_URL REGISTRY_PASSWORD
  if (( JOB_CREATED == 1 )); then
    echo "PR55_MIGRATION_JOB_CLEANUP=STARTED"
    az containerapp job delete \
      --resource-group "$RESOURCE_GROUP" \
      --name "$JOB_NAME" \
      --yes \
      --output none \
      --only-show-errors || true
    echo "PR55_MIGRATION_JOB_CLEANUP=COMPLETE"
  fi
  exit "$status"
}
trap cleanup EXIT INT TERM

ACR_SERVER="$ACR_NAME.azurecr.io"
REGISTRY_IDENTITY="$(az containerapp show \
  --resource-group "$RESOURCE_GROUP" \
  --name "$API_APP" \
  --query "properties.configuration.registries[?server=='$ACR_SERVER'].identity | [0]" \
  --output tsv \
  --only-show-errors)"
REGISTRY_IDENTITY_LOWER="${REGISTRY_IDENTITY,,}"

registry_args=(--registry-server "$ACR_SERVER")
job_identity_args=()
case "$REGISTRY_IDENTITY_LOWER" in
  system-environment)
    registry_args+=(--registry-identity "$REGISTRY_IDENTITY")
    echo "PR55_MIGRATION_JOB_REGISTRY_AUTH=REUSABLE_MANAGED_IDENTITY"
    ;;
  /subscriptions/*)
    [[ "$REGISTRY_IDENTITY_LOWER" =~ ^/subscriptions/[^/]+/resourcegroups/[^/]+/providers/microsoft\.managedidentity/userassignedidentities/[^/]+$ ]] ||
      fail "The test API uses an unsupported ACR identity reference."

    mapfile -t API_USER_ASSIGNED_IDENTITIES < <(az containerapp show \
      --resource-group "$RESOURCE_GROUP" \
      --name "$API_APP" \
      --query "identity.userAssignedIdentities | keys(@)" \
      --output tsv \
      --only-show-errors)

    REGISTRY_IDENTITY_ASSIGNED=0
    for assigned_identity in "${API_USER_ASSIGNED_IDENTITIES[@]}"; do
      if [[ "${assigned_identity,,}" == "$REGISTRY_IDENTITY_LOWER" ]]; then
        REGISTRY_IDENTITY_ASSIGNED=1
        break
      fi
    done
    (( REGISTRY_IDENTITY_ASSIGNED == 1 )) ||
      fail "The test API ACR user-assigned identity is not assigned to the API app."

    job_identity_args+=(--mi-user-assigned "$REGISTRY_IDENTITY")
    registry_args+=(--registry-identity "$REGISTRY_IDENTITY")
    echo "PR55_MIGRATION_JOB_REGISTRY_AUTH=REUSABLE_USER_ASSIGNED_IDENTITY"
    ;;
  ""|none|null|system)
    ;;
  *)
    fail "The test API uses an unsupported ACR identity reference."
    ;;
esac

if (( ${#registry_args[@]} == 2 )); then
  REGISTRY_USERNAME="$(az containerapp show \
    --resource-group "$RESOURCE_GROUP" \
    --name "$API_APP" \
    --query "properties.configuration.registries[?server=='$ACR_SERVER'].username | [0]" \
    --output tsv \
    --only-show-errors)"
  REGISTRY_PASSWORD_REF="$(az containerapp show \
    --resource-group "$RESOURCE_GROUP" \
    --name "$API_APP" \
    --query "properties.configuration.registries[?server=='$ACR_SERVER'].passwordSecretRef | [0]" \
    --output tsv \
    --only-show-errors)"

  if normalize_az_value "$REGISTRY_USERNAME" >/dev/null \
     && normalize_az_value "$REGISTRY_PASSWORD_REF" >/dev/null; then
    REGISTRY_PASSWORD="$(az containerapp secret list \
      --resource-group "$RESOURCE_GROUP" \
      --name "$API_APP" \
      --show-values \
      --query "[?name=='$REGISTRY_PASSWORD_REF'].value | [0]" \
      --output tsv \
      --only-show-errors)"
    normalize_az_value "$REGISTRY_PASSWORD" >/dev/null ||
      fail "The test API ACR password secret is unavailable."
    echo "PR55_MIGRATION_JOB_REGISTRY_AUTH=EXISTING_APP_SECRET"
  else
    REGISTRY_USERNAME='00000000-0000-0000-0000-000000000000'
    REGISTRY_PASSWORD="$(az acr login \
      --name "$ACR_NAME" \
      --expose-token \
      --query accessToken \
      --output tsv \
      --only-show-errors)"
    normalize_az_value "$REGISTRY_PASSWORD" >/dev/null ||
      fail "Azure did not issue a temporary ACR access token for the migration job."
    echo "PR55_MIGRATION_JOB_REGISTRY_AUTH=EPHEMERAL_AZURE_TOKEN"
  fi

  mask_value "$REGISTRY_USERNAME"
  mask_value "$REGISTRY_PASSWORD"
  registry_args+=(
    --registry-username "$REGISTRY_USERNAME"
    --registry-password "$REGISTRY_PASSWORD"
  )
fi

echo "PR55_MIGRATION_JOB_CREATE=STARTED"
az containerapp job create \
  --resource-group "$RESOURCE_GROUP" \
  --name "$JOB_NAME" \
  --environment "$ENVIRONMENT_ID" \
  --trigger-type Manual \
  --replica-timeout 900 \
  --replica-retry-limit 0 \
  --replica-completion-count 1 \
  --parallelism 1 \
  --image "$MIGRATION_IMAGE" \
  --cpu 0.25 \
  --memory 0.5Gi \
  "${job_identity_args[@]}" \
  --secrets "pr55-db-url=$DATABASE_URL" \
  --env-vars \
    PROJECTPULSE_TEST_DATABASE_URL=secretref:pr55-db-url \
  --tags \
    projectpulse-scope=pr55-test-migration \
    projectpulse-release="$EXPECTED_RELEASE_COMMIT" \
  "${registry_args[@]}" \
  --output none \
  --only-show-errors
JOB_CREATED=1
unset REGISTRY_PASSWORD DATABASE_URL
echo "PR55_MIGRATION_JOB_CREATE=COMPLETE"

EXECUTION_NAME="$(az containerapp job start \
  --resource-group "$RESOURCE_GROUP" \
  --name "$JOB_NAME" \
  --query name \
  --output tsv \
  --only-show-errors)"
normalize_az_value "$EXECUTION_NAME" >/dev/null ||
  fail "Azure did not return the migration job execution name."
echo "PR55_MIGRATION_JOB_EXECUTION=$EXECUTION_NAME"

for attempt in $(seq 1 90); do
  EXECUTION_STATUS="$(az containerapp job execution list \
    --resource-group "$RESOURCE_GROUP" \
    --name "$JOB_NAME" \
    --query "[?name=='$EXECUTION_NAME'].properties.status | [0]" \
    --output tsv \
    --only-show-errors)"

  case "$EXECUTION_STATUS" in
    Succeeded)
      echo "PR55_MIGRATION_JOB_STATUS=SUCCEEDED"
      exit 0
      ;;
    Failed|Stopped|Degraded)
      echo "PR55_MIGRATION_JOB_STATUS=$EXECUTION_STATUS" >&2
      fail "The private-network migration job did not succeed."
      ;;
    Running|Processing|Pending|Starting|"") ;;
    *) echo "PR55_MIGRATION_JOB_STATUS=$EXECUTION_STATUS" ;;
  esac

  sleep 10
done

az containerapp job stop \
  --resource-group "$RESOURCE_GROUP" \
  --name "$JOB_NAME" \
  --job-execution-name "$EXECUTION_NAME" \
  --output none \
  --only-show-errors || true
fail "The private-network migration job did not finish within 15 minutes."
