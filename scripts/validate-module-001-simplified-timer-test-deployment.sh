#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
WORKFLOW="$ROOT/.github/workflows/projectpulse-deploy-module-001-simplified-timer-test.yml"
EXPECTED="8768199458f847892b094b375676df5dd29d70d6"

fail() { echo "MODULE001_STREAMLINED_TIMER_DEPLOYMENT_GUARD=FAIL: $*" >&2; exit 1; }
[[ -f "$WORKFLOW" ]] || fail "Workflow is missing."

require() { grep -Fq -- "$1" "$WORKFLOW" || fail "Workflow missing: $1"; }

for value in \
  'name: ProjectPulse Deploy Module 001 Streamlined Timer Test' \
  "default: $EXPECTED" \
  "EXPECTED_RELEASE_COMMIT: $EXPECTED" \
  'DEPLOY-MODULE-001-STREAMLINED-TIMER-TO-TEST' \
  'refs/heads/main' \
  'environment: test' \
  '/api/timesheet/timers/start-by-code' \
  'UPPER(category_code) = @category_code' \
  'snapshot?.assignedTasks' \
  'snapshot?.nonProjectCategories' \
  'category-code:' \
  'retiredTabsRemoved=2' \
  'Deploy streamlined API image' \
  'Deploy streamlined web image' \
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
grep -Fq "! grep -Fq \"{ key: 'queue', label: 'My Work Queue'\"" "$WORKFLOW" || fail "Queue tab absence check is missing."
grep -Fq "! grep -Fq \"{ key: 'calendar', label: 'Calendar / Timeline'\"" "$WORKFLOW" || fail "Calendar tab absence check is missing."
grep -Fq "! grep -Fq '/api/timesheet/timers/targets?weekStart='" "$WORKFLOW" || fail "Empty target endpoint rejection is missing."

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
echo 'MODULE001_STREAMLINED_TIMER_DEPLOYMENT_GUARD=PASS'
