import { useEffect, useMemo, useState } from 'react';
import './project-flowhive-psa-workspace.css';

const KANBAN_COLUMNS = [
  ['not_started', 'Not started'],
  ['in_progress', 'In progress'],
  ['blocked', 'Blocked'],
  ['complete', 'Complete']
];

const ARTIFACTS = [
  ['timeline-risk', 'Timeline & Risk', 'Schedule, critical path, float, dates, and task-level risk/open-question evidence.'],
  ['raid', 'RAID Log', 'Risks, issues, actions, decisions, assumptions, dependencies, and changes.'],
  ['decision-matrix', 'Decision Matrix', 'Project decisions, ownership, status, due dates, and decision/mitigation evidence.'],
  ['gantt', 'Gantt Chart', 'WBS schedule with start/end dates, duration, predecessor, float, and critical path.'],
  ['monthly-calendar', 'Monthly Calendar', 'All scheduled project tasks organized by project dates for delivery planning.'],
  ['work-breakdown', 'Project Work Breakdown', 'Detailed WBS, schedule, effort, progress, status, and assigned identity.']
];

function sessionHeaders(extra = {}) {
  try {
    const raw = window.localStorage.getItem('projectPulseAuthSession');
    const session = raw ? JSON.parse(raw) : null;
    return {
      ...(session?.sessionToken ? {
        Authorization: `Bearer ${session.sessionToken}`,
        'X-ProjectPulse-Session': session.sessionToken
      } : {}),
      ...extra
    };
  } catch {
    return extra;
  }
}

async function jsonRequest(path, options = {}) {
  const response = await fetch(path, { ...options, headers: sessionHeaders(options.headers || {}) });
  const type = response.headers.get('content-type') || '';
  const body = type.includes('application/json') ? await response.json() : null;
  if (!response.ok) {
    const error = new Error(body?.message || body?.detail || `${path} returned HTTP ${response.status}`);
    error.body = body;
    throw error;
  }
  return body;
}

function label(value) {
  return String(value ?? '').replaceAll('_', ' ').replace(/\b\w/g, (letter) => letter.toUpperCase());
}

function number(value) {
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : null;
}

function hours(value) {
  const parsed = number(value);
  return parsed === null ? 'Not available' : `${parsed.toLocaleString(undefined, { maximumFractionDigits: 2 })} hours`;
}

function money(value, currency = 'USD') {
  const parsed = number(value);
  if (parsed === null) return 'Not available';
  return new Intl.NumberFormat(undefined, { style: 'currency', currency, maximumFractionDigits: 2 }).format(parsed);
}

function isoDate(value) {
  if (!value) return '';
  return String(value).slice(0, 10);
}

function displayDate(value) {
  const date = isoDate(value);
  if (!date) return 'Not scheduled';
  const parsed = new Date(`${date}T12:00:00Z`);
  return Number.isNaN(parsed.getTime()) ? date : parsed.toLocaleDateString();
}

function monthKey(date) {
  return `${date.getUTCFullYear()}-${String(date.getUTCMonth() + 1).padStart(2, '0')}`;
}

function addMonths(key, amount) {
  const [year, month] = String(key).split('-').map(Number);
  const date = new Date(Date.UTC(year, month - 1 + amount, 1));
  return monthKey(date);
}

function daysForMonth(key) {
  const [year, month] = String(key).split('-').map(Number);
  const first = new Date(Date.UTC(year, month - 1, 1));
  const last = new Date(Date.UTC(year, month, 0));
  const cells = [];
  for (let index = 0; index < first.getUTCDay(); index += 1) cells.push(null);
  for (let day = 1; day <= last.getUTCDate(); day += 1) cells.push(new Date(Date.UTC(year, month - 1, day)));
  while (cells.length % 7) cells.push(null);
  return cells;
}

function taskForDate(task, date) {
  if (!task?.startDate || !task?.endDate || !date) return false;
  const key = date.toISOString().slice(0, 10);
  return key >= isoDate(task.startDate) && key <= isoDate(task.endDate);
}

function downloadBlob(blob, fileName) {
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = fileName;
  document.body.appendChild(anchor);
  anchor.click();
  anchor.remove();
  window.setTimeout(() => URL.revokeObjectURL(url), 1000);
}

function Empty({ children }) {
  return <div className="flowhive-psa-empty">{children}</div>;
}

