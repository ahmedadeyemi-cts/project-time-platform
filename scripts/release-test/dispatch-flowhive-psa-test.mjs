import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { authorize, repository, candidateBranch, candidatePullRequest, approvalPath, verifyApproval } from './flowhive-psa-admission.mjs';

const workflowId = 315562561;
const workflowPath = '.github/workflows/projectpulse-deploy-test.yml';
export const unresolvedRequestIds = Object.freeze([34495606530, 34377182662, 33654881418]);
const knownNonexecutingRun = unresolvedRequestIds[2];
const dispatchSha = /^[a-f0-9]{40}$/;
const contentSha = /^[a-f0-9]{64}$/;
export const staleSupersessionAuthorizationPath = '.github/flowhive-psa-stale-run-supersession-authorization.json';
export const protectedCutoverAuthorizationPath = '.github/flowhive-psa-protected-cutover.json';
export const protectedCutoverRunIds = Object.freeze([34495606530, 34377182662, 33654881418]);
export const protectedCutoverRunAttestations = Object.freeze([
  Object.freeze({
    runId: 34495606530,
    controllerSha: '9f30078c2c407d4d3576ccefd663a145be50c6c4',
    headBranch: 'main', event: 'workflow_dispatch', runAttempt: 1,
    status: 'queued', conclusion: null,
    createdAt: '2026-09-10T15:26:05Z', updatedAt: '2026-09-10T15:26:05Z',
    historicalBlobs: Object.freeze({
      admission: '2236a05888afff28cd971c9c2cb13d27017acac3',
      dispatcher: '6f960293b8684f837a0f810a3773e95d53469f4e',
      deploymentWorkflow: '182da71f9919ac21d42e5c00425f9e881e927a1b'
    })
  }),
  Object.freeze({
    runId: 34377182662,
    controllerSha: 'af5fcb463384096f668345ac7cc9bd00efef0a33',
    headBranch: 'main', event: 'workflow_dispatch', runAttempt: 1,
    status: 'queued', conclusion: null,
    createdAt: '2026-09-09T16:30:42Z', updatedAt: '2026-09-09T16:30:42Z',
    historicalBlobs: Object.freeze({
      admission: '87681012e101a2403a0a90271a3c16286fbf6028',
      dispatcher: '32501e466c75c800d41451631f55ef6902d02d5d',
      deploymentWorkflow: 'c372c4aa3a532f89fdcf72c62e17b9f3e84dd785'
    })
  }),
  Object.freeze({
    runId: 33654881418,
    controllerSha: '3d02b4cb62683b96554186f9d3a782fc1f21ca54',
    headBranch: 'fix/shared-project-document-planning-20260819', event: 'workflow_dispatch', runAttempt: 1,
    status: 'queued', conclusion: null,
    createdAt: '2026-09-02T16:26:03Z', updatedAt: '2026-09-02T16:26:03Z',
    historicalBlobs: Object.freeze({ deploymentWorkflow: 'd22cd6bfed31a9eb1dbf2475ad50a0707029d800' })
  })
]);
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
export const dispatchEvidenceSchema = 'flowhive-psa-dispatch-attempt-v1';
export const dispatchEvidenceDefaultFile = 'flowhive-psa-dispatch-attempt.json';
export const singleUseClaimPrefix = 'FLOWHIVE_PSA_ADMISSION_CLAIM_V1';
const trustedClaimAuthor = Object.freeze({ login: 'ahmedadeyemi-cts', id: 244059331 });
const workflowClaimAuthor = Object.freeze({ login: 'github-actions[bot]', id: 41898282 });

export class GithubApiError extends Error {
  constructor({ stage, method, path: requestPath, status, requestId }) {
    super(`GITHUB_API_REQUEST_FAILED stage=${stage} method=${method} path=${requestPath} status=${status} requestId=${requestId || 'unknown'}`);
    this.name = 'GithubApiError';
    this.stage = stage;
    this.method = method;
    this.path = requestPath;
    this.status = status;
    this.requestId = requestId || null;
  }
}

function nowIso() {
  return new Date().toISOString();
}

function fingerprint(value) {
  return createHash('sha256').update(JSON.stringify(value)).digest('hex');
}

function evidencePath(filePath = process.env.FLOWHIVE_PSA_DISPATCH_EVIDENCE_FILE) {
  assert.ok(filePath, 'FLOWHIVE_PSA_DISPATCH_EVIDENCE_FILE is required for a dispatch attempt.');
  return filePath;
}

export function persistDispatchEvidence(record, filePath = evidencePath()) {
  const destination = path.resolve(filePath);
  fs.mkdirSync(path.dirname(destination), { recursive: true });
  const temporary = `${destination}.${process.pid}.tmp`;
  fs.writeFileSync(temporary, `${JSON.stringify({ ...record, updatedAt: nowIso() }, null, 2)}\n`, { mode: 0o600 });
  fs.renameSync(temporary, destination);
  return destination;
}

export function readAdmissionExecutionContext(env = process.env) {
  const admissionRunId = Number(env.GITHUB_RUN_ID);
  const admissionRunAttempt = Number(env.GITHUB_RUN_ATTEMPT);
  assert.ok(Number.isSafeInteger(admissionRunId) && admissionRunId > 0,
    'FLOWHIVE_PSA_ADMISSION_RUN_ID_REQUIRED');
  assert.ok(Number.isSafeInteger(admissionRunAttempt) && admissionRunAttempt > 0,
    'FLOWHIVE_PSA_ADMISSION_RUN_ATTEMPT_REQUIRED');
  return { admissionRunId, admissionRunAttempt };
}

export function createDispatchEvidence({ candidateSha, controlSha, dispatch, admissionRunId = null,
  admissionRunAttempt = null, attempt = admissionRunAttempt ?? 1, startedAt = nowIso(),
  cutoverAssessment = null, singleUseClaim = null, preSubmissionValidation = null,
  controllerTransition = null }) {
  return {
    schema: dispatchEvidenceSchema,
    repository,
    candidatePullRequest,
    candidateSha,
    controllerSha: controlSha,
    workflowId,
    workflowPath,
    admissionRunId,
    admissionRunAttempt,
    attempt,
    startedAt,
    updatedAt: startedAt,
    stage: 'pre-dispatch',
    phase: 'dispatch-pending',
    dispatchAttempted: false,
    dispatchWriteCount: 0,
    dispatch: {
      method: dispatch.method,
      path: dispatch.path,
      requestFingerprint: fingerprint(dispatch.body)
    },
    cutoverAssessment,
    singleUseClaim,
    preSubmissionValidation,
    controllerTransition,
    run: null,
    errors: [],
    reportingErrors: []
  };
}

export function safeApiError(error, fallbackStage = 'unknown') {
  return {
    stage: error?.stage || fallbackStage,
    method: error?.method || null,
    path: error?.path || null,
    status: error?.status ?? null,
    requestId: error?.requestId || null,
    type: error?.name || 'Error',
    at: nowIso()
  };
}

export function recordReportingFailure(evidence, error, stage) {
  return {
    ...evidence,
    reporting: {
      status: 'failed',
      stage,
      errors: [...(evidence.reporting?.errors || []), safeApiError(error, stage)]
    },
    reportingErrors: [...(evidence.reportingErrors || []), safeApiError(error, stage)]
  };
}

function runPhase(run) {
  if (run?.status === 'queued') return 'accepted-awaiting-scheduling';
  if (run?.status === 'waiting' || run?.status === 'pending') return 'awaiting-environment-review';
  if (run?.status === 'in_progress') return 'executing';
  if (run?.status === 'completed' && run?.conclusion === 'success') return 'completed-unverified';
  if (run?.status === 'completed') return 'failed-or-partial';
  return 'accepted';
}

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

export function readProtectedCutoverAuthorization(filePath = process.env.FLOWHIVE_PSA_PROTECTED_CUTOVER_FILE || protectedCutoverAuthorizationPath) {
  assert.equal(filePath, protectedCutoverAuthorizationPath,
    'Protected cutover authorization must come from the checked-in trusted-main manifest.');
  try { return JSON.parse(fs.readFileSync(filePath, 'utf8')); }
  catch (error) { throw new Error(`PROTECTED_CUTOVER_AUTHORIZATION_INVALID: ${error.message}`); }
}

