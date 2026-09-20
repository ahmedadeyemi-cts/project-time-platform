export function localDateKey(date = new Date()) {
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;
}

export function validTargetDate(value) {
  if (!value) return true;
  if (!/^\d{4}-\d{2}-\d{2}$/.test(value)) return false;
  const [year, month, day] = value.split('-').map(Number);
  const date = new Date(year, month - 1, day, 12);
  return year >= 1900 && year <= 9999 && date.getFullYear() === year && date.getMonth() === month - 1 && date.getDate() === day;
}

export function formatTargetDate(value) {
  if (!value || !validTargetDate(value)) return 'Not set';
  const [year, month, day] = value.split('-').map(Number);
  return new Intl.DateTimeFormat(undefined, { dateStyle: 'medium' }).format(new Date(year, month - 1, day, 12));
}

export function isTrackingOverdue(targetDate, today = localDateKey()) {
  return Boolean(targetDate && validTargetDate(targetDate) && targetDate < today);
}

export function trackingIssues(form) {
  const issues = [];
  if (!validTargetDate(form.targetDate)) issues.push('Enter a valid target date.');
  if (!['low', 'normal', 'high', 'urgent'].includes(form.priority)) issues.push('Choose a supported priority.');
  const reason = String(form.blockerReason || '').trim();
  if (reason.length > 2000) issues.push('Keep the blocker explanation within 2,000 characters.');
  if (Boolean(reason) !== Boolean(form.blockerOwnerUserId)) issues.push('A blocker needs both an explanation and a responsible person. Clear both to remove it.');
  if (form.authoringHours !== '' && form.authoringHours != null) {
    const hours = Number(form.authoringHours);
    if (!Number.isFinite(hours) || hours < 0 || hours > 1000 || Math.abs(hours * 100 - Math.round(hours * 100)) > 0.000001)
      issues.push('Remaining SA authoring effort must be 0–1,000 hours, with at most two decimal places.');
  }
  return issues;
}

export function trackingForm(tracking = {}) {
  return { targetDate: tracking.targetDate || '', priority: tracking.priority || 'normal',
    blockerReason: tracking.blockerReason || '', blockerOwnerUserId: tracking.blockerOwnerUserId || '',
    authoringHours: tracking.authoringHours == null ? '' : String(tracking.authoringHours) };
}
