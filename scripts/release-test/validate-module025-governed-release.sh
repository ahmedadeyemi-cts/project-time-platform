#!/usr/bin/env bash
set -Eeuo pipefail
fail() { echo "ERROR: $*" >&2; exit 1; }

[[ "$BASE_SHA" =~ ^[0-9a-f]{40}$ ]] || fail 'Pull-request base SHA is unavailable.'
git cat-file -e "$BASE_SHA^{commit}" || fail 'Exact pull-request base commit is unavailable.'

HEAD_BRANCH="${GITHUB_HEAD_REF:-${GITHUB_REF_NAME:-}}"
if [[ "$GITHUB_EVENT_NAME" == 'workflow_dispatch' ]]; then
  [[ "$(git rev-parse HEAD)" == "${RELEASE_SHA}" ]] || fail 'Manual Module 025 validation did not check out the requested candidate SHA.'
fi
if [[ "$HEAD_BRANCH" == 'fix/module025-retained-register-verification-20260919' ]]; then
  node tests/module025-retained-register-scope.mjs
  node tests/validate-systemwide-image-build-controller.mjs
  exit 0
fi
if [[ "$HEAD_BRANCH" == 'fix/module025-register-entry-diagnostics-20260919' ]]; then
  node tests/module025-register-entry-diagnostics-scope.mjs
  node tests/validate-systemwide-image-build-controller.mjs
  exit 0
fi
if [[ "$HEAD_BRANCH" == 'fix/module025-register-startup-readiness-20260918' ]]; then
  node tests/module025-register-startup-readiness-scope.mjs
  node tests/validate-systemwide-image-build-controller.mjs
  exit 0
fi
if [[ "$HEAD_BRANCH" == 'fix/module025-register-report-contract-20260918' ]]; then
  node tests/module025-register-report-contract-scope.mjs
  node tests/validate-systemwide-image-build-controller.mjs
  exit 0
fi
if [[ "$HEAD_BRANCH" == 'fix/module025-phase-provider-recovery-20260918' ]]; then
  node tests/module025-phase-provider-recovery-scope.mjs
  node tests/validate-systemwide-image-build-controller.mjs
  exit 0
fi
if [[ "$HEAD_BRANCH" == 'fix/module025-register-browser-acceptance-20260918' ]]; then
  node tests/module025-register-browser-scope.mjs
  node tests/validate-systemwide-image-build-controller.mjs
  exit 0
fi
if [[ "$HEAD_BRANCH" == 'fix/module025-fixture-generation-authorization-20260918' ]]; then
  node tests/module025-worker-authorization-scope.mjs
  node tests/validate-systemwide-image-build-controller.mjs
  exit 0
fi
if [[ "$HEAD_BRANCH" == 'fix/module025-terminal-provider-evidence-20260918' ]]; then
  node tests/module025-terminal-provider-scope.mjs
  node tests/validate-systemwide-image-build-controller.mjs
  exit 0
fi
if [[ "$HEAD_BRANCH" == 'feature/module025-auto-protected-test-20260917' ]]; then
  node tests/module025-auto-protected-test-scope.mjs
  node tests/validate-systemwide-image-build-controller.mjs
  grep -Fq "acceptance_scope:\"sow_role\"" .github/workflows/module025-protected-uat-control.yml
  grep -Fq "steps.sow_role_uat.outcome == 'success'" .github/workflows/projectpulse-deploy-test.yml
  grep -Fq '109_module025_project_name' .github/workflows/projectpulse-deploy-test.yml
  echo 'MODULE025_AUTO_PROTECTED_TEST_RELEASE_SCOPE=PASSED'
  exit 0
fi
if [[ "$HEAD_BRANCH" == 'fix/module025-output-budget-preflight-20260917' ]]; then
  node tests/module025-output-budget-preflight-scope.mjs
  node tests/validate-systemwide-image-build-controller.mjs
  python3 tests/module025-provider-qualification.test.py
  exit 0
