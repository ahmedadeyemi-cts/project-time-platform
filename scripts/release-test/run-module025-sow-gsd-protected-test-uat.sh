#!/usr/bin/env bash
set -Eeuo pipefail

fail() {
  echo "ERROR: $*" >&2
  exit 1
}

: "${BASE:?BASE is required}"
: "${TEST_LOGIN_PASSWORD:?TEST_LOGIN_PASSWORD is required}"
: "${EVIDENCE_DIR:?EVIDENCE_DIR is required}"
: "${MODULE025_UAT_RUN_ID:?MODULE025_UAT_RUN_ID is required}"

BASE="${BASE%/}"
[[ "$BASE" == 'https://phd-west-test.onenecklab.com' ]] \
  || fail 'Module 025 UAT is restricted to protected Test.'
[[ "$MODULE025_UAT_RUN_ID" =~ ^[0-9]+-[0-9]+$ ]] \
  || fail 'MODULE025_UAT_RUN_ID must be the exact numeric GitHub run ID and attempt.'

install -d -m 0700 "$EVIDENCE_DIR"
WORK_DIR="$(mktemp -d)"
chmod 0700 "$WORK_DIR"

SA_EMAIL='demo.manager@ussignal.local'
SA_USER_ID=''
SA_SESSION=''
ENGAGEMENT_ID=''
ENGAGEMENT_NUMBER=''
ARCHIVED=false

login() {
  local username="$1" output="$2" payload="$3"
  jq -n --arg username "$username" --arg password "$TEST_LOGIN_PASSWORD" \
    '{username:$username,password:$password}' > "$payload"
  : > "$output"
  chmod 0600 "$payload" "$output"
  local status curl_exit
  set +e
  status="$(curl -sS --http1.1 --connect-timeout 30 --max-time 90 \
    -o "$output" -w '%{http_code}' \
    -H 'Cache-Control: no-cache' \
    -H 'Content-Type: application/json' \
    -H "Origin: $BASE" \
    -H 'Sec-Fetch-Site: same-origin' \
    --data-binary @"$payload" \
    "$BASE/api/auth/local/login")"
  curl_exit=$?
  set -e
  rm -f "$payload"
  printf '%s|%s\n' "$curl_exit" "${status:-000}"
}

auth_request() {
  local method="$1" path="$2" output="$3" session="$4" max_time="${5:-120}" body="${6:-}"
  local headers="${7:-}"
  local status curl_exit
  local args=(
    -sS --http1.1 --connect-timeout 30 --max-time "$max_time"
    -o "$output" -w '%{http_code}'
    -X "$method"
    -H 'Cache-Control: no-cache'
    -H 'Accept: application/json'
    -H "Authorization: Bearer $session"
    -H "X-ProjectPulse-Session: $session"
    -H 'X-ProjectPulse-Module-Number: 025'
    -H "X-ProjectPulse-Module025-Uat-Run: $MODULE025_UAT_RUN_ID"
    -H "Origin: $BASE"
    -H 'Sec-Fetch-Site: same-origin'
  )
  if [[ -n "$body" ]]; then
    args+=( -H 'Content-Type: application/json' --data-binary @"$body" )
  fi
  if [[ -n "$headers" ]]; then
    : > "$headers"
    chmod 0600 "$headers"
    args+=( --dump-header "$headers" )
  fi
  set +e
  status="$(curl "${args[@]}" "$BASE$path")"
  curl_exit=$?
  set -e
  printf '%s|%s\n' "$curl_exit" "${status:-000}"
}

