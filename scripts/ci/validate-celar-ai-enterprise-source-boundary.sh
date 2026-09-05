#!/usr/bin/env bash
set -Eeuo pipefail

BASE_BRANCH="${GITHUB_BASE_REF:-main}"
HEAD_BRANCH="${GITHUB_HEAD_REF:-${GITHUB_REF_NAME:-}}"

git fetch origin "$BASE_BRANCH" --no-tags
BASE="$(git merge-base "origin/$BASE_BRANCH" HEAD)"
test -n "$BASE"
CHANGED="$(git diff --name-only "$BASE"...HEAD)"
printf '%s\n' "$CHANGED"
test -n "$CHANGED"

publish_mode() {
  local mode="$1"
  if [[ -n "${GITHUB_ENV:-}" ]]; then
    printf 'CELAR_AI_ENTERPRISE_VALIDATION_MODE=%s\n' "$mode" >> "$GITHUB_ENV"
  fi
  printf 'CELAR_AI_ENTERPRISE_VALIDATION_MODE=%s\n' "$mode"
}

if [[ "$HEAD_BRANCH" == 'feature/deepseek-v4-dgx-primary-20260904' ]]; then
  node tests/validate-deepseek-release-scope.mjs
  publish_mode DEEPSEEK_V4_PROVIDER
  exit 0
fi

if [[ "$HEAD_BRANCH" == 'fix/module025-protected-uat-generation-verification-detailed-plan-parser-20260903' ]]; then
  ALLOWED_DATABASE='^(database/migrations/061_celar_ai_capability_routing\.sql|database/rollback/061_celar_ai_capability_routing_rollback\.sql)$'
  publish_mode MODULE025_DETAILED_PLAN_PARSER
elif [[ "$HEAD_BRANCH" == 'fix/protected-uat-validation-defects-20260903' ]]; then
  ALLOWED_DATABASE='^(database/migrations/100_module001b_catalog_ownership_reconciliation\.sql|database/rollback/100_module001b_catalog_ownership_reconciliation_rollback\.sql)$'
  publish_mode PROTECTED_UAT_VALIDATION_DEFECTS
elif [[ "$HEAD_BRANCH" == security/celar-ai-production-readiness-* ]]; then
  ALLOWED_DATABASE='^(database/migrations/071_ai_runtime_production_hardening\.sql|database/rollback/071_ai_runtime_production_hardening_rollback\.sql)$'
  publish_mode PRODUCTION_HARDENING
elif [[ "$HEAD_BRANCH" == release/consolidated-enterprise-validation-* ]]; then
  ALLOWED_DATABASE='^(database/(migrations/(061_celar_ai_capability_routing|062_super_administrator_permanent_full_control|063_project_management_billing_role_access_repair|064_module_065_enterprise_notification_orchestration)\.sql|rollback/(061_celar_ai_capability_routing_rollback|062_super_administrator_permanent_full_control_rollback|063_project_management_billing_role_access_repair_rollback|064_module_065_enterprise_notification_orchestration_rollback)\.sql))$'
  publish_mode CONSOLIDATED_RELEASE
elif [[ "$HEAD_BRANCH" == feature/celar-ai-unified-chat-routing-attachments-* ]]; then
  ALLOWED_DATABASE='^(database/migrations/072_celar_ai_conversation_attachments\.sql|database/rollback/072_celar_ai_conversation_attachments_rollback\.sql)$'
  publish_mode UNIFIED_CHAT_ATTACHMENTS
elif [[ "$HEAD_BRANCH" == feature/celar-ai-flowhive-production-* ]]; then
  ALLOWED_DATABASE='^(database/migrations/074_module_066_project_flowhive_production\.sql|database/rollback/074_module_066_project_flowhive_production_rollback\.sql)$'
  publish_mode FLOWHIVE_PRODUCTION
elif [[ "$HEAD_BRANCH" == feature/module-066-flowhive-ai-planner-* ]]; then
  ALLOWED_DATABASE='^(database/migrations/079_coordinated_runtime_ai_document_rbac_repair\.sql|database/rollback/079_coordinated_runtime_ai_document_rbac_repair_rollback\.sql)$'
  test -f tests/test-coordinated-document-bridge-migration-079.sh
  publish_mode COORDINATED_FLOWHIVE_DOCUMENT_BRIDGE
elif [[ "$HEAD_BRANCH" == feature/module-066-flowhive-enterprise-pm-* ]]; then
  ALLOWED_DATABASE='^(database/migrations/086_module_066_flowhive_enterprise_pm\.sql|database/rollback/086_module_066_flowhive_enterprise_pm_rollback\.sql)$'
  test -f tests/test-module-066-flowhive-enterprise-pm-migration-086.sh
  publish_mode FLOWHIVE_ENTERPRISE_PM
elif [[ "$HEAD_BRANCH" == feature/celar-ai-internal-data-intelligence-* ]]; then
  ALLOWED_DATABASE='^(database/migrations/(080_celar_ai_internal_data_intelligence|081_celar_ai_private_runtime_activation)\.sql|database/rollback/(080_celar_ai_internal_data_intelligence_rollback|081_celar_ai_private_runtime_activation_rollback)\.sql)$'
  test -f tests/test-celar-ai-internal-data-migration-080.sh
  test -f tests/test-celar-ai-private-runtime-activation-migration-081.sh
  test -f .github/workflows/projectpulse-deploy-celar-ai-private-runtime-test.yml
  publish_mode PRIVATE_RUNTIME_INTERNAL_DATA
