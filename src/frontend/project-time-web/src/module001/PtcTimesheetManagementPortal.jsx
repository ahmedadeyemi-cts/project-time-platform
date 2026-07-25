import { useCallback, useEffect, useMemo, useState } from 'react';
import { createPortal } from 'react-dom';
import './ptc-timesheet-management.css';

function token() {
  try {
    const session = JSON.parse(localStorage.getItem('projectPulseAuthSession') || 'null');
    return session?.sessionToken || session?.token || session?.accessToken || '';
  } catch {
    return '';
  }
}

function viewAsUserId() {
  try {
    const selected = JSON.parse(localStorage.getItem('projectPulseViewAsUser') || 'null');
    return selected?.userId || localStorage.getItem('projectPulseViewAsUserId') || '';
  } catch {
    return localStorage.getItem('projectPulseViewAsUserId') || '';
  }
}

function headers(hasBody = false) {
  const sessionToken = token();
  const viewAs = viewAsUserId();
  return {
    ...(hasBody ? { 'Content-Type': 'application/json' } : {}),
    ...(sessionToken ? {
      Authorization: `Bearer ${sessionToken}`,
      'X-ProjectPulse-Session': sessionToken,
      'X-Project-Pulse-Session': sessionToken,
      'X-Session-Token': sessionToken
    } : {}),
    ...(viewAs ? { 'X-ProjectPulse-View-As-User': viewAs } : {}),
    'Cache-Control': 'no-cache',
    Pragma: 'no-cache'
  };
}

function requestPath(path, method) {
  if (method !== 'GET') return path;
  const url = new URL(path, window.location.origin);
  if (url.pathname === '/api/timesheet/ptc/users') {
    url.pathname = '/api/runtime/timesheet/steward/users';
  } else if (/^\/api\/timesheet\/ptc\/users\/[0-9a-f-]+\/entries$/i.test(url.pathname)) {
    url.pathname = url.pathname
      .replace('/api/timesheet/ptc/users/', '/api/runtime/timesheet/steward/users/')
      .replace(/\/entries$/, '/workspace');
  }
  return `${url.pathname}${url.search}`;
}

function unwrap(payload) {
  let current = payload && typeof payload === 'object' && !Array.isArray(payload) ? payload : {};
  for (let depth = 0; depth < 3; depth += 1) {
    const key = ['data', 'Data', 'result', 'Result', 'value', 'Value', 'payload', 'Payload']
      .find((candidate) => current?.[candidate] && typeof current[candidate] === 'object' && !Array.isArray(current[candidate]));
    if (!key) break;
    current = current[key];
  }
  return current;
}

async function api(path, options = {}) {
  const method = String(options.method || 'GET').toUpperCase();
  const resolvedPath = requestPath(path, method);
  const response = await fetch(resolvedPath, {
    credentials: 'include',
    cache: 'no-store',
    ...options,
    method,
    headers: {
      ...headers(Boolean(options.body)),
      ...(options.headers || {})
    }
  });
  const raw = await response.text();
  let payload;
  try {
    payload = raw ? JSON.parse(raw) : {};
  } catch {
    const error = new Error(`${resolvedPath} returned non-JSON content instead of ProjectPulse API data.`);
    error.status = response.status;
    error.responsePreview = raw.slice(0, 160);
    throw error;
  }
  payload = unwrap(payload);
  if (!response.ok) {
    const error = new Error(payload.message || payload.Message || payload.detail || payload.Detail || `The time-steward request failed (${response.status}).`);
    error.status = response.status;
    error.payload = payload;
    throw error;
  }
  return payload;
}

function publishUsers(payload) {
  window.__projectPulsePtcRuntimeUsers = payload;
  window.dispatchEvent(new CustomEvent('projectpulse:ptc-runtime-users', { detail: payload }));
}

