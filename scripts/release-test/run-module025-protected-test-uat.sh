#!/usr/bin/env bash
set -Eeuo pipefail

BASE="${BASE:-}"
TEST_LOGIN_PASSWORD="${TEST_LOGIN_PASSWORD:-}"
EVIDENCE_DIR="${EVIDENCE_DIR:-/tmp/systemwide-enterprise-reliability-test-evidence}"
MANAGER_EMAIL='demo.manager@ussignal.local'

fail() { echo "ERROR: $*" >&2; exit 1; }
[[ "$BASE" == https://phd-west-test.onenecklab.com* ]] || fail 'Module 025 UAT is restricted to protected Test.'
BASE="${BASE%/}"
[[ ${#TEST_LOGIN_PASSWORD} -ge 12 ]] || fail 'The protected-Test login credential is required.'
command -v curl >/dev/null || fail 'curl is required.'
command -v jq >/dev/null || fail 'jq is required.'
install -d -m 0700 "$EVIDENCE_DIR"

LOGIN_PAYLOAD="$(mktemp)"
LOGIN_RESPONSE="$(mktemp)"
trap 'rm -f "$LOGIN_PAYLOAD" "$LOGIN_RESPONSE"' EXIT INT TERM
chmod 0600 "$LOGIN_PAYLOAD" "$LOGIN_RESPONSE"
jq -n --arg username "$MANAGER_EMAIL" --arg password "$TEST_LOGIN_PASSWORD" '{username:$username,password:$password}' > "$LOGIN_PAYLOAD"
LOGIN_STATUS="$(curl -sS --http1.1 --connect-timeout 30 --max-time 90 \
  -o "$LOGIN_RESPONSE" -w '%{http_code}' \
  -H 'Cache-Control: no-cache' -H 'Content-Type: application/json' \
  -H "Origin: $BASE" -H 'Sec-Fetch-Site: same-origin' \
  --data-binary @"$LOGIN_PAYLOAD" "$BASE/api/auth/local/login" || true)"
[[ "$LOGIN_STATUS" == 200 ]] || fail "Module 025 demo-manager login returned HTTP $LOGIN_STATUS."
jq -e '.provider == "LOCAL" and .mustChangePassword == false and (.sessionToken | type == "string" and length > 0)' "$LOGIN_RESPONSE" >/dev/null \
  || fail 'Module 025 demo-manager login did not satisfy the protected-Test session contract.'
SESSION="$(jq -r '.sessionToken' "$LOGIN_RESPONSE")"
echo "::add-mask::$SESSION"
jq 'del(.sessionToken)' "$LOGIN_RESPONSE" > "$EVIDENCE_DIR/module025-manager-login-redacted.json"

BOOTSTRAP_STATUS="$(curl -sS --http1.1 --connect-timeout 30 --max-time 120 \
  -o "$EVIDENCE_DIR/module025-bootstrap.json" -w '%{http_code}' \
  -H 'Cache-Control: no-cache' \
  -H "Authorization: Bearer $SESSION" \
  -H "X-ProjectPulse-Session: $SESSION" \
  -H 'X-ProjectPulse-Module-Number: 025' \
  -H "Origin: $BASE" -H 'Sec-Fetch-Site: same-origin' \
  "$BASE/api/module025/sow-gsd/bootstrap" || true)"
[[ "$BOOTSTRAP_STATUS" == 200 ]] || fail "Module 025 protected-Test bootstrap returned HTTP $BOOTSTRAP_STATUS."
jq -e '
  .status == "module025_workspace_ready"
  and .module == "025"
  and .migration == "099_module025_sow_gsd_workspace"
  and .contract == "module025-sow-gsd-workspace-v1-20260830"
  and .access.isManager == true
  and .access.managerScopeReadOnly == true
  and ((.commercialModels // []) | map(.key) | sort) == ["fixed","time_and_materials"]
  and ((.customerPrograms // []) | map(.key) | sort) == ["hyundai","standard","toyota"]
  and ((.phases // []) | map(.code)) == ["plan","design","implement","validate","release"]
  and .autosave.enabled == true
  and .stateChanged == false
' "$EVIDENCE_DIR/module025-bootstrap.json" >/dev/null \
  || fail 'Module 025 protected-Test bootstrap violated the SOW/GSD workspace contract.'

LOGOUT_STATUS="$(curl -sS --http1.1 --connect-timeout 30 --max-time 60 \
  -o "$EVIDENCE_DIR/module025-manager-logout.json" -w '%{http_code}' \
  -X POST \
  -H "Authorization: Bearer $SESSION" \
  -H "X-ProjectPulse-Session: $SESSION" \
  -H 'X-ProjectPulse-Module-Number: 025' \
  -H "Origin: $BASE" -H 'Sec-Fetch-Site: same-origin' \
  "$BASE/api/auth/session/logout" || true)"
[[ "$LOGOUT_STATUS" == 200 ]] || fail "Module 025 demo-manager logout returned HTTP $LOGOUT_STATUS."
unset SESSION TEST_LOGIN_PASSWORD

jq -n --arg identity "$MANAGER_EMAIL" '{status:"passed",module:"025",migration099:"applied_and_verified",identity:$identity,managerScopeReadOnly:true,mutationsPerformed:false,productionMutation:false}' \
  > "$EVIDENCE_DIR/module025-protected-test-uat.json"
echo 'MODULE025_PROTECTED_TEST_UAT=PASSED managerScopeReadOnly=true mutationsPerformed=false productionMutation=false'
