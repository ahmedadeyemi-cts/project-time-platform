import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

export const repository = 'ahmedadeyemi-cts/project-time-platform';
export const candidateBranch = 'feature/flowhive-enterprise-psa-revamp-20260906';
export const controlBranch = 'release/flowhive-psa-protected-test-admission-20260906';
export const approvalPath = '.github/flowhive-psa-protected-test-candidate.json';
export const controlManifest = '.github/flowhive-psa-release-control-files.txt';
export const origin = 'https://phd-west-test.onenecklab.com';
const sha = /^[a-f0-9]{40}$/;
const hash = /^[a-f0-9]{64}$/;
const migrations = [
  '103_module_066_flowhive_enterprise_psa_revamp.sql',
  '104_flowhive_bounded_ai_execution.sql'
];

export function verifyApproval(approval, requestedSha) {
  assert.equal(approval.contract, 'flowhive-psa-protected-test-candidate-v1');
  assert.equal(approval.repository, repository);
  assert.equal(approval.pullRequest, 872);
  assert.equal(approval.branch, candidateBranch);
  assert.equal(approval.environment, 'test');
  assert.equal(approval.publicOrigin, origin);
  assert.equal(approval.allowPrivateRuntimeMutation, false);
  assert.equal(approval.allowCustomerPublication, false);
  assert.equal(approval.allowCanonicalTaskAdoption, false);
  assert.match(approval.sha, sha);
  assert.match(approval.sourceBase, sha);
  assert.equal(requestedSha, approval.sha, 'The candidate must be explicitly pinned in reviewed main-branch approval.');
  assert.deepEqual(approval.migrations.map(x => x.file), migrations);
  for (const item of approval.migrations) assert.match(item.sha256, hash);
  assert.ok(Array.isArray(approval.requiredWorkflows) && approval.requiredWorkflows.length >= 21);
  assert.equal(new Set(approval.requiredWorkflows).size, approval.requiredWorkflows.length);
  for (const workflow of approval.requiredWorkflows) assert.match(workflow, /^\.github\/workflows\/[a-z0-9-]+\.yml$/);
  assert.equal(approval.projectId, '0ea25cb8-1a7f-4baf-ba7b-2dd76215be49');
  assert.equal(approval.projectManagerLogin, 'heather.schrock@ussignal.local');
}

export function verifyPullRequest(approval, pr) {
  assert.equal(pr.number, approval.pullRequest);
  assert.equal(pr.state, 'open');
  assert.equal(pr.merged, false);
  assert.equal(pr.head?.repo?.full_name, repository, 'Fork candidates are not authorized.');
  assert.equal(pr.base?.repo?.full_name, repository);
  assert.equal(pr.base?.ref, 'main');
  assert.equal(pr.head?.ref, candidateBranch);
  assert.equal(pr.head?.sha, approval.sha, 'The approved candidate is no longer the PR head.');
  // Draft is deliberately allowed for pre-merge acceptance. Nothing here merges the feature.
}

export function verifyRuns(approval, runs) {
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
    assert.ok(run, `Required exact-SHA CI is missing: ${workflow}`);
    assert.equal(run.status, 'completed', `Required CI has not finished: ${workflow}`);
    assert.equal(run.conclusion, 'success', `Required CI did not pass: ${workflow}`);
  }
  for (const [workflow, run] of latest) {
    assert.equal(run.status, 'completed', `Another candidate check is still active: ${workflow}`);
    assert.ok(run.conclusion === 'success' || run.conclusion === 'skipped', `Candidate CI failed: ${workflow}`);
  }
  return [...latest.values()].map(run => ({ path: run.path, runId: run.id, attempt: run.run_attempt, conclusion: run.conclusion }));
}

export function verifySourceDrift(changed, allowed) {
  assert.deepEqual(allowed, [...new Set(allowed)].sort(), 'The control-only manifest must be sorted and unique.');
  const permitted = new Set(allowed);
  for (const name of changed) assert.ok(permitted.has(name), `Main has a source change absent from this candidate: ${name}`);
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
  if (process.env.TARGET_RELEASE_BRANCH) assert.equal(process.env.TARGET_RELEASE_BRANCH, candidateBranch);
  assert.notEqual(process.env.RECOVER_PRIVATE_RUNTIME, 'true', 'Private runtime recovery is not part of this candidate approval.');
  const main = await github(`/repos/${repository}/git/ref/heads/main`);
  assert.equal(main.object.sha, process.env.GITHUB_SHA, 'The trusted main controller is no longer current.');
  const pr = await github(`/repos/${repository}/pulls/872`);
  verifyPullRequest(approval, pr);
  const branch = await github(`/repos/${repository}/git/ref/heads/${candidateBranch}`);
  assert.equal(branch.object.sha, approval.sha);
  const runs = [];
  for (let page = 1; page <= 10; page++) {
    const result = await github(`/repos/${repository}/actions/runs?head_sha=${approval.sha}&event=pull_request&per_page=100&page=${page}`);
    assert.ok(Array.isArray(result.workflow_runs));
    runs.push(...result.workflow_runs);
    if (result.workflow_runs.length < 100) break;
    assert.ok(page < 10, 'CI pagination exceeded the bounded admission limit.');
  }
  const checks = verifyRuns(approval, runs);
  const git = (...args) => execFileSync('git', args, { encoding: 'utf8', timeout: 30000 }).trim();
  assert.equal(git('rev-parse', 'HEAD'), main.object.sha);
  git('fetch', '--no-tags', 'origin', candidateBranch);
  git('merge-base', '--is-ancestor', approval.sourceBase, approval.sha);
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
