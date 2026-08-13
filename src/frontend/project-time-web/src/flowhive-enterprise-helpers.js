const PHASES = Object.freeze([
  { wbs: '1', name: 'Plan' },
  { wbs: '2', name: 'Design' },
  { wbs: '3', name: 'Implement' },
  { wbs: '4', name: 'Validate' },
  { wbs: '5', name: 'Release' }
]);

export const dependencyTypeHelp = Object.freeze({
  FS: 'Finish-to-Start: the predecessor must finish before the successor starts.',
  SS: 'Start-to-Start: the successor can start after the predecessor starts.',
  FF: 'Finish-to-Finish: the successor cannot finish before the predecessor finishes.',
  SF: 'Start-to-Finish: the successor cannot finish before the predecessor starts.'
});

function phaseName(wbs) {
  return PHASES.find((phase) => phase.wbs === String(wbs))?.name || 'Implement';
}

function clonePlan(plan) {
  return {
    ...plan,
    tasks: (plan?.tasks || []).map((task) => ({ ...task })),
    dependencies: (plan?.dependencies || []).map((dependency) => ({ ...dependency })),
    assignments: (plan?.assignments || []).map((assignment) => ({ ...assignment }))
  };
}

function taskKey(task) {
  return task.clientTaskId || task.canonicalTaskId || task.wbsNumber;
}

export function renumberFlowHivePlan(plan) {
  if (!plan) return plan;
  const source = clonePlan(plan);
  const summaries = new Map(source.tasks.filter((task) => task.isSummary).map((task) => [String(task.wbsNumber), task]));
  const children = source.tasks.filter((task) => !task.isSummary);
  const oldToNew = new Map();
  const orderedTasks = [];

  PHASES.forEach((phase) => {
    const summary = summaries.get(phase.wbs) || {
      clientTaskId: crypto.randomUUID(),
      canonicalTaskId: null,
      wbsNumber: phase.wbs,
      parentWbsNumber: '',
      name: phase.name,
      description: `${phase.name} phase summary.`,
      durationWorkingDays: 0,
      isMilestone: false,
      constraintType: 'ASAP',
      constraintDate: null,
      percentComplete: 0,
      remainingEffortHours: 0,
      status: 'not_started',
      isSummary: true,
      phase: phase.name,
      priority: 'summary'
    };
    orderedTasks.push({ ...summary, wbsNumber: phase.wbs, parentWbsNumber: '', phase: phase.name, isSummary: true });
    children
      .filter((task) => String(task.parentWbsNumber) === phase.wbs)
      .forEach((task, index) => {
        const nextWbs = `${phase.wbs}.${index + 1}`;
        oldToNew.set(String(task.wbsNumber), nextWbs);
        orderedTasks.push({ ...task, wbsNumber: nextWbs, parentWbsNumber: phase.wbs, phase: phase.name, isSummary: false });
      });
  });

  const validWbs = new Set(orderedTasks.filter((task) => !task.isSummary).map((task) => task.wbsNumber));
  const seenDependencies = new Set();
  const dependencies = source.dependencies.flatMap((dependency) => {
    const predecessorWbs = oldToNew.get(String(dependency.predecessorWbs)) || String(dependency.predecessorWbs || '');
    const successorWbs = oldToNew.get(String(dependency.successorWbs)) || String(dependency.successorWbs || '');
    if (!validWbs.has(predecessorWbs) || !validWbs.has(successorWbs) || predecessorWbs === successorWbs) return [];
    const key = `${predecessorWbs}|${successorWbs}`;
    if (seenDependencies.has(key)) return [];
    seenDependencies.add(key);
    return [{ ...dependency, predecessorWbs, successorWbs, type: dependency.type || 'FS' }];
  });

  const assignments = source.assignments.flatMap((assignment) => {
    const taskWbs = oldToNew.get(String(assignment.taskWbs)) || String(assignment.taskWbs || '');
    return validWbs.has(taskWbs) ? [{ ...assignment, taskWbs }] : [];
  });

  return { ...source, tasks: orderedTasks, dependencies, assignments };
}

export function addFlowHiveTask(plan, phaseWbs, createTask) {
  if (!plan) return plan;
  const phase = PHASES.find((candidate) => candidate.wbs === String(phaseWbs)) || PHASES[2];
  const source = clonePlan(plan);
  const children = source.tasks.filter((task) => !task.isSummary && String(task.parentWbsNumber) === phase.wbs);
  const provisionalWbs = `${phase.wbs}.${children.length + 1}`;
  const task = createTask(provisionalWbs, phase.wbs, `New ${phase.name.toLowerCase()} task`,
    'Describe the scoped action, required inputs, expected outputs, validation evidence, acceptance criteria, responsibilities, risks, and completion conditions.');
  const nextSummaryIndex = source.tasks.findIndex((candidate) => candidate.isSummary && Number(candidate.wbsNumber) > Number(phase.wbs));
  source.tasks.splice(nextSummaryIndex < 0 ? source.tasks.length : nextSummaryIndex, 0, task);

  const predecessor = children.at(-1)?.wbsNumber
    || source.tasks.filter((candidate) => !candidate.isSummary && Number(candidate.parentWbsNumber) < Number(phase.wbs)).at(-1)?.wbsNumber
    || '';
  if (predecessor) {
    source.dependencies.push({ predecessorWbs: predecessor, successorWbs: provisionalWbs, type: 'FS', lagWorkingDays: 0 });
  }
  return renumberFlowHivePlan(source);
}

