import test from 'node:test';
import assert from 'node:assert/strict';
import { visibleAgentSnapshot, availableAgentCapabilities, agentStatusLabel } from '../../src/frontend/project-time-web/src/agents/agent-workspace-state.js';
const key = 'synthetic-session:project-1:source-7';
const snapshot = () => ({ contextKey: key, authorized: true, enabled: true, viewAs: false,
  capabilities: [{ code: 'engineering_readiness', name: 'Assignment readiness', authorized: true, executionReady: true }] });
test('off, denied, View-As and stale session/resource projections reveal nothing', () => {
  for (const mutation of [{ enabled: false }, { authorized: false }, { viewAs: true }, { contextKey: 'other-user' }, { contextKey: 'project-2' }]) {
    const value = { ...snapshot(), ...mutation };
    assert.equal(visibleAgentSnapshot(key, value), null);
    assert.deepEqual(availableAgentCapabilities(key, value), []);
  }
  assert.equal(visibleAgentSnapshot('', snapshot()), null);
  assert.equal(visibleAgentSnapshot(key, null), null);
});
test('only server-qualified, authorized capabilities are offered', () => {
  const value = snapshot();
  value.capabilities.push({ code: 'approve_time', name: 'Not permitted', authorized: false, executionReady: true });
  value.capabilities.push({ code: 'project_delivery', name: 'Catalog only', authorized: true, executionReady: false });
  value.capabilities.push({ code: 'https://example.invalid', name: 'Invalid', authorized: true, executionReady: true });
  value.roles = ['SUPER_ADMINISTRATOR'];
  assert.deepEqual(availableAgentCapabilities(key, value).map(x => x.code), ['engineering_readiness']);
});
test('presentation never claims business completion or sent handoff', () => {
  assert.match(agentStatusLabel('AwaitingReview'), /not sent/);
  assert.match(agentStatusLabel('ProposalComplete'), /no business changes applied/);
  assert.equal(agentStatusLabel('made_up'), 'Status not verified');
});
test('malformed capability projections fail closed', () => {
  for (const capabilities of [null, {}, 'all']) assert.deepEqual(availableAgentCapabilities(key, { ...snapshot(), capabilities }), []);
});
