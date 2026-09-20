import test from 'node:test';
import assert from 'node:assert/strict';
import { operationDuration, operationTimestamp } from '../src/frontend/project-time-web/src/ai/operation-progress.js';
import { isFlowHiveArchived, filterFlowHiveProjects } from '../src/frontend/project-time-web/src/flowhive-project-lifecycle.js';

test('server timestamps restore elapsed time across refresh and freeze on completion', () => {
  assert.equal(operationDuration('2026-09-20T01:00:00Z', '2026-09-20T02:03:04Z'), '01:03:04');
  assert.equal(operationDuration('2026-09-20T01:00:00Z', '2026-09-20T01:02:30Z'), '00:02:30');
  assert.equal(operationDuration(1000, 4500), '00:00:03');
});
test('invalid dates and clock skew cannot display NaN or negative time', () => {
  for (const start of ['', null, 'invalid', NaN]) assert.equal(operationDuration(start, Date.now()), '00:00:00');
  assert.equal(operationTimestamp(null), null);
  assert.equal(operationDuration(5000, 1000), '00:00:00');
});
test('project closure immediately moves the same record into archive; reopening restores it', () => {
  const p = { projectId: 'p', status: 'active', projectCode: 'P-1', projectManagerName: 'Fixture PM' };
  assert.equal(filterFlowHiveProjects([p]).length, 1);
  const closed = { ...p, status: ' Closed ' };
  assert.equal(filterFlowHiveProjects([closed]).length, 0);
  assert.equal(filterFlowHiveProjects([closed], { archived: true })[0].projectId, 'p');
  assert.equal(filterFlowHiveProjects([{ ...closed, status: 'active' }]).length, 1);
  assert.equal(isFlowHiveArchived({ status: 'on_hold' }), false);
});
test('archive keeps existing PM, customer and status search filters', () => {
  const rows = [{ projectId:'a', status:'closed', projectManagerName:'Fixture PM', customerName:'Client A' },
    { projectId:'b', status:'completed', accountExecutiveName:'Fixture AE', customerName:'Client B' }];
  assert.deepEqual(filterFlowHiveProjects(rows, { archived:true, search:'fixture ae' }).map(p=>p.projectId), ['b']);
  assert.deepEqual(filterFlowHiveProjects(rows, { archived:true, customer:'Client A', status:'closed' }).map(p=>p.projectId), ['a']);
});
