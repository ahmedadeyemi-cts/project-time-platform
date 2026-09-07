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
export function verifyFiles(changed, manifest, mode = 'initial') {
  assert.deepEqual(manifest, files, 'Approval must retain the exact reviewed control-only file list.');
  assert.ok(['initial','pr874-digest-repair'].includes(mode), 'Unrecognized control repair.');
  assert.deepEqual([...changed].sort(), mode === 'initial' ? files : repairFiles, 'Unexpected or missing file in the release-control PR.');
}
export function verifyController(text) {
  for (const token of [
    'group: projectpulse-deploy-test', 'queue: max', 'cancel-in-progress: false', 'environment: test',
    'node scripts/release-test/flowhive-psa-admission.mjs', 'PSA_RELEASE_AUTHORIZED',
    'refs/heads/main', 'build-and-run-flowhive-psa-migrations.sh', 'run-flowhive-psa-live-uat.py',
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
  const isRepair = changed.length === repairFiles.length;
  verifyFiles(changed, manifest, isRepair ? 'pr874-digest-repair' : 'initial');
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
  assert.ok(supervisor.includes('github.event.issue.number == 872') && supervisor.includes("github.actor == 'ahmedadeyemi-cts'"));
  assert.ok(supervisor.includes('group: module025-protected-uat-control') && supervisor.includes('cancel-in-progress: false'));
  console.log('FLOWHIVE_PSA_RELEASE_CONTROL_SCOPE=PASS productionMutation=false featureMerge=false');
}
if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) validate();
