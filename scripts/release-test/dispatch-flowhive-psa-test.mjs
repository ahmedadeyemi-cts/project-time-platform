import assert from 'node:assert/strict';
import fs from 'node:fs';
import { execFileSync } from 'node:child_process';
import { authorize, repository, candidateBranch, candidatePullRequest } from './flowhive-psa-admission.mjs';

const workflowId = 315562561;
const workflowPath = '.github/workflows/projectpulse-deploy-test.yml';
const knownNonexecutingRun = 33654881418;
const dispatchSha = /^[a-f0-9]{40}$/;
const contentSha = /^[a-f0-9]{64}$/;
export const staleSupersessionAuthorizationPath = '.github/flowhive-psa-stale-run-supersession-authorization.json';
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

export function readStaleSupersessionAuthorization(filePath = process.env.FLOWHIVE_PSA_STALE_RUN_SUPERSESSION_AUTHORIZATION_FILE || staleSupersessionAuthorizationPath) {
  assert.equal(filePath, staleSupersessionAuthorizationPath,
    'Stale supersession authorization must come from the checked-in trusted-main manifest.');
  let authorization;
  try { authorization = JSON.parse(fs.readFileSync(filePath, 'utf8')); }
  catch (error) { throw new Error(`STALE_SUPERSESSION_AUTHORIZATION_INVALID: ${error.message}`); }
  return authorization;
}

export function verifyStaleSupersessionAuthorization(authorization, now = new Date()) {
  const attestation = staleRunSupersessionAttestation;
  assert.equal(authorization?.contract, attestation.contract, 'STALE_SUPERSESSION_AUTHORIZATION_CONTRACT');
  assert.equal(authorization?.approvalReference, attestation.approvalReference, 'STALE_SUPERSESSION_AUTHORIZATION_REFERENCE');
  assert.equal(authorization?.workflowId, attestation.workflowId, 'STALE_SUPERSESSION_AUTHORIZATION_WORKFLOW');
  assert.equal(authorization?.workflowPath, attestation.workflowPath, 'STALE_SUPERSESSION_AUTHORIZATION_WORKFLOW_PATH');
  assert.equal(authorization?.runId, attestation.runId, 'STALE_SUPERSESSION_AUTHORIZATION_RUN');
  assert.equal(authorization?.controllerSha, attestation.controllerSha, 'STALE_SUPERSESSION_AUTHORIZATION_CONTROLLER');
  assert.equal(authorization?.serverConfirmedDispatchInputs ?? authorization?.evidence?.serverConfirmedDispatchInputs, null,
    'Original dispatch inputs are not server-confirmed and must remain unused.');
  assert.equal(authorization?.historicalExecutionProtection?.admissionConditionalOnReleaseBranch, true,
    'Historical conditional admission evidence is required.');
  if (authorization.enabled !== true) {
    assert.equal(authorization.enabled, false, 'STALE_SUPERSESSION_AUTHORIZATION_ENABLED');
    assert.equal(authorization.approval?.status, 'not-approved', 'Inactive supersession must be explicitly unapproved.');
    assert.equal(authorization.approval?.approvedBy, null);
    assert.equal(authorization.approval?.approvedAt, null);
    assert.equal(authorization.approval?.expiresAt, null);
    return { approved: false, reason: 'not-approved' };
  }
  assert.equal(authorization.historicalExecutionProtection?.allDeploymentPathsProtected, false,
    'Historical source-path protection must remain an honest negative result.');
  verifyNativeEnvironmentProtectionContract(authorization.historicalExecutionProtection?.nativeEnvironmentBarrier);
  assert.equal(authorization.approval?.status, 'approved', 'STALE_SUPERSESSION_APPROVAL_STATUS');
  assert.match(authorization.approval?.approvedBy || '', /^[A-Za-z0-9._-]{1,100}$/, 'STALE_SUPERSESSION_APPROVER');
  const approvedAt = Date.parse(authorization.approval?.approvedAt || '');
  const expiresAt = Date.parse(authorization.approval?.expiresAt || '');
  const nowMs = now instanceof Date ? now.getTime() : Date.parse(now);
  assert.ok(Number.isFinite(approvedAt) && Number.isFinite(expiresAt) && Number.isFinite(nowMs), 'STALE_SUPERSESSION_APPROVAL_DATES');
  assert.ok(approvedAt <= nowMs, 'STALE_SUPERSESSION_APPROVAL_NOT_YET_ACTIVE');
  assert.ok(expiresAt > nowMs, 'STALE_SUPERSESSION_APPROVAL_EXPIRED');
  assert.ok(expiresAt - approvedAt <= 15 * 60 * 1000, 'STALE_SUPERSESSION_APPROVAL_WINDOW');
  return { approved: true, approvedAt: new Date(approvedAt).toISOString(), expiresAt: new Date(expiresAt).toISOString() };
}

