import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { execFileSync } from 'node:child_process';
import { verifyReadOnlyWorkflow } from './flowhive-psa-scope.mjs';
const base = "140f42205fbabb3452463fa66bd37d0d29a5a5aa";
const expected = [
  ".github/workflows/flowhive-psa-release-control-ci.yml",
  ".github/workflows/module025-governed-protected-test-release-manual.yml",
  ".github/workflows/module025-retained-register-check.yml",
  "scripts/release-test/authorize-module025-register-check.py",
  "scripts/release-test/validate-protected-test-controller-branches.sh",
  "src/backend/ProjectTime.Api/Ai/CelarAiCapabilityRouting.cs",
  "tests/FlowHiveDetailedPlannerTests/Module025ExternalSowTests.cs",
  "tests/FlowHiveDetailedPlannerTests/Module025ProviderFallbackTests.cs",
  "tests/flowhive-psa-admission.test.mjs",
  "tests/module025-installed-browser-harness.mjs",
  "tests/module025-register-browser.test.py",
  "tests/module025-retained-register-control.test.py",
  "tests/module025-retained-register-scope.mjs",
  "tests/module025-sow-register-browser.py",
  "tests/validate-celar-ai-pr630-consolidated.mjs"
];
const registrations = {
  ".github/workflows/module025-governed-protected-test-release-manual.yml": "          if [[ \"$HEAD_BRANCH\" == 'fix/module025-retained-register-verification-20260919' ]]; then\n            node tests/module025-retained-register-scope.mjs\n            node tests/validate-systemwide-image-build-controller.mjs\n            exit 0\n          fi\n",
  ".github/workflows/flowhive-psa-release-control-ci.yml": "          elif [[ \"$GITHUB_HEAD_REF\" == 'fix/module025-retained-register-verification-20260919' ]]; then\n            node tests/module025-retained-register-scope.mjs\n            python3 tests/module025-retained-register-control.test.py\n",
  "scripts/release-test/validate-protected-test-controller-branches.sh": "elif [[ \"$HEAD_BRANCH\" == 'fix/module025-retained-register-verification-20260919' ]]; then\n  node tests/module025-retained-register-scope.mjs\n  node tests/validate-systemwide-image-build-controller.mjs\n"
};
const replacements = {
  "const module025RegisterEntry =": "const module025RetainedRegister = process.env.GITHUB_HEAD_REF === 'fix/module025-retained-register-verification-20260919';\nconst module025RegisterEntry =",
  "  || module025RegisterEntry ||": "  || module025RetainedRegister || module025RegisterEntry ||",
  "const module025VerifierBase = module025RegisterEntry ?": "const module025VerifierBase = module025RetainedRegister ? '140f42205fbabb3452463fa66bd37d0d29a5a5aa' : module025RegisterEntry ?"
};
const compatibility = {
  "const module025RegisterStartupMode =": "const module025RetainedRegisterMode = branchName === 'fix/module025-retained-register-verification-20260919';\nif (module025RetainedRegisterMode) (await import('./module025-retained-register-scope.mjs')).verifyModule025RetainedRegisterScope();\nconst module025RegisterStartupMode =",
  "const scopedCompatibilityMode = module025RegisterStartupMode ||": "const scopedCompatibilityMode = module025RetainedRegisterMode || module025RegisterStartupMode ||"
};
const git = (...args) => execFileSync('git', args, {encoding:'utf8'}).trim();
const original = name => execFileSync('git', ['show', `${base}:${name}`], {encoding:'utf8'});
export function verifyModule025RetainedRegisterScope() {
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
  for (const [name, changes] of [
    ['tests/flowhive-psa-admission.test.mjs', replacements],
    ['tests/validate-celar-ai-pr630-consolidated.mjs', compatibility]
  ]) {
    let source = original(name);
    for (const [before, after] of Object.entries(changes)) source = source.replace(before, after);
    assert.equal(fs.readFileSync(name, 'utf8'), source);
  }
  for (const name of [
    '.github/workflows/projectpulse-deploy-test.yml', '.github/workflows/projectpulse-deploy-production.yml',
    '.github/workflows/module025-protected-uat-control.yml', '.github/flowhive-psa-protected-test-candidate.json',
    '.github/flowhive-psa-release-control-files.txt', 'scripts/release-test/flowhive-psa-admission.mjs',
    'scripts/release-test/run-module025-sow-gsd-protected-test-uat.sh',
    'src/backend/ProjectTime.Api/Ai/Module025GenerationEngine.cs',
    'src/backend/ProjectTime.Api/Ai/Module025ExternalSowAdapter.cs',
    'src/backend/ProjectTime.Api/Ai/PulseAiEscalationSanitizer.cs'
  ]) assert.equal(fs.readFileSync(name, 'utf8'), original(name), `Protected authority or validation changed: ${name}`);
  assert.equal(git('diff', '--name-only', base, '--', 'deployment', 'database', 'src/frontend', 'src/backend/ProjectTime.Api/Modules'), '');
  const router = fs.readFileSync('src/backend/ProjectTime.Api/Ai/CelarAiCapabilityRouting.cs', 'utf8');
  const start = '        static bool IsPrivateTarget';
  const end = '                // A cloud target needs';
  assert.equal(router.slice(0,router.indexOf(start)), original('src/backend/ProjectTime.Api/Ai/CelarAiCapabilityRouting.cs').split(start)[0]);
  assert.equal(router.slice(router.indexOf(end)), original('src/backend/ProjectTime.Api/Ai/CelarAiCapabilityRouting.cs').slice(original('src/backend/ProjectTime.Api/Ai/CelarAiCapabilityRouting.cs').indexOf(end)));
  assert.match(router, /!RuntimeFlag\("PROJECTPULSE_MODULE025_PAID_FALLBACK_ENABLED"\)/);
  assert.ok(!router.includes('externalSowReady'));
  console.log('MODULE025_RETAINED_REGISTER_SCOPE=PASS exact_files=' + expected.length + ' deployment_authority=unchanged paid_sow_default=disabled');
}
if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) verifyModule025RetainedRegisterScope();