fi
if [[ "$HEAD_BRANCH" == 'control/flowhive-module025-manual-check-trigger-20260916' || "$HEAD_BRANCH" == 'control/flowhive-module025-manual-trigger-registration-20260916' ]]; then
  printf '%s\n' \
    '.github/workflows/flowhive-psa-release-control-ci.yml' \
    '.github/workflows/module025-governed-protected-test-release-manual.yml' \
    | LC_ALL=C sort -u > "$RUNNER_TEMP/module025-manual-check-trigger-files"
  git diff --name-only "$BASE_SHA...HEAD" | LC_ALL=C sort -u > "$RUNNER_TEMP/module025-manual-check-trigger-actual"
  cmp -s "$RUNNER_TEMP/module025-manual-check-trigger-files" "$RUNNER_TEMP/module025-manual-check-trigger-actual" || {
    echo 'Module 025 manual-check trigger contains an unreviewed file.' >&2
    diff -u "$RUNNER_TEMP/module025-manual-check-trigger-files" "$RUNNER_TEMP/module025-manual-check-trigger-actual" >&2 || true
    exit 1
  }
  echo 'MODULE025_MANUAL_CHECK_TRIGGER_SCOPE=PASSED'
  exit 0
elif [[ "$HEAD_BRANCH" == 'control/flowhive-module025-manual-trigger-exact-candidate-20260916' ]]; then
  printf '%s\n' \
    '.github/flowhive-enterprise-psa-release-files.txt' \
    '.github/flowhive-planner-output-budget-release-files.txt' \
    '.github/flowhive-planner-parallel-phases-release-files.txt' \
    '.github/flowhive-psa-protected-test-candidate.json' \
    '.github/flowhive-psa-release-control-files.txt' \
    '.github/module025-sow-sell-governed-release-files.txt' \
    '.github/workflows/flowhive-psa-release-control-ci.yml' \
    '.github/workflows/module025-governed-protected-test-release-manual.yml' \
    'scripts/release-test/flowhive-psa-admission.mjs' \
    'scripts/release-test/validate-protected-test-controller-branches.sh' \
    'tests/flowhive-psa-admission.test.mjs' \
    'tests/flowhive-psa-release-control.mjs' \
    'tests/flowhive-psa-scope.mjs' \
    | LC_ALL=C sort -u > "$RUNNER_TEMP/module025-manual-trigger-exact-candidate-files"
  git diff --name-only "$BASE_SHA...HEAD" | LC_ALL=C sort -u > "$RUNNER_TEMP/module025-manual-trigger-exact-candidate-actual"
  cmp -s "$RUNNER_TEMP/module025-manual-trigger-exact-candidate-files" "$RUNNER_TEMP/module025-manual-trigger-exact-candidate-actual" || {
    echo 'Module 025 exact-candidate registration contains an unreviewed file.' >&2
    diff -u "$RUNNER_TEMP/module025-manual-trigger-exact-candidate-files" "$RUNNER_TEMP/module025-manual-trigger-exact-candidate-actual" >&2 || true
    exit 1
  }
  echo 'MODULE025_MANUAL_TRIGGER_EXACT_CANDIDATE_SCOPE=PASSED'
  exit 0
elif [[ "$HEAD_BRANCH" == 'control/flowhive-live-planner-candidate-refresh-20260912' ]]; then
  BASE_SHA="$BASE_SHA" GITHUB_HEAD_REF="$HEAD_BRANCH" node tests/flowhive-psa-release-control.mjs
  echo 'FLOWHIVE_LIVE_PLANNER_CANDIDATE_REFRESH_SCOPE=PASSED'
  exit 0
elif [[ "$HEAD_BRANCH" == 'control/flowhive-planner-latency-candidate-refresh-20260912' ]]; then
  BASE_SHA="$BASE_SHA" GITHUB_HEAD_REF="$HEAD_BRANCH" node tests/flowhive-psa-release-control.mjs
  echo 'FLOWHIVE_PLANNER_LATENCY_CANDIDATE_REFRESH_SCOPE=PASSED'
  exit 0
elif [[ "$HEAD_BRANCH" == 'control/flowhive-planner-context-budget-candidate-refresh-20260912' ]]; then
  BASE_SHA="$BASE_SHA" GITHUB_HEAD_REF="$HEAD_BRANCH" node tests/flowhive-psa-release-control.mjs
  echo 'FLOWHIVE_PLANNER_CONTEXT_BUDGET_CANDIDATE_REFRESH_SCOPE=PASSED'
  exit 0
