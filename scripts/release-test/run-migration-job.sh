#!/usr/bin/env bash
set -Eeuo pipefail

EXPECTED_RELEASE_COMMIT="e83340b5a4215ea63901cea98ea17596444f96b7"
RESOURCE_GROUP="${AZURE_RESOURCE_GROUP:-}"
API_APP="${AZURE_API_APP:-}"
ACR_NAME="${AZURE_ACR_NAME:-}"
MIGRATION_IMAGE="${MAIN_RELEASE_MIGRATION_IMAGE:-}"
JOB_NAME="${MAIN_RELEASE_MIGRATION_JOB_NAME:-}"
MODE="${MAIN_RELEASE_MIGRATION_MODE:-verify}"
MIGRATOR_IDENTITY="${AZURE_CELAR_MIGRATOR_IDENTITY_RESOURCE_ID:-}"
KEY_VAULT_URI="${AZURE_KEY_VAULT_URI:-}"
SECRET_NAME_VERSION="${PROJECTPULSE_DATABASE_URL_SECRET_NAME:-}"
CONTROL_SHA="${MAIN_RELEASE_CONTROL_SHA:-}"
RUN_SCOPE="${GITHUB_RUN_ID:-unknown}-${GITHUB_RUN_ATTEMPT:-unknown}"

fail() { echo "ERROR: $*" >&2; exit 1; }
normalize() { case "${1:-}" in ""|None|null) return 1 ;; *) printf '%s\n' "$1" ;; esac; }

[[ -n "$RESOURCE_GROUP" ]] || fail "AZURE_RESOURCE_GROUP is not configured."
[[ -n "$API_APP" ]] || fail "AZURE_API_APP is not configured."
[[ -n "$ACR_NAME" ]] || fail "AZURE_ACR_NAME is not configured."
[[ -n "$MIGRATION_IMAGE" ]] || fail "MAIN_RELEASE_MIGRATION_IMAGE is not configured."
[[ "$MODE" == apply || "$MODE" == verify ]] || fail "MAIN_RELEASE_MIGRATION_MODE must be apply or verify."
[[ "$JOB_NAME" =~ ^[a-z][a-z0-9-]{0,30}[a-z0-9]$ ]] || fail "MAIN_RELEASE_MIGRATION_JOB_NAME is invalid."
[[ "$MIGRATION_IMAGE" == "$ACR_NAME.azurecr.io/"*@sha256:* ]] || fail "Migration image must be an immutable digest in the approved ACR."
[[ "$MIGRATOR_IDENTITY" =~ ^/subscriptions/[^/]+/resourceGroups/[^/]+/providers/Microsoft\.ManagedIdentity/userAssignedIdentities/[^/]+$ ]] || fail "AZURE_CELAR_MIGRATOR_IDENTITY_RESOURCE_ID must be an exact UAMI resource ID."
[[ "$KEY_VAULT_URI" =~ ^https://[a-z0-9-]+\.vault\.azure\.net/?$ ]] || fail "AZURE_KEY_VAULT_URI must be an exact HTTPS Key Vault URI."
[[ "$SECRET_NAME_VERSION" =~ ^[A-Za-z0-9-]+/[0-9A-Fa-f]{32}$ ]] || fail "PROJECTPULSE_DATABASE_URL_SECRET_NAME must be secret-name/version."
[[ "$CONTROL_SHA" =~ ^[0-9a-f]{40}$ ]] || fail "MAIN_RELEASE_CONTROL_SHA must be an exact commit."
command -v az >/dev/null || fail "Azure CLI is required."
command -v jq >/dev/null || fail "jq is required."

KEY_VAULT_URI="${KEY_VAULT_URI%/}"
SECRET_URI="$KEY_VAULT_URI/secrets/$SECRET_NAME_VERSION"
ENVIRONMENT_ID="$(az containerapp show -g "$RESOURCE_GROUP" -n "$API_APP" --query properties.managedEnvironmentId -o tsv --only-show-errors)"
LOCATION="$(az containerapp show -g "$RESOURCE_GROUP" -n "$API_APP" --query location -o tsv --only-show-errors)"
normalize "$ENVIRONMENT_ID" >/dev/null || fail "The Test API app has no managed Container Apps environment."
normalize "$LOCATION" >/dev/null || fail "The Test API app has no Azure location."

SUBSCRIPTION_ID="$(az account show --query id -o tsv --only-show-errors)"
[[ "$SUBSCRIPTION_ID" =~ ^[0-9a-fA-F-]{36}$ ]] || fail "Azure subscription ID is unavailable."
JOB_ID="/subscriptions/$SUBSCRIPTION_ID/resourceGroups/$RESOURCE_GROUP/providers/Microsoft.App/jobs/$JOB_NAME"
JOB_URI="https://management.azure.com$JOB_ID?api-version=2024-03-01"

CREATE_ATTEMPTED=0
PREEXISTING_ABSENT=0
EXECUTION_NAME=""
PAYLOAD="$(mktemp)"
cleanup() {
  local status=$?
  trap - EXIT INT TERM
  rm -f "$PAYLOAD"
  if [[ -n "$EXECUTION_NAME" ]]; then
    local execution_status
    execution_status="$(az containerapp job execution list -g "$RESOURCE_GROUP" -n "$JOB_NAME" --query "[?name=='$EXECUTION_NAME'].properties.status | [0]" -o tsv --only-show-errors 2>/dev/null || true)"
    case "$execution_status" in
      Succeeded|Failed|Stopped|Canceled) ;;
      *) az containerapp job stop -g "$RESOURCE_GROUP" -n "$JOB_NAME" --job-execution-name "$EXECUTION_NAME" --output none --only-show-errors 2>/dev/null || status=1 ;;
    esac
  fi
  if (( CREATE_ATTEMPTED == 1 && PREEXISTING_ABSENT == 1 )); then
    az containerapp job delete -g "$RESOURCE_GROUP" -n "$JOB_NAME" --yes --output none --only-show-errors 2>/dev/null || true
    local remaining=1
    for _ in $(seq 1 20); do
      if ! az containerapp job show -g "$RESOURCE_GROUP" -n "$JOB_NAME" --output none --only-show-errors 2>/dev/null; then
        remaining=0
        break
      fi
      sleep 3
    done
    if (( remaining == 1 )); then
      echo "ERROR: Temporary migration job still exists after cleanup." >&2
      status=1
    else
      echo "MAIN_RELEASE_MIGRATION_JOB_CLEANUP=COMPLETE"
    fi
  fi
  exit "$status"
}
trap cleanup EXIT INT TERM

