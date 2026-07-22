#!/usr/bin/env bash
set -Eeuo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
WORKFLOW="$REPO_ROOT/.github/workflows/projectpulse-deploy-pr55-test.yml"
MIGRATOR="$REPO_ROOT/scripts/apply-pr55-test-migrations.sh"
DATABASE_CONFIG="$REPO_ROOT/scripts/export-pr55-test-database-url.sh"
MIGRATION_JOB="$REPO_ROOT/scripts/run-pr55-test-migration-job.sh"
MIGRATION_JOB_IDENTITY_TEST="$REPO_ROOT/tests/test-pr55-migration-job-identity.sh"
MIGRATION_DOCKERFILE="$REPO_ROOT/deployment/containers/pr55-migrator/Dockerfile"
CI_WORKFLOW="$REPO_ROOT/.github/workflows/projectpulse-ci.yml"
GUIDE="$REPO_ROOT/docs/PR55-TEST-DEPLOYMENT-VERIFICATION.md"

fail() {
  echo "ERROR: $*" >&2
  exit 1
}

for file in \
  "$WORKFLOW" \
  "$MIGRATOR" \
  "$DATABASE_CONFIG" \
  "$MIGRATION_JOB" \
  "$MIGRATION_JOB_IDENTITY_TEST" \
  "$MIGRATION_DOCKERFILE" \
  "$CI_WORKFLOW" \
  "$GUIDE"; do
  [[ -f "$file" ]] || fail "Required deployment-safety file is missing: $file"
done

bash -n "$MIGRATOR"
bash -n "$DATABASE_CONFIG"
bash -n "$MIGRATION_JOB"
bash -n "$MIGRATION_JOB_IDENTITY_TEST"

EXPECTED_RELEASE="ea23da6cfdd21a9444489ee4ffd14a6555de8c34"
EXPECTED_034="275c2f3f5ad56d80f303327baeb665506bc41014d52af8a2b7082c6e451974b9"
EXPECTED_035="87c6fcea07a25b829ca58c62c18992c9f01d8477a48b55f70aa1c710807b180d"

grep -Fq "$EXPECTED_RELEASE" "$WORKFLOW" || fail "Workflow is not pinned to the PR #55 merge commit."
grep -Fq "$EXPECTED_RELEASE" "$MIGRATOR" || fail "Migrator is not pinned to the PR #55 merge commit."
grep -Fq "$EXPECTED_034" "$MIGRATOR" || fail "Migration 034 checksum guard is missing."
grep -Fq "$EXPECTED_035" "$MIGRATOR" || fail "Migration 035 checksum guard is missing."
grep -Fq '.projectpulse-release-commit' "$MIGRATOR" || fail "Containerized release-marker guard is missing."
grep -Fq 'run: bash control/scripts/export-pr55-test-database-url.sh' "$WORKFLOW" || fail "Azure database configuration loader is missing."
if grep -Fq 'secrets.PROJECTPULSE_TEST_DATABASE_URL' "$WORKFLOW"; then
  fail "The workflow must not require a separately copied GitHub database secret."
fi
grep -Fq 'az containerapp show' "$DATABASE_CONFIG" || fail "Container App environment lookup is missing."
grep -Fq 'az containerapp secret list' "$DATABASE_CONFIG" || fail "Container App secret lookup is missing."
grep -Fq -- '--show-values' "$DATABASE_CONFIG" || fail "Container App secret values are not explicitly requested."
grep -Fq 'PTP_DB_PASSWORD' "$DATABASE_CONFIG" || fail "Database password lookup is missing."
grep -Fq 'secretRef' "$DATABASE_CONFIG" || fail "Container App secret-reference resolution is missing."
grep -Fq 'mask_value' "$DATABASE_CONFIG" || fail "Database masking guard is missing."
grep -Fq 'value="${value//%/%25}"' "$DATABASE_CONFIG" || fail "GitHub command masking does not escape percent characters."
grep -Fq 'urllib.parse import quote' "$DATABASE_CONFIG" || fail "Database URI encoding is missing."
grep -Fq 'GITHUB_ENV' "$DATABASE_CONFIG" || fail "Ephemeral runner environment export is missing."
grep -Fq 'environment: test' "$WORKFLOW" || fail "Workflow is not restricted to the test environment."
grep -Fq 'refs/heads/main' "$WORKFLOW" || fail "Main-branch dispatch guard is missing."
grep -Fq 'DEPLOY-PR55-TO-TEST' "$WORKFLOW" || fail "Explicit deployment confirmation is missing."
grep -Fq 'apply-pr55-test-migrations.sh' "$WORKFLOW" || fail "Atomic migration step is missing."
grep -Fq 'run-pr55-test-migration-job.sh' "$WORKFLOW" || fail "Private-network migration job is missing."
if grep -Fq 'run: control/scripts/apply-pr55-test-migrations.sh release' "$WORKFLOW"; then
  fail "Migrations must not run directly on the public GitHub runner."
