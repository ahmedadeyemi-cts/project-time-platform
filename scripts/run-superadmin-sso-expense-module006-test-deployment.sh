#!/usr/bin/env bash
set -Eeuo pipefail

EXPECTED_RELEASE_COMMIT="185a0030dbc96813c8cd46498668ca289805a4d7"
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
EXPECTED_CALLBACK="$BASE_URL/api/auth/sso/callback"
API_STARTED=0
WEB_STARTED=0
CURRENT_API_IMAGE=""
CURRENT_WEB_IMAGE=""
API_IMAGE=""
WEB_IMAGE=""
API_REVISION=""
WEB_REVISION=""
PROBE_DIR="${RUNNER_TEMP:-/tmp}/superadmin-sso-expense-module006-probes"
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
    suffix="sawebrb-${RUN_ID}-${RUN_ATTEMPT}"
    revision="$WEB_APP--$suffix"
    az containerapp update -g "$RESOURCE_GROUP" -n "$WEB_APP" --image "$CURRENT_WEB_IMAGE" --revision-suffix "$suffix" --output none --only-show-errors
    bash "$WAIT_SCRIPT" "$RESOURCE_GROUP" "$WEB_APP" "$revision" "$CURRENT_WEB_IMAGE" 60 10
    echo "SUPERADMIN_SSO_EXPENSE_MODULE006_WEB_ROLLBACK=COMPLETE"
  else
    echo "Web rollback skipped because another image is active: $active" >&2
  fi
}

restore_api() {
  [[ "$API_STARTED" == 1 && -n "$CURRENT_API_IMAGE" && -n "$API_IMAGE" ]] || return 0
  local active suffix revision
  active="$(az containerapp show -g "$RESOURCE_GROUP" -n "$API_APP" --query 'properties.template.containers[0].image' -o tsv 2>/dev/null)"
  if [[ "$active" == "$API_IMAGE" ]]; then
    suffix="saapirb-${RUN_ID}-${RUN_ATTEMPT}"
    revision="$API_APP--$suffix"
    az containerapp update -g "$RESOURCE_GROUP" -n "$API_APP" --image "$CURRENT_API_IMAGE" --revision-suffix "$suffix" --output none --only-show-errors
    bash "$WAIT_SCRIPT" "$RESOURCE_GROUP" "$API_APP" "$revision" "$CURRENT_API_IMAGE" 60 10
    echo "SUPERADMIN_SSO_EXPENSE_MODULE006_API_ROLLBACK=COMPLETE"
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
    local candidate="$url${separator}release_check=sae006-${RUN_ID}-${RUN_ATTEMPT}-${attempt}"
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
      echo "SUPERADMIN_SSO_EXPENSE_MODULE006_PROBE_${name^^}=HTTP_$status"
      return 0
    fi
    sleep 5
  done
  echo "$path did not reach expected status $expected with JSON." >&2
  cat "$body_file" >&2 || true
  return 1
}

probe_sso_redirect() {
  local phase="$1" status='' location='' redirect_uri='' authority_host=''
  local headers_file="$PROBE_DIR/sso-${phase}.headers" body_file="$PROBE_DIR/sso-${phase}.body"
  for attempt in $(seq 1 18); do
    status="$(curl -sS --max-time 30 --max-redirs 0 \
      -D "$headers_file" \
      -o "$body_file" \
      -w '%{http_code}' \
      -H 'Accept: text/html,application/xhtml+xml,application/json' \
      -H 'Cache-Control: no-cache, no-store' \
      -H 'Pragma: no-cache' \
      -H "Origin: $BASE_URL" \
      -H "Referer: $BASE_URL/" \
      "$BASE_URL/api/auth/sso/start?prompt=select_account&release_check=sae006-${RUN_ID}-${RUN_ATTEMPT}-${attempt}" || true)"
    location="$(awk 'BEGIN{IGNORECASE=1} /^location:/ {sub(/^[^:]*:[[:space:]]*/, ""); gsub(/\r/, ""); print}' "$headers_file" | tail -1)"
    if [[ "$status" =~ ^30[2378]$ && -n "$location" ]]; then
      redirect_uri="$(python3 - "$location" <<'PY'
import sys
from urllib.parse import parse_qs, urlparse
value = sys.argv[1]
print(parse_qs(urlparse(value).query).get('redirect_uri', [''])[0])
PY
)"
      authority_host="$(python3 - "$location" <<'PY'
