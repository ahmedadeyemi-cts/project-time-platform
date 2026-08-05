#!/usr/bin/env bash
set -Eeuo pipefail

kind="${PROJECTPULSE_CELAR_JOB_KIND:-}"
resource_group="${AZURE_RESOURCE_GROUP:-}"
api_app="${AZURE_API_APP:-}"
job_name="${PROJECTPULSE_CELAR_JOB_NAME:-}"
image="${PROJECTPULSE_CELAR_JOB_IMAGE:-}"
key_vault_uri="${AZURE_KEY_VAULT_URI:-}"
acr_name="${AZURE_ACR_NAME:-}"
expected_sha="${PROJECTPULSE_EXPECTED_RELEASE_SHA:-}"
control_sha="${PROJECTPULSE_EXPECTED_CONTROL_SHA:-}"
upload_root="${PROJECTPULSE_UPLOAD_ROOT:-/mnt/projectpulse/uploads}"
storage_name="${PROJECTPULSE_UPLOAD_ENVIRONMENT_STORAGE_NAME:-pulse-ai-documents}"

fail() { printf 'ERROR: %s\n' "$*" >&2; exit 1; }
kv_url() {
  [[ "$1" =~ ^[A-Za-z0-9-]+/[A-Za-z0-9-]{8,}$ ]] || fail "Key Vault references must be pinned as secret-name/version."
  printf '%s/secrets/%s' "${key_vault_uri%/}" "$1"
}
[[ "$kind" =~ ^(migration|dependencies|storage-write|storage-read|application)$ ]] || fail "Unsupported Celar job kind."
[[ "$job_name" =~ ^[a-z][a-z0-9-]{4,30}[a-z0-9]$ ]] || fail "Invalid one-time job name."
[[ "$expected_sha" =~ ^[0-9a-f]{40}$ ]] || fail "Exact release SHA is required."
[[ "$control_sha" =~ ^[0-9a-f]{40}$ && "$control_sha" != "$expected_sha" ]] || fail "A distinct exact control SHA is required."
[[ -n "$resource_group" && -n "$api_app" && -n "$image" && -n "$key_vault_uri" && -n "$acr_name" ]] || fail "Azure job configuration is incomplete."
[[ "$image" == "$acr_name.azurecr.io/"*@sha256:* && "${image##*@sha256:}" =~ ^[0-9a-f]{64}$ ]] ||
  fail "The one-time job image must be an immutable digest in the approved ACR."

case "$kind" in
  migration) job_identity="${AZURE_CELAR_MIGRATOR_IDENTITY_RESOURCE_ID:-}" ;;
  application) job_identity="${AZURE_CELAR_APPLICATION_IDENTITY_RESOURCE_ID:-}" ;;
  *) job_identity="${AZURE_CELAR_PROBE_IDENTITY_RESOURCE_ID:-}" ;;
esac
job_identity_lower="${job_identity,,}"
[[ "$job_identity_lower" =~ ^/subscriptions/[a-z0-9._/-]+/providers/microsoft\.managedidentity/userassignedidentities/[a-z0-9._-]+$ ]] ||
  fail "A dedicated user-assigned managed identity is required for the $kind job."

app="$(mktemp)"
spec="$(mktemp)"
logs="$(mktemp)"
az containerapp show -g "$resource_group" -n "$api_app" -o json --only-show-errors > "$app"
environment_id="$(jq -er '.properties.managedEnvironmentId' "$app")"
location="$(jq -er '.location' "$app")"
identity="$(jq -nc --arg id "$job_identity" '{type:"UserAssigned",userAssignedIdentities:{($id):{}}}')"
registries="$(jq -nc --arg server "$acr_name.azurecr.io" --arg identity "$job_identity" '[{server:$server,identity:$identity}]')"

secrets='[]'
envs="$(jq -nc --arg kind "$kind" --arg source "$expected_sha" --arg control "$control_sha" \
  '[{name:"PROJECTPULSE_CELAR_PROBE_MODE",value:$kind},{name:"PROJECTPULSE_EXPECTED_RELEASE_SHA",value:$source},{name:"PROJECTPULSE_EXPECTED_CONTROL_SHA",value:$control}]')"
volumes='[]'
mounts='[]'
timeout=1800
container_name=celar-probe

