import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
import { createDraftWriteQueue } from '../src/frontend/project-time-web/src/module001/timesheet-draft-writer.js';

// Execute the actual application handlers with a controlled transport and editor.
const source = fs.readFileSync(new URL('../src/frontend/project-time-web/src/App.jsx', import.meta.url), 'utf8');
function harness(overrides = {}) {
  const calls = [];
  const context = vm.createContext({
    draftWrite: { current: createDraftWriteQueue() }, draftMutation: { current: false },
    draftScope: { current: 'week-a' }, draftHydrationScope: { current: 'week-a' },
    draftRevision: { current: 1 }, draftDirty: true, isSaving: false,
    isAnyDayEditable: true, selectedEntryIsEditable: true,
    selectedWeekStart: 'week-a', selectedCell: { date: 'day-a' },
    buildTimesheetPayload: () => ({ entries: [{ workDate: 'day-a', hours: 8 }, { workDate: 'day-b', hours: 4 }] }),
    getEntriesMissingDescriptions: () => [], getDayTotal: () => 8, formatNumber: String,
    setSaveStatus: () => {}, setSubmissionStatus: () => {}, setAiSuggestionState: () => {},
    setTimesheet: () => {}, window: { alert: () => {} },
    postProjectPulse051DTimeEntryJson: async (url, payload) => { calls.push({ url, payload }); return { timesheet: {} }; },
    ...overrides
  });
  context.setIsSaving = value => { context.isSaving = value; };
  context.setDraftDirty = value => { context.draftDirty = value; };
  context.setSelectedCell = value => { context.selectedCell = value; };
  context.setSelectedWeekStart = value => { context.selectedWeekStart = value; };
  for (const name of ['autoSaveDraft', 'closeEntryDetails', 'changeTimesheetWeek', 'submitSelectedDay']) {
    const start = source.indexOf(`  async function ${name}(`);
    assert.ok(start >= 0, name);
    const end = source.indexOf('\n  }', start) + 4;
    vm.runInContext(source.slice(start, end), context);
  }
  return { context, calls };
}
function deferred() { let resolve; const promise = new Promise(r => { resolve = r; }); return { promise, resolve }; }

test('closing waits for persistence and keeps newer typing open', async () => {
  const pending = deferred();
  const { context: c } = harness({ postProjectPulse051DTimeEntryJson: () => pending.promise });
  const close = c.closeEntryDetails();
  await Promise.resolve();
  c.draftRevision.current++;
  pending.resolve({});
  await close;
  assert.notEqual(c.selectedCell, null);
  assert.equal(c.draftDirty, true);
});

test('failed draft prevents week navigation and retains unsaved editor', async () => {
  const { context: c } = harness({ postProjectPulse051DTimeEntryJson: async () => { throw Error('offline'); } });
  await c.changeTimesheetWeek('week-b');
  assert.equal(c.selectedWeekStart, 'week-a');
  assert.equal(c.draftDirty, true);
});

test('submission locks before pending save settles, saves all days, and submits only selected day', async () => {
  const { context: c, calls } = harness();
  const pending = deferred();
  c.draftWrite.current.enqueue(() => pending.promise);
  const submit = c.submitSelectedDay();
  assert.equal(c.draftMutation.current, true);
  assert.equal(c.isSaving, true);
  assert.equal(await c.autoSaveDraft(), false);
  await c.submitSelectedDay();
  assert.equal(calls.length, 0);
  pending.resolve();
  await submit;
  assert.deepEqual(calls.map(call => call.url), ['/api/timesheets/week/draft', '/api/timesheets/day/submit']);
  assert.equal(calls[0].payload.entries.length, 2);
  assert.equal(calls[1].payload.entries.length, 1);
  assert.equal(c.draftMutation.current, false);
});

test('identity change cancels a waiting submission', async () => {
  const { context: c, calls } = harness();
  const pending = deferred();
  c.draftWrite.current.enqueue(() => pending.promise);
  const submit = c.submitSelectedDay();
  c.draftScope.current = 'another-user';
  pending.resolve();
  await submit;
  assert.equal(calls.length, 0);
  assert.equal(c.draftMutation.current, false);
});
