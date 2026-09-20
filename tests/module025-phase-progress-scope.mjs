import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { verifyReadOnlyWorkflow } from './flowhive-psa-scope.mjs';

// Source preparation only. This registration neither authorizes a deployment
// nor changes the existing candidate, approval, or release admission controls.
const base = '35a442bee3724d7d4e5865f4abc2aaa910d55326';
const branch = 'feat/module025-phase-progress-20260920';
const expected = [
  ".github/workflows/flowhive-psa-release-control-ci.yml",
  ".github/workflows/module025-phase-progress-ci.yml",
  "docs/module025/shared-scope-phase-generation.md",
  "scripts/release-test/validate-module025-governed-release.sh",
  "scripts/release-test/validate-protected-test-controller-branches.sh",
  "src/backend/ProjectTime.Api/Ai/Module025ExternalSowAdapter.cs",
  "src/backend/ProjectTime.Api/Ai/Module025GenerationEngine.cs",
  "src/backend/ProjectTime.Api/Ai/PulseAiPrivateRagService.cs",
  "src/backend/ProjectTime.Api/Modules/Module025GenerationStatus.cs",
  "src/backend/ProjectTime.Api/Modules/Module025SowGsdModule.cs",
  "src/frontend/project-time-web/scripts/validate-module025-sow-register.mjs",
  "src/frontend/project-time-web/src/module025/GenerationProgress.jsx",
  "src/frontend/project-time-web/src/module025/SowGsdAuthoringWorkspace.jsx",
  "src/frontend/project-time-web/src/module025/generation-feedback.js",
  "src/frontend/project-time-web/src/module025/generation-progress.css",
  "src/frontend/project-time-web/src/module025/generation-progress.js",
  "src/frontend/project-time-web/src/module025/useGenerationMonitor.js",
  "tests/FlowHiveDetailedPlannerTests/Module025ExternalSowTests.cs",
  "tests/FlowHiveDetailedPlannerTests/Module025GenerationEngineTests.cs",
  "tests/FlowHiveDetailedPlannerTests/Module025GenerationStatusTests.cs",
  "tests/FlowHiveDetailedPlannerTests/Program.cs",
  "tests/flowhive-psa-admission.test.mjs",
  "tests/module025-generation-feedback.test.mjs",
  "tests/module025-generation-progress.test.mjs",
  "tests/module025-phase-progress-scope.mjs",
  "tests/validate-systemwide-image-build-controller.mjs"
].sort();

const registrations = [
  {
    file: '.github/workflows/flowhive-psa-release-control-ci.yml',
    block: '          if [[ "$GITHUB_HEAD_REF" == feat/module025-phase-progress-20260920 ]]; then\n            node tests/module025-phase-progress-scope.mjs\n          elif',
    original: '          if',
  },
  {
    file: 'scripts/release-test/validate-protected-test-controller-branches.sh',
    block: 'if [[ "$HEAD_BRANCH" == feat/module025-phase-progress-20260920 ]]; then\n  node tests/module025-phase-progress-scope.mjs\n  node tests/validate-systemwide-image-build-controller.mjs\nelif',
    original: 'if',
  },
  {
    file: 'scripts/release-test/validate-module025-governed-release.sh',
    block: 'if [[ "$HEAD_BRANCH" == feat/module025-phase-progress-20260920 ]]; then\n  node tests/module025-phase-progress-scope.mjs\n  node tests/validate-systemwide-image-build-controller.mjs\n  exit 0\nelif',
    original: 'if',
  },
];

const git = (...args) => execFileSync('git', args, { encoding: 'utf8' }).trim();

export function verifyFeaturePaths(actual) {
  assert.deepEqual([...new Set(actual)].sort(), expected, 'Phase progress PR differs from its exact source-only feature scope.');
}

