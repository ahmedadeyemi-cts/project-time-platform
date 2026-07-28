import { useCallback, useEffect, useMemo, useState } from 'react';
import { createPortal } from 'react-dom';
import { authoritativeApi } from '../projectpulse-authoritative-api.js';
import './ptc-timesheet-management.css';
import './module001-runtime-v2.css';

const DESTINATION_GROUPS = Object.freeze([
  'Requests / Service Requests',
  'Project Tasks',
  'Non-Project Time'
]);

function module001Api(path, options = {}) {
  return authoritativeApi(path, {
    ...options,
    moduleNumber: '001'
  });
}

function isTimesheetRoute() {
  return String(window.location.hash || '#dashboard').replace(/^#/, '').split('?')[0] === 'timesheet';
}

function ownedHost() {
  if (!isTimesheetRoute()) return null;
  return document.querySelector(
    '#module001-ptc-time-steward-host[data-projectpulse-react-owned-slot="true"]'
  );
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
  return Number.isNaN(date.getTime())
    ? value
    : date.toLocaleDateString(undefined, {
        weekday: 'short',
        month: 'short',
        day: 'numeric'
      });
}

function statusLabel(value) {
  return String(value || 'not_started')
    .replaceAll('_', ' ')
    .replace(/\b\w/g, (letter) => letter.toUpperCase());
}

function roleLabel(user) {
  const names = Array.isArray(user?.roleNames) ? user.roleNames : [];
  return names.length ? names.join(' / ') : 'Eligible delivery role';
}

function reasonPrompt(action) {
  const reason = window.prompt(
    `${action}\n\nEnter the required business reason. This will be stored in immutable audit history:`
  );
  return reason?.trim() || '';
}

function activityLabel(entry) {
  if (entry?.nonProjectTimeCategoryId) {
    return entry.nonProjectCategoryName
      || entry.nonProjectCategoryCode
      || 'Non-Project Time';
  }
  return [
    entry?.projectCode || entry?.projectName,
    entry?.taskCode || entry?.taskName
  ].filter(Boolean).join(' · ') || 'Project task';
}

function destinationValue(target = {}) {
  const existing = String(target.selectionValue || '').trim();
  if (existing) return existing;
  if (target.assignmentId) return `assignment:${target.assignmentId}`;
  if (target.projectId && target.taskId) return `project-task:${target.projectId}:${target.taskId}`;
  if (target.nonProjectTimeCategoryId) return `category:${target.nonProjectTimeCategoryId}`;
  return '';
}

function destinationPayload(value) {
  const [kind, first, second] = String(value || '').split(':');
  if (kind === 'assignment' && first) {
    return {
      destinationType: 'assignment',
      assignmentId: first,
      projectId: null,
      taskId: null,
      nonProjectTimeCategoryId: null
    };
  }
  if (kind === 'project-task' && first && second) {
    return {
      destinationType: 'project_task',
      assignmentId: null,
      projectId: first,
      taskId: second,
      nonProjectTimeCategoryId: null
    };
  }
  if (kind === 'category' && first) {
    return {
      destinationType: 'non_project',
      assignmentId: null,
      projectId: null,
      taskId: null,
      nonProjectTimeCategoryId: first
    };
  }
  return null;
}

function targetLabel(target = {}) {
  const assignmentNote = target.requiresAssignment ? ' · assignment will be created' : '';
  return `${target.selectionLabel || target.categoryName || target.taskName || 'Activity'}${assignmentNote}`;
}

function groupTargets(targets) {
  const groups = new Map(DESTINATION_GROUPS.map((name) => [name, []]));
  for (const target of targets || []) {
    const group = DESTINATION_GROUPS.includes(target.groupLabel)
      ? target.groupLabel
      : target.destinationType === 'non_project'
        ? 'Non-Project Time'
        : 'Project Tasks';
    groups.get(group).push(target);
  }
  return DESTINATION_GROUPS.map((name) => ({ name, targets: groups.get(name) }));
}

function EditEntryDialog({ entry, onClose, onSave, busy }) {
  const [hours, setHours] = useState(String(entry.hours ?? ''));
  const [description, setDescription] = useState(entry.description || '');
  const [billable, setBillable] = useState(Boolean(entry.billable));
  const [reason, setReason] = useState('');

  return <div className="ptc-modal" role="presentation">
    <article role="dialog" aria-modal="true" aria-label="Correct time entry">
      <header>
        <div>
          <p className="eyebrow">Correct time entry</p>
          <h2>{activityLabel(entry)}</h2>
        </div>
        <button type="button" onClick={onClose} aria-label="Close">×</button>
      </header>
      <div className="ptc-form-grid">
        <label>
          <span>Hours</span>
          <input
            type="number"
            min="0.01"
            max="24"
            step="0.25"
            value={hours}
            onChange={(event) => setHours(event.target.value)}
          />
        </label>
        <label className="ptc-checkbox">
          <input
            type="checkbox"
            checked={billable}
            onChange={(event) => setBillable(event.target.checked)}
          />
          <span>Billable time</span>
        </label>
      </div>
      <label>
        <span>Description</span>
        <textarea value={description} onChange={(event) => setDescription(event.target.value)} />
      </label>
      <label>
        <span>Required reason</span>
        <textarea
          value={reason}
          onChange={(event) => setReason(event.target.value)}
          placeholder="Why is this correction needed?"
        />
      </label>
      <footer>
        <button type="button" onClick={onClose}>Cancel</button>
        <button
          type="button"
          className="primary"
          disabled={busy || !reason.trim() || !hours}
          onClick={() => onSave({
            hours: Number(hours),
            description,
            billable,
            reason: reason.trim()
          })}
        >
          Save correction
        </button>
      </footer>
    </article>
  </div>;
}

function CreateTaskDialog({ projects, targetUserId, onClose, onCreated, busy }) {
  const [projectId, setProjectId] = useState(projects[0]?.projectId || '');
  const [taskCode, setTaskCode] = useState('');
  const [taskName, setTaskName] = useState('');
  const [taskDescription, setTaskDescription] = useState('');
  const [billable, setBillable] = useState(true);
  const [reason, setReason] = useState('');

  return <div className="ptc-modal" role="presentation">
    <article role="dialog" aria-modal="true" aria-label="Create replacement task">
      <header>
        <div>
          <p className="eyebrow">Create and assign replacement task</p>
          <h2>Make the correct destination available</h2>
        </div>
        <button type="button" onClick={onClose} aria-label="Close">×</button>
      </header>
      <label>
        <span>Project</span>
        <select value={projectId} onChange={(event) => setProjectId(event.target.value)}>
          {projects.map((project) => (
            <option key={project.projectId} value={project.projectId}>
              {project.projectCode} · {project.projectName}
            </option>
          ))}
        </select>
      </label>
      <div className="ptc-form-grid">
        <label>
          <span>Task code</span>
          <input
            value={taskCode}
            onChange={(event) => setTaskCode(event.target.value)}
            placeholder="Example: CORRECTION-01"
          />
        </label>
        <label>
          <span>Task name</span>
          <input
            value={taskName}
            onChange={(event) => setTaskName(event.target.value)}
            placeholder="Clear task name"
          />
        </label>
      </div>
      <label>
        <span>Task description</span>
        <textarea value={taskDescription} onChange={(event) => setTaskDescription(event.target.value)} />
      </label>
      <label className="ptc-checkbox">
        <input
          type="checkbox"
          checked={billable}
          onChange={(event) => setBillable(event.target.checked)}
        />
        <span>Billable task</span>
      </label>
      <label>
        <span>Required reason</span>
        <textarea
          value={reason}
          onChange={(event) => setReason(event.target.value)}
          placeholder="Why is a new task needed for this user’s time?"
        />
      </label>
      <footer>
        <button type="button" onClick={onClose}>Cancel</button>
        <button
          type="button"
          className="primary"
          disabled={busy || !projectId || !taskCode.trim() || !taskName.trim() || !reason.trim()}
          onClick={() => onCreated({
            targetUserId,
            projectId,
            taskCode: taskCode.trim(),
            taskName: taskName.trim(),
            taskDescription: taskDescription.trim(),
            billable,
            reason: reason.trim()
          })}
        >
          Create and assign task
        </button>
      </footer>
    </article>
  </div>;
}

export default function PtcTimesheetManagementPortal() {
  const [host, setHost] = useState(() => ownedHost());
  const [authorized, setAuthorized] = useState(null);
  const [weekStart, setWeekStart] = useState(() => sundayFor(new Date()));
  const [search, setSearch] = useState('');
  const [users, setUsers] = useState([]);
  const [selectedUserId, setSelectedUserId] = useState('');
  const [detail, setDetail] = useState(null);
  const [busy, setBusy] = useState('');
  const [loadingUsers, setLoadingUsers] = useState(false);
  const [loadingWorkspace, setLoadingWorkspace] = useState(false);
  const [error, setError] = useState('');
  const [message, setMessage] = useState('');
  const [editingEntry, setEditingEntry] = useState(null);
  const [creatingTask, setCreatingTask] = useState(false);
  const [moveSelections, setMoveSelections] = useState({});

  useEffect(() => {
    const synchronize = () => setHost((current) => {
      const next = ownedHost();
      return current === next ? current : next;
    });
    synchronize();
    const observer = new MutationObserver(synchronize);
    observer.observe(document.body, { childList: true, subtree: true });
    window.addEventListener('hashchange', synchronize);
    return () => {
      observer.disconnect();
      window.removeEventListener('hashchange', synchronize);
    };
  }, []);

  const loadUsers = useCallback(async () => {
    if (!host) return;
    setLoadingUsers(true);
    try {
      const payload = await module001Api(
        `/api/runtime/timesheet/steward/v2/users?weekStart=${encodeURIComponent(weekStart)}&search=${encodeURIComponent(search)}`,
        { requiredCollections: ['users'] }
      );
      const nextUsers = Array.isArray(payload?.users) ? payload.users : [];
      setAuthorized(true);
      setUsers(nextUsers);
      setSelectedUserId((current) => nextUsers.some((user) => user.userId === current) ? current : '');
      setError(nextUsers.length === 0
        ? 'No eligible active users were returned. Confirm active Engineering, Engineering Lead, Project Management, or Project Management Lead role assignments in Module 012.'
        : '');
    } catch (requestError) {
      const status = requestError?.payload?.status || '';
      if (requestError?.status === 403 && status === 'time_steward_role_required') {
        setAuthorized(false);
        setError('');
      } else {
        setAuthorized(true);
        setUsers([]);
        setSelectedUserId('');
        setError(requestError?.message || 'The eligible time-steward user list could not be loaded.');
      }
    } finally {
      setLoadingUsers(false);
    }
  }, [host, search, weekStart]);

  const loadWorkspace = useCallback(async () => {
    if (!host || authorized !== true || !selectedUserId) {
      setDetail(null);
      setMoveSelections({});
      return;
    }
    setLoadingWorkspace(true);
    try {
      const payload = await module001Api(
        `/api/runtime/timesheet/steward/v2/users/${encodeURIComponent(selectedUserId)}/workspace?weekStart=${encodeURIComponent(weekStart)}`,
        { requiredCollections: ['entries', 'moveTargets', 'nonProjectCategories', 'availableProjects'] }
      );
      setDetail(payload);
      setMoveSelections((current) => {
        const next = {};
        for (const entry of payload.entries || []) next[entry.timeEntryId] = current[entry.timeEntryId] || '';
        return next;
      });
      setError('');
    } catch (requestError) {
      setDetail(null);
      setMoveSelections({});
      setError(requestError?.message || 'The selected user’s time and available destinations could not be loaded.');
    } finally {
      setLoadingWorkspace(false);
    }
  }, [authorized, host, selectedUserId, weekStart]);

  useEffect(() => {
    if (!host) return undefined;
    const timer = window.setTimeout(() => void loadUsers(), 180);
    return () => window.clearTimeout(timer);
  }, [host, loadUsers]);

  useEffect(() => {
    void loadWorkspace();
  }, [loadWorkspace]);

  useEffect(() => {
    const refresh = () => {
      void loadUsers();
      void loadWorkspace();
    };
    window.addEventListener('projectpulse:permissions-changed', refresh);
    window.addEventListener('projectpulse:auth-session-ready', refresh);
    return () => {
      window.removeEventListener('projectpulse:permissions-changed', refresh);
      window.removeEventListener('projectpulse:auth-session-ready', refresh);
    };
  }, [loadUsers, loadWorkspace]);

  const selectedUser = users.find((user) => user.userId === selectedUserId) || null;
  const entries = Array.isArray(detail?.entries) ? detail.entries : [];
  const moveTargets = Array.isArray(detail?.moveTargets) ? detail.moveTargets : [];
  const availableProjects = Array.isArray(detail?.availableProjects) ? detail.availableProjects : [];
  const groupedTargets = useMemo(() => groupTargets(moveTargets), [moveTargets]);
  const editableStatus = (status) => ['draft', 'manager_declined', 'pm_declined'].includes(String(status || '').toLowerCase());

  async function run(key, action, successMessage) {
    setBusy(key);
    setError('');
    setMessage('');
    try {
      await action();
      setMessage(successMessage);
      await Promise.all([loadUsers(), loadWorkspace()]);
    } catch (requestError) {
      setError(requestError?.message || 'The requested time-steward action failed.');
    } finally {
      setBusy('');
    }
  }

  function unsubmitWeek() {
    if (!selectedUserId) return;
    const reason = reasonPrompt(`Return ${selectedUser?.displayName || 'the selected user'}’s week to draft`);
    if (!reason) return;
    void run(
      'unsubmit',
      () => module001Api(
        `/api/timesheet/ptc/users/${selectedUserId}/weeks/${weekStart}/unsubmit`,
        { method: 'POST', body: JSON.stringify({ reason }) }
      ),
      'The week is now draft. Make the corrections, then the user must review and submit it again.'
    );
  }

  function saveEntry(changes) {
    if (!editingEntry) return;
    void run(
      `edit-${editingEntry.timeEntryId}`,
      () => module001Api(`/api/timesheet/ptc/entries/${editingEntry.timeEntryId}`, {
        method: 'PATCH',
        body: JSON.stringify({ targetUserId: selectedUserId, ...changes })
      }),
      'The time entry was corrected and recorded in immutable audit history.'
    ).finally(() => setEditingEntry(null));
  }

  function moveEntry(entry) {
    const selected = moveSelections[entry.timeEntryId];
    const destination = destinationPayload(selected);
    if (!destination) {
      setError('Select a Project Task, Request / Service Request, or Non-Project Time destination.');
      return;
    }
    const target = moveTargets.find((item) => destinationValue(item) === selected);
    const reason = reasonPrompt(
      `Move ${entry.hours} hour(s) from ${activityLabel(entry)} to ${targetLabel(target)}`
    );
    if (!reason) return;
    void run(
      `move-${entry.timeEntryId}`,
      () => module001Api(`/api/runtime/timesheet/steward/v2/entries/${entry.timeEntryId}/move`, {
        method: 'POST',
        body: JSON.stringify({
          targetUserId: selectedUserId,
          ...destination,
          reason
        })
      }),
      'The time entry was moved to the selected activity. The user must review and resubmit the week.'
    );
  }

  function removeEntry(entry) {
    if (!window.confirm(
      `Remove the ${entry.hours}-hour draft entry on ${displayDate(entry.workDate)}? The original values will remain in immutable audit history.`
    )) return;
    const reason = reasonPrompt('Remove the selected incorrect draft entry');
    if (!reason) return;
    void run(
      `remove-${entry.timeEntryId}`,
      () => module001Api(`/api/timesheet/ptc/entries/${entry.timeEntryId}/remove`, {
        method: 'POST',
        body: JSON.stringify({ targetUserId: selectedUserId, reason })
      }),
      'The incorrect draft entry was removed. Its original values remain in immutable audit history.'
    );
  }

  function createTask(payload) {
    void run(
      'create-task',
      () => module001Api('/api/timesheet/ptc/tasks', {
        method: 'POST',
        body: JSON.stringify(payload)
      }),
      'The replacement task was created and assigned to the selected user. It is now available as a move destination.'
    ).finally(() => setCreatingTask(false));
  }

  if (!host || authorized === false) return null;

  return createPortal(
    <section
      className="ptc-time-steward-portal"
      data-projectpulse-ptc-time-steward="true"
      data-projectpulse-time-steward-contract="module001-time-steward-v2"
    >
      <header className="ptc-steward-hero">
        <div>
          <p className="eyebrow">Project Team Coordinator · Time Steward</p>
          <h2>Manage time for other users</h2>
          <p>
            Select an eligible delivery user and week, return submitted time to draft, then correct,
            move, create, assign, or remove time with an immutable business reason.
          </p>
        </div>
        <div className="ptc-no-submit">
          <strong>No submission on behalf</strong>
          <span>The selected user reviews and submits the corrected week. This workspace never submits for them.</span>
        </div>
      </header>

      <div className="ptc-workflow-steps">
        <span>1 · Select eligible user</span>
        <span>2 · Return to draft when needed</span>
        <span>3 · Correct or move to any authorized activity</span>
        <span>4 · User reviews and resubmits</span>
      </div>

      {error ? <p className="ptc-alert error" role="alert">{error}</p> : null}
      {message ? <p className="ptc-alert success" role="status">{message}</p> : null}

      <section className="ptc-toolbar">
        <div className="ptc-week-nav">
          <button type="button" onClick={() => setWeekStart(moveWeek(weekStart, -1))}>Previous week</button>
          <strong>Week of {displayDate(weekStart)}</strong>
          <button type="button" onClick={() => setWeekStart(sundayFor(new Date()))}>Current week</button>
          <button type="button" onClick={() => setWeekStart(moveWeek(weekStart, 1))}>Next week</button>
        </div>
        <label>
          <span>Find user</span>
          <input
            value={search}
            onChange={(event) => setSearch(event.target.value)}
            placeholder="Name or email"
          />
        </label>
        <label>
          <span>Select eligible user</span>
          <select
            value={selectedUserId}
            disabled={loadingUsers}
            onChange={(event) => setSelectedUserId(event.target.value)}
          >
            <option value="">{loadingUsers ? 'Loading eligible users…' : 'Select an eligible user'}</option>
            {users.map((user) => (
              <option key={user.userId} value={user.userId}>
                {user.displayName} · {roleLabel(user)} · {user.email} · {statusLabel(user.status)}
              </option>
            ))}
          </select>
          <small>Engineering, Engineering Lead, Project Management, and Project Management Lead.</small>
        </label>
      </section>

      {selectedUser ? <>
        <section className="ptc-user-summary">
          <article>
            <span>Selected user</span>
            <strong>{selectedUser.displayName}</strong>
            <small>{roleLabel(selectedUser)} · {selectedUser.email}</small>
          </article>
          <article>
            <span>Week status</span>
            <strong>{statusLabel(detail?.timesheet?.status || selectedUser.status)}</strong>
            <small>{selectedUser.entryCount} entry or entries</small>
          </article>
          <article>
            <span>Total hours</span>
            <strong>{Number(selectedUser.totalHours || 0).toFixed(2)}</strong>
            <small>Current selected week</small>
          </article>
          <article className="ptc-user-action">
            <span>Correction workflow</span>
            <button
              type="button"
              disabled={Boolean(busy) || !detail?.timesheet || detail?.timesheet?.status === 'draft'}
              onClick={unsubmitWeek}
            >
              {busy === 'unsubmit' ? 'Returning…' : 'Return week to draft'}
            </button>
            <small>Required before changing submitted or approved time</small>
          </article>
        </section>

        <section className="ptc-destination-catalog" aria-label="Available correction destinations">
          <header>
            <div>
              <p className="eyebrow">Available work for selected user</p>
              <h3>Move time across all supported activity types</h3>
              <p>
                Existing assignments are ready immediately. Selecting another active project task creates the
                required assignment in the same governed transaction. Non-Project Time remains available as a destination.
              </p>
            </div>
            <span>{moveTargets.length} destinations</span>
          </header>
          <div className="ptc-destination-groups">
            {groupedTargets.map((group) => (
              <details key={group.name} open={group.name === 'Requests / Service Requests'}>
                <summary><strong>{group.name}</strong><span>{group.targets.length}</span></summary>
                <div>
                  {group.targets.slice(0, 12).map((target) => (
                    <article key={destinationValue(target)}>
                      <strong>{target.selectionLabel || target.categoryName || target.taskName}</strong>
                      <small>{target.requiresAssignment ? 'Assignment created when selected for a move' : 'Available now'}</small>
                    </article>
                  ))}
                  {group.targets.length > 12 ? <p>{group.targets.length - 12} additional destinations are available in each Move to activity list.</p> : null}
                  {group.targets.length === 0 ? <p>No {group.name.toLowerCase()} are currently available.</p> : null}
                </div>
              </details>
            ))}
          </div>
        </section>

        <section className="ptc-entry-section">
          <header>
            <div>
              <p className="eyebrow">Selected user’s time entries</p>
              <h3>{loadingWorkspace ? 'Loading entries…' : `${entries.length} entry or entries`}</h3>
              <p>
                Edit and removal require a draft week. A move may target a request, any active project task,
                or Non-Project Time. The selected user must review and resubmit afterward.
              </p>
            </div>
            <button
              type="button"
              disabled={Boolean(busy) || !selectedUserId || availableProjects.length === 0}
              onClick={() => setCreatingTask(true)}
            >
              Create replacement task
            </button>
          </header>
          <div className="ptc-entry-table-wrap">
            <table className="ptc-entry-table">
              <thead>
                <tr>
                  <th>Date</th>
                  <th>Current activity</th>
                  <th>Hours and description</th>
                  <th>Correct</th>
                  <th>Move to activity</th>
                  <th>Remove</th>
                </tr>
              </thead>
              <tbody>
                {entries.map((entry) => {
                  const canEdit = editableStatus(entry.status);
                  return <tr key={entry.timeEntryId}>
                    <td>
                      <strong>{displayDate(entry.workDate)}</strong>
                      <small>{statusLabel(entry.status)}</small>
                    </td>
                    <td>
                      <strong>{activityLabel(entry)}</strong>
                      <span>{entry.entryGroup || (entry.nonProjectTimeCategoryId ? 'Non-Project Time' : 'Project Tasks')}</span>
                    </td>
                    <td>
                      <strong>{Number(entry.hours).toFixed(2)} hours · {entry.billable ? 'Billable' : 'Non-billable'}</strong>
                      <span>{entry.description || 'No description'}</span>
                    </td>
                    <td>
                      <button
                        type="button"
                        disabled={Boolean(busy) || !canEdit}
                        onClick={() => setEditingEntry(entry)}
                      >
                        Edit entry
                      </button>
                    </td>
                    <td>
                      <select
                        value={moveSelections[entry.timeEntryId] || ''}
                        disabled={Boolean(busy) || !canEdit}
                        onChange={(event) => setMoveSelections((current) => ({
                          ...current,
                          [entry.timeEntryId]: event.target.value
                        }))}
                      >
                        <option value="">Select destination</option>
                        {groupedTargets.map((group) => (
                          <optgroup key={group.name} label={group.name}>
                            {group.targets.map((target) => (
                              <option key={destinationValue(target)} value={destinationValue(target)}>
                                {targetLabel(target)}
                              </option>
                            ))}
                          </optgroup>
                        ))}
                      </select>
                      <button
                        type="button"
                        disabled={Boolean(busy) || !moveSelections[entry.timeEntryId] || !canEdit}
                        onClick={() => moveEntry(entry)}
                      >
                        {busy === `move-${entry.timeEntryId}` ? 'Moving…' : 'Move time'}
                      </button>
                    </td>
                    <td>
                      <button
                        type="button"
                        className="danger"
                        disabled={Boolean(busy) || !canEdit}
                        onClick={() => removeEntry(entry)}
                      >
                        {busy === `remove-${entry.timeEntryId}` ? 'Removing…' : 'Remove draft entry'}
                      </button>
                    </td>
                  </tr>;
                })}
                {!loadingWorkspace && entries.length === 0 ? <tr>
                  <td colSpan="6">
                    <div className="ptc-empty">
                      <strong>No time entries for this user and week</strong>
                      <span>Select another week or user, or confirm that the user has started their timesheet.</span>
                    </div>
                  </td>
                </tr> : null}
              </tbody>
            </table>
          </div>
        </section>
      </> : <section className="ptc-empty ptc-select-user-prompt">
        <strong>Select an eligible user</strong>
        <span>The workspace will load their week, entries, regular tasks, requests, and Non-Project Time destinations.</span>
      </section>}

      {editingEntry ? <EditEntryDialog
        entry={editingEntry}
        busy={Boolean(busy)}
        onClose={() => setEditingEntry(null)}
        onSave={saveEntry}
      /> : null}

      {creatingTask ? <CreateTaskDialog
        projects={availableProjects}
        targetUserId={selectedUserId}
        busy={Boolean(busy)}
        onClose={() => setCreatingTask(false)}
        onCreated={createTask}
      /> : null}
    </section>,
    host
  );
}
