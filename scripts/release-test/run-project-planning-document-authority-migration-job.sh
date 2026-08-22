#!/usr/bin/env bash
set -Eeuo pipefail

RESOURCE_GROUP="${AZURE_RESOURCE_GROUP:-}"
API_APP="${AZURE_API_APP:-}"
ACR_NAME="${AZURE_ACR_NAME:-}"
MIGRATION_IMAGE="${RELIABILITY_MIGRATION_IMAGE:-}"
JOB_NAME="${RELIABILITY_MIGRATION_JOB_NAME:-}"
MIGRATOR_IDENTITY="${AZURE_CELAR_MIGRATOR_IDENTITY_RESOURCE_ID:-}"
KEY_VAULT_URI="${AZURE_KEY_VAULT_URI:-}"
DATABASE_PASSWORD_SECRET_NAME="${PROJECTPULSE_DATABASE_PASSWORD_SECRET_NAME:-}"
DATABASE_HOST="${PROJECTPULSE_TEST_DATABASE_HOST:-}"
DATABASE_PORT="${PROJECTPULSE_TEST_DATABASE_PORT:-}"
DATABASE_NAME="${PROJECTPULSE_TEST_DATABASE_NAME:-}"
DATABASE_USER="${PROJECTPULSE_TEST_DATABASE_USER:-}"
CONTROL_SHA="${RELIABILITY_CONTROL_SHA:-}"
RELEASE_COMMIT="${RELIABILITY_RELEASE_COMMIT:-}"
MIGRATION_SCOPE="${RELIABILITY_MIGRATION_SCOPE:-project-planning-document-authority-test}"
RUN_SCOPE="${GITHUB_RUN_ID:-unknown}-${GITHUB_RUN_ATTEMPT:-unknown}"

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

[[ -n "$RESOURCE_GROUP" ]] || fail "AZURE_RESOURCE_GROUP is not configured."
[[ -n "$API_APP" ]] || fail "AZURE_API_APP is not configured."
[[ -n "$ACR_NAME" ]] || fail "AZURE_ACR_NAME is not configured."
[[ "$MIGRATION_IMAGE" == "$ACR_NAME.azurecr.io/"*@sha256:* ]] \
  || fail "RELIABILITY_MIGRATION_IMAGE must be an immutable digest in the approved Test ACR."
[[ "$JOB_NAME" =~ ^[a-z][a-z0-9-]{0,30}[a-z0-9]$ ]] \
  || fail "RELIABILITY_MIGRATION_JOB_NAME is invalid."
