#!/usr/bin/env bash
set -Eeuo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
WORKFLOW="$REPO_ROOT/.github/workflows/projectpulse-deploy-pr55-test.yml"
MIGRATOR="$REPO_ROOT/scripts/apply-pr55-test-migrations.sh"
ACR_BUILD_HELPER="$REPO_ROOT/scripts/build-pr55-acr-image.sh"
DATABASE_CONFIG="$REPO_ROOT/scripts/export-pr55-test-database-url.sh"
MIGRATION_JOB="$REPO_ROOT/scripts/run-pr55-test-migration-job.sh"
MIGRATION_JOB_IDENTITY_TEST="$REPO_ROOT/tests/test-pr55-migration-job-identity.sh"
ACR_BUILD_DIGEST_TEST="$REPO_ROOT/tests/test-pr55-acr-build-digest.sh"
MIGRATION_BUNDLE_TEST="$REPO_ROOT/tests/test-pr55-migration-bundle.sh"
MIGRATION_DOCKERFILE="$REPO_ROOT/deployment/containers/pr55-migrator/Dockerfile"
MIGRATION_034_SOURCE="$REPO_ROOT/database/migrations/034_module_026_crm_erp_integrations.sql"
MIGRATION_035_SOURCE="$REPO_ROOT/database/migrations/035_work_register_055c_055d_split.sql"
MIGRATION_036_SOURCE="$REPO_ROOT/database/migrations/036_work_register_role_scope_and_closeout_handoff.sql"
MIGRATION_037_SOURCE="$REPO_ROOT/database/migrations/037_work_register_dates_and_contract_types.sql"
MIGRATION_038_SOURCE="$REPO_ROOT/database/migrations/038_work_to_cash_lifecycle_and_audit.sql"
MIGRATION_039_SOURCE="$REPO_ROOT/database/migrations/039_work_to_cash_reactivation_lock_order.sql"
CI_WORKFLOW="$REPO_ROOT/.github/workflows/projectpulse-ci.yml"
GUIDE="$REPO_ROOT/docs/PR55-TEST-DEPLOYMENT-VERIFICATION.md"

fail() {
  echo "ERROR: $*" >&2
  exit 1
}

for file in \
  "$WORKFLOW" \
  "$MIGRATOR" \
  "$ACR_BUILD_HELPER" \
  "$DATABASE_CONFIG" \
  "$MIGRATION_JOB" \
  "$MIGRATION_JOB_IDENTITY_TEST" \
  "$ACR_BUILD_DIGEST_TEST" \
  "$MIGRATION_BUNDLE_TEST" \
  "$MIGRATION_DOCKERFILE" \
  "$MIGRATION_034_SOURCE" \
  "$MIGRATION_035_SOURCE" \
  "$MIGRATION_036_SOURCE" \
  "$MIGRATION_037_SOURCE" \
  "$MIGRATION_038_SOURCE" \
  "$MIGRATION_039_SOURCE" \
  "$CI_WORKFLOW" \
  "$GUIDE"; do
  [[ -f "$file" ]] || fail "Required deployment-safety file is missing: $file"
done

bash -n "$MIGRATOR"
bash -n "$ACR_BUILD_HELPER"
bash -n "$DATABASE_CONFIG"
bash -n "$MIGRATION_JOB"
bash -n "$MIGRATION_JOB_IDENTITY_TEST"
bash -n "$ACR_BUILD_DIGEST_TEST"
bash -n "$MIGRATION_BUNDLE_TEST"

EXPECTED_RELEASE="4cddc469f7bd20e4cb0e028e9ff1d47842ef7532"
EXPECTED_034="275c2f3f5ad56d80f303327baeb665506bc41014d52af8a2b7082c6e451974b9"
EXPECTED_035="87c6fcea07a25b829ca58c62c18992c9f01d8477a48b55f70aa1c710807b180d"
EXPECTED_036="b8f9dab7d7465ce06af2ee287867759ee718f6b7d1fc96d4b8629e65b58d80f3"
EXPECTED_037="00bd6bc9e4f63701831c03e75eb76b09914d7682a8511df27157feed22c311c5"
EXPECTED_038="19f4843d3501c9162ab04e50f820d921c026fb316ea565a0290d1409e53c790f"
EXPECTED_039="04a192736864c30ad60af7a4259d40159ceaddbf16c9dec2d7b5b6c6be4fb35c"

[[ "$(sha256sum "$MIGRATION_034_SOURCE" | awk '{print $1}')" == "$EXPECTED_034" ]] ||
  fail "Migration 034 source does not match its guarded checksum."
