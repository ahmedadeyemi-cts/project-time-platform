import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import { execFileSync } from 'node:child_process';
import { verifyApproval, verifyPullRequest, verifyRuns, verifySourceDrift, verifyTargetReleaseBranch, repository, candidateBranch, candidatePullRequest, protectedTestReleaseLane } from '../scripts/release-test/flowhive-psa-admission.mjs';
import { parseCommand, buildDispatchRequest, verifyDispatchInputs, verifyDispatchRequest, verifyDispatchReceipt, verifyDispatchedRun, buildRequest, githubApiVersion, dispatchOnce, dispatchWithEvidence, request, GithubApiError, createDispatchEvidence, persistDispatchEvidence, recordReportingFailure, readAdmissionExecutionContext, verifyReleaseCutover, inspectReleaseCutover, readInspectOnlyContext, runAdmission, runProtectedAdmissionLifecycle, claimSingleUse, activateProtectedControllerOnce, closeProtectedControllerOnce, revalidateProtectedCutoverForSubmission, inspectActiveController, requireNoUnresolvedRuns, inspectIdleController, sealIdleController, requireIdleRuns, staleRunSupersessionAttestation, staleRunSupersessionApproved, verifyStaleSupersessionAuthorization, verifyHistoricalFenceSources, verifyFencedStaleRun, verifyRequestRunBinding, verifyNativeEnvironmentProtection, readHistoricalFenceSources, readProtectedCutoverAuthorization, verifyProtectedCutoverAuthorization, assessProtectedCutover, verifyProtectedHistoricalWorkflowSource, verifyProtectedRunObservation, protectedCutoverRunAttestations, protectedCutoverRunIds, parseDispatchReceiptArchive } from '../scripts/release-test/dispatch-flowhive-psa-test.mjs';
import { files, repairFiles, repairBase, plannerTimeBudgetApprovalFiles, staleSupersessionFiles, staleSupersessionActivationFiles, staleSupersessionActivationBase, staleSupersessionActivationBranch, staleSupersessionRenewalBranch, verifyFiles, verifyController } from './flowhive-psa-release-control.mjs';
const approval = JSON.parse(fs.readFileSync(new URL('../.github/flowhive-psa-protected-test-candidate.json', import.meta.url), 'utf8'));
const clone = x => structuredClone(x);
const pr = { number: candidatePullRequest, state: 'closed', merged: true,
  merge_commit_sha: approval.mergeCommit,
  head: { ref: approval.sourceBranch, sha: approval.sha, repo: { full_name: repository } },
  base: { ref: 'main', repo: { full_name: repository } } };
const runs = approval.requiredWorkflows.map((path, i) => ({ id: i + 1, path, event: 'pull_request',
  head_sha: approval.sha, status: 'completed', conclusion: 'success', run_attempt: 1,
  head_repository: { full_name: repository } }));