elif [[ "$HEAD_BRANCH" == 'control/flowhive-planner-single-batch-candidate-refresh-20260913' ]]; then
  BASE_SHA="$BASE_SHA" GITHUB_HEAD_REF="$HEAD_BRANCH" node tests/flowhive-psa-release-control.mjs
  echo 'FLOWHIVE_PLANNER_SINGLE_BATCH_CANDIDATE_REFRESH_SCOPE=PASSED'
  exit 0
elif [[ "$HEAD_BRANCH" == 'control/flowhive-planner-compact-batch-candidate-refresh-20260913' ]]; then
  BASE_SHA="$BASE_SHA" GITHUB_HEAD_REF="$HEAD_BRANCH" node tests/flowhive-psa-release-control.mjs
  echo 'FLOWHIVE_PLANNER_COMPACT_BATCH_CANDIDATE_REFRESH_SCOPE=PASSED'
  exit 0
elif [[ "$HEAD_BRANCH" == 'control/flowhive-planner-output-budget-candidate-refresh-20260912' ]]; then
  BASE_SHA="$BASE_SHA" GITHUB_HEAD_REF="$HEAD_BRANCH" node tests/flowhive-psa-release-control.mjs
  echo 'FLOWHIVE_PLANNER_OUTPUT_BUDGET_CANDIDATE_REFRESH_SCOPE=PASSED'
  exit 0
elif [[ "$HEAD_BRANCH" == 'fix/installed-sow-role-identity-lane-20260913' ]]; then
  printf '%s\n' \
    '.github/flowhive-psa-release-control-files.txt' \
    '.github/workflows/flowhive-psa-release-control-ci.yml' \
    '.github/workflows/module025-governed-protected-test-release-manual.yml' \
    'scripts/release-test/resolve-flowhive-installed-deployment.py' \
    'tests/flowhive-installed-resolution.test.py' \
    'tests/flowhive-psa-admission.test.mjs' \
    'tests/flowhive-psa-installed-acceptance.test.py' \
    'tests/flowhive-psa-release-control.mjs' \
    | LC_ALL=C sort -u > "$RUNNER_TEMP/installed-sow-role-identity-lane-files"
  git diff --name-only "$BASE_SHA...HEAD" | LC_ALL=C sort -u > "$RUNNER_TEMP/installed-sow-role-identity-lane-actual"
  cmp -s "$RUNNER_TEMP/installed-sow-role-identity-lane-files" "$RUNNER_TEMP/installed-sow-role-identity-lane-actual" || {
    echo 'Installed identity-lane verifier PR contains an unreviewed file.' >&2
    diff -u "$RUNNER_TEMP/installed-sow-role-identity-lane-files" "$RUNNER_TEMP/installed-sow-role-identity-lane-actual" >&2 || true
    exit 1
  }
  python3 tests/flowhive-installed-resolution.test.py
  echo 'INSTALLED_SOW_ROLE_IDENTITY_LANE_SCOPE=PASSED'
  exit 0
elif [[ "$HEAD_BRANCH" == 'fix/flowhive-planner-provider-budget-20260912' ]]; then
  node src/frontend/project-time-web/scripts/validate-module025-sow-register.mjs
  node tests/validate-systemwide-enterprise-reliability.mjs
  node tests/validate-systemwide-image-build-controller.mjs
  echo 'FLOWHIVE_PLANNER_PROVIDER_BUDGET_SCOPE=PASSED'
  exit 0
elif [[ "$HEAD_BRANCH" == 'fix/sow-role-installed-acceptance-20260913' ]]; then
  BASE_SHA="$BASE_SHA" GITHUB_HEAD_REF="$HEAD_BRANCH" node tests/flowhive-psa-release-control.mjs
  echo 'INSTALLED_SOW_ROLE_ACCEPTANCE_SCOPE=PASSED'
  exit 0
elif [[ "$HEAD_BRANCH" == 'control/module025-admission-manifest-order-20260914' ]]; then
  BASE_SHA="$BASE_SHA" GITHUB_HEAD_REF="$HEAD_BRANCH" node tests/flowhive-psa-release-control.mjs
  exit 0