export function staleRunSupersessionApproved(authorization = readStaleSupersessionAuthorization(), now = new Date()) {
  return verifyStaleSupersessionAuthorization(authorization, now).approved;
}

function gitBlobSha(content) {
  return execFileSync('git', ['hash-object', '--stdin'], { input: content, encoding: 'utf8', timeout: 30000 }).trim();
}

export function verifyRequestRunBinding(binding, run = staleRunSupersessionAttestation) {
  assert.equal(binding?.status, 'server-confirmed', 'STALE_SUPERSESSION_REQUEST_RUN_BINDING_STATUS');
  assert.equal(binding?.source, 'github-audit-log', 'STALE_SUPERSESSION_REQUEST_RUN_BINDING_SOURCE');
  assert.equal(binding?.repository, repository, 'STALE_SUPERSESSION_REQUEST_RUN_BINDING_REPOSITORY');
  assert.equal(binding?.workflowId, workflowId, 'STALE_SUPERSESSION_REQUEST_RUN_BINDING_WORKFLOW');
  assert.equal(binding?.workflowPath, workflowPath, 'STALE_SUPERSESSION_REQUEST_RUN_BINDING_WORKFLOW_PATH');
  assert.equal(binding?.runId, run?.id ?? run?.runId, 'STALE_SUPERSESSION_REQUEST_RUN_BINDING_RUN');
  assert.equal(binding?.controllerSha, run?.head_sha ?? run?.controllerSha,
    'STALE_SUPERSESSION_REQUEST_RUN_BINDING_CONTROLLER');
  assert.equal(binding?.event, 'workflow_dispatch', 'STALE_SUPERSESSION_REQUEST_RUN_BINDING_EVENT');
  assert.equal(binding?.ref, 'main', 'STALE_SUPERSESSION_REQUEST_RUN_BINDING_REF');
  assert.match(binding?.requestId || '', /^[A-Za-z0-9._-]{8,200}$/, 'STALE_SUPERSESSION_REQUEST_RUN_BINDING_REQUEST_ID');
  assert.match(binding?.requestBodySha256 || '', contentSha, 'STALE_SUPERSESSION_REQUEST_RUN_BINDING_BODY_HASH');
  assert.match(binding?.submittedAt || '', /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{3})?Z$/,
    'STALE_SUPERSESSION_REQUEST_RUN_BINDING_SUBMITTED_AT');
  assert.equal(binding?.response?.workflowRunId, binding.runId,
    'STALE_SUPERSESSION_REQUEST_RUN_BINDING_RESPONSE');
  return { bound: true, runId: binding.runId, controllerSha: binding.controllerSha };
}

function verifyNativeEnvironmentProtectionContract(expected) {
  assert.equal(expected?.environment, 'test', 'STALE_SUPERSESSION_NATIVE_ENVIRONMENT');
  assert.equal(expected?.canAdminsBypass, false, 'STALE_SUPERSESSION_NATIVE_BYPASS');
  assert.equal(expected?.preventSelfReview, false, 'STALE_SUPERSESSION_NATIVE_SELF_REVIEW');
  assert.equal(expected?.requiredReviewerLogin, 'ahmedadeyemi-cts', 'STALE_SUPERSESSION_NATIVE_REVIEWER');
  assert.equal(expected?.requiredReviewerId, 244059331, 'STALE_SUPERSESSION_NATIVE_REVIEWER_ID');
  assert.ok(Number.isSafeInteger(expected?.protectionRuleId) && expected.protectionRuleId > 0,
    'STALE_SUPERSESSION_NATIVE_RULE_ID');
  return expected;
}

