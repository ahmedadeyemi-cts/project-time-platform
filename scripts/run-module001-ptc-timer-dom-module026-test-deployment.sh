#!/usr/bin/env bash
set -Eeuo pipefail

EXPECTED_RELEASE_COMMIT="9a1308de1186abef6c199e9e6453f222b5e95999"
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
PROBE_DIR="${RUNNER_TEMP:-/tmp}/module001-module026-microsoft-runtime-probes"
SERVED_DIR="$PROBE_DIR/served"
mkdir -p "$PROBE_DIR" "$SERVED_DIR"

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
    suffix="cmwebrb-${RUN_ID}-${RUN_ATTEMPT}"
    revision="$WEB_APP--$suffix"
    az containerapp update -g "$RESOURCE_GROUP" -n "$WEB_APP" --image "$CURRENT_WEB_IMAGE" --revision-suffix "$suffix" --output none --only-show-errors
    bash "$WAIT_SCRIPT" "$RESOURCE_GROUP" "$WEB_APP" "$revision" "$CURRENT_WEB_IMAGE" 60 10
    echo "COMBINED_MODULE_RUNTIME_WEB_ROLLBACK=COMPLETE"
  else
    echo "Web rollback skipped because another image is active: $active" >&2
  fi
}

restore_api() {
  [[ "$API_STARTED" == 1 && -n "$CURRENT_API_IMAGE" && -n "$API_IMAGE" ]] || return 0
  local active suffix revision
  active="$(az containerapp show -g "$RESOURCE_GROUP" -n "$API_APP" --query 'properties.template.containers[0].image' -o tsv 2>/dev/null)"
  if [[ "$active" == "$API_IMAGE" ]]; then
    suffix="cmapirb-${RUN_ID}-${RUN_ATTEMPT}"
    revision="$API_APP--$suffix"
    az containerapp update -g "$RESOURCE_GROUP" -n "$API_APP" --image "$CURRENT_API_IMAGE" --revision-suffix "$suffix" --output none --only-show-errors
    bash "$WAIT_SCRIPT" "$RESOURCE_GROUP" "$API_APP" "$revision" "$CURRENT_API_IMAGE" 60 10
    echo "COMBINED_MODULE_RUNTIME_API_ROLLBACK=COMPLETE"
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
    local candidate="$url${separator}release_check=combined-${RUN_ID}-${RUN_ATTEMPT}-${attempt}"
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
      echo "COMBINED_MODULE_RUNTIME_PROBE_${name^^}=HTTP_$status"
      return 0
    fi
    sleep 5
  done
  echo "$path did not reach expected status $expected with JSON." >&2
  cat "$body_file" >&2 || true
  return 1
}