elif [[ "$HEAD_BRANCH" == fix/shared-project-document-planning-* ]]; then
  FLOWHIVE_RELEASE_MANIFEST='.github/shared-project-document-planning-governed-release-files.txt'
  ALLOWED_DATABASE='^(database/(migrations/(094_flowhive_canonical_sow_authority|095_project_planning_collaboration_access|096_project_planning_document_authority|097_project_planning_identity_safe_admission)\.sql|rollback/(095_project_planning_collaboration_access_rollback|096_project_planning_document_authority_rollback|097_project_planning_identity_safe_admission_rollback)\.sql))$'
  for required in \
    "$FLOWHIVE_RELEASE_MANIFEST" \
    '.github/workflows/celar-ai-enterprise-platform-ci.yml' \
    '.github/workflows/module030-analytics-center-ci.yml' \
    '.github/workflows/projectpulse-deploy-test.yml' \
    '.github/workflows/projectpulse-release-test-control-ci.yml' \
    'scripts/ci/validate-celar-ai-enterprise-source-boundary.sh' \
    'database/migrations/094_flowhive_canonical_sow_authority.sql' \
    'database/migrations/095_project_planning_collaboration_access.sql' \
    'database/migrations/096_project_planning_document_authority.sql' \
    'database/migrations/097_project_planning_identity_safe_admission.sql' \
    'database/rollback/095_project_planning_collaboration_access_rollback.sql' \
    'database/rollback/096_project_planning_document_authority_rollback.sql' \
    'database/rollback/097_project_planning_identity_safe_admission_rollback.sql' \
    'tests/test-flowhive-canonical-sow-authority-migration-094.sh' \
    'tests/test-project-planning-collaboration-migration-095.sh' \
    'tests/test-project-planning-document-authority-migration-096.sh' \
    'tests/test-project-planning-identity-safe-admission-migration-097.sh' \
    'tests/test-pulse-ai-runtime-job-query-shape.sh'; do
    test -f "$required"
    grep -Fxq "$required" <<<"$CHANGED"
    grep -Fxq "$required" "$FLOWHIVE_RELEASE_MANIFEST"
  done
  publish_mode FLOWHIVE_V2_SHARED_PLANNING
elif grep -Fxq 'src/backend/ProjectTime.Api/Modules/ProjectForgeModule.cs' <<<"$CHANGED"; then
  ALLOWED_DATABASE='^(database/migrations/(070_module_033_project_forge|073_module_033_project_forge_interactive)\.sql|database/rollback/(070_module_033_project_forge_rollback|073_module_033_project_forge_interactive_rollback)\.sql)$'
  publish_mode MODULE_033_PROJECT_FORGE
elif grep -Fxq 'tests/test-uat-functional-completion-contract.sh' <<<"$CHANGED"; then
  ALLOWED_DATABASE='^(database/migrations/066_immutable_project_numbers\.sql|database/rollback/066_immutable_project_numbers_rollback\.sql)$'
  publish_mode UAT_FUNCTIONAL_COMPLETION
else
  ALLOWED_DATABASE='^(database/migrations/061_celar_ai_capability_routing\.sql|database/rollback/061_celar_ai_capability_routing_rollback\.sql)$'
  publish_mode OWNED_SOURCE
fi

DISALLOWED_DATABASE="$(grep '^database/' <<<"$CHANGED" | grep -Ev "$ALLOWED_DATABASE" || true)"
if [[ -n "$DISALLOWED_DATABASE" ]]; then
  echo 'The Celar AI enterprise package changed an unapproved database file for this validation mode:' >&2
  printf '%s\n' "$DISALLOWED_DATABASE" >&2
  exit 1
fi

PROHIBITED="$(grep -E '^(deployment/|scripts/.*deploy|\.github/workflows/projectpulse-deploy-|src/backend/ProjectTime\.Api/Ai/(ProjectPulseAiConfiguration|ProjectPulseAiRemoteProviders|ProjectPulseAiSecretStore)\.cs|src/backend/ProjectTime\.Api/Modules/AiProviderConfigurationModule\.cs)' <<<"$CHANGED" || true)"
if [[ "$HEAD_BRANCH" == security/celar-ai-production-readiness-* ]]; then
  PROHIBITED="$(grep -Fvx \
    -e 'src/backend/ProjectTime.Api/Ai/ProjectPulseAiSecretStore.cs' \
    -e 'src/backend/ProjectTime.Api/Modules/AiProviderConfigurationModule.cs' \
    <<<"$PROHIBITED" || true)"
  test -f .github/workflows/celar-ai-production-hardening-ci.yml
  test -f database/migrations/071_ai_runtime_production_hardening.sql
  test -f database/rollback/071_ai_runtime_production_hardening_rollback.sql
  test -f src/frontend/project-time-web/scripts/validate-celar-ai-production-readiness.mjs
fi
if [[ "$HEAD_BRANCH" == release/consolidated-enterprise-validation-* ]]; then
  PROHIBITED="$(grep -Fvx 'deployment/containers/web/Dockerfile' <<<"$PROHIBITED" || true)"
fi
if [[ "$HEAD_BRANCH" == feature/celar-ai-flowhive-production-* ]]; then
  PROHIBITED="$(grep -Fvx 'src/backend/ProjectTime.Api/Modules/AiProviderConfigurationModule.cs' <<<"$PROHIBITED" || true)"
fi
if [[ "$HEAD_BRANCH" == feature/celar-ai-internal-data-intelligence-* ]]; then
  PROHIBITED="$(grep -Fvx \
    '.github/workflows/projectpulse-deploy-celar-ai-private-runtime-test.yml' \
    <<<"$PROHIBITED" || true)"
