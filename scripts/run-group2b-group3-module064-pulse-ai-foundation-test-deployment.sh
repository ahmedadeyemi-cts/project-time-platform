#!/usr/bin/env bash
set -Eeuo pipefail

EXPECTED_RELEASE_COMMIT="7db5a59fd5bd0850b6ea496a6d4e0d8ca0e02a0d"
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
SLUG="group2b-group3-module064-pulse-ai-foundation"
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
[[ "$(git -C "$RELEASE_ROOT" rev-parse HEAD)" == "$EXPECTED_RELEASE_COMMIT" ]] || fail "Release checkout does not match the pinned source release."

BUILD_SCRIPT="$CONTROL_ROOT/scripts/build-pr55-acr-image.sh"
WAIT_SCRIPT="$CONTROL_ROOT/scripts/wait-containerapp-ready-revision.sh"
[[ -f "$BUILD_SCRIPT" && -f "$WAIT_SCRIPT" ]] || fail "Required shared deployment controls are missing."
mkdir -p "$PROBE_DIR" "$EVIDENCE_DIR"

API_STARTED=0
WEB_STARTED=0
CURRENT_API_IMAGE=""
CURRENT_WEB_IMAGE=""
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
    az containerapp update -g "$RESOURCE_GROUP" -n "$app" --image "$previous" --revision-suffix "$suffix" --output none --only-show-errors
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
    rollback_app WEB "$WEB_APP" "$WEB_IMAGE" "$CURRENT_WEB_IMAGE" "$WEB_STARTED" fweb-rb
    rollback_app API "$API_APP" "$API_IMAGE" "$CURRENT_API_IMAGE" "$API_STARTED" fapi-rb
    set -e
  fi
  exit "$status"
}
trap cleanup EXIT INT TERM

probe_json() {
  local name="$1" path="$2" expected="$3" status type
  local headers="$PROBE_DIR/$name.headers" body="$PROBE_DIR/$name.json"
  for attempt in $(seq 1 18); do
    status="$(curl -sS -L --max-time 30 -D "$headers" -o "$body" -w '%{http_code}' \
      -H 'Accept: application/json' -H 'Cache-Control: no-cache, no-store' \
      -H "Origin: $BASE_URL" -H "Referer: $BASE_URL/#dashboard" \
      "$BASE_URL$path?release_check=${SLUG}-${RUN_ID}-${RUN_ATTEMPT}-${attempt}" || true)"
    type="$(awk 'BEGIN{IGNORECASE=1} /^content-type:/ {gsub(/\r/, ""); print $2}' "$headers" 2>/dev/null | tail -1)"
    if [[ ",$expected," == *",$status,"* && "$type" == application/json* ]]; then
      jq -e 'type == "object"' "$body" >/dev/null
      ! grep -Eiq 'trusted_public_origin_unavailable|invalid_forwarded_public_origin' "$body"
      echo "FOUNDATION_PROBE_${name^^}=HTTP_$status"
      return 0
    fi
    sleep 5
  done
  cat "$body" >&2 || true
  fail "$path did not return expected JSON status $expected"
}

