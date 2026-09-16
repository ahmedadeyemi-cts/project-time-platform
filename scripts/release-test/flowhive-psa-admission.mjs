import assert from 'node:assert/strict';
import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

export const repository = 'ahmedadeyemi-cts/project-time-platform';
// PR887 remains the maintained release-coordination thread. The selected
// successor is the reviewed, merged FlowHive Celar budget candidate PR1051.
export const admissionIssueNumber = 887;
export const candidatePullRequest = 1051;
export const candidateBranch = 'fix/flowhive-planner-live-celar-budget-main-20260915';
export const candidateSourceBranch = 'fix/flowhive-planner-live-celar-budget-main-20260915';
// The deployment controller intentionally checks out trusted main, while its
// protected PSA lane is named independently from the candidate source branch.
// Keep both values explicit and bounded; arbitrary workflow-dispatch refs are
// never valid for this admission path.
export const protectedTestReleaseLane = 'release/flowhive-sow-successor-20260908';
export const authorizedReleaseBranches = Object.freeze([candidateBranch, protectedTestReleaseLane]);
export const candidateMergeCommit = '451432ac7e1dc922e699293d4f2e0ad1dfaa7f88';
export const controlBranch = 'control/flowhive-planner-live-celar-budget-branch-correction-20260916';
export const approvalPath = '.github/flowhive-psa-protected-test-candidate.json';
export const controlManifest = '.github/flowhive-psa-release-control-files.txt';
export const origin = 'https://phd-west-test.onenecklab.com';
const sha = /^[a-f0-9]{40}$/;
const hash = /^[a-f0-9]{64}$/;
const migrations = [
  '103_module_066_flowhive_enterprise_psa_revamp.sql',
  '104_flowhive_bounded_ai_execution.sql',
  '105_flowhive_reviewed_regeneration.sql',
  '106_module025_sow_sell_register.sql',
  '107_module_066_operation_authorization_and_raid_actor.sql'
];
const requiredWorkflows = [
  '.github/workflows/celar-ai-enterprise-api-diagnostics.yml',
  '.github/workflows/celar-ai-production-hardening-ci.yml',
  '.github/workflows/celar-ai-enterprise-retrieval-ci.yml',
  '.github/workflows/celar-ai-runtime-rebrand-ci.yml',
  '.github/workflows/deepseek-v4-provider-ci.yml',
  '.github/workflows/flowhive-detailed-planner-ci.yml',
  '.github/workflows/flowhive-enterprise-psa-ci.yml',
  '.github/workflows/flowhive-psa-release-control-ci.yml',
  '.github/workflows/module025-governed-protected-test-release-ci.yml',
  '.github/workflows/project-planning-collaboration-ci.yml',
  '.github/workflows/projectpulse-ci.yml',
  '.github/workflows/pulse-ai-private-rag-orchestration-ci.yml',
  '.github/workflows/pulse-ai-system-intelligence-ci.yml',
  '.github/workflows/runtime-navigation-work-register-responsive-ci.yml',
  '.github/workflows/security-posture-ci.yml',
  '.github/workflows/shared-project-document-planning-ci.yml',
  '.github/workflows/systemwide-enterprise-reliability-ci.yml'
];
// Retain PR1037's reviewed check inventory as historical evidence. It is not
// silently reused for PR1039 admission; the current set above is derived from
// the exact final-head pull_request runs.
export const historicalCandidateRequiredWorkflows = Object.freeze([
  '.github/workflows/celar-ai-production-hardening-ci.yml',
  '.github/workflows/celar-ai-enterprise-retrieval-ci.yml',
  '.github/workflows/celar-ai-runtime-rebrand-ci.yml',
  '.github/workflows/deepseek-v4-provider-ci.yml',
  '.github/workflows/flowhive-detailed-planner-ci.yml',
  '.github/workflows/flowhive-enterprise-psa-ci.yml',
  '.github/workflows/flowhive-psa-release-control-ci.yml',
  '.github/workflows/projectpulse-ci.yml',
  '.github/workflows/pulse-ai-private-rag-orchestration-ci.yml',
  '.github/workflows/pulse-ai-system-intelligence-ci.yml',
  '.github/workflows/runtime-navigation-work-register-responsive-ci.yml',
  '.github/workflows/security-posture-ci.yml',
  '.github/workflows/shared-project-document-planning-ci.yml',
  '.github/workflows/systemwide-enterprise-reliability-ci.yml'
]);
export const historicalCandidatePathOmissions = Object.freeze([
  {
    workflow: '.github/workflows/enterprise-experience-system-ci.yml',
    reasonCode: 'pull-request-path-filter-no-match',
    baseCommit: '34a0f8fbbe7e79447cdf022d55ba8fb1bdf9c987',
    baseWorkflowSha256: '2ca0b78a5d3d5fa6cacfd58f0a4fbdefd944a1ace08adbf936bf5624c69e748c',
    candidateChangedFilesSha256: 'b4587a3c24dab20a92234efefdd21ef0906173eaab553c6ce993008aec374b43',
    candidateChangedFilesCount: 5
  },
  {
    workflow: '.github/workflows/celar-ai-enterprise-api-diagnostics.yml',
    reasonCode: 'pull-request-path-filter-no-match',
    baseCommit: '34a0f8fbbe7e79447cdf022d55ba8fb1bdf9c987',
    baseWorkflowSha256: 'd3fb31f6495c8e8b65d7962e796cac472170b5957c41f50f694c517e04ebef5b',
    candidateChangedFilesSha256: 'b4587a3c24dab20a92234efefdd21ef0906173eaab553c6ce993008aec374b43',
    candidateChangedFilesCount: 5
  }
]);
const retiredWorkflows = [
  '.github/workflows/projectpulse-release-test-control-ci-reregistered.yml'
];
export const historicalWorkflowExceptions = Object.freeze([
  {
    workflow: '.github/workflows/projectpulse-release-test-control-ci-reregistered.yml',
    reasonCode: 'historical-head-ref-binding-defect',
    candidateRunId: 34805681512,
    candidateRunAttempt: 1,
    candidateSha: 'cdd2f644017c96f88371dca8b81dafcab0d63b77',
    event: 'pull_request',
    conclusion: 'failure',
    sourceCorrection: 'explicit-github-head-ref-binding'
  }
]);

