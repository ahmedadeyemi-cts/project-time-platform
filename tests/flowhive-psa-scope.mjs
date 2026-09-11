import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

export const manifestPath = '.github/flowhive-enterprise-psa-release-files.txt';
const module025ManifestPath = '.github/module025-sow-sell-governed-release-files.txt';
const module025ReviewedPaths = new Set(fs.readFileSync(module025ManifestPath, 'utf8').trim().split(/\r?\n/));
const validationFiles = new Set([
  manifestPath,
  module025ManifestPath,
  '.github/workflows/flowhive-enterprise-psa-ci.yml',
  '.github/workflows/flowhive-psa-installed-acceptance.yml',
  '.github/workflows/flowhive-psa-release-control-ci.yml',
  '.github/workflows/celar-ai-production-platform-ci.yml',
  '.github/workflows/projectpulse-release-test-control-ci.yml',
  '.github/workflows/projectpulse-release-test-control-ci-reregistered.yml',
  'deployment/containers/web/default.conf.template',
  'scripts/release-test/apply-flowhive-psa-migrations.sh',
  'scripts/release-test/build-and-run-flowhive-psa-migrations.sh',
  'scripts/release-test/reconcile-flowhive-planner.py',
  'scripts/release-test/run-flowhive-my-role-browser.py',
  'scripts/release-test/run-flowhive-psa-live-uat.py',
  'scripts/release-test/run-module025-sow-gsd-protected-test-uat.sh',
  'scripts/ci/validate-celar-ai-enterprise-source-boundary.sh',
  'tests/validate-celar-ai-pr630-consolidated.mjs',
  'tests/test-pulse-ai-runtime-job-query-shape.sh',
  'tests/validate-flowhive-sow-evidence-autoadmission.mjs',
  'src/frontend/project-time-web/scripts/inject-celar-ai-production-platform.mjs',
  'src/frontend/project-time-web/scripts/validate-celar-ai-production-platform.mjs',
  'src/frontend/project-time-web/scripts/validate-production-consistency.mjs',
  'src/frontend/project-time-web/scripts/validate-module-066-project-flowhive.mjs',
  'tests/test-module-066-flowhive-enterprise-pm-migration-086.sh',
  'src/backend/ProjectTime.Api/Modules/ProjectForgeModule.cs',
  'src/backend/ProjectTime.Api/Modules/PlatformOperationsContracts.cs',
  'src/backend/ProjectTime.Api/ProjectTime.Api.csproj',
  'src/backend/ProjectTime.Api/Properties/AssemblyInfo.cs',
  'src/frontend/project-time-web/src/ProjectForgeCenter.jsx',
  'src/frontend/project-time-web/src/project-forge-center.css',
  'src/frontend/project-time-web/src/project-forge/projectForgeApi.js'
]);
const roleJourneyPaths = new Set([
  'docs/modules/module-999-role-journeys/README.md',
  'src/frontend/project-time-web/scripts/role-journeys-vite-plugin.mjs',
  'src/frontend/project-time-web/src/role-journeys/MyRoleInPulse.jsx',
  'src/frontend/project-time-web/src/role-journeys/RoleJourneyGuideRouter.jsx',
  'src/frontend/project-time-web/src/role-journeys/role-journeys.css',
  'src/frontend/project-time-web/src/role-journeys/role-journeys.js',
  'src/frontend/project-time-web/src/role-journeys/use-role-journey-context.js',
  'src/frontend/project-time-web/tests/role-journeys.test.mjs',
  'src/frontend/project-time-web/vite.config.js'
]);
const componentPaths = [
  /^src\/backend\/ProjectTime\.Api\/Assets\/Fonts\/[A-Za-z0-9._-]+$/,
  /^src\/backend\/ProjectTime\.Api\/Modules\/(?:ProjectFlowHive[A-Za-z0-9]+|Module025SowGsdModule)\.cs$/,
  /^src\/backend\/ProjectTime\.Api\/Modules\/ProjectPlanning(AiOrchestrator|DocumentResolver)\.cs$/,
  /^src\/backend\/ProjectTime\.Api\/Modules\/CelarAiProductionPlatformModule\.cs$/,
  /^src\/frontend\/project-time-web\/src\/(ProjectFlowHive[A-Za-z0-9]+\.jsx|(?:flowhive-|project-flowhive-|use-flowhive-)[a-z0-9.-]+)$/,
  /^database\/(migrations|rollback)\/(103_module_066_flowhive_enterprise_psa_revamp|104_flowhive_bounded_ai_execution|105_flowhive_reviewed_regeneration)(?:_rollback)?\.sql$/,
  /^tests\/FlowHive[A-Za-z0-9]+\/[A-Za-z0-9._-]+$/,
  /^tests\/flowhive-psa-[a-z0-9.-]+$/,
  /^docs\/modules\/module-066-project-flowhive\/[A-Za-z0-9._-]+$/
];

