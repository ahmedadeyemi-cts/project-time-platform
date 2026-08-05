#!/usr/bin/env node

import { execFileSync } from 'node:child_process';
import { existsSync, readFileSync, readdirSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const VALIDATOR_PATH = 'scripts/validate-deployment-concurrency-governance.mjs';
const WORKFLOW_PATH = '.github/workflows/deployment-concurrency-governance-ci.yml';
const SENTINEL_PATH = '.github/deployment-concurrency-governance-v1.enabled';
const SENTINEL_CONTENT = 'version=1\n';
const TRUSTED_TEMP_NAME = 'trusted-deployment-concurrency-validator.mjs';

const scopeWorkflowPaths = new Set([
  '.github/workflows/validate-admin-experience-008-009-test-deployment.yml',
  '.github/workflows/validate-dynamic-rbac-group2a-test-deployment.yml',
  '.github/workflows/validate-global-session-invalidation-test-deployment.yml',
  '.github/workflows/validate-group2b-group3-module064-foundation-test-deployment.yml',
  '.github/workflows/validate-group4-test-deployment-controls.yml',
  '.github/workflows/validate-module-008-audit-recovery-test-deployment.yml',
  '.github/workflows/validate-module001-ptc-timer-dom-module026-test-deployment.yml',
  '.github/workflows/validate-open-pr-reconciliation-test-deployment.yml',
  '.github/workflows/validate-security-admin-repair-test-deployment.yml',
  '.github/workflows/validate-superadmin-sso-expense-module006-test-deployment.yml',
  '.github/workflows/validate-uat-sso-group3-label-repair-test-deployment.yml',
  '.github/workflows/validate-view-as-drawer-test-deployment.yml',
]);

const contractScriptPaths = new Set([
  'scripts/validate-admin-experience-008-009-test-deployment.sh',
  'scripts/validate-dynamic-rbac-group2a-test-deployment.sh',
  'scripts/validate-global-session-invalidation-test-deployment.sh',
  'scripts/validate-module001-ptc-timer-dom-module026-test-deployment.sh',
  'scripts/validate-open-pr-reconciliation-test-deployment.sh',
  'scripts/validate-security-admin-repair-test-deployment.sh',
  'scripts/validate-superadmin-sso-expense-module006-test-deployment.sh',
  'scripts/validate-view-as-drawer-test-deployment.sh',
]);

const rootScopeBlock = `          # DEPLOYMENT_CONCURRENCY_GOVERNANCE_SCOPE_START
          git show origin/main:${VALIDATOR_PATH} > "\${RUNNER_TEMP}/${TRUSTED_TEMP_NAME}"
          set +e
          node "\${RUNNER_TEMP}/${TRUSTED_TEMP_NAME}" --repo-root "\${GITHUB_WORKSPACE}" --base-ref origin/main --classify-bootstrap-scope
          governance_status=$?
          set -e
          if [[ $governance_status -eq 0 ]]; then
            echo 'DEPLOYMENT_CONCURRENCY_GOVERNANCE_SCOPE=PASS'
            exit 0
          fi
          if [[ $governance_status -ne 2 ]]; then
            exit "$governance_status"
          fi
          # DEPLOYMENT_CONCURRENCY_GOVERNANCE_SCOPE_END
`;

const controlScopeBlock = `          # DEPLOYMENT_CONCURRENCY_GOVERNANCE_SCOPE_START
          git -C control show origin/main:${VALIDATOR_PATH} > "\${RUNNER_TEMP}/${TRUSTED_TEMP_NAME}"
          set +e
          node "\${RUNNER_TEMP}/${TRUSTED_TEMP_NAME}" --repo-root "\${GITHUB_WORKSPACE}/control" --base-ref origin/main --classify-bootstrap-scope
          governance_status=$?
          set -e
          if [[ $governance_status -eq 0 ]]; then
            echo 'DEPLOYMENT_CONCURRENCY_GOVERNANCE_SCOPE=PASS'
            exit 0
          fi
          if [[ $governance_status -ne 2 ]]; then
            exit "$governance_status"
          fi
          # DEPLOYMENT_CONCURRENCY_GOVERNANCE_SCOPE_END
`;

const argument = (name) => {
  const index = process.argv.indexOf(name);
  return index >= 0 ? process.argv[index + 1] : null;
};
const defaultRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const repoRoot = resolve(argument('--repo-root') ?? defaultRoot);
const atRoot = (path) => join(repoRoot, path);
const failures = [];

function fail(message) {
  failures.push(message);
}

function git(args, { allowFailure = false } = {}) {
  try {
    return execFileSync('git', args, { cwd: repoRoot, encoding: 'utf8' });
  } catch (error) {
    if (allowFailure) return null;
    throw error;
  }
}

function baseRef() {
  const value = argument('--base-ref');
  if (!value || value.startsWith('-')) throw new Error('--base-ref requires an exact Git ref');
  return value;
}

function changedPaths(base) {
  return git(['diff', '--name-only', `${base}...HEAD`])
    .trim()
    .split('\n')
    .filter(Boolean)
    .sort();
}

function fileAt(ref, path) {
  return git(['show', `${ref}:${path}`], { allowFailure: true });
}

function current(path) {
  return readFileSync(atRoot(path), 'utf8');
}

function environmentClassesFor(source) {
  const classes = new Set();
  if (/^\s*environment:\s*test\s*$/m.test(source)) classes.add('test');
  if (/^\s*environment:\s*production\s*$/m.test(source)) classes.add('production');
  if (/^\s*environment:\s*\$\{\{\s*inputs\.environment\s*\}\}\s*$/m.test(source)) classes.add('dynamic');
  return classes;
}

function jobBlocks(source) {
  const lines = source.split('\n');
  const jobsIndex = lines.findIndex((line) => line === 'jobs:');
  if (jobsIndex < 0) return [];
  const blocks = [];
  let start = -1;
  for (let index = jobsIndex + 1; index <= lines.length; index += 1) {
    const startsJob = index < lines.length && /^  [A-Za-z0-9_-]+:\s*$/.test(lines[index]);
    if (!startsJob && index < lines.length) continue;
    if (start >= 0) blocks.push(lines.slice(start, index).join('\n'));
    start = startsJob ? index : -1;
  }
  return blocks;
}

function workflowFailures(name, source) {
  const issues = [];
  const usesAzureLogin = /uses:\s*azure\/login(?:@|\s)/.test(source);
  const environmentClasses = environmentClassesFor(source);

  for (const job of jobBlocks(source).filter((block) => /uses:\s*azure\/login(?:@|\s)/.test(block))) {
    if (environmentClassesFor(job).size !== 1) {
      issues.push(`${name}: every Azure-credentialed job must declare exactly one governed protected environment`);
    }
  }

  if (environmentClasses.size === 0) {
    if (usesAzureLogin) issues.push(`${name}: Azure login requires a governed protected environment`);
    return issues;
  }
  if (environmentClasses.size !== 1) {
    issues.push(`${name}: protected workflows must have exactly one environment class`);
    return issues;
  }

  const environmentClass = [...environmentClasses][0];
  const expectedGroup = environmentClass === 'test'
    ? 'projectpulse-deploy-test'
    : environmentClass === 'production'
      ? 'projectpulse-deploy-production'
      : 'projectpulse-deploy-${{ inputs.environment }}';
  const concurrency = source.match(/^concurrency:\s*\n((?:^[ \t]+.*\n?)*)/m)?.[1] ?? '';
  const actualGroup = concurrency.match(/^\s+group:\s*(.+?)\s*$/m)?.[1] ?? '';
  const queue = concurrency.match(/^\s+queue:\s*(.+?)\s*$/m)?.[1] ?? '';
  const cancel = concurrency.match(/^\s+cancel-in-progress:\s*(.+?)\s*$/m)?.[1] ?? '';
  if (actualGroup !== expectedGroup) issues.push(`${name}: expected concurrency group ${expectedGroup}`);
  if (queue !== 'max') issues.push(`${name}: protected releases must set queue: max`);
  if (cancel !== 'false') issues.push(`${name}: protected releases must set cancel-in-progress: false`);
  return issues;
}

function verifyRepository() {
  if (!existsSync(atRoot(SENTINEL_PATH)) || current(SENTINEL_PATH) !== SENTINEL_CONTENT) {
    fail(`${SENTINEL_PATH}: exact governance sentinel is required`);
  }
  const workflowDirectory = atRoot('.github/workflows');
  let governed = 0;
  for (const name of readdirSync(workflowDirectory).filter((item) => /\.ya?ml$/i.test(item)).sort()) {
    const source = readFileSync(join(workflowDirectory, name), 'utf8');
    if (environmentClassesFor(source).size > 0) governed += 1;
    workflowFailures(name, source).forEach(fail);
  }
  if (governed === 0) fail('No protected environment workflows were discovered');
}

function normalizeConcurrency(source) {
  const lines = source.split('\n');
  const index = lines.findIndex((line) => line === 'concurrency:');
  if (index < 0) return source;
  for (let cursor = index + 1; cursor < lines.length; cursor += 1) {
    if (lines[cursor] !== '' && !/^\s/.test(lines[cursor])) break;
    if (/^\s+group:/.test(lines[cursor])) lines[cursor] = '  group: __ENVIRONMENT_DEPLOYMENT_LOCK__';
    if (/^\s+queue:/.test(lines[cursor])) lines.splice(cursor--, 1);
  }
  return lines.join('\n');
}

function isDeploymentControl(path) {
  return path === '.github/workflows/projectpulse-deploy-test.yml'
    || /^\.github\/workflows\/projectpulse-deploy-.*-test\.yml$/.test(path)
    || path === '.github/workflows/projectpulse-deploy-production.yml'
    || path === '.github/workflows/projectpulse-rollback.yml'
    || path === '.github/workflows/projectpulse-run-group4-migration-050-test.yml';
}

function removeExactScopeBlock(source, expected, path) {
  const startCount = source.split('DEPLOYMENT_CONCURRENCY_GOVERNANCE_SCOPE_START').length - 1;
  const endCount = source.split('DEPLOYMENT_CONCURRENCY_GOVERNANCE_SCOPE_END').length - 1;
  if (startCount !== 1 || endCount !== 1 || !source.includes(expected)) {
    fail(`${path}: expected exactly one immutable trusted-validator scope block`);
    return source;
  }
  return source.replace(expected, '');
}

function verifySingleContractReplacement(path, before, after) {
  const beforeLines = before.split('\n');
  const afterLines = after.split('\n');
  if (beforeLines.length !== afterLines.length) {
    fail(`${path}: deployment contract update changed line count`);
    return;
  }
  const changed = beforeLines.map((line, index) => line === afterLines[index] ? -1 : index).filter((index) => index >= 0);
  if (changed.length !== 1) {
    fail(`${path}: expected exactly one deployment concurrency literal change`);
    return;
  }
  const index = changed[0];
  const replaced = beforeLines[index].replace(/projectpulse-(?:deploy|rollback|group4)[A-Za-z0-9_-]*/, 'projectpulse-deploy-test');
  if (afterLines[index] !== replaced || !afterLines[index].includes('projectpulse-deploy-test')) {
    fail(`${path}: only the old private lock may be replaced by projectpulse-deploy-test`);
  }
}

function verifyBootstrapPackage(base) {
  const changed = changedPaths(base);
  const changedSet = new Set(changed);
  if (fileAt(base, SENTINEL_PATH) !== null) fail(`${SENTINEL_PATH}: sentinel already exists in the base`);
  if (!changedSet.has(SENTINEL_PATH) || !existsSync(atRoot(SENTINEL_PATH)) || current(SENTINEL_PATH) !== SENTINEL_CONTENT) {
    fail(`${SENTINEL_PATH}: exact one-time sentinel addition is required`);
  }
  for (const protectedPath of [VALIDATOR_PATH, WORKFLOW_PATH]) {
    if (changedSet.has(protectedPath) || current(protectedPath) !== fileAt(base, protectedPath)) {
      fail(`${protectedPath}: trusted governance controls must remain byte-identical to the base`);
    }
  }
  for (const path of scopeWorkflowPaths) {
    if (!changedSet.has(path)) fail(`${path}: immutable compatibility scope block is required`);
  }
  for (const path of contractScriptPaths) {
    if (!changedSet.has(path)) fail(`${path}: shared-lock contract update is required`);
  }

  for (const path of changed) {
    if (path === SENTINEL_PATH) continue;
    const before = fileAt(base, path);
    if (before === null) {
      fail(`${path}: bootstrap package cannot add this path`);
      continue;
    }
    const after = current(path);
    if (isDeploymentControl(path)) {
      if (normalizeConcurrency(after) !== normalizeConcurrency(before)) {
        fail(`${path}: changed outside the top-level concurrency group/queue lines`);
      }
      continue;
    }
    if (scopeWorkflowPaths.has(path)) {
      const expected = before.includes('git -C control fetch origin main') ? controlScopeBlock : rootScopeBlock;
      if (removeExactScopeBlock(after, expected, path) !== before) {
        fail(`${path}: changed outside the immutable governance scope block`);
      }
      continue;
    }
    if (contractScriptPaths.has(path)) {
      verifySingleContractReplacement(path, before, after);
      continue;
    }
    fail(`${path}: outside the one-time deployment governance package`);
  }
  verifyRepository();
}

function verifyWorkflowRootOfTrust() {
  const source = current(WORKFLOW_PATH);
  const required = [
    'git show "$BASE_SHA:scripts/validate-deployment-concurrency-governance.mjs"',
    '--verify-bootstrap-addition',
    '--verify-pr',
    '--verify-repository',
    '--self-test',
    'fetch-depth: 0',
  ];
  required.forEach((marker) => {
    if (!source.includes(marker)) fail(`${WORKFLOW_PATH}: missing ${marker}`);
  });
}

function selfTest() {
  const good = `on: workflow_dispatch\nconcurrency:\n  group: projectpulse-deploy-test\n  queue: max\n  cancel-in-progress: false\njobs:\n  deploy:\n    environment: test\n    steps:\n      - uses: azure/login@pinned\n`;
  if (workflowFailures('good.yml', good).length !== 0) fail('self-test: valid Test workflow was rejected');
  if (!workflowFailures('missing-queue.yml', good.replace('  queue: max\n', '')).some((item) => item.includes('queue: max'))) {
    fail('self-test: missing queue was not rejected');
  }
  if (!workflowFailures('mixed.yml', `${good}\n  prod:\n    environment: production\n`).some((item) => item.includes('exactly one environment class'))) {
    fail('self-test: mixed environment classes were not rejected');
  }
  if (!workflowFailures('unprotected.yml', 'jobs:\n  deploy:\n    steps:\n      - uses: azure/login@pinned\n').some((item) => item.includes('requires a governed'))) {
    fail('self-test: unprotected Azure credentials were not rejected');
  }
  verifyWorkflowRootOfTrust();
}

function verifyBootstrapAddition(base) {
  const changed = changedPaths(base);
  const expected = [WORKFLOW_PATH, VALIDATOR_PATH].sort();
  if (JSON.stringify(changed) !== JSON.stringify(expected)) {
    fail(`bootstrap PR must add exactly ${expected.join(' and ')}`);
  }
  expected.forEach((path) => {
    if (fileAt(base, path) !== null) fail(`${path}: bootstrap path already exists in base`);
  });
  if (existsSync(atRoot(SENTINEL_PATH))) fail(`${SENTINEL_PATH}: sentinel belongs only in the enforcement PR`);
  selfTest();
}

function classifyBootstrap(base) {
  const changed = new Set(changedPaths(base));
  const baseHasSentinel = fileAt(base, SENTINEL_PATH) !== null;
  if (baseHasSentinel) {
    if (changed.has(SENTINEL_PATH) || changed.has(VALIDATOR_PATH) || changed.has(WORKFLOW_PATH)) return 1;
    return 2;
  }
  if (!changed.has(SENTINEL_PATH)) {
    if (changed.has(VALIDATOR_PATH) || changed.has(WORKFLOW_PATH)) return 1;
    return 2;
  }
  verifyBootstrapPackage(base);
  return failures.length === 0 ? 0 : 1;
}

function verifyPullRequest(base) {
  if (fileAt(base, VALIDATOR_PATH) === null) {
    verifyBootstrapAddition(base);
    return;
  }
  if (current(VALIDATOR_PATH) !== fileAt(base, VALIDATOR_PATH)
      || current(WORKFLOW_PATH) !== fileAt(base, WORKFLOW_PATH)) {
    fail('trusted governance validator and workflow cannot authorize their own modification');
    return;
  }
  const result = classifyBootstrap(base);
  if (result === 0) return;
  if (result === 1) {
    fail('invalid deployment-governance bootstrap or trusted-control mutation');
    return;
  }
  if (fileAt(base, SENTINEL_PATH) === null) {
    selfTest();
    return;
  }
  verifyRepository();
}

let exitCode = 0;
try {
  if (process.argv.includes('--self-test')) {
    selfTest();
  } else if (process.argv.includes('--verify-bootstrap-addition')) {
    verifyBootstrapAddition(baseRef());
  } else if (process.argv.includes('--classify-bootstrap-scope')) {
    exitCode = classifyBootstrap(baseRef());
  } else if (process.argv.includes('--verify-pr')) {
    verifyPullRequest(baseRef());
  } else if (process.argv.includes('--verify-repository')) {
    verifyRepository();
  } else {
    throw new Error('Select --self-test, --verify-bootstrap-addition, --classify-bootstrap-scope, --verify-pr, or --verify-repository');
  }
} catch (error) {
  fail(error instanceof Error ? error.message : String(error));
  exitCode = 1;
}

if (failures.length > 0) {
  console.error('DEPLOYMENT_CONCURRENCY_GOVERNANCE=FAILED');
  failures.forEach((failure) => console.error(`- ${failure}`));
  process.exit(exitCode || 1);
}

if (process.argv.includes('--classify-bootstrap-scope')) {
  console.log(`DEPLOYMENT_CONCURRENCY_GOVERNANCE_CLASSIFICATION=${exitCode === 0 ? 'BOOTSTRAP' : 'NOT_BOOTSTRAP'}`);
  process.exit(exitCode);
}

console.log('DEPLOYMENT_CONCURRENCY_GOVERNANCE=PASSED');