export const supersededCheckWorkflows = Object.freeze([
  {
    workflow: '.github/workflows/flowhive-psa-release-control-ci.yml',
    check: 'contracts'
  },
  {
    workflow: '.github/workflows/projectpulse-release-test-control-ci.yml',
    check: 'Validate governed Test controller, exact scope, and rollback boundaries'
  }
]);

// PR1051 changes the bounded Celar planner envelope, oracle tests, and
// release-validation metadata,
// not the source paths watched by the enterprise PSA or PSA controller
// workflows. Their omission is explicit and bound to the reviewed base bytes
// and exact candidate file inventory; it is not a generic missing-check
// exemption. Historical PR1037/1039 evidence remains immutable below.
export const workflowPathOmissions = Object.freeze([]);
export const workflowDispatchChecks = Object.freeze([
  {
    workflow: '.github/workflows/project-planning-collaboration-ci.yml',
    runId: 35041756021,
    runAttempt: 1,
    event: 'workflow_dispatch',
    headSha: '93e1359f1631bbcf7026fd59f1620b1f908701db',
    headBranch: 'fix/flowhive-planner-live-celar-budget-main-20260915',
    conclusion: 'success'
  },
  {
    workflow: '.github/workflows/runtime-navigation-work-register-responsive-ci.yml',
    runId: 35041757684,
    runAttempt: 1,
    event: 'workflow_dispatch',
    headSha: '93e1359f1631bbcf7026fd59f1620b1f908701db',
    headBranch: 'fix/flowhive-planner-live-celar-budget-main-20260915',
    conclusion: 'success'
  }
]);
export const workflowDispatchNonRequiredEvidence = Object.freeze([
  {
    workflow: '.github/workflows/flowhive-enterprise-psa-ci.yml',
    runId: 35009051752,
    runAttempt: 1,
    event: 'workflow_dispatch',
    headSha: 'e8a25259e8335233815cb9153f24caf4b4e519e4',
    headBranch: 'fix/flowhive-planner-celar-transport-20260915',
    status: 'completed',
    conclusion: 'failure',
    reasonCode: 'non-required-manual-validation-failure',
    failedStep: 'Test resumable observation, identity fencing and bounded network requests',
    failedAssertionCount: 3,
    satisfiesRequiredCheck: false,
    deploymentEligible: false,
    disposition: 'non-required-evidence-only'
  }
]);

