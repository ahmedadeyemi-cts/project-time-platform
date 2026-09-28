import test from 'node:test';
import assert from 'node:assert/strict';
import { workRegisterPeople } from '../src/frontend/project-time-web/src/work-register-filters.js';
import { PULSE_WORKSPACES, workspaceSearchText } from '../src/frontend/project-time-web/src/workspace-registry.js';

test('person choices combine overlapping project roles and assignments once', () => {
  assert.deepEqual(workRegisterPeople([
    {projectManager: 'Alex Smith', accountExecutive: 'Sam Lee', assignedEngineers: ['Alex Smith', 'Taylor Green']},
    {projectManager: '  alex   smith ', solutionArchitect: 'Taylor Green', assignedEngineers: ['Sam Lee', null]},
    {projectCoordinator: '', insideSales: 'Alex Smith'}
  ]), ['Alex Smith', 'Sam Lee', 'Taylor Green']);
});
test('empty portfolios do not invent people', () => assert.deepEqual(workRegisterPeople(), []));
test('workspace search accepts dashboard names and spaced route names', () => {
  for (const [query, number] of [['work register', '055C'], ['create project', '055D'], ['task board', '066']]) {
    assert.ok(PULSE_WORKSPACES.some(w => w.moduleNumber === number && workspaceSearchText(w).includes(query)), query);
  }
});
