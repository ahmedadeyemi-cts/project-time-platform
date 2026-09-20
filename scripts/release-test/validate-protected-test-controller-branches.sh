#!/usr/bin/env bash
#
# Sourced by the protected Test control workflow. Keep branch-specific
# validation explicit; the workflow owns the trusted-main context and
# this file owns only the existing dispatch table.
p='control/flowhive-planner-'
d='20260915'
is_planner_release_control_branch() {
  case "$HEAD_BRANCH" in
    'control/module025-sow-role-candidate-refresh-1009-20260914'|'control/module025-sow-role-candidate-refresh-1014-20260914'|'control/flowhive-pr1044-dispatch-evidence-20260915'|'control/flowhive-pr1044-source-drift-boundary-20260915'|"$p"provider-deadline-candidate-refresh-20260914|"$p"control-path-omissions-20260914|"$p"candidate-approval-refresh-$d|"$p"admission-manifest-refresh-$d|"$p"live-completion-admission-evidence-$d|"$p"compact-phase-native-gate-$d|"$p"parallel-phase-candidate-refresh-$d|"$p"live-celar-budget-approval-20260916|"$p"live-celar-budget-branch-correction-20260916|'fix/flowhive-planner-capacity-safe-'"$d"|'fix/flowhive-planner-live-capacity-repair-'"$d"|"$p"live-capacity-candidate-refresh-$d|"$p"capacity-safe-candidate-refresh-$d|"$p"live-completion-candidate-refresh-$d) return 0 ;;
    'control/flowhive-module025-candidate-refresh-20260916'|'control/flowhive-admission-manifest-refresh-20260916') return 0 ;;
    *) return 1 ;;
  esac
}
if [[ "$HEAD_BRANCH" == feature/flowhive-sequential-wbs-20260920 ]]; then
  python3 tests/flowhive-psa-sequential-scope.py
  node tests/validate-systemwide-image-build-controller.mjs
elif [[ "$HEAD_BRANCH" == feat/sa-workspace-redesign-20260920 ]]; then
  node tests/module025-sa-workspace-scope.mjs
  node tests/validate-systemwide-image-build-controller.mjs
elif [[ "$HEAD_BRANCH" == feature/flowhive-document-readiness-archive-20260920 ]]; then
  python3 tests/flowhive-psa-preparation-scope.py
  node tests/validate-systemwide-image-build-controller.mjs
elif [[ "$HEAD_BRANCH" == feature/flowhive-psa-team-workspace-20260919 ]]; then
  python3 tests/flowhive-psa-workspace-scope.py
  node tests/validate-systemwide-image-build-controller.mjs
elif [[ "$HEAD_BRANCH" == fix/module064-optional-provider-controls ]]; then
  python3 tests/module064-provider-controls-scope.py
  node tests/validate-systemwide-image-build-controller.mjs
elif [[ "$HEAD_BRANCH" == fix/cost-alert-disclosure-state-20260920 ]]; then
  python3 tests/cost-alert-disclosure-scope.py
  node tests/validate-systemwide-image-build-controller.mjs
elif [[ "$HEAD_BRANCH" == fix/reconcile-module-repairs-20260920 ]]; then
  [[ "$PR_NUMBER" == '1118' ]] || fail 'Reconciled repairs are restricted to PR #1118.'
  python3 tests/reconciled-module-repairs-scope.py
  node --test tests/timesheet-draft-writer.test.mjs tests/timesheet-autosave-integration.test.mjs tests/api-error-status.test.mjs tests/project-financial-portfolio.test.mjs
  node tests/validate-systemwide-image-build-controller.mjs
elif [[ "$HEAD_BRANCH" == fix/uat-enterprise-controller-review-20260920 ]]; then
  python3 tests/uat-enterprise-controller-review.py
  node tests/module025-deployment-startup-recovery.test.mjs
  node tests/validate-systemwide-image-build-controller.mjs
elif [[ "$HEAD_BRANCH" == fix/enterprise-completion-20260919 ]]; then
  [[ "$PR_NUMBER" == '1116' ]] || fail 'Enterprise completion registration is restricted to PR #1116.'
  python3 tests/enterprise-completion-scope.py
  node --test tests/timesheet-draft-writer.test.mjs tests/timesheet-autosave-integration.test.mjs tests/api-error-status.test.mjs
  node tests/validate-systemwide-image-build-controller.mjs
elif [[ "$HEAD_BRANCH" == fix/protected-test-foundation-recovery-20260919 ]]; then
  python3 tests/protected-test-foundation-recovery-scope.py
  node tests/module025-deployment-startup-recovery.test.mjs
  node tests/validate-systemwide-image-build-controller.mjs
elif [[ "$HEAD_BRANCH" == fix/enterprise-module-repairs-20260919 ]]; then
  [[ "$PR_NUMBER" == '1111' ]] || fail 'Enterprise repair registration is restricted to PR #1111.'
  python3 tests/enterprise-module-repair-scope.py
  node --test tests/timesheet-draft-writer.test.mjs tests/timesheet-autosave-integration.test.mjs
  node tests/validate-systemwide-image-build-controller.mjs
elif [[ "$HEAD_BRANCH" == fix/module025-export-retention-scope ]]; then
  python3 tests/module025-export-retention-scope.py
  node tests/validate-systemwide-image-build-controller.mjs
elif [[ "$HEAD_BRANCH" == fix/module025-export-orphan-recovery ]]; then
  python3 tests/module025-export-orphan-scope.py
  node tests/module025-deployment-startup-recovery.test.mjs
  node tests/validate-systemwide-image-build-controller.mjs
elif [[ "$HEAD_BRANCH" == fix/module025-export-quarantine-compatibility ]]; then
  python3 tests/module025-export-quarantine-scope.py
  node tests/validate-systemwide-image-build-controller.mjs
elif [[ "$HEAD_BRANCH" == fix/module025-standard-download-formats-20260919 ]]; then
  node tests/module025-standard-download-formats-scope.mjs
  node tests/validate-systemwide-image-build-controller.mjs
