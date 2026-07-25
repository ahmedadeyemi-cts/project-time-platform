#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
WORKFLOW="$ROOT/.github/workflows/projectpulse-deploy-runtime-data-hotfix-test.yml"
VERIFIER="$ROOT/scripts/verify-direct-runtime-data-test.sh"
RUNNER="$ROOT/scripts/run-direct-runtime-data-validation-job.sh"
DOCKERFILE="$ROOT/deployment/containers/runtime-data-validator/Dockerfile"
EXPECTED="34d66764d8e84d683abd677981c79e6dbd20d94f"

fail() { echo "DIRECT_RUNTIME_DATA_DEPLOYMENT_GUARD=FAIL: $*" >&2; exit 1; }

for file in "$WORKFLOW" "$VERIFIER" "$RUNNER" "$DOCKERFILE"; do
  [[ -f "$file" ]] || fail "Required control file is missing: ${file#"$ROOT/"}"
done

require_workflow() { grep -Fq -- "$1" "$WORKFLOW" || fail "Workflow missing: $1"; }
require_verifier() { grep -Fq -- "$1" "$VERIFIER" || fail "Verifier missing: $1"; }
require_runner() { grep -Fq -- "$1" "$RUNNER" || fail "Runner missing: $1"; }
require_dockerfile() { grep -Fq -- "$1" "$DOCKERFILE" || fail "Validator image missing: $1"; }

for value in \
  'name: ProjectPulse Deploy Runtime Data Hotfix Test' \
  "default: $EXPECTED" \
  "EXPECTED_RELEASE_COMMIT: $EXPECTED" \
  'DEPLOY-RUNTIME-DATA-HOTFIX-TO-TEST' \
  'refs/heads/main' \
  'environment: test' \
  'git -C control merge-base --is-ancestor' \
  "'/api/role-policy/summary': '/api/runtime/role-policy/summary'" \
  "'/api/role-policy/matrix': '/api/runtime/role-policy/matrix'" \
  '/api/runtime/timesheet/steward/users?weekStart=' \
  'runtime_api_contract_incomplete' \
  'The server returned 0 eligible users.' \
  'Build immutable API, web, and validation images' \
  'Verify live role, module, policy, and eligible-user data privately' \
  'RUNTIME_DATA_VALIDATION_IMAGE' \
  'run-direct-runtime-data-validation-job.sh' \
  'Deploy direct runtime data API' \
  'Validate runtime API routes and JSON protection' \
  'Deploy direct runtime data web' \
  'Validate served direct runtime assets and exact images' \
  'projectpulse-runtime-data-direct-auth-2026-07-25' \
  'X-Project-Pulse-Session' \
  'X-Session-Token' \
  'readOnlyDatabaseValidation":"passed"' \
  'databaseChanges":"none"' \
  'Roll back API and web images on failure'
do
  require_workflow "$value"
done

for value in \
  'DIRECT_RUNTIME_DATA_VALIDATION=PASS' \
  'Expected 12 active canonical roles' \
  'Expected 70 active scoped modules' \
  'Expected exactly one published role-policy version' \
  'Expected at least one eligible Engineer/Lead/PM user' \
  'Expected effective role-policy grants' \
  'Expected at least one active Super Administrator' \
  "migration_id='043_ptc_time_steward_permissions'" \
  'SELECT COUNT(*) FROM scoped_role_policy_modules WHERE is_active=TRUE' \
  'SELECT COUNT(*) FROM scoped_role_policy_effective_grants'
do
  require_verifier "$value"
done

for value in \
  "EXPECTED_RELEASE_COMMIT=\"$EXPECTED\"" \
  'DIRECT_RUNTIME_DATA_VALIDATION_REGISTRY_AUTH=EPHEMERAL_AZURE_TOKEN' \
  'az acr login' \
  '--expose-token' \
  '--registry-server' \
  '--registry-username' \
  '--registry-password' \
  'PROJECTPULSE_TEST_DATABASE_URL=secretref:runtime-data-db-url' \
  'DIRECT_RUNTIME_DATA_VALIDATION_JOB_STATUS=Succeeded' \
  'DIRECT_RUNTIME_DATA_VALIDATION_JOB_CLEANUP=COMPLETE'
do
  require_runner "$value"
done

for value in \
  'FROM postgres:16-alpine' \
  'COPY scripts/verify-direct-runtime-data-test.sh' \
  'ENTRYPOINT ["/usr/local/bin/verify-direct-runtime-data-test.sh"]'
do
  require_dockerfile "$value"
done

[[ "$(grep -Fc 'az containerapp update' "$WORKFLOW")" == 4 ]] ||
  fail "Expected API/web deployment plus API/web rollback updates."
[[ "$(grep -Fc '@sha256:' "$WORKFLOW")" -ge 1 ]] ||
  fail "Immutable ACR digest enforcement is missing."
[[ "$(grep -Fc 'application/json' "$WORKFLOW")" -ge 1 ]] ||
  fail "Runtime API JSON content-type validation is missing."
[[ "$(grep -Fc 'build-pr55-acr-image.sh' "$WORKFLOW")" == 3 ]] ||
  fail "Expected immutable API, web, and read-only validator image builds."

# The validation SQL may read only. Reject mutation and schema statements.
for forbidden in \
  'INSERT INTO' \
  'UPDATE ' \
  'DELETE FROM' \
  'CREATE TABLE' \
  'ALTER TABLE' \
  'DROP TABLE' \
  'TRUNCATE ' \
  'GRANT ' \
  'REVOKE '
do
  grep -Fqi -- "$forbidden" "$VERIFIER" && fail "Read-only verifier contains forbidden SQL: $forbidden"
done

for forbidden in \
  'environment: production' \
  'AZURE_PRODUCTION' \
  'DEPLOY-PRODUCTION' \
  'workflow_run:' \
  'push:' \
  'database/migrations/' \
  'az role assignment create' \
  'git push' \
  'gh pr merge'
do
  grep -Fq -- "$forbidden" "$WORKFLOW" && fail "Forbidden workflow behavior found: $forbidden"
done

for forbidden in \
  '--registry-identity' \
  '--mi-user-assigned' \
  'az role assignment create' \
  'Microsoft.Authorization/roleAssignments/write'
do
  grep -Fq -- "$forbidden" "$RUNNER" && fail "Validation runner requests forbidden Azure privilege changes: $forbidden"
done

bash -n "$VERIFIER"
bash -n "$RUNNER"
bash -n "$0"

echo 'DIRECT_RUNTIME_DATA_DEPLOYMENT_GUARD=PASS'