export function verifySupersededCheckBinding(binding) {
  assert.equal(binding?.status, 'review-only', 'SUCCESSOR_CHECK_BINDING_STATUS');
  assert.equal(binding?.deploymentEligible, false, 'SUCCESSOR_CHECK_BINDING_MUST_NOT_AUTHORIZE_DEPLOYMENT');
  assert.deepEqual(binding?.candidate, {
    pullRequest: 1051,
    branch: 'fix/flowhive-planner-live-celar-budget-main-20260915',
    headSha: '93e1359f1631bbcf7026fd59f1620b1f908701db',
    baseSha: '41cebfbe2c6e03f4b00221c4a2d95348a36c80d0'
  }, 'SUCCESSOR_CHECK_BINDING_CANDIDATE');
  assert.deepEqual(binding?.supersedes, {
    pullRequest: 1048,
    branch: 'fix/flowhive-planner-live-celar-acceptance-20260915',
    headSha: '8f90f79d9ed281bb3a4038fc1502d75540e5fa32',
    installedAcceptanceRunId: 35031315112,
    installedAcceptanceConclusion: 'failure'
  }, 'SUCCESSOR_CHECK_BINDING_SUPERSEDED_RUN');
  assert.deepEqual(binding?.requiredChecks, supersededCheckWorkflows, 'SUCCESSOR_CHECK_BINDING_CHECK_SET');
  assert.match(binding.candidate.headSha, sha);
  assert.match(binding.candidate.baseSha, sha);
  assert.match(binding.supersedes.headSha, sha);
  assert.notEqual(binding.candidate.headSha, binding.supersedes.headSha, 'SUCCESSOR_CHECK_BINDING_MUST_CHANGE_HEAD');
  assert.equal(binding.candidate.pullRequest !== binding.supersedes.pullRequest, true, 'SUCCESSOR_CHECK_BINDING_PR_IDENTITY');
  return binding;
}

export function verifyApproval(approval, requestedSha) {
  assert.equal(approval.contract, 'flowhive-psa-protected-test-candidate-v2');
  assert.equal(approval.repository, repository);
  assert.equal(approval.pullRequest, candidatePullRequest);
  assert.equal(approval.branch, candidateBranch);
  assert.equal(approval.sourceBranch, candidateSourceBranch);
  assert.equal(approval.mergeCommit, candidateMergeCommit);
  assert.equal(approval.environment, 'test');
  assert.equal(approval.publicOrigin, origin);
  assert.equal(approval.allowPrivateRuntimeMutation, false);
  assert.equal(approval.allowCustomerPublication, false);
  assert.equal(approval.allowCanonicalTaskAdoption, false);
  assert.match(approval.sha, sha);
  assert.match(approval.sourceBase, sha);
  assert.notEqual(approval.sourceBase, approval.sha,
    'The approval base must be the reviewed trusted-main snapshot, not a self-referential candidate pin.');
  assert.equal(requestedSha, approval.sha, 'The candidate must be explicitly pinned in reviewed main-branch approval.');
  assert.deepEqual(approval.migrations.map(x => x.file), migrations);
  for (const item of approval.migrations) assert.match(item.sha256, hash);
  assert.deepEqual(approval.requiredWorkflows, requiredWorkflows,
    'The approval must enumerate the exact applicable workflow set for the selected successor head.');
  assert.equal(new Set(approval.requiredWorkflows).size, approval.requiredWorkflows.length);
  assert.deepEqual(approval.retiredWorkflows, retiredWorkflows,
    'The retired controller list must be explicit and exact.');
  for (const workflow of approval.requiredWorkflows) assert.match(workflow, /^\.github\/workflows\/[a-z0-9-]+\.yml$/);
  assert.deepEqual(approval.workflowExceptions, [],
    'The refreshed candidate must not inherit a historical failure as a current check exception.');
  assert.deepEqual(approval.workflowPathOmissions, workflowPathOmissions,
    'A path-filtered workflow omission must be explicit and cryptographically bound.');
  assert.deepEqual(approval.workflowDispatchChecks, workflowDispatchChecks,
    'Workflow-dispatch evidence must be exact, reviewed, and candidate-bound.');
  assert.deepEqual(approval.workflowDispatchNonRequiredEvidence, workflowDispatchNonRequiredEvidence,
    'Non-required workflow-dispatch evidence must be exact and immutable.');
  assert.deepEqual(approval.historicalCandidateEvidence, {
    pullRequest: 1037,
    branch: 'fix/flowhive-planner-live-capacity-repair-20260915',
    sha: '3c1230e422b55a746a2dde2b534ec94c9dd6678b',
    mergeCommit: '1d11b029ca7c63551af71a098b7a7f4618ce290b',
    sourceBase: '34a0f8fbbe7e79447cdf022d55ba8fb1bdf9c987',
    requiredWorkflows: historicalCandidateRequiredWorkflows,
    workflowPathOmissions: historicalCandidatePathOmissions
  }, 'Historical PR1037 evidence must remain immutable and explicit.');
  assert.equal(approval.projectId, '0ea25cb8-1a7f-4baf-ba7b-2dd76215be49');
  assert.equal(approval.projectManagerLogin, 'heather.schrock@ussignal.local');
  if (approval.successorCheckBinding) verifySupersededCheckBinding(approval.successorCheckBinding);
}

