import { useEffect, useMemo, useState } from 'react';
import usSignalLogoUrl from '../brand/ussignal.png';
import './project-forge-center.css';
import './projectpulse-module-standard.css';

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

const STATUS_COLUMNS = ['not_started', 'in_progress', 'blocked', 'completed'];
const DECISION_QUADRANTS = [
  { id: 'do', label: 'Do', help: 'Important and urgent' },
  { id: 'delegate', label: 'Delegate', help: 'Important, not urgent' },
  { id: 'decide', label: 'Decide', help: 'Urgent, not important' },
  { id: 'delete', label: 'Delete', help: 'Not important or urgent' }
];

function storedSession() {
  try {
    const session = JSON.parse(window.localStorage.getItem('projectPulseAuthSession') || 'null');
    if (!session?.sessionToken) return null;
    if (session.expiresAt && Date.now() >= Date.parse(session.expiresAt)) return null;
    return session;
  } catch {
    return null;
  }
}

function headers(extra = {}) {
  const session = storedSession();
  return {
    ...(session?.sessionToken ? {
      Authorization: `Bearer ${session.sessionToken}`,
      'X-ProjectPulse-Session': session.sessionToken
    } : {}),
    'X-ProjectPulse-Module-Number': '033',
    ...extra
  };
}

async function request(path, options = {}) {
  const response = await fetch(path, { ...options, headers: headers(options.headers) });
  const contentType = response.headers.get('content-type') || '';
  const body = contentType.includes('application/json') ? await response.json() : null;
  if (!response.ok) {
    throw new Error(body?.message || body?.detail || `${path} returned HTTP ${response.status}`);
  }
  return body;
}

function send(path, method, body) {
  return request(path, {
    method,
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body)
  });
}

function money(value, currency = 'USD') {
  return new Intl.NumberFormat(undefined, {
    style: 'currency',
    currency: currency || 'USD',
    maximumFractionDigits: 0
  }).format(Number(value || 0));
}

function hours(value) {
  return `${Number(value || 0).toLocaleString(undefined, { maximumFractionDigits: 1 })}h`;
}

function shortDate(value) {
  if (!value) return 'Not scheduled';
  const date = new Date(`${String(value).slice(0, 10)}T12:00:00`);
  return Number.isNaN(date.getTime())
    ? String(value)
    : date.toLocaleDateString(undefined, { month: 'short', day: 'numeric', year: 'numeric' });
}

function iso(value) {
  return value ? String(value).slice(0, 10) : '';
}

function normalize(value) {
  return String(value || '').trim().toLowerCase().replaceAll(' ', '_').replaceAll('-', '_');
}

function title(value) {
  return String(value || 'Not set')
    .replaceAll('_', ' ')
    .replace(/\b\w/g, (letter) => letter.toUpperCase());
}

function taskId(task) {
  return task.planTaskId || task.projectForgePlanTaskId || task.taskId || task.canonicalTaskId;
}

function projectId(project) {
  return project.projectId || project.id;
}

function taskStatus(task) {
  const status = normalize(task.taskStatus || task.status);
  if (['complete', 'completed', 'done'].includes(status)) return 'completed';
  if (['in_progress', 'active', 'started'].includes(status)) return 'in_progress';
  if (['blocked', 'delayed', 'on_hold'].includes(status)) return 'blocked';
  return 'not_started';
}

function taskProgress(task) {
  return Math.max(0, Math.min(100, Number(task.percentComplete ?? task.progressPercent ?? 0)));
}

function taskStart(task) {
  return iso(task.plannedStartDate || task.startDate || task.scheduledStartDate);
}

function taskEnd(task) {
  return iso(task.plannedEndDate || task.dueDate || task.endDate || task.scheduledEndDate);
}

function taskEstimate(task) {
  return Number(task.estimatedHours ?? task.remainingEffortHours ?? task.assignedHours ?? 0);
}