function publishWorkspace(payload) {
  window.__projectPulsePtcRuntimeWorkspace = payload;
  window.dispatchEvent(new CustomEvent('projectpulse:ptc-runtime-workspace', { detail: payload }));
}

function sundayFor(date) {
  const copy = new Date(date);
  copy.setHours(12, 0, 0, 0);
  copy.setDate(copy.getDate() - copy.getDay());
  return copy.toISOString().slice(0, 10);
}

function moveWeek(weekStart, offset) {
  const date = new Date(`${weekStart}T12:00:00`);
  date.setDate(date.getDate() + offset * 7);
  return date.toISOString().slice(0, 10);
}

function displayDate(value) {
  if (!value) return '—';
  const date = new Date(`${value}T12:00:00`);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleDateString(undefined, { weekday: 'short', month: 'short', day: 'numeric' });
}

function statusLabel(value) {
  return String(value || 'not_started').replaceAll('_', ' ').replace(/\b\w/g, (letter) => letter.toUpperCase());
}

function roleLabel(user) {
  const names = Array.isArray(user?.roleNames) ? user.roleNames : [];
  return names.length ? names.join(' / ') : 'Eligible delivery role';
}

function ensureHost(page) {
  if (!page) return null;
  let host = page.querySelector(':scope > #module001-ptc-time-steward-host');
  if (!host) {
    host = document.createElement('div');
    host.id = 'module001-ptc-time-steward-host';
    host.className = 'module001-ptc-time-steward-host';
    const workspace = page.querySelector('.timesheet-workspace');
    if (workspace) page.insertBefore(host, workspace);
    else page.appendChild(host);
  }
  return host;
}

function hideSubmissionControls(page) {
  const hidden = [];
  page?.querySelectorAll('button').forEach((button) => {
    const text = String(button.textContent || '').trim().toLowerCase();
    if (!text.includes('submit week') && !text.includes('submit timesheet')) return;
    if (button.dataset.ptcSubmissionHidden === 'true') return;
    button.dataset.ptcSubmissionHidden = 'true';
    button.hidden = true;
    hidden.push(button);
  });
  return () => hidden.forEach((button) => {
    button.hidden = false;
    delete button.dataset.ptcSubmissionHidden;
  });
}

function reasonPrompt(action) {
  const reason = window.prompt(`${action}\n\nEnter the required business reason. This will be stored in immutable audit history:`);
  return reason?.trim() || '';
}

function EditEntryDialog({ entry, onClose, onSave, busy }) {
  const [hours, setHours] = useState(String(entry.hours ?? ''));
  const [description, setDescription] = useState(entry.description || '');
  const [billable, setBillable] = useState(Boolean(entry.billable));
  const [reason, setReason] = useState('');

  return <div className="ptc-modal" role="dialog" aria-modal="true" aria-label="Correct time entry"><article>
    <header><div><p className="eyebrow">Correct time entry</p><h2>{entry.projectCode} · {entry.taskName || 'Unassigned task'}</h2></div><button type="button" onClick={onClose} aria-label="Close">×</button></header>
    <div className="ptc-form-grid"><label><span>Hours</span><input type="number" min="0.01" max="24" step="0.25" value={hours} onChange={(event) => setHours(event.target.value)} /></label><label className="ptc-checkbox"><input type="checkbox" checked={billable} onChange={(event) => setBillable(event.target.checked)} /><span>Billable time</span></label></div>
    <label><span>Description</span><textarea value={description} onChange={(event) => setDescription(event.target.value)} /></label>
    <label><span>Required reason</span><textarea value={reason} onChange={(event) => setReason(event.target.value)} placeholder="Why is this correction needed?" /></label>
    <footer><button type="button" onClick={onClose}>Cancel</button><button type="button" className="primary" disabled={busy || !reason.trim() || !hours} onClick={() => onSave({ hours: Number(hours), description, billable, reason: reason.trim() })}>Save correction</button></footer>
  </article></div>;
}