import sys
from urllib.parse import urlparse
print((urlparse(sys.argv[1]).hostname or '').lower())
PY
)"
      [[ "$authority_host" == 'login.microsoftonline.com' ]] || {
        echo "Unexpected Microsoft authorization host: $authority_host" >&2
        return 1
      }
      [[ "$redirect_uri" == "$EXPECTED_CALLBACK" ]] || {
        echo "SSO redirect_uri mismatch. Expected $EXPECTED_CALLBACK but received $redirect_uri" >&2
        return 1
      }
      if grep -Eiq 'azurecontainerapps\.io|\.internal\.' <<<"$location $redirect_uri"; then
        echo 'SSO redirect leaked an internal Azure Container Apps authority.' >&2
        return 1
      fi
      printf '%s\n' "$location" > "$PROBE_DIR/sso-${phase}.location"
      echo "SUPERADMIN_SSO_EXPENSE_MODULE006_SSO_${phase^^}=PUBLIC_CALLBACK_VERIFIED"
      return 0
    fi
    sleep 5
  done
  echo "Interactive SSO start did not return an approved Microsoft redirect. Last status=$status" >&2
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
  [[ "$status" == 200 ]] || {
    echo "SERVED_ASSET_FETCH_FAILED path=$raw_path status=$status url=$url" >&2
    return 1
  }
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
  [[ "$html_status" == 200 ]] || return 1

  mapfile -t js_paths < <(
    grep -Eo '(src|href)="[^"]+\.js[^"]*"' "$SERVED_DIR/index.html" 2>/dev/null \
      | sed -E 's/^(src|href)="//; s/"$//' \
      | sed -E 's/[?#].*$//' | sed '/^$/d' | sort -u
  )
  mapfile -t css_paths < <(
    grep -Eo 'href="[^"]+\.css[^"]*"' "$SERVED_DIR/index.html" 2>/dev/null \
      | sed -E 's/^href="//; s/"$//' \
      | sed -E 's/[?#].*$//' | sed '/^$/d' | sort -u
  )
  [[ "${#js_paths[@]}" -gt 0 ]] || return 1

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

    while IFS= read -r raw; do [[ -n "$raw" ]] && js_paths+=("$raw"); done < <(
      grep -Eo '(\./|/)?assets/[A-Za-z0-9._-]+\.js' "$file" 2>/dev/null \
        | sed -E 's#^\./#/#; s#^assets/#/assets/#' | sort -u
    )
    while IFS= read -r raw; do [[ -n "$raw" ]] && css_paths+=("$raw"); done < <(
      grep -Eo '(\./|/)?assets/[A-Za-z0-9._-]+\.css' "$file" 2>/dev/null \
        | sed -E 's#^\./#/#; s#^assets/#/assets/#' | sort -u
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

  [[ "${#seen_css[@]}" -gt 0 ]] || return 1
  echo "SERVED_JS_ASSET_COUNT=${#seen_js[@]}"
  echo "SERVED_CSS_ASSET_COUNT=${#seen_css[@]}"
  cat "$SERVED_DIR/assets.txt"
}

JS_MARKERS=(
  'MODULE026_EDIT|Edit connection'
  'MODULE026_ADD|Add CRM platform'
  'EXPENSE_LAUNCHER|Project expenses'
  'EXPENSE_CONTEXT|/billing-context'
  'EXPENSE_ACK|/billing-acknowledgement'
  'EXPENSE_NON_INVASIVE|non-invasive-v2'
  'EXPENSE_EXPLICIT_PROJECT|Choose a project only when expense context is needed'
  'EXPENSE_DELETED_EXCLUDED|Deleted and superseded uploads are excluded.'
  'MODULE006_NAME|Toyota & Hyundai Pipeline'
  'MODULE003_TABLE|engineering-utilization-manager-table'
  'RBAC_BOOTSTRAP|/api/rbac/v1/bootstrap'
  'GROUP2A_OVERVIEW|/api/platform-operations/overview'
  'MICROSOFT_RUNTIME|Microsoft Integration'
  'MICROSOFT_SYNC|/api/microsoft-integration/directory-users/sync-now'
)

