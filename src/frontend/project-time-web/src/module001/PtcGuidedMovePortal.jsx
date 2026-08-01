import { useCallback, useEffect, useMemo, useState } from 'react';
import { createPortal } from 'react-dom';
import { authoritativeApi } from '../projectpulse-authoritative-api.js';
import './ptc-guided-move.css';

const DESTINATION_GROUPS = Object.freeze([
  'Requests / Service Requests',
  'Project Tasks',
  'Non-Project Time'
]);

const CLASSIFICATIONS = Object.freeze([
  ['non_billable', 'Non-billable work'],
  ['administrative', 'Administrative'],
  ['training', 'Training'],
  ['leave', 'Leave'],
  ['paid_time_off', 'Paid time off'],
  ['unpaid_time_off', 'Unpaid time off']
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
    weekday: 'short',
    month: 'short',
    day: 'numeric',
    year: 'numeric'
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

function normalizeCode(value) {
  return String(value || '')
    .trim()
    .toUpperCase()
    .replace(/\s+/g, '_')
    .replace(/[^A-Z0-9._-]+/g, '_')
    .replace(/^[_\-.]+|[_\-.]+$/g, '')
    .slice(0, 100);
}

function activityLabel(entry) {
  if (entry?.nonProjectTimeCategoryId) {
    return entry.nonProjectCategoryName || entry.nonProjectCategoryCode || 'Non-Project Time';
  }
  return [
    entry?.projectCode || entry?.projectName,
    entry?.taskCode || entry?.taskName
  ].filter(Boolean).join(' · ') || 'Project task';
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

function destinationLabel(target = {}) {
  const base = target.selectionLabel || target.categoryName || target.taskName || 'Activity';
  return target.requiresAssignment ? `${base} · assignment will be created` : base;
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

function searchableText(target) {
  return [
    target.selectionLabel,
    target.categoryName,
    target.categoryCode,
    target.customerName,
    target.projectCode,
    target.projectName,
    target.taskCode,
    target.taskName,
    target.serviceRequestNumber,
    target.groupLabel
  ].filter(Boolean).join(' ').toLowerCase();
}

function editableStatus(status) {
  return ['draft', 'manager_declined', 'pm_declined'].includes(String(status || '').toLowerCase());
}

function QuickCreateNonProject({ reason, onCancel, onCreated }) {
  const [name, setName] = useState('');
  const [code, setCode] = useState('');
  const [description, setDescription] = useState('');
  const [classification, setClassification] = useState('non_billable');
  const [requiresApproval, setRequiresApproval] = useState(true);
  const [creationReason, setCreationReason] = useState(reason || 'Required Move Time destination');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');

  const generatedCode = useMemo(() => normalizeCode(name), [name]);
  const effectiveCode = normalizeCode(code || generatedCode);
  const canCreate = name.trim().length >= 2
    && effectiveCode.length >= 2
    && creationReason.trim().length >= 5
    && !busy;

  async function create() {
    setBusy(true);
    setError('');
    try {
      const result = await module001Api('/api/timesheet/ptc/non-project-activities', {
        method: 'POST',
        body: JSON.stringify({
          taskCode: effectiveCode,
          taskName: name.trim(),
          taskDescription: description.trim(),
          utilizationClassification: classification,
          requiresApproval,
          displayOrder: 500,
          reason: creationReason.trim()
        })
      });
      onCreated(result);
    } catch (requestError) {
      setError(requestError?.message || 'The non-project activity could not be created.');
    } finally {
      setBusy(false);
    }
  }

  return (
    <section className="ptc-guided-quick-create">
      <header>
        <div>
          <strong>Create a non-project activity</strong>
          <span>It becomes an immediate Move Time destination and never routes to a PM.</span>
        </div>
        <button type="button" onClick={onCancel}>Cancel</button>
      </header>
      {error ? <p className="ptc-guided-alert error" role="alert">{error}</p> : null}
      <div className="ptc-guided-form-grid">
        <label>
          <span>Activity name</span>
          <input value={name} onChange={(event) => setName(event.target.value)} placeholder="Internal coordination" autoFocus />
        </label>
        <label>
          <span>Activity code</span>
          <input value={code} onChange={(event) => setCode(normalizeCode(event.target.value))} placeholder={generatedCode || 'INTERNAL_COORDINATION'} />
          <small>{effectiveCode || 'Generated from the activity name'}</small>
        </label>
        <label>
          <span>Utilization classification</span>
          <select value={classification} onChange={(event) => setClassification(event.target.value)}>
            {CLASSIFICATIONS.map(([value, label]) => <option key={value} value={value}>{label}</option>)}
          </select>
        </label>
        <label className="ptc-guided-checkbox">
          <input type="checkbox" checked={requiresApproval} onChange={(event) => setRequiresApproval(event.target.checked)} />
          <span>Time against this activity requires approval</span>
        </label>
      </div>
      <label>
        <span>Description</span>
        <textarea value={description} onChange={(event) => setDescription(event.target.value)} placeholder="Explain when this activity should be used." />
      </label>
      <label>
        <span>Required creation reason</span>
        <textarea value={creationReason} onChange={(event) => setCreationReason(event.target.value)} />
        <small>This is immutable creation evidence, not an approval comment.</small>
      </label>
      <button type="button" className="primary" disabled={!canCreate} onClick={create}>
        {busy ? 'Creating activity…' : 'Create and select activity'}
      </button>
    </section>
  );
}

function MoveWizard({
  users,
  loadingUsers,
  weekStart,
  setWeekStart,
  selectedUserId,
  setSelectedUserId,
  workspace,
  loadingWorkspace,
  loadWorkspace,
  onClose
}) {
  const [entryId, setEntryId] = useState('');
  const [destination, setDestination] = useState('');
  const [destinationSearch, setDestinationSearch] = useState('');
  const [reason, setReason] = useState('');
  const [returnWeekToDraft, setReturnWeekToDraft] = useState(true);
  const [quickCreate, setQuickCreate] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [message, setMessage] = useState('');

  const selectedUser = users.find((user) => user.userId === selectedUserId) || null;
  const entries = Array.isArray(workspace?.entries) ? workspace.entries : [];
  const moveTargets = Array.isArray(workspace?.moveTargets) ? workspace.moveTargets : [];
  const selectedEntry = entries.find((entry) => entry.timeEntryId === entryId) || null;
  const selectedTarget = moveTargets.find((target) => destinationValue(target) === destination) || null;
  const sourceEditable = selectedEntry ? editableStatus(selectedEntry.status) : false;
  const normalizedDestinationSearch = destinationSearch.trim().toLowerCase();
  const filteredTargets = useMemo(() => moveTargets.filter((target) => (
    !normalizedDestinationSearch || searchableText(target).includes(normalizedDestinationSearch)
  )), [moveTargets, normalizedDestinationSearch]);
  const grouped = useMemo(() => groupTargets(filteredTargets), [filteredTargets]);
  const canMove = Boolean(
    selectedUserId
    && selectedEntry
    && destinationPayload(destination)
    && reason.trim().length >= 5
    && (sourceEditable || returnWeekToDraft)
    && !busy
  );

  useEffect(() => {
    if (!entries.some((entry) => entry.timeEntryId === entryId)) setEntryId('');
  }, [entries, entryId]);

  async function move() {
    if (!canMove) return;
    const target = destinationPayload(destination);
    setBusy(true);
    setError('');
    setMessage('');
    let returnedToDraft = false;
    try {
      if (!sourceEditable) {
        await module001Api(
          `/api/timesheet/ptc/users/${encodeURIComponent(selectedUserId)}/weeks/${encodeURIComponent(weekStart)}/unsubmit`,
          {
            method: 'POST',
            body: JSON.stringify({
              reason: `Move Time correction: ${reason.trim()}`
            })
          }
        );
        returnedToDraft = true;
      }

      const result = await module001Api(
        `/api/runtime/timesheet/steward/v2/entries/${encodeURIComponent(selectedEntry.timeEntryId)}/move`,
        {
          method: 'POST',
          body: JSON.stringify({
            targetUserId: selectedUserId,
            ...target,
            reason: reason.trim()
          })
        }
      );

      setMessage(
        `${returnedToDraft ? 'The week was returned to draft and ' : ''}`
        + `the ${selectedEntry.hours}-hour entry was moved to ${destinationLabel(selectedTarget)}. `
        + 'The user must review and resubmit the corrected week.'
      );
      setEntryId('');
      setDestination('');
      setReason('');
      await loadWorkspace();
      window.dispatchEvent(new CustomEvent('projectpulse:approval-queue-changed'));
      window.dispatchEvent(new CustomEvent('projectpulse:ptc-time-moved', { detail: result }));
    } catch (requestError) {
      setError(
        returnedToDraft
          ? `${requestError?.message || 'The move could not be completed.'} The week remains draft so the correction can be completed safely.`
          : requestError?.message || 'The Move Time operation could not be completed.'
      );
      await loadWorkspace();
    } finally {
      setBusy(false);
    }
  }

  return (
    <article className="ptc-guided-dialog" role="dialog" aria-modal="true" aria-labelledby="ptc-guided-title">
      <header className="ptc-guided-header">
        <div>
          <p className="eyebrow">PROJECT TEAM COORDINATOR · GUIDED CORRECTION</p>
          <h2 id="ptc-guided-title">Move Time wizard</h2>
          <p>Select the user, entry, and destination. The wizard handles the required draft step and records the business reason in immutable history.</p>
        </div>
        <button type="button" className="close" onClick={onClose} aria-label="Close Move Time wizard">×</button>
      </header>

      <div className="ptc-guided-steps" aria-label="Move Time steps">
        <span className={selectedUserId ? 'complete' : 'active'}>1 · User and week</span>
        <span className={entryId ? 'complete' : selectedUserId ? 'active' : ''}>2 · Source entry</span>
        <span className={destination ? 'complete' : entryId ? 'active' : ''}>3 · Destination</span>
        <span className={reason.trim().length >= 5 ? 'complete' : destination ? 'active' : ''}>4 · Review and move</span>
      </div>

      {error ? <p className="ptc-guided-alert error" role="alert">{error}</p> : null}
      {message ? <p className="ptc-guided-alert success" role="status">{message}</p> : null}

      <section className="ptc-guided-section">
        <header><strong>1. Select user and week</strong></header>
        <div className="ptc-guided-form-grid three">
          <label>
            <span>Eligible user</span>
            <select value={selectedUserId} disabled={loadingUsers || busy} onChange={(event) => {
              setSelectedUserId(event.target.value);
              setEntryId('');
              setDestination('');
            }}>
              <option value="">{loadingUsers ? 'Loading eligible users…' : 'Select user'}</option>
              {users.map((user) => (
                <option key={user.userId} value={user.userId}>
                  {user.displayName} · {roleLabel(user)} · {user.email}
                </option>
              ))}
            </select>
          </label>
          <label>
            <span>Week</span>
            <input type="date" value={weekStart} disabled={busy} onChange={(event) => setWeekStart(sundayFor(new Date(`${event.target.value}T12:00:00`)))} />
            <small>Week of {displayDate(weekStart)}</small>
          </label>
          <div className="ptc-guided-week-buttons">
            <button type="button" disabled={busy} onClick={() => setWeekStart(shiftWeek(weekStart, -1))}>Previous</button>
            <button type="button" disabled={busy} onClick={() => setWeekStart(sundayFor(new Date()))}>Current</button>
            <button type="button" disabled={busy} onClick={() => setWeekStart(shiftWeek(weekStart, 1))}>Next</button>
          </div>
        </div>
        {selectedUser ? (
          <div className="ptc-guided-context">
            <span><strong>{selectedUser.displayName}</strong> · {selectedUser.email}</span>
            <span>Week status: <strong>{statusLabel(workspace?.timesheet?.status || selectedUser.status)}</strong></span>
            <span>{entries.length} entry or entries</span>
          </div>
        ) : null}
      </section>

      <section className="ptc-guided-section">
        <header>
          <strong>2. Select the time entry to move</strong>
          {loadingWorkspace ? <span>Loading…</span> : null}
        </header>
        {!selectedUserId ? <p className="ptc-guided-empty">Select an eligible user first.</p> : null}
        {selectedUserId && !loadingWorkspace && entries.length === 0 ? (
          <p className="ptc-guided-empty">No time entries exist for this user and week.</p>
        ) : null}
        <div className="ptc-guided-entry-list">
          {entries.map((entry) => (
            <label key={entry.timeEntryId} className={entryId === entry.timeEntryId ? 'selected' : ''}>
              <input
                type="radio"
                name="ptc-guided-entry"
                value={entry.timeEntryId}
                checked={entryId === entry.timeEntryId}
                disabled={busy}
                onChange={() => {
                  setEntryId(entry.timeEntryId);
                  setDestination('');
                  setMessage('');
                }}
              />
              <span>
                <strong>{displayDate(entry.workDate)} · {Number(entry.hours).toFixed(2)} hours</strong>
                <small>{activityLabel(entry)}</small>
                <small>{entry.description || 'No description'} · {statusLabel(entry.status)}</small>
              </span>
            </label>
          ))}
        </div>
      </section>

      <section className="ptc-guided-section">
        <header>
          <div>
            <strong>3. Choose the correct destination</strong>
            <span>Search across requests, project tasks, and non-project activities.</span>
          </div>
          <button type="button" disabled={busy || !selectedUserId} onClick={() => setQuickCreate((current) => !current)}>
            {quickCreate ? 'Close quick create' : 'Create non-project activity'}
          </button>
        </header>

        {quickCreate ? (
          <QuickCreateNonProject
            reason={reason}
            onCancel={() => setQuickCreate(false)}
            onCreated={async (result) => {
              setQuickCreate(false);
              await loadWorkspace();
              setDestination(result.selectionValue || `category:${result.nonProjectTimeCategoryId}`);
              setMessage(result.message || 'The non-project activity was created and selected.');
            }}
          />
        ) : null}

        <label className="ptc-guided-destination-search">
          <span>Find destination</span>
          <input
            type="search"
            value={destinationSearch}
            disabled={busy || !entryId}
            onChange={(event) => setDestinationSearch(event.target.value)}
            placeholder="Project, task, customer, request, or non-project activity"
          />
        </label>

        <div className="ptc-guided-destination-groups">
          {grouped.map((group) => (
            <section key={group.name}>
              <header>
                <strong>{group.name}</strong>
                <span>{group.targets.length}</span>
              </header>
              <div>
                {group.targets.slice(0, 80).map((target) => {
                  const value = destinationValue(target);
                  return (
                    <button
                      type="button"
                      key={value}
                      className={destination === value ? 'selected' : ''}
                      disabled={busy || !entryId}
                      onClick={() => setDestination(value)}
                    >
                      <strong>{destinationLabel(target)}</strong>
                      <small>
                        {target.destinationType === 'non_project'
                          ? 'Manager then PTC approval; PM not required'
                          : target.requiresAssignment
                            ? 'The user assignment will be created automatically'
                            : 'Available now'}
                      </small>
                    </button>
                  );
                })}
                {group.targets.length === 0 ? <p>No matching {group.name.toLowerCase()}.</p> : null}
                {group.targets.length > 80 ? <p>Refine the search to view the remaining destinations.</p> : null}
              </div>
            </section>
          ))}
        </div>
      </section>

      <section className="ptc-guided-section review">
        <header><strong>4. Review and move</strong></header>
        <div className="ptc-guided-review-grid">
          <article>
            <span>From</span>
            <strong>{selectedEntry ? activityLabel(selectedEntry) : 'Select an entry'}</strong>
            <small>{selectedEntry ? `${selectedEntry.hours} hour(s) on ${displayDate(selectedEntry.workDate)}` : '—'}</small>
          </article>
          <article>
            <span>To</span>
            <strong>{selectedTarget ? destinationLabel(selectedTarget) : 'Select a destination'}</strong>
            <small>{selectedTarget?.groupLabel || '—'}</small>
          </article>
        </div>

        {selectedEntry && !sourceEditable ? (
          <label className="ptc-guided-reopen">
            <input
              type="checkbox"
              checked={returnWeekToDraft}
              disabled={busy}
              onChange={(event) => setReturnWeekToDraft(event.target.checked)}
            />
            <span>
              <strong>Return the week to draft, then move this entry</strong>
              <small>
                This entry is {statusLabel(selectedEntry.status)}. The wizard will perform the required governed return-to-draft step first. The user must review and resubmit afterward.
              </small>
            </span>
          </label>
        ) : null}

        <label>
          <span>Required business reason</span>
          <textarea
            value={reason}
            disabled={busy}
            onChange={(event) => setReason(event.target.value)}
            placeholder="Why must this time be moved?"
          />
          <small>The reason is written to immutable time-management evidence. It is not an approval comment.</small>
        </label>
      </section>

      <footer className="ptc-guided-footer">
        <button type="button" disabled={busy} onClick={onClose}>Close</button>
        <button type="button" className="primary" disabled={!canMove} onClick={move}>
          {busy
            ? 'Completing governed move…'
            : selectedEntry && !sourceEditable
              ? 'Return to draft and move time'
              : 'Move time'}
        </button>
      </footer>
    </article>
  );
}

export default function PtcGuidedMovePortal() {
  const [routeActive, setRouteActive] = useState(() => isTimesheetRoute());
  const [authorized, setAuthorized] = useState(null);
  const [open, setOpen] = useState(false);
  const [weekStart, setWeekStart] = useState(() => sundayFor(new Date()));
  const [users, setUsers] = useState([]);
  const [selectedUserId, setSelectedUserId] = useState('');
  const [workspace, setWorkspace] = useState(null);
  const [loadingUsers, setLoadingUsers] = useState(false);
  const [loadingWorkspace, setLoadingWorkspace] = useState(false);
  const [launcherMessage, setLauncherMessage] = useState('');

  useEffect(() => {
    const refreshRoute = () => setRouteActive(isTimesheetRoute());
    window.addEventListener('hashchange', refreshRoute);
    return () => window.removeEventListener('hashchange', refreshRoute);
  }, []);

  const loadUsers = useCallback(async () => {
    if (!routeActive) return;
    setLoadingUsers(true);
    try {
      const payload = await module001Api(
        `/api/runtime/timesheet/steward/v2/users?weekStart=${encodeURIComponent(weekStart)}&search=`,
        { requiredCollections: ['users'] }
      );
      const nextUsers = Array.isArray(payload?.users) ? payload.users : [];
      setAuthorized(true);
      setUsers(nextUsers);
      setSelectedUserId((current) => nextUsers.some((user) => user.userId === current) ? current : '');
    } catch (requestError) {
      if (requestError?.status === 403) {
        setAuthorized(false);
        setUsers([]);
        setSelectedUserId('');
      } else {
        setAuthorized(true);
        setLauncherMessage(requestError?.message || 'Move Time users could not be loaded.');
      }
    } finally {
      setLoadingUsers(false);
    }
  }, [routeActive, weekStart]);

  const loadWorkspace = useCallback(async () => {
    if (!selectedUserId || authorized !== true) {
      setWorkspace(null);
      return;
    }
    setLoadingWorkspace(true);
    try {
      const payload = await module001Api(
        `/api/runtime/timesheet/steward/v2/users/${encodeURIComponent(selectedUserId)}/workspace?weekStart=${encodeURIComponent(weekStart)}`,
        { requiredCollections: ['entries', 'moveTargets', 'nonProjectCategories', 'availableProjects'] }
      );
      setWorkspace(payload);
      setLauncherMessage('');
    } catch (requestError) {
      setWorkspace(null);
      setLauncherMessage(requestError?.message || 'The selected Move Time workspace could not be loaded.');
    } finally {
      setLoadingWorkspace(false);
    }
  }, [authorized, selectedUserId, weekStart]);

  useEffect(() => {
    void loadUsers();
  }, [loadUsers]);

  useEffect(() => {
    void loadWorkspace();
  }, [loadWorkspace]);

  useEffect(() => {
    const refresh = () => {
      void loadUsers();
      void loadWorkspace();
    };
    window.addEventListener('projectpulse:permissions-changed', refresh);
    window.addEventListener('projectpulse:ptc-non-project-task-created', refresh);
    return () => {
      window.removeEventListener('projectpulse:permissions-changed', refresh);
      window.removeEventListener('projectpulse:ptc-non-project-task-created', refresh);
    };
  }, [loadUsers, loadWorkspace]);

  if (!routeActive || authorized !== true) return null;

  return (
    <>
      <div className="ptc-guided-launcher" data-projectpulse-ptc-guided-move="true">
        <button type="button" onClick={() => setOpen(true)}>
          <span>Move Time</span>
          <small>Guided PTC correction</small>
        </button>
        {launcherMessage ? <p role="status">{launcherMessage}</p> : null}
      </div>
      {open ? createPortal(
        <div className="ptc-guided-overlay" role="presentation">
          <MoveWizard
            users={users}
            loadingUsers={loadingUsers}
            weekStart={weekStart}
            setWeekStart={setWeekStart}
            selectedUserId={selectedUserId}
            setSelectedUserId={setSelectedUserId}
            workspace={workspace}
            loadingWorkspace={loadingWorkspace}
            loadWorkspace={loadWorkspace}
            onClose={() => setOpen(false)}
          />
        </div>,
        document.body
      ) : null}
    </>
  );
}