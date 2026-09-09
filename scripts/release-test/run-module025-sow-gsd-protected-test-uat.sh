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
: "${MODULE025_UAT_EXPIRES_AT:?MODULE025_UAT_EXPIRES_AT is required}"
[[ "$MODULE025_UAT_EXPIRES_AT" =~ ^[0-9]{10}$ ]] \
  || fail 'MODULE025_UAT_EXPIRES_AT must be the exact fixture expiry epoch.'

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
ACCOUNT_EXECUTIVE_USER_ID=''
RESALE_USER_ID=''
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
  local status curl_exit max_time
  max_time="$(fixture_request_budget 90)" || { rm -f "$payload"; printf '28|000\n'; return 0; }
  set +e
  status="$(curl -sS --http1.1 --connect-timeout 30 --max-time "$max_time" \
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

# All normal requests (including post-terminal readback) stop before the
# cleanup reserve. Archival gets its own bounded window; no retry can consume it.
fixture_request_budget() {
  local requested="$1" reserve="${2:-180}" remaining
  remaining="$(( MODULE025_UAT_EXPIRES_AT - reserve - $(date +%s) ))"
  (( remaining > 0 )) || return 1
  (( requested <= remaining )) || requested="$remaining"
  printf '%s\n' "$requested"
}
fixture_retry_pause() {
  local pause
  pause="$(fixture_request_budget 5)" || return 1
  sleep "$pause"
}
auth_request() {
  local method="$1" path="$2" output="$3" session="$4" max_time="${5:-120}" body="${6:-}"
  local headers="${7:-}"
  local status curl_exit reserve=180
  if [[ "$method" == POST && -n "$ENGAGEMENT_ID" && "$path" == "/api/module025/sow-gsd/$ENGAGEMENT_ID/archive" ]]; then
    reserve=60
  elif [[ "$method" == POST && "$path" == '/api/auth/session/logout' ]]; then
    reserve=5
  fi
  if ! max_time="$(fixture_request_budget "$max_time" "$reserve")"; then
    printf '{}\n' > "$output"
    printf '28|000\n'
    return 0
  fi
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
      fixture_retry_pause || break
      continue
    fi

    case "$status" in
      000|502|503|504)
        printf 'request=%s attempt=%s curlExit=%s httpStatus=%s\n' \
          "$label" "$attempt" "$curl_exit" "$status" \
          >> "$EVIDENCE_DIR/module025-transient-gateway-retries.log"
        fixture_retry_pause || break
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
    local max_time
    max_time="$(fixture_request_budget 30)" || break
    set +e
    status="$(curl -sS --http1.1 --connect-timeout 20 --max-time "$max_time" \
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
      *) fixture_retry_pause || break ;;
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
ACCOUNT_EXECUTIVE_USER_ID="$(jq -r 'first(.accountExecutives[] | select((.displayName // "" | ascii_downcase) == "mike beck") | .userId) // empty' "$SA_BOOTSTRAP")"
RESALE_USER_ID="$(jq -r 'first(.insideSalesRepresentatives[] | select((.displayName // "" | ascii_downcase) == "jessica shaffer") | .userId) // empty' "$SA_BOOTSTRAP")"
[[ "$ACCOUNT_EXECUTIVE_USER_ID" =~ ^[0-9a-fA-F-]{36}$ && "$RESALE_USER_ID" =~ ^[0-9a-fA-F-]{36}$ ]] \
  || fail 'The exact-run Solution Architect bootstrap did not expose the assigned Account Executive and Inside Sales Representative IDs.'

FIXTURE_SUFFIX="${GITHUB_RUN_ID:-manual}-${GITHUB_RUN_ATTEMPT:-1}-$(date -u +%Y%m%dT%H%M%SZ)"
CREATE_PAYLOAD="$WORK_DIR/module025-create.json"
  jq -n \
  --arg customerName "Protected UAT Module 025 $FIXTURE_SUFFIX" \
  --arg accountExecutiveUserId "$ACCOUNT_EXECUTIVE_USER_ID" \
  --arg resaleUserId "$RESALE_USER_ID" \
  --arg serviceOverview 'Upgrade Cisco Unified Communications Manager (Cisco CallManager / CUCM) from version 14.0 to version 15.0. Determine and document the complete customer-facing Plan, Design, Implement, Validate, and Release work required for a safe production upgrade, including readiness, compatibility, licensing, backups, sequencing, rollback, testing, operational handoff, and any customer-specific facts that must be confirmed.' \
  '{
    customerId:null,
    customerName:$customerName,
    customerEntryMode:"manual",
    commercialModel:"time_and_materials",
    customerProgram:"standard",
    accountExecutiveUserId:$accountExecutiveUserId,
    resaleUserId:$resaleUserId,
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
# Finish before the existing short-lived fixture expires, reserving three
# minutes for archival cleanup. Never extend the authorization to fit inference.
GENERATION_DEADLINE="$(( GENERATION_POLL_STARTED_AT + 2520 ))"
if [[ "${MODULE025_UAT_EXPIRES_AT:-}" =~ ^[0-9]+$ ]]; then
  FIXTURE_GENERATION_DEADLINE="$(( MODULE025_UAT_EXPIRES_AT - 180 ))"
  (( FIXTURE_GENERATION_DEADLINE >= GENERATION_DEADLINE )) || GENERATION_DEADLINE="$FIXTURE_GENERATION_DEADLINE"