elif [[ "$HEAD_BRANCH" == fix/connectwise-sell-migration-replay ]]; then
  python3 tests/connectwise-sell-replay-scope.py
  node tests/validate-systemwide-image-build-controller.mjs
elif [[ "$HEAD_BRANCH" == fix/module025-orphan-timing-quarantine ]]; then
  python3 tests/module025-orphan-timing-scope.py
  node tests/module025-deployment-startup-recovery.test.mjs
  node tests/validate-systemwide-image-build-controller.mjs
elif [[ "$HEAD_BRANCH" == fix/module025-private-generation-recovery ]]; then
  python3 tests/module025-private-generation-scope.py
  node tests/validate-systemwide-image-build-controller.mjs
elif [[ "$HEAD_BRANCH" == fix/connectwise-sell-module026 ]]; then
  python3 tests/connectwise-sell-release-scope.py
  node tests/validate-systemwide-image-build-controller.mjs
elif [[ "$HEAD_BRANCH" == 'fix/flowhive-reviewed-regeneration-control-20260907' ]]; then
  [[ "$CURRENT_BASE_SHA" == '7e5c378dcb15b2b2a00511fa69f90d2411eec336' ]] \
    || fail 'Reviewed regeneration control is not based on current main.'
  cmp -s "$CIT/diff" "$CIT/e-flowhive-reviewed-regeneration-files" || fail 'Reviewed regeneration control differs from its exact governed file set.'
  run_release_control
elif [[ "$HEAD_BRANCH" == 'control/flowhive-planner-candidate-approval-refresh-20260915' ]]; then
  [[ "$CURRENT_BASE_SHA" == 'e15e6dcd872fe6e5eae790d2213dd0b8347e94f6' ]] \
    || fail 'Planner candidate approval refresh is not based on the current trusted main after PR1044.'
  run_release_control
elif [[ "$HEAD_BRANCH" == 'control/flowhive-planner-celar-approval-20260915' ]]; then
  [[ "$CURRENT_BASE_SHA" == 'f7ee256fb851cbdeb083c2ff6fe8ad650ee4d203' ]] \
    || fail 'Planner Celar approval refresh is not based on the merged PR1048 main.'
  run_release_control
elif [[ "$HEAD_BRANCH" == 'control/flowhive-planner-live-celar-budget-approval-20260916' ]]; then
  [[ "$CURRENT_BASE_SHA" == '451432ac7e1dc922e699293d4f2e0ad1dfaa7f88' ]] \
    || fail 'Live Celar budget approval refresh is not based on the merged PR1051 main.'
  run_release_control
elif [[ "$HEAD_BRANCH" == 'control/flowhive-planner-live-celar-budget-branch-correction-20260916' ]]; then
  [[ "$CURRENT_BASE_SHA" == 'd54698e35b9ee8f3db61fd8d6c5bf150c4651267' ]] \
    || fail 'Live Celar budget branch correction is not based on the merged approval refresh.'
  run_release_control
elif [[ "$HEAD_BRANCH" == 'control/flowhive-module025-candidate-refresh-20260916' ]]; then
  [[ "$CURRENT_BASE_SHA" == '6bad29fab087f62337bb1515a53e319941bcf190' ]] \
    || fail 'Module025 candidate refresh is not based on the merged manual-validation registration.'
  run_release_control
elif [[ "$HEAD_BRANCH" == 'control/flowhive-admission-manifest-refresh-20260916' ]]; then
  [[ "$CURRENT_BASE_SHA" == '557e32aaf2c6be34557990becc6d3dea092efc3a' ]] \
    || fail 'Module025 admission manifest refresh is not based on the reviewed candidate approval main.'
  run_release_control
elif [[ "$HEAD_BRANCH" == 'release/flowhive-psa-protected-test-admission-20260906' ]]; then
  BASE_SHA="$CURRENT_BASE_SHA" node tests/flowhive-psa-release-control.mjs
elif [[ "$HEAD_BRANCH" == 'control/flowhive-sow-successor-approval-20260909' ]]; then
  run_release_control
elif [[ "$HEAD_BRANCH" == 'fix/flowhive-stale-run-supersession-20260909' ]]; then
  [[ "$CURRENT_BASE_SHA" == '785eb54a4f280c9ff0e59951c31a30cad4c1a0da' ]] \
    || fail 'Stale-run supersession control is not based on trusted merged main.'
  run_release_control
elif [[ "$HEAD_BRANCH" == 'fix/flowhive-protected-cutover-20260910' ]]; then
  [[ "$CURRENT_BASE_SHA" == '526cfc0d2eb993c35557db190eb5ae87d29bda65' ]] \
    || fail 'Protected cutover control is not based on merged PR896 main.'
  cmp -s "$CIT/diff" "$CIT/e-flowhive-protected-cutover-files" || fail 'Protected cutover control differs from its exact governed file set.'
  run_release_control
elif [[ "$HEAD_BRANCH" == 'control/flowhive-canonical-dispatch-20260911' ]]; then
  expected="$CIT/flowhive-canonical-dispatch-files"
  printf '%s\n' \
    '.github/workflows/projectpulse-deploy-test.yml' \
    'tests/flowhive-psa-release-control.mjs' \
    'tests/flowhive-psa-release-workflow.test.py' \
    '.github/workflows/flowhive-psa-release-control-ci.yml' \
    '.github/workflows/projectpulse-release-test-control-ci.yml' \
    '.github/workflows/projectpulse-release-test-control-ci-reregistered.yml' \
    | LC_ALL=C sort -u > "$expected"
  cmp -s "$CIT/diff" "$expected" || {
    echo 'Canonical dispatch control differs from its exact governed file set.' >&2
    diff -u "$expected" "$CIT/diff" >&2 || true
    exit 1
  }
elif [[ "$HEAD_BRANCH" == 'control/flowhive-successor-approval-20260911' ]]; then
  run_release_control
elif [[ "$HEAD_BRANCH" == 'control/flowhive-protected-cutover-successor-activation-20260912' ]]; then
  run_release_control
