import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import fs from 'node:fs';

const workflowPath = '.github/workflows/projectpulse-deploy-test.yml';
const retiredWorkflowPath = '.github/workflows/systemwide-enterprise-reliability-test-deployment.yml';
const workflow = fs.readFileSync(workflowPath, 'utf8');

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
assert.match(workflow, /Apply and verify Migrations 086, 088, 093, 094, 095, and 096 inside Test private network/);
assert.match(workflow, /MIGRATION_093=APPLIED_AND_VERIFIED/);
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

const migrationImageBuilders = [
  ['094', 'scripts/release-test/build-and-run-flowhive-authority-migration-094-job.sh'],
  ['095', 'scripts/release-test/build-and-run-project-planning-collaboration-migration-job.sh'],
  ['096', 'scripts/release-test/build-and-run-project-planning-document-authority-migration-job.sh']
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

console.log('SYSTEMWIDE_IMAGE_BUILD_CONTROLLER_VALIDATION=PASS governed-controller=projectpulse-deploy-test local-initialization=ordered acr-path=context-owned docker-fallback=full_image migration-builders=094,095,096 utilization-uat=registered');
