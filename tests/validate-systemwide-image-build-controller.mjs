import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';

const workflowPath = '.github/workflows/projectpulse-deploy-test.yml';
const retiredWorkflowPath = '.github/workflows/systemwide-enterprise-reliability-test-deployment.yml';
const apiProjectPath = 'src/backend/ProjectTime.Api/ProjectTime.Api.csproj';
const apiBuildPropsPath = 'src/backend/ProjectTime.Api/Directory.Build.props';
const sourceRevisionPath = 'src/backend/ProjectTime.Api/.projectpulse-source-revision';
const revisionWaitPath = 'scripts/wait-containerapp-ready-revision.sh';
const assignedWorkUatPath = 'scripts/release-test/run-assigned-work-protected-test-uat.sh';
const module025UatPath = 'scripts/release-test/run-module025-sow-gsd-protected-test-uat.sh';
const module025UatAccessPath = 'src/backend/ProjectTime.Api/Modules/Module025ProtectedTestUatAccess.cs';
const module025ModulePath = 'src/backend/ProjectTime.Api/Modules/Module025SowGsdModule.cs';
const apiProgramPath = 'src/backend/ProjectTime.Api/Program.cs';
const module025WorkspacePath = 'src/frontend/project-time-web/src/module025/SowGsdWorkspace.jsx';
const celarContractsPath = 'src/backend/ProjectTime.Api/Ai/CelarAiEnterprisePlatformContracts.cs';
const celarServicePath = 'src/backend/ProjectTime.Api/Ai/CelarAiEnterprisePlatformService.cs';
const privateRagContractsPath = 'src/backend/ProjectTime.Api/Ai/PulseAiPrivateRagContracts.cs';
const privateRagServicePath = 'src/backend/ProjectTime.Api/Ai/PulseAiPrivateRagService.cs';
const privateRagRepositoryPath = 'src/backend/ProjectTime.Api/Ai/PulseAiPrivateRagRepository.cs';
const privateModelClientPath = 'src/backend/ProjectTime.Api/Ai/PulseAiPrivateModelClient.cs';
const aiServicesPath = 'src/backend/ProjectTime.Api/Ai/ProjectPulseAiServiceCollectionExtensions.cs';
const webProxyPath = 'deployment/containers/web/default.conf.template';
const module033WorkflowPath = '.github/workflows/module033-project-forge-ci.yml';
const deepIntelligenceWorkflowPath = '.github/workflows/deep-intelligence-read-contract-ci.yml';
const celarSourceBoundaryPath = 'scripts/ci/validate-celar-ai-enterprise-source-boundary.sh';
const releaseControllerValidatorPath = '.github/workflows/projectpulse-release-test-control-ci.yml';
const releaseControllerReregisteredValidatorPath = '.github/workflows/projectpulse-release-test-control-ci-reregistered.yml';
const celarPr630ValidatorPath = 'tests/validate-celar-ai-pr630-consolidated.mjs';
const module001bFixturePath = 'src/backend/ProjectTime.Api/Modules/Module001BProtectedTestUatFixtureModule.cs';
const documentAuthorityMigrationBuilderPath = 'scripts/release-test/build-and-run-project-planning-document-authority-migration-job.sh';
const workflow = fs.readFileSync(workflowPath, 'utf8');
const apiProject = fs.readFileSync(apiProjectPath, 'utf8');
const apiBuildProps = fs.readFileSync(apiBuildPropsPath, 'utf8');
const revisionWait = fs.readFileSync(revisionWaitPath, 'utf8');
const assignedWorkUat = fs.readFileSync(assignedWorkUatPath, 'utf8');
const module025Uat = fs.readFileSync(module025UatPath, 'utf8');
const module025UatAccess = fs.readFileSync(module025UatAccessPath, 'utf8');
const module025Module = fs.readFileSync(module025ModulePath, 'utf8');
const apiProgram = fs.readFileSync(apiProgramPath, 'utf8');
const module025Workspace = fs.readFileSync(module025WorkspacePath, 'utf8');
const celarContracts = fs.readFileSync(celarContractsPath, 'utf8');
const celarService = fs.readFileSync(celarServicePath, 'utf8');
const privateRagContracts = fs.readFileSync(privateRagContractsPath, 'utf8');
const privateRagService = fs.readFileSync(privateRagServicePath, 'utf8');
const privateRagRepository = fs.readFileSync(privateRagRepositoryPath, 'utf8');
const privateModelClient = fs.readFileSync(privateModelClientPath, 'utf8');
const aiServices = fs.readFileSync(aiServicesPath, 'utf8');
const webProxy = fs.readFileSync(webProxyPath, 'utf8');
const module033Workflow = fs.readFileSync(module033WorkflowPath, 'utf8');
const deepIntelligenceWorkflow = fs.readFileSync(deepIntelligenceWorkflowPath, 'utf8');
const celarSourceBoundary = fs.readFileSync(celarSourceBoundaryPath, 'utf8');
const releaseControllerValidator = fs.readFileSync(releaseControllerValidatorPath, 'utf8');
const releaseControllerReregisteredValidator = fs.readFileSync(releaseControllerReregisteredValidatorPath, 'utf8');
const celarPr630Validator = fs.readFileSync(celarPr630ValidatorPath, 'utf8');
const module001bFixture = fs.readFileSync(module001bFixturePath, 'utf8');
const documentAuthorityMigrationBuilder = fs.readFileSync(documentAuthorityMigrationBuilderPath, 'utf8');

