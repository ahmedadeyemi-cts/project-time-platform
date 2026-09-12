import assert from 'node:assert/strict';
import fs from 'node:fs';
import { execFileSync } from 'node:child_process';

const manifestPath = '.github/flowhive-planner-output-budget-release-files.txt';
const reviewed = fs.readFileSync(manifestPath, 'utf8').trim().split(/\r?\n/);
assert.deepEqual(reviewed, [...new Set(reviewed)].sort(), 'Planner output-budget manifest must be sorted and unique.');
for (const name of reviewed) {
  assert.match(name, /^(?:\.github|scripts|src|tests)\/[A-Za-z0-9._/-]+$/, `Invalid governed path: ${name}`);
  assert.ok(fs.existsSync(name) && fs.lstatSync(name).isFile(), `Missing governed path: ${name}`);
}

const base = process.env.BASE_SHA || execFileSync('git', ['merge-base', 'origin/main', 'HEAD'], { encoding: 'utf8' }).trim();
assert.match(base, /^[a-f0-9]{40}$/, 'The pull-request base SHA must be a full commit.');
execFileSync('git', ['cat-file', '-e', `${base}^{commit}`], { stdio: 'pipe' });
const actual = execFileSync('git', ['diff', '--name-only', `${base}...HEAD`], { encoding: 'utf8' })
  .trim().split(/\r?\n/).filter(Boolean).sort();
assert.deepEqual(actual, reviewed, 'Planner output-budget changes must match the exact reviewed manifest.');
execFileSync('git', ['diff', '--check', `${base}...HEAD`], { stdio: 'pipe' });

const source = fs.readFileSync('src/backend/ProjectTime.Api/Ai/PulseAiPrivateRagService.cs', 'utf8');
assert.match(source, /FlowHivePlanMaximumOutputTokens\s*=\s*12_000/);
assert.match(source, /FlowHivePlanMaximumAnswerCharacters\s*=\s*96_000/);
assert.match(source, /MaximumOutputTokensForPlanning\(\s*query\.FeatureCode,\s*options\.MaximumOutputTokens\)/s);
assert.match(source, /MaximumAnswerCharactersForPlanning\(\s*query\.FeatureCode,\s*options\.MaximumAnswerCharacters\)/s);

console.log(`FLOWHIVE_PLANNER_OUTPUT_BUDGET_EXACT_SCOPE=PASSED files=${actual.length} base=${base}`);