function CreateTaskDialog({ projects, targetUserId, onClose, onCreated, busy }) {
  const [projectId, setProjectId] = useState(projects[0]?.projectId || '');
  const [taskCode, setTaskCode] = useState('');
  const [taskName, setTaskName] = useState('');
  const [taskDescription, setTaskDescription] = useState('');
  const [billable, setBillable] = useState(true);
  const [reason, setReason] = useState('');

  return <div className="ptc-modal" role="dialog" aria-modal="true" aria-label="Create replacement task"><article>
    <header><div><p className="eyebrow">Create and assign replacement task</p><h2>Make the correct destination available</h2></div><button type="button" onClick={onClose} aria-label="Close">×</button></header>
    <label><span>Project</span><select value={projectId} onChange={(event) => setProjectId(event.target.value)}>{projects.map((project) => <option key={project.projectId} value={project.projectId}>{project.projectCode} · {project.projectName}</option>)}</select></label>
    <div className="ptc-form-grid"><label><span>Task code</span><input value={taskCode} onChange={(event) => setTaskCode(event.target.value)} placeholder="Example: CORRECTION-01" /></label><label><span>Task name</span><input value={taskName} onChange={(event) => setTaskName(event.target.value)} placeholder="Clear task name" /></label></div>
    <label><span>Task description</span><textarea value={taskDescription} onChange={(event) => setTaskDescription(event.target.value)} /></label>
    <label className="ptc-checkbox"><input type="checkbox" checked={billable} onChange={(event) => setBillable(event.target.checked)} /><span>Billable task</span></label>
    <label><span>Required reason</span><textarea value={reason} onChange={(event) => setReason(event.target.value)} placeholder="Why is a new task needed for this user’s time?" /></label>
    <footer><button type="button" onClick={onClose}>Cancel</button><button type="button" className="primary" disabled={busy || !projectId || !taskCode.trim() || !taskName.trim() || !reason.trim()} onClick={() => onCreated({ targetUserId, projectId, taskCode: taskCode.trim(), taskName: taskName.trim(), taskDescription: taskDescription.trim(), billable, reason: reason.trim() })}>Create and assign task</button></footer>
  </article></div>;
}

