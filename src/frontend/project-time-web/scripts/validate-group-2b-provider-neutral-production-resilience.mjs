import fs from 'node:fs';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const webRoot = path.resolve(scriptDirectory, '..');
const repositoryRoot = path.resolve(webRoot, '../../..');
const sourceRoot = path.join(webRoot, 'src');
const backendModulePath = path.join(repositoryRoot, 'src/backend/ProjectTime.Api/Modules/PlatformProductionResilienceModule.cs');
const projectPath = path.join(repositoryRoot, 'src/backend/ProjectTime.Api/ProjectTime.Api.csproj');
const panelPath = path.join(sourceRoot, 'PlatformResiliencePlanningPanel.jsx');
const cssPath = path.join(sourceRoot, 'platform-resilience-planning-panel.css');
const injectionPath = path.join(scriptDirectory, 'inject-group-2b-provider-neutral-resilience.mjs');
const packagePath = path.join(webRoot, 'package.json');
const documentationPath = path.join(repositoryRoot, 'docs/modules/group-2b-production-resilience/README.md');
const fullRepositoryContext = fs.existsSync(path.join(repositoryRoot, '.git'))
  || fs.existsSync(path.join(repositoryRoot, '.github/workflows/projectpulse-ci.yml'));

const protectedDeploymentFiles = [
  '.github/workflows/projectpulse-deploy-runtime-direct-timer-recovery-test.yml',
  '.github/workflows/validate-runtime-direct-timer-recovery-deployment.yml',
  'scripts/validate-runtime-direct-timer-recovery-test-deployment.sh'
];

const moduleApis = [
  '/api/system/backup-dr/production-planning',
  '/api/system/restore-validation/recovery-continuity',
  '/api/system/replication-sync/redundancy-failover',
  '/api/system/backup-dr/resilience-report',
  '/api/system/backup-dr/resilience-report/export'
];

let checks = 0;

function read(filePath) {
  if (!fs.existsSync(filePath)) throw new Error(`Required Group 2B file is missing: ${path.relative(repositoryRoot, filePath)}`);
  return fs.readFileSync(filePath, 'utf8');
}

function optionalRead(filePath) {
  return fs.existsSync(filePath) ? fs.readFileSync(filePath, 'utf8') : '';
}

function assert(condition, message) {
  checks += 1;
  if (!condition) throw new Error(message);
}

function contains(source, value, label) {
  assert(source.includes(value), `${label} is missing: ${value}`);
}

function count(source, value) {
  return source.split(value).length - 1;
}

const backend = fullRepositoryContext ? read(backendModulePath) : optionalRead(backendModulePath);
const project = fullRepositoryContext ? read(projectPath) : optionalRead(projectPath);
const panel = read(panelPath);
const css = read(cssPath);
const injection = read(injectionPath);
const packageJson = JSON.parse(read(packagePath));
const documentation = fullRepositoryContext ? read(documentationPath) : optionalRead(documentationPath);

