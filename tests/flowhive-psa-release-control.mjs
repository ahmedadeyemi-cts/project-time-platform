import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { verifyApproval, verifySupersededCheckBinding, controlManifest } from '../scripts/release-test/flowhive-psa-admission.mjs';

export const files = [
  '.github/flowhive-psa-protected-cutover.json',
  '.github/flowhive-psa-protected-test-candidate.json',
  '.github/flowhive-psa-stale-run-supersession-authorization.json',
  '.github/workflows/celar-ai-enterprise-api-diagnostics.yml',
  '.github/flowhive-psa-release-control-files.txt',
  '.github/workflows/flowhive-psa-protected-test-admission.yml',
  '.github/workflows/flowhive-psa-release-control-ci.yml',
  '.github/workflows/module025-governed-protected-test-release-ci.yml',
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
  'scripts/release-test/resolve-flowhive-installed-deployment.py',
  'scripts/release-test/verify-flowhive-installed-identity.py',
  '.github/workflows/flowhive-psa-installed-acceptance.yml',
  'tests/flowhive-psa-admission.test.mjs',
  'tests/flowhive-installed-resolution.test.py',
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
// The active-state activation mode is retained for the current reviewed
// candidate; the prior disabled-to-active experiment remains in Git history.
export const protectedCutoverActivationBase = 'c408e70cbf01fb63ad3716f5364476591c7e2d9d';
export const protectedCutoverActivationBranch = 'control/flowhive-my-role-cutover-activation-refresh-20260912';
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
export const installedSowRoleAcceptanceBase = '2dc8f2ff79f2ba57130072c2db95b12956e0de12';
export const installedSowRoleAcceptanceBranch = 'fix/sow-role-installed-acceptance-20260913';
export const installedSowRoleAcceptanceFiles = [
  '.github/flowhive-psa-release-control-files.txt',
  'scripts/release-test/run-module025-installed-sa-uat.py',
  'src/frontend/project-time-web/scripts/role-journeys-vite-plugin.mjs',
  'src/frontend/project-time-web/tests/role-journeys.test.mjs',
  'tests/flowhive-psa-release-control.mjs'
].sort();
export const installedVerifierMainPathBase = '1cd854d4e67d601c50677d57c8b9d2c972548a84';
export const installedVerifierMainPathBranch = 'fix/flowhive-installed-verifier-main-path-20260914';
export const installedVerifierMainPathFiles = [
  'scripts/release-test/resolve-flowhive-installed-deployment.py',
  'tests/flowhive-installed-resolution.test.py',
  'tests/flowhive-psa-installed-acceptance.test.py',
  'tests/flowhive-psa-admission.test.mjs',
  'tests/flowhive-psa-release-control.mjs'
].sort();
export const installedVerifierStepNamesBase = 'a7e3965333a4352461779b0b85ddc19ebb4bd451';
export const installedVerifierStepNamesBranch = 'fix/flowhive-installed-verifier-step-names-20260914';
export const installedVerifierStepNamesFiles = [
  'scripts/release-test/resolve-flowhive-installed-deployment.py',
  'tests/flowhive-installed-resolution.test.py',
  'tests/flowhive-psa-admission.test.mjs',
  'tests/flowhive-psa-release-control.mjs'
].sort();
export const module025MyRoleCelarRepairBase = 'c74fd53eb7a5299d6024bb0123c03ebed226fb47';
export const module025MyRoleCelarRepairBranch = 'fix/module025-my-role-celar-repair-20260914';
export const module025MyRoleCelarRepairFiles = [
  '.github/workflows/flowhive-psa-release-control-ci.yml',
  '.github/workflows/projectpulse-release-test-control-ci.yml',
  'scripts/release-test/run-module025-installed-sa-uat.py',
  'src/backend/ProjectTime.Api/Ai/PulseAiPrivateRagService.cs',
  'src/frontend/project-time-web/scripts/inject-pulse-ai-system-chat-group7-compatibility.mjs',
  'src/frontend/project-time-web/src/EnterpriseExperienceController.jsx',
  'src/frontend/project-time-web/tests/role-journeys.test.mjs',
  'tests/FlowHiveDetailedPlannerTests/Program.cs',
  'tests/flowhive-psa-admission.test.mjs',
  'tests/flowhive-psa-installed-acceptance.test.py',
  'tests/flowhive-psa-release-control.mjs'
].sort();
export const module025SowRoleCandidateRefreshBase = '62320ac18c160bfe7d25f04a998274daebfafbfc';
export const module025SowRoleCandidateRefreshBranch = 'control/module025-sow-role-candidate-refresh-20260914';
export const module025SowRoleCandidateRefreshFiles = [
  '.github/flowhive-psa-protected-cutover.json',
  '.github/flowhive-psa-protected-test-candidate.json',
  '.github/workflows/flowhive-psa-release-control-ci.yml',
  '.github/workflows/projectpulse-release-test-control-ci-reregistered.yml',
  '.github/workflows/projectpulse-release-test-control-ci.yml',
  'scripts/release-test/dispatch-flowhive-psa-test.mjs',
  'scripts/release-test/flowhive-psa-admission.mjs',
  'tests/flowhive-psa-admission.test.mjs',
  'tests/flowhive-psa-release-control.mjs'
].sort();
export const module025SowRoleCandidateRefreshFinalBase = '46097cb87db57c73d218910f9dfbe393ecd487fe';
export const module025SowRoleCandidateRefreshFinalBranch = 'control/module025-sow-role-candidate-refresh-final-20260914';
export const module025SowRoleCandidateRefreshFinalFiles = [
  '.github/flowhive-psa-protected-cutover.json',
  '.github/flowhive-psa-protected-test-candidate.json',
  '.github/workflows/flowhive-psa-release-control-ci.yml',
  '.github/workflows/projectpulse-release-test-control-ci.yml',
  'scripts/release-test/dispatch-flowhive-psa-test.mjs',
  'scripts/release-test/flowhive-psa-admission.mjs',
  'tests/flowhive-psa-admission.test.mjs',
  'tests/flowhive-psa-release-control.mjs'
].sort();
export const module025SowRoleCandidateRefresh1009Base = '4fbb7aaca14de7b84eff8fa7b7d7acb2df275c86';
export const module025SowRoleCandidateRefresh1009Branch = 'control/module025-sow-role-candidate-refresh-1009-20260914';
export const module025SowRoleCandidateRefresh1009Files = [
  '.github/flowhive-psa-protected-cutover.json',
  '.github/flowhive-psa-protected-test-candidate.json',
  '.github/workflows/flowhive-psa-release-control-ci.yml',
  '.github/workflows/projectpulse-release-test-control-ci.yml',
  'scripts/release-test/flowhive-psa-admission.mjs',
  'tests/flowhive-psa-admission.test.mjs',
  'tests/flowhive-psa-release-control.mjs'
].sort();
export const module025SowRoleCandidateRefresh1014Base = '2844d70c3891fd74effeba044876942f7a3863cf';
export const module025SowRoleCandidateRefresh1014Branch = 'control/module025-sow-role-candidate-refresh-1014-20260914';
export const module025SowRoleCandidateRefresh1014Files = [
  '.github/flowhive-psa-protected-cutover.json',
  '.github/flowhive-psa-protected-test-candidate.json',
  '.github/workflows/flowhive-psa-release-control-ci.yml',
  '.github/workflows/projectpulse-release-test-control-ci.yml',
  'scripts/release-test/dispatch-flowhive-psa-test.mjs',
  'scripts/release-test/flowhive-psa-admission.mjs',
  'tests/flowhive-psa-admission.test.mjs',
  'tests/flowhive-psa-release-control.mjs'
].sort();
export const plannerProviderDeadlineCandidateRefreshBase = 'd6c124e7641ed11ac54440113cd785e54d80ee3b';
export const plannerProviderDeadlineCandidateRefreshBranch = 'control/flowhive-planner-provider-deadline-candidate-refresh-20260914';
export const plannerProviderDeadlineCandidateRefreshFiles = [
  '.github/workflows/celar-ai-runtime-rebrand-ci.yml',
  '.github/workflows/deepseek-v4-provider-ci.yml',
  '.github/workflows/pulse-ai-private-rag-orchestration-ci.yml',
  '.github/workflows/pulse-ai-system-intelligence-ci.yml',
  'tests/flowhive-psa-release-control.mjs'
].sort();
export const plannerControlCandidateApprovalBase = 'f25f41e773b7ea1e0a991cb5589d9e24c2bb3246';
export const plannerControlCandidateApprovalBranch = 'control/flowhive-planner-control-candidate-approval-20260914';
export const plannerControlCandidateApprovalFiles = [
  '.github/flowhive-psa-protected-cutover.json',
  '.github/flowhive-psa-protected-test-candidate.json',
  'scripts/release-test/dispatch-flowhive-psa-test.mjs',
  'scripts/release-test/flowhive-psa-admission.mjs',
  'tests/flowhive-psa-admission.test.mjs',
  'tests/flowhive-psa-release-control.mjs'
].sort();
export const plannerControlCandidateBaseCorrectionBase = '94c97842509ab768dfe51b78ef0c39802f9ba0bf';
export const plannerControlCandidateBaseCorrectionBranch = 'control/flowhive-planner-control-candidate-base-correction-20260914';
export const plannerControlCandidateBaseCorrectionFiles = [
  '.github/flowhive-psa-protected-test-candidate.json',
  'scripts/release-test/flowhive-psa-admission.mjs',
  'tests/flowhive-psa-admission.test.mjs',
  'tests/flowhive-psa-release-control.mjs'
].sort();
export const plannerControlPathCoverageBase = '87d349171fd1d8dbb45e215b00c442a6527dc9a9';
export const plannerControlPathCoverageBranch = 'control/flowhive-planner-control-path-omissions-20260914';
export const plannerControlPathCoverageFiles = [
  '.github/workflows/celar-ai-enterprise-api-diagnostics.yml',
  '.github/workflows/projectpulse-release-test-control-ci.yml',
  'tests/flowhive-psa-admission.test.mjs',
  'tests/flowhive-psa-release-control.mjs'
].sort();
export const plannerCandidateApprovalRefreshBase = '345d455578668493d23302713daa945f82e97d5d';
export const plannerCandidateApprovalRefreshBranch = 'control/flowhive-planner-candidate-approval-refresh-20260915';
export const plannerCandidateApprovalRefreshFiles = [
  '.github/flowhive-psa-protected-test-candidate.json',
  '.github/workflows/projectpulse-release-test-control-ci.yml',
  'scripts/release-test/flowhive-psa-admission.mjs',
  'tests/flowhive-psa-admission.test.mjs',
  'tests/flowhive-psa-release-control.mjs'
].sort();
export const plannerAdmissionManifestRefreshBase = '7e4f943ca5e42ea9d3daa7c38bf9d7c206152edc';
export const plannerAdmissionManifestRefreshBranch = 'control/flowhive-planner-admission-manifest-refresh-20260915';
export const plannerAdmissionManifestRefreshFiles = [
  '.github/flowhive-psa-protected-test-candidate.json',
  'scripts/release-test/flowhive-psa-admission.mjs',
  'tests/flowhive-psa-admission.test.mjs',
  'tests/flowhive-psa-release-control.mjs'
].sort();
export const plannerCompactPhaseCandidateRefreshBase = 'b37398b13b222cc60acc70ac2b5cbb7ac56fecef';
export const plannerCompactPhaseCandidateRefreshBranch = 'control/flowhive-planner-compact-phase-candidate-refresh-20260915';
export const plannerCompactPhaseCandidateRefreshFiles = [
  '.github/flowhive-psa-protected-cutover.json',
  '.github/flowhive-psa-protected-test-candidate.json',
  '.github/workflows/projectpulse-release-test-control-ci.yml',
  'scripts/release-test/dispatch-flowhive-psa-test.mjs',
  'scripts/release-test/flowhive-psa-admission.mjs',
  'tests/flowhive-psa-admission.test.mjs',
  'tests/flowhive-psa-release-control.mjs'
].sort();
export const plannerCompactPhaseNativeGateBase = '63d1350db8e0ce9d152c05589f739468630b00d8';
export const plannerCompactPhaseNativeGateBranch = 'control/flowhive-planner-compact-phase-native-gate-20260915';
export const plannerCompactPhaseNativeGateFiles = [
  '.github/workflows/flowhive-psa-release-control-ci.yml',
  '.github/workflows/projectpulse-release-test-control-ci.yml',
  'scripts/release-test/dispatch-flowhive-psa-test.mjs',
  'tests/flowhive-psa-admission.test.mjs',
  'tests/flowhive-psa-release-control.mjs'
].sort();
export const triggerCoverageBase = '0b2f3a41b24e1fcb8699379d2d5ceb55093467d3';
export const triggerCoverageBranch = 'control/module025-release-trigger-coverage-20260914';
export const triggerCoverageFiles = [
  '.github/workflows/flowhive-enterprise-psa-ci.yml',
  '.github/workflows/flowhive-psa-release-control-ci.yml',
  '.github/workflows/projectpulse-release-test-control-ci.yml',
  '.github/workflows/runtime-navigation-work-register-responsive-ci.yml',
  'tests/flowhive-psa-admission.test.mjs',
  'tests/flowhive-psa-release-control.mjs',
  'tests/flowhive-psa-release-workflow.test.py'
].sort();
export const plannerProviderDeadlineRetryBase = 'fd8e41edd3313f2e18259a24e3949b48dd3ffec5';
export const plannerProviderDeadlineRetryBranch = 'fix/flowhive-planner-provider-deadline-retry-20260914';
export const plannerProviderDeadlineRetryFiles = [
  '.github/workflows/flowhive-psa-release-control-ci.yml',
  '.github/workflows/projectpulse-release-test-control-ci.yml',
  'src/backend/ProjectTime.Api/Modules/ProjectPlanningAiOrchestrator.cs',
  'tests/flowhive-psa-admission.test.mjs',
  'tests/FlowHiveDetailedPlannerTests/Program.cs',
  'tests/flowhive-psa-release-control.mjs'
].sort();
export const plannerCompactPhaseFixBase = 'aacebde419c0a325a28c5aaaf0b11f5707be2935';
export const plannerCompactPhaseFixBranch = 'fix/flowhive-planner-compact-phase-20260915';
export const plannerCompactPhaseFixFiles = [
  '.github/workflows/projectpulse-release-test-control-ci.yml',
  'src/backend/ProjectTime.Api/Ai/PulseAiPrivateRagService.cs',
  'tests/FlowHiveDetailedPlannerTests/Program.cs',
  'tests/flowhive-psa-admission.test.mjs',
  'tests/flowhive-psa-release-control.mjs'
].sort();
export const module025SowRoleAdmissionScopeBase = 'a2cbab29c3b27fe4ad1dedbf091ae6f98ba39faf';
export const module025SowRoleAdmissionScopeBranch = 'control/module025-sow-role-admission-scope-20260914';
export const module025SowRoleAdmissionScopeFiles = [
  '.github/flowhive-psa-protected-test-candidate.json',
  '.github/workflows/flowhive-psa-release-control-ci.yml',
  '.github/workflows/projectpulse-release-test-control-ci.yml',
  'scripts/release-test/flowhive-psa-admission.mjs',
  'tests/flowhive-psa-admission.test.mjs',
  'tests/flowhive-psa-release-control.mjs'
].sort();
export const module025SowRoleNativeActivationBase = 'e601037dfe8caf763da6d18532b77e14379b1196';
export const module025SowRoleNativeActivationBranch = 'control/module025-sow-role-native-activation-20260914';
export const module025SowRoleNativeActivationFiles = [
  '.github/flowhive-psa-protected-cutover.json',
  '.github/workflows/flowhive-psa-release-control-ci.yml',
  '.github/workflows/projectpulse-release-test-control-ci.yml',
  'scripts/release-test/dispatch-flowhive-psa-test.mjs',
  'tests/flowhive-psa-admission.test.mjs',
  'tests/flowhive-psa-release-control.mjs'
].sort();
export const module025SowRoleNativeActiveBase = 'cdcf6bd0b8e1e683e49cdc8f685c22e8fcf32cec';
export const module025SowRoleNativeActiveBranch = 'control/module025-sow-role-native-active-20260914';
export const module025SowRoleNativeActiveFiles = [
  '.github/flowhive-psa-protected-cutover.json',
  '.github/workflows/flowhive-psa-release-control-ci.yml',
  '.github/workflows/projectpulse-release-test-control-ci.yml',
  'scripts/release-test/dispatch-flowhive-psa-test.mjs',
  'tests/flowhive-psa-admission.test.mjs',
  'tests/flowhive-psa-release-control.mjs'
].sort();

export const module025SowRoleLiveAcceptanceBase = 'fa8631297ae2420523a2079072433c771e6f61e6';
export const module025SowRoleLiveAcceptanceBranch = 'fix/module025-sow-role-live-acceptance-20260914';
export const module025SowRoleLiveAcceptanceFiles = [
  '.github/workflows/flowhive-psa-release-control-ci.yml',
  '.github/workflows/projectpulse-release-test-control-ci.yml',
  'src/backend/ProjectTime.Api/Ai/PulseAiPrivateRagService.cs',
  'src/backend/ProjectTime.Api/DynamicRbacAdministrationModule.g.cs',
  'src/backend/ProjectTime.Api/Modules/DynamicRbacAdministrationModule.cs',
  'src/backend/ProjectTime.Api/Modules/ScopedRolePolicyPersistence.cs',
  'src/backend/ProjectTime.Api/Modules/ScopedRolePolicySupport.cs',
  'src/backend/ProjectTime.Api/ScopedRolePolicyPersistence.g.cs',
  'src/frontend/project-time-web/src/module-availability-bridge.js',
  'src/frontend/project-time-web/src/role-journeys/use-role-journey-context.js',
  'src/frontend/project-time-web/tests/role-journeys.test.mjs',
  'tests/flowhive-psa-admission.test.mjs',
  'tests/FlowHiveDetailedPlannerTests/Program.cs',
  'tests/flowhive-psa-release-control.mjs'
].sort();
export const module025SowRoleLiveRepairBase = '0799fc6d8b2d6cc55f42f244af4c100b635d74b2';
export const module025SowRoleLiveRepairBranch = 'fix/module025-sow-role-live-repair-20260914';
export const module025SowRoleLiveRepairFiles = [
  '.github/workflows/flowhive-psa-release-control-ci.yml',
  '.github/workflows/projectpulse-release-test-control-ci.yml',
  'scripts/release-test/run-flowhive-my-role-browser.py',
  'scripts/release-test/run-module025-installed-sa-uat.py',
  'src/backend/ProjectTime.Api/Ai/PulseAiPrivateRagService.cs',
  'tests/FlowHiveDetailedPlannerTests/Program.cs',
  'tests/flowhive-psa-admission.test.mjs',
  'tests/flowhive-psa-installed-acceptance.test.py',
  'tests/flowhive-psa-release-control.mjs'
].sort();

export function verifyModule025SowRoleSuccessorBinding() {
  const candidate = JSON.parse(fs.readFileSync('.github/flowhive-psa-protected-test-candidate.json', 'utf8'));
  const cutover = JSON.parse(fs.readFileSync('.github/flowhive-psa-protected-cutover.json', 'utf8'));
  verifySupersededCheckBinding(candidate.successorCheckBinding);
  assert.deepEqual(cutover.successorCheckBinding, candidate.successorCheckBinding,
    'The cutover and candidate manifests must bind the same successor checks.');
  assert.equal(candidate.successorCheckBinding.deploymentEligible, false,
    'The pre-registration binding cannot authorize deployment.');
  return candidate.successorCheckBinding;
}
export const admissionManifestOrderBase = '7542d24f17fe963b8c4a94c76b9a37331dabb704';
export const admissionManifestOrderBranch = 'control/module025-admission-manifest-order-20260914';
export const admissionManifestOrderFiles = [
  '.github/flowhive-psa-release-control-files.txt',
  '.github/workflows/flowhive-psa-release-control-ci.yml',
  '.github/workflows/module025-governed-protected-test-release-ci.yml',
  '.github/workflows/projectpulse-release-test-control-ci-reregistered.yml',
  '.github/workflows/projectpulse-release-test-control-ci.yml',
  'tests/flowhive-psa-admission.test.mjs',
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
export const plannerOutputShapeApprovalBase = 'e15634c22377476c40f25f67faffda47248c8110';
export const plannerOutputShapeApprovalBranch = 'control/flowhive-planner-output-shape-approval-20260912';
export const plannerOutputShapeApprovalFiles = [
  '.github/flowhive-psa-protected-cutover.json',
  '.github/flowhive-psa-protected-test-candidate.json',
  'scripts/release-test/dispatch-flowhive-psa-test.mjs',
  'scripts/release-test/flowhive-psa-admission.mjs',
  'tests/flowhive-psa-admission.test.mjs',
  'tests/flowhive-psa-release-control.mjs'
].sort();
export const plannerOutputShapeActivationBase = '8994920c2d71c4c72386250babca473ee817f8cb';
export const plannerOutputShapeActivationBranch = 'control/flowhive-planner-output-shape-activation-20260912';
export const plannerOutputShapeActivationFiles = [
  '.github/flowhive-psa-protected-cutover.json',
  'tests/flowhive-psa-admission.test.mjs',
  'tests/flowhive-psa-release-control.mjs'
].sort();
export const plannerOutputShapeBranchCorrectionBase = '43a4c0e7773cd6b0a5587785519616b870572e2c';
export const plannerOutputShapeBranchCorrectionBranch = 'control/flowhive-planner-output-shape-branch-correction-20260912';
export const plannerOutputShapeBranchCorrectionFiles = [
  '.github/flowhive-psa-protected-cutover.json',
  '.github/flowhive-psa-protected-test-candidate.json',
  'scripts/release-test/flowhive-psa-admission.mjs',
  'tests/flowhive-psa-admission.test.mjs',
  'tests/flowhive-psa-release-control.mjs'
].sort();
export const plannerPhaseCandidateRefreshBase = '67cc1dda877ef77a22c98e9c6e8b0bd3d6231e2a';
export const plannerPhaseCandidateRefreshBranch = 'control/flowhive-my-role-checkset-refresh-20260912';
export const plannerPhaseCandidateRefreshFiles = [
  '.github/flowhive-psa-protected-cutover.json',
  '.github/flowhive-psa-protected-test-candidate.json',
  'docs/releases/FLOWHIVE-PSA-PROTECTED-TEST-ADMISSION.md',
  'scripts/release-test/dispatch-flowhive-psa-test.mjs',
  'scripts/release-test/flowhive-psa-admission.mjs',
  'tests/flowhive-psa-admission.test.mjs',
  'tests/flowhive-psa-release-control.mjs'
].sort();
export const livePlannerCandidateRefreshBase = 'a5f3252af878f2e83efe84951516307eb0ba4bc8';
export const livePlannerCandidateRefreshBranch = 'control/flowhive-live-planner-candidate-refresh-20260912';
export const livePlannerCandidateRefreshFiles = [
  '.github/flowhive-psa-protected-cutover.json',
  '.github/flowhive-psa-protected-test-candidate.json',
  '.github/flowhive-psa-release-control-files.txt',
  '.github/workflows/module025-governed-protected-test-release-ci.yml',
  '.github/workflows/projectpulse-release-test-control-ci-reregistered.yml',
  '.github/workflows/projectpulse-release-test-control-ci.yml',
  'docs/releases/FLOWHIVE-PSA-PROTECTED-TEST-ADMISSION.md',
  'scripts/release-test/dispatch-flowhive-psa-test.mjs',
  'scripts/release-test/flowhive-psa-admission.mjs',
  'tests/flowhive-psa-admission.test.mjs',
  'tests/flowhive-psa-release-control.mjs'
].sort();
export const plannerLatencyCandidateRefreshBase = '7be13b5ff60239db6c068f8a5f373a9fb2f844ac';
export const plannerLatencyCandidateRefreshBranch = 'control/flowhive-planner-latency-candidate-refresh-20260912';
export const plannerLatencyCandidateRefreshFiles = [
  '.github/flowhive-psa-protected-cutover.json',
  '.github/flowhive-psa-protected-test-candidate.json',
  '.github/workflows/module025-governed-protected-test-release-ci.yml',
  '.github/workflows/projectpulse-release-test-control-ci-reregistered.yml',
  '.github/workflows/projectpulse-release-test-control-ci.yml',
  'docs/releases/FLOWHIVE-PSA-PROTECTED-TEST-ADMISSION.md',
  'scripts/release-test/dispatch-flowhive-psa-test.mjs',
  'scripts/release-test/flowhive-psa-admission.mjs',
  'tests/flowhive-psa-admission.test.mjs',
  'tests/flowhive-psa-release-control.mjs'
].sort();
export const plannerContextBudgetCandidateRefreshBase = '975227592535d28d309973f00dfcd1958cf19209';
export const plannerContextBudgetCandidateRefreshBranch = 'control/flowhive-planner-context-budget-candidate-refresh-20260912';
export const plannerContextBudgetCandidateRefreshFiles = [
  '.github/flowhive-psa-protected-cutover.json',
  '.github/flowhive-psa-protected-test-candidate.json',
  '.github/workflows/module025-governed-protected-test-release-ci.yml',
  '.github/workflows/projectpulse-release-test-control-ci-reregistered.yml',
  '.github/workflows/projectpulse-release-test-control-ci.yml',
  'docs/releases/FLOWHIVE-PSA-PROTECTED-TEST-ADMISSION.md',
  'scripts/release-test/dispatch-flowhive-psa-test.mjs',
  'scripts/release-test/flowhive-psa-admission.mjs',
  'tests/flowhive-psa-admission.test.mjs',
  'tests/flowhive-psa-release-control.mjs'
].sort();
export const plannerSingleBatchCandidateRefreshBase = '22d0508eac4064a0c66dd9f65b8f315f4e202d6f';
export const plannerSingleBatchCandidateRefreshBranch = 'control/flowhive-planner-single-batch-candidate-refresh-20260913';
export const plannerSingleBatchCandidateRefreshFiles = [
  '.github/flowhive-psa-protected-cutover.json',
  '.github/flowhive-psa-protected-test-candidate.json',
  '.github/workflows/module025-governed-protected-test-release-ci.yml',
  '.github/workflows/projectpulse-release-test-control-ci-reregistered.yml',
  '.github/workflows/projectpulse-release-test-control-ci.yml',
  'docs/releases/FLOWHIVE-PSA-PROTECTED-TEST-ADMISSION.md',
  'scripts/release-test/dispatch-flowhive-psa-test.mjs',
  'scripts/release-test/flowhive-psa-admission.mjs',
  'tests/flowhive-psa-admission.test.mjs',
  'tests/flowhive-psa-release-control.mjs'
].sort();
export const plannerCompactBatchCandidateRefreshBase = '50317992e55349c52bef56f26383f105152f7f5d';
export const plannerCompactBatchCandidateRefreshBranch = 'control/flowhive-planner-compact-batch-candidate-refresh-20260913';
export const plannerCompactBatchCandidateRefreshFiles = [
  '.github/flowhive-psa-protected-cutover.json',
  '.github/flowhive-psa-protected-test-candidate.json',
  '.github/workflows/module025-governed-protected-test-release-ci.yml',
  '.github/workflows/projectpulse-release-test-control-ci-reregistered.yml',
  '.github/workflows/projectpulse-release-test-control-ci.yml',
  'docs/releases/FLOWHIVE-PSA-PROTECTED-TEST-ADMISSION.md',
  'scripts/release-test/dispatch-flowhive-psa-test.mjs',
  'scripts/release-test/flowhive-psa-admission.mjs',
  'tests/flowhive-psa-admission.test.mjs',
  'tests/flowhive-psa-release-control.mjs'
].sort();
export const plannerCompactBatchActivationBase = '7185f9c76f25d362ed684e9a373dbfc654a90694';
export const plannerCompactBatchActivationBranch = 'control/flowhive-planner-compact-batch-activation-20260913';
export const plannerCompactBatchActivationFiles = [
  '.github/flowhive-psa-protected-cutover.json',
  '.github/workflows/projectpulse-release-test-control-ci-reregistered.yml',
  '.github/workflows/projectpulse-release-test-control-ci.yml',
  'scripts/release-test/dispatch-flowhive-psa-test.mjs',
  'tests/flowhive-psa-release-control.mjs'
].sort();
export const plannerCompactBatchAdmissionAncestryFixBase = '4b99e564fa3e18da1f40c6101798edaa48c71848';
export const plannerCompactBatchAdmissionAncestryFixBranch = 'control/flowhive-planner-compact-batch-admission-ancestry-fix-20260913';
export const plannerCompactBatchAdmissionAncestryFixFiles = [
  '.github/flowhive-psa-protected-cutover.json',
  '.github/workflows/projectpulse-release-test-control-ci-reregistered.yml',
  '.github/workflows/projectpulse-release-test-control-ci.yml',
  'scripts/release-test/dispatch-flowhive-psa-test.mjs',
  'scripts/release-test/flowhive-psa-admission.mjs',
  'tests/flowhive-psa-admission.test.mjs',
  'tests/flowhive-psa-release-control.mjs'
].sort();
export const plannerCompactBatchSourceDriftBoundaryBase = 'b63c02307dc716bf7e2375ee6d3efcb9b231f85f';
export const plannerCompactBatchSourceDriftBoundaryBranch = 'control/flowhive-planner-compact-batch-source-drift-boundary-20260913';
export const plannerCompactBatchSourceDriftBoundaryFiles = [
  '.github/workflows/projectpulse-release-test-control-ci-reregistered.yml',
  '.github/workflows/projectpulse-release-test-control-ci.yml',
  'scripts/release-test/flowhive-psa-admission.mjs',
  'tests/flowhive-psa-admission.test.mjs',
  'tests/flowhive-psa-release-control.mjs'
].sort();
export const plannerCompactBatchActivationRenewalBase = '78ea0f9d49cb59de914cef7f189df7a402f69d4d';
export const plannerCompactBatchActivationRenewalBranch = 'control/flowhive-planner-compact-batch-activation-renewal-20260913';
export const plannerCompactBatchActivationRenewalFiles = [
  '.github/flowhive-psa-protected-cutover.json',
  '.github/workflows/projectpulse-release-test-control-ci-reregistered.yml',
  '.github/workflows/projectpulse-release-test-control-ci.yml',
  'scripts/release-test/dispatch-flowhive-psa-test.mjs',
  'tests/flowhive-psa-release-control.mjs'
].sort();
export const plannerLiveRepairCandidateRefreshBase = '98853fa2508db9e7839e0bf478b37a7a7e9c467c';
export const plannerLiveRepairCandidateRefreshBranch = 'control/flowhive-planner-live-repair-candidate-refresh-20260913';
export const plannerLiveRepairCandidateRefreshFiles = [
  '.github/flowhive-psa-protected-cutover.json',
  '.github/flowhive-psa-protected-test-candidate.json',
  'docs/releases/FLOWHIVE-PSA-PROTECTED-TEST-ADMISSION.md',
  'scripts/release-test/dispatch-flowhive-psa-test.mjs',
  'scripts/release-test/flowhive-psa-admission.mjs',
  'tests/flowhive-psa-admission.test.mjs',
  'tests/flowhive-psa-release-control.mjs'
].sort();
export const plannerLiveRepairFinalRefreshBase = '781a9540051dd405b9d5846d5367e8e94791d9c5';
export const plannerLiveRepairFinalRefreshBranch = 'control/flowhive-planner-live-repair-final-refresh-20260913';
export const plannerLiveRepairFinalRefreshFiles = [...plannerLiveRepairCandidateRefreshFiles].sort();
export const plannerProviderContractRefreshBase = '7e4bfd58f29822368e1c019f27523c3953ae9ccc';
export const plannerProviderContractRefreshBranch = 'control/flowhive-planner-provider-contract-refresh-20260913';
export const plannerProviderContractRefreshFiles = [...plannerLiveRepairCandidateRefreshFiles].sort();
export const plannerLiveProviderOutputRefreshBase = '4ca175430d697631520e9ddb6370e8a90c6b3fa2';
export const plannerLiveProviderOutputRefreshBranch = 'control/flowhive-planner-live-provider-output-refresh-20260913';
export const plannerLiveProviderOutputRefreshFiles = [
  ...plannerProviderContractRefreshFiles,
  'scripts/release-test/prepare-protected-test-scope-manifests.sh'
].sort();
export const plannerNativeTestApprovalRenewalBase = 'b10f12df9e8b0650761936b0ece0a3f01c156a8e';
export const plannerNativeTestApprovalRenewalBranch = 'control/flowhive-planner-live-repair-renewal-safe-20260913';
export const plannerNativeTestApprovalRenewalFiles = [
  '.github/flowhive-psa-protected-cutover.json',
  '.github/workflows/projectpulse-release-test-control-ci-reregistered.yml',
  '.github/workflows/projectpulse-release-test-control-ci.yml',
  'docs/releases/FLOWHIVE-PSA-PROTECTED-TEST-ADMISSION.md',
  'scripts/release-test/dispatch-flowhive-psa-test.mjs',
  'tests/flowhive-psa-admission.test.mjs',
  'tests/flowhive-psa-release-control.mjs'
].sort();
export const plannerSingleBatchActivationBase = 'f1910bc6eab581ff0d9bb9fc4731c0e86631713c';
export const plannerSingleBatchActivationBranch = 'control/flowhive-planner-single-batch-activation-20260913';
export const plannerSingleBatchActivationFiles = [
  '.github/flowhive-psa-protected-cutover.json',
  '.github/workflows/projectpulse-release-test-control-ci-reregistered.yml',
  '.github/workflows/projectpulse-release-test-control-ci.yml',
  'scripts/release-test/dispatch-flowhive-psa-test.mjs',
  'tests/flowhive-psa-admission.test.mjs',
  'tests/flowhive-psa-release-control.mjs'
].sort();
export const plannerContextBudgetActivationBase = '20ac2bb99e1d12ca0733ada0bb78a9d57307946d';
export const plannerContextBudgetActivationBranch = 'control/flowhive-planner-context-budget-activation-20260913';
export const plannerContextBudgetActivationFiles = [
  '.github/flowhive-psa-protected-cutover.json',
  '.github/workflows/projectpulse-release-test-control-ci-reregistered.yml',
  '.github/workflows/projectpulse-release-test-control-ci.yml',
  'scripts/release-test/dispatch-flowhive-psa-test.mjs',
  'tests/flowhive-psa-admission.test.mjs',
  'tests/flowhive-psa-release-control.mjs'
].sort();
export const plannerLatencyActivationBase = '303c860b8392a549d5e224e81ca5adc56878ce8d';
export const plannerLatencyActivationBranch = 'control/flowhive-planner-latency-activation-20260912';
export const plannerLatencyActivationFiles = [
  '.github/flowhive-psa-protected-cutover.json',
  '.github/workflows/projectpulse-release-test-control-ci-reregistered.yml',
  '.github/workflows/projectpulse-release-test-control-ci.yml',
  'scripts/release-test/dispatch-flowhive-psa-test.mjs',
  'tests/flowhive-psa-admission.test.mjs',
  'tests/flowhive-psa-release-control.mjs'
].sort();
export const livePlannerActivationBase = 'abdb76a4cb5a49b8e2436673e006b159f3c2089d';
export const livePlannerActivationBranch = 'control/flowhive-live-planner-activation-20260912';
export const livePlannerActivationFiles = [
  '.github/flowhive-psa-protected-cutover.json',
  '.github/workflows/projectpulse-release-test-control-ci-reregistered.yml',
  '.github/workflows/projectpulse-release-test-control-ci.yml',
  'scripts/release-test/dispatch-flowhive-psa-test.mjs',
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
export const plannerProviderBudgetActivationBase = '549ace1b3286a1ae50e5e28641e38f365b9dd5cc';
export const plannerProviderBudgetActivationBranch = 'control/flowhive-planner-provider-budget-activation-20260912';
export const plannerProviderBudgetActivationFiles = [
  '.github/flowhive-psa-protected-cutover.json',
  'scripts/release-test/dispatch-flowhive-psa-test.mjs',
  'tests/flowhive-psa-admission.test.mjs',
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
export const plannerOutputBudgetCandidateRefreshBase = 'ed75fc0223c5967296d932fa7f46ac6790081020';
export const plannerOutputBudgetCandidateRefreshBranch = 'control/flowhive-planner-output-budget-candidate-refresh-20260912';
export const plannerOutputBudgetCandidateRefreshFiles = [
  '.github/flowhive-psa-protected-cutover.json',
  '.github/flowhive-psa-protected-test-candidate.json',
  '.github/workflows/module025-governed-protected-test-release-ci.yml',
  '.github/workflows/projectpulse-release-test-control-ci-reregistered.yml',
  '.github/workflows/projectpulse-release-test-control-ci.yml',
  'docs/releases/FLOWHIVE-PSA-PROTECTED-TEST-ADMISSION.md',
  'scripts/release-test/dispatch-flowhive-psa-test.mjs',
  'scripts/release-test/flowhive-psa-admission.mjs',
  'tests/flowhive-psa-admission.test.mjs',
  'tests/flowhive-psa-release-control.mjs'
].sort();
export const plannerOutputBudgetFinalActivationBase = 'af66cfa792865a130960f618e3658b082aff2176';
export const plannerOutputBudgetFinalActivationBranch = 'control/flowhive-planner-output-budget-final-activation-20260912';
export const plannerOutputBudgetFinalActivationFiles = [
  '.github/flowhive-psa-protected-cutover.json',
  '.github/workflows/projectpulse-release-test-control-ci-reregistered.yml',
  '.github/workflows/projectpulse-release-test-control-ci.yml',
  'scripts/release-test/dispatch-flowhive-psa-test.mjs',
  'tests/flowhive-psa-admission.test.mjs',
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
  assert.ok(['initial','pr874-digest-repair','reviewed-regeneration-105','candidate-refresh','successor-candidate-refresh','source-base-correction','successor-approval','successor-release-approval','successor-cutover-activation','planner-output-budget-approval','planner-output-shape-approval','planner-output-shape-activation','planner-output-shape-branch-correction','planner-phase-candidate-refresh','live-planner-candidate-refresh','planner-latency-candidate-refresh','planner-context-budget-candidate-refresh','planner-single-batch-candidate-refresh','planner-compact-batch-candidate-refresh','planner-compact-phase-native-gate','planner-compact-batch-activation','planner-compact-batch-admission-ancestry-fix','planner-compact-batch-source-drift-boundary','planner-compact-batch-activation-renewal','planner-live-repair-candidate-refresh','planner-live-repair-final-refresh','planner-provider-contract-refresh','planner-live-provider-output-refresh','planner-native-test-approval-renewal','planner-single-batch-activation','planner-context-budget-activation','planner-latency-activation','live-planner-activation','planner-time-budget-approval','planner-provider-budget-successor-approval','planner-provider-budget-activation','planner-time-budget-workflow-set','planner-time-budget-activation','planner-output-budget-activation','planner-output-budget-activation-refresh','planner-output-budget-candidate-refresh','planner-output-budget-final-activation','planner-provider-deadline-retry','planner-provider-deadline-candidate-refresh','planner-compact-phase-fix','planner-compact-phase-candidate-refresh','planner-control-candidate-approval','planner-control-candidate-base-correction','planner-control-path-coverage','planner-candidate-approval-refresh','planner-admission-manifest-refresh','dispatch-run-recovery','dispatch-lane-repair','dispatch-skipped-recovery','dispatch-receipt-parser','dispatch-lane-admission','stale-run-supersession','stale-run-activation','stale-run-renewal','migration106-wiring','release-stabilization','protected-cutover','protected-cutover-activation','protected-cutover-refresh','protected-cutover-lane-refresh','protected-cutover-pm-identity-refresh','admission-permission-fix','admission-bot-claim-fix','protected-cutover-recovery','reservation-recovery-endpoint-fix','protected-cutover-final','live-uat-status-fix','installed-verification-environment','installed-verification-evidence','installed-acceptance-evidence','installed-acceptance-artifacts','installed-acceptance-readiness','installed-acceptance-visibility','installed-acceptance-planner','installed-acceptance-final','installed-sow-role-acceptance','installed-verifier-main-path','installed-verifier-step-names','module025-my-role-celar-repair','module025-sow-role-candidate-refresh','module025-sow-role-candidate-refresh-final','module025-sow-role-candidate-refresh-1009','module025-sow-role-candidate-refresh-1014','module025-sow-role-admission-scope','module025-sow-role-native-activation','module025-sow-role-native-active','module025-sow-role-live-acceptance','module025-sow-role-live-repair','trigger-coverage','admission-manifest-order'].includes(mode), 'Unrecognized control repair.');
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
  if (mode === 'planner-output-shape-approval') {
    assert.equal(context?.base, plannerOutputShapeApprovalBase, 'Planner output-shape approval must be based on the merged application main.');
    assert.equal(context?.branch, plannerOutputShapeApprovalBranch, 'Wrong planner output-shape approval branch.');
  }
  if (mode === 'planner-output-shape-activation') {
    assert.equal(context?.base, plannerOutputShapeActivationBase, 'Planner output-shape activation must be based on the merged control approval main.');
    assert.equal(context?.branch, plannerOutputShapeActivationBranch, 'Wrong planner output-shape activation branch.');
  }
  if (mode === 'planner-output-shape-branch-correction') {
    assert.equal(context?.base, plannerOutputShapeBranchCorrectionBase, 'Planner output-shape branch correction must be based on the exact failed activation main.');
    assert.equal(context?.branch, plannerOutputShapeBranchCorrectionBranch, 'Wrong planner output-shape branch correction branch.');
  }
  if (mode === 'planner-phase-candidate-refresh') {
    assert.equal(context?.base, plannerPhaseCandidateRefreshBase, 'Planner phase candidate refresh must be based on the merged application main.');
    assert.equal(context?.branch, plannerPhaseCandidateRefreshBranch, 'Wrong planner phase candidate refresh branch.');
  }
  if (mode === 'live-planner-candidate-refresh') {
    assert.equal(context?.base, livePlannerCandidateRefreshBase, 'Live planner candidate refresh must be based on the exact merged application main.');
    assert.equal(context?.branch, livePlannerCandidateRefreshBranch, 'Wrong live planner candidate refresh branch.');
  }
  if (mode === 'planner-latency-candidate-refresh') {
    assert.equal(context?.base, plannerLatencyCandidateRefreshBase, 'Planner latency candidate refresh must be based on the exact merged application main.');
    assert.equal(context?.branch, plannerLatencyCandidateRefreshBranch, 'Wrong planner latency candidate refresh branch.');
  }
  if (mode === 'planner-context-budget-candidate-refresh') {
    assert.equal(context?.base, plannerContextBudgetCandidateRefreshBase, 'Planner context-budget candidate refresh must be based on the merged application main.');
    assert.equal(context?.branch, plannerContextBudgetCandidateRefreshBranch, 'Wrong planner context-budget candidate refresh branch.');
  }
  if (mode === 'planner-single-batch-candidate-refresh') {
    assert.equal(context?.base, plannerSingleBatchCandidateRefreshBase, 'Planner single-batch candidate refresh must be based on the merged application main.');
    assert.equal(context?.branch, plannerSingleBatchCandidateRefreshBranch, 'Wrong planner single-batch candidate refresh branch.');
  }
  if (mode === 'planner-compact-batch-candidate-refresh') {
    assert.equal(context?.base, plannerCompactBatchCandidateRefreshBase, 'Planner compact-batch candidate refresh must be based on the merged application main.');
    assert.equal(context?.branch, plannerCompactBatchCandidateRefreshBranch, 'Wrong planner compact-batch candidate refresh branch.');
  }
  if (mode === 'planner-compact-batch-activation') {
    assert.equal(context?.base, plannerCompactBatchActivationBase, 'Planner compact-batch activation must be based on the merged candidate refresh main.');
    assert.equal(context?.branch, plannerCompactBatchActivationBranch, 'Wrong planner compact-batch activation branch.');
  }
  if (mode === 'planner-compact-batch-admission-ancestry-fix') {
    assert.equal(context?.base, plannerCompactBatchAdmissionAncestryFixBase, 'Planner compact-batch admission fix must be based on the merged activation control main.');
    assert.equal(context?.branch, plannerCompactBatchAdmissionAncestryFixBranch, 'Wrong planner compact-batch admission fix branch.');
  }
  if (mode === 'planner-compact-batch-source-drift-boundary') {
    assert.equal(context?.base, plannerCompactBatchSourceDriftBoundaryBase, 'Planner compact-batch source-drift fix must be based on current trusted main.');
    assert.equal(context?.branch, plannerCompactBatchSourceDriftBoundaryBranch, 'Wrong planner compact-batch source-drift fix branch.');
  }
  if (mode === 'planner-compact-batch-activation-renewal') {
    assert.equal(context?.base, plannerCompactBatchActivationRenewalBase, 'Planner compact-batch activation renewal must be based on the latest trusted main.');
    assert.equal(context?.branch, plannerCompactBatchActivationRenewalBranch, 'Wrong planner compact-batch activation renewal branch.');
  }
  if (mode === 'planner-live-repair-candidate-refresh') {
    assert.equal(context?.base, plannerLiveRepairCandidateRefreshBase, 'Planner live-repair candidate refresh must be based on the merged application main.');
    assert.equal(context?.branch, plannerLiveRepairCandidateRefreshBranch, 'Wrong planner live-repair candidate refresh branch.');
  }
  if (mode === 'planner-live-repair-final-refresh') {
    assert.equal(context?.base, plannerLiveRepairFinalRefreshBase, 'Planner live-repair final refresh must be based on current trusted main.');
    assert.equal(context?.branch, plannerLiveRepairFinalRefreshBranch, 'Wrong planner live-repair final refresh branch.');
  }
  if (mode === 'planner-provider-contract-refresh') {
    assert.equal(context?.base, plannerProviderContractRefreshBase, 'Planner provider-contract refresh must be based on the merged application main.');
    assert.equal(context?.branch, plannerProviderContractRefreshBranch, 'Wrong planner provider-contract refresh branch.');
  }
  if (mode === 'planner-live-provider-output-refresh') {
    assert.equal(context?.base, plannerLiveProviderOutputRefreshBase, 'Planner live provider-output refresh must be based on the merged application main.');
    assert.equal(context?.branch, plannerLiveProviderOutputRefreshBranch, 'Wrong planner live provider-output refresh branch.');
  }
  if (mode === 'planner-native-test-approval-renewal') {
    assert.equal(context?.base, plannerNativeTestApprovalRenewalBase, 'Native Test approval renewal must be based on the latest trusted main.');
    assert.equal(context?.branch, plannerNativeTestApprovalRenewalBranch, 'Wrong native Test approval renewal branch.');
  }
  if (mode === 'planner-single-batch-activation') {
    assert.equal(context?.base, plannerSingleBatchActivationBase, 'Planner single-batch activation must be based on the merged candidate refresh main.');
    assert.equal(context?.branch, plannerSingleBatchActivationBranch, 'Wrong planner single-batch activation branch.');
  }
  if (mode === 'planner-context-budget-activation') {
    assert.equal(context?.base, plannerContextBudgetActivationBase, 'Planner context-budget activation must be based on the merged candidate refresh main.');
    assert.equal(context?.branch, plannerContextBudgetActivationBranch, 'Wrong planner context-budget activation branch.');
  }
  if (mode === 'planner-latency-activation') {
    assert.equal(context?.base, plannerLatencyActivationBase, 'Planner latency activation must be based on the merged candidate refresh main.');
    assert.equal(context?.branch, plannerLatencyActivationBranch, 'Wrong planner latency activation branch.');
  }
  if (mode === 'live-planner-activation') {
    assert.equal(context?.base, livePlannerActivationBase, 'Live planner activation must be based on the refreshed trusted main.');
    assert.equal(context?.branch, livePlannerActivationBranch, 'Wrong live planner activation branch.');
  }
  if (mode === 'planner-time-budget-approval') {
    assert.equal(context?.base, plannerTimeBudgetApprovalBase, 'Planner time-budget approval must be based on the exact trusted main containing PR933.');
    assert.equal(context?.branch, plannerTimeBudgetApprovalBranch, 'Wrong planner time-budget approval branch.');
  }
  if (mode === 'planner-provider-budget-successor-approval') {
    assert.equal(context?.base, plannerProviderBudgetSuccessorApprovalBase, 'Planner provider-budget successor approval must be based on the merged application main.');
    assert.equal(context?.branch, plannerProviderBudgetSuccessorApprovalBranch, 'Wrong planner provider-budget successor approval branch.');
  }
  if (mode === 'planner-provider-budget-activation') {
    assert.equal(context?.base, plannerProviderBudgetActivationBase, 'Planner provider-budget activation must be based on the exact approved trusted main.');
    assert.equal(context?.branch, plannerProviderBudgetActivationBranch, 'Wrong planner provider-budget activation branch.');
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
  if (mode === 'planner-output-budget-candidate-refresh') {
    assert.equal(context?.base, plannerOutputBudgetCandidateRefreshBase, 'Planner output-budget candidate refresh must be based on the merged application main.');
    assert.equal(context?.branch, plannerOutputBudgetCandidateRefreshBranch, 'Wrong planner output-budget candidate refresh branch.');
  }
  if (mode === 'planner-output-budget-final-activation') {
    assert.equal(context?.base, plannerOutputBudgetFinalActivationBase, 'Planner output-budget activation must be based on the merged candidate approval.');
    assert.equal(context?.branch, plannerOutputBudgetFinalActivationBranch, 'Wrong planner output-budget final activation branch.');
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
  if (mode === 'installed-sow-role-acceptance') {
    assert.equal(context?.base, installedSowRoleAcceptanceBase, 'Installed SOW and role acceptance correction must be based on current trusted main.');
    assert.equal(context?.branch, installedSowRoleAcceptanceBranch, 'Wrong installed SOW and role acceptance correction branch.');
    const module025 = fs.readFileSync('scripts/release-test/run-module025-installed-sa-uat.py', 'utf8');
    assert.match(module025, /def phase_skeleton\(phases: object\)/, 'SOW acceptance must validate phase shape before generation.');
    assert.match(module025, /phase_skeleton\(phases\)/, 'SOW acceptance must use the pre-generation phase-shape check.');
    assert.match(module025, /counts = phase_quality\(current\.get\("phases"\)\)/, 'Generated SOW quality must be evaluated after generation.');
    assert.equal((module025.match(/phase_quality\(/g) || []).length, 2, 'SOW quality must not be required before generation.');
    assert.match(module025, /report\["generationPosts"\] = 1/, 'SOW acceptance must prove one real generation request.');
  }
  if (mode === 'installed-verifier-main-path') {
    assert.equal(context?.base, installedVerifierMainPathBase, 'Installed verifier main-path correction must be based on current trusted main.');
    assert.equal(context?.branch, installedVerifierMainPathBranch, 'Wrong installed verifier main-path correction branch.');
  }
  if (mode === 'installed-verifier-step-names') {
    assert.equal(context?.base, installedVerifierStepNamesBase, 'Installed verifier step-name correction must be based on current trusted main.');
    assert.equal(context?.branch, installedVerifierStepNamesBranch, 'Wrong installed verifier step-name correction branch.');
  }
  if (mode === 'module025-my-role-celar-repair') {
    assert.equal(context?.base, module025MyRoleCelarRepairBase, 'Module 025/My Role/Celar repair must be based on the current trusted main.');
    assert.equal(context?.branch, module025MyRoleCelarRepairBranch, 'Wrong Module 025/My Role/Celar repair branch.');
  }
  if (mode === 'module025-sow-role-candidate-refresh') {
    assert.equal(context?.base, module025SowRoleCandidateRefreshBase, 'SOW/My Role candidate refresh must be based on merged PR994 main.');
    assert.equal(context?.branch, module025SowRoleCandidateRefreshBranch, 'Wrong SOW/My Role candidate refresh branch.');
    verifyModule025SowRoleSuccessorBinding();
  }
  if (mode === 'module025-sow-role-candidate-refresh-final') {
    assert.equal(context?.base, module025SowRoleCandidateRefreshFinalBase, 'Final SOW/My Role candidate refresh must be based on merged PR1003 main.');
    assert.equal(context?.branch, module025SowRoleCandidateRefreshFinalBranch, 'Wrong final SOW/My Role candidate refresh branch.');
    verifyModule025SowRoleSuccessorBinding();
  }
  if (mode === 'module025-sow-role-candidate-refresh-1009') {
    assert.equal(context?.base, module025SowRoleCandidateRefresh1009Base, 'PR1009 SOW/My Role candidate refresh must be based on the merged application main.');
    assert.equal(context?.branch, module025SowRoleCandidateRefresh1009Branch, 'Wrong PR1009 SOW/My Role candidate refresh branch.');
    verifyModule025SowRoleSuccessorBinding();
  }
  if (mode === 'module025-sow-role-candidate-refresh-1014') {
    assert.equal(context?.base, module025SowRoleCandidateRefresh1014Base, 'PR1014 SOW/My Role candidate refresh must be based on the merged application main.');
    assert.equal(context?.branch, module025SowRoleCandidateRefresh1014Branch, 'Wrong PR1014 SOW/My Role candidate refresh branch.');
    verifyModule025SowRoleSuccessorBinding();
  }
  if (mode === 'planner-provider-deadline-candidate-refresh') {
    assert.equal(context?.base, plannerProviderDeadlineCandidateRefreshBase, 'Planner provider deadline candidate refresh must be based on current trusted main.');
    assert.equal(context?.branch, plannerProviderDeadlineCandidateRefreshBranch, 'Wrong planner provider deadline candidate refresh branch.');
    verifyModule025SowRoleSuccessorBinding();
  }
  if (mode === 'planner-control-candidate-approval') {
    assert.equal(context?.base, plannerControlCandidateApprovalBase, 'Planner control candidate approval must be based on current trusted main.');
    assert.equal(context?.branch, plannerControlCandidateApprovalBranch, 'Wrong planner control candidate approval branch.');
    verifyModule025SowRoleSuccessorBinding();
  }
  if (mode === 'planner-control-candidate-base-correction') {
    assert.equal(context?.base, plannerControlCandidateBaseCorrectionBase, 'Planner control candidate base correction must be based on merged PR1021 main.');
    assert.equal(context?.branch, plannerControlCandidateBaseCorrectionBranch, 'Wrong planner control candidate base correction branch.');
    verifyModule025SowRoleSuccessorBinding();
  }
  if (mode === 'planner-control-path-coverage') {
    assert.equal(context?.base, plannerControlPathCoverageBase, 'Planner control path coverage must be based on merged PR1022 main.');
    assert.equal(context?.branch, plannerControlPathCoverageBranch, 'Wrong planner control path coverage branch.');
  }
  if (mode === 'planner-candidate-approval-refresh') {
    assert.equal(context?.base, plannerCandidateApprovalRefreshBase, 'Planner candidate approval refresh must be based on merged PR1023 main.');
    assert.equal(context?.branch, plannerCandidateApprovalRefreshBranch, 'Wrong planner candidate approval refresh branch.');
  }
  if (mode === 'planner-admission-manifest-refresh') {
    assert.equal(context?.base, plannerAdmissionManifestRefreshBase, 'Planner admission manifest refresh must be based on merged PR1024 main.');
    assert.equal(context?.branch, plannerAdmissionManifestRefreshBranch, 'Wrong planner admission manifest refresh branch.');
  }
  if (mode === 'planner-compact-phase-candidate-refresh') {
    assert.equal(context?.base, plannerCompactPhaseCandidateRefreshBase, 'Planner compact-phase candidate refresh must be based on merged PR1029 main.');
    assert.equal(context?.branch, plannerCompactPhaseCandidateRefreshBranch, 'Wrong planner compact-phase candidate refresh branch.');
  }
  if (mode === 'planner-compact-phase-native-gate') {
    assert.equal(context?.base, plannerCompactPhaseNativeGateBase, 'Planner compact-phase native gate must be based on current trusted main.');
    assert.equal(context?.branch, plannerCompactPhaseNativeGateBranch, 'Wrong planner compact-phase native gate branch.');
  }
  if (mode === 'module025-sow-role-admission-scope') {
    assert.equal(context?.base, module025SowRoleAdmissionScopeBase, 'Admission scope correction must be based on merged PR1005 main.');
    assert.equal(context?.branch, module025SowRoleAdmissionScopeBranch, 'Wrong admission scope correction branch.');
  }
  if (mode === 'module025-sow-role-native-activation') {
    assert.equal(context?.base, module025SowRoleNativeActivationBase, 'Native activation must be based on merged PR1006 main.');
    assert.equal(context?.branch, module025SowRoleNativeActivationBranch, 'Wrong native activation branch.');
  }
  if (mode === 'module025-sow-role-native-active') {
    assert.equal(context?.base, module025SowRoleNativeActiveBase, 'Active native cutover must be based on the merged PR1007 main.');
    assert.equal(context?.branch, module025SowRoleNativeActiveBranch, 'Wrong active native cutover branch.');
  }
  if (mode === 'module025-sow-role-live-acceptance') {
    assert.equal(context?.base, module025SowRoleLiveAcceptanceBase, 'SOW/My Role live acceptance must be based on trusted main.');
    assert.equal(context?.branch, module025SowRoleLiveAcceptanceBranch, 'Wrong SOW/My Role live acceptance branch.');
  }
  if (mode === 'module025-sow-role-live-repair') {
    assert.equal(context?.base, module025SowRoleLiveRepairBase, 'SOW/My Role live repair must be based on current trusted main.');
    assert.equal(context?.branch, module025SowRoleLiveRepairBranch, 'Wrong SOW/My Role live repair branch.');
  }
  if (mode === 'admission-manifest-order') {
    assert.equal(context?.base, admissionManifestOrderBase, 'Admission manifest correction must be based on the merged PR1001 main.');
    assert.equal(context?.branch, admissionManifestOrderBranch, 'Wrong admission manifest correction branch.');
  }
  if (mode === 'module025-sow-role-candidate-refresh') {
    assert.equal(context?.base, module025SowRoleCandidateRefreshBase, 'SOW/My Role candidate refresh must be based on merged PR994 main.');
    assert.equal(context?.branch, module025SowRoleCandidateRefreshBranch, 'Wrong SOW/My Role candidate refresh branch.');
  }
  if (mode === 'module025-sow-role-candidate-refresh-1009') {
    assert.equal(context?.base, module025SowRoleCandidateRefresh1009Base, 'PR1009 SOW/My Role candidate refresh must be based on the merged application main.');
    assert.equal(context?.branch, module025SowRoleCandidateRefresh1009Branch, 'Wrong PR1009 SOW/My Role candidate refresh branch.');
  }
  if (mode === 'module025-sow-role-candidate-refresh-1014') {
    assert.equal(context?.base, module025SowRoleCandidateRefresh1014Base, 'PR1014 SOW/My Role candidate refresh must be based on the merged application main.');
    assert.equal(context?.branch, module025SowRoleCandidateRefresh1014Branch, 'Wrong PR1014 SOW/My Role candidate refresh branch.');
  }
  if (mode === 'trigger-coverage') {
    assert.equal(context?.base, triggerCoverageBase, 'Trigger coverage correction must be based on current trusted main.');
    assert.equal(context?.branch, triggerCoverageBranch, 'Wrong trigger coverage correction branch.');
  }
  if (mode === 'planner-provider-deadline-retry') {
    assert.equal(context?.base, plannerProviderDeadlineRetryBase, 'Provider deadline retry must be based on current trusted main.');
    assert.equal(context?.branch, plannerProviderDeadlineRetryBranch, 'Wrong provider deadline retry branch.');
  }
  if (mode === 'planner-compact-phase-fix') {
    assert.equal(context?.base, plannerCompactPhaseFixBase, 'Planner compact phase fix must be based on current trusted main.');
    assert.equal(context?.branch, plannerCompactPhaseFixBranch, 'Wrong planner compact phase fix branch.');
  }
  if (mode === 'planner-compact-phase-fix') {
    assert.equal(context?.base, plannerCompactPhaseFixBase, 'Planner compact phase fix must be based on current trusted main.');
    assert.equal(context?.branch, plannerCompactPhaseFixBranch, 'Wrong planner compact phase fix branch.');
  }
  const expected = mode === 'initial' ? files : mode === 'pr874-digest-repair' ? repairFiles : mode === 'reviewed-regeneration-105' ? reviewedRegenerationFiles : mode === 'candidate-refresh' ? candidateRefreshFiles : mode === 'successor-candidate-refresh' ? successorCandidateRefreshFiles : mode === 'source-base-correction' ? sourceBaseCorrectionFiles : mode === 'successor-approval' ? successorApprovalFiles : mode === 'successor-release-approval' ? successorReleaseApprovalFiles : mode === 'successor-cutover-activation' ? successorCutoverActivationFiles : mode === 'planner-output-budget-approval' ? plannerOutputBudgetApprovalFiles : mode === 'planner-output-shape-approval' ? plannerOutputShapeApprovalFiles : mode === 'planner-output-shape-activation' ? plannerOutputShapeActivationFiles : mode === 'planner-output-shape-branch-correction' ? plannerOutputShapeBranchCorrectionFiles : mode === 'planner-phase-candidate-refresh' ? plannerPhaseCandidateRefreshFiles : mode === 'live-planner-candidate-refresh' ? livePlannerCandidateRefreshFiles : mode === 'planner-latency-candidate-refresh' ? plannerLatencyCandidateRefreshFiles : mode === 'planner-latency-activation' ? plannerLatencyActivationFiles : mode === 'live-planner-activation' ? livePlannerActivationFiles : mode === 'planner-time-budget-approval' ? plannerTimeBudgetApprovalFiles : mode === 'planner-provider-budget-successor-approval' ? plannerProviderBudgetSuccessorApprovalFiles : mode === 'planner-provider-budget-activation' ? plannerProviderBudgetActivationFiles : mode === 'planner-time-budget-workflow-set' ? plannerTimeBudgetWorkflowSetFiles : mode === 'planner-time-budget-activation' ? plannerTimeBudgetActivationFiles : mode === 'planner-output-budget-activation' ? plannerOutputBudgetActivationFiles : mode === 'planner-output-budget-activation-refresh' ? plannerOutputBudgetActivationRefreshFiles : mode === 'dispatch-run-recovery' ? dispatchRecoveryFiles : mode === 'dispatch-lane-repair' ? dispatchLaneRepairFiles : mode === 'dispatch-skipped-recovery' ? dispatchSkippedRecoveryFiles : mode === 'dispatch-receipt-parser' ? dispatchReceiptParserFiles : mode === 'dispatch-lane-admission' ? dispatchLaneAdmissionFiles : mode === 'stale-run-activation' ? staleSupersessionActivationFiles : mode === 'stale-run-renewal' ? staleSupersessionRenewalFiles : mode === 'migration106-wiring' ? migration106WiringFiles : mode === 'release-stabilization' ? releaseStabilizationFiles : mode === 'protected-cutover' ? protectedCutoverFiles : mode === 'protected-cutover-activation' ? protectedCutoverActivationFiles : mode === 'protected-cutover-refresh' ? protectedCutoverRefreshFiles : mode === 'admission-permission-fix' ? admissionPermissionFixFiles : mode === 'admission-bot-claim-fix' ? admissionBotClaimFixFiles : mode === 'protected-cutover-recovery' ? protectedCutoverRecoveryFiles : mode === 'reservation-recovery-endpoint-fix' ? reservationRecoveryEndpointFixFiles : mode === 'protected-cutover-final' ? protectedCutoverFinalFiles : mode === 'live-uat-status-fix' ? liveUatStatusFiles : mode === 'installed-verification-environment' ? installedVerificationEnvironmentFiles : mode === 'installed-verification-evidence' ? installedVerificationEvidenceFiles : mode === 'installed-acceptance-evidence' ? installedAcceptanceEvidenceFiles : mode === 'installed-acceptance-artifacts' ? installedAcceptanceArtifactsFiles : mode === 'installed-acceptance-readiness' ? installedAcceptanceReadinessFiles : mode === 'installed-acceptance-visibility' ? installedAcceptanceVisibilityFiles : mode === 'installed-acceptance-planner' ? installedAcceptancePlannerFiles : mode === 'installed-acceptance-final' ? installedAcceptanceFinalFiles : mode === 'installed-sow-role-acceptance' ? installedSowRoleAcceptanceFiles : mode === 'module025-my-role-celar-repair' ? module025MyRoleCelarRepairFiles : staleSupersessionFiles;
  const scopedExpected = mode === 'planner-compact-batch-activation-renewal' ? plannerCompactBatchActivationRenewalFiles : mode === 'planner-output-budget-final-activation' ? plannerOutputBudgetFinalActivationFiles : mode === 'planner-output-budget-candidate-refresh' ? plannerOutputBudgetCandidateRefreshFiles : mode === 'protected-cutover-lane-refresh' ? protectedCutoverLaneRefreshFiles : mode === 'protected-cutover-pm-identity-refresh' ? protectedCutoverPmIdentityRefreshFiles : mode === 'planner-context-budget-activation' ? plannerContextBudgetActivationFiles : mode === 'planner-context-budget-candidate-refresh' ? plannerContextBudgetCandidateRefreshFiles : mode === 'planner-single-batch-activation' ? plannerSingleBatchActivationFiles : mode === 'planner-single-batch-candidate-refresh' ? plannerSingleBatchCandidateRefreshFiles : mode === 'planner-compact-batch-activation' ? plannerCompactBatchActivationFiles : mode === 'planner-compact-batch-admission-ancestry-fix' ? plannerCompactBatchAdmissionAncestryFixFiles : mode === 'planner-compact-batch-source-drift-boundary' ? plannerCompactBatchSourceDriftBoundaryFiles : mode === 'planner-compact-batch-candidate-refresh' ? plannerCompactBatchCandidateRefreshFiles : expected;
  const effectiveScopedExpected = mode === 'trigger-coverage' ? triggerCoverageFiles : mode === 'planner-provider-deadline-retry' ? plannerProviderDeadlineRetryFiles : mode === 'planner-compact-phase-fix' ? plannerCompactPhaseFixFiles : mode === 'planner-provider-deadline-candidate-refresh' ? plannerProviderDeadlineCandidateRefreshFiles : mode === 'planner-control-candidate-approval' ? plannerControlCandidateApprovalFiles : mode === 'planner-control-path-coverage' ? plannerControlPathCoverageFiles : mode === 'planner-candidate-approval-refresh' ? plannerCandidateApprovalRefreshFiles : mode === 'planner-admission-manifest-refresh' ? plannerAdmissionManifestRefreshFiles : mode === 'planner-compact-phase-candidate-refresh' ? plannerCompactPhaseCandidateRefreshFiles : mode === 'planner-compact-phase-native-gate' ? plannerCompactPhaseNativeGateFiles : mode === 'planner-control-candidate-base-correction' ? plannerControlCandidateBaseCorrectionFiles : mode === 'planner-native-test-approval-renewal' ? plannerNativeTestApprovalRenewalFiles : mode === 'planner-live-repair-candidate-refresh' ? plannerLiveRepairCandidateRefreshFiles : mode === 'planner-live-repair-final-refresh' ? plannerLiveRepairFinalRefreshFiles : mode === 'planner-provider-contract-refresh' ? plannerProviderContractRefreshFiles : mode === 'planner-live-provider-output-refresh' ? plannerLiveProviderOutputRefreshFiles : mode === 'installed-verifier-main-path' ? installedVerifierMainPathFiles : mode === 'installed-verifier-step-names' ? installedVerifierStepNamesFiles : mode === 'module025-sow-role-candidate-refresh' ? module025SowRoleCandidateRefreshFiles : mode === 'module025-sow-role-candidate-refresh-final' ? module025SowRoleCandidateRefreshFinalFiles : mode === 'module025-sow-role-candidate-refresh-1009' ? module025SowRoleCandidateRefresh1009Files : mode === 'module025-sow-role-candidate-refresh-1014' ? module025SowRoleCandidateRefresh1014Files : mode === 'module025-sow-role-admission-scope' ? module025SowRoleAdmissionScopeFiles : mode === 'module025-sow-role-native-activation' ? module025SowRoleNativeActivationFiles : mode === 'module025-sow-role-native-active' ? module025SowRoleNativeActiveFiles : mode === 'module025-sow-role-live-acceptance' ? module025SowRoleLiveAcceptanceFiles : mode === 'module025-sow-role-live-repair' ? module025SowRoleLiveRepairFiles : mode === 'admission-manifest-order' ? admissionManifestOrderFiles : scopedExpected;
  assert.deepEqual([...changed].sort(), effectiveScopedExpected, 'Unexpected or missing file in the release-control PR.');
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
  const manifest = fs.readFileSync(controlManifest, 'utf8').trim().split(/\r?\n/).sort();
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
  const isPlannerOutputShapeApproval = process.env.GITHUB_HEAD_REF === plannerOutputShapeApprovalBranch;
  const isPlannerOutputShapeActivation = process.env.GITHUB_HEAD_REF === plannerOutputShapeActivationBranch;
  const isPlannerOutputShapeBranchCorrection = process.env.GITHUB_HEAD_REF === plannerOutputShapeBranchCorrectionBranch;
  const isPlannerPhaseCandidateRefresh = process.env.GITHUB_HEAD_REF === plannerPhaseCandidateRefreshBranch;
  const isLivePlannerCandidateRefresh = process.env.GITHUB_HEAD_REF === livePlannerCandidateRefreshBranch;
  const isPlannerLatencyCandidateRefresh = process.env.GITHUB_HEAD_REF === plannerLatencyCandidateRefreshBranch;
  const isPlannerContextBudgetCandidateRefresh = process.env.GITHUB_HEAD_REF === plannerContextBudgetCandidateRefreshBranch;
  const isPlannerSingleBatchCandidateRefresh = process.env.GITHUB_HEAD_REF === plannerSingleBatchCandidateRefreshBranch;
  const isPlannerCompactBatchCandidateRefresh = process.env.GITHUB_HEAD_REF === plannerCompactBatchCandidateRefreshBranch;
  const isPlannerCompactBatchActivation = process.env.GITHUB_HEAD_REF === plannerCompactBatchActivationBranch;
  const isPlannerCompactBatchAdmissionAncestryFix = process.env.GITHUB_HEAD_REF === plannerCompactBatchAdmissionAncestryFixBranch;
  const isPlannerCompactBatchSourceDriftBoundary = process.env.GITHUB_HEAD_REF === plannerCompactBatchSourceDriftBoundaryBranch;
  const isPlannerCompactBatchActivationRenewal = process.env.GITHUB_HEAD_REF === plannerCompactBatchActivationRenewalBranch;
  const isPlannerLiveRepairCandidateRefresh = process.env.GITHUB_HEAD_REF === plannerLiveRepairCandidateRefreshBranch;
  const isPlannerLiveRepairFinalRefresh = process.env.GITHUB_HEAD_REF === plannerLiveRepairFinalRefreshBranch;
  const isPlannerProviderContractRefresh = process.env.GITHUB_HEAD_REF === plannerProviderContractRefreshBranch;
  const isPlannerLiveProviderOutputRefresh = process.env.GITHUB_HEAD_REF === plannerLiveProviderOutputRefreshBranch;
  const isPlannerNativeTestApprovalRenewal = process.env.GITHUB_HEAD_REF === plannerNativeTestApprovalRenewalBranch;
  const isPlannerSingleBatchActivation = process.env.GITHUB_HEAD_REF === plannerSingleBatchActivationBranch;
  const isPlannerContextBudgetActivation = process.env.GITHUB_HEAD_REF === plannerContextBudgetActivationBranch;
  const isPlannerLatencyActivation = process.env.GITHUB_HEAD_REF === plannerLatencyActivationBranch;
  const isLivePlannerActivation = process.env.GITHUB_HEAD_REF === livePlannerActivationBranch;
  const isPlannerTimeBudgetApproval = process.env.GITHUB_HEAD_REF === plannerTimeBudgetApprovalBranch;
  const isPlannerProviderBudgetSuccessorApproval = process.env.GITHUB_HEAD_REF === plannerProviderBudgetSuccessorApprovalBranch;
  const isPlannerProviderBudgetActivation = process.env.GITHUB_HEAD_REF === plannerProviderBudgetActivationBranch;
  const isPlannerTimeBudgetWorkflowSet = process.env.GITHUB_HEAD_REF === plannerTimeBudgetWorkflowSetBranch;
  const isPlannerTimeBudgetActivation = process.env.GITHUB_HEAD_REF === plannerTimeBudgetActivationBranch;
  const isPlannerOutputBudgetActivation = process.env.GITHUB_HEAD_REF === plannerOutputBudgetActivationBranch;
  const isPlannerOutputBudgetActivationRefresh = process.env.GITHUB_HEAD_REF === plannerOutputBudgetActivationRefreshBranch;
  const isPlannerOutputBudgetCandidateRefresh = process.env.GITHUB_HEAD_REF === plannerOutputBudgetCandidateRefreshBranch;
  const isPlannerOutputBudgetFinalActivation = process.env.GITHUB_HEAD_REF === plannerOutputBudgetFinalActivationBranch;
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
  const isInstalledSowRoleAcceptance = process.env.GITHUB_HEAD_REF === installedSowRoleAcceptanceBranch;
  const isInstalledVerifierMainPath = process.env.GITHUB_HEAD_REF === installedVerifierMainPathBranch;
  const isInstalledVerifierStepNames = process.env.GITHUB_HEAD_REF === installedVerifierStepNamesBranch;
  const isModule025MyRoleCelarRepair = process.env.GITHUB_HEAD_REF === module025MyRoleCelarRepairBranch;
  const isModule025SowRoleCandidateRefresh = process.env.GITHUB_HEAD_REF === module025SowRoleCandidateRefreshBranch;
  const isModule025SowRoleCandidateRefreshFinal = process.env.GITHUB_HEAD_REF === module025SowRoleCandidateRefreshFinalBranch;
  const isModule025SowRoleCandidateRefresh1009 = process.env.GITHUB_HEAD_REF === module025SowRoleCandidateRefresh1009Branch;
  const isModule025SowRoleCandidateRefresh1014 = process.env.GITHUB_HEAD_REF === module025SowRoleCandidateRefresh1014Branch;
  const isPlannerProviderDeadlineCandidateRefresh = process.env.GITHUB_HEAD_REF === plannerProviderDeadlineCandidateRefreshBranch;
  const isPlannerControlCandidateApproval = process.env.GITHUB_HEAD_REF === plannerControlCandidateApprovalBranch;
  const isPlannerControlCandidateBaseCorrection = process.env.GITHUB_HEAD_REF === plannerControlCandidateBaseCorrectionBranch;
  const isPlannerControlPathCoverage = process.env.GITHUB_HEAD_REF === plannerControlPathCoverageBranch;
  const isPlannerCandidateApprovalRefresh = process.env.GITHUB_HEAD_REF === plannerCandidateApprovalRefreshBranch;
  const isPlannerAdmissionManifestRefresh = process.env.GITHUB_HEAD_REF === plannerAdmissionManifestRefreshBranch;
  const isPlannerCompactPhaseCandidateRefresh = process.env.GITHUB_HEAD_REF === plannerCompactPhaseCandidateRefreshBranch;
  const isPlannerCompactPhaseNativeGate = process.env.GITHUB_HEAD_REF === plannerCompactPhaseNativeGateBranch;
  const isModule025SowRoleAdmissionScope = process.env.GITHUB_HEAD_REF === module025SowRoleAdmissionScopeBranch;
  const isModule025SowRoleNativeActivation = process.env.GITHUB_HEAD_REF === module025SowRoleNativeActivationBranch;
  const isModule025SowRoleNativeActive = process.env.GITHUB_HEAD_REF === module025SowRoleNativeActiveBranch;
  const isModule025SowRoleLiveAcceptance = process.env.GITHUB_HEAD_REF === module025SowRoleLiveAcceptanceBranch;
  const isModule025SowRoleLiveRepair = process.env.GITHUB_HEAD_REF === module025SowRoleLiveRepairBranch;
  const isTriggerCoverage = process.env.GITHUB_HEAD_REF === triggerCoverageBranch;
  const isPlannerProviderDeadlineRetry = process.env.GITHUB_HEAD_REF === plannerProviderDeadlineRetryBranch
    || process.env.GITHUB_HEAD_REF === plannerCompactPhaseFixBranch;
  const isPlannerCompactPhaseFix = process.env.GITHUB_HEAD_REF === plannerCompactPhaseFixBranch;
  const isAdmissionManifestOrder = process.env.GITHUB_HEAD_REF === admissionManifestOrderBranch;
  if (isInstalledVerifierMainPath) verifyFiles(changed, manifest, 'installed-verifier-main-path', context);
  if (isInstalledVerifierStepNames) verifyFiles(changed, manifest, 'installed-verifier-step-names', context);
  if (isModule025MyRoleCelarRepair) verifyFiles(changed, manifest, 'module025-my-role-celar-repair', context);
  if (isModule025SowRoleCandidateRefresh) verifyFiles(changed, manifest, 'module025-sow-role-candidate-refresh', context);
  if (isModule025SowRoleCandidateRefreshFinal) verifyFiles(changed, manifest, 'module025-sow-role-candidate-refresh-final', context);
  if (isModule025SowRoleCandidateRefresh1009) verifyFiles(changed, manifest, 'module025-sow-role-candidate-refresh-1009', context);
  if (isModule025SowRoleCandidateRefresh1014) verifyFiles(changed, manifest, 'module025-sow-role-candidate-refresh-1014', context);
  if (isPlannerProviderDeadlineCandidateRefresh) verifyFiles(changed, manifest, 'planner-provider-deadline-candidate-refresh', context);
  if (isPlannerControlCandidateApproval) verifyFiles(changed, manifest, 'planner-control-candidate-approval', context);
  if (isPlannerControlCandidateBaseCorrection) verifyFiles(changed, manifest, 'planner-control-candidate-base-correction', context);
  if (isPlannerControlPathCoverage) verifyFiles(changed, manifest, 'planner-control-path-coverage', context);
  if (isPlannerCandidateApprovalRefresh) verifyFiles(changed, manifest, 'planner-candidate-approval-refresh', context);
  if (isPlannerAdmissionManifestRefresh) verifyFiles(changed, manifest, 'planner-admission-manifest-refresh', context);
  if (isPlannerCompactPhaseCandidateRefresh) verifyFiles(changed, manifest, 'planner-compact-phase-candidate-refresh', context);
  if (isPlannerCompactPhaseNativeGate) verifyFiles(changed, manifest, 'planner-compact-phase-native-gate', context);
  if (isModule025SowRoleAdmissionScope) verifyFiles(changed, manifest, 'module025-sow-role-admission-scope', context);
  if (isModule025SowRoleNativeActivation) verifyFiles(changed, manifest, 'module025-sow-role-native-activation', context);
  if (isModule025SowRoleNativeActive) verifyFiles(changed, manifest, 'module025-sow-role-native-active', context);
  if (isModule025SowRoleLiveAcceptance) verifyFiles(changed, manifest, 'module025-sow-role-live-acceptance', context);
  if (isModule025SowRoleLiveRepair) verifyFiles(changed, manifest, 'module025-sow-role-live-repair', context);
  if (isTriggerCoverage) verifyFiles(changed, manifest, 'trigger-coverage', context);
  if (isPlannerProviderDeadlineRetry && !isPlannerCompactPhaseFix) verifyFiles(changed, manifest, 'planner-provider-deadline-retry', context);
  if (isPlannerCompactPhaseFix) verifyFiles(changed, manifest, 'planner-compact-phase-fix', context);
  if (isAdmissionManifestOrder) verifyFiles(changed, manifest, 'admission-manifest-order', context);
  if (isPlannerNativeTestApprovalRenewal) verifyFiles(changed, manifest, 'planner-native-test-approval-renewal', context);
  if (isPlannerLiveRepairCandidateRefresh) verifyFiles(changed, manifest, 'planner-live-repair-candidate-refresh', context);
  if (isPlannerLiveRepairFinalRefresh) verifyFiles(changed, manifest, 'planner-live-repair-final-refresh', context);
  if (isPlannerProviderContractRefresh) verifyFiles(changed, manifest, 'planner-provider-contract-refresh', context);
  if (isPlannerLiveProviderOutputRefresh) verifyFiles(changed, manifest, 'planner-live-provider-output-refresh', context);
  if (!isPlannerNativeTestApprovalRenewal && !isPlannerLiveRepairCandidateRefresh && !isPlannerLiveRepairFinalRefresh && !isPlannerProviderContractRefresh && !isPlannerLiveProviderOutputRefresh && !isInstalledVerifierMainPath && !isInstalledVerifierStepNames && !isModule025MyRoleCelarRepair && !isModule025SowRoleCandidateRefresh && !isModule025SowRoleCandidateRefreshFinal && !isModule025SowRoleCandidateRefresh1009 && !isModule025SowRoleCandidateRefresh1014 && !isPlannerProviderDeadlineCandidateRefresh && !isPlannerControlCandidateApproval && !isPlannerControlCandidateBaseCorrection && !isPlannerControlPathCoverage && !isPlannerCandidateApprovalRefresh && !isPlannerAdmissionManifestRefresh && !isPlannerCompactPhaseCandidateRefresh && !isPlannerCompactPhaseNativeGate && !isModule025SowRoleAdmissionScope && !isModule025SowRoleNativeActivation && !isModule025SowRoleNativeActive && !isModule025SowRoleLiveAcceptance && !isModule025SowRoleLiveRepair && !isTriggerCoverage && !isPlannerProviderDeadlineRetry && !isAdmissionManifestOrder) verifyFiles(changed, manifest,
    isRepair ? 'pr874-digest-repair' : isReviewedRegeneration ? 'reviewed-regeneration-105' : isCandidateRefresh ? 'candidate-refresh' : isSuccessorCandidateRefresh ? 'successor-candidate-refresh' : isSourceBaseCorrection ? 'source-base-correction' : isSuccessorApproval ? 'successor-approval' : isSuccessorReleaseApproval ? 'successor-release-approval' : isSuccessorCutoverActivation ? 'successor-cutover-activation' : isPlannerOutputBudgetApproval ? 'planner-output-budget-approval' : isPlannerOutputShapeApproval ? 'planner-output-shape-approval' : isPlannerOutputShapeActivation ? 'planner-output-shape-activation' : isPlannerOutputShapeBranchCorrection ? 'planner-output-shape-branch-correction' : isPlannerPhaseCandidateRefresh ? 'planner-phase-candidate-refresh' : isLivePlannerCandidateRefresh ? 'live-planner-candidate-refresh' : isPlannerLatencyCandidateRefresh ? 'planner-latency-candidate-refresh' : isPlannerContextBudgetCandidateRefresh ? 'planner-context-budget-candidate-refresh' : isPlannerSingleBatchCandidateRefresh ? 'planner-single-batch-candidate-refresh' : isPlannerCompactBatchCandidateRefresh ? 'planner-compact-batch-candidate-refresh' : isPlannerCompactPhaseNativeGate ? 'planner-compact-phase-native-gate' : isPlannerCompactBatchActivation ? 'planner-compact-batch-activation' : isPlannerCompactBatchAdmissionAncestryFix ? 'planner-compact-batch-admission-ancestry-fix' : isPlannerCompactBatchSourceDriftBoundary ? 'planner-compact-batch-source-drift-boundary' : isPlannerCompactBatchActivationRenewal ? 'planner-compact-batch-activation-renewal' : isPlannerSingleBatchActivation ? 'planner-single-batch-activation' : isPlannerContextBudgetActivation ? 'planner-context-budget-activation' : isPlannerLatencyActivation ? 'planner-latency-activation' : isLivePlannerActivation ? 'live-planner-activation' : isPlannerTimeBudgetApproval ? 'planner-time-budget-approval' : isPlannerProviderBudgetSuccessorApproval ? 'planner-provider-budget-successor-approval' : isPlannerProviderBudgetActivation ? 'planner-provider-budget-activation' : isPlannerTimeBudgetWorkflowSet ? 'planner-time-budget-workflow-set' : isPlannerTimeBudgetActivation ? 'planner-time-budget-activation' : isPlannerOutputBudgetActivation ? 'planner-output-budget-activation' : isPlannerOutputBudgetActivationRefresh ? 'planner-output-budget-activation-refresh' : isPlannerOutputBudgetCandidateRefresh ? 'planner-output-budget-candidate-refresh' : isPlannerOutputBudgetFinalActivation ? 'planner-output-budget-final-activation' : isDispatchRunRecovery ? 'dispatch-run-recovery' : isDispatchLaneRepair ? 'dispatch-lane-repair' : isDispatchSkippedRecovery ? 'dispatch-skipped-recovery' : isDispatchReceiptParser ? 'dispatch-receipt-parser' : isDispatchLaneAdmission ? 'dispatch-lane-admission' : isProtectedCutoverLaneRefresh ? 'protected-cutover-lane-refresh' : isProtectedCutoverPmIdentityRefresh ? 'protected-cutover-pm-identity-refresh' : isStaleSupersession ? 'stale-run-supersession' : isStaleSupersessionActivation ? 'stale-run-activation' : isStaleSupersessionRenewal ? 'stale-run-renewal' : isMigration106Wiring ? 'migration106-wiring' : isProtectedCutover ? 'protected-cutover' : isProtectedCutoverActivation ? 'protected-cutover-activation' : isProtectedCutoverRefresh ? 'protected-cutover-refresh' : isAdmissionPermissionFix ? 'admission-permission-fix' : isAdmissionBotClaimFix ? 'admission-bot-claim-fix' : isProtectedCutoverRecovery ? 'protected-cutover-recovery' : isReservationRecoveryEndpointFix ? 'reservation-recovery-endpoint-fix' : isProtectedCutoverFinal ? 'protected-cutover-final' : isInstalledSowRoleAcceptance ? 'installed-sow-role-acceptance' : isInstalledAcceptanceFinal ? 'installed-acceptance-final' : isInstalledAcceptancePlanner ? 'installed-acceptance-planner' : isInstalledAcceptanceVisibility ? 'installed-acceptance-visibility' : isInstalledAcceptanceReadiness ? 'installed-acceptance-readiness' : isInstalledAcceptanceArtifacts ? 'installed-acceptance-artifacts' : isInstalledAcceptanceEvidence ? 'installed-acceptance-evidence' : isInstalledVerificationEvidence ? 'installed-verification-evidence' : isInstalledVerificationEnvironment ? 'installed-verification-environment' : isLiveUatStatusFix ? 'live-uat-status-fix' : isReleaseStabilization ? 'release-stabilization' : 'initial', context);
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
  if (isPlannerCompactPhaseNativeGate) {
    assert.equal(protectedCutover.enabled, true, 'Native-gated cutover must remain explicitly enabled for the maintained controller.');
    assert.equal(protectedCutover.activationDecision, 'native-test-required');
    assert.equal(protectedCutover.workflow.allowControllerActivation, false);
    assert.deepEqual(protectedCutover.approval, {
      mode: 'native-test-environment', status: 'native-required', approvedBy: null, approvedAt: null, expiresAt: null
    });
  }
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
  if (isModule025SowRoleCandidateRefreshFinal) {
    assert.equal(protectedCutover.enabled, false, 'The final candidate refresh must not activate deployment.');
    assert.equal(protectedCutover.activationDecision, 'hold');
    assert.equal(protectedCutover.workflow.allowControllerActivation, false);
    assert.deepEqual(protectedCutover.approval, {
      status: 'not-approved',
      approvedBy: null, approvedAt: null, expiresAt: null
    });
  }
  if (isProtectedCutover || isProtectedCutoverActivation || isProtectedCutoverRefresh || isProtectedCutoverRecovery || isProtectedCutoverFinal || isSuccessorReleaseApproval || isSuccessorCutoverActivation || isPlannerOutputShapeApproval || isPlannerOutputShapeActivation || isPlannerOutputShapeBranchCorrection || isPlannerPhaseCandidateRefresh || isLivePlannerCandidateRefresh || isPlannerLatencyCandidateRefresh || isPlannerContextBudgetCandidateRefresh || isPlannerSingleBatchCandidateRefresh || isPlannerCompactBatchCandidateRefresh || isPlannerCompactBatchActivation || isPlannerCompactBatchAdmissionAncestryFix || isPlannerCompactBatchSourceDriftBoundary || isPlannerCompactBatchActivationRenewal || isPlannerSingleBatchActivation || isPlannerContextBudgetActivation || isPlannerLatencyActivation || isLivePlannerActivation || isPlannerTimeBudgetApproval || isPlannerProviderBudgetSuccessorApproval || isPlannerProviderBudgetActivation || isPlannerTimeBudgetActivation || isPlannerOutputBudgetActivation || isPlannerOutputBudgetActivationRefresh || isPlannerOutputBudgetCandidateRefresh || isPlannerOutputBudgetFinalActivation || isPlannerLiveRepairFinalRefresh || isPlannerProviderContractRefresh || isPlannerLiveProviderOutputRefresh || isDispatchSkippedRecovery || isModule025SowRoleNativeActivation || isModule025SowRoleNativeActive) {
    if (isPlannerLiveRepairFinalRefresh || isPlannerProviderContractRefresh || isPlannerLiveProviderOutputRefresh) {
      assert.equal(protectedCutover.enabled, true, 'Final candidate refresh must retain an explicitly active native-gated cutover.');
      assert.equal(protectedCutover.activationDecision, 'approved');
      assert.equal(protectedCutover.workflow.allowControllerActivation, false);
    }
    if (isProtectedCutoverActivation || isProtectedCutoverRefresh || isProtectedCutoverRecovery || isProtectedCutoverFinal || isSuccessorCutoverActivation || isPlannerOutputShapeActivation || isPlannerOutputShapeBranchCorrection || isPlannerCompactBatchActivation || isPlannerCompactBatchAdmissionAncestryFix || isPlannerCompactBatchActivationRenewal || isPlannerSingleBatchActivation || isPlannerContextBudgetActivation || isPlannerLatencyActivation || isLivePlannerActivation || isPlannerProviderBudgetActivation || isPlannerTimeBudgetActivation || isPlannerOutputBudgetActivation || isPlannerOutputBudgetActivationRefresh || isPlannerOutputBudgetFinalActivation || isDispatchSkippedRecovery || isModule025SowRoleNativeActivation || isModule025SowRoleNativeActive) {
      assert.equal(base, isModule025SowRoleNativeActivation ? module025SowRoleNativeActivationBase : isModule025SowRoleNativeActive ? module025SowRoleNativeActiveBase : isPlannerCompactBatchActivationRenewal ? plannerCompactBatchActivationRenewalBase : isPlannerCompactBatchAdmissionAncestryFix ? plannerCompactBatchAdmissionAncestryFixBase : isPlannerCompactBatchActivation ? plannerCompactBatchActivationBase : isPlannerSingleBatchActivation ? plannerSingleBatchActivationBase : isPlannerContextBudgetActivation ? plannerContextBudgetActivationBase : isPlannerLatencyActivation ? plannerLatencyActivationBase : isPlannerOutputBudgetFinalActivation ? plannerOutputBudgetFinalActivationBase : isLivePlannerActivation ? livePlannerActivationBase : isPlannerOutputShapeBranchCorrection ? plannerOutputShapeBranchCorrectionBase : isPlannerOutputShapeActivation ? plannerOutputShapeActivationBase : isPlannerProviderBudgetActivation ? plannerProviderBudgetActivationBase : isPlannerTimeBudgetActivation ? plannerTimeBudgetActivationBase : isProtectedCutoverLaneRefresh ? protectedCutoverLaneRefreshBase : isProtectedCutoverPmIdentityRefresh ? protectedCutoverPmIdentityRefreshBase : isDispatchSkippedRecovery ? dispatchSkippedRecoveryBase : isPlannerOutputBudgetActivationRefresh ? plannerOutputBudgetActivationRefreshBase : isPlannerOutputBudgetActivation ? plannerOutputBudgetActivationBase : isSuccessorCutoverActivation ? successorCutoverActivationBase : isProtectedCutoverFinal ? protectedCutoverFinalBase : isProtectedCutoverRecovery ? protectedCutoverRecoveryBase : isProtectedCutoverRefresh ? protectedCutoverRefreshBase : protectedCutoverActivationBase, 'Activation must be based on the latest reviewed trusted controls.');
      assert.equal(protectedCutover.enabled, true, 'Activation must be explicitly enabled only in its reviewed branch.');
      assert.equal(protectedCutover.activationDecision, 'approved');
      assert.equal(protectedCutover.workflow.allowControllerActivation, isModule025SowRoleNativeActivation ? true : isModule025SowRoleNativeActive ? false : (isProtectedCutoverActivation || isPlannerOutputShapeActivation || isPlannerOutputShapeBranchCorrection || isPlannerCompactBatchActivation || isPlannerCompactBatchAdmissionAncestryFix || isPlannerCompactBatchActivationRenewal || isPlannerSingleBatchActivation || isPlannerContextBudgetActivation || isPlannerLatencyActivation || isLivePlannerActivation || isPlannerProviderBudgetActivation || isPlannerTimeBudgetActivation || isSuccessorCutoverActivation || isPlannerOutputBudgetActivation || isPlannerOutputBudgetActivationRefresh || isPlannerOutputBudgetFinalActivation || isDispatchSkippedRecovery || isProtectedCutoverLaneRefresh || isProtectedCutoverPmIdentityRefresh ? false : true));
      if (isModule025SowRoleNativeActivation || isModule025SowRoleNativeActive) {
        assert.deepEqual(protectedCutover.approval, {
          mode: 'native-test-environment', status: 'native-required', approvedBy: null, approvedAt: null, expiresAt: null
        });
      } else {
        assert.equal(protectedCutover.approval.status, 'approved');
        assert.equal(protectedCutover.approval.approvedBy, 'ahmedadeyemi-cts');
        const approvedAt = Date.parse(protectedCutover.approval.approvedAt);
        const expiresAt = Date.parse(protectedCutover.approval.expiresAt);
        assert.ok(Number.isFinite(approvedAt) && Number.isFinite(expiresAt) && expiresAt > approvedAt);
        assert.ok(expiresAt - approvedAt <= 15 * 60 * 1000, 'Activation approval must remain bounded.');
      }
    }
    assert.equal(protectedCutover.contract, 'flowhive-psa-protected-cutover-v1');
    if (isProtectedCutover || isSuccessorReleaseApproval || isPlannerOutputShapeApproval || isPlannerPhaseCandidateRefresh || isLivePlannerCandidateRefresh || isPlannerLatencyCandidateRefresh || isPlannerContextBudgetCandidateRefresh || isPlannerSingleBatchCandidateRefresh || isPlannerCompactBatchCandidateRefresh || isPlannerTimeBudgetApproval || isPlannerProviderBudgetSuccessorApproval || isPlannerOutputBudgetCandidateRefresh) {
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
      transition: 'disabled_manually-to-active-once', allowControllerActivation: isModule025SowRoleNativeActivation || (isProtectedCutoverRefresh && !isProtectedCutoverLaneRefresh && !isProtectedCutoverPmIdentityRefresh) || isProtectedCutoverRecovery || isProtectedCutoverFinal
    });
    assert.deepEqual(protectedCutover.environment, {
      environment: 'test', protectionRuleId: 65110773,
      requiredReviewerLogin: 'ahmedadeyemi-cts', requiredReviewerId: 244059331,
      reviewers: [{ type: 'User', login: 'ahmedadeyemi-cts', id: 244059331 }],
      preventSelfReview: false, canAdminsBypass: false
    });
    assert.deepEqual(protectedCutover.requests.map(request => request.runId), [34495606530, 34377182662, 33654881418]);
    assert.equal(protectedCutover.serverDispatchInputsConfirmed, false);
    if (isProtectedCutover || isSuccessorReleaseApproval || isPlannerOutputShapeApproval || isPlannerPhaseCandidateRefresh || isLivePlannerCandidateRefresh || isPlannerLatencyCandidateRefresh || isPlannerContextBudgetCandidateRefresh || isPlannerSingleBatchCandidateRefresh || isPlannerCompactBatchCandidateRefresh || isPlannerTimeBudgetApproval || isPlannerProviderBudgetSuccessorApproval || isPlannerOutputBudgetCandidateRefresh) {
      assert.deepEqual(protectedCutover.approval, { status: 'not-approved', approvedBy: null, approvedAt: null, expiresAt: null });
    } else if (isPlannerLiveRepairFinalRefresh || isPlannerProviderContractRefresh || isPlannerLiveProviderOutputRefresh || isModule025SowRoleNativeActivation || isModule025SowRoleNativeActive) {
      assert.deepEqual(protectedCutover.approval, { mode: 'native-test-environment', status: 'native-required', approvedBy: null, approvedAt: null, expiresAt: null });
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
  const admissionGuide = fs.readFileSync('docs/releases/FLOWHIVE-PSA-PROTECTED-TEST-ADMISSION.md', 'utf8');
  const module025Workflow = fs.readFileSync('.github/workflows/module025-governed-protected-test-release-ci.yml', 'utf8');
  assert.ok(!/azure\/login|id-token:|environment:|contents:\s*write/.test(supervisor), 'Admission cannot mutate a cloud environment or source.');
  assert.ok(supervisor.includes('github.event.issue.number == 887') && supervisor.includes("github.actor == 'ahmedadeyemi-cts'"));
  assert.match(admissionGuide, /exact command as a comment on PR #887's\s+issue thread/);
  assert.match(admissionGuide, /not on the candidate PR or another issue/);
  assert.match(module025Workflow, /- '\.github\/flowhive-psa-release-control-files\.txt'/);
  assert.match(module025Workflow, /- '\.github\/workflows\/module025-governed-protected-test-release-ci\.yml'/);
  assert.ok(supervisor.includes('group: module025-protected-uat-control') && supervisor.includes('cancel-in-progress: false'));
  assert.ok(supervisor.includes('FLOWHIVE_PSA_DISPATCH_EVIDENCE_FILE'));
  assert.ok(supervisor.includes('issues: write') && supervisor.includes('pull-requests: write'),
    'The admission token must be able to create the reservation on a pull request issue.');
  assert.doesNotMatch(supervisor, /enable|reseal/i, 'Routine admission must not toggle the canonical deployment workflow.');
  console.log('FLOWHIVE_PSA_RELEASE_CONTROL_SCOPE=PASS productionMutation=false featureMerge=false');
}
if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) validate();
