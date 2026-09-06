import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import { verifyApproval, verifyPullRequest, verifyRuns, verifySourceDrift, repository, candidateBranch } from '../scripts/release-test/flowhive-psa-admission.mjs';
import { parseCommand, verifyDispatchedRun } from '../scripts/release-test/dispatch-flowhive-psa-test.mjs';
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