fi
for attempt in $(seq 1 780); do
  GENERATION_REMAINING_SECONDS="$(( GENERATION_DEADLINE - $(date +%s) ))"
  (( GENERATION_REMAINING_SECONDS > 0 )) || break
  GENERATION_POLL_TIMEOUT=55
  (( GENERATION_REMAINING_SECONDS >= GENERATION_POLL_TIMEOUT )) || GENERATION_POLL_TIMEOUT="$GENERATION_REMAINING_SECONDS"
  GENERATION_POLL_ATTEMPTS="$attempt"
  GENERATION_RESULT="$(auth_request GET "/api/module025/sow-gsd/$ENGAGEMENT_ID/generations/$GENERATION_ID" "$GENERATION_RESPONSE" "$SA_SESSION" "$GENERATION_POLL_TIMEOUT")"
  IFS='|' read -r GENERATION_CURL_EXIT GENERATION_STATUS <<<"$GENERATION_RESULT"
  if [[ "$GENERATION_CURL_EXIT" != 0 || "$GENERATION_STATUS" != 200 ]]; then
    if [[ "$GENERATION_CURL_EXIT" != 0 ]]; then
      printf 'request=generation-status attempt=%s curlExit=%s httpStatus=%s\n' \
        "$attempt" "$GENERATION_CURL_EXIT" "$GENERATION_STATUS" \
        >> "$EVIDENCE_DIR/module025-transient-gateway-retries.log"
      fixture_retry_pause || break
      continue
    fi
    case "$GENERATION_STATUS" in
      000|502|503|504)
        printf 'request=generation-status attempt=%s curlExit=%s httpStatus=%s\n' \
          "$attempt" "$GENERATION_CURL_EXIT" "$GENERATION_STATUS" \
          >> "$EVIDENCE_DIR/module025-transient-gateway-retries.log"
        fixture_retry_pause || break
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
  fixture_retry_pause || break
done
GENERATION_TOTAL_ELAPSED_SECONDS="$(( $(date +%s) - GENERATION_POLL_STARTED_AT + GENERATE_ELAPSED_SECONDS ))"
[[ "$GENERATION_TERMINAL" == true ]] \
  || fail 'Module 025 durable generation did not reach a terminal state within 42 minutes or before the authorization cleanup reserve.'
jq -e --arg id "$GENERATION_ID" '
  .status == "module025_detailed_scope_generated"
  and .generationId == $id
  and .terminal == true
  and .stateChanged == true
  and (.targetDecisions | type == "array" and length > 0)
  and (.targetDecisions[0].Target == "deepseek_v4")
  and any(.targetDecisions[]; (.Target == "deepseek_v4" or .Target == "celar_ai") and .Outcome == "used" and .ReasonCode == "generation_succeeded")
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