export default function PtcTimesheetManagementPortal() {
  const [host, setHost] = useState(null);
  const [authorized, setAuthorized] = useState(false);
  const [weekStart, setWeekStart] = useState(() => sundayFor(new Date()));
  const [search, setSearch] = useState('');
  const [users, setUsers] = useState([]);
  const [selectedUserId, setSelectedUserId] = useState('');
  const [detail, setDetail] = useState(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [message, setMessage] = useState('');
  const [editingEntry, setEditingEntry] = useState(null);
  const [creatingTask, setCreatingTask] = useState(false);
  const [moveSelections, setMoveSelections] = useState({});

  useEffect(() => {
    const sync = () => {
      const onTimesheet = window.location.hash.replace('#', '') === 'timesheet';
      const page = onTimesheet ? document.querySelector('#timesheet.timesheet-page') : null;
      setHost(page ? ensureHost(page) : null);
    };
    sync();
    const observer = new MutationObserver(sync);
    observer.observe(document.body, { childList: true, subtree: true });
    window.addEventListener('hashchange', sync);
    return () => {
      observer.disconnect();
      window.removeEventListener('hashchange', sync);
    };
  }, []);

  const loadUsers = useCallback(async () => {
    if (!host) return;
    try {
      const payload = await api(`/api/runtime/timesheet/steward/users?weekStart=${encodeURIComponent(weekStart)}&search=${encodeURIComponent(search)}`);
      const nextUsers = Array.isArray(payload?.users) ? payload.users : [];
      publishUsers(payload);
      setAuthorized(true);
      setUsers(nextUsers);
      setSelectedUserId((current) => nextUsers.some((user) => user.userId === current) ? current : nextUsers[0]?.userId || '');
      setError(nextUsers.length === 0
        ? 'The server returned 0 eligible users. Confirm active Engineer, Engineering Lead, Project Management, or Project Management Lead role assignments in User Administration.'
        : '');
    } catch (requestError) {
      publishUsers(null);
      if ([401, 403, 503].includes(requestError.status)) {
        setAuthorized(false);
        setError(requestError.message || 'The time-steward user list is unavailable.');
        return;
      }
      setAuthorized(true);
      setUsers([]);
      setSelectedUserId('');
      setError(requestError.message || 'The time-steward user list could not be loaded.');
    }
  }, [host, search, weekStart]);

  const loadDetail = useCallback(async () => {
    if (!authorized || !selectedUserId) {
      setDetail(null);
      publishWorkspace(null);
      return;
    }
    try {
      const payload = await api(`/api/runtime/timesheet/steward/users/${encodeURIComponent(selectedUserId)}/workspace?weekStart=${encodeURIComponent(weekStart)}`);
      setDetail(payload);
      publishWorkspace(payload);
      const defaults = {};
      for (const entry of payload?.entries || []) defaults[entry.timeEntryId] = payload?.assignments?.[0]?.assignmentId || '';
      setMoveSelections(defaults);
      setError('');
    } catch (requestError) {
      setDetail(null);
      publishWorkspace(null);
      setError(requestError.message || 'The selected user’s time and assignments could not be loaded.');
    }
  }, [authorized, selectedUserId, weekStart]);

  useEffect(() => { void loadUsers(); }, [loadUsers]);
  useEffect(() => { void loadDetail(); }, [loadDetail]);

  useEffect(() => {
    if (!host || !authorized) return undefined;
    const page = host.closest('.timesheet-page');
    page?.classList.add('ptc-time-steward-active');
    const restoreSubmission = hideSubmissionControls(page);
    const observer = new MutationObserver(() => hideSubmissionControls(page));
    if (page) observer.observe(page, { childList: true, subtree: true });
    return () => {
      observer.disconnect();
      restoreSubmission();
      page?.querySelectorAll('[data-ptc-submission-hidden="true"]').forEach((button) => {
        button.hidden = false;
        delete button.dataset.ptcSubmissionHidden;
      });
      page?.classList.remove('ptc-time-steward-active');
    };
  }, [authorized, host]);

  const selectedUser = users.find((user) => user.userId === selectedUserId) || null;
  const entries = Array.isArray(detail?.entries) ? detail.entries : [];
  const assignments = Array.isArray(detail?.assignments) ? detail.assignments : [];
  const projects = useMemo(() => {
    const map = new Map();
    for (const assignment of assignments) map.set(assignment.projectId, { projectId: assignment.projectId, projectCode: assignment.projectCode, projectName: assignment.projectName });
    for (const entry of entries) if (entry.projectId) map.set(entry.projectId, { projectId: entry.projectId, projectCode: entry.projectCode, projectName: entry.projectName });
    return [...map.values()];
  }, [assignments, entries]);

  async function run(action, successMessage) {
    setBusy(true);
    setError('');
    setMessage('');
    try {
      await action();
      setMessage(successMessage);
      await loadUsers();
      await loadDetail();
    } catch (requestError) {
      setError(requestError.message);
    } finally {
      setBusy(false);
    }
  }

  function unsubmitWeek() {
    if (!selectedUserId) return;
    const reason = reasonPrompt(`Return ${selectedUser?.displayName || 'the selected user'}’s week to draft`);
    if (!reason) return;
    void run(
      () => api(`/api/timesheet/ptc/users/${selectedUserId}/weeks/${weekStart}/unsubmit`, { method: 'POST', body: JSON.stringify({ reason }) }),
      'The week is now draft. Make the corrections, then the user must review and submit it again.'
    );
  }

  function saveEntry(changes) {
    if (!editingEntry) return;
    void run(
      () => api(`/api/timesheet/ptc/entries/${editingEntry.timeEntryId}`, { method: 'PATCH', body: JSON.stringify({ targetUserId: selectedUserId, ...changes }) }),
      'The time entry was corrected and recorded in immutable audit history.'
    ).finally(() => setEditingEntry(null));
  }

  function moveEntry(entry) {
    const assignmentId = moveSelections[entry.timeEntryId];
    if (!assignmentId) {
      setError('Select a destination task before moving this time entry.');
      return;
    }
    const target = assignments.find((assignment) => assignment.assignmentId === assignmentId);
    const reason = reasonPrompt(`Move ${entry.hours} hour(s) from ${entry.taskName || 'the current task'} to ${target?.taskName || 'the selected task'}`);
    if (!reason) return;
    void run(
      () => api(`/api/timesheet/ptc/entries/${entry.timeEntryId}/move`, { method: 'POST', body: JSON.stringify({ targetUserId: selectedUserId, assignmentId, reason }) }),
      'The time entry was moved to the selected task. The user must review and resubmit the week.'
    );
  }

  function removeEntry(entry) {
    if (!window.confirm(`Remove the ${entry.hours}-hour draft entry on ${displayDate(entry.workDate)}? The original values will remain in immutable audit history.`)) return;
    const reason = reasonPrompt('Remove the selected incorrect draft entry');
    if (!reason) return;
    void run(
      () => api(`/api/timesheet/ptc/entries/${entry.timeEntryId}/remove`, { method: 'POST', body: JSON.stringify({ targetUserId: selectedUserId, reason }) }),
      'The incorrect draft entry was removed. Its original values remain in immutable audit history.'
    );
  }

  function createTask(payload) {
    void run(
      () => api('/api/timesheet/ptc/tasks', { method: 'POST', body: JSON.stringify(payload) }),
      'The replacement task was created and assigned to the selected user. It is now available in each Move to task list.'
    ).finally(() => setCreatingTask(false));
  }

  if (!host || (!authorized && !error)) return null;

  return createPortal(<section className="ptc-time-steward-portal" data-projectpulse-ptc-time-steward="true">
    <header className="ptc-steward-hero"><div><p className="eyebrow">Project Team Coordinator · Time Steward</p><h2>Manage time for other users</h2><p>Select a user and week, return submitted time to draft, make the necessary correction, move entries between tasks, create a replacement task, or remove an incorrect draft entry.</p></div><div className="ptc-no-submit"><strong>No submission on behalf</strong><span>The selected user reviews and submits the corrected week. This workspace never submits for them.</span></div></header>

    <div className="ptc-workflow-steps"><span>1 · Select user</span><span>2 · Return to draft when needed</span><span>3 · Correct, move, create task, or remove</span><span>4 · User reviews and resubmits</span></div>

    {error ? <p className="ptc-alert error">{error}</p> : null}
    {message ? <p className="ptc-alert success">{message}</p> : null}

    {authorized ? <>
      <section className="ptc-toolbar">
        <div className="ptc-week-nav"><button type="button" onClick={() => setWeekStart(moveWeek(weekStart, -1))}>Previous week</button><strong>Week of {displayDate(weekStart)}</strong><button type="button" onClick={() => setWeekStart(sundayFor(new Date()))}>Current week</button><button type="button" onClick={() => setWeekStart(moveWeek(weekStart, 1))}>Next week</button></div>
        <label><span>Find user</span><input value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Name or email" /></label>
        <label><span>Select user</span><select value={selectedUserId} onChange={(event) => setSelectedUserId(event.target.value)}><option value="">Select an eligible user</option>{users.map((user) => <option key={user.userId} value={user.userId}>{user.displayName} · {roleLabel(user)} · {user.email} · {statusLabel(user.status)}</option>)}</select></label>
      </section>

      {selectedUser ? <section className="ptc-user-summary"><article><span>Selected user</span><strong>{selectedUser.displayName}</strong><small>{roleLabel(selectedUser)} · {selectedUser.email}</small></article><article><span>Week status</span><strong>{statusLabel(detail?.timesheet?.status || selectedUser.status)}</strong><small>{selectedUser.entryCount} entry or entries</small></article><article><span>Total hours</span><strong>{Number(selectedUser.totalHours || 0).toFixed(2)}</strong><small>Current selected week</small></article><article className="ptc-user-action"><span>Correction workflow</span><button type="button" disabled={busy || !detail?.timesheet || detail?.timesheet?.status === 'draft'} onClick={unsubmitWeek}>Return week to draft</button><small>Required before changing submitted or approved time</small></article></section> : null}

      <section className="ptc-entry-section"><header><div><p className="eyebrow">Selected user’s time entries</p><h3>{entries.length} entry or entries</h3><p>Edit and removal are available only after the week is draft. Moving time requires an active assignment to the destination task.</p></div><button type="button" disabled={busy || !selectedUserId || projects.length === 0} onClick={() => setCreatingTask(true)}>Create replacement task</button></header>
        <div className="ptc-entry-table-wrap"><table className="ptc-entry-table"><thead><tr><th>Date</th><th>Current project and task</th><th>Hours and description</th><th>Correct</th><th>Move to task</th><th>Remove</th></tr></thead><tbody>{entries.map((entry) => <tr key={entry.timeEntryId}><td><strong>{displayDate(entry.workDate)}</strong><small>{statusLabel(entry.status)}</small></td><td><strong>{entry.projectCode || entry.nonProjectCategoryName || 'Non-Project'} · {entry.projectName || ''}</strong><span>{entry.taskCode || entry.nonProjectCategoryCode || 'No task'} · {entry.taskName || entry.nonProjectCategoryName || 'Unassigned'}</span></td><td><strong>{Number(entry.hours).toFixed(2)} hours · {entry.billable ? 'Billable' : 'Non-billable'}</strong><span>{entry.description || 'No description'}</span></td><td><button type="button" disabled={busy || !['draft', 'manager_declined', 'pm_declined'].includes(entry.status)} onClick={() => setEditingEntry(entry)}>Edit entry</button></td><td><select value={moveSelections[entry.timeEntryId] || ''} disabled={busy || !['draft', 'manager_declined', 'pm_declined'].includes(entry.status)} onChange={(event) => setMoveSelections((current) => ({ ...current, [entry.timeEntryId]: event.target.value }))}><option value="">Select destination</option>{assignments.map((assignment) => <option key={assignment.assignmentId} value={assignment.assignmentId}>[{assignment.groupLabel || 'Project Tasks'}] {assignment.selectionLabel || `${assignment.projectCode} · ${assignment.taskCode} · ${assignment.taskName}`}</option>)}</select><button type="button" disabled={busy || !moveSelections[entry.timeEntryId] || !['draft', 'manager_declined', 'pm_declined'].includes(entry.status)} onClick={() => moveEntry(entry)}>Move time</button></td><td><button type="button" className="danger" disabled={busy || !['draft', 'manager_declined', 'pm_declined'].includes(entry.status)} onClick={() => removeEntry(entry)}>Remove draft entry</button></td></tr>)}{entries.length === 0 ? <tr><td colSpan="6"><div className="ptc-empty"><strong>No time entries for this user and week</strong><span>Select another week or user, or confirm that the user has started their timesheet.</span></div></td></tr> : null}</tbody></table></div>
      </section>
    </> : null}

    {editingEntry ? <EditEntryDialog entry={editingEntry} busy={busy} onClose={() => setEditingEntry(null)} onSave={saveEntry} /> : null}
    {creatingTask ? <CreateTaskDialog projects={projects} targetUserId={selectedUserId} busy={busy} onClose={() => setCreatingTask(false)} onCreated={createTask} /> : null}
  </section>, host);
}