elif [[ "$HEAD_BRANCH" == fix/timesheet-ai-opencloud-deferred-release-* ]]; then
  PROHIBITED="$(grep -Fvx \
    -e '.github/workflows/projectpulse-deploy-celar-ai-private-runtime-test.yml' \
    -e 'deployment/environments/opencloud-template.yml' \
    -e 'deployment/podman/README.md' \
    -e 'deployment/podman/compose.yml' \
    -e 'deployment/podman/private-runtime.env.example' \
    <<<"$PROHIBITED" || true)"
  for required in \
    '.github/workflows/projectpulse-deploy-celar-ai-private-runtime-test.yml' \
    'deployment/environments/opencloud-template.yml' \
    'deployment/podman/README.md' \
    'deployment/podman/compose.yml' \
    'deployment/podman/private-runtime.env.example' \
    'tests/validate-celar-ai-opencloud-deferred-runtime.mjs'; do
    test -f "$required"
    grep -Fxq "$required" <<<"$CHANGED"
  done
  grep -Fq 'workflow_dispatch:' .github/workflows/projectpulse-deploy-celar-ai-private-runtime-test.yml
  grep -Fq 'DEPLOY-CELAR-AI-OPENCLOUD-RUNTIME-TO-TEST' .github/workflows/projectpulse-deploy-celar-ai-private-runtime-test.yml
  ! grep -Eq '^[[:space:]]{2}push:' .github/workflows/projectpulse-deploy-celar-ai-private-runtime-test.yml
  grep -Fq 'status: deferred-until-opencloud' deployment/environments/opencloud-template.yml
elif [[ "$HEAD_BRANCH" == release/test-b1335bd2-timesheet-ai-* ]]; then
  CONTROLLER='.github/workflows/projectpulse-deploy-test.yml'
  CONTROLLER_VALIDATOR='.github/workflows/projectpulse-release-test-control-ci.yml'
  PRIVATE_RUNTIME='.github/workflows/projectpulse-deploy-celar-ai-private-runtime-test.yml'
  HISTORICAL_HOTFIX='.github/workflows/projectpulse-deploy-modules-runtime-hotfix-test.yml'
  MIGRATOR='scripts/release-test/apply-celar-ai-internal-data-080.sh'
  DEFERRAL_TEST='tests/validate-celar-ai-opencloud-deferred-runtime.mjs'
  PROHIBITED="$(grep -Fvx \
    -e "$CONTROLLER" \
    -e "$CONTROLLER_VALIDATOR" \
    -e "$PRIVATE_RUNTIME" \
    -e "$HISTORICAL_HOTFIX" \
    <<<"$PROHIBITED" || true)"
  for required in "$CONTROLLER" "$CONTROLLER_VALIDATOR" "$PRIVATE_RUNTIME" "$HISTORICAL_HOTFIX" "$MIGRATOR" "$DEFERRAL_TEST"; do
    test -f "$required"
    grep -Fxq "$required" <<<"$CHANGED"
  done
  grep -Fq 'b1335bd2426d061f85498ace7c7b2a70c3b5bdc6' "$CONTROLLER"
  grep -Fq '080_celar_ai_internal_data_intelligence.sql' "$CONTROLLER"
  grep -Fq 'currentDescription:"Working on training"' "$CONTROLLER"
  grep -Fq 'group: projectpulse-deploy-test' "$CONTROLLER"
  grep -Fq 'queue: max' "$CONTROLLER"
  ! grep -Fq '081_celar_ai_private_runtime_activation' "$CONTROLLER"
  grep -Fq 'PRIVATE_RUNTIME_MIGRATION_081=ABSENT' "$MIGRATOR"
  for deferred in "$PRIVATE_RUNTIME" "$HISTORICAL_HOTFIX"; do
    ! grep -Eq 'azure/[l]ogin@' "$deferred"
    ! grep -Fq 'environment: test' "$deferred"
    ! grep -Eq 'id-token:[[:space:]]+[w]rite' "$deferred"
  ! grep -Eq '^[[:space:]]{2}push:' "$deferred"
  done
fi

if [[ "$HEAD_BRANCH" == 'fix/protected-uat-validation-defects-20260903' ]]; then
  CONTROLLER='.github/workflows/projectpulse-deploy-test.yml'
  PROHIBITED="$(grep -Fvx "$CONTROLLER" <<<"$PROHIBITED" || true)"
  cat > "${TMPDIR:-/tmp}/protected-uat-validation-defects-expected-files" <<'FILES'
  .github/workflows/module-loading-assignment-propagation-ci.yml
  .github/workflows/module-management-owner-drawer-ci.yml
  .github/workflows/projectpulse-deploy-test.yml
  .github/workflows/projectpulse-release-test-control-ci-reregistered.yml
  .github/workflows/projectpulse-release-test-control-ci.yml
  database/migrations/100_module001b_catalog_ownership_reconciliation.sql
  database/rollback/100_module001b_catalog_ownership_reconciliation_rollback.sql
  scripts/ci/validate-celar-ai-enterprise-source-boundary.sh
  scripts/release-test/build-and-run-project-planning-document-authority-migration-job.sh
  scripts/release-test/run-module025-sow-gsd-protected-test-uat.sh
  src/backend/ProjectTime.Api/Ai/CelarAiInternalDataService.cs
  src/backend/ProjectTime.Api/Ai/PulseAiPrivateRagService.cs
  src/backend/ProjectTime.Api/Modules/Module025SowGsdDocumentExporter.cs
  src/backend/ProjectTime.Api/Modules/Module025SowGsdModule.cs
  src/backend/ProjectTime.Api/Modules/ModuleCatalogOwnershipModule.cs
  src/backend/ProjectTime.Api/Modules/ProjectFlowHiveAiPlannerOrchestrationModule.cs
  src/frontend/project-time-web/src/ProjectFlowHiveCenter.jsx
  src/frontend/project-time-web/src/module025/SowGsdWorkspace.jsx
  src/frontend/project-time-web/src/project-flowhive-center.css
  tests/CelarAiInternalDataTests/Program.cs
  tests/test-module-catalog-owner-repair-migration-093.sh
  tests/validate-celar-ai-pr630-consolidated.mjs
  tests/validate-flowhive-sow-evidence-autoadmission.mjs
  tests/validate-module-management-owner-drawer.mjs
  tests/validate-systemwide-image-build-controller.mjs
