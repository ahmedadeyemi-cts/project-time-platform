import assert from 'node:assert/strict';
import { JSDOM } from 'jsdom';
import React, { act } from 'react';
import { createRoot } from 'react-dom/client';
import Review, {approvalPeriod} from '../../src/frontend/project-time-web/src/ApprovalPeriodReview.jsx';
const dom=new JSDOM('<div id="root"></div>',{url:'https://fixture.invalid'});
globalThis.window=dom.window; globalThis.document=dom.window.document;globalThis.CustomEvent=dom.window.CustomEvent;globalThis.IS_REACT_ACT_ENVIRONMENT=true;
const root=createRoot(document.getElementById('root'));
const data=[{timesheetId:'one',reviewToken:'review-one',workDate:'2026-09-30',stage:'manager',projectId:null,resourceName:'Engineer One',projectNames:'Mixed day',totalHours:8},{timesheetId:'two',reviewToken:'review-two',workDate:'2026-10-01',stage:'manager',projectId:null,resourceName:'Engineer Two',totalHours:4}];
let calls=[], writes=[],fail=false;
const fetchPending=async args=>{calls.push(args);return {items:args.page===1?[data[0]]:[data[1]],filteredCount:2,hasMore:args.page===1,nextPage:args.page===1?2:null};};
const completePending=async args=>{writes.push(args);if(fail)throw Error('Selection changed. Refresh and review.');return {completedCount:args.items.length};};
async function render(readOnly=false){await act(async()=>root.render(React.createElement(Review,{fetchPending,completePending,readOnly})));}
async function click(el){assert.ok(el);await act(async()=>el.click());}
const button=text=>[...document.querySelectorAll('button')].find(el=>el.textContent===text);
assert.deepEqual(approvalPeriod('week','2026-09-30'),{weekStart:'2026-09-27'});
assert.deepEqual(approvalPeriod('month','2026-09-30'),{monthStart:'2026-09-01'});
await render();assert.equal(document.querySelectorAll('tbody tr').length,2,'All pages loaded');
await click(document.querySelector('.approval-period-select input'));
await click(button('Review selected (2)'));assert.equal(writes.length,0,'Review never writes');
await click(button('Confirm approval'));assert.equal(writes.length,1);assert.equal(writes[0].items.length,2);assert.equal(writes[0].mode,'selected');assert.equal(writes[0].items[0].reviewToken,'review-one');assert.ok(writes[0].weekStart);assert.equal(document.querySelectorAll('input:checked').length,0);
// Month and stage changes reset selection and issue exact month query.
await click(document.querySelector('.approval-period-select input'));
await click(button('Month')); assert.equal(button('Month').getAttribute('aria-pressed'),'true');
assert.ok(calls.at(-1).monthStart);assert.equal(document.querySelectorAll('input:checked').length,0);
await click(document.querySelector('.approval-period-select input'));await click(button('Review selected (2)'));
fail=true;await click(button('Confirm approval'));assert.match(document.querySelector('[role="alert"]').textContent,/Selection changed/);assert.equal(writes.at(-1).weekStart,undefined);assert.ok(writes.at(-1).monthStart);assert.equal(button('Confirm approval'),undefined);
await render(true);assert.ok([...document.querySelectorAll('input[type="checkbox"]')].every(el=>el.disabled));assert.ok(button('Review selected (2)').disabled);
await act(async()=>root.unmount());
console.log('PASS bulk review UI: pagination, explicit selection, confirmation, month boundaries, stale rejection, View-As read-only');
