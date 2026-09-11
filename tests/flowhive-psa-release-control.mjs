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
export const dispatchRecoveryBase = 'af5fcb463384096f668345ac7cc9bd00efef0a33';
export const dispatchRecoveryBranch = 'fix/flowhive-dispatch-run-recovery-20260909';
export const dispatchRecoveryFiles = [
  '.github/workflows/flowhive-psa-release-control-ci.yml',
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
export const liveUatStatusFixBase = 'b40d2e430c2385f57c3ce02e6d3b995ee1873a11';
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
  assert.ok(['initial','pr874-digest-repair','reviewed-regeneration-105','candidate-refresh','successor-candidate-refresh','source-base-correction','successor-approval','dispatch-run-recovery','stale-run-supersession','stale-run-activation','stale-run-renewal','migration106-wiring','release-stabilization','protected-cutover','protected-cutover-activation','protected-cutover-refresh','admission-permission-fix','admission-bot-claim-fix','protected-cutover-recovery','reservation-recovery-endpoint-fix','protected-cutover-final','live-uat-status-fix'].includes(mode), 'Unrecognized control repair.');
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
  if (mode === 'dispatch-run-recovery') {
    assert.equal(context?.base, dispatchRecoveryBase, 'Dispatch recovery must be based on merged trusted control main.');
    assert.equal(context?.branch, dispatchRecoveryBranch, 'Wrong dispatch recovery branch.');
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
    assert.equal(context?.base, protectedCutoverRefreshBase, 'Protected cutover refresh must be based on PR899 trusted main.');
    assert.equal(context?.branch, protectedCutoverRefreshBranch, 'Wrong protected cutover refresh control branch.');
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
  const expected = mode === 'initial' ? files : mode === 'pr874-digest-repair' ? repairFiles : mode === 'reviewed-regeneration-105' ? reviewedRegenerationFiles : mode === 'candidate-refresh' ? candidateRefreshFiles : mode === 'successor-candidate-refresh' ? successorCandidateRefreshFiles : mode === 'source-base-correction' ? sourceBaseCorrectionFiles : mode === 'successor-approval' ? successorApprovalFiles : mode === 'dispatch-run-recovery' ? dispatchRecoveryFiles : mode === 'stale-run-activation' ? staleSupersessionActivationFiles : mode === 'stale-run-renewal' ? staleSupersessionRenewalFiles : mode === 'migration106-wiring' ? migration106WiringFiles : mode === 'release-stabilization' ? releaseStabilizationFiles : mode === 'protected-cutover' ? protectedCutoverFiles : mode === 'protected-cutover-activation' ? protectedCutoverActivationFiles : mode === 'protected-cutover-refresh' ? protectedCutoverRefreshFiles : mode === 'admission-permission-fix' ? admissionPermissionFixFiles : mode === 'admission-bot-claim-fix' ? admissionBotClaimFixFiles : mode === 'protected-cutover-recovery' ? protectedCutoverRecoveryFiles : mode === 'reservation-recovery-endpoint-fix' ? reservationRecoveryEndpointFixFiles : mode === 'protected-cutover-final' ? protectedCutoverFinalFiles : mode === 'live-uat-status-fix' ? liveUatStatusFixFiles : staleSupersessionFiles;
  assert.deepEqual([...changed].sort(), expected, 'Unexpected or missing file in the release-control PR.');
}
export function verifyController(text) {
  for (const token of [
    'group: projectpulse-deploy-test', 'queue: max', 'cancel-in-progress: false', 'environment: test',
    'node scripts/release-test/flowhive-psa-admission.mjs', 'PSA_RELEASE_AUTHORIZED',
    'refs/heads/main', '105_flowhive_reviewed_regeneration.sql', '106_module025_sow_sell_register.sql', 'build-and-run-flowhive-psa-migrations.sh',
    'run-flowhive-psa-live-uat.py', 'RELIABILITY_RELEASE_COMMIT:',
    "steps.psa_live_uat.outputs.deployment_health_verified != 'true'",
  ]) {
    assert.ok(text.includes(token), `The Test controller is missing a required control: ${token}`);
  }
  assert.match(text, /deploy:\s*\n[\s\S]*?if: >-\n[\s\S]*github\.event_name == 'push'[\s\S]*github\.event_name == 'workflow_dispatch'[\s\S]*inputs\.release_branch == 'main'[\s\S]*inputs\.release_branch == 'release\/flowhive-sow-successor-20260908'/,
    'Every current deployment path must be bounded by the approved push or explicitly guarded manual-main/PSA dispatch lane.');
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
  const isDispatchRunRecovery = process.env.GITHUB_HEAD_REF === dispatchRecoveryBranch;
  const isStaleSupersession = process.env.GITHUB_HEAD_REF === staleSupersessionBranch;
  const isStaleSupersessionActivation = process.env.GITHUB_HEAD_REF === staleSupersessionActivationBranch;
  const isStaleSupersessionRenewal = process.env.GITHUB_HEAD_REF === staleSupersessionRenewalBranch;
  const isMigration106Wiring = process.env.GITHUB_HEAD_REF === migration106WiringBranch;
  const isReleaseStabilization = process.env.GITHUB_HEAD_REF === releaseStabilizationBranch;
  const isProtectedCutover = process.env.GITHUB_HEAD_REF === protectedCutoverBranch;
  const isProtectedCutoverActivation = process.env.GITHUB_HEAD_REF === protectedCutoverActivationBranch;
  const isProtectedCutoverRefresh = process.env.GITHUB_HEAD_REF === protectedCutoverRefreshBranch;
  const isAdmissionPermissionFix = process.env.GITHUB_HEAD_REF === admissionPermissionFixBranch;
  const isAdmissionBotClaimFix = process.env.GITHUB_HEAD_REF === admissionBotClaimFixBranch;
  const isProtectedCutoverRecovery = process.env.GITHUB_HEAD_REF === protectedCutoverRecoveryBranch;
  const isReservationRecoveryEndpointFix = process.env.GITHUB_HEAD_REF === reservationRecoveryEndpointFixBranch;
  const isProtectedCutoverFinal = process.env.GITHUB_HEAD_REF === protectedCutoverFinalBranch;
  const isLiveUatStatusFix = process.env.GITHUB_HEAD_REF === liveUatStatusFixBranch;
  verifyFiles(changed, manifest,
    isRepair ? 'pr874-digest-repair' : isReviewedRegeneration ? 'reviewed-regeneration-105' : isCandidateRefresh ? 'candidate-refresh' : isSuccessorCandidateRefresh ? 'successor-candidate-refresh' : isSourceBaseCorrection ? 'source-base-correction' : isSuccessorApproval ? 'successor-approval' : isDispatchRunRecovery ? 'dispatch-run-recovery' : isStaleSupersession ? 'stale-run-supersession' : isStaleSupersessionActivation ? 'stale-run-activation' : isStaleSupersessionRenewal ? 'stale-run-renewal' : isMigration106Wiring ? 'migration106-wiring' : isProtectedCutover ? 'protected-cutover' : isProtectedCutoverActivation ? 'protected-cutover-activation' : isProtectedCutoverRefresh ? 'protected-cutover-refresh' : isAdmissionPermissionFix ? 'admission-permission-fix' : isAdmissionBotClaimFix ? 'admission-bot-claim-fix' : isProtectedCutoverRecovery ? 'protected-cutover-recovery' : isReservationRecoveryEndpointFix ? 'reservation-recovery-endpoint-fix' : isProtectedCutoverFinal ? 'protected-cutover-final' : isLiveUatStatusFix ? 'live-uat-status-fix' : isReleaseStabilization ? 'release-stabilization' : 'initial', context);
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
  if (isProtectedCutover || isProtectedCutoverActivation || isProtectedCutoverRefresh || isProtectedCutoverRecovery || isProtectedCutoverFinal) {
    if (isProtectedCutoverActivation || isProtectedCutoverRefresh || isProtectedCutoverRecovery || isProtectedCutoverFinal) {
      assert.equal(base, isProtectedCutoverFinal ? protectedCutoverFinalBase : isProtectedCutoverRecovery ? protectedCutoverRecoveryBase : isProtectedCutoverRefresh ? protectedCutoverRefreshBase : protectedCutoverActivationBase, 'Activation must be based on the latest reviewed trusted controls.');
      assert.equal(protectedCutover.enabled, true, 'Activation must be explicitly enabled only in its reviewed branch.');
      assert.equal(protectedCutover.activationDecision, 'approved');
      assert.equal(protectedCutover.workflow.allowControllerActivation, true);
      const approvedAt = Date.parse(protectedCutover.approval.approvedAt);
      const expiresAt = Date.parse(protectedCutover.approval.expiresAt);
      assert.ok(Number.isFinite(approvedAt) && Number.isFinite(expiresAt) && expiresAt > approvedAt);
      assert.ok(expiresAt - approvedAt <= 15 * 60 * 1000, 'Activation approval must remain bounded.');
    }
    assert.equal(protectedCutover.contract, 'flowhive-psa-protected-cutover-v1');
    if (isProtectedCutover) {
      assert.equal(protectedCutover.enabled, false, 'Protected cutover must remain inactive until separately approved.');
      assert.equal(protectedCutover.activationDecision, 'hold');
    }
    assert.deepEqual(protectedCutover.candidate, {
      pullRequest: 887, branch: 'release/flowhive-sow-successor-20260908',
      sha: '95abbb0aa2445a33fda68e9de542f9446c3e2204'
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
    assert.deepEqual(protectedCutover.workflow, {
      id: 315562561, path: '.github/workflows/projectpulse-deploy-test.yml',
      controllerBranch: 'main', event: 'workflow_dispatch',
      transition: 'disabled_manually-to-active-once', allowControllerActivation: isProtectedCutoverActivation || isProtectedCutoverRefresh || isProtectedCutoverRecovery || isProtectedCutoverFinal
    });
    assert.deepEqual(protectedCutover.environment, {
      environment: 'test', protectionRuleId: 65110773,
      requiredReviewerLogin: 'ahmedadeyemi-cts', requiredReviewerId: 244059331,
      reviewers: [{ type: 'User', login: 'ahmedadeyemi-cts', id: 244059331 }],
      preventSelfReview: false, canAdminsBypass: false
    });
    assert.deepEqual(protectedCutover.requests.map(request => request.runId), [34495606530, 34377182662, 33654881418]);
    assert.equal(protectedCutover.serverDispatchInputsConfirmed, false);
    if (isProtectedCutover) {
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
