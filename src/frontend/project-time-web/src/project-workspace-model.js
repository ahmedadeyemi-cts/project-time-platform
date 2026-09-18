// Module 019 presentation model. Access is enforced by the workspace API.
export function knownNumber(value) {
  if (value === null || value === undefined || value === '') return null;
  const number = Number(value);
  return Number.isFinite(number) ? number : null;
}

export function workOptions(data = {}) {
  const projects = (data.projects || []).map(project => ({
    key: `project:${project.id}`, kind: 'project', id: project.id,
    code: project.projectCode, name: project.projectName, customer: project.clientName, project
  }));
  // Linked requests are shown with their project; requests without a project remain selectable.
  const requests = (data.resourceRequests || []).filter(request => !request.projectId && !['closed', 'completed', 'cancelled', 'canceled', 'archived'].includes(String(request.status || '').toLowerCase())).map(request => ({
    key: `request:${request.requestNumber}`, kind: 'request', id: request.projectIntakeRequestId,
    code: request.requestNumber, name: request.sourceName, customer: 'Service request', request
  }));
  return [...projects, ...requests].sort((a, b) => `${a.customer} ${a.code}`.localeCompare(`${b.customer} ${b.code}`));
}

export function documentsForWork(documents, work, requests = []) {
  if (!work) return [];
  const intakeIds = new Set(requests.filter(request => request.projectId === work.id).map(request => request.projectIntakeRequestId).filter(Boolean));
  const unique = new Map();
  for (const document of documents || []) {
    const matches = work.kind === 'project'
      ? document.projectId === work.id || (!document.projectId && intakeIds.has(document.projectIntakeRequestId))
      : work.id && document.projectIntakeRequestId === work.id;
    if (matches) unique.set(document.id, document);
  }
  return [...unique.values()];
}

export function tasksForProject(assignments, projectId) {
  const tasks = new Map();
  const seen = new Set();
  for (const row of assignments || []) {
    if (row.projectId !== projectId || seen.has(row.id)) continue;
    seen.add(row.id);
    const key = `${row.userId}:${row.taskId}`;
    const current = tasks.get(key);
    if (!current) tasks.set(key, { ...row, id: key, allocationCount: 1 });
    else {
      current.assignedHours += row.assignedHours;
      // The API's task usage is repeated across allocation records, not additional time.
      current.usedHours = Math.max(current.usedHours, row.usedHours);
      current.allocationCount += 1;
    }
  }
  return [...tasks.values()].map(row => ({ ...row, remainingHours: row.assignedHours - row.usedHours }));
}

export function projectHours(data, projectId, userId) {
  const tasks = tasksForProject(data.assignments, projectId);
  const team = new Map();
  for (const row of tasks) {
    const person = team.get(row.userId) || { userId: row.userId, name: row.engineerName, assigned: 0, logged: null, tasks: 0 };
    person.assigned += row.assignedHours;
    person.tasks += 1;
    team.set(row.userId, person);
  }
  const complete = Array.isArray(data.teamHours);
  if (complete) {
    for (const person of team.values()) person.logged = 0;
    for (const row of data.teamHours.filter(item => item.projectId === projectId)) {
      const person = team.get(row.userId) || { userId: row.userId, name: row.engineerName, assigned: 0, tasks: 0 };
      person.logged = knownNumber(row.loggedHours);
      team.set(row.userId, person);
    }
  }
  const people = [...team.values()].sort((a, b) => a.name.localeCompare(b.name));
  const assigned = tasks.reduce((sum, row) => sum + row.assignedHours, 0);
  const logged = complete && people.every(person => person.logged !== null) ? people.reduce((sum, person) => sum + person.logged, 0) : null;
  const mine = people.find(person => person.userId === userId);
  return { tasks, people, assigned, logged, remaining: logged === null ? null : assigned - logged, mine,
    myRemaining: mine?.logged != null ? mine.assigned - mine.logged : null };
}

export function hourExplanation(hours) {
  if (hours.logged === null) return 'Project time is unavailable. Remaining hours cannot be confirmed.';
  if (hours.assigned === 0) return hours.logged > 0
    ? `${hours.logged} hours are logged, but no current hours are allocated. Ask the project manager to confirm the plan.`
    : 'No current hours are allocated. Ask the project manager to confirm the plan.';
  if (hours.remaining < 0) return `${hours.logged} hours logged against ${hours.assigned} currently allocated: ${Math.abs(hours.remaining)} hours over the allocation.`;
  return `${hours.logged} of ${hours.assigned} currently allocated hours used; ${hours.remaining} hours available.`;
}