export function verifyPullRequest(approval, pr) {
  assert.equal(pr.number, approval.pullRequest);
  assert.equal(pr.state, 'closed');
  assert.equal(pr.merged, true);
  assert.equal(pr.head?.repo?.full_name, repository, 'Fork candidates are not authorized.');
  assert.equal(pr.base?.repo?.full_name, repository);
  assert.equal(pr.base?.ref, 'main');
  assert.equal(pr.head?.ref, approval.sourceBranch);
  assert.equal(pr.head?.sha, approval.sha, 'The approved candidate is no longer the PR head.');
  assert.equal(pr.merge_commit_sha, approval.mergeCommit, 'The reviewed merge commit changed.');
}

export function verifyHistoricalWorkflowException(exception, run, approval) {
  assert.deepEqual(exception, historicalWorkflowExceptions[0],
    'Only the reviewed historical controller failure may be superseded.');
  assert.equal(run.id, exception.candidateRunId, 'Historical exception run identity changed.');
  assert.equal(run.run_attempt, exception.candidateRunAttempt, 'Historical exception attempt changed.');
  assert.equal(run.head_sha, exception.candidateSha, 'Historical exception candidate changed.');
  assert.equal(run.event, exception.event, 'Historical exception event changed.');
  assert.equal(run.conclusion, exception.conclusion, 'Historical exception conclusion changed.');
  assert.equal(run.path.split('@')[0], exception.workflow, 'Historical exception workflow changed.');
  assert.equal(run.head_sha, approval.sha, 'Historical exception is not attached to the approved candidate.');
}

export function verifyWorkflowDispatchCheck(binding, run, approval) {
  assert.deepEqual(binding, workflowDispatchChecks.find(item => item.workflow === binding?.workflow),
    'The workflow-dispatch binding must match the reviewed exact-SHA evidence.');
  assert.equal(run.id, binding.runId, 'The workflow-dispatch run identity changed.');
  assert.equal(run.run_attempt, binding.runAttempt, 'The workflow-dispatch attempt changed.');
  assert.equal(run.event, binding.event, 'The workflow-dispatch event changed.');
  assert.equal(run.head_sha, binding.headSha, 'The workflow-dispatch candidate changed.');
  assert.equal(run.head_sha, approval.sha, 'The workflow-dispatch run is not attached to the approved candidate.');
  assert.equal(run.head_branch, binding.headBranch, 'The workflow-dispatch branch changed.');
  assert.equal(String(run.path || '').split('@')[0], binding.workflow, 'The workflow-dispatch workflow changed.');
  assert.equal(run.status, 'completed', 'The workflow-dispatch check has not finished.');
  assert.equal(run.conclusion, binding.conclusion, 'The workflow-dispatch check did not pass.');
  return run;
}

