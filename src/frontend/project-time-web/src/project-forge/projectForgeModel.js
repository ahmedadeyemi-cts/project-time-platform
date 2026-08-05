export const KANBAN_COLUMNS = Object.freeze([
  { id: 'backlog', label: 'Backlog' },
  { id: 'ready', label: 'Ready' },
  { id: 'in_progress', label: 'In Progress' },
  { id: 'blocked', label: 'Blocked' },
  { id: 'review', label: 'Review' },
  { id: 'done', label: 'Done' }
]);

export const DECISION_QUADRANTS = Object.freeze([
  { id: 'do', label: 'Do', help: 'Important and urgent', important: true, urgent: true },
  { id: 'decide', label: 'Decide / Schedule', help: 'Important, not urgent', important: true, urgent: false },
  { id: 'delegate', label: 'Delegate', help: 'Urgent, not important', important: false, urgent: true },
  { id: 'delete', label: 'Delete', help: 'Not important or urgent; this does not delete the task', important: false, urgent: false }
]);

export function normalize(value) {
  return String(value || '').trim().toLowerCase().replaceAll(' ', '_').replaceAll('-', '_');
}

export function normalizeCurrencyCode(value) {
  const code = String(value || '').trim().toUpperCase();
  return /^[A-Z]{3}$/.test(code) ? code : '';
}

export function groupCurrencyTotals(rows = []) {
  const totals = new Map();
  const unavailable = [];
  for (const [index, row] of rows.entries()) {
    const currency = normalizeCurrencyCode(row?.currency);
    const amount = Number(row?.totalAmount ?? row?.amount ?? 0);
    if (!Number.isFinite(amount)) continue;
    if (!currency) {
      unavailable.push({
        currency: null,
        total: amount,
        key: `currency-unavailable:${row?.expenseUploadId || row?.projectExpenseUploadId || row?.uploadId || index}`
      });
      continue;
    }
    totals.set(currency, (totals.get(currency) || 0) + amount);
  }
  const grouped = [...totals.entries()]
    .map(([currency, total]) => ({ currency, total, key: `currency:${currency}` }))
    .sort((left, right) => left.currency.localeCompare(right.currency));
  return [...grouped, ...unavailable];
}

export function title(value) {
  return String(value || 'Not set')
    .replaceAll('_', ' ')
    .replace(/\b\w/g, (letter) => letter.toUpperCase());
}

export function iso(value) {
  return value ? String(value).slice(0, 10) : '';
}

export function parseDateOnly(value) {
  const text = iso(value);
  if (!/^\d{4}-\d{2}-\d{2}$/.test(text)) return null;
  const [year, month, day] = text.split('-').map(Number);
  const date = new Date(year, month - 1, day, 12, 0, 0, 0);
  return Number.isNaN(date.getTime()) ? null : date;
}

export function toDateOnly(date) {
  if (!(date instanceof Date) || Number.isNaN(date.getTime())) return '';
  const year = date.getFullYear();
  const month = String(date.getMonth() + 1).padStart(2, '0');
  const day = String(date.getDate()).padStart(2, '0');
  return `${year}-${month}-${day}`;
}

export function addDays(value, amount) {
  const date = parseDateOnly(value);
  if (!date) return '';
  date.setDate(date.getDate() + Number(amount || 0));
  return toDateOnly(date);
}

export function daysBetween(left, right) {
  const start = parseDateOnly(left);
  const end = parseDateOnly(right);
  if (!start || !end) return 0;
  return Math.round((end.getTime() - start.getTime()) / 86400000);
}

export function shortDate(value) {
  const date = parseDateOnly(value);
  return date
    ? date.toLocaleDateString(undefined, { month: 'short', day: 'numeric', year: 'numeric' })
    : value ? String(value) : 'Not scheduled';
}

export function taskId(task) {
  return task?.planTaskId || task?.projectForgePlanTaskId || task?.taskId || task?.canonicalTaskId || '';
}

export function projectId(project) {
  return project?.projectId || project?.id || '';
}

export function taskSource(task) {
  const source = normalize(task?.recordSource || task?.workspace || task?.sourceKind);
  if (source === 'review_plan' || source === 'plan' || task?.planTaskId || task?.projectForgePlanTaskId) return 'review_plan';
  return 'canonical';
}

export function taskKey(task) {
  return `${taskSource(task)}:${taskId(task)}`;
}

export function taskStatus(task) {
  const status = normalize(task?.taskStatus || task?.status);
  if (['complete', 'completed', 'done'].includes(status)) return 'completed';
  if (['in_progress', 'in_review', 'active', 'started', 'review'].includes(status)) return 'in_progress';
  if (['blocked', 'delayed', 'on_hold'].includes(status)) return 'blocked';
  if (status === 'cancelled') return 'cancelled';
  return 'not_started';
}

