#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
WORKFLOW="$ROOT/.github/workflows/projectpulse-deploy-module-001-simplified-timer-test.yml"
EXPECTED="0a63e25bbde54b3e99ed9e4f413fb0d5c1dc6b7b"

fail() { echo "MODULE001_SIMPLIFIED_TIMER_DEPLOYMENT_GUARD=FAIL: $*" >&2; exit 1; }
[[ -f "$WORKFLOW" ]] || fail "Workflow is missing."

require() { grep -Fq -- "$1" "$WORKFLOW" || fail "Workflow missing: $1"; }

for value in \
  'name: ProjectPulse Deploy Module 001 Simplified Timer Test' \
  "default: $EXPECTED" \
  "EXPECTED_RELEASE_COMMIT: $EXPECTED" \
  'DEPLOY-MODULE-001-SIMPLIFIED-TIMER-TO-TEST' \
  'refs/heads/main' \
  'environment: test' \
  '/api/timesheet/timers/targets' \
  'project_assignments' \
  'non_project_time_categories' \
  'app.MapModule001TimerTargetEndpoints();' \
  '<optgroup' \
  'Assigned project work' \
  'Authorized non-project activities' \
  'Deploy simplified API image' \
  'Deploy simplified web image' \
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
echo 'MODULE001_SIMPLIFIED_TIMER_DEPLOYMENT_GUARD=PASS'
