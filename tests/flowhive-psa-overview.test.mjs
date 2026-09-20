import test from 'node:test';
import assert from 'node:assert/strict';
import { projectOverview, flowHiveProjectLink } from '../src/frontend/project-time-web/src/flowhive-psa-overview.js';
const plan = { projectEndDate:'2026-09-21', tasks:[
  {wbsNumber:'1',isSummary:true}, {wbsNumber:'1.1',status:'blocked'},
  {wbsNumber:'1.2',status:'complete',percentComplete:100}, {wbsNumber:'1.3',status:'not_started'}],
  assignments:[{taskWbs:'1.1',resourceUserId:'a'},{taskWbs:'1.3',resourceUserId:null}]};
const schedule = {valid:true,projectFinishDate:'2026-09-24',tasks:[
  {wbsNumber:'1.1',endDate:'2026-09-20',isCritical:true}, {wbsNumber:'1.2',endDate:'2026-09-20'},
  {wbsNumber:'1.3',endDate:'2026-09-24'}]};
test('overview excludes phase totals and closed work; counts only canonical assignees',()=>{
  const data=projectOverview(plan,schedule,'2026-09-21','a');
  assert.deepEqual(data.totals,{overdue:1,dueSoon:1,unassigned:1,blocked:1,critical:1,mine:1});
  assert.equal(data.completed,1); assert.equal(data.tasks.length,3);
});
test('invalid or absent schedule never invents dates or overdue work',()=>{
  const data=projectOverview(plan,{...schedule,valid:false},'2026-09-21');
  assert.equal(data.finish,null);assert.equal(data.totals.overdue,0);assert.equal(data.totals.critical,0);
});
test('project links resolve only within the authorized portfolio',()=>{
  assert.equal(flowHiveProjectLink('#project-flowhive?projectId=a',[{projectId:'a'}]),'a');
  assert.equal(flowHiveProjectLink('#project-flowhive?projectId=b',[{projectId:'a'}]),'');
  assert.equal(flowHiveProjectLink('#other?projectId=a',[{projectId:'a'}]),'');
});