export function taskKanban(task) {
  const explicit = normalize(task?.kanbanCategory || task?.kanban_category);
  if (KANBAN_COLUMNS.some((column) => column.id === explicit)) return explicit;
  const status = taskStatus(task);
  if (status === 'completed') return 'done';
  if (status === 'blocked') return 'blocked';
  if (status === 'in_progress') return 'in_progress';
  return 'backlog';
}

export function taskProgress(task) {
  return Math.max(0, Math.min(100, Number(task?.percentComplete ?? task?.progressPercent ?? 0)));
}

export function taskStart(task) {
  return iso(task?.plannedStartDate || task?.startDate || task?.scheduledStartDate);
}

export function taskEnd(task) {
  return iso(task?.plannedEndDate || task?.dueDate || task?.endDate || task?.scheduledEndDate);
}

export function taskEstimate(task) {
  return Number(task?.estimatedHours ?? task?.remainingEffortHours ?? task?.assignedHours ?? 0);
}

export function taskRevision(task) {
  const revision = Number(task?.revision ?? task?.revisionNumber ?? task?.planningRevision ?? task?.taskRevision ?? 0);
  return Number.isFinite(revision) && revision > 0 ? revision : null;
}

export function taskName(task) {
  return task?.taskName || task?.name || 'Untitled task';
}

export function taskCode(task) {
  return task?.taskCode || task?.wbsNumber || task?.wbsCode || '—';
}

export function taskDecision(task) {
  const explicit = normalize(task?.decisionAction);
  if (DECISION_QUADRANTS.some((quadrant) => quadrant.id === explicit)) return explicit;
  const important = Boolean(task?.isImportant ?? task?.important);
  const urgent = Boolean(task?.isUrgent ?? task?.urgent);
  return important ? (urgent ? 'do' : 'decide') : (urgent ? 'delegate' : 'delete');
}

export function decisionPatch(decisionAction) {
  const quadrant = DECISION_QUADRANTS.find((item) => item.id === decisionAction) || DECISION_QUADRANTS[2];
  return { decisionAction: quadrant.id, important: quadrant.important, urgent: quadrant.urgent };
}

export function statusForKanban(category, currentProgress = 0) {
  if (category === 'done') return { status: 'completed', taskStatus: 'completed', percentComplete: 100 };
  if (category === 'blocked') return { status: 'blocked', taskStatus: 'blocked', percentComplete: currentProgress };
  if (category === 'in_progress' || category === 'review') {
    return { status: 'in_progress', taskStatus: 'in_progress', percentComplete: Math.max(1, Math.min(99, currentProgress)) };
  }
  return { status: 'not_started', taskStatus: 'not_started', percentComplete: currentProgress >= 100 ? 0 : currentProgress };
}

export function shiftedSchedule(task, targetDate) {
  const currentStart = taskStart(task);
  const currentEnd = taskEnd(task);
  const anchor = currentEnd || currentStart;
  if (!anchor) return { startDate: targetDate, dueDate: targetDate };
  const delta = daysBetween(anchor, targetDate);
  return {
    startDate: currentStart ? addDays(currentStart, delta) : targetDate,
    dueDate: currentEnd ? addDays(currentEnd, delta) : targetDate
  };
}

export function taskOccursOn(task, date) {
  const value = iso(date);
  const start = taskStart(task);
  const end = taskEnd(task);
  if (!start && !end) return false;
  if (!start) return end === value;
  if (!end) return start === value;
  return start <= value && end >= value;
}

export function replaceTask(collection, target, replacement) {
  const key = taskKey(target);
  return (collection || []).map((task) => taskKey(task) === key ? { ...task, ...replacement } : task);
}

export function mergeMutationTask(result, fallback) {
  const authoritative = result?.task || result?.updatedTask || result?.canonicalTask || result?.planTask;
  if (authoritative) return authoritative;
  const revision = result?.revision ?? result?.version;
  return {
    ...fallback,
    ...(result?.changes || {}),
    ...(revision ? { revision, revisionNumber: revision, planningRevision: revision } : {})
  };
}

export function hasRecurrence(task) {
  if (normalize(task?.taskType) === 'recurring') return true;
  const rule = task?.recurrenceRule;
  if (!rule) return false;
  if (typeof rule === 'string') {
    try { return Object.keys(JSON.parse(rule)).length > 0; } catch { return rule.trim().length > 2; }
  }
  return typeof rule === 'object' && Object.keys(rule).length > 0;
}

export function recurrenceConfig(task) {
  let rule = task?.recurrenceRule;
  if (typeof rule === 'string') {
    try { rule = JSON.parse(rule); } catch { rule = {}; }
  }
  rule = rule && typeof rule === 'object' ? rule : {};
  return {
    frequency: normalize(rule.frequency || rule.unit) || 'weekly',
    interval: Math.max(1, Number(rule.interval || 1)),
    endDate: iso(rule.endDate || rule.until),
    active: rule.active !== false
  };
}