[[ "$(sha256sum "$MIGRATION_035_SOURCE" | awk '{print $1}')" == "$EXPECTED_035" ]] ||
  fail "Migration 035 source does not match its guarded checksum."
[[ "$(sha256sum "$MIGRATION_036_SOURCE" | awk '{print $1}')" == "$EXPECTED_036" ]] ||
  fail "Migration 036 source does not match its guarded checksum."
[[ "$(sha256sum "$MIGRATION_037_SOURCE" | awk '{print $1}')" == "$EXPECTED_037" ]] ||
  fail "Migration 037 source does not match its guarded checksum."
[[ "$(sha256sum "$MIGRATION_038_SOURCE" | awk '{print $1}')" == "$EXPECTED_038" ]] ||
  fail "Migration 038 source does not match its guarded checksum."
[[ "$(sha256sum "$MIGRATION_039_SOURCE" | awk '{print $1}')" == "$EXPECTED_039" ]] ||
  fail "Migration 039 source does not match its guarded checksum."

grep -Fq "$EXPECTED_RELEASE" "$WORKFLOW" || fail "Workflow is not pinned to the verified Work Register rollout commit."
grep -Fq "$EXPECTED_RELEASE" "$MIGRATOR" || fail "Migrator is not pinned to the verified Work Register rollout commit."
grep -Fq "$EXPECTED_RELEASE" "$MIGRATION_JOB" || fail "Migration job is not tagged with the verified Work Register rollout commit."
grep -Fq "$EXPECTED_034" "$MIGRATOR" || fail "Migration 034 checksum guard is missing."
grep -Fq "$EXPECTED_035" "$MIGRATOR" || fail "Migration 035 checksum guard is missing."
grep -Fq "$EXPECTED_036" "$MIGRATOR" || fail "Migration 036 checksum guard is missing."
grep -Fq "$EXPECTED_037" "$MIGRATOR" || fail "Migration 037 checksum guard is missing."
grep -Fq "$EXPECTED_038" "$MIGRATOR" || fail "Migration 038 checksum guard is missing."
grep -Fq "$EXPECTED_039" "$MIGRATOR" || fail "Migration 039 checksum guard is missing."
grep -Fq 'MIGRATION_036_APPLIED=YES' "$MIGRATOR" || fail "Migration 036 verification evidence is missing."
grep -Fq 'MIGRATION_037_APPLIED=YES' "$MIGRATOR" || fail "Migration 037 verification evidence is missing."
grep -Fq 'MIGRATION_038_APPLIED=YES' "$MIGRATOR" || fail "Migration 038 verification evidence is missing."
grep -Fq 'MIGRATION_039_APPLIED=YES' "$MIGRATOR" || fail "Migration 039 verification evidence is missing."
grep -Fq 'recognized contract variants remain unnormalized' "$MIGRATOR" ||
  fail "Migration 037 contract-normalization verification is missing."
grep -Fq 'lifecycle, audit, or live-source guards are incomplete' "$MIGRATOR" ||
  fail "Migration 038 lifecycle/audit verification is missing."
grep -Fq 'application-role lifecycle grants are incomplete' "$MIGRATOR" ||
  fail "Migration 038 application-role grant verification is missing."
grep -Fq 'invoice-reactivation advisory-lock order is incorrect' "$MIGRATOR" ||
  fail "Migration 039 advisory-lock-order verification is missing."
grep -Fq 'administrator Work Register grants are incomplete' "$MIGRATOR" ||
  fail "Migration 036 administrator-grant verification is missing."
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
[[ "$(grep -Fc 'az acr repository show' "$WORKFLOW")" -eq 1 ]] ||
  fail "Newly built images must use the ACR build result instead of immediate tag lookups."
grep -Fq '@$API_DIGEST' "$WORKFLOW" || fail "API deployment is not pinned to an immutable digest."
grep -Fq '@$WEB_DIGEST' "$WORKFLOW" || fail "Web deployment is not pinned to an immutable digest."
build_block="$(sed -n '/- name: Build exact API and web images/,/- name: Apply and verify migrations/p' "$WORKFLOW")"
grep -Fq 'working-directory: release' <<<"$build_block" || fail "ACR builds must run inside the exact release checkout."
grep -Fq 'test -f deployment/containers/api/Dockerfile' <<<"$build_block" || fail "API Dockerfile preflight is missing."
grep -Fq 'test -f deployment/containers/web/Dockerfile' <<<"$build_block" || fail "Web Dockerfile preflight is missing."
grep -Fq 'deployment/containers/api/Dockerfile' <<<"$build_block" || fail "API ACR build Dockerfile path is missing."
grep -Fq 'deployment/containers/web/Dockerfile' <<<"$build_block" || fail "Web ACR build Dockerfile path is missing."
[[ "$(grep -Fc 'build-pr55-acr-image.sh' <<<"$build_block")" -eq 3 ]] ||
  fail "API, web, and migration builds must share the guarded ACR digest helper."