export default function ProjectFlowHivePsaWorkspace({
  mode,
  projectId,
  draftPlan,
  setDraftPlan,
  schedule,
  setSchedule,
  financials,
  controls,
  canManage,
  setDirty,
  setNotice,
  setError
}) {
  const [psa, setPsa] = useState(null);
  const [loading, setLoading] = useState(false);
  const [action, setAction] = useState('');
  const [meetingForm, setMeetingForm] = useState({ title: '', meetingAt: '', customerVisible: false, file: null });
  const [reminders, setReminders] = useState({ enabled: true, leadDays: [2, 1], includeProjectManager: true, includeAssignedTeamMembers: true, includeOverdue: true, timezoneName: 'America/Chicago', deliveryBoundary: 'test_only' });
  const [calendarMonth, setCalendarMonth] = useState(() => monthKey(new Date()));

  async function loadPsa(silent = false) {
    if (!projectId) return;
    if (!silent) setLoading(true);
    try {
      const result = await jsonRequest(`/api/project-flowhive/projects/${projectId}/psa`);
      setPsa(result);
      if (result?.reminderPreferences) setReminders((current) => ({ ...current, ...result.reminderPreferences }));
    } catch (error) {
      if (error.body?.status !== 'migration_103_required') setError?.(error.message);
      setPsa(error.body?.status === 'migration_103_required' ? { migrationRequired: true, ...error.body } : null);
    } finally {
      if (!silent) setLoading(false);
    }
  }

  useEffect(() => {
    setPsa(null);
    if (projectId) loadPsa();
  }, [projectId]);

  useEffect(() => {
    if (draftPlan?.projectStartDate) setCalendarMonth(isoDate(draftPlan.projectStartDate).slice(0, 7));
  }, [draftPlan?.projectStartDate, projectId]);

  const executableTasks = useMemo(
    () => (draftPlan?.tasks || []).filter((task) => !task.isSummary),
    [draftPlan]
  );
  const scheduleTasks = schedule?.tasks || [];
  const scheduleByWbs = useMemo(() => new Map(scheduleTasks.map((task) => [String(task.wbsNumber), task])), [scheduleTasks]);
  const assignments = useMemo(() => new Map((draftPlan?.assignments || []).map((item) => [String(item.taskWbs), item])), [draftPlan]);

  function changeTaskStatus(wbsNumber, status) {
    if (!canManage) return;
    setDraftPlan?.((current) => current ? {
      ...current,
      tasks: (current.tasks || []).map((task) => String(task.wbsNumber) === String(wbsNumber)
        ? { ...task, status, percentComplete: status === 'complete' ? 100 : status === 'not_started' ? 0 : task.percentComplete }
        : task)
    } : current);
    setSchedule?.(null);
    setDirty?.(true);
    setNotice?.(`WBS ${wbsNumber} moved to ${label(status)}. Save the working copy to persist the change.`);
  }

  async function calculateSchedule() {
    if (!draftPlan) return;
    setAction('schedule');
    try {
      const result = await jsonRequest('/api/project-flowhive/schedule/calculate', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(draftPlan)
      });
      setSchedule?.(result);
      setNotice?.('FlowHive recalculated the weekday schedule from the current start date, durations, dependencies, and constraints.');
    } catch (error) {
      setError?.(error.message);
    } finally {
      setAction('');
    }
  }

  async function uploadMeeting(event) {
    event.preventDefault();
    if (!projectId || !meetingForm.file) return;
    setAction('meeting-upload');
    try {
      const data = new FormData();
      data.append('file', meetingForm.file);
      data.append('title', meetingForm.title || meetingForm.file.name.replace(/\.mp4$/i, ''));
      if (meetingForm.meetingAt) data.append('meetingAt', new Date(meetingForm.meetingAt).toISOString());
      data.append('customerVisible', String(Boolean(meetingForm.customerVisible)));
      const result = await jsonRequest(`/api/project-flowhive/projects/${projectId}/meetings`, { method: 'POST', body: data });
      setMeetingForm({ title: '', meetingAt: '', customerVisible: false, file: null });
      setNotice?.(result.message || 'Project meeting uploaded.');
      await loadPsa(true);
    } catch (error) {
      setError?.(error.message);
    } finally {
      setAction('');
    }
  }

  async function updateMeeting(meeting, patch) {
    setAction(`meeting-${meeting.meetingId}`);
    try {
      await jsonRequest(`/api/project-flowhive/projects/${projectId}/meetings/${meeting.meetingId}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(patch)
      });
      await loadPsa(true);
      setNotice?.('Project meeting controls updated.');
    } catch (error) {
      setError?.(error.message);
    } finally {
      setAction('');
    }
  }

  async function downloadMeeting(meeting) {
    setAction(`download-${meeting.meetingId}`);
    const path = `/api/project-flowhive/projects/${projectId}/meetings/${meeting.meetingId}/download`;
    try {
      const response = await fetch(path, { headers: sessionHeaders() });
      if (!response.ok) {
        const body = await response.json().catch(() => ({}));
        throw new Error(body.message || `Meeting download returned HTTP ${response.status}`);
      }
      downloadBlob(await response.blob(), meeting.originalFileName || 'project-meeting.mp4');
    } catch (error) {
      setError?.(error.message);
    } finally {
      setAction('');
    }
  }

  async function saveReminders() {
    setAction('reminders');
    try {
      const result = await jsonRequest(`/api/project-flowhive/projects/${projectId}/task-reminders`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(reminders)
      });
      setReminders((current) => ({ ...current, ...result }));
      setNotice?.('Task due-date reminder controls saved. FlowHive will route eligible reminders through the governed Module 065 notification service.');
      await loadPsa(true);
    } catch (error) {
      setError?.(error.message);
    } finally {
      setAction('');
    }
  }

  async function exportArtifact(kind, format) {
    if (!draftPlan) return;
    setAction(`export-${kind}-${format}`);
    const path = `/api/project-flowhive/projects/${projectId}/artifacts/${kind}/${format}`;
    try {
      const response = await fetch(path, {
        method: 'POST',
        headers: sessionHeaders({ 'Content-Type': 'application/json' }),
        body: JSON.stringify({ plan: draftPlan })
      });
      if (!response.ok) {
        const body = await response.json().catch(() => ({}));
        throw new Error(body.message || `Artifact export returned HTTP ${response.status}`);
      }
      const extension = format === 'excel' ? 'xlsx' : 'pdf';
      downloadBlob(await response.blob(), `${draftPlan.projectCode || 'flowhive'}-${kind}.${extension}`);
      setNotice?.(`US Signal branded ${ARTIFACTS.find(([id]) => id === kind)?.[1] || label(kind)} ${format === 'excel' ? 'Excel' : 'PDF'} generated.`);
    } catch (error) {
      setError?.(error.message);
    } finally {
      setAction('');
    }
  }

  if (!projectId) return <div className="flowhive-view-panel"><Empty>Select a canonical project to use this workspace.</Empty></div>;
  if (psa?.migrationRequired) return <div className="flowhive-view-panel"><div className="flowhive-psa-readiness"><strong>FlowHive PSA database upgrade required</strong><p>{psa.message}</p><code>{psa.requiredMigration}</code></div></div>;

  if (mode === 'kanban') {
    return <div className="flowhive-view-panel flowhive-psa-panel">
      <div className="flowhive-section-heading"><div><span>Execution workspace</span><h3>Kanban task board</h3><p>The board and WBS are two views of the same FlowHive working plan. Moving a card changes only the editable working copy until you save it.</p></div><strong>{executableTasks.length} tasks</strong></div>
      <div className="flowhive-psa-kanban">{KANBAN_COLUMNS.map(([status, title]) => {
        const tasks = executableTasks.filter((task) => (task.status || 'not_started') === status);
        return <section key={status} className={`flowhive-psa-kanban-column ${status}`} onDragOver={(event) => event.preventDefault()} onDrop={(event) => changeTaskStatus(event.dataTransfer.getData('text/flowhive-wbs'), status)}>
          <header><strong>{title}</strong><span>{tasks.length}</span></header>
          <div>{tasks.map((task) => {
            const timing = scheduleByWbs.get(String(task.wbsNumber));
            const assignment = assignments.get(String(task.wbsNumber));
            return <article key={task.wbsNumber} draggable={Boolean(canManage)} onDragStart={(event) => event.dataTransfer.setData('text/flowhive-wbs', String(task.wbsNumber))}>
              <span>{task.phase} · WBS {task.wbsNumber}</span>
              <h4>{task.name}</h4>
              <p>{task.description || 'No task description recorded.'}</p>
              <dl><div><dt>Due</dt><dd>{displayDate(timing?.endDate)}</dd></div><div><dt>Owner</dt><dd>{assignment?.resourceDisplayName || 'Unassigned'}</dd></div><div><dt>Hours</dt><dd>{number(task.remainingEffortHours)?.toLocaleString() ?? '—'}</dd></div><div><dt>Progress</dt><dd>{Math.round(number(task.percentComplete) || 0)}%</dd></div></dl>
              <select aria-label={`Status for ${task.name}`} value={task.status || 'not_started'} disabled={!canManage} onChange={(event) => changeTaskStatus(task.wbsNumber, event.target.value)}>{KANBAN_COLUMNS.map(([value, text]) => <option key={value} value={value}>{text}</option>)}</select>
            </article>;
          })}{!tasks.length ? <Empty>No tasks in this lane.</Empty> : null}</div>
        </section>;
      })}</div>
    </div>;
  }

  if (mode === 'calendar') {
    const days = daysForMonth(calendarMonth);
    const monthTitle = new Date(`${calendarMonth}-01T12:00:00Z`).toLocaleDateString(undefined, { month: 'long', year: 'numeric' });
    return <div className="flowhive-view-panel flowhive-psa-panel">
      <div className="flowhive-section-heading"><div><span>Schedule workspace</span><h3>Monthly project calendar</h3><p>Every scheduled executable WBS task appears on each day it is planned to run. Dates come from the same deterministic schedule used by the WBS and Gantt views.</p></div><button type="button" disabled={action === 'schedule'} onClick={calculateSchedule}>{action === 'schedule' ? 'Calculating…' : 'Recalculate schedule'}</button></div>
      {!schedule ? <Empty>Calculate the schedule to populate the monthly calendar from the project start date, durations, constraints, and dependencies.</Empty> : <>
        <div className="flowhive-psa-calendar-toolbar"><button type="button" onClick={() => setCalendarMonth((current) => addMonths(current, -1))}>Previous</button><strong>{monthTitle}</strong><button type="button" onClick={() => setCalendarMonth((current) => addMonths(current, 1))}>Next</button></div>
        <div className="flowhive-psa-calendar-weekdays">{['Sun','Mon','Tue','Wed','Thu','Fri','Sat'].map((day) => <strong key={day}>{day}</strong>)}</div>
        <div className="flowhive-psa-calendar-grid">{days.map((date, index) => {
          const tasks = date ? scheduleTasks.filter((task) => !task.isSummary && taskForDate(task, date)) : [];
          return <section key={date ? date.toISOString() : `blank-${index}`} className={!date ? 'blank' : ''}>{date ? <><header>{date.getUTCDate()}</header>{tasks.slice(0, 8).map((task) => <article key={task.wbsNumber} title={`${task.wbsNumber} · ${task.name}`}><strong>{task.wbsNumber}</strong><span>{task.name}</span></article>)}{tasks.length > 8 ? <small>+{tasks.length - 8} more</small> : null}</> : null}</section>;
        })}</div>
      </>}
    </div>;
  }

  if (mode === 'meetings') {
    const meetings = psa?.meetings || [];
    return <div className="flowhive-view-panel flowhive-psa-panel">
      <div className="flowhive-section-heading"><div><span>Customer collaboration</span><h3>Project meetings and recordings</h3><p>Upload MP4 meeting recordings to the governed project store. Customer-visible recordings can be downloaded only through an active reviewed FlowHive sharing link that explicitly allows meetings.</p></div><strong>{meetings.length} recording(s)</strong></div>
      <form className="flowhive-psa-meeting-upload" onSubmit={uploadMeeting}>
        <label>Meeting title<input value={meetingForm.title} onChange={(event) => setMeetingForm({ ...meetingForm, title: event.target.value })} placeholder="Weekly project status meeting" /></label>
        <label>Meeting date / time<input type="datetime-local" value={meetingForm.meetingAt} onChange={(event) => setMeetingForm({ ...meetingForm, meetingAt: event.target.value })} /></label>
        <label>MP4 recording<input type="file" accept="video/mp4,.mp4" onChange={(event) => setMeetingForm({ ...meetingForm, file: event.target.files?.[0] || null })} /></label>
        <label className="flowhive-psa-check"><input type="checkbox" checked={meetingForm.customerVisible} onChange={(event) => setMeetingForm({ ...meetingForm, customerVisible: event.target.checked })} />Allow through governed customer sharing</label>
        <button type="submit" className="primary" disabled={!canManage || !meetingForm.file || action === 'meeting-upload'}>{action === 'meeting-upload' ? 'Uploading…' : 'Upload meeting'}</button>
      </form>
      <div className="flowhive-psa-meetings">{meetings.map((meeting) => <article key={meeting.meetingId}>
        <header><div><span>{displayDate(meeting.meetingAt)}</span><h4>{meeting.title}</h4><small>{meeting.originalFileName} · {(Number(meeting.sizeBytes || 0) / 1024 / 1024).toFixed(1)} MB</small></div><strong className={`transcript-${meeting.transcriptStatus}`}>{label(meeting.transcriptStatus)}</strong></header>
        <div className="flowhive-psa-meeting-meta"><span>Customer access: <strong>{meeting.customerVisible ? 'Allowed by meeting control' : 'Internal only'}</strong></span><span>SHA-256: <code>{String(meeting.sha256 || '').slice(0, 16)}…</code></span></div>
        {meeting.actionItems?.length ? <details><summary>Action items ({meeting.actionItems.length})</summary><ul>{meeting.actionItems.map((item, index) => <li key={`${index}-${JSON.stringify(item)}`}>{typeof item === 'string' ? item : item.text || item.title || JSON.stringify(item)}</li>)}</ul></details> : null}
        <footer><button type="button" disabled={Boolean(action)} onClick={() => downloadMeeting(meeting)}>Download MP4</button>{canManage ? <button type="button" disabled={Boolean(action)} onClick={() => updateMeeting(meeting, { customerVisible: !meeting.customerVisible })}>{meeting.customerVisible ? 'Make internal only' : 'Allow customer download'}</button> : null}{canManage && ['failed','unavailable'].includes(meeting.transcriptStatus) ? <button type="button" disabled={Boolean(action)} onClick={() => updateMeeting(meeting, { retryTranscription: true })}>Retry transcription</button> : null}</footer>
      </article>)}{!loading && !meetings.length ? <Empty>No project meeting recordings have been uploaded.</Empty> : null}</div>
    </div>;
  }

  if (mode === 'exports') {
    return <section className="flowhive-psa-export-matrix">
      <header><div><span>Enterprise artifacts</span><h3>US Signal branded project exports</h3><p>Generate the specific artifact your project team or customer needs. Customer delivery remains governed by the reviewed-baseline sharing workflow.</p></div></header>
      <div>{ARTIFACTS.map(([kind, title, description]) => <article key={kind}><div><h4>{title}</h4><p>{description}</p></div><footer><button type="button" disabled={!draftPlan || Boolean(action)} onClick={() => exportArtifact(kind, 'excel')}>Excel</button><button type="button" disabled={!draftPlan || Boolean(action)} onClick={() => exportArtifact(kind, 'pdf')}>PDF</button></footer></article>)}</div>
    </section>;
  }

  if (mode === 'financials') {
    const project = financials?.project || financials || {};
    const currency = controls?.currencyCode || 'USD';
    const planned = number(project.plannedHours) ?? executableTasks.reduce((sum, task) => sum + (number(task.remainingEffortHours) || 0), 0);
    const used = number(project.usedHours);
    const remaining = number(project.remainingHours) ?? (used === null ? null : Math.max(0, planned - used));
    const approved = number(controls?.approvedBudget ?? project.approvedBudget ?? project.contractedValue);
    const actual = [project.laborCost, project.uploadedExpenses ?? project.expenses?.total].map(number).filter((value) => value !== null).reduce((sum, value) => sum + value, 0);
    const hasActual = number(project.laborCost) !== null || number(project.uploadedExpenses ?? project.expenses?.total) !== null;
    const forecast = number(controls?.forecastAtCompletion ?? project.forecastedFinalCost);
    const remainingBudget = approved !== null && hasActual ? approved - actual : null;
    return <section className="flowhive-psa-financial-summary">
      <header><div><span>Project economics</span><h3>Hours, cost, burn, and remaining delivery capacity</h3><p>FlowHive correlates authoritative time and expense actuals with the PM commercial controls. Missing cost data is never estimated.</p></div></header>
      <div><article><span>Total project hours</span><strong>{hours(planned)}</strong></article><article><span>Hours used</span><strong>{hours(used)}</strong></article><article><span>Hours remaining</span><strong>{hours(remaining)}</strong></article><article><span>Approved project value / budget</span><strong>{money(approved, currency)}</strong></article><article><span>Actual cost recorded</span><strong>{hasActual ? money(actual, currency) : 'Not available'}</strong></article><article><span>Remaining budget</span><strong>{money(remainingBudget, currency)}</strong></article><article><span>Forecast at completion</span><strong>{money(forecast, currency)}</strong></article><article><span>Hours burn</span><strong>{used !== null && planned > 0 ? `${Math.round((used / planned) * 100)}%` : 'Not available'}</strong></article></div>
    </section>;
  }

  if (mode === 'status') {
    const decisions = psa?.decisions || [];
    const history = psa?.raidHistory || [];
    return <div className="flowhive-psa-status-extras">
      <section><header><div><span>Decision governance</span><h3>Decision Matrix</h3><p>Decisions are part of the same RAID authority and can be exported as a dedicated US Signal artifact.</p></div><strong>{decisions.length}</strong></header>{decisions.length ? <div className="flowhive-psa-table-wrap"><table><thead><tr><th>Decision</th><th>Status</th><th>Priority</th><th>Owner</th><th>Due</th><th>Decision / mitigation</th></tr></thead><tbody>{decisions.map((item, index) => <tr key={`${item.title}-${index}`}><td>{item.title}</td><td>{label(item.status)}</td><td>{label(item.priority)}</td><td>{item.owner || 'Unassigned'}</td><td>{displayDate(item.dueDate)}</td><td>{item.mitigation || 'Not recorded'}</td></tr>)}</tbody></table></div> : <Empty>No decision records have been added yet.</Empty>}</section>
      <section><header><div><span>Immutable evidence</span><h3>RAID change history</h3><p>Every RAID create, update, and delete operation is retained as append-only evidence for project audit and customer dispute resolution.</p></div><strong>{history.length}</strong></header>{history.slice(0, 100).map((event) => <details key={event.raidEventId}><summary>{new Date(event.occurredAt).toLocaleString()} · {label(event.actionCode)} · {String(event.raidItemId).slice(0, 8)}</summary><pre>{JSON.stringify(event.current || event.prior, null, 2)}</pre></details>)}{!history.length ? <Empty>No RAID changes have been recorded since the immutable audit contract was enabled.</Empty> : null}</section>
    </div>;
  }

  if (mode === 'governance') {
    return <section className="flowhive-psa-reminders">
      <header><div><span>Proactive delivery</span><h3>Task due-date reminders</h3><p>FlowHive evaluates scheduled executable tasks and routes deduplicated reminders to the assigned Project Manager and task identities through the governed enterprise notification service.</p></div><strong>{reminders.enabled ? 'Enabled' : 'Disabled'}</strong></header>
      <div className="flowhive-psa-reminder-grid"><label className="flowhive-psa-check"><input type="checkbox" checked={Boolean(reminders.enabled)} disabled={!canManage} onChange={(event) => setReminders({ ...reminders, enabled: event.target.checked })} />Enable task reminders</label><label>Lead days<input value={(reminders.leadDays || []).join(', ')} disabled={!canManage} onChange={(event) => setReminders({ ...reminders, leadDays: event.target.value.split(',').map((value) => Number(value.trim())).filter((value) => Number.isInteger(value) && value >= 0 && value <= 60) })} placeholder="2, 1" /></label><label>Timezone<input value={reminders.timezoneName || 'America/Chicago'} disabled={!canManage} onChange={(event) => setReminders({ ...reminders, timezoneName: event.target.value })} /></label><label>Delivery boundary<select value={reminders.deliveryBoundary || 'test_only'} disabled={!canManage} onChange={(event) => setReminders({ ...reminders, deliveryBoundary: event.target.value })}><option value="test_only">Protected Test — record/suppress live email</option><option value="production_governed">Production governed — deliver through Module 065</option><option value="locked">Locked — no delivery</option></select></label><label className="flowhive-psa-check"><input type="checkbox" checked={Boolean(reminders.includeProjectManager)} disabled={!canManage} onChange={(event) => setReminders({ ...reminders, includeProjectManager: event.target.checked })} />Notify Project Manager</label><label className="flowhive-psa-check"><input type="checkbox" checked={Boolean(reminders.includeAssignedTeamMembers)} disabled={!canManage} onChange={(event) => setReminders({ ...reminders, includeAssignedTeamMembers: event.target.checked })} />Notify assigned task members</label><label className="flowhive-psa-check"><input type="checkbox" checked={Boolean(reminders.includeOverdue)} disabled={!canManage} onChange={(event) => setReminders({ ...reminders, includeOverdue: event.target.checked })} />Include overdue tasks</label></div>
      <footer><button type="button" className="primary" disabled={!canManage || action === 'reminders'} onClick={saveReminders}>{action === 'reminders' ? 'Saving…' : 'Save reminder policy'}</button></footer>
      <aside><strong>Governance boundary</strong><p>Protected Test remains <code>test_only</code> by default. Moving to production-governed delivery is an explicit Project Manager action and still depends on Module 065 delivery readiness and recipient policy.</p></aside>
    </section>;
  }

  return null;
}
