#!/usr/bin/env node

import { readdirSync, readFileSync } from 'node:fs';
import { join } from 'node:path';

const workflowRoot = new URL('../.github/workflows/', import.meta.url);
const workflowDirectory = workflowRoot.pathname;
const workflowNames = readdirSync(workflowDirectory)
  .filter((name) => /\.ya?ml$/i.test(name))
  .sort();

const failures = [];
let governed = 0;

for (const name of workflowNames) {
  const source = readFileSync(join(workflowDirectory, name), 'utf8');
  if (!/uses:\s*azure\/login(?:@|\s)/.test(source)) continue;

  let expectedGroup = null;
  if (/^\s*environment:\s*test\s*$/m.test(source)) {
    expectedGroup = 'projectpulse-deploy-test';
  } else if (/^\s*environment:\s*production\s*$/m.test(source)) {
    expectedGroup = 'projectpulse-deploy-production';
  } else if (/^\s*environment:\s*\$\{\{\s*inputs\.environment\s*\}\}\s*$/m.test(source)) {
    expectedGroup = 'projectpulse-deploy-${{ inputs.environment }}';
  }

  if (!expectedGroup) continue;
  governed += 1;

  const concurrency = source.match(/^concurrency:\s*\n((?:^[ \t]+.*\n?)*)/m)?.[1] ?? '';
  const actualGroup = concurrency.match(/^\s+group:\s*(.+?)\s*$/m)?.[1] ?? '';
  const cancel = concurrency.match(/^\s+cancel-in-progress:\s*(.+?)\s*$/m)?.[1] ?? '';

  if (actualGroup !== expectedGroup) {
    failures.push(`${name}: expected concurrency group ${expectedGroup}, found ${actualGroup || 'none'}`);
  }
  if (cancel !== 'false') {
    failures.push(`${name}: environment mutations must set cancel-in-progress: false`);
  }
}

if (governed === 0) failures.push('No Azure environment-mutating workflows were discovered.');

if (failures.length > 0) {
  console.error('DEPLOYMENT_CONCURRENCY_GOVERNANCE=FAILED');
  failures.forEach((failure) => console.error(`- ${failure}`));
  process.exit(1);
}

console.log(`DEPLOYMENT_CONCURRENCY_GOVERNANCE=PASSED workflows=${governed}`);
