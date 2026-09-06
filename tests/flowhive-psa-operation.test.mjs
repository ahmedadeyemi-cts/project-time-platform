import test from 'node:test';
import assert from 'node:assert/strict';
import { abortableDelay, boundedFetch, canApplyPlannerResult, observePlanner } from '../src/frontend/project-time-web/src/flowhive-planner-operation.js';
const initial = { projectId: 'project-a', runId: 'run-a', terminal: false };
const complete = { ...initial, terminal: true, workingDraft: { persisted: true }, plan: { projectId: 'project-a' } };
const noDelay = async () => {};

test('status observation uses GET paths only and finishes with exact run identity', async () => {
  const paths = []; const updates = [];
  const result = await observePlanner({ projectId: 'project-a', initial, delay: noDelay,
    read: async path => { paths.push(path); return complete; }, onUpdate: value => updates.push(value) });
  assert.equal(result, complete); assert.equal(paths.length, 1);
  assert.equal(paths[0], '/api/project-flowhive/projects/project-a/ai-planner/runs/run-a');
  assert.deepEqual(updates, [initial, complete]);
});
test('read retries are finite and cannot call a start endpoint', async () => {
  let calls = 0;
  await assert.rejects(observePlanner({ projectId: 'project-a', initial, delay: noDelay, onUpdate: () => {},
    read: async () => { calls++; throw new Error('connection unavailable'); } }), /connection unavailable/);
  assert.equal(calls, 3);
});
test('authorization failure is terminal for observation and is never retried', async () => {
  let calls = 0;
  await assert.rejects(observePlanner({ projectId: 'project-a', initial, delay: noDelay, onUpdate: () => {},
    read: async () => { calls++; throw Object.assign(new Error('denied'), { status: 403 }); } }), /denied/);
  assert.equal(calls, 1);
});
test('wrong-project and wrong-run responses are rejected before UI update', async () => {
  for (const next of [{ ...complete, projectId: 'other' }, { ...complete, runId: 'other' }]) {
    const updates = [];
    await assert.rejects(observePlanner({ projectId: 'project-a', initial, delay: noDelay,
      read: async () => next, onUpdate: item => updates.push(item) }), /identity mismatch/);
    assert.deepEqual(updates, [initial]);
  }
});
test('navigation cancellation blocks a late successful response', async () => {
  const controller = new AbortController(); const updates = [];
  await assert.rejects(observePlanner({ projectId: 'project-a', initial, delay: noDelay, signal: controller.signal,
    read: async () => { controller.abort(); return complete; }, onUpdate: item => updates.push(item) }), { name: 'AbortError' });
  assert.deepEqual(updates, [initial]);
});
test('observation limit does not fabricate terminal state or start more AI work', async () => {
  let time = 0; let reads = 0;
  const result = await observePlanner({ projectId: 'project-a', initial, now: () => time,
    maximumObservationMs: 10, delay: async () => { time += 10; }, onUpdate: () => {},
    read: async () => { reads++; return initial; } });
  assert.equal(result.terminal, false); assert.equal(reads, 1);
});
test('new PM edits or project switches prohibit applying completed AI output', () => {
  assert.equal(canApplyPlannerResult('project-a', 'project-a', 5, 5, complete), true);
  assert.equal(canApplyPlannerResult('project-a', 'project-a', 5, 6, complete), false);
  assert.equal(canApplyPlannerResult('project-a', 'project-b', 5, 5, complete), false);
  assert.equal(canApplyPlannerResult('project-a', 'project-a', 5, 5, { ...complete, plan: { projectId: 'other' } }), false);
  assert.equal(canApplyPlannerResult('project-a', 'project-a', 5, 5, initial), false);
});
test('waiting is interruptible without leaving a timer running', async () => {
  const controller = new AbortController(); const waiting = abortableDelay(60000, controller.signal);
  controller.abort(); await assert.rejects(waiting, { name: 'AbortError' });
});
test('network timeout includes stalled body reads', async () => {
  let stopped = false;
  const fake = async (path, { signal }) => ({ status: 200, headers: new Headers(), statusText: 'OK',
    text: () => new Promise((resolve, reject) => signal.addEventListener('abort', () => { stopped = true; reject(signal.reason); }, { once: true })) });
  await assert.rejects(boundedFetch('/api/test', {}, fake, 15), { name: 'TimeoutError' });
  assert.equal(stopped, true);
});
test('empty 204 replies remain valid', async () => {
  const response = await boundedFetch('/api/test', {}, async () => new Response(null, { status: 204 }));
  assert.equal(response.status, 204);
});
