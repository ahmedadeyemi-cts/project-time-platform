import { useEffect, useMemo, useRef, useState } from 'react';
import usSignalLogoUrl from '../brand/ussignal.png';
import ProjectForgeTaskDialog from './project-forge/ProjectForgeTaskDialog.jsx';
import {
  CalendarMonth,
  CalendarWeek,
  DecisionMatrix,
  Empty,
  GanttChart,
  KanbanBoard,
  Metric,
  Progress,
  TaskTable
} from './project-forge/ProjectForgeViews.jsx';
import { ProjectForgeApiError, projectForgeApi, projectForgeSend } from './project-forge/projectForgeApi.js';
import {
  decisionPatch,
  clientMutationId,
  groupCurrencyTotals,
  hasRecurrence,
  mergeMutationTask,
  normalize,
  normalizeCurrencyCode,
  projectId,
  replaceTask,
  shortDate,
  statusForKanban,
  taskEnd,
  taskEstimate,
  taskId,
  taskKanban,
  taskKey,
  taskProgress,
  taskRevision,
  taskSource,
  taskStart,
  taskStatus,
  title
} from './project-forge/projectForgeModel.js';
import './projectpulse-module-standard.css';
import './project-forge-center.css';

const WORKBOOK_TABS = Object.freeze([
  ['instructions', 'Instructions'],
  ['setup', 'Setup'],
  ['overall-dashboard', 'Overall Dashboard'],
  ['monthly-calendar', 'Monthly Calendar'],
  ['weekly-calendar', 'Weekly Calendar'],
  ['project-overview', 'Project Overview'],
  ['project-manager', 'Project Manager'],
  ['project-budget', 'Project Budget'],
  ['variable-tasks', 'Variable Tasks'],
  ['recurring-tasks', 'Recurring Tasks'],
  ['tasks-schedule', 'Tasks Schedule'],
  ['tasks-filter', 'Tasks Filter'],
  ['decision-matrix', 'Decision Matrix'],
  ['kanban-board', 'Kanban Board'],
  ['gantt-chart', 'Gantt Chart']
].map(([id, label]) => Object.freeze({ id, label })));

const STATUS_FILTERS = ['not_started', 'in_progress', 'blocked', 'completed', 'cancelled'];
const NEW_TASK_TABS = new Set(['variable-tasks', 'recurring-tasks', 'tasks-schedule', 'kanban-board', 'gantt-chart']);

function money(value, currency = 'USD') {
  const code = normalizeCurrencyCode(currency);
  if (!code) return `${Number(value || 0).toLocaleString(undefined, { maximumFractionDigits: 2 })} (currency unavailable)`;
  return new Intl.NumberFormat(undefined, { style: 'currency', currency: code, maximumFractionDigits: 0 }).format(Number(value || 0));
}

function hours(value) {
  return `${Number(value || 0).toLocaleString(undefined, { maximumFractionDigits: 1 })}h`;
}

function estimatedCost(task, bucket) {
  if (bucket === 'labor') return taskEstimate(task) * Number(task.hourlyRate || 0);
  if (bucket === 'materials') return Number(task.materialUnits || 0) * Number(task.materialUnitCost || 0);
  if (bucket === 'fixed') return Number(task.fixedCost || 0);
  if (bucket === 'travel') return Number(task.travelCost || 0);
  if (bucket === 'equipment') return Number(task.equipmentCost || 0);
  if (bucket === 'miscellaneous') return Number(task.miscCost ?? task.miscellaneousCost ?? 0);
  return 0;
}

function belongsToProject(row, selectedProjectId) {
  return !selectedProjectId || String(row.projectId) === String(selectedProjectId);
}

function aiDraftNotice(result) {
  const status = normalize(result?.status);
  const compositionStatus = normalize(result?.compositionStatus || result?.status);
  const target = normalize(result?.selectedTarget);
  const path = normalize(result?.primaryExecutionPath);
  const grounded = ['document_grounded_review_draft_created', 'celar_ai_solution_draft_completed', 'celar_ai_solution_draft_partial'].includes(status);
  const artifactDescription = compositionStatus === 'celar_ai_solution_draft_partial'
    ? 'A partial private, citation-grounded review scaffold was created.'
    : 'A private, citation-grounded review draft was created.';
  if (!grounded) return `${title(status)}. The selected target was ${title(target)} through ${title(path)}. No canonical task or assignment has been changed.`;
  if (target === 'celar_ai') return `${artifactDescription} Private Celar AI was the selected target. No canonical task or assignment has been changed.`;
  if (['claude', 'openai', 'open_ai'].includes(target)) return `${artifactDescription} ${target === 'claude' ? 'Claude' : 'OpenAI'} supplied only separate generic assistance through the governed route; it did not receive or replace the private project evidence. No canonical task or assignment has been changed.`;
  return `${artifactDescription} The route ended at governed local, whose output did not replace the private project evidence or artifact. No canonical task or assignment has been changed.`;
}

function recurrenceRule(form) {
  if (form.taskType !== 'recurring') return null;
  return {
    frequency: form.recurrenceFrequency,
    interval: Math.max(1, Number(form.recurrenceInterval || 1)),
    endDate: form.recurrenceEndDate || null,
    active: Boolean(form.recurrenceActive)
  };
}

function detailsPayload(form, includeClearFlags = true) {
  const recurring = recurrenceRule(form);
  return {
    taskName: form.taskName.trim(),
    description: form.description.trim(),
    taskType: form.taskType,
    phase: form.phase.trim(),
    priority: form.priority,
    durationWorkingDays: Number(form.durationWorkingDays || 0),
    estimatedHours: Number(form.estimatedHours || 0),
    hourlyRate: Number(form.hourlyRate || 0),
    materialUnits: Number(form.materialUnits || 0),
    materialUnitCost: Number(form.materialUnitCost || 0),
    fixedCost: Number(form.fixedCost || 0),
    travelCost: Number(form.travelCost || 0),
    equipmentCost: Number(form.equipmentCost || 0),
    miscCost: Number(form.miscCost || 0),
    ...(form.parentTaskId
      ? { parentTaskId: form.parentTaskId }
      : includeClearFlags ? { clearParentTask: true } : {}),
    ...(recurring
      ? { recurrenceRule: JSON.stringify(recurring) }
      : includeClearFlags ? { clearRecurrenceRule: true } : {})
  };
}

function estimatePayload(form, includeCosts) {
  return {
    estimatedHours: Number(form.estimatedHours || 0),
    reviewNote: form.reviewNote || 'Estimate reviewed in Project Forge.',
    ...(includeCosts ? {
      hourlyRate: Number(form.hourlyRate || 0),
      materialUnits: Number(form.materialUnits || 0),
      materialUnitCost: Number(form.materialUnitCost || 0),
      fixedCost: Number(form.fixedCost || 0),
      travelCost: Number(form.travelCost || 0),
      equipmentCost: Number(form.equipmentCost || 0),
      miscCost: Number(form.miscCost || 0)
    } : {})
  };
}

