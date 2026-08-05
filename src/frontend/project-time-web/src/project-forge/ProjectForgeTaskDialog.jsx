import { useEffect, useMemo, useRef, useState } from 'react';
import {
  DECISION_QUADRANTS,
  KANBAN_COLUMNS,
  iso,
  normalize,
  projectedOccurrenceDates,
  shortDate,
  taskDecision,
  taskEnd,
  taskEstimate,
  taskId,
  taskKanban,
  taskName,
  taskProgress,
  taskSource,
  taskStart,
  taskStatus,
  title
} from './projectForgeModel.js';

const PRIORITIES = ['low', 'normal', 'high', 'critical'];
const STATUSES = ['not_started', 'in_progress', 'blocked', 'completed'];

function laneForStatus(status) {
  if (status === 'completed') return 'done';
  if (status === 'blocked') return 'blocked';
  if (status === 'in_progress') return 'in_progress';
  return 'backlog';
}

function statusForLane(lane) {
  if (lane === 'done') return 'completed';
  if (lane === 'blocked') return 'blocked';
  if (lane === 'in_progress' || lane === 'review') return 'in_progress';
  return 'not_started';
}

function recurrenceParts(value) {
  let rule = value;
  if (typeof value === 'string') {
    try { rule = JSON.parse(value); } catch { rule = {}; }
  }
  rule = rule && typeof rule === 'object' ? rule : {};
  return {
    recurrenceFrequency: normalize(rule.frequency || rule.unit) || 'weekly',
    recurrenceInterval: Math.max(1, Number(rule.interval || 1)),
    recurrenceEndDate: iso(rule.endDate || rule.until),
    recurrenceActive: rule.active !== false
  };
}

function recurrenceRuleForForm(form) {
  if (form.taskType !== 'recurring') return null;
  return {
    frequency: form.recurrenceFrequency,
    interval: Math.max(1, Number(form.recurrenceInterval || 1)),
    endDate: form.recurrenceEndDate || null,
    active: Boolean(form.recurrenceActive)
  };
}

function initialForm(task) {
  return {
    taskName: task?.isNew ? '' : taskName(task),
    description: task.taskDescription || task.description || '',
    taskType: normalize(task.taskType) === 'recurring' ? 'recurring' : 'variable',
    phase: task.phaseName || task.phase || '',
    priority: normalize(task.priorityCode || task.priority) || 'normal',
    durationWorkingDays: Number(task.durationWorkingDays || 1),
    parentTaskId: task.parentTaskId || '',
    estimatedHours: Number(taskEstimate(task)),
    hourlyRate: Number(task.hourlyRate || 0),
    materialUnits: Number(task.materialUnits || 0),
    materialUnitCost: Number(task.materialUnitCost || 0),
    fixedCost: Number(task.fixedCost || 0),
    travelCost: Number(task.travelCost || 0),
    equipmentCost: Number(task.equipmentCost || 0),
    miscCost: Number(task.miscCost ?? task.miscellaneousCost ?? 0),
    ...recurrenceParts(task.recurrenceRule),
    status: taskStatus(task),
    kanbanCategory: taskKanban(task),
    percentComplete: Number(taskProgress(task)),
    blockedReason: task.blockedReason || '',
    startDate: taskStart(task),
    dueDate: taskEnd(task),
    decisionAction: taskDecision(task),
    assigneeUserId: task.assigneeUserId || task.resourceUserId || task.reviewerUserId || '',
    assignedHours: Number(task.assignedHours ?? taskEstimate(task)),
    allocationPercent: Number(task.allocationPercent || 100),
    reviewNote: task.reviewNote || ''
  };
}

function can(task, capability, fallback) {
  return task?.[capability] === undefined ? Boolean(fallback) : Boolean(task[capability]);
}

