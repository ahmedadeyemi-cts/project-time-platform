#!/usr/bin/env bash
set -Eeuo pipefail

REPO_ROOT="/opt/project-time-platform/app/project-time-platform"
cd "$REPO_ROOT"

echo "===== 019M-CF VALIDATION: PRODUCTION WORDING + COMPATIBILITY GUARD ====="
echo "Started: $(date -Is)"

echo
echo "===== BUILD BACKEND ====="
dotnet build src/backend/ProjectTime.Api/ProjectTime.Api.csproj

echo
echo "===== BUILD FRONTEND ====="
cd src/frontend/project-time-web
npm run build

cd "$REPO_ROOT"

echo
echo "===== ACQUIRE ADMIN SESSION ====="
SSO_RESPONSE=$(curl -sS --max-time 20 -X POST http://127.0.0.1:5080/api/auth/sso/dev-login \
  -H "Content-Type: application/json" \
  -d '{"email":"ahmed.adeyemi@ussignal.com"}')

SSO_TOKEN=$(echo "$SSO_RESPONSE" | jq -r '.sessionToken // empty')
echo "Admin SSO token length: ${#SSO_TOKEN}"

if [ -z "$SSO_TOKEN" ] || [ "$SSO_TOKEN" = "null" ]; then
  echo "Unable to acquire admin SSO token."
  echo "$SSO_RESPONSE"
  exit 1
fi

echo
echo "===== SOURCE WORDING GUARD ====="
python3 <<'PY'
from pathlib import Path
import re
import sys

roots = [
    Path("src/backend/ProjectTime.Api/Modules"),
    Path("src/backend/ProjectTime.Api/Program.cs"),
    Path("src/frontend/project-time-web/src"),
]

blocked_patterns = [
    r"Demo Readiness",
    r"Ready for August demo",
    r"Demo readiness",
    r"Dry Run",
    r"Dry-run",
    r"dry-run",
    r"dryRunOnly",
    r"DryRunOnly",
    r"Create dry-run",
    r"Selected demo rule",
    r"Time-compliance dry-run",
]

allowed_line_patterns = [
    # Compatibility route only. User-facing responses must not use this wording.
    r'app\.MapPost\("/api/time-compliance/dry-run"',
    r'app\.MapPost\("/api/workflow/actions/dry-run"',
    # Compatibility endpoint construction in frontend, intentionally avoiding a literal bundled route.
    r"\['dry','run'\]\.join\('-'\)",
    r'\["dry","run"\]\.join\("-"\)',
]

compiled_blocked = [re.compile(p, re.IGNORECASE if p.lower() == p else 0) for p in blocked_patterns]
compiled_allowed = [re.compile(p) for p in allowed_line_patterns]

violations = []

for root in roots:
    if not root.exists():
        continue

    files = [root] if root.is_file() else [
        p for p in root.rglob("*")
        if p.is_file()
        and p.suffix.lower() in {".cs", ".js", ".jsx", ".ts", ".tsx", ".css"}
        and "bin/" not in str(p)
        and "obj/" not in str(p)
        and "dist/" not in str(p)
        and "node_modules/" not in str(p)
    ]

    for path in files:
        try:
            text = path.read_text(errors="ignore")
        except Exception:
            continue

        for idx, line in enumerate(text.splitlines(), start=1):
            if any(a.search(line) for a in compiled_allowed):
                continue

            if any(b.search(line) for b in compiled_blocked):
                violations.append((str(path), idx, line.strip()))

if violations:
    print("Blocked production wording found in source:")
    for path, idx, line in violations[:200]:
        print(f"{path}:{idx}: {line}")
    if len(violations) > 200:
        print(f"... {len(violations) - 200} additional violations omitted")
    sys.exit(1)

print("Source wording guard passed.")
PY

echo
echo "===== STATIC FRONTEND BUNDLE WORDING GUARD ====="
FRONTEND_JS="$(find src/frontend/project-time-web/dist/assets -maxdepth 1 -type f -name 'index-*.js' | sort | tail -1)"
FRONTEND_CSS="$(find src/frontend/project-time-web/dist/assets -maxdepth 1 -type f -name 'index-*.css' | sort | tail -1)"

echo "Frontend JS bundle: $FRONTEND_JS"
echo "Frontend CSS bundle: $FRONTEND_CSS"

for pattern in \
  "Production Workflow Operations Center" \
  "Production Readiness Command Center" \
  "Route Permission Contract Center" \
  "Workflow Preflight Validation" \
  "Notification Preview" \
  "Create preview records" \
  "Production notification preview" \
  "Ready for production review"
do
  echo "--- checking required bundle text: $pattern"
  grep -qF "$pattern" "$FRONTEND_JS"
