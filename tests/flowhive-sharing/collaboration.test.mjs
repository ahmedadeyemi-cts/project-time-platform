import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import {flowHivePlanRequest,workingCopyRequest,flowHiveErrorText} from '../../src/frontend/project-time-web/src/flowhive-plan-request.js';
import {assigneeKey,changeTaskAssignee,taskAssignees,updateAssigneeHours,assigneeNames} from '../../src/frontend/project-time-web/src/flowhive-assignees.js';
import {projectOverview} from '../../src/frontend/project-time-web/src/flowhive-psa-overview.js';
const project='11111111-1111-4111-8111-111111111111',u1='22222222-2222-4222-8222-222222222222',u2='33333333-3333-4333-8333-333333333333',c1='44444444-4444-4444-8444-444444444444';
function plan(){return {projectId:project,planId:'',projectStartDate:'2026-09-28',projectEndDate:'',tasks:[{wbsNumber:'1.1',name:'Review design',durationWorkingDays:2,percentComplete:0,remainingEffortHours:8,constraintDate:'',clientTaskId:'',canonicalTaskId:null,notes:'Preserve this note',detailedSteps:['Review','Confirm']}],assignments:[],dependencies:[],milestones:[]};}
test('cleared optional dates and identifiers use null without dropping WBS details',()=>{
 const raw=plan(),copy=workingCopyRequest(raw,project,'');assert.equal(copy.plan.projectEndDate,null);assert.equal(copy.plan.tasks[0].constraintDate,null);assert.equal(copy.plan.planId,null);assert.equal(copy.expectedRowVersion,null);
 assert.equal(copy.plan.tasks[0].notes,'Preserve this note');assert.deepEqual(copy.plan.tasks[0].detailedSteps,['Review','Confirm']);assert.equal(raw.projectEndDate,'');
});
test('fractional durations, nonfinite progress, invalid dates and forged identities have field errors',()=>{
 for(const change of [{durationWorkingDays:1.5},{percentComplete:NaN},{constraintDate:'2026-02-30'},{canonicalTaskId:'not-an-id'}]){const value=plan();Object.assign(value.tasks[0],change);assert.throws(()=>flowHivePlanRequest(value,project),e=>e.responseBody.issues[0].path.startsWith('WBS 1.1'));}
 assert.throws(()=>flowHivePlanRequest(plan(),u1),/selected project/);
});
test('multiple internal and external assignees coexist and adding does not multiply effort',()=>{
 let value=plan();value.assignments=[{taskWbs:'1.1',resourceUserId:u1,resourceDisplayName:'Engineer One',plannedHours:8,allocationPercent:75}];
 value=changeTaskAssignee(value,'1.1',{resourceUserId:u2,displayName:'Engineer Two'},true);
 value=changeTaskAssignee(value,'1.1',{projectContactId:c1,displayName:'Customer Contact'},true);
 assert.equal(taskAssignees(value,'1.1').length,3);assert.equal(value.assignments[0].plannedHours,8);assert.equal(value.assignments[0].allocationPercent,75);assert.equal(value.assignments[1].plannedHours,0);assert.equal(value.assignments[2].resourceUserId,null);
 assert.equal(assigneeNames(value.assignments),'Engineer One, Engineer Two, Customer Contact');assert.equal(value.tasks[0].remainingEffortHours,8);
});
test('remove one person retains every other task and person and repeated selections are idempotent',()=>{
 let value=plan();value.assignments=[{taskWbs:'2.1',resourceUserId:u1,plannedHours:2},{taskWbs:'1.1',resourceUserId:u1,plannedHours:8}];
 value=changeTaskAssignee(value,'1.1',{resourceUserId:u2,displayName:'Two'},true);const before=value;
 assert.equal(changeTaskAssignee(value,'1.1',{resourceUserId:u2},true),before);
 value=changeTaskAssignee(value,'1.1',{resourceUserId:u1},false);assert.equal(value.assignments.length,2);assert.equal(value.assignments[0].taskWbs,'2.1');assert.equal(value.assignments[1].resourceUserId,u2);
});
test('per-person effort editing does not rewrite the other person',()=>{
 let value=plan();value=changeTaskAssignee(value,'1.1',{resourceUserId:u1,displayName:'One'},true);value=changeTaskAssignee(value,'1.1',{projectContactId:c1,displayName:'Customer'},true);
 value=updateAssigneeHours(value,'1.1',`user:${u1}`,'plannedHours',3);assert.equal(value.assignments[0].plannedHours,3);assert.equal(value.assignments[1].plannedHours,0);assert.equal(assigneeKey(value.assignments[1]),`contact:${c1}`);
});
test('external contact counts as assigned, never as an internal user’s My work',()=>{
 let value=changeTaskAssignee(plan(),'1.1',{projectContactId:c1,displayName:'Customer'},true);const data=projectOverview(value,null,'2026-09-28',c1);assert.equal(data.totals.unassigned,0);assert.equal(data.totals.mine,0);assert.equal(data.tasks[0].assignments.length,1);
});
test('error presentation retains field paths and diagnostic reference',()=>{
 assert.match(flowHiveErrorText({responseBody:{issues:[{path:'WBS 1.1.constraintDate',message:'Enter a valid date.'}],correlationId:'fixture'}}),/constraintDate.*valid date.*fixture/);
});
test('plan-bearing writes use normalized contracts and Board groups all assignees',()=>{
 const center=fs.readFileSync(new URL('../../src/frontend/project-time-web/src/ProjectFlowHiveCenter.jsx',import.meta.url),'utf8');
 const psa=fs.readFileSync(new URL('../../src/frontend/project-time-web/src/ProjectFlowHivePsaWorkspace.jsx',import.meta.url),'utf8');
 assert.match(center,/workingCopyRequest\(draftPlan,selectedProjectId,loadedWorkingVersion.current\)/);assert.match(center,/body\?\.plan \? \{\.\.\.body,plan:flowHivePlanRequest/);assert.match(center,/Fields needing correction/);assert.match(center,/ProjectFlowHiveAssignees/);assert.match(psa,/assigneeNames\(assignment\)/);assert.match(psa,/plan: flowHivePlanRequest\(draftPlan,projectId\)/);
});
