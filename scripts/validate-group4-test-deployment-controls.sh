#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
EXPECTED_RELEASE="24fb92d751726b1bab66c11d902c0b2571701b23"
MIGRATION_WORKFLOW="$ROOT/.github/workflows/projectpulse-run-group4-migration-050-test.yml"
DEPLOY_WORKFLOW="$ROOT/.github/workflows/projectpulse-deploy-group4-notifications-test.yml"
VALIDATION_WORKFLOW="$ROOT/.github/workflows/validate-group4-test-deployment-controls.yml"
APPLY="$ROOT/scripts/apply-group4-migration-050-test.sh"
JOB="$ROOT/scripts/run-group4-migration-050-test-job.sh"
DEPLOY="$ROOT/scripts/run-group4-notifications-test-deployment.sh"
SELF="$ROOT/scripts/validate-group4-test-deployment-controls.sh"

fail() { echo "ERROR: $*" >&2; exit 1; }
require() { grep -Fq -- "$2" "$1" || fail "Missing contract in $1: $2"; }
reject() { ! grep -Eiq -- "$2" "$1" || fail "Forbidden contract in $1: $2"; }

for file in "$MIGRATION_WORKFLOW" "$DEPLOY_WORKFLOW" "$VALIDATION_WORKFLOW" "$APPLY" "$JOB" "$DEPLOY" "$SELF"; do
  [[ -f "$file" ]] || fail "Missing $file"
done

bash -n "$APPLY"
bash -n "$JOB"
bash -n "$DEPLOY"
bash -n "$SELF"

require "$MIGRATION_WORKFLOW" "default: $EXPECTED_RELEASE"
require "$MIGRATION_WORKFLOW" "EXPECTED_RELEASE_COMMIT: $EXPECTED_RELEASE"
require "$MIGRATION_WORKFLOW" 'APPLY-GROUP4-MIGRATION-050-TO-TEST'
require "$MIGRATION_WORKFLOW" 'environment: test'
require "$MIGRATION_WORKFLOW" 'GROUP4_MIGRATION_MODE: apply'
require "$MIGRATION_WORKFLOW" 'GROUP4_MIGRATION_MODE: verify'
require "$MIGRATION_WORKFLOW" 'run-group4-migration-050-test-job.sh'
require "$MIGRATION_WORKFLOW" 'databaseMigration": true'
require "$MIGRATION_WORKFLOW" 'apiWebDeployment": false'

require "$DEPLOY_WORKFLOW" "default: $EXPECTED_RELEASE"
require "$DEPLOY_WORKFLOW" "EXPECTED_RELEASE_COMMIT: $EXPECTED_RELEASE"
require "$DEPLOY_WORKFLOW" 'DEPLOY-GROUP4-NOTIFICATIONS-TO-TEST'
require "$DEPLOY_WORKFLOW" 'environment: test'
require "$DEPLOY_WORKFLOW" 'GROUP4_MIGRATION_MODE: verify'
require "$DEPLOY_WORKFLOW" 'run-group4-notifications-test-deployment.sh'
require "$DEPLOY_WORKFLOW" 'This API/web Action does not apply migration 050.'

require "$APPLY" "EXPECTED_RELEASE_COMMIT=\"$EXPECTED_RELEASE\""
require "$APPLY" 'MIGRATION_FILE="050_project_notification_routing_and_schedules.sql"'
require "$APPLY" 'GROUP4_MIGRATION_MODE'
require "$APPLY" 'GROUP4_MIGRATION_050_RESULT=VERIFY_ONLY_PASS'
require "$APPLY" 'GROUP4_MIGRATION_050_OPERATIONAL_COUNTS=UNCHANGED'
require "$APPLY" 'trg_projectpulse050_delivery_attempts_immutable'
require "$APPLY" 'trg_projectpulse050_configuration_audit_immutable'

require "$JOB" "EXPECTED_RELEASE_COMMIT=\"$EXPECTED_RELEASE\""
require "$JOB" 'GROUP4_MIGRATION_MODE'
require "$JOB" 'registry-identity'
require "$JOB" 'GROUP4_MIGRATION_JOB_CLEANUP=COMPLETE'

require "$DEPLOY" "EXPECTED_RELEASE_COMMIT=\"$EXPECTED_RELEASE\""
require "$DEPLOY" 'migration050PreflightVerified": true'
require "$DEPLOY" 'databaseMigrationExecutedByThisAction": false'
require "$DEPLOY" 'Module 065 is the only mail-delivery authority'
require "$DEPLOY" 'trusted_public_origin_unavailable'
require "$DEPLOY" 'emailSentByDeployment": false'
require "$DEPLOY" 'graphCallPerformedByDeployment": false'
require "$DEPLOY" 'productionChanged": false'
require "$DEPLOY" 'PROJECTPULSE_NOTIFICATION_SCHEDULER_INITIAL_DELAY_SECONDS=600'

require "$VALIDATION_WORKFLOW" 'Enforce exact seven-file deployment-control scope'
require "$VALIDATION_WORKFLOW" 'Test migration 050 apply, idempotency, invariants, and rollback'
require "$VALIDATION_WORKFLOW" 'Build ProjectTime API from exact Group 4 release'
require "$VALIDATION_WORKFLOW" 'Build complete frontend production bundle from exact Group 4 release'

reject "$MIGRATION_WORKFLOW" '^[[:space:]]*schedule:'
reject "$DEPLOY_WORKFLOW" '^[[:space:]]*schedule:'
reject "$MIGRATION_WORKFLOW" 'environment:[[:space:]]*production'
reject "$DEPLOY_WORKFLOW" 'environment:[[:space:]]*production'
reject "$MIGRATION_WORKFLOW" 'graph\.microsoft\.com|/sendMail|api\.openai\.com|api\.anthropic\.com'
reject "$DEPLOY_WORKFLOW" 'graph\.microsoft\.com|/sendMail|api\.openai\.com|api\.anthropic\.com'
reject "$DEPLOY" '\bpsql\b|database/migrations/[0-9]|graph\.microsoft\.com|/sendMail|api\.openai\.com|api\.anthropic\.com'

echo 'GROUP4_TEST_DEPLOYMENT_CONTROLS=PASS'