probe_sso_redirect() {
  local headers="$PROBE_DIR/sso.headers" status location redirect
  status="$(curl -sS --max-time 30 --max-redirs 0 -D "$headers" -o "$PROBE_DIR/sso.body" -w '%{http_code}' \
    -H "Origin: $BASE_URL" -H "Referer: $BASE_URL/" \
    "$BASE_URL/api/auth/sso/start?prompt=select_account&release_check=$RUN_ID" || true)"
  location="$(awk 'BEGIN{IGNORECASE=1} /^location:/ {sub(/^[^:]*:[[:space:]]*/, ""); gsub(/\r/, ""); print}' "$headers" | tail -1)"
  [[ "$status" =~ ^30[2378]$ && "$location" == https://login.microsoftonline.com/* ]] || fail "SSO start did not return an approved Microsoft redirect."
  redirect="$(python3 - "$location" <<'PY'
import sys
from urllib.parse import parse_qs, urlparse
print(parse_qs(urlparse(sys.argv[1]).query).get('redirect_uri', [''])[0])
PY
)"
  [[ "$redirect" == "$BASE_URL/api/auth/sso/callback" ]] || fail "SSO redirect_uri mismatch: $redirect"
  ! grep -Eiq 'azurecontainerapps\.io|\.internal\.' <<<"$location $redirect" || fail "SSO redirect leaked an internal host."
  echo 'FOUNDATION_SSO_REDIRECT=PASS'
}

bundle_markers() {
  local html="$PROBE_DIR/index.html" js="$PROBE_DIR/bundle.js" css="$PROBE_DIR/bundle.css"
  curl -fsSL -H 'Cache-Control: no-cache, no-store' "$BASE_URL/?release_check=$RUN_ID" -o "$html"
  : > "$js"; : > "$css"
  mapfile -t js_paths < <(grep -Eo 'src="[^"]+\.js[^"]*"' "$html" | sed -E 's/^src="//;s/"$//;s/[?#].*$//' | sort -u)
  mapfile -t css_paths < <(grep -Eo 'href="[^"]+\.css[^"]*"' "$html" | sed -E 's/^href="//;s/"$//;s/[?#].*$//' | sort -u)
  [[ ${#js_paths[@]} -gt 0 && ${#css_paths[@]} -gt 0 ]] || fail "Served index did not expose JavaScript and CSS assets."
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
    'Provider-neutral by design' '/api/system/backup-dr/production-planning' \
    'Project portfolio and financial truth' '/api/project-financials/portfolio' \
    'AI Provider Configuration' 'Checking automatically' '/api/ai-configuration' \
    'Pulse AI' 'Knowledge & RAG' 'Governance' 'work-task-builder'; do
    grep -Fq "$marker" "$js" || fail "Missing served JavaScript marker: $marker"
  done
  for marker in '.group2b-resilience-shell' '.group3-financial-workspace' '.ai-provider-center' '.pulse-ai-hero' '.pulse-ai-mission-control'; do
    grep -Fq "$marker" "$css" || fail "Missing served CSS marker: $marker"
  done
  echo 'FOUNDATION_SERVED_BUNDLE=PASS'
}

RAW_API_IMAGE="$(az containerapp show -g "$RESOURCE_GROUP" -n "$API_APP" --query 'properties.template.containers[0].image' -o tsv --only-show-errors)"
RAW_WEB_IMAGE="$(az containerapp show -g "$RESOURCE_GROUP" -n "$WEB_APP" --query 'properties.template.containers[0].image' -o tsv --only-show-errors)"
API_MODE="$(az containerapp show -g "$RESOURCE_GROUP" -n "$API_APP" --query 'properties.configuration.activeRevisionsMode' -o tsv --only-show-errors)"
WEB_MODE="$(az containerapp show -g "$RESOURCE_GROUP" -n "$WEB_APP" --query 'properties.configuration.activeRevisionsMode' -o tsv --only-show-errors)"
[[ "${API_MODE,,}" == single && "${WEB_MODE,,}" == single ]] || fail "API and web must use single-revision mode."
CURRENT_API_IMAGE="$(resolve_digest "$RAW_API_IMAGE")"
CURRENT_WEB_IMAGE="$(resolve_digest "$RAW_WEB_IMAGE")"

API_REPOSITORY='project-health-dashboard-api'
WEB_REPOSITORY='project-health-dashboard-web'
API_DIGEST="$("$BUILD_SCRIPT" "$ACR_NAME" "$API_REPOSITORY:$SLUG-$TARGET_COMMIT" "$RELEASE_ROOT/deployment/containers/api/Dockerfile" "$RELEASE_ROOT")"
WEB_DIGEST="$("$BUILD_SCRIPT" "$ACR_NAME" "$WEB_REPOSITORY:$SLUG-$TARGET_COMMIT" "$RELEASE_ROOT/deployment/containers/web/Dockerfile" "$RELEASE_ROOT")"
[[ "$API_DIGEST" == sha256:* && "$WEB_DIGEST" == sha256:* ]] || fail "Build did not return immutable digests."
API_IMAGE="$ACR_NAME.azurecr.io/$API_REPOSITORY@$API_DIGEST"
WEB_IMAGE="$ACR_NAME.azurecr.io/$WEB_REPOSITORY@$WEB_DIGEST"

API_REVISION="$API_APP--fapi-${RUN_ID}-${RUN_ATTEMPT}"
API_STARTED=1
az containerapp update -g "$RESOURCE_GROUP" -n "$API_APP" --image "$API_IMAGE" --revision-suffix "fapi-${RUN_ID}-${RUN_ATTEMPT}" --output none --only-show-errors
bash "$WAIT_SCRIPT" "$RESOURCE_GROUP" "$API_APP" "$API_REVISION" "$API_IMAGE" 60 10
probe_json health '/health' '200'
probe_json version '/api/version' '200'
probe_json group2b '/api/system/backup-dr/production-planning' '401,403'
probe_json group3 '/api/project-financials/portfolio' '401,403'
probe_json module064 '/api/ai-configuration' '401,403'
probe_sso_redirect

WEB_REVISION="$WEB_APP--fweb-${RUN_ID}-${RUN_ATTEMPT}"
WEB_STARTED=1
az containerapp update -g "$RESOURCE_GROUP" -n "$WEB_APP" --image "$WEB_IMAGE" --revision-suffix "fweb-${RUN_ID}-${RUN_ATTEMPT}" --output none --only-show-errors
bash "$WAIT_SCRIPT" "$RESOURCE_GROUP" "$WEB_APP" "$WEB_REVISION" "$WEB_IMAGE" 60 10
probe_json proxy_group2b '/api/system/backup-dr/production-planning' '401,403'
probe_json proxy_group3 '/api/project-financials/portfolio' '401,403'
probe_json proxy_module064 '/api/ai-configuration' '401,403'
bundle_markers

READY_API="$(az containerapp show -g "$RESOURCE_GROUP" -n "$API_APP" --query properties.latestReadyRevisionName -o tsv --only-show-errors)"
READY_WEB="$(az containerapp show -g "$RESOURCE_GROUP" -n "$WEB_APP" --query properties.latestReadyRevisionName -o tsv --only-show-errors)"
[[ "$READY_API" == "$API_REVISION" && "$READY_WEB" == "$WEB_REVISION" ]] || fail "Unexpected ready revision."

cat > "$EVIDENCE_DIR/${SLUG}-test-deployment.json" <<JSON
{
  "environment": "test",
  "deploymentType": "api-web-no-migration",
  "releaseCommit": "$TARGET_COMMIT",
  "workflowCommit": "$WORKFLOW_SHA",
  "runId": "$RUN_ID",
  "runAttempt": "$RUN_ATTEMPT",
  "apiImage": "$API_IMAGE",
  "webImage": "$WEB_IMAGE",
  "previousApiImage": "$CURRENT_API_IMAGE",
  "previousWebImage": "$CURRENT_WEB_IMAGE",
  "apiRevision": "$READY_API",
  "webRevision": "$READY_WEB",
  "databaseMigration": false,
  "group2bProviderNeutralResilience": true,
  "group3ProjectFinancialTruth": true,
  "module064AutomaticProviderHealth": true,
  "pulseAiFoundation": true,
  "pulseAiDeepIntelligenceIncluded": false,
  "vectorIndexCreated": false,
  "privateModelConfigured": false,
  "externalProviderRoutingActivated": false,
  "externalProviderCallsPerformedByDeployment": false,
  "credentialValuesChanged": false,
  "interactiveSsoCompletedByDeployment": false,
  "graphCallPerformedByDeployment": false,
  "emailSentByDeployment": false,
  "imageRollbackOnFailure": "candidate-only",
  "smokeTests": "passed",
  "authenticatedFunctionalUat": "required"
}
JSON

echo 'GROUP2B_GROUP3_MODULE064_PULSE_AI_FOUNDATION_TEST_DEPLOYMENT=COMPLETE'
