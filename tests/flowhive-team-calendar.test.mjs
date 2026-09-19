import test from 'node:test';
import assert from 'node:assert/strict';
import { calendarRange, moveCalendar, intervalsForDay } from '../src/frontend/project-time-web/src/flowhive-team-calendar.js';

test('weeks start Monday and cross year boundaries with exclusive end', () => {
  const range = calendarRange('2027-01-01');
  assert.equal(range.start, '2026-12-28');
  assert.equal(range.end, '2027-01-04');
  assert.equal(range.days.length, 7);
  assert.equal(range.days.at(-1), '2027-01-03');
});
test('month navigation clamps to first day rather than skipping February', () => {
  assert.equal(moveCalendar('2027-01-31', 'month', 1), '2027-02-01');
  assert.equal(moveCalendar('2027-01-31', 'month', -1), '2026-12-01');
  assert.equal(calendarRange('2028-02-20', 'month').days.length, 29);
});
test('week navigation handles daylight saving dates as UTC dates', () => {
  assert.equal(moveCalendar('2026-03-08', 'week', 1), '2026-03-09');
  assert.equal(moveCalendar('2026-11-01', 'week', 1), '2026-11-02');
});
test('events crossing midnight appear on both days without leaking end-exclusive events', () => {
  const intervals = [
    { start: '2026-09-19T23:00:00Z', end: '2026-09-20T02:00:00Z' },
    { start: '2026-09-18T22:00:00Z', end: '2026-09-19T00:00:00Z' },
    { start: '2026-09-19T10:00:00Z', end: '2026-09-19T11:00:00Z' }
  ];
  assert.deepEqual(intervalsForDay(intervals, '2026-09-19'), [intervals[2], intervals[0]]);
  assert.deepEqual(intervalsForDay(intervals, '2026-09-20'), [intervals[0]]);
  assert.deepEqual(intervalsForDay([], '2026-09-19'), []);
});
test('invalid dates are rejected', () => assert.throws(() => calendarRange('invalid'), /valid calendar date/));
