import assert from 'node:assert/strict';
import { test } from 'node:test';
import { readFileSync } from 'node:fs';
import { actionStatus, mayRecord, confirmationRequest } from '../src/completion-checklist-model.mjs';
const source = readFileSync(new URL('../src/ProjectCompletionChecklist.jsx',import.meta.url),'utf8');
const base = { closed:false, state:{revision:3}, capabilities:{sent:true}, basisFingerprint:'current' };
test('unknown authority and closed projects do not enable writes',()=>{
 for(const data of [null,{}, {...base,closed:true},{...base,capabilities:{sent:'true'}},{...base,capabilities:{}}]) assert.equal(mayRecord('sent',data),false);
 assert.equal(mayRecord('sent',base),true); assert.equal(mayRecord('billed',base),false);
});
test('partial and connected sends are never presented as fully billed',()=>{
 assert.equal(actionStatus('billed',{...base,state:{sent:{scope:'partial'}}}),'Not confirmed');
 assert.equal(actionStatus('billed',{...base,automatedFinalDelivered:true}),'Not confirmed');
 assert.equal(actionStatus('sent',{...base,automatedFinalDelivered:true}),'Connected final delivery verified');
});
test('conditional acceptance stays outstanding and stale billing stays unconfirmed',()=>{
 assert.equal(actionStatus('acceptance',{...base,state:{acceptance:{scope:'conditional'}}}),'Conditions outstanding');
 assert.equal(actionStatus('billed',{...base,billingEvidenceStale:true}),'Reconciliation required');
});
test('save carries expected version, project charge basis, and persistent retry operation',()=>{
 const form={occurredOn:'2026-09-22',reference:' ref ',evidence:' evidence ',party:' person ',notes:' note ',reason:' reviewed ',scope:'final',confirmed:true,coveredInvoiceIds:['invoice']};
 const request=confirmationRequest(form,base,'same-operation');
 assert.equal(request.expectedRevision,3);assert.equal(request.expectedBasisFingerprint,'current');assert.equal(request.operationId,'same-operation');assert.equal(request.reference,'ref');assert.deepEqual(request.coveredInvoiceIds,['invoice']);
});
test('component does not call any external transmission or AI endpoint',()=>{
 assert.ok(source.includes('/completion-checklist')); assert.ok(!source.includes('/certinia/send')); assert.ok(!source.includes('/process-outbox')); assert.ok(!source.includes('dangerouslySetInnerHTML'));
});
test('identity invalidation, request serialization and unknown outcome are explicit',()=>{
 for(const text of ['useSyncExternalStore','identityMatches()','AbortController','inFlight.current','requestRef.current ||=','Retry the same confirmation','outcomeUnknown','projectpulse:auth-session-cleared']) assert.ok(source.includes(text),text);
});
test('PM completion view is independent of SELL/financial detail failures',()=>{
 const pm=readFileSync(new URL('../src/ProjectManagerWorkloadCenter.jsx',import.meta.url),'utf8');
 assert.ok(pm.indexOf("tab === 'completion' ?") < pm.indexOf('currentDetail.loading ?'));
});
test('internal evidence is excluded from printed customer invoices',()=>{
 const css=readFileSync(new URL('../src/project-completion-checklist.css',import.meta.url),'utf8');
 assert.match(css,/@media print\s*\{\s*\.completion-checklist\s*\{\s*display:none !important/);
 const invoice=readFileSync(new URL('../src/InvoiceBillingCenter.jsx',import.meta.url),'utf8');
 assert.ok(invoice.indexOf('<ProjectCompletionChecklist') < invoice.indexOf('className="m042-workspace"'));
 assert.doesNotMatch(invoice, /fully invoiced/i);
});
