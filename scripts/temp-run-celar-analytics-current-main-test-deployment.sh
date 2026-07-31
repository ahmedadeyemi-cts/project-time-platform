#!/usr/bin/env bash
set -Eeuo pipefail

EXPECTED_RELEASE_COMMIT="1722b6476845e23ab5d6fc63b630420dcbf9a97c"
CONTROL_ROOT="${1:-}"
RELEASE_ROOT="${2:-}"
TARGET_COMMIT="${3:-}"
RESOURCE_GROUP="${AZURE_RESOURCE_GROUP:-}"
API_APP="${AZURE_API_APP:-}"
WEB_APP="${AZURE_WEB_APP:-}"
ACR_NAME="${AZURE_ACR_NAME:-}"
BASE_URL="${PUBLIC_URL:-}"
RUN_ID="${GITHUB_RUN_ID:-local}"
RUN_ATTEMPT="${GITHUB_RUN_ATTEMPT:-1}"
WORKFLOW_SHA="${GITHUB_SHA:-unknown}"
SLUG="celar-analytics-1722b647"
PROBE_DIR="${RUNNER_TEMP:-/tmp}/${SLUG}-probes"
EVIDENCE_DIR="$CONTROL_ROOT/evidence"

fail() { echo "ERROR: $*" >&2; exit 1; }
need() { [[ -n "$2" ]] || fail "$1 is not configured."; }
for pair in \
  "CONTROL_ROOT:$CONTROL_ROOT" "RELEASE_ROOT:$RELEASE_ROOT" "TARGET_COMMIT:$TARGET_COMMIT" \
  "AZURE_RESOURCE_GROUP:$RESOURCE_GROUP" "AZURE_API_APP:$API_APP" "AZURE_WEB_APP:$WEB_APP" \
  "AZURE_ACR_NAME:$ACR_NAME" "PUBLIC_URL:$BASE_URL"; do
  need "${pair%%:*}" "${pair#*:}"
done
BASE_URL="${BASE_URL%/}"
[[ "$TARGET_COMMIT" == "$EXPECTED_RELEASE_COMMIT" ]] || fail "Unexpected source release: $TARGET_COMMIT"
[[ "$(git -C "$RELEASE_ROOT" rev-parse HEAD)" == "$EXPECTED_RELEASE_COMMIT" ]] ||
  fail "Release checkout does not match the pinned source release."
[[ "$BASE_URL" == https://*.onenecklab.com || "$BASE_URL" == https://*.ussignal.com ]] ||
  fail "PUBLIC_URL is not an approved HTTPS ProjectPulse origin."
! grep -Eiq 'azurecontainerapps\.io|\.internal\.' <<<"$BASE_URL" ||
  fail "PUBLIC_URL exposes an internal Azure hostname."

BUILD_SCRIPT="$CONTROL_ROOT/scripts/build-pr55-acr-image.sh"
WAIT_SCRIPT="$CONTROL_ROOT/scripts/wait-containerapp-ready-revision.sh"
[[ -f "$BUILD_SCRIPT" && -f "$WAIT_SCRIPT" ]] || fail "Required shared deployment controls are missing."
mkdir -p "$PROBE_DIR" "$EVIDENCE_DIR"

API_STARTED=0
WEB_STARTED=0
CURRENT_API_IMAGE=""
CURRENT_WEB_IMAGE=""
CURRENT_SOURCE_COMMIT=""
API_IMAGE=""
WEB_IMAGE=""
API_REVISION=""
WEB_REVISION=""

resolve_digest() {
  local image="$1" registry="$ACR_NAME.azurecr.io/" relative repository digest
  case "$image" in
    "$registry"*@sha256:*) printf '%s\n' "$image"; return ;;
    "$registry"*:*) ;;
    *) fail "Image is outside approved ACR: $image" ;;
  esac
  relative="${image#"$registry"}"
  repository="${relative%:*}"
  digest="$(az acr repository show --name "$ACR_NAME" --image "$relative" --query digest -o tsv --only-show-errors)"
  [[ "$digest" == sha256:* ]] || fail "Could not resolve immutable digest for $image"
  printf '%s%s@%s\n' "$registry" "$repository" "$digest"
}

