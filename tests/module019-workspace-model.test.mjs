import test from 'node:test';
import assert from 'node:assert/strict';
import { knownNumber, workOptions, documentsForWork, tasksForProject, projectHours, hourExplanation } from '../src/frontend/project-time-web/src/project-workspace-model.js';
const task = (extra = {}) => ({ id: 'a1', projectId: 'p1', userId: 'u1', taskId: 't1', engineerName: 'Engineer One', assignedHours: 10, usedHours: 8, ...extra });

test('selection keeps standalone service requests and consolidates linked requests', () => {
  const options = workOptions({ projects: [{ id: 'p1', projectCode: 'P1' }], resourceRequests: [{ requestNumber: 'SR1', projectId: 'p1' }, { requestNumber: 'SR2', projectIntakeRequestId: 'i2' }] });
  assert.deepEqual(options.map(row => row.key).sort(), ['project:p1', 'request:SR2']);
});
test('documents use stable project/intake IDs and preserve separate file versions', () => {
  const documents = [{ id: 'd1', projectId: 'p1', originalFileName: 'SOW.docx' }, { id: 'd2', projectId: 'p1', originalFileName: 'SOW.docx' }, { id: 'd3', projectId: 'p2', projectCode: 'P1' }, { id: 'd4', projectIntakeRequestId: 'i2' }];
  assert.equal(documentsForWork(documents, { kind: 'project', id: 'p1' }).length, 2);
  assert.deepEqual(documentsForWork(documents, { kind: 'request', id: 'i2' }).map(row => row.id), ['d4']);
  assert.equal(documentsForWork(documents, { kind: 'request', id: null }).length, 0);
});
test('multiple allocations for the same task do not double count logged time', () => {
  const tasks = tasksForProject([task(), task(), task({ id: 'a2', assignedHours: 4 }), task({ id: 'other', projectId: 'p2' })], 'p1');
  assert.equal(tasks.length, 1); assert.equal(tasks[0].assignedHours, 14); assert.equal(tasks[0].usedHours, 8); assert.equal(tasks[0].remainingHours, 6);
});
test('team total includes a former engineer and taskless hours without inflating allocation', () => {
  const result = projectHours({ assignments: [task(), task({ id: 'a2', userId: 'u2', engineerName: 'Engineer Two', assignedHours: 5 })], teamHours: [{ projectId: 'p1', userId: 'u1', engineerName: 'Engineer One', loggedHours: 8 }, { projectId: 'p1', userId: 'u2', engineerName: 'Engineer Two', loggedHours: 4 }, { projectId: 'p1', userId: 'u3', engineerName: 'Former engineer', loggedHours: 6 }] }, 'p1', 'u1');
  assert.equal(result.assigned, 15); assert.equal(result.logged, 18); assert.equal(result.remaining, -3); assert.equal(result.myRemaining, 2); assert.equal(result.people.length, 3);
  assert.match(hourExplanation(result), /3 hours over/);
});
test('missing project totals remain unknown instead of becoming zero', () => {
  const result = projectHours({ assignments: [task()] }, 'p1', 'u1');
  assert.equal(result.logged, null); assert.equal(result.remaining, null); assert.match(hourExplanation(result), /unavailable/);
  assert.equal(knownNumber(null), null); assert.equal(knownNumber(''), null); assert.equal(knownNumber(0), 0);
});
test('zero allocation is explained separately from a confirmed budget overrun', () => {
  assert.match(hourExplanation({ assigned: 0, logged: 3, remaining: -3 }), /no current hours are allocated/);
});