elif [[ "$HEAD_BRANCH" == 'feature/celar-1am-central-runtime-version-20260905' ]]; then
  bash scripts/ci/validate-celar-ai-enterprise-source-boundary.sh
  node src/frontend/project-time-web/scripts/validate-module-084-celar-ai-runtime-version.mjs
elif [[ "$HEAD_BRANCH" == fix/sow-runtime-diagnostics-and-uat-isolation-20260906 || "$HEAD_BRANCH" == fix/sow-transport-prompt-20260906 || "$HEAD_BRANCH" == fix/sow-phase-runtime-retry-20260906 || "$HEAD_BRANCH" == fix/sow-generation-uat-20260906 || "$HEAD_BRANCH" == fix/flowhive-terminal-refusal-diagnostics-20260906 || "$HEAD_BRANCH" == fix/celar-runtime-preflight-evidence-20260906 || "$HEAD_BRANCH" == fix/celar-sow-cpu-inference-20260905 || "$HEAD_BRANCH" == fix/celar-sow-runtime-deadlines-20260905 || "$HEAD_BRANCH" == fix/celar-oracle-token-budget-20260905 || "$HEAD_BRANCH" == fix/celar-routed-model-readiness-20260905 || "$HEAD_BRANCH" == fix/celar-hostname-runtime-20260905 || "$HEAD_BRANCH" == fix/protected-uat-recovery-and-ai-readiness-20260905 || "$HEAD_BRANCH" == 'fix/module064-deepseek-chat-answer-20260905' ]]; then
  node tests/validate-celar-ai-pr630-consolidated.mjs
elif [[ "$HEAD_BRANCH" == 'control/flowhive-live-planner-candidate-refresh-20260912' ]]; then
  run_release_control
elif [[ "$HEAD_BRANCH" == 'control/flowhive-planner-live-repair-renewal-safe-20260913' ]]; then
  run_release_control
elif [[ "$HEAD_BRANCH" == 'control/flowhive-planner-latency-candidate-refresh-20260912' ]]; then
  run_release_control
elif [[ "$HEAD_BRANCH" == 'control/flowhive-planner-context-budget-candidate-refresh-20260912' ]]; then
  run_release_control
elif [[ "$HEAD_BRANCH" == 'control/flowhive-planner-context-budget-activation-20260913' ]]; then
  run_release_control
elif [[ "$HEAD_BRANCH" == 'control/flowhive-planner-single-batch-candidate-refresh-20260913' ]]; then
  run_release_control
elif [[ "$HEAD_BRANCH" == 'control/flowhive-planner-compact-batch-candidate-refresh-20260913' ]]; then
  run_release_control
elif [[ "$HEAD_BRANCH" == 'control/flowhive-planner-compact-batch-activation-20260913' ]]; then
  run_release_control
elif [[ "$HEAD_BRANCH" == 'control/flowhive-planner-compact-batch-admission-ancestry-fix-20260913' ]]; then
  run_release_control
elif [[ "$HEAD_BRANCH" == 'control/flowhive-planner-compact-batch-source-drift-boundary-20260913' ]]; then
  run_release_control
elif [[ "$HEAD_BRANCH" == 'control/flowhive-planner-compact-batch-activation-renewal-20260913' ]]; then
  run_release_control
elif [[ "$HEAD_BRANCH" == 'control/flowhive-planner-single-batch-activation-20260913' ]]; then
  run_release_control
elif [[ "$HEAD_BRANCH" == 'control/flowhive-planner-latency-activation-20260912' ]]; then
  run_release_control
elif [[ "$HEAD_BRANCH" == 'control/flowhive-planner-output-budget-candidate-refresh-20260912' ]]; then
  run_release_control
elif [[ "$HEAD_BRANCH" == 'control/flowhive-planner-output-budget-final-activation-20260912' ]]; then
  run_release_control
elif [[ "$HEAD_BRANCH" == 'control/flowhive-live-planner-activation-20260912' ]]; then
  run_release_control
elif [[ "$HEAD_BRANCH" == 'control/flowhive-planner-time-budget-activation-20260912' ]]; then
  run_release_control
elif [[ "$HEAD_BRANCH" == 'control/flowhive-planner-time-budget-checkset-20260912' ]]; then
  run_release_control
elif [[ "$HEAD_BRANCH" == 'control/flowhive-planner-time-budget-approval-20260912' ]]; then
  run_release_control
elif [[ "$HEAD_BRANCH" == 'control/flowhive-planner-provider-budget-approval-20260912' ]]; then
  run_release_control
elif [[ "$HEAD_BRANCH" == 'fix/flowhive-planner-output-budget-20260912' ]]; then
  BASE_SHA="$CURRENT_BASE_SHA" node tests/validate-flowhive-planner-output-budget-scope.mjs
elif [[ "$HEAD_BRANCH" == 'fix/flowhive-planner-celar-transport-20260915' ]]; then
  MANIFEST='.github/flowhive-planner-output-budget-release-files.txt'
  test -s "$MANIFEST" || fail 'FlowHive planner transport manifest is missing.'
  grep -Ev '^[[:space:]]*(#|$)' "$MANIFEST" | LC_ALL=C sort -u > "$CIT/flowhive-planner-transport-files"
  cmp -s "$MANIFEST" "$CIT/flowhive-planner-transport-files" || fail 'FlowHive planner transport manifest must be sorted, unique, and comment-free.'
  cmp -s "$CIT/diff" "$CIT/flowhive-planner-transport-files" || {
    echo 'FlowHive planner transport change differs from its exact governed release set.' >&2
    diff -u "$CIT/flowhive-planner-transport-files" "$CIT/diff" >&2 || true
    exit 1
  }
  BASE_SHA="$CURRENT_BASE_SHA" node tests/validate-flowhive-planner-output-budget-scope.mjs
  node tests/validate-systemwide-image-build-controller.mjs
