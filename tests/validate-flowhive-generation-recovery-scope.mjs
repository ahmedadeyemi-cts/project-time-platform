import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
const root = fileURLToPath(new URL('../', import.meta.url));
const run = (...args) => execFileSync('git', args, { cwd: root, encoding: 'utf8' }).trim();
const manifest = readFileSync(new URL('../.github/flowhive-generation-recovery-files.txt', import.meta.url), 'utf8').trim().split('\n');
assert.deepEqual(manifest, [...new Set(manifest)].sort(), 'FlowHive generation recovery manifest must be sorted and unique');
const base = process.env.BASE_SHA || run('merge-base', 'origin/main', 'HEAD');
assert.match(base, /^[a-f0-9]{40}$/);
const actual = run('diff', '--name-only', base, 'HEAD').split('\n').filter(Boolean).sort();
assert.deepEqual(actual, manifest, 'FlowHive generation recovery changes must match the complete authorized manifest');
assert.ok(!actual.includes('.github/workflows/projectpulse-deploy-production.yml'));
run('diff', '--check', base, 'HEAD');
console.log('FLOWHIVE_GENERATION_RECOVERY_EXACT_RELEASE_SCOPE=PASS');

execFileSync('python3', ['tests/test-oracle-runtime-preflight.py'], {cwd: root, stdio: 'inherit'});
execFileSync('python3', ['tests/test-celar-sow-runtime-deadlines.py'], {cwd: root, stdio: 'inherit'});
