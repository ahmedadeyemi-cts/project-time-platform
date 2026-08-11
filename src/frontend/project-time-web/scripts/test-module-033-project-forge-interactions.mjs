import assert from 'node:assert/strict';
import {
  calendarTasksInRange,
  decisionPatch,
  groupCurrencyTotals,
  projectedOccurrenceDates,
  projectedOccurrenceDatesInRange,
  shiftedSchedule,
  statusForKanban,
  taskOccursOn,
  taskSource,
  taskStatus
} from '../src/project-forge/projectForgeModel.js';

const cases = [];
const test = (name, run) => cases.push({ name, run });

const withFixedLocalDate = (value, run) => {
  const RealDate = globalThis.Date;
  const [year, month, day] = value.split('-').map(Number);
  const fixedTime = new RealDate(year, month - 1, day, 12, 0, 0, 0).getTime();
  globalThis.Date = class FixedDate extends RealDate {
    constructor(...args) {
      super(...(args.length ? args : [fixedTime]));
    }

    static now() {
      return fixedTime;
    }
  };
  try {
    return run();
  } finally {
    globalThis.Date = RealDate;
  }
};

test('all six Kanban lanes map to a consistent canonical status and progress', () => {
  assert.deepEqual(statusForKanban('backlog', 75), { status: 'not_started', taskStatus: 'not_started', percentComplete: 75 });
  assert.deepEqual(statusForKanban('ready', 100), { status: 'not_started', taskStatus: 'not_started', percentComplete: 0 });
  assert.deepEqual(statusForKanban('in_progress', 0), { status: 'in_progress', taskStatus: 'in_progress', percentComplete: 1 });
  assert.deepEqual(statusForKanban('review', 100), { status: 'in_progress', taskStatus: 'in_progress', percentComplete: 99 });
  assert.deepEqual(statusForKanban('blocked', 35), { status: 'blocked', taskStatus: 'blocked', percentComplete: 35 });
  assert.deepEqual(statusForKanban('done', 0), { status: 'completed', taskStatus: 'completed', percentComplete: 100 });
});

test('Decision Matrix moves persist the quadrant and its importance flags together', () => {
  assert.deepEqual(decisionPatch('do'), { decisionAction: 'do', important: true, urgent: true });
  assert.deepEqual(decisionPatch('decide'), { decisionAction: 'decide', important: true, urgent: false });
  assert.deepEqual(decisionPatch('delegate'), { decisionAction: 'delegate', important: false, urgent: true });
  assert.deepEqual(decisionPatch('delete'), { decisionAction: 'delete', important: false, urgent: false });
});

test('review workflow status remains an active work state', () => {
  assert.equal(taskStatus({ status: 'in_review' }), 'in_progress');
});

test('calendar movement preserves the task span in the client preview', () => {
  assert.deepEqual(
    shiftedSchedule({ startDate: '2026-08-03', dueDate: '2026-08-07' }, '2026-08-14'),
    { startDate: '2026-08-10', dueDate: '2026-08-14' }
  );
  assert.equal(taskOccursOn({ startDate: '2026-08-03', dueDate: '2026-08-07' }, '2026-08-05'), true);
  assert.equal(taskOccursOn({ startDate: '2026-08-03', dueDate: '2026-08-07' }, '2026-08-08'), false);
});

test('recurrence preview is bounded and never creates persisted task identities', () => {
  withFixedLocalDate('2026-08-09', () => {
    assert.deepEqual(
      projectedOccurrenceDates({
        taskType: 'recurring',
        startDate: '2026-08-03',
        recurrenceRule: { frequency: 'weekly', interval: 1, endDate: '2026-08-31', active: true }
      }),
      ['2026-08-10', '2026-08-17', '2026-08-24', '2026-08-31']
    );
  });
});

test('live and review-plan task identities remain explicit', () => {
  assert.equal(taskSource({ taskId: 'live' }), 'canonical');
  assert.equal(taskSource({ planTaskId: 'draft' }), 'review_plan');
  assert.equal(taskSource({ recordSource: 'review_plan' }), 'review_plan');
});

test('budget totals remain separated by recorded currency', () => {
  assert.deepEqual(groupCurrencyTotals([
    { expenseUploadId: 'usd-1', currency: 'USD', totalAmount: 100 },
    { currency: 'usd', totalAmount: 25 },
    { currency: 'EUR', totalAmount: 80 },
    { expenseUploadId: 'unknown-1', currency: '', totalAmount: 10 },
    { expenseUploadId: 'unknown-2', currency: 'US', totalAmount: 20 }
  ]), [
    { currency: 'EUR', total: 80, key: 'currency:EUR' },
    { currency: 'USD', total: 125, key: 'currency:USD' },
    { currency: null, total: 10, key: 'currency-unavailable:unknown-1' },
    { currency: null, total: 20, key: 'currency-unavailable:unknown-2' }
  ]);
});

test('calendar recurrence projection is bounded to the visible range and rule end date', () => {
  const weekly = { taskId: 'weekly', taskType: 'recurring', startDate: '2026-01-05', dueDate: '2026-01-06', recurrenceRule: { frequency: 'weekly', interval: 1, endDate: '2026-02-02', active: true } };
  assert.deepEqual(projectedOccurrenceDatesInRange(weekly, '2026-01-12', '2026-01-31'), ['2026-01-12', '2026-01-19', '2026-01-26']);
  assert.deepEqual(projectedOccurrenceDatesInRange({ ...weekly, recurrenceRule: { ...weekly.recurrenceRule, active: false } }, '2026-01-01', '2026-02-28'), []);
  assert.equal(projectedOccurrenceDatesInRange({ ...weekly, recurrenceRule: { frequency: 'daily', interval: 1, active: true } }, '2026-01-06', '2026-12-31', 4).length, 4);
  assert.deepEqual(projectedOccurrenceDatesInRange({ ...weekly, startDate: '2024-01-31', recurrenceRule: { frequency: 'monthly', interval: 1, active: true } }, '2024-02-01', '2024-03-31'), ['2024-02-29', '2024-03-31']);
  assert.deepEqual(projectedOccurrenceDatesInRange({ ...weekly, startDate: '2024-02-29', recurrenceRule: { frequency: 'yearly', interval: 1, active: true } }, '2025-01-01', '2028-12-31'), ['2025-02-28', '2026-02-28', '2027-02-28', '2028-02-29']);
  const projected = calendarTasksInRange([weekly], '2026-01-12', '2026-01-18');
  assert.equal(projected.length, 2);
  assert.equal(projected[1].recurrenceProjection, true);
  assert.equal(projected[1].plannedEndDate, '2026-01-13');
  assert.equal(projected[1].recurrenceCanonicalTask, weekly);
});

for (const { name, run } of cases) {
  try {
    await run();
    console.log(`PASS ${name}`);
  } catch (error) {
    console.error(`FAIL ${name}`);
    throw error;
  }
}

console.log(`MODULE_033_INTERACTION_MODEL=PASS cases=${cases.length}`);
