#!/usr/bin/env bash
set -Eeuo pipefail

EXPECTED_RELEASE_COMMIT="816deeda98d3d875cdbdea1a42b5662815334eb9"
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
PROBE_DIR="${RUNNER_TEMP:-/tmp}/module001-ptc-timer-dom-probes"
mkdir -p "$PROBE_DIR"

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
  [[ "$digest" == sha256:* ]] || fail "Could not resolve an immutable digest for $image"
  printf '%s%s@%s\n' "$registry" "$repository" "$digest"
}

restore_web() {
  [[ "$WEB_STARTED" == 1 && -n "$CURRENT_WEB_IMAGE" && -n "$WEB_IMAGE" ]] || return 0
  local active suffix revision
  active="$(az containerapp show -g "$RESOURCE_GROUP" -n "$WEB_APP" --query 'properties.template.containers[0].image' -o tsv 2>/dev/null)"
  if [[ "$active" == "$WEB_IMAGE" ]]; then
    suffix="m1webrb-${RUN_ID}-${RUN_ATTEMPT}"
    revision="$WEB_APP--$suffix"
    az containerapp update -g "$RESOURCE_GROUP" -n "$WEB_APP" --image "$CURRENT_WEB_IMAGE" --revision-suffix "$suffix" --output none --only-show-errors
    bash "$WAIT_SCRIPT" "$RESOURCE_GROUP" "$WEB_APP" "$revision" "$CURRENT_WEB_IMAGE" 60 10
    echo "MODULE001_PTC_TIMER_DOM_WEB_ROLLBACK=COMPLETE"
  else
    echo "Web rollback skipped because another image is active: $active" >&2
  fi
}

