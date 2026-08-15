#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PORT="${PROJECTPULSE_STARTUP_TEST_PORT:-5099}"
BASE="http://127.0.0.1:${PORT}"
LOG="/tmp/projectpulse-api-startup-${GITHUB_RUN_ID:-local}-${GITHUB_RUN_ATTEMPT:-1}.log"
PID=""

cleanup() {
  local status=$?
  trap - EXIT INT TERM
  if [[ -n "$PID" ]] && kill -0 "$PID" >/dev/null 2>&1; then
    kill "$PID" >/dev/null 2>&1 || true
    wait "$PID" >/dev/null 2>&1 || true
  fi
  if (( status != 0 )); then
    echo 'PROJECTPULSE_API_STARTUP_LOG_BEGIN' >&2
    tail -200 "$LOG" >&2 || true
    echo 'PROJECTPULSE_API_STARTUP_LOG_END' >&2
  fi
  exit "$status"
}
trap cleanup EXIT INT TERM

cd "$ROOT"

PROJECT_INTAKE_MODULE='src/backend/ProjectTime.Api/Modules/ProjectIntakeModule.cs'
grep -Fq '(Func<HttpContext, Task<IResult>>)GetOverviewAsync' "$PROJECT_INTAKE_MODULE" || {
  echo 'ERROR: Project Intake overview must use an explicit IResult route binding.' >&2
  exit 1
}
if grep -Fq 'app.MapGet("/api/project-intake/overview", GetOverviewAsync);' "$PROJECT_INTAKE_MODULE"; then
  echo 'ERROR: Project Intake overview uses the RequestDelegate binding that can discard its IResult response.' >&2
  exit 1
fi

env \
  -u PTP_DB_HOST \
  -u PTP_DB_PORT \
  -u PTP_DB_NAME \
  -u PTP_DB_USER \
  -u PTP_DB_PASSWORD \
  -u PROJECTPULSE_CONNECTION_STRING \
  -u PROJECTTIME_DATABASE_CONNECTION \
  ASPNETCORE_URLS="$BASE" \
  PROJECTPULSE_ENVIRONMENT=test \
  dotnet run \
    --project src/backend/ProjectTime.Api/ProjectTime.Api.csproj \
    --configuration Release \
    --no-build \
    >"$LOG" 2>&1 &
PID=$!

HEALTH_STATUS=""
for attempt in $(seq 1 90); do
  if ! kill -0 "$PID" >/dev/null 2>&1; then
    echo 'ERROR: ProjectTime.Api exited before its health endpoint became available.' >&2
    exit 1
  fi
  HEALTH_STATUS="$(curl -sS -o /tmp/projectpulse-startup-health.json -w '%{http_code}' "$BASE/health" || true)"
  [[ "$HEALTH_STATUS" == 200 ]] && break
  sleep 1
done

[[ "$HEALTH_STATUS" == 200 ]] || {
  echo "ERROR: /health did not become ready; last status=$HEALTH_STATUS" >&2
  exit 1
}
grep -Fq '"status":"healthy"' /tmp/projectpulse-startup-health.json || {
  echo 'ERROR: /health did not return the expected healthy JSON contract.' >&2
  cat /tmp/projectpulse-startup-health.json >&2 || true
  exit 1
}

VERSION_STATUS="$(curl -sS -o /tmp/projectpulse-startup-version.json -w '%{http_code}' "$BASE/api/version" || true)"
[[ "$VERSION_STATUS" == 200 ]] || {
  echo "ERROR: /api/version returned $VERSION_STATUS" >&2
  exit 1
}
grep -Fq '"component":"ProjectTime.Api"' /tmp/projectpulse-startup-version.json || {
  echo 'ERROR: /api/version did not return the ProjectTime.Api contract.' >&2
  cat /tmp/projectpulse-startup-version.json >&2 || true
  exit 1
}