const historicalFence = readHistoricalFenceSources();
const nativeEnvironmentProtection = {
  name: 'test', can_admins_bypass: false,
  protection_rules: [{ id: 65110773, type: 'required_reviewers', prevent_self_review: false,
    reviewers: [{ type: 'User', reviewer: { login: 'ahmedadeyemi-cts', id: 244059331 } }] }]
};
// Schema-only binding fixture. It is never used as historical execution evidence;
// the real stale run remains blocked because its server audit binding is absent.
const staleAuthorization = () => ({
  contract: staleRunSupersessionAttestation.contract,
  enabled: true,
  approvalReference: staleRunSupersessionAttestation.approvalReference,
  workflowId: staleRunSupersessionAttestation.workflowId,
  workflowPath: staleRunSupersessionAttestation.workflowPath,
  runId: staleRunSupersessionAttestation.runId,
  controllerSha: staleRunSupersessionAttestation.controllerSha,
  evidence: { serverConfirmedDispatchInputs: null, requestToRunBinding: {
    status: 'server-confirmed', source: 'github-audit-log', repository,
    workflowId: staleRunSupersessionAttestation.workflowId, workflowPath: staleRunSupersessionAttestation.workflowPath,
    runId: staleRunSupersessionAttestation.runId, controllerSha: staleRunSupersessionAttestation.controllerSha,
    event: 'workflow_dispatch', ref: 'main', requestId: 'github-request-schema-fixture',
    requestBodySha256: '0'.repeat(64), submittedAt: '2026-09-09T16:30:41Z',
    response: { workflowRunId: staleRunSupersessionAttestation.runId }
  } },
  historicalExecutionProtection: {
    admissionConditionalOnReleaseBranch: true, allDeploymentPathsProtected: false, jobUsesTestEnvironment: true,
    nativeEnvironmentBarrier: { environment: 'test', protectionRuleId: 65110773,
      requiredReviewerLogin: 'ahmedadeyemi-cts', requiredReviewerId: 244059331,
      preventSelfReview: false, canAdminsBypass: false }
  },
  approval: { status: 'approved', approvedBy: 'ahmedadeyemi-cts', approvedAt: '2026-09-09T22:00:00Z', expiresAt: '2026-09-09T22:15:00Z' }
});
const staleRun = () => ({
  workflow: { id: staleRunSupersessionAttestation.workflowId, path: staleRunSupersessionAttestation.workflowPath, state: 'disabled_manually' },
  run: { id: staleRunSupersessionAttestation.runId, workflow_id: staleRunSupersessionAttestation.workflowId,
    path: staleRunSupersessionAttestation.workflowPath, head_sha: staleRunSupersessionAttestation.controllerSha,
    head_branch: staleRunSupersessionAttestation.headBranch, event: staleRunSupersessionAttestation.event,
    run_attempt: staleRunSupersessionAttestation.runAttempt, status: staleRunSupersessionAttestation.status,
    conclusion: staleRunSupersessionAttestation.conclusion, created_at: staleRunSupersessionAttestation.createdAt,
    updated_at: staleRunSupersessionAttestation.updatedAt },
  attemptJobs: { total_count: 0, jobs: [] }, pendingDeployments: [],
  concurrencyGroups: { total_count: 0, concurrency_groups: [] }, artifacts: { total_count: 0, artifacts: [] },
  currentMainSha: '785eb54a4f280c9ff0e59951c31a30cad4c1a0da', executingControllerSha: '785eb54a4f280c9ff0e59951c31a30cad4c1a0da',
  historicalSources: historicalFence, environmentProtection: nativeEnvironmentProtection,
  authorization: staleAuthorization(), authorizationNow: new Date('2026-09-09T22:05:00Z')
});
const protectedCutoverAuthorization = () => {
  const authorization = JSON.parse(fs.readFileSync(new URL('../.github/flowhive-psa-protected-cutover.json', import.meta.url), 'utf8'));
  authorization.enabled = true;
  authorization.activationDecision = 'approved';
  authorization.workflow.allowControllerActivation = true;
  authorization.approval = {
    status: 'approved', approvedBy: 'ahmedadeyemi-cts',
    approvedAt: '2026-09-10T19:00:00Z', expiresAt: '2026-09-10T19:15:00Z'
  };
  return authorization;
};
function protectedCutoverApi({ fourthRun = false, approvalHistory = [], jobCount = 0,
  protection = nativeEnvironmentProtection, protectionAfterFirstRead = null,
  state = 'active', dispatchFailure = false, disableFailure = false, afterReservation = null } = {}) {
  const control = 'e'.repeat(40);
  const calls = [];
  const comments = [];
  let nextCommentId = 7000;
  let environmentReadCount = 0;
  let workflowState = state;
  let currentJobCount = jobCount;
  let currentApprovalHistory = approvalHistory;
  const runs = protectedCutoverRunAttestations.map(expected => ({
    id: expected.runId, workflow_id: 315562561, path: '.github/workflows/projectpulse-deploy-test.yml',
    event: expected.event, head_branch: expected.headBranch, head_sha: expected.controllerSha,
    run_attempt: expected.runAttempt, status: expected.status, conclusion: expected.conclusion,
    created_at: expected.createdAt, updated_at: expected.updatedAt
  }));
  if (fourthRun) runs.push({ id: 99999999999, status: 'queued' });
  const request = async (url, method = 'GET', body) => {
    calls.push({ url, method });
    if (url === 'actions/workflows/315562561') return { id: 315562561, path: '.github/workflows/projectpulse-deploy-test.yml', state: workflowState };
    if (url === 'actions/workflows/315562561/enable' && method === 'PUT') { workflowState = 'active'; return null; }
    if (url === 'actions/workflows/315562561/disable' && method === 'PUT') {
      if (disableFailure) throw new Error('restoration failed');
      workflowState = 'disabled_manually'; return null;
    }
    if (url === 'git/ref/heads/main') return { object: { sha: control } };
    if (url === 'environments/test') {
      environmentReadCount += 1;
      return environmentReadCount === 1 || !protectionAfterFirstRead ? protection : protectionAfterFirstRead;
    }
    if (url.startsWith(`issues/${candidatePullRequest}/comments?`)) return comments;
    if (url === `issues/${candidatePullRequest}/comments` && method === 'POST') {
      const comment = { id: nextCommentId++, body: body?.body || '', user: { login: 'ahmedadeyemi-cts', id: 244059331 } };
      comments.push(comment);
      afterReservation?.({ runs, setJobCount: value => { currentJobCount = value; },
        setApprovalHistory: value => { currentApprovalHistory = value; }, addRun: run => runs.push(run) });
      return comment;
    }
    if (url.includes('/dispatches') && dispatchFailure) throw new Error('dispatch response timeout');
    if (url.includes('/dispatches')) return {
      workflow_run_id: 9001,
      run_url: 'https://api.github.com/repos/ahmedadeyemi-cts/project-time-platform/actions/runs/9001',
      html_url: 'https://github.com/ahmedadeyemi-cts/project-time-platform/actions/runs/9001'
    };
    if (url === 'actions/runs/9001') return {
      id: 9001, workflow_id: 315562561, event: 'workflow_dispatch', head_branch: 'main', head_sha: control,
      created_at: '2026-09-10T20:00:00Z', status: 'queued', conclusion: null,
      display_title: 'Protected Test', run_attempt: 1
    };
    const runMatch = /^actions\/runs\/(\d+)$/.exec(url);
    if (runMatch) {
      const run = runs.find(item => item.id === Number(runMatch[1]));
      if (run) return run;
    }
    if (url.includes('/runs?status=')) return { workflow_runs: runs };
    if (url.includes('/attempts/') && url.includes('/jobs?')) return { total_count: currentJobCount, jobs: currentJobCount ? [{ id: 1 }] : [] };
    if (url.includes('/pending_deployments')) return [];
    if (url.includes('/approvals')) return currentApprovalHistory;
    if (url.includes('/concurrency_groups')) return { total_count: 0, concurrency_groups: [] };
    if (url.includes('/artifacts?')) return { total_count: 0, artifacts: [] };
    throw new Error(`UNEXPECTED_PROTECTED_CUTOVER_REQUEST ${method} ${url}`);
  };
  return { request, calls, control, runs, comments };
}
test('approved current draft candidate is admissible without merging', () => {
  verifyApproval(approval, approval.sha); verifyPullRequest(approval, pr); verifyRuns(approval, runs);
});
test('protected cutover refresh uses a new approval reference and preserves historical evidence identity', () => {
  const authorization = JSON.parse(fs.readFileSync(new URL('../.github/flowhive-psa-protected-cutover.json', import.meta.url), 'utf8'));
  assert.equal(authorization.approvalReference, 'FLOWHIVE-PSA-PROTECTED-CUTOVER-20260912-PLANNER-PROVIDER-BUDGET');
  assert.equal(authorization.supersedesApprovalReference, 'FLOWHIVE-PSA-PROTECTED-CUTOVER-20260912-PLANNER-TIME-BUDGET');
  assert.notEqual(authorization.approvalReference, authorization.supersedesApprovalReference);
  assert.equal(authorization.reservationRecovery.approvalReference, 'FLOWHIVE-PSA-PROTECTED-CUTOVER-20260911');
  assert.equal(authorization.reservationRecovery.status, 'terminal-skipped-no-mutation');
});
test('admission accepts only the candidate source branch or the protected deployment lane', () => {
  verifyTargetReleaseBranch(candidateBranch);
  verifyTargetReleaseBranch(protectedTestReleaseLane);
  verifyTargetReleaseBranch(undefined);
  verifyTargetReleaseBranch('');
  for (const branch of ['main', 'fix/unreviewed', 'release/other', 'refs/heads/main']) {
    assert.throws(() => verifyTargetReleaseBranch(branch), /PSA release branch/);
  }
});
for (const [field, value] of [['environment','production'], ['publicOrigin','https://elsewhere.invalid'], ['sha','1'.repeat(40)], ['allowPrivateRuntimeMutation',true], ['allowCanonicalTaskAdoption',true], ['allowCustomerPublication',true], ['projectId','1'.repeat(36)]]) {
  test('reject unapproved '+field, () => { const a=clone(approval); a[field]=value; assert.throws(()=>verifyApproval(a,approval.sha)); });
}
test('reject migration substitution and required-check dilution', () => {
  const a=clone(approval); a.migrations.reverse();assert.throws(()=>verifyApproval(a,a.sha));
  const b=clone(approval);b.requiredWorkflows=b.requiredWorkflows.slice(0,1);assert.throws(()=>verifyApproval(b,b.sha));
});
test('reject wrong repo, wrong head, changed branch and merge identity', () => {
  for(const mutate of [p=>p.head.repo.full_name='someone/fork', p=>p.head.sha='0'.repeat(40), p=>p.head.ref='main', p=>p.state='open', p=>p.merged=false, p=>p.merge_commit_sha='0'.repeat(40)]) {
    const p=clone(pr);mutate(p);assert.throws(()=>verifyPullRequest(approval,p));
  }
});
test('CI must be complete, successful and for exact source', () => {
  assert.throws(()=>verifyRuns(approval,runs.slice(1)));
  for(const mutate of [r=>r.head_sha='0'.repeat(40),r=>r.conclusion='failure',r=>r.status='in_progress',r=>r.event='push']) {
    const r=clone(runs);mutate(r[0]);assert.throws(()=>verifyRuns(approval,r));
  }
});
test('later failed rerun or unknown failed workflow cannot hide behind older green result', () => {
  assert.throws(()=>verifyRuns(approval,[...runs,{...runs[0],run_attempt:2,conclusion:'failure'}]));
  assert.throws(()=>verifyRuns(approval,[...runs,{...runs[0],id:999,conclusion:'cancelled'}]));
  assert.throws(()=>verifyRuns(approval,[...runs,{...runs[0],path:'.github/workflows/new-check.yml',id:1000,conclusion:'failure'}]));
});
test('source drift allows only reviewed control paths; application drift is rejected', () => {
  verifySourceDrift(files,files);assert.throws(()=>verifySourceDrift([...files,'src/backend/ProjectTime.Api/Program.cs'],files));
});
test('superseded dispatch receipt remains audit evidence and is not current-candidate recovery', () => {
  const authorization = JSON.parse(fs.readFileSync(new URL('../.github/flowhive-psa-protected-cutover.json', import.meta.url), 'utf8'));
  assert.notEqual(authorization.reservationRecovery.candidateSha, approval.sha);
  assert.equal(authorization.reservationRecovery.candidateSha, '86c9be03b87e588eeec47492e35131177716263b');
  assert.equal(authorization.reservationRecovery.approvalReference, 'FLOWHIVE-PSA-PROTECTED-CUTOVER-20260911');
  assert.doesNotThrow(() => verifyProtectedCutoverAuthorization(authorization, new Date('2026-09-12T13:10:00Z')));
});
test('successor candidate binds to trusted main and rejects unincorporated application drift', () => {
  const reviewedMain = approval.sourceBase;
  const candidate = approval.sha;
  assert.match(reviewedMain, /^[0-9a-f]{40}$/);
  assert.match(candidate, /^[0-9a-f]{40}$/);
  assert.equal(approval.pullRequest, 937);
  assert.equal(approval.branch, candidateBranch);
  assert.equal(approval.sourceBranch, 'fix/flowhive-planner-provider-budget-20260912');
  assert.equal(approval.mergeCommit, '3707d013675b7edd130c74c666b11315cdb32c9e');
  assert.equal(approval.sourceBase, reviewedMain);
  assert.equal(approval.sha, candidate);
  assert.notEqual(approval.sourceBase, approval.sha);
  verifySourceDrift(plannerTimeBudgetApprovalFiles, files);
  for (const unrelated of [
    'src/backend/ProjectTime.Api/Program.cs',
    'src/frontend/project-time-web/src/App.jsx',
    'scripts/release-test/unincorporated-application-change.sh'
  ]) assert.throws(() => verifySourceDrift([...plannerTimeBudgetApprovalFiles, unrelated], files));
});
test('successor approval enumerates only the workflows that ran for the exact PR937 head', () => {
  assert.deepEqual(approval.requiredWorkflows, [
    '.github/workflows/celar-ai-enterprise-api-diagnostics.yml',
    '.github/workflows/celar-ai-production-hardening-ci.yml',
    '.github/workflows/flowhive-detailed-planner-ci.yml',
    '.github/workflows/flowhive-enterprise-psa-ci.yml',
    '.github/workflows/module025-governed-protected-test-release-ci.yml',
    '.github/workflows/project-planning-collaboration-ci.yml',
    '.github/workflows/projectpulse-ci.yml',
    '.github/workflows/projectpulse-release-test-control-ci-reregistered.yml',
    '.github/workflows/projectpulse-release-test-control-ci.yml',
    '.github/workflows/security-posture-ci.yml',
    '.github/workflows/shared-project-document-planning-ci.yml',
    '.github/workflows/systemwide-enterprise-reliability-ci.yml'
  ]);
  assert.equal(verifyRuns(approval, runs).length, approval.requiredWorkflows.length);
  assert.throws(() => verifyRuns(approval, runs.slice(1)), /Required exact-SHA CI is missing/);
});
test('release scope cannot absorb application files, unknown workflows or production changes', () => {
  verifyFiles(files,files);
  for(const extra of ['src/frontend/project-time-web/src/App.jsx','.github/workflows/random-deploy.yml','deployment/production/main.bicep'])
    assert.throws(()=>verifyFiles([...files,extra],[...files,extra].sort()));
  assert.throws(()=>verifyFiles(files.slice(1),files));
});
test('comment cannot select an arbitrary workflow, ref, environment or shell command', () => {
  assert.equal(parseCommand('DEPLOY FLOWHIVE PSA PROTECTED TEST SHA '+approval.sha),approval.sha);
  for(const suffix of ['; echo stolen','\nOTHER',' prod',' ','\n']) assert.throws(()=>parseCommand('DEPLOY FLOWHIVE PSA PROTECTED TEST SHA '+approval.sha+suffix));
});
test('dispatch response binds submitted candidate and returned run identity', () => {
  const control='a'.repeat(40),created='2026-09-06T00:00:00Z';
  const request=buildDispatchRequest(approval.sha,'a'.repeat(40));
  assert.equal(request.path,'actions/workflows/315562561/dispatches');
  verifyDispatchRequest(request,approval.sha,'a'.repeat(40));
  const serialized=buildRequest(request.path,request.method,request.body,'test-token');
  assert.equal(serialized.url,'https://api.github.com/repos/ahmedadeyemi-cts/project-time-platform/actions/workflows/315562561/dispatches');
  assert.equal(serialized.init.headers['X-GitHub-Api-Version'],githubApiVersion);
  assert.equal(serialized.init.headers['X-GitHub-Api-Version'],'2022-11-28');
  assert.deepEqual(JSON.parse(serialized.init.body),{ref:'main',return_run_details:true,inputs:{release_sha:approval.sha,release_branch:protectedTestReleaseLane,recover_private_runtime:false,admission_controller_sha:'a'.repeat(40)}});
  assert.notEqual(protectedTestReleaseLane, candidateBranch,
    'The stable deployment lane must not be mistaken for the approved candidate source branch.');
  assert.equal(new URL(serialized.url).search,'');
  assert.throws(()=>verifyDispatchRequest({...request,body:{...request.body,return_run_details:false}},approval.sha,'a'.repeat(40)));
  const receipt={workflow_run_id:7,run_url:'https://api.github.com/repos/ahmedadeyemi-cts/project-time-platform/actions/runs/7',html_url:'https://github.com/ahmedadeyemi-cts/project-time-platform/actions/runs/7'};
  assert.equal(verifyDispatchReceipt(receipt),7);
  const r={id:7,workflow_id:315562561,event:'workflow_dispatch',head_branch:'main',head_sha:control,created_at:created,display_title:'Deploy System-wide Enterprise Reliability and Utilization to Protected Test'};
  assert.equal(verifyDispatchedRun(r,control,approval.sha,created,7),7);
  assert.throws(()=>verifyDispatchedRun({...r,head_sha:approval.sha},control,approval.sha,created,7));
  assert.throws(()=>verifyDispatchedRun({...r,id:8},control,approval.sha,created,7));
  assert.throws(()=>verifyDispatchReceipt({...receipt,workflow_run_id:8}));
  assert.throws(()=>verifyDispatchReceipt({...receipt,run_url:receipt.run_url.replace('/7','/8')}));
});
test('returned run ID works with delayed or generic server titles',async()=>{
  const control='b'.repeat(40),created='2026-09-06T00:00:00Z',calls=[];
  const run={id:9,workflow_id:315562561,event:'workflow_dispatch',head_branch:'main',head_sha:control,created_at:created,display_title:'Deploy System-wide Enterprise Reliability and Utilization to Protected Test'};
  const receipt={workflow_run_id:9,run_url:'https://api.github.com/repos/ahmedadeyemi-cts/project-time-platform/actions/runs/9',html_url:'https://github.com/ahmedadeyemi-cts/project-time-platform/actions/runs/9'};
  const result=await dispatchOnce(async(path,method,body)=>{calls.push({path,method,body});return path.includes('/dispatches')?receipt:run;},approval.sha,control,created);
  assert.equal(result.runId,9);assert.equal(calls.filter(x=>x.method==='POST').length,1);
  assert.equal(calls[0].body.inputs.release_sha,approval.sha);
});
test('receipt is persisted before a follow-up read and no second dispatch is possible', async () => {
  const control='d'.repeat(40),created='2026-09-06T00:00:00Z',events=[];
  const receipt={workflow_run_id:11,run_url:'https://api.github.com/repos/ahmedadeyemi-cts/project-time-platform/actions/runs/11',html_url:'https://github.com/ahmedadeyemi-cts/project-time-platform/actions/runs/11'};
  await assert.rejects(dispatchOnce(async(path,method,body,stage)=>{
    events.push({path,method,stage});
    if(path.includes('/dispatches')) return receipt;
    throw new GithubApiError({stage:'dispatch-run-read',method:'GET',path:'actions/runs/11',status:403,requestId:'E-READ'});
  },approval.sha,control,created,{
    beforeDispatch:dispatch=>events.push({stage:'before-dispatch',fingerprint:dispatch.body.inputs.release_sha}),
    receipt:({runId})=>events.push({stage:'receipt-accepted',runId}),
    failure:(error,stage)=>events.push({stage,errorStage:error.stage,failureStage:stage})
  }),/GITHUB_API_REQUEST_FAILED/);
  assert.deepEqual(events.map(event=>event.stage),['before-dispatch','dispatch','receipt-accepted','dispatch-run-read','dispatch-run-read']);
  assert.equal(events.filter(event=>event.method==='POST').length,1);
});
test('dispatch evidence is sanitized and binds the reviewed request fingerprint', () => {
  const directory=fs.mkdtempSync('/tmp/flowhive-dispatch-evidence-');
  try {
    const dispatch=buildDispatchRequest(approval.sha,'e'.repeat(40));
    const record=createDispatchEvidence({candidateSha:approval.sha,controlSha:'e'.repeat(40),dispatch,startedAt:'2026-09-10T00:00:00.000Z'});
    const file=persistDispatchEvidence(record,`${directory}/attempt.json`);
    const saved=JSON.parse(fs.readFileSync(file,'utf8'));
    assert.equal(saved.dispatchWriteCount,0);
    assert.match(saved.dispatch.requestFingerprint,/^[0-9a-f]{64}$/);
    assert.equal(saved.dispatch.body,undefined);
    assert.equal(JSON.stringify(saved).includes('test-token'),false);
  } finally { fs.rmSync(directory,{recursive:true,force:true}); }
});
test('API failures retain stage, method, path, status and request ID without retrying', async () => {
  const previousFetch=globalThis.fetch; let calls=0;
  globalThis.fetch=async()=>{ calls+=1; return {ok:false,status:403,headers:new Headers({'x-github-request-id':'E-COMMENT'})}; };
  try {
    await assert.rejects(request('issues/887/comments','POST',{body:'sanitized'},'reporting-comment'),error=>
      error instanceof GithubApiError && error.stage==='reporting-comment' && error.method==='POST' &&
      error.path==='issues/887/comments' && error.status===403 && error.requestId==='E-COMMENT');
  } finally { globalThis.fetch=previousFetch; }
  assert.equal(calls,1);
});
test('reporting failure preserves the accepted dispatch receipt and is not a deployment failure', () => {
  const receipt = { id: 34495606530, webUrl: 'https://github.com/example/actions/runs/34495606530' };
  const evidence = { stage: 'identity-verified', phase: 'accepted-awaiting-scheduling', run: receipt, reportingErrors: [] };
  const error = new GithubApiError({ stage: 'reporting-comment', method: 'POST', path: 'issues/887/comments', status: 403, requestId: 'E-COMMENT' });
  const updated = recordReportingFailure(evidence, error, 'reporting-comment');
  assert.deepEqual(updated.run, receipt);
  assert.equal(updated.phase, 'accepted-awaiting-scheduling');
  assert.equal(updated.stage, 'identity-verified');
  assert.equal(updated.reporting.status, 'failed');
  assert.equal(updated.reporting.stage, 'reporting-comment');
  assert.deepEqual(updated.reportingErrors, [{ stage: 'reporting-comment', method: 'POST', path: 'issues/887/comments', status: 403, requestId: 'E-COMMENT', type: 'GithubApiError', at: updated.reportingErrors[0].at }]);
});
test('the admission caller records its actual run ID and rerun attempt in persisted evidence', async () => {
  const directory = fs.mkdtempSync('/tmp/flowhive-admission-attempt-');
  const control = 'f'.repeat(40), created = '2026-09-10T00:00:00Z';
  const receipt = { workflow_run_id: 101, run_url: 'https://api.github.com/repos/ahmedadeyemi-cts/project-time-platform/actions/runs/101', html_url: 'https://github.com/ahmedadeyemi-cts/project-time-platform/actions/runs/101' };
  const run = { id: 101, workflow_id: 315562561, event: 'workflow_dispatch', head_branch: 'main', head_sha: control, created_at: created, status: 'queued', conclusion: null, display_title: 'Protected Test' };
  try {
    const result = await dispatchWithEvidence({
      api: async path => path.endsWith('/dispatches') ? receipt : run,
      candidateSha: approval.sha, controlSha: control, createdAfter: created,
      admissionRunId: 9001, admissionRunAttempt: 2, evidenceFile: `${directory}/attempt.json`
    });
    const saved = JSON.parse(fs.readFileSync(`${directory}/attempt.json`, 'utf8'));
    assert.equal(result.dispatched.runId, 101);
    assert.equal(saved.admissionRunId, 9001);
    assert.equal(saved.admissionRunAttempt, 2);
    assert.equal(saved.attempt, 2);
    assert.equal(saved.run.id, 101);
  } finally { fs.rmSync(directory, { recursive: true, force: true }); }
  assert.deepEqual(readAdmissionExecutionContext({ GITHUB_RUN_ID: '9001', GITHUB_RUN_ATTEMPT: '2' }), { admissionRunId: 9001, admissionRunAttempt: 2 });
  assert.throws(() => readAdmissionExecutionContext({ GITHUB_RUN_ID: '9001', GITHUB_RUN_ATTEMPT: '0' }), /FLOWHIVE_PSA_ADMISSION_RUN_ATTEMPT_REQUIRED/);
});
test('malformed or uncertain dispatch responses fail without a duplicate POST',async()=>{
  const control='c'.repeat(40),created='2026-09-06T00:00:00Z';
  let postCount=0;
  await assert.rejects(dispatchOnce(async()=>{postCount+=1;throw new Error('dispatch response timeout');},approval.sha,control,created),/timeout/);
  assert.equal(postCount,1);
  await assert.rejects(dispatchOnce(async(path)=>path.includes('/dispatches')?{}:null,approval.sha,control,created),/workflow run ID/);
});
test('maintained admission rejects every unresolved request before dispatch', async () => {
  const calls=[];
  const api=async url=>{
    calls.push(url);
    if(url.includes('/runs?status=queued')) return {workflow_runs:[{id:34495606530,status:'queued'}]};
    throw new Error(`UNEXPECTED_REQUEST ${url}`);
  };
  await assert.rejects(requireNoUnresolvedRuns(api),/PSA_UNRESOLVED_DEPLOYMENT_RUN id=34495606530 status=queued/);
  assert.equal(calls.length,1);
});
test('maintained admission requires an active controller and no unresolved runs', async () => {
  const active=[];
  const api=async(url,method)=>{
    active.push({url,method});
    if(url==='actions/workflows/315562561') return {id:315562561,path:'.github/workflows/projectpulse-deploy-test.yml',state:'active'};
    if(url.includes('/runs?')) return {workflow_runs:[]};
    throw new Error(`UNEXPECTED_REQUEST ${url}`);
  };
  assert.deepEqual(await inspectActiveController(api),{id:315562561,path:'.github/workflows/projectpulse-deploy-test.yml',state:'active',executableActiveRuns:0});
  await assert.rejects(inspectActiveController(async url=>({id:315562561,path:'.github/workflows/projectpulse-deploy-test.yml',state:'disabled_manually'})),/PSA_CONTROLLER_MUST_REMAIN_ACTIVE/);
});
test('protected cutover assesses all three queued requests and permits exactly one dispatch', async () => {
  const fixture = protectedCutoverApi();
  const authorization = protectedCutoverAuthorization();
  assert.equal(verifyProtectedCutoverAuthorization(authorization, new Date('2026-09-10T19:05:00Z')).approved, true);
  const directory = fs.mkdtempSync('/tmp/flowhive-protected-cutover-');
  try {
    const result = await runAdmission({
      api: fixture.request, candidateSha: approval.sha, controlSha: fixture.control,
      createdAfter: '2026-09-10T19:05:00Z', admissionRunId: 9002, admissionRunAttempt: 1,
      evidenceFile: `${directory}/attempt.json`, authorization,
      authorizationNow: new Date('2026-09-10T19:05:00Z'),
      submissionAuthorizationNow: new Date('2026-09-10T19:06:00Z')
    });
    assert.equal(result.dispatched.runId, 9001);
    assert.deepEqual(result.cutover.protectedAssessment.runIds, protectedCutoverRunIds);
    assert.equal(result.preSubmissionValidation.authorization.expiresAt, '2026-09-10T19:15:00.000Z');
    const saved = JSON.parse(fs.readFileSync(`${directory}/attempt.json`, 'utf8'));
    assert.deepEqual(saved.cutoverAssessment.runIds, protectedCutoverRunIds);
    assert.equal(saved.singleUseClaim.commentId, 7000);
    assert.equal(saved.preSubmissionValidation.controllerSha, fixture.control);
  } finally { fs.rmSync(directory, { recursive: true, force: true }); }
  assert.equal(fixture.calls.filter(call => call.method === 'POST' && call.url.includes('/dispatches')).length, 1);
});
test('successor active-state cutover dispatches once without toggling the already-active controller', async () => {
  const fixture = protectedCutoverApi();
  const authorization = protectedCutoverAuthorization();
  authorization.workflow.allowControllerActivation = false;
  const directory = fs.mkdtempSync('/tmp/flowhive-successor-active-cutover-');
  try {
    const result = await runProtectedAdmissionLifecycle({
      api: fixture.request,
      activationRequired: false,
      admissionOptions: {
        candidateSha: approval.sha, controlSha: fixture.control,
        admissionRunId: 9012, admissionRunAttempt: 1,
        evidenceFile: `${directory}/attempt.json`, authorization,
        authorizationNow: new Date('2026-09-10T19:05:00Z'),
        submissionAuthorizationNow: new Date('2026-09-10T19:06:00Z'),
        createdAfter: '2026-09-10T19:05:00Z'
      }
    });
    assert.equal(result.dispatched.runId, 9001);
    assert.equal(result.lifecycle.policy, 'active-after-approved-bootstrap');
    assert.deepEqual(fixture.calls.filter(call => call.method === 'PUT'), []);
  } finally { fs.rmSync(directory, { recursive: true, force: true }); }
  assert.equal(fixture.calls.filter(call => call.method === 'POST' && call.url.includes('/dispatches')).length, 1);
});
test('reviewed activation transitions disabled to active once and closes without retrying', async () => {
  const fixture = protectedCutoverApi({ state: 'disabled_manually' });
  const authorization = protectedCutoverAuthorization();
  const transition = await activateProtectedControllerOnce(fixture.request, {
    candidateSha: approval.sha, executingControllerSha: fixture.control, authorization,
    authorizationNow: new Date('2026-09-10T19:05:00Z')
  });
  assert.equal(transition.preAssessment.workflow.state, 'disabled_manually');
  assert.equal(transition.active.state, 'active');
  const closure = await closeProtectedControllerOnce(fixture.request);
  assert.equal(closure.state, 'disabled_manually');
  assert.deepEqual(fixture.calls.filter(call => call.method === 'PUT').map(call => call.url), [
    'actions/workflows/315562561/enable', 'actions/workflows/315562561/disable'
  ]);
});
test('single-use claim blocks repeated and uncertain/restarted admissions without a second dispatch', async () => {
  const directory = fs.mkdtempSync('/tmp/flowhive-protected-cutover-single-use-');
  try {
    const fixture = protectedCutoverApi();
    const authorization = protectedCutoverAuthorization();
    const first = await runAdmission({ api: fixture.request, candidateSha: approval.sha, controlSha: fixture.control,
      admissionRunId: 9003, admissionRunAttempt: 1, evidenceFile: `${directory}/first.json`, authorization,
      authorizationNow: new Date('2026-09-10T19:05:00Z'), submissionAuthorizationNow: new Date('2026-09-10T19:06:00Z'),
      createdAfter: '2026-09-10T19:05:00Z' });
    assert.equal(first.dispatched.runId, 9001);
    await assert.rejects(runAdmission({ api: fixture.request, candidateSha: approval.sha, controlSha: fixture.control,
      admissionRunId: 9004, admissionRunAttempt: 1, evidenceFile: `${directory}/repeat.json`, authorization,
      authorizationNow: new Date('2026-09-10T19:07:00Z'), createdAfter: '2026-09-10T19:07:00Z' }),
    /PROTECTED_CUTOVER_SINGLE_USE_ALREADY_CLAIMED/);
    assert.equal(fixture.calls.filter(call => call.method === 'POST' && call.url.includes('/dispatches')).length, 1);

    const uncertain = protectedCutoverApi({ dispatchFailure: true });
    await assert.rejects(runAdmission({ api: uncertain.request, candidateSha: approval.sha, controlSha: uncertain.control,
      admissionRunId: 9005, admissionRunAttempt: 1, evidenceFile: `${directory}/uncertain.json`, authorization,
      authorizationNow: new Date('2026-09-10T19:05:00Z'), submissionAuthorizationNow: new Date('2026-09-10T19:06:00Z'),
      createdAfter: '2026-09-10T19:05:00Z' }), /dispatch response timeout/);
    await assert.rejects(runAdmission({ api: uncertain.request, candidateSha: approval.sha, controlSha: uncertain.control,
      admissionRunId: 9006, admissionRunAttempt: 2, evidenceFile: `${directory}/uncertain-restart.json`, authorization,
      authorizationNow: new Date('2026-09-10T19:07:00Z'), createdAfter: '2026-09-10T19:07:00Z' }),
    /PROTECTED_CUTOVER_SINGLE_USE_ALREADY_CLAIMED/);
    assert.equal(uncertain.calls.filter(call => call.method === 'POST' && call.url.includes('/dispatches')).length, 1);
  } finally { fs.rmSync(directory, { recursive: true, force: true }); }
});
test('submission revalidation rejects expiry and Test-protection drift after initial assessment', async () => {
  const expired = protectedCutoverApi();
  const directory = fs.mkdtempSync('/tmp/flowhive-protected-cutover-revalidation-');
  try {
    await assert.rejects(runAdmission({ api: expired.request, candidateSha: approval.sha, controlSha: expired.control,
      admissionRunId: 9007, admissionRunAttempt: 1, evidenceFile: `${directory}/expired.json`,
      authorization: protectedCutoverAuthorization(), authorizationNow: new Date('2026-09-10T19:05:00Z'),
      submissionAuthorizationNow: new Date('2026-09-10T19:16:00Z'), createdAfter: '2026-09-10T19:05:00Z' }),
    /PROTECTED_CUTOVER_APPROVAL_EXPIRED/);
    assert.equal(expired.calls.filter(call => call.method === 'POST' && call.url.includes('/dispatches')).length, 0);
    const driftedProtection = clone(nativeEnvironmentProtection);
    driftedProtection.can_admins_bypass = true;
    const drifted = protectedCutoverApi({ protectionAfterFirstRead: driftedProtection });
    await assert.rejects(runAdmission({ api: drifted.request, candidateSha: approval.sha, controlSha: drifted.control,
      admissionRunId: 9008, admissionRunAttempt: 1, evidenceFile: `${directory}/drifted.json`,
      authorization: protectedCutoverAuthorization(), authorizationNow: new Date('2026-09-10T19:05:00Z'),
      submissionAuthorizationNow: new Date('2026-09-10T19:06:00Z'), createdAfter: '2026-09-10T19:05:00Z' }),
    /STALE_SUPERSESSION_NATIVE_READBACK_BYPASS/);
    assert.equal(drifted.calls.filter(call => call.method === 'POST' && call.url.includes('/dispatches')).length, 0);
  } finally { fs.rmSync(directory, { recursive: true, force: true }); }
});
test('submission revalidation refreshes jobs, approvals and the complete nonterminal inventory after reservation', async () => {
  const directory = fs.mkdtempSync('/tmp/flowhive-protected-cutover-final-observation-');
  const authorization = protectedCutoverAuthorization();
  const cases = [
    { name: 'jobs', mutate: ({ setJobCount }) => setJobCount(1), expected: /PROTECTED_CUTOVER_ATTEMPT_JOBS/ },
    { name: 'approvals', mutate: ({ setApprovalHistory }) => setApprovalHistory([{ id: 44 }]), expected: /PROTECTED_CUTOVER_APPROVAL_HISTORY/ },
    { name: 'fourth request', mutate: ({ addRun }) => addRun({ id: 99999999999, status: 'queued' }), expected: /PSA_UNRESOLVED_DEPLOYMENT_RUN/ }
  ];
  try {
    for (const item of cases) {
      const fixture = protectedCutoverApi({ afterReservation: item.mutate });
      await assert.rejects(runAdmission({ api: fixture.request, candidateSha: approval.sha, controlSha: fixture.control,
        admissionRunId: 9010, admissionRunAttempt: 1, evidenceFile: `${directory}/${item.name}.json`, authorization,
        authorizationNow: new Date('2026-09-10T19:05:00Z'), submissionAuthorizationNow: new Date('2026-09-10T19:06:00Z') }), item.expected);
      assert.equal(fixture.calls.filter(call => call.method === 'POST' && call.url.includes('/dispatches')).length, 0, item.name);
    }
  } finally { fs.rmSync(directory, { recursive: true, force: true }); }
});
test('single-use authorization is stable across controller changes and trusts structured reservation authorship', async () => {
  const fixture = protectedCutoverApi();
  const reference = protectedCutoverAuthorization().approvalReference;
  const claim = await claimSingleUse(fixture.request, { candidateSha: approval.sha, controlSha: fixture.control,
    approvalReference: reference });
  assert.deepEqual(claim.author, { login: 'ahmedadeyemi-cts', id: 244059331 });
  await assert.rejects(claimSingleUse(fixture.request, { candidateSha: approval.sha, controlSha: '1'.repeat(40), approvalReference: reference }),
    /PROTECTED_CUTOVER_CLAIM_CONTROLLER_CHANGED/);

  const untrusted = protectedCutoverApi();
  untrusted.comments.push({ id: 7001,
    body: `FLOWHIVE_PSA_ADMISSION_CLAIM_V1 candidate=${approval.sha} approval=${reference} controller=${untrusted.control} status=reserved observedAt=2026-09-10T19:05:00Z`,
    user: { login: 'untrusted-user', id: 123 } });
  await assert.rejects(claimSingleUse(untrusted.request, { candidateSha: approval.sha, controlSha: untrusted.control, approvalReference: reference }),
    /PROTECTED_CUTOVER_CLAIM_UNTRUSTED/);

  const malformed = protectedCutoverApi();
  malformed.comments.push({ id: 7002, body: 'FLOWHIVE_PSA_ADMISSION_CLAIM_V1 candidate=malformed', user: { login: 'ahmedadeyemi-cts', id: 244059331 } });
  await assert.rejects(claimSingleUse(malformed.request, { candidateSha: approval.sha, controlSha: malformed.control, approvalReference: reference }),
    /PROTECTED_CUTOVER_CLAIM_MALFORMED/);

  const uncertainComments = [];
  let reservationWrites = 0;
  const uncertainReservationApi = async (url, method = 'GET', body) => {
    if (url.startsWith(`issues/${candidatePullRequest}/comments?`)) return uncertainComments;
    if (url === `issues/${candidatePullRequest}/comments` && method === 'POST') {
      reservationWrites += 1;
      uncertainComments.push({ id: 7003, body: body.body, user: { login: 'ahmedadeyemi-cts', id: 244059331 } });
      throw new Error('reservation response timeout');
    }
    throw new Error(`UNEXPECTED_RESERVATION_REQUEST ${method} ${url}`);
  };
  await assert.rejects(claimSingleUse(uncertainReservationApi, { candidateSha: approval.sha, controlSha: fixture.control, approvalReference: reference }),
    /PROTECTED_CUTOVER_SINGLE_USE_CLAIM_UNCERTAIN/);
  await assert.rejects(claimSingleUse(uncertainReservationApi, { candidateSha: approval.sha, controlSha: fixture.control, approvalReference: reference }),
    /PROTECTED_CUTOVER_SINGLE_USE_ALREADY_CLAIMED/);
  assert.equal(reservationWrites, 1);
});
test('workflow-token reservations bind the bot comment to the owner-triggered admission run', async () => {
  const comments = [];
  const control = 'e'.repeat(40);
  const api = async (url, method = 'GET', body) => {
    if (url.startsWith(`issues/${candidatePullRequest}/comments?`)) return comments;
    if (url === `issues/${candidatePullRequest}/comments` && method === 'POST') {
      const comment = { id: 7010, body: body.body,
        user: { login: 'github-actions[bot]', id: 41898282 } };
      comments.push(comment);
      return comment;
    }
    if (url === 'actions/runs/9010') return {
      id: 9010, event: 'issue_comment', head_branch: 'main', head_sha: control,
      run_attempt: 1, actor: { login: 'ahmedadeyemi-cts', id: 244059331 }
    };
    throw new Error(`UNEXPECTED_WORKFLOW_CLAIM_REQUEST ${method} ${url}`);
  };
  const claim = await claimSingleUse(api, { candidateSha: approval.sha, controlSha: control,
    approvalReference: protectedCutoverAuthorization().approvalReference,
    admissionRunId: 9010, admissionRunAttempt: 1 });
  assert.deepEqual(claim.author, { login: 'github-actions[bot]', id: 41898282 });
  assert.deepEqual(claim.execution, { id: 9010, attempt: 1, event: 'issue_comment', headSha: control, actor: 'ahmedadeyemi-cts' });
  const reused = await claimSingleUse(api, { candidateSha: approval.sha, controlSha: control,
    approvalReference: protectedCutoverAuthorization().approvalReference,
    admissionRunId: 9011, admissionRunAttempt: 1 });
  assert.equal(reused.reused, true);
  assert.equal(reused.commentId, 7010);
});
test('reviewed recovery consumes the stale bot reservation before creating one current-controller claim', async () => {
  const control = 'e'.repeat(40);
  const oldControl = 'c'.repeat(40);
  const reference = protectedCutoverAuthorization().approvalReference;
  const oldBody = `FLOWHIVE_PSA_ADMISSION_CLAIM_V1 candidate=${approval.sha} approval=${reference} controller=${oldControl} status=reserved observedAt=2026-09-10T22:11:16.764Z`;
  const comments = [{ id: 7020, body: oldBody, user: { login: 'github-actions[bot]', id: 41898282 } }];
  const recovery = { commentId: 7020, admissionRunId: 9020, admissionRunAttempt: 1,
    candidateSha: approval.sha, approvalReference: reference, controllerSha: oldControl,
    observedAt: '2026-09-10T22:11:16.764Z', status: 'pre-dispatch-failed',
    dispatchSubmitted: false, controllerMutation: false };
  const api = async (url, method = 'GET', body) => {
    if (url.startsWith(`issues/${candidatePullRequest}/comments?`)) return comments;
    if (url === 'issues/comments/7020') return comments[0];
    if (url === `issues/${candidatePullRequest}/comments` && method === 'POST') {
      const comment = { id: 7021, body: body.body, user: { login: 'github-actions[bot]', id: 41898282 } };
      comments.push(comment); return comment;
    }
    if (url === 'actions/runs/9020') return { id: 9020, event: 'issue_comment', head_branch: 'main', head_sha: oldControl,
      run_attempt: 1, actor: { login: 'ahmedadeyemi-cts' }, status: 'completed', conclusion: 'failure' };
    if (url === 'actions/runs/9021') return { id: 9021, event: 'issue_comment', head_branch: 'main', head_sha: control,
      run_attempt: 1, actor: { login: 'ahmedadeyemi-cts' } };
    throw new Error(`UNEXPECTED_RECOVERY_REQUEST ${method} ${url}`);
  };
  const claim = await claimSingleUse(api, { candidateSha: approval.sha, controlSha: control,
    approvalReference: reference, admissionRunId: 9021, admissionRunAttempt: 1, reservationRecovery: recovery });
  assert.deepEqual(claim.supersededReservations, [{ commentId: 7020, admissionRunId: 9020,
    controllerSha: oldControl, observedAt: recovery.observedAt, disposition: 'pre-dispatch-failed' }]);
  assert.equal(claim.commentId, 7021);
  assert.equal(comments.length, 2);
});
test('reviewed recovery consumes a terminal skipped dispatch before creating one current-controller claim', async () => {
  const control = 'f'.repeat(40);
  const oldControl = 'b'.repeat(40);
  const reference = protectedCutoverAuthorization().approvalReference;
  const oldBody = `FLOWHIVE_PSA_ADMISSION_CLAIM_V1 candidate=${approval.sha} approval=${reference} controller=${oldControl} run=9020 attempt=1 status=reserved observedAt=2026-09-12T10:10:03.389Z`;
  const comments = [{ id: 7022, body: oldBody, user: { login: 'github-actions[bot]', id: 41898282 } }];
  const receiptPayload = { schema: 'flowhive-psa-dispatch-attempt-v1', repository,
    candidatePullRequest, candidateSha: approval.sha, controllerSha: oldControl, workflowId: 315562561,
    workflowPath: '.github/workflows/projectpulse-deploy-test.yml', admissionRunId: 9020,
    admissionRunAttempt: 1, attempt: 1, dispatchAttempted: true, dispatchWriteCount: 1,
    dispatch: { method: 'POST', path: 'actions/workflows/315562561/dispatches', requestFingerprint: 'b'.repeat(64) },
    run: { id: 9022, headSha: oldControl, headBranch: 'main', event: 'workflow_dispatch' } };
  const recovery = { commentId: 7022, admissionRunId: 9020, admissionRunAttempt: 1,
    candidateSha: approval.sha, approvalReference: reference, controllerSha: oldControl,
    observedAt: '2026-09-12T10:10:03.389Z', status: 'terminal-skipped-no-mutation',
    dispatchSubmitted: true, controllerMutation: false, deploymentRunId: 9022, deploymentRunAttempt: 1,
    dispatchReceipt: { artifactId: 9023, artifactName: 'flowhive-psa-dispatch-evidence-9020-1',
      artifactDigest: `sha256:${'a'.repeat(64)}`, schema: 'flowhive-psa-dispatch-attempt-v1',
      candidateSha: approval.sha, controllerSha: oldControl, admissionRunId: 9020,
      admissionRunAttempt: 1, dispatchPath: 'actions/workflows/315562561/dispatches',
      dispatchRequestFingerprint: 'b'.repeat(64), dispatchWriteCount: 1,
      deploymentRunId: 9022, deploymentRunAttempt: 1 } };
  const api = async (url, method = 'GET', body) => {
    if (url.startsWith(`issues/${candidatePullRequest}/comments?`)) return comments;
    if (url === 'issues/comments/7022') return comments[0];
    if (url === `issues/${candidatePullRequest}/comments` && method === 'POST') {
      const comment = { id: 7023, body: body.body, user: { login: 'github-actions[bot]', id: 41898282 } };
      comments.push(comment); return comment;
    }
    if (url === 'actions/runs/9020') return { id: 9020, event: 'issue_comment', head_branch: 'main', head_sha: oldControl,
      run_attempt: 1, actor: { login: 'ahmedadeyemi-cts' }, status: 'completed', conclusion: 'success' };
    if (url === 'actions/runs/9021') return { id: 9021, event: 'issue_comment', head_branch: 'main', head_sha: control,
      run_attempt: 1, actor: { login: 'ahmedadeyemi-cts' } };
    if (url === 'actions/runs/9022') return { id: 9022, workflow_id: 315562561, event: 'workflow_dispatch',
      head_branch: 'main', head_sha: oldControl, run_attempt: 1, status: 'completed', conclusion: 'skipped' };
    if (url === 'actions/artifacts/9023') return { id: 9023, name: 'flowhive-psa-dispatch-evidence-9020-1',
      digest: `sha256:${'a'.repeat(64)}`, expired: false };
    if (url === 'actions/runs/9020/artifacts?per_page=100') return { total_count: 1, artifacts: [{
      id: 9023, name: 'flowhive-psa-dispatch-evidence-9020-1', digest: `sha256:${'a'.repeat(64)}`, expired: false
    }] };
    if (url === 'actions/runs/9022/jobs?per_page=100') return { jobs: [{ id: 90220, status: 'completed', conclusion: 'skipped' }] };
    if (url === 'actions/runs/9022/pending_deployments') return [];
    throw new Error(`UNEXPECTED_TERMINAL_SKIP_RECOVERY_REQUEST ${method} ${url}`);
  };
  const claim = await claimSingleUse(api, { candidateSha: approval.sha, controlSha: control,
    approvalReference: reference, admissionRunId: 9021, admissionRunAttempt: 1, reservationRecovery: recovery,
    artifactArchiveReader: async () => receiptPayload });
  assert.deepEqual(claim.supersededReservations, [{ commentId: 7022, admissionRunId: 9020,
    controllerSha: oldControl, observedAt: recovery.observedAt,
    disposition: 'terminal-skipped-no-mutation', deploymentRunId: 9022 }]);
  assert.equal(claim.commentId, 7023);
  assert.equal(comments.length, 2);
});
test('terminal skipped recovery rejects missing or mismatched dispatch receipts', () => {
  const missing = protectedCutoverAuthorization();
  delete missing.reservationRecovery.dispatchReceipt;
  assert.throws(() => verifyProtectedCutoverAuthorization(missing, new Date('2026-09-10T19:05:00Z')),
    /PROTECTED_CUTOVER_DISPATCH_RECEIPT_REQUIRED/);
  const mismatched = protectedCutoverAuthorization();
  mismatched.reservationRecovery.dispatchReceipt.deploymentRunId += 1;
  assert.throws(() => verifyProtectedCutoverAuthorization(mismatched, new Date('2026-09-10T19:05:00Z')),
    /PROTECTED_CUTOVER_DISPATCH_RECEIPT_DEPLOYMENT_RUN/);
});
test('dispatch receipt parser reads a real ZIP from non-seekable stdin and rejects malformed archives', () => {
  const expected = { schema: 'flowhive-psa-dispatch-attempt-v1', run: { id: 9022 } };
  const archive = execFileSync('python3', ['-c', [
    'import io, sys, zipfile',
    'buffer = io.BytesIO()',
    "with zipfile.ZipFile(buffer, 'w') as archive: archive.writestr('flowhive-psa-dispatch-attempt.json', sys.stdin.read())",
    'sys.stdout.buffer.write(buffer.getvalue())'
  ].join('\n')], { input: JSON.stringify(expected) });
  assert.deepEqual(parseDispatchReceiptArchive(archive), expected);
  assert.throws(() => parseDispatchReceiptArchive(Buffer.from('not-a-zip')), /PROTECTED_CUTOVER_DISPATCH_RECEIPT_PAYLOAD_INVALID/);
});
test('protected lifecycle keeps the active operating state after one approved bootstrap', async () => {
  const fixture = protectedCutoverApi({ state: 'disabled_manually' });
  const directory = fs.mkdtempSync('/tmp/flowhive-protected-cutover-lifecycle-success-');
  try {
    const result = await runProtectedAdmissionLifecycle({
      api: fixture.request, activationRequired: true,
      activationOptions: { candidateSha: approval.sha, executingControllerSha: fixture.control,
        authorization: protectedCutoverAuthorization(), authorizationNow: new Date('2026-09-10T19:05:00Z') },
      admissionOptions: { candidateSha: approval.sha, controlSha: fixture.control, admissionRunId: 9011,
        admissionRunAttempt: 1, evidenceFile: `${directory}/success.json`, authorization: protectedCutoverAuthorization(),
        authorizationNow: new Date('2026-09-10T19:05:00Z'), submissionAuthorizationNow: new Date('2026-09-10T19:06:00Z'),
        createdAfter: '2026-09-10T19:05:00Z' }
    });
    assert.equal(result.lifecycle.policy, 'active-after-approved-bootstrap');
    assert.equal(result.lifecycle.finalState.state, 'active');
    assert.equal(result.lifecycle.restored, false);
    assert.deepEqual(fixture.calls.filter(call => call.method === 'PUT').map(call => call.url), ['actions/workflows/315562561/enable']);
    assert.equal(JSON.parse(fs.readFileSync(`${directory}/success.json`, 'utf8')).lifecycle.finalState.state, 'active');
  } finally { fs.rmSync(directory, { recursive: true, force: true }); }
});
test('protected lifecycle restores only pre-dispatch failures and preserves cleanup failures', async () => {
  const directory = fs.mkdtempSync('/tmp/flowhive-protected-cutover-lifecycle-failure-');
  const base = { candidateSha: approval.sha, controlSha: 'e'.repeat(40), admissionRunId: 9012,
    admissionRunAttempt: 1, authorization: protectedCutoverAuthorization(), authorizationNow: new Date('2026-09-10T19:05:00Z'),
    submissionAuthorizationNow: new Date('2026-09-10T19:06:00Z') };
  try {
    const beforeWrite = protectedCutoverApi({ state: 'disabled_manually', afterReservation: ({ setJobCount }) => setJobCount(1) });
    await assert.rejects(runProtectedAdmissionLifecycle({ api: beforeWrite.request, activationRequired: true,
      activationOptions: { candidateSha: approval.sha, executingControllerSha: beforeWrite.control,
        authorization: protectedCutoverAuthorization(), authorizationNow: base.authorizationNow },
      admissionOptions: { ...base, controlSha: beforeWrite.control, evidenceFile: `${directory}/before-write.json` } }), error => {
      assert.equal(error.lifecycle.policy, 'restore-disabled-before-dispatch');
      assert.equal(error.lifecycle.finalState.state, 'disabled_manually');
      return true;
    });
    assert.equal(beforeWrite.calls.filter(call => call.method === 'POST' && call.url.includes('/dispatches')).length, 0);

    const cleanupFailure = protectedCutoverApi({ state: 'disabled_manually', disableFailure: true,
      afterReservation: ({ setJobCount }) => setJobCount(1) });
    await assert.rejects(runProtectedAdmissionLifecycle({ api: cleanupFailure.request, activationRequired: true,
      activationOptions: { candidateSha: approval.sha, executingControllerSha: cleanupFailure.control,
        authorization: protectedCutoverAuthorization(), authorizationNow: base.authorizationNow },
      admissionOptions: { ...base, controlSha: cleanupFailure.control, evidenceFile: `${directory}/cleanup-failure.json` } }), error => {
      assert.ok(error instanceof AggregateError);
      assert.match(error.primaryError.message, /PROTECTED_CUTOVER_ATTEMPT_JOBS/);
      assert.match(error.cleanupError.message, /restoration failed/);
      assert.equal(error.lifecycle.finalState.state, 'active');
      return true;
    });

    const uncertain = protectedCutoverApi({ state: 'disabled_manually', dispatchFailure: true });
    await assert.rejects(runProtectedAdmissionLifecycle({ api: uncertain.request, activationRequired: true,
      activationOptions: { candidateSha: approval.sha, executingControllerSha: uncertain.control,
        authorization: protectedCutoverAuthorization(), authorizationNow: base.authorizationNow },
      admissionOptions: { ...base, controlSha: uncertain.control, evidenceFile: `${directory}/uncertain.json` } }), error => {
      assert.equal(error.lifecycle.policy, 'retain-active-after-uncertain-dispatch');
      assert.equal(error.lifecycle.finalState.state, 'active');
      return true;
    });
    assert.deepEqual(uncertain.calls.filter(call => call.method === 'PUT').map(call => call.url), ['actions/workflows/315562561/enable']);
  } finally { fs.rmSync(directory, { recursive: true, force: true }); }
});
test('protected cutover remains fail-closed for inactive authorization, fourth runs, execution, approval, or weakened Test protection', async () => {
  const inactive = clone(JSON.parse(fs.readFileSync(new URL('../.github/flowhive-psa-protected-cutover.json', import.meta.url), 'utf8')));
  inactive.enabled = false;
  inactive.activationDecision = 'hold';
  inactive.workflow.allowControllerActivation = false;
  inactive.approval = { status: 'not-approved', approvedBy: null, approvedAt: null, expiresAt: null };
  const inactiveFixture = protectedCutoverApi();
  await assert.rejects(verifyReleaseCutover(inactiveFixture.request, {
    candidateSha: approval.sha, executingControllerSha: inactiveFixture.control, authorization: inactive
  }), /PSA_CUTOVER_RUN_NOT_TERMINAL/);
  for (const options of [
    { fourthRun: true },
    { approvalHistory: [{ id: 1 }] },
    { jobCount: 1 },
    { protection: { ...nativeEnvironmentProtection, can_admins_bypass: true } }
  ]) {
    const fixture = protectedCutoverApi(options);
    await assert.rejects(verifyReleaseCutover(fixture.request, {
      candidateSha: approval.sha, executingControllerSha: fixture.control,
      authorization: protectedCutoverAuthorization(), authorizationNow: new Date('2026-09-10T19:05:00Z')
    }));
    assert.equal(fixture.calls.filter(call => call.method === 'POST' && call.url.includes('/dispatches')).length, 0);
  }
});
test('protected source assessment binds actual blobs and rejects disconnected workflow content', () => {
  const expected = protectedCutoverRunAttestations[0];
  const source = execFileSync('git', ['show', `${expected.controllerSha}:.github/workflows/projectpulse-deploy-test.yml`], { encoding: 'utf8' });
  assert.equal(verifyProtectedHistoricalWorkflowSource(source, expected.historicalBlobs.deploymentWorkflow, expected.runId).allMutationPathsBehindTest, true);
  assert.throws(() => verifyProtectedHistoricalWorkflowSource(source.replace('environment: test', 'environment: production'), expected.historicalBlobs.deploymentWorkflow, expected.runId));
  assert.throws(() => verifyProtectedHistoricalWorkflowSource(`${source}\njobs:\n  deploy:\n    environment: test`, expected.historicalBlobs.deploymentWorkflow, expected.runId));
});
test('the maintained inspect-only entrypoint validates context and remains GET-only', async () => {
  const env = { GITHUB_REPOSITORY: repository, GITHUB_REF: 'refs/heads/main',
    GITHUB_EVENT_NAME: 'workflow_dispatch', GH_TOKEN: 'offline-fixture', GITHUB_SHA: 'e'.repeat(40) };
  const fixture = protectedCutoverApi();
  const result = await inspectReleaseCutover(fixture.request, {
    env, authorization: protectedCutoverAuthorization(), authorizationNow: new Date('2026-09-10T19:05:00Z')
  });
  assert.equal(result.operation, 'inspect-only');
  assert.equal(result.context.candidateSha, approval.sha);
  assert.equal(result.context.controllerSha, fixture.control);
  assert.ok(fixture.calls.every(call => call.method === 'GET'));
  assert.equal(fixture.calls.some(call => call.url.includes('/dispatches')), false);
  const inactive = clone(JSON.parse(fs.readFileSync(new URL('../.github/flowhive-psa-protected-cutover.json', import.meta.url), 'utf8')));
  inactive.enabled = false;
  inactive.activationDecision = 'hold';
  inactive.workflow.allowControllerActivation = false;
  inactive.approval = { status: 'not-approved', approvedBy: null, approvedAt: null, expiresAt: null };
  await assert.rejects(inspectReleaseCutover(protectedCutoverApi().request, {
    env, authorization: inactive, authorizationNow: new Date('2026-09-10T19:05:00Z')
  }), /PSA_CUTOVER_RUN_NOT_TERMINAL/);
  const expired = protectedCutoverAuthorization();
  await assert.rejects(inspectReleaseCutover(protectedCutoverApi().request, {
    env, authorization: expired, authorizationNow: new Date('2026-09-10T19:16:00Z')
  }), /PROTECTED_CUTOVER_APPROVAL_EXPIRED/);
  await assert.rejects(inspectReleaseCutover(protectedCutoverApi().request, {
    env: { ...env, GITHUB_SHA: '' }, authorization: protectedCutoverAuthorization()
  }), /PROTECTED_CUTOVER_INSPECT_CONTROLLER/);
});
test('release cutover requires terminal server state for all three requests and an active controller', async () => {
  const inactive = clone(JSON.parse(fs.readFileSync(new URL('../.github/flowhive-psa-protected-cutover.json', import.meta.url), 'utf8')));
  inactive.enabled = false;
  inactive.activationDecision = 'hold';
  inactive.workflow.allowControllerActivation = false;
  inactive.approval = { status: 'not-approved', approvedBy: null, approvedAt: null, expiresAt: null };
  const calls = [];
  const api = async (url) => {
    calls.push(url);
    if (url === 'actions/workflows/315562561') return { id: 315562561, path: '.github/workflows/projectpulse-deploy-test.yml', state: 'active' };
    const runMatch = /actions\/runs\/(\d+)$/.exec(url);
    if (runMatch) return { id: Number(runMatch[1]), workflow_id: 315562561, path: '.github/workflows/projectpulse-deploy-test.yml', event: 'workflow_dispatch', status: 'completed', conclusion: 'cancelled', head_sha: 'a'.repeat(40), run_attempt: 1 };
    if (url.includes('/jobs?')) return { jobs: [] };
    if (url.includes('/pending_deployments')) return [];
    if (url.includes('/runs?')) return { workflow_runs: [] };
    throw new Error(`UNEXPECTED_REQUEST ${url}`);
  };
  const result = await verifyReleaseCutover(api, { authorization: inactive, candidateSha: approval.sha });
  assert.equal(result.requests.length, 3);
  assert.deepEqual(result.requests.map(item => item.id), [34495606530, 34377182662, 33654881418]);
  assert.equal(calls.filter(url => /actions\/runs\/\d+$/.test(url)).length, 3);
  await assert.rejects(verifyReleaseCutover(async url => {
    if (url === 'actions/workflows/315562561') return { id: 315562561, path: '.github/workflows/projectpulse-deploy-test.yml', state: 'active' };
    if (url.endsWith('/34495606530')) return { id: 34495606530, workflow_id: 315562561, path: '.github/workflows/projectpulse-deploy-test.yml', event: 'workflow_dispatch', status: 'queued' };
    throw new Error(`UNEXPECTED_REQUEST ${url}`);
  }, { authorization: inactive, candidateSha: approval.sha }), /PSA_CUTOVER_RUN_NOT_TERMINAL id=34495606530 status=queued/);
});
test('environment job remains serialized and cannot publish source or target production', () => {
  const controller=fs.readFileSync(new URL('../.github/workflows/projectpulse-deploy-test.yml',import.meta.url),'utf8');
  verifyController(controller);
  assert.throws(()=>verifyController(controller.replace('environment: test','environment: production')));
  assert.throws(()=>verifyController(controller.replace('cancel-in-progress: false','cancel-in-progress: true')));
  assert.throws(()=>verifyController(controller.replace('contents: read','contents: write')));
});