# Exercise the real saved-edit → confirmation → migration-106 retained-version
# lifecycle against this same temporary authorized SOW. The edit changes only
# reviewed phase effort, so it must not start a second AI generation request.
SAVE_PAYLOAD="$WORK_DIR/module025-save-edited-phase.json"
jq '{
  expectedRevision: .engagement.revision,
  customerId: .engagement.customerId,
  customerName: .engagement.customerName,
  customerEntryMode: .engagement.customerEntryMode,
  commercialModel: .engagement.commercialModel,
  customerProgram: .engagement.customerProgram,
  accountExecutiveUserId: .engagement.accountExecutiveUserId,
  resaleUserId: .engagement.resaleUserId,
  serviceOverview: .engagement.serviceOverview,
  phases: [.engagement.phases[] | {
    phaseCode, finalHours: ((.finalHours // 0) + (if .phaseCode == "plan" then 1 else 0 end)),
    objective, detailedActivities, technicalTasks, deliverables, customerResponsibilities,
    usSignalResponsibilities, prerequisites, dependencies, assumptions, openQuestions,
    acceptanceCriteria, validationSteps, risks, loeRationale
  }]
}' "$READBACK_RESPONSE" > "$SAVE_PAYLOAD"
SAVE_RESPONSE="$EVIDENCE_DIR/module025-saved-edit-response.json"
SAVE_RESULT="$(auth_request PUT "/api/module025/sow-gsd/$ENGAGEMENT_ID" "$SAVE_RESPONSE" "$SA_SESSION" 120 "$SAVE_PAYLOAD")"
IFS='|' read -r SAVE_CURL_EXIT SAVE_STATUS <<<"$SAVE_RESULT"
[[ "$SAVE_CURL_EXIT" == 0 && "$SAVE_STATUS" == 200 ]] \
  || fail "Module 025 saved-edit API returned curl exit $SAVE_CURL_EXIT and HTTP $SAVE_STATUS."
jq -e --argjson prior "$GENERATED_REVISION" '
  .status == "module025_autosaved"
  and .stateChanged == true
  and (.revision | type == "number" and . > $prior)
  and .requiresRegeneration == false
' "$SAVE_RESPONSE" >/dev/null \
  || fail 'Module 025 saved-edit receipt did not preserve the generated scope without requesting another generation.'
SAVED_EDIT_REVISION="$(jq -r '.revision' "$SAVE_RESPONSE")"

CONFIRM_RESPONSE="$EVIDENCE_DIR/module025-confirm-response.json"
CONFIRM_RESULT="$(auth_request POST "/api/module025/sow-gsd/$ENGAGEMENT_ID/confirm" "$CONFIRM_RESPONSE" "$SA_SESSION" 120 '')"
IFS='|' read -r CONFIRM_CURL_EXIT CONFIRM_STATUS <<<"$CONFIRM_RESULT"
[[ "$CONFIRM_CURL_EXIT" == 0 && "$CONFIRM_STATUS" == 200 ]] \
  || fail "Module 025 confirmation API returned curl exit $CONFIRM_CURL_EXIT and HTTP $CONFIRM_STATUS."
jq -e --argjson prior "$SAVED_EDIT_REVISION" '
  .status == "module025_confirmed" and .stateChanged == true and (.revision | type == "number" and . > $prior)
  and (.version.versionId | type == "string")
' "$CONFIRM_RESPONSE" >/dev/null \
  || fail 'Module 025 confirmation did not create an authorized reviewed state.'
CONFIRMED_REVISION="$(jq -r '.revision' "$CONFIRM_RESPONSE")"
CONFIRMED_VERSION_ID="$(jq -r '.version.versionId' "$CONFIRM_RESPONSE")"
CONFIRMED_SOW_SHA="$(jq -r '.version.sowSha256' "$CONFIRM_RESPONSE")"
CONFIRMED_GSD_SHA="$(jq -r '.version.gsdSha256' "$CONFIRM_RESPONSE")"

RELEASE_PAYLOAD="$WORK_DIR/module025-release-version.json"
jq -n --argjson expectedRevision "$CONFIRMED_REVISION" '{expectedRevision:$expectedRevision}' > "$RELEASE_PAYLOAD"
VERSION_RESPONSE="$EVIDENCE_DIR/module025-retained-version-response.json"
VERSION_RESULT="$(auth_request POST "/api/module025/sow-gsd/$ENGAGEMENT_ID/versions" "$VERSION_RESPONSE" "$SA_SESSION" 120 "$RELEASE_PAYLOAD")"
IFS='|' read -r VERSION_CURL_EXIT VERSION_STATUS <<<"$VERSION_RESULT"
[[ "$VERSION_CURL_EXIT" == 0 && "$VERSION_STATUS" == 200 ]] \
  || fail "Module 025 retained-version API returned curl exit $VERSION_CURL_EXIT and HTTP $VERSION_STATUS."
jq -e --arg id "$CONFIRMED_VERSION_ID" --arg sow "$CONFIRMED_SOW_SHA" --arg gsd "$CONFIRMED_GSD_SHA" '
  .status == "module025_version_released" and .stateChanged == false
  and .version.versionId == $id and .version.sowSha256 == $sow and .version.gsdSha256 == $gsd
' "$VERSION_RESPONSE" >/dev/null \
  || fail 'Repeated retention did not reuse the immutable version created by confirmation.'
VERSION_ID="$(jq -r '.version.versionId' "$VERSION_RESPONSE")"
VERSION_SOW_SHA="$(jq -r '.version.sowSha256' "$VERSION_RESPONSE")"
VERSION_GSD_SHA="$(jq -r '.version.gsdSha256' "$VERSION_RESPONSE")"

VERSION_REPEAT_RESPONSE="$EVIDENCE_DIR/module025-retained-version-repeat-response.json"
VERSION_REPEAT_RESULT="$(auth_request POST "/api/module025/sow-gsd/$ENGAGEMENT_ID/versions" "$VERSION_REPEAT_RESPONSE" "$SA_SESSION" 120 "$RELEASE_PAYLOAD")"
IFS='|' read -r VERSION_REPEAT_CURL_EXIT VERSION_REPEAT_STATUS <<<"$VERSION_REPEAT_RESULT"
[[ "$VERSION_REPEAT_CURL_EXIT" == 0 && "$VERSION_REPEAT_STATUS" == 200 ]] \
  || fail "Module 025 repeated retain request returned curl exit $VERSION_REPEAT_CURL_EXIT and HTTP $VERSION_REPEAT_STATUS."
jq -e --arg id "$VERSION_ID" '.stateChanged == false and .version.versionId == $id' "$VERSION_REPEAT_RESPONSE" >/dev/null \
  || fail 'Repeated retention created a duplicate immutable version.'

VERSIONS_RESPONSE="$EVIDENCE_DIR/module025-retained-versions-readback.json"
VERSIONS_RESULT="$(auth_get_with_transient_retry "/api/module025/sow-gsd/$ENGAGEMENT_ID/versions?page=1" "$VERSIONS_RESPONSE" "$SA_SESSION" 'retained-versions-readback')"
IFS='|' read -r VERSIONS_CURL_EXIT VERSIONS_STATUS <<<"$VERSIONS_RESULT"
[[ "$VERSIONS_CURL_EXIT" == 0 && "$VERSIONS_STATUS" == 200 ]] \
  || fail "Module 025 retained-version readback returned curl exit $VERSIONS_CURL_EXIT and HTTP $VERSIONS_STATUS."
jq -e --arg id "$VERSION_ID" '
  ([.versions[] | select(.versionId == $id)] | length) == 1
  and ([.versions[] | select(.versionId == $id) | .firstSowServedAt] | all(. == null))
' "$VERSIONS_RESPONSE" >/dev/null \
  || fail 'Module 025 retained-version readback did not expose exactly one unissued version before download.'

download_twice() {
  local artifact="$1" expected="$2" first second headers first_result second_result first_curl_exit first_status second_curl_exit second_status
  first="$EVIDENCE_DIR/module025-${artifact}-download-1.bin"
  second="$EVIDENCE_DIR/module025-${artifact}-download-2.bin"
  headers="$EVIDENCE_DIR/module025-${artifact}-download-headers.txt"
  first_result="$(auth_request GET "/api/module025/sow-gsd/$ENGAGEMENT_ID/versions/$VERSION_ID/$artifact" "$first" "$SA_SESSION" 120 '' "$headers")"
  IFS='|' read -r first_curl_exit first_status <<<"$first_result"
  [[ "$first_curl_exit" == 0 && "$first_status" == 200 ]] \
    || fail "$artifact first download returned curl exit $first_curl_exit and HTTP $first_status."
  second_result="$(auth_request GET "/api/module025/sow-gsd/$ENGAGEMENT_ID/versions/$VERSION_ID/$artifact" "$second" "$SA_SESSION" 120 '')"
  IFS='|' read -r second_curl_exit second_status <<<"$second_result"
  [[ "$second_curl_exit" == 0 && "$second_status" == 200 ]] \
    || fail "$artifact repeated download returned curl exit $second_curl_exit and HTTP $second_status."
  [[ "$(sha256sum "$first" | awk '{print $1}')" == "$expected" ]] || fail "$artifact first download failed retained hash verification."
  [[ "$(sha256sum "$second" | awk '{print $1}')" == "$expected" ]] || fail "$artifact repeated download changed retained bytes."
}
download_twice sow.docx "$VERSION_SOW_SHA"
download_twice gsd.xlsx "$VERSION_GSD_SHA"

VERSIONS_AFTER_DOWNLOAD_RESPONSE="$EVIDENCE_DIR/module025-retained-versions-after-download-readback.json"
auth_get_with_transient_retry "/api/module025/sow-gsd/$ENGAGEMENT_ID/versions?page=1" "$VERSIONS_AFTER_DOWNLOAD_RESPONSE" "$SA_SESSION" 'retained-versions-after-download-readback' >/dev/null
jq -e --arg id "$VERSION_ID" '
  ([.versions[] | select(.versionId == $id)] | length) == 1
  and ([.versions[] | select(.versionId == $id) | .firstSowServedAt] | length) == 1
  and ([.versions[] | select(.versionId == $id) | .firstGsdServedAt] | length) == 1
' "$VERSIONS_AFTER_DOWNLOAD_RESPONSE" >/dev/null \
  || fail 'Repeated retained downloads did not leave one first-issuance receipt for each artifact.'
echo 'MODULE025_RETAINED_VERSION_API_LIFECYCLE=PASS'

[[ -x "${RUNNER_TEMP:-}/flowhive-psa-browser/bin/python" ]] \
  || fail 'The authenticated Module 025 browser verifier is not installed.'
BASE="$BASE" TEST_LOGIN_PASSWORD="$TEST_LOGIN_PASSWORD" \
  MODULE025_ENGAGEMENT_NUMBER="$ENGAGEMENT_NUMBER" MODULE025_CREATE_RESPONSE="$CREATE_RESPONSE" \
  "${RUNNER_TEMP}/flowhive-psa-browser/bin/python" tests/module025-sow-register-browser.py \
  > "$EVIDENCE_DIR/module025-register-browser.log"
grep -Fq 'MODULE025_REGISTER_BROWSER_DISPLAY=PASS' "$EVIDENCE_DIR/module025-register-browser.log" \
  || fail 'Authenticated retained SOW/GSD and CSV browser downloads were not verified.'
echo 'MODULE025_RETAINED_VERSION_BROWSER_LIFECYCLE=PASS authenticatedClicks=true hashes=verified unauthorized=checked reload=verified'

ACTIVE_LIST="$EVIDENCE_DIR/module025-active-list-readback.json"
ACTIVE_LIST_RESULT="$(auth_get_with_transient_retry "/api/module025/sow-gsd?state=active&ownerUserId=$SA_USER_ID" "$ACTIVE_LIST" "$SA_SESSION" 'active-list-readback')"
IFS='|' read -r ACTIVE_LIST_CURL_EXIT ACTIVE_LIST_STATUS <<<"$ACTIVE_LIST_RESULT"
[[ "$ACTIVE_LIST_CURL_EXIT" == 0 && "$ACTIVE_LIST_STATUS" == 200 ]] \
  || fail "Module 025 active-list readback returned curl exit $ACTIVE_LIST_CURL_EXIT and HTTP $ACTIVE_LIST_STATUS."
jq -e --arg id "$ENGAGEMENT_ID" '
  .status == "module025_engagements_loaded"
  and .state == "active"
  and any(.engagements[]?; .engagementId == $id and .status == "confirmed" and .isActive == true)
' "$ACTIVE_LIST" >/dev/null \
  || fail 'Module 025 active queue did not expose the persisted confirmed SOW/GSD.'

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
  --argjson targetDecisions "$(jq -c '.targetDecisions' "$GENERATION_RESPONSE")" \
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
  --argjson savedEditRevision "$SAVED_EDIT_REVISION" \
  --argjson confirmedRevision "$CONFIRMED_REVISION" \
  --arg retainedVersionId "$VERSION_ID" \
  --arg retainedSowSha256 "$VERSION_SOW_SHA" \
  --arg retainedGsdSha256 "$VERSION_GSD_SHA" \
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
    targetDecisions:$targetDecisions,
    draftProvider:([$targetDecisions[] | select((.Target == "deepseek_v4" or .Target == "celar_ai") and .Outcome == "used") | .Target] | first),
    readbackStatus:"confirmed",
    generatedRevision:$generatedRevision,
    savedEditRevision:$savedEditRevision,
    confirmedRevision:$confirmedRevision,
    retainedVersionId:$retainedVersionId,
    retainedVersionNumber:1,
    retainedSowSha256:$retainedSowSha256,
    retainedGsdSha256:$retainedGsdSha256,
    repeatedRetentionStateChanged:false,
    repeatedDownloadsPreservedBytes:true,
    firstIssuanceRowsPerArtifact:1,
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

echo "MODULE025_SOW_GSD_PROTECTED_TEST_UAT=PASS identity=$SA_EMAIL authorization=exact-run-non-persistent-solution-architect-fixture engagement=$ENGAGEMENT_NUMBER example=cisco-callmanager-14-to-15 minimumWorkPackages=10 phases=plan,design,implement,validate,release state=confirmed retainedVersion=1 repeatedDownloads=stable cleanup=archived"