export function verifyPaths(actual, reviewed) {
  assert.ok(Array.isArray(actual) && Array.isArray(reviewed) && reviewed.length > 0, 'A reviewed manifest is required');
  for (const name of [...actual, ...reviewed]) {
    assert.ok(typeof name === 'string' && name.length > 0 && !name.includes('\\')
      && !name.split('/').includes('..') && !/[\s*?\[\]{}]/.test(name), `Invalid concrete scope path: ${name}`);
    assert.ok(validationFiles.has(name) || roleJourneyPaths.has(name) || module025ReviewedPaths.has(name) || componentPaths.some(pattern => pattern.test(name)),
      `Outside the reviewed FlowHive source boundary: ${name}`);
  }
  assert.deepEqual(reviewed, [...new Set(reviewed)].sort(), 'The manifest must be sorted and unique');
  assert.equal(actual.length, new Set(actual).size, 'Duplicate changed path');
  assert.deepEqual([...actual].sort(), reviewed, 'Changed files must exactly match the reviewed manifest');
}

export function verifyReadOnlyWorkflow(text, name) {
  assert.match(text, /^permissions:\s*\n\s+contents:\s*read\s*$/m, `Read-only CI permissions required: ${name}`);
  assert.ok(!/^\s*(?:contents|id-token|actions|pull-requests|packages):\s*write\s*$/m.test(text), `Privileged CI is outside this release: ${name}`);
  const installedAcceptance = name === '.github/workflows/flowhive-psa-installed-acceptance.yml';
  if (installedAcceptance) {
    assert.match(text, /environment:\s*\n\s+name:\s*test\b/, 'Installed acceptance may use only the protected Test environment.');
    assert.ok(!/\baz(?:\s|\.)|containerapp|docker\s+(?:build|run)|workflow\s+(?:dispatch|enable|disable)/i.test(text),
      'Installed acceptance cannot mutate deployment infrastructure.');
  } else {
    assert.ok(!/^\s*(?:environment:|uses:\s*azure\/login@)/m.test(text), `No deployment environment or Azure login: ${name}`);
  }
  // This exact inherited admission CI uses only the read-only repository token
  // to inspect deployment state. No application credential or write scope is allowed.
  const sanitized = name === '.github/workflows/flowhive-psa-release-control-ci.yml'
    ? text.replace(/\$\{\{ secrets\.GITHUB_TOKEN \}\}/g, '')
    : installedAcceptance
      ? text.replace(/\$\{\{ secrets\.(?:PROJECTPULSE_TEST_UAT_SESSION|PROJECTPULSE_TEST_UAT_ADMIN_EMAIL|PROJECTPULSE_TEST_UAT_ADMIN_PASSWORD|PROJECTPULSE_M025_PM_EMAIL|PROJECTPULSE_M025_PM_PASSWORD|PROJECTPULSE_M025_SA_EMAIL|PROJECTPULSE_M025_SA_PASSWORD|PROJECTPULSE_M087_PASSWORD) \}\}/g, '')
      : text;
  assert.ok(!/\$\{\{\s*secrets\./.test(sanitized), `No application or deployment secrets in validation: ${name}`);
}

export function verifyRepositoryScope() {
  const reviewed = fs.readFileSync(manifestPath, 'utf8').trim().split(/\r?\n/);
  const base = execFileSync('git', ['merge-base', 'origin/main', 'HEAD'], { encoding: 'utf8' }).trim();
  assert.match(base, /^[a-f0-9]{40}$/, 'The current main merge base must be available');
  const actual = execFileSync('git', ['diff', '--name-only', `${base}...HEAD`], { encoding: 'utf8' })
    .trim().split(/\r?\n/).filter(Boolean);
  verifyPaths(actual, reviewed);
  for (const name of reviewed) {
    assert.ok(fs.existsSync(name) && fs.lstatSync(name).isFile(), `Missing reviewed source file: ${name}`);
    if (name.startsWith('.github/workflows/')) verifyReadOnlyWorkflow(fs.readFileSync(name, 'utf8'), name);
  }
  for (const temporary of ['.github/flowhive-reviewed-source.patch',
    '.github/workflows/flowhive-reviewed-source-apply.yml', '.github/workflows/flowhive-offline-validation-tools.yml']) {
    assert.ok(!fs.existsSync(temporary), `Temporary source transport must be removed: ${temporary}`);
  }
  execFileSync('git', ['diff', '--check', `${base}...HEAD`], { stdio: 'pipe' });
  console.log(`FLOWHIVE_ENTERPRISE_PSA_EXACT_SOURCE_SCOPE=PASSED files=${actual.length} base=${base}`);
  return { base, files: actual };
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) verifyRepositoryScope();
