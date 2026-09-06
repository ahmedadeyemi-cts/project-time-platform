import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
const root = fileURLToPath(new URL('../', import.meta.url));
const git = (...args) => execFileSync('git', args, {cwd: root, encoding:'utf8'}).trim();
const manifest = readFileSync(new URL('../.github/sow-generation-uat-files.txt', import.meta.url), 'utf8').trim().split('\n');
assert.deepEqual(manifest, [...new Set(manifest)].sort());
const base = process.env.BASE_SHA || git('merge-base', 'origin/main', 'HEAD');
assert.match(base, /^[a-f0-9]{40}$/);
assert.deepEqual(git('diff','--name-only',base,'HEAD').split('\n').filter(Boolean).sort(), manifest);
for (const path of ['deployment', 'database', '.github/workflows/projectpulse-deploy-test.yml', '.github/workflows/projectpulse-deploy-production.yml'])
  assert.equal(git('diff','--name-only',base,'HEAD','--',path), '', `${path} is outside this release`);
git('diff','--check',base,'HEAD');
console.log('SOW_GENERATION_UAT_EXACT_SCOPE=PASS');