assert.equal(
  fs.existsSync(retiredWorkflowPath),
  false,
  'the unregistered duplicate protected-Test deployment workflow must remain retired'
);
assert.doesNotMatch(workflow, /\bfull_imae\b/);
assert.doesNotMatch(
  workflow,
  /local\s+repository="\$1"[^\n]*\bimage="\$repository:/,
  'image must not reference repository in the same local declaration under set -u'
);
assert.match(
  workflow,
  /local repository="\$1" dockerfile="\$2" context="\$3"\s*\n\s*local image="\$\{repository\}:\$\{UNIQUE_TAG\}"/
);
assert.match(workflow, /group: projectpulse-deploy-test/);
assert.match(workflow, /queue: max/);
assert.match(workflow, /cancel-in-progress: false/);
assert.match(workflow, /dockerfile_for_acr="\$\{dockerfile_abs#"\$context_abs"\/\}"/);
assert.match(workflow, /--file "\$dockerfile_for_acr"/);
assert.match(workflow, /docker build --file "\$dockerfile_abs" --tag "\$full_image" "\$context_abs"/);
assert.match(workflow, /docker push "\$full_image"/);
assert.match(workflow, /BUILD_LOG="\$EVIDENCE_DIR\/image-build\.log"/);
assert.match(workflow, /node tests\/validate-systemwide-image-build-controller\.mjs/);
assert.match(workflow, /run-utilization-role-scoping-protected-test-uat\.sh/);
assert.match(workflow, /Run protected-Test Module 025 SOW\/GSD generation lifecycle UAT/);
assert.match(workflow, /Enable exact-run Module 025 protected-Test authorization fixture/);
assert.match(workflow, /Disable exact-run Module 025 protected-Test authorization fixture/);
assert.doesNotMatch(workflow, /TEST_APPLICATION_GATEWAY_/);
assert.doesNotMatch(workflow, /az network application-gateway/);
assert.match(workflow, /productionMutation:false/);
assert.match(workflow, /PROJECTPULSE_MODULE025_PROTECTED_TEST_UAT_ENABLED=true/);
assert.match(workflow, /PROJECTPULSE_MODULE025_PROTECTED_TEST_UAT_ENABLED=false/);
assert.match(workflow, /PROJECTPULSE_MODULE025_PROTECTED_TEST_UAT_RUN_ID="\$\{GITHUB_RUN_ID\}-\$\{GITHUB_RUN_ATTEMPT\}"/);
assert.match(workflow, /persistentRoleAssignmentMutation:false/);
assert.match(workflow, /run-module025-sow-gsd-protected-test-uat\.sh/);
assert.match(workflow, /module025-sow-gsd-protected-test-uat\.json/);
assert.match(module025Uat, /https:\/\/phd-west-test\.onenecklab\.com/);
assert.match(module025Uat, /\.access\.isSolutionArchitect == true/);
assert.match(module025Uat, /module025_detailed_scope_generation_queued/);
assert.match(module025Uat, /module025_detailed_scope_generated/);
assert.match(module025Uat, /\/generations\/\$GENERATION_ID/);
assert.match(module025Uat, /\.terminal == true/);
assert.match(module025Uat, /\["plan","design","implement","validate","release"\]/);
assert.match(module025Uat, /\.engagement\.status == "review_ready"/);
assert.match(module025Uat, /module025_archived/);
assert.match(module025Uat, /productionMutation:false/);
assert.match(module025Uat, /demo\.manager@ussignal\.local/);
assert.match(module025Uat, /X-ProjectPulse-Module025-Uat-Run/);
assert.match(module025Uat, /protectedTestUatRoleFixture == true/);
assert.match(module025Uat, /module025-generate-response-headers\.txt/);
assert.match(module025Uat, /generationQueueElapsedSeconds/);
assert.match(module025Uat, /generationTotalElapsedSeconds/);
assert.match(module025Uat, /generationPollAttempts/);
assert.match(module025Uat, /generationResponseServer/);
assert.match(module025Uat, /seq 1 180/);
assert.match(module025Uat, /terminal state within 15 minutes/);
assert.match(module025UatAccess, /PROJECTPULSE_MODULE025_PROTECTED_TEST_UAT_ENABLED/);
assert.match(module025UatAccess, /PROJECTPULSE_MODULE025_PROTECTED_TEST_UAT_RUN_ID/);
assert.match(module025UatAccess, /PROJECTPULSE_MODULE025_PROTECTED_TEST_UAT_SOURCE_COMMIT/);
assert.match(module025UatAccess, /PROJECTPULSE_MODULE025_PROTECTED_TEST_UAT_EXPIRES_AT/);
assert.match(module025UatAccess, /expiresAt <= now/);
assert.match(module025UatAccess, /expiresAt > now \+ 3_600/);
assert.match(module025UatAccess, /phd-west-test\.onenecklab\.com/);
assert.match(module025UatAccess, /actualUserId == effectiveUserId/);
assert.match(module025UatAccess, /!ProjectPulseActualSessionAuthority\.IsViewAs\(context\)/);
assert.match(module025UatAccess, /roles\.Contains\("MANAGER"\)/);
assert.match(module025UatAccess, /!roles\.Overlaps/);
assert.doesNotMatch(module025UatAccess, /INSERT|UPDATE|DELETE|app_user_role_assignments/i);
assert.match(
  celarContracts,
  /internal sealed record CelarAiAuthoritativeScopeEvidence/,
  'Module 025 saved-scope evidence must remain server-internal and unavailable to public request binding'
);
assert.match(module025Module, /ComposeModule025SowAsync\(/);
assert.match(module025Module, /new CelarAiAuthoritativeScopeEvidence\(/);
assert.match(module025Module, /generations\/\{generationId:guid\}/);
assert.match(module025Module, /ai_generation_queued/);
assert.match(module025Module, /ai_generation_started/);
assert.match(module025Module, /ai_generation_completed/);
assert.match(module025Module, /ProcessNextQueuedGenerationAsync/);
assert.match(module025Module, /pg_try_advisory_lock\(hashtextextended\(@generation_id::text,725\)\)/);
assert.match(module025Module, /WorkerLockConnectionString/);
assert.match(module025Module, /KeepAlive = 30/);
assert.match(module025Module, /RecordGenerationStartedAsync\(connectionString/);
assert.match(module025Module, /RecordGenerationTerminalAsync\(\s*connectionString/);
assert.doesNotMatch(module025Module, /IValueHttpResult|IStatusCodeHttpResult/);
assert.match(apiProgram, /AddHostedService<Module025SowGsdGenerationWorker>\(\)/);
assert.match(module025Workspace, /waitForDetailedScopeGeneration/);
assert.match(module025Workspace, /\/generations\/\$\{generationId\}/);
assert.match(module025Workspace, /payload\?\.terminal === true/);
assert.match(celarService, /authoritativeScopeEvidence: null/);
assert.match(celarService, /internal Task<CelarAiComposeResult> ComposeModule025SowAsync/);
assert.match(celarService, /GenerateModule025SowPlanAsync\(/);
assert.match(privateRagContracts, /string SourceType = "project_document"/);
assert.match(privateRagContracts, /string SourceModule = "011"/);
assert.match(privateRagService, /CreateModule025AuthoritativeScopeSource/);
assert.match(privateRagService, /SourceType: "module025_saved_service_overview"/);
assert.match(privateRagService, /SourceModule: "025"/);
assert.match(privateRagService, /RetrievalMode: "direct_knowledge"/);
assert.match(privateRagService, /hasModule025AuthoritativeScope: authoritativeSource is not null/);
assert.match(
  privateRagService,
  /AllowsDeterministicCitedPlanningFallback[\s\S]*?ProjectFlowHivePlan[\s\S]*?ProjectForgePlanEstimate;/,
  'only FlowHive and Project Forge may use deterministic cited planning fallback'
);
assert.doesNotMatch(
  privateRagService.match(/private static bool AllowsDeterministicCitedPlanningFallback[\s\S]*?;/)?.[0] ?? '',
  /SowGsdPlanning/,
  'Module 025 SOW generation must fail closed when the approved private model does not complete'
);
assert.match(privateRagService, /Module025SowMaximumOutputTokens = 1_000/);
assert.match(
  privateRagService,
  /Math\.Min\(options\.MaximumOutputTokens, Module025SowMaximumOutputTokens\)/
);
assert.match(privateRagService, /Return between one and three cited scope work-package tasks/);
assert.match(privateRagService, /Keep the complete JSON below 3,500 characters/);
assert.match(privateRagService, /[Dd]o not repeat or restate the incoming request/);
assert.match(privateRagService, /ExpandModule025CitedScopeTasks/);
assert.match(privateRagService, /expandModule025CitedPhases: authoritativeSource is not null/);
assert.match(privateRagService, /A deterministic, citation-preserving composer expands/);
assert.match(
  privateRagService,
  /Name = "Plan"[\s\S]*?Name = "Design"[\s\S]*?Name = "Implement"[\s\S]*?Name = "Validate"[\s\S]*?Name = "Release"/,
  'the governed Module 025 composer must expand cited scope through exact P/D/I/V/R order'
);
assert.match(
  privateRagService,
  /query\.FeatureCode == CelarAiCapabilityCatalog\.SowGsdPlanning[\s\S]*?\? 0\.05m[\s\S]*?: flowHive/
);
assert.match(privateRagService, /hasExecutableDetail/);
assert.doesNotMatch(
  privateRagService,
  /return string\.Equals\(task\.Name\?\.Trim\(\), phase, StringComparison\.OrdinalIgnoreCase\);/,
  'a substantive private-model task must not be discarded solely because its name matches its phase'
);
assert.match(aiServices, /AddHttpClient\("PulseAiPrivateSowInference"/);
assert.match(aiServices, /PulseAiPrivateSowInference[\s\S]*?TimeSpan\.FromMinutes\(12\)/);
assert.match(privateModelClient, /CelarAiCapabilityCatalog\.SowGsdPlanning[\s\S]*?"PulseAiPrivateSowInference"/);
assert.match(privateModelClient, /private_model_output_truncated/);
assert.match(privateModelClient, /ReadFinishReason/);
assert.match(module025Module, /CompositionDiagnosticCode/);
assert.match(module025Module, /private_sow_work_packages_missing/);
assert.match(module025Module, /private_sow_phase_coverage_incomplete/);
assert.match(module025Module, /Missing phase coverage:/);
assert.match(
  module025Module,
  /var normalizedPhase = phase\?\.Trim\(\)\.ToLowerInvariant\(\)[\s\S]*?PhaseCodes\.Contains\(normalizedPhase[\s\S]*?return normalizedPhase;[\s\S]*?var value = \$"\{phase\} \{name\} \{description\}"/,
  'an exact governed phase value must win before description heuristics are considered'
);
assert.match(privateRagRepository, /citation\.SourceType,[\s\S]*?"module025_saved_service_overview"/);
assert.match(privateRagRepository, /@answer_run_id,NULL,NULL,NULL,NULL,@source_type,@source_module/);
assert.doesNotMatch(webProxy, /proxy_read_timeout 230s;/);
assert.doesNotMatch(webProxy, /location ~ "\^\/api\/module025\/sow-gsd\/[\s\S]*?\/generate\$"/);
assert.match(
  webProxy,
  /location \/api\/ \{[\s\S]*?proxy_read_timeout 60s;/,
  'durable Module 025 generation must use the bounded generic API proxy window'
);
for (const source of [module033Workflow, celarSourceBoundary, celarPr630Validator]) {
  assert.match(
    source,
    /fix\/module025-protected-uat-generation-verification-/,
    'legacy CI compatibility must remain restricted to the reviewed Module 025 repair branch'
  );
}
assert.match(celarSourceBoundary, /CELAR_AI_MODULE025_PROTECTED_UAT_BOUNDARY=PASSED/);
assert.match(celarSourceBoundary, /CELAR_AI_MODULE025_DURABLE_WORKER_REPAIR_BOUNDARY=PASSED/);
assert.match(celarSourceBoundary, /CELAR_AI_MODULE025_COMPACT_PRIVATE_PLAN_BOUNDARY=PASSED/);
assert.match(celarSourceBoundary, /CELAR_AI_MODULE025_SUBSTANTIVE_PHASE_TASK_BOUNDARY=PASSED/);
assert.match(celarSourceBoundary, /CELAR_AI_MODULE025_EXACT_PHASE_AUTHORITY_BOUNDARY=PASSED/);
assert.match(celarSourceBoundary, /CELAR_AI_MODULE025_BOUNDED_PRIVATE_OUTPUT_BOUNDARY=PASSED/);
assert.match(celarSourceBoundary, /CELAR_AI_MODULE025_CITED_PHASE_EXPANSION_BOUNDARY=PASSED/);
assert.match(celarSourceBoundary, /! grep -Fq 'phd-west\.onenecklab\.com'/);
for (const controllerValidator of [releaseControllerValidator, releaseControllerReregisteredValidator]) {
  assert.match(controllerValidator, /\.github\/workflows\/deep-intelligence-read-contract-ci\.yml/);
  assert.match(controllerValidator, /src\/backend\/ProjectTime\.Api\/Ai\/ProjectPulseAiServiceCollectionExtensions\.cs/);
  assert.match(controllerValidator, /src\/backend\/ProjectTime\.Api\/Ai\/PulseAiPrivateModelClient\.cs/);
}
assert.match(deepIntelligenceWorkflow, /'\/api\/project-flowhive\/projects\/'/);
assert.match(deepIntelligenceWorkflow, /'\/ai-planner\/runs'/);
assert.doesNotMatch(deepIntelligenceWorkflow, /assert_js_marker[\s\S]{0,1200}'\/api\/project-flowhive\/ai\/production-generate'/);
assert.match(module033Workflow, /MODULE_033_MODULE025_ASYNC_PROXY_REGRESSION_BOUNDARY=PASSED/);
assert.match(celarPr630Validator, /CELAR_PR630_MODULE025_PROTECTED_UAT_COMPATIBILITY=PASS/);
assert.match(workflow, /093_assigned_work_canonical_visibility_repair\.sql/);
assert.match(workflow, /097_project_planning_identity_safe_admission\.sql/);
assert.match(workflow, /097_project_planning_identity_safe_admission_rollback\.sql/);
assert.match(workflow, /test-project-planning-identity-safe-admission-migration-097\.sh/);
assert.match(workflow, /test-pulse-ai-runtime-job-query-shape\.sh/);
assert.match(workflow, /Apply and verify Migrations 086, 088, 093, 094, 095, 096, and 097 inside Test private network/);
assert.match(workflow, /MIGRATION_093=APPLIED_AND_VERIFIED/);
assert.match(workflow, /migration097:"applied_and_verified"/);
assert.match(
  workflow,
  /\(NOT EXISTS \(\s*SELECT 1\s*FROM work_register_task_assignment_history history[\s\S]*?\)\)::text;/,
  'Migration 093 verification must cast the complete NOT EXISTS predicate after boolean evaluation'
);
assert.doesNotMatch(
  workflow,
  /\n\s{12}NOT EXISTS \(\s*SELECT 1\s*FROM work_register_task_assignment_history history/,
  'Migration 093 verification must not apply NOT to a text-cast EXISTS result'
);

assert.match(
  revisionWait,
  /MODULE001B_PROTECTED_TEST_RECONCILE=false/,
  'Module 001B reconciliation must remain opt-in rather than changing the generic readiness helper'
);
assert.match(
  revisionWait,
  /\*--m1be-\*\|\*--m1bd-\*/,
  'only Module 001B protected-Test fixture revisions may enter stale-revision reconciliation'
);
assert.match(
  revisionWait,
  /Refusing Module 001B revision reconciliation because the Container App is not tagged Test\./,
  'Module 001B reconciliation must fail closed outside the Test-tagged Container App'
);
assert.match(
  revisionWait,
  /Refusing Module 001B revision reconciliation because the Container App is not in Single revision mode\./,
  'Module 001B reconciliation must require Single revision mode'
);
assert.match(
  revisionWait,
  /Refusing Module 001B revision reconciliation because a different revision is now latest:/,
  'Module 001B reconciliation must refuse to mutate after a concurrent newer revision appears'
);
assert.match(
  revisionWait,
  /az containerapp revision deactivate/,
  'Module 001B protected-Test reconciliation must deactivate stale active revisions explicitly'
);
assert.doesNotMatch(
  revisionWait,
  /if \[\[ "\$ACTIVE" == true \]\]; then/,
  'Module 001B reconciliation must not require the newly-ready revision to be active before deactivating stale revisions'
);
assert.match(
  revisionWait,
  /requiring ACTIVE=true here creates a circular wait/,
  'Module 001B reconciliation must document the ready-but-inactive Single-mode handoff'
);
assert.match(
  revisionWait,
  /singleRevisionConverged=true/,
  'Module 001B reconciliation must prove sole-active convergence before returning success'
);
assert.doesNotMatch(
  revisionWait,
  /containerapp ingress traffic set/,
  'Module 001B reconciliation must not restore manual ingress traffic manipulation'
);
assert.doesNotMatch(
  revisionWait,
  /--revision-weight/,
  'Module 001B reconciliation must not restore revision-weight manipulation'
);
const revisionWaitSyntax = spawnSync('bash', ['-n', revisionWaitPath], { encoding: 'utf8' });
assert.equal(revisionWaitSyntax.status, 0, revisionWaitSyntax.stderr);

for (const [label, source] of [
  ['generic revision waiter', revisionWait],
  ['Module 001B convergence waiter', assignedWorkUat]
]) {
  assert.doesNotMatch(
    source,
    /<<<"\$\{[A-Za-z_][A-Za-z0-9_]*:-\{\}\}"/,
    `${label} must not append a closing brace to non-empty Azure JSON through an ambiguous Bash object fallback`
  );
}
assert.match(
  revisionWait,
  /jq -e 'type == "object"' <<<"\$REVISION_JSON"/,
  'the generic revision waiter must normalize Azure revision JSON before extracting fields'
);
assert.match(
  assignedWorkUat,
  /jq -e 'type == "object"' <<<"\$app_json"/,
  'the Module 001B convergence waiter must normalize the Container App object'
);
assert.match(
  assignedWorkUat,
  /jq -e 'type == "array"' <<<"\$revisions_json"/,
  'the Module 001B convergence waiter must normalize the revision array'
);
assert.doesNotMatch(
  module001bFixture,
  /DELETE FROM scoped_time_management_events/,
  'Module 001B fixture cleanup must preserve immutable time-management audit evidence'
);
assert.doesNotMatch(
  module001bFixture,
  /DELETE FROM module001_timesheet_entry_associations/,
  'Module 001B fixture cleanup must rely on the entry-association ON DELETE CASCADE contract'
);
assert.match(
  module001bFixture,
  /entry-association foreign key is ON DELETE CASCADE[\s\S]*?immutable steward events are deliberately[\s\S]*?retained/,
  'Module 001B fixture cleanup must document its immutable-evidence and cascading-association contract'
);
assert.match(
  module001bFixture,
  /NOT EXISTS \(\s*SELECT 1\s*FROM scoped_time_management_events audit\s*WHERE audit\.timesheet_id = t\.timesheet_id\s*\)/,
  'Module 001B fixture cleanup must retain an empty fixture timesheet while immutable audit evidence references it'
);
assert.match(
  module001bFixture,
  /FROM module001a_engineer_task_closeouts closeout[\s\S]*?closeout\.engineer_user_id = @target_user_id[\s\S]*?closeout\.closeout_status IN \('engineer_closed', 'ptc_final_closed'\)/,
  'Module 001B fixture creation must not select a source task closed for the target engineer'
);
assert.match(
  module001bFixture,
  /source\.Parameters\.AddWithValue\("target_user_id", targetUserId\.Value\)/,
  'Module 001B fixture source selection must bind the protected-Test target user explicitly'
);

const convergenceFunctionStart = assignedWorkUat.indexOf('module001b_wait_single_revision_converged() {');
const convergenceFunctionEnd = assignedWorkUat.indexOf('\nmodule001b_disable_gate() {', convergenceFunctionStart);
assert.ok(convergenceFunctionStart >= 0, 'the Module 001B convergence function must exist');
assert.ok(convergenceFunctionEnd > convergenceFunctionStart, 'the Module 001B convergence function boundary must remain testable');
const convergenceFunction = assignedWorkUat.slice(convergenceFunctionStart, convergenceFunctionEnd);

const mockRoot = fs.mkdtempSync(path.join(os.tmpdir(), 'module001b-convergence-'));
try {
  const mockAzPath = path.join(mockRoot, 'az');
  fs.writeFileSync(mockAzPath, [
    '#!/usr/bin/env bash',
    'set -Eeuo pipefail',
    'case "$*" in',
    '  *"--query properties.latestReadyRevisionName"*) printf "%s\\n" "$MOCK_EXPECTED_REVISION" ;;',
    '  *"containerapp revision show"*) printf "%s\\n" "$MOCK_REVISION_SHOW_JSON" ;;',
    '  *"containerapp revision list"*) printf "%s\\n" "$MOCK_REVISION_LIST_JSON" ;;',
    '  *"containerapp show"*) printf "%s\\n" "$MOCK_APP_JSON" ;;',
    '  *"containerapp revision activate"*|*"containerapp revision deactivate"*) exit 0 ;;',
    '  *) printf "unexpected mock az invocation: %s\\n" "$*" >&2; exit 64 ;;',
    'esac',
    ''
  ].join('\n'), { encoding: 'utf8', mode: 0o755 });

  const expectedRevision = 'ca-protected-test-api--m1be-regression-1';
  const expectedImage = 'acr.example.invalid/project-time-api@sha256:1234567890abcdef';
  const mockEnv = {
    ...process.env,
    PATH: `${mockRoot}:${process.env.PATH ?? ''}`,
    MOCK_EXPECTED_REVISION: expectedRevision,
    MOCK_EXPECTED_IMAGE: expectedImage,
    MOCK_APP_JSON: JSON.stringify({
      tags: { environment: 'test' },
      properties: {
        configuration: { activeRevisionsMode: 'Single' },
        latestRevisionName: expectedRevision,
        latestReadyRevisionName: expectedRevision
      }
    }),
    MOCK_REVISION_LIST_JSON: JSON.stringify([{
      name: expectedRevision,
      properties: {
        active: true,
        provisioningState: 'Provisioned',
        healthState: 'Healthy',
        trafficWeight: 100,
        template: { containers: [{ image: expectedImage }] }
      }
    }]),
    MOCK_REVISION_SHOW_JSON: JSON.stringify({
      image: expectedImage,
      provisioningState: 'Provisioned',
      healthState: 'Healthy',
      active: true,
      trafficWeight: 100
    })
  };

  const genericWaitBehavior = spawnSync(
    'bash',
    [revisionWaitPath, 'protected-test-rg', 'protected-test-api', expectedRevision, expectedImage, '1', '1'],
    { encoding: 'utf8', env: mockEnv }
  );
  assert.equal(genericWaitBehavior.status, 0, genericWaitBehavior.stderr);
  assert.match(genericWaitBehavior.stdout, /CONTAINERAPP_CANDIDATE_READY[\s\S]*singleRevisionConverged=true/);
  assert.doesNotMatch(genericWaitBehavior.stderr, /jq: (?:parse )?error/i);

  const convergenceHarness = [
    'set -Eeuo pipefail',
    'MODULE001B_API_RG=protected-test-rg',
    'MODULE001B_API_APP=protected-test-api',
    'MODULE001B_API_IMAGE="$MOCK_EXPECTED_IMAGE"',
    convergenceFunction,
    'module001b_wait_single_revision_converged enabled "$MOCK_EXPECTED_REVISION"',
    ''
  ].join('\n');
  const module001bWaitBehavior = spawnSync('bash', ['-c', convergenceHarness], {
    encoding: 'utf8',
    env: mockEnv
  });
  assert.equal(module001bWaitBehavior.status, 0, module001bWaitBehavior.stderr);
  assert.match(module001bWaitBehavior.stderr, /revisionActive=true imageMatch=true/);
  assert.match(module001bWaitBehavior.stderr, /MODULE001B_SINGLE_REVISION_CONVERGED label=enabled/);
  assert.doesNotMatch(module001bWaitBehavior.stderr, /jq: (?:parse )?error/i);
  assert.doesNotMatch(module001bWaitBehavior.stderr, /revisionActive=true\s+false imageMatch=true/);
} finally {
  fs.rmSync(mockRoot, { recursive: true, force: true });
}

assert.match(
  documentAuthorityMigrationBuilder,
  /099_module025_sow_gsd_workspace\.sql/,
  'the governed private-network migration image must carry Module 025 migration 099'
);
assert.match(
  documentAuthorityMigrationBuilder,
  /psql -X -v ON_ERROR_STOP=1 --file "\$MODULE025_MIGRATION"/,
  'the governed private-network migration job must execute Module 025 migration 099'
);
assert.match(
  documentAuthorityMigrationBuilder,
  /module025_verification=/,
  'the governed migration job must verify Module 025 workspace schema readiness'
);
assert.match(
  documentAuthorityMigrationBuilder,
  /to_regclass\('public\.module025_sow_gsd_engagements'\)/,
  'Module 025 migration verification must prove the engagement workspace table exists'
);
assert.match(
  documentAuthorityMigrationBuilder,
  /MIGRATION_099_MODULE025_SOW_GSD=APPLIED_AND_VERIFIED/,
  'the governed migration job must publish an explicit migration 099 success marker'
);
assert.match(
  documentAuthorityMigrationBuilder,
  /migration-099\.json/,
  'the governed migration job must emit migration 099 release evidence'
);

assert.match(
  apiProject,
  /<AssemblyMetadata Include="ProjectPulseSourceRevision" Value="\$\(ProjectPulseSourceRevision\)" \/>/,
  'API assembly metadata must remain bound to the ProjectPulseSourceRevision MSBuild property'
);
assert.match(
  apiBuildProps,
  /Exists\('\$\(MSBuildProjectDirectory\)\/\.projectpulse-source-revision'\)/,
  'API build props must activate exact source binding only when the temporary release revision file exists'
);
assert.match(
  apiBuildProps,
  /<ProjectPulseSourceRevision>\$\(\[System\.IO\.File\]::ReadAllText\('\$\(MSBuildProjectDirectory\)\/\.projectpulse-source-revision'\)\)<\/ProjectPulseSourceRevision>/,
  'API build props must read the exact staged release SHA into ProjectPulseSourceRevision'
);

const releaseCommit = (process.env.TARGET_RELEASE_COMMIT ?? '').trim().toLowerCase();
if (releaseCommit.length > 0) {
  assert.match(
    releaseCommit,
    /^[0-9a-f]{40}$/,
    'the governed Protected-Test controller must supply an exact 40-character release SHA'
  );
  assert.equal(
    fs.existsSync(sourceRevisionPath),
    false,
    'the temporary source-revision file must not exist in reviewed source'
  );
  fs.writeFileSync(sourceRevisionPath, releaseCommit, { encoding: 'utf8', mode: 0o444 });
  assert.equal(
    fs.readFileSync(sourceRevisionPath, 'utf8'),
    releaseCommit,
    'the temporary source-revision file must contain only the exact governed release SHA'
  );
  console.log(`SYSTEMWIDE_API_SOURCE_REVISION_STAGED=${releaseCommit}`);
}

const migrationImageBuilders = [
  ['094', 'scripts/release-test/build-and-run-flowhive-authority-migration-094-job.sh'],
  ['095', 'scripts/release-test/build-and-run-project-planning-collaboration-migration-job.sh'],
  ['096+097+098+099', documentAuthorityMigrationBuilderPath]
];
for (const [migration, builderPath] of migrationImageBuilders) {
  const builder = fs.readFileSync(builderPath, 'utf8');
  assert.match(
    builder,
    /DOCKERFILE="\$CONTEXT\/Dockerfile"/,
    `Migration ${migration} must bind the Dockerfile to its generated ACR context`
  );
  assert.match(
    builder,
    /\[\[ -f "\$DOCKERFILE" \]\]/,
    `Migration ${migration} must prove the generated Dockerfile exists before ACR build`
  );
  assert.match(
    builder,
    /--file "\$DOCKERFILE"/,
    `Migration ${migration} must pass the absolute generated Dockerfile path to Azure ACR`
  );
  assert.doesNotMatch(
    builder,
    /--file Dockerfile(?:\s|\\)/,
    `Migration ${migration} must not resolve Dockerfile from the caller working directory`
  );
  assert.match(
    builder,
    /"\$CONTEXT"; then/,
    `Migration ${migration} must upload the same context that owns its Dockerfile`
  );
}

const bashScript = [
  'set -Eeuo pipefail',
  "UNIQUE_TAG='validation-tag'",
  'build_image_contract() {',
  '  local repository="$1" dockerfile="$2" context="$3"',
  '  local image="${repository}:${UNIQUE_TAG}"',
  `  printf '%s|%s|%s|%s\\n' "$repository" "$dockerfile" "$context" "$image"`,
  '}',
  "build_image_contract 'repository' 'Dockerfile' '.'"
].join('\n');

const bashProbe = spawnSync('bash', ['-c', bashScript], { encoding: 'utf8' });
assert.equal(bashProbe.status, 0, bashProbe.stderr);
assert.equal(bashProbe.stdout.trim(), 'repository|Dockerfile|.|repository:validation-tag');

console.log('SYSTEMWIDE_IMAGE_BUILD_CONTROLLER_VALIDATION=PASS governed-controller=projectpulse-deploy-test local-initialization=ordered acr-path=context-owned docker-fallback=full_image api-source-provenance=temporary-revision-file migration-builders=094,095,096+097+098+099 utilization-uat=registered module001b-single-revision-reconcile=ready-inactive-safe module001b-fixture-audit=immutable module025-migration099=registered module025-generation-uat=registered');