elif [[ "$HEAD_BRANCH" == 'fix/flowhive-planner-live-celar-acceptance-20260915' ]]; then
  expected="$CIT/flowhive-planner-live-celar-acceptance-files"
  printf '%s\n' \
    '.github/flowhive-enterprise-psa-release-files.txt' \
    '.github/workflows/flowhive-psa-release-control-ci.yml' \
    '.github/workflows/module025-governed-protected-test-release-manual.yml' \
    'scripts/release-test/validate-protected-test-controller-branches.sh' \
    'src/backend/ProjectTime.Api/Ai/PulseAiPrivateRagService.cs' \
    'tests/FlowHiveDetailedPlannerTests/Program.cs' \
    'tests/flowhive-psa-admission.test.mjs' \
    'tests/flowhive-psa-scope.mjs' \
    | LC_ALL=C sort -u > "$expected"
  cmp -s "$CIT/diff" "$expected" || {
    echo 'FlowHive Celar live-acceptance repair contains an unreviewed file.' >&2
    diff -u "$expected" "$CIT/diff" >&2 || true
    exit 1
  }
  node tests/flowhive-psa-scope.mjs --allow-reviewed-superset
elif [[ "$HEAD_BRANCH" == 'fix/flowhive-planner-live-celar-compact-prompt-20260916' ]]; then
  expected="$CIT/flowhive-planner-live-celar-compact-prompt-files"
  printf '%s\n' \
    '.github/workflows/flowhive-psa-release-control-ci.yml' \
    'scripts/release-test/validate-protected-test-controller-branches.sh' \
    'src/backend/ProjectTime.Api/Ai/PulseAiPrivateRagService.cs' \
    'tests/FlowHiveDetailedPlannerTests/Program.cs' \
    'tests/flowhive-psa-admission.test.mjs' \
    | LC_ALL=C sort -u > "$expected"
  cmp -s "$CIT/diff" "$expected" || {
    echo 'FlowHive Celar compact-prompt repair contains an unreviewed file.' >&2
    diff -u "$expected" "$CIT/diff" >&2 || true
    exit 1
  }
  node tests/flowhive-psa-scope.mjs --allow-reviewed-superset
elif [[ "$HEAD_BRANCH" == 'fix/module025-retained-record-lookup-20260919' ]]; then
  node tests/module025-retained-record-lookup-scope.mjs
  node tests/validate-systemwide-image-build-controller.mjs
elif [[ "$HEAD_BRANCH" == 'fix/module025-retained-register-verification-20260919' ]]; then
  node tests/module025-retained-register-scope.mjs
  node tests/validate-systemwide-image-build-controller.mjs
elif [[ "$HEAD_BRANCH" == 'fix/module025-register-entry-diagnostics-20260919' ]]; then
  node tests/module025-register-entry-diagnostics-scope.mjs
  node tests/validate-systemwide-image-build-controller.mjs
elif [[ "$HEAD_BRANCH" == 'fix/module025-register-startup-readiness-20260918' ]]; then
  node tests/module025-register-startup-readiness-scope.mjs
  node tests/validate-systemwide-image-build-controller.mjs
elif [[ "$HEAD_BRANCH" == 'fix/module025-register-report-contract-20260918' ]]; then
  node tests/module025-register-report-contract-scope.mjs
  node tests/validate-systemwide-image-build-controller.mjs
elif [[ "$HEAD_BRANCH" == 'fix/module025-phase-provider-recovery-20260918' ]]; then
  node tests/module025-phase-provider-recovery-scope.mjs
  node tests/validate-systemwide-image-build-controller.mjs
elif [[ "$HEAD_BRANCH" == 'fix/module025-register-browser-acceptance-20260918' ]]; then
  node tests/module025-register-browser-scope.mjs
  node tests/validate-systemwide-image-build-controller.mjs
elif [[ "$HEAD_BRANCH" == 'codex/module019-project-focused-workspace' ]]; then
  [[ "$PR_NUMBER" == '1090' ]] || fail 'Module 019 scope is registered only for PR #1090.'
  node tests/module019-release-scope.mjs
  node tests/validate-systemwide-image-build-controller.mjs
elif [[ "$HEAD_BRANCH" == 'fix/module025-fixture-generation-authorization-20260918' ]]; then
  node tests/module025-worker-authorization-scope.mjs
  node tests/validate-systemwide-image-build-controller.mjs
elif [[ "$HEAD_BRANCH" == 'fix/module025-terminal-provider-evidence-20260918' ]]; then
  node tests/module025-terminal-provider-scope.mjs
  node tests/validate-systemwide-image-build-controller.mjs
elif [[ "$HEAD_BRANCH" == 'fix/module025-orphan-35374125567-20260918' ]]; then
  node tests/module025-deployment-startup-recovery.test.mjs --orphan-scope
  node tests/validate-systemwide-image-build-controller.mjs
elif [[ "$HEAD_BRANCH" == 'fix/module025-deployment-startup-recovery-20260918' ]]; then
  node tests/module025-deployment-startup-recovery.test.mjs --scope
  node tests/validate-systemwide-image-build-controller.mjs
elif [[ "$HEAD_BRANCH" == 'feature/module025-review-timing-delete-reliability-20260918' ]]; then
  node tests/module025-review-timing-delete-scope.mjs
  node tests/module025-review-timing-delete.test.mjs
  node tests/validate-systemwide-image-build-controller.mjs
elif [[ "$HEAD_BRANCH" == 'feature/module025-project-name-20260917' ]]; then
  node tests/module025-project-name-scope.mjs
  node tests/validate-systemwide-image-build-controller.mjs
elif [[ "$HEAD_BRANCH" == 'fix/module025-auto-deploy-exact-zero-job-quarantine-20260917' ]]; then
  node tests/module025-auto-protected-test-scope.mjs
  node tests/validate-systemwide-image-build-controller.mjs
elif [[ "$HEAD_BRANCH" == 'fix/module025-auto-deploy-stale-zero-job-20260917' ]]; then
  node tests/module025-auto-protected-test-scope.mjs
  node tests/validate-systemwide-image-build-controller.mjs
elif [[ "$HEAD_BRANCH" == 'feature/module025-auto-protected-test-20260917' ]]; then
  node tests/module025-auto-protected-test-scope.mjs
  node tests/validate-systemwide-image-build-controller.mjs