test('temporary stale supersession activation is bounded and native-gated', () => {
  const configured = JSON.parse(fs.readFileSync(new URL('../.github/flowhive-psa-stale-run-supersession-authorization.json', import.meta.url), 'utf8'));
  const renewal = process.env.GITHUB_HEAD_REF === staleSupersessionRenewalBranch;
  assert.equal(configured.enabled, renewal);
  assert.equal(configured.activationDecision, renewal ? 'approved' : 'hold');
  assert.equal(configured.approval.status, renewal ? 'approved' : 'not-approved');
  assert.equal(staleRunSupersessionApproved(configured, renewal ? new Date(configured.approval.approvedAt) : new Date()), renewal);
  const inactive = clone(configured);
  inactive.enabled = false;
  inactive.activationDecision = 'hold';
  inactive.approval = { status: 'not-approved', approvedBy: null, approvedAt: null, expiresAt: null };
  assert.equal(staleRunSupersessionApproved(inactive), false);
  assert.equal(staleRunSupersessionApproved(staleAuthorization(), new Date('2026-09-09T22:05:00Z')), true);
  assert.throws(() => verifyStaleSupersessionAuthorization({ ...staleAuthorization(), approval: { ...staleAuthorization().approval, expiresAt: '2026-09-09T22:04:59Z' } }, new Date('2026-09-09T22:05:00Z')), /EXPIRED/);
  assert.throws(() => verifyStaleSupersessionAuthorization({ ...staleAuthorization(), approval: { ...staleAuthorization().approval, approvedAt: null } }, new Date('2026-09-09T22:05:00Z')), /APPROVAL_DATES/);
});
test('the unresolved current request has an explicit blocking disposition', () => {
  const authorization = JSON.parse(fs.readFileSync(new URL('../.github/flowhive-psa-stale-run-supersession-authorization.json', import.meta.url), 'utf8'));
  assert.deepEqual(authorization.evidence.currentOutstandingRequest, {
    runId: 34495606530,
    workflowId: 315562561,
    workflowPath: '.github/workflows/projectpulse-deploy-test.yml',
    controllerSha: '9f30078c2c407d4d3576ccefd663a145be50c6c4',
    status: 'queued',
    jobs: 0,
    pendingDeployments: 0,
    approvalPerformed: false,
    disposition: 'blocking-hold',
    dispositionSource: 'release-owner-record',
    nextAction: 'Use one separately reviewed run-control operation for each of the three queued requests, verify server-confirmed terminal state and no execution, then use the native workflow enable operation once and verify active identity before a new admission.'
  });
});

