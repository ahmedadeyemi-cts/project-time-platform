import assert from 'node:assert/strict';
import fs from 'node:fs';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import { verifyReadOnlyWorkflow } from './flowhive-psa-scope.mjs';

const base='7339b882ed97371b399ff8cd35b8e48f2bce3a6c';
const expected=[
  ".github/workflows/flowhive-psa-release-control-ci.yml",
  ".github/workflows/projectpulse-deploy-test.yml",
  "database/migrations/110_module025_ungenerated_draft_delete.sql",
  "scripts/release-test/build-and-run-module025-retention-migration-106.sh",
  "scripts/release-test/run-module025-installed-sa-uat.py",
  "scripts/release-test/validate-protected-test-controller-branches.sh",
  "src/backend/ProjectTime.Api/Ai/CelarAiCapabilityRouting.cs",
  "src/backend/ProjectTime.Api/Ai/Module025GenerationEngine.cs",
  "src/backend/ProjectTime.Api/Modules/Module025SowGsdModule.cs",
  "src/frontend/project-time-web/src/module025/SowGsdAuthoringWorkspace.jsx",
  "src/frontend/project-time-web/src/module025/generation-feedback.js",
  "src/frontend/project-time-web/src/module025/sow-gsd-workspace.css",
  "tests/module025-review-timing-delete-scope.mjs",
  "tests/flowhive-psa-admission.test.mjs",
  "tests/module025-review-timing-delete.test.mjs",
  "tests/test-module025-draft-delete-migration-110.sh",
  "tests/validate-celar-ai-pr630-consolidated.mjs"
].sort();

export function verifyModule025ReviewTimingDeleteScope(){
  assert.equal(execFileSync('git',['merge-base',base,'HEAD'],{encoding:'utf8'}).trim(),base);
  const text=execFileSync('git',['diff','--name-only',`${base}...HEAD`],{encoding:'utf8'}).trim();
  const actual=text?text.split(/\r?\n/):[];
  assert.deepEqual([...actual].sort(),expected);
  assert.throws(()=>assert.deepEqual([...actual,'.github/workflows/projectpulse-deploy-production.yml'].sort(),expected));
  for(const file of expected.filter(file=>file.startsWith('.github/workflows/') && file !== '.github/workflows/projectpulse-deploy-test.yml'))
    verifyReadOnlyWorkflow(fs.readFileSync(file,'utf8'),file);
  const deploy=fs.readFileSync('.github/workflows/projectpulse-deploy-test.yml','utf8');
  assert.match(deploy,/environment: test/);
  assert.match(deploy,/group: projectpulse-deploy-test/);
  assert.match(deploy,/110_module025_ungenerated_draft_delete/);
  assert.doesNotMatch(deploy,/projectpulse-deploy-production/);
  assert.deepEqual(
    fs.readFileSync('.github/workflows/projectpulse-deploy-production.yml'),
    execFileSync('git',['show',`${base}:.github/workflows/projectpulse-deploy-production.yml`])
  );
  const migration=fs.readFileSync('database/migrations/110_module025_ungenerated_draft_delete.sql','utf8');
  assert.match(migration,/module025_allow_draft_delete/);
  assert.match(migration,/last_generated_at IS NULL/);
  assert.match(migration,/module025_sow_gsd_versions/);
  const module=fs.readFileSync('src/backend/ProjectTime.Api/Modules/Module025SowGsdModule.cs','utf8');
  assert.match(module,/MapDelete\("\/api\/module025\/sow-gsd\/\{engagementId:guid\}"/);
  assert.match(module,/MeaningfulServiceOverview/);
  assert.match(module,/elapsedSeconds/);
  const editor=fs.readFileSync('src/frontend/project-time-web/src/module025/SowGsdAuthoringWorkspace.jsx','utf8');
  for(const marker of ['Delete Draft','Review readiness','Confirm Reviewed SOW / GSD','m025-generation-timer','meaningfulServiceOverview'])
    assert.ok(editor.includes(marker),`missing editor marker: ${marker}`);
  const engine=fs.readFileSync('src/backend/ProjectTime.Api/Ai/Module025GenerationEngine.cs','utf8');
  assert.match(engine,/ProviderTimeoutSeconds = 180/);
  assert.match(engine,/ExternalProviderTimeoutSeconds = 120/);
  const verifier=fs.readFileSync('scripts/release-test/run-module025-installed-sa-uat.py','utf8');
  assert.ok(!verifier.includes('get_by_role("heading", name="SOW & GSD Workspace", exact=True).wait_for'));
  assert.ok(verifier.includes('workspace.locator(".m025-filters").wait_for'));
  console.log('MODULE025_REVIEW_TIMING_DELETE_SCOPE=PASS production=unchanged');
}
if(process.argv[1]&&path.resolve(process.argv[1])===fileURLToPath(import.meta.url))
  verifyModule025ReviewTimingDeleteScope();
