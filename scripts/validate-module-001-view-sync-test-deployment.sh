#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
WORKFLOW="$ROOT/.github/workflows/projectpulse-deploy-module-001-view-sync-test.yml"
EXPECTED="320f6489be8ce900d4fba29d9d7728a258b638d3"

fail() { echo "COMPLETE_FRIENDLY_ERROR_COVERAGE_DEPLOYMENT_GUARD=FAIL: $*" >&2; exit 1; }
[[ -f "$WORKFLOW" ]] || fail "Workflow is missing."

require() { grep -Fq -- "$1" "$WORKFLOW" || fail "Workflow missing: $1"; }

for value in \
  'name: ProjectPulse Deploy Complete Friendly Error Coverage Test' \
  "default: $EXPECTED" \
  "EXPECTED_RELEASE_COMMIT: $EXPECTED" \
  'DEPLOY-COMPLETE-FRIENDLY-ERROR-COVERAGE-TO-TEST' \
  'refs/heads/main' \
  'environment: test' \
  'RAW_API_FAILURE_PATTERN' \
  'document.createTreeWalker' \
  'NodeFilter.SHOW_TEXT' \
  'nested user-interface error detail' \
  'installNativeDialogGuards' \
  'window.alert = (message)' \
  'window.confirm = (message)' \
  'ERROR_ATTRIBUTE_NAMES' \
  'sanitizeTechnicalAttributes' \
  'attributeFilter: ERROR_ATTRIBUTE_NAMES' \
  'temporarily unavailable while access is being verified' \
  '.projectpulse-friendly-error.compact' \
  '.projectpulse-friendly-error.compact::marker' \
  'Some closeout data sources were unavailable.' \
  '<li key={warning}>{warning}</li>' \
  'Build immutable complete friendly error coverage web image' \
  'Deploy complete friendly error coverage web image only' \
  'Validate served complete friendly error coverage and active image' \
  'nestedErrors":"covered' \
  'nativeDialogs":"covered' \
  'technicalAttributes":"covered' \
  'legacySurfaceInventory":"enforced' \
  'apiDeployment":"unchanged' \
  'diagnosticEndpoint":"unchanged' \
  'migration":"unchanged' \
  'database":"unchanged' \
  'permissions":"unchanged' \
  'moduleStates":"unchanged' \
  'Roll back web image on failure'
do
  require "$value"
done

[[ "$(grep -Fc 'az containerapp update' "$WORKFLOW")" == 2 ]] ||
  fail "Expected one web deployment and one web rollback."
[[ "$(grep -Fc 'scripts/build-pr55-acr-image.sh' "$WORKFLOW")" == 1 ]] ||
  fail "Expected exactly one immutable web image build."
grep -Fq 'git -C control merge-base --is-ancestor' "$WORKFLOW" || fail "Release ancestry guard is missing."
grep -Fq '@$DIGEST' "$WORKFLOW" || fail "Immutable web digest construction is missing."
grep -Fq 'steps.before.outputs.old_web_image' "$WORKFLOW" || fail "Web rollback image capture is missing."
grep -Fq "grep -Fq 'projectpulse-friendly-error' /tmp/app.js" "$WORKFLOW" || fail "Served JS friendly-error marker validation is missing."
grep -Fq "grep -Fq 'projectpulse-friendly-error.compact' /tmp/app.css" "$WORKFLOW" || fail "Served CSS compact marker validation is missing."

for forbidden in \
  'AZURE_API_APP' \
  'project-health-dashboard-api' \
  'Deploy friendly API error API image' \
  'PROJECTPULSE_TEST_DATABASE_URL' \
  'export-pr55-test-database-url.sh' \
  'database/migrations/' \
  'MIGRATION_IMAGE' \
  'MODULE001_MIGRATION_IMAGE' \
  'MODULE_AVAILABILITY_MIGRATION_IMAGE' \
  'run-module-001-test-migration-job.sh' \
  'run-module-availability-test-migration-job.sh' \
  'Apply and verify migration' \
  'environment: production' \
  'AZURE_PRODUCTION' \
  'DEPLOY-PRODUCTION'
do
  grep -Fq "$forbidden" "$WORKFLOW" && fail "Forbidden rollout behavior: $forbidden"
done

bash -n "$0"
echo 'COMPLETE_FRIENDLY_ERROR_COVERAGE_DEPLOYMENT_GUARD=PASS'
