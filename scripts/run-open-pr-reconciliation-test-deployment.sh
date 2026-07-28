#!/usr/bin/env bash
set -Eeuo pipefail

EXPECTED_RELEASE_COMMIT="e678cdaacc020ccd2ee7726d6e77f0276fae38ce"
CONTROL_ROOT="${1:-}"
RELEASE_ROOT="${2:-}"
TARGET_COMMIT="${3:-}"

RESOURCE_GROUP="${AZURE_RESOURCE_GROUP:-}"
API_APP="${AZURE_API_APP:-}"
WEB_APP="${AZURE_WEB_APP:-}"
ACR_NAME="${AZURE_ACR_NAME:-}"
PUBLIC_URL_VALUE="${PUBLIC_URL:-}"
DATABASE_URL="${PROJECTPULSE_TEST_DATABASE_URL:-}"
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
require_value PROJECTPULSE_TEST_DATABASE_URL "$DATABASE_URL"
[[ "$TARGET_COMMIT" == "$EXPECTED_RELEASE_COMMIT" ]] || fail "Unexpected source release: $TARGET_COMMIT"
[[ "$(git -C "$RELEASE_ROOT" rev-parse HEAD)" == "$EXPECTED_RELEASE_COMMIT" ]] || fail "Release checkout does not match the pinned source release."

BUILD_SCRIPT="$CONTROL_ROOT/scripts/build-pr55-acr-image.sh"
WAIT_SCRIPT="$CONTROL_ROOT/scripts/wait-containerapp-ready-revision.sh"
MIGRATION_RUNNER="$CONTROL_ROOT/scripts/run-open-pr-reconciliation-test-migration-job.sh"
MIGRATION_APPLIER="$CONTROL_ROOT/scripts/apply-open-pr-reconciliation-test-migration-049.sh"
MIGRATOR_DOCKERFILE="$CONTROL_ROOT/deployment/containers/pr55-migrator/Dockerfile"
for required in "$BUILD_SCRIPT" "$WAIT_SCRIPT" "$MIGRATION_RUNNER" "$MIGRATION_APPLIER" "$MIGRATOR_DOCKERFILE"; do
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
MIGRATION_IMAGE=""
MIGRATION_CONTEXT=""

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

restore_web() {
  [[ "$WEB_STARTED" == 1 && -n "$CURRENT_WEB_IMAGE" && -n "$WEB_IMAGE" ]] || return 0
  local active suffix revision
  active="$(az containerapp show -g "$RESOURCE_GROUP" -n "$WEB_APP" --query 'properties.template.containers[0].image' -o tsv 2>/dev/null)"
  if [[ "$active" == "$WEB_IMAGE" ]]; then
    suffix="oprrwebrb-${RUN_ID}-${RUN_ATTEMPT}"
    revision="$WEB_APP--$suffix"
    az containerapp update -g "$RESOURCE_GROUP" -n "$WEB_APP" --image "$CURRENT_WEB_IMAGE" --revision-suffix "$suffix" --output none
    bash "$WAIT_SCRIPT" "$RESOURCE_GROUP" "$WEB_APP" "$revision" "$CURRENT_WEB_IMAGE" 60 10
    echo "OPEN_PR_RECONCILIATION_WEB_ROLLBACK=COMPLETE"
  else
    echo "Web rollback skipped because another image is active: $active" >&2
  fi
}