restore_api() {
  [[ "$API_STARTED" == 1 && -n "$CURRENT_API_IMAGE" && -n "$API_IMAGE" ]] || return 0
  local active suffix revision
  active="$(az containerapp show -g "$RESOURCE_GROUP" -n "$API_APP" --query 'properties.template.containers[0].image' -o tsv 2>/dev/null)"
  if [[ "$active" == "$API_IMAGE" ]]; then
    suffix="m1apirb-${RUN_ID}-${RUN_ATTEMPT}"
    revision="$API_APP--$suffix"
    az containerapp update -g "$RESOURCE_GROUP" -n "$API_APP" --image "$CURRENT_API_IMAGE" --revision-suffix "$suffix" --output none --only-show-errors
    bash "$WAIT_SCRIPT" "$RESOURCE_GROUP" "$API_APP" "$revision" "$CURRENT_API_IMAGE" 60 10
    echo "MODULE001_PTC_TIMER_DOM_API_ROLLBACK=COMPLETE"
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
    local candidate="$url${separator}release_check=m1ptd-${RUN_ID}-${RUN_ATTEMPT}-${attempt}"
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
      -H "Referer: $BASE_URL/#timesheet"
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
      echo "MODULE001_PTC_TIMER_DOM_PROBE_${name^^}=HTTP_$status"
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
API_DIGEST="$("$BUILD_SCRIPT" "$ACR_NAME" "$API_REPOSITORY:module001-ptc-timer-dom-$TARGET_COMMIT" "$RELEASE_ROOT/deployment/containers/api/Dockerfile" "$RELEASE_ROOT")"
WEB_DIGEST="$("$BUILD_SCRIPT" "$ACR_NAME" "$WEB_REPOSITORY:module001-ptc-timer-dom-$TARGET_COMMIT" "$RELEASE_ROOT/deployment/containers/web/Dockerfile" "$RELEASE_ROOT")"
[[ "$API_DIGEST" == sha256:* && "$WEB_DIGEST" == sha256:* ]] || fail "Candidate image build did not return immutable digests."
API_IMAGE="$ACR_NAME.azurecr.io/$API_REPOSITORY@$API_DIGEST"
WEB_IMAGE="$ACR_NAME.azurecr.io/$WEB_REPOSITORY@$WEB_DIGEST"
echo "MODULE001_PTC_TIMER_DOM_API_IMAGE=$API_IMAGE"
echo "MODULE001_PTC_TIMER_DOM_WEB_IMAGE=$WEB_IMAGE"

API_SUFFIX="m1api-${RUN_ID}-${RUN_ATTEMPT}"
API_REVISION="$API_APP--$API_SUFFIX"
API_STARTED=1
az containerapp update -g "$RESOURCE_GROUP" -n "$API_APP" --image "$API_IMAGE" --revision-suffix "$API_SUFFIX" --output none --only-show-errors
bash "$WAIT_SCRIPT" "$RESOURCE_GROUP" "$API_APP" "$API_REVISION" "$API_IMAGE" 60 10

probe_api health GET '/health' '200'
probe_api version GET '/api/version' '200'
probe_api ptc_v2_users GET '/api/runtime/timesheet/steward/v2/users?weekStart=2026-07-26' '401,403'
probe_api ptc_v2_workspace GET '/api/runtime/timesheet/steward/v2/users/00000000-0000-0000-0000-000000000000/workspace?weekStart=2026-07-26' '401,403'
probe_api ptc_v2_move POST '/api/runtime/timesheet/steward/v2/entries/00000000-0000-0000-0000-000000000000/move' '401,403' '{"targetUserId":"00000000-0000-0000-0000-000000000000","destinationType":"non_project","nonProjectTimeCategoryId":"00000000-0000-0000-0000-000000000000","reason":"deployment protected boundary probe"}'
probe_api ptc_legacy_users GET '/api/timesheet/ptc/users?weekStart=2026-07-26' '401,403'
probe_api timer_targets GET '/api/timesheet/timers/targets?weekStart=2026-07-26' '401,403'
probe_api timer_active GET '/api/timesheet/timers/active' '401,403'
probe_api timer_history GET '/api/timesheet/timers/history?weekStart=2026-07-26' '401,403'
probe_api rbac_bootstrap GET '/api/rbac/v1/bootstrap' '401,403'
probe_api rbac_matrix GET '/api/rbac/v1/matrix' '401,403'
probe_api module026_providers GET '/api/integrations/026/providers' '401,403'
probe_api platform_overview GET '/api/platform-operations/overview' '401,403'
echo "MODULE001_PTC_TIMER_DOM_API_PROTECTED_BOUNDARY=PASS"

READY_API="$(az containerapp show -g "$RESOURCE_GROUP" -n "$API_APP" --query 'properties.latestReadyRevisionName' -o tsv --only-show-errors)"
[[ "$READY_API" == "$API_REVISION" ]] || fail "Unexpected ready API revision: $READY_API"
ACTIVE_API_IMAGE="$(az containerapp revision show -g "$RESOURCE_GROUP" -n "$API_APP" --revision "$READY_API" --query 'properties.template.containers[0].image' -o tsv --only-show-errors)"
[[ "$ACTIVE_API_IMAGE" == "$API_IMAGE" ]] || fail "Unexpected active API image."
echo "MODULE001_PTC_TIMER_DOM_API_VALIDATION=PASS"

WEB_SUFFIX="m1web-${RUN_ID}-${RUN_ATTEMPT}"
WEB_REVISION="$WEB_APP--$WEB_SUFFIX"
WEB_STARTED=1
az containerapp update -g "$RESOURCE_GROUP" -n "$WEB_APP" --image "$WEB_IMAGE" --revision-suffix "$WEB_SUFFIX" --output none --only-show-errors
bash "$WAIT_SCRIPT" "$RESOURCE_GROUP" "$WEB_APP" "$WEB_REVISION" "$WEB_IMAGE" 60 10

# Traverse the public web proxy after the web candidate is active.
probe_api proxy_ptc_users GET '/api/runtime/timesheet/steward/v2/users?weekStart=2026-07-26' '401,403'
probe_api proxy_timer_active GET '/api/timesheet/timers/active' '401,403'
probe_api proxy_timer_history GET '/api/timesheet/timers/history?weekStart=2026-07-26' '401,403'
probe_api proxy_module026 GET '/api/integrations/026/providers' '401,403'

SERVED_READY=false
for attempt in $(seq 1 24); do
  BUSTER="m1ptd-${RUN_ID}-${RUN_ATTEMPT}-${attempt}"
  HTML_STATUS="$(curl -sS -L --max-time 30 -H 'Cache-Control: no-cache, no-store' -o "$PROBE_DIR/index.html" -w '%{http_code}' "$BASE_URL/?release_check=$BUSTER" || true)"
  if [[ "$HTML_STATUS" == 200 ]]; then
    JS_PATH="$(sed -nE 's/.*src="([^"]+\.js)".*/\1/p' "$PROBE_DIR/index.html" | head -1)"
    CSS_PATH="$(sed -nE 's/.*href="([^"]+\.css)".*/\1/p' "$PROBE_DIR/index.html" | head -1)"
    [[ "$JS_PATH" == http* ]] && JS_URL="$JS_PATH" || JS_URL="$BASE_URL/${JS_PATH#/}"
    [[ "$CSS_PATH" == http* ]] && CSS_URL="$CSS_PATH" || CSS_URL="$BASE_URL/${CSS_PATH#/}"
    JS_STATUS="$(curl -sS -L --max-time 30 -H 'Cache-Control: no-cache, no-store' -o "$PROBE_DIR/app.js" -w '%{http_code}' "$JS_URL?release_check=$BUSTER" || true)"
    CSS_STATUS="$(curl -sS -L --max-time 30 -H 'Cache-Control: no-cache, no-store' -o "$PROBE_DIR/app.css" -w '%{http_code}' "$CSS_URL?release_check=$BUSTER" || true)"
    if [[ "$JS_STATUS" == 200 && "$CSS_STATUS" == 200 ]] \
      && grep -Fq '/api/runtime/timesheet/steward/v2/users' "$PROBE_DIR/app.js" \
      && grep -Fq '/api/runtime/timesheet/steward/v2/entries/' "$PROBE_DIR/app.js" \
      && grep -Fq 'Engineering, Engineering Lead, Project Management, and Project Management Lead' "$PROBE_DIR/app.js" \
      && grep -Fq 'Move time across all supported activity types' "$PROBE_DIR/app.js" \
      && grep -Fq 'Requests / Service Requests' "$PROBE_DIR/app.js" \
      && grep -Fq 'Project Tasks' "$PROBE_DIR/app.js" \
      && grep -Fq 'Non-Project Time' "$PROBE_DIR/app.js" \
      && grep -Fq '/api/timesheet/timers/targets' "$PROBE_DIR/app.js" \
      && grep -Fq '/api/timesheet/timers/active' "$PROBE_DIR/app.js" \
      && grep -Fq '/api/timesheet/timers/history' "$PROBE_DIR/app.js" \
      && grep -Fq 'Timer history' "$PROBE_DIR/app.js" \
      && grep -Fq 'sign-out, and session expiration' "$PROBE_DIR/app.js" \
      && grep -Fq 'data-projectpulse-react-owned-slot' "$PROBE_DIR/app.js" \
      && grep -Fq 'react-owned-v1' "$PROBE_DIR/app.js" \
      && grep -Fq 'attributes-only-v1' "$PROBE_DIR/app.js" \
      && grep -Fq 'Search by page name' "$PROBE_DIR/app.js" \
      && grep -Fq 'Edit connection' "$PROBE_DIR/app.js" \
      && grep -Fq 'Add CRM platform' "$PROBE_DIR/app.js" \
      && grep -Fq '/api/platform-operations/overview' "$PROBE_DIR/app.js" \
      && grep -Fq '.module001-server-timer-recovery' "$PROBE_DIR/app.css" \
      && grep -Fq '.ptc-destination-catalog' "$PROBE_DIR/app.css" \
      && grep -Fq '.projectpulse-more-intuitive-name' "$PROBE_DIR/app.css" \
      && ! grep -Fq 'ptc-runtime-task-catalog-host' "$PROBE_DIR/app.js" \
      && ! grep -Fq 'module001-toolbar-host' "$PROBE_DIR/app.js"; then
      SERVED_READY=true
      break
    fi
  fi
  sleep 5
done
[[ "$SERVED_READY" == true ]] || fail "The served web bundle did not expose the Module 001 PTC/timer, React-owned navigation, Module 026, and Group 2A release markers."

READY_WEB="$(az containerapp show -g "$RESOURCE_GROUP" -n "$WEB_APP" --query 'properties.latestReadyRevisionName' -o tsv --only-show-errors)"
[[ "$READY_WEB" == "$WEB_REVISION" ]] || fail "Unexpected ready web revision: $READY_WEB"
ACTIVE_WEB_IMAGE="$(az containerapp revision show -g "$RESOURCE_GROUP" -n "$WEB_APP" --revision "$READY_WEB" --query 'properties.template.containers[0].image' -o tsv --only-show-errors)"
[[ "$ACTIVE_WEB_IMAGE" == "$WEB_IMAGE" ]] || fail "Unexpected active web image."
echo "MODULE001_PTC_TIMER_DOM_WEB_VALIDATION=PASS"

mkdir -p "$CONTROL_ROOT/evidence"
cat > "$CONTROL_ROOT/evidence/module001-ptc-timer-dom-module026-test-deployment.json" <<JSON
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
  "module001ResultExecution": "explicit-iresult-v1",
  "eligiblePtcRoleFamilies": ["ENGINEERING", "ENGINEERING_LEAD", "PROJECT_MANAGEMENT", "PROJECT_MANAGEMENT_LEAD"],
  "ptcDestinations": ["REQUESTS_SERVICE_REQUESTS", "PROJECT_TASKS", "NON_PROJECT_TIME"],
  "crossTaskAssignmentDuringMove": "authorized-and-audited",
  "submissionOnBehalf": false,
  "timerAuthority": "server",
  "timerRecovery": "refresh-signout-session-expiration-next-login",
  "timerHistory": "visible-current-week",
  "reactChildOwnership": "static-generated-slots",
  "runtimeNavigationMutation": "attributes-only",
  "module026Pr207": "editable-built-in-and-custom-connectors-included",
  "group2A": "modules-013-016-068-preserved",
  "roleOrPolicyMutationPerformedByDeployment": false,
  "timeOrTimerMutationPerformedByDeployment": false,
  "credentialValuesChanged": false,
  "externalProviderCallsPerformedByDeployment": false,
  "imageRollbackOnFailure": "candidate-only",
  "smokeTests": "passed",
  "authenticatedFunctionalUat": "required"
}
JSON

echo "MODULE001_PTC_TIMER_DOM_MODULE026_TEST_DEPLOYMENT=COMPLETE"
