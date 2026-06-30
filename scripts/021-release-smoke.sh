#!/usr/bin/env bash
set -Eeuo pipefail

echo "============================================================"
echo "021 RELEASE SMOKE"
echo "============================================================"
date -u
echo

check_url() {
  local label="$1"
  local url="$2"
  local expected="$3"

  local body_file="/tmp/projectpulse-021-smoke-body.txt"
  local code
  local preview

  code="$(curl -k -s -o "$body_file" -w "%{http_code}" "$url")"
  preview="$(head -c 180 "$body_file" | tr '\n' ' ')"

  if [ "$code" = "$expected" ]; then
    echo "PASS: $label -> HTTP $code"
  else
    echo "CHECK: $label -> HTTP $code expected $expected :: $preview"
  fi
}

check_url "local health" "http://127.0.0.1:5080/health" "200"
check_url "local version" "http://127.0.0.1:5080/api/version" "200"
check_url "public test health" "https://projectpulse-test.onenecklab.com/health" "200"

echo
echo "Protected endpoints should return 401 without a browser session."
check_url "customers overview" "https://projectpulse-test.onenecklab.com/api/customers/overview" "401"
check_url "project intake overview" "https://projectpulse-test.onenecklab.com/api/project-intake/overview" "401"
check_url "resource assignment handoff" "https://projectpulse-test.onenecklab.com/api/project-intake/resource-assignment-handoff" "401"
check_url "workflow summary" "https://projectpulse-test.onenecklab.com/api/workflow/approval-export-summary" "401"
check_url "time exports" "https://projectpulse-test.onenecklab.com/api/time-exports" "401"
check_url "audit history summary" "https://projectpulse-test.onenecklab.com/api/audit-history/summary" "401"
check_url "production readiness" "https://projectpulse-test.onenecklab.com/api/production/readiness-command-center" "401"
check_url "dashboard module visibility" "https://projectpulse-test.onenecklab.com/api/dashboard/module-visibility-smoke" "401"
check_url "navigation registry integrity" "https://projectpulse-test.onenecklab.com/api/navigation/registry-integrity" "401"

echo
echo "============================================================"
echo "021 RELEASE SMOKE COMPLETE"
echo "============================================================"
