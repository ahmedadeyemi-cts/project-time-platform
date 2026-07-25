#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
WORKFLOW="$ROOT/.github/workflows/projectpulse-deploy-direct-runtime-verified-test.yml"
EXPECTED="34d66764d8e84d683abd677981c79e6dbd20d94f"

fail() { echo "DIRECT_RUNTIME_VERIFIED_DEPLOYMENT_GUARD=FAIL: $*" >&2; exit 1; }
[[ -f "$WORKFLOW" ]] || fail "Corrected workflow is missing."

require() { grep -Fq -- "$1" "$WORKFLOW" || fail "Workflow missing: $1"; }

for value in \
  'name: ProjectPulse Deploy Direct Runtime Data Verified Test' \
  "default: $EXPECTED" \
  "EXPECTED_RELEASE_COMMIT: $EXPECTED" \
  'DEPLOY-DIRECT-RUNTIME-VERIFIED-TO-TEST' \
  'refs/heads/main' \
  'environment: test' \
  'git -C control merge-base --is-ancestor' \
  'test -f control/deployment/containers/runtime-data-validator/Dockerfile' \
  'test -f control/scripts/verify-direct-runtime-data-test.sh' \
  'test -f control/scripts/run-direct-runtime-data-validation-job.sh' \
  'control/deployment/containers/runtime-data-validator/Dockerfile control' \
  'Verify required live database data before deployment' \
  'run: bash control/scripts/run-direct-runtime-data-validation-job.sh' \
  '/api/runtime/role-policy/summary' \
  '/api/runtime/role-policy/matrix' \
  '/api/runtime/timesheet/steward/users' \
  'X-Project-Pulse-Session' \
  'X-Session-Token' \
  'runtime_api_contract_incomplete' \
  'readOnlyDatabaseValidation":"passed"' \
  'Roll back API and web images on failure'
do
  require "$value"
done

for forbidden in \
  'test -f release/deployment/containers/runtime-data-validator/Dockerfile' \
  'test -f release/scripts/verify-direct-runtime-data-test.sh' \
  'release/deployment/containers/runtime-data-validator/Dockerfile' \
  'environment: production' \
  'DEPLOY-PRODUCTION' \
  'workflow_run:' \
  'push:' \
  'az role assignment create' \
  'psql ' \
  'database/migrations/'
do
  grep -Fq -- "$forbidden" "$WORKFLOW" && fail "Forbidden workflow behavior: $forbidden"
done

[[ "$(grep -Fc 'az containerapp update' "$WORKFLOW")" == 4 ]] || fail "Expected API/web deployment plus API/web rollback."
[[ "$(grep -Fc 'build-pr55-acr-image.sh' "$WORKFLOW")" == 3 ]] || fail "Expected API, web, and validator image builds."
[[ "$(grep -Fc 'application/json' "$WORKFLOW")" -ge 1 ]] || fail "JSON content-type validation is missing."

bash -n "$0"
echo 'DIRECT_RUNTIME_VERIFIED_DEPLOYMENT_GUARD=PASS'
