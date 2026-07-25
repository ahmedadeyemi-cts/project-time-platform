#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
WORKFLOW="$ROOT/.github/workflows/projectpulse-deploy-intuitive-permissions-ptc-test.yml"
EXECUTOR="$ROOT/scripts/apply-intuitive-permissions-ptc-test-migrations.sh"
RUNNER="$ROOT/scripts/run-intuitive-permissions-ptc-test-migration-job.sh"
EXPECTED="d874b1a5e03c77ab48e174020b98b6678c6eabc9"

fail() { echo "INTUITIVE_PERMISSIONS_PTC_DEPLOYMENT_GUARD=FAIL: $*" >&2; exit 1; }
[[ -f "$WORKFLOW" && -f "$EXECUTOR" && -f "$RUNNER" ]] ||
  fail "Workflow, migration executor, or private-network runner is missing."

require_workflow() { grep -Fq -- "$1" "$WORKFLOW" || fail "Workflow missing: $1"; }
require_executor() { grep -Fq -- "$1" "$EXECUTOR" || fail "Executor missing: $1"; }
require_runner() { grep -Fq -- "$1" "$RUNNER" || fail "Runner missing: $1"; }

for value in \
  'name: ProjectPulse Deploy Intuitive Permissions and PTC Test' \
  "default: $EXPECTED" \
  "EXPECTED_RELEASE_COMMIT: $EXPECTED" \
  'DEPLOY-INTUITIVE-PERMISSIONS-PTC-TO-TEST' \
  'refs/heads/main' \
  'environment: test' \
  'git -C control merge-base --is-ancestor' \
  '040_scoped_role_policy_versions.sql' \
  '041_module_001_timesheet_timer_and_task_association.sql' \
  '042_module_availability_controls.sql' \
  '043_ptc_time_steward_permissions.sql' \
  'Select a role first' \
  'Detailed permissions' \
  'The Module, Permission, and Description columns stay pinned' \
  'Project Team Coordinator · Time Steward' \
  'No submission on behalf' \
  'Return week to draft' \
  'Create replacement task' \
  'Apply or verify migrations 040-043 inside private network' \
  'Build immutable API and web images' \
  'Deploy combined API' \
  'Deploy combined web' \
  'Validate served Modules 001, 012, and 037' \
  'Roll back API and web images on failure' \
  'intuitive-permissions-ptc-test-deployment-'
do
  require_workflow "$value"
done

for value in \
  "EXPECTED_RELEASE_COMMIT=\"$EXPECTED\"" \
  'Migration checksum manifest must contain exactly 15 SQL files.' \
  '040_scoped_role_policy_versions.sql' \
  '041_module_001_timesheet_timer_and_task_association.sql' \
  '042_module_availability_controls.sql' \
  '043_ptc_time_steward_permissions.sql' \
  'sha256sum --check --strict SHA256SUMS' \
  'The active scoped module catalog count is not 70.' \
  'The effective canonical role count is not 12.' \
  'The PTC time-steward grant set is incomplete.' \
  'The PTC protected denial set is incomplete.' \
  'INTUITIVE_PERMISSIONS_PTC_MIGRATIONS=APPLIED_OR_VERIFIED'
do
  require_executor "$value"
done

for value in \
  "EXPECTED_RELEASE_COMMIT=\"$EXPECTED\"" \
  'INTUITIVE_PERMISSIONS_MIGRATION_REGISTRY_AUTH=EPHEMERAL_AZURE_TOKEN' \
  'az acr login' \
  '--expose-token' \
  '--registry-server' \
  '--registry-username' \
  '--registry-password' \
  'PROJECTPULSE_TEST_DATABASE_URL=secretref:projectpulse-db-url' \
  'INTUITIVE_PERMISSIONS_MIGRATION_JOB_STATUS=Succeeded' \
  'INTUITIVE_PERMISSIONS_MIGRATION_JOB_CLEANUP=COMPLETE'
do
  require_runner "$value"
done

[[ "$(grep -Fc 'az containerapp update' "$WORKFLOW")" == 4 ]] ||
  fail "Expected API/web deployment plus API/web rollback updates."
[[ "$(grep -Fc '@sha256:' "$WORKFLOW")" -ge 1 ]] ||
  fail "Immutable image references are not enforced."
[[ "$(grep -Fc 'SHA256SUMS' "$WORKFLOW")" -ge 1 ]] ||
  fail "The workflow does not create a migration checksum manifest."

for forbidden in \
  'environment: production' \
  'AZURE_PRODUCTION' \
  'DEPLOY-PRODUCTION' \
  'workflow_run:' \
  'push:' \
  'git push' \
  'gh pr merge'
do
  grep -Fq -- "$forbidden" "$WORKFLOW" &&
    fail "Forbidden automatic or production behavior: $forbidden"
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

echo 'INTUITIVE_PERMISSIONS_PTC_DEPLOYMENT_GUARD=PASS'
