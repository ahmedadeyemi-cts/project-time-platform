#!/usr/bin/env node

import { execFileSync } from 'node:child_process';
import { readdirSync, readFileSync } from 'node:fs';
import { join } from 'node:path';

const workflowRoot = new URL('../.github/workflows/', import.meta.url);
const workflowDirectory = workflowRoot.pathname;
const workflowNames = readdirSync(workflowDirectory)
  .filter((name) => /\.ya?ml$/i.test(name))
  .sort();

const failures = [];
let governed = 0;

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

const governanceOwnedPaths = new Set([
  '.github/workflows/deployment-concurrency-governance-ci.yml',
  'scripts/validate-deployment-concurrency-governance.mjs',
]);

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

function normalizeConcurrencyGroup(source) {
  const lines = source.split('\n');
  const concurrencyIndex = lines.findIndex((line) => line === 'concurrency:');
  if (concurrencyIndex < 0) return source;
  for (let index = concurrencyIndex + 1; index < lines.length; index += 1) {
    if (lines[index] !== '' && !/^\s/.test(lines[index])) break;
    if (/^\s+group:/.test(lines[index])) {
      lines[index] = '  group: __ENVIRONMENT_DEPLOYMENT_LOCK__';
    }
    if (/^\s+queue:/.test(lines[index])) lines.splice(index--, 1);
  }
  return lines.join('\n');
}

function removeGovernanceScopeBlock(source) {
  return source.replace(
    /^\s{10}# DEPLOYMENT_CONCURRENCY_GOVERNANCE_SCOPE_START\n[\s\S]*?^\s{10}# DEPLOYMENT_CONCURRENCY_GOVERNANCE_SCOPE_END\n/m,
    '',
  );
}

function normalizeContractGroup(source) {
  return source.replace(
    /projectpulse-(?:deploy|rollback|group4)[A-Za-z0-9_-]*/g,
    '__ENVIRONMENT_DEPLOYMENT_LOCK__',
  );
}

function validateGovernanceDiff(baseRef) {
  const range = `${baseRef}...HEAD`;
  const changed = execFileSync('git', ['diff', '--name-only', range], { encoding: 'utf8' })
    .trim()
    .split('\n')
    .filter(Boolean);

  for (const path of changed) {
    const current = readFileSync(path, 'utf8');
    if (governanceOwnedPaths.has(path)) continue;

    let base;
    try {
      base = execFileSync('git', ['show', `${baseRef}:${path}`], { encoding: 'utf8' });
    } catch {
      failures.push(`${path}: governance mode cannot add or replace this path`);
      continue;
    }

    const deploymentControl = /^\.github\/workflows\/projectpulse-deploy-.*-test\.yml$/.test(path)
      || path === '.github/workflows/projectpulse-deploy-production.yml'
      || path === '.github/workflows/projectpulse-rollback.yml'
      || path === '.github/workflows/projectpulse-run-group4-migration-050-test.yml';

    if (deploymentControl) {
      if (normalizeConcurrencyGroup(current) !== normalizeConcurrencyGroup(base)) {
        failures.push(`${path}: changed outside the single concurrency.group line`);
      }
      continue;
    }

    if (scopeWorkflowPaths.has(path)) {
      if (removeGovernanceScopeBlock(current) !== base) {
        failures.push(`${path}: changed outside the marker-delimited governance scope block`);
      }
      continue;
    }

    if (contractScriptPaths.has(path)) {
      if (normalizeContractGroup(current) !== normalizeContractGroup(base)) {
        failures.push(`${path}: changed outside the deployment concurrency contract string`);
      }
      continue;
    }

    failures.push(`${path}: is outside the reviewed deployment-concurrency governance scope`);
  }
}

const baseRefIndex = process.argv.indexOf('--base-ref');
if (baseRefIndex >= 0) {
  const baseRef = process.argv[baseRefIndex + 1];
  if (!baseRef || baseRef.startsWith('-')) {
    console.error('DEPLOYMENT_CONCURRENCY_GOVERNANCE=FAILED');
    console.error('- --base-ref requires an exact local Git ref');
    process.exit(1);
  }
  validateGovernanceDiff(baseRef);
}

for (const name of workflowNames) {
  const source = readFileSync(join(workflowDirectory, name), 'utf8');
  const usesAzureLogin = /uses:\s*azure\/login(?:@|\s)/.test(source);
  const environmentClasses = environmentClassesFor(source);

  for (const job of jobBlocks(source).filter((block) => /uses:\s*azure\/login(?:@|\s)/.test(block))) {
    if (environmentClassesFor(job).size !== 1) {
      failures.push(`${name}: every Azure-credentialed job must declare exactly one governed protected environment`);
    }
  }

  if (environmentClasses.size === 0) {
    if (usesAzureLogin) {
      failures.push(`${name}: Azure login requires a governed test, production, or dynamic protected environment`);
    }
    continue;
  }
  if (environmentClasses.size !== 1) {
    failures.push(`${name}: Azure login requires exactly one governed test, production, or dynamic environment class`);
    continue;
  }

  const environmentClass = [...environmentClasses][0];
  const expectedGroup = environmentClass === 'test'
    ? 'projectpulse-deploy-test'
    : environmentClass === 'production'
      ? 'projectpulse-deploy-production'
      : 'projectpulse-deploy-${{ inputs.environment }}';
  governed += 1;

  const concurrency = source.match(/^concurrency:\s*\n((?:^[ \t]+.*\n?)*)/m)?.[1] ?? '';
  const actualGroup = concurrency.match(/^\s+group:\s*(.+?)\s*$/m)?.[1] ?? '';
  const cancel = concurrency.match(/^\s+cancel-in-progress:\s*(.+?)\s*$/m)?.[1] ?? '';
  const queue = concurrency.match(/^\s+queue:\s*(.+?)\s*$/m)?.[1] ?? '';

  if (actualGroup !== expectedGroup) {
    failures.push(`${name}: expected concurrency group ${expectedGroup}, found ${actualGroup || 'none'}`);
  }
  if (cancel !== 'false') {
    failures.push(`${name}: environment mutations must set cancel-in-progress: false`);
  }
  if (queue !== 'max') {
    failures.push(`${name}: environment mutations must set queue: max so pending releases are not replaced`);
  }
}

if (governed === 0) failures.push('No Azure environment-mutating workflows were discovered.');

if (failures.length > 0) {
  console.error('DEPLOYMENT_CONCURRENCY_GOVERNANCE=FAILED');
  failures.forEach((failure) => console.error(`- ${failure}`));
  process.exit(1);
}

console.log(`DEPLOYMENT_CONCURRENCY_GOVERNANCE=PASSED workflows=${governed}`);