elif [[ "$HEAD_BRANCH" == 'fix/module025-scope-progress-20260917' ]]; then
  node tests/module025-scope-progress-scope.mjs
  node tests/validate-systemwide-image-build-controller.mjs
elif [[ "$HEAD_BRANCH" == 'fix/module025-catalog-consistency-20260917' ]]; then
  node tests/module-catalog-scope.mjs
  node tests/validate-systemwide-image-build-controller.mjs
elif [[ "$HEAD_BRANCH" == 'fix/module025-workspace-entry-20260917' ]]; then
  node tests/module025-workspace-entry-scope.mjs
  node tests/validate-systemwide-image-build-controller.mjs
elif [[ "$HEAD_BRANCH" == 'fix/module025-browser-verifier-20260917' ]]; then
  node tests/module025-browser-verifier-scope.mjs
  node tests/validate-systemwide-image-build-controller.mjs
elif [[ "$HEAD_BRANCH" == 'fix/module025-assembly-predecessors-20260917' ]]; then
  node tests/module025-assembly-predecessors-scope.mjs
  node tests/validate-systemwide-image-build-controller.mjs
elif [[ "$HEAD_BRANCH" == 'fix/module025-version-facts-20260917' ]]; then
  node tests/module025-version-facts-scope.mjs
  node tests/validate-systemwide-image-build-controller.mjs
elif [[ "$HEAD_BRANCH" == 'fix/module025-structured-contract-20260917' ]]; then
  node tests/module025-structured-contract-scope.mjs
  node tests/validate-systemwide-image-build-controller.mjs
elif [[ "$HEAD_BRANCH" == 'fix/module025-output-budget-preflight-20260917' ]]; then
  node tests/module025-output-budget-preflight-scope.mjs
  node tests/validate-systemwide-image-build-controller.mjs
elif [[ "$HEAD_BRANCH" == 'fix/module025-complete-acceptance-20260917' ]]; then
  node tests/module025-complete-acceptance-scope.mjs
  node tests/validate-systemwide-image-build-controller.mjs
elif [[ "$HEAD_BRANCH" == 'fix/module025-qualification-policy-20260917' ]]; then
  node tests/module025-qualification-policy-scope.mjs
  node tests/validate-systemwide-image-build-controller.mjs
elif [[ "$HEAD_BRANCH" == 'fix/module025-qualification-contract-20260917' ]]; then
  node tests/module025-provider-qualification-scope.mjs
  node tests/validate-systemwide-image-build-controller.mjs
elif [[ "$HEAD_BRANCH" == 'fix/module025-provider-diagnostics-20260917' ]]; then
  node tests/module025-provider-diagnostics-scope.mjs
  node tests/validate-systemwide-image-build-controller.mjs
elif [[ "$HEAD_BRANCH" == 'fix/module025-external-sow-20260916' ]]; then
  node tests/module025-external-sow-scope.mjs
  node tests/validate-systemwide-image-build-controller.mjs
elif [[ "$HEAD_BRANCH" == 'fix/module025-durable-engine-20260916' ]]; then
  expected="$CIT/module025-engine-files"
  printf '%s\n' \
    '.github/workflows/flowhive-detailed-planner-ci.yml' \
    '.github/workflows/flowhive-psa-release-control-ci.yml' \
    'scripts/release-test/run-module025-installed-sa-uat.py' \
    'scripts/release-test/validate-protected-test-controller-branches.sh' \
    'src/backend/ProjectTime.Api/Ai/CelarAiCapabilityRouting.cs' \
    'src/backend/ProjectTime.Api/Ai/CelarAiEnterprisePlatformContracts.cs' \
    'src/backend/ProjectTime.Api/Ai/CelarAiEnterprisePlatformService.cs' \
    'src/backend/ProjectTime.Api/Ai/Module025GenerationEngine.cs' \
    'src/backend/ProjectTime.Api/Ai/ProjectPulseDeepSeekProvider.cs' \
    'src/backend/ProjectTime.Api/Ai/PulseAiPrivateRagService.cs' \
    'src/backend/ProjectTime.Api/Modules/Module025GenerationJournal.cs' \
    'src/backend/ProjectTime.Api/Modules/Module025SowGsdModule.cs' \
    'src/backend/ProjectTime.Api/Modules/Module025SowSellModule.cs' \
    'src/frontend/project-time-web/src/module025/SowGsdAuthoringWorkspace.jsx' \
    'tests/DeepSeekProviderTests/Program.cs' \
    'tests/FlowHiveDetailedPlannerTests/Module025GenerationEngineTests.cs' \
    'tests/FlowHiveDetailedPlannerTests/Program.cs' \
    'tests/flowhive-psa-admission.test.mjs' \
    'tests/flowhive-psa-installed-acceptance.test.py' \
    'tests/module025-engine-scope.mjs' \
    'tests/test-sow-uat-isolation.py' \
    'tests/validate-celar-ai-pr630-consolidated.mjs' \
    | LC_ALL=C sort -u > "$expected"
  cmp -s "$CIT/diff" "$expected" || fail 'Module 025 engine redesign differs from its exact file set.'
  node tests/module025-engine-scope.mjs
  node tests/validate-systemwide-image-build-controller.mjs
elif [[ "$HEAD_BRANCH" == 'fix/module025-scoped-test-deploy-20260916' ]]; then
  expected="$CIT/module025-scoped-deploy-files"
  printf '%s\n' \
    '.github/workflows/flowhive-psa-release-control-ci.yml' \
    '.github/workflows/projectpulse-deploy-test.yml' \
    'scripts/release-test/validate-protected-test-controller-branches.sh' \
    'tests/flowhive-psa-admission.test.mjs' \
    'tests/flowhive-psa-release-workflow.test.py' \
    'scripts/release-test/resolve-flowhive-installed-deployment.py' \
    'tests/flowhive-installed-resolution.test.py' \
    'tests/module025-scoped-deploy.test.py' \
    | LC_ALL=C sort -u > "$expected"
  cmp -s "$CIT/diff" "$expected" || fail 'Module 025 scoped deployment differs from its exact file set.'
  python3 -m pip install PyYAML==6.0.3
  python3 tests/module025-scoped-deploy.test.py
  node tests/validate-systemwide-image-build-controller.mjs
  node tests/validate-systemwide-enterprise-reliability.mjs
