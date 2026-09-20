import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { verifyReadOnlyWorkflow } from './flowhive-psa-scope.mjs';

// Source preparation only. This registration neither authorizes a deployment
// nor changes the existing candidate, approval, or release admission controls.
const base = '373b37e9c76e430355ed2dc5f71ec6a133ead3aa';
const branch = 'feat/sa-workspace-redesign-20260920';
const expected = [
  '.github/workflows/flowhive-psa-release-control-ci.yml',
  '.github/workflows/module025-sa-workspace-ci.yml',
  'database/migrations/116_module025_governed_ownership_transfer.sql',
  'database/migrations/117_module025_template_candidates.sql',
  'database/migrations/118_module025_work_tracking.sql',
  'database/migrations/119_module025_temporary_handoffs.sql',
  'database/migrations/120_module025_handoff_notifications.sql',
  'database/rollback/116_module025_governed_ownership_transfer_rollback.sql',
  'database/rollback/117_module025_template_candidates_rollback.sql',
  'database/rollback/118_module025_work_tracking_rollback.sql',
  'database/rollback/119_module025_temporary_handoffs_rollback.sql',
  'database/rollback/120_module025_handoff_notifications_rollback.sql',
  'docs/module025/sa-workspace-redesign.md',
  'docs/module025/sa-workspace-uat-rollout.md',
  'docs/production-readiness/foundation/initialization-review.json',
  'scripts/release-test/build-and-run-module025-retention-migration-106.sh',
  'scripts/release-test/validate-module025-governed-release.sh',
  'scripts/release-test/validate-protected-test-controller-branches.sh',
  'src/backend/ProjectTime.Api/Modules/EnterpriseNotificationOrchestrationService.cs',
  'src/backend/ProjectTime.Api/Modules/EnterpriseNotificationRecipientResolver.cs',
  'src/backend/ProjectTime.Api/Modules/EnterpriseNotificationRepository.cs',
  'src/backend/ProjectTime.Api/Modules/Module025HandoffNotifications.cs',
  'src/backend/ProjectTime.Api/Modules/Module025SowGsdDocumentExporter.cs',
  'src/backend/ProjectTime.Api/Modules/Module025SowGsdModule.cs',
  'src/backend/ProjectTime.Api/Modules/Module025SowGsdTransfers.cs',
  'src/backend/ProjectTime.Api/Modules/Module025StandardGsdExporter.cs',
  'src/backend/ProjectTime.Api/Modules/Module025TaskDrafts.cs',
  'src/backend/ProjectTime.Api/Modules/Module025TaskEstimates.cs',
  'src/backend/ProjectTime.Api/Modules/Module025TemplateCatalog.cs',
  'src/backend/ProjectTime.Api/Modules/Module025WorkTracking.cs',
  'src/frontend/project-time-web/src/module025/OwnershipTransfer.jsx',
  'src/frontend/project-time-web/src/module025/PhaseTaskReview.jsx',
  'src/frontend/project-time-web/src/module025/SowGsdAuthoringWorkspace.jsx',
  'src/frontend/project-time-web/src/module025/TemplateCatalog.jsx',
  'src/frontend/project-time-web/src/module025/WorkTrackingPanel.jsx',
  'src/frontend/project-time-web/src/module025/sa-workspace-redesign.css',
  'src/frontend/project-time-web/src/module025/task-estimates.js',
  'src/frontend/project-time-web/src/module025/template-catalog.css',
  'src/frontend/project-time-web/src/module025/work-queue.js',
  'src/frontend/project-time-web/src/module025/work-tracking.css',
  'src/frontend/project-time-web/src/module025/work-tracking.js',
  'tests/Module025ExportTests/Module025ExportTests.csproj',
  'tests/Module025ExportTests/Program.cs',
  'tests/Module025HandoffNotificationTests/Module025HandoffNotificationTests.csproj',
  'tests/Module025HandoffNotificationTests/Program.cs',
  'tests/Module025TemplateCatalogTests/Module025TemplateCatalogTests.csproj',
  'tests/Module025TemplateCatalogTests/Program.cs',
  'tests/Module025TransferTests/Module025TransferTests.csproj',
  'tests/Module025TransferTests/Program.cs',
  'tests/Module025WorkTrackingTests/Module025WorkTrackingTests.csproj',
  'tests/Module025WorkTrackingTests/Program.cs',
  'tests/flowhive-psa-admission.test.mjs',
  'tests/module025-coverage.test.mjs',
  'tests/module025-document-actions.test.mjs',
  'tests/module025-export-deploy.test.py',
  'tests/module025-installed-browser-harness.mjs',
  'tests/module025-sa-rollout.test.py',
  'tests/module025-sa-workspace-scope.mjs',
  'tests/module025-task-editor.test.mjs',
  'tests/module025-task-estimates.test.mjs',
  'tests/module025-team-workspace.test.mjs',
  'tests/module025-work-queue.test.mjs',
  'tests/module025-work-tracking.test.mjs',
  'tests/test-module025-template-migration-117.sh',
].sort();

const registrations = [
  {
    file: '.github/workflows/flowhive-psa-release-control-ci.yml',
    block: '          if [[ "$GITHUB_HEAD_REF" == feat/sa-workspace-redesign-20260920 ]]; then\n            node tests/module025-sa-workspace-scope.mjs\n          elif',
    original: '          if',
  },
  {
    file: 'scripts/release-test/validate-protected-test-controller-branches.sh',
    block: 'if [[ "$HEAD_BRANCH" == feat/sa-workspace-redesign-20260920 ]]; then\n  node tests/module025-sa-workspace-scope.mjs\n  node tests/validate-systemwide-image-build-controller.mjs\nelif',
    original: 'if',
  },
  {
    file: 'scripts/release-test/validate-module025-governed-release.sh',
    block: 'if [[ "$HEAD_BRANCH" == feat/sa-workspace-redesign-20260920 ]]; then\n  node tests/module025-sa-workspace-scope.mjs\n  node tests/validate-systemwide-image-build-controller.mjs\n  exit 0\nelif',
    original: 'if',
  },
];

const git = (...args) => execFileSync('git', args, { encoding: 'utf8' }).trim();

export function verifyFeaturePaths(actual) {
  assert.deepEqual([...new Set(actual)].sort(), expected, 'SA workspace PR differs from its exact source-only feature scope.');
}

export function verifyModule025SaWorkspaceScope() {
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

  // The only migration-runner increment is its exact 116–120 payload and schema
  // verification. Its original deployment authority is byte-for-byte pinned.
  execFileSync('python3', ['tests/module025-sa-rollout.test.py', '--source-only'], { stdio: 'pipe' });

  assert.equal(git('rev-parse', 'HEAD:deployment'), git('rev-parse', `${base}:deployment`), 'Deployment infrastructure must remain unchanged.');
  execFileSync('git', ['diff', '--check', base], { stdio: 'pipe' });
  console.log(`MODULE025_SA_WORKSPACE_SCOPE=PASS files=${expected.length} deployment_authority=unchanged migrations=prepared_not_applied production=unchanged`);
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  if (process.argv.includes('--self-test')) {
    verifyFeaturePaths(expected);
    assert.throws(() => verifyFeaturePaths([...expected, '.github/flowhive-psa-protected-test-candidate.json']));
    assert.throws(() => verifyFeaturePaths(expected.slice(1)));
    console.log('MODULE025_SA_WORKSPACE_SCOPE_NEGATIVE_TESTS=PASS');
  } else verifyModule025SaWorkspaceScope();
}
