#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
WORKFLOW="$ROOT/.github/workflows/projectpulse-deploy-module-001-timer-hotfix-test.yml"
EXPECTED="79d5d172c8d1e9d21f064f24dbf86c8a6852a28b"

fail() { echo "MODULE001_TIMER_HOTFIX_DEPLOYMENT_GUARD=FAIL: $*" >&2; exit 1; }
[[ -f "$WORKFLOW" ]] || fail "Workflow is missing."

require() { grep -Fq -- "$1" "$WORKFLOW" || fail "Workflow missing: $1"; }

for value in \
  'name: ProjectPulse Deploy Module 001 Timer Hotfix Test' \
  "default: $EXPECTED" \
  "EXPECTED_RELEASE_COMMIT: $EXPECTED" \
  'DEPLOY-MODULE-001-TIMER-HOTFIX-TO-TEST' \
  'refs/heads/main' \
  'environment: test' \
  'if (forUpdate) sql += " FOR UPDATE OF t";' \
  'TIMER_TARGET_PATTERN' \
  'No authorized timer activity available' \
  '#timesheet.module001-timer-mode .timesheet-workspace' \
  'Deploy timer-hotfix API image' \
  'Deploy timer-hotfix web image' \
  '/api/timesheet/timers/active' \
  '/api/timesheet/timers/history?weekStart=2026-07-19' \
  'Select a valid assigned task or authorized non-project activity' \
  'migration041":"unchanged' \
  'database":"unchanged' \
  'Roll back API and web images on failure'
do require "$value"; done

[[ "$(grep -Fc 'az containerapp update' "$WORKFLOW")" == 4 ]] ||
  fail "Expected API/web deployment plus API/web rollback updates."
[[ "$(grep -Fc 'scripts/build-pr55-acr-image.sh' "$WORKFLOW")" == 2 ]] ||
  fail "Expected exactly one immutable API build and one immutable web build."
grep -Fq 'git -C control merge-base --is-ancestor' "$WORKFLOW" || fail "Release ancestry guard is missing."
grep -Fq '@$API_DIGEST' "$WORKFLOW" || fail "Immutable API digest construction is missing."
grep -Fq '@$WEB_DIGEST' "$WORKFLOW" || fail "Immutable web digest construction is missing."
grep -Fq 'steps.before.outputs.old_api_image' "$WORKFLOW" || fail "API rollback image capture is missing."
grep -Fq 'steps.before.outputs.old_web_image' "$WORKFLOW" || fail "Web rollback image capture is missing."

for forbidden in \
  'PROJECTPULSE_TEST_DATABASE_URL' \
  'database/migrations' \
  'MODULE001_MIGRATION_IMAGE' \
  'run-module-001-test-migration-job.sh' \
  'Apply and verify migration 041' \
  'environment: production' \
  'AZURE_PRODUCTION' \
  'DEPLOY-PRODUCTION'
do
  grep -Fq "$forbidden" "$WORKFLOW" && fail "Forbidden rollout behavior: $forbidden"
done

bash -n "$0"
echo 'MODULE001_TIMER_HOTFIX_DEPLOYMENT_GUARD=PASS'