auth_get_with_transient_retry() {
  local path="$1" output="$2" session="$3" label="$4"
  local max_attempts="${5:-12}" max_time="${6:-30}"
  local result='1|000' curl_exit=1 status=000 attempt

  for attempt in $(seq 1 "$max_attempts"); do
    result="$(auth_request GET "$path" "$output" "$session" "$max_time")"
    IFS='|' read -r curl_exit status <<<"$result"
    if [[ "$curl_exit" == 0 && "$status" == 200 ]]; then
      printf '%s|%s\n' "$curl_exit" "$status"
      return 0
    fi

    if [[ "$curl_exit" != 0 ]]; then
      printf 'request=%s attempt=%s curlExit=%s httpStatus=%s\n' \
        "$label" "$attempt" "$curl_exit" "$status" \
        >> "$EVIDENCE_DIR/module025-transient-gateway-retries.log"
      sleep 5
      continue
    fi

    case "$status" in
      000|502|503|504)
        printf 'request=%s attempt=%s curlExit=%s httpStatus=%s\n' \
          "$label" "$attempt" "$curl_exit" "$status" \
          >> "$EVIDENCE_DIR/module025-transient-gateway-retries.log"
        sleep 5
        ;;
      *)
        printf '%s|%s\n' "$curl_exit" "$status"
        return 0
        ;;
    esac
  done

  printf '%s|%s\n' "$curl_exit" "$status"
}

wait_for_fixture_public_revision() {
  local response="$EVIDENCE_DIR/module025-fixture-public-health.json"
  local poll_log="$EVIDENCE_DIR/module025-fixture-public-health-polls.log"
  local status curl_exit attempt consecutive_healthy=0
  : > "$poll_log"

  # Azure can report the Container Apps revision healthy before Application Gateway
  # has refreshed its backend pool. Require three consecutive live-origin responses
  # so the exact-run fixture is exercised only after public ingress has converged.
  for attempt in $(seq 1 36); do
    set +e
    status="$(curl -sS --http1.1 --connect-timeout 20 --max-time 30 \
      -o "$response" -w '%{http_code}' \
      -H 'Cache-Control: no-cache, no-store, max-age=0' \
      "$BASE/health?module025-fixture=$MODULE025_UAT_RUN_ID-$attempt")"
    curl_exit=$?
    set -e
    status="${status:-000}"

    if [[ "$curl_exit" == 0 && "$status" == 200 ]] \
      && jq -e '.status == "healthy"' "$response" >/dev/null 2>&1; then
      (( consecutive_healthy += 1 ))
    else
      consecutive_healthy=0
    fi
    printf 'attempt=%s curlExit=%s httpStatus=%s consecutiveHealthy=%s\n' \
      "$attempt" "$curl_exit" "$status" "$consecutive_healthy" >> "$poll_log"

    if (( consecutive_healthy >= 3 )); then
      jq -n \
        --argjson attempts "$attempt" \
        --argjson consecutiveHealthy "$consecutive_healthy" \
        --arg runId "$MODULE025_UAT_RUN_ID" \
        '{status:"public_revision_ready",attempts:$attempts,consecutiveHealthy:$consecutiveHealthy,runId:$runId,productionMutation:false}' \
        > "$EVIDENCE_DIR/module025-fixture-public-revision-ready.json"
      return 0
    fi

    case "$status" in
      000|502|503|504) sleep 5 ;;
      *) sleep 3 ;;
    esac
  done

  fail 'Module 025 fixture revision did not converge through the protected-Test public gateway.'
}

logout_session() {
  local session="$1" label="$2"
  [[ -n "$session" ]] || return 0
  local output="$EVIDENCE_DIR/${label}-logout.json" result curl_exit status
  result="$(auth_request POST '/api/auth/session/logout' "$output" "$session" 60)"
  IFS='|' read -r curl_exit status <<<"$result"
  jq -n --arg label "$label" --argjson curlExit "$curl_exit" --arg httpStatus "$status" \
    '{identity:$label,curlExit:$curlExit,httpStatus:$httpStatus}' \
    > "$EVIDENCE_DIR/${label}-logout-result.json"
}

