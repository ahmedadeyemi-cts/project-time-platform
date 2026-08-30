import { useCallback, useEffect, useMemo, useState } from 'react';
import { createPortal } from 'react-dom';
import { authoritativeApi } from '../projectpulse-authoritative-api.js';
import './module001b-time-reallocation.css';

const DESTINATION_GROUPS = Object.freeze([
  'Requests / Service Requests',
  'Project Tasks',
  'Non-Project Time'
]);

function module001bApi(path, options = {}) {
  return authoritativeApi(path, { ...options, moduleNumber: '001B' });
}

function isModule001BRoute() {
  const route = String(window.location.hash || '#dashboard').replace(/^#/, '').split('?')[0];
  return route === 'time-reallocation' || route === 'module-001b';
}

function sundayFor(date) {
  const copy = new Date(date);
  copy.setHours(12, 0, 0, 0);
  copy.setDate(copy.getDate() - copy.getDay());
  return copy.toISOString().slice(0, 10);
}

function shiftWeek(weekStart, offset) {
  const date = new Date(`${weekStart}T12:00:00`);
  date.setDate(date.getDate() + offset * 7);
  return date.toISOString().slice(0, 10);
}

function displayDate(value) {
  if (!value) return '—';
  const date = new Date(`${value}T12:00:00`);
  if (Number.isNaN(date.getTime())) return value;
  return date.toLocaleDateString(undefined, {
    weekday: 'short', month: 'short', day: 'numeric', year: 'numeric'
  });
}

function statusLabel(value) {
  return String(value || 'unknown')
    .replaceAll('_', ' ')
    .replace(/\b\w/g, (letter) => letter.toUpperCase());
}

function activityLabel(entry) {
  if (entry?.nonProjectTimeCategoryId) {
    return entry.nonProjectCategoryName || entry.nonProjectCategoryCode || 'Non-Project Time';
  }
  return [entry?.projectCode || entry?.projectName, entry?.taskCode || entry?.taskName]
    .filter(Boolean)
    .join(' · ') || 'Project task';
}

function destinationValue(target = {}) {
  if (target.selectionValue) return String(target.selectionValue);
  if (target.assignmentId) return `assignment:${target.assignmentId}`;
  if (target.projectId && target.taskId) return `project-task:${target.projectId}:${target.taskId}`;
  if (target.nonProjectTimeCategoryId) return `category:${target.nonProjectTimeCategoryId}`;
  return '';
}

function destinationPayload(value) {
  const [kind, first, second] = String(value || '').split(':');
  if (kind === 'assignment' && first) {
    return {
      destinationType: 'assignment', assignmentId: first, projectId: null,
      taskId: null, nonProjectTimeCategoryId: null
    };
  }
  if (kind === 'project-task' && first && second) {
    return {
      destinationType: 'project_task', assignmentId: null, projectId: first,
      taskId: second, nonProjectTimeCategoryId: null
    };
  }
  if (kind === 'category' && first) {
    return {
      destinationType: 'non_project', assignmentId: null, projectId: null,
      taskId: null, nonProjectTimeCategoryId: first
    };
  }
  return null;
}

function destinationLabel(target = {}) {
  return target.selectionLabel
    || target.categoryName
    || [target.projectCode || target.projectName, target.taskCode || target.taskName].filter(Boolean).join(' · ')
    || target.taskName
    || 'Activity';
}

function destinationGroup(target = {}) {
  if (DESTINATION_GROUPS.includes(target.groupLabel)) return target.groupLabel;
  if (target.destinationType === 'non_project' || target.nonProjectTimeCategoryId) return 'Non-Project Time';
  const text = [target.taskName, target.taskCode, target.serviceRequestNumber, target.workTaskCategory]
    .filter(Boolean).join(' ').toLowerCase();
  return target.serviceRequestNumber || text.includes('service request') || text.includes('ticket')
    ? 'Requests / Service Requests'
    : 'Project Tasks';
}

function ModuleShell({ children }) {
  return createPortal(
    <div className="module001b-shell" data-module="001B">{children}</div>,
    document.body
  );
}

function NoAccess() {
  return (
    <ModuleShell>
      <main className="module001b-workspace">
        <section className="module001b-card">
          <p className="eyebrow">MODULE 001B · TIME REALLOCATION &amp; CORRECTIONS</p>
          <h1>No Access</h1>
          <p>This module is restricted to Project Team Coordinators and Super Administrators.</p>
          <p>Managers, Project Managers, Engineers, Engineering Leads, Administrators, and all other roles cannot access or execute time reallocation.</p>
          <div className="module001b-actions">
            <button type="button" onClick={() => { window.location.hash = '#dashboard'; }}>Return to dashboard</button>
          </div>
        </section>
      </main>
    </ModuleShell>
  );
}

function emptyTaskDraft(projectId = '') {
  return {
    projectId,
    taskCode: '',
    taskName: '',
    taskDescription: '',
    billable: true,
    reason: ''
  };
}

export default function Module001BTimeReallocationPortal({ allowed }) {
  const [routeActive, setRouteActive] = useState(() => isModule001BRoute());
  const [weekStart, setWeekStart] = useState(() => sundayFor(new Date()));
  const [users, setUsers] = useState([]);
  const [selectedUserId, setSelectedUserId] = useState('');
  const [workspace, setWorkspace] = useState(null);
  const [entryId, setEntryId] = useState('');
  const [destination, setDestination] = useState('');
  const [destinationSearch, setDestinationSearch] = useState('');
  const [reason, setReason] = useState('');
  const [showCreateTask, setShowCreateTask] = useState(false);
  const [taskDraft, setTaskDraft] = useState(() => emptyTaskDraft());
  const [loading, setLoading] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [message, setMessage] = useState('');

  useEffect(() => {
    const refresh = () => setRouteActive(isModule001BRoute());
    window.addEventListener('hashchange', refresh);
    return () => window.removeEventListener('hashchange', refresh);
  }, []);

  const loadUsers = useCallback(async () => {
    if (!routeActive || !allowed) return;
    setLoading(true);
    setError('');
    try {
      const payload = await module001bApi(
        `/api/runtime/timesheet/steward/v2/users?weekStart=${encodeURIComponent(weekStart)}&search=`,
        { requiredCollections: ['users'] }
      );
      const nextUsers = Array.isArray(payload?.users) ? payload.users : [];
      setUsers(nextUsers);
      setSelectedUserId((current) => nextUsers.some((user) => user.userId === current) ? current : '');
    } catch (requestError) {
      setError(requestError?.message || 'Eligible users could not be loaded.');
    } finally {
      setLoading(false);
    }
  }, [allowed, routeActive, weekStart]);

  const loadWorkspace = useCallback(async () => {
    if (!routeActive || !allowed || !selectedUserId) {
      setWorkspace(null);
      return;
    }
    setLoading(true);
    setError('');
    try {
      const payload = await module001bApi(
        `/api/runtime/timesheet/steward/v2/users/${encodeURIComponent(selectedUserId)}/workspace?weekStart=${encodeURIComponent(weekStart)}`,
        { requiredCollections: ['entries', 'moveTargets', 'availableProjects'] }
      );
      setWorkspace(payload);
    } catch (requestError) {
      setWorkspace(null);
      setError(requestError?.message || 'The selected time workspace could not be loaded.');
    } finally {
      setLoading(false);
    }
  }, [allowed, routeActive, selectedUserId, weekStart]);

  useEffect(() => { void loadUsers(); }, [loadUsers]);
  useEffect(() => { void loadWorkspace(); }, [loadWorkspace]);

  const entries = Array.isArray(workspace?.entries) ? workspace.entries : [];
  const targets = Array.isArray(workspace?.moveTargets) ? workspace.moveTargets : [];
  const availableProjects = Array.isArray(workspace?.availableProjects) ? workspace.availableProjects : [];
  const selectedEntry = entries.find((entry) => entry.timeEntryId === entryId) || null;
  const search = destinationSearch.trim().toLowerCase();

  const filteredTargets = useMemo(() => targets.filter((target) => {
    if (!search) return true;
    return [target.selectionLabel, target.projectCode, target.projectName, target.taskCode, target.taskName,
      target.categoryName, target.categoryCode, target.serviceRequestNumber, target.groupLabel]
      .filter(Boolean).join(' ').toLowerCase().includes(search);
  }), [targets, search]);

  const groupedTargets = useMemo(() => DESTINATION_GROUPS.map((group) => ({
    group,
    targets: filteredTargets.filter((target) => destinationGroup(target) === group)
  })), [filteredTargets]);

  useEffect(() => {
    if (!showCreateTask) return;
    setTaskDraft((current) => current.projectId || availableProjects.length === 0
      ? current
      : { ...current, projectId: availableProjects[0].projectId });
  }, [availableProjects, showCreateTask]);

  const canReallocate = Boolean(
    selectedEntry && destinationPayload(destination) && reason.trim().length >= 5 && !busy
  );
  const canCreateTask = Boolean(
    selectedUserId
      && taskDraft.projectId
      && taskDraft.taskCode.trim()
      && taskDraft.taskName.trim()
      && taskDraft.reason.trim().length >= 5
      && !busy
  );

  async function createDestinationTask() {
    if (!canCreateTask) return;
    setBusy(true);
    setError('');
    setMessage('');
    try {
      const result = await module001bApi('/api/timesheet/ptc/tasks', {
        method: 'POST',
        body: JSON.stringify({
          targetUserId: selectedUserId,
          projectId: taskDraft.projectId,
          taskCode: taskDraft.taskCode.trim(),
          taskName: taskDraft.taskName.trim(),
          taskDescription: taskDraft.taskDescription.trim(),
          billable: Boolean(taskDraft.billable),
          reason: taskDraft.reason.trim()
        })
      });
      await loadWorkspace();
      if (result?.projectId && result?.taskId) {
        setDestination(`project-task:${result.projectId}:${result.taskId}`);
      }
      setShowCreateTask(false);
      setTaskDraft(emptyTaskDraft(availableProjects[0]?.projectId || ''));
      setMessage('New destination task created and assigned. Review the correction reason before reallocating the entry.');
    } catch (requestError) {
      setError(requestError?.message || 'The new destination task could not be created.');
    } finally {
      setBusy(false);
    }
  }

  async function reallocate() {
    if (!canReallocate) return;
    const payload = destinationPayload(destination);
    const preservedStatus = selectedEntry.status;
    setBusy(true);
    setError('');
    setMessage('');
    try {
      const result = await module001bApi(
        `/api/runtime/timesheet/steward/001b/reallocation/entries/${encodeURIComponent(selectedEntry.timeEntryId)}/move`,
        {
          method: 'POST',
          body: JSON.stringify({
            targetUserId: selectedUserId,
            ...payload,
            reason: reason.trim()
          })
        }
      );
      setMessage(
        `Allocation corrected successfully. Status stayed ${statusLabel(result?.currentStatus || preservedStatus)}. `
        + 'No worker resubmission, Manager approval, or Project Manager approval is required.'
      );
      setEntryId('');
      setDestination('');
      setReason('');
      await loadWorkspace();
      window.dispatchEvent(new CustomEvent('projectpulse:module001b-time-reallocated', { detail: result }));
    } catch (requestError) {
      setError(requestError?.message || 'The time allocation could not be corrected. No submission state was changed.');
      await loadWorkspace();
    } finally {
      setBusy(false);
    }
  }

  if (!routeActive) return null;
  if (!allowed) return <NoAccess />;

  return (
    <ModuleShell>
      <main className="module001b-workspace">
        <header className="module001b-header">
          <div>
            <p className="eyebrow">MODULE 001B · PROJECT TEAM COORDINATOR</p>
            <h1>Time Reallocation &amp; Corrections</h1>
            <p>
              Correct the allocation of an existing time entry without changing the worker, work date,
              worked hours, or submission/approval status.
            </p>
          </div>
          <button type="button" onClick={() => { window.location.hash = '#dashboard'; }}>Close</button>
        </header>

        <p className="module001b-alert success">
          <strong>Administrative allocation correction:</strong> Submitted and approved time stays in its current status.
          No unsubmit, Draft transition, worker resubmission, Manager approval, or Project Manager approval is triggered.
        </p>
        {error ? <p className="module001b-alert error" role="alert">{error}</p> : null}
        {message ? <p className="module001b-alert success" role="status">{message}</p> : null}

        <section className="module001b-section">
          <header><strong>1. Find the person and week</strong></header>
          <div className="module001b-grid">
            <label>
              <span>Eligible user</span>
              <select value={selectedUserId} disabled={loading || busy} onChange={(event) => {
                setSelectedUserId(event.target.value);
                setEntryId('');
                setDestination('');
                setMessage('');
              }}>
                <option value="">{loading ? 'Loading users…' : 'Select user'}</option>
                {users.map((user) => (
                  <option key={user.userId} value={user.userId}>{user.displayName} · {user.email}</option>
                ))}
              </select>
            </label>
            <label>
              <span>Week</span>
              <input type="date" value={weekStart} disabled={busy}
                onChange={(event) => setWeekStart(sundayFor(new Date(`${event.target.value}T12:00:00`)))} />
              <small className="module001b-muted">Week of {displayDate(weekStart)}</small>
            </label>
            <div className="module001b-inline-controls">
              <button type="button" disabled={busy} onClick={() => setWeekStart(shiftWeek(weekStart, -1))}>Previous</button>
              <button type="button" disabled={busy} onClick={() => setWeekStart(sundayFor(new Date()))}>Current</button>
              <button type="button" disabled={busy} onClick={() => setWeekStart(shiftWeek(weekStart, 1))}>Next</button>
            </div>
          </div>
        </section>

        <section className="module001b-section">
          <header><strong>2. Select the existing time entry</strong></header>
          <div className="module001b-entry-list">
            {entries.map((entry) => (
              <label key={entry.timeEntryId} className={`module001b-choice ${entryId === entry.timeEntryId ? 'selected' : ''}`}>
                <input type="radio" name="module001b-entry" checked={entryId === entry.timeEntryId} disabled={busy}
                  onChange={() => { setEntryId(entry.timeEntryId); setDestination(''); setMessage(''); }} />
                <span>
                  <strong>{displayDate(entry.workDate)} · {Number(entry.hours).toFixed(2)} hours · {statusLabel(entry.status)}</strong>
                  <small>{activityLabel(entry)}</small>
                  <small>{entry.description || 'No description'}</small>
                </span>
              </label>
            ))}
            {selectedUserId && !loading && entries.length === 0 ? <p>No time entries exist for this week.</p> : null}
          </div>
        </section>

        <section className="module001b-section">
          <header><strong>3. Choose the correct destination</strong></header>
          <label>
            <span>Search destinations</span>
            <input type="search" value={destinationSearch} disabled={!entryId || busy}
              onChange={(event) => setDestinationSearch(event.target.value)}
              placeholder="Project, task, request number, or non-project activity" />
          </label>
          <div className="module001b-actions">
            <button type="button" disabled={!selectedUserId || availableProjects.length === 0 || busy}
              onClick={() => setShowCreateTask((current) => !current)}>
              {showCreateTask ? 'Hide new task form' : 'Create new billable / non-billable task'}
            </button>
          </div>

          {showCreateTask ? (
            <section className="module001b-card">
              <strong>Create and assign a destination task</strong>
              <div className="module001b-grid">
                <label>
                  <span>Project</span>
                  <select value={taskDraft.projectId} disabled={busy}
                    onChange={(event) => setTaskDraft((current) => ({ ...current, projectId: event.target.value }))}>
                    <option value="">Select project</option>
                    {availableProjects.map((project) => (
                      <option key={project.projectId} value={project.projectId}>
                        {project.projectCode} · {project.projectName}
                      </option>
                    ))}
                  </select>
                </label>
                <label>
                  <span>Task code</span>
                  <input value={taskDraft.taskCode} disabled={busy}
                    onChange={(event) => setTaskDraft((current) => ({ ...current, taskCode: event.target.value }))} />
                </label>
                <label>
                  <span>Task name</span>
                  <input value={taskDraft.taskName} disabled={busy}
                    onChange={(event) => setTaskDraft((current) => ({ ...current, taskName: event.target.value }))} />
                </label>
              </div>
              <label>
                <span>Task description</span>
                <textarea value={taskDraft.taskDescription} disabled={busy}
                  onChange={(event) => setTaskDraft((current) => ({ ...current, taskDescription: event.target.value }))} />
              </label>
              <label className="module001b-check">
                <input type="checkbox" checked={taskDraft.billable} disabled={busy}
                  onChange={(event) => setTaskDraft((current) => ({ ...current, billable: event.target.checked }))} />
                <span>Billable task (clear for non-billable)</span>
              </label>
              <label>
                <span>Required task-creation reason</span>
                <textarea value={taskDraft.reason} disabled={busy}
                  onChange={(event) => setTaskDraft((current) => ({ ...current, reason: event.target.value }))} />
              </label>
              <div className="module001b-actions">
                <button type="button" className="primary" disabled={!canCreateTask} onClick={() => void createDestinationTask()}>
                  {busy ? 'Working…' : 'Create destination task'}
                </button>
              </div>
            </section>
          ) : null}

          {groupedTargets.map(({ group, targets: groupItems }) => (
            <section key={group} className="module001b-card">
              <strong>{group} · {groupItems.length}</strong>
              <div className="module001b-destination-list">
                {groupItems.map((target) => {
                  const value = destinationValue(target);
                  return (
                    <label key={value} className={`module001b-choice ${destination === value ? 'selected' : ''}`}>
                      <input type="radio" name="module001b-destination" value={value}
                        checked={destination === value} disabled={!entryId || busy}
                        onChange={() => setDestination(value)} />
                      <span>
                        <strong>{destinationLabel(target)}</strong>
                        <small>{target.requiresAssignment ? 'Assignment will be created for the original work date.' : 'Available now'}</small>
                      </span>
                    </label>
                  );
                })}
                {groupItems.length === 0 ? <p>No matching destinations.</p> : null}
              </div>
            </section>
          ))}
        </section>

        <section className="module001b-section">
          <header><strong>4. Required correction reason</strong></header>
          <label>
            <span>Business reason</span>
            <textarea value={reason} disabled={!selectedEntry || busy}
              onChange={(event) => setReason(event.target.value)}
              placeholder="Explain why the existing allocation is being corrected (minimum 5 characters)." />
          </label>
          {selectedEntry ? (
            <p className="module001b-muted">
              Protected values: {displayDate(selectedEntry.workDate)} · {Number(selectedEntry.hours).toFixed(2)} hours · {statusLabel(selectedEntry.status)}.
            </p>
          ) : null}
          <div className="module001b-actions">
            <button type="button" className="primary" disabled={!canReallocate} onClick={() => void reallocate()}>
              {busy ? 'Reallocating…' : 'Reallocate time'}
            </button>
          </div>
        </section>
      </main>
    </ModuleShell>
  );
}
