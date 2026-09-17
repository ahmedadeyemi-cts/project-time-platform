import assert from 'node:assert/strict';
import fs from 'node:fs';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import { verifyReadOnlyWorkflow } from './flowhive-psa-scope.mjs';

const base = '5c293fc66ecb50383361e1f6bdb2bcbd4cd11a7b';
const expected = [
  ".github/workflows/flowhive-psa-release-control-ci.yml",
  ".github/workflows/module-management-owner-drawer-ci.yml",
  "database/migrations/109_module025_project_name.sql",
  "docs/releases/2026-09-17-module025-project-name.md",
  "scripts/release-test/build-and-run-project-planning-document-authority-migration-job.sh",
  "scripts/release-test/validate-protected-test-controller-branches.sh",
  "src/backend/ProjectTime.Api/Modules/Module025SowGsdContracts.cs",
  "src/backend/ProjectTime.Api/Modules/Module025SowGsdDocumentExporter.cs",
  "src/backend/ProjectTime.Api/Modules/Module025SowGsdModule.cs",
  "src/backend/ProjectTime.Api/Modules/Module025SowSellModule.cs",
  "src/backend/ProjectTime.Api/Modules/Module025SowSellPolicy.cs",
  "src/backend/ProjectTime.Api/Modules/Module025SowSellReporting.cs",
  "src/backend/ProjectTime.Api/Modules/Module025SowSellWorker.cs",
  "src/frontend/project-time-web/src/module025/SowGsdAuthoringWorkspace.jsx",
  "src/frontend/project-time-web/src/module025/SowRegister.jsx",
  "tests/flowhive-psa-admission.test.mjs",
  "tests/module025-project-name-scope.mjs",
  "tests/validate-celar-ai-pr630-consolidated.mjs"
].sort();

export function verifyModule025ProjectNameScope() {
  assert.equal(execFileSync('git', ['merge-base', base, 'HEAD'], { encoding: 'utf8' }).trim(), base);
  const text = execFileSync('git', ['diff', '--name-only', `${base}...HEAD`], { encoding: 'utf8' }).trim();
  const actual = text ? text.split(/\r?\n/) : [];
  assert.deepEqual([...actual].sort(), expected);
  assert.throws(() => assert.deepEqual([...actual, '.github/workflows/projectpulse-deploy-production.yml'].sort(), expected));
  assert.throws(() => assert.deepEqual(actual.slice(1).sort(), expected));

  for (const file of expected.filter(file => file.startsWith('.github/workflows/')))
    verifyReadOnlyWorkflow(fs.readFileSync(file, 'utf8'), file);

  for (const file of [
    '.github/flowhive-psa-protected-test-candidate.json',
    '.github/flowhive-psa-release-control-files.txt',
    'scripts/release-test/flowhive-psa-admission.mjs',
    'scripts/release-test/dispatch-flowhive-psa-test.mjs',
    'scripts/validate-deployment-concurrency-governance.mjs',
    '.github/workflows/projectpulse-deploy-test.yml',
    '.github/workflows/projectpulse-deploy-production.yml',
    '.github/workflows/flowhive-psa-installed-acceptance.yml'
  ]) assert.deepEqual(fs.readFileSync(file), execFileSync('git', ['show', `${base}:${file}`]), `Deployment authority changed: ${file}`);

  for (const name of [
    'deployment',
    'src/backend/ProjectTime.Api/Ai/ProjectPulseAiRemoteProviders.cs',
    'src/backend/ProjectTime.Api/Ai/Module025PhaseOutputContract.cs',
    'src/backend/ProjectTime.Api/Ai/PulseAiPrivateModelClient.cs',
    'src/backend/ProjectTime.Api/Ai/ProjectPulseAiConfiguration.cs',
    'src/backend/ProjectTime.Api/Ai/ProjectPulseAiSecretStore.cs'
  ]) assert.equal(
    execFileSync('git', ['rev-parse', `HEAD:${name}`], { encoding: 'utf8' }),
    execFileSync('git', ['rev-parse', `${base}:${name}`], { encoding: 'utf8' }),
    `Out-of-scope change: ${name}`
  );

  const migration = fs.readFileSync('database/migrations/109_module025_project_name.sql', 'utf8');
  assert.match(migration, /ADD COLUMN IF NOT EXISTS project_name/i);
  assert.match(migration, /109_module025_project_name/);
  const runner = fs.readFileSync('scripts/release-test/build-and-run-project-planning-document-authority-migration-job.sh', 'utf8');
  assert.match(runner, /109_module025_project_name\.sql/);
  assert.match(runner, /MIGRATION_109_MODULE025_PROJECT_NAME=APPLIED_AND_VERIFIED/);

  console.log('MODULE025_PROJECT_NAME_SCOPE=PASS deployment_authority=unchanged production=unchanged');
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url))
  verifyModule025ProjectNameScope();
