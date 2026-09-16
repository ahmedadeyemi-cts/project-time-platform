import assert from 'node:assert/strict';
import fs from 'node:fs';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import { verifyReadOnlyWorkflow } from './flowhive-psa-scope.mjs';
const base = 'e632c8d24549339a8476b9b50a8411b4d253ff02';
const expected = [
  ".github/workflows/celar-ai-oracle-gitops-ci.yml",
  ".github/workflows/flowhive-psa-release-control-ci.yml",
  "deployment/oracle-celar/gateway/wsgi.py",
  "deployment/oracle-celar/release.json",
  "docs/releases/2026-09-16-module025-external-sow-and-celar-deadline.md",
  "scripts/release-test/collect-celar-runtime-evidence.py",
  "scripts/release-test/validate-protected-test-controller-branches.sh",
  "src/backend/ProjectTime.Api/Ai/CelarAiCapabilityRouting.cs",
  "src/backend/ProjectTime.Api/Ai/CelarAiEnterprisePlatformService.cs",
  "src/backend/ProjectTime.Api/Ai/Module025ExternalSowAdapter.cs",
  "src/backend/ProjectTime.Api/Ai/Module025GenerationEngine.cs",
  "src/backend/ProjectTime.Api/Ai/ProjectPulseAiContracts.cs",
  "src/backend/ProjectTime.Api/Ai/ProjectPulseAiRemoteProviders.cs",
  "src/backend/ProjectTime.Api/Ai/PulseAiPrivateModelClient.cs",
  "src/backend/ProjectTime.Api/Ai/PulseAiPrivateRagService.cs",
  "src/backend/ProjectTime.Api/Modules/Module025SowGsdModule.cs",
  "tests/FlowHiveDetailedPlannerTests/Module025ExternalSowTests.cs",
  "tests/FlowHiveDetailedPlannerTests/Module025ProviderQualification.cs",
  "tests/FlowHiveDetailedPlannerTests/Program.cs",
  "tests/flowhive-psa-admission.test.mjs",
  "tests/module025-external-sow-scope.mjs",
  "tests/test-celar-runtime-evidence.py",
  "tests/test-celar-sow-runtime-deadlines.py",
  "tests/validate-celar-ai-pr630-consolidated.mjs"
];
function verifyPaths(actual) { assert.deepEqual([...actual].sort(), expected); }
export function verifyModule025ExternalScope() {
  assert.equal(execFileSync('git', ['merge-base', base, 'HEAD'], {encoding:'utf8'}).trim(), base);
  const actual = execFileSync('git', ['diff', '--name-only', `${base}...HEAD`], {encoding:'utf8'}).trim().split(/\r?\n/);
  verifyPaths(actual);
  assert.throws(() => verifyPaths([...actual, '.github/workflows/projectpulse-deploy-test.yml']));
  assert.throws(() => verifyPaths(actual.slice(1)));
  for (const name of expected.filter(name => name.startsWith('.github/workflows/')))
    verifyReadOnlyWorkflow(fs.readFileSync(name, 'utf8'), name);
  for (const name of ['.github/workflows/projectpulse-deploy-test.yml',
    '.github/workflows/flowhive-psa-installed-acceptance.yml',
    '.github/flowhive-psa-protected-test-candidate.json',
    '.github/flowhive-psa-release-control-files.txt',
    'scripts/release-test/flowhive-psa-admission.mjs'])
    assert.deepEqual(fs.readFileSync(name), execFileSync('git', ['show', `${base}:${name}`]),
      `External SOW adapters cannot change deployment authority: ${name}`);
  console.log('MODULE025_EXTERNAL_SOW_EXACT_SCOPE=PASS deployment_authority=unchanged');
}
if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) verifyModule025ExternalScope();
