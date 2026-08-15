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

OTHER_ENGINEER_RESPONSE="$EVIDENCE_DIR/kevin-other-engineer-denied.json"
OTHER_ENGINEER_STATUS="$(auth_get '/api/utilization/engineering-team-summary?year=2026&engineerUserId=11111111-1111-1111-1111-111111111111' "$OTHER_ENGINEER_RESPONSE")"
[[ "$OTHER_ENGINEER_STATUS" == 403 ]] \
  || fail "Kevin cross-engineer utilization probe returned HTTP $OTHER_ENGINEER_STATUS instead of 403."

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

jq -n \
  --arg identity "$KEVIN_EMAIL" \
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
    crossEngineerRequestStatus:403,
    viewAsRequestStatus:403,
    identityPreservedAfterOptionalFailures:true,
    productionMutation:false
  }' > "$EVIDENCE_DIR/utilization-role-scoping-uat.json"

unset SESSION_TOKEN TEST_LOGIN_PASSWORD

echo 'UTILIZATION_ROLE_SCOPING_PROTECTED_TEST_UAT=PASS identity=Kevin.damisch@ussignal.local role=Engineer scope=self-only annualHours=572 annualPercent=29.67'
