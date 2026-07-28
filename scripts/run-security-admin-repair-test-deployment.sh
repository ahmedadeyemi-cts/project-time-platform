#!/usr/bin/env bash
set -Eeuo pipefail

EXPECTED_RELEASE_COMMIT="845fb2d90affd808d2ec06175afcffa04863abe0"
CONTROL_ROOT="${1:-}"
RELEASE_ROOT="${2:-}"
TARGET_COMMIT="${3:-}"

RESOURCE_GROUP="${AZURE_RESOURCE_GROUP:-}"
API_APP="${AZURE_API_APP:-}"
WEB_APP="${AZURE_WEB_APP:-}"
ACR_NAME="${AZURE_ACR_NAME:-}"
PUBLIC_URL_VALUE="${PUBLIC_URL:-}"
RUN_ID="${GITHUB_RUN_ID:-local}"
RUN_ATTEMPT="${GITHUB_RUN_ATTEMPT:-1}"
WORKFLOW_SHA="${GITHUB_SHA:-unknown}"

fail() { echo "ERROR: $*" >&2; exit 1; }
require_value() { [[ -n "$2" ]] || fail "$1 is not configured."; }

require_value CONTROL_ROOT "$CONTROL_ROOT"
require_value RELEASE_ROOT "$RELEASE_ROOT"
require_value TARGET_COMMIT "$TARGET_COMMIT"
require_value AZURE_RESOURCE_GROUP "$RESOURCE_GROUP"
require_value AZURE_API_APP "$API_APP"
require_value AZURE_WEB_APP "$WEB_APP"
require_value AZURE_ACR_NAME "$ACR_NAME"
require_value PUBLIC_URL "$PUBLIC_URL_VALUE"
[[ "$TARGET_COMMIT" == "$EXPECTED_RELEASE_COMMIT" ]] || fail "Unexpected source release: $TARGET_COMMIT"
[[ "$(git -C "$RELEASE_ROOT" rev-parse HEAD)" == "$EXPECTED_RELEASE_COMMIT" ]] || fail "Release checkout does not match the pinned source release."

BUILD_SCRIPT="$CONTROL_ROOT/scripts/build-pr55-acr-image.sh"
WAIT_SCRIPT="$CONTROL_ROOT/scripts/wait-containerapp-ready-revision.sh"
for required in "$BUILD_SCRIPT" "$WAIT_SCRIPT"; do
  [[ -f "$required" ]] || fail "Missing deployment control: $required"
done

BASE_URL="${PUBLIC_URL_VALUE%/}"
API_STARTED=0
WEB_STARTED=0
CURRENT_API_IMAGE=""
CURRENT_WEB_IMAGE=""
API_IMAGE=""
WEB_IMAGE=""
API_REVISION=""
WEB_REVISION=""
PROBE_DIR="${RUNNER_TEMP:-/tmp}/security-admin-repair-probes"
mkdir -p "$PROBE_DIR"

resolve_digest() {
  local image="$1" registry="$ACR_NAME.azurecr.io/" relative repository digest
  case "$image" in
    "$registry"*@sha256:*) printf '%s\n' "$image"; return ;;
    "$registry"*:*) ;;
    *) fail "Image is outside the approved ACR: $image" ;;
  esac
  relative="${image#"$registry"}"
  repository="${relative%:*}"
  digest="$(az acr repository show --name "$ACR_NAME" --image "$relative" --query digest -o tsv --only-show-errors)"
  [[ "$digest" == sha256:* ]] || fail "Could not resolve an immutable digest for $image"
  printf '%s%s@%s\n' "$registry" "$repository" "$digest"
}

restore_web() {
  [[ "$WEB_STARTED" == 1 && -n "$CURRENT_WEB_IMAGE" && -n "$WEB_IMAGE" ]] || return 0
  local active suffix revision
  active="$(az containerapp show -g "$RESOURCE_GROUP" -n "$WEB_APP" --query 'properties.template.containers[0].image' -o tsv 2>/dev/null)"
  if [[ "$active" == "$WEB_IMAGE" ]]; then
    suffix="sarwebrb-${RUN_ID}-${RUN_ATTEMPT}"
    revision="$WEB_APP--$suffix"
    az containerapp update -g "$RESOURCE_GROUP" -n "$WEB_APP" --image "$CURRENT_WEB_IMAGE" --revision-suffix "$suffix" --output none --only-show-errors
    bash "$WAIT_SCRIPT" "$RESOURCE_GROUP" "$WEB_APP" "$revision" "$CURRENT_WEB_IMAGE" 60 10
    echo "SECURITY_ADMIN_REPAIR_WEB_ROLLBACK=COMPLETE"
  else
    echo "Web rollback skipped because another image is active: $active" >&2
  fi
}

