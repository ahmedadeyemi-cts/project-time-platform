import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { verifyApproval, controlManifest } from '../scripts/release-test/flowhive-psa-admission.mjs';

export const files = [
  '.github/flowhive-psa-protected-test-candidate.json',
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
  'scripts/release-test/run-flowhive-psa-live-uat.py',
  'tests/flowhive-psa-admission.test.mjs',
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
  assert.ok(['initial','pr874-digest-repair','reviewed-regeneration-105','candidate-refresh','source-base-correction','successor-approval'].includes(mode), 'Unrecognized control repair.');
  if (mode === 'pr874-digest-repair') verifyRepairContext(context);
  if (mode === 'reviewed-regeneration-105') {
    assert.equal(context?.base, reviewedRegenerationBase, 'Reviewed regeneration control must be based on current main.');
    assert.equal(context?.branch, reviewedRegenerationBranch, 'Wrong reviewed regeneration control branch.');
  }
  if (mode === 'candidate-refresh') {
    assert.equal(context?.base, candidateRefreshBase, 'Candidate refresh must be based on the reviewed current main.');
    assert.equal(context?.branch, candidateRefreshBranch, 'Wrong candidate refresh control branch.');
  }
  if (mode === 'source-base-correction') {
    assert.equal(context?.base, sourceBaseCorrectionBase, 'Source-base correction must be based on the trusted candidate-refresh main.');
    assert.equal(context?.branch, sourceBaseCorrectionBranch, 'Wrong source-base correction control branch.');
  }
  if (mode === 'successor-approval') {
    assert.equal(context?.base, successorApprovalBase, 'Successor approval must be based on the current trusted main.');
    assert.equal(context?.branch, successorApprovalBranch, 'Wrong successor approval branch.');
  }
  const expected = mode === 'initial' ? files : mode === 'pr874-digest-repair' ? repairFiles : mode === 'reviewed-regeneration-105' ? reviewedRegenerationFiles : mode === 'candidate-refresh' ? candidateRefreshFiles : mode === 'source-base-correction' ? sourceBaseCorrectionFiles : successorApprovalFiles;
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
  assert.ok(!/contents:\s*write/.test(text), 'The environment mutation job must not publish source.');
  assert.ok(!/environment:\s*(?:production|prod)\b/i.test(text), 'Production is not an approved target.');
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
  const isSourceBaseCorrection = process.env.GITHUB_HEAD_REF === sourceBaseCorrectionBranch;
  const isSuccessorApproval = process.env.GITHUB_HEAD_REF === successorApprovalBranch;
  verifyFiles(changed, manifest,
    isRepair ? 'pr874-digest-repair' : isReviewedRegeneration ? 'reviewed-regeneration-105' : isCandidateRefresh ? 'candidate-refresh' : isSourceBaseCorrection ? 'source-base-correction' : isSuccessorApproval ? 'successor-approval' : 'initial', context);
  if (isRepair) {
    // The repair cannot alter the admitted environment workflow, permissions,
    // migration bytes or dispatcher. Only its exact seven-file list is allowed.
    assert.equal(fs.readFileSync('.github/workflows/projectpulse-deploy-test.yml','utf8').trimEnd(),
      git('show', `${base}:.github/workflows/projectpulse-deploy-test.yml`));
  }
  for (const file of files) assert.ok(fs.statSync(file).isFile() && !fs.lstatSync(file).isSymbolicLink());
  const approval = JSON.parse(fs.readFileSync('.github/flowhive-psa-protected-test-candidate.json', 'utf8'));
  verifyApproval(approval, approval.sha);
  verifyController(fs.readFileSync('.github/workflows/projectpulse-deploy-test.yml', 'utf8'));
  const supervisor = fs.readFileSync('.github/workflows/flowhive-psa-protected-test-admission.yml', 'utf8');
  assert.ok(!/azure\/login|id-token:|environment:|contents:\s*write/.test(supervisor), 'Admission cannot mutate a cloud environment or source.');
  assert.ok(supervisor.includes('github.event.issue.number == 887') && supervisor.includes("github.actor == 'ahmedadeyemi-cts'"));
  assert.ok(supervisor.includes('group: module025-protected-uat-control') && supervisor.includes('cancel-in-progress: false'));
  console.log('FLOWHIVE_PSA_RELEASE_CONTROL_SCOPE=PASS productionMutation=false featureMerge=false');
}
if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) validate();
