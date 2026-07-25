#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
WORKFLOW="$ROOT/.github/workflows/projectpulse-deploy-module-001-test.yml"
EXECUTOR="$ROOT/scripts/apply-module-availability-test-migration.sh"
RUNNER="$ROOT/scripts/run-module-availability-test-migration-job.sh"
MIGRATOR="$ROOT/deployment/containers/module-availability-migrator/Dockerfile"
EXPECTED="b64f495c743c30176977d05435f838259ead2d9e"

fail() { echo "MODULE_AVAILABILITY_DEPLOYMENT_GUARD=FAIL: $*" >&2; exit 1; }
[[ -f "$WORKFLOW" && -f "$EXECUTOR" && -f "$RUNNER" && -f "$MIGRATOR" ]] ||
  fail "Workflow, migration executor, migration runner, or migrator Dockerfile is missing."

require_workflow() { grep -Fq -- "$1" "$WORKFLOW" || fail "Workflow missing: $1"; }
require_executor() { grep -Fq -- "$1" "$EXECUTOR" || fail "Executor missing: $1"; }
require_runner() { grep -Fq -- "$1" "$RUNNER" || fail "Runner missing: $1"; }
require_migrator() { grep -Fq -- "$1" "$MIGRATOR" || fail "Migrator Dockerfile missing: $1"; }

for value in \
  'name: ProjectPulse Deploy Module Availability Test' \
  "default: $EXPECTED" \
  "EXPECTED_RELEASE_COMMIT: $EXPECTED" \
  'DEPLOY-MODULE-AVAILABILITY-TO-TEST' \
  'refs/heads/main' \
  'environment: test' \
  'Apply and verify migration 042 inside private network' \
  'MODULE_AVAILABILITY_MIGRATION_IMAGE' \
  'MODULE_AVAILABILITY_MIGRATION_JOB_NAME' \
  'run-module-availability-test-migration-job.sh' \
  'sha256sum 042_module_availability_controls.sql > SHA256SUMS' \
  'Deploy API' \
  'Deploy web' \
  'Roll back API and web images on failure' \
  'Migration 042 is additive and is not automatically rolled back after application.' \
  'module-availability-test-deployment-' \
  'Enable or disable modules safely' \
  'Disabled modules are preserved' \
  'X-ProjectPulse-Module-Number' \
  'availabilityRowsChangedByMigration":false'
do require_workflow "$value"; done

for value in \
  "EXPECTED_RELEASE_COMMIT=\"$EXPECTED\"" \
  '042_module_availability_controls.sql' \
  'SHA256SUMS' \
  'sha256sum --check --strict SHA256SUMS' \
  'MODULE_AVAILABILITY_MIGRATION_CHECKSUM=VERIFIED' \
  'Migration 041 prerequisite is missing.' \
  'Migration 042 changed module availability rows.' \
  'Migration 042 changed module availability audit rows.' \
  'Migration 042 changed the number of disabled modules.' \
  'Migration 042 was not registered.' \
  'has_table_privilege(current_user' \
  "IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='ptp_app')" \
  "IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='projectpulse_app')" \
  'The test API database principal cannot use module availability storage.'
do require_executor "$value"; done

for value in \
  "EXPECTED_RELEASE_COMMIT=\"$EXPECTED\"" \
  'MODULE_AVAILABILITY_MIGRATION_JOB_REGISTRY_AUTH=EPHEMERAL_AZURE_TOKEN' \
  'az acr login' \
  '--expose-token' \
  '--registry-server' \
  '--registry-username' \
  '--registry-password' \
  'PROJECTPULSE_TEST_DATABASE_URL=secretref:module-availability-db-url' \
  'MODULE_AVAILABILITY_MIGRATION_JOB_STATUS=Succeeded' \
  'MODULE_AVAILABILITY_MIGRATION_JOB_CLEANUP=COMPLETE'
do require_runner "$value"; done

for value in \
  'FROM postgres:16-alpine' \
  'COPY release-commit .projectpulse-release-commit' \
  'COPY migrations/ database/migrations/' \
  'ENTRYPOINT ["/usr/local/bin/apply-module-availability-test-migration.sh", "/opt/projectpulse/release"]'
do require_migrator "$value"; done

[[ "$(grep -Fc 'az containerapp update' "$WORKFLOW")" == 4 ]] ||
  fail "Expected API/web deployment plus API/web image rollback updates."
[[ "$(grep -Fc 'sha256:' "$WORKFLOW")" -ge 1 ]] ||
  fail "Immutable image references are not enforced."
[[ "$(grep -Fc 'sha256sum --check --strict SHA256SUMS' "$EXECUTOR")" == 1 ]] ||
  fail "Executor must verify the generated checksum manifest exactly once."
grep -Fq 'git -C control merge-base --is-ancestor' "$WORKFLOW" || fail "Release ancestry guard is missing."
grep -Fq 'PROJECTPULSE_TEST_DATABASE_URL' "$EXECUTOR" || fail "Executor is not restricted to the test database contract."

for forbidden in \
  'environment: production' \
  'AZURE_PRODUCTION' \
  'DEPLOY-PRODUCTION' \
  'database/rollback/042_module_availability_controls_rollback.sql' \
  'git push' \
  'gh pr merge'
do
  grep -Fq "$forbidden" "$WORKFLOW" && fail "Forbidden rollout behavior: $forbidden"
done

for forbidden in \
  '--registry-identity' \
  '--mi-user-assigned' \
  'az role assignment create' \
  'Microsoft.Authorization/roleAssignments/write'
do
  grep -Fq -- "$forbidden" "$RUNNER" && fail "Migration runner must not request Azure role assignments: $forbidden"
done

bash -n "$EXECUTOR"
bash -n "$RUNNER"
echo 'MODULE_AVAILABILITY_DEPLOYMENT_GUARD=PASS'