elif [[ "$HEAD_BRANCH" == 'fix/module025-generation-path-20260916' ]]; then
  expected="$CIT/module025-generation-files"
  printf '%s\n' \
    '.github/workflows/flowhive-psa-release-control-ci.yml' \
    'scripts/release-test/validate-protected-test-controller-branches.sh' \
    'src/backend/ProjectTime.Api/Ai/PulseAiPrivateRagService.cs' \
    'tests/FlowHiveDetailedPlannerTests/Program.cs' \
    'tests/flowhive-psa-admission.test.mjs' \
    | LC_ALL=C sort -u > "$expected"
  cmp -s "$CIT/diff" "$expected" || {
    echo 'Module 025 generation correction contains an unreviewed file.' >&2
    diff -u "$expected" "$CIT/diff" >&2 || true
    exit 1
  }
  node tests/flowhive-psa-scope.mjs --allow-reviewed-superset
elif [[ "$HEAD_BRANCH" == 'fix/flowhive-planner-celar-response-contract-20260916' ]]; then
  expected="$CIT/flowhive-planner-celar-response-contract-files"
  printf '%s\n' \
    '.github/workflows/flowhive-psa-release-control-ci.yml' \
    'scripts/release-test/validate-protected-test-controller-branches.sh' \
    'src/backend/ProjectTime.Api/Ai/PulseAiPrivateRagService.cs' \
    'tests/FlowHiveDetailedPlannerTests/Program.cs' \
    'tests/flowhive-psa-admission.test.mjs' \
    | LC_ALL=C sort -u > "$expected"
  cmp -s "$CIT/diff" "$expected" || {
    echo 'FlowHive Celar response-contract repair contains an unreviewed file.' >&2
    diff -u "$expected" "$CIT/diff" >&2 || true
    exit 1
  }
  node tests/flowhive-psa-scope.mjs --allow-reviewed-superset
elif [[ "$HEAD_BRANCH" == 'fix/flowhive-planner-live-celar-budget-main-20260915' ]]; then
  expected="$CIT/flowhive-planner-live-celar-budget-files"
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
    | LC_ALL=C sort -u > "$expected"
  cmp -s "$CIT/diff" "$expected" || {
    echo 'FlowHive Celar planner budget repair contains an unreviewed file.' >&2
    diff -u "$expected" "$CIT/diff" >&2 || true
    exit 1
  }
  node tests/flowhive-psa-scope.mjs --allow-reviewed-superset
  node tests/validate-systemwide-image-build-controller.mjs
elif [[ "$HEAD_BRANCH" == 'feature/flowhive-enterprise-psa-revamp-20260906' || "$HEAD_BRANCH" == 'release/flowhive-sow-successor-20260908' ]]; then
  node tests/flowhive-psa-scope.mjs
elif [[ "$HEAD_BRANCH" == 'fix/flowhive-planner-provider-budget-20260912' ]]; then
  node tests/flowhive-psa-scope.mjs --allow-reviewed-superset
elif [[ "$HEAD_BRANCH" == 'fix/flowhive-planner-compact-batch-20260913' ]]; then
  test -s .github/flowhive-planner-compact-batch-release-files.txt
  grep -Ev '^[[:space:]]*(#|$)' .github/flowhive-planner-compact-batch-release-files.txt | LC_ALL=C sort -u > "$CIT/flowhive-planner-compact-batch-files"
  cmp -s "$CIT/diff" "$CIT/flowhive-planner-compact-batch-files" || {
    echo 'Planner compact-batch change differs from its exact reviewed release manifest.' >&2
    diff -u "$CIT/flowhive-planner-compact-batch-files" "$CIT/diff" >&2 || true
    exit 1
  }
elif [[ "$HEAD_BRANCH" == 'feature/celar-enterprise-retrieval-20260906' || "$HEAD_BRANCH" == 'fix/celar-enterprise-synthesis-20260906' ]]; then
  BASE_SHA="$CURRENT_BASE_SHA" node tests/validate-celar-enterprise-retrieval-scope.mjs
elif [[ "$HEAD_BRANCH" == 'fix/celar-public-answer-fallback-20260906' ]]; then
  BASE_SHA="$CURRENT_BASE_SHA" node tests/validate-celar-customer-public-answer-scope.mjs
elif [[ "$HEAD_BRANCH" == 'fix/module064-public-geography-fallback-20260905' ]]; then
  node tests/validate-celar-ai-pr630-consolidated.mjs
elif [[ "$HEAD_BRANCH" == 'fix/module064-systemwide-failover-20260905' ]]; then
  node tests/validate-module064-systemwide-failover-scope.mjs
elif [[ "$HEAD_BRANCH" == 'fix/ai-routing-sow-regeneration-20260905' ]]; then
  node tests/validate-ai-routing-sow-release-scope.mjs
elif [[ "$HEAD_BRANCH" == 'feature/deepseek-v4-dgx-primary-20260904' ]]; then
  node tests/validate-deepseek-release-scope.mjs
elif [[ "$HEAD_BRANCH" == 'fix/protected-uat-validation-defects-20260903' ]]; then
  cmp -s "$CIT/diff" "$CIT/e-uat-defects" || {
    echo 'Protected-UAT validation-defect repair differs from its exact governed file set.' >&2
    diff -u "$CIT/e-uat-defects" "$CIT/diff" >&2 || true
    exit 1
  }
elif [[ "$HEAD_BRANCH" == 'fix/celar-internal-trust-evidence-20260903' ]]; then
  cmp -s "$CIT/diff" "$CIT/e-celar-internal-trust-evidence-files" || {
    echo 'Celar internal-data trust-evidence repair differs from its exact governed file set.' >&2
    diff -u "$CIT/e-celar-internal-trust-evidence-files" "$CIT/diff" >&2 || true
    exit 1
  }
