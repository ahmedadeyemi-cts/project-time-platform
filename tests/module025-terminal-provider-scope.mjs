import assert from 'node:assert/strict';
import fs from 'node:fs';
import { execFileSync } from 'node:child_process';
import { verifyReadOnlyWorkflow } from './flowhive-psa-scope.mjs';

// Register this repair in the existing exact-scope validators. It changes UAT
// evidence verification only; no application or deployment authority is added.
const base = '245b0915d895d83f1ceaed32460ad95a4a3d79be';
const workflow = '.github/workflows/module025-governed-protected-test-release-manual.yml';
const branches = 'scripts/release-test/validate-protected-test-controller-branches.sh';
const expected = [
  workflow,
  'scripts/release-test/run-module025-sow-gsd-protected-test-uat.sh',
  branches,
  'tests/module025-terminal-provider-scope.mjs',
  'tests/validate-systemwide-image-build-controller.mjs'
].sort();
const git = (...args) => execFileSync('git', args, {encoding:'utf8'}).trim();
assert.equal(git('merge-base', base, 'HEAD'), base, 'Repair must include its reviewed main base');
const verify = files => assert.deepEqual([...files].sort(), expected);
verify(git('diff', '--name-only', base).split(/\r?\n/));
for (const file of expected) assert.throws(() => verify(expected.filter(item => item !== file)));
for (const file of [
  '.github/workflows/projectpulse-deploy-test.yml',
  '.github/workflows/projectpulse-deploy-production.yml',
  '.github/workflows/module025-protected-uat-control.yml',
  'src/backend/ProjectTime.Api/Ai/CelarAiCapabilityRouting.cs',
  'deployment/oracle-celar/ollama-update.sh'
]) assert.throws(() => verify([...expected, file]));

const original = name => execFileSync('git', ['show', `${base}:${name}`], {encoding:'utf8'});
const workflowSource = fs.readFileSync(workflow, 'utf8');
verifyReadOnlyWorkflow(workflowSource, workflow);
assert.throws(() => verifyReadOnlyWorkflow(workflowSource.replace('contents: read', 'contents: write'), workflow));

// Only the two explicit validation registrations may differ from trusted main.
// All other branches and their fail-closed checks must remain byte-identical.
const workflowRegistration = `          if [[ "$HEAD_BRANCH" == 'fix/module025-terminal-provider-evidence-20260918' ]]; then
            node tests/module025-terminal-provider-scope.mjs
            node tests/validate-systemwide-image-build-controller.mjs
            exit 0
          fi
`;
assert.equal(workflowSource.replace(workflowRegistration, ''), original(workflow));
const branchRegistration = `elif [[ "$HEAD_BRANCH" == 'fix/module025-terminal-provider-evidence-20260918' ]]; then
  node tests/module025-terminal-provider-scope.mjs
  node tests/validate-systemwide-image-build-controller.mjs
`;
assert.equal(fs.readFileSync(branches, 'utf8').replace(branchRegistration, ''), original(branches));

for (const file of [
  '.github/workflows/projectpulse-deploy-test.yml',
  '.github/workflows/projectpulse-deploy-production.yml',
  '.github/workflows/module025-protected-uat-control.yml',
  '.github/flowhive-psa-protected-test-candidate.json',
  '.github/flowhive-psa-release-control-files.txt',
  'scripts/release-test/flowhive-psa-admission.mjs'
]) assert.equal(fs.readFileSync(file, 'utf8'), original(file), `Deployment authority changed: ${file}`);
assert.equal(git('diff', '--name-only', base, '--', 'src', 'deployment', 'database'), '');
console.log('MODULE025_TERMINAL_PROVIDER_SCOPE=PASS exact_files=5 deployment_authority=unchanged');