elif [[ "$HEAD_BRANCH" == 'control/flowhive-planner-admission-manifest-refresh-20260915' ]]; then
  BASE_SHA="$BASE_SHA" GITHUB_HEAD_REF="$HEAD_BRANCH" node tests/flowhive-psa-release-control.mjs
  echo 'FLOWHIVE_PLANNER_ADMISSION_MANIFEST_REFRESH_SCOPE=PASSED'
  exit 0
elif [[ "$HEAD_BRANCH" == 'control/flowhive-planner-celar-approval-20260915' ]]; then
  BASE_SHA="$BASE_SHA" GITHUB_HEAD_REF="$HEAD_BRANCH" node tests/flowhive-psa-release-control.mjs
  echo 'FLOWHIVE_PLANNER_CELAR_APPROVAL_REFRESH_SCOPE=PASSED'
  exit 0
elif [[ "$HEAD_BRANCH" == 'control/flowhive-planner-live-celar-budget-approval-20260916' ]]; then
  BASE_SHA="$BASE_SHA" GITHUB_HEAD_REF="$HEAD_BRANCH" node tests/flowhive-psa-release-control.mjs
  echo 'FLOWHIVE_PLANNER_LIVE_CELAR_BUDGET_APPROVAL_SCOPE=PASSED'
  exit 0
elif [[ "$HEAD_BRANCH" == 'control/flowhive-planner-live-celar-budget-branch-correction-20260916' ]]; then
  BASE_SHA="$BASE_SHA" GITHUB_HEAD_REF="$HEAD_BRANCH" node tests/flowhive-psa-release-control.mjs
  echo 'FLOWHIVE_PLANNER_LIVE_CELAR_BUDGET_BRANCH_CORRECTION_SCOPE=PASSED'
  exit 0
elif [[ "$HEAD_BRANCH" == 'control/flowhive-module025-candidate-refresh-20260916' ]]; then
  BASE_SHA="$BASE_SHA" GITHUB_HEAD_REF="$HEAD_BRANCH" node tests/flowhive-psa-release-control.mjs
  echo 'MODULE025_CANDIDATE_REFRESH_SCOPE=PASSED'
  exit 0
elif [[ "$HEAD_BRANCH" == 'control/flowhive-admission-manifest-refresh-20260916' ]]; then
  BASE_SHA="$BASE_SHA" GITHUB_HEAD_REF="$HEAD_BRANCH" node tests/flowhive-psa-release-control.mjs
  echo 'MODULE025_ADMISSION_MANIFEST_REFRESH_SCOPE=PASSED'
  exit 0
elif [[ "$HEAD_BRANCH" == 'control/flowhive-pr1044-source-drift-boundary-20260915' ]]; then
  BASE_SHA="$BASE_SHA" GITHUB_HEAD_REF="$HEAD_BRANCH" node tests/flowhive-psa-release-control.mjs
  echo 'FLOWHIVE_PLANNER_SOURCE_DRIFT_BOUNDARY_SCOPE=PASSED'
  exit 0
elif [[ "$HEAD_BRANCH" == 'fix/flowhive-planner-parallel-phases-20260915' ]]; then
  MANIFEST='.github/flowhive-planner-parallel-phases-release-files.txt'
  test -s "$MANIFEST" || fail 'FlowHive planner parallel-phase manifest is missing.'
  grep -Ev '^[[:space:]]*(#|$)' "$MANIFEST" | LC_ALL=C sort -u > "$RUNNER_TEMP/flowhive-planner-parallel-files"
  cmp -s "$MANIFEST" "$RUNNER_TEMP/flowhive-planner-parallel-files" || fail 'FlowHive planner parallel-phase manifest must be sorted, unique, and comment-free.'
  git diff --name-only "$BASE_SHA...HEAD" | LC_ALL=C sort -u > "$RUNNER_TEMP/flowhive-planner-parallel-actual"
  cmp -s "$MANIFEST" "$RUNNER_TEMP/flowhive-planner-parallel-actual" || {
    echo 'PR differs from the exact governed FlowHive planner parallel-phase release set.' >&2
    diff -u "$MANIFEST" "$RUNNER_TEMP/flowhive-planner-parallel-actual" >&2 || true
    exit 1
  }
  git diff --check "$BASE_SHA...HEAD"
  node tests/validate-systemwide-enterprise-reliability.mjs
  node tests/validate-systemwide-image-build-controller.mjs
  echo 'FLOWHIVE_PLANNER_PARALLEL_PHASES_SCOPE=PASSED'
  exit 0