FILES
  sed -i 's/^[[:space:]]*//' "${TMPDIR:-/tmp}/protected-uat-validation-defects-expected-files"
  LC_ALL=C sort -u "${TMPDIR:-/tmp}/protected-uat-validation-defects-expected-files" \
    -o "${TMPDIR:-/tmp}/protected-uat-validation-defects-expected-files"
  printf '%s\n' "$CHANGED" | LC_ALL=C sort -u > "${TMPDIR:-/tmp}/protected-uat-validation-defects-actual-files"
  cmp -s \
    "${TMPDIR:-/tmp}/protected-uat-validation-defects-expected-files" \
    "${TMPDIR:-/tmp}/protected-uat-validation-defects-actual-files" || {
    echo 'The Protected-UAT validation-defect repair differs from its governed file set.' >&2
    diff -u \
      "${TMPDIR:-/tmp}/protected-uat-validation-defects-expected-files" \
      "${TMPDIR:-/tmp}/protected-uat-validation-defects-actual-files" >&2 || true
    exit 1
  }
  grep -Fq 'Module025SowMaximumOutputTokens = 12_000' src/backend/ProjectTime.Api/Ai/PulseAiPrivateRagService.cs
  grep -Fq 'ParseModule025DetailedPlan' src/backend/ProjectTime.Api/Ai/PulseAiPrivateRagService.cs
  grep -Fq 'insideSalesRepresentatives' src/backend/ProjectTime.Api/Modules/Module025SowGsdModule.cs
  grep -Fq 'CELAR_AI_KEVIN_PROJECT_COUNT_UAT=PASSED' "$CONTROLLER"
  grep -Fq 'MIGRATION_100_MODULE001B_CATALOG=APPLIED_AND_VERIFIED' scripts/release-test/build-and-run-project-planning-document-authority-migration-job.sh
  grep -Fq 'MaximumAiRouteRetries = 2' src/backend/ProjectTime.Api/Modules/ProjectFlowHiveAiPlannerOrchestrationModule.cs
  ! grep -Eq 'environment:[[:space:]]+production' "$CONTROLLER"
  echo 'CELAR_AI_PROTECTED_UAT_VALIDATION_DEFECTS_BOUNDARY=PASSED'
fi

if [[ "$HEAD_BRANCH" == fix/module025-protected-uat-generation-verification-* ]]; then
  PROHIBITED="$(grep -Fvx \
    -e '.github/workflows/projectpulse-deploy-test.yml' \
    -e 'deployment/containers/web/default.conf.template' \
    <<<"$PROHIBITED" || true)"
  test -f .github/workflows/module025-protected-uat-control.yml
  test -f scripts/release-test/run-module025-sow-gsd-protected-test-uat.sh
  if grep -Fxq '.github/workflows/projectpulse-deploy-test.yml' <<<"$CHANGED" \
    || grep -Fxq 'deployment/containers/web/default.conf.template' <<<"$CHANGED"; then
    grep -Fxq '.github/workflows/projectpulse-deploy-test.yml' <<<"$CHANGED"
    grep -Fxq 'deployment/containers/web/default.conf.template' <<<"$CHANGED"
    grep -Fq 'Run protected-Test Module 025 SOW/GSD generation lifecycle UAT' \
      .github/workflows/projectpulse-deploy-test.yml
    ! grep -Fq 'proxy_read_timeout 230s;' deployment/containers/web/default.conf.template
    ! grep -Fq '/generate$' deployment/containers/web/default.conf.template
  else
    if [[ "$HEAD_BRANCH" == 'fix/module025-protected-uat-generation-verification-detailed-plan-parser-20260903' ]]; then
      cat > "${TMPDIR:-/tmp}/module025-detailed-plan-parser-expected-files" <<'FILES'
      .github/workflows/projectpulse-release-test-control-ci-reregistered.yml
      .github/workflows/projectpulse-release-test-control-ci.yml
      scripts/ci/validate-celar-ai-enterprise-source-boundary.sh
      src/backend/ProjectTime.Api/Ai/PulseAiPrivateRagService.cs
      tests/FlowHiveDetailedPlannerTests/Program.cs
      tests/validate-systemwide-image-build-controller.mjs