elif [[ "$HEAD_BRANCH" == fix/shared-project-document-planning-* ]]; then
  test -s "$SHARED_PLANNING_MANIFEST" || fail 'The shared project-document planning release manifest is missing.'
  grep -Ev '^[[:space:]]*(#|$)' "$SHARED_PLANNING_MANIFEST" | LC_ALL=C sort -u > "$CIT/shared-project-document-planning-files"
  cmp -s "$SHARED_PLANNING_MANIFEST" "$CIT/shared-project-document-planning-files" \
    || fail 'The shared project-document planning release manifest must be sorted, unique, and comment-free.'
  while IFS= read -r required_file; do
    test -f "$required_file" || fail "Governed shared-planning artifact is missing: $required_file"
  done < "$CIT/shared-project-document-planning-files"
  cmp -s "$CIT/diff" "$CIT/shared-project-document-planning-files" || {
    echo 'The clean FlowHive/Forge PR differs from its exact governed release manifest.' >&2
    diff -u "$CIT/shared-project-document-planning-files" "$CIT/diff" >&2 || true
    exit 1
  }
  if grep -E '(^|/)(temporary-flowhive|temporary_.*flowhive|flowhive-repair|dispatch-trigger|repair\.patch|patch-[0-9]+\.b64|one-time-flowhive-clean)' "$CIT/diff"; then
    fail 'Temporary recovery or publisher artifacts are forbidden from the clean release.'
  fi
elif [[ "$HEAD_BRANCH" == 'feat/module025-sow-sell-versioned-register-20260908' ]]; then
  node src/frontend/project-time-web/scripts/validate-module025-sow-register.mjs
  echo 'MODULE025_SOW_SELL_REGISTER_NONDEPLOYMENT_SCOPE=PASSED'
elif [[ "$HEAD_BRANCH" == 'fix/module025-my-role-celar-repair-20260914' ]]; then
  printf '%s\n' \
    '.github/workflows/flowhive-psa-release-control-ci.yml' \
    '.github/workflows/projectpulse-release-test-control-ci.yml' \
    'scripts/release-test/run-module025-installed-sa-uat.py' \
    'src/backend/ProjectTime.Api/Ai/PulseAiPrivateRagService.cs' \
    'src/frontend/project-time-web/scripts/inject-pulse-ai-system-chat-group7-compatibility.mjs' \
    'src/frontend/project-time-web/src/EnterpriseExperienceController.jsx' \
    'src/frontend/project-time-web/tests/role-journeys.test.mjs' \
    'tests/FlowHiveDetailedPlannerTests/Program.cs' \
    'tests/flowhive-psa-admission.test.mjs' \
    'tests/flowhive-psa-installed-acceptance.test.py' \
    'tests/flowhive-psa-release-control.mjs' \
    | LC_ALL=C sort -u > "$CIT/module025-my-role-celar-repair-files"
  cmp -s "$CIT/diff" "$CIT/module025-my-role-celar-repair-files" || {
    echo 'Module 025/My Role/Celar repair PR contains an unreviewed file.' >&2
    diff -u "$CIT/module025-my-role-celar-repair-files" "$CIT/diff" >&2 || true
    exit 1
  }
elif is_planner_release_control_branch; then
  run_release_control
elif [[ "$HEAD_BRANCH" == 'control/module025-release-trigger-coverage-20260914' ]]; then
  printf '%s\n' \
    '.github/workflows/flowhive-enterprise-psa-ci.yml' \
    '.github/workflows/flowhive-psa-release-control-ci.yml' \
    '.github/workflows/projectpulse-release-test-control-ci.yml' \
    '.github/workflows/runtime-navigation-work-register-responsive-ci.yml' \
    'tests/flowhive-psa-admission.test.mjs' \
    'tests/flowhive-psa-release-control.mjs' \
    'tests/flowhive-psa-release-workflow.test.py' \
    | LC_ALL=C sort -u > "$CIT/module025-release-trigger-coverage-files"
  cmp -s "$CIT/diff" "$CIT/module025-release-trigger-coverage-files" || {
    echo 'Module 025 release trigger coverage contains an unreviewed file.' >&2
    diff -u "$CIT/module025-release-trigger-coverage-files" "$CIT/diff" >&2 || true
    exit 1
  }
elif [[ "$HEAD_BRANCH" == 'fix/module025-sow-role-live-repair-20260914' ]]; then
  run_release_control
elif [[ "$HEAD_BRANCH" == 'fix/flowhive-planner-provider-deadline-retry-20260914' ]]; then
  printf '%s\n' \
    '.github/workflows/flowhive-psa-release-control-ci.yml' \
    '.github/workflows/projectpulse-release-test-control-ci.yml' \
    'src/backend/ProjectTime.Api/Modules/ProjectPlanningAiOrchestrator.cs' \
    'tests/flowhive-psa-admission.test.mjs' \
    'tests/FlowHiveDetailedPlannerTests/Program.cs' \
    'tests/flowhive-psa-release-control.mjs' \
    | LC_ALL=C sort -u > "$CIT/flowhive-planner-provider-deadline-retry-files"
  cmp -s "$CIT/diff" "$CIT/flowhive-planner-provider-deadline-retry-files" || {
    echo 'FlowHive provider deadline retry contains an unreviewed file.' >&2
    diff -u "$CIT/flowhive-planner-provider-deadline-retry-files" "$CIT/diff" >&2 || true
    exit 1
  }
  run_release_control
elif [[ "$HEAD_BRANCH" == 'fix/flowhive-planner-compact-phase-20260915' ]]; then
  [[ $(sha256sum "$CIT/diff") == b4587a3c24dab20a92234efefdd21ef0906173eaab553c6ce993008aec374b43* ]]
  run_release_control
