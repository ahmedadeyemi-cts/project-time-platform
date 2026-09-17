import assert from 'node:assert/strict';
import fs from 'node:fs';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import { verifyReadOnlyWorkflow } from './flowhive-psa-scope.mjs';

const base = '5f2f520890309db20d3897f56c09c1ae194a4e33';
const expected = [
  '.github/workflows/flowhive-psa-release-control-ci.yml',
  '.github/workflows/module025-governed-protected-test-release-manual.yml',
  'docs/releases/2026-09-17-module025-output-budget-preflight.md',
  'scripts/ci/validate-celar-ai-enterprise-source-boundary.sh',
  'scripts/release-test/run-module025-provider-qualification.py',
  'scripts/release-test/validate-protected-test-controller-branches.sh',
  'src/backend/ProjectTime.Api/Ai/Module025ExternalSowAdapter.cs',
  'src/backend/ProjectTime.Api/Ai/Module025GenerationEngine.cs',
  'src/backend/ProjectTime.Api/Ai/ProjectPulseAiRemoteProviders.cs',
  'src/backend/ProjectTime.Api/Ai/PulseAiPrivateRagService.cs',
  'tests/FlowHiveDetailedPlannerTests/Module025ExternalSowTests.cs',
  'tests/FlowHiveDetailedPlannerTests/Module025GenerationEngineTests.cs',
  'tests/FlowHiveDetailedPlannerTests/Module025ProviderQualification.cs',
  'tests/flowhive-psa-admission.test.mjs',
  'tests/module025-output-budget-preflight-scope.mjs',
  'tests/module025-provider-qualification.test.py',
  'tests/validate-celar-ai-pr630-consolidated.mjs',
  'tests/validate-systemwide-image-build-controller.mjs'
].sort();

export function verifyModule025OutputBudgetPreflightScope() {
  assert.equal(execFileSync('git', ['merge-base', base, 'HEAD'], { encoding: 'utf8' }).trim(), base);
  const actual = execFileSync('git', ['diff', '--name-only', `${base}...HEAD`], { encoding: 'utf8' }).trim().split(/\r?\n/);
  const verify = files => assert.deepEqual([...files].sort(), expected);
  verify(actual);
  assert.throws(() => verify([...actual, '.github/workflows/projectpulse-deploy-production.yml']));
  assert.throws(() => verify(actual.slice(1)));
  for (const file of expected.filter(file => file.startsWith('.github/workflows/')))
    verifyReadOnlyWorkflow(fs.readFileSync(file, 'utf8'), file);
  for (const file of [
    '.github/flowhive-psa-protected-test-candidate.json',
    '.github/flowhive-psa-release-control-files.txt',
    'scripts/release-test/flowhive-psa-admission.mjs',
    'scripts/release-test/dispatch-flowhive-psa-test.mjs',
    'scripts/validate-deployment-concurrency-governance.mjs',
    '.github/workflows/projectpulse-deploy-test.yml',
    '.github/workflows/projectpulse-deploy-production.yml',
    '.github/workflows/flowhive-psa-installed-acceptance.yml'
  ]) assert.deepEqual(fs.readFileSync(file), execFileSync('git', ['show', `${base}:${file}`]), `Deployment authority changed: ${file}`);
  for (const name of [
    'deployment', 'database',
    'src/backend/ProjectTime.Api/Ai/PulseAiPrivateModelClient.cs',
    'src/backend/ProjectTime.Api/Ai/ProjectPulseAiConfiguration.cs',
    'src/backend/ProjectTime.Api/Ai/ProjectPulseAiSecretStore.cs'
  ]) assert.equal(execFileSync('git', ['rev-parse', `HEAD:${name}`], { encoding: 'utf8' }),
    execFileSync('git', ['rev-parse', `${base}:${name}`], { encoding: 'utf8' }), `Out-of-scope change: ${name}`);
  console.log('MODULE025_OUTPUT_BUDGET_PREFLIGHT_SCOPE=PASS deployment_authority=unchanged celar_runtime=unchanged');
}
if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) verifyModule025OutputBudgetPreflightScope();