elif [[ "$HEAD_BRANCH" == 'fix/flowhive-planner-celar-transport-20260915' ]]; then
  MANIFEST='.github/flowhive-planner-output-budget-release-files.txt'
  test -s "$MANIFEST" || fail 'FlowHive planner transport manifest is missing.'
  grep -Ev '^[[:space:]]*(#|$)' "$MANIFEST" | LC_ALL=C sort -u > "$RUNNER_TEMP/flowhive-planner-transport-files"
  cmp -s "$MANIFEST" "$RUNNER_TEMP/flowhive-planner-transport-files" || fail 'FlowHive planner transport manifest must be sorted, unique, and comment-free.'
  git diff --name-only "$BASE_SHA...HEAD" | LC_ALL=C sort -u > "$RUNNER_TEMP/flowhive-planner-transport-actual"
  cmp -s "$MANIFEST" "$RUNNER_TEMP/flowhive-planner-transport-actual" || {
    echo 'PR differs from the exact governed FlowHive planner transport release set.' >&2
    diff -u "$MANIFEST" "$RUNNER_TEMP/flowhive-planner-transport-actual" >&2 || true
    exit 1
  }
  git diff --check "$BASE_SHA...HEAD"
  BASE_SHA="$BASE_SHA" node tests/validate-flowhive-planner-output-budget-scope.mjs
  node tests/validate-systemwide-enterprise-reliability.mjs
  node tests/validate-systemwide-image-build-controller.mjs
  echo 'FLOWHIVE_PLANNER_CELAR_TRANSPORT_SCOPE=PASSED'
  exit 0
elif [[ "$HEAD_BRANCH" == 'fix/flowhive-planner-live-celar-acceptance-20260915' ]]; then
  printf '%s\n' \
    '.github/flowhive-enterprise-psa-release-files.txt' \
    '.github/workflows/flowhive-psa-release-control-ci.yml' \
    '.github/workflows/module025-governed-protected-test-release-manual.yml' \
    'scripts/release-test/validate-protected-test-controller-branches.sh' \
    'src/backend/ProjectTime.Api/Ai/PulseAiPrivateRagService.cs' \
    'tests/FlowHiveDetailedPlannerTests/Program.cs' \
    'tests/flowhive-psa-admission.test.mjs' \
    'tests/flowhive-psa-scope.mjs' \
    | LC_ALL=C sort -u > "$RUNNER_TEMP/flowhive-planner-live-celar-acceptance-files"
  git diff --name-only "$BASE_SHA...HEAD" | LC_ALL=C sort -u > "$RUNNER_TEMP/flowhive-planner-live-celar-acceptance-actual"
  cmp -s "$RUNNER_TEMP/flowhive-planner-live-celar-acceptance-files" "$RUNNER_TEMP/flowhive-planner-live-celar-acceptance-actual" || {
    echo 'FlowHive Celar live-acceptance repair contains an unreviewed file.' >&2
    diff -u "$RUNNER_TEMP/flowhive-planner-live-celar-acceptance-files" "$RUNNER_TEMP/flowhive-planner-live-celar-acceptance-actual" >&2 || true
    exit 1
  }
  node tests/flowhive-psa-scope.mjs --allow-reviewed-superset
  exit 0