restore_api() {
  [[ "$API_STARTED" == 1 && -n "$CURRENT_API_IMAGE" && -n "$API_IMAGE" ]] || return 0
  local active suffix revision
  active="$(az containerapp show -g "$RESOURCE_GROUP" -n "$API_APP" --query 'properties.template.containers[0].image' -o tsv 2>/dev/null)"
  if [[ "$active" == "$API_IMAGE" ]]; then
    suffix="oprrapirb-${RUN_ID}-${RUN_ATTEMPT}"
    revision="$API_APP--$suffix"
    az containerapp update -g "$RESOURCE_GROUP" -n "$API_APP" --image "$CURRENT_API_IMAGE" --revision-suffix "$suffix" --output none
    bash "$WAIT_SCRIPT" "$RESOURCE_GROUP" "$API_APP" "$revision" "$CURRENT_API_IMAGE" 60 10
    echo "OPEN_PR_RECONCILIATION_API_ROLLBACK=COMPLETE"
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
  [[ -z "$MIGRATION_CONTEXT" ]] || rm -rf "$MIGRATION_CONTEXT"
  unset DATABASE_URL PROJECTPULSE_TEST_DATABASE_URL
  exit "$status"
}
trap cleanup EXIT INT TERM

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
API_DIGEST="$("$BUILD_SCRIPT" "$ACR_NAME" "$API_REPOSITORY:open-pr-reconciliation-$TARGET_COMMIT" "$RELEASE_ROOT/deployment/containers/api/Dockerfile" "$RELEASE_ROOT")"
WEB_DIGEST="$("$BUILD_SCRIPT" "$ACR_NAME" "$WEB_REPOSITORY:open-pr-reconciliation-$TARGET_COMMIT" "$RELEASE_ROOT/deployment/containers/web/Dockerfile" "$RELEASE_ROOT")"
[[ "$API_DIGEST" == sha256:* && "$WEB_DIGEST" == sha256:* ]] || fail "Candidate image build did not return immutable digests."
API_IMAGE="$ACR_NAME.azurecr.io/$API_REPOSITORY@$API_DIGEST"
WEB_IMAGE="$ACR_NAME.azurecr.io/$WEB_REPOSITORY@$WEB_DIGEST"
echo "OPEN_PR_RECONCILIATION_API_IMAGE=$API_IMAGE"
echo "OPEN_PR_RECONCILIATION_WEB_IMAGE=$WEB_IMAGE"

MIGRATION_CONTEXT="$(mktemp -d "${RUNNER_TEMP:-/tmp}/open-pr-reconciliation-migrator.XXXXXX")"
install -d -m 0700 "$MIGRATION_CONTEXT/migrations"
install -m 0644 "$MIGRATOR_DOCKERFILE" "$MIGRATION_CONTEXT/Dockerfile"
install -m 0755 "$MIGRATION_APPLIER" "$MIGRATION_CONTEXT/apply-pr55-test-migrations.sh"
install -m 0644 "$RELEASE_ROOT/database/migrations/049_module_021_sell_customer_sync.sql" "$MIGRATION_CONTEXT/migrations/049_module_021_sell_customer_sync.sql"
(
  cd "$MIGRATION_CONTEXT/migrations"
  sha256sum 049_module_021_sell_customer_sync.sql > SHA256SUMS
)
printf '%s\n' "$TARGET_COMMIT" > "$MIGRATION_CONTEXT/release-commit"
MIGRATION_DIGEST="$("$BUILD_SCRIPT" "$ACR_NAME" "project-health-dashboard-open-pr-reconciliation-migrator:$TARGET_COMMIT-${WORKFLOW_SHA:0:12}" "$MIGRATION_CONTEXT/Dockerfile" "$MIGRATION_CONTEXT")"
[[ "$MIGRATION_DIGEST" == sha256:* ]] || fail "Migration image build did not return an immutable digest."
MIGRATION_IMAGE="$ACR_NAME.azurecr.io/project-health-dashboard-open-pr-reconciliation-migrator@$MIGRATION_DIGEST"
echo "OPEN_PR_RECONCILIATION_MIGRATION_IMAGE=$MIGRATION_IMAGE"

export AZURE_RESOURCE_GROUP="$RESOURCE_GROUP"
export AZURE_API_APP="$API_APP"
export AZURE_ACR_NAME="$ACR_NAME"
export PROJECTPULSE_TEST_DATABASE_URL="$DATABASE_URL"
export OPEN_PR_RECONCILIATION_MIGRATION_IMAGE="$MIGRATION_IMAGE"
export OPEN_PR_RECONCILIATION_MIGRATION_JOB_NAME="opr049-${RUN_ID}-${RUN_ATTEMPT}"
bash "$MIGRATION_RUNNER"
echo "OPEN_PR_RECONCILIATION_MIGRATION_049=APPLIED_OR_VERIFIED"

API_SUFFIX="oprrapi-${RUN_ID}-${RUN_ATTEMPT}"
API_REVISION="$API_APP--$API_SUFFIX"
API_STARTED=1
az containerapp update -g "$RESOURCE_GROUP" -n "$API_APP" --image "$API_IMAGE" --revision-suffix "$API_SUFFIX" --output none
bash "$WAIT_SCRIPT" "$RESOURCE_GROUP" "$API_APP" "$API_REVISION" "$API_IMAGE" 60 10

