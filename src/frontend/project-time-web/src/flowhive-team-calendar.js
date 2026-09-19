export function calendarRange(anchor, view = 'week') {
  const date = new Date(`${anchor}T00:00:00Z`);
  if (!Number.isFinite(date.getTime())) throw new Error('Choose a valid calendar date.');
  if (view === 'month') date.setUTCDate(1);
  else date.setUTCDate(date.getUTCDate() - (date.getUTCDay() + 6) % 7);
  const end = new Date(date);
  if (view === 'month') end.setUTCMonth(end.getUTCMonth() + 1);
  else end.setUTCDate(end.getUTCDate() + 7);
  const days = [];
  for (const day = new Date(date); day < end; day.setUTCDate(day.getUTCDate() + 1)) days.push(day.toISOString().slice(0, 10));
  return { start: date.toISOString().slice(0, 10), end: end.toISOString().slice(0, 10), days };
}

export function moveCalendar(anchor, view, offset) {
  const range = calendarRange(anchor, view);
  const date = new Date(`${range.start}T00:00:00Z`);
  if (view === 'month') date.setUTCMonth(date.getUTCMonth() + offset);
  else date.setUTCDate(date.getUTCDate() + offset * 7);
  return date.toISOString().slice(0, 10);
}

export function intervalsForDay(intervals, day) {
  const start = Date.parse(`${day}T00:00:00Z`);
  const end = start + 86400000;
  return (intervals || []).filter(item => Date.parse(item.start) < end && Date.parse(item.end) > start)
    .sort((a, b) => Date.parse(a.start) - Date.parse(b.start));
}
