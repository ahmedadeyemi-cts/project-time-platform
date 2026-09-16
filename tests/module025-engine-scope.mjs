import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { verifyReadOnlyWorkflow } from './flowhive-psa-scope.mjs';

export const module025EngineFiles = [
  ".github/workflows/flowhive-detailed-planner-ci.yml",
  ".github/workflows/flowhive-psa-release-control-ci.yml",
  "scripts/release-test/run-module025-installed-sa-uat.py",
  "scripts/release-test/validate-protected-test-controller-branches.sh",
  "src/backend/ProjectTime.Api/Ai/CelarAiCapabilityRouting.cs",
  "src/backend/ProjectTime.Api/Ai/CelarAiEnterprisePlatformContracts.cs",
  "src/backend/ProjectTime.Api/Ai/CelarAiEnterprisePlatformService.cs",
  "src/backend/ProjectTime.Api/Ai/Module025GenerationEngine.cs",
  "src/backend/ProjectTime.Api/Ai/ProjectPulseDeepSeekProvider.cs",
  "src/backend/ProjectTime.Api/Ai/PulseAiPrivateRagService.cs",
  "src/backend/ProjectTime.Api/Modules/Module025GenerationJournal.cs",
  "src/backend/ProjectTime.Api/Modules/Module025SowGsdModule.cs",
  "src/backend/ProjectTime.Api/Modules/Module025SowSellModule.cs",
  "src/frontend/project-time-web/src/module025/SowGsdAuthoringWorkspace.jsx",
  "tests/DeepSeekProviderTests/Program.cs",
  "tests/FlowHiveDetailedPlannerTests/Module025GenerationEngineTests.cs",
  "tests/FlowHiveDetailedPlannerTests/Program.cs",
  "tests/flowhive-psa-admission.test.mjs",
  "tests/flowhive-psa-installed-acceptance.test.py",
  "tests/module025-engine-scope.mjs",
  "tests/test-sow-uat-isolation.py",
  "tests/validate-celar-ai-pr630-consolidated.mjs"
];
export function verifyModule025EnginePaths(actual) {
  assert.deepEqual([...actual].sort(), module025EngineFiles,
    'Module 025 engine redesign must match its exact file set');
}
export function verifyModule025EngineScope() {
  const base = '37dfd1554d50a7b45b25982faf9baa68603a0e43';
  const mergeBase = execFileSync('git', ['merge-base', base, 'HEAD'], { encoding: 'utf8' }).trim();
  assert.equal(mergeBase, base, 'Engine redesign must descend from the reviewed base');
  const files = execFileSync('git', ['diff', '--name-only', `${base}...HEAD`], { encoding: 'utf8' }).trim().split(/\r?\n/);
  verifyModule025EnginePaths(files);
  for (const name of module025EngineFiles.filter(name => name.startsWith('.github/workflows/')))
    verifyReadOnlyWorkflow(fs.readFileSync(name, 'utf8'), name);
  for (const name of ['.github/workflows/projectpulse-deploy-test.yml',
    '.github/workflows/flowhive-psa-installed-acceptance.yml',
    '.github/flowhive-psa-protected-test-candidate.json',
    '.github/flowhive-psa-release-control-files.txt',
    'scripts/release-test/flowhive-psa-admission.mjs']) {
    assert.deepEqual(fs.readFileSync(name), execFileSync('git', ['show', `${base}:${name}`]),
      `Engine redesign cannot change release authority: ${name}`);
  }
  assert.throws(() => verifyModule025EnginePaths([...files, '.github/workflows/projectpulse-deploy-test.yml']));
  assert.throws(() => verifyModule025EnginePaths(files.slice(1)));
  console.log('MODULE025_ENGINE_EXACT_SCOPE=PASS deployment_authority=unchanged');
}
if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) verifyModule025EngineScope();
