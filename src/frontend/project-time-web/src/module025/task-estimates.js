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
  if (!['toyota', 'hyundai'].includes(engagement.customerProgram)) {
    checks.push({ key: 'task-hours', label: 'Reviewed task hours in all five phases', complete: engagement.phases?.length === 5 && engagement.phases.every(p => taskTotal(p.tasks) !== null && taskTotal(p.tasks) === Number(p.finalHours)) });
  }
  return checks;
}
