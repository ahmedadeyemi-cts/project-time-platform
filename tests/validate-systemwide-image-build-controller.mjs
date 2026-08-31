import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import fs from 'node:fs';

const workflowPath = '.github/workflows/projectpulse-deploy-test.yml';
const retiredWorkflowPath = '.github/workflows/systemwide-enterprise-reliability-test-deployment.yml';
const apiProjectPath = 'src/backend/ProjectTime.Api/ProjectTime.Api.csproj';
const apiBuildPropsPath = 'src/backend/ProjectTime.Api/Directory.Build.props';
const sourceRevisionPath = 'src/backend/ProjectTime.Api/.projectpulse-source-revision';
const revisionWaitPath = 'scripts/wait-containerapp-ready-revision.sh';
const documentAuthorityMigrationBuilderPath = 'scripts/release-test/build-and-run-project-planning-document-authority-migration-job.sh';
const workflow = fs.readFileSync(workflowPath, 'utf8');
const apiProject = fs.readFileSync(apiProjectPath, 'utf8');
const apiBuildProps = fs.readFileSync(apiBuildPropsPath, 'utf8');
const revisionWait = fs.readFileSync(revisionWaitPath, 'utf8');
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

console.log('SYSTEMWIDE_IMAGE_BUILD_CONTROLLER_VALIDATION=PASS governed-controller=projectpulse-deploy-test local-initialization=ordered acr-path=context-owned docker-fallback=full_image api-source-provenance=temporary-revision-file migration-builders=094,095,096+097+098+099 utilization-uat=registered module001b-single-revision-reconcile=guarded module025-migration099=registered');