function verifyBoundedApproval(authorization, now) {
  assert.equal(authorization?.approval?.status, 'approved', 'PROTECTED_CUTOVER_APPROVAL_STATUS');
  assert.match(authorization.approval.approvedBy || '', /^[A-Za-z0-9._-]{1,100}$/, 'PROTECTED_CUTOVER_APPROVER');
  const approvedAt = Date.parse(authorization.approval.approvedAt || '');
  const expiresAt = Date.parse(authorization.approval.expiresAt || '');
  const nowMs = now instanceof Date ? now.getTime() : Date.parse(now);
  assert.ok(Number.isFinite(approvedAt) && Number.isFinite(expiresAt) && Number.isFinite(nowMs), 'PROTECTED_CUTOVER_APPROVAL_DATES');
  assert.ok(approvedAt <= nowMs, 'PROTECTED_CUTOVER_APPROVAL_NOT_YET_ACTIVE');
  assert.ok(expiresAt > nowMs, 'PROTECTED_CUTOVER_APPROVAL_EXPIRED');
  assert.ok(expiresAt - approvedAt <= 15 * 60 * 1000, 'PROTECTED_CUTOVER_APPROVAL_WINDOW');
  return { approvedAt: new Date(approvedAt).toISOString(), expiresAt: new Date(expiresAt).toISOString() };
}

export function verifyProtectedCutoverAuthorization(authorization, now = new Date()) {
  assert.equal(authorization?.contract, 'flowhive-psa-protected-cutover-v1', 'PROTECTED_CUTOVER_CONTRACT');
  assert.equal(authorization?.approvalReference, 'FLOWHIVE-PSA-PROTECTED-CUTOVER-20260910', 'PROTECTED_CUTOVER_REFERENCE');
  assert.deepEqual(authorization?.candidate, {
    pullRequest: candidatePullRequest,
    branch: candidateBranch,
    sha: '95abbb0aa2445a33fda68e9de542f9446c3e2204'
  }, 'PROTECTED_CUTOVER_CANDIDATE');
  assert.deepEqual(authorization?.workflow, {
    id: workflowId, path: workflowPath, controllerBranch: 'main', event: 'workflow_dispatch',
    transition: 'disabled_manually-to-active-once', allowControllerActivation: authorization?.workflow?.allowControllerActivation
  }, 'PROTECTED_CUTOVER_WORKFLOW');
  assert.equal(authorization?.serverDispatchInputsConfirmed, false,
    'Original historical dispatch inputs are not server-confirmed and must remain unused.');
  verifyNativeEnvironmentProtectionContract(authorization?.environment);
  assert.deepEqual(authorization?.requests?.map(request => request.runId), protectedCutoverRunIds,
    'PROTECTED_CUTOVER_REQUEST_SET');
  for (const expected of protectedCutoverRunAttestations) {
    const configured = authorization.requests.find(request => request.runId === expected.runId);
    assert.ok(configured, `PROTECTED_CUTOVER_REQUEST_MISSING ${expected.runId}`);
    for (const field of ['controllerSha', 'headBranch', 'event', 'runAttempt', 'status', 'conclusion', 'createdAt', 'updatedAt']) {
      assert.equal(configured[field], expected[field], `PROTECTED_CUTOVER_REQUEST_${field} ${expected.runId}`);
    }
    assert.deepEqual(configured.historicalBlobs, expected.historicalBlobs,
      `PROTECTED_CUTOVER_SOURCE_ATTESTATION ${expected.runId}`);
  }
  if (authorization.enabled !== true) {
    assert.equal(authorization.enabled, false, 'PROTECTED_CUTOVER_ENABLED');
    assert.equal(authorization.workflow.allowControllerActivation, false,
      'Inactive protected cutover cannot activate the controller.');
    assert.equal(authorization.activationDecision, 'hold', 'Inactive protected cutover must remain held.');
    assert.deepEqual(authorization.approval, { status: 'not-approved', approvedBy: null, approvedAt: null, expiresAt: null },
      'Inactive protected cutover must not contain approval data.');
    return { approved: false, reason: 'not-approved' };
  }
  assert.equal(authorization.activationDecision, 'approved', 'PROTECTED_CUTOVER_DECISION');
  assert.equal(authorization.workflow.allowControllerActivation, true,
    'Active protected cutover must explicitly authorize the one-time controller transition.');
  return { approved: true, ...verifyBoundedApproval(authorization, now) };
}

export function readInspectOnlyContext(env = process.env, authorization = readProtectedCutoverAuthorization(), nowOverride = null) {
  assert.equal(env.GITHUB_REPOSITORY, repository, 'PROTECTED_CUTOVER_INSPECT_REPOSITORY');
  assert.equal(env.GITHUB_REF, 'refs/heads/main', 'PROTECTED_CUTOVER_INSPECT_REF');
  assert.equal(env.GITHUB_EVENT_NAME, 'workflow_dispatch', 'PROTECTED_CUTOVER_INSPECT_EVENT');
  assert.ok(env.GH_TOKEN, 'PROTECTED_CUTOVER_INSPECT_TOKEN');
  assert.match(env.GITHUB_SHA || '', dispatchSha, 'PROTECTED_CUTOVER_INSPECT_CONTROLLER');
  const approval = JSON.parse(fs.readFileSync(approvalPath, 'utf8'));
  verifyApproval(approval, authorization?.candidate?.sha);
  const authorizationNow = nowOverride || (env.FLOWHIVE_PSA_AUTHORIZATION_NOW
    ? new Date(env.FLOWHIVE_PSA_AUTHORIZATION_NOW) : new Date());
  const authorizationState = verifyProtectedCutoverAuthorization(authorization, authorizationNow);
  return {
    candidateSha: authorization.candidate.sha,
    controllerSha: env.GITHUB_SHA,
    authorization,
    authorizationState,
    authorizationNow
  };
}

export async function inspectReleaseCutover(api = request, options = {}) {
  const authorization = options.authorization || readProtectedCutoverAuthorization();
  const env = options.env || process.env;
  const context = readInspectOnlyContext(env, authorization, options.authorizationNow || null);
  const result = await verifyReleaseCutover(api, {
    candidateSha: context.candidateSha,
    executingControllerSha: context.controllerSha,
    authorization,
    authorizationNow: options.authorizationNow || context.authorizationNow
  });
  return { operation: 'inspect-only', context: {
    candidateSha: context.candidateSha,
    controllerSha: context.controllerSha,
    authorizationReference: authorization.approvalReference,
    authorizationState: context.authorizationState
  }, ...result };
}

function gitBlobSha(content) {
  return execFileSync('git', ['hash-object', '--stdin'], { input: content, encoding: 'utf8', timeout: 30000 }).trim();
}

function gitShowIfPresent(revision, file) {
  try { return execFileSync('git', ['show', `${revision}:${file}`], { encoding: 'utf8', timeout: 30000 }); }
  catch { return null; }
}

function verifyHistoricalAdmissionSource(source, expectedBlob, runId) {
  assert.equal(gitBlobSha(source), expectedBlob, `PROTECTED_CUTOVER_ADMISSION_BLOB ${runId}`);
  const guard = source.indexOf('The trusted main controller is no longer current.');
  const firstRead = source.indexOf('const pr = await github(');
  assert.ok(guard >= 0 && firstRead >= 0 && guard < firstRead,
    `PROTECTED_CUTOVER_ADMISSION_GUARD ${runId}`);
  return { blob: expectedBlob, staleGuardBeforeAdmissionReads: true };
}

function verifyHistoricalDispatcherSource(source, expectedBlob, runId) {
  assert.equal(gitBlobSha(source), expectedBlob, `PROTECTED_CUTOVER_DISPATCHER_BLOB ${runId}`);
  const authorizeCall = source.indexOf('await authorize();');
  const dispatchCall = source.indexOf("/dispatches`, 'POST");
  assert.ok(authorizeCall >= 0 && dispatchCall > authorizeCall,
    `PROTECTED_CUTOVER_DISPATCH_AUTHORIZATION_ORDER ${runId}`);
  return { blob: expectedBlob, authorizationBeforeDispatch: true };
}

