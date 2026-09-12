import assert from 'node:assert/strict';
import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

export const repository = 'ahmedadeyemi-cts/project-time-platform';
// PR887 remains the maintained release-coordination thread. The selected
// successor is the reviewed, merged live FlowHive planner correction PR958.
export const admissionIssueNumber = 887;
export const candidatePullRequest = 958;
export const candidateBranch = 'fix/flowhive-planner-output-budget-20260912';
export const candidateSourceBranch = 'fix/flowhive-planner-output-budget-20260912';
// The deployment controller intentionally checks out trusted main, while its
// protected PSA lane is named independently from the candidate source branch.
// Keep both values explicit and bounded; arbitrary workflow-dispatch refs are
// never valid for this admission path.
export const protectedTestReleaseLane = 'release/flowhive-sow-successor-20260908';
export const authorizedReleaseBranches = Object.freeze([candidateBranch, protectedTestReleaseLane]);
export const candidateMergeCommit = 'ed75fc0223c5967296d932fa7f46ac6790081020';
export const controlBranch = 'release/flowhive-psa-protected-test-admission-20260906';
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
  '.github/workflows/celar-ai-production-hardening-ci.yml',
  '.github/workflows/projectpulse-ci.yml',
  '.github/workflows/security-posture-ci.yml',
  '.github/workflows/systemwide-enterprise-reliability-ci.yml',
  '.github/workflows/pulse-ai-system-intelligence-ci.yml',
  '.github/workflows/celar-ai-runtime-rebrand-ci.yml'
];

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
  for (const workflow of approval.requiredWorkflows) assert.match(workflow, /^\.github\/workflows\/[a-z0-9-]+\.yml$/);
  assert.deepEqual(approval.workflowExceptions, [], 'The successor approval must not retain an obsolete missing-workflow exception.');
  assert.equal(approval.projectId, '0ea25cb8-1a7f-4baf-ba7b-2dd76215be49');
  assert.equal(approval.projectManagerLogin, 'heather.schrock@ussignal.local');
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

export function verifyRuns(approval, runs, allowedMissing = []) {
  const latest = new Map();
  for (const run of runs) {
    if (run.head_sha !== approval.sha || run.event !== 'pull_request') continue;
    assert.equal(run.head_repository?.full_name, repository, 'CI must run against the same repository.');
    const workflow = String(run.path || '').split('@')[0];
    const prior = latest.get(workflow);
    if (!prior || Number(run.id) > Number(prior.id) ||
      (run.id === prior.id && Number(run.run_attempt) > Number(prior.run_attempt))) latest.set(workflow, run);
  }
  for (const workflow of approval.requiredWorkflows) {
    const run = latest.get(workflow);
    if (!run) {
      assert.ok(allowedMissing.includes(workflow), `Required exact-SHA CI is missing: ${workflow}`);
      continue;
    }
    assert.equal(run.status, 'completed', `Required CI has not finished: ${workflow}`);
    assert.equal(run.conclusion, 'success', `Required CI did not pass: ${workflow}`);
  }
  for (const [workflow, run] of latest) {
    assert.equal(run.status, 'completed', `Another candidate check is still active: ${workflow}`);
    assert.ok(run.conclusion === 'success' || run.conclusion === 'skipped', `Candidate CI failed: ${workflow}`);
  }
  return [...latest.values()].map(run => ({ path: run.path, runId: run.id, attempt: run.run_attempt, conclusion: run.conclusion }));
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
  const branch = await github(`/repos/${repository}/git/ref/heads/${candidateBranch}`);
  assert.equal(branch.object.sha, approval.sha);
  const git = (...args) => execFileSync('git', args, { encoding: 'utf8', timeout: 30000 }).trim();
  const fileResponse = await github(`/repos/${repository}/pulls/${candidatePullRequest}/files?per_page=100`);
  assert.ok(Array.isArray(fileResponse) && fileResponse.length > 0, 'The candidate file inventory is missing.');
  const candidateFiles = fileResponse.map(file => file.filename).sort();
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
  const checks = verifyRuns(approval, runs, workflowExceptions);
  assert.equal(git('rev-parse', 'HEAD'), main.object.sha);
  git('fetch', '--no-tags', 'origin', candidateBranch);
  // The successor application was merged into the reviewed main snapshot.
  // The later control merge is then constrained to control-only drift from
  // that snapshot, without widening the allowlist for application files.
  git('merge-base', '--is-ancestor', approval.sha, approval.sourceBase);
  git('merge-base', '--is-ancestor', approval.sourceBase, main.object.sha);
  const controlFiles = fs.readFileSync(controlManifest, 'utf8').trim().split(/\r?\n/);
  const mainChanges = git('diff', '--name-only', `${approval.sourceBase}..${main.object.sha}`).split(/\r?\n/).filter(Boolean);
  verifySourceDrift(mainChanges, controlFiles);
  if (process.env.GITHUB_OUTPUT) fs.appendFileSync(process.env.GITHUB_OUTPUT, `authorized=true\nrelease_sha=${approval.sha}\n`);
  console.log(`FLOWHIVE_PSA_RELEASE_ADMISSION=PASS sha=${approval.sha} checks=${checks.length} productionMutation=false`);
  return { approval, checks };
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  authorize().catch(error => { console.error(`FLOWHIVE_PSA_RELEASE_ADMISSION=FAIL ${error.message}`); process.exitCode = 1; });
}