[[ "$(grep -Ec '^[[:space:]]+\.$' <<<"$build_block")" -eq 2 ]] ||
  fail "Both ACR builds must submit the release checkout as dot context."
if grep -Eq '^[[:space:]]+release[[:space:]]*$' <<<"$build_block"; then
  fail "ACR builds must not resolve a sibling release context from the runner root."
fi
if grep -Fq 'az acr repository show' <<<"$build_block"; then
  fail "A newly pushed image tag must not be queried to recover its digest."
fi
grep -Fq -- '--no-logs' "$ACR_BUILD_HELPER" || fail "ACR build-result capture must suppress streamed logs."
grep -Fq -- "--query 'outputImages[0].digest'" "$ACR_BUILD_HELPER" ||
  fail "ACR build-result digest query is missing."
grep -Fq '^sha256:[0-9a-f]{64}$' "$ACR_BUILD_HELPER" ||
  fail "ACR build-result digests are not strictly validated."
if grep -Fq 'az acr repository show' "$ACR_BUILD_HELPER"; then
  fail "The ACR build helper must not perform a post-build tag lookup."
fi
grep -Fq 'Build checksum-pinned migration image' "$WORKFLOW" || fail "Migration image build step is missing."
grep -Fq 'project-health-dashboard-pr55-migrator' "$WORKFLOW" || fail "Dedicated migration image repository is missing."
grep -Fq 'IMMUTABLE_MIGRATION_IMAGE=' "$WORKFLOW" || fail "Migration image digest evidence is missing."
grep -Fq 'steps.migration_image.outputs.image' "$WORKFLOW" || fail "Migration job is not pinned to the built digest."
grep -Fq 'EXPECTED_FILES=(' "$WORKFLOW" || fail "Minimal migration build-context allowlist is missing."
[[ "$(grep -Fc 'release/database/migrations/036_work_register_role_scope_and_closeout_handoff.sql' "$WORKFLOW")" -eq 1 ]] ||
  fail "Migration 036 must be copied exactly once from the verified release."
[[ "$(grep -Fc '"$CONTEXT/migrations/036_work_register_role_scope_and_closeout_handoff.sql"' "$WORKFLOW")" -eq 1 ]] ||
  fail "Migration 036 must have exactly one destination in the immutable migration build context."
[[ "$(grep -Ec '^[[:space:]]+migrations/036_work_register_role_scope_and_closeout_handoff\.sql$' "$WORKFLOW")" -eq 1 ]] ||
  fail "Migration 036 must be allowlisted exactly once in the immutable migration build context."
[[ "$(grep -Fc 'release/database/migrations/037_work_register_dates_and_contract_types.sql' "$WORKFLOW")" -eq 1 ]] ||
  fail "Migration 037 must be copied exactly once from the verified release."
[[ "$(grep -Fc '"$CONTEXT/migrations/037_work_register_dates_and_contract_types.sql"' "$WORKFLOW")" -eq 1 ]] ||
  fail "Migration 037 must have exactly one destination in the immutable migration build context."
[[ "$(grep -Ec '^[[:space:]]+migrations/037_work_register_dates_and_contract_types\.sql$' "$WORKFLOW")" -eq 1 ]] ||
  fail "Migration 037 must be allowlisted exactly once in the immutable migration build context."
[[ "$(grep -Fc 'release/database/migrations/038_work_to_cash_lifecycle_and_audit.sql' "$WORKFLOW")" -eq 1 ]] ||
  fail "Migration 038 must be copied exactly once from the verified release."
[[ "$(grep -Fc '"$CONTEXT/migrations/038_work_to_cash_lifecycle_and_audit.sql"' "$WORKFLOW")" -eq 1 ]] ||
  fail "Migration 038 must have exactly one destination in the immutable migration build context."
[[ "$(grep -Ec '^[[:space:]]+migrations/038_work_to_cash_lifecycle_and_audit\.sql$' "$WORKFLOW")" -eq 1 ]] ||
  fail "Migration 038 must be allowlisted exactly once in the immutable migration build context."