CSS_MARKERS=(
  'EXPENSE_LAUNCHER_STYLE|.expense-cross-module-launcher'
  'EXPENSE_OPEN_STYLE|.expense-cross-module-shell.is-open'
  'EXPENSE_ACK_STYLE|.expense-cross-acknowledgement'
  'MODULE003_TABLE_STYLE|.engineering-utilization-table-wrap'
  'MODULE003_STICKY_STYLE|.engineering-utilization-table thead th'
  'MORE_NAME_STYLE|.projectpulse-more-intuitive-name'
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
echo "CAPTURED_CURRENT_API_IMAGE=$CURRENT_API_IMAGE"
echo "CAPTURED_CURRENT_WEB_IMAGE=$CURRENT_WEB_IMAGE"

API_REPOSITORY='project-health-dashboard-api'
WEB_REPOSITORY='project-health-dashboard-web'
API_DIGEST="$("$BUILD_SCRIPT" "$ACR_NAME" "$API_REPOSITORY:superadmin-sso-expense-module006-$TARGET_COMMIT" "$RELEASE_ROOT/deployment/containers/api/Dockerfile" "$RELEASE_ROOT")"
WEB_DIGEST="$("$BUILD_SCRIPT" "$ACR_NAME" "$WEB_REPOSITORY:superadmin-sso-expense-module006-$TARGET_COMMIT" "$RELEASE_ROOT/deployment/containers/web/Dockerfile" "$RELEASE_ROOT")"
[[ "$API_DIGEST" == sha256:* && "$WEB_DIGEST" == sha256:* ]] || fail "Candidate image build did not return immutable digests."
API_IMAGE="$ACR_NAME.azurecr.io/$API_REPOSITORY@$API_DIGEST"
WEB_IMAGE="$ACR_NAME.azurecr.io/$WEB_REPOSITORY@$WEB_DIGEST"
echo "SUPERADMIN_SSO_EXPENSE_MODULE006_API_IMAGE=$API_IMAGE"
echo "SUPERADMIN_SSO_EXPENSE_MODULE006_WEB_IMAGE=$WEB_IMAGE"

API_SUFFIX="saapi-${RUN_ID}-${RUN_ATTEMPT}"
API_REVISION="$API_APP--$API_SUFFIX"
API_STARTED=1
az containerapp update -g "$RESOURCE_GROUP" -n "$API_APP" --image "$API_IMAGE" --revision-suffix "$API_SUFFIX" --output none --only-show-errors
bash "$WAIT_SCRIPT" "$RESOURCE_GROUP" "$API_APP" "$API_REVISION" "$API_IMAGE" 60 10

probe_api health GET '/health' '200'
probe_api version GET '/api/version' '200'
probe_api module026_providers GET '/api/integrations/026/providers' '401,403'
probe_api expense_context GET '/api/project-expenses/context' '401,403'
probe_api expense_billing_context GET '/api/project-expenses/projects/00000000-0000-0000-0000-000000000000/billing-context' '401,403'
probe_api expense_ack POST '/api/project-expenses/projects/00000000-0000-0000-0000-000000000000/billing-acknowledgement' '401,403' '{"reason":"deployment protected boundary probe"}'
probe_api billing_candidates GET '/api/billing/candidates' '401,403'
probe_api rbac_bootstrap GET '/api/rbac/v1/bootstrap' '401,403'
probe_api rbac_matrix GET '/api/rbac/v1/matrix' '401,403'
probe_api platform_overview GET '/api/platform-operations/overview' '401,403'
probe_api microsoft_sync GET '/api/microsoft-integration/directory-users/sync-status' '401,403'
probe_sso_redirect api

echo "SUPERADMIN_SSO_EXPENSE_MODULE006_API_PROTECTED_BOUNDARY=PASS"
READY_API="$(az containerapp show -g "$RESOURCE_GROUP" -n "$API_APP" --query 'properties.latestReadyRevisionName' -o tsv --only-show-errors)"
[[ "$READY_API" == "$API_REVISION" ]] || fail "Unexpected ready API revision: $READY_API"
ACTIVE_API_IMAGE="$(az containerapp revision show -g "$RESOURCE_GROUP" -n "$API_APP" --revision "$READY_API" --query 'properties.template.containers[0].image' -o tsv --only-show-errors)"
[[ "$ACTIVE_API_IMAGE" == "$API_IMAGE" ]] || fail "Unexpected active API image."
echo "SUPERADMIN_SSO_EXPENSE_MODULE006_API_VALIDATION=PASS"

WEB_SUFFIX="saweb-${RUN_ID}-${RUN_ATTEMPT}"
WEB_REVISION="$WEB_APP--$WEB_SUFFIX"
WEB_STARTED=1
az containerapp update -g "$RESOURCE_GROUP" -n "$WEB_APP" --image "$WEB_IMAGE" --revision-suffix "$WEB_SUFFIX" --output none --only-show-errors
bash "$WAIT_SCRIPT" "$RESOURCE_GROUP" "$WEB_APP" "$WEB_REVISION" "$WEB_IMAGE" 60 10

probe_api proxy_module026 GET '/api/integrations/026/providers' '401,403'
probe_api proxy_expense_context GET '/api/project-expenses/projects/00000000-0000-0000-0000-000000000000/billing-context' '401,403'
probe_api proxy_billing_candidates GET '/api/billing/candidates' '401,403'
probe_sso_redirect web

SERVED_READY=false
for attempt in $(seq 1 24); do
  BUSTER="sae006-${RUN_ID}-${RUN_ATTEMPT}-${attempt}"
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
  fail "The served multi-asset web bundle did not expose the Super Administrator, SSO, expense billing, Module 006, Module 003, RBAC, and Group 2A release markers."
fi
echo 'SUPERADMIN_SSO_EXPENSE_MODULE006_SERVED_BUNDLE_VALIDATION=PASS'

READY_WEB="$(az containerapp show -g "$RESOURCE_GROUP" -n "$WEB_APP" --query 'properties.latestReadyRevisionName' -o tsv --only-show-errors)"
[[ "$READY_WEB" == "$WEB_REVISION" ]] || fail "Unexpected ready web revision: $READY_WEB"
ACTIVE_WEB_IMAGE="$(az containerapp revision show -g "$RESOURCE_GROUP" -n "$WEB_APP" --revision "$READY_WEB" --query 'properties.template.containers[0].image' -o tsv --only-show-errors)"
[[ "$ACTIVE_WEB_IMAGE" == "$WEB_IMAGE" ]] || fail "Unexpected active web image."
echo "SUPERADMIN_SSO_EXPENSE_MODULE006_WEB_VALIDATION=PASS"

mkdir -p "$CONTROL_ROOT/evidence"
cp -R "$SERVED_DIR" "$CONTROL_ROOT/evidence/superadmin-sso-expense-module006-served-assets"
cat > "$CONTROL_ROOT/evidence/superadmin-sso-expense-module006-test-deployment.json" <<JSON
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
  "superAdministratorAuthority": "actual-session-permanent-full-control",
  "viewAsMutationAuthority": false,
  "module026EditableConnectors": true,
  "microsoftSsoExpectedCallback": "$EXPECTED_CALLBACK",
  "microsoftSsoInternalAzureHostRejected": true,
  "expenseDrawer": "collapsed-non-invasive-explicit-project-selection",
  "expenseAcknowledgementRoles": ["PROJECT_MANAGEMENT", "PROJECT_MANAGEMENT_LEAD", "PROJECT_TEAM_COORDINATOR", "ACCOUNTING", "SUPER_ADMINISTRATOR"],
  "expensePassThroughTreatment": "ready-for-module-042-review",
  "expenseFixedPriceTreatment": "included-project-cost-no-separate-charge",
  "deletedAndSupersededExpensesExcluded": true,
  "staleExpenseReadinessBlocked": true,
  "module006DisplayName": "Toyota & Hyundai Pipeline",
  "module003StructuredUtilizationTable": true,
  "roleOrPolicyMutationPerformedByDeployment": false,
  "expenseAcknowledgementPerformedByDeployment": false,
  "invoiceMutationPerformedByDeployment": false,
  "credentialValuesChanged": false,
  "interactiveSsoCompletedByDeployment": false,
  "graphCallPerformedByDeployment": false,
  "emailSentByDeployment": false,
  "externalProviderCallsPerformedByDeployment": false,
  "imageRollbackOnFailure": "candidate-only",
  "smokeTests": "passed",
  "authenticatedFunctionalUat": "required"
}
JSON

echo "SUPERADMIN_SSO_EXPENSE_MODULE006_TEST_DEPLOYMENT=COMPLETE"