export function verifyWorkflowDispatchNonRequiredEvidence(evidence, run, approval) {
  assert.deepEqual(evidence, workflowDispatchNonRequiredEvidence.find(item => item.workflow === evidence?.workflow),
    'Non-required workflow-dispatch evidence must match the reviewed exact-run record.');
  if (run.head_sha === approval.sha) {
    assert.ok(!approval.requiredWorkflows.includes(evidence.workflow),
      'Current-candidate non-required workflow-dispatch evidence cannot remove an applicable required check.');
  } else {
    assert.equal(evidence.disposition, 'non-required-evidence-only',
      'Superseded workflow-dispatch evidence must remain explicitly non-authorizing.');
  }
  assert.equal(evidence.satisfiesRequiredCheck, false,
    'Non-required workflow-dispatch evidence cannot satisfy a required check.');
  assert.equal(evidence.deploymentEligible, false,
    'Non-required workflow-dispatch evidence cannot authorize deployment.');
  assert.equal(run.id, evidence.runId, 'Non-required workflow-dispatch run identity changed.');
  assert.equal(run.run_attempt, evidence.runAttempt, 'Non-required workflow-dispatch attempt changed.');
  assert.equal(run.event, evidence.event, 'Non-required workflow-dispatch event changed.');
  assert.equal(run.head_sha, evidence.headSha, 'Non-required workflow-dispatch candidate changed.');
  if (run.head_sha !== approval.sha) {
    assert.equal(evidence.disposition, 'non-required-evidence-only',
      'Historical non-required evidence must remain explicitly non-authorizing.');
    assert.notEqual(evidence.headSha, approval.sha,
      'Historical non-required evidence must not be presented as current-candidate evidence.');
  } else {
    assert.equal(run.head_sha, approval.sha,
      'Non-required workflow-dispatch run is not attached to the approved candidate.');
  }
  assert.equal(run.head_branch, evidence.headBranch, 'Non-required workflow-dispatch branch changed.');
  assert.equal(String(run.path || '').split('@')[0], evidence.workflow,
    'Non-required workflow-dispatch workflow changed.');
  assert.equal(run.status, evidence.status, 'Non-required workflow-dispatch run is not terminal.');
  assert.equal(run.conclusion, evidence.conclusion,
    'Non-required workflow-dispatch evidence must remain a recorded failure.');
  return run;
}

export function verifyRuns(approval, runs, allowedMissing = []) {
  const exceptions = new Map((approval.workflowExceptions || []).map(exception => [exception.workflow, exception]));
  const dispatchBindings = new Map((approval.workflowDispatchChecks || []).map(binding => [binding.workflow, binding]));
  const dispatchNonRequiredEvidence = new Map((approval.workflowDispatchNonRequiredEvidence || []).map(evidence => [evidence.workflow, evidence]));
  const latest = new Map();
  const dispatchRuns = new Map();
  for (const run of runs) {
    if (run.head_sha !== approval.sha) continue;
    const workflow = String(run.path || '').split('@')[0];
    if (run.event === 'workflow_dispatch') {
      assert.equal(run.head_repository?.full_name, repository, 'CI must run against the same repository.');
      const prior = dispatchRuns.get(workflow);
      if (!prior || Number(run.id) > Number(prior.id) ||
        (run.id === prior.id && Number(run.run_attempt) > Number(prior.run_attempt))) dispatchRuns.set(workflow, run);
      continue;
    }
    if (run.event !== 'pull_request') continue;
    assert.equal(run.head_repository?.full_name, repository, 'CI must run against the same repository.');
    const prior = latest.get(workflow);
    if (!prior || Number(run.id) > Number(prior.id) ||
      (run.id === prior.id && Number(run.run_attempt) > Number(prior.run_attempt))) latest.set(workflow, run);
  }
  for (const workflow of approval.requiredWorkflows) {
    const run = latest.get(workflow);
    if (!run) {
      const dispatchBinding = dispatchBindings.get(workflow);
      if (dispatchBinding) {
        const dispatchRun = dispatchRuns.get(workflow);
        assert.ok(dispatchRun, `The exact workflow-dispatch check is missing: ${workflow}`);
        verifyWorkflowDispatchCheck(dispatchBinding, dispatchRun, approval);
        continue;
      }
      assert.ok(allowedMissing.includes(workflow), `Required exact-SHA CI is missing: ${workflow}`);
      continue;
    }
    assert.equal(run.status, 'completed', `Required CI has not finished: ${workflow}`);
    assert.equal(run.conclusion, 'success', `Required CI did not pass: ${workflow}`);
  }
  for (const exception of approval.workflowExceptions || []) {
    const run = latest.get(exception.workflow);
    assert.ok(run, `The exact historical exception run is missing: ${exception.workflow}`);
    verifyHistoricalWorkflowException(exception, run, approval);
  }
  for (const [workflow, run] of latest) {
    if (exceptions.has(workflow)) {
      verifyHistoricalWorkflowException(exceptions.get(workflow), run, approval);
      continue;
    }
    assert.equal(run.status, 'completed', `Another candidate check is still active: ${workflow}`);
    assert.ok(run.conclusion === 'success' || run.conclusion === 'skipped', `Candidate CI failed: ${workflow}`);
  }
  for (const [workflow, run] of dispatchRuns) {
    const binding = dispatchBindings.get(workflow);
    if (binding) verifyWorkflowDispatchCheck(binding, run, approval);
    else {
      const evidence = dispatchNonRequiredEvidence.get(workflow);
      assert.ok(evidence, `Unbound workflow-dispatch run is not admissible: ${workflow}`);
      verifyWorkflowDispatchNonRequiredEvidence(evidence, run, approval);
    }
  }
  return [
    ...latest.values(),
    ...[...dispatchRuns.entries()]
      .filter(([workflow]) => dispatchBindings.has(workflow))
      .map(([, run]) => run)
  ].map(run => ({ path: run.path, runId: run.id, attempt: run.run_attempt, conclusion: run.conclusion }));
}

