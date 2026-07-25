#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
WORKFLOW="$ROOT/.github/workflows/projectpulse-deploy-module-001-view-sync-test.yml"
EXPECTED="b2a05508bad2d92ef1fb1bc4cb966ab86406dd07"

fail() { echo "FRIENDLY_API_ERRORS_DEPLOYMENT_GUARD=FAIL: $*" >&2; exit 1; }
[[ -f "$WORKFLOW" ]] || fail "Workflow is missing."

require() { grep -Fq -- "$1" "$WORKFLOW" || fail "Workflow missing: $1"; }

for value in \
  'name: ProjectPulse Deploy Friendly API Errors Test' \
  "default: $EXPECTED" \
  "EXPECTED_RELEASE_COMMIT: $EXPECTED" \
  'DEPLOY-FRIENDLY-API-ERRORS-TO-TEST' \
  'refs/heads/main' \
  'environment: test' \
  'api-error-presentation.js' \
  'friendly-api-errors.css' \
  'validate-friendly-api-errors.mjs' \
  'ClientDiagnosticModule.cs' \
  'app.MapClientDiagnosticEndpoints();' \
  '/api/client-diagnostics' \
  "We couldn't verify those sign-in details" \
  "You don't have access to utilization information" \
  'ProjectPulse API diagnostic' \
  'Module 003 utilization policies' \
  'Module 003 utilization targets' \
  'Build immutable friendly API error API and web images' \
  'Deploy friendly API error API image' \
  'Validate API health and protected diagnostic endpoint' \
  'Deploy friendly API error web image' \
  'Validate served friendly error interface and active images' \
  'errorPresentation":"standardized' \
  'consoleDiagnostics":"enabled' \
  'auditDiagnostics":"sanitized' \
  'migration":"unchanged' \
  'databaseSchema":"unchanged' \
  'permissions":"unchanged' \
  'moduleStates":"unchanged' \
  'Roll back API and web images on failure'
do
  require "$value"
done

[[ "$(grep -Fc 'az containerapp update' "$WORKFLOW")" == 4 ]] ||
  fail "Expected API/web deployment plus API/web rollback updates."
[[ "$(grep -Fc 'scripts/build-pr55-acr-image.sh' "$WORKFLOW")" == 2 ]] ||
  fail "Expected exactly one immutable API build and one immutable web build."
grep -Fq 'git -C control merge-base --is-ancestor' "$WORKFLOW" || fail "Release ancestry guard is missing."
grep -Fq '@$API_DIGEST' "$WORKFLOW" || fail "Immutable API digest construction is missing."
grep -Fq '@$WEB_DIGEST' "$WORKFLOW" || fail "Immutable web digest construction is missing."
grep -Fq 'steps.before.outputs.old_api_image' "$WORKFLOW" || fail "API rollback image capture is missing."
grep -Fq 'steps.before.outputs.old_web_image' "$WORKFLOW" || fail "Web rollback image capture is missing."
grep -Fq '[[ -s /tmp/diagnostic-response ]]' "$WORKFLOW" || fail "Non-empty diagnostic response validation is missing."
grep -Fq "grep -Fq 'session_required' /tmp/diagnostic-response" "$WORKFLOW" || fail "Protected diagnostic response validation is missing."

for forbidden in \
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
echo 'FRIENDLY_API_ERRORS_DEPLOYMENT_GUARD=PASS'
