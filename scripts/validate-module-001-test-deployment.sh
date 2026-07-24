#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
WORKFLOW="$ROOT/.github/workflows/projectpulse-deploy-module-001-test.yml"
EXECUTOR="$ROOT/scripts/apply-module-001-test-migration.sh"
RUNNER="$ROOT/scripts/run-module-001-test-migration-job.sh"
MIGRATOR="$ROOT/deployment/containers/module001-migrator/Dockerfile"
EXPECTED="8c4afed94bfb949ad158f029ebd498f6d930fcce"
REJECTED="1e67168a7c8d8ad8c2c6bb0e0007327b53805878"

fail() { echo "MODULE001_DEPLOYMENT_GUARD=FAIL: $*" >&2; exit 1; }
[[ -f "$WORKFLOW" && -f "$EXECUTOR" && -f "$RUNNER" && -f "$MIGRATOR" ]] ||
  fail "Workflow, migration executor, migration runner, or migrator Dockerfile is missing."

require_workflow() { grep -Fq -- "$1" "$WORKFLOW" || fail "Workflow missing: $1"; }
require_executor() { grep -Fq -- "$1" "$EXECUTOR" || fail "Executor missing: $1"; }
require_runner() { grep -Fq -- "$1" "$RUNNER" || fail "Runner missing: $1"; }
require_migrator() { grep -Fq -- "$1" "$MIGRATOR" || fail "Migrator Dockerfile missing: $1"; }

for value in \
  'name: ProjectPulse Deploy Module 001 Test' \
  "default: $EXPECTED" \
  "EXPECTED_RELEASE_COMMIT: $EXPECTED" \
  'DEPLOY-MODULE-001-TO-TEST' \
  'refs/heads/main' \
  'environment: test' \
  'Apply and verify migration 041 inside private network' \
  'MODULE001_MIGRATION_IMAGE' \
  'MODULE001_MIGRATION_JOB_NAME' \
  'run-module-001-test-migration-job.sh' \
  'sha256sum 041_module_001_timesheet_timer_and_task_association.sql > SHA256SUMS' \
  "FOREACH role_name IN ARRAY ARRAY['ptp_app', 'projectpulse_app']" \
  'Deploy API' \
  'Deploy web' \
  'Roll back API and web images on failure' \
  'Migration 041 is additive and is not automatically rolled back after application.' \
  'module-001-test-deployment-' \
  'Start / Stop Timer' \
  'Submit Timesheet week'
do require_workflow "$value"; done

for value in \
  "EXPECTED_RELEASE_COMMIT=\"$EXPECTED\"" \
  '041_module_001_timesheet_timer_and_task_association.sql' \
  'SHA256SUMS' \
  'sha256sum --check --strict SHA256SUMS' \
  'MODULE001_MIGRATION_CHECKSUM=VERIFIED' \
  'Migration 040 prerequisite is missing.' \
  'Existing Timesheet, Time Entry, assignment, or task row counts changed during migration 041.' \
  'One-running-timer unique index is missing.' \
  'Immutable timer audit trigger is missing.' \
  'has_table_privilege(current_user' \
  "IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='ptp_app')" \
  "IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='projectpulse_app')" \
  'The test API database principal cannot use the Module 001 tables.'
do require_executor "$value"; done

for value in \
  "EXPECTED_RELEASE_COMMIT=\"$EXPECTED\"" \
  'MODULE001_MIGRATION_JOB_REGISTRY_AUTH=EPHEMERAL_AZURE_TOKEN' \
  'az acr login' \
  '--expose-token' \
  '--registry-server' \
  '--registry-username' \
  '--registry-password' \
  'PROJECTPULSE_TEST_DATABASE_URL=secretref:module001-db-url' \
  'MODULE001_MIGRATION_JOB_STATUS=Succeeded' \
  'MODULE001_MIGRATION_JOB_CLEANUP=COMPLETE'
do require_runner "$value"; done

for value in \
  'FROM postgres:16-alpine' \
  'COPY release-commit .projectpulse-release-commit' \
  'COPY migrations/ database/migrations/' \
  'ENTRYPOINT ["/usr/local/bin/apply-module-001-test-migration.sh", "/opt/projectpulse/release"]'
do require_migrator "$value"; done

[[ "$(grep -Fc 'az containerapp update' "$WORKFLOW")" == 4 ]] ||
  fail "Expected API/web deployment plus API/web image rollback updates."
[[ "$(grep -Fc 'sha256:' "$WORKFLOW")" -ge 1 ]] ||
  fail "Immutable image references are not enforced."
[[ "$(grep -Fc 'sha256sum --check --strict SHA256SUMS' "$EXECUTOR")" == 1 ]] ||
  fail "Executor must verify the generated checksum manifest exactly once."

for file in "$WORKFLOW" "$EXECUTOR" "$RUNNER"; do
  grep -Fq "$REJECTED" "$file" && fail "Stale failed release pin remains in $file."
done

for forbidden in \
  'environment: production' \
  'AZURE_PRODUCTION' \
  'DEPLOY-PRODUCTION' \
  'database/rollback/041_module_001_timesheet_timer_and_task_association_rollback.sql' \
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

grep -Fq 'git -C control merge-base --is-ancestor' "$WORKFLOW" ||
  fail "Release ancestry guard is missing."
grep -Fq 'PROJECTPULSE_TEST_DATABASE_URL' "$EXECUTOR" ||
  fail "Executor is not restricted to the test database contract."

bash -n "$EXECUTOR"
bash -n "$RUNNER"
echo 'MODULE001_DEPLOYMENT_GUARD=PASS'
