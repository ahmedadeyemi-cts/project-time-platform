#!/usr/bin/env bash
set -Eeuo pipefail

EXPECTED_RELEASE_COMMIT="1892c6d0187edc367a57b8cee2e868417dd9a01a"
RESOURCE_GROUP="${AZURE_RESOURCE_GROUP:-}"
API_APP="${AZURE_API_APP:-}"
ACR_NAME="${AZURE_ACR_NAME:-}"
MIGRATION_IMAGE="${MAIN_RELEASE_MIGRATION_IMAGE:-}"
JOB_NAME="${MAIN_RELEASE_MIGRATION_JOB_NAME:-}"
MODE="${MAIN_RELEASE_MIGRATION_MODE:-verify}"
MIGRATOR_IDENTITY="${AZURE_CELAR_MIGRATOR_IDENTITY_RESOURCE_ID:-}"
KEY_VAULT_URI="${AZURE_KEY_VAULT_URI:-}"
PASSWORD_SECRET_NAME_VERSION="${PROJECTPULSE_DATABASE_PASSWORD_SECRET_NAME:-}"
EXPECTED_DATABASE_HOST="${PROJECTPULSE_TEST_DATABASE_HOST:-}"
EXPECTED_DATABASE_PORT="${PROJECTPULSE_TEST_DATABASE_PORT:-}"
EXPECTED_DATABASE_NAME="${PROJECTPULSE_TEST_DATABASE_NAME:-}"
EXPECTED_DATABASE_USER="${PROJECTPULSE_TEST_DATABASE_USER:-}"
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
MIGRATOR_IDENTITY_LOWER="${MIGRATOR_IDENTITY,,}"
[[ "$MIGRATOR_IDENTITY_LOWER" =~ ^/subscriptions/[^/]+/resourcegroups/[^/]+/providers/microsoft\.managedidentity/userassignedidentities/[^/]+$ ]] || fail "AZURE_CELAR_MIGRATOR_IDENTITY_RESOURCE_ID must be an exact UAMI resource ID."
[[ "$KEY_VAULT_URI" =~ ^https://[a-z0-9-]+\.vault\.azure\.net/?$ ]] || fail "AZURE_KEY_VAULT_URI must be an exact HTTPS Key Vault URI."
[[ "$PASSWORD_SECRET_NAME_VERSION" =~ ^[A-Za-z0-9-]+(/[0-9A-Fa-f]{32})?$ ]] || fail "PROJECTPULSE_DATABASE_PASSWORD_SECRET_NAME must be an exact secret name with an optional version."
[[ "$EXPECTED_DATABASE_HOST" =~ ^[A-Za-z0-9][A-Za-z0-9.-]{0,252}[A-Za-z0-9]$ ]] || fail "PROJECTPULSE_TEST_DATABASE_HOST must be an exact database host."
[[ "$EXPECTED_DATABASE_PORT" =~ ^[0-9]{1,5}$ ]] && (( EXPECTED_DATABASE_PORT >= 1 && EXPECTED_DATABASE_PORT <= 65535 )) || fail "PROJECTPULSE_TEST_DATABASE_PORT must be an exact database port."
[[ "$EXPECTED_DATABASE_NAME" =~ ^[A-Za-z_][A-Za-z0-9_]{0,62}$ ]] || fail "PROJECTPULSE_TEST_DATABASE_NAME must be an exact PostgreSQL identifier."
[[ "$EXPECTED_DATABASE_USER" =~ ^[A-Za-z_][A-Za-z0-9_]{0,62}$ ]] || fail "PROJECTPULSE_TEST_DATABASE_USER must be an exact PostgreSQL identifier."
[[ "$CONTROL_SHA" =~ ^[0-9a-f]{40}$ ]] || fail "MAIN_RELEASE_CONTROL_SHA must be an exact commit."
command -v az >/dev/null || fail "Azure CLI is required."
command -v curl >/dev/null || fail "curl is required."
command -v jq >/dev/null || fail "jq is required."

KEY_VAULT_URI="${KEY_VAULT_URI%/}"
PASSWORD_SECRET_URI="$KEY_VAULT_URI/secrets/$PASSWORD_SECRET_NAME_VERSION"

SUBSCRIPTION_ID="$(az account show --query id -o tsv --only-show-errors)"
[[ "$SUBSCRIPTION_ID" =~ ^[0-9a-fA-F-]{36}$ ]] || fail "Azure subscription ID is unavailable."
UAMI_SUBSCRIPTION="${MIGRATOR_IDENTITY#*/subscriptions/}"
UAMI_SUBSCRIPTION="${UAMI_SUBSCRIPTION%%/*}"
[[ "${UAMI_SUBSCRIPTION,,}" == "${SUBSCRIPTION_ID,,}" ]] || fail "The migrator UAMI is outside the logged-in Test subscription."
ACTUAL_UAMI_ID="$(az identity show --ids "$MIGRATOR_IDENTITY" --query id -o tsv --only-show-errors)"
[[ "${ACTUAL_UAMI_ID,,}" == "${MIGRATOR_IDENTITY,,}" ]] || fail "The protected Test migrator UAMI could not be resolved exactly."

API_JSON="$(az containerapp show -g "$RESOURCE_GROUP" -n "$API_APP" -o json --only-show-errors)"
JQ_IDENTITY="$MIGRATOR_IDENTITY" jq -e '
  (.identity.userAssignedIdentities // {}) | keys | map(ascii_downcase) |
  index(env.JQ_IDENTITY | ascii_downcase) != null
' <<<"$API_JSON" >/dev/null || fail "The migrator UAMI is not assigned to the protected Test API app."
ENVIRONMENT_ID="$(jq -r '.properties.managedEnvironmentId // empty' <<<"$API_JSON")"
LOCATION="$(jq -r '.location // empty' <<<"$API_JSON")"
normalize "$ENVIRONMENT_ID" >/dev/null || fail "The Test API app has no managed Container Apps environment."
normalize "$LOCATION" >/dev/null || fail "The Test API app has no Azure location."

JOB_ID="/subscriptions/$SUBSCRIPTION_ID/resourceGroups/$RESOURCE_GROUP/providers/Microsoft.App/jobs/$JOB_NAME"
JOB_URI="https://management.azure.com$JOB_ID?api-version=2024-03-01"

PAYLOAD="$(mktemp)"
JOB_RESPONSE="$(mktemp)"
CREATE_ATTEMPTED=0
START_ATTEMPTED=0
PREFLIGHT_CONFIRMED_404=0
EXECUTION_NAME=""

arm_get_job() {
  local output_file="$1"
  local token=""
  local http_code=""
  local curl_status=0
  token="$(az account get-access-token --resource-type arm --query accessToken -o tsv --only-show-errors)" || return 1
  [[ -n "$token" ]] || return 1
  http_code="$(curl --silent --show-error --retry 0 --connect-timeout 20 --max-time 60 \
    --output "$output_file" --write-out '%{http_code}' \
    --header "Authorization: Bearer $token" \
    --header 'Accept: application/json' \
    "$JOB_URI")" || curl_status=$?
  unset token
  (( curl_status == 0 )) || return "$curl_status"
  [[ "$http_code" =~ ^[0-9]{3}$ ]] || return 1
  printf '%s\n' "$http_code"
}

validate_job_ownership() {
  local document="$1"
  jq -e \
    --arg id "$JOB_ID" \
    --arg location "$LOCATION" \
    --arg environmentId "$ENVIRONMENT_ID" \
    --arg identity "$MIGRATOR_IDENTITY" \
    --arg server "$ACR_NAME.azurecr.io" \
    --arg image "$MIGRATION_IMAGE" \
    --arg jobName "$JOB_NAME" \
    --arg passwordSecretUri "$PASSWORD_SECRET_URI" \
    --arg mode "$MODE" \
    --arg release "$EXPECTED_RELEASE_COMMIT" \
    --arg control "$CONTROL_SHA" \
    --arg runScope "$RUN_SCOPE" \
    --arg databaseHost "$EXPECTED_DATABASE_HOST" \
    --arg databasePort "$EXPECTED_DATABASE_PORT" \
    --arg databaseName "$EXPECTED_DATABASE_NAME" \
    --arg databaseUser "$EXPECTED_DATABASE_USER" '
      ((.id | ascii_downcase) == ($id | ascii_downcase)) and
      (.name == $jobName) and
      ((.location | ascii_downcase) == ($location | ascii_downcase)) and
      (.tags == {
        "projectpulse-scope": "modules-081-082-001a-celar-ai-test",
        "projectpulse-release": $release,
        "projectpulse-control": $control,
        "projectpulse-mode": $mode,
        "projectpulse-run": $runScope
      }) and
      (.identity.type == "UserAssigned") and
      ((.identity.userAssignedIdentities | keys | length) == 1) and
      (((.identity.userAssignedIdentities | keys[0]) | ascii_downcase) == ($identity | ascii_downcase)) and
      ((.properties.environmentId | ascii_downcase) == ($environmentId | ascii_downcase)) and
      (.properties.configuration.triggerType == "Manual") and
      (.properties.configuration.replicaTimeout == 1800) and
      (.properties.configuration.replicaRetryLimit == 0) and
      (.properties.configuration.manualTriggerConfig.replicaCompletionCount == 1) and
      (.properties.configuration.manualTriggerConfig.parallelism == 1) and
      ((.properties.configuration.registries | length) == 1) and
      (.properties.configuration.registries[0].server == $server) and
      ((.properties.configuration.registries[0].identity | ascii_downcase) == ($identity | ascii_downcase)) and
      ((.properties.configuration.secrets | length) == 1) and
      (.properties.configuration.secrets[0].name == "main-release-db-password") and
      (.properties.configuration.secrets[0].keyVaultUrl == $passwordSecretUri) and
      ((.properties.configuration.secrets[0].identity | ascii_downcase) == ($identity | ascii_downcase)) and
      ((.properties.template.containers | length) == 1) and
      (.properties.template.containers[0].name == $jobName) and
      (.properties.template.containers[0].image == $image) and
      (.properties.template.containers[0].resources.cpu == 0.5) and
      (.properties.template.containers[0].resources.memory == "1Gi") and
      (
        [.properties.template.containers[0].env[] |
          {name, value: (.value // null), secretRef: (.secretRef // null)}
        ] | sort_by(.name)
      ) == ([
        {name: "MAIN_RELEASE_MIGRATION_MODE", value: $mode, secretRef: null},
        {name: "PGCONNECT_TIMEOUT", value: "15", secretRef: null},
        {name: "PGDATABASE", value: $databaseName, secretRef: null},
        {name: "PGHOST", value: $databaseHost, secretRef: null},
        {name: "PGPASSWORD", value: null, secretRef: "main-release-db-password"},
        {name: "PGPORT", value: $databasePort, secretRef: null},
        {name: "PGSSLROOTCERT", value: "system", secretRef: null},
        {name: "PGSSLMODE", value: "verify-full", secretRef: null},
        {name: "PGUSER", value: $databaseUser, secretRef: null},
        {name: "PROJECTPULSE_TEST_DATABASE_NAME", value: $databaseName, secretRef: null}
      ] | sort_by(.name))
    ' "$document" >/dev/null
}

stop_nonterminal_executions() {
  local executions=""
  local pending_json="[]"
  local pending=()
  local stop_failed=0
  for _ in $(seq 1 30); do
    executions="$(az containerapp job execution list \
      -g "$RESOURCE_GROUP" -n "$JOB_NAME" -o json --only-show-errors)" || return 1
    pending_json="$(jq -ec '[
      .[] |
      select((.properties.status // "") as $status |
        ($status != "Succeeded" and
         $status != "Failed" and
         $status != "Stopped" and
         $status != "Canceled" and
         $status != "Cancelled")) |
      .name
    ]' <<<"$executions")" || return 1
    mapfile -t pending < <(jq -r '.[]' <<<"$pending_json")
    if (( ${#pending[@]} == 0 )); then
      return "$stop_failed"
    fi
    for execution in "${pending[@]}"; do
      az containerapp job stop \
        -g "$RESOURCE_GROUP" -n "$JOB_NAME" \
        --job-execution-name "$execution" \
        --output none --only-show-errors || stop_failed=1
    done
    sleep 2
  done
  return 1
}
cleanup() {
  local status=$?
  local http_status=""
  local can_delete=1
  trap - EXIT INT TERM
  set +e

  if (( CREATE_ATTEMPTED == 1 && PREFLIGHT_CONFIRMED_404 == 1 )); then
    if ! http_status="$(arm_get_job "$JOB_RESPONSE")"; then
      echo "ERROR: Could not determine temporary migration job state during cleanup." >&2
      status=1
      can_delete=0
    elif [[ "$http_status" == 404 ]]; then
      echo "MAIN_RELEASE_MIGRATION_JOB_CLEANUP=CONFIRMED_ABSENT"
      can_delete=0
    elif [[ "$http_status" != 200 ]]; then
      echo "ERROR: Unexpected ARM status while checking the temporary migration job during cleanup." >&2
      status=1
      can_delete=0
    elif ! validate_job_ownership "$JOB_RESPONSE"; then
      echo "ERROR: Refusing to stop or delete a migration job whose ownership contract is not exact." >&2
      status=1
      can_delete=0
    fi

    if (( can_delete == 1 && START_ATTEMPTED == 1 )); then
      if ! stop_nonterminal_executions; then
        echo "ERROR: Could not confirm all temporary migration executions are terminal." >&2
        status=1
        can_delete=0
      fi
    fi

    if (( can_delete == 1 )); then
      if ! http_status="$(arm_get_job "$JOB_RESPONSE")"; then
        echo "ERROR: Could not revalidate migration job ownership before deletion." >&2
        status=1
        can_delete=0
      elif [[ "$http_status" == 404 ]]; then
        echo "MAIN_RELEASE_MIGRATION_JOB_CLEANUP=CONFIRMED_ABSENT"
        can_delete=0
      elif [[ "$http_status" != 200 ]] || ! validate_job_ownership "$JOB_RESPONSE"; then
        echo "ERROR: Refusing to delete a migration job after ownership revalidation failed." >&2
        status=1
        can_delete=0
      fi
    fi

    if (( can_delete == 1 )); then
      if ! az containerapp job delete \
        -g "$RESOURCE_GROUP" -n "$JOB_NAME" \
        --yes --output none --only-show-errors; then
        echo "ERROR: Temporary migration job deletion request failed." >&2
        status=1
      fi
      local confirmed_absent=0
      for _ in $(seq 1 30); do
        if ! http_status="$(arm_get_job "$JOB_RESPONSE")"; then
          sleep 2
          continue
        fi
        if [[ "$http_status" == 404 ]]; then
          confirmed_absent=1
          break
        fi
        if [[ "$http_status" == 200 ]] && ! validate_job_ownership "$JOB_RESPONSE"; then
          echo "ERROR: Migration job ownership changed while waiting for deletion." >&2
          status=1
          break
        fi
        sleep 2
      done
      if (( confirmed_absent == 0 )); then
        echo "ERROR: Temporary migration job was not confirmed absent after cleanup." >&2
        status=1
      else
        echo "MAIN_RELEASE_MIGRATION_JOB_CLEANUP=CONFIRMED_404"
      fi
    fi
  fi

  rm -f "$PAYLOAD" "$JOB_RESPONSE"
  unset PASSWORD_SECRET_URI
  exit "$status"
}
trap cleanup EXIT INT TERM

PREFLIGHT_STATUS="$(arm_get_job "$JOB_RESPONSE")" || fail "Could not perform the migration job ARM preflight."
[[ "$PREFLIGHT_STATUS" == 404 ]] || {
  if [[ "$PREFLIGHT_STATUS" == 200 ]]; then
    fail "A Container Apps Job already exists with the guarded release name."
  fi
  fail "Migration job preflight did not return the required HTTP 404."
}
PREFLIGHT_CONFIRMED_404=1
echo "MAIN_RELEASE_MIGRATION_JOB_PREFLIGHT=CONFIRMED_404"

jq -n \
  --arg location "$LOCATION" \
  --arg environmentId "$ENVIRONMENT_ID" \
  --arg identity "$MIGRATOR_IDENTITY" \
  --arg server "$ACR_NAME.azurecr.io" \
  --arg image "$MIGRATION_IMAGE" \
  --arg jobName "$JOB_NAME" \
  --arg passwordSecretUri "$PASSWORD_SECRET_URI" \
  --arg mode "$MODE" \
  --arg release "$EXPECTED_RELEASE_COMMIT" \
  --arg control "$CONTROL_SHA" \
  --arg runScope "$RUN_SCOPE" \
  --arg databaseHost "$EXPECTED_DATABASE_HOST" \
  --arg databasePort "$EXPECTED_DATABASE_PORT" \
  --arg databaseName "$EXPECTED_DATABASE_NAME" \
  --arg databaseUser "$EXPECTED_DATABASE_USER" \
  '{
    location: $location,
    identity: {
      type: "UserAssigned",
      userAssignedIdentities: {($identity): {}}
    },
    tags: {
      "projectpulse-scope": "modules-081-082-001a-celar-ai-test",
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
        secrets: [{name: "main-release-db-password", keyVaultUrl: $passwordSecretUri, identity: $identity}]
      },
      template: {
        containers: [{
          name: $jobName,
          image: $image,
          env: [
            {name: "MAIN_RELEASE_MIGRATION_MODE", value: $mode},
            {name: "PGCONNECT_TIMEOUT", value: "15"},
            {name: "PGDATABASE", value: $databaseName},
            {name: "PGHOST", value: $databaseHost},
            {name: "PGPASSWORD", secretRef: "main-release-db-password"},
            {name: "PGPORT", value: $databasePort},
            {name: "PGSSLROOTCERT", value: "system"},
            {name: "PGSSLMODE", value: "verify-full"},
            {name: "PGUSER", value: $databaseUser},
            {name: "PROJECTPULSE_TEST_DATABASE_NAME", value: $databaseName}
          ],
          resources: {cpu: 0.5, memory: "1Gi"}
        }]
      }
    }
  }' > "$PAYLOAD"

CREATE_ATTEMPTED=1
az rest --method put --uri "$JOB_URI" --body @"$PAYLOAD" --output none --only-show-errors

PROVISIONED=0
for _ in $(seq 1 60); do
  JOB_STATUS="$(arm_get_job "$JOB_RESPONSE")" || fail "Could not read the temporary migration job after creation."
  [[ "$JOB_STATUS" == 200 ]] || fail "Temporary migration job was not readable after creation."
  validate_job_ownership "$JOB_RESPONSE" || fail "Temporary migration job ownership or immutable configuration did not match exactly."
  PROVISIONING_STATE="$(jq -r '.properties.provisioningState // empty' "$JOB_RESPONSE")"
  case "$PROVISIONING_STATE" in
    Succeeded)
      PROVISIONED=1
      break
      ;;
    Failed|Canceled|Cancelled)
      fail "Temporary migration job provisioning did not succeed."
      ;;
  esac
  sleep 2
done
(( PROVISIONED == 1 )) || fail "Temporary migration job provisioning did not reach Succeeded."
echo "MAIN_RELEASE_MIGRATION_JOB_OWNERSHIP=VERIFIED"

START_ATTEMPTED=1
EXECUTION_NAME="$(az containerapp job start \
  -g "$RESOURCE_GROUP" -n "$JOB_NAME" \
  --query name -o tsv --only-show-errors)"
normalize "$EXECUTION_NAME" >/dev/null || fail "Azure did not return the migration execution name."
echo "MAIN_RELEASE_MIGRATION_JOB_EXECUTION=STARTED mode=$MODE"

for _ in $(seq 1 180); do
  STATUS="$(az containerapp job execution list \
    -g "$RESOURCE_GROUP" -n "$JOB_NAME" \
    --query "[?name=='$EXECUTION_NAME'].properties.status | [0]" \
    -o tsv --only-show-errors)"
  case "$STATUS" in
    Succeeded)
      echo "MAIN_RELEASE_MIGRATION_JOB_STATUS=SUCCEEDED mode=$MODE"
      exit 0
      ;;
    Failed|Stopped|Canceled|Cancelled|Degraded)
      echo "MAIN_RELEASE_MIGRATION_JOB_STATUS=$STATUS mode=$MODE" >&2
      break
      ;;
  esac
  sleep 5
done

az containerapp job logs show \
  -g "$RESOURCE_GROUP" -n "$JOB_NAME" \
  --execution "$EXECUTION_NAME" --container "$JOB_NAME" \
  --tail 250 --only-show-errors >&2 || true
fail "The private-network migration job did not succeed in $MODE mode."
