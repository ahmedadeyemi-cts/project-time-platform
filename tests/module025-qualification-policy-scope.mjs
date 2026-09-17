import assert from 'node:assert/strict';
import fs from 'node:fs';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import { verifyReadOnlyWorkflow } from './flowhive-psa-scope.mjs';

const base = 'ee78efea179325e0ee4d8ef784c9415a28028ac0';
const expected = [
  '.github/workflows/flowhive-psa-release-control-ci.yml',
  'scripts/release-test/run-module025-provider-qualification.py',
  'scripts/release-test/validate-protected-test-controller-branches.sh',
  'tests/flowhive-psa-admission.test.mjs',
  'tests/module025-qualification-policy-scope.mjs',
  'tests/module025-provider-qualification.test.py',
  'tests/validate-celar-ai-pr630-consolidated.mjs'
].sort();
export function verifyModule025QualificationPolicyScope() {
  assert.equal(execFileSync('git', ['merge-base', base, 'HEAD'], {encoding:'utf8'}).trim(), base);
  const actual = execFileSync('git', ['diff', '--name-only', `${base}...HEAD`], {encoding:'utf8'}).trim().split(/\r?\n/);
  const verify = files => assert.deepEqual([...files].sort(), expected);
  verify(actual);
  assert.throws(() => verify([...actual, '.github/workflows/projectpulse-deploy-production.yml']));
  assert.throws(() => verify(actual.slice(1)));
  verifyReadOnlyWorkflow(fs.readFileSync(expected[0], 'utf8'), expected[0]);
  for (const file of ['.github/workflows/projectpulse-deploy-test.yml',
    '.github/flowhive-psa-protected-test-candidate.json', '.github/flowhive-psa-release-control-files.txt',
    'scripts/release-test/flowhive-psa-admission.mjs', 'scripts/validate-deployment-concurrency-governance.mjs'])
    assert.deepEqual(fs.readFileSync(file), execFileSync('git', ['show', `${base}:${file}`]));
  for (const directory of ['src', 'deployment', 'database'])
    assert.equal(execFileSync('git', ['rev-parse', `HEAD:${directory}`], {encoding:'utf8'}),
      execFileSync('git', ['rev-parse', `${base}:${directory}`], {encoding:'utf8'}));
  execFileSync('python3', ['tests/module025-provider-qualification.test.py'], {stdio:'inherit'});
  console.log('MODULE025_QUALIFICATION_POLICY_SCOPE=PASS application_and_deployment_authority=unchanged');
}
if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) verifyModule025QualificationPolicyScope();