export function verifyModule025PhaseProgressScope() {
  const reportedBranch = process.env.GITHUB_HEAD_REF;
  if (reportedBranch) assert.equal(reportedBranch, branch, 'This source registration applies only to the named preparation branch.');
  if (process.env.GITHUB_EVENT_NAME)
    assert.equal(process.env.GITHUB_EVENT_NAME, 'pull_request', 'Preparation validation must not authorize manual dispatch or deployment.');
  assert.equal(git('merge-base', base, 'HEAD'), base, 'The reviewed source base must remain an ancestor.');
  // Include pending local changes for pre-commit validation; CI runs on a clean
  // exact-head checkout, so the same comparison covers its complete PR diff.
  const actual = [
    ...git('diff', '--name-only', base).split(/\r?\n/),
    ...git('ls-files', '--others', '--exclude-standard', '--exclude=src/frontend/project-time-web/node_modules', '--exclude=src/frontend/project-time-web/.celar-ai-production-build-backup/').split(/\r?\n/),
  ].filter(Boolean);
  verifyFeaturePaths(actual);
  assert.throws(() => verifyFeaturePaths([...expected, '.github/workflows/projectpulse-deploy-production.yml']));
  assert.throws(() => verifyFeaturePaths(expected.slice(1)));

  for (const file of expected.filter(file => file.startsWith('.github/workflows/')))
    verifyReadOnlyWorkflow(fs.readFileSync(file, 'utf8'), file);

  // Register this source feature while retaining every previous CI assertion.
  for (const { file, block, original } of registrations) {
    const text = fs.readFileSync(file, 'utf8');
    assert.equal(text.split(block).length, 2, `Missing or duplicate exact registration: ${file}`);
    assert.equal(text.replace(block, original), execFileSync('git', ['show', `${base}:${file}`], { encoding: 'utf8' }),
      `Unrelated validation changes: ${file}`);
  }

  for (const file of [
    '.github/flowhive-psa-protected-test-candidate.json',
    '.github/flowhive-psa-release-control-files.txt',
    '.github/workflows/projectpulse-deploy-test.yml',
    '.github/workflows/projectpulse-deploy-production.yml',
    '.github/workflows/module025-protected-uat-control.yml',
    '.github/workflows/flowhive-psa-installed-acceptance.yml',
    'scripts/release-test/flowhive-psa-admission.mjs',
    'scripts/release-test/dispatch-flowhive-psa-test.mjs',
    'scripts/release-test/build-and-run-project-planning-document-authority-migration-job.sh',
    'src/backend/ProjectTime.Api/Modules/Module025SowSellModule.cs',
    'src/backend/ProjectTime.Api/Modules/Module025SowSellPolicy.cs',
    'src/backend/ProjectTime.Api/Modules/Module025SowSellWorker.cs',
    'database/migrations/106_module025_sow_sell_register.sql',
    'database/migrations/110_module025_ungenerated_draft_delete.sql',
  ]) assert.deepEqual(fs.readFileSync(file), execFileSync('git', ['show', `${base}:${file}`]), `Release or retained lifecycle boundary changed: ${file}`);

  // Only select the existing historical verifier fixture for this source branch.
  // No deployment admission logic or approved source-drift set is expanded.
  const admissionFile = 'tests/flowhive-psa-admission.test.mjs';
  const admission = fs.readFileSync(admissionFile, 'utf8')
    .replace("const phaseProgressPreparation = process.env.GITHUB_HEAD_REF === 'feat/module025-phase-progress-20260920';\n", '')
    .replace('const module025VerifierCorrection = phaseProgressPreparation || ', 'const module025VerifierCorrection = ')
    .replace("const module025VerifierBase = phaseProgressPreparation ? '35a442bee3724d7d4e5865f4abc2aaa910d55326' : ", 'const module025VerifierBase = ');
  assert.equal(admission, execFileSync('git', ['show', `${base}:${admissionFile}`], { encoding: 'utf8' }),
    'Only the exact source-only branch fixture may be registered.');

  assert.equal(git('rev-parse', 'HEAD:deployment'), git('rev-parse', `${base}:deployment`), 'Deployment infrastructure must remain unchanged.');
  execFileSync('git', ['diff', '--check', base], { stdio: 'pipe' });
  console.log(`MODULE025_PHASE_PROGRESS_SCOPE=PASS files=${expected.length} deployment_authority=unchanged migrations=none production=unchanged`);
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  if (process.argv.includes('--self-test')) {
    verifyFeaturePaths(expected);
    assert.throws(() => verifyFeaturePaths([...expected, '.github/flowhive-psa-protected-test-candidate.json']));
    assert.throws(() => verifyFeaturePaths(expected.slice(1)));
    console.log('MODULE025_PHASE_PROGRESS_SCOPE_NEGATIVE_TESTS=PASS');
  } else verifyModule025PhaseProgressScope();
}