elif [[ "$HEAD_BRANCH" == 'control/module025-sow-role-candidate-refresh-final-20260914' ]]; then
  printf '%s\n' \
    '.github/flowhive-psa-protected-cutover.json' \
    '.github/flowhive-psa-protected-test-candidate.json' \
    '.github/workflows/flowhive-psa-release-control-ci.yml' \
    '.github/workflows/projectpulse-release-test-control-ci.yml' \
    'scripts/release-test/dispatch-flowhive-psa-test.mjs' \
    'scripts/release-test/flowhive-psa-admission.mjs' \
    'tests/flowhive-psa-admission.test.mjs' \
    'tests/flowhive-psa-release-control.mjs' \
    | LC_ALL=C sort -u > "$CIT/module025-sow-role-candidate-refresh-final-files"
  cmp -s "$CIT/diff" "$CIT/module025-sow-role-candidate-refresh-final-files" || {
    echo 'Final SOW/My Role candidate refresh contains an unreviewed file.' >&2
    diff -u "$CIT/module025-sow-role-candidate-refresh-final-files" "$CIT/diff" >&2 || true
    exit 1
  }
elif [[ "$HEAD_BRANCH" == 'control/module025-sow-role-admission-scope-20260914' ]]; then
  printf '%s\n' \
    '.github/flowhive-psa-protected-test-candidate.json' \
    '.github/workflows/flowhive-psa-release-control-ci.yml' \
    '.github/workflows/projectpulse-release-test-control-ci.yml' \
    'scripts/release-test/flowhive-psa-admission.mjs' \
    'tests/flowhive-psa-admission.test.mjs' \
    'tests/flowhive-psa-release-control.mjs' \
    | LC_ALL=C sort -u > "$CIT/module025-sow-role-admission-scope-files"
  cmp -s "$CIT/diff" "$CIT/module025-sow-role-admission-scope-files" || {
    echo 'SOW/My Role admission scope correction contains an unreviewed file.' >&2
    diff -u "$CIT/module025-sow-role-admission-scope-files" "$CIT/diff" >&2 || true
    exit 1
  }
  run_release_control
elif [[ "$HEAD_BRANCH" == 'control/module025-sow-role-native-activation-20260914' ]]; then
  run_release_control
elif [[ "$HEAD_BRANCH" == 'control/module025-sow-role-native-active-20260914' ]]; then
  run_release_control
elif [[ "$HEAD_BRANCH" == 'control/module025-sow-role-candidate-refresh-20260914' ]]; then
  printf '%s\n' \
    '.github/flowhive-psa-protected-cutover.json' \
    '.github/flowhive-psa-protected-test-candidate.json' \
    '.github/workflows/flowhive-psa-release-control-ci.yml' \
    '.github/workflows/projectpulse-release-test-control-ci-reregistered.yml' \
    '.github/workflows/projectpulse-release-test-control-ci.yml' \
    'scripts/release-test/dispatch-flowhive-psa-test.mjs' \
    'scripts/release-test/flowhive-psa-admission.mjs' \
    'tests/flowhive-psa-admission.test.mjs' \
    'tests/flowhive-psa-release-control.mjs' \
    | LC_ALL=C sort -u > "$CIT/module025-sow-role-candidate-refresh-files"
  cmp -s "$CIT/diff" "$CIT/module025-sow-role-candidate-refresh-files" || {
    echo 'SOW/My Role candidate refresh contains an unreviewed file.' >&2
    diff -u "$CIT/module025-sow-role-candidate-refresh-files" "$CIT/diff" >&2 || true
    exit 1
  }
elif [[ "$HEAD_BRANCH" == 'fix/module025-sow-role-live-acceptance-20260914' ]]; then
  run_release_control
elif [[ "$HEAD_BRANCH" == 'control/module025-admission-manifest-order-20260914' ]]; then
  BASE_SHA="$BASE_SHA" GITHUB_HEAD_REF="$HEAD_BRANCH" node tests/flowhive-psa-release-control.mjs
elif [[ "$PR_NUMBER" == '734' && -f .github/flowhive-pr734-governed-release-files.txt ]]; then
  grep -Ev '^[[:space:]]*(#|$)' .github/flowhive-pr734-governed-release-files.txt | LC_ALL=C sort -u > "$CIT/flowhive-pr734-files"
  cmp -s "$CIT/diff" "$CIT/flowhive-pr734-files" || {
    echo 'PR #734 governed release scope differs from its exact reviewed manifest.' >&2
    diff -u "$CIT/flowhive-pr734-files" "$CIT/diff" >&2 || true
    exit 1
  }
  echo 'FLOWHIVE_PR734_GOVERNED_RELEASE_SCOPE=PASSED'
elif [[ "$PR_NUMBER" == '777' ]]; then
  cmp -s "$CIT/diff" "$CIT/e-777-workspace" || {
    echo 'PR #777 Module 025 workspace repair differs from its exact governed file set.' >&2
    diff -u "$CIT/e-777-workspace" "$CIT/diff" >&2 || true
    exit 1
  }
elif cmp -s "$CIT/diff" "$CIT/e-runtime-repair-files"; then
:
elif cmp -s "$CIT/diff" "$CIT/e-assigned-work-repair-files"; then
:
elif cmp -s "$CIT/diff" "$CIT/e-pr719-module-directory-owner-001a-files"; then
:
elif cmp -s "$CIT/diff" "$CIT/e-modules-directory-authority-starvation-files"; then
:
elif cmp -s "$CIT/diff" "$CIT/e-module001b-live-uat-repair-files"; then
:
elif [[ "$HEAD_BRANCH" == 'fix/utilization-manager-team-summary-authorization-20260903' ]]; then
  cmp -s "$CIT/diff" "$CIT/e-utilization-manager-authorization-repair-files" || {
    echo 'Manager utilization authorization repair differs from its exact governed file set.' >&2
    diff -u "$CIT/e-utilization-manager-authorization-repair-files" "$CIT/diff" >&2 || true
    exit 1
  }
elif cmp -s "$CIT/diff" "$CIT/e-protected-uat-merged-main-relaunch-governance-files"; then
:
else
  comm -23 "$CIT/diff" "$CIT/allowed-release-files" > "$CIT/unexpected-release-files"
  [[ ! -s "$CIT/unexpected-release-files" ]] || {
    echo 'Unexpected files in governed Test controller PR:' >&2
    cat "$CIT/unexpected-release-files" >&2
    exit 1
  }
fi