FILES
      sed -i 's/^[[:space:]]*//' "${TMPDIR:-/tmp}/module025-detailed-plan-parser-expected-files"
      LC_ALL=C sort -u "${TMPDIR:-/tmp}/module025-detailed-plan-parser-expected-files" \
        -o "${TMPDIR:-/tmp}/module025-detailed-plan-parser-expected-files"
      printf '%s\n' "$CHANGED" | LC_ALL=C sort -u > "${TMPDIR:-/tmp}/module025-detailed-plan-parser-actual-files"
      cmp -s \
        "${TMPDIR:-/tmp}/module025-detailed-plan-parser-expected-files" \
        "${TMPDIR:-/tmp}/module025-detailed-plan-parser-actual-files" || {
        echo 'The Module 025 detailed-plan parser repair differs from its exact governed file set.' >&2
        diff -u \
          "${TMPDIR:-/tmp}/module025-detailed-plan-parser-expected-files" \
          "${TMPDIR:-/tmp}/module025-detailed-plan-parser-actual-files" >&2 || true
        exit 1
      }
      grep -Fq 'Module025ModelTaskItems' \
        src/backend/ProjectTime.Api/Ai/PulseAiPrivateRagService.cs
      grep -Fq 'Use the exact top-level property name tasks' \
        src/backend/ProjectTime.Api/Ai/PulseAiPrivateRagService.cs
      grep -Fq 'module025_grouped_work_packages_parse' \
        tests/FlowHiveDetailedPlannerTests/Program.cs
      echo 'CELAR_AI_MODULE025_DETAILED_PLAN_PARSER_BOUNDARY=PASSED'
    elif [[ "$HEAD_BRANCH" == fix/module025-protected-uat-generation-verification-readback-metadata-case-* ]]; then
      cat > "${TMPDIR:-/tmp}/module025-readback-metadata-case-expected-files" <<'FILES'
      scripts/ci/validate-celar-ai-enterprise-source-boundary.sh
      scripts/release-test/run-module025-sow-gsd-protected-test-uat.sh
      tests/validate-systemwide-image-build-controller.mjs
FILES
      sed -i 's/^[[:space:]]*//' "${TMPDIR:-/tmp}/module025-readback-metadata-case-expected-files"
      LC_ALL=C sort -u "${TMPDIR:-/tmp}/module025-readback-metadata-case-expected-files" \
        -o "${TMPDIR:-/tmp}/module025-readback-metadata-case-expected-files"
      printf '%s\n' "$CHANGED" | LC_ALL=C sort -u > "${TMPDIR:-/tmp}/module025-readback-metadata-case-actual-files"
      cmp -s \
        "${TMPDIR:-/tmp}/module025-readback-metadata-case-expected-files" \
        "${TMPDIR:-/tmp}/module025-readback-metadata-case-actual-files" || {
        echo 'The Module 025 readback metadata-case repair differs from its governed file set.' >&2
        diff -u \
          "${TMPDIR:-/tmp}/module025-readback-metadata-case-expected-files" \
          "${TMPDIR:-/tmp}/module025-readback-metadata-case-actual-files" >&2 || true
        exit 1
      }
      grep -Fq '.engagement.aiMetadata.CorrelationId // .engagement.aiMetadata.correlationId' \
        scripts/release-test/run-module025-sow-gsd-protected-test-uat.sh
      echo 'CELAR_AI_MODULE025_READBACK_METADATA_CASE_BOUNDARY=PASSED'
    elif [[ "$HEAD_BRANCH" == fix/module025-protected-uat-generation-verification-tolerant-cited-scope-* ]]; then
      cat > "${TMPDIR:-/tmp}/module025-tolerant-cited-scope-expected-files" <<'FILES'
      scripts/ci/validate-celar-ai-enterprise-source-boundary.sh
      src/backend/ProjectTime.Api/Ai/PulseAiPrivateRagService.cs
      tests/validate-systemwide-image-build-controller.mjs
FILES
      sed -i 's/^[[:space:]]*//' "${TMPDIR:-/tmp}/module025-tolerant-cited-scope-expected-files"
      LC_ALL=C sort -u "${TMPDIR:-/tmp}/module025-tolerant-cited-scope-expected-files" \
        -o "${TMPDIR:-/tmp}/module025-tolerant-cited-scope-expected-files"
      printf '%s\n' "$CHANGED" | LC_ALL=C sort -u > "${TMPDIR:-/tmp}/module025-tolerant-cited-scope-actual-files"
      cmp -s \
        "${TMPDIR:-/tmp}/module025-tolerant-cited-scope-expected-files" \
        "${TMPDIR:-/tmp}/module025-tolerant-cited-scope-actual-files" || {
        echo 'The Module 025 tolerant cited-scope repair differs from its governed file set.' >&2
        diff -u \
          "${TMPDIR:-/tmp}/module025-tolerant-cited-scope-expected-files" \
          "${TMPDIR:-/tmp}/module025-tolerant-cited-scope-actual-files" >&2 || true
        exit 1
      }
      grep -Fq 'PulseAiPrivateModule025Scope' \
        src/backend/ProjectTime.Api/Ai/PulseAiPrivateRagService.cs
      grep -Fq 'ParseModule025CitedScopePlan' \
        src/backend/ProjectTime.Api/Ai/PulseAiPrivateRagService.cs
      grep -Fq 'private_module025_scope_schema_invalid' \
        src/backend/ProjectTime.Api/Ai/PulseAiPrivateRagService.cs
      grep -Fq 'catch (Exception) when (expandModule025CitedPhases)' \
        src/backend/ProjectTime.Api/Ai/PulseAiPrivateRagService.cs
      grep -Fq 'ExpandModule025CitedScopeTasks' \
        src/backend/ProjectTime.Api/Ai/PulseAiPrivateRagService.cs
      echo 'CELAR_AI_MODULE025_TOLERANT_CITED_SCOPE_BOUNDARY=PASSED'
    elif [[ "$HEAD_BRANCH" == fix/module025-protected-uat-generation-verification-cited-phase-expansion-* ]]; then
      cat > "${TMPDIR:-/tmp}/module025-cited-phase-expansion-expected-files" <<'FILES'
      scripts/ci/validate-celar-ai-enterprise-source-boundary.sh
      src/backend/ProjectTime.Api/Ai/PulseAiPrivateRagService.cs
      tests/validate-systemwide-image-build-controller.mjs