API_PROBE_DIR="${RUNNER_TEMP:-/tmp}/open-pr-reconciliation-api"
mkdir -p "$API_PROBE_DIR"
probe_api() {
  local name="$1" method="$2" path="$3" expected="$4" body="${5:-}" status='' type=''
  for attempt in $(seq 1 36); do
    local separator='?'; [[ "$path" == *\?* ]] && separator='&'
    local url="$BASE_URL$path${separator}release_check=oprr-${RUN_ID}-${RUN_ATTEMPT}-${attempt}"
    local args=(-sS -L -X "$method" -D "$API_PROBE_DIR/$name.headers" -o "$API_PROBE_DIR/$name.body" -w '%{http_code}' -H 'Cache-Control: no-cache, no-store' -H 'Pragma: no-cache')
    if [[ -n "$body" ]]; then args+=(-H 'Content-Type: application/json' --data "$body"); fi
    status="$(curl "${args[@]}" "$url" || true)"
    type="$(awk 'BEGIN{IGNORECASE=1} /^content-type:/ {gsub(/\r/, ""); print $2}' "$API_PROBE_DIR/$name.headers" 2>/dev/null | tail -1)"
    if [[ ",$expected," == *",$status,"* && "$type" == application/json* ]]; then
      jq -e 'type == "object"' "$API_PROBE_DIR/$name.body" >/dev/null
      return 0
    fi
    sleep 10
  done
  echo "$path did not reach expected status $expected with JSON." >&2
  cat "$API_PROBE_DIR/$name.body" >&2 || true
  return 1
}

probe_api health GET '/health' '200'
probe_api version GET '/api/version' '200'
probe_api sell-status GET '/api/customers/sell/status' '401'
probe_api sell-preview POST '/api/customers/sell/preview' '401' '{}'
probe_api sell-import POST '/api/customers/sell/import' '401' '{}'
probe_api sell-runs GET '/api/customers/sell/runs' '401'
probe_api role-summary GET '/api/runtime/v2/role-policy/summary' '401'
probe_api audit-history GET '/api/admin/audit-history/events' '401'
probe_api module065-sso POST '/api/microsoft-integration/sso-apply-profile' '401' '{}'
probe_api module065-services POST '/api/microsoft-integration/services-apply-profile' '401' '{}'
probe_api module065-mail-test POST '/api/microsoft-integration/mail-runtime/test' '401,403' '{}'
probe_api platform-overview GET '/api/platform-operations/overview' '401'
probe_api platform-apis GET '/api/platform-operations/apis' '401'
probe_api platform-evidence GET '/api/platform-operations/evidence' '401'
probe_api platform-architecture GET '/api/platform-operations/architecture' '401'

READY_API="$(az containerapp show -g "$RESOURCE_GROUP" -n "$API_APP" --query 'properties.latestReadyRevisionName' -o tsv --only-show-errors)"
[[ "$READY_API" == "$API_REVISION" ]] || fail "Unexpected ready API revision: $READY_API"
ACTIVE_API_IMAGE="$(az containerapp revision show -g "$RESOURCE_GROUP" -n "$API_APP" --revision "$READY_API" --query 'properties.template.containers[0].image' -o tsv --only-show-errors)"
[[ "$ACTIVE_API_IMAGE" == "$API_IMAGE" ]] || fail "Unexpected active API image."
echo "OPEN_PR_RECONCILIATION_API_VALIDATION=PASS"

WEB_SUFFIX="oprrweb-${RUN_ID}-${RUN_ATTEMPT}"
WEB_REVISION="$WEB_APP--$WEB_SUFFIX"
WEB_STARTED=1
az containerapp update -g "$RESOURCE_GROUP" -n "$WEB_APP" --image "$WEB_IMAGE" --revision-suffix "$WEB_SUFFIX" --output none
bash "$WAIT_SCRIPT" "$RESOURCE_GROUP" "$WEB_APP" "$WEB_REVISION" "$WEB_IMAGE" 60 10

