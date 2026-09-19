import test from 'node:test';
import assert from 'node:assert/strict';
import { createDraftWriteQueue } from '../src/frontend/project-time-web/src/module001/timesheet-draft-writer.js';

test('slow draft cannot overwrite a newer edit or be overtaken by submission', async () => {
  const queue = createDraftWriteQueue();
  const stored = [];
  let release;
  const wait = new Promise(resolve => { release = resolve; });
  const first = queue.enqueue(async () => { await wait; stored.push('old note'); });
  const second = queue.enqueue(async () => { stored.push('latest note'); });
  await Promise.resolve();
  assert.deepEqual(stored, []);
  release();
  await queue.idle();
  stored.push('submit');
  await Promise.all([first, second]);
  assert.deepEqual(stored, ['old note', 'latest note', 'submit']);
});

test('a failed write rejects visibly but does not block a retry', async () => {
  const queue = createDraftWriteQueue();
  await assert.rejects(queue.enqueue(async () => { throw new Error('offline'); }), /offline/);
  assert.equal(await queue.enqueue(async () => 'saved'), 'saved');
});

test('a queued draft for a previous user or week is not sent', async () => {
  const queue = createDraftWriteQueue();
  let sameScope = true;
  let calls = 0;
  const pending = queue.enqueue(async () => { calls++; }, () => sameScope);
  sameScope = false;
  assert.equal(await pending, false);
  assert.equal(calls, 0);
});
