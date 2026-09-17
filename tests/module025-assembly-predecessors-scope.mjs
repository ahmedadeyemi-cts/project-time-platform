import assert from 'node:assert/strict';
import fs from 'node:fs';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import { verifyReadOnlyWorkflow } from './flowhive-psa-scope.mjs';

const base = '5205140a5b12cbb123cc3dff5ef4c8200e22e98c';
const expected = [
  '.github/workflows/flowhive-psa-release-control-ci.yml',
  'docs/releases/2026-09-17-module025-assembly-predecessors.md',
  'scripts/release-test/validate-protected-test-controller-branches.sh',
  'src/backend/ProjectTime.Api/Ai/Module025GenerationEngine.cs',
  'src/backend/ProjectTime.Api/Ai/PulseAiPrivateRagService.cs',
  'tests/FlowHiveDetailedPlannerTests/Module025GenerationEngineTests.cs',
  'tests/flowhive-psa-admission.test.mjs',
  'tests/module025-assembly-predecessors-scope.mjs',
  'tests/validate-celar-ai-pr630-consolidated.mjs'
].sort();

export function verifyModule025AssemblyPredecessorsScope() {
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
    'src/backend/ProjectTime.Api/Ai/ProjectPulseAiRemoteProviders.cs',
    'src/backend/ProjectTime.Api/Ai/Module025ExternalSowAdapter.cs',
    'src/backend/ProjectTime.Api/Ai/Module025PhaseOutputContract.cs',
    'src/backend/ProjectTime.Api/Ai/PulseAiPrivateModelClient.cs',
    'src/backend/ProjectTime.Api/Ai/ProjectPulseAiConfiguration.cs',
    'src/backend/ProjectTime.Api/Ai/ProjectPulseAiSecretStore.cs'
  ]) assert.equal(execFileSync('git', ['rev-parse', `HEAD:${name}`], { encoding: 'utf8' }),
    execFileSync('git', ['rev-parse', `${base}:${name}`], { encoding: 'utf8' }), `Out-of-scope change: ${name}`);
  console.log('MODULE025_ASSEMBLY_PREDECESSORS_SCOPE=PASS deployment_authority=unchanged celar_runtime=unchanged');
}
if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) verifyModule025AssemblyPredecessorsScope();