add_kv_secret() {
  local app_name="$1" source_name="$2" env_name="$3"
  [[ -n "$source_name" ]] || fail "Key Vault source is missing for $env_name."
  secrets="$(jq --arg name "$app_name" --arg url "$(kv_url "$source_name")" --arg identity "$job_identity" '. + [{name:$name,keyVaultUrl:$url,identity:$identity}]' <<<"$secrets")"
  envs="$(jq --arg env "$env_name" --arg ref "$app_name" '. + [{name:$env,secretRef:$ref}]' <<<"$envs")"
}
add_value() { envs="$(jq --arg env "$1" --arg value "$2" '. + [{name:$env,value:$value}]' <<<"$envs")"; }

case "$kind" in
  migration)
    container_name=celar-migrations
    timeout=1800
    add_kv_secret pp-db "$PROJECTPULSE_DATABASE_URL_SECRET_NAME" PROJECTPULSE_DATABASE_URL
    add_value PROJECTPULSE_AI_SECRET_ENCRYPTION_KEY_ID "$PROJECTPULSE_AI_SECRET_ENCRYPTION_KEY_ID"
    add_value PROJECTPULSE_CELAR_MIGRATION_MODE "${PROJECTPULSE_CELAR_MIGRATION_MODE:-verify}"
    ;;
  dependencies)
    add_kv_secret pp-inference-endpoint "$PROJECTPULSE_PRIVATE_INFERENCE_ENDPOINT_SECRET_NAME" PROJECTPULSE_PRIVATE_INFERENCE_ENDPOINT
    add_kv_secret pp-inference-model "$PROJECTPULSE_PRIVATE_INFERENCE_MODEL_SECRET_NAME" PROJECTPULSE_PRIVATE_INFERENCE_MODEL
    add_kv_secret pp-inference-token "$PROJECTPULSE_PRIVATE_INFERENCE_TOKEN_SECRET_NAME" PROJECTPULSE_PRIVATE_INFERENCE_BEARER_TOKEN
    add_value PROJECTPULSE_PRIVATE_ENDPOINT_HOST_ALLOWLIST "$PROJECTPULSE_PRIVATE_ENDPOINT_HOST_ALLOWLIST"
    add_value PROJECTPULSE_CLAMAV_HOST "$PROJECTPULSE_CLAMAV_HOST"
    add_value PROJECTPULSE_CLAMAV_PORT "$PROJECTPULSE_CLAMAV_PORT"
    if [[ -n "${PROJECTPULSE_PRIVATE_EMBEDDING_ENDPOINT_SECRET_NAME:-}" ]]; then
      add_kv_secret pp-embedding-endpoint "$PROJECTPULSE_PRIVATE_EMBEDDING_ENDPOINT_SECRET_NAME" PROJECTPULSE_PRIVATE_EMBEDDING_ENDPOINT
      add_kv_secret pp-embedding-model "$PROJECTPULSE_PRIVATE_EMBEDDING_MODEL_SECRET_NAME" PROJECTPULSE_PRIVATE_EMBEDDING_MODEL
      add_kv_secret pp-embedding-token "$PROJECTPULSE_PRIVATE_EMBEDDING_TOKEN_SECRET_NAME" PROJECTPULSE_PRIVATE_EMBEDDING_BEARER_TOKEN
    else
      add_value PROJECTPULSE_PULSE_AI_ALLOW_LEXICAL_ONLY_COMPLETION "true"
      add_value PROJECTPULSE_PULSE_AI_LEXICAL_ONLY_APPROVAL_REFERENCE "$PROJECTPULSE_PULSE_AI_LEXICAL_ONLY_APPROVAL_REFERENCE"
    fi
    if [[ -n "${PROJECTPULSE_PRIVATE_OCR_ENDPOINT_SECRET_NAME:-}" ]]; then
      add_kv_secret pp-ocr-endpoint "$PROJECTPULSE_PRIVATE_OCR_ENDPOINT_SECRET_NAME" PROJECTPULSE_PRIVATE_OCR_ENDPOINT
      add_kv_secret pp-ocr-model "$PROJECTPULSE_PRIVATE_OCR_MODEL_SECRET_NAME" PROJECTPULSE_PRIVATE_OCR_MODEL
      add_kv_secret pp-ocr-token "$PROJECTPULSE_PRIVATE_OCR_TOKEN_SECRET_NAME" PROJECTPULSE_PRIVATE_OCR_BEARER_TOKEN
    fi
    ;;
  storage-write|storage-read)
    add_value PROJECTPULSE_UPLOAD_ROOT "$upload_root"
    add_value PROJECTPULSE_UPLOAD_ROOT_SHARED_PERSISTENT "true"
    add_value PROJECTPULSE_STORAGE_CANARY "$PROJECTPULSE_STORAGE_CANARY"
    volumes="$(jq -nc --arg storage "$storage_name" '[{name:"pulse-ai-documents",storageType:"AzureFile",storageName:$storage}]')"
    mounts="$(jq -nc --arg root "$upload_root" '[{volumeName:"pulse-ai-documents",mountPath:$root}]')"
    ;;
  application)
    timeout=2400
    add_value PROJECTPULSE_CANDIDATE_URL "$PROJECTPULSE_CANDIDATE_URL"
    add_value PROJECTPULSE_CELAR_CONFIGURE_PROFILE "true"
    add_value PROJECTPULSE_CELAR_CHANGE_TICKET "$PROJECTPULSE_CELAR_CHANGE_TICKET"
    add_value PROJECTPULSE_PRIVATE_ENDPOINT_HOST_ALLOWLIST "$PROJECTPULSE_PRIVATE_ENDPOINT_HOST_ALLOWLIST"
    add_kv_secret pp-session "$PROJECTPULSE_ACTIVATION_SESSION_COOKIE_SECRET_NAME" PROJECTPULSE_ACTIVATION_SESSION_COOKIE
    add_kv_secret pp-e2e "$PROJECTPULSE_E2E_TIMESHEET_REQUEST_SECRET_NAME" PROJECTPULSE_E2E_TIMESHEET_REQUEST_JSON
    add_kv_secret pp-sow-activation "$PROJECTPULSE_SOW_ACTIVATION_REQUEST_SECRET_NAME" PROJECTPULSE_SOW_ACTIVATION_REQUEST_JSON
    add_kv_secret pp-inference-endpoint "$PROJECTPULSE_PRIVATE_INFERENCE_ENDPOINT_SECRET_NAME" PROJECTPULSE_PRIVATE_INFERENCE_ENDPOINT
    add_kv_secret pp-inference-model "$PROJECTPULSE_PRIVATE_INFERENCE_MODEL_SECRET_NAME" PROJECTPULSE_PRIVATE_INFERENCE_MODEL
    add_kv_secret pp-inference-token "$PROJECTPULSE_PRIVATE_INFERENCE_TOKEN_SECRET_NAME" PROJECTPULSE_PRIVATE_INFERENCE_BEARER_TOKEN
    ;;
