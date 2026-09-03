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
  || fail "Utilization UAT is restricted to protected Test."

install -d -m 0700 "$EVIDENCE_DIR"

LOGIN_RESPONSE="$(mktemp)"
LOGIN_PAYLOAD="$(mktemp)"
chmod 0600 "$LOGIN_RESPONSE" "$LOGIN_PAYLOAD"
cleanup() {
  rm -f "$LOGIN_RESPONSE" "$LOGIN_PAYLOAD"
}
trap cleanup EXIT

KEVIN_EMAIL='Kevin.damisch@ussignal.local'
jq -n \
  --arg username "$KEVIN_EMAIL" \
  --arg password "$TEST_LOGIN_PASSWORD" \
  '{username:$username,password:$password}' > "$LOGIN_PAYLOAD"

LOGIN_STATUS="$(curl -sS --max-time 90 \
  -o "$LOGIN_RESPONSE" -w '%{http_code}' \
  -H 'Cache-Control: no-cache' \
  -H 'Content-Type: application/json' \
  -H "Origin: $BASE" \
  -H 'Sec-Fetch-Site: same-origin' \
  --data-binary @"$LOGIN_PAYLOAD" \
  "$BASE/api/auth/local/login" || true)"
[[ "$LOGIN_STATUS" == 200 ]] \
  || fail "Kevin protected-Test login returned HTTP $LOGIN_STATUS."

jq -e '
  .provider == "LOCAL"
  and .mustChangePassword == false
  and (.sessionToken | type == "string" and length > 0)
' "$LOGIN_RESPONSE" >/dev/null \
  || fail "Kevin login response did not satisfy the authenticated session contract."

SESSION_TOKEN="$(jq -r '.sessionToken' "$LOGIN_RESPONSE")"
echo "::add-mask::$SESSION_TOKEN"
jq 'del(.sessionToken,.token,.password)' "$LOGIN_RESPONSE" \
  > "$EVIDENCE_DIR/kevin-login-redacted.json"
rm -f "$LOGIN_PAYLOAD" "$LOGIN_RESPONSE"
trap - EXIT

AUTH_HEADERS=(
  -H 'Cache-Control: no-cache'
  -H "Authorization: Bearer $SESSION_TOKEN"
  -H "X-ProjectPulse-Session: $SESSION_TOKEN"
  -H 'X-ProjectPulse-Module-Number: 003'
  -H "Origin: $BASE"
  -H 'Sec-Fetch-Site: same-origin'
)

auth_get() {
  local path="$1" output="$2"
  curl -sS --max-time 120 \
    -o "$output" -w '%{http_code}' \
    "${AUTH_HEADERS[@]}" \
    "$BASE$path" || true
}

SECURITY_BEFORE="$EVIDENCE_DIR/kevin-security-context-before.json"
SECURITY_BEFORE_STATUS="$(auth_get '/api/security/context' "$SECURITY_BEFORE")"
[[ "$SECURITY_BEFORE_STATUS" == 200 ]] \
  || fail "Kevin security context returned HTTP $SECURITY_BEFORE_STATUS."
jq -e . "$SECURITY_BEFORE" >/dev/null \
  || fail "Kevin security context did not return JSON."

