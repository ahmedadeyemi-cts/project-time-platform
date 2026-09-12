import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { verifyApproval, controlManifest } from '../scripts/release-test/flowhive-psa-admission.mjs';

export const files = [
  '.github/flowhive-psa-protected-cutover.json',
  '.github/flowhive-psa-protected-test-candidate.json',
  '.github/flowhive-psa-stale-run-supersession-authorization.json',
  '.github/flowhive-psa-release-control-files.txt',
  '.github/workflows/flowhive-psa-protected-test-admission.yml',
  '.github/workflows/flowhive-psa-release-control-ci.yml',
  '.github/workflows/projectpulse-deploy-test.yml',
  '.github/workflows/projectpulse-release-test-control-ci-reregistered.yml',
  '.github/workflows/projectpulse-release-test-control-ci.yml',
  'docs/releases/FLOWHIVE-PSA-PROTECTED-TEST-ADMISSION.md',
  'scripts/ci/validate-celar-ai-enterprise-source-boundary.sh',
  'scripts/release-test/apply-flowhive-psa-migrations.sh',
  'scripts/release-test/build-and-run-flowhive-psa-migrations.sh',
  'scripts/release-test/dispatch-flowhive-psa-test.mjs',
  'scripts/release-test/flowhive-psa-admission.mjs',
  'scripts/release-test/prepare-protected-test-scope-manifests.sh',
  'scripts/release-test/check-module025-installed-prerequisite.py',
  'scripts/release-test/run-flowhive-my-role-browser.py',
  'scripts/release-test/run-flowhive-psa-live-uat.py',
  'scripts/release-test/verify-flowhive-installed-identity.py',
  '.github/workflows/flowhive-psa-installed-acceptance.yml',
  'tests/flowhive-psa-admission.test.mjs',
  'tests/flowhive-psa-installed-acceptance.test.py',
  'tests/flowhive-psa-live-uat.test.py',
  'tests/flowhive-psa-migration-fixture.py',
  'tests/flowhive-psa-release-control.mjs',
  'tests/flowhive-psa-release-workflow.test.py',
  'tests/validate-celar-ai-pr630-consolidated.mjs'
].sort();
export const repairFiles = [
  '.github/flowhive-psa-protected-test-candidate.json',
  'docs/releases/FLOWHIVE-PSA-PROTECTED-TEST-ADMISSION.md',
  'scripts/release-test/build-and-run-flowhive-psa-migrations.sh',
  'tests/flowhive-psa-admission.test.mjs',
  'tests/flowhive-psa-migration-fixture.py',
  'tests/flowhive-psa-release-control.mjs',
  'tests/flowhive-psa-release-workflow.test.py'
].sort();
export const repairBase = '55ebb51fda1917f202ce6561ed5f5e635468d01c';
export const reviewedRegenerationBase = '7e5c378dcb15b2b2a00511fa69f90d2411eec336';
export const reviewedRegenerationBranch = 'fix/flowhive-reviewed-regeneration-control-20260907';
export const candidateRefreshBase = '4871d47fbeaad0fd5c08ddca27f193d682a0ea92';
export const candidateRefreshBranch = 'fix/flowhive-protected-test-candidate-refresh-20260908';
export const candidateRefreshFiles = [
  '.github/flowhive-psa-protected-test-candidate.json',
  'docs/releases/FLOWHIVE-PSA-PROTECTED-TEST-ADMISSION.md',
  'tests/flowhive-psa-release-control.mjs'
].sort();
export const successorCandidateRefreshBase = 'd2262ccbef31800883589197d03122bd51bb87cc';
export const successorCandidateRefreshBranch = 'control/flowhive-sow-successor-candidate-refresh-20260910';
export const successorCandidateRefreshFiles = [
  ...candidateRefreshFiles,
  'tests/flowhive-psa-admission.test.mjs'
].sort();
export const sourceBaseCorrectionBase = '040709cdac0a940ad8feffbd730f1be35ce50280';
export const sourceBaseCorrectionBranch = 'fix/flowhive-admission-source-base-20260908';
export const sourceBaseCorrectionFiles = [
  '.github/flowhive-psa-protected-test-candidate.json',
  'docs/releases/FLOWHIVE-PSA-PROTECTED-TEST-ADMISSION.md',
  'tests/flowhive-psa-admission.test.mjs',
  'tests/flowhive-psa-release-control.mjs'
].sort();
export const successorApprovalBase = 'bf401fa1d017eae0ebf10c9ed79720829ce8de60';
export const successorApprovalBranch = 'control/flowhive-sow-successor-approval-20260909';
export const successorApprovalFiles = [
  '.github/flowhive-psa-protected-test-candidate.json',
  '.github/flowhive-psa-release-control-files.txt',
  '.github/workflows/flowhive-psa-protected-test-admission.yml',
  '.github/workflows/projectpulse-release-test-control-ci-reregistered.yml',
  '.github/workflows/projectpulse-release-test-control-ci.yml',
  '.github/workflows/projectpulse-deploy-test.yml',
  'docs/releases/FLOWHIVE-PSA-PROTECTED-TEST-ADMISSION.md',
  'scripts/release-test/apply-flowhive-psa-migrations.sh',
  'scripts/release-test/dispatch-flowhive-psa-test.mjs',
  'scripts/release-test/flowhive-psa-admission.mjs',
  'scripts/release-test/prepare-protected-test-scope-manifests.sh',
  'tests/flowhive-psa-admission.test.mjs',
  'tests/flowhive-psa-migration-fixture.py',
  'tests/flowhive-psa-release-control.mjs',
  'tests/flowhive-psa-release-workflow.test.py'
].sort();
export const successorReleaseApprovalBase = '2057df629ebb1f3ef651295c0da541061b77d56a';
export const successorReleaseApprovalBranch = 'control/flowhive-successor-approval-20260911';
export const successorReleaseApprovalFiles = [
  '.github/flowhive-psa-protected-cutover.json',
  '.github/flowhive-psa-protected-test-candidate.json',
  '.github/workflows/projectpulse-release-test-control-ci-reregistered.yml',
  '.github/workflows/projectpulse-release-test-control-ci.yml',
  '.github/workflows/projectpulse-deploy-test.yml',
  'scripts/release-test/dispatch-flowhive-psa-test.mjs',
  'scripts/release-test/flowhive-psa-admission.mjs',
  'tests/flowhive-psa-admission.test.mjs',
  'tests/flowhive-psa-migration-fixture.py',
  'tests/flowhive-psa-release-control.mjs',
  'tests/flowhive-psa-release-workflow.test.py'
].sort();
export const successorCutoverActivationBase = '6ed862212156e148f2fa3bba1bcc3c9d62d5da6c';
export const successorCutoverActivationBranch = 'control/flowhive-protected-cutover-successor-activation-20260912';
export const successorCutoverActivationFiles = [
  '.github/flowhive-psa-protected-cutover.json',
  '.github/workflows/projectpulse-release-test-control-ci-reregistered.yml',
  '.github/workflows/projectpulse-release-test-control-ci.yml',
  'scripts/release-test/dispatch-flowhive-psa-test.mjs',
  'tests/flowhive-psa-admission.test.mjs',
  'tests/flowhive-psa-release-control.mjs'
].sort();
export const dispatchRecoveryBase = 'af5fcb463384096f668345ac7cc9bd00efef0a33';
export const dispatchRecoveryBranch = 'fix/flowhive-dispatch-run-recovery-20260909';
export const dispatchRecoveryFiles = [
  '.github/workflows/flowhive-psa-release-control-ci.yml',
  'scripts/release-test/dispatch-flowhive-psa-test.mjs',
  'tests/flowhive-psa-admission.test.mjs',
  'tests/flowhive-psa-release-control.mjs'
].sort();
export const dispatchLaneRepairBase = 'e2289c617b69b1a563ee8bd6f01aaa5c54965c6a';
export const dispatchLaneRepairBranch = 'fix/flowhive-psa-dispatch-lane-20260912';
export const dispatchLaneRepairFiles = [
  'scripts/release-test/dispatch-flowhive-psa-test.mjs',
  'tests/flowhive-psa-admission.test.mjs',
  'tests/flowhive-psa-release-control.mjs'
].sort();
export const dispatchSkippedRecoveryBase = 'a2dc9e7050851cc763b1bd7ed915d390e8585ceb';
export const dispatchSkippedRecoveryBranch = 'fix/flowhive-psa-skipped-dispatch-recovery-20260912';
export const dispatchSkippedRecoveryFiles = [
  '.github/flowhive-psa-protected-cutover.json',
  'scripts/release-test/dispatch-flowhive-psa-test.mjs',
  'tests/flowhive-psa-admission.test.mjs',
  'tests/flowhive-psa-release-control.mjs'
].sort();
export const dispatchReceiptParserBase = '61b410cc6d08387acae00bc5bfa186beac18adea';
export const dispatchReceiptParserBranch = 'fix/flowhive-psa-receipt-parser-20260912';
export const dispatchReceiptParserFiles = [
  'scripts/release-test/dispatch-flowhive-psa-test.mjs',
  'tests/flowhive-psa-admission.test.mjs',
  'tests/flowhive-psa-release-control.mjs'
].sort();
export const dispatchLaneAdmissionBase = '316d7a8b53c603a91b5e94683435bf53b8e0c5e7';
export const dispatchLaneAdmissionBranch = 'fix/flowhive-psa-dispatch-lane-admission-20260912';
export const dispatchLaneAdmissionFiles = [
  'scripts/release-test/dispatch-flowhive-psa-test.mjs',
  'scripts/release-test/flowhive-psa-admission.mjs',
  'tests/flowhive-psa-admission.test.mjs',
  'tests/flowhive-psa-release-control.mjs'
].sort();
export const protectedCutoverLaneRefreshBase = '30d27ae3099262be84e4cc5b8456699f6cba5c3e';
export const protectedCutoverLaneRefreshBranch = 'control/flowhive-protected-cutover-lane-refresh-20260912';
export const protectedCutoverLaneRefreshFiles = [
  '.github/flowhive-psa-protected-cutover.json',
  'scripts/release-test/dispatch-flowhive-psa-test.mjs',
  'tests/flowhive-psa-admission.test.mjs',
  'tests/flowhive-psa-release-control.mjs'
].sort();
export const protectedCutoverPmIdentityRefreshBase = '557bb9cab4c43bfd4fb9d4dbb54c2e116b2e1753';
export const protectedCutoverPmIdentityRefreshBranch = 'control/flowhive-protected-cutover-pm-identity-refresh-20260912';
export const protectedCutoverPmIdentityRefreshFiles = [
  '.github/flowhive-psa-protected-cutover.json',
  'scripts/release-test/dispatch-flowhive-psa-test.mjs',
  'tests/flowhive-psa-admission.test.mjs',
  'tests/flowhive-psa-release-control.mjs'
].sort();
export const staleSupersessionBase = '785eb54a4f280c9ff0e59951c31a30cad4c1a0da';
export const staleSupersessionBranch = 'fix/flowhive-stale-run-supersession-20260909';
export const staleSupersessionActivationBase = '2d31f842599927f0a62e692d2669f643dc9e287b';
export const staleSupersessionActivationBranch = 'control/flowhive-stale-run-activation-20260910';
export const staleSupersessionRenewalBase = 'cb15168c791bdd70744aae9549d71475b9a25eb4';
export const staleSupersessionRenewalBranch = 'control/flowhive-stale-run-renewal-20260910';
export const releaseStabilizationBase = '9f30078c2c407d4d3576ccefd663a145be50c6c4';
export const releaseStabilizationBranch = 'fix/flowhive-release-stabilization-20260910';
export const releaseStabilizationFiles = [
  '.github/flowhive-psa-stale-run-supersession-authorization.json',
  '.github/workflows/flowhive-psa-protected-test-admission.yml',
  '.github/workflows/projectpulse-deploy-test.yml',
  'docs/releases/FLOWHIVE-PSA-PROTECTED-TEST-ADMISSION.md',
  'scripts/release-test/dispatch-flowhive-psa-test.mjs',
  'scripts/release-test/prepare-protected-test-scope-manifests.sh',
  'tests/flowhive-psa-admission.test.mjs',
  'tests/flowhive-psa-release-control.mjs',
  'tests/flowhive-psa-release-workflow.test.py'
].sort();
export const protectedCutoverBase = '526cfc0d2eb993c35557db190eb5ae87d29bda65';
export const protectedCutoverBranch = 'fix/flowhive-protected-cutover-20260910';
export const protectedCutoverFiles = [
  '.github/flowhive-psa-protected-cutover.json',
  '.github/flowhive-psa-release-control-files.txt',
  '.github/workflows/flowhive-psa-protected-test-admission.yml',
  '.github/workflows/projectpulse-release-test-control-ci-reregistered.yml',
  '.github/workflows/projectpulse-release-test-control-ci.yml',
  'docs/releases/FLOWHIVE-PSA-PROTECTED-TEST-ADMISSION.md',
  'scripts/release-test/dispatch-flowhive-psa-test.mjs',
  'scripts/release-test/prepare-protected-test-scope-manifests.sh',
  'tests/flowhive-psa-admission.test.mjs',
  'tests/flowhive-psa-release-control.mjs',
  'tests/flowhive-psa-release-workflow.test.py'
].sort();
export const protectedCutoverActivationBase = '1a2119f76e6f0f09634b5f1586f5a524ab1b0d1e';
export const protectedCutoverActivationBranch = 'control/flowhive-protected-cutover-activation-20260910';
export const protectedCutoverActivationFiles = [
  '.github/flowhive-psa-protected-cutover.json',
  'docs/releases/FLOWHIVE-PSA-PROTECTED-TEST-ADMISSION.md',
  'tests/flowhive-psa-admission.test.mjs',
  'tests/flowhive-psa-release-control.mjs'
].sort();
export const protectedCutoverRefreshBase = '5ebd3b6bfd34339efc1f74688aea01d58f647dd2';
export const protectedCutoverRefreshBranch = 'control/flowhive-protected-cutover-refresh-20260910';
export const protectedCutoverRefreshFiles = [
  '.github/flowhive-psa-protected-cutover.json',
  'tests/flowhive-psa-release-control.mjs'
].sort();
export const admissionPermissionFixBase = '09d95657c71e872703809f5f0e3473e038cfdad2';
export const admissionPermissionFixBranch = 'fix/flowhive-psa-admission-comment-permission-20260910';
export const admissionPermissionFixFiles = [
  '.github/flowhive-psa-protected-cutover.json',
  '.github/workflows/flowhive-psa-protected-test-admission.yml',
  'tests/flowhive-psa-release-workflow.test.py',
  'tests/flowhive-psa-release-control.mjs'
].sort();
export const admissionBotClaimFixBase = 'c78d346042314a7395a2ceeaa46f2737e6c0b076';
export const admissionBotClaimFixBranch = 'fix/flowhive-psa-admission-bot-claim-20260910';
export const admissionBotClaimFixFiles = [
  'scripts/release-test/dispatch-flowhive-psa-test.mjs',
  'tests/flowhive-psa-admission.test.mjs',
  'tests/flowhive-psa-release-control.mjs'
].sort();
export const protectedCutoverRecoveryBase = '6a806ca888fe8adbbe5cfcbf1139868fc6a4c5f0';
export const protectedCutoverRecoveryBranch = 'control/flowhive-protected-cutover-recovery-20260910';
export const protectedCutoverRecoveryFiles = [
  '.github/flowhive-psa-protected-cutover.json',
  'scripts/release-test/dispatch-flowhive-psa-test.mjs',
  'tests/flowhive-psa-admission.test.mjs',
  'tests/flowhive-psa-release-control.mjs'
].sort();
export const reservationRecoveryEndpointFixBase = '559087e3c9ba69df07be62aecd6379f9aaf33c8e';
export const reservationRecoveryEndpointFixBranch = 'fix/flowhive-psa-reservation-recovery-endpoint-20260910';
export const reservationRecoveryEndpointFixFiles = [
  'scripts/release-test/dispatch-flowhive-psa-test.mjs',
  'tests/flowhive-psa-admission.test.mjs',
  'tests/flowhive-psa-release-control.mjs'
].sort();
export const protectedCutoverFinalBase = '73e4d887179961ade9b726af5163f8d61bea9682';
export const protectedCutoverFinalBranch = 'control/flowhive-protected-cutover-final-20260910';
export const protectedCutoverFinalFiles = [
  '.github/flowhive-psa-protected-cutover.json',
  'tests/flowhive-psa-release-control.mjs'
].sort();
export const liveUatStatusFixBase = 'df6f7fc6d52495f83b0fd169047f5a249493db7e';
export const liveUatStatusFixBranch = 'fix/flowhive-live-uat-status-20260910';
export const liveUatStatusFixFiles = [
  '.github/flowhive-psa-release-control-files.txt',
  '.github/workflows/flowhive-psa-installed-acceptance.yml',
  '.github/workflows/flowhive-psa-release-control-ci.yml',
  'scripts/release-test/check-module025-installed-prerequisite.py',
  'scripts/release-test/run-flowhive-my-role-browser.py',
  'scripts/release-test/run-flowhive-psa-live-uat.py',
  'scripts/release-test/verify-flowhive-installed-identity.py',
  'tests/flowhive-psa-live-uat.test.py',
  'tests/flowhive-psa-installed-acceptance.test.py',
  'tests/flowhive-psa-release-control.mjs'
].sort();
export const installedVerificationEnvironmentBase = '25868daaca1115dca4440f7acb3927ebdd6da3ae';
export const installedVerificationEnvironmentBranch = 'fix/flowhive-installed-verification-environment-20260910';
export const installedVerificationEnvironmentFiles = [
  '.github/workflows/flowhive-psa-installed-acceptance.yml',
  'tests/flowhive-psa-installed-acceptance.test.py',
  'tests/flowhive-psa-release-control.mjs'
].sort();
export const installedVerificationEvidenceBase = 'ff93beae7494265e03bcf1f4f80c6424f75b3eab';
export const installedVerificationEvidenceBranch = 'fix/flowhive-installed-verification-evidence-20260910';
export const installedVerificationEvidenceFiles = [
  '.github/workflows/flowhive-psa-installed-acceptance.yml',
  'tests/flowhive-psa-installed-acceptance.test.py',
  'tests/flowhive-psa-release-control.mjs'
].sort();
export const installedAcceptanceEvidenceBase = '0cc12647b8453803fd8b230786c1e18494634aae';
export const installedAcceptanceEvidenceBranch = 'fix/flowhive-installed-acceptance-evidence-20260910';
export const installedAcceptanceEvidenceFiles = [
  '.github/workflows/flowhive-psa-installed-acceptance.yml',
  'tests/flowhive-psa-installed-acceptance.test.py',
  'tests/flowhive-psa-release-control.mjs'
].sort();
export const installedAcceptanceArtifactsBase = 'db8054942f12d649e5503166c0f39a6ad7eb18c2';
export const installedAcceptanceArtifactsBranch = 'fix/flowhive-installed-acceptance-artifacts-20260910';
export const installedAcceptanceArtifactsFiles = [
  '.github/workflows/flowhive-psa-installed-acceptance.yml',
  'tests/flowhive-psa-installed-acceptance.test.py',
  'tests/flowhive-psa-release-control.mjs'
].sort();
export const installedAcceptanceReadinessBase = '9c2ef1869ddee31bd175cba0cc50dc8ed82eb117';
export const installedAcceptanceReadinessBranch = 'fix/flowhive-installed-acceptance-readiness-20260910';
export const installedAcceptanceReadinessFiles = [
  '.github/workflows/flowhive-psa-installed-acceptance.yml',
  'scripts/release-test/run-flowhive-my-role-browser.py',
  'scripts/release-test/verify-flowhive-installed-identity.py',
  'tests/flowhive-psa-installed-acceptance.test.py',
  'tests/flowhive-psa-release-control.mjs'
].sort();
export const installedAcceptanceVisibilityBase = '42aa8534c10be3c99ccf7020919e96d3ffeddf4d';
export const installedAcceptanceVisibilityBranch = 'fix/flowhive-installed-acceptance-visibility-20260910';
export const installedAcceptanceVisibilityFiles = [
  '.github/workflows/flowhive-psa-installed-acceptance.yml',
  'scripts/release-test/run-flowhive-my-role-browser.py',
  'scripts/release-test/verify-flowhive-installed-identity.py',
  'tests/flowhive-psa-installed-acceptance.test.py',
  'tests/flowhive-psa-release-control.mjs'
].sort();
export const installedAcceptancePlannerBase = '7e705d38607dabe39e1a88fc6fc8e13c47c0f9c3';
export const installedAcceptancePlannerBranch = 'fix/flowhive-installed-acceptance-planner-20260910';
export const installedAcceptancePlannerFiles = [
  '.github/workflows/flowhive-psa-installed-acceptance.yml',
  'scripts/release-test/reconcile-flowhive-planner.py',
  'tests/flowhive-psa-installed-acceptance.test.py',
  'tests/flowhive-psa-release-control.mjs'
].sort();
export const installedAcceptanceFinalBase = 'c7448a069920a6e359ec6be21b6e68a4801a767b';
export const installedAcceptanceFinalBranch = 'fix/flowhive-installed-acceptance-final-20260911';
export const installedAcceptanceFinalFiles = [
  '.github/workflows/flowhive-psa-installed-acceptance.yml',
  'scripts/release-test/run-flowhive-my-role-browser.py',
  'scripts/release-test/run-module025-installed-sa-uat.py',
  'scripts/release-test/verify-flowhive-installed-identity.py',
  'tests/flowhive-psa-installed-acceptance.test.py',
  'tests/flowhive-psa-release-control.mjs'
].sort();
export const plannerOutputBudgetApprovalBase = 'dbe8c34eeb0b23ab0726cb414a614ec7f6ea221a';
export const plannerOutputBudgetApprovalBranch = 'control/flowhive-planner-output-budget-approval-20260912';
export const plannerOutputBudgetApprovalFiles = [
  '.github/flowhive-psa-protected-cutover.json',
  '.github/flowhive-psa-protected-test-candidate.json',
  'scripts/release-test/flowhive-psa-admission.mjs',
  'tests/flowhive-psa-admission.test.mjs',
  'tests/flowhive-psa-release-control.mjs'
].sort();
export const plannerTimeBudgetApprovalBase = '3b2c0111f780b85ee7258ff91e56cd92d144828a';
export const plannerTimeBudgetApprovalBranch = 'control/flowhive-planner-time-budget-approval-20260912';
export const plannerTimeBudgetApprovalFiles = [
  '.github/flowhive-psa-protected-cutover.json',
  '.github/flowhive-psa-protected-test-candidate.json',
  '.github/workflows/projectpulse-release-test-control-ci-reregistered.yml',
  '.github/workflows/projectpulse-release-test-control-ci.yml',
  'scripts/release-test/dispatch-flowhive-psa-test.mjs',
  'scripts/release-test/flowhive-psa-admission.mjs',
  'scripts/release-test/prepare-protected-test-scope-manifests.sh',
  'tests/flowhive-psa-admission.test.mjs',
  'tests/flowhive-psa-release-control.mjs'
].sort();
export const plannerProviderBudgetSuccessorApprovalBase = 'e173a4dc499dae723b2597d0ba1fde657a18f1d1';
export const plannerProviderBudgetSuccessorApprovalBranch = 'control/flowhive-planner-provider-budget-approval-20260912';
export const plannerProviderBudgetSuccessorApprovalFiles = [
  'scripts/release-test/flowhive-psa-admission.mjs',
  'tests/flowhive-psa-release-control.mjs'
].sort();
export const plannerTimeBudgetWorkflowSetBase = '59467301b02f9bf60531079f0c9dd51925eaa63d';
export const plannerTimeBudgetWorkflowSetBranch = 'control/flowhive-planner-time-budget-checkset-20260912';
export const plannerTimeBudgetWorkflowSetFiles = [
  '.github/flowhive-psa-protected-test-candidate.json',
  '.github/workflows/projectpulse-release-test-control-ci-reregistered.yml',
  '.github/workflows/projectpulse-release-test-control-ci.yml',
  'scripts/release-test/flowhive-psa-admission.mjs',
  'tests/flowhive-psa-admission.test.mjs',
  'tests/flowhive-psa-release-control.mjs'
].sort();
export const plannerTimeBudgetActivationBase = '13f1a406ddab791155404cd5ffa884c0323d47f4';
export const plannerTimeBudgetActivationBranch = 'control/flowhive-planner-time-budget-activation-20260912';
export const plannerTimeBudgetActivationFiles = [
  '.github/flowhive-psa-protected-cutover.json',
  '.github/workflows/projectpulse-release-test-control-ci-reregistered.yml',
  '.github/workflows/projectpulse-release-test-control-ci.yml',
  'scripts/release-test/dispatch-flowhive-psa-test.mjs',
  'tests/flowhive-psa-admission.test.mjs',
  'tests/flowhive-psa-release-control.mjs'
].sort();
export const plannerOutputBudgetActivationBase = 'e6ba3b8bd3d36653f04616514699ac242432bb1c';
export const plannerOutputBudgetActivationBranch = 'control/flowhive-planner-output-budget-activation-refresh-20260912';
export const plannerOutputBudgetActivationFiles = [
  '.github/flowhive-psa-protected-cutover.json',
  'tests/flowhive-psa-release-control.mjs'
].sort();
export const plannerOutputBudgetActivationRefreshBase = 'a184133eb517e1bdf17c1ea76cfcd21cd8d895c0';
export const plannerOutputBudgetActivationRefreshBranch = 'control/flowhive-planner-output-budget-activation-refresh2-20260912';
export const plannerOutputBudgetActivationRefreshFiles = [
  '.github/flowhive-psa-protected-cutover.json',
  'tests/flowhive-psa-release-control.mjs'
].sort();
export const staleSupersessionFiles = [
  '.github/flowhive-psa-release-control-files.txt',
  '.github/flowhive-psa-stale-run-supersession-authorization.json',
  '.github/workflows/flowhive-psa-protected-test-admission.yml',
  '.github/workflows/projectpulse-deploy-test.yml',
  '.github/workflows/projectpulse-release-test-control-ci-reregistered.yml',
  '.github/workflows/projectpulse-release-test-control-ci.yml',
  'scripts/release-test/dispatch-flowhive-psa-test.mjs',
  'scripts/release-test/flowhive-psa-admission.mjs',
  'tests/flowhive-psa-admission.test.mjs',
  'tests/flowhive-psa-release-control.mjs',
  'tests/flowhive-psa-release-workflow.test.py'
].sort();
export const staleSupersessionActivationFiles = [
  '.github/flowhive-psa-stale-run-supersession-authorization.json',
  'tests/flowhive-psa-admission.test.mjs',
  'tests/flowhive-psa-release-control.mjs'
].sort();
export const staleSupersessionRenewalFiles = [
  '.github/flowhive-psa-stale-run-supersession-authorization.json',
  'tests/flowhive-psa-admission.test.mjs',
  'tests/flowhive-psa-release-control.mjs'
].sort();
export const migration106WiringBranch = 'fix/flowhive-migration106-release-wiring-20260910';
export const migration106WiringFiles = [
  '.github/flowhive-psa-stale-run-supersession-authorization.json',
  'scripts/release-test/build-and-run-flowhive-psa-migrations.sh',
  'tests/flowhive-psa-admission.test.mjs',
  'tests/flowhive-psa-migration-fixture.py',
  'tests/flowhive-psa-release-control.mjs',
  'tests/flowhive-psa-release-workflow.test.py'
].sort();
export const reviewedRegenerationFiles = [
  '.github/flowhive-psa-protected-test-candidate.json',
  '.github/workflows/projectpulse-deploy-test.yml',
  '.github/workflows/projectpulse-release-test-control-ci-reregistered.yml',
  '.github/workflows/projectpulse-release-test-control-ci.yml',
  'docs/releases/FLOWHIVE-PSA-PROTECTED-TEST-ADMISSION.md',
  'scripts/release-test/apply-flowhive-psa-migrations.sh',
  'scripts/release-test/build-and-run-flowhive-psa-migrations.sh',
  'scripts/release-test/flowhive-psa-admission.mjs',
  'scripts/release-test/run-flowhive-psa-live-uat.py',
  'tests/flowhive-psa-live-uat.test.py',
  'tests/flowhive-psa-migration-fixture.py',
  'tests/flowhive-psa-release-control.mjs',
  'tests/flowhive-psa-release-workflow.test.py'
].sort();
const repairRepository = 'ahmedadeyemi-cts/project-time-platform';
const repairBranch = 'release/flowhive-psa-protected-test-admission-20260906';
export function verifyRepairContext(context) {
  assert.equal(context?.eventName, 'pull_request', 'PR876 repair requires a pull-request event.');
  assert.equal(context?.repository, repairRepository, 'Wrong repair repository.');
  assert.equal(context?.base, repairBase, 'PR876 repair is bound to the reviewed PR874 merge base.');
  assert.match(context?.head || '', /^[0-9a-f]{40}$/, 'Exact repair head is required.');
  const event = context?.event;
  assert.equal(event?.number, 876, 'The seven-file exception is exclusive to PR876.');
  assert.equal(event?.repository?.full_name, repairRepository, 'Wrong event repository.');
  const pr = event?.pull_request;
  assert.equal(pr?.number, 876, 'Wrong repair pull request.');
  assert.equal(pr?.state, 'open', 'The repair must still be open.');
  assert.equal(pr?.base?.ref, 'main', 'Wrong repair base branch.');
  assert.equal(pr?.base?.sha, repairBase, 'The event base is not the reviewed repair base.');
  assert.equal(pr?.base?.repo?.full_name, repairRepository, 'Wrong repair base repository.');
  assert.equal(pr?.head?.ref, repairBranch, 'Wrong repair source branch.');
  assert.equal(pr?.head?.repo?.full_name, repairRepository, 'Forked repair source is not admitted.');
  assert.equal(pr?.head?.sha, context.head, 'The checked-out repair head does not match the event.');
}
export function verifyFiles(changed, manifest, mode = 'initial', context = null) {
  assert.deepEqual(manifest, files, 'Approval must retain the exact reviewed control-only file list.');
  assert.ok(['initial','pr874-digest-repair','reviewed-regeneration-105','candidate-refresh','successor-candidate-refresh','source-base-correction','successor-approval','successor-release-approval','successor-cutover-activation','planner-output-budget-approval','planner-time-budget-approval','planner-provider-budget-successor-approval','planner-time-budget-workflow-set','planner-time-budget-activation','planner-output-budget-activation','planner-output-budget-activation-refresh','dispatch-run-recovery','dispatch-lane-repair','dispatch-skipped-recovery','dispatch-receipt-parser','dispatch-lane-admission','stale-run-supersession','stale-run-activation','stale-run-renewal','migration106-wiring','release-stabilization','protected-cutover','protected-cutover-activation','protected-cutover-refresh','protected-cutover-lane-refresh','protected-cutover-pm-identity-refresh','admission-permission-fix','admission-bot-claim-fix','protected-cutover-recovery','reservation-recovery-endpoint-fix','protected-cutover-final','live-uat-status-fix','installed-verification-environment','installed-verification-evidence','installed-acceptance-evidence','installed-acceptance-artifacts','installed-acceptance-readiness','installed-acceptance-visibility','installed-acceptance-planner','installed-acceptance-final'].includes(mode), 'Unrecognized control repair.');
  if (mode === 'pr874-digest-repair') verifyRepairContext(context);
  if (mode === 'reviewed-regeneration-105') {
    assert.equal(context?.base, reviewedRegenerationBase, 'Reviewed regeneration control must be based on current main.');
    assert.equal(context?.branch, reviewedRegenerationBranch, 'Wrong reviewed regeneration control branch.');
  }
  if (mode === 'candidate-refresh') {
    assert.equal(context?.base, candidateRefreshBase, 'Candidate refresh must be based on the reviewed current main.');
    assert.equal(context?.branch, candidateRefreshBranch, 'Wrong candidate refresh control branch.');
  }
  if (mode === 'successor-candidate-refresh') {
    assert.equal(context?.base, successorCandidateRefreshBase, 'Successor candidate refresh must be based on current trusted main.');
    assert.equal(context?.branch, successorCandidateRefreshBranch, 'Wrong successor candidate refresh control branch.');
  }
  if (mode === 'source-base-correction') {
    assert.equal(context?.base, sourceBaseCorrectionBase, 'Source-base correction must be based on the trusted candidate-refresh main.');
    assert.equal(context?.branch, sourceBaseCorrectionBranch, 'Wrong source-base correction control branch.');
  }
  if (mode === 'successor-approval') {
    assert.equal(context?.base, successorApprovalBase, 'Successor approval must be based on the current trusted main.');
    assert.equal(context?.branch, successorApprovalBranch, 'Wrong successor approval branch.');
  }
  if (mode === 'successor-release-approval') {
    assert.equal(context?.base, successorReleaseApprovalBase, 'Successor release approval must be based on the reviewed trusted main.');
    assert.equal(context?.branch, successorReleaseApprovalBranch, 'Wrong successor release approval branch.');
  }
  if (mode === 'planner-output-budget-approval') {
    assert.equal(context?.base, plannerOutputBudgetApprovalBase, 'Planner output-budget approval must be based on current trusted main.');
    assert.equal(context?.branch, plannerOutputBudgetApprovalBranch, 'Wrong planner output-budget approval branch.');
  }
  if (mode === 'planner-time-budget-approval') {
    assert.equal(context?.base, plannerTimeBudgetApprovalBase, 'Planner time-budget approval must be based on the exact trusted main containing PR933.');
    assert.equal(context?.branch, plannerTimeBudgetApprovalBranch, 'Wrong planner time-budget approval branch.');
  }
  if (mode === 'planner-provider-budget-successor-approval') {
    assert.equal(context?.base, plannerProviderBudgetSuccessorApprovalBase, 'Planner provider-budget successor approval must be based on the merged application main.');
    assert.equal(context?.branch, plannerProviderBudgetSuccessorApprovalBranch, 'Wrong planner provider-budget successor approval branch.');
  }
  if (mode === 'planner-time-budget-workflow-set') {
    assert.equal(context?.base, plannerTimeBudgetWorkflowSetBase, 'Planner workflow-set correction must be based on the merged approval control.');
    assert.equal(context?.branch, plannerTimeBudgetWorkflowSetBranch, 'Wrong planner workflow-set correction branch.');
  }
  if (mode === 'planner-time-budget-activation') {
    assert.equal(context?.base, plannerTimeBudgetActivationBase, 'Planner activation must be based on the exact approved current main.');
    assert.equal(context?.branch, plannerTimeBudgetActivationBranch, 'Wrong planner activation branch.');
  }
  if (mode === 'planner-output-budget-activation') {
    assert.equal(context?.base, plannerOutputBudgetActivationBase, 'Planner output-budget activation must be based on the approved candidate main.');
    assert.equal(context?.branch, plannerOutputBudgetActivationBranch, 'Wrong planner output-budget activation branch.');
  }
  if (mode === 'planner-output-budget-activation-refresh') {
    assert.equal(context?.base, plannerOutputBudgetActivationRefreshBase, 'Planner output-budget activation refresh must be based on the corrected trusted controller.');
    assert.equal(context?.branch, plannerOutputBudgetActivationRefreshBranch, 'Wrong planner output-budget activation refresh branch.');
  }
  if (mode === 'dispatch-run-recovery') {
    assert.equal(context?.base, dispatchRecoveryBase, 'Dispatch recovery must be based on merged trusted control main.');
    assert.equal(context?.branch, dispatchRecoveryBranch, 'Wrong dispatch recovery branch.');
  }
  if (mode === 'dispatch-lane-repair') {
    assert.equal(context?.base, dispatchLaneRepairBase, 'Dispatch-lane repair must be based on the merged trusted controller.');
    assert.equal(context?.branch, dispatchLaneRepairBranch, 'Wrong dispatch-lane repair branch.');
  }
  if (mode === 'dispatch-skipped-recovery') {
    assert.equal(context?.base, dispatchSkippedRecoveryBase, 'Skipped-dispatch recovery must be based on the corrected trusted controller.');
    assert.equal(context?.branch, dispatchSkippedRecoveryBranch, 'Wrong skipped-dispatch recovery branch.');
  }
  if (mode === 'dispatch-receipt-parser') {
    assert.equal(context?.base, dispatchReceiptParserBase, 'Dispatch receipt parser must be based on merged trusted main.');
    assert.equal(context?.branch, dispatchReceiptParserBranch, 'Wrong dispatch receipt parser branch.');
  }
  if (mode === 'dispatch-lane-admission') {
    assert.equal(context?.base, dispatchLaneAdmissionBase, 'Dispatch lane admission must be based on merged trusted main.');
    assert.equal(context?.branch, dispatchLaneAdmissionBranch, 'Wrong dispatch lane admission branch.');
  }
  if (mode === 'protected-cutover-lane-refresh') {
    assert.equal(context?.base, protectedCutoverLaneRefreshBase, 'Protected cutover lane refresh must be based on the merged admission-lane correction.');
    assert.equal(context?.branch, protectedCutoverLaneRefreshBranch, 'Wrong protected cutover lane refresh branch.');
  }
  if (mode === 'protected-cutover-pm-identity-refresh') {
    assert.equal(context?.base, protectedCutoverPmIdentityRefreshBase, 'Protected cutover PM identity refresh must be based on trusted main.');
    assert.equal(context?.branch, protectedCutoverPmIdentityRefreshBranch, 'Wrong protected cutover PM identity refresh branch.');
  }
  if (mode === 'stale-run-supersession') {
    assert.equal(context?.base, staleSupersessionBase, 'Stale supersession must be based on merged trusted main.');
    assert.equal(context?.branch, staleSupersessionBranch, 'Wrong stale supersession control branch.');
  }
  if (mode === 'stale-run-activation') {
    assert.equal(context?.base, staleSupersessionActivationBase, 'Stale activation must be based on current trusted main.');
    assert.equal(context?.branch, staleSupersessionActivationBranch, 'Wrong stale activation control branch.');
  }
  if (mode === 'stale-run-renewal') {
    assert.equal(context?.base, staleSupersessionRenewalBase, 'Stale renewal must be based on the current trusted main.');
    assert.equal(context?.branch, staleSupersessionRenewalBranch, 'Wrong stale renewal control branch.');
  }
  if (mode === 'release-stabilization') {
    assert.equal(context?.base, releaseStabilizationBase, 'Release stabilization must be based on the current trusted main.');
    assert.equal(context?.branch, releaseStabilizationBranch, 'Wrong release stabilization branch.');
  }
  if (mode === 'protected-cutover') {
    assert.equal(context?.base, protectedCutoverBase, 'Protected cutover must be based on merged trusted main.');
    assert.equal(context?.branch, protectedCutoverBranch, 'Wrong protected cutover control branch.');
  }
  if (mode === 'protected-cutover-refresh') {
    assert.equal(context?.base, context?.branch === protectedCutoverLaneRefreshBranch ? protectedCutoverLaneRefreshBase : protectedCutoverRefreshBase,
      'Protected cutover refresh must be based on the reviewed trusted main.');
    assert.equal(context?.branch, context?.branch === protectedCutoverLaneRefreshBranch ? protectedCutoverLaneRefreshBranch : protectedCutoverRefreshBranch,
      'Wrong protected cutover refresh control branch.');
  }
  if (mode === 'admission-permission-fix') {
    assert.equal(context?.base, admissionPermissionFixBase, 'Permission fix must be based on the merged activation controls.');
    assert.equal(context?.branch, admissionPermissionFixBranch, 'Wrong admission permission-fix branch.');
  }
  if (mode === 'admission-bot-claim-fix') {
    assert.equal(context?.base, admissionBotClaimFixBase, 'Bot-claim fix must be based on current trusted main.');
    assert.equal(context?.branch, admissionBotClaimFixBranch, 'Wrong bot-claim fix branch.');
  }
  if (mode === 'protected-cutover-recovery') {
    assert.equal(context?.base, protectedCutoverRecoveryBase, 'Protected cutover recovery must be based on PR901 trusted main.');
    assert.equal(context?.branch, protectedCutoverRecoveryBranch, 'Wrong protected cutover recovery branch.');
  }
  if (mode === 'reservation-recovery-endpoint-fix') {
    assert.equal(context?.base, reservationRecoveryEndpointFixBase, 'Reservation recovery endpoint fix must be based on current trusted main.');
    assert.equal(context?.branch, reservationRecoveryEndpointFixBranch, 'Wrong reservation recovery endpoint branch.');
  }
  if (mode === 'protected-cutover-final') {
    assert.equal(context?.base, protectedCutoverFinalBase, 'Final protected cutover must be based on PR903 trusted main.');
    assert.equal(context?.branch, protectedCutoverFinalBranch, 'Wrong final protected cutover branch.');
  }
  if (mode === 'live-uat-status-fix') {
    assert.equal(context?.base, liveUatStatusFixBase, 'Live UAT verifier fix must be based on current trusted main.');
    assert.equal(context?.branch, liveUatStatusFixBranch, 'Wrong live UAT verifier fix branch.');
  }
  if (mode === 'installed-verification-environment') {
    assert.equal(context?.base, installedVerificationEnvironmentBase, 'Installed verification environment fix must be based on current trusted main.');
    assert.equal(context?.branch, installedVerificationEnvironmentBranch, 'Wrong installed verification environment branch.');
  }
  if (mode === 'installed-verification-evidence') {
    assert.equal(context?.base, installedVerificationEvidenceBase, 'Installed verification evidence fix must be based on current trusted main.');
    assert.equal(context?.branch, installedVerificationEvidenceBranch, 'Wrong installed verification evidence branch.');
  }
  if (mode === 'installed-acceptance-evidence') {
    assert.equal(context?.base, installedAcceptanceEvidenceBase, 'Installed acceptance evidence fix must be based on current trusted main.');
    assert.equal(context?.branch, installedAcceptanceEvidenceBranch, 'Wrong installed acceptance evidence branch.');
  }
  if (mode === 'installed-acceptance-artifacts') {
    assert.equal(context?.base, installedAcceptanceArtifactsBase, 'Installed acceptance artifact fix must be based on current trusted main.');
    assert.equal(context?.branch, installedAcceptanceArtifactsBranch, 'Wrong installed acceptance artifact branch.');
  }
  if (mode === 'installed-acceptance-readiness') {
    assert.equal(context?.base, installedAcceptanceReadinessBase, 'Installed acceptance readiness must be based on current trusted main.');
    assert.equal(context?.branch, installedAcceptanceReadinessBranch, 'Wrong installed acceptance readiness branch.');
  }
  if (mode === 'installed-acceptance-visibility') {
    assert.equal(context?.base, installedAcceptanceVisibilityBase, 'Installed acceptance visibility must be based on current trusted main.');
    assert.equal(context?.branch, installedAcceptanceVisibilityBranch, 'Wrong installed acceptance visibility branch.');
  }
  if (mode === 'installed-acceptance-planner') {
    assert.equal(context?.base, installedAcceptancePlannerBase, 'Installed acceptance planner reconciliation must be based on current trusted main.');
    assert.equal(context?.branch, installedAcceptancePlannerBranch, 'Wrong installed acceptance planner branch.');
  }
  if (mode === 'installed-acceptance-final') {
    assert.equal(context?.base, installedAcceptanceFinalBase, 'Installed acceptance final verifier must be based on current trusted main.');
    assert.equal(context?.branch, installedAcceptanceFinalBranch, 'Wrong installed acceptance final verifier branch.');
  }
  const expected = mode === 'initial' ? files : mode === 'pr874-digest-repair' ? repairFiles : mode === 'reviewed-regeneration-105' ? reviewedRegenerationFiles : mode === 'candidate-refresh' ? candidateRefreshFiles : mode === 'successor-candidate-refresh' ? successorCandidateRefreshFiles : mode === 'source-base-correction' ? sourceBaseCorrectionFiles : mode === 'successor-approval' ? successorApprovalFiles : mode === 'successor-release-approval' ? successorReleaseApprovalFiles : mode === 'successor-cutover-activation' ? successorCutoverActivationFiles : mode === 'planner-output-budget-approval' ? plannerOutputBudgetApprovalFiles : mode === 'planner-time-budget-approval' ? plannerTimeBudgetApprovalFiles : mode === 'planner-provider-budget-successor-approval' ? plannerProviderBudgetSuccessorApprovalFiles : mode === 'planner-time-budget-workflow-set' ? plannerTimeBudgetWorkflowSetFiles : mode === 'planner-time-budget-activation' ? plannerTimeBudgetActivationFiles : mode === 'planner-output-budget-activation' ? plannerOutputBudgetActivationFiles : mode === 'planner-output-budget-activation-refresh' ? plannerOutputBudgetActivationRefreshFiles : mode === 'dispatch-run-recovery' ? dispatchRecoveryFiles : mode === 'dispatch-lane-repair' ? dispatchLaneRepairFiles : mode === 'dispatch-skipped-recovery' ? dispatchSkippedRecoveryFiles : mode === 'dispatch-receipt-parser' ? dispatchReceiptParserFiles : mode === 'dispatch-lane-admission' ? dispatchLaneAdmissionFiles : mode === 'stale-run-activation' ? staleSupersessionActivationFiles : mode === 'stale-run-renewal' ? staleSupersessionRenewalFiles : mode === 'migration106-wiring' ? migration106WiringFiles : mode === 'release-stabilization' ? releaseStabilizationFiles : mode === 'protected-cutover' ? protectedCutoverFiles : mode === 'protected-cutover-activation' ? protectedCutoverActivationFiles : mode === 'protected-cutover-refresh' ? protectedCutoverRefreshFiles : mode === 'admission-permission-fix' ? admissionPermissionFixFiles : mode === 'admission-bot-claim-fix' ? admissionBotClaimFixFiles : mode === 'protected-cutover-recovery' ? protectedCutoverRecoveryFiles : mode === 'reservation-recovery-endpoint-fix' ? reservationRecoveryEndpointFixFiles : mode === 'protected-cutover-final' ? protectedCutoverFinalFiles : mode === 'live-uat-status-fix' ? liveUatStatusFixFiles : mode === 'installed-verification-environment' ? installedVerificationEnvironmentFiles : mode === 'installed-verification-evidence' ? installedVerificationEvidenceFiles : mode === 'installed-acceptance-evidence' ? installedAcceptanceEvidenceFiles : mode === 'installed-acceptance-artifacts' ? installedAcceptanceArtifactsFiles : mode === 'installed-acceptance-readiness' ? installedAcceptanceReadinessFiles : mode === 'installed-acceptance-visibility' ? installedAcceptanceVisibilityFiles : mode === 'installed-acceptance-planner' ? installedAcceptancePlannerFiles : mode === 'installed-acceptance-final' ? installedAcceptanceFinalFiles : staleSupersessionFiles;
  const scopedExpected = mode === 'protected-cutover-lane-refresh' ? protectedCutoverLaneRefreshFiles : mode === 'protected-cutover-pm-identity-refresh' ? protectedCutoverPmIdentityRefreshFiles : expected;
  assert.deepEqual([...changed].sort(), scopedExpected, 'Unexpected or missing file in the release-control PR.');
}
export function verifyController(text) {
  for (const token of [
    'group: projectpulse-deploy-test', 'queue: max', 'cancel-in-progress: false', 'environment: test',
    'node scripts/release-test/flowhive-psa-admission.mjs', 'PSA_RELEASE_AUTHORIZED',
    'refs/heads/main', '105_flowhive_reviewed_regeneration.sql', '106_module025_sow_sell_register.sql', '107_module_066_operation_authorization_and_raid_actor.sql', 'build-and-run-flowhive-psa-migrations.sh',
    'run-flowhive-psa-live-uat.py', 'RELIABILITY_RELEASE_COMMIT:', 'Seal server-confirmed deployment identity',
    "steps.psa_live_uat.outputs.deployment_health_verified != 'true'",
  ]) {
    assert.ok(text.includes(token), `The Test controller is missing a required control: ${token}`);
  }
  assert.match(text, /on:\s*\n\s+workflow_dispatch:/,
    'The canonical Test controller must be dispatch-only.');
  assert.doesNotMatch(text, /^\s+push:\s*$/m,
    'Automatic push deployment must not remain enabled.');
  assert.match(text, /deploy:\s*\n[\s\S]*?if: >-\n[\s\S]*github\.event_name == 'workflow_dispatch'[\s\S]*inputs\.release_branch == 'main'[\s\S]*inputs\.release_branch == 'release\/flowhive-sow-successor-20260908'/,
    'Every current deployment path must be bounded by an explicitly guarded manual-main/PSA dispatch lane.');
  assert.doesNotMatch(text, /github\.event_name == 'push'|GITHUB_EVENT_NAME.*== push/,
    'The controller must not retain an executable push deployment path.');
  assert.match(text, /deployment-identity\.json/,
    'The controller must seal server-confirmed API/web identity evidence.');
  assert.doesNotMatch(text, /github\.event_name == 'workflow_dispatch' \|\| github\.ref == 'refs\/heads\/main'/,
    'The old unbounded workflow_dispatch job gate must not remain.');
  assert.ok(!/contents:\s*write/.test(text), 'The environment mutation job must not publish source.');
  assert.ok(!/environment:\s*(?:production|prod)\b/i.test(text), 'Production is not an approved target.');
  assert.match(text, /Verify admitted controller identity before deployment mutations/);
  assert.match(text, /admission_controller_sha/);
  assert.match(text, /PSA admission controller identity changed before deployment/);
}
export function validate() {
  const git = (...args) => execFileSync('git', args, { encoding: 'utf8' }).trim();
  const base = git('merge-base', process.env.BASE_SHA || 'origin/main', 'HEAD');
  assert.match(base, /^[0-9a-f]{40}$/);
  const changed = git('diff', '--name-only', base, 'HEAD').split(/\r?\n/).filter(Boolean);
  const manifest = fs.readFileSync(controlManifest, 'utf8').trim().split(/\r?\n/);
  const event = process.env.GITHUB_EVENT_PATH
    ? JSON.parse(fs.readFileSync(process.env.GITHUB_EVENT_PATH, 'utf8')) : null;
  const context = { event, eventName: process.env.GITHUB_EVENT_NAME,
    repository: process.env.GITHUB_REPOSITORY, base, branch: process.env.GITHUB_HEAD_REF,
    head: git('rev-parse', 'HEAD') };
  const isRepair = event?.number === 876;
  const isReviewedRegeneration = process.env.GITHUB_HEAD_REF === reviewedRegenerationBranch;
  const isCandidateRefresh = process.env.GITHUB_HEAD_REF === candidateRefreshBranch;
  const isSuccessorCandidateRefresh = process.env.GITHUB_HEAD_REF === successorCandidateRefreshBranch;
  const isSourceBaseCorrection = process.env.GITHUB_HEAD_REF === sourceBaseCorrectionBranch;
  const isSuccessorApproval = process.env.GITHUB_HEAD_REF === successorApprovalBranch;
  const isSuccessorReleaseApproval = process.env.GITHUB_HEAD_REF === successorReleaseApprovalBranch;
  const isSuccessorCutoverActivation = process.env.GITHUB_HEAD_REF === successorCutoverActivationBranch;
  const isPlannerOutputBudgetApproval = process.env.GITHUB_HEAD_REF === plannerOutputBudgetApprovalBranch;
  const isPlannerTimeBudgetApproval = process.env.GITHUB_HEAD_REF === plannerTimeBudgetApprovalBranch;
  const isPlannerProviderBudgetSuccessorApproval = process.env.GITHUB_HEAD_REF === plannerProviderBudgetSuccessorApprovalBranch;
  const isPlannerTimeBudgetWorkflowSet = process.env.GITHUB_HEAD_REF === plannerTimeBudgetWorkflowSetBranch;
  const isPlannerTimeBudgetActivation = process.env.GITHUB_HEAD_REF === plannerTimeBudgetActivationBranch;
  const isPlannerOutputBudgetActivation = process.env.GITHUB_HEAD_REF === plannerOutputBudgetActivationBranch;
  const isPlannerOutputBudgetActivationRefresh = process.env.GITHUB_HEAD_REF === plannerOutputBudgetActivationRefreshBranch;
  const isDispatchRunRecovery = process.env.GITHUB_HEAD_REF === dispatchRecoveryBranch;
  const isDispatchLaneRepair = process.env.GITHUB_HEAD_REF === dispatchLaneRepairBranch;
  const isDispatchSkippedRecovery = process.env.GITHUB_HEAD_REF === dispatchSkippedRecoveryBranch;
  const isDispatchReceiptParser = process.env.GITHUB_HEAD_REF === dispatchReceiptParserBranch;
  const isDispatchLaneAdmission = process.env.GITHUB_HEAD_REF === dispatchLaneAdmissionBranch;
  const isProtectedCutoverLaneRefresh = process.env.GITHUB_HEAD_REF === protectedCutoverLaneRefreshBranch;
  const isProtectedCutoverPmIdentityRefresh = process.env.GITHUB_HEAD_REF === protectedCutoverPmIdentityRefreshBranch;
  const isStaleSupersession = process.env.GITHUB_HEAD_REF === staleSupersessionBranch;
  const isStaleSupersessionActivation = process.env.GITHUB_HEAD_REF === staleSupersessionActivationBranch;
  const isStaleSupersessionRenewal = process.env.GITHUB_HEAD_REF === staleSupersessionRenewalBranch;
  const isMigration106Wiring = process.env.GITHUB_HEAD_REF === migration106WiringBranch;
  const isReleaseStabilization = process.env.GITHUB_HEAD_REF === releaseStabilizationBranch;
  const isProtectedCutover = process.env.GITHUB_HEAD_REF === protectedCutoverBranch;
  const isProtectedCutoverActivation = process.env.GITHUB_HEAD_REF === protectedCutoverActivationBranch;
  const isProtectedCutoverRefresh = process.env.GITHUB_HEAD_REF === protectedCutoverRefreshBranch || isProtectedCutoverLaneRefresh || isProtectedCutoverPmIdentityRefresh;
  const isAdmissionPermissionFix = process.env.GITHUB_HEAD_REF === admissionPermissionFixBranch;
  const isAdmissionBotClaimFix = process.env.GITHUB_HEAD_REF === admissionBotClaimFixBranch;
  const isProtectedCutoverRecovery = process.env.GITHUB_HEAD_REF === protectedCutoverRecoveryBranch;
  const isReservationRecoveryEndpointFix = process.env.GITHUB_HEAD_REF === reservationRecoveryEndpointFixBranch;
  const isProtectedCutoverFinal = process.env.GITHUB_HEAD_REF === protectedCutoverFinalBranch;
  const isLiveUatStatusFix = process.env.GITHUB_HEAD_REF === liveUatStatusFixBranch;
  const isInstalledVerificationEnvironment = process.env.GITHUB_HEAD_REF === installedVerificationEnvironmentBranch;
  const isInstalledVerificationEvidence = process.env.GITHUB_HEAD_REF === installedVerificationEvidenceBranch;
  const isInstalledAcceptanceEvidence = process.env.GITHUB_HEAD_REF === installedAcceptanceEvidenceBranch;
  const isInstalledAcceptanceArtifacts = process.env.GITHUB_HEAD_REF === installedAcceptanceArtifactsBranch;
  const isInstalledAcceptanceReadiness = process.env.GITHUB_HEAD_REF === installedAcceptanceReadinessBranch;
  const isInstalledAcceptanceVisibility = process.env.GITHUB_HEAD_REF === installedAcceptanceVisibilityBranch;
  const isInstalledAcceptancePlanner = process.env.GITHUB_HEAD_REF === installedAcceptancePlannerBranch;
  const isInstalledAcceptanceFinal = process.env.GITHUB_HEAD_REF === installedAcceptanceFinalBranch;
  verifyFiles(changed, manifest,
    isRepair ? 'pr874-digest-repair' : isReviewedRegeneration ? 'reviewed-regeneration-105' : isCandidateRefresh ? 'candidate-refresh' : isSuccessorCandidateRefresh ? 'successor-candidate-refresh' : isSourceBaseCorrection ? 'source-base-correction' : isSuccessorApproval ? 'successor-approval' : isSuccessorReleaseApproval ? 'successor-release-approval' : isSuccessorCutoverActivation ? 'successor-cutover-activation' : isPlannerOutputBudgetApproval ? 'planner-output-budget-approval' : isPlannerTimeBudgetApproval ? 'planner-time-budget-approval' : isPlannerProviderBudgetSuccessorApproval ? 'planner-provider-budget-successor-approval' : isPlannerTimeBudgetWorkflowSet ? 'planner-time-budget-workflow-set' : isPlannerTimeBudgetActivation ? 'planner-time-budget-activation' : isPlannerOutputBudgetActivation ? 'planner-output-budget-activation' : isPlannerOutputBudgetActivationRefresh ? 'planner-output-budget-activation-refresh' : isDispatchRunRecovery ? 'dispatch-run-recovery' : isDispatchLaneRepair ? 'dispatch-lane-repair' : isDispatchSkippedRecovery ? 'dispatch-skipped-recovery' : isDispatchReceiptParser ? 'dispatch-receipt-parser' : isDispatchLaneAdmission ? 'dispatch-lane-admission' : isProtectedCutoverLaneRefresh ? 'protected-cutover-lane-refresh' : isProtectedCutoverPmIdentityRefresh ? 'protected-cutover-pm-identity-refresh' : isStaleSupersession ? 'stale-run-supersession' : isStaleSupersessionActivation ? 'stale-run-activation' : isStaleSupersessionRenewal ? 'stale-run-renewal' : isMigration106Wiring ? 'migration106-wiring' : isProtectedCutover ? 'protected-cutover' : isProtectedCutoverActivation ? 'protected-cutover-activation' : isProtectedCutoverRefresh ? 'protected-cutover-refresh' : isAdmissionPermissionFix ? 'admission-permission-fix' : isAdmissionBotClaimFix ? 'admission-bot-claim-fix' : isProtectedCutoverRecovery ? 'protected-cutover-recovery' : isReservationRecoveryEndpointFix ? 'reservation-recovery-endpoint-fix' : isProtectedCutoverFinal ? 'protected-cutover-final' : isInstalledAcceptanceFinal ? 'installed-acceptance-final' : isInstalledAcceptancePlanner ? 'installed-acceptance-planner' : isInstalledAcceptanceVisibility ? 'installed-acceptance-visibility' : isInstalledAcceptanceReadiness ? 'installed-acceptance-readiness' : isInstalledAcceptanceArtifacts ? 'installed-acceptance-artifacts' : isInstalledAcceptanceEvidence ? 'installed-acceptance-evidence' : isInstalledVerificationEvidence ? 'installed-verification-evidence' : isInstalledVerificationEnvironment ? 'installed-verification-environment' : isLiveUatStatusFix ? 'live-uat-status-fix' : isReleaseStabilization ? 'release-stabilization' : 'initial', context);
  if (isDispatchReceiptParser) {
    assert.equal(fs.readFileSync('.github/flowhive-psa-protected-cutover.json', 'utf8').trimEnd(),
      git('show', `${base}:.github/flowhive-psa-protected-cutover.json`),
      'Receipt parser correction must not alter release authorization.');
  }
  if (isRepair) {
    // The repair cannot alter the admitted environment workflow, permissions,
    // migration bytes or dispatcher. Only its exact seven-file list is allowed.
    assert.equal(fs.readFileSync('.github/workflows/projectpulse-deploy-test.yml','utf8').trimEnd(),
      git('show', `${base}:.github/workflows/projectpulse-deploy-test.yml`));
  }
  for (const file of files) assert.ok(fs.statSync(file).isFile() && !fs.lstatSync(file).isSymbolicLink());
  const approval = JSON.parse(fs.readFileSync('.github/flowhive-psa-protected-test-candidate.json', 'utf8'));
  verifyApproval(approval, approval.sha);
  const staleAuthorization = JSON.parse(fs.readFileSync('.github/flowhive-psa-stale-run-supersession-authorization.json', 'utf8'));
  const protectedCutover = JSON.parse(fs.readFileSync('.github/flowhive-psa-protected-cutover.json', 'utf8'));
  if (isStaleSupersessionActivation || isStaleSupersessionRenewal) {
    assert.equal(staleAuthorization.enabled, true, 'Activation must be explicit and limited to the reviewed activation branch.');
    assert.equal(staleAuthorization.activationDecision, 'approved');
    assert.equal(staleAuthorization.approval.status, 'approved');
    assert.equal(staleAuthorization.approval.approvedBy, 'ahmedadeyemi-cts');
    const approvedAt = Date.parse(staleAuthorization.approval.approvedAt);
    const expiresAt = Date.parse(staleAuthorization.approval.expiresAt);
    assert.ok(Number.isFinite(approvedAt) && Number.isFinite(expiresAt));
    assert.ok(expiresAt > approvedAt && expiresAt - approvedAt <= 15 * 60 * 1000);
  } else if (isReleaseStabilization) {
    assert.equal(staleAuthorization.enabled, false, 'Expired recovery authorization must be inactive in stabilization.');
    assert.equal(staleAuthorization.activationDecision, 'hold');
    assert.equal(staleAuthorization.approval.status, 'not-approved');
    assert.equal(staleAuthorization.approval.approvedBy, null);
    assert.equal(staleAuthorization.approval.approvedAt, null);
    assert.equal(staleAuthorization.approval.expiresAt, null);
  } else {
    assert.equal(staleAuthorization.enabled, false, 'Stale supersession must remain inactive outside the reviewed activation branch.');
    assert.equal(staleAuthorization.activationDecision, 'hold');
  }
  if (isProtectedCutover || isProtectedCutoverActivation || isProtectedCutoverRefresh || isProtectedCutoverRecovery || isProtectedCutoverFinal || isSuccessorReleaseApproval || isSuccessorCutoverActivation || isPlannerTimeBudgetApproval || isPlannerProviderBudgetSuccessorApproval || isPlannerTimeBudgetActivation || isPlannerOutputBudgetActivation || isPlannerOutputBudgetActivationRefresh || isDispatchSkippedRecovery) {
    if (isProtectedCutoverActivation || isProtectedCutoverRefresh || isProtectedCutoverRecovery || isProtectedCutoverFinal || isSuccessorCutoverActivation || isPlannerTimeBudgetActivation || isPlannerOutputBudgetActivation || isPlannerOutputBudgetActivationRefresh || isDispatchSkippedRecovery) {
      assert.equal(base, isPlannerTimeBudgetActivation ? plannerTimeBudgetActivationBase : isProtectedCutoverLaneRefresh ? protectedCutoverLaneRefreshBase : isProtectedCutoverPmIdentityRefresh ? protectedCutoverPmIdentityRefreshBase : isDispatchSkippedRecovery ? dispatchSkippedRecoveryBase : isPlannerOutputBudgetActivationRefresh ? plannerOutputBudgetActivationRefreshBase : isPlannerOutputBudgetActivation ? plannerOutputBudgetActivationBase : isSuccessorCutoverActivation ? successorCutoverActivationBase : isProtectedCutoverFinal ? protectedCutoverFinalBase : isProtectedCutoverRecovery ? protectedCutoverRecoveryBase : isProtectedCutoverRefresh ? protectedCutoverRefreshBase : protectedCutoverActivationBase, 'Activation must be based on the latest reviewed trusted controls.');
      assert.equal(protectedCutover.enabled, true, 'Activation must be explicitly enabled only in its reviewed branch.');
      assert.equal(protectedCutover.activationDecision, 'approved');
      assert.equal(protectedCutover.workflow.allowControllerActivation, isPlannerTimeBudgetActivation || isSuccessorCutoverActivation || isPlannerOutputBudgetActivation || isPlannerOutputBudgetActivationRefresh || isDispatchSkippedRecovery || isProtectedCutoverLaneRefresh || isProtectedCutoverPmIdentityRefresh ? false : true);
      assert.equal(protectedCutover.approval.status, 'approved');
      assert.equal(protectedCutover.approval.approvedBy, 'ahmedadeyemi-cts');
      const approvedAt = Date.parse(protectedCutover.approval.approvedAt);
      const expiresAt = Date.parse(protectedCutover.approval.expiresAt);
      assert.ok(Number.isFinite(approvedAt) && Number.isFinite(expiresAt) && expiresAt > approvedAt);
      assert.ok(expiresAt - approvedAt <= 15 * 60 * 1000, 'Activation approval must remain bounded.');
    }
    assert.equal(protectedCutover.contract, 'flowhive-psa-protected-cutover-v1');
    if (isProtectedCutover || isSuccessorReleaseApproval || isPlannerTimeBudgetApproval || isPlannerProviderBudgetSuccessorApproval) {
      assert.equal(protectedCutover.enabled, false, 'Protected cutover must remain inactive until separately approved.');
      assert.equal(protectedCutover.activationDecision, 'hold');
    }
    const candidate = JSON.parse(fs.readFileSync('.github/flowhive-psa-protected-test-candidate.json', 'utf8'));
    assert.deepEqual(protectedCutover.candidate, {
      pullRequest: candidate.pullRequest, branch: candidate.branch, sha: candidate.sha
    });
    if (isProtectedCutoverRecovery) {
      assert.deepEqual(protectedCutover.reservationRecovery, {
        commentId: 5626123050,
        admissionRunId: 34536122774,
        admissionRunAttempt: 1,
        candidateSha: '95abbb0aa2445a33fda68e9de542f9446c3e2204',
        approvalReference: 'FLOWHIVE-PSA-PROTECTED-CUTOVER-20260910',
        controllerSha: 'c78d346042314a7395a2ceeaa46f2737e6c0b076',
        observedAt: '2026-09-10T22:11:16.764Z',
        status: 'pre-dispatch-failed', dispatchSubmitted: false, controllerMutation: false
      });
    }
    if (isDispatchSkippedRecovery) {
      assert.deepEqual(protectedCutover.reservationRecovery, {
        status: 'terminal-skipped-no-mutation',
        commentId: 5645243064,
        admissionRunId: 34687715702,
        admissionRunAttempt: 1,
        candidateSha: '86c9be03b87e588eeec47492e35131177716263b',
        approvalReference: 'FLOWHIVE-PSA-PROTECTED-CUTOVER-20260911',
        controllerSha: 'e2289c617b69b1a563ee8bd6f01aaa5c54965c6a',
        observedAt: '2026-09-12T10:10:03.389Z',
        dispatchSubmitted: true,
        controllerMutation: false,
        deploymentRunId: 34687729664,
        deploymentRunAttempt: 1,
        dispatchReceipt: {
          artifactId: 10296121750,
          artifactName: 'flowhive-psa-dispatch-evidence-34687715702-1',
          artifactDigest: 'sha256:dcbd54c84059b52885bc5e54956574c8c01354ddad3efe39a156850e371e378c',
          schema: 'flowhive-psa-dispatch-attempt-v1',
          candidateSha: '86c9be03b87e588eeec47492e35131177716263b',
          controllerSha: 'e2289c617b69b1a563ee8bd6f01aaa5c54965c6a',
          admissionRunId: 34687715702,
          admissionRunAttempt: 1,
          dispatchPath: 'actions/workflows/315562561/dispatches',
          dispatchRequestFingerprint: 'c4c46ec21d11e1f69e6d375aa2f395b1cd1be8f7f6046823f6e83dd53974f955',
          dispatchWriteCount: 1,
          deploymentRunId: 34687729664,
          deploymentRunAttempt: 1
        }
      });
    }
    assert.deepEqual(protectedCutover.workflow, {
      id: 315562561, path: '.github/workflows/projectpulse-deploy-test.yml',
      controllerBranch: 'main', event: 'workflow_dispatch',
      transition: 'disabled_manually-to-active-once', allowControllerActivation: isProtectedCutoverActivation || (isProtectedCutoverRefresh && !isProtectedCutoverLaneRefresh && !isProtectedCutoverPmIdentityRefresh) || isProtectedCutoverRecovery || isProtectedCutoverFinal
    });
    assert.deepEqual(protectedCutover.environment, {
      environment: 'test', protectionRuleId: 65110773,
      requiredReviewerLogin: 'ahmedadeyemi-cts', requiredReviewerId: 244059331,
      reviewers: [{ type: 'User', login: 'ahmedadeyemi-cts', id: 244059331 }],
      preventSelfReview: false, canAdminsBypass: false
    });
    assert.deepEqual(protectedCutover.requests.map(request => request.runId), [34495606530, 34377182662, 33654881418]);
    assert.equal(protectedCutover.serverDispatchInputsConfirmed, false);
    if (isProtectedCutover || isSuccessorReleaseApproval || isPlannerTimeBudgetApproval || isPlannerProviderBudgetSuccessorApproval) {
      assert.deepEqual(protectedCutover.approval, { status: 'not-approved', approvedBy: null, approvedAt: null, expiresAt: null });
    } else {
      assert.equal(protectedCutover.approval.status, 'approved');
      assert.equal(protectedCutover.approval.approvedBy, 'ahmedadeyemi-cts');
    }
  }
  assert.equal(staleAuthorization.historicalExecutionProtection.allDeploymentPathsProtected, false);
  assert.equal(staleAuthorization.historicalExecutionProtection.jobUsesTestEnvironment, true);
  assert.deepEqual(staleAuthorization.historicalExecutionProtection.nativeEnvironmentBarrier, {
    environment: 'test', protectionRuleId: 65110773, requiredReviewerLogin: 'ahmedadeyemi-cts',
    requiredReviewerId: 244059331, preventSelfReview: false, canAdminsBypass: false
  });
  assert.deepEqual(staleAuthorization.evidence.nativeEnvironmentCoverage.map(run => ({
    runId: run.runId, status: run.status, jobs: run.jobs, pendingDeployments: run.pendingDeployments,
    approvalPerformed: run.approvalPerformed
  })), [
    { runId: 34377182662, status: 'queued', jobs: 0, pendingDeployments: 0, approvalPerformed: false },
    { runId: 33654881418, status: 'queued', jobs: 0, pendingDeployments: 0, approvalPerformed: false }
  ]);
  assert.deepEqual(staleAuthorization.evidence.currentOutstandingRequest, {
    runId: 34495606530,
    workflowId: 315562561,
    workflowPath: '.github/workflows/projectpulse-deploy-test.yml',
    controllerSha: '9f30078c2c407d4d3576ccefd663a145be50c6c4',
    status: 'queued', jobs: 0, pendingDeployments: 0, approvalPerformed: false,
    disposition: 'blocking-hold', dispositionSource: 'release-owner-record',
    nextAction: 'Use one separately reviewed run-control operation for each of the three queued requests, verify server-confirmed terminal state and no execution, then use the native workflow enable operation once and verify active identity before a new admission.'
  });
  assert.equal(staleAuthorization.evidence.requestToRunBinding.status, 'not-established');
  assert.equal(staleAuthorization.evidence.requestToRunBinding.serverConfirmed, false);
  assert.equal(staleAuthorization.evidence.requestToRunBinding.requestId, null);
  assert.equal(staleAuthorization.evidence.requestToRunBinding.response, null);
  verifyController(fs.readFileSync('.github/workflows/projectpulse-deploy-test.yml', 'utf8'));
  const supervisor = fs.readFileSync('.github/workflows/flowhive-psa-protected-test-admission.yml', 'utf8');
  assert.ok(!/azure\/login|id-token:|environment:|contents:\s*write/.test(supervisor), 'Admission cannot mutate a cloud environment or source.');
  assert.ok(supervisor.includes('github.event.issue.number == 887') && supervisor.includes("github.actor == 'ahmedadeyemi-cts'"));
  assert.ok(supervisor.includes('group: module025-protected-uat-control') && supervisor.includes('cancel-in-progress: false'));
  assert.ok(supervisor.includes('FLOWHIVE_PSA_DISPATCH_EVIDENCE_FILE'));
  assert.ok(supervisor.includes('issues: write') && supervisor.includes('pull-requests: write'),
    'The admission token must be able to create the reservation on a pull request issue.');
  assert.doesNotMatch(supervisor, /enable|reseal/i, 'Routine admission must not toggle the canonical deployment workflow.');
  console.log('FLOWHIVE_PSA_RELEASE_CONTROL_SCOPE=PASS productionMutation=false featureMerge=false');
}
if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) validate();
