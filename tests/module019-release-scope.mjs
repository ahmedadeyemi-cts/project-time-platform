import assert from 'node:assert/strict';
import fs from 'node:fs';
import { execFileSync } from 'node:child_process';
import { verifyReadOnlyWorkflow } from './flowhive-psa-scope.mjs';

// Proposed read-only registration for PR #1090. Passing this validator is not
// approval to merge, dispatch, supersede another run, or change an environment.
const base = '9557e1ce52ac2f7781f9deb535698b635e60b253';
const featureHead = '3c8462a410fa24691870112bb0b8a2ef452bb720';
const branch = 'codex/module019-project-focused-workspace';
const gate = 'scripts/release-test/validate-protected-test-controller-branches.sh';
const featureFiles = [
  '.github/workflows/module019-document-access-repair-ci.yml',
  'docs/modules/module019-project-workspace/README.md',
  'src/backend/ProjectTime.Api/Modules/ProjectWorkspaceModule019Repair.cs',
  'src/frontend/project-time-web/scripts/inject-group-3-project-financial-workspaces.mjs',
  'src/frontend/project-time-web/scripts/validate-group-3-project-financial-workspaces.mjs',
  'src/frontend/project-time-web/scripts/validate-module-loading-assignment-propagation.mjs',
  'src/frontend/project-time-web/src/ProjectWorkspaceCenter.jsx',
  'src/frontend/project-time-web/src/project-workspace-center.css',
  'src/frontend/project-time-web/src/project-workspace-model.js',
  'tests/module019-workspace-access.test.mjs',
  'tests/module019-workspace-browser.mjs',
  'tests/module019-workspace-model.test.mjs',
  'tests/module019-workspace-sql.test.mjs'
].sort();
const expected = [...featureFiles, gate, 'tests/module019-release-scope.mjs',
  'docs/releases/MODULE019-PROTECTED-TEST-PREPARATION.md'].sort();
const git = (...args) => execFileSync('git', args, { encoding: 'utf8' }).trim();
const original = (sha, file) => execFileSync('git', ['show', `${sha}:${file}`], { encoding: 'utf8' });
assert.equal(git('merge-base', base, 'HEAD'), base, 'Include the prepared main base');
assert.equal(git('merge-base', featureHead, 'HEAD'), featureHead, 'Preserve the existing PR history');
assert.equal(git('merge-base', 'origin/main', 'HEAD'), base, 'Main advanced: refresh and revalidate the proposed scope');
if (process.env.GITHUB_HEAD_REF) assert.equal(process.env.GITHUB_HEAD_REF, branch);
if (process.env.PR_NUMBER) assert.equal(process.env.PR_NUMBER, '1090');

const verify = files => assert.deepEqual([...files].sort(), expected, 'Exact Module 019 feature and preparation files required');
verify(git('diff', '--name-only', base).split(/\r?\n/).filter(Boolean));
for (const file of expected) assert.throws(() => verify(expected.filter(value => value !== file)));
for (const file of [
  '.github/workflows/projectpulse-deploy-test.yml',
  '.github/workflows/projectpulse-deploy-production.yml',
  '.github/flowhive-psa-protected-test-candidate.json',
  'src/backend/ProjectTime.Api/Modules/InvoiceBillingModule.cs',
  'deployment/oracle-celar/ollama-update.sh',
  'database/migrations/unreviewed.sql'
]) assert.throws(() => verify([...expected, file]));
assert.throws(() => verify([...expected, expected[0]]));

// Freeze all application/test changes to the existing reviewed PR snapshot.
for (const file of featureFiles) {
  assert.equal(fs.readFileSync(file, 'utf8'), original(featureHead, file), `Feature changed since audit: ${file}`);
}
const registration = `elif [[ "$HEAD_BRANCH" == 'codex/module019-project-focused-workspace' ]]; then
  [[ "$PR_NUMBER" == '1090' ]] || fail 'Module 019 scope is registered only for PR #1090.'
  node tests/module019-release-scope.mjs
  node tests/validate-systemwide-image-build-controller.mjs
`;
const verifyGate = text => {
  assert.equal(text.split(registration).length, 2, 'Exactly one registration is required');
  assert.equal(text.replace(registration, ''), original(base, gate), 'All existing release checks must remain unchanged');
};
const gateSource = fs.readFileSync(gate, 'utf8');
verifyGate(gateSource);
assert.throws(() => verifyGate(gateSource.replace('codex/module019-project-focused-workspace', '*')));
assert.throws(() => verifyGate(gateSource.replace("[[ \"$PR_NUMBER\" == '1090' ]]", 'true')));
assert.throws(() => verifyGate(gateSource.replace('exit 1', 'exit 0')));

const workflow = featureFiles[0];
verifyReadOnlyWorkflow(fs.readFileSync(workflow, 'utf8'), workflow);
for (const file of [
  '.github/workflows/projectpulse-release-test-control-ci.yml',
  '.github/workflows/projectpulse-deploy-test.yml',
  '.github/workflows/projectpulse-deploy-production.yml',
  '.github/workflows/module025-protected-uat-control.yml',
  '.github/flowhive-psa-protected-test-candidate.json',
  '.github/flowhive-psa-protected-cutover.json',
  'scripts/release-test/flowhive-psa-admission.mjs',
  'scripts/release-test/prepare-protected-test-scope-manifests.sh'
]) assert.equal(fs.readFileSync(file, 'utf8'), original(base, file), `Deployment or approval authority changed: ${file}`);
assert.equal(git('diff', '--name-only', base, '--', 'deployment', 'database'), '');
execFileSync('git', ['diff', '--check', base], { stdio: 'pipe' });
console.log(`MODULE019_RELEASE_SCOPE=PASS exact_files=${expected.length} feature_files=${featureFiles.length} base=${base} deployment_authorized=false`);