FILES
      sed -i 's/^[[:space:]]*//' "${TMPDIR:-/tmp}/module025-cited-phase-expansion-expected-files"
      LC_ALL=C sort -u "${TMPDIR:-/tmp}/module025-cited-phase-expansion-expected-files" \
        -o "${TMPDIR:-/tmp}/module025-cited-phase-expansion-expected-files"
      printf '%s\n' "$CHANGED" | LC_ALL=C sort -u > "${TMPDIR:-/tmp}/module025-cited-phase-expansion-actual-files"
      cmp -s \
        "${TMPDIR:-/tmp}/module025-cited-phase-expansion-expected-files" \
        "${TMPDIR:-/tmp}/module025-cited-phase-expansion-actual-files" || {
        echo 'The Module 025 cited phase-expansion repair differs from its governed file set.' >&2
        diff -u \
          "${TMPDIR:-/tmp}/module025-cited-phase-expansion-expected-files" \
          "${TMPDIR:-/tmp}/module025-cited-phase-expansion-actual-files" >&2 || true
        exit 1
      }
      grep -Fq 'Module025SowMaximumOutputTokens = 1_000' \
        src/backend/ProjectTime.Api/Ai/PulseAiPrivateRagService.cs
      grep -Fq 'ExpandModule025CitedScopeTasks' \
        src/backend/ProjectTime.Api/Ai/PulseAiPrivateRagService.cs
      grep -Fq 'expandModule025CitedPhases: authoritativeSource is not null' \
        src/backend/ProjectTime.Api/Ai/PulseAiPrivateRagService.cs
      grep -Fq 'below 3,500 characters' \
        src/backend/ProjectTime.Api/Ai/PulseAiPrivateRagService.cs
      grep -Fq 'A deterministic, citation-preserving composer expands' \
        src/backend/ProjectTime.Api/Ai/PulseAiPrivateRagService.cs
      echo 'CELAR_AI_MODULE025_CITED_PHASE_EXPANSION_BOUNDARY=PASSED'
    elif [[ "$HEAD_BRANCH" == fix/module025-protected-uat-generation-verification-bounded-output-* ]]; then
      cat > "${TMPDIR:-/tmp}/module025-bounded-output-expected-files" <<'FILES'
      scripts/ci/validate-celar-ai-enterprise-source-boundary.sh
      src/backend/ProjectTime.Api/Ai/PulseAiPrivateRagService.cs
      tests/validate-systemwide-image-build-controller.mjs
FILES
      sed -i 's/^[[:space:]]*//' "${TMPDIR:-/tmp}/module025-bounded-output-expected-files"
      LC_ALL=C sort -u "${TMPDIR:-/tmp}/module025-bounded-output-expected-files" \
        -o "${TMPDIR:-/tmp}/module025-bounded-output-expected-files"
      printf '%s\n' "$CHANGED" | LC_ALL=C sort -u > "${TMPDIR:-/tmp}/module025-bounded-output-actual-files"
      cmp -s \
        "${TMPDIR:-/tmp}/module025-bounded-output-expected-files" \
        "${TMPDIR:-/tmp}/module025-bounded-output-actual-files" || {
        echo 'The Module 025 bounded private-output repair differs from its governed file set.' >&2
        diff -u \
          "${TMPDIR:-/tmp}/module025-bounded-output-expected-files" \
          "${TMPDIR:-/tmp}/module025-bounded-output-actual-files" >&2 || true
        exit 1
      }
      grep -Fq 'Module025SowMaximumOutputTokens = 1_800' \
        src/backend/ProjectTime.Api/Ai/PulseAiPrivateRagService.cs
      grep -Fq 'below 6,000 characters' \
        src/backend/ProjectTime.Api/Ai/PulseAiPrivateRagService.cs
      grep -Fq '? 0.05m' \
        src/backend/ProjectTime.Api/Ai/PulseAiPrivateRagService.cs
      echo 'CELAR_AI_MODULE025_BOUNDED_PRIVATE_OUTPUT_BOUNDARY=PASSED'
    elif [[ "$HEAD_BRANCH" == fix/module025-protected-uat-generation-verification-phase-authority-* ]]; then
      cat > "${TMPDIR:-/tmp}/module025-phase-authority-expected-files" <<'FILES'
      scripts/ci/validate-celar-ai-enterprise-source-boundary.sh
      src/backend/ProjectTime.Api/Modules/Module025SowGsdModule.cs
      tests/validate-systemwide-image-build-controller.mjs
FILES
      sed -i 's/^[[:space:]]*//' "${TMPDIR:-/tmp}/module025-phase-authority-expected-files"
      LC_ALL=C sort -u "${TMPDIR:-/tmp}/module025-phase-authority-expected-files" \
        -o "${TMPDIR:-/tmp}/module025-phase-authority-expected-files"
      printf '%s\n' "$CHANGED" | LC_ALL=C sort -u > "${TMPDIR:-/tmp}/module025-phase-authority-actual-files"
      cmp -s \
        "${TMPDIR:-/tmp}/module025-phase-authority-expected-files" \
        "${TMPDIR:-/tmp}/module025-phase-authority-actual-files" || {
        echo 'The Module 025 exact phase-authority repair differs from its governed file set.' >&2
        diff -u \
          "${TMPDIR:-/tmp}/module025-phase-authority-expected-files" \
          "${TMPDIR:-/tmp}/module025-phase-authority-actual-files" >&2 || true
        exit 1
      }
      grep -Fq 'PhaseCodes.Contains(normalizedPhase' \
        src/backend/ProjectTime.Api/Modules/Module025SowGsdModule.cs
      grep -Fq 'Missing phase coverage:' \
        src/backend/ProjectTime.Api/Modules/Module025SowGsdModule.cs
      echo 'CELAR_AI_MODULE025_EXACT_PHASE_AUTHORITY_BOUNDARY=PASSED'
    elif [[ "$HEAD_BRANCH" == fix/module025-protected-uat-generation-verification-substantive-phase-tasks-* ]]; then
      cat > "${TMPDIR:-/tmp}/module025-substantive-phase-tasks-expected-files" <<'FILES'
      scripts/ci/validate-celar-ai-enterprise-source-boundary.sh
      src/backend/ProjectTime.Api/Ai/PulseAiPrivateRagService.cs
      src/backend/ProjectTime.Api/Modules/Module025SowGsdModule.cs
      tests/validate-systemwide-image-build-controller.mjs
