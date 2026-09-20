export function taskTotal(tasks) {
  if (!Array.isArray(tasks) || !tasks.length || tasks.length > 200) return null;
  let cents = 0;
  const ids = new Set();
  for (const task of tasks) {
    if (!task || !task.taskId || ids.has(task.taskId) || !String(task.description || '').trim()) return null;
    ids.add(task.taskId);
    if (task.hours === null || task.hours === undefined || task.hours === '') return null;
    const hours = Number(task.hours);
    if (!Number.isFinite(hours) || hours < 0 || hours > 100000 || Math.abs(hours * 100 - Math.round(hours * 100)) > 0.000001) return null;
    // Split fields are optional for legacy records, but must be supplied together.
    // A required after-hours window needs a positive allocation before SA review.
    if ((task.regularHours != null) !== (task.afterHours != null) || task.regularHours === '' || task.afterHours === '') return null;
    const after = Number(task.afterHours ?? 0);
    const regular = Number(task.regularHours ?? hours);
    if (![after, regular].every(value => Number.isFinite(value) && value >= 0 && value <= 100000 && Math.abs(value * 100 - Math.round(value * 100)) <= 0.000001)) return null;
    if (Math.round((regular + after) * 100) !== Math.round(hours * 100) || (!task.afterHoursRequired && after > 0) || (task.afterHoursRequired && after <= 0)) return null;
    cents += Math.round(hours * 100);
  }
  return cents > 99999999 ? null : cents / 100;
}

export function seedTasks(phase, id = () => crypto.randomUUID()) {
  const descriptions = [...new Set((phase.detailedActivities?.length ? phase.detailedActivities : phase.technicalTasks?.length ? phase.technicalTasks : [phase.objective]).filter(v => String(v || '').trim()))];
  // Never silently truncate scope. The editor can group tasks explicitly.
  if (descriptions.length > 200) throw new Error('Group this phase into at most 200 tasks before allocating hours.');
  return descriptions.map(description => ({ taskId: id(), description, hours: null, notes: '' }));
}

export function withTasks(phase, tasks) {
  const total = taskTotal(tasks);
  return { ...phase, tasks, ...(total === null ? {} : { finalHours: total }) };
}

export function proposeTasks(phase, id = () => crypto.randomUUID()) {
  // Existing tasks, including human edits, are never replaced by this action.
  if (phase.tasks?.length) return phase.tasks;
  const tasks = seedTasks(phase, id).map(task => ({ ...task, reviewed: false }));
  const hours = Number(phase.finalHours ?? phase.suggestedHours);
  if (!tasks.length || !Number.isFinite(hours) || hours < 0 || hours > 999999.99 || (hours === 0 && !phase.aiGenerated)) return tasks;
  const cents = Math.round(hours * 100);
  return tasks.map((task, index) => {
    const allocated = (Math.floor(cents / tasks.length) + (index < cents % tasks.length ? 1 : 0)) / 100;
    const suggested = /\b(cutover|outage|restart|reboot|production upgrade)\b/i.test(task.description);
    return { ...task, hours: allocated, regularHours: allocated, afterHours: 0,
      afterHoursRequired: false, afterHoursSuggested: suggested,
      afterHoursReason: suggested ? 'Potential service disruption. Confirm the customer maintenance window.' : '',
      reviewed: false, estimateBasis: 'Proposed equal allocation of the saved phase estimate. Review task complexity and dependencies before accepting.' };
  });
}

export function phaseTaskIssues(phase) {
  const tasks = phase.tasks || [];
  const issues = [];
  if (!tasks.length) return ['No task estimates. Propose tasks from the saved scope.'];
  tasks.forEach((task, index) => {
    if (taskTotal([task]) === null) issues.push(`Task ${index + 1}: complete its description and valid hours, including any after-hours allocation.`);
    else if (task.reviewed === false) issues.push(`Task ${index + 1}: confirm the proposed estimate and work window.`);
  });
  const total = taskTotal(tasks);
  if (!issues.length && total === null) issues.push('Task identifiers or the phase total are invalid. Reload the record before continuing.');
  if (total !== null && total !== Number(phase.finalHours)) issues.push('Task hours must equal the phase total.');
  return issues;
}

export function exportChecks(engagement) {
  if (!engagement) return [];
  const has = value => Boolean(String(value || '').trim());
  const checks = [
    { key: 'customer', label: 'Customer Name', complete: has(engagement.customerName) },
    { key: 'project', label: 'Project Name', complete: has(engagement.projectName) },
    { key: 'contract', label: 'Contract Type', complete: ['fixed', 'time_and_materials'].includes(engagement.commercialModel) },
    { key: 'sa', label: 'Solution Architect', complete: has(engagement.ownerDisplayName) },
    { key: 'ae', label: 'Account Executive', complete: Boolean(engagement.accountExecutiveUserId) },
    { key: 'saa', label: 'SAA / Inside Sales', complete: Boolean(engagement.resaleUserId) }
  ];
  if (!['toyota', 'hyundai'].includes(engagement.customerProgram) || engagement.phases?.some(p => p.tasks?.length)) {
    checks.push({ key: 'task-hours', label: 'Task estimates and work windows reviewed in all five phases', complete: engagement.phases?.length === 5 && engagement.phases.every(p => phaseTaskIssues(p).length === 0) });
  }
  return checks;
}
