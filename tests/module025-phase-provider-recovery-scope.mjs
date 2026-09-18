import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { execFileSync } from 'node:child_process';
import { verifyReadOnlyWorkflow } from './flowhive-psa-scope.mjs';
const base = '66e373ab1b3005e3e542683261164f5915f170a0';
const expected = [
  ".github/workflows/flowhive-psa-release-control-ci.yml",
  ".github/workflows/module025-governed-protected-test-release-manual.yml",
  "scripts/ci/validate-celar-ai-enterprise-source-boundary.sh",
  "scripts/release-test/validate-protected-test-controller-branches.sh",
  "src/backend/ProjectTime.Api/Ai/Module025ExternalSowAdapter.cs",
  "src/backend/ProjectTime.Api/Ai/Module025GenerationEngine.cs",
  "src/backend/ProjectTime.Api/Ai/Module025PhaseOutputContract.cs",
  "src/backend/ProjectTime.Api/Ai/ProjectPulseAiRemoteProviders.cs",
  "tests/FlowHiveDetailedPlannerTests/Module025ExternalSowTests.cs",
  "tests/FlowHiveDetailedPlannerTests/Module025GenerationEngineTests.cs",
  "tests/FlowHiveDetailedPlannerTests/Module025ProviderFallbackTests.cs",
  "tests/FlowHiveDetailedPlannerTests/Module025StructuredContractTests.cs",
  "tests/flowhive-psa-admission.test.mjs",
  "tests/flowhive-psa-release-workflow.test.py",
  "tests/module025-phase-provider-recovery-scope.mjs",
  "tests/validate-celar-ai-pr630-consolidated.mjs"
];
const registrations = {
  ".github/workflows/module025-governed-protected-test-release-manual.yml": "          if [[ \"$HEAD_BRANCH\" == 'fix/module025-phase-provider-recovery-20260918' ]]; then\n            node tests/module025-phase-provider-recovery-scope.mjs\n            node tests/validate-systemwide-image-build-controller.mjs\n            exit 0\n          fi\n",
  ".github/workflows/flowhive-psa-release-control-ci.yml": "          elif [[ \"$GITHUB_HEAD_REF\" == 'fix/module025-phase-provider-recovery-20260918' ]]; then\n            node tests/module025-phase-provider-recovery-scope.mjs\n",
  "scripts/release-test/validate-protected-test-controller-branches.sh": "elif [[ \"$HEAD_BRANCH\" == 'fix/module025-phase-provider-recovery-20260918' ]]; then\n  node tests/module025-phase-provider-recovery-scope.mjs\n  node tests/validate-systemwide-image-build-controller.mjs\n",
  "scripts/ci/validate-celar-ai-enterprise-source-boundary.sh": "if [[ \"$HEAD_BRANCH\" == 'fix/module025-phase-provider-recovery-20260918' ]]; then\n  node tests/module025-phase-provider-recovery-scope.mjs\n  PROHIBITED=\"$(grep -Fvx 'src/backend/ProjectTime.Api/Ai/ProjectPulseAiRemoteProviders.cs' <<<\"$PROHIBITED\" || true)\"\nfi\n"
};