restore_api() {
  [[ "$API_STARTED" == 1 && -n "$CURRENT_API_IMAGE" && -n "$API_IMAGE" ]] || return 0
  local active suffix revision
  active="$(az containerapp show -g "$RESOURCE_GROUP" -n "$API_APP" --query 'properties.template.containers[0].image' -o tsv 2>/dev/null)"
  if [[ "$active" == "$API_IMAGE" ]]; then
    suffix="sarapirb-${RUN_ID}-${RUN_ATTEMPT}"
    revision="$API_APP--$suffix"
    az containerapp update -g "$RESOURCE_GROUP" -n "$API_APP" --image "$CURRENT_API_IMAGE" --revision-suffix "$suffix" --output none --only-show-errors
    bash "$WAIT_SCRIPT" "$RESOURCE_GROUP" "$API_APP" "$revision" "$CURRENT_API_IMAGE" 60 10
    echo "SECURITY_ADMIN_REPAIR_API_ROLLBACK=COMPLETE"
  else
    echo "API rollback skipped because another image is active: $active" >&2
  fi
}

cleanup() {
  local status=$?
  trap - EXIT INT TERM
  if (( status != 0 )); then
    set +e
    restore_web
    restore_api
    set -e
  fi
  exit "$status"
}
trap cleanup EXIT INT TERM

probe_api() {
  local name="$1" method="$2" path="$3" expected="$4" body="${5:-}"
  local status='' type='' url="$BASE_URL$path" headers_file="$PROBE_DIR/$name.headers" body_file="$PROBE_DIR/$name.body"
  for attempt in $(seq 1 18); do
    local separator='?'
    [[ "$url" == *\?* ]] && separator='&'
    local candidate="$url${separator}release_check=sar-${RUN_ID}-${RUN_ATTEMPT}-${attempt}"
    local args=(
      -sS -L --max-time 30
      -X "$method"
      -D "$headers_file"
      -o "$body_file"
      -w '%{http_code}'
      -H 'Accept: application/json'
      -H 'Cache-Control: no-cache, no-store'
      -H 'Pragma: no-cache'
      -H "Origin: $BASE_URL"
      -H "Referer: $BASE_URL/#dashboard"
    )
    if [[ -n "$body" ]]; then
      args+=(-H 'Content-Type: application/json' --data "$body")
    fi
    status="$(curl "${args[@]}" "$candidate" || true)"
    type="$(awk 'BEGIN{IGNORECASE=1} /^content-type:/ {gsub(/\r/, ""); print $2}' "$headers_file" 2>/dev/null | tail -1)"
    if [[ ",$expected," == *",$status,"* && "$type" == application/json* ]]; then
      jq -e 'type == "object"' "$body_file" >/dev/null
      if grep -Eiq 'invalid_forwarded_public_origin|trusted_public_origin_unavailable|outside the approved environment domains' "$body_file"; then
        echo "$path returned a public-origin rejection instead of the expected protected response." >&2
        cat "$body_file" >&2
        return 1
      fi
      echo "SECURITY_ADMIN_REPAIR_PROBE_${name^^}=HTTP_$status"
      return 0
    fi
    sleep 5
  done
  echo "$path did not reach expected status $expected with JSON." >&2
  cat "$body_file" >&2 || true
  return 1
}

RAW_API_IMAGE="$(az containerapp show -g "$RESOURCE_GROUP" -n "$API_APP" --query 'properties.template.containers[0].image' -o tsv --only-show-errors)"
RAW_WEB_IMAGE="$(az containerapp show -g "$RESOURCE_GROUP" -n "$WEB_APP" --query 'properties.template.containers[0].image' -o tsv --only-show-errors)"
API_MODE="$(az containerapp show -g "$RESOURCE_GROUP" -n "$API_APP" --query 'properties.configuration.activeRevisionsMode' -o tsv --only-show-errors)"
WEB_MODE="$(az containerapp show -g "$RESOURCE_GROUP" -n "$WEB_APP" --query 'properties.configuration.activeRevisionsMode' -o tsv --only-show-errors)"
[[ -n "$RAW_API_IMAGE" && -n "$RAW_WEB_IMAGE" ]] || fail "Current API/web images are unavailable."
[[ "${API_MODE,,}" == single && "${WEB_MODE,,}" == single ]] || fail "API and web apps must use single-revision mode."
CURRENT_API_IMAGE="$(resolve_digest "$RAW_API_IMAGE")"
CURRENT_WEB_IMAGE="$(resolve_digest "$RAW_WEB_IMAGE")"
[[ -n "$CURRENT_API_IMAGE" && -n "$CURRENT_WEB_IMAGE" ]] || fail "Rollback images could not be resolved."
echo "CAPTURED_CURRENT_API_IMAGE=$CURRENT_API_IMAGE"
echo "CAPTURED_CURRENT_WEB_IMAGE=$CURRENT_WEB_IMAGE"

