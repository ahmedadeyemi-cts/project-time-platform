import assert from 'node:assert/strict';
import fs from 'node:fs';
import { authorize, repository, candidateBranch } from './flowhive-psa-admission.mjs';

const workflowId = 315562561;
const workflowPath = '.github/workflows/projectpulse-deploy-test.yml';
const knownNonexecutingRun = 33654881418;
export function parseCommand(text) {
  const match = /^DEPLOY FLOWHIVE PSA PROTECTED TEST SHA ([0-9a-f]{40})$/.exec(text);
  assert.ok(match && match[0] === text, 'The candidate command must be exact.');
  return match[1];
}
export function verifyDispatchedRun(run, controlSha, candidateSha, createdAfter) {
  assert.equal(run.workflow_id, workflowId);
  assert.equal(run.event, 'workflow_dispatch');
  assert.equal(run.head_branch, 'main');
  assert.equal(run.head_sha, controlSha, 'Workflow identity must be the trusted control revision, not the candidate.');
  assert.equal(run.display_title, `Protected Test ${candidateSha}`);
  assert.ok(run.created_at >= createdAfter);
  assert.ok(Number.isSafeInteger(run.id) && run.id > 0);
  return run.id;
}
async function request(path, method = 'GET', body) {
  const response = await fetch(`https://api.github.com/repos/${repository}/${path}`, {
    method, redirect: 'error', signal: AbortSignal.timeout(30000),
    headers: { Authorization: `Bearer ${process.env.GH_TOKEN}`, Accept: 'application/vnd.github+json',
      'Content-Type': 'application/json', 'X-GitHub-Api-Version': '2022-11-28' },
    ...(body ? { body: JSON.stringify(body) } : {})
  });
  assert.ok(response.ok, `GitHub dispatch operation failed: HTTP ${response.status}`);
  return response.status === 204 ? null : response.json();
}
function verifyWorkflow(workflow) {
  assert.equal(workflow?.id, workflowId, 'PSA_WORKFLOW_IDENTITY_OR_STATE');
  assert.equal(workflow.path, workflowPath, 'PSA_WORKFLOW_IDENTITY_OR_STATE');
  assert.ok(['active', 'disabled_manually'].includes(workflow.state), 'PSA_WORKFLOW_IDENTITY_OR_STATE');
  return workflow;
}
async function requireIdleRuns(api) {
  for (const status of ['queued', 'in_progress', 'waiting', 'pending', 'requested']) {
    for (let page = 1; page <= 10; page++) {
      const runs = await api(`actions/workflows/${workflowId}/runs?status=${status}&per_page=100&page=${page}`);
      assert.ok(Array.isArray(runs.workflow_runs), 'PSA_ACTIVE_RUN_INVENTORY_INVALID');
      for (const run of runs.workflow_runs) {
        if (run.status === 'completed') continue;
        if (run.id === knownNonexecutingRun) {
          const jobs = await api(`actions/runs/${run.id}/jobs?per_page=1`);
          assert.ok(jobs.total_count === 0 && Array.isArray(jobs.jobs) && jobs.jobs.length === 0,
            'PSA_ANOTHER_DEPLOYMENT_IS_ACTIVE');
          continue;
        }
        throw new Error('PSA_ANOTHER_DEPLOYMENT_IS_ACTIVE');
      }
      if (runs.workflow_runs.length < 100) break;
      assert.ok(page < 10, 'Active-run pagination exceeded the bounded admission limit.');
    }
  }
}
export async function inspectIdleController(api = request) {
  const workflow = verifyWorkflow(await api(`actions/workflows/${workflowId}`));
  await requireIdleRuns(api);
  return { id: workflow.id, path: workflow.path, state: workflow.state, executableActiveRuns: 0,
    requiresSealing: workflow.state === 'active' };
}
// The owner-authorized main supervisor holds the shared admission lock before
// calling this. Only admissions are disabled; no run or cloud resource changes.
export async function sealIdleController(api = request) {
  const inspection = await inspectIdleController(api);
  if (inspection.requiresSealing) await api(`actions/workflows/${workflowId}/disable`, 'PUT');
  const sealed = verifyWorkflow(await api(`actions/workflows/${workflowId}`));
  assert.equal(sealed.state, 'disabled_manually', 'PSA_ADMISSION_MUST_BEGIN_SEALED');
  // Fail closed if another source admitted a run between inventory and sealing.
  await requireIdleRuns(api);
  return { ...inspection, state: sealed.state, requiresSealing: false };
}
async function main() {
  assert.equal(process.env.GITHUB_ACTOR, 'ahmedadeyemi-cts');
  assert.equal(process.env.GITHUB_EVENT_NAME, 'issue_comment');
  const event = JSON.parse(fs.readFileSync(process.env.GITHUB_EVENT_PATH, 'utf8'));
  assert.equal(event.action, 'created');
  assert.equal(event.issue?.number, 872);
  assert.equal(event.comment?.user?.login, 'ahmedadeyemi-cts');
  assert.ok(event.issue.pull_request);
  const candidateSha = parseCommand(event.comment.body);
  process.env.TARGET_RELEASE_COMMIT = candidateSha;
  process.env.TARGET_RELEASE_BRANCH = candidateBranch;
  await authorize();
  const controlSha = process.env.GITHUB_SHA;
  await sealIdleController();
  const controlCheck = await request('git/ref/heads/main');
  assert.equal(controlCheck.object.sha, controlSha, 'Main changed during admission; re-review is required.');
  let resealed = false;
  let dispatchAttempted = false;
  let dispatched;
  try {
    const createdAfter = new Date().toISOString().replace(/\.\d{3}Z$/, 'Z');
    await request(`actions/workflows/${workflowId}/enable`, 'PUT');
    assert.equal((await request(`actions/workflows/${workflowId}`)).state, 'active');
    // Never retry this write. A lost response is an unknown outcome requiring inspection.
    dispatchAttempted = true;
    await request(`actions/workflows/${workflowId}/dispatches`, 'POST', {
      ref: 'main', inputs: { release_sha: candidateSha, release_branch: candidateBranch, recover_private_runtime: false }
    });
    for (let attempt = 0; attempt < 60; attempt++) {
      const runs = await request(`actions/workflows/${workflowId}/runs?event=workflow_dispatch&per_page=100`);
      const matches = runs.workflow_runs.filter(run => run.head_sha === controlSha && run.head_branch === 'main'
        && run.created_at >= createdAfter && run.display_title === `Protected Test ${candidateSha}`);
      assert.ok(matches.length <= 1, 'Multiple matching dispatches require manual investigation.');
      if (matches.length === 1) {
        const runId = verifyDispatchedRun(matches[0], controlSha, candidateSha, createdAfter);
        dispatched = { runId, controlSha, candidateSha, productionMutation: false };
        break;
      }
      await new Promise(resolve => setTimeout(resolve, 2000));
    }
    assert.ok(dispatched, 'Dispatch outcome is not confirmed. Do not repost the command; inspect workflow runs.');
  } finally {
    // Reseal even if enable/dispatch/observation timed out. Never cancel any deployment.
    await request(`actions/workflows/${workflowId}/disable`, 'PUT');
    resealed = (await request(`actions/workflows/${workflowId}`)).state === 'disabled_manually';
    assert.ok(resealed, 'Protected Test admissions did not reseal. Operator action is required.');
    console.log(`FLOWHIVE_PSA_DISPATCH_ATTEMPTED=${dispatchAttempted} RESEALED=${resealed}`);
  }
  fs.appendFileSync(process.env.GITHUB_STEP_SUMMARY, `## FlowHive PSA candidate admission\n\nCandidate: \`${candidateSha}\`\n\nTrusted controller: \`${controlSha}\`\n\nDeployment run: ${dispatched.runId}\n\nAdmissions resealed. Feature PR #872 remains unmerged. Live acceptance is not yet established.\n`);
  await request('issues/872/comments', 'POST', {
    body: `Exact FlowHive candidate admission completed. Candidate \`${candidateSha}\`; trusted main controller \`${controlSha}\`. Protected Test deployment: https://github.com/${repository}/actions/runs/${dispatched.runId}. Admissions have been resealed; no Production/private-runtime recovery is requested. This is a deployment dispatch, not a live AI success or a completed enterprise PSA release.`
  });
  console.log(`FLOWHIVE_PSA_CANDIDATE_DISPATCHED=${dispatched.runId}`);
}
if (process.argv[1]?.endsWith('/dispatch-flowhive-psa-test.mjs')) {
  const operation = process.argv.length === 3 && process.argv[2] === '--inspect-only'
    ? inspectIdleController().then(result => console.log(JSON.stringify(result)))
    : main();
  operation.catch(error => { console.error(error.message); process.exitCode = 1; });
}
