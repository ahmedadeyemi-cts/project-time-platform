#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
WORKFLOW="$ROOT/.github/workflows/projectpulse-deploy-module-001-simplified-timer-test.yml"
EXPECTED="84190aa026ba67eec9966850d2710ff458b6a7a1"

fail() { echo "MODULE_AVAILABILITY_RESULT_FIX_DEPLOYMENT_GUARD=FAIL: $*" >&2; exit 1; }
[[ -f "$WORKFLOW" ]] || fail "Workflow is missing."

require() { grep -Fq -- "$1" "$WORKFLOW" || fail "Workflow missing: $1"; }

for value in \
  'name: ProjectPulse Deploy Module Availability Result Fix API Test' \
  "default: $EXPECTED" \
  "EXPECTED_RELEASE_COMMIT: $EXPECTED" \
  'DEPLOY-MODULE-AVAILABILITY-RESULT-FIX-TO-TEST' \
  'refs/heads/main' \
  'environment: test' \
  '(Func<HttpContext, Task<IResult>>)GetOverridesAsync' \
  'discard the returned IResult' \
  '! grep -Fq '\''app.MapGet("/api/module-availability/overrides", GetOverridesAsync);' \
  'Build immutable module availability result fix API image' \
  'Deploy module availability result fix API image only' \
  'Validate API health and non-empty protected response' \
  'content-type:[[:space:]]*application/json' \
  '[[ -s /tmp/override-response ]]' \
  'session_required|forbidden' \
  'webDeployment":"unchanged' \
  'migration042":"unchanged' \
  'database":"unchanged' \
  'moduleStates":"unchanged' \
  'Roll back API image on failure'
do require "$value"; done

[[ "$(grep -Fc 'az containerapp update' "$WORKFLOW")" == 2 ]] ||
  fail "Expected one API deployment and one API rollback update."
[[ "$(grep -Fc 'scripts/build-pr55-acr-image.sh' "$WORKFLOW")" == 1 ]] ||
  fail "Expected exactly one immutable API image build."
grep -Fq 'git -C control merge-base --is-ancestor' "$WORKFLOW" || fail "Release ancestry guard is missing."
grep -Fq '@$API_DIGEST' "$WORKFLOW" || fail "Immutable API digest construction is missing."
grep -Fq 'steps.before.outputs.old_api_image' "$WORKFLOW" || fail "API rollback image capture is missing."

for forbidden in \
  'AZURE_WEB_APP' \
  'old_web_image' \
  'web_image=' \
  'deployment/containers/web/Dockerfile' \
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
echo 'MODULE_AVAILABILITY_RESULT_FIX_DEPLOYMENT_GUARD=PASS'
