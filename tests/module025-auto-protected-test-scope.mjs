import assert from 'node:assert/strict';
import fs from 'node:fs';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const base='73412b3a3509f2f4098d137748c4d376947c1143';
const expected=[
  ".github/workflows/flowhive-psa-release-control-ci.yml",
  ".github/workflows/module025-governed-protected-test-release-manual.yml",
".github/workflows/module025-protected-uat-control.yml",
".github/workflows/projectpulse-release-test-control-ci.yml",
  ".github/workflows/projectpulse-deploy-test.yml",
  "scripts/release-test/validate-protected-test-controller-branches.sh",
  "tests/flowhive-psa-admission.test.mjs",
  "tests/module025-auto-protected-test-scope.mjs",
  "tests/test-project-planning-collaboration-migration-095.sh",
"tests/validate-systemwide-enterprise-reliability.mjs",
  "tests/validate-systemwide-image-build-controller.mjs",
  "tests/validate-celar-ai-pr630-consolidated.mjs"
].sort();

export function verifyModule025AutoProtectedTestScope(){
  assert.equal(execFileSync('git',['merge-base',base,'HEAD'],{encoding:'utf8'}).trim(),base);
  const t=execFileSync('git',['diff','--name-only',`${base}...HEAD`],{encoding:'utf8'}).trim();
  const actual=t?t.split(/\r?\n/):[];
  assert.deepEqual([...actual].sort(),expected);
  const supervisor=fs.readFileSync('.github/workflows/module025-protected-uat-control.yml','utf8');
  assert.match(supervisor,/push:\s*[\s\S]*branches:\s*\[main\]/);
  assert.match(supervisor,/permissions:\s*[\s\S]*actions:\s*write[\s\S]*contents:\s*read[\s\S]*issues:\s*write[\s\S]*pull-requests:\s*write/);
  assert.match(supervisor,/acceptance_scope:"sow_role"/);
  assert.match(supervisor,/disabled_manually/);
  const deploy=fs.readFileSync('.github/workflows/projectpulse-deploy-test.yml','utf8');
  assert.match(deploy,/permissions:\s*[\s\S]*id-token:\s*write[\s\S]*contents:\s*read[\s\S]*actions:\s*read/);
  assert.match(deploy,/steps\.sow_role_uat\.outcome == 'success'/);
  assert.match(deploy,/109_module025_project_name/);
  assert.deepEqual(fs.readFileSync('.github/workflows/projectpulse-deploy-production.yml'),execFileSync('git',['show',`${base}:.github/workflows/projectpulse-deploy-production.yml`]));
  console.log('MODULE025_AUTO_PROTECTED_TEST_SCOPE=PASS production=unchanged');
}
if(process.argv[1]&&path.resolve(process.argv[1])===fileURLToPath(import.meta.url)) verifyModule025AutoProtectedTestScope();
