import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import { verifyApproval, verifyPullRequest, verifyRuns, verifySourceDrift, repository, candidateBranch, candidatePullRequest } from '../scripts/release-test/flowhive-psa-admission.mjs';
import { parseCommand, buildDispatchRequest, verifyDispatchInputs, verifyDispatchRequest, verifyDispatchReceipt, verifyDispatchedRun, buildRequest, githubApiVersion, dispatchOnce, inspectIdleController, sealIdleController, requireIdleRuns, staleRunSupersessionAttestation, staleRunSupersessionApproved, verifyStaleSupersessionAuthorization, verifyHistoricalFenceSources, verifyFencedStaleRun, verifyRequestRunBinding, verifyNativeEnvironmentProtection, readHistoricalFenceSources } from '../scripts/release-test/dispatch-flowhive-psa-test.mjs';
import { files, repairFiles, repairBase, successorApprovalFiles, staleSupersessionFiles, staleSupersessionActivationFiles, staleSupersessionActivationBase, staleSupersessionActivationBranch, verifyFiles, verifyController } from './flowhive-psa-release-control.mjs';
const approval = JSON.parse(fs.readFileSync(new URL('../.github/flowhive-psa-protected-test-candidate.json', import.meta.url), 'utf8'));
const clone = x => structuredClone(x);
const pr = { number: candidatePullRequest, state: 'open', merged: false, draft: true,
  head: { ref: candidateBranch, sha: approval.sha, repo: { full_name: repository } },
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
test('approved current draft candidate is admissible without merging', () => {
  verifyApproval(approval, approval.sha); verifyPullRequest(approval, pr); verifyRuns(approval, runs);
});
for (const [field, value] of [['environment','production'], ['publicOrigin','https://elsewhere.invalid'], ['sha','1'.repeat(40)], ['allowPrivateRuntimeMutation',true], ['allowCanonicalTaskAdoption',true], ['allowCustomerPublication',true], ['projectId','1'.repeat(36)]]) {
  test('reject unapproved '+field, () => { const a=clone(approval); a[field]=value; assert.throws(()=>verifyApproval(a,approval.sha)); });
}
test('reject migration substitution and required-check dilution', () => {
  const a=clone(approval); a.migrations.reverse();assert.throws(()=>verifyApproval(a,a.sha));
  const b=clone(approval);b.requiredWorkflows=b.requiredWorkflows.slice(0,1);assert.throws(()=>verifyApproval(b,b.sha));
});
test('reject wrong repo, wrong head, changed branch and merged PR', () => {
  for(const mutate of [p=>p.head.repo.full_name='someone/fork', p=>p.head.sha='0'.repeat(40), p=>p.head.ref='main', p=>p.merged=true]) {
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
test('successor candidate binds to trusted main and rejects unincorporated application drift', () => {
  const reviewedMain = approval.sourceBase;
  const candidate = approval.sha;
  assert.match(reviewedMain, /^[0-9a-f]{40}$/);
  assert.match(candidate, /^[0-9a-f]{40}$/);
  assert.equal(approval.pullRequest, 887);
  assert.equal(approval.branch, candidateBranch);
  assert.equal(approval.sourceBase, reviewedMain);
  assert.equal(approval.sha, candidate);
  verifySourceDrift(successorApprovalFiles, files);
  for (const unrelated of [
    'src/backend/ProjectTime.Api/Program.cs',
    'src/frontend/project-time-web/src/App.jsx',
    'scripts/release-test/unincorporated-application-change.sh'
  ]) assert.throws(() => verifySourceDrift([...successorApprovalFiles, unrelated], files));
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
  const request=buildDispatchRequest(approval.sha);
  assert.equal(request.path,'actions/workflows/315562561/dispatches');
  verifyDispatchRequest(request,approval.sha);
  const serialized=buildRequest(request.path,request.method,request.body,'test-token');
  assert.equal(serialized.url,'https://api.github.com/repos/ahmedadeyemi-cts/project-time-platform/actions/workflows/315562561/dispatches');
  assert.equal(serialized.init.headers['X-GitHub-Api-Version'],githubApiVersion);
  assert.equal(serialized.init.headers['X-GitHub-Api-Version'],'2022-11-28');
  assert.deepEqual(JSON.parse(serialized.init.body),{ref:'main',return_run_details:true,inputs:{release_sha:approval.sha,release_branch:candidateBranch,recover_private_runtime:false}});
  assert.equal(new URL(serialized.url).search,'');
  assert.throws(()=>verifyDispatchRequest({...request,body:{...request.body,return_run_details:false}},approval.sha));
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
test('malformed or uncertain dispatch responses fail without a duplicate POST',async()=>{
  const control='c'.repeat(40),created='2026-09-06T00:00:00Z';
  let postCount=0;
  await assert.rejects(dispatchOnce(async()=>{postCount+=1;throw new Error('dispatch response timeout');},approval.sha,control,created),/timeout/);
  assert.equal(postCount,1);
  await assert.rejects(dispatchOnce(async(path)=>path.includes('/dispatches')?{}:null,approval.sha,control,created),/workflow run ID/);
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
  assert.equal(configured.enabled, true);
  assert.equal(configured.activationDecision, 'approved');
  assert.equal(staleRunSupersessionApproved(configured, new Date(configured.approval.approvedAt)), true);
  const inactive = clone(configured);
  inactive.enabled = false;
  inactive.activationDecision = 'hold';
  inactive.approval = { status: 'not-approved', approvedBy: null, approvedAt: null, expiresAt: null };
  assert.equal(staleRunSupersessionApproved(inactive), false);
  assert.equal(staleRunSupersessionApproved(staleAuthorization(), new Date('2026-09-09T22:05:00Z')), true);
  assert.throws(() => verifyStaleSupersessionAuthorization({ ...staleAuthorization(), approval: { ...staleAuthorization().approval, expiresAt: '2026-09-09T22:04:59Z' } }, new Date('2026-09-09T22:05:00Z')), /EXPIRED/);
  assert.throws(() => verifyStaleSupersessionAuthorization({ ...staleAuthorization(), approval: { ...staleAuthorization().approval, approvedAt: null } }, new Date('2026-09-09T22:05:00Z')), /APPROVAL_DATES/);
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
  assert.match(admission,/FLOWHIVE_PSA_STALE_RUN_SUPERSESSION_AUTHORIZATION_FILE: \.github\/flowhive-psa-stale-run-supersession-authorization\.json/);
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