SERVED_READY=false
for attempt in $(seq 1 36); do
  BUSTER="oprr-${RUN_ID}-${RUN_ATTEMPT}-${attempt}"
  HTML_STATUS="$(curl -sS -L -H 'Cache-Control: no-cache, no-store' -o /tmp/oprr-index.html -w '%{http_code}' "$BASE_URL/?release_check=$BUSTER" || true)"
  if [[ "$HTML_STATUS" == 200 ]]; then
    JS_PATH="$(sed -nE 's/.*src="([^"]+\.js)".*/\1/p' /tmp/oprr-index.html | head -1)"
    CSS_PATH="$(sed -nE 's/.*href="([^"]+\.css)".*/\1/p' /tmp/oprr-index.html | head -1)"
    [[ "$JS_PATH" == http* ]] && JS_URL="$JS_PATH" || JS_URL="$BASE_URL/${JS_PATH#/}"
    [[ "$CSS_PATH" == http* ]] && CSS_URL="$CSS_PATH" || CSS_URL="$BASE_URL/${CSS_PATH#/}"
    JS_STATUS="$(curl -sS -L -H 'Cache-Control: no-cache, no-store' -o /tmp/oprr-app.js -w '%{http_code}' "$JS_URL?release_check=$BUSTER" || true)"
    CSS_STATUS="$(curl -sS -L -H 'Cache-Control: no-cache, no-store' -o /tmp/oprr-app.css -w '%{http_code}' "$CSS_URL?release_check=$BUSTER" || true)"
    if [[ "$JS_STATUS" == 200 && "$CSS_STATUS" == 200 ]] \
      && grep -Fq 'Search module number or page name' /tmp/oprr-app.js \
      && grep -Fq 'X-ProjectPulse-View-As-User' /tmp/oprr-app.js \
      && grep -Fq 'Project creation and project/task management moved to Modules 055D and 055C.' /tmp/oprr-app.js \
      && grep -Fq '/api/customers/sell/status' /tmp/oprr-app.js \
      && grep -Fq '/api/customers/sell/preview' /tmp/oprr-app.js \
      && grep -Fq '/api/admin/audit-history/events' /tmp/oprr-app.js \
      && grep -Fq 'module_065_services_profile_not_active' /tmp/oprr-app.js \
      && grep -Fq '/api/platform-operations/overview' /tmp/oprr-app.js \
      && grep -Fq 'System Health & API Diagnostics' /tmp/oprr-app.js \
      && grep -Fq '/api/platform-operations/evidence' /tmp/oprr-app.js \
      && grep -Fq 'Operational Evidence & Diagnostic History' /tmp/oprr-app.js \
      && grep -Fq '/api/platform-operations/architecture' /tmp/oprr-app.js \
      && grep -Fq 'System Architecture & API Dependency Map' /tmp/oprr-app.js \
      && grep -Fq 'projectpulse-more-menu-tools' /tmp/oprr-app.css; then
      SERVED_READY=true
      break
    fi
  fi
  sleep 10
done
[[ "$SERVED_READY" == true ]] || fail "The served web bundle did not expose all reconciled release markers."

READY_WEB="$(az containerapp show -g "$RESOURCE_GROUP" -n "$WEB_APP" --query 'properties.latestReadyRevisionName' -o tsv --only-show-errors)"
[[ "$READY_WEB" == "$WEB_REVISION" ]] || fail "Unexpected ready web revision: $READY_WEB"
ACTIVE_WEB_IMAGE="$(az containerapp revision show -g "$RESOURCE_GROUP" -n "$WEB_APP" --revision "$READY_WEB" --query 'properties.template.containers[0].image' -o tsv --only-show-errors)"
[[ "$ACTIVE_WEB_IMAGE" == "$WEB_IMAGE" ]] || fail "Unexpected active web image."
echo "OPEN_PR_RECONCILIATION_WEB_VALIDATION=PASS"

mkdir -p "$CONTROL_ROOT/evidence"
cat > "$CONTROL_ROOT/evidence/open-pr-reconciliation-test-deployment.json" <<JSON
{
  "environment": "test",
  "releaseCommit": "$TARGET_COMMIT",
  "workflowCommit": "$WORKFLOW_SHA",
  "runId": "$RUN_ID",
  "apiImage": "$API_IMAGE",
  "webImage": "$WEB_IMAGE",
  "migrationImage": "$MIGRATION_IMAGE",
  "previousApiImage": "$CURRENT_API_IMAGE",
  "previousWebImage": "$CURRENT_WEB_IMAGE",
  "apiRevision": "$READY_API",
  "webRevision": "$READY_WEB",
  "migration049": "applied-or-verified",
  "migrationRuntimeRole": "configured-api-database-identity",
  "optionalPtpAppRole": "verified-when-present-not-required",
  "operationalRows": "preserved-by-guard",
  "providerNeutralPlatformOperations": true,
  "modules013016068": "served-and-protected",
  "sellExternalApiCalledByDeployment": false,
  "providerCredentialsChanged": false,
  "module011DataDeleted": false,
  "rollbackImageResolution": "fail-closed",
  "imageRollbackOnFailure": "candidate-only",
  "migrationRollbackOnFailure": "not-automatic-additive-schema-remains",
  "smokeTests": "passed"
}
JSON

echo "OPEN_PR_RECONCILIATION_TEST_DEPLOYMENT=COMPLETE"