function digestLines(lines) {
  return crypto.createHash('sha256').update(`${[...lines].sort().join('\n')}\n`).digest('hex');
}

function workflowPathMatches(pattern, filename) {
  const expression = `^${pattern.replace(/[.+?^${}()|[\]\\]/g, '\\$&').replace(/\*\*/g, '.*').replace(/\*/g, '[^/]*')}$`;
  return new RegExp(expression).test(filename);
}

export function verifyWorkflowException(approval, pullRequest, changedFiles, baseWorkflowContent) {
  if ((approval.workflowExceptions || []).length === 0) return [];
  const [exception] = approval.workflowExceptions || [];
  assert.ok(exception, 'The candidate must explain every missing required workflow.');
  if (exception.reasonCode === 'historical-head-ref-binding-defect') {
    assert.deepEqual(approval.workflowExceptions, historicalWorkflowExceptions,
      'The historical controller exception must remain exact and singular.');
    return [];
  }
  assert.equal(exception.workflow, '.github/workflows/module025-governed-protected-test-release-ci.yml');
  assert.equal(exception.reasonCode, 'pull-request-path-filter-no-match');
  assert.equal(exception.baseCommit, pullRequest.base?.sha, 'Path-filter evidence must bind to the actual PR base.');
  assert.equal(crypto.createHash('sha256').update(baseWorkflowContent).digest('hex'), exception.baseWorkflowSha256,
    'Path-filter evidence must bind to the actual base workflow bytes.');
  assert.equal(digestLines(changedFiles), exception.candidateChangedFilesSha256,
    'Path-filter evidence must bind to the actual candidate file inventory.');
  assert.equal(changedFiles.length, exception.candidateChangedFilesCount);
  const pathsBlock = baseWorkflowContent.match(/\n\s+paths:\s*\n([\s\S]*?)\n\s+permissions:/);
  assert.ok(pathsBlock, 'The base Module 025 workflow must expose a pull-request path filter.');
  const pathFilters = [...pathsBlock[1].matchAll(/^\s*-\s*["']([^"']+)["']\s*$/gm)].map(match => match[1]);
  assert.ok(pathFilters.length > 0, 'The base Module 025 path filter must not be empty.');
  assert.ok(changedFiles.every(file => !pathFilters.some(pattern => workflowPathMatches(pattern, file))),
    'A missing workflow may be excepted only when no candidate file matches its base path filter.');
  return [exception.workflow];
}

export function verifyWorkflowPathOmission(omission, pullRequest, changedFiles, baseWorkflowContent) {
  const required = new Set(requiredWorkflows);
  assert.ok(!required.has(omission.workflow), 'A path-filtered workflow cannot also be required.');
  assert.equal(omission.reasonCode, 'pull-request-path-filter-no-match');
  assert.equal(omission.baseCommit, pullRequest.base?.sha,
    'Path-filter omission must bind to the actual candidate base.');
  assert.equal(crypto.createHash('sha256').update(baseWorkflowContent).digest('hex'), omission.baseWorkflowSha256,
    'Path-filter omission must bind to the actual base workflow bytes.');
  assert.equal(digestLines(changedFiles), omission.candidateChangedFilesSha256,
    'Path-filter omission must bind to the actual candidate file inventory.');
  assert.equal(changedFiles.length, omission.candidateChangedFilesCount);
  const pathsBlock = baseWorkflowContent.match(/\n[ \t]+paths:\s*\n([\s\S]*?)\n[ \t]*(?:workflow_dispatch:|permissions:)/);
  assert.ok(pathsBlock, 'The omitted workflow must expose a pull-request path filter.');
  const pathFilters = [...pathsBlock[1].matchAll(/^\s*-\s*["']([^"']+)["']\s*$/gm)].map(match => match[1]);
  assert.ok(pathFilters.length > 0, 'The omitted workflow path filter must not be empty.');
  assert.ok(changedFiles.every(file => !pathFilters.some(pattern => workflowPathMatches(pattern, file))),
    'A workflow may be omitted only when no candidate file matches its pull-request path filter.');
  return omission.workflow;
}

