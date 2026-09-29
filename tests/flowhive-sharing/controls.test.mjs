import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { projectControlsRequest, enableProjectCustomerSharing, customerSharingError } from '../../src/frontend/project-time-web/src/flowhive-project-controls.js';
const projectId = '11111111-1111-4111-8111-111111111111';
const enterprise = { project: { projectId }, access: { canShare: true, isViewAs: false } };
const success = { projectId, customerSharingEnabled: true, customerLinkCreated: false, message: 'Enabled' };
function invocation(extra={}) {
  const calls = []; const saved = []; const errors = []; let settled = 0;
  return {calls,saved,errors,get settled(){return settled;},args:{projectId,enterprise,busy:false,
    post:async (...args)=>{calls.push(args);return success;},isCurrent:()=>true,
    onSaved:result=>saved.push(result),onError:error=>errors.push(error),onSettled:()=>settled++,...extra}};
}
test('financial write has only the ten request fields, not API metadata',()=>{
  const result=projectControlsRequest({projectId,updatedAt:'server-only',restricted:false,approvedBudget:'12.50',expenseBudget:'',contingencyBudget:null,customerSharingEnabled:true});
  assert.deepEqual(Object.keys(result).sort(),['contractType','currencyCode','approvedBudget','expenseBudget','contingencyBudget','forecastAtCompletion','percentCompleteMethod','statusReportCadence','customerSharingEnabled','financialNotes'].sort());
  assert.equal(result.approvedBudget,12.5);assert.equal(result.expenseBudget,null);assert.equal(result.forecastAtCompletion,null);
});
test('invalid numeric values and DOM events fail locally rather than create a bad JSON save',()=>{
  for(const value of [-1,Infinity,NaN,'not money',{},true]) assert.throws(()=>projectControlsRequest({approvedBudget:value}),/Approved budget/);
  assert.throws(()=>projectControlsRequest({nativeEvent:{}}),/not ready/);
});
test('enable sends only an empty sharing command, never financial fields or a customer-link request',async()=>{
  const f=invocation({enterprise:{...enterprise,controls:{approvedBudget:'invalid',financialNotes:'Unsaved private note'}}});
  assert.equal(await enableProjectCustomerSharing(f.args),true);
  assert.deepEqual(f.calls,[[`/api/project-flowhive/projects/${projectId}/customer-sharing/enable`,{}]]);
  assert.deepEqual(f.saved,[success]);assert.equal(f.settled,1);assert.deepEqual(f.errors,[]);
});
test('view-only, View-As, missing project, stale project and busy requests cannot send',async()=>{
  for(const extra of [{busy:true},{projectId:''},{enterprise:null},{enterprise:{...enterprise,project:{projectId:'other'}}},{enterprise:{...enterprise,access:{canManage:true,canShare:false}}},{enterprise:{...enterprise,access:{canShare:true,isViewAs:true}}}]){
    const f=invocation(extra);assert.equal(await enableProjectCustomerSharing(f.args),false);assert.equal(f.calls.length,0);
  }
});
test('failure does not optimistically enable; exposes the useful reference and settles once',async()=>{
  const error=Object.assign(new Error('Validation failed'),{status:400,responseBody:{message:'Invalid request.',correlationId:'PP-FIXTURE'}});
  const f=invocation({post:async()=>{throw error;}});assert.equal(await enableProjectCustomerSharing(f.args),false);
  assert.deepEqual(f.saved,[]);assert.deepEqual(f.errors,[error]);assert.equal(f.settled,1);
  assert.deepEqual(customerSharingError(error),{message:'Invalid request.',correlationId:'PP-FIXTURE'});
});
test('late success and failure cannot update another project or selection generation',async()=>{
  for(const fail of [false,true]){
    const f=invocation({isCurrent:()=>false,post:async()=>{if(fail)throw new Error('late');return success;}});
    assert.equal(await enableProjectCustomerSharing(f.args),false);assert.equal(f.saved.length+f.errors.length+f.settled,0);
  }
});
test('response must name the project and confirm enablement without a link',async()=>{
  for(const response of [{...success,projectId:'other'},{...success,customerSharingEnabled:false},{...success,customerLinkCreated:true},null]){
    const f=invocation({post:async()=>response});assert.equal(await enableProjectCustomerSharing(f.args),false);assert.equal(f.saved.length,0);assert.equal(f.errors.length,1);
  }
});
test('session, authorization and timeout failures have actionable messages',()=>{
  assert.match(customerSharingError({status:401}).message,/Sign in/);
  assert.match(customerSharingError({status:403}).message,/View-As/);
  assert.match(customerSharingError({name:'TimeoutError'}).message,/may have completed/);
});
test('center routes sharing independently and does not reload over unsaved financial edits',()=>{
  const code=readFileSync(new URL('../../src/frontend/project-time-web/src/ProjectFlowHiveCenter.jsx',import.meta.url),'utf8');
  const enable=code.slice(code.indexOf('  async function enableCustomerSharing()'),code.indexOf('  async function createCustomerShare()'));
  assert.ok(enable.includes('enableProjectCustomerSharing'));
  for(const forbidden of ['saveProjectControls(', 'loadEnterpriseWorkspace(', 'customer-shares`']) assert.ok(!enable.includes(forbidden));
  assert.ok(enable.includes('sharingRequestInFlight.current'));assert.ok(enable.includes('captureWorkspaceOperation(false)'));
  assert.ok(code.includes('sharingPanel={sharingPanel}'));
});