export function verifyProtectedHistoricalWorkflowSource(source, expectedBlob, runId) {
  assert.equal(gitBlobSha(source), expectedBlob, `PROTECTED_CUTOVER_WORKFLOW_BLOB ${runId}`);
  const jobsStart = source.indexOf('\njobs:\n');
  assert.ok(jobsStart >= 0, `PROTECTED_CUTOVER_WORKFLOW_JOBS_SECTION ${runId}`);
  const jobsSection = source.slice(jobsStart + 1);
  const jobNames = [...jobsSection.matchAll(/^  ([A-Za-z0-9_-]+):\s*$/gm)].map(match => match[1]);
  assert.deepEqual(jobNames, ['deploy'], `PROTECTED_CUTOVER_WORKFLOW_JOBS ${runId}`);
  const environmentLine = source.split('\n').findIndex(line => /^    environment:\s+test\s*$/.test(line));
  assert.ok(environmentLine >= 0, `PROTECTED_CUTOVER_TEST_ENVIRONMENT ${runId}`);
  assert.doesNotMatch(source, /^\s+environment:\s+(?:production|prod)\s*$/im,
    `PROTECTED_CUTOVER_PRODUCTION_ENVIRONMENT ${runId}`);
  assert.match(source, /group:\s*projectpulse-deploy-test/, `PROTECTED_CUTOVER_SERIALIZATION ${runId}`);
  assert.match(source, /queue:\s*max/, `PROTECTED_CUTOVER_SERIALIZATION_QUEUE ${runId}`);
  assert.match(source, /cancel-in-progress:\s*false/, `PROTECTED_CUTOVER_SERIALIZATION_CANCEL ${runId}`);
  const mutationPattern = /az containerapp (?:secret set|secret remove|update)|az acr build|docker push|psql\s+-X\s+-v\s+ON_ERROR_STOP=1\s+--file/g;
  const mutationLines = [];
  for (const [index, line] of source.split('\n').entries()) {
    if (mutationPattern.test(line)) mutationLines.push(index);
    mutationPattern.lastIndex = 0;
  }
  assert.ok(mutationLines.length > 0, `PROTECTED_CUTOVER_MUTATION_SITES ${runId}`);
  assert.ok(mutationLines.every(index => index > environmentLine),
    `PROTECTED_CUTOVER_MUTATION_BEFORE_NATIVE_GATE ${runId}`);
  return { blob: expectedBlob, jobNames, environmentLine: environmentLine + 1,
    mutationLines: mutationLines.map(index => index + 1), allMutationPathsBehindTest: true };
}

export function readProtectedCutoverSources(authorization = readProtectedCutoverAuthorization()) {
  return protectedCutoverRunAttestations.map(expected => {
    const configured = authorization.requests.find(request => request.runId === expected.runId);
    const workflow = gitShowIfPresent(expected.controllerSha, workflowPath);
    assert.ok(workflow, `PROTECTED_CUTOVER_WORKFLOW_SOURCE_MISSING ${expected.runId}`);
    const admission = expected.historicalBlobs.admission
      ? gitShowIfPresent(expected.controllerSha, 'scripts/release-test/flowhive-psa-admission.mjs') : null;
    const dispatcher = expected.historicalBlobs.dispatcher
      ? gitShowIfPresent(expected.controllerSha, 'scripts/release-test/dispatch-flowhive-psa-test.mjs') : null;
    assert.equal(configured?.historicalBlobs?.deploymentWorkflow, expected.historicalBlobs.deploymentWorkflow,
      `PROTECTED_CUTOVER_CONFIGURED_SOURCE ${expected.runId}`);
    return {
      runId: expected.runId,
      workflow: verifyProtectedHistoricalWorkflowSource(workflow, expected.historicalBlobs.deploymentWorkflow, expected.runId),
      admission: admission ? verifyHistoricalAdmissionSource(admission, expected.historicalBlobs.admission, expected.runId) : null,
      dispatcher: dispatcher ? verifyHistoricalDispatcherSource(dispatcher, expected.historicalBlobs.dispatcher, expected.runId) : null
    };
  });
}

function verifyProtectedRunIdentity(run, expected, workflow, allowDisabledWorkflow = false) {
  assert.equal(run?.id, expected.runId, `PROTECTED_CUTOVER_RUN_ID ${expected.runId}`);
  assert.equal(run?.workflow_id, workflowId, `PROTECTED_CUTOVER_RUN_WORKFLOW ${expected.runId}`);
  assert.equal(run?.path, workflowPath, `PROTECTED_CUTOVER_RUN_PATH ${expected.runId}`);
  assert.equal(run?.event, expected.event, `PROTECTED_CUTOVER_RUN_EVENT ${expected.runId}`);
  assert.equal(run?.head_branch, expected.headBranch, `PROTECTED_CUTOVER_RUN_BRANCH ${expected.runId}`);
  assert.equal(run?.head_sha, expected.controllerSha, `PROTECTED_CUTOVER_RUN_CONTROLLER ${expected.runId}`);
  assert.equal(run?.run_attempt, expected.runAttempt, `PROTECTED_CUTOVER_RUN_ATTEMPT ${expected.runId}`);
  assert.equal(run?.status, expected.status, `PROTECTED_CUTOVER_RUN_STATUS ${expected.runId}`);
  assert.equal(run?.conclusion, expected.conclusion, `PROTECTED_CUTOVER_RUN_CONCLUSION ${expected.runId}`);
  assert.equal(run?.created_at, expected.createdAt, `PROTECTED_CUTOVER_RUN_CREATED ${expected.runId}`);
  assert.equal(run?.updated_at, expected.updatedAt, `PROTECTED_CUTOVER_RUN_UPDATED ${expected.runId}`);
  assert.equal(workflow?.id, workflowId, `PROTECTED_CUTOVER_WORKFLOW_ID ${expected.runId}`);
  assert.equal(workflow?.path, workflowPath, `PROTECTED_CUTOVER_WORKFLOW_PATH ${expected.runId}`);
  assert.ok(workflow?.state === 'active' || (allowDisabledWorkflow && workflow?.state === 'disabled_manually'),
    'PROTECTED_CUTOVER_CONTROLLER_NOT_ACTIVE');
  return run;
}

function verifyAttemptJobs(attemptJobs, runId, attempt) {
  assert.equal(attemptJobs?.total_count, 0, `PROTECTED_CUTOVER_ATTEMPT_JOBS ${runId}/${attempt}`);
  assert.deepEqual(attemptJobs?.jobs, [], `PROTECTED_CUTOVER_ATTEMPT_JOBS ${runId}/${attempt}`);
}

export function verifyProtectedRunObservation({ workflow, expected, run, attempts, pendingDeployments, approvals,
  concurrencyGroups, artifacts, currentMainSha, executingControllerSha, environmentProtection, authorization,
  allowDisabledWorkflow = false }) {
  verifyProtectedRunIdentity(run, expected, workflow, allowDisabledWorkflow);
  assert.equal(currentMainSha, executingControllerSha, 'PROTECTED_CUTOVER_MAIN_CONTROLLER_CHANGED');
  assert.match(currentMainSha || '', dispatchSha, 'PROTECTED_CUTOVER_CURRENT_MAIN_INVALID');
  assert.equal(authorization?.serverDispatchInputsConfirmed, false,
    'Historical dispatch inputs are not server-confirmed and must not be inferred.');
  assert.equal(attempts?.length, expected.runAttempt, `PROTECTED_CUTOVER_ATTEMPT_HISTORY ${expected.runId}`);
  for (const attempt of attempts) verifyAttemptJobs(attempt.jobs, expected.runId, attempt.attempt);
  assert.deepEqual(pendingDeployments, [], `PROTECTED_CUTOVER_PENDING_DEPLOYMENT ${expected.runId}`);
  assert.ok(Array.isArray(approvals), `PROTECTED_CUTOVER_APPROVAL_HISTORY_INVALID ${expected.runId}`);
  assert.deepEqual(approvals, [], `PROTECTED_CUTOVER_APPROVAL_HISTORY ${expected.runId}`);
  assert.equal(concurrencyGroups?.total_count, 0, `PROTECTED_CUTOVER_CONCURRENCY ${expected.runId}`);
  assert.deepEqual(concurrencyGroups?.concurrency_groups, [], `PROTECTED_CUTOVER_CONCURRENCY ${expected.runId}`);
  assert.equal(artifacts?.total_count, 0, `PROTECTED_CUTOVER_ARTIFACTS ${expected.runId}`);
  assert.deepEqual(artifacts?.artifacts, [], `PROTECTED_CUTOVER_ARTIFACTS ${expected.runId}`);
  verifyNativeEnvironmentProtection(environmentProtection, authorization.environment);
  return {
    runId: expected.runId, status: run.status, conclusion: run.conclusion,
    runAttempt: run.run_attempt, attemptCount: attempts.length,
    jobs: 0, pendingDeployments: 0, approvals: approvals.length,
    concurrencyGroups: 0, artifacts: 0, nativeEnvironmentProtected: true
  };
}

