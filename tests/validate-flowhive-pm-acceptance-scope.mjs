import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { verifyController } from './flowhive-psa-release-control.mjs';

// This repair changes verification/control wiring only, never application,
// migration, deployment-authorization, secret, or customer data authority.
export const branch = 'fix/flowhive-pm-acceptance-contract';
export const files = [
  '.github/workflows/flowhive-psa-installed-acceptance.yml',
  '.github/workflows/flowhive-psa-release-control-ci.yml',
  '.github/workflows/projectpulse-deploy-test.yml',
  'scripts/ci/validate-celar-ai-enterprise-source-boundary.sh',
  'scripts/release-test/check-flowhive-acceptance-inputs.py',
  'scripts/release-test/prepare-protected-test-scope-manifests.sh',
  'scripts/release-test/resolve-flowhive-installed-deployment.py',
  'tests/flowhive-installed-resolution.test.py',
  'tests/flowhive-psa-installed-acceptance.test.py',
  'tests/validate-flowhive-pm-acceptance-scope.mjs',
].sort();

export function verifyScope(changed, headBranch) {
  assert.equal(headBranch, branch, 'Unexpected PM-acceptance repair branch.');
  assert.deepEqual([...changed].sort(), files, 'Unexpected or missing PM-acceptance repair file.');
}

export function validate() {
  const base = process.env.BASE_SHA || process.env.CONTROL_BASE;
  assert.match(base || '', /^[a-f0-9]{40}$/, 'The trusted PR base SHA is required.');
  const git = (...args) => execFileSync('git', args, { encoding: 'utf8' }).trim();
  git('cat-file', '-e', `${base}^{commit}`);
  git('merge-base', '--is-ancestor', base, 'HEAD');
  const changed = git('diff', '--name-only', `${base}...HEAD`).split('\n').filter(Boolean);
  verifyScope(changed, process.env.GITHUB_HEAD_REF || process.env.GITHUB_REF_NAME);
  for (const file of files) {
    const stat = fs.lstatSync(file);
    assert.ok(stat.isFile() && !stat.isSymbolicLink(), `Nonregular repair path: ${file}`);
  }
  verifyController(fs.readFileSync('.github/workflows/projectpulse-deploy-test.yml', 'utf8'));
  git('diff', '--check', `${base}...HEAD`);
  console.log('FLOWHIVE_PM_ACCEPTANCE_SCOPE=PASS applicationMutation=false migrationMutation=false');
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  if (process.argv.includes('--print-files')) {
    console.log(files.join('\n'));
  } else if (process.argv.includes('--self-test')) {
    verifyScope(files, branch);
    assert.throws(() => verifyScope(files, 'unrelated-branch'));
    assert.throws(() => verifyScope(files.slice(1), branch));
    assert.throws(() => verifyScope([...files, files[0]], branch));
    for (const prohibited of ['src/backend/ProjectTime.Api/Program.cs', 'database/migrations/107_module_066_operation_authorization_and_raid_actor.sql', '.github/flowhive-psa-protected-test-candidate.json', '.github/flowhive-psa-protected-cutover.json']) {
      assert.throws(() => verifyScope([...files, prohibited], branch));
    }
    console.log('FLOWHIVE_PM_ACCEPTANCE_SCOPE_NEGATIVE_TESTS=PASS');
  } else {
    validate();
  }
}