test('request-to-run binding is exact and absent evidence cannot be promoted', () => {
  const binding = staleAuthorization().evidence.requestToRunBinding;
  assert.deepEqual(verifyRequestRunBinding(binding), {
    bound: true, runId: staleRunSupersessionAttestation.runId,
    controllerSha: staleRunSupersessionAttestation.controllerSha
  });
  for (const mutate of [
    x => { x.status = 'not-established'; },
    x => { x.runId += 1; },
    x => { x.controllerSha = '0'.repeat(40); },
    x => { x.requestId = null; },
    x => { x.requestBodySha256 = null; },
    x => { x.response.workflowRunId += 1; }
  ]) {
    const candidate = clone(binding); mutate(candidate);
    assert.throws(() => verifyRequestRunBinding(candidate));
  }
  const inactive = JSON.parse(fs.readFileSync(new URL('../.github/flowhive-psa-stale-run-supersession-authorization.json', import.meta.url), 'utf8'));
  assert.throws(() => verifyRequestRunBinding(inactive.evidence.requestToRunBinding));
});

test('native Test protection is an exact saved barrier and rejects weakened readback', () => {
  assert.deepEqual(verifyNativeEnvironmentProtection(nativeEnvironmentProtection,
    staleAuthorization().historicalExecutionProtection.nativeEnvironmentBarrier), {
    environment: 'test', protectionRuleId: 65110773, reviewer: 'ahmedadeyemi-cts',
    reviewers: [{ type: 'User', id: 244059331, login: 'ahmedadeyemi-cts', slug: null }],
    preventSelfReview: false, canAdminsBypass: false
  });
  for (const mutate of [
    value => { value.can_admins_bypass = true; },
    value => { value.protection_rules[0].id = 0; },
    value => { value.protection_rules[0].prevent_self_review = true; },
    value => { value.protection_rules[0].reviewers[0].reviewer.login = 'other-user'; },
    value => { value.protection_rules = []; }
  ]) {
    const candidate = clone(nativeEnvironmentProtection); mutate(candidate);
    assert.throws(() => verifyNativeEnvironmentProtection(candidate,
      staleAuthorization().historicalExecutionProtection.nativeEnvironmentBarrier));
  }
});
test('protected Test protection rejects an extra or duplicate required reviewer', () => {
  const expected = protectedCutoverAuthorization().environment;
  for (const mutate of [
    value => value.protection_rules[0].reviewers.push({ type: 'User', reviewer: { login: 'other-user', id: 987654321 } }),
    value => value.protection_rules[0].reviewers.push({ type: 'Team', reviewer: { slug: 'release-team', id: 987654322 } }),
    value => value.protection_rules[0].reviewers.push(value.protection_rules[0].reviewers[0])
  ]) {
    const candidate = clone(nativeEnvironmentProtection); mutate(candidate);
    assert.throws(() => verifyNativeEnvironmentProtection(candidate, expected));
  }
  assert.doesNotThrow(() => verifyNativeEnvironmentProtection(nativeEnvironmentProtection, expected));
});

