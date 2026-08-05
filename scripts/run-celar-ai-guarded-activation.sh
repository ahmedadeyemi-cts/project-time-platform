#!/usr/bin/env bash
set -Eeuo pipefail

mode="${PROJECTPULSE_CELAR_ACTIVATION_MODE:-preflight}"
expected_sha="${PROJECTPULSE_EXPECTED_RELEASE_SHA:-}"
control_sha="${PROJECTPULSE_EXPECTED_CONTROL_SHA:-}"
environment_code="${PROJECTPULSE_ENVIRONMENT:-}"
resource_group="${AZURE_RESOURCE_GROUP:-}"
api_app="${AZURE_API_APP:-}"
web_app="${AZURE_WEB_APP:-}"
acr_name="${AZURE_ACR_NAME:-}"
key_vault_uri="${AZURE_KEY_VAULT_URI:-}"
key_vault_identity="${AZURE_KEY_VAULT_IDENTITY_RESOURCE_ID:-}"
storage_account="${PROJECTPULSE_UPLOAD_STORAGE_ACCOUNT:-}"
storage_share="${PROJECTPULSE_UPLOAD_STORAGE_SHARE:-}"
storage_name="${PROJECTPULSE_UPLOAD_ENVIRONMENT_STORAGE_NAME:-pulse-ai-documents}"
upload_root="${PROJECTPULSE_UPLOAD_ROOT:-/mnt/projectpulse/uploads}"
api_image="${PROJECTPULSE_CELAR_API_IMAGE:-}"
migration_image="${PROJECTPULSE_CELAR_MIGRATION_IMAGE:-}"
probe_image="${PROJECTPULSE_CELAR_PROBE_IMAGE:-}"
run_token="${PROJECTPULSE_CELAR_RUN_TOKEN:-local}"
change_ticket="${PROJECTPULSE_CELAR_CHANGE_TICKET:-}"
typed_confirmation="${PROJECTPULSE_CELAR_TYPED_CONFIRMATION:-}"
migrator_identity="${AZURE_CELAR_MIGRATOR_IDENTITY_RESOURCE_ID:-}"
probe_identity="${AZURE_CELAR_PROBE_IDENTITY_RESOURCE_ID:-}"
application_identity="${AZURE_CELAR_APPLICATION_IDENTITY_RESOURCE_ID:-}"

fail() { printf 'ERROR: %s\n' "$*" >&2; exit 1; }
required() { [[ -n "${!1:-}" ]] || fail "$1 is required."; }
kv_url() {
  [[ "$1" =~ ^[A-Za-z0-9-]+/[A-Za-z0-9-]{8,}$ ]] || fail "Key Vault references must be pinned as secret-name/version."
  printf '%s/secrets/%s' "${key_vault_uri%/}" "$1"
}
valid_digest() { [[ "$1" == "$acr_name.azurecr.io/"*@sha256:* && "${1##*@sha256:}" =~ ^[0-9a-f]{64}$ ]]; }
valid_base_digest() { [[ "$1" =~ ^[A-Za-z0-9][A-Za-z0-9._:/-]*@sha256:[0-9a-f]{64}$ ]]; }
valid_identity() {
  local lowered="${1,,}"
  [[ "$lowered" =~ ^/subscriptions/[a-z0-9._/-]+/providers/microsoft\.managedidentity/userassignedidentities/[a-z0-9._-]+$ ]]
}

