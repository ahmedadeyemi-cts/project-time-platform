#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
WORKFLOW="$ROOT/.github/workflows/projectpulse-deploy-stabilized-role-permission-timesheet-test.yml"
VERIFY="$ROOT/scripts/verify-stabilized-role-permission-timesheet-test.sh"
RUNNER="$ROOT/scripts/run-stabilized-role-permission-timesheet-validation-job.sh"
DOCKERFILE="$ROOT/deployment/containers/stabilized-role-timesheet-validator/Dockerfile"
EXPECTED="2216bfadaca76858fe07e8d1228df888688fd786"

fail() { echo "STABILIZED_ROLE_PERMISSION_TIMESHEET_DEPLOYMENT_GUARD=FAIL: $*" >&2; exit 1; }
for file in "$WORKFLOW" "$VERIFY" "$RUNNER" "$DOCKERFILE"; do [[ -f "$file" ]] || fail "Missing $file"; done
require() { grep -Fq -- "$1" "$WORKFLOW" || fail "Workflow missing: $1"; }

for value in \
  'name: ProjectPulse Deploy Stabilized Roles Permissions Timesheet Test' \
  "default: $EXPECTED" \
  "EXPECTED_RELEASE_COMMIT: $EXPECTED" \
  'DEPLOY-STABILIZED-ROLES-PERMISSIONS-TIMESHEET-TO-TEST' \
  'refs/heads/main' \
  'environment: test' \
  'PTP_DB_HOST PTP_DB_PORT PTP_DB_NAME PTP_DB_USER PTP_DB_PASSWORD' \
  'control/deployment/containers/stabilized-role-timesheet-validator/Dockerfile control' \
  'Verify live roles permissions users and timer targets before deployment' \
  'run: bash control/scripts/run-stabilized-role-permission-timesheet-validation-job.sh' \
  '/api/runtime/role-policy/summary' \
  '/api/runtime/role-policy/matrix' \
  '/api/runtime/timesheet/steward/users' \
  '/api/timesheet/timers/targets' \
  'projectPulseViewAsUser' \
  'PROJECT_TEAM_COORDINATOR' \
  'SUPER_ADMINISTRATOR' \
  'Roll back API and web images on failure'
do
  require "$value"
done

for value in \
  'BEGIN READ ONLY;' \
  'STABILIZED_ROLE_COUNT=' \
  'STABILIZED_MODULE_COUNT=' \
  'STABILIZED_ELIGIBLE_USER_COUNT=' \
  'STABILIZED_ASSIGNMENT_TARGET_COUNT=' \
  'STABILIZED_REGULAR_TASK_COUNT=' \
  'STABILIZED_SERVICE_REQUEST_TASK_COUNT=' \
  'STABILIZED_NON_PROJECT_COUNT=' \
  'STABILIZED_ROLE_PERMISSION_TIMESHEET_DATA=PASS'
do
  grep -Fq -- "$value" "$VERIFY" || fail "Read-only verifier missing: $value"
done

for forbidden in \
  'INSERT ' 'UPDATE ' 'DELETE ' 'ALTER ' 'DROP ' 'TRUNCATE ' 'CREATE TABLE' 'COMMIT;'
do
  grep -Eq "(^|[[:space:]])${forbidden}" "$VERIFY" && fail "Verifier contains mutation token: $forbidden"
done

for forbidden in \
  'environment: production' 'DEPLOY-PRODUCTION' 'workflow_run:' 'push:' \
  'az role assignment create' 'database/migrations/' 'psql '
do
  grep -Fq -- "$forbidden" "$WORKFLOW" && fail "Forbidden workflow behavior: $forbidden"
done

[[ "$(grep -Fc 'az containerapp update' "$WORKFLOW")" == 4 ]] || fail 'Expected API/web deployment plus API/web rollback.'
[[ "$(grep -Ec '^[[:space:]]*(API|WEB|VALIDATOR)_DIGEST=.*build-pr55-acr-image.sh' "$WORKFLOW")" == 3 ]] || fail 'Expected exactly three immutable image builds.'
[[ "$(grep -Fc 'application/json' "$WORKFLOW")" -ge 1 ]] || fail 'JSON endpoint validation is missing.'

for unreliable in "grep -Fq 'PtcTimeStewardGate'" "grep -Fq 'time_steward_role_required'"; do
  grep -Fq -- "$unreliable" "$WORKFLOW" && fail "Workflow relies on unstable or backend-only bundle marker: $unreliable"
done

grep -Fq 'EXPECTED_RELEASE_COMMIT="2216bfadaca76858fe07e8d1228df888688fd786"' "$RUNNER" || fail 'Runner release pin is wrong.'
grep -Fq 'STABILIZED_VALIDATION_JOB_CLEANUP=COMPLETE' "$RUNNER" || fail 'Private validation job cleanup is missing.'
grep -Fq 'verify-stabilized-role-permission-timesheet-test.sh' "$DOCKERFILE" || fail 'Validator image entrypoint is wrong.'

bash -n "$VERIFY"
bash -n "$RUNNER"
bash -n "$0"
echo 'STABILIZED_ROLE_PERMISSION_TIMESHEET_DEPLOYMENT_GUARD=PASS'