test('stale request remains blocking when the owner approval gate is absent', async () => {
  const evidence = staleRun();
  const inactive = clone(JSON.parse(fs.readFileSync(new URL('../.github/flowhive-psa-stale-run-supersession-authorization.json', import.meta.url), 'utf8')));
  inactive.enabled = false;
  inactive.activationDecision = 'hold';
  inactive.approval = { status: 'not-approved', approvedBy: null, approvedAt: null, expiresAt: null };
  const api = async url => {
    if (url.includes('/runs?status=queued')) return { workflow_runs: [{ id: staleRunSupersessionAttestation.runId, status: 'queued' }] };
    if (url.includes('/runs?')) return { workflow_runs: [] };
    if (url === `actions/runs/${staleRunSupersessionAttestation.runId}`) return evidence.run;
    if (url.includes('/attempts/1/jobs')) return evidence.attemptJobs;
    if (url.includes('/pending_deployments')) return evidence.pendingDeployments;
    if (url.includes('/concurrency_groups')) return evidence.concurrencyGroups;
    if (url.includes('/artifacts')) return evidence.artifacts;
    if (url === 'git/ref/heads/main') return { object: { sha: evidence.currentMainSha } };
    throw new Error(`UNEXPECTED_REQUEST ${url}`);
  };
  await assert.rejects(requireIdleRuns(api, evidence.workflow, false, evidence.currentMainSha, inactive), /PSA_ANOTHER_DEPLOYMENT_IS_ACTIVE/);
});

