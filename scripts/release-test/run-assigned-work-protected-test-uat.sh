#!/usr/bin/env bash
set -Eeuo pipefail

fail() {
  echo "ERROR: $*" >&2
  exit 1
}

: "${BASE:?BASE is required}"
: "${TEST_LOGIN_PASSWORD:?TEST_LOGIN_PASSWORD is required}"
: "${EVIDENCE_DIR:?EVIDENCE_DIR is required}"

BASE="${BASE%/}"
[[ "$BASE" == "https://phd-west-test.onenecklab.com" ]] \
  || fail "Assigned-work UAT is restricted to protected Test."
[[ ${#TEST_LOGIN_PASSWORD} -ge 12 ]] \
  || fail "The protected-Test login credential is unavailable."

install -d -m 0700 "$EVIDENCE_DIR"
umask 077

JASON_EMAIL='jason.mosier@ussignal.local'
WEEK_START='2026-08-16'
WEEK_END='2026-08-22'
REPORTED_SERVICE_REQUEST='SR-8C81ACA3'

LOGIN_PAYLOAD="$(mktemp)"
LOGIN_RESPONSE="$(mktemp)"
chmod 0600 "$LOGIN_PAYLOAD" "$LOGIN_RESPONSE"
cleanup() {
  rm -f "$LOGIN_PAYLOAD" "$LOGIN_RESPONSE"
}
trap cleanup EXIT

jq -n \
  --arg username "$JASON_EMAIL" \
  --arg password "$TEST_LOGIN_PASSWORD" \
  '{username:$username,password:$password}' > "$LOGIN_PAYLOAD"

LOGIN_STATUS="$(curl -sS --max-time 90 \
  -o "$LOGIN_RESPONSE" -w '%{http_code}' \
  -H 'Cache-Control: no-cache, no-store, max-age=0' \
  -H 'Content-Type: application/json' \
  -H "Origin: $BASE" \
  -H 'Sec-Fetch-Site: same-origin' \
  --data-binary @"$LOGIN_PAYLOAD" \
  "$BASE/api/auth/local/login" || true)"
[[ "$LOGIN_STATUS" == 200 ]] \
  || fail "Jason protected-Test login returned HTTP $LOGIN_STATUS."

jq -e '
  .provider == "LOCAL"
  and .mustChangePassword == false
  and (.sessionToken | type == "string" and length > 0)
' "$LOGIN_RESPONSE" >/dev/null \
  || fail "Jason login response did not satisfy the authenticated session contract."

SESSION_TOKEN="$(jq -r '.sessionToken' "$LOGIN_RESPONSE")"
echo "::add-mask::$SESSION_TOKEN"
jq 'del(.sessionToken,.token,.password)' "$LOGIN_RESPONSE" \
  > "$EVIDENCE_DIR/jason-assigned-work-login-redacted.json"
rm -f "$LOGIN_PAYLOAD" "$LOGIN_RESPONSE"
trap - EXIT

AUTH_HEADERS=(
  -H 'Cache-Control: no-cache, no-store, max-age=0'
  -H "Authorization: Bearer $SESSION_TOKEN"
  -H "X-ProjectPulse-Session: $SESSION_TOKEN"
  -H "Origin: $BASE"
  -H 'Sec-Fetch-Site: same-origin'
)

auth_get() {
  local module_number="$1" path="$2" output="$3" label="$4"
  local status
  status="$(curl -sS --http1.1 --connect-timeout 20 --max-time 120 \
    -o "$output" -w '%{http_code}' \
    "${AUTH_HEADERS[@]}" \
    -H "X-ProjectPulse-Module-Number: $module_number" \
    "$BASE$path" || true)"
  [[ "$status" == 200 ]] || fail "$label returned HTTP $status."
  [[ -s "$output" ]] || fail "$label returned HTTP 200 with an empty response body."
  jq -e . "$output" >/dev/null || fail "$label did not return JSON."
}

SECURITY="$EVIDENCE_DIR/jason-assigned-work-security-context.json"
AVAILABLE="$EVIDENCE_DIR/jason-available-tasks-$WEEK_START.json"
CANONICAL_QUEUE="$EVIDENCE_DIR/jason-canonical-work-queue-$WEEK_START.json"
CLOSEOUT="$EVIDENCE_DIR/jason-engineer-closeout.json"
WORKSPACE="$EVIDENCE_DIR/jason-engineering-workspace.json"
OWNERS="$EVIDENCE_DIR/jason-module-owner-catalog.json"

auth_get '001' '/api/security/context' "$SECURITY" 'Jason security context'
auth_get '001' "/api/assignments/available-tasks?weekStart=$WEEK_START" "$AVAILABLE" 'Module 001 available tasks'
auth_get '001' "/api/timesheet/work-queue?weekStart=$WEEK_START" "$CANONICAL_QUEUE" 'Module 001 canonical work queue'
auth_get '001A' '/api/engineer-task-closeout/overview' "$CLOSEOUT" 'Module 001A Engineer closeout'
auth_get '019' '/api/project-workspace/overview' "$WORKSPACE" 'Module 019 Engineering Workspace'
auth_get '012' '/api/module-catalog/owners' "$OWNERS" 'Module owner catalog read-through'

JASON_USER_ID="$(jq -r '.userId // empty' "$SECURITY")"
[[ "$JASON_USER_ID" =~ ^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$ ]] \
  || fail "Jason security context did not expose a valid user ID."

jq -e \
  --arg weekStart "$WEEK_START" \
  --arg weekEnd "$WEEK_END" \
  --arg sr "$REPORTED_SERVICE_REQUEST" '
    .weekStart == $weekStart
    and .weekEnd == $weekEnd
    and .authoritativeSource == "project_assignments"
    and .activityClassification == "durable_project_code_and_work_type"
    and ([.tasks[]? | select(
      .projectCode == $sr
      and .timeEntrySection == "requests"
      and .rowType == "service_request"
      and .requestNumber == $sr
      and .serviceRequestNumber == $sr
    )] | length) >= 1
    and ([.tasks[]?
      | select((.projectCode // "") | test("^(SR|PRES|INT)-"; "i"))
      | select(.timeEntrySection != "requests")
    ] | length) == 0
  ' "$AVAILABLE" >/dev/null \
  || fail "Module 001 available tasks did not expose the reported Service Request in the Sunday-through-Saturday request queue."

jq -e \
  --arg sr "$REPORTED_SERVICE_REQUEST" \
  --arg userId "$JASON_USER_ID" '
    .authoritativeSource == "project_assignments"
    and ([.tasks[]? | select(
      .projectCode == $sr
      and .assignedEngineerId == $userId
      and .taskStatus == "active"
    )] | length) >= 1
  ' "$CANONICAL_QUEUE" >/dev/null \
  || fail "Module 001 canonical work queue does not contain the reported Service Request for Jason."

jq -e \
  --arg sr "$REPORTED_SERVICE_REQUEST" \
  --arg userId "$JASON_USER_ID" '
    .module == "001A"
    and .status == "engineer_task_closeout_loaded"
    and .eligibilityContract == "MODULE001A_REQUEST_FAMILY_ONLY_V3"
    and .workflow.identifierAuthority == "durable_project_code_prefix"
    and .workflow.projectTasksExcluded == true
    and .access.effectiveUserId == $userId
    and ([.active[]?, .history[]?
      | select((((.projectCode // "") | test("^(SR|PRES|INT)-"; "i")) | not))
    ] | length) == 0
    and ([.active[]?, .history[]? | select(
      .projectCode == $sr
      and .engineerUserId == $userId
      and .requestType == "Service Request"
      and .serviceRequestNumber == $sr
      and .canClose == true
    )] | length) >= 1
  ' "$CLOSEOUT" >/dev/null \
  || fail "Module 001A did not return the reported Service Request for Jason."

jq -e '
    .access.canManage == false
    and .access.authoritySource == "authenticated_read_only"
    and (.ownerCandidates | length) == 0
    and ([.owners[]? | select(
      (.moduleNumber == "001" or .moduleNumber == "001A" or .moduleNumber == "019")
      and .ownerUserId != null
      and (.displayName // "") != ""
      and .displayName != "Unassigned"
    )] | length) == 3
  ' "$OWNERS" >/dev/null \
  || fail "Ordinary authenticated users did not receive the saved Super Administrator module owner display state."

jq -e \
  --arg sr "$REPORTED_SERVICE_REQUEST" \
  --arg userId "$JASON_USER_ID" '
    .module == "019"
    and .access.userId == $userId
    and ([.projects[]? | select(.projectCode == $sr)] | length) >= 1
    and ([.assignments[]? | select(
      .projectCode == $sr
      and (.engineerEmail // "" | ascii_downcase) == "jason.mosier@ussignal.local"
    )] | length) >= 1
  ' "$WORKSPACE" >/dev/null \
  || fail "Module 019 did not return the reported Service Request project and assignment for Jason."

LOGOUT_STATUS="$(curl -sS --max-time 60 \
  -o "$EVIDENCE_DIR/jason-assigned-work-logout.json" -w '%{http_code}' \
  -X POST \
  "${AUTH_HEADERS[@]}" \
  -H 'X-ProjectPulse-Module-Number: 001' \
  "$BASE/api/auth/session/logout" || true)"
[[ "$LOGOUT_STATUS" == 200 ]] \
  || fail "Jason logout returned HTTP $LOGOUT_STATUS."

jq -n \
  --arg identity "$JASON_EMAIL" \
  --arg userId "$JASON_USER_ID" \
  --arg serviceRequest "$REPORTED_SERVICE_REQUEST" \
  --arg weekStart "$WEEK_START" \
  --arg weekEnd "$WEEK_END" \
  '{
    status:"passed",
    environment:"protected-test",
    identity:$identity,
    effectiveUserId:$userId,
    serviceRequest:$serviceRequest,
    weekStart:$weekStart,
    weekEnd:$weekEnd,
    module001AvailableTasks:true,
    module001CanonicalWorkQueue:true,
    module001aEngineerCloseout:true,
    module001aVisibleRequestReference:true,
    module001aRequestFamilyOnly:true,
    module001aProjectTasksExcluded:true,
    module019EngineeringWorkspace:true,
    moduleOwnerCatalogReadThrough:true,
    ownerCandidatesRestrictedToSuperAdministrators:true,
    requestFamilies:["Service Request","Pre-Sales","Internal"],
    mutation:false,
    productionMutation:false
  }' > "$EVIDENCE_DIR/assigned-work-protected-test-uat.json"

unset SESSION_TOKEN

echo 'ASSIGNED_WORK_PROTECTED_TEST_UAT=PASS identity=jason.mosier@ussignal.local serviceRequest=SR-8C81ACA3 modules=001,001A,019 module001aRequestFamilyOnly=true projectTasksExcluded=true week=2026-08-16..2026-08-22'

# Module 001B live submitted-time reallocation proof.
# This runs inside the existing manifest-approved Protected-Test deployment controller,
# so no parallel workflow receives Azure mutation authority. The disposable fixture
# surface is enabled only around this proof and is forced closed again on every exit.
EXPECTED_RELEASE_SHA="${RELIABILITY_RELEASE_COMMIT:-${GITHUB_SHA:-}}"
[[ "$EXPECTED_RELEASE_SHA" =~ ^[0-9a-f]{40}$ ]] \
  || fail "Module 001B UAT requires the exact protected-Test release SHA."
command -v az >/dev/null 2>&1 || fail "Azure CLI is unavailable inside the governed Test controller."

MODULE001B_SESSION=''
MODULE001B_ENTRY_ID=''
MODULE001B_API_APP=''
MODULE001B_API_RG=''
MODULE001B_GATE_ENABLED=false

module001b_request() {
  local method="$1" path="$2" output="$3" input="${4:-}"
  local args=(
    -sS --http1.1 --connect-timeout 30 --max-time 120
    -X "$method"
    -o "$output" -w '%{http_code}'
    -H 'Cache-Control: no-cache, no-store, max-age=0'
    -H "Authorization: Bearer $MODULE001B_SESSION"
    -H "X-ProjectPulse-Session: $MODULE001B_SESSION"
    -H 'X-ProjectPulse-Module-Number: 001B'
    -H "Origin: $BASE"
    -H 'Sec-Fetch-Site: same-origin'
  )
  if [[ -n "$input" ]]; then
    args+=( -H 'Content-Type: application/json' --data-binary @"$input" )
  fi
  curl "${args[@]}" "$BASE$path" || true
}

module001b_wait_healthy() {
  local label="$1" ready=false status attempt
  for attempt in $(seq 1 36); do
    status="$(curl -sS --http1.1 --connect-timeout 20 --max-time 30 \
      -o "$EVIDENCE_DIR/module001b-health-$label.json" -w '%{http_code}' \
      -H 'Cache-Control: no-cache, no-store, max-age=0' \
      "$BASE/health?module001b-live-uat=${GITHUB_RUN_ID:-local}-$label-$attempt" || true)"
    if [[ "$status" == 200 ]] \
      && jq -e '.status == "healthy"' "$EVIDENCE_DIR/module001b-health-$label.json" >/dev/null 2>&1; then
      ready=true
      break
    fi
    sleep 5
  done
  [[ "$ready" == true ]] || fail "Protected Test did not become healthy during Module 001B $label."
}

module001b_disable_gate() {
  if [[ "$MODULE001B_GATE_ENABLED" != true || -z "$MODULE001B_API_RG" || -z "$MODULE001B_API_APP" ]]; then
    return 0
  fi
  set +e
  az containerapp update \
    -g "$MODULE001B_API_RG" \
    -n "$MODULE001B_API_APP" \
    --set-env-vars PROJECTPULSE_MODULE001B_PROTECTED_TEST_UAT_ENABLED=false \
    --revision-suffix "m1bd-${GITHUB_RUN_ID:-0}-${GITHUB_RUN_ATTEMPT:-1}" \
    --output none --only-show-errors
  local disable_rc=$?
  set -e
  MODULE001B_GATE_ENABLED=false
  [[ $disable_rc -eq 0 ]] || return "$disable_rc"

  local after source_after flag_after
  after="$(az containerapp show -g "$MODULE001B_API_RG" -n "$MODULE001B_API_APP" -o json --only-show-errors)" || return $?
  source_after="$(jq -r '[.properties.template.containers[0].env[]? | select(.name == "PROJECTPULSE_SOURCE_COMMIT") | .value // empty] | if length == 1 then .[0] else empty end' <<<"$after")"
  flag_after="$(jq -r '[.properties.template.containers[0].env[]? | select(.name == "PROJECTPULSE_MODULE001B_PROTECTED_TEST_UAT_ENABLED") | .value // empty] | if length == 1 then .[0] else empty end' <<<"$after")"
  [[ "$source_after" == "$EXPECTED_RELEASE_SHA" && "$flag_after" == false ]]
}

module001b_cleanup() {
  local original_rc=$?
  set +e
  if [[ -n "$MODULE001B_SESSION" && -n "$MODULE001B_ENTRY_ID" ]]; then
    module001b_request DELETE \
      "/api/runtime/timesheet/steward/001b/protected-test-uat/fixture/$MODULE001B_ENTRY_ID" \
      "$EVIDENCE_DIR/module001b-cleanup-on-exit.json" >/dev/null
  fi
  module001b_disable_gate
  local gate_rc=$?
  set -e
  if [[ $original_rc -eq 0 && $gate_rc -ne 0 ]]; then
    echo "ERROR: Failed to force the Module 001B Protected-Test fixture surface closed." >&2
    return "$gate_rc"
  fi
  return "$original_rc"
}
trap module001b_cleanup EXIT

# Discover the exact Test API app from the already authenticated Azure Test subscription.
# Require a single app tagged Test, pinned to this exact release, and carrying the API-only
# Celar public-fact runtime marker. No repository secret or guessed resource name is used.
mapfile -t MODULE001B_CANDIDATES < <(az containerapp list \
  --query "[?tags.environment=='test'].[resourceGroup,name]" \
  -o tsv --only-show-errors)
[[ ${#MODULE001B_CANDIDATES[@]} -gt 0 ]] \
  || fail "No Azure Container App tagged as protected Test was found."

MODULE001B_MATCHES=()
for candidate in "${MODULE001B_CANDIDATES[@]}"; do
  candidate_rg="$(awk '{print $1}' <<<"$candidate")"
  candidate_name="$(awk '{print $2}' <<<"$candidate")"
  [[ -n "$candidate_rg" && -n "$candidate_name" ]] || continue
  candidate_json="$(az containerapp show -g "$candidate_rg" -n "$candidate_name" -o json --only-show-errors)" || continue
  candidate_source="$(jq -r '[.properties.template.containers[0].env[]? | select(.name == "PROJECTPULSE_SOURCE_COMMIT") | .value // empty] | if length == 1 then .[0] else empty end' <<<"$candidate_json")"
  candidate_api_marker="$(jq -r '[.properties.template.containers[0].env[]? | select(.name == "PROJECTPULSE_CELAR_AI_CURRENT_PUBLIC_FACTS_ENABLED") | .value // empty] | if length == 1 then .[0] else empty end' <<<"$candidate_json")"
  if [[ "$candidate_source" == "$EXPECTED_RELEASE_SHA" && "$candidate_api_marker" == true ]]; then
    MODULE001B_MATCHES+=("$candidate_rg"$'\t'"$candidate_name")
  fi
done
[[ ${#MODULE001B_MATCHES[@]} -eq 1 ]] \
  || fail "Expected exactly one protected-Test API app pinned to $EXPECTED_RELEASE_SHA; found ${#MODULE001B_MATCHES[@]}."
MODULE001B_API_RG="$(cut -f1 <<<"${MODULE001B_MATCHES[0]}")"
MODULE001B_API_APP="$(cut -f2 <<<"${MODULE001B_MATCHES[0]}")"

API_BEFORE="$(az containerapp show -g "$MODULE001B_API_RG" -n "$MODULE001B_API_APP" -o json --only-show-errors)"
[[ "$(jq -r '.tags.environment // empty' <<<"$API_BEFORE")" == test ]] \
  || fail "Module 001B UAT resolved a container app that is not tagged Test."
[[ "$(jq -r '[.properties.template.containers[0].env[]? | select(.name == "PROJECTPULSE_SOURCE_COMMIT") | .value // empty] | if length == 1 then .[0] else empty end' <<<"$API_BEFORE")" == "$EXPECTED_RELEASE_SHA" ]] \
  || fail "Module 001B UAT resolved a Test API app on the wrong source SHA."

echo 'MODULE001B_PROTECTED_TEST_FIXTURE_SURFACE=ENABLING'
az containerapp update \
  -g "$MODULE001B_API_RG" \
  -n "$MODULE001B_API_APP" \
  --set-env-vars PROJECTPULSE_MODULE001B_PROTECTED_TEST_UAT_ENABLED=true \
  --revision-suffix "m1be-${GITHUB_RUN_ID:-0}-${GITHUB_RUN_ATTEMPT:-1}" \
  --output none --only-show-errors
MODULE001B_GATE_ENABLED=true

API_ENABLED="$(az containerapp show -g "$MODULE001B_API_RG" -n "$MODULE001B_API_APP" -o json --only-show-errors)"
[[ "$(jq -r '[.properties.template.containers[0].env[]? | select(.name == "PROJECTPULSE_SOURCE_COMMIT") | .value // empty] | if length == 1 then .[0] else empty end' <<<"$API_ENABLED")" == "$EXPECTED_RELEASE_SHA" ]] \
  || fail "Protected Test source marker changed while enabling Module 001B fixture access."
[[ "$(jq -r '[.properties.template.containers[0].env[]? | select(.name == "PROJECTPULSE_MODULE001B_PROTECTED_TEST_UAT_ENABLED") | .value // empty] | if length == 1 then .[0] else empty end' <<<"$API_ENABLED")" == true ]] \
  || fail "Module 001B Protected-Test fixture surface did not enable."
module001b_wait_healthy enabled

PTC_LOGIN_PAYLOAD="$(mktemp)"
PTC_LOGIN_RESPONSE="$(mktemp)"
chmod 0600 "$PTC_LOGIN_PAYLOAD" "$PTC_LOGIN_RESPONSE"
jq -n \
  --arg username 'project.team.coordinator@ussignal.local' \
  --arg password "$TEST_LOGIN_PASSWORD" \
  '{username:$username,password:$password}' > "$PTC_LOGIN_PAYLOAD"
PTC_LOGIN_STATUS="$(curl -sS --http1.1 --connect-timeout 30 --max-time 90 \
  -o "$PTC_LOGIN_RESPONSE" -w '%{http_code}' \
  -H 'Cache-Control: no-cache, no-store, max-age=0' \
  -H 'Content-Type: application/json' \
  -H "Origin: $BASE" \
  -H 'Sec-Fetch-Site: same-origin' \
  --data-binary @"$PTC_LOGIN_PAYLOAD" \
  "$BASE/api/auth/local/login" || true)"
rm -f "$PTC_LOGIN_PAYLOAD"
[[ "$PTC_LOGIN_STATUS" == 200 ]] || fail "Coordinator login returned HTTP $PTC_LOGIN_STATUS."
jq -e '.provider == "LOCAL" and .mustChangePassword == false and (.sessionToken | type == "string" and length > 0)' \
  "$PTC_LOGIN_RESPONSE" >/dev/null || fail "Coordinator login did not satisfy the authenticated session contract."
MODULE001B_SESSION="$(jq -r '.sessionToken' "$PTC_LOGIN_RESPONSE")"
echo "::add-mask::$MODULE001B_SESSION"
jq 'del(.sessionToken,.token,.password)' "$PTC_LOGIN_RESPONSE" > "$EVIDENCE_DIR/module001b-coordinator-login-redacted.json"
rm -f "$PTC_LOGIN_RESPONSE"

CAPABILITY_STATUS="$(module001b_request GET \
  '/api/runtime/timesheet/steward/001b/reallocation/capabilities' \
  "$EVIDENCE_DIR/module001b-capabilities.json")"
[[ "$CAPABILITY_STATUS" == 200 ]] || fail "Module 001B capabilities returned HTTP $CAPABILITY_STATUS."
jq -e '
  .module == "001B"
  and .allocationOnly == true
  and .submissionStatePreserved == true
  and .unsubmitRequired == false
  and .workerResubmissionRequired == false
  and .managerApprovalRequired == false
  and .projectManagerApprovalRequired == false
' "$EVIDENCE_DIR/module001b-capabilities.json" >/dev/null \
  || fail "Module 001B capabilities do not preserve the approved reallocation contract."

# Azure Container Apps remains deliberately in single-revision mode, but while a new
# revision is starting the old healthy revision can continue serving for a short period.
# The prior controller treated that old-revision 404 as an application failure. Retry only
# the fail-closed route-unavailable response (and bounded gateway handoff statuses) until
# the enabled revision actually serves the disposable fixture endpoint.
FIXTURE_STATUS=''
for attempt in $(seq 1 36); do
  : > "$EVIDENCE_DIR/module001b-fixture.json"
  FIXTURE_STATUS="$(module001b_request POST \
    '/api/runtime/timesheet/steward/001b/protected-test-uat/fixture' \
    "$EVIDENCE_DIR/module001b-fixture.json")"
  if [[ "$FIXTURE_STATUS" == 201 ]]; then
    echo "MODULE001B_ENABLED_REVISION=SERVING attempt=$attempt" >&2
    break
  fi

  if [[ "$FIXTURE_STATUS" == 404 ]] \
    && jq -e '.status == "protected_test_uat_route_unavailable"' \
      "$EVIDENCE_DIR/module001b-fixture.json" >/dev/null 2>&1; then
    echo "MODULE001B_ENABLED_REVISION=PENDING attempt=$attempt status=$FIXTURE_STATUS" >&2
    sleep 5
    continue
  fi

  case "$FIXTURE_STATUS" in
    000|502|503|504)
      echo "MODULE001B_ENABLED_REVISION=TRANSIENT attempt=$attempt status=$FIXTURE_STATUS" >&2
      sleep 5
      ;;
    *)
      break
      ;;
  esac
done
[[ "$FIXTURE_STATUS" == 201 ]] || fail "Protected-Test Module 001B fixture creation returned HTTP $FIXTURE_STATUS."
jq -e --arg release "$EXPECTED_RELEASE_SHA" '
  .status == "protected_test_fixture_ready"
  and .module == "001B"
  and .sourceCommit == $release
  and .targetEmail == "demo.engineer@ussignal.local"
  and .weekStart == "2099-12-27"
  and .workDate == "2099-12-28"
  and .expectedHours == 1.25
  and .expectedStatus == "submitted"
  and .destinationType == "non_project"
  and .disposable == true
' "$EVIDENCE_DIR/module001b-fixture.json" >/dev/null \
  || fail "Protected-Test Module 001B fixture violated the isolated submitted-entry contract."

MODULE001B_TARGET_USER_ID="$(jq -r '.targetUserId' "$EVIDENCE_DIR/module001b-fixture.json")"
MODULE001B_ENTRY_ID="$(jq -r '.timeEntryId' "$EVIDENCE_DIR/module001b-fixture.json")"
MODULE001B_CATEGORY_ID="$(jq -r '.nonProjectTimeCategoryId' "$EVIDENCE_DIR/module001b-fixture.json")"
[[ "$MODULE001B_TARGET_USER_ID" =~ ^[0-9a-fA-F-]{36}$ ]] || fail "Module 001B fixture target user ID is invalid."
[[ "$MODULE001B_ENTRY_ID" =~ ^[0-9a-fA-F-]{36}$ ]] || fail "Module 001B fixture time-entry ID is invalid."
[[ "$MODULE001B_CATEGORY_ID" =~ ^[0-9a-fA-F-]{36}$ ]] || fail "Module 001B fixture destination category ID is invalid."

MOVE_PAYLOAD="$(mktemp)"
chmod 0600 "$MOVE_PAYLOAD"
jq -n \
  --arg targetUserId "$MODULE001B_TARGET_USER_ID" \
  --arg categoryId "$MODULE001B_CATEGORY_ID" \
  '{
    targetUserId:$targetUserId,
    destinationType:"non_project",
    assignmentId:null,
    projectId:null,
    taskId:null,
    nonProjectTimeCategoryId:$categoryId,
    reason:"Protected Test synthetic Module 001B submitted-time reallocation UAT."
  }' > "$MOVE_PAYLOAD"
MOVE_STATUS="$(module001b_request POST \
  "/api/runtime/timesheet/steward/001b/reallocation/entries/$MODULE001B_ENTRY_ID/move" \
  "$EVIDENCE_DIR/module001b-move.json" "$MOVE_PAYLOAD")"
rm -f "$MOVE_PAYLOAD"
[[ "$MOVE_STATUS" == 200 ]] || fail "Live Module 001B /move returned HTTP $MOVE_STATUS."
jq -e \
  --arg entryId "$MODULE001B_ENTRY_ID" \
  --arg targetUserId "$MODULE001B_TARGET_USER_ID" \
  --arg categoryId "$MODULE001B_CATEGORY_ID" '
    .status == "reallocated"
    and .module == "001B"
    and .apiContractVersion == "module001b-time-reallocation-v1-2026-08-28"
    and .entry.timeEntryId == $entryId
    and .entry.userId == $targetUserId
    and .entry.workDate == "2099-12-28"
    and .entry.hours == 1.25
    and .entry.status == "submitted"
    and .entry.nonProjectTimeCategoryId == $categoryId
    and .previousStatus == "submitted"
    and .currentStatus == "submitted"
    and .submissionStatePreserved == true
    and .userMustResubmit == false
    and .managerApprovalRequired == false
    and .projectManagerApprovalRequired == false
    and .invariants.workerUnchanged == true
    and .invariants.workDateUnchanged == true
    and .invariants.workedTimeUnchanged == true
    and .invariants.statusUnchanged == true
  ' "$EVIDENCE_DIR/module001b-move.json" >/dev/null \
  || fail "Live Module 001B /move response violated the submitted-time reallocation contract."

WORKSPACE_STATUS="$(module001b_request GET \
  "/api/runtime/timesheet/steward/v2/users/$MODULE001B_TARGET_USER_ID/workspace?weekStart=2099-12-27" \
  "$EVIDENCE_DIR/module001b-workspace-after.json")"
[[ "$WORKSPACE_STATUS" == 200 ]] || fail "Module 001B persisted workspace reload returned HTTP $WORKSPACE_STATUS."
jq -e \
  --arg entryId "$MODULE001B_ENTRY_ID" \
  --arg categoryId "$MODULE001B_CATEGORY_ID" '
    .timesheet.status == "submitted"
    and any(.entries[]?;
      .timeEntryId == $entryId
      and .status == "submitted"
      and .hours == 1.25
      and .workDate == "2099-12-28"
      and .projectId == null
      and .taskId == null
      and .nonProjectTimeCategoryId == $categoryId
    )
  ' "$EVIDENCE_DIR/module001b-workspace-after.json" >/dev/null \
  || fail "The Module 001B reallocated entry did not persist with submitted state and protected fields intact."

CLEANUP_STATUS="$(module001b_request DELETE \
  "/api/runtime/timesheet/steward/001b/protected-test-uat/fixture/$MODULE001B_ENTRY_ID" \
  "$EVIDENCE_DIR/module001b-cleanup.json")"
[[ "$CLEANUP_STATUS" == 200 ]] || fail "Protected-Test Module 001B fixture cleanup returned HTTP $CLEANUP_STATUS."
jq -e '
  .status == "protected_test_fixture_removed"
  and .module == "001B"
  and .auditVerified == true
  and .currentEmployeeWeekMutation == false
' "$EVIDENCE_DIR/module001b-cleanup.json" >/dev/null \
  || fail "Module 001B cleanup did not verify immutable reallocation audit evidence."
MODULE001B_ENTRY_ID=''

MODULE001B_LOGOUT_STATUS="$(curl -sS --max-time 60 \
  -X POST -o "$EVIDENCE_DIR/module001b-logout.json" -w '%{http_code}' \
  -H "Authorization: Bearer $MODULE001B_SESSION" \
  -H "X-ProjectPulse-Session: $MODULE001B_SESSION" \
  -H 'X-ProjectPulse-Module-Number: 001B' \
  -H "Origin: $BASE" -H 'Sec-Fetch-Site: same-origin' \
  "$BASE/api/auth/session/logout" || true)"
[[ "$MODULE001B_LOGOUT_STATUS" == 200 ]] || fail "Coordinator logout returned HTTP $MODULE001B_LOGOUT_STATUS."
MODULE001B_SESSION=''

module001b_disable_gate || fail "Failed to force the Module 001B Protected-Test fixture surface closed."
module001b_wait_healthy disabled
trap - EXIT
unset TEST_LOGIN_PASSWORD

jq -n \
  --arg release "$EXPECTED_RELEASE_SHA" \
  '{
    status:"passed",
    module:"001B",
    environment:"protected-test",
    protectedTestRelease:$release,
    liveMoveHttpStatus:200,
    persistedReloadVerified:true,
    submittedStatePreserved:true,
    workerResubmissionRequired:false,
    managerApprovalRequired:false,
    projectManagerApprovalRequired:false,
    immutableAuditVerified:true,
    disposableFixtureRemoved:true,
    fixtureSurfaceDisabled:true,
    currentEmployeeWeekMutation:false,
    productionMutation:false
  }' > "$EVIDENCE_DIR/module001b-live-reallocation-protected-test-uat.json"

echo 'MODULE001B_LIVE_REALLOCATION_UAT=PASS submittedStatePreserved=true workerResubmissionRequired=false managerApprovalRequired=false projectManagerApprovalRequired=false fixtureRemoved=true fixtureSurfaceDisabled=true productionMutation=false'