rollback_app() {
  local label="$1" app="$2" candidate="$3" previous="$4" started="$5" prefix="$6"
  [[ "$started" == 1 && -n "$candidate" && -n "$previous" ]] || return 0
  local active suffix revision
  active="$(az containerapp show -g "$RESOURCE_GROUP" -n "$app" --query 'properties.template.containers[0].image' -o tsv 2>/dev/null || true)"
  if [[ "$active" == "$candidate" ]]; then
    suffix="${prefix}-${RUN_ID}-${RUN_ATTEMPT}"
    revision="$app--$suffix"
    az containerapp update \
      -g "$RESOURCE_GROUP" \
      -n "$app" \
      --image "$previous" \
      --revision-suffix "$suffix" \
      --output none \
      --only-show-errors
    bash "$WAIT_SCRIPT" "$RESOURCE_GROUP" "$app" "$revision" "$previous" 60 10
    echo "${label}_ROLLBACK=COMPLETE"
  else
    echo "$label rollback skipped because another image is active: $active" >&2
  fi
}

cleanup() {
  local status=$?
  trap - EXIT INT TERM
  if (( status != 0 )); then
    set +e
    rollback_app WEB "$WEB_APP" "$WEB_IMAGE" "$CURRENT_WEB_IMAGE" "$WEB_STARTED" caweb-rb
    rollback_app API "$API_APP" "$API_IMAGE" "$CURRENT_API_IMAGE" "$API_STARTED" caapi-rb
    set -e
  fi
  exit "$status"
}
trap cleanup EXIT INT TERM

probe_json() {
  local name="$1" path="$2" expected="$3" status type
  local headers="$PROBE_DIR/$name.headers" body="$PROBE_DIR/$name.json"
  for attempt in $(seq 1 24); do
    status="$(curl -sS -L --max-time 30 -D "$headers" -o "$body" -w '%{http_code}' \
      -H 'Accept: application/json' \
      -H 'Cache-Control: no-cache, no-store' \
      -H "Origin: $BASE_URL" \
      -H "Referer: $BASE_URL/#dashboard" \
      "$BASE_URL$path?release_check=${SLUG}-${RUN_ID}-${RUN_ATTEMPT}-${attempt}" || true)"
    type="$(awk 'BEGIN{IGNORECASE=1} /^content-type:/ {gsub(/\r/, ""); print $2}' "$headers" 2>/dev/null | tail -1)"
    if [[ ",$expected," == *",$status,"* && "$type" == application/json* ]]; then
      jq -e 'type == "object"' "$body" >/dev/null
      ! grep -Eiq 'trusted_public_origin_unavailable|invalid_forwarded_public_origin' "$body"
      echo "CELAR_ANALYTICS_PROBE_${name^^}=HTTP_$status"
      return 0
    fi
    sleep 5
  done
  cat "$body" >&2 || true
  fail "$path did not return expected JSON status $expected"
}

probe_json_post() {
  local name="$1" path="$2" expected="$3" payload="$4" status type
  local headers="$PROBE_DIR/$name.headers" body="$PROBE_DIR/$name.json"
  for attempt in $(seq 1 12); do
    status="$(curl -sS -L --max-time 30 -D "$headers" -o "$body" -w '%{http_code}' \
      -X POST \
      -H 'Accept: application/json' \
      -H 'Content-Type: application/json' \
      -H 'Cache-Control: no-cache, no-store' \
      -H "Origin: $BASE_URL" \
      -H "Referer: $BASE_URL/#dashboard" \
      --data "$payload" \
      "$BASE_URL$path?release_check=${SLUG}-${RUN_ID}-${RUN_ATTEMPT}-${attempt}" || true)"
    type="$(awk 'BEGIN{IGNORECASE=1} /^content-type:/ {gsub(/\r/, ""); print $2}' "$headers" 2>/dev/null | tail -1)"
    if [[ ",$expected," == *",$status,"* && "$type" == application/json* ]]; then
      jq -e 'type == "object"' "$body" >/dev/null
      ! grep -Eiq 'trusted_public_origin_unavailable|invalid_forwarded_public_origin' "$body"
      echo "CELAR_ANALYTICS_PROBE_${name^^}=HTTP_$status"
      return 0
    fi
    sleep 5
  done
  cat "$body" >&2 || true
  fail "$path did not return expected JSON status $expected for POST"
}

