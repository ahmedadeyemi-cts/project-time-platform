import assert from 'node:assert/strict';
import fs from 'node:fs';
import { execFileSync } from 'node:child_process';

const manifestPath = '.github/flowhive-planner-output-budget-release-files.txt';
const reviewed = fs.readFileSync(manifestPath, 'utf8').trim().split(/\r?\n/);
assert.deepEqual(reviewed, [...new Set(reviewed)].sort(), 'Planner phase-assembly manifest must be sorted and unique.');

const base = process.env.BASE_SHA || execFileSync('git', ['merge-base', 'origin/main', 'HEAD'], { encoding: 'utf8' }).trim();
assert.match(base, /^[a-f0-9]{40}$/, 'The pull-request base SHA must be a full commit.');
execFileSync('git', ['cat-file', '-e', `${base}^{commit}`], { stdio: 'pipe' });
const actual = execFileSync('git', ['diff', '--name-only', `${base}...HEAD`], { encoding: 'utf8' })
  .trim().split(/\r?\n/).filter(Boolean).sort();
assert.deepEqual(actual, reviewed, 'Planner phase-assembly changes must match the exact reviewed manifest.');
execFileSync('git', ['diff', '--check', `${base}...HEAD`], { stdio: 'pipe' });

const service = fs.readFileSync('src/backend/ProjectTime.Api/Ai/PulseAiPrivateRagService.cs', 'utf8');
for (const marker of [
  'AssembleModule025PhasePlans',
  'CanonicalWbs',
  'unresolvable or self-referencing predecessor',
  'Predecessors = predecessors.Distinct'
]) {
  assert.ok(service.includes(marker), `Phase assembly protection is missing: ${marker}`);
}

const regression = fs.readFileSync('tests/FlowHiveDetailedPlannerTests/Program.cs', 'utf8');
for (const marker of [
  'module025_phase_local_wbs_is_normalized_without_duplicate_proposal_ids',
  'module025_phase_local_predecessor_resolves_to_prior_phase'
]) {
  assert.ok(regression.includes(marker), `Phase assembly regression is missing: ${marker}`);
}

console.log(`FLOWHIVE_PLANNER_PHASE_ASSEMBLY_EXACT_SCOPE=PASSED files=${actual.length} base=${base}`);