function hasRecurrence(task) {
  if (normalize(task.taskType) === 'recurring') return true;
  const rule = task.recurrenceRule;
  if (!rule) return false;
  if (typeof rule === 'string') {
    try { return Object.keys(JSON.parse(rule)).length > 0; } catch { return rule.trim().length > 2; }
  }
  return typeof rule === 'object' && Object.keys(rule).length > 0;
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

function Progress({ value }) {
  const progress = Math.max(0, Math.min(100, Number(value || 0)));
  return (
    <div className="forge-progress" aria-label={`${progress}% complete`}>
      <span style={{ width: `${progress}%` }} />
      <b>{Math.round(progress)}%</b>
    </div>
  );
}

function Empty({ children = 'No live records match this view.' }) {
  return <div className="forge-empty">{children}</div>;
}

function Metric({ label, value, hint }) {
  return (
    <article className="forge-metric">
      <span>{label}</span>
      <strong>{value}</strong>
      {hint ? <small>{hint}</small> : null}
    </article>
  );
}

function TaskTable({ tasks, canEditEstimate, onEstimate, showDecision = false }) {
  const [editing, setEditing] = useState({});
  if (!tasks.length) return <Empty />;
  return (
    <div className="forge-table-wrap">
      <table className="forge-table">
        <thead>
          <tr>
            <th>Task</th><th>Phase</th><th>Status</th><th>Owner / reviewer</th>
            <th>Start</th><th>Due</th><th>Estimate</th><th>Progress</th>
            {showDecision ? <th>Decision</th> : null}
          </tr>
        </thead>
        <tbody>
          {tasks.map((task) => {
            const id = taskId(task);
            const editable = Boolean(canEditEstimate && (task.canEditEstimate ?? true) && id);
            return (
              <tr key={id || `${task.taskCode}-${task.taskName}`}>
                <td><b>{task.taskCode || task.wbsNumber || '—'}</b><span>{task.taskName || task.name}</span></td>
                <td>{task.phaseName || task.phase || 'Unphased'}</td>
                <td><span className={`forge-pill ${taskStatus(task)}`}>{title(taskStatus(task))}</span></td>
                <td>{task.assigneeName || task.reviewerName || 'Unassigned'}</td>
                <td>{shortDate(taskStart(task))}</td>
                <td>{shortDate(taskEnd(task))}</td>
                <td>
                  {editable ? (
                    <form className="forge-estimate" onSubmit={(event) => {
                      event.preventDefault();
                      onEstimate(task, Number(editing[id] ?? taskEstimate(task)));
                    }}>
                      <input
                        aria-label={`Estimated hours for ${task.taskName || task.name}`}
                        type="number" min="0" max="100000" step="0.25"
                        value={editing[id] ?? taskEstimate(task)}
                        onChange={(event) => setEditing((current) => ({ ...current, [id]: event.target.value }))}
                      />
                      <button type="submit">Save</button>
                    </form>
                  ) : hours(taskEstimate(task))}
                </td>
                <td><Progress value={taskProgress(task)} /></td>
                {showDecision ? <td>{title(task.decisionAction || 'decide')}</td> : null}
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}

function CalendarMonth({ tasks, holidays }) {
  const [cursor, setCursor] = useState(() => new Date());
  const year = cursor.getFullYear();
  const month = cursor.getMonth();
  const first = new Date(year, month, 1);
  const days = new Date(year, month + 1, 0).getDate();
  const cells = Array.from({ length: first.getDay() + days }, (_, index) => index < first.getDay() ? null : index - first.getDay() + 1);
  const inMonth = (value, day) => {
    if (!value) return false;
    const date = new Date(`${value}T12:00:00`);
    return date.getFullYear() === year && date.getMonth() === month && date.getDate() === day;
  };
  return (
    <>
      <div className="forge-calendar-controls">
        <button type="button" onClick={() => setCursor(new Date(year, month - 1, 1))}>Previous</button>
        <h3>{cursor.toLocaleDateString(undefined, { month: 'long', year: 'numeric' })}</h3>
        <button type="button" onClick={() => setCursor(new Date(year, month + 1, 1))}>Next</button>
      </div>
      <div className="forge-month-grid">
        {['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'].map((day) => <b className="forge-weekday" key={day}>{day}</b>)}
        {cells.map((day, index) => day ? (
          <article key={day}>
            <strong>{day}</strong>
            {holidays.filter((holiday) => inMonth(iso(holiday.holidayDate || holiday.date), day)).map((holiday) => (
              <span className="holiday" key={holiday.companyHolidayId || holiday.holidayId || holiday.holidayDate || holiday.date}>{holiday.holidayName || holiday.name}</span>
            ))}
            {tasks.filter((task) => inMonth(taskEnd(task), day)).slice(0, 4).map((task) => (
              <span className={taskStatus(task)} key={taskId(task)} title={task.taskName || task.name}>{task.taskCode || task.wbsNumber} · {task.taskName || task.name}</span>
            ))}
          </article>
        ) : <div key={`blank-${index}`} className="blank" />)}
      </div>
    </>
  );
}

function CalendarWeek({ tasks }) {
  const [offset, setOffset] = useState(0);
  const start = useMemo(() => {
    const value = new Date();
    value.setHours(12, 0, 0, 0);
    value.setDate(value.getDate() - value.getDay() + (offset * 7));
    return value;
  }, [offset]);
  const days = Array.from({ length: 7 }, (_, index) => {
    const value = new Date(start);
    value.setDate(start.getDate() + index);
    return value;
  });
  return (
    <>
      <div className="forge-calendar-controls">
        <button type="button" onClick={() => setOffset((value) => value - 1)}>Previous week</button>
        <h3>{shortDate(days[0].toISOString())} – {shortDate(days[6].toISOString())}</h3>
        <button type="button" onClick={() => setOffset((value) => value + 1)}>Next week</button>
      </div>
      <div className="forge-week-grid">
        {days.map((day) => {
          const date = day.toISOString().slice(0, 10);
          const due = tasks.filter((task) => taskStart(task) === date || taskEnd(task) === date);
          return (
            <article key={date}>
              <h4>{day.toLocaleDateString(undefined, { weekday: 'short', month: 'short', day: 'numeric' })}</h4>
              {due.length ? due.map((task) => <span key={taskId(task)}>{task.taskCode || task.wbsNumber} · {task.taskName || task.name}</span>) : <small>No scheduled work</small>}
            </article>
          );
        })}
      </div>
    </>
  );
}

function Gantt({ tasks }) {
  const dated = tasks.filter((task) => taskStart(task) && taskEnd(task));
  if (!dated.length) return <Empty>No scheduled task dates are available for this project.</Empty>;
  const starts = dated.map((task) => Date.parse(`${taskStart(task)}T12:00:00`));
  const ends = dated.map((task) => Date.parse(`${taskEnd(task)}T12:00:00`));
  const min = Math.min(...starts);
  const max = Math.max(...ends, min + 86400000);
  const span = Math.max(86400000, max - min);
  return (
    <div className="forge-gantt">
      <div className="forge-gantt-scale"><span>{shortDate(new Date(min).toISOString())}</span><span>{shortDate(new Date(max).toISOString())}</span></div>
      {dated.map((task) => {
        const left = ((Date.parse(`${taskStart(task)}T12:00:00`) - min) / span) * 100;
        const width = Math.max(2, ((Date.parse(`${taskEnd(task)}T12:00:00`) - Date.parse(`${taskStart(task)}T12:00:00`) + 86400000) / span) * 100);
        return (
          <div className="forge-gantt-row" key={taskId(task)}>
            <b>{task.taskCode || task.wbsNumber}</b>
            <div><span className={taskStatus(task)} style={{ left: `${left}%`, width: `${Math.min(width, 100 - left)}%` }}>{taskProgress(task)}%</span></div>
            <small>{task.taskName || task.name}</small>
          </div>
        );
      })}
    </div>
  );
}

export default function ProjectForgeCenter() {
  const [activeTab, setActiveTab] = useState('instructions');
  const [data, setData] = useState(null);
  const [selectedPm, setSelectedPm] = useState('');
  const [selectedProject, setSelectedProject] = useState('');
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState('');
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');
  const [search, setSearch] = useState('');
  const [statusFilter, setStatusFilter] = useState('all');
  const [priorityFilter, setPriorityFilter] = useState('all');
  const [aiOpen, setAiOpen] = useState(false);
  const [allowExternalAi, setAllowExternalAi] = useState(false);
  const [aiOutcome, setAiOutcome] = useState('Create a detailed, reviewable project plan with tasks, dependencies, realistic engineering estimates, acceptance criteria, risks, handoff, and closeout based on the authorized project documents.');
  const [generatedDraft, setGeneratedDraft] = useState(null);
  const [reviewerId, setReviewerId] = useState('');

  async function load({ pm = selectedPm, project = selectedProject } = {}) {
    setLoading(true);
    setError('');
    const query = new URLSearchParams();
    if (pm) query.set('projectManagerUserId', pm);
    try {
      const result = await request(`/api/project-forge/bootstrap${query.size ? `?${query}` : ''}`);
      setData(result);
      const availableProjects = result.projects || [];
      const nextProject = availableProjects.some((item) => String(projectId(item)) === String(project))
        ? project
        : projectId(availableProjects[0]) || '';
      setSelectedProject(String(nextProject || ''));
      if (result.access?.selectedProjectManagerUserId) setSelectedPm(String(result.access.selectedProjectManagerUserId));
    } catch (loadError) {
      setError(loadError.message || 'Project Forge could not be loaded.');
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => { load({ pm: '', project: '' }); }, []);

  const projects = data?.projects || [];
  const projectManagers = data?.projectManagers || data?.selectableProjectManagers || [];
  const allTasks = data?.tasks || data?.planTasks || [];
  const assignments = data?.assignments || [];
  const holidays = data?.holidays || [];
  const expenses = data?.expenses || [];
  const activity = data?.activity || data?.activityEvents || [];
  const plans = data?.plans || [];
  const currentProject = projects.find((item) => String(projectId(item)) === String(selectedProject)) || projects[0] || null;
  const currentProjectId = currentProject ? projectId(currentProject) : '';
  const projectTasks = allTasks.filter((task) => belongsToProject(task, currentProjectId));
  const filteredTasks = projectTasks.filter((task) => {
    const haystack = `${task.taskCode || ''} ${task.wbsNumber || ''} ${task.taskName || task.name || ''} ${task.taskDescription || task.description || ''} ${task.assigneeName || ''}`.toLowerCase();
    return (!search || haystack.includes(search.toLowerCase()))
      && (statusFilter === 'all' || taskStatus(task) === statusFilter)
      && (priorityFilter === 'all' || normalize(task.priorityCode || task.priority) === priorityFilter);
  });
  const currentAssignments = assignments.filter((item) => belongsToProject(item, currentProjectId));
  const currentExpenses = expenses.filter((item) => belongsToProject(item, currentProjectId));
  const currentPlan = plans.find((plan) => String(plan.projectId) === String(currentProjectId)) || null;
  const currency = currentProject?.currency || data?.setup?.currency || 'USD';
  const plannedCost = Number(currentProject?.plannedTotalProjectCost || currentProject?.plannedCost || 0)
    || projectTasks.reduce((sum, task) => sum
      + ['labor', 'materials', 'fixed', 'travel', 'equipment', 'miscellaneous']
        .reduce((taskTotal, bucket) => taskTotal + estimatedCost(task, bucket), 0), 0);
  const actualExpense = currentExpenses.reduce((sum, item) => sum + Number(item.totalAmount || item.amount || 0), 0);
  const actualHours = projectTasks.reduce((sum, task) => sum + Number(task.actualHours || task.usedHours || 0), 0);
  const estimatedHours = projectTasks.reduce((sum, task) => sum + taskEstimate(task), 0);
  const progress = projectTasks.length ? projectTasks.reduce((sum, task) => sum + taskProgress(task), 0) / projectTasks.length : 0;
  const canManage = Boolean(data?.access?.canManage && !data?.access?.isViewAs);
  const canUseAi = Boolean(data?.access?.canUseAi && !data?.access?.isViewAs);
  const canEditEstimate = Boolean(data?.access?.canEditAssignedEstimate && !data?.access?.isViewAs) || canManage;
  const canSelectPm = Boolean(data?.access?.canSelectProjectManager);
  const engineers = currentAssignments
    .filter((item) => item.isReviewerEligible && (item.resourceUserId || item.userId))
    .reduce((values, item) => {
      const id = item.resourceUserId || item.userId;
      if (!values.some((entry) => entry.id === id)) values.push({ id, name: item.resourceName || item.displayName || item.userName || item.email });
      return values;
    }, []);

  async function saveEstimate(task, estimatedHoursValue) {
    const id = taskId(task);
    if (!id || !Number.isFinite(estimatedHoursValue) || estimatedHoursValue < 0) return;
    setBusy(`estimate-${id}`); setError(''); setNotice('');
    try {
      await send(`/api/project-forge/plan-tasks/${id}/estimate`, 'PATCH', {
        estimatedHours: estimatedHoursValue,
        hourlyRate: Number(task.hourlyRate || 0),
        materialUnits: Number(task.materialUnits || 0),
        materialUnitCost: Number(task.materialUnitCost || 0),
        fixedCost: Number(task.fixedCost || 0),
        travelCost: Number(task.travelCost || 0),
        equipmentCost: Number(task.equipmentCost || 0),
        miscCost: Number(task.miscCost || task.miscellaneousCost || 0),
        startDate: taskStart(task) || null,
        dueDate: taskEnd(task) || null,
        reviewNote: 'Estimate reviewed in Project Forge.',
        expectedVersion: task.revisionNumber ?? task.revision ?? null
      });
      setNotice('The estimate was saved. Associated project participants were queued for notification through Module 065.');
      await load({ pm: selectedPm, project: currentProjectId });
    } catch (saveError) { setError(saveError.message); } finally { setBusy(''); }
  }

  async function generateAiDraft() {
    if (!currentProjectId) return;
    setBusy('ai'); setError(''); setNotice('');
    try {
      const result = await send(`/api/project-forge/projects/${currentProjectId}/ai-drafts`, 'POST', {
        requestedOutcome: aiOutcome,
        detailLevel: 'comprehensive',
        allowSanitizedExternalFallback: allowExternalAi
      });
      setGeneratedDraft(result.draft || result);
      setNotice('Celar AI created a private, document-grounded review draft. No canonical task or assignment has been changed.');
      await load({ pm: selectedPm, project: currentProjectId });
    } catch (generationError) { setError(generationError.message); } finally { setBusy(''); }
  }

  async function assignReviewer() {
    const draftId = generatedDraft?.planId || generatedDraft?.aiDraftId || generatedDraft?.draftId || currentPlan?.planId;
    if (!draftId || !reviewerId) return;
    setBusy('reviewer'); setError('');
    try {
      await send(`/api/project-forge/ai-drafts/${draftId}/assign-reviewer`, 'POST', {
        reviewerUserId: reviewerId,
        planTaskIds: null,
        reviewNote: 'Review and modify the proposed Project Forge estimate.'
      });
      setNotice('The engineer review was assigned. Module 065 will notify the reviewer and associated project participants.');
      await load({ pm: selectedPm, project: currentProjectId });
    } catch (assignError) { setError(assignError.message); } finally { setBusy(''); }
  }

  async function adoptPlan() {
    const planId = generatedDraft?.planId || currentPlan?.planId;
    if (!planId) return;
    if (!window.confirm('Adopt this human-reviewed plan into canonical project tasks and assignments?')) return;
    setBusy('adopt'); setError('');
    try {
      await send(`/api/project-forge/plans/${planId}/adopt`, 'POST', {
        confirmation: 'ADOPT PROJECT FORGE PLAN',
        createAssignments: true,
        adoptionNote: 'Human-reviewed Project Forge plan adopted from the Project Forge workspace.'
      });
      setNotice('The reviewed plan was adopted into the canonical project task and assignment records.');
      await load({ pm: selectedPm, project: currentProjectId });
    } catch (adoptError) { setError(adoptError.message); } finally { setBusy(''); }
  }

  function renderTab() {
    switch (activeTab) {
      case 'instructions':
        return (
          <div className="forge-instructions">
            <section>
              <h3>Project Forge workflow</h3>
              <ol>
                <li>Select a live project within your server-authorized scope.</li>
                <li>Review its portfolio, budget, schedule, Kanban, decision matrix, and Gantt views.</li>
                <li>Use Celar AI to draft a plan from authorized SOW, GSD, design, and supporting project documents.</li>
                <li>Assign the draft estimate to an engineer already associated with the project for review.</li>
                <li>A Project Manager adopts the human-reviewed draft before canonical tasks or assignments are created.</li>
              </ol>
            </section>
            <section>
              <h3>Governed integrations</h3>
              <dl>
                <div><dt>Project data</dt><dd>Canonical ProjectPulse projects, tasks, assignments, time, expenses, documents, holidays, and identities</dd></div>
                <div><dt>AI</dt><dd>Module 064 capability routing with private project-document grounding and deterministic FlowHive scheduling</dd></div>
                <div><dt>Notifications</dt><dd>Durable Module 065 notifications for review assignment, task assignment, and material updates</dd></div>
                <div><dt>Your scope</dt><dd>{data?.access?.scopeLabel || data?.access?.scope || 'Server-authorized project scope'}</dd></div>
              </dl>
            </section>
          </div>
        );
      case 'setup':
        return (
          <div className="forge-setup-grid">
            <section><h3>Authoritative setup</h3><dl>
              <div><dt>Currency</dt><dd>{currency}</dd></div>
              <div><dt>Working days</dt><dd>{(data?.setup?.workingDays || ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday']).join(', ')}</dd></div>
              <div><dt>Statuses</dt><dd>{(data?.setup?.statuses || STATUS_COLUMNS).map(title).join(', ')}</dd></div>
              <div><dt>Priorities</dt><dd>{(data?.setup?.priorities || ['low', 'normal', 'high', 'critical']).map(title).join(', ')}</dd></div>
            </dl></section>
            <section><h3>Project team</h3>{currentAssignments.length ? <ul>{currentAssignments.map((item) => <li key={item.assignmentId || `${item.userId}-${item.taskId}`}><b>{item.resourceName || item.displayName || item.userName || item.email}</b><span>{item.taskName || item.roleName || 'Project assignment'} · {hours(item.assignedHours)}</span></li>)}</ul> : <Empty />}</section>
            <section><h3>Company holidays</h3>{holidays.length ? <ul>{holidays.slice(0, 20).map((holiday) => <li key={holiday.companyHolidayId || holiday.holidayId || holiday.holidayDate || holiday.date}><b>{holiday.holidayName || holiday.name}</b><span>{shortDate(holiday.holidayDate || holiday.date)}</span></li>)}</ul> : <Empty />}</section>
          </div>
        );
      case 'overall-dashboard':
        return (
          <><div className="forge-metrics"><Metric label="Projects in scope" value={projects.length} /><Metric label="Open tasks" value={projectTasks.filter((task) => taskStatus(task) !== 'completed').length} /><Metric label="Tasks due this month" value={projectTasks.filter((task) => { const due = taskEnd(task); const now = new Date(); return due && new Date(`${due}T12:00:00`).getMonth() === now.getMonth() && new Date(`${due}T12:00:00`).getFullYear() === now.getFullYear(); }).length} /><Metric label="Portfolio estimate" value={hours(estimatedHours)} /><Metric label="Actual hours" value={hours(actualHours)} /><Metric label="Overall progress" value={`${Math.round(progress)}%`} /></div>
          <div className="forge-dashboard-grid"><section><h3>Project status</h3>{projects.map((project) => <div className="forge-project-line" key={projectId(project)}><span>{project.projectCode} · {project.projectName}</span><b>{title(project.status)}</b></div>)}</section><section><h3>Upcoming tasks</h3><TaskTable tasks={projectTasks.filter((task) => taskStatus(task) !== 'completed').sort((a, b) => taskEnd(a).localeCompare(taskEnd(b))).slice(0, 8)} canEditEstimate={false} /></section></div></>
        );
      case 'monthly-calendar': return <CalendarMonth tasks={projectTasks} holidays={holidays} />;
      case 'weekly-calendar': return <CalendarWeek tasks={projectTasks} />;
      case 'project-overview':
        return currentProject ? (
          <><div className="forge-project-hero"><div><span>{currentProject.projectCode}</span><h3>{currentProject.projectName}</h3><p>{currentProject.projectDescription || currentProject.description || 'No project description is available.'}</p></div><Progress value={progress} /></div><div className="forge-metrics"><Metric label="Status" value={title(currentProject.status)} /><Metric label="Project Manager" value={currentProject.projectManagerName || 'Unassigned'} /><Metric label="Start" value={shortDate(currentProject.startDate)} /><Metric label="End" value={shortDate(currentProject.endDate)} /><Metric label="Estimated" value={hours(estimatedHours)} /><Metric label="Actual" value={hours(actualHours)} /></div><TaskTable tasks={projectTasks} canEditEstimate={canEditEstimate} onEstimate={saveEstimate} /></>
        ) : <Empty>Select a project within your authorized scope.</Empty>;
      case 'project-manager':
        return <div className="forge-table-wrap"><table className="forge-table"><thead><tr><th>Project</th><th>PM</th><th>Status</th><th>Dates</th><th>Tasks</th><th>Progress</th><th>Planned cost</th></tr></thead><tbody>{projects.map((project) => { const tasks = allTasks.filter((task) => belongsToProject(task, projectId(project))); const average = tasks.length ? tasks.reduce((sum, task) => sum + taskProgress(task), 0) / tasks.length : 0; return <tr key={projectId(project)}><td><b>{project.projectCode}</b><span>{project.projectName}</span></td><td>{project.projectManagerName || 'Unassigned'}</td><td>{title(project.status)}</td><td>{shortDate(project.startDate)} – {shortDate(project.endDate)}</td><td>{tasks.length}</td><td><Progress value={average} /></td><td>{money(project.plannedTotalProjectCost ?? project.plannedCost, currency)}</td></tr>; })}</tbody></table></div>;
      case 'project-budget':
        return <><div className="forge-metrics"><Metric label="Planned project cost" value={money(plannedCost, currency)} /><Metric label="Actual expenses" value={money(actualExpense, currency)} /><Metric label="Estimated labor" value={hours(estimatedHours)} /><Metric label="Actual labor" value={hours(actualHours)} /><Metric label="Budget variance" value={money(plannedCost - actualExpense, currency)} /></div><div className="forge-budget-bars">{['labor', 'materials', 'fixed', 'travel', 'equipment', 'miscellaneous'].map((bucket) => { const value = projectTasks.reduce((sum, task) => sum + estimatedCost(task, bucket), 0); return <div key={bucket}><span>{title(bucket)}</span><b>{money(value, currency)}</b><i style={{ width: `${plannedCost ? Math.min(100, (value / plannedCost) * 100) : 0}%` }} /></div>; })}</div><section className="forge-expenses"><h3>Expense tracker</h3>{currentExpenses.length ? <div className="forge-table-wrap"><table className="forge-table"><thead><tr><th>Period / upload</th><th>Owner</th><th>Lines</th><th>Total</th><th>Status</th></tr></thead><tbody>{currentExpenses.map((item) => <tr key={item.expenseUploadId || item.projectExpenseUploadId || item.uploadId}><td>{shortDate(item.periodStart || item.uploadedAt)}</td><td>{item.ownerName || item.expenseOwnerName || 'Project team'}</td><td>{item.lineCount || 0}</td><td>{money(item.totalAmount, item.currency || currency)}</td><td>{title(item.status || item.notificationStatus)}</td></tr>)}</tbody></table></div> : <Empty />}</section></>;
      case 'variable-tasks': return <TaskTable tasks={projectTasks.filter((task) => normalize(task.taskType) !== 'recurring')} canEditEstimate={canEditEstimate} onEstimate={saveEstimate} showDecision />;
      case 'recurring-tasks': return <TaskTable tasks={projectTasks.filter(hasRecurrence)} canEditEstimate={canEditEstimate} onEstimate={saveEstimate} />;
      case 'tasks-schedule': return <TaskTable tasks={[...projectTasks].sort((a, b) => taskStart(a).localeCompare(taskStart(b)))} canEditEstimate={canEditEstimate} onEstimate={saveEstimate} />;
      case 'tasks-filter':
        return <><div className="forge-filters"><label>Search<input value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Task, owner, code…" /></label><label>Status<select value={statusFilter} onChange={(event) => setStatusFilter(event.target.value)}><option value="all">All statuses</option>{STATUS_COLUMNS.map((value) => <option key={value} value={value}>{title(value)}</option>)}</select></label><label>Priority<select value={priorityFilter} onChange={(event) => setPriorityFilter(event.target.value)}><option value="all">All priorities</option>{['low', 'normal', 'high', 'critical'].map((value) => <option key={value} value={value}>{title(value)}</option>)}</select></label></div><TaskTable tasks={filteredTasks} canEditEstimate={canEditEstimate} onEstimate={saveEstimate} showDecision /></>;
      case 'decision-matrix':
        return <div className="forge-decision-grid">{DECISION_QUADRANTS.map((quadrant) => { const rows = projectTasks.filter((task) => { const important = Boolean(task.isImportant ?? task.important); const urgent = Boolean(task.isUrgent ?? task.urgent); const derived = important ? (urgent ? 'do' : 'delegate') : (urgent ? 'decide' : 'delete'); return normalize(task.decisionAction) === quadrant.id || ((!task.decisionAction || normalize(task.decisionAction) === 'none') && quadrant.id === derived); }); return <section key={quadrant.id}><h3>{quadrant.label}</h3><small>{quadrant.help}</small>{rows.length ? rows.map((task) => <article key={taskId(task)}><b>{task.taskCode || task.wbsNumber}</b><span>{task.taskName || task.name}</span><em>{hours(taskEstimate(task))}</em></article>) : <Empty />}</section>; })}</div>;
      case 'kanban-board':
        return <div className="forge-kanban">{STATUS_COLUMNS.map((status) => <section key={status}><h3>{title(status)} <span>{projectTasks.filter((task) => taskStatus(task) === status).length}</span></h3>{projectTasks.filter((task) => taskStatus(task) === status).map((task) => <article key={taskId(task)}><b>{task.taskCode || task.wbsNumber}</b><h4>{task.taskName || task.name}</h4><p>{task.taskDescription || task.description}</p><Progress value={taskProgress(task)} /><small>{task.assigneeName || 'Unassigned'} · {shortDate(taskEnd(task))}</small></article>)}</section>)}</div>;
      case 'gantt-chart': return <Gantt tasks={projectTasks} />;
      default: return null;
    }
  }

  return (
    <div className="project-forge projectpulse-module-standard">
      <header className="forge-header">
        <div className="forge-brand"><img src={usSignalLogoUrl} alt="US Signal" /><span>MODULE 033</span><h2>Project Forge</h2><p>Live project planning, governed estimates, and document-grounded AI.</p></div>
        <div className="forge-header-controls">
          {canSelectPm ? <label>Project Manager<select value={selectedPm} onChange={(event) => { const value = event.target.value; setSelectedPm(value); load({ pm: value, project: '' }); }}><option value="">All authorized PMs</option>{projectManagers.map((pm) => <option key={pm.userId || pm.projectManagerUserId} value={pm.userId || pm.projectManagerUserId}>{pm.name || pm.displayName || pm.projectManagerName || pm.email}</option>)}</select></label> : null}
          <label>Project<select value={currentProjectId} onChange={(event) => setSelectedProject(event.target.value)}><option value="">Select a live project</option>{projects.map((project) => <option key={projectId(project)} value={projectId(project)}>{project.projectCode} · {project.projectName}</option>)}</select></label>
          {canUseAi ? <button type="button" className="forge-ai-button" onClick={() => setAiOpen((value) => !value)}>✦ AI plan & estimate</button> : null}
        </div>
      </header>

      {error ? <div className="forge-banner error" role="alert">{error}</div> : null}
      {notice ? <div className="forge-banner success">{notice}</div> : null}

      {aiOpen ? (
        <section className="forge-ai-studio">
          <div><span>MODULE 064 · CELAR AI</span><h3>Document-grounded plan and estimate</h3><p>Uses only project evidence the effective user is authorized to access. The result remains a review draft until a Project Manager explicitly adopts it.</p></div>
          <label>Requested outcome<textarea rows="5" value={aiOutcome} onChange={(event) => setAiOutcome(event.target.value)} /></label>
          <label className="forge-ai-external"><input type="checkbox" checked={allowExternalAi} onChange={(event) => setAllowExternalAi(event.target.checked)} /><span>Allow Module 064 to use only a sanitized, generic reasoning prompt when private evidence is incomplete. Project names, people, costs, and document text remain private.</span></label>
          <div className="forge-ai-actions"><button type="button" disabled={!currentProjectId || busy === 'ai'} onClick={generateAiDraft}>{busy === 'ai' ? 'Generating…' : 'Generate review draft'}</button>{generatedDraft || currentPlan ? <><label>Engineer reviewer<select value={reviewerId} onChange={(event) => setReviewerId(event.target.value)}><option value="">Select an eligible project Engineer</option>{engineers.map((engineer) => <option key={engineer.id} value={engineer.id}>{engineer.name}</option>)}</select></label><button type="button" disabled={!reviewerId || busy === 'reviewer'} onClick={assignReviewer}>Assign review</button>{canManage ? <button type="button" className="adopt" disabled={busy === 'adopt' || (currentPlan?.sourceKind === 'ai_generated' && normalize(currentPlan?.status) !== 'reviewed')} onClick={adoptPlan}>Adopt reviewed plan</button> : null}</> : null}</div>
          {generatedDraft ? <div className="forge-ai-evidence"><b>Confidence: {Math.round(Number(generatedDraft.confidence || 0) * 100)}%</b><span>{generatedDraft.confidenceExplanation || 'Human review is required.'}</span><span>{(generatedDraft.citations || []).length} authorized citation(s)</span><span>{(generatedDraft.warnings || []).length} warning(s)</span></div> : null}
        </section>
      ) : null}

      <nav className="forge-tabs" aria-label="Project Forge workbook tabs">
        {WORKBOOK_TABS.map((tab, index) => <button type="button" key={tab.id} className={activeTab === tab.id ? 'active' : ''} onClick={() => setActiveTab(tab.id)}><span>{String(index + 1).padStart(2, '0')}</span>{tab.label}</button>)}
      </nav>

      <main className="forge-content">
        <div className="forge-content-heading"><div><span>Workbook tab {WORKBOOK_TABS.findIndex((tab) => tab.id === activeTab) + 1} of {WORKBOOK_TABS.length}</span><h2>{WORKBOOK_TABS.find((tab) => tab.id === activeTab)?.label}</h2></div><button type="button" onClick={() => load({ pm: selectedPm, project: currentProjectId })} disabled={loading}>{loading ? 'Loading…' : 'Refresh live data'}</button></div>
        {loading && !data ? <div className="forge-loading">Loading live ProjectPulse records…</div> : renderTab()}
      </main>

      {activity.length ? <footer className="forge-activity"><b>Recent Project Forge activity</b>{activity.slice(0, 3).map((item) => <span key={item.activityId || item.activityEventId || item.id}>{title(item.eventCode || item.action)} · {item.summary || item.changeSummary || shortDate(item.occurredAt || item.createdAt)}</span>)}</footer> : null}
    </div>
  );
}