verify_exact_source_web() {
  local app_json revision_json revisions_json active_revision_count web_mode latest_revision ready_revision
  local web_source web_image web_fqdn temp_root index_file headers_file status asset asset_url bundle_file

  app_json="$(az containerapp show -g "$resource_group" -n "$web_app" -o json --only-show-errors)"
  web_mode="$(jq -er '.properties.configuration.activeRevisionsMode' <<<"$app_json")"
  [[ "${web_mode,,}" == single ]] || fail "The exact-source web prerequisite requires single-revision mode."
  latest_revision="$(jq -er '.properties.latestRevisionName' <<<"$app_json")"
  ready_revision="$(jq -er '.properties.latestReadyRevisionName' <<<"$app_json")"
  [[ -n "$ready_revision" && "$latest_revision" == "$ready_revision" ]] ||
    fail "The web latest and latest-ready revisions do not identify one stable release."

  revisions_json="$(az containerapp revision list -g "$resource_group" -n "$web_app" -o json --only-show-errors)"
  active_revision_count="$(jq '[.[] | select(.properties.active == true)] | length' <<<"$revisions_json")"
  [[ "$active_revision_count" == 1 ]] || fail "The web prerequisite requires exactly one active revision."
  jq -e --arg revision "$ready_revision" '
    [.[] | select(.properties.active == true and .name == $revision)] | length == 1
  ' <<<"$revisions_json" >/dev/null || fail "The only active web revision is not the latest-ready revision."

  revision_json="$(az containerapp revision show -g "$resource_group" -n "$web_app" \
    --revision "$ready_revision" -o json --only-show-errors)"
  jq -e '
    .properties.active == true
    and .properties.healthState == "Healthy"
    and (.properties.runningState == "Running" or .properties.runningState == "RunningAtMaxScale")
  ' <<<"$revision_json" >/dev/null || fail "The exact-source web revision is not active, healthy, and running."
  web_source="$(jq -er '
    [.properties.template.containers[0].env[]? | select(.name == "PROJECTPULSE_SOURCE_COMMIT") | .value]
    | if length == 1 then .[0] else error("one source commit value is required") end
  ' <<<"$revision_json")" || fail "The active web revision does not expose exactly one PROJECTPULSE_SOURCE_COMMIT value."
  [[ "$web_source" == "$expected_sha" ]] || fail "The active web revision was not built for the authorized application source SHA."
  web_image="$(jq -er '.properties.template.containers[0].image' <<<"$revision_json")"
  web_fqdn="$(jq -er '.properties.fqdn // empty' <<<"$revision_json")"
  [[ -n "$web_fqdn" ]] || web_fqdn="$(jq -er '.properties.configuration.ingress.fqdn' <<<"$app_json")"
  [[ "$ready_revision" =~ ^[a-z0-9][a-z0-9-]{2,127}$ ]] || fail "The active web revision name is invalid."
  [[ "$web_image" =~ ^[A-Za-z0-9][A-Za-z0-9._:/@-]{5,500}$ ]] || fail "The active web image reference is invalid."
  [[ "$web_fqdn" =~ ^[A-Za-z0-9][A-Za-z0-9.-]{3,252}$ ]] || fail "The active web FQDN is invalid."

  temp_root="$(mktemp -d)"
  index_file="$temp_root/index.html"
  headers_file="$temp_root/index.headers"
  bundle_file="$temp_root/served.js"
  status="$(curl --silent --show-error --fail-with-body --proto '=https' --tlsv1.2 \
    --max-time 45 --max-redirs 0 -H 'Cache-Control: no-cache' -H 'Pragma: no-cache' \
    -D "$headers_file" -o "$index_file" --write-out '%{http_code}' \
    "https://$web_fqdn/index.html?celar_source=$expected_sha")" || fail "The exact-source web index could not be fetched without cache reuse."
  [[ "$status" == 200 ]] || fail "The exact-source web index returned HTTP $status."
  grep -Eqi '^cache-control:[[:space:]]*(no-store|no-cache|max-age=0)' "$headers_file" ||
    fail "The exact-source web index did not return a fail-closed cache policy."
  : > "$bundle_file"
  mapfile -t assets < <(
    grep -oE '(src|href)="[^"]+\.js([?][^"]*)?"' "$index_file" \
      | sed -E 's/^(src|href)="//; s/"$//; s/[?].*$//' \
      | sort -u
  )
  (( ${#assets[@]} > 0 )) || fail "The exact-source web index exposed no JavaScript asset."
  for asset in "${assets[@]}"; do
    case "$asset" in
      /assets/*) asset_url="https://$web_fqdn$asset" ;;
      ./assets/*) asset_url="https://$web_fqdn/${asset#./}" ;;
      assets/*) asset_url="https://$web_fqdn/$asset" ;;
      *) fail "The web index referenced an unexpected JavaScript asset origin." ;;
    esac
    curl --silent --show-error --fail-with-body --proto '=https' --tlsv1.2 \
      --max-time 45 --max-redirs 0 -H 'Cache-Control: no-cache' -H 'Pragma: no-cache' \
      "$asset_url?celar_source=$expected_sha" >> "$bundle_file" ||
      fail "An exact-source web JavaScript asset could not be fetched."
    printf '\n' >> "$bundle_file"
  done
  for marker in \
    '/api/timesheets/ai-description-suggestions' \
    'nonProjectTimeCategoryId' \
    'Generate a customer-facing description' \
    'AI Provider Configuration Center' \
    'Enable the private Celar AI target' \
    'Default: Celar AI'; do
    grep -aFq "$marker" "$bundle_file" || fail "The served exact-source web bundle is missing a required task-grounding or Celar AI marker."
  done
  rm -rf "$temp_root"

  printf 'CELAR_AI_WEB_REVISION=%s\n' "$ready_revision"
  printf 'CELAR_AI_WEB_IMAGE=%s\n' "$web_image"
  printf 'CELAR_AI_WEB_SOURCE_SHA=%s\n' "$web_source"
  printf 'CELAR_AI_EXACT_SOURCE_WEB=VERIFIED mutation=false cacheBypass=true\n'
}

[[ "$mode" == preflight || "$mode" == activate || "$mode" == verify-web ]] || fail "Activation mode must be preflight, activate, or verify-web."
[[ "$expected_sha" =~ ^[0-9a-f]{40}$ ]] || fail "An exact 40-character application source SHA is required."
[[ "$control_sha" =~ ^[0-9a-f]{40}$ ]] || fail "An exact 40-character activation-control SHA is required."
[[ "$expected_sha" != "$control_sha" ]] || fail "Application source and activation-control SHAs must be distinct."
[[ "$environment_code" == test || "$environment_code" == production ]] || fail "Environment must be test or production."
[[ "$change_ticket" =~ ^[A-Za-z0-9][A-Za-z0-9._:/-]{4,199}$ ]] || fail "A non-secret approved change-ticket reference is required."
if [[ "$mode" == activate ]]; then
  expected_confirmation="ACTIVATE-CELAR-AI-${environment_code^^}"
  [[ "$typed_confirmation" == "$expected_confirmation" ]] || fail "Typed activation confirmation does not match the selected environment."
fi
for name in AZURE_RESOURCE_GROUP AZURE_API_APP AZURE_WEB_APP AZURE_ACR_NAME AZURE_KEY_VAULT_URI \
  AZURE_KEY_VAULT_IDENTITY_RESOURCE_ID AZURE_CELAR_MIGRATOR_IDENTITY_RESOURCE_ID \
  AZURE_CELAR_PROBE_IDENTITY_RESOURCE_ID AZURE_CELAR_APPLICATION_IDENTITY_RESOURCE_ID \
  PROJECTPULSE_CELAR_API_SDK_BASE_IMAGE PROJECTPULSE_CELAR_API_RUNTIME_BASE_IMAGE \
  PROJECTPULSE_CELAR_PROBE_BASE_IMAGE PROJECTPULSE_CELAR_MIGRATOR_BASE_IMAGE \
  PROJECTPULSE_UPLOAD_STORAGE_ACCOUNT \
  PROJECTPULSE_UPLOAD_STORAGE_SHARE PROJECTPULSE_PRIVATE_ENDPOINT_HOST_ALLOWLIST \
  PROJECTPULSE_PULSE_AI_DOCUMENT_SERVICE_PRINCIPAL_USER_ID \
  PROJECTPULSE_CLAMAV_HOST PROJECTPULSE_CLAMAV_PORT \
  PROJECTPULSE_PULSE_AI_DOCUMENT_MALWARE_SCAN_APPROVAL_REFERENCE \
  PROJECTPULSE_CELAR_PRIVACY_APPROVAL_REFERENCE \
  PROJECTPULSE_AI_SECRET_ENCRYPTION_KEY_ID \
  PROJECTPULSE_AI_SECRET_ENCRYPTION_KEY_SECRET_NAME \
  PROJECTPULSE_PRIVATE_INFERENCE_ENDPOINT_SECRET_NAME \
  PROJECTPULSE_PRIVATE_INFERENCE_MODEL_SECRET_NAME \
  PROJECTPULSE_PRIVATE_INFERENCE_TOKEN_SECRET_NAME \
  PROJECTPULSE_DATABASE_URL_SECRET_NAME \
  PROJECTPULSE_ACTIVATION_SESSION_COOKIE_SECRET_NAME \
  PROJECTPULSE_E2E_TIMESHEET_REQUEST_SECRET_NAME; do required "$name"; done
required PROJECTPULSE_SOW_ACTIVATION_REQUEST_SECRET_NAME

[[ "$upload_root" == /* ]] || fail "Upload root must be absolute."
case "$upload_root" in /tmp|/tmp/*|/var/tmp|/var/tmp/*|/dev/shm|/dev/shm/*|/run|/run/*) fail "Ephemeral upload roots are forbidden." ;; esac
[[ "$PROJECTPULSE_PULSE_AI_DOCUMENT_SERVICE_PRINCIPAL_USER_ID" =~ ^[0-9a-fA-F-]{36}$ ]] || fail "Document service principal must be a UUID."
[[ "$PROJECTPULSE_CELAR_PRIVACY_APPROVAL_REFERENCE" =~ ^[A-Za-z0-9][A-Za-z0-9._:/-]{5,199}$ ]] || fail "A durable privacy/retention approval reference is required before external fallback can be enabled."
for identity in "$migrator_identity" "$probe_identity" "$application_identity"; do
  valid_identity "$identity" || fail "Each Celar one-time job requires a dedicated user-assigned managed identity resource ID."
  [[ "${identity,,}" != "${key_vault_identity,,}" ]] || fail "One-time jobs may not reuse the API Key Vault identity."
done
[[ "${migrator_identity,,}" != "${probe_identity,,}" \
  && "${migrator_identity,,}" != "${application_identity,,}" \
  && "${probe_identity,,}" != "${application_identity,,}" ]] ||
  fail "Migration, dependency/storage, and application jobs require three distinct managed identities."
for base_image_var in PROJECTPULSE_CELAR_API_SDK_BASE_IMAGE PROJECTPULSE_CELAR_API_RUNTIME_BASE_IMAGE \
  PROJECTPULSE_CELAR_PROBE_BASE_IMAGE PROJECTPULSE_CELAR_MIGRATOR_BASE_IMAGE; do
  valid_base_digest "${!base_image_var}" || fail "$base_image_var must be an immutable OCI sha256 digest reference."
done

command -v az >/dev/null || fail "Azure CLI is required."
command -v jq >/dev/null || fail "jq is required."
command -v curl >/dev/null || fail "curl is required."
az account show --output none --only-show-errors
if [[ "$mode" == verify-web ]]; then
  verify_exact_source_web
  exit 0
fi
for identity in "$migrator_identity" "$probe_identity" "$application_identity"; do
  az identity show --ids "$identity" --query id -o tsv --only-show-errors >/dev/null ||
    fail "A dedicated Celar one-time-job identity is unavailable in the protected environment."
done
api_user_identities="$(az containerapp show -g "$resource_group" -n "$api_app" \
  --query 'identity.userAssignedIdentities' -o json --only-show-errors)"
jq -e --arg id "${key_vault_identity,,}" 'keys | map(ascii_downcase) | index($id) != null' <<<"$api_user_identities" >/dev/null ||
  fail "The API Key Vault identity is not attached to the API Container App."

environment_id="$(az containerapp show -g "$resource_group" -n "$api_app" --query properties.managedEnvironmentId -o tsv --only-show-errors)"
[[ -n "$environment_id" ]] || fail "API Container App has no managed environment."
environment_name="${environment_id##*/}"
baseline_traffic="$(az containerapp ingress traffic show -g "$resource_group" -n "$api_app" -o json --only-show-errors)"
current_revision="$(jq -er '[.[] | select(.weight == 100) | .revisionName] | if length == 1 then .[0] else error("one 100-percent baseline revision is required") end' <<<"$baseline_traffic")" ||
  fail "Celar activation requires one unambiguous 100-percent baseline revision."
jq -e --arg current "$current_revision" 'all(.[]; .revisionName == $current or .weight == 0)' <<<"$baseline_traffic" >/dev/null ||
  fail "Celar activation cannot start while another revision has weighted traffic."
latest_revision="$(az containerapp show -g "$resource_group" -n "$api_app" --query properties.latestRevisionName -o tsv --only-show-errors)"
[[ "$latest_revision" == "$current_revision" ]] || fail "An unpromoted later revision exists; resolve it before guarded activation."
current_image="$(az containerapp revision show -g "$resource_group" -n "$api_app" --revision "$current_revision" \
  --query properties.template.containers[0].image -o tsv --only-show-errors)"
[[ -n "$current_image" ]] || fail "Current weighted API image could not be captured."

# Preflight proves references and infrastructure exist without retrieving values.
for secret_name_var in \
  PROJECTPULSE_AI_SECRET_ENCRYPTION_KEY_SECRET_NAME \
  PROJECTPULSE_PRIVATE_INFERENCE_ENDPOINT_SECRET_NAME \
  PROJECTPULSE_PRIVATE_INFERENCE_MODEL_SECRET_NAME \
  PROJECTPULSE_PRIVATE_INFERENCE_TOKEN_SECRET_NAME \
  PROJECTPULSE_DATABASE_URL_SECRET_NAME \
  PROJECTPULSE_ACTIVATION_SESSION_COOKIE_SECRET_NAME \
  PROJECTPULSE_E2E_TIMESHEET_REQUEST_SECRET_NAME; do
  az keyvault secret show --id "$(kv_url "${!secret_name_var}")" --query id -o tsv --only-show-errors >/dev/null ||
    fail "Required Key Vault secret reference is unavailable: $secret_name_var"
done
az keyvault secret show --id "$(kv_url "$PROJECTPULSE_SOW_ACTIVATION_REQUEST_SECRET_NAME")" --query id -o tsv --only-show-errors >/dev/null ||
  fail "Required exact-document SOW activation request is unavailable."
# Preflight deliberately inspects only the version-pinned secret reference. The
# protected value never enters the GitHub-hosted runner. The zero-traffic API
# candidate loads it through its managed-identity Key Vault reference, and the
# final Module 064 aggregate readiness gate proves that it decodes to 32 bytes.
printf 'CELAR_AI_STABLE_ENCRYPTION_KEY_REFERENCE=VERIFIED valueReadByRunner=false\n'
if [[ -n "${PROJECTPULSE_PRIVATE_EMBEDDING_ENDPOINT_SECRET_NAME:-}" ]]; then
  for secret_name_var in PROJECTPULSE_PRIVATE_EMBEDDING_ENDPOINT_SECRET_NAME \
    PROJECTPULSE_PRIVATE_EMBEDDING_MODEL_SECRET_NAME PROJECTPULSE_PRIVATE_EMBEDDING_TOKEN_SECRET_NAME; do
    required "$secret_name_var"
    az keyvault secret show --id "$(kv_url "${!secret_name_var}")" --query id -o tsv --only-show-errors >/dev/null ||
      fail "Required private embedding secret reference is unavailable: $secret_name_var"
  done
else
  [[ "${PROJECTPULSE_PULSE_AI_ALLOW_LEXICAL_ONLY_COMPLETION:-false}" == true ]] || fail "Configure private embeddings or explicitly approve lexical-only completion."
  required PROJECTPULSE_PULSE_AI_LEXICAL_ONLY_APPROVAL_REFERENCE
fi
if [[ -n "${PROJECTPULSE_PRIVATE_OCR_ENDPOINT_SECRET_NAME:-}" ]]; then
  for secret_name_var in PROJECTPULSE_PRIVATE_OCR_ENDPOINT_SECRET_NAME \
    PROJECTPULSE_PRIVATE_OCR_MODEL_SECRET_NAME PROJECTPULSE_PRIVATE_OCR_TOKEN_SECRET_NAME; do
    required "$secret_name_var"
    az keyvault secret show --id "$(kv_url "${!secret_name_var}")" --query id -o tsv --only-show-errors >/dev/null ||
      fail "Required private OCR secret reference is unavailable: $secret_name_var"
  done
fi
az storage share-rm show --resource-group "$resource_group" --storage-account "$storage_account" \
  --name "$storage_share" --query name -o tsv --only-show-errors >/dev/null || fail "Persistent Azure Files share is unavailable."

printf 'CELAR_AI_PREFLIGHT=PASSED environment=%s source=%s control=%s\n' "$environment_code" "$expected_sha" "$control_sha"
[[ "$mode" == activate ]] || exit 0

valid_digest "$api_image" || fail "API image must be an immutable digest in the approved ACR."
valid_digest "$migration_image" || fail "Migration image must be an immutable digest in the approved ACR."
valid_digest "$probe_image" || fail "Probe image must be an immutable digest in the approved ACR."
[[ "$run_token" =~ ^[a-z0-9][a-z0-9-]{3,20}$ ]] || fail "Run token is invalid."

candidate_revision=""
promoted=false
rollback_candidate() {
  status=$?
  trap - EXIT INT TERM
  if (( status != 0 )) && [[ -n "$candidate_revision" ]]; then
    traffic="$(az containerapp ingress traffic show -g "$resource_group" -n "$api_app" -o json --only-show-errors 2>/dev/null || printf '[]')"
    if jq -e --arg current "$current_revision" --arg candidate "$candidate_revision" '
      all(.[]; .revisionName == $current or .revisionName == $candidate)
      and ([.[] | select(.revisionName == $current and .weight == 100)] | length == 1)
      and ([.[] | select(.revisionName == $candidate and .weight == 0)] | length == 1)
    ' <<<"$traffic" >/dev/null 2>&1; then
      az containerapp ingress traffic set -g "$resource_group" -n "$api_app" \
        --revision-weight "$current_revision=100" "$candidate_revision=0" --output none --only-show-errors || true
      az containerapp revision deactivate -g "$resource_group" -n "$api_app" --revision "$candidate_revision" \
        --output none --only-show-errors || true
      printf 'CELAR_AI_CANDIDATE_ROLLBACK=COMPLETE databaseRollback=false\n' >&2
    else
      printf 'CELAR_AI_CANDIDATE_ROLLBACK=SKIPPED reason=later_release_or_traffic_change\n' >&2
    fi
  fi
  exit "$status"
}
trap rollback_candidate EXIT INT TERM

# Register the Azure Files share on the Container Apps environment. The account
# key is held only in memory for this command and is never emitted.
storage_key="$(az storage account keys list -g "$resource_group" -n "$storage_account" --query '[0].value' -o tsv --only-show-errors)"
[[ -n "$storage_key" ]] || fail "Azure Files account key could not be resolved."
printf '::add-mask::%s\n' "$storage_key"
az containerapp env storage set -g "$resource_group" -n "$environment_name" --storage-name "$storage_name" \
  --azure-file-account-name "$storage_account" --azure-file-account-key "$storage_key" \
  --azure-file-share-name "$storage_share" --access-mode ReadWrite --output none --only-show-errors
unset storage_key

# The API stores only Key Vault references. Secret values never become workflow
# inputs, outputs, command logs, or generated specifications.
declare -A secret_refs=(
  [pp-ai-key]="$PROJECTPULSE_AI_SECRET_ENCRYPTION_KEY_SECRET_NAME"
  [pp-private-endpoint]="$PROJECTPULSE_PRIVATE_INFERENCE_ENDPOINT_SECRET_NAME"
  [pp-private-model]="$PROJECTPULSE_PRIVATE_INFERENCE_MODEL_SECRET_NAME"
  [pp-private-token]="$PROJECTPULSE_PRIVATE_INFERENCE_TOKEN_SECRET_NAME"
)
secret_args=()
for app_secret in "${!secret_refs[@]}"; do
  secret_args+=("$app_secret=keyvaultref:$(kv_url "${secret_refs[$app_secret]}"),identityref:$key_vault_identity")
done
if [[ -n "${PROJECTPULSE_PRIVATE_EMBEDDING_ENDPOINT_SECRET_NAME:-}" ]]; then
  secret_args+=(
    "pp-embedding-endpoint=keyvaultref:$(kv_url "$PROJECTPULSE_PRIVATE_EMBEDDING_ENDPOINT_SECRET_NAME"),identityref:$key_vault_identity"
    "pp-embedding-model=keyvaultref:$(kv_url "$PROJECTPULSE_PRIVATE_EMBEDDING_MODEL_SECRET_NAME"),identityref:$key_vault_identity"
    "pp-embedding-token=keyvaultref:$(kv_url "$PROJECTPULSE_PRIVATE_EMBEDDING_TOKEN_SECRET_NAME"),identityref:$key_vault_identity"
  )
fi
if [[ -n "${PROJECTPULSE_PRIVATE_OCR_ENDPOINT_SECRET_NAME:-}" ]]; then
  secret_args+=(
    "pp-ocr-endpoint=keyvaultref:$(kv_url "$PROJECTPULSE_PRIVATE_OCR_ENDPOINT_SECRET_NAME"),identityref:$key_vault_identity"
    "pp-ocr-model=keyvaultref:$(kv_url "$PROJECTPULSE_PRIVATE_OCR_MODEL_SECRET_NAME"),identityref:$key_vault_identity"
    "pp-ocr-token=keyvaultref:$(kv_url "$PROJECTPULSE_PRIVATE_OCR_TOKEN_SECRET_NAME"),identityref:$key_vault_identity"
  )
fi
az containerapp secret set -g "$resource_group" -n "$api_app" --secrets "${secret_args[@]}" --output none --only-show-errors
az containerapp revision set-mode -g "$resource_group" -n "$api_app" --mode multiple --output none --only-show-errors

spec="$(mktemp)"
candidate="$(mktemp)"
az containerapp show -g "$resource_group" -n "$api_app" -o json --only-show-errors > "$spec"
env_json="$(jq -nc \
  --arg environment "$environment_code" --arg upload "$upload_root" \
  --arg source "$expected_sha" \
  --arg keyId "$PROJECTPULSE_AI_SECRET_ENCRYPTION_KEY_ID" \
  --arg allowlist "$PROJECTPULSE_PRIVATE_ENDPOINT_HOST_ALLOWLIST" \
  --arg servicePrincipal "$PROJECTPULSE_PULSE_AI_DOCUMENT_SERVICE_PRINCIPAL_USER_ID" \
  --arg privacyApproval "$PROJECTPULSE_CELAR_PRIVACY_APPROVAL_REFERENCE" \
  --arg clamHost "${PROJECTPULSE_CLAMAV_HOST:-}" --arg clamPort "${PROJECTPULSE_CLAMAV_PORT:-3310}" \
  --arg scanApproval "${PROJECTPULSE_PULSE_AI_DOCUMENT_MALWARE_SCAN_APPROVAL_REFERENCE:-}" \
  --arg lexical "${PROJECTPULSE_PULSE_AI_ALLOW_LEXICAL_ONLY_COMPLETION:-false}" \
  --arg lexicalApproval "${PROJECTPULSE_PULSE_AI_LEXICAL_ONLY_APPROVAL_REFERENCE:-}" \
  '[
    {name:"PROJECTPULSE_ENVIRONMENT",value:$environment},
    {name:"PROJECTPULSE_SOURCE_COMMIT",value:$source},
    {name:"PROJECTPULSE_UPLOAD_ROOT",value:$upload},
    {name:"PROJECTPULSE_UPLOAD_ROOT_SHARED_PERSISTENT",value:"true"},
    {name:"PROJECTPULSE_AI_SECRET_ENCRYPTION_KEY",secretRef:"pp-ai-key"},
    {name:"PROJECTPULSE_AI_SECRET_ENCRYPTION_KEY_ID",value:$keyId},
    {name:"PROJECTPULSE_PRIVATE_INFERENCE_ENDPOINT",secretRef:"pp-private-endpoint"},
    {name:"PROJECTPULSE_PRIVATE_INFERENCE_MODEL",secretRef:"pp-private-model"},
    {name:"PROJECTPULSE_PRIVATE_INFERENCE_BEARER_TOKEN",secretRef:"pp-private-token"},
    {name:"PROJECTPULSE_PRIVATE_INFERENCE_AUTH_MODE",value:"bearer"},
    {name:"PROJECTPULSE_PRIVATE_ENDPOINT_HOST_ALLOWLIST",value:$allowlist},
    {name:"PROJECTPULSE_PULSE_AI_PRIVATE_RAG_ENABLED",value:"true"},
    {name:"PROJECTPULSE_PULSE_AI_RAG_REQUIRE_PRIVATE_MODEL",value:"true"},
    {name:"PROJECTPULSE_PULSE_AI_PRIVATE_RUNTIME_WORKER_ENABLED",value:"true"},
    {name:"PROJECTPULSE_PULSE_AI_AUTO_QUEUE_ELIGIBLE_DOCUMENTS",value:"true"},
    {name:"PROJECTPULSE_PULSE_AI_DOCUMENT_SERVICE_PRINCIPAL_USER_ID",value:$servicePrincipal},
    {name:"PROJECTPULSE_PULSE_AI_DOCUMENT_MALWARE_SCANNER_MODE",value:"clamav_tcp"},
    {name:"PROJECTPULSE_PULSE_AI_CLAMAV_HOST",value:$clamHost},
    {name:"PROJECTPULSE_PULSE_AI_CLAMAV_PORT",value:$clamPort},
    {name:"PROJECTPULSE_PULSE_AI_DOCUMENT_MALWARE_SCAN_APPROVAL_REFERENCE",value:$scanApproval},
    {name:"PROJECTPULSE_PULSE_AI_ALLOW_LEXICAL_ONLY_COMPLETION",value:$lexical},
    {name:"PROJECTPULSE_PULSE_AI_LEXICAL_ONLY_APPROVAL_REFERENCE",value:$lexicalApproval},
    {name:"PROJECTPULSE_AI_ALLOW_SANITIZED_EXTERNAL_ESCALATION",value:"true"},
    {name:"PROJECTPULSE_CELAR_AI_SANITIZED_EXTERNAL_FALLBACK_ENABLED",value:"true"},
    {name:"PROJECTPULSE_CELAR_PRIVACY_APPROVAL_REFERENCE",value:$privacyApproval}
  ]')"
if [[ -n "${PROJECTPULSE_PRIVATE_EMBEDDING_ENDPOINT_SECRET_NAME:-}" ]]; then
  env_json="$(jq '. + [{name:"PROJECTPULSE_PRIVATE_EMBEDDING_ENDPOINT",secretRef:"pp-embedding-endpoint"},{name:"PROJECTPULSE_PRIVATE_EMBEDDING_MODEL",secretRef:"pp-embedding-model"},{name:"PROJECTPULSE_PRIVATE_EMBEDDING_BEARER_TOKEN",secretRef:"pp-embedding-token"}]' <<<"$env_json")"
fi
if [[ -n "${PROJECTPULSE_PRIVATE_OCR_ENDPOINT_SECRET_NAME:-}" ]]; then
  env_json="$(jq '. + [{name:"PROJECTPULSE_PRIVATE_OCR_ENDPOINT",secretRef:"pp-ocr-endpoint"},{name:"PROJECTPULSE_PRIVATE_OCR_MODEL",secretRef:"pp-ocr-model"},{name:"PROJECTPULSE_PRIVATE_OCR_BEARER_TOKEN",secretRef:"pp-ocr-token"}]' <<<"$env_json")"
fi
jq --arg image "$api_image" --arg storage "$storage_name" --arg root "$upload_root" --argjson desired "$env_json" '
  .properties.configuration.activeRevisionsMode = "Multiple"
  | .properties.template.containers[0].image = $image
  | .properties.template.containers[0].env = ((.properties.template.containers[0].env // []) as $old | ($desired | map(.name)) as $names | ($old | map(select(.name as $n | $names | index($n) | not))) + $desired)
  | .properties.template.containers[0].volumeMounts = (((.properties.template.containers[0].volumeMounts // []) | map(select(.volumeName != "pulse-ai-documents"))) + [{volumeName:"pulse-ai-documents",mountPath:$root}])
  | .properties.template.volumes = (((.properties.template.volumes // []) | map(select(.name != "pulse-ai-documents"))) + [{name:"pulse-ai-documents",storageType:"AzureFile",storageName:$storage}])
  | del(.id,.name,.type,.systemData,.resourceGroup,.properties.latestRevisionName,.properties.latestReadyRevisionName,.properties.latestRevisionFqdn,.properties.provisioningState,.properties.runningStatus,.properties.eventStreamEndpoint)
' "$spec" > "$candidate"

suffix="celar-${run_token}"
az containerapp update -g "$resource_group" -n "$api_app" --yaml "$candidate" --revision-suffix "$suffix" --output none --only-show-errors
candidate_revision="$(az containerapp revision list -g "$resource_group" -n "$api_app" \
  --query "[?ends_with(name, '$suffix')].name | [0]" -o tsv --only-show-errors)"
[[ -n "$candidate_revision" && "$candidate_revision" != "$current_revision" ]] || fail "Candidate revision was not created."
candidate_revision_json="$(az containerapp revision show -g "$resource_group" -n "$api_app" \
  --revision "$candidate_revision" -o json --only-show-errors)"
candidate_fqdn="$(jq -er '.properties.fqdn' <<<"$candidate_revision_json")"
[[ -n "$candidate_fqdn" ]] || fail "Candidate FQDN is unavailable."
candidate_source="$(jq -er '
  [.properties.template.containers[0].env[]? | select(.name == "PROJECTPULSE_SOURCE_COMMIT") | .value]
  | if length == 1 then .[0] else error("one source commit value is required") end
' <<<"$candidate_revision_json")" || fail "The candidate API revision does not expose exactly one PROJECTPULSE_SOURCE_COMMIT value."
[[ "$candidate_source" == "$expected_sha" ]] || fail "The candidate API revision source commit does not match the authorized application source SHA."
az containerapp ingress traffic set -g "$resource_group" -n "$api_app" --revision-weight "$current_revision=100" "$candidate_revision=0" --output none --only-show-errors

# The workflow invokes checksum-locked migration and private-network probe jobs
# after this candidate is staged. Promotion is a separate invocation so failure
# can only deactivate the candidate; additive migrations remain intact.
printf 'CELAR_AI_CANDIDATE_REVISION=%s\n' "$candidate_revision"
printf 'CELAR_AI_CANDIDATE_FQDN=%s\n' "$candidate_fqdn"
printf 'CELAR_AI_PREVIOUS_REVISION=%s\n' "$current_revision"
printf 'CELAR_AI_SOURCE_SHA=%s\n' "$expected_sha"
printf 'CELAR_AI_CONTROL_SHA=%s\n' "$control_sha"
printf 'CELAR_AI_CANDIDATE_STAGED=PASSED\n'
trap - EXIT INT TERM