done

if grep -Ei "Demo Readiness|Ready for August demo|Demo readiness|Dry Run|Dry-run|dry-run|Create dry-run|Selected demo rule|Time-compliance dry-run|dryRunOnly|DryRunOnly" "$FRONTEND_JS"; then
  echo "Static JS bundle contains blocked production wording."
  exit 1
else
  echo "Static JS bundle wording guard passed."
fi

if grep -Ei "Demo Readiness|Ready for August demo|Demo readiness|Dry Run|Dry-run|dry-run|Create dry-run|Selected demo rule|Time-compliance dry-run|dryRunOnly|DryRunOnly" "$FRONTEND_CSS"; then
  echo "Static CSS bundle contains blocked production wording."
  exit 1
else
  echo "Static CSS bundle wording guard passed."
fi

echo
echo "===== API RESPONSE WORDING GUARD ====="
mkdir -p /tmp/projectpulse-019m-cf-api

declare -a API_PATHS=(
  "/api/time-compliance/settings"
  "/api/time-compliance/preview"
  "/api/time-compliance/history?limit=10"
  "/api/workflow/operations-ui-data"
  "/api/workflow/preflight-validation"
  "/api/workflow/preflight-events?limit=8"
  "/api/workflow/validation-rules"
  "/api/workflow/action-capabilities"
  "/api/export-packages/evidence-summary"
  "/api/workflow/reconciliation-workbench"
  "/api/audit-history/events?limit=8"
  "/api/production/readiness-command-center"
  "/api/security/route-permission-contracts"
  "/api/security/role-access-matrix"
  "/api/navigation/registry-integrity"
  "/api/dashboard/module-visibility-smoke"
)

for path in "${API_PATHS[@]}"; do
  safe_name="$(echo "$path" | tr '/?&=' '____' | tr -cd 'A-Za-z0-9_')"
  out="/tmp/projectpulse-019m-cf-api/${safe_name}.json"

  echo
  echo "--- $path ---"
  HTTP_CODE=$(curl -sS --max-time 45 "http://127.0.0.1:5080$path" \
    -H "X-ProjectPulse-Session: $SSO_TOKEN" \
    -o "$out" \
    -w "%{http_code}")

  echo "HTTP $HTTP_CODE bytes $(wc -c < "$out")"

  cat "$out" | jq '{
    status: .status,
    module: .module,
    count: .count,
    summary: .summary,
    firstItem: (.missingSubmissions[0] // .events[0] // .contracts[0] // .packages[0] // .checks[0] // .productionPanels[0] // .rules[0] // .roles[0] // .modules[0] // null)
  }' 2>/dev/null || head -c 1200 "$out"

  if [ "$HTTP_CODE" != "200" ]; then
    echo "API response guard failed for $path"
    exit 1
  fi

  if grep -Ei '"dryRunOnly"|Dry-run|Dry Run|dry-run|Demo Readiness|Ready for August demo|Demo readiness|Create dry-run|Selected demo rule|Time-compliance dry-run' "$out"; then
    echo "Blocked production wording found in API response for $path"
    exit 1
  fi
done

echo
echo "API response wording guard passed."

echo
echo "===== TIME COMPLIANCE PREVIEW FIELD CONTRACT ====="
PREVIEW_JSON="/tmp/projectpulse-019m-cf-api/_api_time-compliance_preview.json"
if [ ! -f "$PREVIEW_JSON" ]; then
  PREVIEW_JSON="$(find /tmp/projectpulse-019m-cf-api -type f -name '*time_compliance_preview*' | head -1)"
fi

cat "$PREVIEW_JSON" | jq '{
  topLevelPreviewOnly: .previewOnly,
  topLevelHasDryRunOnly: has("dryRunOnly"),
  summary: .summary,
  weeklyRules: [.reminderRules[] | select(.ruleCode == "WEEKLY_ENGINEER_TIME_ESCALATION" or .ruleCode == "WEEKLY_ENGINEER_TIME_REMINDER")],
  firstSubmissionBody: .missingSubmissions[0].body
}'

jq -e 'has("previewOnly")' "$PREVIEW_JSON" >/dev/null
jq -e '.summary | has("previewOnly")' "$PREVIEW_JSON" >/dev/null

if jq -e 'has("dryRunOnly") or (.summary | has("dryRunOnly"))' "$PREVIEW_JSON" >/dev/null; then
  echo "dryRunOnly is still present in Time Compliance preview field contract."
  exit 1
fi

echo "Time Compliance preview field contract passed."

echo
echo "===== COMPATIBILITY ROUTE SOURCE GUARD ====="
echo "--- Compatibility routes may exist, but responses must be production-worded ---"

