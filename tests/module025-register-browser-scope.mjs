import assert from 'node:assert/strict';
import fs from 'node:fs';
import { execFileSync } from 'node:child_process';
import { verifyReadOnlyWorkflow } from './flowhive-psa-scope.mjs';
const base = '5dac14686c3052b8053f5f67189e02f10f07408d';
const git = (...args) => execFileSync('git', args, {encoding:'utf8'}).trim();
const original = name => execFileSync('git', ['show', `${base}:${name}`], {encoding:'utf8'});
const expected = [
  ".github/workflows/flowhive-psa-release-control-ci.yml",
  ".github/workflows/module025-governed-protected-test-release-manual.yml",
  "scripts/release-test/validate-protected-test-controller-branches.sh",
  "tests/flowhive-psa-admission.test.mjs",
  "tests/module025-installed-browser-harness.mjs",
  "tests/module025-register-browser-scope.mjs",
  "tests/module025-register-browser.test.py",
  "tests/module025-sow-register-browser.py"
];
const registrations = {
  ".github/workflows/module025-governed-protected-test-release-manual.yml": "          if [[ \"$HEAD_BRANCH\" == 'fix/module025-register-browser-acceptance-20260918' ]]; then\n            node tests/module025-register-browser-scope.mjs\n            node tests/validate-systemwide-image-build-controller.mjs\n            exit 0\n          fi\n",
  ".github/workflows/flowhive-psa-release-control-ci.yml": "          elif [[ \"$GITHUB_HEAD_REF\" == 'fix/module025-register-browser-acceptance-20260918' ]]; then\n            node tests/module025-register-browser-scope.mjs\n",
  "scripts/release-test/validate-protected-test-controller-branches.sh": "elif [[ \"$HEAD_BRANCH\" == 'fix/module025-register-browser-acceptance-20260918' ]]; then\n  node tests/module025-register-browser-scope.mjs\n  node tests/validate-systemwide-image-build-controller.mjs\n"
};
assert.equal(git('merge-base', base, 'HEAD'), base);
const verify = files => assert.deepEqual([...files].sort(), expected);
verify(git('diff', '--name-only', base).split(/\r?\n/));
for (const name of expected) assert.throws(() => verify(expected.filter(value => value !== name)));
for (const name of ['.github/workflows/projectpulse-deploy-test.yml', 'src/backend/ProjectTime.Api/Ai/CelarAiCapabilityRouting.cs'])
  assert.throws(() => verify([...expected, name]));
for (const [name, registration] of Object.entries(registrations)) {
  const source = fs.readFileSync(name, 'utf8');
  const restored = name.endsWith('flowhive-psa-release-control-ci.yml') ? source.replace("          \"$RUNNER_TEMP/module025-python-browser/bin/python\" tests/module025-register-browser.test.py\n", '') : source;
  assert.equal(restored.replace(registration, ''), original(name));
  if (name.endsWith('.yml')) {
    verifyReadOnlyWorkflow(source, name);
    assert.throws(() => verifyReadOnlyWorkflow(source.replace('contents: read', 'contents: write'), name));
  }
}
const admissionTest = 'tests/flowhive-psa-admission.test.mjs';
assert.equal(fs.readFileSync(admissionTest, 'utf8'), original(admissionTest)
  .replace("const module025WorkerAuthorization =", "const module025RegisterBrowser = process.env.GITHUB_HEAD_REF === 'fix/module025-register-browser-acceptance-20260918';\nconst module025WorkerAuthorization =")
  .replace("  || module025WorkerAuthorization ||", "  || module025RegisterBrowser || module025WorkerAuthorization ||")
  .replace("const module025VerifierBase = module025WorkerAuthorization ?", "const module025VerifierBase = module025RegisterBrowser ? '5dac14686c3052b8053f5f67189e02f10f07408d' : module025WorkerAuthorization ?")
  , 'Historical admission tests may only register this branch and exact main base');
for (const name of [
  '.github/workflows/projectpulse-deploy-test.yml', '.github/workflows/projectpulse-deploy-production.yml',
  '.github/workflows/module025-protected-uat-control.yml', '.github/flowhive-psa-protected-test-candidate.json',
  '.github/flowhive-psa-release-control-files.txt', 'scripts/release-test/flowhive-psa-admission.mjs',
  'scripts/release-test/run-module025-sow-gsd-protected-test-uat.sh'
]) assert.equal(fs.readFileSync(name, 'utf8'), original(name), `Release or acceptance authority changed: ${name}`);
assert.equal(git('diff', '--name-only', base, '--', 'deployment', 'database', 'src'), '');
console.log('MODULE025_REGISTER_BROWSER_SCOPE=PASS exact_files=8 application=unchanged deployment_authority=unchanged');