FILES
      sed -i 's/^[[:space:]]*//' "${TMPDIR:-/tmp}/module025-substantive-phase-tasks-expected-files"
      LC_ALL=C sort -u "${TMPDIR:-/tmp}/module025-substantive-phase-tasks-expected-files" \
        -o "${TMPDIR:-/tmp}/module025-substantive-phase-tasks-expected-files"
      printf '%s\n' "$CHANGED" | LC_ALL=C sort -u > "${TMPDIR:-/tmp}/module025-substantive-phase-tasks-actual-files"
      cmp -s \
        "${TMPDIR:-/tmp}/module025-substantive-phase-tasks-expected-files" \
        "${TMPDIR:-/tmp}/module025-substantive-phase-tasks-actual-files" || {
        echo 'The Module 025 substantive phase-task repair differs from its exact governed file set.' >&2
        diff -u \
          "${TMPDIR:-/tmp}/module025-substantive-phase-tasks-expected-files" \
          "${TMPDIR:-/tmp}/module025-substantive-phase-tasks-actual-files" >&2 || true
        exit 1
      }
      grep -Fq 'hasExecutableDetail' \
        src/backend/ProjectTime.Api/Ai/PulseAiPrivateRagService.cs
      grep -Fq 'Never name a task only Plan, Design, Implement, Validate, or Release.' \
        src/backend/ProjectTime.Api/Ai/PulseAiPrivateRagService.cs
      grep -Fq 'private_sow_work_packages_missing' \
        src/backend/ProjectTime.Api/Modules/Module025SowGsdModule.cs
      echo 'CELAR_AI_MODULE025_SUBSTANTIVE_PHASE_TASK_BOUNDARY=PASSED'
    elif grep -Fxq 'src/backend/ProjectTime.Api/Ai/PulseAiPrivateRagService.cs' <<<"$CHANGED"; then
      cat > "${TMPDIR:-/tmp}/module025-compact-private-plan-expected-files" <<'FILES'
      scripts/ci/validate-celar-ai-enterprise-source-boundary.sh
      src/backend/ProjectTime.Api/Ai/PulseAiPrivateModelClient.cs
      src/backend/ProjectTime.Api/Ai/PulseAiPrivateRagService.cs
      src/backend/ProjectTime.Api/Modules/Module025SowGsdModule.cs
      tests/validate-systemwide-image-build-controller.mjs
FILES
      sed -i 's/^[[:space:]]*//' "${TMPDIR:-/tmp}/module025-compact-private-plan-expected-files"
      LC_ALL=C sort -u "${TMPDIR:-/tmp}/module025-compact-private-plan-expected-files" \
        -o "${TMPDIR:-/tmp}/module025-compact-private-plan-expected-files"
      printf '%s\n' "$CHANGED" | LC_ALL=C sort -u > "${TMPDIR:-/tmp}/module025-compact-private-plan-actual-files"
      cmp -s \
        "${TMPDIR:-/tmp}/module025-compact-private-plan-expected-files" \
        "${TMPDIR:-/tmp}/module025-compact-private-plan-actual-files" || {
        echo 'The Module 025 compact private-plan repair differs from its exact governed file set.' >&2
        diff -u \
          "${TMPDIR:-/tmp}/module025-compact-private-plan-expected-files" \
          "${TMPDIR:-/tmp}/module025-compact-private-plan-actual-files" >&2 || true
        exit 1
      }
      grep -Fq 'Module025SowMaximumOutputTokens = 3_000' \
        src/backend/ProjectTime.Api/Ai/PulseAiPrivateRagService.cs
      grep -Fq 'private_model_output_truncated' \
        src/backend/ProjectTime.Api/Ai/PulseAiPrivateModelClient.cs
      grep -Fq 'CompositionDiagnosticCode' \
        src/backend/ProjectTime.Api/Modules/Module025SowGsdModule.cs
      echo 'CELAR_AI_MODULE025_COMPACT_PRIVATE_PLAN_BOUNDARY=PASSED'
    else
      cat > "${TMPDIR:-/tmp}/module025-worker-repair-expected-files" <<'FILES'
      .github/workflows/deep-intelligence-read-contract-ci.yml
      .github/workflows/projectpulse-release-test-control-ci-reregistered.yml
      .github/workflows/projectpulse-release-test-control-ci.yml
      scripts/ci/validate-celar-ai-enterprise-source-boundary.sh
      scripts/release-test/run-module025-sow-gsd-protected-test-uat.sh
      src/backend/ProjectTime.Api/Ai/ProjectPulseAiServiceCollectionExtensions.cs
      src/backend/ProjectTime.Api/Ai/PulseAiPrivateModelClient.cs
      src/backend/ProjectTime.Api/Modules/Module025SowGsdModule.cs
      tests/validate-systemwide-image-build-controller.mjs
