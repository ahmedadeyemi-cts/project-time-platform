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
  "/api/workflow/preflight-validation?weekStart=2026-06-21&weekEnd=2026-07-04"
  "/api/workflow/preflight-events?limit=10"
  "/api/production/readiness-command-center"
  "/api/security/route-permission-contracts"
  "/api/navigation/registry-integrity"
  "/api/export-packages/evidence-summary"
  "/api/workflow/operations-ui-data"
  "/api/audit-history/events?limit=10"
  "/api/workflow/operations-center"
)

for path in "${ENDPOINTS[@]}"; do
  echo
  echo "--- $path ---"
  HTTP_CODE=$(curl -sS --max-time 30 "http://127.0.0.1:5080$path" \
    -H "X-ProjectPulse-Session: $SSO_TOKEN" \
    -o /tmp/019m-bl-bu-endpoint.json \
    -w "%{http_code}")

  echo "HTTP $HTTP_CODE bytes $(wc -c < /tmp/019m-bl-bu-endpoint.json)"
  cat /tmp/019m-bl-bu-endpoint.json | jq '{
    status: .status,
    module: .module,
    count: .count,
    summary: .summary,
    access: .access,
    firstItem: (.events[0] // .contracts[0] // .packages[0] // .checks[0] // .productionPanels[0] // .rules[0] // null)
  }' 2>/dev/null || head -c 1200 /tmp/019m-bl-bu-endpoint.json

  if [ "$HTTP_CODE" != "200" ]; then
    echo "Endpoint validation failed for $path"
    exit 1
  fi
done

echo
echo "--- Production preflight validation run ---"
HTTP_CODE=$(curl -sS --max-time 30 "http://127.0.0.1:5080/api/workflow/preflight-validation/run" \
  -X POST \
  -H "X-ProjectPulse-Session: $SSO_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"action":"accounting_reconciliation_review","weekStart":"2026-06-21","weekEnd":"2026-07-04"}' \
  -o /tmp/019m-bl-bu-preflight-run.json \
  -w "%{http_code}")

echo "HTTP $HTTP_CODE bytes $(wc -c < /tmp/019m-bl-bu-preflight-run.json)"
cat /tmp/019m-bl-bu-preflight-run.json | jq .

if [ "$HTTP_CODE" != "200" ]; then
  echo "Production preflight validation run failed."
  exit 1
fi

echo
echo "--- Engineer negative access checks ---"
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
    "/api/export-packages/evidence-summary" \
    "/api/security/route-permission-contracts" \
    "/api/navigation/registry-integrity" \
    "/api/workflow/preflight-validation/run"
  do
    echo
    echo "--- Engineer View-As $path ---"

    if [[ "$path" == "/api/workflow/preflight-validation/run" ]]; then
      HTTP_CODE=$(curl -sS --max-time 30 "http://127.0.0.1:5080$path" \
        -X POST \
        -H "X-ProjectPulse-Session: $SSO_TOKEN" \
        -H "X-ProjectPulse-View-As-User: $ENGINEER_ONLY_ID" \
        -H "Content-Type: application/json" \
        -d '{"action":"accounting_reconciliation_review"}' \
        -o /tmp/019m-bl-bu-engineer.json \
        -w "%{http_code}")
    else
      HTTP_CODE=$(curl -sS --max-time 30 "http://127.0.0.1:5080$path" \
        -H "X-ProjectPulse-Session: $SSO_TOKEN" \
        -H "X-ProjectPulse-View-As-User: $ENGINEER_ONLY_ID" \
        -o /tmp/019m-bl-bu-engineer.json \
        -w "%{http_code}")
    fi

    echo "HTTP $HTTP_CODE bytes $(wc -c < /tmp/019m-bl-bu-engineer.json)"
    cat /tmp/019m-bl-bu-engineer.json | jq . 2>/dev/null || head -c 1000 /tmp/019m-bl-bu-engineer.json

    if [ "$HTTP_CODE" != "403" ]; then
      echo "Engineer negative access check failed for $path"
      exit 1
    fi
  done
else
  echo "No engineer-only user found for negative access checks."
fi

echo
echo "--- Production naming grep ---"
grep -nE "Production Readiness Command Center|Workflow Preflight Validation|Production Export Evidence|Route Permission Contracts|Navigation Registry Integrity Guard|Engineer Negative Access Smoke" \
  src/frontend/project-time-web/src/App.jsx \
  src/backend/ProjectTime.Api/Program.cs \
  database/migrations/019m-bl-through-bu-production-hardening.sql \
  | sed -n '1,240p'
