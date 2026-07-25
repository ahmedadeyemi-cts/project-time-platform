#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
WORKFLOW="$ROOT/.github/workflows/projectpulse-deploy-runtime-data-hotfix-test.yml"
EXPECTED="62bf874ae3a184554bc52960ef878a2f39ab67ef"

fail() { echo "RUNTIME_DATA_HOTFIX_DEPLOYMENT_GUARD=FAIL: $*" >&2; exit 1; }
[[ -f "$WORKFLOW" ]] || fail "The guarded workflow is missing."

require() { grep -Fq -- "$1" "$WORKFLOW" || fail "Workflow missing: $1"; }

for value in \
  'name: ProjectPulse Deploy Runtime Data Hotfix Test' \
  "default: $EXPECTED" \
  "EXPECTED_RELEASE_COMMIT: $EXPECTED" \
  'DEPLOY-RUNTIME-DATA-HOTFIX-TO-TEST' \
  "refs/heads/main" \
  'environment: test' \
  'git -C control merge-base --is-ancestor' \
  'RuntimeDataCompatibilityModule.cs' \
  '/api/runtime/role-policy/summary' \
  '/api/runtime/role-policy/matrix' \
  '/api/runtime/timesheet/steward/users' \
  'PtcManagedRoleAliases' \
  'Requests / Service Requests' \
  'Project Tasks' \
  'Non-Project Time' \
  'runtime_api_non_json_response' \
  'Build immutable runtime data API and web images' \
  'Deploy runtime data API' \
  'Validate runtime API routes and JSON protection' \
  'Deploy runtime data web' \
  'Validate served runtime data assets and exact images' \
  'databaseChanges":"none"' \
  'Roll back API and web images on failure'
do
  require "$value"
done

[[ "$(grep -Fc 'az containerapp update' "$WORKFLOW")" == 4 ]] ||
  fail "Expected API/web deployment plus API/web rollback updates."
[[ "$(grep -Fc '@sha256:' "$WORKFLOW")" -ge 1 ]] ||
  fail "Immutable ACR digest enforcement is missing."
[[ "$(grep -Fc 'application/json' "$WORKFLOW")" -ge 1 ]] ||
  fail "Runtime API JSON content-type validation is missing."

for forbidden in \
  'environment: production' \
  'AZURE_PRODUCTION' \
  'DEPLOY-PRODUCTION' \
  'workflow_run:' \
  'push:' \
  'database/migrations/' \
  'psql ' \
  'az role assignment create' \
  'git push' \
  'gh pr merge'
do
  grep -Fq -- "$forbidden" "$WORKFLOW" && fail "Forbidden behavior found: $forbidden"
done

bash -n "$0"
echo 'RUNTIME_DATA_HOTFIX_DEPLOYMENT_GUARD=PASS'