export default function ProjectForgeCenter() {
  const [activeTab, setActiveTab] = useState('instructions');
  const [data, setData] = useState(null);
  const dataRef = useRef(null);
  const loadSequence = useRef(0);
  const loadAbort = useRef(null);
  const tabButtons = useRef([]);
  const [selectedPm, setSelectedPm] = useState('');
  const [selectedProject, setSelectedProject] = useState('');
  const [workspace, setWorkspace] = useState('canonical');
  const [selectedPlan, setSelectedPlan] = useState('');
  const [selectedTask, setSelectedTask] = useState(null);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState('');
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');
  const [search, setSearch] = useState('');
  const [statusFilter, setStatusFilter] = useState('all');
  const [priorityFilter, setPriorityFilter] = useState('all');
  const [aiOpen, setAiOpen] = useState(false);
  const [aiOutcome, setAiOutcome] = useState('Create a detailed, reviewable project plan with tasks, dependencies, realistic engineering estimates, acceptance criteria, risks, handoff, and closeout based on the authorized project documents.');
  const [generatedDraft, setGeneratedDraft] = useState(null);
  const [reviewerId, setReviewerId] = useState('');

  function commitData(next) {
    dataRef.current = next;
    setData(next);
  }

  function resetWorkspaceEphemera() {
    setGeneratedDraft(null);
    setReviewerId('');
    setAiOpen(false);
    setSelectedTask(null);
    setNotice('');
    setError('');
  }

  async function load({
    pm = selectedPm,
    project = selectedProject,
    workspaceValue = workspace,
    planIdValue = selectedPlan
  } = {}) {
    const sequence = ++loadSequence.current;
    loadAbort.current?.abort();
    const controller = new AbortController();
    loadAbort.current = controller;
    setLoading(true);
    setError('');
    try {
      const result = await projectForgeApi.bootstrap({ projectManagerUserId: pm, projectId: project, workspace: workspaceValue, planId: planIdValue, signal: controller.signal });
      if (sequence !== loadSequence.current) return;
      commitData(result);
      const availableProjects = result.projects || [];
      const serverSelectedProject = result.selectedProjectId || result.summary?.selectedProjectId || '';
      const nextProject = availableProjects.some((item) => String(projectId(item)) === String(serverSelectedProject || project))
        ? String(serverSelectedProject || project)
        : projectId(availableProjects[0]) || '';
      setSelectedProject(String(nextProject || ''));
      if (result.access?.selectedProjectManagerUserId !== undefined) setSelectedPm(String(result.access.selectedProjectManagerUserId || pm || ''));
      if (workspaceValue === 'review_plan') setSelectedPlan(String(planIdValue || result.selectedPlanId || result.plan?.planId || ''));
    } catch (loadError) {
      if (loadError?.name !== 'AbortError' && sequence === loadSequence.current) setError(loadError.message || 'Project Forge could not be loaded.');
    } finally {
      if (sequence === loadSequence.current) setLoading(false);
    }
  }

  useEffect(() => {
    load({ pm: '', project: '', workspaceValue: 'canonical', planIdValue: '' });
    return () => loadAbort.current?.abort();
  }, []);

  const projects = data?.projects || [];
  const projectManagers = data?.projectManagers || data?.selectableProjectManagers || [];
  const plans = data?.plans || (data?.plan ? [data.plan] : []);
  const rawTasks = workspace === 'review_plan' ? (data?.planTasks || data?.tasks || []) : (data?.tasks || data?.planTasks || []);
  const allTasks = rawTasks.map((task) => ({
    ...task,
    recordSource: task.recordSource || workspace,
    ...(workspace === 'review_plan' ? {
      planId: task.planId || selectedPlan,
      isAssignedReviewer: String(task.reviewerUserId || '') === String(data?.access?.effectiveUserId || ''),
      canCompleteReview: String(task.reviewerUserId || '') === String(data?.access?.effectiveUserId || '') && Boolean(task.canEditEstimate)
    } : {})
  }));
  const assignments = data?.assignments || [];
  const dependencies = data?.taskDependencies || data?.dependencies || [];
  const holidays = data?.holidays || [];
  const expenses = data?.expenses || [];
  const activity = data?.activity || data?.activityEvents || [];
  const currentProject = projects.find((item) => String(projectId(item)) === String(selectedProject)) || projects[0] || null;
  const currentProjectId = currentProject ? projectId(currentProject) : '';
  const projectPlans = plans.filter((plan) => String(plan.projectId) === String(currentProjectId));
  const currentPlan = projectPlans.find((plan) => String(plan.planId) === String(selectedPlan)) || projectPlans[0] || null;
  const projectTasks = allTasks.filter((task) => belongsToProject(task, currentProjectId) && task.active !== false && task.isActive !== false && !task.archivedAt && taskStatus(task) !== 'cancelled');
  const filteredTasks = projectTasks.filter((task) => {
    const haystack = `${task.taskCode || ''} ${task.wbsNumber || ''} ${task.taskName || task.name || ''} ${task.taskDescription || task.description || ''} ${task.assigneeName || ''}`.toLowerCase();
    return (!search || haystack.includes(search.toLowerCase()))
      && (statusFilter === 'all' || taskStatus(task) === statusFilter)
      && (priorityFilter === 'all' || normalize(task.priorityCode || task.priority) === priorityFilter);
  });
  const currentAssignments = assignments.filter((item) => belongsToProject(item, currentProjectId));
  const currentExpenses = expenses.filter((item) => belongsToProject(item, currentProjectId));
  const currentActivity = activity.filter((item) => !item.projectId || belongsToProject(item, currentProjectId));
  const currency = normalizeCurrencyCode(currentProject?.plannedCurrency || currentProject?.currency || data?.setup?.currency);
  const authoritativePlannedCost = currentProject?.plannedTotalProjectCost ?? currentProject?.plannedCost;
  const plannedCost = authoritativePlannedCost == null || !Number.isFinite(Number(authoritativePlannedCost))
    ? null
    : Number(authoritativePlannedCost);
  const expenseTotalsByCurrency = groupCurrencyTotals(currentExpenses);
  const matchingExpenseTotal = currency
    ? expenseTotalsByCurrency.find((total) => total.currency === currency)?.total || 0
    : null;
  const plannedVarianceAvailable = plannedCost != null && Boolean(currency);
  const actualHours = projectTasks.reduce((sum, task) => sum + Number(task.actualHours || task.usedHours || 0), 0);
  const estimatedHours = projectTasks.reduce((sum, task) => sum + taskEstimate(task), 0);
  const progress = projectTasks.length ? projectTasks.reduce((sum, task) => sum + taskProgress(task), 0) / projectTasks.length : 0;
  const portfolioTaskCount = projects.reduce((sum, project) => sum + Number(project.taskCount || 0), 0);
  const portfolioOpenTaskCount = projects.reduce((sum, project) => sum + Number(project.openTaskCount ?? Math.max(0, Number(project.taskCount || 0) - Number(project.completedTaskCount || 0))), 0);
  const portfolioDueThisMonthCount = projects.reduce((sum, project) => sum + Number(project.dueThisMonthCount || 0), 0);
  const portfolioEstimatedHours = projects.reduce((sum, project) => sum + Number(project.estimatedHours || 0), 0);
  const portfolioActualHours = projects.reduce((sum, project) => sum + Number(project.actualHours || 0), 0);
  const portfolioProgress = portfolioTaskCount
    ? projects.reduce((sum, project) => sum + Number(project.progressPercent || 0) * Number(project.taskCount || 0), 0) / portfolioTaskCount
    : 0;
  const canManage = Boolean(data?.access?.canManage && !data?.access?.isViewAs);
  const canUseAi = Boolean(data?.access?.canUseAi && !data?.access?.isViewAs);
  const canMoveWorkflow = canManage || Boolean(data?.access?.canUpdateAssignedTaskStatus && !data?.access?.isViewAs);
  const canEditEstimate = Boolean(data?.access?.canEditAssignedEstimate && !data?.access?.isViewAs) || canManage;
  const canViewCosts = Boolean((data?.access?.canViewFinancials || data?.access?.canViewCosts) && !data?.access?.isViewAs) || canManage;
  const canSelectPm = Boolean(data?.access?.canSelectProjectManager);
  const aiConnection = data?.ai?.module064Connection || null;
  const projectEvidenceMissing = Boolean(
    currentProjectId
      && aiConnection
      && Number(aiConnection.projectReadyDocumentCount || 0) === 0
  );
  const engineers = useMemo(() => {
    const candidates = [
      ...(data?.eligibleReviewers || []),
      ...(data?.projectTeam || []),
      ...(data?.engineers || []),
      ...currentAssignments.filter((item) => item.isReviewerEligible)
    ].filter((item) => item.isReviewerEligible !== false);
    return candidates.reduce((values, item) => {
      const id = item.resourceUserId || item.userId || item.id;
      if (id && !values.some((entry) => String(entry.id) === String(id))) values.push({ id, name: item.resourceName || item.displayName || item.userName || item.name || item.email });
      return values;
    }, []);
  }, [data, currentAssignments]);

  function patchTaskState(target, patch) {
    const current = dataRef.current;
    if (!current) return;
    const next = { ...current };
    ['tasks', 'planTasks'].forEach((name) => { if (Array.isArray(current[name])) next[name] = replaceTask(current[name], target, patch); });
    commitData(next);
    setSelectedTask((open) => open && taskKey(open) === taskKey(target) ? { ...open, ...patch } : open);
  }

  async function mutateTask(task, optimisticPatch, invoke, successMessage, busyKey = 'task') {
    const snapshot = dataRef.current;
    setBusy(`${busyKey}-${taskId(task)}`);
    setError(''); setNotice('');
    patchTaskState(task, optimisticPatch);
    try {
      const result = await invoke(task);
      const authoritative = mergeMutationTask(result, { ...task, ...optimisticPatch });
      patchTaskState(task, authoritative);
      if (successMessage) setNotice(successMessage);
      return authoritative;
    } catch (mutationError) {
      commitData(snapshot);
      if (mutationError instanceof ProjectForgeApiError && mutationError.status === 409) {
        setSelectedTask(null);
        await load({ pm: selectedPm, project: currentProjectId, workspaceValue: workspace, planIdValue: selectedPlan });
        setError('This task changed after you opened it. Project Forge reloaded the latest revision; review it and try again.');
      } else {
        setSelectedTask(task);
        setError(mutationError.message || 'The task update could not be saved.');
      }
      return null;
    } finally {
      setBusy('');
    }
  }

  async function refreshReviewPlanAfter(task, changed, closeDialog = false) {
    if (!changed || taskSource(task) !== 'review_plan') return;
    if (closeDialog) setSelectedTask(null);
    await load({ pm: selectedPm, project: currentProjectId, workspaceValue: 'review_plan', planIdValue: task.planId || task.projectForgePlanId || selectedPlan });
  }

  async function moveWorkflow(task, category, position = {}) {
    const orderOnly = taskKanban(task) === category && Boolean(position.beforeTaskId || position.afterTaskId);
    const workflow = orderOnly
      ? { status: normalize(task.taskStatus || task.status) || 'not_started', percentComplete: taskProgress(task), blockedReason: task.blockedReason || null }
      : { ...statusForKanban(category, taskProgress(task)), blockedReason: category === 'blocked' ? task.blockedReason || '' : null };
    const changed = await mutateTask(task, { kanbanCategory: category, ...workflow }, (current) => projectForgeApi.updateWorkflow(current, category, { ...position, workflow }), orderOnly ? 'Task order saved.' : 'Task workflow saved. Associated people will be notified through Module 065.', 'workflow');
    if (changed) {
      // Reordering may revise neighboring cards in the same lane. Reload the
      // authoritative lane revisions so the next edit does not use stale data.
      await load({ pm: selectedPm, project: currentProjectId, workspaceValue: workspace, planIdValue: selectedPlan });
    }
  }

  async function moveSchedule(task, startDate, dueDate, interaction = 'move') {
    const changed = await mutateTask(task, { plannedStartDate: startDate, startDate, plannedEndDate: dueDate, dueDate }, (current) => projectForgeApi.updateSchedule(current, startDate, dueDate, interaction), 'Task schedule saved. Associated people will be notified through Module 065.', 'schedule');
    await refreshReviewPlanAfter(task, changed);
  }

  async function moveDecision(task, action) {
    const patch = decisionPatch(action);
    const changed = await mutateTask(task, { decisionAction: action, isImportant: patch.important, isUrgent: patch.urgent }, (current) => projectForgeApi.updateDecision(current, patch), 'Decision priority saved.', 'decision');
    await refreshReviewPlanAfter(task, changed);
  }

  async function saveTask(task, form, permissions) {
    if (task.isNew) {
      setBusy('create-task'); setError(''); setNotice('');
      try {
        const decision = decisionPatch(form.decisionAction);
        const result = await projectForgeApi.createTask(currentProjectId, {
          ...detailsPayload(form, false),
          status: form.status,
          kanbanCategory: form.kanbanCategory,
          percentComplete: Number(form.percentComplete || 0),
          blockedReason: form.blockedReason || null,
          startDate: form.startDate || null, dueDate: form.dueDate || null,
          billable: true,
          assigneeUserId: form.assigneeUserId || null,
          decisionAction: form.decisionAction,
          important: decision.important,
          urgent: decision.urgent
        });
        const created = mergeMutationTask(result, task);
        const current = dataRef.current || {};
        commitData({ ...current, tasks: [...(current.tasks || []), created] });
        setSelectedTask(created);
        setNotice('Live task created. The assigned Engineer and associated project participants will be notified through Module 065.');
        return created;
      } catch (createError) {
        setError(createError.message || 'The task could not be created.');
        return null;
      } finally { setBusy(''); }
    }

    const changes = {};
    const optimisticPatch = {};
    if ((permissions.managerDetails || permissions.reviewContent) && permissions.dirty.details) {
      const patch = permissions.managerDetails ? detailsPayload(form) : {
        description: form.description.trim(),
        durationWorkingDays: Number(form.durationWorkingDays || 0),
        estimatedHours: Number(form.estimatedHours || 0),
        ...(permissions.estimate && permissions.financial ? {
          hourlyRate: Number(form.hourlyRate || 0),
          materialUnits: Number(form.materialUnits || 0),
          materialUnitCost: Number(form.materialUnitCost || 0),
          fixedCost: Number(form.fixedCost || 0),
          travelCost: Number(form.travelCost || 0),
          equipmentCost: Number(form.equipmentCost || 0),
          miscCost: Number(form.miscCost || 0)
        } : {})
      };
      changes.details = patch;
      Object.assign(optimisticPatch, patch);
    } else if (permissions.estimate && permissions.dirty.details) {
      const { reviewNote: _reviewNote, ...patch } = estimatePayload(form, permissions.financial);
      changes.details = patch;
      Object.assign(optimisticPatch, patch);
    }
    if (permissions.workflow && permissions.dirty.workflow) {
      const workflow = { status: form.status, percentComplete: Number(form.percentComplete || 0), blockedReason: form.blockedReason || null };
      changes.workflow = { ...workflow, kanbanCategory: form.kanbanCategory };
      Object.assign(optimisticPatch, workflow, { kanbanCategory: form.kanbanCategory });
    }
    if (permissions.schedule && permissions.dirty.schedule) {
      const interaction = permissions.dirty.scheduleStart && permissions.dirty.scheduleDue
        ? 'set_range'
        : permissions.dirty.scheduleStart ? 'resize_start' : 'resize_end';
      changes.schedule = { interaction, startDate: form.startDate || null, dueDate: form.dueDate || null, cascadeSuccessors: false };
      Object.assign(optimisticPatch, { startDate: form.startDate, plannedStartDate: form.startDate, dueDate: form.dueDate, plannedEndDate: form.dueDate });
    }
    if (permissions.decision && permissions.dirty.decision) {
      const patch = decisionPatch(form.decisionAction);
      changes.decision = patch;
      Object.assign(optimisticPatch, { decisionAction: form.decisionAction, isImportant: patch.important, isUrgent: patch.urgent });
    }
    const hasChanges = Object.keys(changes).length > 0;
    const current = hasChanges
      ? await mutateTask(task, optimisticPatch, (value) => projectForgeApi.updateCompositeTask(value, changes), '', 'composite')
      : task;
    if (!current) return null;
    if (hasChanges) {
      setSelectedTask(null);
      await load({ pm: selectedPm, project: currentProjectId, workspaceValue: workspace, planIdValue: selectedPlan });
    } else {
      setSelectedTask(current);
    }
    setNotice(hasChanges ? 'Task changes saved atomically. Associated people will be notified through Module 065.' : 'No task fields changed.');
    return current;
  }

  async function assignTask(task, form) {
    const engineer = engineers.find((item) => String(item.id) === String(form.assigneeUserId));
    const changed = await mutateTask(task, { assigneeUserId: form.assigneeUserId, assigneeName: engineer?.name, assignedHours: Number(form.assignedHours || 0), allocationPercent: Number(form.allocationPercent || 0) }, (current) => projectForgeApi.assignTask(current, {
      userId: form.assigneeUserId,
      assignedHours: Number(form.assignedHours || 0),
      allocationPercent: Number(form.allocationPercent || 0),
      startDate: form.startDate || null,
      endDate: form.dueDate || null
    }), 'Assignment saved. The assignee and associated project participants will be notified through Module 065.', 'assignment');
    await refreshReviewPlanAfter(task, changed, true);
  }

  async function saveEstimate(task, form) {
    const changed = await mutateTask(task, estimatePayload(form, canViewCosts), (current) => projectForgeApi.saveEstimate(current, estimatePayload(form, canViewCosts)), 'The estimate was saved; review remains open until explicitly completed.', 'estimate');
    await refreshReviewPlanAfter(task, changed, true);
  }

  async function completeReview(task, reviewNote, decision) {
    const planIdValue = task.planId || task.projectForgePlanId || selectedPlan;
    const changed = await mutateTask(task, { reviewStatus: decision, reviewNote }, (current) => projectForgeApi.completeReview(planIdValue, current, reviewNote, decision), decision === 'completed' ? 'Review completed. The Project Manager will be notified through Module 065.' : 'Changes requested. The Project Manager and associated reviewer will be notified through Module 065.', 'review');
    await refreshReviewPlanAfter(task, changed, true);
  }

  async function archiveTask(task) {
    if (!window.confirm(`Archive “${task.taskName || task.name}”? This keeps history and cancels active planning work.`)) return;
    const result = await mutateTask(task, { active: false, status: 'cancelled', taskStatus: 'cancelled' }, (current) => projectForgeApi.archiveTask(current, 'Archived by an authorized Project Forge user.'), 'Task archived. Associated people will be notified through Module 065.', 'archive');
    if (result) setSelectedTask(null);
    await refreshReviewPlanAfter(task, result, true);
  }

  async function addDependency(task, dependency) {
    setBusy('dependency'); setError('');
    try {
      const result = await projectForgeApi.createDependency(currentProjectId, task, dependency);
      const edge = result.dependency;
      const current = dataRef.current || {};
      const name = Array.isArray(current.taskDependencies) ? 'taskDependencies' : 'dependencies';
      commitData({ ...current, [name]: [...(current[name] || []), edge] });
      setNotice('Dependency saved. The schedule will use the authoritative project dependency.');
      if (workspace === 'review_plan') {
        await load({ pm: selectedPm, project: currentProjectId, workspaceValue: workspace, planIdValue: selectedPlan });
      }
    } catch (dependencyError) {
      if (dependencyError.status === 409) await load({ pm: selectedPm, project: currentProjectId, workspaceValue: workspace, planIdValue: selectedPlan });
      setError(dependencyError.message || 'The dependency could not be saved.');
    } finally { setBusy(''); }
  }

  async function deleteDependency(task, dependency) {
    setBusy('dependency'); setError('');
    try {
      await projectForgeApi.deleteDependency(task, dependency);
      const current = dataRef.current || {};
      const name = Array.isArray(current.taskDependencies) ? 'taskDependencies' : 'dependencies';
      const id = dependency.taskDependencyId || dependency.dependencyId;
      commitData({ ...current, [name]: (current[name] || []).filter((edge) => String(edge.taskDependencyId || edge.dependencyId) !== String(id)) });
      setNotice('Dependency removed.');
      if (workspace === 'review_plan') {
        await load({ pm: selectedPm, project: currentProjectId, workspaceValue: workspace, planIdValue: selectedPlan });
      }
    } catch (dependencyError) {
      if (dependencyError.status === 409) await load({ pm: selectedPm, project: currentProjectId, workspaceValue: workspace, planIdValue: selectedPlan });
      setError(dependencyError.message || 'The dependency could not be removed.');
    } finally { setBusy(''); }
  }

  async function generateAiDraft() {
    if (!currentProjectId) return;
    setBusy('ai'); setError(''); setNotice('');
    try {
      const result = await projectForgeSend(`/api/project-forge/projects/${currentProjectId}/ai-drafts`, 'POST', { requestedOutcome: aiOutcome, detailLevel: 'comprehensive', allowSanitizedExternalFallback: true });
      const draft = result.draft || result;
      setGeneratedDraft(draft);
      if (draft.planId) {
        setWorkspace('review_plan');
        setSelectedPlan(String(draft.planId));
      }
      setNotice(aiDraftNotice(result));
      await load({
        pm: selectedPm,
        project: currentProjectId,
        workspaceValue: draft.planId ? 'review_plan' : workspace,
        planIdValue: draft.planId || (workspace === 'review_plan' ? selectedPlan : '')
      });
    } catch (generationError) { setError(generationError.message); } finally { setBusy(''); }
  }

  async function assignReviewer() {
    const draftId = generatedDraft?.planId || generatedDraft?.aiDraftId || generatedDraft?.draftId || currentPlan?.planId;
    if (!draftId || !reviewerId) return;
    if (workspace !== 'review_plan') {
      setError('Open the Review Plan workspace before assigning its Engineer reviewer.');
      return;
    }
    const reviewTasks = projectTasks.filter((task) => taskSource(task) === 'review_plan');
    const expectedTaskRevisions = Object.fromEntries(reviewTasks.filter((task) => taskRevision(task)).map((task) => [String(taskId(task)), taskRevision(task)]));
    if (!reviewTasks.length || Object.keys(expectedTaskRevisions).length !== reviewTasks.length) {
      setError('Refresh this Review Plan before assigning its Engineer reviewer.');
      return;
    }
    setBusy('reviewer'); setError('');
    try {
      await projectForgeSend(`/api/project-forge/ai-drafts/${draftId}/assign-reviewer`, 'POST', {
        reviewerUserId: reviewerId,
        planTaskIds: null,
        reviewNote: 'Review and modify the proposed Project Forge estimate.',
        expectedPlanRevision: taskRevision(currentPlan || generatedDraft),
        expectedTaskRevisions,
        clientMutationId: clientMutationId()
      });
      setNotice('The Engineer review was assigned. Module 065 will notify the reviewer and associated project participants.');
      await load({ pm: selectedPm, project: currentProjectId, workspaceValue: workspace, planIdValue: selectedPlan });
    } catch (assignError) { setError(assignError.message); } finally { setBusy(''); }
  }

  async function adoptPlan() {
    const planIdValue = generatedDraft?.planId || currentPlan?.planId;
    if (!planIdValue || !window.confirm('Adopt this human-reviewed plan into canonical project tasks and assignments?')) return;
    setBusy('adopt'); setError('');
    try {
      await projectForgeSend(`/api/project-forge/plans/${planIdValue}/adopt`, 'POST', {
        confirmation: 'ADOPT PROJECT FORGE PLAN',
        createAssignments: true,
        adoptionNote: 'Human-reviewed Project Forge plan adopted from the Project Forge workspace.',
        expectedPlanRevision: taskRevision(currentPlan || generatedDraft),
        clientMutationId: clientMutationId()
      });
      setNotice('The reviewed plan was adopted into canonical project task and assignment records.');
      setWorkspace('canonical'); setSelectedPlan(''); setGeneratedDraft(null); setReviewerId('');
      await load({ pm: selectedPm, project: currentProjectId, workspaceValue: 'canonical', planIdValue: '' });
    } catch (adoptError) { setError(adoptError.message); } finally { setBusy(''); }
  }

  function openNewTask() {
    setSelectedTask({
      isNew: true,
      recordSource: 'canonical',
      projectId: currentProjectId,
      taskName: '',
      taskType: activeTab === 'recurring-tasks' ? 'recurring' : 'variable',
      priority: 'normal',
      status: 'not_started',
      kanbanCategory: 'backlog',
      percentComplete: 0
    });
  }

  function changePm(value) {
    resetWorkspaceEphemera(); setSelectedPm(value); setWorkspace('canonical'); setSelectedPlan(''); setSelectedProject('');
    load({ pm: value, project: '', workspaceValue: 'canonical', planIdValue: '' });
  }

  function changeProject(value) {
    resetWorkspaceEphemera(); setSelectedProject(value); setWorkspace('canonical'); setSelectedPlan('');
    load({ pm: selectedPm, project: value, workspaceValue: 'canonical', planIdValue: '' });
  }

  function changeWorkspace(value) {
    resetWorkspaceEphemera(); setWorkspace(value);
    const nextPlan = value === 'review_plan' ? String(projectPlans[0]?.planId || '') : '';
    setSelectedPlan(nextPlan);
    if (value === 'review_plan' && !nextPlan) { setError('No review plan exists for this project. Generate an AI plan first or select another project.'); return; }
    load({ pm: selectedPm, project: currentProjectId, workspaceValue: value, planIdValue: nextPlan });
  }

  function changePlan(value) {
    resetWorkspaceEphemera(); setSelectedPlan(value);
    load({ pm: selectedPm, project: currentProjectId, workspaceValue: 'review_plan', planIdValue: value });
  }

  function handleTabKeyDown(event, index) {
    const keyDelta = { ArrowRight: 1, ArrowDown: 1, ArrowLeft: -1, ArrowUp: -1 }[event.key];
    let nextIndex = keyDelta === undefined ? index : (index + keyDelta + WORKBOOK_TABS.length) % WORKBOOK_TABS.length;
    if (event.key === 'Home') nextIndex = 0;
    else if (event.key === 'End') nextIndex = WORKBOOK_TABS.length - 1;
    else if (keyDelta === undefined) return;
    event.preventDefault();
    setActiveTab(WORKBOOK_TABS[nextIndex].id);
    window.requestAnimationFrame(() => tabButtons.current[nextIndex]?.focus());
  }

  function renderTab() {
    switch (activeTab) {
      case 'instructions': return <div className="forge-instructions"><section><h3>Project Forge workflow</h3><ol><li>Select a live project within your server-authorized scope.</li><li>Choose Live Project for canonical work or Review Plan for an isolated proposal.</li><li>Edit tasks through the table, calendar, decision matrix, Kanban, or Gantt views.</li><li>Use Celar AI to draft a plan from authorized SOW, GSD, design, and supporting project documents.</li><li>Assign the draft to an eligible Engineer, explicitly complete review, then let a Project Manager adopt it.</li></ol></section><section><h3>Governed integrations</h3><dl><div><dt>Project data</dt><dd>Canonical projects, tasks, assignments, time, expenses, documents, holidays, and identities</dd></div><div><dt>AI</dt><dd>Module 064 routing with private project-document grounding and deterministic scheduling</dd></div><div><dt>Notifications</dt><dd>Module 065 events for assignments and material task updates</dd></div><div><dt>Your scope</dt><dd>{data?.access?.scopeLabel || data?.access?.scope || 'Server-authorized project scope'}</dd></div></dl></section></div>;
      case 'setup': return <div className="forge-setup-grid"><section><h3>Authoritative setup</h3><dl><div><dt>Currency</dt><dd>{currency}</dd></div><div><dt>Working days</dt><dd>{(data?.setup?.workingDays || ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday']).join(', ')}</dd></div><div><dt>Statuses</dt><dd>{(data?.setup?.statuses || STATUS_FILTERS).map(title).join(', ')}</dd></div><div><dt>Priorities</dt><dd>{(data?.setup?.priorities || ['low', 'normal', 'high', 'critical']).map(title).join(', ')}</dd></div></dl></section><section><h3>Project team</h3>{currentAssignments.length ? <ul>{currentAssignments.map((item) => <li key={item.assignmentId || `${item.userId}-${item.taskId}`}><b>{item.resourceName || item.displayName || item.userName || item.email}</b><span>{item.taskName || item.roleName || 'Project assignment'} · {hours(item.assignedHours)}</span></li>)}</ul> : <Empty />}</section><section><h3>Company holidays</h3>{holidays.length ? <ul>{holidays.slice(0, 20).map((holiday) => <li key={holiday.companyHolidayId || holiday.holidayId || holiday.holidayDate || holiday.date}><b>{holiday.holidayName || holiday.name}</b><span>{shortDate(holiday.holidayDate || holiday.date)}</span></li>)}</ul> : <Empty />}</section></div>;
      case 'overall-dashboard': return <><div className="forge-metrics"><Metric label="Projects in scope" value={projects.length} /><Metric label="Open tasks" value={portfolioOpenTaskCount} /><Metric label="Tasks due this month" value={portfolioDueThisMonthCount} /><Metric label="Portfolio estimate" value={hours(portfolioEstimatedHours)} /><Metric label="Actual hours" value={hours(portfolioActualHours)} /><Metric label="Overall progress" value={`${Math.round(portfolioProgress)}%`} /></div><div className="forge-dashboard-grid"><section><h3>Project status</h3>{projects.map((project) => <div className="forge-project-line" key={projectId(project)}><button type="button" className="forge-project-link" onClick={() => { changeProject(String(projectId(project))); setActiveTab('project-overview'); }}><span>{project.projectCode} · {project.projectName}</span></button><b>{title(project.status)}</b></div>)}</section><section><h3>Upcoming tasks for selected project</h3><TaskTable tasks={projectTasks.filter((task) => taskStatus(task) !== 'completed').sort((a, b) => (taskEnd(a) || '9999-12-31').localeCompare(taskEnd(b) || '9999-12-31')).slice(0, 8)} onOpenTask={setSelectedTask} /></section></div></>;
      case 'monthly-calendar': return <CalendarMonth tasks={projectTasks} holidays={holidays} canManage={canManage} onOpenTask={setSelectedTask} onMoveSchedule={moveSchedule} />;
      case 'weekly-calendar': return <CalendarWeek tasks={projectTasks} holidays={holidays} canManage={canManage} onOpenTask={setSelectedTask} onMoveSchedule={moveSchedule} />;
      case 'project-overview': return currentProject ? <><div className="forge-project-hero"><div><span>{currentProject.projectCode}</span><h3>{currentProject.projectName}</h3><p>{currentProject.projectDescription || currentProject.description || 'No project description is available.'}</p></div><Progress value={progress} /></div><div className="forge-metrics"><Metric label="Status" value={title(currentProject.status)} /><Metric label="Project Manager" value={currentProject.projectManagerName || 'Unassigned'} /><Metric label="Start" value={shortDate(currentProject.startDate)} /><Metric label="End" value={shortDate(currentProject.endDate)} /><Metric label="Estimated" value={hours(estimatedHours)} /><Metric label="Actual" value={hours(actualHours)} /></div><TaskTable tasks={projectTasks} onOpenTask={setSelectedTask} /></> : <Empty>Select a project within your authorized scope.</Empty>;
      case 'project-manager': return <div className="forge-table-wrap"><table className="forge-table"><thead><tr><th>Project</th><th>PM</th><th>Status</th><th>Dates</th><th>Tasks</th><th>Progress</th>{canViewCosts ? <th>Planned cost</th> : null}</tr></thead><tbody>{projects.map((project) => <tr key={projectId(project)}><td><button type="button" className="forge-project-link" onClick={() => { changeProject(String(projectId(project))); setActiveTab('project-overview'); }}><b>{project.projectCode}</b><span>{project.projectName}</span></button></td><td>{project.projectManagerName || 'Unassigned'}</td><td>{title(project.status)}</td><td>{shortDate(project.startDate)} – {shortDate(project.endDate)}</td><td>{Number(project.taskCount || 0)}</td><td><Progress value={Number(project.progressPercent || 0)} /></td>{canViewCosts ? <td>{project.plannedTotalProjectCost == null && project.plannedCost == null ? 'Not available' : money(project.plannedTotalProjectCost ?? project.plannedCost, currency)}</td> : null}</tr>)}</tbody></table></div>;
      case 'project-budget': return canViewCosts ? <><div className="forge-metrics"><Metric label="Planned project cost" value={plannedCost == null ? 'Not available' : money(plannedCost, currency)} hint={currency ? `Governed project currency: ${currency}` : 'Authoritative currency unavailable'} />{expenseTotalsByCurrency.map((total) => <Metric key={total.key} label={`${total.currency ? 'Current uploads' : 'Current upload'} · ${total.currency || 'Currency unavailable'}`} value={money(total.total, total.currency)} hint={total.currency && total.currency === currency ? `Included in the ${currency} variance` : 'Planned-cost variance unavailable for this total'} />)}<Metric label="Estimated labor" value={hours(estimatedHours)} /><Metric label="Actual labor" value={hours(actualHours)} /><Metric label={currency ? `Planned cost less ${currency} uploads` : 'Planned cost variance'} value={plannedVarianceAvailable ? money(plannedCost - matchingExpenseTotal, currency) : 'Unavailable'} /></div><p className="forge-note forge-budget-currency-note">Expense uploads are totaled separately by their recorded currency. Project Forge does not convert or sum unlike currencies. Uploads without a valid currency remain individual amounts and are never combined.{expenseTotalsByCurrency.some((total) => total.currency !== currency) ? ` Only ${currency || 'a matching project currency'} uploads are included in the planned-cost variance; other totals remain separate.` : ''}{!plannedVarianceAvailable ? ' Variance is unavailable because an authoritative planned amount and currency are both required.' : ''}</p><div className="forge-budget-bars">{['labor', 'materials', 'fixed', 'travel', 'equipment', 'miscellaneous'].map((bucket) => { const value = projectTasks.reduce((sum, task) => sum + estimatedCost(task, bucket), 0); return <div key={bucket}><span>{title(bucket)}</span><b>{money(value, currency)}</b><i style={{ width: `${plannedCost ? Math.min(100, (value / plannedCost) * 100) : 0}%` }} /></div>; })}</div><section className="forge-expenses"><h3>Expense tracker</h3><p className="forge-note">These are current project-linked uploads. Approval and accounting status come from the expense authority and are not inferred by Project Forge.</p>{currentExpenses.length ? <div className="forge-table-wrap"><table className="forge-table"><thead><tr><th>Period / upload</th><th>Owner</th><th>Lines</th><th>Total</th><th>Approval status</th></tr></thead><tbody>{currentExpenses.map((item) => <tr key={item.expenseUploadId || item.projectExpenseUploadId || item.uploadId}><td>{shortDate(item.periodStart || item.uploadedAt)}</td><td>{item.ownerName || item.expenseOwnerName || 'Project team'}</td><td>{item.lineCount || 0}</td><td>{money(item.totalAmount, item.currency)}</td><td>{title(item.approvalStatus || item.status || 'not_available')}</td></tr>)}</tbody></table></div> : <Empty />}</section></> : <Empty>Project budget and expense amounts are restricted to authorized financial and project-management roles.</Empty>;
      case 'variable-tasks': return <TaskTable tasks={projectTasks.filter((task) => normalize(task.taskType) !== 'recurring')} onOpenTask={setSelectedTask} showDecision />;
      case 'recurring-tasks': return <TaskTable tasks={projectTasks.filter(hasRecurrence)} onOpenTask={setSelectedTask} showRecurrence />;
      case 'tasks-schedule': return <TaskTable tasks={[...projectTasks].sort((a, b) => taskStart(a).localeCompare(taskStart(b)))} onOpenTask={setSelectedTask} />;
      case 'tasks-filter': return <><div className="forge-filters"><label>Search<input value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Task, owner, code…" /></label><label>Status<select value={statusFilter} onChange={(event) => setStatusFilter(event.target.value)}><option value="all">All statuses</option>{STATUS_FILTERS.map((value) => <option key={value} value={value}>{title(value)}</option>)}</select></label><label>Priority<select value={priorityFilter} onChange={(event) => setPriorityFilter(event.target.value)}><option value="all">All priorities</option>{['low', 'normal', 'high', 'critical'].map((value) => <option key={value} value={value}>{title(value)}</option>)}</select></label></div><TaskTable tasks={filteredTasks} onOpenTask={setSelectedTask} showDecision /></>;
      case 'decision-matrix': return <DecisionMatrix tasks={projectTasks} canManage={canManage} onOpenTask={setSelectedTask} onMoveDecision={moveDecision} />;
      case 'kanban-board': return <KanbanBoard tasks={projectTasks} canManage={canMoveWorkflow} onOpenTask={setSelectedTask} onMoveWorkflow={moveWorkflow} />;
      case 'gantt-chart': return <GanttChart tasks={projectTasks} dependencies={dependencies} canManage={canManage} onOpenTask={setSelectedTask} onMoveSchedule={moveSchedule} />;
      default: return null;
    }
  }

  return (
    <div className="project-forge projectpulse-module-standard">
      <header className="forge-header">
        <div className="forge-brand"><img src={usSignalLogoUrl} alt="US Signal" /><span>MODULE 033</span><h2>Project Forge</h2><p>Live project planning, governed estimates, and document-grounded AI.</p></div>
        <div className="forge-header-controls">
          {canSelectPm ? <label>Project Manager<select value={selectedPm} onChange={(event) => changePm(event.target.value)}><option value="">All authorized PMs</option>{projectManagers.map((pm) => <option key={pm.userId || pm.projectManagerUserId} value={pm.userId || pm.projectManagerUserId}>{pm.name || pm.displayName || pm.projectManagerName || pm.email}</option>)}</select></label> : null}
          <label>Project<select value={currentProjectId} onChange={(event) => changeProject(event.target.value)}><option value="">Select a live project</option>{projects.map((project) => <option key={projectId(project)} value={projectId(project)}>{project.projectCode} · {project.projectName}</option>)}</select></label>
          <label>Workspace<select value={workspace} onChange={(event) => changeWorkspace(event.target.value)}><option value="canonical">Live Project</option><option value="review_plan">Review Plan</option></select></label>
          {workspace === 'review_plan' ? <label>Review plan<select value={selectedPlan} onChange={(event) => changePlan(event.target.value)}>{projectPlans.map((plan) => <option key={plan.planId} value={plan.planId}>{plan.planName || plan.name || `${title(plan.sourceKind)} · ${title(plan.status)}`}</option>)}</select></label> : null}
          {canUseAi ? <button type="button" className="forge-ai-button" onClick={() => setAiOpen((value) => !value)}>✦ AI plan & estimate</button> : null}
        </div>
      </header>

      <div className="forge-workspace-banner"><b>{workspace === 'canonical' ? 'Live Project' : 'Review Plan'}</b><span>{workspace === 'canonical' ? 'Changes update canonical project records.' : 'Changes stay in this proposal until an authorized PM adopts it.'}</span></div>
      {aiConnection ? (
        <div className={`forge-ai-connection ${aiConnection.connected ? 'is-connected' : 'is-attention'}`} role="status">
          <div>
            <span>MODULE 064 · CELAR AI</span>
            <strong>{aiConnection.connected ? 'Project Forge is connected' : 'Project Forge AI connection needs attention'}</strong>
            <small>
              {aiConnection.permissionAuthorized
                ? aiConnection.privateKnowledgeReady
                  ? 'Your Project Forge permission, governed route, private inference, and current document knowledge are ready.'
                  : 'Your Project Forge permission and governed route are connected; Module 064 reports private knowledge readiness items.'
                : 'The central Module 064 route is present. Your current role still needs Project Forge AI permission.'}
            </small>
          </div>
          <div className="forge-ai-connection__evidence">
            <span>{aiConnection.projectReadyDocumentCount ?? 0} project document(s)</span>
            <span>{aiConnection.projectActiveVersionCount ?? 0} project version(s)</span>
            <span>{aiConnection.projectActiveChunkCount ?? 0} project chunk(s)</span>
            <span>Project indexed {shortDate(aiConnection.projectLastIndexedAt)}</span>
          </div>
        </div>
      ) : null}
      {projectEvidenceMissing ? (
        <aside className="forge-ai-readiness-help" role="status">
          <div><strong>Project evidence is not ready yet</strong><span>Project Forge requires at least one citation-ready private document and will not create an uncited plan.</span></div>
          <ol><li>Upload an approved SOW, GSD, design, order, proposal, or supporting document to this project.</li><li>Mark it active and engineering-visible, and enable its AI context.</li><li>Wait for scanning, extraction, version approval, and indexing; then refresh this page.</li></ol>
        </aside>
      ) : null}
      {error ? <div className="forge-banner error" role="alert">{error}</div> : null}
      {notice ? <div className="forge-banner success" role="status">{notice}</div> : null}

      {aiOpen ? <section className="forge-ai-studio"><div><span>MODULE 064 · CELAR AI</span><h3>Document-grounded plan and estimate</h3><p>Uses only project evidence the effective user is authorized to access. Celar AI automatically fills each customer-facing task with detailed procedures, inputs, outputs, validation, measurable acceptance criteria, responsibilities, prerequisites, risks, open questions, roles, dependencies, durations, hours, priority, and citations. The result remains a review draft until a Project Manager explicitly adopts it.</p></div><label>Requested outcome<textarea rows="5" value={aiOutcome} onChange={(event) => setAiOutcome(event.target.value)} /></label><aside className="forge-ai-external" aria-label="Automatic AI fallback policy"><strong>Fallback is automatic and backend governed.</strong><span>Module 064 follows the stored priority among eligible targets. Private document evidence is never sent to a public fallback.</span></aside><div className="forge-ai-actions"><button type="button" title={projectEvidenceMissing ? 'Process and approve at least one document for this project first.' : undefined} disabled={!currentProjectId || busy === 'ai' || projectEvidenceMissing} onClick={generateAiDraft}>{busy === 'ai' ? 'Generating…' : projectEvidenceMissing ? 'Project evidence required' : 'Generate review draft'}</button>{workspace === 'review_plan' && (generatedDraft || currentPlan) ? <><label>Engineer reviewer<select value={reviewerId} onChange={(event) => setReviewerId(event.target.value)}><option value="">Select an eligible project Engineer</option>{engineers.map((engineer) => <option key={engineer.id} value={engineer.id}>{engineer.name}</option>)}</select></label><button type="button" disabled={!reviewerId || busy === 'reviewer'} onClick={assignReviewer}>Assign review</button>{canManage ? <button type="button" className="adopt" disabled={busy === 'adopt' || (currentPlan?.sourceKind === 'ai_generated' && normalize(currentPlan?.status) !== 'reviewed')} onClick={adoptPlan}>Adopt reviewed plan</button> : null}</> : null}</div>{generatedDraft ? <div className="forge-ai-evidence"><b>Confidence: {Math.round(Number(generatedDraft.confidence || 0) * 100)}%</b><span>{generatedDraft.confidenceExplanation || 'Human review is required.'}</span><span>{(generatedDraft.citations || []).length} authorized citation(s)</span><span>{(generatedDraft.warnings || []).length} warning(s)</span></div> : null}</section> : null}

      <nav className="forge-tabs" aria-label="Project Forge workbook tabs" role="tablist" aria-orientation="horizontal">
        {WORKBOOK_TABS.map((tab, index) => <button ref={(node) => { tabButtons.current[index] = node; }} id={`forge-tab-${tab.id}`} aria-controls={`forge-panel-${tab.id}`} tabIndex={activeTab === tab.id ? 0 : -1} type="button" role="tab" aria-selected={activeTab === tab.id} key={tab.id} className={activeTab === tab.id ? 'active' : ''} onKeyDown={(event) => handleTabKeyDown(event, index)} onClick={() => setActiveTab(tab.id)}><span>{String(index + 1).padStart(2, '0')}</span>{tab.label}</button>)}
      </nav>

      <main className="forge-content" id={`forge-panel-${activeTab}`} role="tabpanel" aria-labelledby={`forge-tab-${activeTab}`} tabIndex="0">
        <div className="forge-content-heading"><div><span>Workbook tab {WORKBOOK_TABS.findIndex((tab) => tab.id === activeTab) + 1} of {WORKBOOK_TABS.length}</span><h2>{WORKBOOK_TABS.find((tab) => tab.id === activeTab)?.label}</h2></div><div className="forge-content-actions">{canManage && workspace === 'canonical' && currentProjectId && NEW_TASK_TABS.has(activeTab) ? <button type="button" className="forge-create-button" onClick={openNewTask}>+ Create live task</button> : null}<button type="button" onClick={() => load({ pm: selectedPm, project: currentProjectId, workspaceValue: workspace, planIdValue: selectedPlan })} disabled={loading}>{loading ? 'Loading…' : 'Refresh live data'}</button></div></div>
        {loading && !data ? <div className="forge-loading">Loading live ProjectPulse records…</div> : renderTab()}
      </main>

      {currentActivity.length ? <footer className="forge-activity"><b>Recent Project Forge activity</b>{currentActivity.slice(0, 3).map((item) => <span key={item.activityId || item.activityEventId || item.id}>{title(item.eventCode || item.action)} · {item.summary || item.changeSummary || shortDate(item.occurredAt || item.createdAt)}</span>)}</footer> : null}

      {selectedTask ? <ProjectForgeTaskDialog task={selectedTask} tasks={projectTasks} engineers={engineers} dependencies={dependencies} canManage={canManage} canEditEstimate={canEditEstimate} canViewCosts={canViewCosts} busy={Boolean(busy)} onClose={() => setSelectedTask(null)} onSave={saveTask} onAssign={assignTask} onSaveEstimate={saveEstimate} onCompleteReview={completeReview} onAddDependency={addDependency} onDeleteDependency={deleteDependency} onArchive={archiveTask} /> : null}
    </div>
  );
}
