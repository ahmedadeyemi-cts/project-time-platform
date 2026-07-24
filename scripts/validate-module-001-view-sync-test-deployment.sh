#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
WORKFLOW="$ROOT/.github/workflows/projectpulse-deploy-module-001-view-sync-test.yml"
EXPECTED="f2022074e538ea761d18ad43de1c351e1537c3b5"

fail() { echo "MODULE001_SHARED_TASK_SOURCE_DEPLOYMENT_GUARD=FAIL: $*" >&2; exit 1; }
[[ -f "$WORKFLOW" ]] || fail "Workflow is missing."

require() { grep -Fq -- "$1" "$WORKFLOW" || fail "Workflow missing: $1"; }

for value in \
  'name: ProjectPulse Deploy Module 001 Shared Task Source Test' \
  "default: $EXPECTED" \
  "EXPECTED_RELEASE_COMMIT: $EXPECTED" \
  'DEPLOY-MODULE-001-SHARED-TASK-SOURCE-TO-TEST' \
  'refs/heads/main' \
  'environment: test' \
  '/api/assignments/available-tasks?weekStart=' \
  '! grep -Fq '\''/api/timesheet/timers/targets?weekStart=' \
  'canonicalWorkTypeGroup' \
  "workType === 'project' || workType === 'iqs'" \
  'regularAssignedTasks:' \
  'requestAssignedTasks:' \
  'role="combobox"' \
  'Search activity, task, project, customer, or request' \
  'Non-Project Time' \
  'Regular Tasks' \
  'Service Request Tasks' \
  'window.setInterval(refresh, 5000)' \
  'error?.status === 409' \
  'error?.payload?.activeTimer' \
  '.module001-task-results' \
  'Deploy shared-task-source web image only' \
  'apiDeployment":"unchanged' \
  'migration041":"unchanged' \
  'database":"unchanged' \
  'Roll back web image on failure'
do require "$value"; done

[[ "$(grep -Fc 'az containerapp update' "$WORKFLOW")" == 2 ]] ||
  fail "Expected one web deployment and one web rollback."
[[ "$(grep -Fc 'scripts/build-pr55-acr-image.sh' "$WORKFLOW")" == 1 ]] ||
  fail "Expected exactly one immutable web image build."
grep -Fq 'git -C control merge-base --is-ancestor' "$WORKFLOW" || fail "Release ancestry guard is missing."
grep -Fq '@$DIGEST' "$WORKFLOW" || fail "Immutable web digest construction is missing."
grep -Fq 'steps.before.outputs.old_web_image' "$WORKFLOW" || fail "Web rollback image capture is missing."

for forbidden in \
  'AZURE_API_APP' \
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
echo 'MODULE001_SHARED_TASK_SOURCE_DEPLOYMENT_GUARD=PASS'