API_REPOSITORY='project-health-dashboard-api'
WEB_REPOSITORY='project-health-dashboard-web'
API_DIGEST="$("$BUILD_SCRIPT" "$ACR_NAME" "$API_REPOSITORY:security-admin-repair-$TARGET_COMMIT" "$RELEASE_ROOT/deployment/containers/api/Dockerfile" "$RELEASE_ROOT")"
WEB_DIGEST="$("$BUILD_SCRIPT" "$ACR_NAME" "$WEB_REPOSITORY:security-admin-repair-$TARGET_COMMIT" "$RELEASE_ROOT/deployment/containers/web/Dockerfile" "$RELEASE_ROOT")"
[[ "$API_DIGEST" == sha256:* && "$WEB_DIGEST" == sha256:* ]] || fail "Candidate image build did not return immutable digests."
API_IMAGE="$ACR_NAME.azurecr.io/$API_REPOSITORY@$API_DIGEST"
WEB_IMAGE="$ACR_NAME.azurecr.io/$WEB_REPOSITORY@$WEB_DIGEST"
echo "SECURITY_ADMIN_REPAIR_API_IMAGE=$API_IMAGE"
echo "SECURITY_ADMIN_REPAIR_WEB_IMAGE=$WEB_IMAGE"

API_SUFFIX="sarapi-${RUN_ID}-${RUN_ATTEMPT}"
API_REVISION="$API_APP--$API_SUFFIX"
API_STARTED=1
az containerapp update -g "$RESOURCE_GROUP" -n "$API_APP" --image "$API_IMAGE" --revision-suffix "$API_SUFFIX" --output none --only-show-errors
bash "$WAIT_SCRIPT" "$RESOURCE_GROUP" "$API_APP" "$API_REVISION" "$API_IMAGE" 60 10

probe_api health GET '/health' '200'
probe_api version GET '/api/version' '200'
probe_api role_summary GET '/api/runtime/v2/role-policy/summary' '401,403'
probe_api role_catalog GET '/api/runtime/v2/role-policy/catalog' '401,403'
probe_api role_matrix GET '/api/runtime/v2/role-policy/matrix' '401,403'
probe_api platform_overview GET '/api/platform-operations/overview' '401,403'

READY_API="$(az containerapp show -g "$RESOURCE_GROUP" -n "$API_APP" --query 'properties.latestReadyRevisionName' -o tsv --only-show-errors)"
[[ "$READY_API" == "$API_REVISION" ]] || fail "Unexpected ready API revision: $READY_API"
ACTIVE_API_IMAGE="$(az containerapp revision show -g "$RESOURCE_GROUP" -n "$API_APP" --revision "$READY_API" --query 'properties.template.containers[0].image' -o tsv --only-show-errors)"
[[ "$ACTIVE_API_IMAGE" == "$API_IMAGE" ]] || fail "Unexpected active API image."
echo "SECURITY_ADMIN_REPAIR_API_VALIDATION=PASS"

WEB_SUFFIX="sarweb-${RUN_ID}-${RUN_ATTEMPT}"
WEB_REVISION="$WEB_APP--$WEB_SUFFIX"
WEB_STARTED=1
az containerapp update -g "$RESOURCE_GROUP" -n "$WEB_APP" --image "$WEB_IMAGE" --revision-suffix "$WEB_SUFFIX" --output none --only-show-errors
bash "$WAIT_SCRIPT" "$RESOURCE_GROUP" "$WEB_APP" "$WEB_REVISION" "$WEB_IMAGE" 60 10

# These protected calls traverse the newly deployed web proxy. A 401/403 JSON
# response proves the public HTTPS origin reached endpoint authorization instead
# of being rejected as an invalid forwarded origin.
probe_api module065_sso POST '/api/microsoft-integration/sso-apply-profile' '401,403' '{}'
probe_api module010_preview POST '/api/admin/azure/users/preview' '401,403' '{}'
probe_api module026_providers GET '/api/integrations/026/providers' '401,403'