jq -e '
  [
    .roles[]?
    | if type == "object"
      then (.roleCode // .roleName // "")
      else tostring
      end
    | tostring
    | ascii_upcase
    | gsub("[ -]+"; "_")
  ] as $roles
  | (($roles | index("ENGINEER")) != null or ($roles | index("ENGINEERING")) != null)
  and (($roles | index("TEAM_LEAD")) == null)
  and (($roles | index("ENGINEERING_LEAD")) == null)
  and (($roles | index("ENGINEERING_TEAM_LEAD")) == null)
' "$SECURITY_BEFORE" >/dev/null \
  || fail "Kevin is not authenticated exclusively within the expected Engineer role family."

KEVIN_USER_ID="$(jq -r '.userId // empty' "$SECURITY_BEFORE")"
[[ "$KEVIN_USER_ID" =~ ^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$ ]] \
  || fail "Kevin security context did not expose a valid authenticated user ID."

YEARLY="$EVIDENCE_DIR/kevin-utilization-2026.json"
YEARLY_STATUS="$(auth_get '/api/utilization/yearly-status?year=2026' "$YEARLY")"
[[ "$YEARLY_STATUS" == 200 ]] \
  || fail "Kevin yearly utilization returned HTTP $YEARLY_STATUS."
jq -e . "$YEARLY" >/dev/null \
  || fail "Kevin yearly utilization did not return JSON."

jq -e '
  .year == 2026
  and .calculationStatus == "calculated"
  and (.annualSummary.billableHours == 572)
  and (.annualSummary.utilizationPercent == 29.67)
  and ([.quarters[] | select(.quarterNumber == 1)][0].billableHours == 0)
  and ([.quarters[] | select(.quarterNumber == 1)][0].utilizationPercent == 0)
  and ([.quarters[] | select(.quarterNumber == 2)][0].billableHours == 364)
  and ([.quarters[] | select(.quarterNumber == 2)][0].utilizationPercent == 75.52)
  and ([.quarters[] | select(.quarterNumber == 3)][0].billableHours == 208)
  and ([.quarters[] | select(.quarterNumber == 3)][0].utilizationPercent == 43.15)
  and ([.quarters[] | select(.quarterNumber == 4)][0].billableHours == 0)
  and ([.quarters[] | select(.quarterNumber == 4)][0].utilizationPercent == 0)
' "$YEARLY" >/dev/null \
  || fail "Kevin utilization does not match the accepted 2026 quarterly and annual values."

OTHER_ENGINEER_ID='11111111-1111-1111-1111-111111111111'
[[ "$OTHER_ENGINEER_ID" != "$KEVIN_USER_ID" ]] \
  || fail "The cross-engineer utilization probe must use an identity different from Kevin."

OTHER_ENGINEER_RESPONSE="$EVIDENCE_DIR/kevin-cross-engineer-normalized.json"
OTHER_ENGINEER_STATUS="$(auth_get "/api/utilization/engineering-team-summary?year=2026&engineerUserId=$OTHER_ENGINEER_ID" "$OTHER_ENGINEER_RESPONSE")"
[[ "$OTHER_ENGINEER_STATUS" == 200 ]] \
  || fail "Kevin cross-engineer utilization normalization probe returned HTTP $OTHER_ENGINEER_STATUS instead of 200."
jq -e \
  --arg kevinUserId "$KEVIN_USER_ID" \
  --arg requestedUserId "$OTHER_ENGINEER_ID" \
  '
    .canViewEngineeringTeamUtilization == true
    and .scope == "own_engineer_scope"
    and .selectedEngineerUserId == $kevinUserId
    and .selectedEngineerUserId != $requestedUserId
    and .access.canViewAll == false
    and .access.canSelectEngineer == false
    and .access.canUseTeamScope == false
    and .access.canUseOwnScope == true
    and (.selectableEngineers | type == "array" and length == 1)
    and ([.selectableEngineers[]?.userId] == [$kevinUserId])
    and (.members | type == "array" and length == 1)
    and ([.members[]?.userId] == [$kevinUserId])
    and (([.members[]?.userId] | index($requestedUserId)) == null)
    and (.collectiveSummary.memberCount == 1)
  ' "$OTHER_ENGINEER_RESPONSE" >/dev/null \
  || fail "Kevin cross-engineer utilization request was not normalized to his authenticated self-only scope."

VIEW_AS_RESPONSE="$EVIDENCE_DIR/kevin-view-as-denied.json"
VIEW_AS_STATUS="$(auth_get '/api/project-workspace/view-as/users' "$VIEW_AS_RESPONSE")"
[[ "$VIEW_AS_STATUS" == 403 ]] \
  || fail "Kevin View-As probe returned HTTP $VIEW_AS_STATUS instead of 403."

SECURITY_AFTER="$EVIDENCE_DIR/kevin-security-context-after-optional-failures.json"
SECURITY_AFTER_STATUS="$(auth_get '/api/security/context' "$SECURITY_AFTER")"
[[ "$SECURITY_AFTER_STATUS" == 200 ]] \
  || fail "Kevin identity was not preserved after optional 403 responses; security context returned HTTP $SECURITY_AFTER_STATUS."
jq -e . "$SECURITY_AFTER" >/dev/null \
  || fail "Kevin post-failure security context did not return JSON."

LOGOUT_STATUS="$(curl -sS --max-time 60 \
  -o "$EVIDENCE_DIR/kevin-logout.json" -w '%{http_code}' \
  -X POST \
  "${AUTH_HEADERS[@]}" \
  "$BASE/api/auth/session/logout" || true)"
[[ "$LOGOUT_STATUS" == 200 ]] \
  || fail "Kevin logout returned HTTP $LOGOUT_STATUS."

# Prove a non-admin Manager cannot expand utilization beyond the backend-returned team scope.
MANAGER_EMAIL='demo.manager@ussignal.local'
MANAGER_LOGIN_RESPONSE="$(mktemp)"
MANAGER_LOGIN_PAYLOAD="$(mktemp)"
chmod 0600 "$MANAGER_LOGIN_RESPONSE" "$MANAGER_LOGIN_PAYLOAD"
manager_login_cleanup() {
  rm -f "$MANAGER_LOGIN_RESPONSE" "$MANAGER_LOGIN_PAYLOAD"
}
trap manager_login_cleanup EXIT

jq -n \
  --arg username "$MANAGER_EMAIL" \
  --arg password "$TEST_LOGIN_PASSWORD" \
  '{username:$username,password:$password}' > "$MANAGER_LOGIN_PAYLOAD"

MANAGER_LOGIN_STATUS="$(curl -sS --max-time 90 \
  -o "$MANAGER_LOGIN_RESPONSE" -w '%{http_code}' \
  -H 'Cache-Control: no-cache' \
  -H 'Content-Type: application/json' \
  -H "Origin: $BASE" \
  -H 'Sec-Fetch-Site: same-origin' \
  --data-binary @"$MANAGER_LOGIN_PAYLOAD" \
  "$BASE/api/auth/local/login" || true)"
[[ "$MANAGER_LOGIN_STATUS" == 200 ]] \
  || fail "Demo Manager protected-Test login returned HTTP $MANAGER_LOGIN_STATUS."

jq -e '
  .provider == "LOCAL"
  and .mustChangePassword == false
  and (.sessionToken | type == "string" and length > 0)
' "$MANAGER_LOGIN_RESPONSE" >/dev/null \
  || fail "Demo Manager login response did not satisfy the authenticated session contract."

MANAGER_SESSION_TOKEN="$(jq -r '.sessionToken' "$MANAGER_LOGIN_RESPONSE")"
echo "::add-mask::$MANAGER_SESSION_TOKEN"
jq 'del(.sessionToken,.token,.password)' "$MANAGER_LOGIN_RESPONSE" \
  > "$EVIDENCE_DIR/manager-utilization-login-redacted.json"
rm -f "$MANAGER_LOGIN_PAYLOAD" "$MANAGER_LOGIN_RESPONSE"
trap - EXIT

MANAGER_AUTH_HEADERS=(
  -H 'Cache-Control: no-cache'
  -H "Authorization: Bearer $MANAGER_SESSION_TOKEN"
  -H "X-ProjectPulse-Session: $MANAGER_SESSION_TOKEN"
  -H 'X-ProjectPulse-Module-Number: 003'
  -H "Origin: $BASE"
  -H 'Sec-Fetch-Site: same-origin'
)

manager_auth_get() {
  local path="$1" output="$2"
  curl -sS --max-time 120 \
    -o "$output" -w '%{http_code}' \
    "${MANAGER_AUTH_HEADERS[@]}" \
    "$BASE$path" || true
}

MANAGER_SECURITY="$EVIDENCE_DIR/manager-utilization-security-context.json"
MANAGER_SECURITY_STATUS="$(manager_auth_get '/api/security/context' "$MANAGER_SECURITY")"
[[ "$MANAGER_SECURITY_STATUS" == 200 ]] \
  || fail "Demo Manager security context returned HTTP $MANAGER_SECURITY_STATUS."
jq -e . "$MANAGER_SECURITY" >/dev/null \
  || fail "Demo Manager security context did not return JSON."

jq -e '
  [
    .roles[]?
    | if type == "object"
      then (.roleCode // .roleName // "")
      else tostring
      end
    | tostring
    | ascii_upcase
    | gsub("[ -]+"; "_")
  ] as $roles
  | [
      .permissions[]?
      | tostring
      | ascii_upcase
    ] as $permissions
  | (($roles | index("MANAGER")) != null or ($roles | index("ENGINEERING_MANAGER")) != null)
  and (($roles | index("EXECUTIVE")) == null)
  and (($roles | index("EXECUTIVE_LEADERSHIP")) == null)
  and (($roles | index("ADMINISTRATOR")) == null)
  and (($roles | index("SUPER_ADMINISTRATOR")) == null)
  and (($roles | index("GLOBAL_ADMINISTRATOR")) == null)
  and (($permissions | index("VIEW_TEAM_UTILIZATION")) != null)
  and (($permissions | index("VIEW_ORGANIZATION_UTILIZATION")) == null)
  and (($permissions | index("VIEW_ALL_UTILIZATION")) == null)
  and (($permissions | index("SYSTEM_ADMINISTRATION")) == null)
  and (($permissions | index("MANAGE_ALL")) == null)
' "$MANAGER_SECURITY" >/dev/null \
  || fail "Demo Manager is not the expected non-admin team-scoped utilization identity."

MANAGER_USER_ID="$(jq -r '.userId // empty' "$MANAGER_SECURITY")"
[[ "$MANAGER_USER_ID" =~ ^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$ ]] \
  || fail "Demo Manager security context did not expose a valid authenticated user ID."

MANAGER_SUMMARY="$EVIDENCE_DIR/manager-utilization-team-scope.json"
MANAGER_SUMMARY_STATUS="$(manager_auth_get '/api/utilization/engineering-team-summary?year=2026' "$MANAGER_SUMMARY")"
[[ "$MANAGER_SUMMARY_STATUS" == 200 ]] \
  || fail "Demo Manager utilization team summary returned HTTP $MANAGER_SUMMARY_STATUS."
jq -e . "$MANAGER_SUMMARY" >/dev/null \
  || fail "Demo Manager utilization team summary did not return JSON."

jq -e '
  .canViewEngineeringTeamUtilization == true
  and .scope == "engineering_team_scope"
  and .access.canViewAll == false
  and .access.canUseTeamScope == true
  and .access.canSelectEngineer == true
  and (.selectableEngineers | type == "array")
  and (.members | type == "array")
' "$MANAGER_SUMMARY" >/dev/null \
  || fail "Demo Manager utilization response is not constrained to engineering_team_scope."

# Resolve candidate engineer UUIDs from live authenticated identities. Never use a
# fabricated/static UUID for this least-privilege proof: the 403 must target a real user.
RESOLVED_ENGINEER_USER_ID=''
resolve_live_engineer_identity() {
  local email="$1" label="$2"
  local slug login_payload login_response security_response logout_response
  local login_status security_status logout_status candidate_session=''

  slug="$(printf '%s' "$label" | tr '[:upper:] ' '[:lower:]-' | tr -cd 'a-z0-9_-')"
  login_payload="$(mktemp)"
  login_response="$(mktemp)"
  chmod 0600 "$login_payload" "$login_response"

  candidate_cleanup() {
    local cleanup_output cleanup_status
    rm -f "$login_payload" "$login_response"
    if [[ -n "$candidate_session" ]]; then
      cleanup_output="$(mktemp)"
      cleanup_status="$(curl -sS --max-time 60 \
        -o "$cleanup_output" -w '%{http_code}' \
        -X POST \
        -H 'Cache-Control: no-cache' \
        -H "Authorization: Bearer $candidate_session" \
        -H "X-ProjectPulse-Session: $candidate_session" \
        -H 'X-ProjectPulse-Module-Number: 003' \
        -H "Origin: $BASE" \
        -H 'Sec-Fetch-Site: same-origin' \
        "$BASE/api/auth/session/logout" || true)"
      rm -f "$cleanup_output"
      if [[ "$cleanup_status" != 200 ]]; then
        echo "WARN: $label candidate-session cleanup returned HTTP $cleanup_status." >&2
      fi
      candidate_session=''
    fi
  }

  jq -n \
    --arg username "$email" \
    --arg password "$TEST_LOGIN_PASSWORD" \
    '{username:$username,password:$password}' > "$login_payload"

  login_status="$(curl -sS --max-time 90 \
    -o "$login_response" -w '%{http_code}' \
    -H 'Cache-Control: no-cache' \
    -H 'Content-Type: application/json' \
    -H "Origin: $BASE" \
    -H 'Sec-Fetch-Site: same-origin' \
    --data-binary @"$login_payload" \
    "$BASE/api/auth/local/login" || true)"
  rm -f "$login_payload"
  if [[ "$login_status" != 200 ]]; then
    candidate_cleanup
    fail "$label live identity login returned HTTP $login_status."
  fi

  candidate_session="$(jq -r '.sessionToken // empty' "$login_response" 2>/dev/null || true)"
  if [[ -n "$candidate_session" ]]; then
    echo "::add-mask::$candidate_session"
  fi
  if ! jq -e '
    .provider == "LOCAL"
    and .mustChangePassword == false
    and (.sessionToken | type == "string" and length > 0)
  ' "$login_response" >/dev/null; then
    candidate_cleanup
    fail "$label live identity login did not satisfy the session contract."
  fi

  jq 'del(.sessionToken,.token,.password)' "$login_response" \
    > "$EVIDENCE_DIR/manager-outsider-candidate-$slug-login-redacted.json"
  rm -f "$login_response"

  security_response="$EVIDENCE_DIR/manager-outsider-candidate-$slug-security-context.json"
  security_status="$(curl -sS --max-time 120 \
    -o "$security_response" -w '%{http_code}' \
    -H 'Cache-Control: no-cache' \
    -H "Authorization: Bearer $candidate_session" \
    -H "X-ProjectPulse-Session: $candidate_session" \
    -H 'X-ProjectPulse-Module-Number: 003' \
    -H "Origin: $BASE" \
    -H 'Sec-Fetch-Site: same-origin' \
    "$BASE/api/security/context" || true)"
  if [[ "$security_status" != 200 ]]; then
    candidate_cleanup
    fail "$label live identity security context returned HTTP $security_status."
  fi
  if ! jq -e '
    [
      .roles[]?
      | if type == "object"
        then (.roleCode // .roleName // "")
        else tostring
        end
      | tostring
      | ascii_upcase
      | gsub("[ -]+"; "_")
    ] as $roles
    | (($roles | index("ENGINEER")) != null or ($roles | index("ENGINEERING")) != null)
  ' "$security_response" >/dev/null; then
    candidate_cleanup
    fail "$label is not a live Engineer identity in Protected Test."
  fi

  RESOLVED_ENGINEER_USER_ID="$(jq -r '.userId // empty' "$security_response")"
  if [[ ! "$RESOLVED_ENGINEER_USER_ID" =~ ^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$ ]]; then
    candidate_cleanup
    fail "$label live security context did not expose a valid user ID."
  fi

  logout_response="$EVIDENCE_DIR/manager-outsider-candidate-$slug-logout.json"
  logout_status="$(curl -sS --max-time 60 \
    -o "$logout_response" -w '%{http_code}' \
    -X POST \
    -H 'Cache-Control: no-cache' \
    -H "Authorization: Bearer $candidate_session" \
    -H "X-ProjectPulse-Session: $candidate_session" \
    -H 'X-ProjectPulse-Module-Number: 003' \
    -H "Origin: $BASE" \
    -H 'Sec-Fetch-Site: same-origin' \
    "$BASE/api/auth/session/logout" || true)"
  if [[ "$logout_status" != 200 ]]; then
    candidate_cleanup
    fail "$label live identity logout returned HTTP $logout_status."
  fi
  candidate_session=''
  rm -f "$login_payload" "$login_response"
}

MANAGER_OUTSIDER_ID=''
MANAGER_OUTSIDER_EMAIL=''

# Kevin's UUID already came from his live security context above. Try him first.
if ! jq -e --arg candidateUserId "$KEVIN_USER_ID" \
  '([.selectableEngineers[]?.userId] | index($candidateUserId)) != null' \
  "$MANAGER_SUMMARY" >/dev/null; then
  MANAGER_OUTSIDER_ID="$KEVIN_USER_ID"
  MANAGER_OUTSIDER_EMAIL="$(printf '%s' "$KEVIN_EMAIL" | tr '[:upper:]' '[:lower:]')"
fi

# If Kevin is legitimately in scope, resolve other approved live Engineer accounts.
if [[ -z "$MANAGER_OUTSIDER_ID" ]]; then
  while IFS='|' read -r candidate_email candidate_label; do
    [[ -n "$candidate_email" ]] || continue
    resolve_live_engineer_identity "$candidate_email" "$candidate_label"
    if ! jq -e --arg candidateUserId "$RESOLVED_ENGINEER_USER_ID" \
      '([.selectableEngineers[]?.userId] | index($candidateUserId)) != null' \
      "$MANAGER_SUMMARY" >/dev/null; then
      MANAGER_OUTSIDER_ID="$RESOLVED_ENGINEER_USER_ID"
      MANAGER_OUTSIDER_EMAIL="$candidate_email"
      break
    fi
  done <<'ENGINEER_CANDIDATES'
jason.mosier@ussignal.local|Jason Mosier
jeremy.holt@ussignal.local|Jeremy Holt
demo.engineer@ussignal.local|Demo Engineer
ENGINEER_CANDIDATES
fi

[[ "$MANAGER_OUTSIDER_ID" =~ ^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$ ]] \
  || fail "Protected Test did not expose a live Engineer identity outside Demo Manager's assigned utilization team scope."
[[ -n "$MANAGER_OUTSIDER_EMAIL" ]] \
  || fail "Manager outsider probe did not resolve a live Engineer email."

MANAGER_OUTSIDER_RESPONSE="$EVIDENCE_DIR/manager-utilization-cross-team-denied.json"
MANAGER_OUTSIDER_STATUS="$(manager_auth_get "/api/utilization/engineering-team-summary?year=2026&engineerUserId=$MANAGER_OUTSIDER_ID" "$MANAGER_OUTSIDER_RESPONSE")"
[[ "$MANAGER_OUTSIDER_STATUS" == 403 ]] \
  || fail "Demo Manager cross-team utilization probe returned HTTP $MANAGER_OUTSIDER_STATUS instead of 403."
jq -e '
  .message == "Selected engineer is not available within your utilization scope."
' "$MANAGER_OUTSIDER_RESPONSE" >/dev/null \
  || fail "Demo Manager cross-team denial did not return the governed utilization-scope message."

MANAGER_SECURITY_AFTER="$EVIDENCE_DIR/manager-utilization-security-context-after-denial.json"
MANAGER_SECURITY_AFTER_STATUS="$(manager_auth_get '/api/security/context' "$MANAGER_SECURITY_AFTER")"
[[ "$MANAGER_SECURITY_AFTER_STATUS" == 200 ]] \
  || fail "Demo Manager identity was not preserved after the expected cross-team 403."
jq -e --arg managerUserId "$MANAGER_USER_ID" '.userId == $managerUserId' "$MANAGER_SECURITY_AFTER" >/dev/null \
  || fail "Demo Manager security identity changed after the expected cross-team denial."

MANAGER_LOGOUT_STATUS="$(curl -sS --max-time 60 \
  -o "$EVIDENCE_DIR/manager-utilization-logout.json" -w '%{http_code}' \
  -X POST \
  "${MANAGER_AUTH_HEADERS[@]}" \
  "$BASE/api/auth/session/logout" || true)"
[[ "$MANAGER_LOGOUT_STATUS" == 200 ]] \
  || fail "Demo Manager logout returned HTTP $MANAGER_LOGOUT_STATUS."

jq -n \
  --arg identity "$KEVIN_EMAIL" \
  --arg requestedUserId "$OTHER_ENGINEER_ID" \
  --arg effectiveUserId "$KEVIN_USER_ID" \
  --arg managerIdentity "$MANAGER_EMAIL" \
  --arg managerUserId "$MANAGER_USER_ID" \
  --arg managerOutsiderId "$MANAGER_OUTSIDER_ID" \
  --arg managerOutsiderEmail "$MANAGER_OUTSIDER_EMAIL" \
  '{
    status:"passed",
    identity:$identity,
    roleScope:"engineer_self_only",
    year:2026,
    q1:{billableHours:0,utilizationPercent:0},
    q2:{billableHours:364,utilizationPercent:75.52},
    q3:{billableHours:208,utilizationPercent:43.15},
    q4:{billableHours:0,utilizationPercent:0},
    annual:{billableHours:572,utilizationPercent:29.67},
    crossEngineerRequestStatus:200,
    crossEngineerRequestOutcome:"normalized_to_authenticated_engineer",
    crossEngineerRequestedUserId:$requestedUserId,
    crossEngineerEffectiveUserId:$effectiveUserId,
    viewAsRequestStatus:403,
    identityPreservedAfterSecurityProbes:true,
    managerIdentity:$managerIdentity,
    managerUserId:$managerUserId,
    managerRoleScope:"assigned_team_only",
    managerSummaryStatus:200,
    managerCanViewAll:false,
    managerCanUseTeamScope:true,
    managerCrossTeamRequestStatus:403,
    managerCrossTeamRequestedUserId:$managerOutsiderId,
    managerCrossTeamRequestedEmail:$managerOutsiderEmail,
    managerCrossTeamIdentitySource:"live_authenticated_security_context",
    managerCrossTeamOutcome:"denied_outside_assigned_team",
    managerIdentityPreservedAfterDenial:true,
    productionMutation:false
  }' > "$EVIDENCE_DIR/utilization-role-scoping-uat.json"

unset SESSION_TOKEN MANAGER_SESSION_TOKEN RESOLVED_ENGINEER_USER_ID TEST_LOGIN_PASSWORD

echo 'UTILIZATION_ROLE_SCOPING_PROTECTED_TEST_UAT=PASS engineer=Kevin.damisch@ussignal.local engineerScope=self-only manager=demo.manager@ussignal.local managerScope=assigned-team-only managerCrossTeam=denied liveOutsiderIdentity=true annualHours=572 annualPercent=29.67'
