import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import { verifyApproval, verifyPullRequest, verifyRuns, verifySourceDrift, repository, candidateBranch } from '../scripts/release-test/flowhive-psa-admission.mjs';
import { parseCommand, verifyDispatchedRun, inspectIdleController, sealIdleController } from '../scripts/release-test/dispatch-flowhive-psa-test.mjs';
import { files, verifyFiles, verifyController } from './flowhive-psa-release-control.mjs';
const approval = JSON.parse(fs.readFileSync(new URL('../.github/flowhive-psa-protected-test-candidate.json', import.meta.url), 'utf8'));
const clone = x => structuredClone(x);
const pr = { number: 872, state: 'open', merged: false, draft: true,
  head: { ref: candidateBranch, sha: approval.sha, repo: { full_name: repository } },
  base: { ref: 'main', repo: { full_name: repository } } };
const runs = approval.requiredWorkflows.map((path, i) => ({ id: i + 1, path, event: 'pull_request',
  head_sha: approval.sha, status: 'completed', conclusion: 'success', run_attempt: 1,
  head_repository: { full_name: repository } }));
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
test('dispatched run identity is the main control revision plus the exact candidate title', () => {
  const control='a'.repeat(40),created='2026-09-06T00:00:00Z';
  const r={id:7,workflow_id:315562561,event:'workflow_dispatch',head_branch:'main',head_sha:control,created_at:created,display_title:'Protected Test '+approval.sha};
  assert.equal(verifyDispatchedRun(r,control,approval.sha,created),7);
  assert.throws(()=>verifyDispatchedRun({...r,head_sha:approval.sha},control,approval.sha,created));
  assert.throws(()=>verifyDispatchedRun({...r,display_title:'Protected Test '+'0'.repeat(40)},control,approval.sha,created));
});
test('environment job remains serialized and cannot publish source or target production', () => {
  const controller=fs.readFileSync(new URL('../.github/workflows/projectpulse-deploy-test.yml',import.meta.url),'utf8');
  verifyController(controller);
  assert.throws(()=>verifyController(controller.replace('environment: test','environment: production')));
  assert.throws(()=>verifyController(controller.replace('cancel-in-progress: false','cancel-in-progress: true')));
  assert.throws(()=>verifyController(controller.replace('contents: read','contents: write')));
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
test('read-only probe reports active idle admissions without changing workflow state',async()=>{
  const a=controllerApi();const result=await inspectIdleController(a.request);
  assert.equal(result.requiresSealing,true);assert.equal(result.executableActiveRuns,0);
  assert.ok(a.calls.every(c=>c.method==='GET'));
});
test('idle active controller is sealed and verified with one disable and no dispatch',async()=>{
  const a=controllerApi();const result=await sealIdleController(a.request);
  assert.equal(result.state,'disabled_manually');assert.equal(result.requiresSealing,false);
  assert.deepEqual(a.calls.filter(c=>c.method!=='GET'),[{url:'actions/workflows/315562561/disable',method:'PUT'}]);
});
test('already sealed admissions remain read-only',async()=>{
  const a=controllerApi({state:'disabled_manually'});await sealIdleController(a.request);
  assert.ok(a.calls.every(c=>c.method==='GET'));
});
test('any executable active run blocks sealing; the quarantined id must still have zero jobs',async()=>{
  for(const options of [{runs:[{id:123}]},{runs:[{id:33654881418}],quarantinedJobs:1}]) {
    const a=controllerApi(options);await assert.rejects(sealIdleController(a.request),/ANOTHER_DEPLOYMENT/);
    assert.ok(a.calls.every(c=>c.method==='GET'));
  }
  const a=controllerApi({runs:[{id:33654881418}]});await sealIdleController(a.request);
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
  await assert.rejects(sealIdleController(a.request),/ANOTHER_DEPLOYMENT/);
  assert.deepEqual(a.calls.filter(c=>c.method!=='GET').map(c=>c.url),['actions/workflows/315562561/disable']);
});
test('unverifiable active-run inventory cannot pass as idle',async()=>{
  await assert.rejects(inspectIdleController(async url=> url.includes('/runs?')?{}:
    {id:315562561,path:'.github/workflows/projectpulse-deploy-test.yml',state:'active'}),/INVENTORY_INVALID/);
});
