export function projectOverview(plan, schedule, today, userId = '') {
  const dated = schedule?.valid ? new Map((schedule.tasks || []).map(task => [task.wbsNumber, task])) : new Map();
  const tasks = (plan?.tasks || []).filter(task => !task.isSummary && !task.isMilestone).map(task => {
    const assignments = (plan.assignments || []).filter(item => item.taskWbs === task.wbsNumber && item.resourceUserId);
    const due = dated.get(task.wbsNumber)?.endDate || '';
    const days = due ? Math.round((Date.parse(`${due}T00:00:00Z`) - Date.parse(`${today}T00:00:00Z`)) / 86400000) : null;
    const closed = Number(task.percentComplete) >= 100 || ['complete', 'completed', 'done', 'cancelled', 'canceled', 'archived'].includes(task.status);
    return { ...task, assignments, due, closed,
      mine: Boolean(userId) && assignments.some(item => item.resourceUserId === userId),
      overdue: !closed && days !== null && days < 0,
      dueSoon: !closed && days !== null && days >= 0 && days <= 3,
      unassigned: !closed && assignments.length === 0,
      blocked: !closed && task.status === 'blocked',
      critical: !closed && Boolean(dated.get(task.wbsNumber)?.isCritical) };
  });
  return { tasks, totals: Object.fromEntries(['overdue', 'dueSoon', 'unassigned', 'blocked', 'critical', 'mine']
    .map(key => [key, tasks.filter(task => !task.closed && task[key]).length])),
    completed: tasks.filter(task => task.closed).length,
    finish: schedule?.valid ? schedule.projectFinishDate : null,
    target: plan?.projectEndDate || null };
}

export function flowHiveProjectLink(hash, projects) {
  if (!String(hash).startsWith('#project-flowhive?')) return '';
  const id = new URLSearchParams(String(hash).split('?')[1]).get('projectId');
  return (projects || []).find(project => project.projectId === id)?.projectId || '';
}