export function verifyNativeEnvironmentProtection(actual, expected) {
  verifyNativeEnvironmentProtectionContract(expected);
  assert.equal(actual?.name, expected.environment, 'STALE_SUPERSESSION_NATIVE_READBACK_ENVIRONMENT');
  assert.equal(actual?.can_admins_bypass, expected.canAdminsBypass, 'STALE_SUPERSESSION_NATIVE_READBACK_BYPASS');
  const rule = (actual?.protection_rules || []).find(item => item?.type === 'required_reviewers');
  assert.ok(rule, 'STALE_SUPERSESSION_NATIVE_READBACK_RULE');
  assert.equal(rule.id, expected.protectionRuleId, 'STALE_SUPERSESSION_NATIVE_READBACK_RULE_ID');
  assert.equal(rule.prevent_self_review, expected.preventSelfReview, 'STALE_SUPERSESSION_NATIVE_READBACK_SELF_REVIEW');
  const reviewer = (rule.reviewers || []).find(item => item?.type === 'User')?.reviewer;
  assert.equal(reviewer?.login, expected.requiredReviewerLogin, 'STALE_SUPERSESSION_NATIVE_READBACK_REVIEWER');
  assert.equal(reviewer?.id, expected.requiredReviewerId, 'STALE_SUPERSESSION_NATIVE_READBACK_REVIEWER_ID');
  return { environment: actual.name, protectionRuleId: rule.id, reviewer: reviewer.login,
    preventSelfReview: rule.prevent_self_review, canAdminsBypass: actual.can_admins_bypass };
}

export function verifyHistoricalFenceSources({ admissionBlob, dispatcherBlob, deploymentWorkflowBlob, admission, dispatcher, deploymentWorkflow }) {
  const attestation = staleRunSupersessionAttestation;
  assert.equal(admissionBlob, attestation.historicalBlobs.admission, 'Historical admission blob changed.');
  assert.equal(dispatcherBlob, attestation.historicalBlobs.dispatcher, 'Historical dispatcher blob changed.');
  assert.equal(deploymentWorkflowBlob, attestation.historicalBlobs.deploymentWorkflow, 'Historical workflow blob changed.');
  assert.equal(gitBlobSha(admission), admissionBlob, 'Historical admission content does not match its Git blob.');
  assert.equal(gitBlobSha(dispatcher), dispatcherBlob, 'Historical dispatcher content does not match its Git blob.');
  assert.equal(gitBlobSha(deploymentWorkflow), deploymentWorkflowBlob,
    'Historical workflow content does not match its Git blob.');
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
  const conditionalAdmission = deploymentWorkflow.indexOf("if: github.event_name == 'workflow_dispatch' && inputs.release_branch == 'release/flowhive-sow-successor-20260908'", admissionStep);
  assert.ok(admissionStep >= 0 && conditionalAdmission > admissionStep,
    'Historical PSA admission must be identified as conditional on release_branch.');
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
  const deployJobIf = /jobs:\s*\n\s+deploy:\s*\n\s+if: >-\n([\s\S]*?)\n\s+environment:/.exec(deploymentWorkflow)?.[1] || '';
  const allDeploymentPathsProtected = deployJobIf.includes("github.event_name == 'push'") &&
    deployJobIf.includes("github.event_name == 'workflow_dispatch'") &&
    deployJobIf.includes("inputs.release_branch == 'release/flowhive-sow-successor-20260908'");
  const jobUsesTestEnvironment = /jobs:\s*\n\s+deploy:\s*\n[\s\S]*?\n\s+environment:\s+test\b/.test(deploymentWorkflow);
  return { contract: attestation.contract, staleGuardBeforeMutation: true, cleanupRequiresStarted: true,
    admissionConditionalOnReleaseBranch: true, allDeploymentPathsProtected, jobUsesTestEnvironment };
}