fi
if grep -Fq 'Install PostgreSQL client' "$WORKFLOW"; then
  fail "The GitHub runner must not install a database client for the private test database."
fi
grep -Fq 'old_api_image' "$WORKFLOW" || fail "API rollback image capture is missing."
grep -Fq 'old_web_image' "$WORKFLOW" || fail "Web rollback image capture is missing."
grep -Fq 'resolve_acr_image_digest' "$WORKFLOW" || fail "Immutable rollback-image resolution is missing."
grep -Fq 'CURRENT_API_IMAGE_IMMUTABLE' "$WORKFLOW" || fail "Immutable API rollback evidence is missing."
grep -Fq 'CURRENT_WEB_IMAGE_IMMUTABLE' "$WORKFLOW" || fail "Immutable web rollback evidence is missing."
grep -Fq 'activeRevisionsMode' "$WORKFLOW" || fail "Single-revision traffic guard is missing."
grep -Fq 'az acr repository show' "$WORKFLOW" || fail "Immutable image digest resolution is missing."
grep -Fq '@$API_DIGEST' "$WORKFLOW" || fail "API deployment is not pinned to an immutable digest."
grep -Fq '@$WEB_DIGEST' "$WORKFLOW" || fail "Web deployment is not pinned to an immutable digest."
build_block="$(sed -n '/- name: Build exact API and web images/,/- name: Apply and verify migrations/p' "$WORKFLOW")"
grep -Fq 'working-directory: release' <<<"$build_block" || fail "ACR builds must run inside the exact release checkout."
grep -Fq 'test -f deployment/containers/api/Dockerfile' <<<"$build_block" || fail "API Dockerfile preflight is missing."
grep -Fq 'test -f deployment/containers/web/Dockerfile' <<<"$build_block" || fail "Web Dockerfile preflight is missing."
grep -Fq -- '--file deployment/containers/api/Dockerfile' <<<"$build_block" || fail "API ACR build Dockerfile path is missing."
grep -Fq -- '--file deployment/containers/web/Dockerfile' <<<"$build_block" || fail "Web ACR build Dockerfile path is missing."
[[ "$(grep -Ec '^[[:space:]]+\.$' <<<"$build_block")" -eq 2 ]] ||
  fail "Both ACR builds must submit the release checkout as dot context."
if grep -Eq '^[[:space:]]+release[[:space:]]*$' <<<"$build_block"; then
  fail "ACR builds must not resolve a sibling release context from the runner root."
fi
grep -Fq 'Build checksum-pinned migration image' "$WORKFLOW" || fail "Migration image build step is missing."
grep -Fq 'project-health-dashboard-pr55-migrator' "$WORKFLOW" || fail "Dedicated migration image repository is missing."
grep -Fq 'IMMUTABLE_MIGRATION_IMAGE=' "$WORKFLOW" || fail "Migration image digest evidence is missing."
grep -Fq 'steps.migration_image.outputs.image' "$WORKFLOW" || fail "Migration job is not pinned to the built digest."
grep -Fq 'EXPECTED_FILES=(' "$WORKFLOW" || fail "Minimal migration build-context allowlist is missing."
grep -Fq 'COPY release-commit .projectpulse-release-commit' "$MIGRATION_DOCKERFILE" || fail "Migration image release marker is missing."
grep -Fq 'COPY migrations/ database/migrations/' "$MIGRATION_DOCKERFILE" || fail "Migration image does not contain the pinned SQL files."
grep -Fq 'ENTRYPOINT ["/usr/local/bin/apply-pr55-test-migrations.sh", "/opt/projectpulse/release"]' "$MIGRATION_DOCKERFILE" ||
  fail "Migration image entrypoint is not pinned to the guarded migrator."
grep -Fq 'properties.managedEnvironmentId' "$MIGRATION_JOB" || fail "Migration job does not inherit the API Container Apps environment."
grep -Fq 'az containerapp job create' "$MIGRATION_JOB" || fail "Temporary migration job creation is missing."
grep -Fq 'az containerapp job start' "$MIGRATION_JOB" || fail "Temporary migration job execution is missing."
grep -Fq 'az containerapp job execution list' "$MIGRATION_JOB" || fail "Migration job status verification is missing."
grep -Fq 'az containerapp job delete' "$MIGRATION_JOB" || fail "Temporary migration job cleanup is missing."
grep -Fq -- '--replica-retry-limit 0' "$MIGRATION_JOB" || fail "Migration job retries must be disabled."
grep -Fq 'PROJECTPULSE_TEST_DATABASE_URL=secretref:pr55-db-url' "$MIGRATION_JOB" ||
  fail "Database URI is not passed through a temporary Container Apps secret."
grep -Fq 'MIGRATION_IMAGE" == "$ACR_NAME.azurecr.io/"*@sha256:' "$MIGRATION_JOB" ||
  fail "Migration job does not require an immutable approved-ACR image."
grep -Fq 'REGISTRY_IDENTITY_LOWER="${REGISTRY_IDENTITY,,}"' "$MIGRATION_JOB" ||
  fail "Migration job does not normalize Azure identity resource-ID casing."
