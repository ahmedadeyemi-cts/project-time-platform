#!/usr/bin/env bash
set -Eeuo pipefail

echo "============================================================"
echo "021 Production Readiness Smoke"
echo "============================================================"
date -u
echo

OVERALL_STATUS=0

check_service() {
  local service_name="$1"
  echo "Service: ${service_name}"
  if systemctl is-active --quiet "${service_name}"; then
    echo "  OK: active"
  else
    echo "  FAIL: not active"
    OVERALL_STATUS=1
  fi
}

check_endpoint() {
  local name="$1"
  local url="$2"
  local expected="$3"
  local code
  code="$(curl -k -s -o /dev/null -w "%{http_code}" "${url}" || true)"
  echo "Endpoint: ${name}"
  echo "  URL: ${url}"
  echo "  Expected: ${expected}; Actual: ${code}"
  if [ "${code}" != "${expected}" ]; then
    OVERALL_STATUS=1
  fi
}

echo "============================================================"
echo "Service checks"
echo "============================================================"
check_service "projecttime-api.service"
check_service "projecttime-frontend-public.service"
check_service "nginx.service"
check_service "postgresql.service"

echo "============================================================"
echo "Endpoint checks"
echo "============================================================"
check_endpoint "API health" "http://127.0.0.1:5080/health" "200"
check_endpoint "API version" "http://127.0.0.1:5080/api/version" "200"
check_endpoint "Production readiness command center protected access" "http://127.0.0.1:5080/api/production/readiness-command-center" "401"
check_endpoint "Workflow operational readiness protected access" "http://127.0.0.1:5080/api/workflow/operational-readiness" "401"
check_endpoint "Manager approvals protected access" "http://127.0.0.1:5080/api/manager/approvals" "401"
check_endpoint "Audit history protected access" "http://127.0.0.1:5080/api/audit/history" "401"
check_endpoint "Public frontend" "https://projectpulse-test.onenecklab.com" "200"

echo "============================================================"
echo "Git revision"
echo "============================================================"
git -C /opt/project-time-platform/app/project-time-platform branch --show-current || true
git -C /opt/project-time-platform/app/project-time-platform log --oneline -5 || true

echo "============================================================"
echo "Final smoke status"
echo "============================================================"
if [ "${OVERALL_STATUS}" = "0" ]; then
  echo "PASS: production readiness smoke checks passed."
else
  echo "FAIL: one or more production readiness smoke checks failed."
fi

exit "${OVERALL_STATUS}"
