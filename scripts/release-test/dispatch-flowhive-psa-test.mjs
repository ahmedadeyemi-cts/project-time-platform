import assert from 'node:assert/strict';
import fs from 'node:fs';
import { execFileSync } from 'node:child_process';
import { authorize, repository, candidateBranch, candidatePullRequest } from './flowhive-psa-admission.mjs';

const workflowId = 315562561;
const workflowPath = '.github/workflows/projectpulse-deploy-test.yml';
const knownNonexecutingRun = 33654881418;
const dispatchSha = /^[a-f0-9]{40}$/;
export const staleRunSupersessionAttestation = Object.freeze({
  contract: 'flowhive-psa-stale-run-supersession-v1',
  approvalReference: 'FLOWHIVE-PSA-STALE-RUN-SUPERSESSION-20260909',
  workflowId,
  workflowPath,
  runId: 34377182662,
  controllerSha: 'af5fcb463384096f668345ac7cc9bd00efef0a33',
  headBranch: 'main',
  event: 'workflow_dispatch',
  runAttempt: 1,
  status: 'queued',
  conclusion: null,
  createdAt: '2026-09-09T16:30:42Z',
  updatedAt: '2026-09-09T16:30:42Z',
  supersededBefore: '2026-09-09T21:40:27Z',
  historicalBlobs: Object.freeze({
    admission: '87681012e101a2403a0a90271a3c16286fbf6028',
    dispatcher: '32501e466c75c800d41451631f55ef6902d02d5d',
    deploymentWorkflow: 'c372c4aa3a532f89fdcf72c62e17b9f3e84dd785'
  })
});
export const githubApiVersion = '2022-11-28';

export function staleRunSupersessionApproved(env = process.env) {
  return env.FLOWHIVE_PSA_STALE_RUN_SUPERSESSION_APPROVED === 'true' &&
    env.FLOWHIVE_PSA_STALE_RUN_SUPERSESSION_REFERENCE === staleRunSupersessionAttestation.approvalReference;
}

export function verifyHistoricalFenceSources({ admissionBlob, dispatcherBlob, deploymentWorkflowBlob, admission, dispatcher, deploymentWorkflow }) {
  const attestation = staleRunSupersessionAttestation;
  assert.equal(admissionBlob, attestation.historicalBlobs.admission, 'Historical admission blob changed.');
  assert.equal(dispatcherBlob, attestation.historicalBlobs.dispatcher, 'Historical dispatcher blob changed.');
  assert.equal(deploymentWorkflowBlob, attestation.historicalBlobs.deploymentWorkflow, 'Historical workflow blob changed.');
  assert.match(admission, /assert\.equal\(main\.object\.sha, process\.env\.GITHUB_SHA, 'The trusted main controller is no longer current\.'\);/,
    'The historical admission guard is missing.');
  const staleGuard = admission.indexOf('The trusted main controller is no longer current.');
  const firstAdmissionRead = admission.indexOf('const pr = await github(');
  const authorizationOutput = admission.indexOf('GITHUB_OUTPUT');
  assert.ok(staleGuard >= 0 && staleGuard < firstAdmissionRead && staleGuard < authorizationOutput,
    'Historical stale rejection must precede subsequent admission reads and output.');

  const authorizeCall = dispatcher.indexOf('await authorize();');
  const enableCall = dispatcher.indexOf("/enable`, 'PUT");
  const dispatchCall = dispatcher.indexOf("/dispatches`, 'POST");
  const cleanup = dispatcher.indexOf('finally {');
  const disableAfterCleanup = dispatcher.indexOf("/disable`, 'PUT", cleanup);
  assert.ok(authorizeCall >= 0 && authorizeCall < enableCall && enableCall < dispatchCall && cleanup < disableAfterCleanup,
    'Historical dispatcher order must authorize before enable/dispatch and reseal in finally.');

  const admissionStep = deploymentWorkflow.indexOf('name: Admit the exact reviewed PSA candidate using trusted main controls');
  const mutationSites = [
    deploymentWorkflow.indexOf('az containerapp secret set'),
    deploymentWorkflow.indexOf('az containerapp update'),
    deploymentWorkflow.indexOf('az containerapp secret remove')
  ].filter(index => index >= 0);
  assert.ok(admissionStep >= 0 && mutationSites.length > 0 && admissionStep < Math.min(...mutationSites),
    'Historical admission must precede every cloud mutation site.');
  for (const marker of [
    "always() && steps.module025_fixture.outputs.started == 'true'",
    "failure() && (steps.deploy_api.outputs.started == 'true' || steps.deploy_web.outputs.started == 'true')",
    "failure() && steps.recovery_deploy_api.outputs.started == 'true'"
  ]) assert.ok(deploymentWorkflow.includes(marker), `Historical cleanup guard is missing: ${marker}`);
  return { contract: attestation.contract, staleGuardBeforeMutation: true, cleanupRequiresStarted: true };
}

