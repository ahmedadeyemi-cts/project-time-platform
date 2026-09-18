import assert from 'node:assert/strict';
import fs from 'node:fs';
import { execFileSync } from 'node:child_process';
import { verifyReadOnlyWorkflow } from './flowhive-psa-scope.mjs';
const base = '734aa6d844a2ba2bc89e9f64d759db862ca4db58';
const git = (...args) => execFileSync('git', args, {encoding:'utf8'}).trim();
const original = name => execFileSync('git', ['show', `${base}:${name}`], {encoding:'utf8'});
const expected = [
  ".github/workflows/flowhive-psa-release-control-ci.yml",
  ".github/workflows/module025-governed-protected-test-release-manual.yml",
  "scripts/release-test/validate-protected-test-controller-branches.sh",
  "src/backend/ProjectTime.Api/Modules/Module025ProtectedTestUatAccess.cs",
  "src/backend/ProjectTime.Api/Modules/Module025SowGsdModule.cs",
  "tests/FlowHiveDetailedPlannerTests/Module025WorkerAuthorizationTests.cs",
  "tests/FlowHiveDetailedPlannerTests/Program.cs",
  "tests/flowhive-psa-admission.test.mjs",
  "tests/module025-worker-authorization-scope.mjs"
];
const registrations = {
  ".github/workflows/module025-governed-protected-test-release-manual.yml": "          if [[ \"$HEAD_BRANCH\" == 'fix/module025-fixture-generation-authorization-20260918' ]]; then\n            node tests/module025-worker-authorization-scope.mjs\n            node tests/validate-systemwide-image-build-controller.mjs\n            exit 0\n          fi\n",
  ".github/workflows/flowhive-psa-release-control-ci.yml": "          elif [[ \"$GITHUB_HEAD_REF\" == 'fix/module025-fixture-generation-authorization-20260918' ]]; then\n            node tests/module025-worker-authorization-scope.mjs\n",
  "scripts/release-test/validate-protected-test-controller-branches.sh": "elif [[ \"$HEAD_BRANCH\" == 'fix/module025-fixture-generation-authorization-20260918' ]]; then\n  node tests/module025-worker-authorization-scope.mjs\n  node tests/validate-systemwide-image-build-controller.mjs\n"
};
assert.equal(git('merge-base', base, 'HEAD'), base);
const verify = files => assert.deepEqual([...files].sort(), expected);
verify(git('diff', '--name-only', base).split(/\r?\n/));
for (const name of expected) assert.throws(() => verify(expected.filter(value => value !== name)));
for (const name of ['.github/workflows/projectpulse-deploy-test.yml', 'src/backend/ProjectTime.Api/Ai/CelarAiCapabilityRouting.cs'])
  assert.throws(() => verify([...expected, name]));
for (const [name, registration] of Object.entries(registrations)) {
  const source = fs.readFileSync(name, 'utf8');
  assert.equal(source.replace(registration, ''), original(name));
  if (name.endsWith('.yml')) {
    verifyReadOnlyWorkflow(source, name);
    assert.throws(() => verifyReadOnlyWorkflow(source.replace('contents: read', 'contents: write'), name));
  }
}
const admissionTest = 'tests/flowhive-psa-admission.test.mjs';
assert.equal(fs.readFileSync(admissionTest, 'utf8'), original(admissionTest)
  .replace('const module025ReviewTimingDelete =', "const module025WorkerAuthorization = process.env.GITHUB_HEAD_REF === 'fix/module025-fixture-generation-authorization-20260918';\nconst module025ReviewTimingDelete =")
  .replace('  || module025GenerationCorrection ||', '  || module025WorkerAuthorization || module025GenerationCorrection ||')
  .replace('const module025VerifierBase = module025ReviewTimingDelete ?', "const module025VerifierBase = module025WorkerAuthorization ? '734aa6d844a2ba2bc89e9f64d759db862ca4db58' : module025ReviewTimingDelete ?"),
  'Historical admission tests may only register this branch and exact main base');
for (const name of [
  '.github/workflows/projectpulse-deploy-test.yml', '.github/workflows/projectpulse-deploy-production.yml',
  '.github/workflows/module025-protected-uat-control.yml', '.github/flowhive-psa-protected-test-candidate.json',
  '.github/flowhive-psa-release-control-files.txt', 'scripts/release-test/flowhive-psa-admission.mjs',
  'scripts/release-test/run-module025-sow-gsd-protected-test-uat.sh'
]) assert.equal(fs.readFileSync(name, 'utf8'), original(name), `Release or acceptance authority changed: ${name}`);
assert.equal(git('diff', '--name-only', base, '--', 'deployment', 'database', 'src/backend/ProjectTime.Api/Ai'), '');
const access = fs.readFileSync('src/backend/ProjectTime.Api/Modules/Module025ProtectedTestUatAccess.cs', 'utf8');
const originalAccess = original('src/backend/ProjectTime.Api/Modules/Module025ProtectedTestUatAccess.cs');
assert.equal(access.slice(access.indexOf('    internal const string EnabledVariable')), originalAccess.slice(originalAccess.indexOf('    internal const string EnabledVariable')),
  'Original request authorization must remain unchanged');
const module = fs.readFileSync('src/backend/ProjectTime.Api/Modules/Module025SowGsdModule.cs', 'utf8');
assert.ok(module.indexOf('MatchesWorkerGrant(protectedTestUatGrant)) continue;') < module.indexOf('if (!await TryLockGenerationAsync'),
  'Worker binding must be checked before claiming the job');
assert.match(module, /catch \(UnauthorizedAccessException exception\) when \(exception.Message == "module025_generation_authority_revoked"\)/);
console.log('MODULE025_WORKER_AUTHORIZATION_SCOPE=PASS exact_files=9 request_authority=unchanged deployment_authority=unchanged');