archive_fixture() {
  [[ -n "$ENGAGEMENT_ID" && -n "$SA_SESSION" && "$ARCHIVED" != true ]] || return 0
  local output="$EVIDENCE_DIR/module025-cleanup-archive.json" result curl_exit status
  result="$(auth_request POST "/api/module025/sow-gsd/$ENGAGEMENT_ID/archive" "$output" "$SA_SESSION" 120)"
  IFS='|' read -r curl_exit status <<<"$result"
  jq -n \
    --arg engagementId "$ENGAGEMENT_ID" \
    --argjson curlExit "$curl_exit" \
    --arg httpStatus "$status" \
    --arg apiStatus "$(jq -r '.status // empty' "$output" 2>/dev/null || true)" \
    '{engagementId:$engagementId,curlExit:$curlExit,httpStatus:$httpStatus,apiStatus:$apiStatus}' \
    > "$EVIDENCE_DIR/module025-cleanup-result.json"
  if [[ "$curl_exit" == 0 && "$status" == 200 ]] \
    && jq -e '.status == "module025_archived"' "$output" >/dev/null 2>&1; then
    ARCHIVED=true
  fi
}

cleanup() {
  local exit_code="$1"
  set +e
  archive_fixture
  logout_session "$SA_SESSION" 'module025-solution-architect'
  rm -rf -- "$WORK_DIR"
  unset SA_SESSION TEST_LOGIN_PASSWORD
  if [[ "$exit_code" -ne 0 && -n "$ENGAGEMENT_ID" && "$ARCHIVED" != true ]]; then
    echo "ERROR: Module 025 fixture $ENGAGEMENT_ID could not be confirmed archived during failure cleanup." >&2
  fi
}

on_exit() {
  local exit_code=$?
  trap - EXIT
  cleanup "$exit_code"
  exit "$exit_code"
}
trap on_exit EXIT

wait_for_fixture_public_revision

SA_LOGIN="$WORK_DIR/sa-login.json"
SA_LOGIN_PAYLOAD="$WORK_DIR/sa-login-payload.json"
SA_LOGIN_RESULT="$(login "$SA_EMAIL" "$SA_LOGIN" "$SA_LOGIN_PAYLOAD")"
IFS='|' read -r SA_LOGIN_CURL_EXIT SA_LOGIN_STATUS <<<"$SA_LOGIN_RESULT"
[[ "$SA_LOGIN_CURL_EXIT" == 0 && "$SA_LOGIN_STATUS" == 200 ]] \
  || fail "Module 025 protected-Test role-fixture login returned curl exit $SA_LOGIN_CURL_EXIT and HTTP $SA_LOGIN_STATUS."
jq -e '.provider == "LOCAL" and .mustChangePassword == false and (.sessionToken | type == "string" and length > 0)' \
  "$SA_LOGIN" >/dev/null \
  || fail 'Module 025 protected-Test role-fixture login did not satisfy the session contract.'
SA_SESSION="$(jq -r '.sessionToken' "$SA_LOGIN")"
echo "::add-mask::$SA_SESSION"
jq 'del(.sessionToken,.token,.password)' "$SA_LOGIN" \
  > "$EVIDENCE_DIR/module025-solution-architect-login-redacted.json"

SA_SECURITY="$EVIDENCE_DIR/module025-solution-architect-security-context.json"
SA_SECURITY_RESULT="$(auth_get_with_transient_retry '/api/security/context' "$SA_SECURITY" "$SA_SESSION" 'security-context')"
IFS='|' read -r SA_SECURITY_CURL_EXIT SA_SECURITY_STATUS <<<"$SA_SECURITY_RESULT"
[[ "$SA_SECURITY_CURL_EXIT" == 0 && "$SA_SECURITY_STATUS" == 200 ]] \
  || fail "Module 025 role-fixture security context returned curl exit $SA_SECURITY_CURL_EXIT and HTTP $SA_SECURITY_STATUS."