esac

jq -n --arg location "$location" --arg envId "$environment_id" --arg name "$container_name" \
  --arg image "$image" --argjson identity "$identity" --argjson registries "$registries" \
  --argjson secrets "$secrets" --argjson envs "$envs" --argjson volumes "$volumes" \
  --argjson mounts "$mounts" --argjson timeout "$timeout" '
  {
    location:$location,
    identity:$identity,
    properties:{
      environmentId:$envId,
      configuration:{triggerType:"Manual",replicaTimeout:$timeout,replicaRetryLimit:0,registries:$registries,secrets:$secrets},
      template:{containers:[{name:$name,image:$image,env:$envs,resources:{cpu:0.5,memory:"1Gi"},volumeMounts:$mounts}],volumes:$volumes}
    }
  }' > "$spec"

cleanup() {
  status=$?
  trap - EXIT INT TERM
  az containerapp job delete -g "$resource_group" -n "$job_name" --yes --output none --only-show-errors || true
  rm -f "$app" "$spec" "$logs"
  exit "$status"
}
trap cleanup EXIT INT TERM

az containerapp job create -g "$resource_group" -n "$job_name" --yaml "$spec" --output none --only-show-errors
execution="$(az containerapp job start -g "$resource_group" -n "$job_name" --query name -o tsv --only-show-errors)"
[[ -n "$execution" ]] || fail "Azure did not return a job execution."
for _ in $(seq 1 240); do
  status="$(az containerapp job execution list -g "$resource_group" -n "$job_name" \
    --query "[?name=='$execution'].properties.status | [0]" -o tsv --only-show-errors)"
  case "$status" in
    Succeeded) break ;;
    Failed|Stopped|Canceled|Degraded) fail "Celar $kind job failed with status $status." ;;
  esac
  sleep 5
done
[[ "${status:-}" == Succeeded ]] || fail "Celar $kind job timed out."
az containerapp job logs show -g "$resource_group" -n "$job_name" --execution "$execution" \
  --container "$container_name" --tail 300 --only-show-errors > "$logs"
cat "$logs"
grep -Eq 'CELAR_AI_.*=(VERIFIED|PASSED|CONFIGURED|EXPLICITLY_APPROVED)' "$logs" || fail "Celar job produced no success evidence."
printf 'CELAR_AI_CONTAINERAPP_JOB=SUCCEEDED kind=%s\n' "$kind"