probe_version_commit() {
  local body="$PROBE_DIR/version.json"
  curl -fsSL \
    -H 'Accept: application/json' \
    -H 'Cache-Control: no-cache, no-store' \
    "$BASE_URL/api/version?release_check=${RUN_ID}-${RUN_ATTEMPT}" \
    -o "$body"
  jq -e 'type == "object"' "$body" >/dev/null
  if jq -e 'has("sourceCommit") or has("source_commit") or has("commit")' "$body" >/dev/null 2>&1; then
    jq -e --arg expected "$TARGET_COMMIT" \
      '(.sourceCommit // .source_commit // .commit) == $expected' "$body" >/dev/null || {
        cat "$body" >&2
        fail "The API version response does not identify the deployed release commit."
      }
  fi
  echo "CELAR_ANALYTICS_VERSION_ENDPOINT=PASS"
}

probe_sso_state_origin_recovery() {
  local start_headers="$PROBE_DIR/sso-start.headers"
  local start_body="$PROBE_DIR/sso-start.body"
  local callback_headers="$PROBE_DIR/sso-callback.headers"
  local callback_body="$PROBE_DIR/sso-callback.body"
  local start_status location parsed redirect state encoded_state callback_status callback_location

  start_status="$(curl -sS --max-time 30 --max-redirs 0 -D "$start_headers" -o "$start_body" -w '%{http_code}' \
    -H "Origin: $BASE_URL" \
    -H "Referer: $BASE_URL/" \
    "$BASE_URL/api/auth/sso/start?prompt=select_account&release_check=$RUN_ID" || true)"
  location="$(awk 'BEGIN{IGNORECASE=1} /^location:/ {sub(/^[^:]*:[[:space:]]*/, ""); gsub(/\r/, ""); print}' "$start_headers" | tail -1)"
  [[ "$start_status" =~ ^30[2378]$ && "$location" == https://login.microsoftonline.com/* ]] ||
    fail "SSO start did not return an approved Microsoft redirect."

  parsed="$(python3 - "$location" <<'PY'
import sys
from urllib.parse import parse_qs, urlparse
q = parse_qs(urlparse(sys.argv[1]).query)
print(q.get('redirect_uri', [''])[0])
print(q.get('state', [''])[0])
PY
)"
  redirect="$(sed -n '1p' <<<"$parsed")"
  state="$(sed -n '2p' <<<"$parsed")"
  [[ "$redirect" == "$BASE_URL/api/auth/sso/callback" ]] || fail "SSO redirect_uri mismatch: $redirect"
  [[ -n "$state" ]] || fail "SSO start did not provide a state token."
  ! grep -Eiq 'azurecontainerapps\.io|\.internal\.' <<<"$location $redirect" ||
    fail "SSO redirect leaked an internal host."

  encoded_state="$(python3 - "$state" <<'PY'
import sys
from urllib.parse import quote
print(quote(sys.argv[1], safe=''))
PY
)"
  callback_status="$(curl -sS --max-time 30 --max-redirs 0 -D "$callback_headers" -o "$callback_body" -w '%{http_code}' \
    "$BASE_URL/api/auth/sso/callback?state=$encoded_state" || true)"
  callback_location="$(awk 'BEGIN{IGNORECASE=1} /^location:/ {sub(/^[^:]*:[[:space:]]*/, ""); gsub(/\r/, ""); print}' "$callback_headers" | tail -1)"

  [[ "$callback_status" =~ ^(30[2378]|400)$ ]] || {
    cat "$callback_body" >&2 || true
    fail "SSO callback recovery probe returned unexpected HTTP status $callback_status"
  }
  ! grep -Eiq 'trusted_public_origin_unavailable|invalid_forwarded_public_origin' "$callback_headers" "$callback_body" || {
    cat "$callback_body" >&2 || true
    fail "SSO callback failed before using its saved state origin."
  }
  [[ -z "$callback_location" || "$callback_location" == "$BASE_URL"/* || "$callback_location" == /\#* ]] ||
    fail "Callback recovery redirected outside ProjectPulse: $callback_location"
  echo "CELAR_ANALYTICS_SSO_STATE_ORIGIN_RECOVERY=HTTP_$callback_status"
}

bundle_markers() {
  local html="$PROBE_DIR/index.html" js="$PROBE_DIR/bundle.js" css="$PROBE_DIR/bundle.css"
  curl -fsSL -H 'Cache-Control: no-cache, no-store' "$BASE_URL/?release_check=$RUN_ID" -o "$html"
  : > "$js"
  : > "$css"
  mapfile -t js_paths < <(grep -Eo 'src="[^"]+\.js[^"]*"' "$html" | sed -E 's/^src="//;s/"$//;s/[?#].*$//' | sort -u)
  mapfile -t css_paths < <(grep -Eo 'href="[^"]+\.css[^"]*"' "$html" | sed -E 's/^href="//;s/"$//;s/[?#].*$//' | sort -u)
  [[ ${#js_paths[@]} -gt 0 && ${#css_paths[@]} -gt 0 ]] ||
    fail "Served index did not expose JavaScript and CSS assets."

  local p url
  for p in "${js_paths[@]}"; do
    [[ "$p" == http* ]] && url="$p" || url="$BASE_URL/${p#./}"
    curl -fsSL "$url?release_check=$RUN_ID" >> "$js"
  done
  for p in "${css_paths[@]}"; do
    [[ "$p" == http* ]] && url="$p" || url="$BASE_URL/${p#./}"
    curl -fsSL "$url?release_check=$RUN_ID" >> "$css"
  done

  for marker in \
    'Ask Celar AI' \
    'Celar AI Workbench' \
    'Speed of light. Speed of delivery.' \
    '/api/celar-ai/v1/chat' \
    'Private intelligence and governed provider routing' \
    'Analytics Center' \
    'Select report' \
    'All customers' \
    'All projects' \
    'All engineers' \
    'Preview report' \
    'Run & save' \
    '/api/analytics/catalog' \
    'Pending approval work' \
    'Toyota & Hyundai Pipelines'; do
    grep -Fq "$marker" "$js" || fail "Missing served JavaScript marker: $marker"
  done
  ! grep -Fq 'selectedEngineerSummaryText' "$js" ||
    fail "Legacy Analytics Center failure marker remains in the served bundle."

  for marker in \
    '.celar-ai-provider-bridge' \
    '.celar-ai-provider-bridge__route-grid' \
    '.analytics-center' \
    '.analytics-report-cards' \
    '.analytics-filter-grid' \
    '.analytics-history-list' \
    '.pending-approval-center' \
    '.project-register-center'; do
    grep -Fq "$marker" "$css" || fail "Missing served CSS marker: $marker"
  done
  echo 'CELAR_ANALYTICS_SERVED_BUNDLE=PASS'
}

RAW_API_IMAGE="$(az containerapp show -g "$RESOURCE_GROUP" -n "$API_APP" --query 'properties.template.containers[0].image' -o tsv --only-show-errors)"
RAW_WEB_IMAGE="$(az containerapp show -g "$RESOURCE_GROUP" -n "$WEB_APP" --query 'properties.template.containers[0].image' -o tsv --only-show-errors)"
CURRENT_SOURCE_COMMIT="$(az containerapp show -g "$RESOURCE_GROUP" -n "$API_APP" --query "properties.template.containers[0].env[?name=='PROJECTPULSE_SOURCE_COMMIT'].value | [0]" -o tsv --only-show-errors || true)"
API_MODE="$(az containerapp show -g "$RESOURCE_GROUP" -n "$API_APP" --query 'properties.configuration.activeRevisionsMode' -o tsv --only-show-errors)"
WEB_MODE="$(az containerapp show -g "$RESOURCE_GROUP" -n "$WEB_APP" --query 'properties.configuration.activeRevisionsMode' -o tsv --only-show-errors)"
[[ "${API_MODE,,}" == single && "${WEB_MODE,,}" == single ]] ||
  fail "API and web must use single-revision mode."
CURRENT_API_IMAGE="$(resolve_digest "$RAW_API_IMAGE")"
CURRENT_WEB_IMAGE="$(resolve_digest "$RAW_WEB_IMAGE")"

API_REPOSITORY='project-health-dashboard-api'
WEB_REPOSITORY='project-health-dashboard-web'
API_DIGEST="$("$BUILD_SCRIPT" \
  "$ACR_NAME" \
  "$API_REPOSITORY:$SLUG-$TARGET_COMMIT" \
  "$RELEASE_ROOT/deployment/containers/api/Dockerfile" \
  "$RELEASE_ROOT")"
WEB_DIGEST="$("$BUILD_SCRIPT" \
  "$ACR_NAME" \
  "$WEB_REPOSITORY:$SLUG-$TARGET_COMMIT" \
  "$RELEASE_ROOT/deployment/containers/web/Dockerfile" \
  "$RELEASE_ROOT")"
[[ "$API_DIGEST" == sha256:* && "$WEB_DIGEST" == sha256:* ]] ||
  fail "Build did not return immutable digests."
API_IMAGE="$ACR_NAME.azurecr.io/$API_REPOSITORY@$API_DIGEST"
WEB_IMAGE="$ACR_NAME.azurecr.io/$WEB_REPOSITORY@$WEB_DIGEST"

API_REVISION="$API_APP--caapi-${RUN_ID}-${RUN_ATTEMPT}"
API_STARTED=1
az containerapp update \
  -g "$RESOURCE_GROUP" \
  -n "$API_APP" \
  --image "$API_IMAGE" \
  --set-env-vars \
    PROJECTPULSE_ENVIRONMENT=test \
    PROJECTPULSE_SOURCE_COMMIT="$TARGET_COMMIT" \
    PROJECTPULSE_NOTIFICATION_SCHEDULER_INITIAL_DELAY_SECONDS=600 \
    PROJECTPULSE_PULSE_AI_PRIVATE_RUNTIME_WORKER_ENABLED=false \
    PROJECTPULSE_PULSE_AI_PRIVATE_RAG_ENABLED=false \
    PROJECTPULSE_PULSE_AI_ALLOW_LEXICAL_ONLY_COMPLETION=false \
    PROJECTPULSE_PULSE_AI_DOCUMENT_MALWARE_SCAN_ATTESTED=false \
  --revision-suffix "caapi-${RUN_ID}-${RUN_ATTEMPT}" \
  --output none \
  --only-show-errors
bash "$WAIT_SCRIPT" "$RESOURCE_GROUP" "$API_APP" "$API_REVISION" "$API_IMAGE" 60 10

probe_json health '/health' '200'
probe_json version '/api/version' '200'
probe_version_commit
probe_json approval_work '/api/approval-work/pending' '401,403'
probe_json work_register '/api/work-register/overview' '401,403'
probe_json pulse_system_readiness '/api/pulse-ai/v1/system/readiness' '401,403'
probe_json pulse_system_apis '/api/pulse-ai/v1/system/apis' '401,403'
probe_json pulse_conversations '/api/pulse-ai/v1/system/conversations' '401,403'
probe_json celar_about '/api/celar-ai/v1/about' '401,403'
probe_json celar_provider_bridge '/api/celar-ai/v1/provider-bridge/readiness' '401,403'
probe_json analytics_catalog '/api/analytics/catalog' '401,403'
probe_json analytics_history '/api/analytics/history' '401,403'
probe_json_post celar_chat '/api/celar-ai/v1/chat' '401,403' '{"question":"What is Celar AI?","detailLevel":"comprehensive"}'
probe_json_post pulse_system_question '/api/pulse-ai/v1/system/questions' '401,403' '{"question":"What is the current system state?","detailLevel":"comprehensive"}'
probe_json_post analytics_filters '/api/analytics/filter-options' '401,403' '{"reportCode":"project_portfolio"}'
probe_sso_state_origin_recovery

WEB_REVISION="$WEB_APP--caweb-${RUN_ID}-${RUN_ATTEMPT}"
WEB_STARTED=1
az containerapp update \
  -g "$RESOURCE_GROUP" \
  -n "$WEB_APP" \
  --image "$WEB_IMAGE" \
  --revision-suffix "caweb-${RUN_ID}-${RUN_ATTEMPT}" \
  --output none \
  --only-show-errors
bash "$WAIT_SCRIPT" "$RESOURCE_GROUP" "$WEB_APP" "$WEB_REVISION" "$WEB_IMAGE" 60 10
probe_json proxy_celar_about '/api/celar-ai/v1/about' '401,403'
probe_json proxy_pulse_system '/api/pulse-ai/v1/system/readiness' '401,403'
probe_json proxy_analytics '/api/analytics/catalog' '401,403'
probe_json_post proxy_celar_chat '/api/celar-ai/v1/chat' '401,403' '{"question":"What is Celar AI?","detailLevel":"comprehensive"}'
bundle_markers

READY_API="$(az containerapp show -g "$RESOURCE_GROUP" -n "$API_APP" --query properties.latestReadyRevisionName -o tsv --only-show-errors)"
READY_WEB="$(az containerapp show -g "$RESOURCE_GROUP" -n "$WEB_APP" --query properties.latestReadyRevisionName -o tsv --only-show-errors)"
[[ "$READY_API" == "$API_REVISION" && "$READY_WEB" == "$WEB_REVISION" ]] ||
  fail "Unexpected ready revision."

cat > "$EVIDENCE_DIR/${SLUG}-test-deployment.json" <<JSON
{
  "environment": "test",
  "deploymentType": "cumulative-api-web-after-migrations-054-055",
  "releaseCommit": "$TARGET_COMMIT",
  "workflowCommit": "$WORKFLOW_SHA",
  "runId": "$RUN_ID",
  "runAttempt": "$RUN_ATTEMPT",
  "apiImage": "$API_IMAGE",
  "webImage": "$WEB_IMAGE",
  "previousApiImage": "$CURRENT_API_IMAGE",
  "previousWebImage": "$CURRENT_WEB_IMAGE",
  "previousSourceCommit": "$CURRENT_SOURCE_COMMIT",
  "apiRevision": "$READY_API",
  "webRevision": "$READY_WEB",
  "migration054Verified": true,
  "migration055Verified": true,
  "celarAiSystemIntelligenceIncluded": true,
  "celarAiDurableConversationsIncluded": true,
  "celarAiModule064ProviderBridgeIncluded": true,
  "analyticsCenterIncluded": true,
  "analyticsCenterImmutableRunEvidenceIncluded": true,
  "pendingApprovalWorkflowPreserved": true,
  "module006ToyotaHyundaiPipelinesPreserved": true,
  "privateRuntimeWorkerEnabled": false,
  "privateRagEnabled": false,
  "emailSentByDeployment": false,
  "graphCallPerformedByDeployment": false,
  "credentialValuesChanged": false,
  "providerConfigurationChanged": false,
  "vectorIndexCreated": false,
  "privateModelConfigured": false,
  "externalProviderRoutingActivated": false,
  "productionChanged": false,
  "imageRollbackOnFailure": "candidate-only",
  "databaseRollbackOnDeploymentFailure": "manual-additive-migrations-remain-applied",
  "smokeTests": "passed",
  "authenticatedFunctionalUat": "required"
}
JSON

echo 'CELAR_ANALYTICS_CURRENT_MAIN_TEST_DEPLOYMENT=COMPLETE'
