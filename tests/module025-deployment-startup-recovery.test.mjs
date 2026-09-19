import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { execFileSync, spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

// Execute the actual workflow shell offline. No GitHub or Azure calls are made.
if (process.argv[2] === '--fake-command') {
  const statePath = process.env.RECOVERY_TEST_STATE;
  const state = JSON.parse(fs.readFileSync(statePath, 'utf8'));
  const [command, ...args] = process.argv.slice(3);
  state.calls.push([command, ...args]);
  const save = () => fs.writeFileSync(statePath, JSON.stringify(state));
  const reply = (value) => { save(); console.log(typeof value === 'string' ? value : JSON.stringify(value)); process.exit(0); };
  if (command === 'sleep') { state.epoch = (state.epoch || 0) + Number(args[0]); reply(''); }
  if (command === 'git') {
    save();
    if (args[0] === 'show') {
      const content = execFileSync(process.env.RECOVERY_REAL_GIT, args);
      fs.writeFileSync(1, content);
      if (args[1].startsWith('HEAD:') && state.deploymentGuardChanged) process.stdout.write('unexpected controller change\n');
      process.exit(0);
    }
    process.exit((args[0] === 'diff' ? state.deploymentGuardChanged : state.ancestryOk === false) ? 1 : 0);
  }
  if (command === 'date') reply(args[0] === '+%s' ? String(state.epoch || 0) : '2026-09-18T16:30:00Z');
  assert.equal(command, 'gh');
  const endpoint = args.find((arg) => arg.startsWith('repos/'));
  if (endpoint.endsWith('/enable')) { state.workflowState = 'active'; reply(''); }
  if (endpoint.endsWith('/disable')) { state.workflowState = 'disabled_manually'; reply(''); }
  if (endpoint.endsWith('/dispatches')) {
    save(); process.exit(state.dispatchError ? 1 : 0);
  }
  if (endpoint.endsWith('/git/ref/heads/main')) reply(state.main);
  if (endpoint.includes('/jobs?')) {
    state.jobReads++;
    save();
    if (state.jobsApiError) process.exit(1);
    reply({total_count: state.jobReads > state.emptyJobReads ? (state.reportedJobsTotal ?? state.jobs.length) : 0,
      jobs: state.jobReads > state.emptyJobReads ? state.jobs : []});
  }
  if (endpoint.endsWith('/pending_deployments')) {
    save();
    if (state.pendingApiError) process.exit(1);
    reply(state.pendingDeployments ?? []);
  }
  if (/\/actions\/runs\/\d+$/.test(endpoint)) reply(state.run);
  if (endpoint.includes('/runs?')) reply({workflow_runs: [state.run, ...(state.additionalRuns ?? [])]});
  if (endpoint.endsWith('/actions/workflows/315562561')) reply(state.workflowState);
  throw new Error(`Unexpected mock command: ${args.join(' ')}`);
}

const root = process.cwd();
const base = '669b467b932d3d029bde2c5bba03e0f63ba474f4';
const supervisorPath = '.github/workflows/module025-protected-uat-control.yml';
const branchesPath = 'scripts/release-test/validate-protected-test-controller-branches.sh';
const testPath = 'tests/module025-deployment-startup-recovery.test.mjs';
const source = fs.readFileSync(supervisorPath, 'utf8');
function shellStep(name) {
  const marker = `      - name: ${name}\n`;
  const start = source.indexOf(marker);
  assert.ok(start >= 0, `Missing step: ${name}`);
  const next = source.indexOf('\n      - name:', start + marker.length);
  const step = source.slice(start, next < 0 ? undefined : next);
  const body = step.split('        run: |\n')[1];
  assert.ok(body);
  return body.split('\n').map((line) => line.startsWith('          ') ? line.slice(10) : line).join('\n');
}
const dispatch = shellStep('Dispatch exact governed Protected-Test deployment and reseal admissions');
const authorize = shellStep('Authorize exact Module 025 Protected-Test release');
const release = 'b'.repeat(40);
const defaultRun = {
  id: 999, workflow_id: 315562561, event: 'workflow_dispatch', head_branch: 'main',
  head_sha: release, run_attempt: 1, status: 'queued', conclusion: null,
  created_at: '2026-09-18T16:30:00Z', updated_at: '2026-09-18T16:30:00Z',
  html_url: 'https://github.com/example/repo/actions/runs/999'
};
const orphan = {...defaultRun, id: 35364203547, head_sha: base,
  created_at: '2026-09-18T15:43:56Z', updated_at: '2026-09-18T15:43:56Z'};
const repairBase = 'e7f1634aaa6eabb81d404510178de2812660214d';
const uncancellable = {...defaultRun, id: 35374125567,
  head_sha: '245b0915d895d83f1ceaed32460ad95a4a3d79be',
  created_at: '2026-09-18T17:24:14Z', updated_at: '2026-09-18T17:24:14Z'};
const temporary = fs.mkdtempSync(path.join(os.tmpdir(), 'module025-startup-'));
let cases = 0;
function execute(body, changes = {}, envChanges = {}) {
  const directory = path.join(temporary, String(cases++));
  fs.mkdirSync(directory);
  const statePath = path.join(directory, 'state.json');
  const initial = {calls: [], workflowState: 'disabled_manually', main: release,
    run: defaultRun, jobs: [{id: 123, run_id: 999}], emptyJobReads: 0, jobReads: 0, ...changes};
  fs.writeFileSync(statePath, JSON.stringify(initial));
  const output = path.join(directory, 'output');
  fs.writeFileSync(output, '');
  const result = spawnSync('bash', ['-c', body], {cwd: root, encoding: 'utf8', timeout: 30000,
    env: {...process.env, PATH: `${path.join(temporary, 'bin')}:${process.env.PATH}`,
      RECOVERY_REAL_GIT: execFileSync('which', ['git'], {encoding: 'utf8'}).trim(),
      RECOVERY_TEST_STATE: statePath, RUNNER_TEMP: directory, GITHUB_OUTPUT: output,
      GITHUB_REPOSITORY: 'example/repo', GITHUB_SHA: release, RELEASE_SHA: release,
      GITHUB_EVENT_NAME: 'push', GITHUB_REF: 'refs/heads/main', REQUEST_COMMENT: '',
      DEPLOY_WORKFLOW_ID: '315562561', DEPLOY_WORKFLOW_PATH: '.github/workflows/projectpulse-deploy-test.yml',
      REPAIRED_MODULE025_SHA: 'f7c86b45cff09741dd022c0e80bc1e6ad7d5c80b',
      QUARANTINED_ZERO_JOB_RUN_ID: '33654881418', QUARANTINED_ZERO_JOB_RUN_ID_2: '34377182662',
      QUARANTINED_ZERO_JOB_RUN_ID_3: '34495606530', QUARANTINED_ZERO_JOB_RUN_ID_4: '35364203547',
      QUARANTINED_ZERO_JOB_RUN_ID_5: '35374125567',
      ...envChanges}});
  assert.ifError(result.error);
  return {...result, state: JSON.parse(fs.readFileSync(statePath, 'utf8')), output: fs.readFileSync(output, 'utf8')};
}
try {
  fs.mkdirSync(path.join(temporary, 'bin'));
  for (const name of ['gh', 'git', 'sleep', 'date']) {
    const script = `#!/bin/sh\nexec '${process.execPath}' '${fileURLToPath(import.meta.url)}' --fake-command '${name}' "$@"\n`;
    fs.writeFileSync(path.join(temporary, 'bin', name), script, {mode: 0o755});
  }
  for (const emptyJobReads of [0, 2]) {
    const result = execute(dispatch, {emptyJobReads});
    assert.equal(result.status, 0, result.stderr);
    assert.equal(result.state.workflowState, 'disabled_manually');
    assert.match(result.stdout, /DEPLOYMENT_JOB_ATTACHED=999/);
    assert.equal(result.state.jobReads, emptyJobReads + 1);
    const calls = result.state.calls.map((call) => call.join(' '));
    assert.ok(calls.findIndex((call) => call.endsWith('/disable')) > calls.findIndex((call) => call.includes('/jobs?')));
    assert.equal(calls.filter((call) => call.includes('/dispatches')).length, 1);
  }
  for (const changes of [
    {jobs: []}, {jobs: [{id: 123, run_id: 998}]}, {jobsApiError: true},
    {dispatchError: true}, {run: {...defaultRun, run_attempt: 2}},
    {run: {...defaultRun, workflow_id: 111}},
    {run: {...defaultRun, status: 'completed', conclusion: 'failure'}}
  ]) {
    const result = execute(dispatch, changes);
    assert.notEqual(result.status, 0);
    assert.equal(result.state.workflowState, 'disabled_manually', 'failure must reseal admissions');
    assert.doesNotMatch(result.stdout, /DEPLOYMENT_DISPATCHED=/);
    assert.equal(result.state.calls.filter((call) => call.some((arg) => arg.endsWith('/dispatches'))).length, 1);
    assert.ok(result.state.jobReads <= 90);
  }
  const accepted = execute(authorize, {run: orphan, jobs: []});
  assert.equal(accepted.status, 0, accepted.stderr);
  assert.match(accepted.stdout, /ZERO_JOB_QUARANTINE=35364203547/);
  for (const changes of [
    {run: {...orphan, id: 35364203548}}, {run: {...orphan, head_sha: 'c'.repeat(40)}},
    {run: {...orphan, head_branch: 'other'}}, {run: {...orphan, workflow_id: 111}},
    {run: {...orphan, event: 'push'}}, {run: {...orphan, run_attempt: 2}},
    {run: {...orphan, status: 'in_progress'}}, {run: {...orphan, conclusion: 'failure'}},
    {run: {...orphan, updated_at: '2026-09-18T16:00:00Z'}},
    {run: {...orphan, created_at: '2026-09-18T15:43:57Z'}},
    {jobs: [{id: 123, run_id: orphan.id}]}, {jobsApiError: true}, {ancestryOk: false}
  ]) {
    const result = execute(authorize, {run: orphan, jobs: [], ...changes});
    assert.notEqual(result.status, 0, 'changed orphan evidence must block admission');
    assert.doesNotMatch(result.stdout, /RELEASE_AUTHORIZED=/);
  }
  const sameRelease = execute(authorize, {run: orphan, jobs: [], main: base}, {GITHUB_SHA: base});
  assert.notEqual(sameRelease.status, 0, 'same release cannot quarantine a possible duplicate deployment');

  const recovered = execute(authorize, {run: uncancellable, jobs: []});
  assert.equal(recovered.status, 0, recovered.stderr);
  assert.match(recovered.stdout, /ZERO_JOB_QUARANTINE=35374125567/);
  assert.match(recovered.stdout, /RELEASE_AUTHORIZED=/);
  for (const changes of [
    {run: {...uncancellable, id: 35374125568}},
    {run: {...uncancellable, head_sha: 'c'.repeat(40)}},
    {run: {...uncancellable, head_branch: 'other'}},
    {run: {...uncancellable, workflow_id: 111}},
    {run: {...uncancellable, event: 'push'}},
    {run: {...uncancellable, run_attempt: 2}},
    {run: {...uncancellable, status: 'in_progress'}},
    {run: {...uncancellable, status: 'waiting'}},
    {run: {...uncancellable, conclusion: 'failure'}},
    {run: {...uncancellable, updated_at: '2026-09-18T17:24:15Z'}},
    {run: {...uncancellable, created_at: '2026-09-18T17:24:15Z'}},
    {jobs: [{id: 123, run_id: uncancellable.id}]},
    {jobs: [{id: 123, run_id: uncancellable.id}], reportedJobsTotal: 0},
    {reportedJobsTotal: 1}, {jobsApiError: true}, {ancestryOk: false},
    {deploymentGuardChanged: true}, {pendingApiError: true},
    {pendingDeployments: [{environment: {id: 123, name: 'test'}}]},
    {pendingDeployments: {}},
    {additionalRuns: [{...defaultRun, id: 1000, status: 'in_progress'}]},
    {additionalRuns: [{...defaultRun, id: 1000, status: 'queued'}]}
  ]) {
    const result = execute(authorize, {run: uncancellable, jobs: [], ...changes});
    assert.notEqual(result.status, 0, 'changed evidence or another active run must block recovery');
    assert.doesNotMatch(result.stdout, /RELEASE_AUTHORIZED=/);
  }
  const sameOrphanRelease = execute(authorize, {run: uncancellable, jobs: [], main: uncancellable.head_sha},
    {GITHUB_SHA: uncancellable.head_sha});
  assert.notEqual(sameOrphanRelease.status, 0, 'recovery requires a descendant release');

  if (process.argv.includes('--orphan-scope')) {
    assert.equal(execFileSync('git', ['merge-base', repairBase, 'HEAD'], {encoding: 'utf8'}).trim(), repairBase);
    const changed = execFileSync('git', ['diff', '--name-only', repairBase], {encoding: 'utf8'}).trim().split('\n').sort();
    const verify = files => assert.deepEqual([...files].sort(), [supervisorPath, branchesPath, testPath].sort());
    verify(changed);
    assert.throws(() => verify([...changed, '.github/workflows/projectpulse-deploy-test.yml']));
    for (const file of changed) assert.throws(() => verify(changed.filter(name => name !== file)));
    for (const file of [
      '.github/workflows/projectpulse-deploy-test.yml', '.github/workflows/projectpulse-deploy-production.yml',
      '.github/flowhive-psa-protected-test-candidate.json', 'scripts/release-test/flowhive-psa-admission.mjs',
      'scripts/release-test/run-module025-sow-gsd-protected-test-uat.sh'
    ]) assert.deepEqual(fs.readFileSync(file), execFileSync('git', ['show', `${repairBase}:${file}`]));
    const original = execFileSync('git', ['show', `${repairBase}:${supervisorPath}`], {encoding: 'utf8'});
    const tail = '      - name: Dispatch exact governed Protected-Test deployment and reseal admissions';
    assert.equal(source.slice(source.indexOf(tail)), original.slice(original.indexOf(tail)),
      'dispatch, startup deadline, observation, and finalization controls remain unchanged');
    assert.equal(source.split('    env:\n')[0], original.split('    env:\n')[0],
      'triggers, permissions, concurrency, and job conditions remain unchanged');
    const deploy = fs.readFileSync('.github/workflows/projectpulse-deploy-test.yml', 'utf8');
    assert.ok(deploy.indexOf('Guard exact source and validate release') < deploy.indexOf('Sign in to protected Test subscription'));
    assert.ok(deploy.includes('[[ "$(git rev-parse origin/main)" == "$TARGET_RELEASE_COMMIT" ]]'));
    assert.deepEqual(fs.readFileSync('.github/workflows/projectpulse-deploy-test.yml'),
      execFileSync('git', ['show', `${uncancellable.head_sha}:.github/workflows/projectpulse-deploy-test.yml`]));
  }

  if (process.argv.includes('--scope')) {
    assert.equal(execFileSync('git', ['merge-base', base, 'HEAD'], {encoding: 'utf8'}).trim(), base);
    const changed = execFileSync('git', ['diff', '--name-only', `${base}...HEAD`], {encoding: 'utf8'}).trim().split('\n').sort();
    assert.deepEqual(changed, [supervisorPath, branchesPath, testPath].sort());
    for (const file of ['.github/workflows/projectpulse-deploy-test.yml', '.github/workflows/projectpulse-deploy-production.yml']) {
      assert.deepEqual(fs.readFileSync(file), execFileSync('git', ['show', `${base}:${file}`]));
    }
    const original = execFileSync('git', ['show', `${base}:${supervisorPath}`], {encoding: 'utf8'});
    const tail = '      - name: Publish dispatched Protected-Test run';
    assert.equal(source.slice(source.indexOf(tail)), original.slice(original.indexOf(tail)), 'observation and finalization controls remain unchanged');
    const deploy = fs.readFileSync('.github/workflows/projectpulse-deploy-test.yml', 'utf8');
    assert.ok(deploy.indexOf('Guard exact source and validate release') < deploy.indexOf('Sign in to protected Test subscription'));
    assert.ok(deploy.includes('[[ "$(git rev-parse origin/main)" == "$TARGET_RELEASE_COMMIT" ]]'));
  }
  console.log(`MODULE025_DEPLOYMENT_STARTUP_RECOVERY=PASS scenarios=${cases}`);
} finally {
  fs.rmSync(temporary, {recursive: true, force: true});
}