elif [[ "$HEAD_BRANCH" == 'fix/flowhive-planner-live-celar-budget-main-20260915' ]]; then
  printf '%s\n' \
    '.github/flowhive-enterprise-psa-release-files.txt' \
    '.github/workflows/flowhive-psa-release-control-ci.yml' \
    '.github/workflows/module025-governed-protected-test-release-manual.yml' \
    'scripts/release-test/validate-protected-test-controller-branches.sh' \
    'src/backend/ProjectTime.Api/Ai/ProjectPulseAiServiceCollectionExtensions.cs' \
    'src/backend/ProjectTime.Api/Ai/PulseAiPrivateRagService.cs' \
    'tests/CelarAiOracleExternalRuntimeTests/Program.cs' \
    'tests/FlowHiveDetailedPlannerTests/Program.cs' \
    'tests/flowhive-psa-admission.test.mjs' \
    'tests/flowhive-psa-scope.mjs' \
    'tests/validate-systemwide-image-build-controller.mjs' \
    | LC_ALL=C sort -u > "$RUNNER_TEMP/flowhive-planner-live-celar-budget-files"
  git diff --name-only "$BASE_SHA...HEAD" | LC_ALL=C sort -u > "$RUNNER_TEMP/flowhive-planner-live-celar-budget-actual"
  cmp -s "$RUNNER_TEMP/flowhive-planner-live-celar-budget-files" "$RUNNER_TEMP/flowhive-planner-live-celar-budget-actual" || {
    echo 'FlowHive Celar planner budget repair contains an unreviewed file.' >&2
    diff -u "$RUNNER_TEMP/flowhive-planner-live-celar-budget-files" "$RUNNER_TEMP/flowhive-planner-live-celar-budget-actual" >&2 || true
    exit 1
  }
  node tests/flowhive-psa-scope.mjs --allow-reviewed-superset
  node tests/validate-systemwide-image-build-controller.mjs
  echo 'FLOWHIVE_PLANNER_LIVE_CELAR_BUDGET_SCOPE=PASSED'
  exit 0
elif [[ "$HEAD_BRANCH" == 'feat/module025-sow-sell-versioned-register-20260908' || "$HEAD_BRANCH" == 'release/flowhive-sow-successor-20260908' || "$HEAD_BRANCH" == 'fix/flowhive-installed-pm-readiness-candidate-20260911' || "$HEAD_BRANCH" == 'fix/flowhive-planner-output-budget-20260912' || "$HEAD_BRANCH" == 'fix/flowhive-planner-time-budget-20260912' ]]; then
  if [[ "$HEAD_BRANCH" == 'release/flowhive-sow-successor-20260908' || "$HEAD_BRANCH" == 'fix/flowhive-installed-pm-readiness-candidate-20260911' ]]; then
    MANIFEST='.github/flowhive-enterprise-psa-release-files.txt'
  elif [[ "$HEAD_BRANCH" == 'fix/flowhive-planner-output-budget-20260912' || "$HEAD_BRANCH" == 'fix/flowhive-planner-time-budget-20260912' ]]; then
    MANIFEST='.github/flowhive-planner-output-budget-release-files.txt'
  else
    MANIFEST='.github/module025-sow-sell-governed-release-files.txt'
  fi
  test -s "$MANIFEST" || fail 'Module 025 SOW governed release manifest is missing.'
  grep -Ev '^[[:space:]]*(#|$)' "$MANIFEST" | LC_ALL=C sort -u > "$RUNNER_TEMP/module025-sow-files"
  cmp -s "$MANIFEST" "$RUNNER_TEMP/module025-sow-files" || fail 'Module 025 SOW manifest must be sorted, unique, and comment-free.'
  git diff --name-only "$BASE_SHA...HEAD" | LC_ALL=C sort -u > "$RUNNER_TEMP/actual-module025-sow-files"
  cmp -s "$MANIFEST" "$RUNNER_TEMP/actual-module025-sow-files" || {
    echo 'PR differs from the exact governed Module 025 SOW release set.' >&2
    diff -u "$MANIFEST" "$RUNNER_TEMP/actual-module025-sow-files" >&2 || true
    exit 1
  }
  if [[ "$HEAD_BRANCH" == 'fix/flowhive-planner-output-budget-20260912' ]]; then
    node tests/validate-flowhive-planner-output-budget-scope.mjs
  else
    node src/frontend/project-time-web/scripts/validate-module025-sow-register.mjs
  fi
  bash tests/test-module025-sow-sell-register-migration-106.sh
  node tests/validate-systemwide-enterprise-reliability.mjs
  node tests/validate-systemwide-image-build-controller.mjs
  echo 'MODULE025_SOW_SELL_EXACT_GOVERNED_SCOPE=PASSED'
  exit 0
fi

if [[ "$HEAD_BRANCH" == 'control/flowhive-module025-manual-dispatch-20260916' ]]; then
  printf '%s\n' \
    '.github/workflows/module025-governed-protected-test-release-manual.yml' \
    > "$RUNNER_TEMP/expected-module025-files"
