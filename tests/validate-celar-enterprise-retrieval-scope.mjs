import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const root = fileURLToPath(new URL('../', import.meta.url));
const git = (...args) => execFileSync('git', args, { cwd: root, encoding: 'utf8' }).trim();
const manifest = readFileSync(new URL('../.github/celar-enterprise-retrieval-files.txt', import.meta.url), 'utf8').trim().split('\n');
assert.deepEqual(manifest, [...new Set(manifest)].sort(), 'Manifest must be sorted and unique');
const base = process.env.BASE_SHA || git('merge-base', 'origin/main', 'HEAD');
assert.match(base, /^[a-f0-9]{40}$/);
assert.deepEqual(git('diff','--name-only',base,'HEAD').split('\n').filter(Boolean).sort(),manifest,
  'Enterprise retrieval changes must match their reviewed file scope');
for (const path of [
  'src/backend/ProjectTime.Api/Ai/CelarAiCapabilityRouting.cs',
  'src/backend/ProjectTime.Api/Ai/PulseAiExternalHttpsRuntimePolicy.cs',
  'src/backend/ProjectTime.Api/Modules/Module025SowGsdModule.cs',
  '.github/workflows/projectpulse-deploy-test.yml',
  '.github/workflows/projectpulse-deploy-production.yml',
  'deployment', 'database'
]) assert.equal(git('diff','--name-only',base,'HEAD','--',path),'',`${path} is outside this release`);
git('diff','--check',base,'HEAD');
console.log('CELAR_ENTERPRISE_RETRIEVAL_EXACT_SCOPE=PASS');