READINESS_STATUS="$(curl -sS -o /tmp/projectpulse-startup-readiness.json -w '%{http_code}' "$BASE/health/combined-modules" || true)"
[[ "$READINESS_STATUS" == 503 ]] || {
  echo "ERROR: Combined readiness should fail closed without database configuration; status=$READINESS_STATUS" >&2
  cat /tmp/projectpulse-startup-readiness.json >&2 || true
  exit 1
}
grep -Fq 'combined_module_runtime_unavailable' /tmp/projectpulse-startup-readiness.json || {
  echo 'ERROR: Combined readiness did not return a controlled database-unavailable contract.' >&2
  cat /tmp/projectpulse-startup-readiness.json >&2 || true
  exit 1
}
grep -Fq '"operationalCountsReturned":false' /tmp/projectpulse-startup-readiness.json || {
  echo 'ERROR: Public health readiness did not explicitly suppress operational counts.' >&2
  cat /tmp/projectpulse-startup-readiness.json >&2 || true
  exit 1
}

PUBLIC_COMBINED_STATUS="$(curl -sS -o /tmp/projectpulse-startup-public-combined-readiness.json -w '%{http_code}' "$BASE/api/public/combined-modules/readiness" || true)"
[[ "$PUBLIC_COMBINED_STATUS" == 503 ]] || {
  echo "ERROR: Public combined readiness must bypass session authentication and fail closed only because the database is unavailable; status=$PUBLIC_COMBINED_STATUS" >&2
  cat /tmp/projectpulse-startup-public-combined-readiness.json >&2 || true
  exit 1
}
grep -Fq 'combined_module_runtime_unavailable' /tmp/projectpulse-startup-public-combined-readiness.json || {
  echo 'ERROR: Public combined readiness did not return the controlled database-unavailable contract.' >&2
  cat /tmp/projectpulse-startup-public-combined-readiness.json >&2 || true
  exit 1
}
grep -Fq '"operationalCountsReturned":false' /tmp/projectpulse-startup-public-combined-readiness.json || {
  echo 'ERROR: Public combined readiness did not explicitly suppress operational counts.' >&2
  cat /tmp/projectpulse-startup-public-combined-readiness.json >&2 || true
  exit 1
}

PUBLIC_EXPENSE_STATUS="$(curl -sS -o /tmp/projectpulse-startup-public-expense-readiness.json -w '%{http_code}' "$BASE/api/public/project-expenses/readiness" || true)"
[[ "$PUBLIC_EXPENSE_STATUS" == 503 ]] || {
  echo "ERROR: Public project-expense readiness must bypass session authentication and fail closed only because the database is unavailable; status=$PUBLIC_EXPENSE_STATUS" >&2
  cat /tmp/projectpulse-startup-public-expense-readiness.json >&2 || true
  exit 1
}
grep -Fq 'project_expense_runtime_unavailable' /tmp/projectpulse-startup-public-expense-readiness.json || {
  echo 'ERROR: Public project-expense readiness did not return the controlled database-unavailable contract.' >&2
  cat /tmp/projectpulse-startup-public-expense-readiness.json >&2 || true
  exit 1
}
grep -Fq '"operationalCountsReturned":false' /tmp/projectpulse-startup-public-expense-readiness.json || {
  echo 'ERROR: Public project-expense readiness did not explicitly suppress operational counts.' >&2
  cat /tmp/projectpulse-startup-public-expense-readiness.json >&2 || true
  exit 1
}

PROTECTED_COMBINED_STATUS="$(curl -sS -o /tmp/projectpulse-startup-protected-combined-readiness.json -w '%{http_code}' "$BASE/api/runtime/v2/readiness" || true)"
[[ "$PROTECTED_COMBINED_STATUS" == 401 ]] || {
  echo "ERROR: Protected combined runtime readiness must continue requiring a valid session; status=$PROTECTED_COMBINED_STATUS" >&2
  cat /tmp/projectpulse-startup-protected-combined-readiness.json >&2 || true
  exit 1
}
grep -Fq 'session_required' /tmp/projectpulse-startup-protected-combined-readiness.json || {
  echo 'ERROR: Protected combined runtime readiness did not preserve the session-required boundary.' >&2
  cat /tmp/projectpulse-startup-protected-combined-readiness.json >&2 || true
  exit 1
}

echo 'PROJECTPULSE_API_STARTUP_SMOKE=PASS health=200 version=200 publicReadinessContracts=ready protectedAuthBoundary=ready projectIntakeIResultBinding=ready operationalCountsSuppressed=true'
