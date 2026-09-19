import assert from 'node:assert/strict';
import fs from 'node:fs';
import { execFileSync } from 'node:child_process';
import { verifyReadOnlyWorkflow } from './flowhive-psa-scope.mjs';
const base = "42cb8e43216264bd5876afd993c066cad5728c1a";
const expected = [
  ".github/workflows/flowhive-psa-release-control-ci.yml",
  ".github/workflows/module025-governed-protected-test-release-manual.yml",
  "scripts/release-test/run-module025-installed-sa-uat.py",
  "scripts/release-test/validate-module025-governed-release.sh",
  "scripts/release-test/validate-protected-test-controller-branches.sh",
  "tests/flowhive-psa-admission.test.mjs",
  "tests/module025-register-browser.test.py",
  "tests/module025-retained-record-lookup-scope.mjs",
  "tests/module025-sow-register-browser.py"
];
const registrations = {
  "scripts/release-test/validate-module025-governed-release.sh": "if [[ \"$HEAD_BRANCH\" == 'fix/module025-retained-record-lookup-20260919' ]]; then\n  node tests/module025-retained-record-lookup-scope.mjs\n  node tests/validate-systemwide-image-build-controller.mjs\n  exit 0\nfi\n",
  ".github/workflows/flowhive-psa-release-control-ci.yml": "          elif [[ \"$GITHUB_HEAD_REF\" == 'fix/module025-retained-record-lookup-20260919' ]]; then\n            node tests/module025-retained-record-lookup-scope.mjs\n",
  "scripts/release-test/validate-protected-test-controller-branches.sh": "elif [[ \"$HEAD_BRANCH\" == 'fix/module025-retained-record-lookup-20260919' ]]; then\n  node tests/module025-retained-record-lookup-scope.mjs\n  node tests/validate-systemwide-image-build-controller.mjs\n",
  ".github/workflows/module025-governed-protected-test-release-manual.yml": "      - 'scripts/release-test/validate-module025-governed-release.sh'\n"
};
const replacements = {
  "const module025RetainedRegister =": "const module025RetainedLookup = process.env.GITHUB_HEAD_REF === 'fix/module025-retained-record-lookup-20260919';\nconst module025RetainedRegister =",
  "  || module025RetainedRegister ||": "  || module025RetainedLookup || module025RetainedRegister ||",
  "const module025VerifierBase = module025RetainedRegister ?": "const module025VerifierBase = module025RetainedLookup ? '42cb8e43216264bd5876afd993c066cad5728c1a' : module025RetainedRegister ?"
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
  if (name.endsWith('.yml')) verifyReadOnlyWorkflow(source, name);
}
let admission = original('tests/flowhive-psa-admission.test.mjs');
for (const [before, after] of Object.entries(replacements)) admission = admission.replace(before, after);
assert.equal(fs.readFileSync('tests/flowhive-psa-admission.test.mjs', 'utf8'), admission);
const sa = 'scripts/release-test/run-module025-installed-sa-uat.py';
assert.equal(fs.readFileSync(sa, 'utf8'), original(sa).replaceAll(
  '        save_payload = {\n            "expectedRevision": current.get("revision"),',
  '        save_payload = {\n            "expectedRevision": current.get("revision"),\n            "projectName": current.get("projectName"),'));
for (const name of [
  '.github/workflows/projectpulse-deploy-test.yml', '.github/workflows/projectpulse-deploy-production.yml',
  '.github/workflows/module025-protected-uat-control.yml', '.github/workflows/module025-retained-register-check.yml',
  '.github/flowhive-psa-protected-test-candidate.json', '.github/flowhive-psa-release-control-files.txt',
  'scripts/release-test/flowhive-psa-admission.mjs', 'scripts/release-test/authorize-module025-register-check.py',
  'scripts/release-test/run-module025-sow-gsd-protected-test-uat.sh'
]) assert.equal(fs.readFileSync(name, 'utf8'), original(name), `Release or acceptance authority changed: ${name}`);
assert.equal(git('diff', '--name-only', base, '--', 'deployment', 'database', 'src'), '');
console.log('MODULE025_RETAINED_RECORD_LOOKUP_SCOPE=PASS exact_files=9 business_authority=unchanged generation=unchanged');