export function verifyWorkflowPathOmissions(omissions, pullRequest, changedFiles, baseWorkflowContent) {
  assert.deepEqual(omissions, workflowPathOmissions,
    'The candidate must use the reviewed path-filter omission set exactly.');
  return omissions.map(omission => {
    const content = typeof baseWorkflowContent === 'function'
      ? baseWorkflowContent(omission)
      : baseWorkflowContent instanceof Map
        ? baseWorkflowContent.get(omission.workflow)
        : baseWorkflowContent;
    assert.equal(typeof content, 'string', `Missing base workflow bytes for ${omission.workflow}.`);
    return verifyWorkflowPathOmission(omission, pullRequest, changedFiles, content);
  });
}

export function verifySourceDrift(changed, allowed) {
  assert.deepEqual(allowed, [...new Set(allowed)].sort(), 'The control-only manifest must be sorted and unique.');
  const permitted = new Set(allowed);
  for (const name of changed) assert.ok(permitted.has(name), `Main has a source change absent from this candidate: ${name}`);
}

export function verifyTargetReleaseBranch(branch) {
  if (branch === undefined || branch === '') return;
  assert.ok(authorizedReleaseBranches.includes(branch),
    `The PSA release branch must be one of: ${authorizedReleaseBranches.join(', ')}`);
}

export function verifyCandidateCommitObject(approval, git = (...args) =>
  execFileSync('git', args, { encoding: 'utf8', timeout: 30000 }).trim()) {
  assert.match(approval?.sha || '', sha, 'The approved candidate SHA is malformed.');
  const resolved = git('rev-parse', '--verify', `${approval.sha}^{commit}`);
  assert.equal(resolved, approval.sha,
    'The approved candidate commit must be present in the executing trusted-main checkout.');
  return resolved;
}

async function github(resource) {
  assert.ok(resource.startsWith(`/repos/${repository}/`));
  const response = await fetch(`https://api.github.com${resource}`, {
    headers: { Authorization: `Bearer ${process.env.GH_TOKEN}`, Accept: 'application/vnd.github+json',
      'X-GitHub-Api-Version': '2022-11-28' },
    redirect: 'error', signal: AbortSignal.timeout(30000)
  });
  if (!response.ok) throw new Error(`GitHub admission read failed: HTTP ${response.status}`);
  return response.json();
}