async function readProtectedRunObservation(api, workflow, expected, options) {
  const run = await api(`actions/runs/${expected.runId}`, 'GET', undefined, 'protected-cutover-run-read');
  const attempts = [];
  const attemptCount = Number(run?.run_attempt);
  assert.ok(Number.isSafeInteger(attemptCount) && attemptCount > 0,
    `PROTECTED_CUTOVER_ATTEMPT_COUNT ${expected.runId}`);
  for (let attempt = 1; attempt <= attemptCount; attempt += 1) {
    attempts.push({ attempt, jobs: await api(`actions/runs/${expected.runId}/attempts/${attempt}/jobs?per_page=100`,
      'GET', undefined, 'protected-cutover-attempt-jobs-read') });
  }
  const [pendingDeployments, approvals, concurrencyGroups, artifacts] = await Promise.all([
    api(`actions/runs/${expected.runId}/pending_deployments`, 'GET', undefined, 'protected-cutover-pending-read'),
    api(`actions/runs/${expected.runId}/approvals`, 'GET', undefined, 'protected-cutover-approval-history-read'),
    api(`actions/runs/${expected.runId}/concurrency_groups`, 'GET', undefined, 'protected-cutover-concurrency-read'),
    api(`actions/runs/${expected.runId}/artifacts?per_page=100`, 'GET', undefined, 'protected-cutover-artifacts-read')
  ]);
  return {
    expected, run, attempts, pendingDeployments, approvals, concurrencyGroups, artifacts,
    observation: verifyProtectedRunObservation({ workflow, expected, run, attempts, pendingDeployments, approvals,
      concurrencyGroups, artifacts, currentMainSha: options.currentMainSha,
      executingControllerSha: options.executingControllerSha,
      environmentProtection: options.environmentProtection, authorization: options.authorization,
      allowDisabledWorkflow: options.allowDisabledWorkflow === true })
  };
}

export async function assessProtectedCutover(api = request, options = {}) {
  const authorization = options.authorization || readProtectedCutoverAuthorization();
  const authorizationNow = options.authorizationNow || new Date();
  assert.equal(verifyProtectedCutoverAuthorization(authorization, authorizationNow).approved, true,
    'PROTECTED_CUTOVER_NOT_APPROVED');
  const candidateSha = options.candidateSha;
  assert.equal(candidateSha, authorization.candidate.sha, 'PROTECTED_CUTOVER_CANDIDATE_MISMATCH');
  const executingControllerSha = options.executingControllerSha || process.env.GITHUB_SHA;
  assert.match(executingControllerSha || '', dispatchSha, 'PROTECTED_CUTOVER_CONTROLLER_INVALID');
  const [workflow, main] = await Promise.all([
    verifyWorkflow(await api(`actions/workflows/${workflowId}`, 'GET', undefined, 'protected-cutover-controller-read')),
    api('git/ref/heads/main', 'GET', undefined, 'protected-cutover-main-readback')
  ]);
  if (options.allowDisabledWorkflow === true) {
    assert.equal(workflow.state, 'disabled_manually', 'PROTECTED_CUTOVER_ACTIVATION_PRESTATE');
  } else {
    assert.equal(workflow.state, 'active', 'PROTECTED_CUTOVER_CONTROLLER_NOT_ACTIVE');
  }
  assert.equal(main?.object?.sha, executingControllerSha, 'PROTECTED_CUTOVER_MAIN_CONTROLLER_CHANGED');
  const environmentProtection = await api('environments/test', 'GET', undefined, 'protected-cutover-environment-read');
  verifyNativeEnvironmentProtection(environmentProtection, authorization.environment);
  const sources = readProtectedCutoverSources(authorization);
  const records = [];
  for (const expected of protectedCutoverRunAttestations) {
    records.push(await readProtectedRunObservation(api, workflow, expected, {
      ...options, currentMainSha: main.object.sha, executingControllerSha,
      environmentProtection, authorization, allowDisabledWorkflow: options.allowDisabledWorkflow === true
    }));
  }
  return {
    kind: 'protected-nonterminal-cutover',
    candidateSha, executingControllerSha, workflow, environmentProtection, sources,
    runIds: [...protectedCutoverRunIds], records,
    allowedNonterminalRunIds: new Set(protectedCutoverRunIds),
    allowDisabledWorkflow: options.allowDisabledWorkflow === true,
    authorization,
    authorizationReference: authorization.approvalReference,
    approval: authorization.approval
  };
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
  if (expected?.reviewers !== undefined) {
    assert.ok(Array.isArray(expected.reviewers) && expected.reviewers.length > 0,
      'STALE_SUPERSESSION_NATIVE_REVIEWER_SET');
    const normalized = normalizeReviewerSet(expected.reviewers);
    assert.equal(new Set(normalized.map(item => JSON.stringify(item))).size, normalized.length,
      'STALE_SUPERSESSION_NATIVE_REVIEWER_DUPLICATE');
  }
  return expected;
}

function normalizeReviewerSet(reviewers) {
  assert.ok(Array.isArray(reviewers), 'STALE_SUPERSESSION_NATIVE_REVIEWERS_INVALID');
  return reviewers.map(item => {
    const reviewer = item?.reviewer || item;
    const type = String(item?.type || reviewer?.type || '');
    assert.ok(type === 'User' || type === 'Team', 'STALE_SUPERSESSION_NATIVE_REVIEWER_TYPE');
    const id = reviewer?.id;
    assert.ok(Number.isSafeInteger(Number(id)) && Number(id) > 0,
      'STALE_SUPERSESSION_NATIVE_REVIEWER_ID');
    const login = reviewer?.login ?? null;
    const slug = reviewer?.slug ?? null;
    assert.ok(type === 'User' ? typeof login === 'string' && login.length > 0 :
      (typeof slug === 'string' && slug.length > 0) || typeof login === 'string' && login.length > 0,
    'STALE_SUPERSESSION_NATIVE_REVIEWER_NAME');
    return { type, id: Number(id), login, slug };
  }).sort((left, right) => JSON.stringify(left).localeCompare(JSON.stringify(right)));
}

