import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { execFileSync } from 'node:child_process';
import { verifyReadOnlyWorkflow } from './flowhive-psa-scope.mjs';
const base = "79614e06d920c67c5a937bd6f64aa54223e4c256";
const expected = [
  ".github/workflows/flowhive-psa-release-control-ci.yml",
  ".github/workflows/projectpulse-deploy-test.yml",
  "scripts/ci/validate-celar-ai-enterprise-source-boundary.sh",
  "scripts/release-test/run-module025-installed-sa-uat.py",
  "scripts/release-test/run-module025-sow-gsd-protected-test-uat.sh",
  "scripts/release-test/validate-module025-governed-release.sh",
  "scripts/release-test/validate-protected-test-controller-branches.sh",
  "scripts/release-test/verify-module025-sa-register-evidence.py",
  "tests/flowhive-psa-admission.test.mjs",
  "tests/flowhive-psa-installed-acceptance.test.py",
  "tests/module025-normal-sa-register-gate-scope.mjs",
  "tests/module025-register-browser.test.py",
  "tests/module025-sow-register-browser.py",
  "tests/validate-celar-ai-pr630-consolidated.mjs"
];
const registrations = {
  "scripts/release-test/validate-module025-governed-release.sh": "if [[ \"$HEAD_BRANCH\" == 'fix/module025-normal-sa-register-gate-20260919' ]]; then\n  node tests/module025-normal-sa-register-gate-scope.mjs\n  node tests/validate-systemwide-image-build-controller.mjs\n  exit 0\nfi\n",
  ".github/workflows/flowhive-psa-release-control-ci.yml": "          elif [[ \"$GITHUB_HEAD_REF\" == 'fix/module025-normal-sa-register-gate-20260919' ]]; then\n            node tests/module025-normal-sa-register-gate-scope.mjs\n",
  "scripts/release-test/validate-protected-test-controller-branches.sh": "elif [[ \"$HEAD_BRANCH\" == 'fix/module025-normal-sa-register-gate-20260919' ]]; then\n  node tests/module025-normal-sa-register-gate-scope.mjs\n  node tests/validate-systemwide-image-build-controller.mjs\n",
  "scripts/ci/validate-celar-ai-enterprise-source-boundary.sh": "if [[ \"$HEAD_BRANCH\" == 'fix/module025-normal-sa-register-gate-20260919' ]]; then\n  node tests/module025-normal-sa-register-gate-scope.mjs\n  PROHIBITED=\"$(grep -Fvx '.github/workflows/projectpulse-deploy-test.yml' <<<\"$PROHIBITED\" || true)\"\nfi\n"
};
const replacements = {
  "const module025RetainedLookup =": "const module025NormalSaRegister = process.env.GITHUB_HEAD_REF === 'fix/module025-normal-sa-register-gate-20260919';\nconst module025RetainedLookup =",
  "  || module025RetainedLookup ||": "  || module025NormalSaRegister || module025RetainedLookup ||",
  "const module025VerifierBase = module025RetainedLookup ?": "const module025VerifierBase = module025NormalSaRegister ? '79614e06d920c67c5a937bd6f64aa54223e4c256' : module025RetainedLookup ?",
  "      const baseBytes = execFileSync('git', ['show', `${module025VerifierBase}:${path}`]);\n      assert.deepEqual(fs.readFileSync(new URL(`../${path}`, import.meta.url)), baseBytes,": "      const baseBytes = execFileSync('git', ['show', `${module025VerifierBase}:${path}`]);\n      const expectedBytes = module025NormalSaRegister && path === '.github/workflows/projectpulse-deploy-test.yml'\n        ? Buffer.from(baseBytes.toString().replace(\n          '          MODULE025_UAT_EXPIRES_AT: ${{ steps.module025_fixture.outputs.expires_at }}\\n',\n          \"          MODULE025_UAT_EXPIRES_AT: ${{ steps.module025_fixture.outputs.expires_at }}\\n          MODULE025_NORMAL_SA_REGISTER_REQUIRED: ${{ inputs.acceptance_scope == 'sow_role' }}\\n\")) : baseBytes;\n      assert.deepEqual(fs.readFileSync(new URL(`../${path}`, import.meta.url)), expectedBytes,"
};
const compatibility = {
  "const module025RetainedRegisterMode =": "const module025NormalSaRegisterMode = branchName === 'fix/module025-normal-sa-register-gate-20260919';\nif (module025NormalSaRegisterMode) (await import('./module025-normal-sa-register-gate-scope.mjs')).verifyModule025NormalSaRegisterScope();\nconst module025RetainedRegisterMode =",
  "const scopedCompatibilityMode = module025RetainedRegisterMode ||": "const scopedCompatibilityMode = module025NormalSaRegisterMode || module025RetainedRegisterMode ||"
};