export async function authorize() {
  assert.equal(process.env.GITHUB_REPOSITORY, repository);
  assert.equal(process.env.GITHUB_REF, 'refs/heads/main', 'Only the trusted main controller can admit a candidate.');
  assert.ok(['workflow_dispatch', 'issue_comment'].includes(process.env.GITHUB_EVENT_NAME));
  assert.ok(process.env.GH_TOKEN, 'The read-only admission token is required.');
  const approval = JSON.parse(fs.readFileSync(approvalPath, 'utf8'));
  verifyApproval(approval, process.env.TARGET_RELEASE_COMMIT);
  verifyTargetReleaseBranch(process.env.TARGET_RELEASE_BRANCH);
  assert.notEqual(process.env.RECOVER_PRIVATE_RUNTIME, 'true', 'Private runtime recovery is not part of this candidate approval.');
  const main = await github(`/repos/${repository}/git/ref/heads/main`);
  assert.match(main.object?.sha || '', sha, 'The current main response is missing or malformed.');
  assert.match(process.env.GITHUB_SHA || '', sha, 'The executing trusted controller SHA is missing or malformed.');
  assert.equal(main.object.sha, process.env.GITHUB_SHA, 'The trusted main controller is no longer current.');
  const pr = await github(`/repos/${repository}/pulls/${candidatePullRequest}`);
  verifyPullRequest(approval, pr);
  const git = (...args) => execFileSync('git', args, { encoding: 'utf8', timeout: 30000 }).trim();
  // Normal PR merges delete the source branch. The PR API's retained head
  // identity plus this checked-out commit-object check bind the candidate
  // without requiring a mutable branch ref to survive the merge.
  verifyCandidateCommitObject(approval, git);
  for (const exception of approval.workflowExceptions || []) {
    const source = git('show', `HEAD:${exception.workflow}`);
    assert.match(source, /PR_HEAD_REF: \$\{\{ github\.head_ref \}\}/,
      'Historical controller source must bind the pull-request head explicitly.');
    assert.match(source, /HEAD_BRANCH="\$\{PR_HEAD_REF:-\$\{GITHUB_HEAD_REF:-\$\{GITHUB_REF_NAME:-\}\}\}"/,
      'Historical controller must use the explicit pull-request head binding.');
    assert.match(source, /control\/module025-sow-role-candidate-refresh-20260914/,
      'The corrected controller must retain an exact governed scope path.');
  }
  const fileResponse = await github(`/repos/${repository}/pulls/${candidatePullRequest}/files?per_page=100`);
  assert.ok(Array.isArray(fileResponse) && fileResponse.length > 0, 'The candidate file inventory is missing.');
  const candidateFiles = fileResponse.map(file => file.filename).sort();
  verifyWorkflowPathOmissions(approval.workflowPathOmissions, pr, candidateFiles, omission =>
    execFileSync('git', ['show', `${pr.base.sha}:${omission.workflow}`], { encoding: 'utf8', timeout: 30000 }));
  const workflowExceptions = verifyWorkflowException(approval, pr, candidateFiles,
    approval.workflowExceptions.length > 0
      ? execFileSync('git', ['show', `${pr.base.sha}:${approval.workflowExceptions[0].workflow}`], { encoding: 'utf8', timeout: 30000 })
      : '');
  const runs = [];
  for (let page = 1; page <= 10; page++) {
    const result = await github(`/repos/${repository}/actions/runs?head_sha=${approval.sha}&event=pull_request&per_page=100&page=${page}`);
    assert.ok(Array.isArray(result.workflow_runs));
    runs.push(...result.workflow_runs);
    if (result.workflow_runs.length < 100) break;
    assert.ok(page < 10, 'CI pagination exceeded the bounded admission limit.');
  }
  for (let page = 1; page <= 10; page++) {
    const result = await github(`/repos/${repository}/actions/runs?head_sha=${approval.sha}&event=workflow_dispatch&per_page=100&page=${page}`);
    assert.ok(Array.isArray(result.workflow_runs));
    runs.push(...result.workflow_runs);
    if (result.workflow_runs.length < 100) break;
    assert.ok(page < 10, 'Workflow-dispatch CI pagination exceeded the bounded admission limit.');
  }
  const checks = verifyRuns(approval, runs, workflowExceptions);
  assert.equal(git('rev-parse', 'HEAD'), main.object.sha);
  // sourceBase proves the candidate was built from the reviewed application
  // base. The merge commit is the narrower boundary for current-main drift:
  // it already contains the approved application, so only later control
  // changes may be admitted without another application approval.
  git('merge-base', '--is-ancestor', approval.sourceBase, approval.sha);
  git('merge-base', '--is-ancestor', approval.mergeCommit, main.object.sha);
  const controlFiles = fs.readFileSync(controlManifest, 'utf8').trim().split(/\r?\n/);
  const mainChanges = git('diff', '--name-only', `${approval.mergeCommit}..${main.object.sha}`).split(/\r?\n/).filter(Boolean);
  verifySourceDrift(mainChanges, controlFiles);
  if (process.env.GITHUB_OUTPUT) fs.appendFileSync(process.env.GITHUB_OUTPUT, `authorized=true\nrelease_sha=${approval.sha}\n`);
  console.log(`FLOWHIVE_PSA_RELEASE_ADMISSION=PASS sha=${approval.sha} checks=${checks.length} productionMutation=false`);
  return { approval, checks };
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  authorize().catch(error => { console.error(`FLOWHIVE_PSA_RELEASE_ADMISSION=FAIL ${error.message}`); process.exitCode = 1; });
}