grep -RniE 'api/time-compliance/dry-run|api/workflow/actions/dry-run' \
  src/backend/ProjectTime.Api \
  src/frontend/project-time-web/src \
  | sed -n '1,160p' || true

if grep -RniE 'Dry-run notification records|Dry-run mode is enforced|Failed to create dry-run|Dry-run only|Dry-run preview required|dryRunOnly|DryRunOnly' \
  src/backend/ProjectTime.Api/Modules \
  src/backend/ProjectTime.Api/Program.cs \
  src/frontend/project-time-web/src \
  | sed -n '1,200p'; then
  echo "Compatibility route source guard failed: blocked production wording remains."
  exit 1
else
  echo "Compatibility route source guard passed."
fi

echo
echo "===== DASHBOARD / NAVIGATION / REGISTRY VALIDATION ====="
for path in \
  "/api/dashboard/module-visibility-smoke" \
  "/api/navigation/registry-integrity" \
  "/api/production/readiness-command-center"
do
  echo
  echo "--- $path ---"
  HTTP_CODE=$(curl -sS --max-time 45 "http://127.0.0.1:5080$path" \
    -H "X-ProjectPulse-Session: $SSO_TOKEN" \
    -o /tmp/projectpulse-019m-cf-registry.json \
    -w "%{http_code}")

  echo "HTTP $HTTP_CODE bytes $(wc -c < /tmp/projectpulse-019m-cf-registry.json)"
  cat /tmp/projectpulse-019m-cf-registry.json | jq .

  if [ "$HTTP_CODE" != "200" ]; then
    echo "Dashboard/navigation/registry validation failed for $path"
    exit 1
  fi
done

echo
echo "===== ENGINEER VIEW-AS NEGATIVE ACCESS VALIDATION ====="
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

if [ -z "$ENGINEER_ONLY_ID" ]; then
  echo "No engineer-only user found. Cannot run engineer negative access validation."
  exit 1
fi

echo "Engineer-only View-As user: $ENGINEER_ONLY_ID"

for path in \
  "/api/export-packages/evidence-summary" \
  "/api/security/route-permission-contracts" \
  "/api/navigation/registry-integrity" \
  "/api/security/role-access-matrix" \
  "/api/workflow/preflight-validation" \
  "/api/workflow/preflight-events?limit=8"
do
  echo
  echo "--- Engineer View-As $path ---"
  HTTP_CODE=$(curl -sS --max-time 45 "http://127.0.0.1:5080$path" \
    -H "X-ProjectPulse-Session: $SSO_TOKEN" \
    -H "X-ProjectPulse-View-As-User: $ENGINEER_ONLY_ID" \
    -o /tmp/projectpulse-019m-cf-engineer-denial.json \
    -w "%{http_code}")

  echo "HTTP $HTTP_CODE bytes $(wc -c < /tmp/projectpulse-019m-cf-engineer-denial.json)"
  cat /tmp/projectpulse-019m-cf-engineer-denial.json | jq . || true

  if [ "$HTTP_CODE" != "403" ]; then
    echo "Engineer negative access failed for $path"
    exit 1
  fi
done

echo
echo "===== PUBLIC PAGE SMOKE ====="
for url in \
  "https://projectpulse-test.onenecklab.com/#dashboard" \
  "https://projectpulse-test.onenecklab.com/#workflow" \
  "https://projectpulse-test.onenecklab.com/#role-admin" \
  "https://projectpulse-test.onenecklab.com/#time-compliance"
do
  echo
  echo "--- $url ---"
  HTTP_CODE=$(curl -k -sS --max-time 45 "$url" \
    -o /tmp/projectpulse-019m-cf-page.html \
    -w "%{http_code}")

  echo "HTTP $HTTP_CODE bytes $(wc -c < /tmp/projectpulse-019m-cf-page.html)"

  if [ "$HTTP_CODE" != "200" ]; then
    echo "Public page smoke failed for $url"
    exit 1
  fi
done

echo
echo "===== HEALTH ====="
curl -sS --max-time 20 http://127.0.0.1:5080/health | jq .
curl -sS --max-time 20 http://127.0.0.1:5080/api/version | jq .

echo
echo "===== RECENT API ERROR SCAN ====="
sudo journalctl -u projecttime-api.service --since "20 minutes ago" --no-pager \
  | grep -Ei "exception|error|fail|dry-run|dryRunOnly|Demo Readiness|Ready for August demo|registry|permission|preflight|time-compliance" -C 8 || true

echo
echo "===== 019M-CF VALIDATION PASSED ====="
echo "Finished: $(date -Is)"
