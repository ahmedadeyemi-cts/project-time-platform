#!/usr/bin/env bash
set -Eeuo pipefail

cd /opt/project-time-platform/app/project-time-platform

SSO_RESPONSE=$(curl -sS --max-time 20 -X POST http://127.0.0.1:5080/api/auth/sso/dev-login \
  -H "Content-Type: application/json" \
  -d '{"email":"ahmed.adeyemi@ussignal.com"}')

SSO_TOKEN=$(echo "$SSO_RESPONSE" | jq -r '.sessionToken // empty')

if [ -z "$SSO_TOKEN" ]; then
  echo "Unable to obtain SSO token."
  exit 1
fi

echo "Admin SSO token length: ${#SSO_TOKEN}"

ENDPOINTS=(
  "/api/audit-history/summary"
  "/api/audit-history/events?limit=10"
  "/api/workflow/action-capabilities"
  "/api/dashboard/module-visibility-smoke"
  "/api/export-packages/readiness-summary"
  "/api/workflow/reconciliation-workbench"
  "/api/workflow/lock-evidence"
  "/api/security/role-access-matrix"
  "/api/demo/readiness-command-center"
  "/api/workflow/validation-rules"
  "/api/workflow/operations-center"
)

for path in "${ENDPOINTS[@]}"; do
  echo
  echo "--- $path ---"
  curl -sS --max-time 30 "http://127.0.0.1:5080$path" \
    -H "X-ProjectPulse-Session: $SSO_TOKEN" \
    -o /tmp/019m-az-bj-endpoint.json \
    -w "HTTP %{http_code} bytes %{size_download}\n"

  cat /tmp/019m-az-bj-endpoint.json | jq '{
    status: .status,
    module: .module,
    count: .count,
    summary: .summary,
    access: .access,
    firstItem: (.events[0] // .expectations[0] // .packages[0] // .groups[0] // .lockedItems[0] // .matrix[0] // .checks[0] // .rules[0] // null)
  }' 2>/dev/null || head -c 1200 /tmp/019m-az-bj-endpoint.json
done

echo
echo "--- Dry run POST ---"
curl -sS --max-time 30 "http://127.0.0.1:5080/api/workflow/actions/dry-run" \
  -X POST \
  -H "X-ProjectPulse-Session: $SSO_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"action":"accounting_reconcile","weekStart":"2026-06-21","weekEnd":"2026-07-04"}' \
  -o /tmp/019m-az-bj-dry-run.json \
  -w "HTTP %{http_code} bytes %{size_download}\n"

cat /tmp/019m-az-bj-dry-run.json | jq .

echo
echo "--- Engineer denial checks ---"
ENGINEER_ONLY_ID="$(sudo -u postgres psql -d ProjectPulse -At <<'SQL'
WITH user_roles AS (
    SELECT
        u.user_id,
        STRING_AGG(r.role_code, ',' ORDER BY r.role_code) AS roles
    FROM app_users u
    JOIN app_user_role_assignments ura
      ON ura.user_id = u.user_id
     AND ura.is_active = TRUE
    JOIN app_roles r
      ON r.app_role_id = ura.app_role_id
     AND r.is_active = TRUE
    WHERE u.is_active = TRUE
    GROUP BY u.user_id
)
SELECT user_id
FROM user_roles
WHERE roles LIKE '%ENGINEER%'
  AND roles NOT LIKE '%ADMINISTRATOR%'
  AND roles NOT LIKE '%PROJECT_TEAM_COORDINATOR%'
  AND roles NOT LIKE '%MANAGER%'
  AND roles NOT LIKE '%PROJECT_MANAGER%'
  AND roles NOT LIKE '%PROJECT_MANAGEMENT%'
ORDER BY user_id
LIMIT 1;
SQL
)"

if [ -n "$ENGINEER_ONLY_ID" ]; then
  for path in \
    "/api/export-packages/readiness-summary" \
    "/api/workflow/reconciliation-workbench" \
    "/api/security/role-access-matrix" \
    "/api/workflow/actions/dry-run"
  do
    echo
    echo "--- Engineer View-As $path ---"

    if [[ "$path" == "/api/workflow/actions/dry-run" ]]; then
      curl -sS --max-time 30 "http://127.0.0.1:5080$path" \
        -X POST \
        -H "X-ProjectPulse-Session: $SSO_TOKEN" \
        -H "X-ProjectPulse-View-As-User: $ENGINEER_ONLY_ID" \
        -H "Content-Type: application/json" \
        -d '{"action":"accounting_reconcile"}' \
        -o /tmp/019m-az-bj-engineer.json \
        -w "HTTP %{http_code} bytes %{size_download}\n"
    else
      curl -sS --max-time 30 "http://127.0.0.1:5080$path" \
        -H "X-ProjectPulse-Session: $SSO_TOKEN" \
        -H "X-ProjectPulse-View-As-User: $ENGINEER_ONLY_ID" \
        -o /tmp/019m-az-bj-engineer.json \
        -w "HTTP %{http_code} bytes %{size_download}\n"
    fi

    cat /tmp/019m-az-bj-engineer.json | jq . 2>/dev/null || head -c 800 /tmp/019m-az-bj-engineer.json
  done
fi

echo
echo "--- Dashboard registry grep ---"
grep -nE "Audit History Events|Workflow Action Capabilities|Dashboard Module Visibility Smoke|Export Package Readiness Summary|Accounting Reconciliation Workbench|Locked Period Audit Evidence|Role Access Matrix|Demo Readiness Command Center|Workflow Validation Rules|Workflow Operations Center|Sprint Automation Validation" \
  src/frontend/project-time-web/src/App.jsx \
  | sed -n '1,220p'