[[ "$(grep -Fc 'release/database/migrations/039_work_to_cash_reactivation_lock_order.sql' "$WORKFLOW")" -eq 1 ]] ||
  fail "Migration 039 must be copied exactly once from the verified release."
[[ "$(grep -Fc '"$CONTEXT/migrations/039_work_to_cash_reactivation_lock_order.sql"' "$WORKFLOW")" -eq 1 ]] ||
  fail "Migration 039 must have exactly one destination in the immutable migration build context."
[[ "$(grep -Ec '^[[:space:]]+migrations/039_work_to_cash_reactivation_lock_order\.sql$' "$WORKFLOW")" -eq 1 ]] ||
  fail "Migration 039 must be allowlisted exactly once in the immutable migration build context."
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
web_validation_block="$(sed -n '/- name: Validate deployed web and release identity/,/- name: Write deployment evidence/p' "$WORKFLOW")"
grep -Fq 'EXPECTED_LABELS=(' <<<"$web_validation_block" || fail "Web readiness expected-label set is missing."
expected_labels_block="$(sed -n '/EXPECTED_LABELS=(/,/          )/p' <<<"$web_validation_block")"
for expected_label in \
  'Manage Existing Projects' \
  'Create New Project' \
  'MODULE 055C' \
  'MODULE 055D' \
  'MODULE 999'; do
  grep -Fq "'$expected_label'" <<<"$expected_labels_block" ||
    fail "Web readiness expected-label set is missing: $expected_label"
done
grep -Fq 'for attempt in $(seq 1 30); do' <<<"$web_validation_block" || fail "Five-minute web readiness retry loop is missing."
grep -Fq 'CACHE_BUSTER="pr55-${GITHUB_RUN_ID}-${GITHUB_RUN_ATTEMPT}-${attempt}"' <<<"$web_validation_block" ||
  fail "Per-attempt web cache buster is missing."
grep -Fq 'INDEX_URL="$BASE_URL/?release_check=$CACHE_BUSTER"' <<<"$web_validation_block" ||
  fail "HTML readiness request is not cache-busted."
grep -Fq 'ASSET_URL_WITH_BUSTER="$ASSET_URL?release_check=$CACHE_BUSTER"' <<<"$web_validation_block" ||
  fail "JavaScript readiness request is not cache-busted."
grep -Fq -- "--header 'Cache-Control: no-cache'" <<<"$web_validation_block" ||
  fail "Web readiness requests do not bypass intermediary caches."
grep -Fq 'for label in "${EXPECTED_LABELS[@]}"; do' <<<"$web_validation_block" ||
  fail "Expected release labels are not checked inside the readiness loop."
grep -Fq 'MISSING_LABELS+=("$label")' <<<"$web_validation_block" ||
  fail "Web readiness does not retain missing-label evidence."
grep -Fq 'WEB_READINESS_ATTEMPT=' <<<"$web_validation_block" ||
  fail "Web readiness does not print per-attempt diagnostics."
grep -Fq "WEB_CONTENT_READY='true'" <<<"$web_validation_block" ||
  fail "Web readiness cannot distinguish expected release content from a stale HTTP 200."
grep -Fq '(( attempt < 30 )) && sleep 10' <<<"$web_validation_block" ||
  fail "Web readiness no longer preserves the bounded five-minute wait."
grep -Fq 'Active API image does not match the expected release image:' <<<"$web_validation_block" ||
  fail "API image identity failure does not print actionable evidence."
grep -Fq 'Active web image does not match the expected release image:' <<<"$web_validation_block" ||
  fail "Web image identity failure does not print actionable evidence."
if grep -Fq '[[ "$STATUS" == '\''200'\'' ]] && break' <<<"$web_validation_block"; then
  fail "Web readiness must not accept HTTP 200 before expected release content is verified."
fi
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
"$ACR_BUILD_DIGEST_TEST"
"$MIGRATION_BUNDLE_TEST"

echo "PR55_TEST_DEPLOYMENT_VALIDATION=PASS"
echo "EXPECTED_RELEASE_COMMIT=$EXPECTED_RELEASE"
echo "MIGRATION_034_CHECKSUM=$EXPECTED_034"
echo "MIGRATION_035_CHECKSUM=$EXPECTED_035"
echo "MIGRATION_036_CHECKSUM=$EXPECTED_036"
echo "MIGRATION_037_CHECKSUM=$EXPECTED_037"
echo "MIGRATION_038_CHECKSUM=$EXPECTED_038"
echo "MIGRATION_039_CHECKSUM=$EXPECTED_039"