export function projectedOccurrenceDates(task, count = 6) {
  const rule = recurrenceConfig(task);
  const first = parseDateOnly(taskStart(task));
  if (!first || !rule.active || !hasRecurrence(task)) return [];
  const end = parseDateOnly(rule.endDate);
  const today = parseDateOnly(toDateOnly(new Date()));
  const dates = [];
  const cursor = new Date(first);
  const advance = () => {
    if (rule.frequency === 'daily') cursor.setDate(cursor.getDate() + rule.interval);
    else if (rule.frequency === 'monthly') cursor.setMonth(cursor.getMonth() + rule.interval);
    else if (rule.frequency === 'yearly') cursor.setFullYear(cursor.getFullYear() + rule.interval);
    else cursor.setDate(cursor.getDate() + (7 * rule.interval));
  };
  for (let guard = 0; guard < 5000 && dates.length < count; guard += 1) {
    if (end && cursor > end) break;
    if (!today || cursor >= today) dates.push(toDateOnly(cursor));
    advance();
  }
  return dates;
}

function advanceRecurrenceDate(cursor, rule, anchor) {
  if (rule.frequency === 'daily') {
    cursor.setDate(cursor.getDate() + rule.interval);
    return;
  }
  if (rule.frequency === 'weekly') {
    cursor.setDate(cursor.getDate() + (7 * rule.interval));
    return;
  }
  if (rule.frequency === 'monthly') {
    const targetMonth = cursor.getMonth() + rule.interval;
    const targetYear = cursor.getFullYear() + Math.floor(targetMonth / 12);
    const normalizedMonth = ((targetMonth % 12) + 12) % 12;
    const lastDay = new Date(targetYear, normalizedMonth + 1, 0, 12).getDate();
    cursor.setFullYear(targetYear, normalizedMonth, Math.min(anchor.day, lastDay));
    return;
  }
  const targetYear = cursor.getFullYear() + rule.interval;
  const lastDay = new Date(targetYear, anchor.month + 1, 0, 12).getDate();
  cursor.setFullYear(targetYear, anchor.month, Math.min(anchor.day, lastDay));
}

export function projectedOccurrenceDatesInRange(task, rangeStart, rangeEnd, limit = 366) {
  const rule = recurrenceConfig(task);
  const first = parseDateOnly(taskStart(task));
  const lower = parseDateOnly(rangeStart);
  const upper = parseDateOnly(rangeEnd);
  if (!first || !lower || !upper || lower > upper || !rule.active || !hasRecurrence(task)) return [];
  const ruleEnd = parseDateOnly(rule.endDate);
  const cursor = new Date(first);
  const anchor = { month: first.getMonth(), day: first.getDate() };
  const dates = [];
  advanceRecurrenceDate(cursor, rule, anchor);
  for (let guard = 0; guard < 5000 && dates.length < Math.max(0, limit); guard += 1) {
    if (cursor > upper || (ruleEnd && cursor > ruleEnd)) break;
    if (cursor >= lower) dates.push(toDateOnly(cursor));
    advanceRecurrenceDate(cursor, rule, anchor);
  }
  return dates;
}

export function calendarTasksInRange(tasks, rangeStart, rangeEnd, limit = 366) {
  const projected = [];
  let remaining = Math.max(0, limit);
  for (const task of tasks || []) {
    if (!remaining) break;
    const originalStart = taskStart(task);
    if (!originalStart || !hasRecurrence(task) || !recurrenceConfig(task).active) continue;
    const durationDays = Math.max(0, daysBetween(originalStart, taskEnd(task) || originalStart));
    const anchors = projectedOccurrenceDatesInRange(task, addDays(rangeStart, -durationDays), rangeEnd, remaining);
    for (const occurrenceStart of anchors) {
      const occurrenceEnd = addDays(occurrenceStart, durationDays);
      if (occurrenceEnd < rangeStart || occurrenceStart > rangeEnd) continue;
      projected.push({
        ...task,
        startDate: occurrenceStart,
        plannedStartDate: occurrenceStart,
        dueDate: occurrenceEnd,
        plannedEndDate: occurrenceEnd,
        recurrenceProjection: true,
        recurrenceOccurrenceDate: occurrenceStart,
        recurrenceCanonicalTask: task
      });
      remaining -= 1;
      if (!remaining) break;
    }
  }
  return [...(tasks || []), ...projected];
}

export function recurrenceSummary(task) {
  const rule = recurrenceConfig(task);
  if (!hasRecurrence(task)) return 'Not recurring';
  const unit = rule.frequency === 'daily' ? 'day' : rule.frequency === 'monthly' ? 'month' : rule.frequency === 'yearly' ? 'year' : 'week';
  return `Every ${rule.interval} ${unit}${rule.interval === 1 ? '' : 's'}${rule.endDate ? ` until ${shortDate(rule.endDate)}` : ''}${rule.active ? '' : ' · inactive'}`;
}

export function clientMutationId() {
  if (globalThis.crypto?.randomUUID) return globalThis.crypto.randomUUID();
  return `forge-${Date.now()}-${Math.random().toString(16).slice(2)}`;
}
