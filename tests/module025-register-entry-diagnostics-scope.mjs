import assert from 'node:assert/strict';
import fs from 'node:fs';
import { execFileSync } from 'node:child_process';
import { verifyReadOnlyWorkflow } from './flowhive-psa-scope.mjs';
const base = 'edf957df33b3cd22b91e20ca71f33c12811ceb72';
const expected = [
  ".github/workflows/flowhive-psa-release-control-ci.yml",
  ".github/workflows/module025-governed-protected-test-release-manual.yml",
  "scripts/release-test/validate-protected-test-controller-branches.sh",
  "tests/flowhive-psa-admission.test.mjs",
  "tests/module025-installed-browser-harness.mjs",
  "tests/module025-register-browser.test.py",
  "tests/module025-register-entry-diagnostics-scope.mjs",
  "tests/module025-sow-register-browser.py"
];
const registrations = {
  ".github/workflows/module025-governed-protected-test-release-manual.yml": "          if [[ \"$HEAD_BRANCH\" == 'fix/module025-register-entry-diagnostics-20260919' ]]; then\n            node tests/module025-register-entry-diagnostics-scope.mjs\n            node tests/validate-systemwide-image-build-controller.mjs\n            exit 0\n          fi\n",
  ".github/workflows/flowhive-psa-release-control-ci.yml": "          elif [[ \"$GITHUB_HEAD_REF\" == 'fix/module025-register-entry-diagnostics-20260919' ]]; then\n            node tests/module025-register-entry-diagnostics-scope.mjs\n",
  "scripts/release-test/validate-protected-test-controller-branches.sh": "elif [[ \"$HEAD_BRANCH\" == 'fix/module025-register-entry-diagnostics-20260919' ]]; then\n  node tests/module025-register-entry-diagnostics-scope.mjs\n  node tests/validate-systemwide-image-build-controller.mjs\n"
};
const replacements = {
  "const module025RegisterStartup =": "const module025RegisterEntry = process.env.GITHUB_HEAD_REF === 'fix/module025-register-entry-diagnostics-20260919';\nconst module025RegisterStartup =",
  "  || module025RegisterStartup ||": "  || module025RegisterEntry || module025RegisterStartup ||",
  "const module025VerifierBase = module025RegisterStartup ?": "const module025VerifierBase = module025RegisterEntry ? 'edf957df33b3cd22b91e20ca71f33c12811ceb72' : module025RegisterStartup ?"
};
const git = (...args) => execFileSync('git', args, {encoding:'utf8'}).trim();
const original = name => execFileSync('git', ['show', `${base}:${name}`], {encoding:'utf8'});
assert.equal(git('merge-base', base, 'HEAD'), base);
const verify = files => assert.deepEqual([...files].sort(), expected);
verify(git('diff', '--name-only', base).split(/\r?\n/));
for (const name of expected) assert.throws(() => verify(expected.filter(value => value !== name)));
assert.throws(() => verify([...expected, '.github/workflows/projectpulse-deploy-test.yml']));
for (const [name, registration] of Object.entries(registrations)) {
  const source = fs.readFileSync(name, 'utf8');
  assert.equal(source.replace(registration, ''), original(name));
  if (name.endsWith('.yml')) {
    verifyReadOnlyWorkflow(source, name);
    assert.throws(() => verifyReadOnlyWorkflow(source.replace('contents: read', 'contents: write'), name));
  }
}
let admission = original('tests/flowhive-psa-admission.test.mjs');
for (const [before, after] of Object.entries(replacements)) admission = admission.replace(before, after);
assert.equal(fs.readFileSync('tests/flowhive-psa-admission.test.mjs', 'utf8'), admission);
for (const name of [
  '.github/workflows/projectpulse-deploy-test.yml', '.github/workflows/projectpulse-deploy-production.yml',
  '.github/workflows/module025-protected-uat-control.yml', '.github/flowhive-psa-protected-test-candidate.json',
  '.github/flowhive-psa-release-control-files.txt', 'scripts/release-test/flowhive-psa-admission.mjs',
  'scripts/release-test/run-module025-sow-gsd-protected-test-uat.sh'
]) assert.equal(fs.readFileSync(name, 'utf8'), original(name), `Release or acceptance authority changed: ${name}`);
assert.equal(git('diff', '--name-only', base, '--', 'deployment', 'database', 'src'), '');
console.log('MODULE025_REGISTER_ENTRY_SCOPE=PASS exact_files=8 application=unchanged deployment_authority=unchanged');
