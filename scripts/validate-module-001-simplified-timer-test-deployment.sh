#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
WORKFLOW="$ROOT/.github/workflows/projectpulse-deploy-module-001-simplified-timer-test.yml"
EXPECTED="f4c76842f6803582895544f9eede473a70c13927"

fail() { echo "SIMPLIFIED_MODULE_AVAILABILITY_DEPLOYMENT_GUARD=FAIL: $*" >&2; exit 1; }
[[ -f "$WORKFLOW" ]] || fail "Workflow is missing."

require() { grep -Fq -- "$1" "$WORKFLOW" || fail "Workflow missing: $1"; }

for value in \
  'name: ProjectPulse Deploy Simplified Module Availability Test' \
  "default: $EXPECTED" \
  "EXPECTED_RELEASE_COMMIT: $EXPECTED" \
  'DEPLOY-SIMPLIFIED-MODULE-AVAILABILITY-TO-TEST' \
  'refs/heads/main' \
  'environment: test' \
  '/api/module-availability/overrides' \
  'Only persisted overrides are returned; missing rows mean Enabled.' \
  'missingOverrideBehavior = "ENABLED"' \
  'app.MapModuleAvailabilityOverrideEndpoints();' \
  'Missing overrides default to Enabled' \
  'Toggle controls require SUPER_ADMINISTRATOR' \
  "if (route === 'timesheet') return 'Timesheet'" \
  'Existing module cards remain available' \
  'clearAvailabilityNavigationState' \
  "! grep -Fq 'createPortal'" \
  "! grep -Fq 'module-availability-governed'" \
  'Deploy simplified module availability API image' \
  'Deploy simplified module availability web image' \
  'Validate API health and protected availability routes' \
  'Validate served existing-directory controls and active images' \
  'Module availability returned no module inventory' \
  'migration042":"unchanged' \
  'database":"unchanged' \
  'moduleStates":"unchanged' \
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
grep -Fq 'wait_status GET "$BASE/api/module-availability/overrides"' "$WORKFLOW" || fail "Protected override endpoint probe is missing."
grep -Fq 'wait_status PUT "$BASE/api/module-availability/001"' "$WORKFLOW" || fail "Protected update endpoint probe is missing."
grep -Fq "! grep -Fq 'Module availability returned no module inventory'" "$WORKFLOW" || fail "Retired inventory-error absence check is missing."

for forbidden in \
  'PROJECTPULSE_TEST_DATABASE_URL' \
  'database/migrations/' \
  'MODULE001_MIGRATION_IMAGE' \
  'MODULE_AVAILABILITY_MIGRATION_IMAGE' \
  'run-module-availability-test-migration-job.sh' \
  'Apply and verify migration 042' \
  'environment: production' \
  'AZURE_PRODUCTION' \
  'DEPLOY-PRODUCTION'
do
  grep -Fq "$forbidden" "$WORKFLOW" && fail "Forbidden rollout behavior: $forbidden"
done

bash -n "$0"
echo 'SIMPLIFIED_MODULE_AVAILABILITY_DEPLOYMENT_GUARD=PASS'