export function verifyFencedStaleRun({ workflow, run, attemptJobs, pendingDeployments, concurrencyGroups, artifacts, currentMainSha, executingControllerSha, historicalSources, authorization, authorizationNow = new Date(), serverDispatchInputs, environmentProtection, requireSealed = true }) {
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
  assert.match(currentMainSha || '', dispatchSha, 'PSA_STALE_SUPERSESSION_CURRENT_MAIN');
  assert.match(executingControllerSha || '', dispatchSha, 'PSA_STALE_SUPERSESSION_EXECUTING_CONTROLLER');
  assert.equal(currentMainSha, executingControllerSha, 'The current main ref must match the executing trusted controller.');
  assert.notEqual(currentMainSha, attestation.controllerSha, 'The stale controller must not become current again.');
  assert.equal(attemptJobs?.total_count, 0, 'PSA_STALE_SUPERSESSION_JOBS');
  assert.deepEqual(attemptJobs?.jobs, [], 'PSA_STALE_SUPERSESSION_JOBS');
  assert.deepEqual(pendingDeployments, [], 'PSA_STALE_SUPERSESSION_PENDING_DEPLOYMENT');
  assert.equal(concurrencyGroups?.total_count, 0, 'PSA_STALE_SUPERSESSION_CONCURRENCY');
  assert.equal(artifacts?.total_count, 0, 'PSA_STALE_SUPERSESSION_ARTIFACTS');
  if (requireSealed) assert.equal(workflow.state, 'disabled_manually', 'The stale request must be sealed before supersession.');
  assert.equal(verifyStaleSupersessionAuthorization(authorization, authorizationNow).approved, true, 'STALE_SUPERSESSION_NOT_APPROVED');
  assert.equal(authorization?.historicalExecutionProtection?.allDeploymentPathsProtected, false,
    'Historical source-path protection must remain an honest negative result.');
  verifyNativeEnvironmentProtection(environmentProtection, authorization?.historicalExecutionProtection?.nativeEnvironmentBarrier);
  assert.equal(historicalSources?.admissionBlob, attestation.historicalBlobs.admission);
  assert.equal(historicalSources?.dispatcherBlob, attestation.historicalBlobs.dispatcher);
  assert.equal(historicalSources?.deploymentWorkflowBlob, attestation.historicalBlobs.deploymentWorkflow);
  const sourceProof = verifyHistoricalFenceSources(historicalSources);
  assert.equal(sourceProof.allDeploymentPathsProtected, false,
    'Historical source-path protection must remain an honest negative result.');
  assert.equal(sourceProof.jobUsesTestEnvironment, true,
    'The historical deployment job is not covered by the saved Test environment gate.');
  return { fenced: true, inputIdentity: 'not-server-confirmed-and-not-used', nativeEnvironmentBarrier: true, productionMutation: false };
}

export function readHistoricalFenceSources() {
  const git = (...args) => execFileSync('git', args, { encoding: 'utf8', timeout: 30000 }).trim();
  const controller = staleRunSupersessionAttestation.controllerSha;
  const show = file => execFileSync('git', ['show', `${controller}:${file}`], { encoding: 'utf8', timeout: 30000 });
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
export async function requireIdleRuns(api, workflow, requireSealed = false,
  executingControllerSha = process.env.GITHUB_SHA,
  authorization = readStaleSupersessionAuthorization(), authorizationNow = new Date(),
  historicalSources = readHistoricalFenceSources(), environmentProtectionSnapshot) {
  const supersessionApproved = staleRunSupersessionApproved(authorization, authorizationNow);
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
          const environmentProtection = environmentProtectionSnapshot || await api('environments/test');
          verifyFencedStaleRun({ workflow, run: staleRun, attemptJobs, pendingDeployments, concurrencyGroups,
            artifacts, currentMainSha: main.object?.sha, executingControllerSha,
            historicalSources, authorization, authorizationNow, environmentProtection, requireSealed });
          continue;
        }
        throw new Error('PSA_ANOTHER_DEPLOYMENT_IS_ACTIVE');
      }
      if (runs.workflow_runs.length < 100) break;
      assert.ok(page < 10, 'Active-run pagination exceeded the bounded admission limit.');
    }
  }
}
export async function inspectIdleController(api = request, options = {}) {
  const workflow = verifyWorkflow(await api(`actions/workflows/${workflowId}`));
  await requireIdleRuns(api, workflow, false, options.executingControllerSha, options.authorization,
    options.authorizationNow, options.historicalSources, options.environmentProtection);
  return { id: workflow.id, path: workflow.path, state: workflow.state, executableActiveRuns: 0,
    requiresSealing: workflow.state === 'active' };
}
// The owner-authorized main supervisor holds the shared admission lock before
// calling this. Only admissions are disabled; no run or cloud resource changes.
export async function sealIdleController(api = request, options = {}) {
  const inspection = await inspectIdleController(api, options);
  if (inspection.requiresSealing) await api(`actions/workflows/${workflowId}/disable`, 'PUT');
  const sealed = verifyWorkflow(await api(`actions/workflows/${workflowId}`));
  assert.equal(sealed.state, 'disabled_manually', 'PSA_ADMISSION_MUST_BEGIN_SEALED');
  // Fail closed if another source admitted a run between inventory and sealing.
  await requireIdleRuns(api, sealed, true, options.executingControllerSha, options.authorization,
    options.authorizationNow, options.historicalSources, options.environmentProtection);
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