const git = (...args) => execFileSync('git', args, {encoding:'utf8'}).trim();
const original = name => execFileSync('git', ['show', `${base}:${name}`], {encoding:'utf8'});
export function verifyModule025NormalSaRegisterScope() {
  assert.equal(git('merge-base', base, 'HEAD'), base);
  const verify = files => assert.deepEqual([...files].sort(), expected);
  verify(git('diff', '--name-only', base).split(/\r?\n/));
  for (const name of expected) assert.throws(() => verify(expected.filter(value => value !== name)));
  assert.throws(() => verify([...expected, '.github/workflows/projectpulse-deploy-production.yml']));
  for (const [name, registration] of Object.entries(registrations)) {
    const source = fs.readFileSync(name, 'utf8');
    assert.equal(source.replace(registration, ''), original(name));
    if (name.endsWith('.yml')) verifyReadOnlyWorkflow(source, name);
  }
  for (const [name, changes] of [
    ['tests/flowhive-psa-admission.test.mjs', replacements],
    ['tests/validate-celar-ai-pr630-consolidated.mjs', compatibility]
  ]) {
    let source = original(name);
    for (const [before, after] of Object.entries(changes)) source = source.replace(before, after);
    assert.equal(fs.readFileSync(name, 'utf8'), source);
  }
  const controller = '.github/workflows/projectpulse-deploy-test.yml';
  const flag = "          MODULE025_NORMAL_SA_REGISTER_REQUIRED: ${{ inputs.acceptance_scope == 'sow_role' }}\n";
  assert.equal(fs.readFileSync(controller,'utf8').replace(flag,''), original(controller));
  for (const name of [
    '.github/workflows/projectpulse-deploy-production.yml', '.github/workflows/module025-protected-uat-control.yml',
    '.github/workflows/module025-retained-register-check.yml', '.github/flowhive-psa-protected-test-candidate.json',
    '.github/flowhive-psa-release-control-files.txt', 'scripts/release-test/flowhive-psa-admission.mjs',
    'scripts/release-test/authorize-module025-register-check.py'
  ]) assert.equal(fs.readFileSync(name, 'utf8'), original(name), `Release authority changed: ${name}`);
  assert.equal(git('diff', '--name-only', base, '--', 'deployment', 'database', 'src'), '');
  const fixture = fs.readFileSync('scripts/release-test/run-module025-sow-gsd-protected-test-uat.sh','utf8');
  assert.ok(fixture.indexOf('verify-module025-sa-register-evidence.py') < fixture.indexOf('WORK_DIR='));
  assert.match(fixture, /MODULE025_RETAINED_VERSION_BROWSER_LIFECYCLE=PASS/);
  const sa = fs.readFileSync('scripts/release-test/run-module025-installed-sa-uat.py','utf8');
  assert.match(sa, /await verify_normal_sa_register\(report, os.environ.get\('GITHUB_RUN_ID', ''\)\)/);
  assert.match(sa, /report\["fullRequestedScopePassed"\] = False/);
  console.log('MODULE025_NORMAL_SA_REGISTER_GATE_SCOPE=PASS exact_files=' + expected.length + ' deployment_authority=unchanged browser_gate=actual_sa');
}
if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) verifyModule025NormalSaRegisterScope();
