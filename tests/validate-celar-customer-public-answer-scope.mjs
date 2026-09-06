import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const root = fileURLToPath(new URL('../', import.meta.url));
const git = (...args) => execFileSync('git', args, { cwd: root, encoding: 'utf8' }).trim();
const manifest = readFileSync(new URL('../.github/celar-customer-public-answer-files.txt', import.meta.url), 'utf8').trim().split('\n');
assert.deepEqual(manifest, [...new Set(manifest)].sort(), 'Release manifest must be sorted and unique');
const base = process.env.BASE_SHA || git('merge-base', 'origin/main', 'HEAD');
assert.match(base, /^[a-f0-9]{40}$/);
const actual = git('diff', '--name-only', base, 'HEAD').split('\n').filter(Boolean).sort();
assert.deepEqual(actual, manifest, 'Customer/public answer release must match its exact file manifest');
// The release only changes answer retrieval/presentation and its own CI scope.
// Provider policy, Oracle DNS/runtime, database schema and deployment remain unchanged.
for (const path of [
  'src/backend/ProjectTime.Api/Ai/CelarAiCapabilityRouting.cs',
  'src/backend/ProjectTime.Api/Ai/PulseAiExternalHttpsRuntimePolicy.cs',
  '.github/workflows/projectpulse-deploy-test.yml',
  '.github/workflows/projectpulse-deploy-production.yml',
  'deployment/oracle-celar',
  'database'
]) assert.equal(git('diff', '--name-only', base, 'HEAD', '--', path), '', `${path} is outside this release`);
git('diff', '--check', base, 'HEAD');
console.log('CELAR_CUSTOMER_PUBLIC_ANSWER_EXACT_RELEASE_SCOPE=PASS');
