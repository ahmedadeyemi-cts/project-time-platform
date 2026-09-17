import assert from 'node:assert/strict';
import fs from 'node:fs';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import { verifyReadOnlyWorkflow } from './flowhive-psa-scope.mjs';

const base = 'd53b9451d7b2eebda686e8bdb84b46d53dc9e7f9';
const expected = [
  '.github/workflows/flowhive-psa-release-control-ci.yml',
  '.github/workflows/projectpulse-deploy-test.yml',
  'docs/releases/2026-09-17-module025-complete-acceptance.md',
  'scripts/ci/validate-celar-ai-enterprise-source-boundary.sh',
  'scripts/release-test/build-and-run-module025-retention-migration-106.sh',
  'scripts/release-test/run-module025-installed-sa-uat.py',
  'scripts/release-test/run-module025-provider-qualification.py',
  'scripts/release-test/validate-protected-test-controller-branches.sh',
  'src/backend/ProjectTime.Api/Ai/Module025ExternalSowAdapter.cs',
  'src/backend/ProjectTime.Api/Ai/PulseAiEscalationSanitizer.cs',
  'tests/FlowHiveDetailedPlannerTests/Module025ExternalSowTests.cs',
  'tests/flowhive-psa-admission.test.mjs',
  'tests/flowhive-psa-installed-acceptance.test.py',
  'tests/flowhive-psa-release-workflow.test.py',
  'tests/module025-complete-acceptance-scope.mjs',
  'tests/module025-complete-acceptance.test.py',
  'tests/module025-provider-qualification.test.py',
  'tests/module025-scoped-deploy.test.py',
  'tests/module025-sow-register-browser.py',
  'tests/module025_qualification_workflow.py',
  'tests/validate-celar-ai-pr630-consolidated.mjs'
].sort();

export function verifyModule025CompleteAcceptanceScope() {
  assert.equal(execFileSync('git', ['merge-base', base, 'HEAD'], {encoding:'utf8'}).trim(), base);
  const actual = execFileSync('git', ['diff', '--name-only', `${base}...HEAD`], {encoding:'utf8'}).trim().split(/\r?\n/);
  const verify = files => assert.deepEqual([...files].sort(), expected);
  verify(actual);
  assert.throws(() => verify([...actual, '.github/workflows/projectpulse-deploy-production.yml']));
  assert.throws(() => verify(actual.slice(1)));
  verifyReadOnlyWorkflow(fs.readFileSync(expected[0], 'utf8'), expected[0]);
  for (const file of ['.github/flowhive-psa-protected-test-candidate.json',
    '.github/flowhive-psa-release-control-files.txt', 'scripts/release-test/flowhive-psa-admission.mjs',
    'scripts/release-test/dispatch-flowhive-psa-test.mjs', 'scripts/validate-deployment-concurrency-governance.mjs',
    '.github/workflows/projectpulse-deploy-production.yml', '.github/workflows/flowhive-psa-installed-acceptance.yml'])
    assert.deepEqual(fs.readFileSync(file), execFileSync('git', ['show', `${base}:${file}`]), `Deployment authority changed: ${file}`);
  for (const name of ['deployment', 'database',
    'src/backend/ProjectTime.Api/Ai/PulseAiPrivateModelClient.cs',
    'src/backend/ProjectTime.Api/Ai/ProjectPulseAiConfiguration.cs',
    'src/backend/ProjectTime.Api/Ai/ProjectPulseAiSecretStore.cs'])
    assert.equal(execFileSync('git', ['rev-parse', `HEAD:${name}`], {encoding:'utf8'}),
      execFileSync('git', ['rev-parse', `${base}:${name}`], {encoding:'utf8'}), `Out-of-scope change: ${name}`);
  // Projection removes only the explicitly tested migration and independent
  // acceptance additions; every other deployment command remains exact.
  for (const test of ['module025-provider-qualification.test.py', 'module025-scoped-deploy.test.py',
    'module025-complete-acceptance.test.py'])
    execFileSync('python3', [`tests/${test}`], {stdio:'inherit'});
  console.log('MODULE025_COMPLETE_ACCEPTANCE_SCOPE=PASS deployment_authority=unchanged celar_runtime=unchanged');
}
if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) verifyModule025CompleteAcceptanceScope();