jq -e '
  [
    .roles[]?
    | if type == "object" then (.roleCode // .roleName // "") else tostring end
    | ascii_upcase
    | gsub("[ -]+"; "_")
  ] as $roles
  | any($roles[]; . == "MANAGER")
  and (any($roles[]; . == "SOLUTION_ARCHITECT" or . == "SOLUTIONS_ARCHITECT" or . == "SA" or . == "SAA") | not)
' "$SA_SECURITY" >/dev/null \
  || fail 'The protected-Test role fixture must begin as Manager-only and must not alter persistent role assignments.'

SA_BOOTSTRAP="$EVIDENCE_DIR/module025-solution-architect-bootstrap.json"
SA_BOOTSTRAP_RESULT="$(auth_get_with_transient_retry '/api/module025/sow-gsd/bootstrap' "$SA_BOOTSTRAP" "$SA_SESSION" 'bootstrap')"
IFS='|' read -r SA_BOOTSTRAP_CURL_EXIT SA_BOOTSTRAP_STATUS <<<"$SA_BOOTSTRAP_RESULT"
[[ "$SA_BOOTSTRAP_CURL_EXIT" == 0 && "$SA_BOOTSTRAP_STATUS" == 200 ]] \
  || fail "Module 025 Solution Architect bootstrap returned curl exit $SA_BOOTSTRAP_CURL_EXIT and HTTP $SA_BOOTSTRAP_STATUS."
jq -e '
  .status == "module025_workspace_ready"
  and .module == "025"
  and .migration == "099_module025_sow_gsd_workspace"
  and .access.isSolutionArchitect == true
  and .access.protectedTestUatRoleFixture == true
  and .access.canCreate == true
  and .access.isViewAs == false
  and (.accountExecutives | type == "array" and length > 0)
  and (.insideSalesRepresentatives | type == "array" and length > 0)
  and (.resalePeople == .insideSalesRepresentatives)
  and any(.accountExecutives[]?; ((.displayName // "") | ascii_downcase) == "mike beck")
  and any(.insideSalesRepresentatives[]?; ((.displayName // "") | ascii_downcase) == "jessica shaffer")
  and (
    . as $bootstrap
    | all($bootstrap.accountExecutives[]?;
        .userId as $accountExecutiveUserId
        | all($bootstrap.insideSalesRepresentatives[]?; .userId != $accountExecutiveUserId))
  )
' "$SA_BOOTSTRAP" >/dev/null \
  || fail 'Module 025 did not activate the exact-run Solution Architect fixture with separate Account Executive and Inside Sales Representative directories.'
SA_USER_ID="$(jq -r '.currentUser.userId // empty' "$SA_BOOTSTRAP")"
[[ "$SA_USER_ID" =~ ^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$ ]] \
  || fail 'The selected Solution Architect bootstrap did not expose a valid user ID.'

FIXTURE_SUFFIX="${GITHUB_RUN_ID:-manual}-${GITHUB_RUN_ATTEMPT:-1}-$(date -u +%Y%m%dT%H%M%SZ)"
CREATE_PAYLOAD="$WORK_DIR/module025-create.json"
jq -n \
  --arg customerName "Protected UAT Module 025 $FIXTURE_SUFFIX" \
  --arg serviceOverview 'Upgrade Cisco Unified Communications Manager (Cisco CallManager / CUCM) from version 14.0 to version 15.0. Determine and document the complete customer-facing Plan, Design, Implement, Validate, and Release work required for a safe production upgrade, including readiness, compatibility, licensing, backups, sequencing, rollback, testing, operational handoff, and any customer-specific facts that must be confirmed.' \
  '{
    customerId:null,
    customerName:$customerName,
    customerEntryMode:"manual",
    commercialModel:"time_and_materials",
    customerProgram:"standard",
    accountExecutiveUserId:null,
    resaleUserId:null,
    serviceOverview:$serviceOverview
  }' > "$CREATE_PAYLOAD"

CREATE_RESPONSE="$EVIDENCE_DIR/module025-created-engagement.json"
CREATE_RESULT="$(auth_request POST '/api/module025/sow-gsd' "$CREATE_RESPONSE" "$SA_SESSION" 120 "$CREATE_PAYLOAD")"
IFS='|' read -r CREATE_CURL_EXIT CREATE_STATUS <<<"$CREATE_RESULT"
[[ "$CREATE_CURL_EXIT" == 0 && "$CREATE_STATUS" == 201 ]] \
  || fail "Module 025 temporary SOW creation returned curl exit $CREATE_CURL_EXIT and HTTP $CREATE_STATUS (status $(jq -r '.status // "not-json"' "$CREATE_RESPONSE" 2>/dev/null || true))."
jq -e --arg owner "$SA_USER_ID" '
  .status == "module025_engagement_loaded"
  and .stateChanged == false
  and .engagement.ownerUserId == $owner
  and .engagement.customerEntryMode == "manual"
  and .engagement.commercialModel == "time_and_materials"
  and .engagement.customerProgram == "standard"
  and .engagement.status == "draft"
  and .engagement.isActive == true
  and .engagement.revision == 1
  and (.engagement.phases | type == "array" and length == 5)
' "$CREATE_RESPONSE" >/dev/null \
  || fail 'Module 025 temporary SOW creation did not satisfy the owned draft contract.'
ENGAGEMENT_ID="$(jq -r '.engagement.engagementId // empty' "$CREATE_RESPONSE")"
ENGAGEMENT_NUMBER="$(jq -r '.engagement.engagementNumber // empty' "$CREATE_RESPONSE")"
[[ "$ENGAGEMENT_ID" =~ ^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$ ]] \
  || fail 'Module 025 temporary SOW creation did not return a valid engagement ID.'
[[ -n "$ENGAGEMENT_NUMBER" ]] || fail 'Module 025 temporary SOW creation did not return an engagement number.'

GENERATE_RESPONSE="$EVIDENCE_DIR/module025-generate-response.json"
GENERATE_HEADERS="$EVIDENCE_DIR/module025-generate-response-headers.txt"
GENERATE_STARTED_AT="$(date +%s)"
GENERATE_RESULT="$(auth_request POST "/api/module025/sow-gsd/$ENGAGEMENT_ID/generate" "$GENERATE_RESPONSE" "$SA_SESSION" 55 '' "$GENERATE_HEADERS")"
GENERATE_ELAPSED_SECONDS="$(( $(date +%s) - GENERATE_STARTED_AT ))"
IFS='|' read -r GENERATE_CURL_EXIT GENERATE_STATUS <<<"$GENERATE_RESULT"
GENERATE_RESPONSE_SERVER="$(awk '
  BEGIN { IGNORECASE=1 }
  /^server:/ {
    sub(/\r$/, "")
    value=$0
    sub(/^[^:]+:[[:space:]]*/, "", value)
  }
  END { print value }
' "$GENERATE_HEADERS")"
jq -n \
  --argjson curlExit "$GENERATE_CURL_EXIT" \
  --arg httpStatus "$GENERATE_STATUS" \
  --argjson elapsedSeconds "$GENERATE_ELAPSED_SECONDS" \
  --arg responseServer "$GENERATE_RESPONSE_SERVER" \
  --arg apiStatus "$(jq -r '.status // empty' "$GENERATE_RESPONSE" 2>/dev/null || true)" \
  --arg generationId "$(jq -r '.generationId // empty' "$GENERATE_RESPONSE" 2>/dev/null || true)" \
  --arg correlationId "$(jq -r '.correlationId // empty' "$GENERATE_RESPONSE" 2>/dev/null || true)" \
  '{curlExit:$curlExit,httpStatus:$httpStatus,elapsedSeconds:$elapsedSeconds,responseServer:$responseServer,apiStatus:$apiStatus,generationId:$generationId,correlationId:$correlationId}' \
  > "$EVIDENCE_DIR/module025-generate-http-result.json"
[[ "$GENERATE_CURL_EXIT" == 0 && "$GENERATE_STATUS" == 202 ]] \
  || fail "Module 025 detailed-scope queue request returned curl exit $GENERATE_CURL_EXIT and HTTP $GENERATE_STATUS (status $(jq -r '.status // "not-json"' "$GENERATE_RESPONSE" 2>/dev/null || true))."
(( GENERATE_ELAPSED_SECONDS < 55 )) \
  || fail "Module 025 durable queue request exceeded the protected-Test gateway window ($GENERATE_ELAPSED_SECONDS seconds)."
jq -e '
  .status == "module025_detailed_scope_generation_queued"
  and .stateChanged == true
  and .terminal == false
  and (.generationId | type == "string" and test("^[0-9a-fA-F-]{36}$"))
  and (.revision | type == "number" and . == 1)
  and (.correlationId | type == "string" and length > 0)
' "$GENERATE_RESPONSE" >/dev/null \
  || fail 'Module 025 generation response did not confirm a durable non-terminal queue operation.'
GENERATION_ID="$(jq -r '.generationId' "$GENERATE_RESPONSE")"

GENERATION_RESPONSE="$EVIDENCE_DIR/module025-generation-terminal-response.json"
GENERATION_POLL_STARTED_AT="$(date +%s)"
GENERATION_TERMINAL=false
GENERATION_POLL_ATTEMPTS=0
# This is an asynchronous document job on the Oracle CPU runtime. Use a
# wall-clock ceiling so slow polling requests cannot extend the acceptance window.
GENERATION_DEADLINE="$(( GENERATION_POLL_STARTED_AT + 3900 ))"
for attempt in $(seq 1 780); do
  (( $(date +%s) < GENERATION_DEADLINE )) || break
  GENERATION_POLL_ATTEMPTS="$attempt"
  GENERATION_RESULT="$(auth_request GET "/api/module025/sow-gsd/$ENGAGEMENT_ID/generations/$GENERATION_ID" "$GENERATION_RESPONSE" "$SA_SESSION" 55)"
  IFS='|' read -r GENERATION_CURL_EXIT GENERATION_STATUS <<<"$GENERATION_RESULT"
  if [[ "$GENERATION_CURL_EXIT" != 0 || "$GENERATION_STATUS" != 200 ]]; then
    if [[ "$GENERATION_CURL_EXIT" != 0 ]]; then
      printf 'request=generation-status attempt=%s curlExit=%s httpStatus=%s\n' \
        "$attempt" "$GENERATION_CURL_EXIT" "$GENERATION_STATUS" \
        >> "$EVIDENCE_DIR/module025-transient-gateway-retries.log"
      sleep 5
      continue
    fi
    case "$GENERATION_STATUS" in
      000|502|503|504)
        printf 'request=generation-status attempt=%s curlExit=%s httpStatus=%s\n' \
          "$attempt" "$GENERATION_CURL_EXIT" "$GENERATION_STATUS" \
          >> "$EVIDENCE_DIR/module025-transient-gateway-retries.log"
        sleep 5
        continue
        ;;
      *)
        fail "Module 025 generation status poll $attempt returned curl exit $GENERATION_CURL_EXIT and HTTP $GENERATION_STATUS."
        ;;
    esac
  fi
  if jq -e '.terminal == true' "$GENERATION_RESPONSE" >/dev/null; then
    GENERATION_TERMINAL=true
    break
  fi
  jq -e '
    .terminal == false
    and (.status == "module025_detailed_scope_generation_queued" or .status == "module025_detailed_scope_generation_running")
  ' "$GENERATION_RESPONSE" >/dev/null \
    || fail "Module 025 generation status poll $attempt returned an invalid non-terminal contract."
  sleep 5
done
GENERATION_TOTAL_ELAPSED_SECONDS="$(( $(date +%s) - GENERATION_POLL_STARTED_AT + GENERATE_ELAPSED_SECONDS ))"
[[ "$GENERATION_TERMINAL" == true ]] \
  || fail 'Module 025 durable generation did not reach a terminal state within 65 minutes.'
jq -e --arg id "$GENERATION_ID" '
  .status == "module025_detailed_scope_generated"
  and .generationId == $id
  and .terminal == true
  and .stateChanged == true
  and (.revision | type == "number" and . > 1)
  and (.correlationId | type == "string" and length > 0)
' "$GENERATION_RESPONSE" >/dev/null \
  || fail "Module 025 durable generation finished with status $(jq -r '.status // "not-json"' "$GENERATION_RESPONSE" 2>/dev/null || true): $(jq -r '.message // "no message"' "$GENERATION_RESPONSE" 2>/dev/null || true)"
GENERATED_REVISION="$(jq -r '.revision' "$GENERATION_RESPONSE")"

READBACK_RESPONSE="$EVIDENCE_DIR/module025-review-ready-readback.json"
READBACK_RESULT="$(auth_get_with_transient_retry "/api/module025/sow-gsd/$ENGAGEMENT_ID" "$READBACK_RESPONSE" "$SA_SESSION" 'generated-readback')"
IFS='|' read -r READBACK_CURL_EXIT READBACK_STATUS <<<"$READBACK_RESULT"
[[ "$READBACK_CURL_EXIT" == 0 && "$READBACK_STATUS" == 200 ]] \
  || fail "Module 025 generated-scope readback returned curl exit $READBACK_CURL_EXIT and HTTP $READBACK_STATUS."
jq -e --arg id "$ENGAGEMENT_ID" --arg owner "$SA_USER_ID" --argjson revision "$GENERATED_REVISION" '
  .status == "module025_engagement_loaded"
  and .stateChanged == false
  and .engagement.engagementId == $id
  and .engagement.ownerUserId == $owner
  and .engagement.status == "review_ready"
  and .engagement.isActive == true
  and .engagement.revision == $revision
  and (.engagement.lastGeneratedAt | type == "string" and length > 0)
  and .engagement.sowSections.reviewRequired == true
  and .engagement.sowSections.contractuallyBinding == false
  and ((.engagement.aiMetadata.CorrelationId // .engagement.aiMetadata.correlationId) | type == "string" and length > 0)
  and (.engagement.phases | type == "array" and length == 5)
  and ([.engagement.phases | sort_by(.sortOrder)[] | .phaseCode] == ["plan","design","implement","validate","release"])
  and all(.engagement.phases[];
    (.objective | type == "string" and length >= 120)
    and ((.objective | ascii_downcase) | test("cisco|callmanager|cucm|unified communications manager"))
    and (((.objective | ascii_downcase) | contains("cited scope")) | not)
    and (((.objective | ascii_downcase) | contains("source-backed scope")) | not)
    and (.detailedActivities | type == "array" and length >= 2)
    and (.technicalTasks | type == "array" and length >= 4)
    and (.deliverables | type == "array" and length >= 2)
    and (.customerResponsibilities | type == "array" and length > 0)
    and (.usSignalResponsibilities | type == "array" and length > 0)
    and (.prerequisites | type == "array" and length > 0)
    and (.acceptanceCriteria | type == "array" and length > 0)
    and (.validationSteps | type == "array" and length > 0)
    and (.risks | type == "array" and length > 0)
    and .aiGenerated == true
    and (.suggestedHours | type == "number" and . > 0)
    and (.finalHours | type == "number" and . >= 0)
  )
  and ([.engagement.phases[].detailedActivities | length] | add) >= 10
  and ([.engagement.phases[].suggestedHours] | add) > 0
' "$READBACK_RESPONSE" >/dev/null \
  || fail 'Module 025 persisted readback did not contain the exhaustive Cisco CallManager 14-to-15 P/D/I/V/R contract.'

ACTIVE_LIST="$EVIDENCE_DIR/module025-active-list-readback.json"
ACTIVE_LIST_RESULT="$(auth_get_with_transient_retry "/api/module025/sow-gsd?state=active&ownerUserId=$SA_USER_ID" "$ACTIVE_LIST" "$SA_SESSION" 'active-list-readback')"
IFS='|' read -r ACTIVE_LIST_CURL_EXIT ACTIVE_LIST_STATUS <<<"$ACTIVE_LIST_RESULT"
[[ "$ACTIVE_LIST_CURL_EXIT" == 0 && "$ACTIVE_LIST_STATUS" == 200 ]] \
  || fail "Module 025 active-list readback returned curl exit $ACTIVE_LIST_CURL_EXIT and HTTP $ACTIVE_LIST_STATUS."
jq -e --arg id "$ENGAGEMENT_ID" '
  .status == "module025_engagements_loaded"
  and .state == "active"
  and any(.engagements[]?; .engagementId == $id and .status == "review_ready" and .isActive == true)
' "$ACTIVE_LIST" >/dev/null \
  || fail 'Module 025 active queue did not expose the persisted review-ready SOW/GSD.'

archive_fixture
[[ "$ARCHIVED" == true ]] || fail 'Module 025 generated fixture was not archived.'

ARCHIVED_LIST="$EVIDENCE_DIR/module025-archived-list-readback.json"
ARCHIVED_LIST_RESULT="$(auth_get_with_transient_retry "/api/module025/sow-gsd?state=archived&ownerUserId=$SA_USER_ID" "$ARCHIVED_LIST" "$SA_SESSION" 'archived-list-readback')"
IFS='|' read -r ARCHIVED_LIST_CURL_EXIT ARCHIVED_LIST_STATUS <<<"$ARCHIVED_LIST_RESULT"
[[ "$ARCHIVED_LIST_CURL_EXIT" == 0 && "$ARCHIVED_LIST_STATUS" == 200 ]] \
  || fail "Module 025 archived-list verification returned curl exit $ARCHIVED_LIST_CURL_EXIT and HTTP $ARCHIVED_LIST_STATUS."
jq -e --arg id "$ENGAGEMENT_ID" '
  .status == "module025_engagements_loaded"
  and .state == "archived"
  and any(.engagements[]?; .engagementId == $id and .status == "archived" and .isActive == false)
' "$ARCHIVED_LIST" >/dev/null \
  || fail 'Module 025 cleanup did not persist the fixture in the archived queue.'

jq -n \
  --arg identity "$SA_EMAIL" \
  --arg userId "$SA_USER_ID" \
  --arg engagementId "$ENGAGEMENT_ID" \
  --arg engagementNumber "$ENGAGEMENT_NUMBER" \
  --arg generationId "$GENERATION_ID" \
  --argjson generatedRevision "$GENERATED_REVISION" \
  --argjson generationQueueElapsedSeconds "$GENERATE_ELAPSED_SECONDS" \
  --argjson generationTotalElapsedSeconds "$GENERATION_TOTAL_ELAPSED_SECONDS" \
  --argjson generationPollAttempts "$GENERATION_POLL_ATTEMPTS" \
  --arg generationResponseServer "$GENERATE_RESPONSE_SERVER" \
  --arg correlationId "$(jq -r '.correlationId' "$GENERATION_RESPONSE")" \
  --argjson suggestedHours "$(jq -r '[.engagement.phases[].suggestedHours] | add' "$READBACK_RESPONSE")" \
  '{
    status:"passed",
    environment:"protected-test",
    identity:$identity,
    userId:$userId,
    role:"SOLUTION_ARCHITECT",
    authorizationMode:"exact_run_non_persistent_module025_role_fixture",
    persistentRoleAssignmentMutation:false,
    engagementId:$engagementId,
    engagementNumber:$engagementNumber,
    generationId:$generationId,
    createStatus:"draft",
    queueStatus:"module025_detailed_scope_generation_queued",
    generateStatus:"module025_detailed_scope_generated",
    readbackStatus:"review_ready",
    generatedRevision:$generatedRevision,
    generationQueueElapsedSeconds:$generationQueueElapsedSeconds,
    generationTotalElapsedSeconds:$generationTotalElapsedSeconds,
    generationPollAttempts:$generationPollAttempts,
    generationResponseServer:$generationResponseServer,
    phaseCodes:["plan","design","implement","validate","release"],
    technologyExample:"Cisco Unified Communications Manager 14.0 to 15.0",
    minimumDetailedWorkPackages:10,
    genericCitedScopeBoilerplateRejected:true,
    accountExecutiveDirectoryRole:"SALES",
    insideSalesRepresentativeDirectoryRole:"INSIDE_SALES",
    suggestedHours:$suggestedHours,
    correlationId:$correlationId,
    cleanupStatus:"archived",
    fixtureActive:false,
    productionMutation:false,
    privateRuntimeConfigurationMutation:false
  }' > "$EVIDENCE_DIR/module025-sow-gsd-protected-test-uat.json"

echo "MODULE025_SOW_GSD_PROTECTED_TEST_UAT=PASS identity=$SA_EMAIL authorization=exact-run-non-persistent-solution-architect-fixture engagement=$ENGAGEMENT_NUMBER example=cisco-callmanager-14-to-15 minimumWorkPackages=10 phases=plan,design,implement,validate,release state=review_ready cleanup=archived"