if (fullRepositoryContext) {
  contains(backend, 'public static partial class PlatformOperationsModule', 'Group 2B backend');
  contains(backend, 'BuildSnapshotAsync(context, connection)', 'Group 2A abstraction consumption');
  contains(backend, 'AuthorizeAsync(context)', 'actual-session administrator authorization');
  contains(backend, 'AccessContract(context)', 'access contract');
  contains(backend, 'SecurityContract()', 'security contract');
  contains(backend, 'not_recorded', 'truthful missing-evidence contract');
  contains(backend, 'Modules 014, 015, and 017 consume the provider-neutral platform snapshot', 'provider-neutral ownership');

  for (const api of moduleApis) contains(backend, api, 'Group 2B reporting API');
  assert(count(backend, 'endpoints.MapGet(') === moduleApis.length, 'Group 2B backend must expose exactly five read-only GET endpoints.');
  assert(!/\.Map(Post|Put|Patch|Delete)\s*\(/.test(backend), 'Group 2B backend must not expose mutation endpoints.');

  for (const field of [
    'environment_and_production_readiness_planning',
    'backup_recovery_restoration_and_continuity',
    'availability_regions_replicas_redundancy_and_failover',
    'RecoveryPointObjectiveMinutes',
    'RecoveryTimeObjectiveMinutes',
    'LastSuccessfulRecoveryTestAt',
    'DatabaseReplicaStatus',
    'StorageReplicationStatus',
    'FailoverPrerequisites',
    'ResponsibleOwners',
    'approvalHistory',
    'BuildProductionPlanningBlockers',
    'BuildRecoveryBlockers',
    'BuildRedundancyBlockers'
  ]) {
    contains(backend.toLowerCase(), field.toLowerCase(), 'Group 2B shared contract');
  }

  assert(count(project, 'app.MapPlatformProductionResilienceEndpoints();') === 1, 'ProjectTime.Api.csproj must register the Group 2B API map exactly once.');
  assert(!project.includes('app.MapPlatformProductionResilienceEndpoints();app.MapPlatformProductionResilienceEndpoints();'), 'Group 2B API registration must not be duplicated.');
  assert(!/\boracle\b/i.test(backend), 'Primary Group 2B API must not encode Oracle-specific assumptions.');

  contains(documentation, 'Module 014', 'Module 014 documentation');
  contains(documentation, 'Module 015', 'Module 015 documentation');
  contains(documentation, 'Module 017', 'Module 017 documentation');
  contains(documentation, 'Group 2A', 'Group 2A dependency documentation');
  contains(documentation, 'No migration', 'migration declaration');
  for (const protectedPath of protectedDeploymentFiles) {
    assert(!documentation.includes(`modify ${protectedPath}`), `Documentation must not authorize changes to ${protectedPath}.`);
  }
} else {
  console.log('GROUP_2B_BACKEND_AND_GOVERNANCE_CONTRACT=SKIPPED_FRONTEND_CONTAINER_CONTEXT');
}

contains(panel, "import { usSignalLogoDataUrl } from './assets/usSignalLogoData.js';", 'US Signal branding');
contains(panel, 'data-projectpulse-group2b="provider-neutral"', 'provider-neutral UI marker');
contains(panel, 'Current versus target', 'platform comparison UI');
contains(panel, 'Single-instance constraint', 'single-instance limitation UI');
contains(panel, 'Recovery point objective', 'RPO UI');
contains(panel, 'Recovery time objective', 'RTO UI');
contains(panel, 'Last recovery test', 'recovery-test UI');
contains(panel, 'Database replica', 'database-replica UI');
contains(panel, 'Storage replication', 'storage-replication UI');
contains(panel, 'Regional coverage', 'regional coverage UI');
contains(panel, 'Failover prerequisites', 'failover prerequisite UI');
contains(panel, 'Responsible owners', 'owner UI');
contains(panel, 'Approval history', 'approval history UI');
contains(panel, 'Reporting API contract', 'reporting API UI');
for (const api of moduleApis) contains(panel, api, 'frontend Group 2B API consumption');
assert(!/\boracle\b/i.test(panel), 'Primary Group 2B UI must not encode Oracle-specific assumptions.');

contains(css, '.group2b-resilience-shell', 'scoped enterprise styling');
contains(css, '.group2b-resilience-hero', 'enterprise hero styling');
contains(css, '@media (max-width: 620px)', 'mobile behavior');
contains(css, 'group2b-resilience-status.critical', 'readiness status styling');

for (const target of ['BackupDrCenter.jsx', 'RestoreValidationCenter.jsx', 'ReplicationSyncStatusCenter.jsx']) {
  contains(injection, target, 'Group 2B injection target');
}
assert(!injection.includes('App.jsx'), 'Group 2B injection must not rewrite App.jsx.');
assert(!injection.includes('main.jsx'), 'Group 2B injection must not rewrite main.jsx.');
contains(injection, 'GROUP_2B_PROVIDER_NEUTRAL_RESILIENCE_START', 'idempotent injection marker');

const prebuild = packageJson.scripts?.prebuild ?? '';
const predev = packageJson.scripts?.predev ?? '';
const build = packageJson.scripts?.build ?? '';
contains(prebuild, 'inject-group-2b-provider-neutral-resilience.mjs', 'prebuild Group 2B injection');
contains(predev, 'inject-group-2b-provider-neutral-resilience.mjs', 'predev Group 2B injection');
contains(build, 'validate:group2b-production-resilience', 'full frontend build Group 2B validation');
assert(packageJson.scripts?.['validate:group2b-production-resilience'] === 'node ./scripts/validate-group-2b-provider-neutral-production-resilience.mjs', 'Group 2B validator package script must be authoritative.');

execFileSync(process.execPath, [injectionPath], {
  cwd: webRoot,
  stdio: 'inherit'
});

const mounts = [
  ['BackupDrCenter.jsx', '014'],
  ['RestoreValidationCenter.jsx', '015'],
  ['ReplicationSyncStatusCenter.jsx', '017']
];
for (const [fileName, moduleCode] of mounts) {
  const source = read(path.join(sourceRoot, fileName));
  assert(count(source, "import PlatformResiliencePlanningPanel from './PlatformResiliencePlanningPanel.jsx';") === 1, `${fileName} must import the Group 2B panel exactly once after injection.`);
  assert(count(source, 'GROUP_2B_PROVIDER_NEUTRAL_RESILIENCE_START') === 1, `${fileName} must contain one Group 2B start marker.`);
  assert(count(source, `moduleCode="${moduleCode}" authSession={authSession}`) === 1, `${fileName} must mount Module ${moduleCode} exactly once.`);
}

console.log(`GROUP_2B_VALIDATION_CHECKS=${checks}`);
console.log(`GROUP_2B_FULL_REPOSITORY_CONTEXT=${fullRepositoryContext ? 'YES' : 'NO'}`);
console.log('GROUP_2B_PROVIDER_NEUTRAL_PRODUCTION_RESILIENCE=PASS');
