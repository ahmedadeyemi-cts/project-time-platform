import { useCallback, useEffect, useMemo, useState } from 'react';
import { createPortal } from 'react-dom';
import { authoritativeApi } from '../projectpulse-authoritative-api.js';
import '../module001/ptc-guided-move.css';

const DESTINATION_GROUPS = Object.freeze([
  'Requests / Service Requests',
  'Project Tasks',
  'Non-Project Time'
]);

function sharedTimeApi(path, options = {}) {
  return authoritativeApi(path, { ...options, moduleNumber: '001' });
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
    return { destinationType: 'assignment', assignmentId: first, projectId: null, taskId: null, nonProjectTimeCategoryId: null };
  }
  if (kind === 'project-task' && first && second) {
    return { destinationType: 'project_task', assignmentId: null, projectId: first, taskId: second, nonProjectTimeCategoryId: null };
  }
  if (kind === 'category' && first) {
    return { destinationType: 'non_project', assignmentId: null, projectId: null, taskId: null, nonProjectTimeCategoryId: first };
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
    <div style={{
      position: 'fixed', inset: 0, zIndex: 2147482000, overflow: 'auto',
      background: 'var(--background, #f5f7fb)', padding: '28px'
    }} data-module="001B">
      {children}
    </div>,
    document.body
  );
}

