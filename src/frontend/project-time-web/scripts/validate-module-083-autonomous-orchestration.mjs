import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = fileURLToPath(new URL('../../../../', import.meta.url));
const rel = (value) => path.join(root, value);
const read = (value) => fs.readFileSync(rel(value), 'utf8');

const files = Object.freeze({
  foundation: 'src/backend/ProjectTime.Api/Modules/FullFutureLoopAutomationFoundation.cs',
  module: 'src/backend/ProjectTime.Api/Modules/FullFutureLoopAutomationModule.cs',
  bridge: 'src/backend/ProjectTime.Api/Modules/ReleaseDeploymentControlModule.cs',
  host: 'src/frontend/project-time-web/src/FullFutureLoopCenter.jsx',
  ui: 'src/frontend/project-time-web/src/FullFutureLoopAutomationCenter.jsx',
  uiStyle: 'src/frontend/project-time-web/src/full-future-loop-automation-center.css',
  migration: 'database/migrations/083_module_083_autonomous_control_plane.sql',
  rollback: 'database/rollback/083_module_083_autonomous_control_plane_rollback.sql',
  policySchema: 'schemas/full-future-loop/automation-policy.schema.json',
  manifestSchema: 'schemas/full-future-loop/release-manifest.schema.json',
  policyExample: 'config/full-future-loop/automation-policy.example.json',
  architecture: 'docs/modules/module-083-full-future-loop/AUTONOMOUS-CONTROL-PLANE.md',
  orchestration: 'docs/modules/module-083-full-future-loop/AUTONOMOUS-ORCHESTRATION.md',
  workflow: '.github/workflows/module-083-autonomous-control-plane-ci.yml'
});

let checks = 0;
let failures = 0;
function test(name, condition) {
  checks += 1;
  if (!condition) failures += 1;
  console.log(`MODULE_083_AUTONOMOUS_ORCHESTRATION_${name}=${condition ? 'PASSED' : 'FAILED'}`);
}

for (const [name, file] of Object.entries(files)) {
  test(`FILE_${name.toUpperCase()}`, fs.existsSync(rel(file)));
}

for (const jsonFile of [files.policySchema, files.manifestSchema, files.policyExample]) {
  try {
    JSON.parse(read(jsonFile));
    test(`JSON_${path.basename(jsonFile).replace(/\W/g, '_').toUpperCase()}`, true);
  } catch {
    test(`JSON_${path.basename(jsonFile).replace(/\W/g, '_').toUpperCase()}`, false);
  }
}

const foundation = read(files.foundation);
const module = read(files.module);
const bridge = read(files.bridge);
const host = read(files.host);
const ui = read(files.ui);
const uiStyle = read(files.uiStyle);
const migration = read(files.migration);
const rollback = read(files.rollback);
const workflow = read(files.workflow);
const manifestSchema = read(files.manifestSchema);
const documentation = read(files.architecture) + read(files.orchestration);

