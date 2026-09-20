export function operationTimestamp(value) {
  if (typeof value === 'number') return Number.isFinite(value) ? value : null;
  const parsed = typeof value === 'string' && value ? Date.parse(value) : NaN;
  return Number.isFinite(parsed) ? parsed : null;
}

export function operationDuration(startedAt, endedAt) {
  const start = operationTimestamp(startedAt), end = operationTimestamp(endedAt);
  const seconds = start === null || end === null ? 0 : Math.max(0, Math.floor((end - start) / 1000));
  return [Math.floor(seconds / 3600), Math.floor(seconds / 60) % 60, seconds % 60]
    .map(value => String(value).padStart(2, '0')).join(':');
}