const git = (...args) => execFileSync('git', args, {encoding:'utf8'}).trim();
const original = name => execFileSync('git', ['show', `${base}:${name}`], {encoding:'utf8'});
export function verifyModule025PhaseProviderRecoveryScope() {
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
  assert.equal(fs.readFileSync("tests/flowhive-psa-admission.test.mjs", 'utf8'), original("tests/flowhive-psa-admission.test.mjs")
    .replace("const module025RegisterBrowser =", "const module025PhaseProviderRecovery = process.env.GITHUB_HEAD_REF === 'fix/module025-phase-provider-recovery-20260918';\nconst module025RegisterBrowser =")
    .replace("  || module025RegisterBrowser ||", "  || module025PhaseProviderRecovery || module025RegisterBrowser ||")
    .replace("const module025VerifierBase = module025RegisterBrowser ?", "const module025VerifierBase = module025PhaseProviderRecovery ? '66e373ab1b3005e3e542683261164f5915f170a0' : module025RegisterBrowser ?")
  , 'Only exact branch and base registration is allowed');
  assert.equal(fs.readFileSync("tests/validate-celar-ai-pr630-consolidated.mjs", 'utf8'), original("tests/validate-celar-ai-pr630-consolidated.mjs")
    .replace("const module025ReviewTimingDeleteMode =", "const module025PhaseProviderRecoveryMode = branchName === 'fix/module025-phase-provider-recovery-20260918';\nif (module025PhaseProviderRecoveryMode) (await import('./module025-phase-provider-recovery-scope.mjs')).verifyModule025PhaseProviderRecoveryScope();\nconst module025ReviewTimingDeleteMode =")
    .replace("const scopedCompatibilityMode = module025ReviewTimingDeleteMode ||", "const scopedCompatibilityMode = module025PhaseProviderRecoveryMode || module025ReviewTimingDeleteMode ||")
  , 'Only exact branch and base registration is allowed');
  assert.equal(fs.readFileSync('tests/flowhive-psa-release-workflow.test.py', 'utf8'), original('tests/flowhive-psa-release-workflow.test.py').replace("            self.assertEqual(controller_script.count('\"$PR_NUMBER\"'), 2)", "            self.assertEqual([line.strip() for line in controller_script.splitlines() if '\"$PR_NUMBER\"' in line], [\n                \"[[ \\\"$PR_NUMBER\\\" == '1090' ]] || fail 'Module 019 scope is registered only for PR #1090.'\",\n                \"elif [[ \\\"$PR_NUMBER\\\" == '734' && -f .github/flowhive-pr734-governed-release-files.txt ]]; then\",\n                \"elif [[ \\\"$PR_NUMBER\\\" == '777' ]]; then\",\n            ] if 'source scripts/release-test/validate-protected-test-controller-branches.sh' in controller_script\n                else ['[[ \"$PR_NUMBER\" == \"$PR_NUMBER\" ]]'])"), 'Only the exact existing PR-number guards may be registered');
  for (const name of [
    '.github/workflows/projectpulse-deploy-test.yml', '.github/workflows/projectpulse-deploy-production.yml',
    '.github/workflows/module025-protected-uat-control.yml', '.github/flowhive-psa-protected-test-candidate.json',
    '.github/flowhive-psa-release-control-files.txt', 'scripts/release-test/flowhive-psa-admission.mjs',
    'scripts/release-test/run-module025-sow-gsd-protected-test-uat.sh',
    'src/backend/ProjectTime.Api/Ai/PulseAiEscalationSanitizer.cs',
    'src/backend/ProjectTime.Api/Ai/CelarAiCapabilityRouting.cs',
    'src/backend/ProjectTime.Api/Ai/PulseAiPrivateRagService.cs'
  ]) assert.equal(fs.readFileSync(name, 'utf8'), original(name), `Protected policy changed: ${name}`);
  assert.equal(git('diff', '--name-only', base, '--', 'deployment', 'database', 'src/frontend', 'src/backend/ProjectTime.Api/Modules'), '');
  const remote = 'src/backend/ProjectTime.Api/Ai/ProjectPulseAiRemoteProviders.cs';
  const remoteSource = fs.readFileSync(remote, 'utf8');
  // Claude request construction is the only remote-provider change. HTTP,
  // refusals, response handling, OpenAI and other providers remain identical.
  const boundary = '        var response = await ProjectPulseAiHttp.SendWithRetryAsync(';
  assert.equal(remoteSource.slice(remoteSource.indexOf(boundary)), original(remote).slice(original(remote).indexOf(boundary)));
  const adapter = 'src/backend/ProjectTime.Api/Ai/Module025ExternalSowAdapter.cs';
  const adapterSource = fs.readFileSync(adapter, 'utf8');
  const validation = '    internal bool Validate(';
  assert.equal(adapterSource.slice(adapterSource.indexOf(validation)), original(adapter).slice(original(adapter).indexOf(validation)));
  const engine = fs.readFileSync('src/backend/ProjectTime.Api/Ai/Module025GenerationEngine.cs', 'utf8');
  assert.match(engine, /AttemptsPerPhase = 4;/);
  for (const [key, value] of Object.entries({DeadlineSeconds:1200, ProviderTimeoutSeconds:180, ExternalProviderTimeoutSeconds:120, MaximumOutputTokens:6144, MaximumExternalOutputTokens:12288}))
    assert.match(engine, new RegExp(`${key} = ${value};`));
  console.log('MODULE025_PHASE_PROVIDER_RECOVERY_SCOPE=PASS exact_files=16 privacy=unchanged deadlines=unchanged deployment_authority=unchanged');
}
if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) verifyModule025PhaseProviderRecoveryScope();