function NoAccess() {
  return (
    <ModuleShell>
      <main style={{ maxWidth: 900, margin: '8vh auto', background: 'var(--surface, white)', borderRadius: 18, padding: 32 }}>
        <p className="eyebrow">MODULE 001B · TIME REALLOCATION &amp; CORRECTIONS</p>
        <h1>No Access</h1>
        <p>This module is restricted to Project Team Coordinators and Super Administrators.</p>
        <p>Managers, Project Managers, Engineers, Engineering Leads, Administrators, and all other roles cannot access or execute time reallocation.</p>
        <button type="button" onClick={() => { window.location.hash = '#dashboard'; }}>Return to dashboard</button>
      </main>
    </ModuleShell>
  );
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
      const payload = await sharedTimeApi(
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
      const payload = await sharedTimeApi(
        `/api/runtime/timesheet/steward/v2/users/${encodeURIComponent(selectedUserId)}/workspace?weekStart=${encodeURIComponent(weekStart)}`,
        { requiredCollections: ['entries', 'moveTargets'] }
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
  const selectedEntry = entries.find((entry) => entry.timeEntryId === entryId) || null;
  const selectedTarget = targets.find((target) => destinationValue(target) === destination) || null;
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

  const canMove = Boolean(selectedEntry && destinationPayload(destination) && reason.trim().length >= 5 && !busy);

  async function reallocate() {
    if (!canMove) return;
    const payload = destinationPayload(destination);
    const preservedStatus = selectedEntry.status;
    setBusy(true);
    setError('');
    setMessage('');
    try {
      const result = await sharedTimeApi(
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
        `Allocation moved successfully. Status stayed ${statusLabel(result?.currentStatus || preservedStatus)}. `
        + 'No worker resubmission, Manager approval, or Project Manager approval is required.'
      );
      setEntryId('');
      setDestination('');
      setReason('');
      await loadWorkspace();
      window.dispatchEvent(new CustomEvent('projectpulse:ptc-time-reallocated', { detail: result }));
    } catch (requestError) {
      setError(requestError?.message || 'The time allocation could not be moved. No submission state was changed.');
      await loadWorkspace();
    } finally {
      setBusy(false);
    }
  }

  if (!routeActive) return null;
  if (!allowed) return <NoAccess />;

  return (
    <ModuleShell>
      <main style={{ maxWidth: 1320, margin: '0 auto' }}>
        <header style={{ display: 'flex', justifyContent: 'space-between', gap: 24, alignItems: 'flex-start', marginBottom: 22 }}>
          <div>
            <p className="eyebrow">MODULE 001B · PROJECT TEAM COORDINATOR</p>
            <h1 style={{ marginBottom: 8 }}>Time Reallocation &amp; Corrections</h1>
            <p style={{ maxWidth: 850 }}>
              Move an existing time entry to the correct project task, service request task, or non-project activity without reopening the timesheet.
            </p>
          </div>
          <button type="button" onClick={() => { window.location.hash = '#dashboard'; }}>Close</button>
        </header>

        <section className="ptc-guided-alert success" style={{ marginBottom: 18 }}>
          <strong>Administrative correction:</strong> Submitted and approved time stays in its current status. No unsubmit, Draft transition, worker resubmission, Manager approval, or Project Manager approval is triggered.
        </section>
        {error ? <p className="ptc-guided-alert error" role="alert">{error}</p> : null}
        {message ? <p className="ptc-guided-alert success" role="status">{message}</p> : null}

        <article className="ptc-guided-dialog" style={{ position: 'static', width: '100%', maxWidth: 'none', margin: 0 }}>
          <section className="ptc-guided-section">
            <header><strong>1. Find the person and week</strong></header>
            <div className="ptc-guided-form-grid three">
              <label>
                <span>Eligible user</span>
                <select value={selectedUserId} disabled={loading || busy} onChange={(event) => {
                  setSelectedUserId(event.target.value); setEntryId(''); setDestination(''); setMessage('');
                }}>
                  <option value="">{loading ? 'Loading users…' : 'Select user'}</option>
                  {users.map((user) => <option key={user.userId} value={user.userId}>{user.displayName} · {user.email}</option>)}
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
          </section>

          <section className="ptc-guided-section">
            <header><strong>2. Select the time entry</strong><span>{loading ? 'Loading…' : `${entries.length} entries`}</span></header>
            <div className="ptc-guided-entry-list">
              {entries.map((entry) => (
                <label key={entry.timeEntryId} className={entryId === entry.timeEntryId ? 'selected' : ''}>
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

          <section className="ptc-guided-section">
            <header><strong>3. Choose the correct destination</strong><span>Project tasks · Service requests · Non-project time</span></header>
            <label className="ptc-guided-destination-search">
              <span>Search destinations</span>
              <input type="search" value={destinationSearch} disabled={!entryId || busy}
                onChange={(event) => setDestinationSearch(event.target.value)}
                placeholder="Project, task, request number, or non-project activity" />
            </label>
            <div className="ptc-guided-destination-groups">
              {groupedTargets.map(({ group, targets: groupItems }) => (
                <section key={group}>
                  <header><strong>{group}</strong><span>{groupItems.length}</span></header>
                  <div>
                    {groupItems.slice(0, 80).map((target) => {
                      const value = destinationValue(target);
                      return <button type="button" key={value} className={destination === value ? 'selected' : ''}
                        disabled={!entryId || busy} onClick={() => setDestination(value)}>
                        <strong>{destinationLabel(target)}</strong>
                        <small>{target.requiresAssignment ? 'Assignment will be created automatically' : 'Available for immediate reallocation'}</small>
                      </button>;
                    })}
                    {groupItems.length === 0 ? <p>No matching destinations.</p> : null}
                  </div>
                </section>
              ))}
            </div>
          </section>

          <section className="ptc-guided-section review">
            <header><strong>4. Review and reallocate</strong></header>
            <div className="ptc-guided-review-grid">
              <article><span>From</span><strong>{selectedEntry ? activityLabel(selectedEntry) : 'Select an entry'}</strong><small>{selectedEntry ? `${selectedEntry.hours} hour(s) · ${statusLabel(selectedEntry.status)}` : '—'}</small></article>
              <article><span>To</span><strong>{selectedTarget ? destinationLabel(selectedTarget) : 'Select a destination'}</strong><small>{selectedTarget ? destinationGroup(selectedTarget) : '—'}</small></article>
            </div>
            <label>
              <span>Required correction reason</span>
              <textarea value={reason} disabled={busy} onChange={(event) => setReason(event.target.value)} placeholder="Why is this allocation being corrected?" />
              <small>The reason is retained in the time-management audit trail.</small>
            </label>
            {selectedEntry ? (
              <p className="ptc-guided-alert success">
                <strong>Status protection:</strong> {statusLabel(selectedEntry.status)} will remain {statusLabel(selectedEntry.status)} after this move.
              </p>
            ) : null}
          </section>

          <footer className="ptc-guided-footer">
            <button type="button" disabled={busy} onClick={() => { window.location.hash = '#dashboard'; }}>Cancel</button>
            <button type="button" className="primary" disabled={!canMove} onClick={reallocate}>
              {busy ? 'Reallocating…' : 'Reallocate time'}
            </button>
          </footer>
        </article>
      </main>
    </ModuleShell>
  );
}
