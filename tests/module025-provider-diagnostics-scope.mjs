import assert from 'node:assert/strict';
import fs from 'node:fs';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import { verifyReadOnlyWorkflow } from './flowhive-psa-scope.mjs';
const base = 'dd6403e4ba89a8d15fa6308b85a0d20994d6cd13';
const expected = [
  ".github/workflows/flowhive-psa-release-control-ci.yml",
  ".github/workflows/module033-project-forge-ci.yml",
  ".github/workflows/projectpulse-deploy-test.yml",
  "deployment/module025-qualification/Dockerfile",
  "docs/releases/2026-09-17-module025-provider-diagnostics.md",
  "scripts/ci/validate-celar-ai-enterprise-source-boundary.sh",
  "scripts/release-test/run-module025-installed-sa-uat.py",
  "scripts/release-test/run-module025-provider-qualification.py",
  "scripts/release-test/validate-protected-test-controller-branches.sh",
  "src/backend/ProjectTime.Api/Ai/CelarAiCapabilityRouting.cs",
  "src/backend/ProjectTime.Api/Ai/Module025ExternalSowAdapter.cs",
  "src/backend/ProjectTime.Api/Ai/Module025GenerationEngine.cs",
  "src/backend/ProjectTime.Api/Ai/Module025ProviderDiagnostics.cs",
  "src/backend/ProjectTime.Api/Ai/ProjectPulseAiContracts.cs",
  "src/backend/ProjectTime.Api/Ai/ProjectPulseAiRemoteProviders.cs",
  "src/backend/ProjectTime.Api/Ai/PulseAiEscalationSanitizer.cs",
  "src/backend/ProjectTime.Api/Modules/Module025SowGsdModule.cs",
  "src/frontend/project-time-web/scripts/validate-module025-sow-register.mjs",
  "src/frontend/project-time-web/src/module025/SowGsdAuthoringWorkspace.jsx",
  "src/frontend/project-time-web/src/module025/SowGsdWorkspace.jsx",
  "src/frontend/project-time-web/src/module025/SowRegister.jsx",
  "src/frontend/project-time-web/src/module025/protected-download.js",
  "tests/FlowHiveDetailedPlannerTests/Module025ExternalSowTests.cs",
  "tests/FlowHiveDetailedPlannerTests/Module025ProviderQualification.cs",
  "tests/FlowHiveDetailedPlannerTests/Program.cs",
  "tests/flowhive-psa-admission.test.mjs",
  "tests/flowhive-psa-installed-acceptance.test.py",
  "tests/flowhive-psa-release-workflow.test.py",
  "tests/module025-document-actions.test.mjs",
  "tests/module025-provider-diagnostics-scope.mjs",
  "tests/module025-provider-qualification.test.py",
  "tests/module025-scoped-deploy.test.py",
  "tests/module025_qualification_workflow.py",
  "tests/validate-celar-ai-pr630-consolidated.mjs"
];
function verifyPaths(actual) { assert.deepEqual([...actual].sort(), expected); }
export function verifyModule025ProviderDiagnosticsScope() {
  assert.equal(execFileSync('git', ['merge-base', base, 'HEAD'], {encoding:'utf8'}).trim(), base);
  const actual = execFileSync('git', ['diff', '--name-only', `${base}...HEAD`], {encoding:'utf8'}).trim().split(/\r?\n/);
  verifyPaths(actual);
  assert.throws(() => verifyPaths([...actual, '.github/workflows/projectpulse-deploy-production.yml']));
  assert.throws(() => verifyPaths(actual.slice(1)));
  for (const name of expected.filter(name => name.startsWith('.github/workflows/') && name !== '.github/workflows/projectpulse-deploy-test.yml'))
    verifyReadOnlyWorkflow(fs.readFileSync(name, 'utf8'), name);
  // This isolated qualification is an explicit manual Test operation. It is
  // separately validated, never treated as privileged PR CI.
  execFileSync('python3', ['tests/module025-provider-qualification.test.py'], {stdio: 'inherit'});
  for (const name of ['.github/workflows/flowhive-psa-installed-acceptance.yml',
    '.github/flowhive-psa-protected-test-candidate.json',
    '.github/flowhive-psa-release-control-files.txt',
    'scripts/release-test/flowhive-psa-admission.mjs'])
    assert.deepEqual(fs.readFileSync(name), execFileSync('git', ['show', `${base}:${name}`]),
      `Provider diagnostics cannot change deployment authority: ${name}`);
  for (const name of ['deployment/oracle-celar', 'src/backend/ProjectTime.Api/Ai/PulseAiPrivateModelClient.cs',
    'src/backend/ProjectTime.Api/Ai/ProjectPulseAiConfiguration.cs',
    'src/backend/ProjectTime.Api/Ai/ProjectPulseAiSecretStore.cs'])
    assert.equal(execFileSync('git', ['rev-parse', `HEAD:${name}`], {encoding:'utf8'}).trim(),
      execFileSync('git', ['rev-parse', `${base}:${name}`], {encoding:'utf8'}).trim(),
      `Cloud SOW release must not deploy or change the deferred Celar runtime: ${name}`);
  console.log('MODULE025_PROVIDER_DIAGNOSTICS_EXACT_SCOPE=PASS deployment_authority=unchanged celar_runtime=unchanged');
}
if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) verifyModule025ProviderDiagnosticsScope();