export default function ProjectForgeTaskDialog({
  task,
  tasks,
  engineers,
  canManage,
  canEditEstimate,
  canViewCosts,
  dependencies = [],
  busy,
  onClose,
  onSave,
  onAssign,
  onSaveEstimate,
  onCompleteReview,
  onAddDependency,
  onDeleteDependency,
  onArchive
}) {
  const [form, setForm] = useState(() => initialForm(task));
  const [validation, setValidation] = useState('');
  const [predecessorTaskId, setPredecessorTaskId] = useState('');
  const [dependencyType, setDependencyType] = useState('FS');
  const [lagWorkingDays, setLagWorkingDays] = useState(0);
  const titleRef = useRef(null);
  const dialogRef = useRef(null);
  const returnFocusRef = useRef(null);
  const busyRef = useRef(busy);
  const closeRef = useRef(onClose);

  useEffect(() => { busyRef.current = busy; }, [busy]);
  useEffect(() => { closeRef.current = onClose; }, [onClose]);

  useEffect(() => {
    setForm(initialForm(task));
    setValidation('');
    setPredecessorTaskId('');
  }, [task]);

  useEffect(() => {
    returnFocusRef.current = document.activeElement;
    titleRef.current?.focus();
    const handleKey = (event) => {
      if (event.key === 'Escape' && !busyRef.current) closeRef.current();
      if (event.key !== 'Tab') return;
      const focusable = [...(dialogRef.current?.querySelectorAll('button:not(:disabled), input:not(:disabled), select:not(:disabled), textarea:not(:disabled), [tabindex]:not([tabindex="-1"])') || [])];
      if (!focusable.length) return;
      const first = focusable[0];
      const last = focusable[focusable.length - 1];
      if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus(); }
      else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); }
    };
    document.addEventListener('keydown', handleKey);
    return () => {
      document.removeEventListener('keydown', handleKey);
      returnFocusRef.current?.focus?.();
    };
  }, []);

  const permissions = useMemo(() => ({
    details: can(task, 'canEditDetails', canManage),
    workflow: can(task, 'canEditWorkflow', canManage),
    schedule: can(task, 'canEditSchedule', canManage),
    decision: can(task, 'canEditDecision', canManage),
    assign: can(task, 'canAssign', canManage),
    estimate: Boolean(canEditEstimate && (task.canEditEstimate ?? true)),
    dependencies: can(task, 'canEditDependencies', canManage),
    archive: can(task, 'canArchive', canManage)
  }), [task, canManage, canEditEstimate]);
  const reviewPlan = taskSource(task) === 'review_plan';
  const isNew = Boolean(task.isNew);
  const canCompleteReview = Boolean(task.canCompleteReview || task.isAssignedReviewer);
  const canEditReviewContent = Boolean(reviewPlan && canCompleteReview);
  const canEditManagerDetails = Boolean(canManage && permissions.details);
  const canEditNarrative = Boolean(canEditManagerDetails || canEditReviewContent);
  const canEditDates = Boolean(permissions.schedule && (canManage || canEditReviewContent));
  const recurrencePreview = projectedOccurrenceDates({
    taskType: form.taskType,
    startDate: form.startDate,
    recurrenceRule: recurrenceRuleForForm(form)
  });
  const taskDependencies = dependencies.filter((edge) => String(edge.successorTaskId) === String(taskId(task)) || String(edge.taskId) === String(taskId(task)));
  const update = (name, value) => setForm((current) => ({ ...current, [name]: value }));
  const before = initialForm(task);
  const changed = (keys) => keys.some((key) => String(before[key] ?? '') !== String(form[key] ?? ''));
  const dirty = {
    details: isNew || changed(['taskName', 'description', 'taskType', 'phase', 'priority', 'durationWorkingDays', 'parentTaskId', 'estimatedHours', 'hourlyRate', 'materialUnits', 'materialUnitCost', 'fixedCost', 'travelCost', 'equipmentCost', 'miscCost', 'recurrenceFrequency', 'recurrenceInterval', 'recurrenceEndDate', 'recurrenceActive']),
    workflow: changed(['status', 'kanbanCategory', 'percentComplete', 'blockedReason']),
    schedule: changed(['startDate', 'dueDate']),
    scheduleStart: changed(['startDate']),
    scheduleDue: changed(['dueDate']),
    decision: changed(['decisionAction'])
  };
  const reviewEstimateDirty = changed(canViewCosts
    ? ['estimatedHours', 'hourlyRate', 'materialUnits', 'materialUnitCost', 'fixedCost', 'travelCost', 'equipmentCost', 'miscCost']
    : ['estimatedHours']);
  const reviewTaskDirty = changed(['description', 'durationWorkingDays', 'startDate', 'dueDate']);
  const reviewEditsDirty = reviewEstimateDirty || reviewTaskDirty;

  async function submit(event) {
    event.preventDefault();
    setValidation('');
    if (!form.taskName.trim()) { setValidation('Task name is required.'); return; }
    if (Boolean(form.startDate) !== Boolean(form.dueDate)) { setValidation('Start date and due date are both required when scheduling a task.'); return; }
    if (!form.startDate && !form.dueDate && (before.startDate || before.dueDate)) { setValidation('Removing an existing schedule is not supported. Choose valid replacement dates.'); return; }
    if (form.startDate && form.dueDate && form.dueDate < form.startDate) { setValidation('Due date cannot be earlier than the start date.'); return; }
    await onSave(task, form, {
      ...permissions,
      managerDetails: canEditManagerDetails,
      reviewContent: canEditReviewContent,
      financial: Boolean(canViewCosts),
      dirty
    });
  }

  return (
    <div className="forge-dialog-backdrop" role="presentation" onMouseDown={(event) => { if (event.target === event.currentTarget && !busy) onClose(); }}>
      <section ref={dialogRef} className="forge-task-dialog" role="dialog" aria-modal="true" aria-labelledby="forge-task-dialog-title" aria-describedby="forge-task-dialog-context">
        <header>
          <div>
            <span className={`forge-source-badge ${taskSource(task)}`}>{reviewPlan ? 'Review plan' : 'Live project'}</span>
            <h2 id="forge-task-dialog-title" ref={titleRef} tabIndex="-1">{isNew ? 'Create live project task' : taskName(task)}</h2>
          </div>
          <button type="button" className="forge-close-button" onClick={onClose} disabled={busy} aria-label="Close task editor">Close</button>
        </header>
        <p id="forge-task-dialog-context" className="forge-dialog-context">Edit the authoritative {reviewPlan ? 'review-plan' : 'live-project'} task. Saving a review estimate does not complete its review.</p>

        <form onSubmit={submit}>
          <fieldset disabled={busy}>
            <legend>Task details</legend>
            <div className="forge-dialog-grid">
              <label className="wide">Task name<input value={form.taskName} onChange={(event) => update('taskName', event.target.value)} required disabled={!canEditManagerDetails && !isNew} /></label>
              <label>Type<select value={form.taskType} onChange={(event) => update('taskType', event.target.value)} disabled={!canEditManagerDetails && !isNew}><option value="variable">Variable</option><option value="recurring">Recurring</option></select></label>
              <label>Phase<input value={form.phase} onChange={(event) => update('phase', event.target.value)} disabled={!canEditManagerDetails && !isNew} /></label>
              <label>Priority<select value={form.priority} onChange={(event) => update('priority', event.target.value)} disabled={!canEditManagerDetails && !isNew}>{PRIORITIES.map((value) => <option key={value} value={value}>{title(value)}</option>)}</select></label>
              <label>Working days<input type="number" min="0" max="730" step="1" value={form.durationWorkingDays} onChange={(event) => update('durationWorkingDays', event.target.value)} disabled={!canEditNarrative && !isNew} /></label>
              <label>Parent task<select value={form.parentTaskId} onChange={(event) => update('parentTaskId', event.target.value)} disabled={!canEditManagerDetails && !isNew}><option value="">No parent</option>{tasks.filter((candidate) => candidate !== task).map((candidate) => <option key={`${taskSource(candidate)}:${candidate.taskId || candidate.planTaskId}`} value={candidate.taskId || candidate.planTaskId}>{taskName(candidate)}</option>)}</select></label>
              <label className="wide">Description<textarea rows="4" value={form.description} onChange={(event) => update('description', event.target.value)} disabled={!canEditNarrative && !isNew} /></label>
              {form.taskType === 'recurring' ? <>
                <label>Repeat<select value={form.recurrenceFrequency} onChange={(event) => update('recurrenceFrequency', event.target.value)} disabled={!canEditManagerDetails && !isNew}><option value="daily">Daily</option><option value="weekly">Weekly</option><option value="monthly">Monthly</option><option value="yearly">Yearly</option></select></label>
                <label>Every<input type="number" min="1" max="365" step="1" value={form.recurrenceInterval} onChange={(event) => update('recurrenceInterval', event.target.value)} disabled={!canEditManagerDetails && !isNew} /></label>
                <label>Repeat until<input type="date" value={form.recurrenceEndDate} onChange={(event) => update('recurrenceEndDate', event.target.value)} disabled={!canEditManagerDetails && !isNew} /></label>
                <label className="forge-checkbox-label"><input type="checkbox" checked={form.recurrenceActive} onChange={(event) => update('recurrenceActive', event.target.checked)} disabled={!canEditManagerDetails && !isNew} />Active recurring series</label>
                <p className="forge-recurrence-preview wide"><b>Next occurrences (projection)</b><span>{recurrencePreview.map(shortDate).join(' · ') || 'Add a start date to preview future occurrences.'}</span><small>This preview does not create duplicate task rows.</small></p>
              </> : null}
            </div>
          </fieldset>

          {canManage && permissions.workflow ? <fieldset disabled={busy}>
            <legend>Workflow</legend>
            <div className="forge-dialog-grid">
              <label>Status<select value={form.status} onChange={(event) => setForm((current) => ({ ...current, status: event.target.value, kanbanCategory: laneForStatus(event.target.value) }))}>{STATUSES.map((value) => <option key={value} value={value}>{title(value)}</option>)}</select></label>
              <label>Kanban column<select value={form.kanbanCategory} onChange={(event) => setForm((current) => ({ ...current, kanbanCategory: event.target.value, status: statusForLane(event.target.value) }))}>{KANBAN_COLUMNS.map((value) => <option key={value.id} value={value.id}>{value.label}</option>)}</select></label>
              <label>Progress<input type="number" min="0" max="100" step="1" value={form.percentComplete} onChange={(event) => update('percentComplete', event.target.value)} /></label>
              <label className="wide">Blocked reason<textarea rows="2" value={form.blockedReason} onChange={(event) => update('blockedReason', event.target.value)} /></label>
            </div>
          </fieldset> : null}

          <fieldset disabled={!canEditDates || busy}>
            <legend>Schedule</legend>
            <div className="forge-dialog-grid">
              <label>Start date<input type="date" value={iso(form.startDate)} onChange={(event) => update('startDate', event.target.value)} /></label>
              <label>Due date<input type="date" value={iso(form.dueDate)} onChange={(event) => update('dueDate', event.target.value)} /></label>
            </div>
          </fieldset>

          {canManage && permissions.decision ? <fieldset disabled={busy}>
            <legend>Decision matrix</legend>
            <label>Quadrant<select value={form.decisionAction} onChange={(event) => update('decisionAction', event.target.value)}>{DECISION_QUADRANTS.map((value) => <option key={value.id} value={value.id}>{value.label} — {value.help}</option>)}</select></label>
          </fieldset> : null}

          <fieldset disabled={(!permissions.details && !permissions.estimate) || busy}>
            <legend>Estimate and cost</legend>
            <div className="forge-dialog-grid forge-cost-grid">
              <label>Estimated hours<input type="number" min="0" max="100000" step="0.25" value={form.estimatedHours} onChange={(event) => update('estimatedHours', event.target.value)} /></label>
              {canViewCosts ? <><label>Hourly rate<input type="number" min="0" step="0.01" value={form.hourlyRate} onChange={(event) => update('hourlyRate', event.target.value)} /></label><label>Material units<input type="number" min="0" step="0.01" value={form.materialUnits} onChange={(event) => update('materialUnits', event.target.value)} /></label><label>Material unit cost<input type="number" min="0" step="0.01" value={form.materialUnitCost} onChange={(event) => update('materialUnitCost', event.target.value)} /></label><label>Fixed cost<input type="number" min="0" step="0.01" value={form.fixedCost} onChange={(event) => update('fixedCost', event.target.value)} /></label><label>Travel cost<input type="number" min="0" step="0.01" value={form.travelCost} onChange={(event) => update('travelCost', event.target.value)} /></label><label>Equipment cost<input type="number" min="0" step="0.01" value={form.equipmentCost} onChange={(event) => update('equipmentCost', event.target.value)} /></label><label>Miscellaneous cost<input type="number" min="0" step="0.01" value={form.miscCost} onChange={(event) => update('miscCost', event.target.value)} /></label></> : <p className="forge-field-help wide">Financial rates and project costs are restricted to authorized project-management roles.</p>}
            </div>
          </fieldset>

          {canManage && permissions.assign && engineers.length ? (
            <fieldset disabled={busy}>
              <legend>Assignment</legend>
              <div className="forge-dialog-grid">
                <label>Engineer<select value={form.assigneeUserId} onChange={(event) => update('assigneeUserId', event.target.value)}><option value="" disabled>Select an Engineer</option>{engineers.map((engineer) => <option key={engineer.id} value={engineer.id}>{engineer.name}</option>)}</select></label>
                {isNew ? <p className="forge-field-help">The new task will use its estimated hours for the initial assignment. Allocation can be adjusted after creation.</p> : <>
                  <label>Assigned hours<input type="number" min="0" step="0.25" value={form.assignedHours} onChange={(event) => update('assignedHours', event.target.value)} /></label>
                  <label>Allocation percent<input type="number" min="0.01" max="100" step="0.01" value={form.allocationPercent} onChange={(event) => update('allocationPercent', event.target.value)} /></label>
                  <button type="button" className="forge-secondary-button" onClick={() => onAssign(task, form)} disabled={!form.assigneeUserId || busy}>Save assignment</button>
                </>}
              </div>
            </fieldset>
          ) : null}

          {!isNew && canManage && permissions.dependencies ? (
            <fieldset disabled={busy}>
              <legend>Dependencies</legend>
              {taskDependencies.length ? <ul className="forge-dependency-list">{taskDependencies.map((edge) => {
                const predecessor = tasks.find((candidate) => String(taskId(candidate)) === String(edge.predecessorTaskId));
                return <li key={edge.taskDependencyId || edge.dependencyId}><span><b>{predecessor ? taskName(predecessor) : edge.predecessorTaskId}</b> · {edge.dependencyType || 'FS'} · lag {Number(edge.lagWorkingDays || 0)} working day(s)</span><button type="button" className="forge-danger-button" onClick={() => onDeleteDependency(task, edge)}>Remove</button></li>;
              })}</ul> : <p className="forge-field-help">No predecessor dependency is configured.</p>}
              <div className="forge-dialog-grid">
                <label>Predecessor<select value={predecessorTaskId} onChange={(event) => setPredecessorTaskId(event.target.value)}><option value="">Select a predecessor</option>{tasks.filter((candidate) => String(taskId(candidate)) !== String(taskId(task))).map((candidate) => <option key={`${taskSource(candidate)}:${taskId(candidate)}`} value={taskId(candidate)}>{taskName(candidate)}</option>)}</select></label>
                <label>Relationship<select value={dependencyType} onChange={(event) => setDependencyType(event.target.value)}>{['FS', 'SS', 'FF', 'SF'].map((value) => <option key={value} value={value}>{value}</option>)}</select></label>
                <label>Lag (working days)<input type="number" min="-365" max="365" step="1" value={lagWorkingDays} onChange={(event) => setLagWorkingDays(event.target.value)} /></label>
                <button type="button" className="forge-secondary-button" disabled={!predecessorTaskId || busy} onClick={() => onAddDependency(task, { predecessorTaskId, successorTaskId: taskId(task), dependencyType, lagWorkingDays: Number(lagWorkingDays || 0) })}>Add dependency</button>
              </div>
            </fieldset>
          ) : null}

          {reviewPlan && canCompleteReview ? (
            <fieldset disabled={busy} aria-describedby={reviewEditsDirty ? 'forge-review-save-warning' : undefined}>
              <legend>Engineer review</legend>
              <label>Review note<textarea rows="3" value={form.reviewNote} onChange={(event) => update('reviewNote', event.target.value)} /></label>
              {reviewEditsDirty ? <p id="forge-review-save-warning" className="forge-field-help forge-review-save-warning" role="status" aria-live="polite">Save task changes before completing the review or requesting changes. Use <b>Save task</b>; estimate-only saving cannot include your description, duration, or schedule edits.</p> : null}
              <div className="forge-review-actions">
                {permissions.estimate ? <button type="button" className="forge-secondary-button" onClick={() => onSaveEstimate(task, form)} disabled={!reviewEstimateDirty || reviewTaskDirty || busy} title={reviewTaskDirty ? 'Save task first so description, duration, and schedule edits are retained.' : undefined}>Save estimate only</button> : null}
                <button type="button" className="forge-secondary-button" disabled={reviewEditsDirty || busy} aria-describedby={reviewEditsDirty ? 'forge-review-save-warning' : undefined} onClick={() => onCompleteReview(task, form.reviewNote, 'changes_requested')}>Request changes</button>
                <button type="button" className="forge-primary-button" disabled={reviewEditsDirty || busy} aria-describedby={reviewEditsDirty ? 'forge-review-save-warning' : undefined} onClick={() => onCompleteReview(task, form.reviewNote, 'completed')}>Complete review</button>
              </div>
            </fieldset>
          ) : null}

          <p className="forge-dialog-validation" role="alert" aria-live="assertive">{validation}</p>
          <footer>
            {!isNew && permissions.archive ? <button type="button" className="forge-danger-button" onClick={() => onArchive(task)} disabled={busy}>Archive task</button> : null}
            <button type="button" className="forge-secondary-button" onClick={onClose} disabled={busy}>Cancel</button>
            <button type="submit" className="forge-primary-button" disabled={busy || !Object.values(permissions).some(Boolean)}>{busy ? 'Saving…' : isNew ? 'Create task' : 'Save task'}</button>
          </footer>
        </form>
      </section>
    </div>
  );
}