test('historical stale request is fenced by native Test protection without fabricated request evidence', () => {
  assert.deepEqual(verifyFencedStaleRun(staleRun()), {
    fenced: true, inputIdentity: 'not-server-confirmed-and-not-used',
    nativeEnvironmentBarrier: true, productionMutation: false
  });
  const initiallyActive = staleRun(); initiallyActive.workflow.state = 'active';
  assert.deepEqual(verifyFencedStaleRun({ ...initiallyActive, requireSealed: false }), {
    fenced: true, inputIdentity: 'not-server-confirmed-and-not-used',
    nativeEnvironmentBarrier: true, productionMutation: false
  });
  assert.throws(() => verifyFencedStaleRun(initiallyActive), /sealed before supersession/);
  const mutations = [
    x => { x.workflow.state = 'active'; },
    x => { x.run.status = 'in_progress'; },
    x => { x.run.updated_at = '2026-09-09T16:31:00Z'; },
    x => { x.run.head_sha = approval.sha; },
    x => { x.run.run_attempt = 2; },
    x => { x.attemptJobs = { total_count: 1, jobs: [{ id: 1 }] }; },
    x => { x.pendingDeployments = [{ environment: { name: 'test' } }]; },
    x => { x.concurrencyGroups = { total_count: 1, concurrency_groups: [{ id: 1 }] }; },
    x => { x.artifacts = { total_count: 1, artifacts: [{ id: 1 }] }; },
    x => { x.currentMainSha = undefined; },
    x => { x.executingControllerSha = 'not-a-sha'; },
    x => { x.currentMainSha = staleRunSupersessionAttestation.controllerSha; },
    x => { x.serverDispatchInputs = { release_sha: approval.sha }; }
  ];
  for (const mutate of mutations) {
    const evidence = staleRun(); mutate(evidence);
    assert.throws(() => verifyFencedStaleRun(evidence));
  }
});

