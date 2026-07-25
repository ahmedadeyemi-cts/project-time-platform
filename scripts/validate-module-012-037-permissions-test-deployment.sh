#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
WORKFLOW="$ROOT/.github/workflows/projectpulse-deploy-module-012-037-permissions-test.yml"
EXECUTOR="$ROOT/scripts/apply-module-012-037-permissions-test-migration.sh"
RUNNER="$ROOT/scripts/run-module-012-037-permissions-test-migration-job.sh"
EXPECTED="436eaa60c88442eae1365233569f0bc6f4271a9b"

fail() { echo "MODULE_012_037_PERMISSION_DEPLOYMENT_GUARD=FAIL: $*" >&2; exit 1; }
[[ -f "$WORKFLOW" && -f "$EXECUTOR" && -f "$RUNNER" ]] ||
  fail "Workflow, migration verifier, or private-network runner is missing."

require_workflow() { grep -Fq -- "$1" "$WORKFLOW" || fail "Workflow missing: $1"; }
require_executor() { grep -Fq -- "$1" "$EXECUTOR" || fail "Executor missing: $1"; }
require_runner() { grep -Fq -- "$1" "$RUNNER" || fail "Runner missing: $1"; }

for value in \
  'name: ProjectPulse Deploy Module 012/037 Permissions Test' \
  "default: $EXPECTED" \
  "EXPECTED_RELEASE_COMMIT: $EXPECTED" \
  'DEPLOY-MODULE-012-037-PERMISSIONS-TO-TEST' \
  'refs/heads/main' \
  'environment: test' \
  'git -C control merge-base --is-ancestor' \
  'Select a role, choose a database-backed module' \
  'Permission Matrix' \
  'Role Reference' \
  'Permission Levels' \
  'projectpulse:permissions-changed' \
  'permanent organization-wide Full Control' \
  'EXPECTED_MODULES = 70' \
  'EXPECTED_DECISIONS = 840' \
  'Apply or verify migration 040 inside private network' \
  'Build immutable permission API and web images' \
  'Deploy permission API' \
  'Deploy permission web' \
  'Validate served permission workbench and active images' \
  'Roll back API and web images on failure' \
  'module-012-037-permissions-test-deployment-'
do
  require_workflow "$value"
done

for value in \
  "EXPECTED_RELEASE_COMMIT=\"$EXPECTED\"" \
  '040_scoped_role_policy_versions.sql' \
  'SHA256SUMS' \
  'sha256sum --check --strict SHA256SUMS' \
  'Migration checksum manifest must contain exactly 12 scoped RBAC SQL files.' \
  'Scoped module catalog count is not 70.' \
  'Exactly one published scoped policy is required.' \
  'Super Administrator policy-publish authority is missing.' \
  'PTC delegated approval authority is incomplete.'
do
  require_executor "$value"
done

for value in \
  "EXPECTED_RELEASE_COMMIT=\"$EXPECTED\"" \
  'MODULE_012_037_PERMISSION_MIGRATION_JOB_REGISTRY_AUTH=EPHEMERAL_AZURE_TOKEN' \
  'az acr login' \
  '--expose-token' \
  '--registry-server' \
  '--registry-username' \
  '--registry-password' \
  'PROJECTPULSE_TEST_DATABASE_URL=secretref:scoped-rbac-db-url' \
  'MODULE_012_037_PERMISSION_MIGRATION_JOB_STATUS=Succeeded' \
  'MODULE_012_037_PERMISSION_MIGRATION_JOB_CLEANUP=COMPLETE'
do
  require_runner "$value"
done

[[ "$(grep -Fc 'az containerapp update' "$WORKFLOW")" == 4 ]] ||
  fail "Expected API/web deployment plus API/web rollback updates."
[[ "$(grep -Fc 'sha256:' "$WORKFLOW")" -ge 1 ]] ||
  fail "Immutable image references are not enforced."
[[ "$(grep -Fc 'SHA256SUMS' "$WORKFLOW")" -ge 2 ]] ||
  fail "Workflow does not create and verify a migration checksum manifest."

for forbidden in \
  'environment: production' \
  'AZURE_PRODUCTION' \
  'DEPLOY-PRODUCTION' \
  'git push' \
  'gh pr merge' \
  'workflow_run:' \
  'push:'
do
  grep -Fq -- "$forbidden" "$WORKFLOW" &&
    fail "Forbidden or automatic rollout behavior: $forbidden"
done

for forbidden in \
  '--registry-identity' \
  '--mi-user-assigned' \
  'az role assignment create' \
  'Microsoft.Authorization/roleAssignments/write'
do
  grep -Fq -- "$forbidden" "$RUNNER" &&
    fail "Migration runner must not request Azure role assignments: $forbidden"
done

bash -n "$EXECUTOR"
bash -n "$RUNNER"
bash -n "$0"

echo 'MODULE_012_037_PERMISSION_DEPLOYMENT_GUARD=PASS'