grep -Fq '^/subscriptions/[^/]+/resourcegroups/[^/]+/providers/microsoft\.managedidentity/userassignedidentities/[^/]+$' "$MIGRATION_JOB" ||
  fail "Migration job does not restrict identity reuse to user-assigned identity resource IDs."
grep -Fq 'identity.userAssignedIdentities | keys(@)' "$MIGRATION_JOB" ||
  fail "Migration job does not verify that the ACR identity is assigned to the API app."
grep -Fq 'job_identity_args+=(--mi-user-assigned "$REGISTRY_IDENTITY")' "$MIGRATION_JOB" ||
  fail "Migration job does not assign the reusable user identity to the temporary job."
grep -Fq 'registry_args+=(--registry-identity "$REGISTRY_IDENTITY")' "$MIGRATION_JOB" ||
  fail "Migration job does not use the reusable identity for the private registry."
grep -Fq -- '--expose-token' "$MIGRATION_JOB" || fail "Ephemeral ACR-token fallback is missing."
grep -Fq "REGISTRY_USERNAME='00000000-0000-0000-0000-000000000000'" "$MIGRATION_JOB" ||
  fail "The documented ACR token username is missing."
grep -Fq 'Remove temporary PR55 migration job' "$WORKFLOW" || fail "Always-run migration cleanup step is missing."
cleanup_block="$(sed -n '/- name: Remove temporary PR55 migration job/,/- name: Deploy API/p' "$WORKFLOW")"
grep -Fq 'if: ${{ always() }}' <<<"$cleanup_block" || fail "Migration cleanup must run after success, failure, or cancellation."
grep -Fq 'az containerapp job delete' <<<"$cleanup_block" || fail "Workflow-level migration cleanup deletion is missing."
grep -Fq 'az containerapp job list' <<<"$cleanup_block" || fail "Workflow-level migration cleanup verification is missing."
grep -Fq 'The temporary migration job or its secret still exists. Deployment stopped.' <<<"$cleanup_block" ||
  fail "Deployment does not stop when migration-job cleanup is incomplete."
grep -Fq 'Roll back application images on failure' "$WORKFLOW" || fail "Failure rollback step is missing."
grep -Fq '/health' "$WORKFLOW" || fail "Public API health check is missing."
grep -Fq '/api/version' "$WORKFLOW" || fail "Public version check is missing."
grep -Fq '/api/integrations/026/providers' "$WORKFLOW" || fail "Module 026 route check is missing."
grep -Fq '/api/work-register/overview' "$WORKFLOW" || fail "Work Register route check is missing."
grep -Fq 'Manage Existing Projects' "$WORKFLOW" || fail "Module 055C frontend check is missing."
grep -Fq 'Create New Project' "$WORKFLOW" || fail "Module 055D frontend check is missing."
grep -Fq 'scripts/validate-pr55-test-deployment.sh' "$CI_WORKFLOW" || fail "CI does not enforce this deployment guard."

azure_login_line="$(grep -n 'uses: azure/login@v2' "$WORKFLOW" | head -1 | cut -d: -f1)"
database_config_line="$(grep -n 'export-pr55-test-database-url.sh' "$WORKFLOW" | head -1 | cut -d: -f1)"
migration_image_line="$(grep -n -- '- name: Build checksum-pinned migration image' "$WORKFLOW" | head -1 | cut -d: -f1)"
migration_line="$(grep -n 'run-pr55-test-migration-job.sh' "$WORKFLOW" | head -1 | cut -d: -f1)"
cleanup_line="$(grep -n -- '- name: Remove temporary PR55 migration job' "$WORKFLOW" | head -1 | cut -d: -f1)"
api_deploy_line="$(grep -n -- '- name: Deploy API' "$WORKFLOW" | head -1 | cut -d: -f1)"

[[ -n "$azure_login_line" && -n "$database_config_line" && -n "$migration_image_line" && -n "$migration_line" && -n "$cleanup_line" && -n "$api_deploy_line" ]] ||
  fail "Cannot determine Azure login, database configuration, private migration, cleanup, and deployment ordering."
(( azure_login_line < database_config_line )) ||
  fail "Azure login must complete before database configuration is read."
(( database_config_line < migration_image_line )) ||
  fail "Database configuration must be ready before the migration image is built."
(( migration_image_line < migration_line )) ||
  fail "The immutable migration image must exist before the private job starts."
(( migration_line < cleanup_line && cleanup_line < api_deploy_line )) ||
  fail "The migration job must succeed and be cleaned up before API deployment starts."

"$MIGRATION_JOB_IDENTITY_TEST"

echo "PR55_TEST_DEPLOYMENT_VALIDATION=PASS"
echo "EXPECTED_RELEASE_COMMIT=$EXPECTED_RELEASE"
echo "MIGRATION_034_CHECKSUM=$EXPECTED_034"
echo "MIGRATION_035_CHECKSUM=$EXPECTED_035"