export function verifyFencedStaleRun({ workflow, run, attemptJobs, pendingDeployments, concurrencyGroups, artifacts, currentMainSha, historicalSources, serverDispatchInputs, requireSealed = true }) {
  const attestation = staleRunSupersessionAttestation;
  assert.equal(serverDispatchInputs, undefined,
    'GitHub does not expose original dispatch inputs; supersession must not rely on reconstructed inputs.');
  assert.equal(workflow?.id, attestation.workflowId, 'PSA_STALE_SUPERSESSION_WORKFLOW');
  assert.equal(workflow?.path, attestation.workflowPath, 'PSA_STALE_SUPERSESSION_WORKFLOW');
  assert.ok(['active', 'disabled_manually'].includes(workflow?.state), 'PSA_STALE_SUPERSESSION_WORKFLOW_STATE');
  assert.equal(run?.id, attestation.runId, 'PSA_STALE_SUPERSESSION_RUN');
  assert.equal(run?.workflow_id, attestation.workflowId, 'PSA_STALE_SUPERSESSION_RUN');
  assert.equal(run?.path, attestation.workflowPath, 'PSA_STALE_SUPERSESSION_RUN');
  assert.equal(run?.head_sha, attestation.controllerSha, 'PSA_STALE_SUPERSESSION_CONTROLLER');
  assert.equal(run?.head_branch, attestation.headBranch, 'PSA_STALE_SUPERSESSION_BRANCH');
  assert.equal(run?.event, attestation.event, 'PSA_STALE_SUPERSESSION_EVENT');
  assert.equal(run?.run_attempt, attestation.runAttempt, 'PSA_STALE_SUPERSESSION_ATTEMPT');
  assert.equal(run?.status, attestation.status, 'PSA_STALE_SUPERSESSION_STATUS');
  assert.equal(run?.conclusion, attestation.conclusion, 'PSA_STALE_SUPERSESSION_CONCLUSION');
  assert.equal(run?.created_at, attestation.createdAt, 'PSA_STALE_SUPERSESSION_CREATED_AT');
  assert.equal(run?.updated_at, attestation.updatedAt, 'PSA_STALE_SUPERSESSION_UPDATED_AT');
  assert.ok(run.created_at < attestation.supersededBefore, 'PSA_STALE_SUPERSESSION_EXPIRY');
  assert.notEqual(currentMainSha, attestation.controllerSha, 'The stale controller must not become current again.');
  assert.equal(attemptJobs?.total_count, 0, 'PSA_STALE_SUPERSESSION_JOBS');
  assert.deepEqual(attemptJobs?.jobs, [], 'PSA_STALE_SUPERSESSION_JOBS');
  assert.deepEqual(pendingDeployments, [], 'PSA_STALE_SUPERSESSION_PENDING_DEPLOYMENT');
  assert.equal(concurrencyGroups?.total_count, 0, 'PSA_STALE_SUPERSESSION_CONCURRENCY');
  assert.equal(artifacts?.total_count, 0, 'PSA_STALE_SUPERSESSION_ARTIFACTS');
  if (requireSealed) assert.equal(workflow.state, 'disabled_manually', 'The stale request must be sealed before supersession.');
  assert.equal(historicalSources?.admissionBlob, attestation.historicalBlobs.admission);
  assert.equal(historicalSources?.dispatcherBlob, attestation.historicalBlobs.dispatcher);
  assert.equal(historicalSources?.deploymentWorkflowBlob, attestation.historicalBlobs.deploymentWorkflow);
  verifyHistoricalFenceSources(historicalSources);
  return { fenced: true, inputIdentity: 'not-server-confirmed-and-not-used', productionMutation: false };
}