const endpoints = [
  '/readiness', '/policy', '/policy/simulate', '/adapters', '/runs',
  '/runs/dry-run', '/manifest', '/approvals', '/decision', '/runtime', '/evidence'
];
test('ENDPOINTS', endpoints.every((value) => module.includes(value)));
test('ENDPOINT_BRIDGE', bridge.includes('endpoints.MapFullFutureLoopAutomationEndpoints();') && bridge.split('MapFullFutureLoopAutomationEndpoints').length - 1 === 1);
test('POLICY_ENGINE_REUSED', module.includes('FullFutureLoopAutomationPolicyEngine.Evaluate') && module.includes('FullFutureLoopAutomationPolicy.EnterpriseDefault'));
test('DURABLE_TABLES', [
  'full_future_loop_automation_policies', 'full_future_loop_automation_state',
  'full_future_loop_automation_adapters', 'full_future_loop_automation_runs',
  'full_future_loop_automation_steps', 'full_future_loop_automation_approvals',
  'full_future_loop_release_manifests', 'full_future_loop_automation_evidence',
  'full_future_loop_outbox'
].every((value) => migration.includes(value) && module.includes(value)));
test('MIGRATION_ID', migration.includes("'083_module_083_autonomous_control_plane'") && module.includes('083_module_083_autonomous_control_plane'));
test('DRY_RUN_DATABASE_BOUNDARY', migration.includes('CHECK (dry_run=TRUE)') && migration.includes('CHECK (dry_run_only=TRUE)') && module.includes('externalExecutionEnabled = false'));
test('ADAPTER_MODE_BOUNDARY', migration.includes("adapter_mode IN ('disabled','dry_run')") && module.includes('ACTIVE_ADAPTER_MODE_NOT_AUTHORIZED'));
test('NO_EXTERNAL_CLIENTS', !/(HttpClient|Octokit|Azure\.|Process\.Start|System\.Diagnostics\.Process|RestClient|GraphServiceClient|SecretClient|ContainerAppsAPI)/.test(module));
test('NO_EXTERNAL_AI_CLIENT', !/(Anthropic|OpenAIClient|ChatClient|OllamaSharp)/.test(module));
test('NO_COMMAND_EXECUTION', !/(bash -c|powershell|cmd\.exe|kubectl|terraform apply|az containerapp)/i.test(module));
test('IDEMPOTENCY_AND_LEASES', migration.includes('idempotency_key') && migration.includes('lease_owner') && migration.includes('lease_expires_at') && module.includes('BuildIdempotencyKey'));
test('APPROVAL_SEPARATION_OF_DUTIES', module.includes('Separation of duties prevents the requesting user') && module.includes('runRequester == access!.EffectiveUserId'));
test('APPROVAL_GATES', ['production_environment_approval','migration_approval','security_approval','infrastructure_approval','secret_change_approval'].every((value) => foundation.includes(value)));
test('MANIFEST_EVIDENCE', ['SbomReference','ProvenanceReference','SignatureReference','RollbackArtifactDigests','ConfigurationFingerprint'].every((value) => foundation.includes(value) && module.includes(value)));
test('MANIFEST_SCHEMA_ALIGNED', ['buildWorkflow','buildRunId','buildRunAttempt','approvalEvidenceReferences','rollbackArtifactDigests'].every((value) => manifestSchema.includes(`"${value}"`)) && !manifestSchema.includes('"build": {'));
test('APPEND_ONLY', migration.includes('pulse083_immutable_automation_evidence') && migration.includes('BEFORE UPDATE OR DELETE ON full_future_loop_automation_policies') && migration.includes('BEFORE UPDATE OR DELETE ON full_future_loop_release_manifests') && migration.includes('BEFORE UPDATE OR DELETE ON full_future_loop_automation_evidence'));
test('VIEW_AS_READ_ONLY', module.includes('EnterpriseGovernanceResults.ViewAsReadOnly') && module.includes('access.IsViewAs'));
test('AI_NOT_AUTHORITY', foundation.includes('AI model cannot be the approving or requesting authority') && documentation.includes('cannot approve its own'));
test('KILL_SWITCH', migration.includes('global_kill_switch BOOLEAN NOT NULL DEFAULT TRUE') && module.includes('globalKillSwitch') && module.includes('UpdateRuntimeAsync'));
test('FAIL_CLOSED_DEFAULT_POLICY', migration.includes("'enterprise-default-v1'") && migration.includes('FALSE,TRUE,TRUE') && read(files.policyExample).includes('"globalKillSwitch": true'));
test('RBAC', [
  'VIEW_FULL_FUTURE_LOOP_AUTOMATION_083',
  'OPERATE_FULL_FUTURE_LOOP_AUTOMATION_083',
  'MANAGE_FULL_FUTURE_LOOP_AUTOMATION_083',
  'APPROVE_FULL_FUTURE_LOOP_AUTOMATION_083'
].every((value) => migration.includes(value) && module.includes(value)));
test('ROLLBACK_GUARDS', ['autonomous run evidence exists','approval evidence exists','immutable release manifests exist','append-only automation evidence exists','outbox records exist'].every((value) => rollback.includes(value)));
test('ROLLBACK_OWNERSHIP', rollback.includes('full_future_loop_083_role_grants') && rollback.includes('full_future_loop_083_permissions_created'));
test('UI_INTEGRATION', host.includes("import FullFutureLoopAutomationCenter from './FullFutureLoopAutomationCenter.jsx';") && host.includes('<FullFutureLoopAutomationCenter authSession={authSession} selectedLoopId={loop?.loopId || null} />'));
test('UI_CONTROL_SURFACES', ['Policy Simulator','Adapters','Runs & Manifests','Approvals','Evidence','Save governed runtime state','Create durable dry run','Register immutable manifest'].every((value) => ui.includes(value)));
test('UI_SAFETY_BOUNDARY', ui.includes('External execution: OFF') && ui.includes('No external execution was attempted') && ui.includes('ACTIVE_ADAPTER_MODE_NOT_AUTHORIZED') === false && !/(azure\/login@|az containerapp update|Octokit|HttpClient)/.test(ui));
test('UI_ACCESS_BOUNDARIES', ui.includes('permissions.canManage') && ui.includes('permissions.canOperateDryRuns') && ui.includes('permissions.canApprove') && ui.includes('separationOfDutiesSatisfied'));
test('UI_THEME_AND_RESPONSIVE', uiStyle.includes("[data-theme='dark'] .ffla-center") && uiStyle.includes('@media (max-width: 900px)') && uiStyle.includes('@media (max-width: 620px)'));
test('WORKFLOW_VALIDATION', workflow.includes('validate-module-083-autonomous-orchestration.mjs') && workflow.includes('dotnet build src/backend/ProjectTime.Api/ProjectTime.Api.csproj'));
test('WORKFLOW_FRONTEND_BUILD', workflow.includes('npm ci --no-fund') && workflow.includes('npm run build') && workflow.includes('FullFutureLoopAutomationCenter.jsx'));
const executableWorkflow = workflow
  .split(/\r?\n/)
  .filter((line) => !line.includes('grep -'))
  .join('\n');
test('SOURCE_ONLY_WORKFLOW',
  !/^\s*uses:\s*azure\/login@/m.test(executableWorkflow)
  && !/^\s*id-token:\s*write\s*$/m.test(executableWorkflow)
  && !/^\s*contents:\s*write\s*$/m.test(executableWorkflow)
  && !/^\s*environment:\s*(test|production)\s*$/m.test(executableWorkflow)
  && !/\baz containerapp update\b/.test(executableWorkflow)
  && !/\bpsql\b.*083_module/.test(executableWorkflow));
test('DOCUMENTED_ACTIVATION', documentation.includes('GitHub App') && documentation.includes('Azure') && documentation.includes('kill switch') && documentation.includes('dry-run'));
test('FOUNDATION_STILL_FAIL_CLOSED', foundation.includes('Enabled: false') && foundation.includes('GlobalKillSwitch: true') && foundation.includes('FullFutureLoopAdapterMode.Disabled'));

console.log(`MODULE_083_AUTONOMOUS_ORCHESTRATION_CHECKS=${checks}`);
console.log(`MODULE_083_AUTONOMOUS_ORCHESTRATION_CONTRACT=${failures ? 'FAILED' : 'PASSED'}`);
process.exitCode = failures ? 1 : 0;