FILES
      sed -i 's/^[[:space:]]*//' "${TMPDIR:-/tmp}/module025-worker-repair-expected-files"
      LC_ALL=C sort -u "${TMPDIR:-/tmp}/module025-worker-repair-expected-files" \
        -o "${TMPDIR:-/tmp}/module025-worker-repair-expected-files"
      printf '%s\n' "$CHANGED" | LC_ALL=C sort -u > "${TMPDIR:-/tmp}/module025-worker-repair-actual-files"
      cmp -s \
        "${TMPDIR:-/tmp}/module025-worker-repair-expected-files" \
        "${TMPDIR:-/tmp}/module025-worker-repair-actual-files" || {
        echo 'The Module 025 durable worker repair differs from its exact governed file set.' >&2
        diff -u \
          "${TMPDIR:-/tmp}/module025-worker-repair-expected-files" \
          "${TMPDIR:-/tmp}/module025-worker-repair-actual-files" >&2 || true
        exit 1
      }
      grep -Fq 'PulseAiPrivateSowInference' \
        src/backend/ProjectTime.Api/Ai/ProjectPulseAiServiceCollectionExtensions.cs \
        src/backend/ProjectTime.Api/Ai/PulseAiPrivateModelClient.cs
      grep -Fq 'WorkerLockConnectionString' \
        src/backend/ProjectTime.Api/Modules/Module025SowGsdModule.cs
      echo 'CELAR_AI_MODULE025_DURABLE_WORKER_REPAIR_BOUNDARY=PASSED'
    fi
  fi
  grep -Fq 'module025_detailed_scope_generation_queued' \
    src/backend/ProjectTime.Api/Modules/Module025SowGsdModule.cs
  grep -Fq 'ProcessNextQueuedGenerationAsync' \
    src/backend/ProjectTime.Api/Modules/Module025SowGsdModule.cs
  ! grep -Fq 'phd-west.onenecklab.com' \
    .github/workflows/projectpulse-deploy-test.yml \
    .github/workflows/module025-protected-uat-control.yml \
    scripts/release-test/run-module025-sow-gsd-protected-test-uat.sh
  echo 'CELAR_AI_MODULE025_PROTECTED_UAT_BOUNDARY=PASSED'
fi

if [[ "$HEAD_BRANCH" == fix/shared-project-document-planning-* ]]; then
  FLOWHIVE_RELEASE_MANIFEST='.github/shared-project-document-planning-governed-release-files.txt'
  CONTROLLER='.github/workflows/projectpulse-deploy-test.yml'
  CONTROLLER_VALIDATOR='.github/workflows/projectpulse-release-test-control-ci.yml'
  PROHIBITED="$(grep -Fvx "$CONTROLLER" <<<"$PROHIBITED" || true)"
  for required in "$FLOWHIVE_RELEASE_MANIFEST" "$CONTROLLER" "$CONTROLLER_VALIDATOR"; do
    test -f "$required"
    grep -Fxq "$required" <<<"$CHANGED"
    grep -Fxq "$required" "$FLOWHIVE_RELEASE_MANIFEST"
  done
  grep -Fq 'workflow_dispatch:' "$CONTROLLER"
  grep -Fq 'environment: test' "$CONTROLLER"
  grep -Fq 'Only the authorized FlowHive V2 candidate branch may use pre-merge Protected-Test deployment.' "$CONTROLLER"
  grep -Fq 'PRODUCTION_MUTATION=NONE' "$CONTROLLER"
  grep -Fq 'group: projectpulse-deploy-test' "$CONTROLLER"
  grep -Fq 'queue: max' "$CONTROLLER"
  ! grep -Eq 'environment:[[:space:]]+production' "$CONTROLLER"
  echo 'CELAR_AI_FLOWHIVE_PROTECTED_TEST_BOUNDARY=PASSED'
fi

if [[ "$HEAD_BRANCH" == 'fix/ai-planner-evidence-fallback-20260905' ]]; then
  node tests/validate-planner-fallback-build-release-scope.mjs
  PROHIBITED="$(grep -Fvx 'deployment/containers/api/Dockerfile' <<<"$PROHIBITED" || true)"
fi
if [[ "$HEAD_BRANCH" == 'fix/module064-systemwide-failover-20260905' ]]; then
  node tests/validate-module064-systemwide-failover-scope.mjs
  # This exact repair adds an authenticated chat assertion to the Test gate.
  PROHIBITED="$(grep -Fvx '.github/workflows/projectpulse-deploy-test.yml' <<<"$PROHIBITED" || true)"
fi
if [[ "$HEAD_BRANCH" == 'fix/protected-uat-recovery-and-ai-readiness-20260905' ]]; then
  node tests/validate-protected-uat-recovery-scope.mjs
  node tests/validate-protected-uat-recovery.mjs
  PROHIBITED="$(grep -Fvx '.github/workflows/projectpulse-deploy-test.yml' <<<"$PROHIBITED" || true)"
fi
if [[ -n "$PROHIBITED" ]]; then
  echo 'The Celar AI enterprise interface overlaps a prohibited deployment or provider-secret surface:' >&2
  printf '%s\n' "$PROHIBITED" >&2
  exit 1
fi

for protected in \
  '.github/workflows/projectpulse-deploy-runtime-direct-timer-recovery-test.yml' \
  '.github/workflows/validate-runtime-direct-timer-recovery-deployment.yml' \
  'scripts/validate-runtime-direct-timer-recovery-test-deployment.sh'; do
  if grep -Fxq "$protected" <<<"$CHANGED"; then
    echo "Protected deployment file changed: $protected" >&2
    exit 1
  fi
done

git diff --check "$BASE"...HEAD
echo "CELAR_AI_ENTERPRISE_BASE=$BASE"
echo "CELAR_AI_ENTERPRISE_HEAD=$(git rev-parse HEAD)"
echo 'CELAR_AI_ENTERPRISE_SOURCE_ISOLATION=PASSED'
