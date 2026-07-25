#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
WORKFLOW="$ROOT/.github/workflows/projectpulse-deploy-module-001-view-sync-test.yml"
EXPECTED="cbee20a671dd34365f93112645b9284f825d6212"

fail() { echo "MODULE_AVAILABILITY_FAIL_OPEN_DEPLOYMENT_GUARD=FAIL: $*" >&2; exit 1; }
[[ -f "$WORKFLOW" ]] || fail "Workflow is missing."

require() { grep -Fq -- "$1" "$WORKFLOW" || fail "Workflow missing: $1"; }

for value in \
  'name: ProjectPulse Deploy Module Availability Fail-Open Test' \
  "default: $EXPECTED" \
  "EXPECTED_RELEASE_COMMIT: $EXPECTED" \
  'DEPLOY-MODULE-AVAILABILITY-FAIL-OPEN-TO-TEST' \
  'refs/heads/main' \
  'environment: test' \
  'PROJECTPULSE_MODULES' \
  'normalizeAvailabilityResponse' \
  'inventoryComplete' \
  'removeGovernedDirectory' \
  'The existing Modules directory remains available' \
  'Toggle controls require the SUPER_ADMINISTRATOR role' \
  '.enterprise-sidebar-section a[href^="#"]' \
  'const canReplaceDirectory = inventoryReady && (isSuperAdministrator || routes.size > 0)' \
  "moduleNumber: '001', route: 'timesheet', displayName: 'Timesheet'" \
  'Deploy module availability fail-open web image only' \
  'apiDeployment":"unchanged' \
  'migration042":"unchanged' \
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
  'Apply and verify migration 042' \
  'environment: production' \
  'AZURE_PRODUCTION' \
  'DEPLOY-PRODUCTION'
do
  grep -Fq "$forbidden" "$WORKFLOW" && fail "Forbidden rollout behavior: $forbidden"
done

bash -n "$0"
echo 'MODULE_AVAILABILITY_FAIL_OPEN_DEPLOYMENT_GUARD=PASS'