export function deleteFlowHiveTask(plan, wbsNumber) {
  if (!plan) return plan;
  const source = clonePlan(plan);
  const target = source.tasks.find((task) => !task.isSummary && String(task.wbsNumber) === String(wbsNumber));
  if (!target) return source;

  const incoming = source.dependencies.find((dependency) => String(dependency.successorWbs) === String(wbsNumber));
  const outgoing = source.dependencies.filter((dependency) => String(dependency.predecessorWbs) === String(wbsNumber));
  source.tasks = source.tasks.filter((task) => taskKey(task) !== taskKey(target));
  source.assignments = source.assignments.filter((assignment) => String(assignment.taskWbs) !== String(wbsNumber));
  source.dependencies = source.dependencies.filter((dependency) =>
    String(dependency.predecessorWbs) !== String(wbsNumber)
    && String(dependency.successorWbs) !== String(wbsNumber));

  if (incoming?.predecessorWbs) {
    outgoing.forEach((dependency) => {
      if (String(incoming.predecessorWbs) !== String(dependency.successorWbs)) {
        source.dependencies.push({
          ...dependency,
          predecessorWbs: incoming.predecessorWbs,
          type: dependency.type || incoming.type || 'FS'
        });
      }
    });
  }
  return renumberFlowHivePlan(source);
}

export function moveFlowHiveTask(plan, sourceWbs, targetWbs, targetPhaseWbs, placement = 'before') {
  if (!plan || !sourceWbs) return plan;
  const source = clonePlan(plan);
  const sourceIndex = source.tasks.findIndex((task) => !task.isSummary && String(task.wbsNumber) === String(sourceWbs));
  if (sourceIndex < 0) return source;
  const [task] = source.tasks.splice(sourceIndex, 1);
  const phase = PHASES.find((candidate) => candidate.wbs === String(targetPhaseWbs))
    || PHASES.find((candidate) => candidate.wbs === String(task.parentWbsNumber))
    || PHASES[2];
  const moved = { ...task, parentWbsNumber: phase.wbs, phase: phase.name };

  let insertionIndex = targetWbs
    ? source.tasks.findIndex((candidate) => !candidate.isSummary && String(candidate.wbsNumber) === String(targetWbs))
    : -1;
  if (insertionIndex >= 0 && placement === 'after') insertionIndex += 1;
  if (insertionIndex < 0) {
    const nextSummaryIndex = source.tasks.findIndex((candidate) => candidate.isSummary && Number(candidate.wbsNumber) > Number(phase.wbs));
    insertionIndex = nextSummaryIndex < 0 ? source.tasks.length : nextSummaryIndex;
  }
  source.tasks.splice(insertionIndex, 0, moved);
  return renumberFlowHivePlan(source);
}

export function moveFlowHiveTaskByOffset(plan, wbsNumber, offset) {
  if (!plan || !offset) return plan;
  const task = plan.tasks.find((candidate) => !candidate.isSummary && String(candidate.wbsNumber) === String(wbsNumber));
  if (!task) return plan;
  const siblings = plan.tasks.filter((candidate) => !candidate.isSummary && candidate.parentWbsNumber === task.parentWbsNumber);
  const index = siblings.findIndex((candidate) => candidate.wbsNumber === task.wbsNumber);
  const target = siblings[index + offset];
  if (!target) return plan;
  return moveFlowHiveTask(plan, wbsNumber, target.wbsNumber, task.parentWbsNumber, offset > 0 ? 'after' : 'before');
}

export function workingDaysInclusive(startValue, endValue) {
  if (!startValue || !endValue) return 1;
  const start = new Date(`${startValue}T12:00:00Z`);
  const end = new Date(`${endValue}T12:00:00Z`);
  if (Number.isNaN(start.getTime()) || Number.isNaN(end.getTime()) || end < start) return 1;
  let days = 0;
  for (const cursor = new Date(start); cursor <= end; cursor.setUTCDate(cursor.getUTCDate() + 1)) {
    if (cursor.getUTCDay() !== 0 && cursor.getUTCDay() !== 6) days += 1;
  }
  return Math.max(1, days);
}

export function deriveFlowHiveExecutiveSummary(plan, schedule, enterprise, aiPreview, financials) {
  const tasks = (plan?.tasks || []).filter((task) => !task.isSummary);
  const completed = tasks.filter((task) => task.status === 'complete' || Number(task.percentComplete || 0) >= 100).length;
  const blocked = tasks.filter((task) => task.status === 'blocked').length;
  const inProgress = tasks.filter((task) => task.status === 'in_progress').length;
  const percentage = tasks.length
    ? Math.round(tasks.reduce((total, task) => total + Number(task.percentComplete || 0), 0) / tasks.length)
    : 0;
  const aiSummary = aiPreview?.detailedAnswer?.executiveSummary
    || aiPreview?.privatePlan?.objective
    || aiPreview?.detailedAnswer?.directConclusion
    || '';
  const budgetStatus = financials?.project?.budgetStatus || financials?.budgetStatus || 'not available';
  const openRaid = (enterprise?.raidItems || []).filter((item) => !['closed', 'resolved', 'rejected'].includes(item.status)).length;
  const scheduleText = schedule?.projectFinishDate
    ? `The current deterministic schedule forecasts completion on ${schedule.projectFinishDate}.`
    : 'The schedule requires recalculation after the latest plan changes.';
  const operational = `${completed} of ${tasks.length} executable tasks are complete, ${inProgress} are in progress, and ${blocked} are blocked. Overall task progress is approximately ${percentage}%.`;
  const governance = `${openRaid} open RAID item(s) require monitoring. Financial status is ${String(budgetStatus).replaceAll('_', ' ')}.`;
  return [aiSummary, operational, scheduleText, governance].filter(Boolean).join(' ');
}

export function phaseDefinitions() {
  return PHASES.map((phase) => ({ ...phase }));
}