else
  printf '%s\n' \
    'database/migrations/089_module_catalog_role_administration_reconciliation.sql' \
    'src/backend/ProjectTime.Api/Modules/ModuleAvailabilityModule.cs' \
    'src/frontend/project-time-web/scripts/validate-group-6-enterprise-presentation.mjs' \
    'src/frontend/project-time-web/src/enterprise/SalesDeliveryWorkflowCenter.jsx' \
    'src/frontend/project-time-web/src/module-availability-registry.js' \
    'tests/validate-systemwide-enterprise-reliability.mjs' \
    > "$RUNNER_TEMP/expected-module025-files"
fi
sed -i 's/^[[:space:]]*//' "$RUNNER_TEMP/expected-module025-files"
LC_ALL=C sort -u "$RUNNER_TEMP/expected-module025-files" -o "$RUNNER_TEMP/expected-module025-files"

git diff --name-only "$BASE_SHA...HEAD" | LC_ALL=C sort -u > "$RUNNER_TEMP/actual-module025-files"
cmp -s "$RUNNER_TEMP/expected-module025-files" "$RUNNER_TEMP/actual-module025-files" || {
  echo 'PR differs from the exact governed Module 025 release set.' >&2
  diff -u "$RUNNER_TEMP/expected-module025-files" "$RUNNER_TEMP/actual-module025-files" >&2 || true
  exit 1
}

CONTROLLER='.github/workflows/projectpulse-deploy-test.yml'
test -f "$CONTROLLER" || fail 'Protected-Test deployment controller is missing.'
grep -Fq 'environment: test' "$CONTROLLER"
grep -Fq 'group: projectpulse-deploy-test' "$CONTROLLER"
grep -Fq 'cancel-in-progress: false' "$CONTROLLER"
grep -Fq 'Build immutable API, web, and migration images' "$CONTROLLER"
grep -Fq 'Deploy immutable Test API image' "$CONTROLLER"
grep -Fq 'Deploy immutable Test web image' "$CONTROLLER"
grep -Fq 'Run protected-Test authenticated functional UAT' "$CONTROLLER"
grep -Fq 'Run protected-Test assigned-work visibility UAT' "$CONTROLLER"
grep -Fq 'Run protected-Test utilization role-scoping UAT' "$CONTROLLER"
grep -Fq 'Production mutation: none' "$CONTROLLER"
grep -Fq 'Oracle/private AI runtime mutation: none' "$CONTROLLER"

MIGRATION_BUILDER='scripts/release-test/build-and-run-project-planning-document-authority-migration-job.sh'
test -f "$MIGRATION_BUILDER" || fail 'Governed migration builder is missing.'
grep -Fq '098_module_management_owner_storage_reconciliation.sql' "$MIGRATION_BUILDER"
grep -Fq '098_customer_directory_source_authority.sql' "$MIGRATION_BUILDER"
grep -Fq '099_module025_sow_gsd_workspace.sql' "$MIGRATION_BUILDER"
grep -Fq 'MIGRATION_098_OWNER_STORAGE=APPLIED_AND_VERIFIED' "$MIGRATION_BUILDER"
grep -Fq 'MIGRATION_098_CUSTOMER_SOURCE=APPLIED_AND_VERIFIED' "$MIGRATION_BUILDER"
grep -Fq 'MIGRATION_099_MODULE025_SOW_GSD=APPLIED_AND_VERIFIED' "$MIGRATION_BUILDER"

node src/frontend/project-time-web/scripts/validate-group-6-enterprise-presentation.mjs
node tests/validate-systemwide-enterprise-reliability.mjs
node scripts/validate-deployment-concurrency-governance.mjs --repo-root "$GITHUB_WORKSPACE" --verify-repository
python3 scripts/security/validate-repository-security-posture.py
git diff --check "$BASE_SHA...HEAD"

echo 'MODULE025_EXACT_GOVERNED_SCOPE=PASSED'
echo 'MODULE025_LIVE_WORKSPACE_ROUTE=PASSED'
echo 'MIGRATIONS_098_099_PROTECTED_TEST=REQUIRED'
echo 'AUTHENTICATED_ASSIGNED_WORK_UTILIZATION_UAT=REQUIRED'
echo 'PRODUCTION_MUTATION=NONE'
