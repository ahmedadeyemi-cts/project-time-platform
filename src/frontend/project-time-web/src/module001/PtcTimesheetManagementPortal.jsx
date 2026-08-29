import { useCallback, useEffect, useState } from 'react';
import { createPortal } from 'react-dom';
import { authoritativeApi } from '../projectpulse-authoritative-api.js';
import './ptc-timesheet-management.css';
import './module001-runtime-v2.css';

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

function shiftWeek(weekStart, offset) {
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
      return;
    }
    setLoadingWorkspace(true);
    try {
      const payload = await module001Api(
        `/api/runtime/timesheet/steward/v2/users/${encodeURIComponent(selectedUserId)}/workspace?weekStart=${encodeURIComponent(weekStart)}`,
        { requiredCollections: ['entries'] }
      );
      setDetail(payload);
      setError('');
    } catch (requestError) {
      setDetail(null);
      setError(requestError?.message || 'The selected user’s time could not be loaded.');
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
      'The week is now draft. The user must review and submit it again after ordinary time corrections are complete.'
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
          <h2>Manage ordinary time for other users</h2>
          <p>
            Select an eligible delivery user and week to review ordinary time, return a week to draft when necessary,
            correct draft entry details, or remove an incorrect draft entry with an immutable business reason.
          </p>
        </div>
        <div className="ptc-no-submit">
          <strong>No submission on behalf</strong>
          <span>The selected user reviews and submits a corrected draft week. This workspace never submits for them.</span>
        </div>
      </header>

      <div className="ptc-workflow-steps">
        <span>1 · Select eligible user</span>
        <span>2 · Review the selected week</span>
        <span>3 · Correct ordinary draft-entry details</span>
        <span>4 · User reviews and submits</span>
      </div>

      {error ? <p className="ptc-alert error" role="alert">{error}</p> : null}
      {message ? <p className="ptc-alert success" role="status">{message}</p> : null}

      <section className="ptc-toolbar">
        <div className="ptc-week-nav">
          <button type="button" onClick={() => setWeekStart(shiftWeek(weekStart, -1))}>Previous week</button>
          <strong>Week of {displayDate(weekStart)}</strong>
          <button type="button" onClick={() => setWeekStart(sundayFor(new Date()))}>Current week</button>
          <button type="button" onClick={() => setWeekStart(shiftWeek(weekStart, 1))}>Next week</button>
        </div>
        <label>
          <span>Find user</span>
          <input value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Name or email" />
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
            <span>Draft correction workflow</span>
            <button
              type="button"
              disabled={Boolean(busy) || !detail?.timesheet || detail?.timesheet?.status === 'draft'}
              onClick={unsubmitWeek}
            >
              {busy === 'unsubmit' ? 'Returning…' : 'Return week to draft'}
            </button>
            <small>Use only when ordinary time-entry values require correction.</small>
          </article>
        </section>

        <section className="ptc-entry-section">
          <header>
            <div>
              <p className="eyebrow">Selected user’s time entries</p>
              <h3>{loadingWorkspace ? 'Loading entries…' : `${entries.length} entry or entries`}</h3>
              <p>Draft entries can be corrected or removed. Submitted and approved entries remain read-only in Module 001.</p>
            </div>
          </header>

          <div className="ptc-entry-table-wrap">
            <table className="ptc-entry-table">
              <thead>
                <tr>
                  <th>Date</th>
                  <th>Activity</th>
                  <th>Hours</th>
                  <th>Status</th>
                  <th>Actions</th>
                </tr>
              </thead>
              <tbody>
                {entries.map((entry) => (
                  <tr key={entry.timeEntryId}>
                    <td>{displayDate(entry.workDate)}</td>
                    <td><strong>{activityLabel(entry)}</strong><small>{entry.description || 'No description'}</small></td>
                    <td>{Number(entry.hours || 0).toFixed(2)}</td>
                    <td>{statusLabel(entry.status)}</td>
                    <td>
                      <div className="ptc-entry-actions">
                        <button
                          type="button"
                          disabled={Boolean(busy) || !editableStatus(entry.status)}
                          onClick={() => setEditingEntry(entry)}
                        >
                          Edit
                        </button>
                        <button
                          type="button"
                          disabled={Boolean(busy) || !editableStatus(entry.status)}
                          onClick={() => removeEntry(entry)}
                        >
                          Remove
                        </button>
                      </div>
                    </td>
                  </tr>
                ))}
                {!loadingWorkspace && entries.length === 0 ? (
                  <tr><td colSpan="5">No time entries exist for the selected user and week.</td></tr>
                ) : null}
              </tbody>
            </table>
          </div>
        </section>
      </> : <p className="ptc-empty-state">Select an eligible user to review ordinary time.</p>}

      {editingEntry ? (
        <EditEntryDialog
          entry={editingEntry}
          busy={Boolean(busy)}
          onClose={() => setEditingEntry(null)}
          onSave={saveEntry}
        />
      ) : null}
    </section>,
    host
  );
}
