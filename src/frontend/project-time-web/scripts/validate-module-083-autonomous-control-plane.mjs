import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repositoryRoot = fileURLToPath(new URL('../../../../', import.meta.url));
const resolve = (relativePath) => path.join(repositoryRoot, relativePath);
const read = (relativePath) => fs.readFileSync(resolve(relativePath), 'utf8');

const files = Object.freeze({
  source: 'src/backend/ProjectTime.Api/Modules/FullFutureLoopAutomationFoundation.cs',
  policySchema: 'schemas/full-future-loop/automation-policy.schema.json',
  manifestSchema: 'schemas/full-future-loop/release-manifest.schema.json',
  policyExample: 'config/full-future-loop/automation-policy.example.json',
  architecture: 'docs/modules/module-083-full-future-loop/AUTONOMOUS-CONTROL-PLANE.md'
});

let checks = 0;
let failures = 0;
function test(name, condition) {
  checks += 1;
  if (!condition) failures += 1;
  console.log(`MODULE_083_AUTONOMY_${name}=${condition ? 'PASSED' : 'FAILED'}`);
}

for (const [name, relativePath] of Object.entries(files)) {
  test(`FILE_${name.toUpperCase()}`, fs.existsSync(resolve(relativePath)));
}

const source = read(files.source);
const architecture = read(files.architecture);
const policySchema = JSON.parse(read(files.policySchema));
const manifestSchema = JSON.parse(read(files.manifestSchema));
const policy = JSON.parse(read(files.policyExample));

const requiredSourceMarkers = [
  '083-autonomous-control-plane-foundation-v1',
  'FullFutureLoopAutomationPolicyEngine',
  'FullFutureLoopAutomationDisposition',
  'AutoExecute',
  'ApprovalRequired',
  'Blocked',
  'GlobalKillSwitch',
  'AllowedRepositories',
  'AllowedEnvironments',
  'AllowedOperations',
  'RequestedByAi',
  'production_environment_approval',
  'migration_approval',
  'security_approval',
  'infrastructure_approval',
  'secret_change_approval',
  'RollbackTargetProven',
  'ExactArtifactDigestsPresent',
  'SbomPresent',
  'ProvenancePresent',
  'SignaturesVerified',
  'EvidenceMaximumAge'
];
test('DETERMINISTIC_POLICY_CONTRACT', requiredSourceMarkers.every((marker) => source.includes(marker)));

test('FAIL_CLOSED_DEFAULTS',
  source.includes('Enabled: false')
  && source.includes('GlobalKillSwitch: true')
  && policy.enabled === false
  && policy.globalKillSwitch === true
  && policy.automaticActions.productionDeployment === false
  && policy.automaticActions.productionRollback === false);

test('TEST_AUTOMATION_BOUNDED',
  policy.allowedEnvironments.includes('test')
  && !policy.allowedEnvironments.includes('production')
  && policy.automaticActions.testDeployment === true
  && policy.automaticActions.testRollback === true);

test('HUMAN_AUTHORITY_GATES',
  policy.approvalRequirements.production === true
  && policy.approvalRequirements.migration === true
  && policy.approvalRequirements.security === true
  && policy.approvalRequirements.infrastructure === true
  && policy.approvalRequirements.secretChange === true
  && source.includes('An AI model cannot be the approving or requesting authority'));

test('SUPPLY_CHAIN_GATES',
  source.includes('A release mutation requires an SBOM reference')
  && source.includes('A release mutation requires build provenance')
  && source.includes('A release mutation requires verified artifact signatures')
  && source.includes('immutable artifact digests'));

test('CANARY_AND_ROLLBACK_GATES',
  source.includes('passing canary')
  && source.includes('canary cleanup')
  && source.includes('exact prior known-good target'));

test('NO_EXTERNAL_CLIENT_IN_FOUNDATION',
  !source.includes('HttpClient')
  && !source.includes('Octokit')
  && !source.includes('Azure.Identity')
  && !source.includes('Azure.ResourceManager')
  && !source.includes('Process.Start')
  && !source.includes('Npgsql'));

test('ADAPTERS_DISABLED_BY_DEFAULT',
  source.includes('FullFutureLoopAdapterMode.Disabled')
  && source.includes('IsReady: false')
  && source.includes('Not configured. The autonomous foundation remains dry-run and fail-closed.'));

test('POLICY_SCHEMA_IDENTITY',
  policySchema.$schema === 'https://json-schema.org/draft/2020-12/schema'
  && policySchema.title === 'Pulse Full Future Loop Automation Policy'
  && policySchema.additionalProperties === false);

test('MANIFEST_SCHEMA_IDENTITY',
  manifestSchema.$schema === 'https://json-schema.org/draft/2020-12/schema'
  && manifestSchema.title === 'Pulse Full Future Loop Release Manifest'
  && manifestSchema.additionalProperties === false);

test('EXACT_SOURCE_AND_DIGEST_SCHEMA',
  manifestSchema.properties.sourceCommit.pattern === '^[0-9a-f]{40}$'
  && manifestSchema.properties.artifacts.items.properties.digest.pattern === '^sha256:[0-9a-f]{64}$'
  && manifestSchema.properties.configurationFingerprint.pattern === '^[0-9a-f]{64}$');

test('RELEASE_EVIDENCE_SCHEMA',
  ['artifacts', 'migrations', 'canaryEvidenceReferences', 'verificationEvidenceReferences',
    'approvalEvidenceReferences', 'rollbackArtifactDigests', 'configurationFingerprint']
    .every((field) => manifestSchema.required.includes(field)));

test('REPOSITORY_STRATEGY',
  architecture.includes('A new repository is not required')
  && architecture.includes('separate private repository may be introduced later')
  && architecture.includes('must not imply that private enterprise source becomes publicly visible'));

test('ENTERPRISE_SAFEGUARDS', [
  'least-privilege GitHub App',
  'separate Test and Production GitHub Environments',
  'separate Azure federated identities',
  'global automation kill switch',
  'append-only action, approval, evidence, and policy-decision history',
  'idempotency keys',
  'database leases'
].every((marker) => architecture.includes(marker)));

test('PR600_ISOLATION',
  architecture.includes('must not:\n\n- merge or modify PR #600')
  && architecture.includes('deploy to Test or Production')
  && architecture.includes('create or change Azure resources')
  && architecture.includes('create or reveal secrets'));

console.log(`MODULE_083_AUTONOMY_VALIDATION_CHECKS=${checks}`);
console.log(`MODULE_083_AUTONOMY_CONTRACT=${failures ? 'FAILED' : 'PASSED'}`);
process.exitCode = failures ? 1 : 0;
