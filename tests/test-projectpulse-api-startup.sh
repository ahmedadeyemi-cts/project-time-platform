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

READINESS_STATUS="$(curl -sS -o /tmp/projectpulse-startup-readiness.json -w '%{http_code}' "$BASE/api/runtime/v2/readiness" || true)"
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

DELETE_STATUS="$(curl -sS -X DELETE \
  -H 'Content-Type: application/json' \
  -d '{"reason":"startup contract test"}' \
  -o /tmp/projectpulse-startup-delete.json \
  -w '%{http_code}' \
  "$BASE/api/project-expenses/uploads/00000000-0000-0000-0000-000000000001" || true)"
[[ "$DELETE_STATUS" == 500 || "$DELETE_STATUS" == 503 ]] || {
  echo "ERROR: Startup-safe DELETE route was not registered; status=$DELETE_STATUS" >&2
  cat /tmp/projectpulse-startup-delete.json >&2 || true
  exit 1
}

echo 'PROJECTPULSE_API_STARTUP_SMOKE=PASS health=200 version=200 endpointRegistration=ready'
