import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import { catalogRows, reconciliationSql } from '../scripts/release-test/reconcile-module-catalog.mjs';
import { PROJECTPULSE_MODULES } from '../src/frontend/project-time-web/src/module-availability-registry.js';
import { verifyPolicyPublication } from '../src/frontend/project-time-web/src/role-policy-publication.js';
import { grantsFor } from '../src/frontend/project-time-web/src/role-permission-model.js';
const backend=fs.readFileSync('src/backend/ProjectTime.Api/Modules/ModuleAvailabilityModule.cs','utf8');
test('every built-in module agrees across frontend and backend',()=>{
  assert.equal(catalogRows().length,PROJECTPULSE_MODULES.length);
  assert.equal(catalogRows().find(r=>r.module_code==='025').module_name,'SOW & GSD Workspace');
});
test('new module cannot ship with missing backend registration or duplicate identity/route',()=>{
  const next={moduleNumber:'084',route:'synthetic-new-module',displayName:'New module'};
  assert.throws(()=>catalogRows([...PROJECTPULSE_MODULES,next],backend),/registry drift/);
  assert.equal(catalogRows([...PROJECTPULSE_MODULES,next],backend+'\n["084"] = Module("084", "synthetic-new-module", "New module", "Test"),').length,PROJECTPULSE_MODULES.length+1);
  assert.throws(()=>catalogRows([...PROJECTPULSE_MODULES,PROJECTPULSE_MODULES[0]],backend),/Duplicate module ID/);
  assert.throws(()=>catalogRows([...PROJECTPULSE_MODULES,{...next,route:PROJECTPULSE_MODULES[0].route}],backend),/Duplicate module route/);
  assert.throws(()=>catalogRows(PROJECTPULSE_MODULES,backend.replace('SOW & GSD Workspace','Stale title')),/registry drift/);
});
test('generated SQL binds release and complete catalog, with no unresolved placeholders',()=>{
  const sql=reconciliationSql('a'.repeat(40));
  assert.ok(sql.includes('SOW & GSD Workspace')); assert.ok(!/\/\*(CATALOG|RELEASE)_/.test(sql));
  assert.throws(()=>reconciliationSql('main'));
});
function fixture() {
  const change={roleCode:'SOLUTION_ARCHITECT',moduleCode:'025',grants:grantsFor('025','SOLUTION_ARCHITECT','Full Control','ORGANIZATION')};
  const receipt={status:'policy_published',versionNumber:12,policyVersionId:'version-12'};
  const rows=change.grants.map(g=>({...g,roleCode:change.roleCode,moduleCode:change.moduleCode,grantEffect:g.effect}));
  return {request:{baseVersionNumber:11,changes:[change]},receipt,
    detail:{role:{roleCode:change.roleCode},moduleCode:'025',policyVersion:receipt,grants:structuredClone(rows)},
    matrix:{policyVersion:receipt,grants:structuredClone(rows)}};
}
test('publish readback verifies full grants and tolerates JSONB property ordering',()=>{
  const f=fixture(); for(const g of f.matrix.grants) g.conditions=Object.fromEntries(Object.entries(g.conditions).reverse());
  assert.equal(verifyPolicyPublication(f.request,f.receipt,f.detail,f.matrix).versionNumber,12);
});
for (const [name,mutate] of [
  ['stale denial',f=>f.matrix.grants.push({...f.matrix.grants[0],grantEffect:'DENY'})],
  ['missing saved permission',f=>f.detail.grants.pop()],
  ['wrong module detail',f=>f.detail.moduleCode='001'],
  ['wrong role detail',f=>f.detail.role.roleCode='ENGINEERING'],
  ['superseded version',f=>f.matrix.policyVersion={...f.receipt,versionNumber:13}],
  ['wrong policy identity',f=>f.matrix.policyVersion={...f.receipt,policyVersionId:'wrong'}],
  ['empty HTTP 200 payload',f=>f.receipt={}],
  ['duplicate rows',f=>f.matrix.grants.push(f.matrix.grants[0])],
  ['different scope',f=>f.matrix.grants[0].scopeCode='SELF']
]) test(`readback rejects ${name}`,()=>{const f=fixture();mutate(f);assert.throws(()=>verifyPolicyPublication(f.request,f.receipt,f.detail,f.matrix));});