export function verifyNativeEnvironmentProtection(actual, expected) {
  verifyNativeEnvironmentProtectionContract(expected);
  assert.equal(actual?.name, expected.environment, 'STALE_SUPERSESSION_NATIVE_READBACK_ENVIRONMENT');
  assert.equal(actual?.can_admins_bypass, expected.canAdminsBypass, 'STALE_SUPERSESSION_NATIVE_READBACK_BYPASS');
  const rule = (actual?.protection_rules || []).find(item => item?.type === 'required_reviewers');
  assert.ok(rule, 'STALE_SUPERSESSION_NATIVE_READBACK_RULE');
  assert.equal(rule.id, expected.protectionRuleId, 'STALE_SUPERSESSION_NATIVE_READBACK_RULE_ID');
  assert.equal(rule.prevent_self_review, expected.preventSelfReview, 'STALE_SUPERSESSION_NATIVE_READBACK_SELF_REVIEW');
  if (expected.reviewers !== undefined) {
    assert.deepEqual(normalizeReviewerSet(rule.reviewers), normalizeReviewerSet(expected.reviewers),
      'STALE_SUPERSESSION_NATIVE_READBACK_REVIEWER_SET');
  } else {
    const reviewer = (rule.reviewers || []).find(item => item?.type === 'User')?.reviewer;
    assert.equal(reviewer?.login, expected.requiredReviewerLogin, 'STALE_SUPERSESSION_NATIVE_READBACK_REVIEWER');
    assert.equal(reviewer?.id, expected.requiredReviewerId, 'STALE_SUPERSESSION_NATIVE_READBACK_REVIEWER_ID');
  }
  const reviewers = normalizeReviewerSet(rule.reviewers || []);
  return { environment: actual.name, protectionRuleId: rule.id,
    reviewer: reviewers.find(item => item.type === 'User')?.login,
    reviewers,
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
export function buildDispatchRequest(candidateSha, controllerSha) {
  assert.match(candidateSha, dispatchSha, 'The submitted candidate SHA must be complete.');
  assert.match(controllerSha, dispatchSha, 'The submitted controller SHA must be complete.');
  return {
    path: `actions/workflows/${workflowId}/dispatches`,
    method: 'POST',
    body: {
      ref: 'main',
      return_run_details: true,
      inputs: {
        release_sha: candidateSha,
        release_branch: candidateBranch,
        recover_private_runtime: false,
        admission_controller_sha: controllerSha
      }
    }
  };
}
export function verifyDispatchInputs(inputs, candidateSha, controllerSha) {
  assert.match(candidateSha, dispatchSha);
  assert.match(controllerSha, dispatchSha);
  assert.deepEqual(inputs, {
    release_sha: candidateSha,
    release_branch: candidateBranch,
    recover_private_runtime: false,
    admission_controller_sha: controllerSha
  }, 'The submitted dispatch inputs must remain bound to the admitted candidate.');
}
export function verifyDispatchRequest(dispatch, candidateSha, controllerSha) {
  assert.equal(dispatch.path, `actions/workflows/${workflowId}/dispatches`);
  assert.equal(dispatch.method, 'POST');
  assert.equal(dispatch.body.return_run_details, true, 'The documented receipt option must be a JSON body parameter.');
  verifyDispatchInputs(dispatch.body.inputs, candidateSha, controllerSha);
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
export async function dispatchOnce(api, candidateSha, controlSha, createdAfter, lifecycle = {}) {
  const dispatch = verifyDispatchRequest(buildDispatchRequest(candidateSha, controlSha), candidateSha, controlSha);
  lifecycle.beforeDispatch?.(dispatch);
  let receipt;
  try {
    receipt = await api(dispatch.path, dispatch.method, dispatch.body, 'dispatch');
  } catch (error) {
    lifecycle.failure?.(error, 'dispatch');
    throw error;
  }
  let runId;
  try {
    runId = verifyDispatchReceipt(receipt);
  } catch (error) {
    lifecycle.failure?.(error, 'receipt-validation');
    throw error;
  }
  lifecycle.receipt?.({ receipt, runId });
  let run;
  try {
    run = await api(`actions/runs/${runId}`, 'GET', undefined, 'dispatch-run-read');
  } catch (error) {
    lifecycle.failure?.(error, 'dispatch-run-read');
    throw error;
  }
  try {
    verifyDispatchedRun(run, controlSha, candidateSha, createdAfter, runId);
  } catch (error) {
    lifecycle.failure?.(error, 'dispatch-run-identity');
    throw error;
  }
  lifecycle.verified?.(run);
  return { runId, run, candidateSha, controlSha };
}
export async function request(path, method = 'GET', body, stage = `${method} ${path}`) {
  const { url, init } = buildRequest(path, method, body);
  let response;
  try {
    response = await fetch(url, init);
  } catch (error) {
    throw new GithubApiError({ stage, method, path, status: 'network', requestId: null, cause: error });
  }
  if (!response.ok) {
    throw new GithubApiError({ stage, method, path, status: response.status,
      requestId: response.headers?.get('x-github-request-id') });
  }
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

export async function requireNoUnresolvedRuns(api = request, options = {}) {
  const protectedAssessment = options.protectedAssessment || null;
  for (const status of ['queued', 'in_progress', 'waiting', 'pending', 'requested']) {
    for (let page = 1; page <= 10; page += 1) {
      const runs = await api(`actions/workflows/${workflowId}/runs?status=${status}&per_page=100&page=${page}`);
      assert.ok(Array.isArray(runs.workflow_runs), 'PSA_ACTIVE_RUN_INVENTORY_INVALID');
      for (const run of runs.workflow_runs) {
        if (run.status !== 'completed') {
          if (protectedAssessment?.allowedNonterminalRunIds?.has(run.id)) {
            assert.equal(run.status, 'queued', `PROTECTED_CUTOVER_INVENTORY_STATUS id=${run.id} status=${run.status}`);
            const expected = protectedCutoverRunAttestations.find(item => item.runId === run.id);
            assert.ok(expected, `PROTECTED_CUTOVER_INVENTORY_UNKNOWN_RUN id=${run.id}`);
            await readProtectedRunObservation(api, protectedAssessment.workflow, expected, {
              currentMainSha: protectedAssessment.executingControllerSha,
              executingControllerSha: protectedAssessment.executingControllerSha,
              environmentProtection: protectedAssessment.environmentProtection,
              authorization: protectedAssessment.authorization,
              allowDisabledWorkflow: protectedAssessment.allowDisabledWorkflow === true
            });
            continue;
          }
          const error = new Error(`PSA_UNRESOLVED_DEPLOYMENT_RUN id=${run.id} status=${run.status}`);
          error.runId = run.id;
          error.runStatus = run.status;
          throw error;
        }
      }
      if (runs.workflow_runs.length < 100) break;
      assert.ok(page < 10, 'Active-run pagination exceeded the bounded admission limit.');
    }
  }
}

// Read-only cutover gate. The normal path still requires terminal historical
// requests. An explicitly approved protected cutover may preserve exactly the
// three reviewed queued records when the same live native Test assessment is
// passed to the final nonterminal inventory check.
export async function verifyReleaseCutover(api = request, options = {}) {
  const workflow = verifyWorkflow(await api(`actions/workflows/${workflowId}`, 'GET', undefined, 'cutover-controller-read'));
  const authorization = options.authorization || readProtectedCutoverAuthorization();
  if (authorization.enabled === true) {
    const protectedAssessment = await assessProtectedCutover(api, { ...options, authorization });
    await requireNoUnresolvedRuns(api, { protectedAssessment });
    return {
      workflow: { id: workflow.id, path: workflow.path, state: workflow.state },
      requests: protectedAssessment.records.map(record => record.observation),
      protectedAssessment
    };
  }
  const requests = [];
  for (const runId of unresolvedRequestIds) {
    const run = await api(`actions/runs/${runId}`, 'GET', undefined, 'cutover-run-read');
    assert.equal(run.id, runId, 'PSA_CUTOVER_RUN_ID_MISMATCH');
    assert.equal(run.workflow_id, workflowId, 'PSA_CUTOVER_RUN_WORKFLOW_MISMATCH');
    assert.equal(run.path, workflowPath, 'PSA_CUTOVER_RUN_PATH_MISMATCH');
    assert.equal(run.event, 'workflow_dispatch', 'PSA_CUTOVER_RUN_EVENT_MISMATCH');
    assert.equal(run.status, 'completed', `PSA_CUTOVER_RUN_NOT_TERMINAL id=${runId} status=${run.status}`);
    const jobs = await api(`actions/runs/${runId}/jobs?per_page=100`, 'GET', undefined, 'cutover-jobs-read');
    assert.ok(Array.isArray(jobs.jobs), 'PSA_CUTOVER_JOBS_INVALID');
    assert.ok(jobs.jobs.every(job => job.status === 'completed'), `PSA_CUTOVER_EXECUTING_JOB id=${runId}`);
    const pending = await api(`actions/runs/${runId}/pending_deployments`, 'GET', undefined, 'cutover-pending-read');
    assert.ok(Array.isArray(pending) && pending.length === 0, `PSA_CUTOVER_PENDING_DEPLOYMENT id=${runId}`);
    requests.push({ id: run.id, status: run.status, conclusion: run.conclusion ?? null,
      headSha: run.head_sha, runAttempt: run.run_attempt });
  }
  assert.equal(workflow.state, 'active', 'PSA_CUTOVER_CONTROLLER_NOT_ACTIVE');
  await requireNoUnresolvedRuns(api);
  return { workflow: { id: workflow.id, path: workflow.path, state: workflow.state }, requests };
}

export async function inspectActiveController(api = request) {
  const workflow = verifyWorkflow(await api(`actions/workflows/${workflowId}`, 'GET', undefined, 'controller-read'));
  assert.equal(workflow.state, 'active', 'PSA_CONTROLLER_MUST_REMAIN_ACTIVE');
  await requireNoUnresolvedRuns(api);
  return { id: workflow.id, path: workflow.path, state: workflow.state, executableActiveRuns: 0 };
}

function singleUseClaimMarker({ candidateSha, controlSha, approvalReference, admissionRunId = null, admissionRunAttempt = null }) {
  assert.match(candidateSha || '', dispatchSha, 'PROTECTED_CUTOVER_CLAIM_CANDIDATE');
  assert.match(controlSha || '', dispatchSha, 'PROTECTED_CUTOVER_CLAIM_CONTROLLER');
  assert.match(approvalReference || '', /^[A-Z0-9._-]{8,100}$/, 'PROTECTED_CUTOVER_CLAIM_REFERENCE');
  if (admissionRunId === null && admissionRunAttempt === null) return `${singleUseClaimPrefix} candidate=${candidateSha} approval=${approvalReference} controller=${controlSha}`;
  assert.ok(Number.isSafeInteger(admissionRunId) && admissionRunId > 0, 'PROTECTED_CUTOVER_CLAIM_RUN_ID');
  assert.ok(Number.isSafeInteger(admissionRunAttempt) && admissionRunAttempt > 0, 'PROTECTED_CUTOVER_CLAIM_RUN_ATTEMPT');
  return `${singleUseClaimPrefix} candidate=${candidateSha} approval=${approvalReference} controller=${controlSha} run=${admissionRunId} attempt=${admissionRunAttempt}`;
}

function parseSingleUseClaim(body) {
  const match = new RegExp(`^${singleUseClaimPrefix} candidate=([a-f0-9]{40}) approval=([A-Z0-9._-]{8,100}) controller=([a-f0-9]{40})(?: run=([0-9]+) attempt=([0-9]+))? status=reserved observedAt=([^\\s]+)$`).exec(body || '');
  assert.ok(match, 'PROTECTED_CUTOVER_CLAIM_STRUCTURED_BINDING');
  const observedAt = Date.parse(match[6] || match[4]);
  assert.ok(Number.isFinite(observedAt), 'PROTECTED_CUTOVER_CLAIM_TIMESTAMP');
  const runId = match[4] ? Number(match[4]) : null;
  const runAttempt = match[5] ? Number(match[5]) : null;
  if (runId !== null) assert.ok(Number.isSafeInteger(runId) && runId > 0 && Number.isSafeInteger(runAttempt) && runAttempt > 0,
    'PROTECTED_CUTOVER_CLAIM_RUN_CONTEXT');
  return { candidateSha: match[1], approvalReference: match[2], controlSha: match[3], runId, runAttempt,
    observedAt: new Date(observedAt).toISOString() };
}

function verifyTrustedClaimAuthor(comment, { allowWorkflowBot = false } = {}) {
  const login = comment?.user?.login;
  const id = Number(comment?.user?.id);
  if (login === trustedClaimAuthor.login && id === trustedClaimAuthor.id) return { login, id };
  if (allowWorkflowBot && login === workflowClaimAuthor.login && id === workflowClaimAuthor.id) return { login, id };
  assert.equal(login, trustedClaimAuthor.login, 'PROTECTED_CUTOVER_CLAIM_AUTHOR');
  assert.equal(id, trustedClaimAuthor.id, 'PROTECTED_CUTOVER_CLAIM_AUTHOR_ID');
  return { login, id };
}

async function verifyClaimExecution(api, { runId, runAttempt, controlSha }) {
  const run = await api(`actions/runs/${runId}`, 'GET', undefined, 'single-use-claim-run-read');
  assert.equal(Number(run?.id), runId, 'PROTECTED_CUTOVER_CLAIM_RUN_ID');
  assert.equal(run?.event, 'issue_comment', 'PROTECTED_CUTOVER_CLAIM_RUN_EVENT');
  assert.equal(run?.head_branch, 'main', 'PROTECTED_CUTOVER_CLAIM_RUN_BRANCH');
  assert.equal(run?.head_sha, controlSha, 'PROTECTED_CUTOVER_CLAIM_RUN_CONTROLLER');
  assert.equal(Number(run?.run_attempt), runAttempt, 'PROTECTED_CUTOVER_CLAIM_RUN_ATTEMPT');
  assert.equal(run?.actor?.login, trustedClaimAuthor.login, 'PROTECTED_CUTOVER_CLAIM_RUN_ACTOR');
  return { id: runId, attempt: runAttempt, event: run.event, headSha: run.head_sha, actor: run.actor.login };
}

export async function claimSingleUse(api = request, { candidateSha, controlSha, approvalReference,
  admissionRunId = null, admissionRunAttempt = null }) {
  const marker = singleUseClaimMarker({ candidateSha, controlSha, approvalReference, admissionRunId, admissionRunAttempt });
  for (let page = 1; page <= 10; page += 1) {
    const comments = await api(`issues/${candidatePullRequest}/comments?per_page=100&page=${page}`,
      'GET', undefined, 'single-use-claim-read');
    assert.ok(Array.isArray(comments), 'PROTECTED_CUTOVER_CLAIM_COMMENTS_INVALID');
    for (const comment of comments) {
      if (typeof comment?.body !== 'string' || !comment.body.startsWith(`${singleUseClaimPrefix} `)) continue;
      let parsed;
      try { parsed = parseSingleUseClaim(comment.body); }
      catch (error) { throw new Error(`PROTECTED_CUTOVER_CLAIM_MALFORMED: ${error.message}`); }
      const hasRunContext = parsed.runId !== null;
      let author;
      try { author = verifyTrustedClaimAuthor(comment, { allowWorkflowBot: hasRunContext }); }
      catch (error) { throw new Error(`PROTECTED_CUTOVER_CLAIM_UNTRUSTED: ${error.message}`); }
      if (parsed.candidateSha !== candidateSha || parsed.approvalReference !== approvalReference) continue;
      if (parsed.controlSha !== controlSha) throw new Error('PROTECTED_CUTOVER_CLAIM_CONTROLLER_CHANGED');
      if (!hasRunContext) {
        if (author.login === workflowClaimAuthor.login) throw new Error('PROTECTED_CUTOVER_CLAIM_LEGACY_UNBOUND');
        throw new Error('PROTECTED_CUTOVER_SINGLE_USE_ALREADY_CLAIMED');
      }
      if (author.login !== workflowClaimAuthor.login) throw new Error('PROTECTED_CUTOVER_SINGLE_USE_ALREADY_CLAIMED');
      const run = await verifyClaimExecution(api, { runId: parsed.runId, runAttempt: parsed.runAttempt, controlSha });
      return { marker, commentId: Number(comment.id), observedAt: parsed.observedAt, candidateSha,
        controlSha, approvalReference, author,
        execution: run, key: `${candidateSha}:${approvalReference}`, reused: true };
    }
    if (comments.length < 100) break;
    assert.ok(page < 10, 'Protected cutover claim pagination exceeded the bounded limit.');
  }
  const observedAt = nowIso();
  let response;
  try {
    response = await api(`issues/${candidatePullRequest}/comments`, 'POST', {
      body: `${marker} status=reserved observedAt=${observedAt}`
    }, 'single-use-claim-write');
  } catch (error) {
    throw new Error(`PROTECTED_CUTOVER_SINGLE_USE_CLAIM_UNCERTAIN: ${error.message}`);
  }
  const commentId = Number(response?.id);
  assert.ok(Number.isSafeInteger(commentId) && commentId > 0,
    'PROTECTED_CUTOVER_SINGLE_USE_CLAIM_RECEIPT');
  const author = verifyTrustedClaimAuthor(response, { allowWorkflowBot: admissionRunId !== null });
  const returned = parseSingleUseClaim(response.body);
  assert.deepEqual(returned, { candidateSha, approvalReference, controlSha,
    runId: admissionRunId, runAttempt: admissionRunAttempt, observedAt });
  const execution = author.login === workflowClaimAuthor.login && admissionRunId !== null
    ? await verifyClaimExecution(api, { runId: admissionRunId, runAttempt: admissionRunAttempt, controlSha }) : null;
  return { marker, commentId, observedAt, candidateSha, controlSha, approvalReference,
    author, execution, key: `${candidateSha}:${approvalReference}` };
}

export async function activateProtectedControllerOnce(api = request, options = {}) {
  const authorization = options.authorization || readProtectedCutoverAuthorization();
  assert.equal(authorization.enabled, true, 'PROTECTED_CUTOVER_ACTIVATION_NOT_ENABLED');
  assert.equal(authorization.workflow.allowControllerActivation, true,
    'PROTECTED_CUTOVER_ACTIVATION_NOT_AUTHORIZED');
  const preAssessment = await assessProtectedCutover(api, {
    ...options, authorization, allowDisabledWorkflow: true
  });
  await requireNoUnresolvedRuns(api, { protectedAssessment: preAssessment });
  assert.equal(preAssessment.workflow.state, 'disabled_manually', 'PROTECTED_CUTOVER_ACTIVATION_PRESTATE');
  let transitionAttempted = false;
  try {
    transitionAttempted = true;
    await api(`actions/workflows/${workflowId}/enable`, 'PUT', undefined, 'protected-cutover-controller-enable');
    const active = verifyWorkflow(await api(`actions/workflows/${workflowId}`, 'GET', undefined,
      'protected-cutover-controller-active-readback'));
    assert.equal(active.state, 'active', 'PROTECTED_CUTOVER_ACTIVATION_READBACK');
    const activeAssessment = await assessProtectedCutover(api, {
      ...options, authorization, allowDisabledWorkflow: false
    });
    await requireNoUnresolvedRuns(api, { protectedAssessment: activeAssessment });
    return { preAssessment, active, activeAssessment, transitionedAt: nowIso(),
      operatingState: 'active-after-approved-bootstrap' };
  } catch (error) {
    error.controllerTransitionAttempted = transitionAttempted;
    throw error;
  }
}

export async function closeProtectedControllerOnce(api = request) {
  const current = verifyWorkflow(await api(`actions/workflows/${workflowId}`, 'GET', undefined,
    'protected-cutover-controller-closure-read'));
  if (current.state === 'active') {
    await api(`actions/workflows/${workflowId}/disable`, 'PUT', undefined, 'protected-cutover-controller-disable');
  } else {
    assert.equal(current.state, 'disabled_manually', 'PROTECTED_CUTOVER_CLOSURE_STATE');
  }
  const closed = verifyWorkflow(await api(`actions/workflows/${workflowId}`, 'GET', undefined,
    'protected-cutover-controller-closure-readback'));
  assert.equal(closed.state, 'disabled_manually', 'PROTECTED_CUTOVER_CLOSURE_READBACK');
  return { state: closed.state, closedAt: nowIso() };
}

export async function revalidateProtectedCutoverForSubmission(api = request, assessment, options = {}) {
  assert.equal(assessment?.kind, 'protected-nonterminal-cutover', 'PROTECTED_CUTOVER_SUBMISSION_ASSESSMENT');
  const authorization = assessment.authorization;
  const executingControllerSha = assessment.executingControllerSha;
  // Refresh the complete reviewed request set and every nonterminal workflow
  // status after the reservation write. The final clock check occurs after
  // these remote reads, so a slow observation cannot consume an expired gate.
  const finalAssessment = await assessProtectedCutover(api, {
    candidateSha: assessment.candidateSha, executingControllerSha, authorization,
    authorizationNow: options.authorizationNow || new Date()
  });
  await requireNoUnresolvedRuns(api, { protectedAssessment: finalAssessment });
  const authorizationNow = options.authorizationNow || new Date();
  const authorizationState = verifyProtectedCutoverAuthorization(authorization, authorizationNow);
  assert.equal(authorizationState.approved, true, 'PROTECTED_CUTOVER_SUBMISSION_AUTHORIZATION');
  const workflow = finalAssessment.workflow;
  const protection = verifyNativeEnvironmentProtection(finalAssessment.environmentProtection, authorization.environment);
  return {
    observedAt: authorizationNow instanceof Date ? authorizationNow.toISOString() : new Date(authorizationNow).toISOString(),
    authorization: {
      reference: authorization.approvalReference,
      status: authorization.approval.status,
      approvedBy: authorization.approval.approvedBy,
      approvedAt: authorizationState.approvedAt,
      expiresAt: authorizationState.expiresAt
    },
    controllerSha: executingControllerSha,
    workflow: { id: workflow.id, path: workflow.path, state: workflow.state },
    environment: protection,
    assessment: finalAssessment,
    nonterminalRunIds: finalAssessment.runIds
  };
}

export async function runAdmission({ api = request, candidateSha, controlSha, admissionRunId,
  admissionRunAttempt, evidenceFile, authorization = readProtectedCutoverAuthorization(),
  authorizationNow = new Date(), submissionAuthorizationNow = null, createdAfter = nowIso(),
  controllerTransition = null }) {
  assert.match(candidateSha || '', dispatchSha, 'PROTECTED_CUTOVER_ADMISSION_CANDIDATE');
  assert.match(controlSha || '', dispatchSha, 'PROTECTED_CUTOVER_ADMISSION_CONTROLLER');
  const cutover = await verifyReleaseCutover(api, {
    candidateSha, executingControllerSha: controlSha, authorization, authorizationNow
  });
  const controlCheck = await api('git/ref/heads/main', 'GET', undefined, 'main-readback');
  assert.equal(controlCheck.object.sha, controlSha, 'Main changed during admission; re-review is required.');
  const claim = await claimSingleUse(api, {
    candidateSha, controlSha, approvalReference: authorization.approvalReference,
    admissionRunId, admissionRunAttempt
  });
  const preSubmissionValidation = cutover.protectedAssessment
    ? await revalidateProtectedCutoverForSubmission(api, cutover.protectedAssessment, {
      authorizationNow: submissionAuthorizationNow || new Date()
    }) : null;
  const finalAssessment = preSubmissionValidation?.assessment || cutover.protectedAssessment;
  const cutoverEvidence = finalAssessment ? {
    kind: finalAssessment.kind,
    authorizationReference: finalAssessment.authorizationReference,
    runIds: finalAssessment.runIds,
    statuses: finalAssessment.records.map(record => record.observation.status),
    nativeEnvironmentProtected: finalAssessment.records.every(record => record.observation.nativeEnvironmentProtected),
    finalNonterminalInventory: preSubmissionValidation?.nonterminalRunIds || finalAssessment.runIds
  } : null;
  const dispatched = await dispatchWithEvidence({ api, candidateSha, controlSha, createdAfter,
    admissionRunId, admissionRunAttempt, evidenceFile, cutoverAssessment: cutoverEvidence,
    singleUseClaim: claim, preSubmissionValidation, controllerTransition });
  return { ...dispatched, cutover: finalAssessment ? { ...cutover, protectedAssessment: finalAssessment } : cutover,
    claim, preSubmissionValidation };
}

export async function dispatchWithEvidence({ api = request, candidateSha, controlSha, createdAfter = nowIso(),
  admissionRunId, admissionRunAttempt, evidenceFile, cutoverAssessment = null,
  singleUseClaim = null, preSubmissionValidation = null, controllerTransition = null }) {
  const dispatch = verifyDispatchRequest(buildDispatchRequest(candidateSha, controlSha), candidateSha, controlSha);
  let evidence = createDispatchEvidence({ candidateSha, controlSha, dispatch, admissionRunId,
    admissionRunAttempt, startedAt: createdAfter, cutoverAssessment, singleUseClaim, preSubmissionValidation,
    controllerTransition });
  const saveEvidence = patch => {
    evidence = { ...evidence, ...patch };
    persistDispatchEvidence(evidence, evidenceFile);
  };
  saveEvidence({ stage: 'pre-dispatch', phase: 'dispatch-pending' });
  const lifecycle = {
    beforeDispatch: () => saveEvidence({ stage: 'dispatching', phase: 'dispatching', dispatchAttempted: true, dispatchWriteCount: 1 }),
    receipt: ({ receipt, runId }) => saveEvidence({
      stage: 'receipt-accepted', phase: 'receipt-accepted',
      run: { id: runId, apiUrl: receipt.run_url, webUrl: receipt.html_url, status: null, conclusion: null }
    }),
    verified: run => saveEvidence({
      stage: 'identity-verified', phase: runPhase(run),
      run: { ...evidence.run, status: run.status, conclusion: run.conclusion ?? null,
        headSha: run.head_sha, headBranch: run.head_branch, event: run.event }
    }),
    failure: (error, stage) => saveEvidence({
      stage,
      phase: evidence.run?.id ? 'receipt-accepted-follow-up-failed' : 'dispatch-uncertain',
      errors: [...evidence.errors, safeApiError(error, stage)]
    })
  };
  try {
    const dispatched = await dispatchOnce(api, candidateSha, controlSha, createdAfter, lifecycle);
    return { dispatched, evidence };
  } catch (error) {
    if (evidence.errors.length === 0) {
      saveEvidence({ stage: 'admission-failed', phase: 'blocked', errors: [safeApiError(error, 'admission')] });
    }
    // The outer lifecycle must distinguish a failure before the dispatch
    // boundary from an uncertain/accepted external write. Disabling a GitHub
    // workflow is not cancellation, so an uncertain write keeps the active
    // operating state and is reported for manual reconciliation.
    error.dispatchAttempted = evidence.dispatchAttempted === true;
    error.dispatchReceiptAccepted = Boolean(evidence.run?.id);
    error.admissionEvidence = evidence;
    throw error;
  }
}

async function readControllerState(api, stage) {
  return verifyWorkflow(await api(`actions/workflows/${workflowId}`, 'GET', undefined, stage));
}

function lifecycleFailure(primaryError, cleanupError, finalState, policy) {
  const message = [
    'PROTECTED_CUTOVER_LIFECYCLE_FAILED',
    `policy=${policy}`,
    `finalState=${finalState?.state || 'unknown'}`,
    `primary=${primaryError?.message || 'none'}`,
    `cleanup=${cleanupError?.message || 'none'}`
  ].join(' ');
  const error = cleanupError
    ? new AggregateError([primaryError, cleanupError], message)
    : primaryError;
  error.lifecycle = { policy, finalState: finalState ? { id: finalState.id, path: finalState.path, state: finalState.state } : null,
    primaryError: primaryError ? safeApiError(primaryError, 'admission') : null,
    cleanupError: cleanupError ? safeApiError(cleanupError, 'restoration') : null };
  error.primaryError = primaryError;
  error.cleanupError = cleanupError;
  return error;
}

// The approved protected bootstrap has an explicit operating-state policy:
// on a verified dispatch it remains active for the canonical Test controller.
// Restoration is only for failures before an external dispatch write. An
// uncertain write is never "cleaned up" by disabling the workflow because
// that cannot stop a run that may already exist.
export async function runProtectedAdmissionLifecycle({ api = request, admissionOptions,
  activationOptions, activationRequired = true }) {
  let transition = null;
  let admissionResult = null;
  let primaryError = null;
  let cleanupError = null;
  let finalState = null;
  let policy = 'active-after-approved-bootstrap';
  try {
    if (activationRequired) transition = await activateProtectedControllerOnce(api, activationOptions);
    admissionResult = await runAdmission({ ...admissionOptions, api,
      controllerTransition: transition ? {
        mode: 'disabled_manually-to-active-once',
        operatingState: transition.operatingState,
        preState: transition.preAssessment.workflow.state,
        activeState: transition.active.state,
        transitionedAt: transition.transitionedAt,
        closure: 'not-performed-on-success'
      } : admissionOptions.controllerTransition });
  } catch (error) {
    primaryError = error;
    const dispatchedWrite = error.dispatchAttempted === true;
    const transitionAttempted = Boolean(transition || error.controllerTransitionAttempted);
    if (transitionAttempted && !dispatchedWrite) {
      policy = 'restore-disabled-before-dispatch';
      try { await closeProtectedControllerOnce(api); }
      catch (cleanupFailure) { cleanupError = cleanupFailure; }
    } else if (transitionAttempted) {
      policy = 'retain-active-after-uncertain-dispatch';
    }
  }

  try {
    finalState = await readControllerState(api, 'protected-cutover-final-controller-read');
  } catch (stateError) {
    if (!cleanupError) cleanupError = stateError;
    policy = `${policy}-state-unavailable`;
  }

  if (!primaryError && !cleanupError) {
    assert.equal(finalState?.state, 'active', 'PROTECTED_CUTOVER_SUCCESS_CONTROLLER_NOT_ACTIVE');
  }
  const lifecycle = {
    policy,
    transition: transition ? { mode: 'disabled_manually-to-active-once', transitionedAt: transition.transitionedAt } : null,
    finalState: finalState ? { id: finalState.id, path: finalState.path, state: finalState.state } : null,
    restored: policy.startsWith('restore-disabled-before-dispatch') && finalState?.state === 'disabled_manually',
    authorizationConsumed: Boolean(transition)
  };
  if (admissionResult) {
    admissionResult.lifecycle = lifecycle;
    if (admissionResult.evidence) {
      admissionResult.evidence = { ...admissionResult.evidence, lifecycle };
      persistDispatchEvidence(admissionResult.evidence, admissionOptions.evidenceFile);
    }
  }
  if (primaryError || cleanupError) {
    throw lifecycleFailure(primaryError, cleanupError, finalState, policy);
  }
  return admissionResult;
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
  const admission = readAdmissionExecutionContext();
  const cutoverAuthorization = readProtectedCutoverAuthorization();
  const admissionResult = await runProtectedAdmissionLifecycle({
    api: request,
    activationRequired: cutoverAuthorization.enabled === true && cutoverAuthorization.workflow.allowControllerActivation === true,
    activationOptions: {
      candidateSha, executingControllerSha: controlSha, authorization: cutoverAuthorization
    },
    admissionOptions: { candidateSha, controlSha,
      admissionRunId: admission.admissionRunId, admissionRunAttempt: admission.admissionRunAttempt,
      evidenceFile: evidencePath(), authorization: cutoverAuthorization,
      controllerTransition: null }
  });
  let { dispatched, evidence } = admissionResult;
  const summary = `## FlowHive PSA candidate admission\n\nCandidate: \`${candidateSha}\`\n\nTrusted controller: \`${controlSha}\`\n\nDeployment run: ${dispatched.runId}\n\nController lifecycle: \`${admissionResult.lifecycle.policy}\`; final state: \`${admissionResult.lifecycle.finalState?.state || 'unknown'}\`. Run identity came from the dispatch response. Feature PR #${candidatePullRequest} remains unmerged. Live acceptance is not yet established.\n`;
  try {
    fs.appendFileSync(process.env.GITHUB_STEP_SUMMARY, summary);
  } catch (error) {
    evidence = recordReportingFailure(evidence, error, 'reporting-summary');
    persistDispatchEvidence(evidence, evidencePath());
    console.warn(`FLOWHIVE_PSA_REPORTING_FAILURE stage=reporting-summary type=${error.name || 'Error'}`);
  }
  try {
    await request(`issues/${candidatePullRequest}/comments`, 'POST', {
      body: `Exact FlowHive candidate admission completed. Candidate \`${candidateSha}\`; trusted main controller \`${controlSha}\`. Protected Test deployment: https://github.com/${repository}/actions/runs/${dispatched.runId}. Final controller state: \`${admissionResult.lifecycle.finalState?.state || 'unknown'}\`; lifecycle policy: \`${admissionResult.lifecycle.policy}\`. Run identity came from the dispatch response; no Production/private-runtime recovery is requested. This is a deployment dispatch, not a live AI success or a completed enterprise PSA release.`
    }, 'reporting-comment');
  } catch (error) {
    evidence = recordReportingFailure(evidence, error, 'reporting-comment');
    persistDispatchEvidence(evidence, evidencePath());
    console.warn(`FLOWHIVE_PSA_REPORTING_FAILURE stage=reporting-comment status=${error.status ?? 'unknown'} requestId=${error.requestId || 'unknown'}`);
  }
  console.log(`FLOWHIVE_PSA_CANDIDATE_DISPATCHED=${dispatched.runId}`);
}
if (process.argv[1]?.endsWith('/dispatch-flowhive-psa-test.mjs')) {
  const operation = process.argv.length === 3 && process.argv[2] === '--inspect-only'
    ? inspectReleaseCutover().then(result => console.log(JSON.stringify(result)))
    : main();
  operation.catch(error => { console.error(error.message); process.exitCode = 1; });
}