if az containerapp job show -g "$RESOURCE_GROUP" -n "$JOB_NAME" --output none --only-show-errors 2>/dev/null; then
  fail "A Container Apps Job already exists with guarded name $JOB_NAME."
fi
PREEXISTING_ABSENT=1

jq -n \
  --arg location "$LOCATION" \
  --arg environmentId "$ENVIRONMENT_ID" \
  --arg identity "$MIGRATOR_IDENTITY" \
  --arg server "$ACR_NAME.azurecr.io" \
  --arg image "$MIGRATION_IMAGE" \
  --arg jobName "$JOB_NAME" \
  --arg secretUri "$SECRET_URI" \
  --arg mode "$MODE" \
  --arg release "$EXPECTED_RELEASE_COMMIT" \
  --arg control "$CONTROL_SHA" \
  --arg runScope "$RUN_SCOPE" \
  '{
    location: $location,
    identity: {
      type: "UserAssigned",
      userAssignedIdentities: {($identity): {}}
    },
    tags: {
      "projectpulse-scope": "current-main-071-073-test",
      "projectpulse-release": $release,
      "projectpulse-control": $control,
      "projectpulse-mode": $mode,
      "projectpulse-run": $runScope
    },
    properties: {
      environmentId: $environmentId,
      configuration: {
        triggerType: "Manual",
        replicaTimeout: 1800,
        replicaRetryLimit: 0,
        manualTriggerConfig: {replicaCompletionCount: 1, parallelism: 1},
        registries: [{server: $server, identity: $identity}],
        secrets: [{name: "main-release-db-url", keyVaultUrl: $secretUri, identity: $identity}]
      },
      template: {
        containers: [{
          name: $jobName,
          image: $image,
          env: [
            {name: "PROJECTPULSE_TEST_DATABASE_URL", secretRef: "main-release-db-url"},
            {name: "MAIN_RELEASE_MIGRATION_MODE", value: $mode}
          ],
          resources: {cpu: 0.5, memory: "1Gi"}
        }]
      }
    }
  }' > "$PAYLOAD"

CREATE_ATTEMPTED=1
az rest --method put --uri "$JOB_URI" --body "$$PAYLOAD" --output none --only-show-errors

ACTUAL_IMAGE="$(az containerapp job show -g "$RESOURCE_GROUP" -n "$JOB_NAME" --query properties.template.containers[0].image -o tsv --only-show-errors)"
ACTUAL_ENVIRONMENT="$(az containerapp job show -g "$RESOURCE_GROUP" -n "$JOB_NAME" --query properties.environmentId -o tsv --only-show-errors)"
ACTUAL_SECRET_URI="$(az containerapp job show -g "$RESOURCE_GROUP" -n "$JOB_NAME" --query "properties.configuration.secrets[?name=='main-release-db-url'].keyVaultUrl | [0]" -o tsv --only-show-errors)"
[[ "$ACTUAL_IMAGE" == "$MIGRATION_IMAGE" ]] || fail "Migration job image drifted."
[[ "${ACTUAL_ENVIRONMENT,,}" == "${ENVIRONMENT_ID,,}" ]] || fail "Migration job environment drifted."
[[ "$ACTUAL_SECRET_URI" == "$SECRET_URI" ]] || fail "Migration job did not preserve the version-pinned Key Vault reference."

EXECUTION_NAME="$(az containerapp job start -g "$RESOURCE_GROUP" -n "$JOB_NAME" --query name -o tsv --only-show-errors)"
normalize "$EXECUTION_NAME" >/dev/null || fail "Azure did not return the migration execution name."
echo "MAIN_RELEASE_MIGRATION_JOB_EXECUTION=$EXECUTION_NAME mode=$MODE"

for _ in $(seq 1 180); do
  STATUS="$(az containerapp job execution list -g "$RESOURCE_GROUP" -n "$JOB_NAME" --query "[?name=='$EXECUTION_NAME'].properties.status | [0]" -o tsv --only-show-errors)"
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

az containerapp job logs show -g "$RESOURCE_GROUP" -n "$JOB_NAME" --execution "$EXECUTION_NAME" --container "$JOB_NAME" --tail 250 --only-show-errors >&2 || true
fail "The private-network migration job did not succeed in $MODE mode."
