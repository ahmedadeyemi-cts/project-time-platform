#!/usr/bin/env bash
set -euo pipefail

PUBLIC_BASE_URL="${PUBLIC_BASE_URL:-https://projectpulse-test.onenecklab.com}"
LOCAL_API_BASE_URL="${LOCAL_API_BASE_URL:-http://127.0.0.1:5080}"
API_PUBLISHED="${API_PUBLISHED:-/opt/project-time-platform/app/published/api}"

echo "============================================================"
echo "ProjectPulse 050 Launch Blocker Smoke"
echo "============================================================"
echo "PUBLIC_BASE_URL=$PUBLIC_BASE_URL"
echo "LOCAL_API_BASE_URL=$LOCAL_API_BASE_URL"
echo "API_PUBLISHED=$API_PUBLISHED"
echo "TIME=$(date -Is)"
echo

failures=0

check_status_not_success() {
  local method="$1"
  local url="$2"
  local body="${3:-}"

  local tmp
  tmp="$(mktemp)"

  local code
  if [ "$method" = "POST" ]; then
    code="$(curl -ksS -o "$tmp" -w '%{http_code}' -X POST -H 'Content-Type: application/json' --data "$body" "$url" || true)"
  else
    code="$(curl -ksS -o "$tmp" -w '%{http_code}' "$url" || true)"
  fi

  echo "$method $url -> $code"
  head -c 500 "$tmp" || true
  echo
  rm -f "$tmp"

  case "$code" in
    200|201|202|204)
      echo "ERROR: Launch-blocker smoke expected non-success for unauthenticated request."
      failures=$((failures + 1))
      ;;
  esac
}

check_status_equals() {
  local method="$1"
  local url="$2"
  local expected="$3"
  local body="${4:-}"

  local tmp
  tmp="$(mktemp)"

  local code
  if [ "$method" = "POST" ]; then
    code="$(curl -ksS -o "$tmp" -w '%{http_code}' -X POST -H 'Content-Type: application/json' --data "$body" "$url" || true)"
  else
    code="$(curl -ksS -o "$tmp" -w '%{http_code}' "$url" || true)"
  fi

  echo "$method $url -> $code"
  head -c 500 "$tmp" || true
  echo
  rm -f "$tmp"

  if [ "$code" != "$expected" ]; then
    echo "ERROR: Expected $expected but got $code for $url"
    failures=$((failures + 1))
  fi
}

echo "============================================================"
echo "Health"
echo "============================================================"
check_status_equals GET "$LOCAL_API_BASE_URL/health" 200

echo
echo "============================================================"
echo "PP-C1 dev-login/auth-shortcut candidates must not succeed"
echo "============================================================"
for path in \
  "/api/dev-login" \
  "/api/auth/dev-login" \
  "/api/development-login" \
  "/api/auth/development-login" \
  "/api/development/session" \
  "/api/dev/session" \
  "/api/debug-login" \
  "/api/auth/debug-login" \
  "/api/mint-session" \
  "/api/impersonate"
do
  check_status_not_success POST "$PUBLIC_BASE_URL$path" '{"email":"demo@example.com"}'
done

echo
echo "============================================================"
echo "PP-C2/critical protected routes must require a session"
echo "============================================================"
for path in \
  "/api/approvals" \
  "/api/approval" \
  "/api/manager/approvals" \
  "/api/time" \
  "/api/time-entries" \
  "/api/timesheet" \
  "/api/timesheets" \
  "/api/project-closeout/email/send" \
  "/api/accounting/export" \
  "/api/admin/user-admin/users/profile" \
  "/api/profile/preferences" \
  "/api/profile/preferences/backup-readiness" \
  "/api/profile/preferences/production-validation"
do
  check_status_not_success POST "$PUBLIC_BASE_URL$path" '{}'
done

echo
echo "============================================================"
echo "PP-C6 release manifest"
echo "============================================================"
manifest="$API_PUBLISHED/projectpulse-release-manifest.json"
if [ ! -f "$manifest" ]; then
  echo "ERROR: Missing release manifest: $manifest"
  failures=$((failures + 1))
else
  echo "Release manifest present:"
  cat "$manifest"
  echo
fi

echo
echo "============================================================"
echo "050 smoke result"
echo "============================================================"
if [ "$failures" -ne 0 ]; then
  echo "FAILED: $failures launch-blocker smoke check(s) failed."
  exit 1
fi

echo "PASSED: 050 launch-blocker smoke checks passed."
