export function localDateKey(now = new Date()) {
  return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-${String(now.getDate()).padStart(2, '0')}`;
}

export function calendarDate(value) {
  if (!value || !/^\d{4}-\d{2}-\d{2}$/.test(value)) return 'Not set';
  const date = new Date(`${value}T12:00:00`);
  if (!Number.isFinite(date.getTime())) return 'Not set';
  return new Intl.DateTimeFormat(undefined, { dateStyle: 'medium' }).format(date);
}

export function queueFlags(row, tracking, today = localDateKey()) {
  const active = row.isActive !== false && row.status !== 'archived';
  return {
    overdue: active && Boolean(tracking?.targetDate && tracking.targetDate < today),
    blocked: active && Boolean(tracking?.blockerReason?.trim()),
    urgent: active && tracking?.priority === 'urgent'
  };
}

export function selectQueue(rows, trackingById, filter = 'all', sort = 'updated', today = localDateKey()) {
  const selected = rows.filter(row => filter === 'all' || queueFlags(row, trackingById[row.engagementId], today)[filter] === true);
  if (sort === 'target') selected.sort((a, b) => (trackingById[a.engagementId]?.targetDate || '9999-12-31')
    .localeCompare(trackingById[b.engagementId]?.targetDate || '9999-12-31'));
  if (sort === 'priority') {
    const rank = { urgent: 0, high: 1, normal: 2, low: 3 };
    selected.sort((a, b) => (rank[trackingById[a.engagementId]?.priority] ?? 4) - (rank[trackingById[b.engagementId]?.priority] ?? 4));
  }
  return selected;
}