test('historical source content identity and cleanup proof are fail-closed', () => {
  assert.equal(verifyHistoricalFenceSources(historicalFence).allDeploymentPathsProtected, false);
  for (const mutate of [
    source => ({ ...source, admission: source.admission.replace('The trusted main controller is no longer current.', 'removed') }),
    source => ({ ...source, dispatcher: source.dispatcher.replace('await authorize();', 'await sealIdleController();') }),
    source => ({ ...source, deploymentWorkflow: source.deploymentWorkflow.replace("always() && steps.module025_fixture.outputs.started == 'true'", 'always()') }),
    source => ({ ...source, admissionBlob: '0'.repeat(40) }),
    source => ({ ...source, deploymentWorkflow: `${source.deploymentWorkflow}\njobs:\n  deploy:\n    if: github.event_name == 'push'` })
  ]) {
    assert.throws(() => verifyHistoricalFenceSources(mutate(historicalFence)));
  }
});

test('real stale inventory uses the native barrier and rejects a competing request', async () => {
  const evidence = staleRun();
  const authorization = staleAuthorization();
  const authorizationNow = new Date('2026-09-09T22:05:00Z');
  const api = async url => {
    if (url.includes('/runs?status=queued')) return { workflow_runs: [{ id: staleRunSupersessionAttestation.runId, status: 'queued' }] };
    if (url.includes('/runs?')) return { workflow_runs: [] };
    if (url === `actions/runs/${staleRunSupersessionAttestation.runId}`) return evidence.run;
    if (url.includes('/attempts/1/jobs')) return evidence.attemptJobs;
    if (url.includes('/pending_deployments')) return evidence.pendingDeployments;
    if (url.includes('/concurrency_groups')) return evidence.concurrencyGroups;
    if (url.includes('/artifacts')) return evidence.artifacts;
    if (url === 'git/ref/heads/main') return { object: { sha: evidence.currentMainSha } };
    if (url === 'environments/test') return nativeEnvironmentProtection;
    throw new Error(`UNEXPECTED_REQUEST ${url}`);
  };
  await requireIdleRuns(api, evidence.workflow, false, evidence.currentMainSha, authorization, authorizationNow, historicalFence);
  await assert.rejects(requireIdleRuns(async url => {
    if (url.includes('/runs?status=queued')) return { workflow_runs: [{ id: 999 }, { id: staleRunSupersessionAttestation.runId, status: 'queued' }] };
    return api(url);
  }, evidence.workflow, false, evidence.currentMainSha, authorization, authorizationNow, historicalFence), /PSA_ANOTHER_DEPLOYMENT_IS_ACTIVE/);
});
test('stale supersession scope is bound to trusted main and its reviewed control boundary', () => {
  verifyFiles(staleSupersessionFiles, files, 'stale-run-supersession', {
    base: '785eb54a4f280c9ff0e59951c31a30cad4c1a0da', branch: 'fix/flowhive-stale-run-supersession-20260909'
  });
  assert.throws(() => verifyFiles(staleSupersessionFiles, files, 'stale-run-supersession', {
    base: 'c6efce9a4918ac6674fa292586348a5aa8be2b91', branch: 'fix/flowhive-stale-run-supersession-20260909'
  }));
  assert.throws(() => verifyFiles([...staleSupersessionFiles, 'scripts/release-test/flowhive-psa-admission.mjs'], files,
    'stale-run-supersession', { base: '785eb54a4f280c9ff0e59951c31a30cad4c1a0da', branch: 'fix/flowhive-stale-run-supersession-20260909' }));
  verifyFiles(staleSupersessionActivationFiles, files, 'stale-run-activation', {
    base: staleSupersessionActivationBase, branch: staleSupersessionActivationBranch
  });
  assert.throws(() => verifyFiles(staleSupersessionActivationFiles, files, 'stale-run-activation', {
    base: '785eb54a4f280c9ff0e59951c31a30cad4c1a0da', branch: staleSupersessionActivationBranch
  }));
});
test('source-only control CI defers live readiness to the locked admission workflow', () => {
  const sourceCi=fs.readFileSync(new URL('../.github/workflows/flowhive-psa-release-control-ci.yml',import.meta.url),'utf8');
  const admission=fs.readFileSync(new URL('../.github/workflows/flowhive-psa-protected-test-admission.yml',import.meta.url),'utf8');
  assert.doesNotMatch(sourceCi,/dispatch-flowhive-psa-test\.mjs\s+--inspect-only/);
  assert.match(admission,/node scripts\/release-test\/dispatch-flowhive-psa-test\.mjs/);
  assert.match(admission,/group: module025-protected-uat-control/);
  assert.doesNotMatch(admission,/FLOWHIVE_PSA_STALE_RUN_SUPERSESSION_AUTHORIZATION_FILE/);
  assert.match(admission,/FLOWHIVE_PSA_DISPATCH_EVIDENCE_FILE: \$\{\{ runner\.temp \}\}\/flowhive-psa-dispatch-attempt\.json/);
});

