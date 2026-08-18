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
unset TEST_LOGIN_PASSWORD

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
    and .access.effectiveUserId == $userId
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
    module019EngineeringWorkspace:true,
    moduleOwnerCatalogReadThrough:true,
    ownerCandidatesRestrictedToSuperAdministrators:true,
    requestFamilies:["Service Request","Presales","Internal"],
    mutation:false,
    productionMutation:false
  }' > "$EVIDENCE_DIR/assigned-work-protected-test-uat.json"

unset SESSION_TOKEN

echo 'ASSIGNED_WORK_PROTECTED_TEST_UAT=PASS identity=jason.mosier@ussignal.local serviceRequest=SR-8C81ACA3 modules=001,001A,019 week=2026-08-16..2026-08-22'