[[ "$CONTROL_SHA" =~ ^[0-9a-f]{40}$ ]] || fail "RELIABILITY_CONTROL_SHA must be an exact commit."
[[ "$RELEASE_COMMIT" =~ ^[0-9a-f]{40}$ ]] || fail "RELIABILITY_RELEASE_COMMIT must be an exact commit."
[[ "$MIGRATION_SCOPE" =~ ^[a-z0-9][a-z0-9-]{2,62}$ ]] || fail "RELIABILITY_MIGRATION_SCOPE is invalid."
[[ "$KEY_VAULT_URI" =~ ^https://[a-z0-9-]+\.vault\.azure\.net/?$ ]] \
  || fail "AZURE_KEY_VAULT_URI must be an exact HTTPS Key Vault URI."
[[ "$DATABASE_PASSWORD_SECRET_NAME" =~ ^[A-Za-z0-9-]+(/[0-9A-Fa-f]{32})?$ ]] \
  || fail "PROJECTPULSE_DATABASE_PASSWORD_SECRET_NAME must be an exact secret name with an optional version."
[[ "$DATABASE_HOST" =~ ^[A-Za-z0-9][A-Za-z0-9.-]{0,252}[A-Za-z0-9]$ ]] \
  || fail "PROJECTPULSE_TEST_DATABASE_HOST must be an exact database host."
[[ "$DATABASE_PORT" =~ ^[0-9]{1,5}$ ]] \
  && (( DATABASE_PORT >= 1 && DATABASE_PORT <= 65535 )) \
  || fail "PROJECTPULSE_TEST_DATABASE_PORT must be an exact database port."
[[ "$DATABASE_NAME" =~ ^[A-Za-z_][A-Za-z0-9_]{0,62}$ ]] \
  || fail "PROJECTPULSE_TEST_DATABASE_NAME must be an exact PostgreSQL identifier."
[[ "$DATABASE_USER" =~ ^[A-Za-z_][A-Za-z0-9_]{0,62}$ ]] \
  || fail "PROJECTPULSE_TEST_DATABASE_USER must be an exact PostgreSQL identifier."

for command_name in az curl jq; do
  command -v "$command_name" >/dev/null 2>&1 || fail "$command_name is required."
done

MIGRATOR_IDENTITY_LOWER="${MIGRATOR_IDENTITY,,}"
[[ "$MIGRATOR_IDENTITY_LOWER" =~ ^/subscriptions/[^/]+/resourcegroups/[^/]+/providers/microsoft\.managedidentity/userassignedidentities/[^/]+$ ]] \
  || fail "AZURE_CELAR_MIGRATOR_IDENTITY_RESOURCE_ID must be an exact UAMI resource ID."

KEY_VAULT_URI="${KEY_VAULT_URI%/}"
DATABASE_PASSWORD_SECRET_URI="$KEY_VAULT_URI/secrets/$DATABASE_PASSWORD_SECRET_NAME"

SUBSCRIPTION_ID="$(az account show --query id -o tsv --only-show-errors)"
[[ "$SUBSCRIPTION_ID" =~ ^[0-9a-fA-F-]{36}$ ]] || fail "Azure subscription ID is unavailable."
UAMI_SUBSCRIPTION="${MIGRATOR_IDENTITY#*/subscriptions/}"
UAMI_SUBSCRIPTION="${UAMI_SUBSCRIPTION%%/*}"
[[ "${UAMI_SUBSCRIPTION,,}" == "${SUBSCRIPTION_ID,,}" ]] \
  || fail "The migration UAMI is outside the logged-in Test subscription."

ACTUAL_IDENTITY="$(az identity show --ids "$MIGRATOR_IDENTITY" --query id -o tsv --only-show-errors)"
[[ "${ACTUAL_IDENTITY,,}" == "${MIGRATOR_IDENTITY,,}" ]] \
  || fail "The protected Test migration UAMI could not be resolved exactly."

API_JSON="$(az containerapp show -g "$RESOURCE_GROUP" -n "$API_APP" -o json --only-show-errors)"
[[ "$(jq -r '.tags.environment // empty' <<<"$API_JSON")" == "test" ]] \
  || fail "The API app is not tagged as Test."
JQ_IDENTITY="$MIGRATOR_IDENTITY" jq -e '
  (.identity.userAssignedIdentities // {}) | keys | map(ascii_downcase) |
  index(env.JQ_IDENTITY | ascii_downcase) != null
' <<<"$API_JSON" >/dev/null \
  || fail "The protected Test migration UAMI is not assigned to the API app."

ENVIRONMENT_ID="$(jq -r '.properties.managedEnvironmentId // empty' <<<"$API_JSON")"
LOCATION="$(jq -r '.location // empty' <<<"$API_JSON")"
normalize "$ENVIRONMENT_ID" >/dev/null || fail "The Test API app has no managed environment."
normalize "$LOCATION" >/dev/null || fail "The Test API app has no Azure location."

JOB_ID="/subscriptions/$SUBSCRIPTION_ID/resourceGroups/$RESOURCE_GROUP/providers/Microsoft.App/jobs/$JOB_NAME"
JOB_URI="https://management.azure.com$JOB_ID?api-version=2024-03-01"
PAYLOAD="$(mktemp)"
JOB_RESPONSE="$(mktemp)"
chmod 0600 "$PAYLOAD" "$JOB_RESPONSE"
CREATE_ATTEMPTED=0
START_ATTEMPTED=0
PREFLIGHT_CONFIRMED_404=0

arm_get_job() {
  local output_file="$1" token='' http_code='' curl_status=0
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
    --arg databaseSecretUri "$DATABASE_PASSWORD_SECRET_URI" \
    --arg release "$RELEASE_COMMIT" \
    --arg scope "$MIGRATION_SCOPE" \
    --arg control "$CONTROL_SHA" \
    --arg runScope "$RUN_SCOPE" \
    --arg databaseHost "$DATABASE_HOST" \
    --arg databasePort "$DATABASE_PORT" \
    --arg databaseName "$DATABASE_NAME" \
    --arg databaseUser "$DATABASE_USER" '
      ((.id | ascii_downcase) == ($id | ascii_downcase)) and
      (.name == $jobName) and
      ((.location | ascii_downcase) == ($location | ascii_downcase)) and
      (.tags["projectpulse-scope"] == $scope) and
      (.tags["projectpulse-release"] == $release) and
      (.tags["projectpulse-control"] == $control) and
      (.tags["projectpulse-run"] == $runScope) and
      (.tags["projectpulse-migration"] == "096-project-planning-document-authority") and
      (.identity.type == "UserAssigned") and
      ((.identity.userAssignedIdentities | keys | length) == 1) and
      (((.identity.userAssignedIdentities | keys[0]) | ascii_downcase) == ($identity | ascii_downcase)) and
      ((.properties.environmentId | ascii_downcase) == ($environmentId | ascii_downcase)) and
      (.properties.configuration.triggerType == "Manual") and
      (.properties.configuration.replicaRetryLimit == 0) and
      ((.properties.configuration.registries | length) == 1) and
      (.properties.configuration.registries[0].server == $server) and
      ((.properties.configuration.registries[0].identity | ascii_downcase) == ($identity | ascii_downcase)) and
      (([.properties.configuration.secrets[].name] | sort) == ["main-db-password"]) and
      (.properties.configuration.secrets[] | select(.name == "main-db-password") | .keyVaultUrl == $databaseSecretUri) and
      ((.properties.template.containers | length) == 1) and
      (.properties.template.containers[0].name == $jobName) and
      (.properties.template.containers[0].image == $image) and
      (
        [.properties.template.containers[0].env[] |
          {name, value: (.value // null), secretRef: (.secretRef // null)}
        ] | sort_by(.name)
      ) == ([
        {name: "MAIN_RELEASE_EXPECTED_RELEASE_COMMIT", value: $release, secretRef: null},
        {name: "PGCONNECT_TIMEOUT", value: "15", secretRef: null},
        {name: "PGDATABASE", value: $databaseName, secretRef: null},
        {name: "PGHOST", value: $databaseHost, secretRef: null},
        {name: "PGPASSWORD", value: null, secretRef: "main-db-password"},
        {name: "PGPORT", value: $databasePort, secretRef: null},
        {name: "PGSSLROOTCERT", value: "system", secretRef: null},
        {name: "PGSSLMODE", value: "verify-full", secretRef: null},
        {name: "PGUSER", value: $databaseUser, secretRef: null},
        {name: "PROJECTPULSE_ENVIRONMENT", value: "Test", secretRef: null}
      ] | sort_by(.name))
    ' "$document" >/dev/null
}

stop_nonterminal_executions() {
  local executions execution status
  executions="$(az containerapp job execution list -g "$RESOURCE_GROUP" -n "$JOB_NAME" -o json --only-show-errors)" || return 1
  while IFS=$'\t' read -r execution status; do
    [[ -n "$execution" ]] || continue
    case "$status" in
      Succeeded|Failed|Stopped|Canceled|Cancelled) ;;
      *)
        az containerapp job stop -g "$RESOURCE_GROUP" -n "$JOB_NAME" \
          --job-execution-name "$execution" --output none --only-show-errors || return 1
        ;;
    esac
  done < <(jq -r '.[] | [.name, (.properties.status // "")] | @tsv' <<<"$executions")
}

cleanup() {
  local exit_status=$? http_status='' confirmed_absent=0
  trap - EXIT INT TERM
  set +e
  if (( CREATE_ATTEMPTED == 1 && PREFLIGHT_CONFIRMED_404 == 1 )); then
    http_status="$(arm_get_job "$JOB_RESPONSE" 2>/dev/null || true)"
    if [[ "$http_status" == "200" ]]; then
      if ! validate_job_ownership "$JOB_RESPONSE"; then
        echo "ERROR: Refusing to delete a temporary migration job whose ownership contract changed." >&2
        exit_status=1
      else
        if (( START_ATTEMPTED == 1 )); then stop_nonterminal_executions || exit_status=1; fi
        az containerapp job delete -g "$RESOURCE_GROUP" -n "$JOB_NAME" \
          --yes --output none --only-show-errors || exit_status=1
        for _ in $(seq 1 30); do
          http_status="$(arm_get_job "$JOB_RESPONSE" 2>/dev/null || true)"
          if [[ "$http_status" == "404" ]]; then confirmed_absent=1; break; fi
          sleep 2
        done
        (( confirmed_absent == 1 )) || exit_status=1
      fi
    elif [[ "$http_status" != "404" ]]; then
      echo "ERROR: Could not prove temporary migration job cleanup." >&2
      exit_status=1
    fi
  fi
  : > "$PAYLOAD" 2>/dev/null || true
  rm -f "$PAYLOAD" "$JOB_RESPONSE"
  exit "$exit_status"
}
trap cleanup EXIT INT TERM

PREFLIGHT_STATUS="$(arm_get_job "$JOB_RESPONSE")" || fail "Could not perform the migration job ARM preflight."
[[ "$PREFLIGHT_STATUS" == "404" ]] || fail "A Container Apps Job already exists with the guarded migration job name."
PREFLIGHT_CONFIRMED_404=1

jq -n \
  --arg location "$LOCATION" \
  --arg environmentId "$ENVIRONMENT_ID" \
  --arg identity "$MIGRATOR_IDENTITY" \
  --arg server "$ACR_NAME.azurecr.io" \
  --arg image "$MIGRATION_IMAGE" \
  --arg jobName "$JOB_NAME" \
  --arg databaseSecretUri "$DATABASE_PASSWORD_SECRET_URI" \
  --arg release "$RELEASE_COMMIT" \
  --arg scope "$MIGRATION_SCOPE" \
  --arg control "$CONTROL_SHA" \
  --arg runScope "$RUN_SCOPE" \
  --arg databaseHost "$DATABASE_HOST" \
  --arg databasePort "$DATABASE_PORT" \
  --arg databaseName "$DATABASE_NAME" \
  --arg databaseUser "$DATABASE_USER" '
  {
    location: $location,
    identity: {type: "UserAssigned", userAssignedIdentities: {($identity): {}}},
    tags: {
      "projectpulse-scope": $scope,
      "projectpulse-release": $release,
      "projectpulse-control": $control,
      "projectpulse-run": $runScope,
      "projectpulse-migration": "096-project-planning-document-authority"
    },
    properties: {
      environmentId: $environmentId,
      configuration: {
        triggerType: "Manual",
        replicaTimeout: 1800,
        replicaRetryLimit: 0,
        manualTriggerConfig: {replicaCompletionCount: 1, parallelism: 1},
        registries: [{server: $server, identity: $identity}],
        secrets: [{name: "main-db-password", keyVaultUrl: $databaseSecretUri, identity: $identity}]
      },
      template: {
        containers: [{
          name: $jobName,
          image: $image,
          env: [
            {name: "MAIN_RELEASE_EXPECTED_RELEASE_COMMIT", value: $release},
            {name: "PGCONNECT_TIMEOUT", value: "15"},
            {name: "PGDATABASE", value: $databaseName},
            {name: "PGHOST", value: $databaseHost},
            {name: "PGPASSWORD", secretRef: "main-db-password"},
            {name: "PGPORT", value: $databasePort},
            {name: "PGSSLROOTCERT", value: "system"},
            {name: "PGSSLMODE", value: "verify-full"},
            {name: "PGUSER", value: $databaseUser},
            {name: "PROJECTPULSE_ENVIRONMENT", value: "Test"}
          ],
          resources: {cpu: 0.5, memory: "1Gi"}
        }]
      }
    }
  }' > "$PAYLOAD"

CREATE_ATTEMPTED=1
az rest --method put --uri "$JOB_URI" --body @"$PAYLOAD" --output none --only-show-errors
: > "$PAYLOAD"

PROVISIONED=0
for _ in $(seq 1 60); do
  JOB_STATUS="$(arm_get_job "$JOB_RESPONSE")" || fail "Could not read the temporary migration job after creation."
  [[ "$JOB_STATUS" == "200" ]] || fail "The temporary migration job was not readable after creation."
  validate_job_ownership "$JOB_RESPONSE" || fail "The temporary migration job ownership or immutable configuration did not match."
  PROVISIONING_STATE="$(jq -r '.properties.provisioningState // empty' "$JOB_RESPONSE")"
  case "$PROVISIONING_STATE" in
    Succeeded) PROVISIONED=1; break ;;
    Failed|Canceled|Cancelled) fail "The temporary migration job provisioning failed." ;;
  esac
  sleep 2
done
(( PROVISIONED == 1 )) || fail "The temporary migration job did not provision successfully."

START_ATTEMPTED=1
EXECUTION_NAME="$(az containerapp job start -g "$RESOURCE_GROUP" -n "$JOB_NAME" --query name -o tsv --only-show-errors)"
normalize "$EXECUTION_NAME" >/dev/null || fail "Azure did not return the migration execution name."

for _ in $(seq 1 180); do
  STATUS="$(az containerapp job execution list -g "$RESOURCE_GROUP" -n "$JOB_NAME" \
    --query "[?name=='$EXECUTION_NAME'].properties.status | [0]" -o tsv --only-show-errors)"
  case "$STATUS" in
    Succeeded)
      echo "SYSTEMWIDE_RELIABILITY_MIGRATIONS_PRIVATE_NETWORK_JOB=SUCCEEDED"
      exit 0
      ;;
    Failed|Stopped|Canceled|Cancelled|Degraded)
      echo "SYSTEMWIDE_RELIABILITY_MIGRATIONS_PRIVATE_NETWORK_JOB=$STATUS" >&2
      break
      ;;
  esac
  sleep 5
done

az containerapp job logs show -g "$RESOURCE_GROUP" -n "$JOB_NAME" \
  --execution "$EXECUTION_NAME" --container "$JOB_NAME" --tail 250 --only-show-errors >&2 || true
fail "The protected Test system-wide reliability migration job did not succeed."