function controllerApi({state='active',runs=[],quarantinedJobs=0,metadata={},onDisable}={}) {
  const calls=[];
  const request=async (url,method='GET')=>{
    calls.push({url,method});
    if(url.endsWith('/disable') && method==='PUT') {state='disabled_manually';onDisable?.();return null;}
    if(url==='actions/workflows/315562561')return {id:315562561,path:'.github/workflows/projectpulse-deploy-test.yml',state,...metadata};
    if(url.includes('/runs?'))return {workflow_runs:runs};
    if(url.includes('/jobs?'))return {total_count:quarantinedJobs,jobs:quarantinedJobs ? [{id:1}] : []};
    throw new Error('UNEXPECTED_TEST_REQUEST');
  };
  return {request,calls,runs};
}
const activeAdmissionOptions = () => ({
  authorization: staleAuthorization(),
  authorizationNow: new Date('2026-09-09T22:05:00Z')
});
test('the locked admission path blocks the stale request while authorization is inactive', async()=>{
  const a=controllerApi({runs:[{id:staleRunSupersessionAttestation.runId}]});
  const inactive = clone(JSON.parse(fs.readFileSync(new URL('../.github/flowhive-psa-stale-run-supersession-authorization.json', import.meta.url), 'utf8')));
  inactive.enabled = false;
  inactive.activationDecision = 'hold';
  inactive.approval = { status: 'not-approved', approvedBy: null, approvedAt: null, expiresAt: null };
  await assert.rejects(sealIdleController(a.request, { authorization: inactive }),/PSA_ANOTHER_DEPLOYMENT_IS_ACTIVE/);
  assert.ok(a.calls.every(c=>c.method==='GET'));
});
test('sealed admission accepts the real stale run only behind the native barrier', async()=>{
  const evidence=staleRun(); const calls=[]; let state='active';
  const api=async(url,method='GET')=>{
    calls.push({url,method});
    if(url.endsWith('/disable') && method==='PUT'){state='disabled_manually';return null;}
    if(url==='actions/workflows/315562561')return {id:315562561,path:'.github/workflows/projectpulse-deploy-test.yml',state};
    if(url.includes('/runs?'))return {workflow_runs:[evidence.run]};
    if(url===`actions/runs/${evidence.run.id}`)return evidence.run;
    if(url.includes('/attempts/1/jobs'))return evidence.attemptJobs;
    if(url.includes('/pending_deployments'))return evidence.pendingDeployments;
    if(url.includes('/concurrency_groups'))return evidence.concurrencyGroups;
    if(url.includes('/artifacts'))return evidence.artifacts;
    if(url==='git/ref/heads/main')return {object:{sha:evidence.currentMainSha}};
    if(url==='environments/test')return nativeEnvironmentProtection;
    throw new Error(`UNEXPECTED_TEST_REQUEST ${url}`);
  };
  const result = await sealIdleController(api,{executingControllerSha:evidence.executingControllerSha,
    authorization:evidence.authorization,authorizationNow:evidence.authorizationNow,historicalSources:historicalFence});
  assert.equal(result.state, 'disabled_manually');
  assert.deepEqual(calls.filter(x=>x.method!=='GET'),[
    {url:'actions/workflows/315562561/disable',method:'PUT'}
  ]);
});
test('read-only probe reports active idle admissions without changing workflow state',async()=>{
  const a=controllerApi();const result=await inspectIdleController(a.request,activeAdmissionOptions());
  assert.equal(result.requiresSealing,true);assert.equal(result.executableActiveRuns,0);
  assert.ok(a.calls.every(c=>c.method==='GET'));
});
test('idle active controller is sealed and verified with one disable and no dispatch',async()=>{
  const a=controllerApi();const result=await sealIdleController(a.request,activeAdmissionOptions());
  assert.equal(result.state,'disabled_manually');assert.equal(result.requiresSealing,false);
  assert.deepEqual(a.calls.filter(c=>c.method!=='GET'),[{url:'actions/workflows/315562561/disable',method:'PUT'}]);
});
test('already sealed admissions remain read-only',async()=>{
  const a=controllerApi({state:'disabled_manually'});await sealIdleController(a.request,activeAdmissionOptions());
  assert.ok(a.calls.every(c=>c.method==='GET'));
});
test('any executable active run blocks sealing; the quarantined id must still have zero jobs',async()=>{
  for(const options of [{runs:[{id:123}]},{runs:[{id:33654881418}],quarantinedJobs:1}]) {
    const a=controllerApi(options);await assert.rejects(sealIdleController(a.request,activeAdmissionOptions()),/ANOTHER_DEPLOYMENT/);
    assert.ok(a.calls.every(c=>c.method==='GET'));
  }
  const a=controllerApi({runs:[{id:33654881418}]});await sealIdleController(a.request,activeAdmissionOptions());
  assert.equal(a.calls.filter(c=>c.method==='PUT').length,1);
});
test('wrong workflow identity or unknown state cannot be sealed',async()=>{
  for(const metadata of [{id:42},{path:'.github/workflows/production.yml'},{state:'disabled_inactivity'}]) {
    const a=controllerApi({metadata});await assert.rejects(sealIdleController(a.request),/IDENTITY_OR_STATE/);
    assert.ok(a.calls.every(c=>c.method==='GET'));
  }
});
test('a deployment that arrives while sealing blocks subsequent admission',async()=>{
  const runs=[];const a=controllerApi({runs,onDisable:()=>runs.push({id:456})});
  await assert.rejects(sealIdleController(a.request,activeAdmissionOptions()),/ANOTHER_DEPLOYMENT/);
  assert.deepEqual(a.calls.filter(c=>c.method!=='GET').map(c=>c.url),['actions/workflows/315562561/disable']);
});
test('unverifiable active-run inventory cannot pass as idle',async()=>{
  await assert.rejects(inspectIdleController(async url=> url.includes('/runs?')?{}:
    {id:315562561,path:'.github/workflows/projectpulse-deploy-test.yml',state:'active'},activeAdmissionOptions()),/INVENTORY_INVALID/);
});

function repairContext() {
  const repository = 'ahmedadeyemi-cts/project-time-platform';
  const head = 'a'.repeat(40);
  return { eventName: 'pull_request', repository, base: repairBase, head,
    event: { number: 876, repository: { full_name: repository },
      pull_request: { number: 876, state: 'open',
        base: { ref: 'main', sha: repairBase, repo: { full_name: repository } },
        head: { ref: 'release/flowhive-psa-protected-test-admission-20260906', sha: head,
          repo: { full_name: repository } } } } };
}
test('PR874 digest repair retains the full boundary and accepts only its seven exact paths',()=>{
  verifyFiles(repairFiles,files,'pr874-digest-repair',repairContext());
  for(const extra of ['.github/workflows/projectpulse-deploy-test.yml','src/backend/ProjectTime.Api/Program.cs','database/migrations/104_flowhive_bounded_ai_execution.sql'])
    assert.throws(()=>verifyFiles([...repairFiles,extra],files,'pr874-digest-repair',repairContext()));
  assert.throws(()=>verifyFiles(repairFiles.slice(1),files,'pr874-digest-repair',repairContext()));
  assert.throws(()=>verifyFiles(repairFiles,repairFiles,'pr874-digest-repair',repairContext()));
  assert.throws(()=>verifyFiles(repairFiles,files,'unknown'));
});

test('PR876 repair cannot be reused on another base, identity, event or checkout',()=>{
  assert.throws(()=>verifyFiles(repairFiles,files,'pr874-digest-repair'));
  const mutations = [
    x=>{ x.base='b'.repeat(40); },
    x=>{ x.eventName='push'; },
    x=>{ x.repository='outsider/project-time-platform'; },
    x=>{ x.event.number=877; },
    x=>{ x.event.repository.full_name='outsider/project-time-platform'; },
    x=>{ x.event.pull_request.number=877; },
    x=>{ x.event.pull_request.state='closed'; },
    x=>{ x.event.pull_request.base.ref='release'; },
    x=>{ x.event.pull_request.base.sha='b'.repeat(40); },
    x=>{ x.event.pull_request.base.repo.full_name='outsider/project-time-platform'; },
    x=>{ x.event.pull_request.head.ref='unrelated-repair'; },
    x=>{ x.event.pull_request.head.repo.full_name='outsider/project-time-platform'; },
    x=>{ x.event.pull_request.head.sha='b'.repeat(40); },
    x=>{ x.head=''; },
    x=>{ x.event=null; }
  ];
  for (const mutate of mutations) {
    const context=repairContext();mutate(context);
    assert.throws(()=>verifyFiles(repairFiles,files,'pr874-digest-repair',context));
  }
  verifyFiles(files,files,'initial');
  assert.throws(()=>verifyFiles(repairFiles,files,'initial',repairContext()));
});