SERVED_READY=false
for attempt in $(seq 1 24); do
  BUSTER="sar-${RUN_ID}-${RUN_ATTEMPT}-${attempt}"
  HTML_STATUS="$(curl -sS -L --max-time 30 -H 'Cache-Control: no-cache, no-store' -o "$PROBE_DIR/index.html" -w '%{http_code}' "$BASE_URL/?release_check=$BUSTER" || true)"
  if [[ "$HTML_STATUS" == 200 ]]; then
    JS_PATH="$(sed -nE 's/.*src="([^"]+\.js)".*/\1/p' "$PROBE_DIR/index.html" | head -1)"
    CSS_PATH="$(sed -nE 's/.*href="([^"]+\.css)".*/\1/p' "$PROBE_DIR/index.html" | head -1)"
    [[ "$JS_PATH" == http* ]] && JS_URL="$JS_PATH" || JS_URL="$BASE_URL/${JS_PATH#/}"
    [[ "$CSS_PATH" == http* ]] && CSS_URL="$CSS_PATH" || CSS_URL="$BASE_URL/${CSS_PATH#/}"
    JS_STATUS="$(curl -sS -L --max-time 30 -H 'Cache-Control: no-cache, no-store' -o "$PROBE_DIR/app.js" -w '%{http_code}' "$JS_URL?release_check=$BUSTER" || true)"
    CSS_STATUS="$(curl -sS -L --max-time 30 -H 'Cache-Control: no-cache, no-store' -o "$PROBE_DIR/app.css" -w '%{http_code}' "$CSS_URL?release_check=$BUSTER" || true)"
    if [[ "$JS_STATUS" == 200 && "$CSS_STATUS" == 200 ]] \
      && grep -Fq 'projectpulse-role-policy-authoritative-v3' "$PROBE_DIR/app.js" \
      && grep -Fq '/api/runtime/v2/role-policy/catalog' "$PROBE_DIR/app.js" \
      && grep -Fq '/api/runtime/v2/role-policy/matrix' "$PROBE_DIR/app.js" \
      && grep -Fq 'X-ProjectPulse-View-As-User' "$PROBE_DIR/app.js" \
      && grep -Fq '/api/integrations/026/providers' "$PROBE_DIR/app.js" \
      && grep -Fq 'Show volume details' "$PROBE_DIR/app.js" \
      && grep -Fq 'Hide volume details' "$PROBE_DIR/app.js" \
      && grep -Fq 'module-013-volume-details' "$PROBE_DIR/app.js"; then
      SERVED_READY=true
      break
    fi
  fi
  sleep 5
done
[[ "$SERVED_READY" == true ]] || fail "The served web bundle did not expose the role-policy, Module 026, View-As, and Module 013 repair markers."

READY_WEB="$(az containerapp show -g "$RESOURCE_GROUP" -n "$WEB_APP" --query 'properties.latestReadyRevisionName' -o tsv --only-show-errors)"
[[ "$READY_WEB" == "$WEB_REVISION" ]] || fail "Unexpected ready web revision: $READY_WEB"
ACTIVE_WEB_IMAGE="$(az containerapp revision show -g "$RESOURCE_GROUP" -n "$WEB_APP" --revision "$READY_WEB" --query 'properties.template.containers[0].image' -o tsv --only-show-errors)"
[[ "$ACTIVE_WEB_IMAGE" == "$WEB_IMAGE" ]] || fail "Unexpected active web image."
echo "SECURITY_ADMIN_REPAIR_WEB_VALIDATION=PASS"

mkdir -p "$CONTROL_ROOT/evidence"
cat > "$CONTROL_ROOT/evidence/security-admin-repair-test-deployment.json" <<JSON
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
  "databaseMutation": false,
  "rolePolicyTransport": "authoritative-runtime-v2",
  "trustedPublicOrigin": "https-approved-hosts-fail-closed",
  "module010PreviewRoute": "protected-and-reachable",
  "module026CredentialMutation": "actual-session-and-write-only",
  "module013VolumeDetails": "collapsed-by-default",
  "externalProviderCallsPerformedByDeployment": false,
  "credentialValuesChanged": false,
  "imageRollbackOnFailure": "candidate-only",
  "smokeTests": "passed",
  "authenticatedFunctionalUat": "required"
}
JSON

echo "SECURITY_ADMIN_REPAIR_TEST_DEPLOYMENT=COMPLETE"