export function readHistoricalFenceSources() {
  const git = (...args) => execFileSync('git', args, { encoding: 'utf8', timeout: 30000 }).trim();
  const controller = staleRunSupersessionAttestation.controllerSha;
  const show = file => git('show', `${controller}:${file}`);
  return {
    admissionBlob: git('rev-parse', `${controller}:scripts/release-test/flowhive-psa-admission.mjs`),
    dispatcherBlob: git('rev-parse', `${controller}:scripts/release-test/dispatch-flowhive-psa-test.mjs`),
    deploymentWorkflowBlob: git('rev-parse', `${controller}:.github/workflows/projectpulse-deploy-test.yml`),
    admission: show('scripts/release-test/flowhive-psa-admission.mjs'),
    dispatcher: show('scripts/release-test/dispatch-flowhive-psa-test.mjs'),
    deploymentWorkflow: show('.github/workflows/projectpulse-deploy-test.yml')
  };
}
export function parseCommand(text) {
  const match = /^DEPLOY FLOWHIVE PSA PROTECTED TEST SHA ([0-9a-f]{40})$/.exec(text);
  assert.ok(match && match[0] === text, 'The candidate command must be exact.');
  return match[1];
}
export function buildDispatchRequest(candidateSha) {
  assert.match(candidateSha, dispatchSha, 'The submitted candidate SHA must be complete.');
  return {
    path: `actions/workflows/${workflowId}/dispatches`,
    method: 'POST',
    body: {
      ref: 'main',
      return_run_details: true,
      inputs: { release_sha: candidateSha, release_branch: candidateBranch, recover_private_runtime: false }
    }
  };
}
export function verifyDispatchInputs(inputs, candidateSha) {
  assert.match(candidateSha, dispatchSha);
  assert.deepEqual(inputs, {
    release_sha: candidateSha,
    release_branch: candidateBranch,
    recover_private_runtime: false
  }, 'The submitted dispatch inputs must remain bound to the admitted candidate.');
}
export function verifyDispatchRequest(dispatch, candidateSha) {
  assert.equal(dispatch.path, `actions/workflows/${workflowId}/dispatches`);
  assert.equal(dispatch.method, 'POST');
  assert.equal(dispatch.body.return_run_details, true, 'The documented receipt option must be a JSON body parameter.');
  verifyDispatchInputs(dispatch.body.inputs, candidateSha);
  return dispatch;
}
export function verifyDispatchReceipt(receipt) {
  const runId = Number(receipt?.workflow_run_id);
  assert.ok(Number.isSafeInteger(runId) && runId > 0, 'The dispatch response must return one workflow run ID.');
  assert.equal(new URL(receipt.run_url).pathname,
    `/repos/${repository}/actions/runs/${runId}`, 'The returned API run URL is not bound to the returned run.');
  assert.equal(new URL(receipt.html_url).pathname,
    `/ahmedadeyemi-cts/project-time-platform/actions/runs/${runId}`, 'The returned web run URL is not bound to the returned run.');
  return runId;
}
export function verifyDispatchedRun(run, controlSha, candidateSha, createdAfter, expectedRunId) {
  assert.match(candidateSha, dispatchSha);
  assert.equal(run.id, expectedRunId, 'The observed run must be the ID returned by dispatch.');
  assert.equal(run.workflow_id, workflowId);
  assert.equal(run.event, 'workflow_dispatch');
  assert.equal(run.head_branch, 'main');
  assert.equal(run.head_sha, controlSha, 'Workflow identity must be the trusted control revision, not the candidate.');
  assert.ok(run.created_at >= createdAfter);
  assert.ok(typeof run.display_title === 'string' && run.display_title.length > 0,
    'The workflow run must have a server-provided title; its value is not candidate identity.');
  return expectedRunId;
}
export function buildRequest(path, method = 'GET', body, token = process.env.GH_TOKEN) {
  const init = {
    method,
    redirect: 'error',
    signal: AbortSignal.timeout(30000),
    headers: {
      Authorization: `Bearer ${token}`,
      Accept: 'application/vnd.github+json',
      'Content-Type': 'application/json',
      'X-GitHub-Api-Version': githubApiVersion
    }
  };
  if (body !== undefined) init.body = JSON.stringify(body);
  return { url: `https://api.github.com/repos/${repository}/${path}`, init };
}
export async function dispatchOnce(api, candidateSha, controlSha, createdAfter) {
  const dispatch = verifyDispatchRequest(buildDispatchRequest(candidateSha), candidateSha);
  const receipt = await api(dispatch.path, dispatch.method, dispatch.body);
  const runId = verifyDispatchReceipt(receipt);
  const run = await api(`actions/runs/${runId}`);
  verifyDispatchedRun(run, controlSha, candidateSha, createdAfter, runId);
  return { runId, run, candidateSha, controlSha };
}
async function request(path, method = 'GET', body) {
  const { url, init } = buildRequest(path, method, body);
  const response = await fetch(url, init);
  assert.ok(response.ok, `GitHub dispatch operation failed: HTTP ${response.status}`);
  return response.status === 204 ? null : response.json();
}
function verifyWorkflow(workflow) {
  assert.equal(workflow?.id, workflowId, 'PSA_WORKFLOW_IDENTITY_OR_STATE');
  assert.equal(workflow.path, workflowPath, 'PSA_WORKFLOW_IDENTITY_OR_STATE');
  assert.ok(['active', 'disabled_manually'].includes(workflow.state), 'PSA_WORKFLOW_IDENTITY_OR_STATE');
  return workflow;
}
export async function requireIdleRuns(api, workflow, requireSealed = false) {
  const supersessionApproved = staleRunSupersessionApproved();
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
        if (run.id === staleRunSupersessionAttestation.runId && supersessionApproved) {
          const staleRun = await api(`actions/runs/${run.id}`);
          const attemptJobs = await api(`actions/runs/${run.id}/attempts/${staleRun.run_attempt}/jobs?per_page=100`);
          const pendingDeployments = await api(`actions/runs/${run.id}/pending_deployments`);
          const concurrencyGroups = await api(`actions/runs/${run.id}/concurrency_groups`);
          const artifacts = await api(`actions/runs/${run.id}/artifacts?per_page=100`);
          const main = await api('git/ref/heads/main');
          verifyFencedStaleRun({ workflow, run: staleRun, attemptJobs, pendingDeployments, concurrencyGroups,
            artifacts, currentMainSha: main.object?.sha, historicalSources: readHistoricalFenceSources(), requireSealed });
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
  await requireIdleRuns(api, workflow, false);
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
  await requireIdleRuns(api, sealed, true);
  return { ...inspection, state: sealed.state, requiresSealing: false };
}
async function main() {
  assert.equal(process.env.GITHUB_ACTOR, 'ahmedadeyemi-cts');
  assert.equal(process.env.GITHUB_EVENT_NAME, 'issue_comment');
  const event = JSON.parse(fs.readFileSync(process.env.GITHUB_EVENT_PATH, 'utf8'));
  assert.equal(event.action, 'created');
  assert.equal(event.issue?.number, candidatePullRequest);
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
    const receipt = await dispatchOnce(request, candidateSha, controlSha, createdAfter);
    dispatched = { ...receipt, productionMutation: false };
  } finally {
    // Reseal even if enable/dispatch/observation timed out. Never cancel any deployment.
    await request(`actions/workflows/${workflowId}/disable`, 'PUT');
    resealed = (await request(`actions/workflows/${workflowId}`)).state === 'disabled_manually';
    assert.ok(resealed, 'Protected Test admissions did not reseal. Operator action is required.');
    console.log(`FLOWHIVE_PSA_DISPATCH_ATTEMPTED=${dispatchAttempted} RESEALED=${resealed}`);
  }
  fs.appendFileSync(process.env.GITHUB_STEP_SUMMARY, `## FlowHive PSA candidate admission\n\nCandidate: \`${candidateSha}\`\n\nTrusted controller: \`${controlSha}\`\n\nDeployment run: ${dispatched.runId}\n\nRun identity came from the dispatch response. Feature PR #${candidatePullRequest} remains unmerged. Live acceptance is not yet established.\n`);
  await request(`issues/${candidatePullRequest}/comments`, 'POST', {
    body: `Exact FlowHive candidate admission completed. Candidate \`${candidateSha}\`; trusted main controller \`${controlSha}\`. Protected Test deployment: https://github.com/${repository}/actions/runs/${dispatched.runId}. Run identity came from the dispatch response; admissions have been resealed; no Production/private-runtime recovery is requested. This is a deployment dispatch, not a live AI success or a completed enterprise PSA release.`
  });
  console.log(`FLOWHIVE_PSA_CANDIDATE_DISPATCHED=${dispatched.runId}`);
}
if (process.argv[1]?.endsWith('/dispatch-flowhive-psa-test.mjs')) {
  const operation = process.argv.length === 3 && process.argv[2] === '--inspect-only'
    ? inspectIdleController().then(result => console.log(JSON.stringify(result)))
    : main();
  operation.catch(error => { console.error(error.message); process.exitCode = 1; });
}
