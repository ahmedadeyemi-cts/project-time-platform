import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
const root = fileURLToPath(new URL('../', import.meta.url));
const git = (...args) => execFileSync('git', args, {cwd: root, encoding:'utf8'}).trim();
const branch = process.env.GITHUB_HEAD_REF || git('branch','--show-current');
const isolationMode = branch === 'fix/sow-runtime-diagnostics-and-uat-isolation-20260906';
const manifestName = isolationMode ? 'sow-runtime-isolation-files.txt' : branch === 'fix/sow-transport-prompt-20260906' ? 'sow-transport-prompt-files.txt' : branch === 'fix/sow-phase-runtime-retry-20260906' ? 'sow-phase-runtime-retry-files.txt' : 'sow-generation-uat-files.txt';
const manifest = readFileSync(new URL(`../.github/${manifestName}`, import.meta.url), 'utf8').trim().split('\n');
assert.deepEqual(manifest, [...new Set(manifest)].sort());
const base = process.env.BASE_SHA || git('merge-base', 'origin/main', 'HEAD');
assert.match(base, /^[a-f0-9]{40}$/);
assert.deepEqual(git('diff','--name-only',base,'HEAD').split('\n').filter(Boolean).sort(), manifest);
for (const path of ['database', ...(!isolationMode ? ['deployment', '.github/workflows/projectpulse-deploy-test.yml'] : []), '.github/workflows/projectpulse-deploy-production.yml'])
  assert.equal(git('diff','--name-only',base,'HEAD','--',path), '', `${path} is outside this release`);
git('diff','--check',base,'HEAD');
if (isolationMode) {
  assert.deepEqual(git('diff','--name-only',base,'HEAD','--','deployment').split('\n').sort(), [
    'deployment/oracle-celar/deploy.sh',
    'deployment/oracle-celar/health-check.sh',
    'deployment/oracle-celar/release.json',
    'deployment/oracle-celar/verify-ollama-memory-policy.py'
  ]);
  const previousRuntime = JSON.parse(git('show', `${base}:deployment/oracle-celar/release.json`));
  const currentRuntime = JSON.parse(readFileSync(new URL('../deployment/oracle-celar/release.json', import.meta.url), 'utf8'));
  assert.equal(currentRuntime.ollamaMaxLoadedModels, 1);
  assert.equal(currentRuntime.ollamaNumParallel, 1);
  assert.equal(currentRuntime.gatewayVersion, '1.1.7');
  assert.deepEqual({...currentRuntime, gatewayVersion:previousRuntime.gatewayVersion,
    ollamaMaxLoadedModels:previousRuntime.ollamaMaxLoadedModels}, previousRuntime,
    'only runtime version and model residence count may change');
  execFileSync('python3', ['tests/test-ollama-memory-policy.py'], {cwd: root, stdio:'inherit'});
  const oracleCi = '.github/workflows/celar-ai-oracle-gitops-ci.yml';
  const oldCi = git('show', `${base}:${oracleCi}`);
  const expectedCi = oldCi.replace('.gatewayVersion == "1.1.6"', '.gatewayVersion == "1.1.7" and\n            .ollamaMaxLoadedModels == 1 and\n            .ollamaNumParallel == 1')
    .replace('          python3 tests/test-celar-sow-runtime-deadlines.py', '          python3 tests/test-celar-sow-runtime-deadlines.py\n          python3 tests/test-ollama-memory-policy.py\n          python3 tests/test-celar-runtime-evidence.py');
  assert.equal(readFileSync(new URL(`../${oracleCi}`, import.meta.url), 'utf8').trimEnd(), expectedCi,
    'Oracle validation changes must only correct the pinned manifest and add policy/privacy tests');


  const controller = '.github/workflows/projectpulse-deploy-test.yml';
  const before = git('show', `${base}:${controller}`).split(/(?=^      - name: )/m);
  const after = readFileSync(new URL(`../${controller}`, import.meta.url), 'utf8').trimEnd().split(/(?=^      - name: )/m);
  assert.equal(after[0], before[0], 'release triggers, permissions, concurrency and environment are unchanged');
  const name = block => block.split('\n')[0];
  const oldSteps = new Map(before.slice(1).map(block => [name(block), block.trimEnd()]));
  assert.equal(after.length, before.length, 'no deployment step may be added or removed');
  const revisedGates = new Set([
    'Run protected-Test assigned-work visibility UAT',
    'Run protected-Test utilization role-scoping UAT',
    'Enable exact-run Module 025 protected-Test authorization fixture',
    'Run protected-Test Module 025 SOW/GSD generation lifecycle UAT'
  ].map(value => `      - name: ${value}`));
  for (const block of after.slice(1)) {
    const key = name(block);
    let normalized = block.trimEnd();
    if (revisedGates.has(key)) {
      normalized = normalized.replace(/^        if:.*\n/m, '')
        .replace(/^          echo "expires_at=\$FIXTURE_EXPIRES_AT" >> "\$GITHUB_OUTPUT"\n/m, '')
        .replace(/^          MODULE025_UAT_EXPIRES_AT:.*\n/m, '');
    }
    assert.equal(normalized, revisedGates.has(key) ? oldSteps.get(key)?.replace(/^        if:.*\n/m, '') : oldSteps.get(key), `${key}: only gate conditions/order and fixture expiry propagation may change`);
    oldSteps.delete(key);
  }
  assert.equal(oldSteps.size, 0, 'every original deployment step is retained exactly once');

  execFileSync('python3', ['tests/test-sow-uat-isolation.py'], {cwd: root, stdio:'inherit'});
  execFileSync('python3', ['tests/test-celar-runtime-evidence.py'], {cwd: root, stdio:'inherit'});
}
console.log('SOW_GENERATION_UAT_EXACT_SCOPE=PASS');