normalize_asset_path() {
  local value="${1%%\#*}"
  value="${value%%\?*}"
  value="${value#./}"
  [[ "$value" == assets/* ]] && value="/$value"
  printf '%s\n' "$value"
}

asset_url() {
  local value
  value="$(normalize_asset_path "$1")"
  case "$value" in
    https://*|http://*) printf '%s\n' "$value" ;;
    //*) printf 'https:%s\n' "$value" ;;
    /*) printf '%s%s\n' "$BASE_URL" "$value" ;;
    *) printf '%s/%s\n' "$BASE_URL" "$value" ;;
  esac
}

cache_busted_url() {
  local url="$1" buster="$2" separator='?'
  [[ "$url" == *\?* ]] && separator='&'
  printf '%s%srelease_check=%s\n' "$url" "$separator" "$buster"
}

fetch_asset() {
  local raw_path="$1" destination="$2" buster="$3" url status
  url="$(asset_url "$raw_path")"
  url="$(cache_busted_url "$url" "$buster")"
  status="$(curl -sS -L --max-time 30 \
    -H 'Cache-Control: no-cache, no-store' \
    -H 'Pragma: no-cache' \
    -o "$destination" \
    -w '%{http_code}' \
    "$url" || true)"
  if [[ "$status" != 200 ]]; then
    echo "SERVED_ASSET_FETCH_FAILED path=$raw_path status=$status url=$url" >&2
    return 1
  fi
  return 0
}

fetch_served_asset_graph() {
  local buster="$1" html_status raw path url file index=0
  rm -rf "$SERVED_DIR"
  mkdir -p "$SERVED_DIR/js" "$SERVED_DIR/css"
  : > "$SERVED_DIR/bundle.js"
  : > "$SERVED_DIR/bundle.css"
  : > "$SERVED_DIR/assets.txt"

  html_status="$(curl -sS -L --max-time 30 \
    -H 'Cache-Control: no-cache, no-store' \
    -H 'Pragma: no-cache' \
    -o "$SERVED_DIR/index.html" \
    -w '%{http_code}' \
    "$BASE_URL/?release_check=$buster" || true)"
  [[ "$html_status" == 200 ]] || {
    echo "SERVED_INDEX_FETCH_FAILED status=$html_status" >&2
    return 1
  }

  mapfile -t js_paths < <(
    grep -Eo '(src|href)="[^"]+\.js[^"]*"' "$SERVED_DIR/index.html" 2>/dev/null \
      | sed -E 's/^(src|href)="//; s/"$//' \
      | sed -E 's/[?#].*$//' \
      | sed '/^$/d' \
      | sort -u
  )
  mapfile -t css_paths < <(
    grep -Eo 'href="[^"]+\.css[^"]*"' "$SERVED_DIR/index.html" 2>/dev/null \
      | sed -E 's/^href="//; s/"$//' \
      | sed -E 's/[?#].*$//' \
      | sed '/^$/d' \
      | sort -u
  )
  [[ "${#js_paths[@]}" -gt 0 ]] || {
    echo 'SERVED_JS_ENTRYPOINTS=0' >&2
    return 1
  }

  declare -A seen_js=()
  while (( index < ${#js_paths[@]} )); do
    raw="${js_paths[$index]}"
    index=$((index + 1))
    path="$(normalize_asset_path "$raw")"
    url="$(asset_url "$path")"
    [[ -n "${seen_js[$url]:-}" ]] && continue
    seen_js[$url]=1
    file="$SERVED_DIR/js/$(printf '%04d' "${#seen_js[@]}").js"
    fetch_asset "$path" "$file" "$buster" || return 1
    printf '\n/* served asset: %s */\n' "$url" >> "$SERVED_DIR/bundle.js"
    cat "$file" >> "$SERVED_DIR/bundle.js"
    printf 'JS %s\n' "$url" >> "$SERVED_DIR/assets.txt"

    while IFS= read -r raw; do
      [[ -n "$raw" ]] && js_paths+=("$raw")
    done < <(
      grep -Eo '(\./|/)?assets/[A-Za-z0-9._-]+\.js' "$file" 2>/dev/null \
        | sed -E 's#^\./#/#; s#^assets/#/assets/#' \
        | sort -u
    )
    while IFS= read -r raw; do
      [[ -n "$raw" ]] && css_paths+=("$raw")
    done < <(
      grep -Eo '(\./|/)?assets/[A-Za-z0-9._-]+\.css' "$file" 2>/dev/null \
        | sed -E 's#^\./#/#; s#^assets/#/assets/#' \
        | sort -u
    )
  done

  declare -A seen_css=()
  for raw in "${css_paths[@]}"; do
    path="$(normalize_asset_path "$raw")"
    url="$(asset_url "$path")"
    [[ -n "${seen_css[$url]:-}" ]] && continue
    seen_css[$url]=1
    file="$SERVED_DIR/css/$(printf '%04d' "${#seen_css[@]}").css"
    fetch_asset "$path" "$file" "$buster" || return 1
    printf '\n/* served asset: %s */\n' "$url" >> "$SERVED_DIR/bundle.css"
    cat "$file" >> "$SERVED_DIR/bundle.css"
    printf 'CSS %s\n' "$url" >> "$SERVED_DIR/assets.txt"
  done

  [[ "${#seen_css[@]}" -gt 0 ]] || {
    echo 'SERVED_CSS_ASSETS=0' >&2
    return 1
  }
  echo "SERVED_JS_ASSET_COUNT=${#seen_js[@]}"
  echo "SERVED_CSS_ASSET_COUNT=${#seen_css[@]}"
  cat "$SERVED_DIR/assets.txt"
}

JS_MARKERS=(
  'PTC_USERS|/api/runtime/timesheet/steward/v2/users'
  'PTC_MOVE|/api/runtime/timesheet/steward/v2/entries/'
  'PTC_ROLES|Engineering, Engineering Lead, Project Management, and Project Management Lead'
  'PTC_DESTINATIONS|Move time across all supported activity types'
  'REQUESTS_GROUP|Requests / Service Requests'
  'PROJECT_TASKS_GROUP|Project Tasks'
  'NON_PROJECT_GROUP|Non-Project Time'
  'TIMER_TARGETS|/api/timesheet/timers/targets'
  'TIMER_ACTIVE|/api/timesheet/timers/active'
  'TIMER_HISTORY_ENDPOINT|/api/timesheet/timers/history'
  'TIMER_HISTORY_UI|Timer history'
  'TIMER_SESSION_RECOVERY|sign-out, and session expiration'
  'REACT_OWNED_SLOT|data-projectpulse-react-owned-slot'
  'REACT_OWNED_MORE|react-owned-v1'
  'ATTRIBUTE_ONLY_NAVIGATION|attributes-only-v1'
  'MORE_SEARCH|Search by page name'
  'MODULE026_EDIT|Edit connection'
  'MODULE026_ADD|Add CRM platform'
  'GROUP2A_OVERVIEW|/api/platform-operations/overview'
  'MICROSOFT_SYNC|/api/microsoft-integration/directory-users/sync-now'
  'MICROSOFT_PROFILE_GATE|module_065_services_profile_not_active'
  'MICROSOFT_MANUAL_SYNC|Manual only'
  'MICROSOFT_AUTOMATIC_SYNC|Automatic and manual'
  'MICROSOFT_SYNC_INTERVAL|between 1 and 168 hours'
  'MICROSOFT_MAIL_TEST|/api/microsoft-integration/mail-runtime/test'
  'MICROSOFT_NON_DELIVERY|No live message is sent.'
  'MICROSOFT_CONFIGURED_PROVIDER|configuredProvider'
  'MICROSOFT_ACTIVE_PROVIDER|activeDeliveryProvider'
  'MICROSOFT_PORTAL|Microsoft Integration'
  'MICROSOFT_CALLBACK|Use current callback'
)

CSS_MARKERS=(
  'TIMER_RECOVERY_STYLE|.module001-server-timer-recovery'
  'PTC_DESTINATION_STYLE|.ptc-destination-catalog'
  'MORE_NAME_STYLE|.projectpulse-more-intuitive-name'
  'MICROSOFT_ENVIRONMENT_STYLE|.microsoft-environment-switcher'
  'MICROSOFT_SYNC_STYLE|.microsoft-directory-sync-card'
  'MICROSOFT_MAIL_STYLE|.microsoft-mail-environment-card'
  'MICROSOFT_READINESS_STYLE|.microsoft-mail-readiness-panel'
)

bundle_ready() {
  local spec label marker
  for spec in "${JS_MARKERS[@]}"; do
    IFS='|' read -r label marker <<<"$spec"
    grep -Fq -- "$marker" "$SERVED_DIR/bundle.js" || return 1
  done
  for spec in "${CSS_MARKERS[@]}"; do
    IFS='|' read -r label marker <<<"$spec"
    grep -Fq -- "$marker" "$SERVED_DIR/bundle.css" || return 1
  done
  ! grep -Fq 'ptc-runtime-task-catalog-host' "$SERVED_DIR/bundle.js" \
    && ! grep -Fq 'module001-toolbar-host' "$SERVED_DIR/bundle.js"
}

report_bundle_markers() {
  local spec label marker failed=0
  for spec in "${JS_MARKERS[@]}"; do
    IFS='|' read -r label marker <<<"$spec"
    if grep -Fq -- "$marker" "$SERVED_DIR/bundle.js"; then
      echo "SERVED_JS_MARKER_${label}=PASS"
    else
      echo "SERVED_JS_MARKER_${label}=MISSING" >&2
      failed=1
    fi
  done
  for spec in "${CSS_MARKERS[@]}"; do
    IFS='|' read -r label marker <<<"$spec"
    if grep -Fq -- "$marker" "$SERVED_DIR/bundle.css"; then
      echo "SERVED_CSS_MARKER_${label}=PASS"
    else
      echo "SERVED_CSS_MARKER_${label}=MISSING" >&2
      failed=1
    fi
  done
  for marker in ptc-runtime-task-catalog-host module001-toolbar-host; do
    if grep -Fq -- "$marker" "$SERVED_DIR/bundle.js"; then
      echo "SERVED_JS_FORBIDDEN_${marker^^}=PRESENT" >&2
      failed=1
    else
      echo "SERVED_JS_FORBIDDEN_${marker^^}=ABSENT"
    fi
  done
  return "$failed"
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
API_DIGEST="$("$BUILD_SCRIPT" "$ACR_NAME" "$API_REPOSITORY:combined-module-runtime-$TARGET_COMMIT" "$RELEASE_ROOT/deployment/containers/api/Dockerfile" "$RELEASE_ROOT")"
WEB_DIGEST="$("$BUILD_SCRIPT" "$ACR_NAME" "$WEB_REPOSITORY:combined-module-runtime-$TARGET_COMMIT" "$RELEASE_ROOT/deployment/containers/web/Dockerfile" "$RELEASE_ROOT")"
[[ "$API_DIGEST" == sha256:* && "$WEB_DIGEST" == sha256:* ]] || fail "Candidate image build did not return immutable digests."
API_IMAGE="$ACR_NAME.azurecr.io/$API_REPOSITORY@$API_DIGEST"
WEB_IMAGE="$ACR_NAME.azurecr.io/$WEB_REPOSITORY@$WEB_DIGEST"
echo "COMBINED_MODULE_RUNTIME_API_IMAGE=$API_IMAGE"
echo "COMBINED_MODULE_RUNTIME_WEB_IMAGE=$WEB_IMAGE"

API_SUFFIX="cmapi-${RUN_ID}-${RUN_ATTEMPT}"
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
probe_api microsoft_sync_status GET '/api/microsoft-integration/directory-users/sync-status' '401,403'
probe_api microsoft_sync_now POST '/api/microsoft-integration/directory-users/sync-now' '401,403' '{"environmentMode":"test"}'
probe_api microsoft_mail_test POST '/api/microsoft-integration/mail-runtime/test' '401,403' '{"environmentMode":"test"}'
probe_api microsoft_preview POST '/api/admin/azure/users/preview' '401,403' '{}'
echo "COMBINED_MODULE_RUNTIME_API_PROTECTED_BOUNDARY=PASS"

READY_API="$(az containerapp show -g "$RESOURCE_GROUP" -n "$API_APP" --query 'properties.latestReadyRevisionName' -o tsv --only-show-errors)"
[[ "$READY_API" == "$API_REVISION" ]] || fail "Unexpected ready API revision: $READY_API"
ACTIVE_API_IMAGE="$(az containerapp revision show -g "$RESOURCE_GROUP" -n "$API_APP" --revision "$READY_API" --query 'properties.template.containers[0].image' -o tsv --only-show-errors)"
[[ "$ACTIVE_API_IMAGE" == "$API_IMAGE" ]] || fail "Unexpected active API image."
echo "COMBINED_MODULE_RUNTIME_API_VALIDATION=PASS"

WEB_SUFFIX="cmweb-${RUN_ID}-${RUN_ATTEMPT}"
WEB_REVISION="$WEB_APP--$WEB_SUFFIX"
WEB_STARTED=1
az containerapp update -g "$RESOURCE_GROUP" -n "$WEB_APP" --image "$WEB_IMAGE" --revision-suffix "$WEB_SUFFIX" --output none --only-show-errors
bash "$WAIT_SCRIPT" "$RESOURCE_GROUP" "$WEB_APP" "$WEB_REVISION" "$WEB_IMAGE" 60 10

# Traverse the public web proxy after the web candidate is active. Every write
# probe is unauthenticated and must stop at authorization before any mutation or
# provider call can execute.
probe_api proxy_ptc_users GET '/api/runtime/timesheet/steward/v2/users?weekStart=2026-07-26' '401,403'
probe_api proxy_timer_active GET '/api/timesheet/timers/active' '401,403'
probe_api proxy_timer_history GET '/api/timesheet/timers/history?weekStart=2026-07-26' '401,403'
probe_api proxy_module026 GET '/api/integrations/026/providers' '401,403'
probe_api proxy_microsoft_sync GET '/api/microsoft-integration/directory-users/sync-status' '401,403'
probe_api proxy_microsoft_mail_test POST '/api/microsoft-integration/mail-runtime/test' '401,403' '{"environmentMode":"test"}'

SERVED_READY=false
for attempt in $(seq 1 24); do
  BUSTER="combined-${RUN_ID}-${RUN_ATTEMPT}-${attempt}"
  if fetch_served_asset_graph "$BUSTER" && bundle_ready; then
    report_bundle_markers
    SERVED_READY=true
    break
  fi
  echo "SERVED_BUNDLE_ATTEMPT=$attempt RESULT=INCOMPLETE"
  sleep 5
done
if [[ "$SERVED_READY" != true ]]; then
  report_bundle_markers || true
  fail "The served multi-asset web bundle did not expose the complete Module 001, Module 026, Microsoft runtime, React-owned navigation, and Group 2A release markers."
fi
echo 'SERVED_MULTI_ASSET_BUNDLE_VALIDATION=PASS'

READY_WEB="$(az containerapp show -g "$RESOURCE_GROUP" -n "$WEB_APP" --query 'properties.latestReadyRevisionName' -o tsv --only-show-errors)"
[[ "$READY_WEB" == "$WEB_REVISION" ]] || fail "Unexpected ready web revision: $READY_WEB"
ACTIVE_WEB_IMAGE="$(az containerapp revision show -g "$RESOURCE_GROUP" -n "$WEB_APP" --revision "$READY_WEB" --query 'properties.template.containers[0].image' -o tsv --only-show-errors)"
[[ "$ACTIVE_WEB_IMAGE" == "$WEB_IMAGE" ]] || fail "Unexpected active web image."
echo "COMBINED_MODULE_RUNTIME_WEB_VALIDATION=PASS"

mkdir -p "$CONTROL_ROOT/evidence"
cp -R "$SERVED_DIR" "$CONTROL_ROOT/evidence/module001-module026-microsoft-runtime-served-assets"
cat > "$CONTROL_ROOT/evidence/module001-module026-microsoft-runtime-test-deployment.json" <<JSON
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
  "servedBundleValidation": "all-index-and-dynamically-referenced-js-css-assets",
  "module026Pr207": "editable-built-in-and-custom-connectors-included",
  "microsoftRuntimePr208": "included",
  "microsoftRuntimeEnvironmentResolution": "trusted-host-precedes-aspnetcore",
  "interactiveSsoProfileHydration": "environment-specific",
  "directorySync": "manual-and-automatic-1-168-hours",
  "directorySyncLocks": "process-and-postgresql-advisory",
  "mailConfigurationScope": "test-and-production-independent",
  "mailReadiness": "non-delivery-no-secrets",
  "group2A": "modules-013-016-068-preserved",
  "roleOrPolicyMutationPerformedByDeployment": false,
  "timeOrTimerMutationPerformedByDeployment": false,
  "directorySyncExecutedByDeployment": false,
  "mailReadinessExecutedByDeployment": false,
  "emailSentByDeployment": false,
  "credentialValuesChanged": false,
  "externalProviderCallsPerformedByDeployment": false,
  "imageRollbackOnFailure": "candidate-only",
  "smokeTests": "passed",
  "authenticatedFunctionalUat": "required"
}
JSON

echo "MODULE001_MODULE026_MICROSOFT_RUNTIME_TEST_DEPLOYMENT=COMPLETE"
