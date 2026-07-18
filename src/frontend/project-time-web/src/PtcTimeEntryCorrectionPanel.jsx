import { useEffect, useMemo, useState } from 'react';
import './approval-center.css';

function getProjectPulseAuthHeaders() {
  try {
    const raw = window.localStorage.getItem('projectPulseAuthSession');
    if (!raw) return {};

    const session = JSON.parse(raw);
    return session?.sessionToken
      ? { 'X-ProjectPulse-Session': session.sessionToken }
      : {};
  } catch {
    return {};
  }
}

async function readApiErrorMessage(response, path) {
  const raw = await response.text();

  if (!raw) return `${path} returned HTTP ${response.status}`;

  try {
    const parsed = JSON.parse(raw);
    return `${path} returned HTTP ${response.status}: ${parsed.message || parsed.detail || parsed.status || raw}`;
  } catch {
    return `${path} returned HTTP ${response.status}: ${raw}`;
  }
}

async function fetchJson(path) {
  const response = await fetch(path, {
    headers: getProjectPulseAuthHeaders()
  });

  if (!response.ok) {
    throw new Error(await readApiErrorMessage(response, path));
  }

  return response.json();
}

async function postJson(path, payload) {
  const response = await fetch(path, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      ...getProjectPulseAuthHeaders()
    },
    body: JSON.stringify(payload)
  });

  if (!response.ok) {
    throw new Error(await readApiErrorMessage(response, path));
  }

  return response.json();
}

function toIsoDate(date) {
  return date.toISOString().slice(0, 10);
}

function getSundayForDate(date = new Date()) {
  const current = new Date(Date.UTC(date.getFullYear(), date.getMonth(), date.getDate()));
  current.setUTCDate(current.getUTCDate() - current.getUTCDay());
  return toIsoDate(current);
}

function shiftDate(isoDate, days) {
  const date = new Date(`${isoDate}T00:00:00Z`);
  date.setUTCDate(date.getUTCDate() + days);
  return toIsoDate(date);
}

function formatNumber(value) {
  const parsed = Number(value ?? 0);
  return Number.isFinite(parsed) ? parsed.toFixed(2) : '0.00';
}

function titleCase(value) {
  return String(value ?? '')
    .replaceAll('_', ' ')
    .replace(/\b\w/g, (character) => character.toUpperCase());
}

function getEntryKey(entry) {
  return entry.timeEntryId;
}

function defaultForm() {
  return {
    operation: 'move',
    targetTaskId: '',
    splitHours: '',
    reason: ''
  };
}

export default function PtcTimeEntryCorrectionPanel() {
  const [weekStart, setWeekStart] = useState(getSundayForDate());
  const [state, setState] = useState({
    loading: true,
    entries: [],
    tasks: [],
    error: null
  });
  const [searchText, setSearchText] = useState('');
  const [showProtected, setShowProtected] = useState(false);
  const [expandedKey, setExpandedKey] = useState('');
  const [forms, setForms] = useState({});
  const [busyKey, setBusyKey] = useState('');
  const [statusMessage, setStatusMessage] = useState('Ready.');

  const weekEnd = shiftDate(weekStart, 6);

  async function loadCorrections() {
    setState((current) => ({ ...current, loading: true, error: null }));

    try {
      const result = await fetchJson(
        `/api/workflow/ptc-time-entry-corrections?weekStart=${weekStart}&weekEnd=${weekEnd}`
      );

      setState({
        loading: false,
        entries: Array.isArray(result.entries) ? result.entries : [],
        tasks: Array.isArray(result.tasks) ? result.tasks : [],
        error: null
      });
    } catch (error) {
      setState({
        loading: false,
        entries: [],
        tasks: [],
        error: error instanceof Error
          ? error.message
          : 'Unable to load PTC time-entry corrections.'
      });
    }
  }

  useEffect(() => {
    void loadCorrections();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [weekStart]);

  const visibleEntries = useMemo(() => {
    const normalizedSearch = searchText.trim().toLowerCase();

    return state.entries.filter((entry) => {
      if (!showProtected && !entry.canCorrect) return false;

      if (!normalizedSearch) return true;

      return [
        entry.employeeName,
        entry.employeeEmail,
        entry.workDate,
        entry.workflowStatus,
        entry.projectCode,
        entry.projectName,
        entry.taskCode,
        entry.taskName,
        entry.description
      ]
        .filter(Boolean)
        .some((value) => String(value).toLowerCase().includes(normalizedSearch));
    });
  }, [searchText, showProtected, state.entries]);

  function getForm(entry) {
    return forms[getEntryKey(entry)] ?? defaultForm();
  }

  function updateForm(entry, patch) {
    const key = getEntryKey(entry);

    setForms((current) => ({
      ...current,
      [key]: {
        ...defaultForm(),
        ...(current[key] ?? {}),
        ...patch
      }
    }));
  }

  function toggleEntry(entry) {
    const key = getEntryKey(entry);

    setExpandedKey((current) => current === key ? '' : key);
    setForms((current) => ({
      ...current,
      [key]: current[key] ?? defaultForm()
    }));
    setStatusMessage('Ready.');
  }

  async function submitCorrection(entry) {
    const key = getEntryKey(entry);
    const form = getForm(entry);
    const target = state.tasks.find((task) => task.taskId === form.targetTaskId) ?? null;
    const splitHours = Number.parseFloat(form.splitHours);

    if (!target) {
      setStatusMessage('Select an active destination project task.');
      return;
    }

    if (target.projectId === entry.projectId && target.taskId === entry.taskId) {
      setStatusMessage('Choose a destination task that differs from the source task.');
      return;
    }

    if (!form.reason.trim()) {
      setStatusMessage('A correction reason is required.');
      return;
    }

    if (form.operation === 'split' && (!Number.isFinite(splitHours) || splitHours <= 0)) {
      setStatusMessage('Enter positive split-copy hours.');
      return;
    }

    if (form.operation === 'split' && splitHours > Number(entry.hours)) {
      setStatusMessage('Split-copy hours cannot exceed the source hours.');
      return;
    }

    setBusyKey(key);
    setStatusMessage(
      form.operation === 'move'
        ? 'Moving the complete entry without changing total hours...'
        : 'Splitting hours without changing the total recorded hours...'
    );

    try {
      const result = await postJson(
        '/api/workflow/ptc-time-entry-corrections/action',
        {
          timeEntryId: entry.timeEntryId,
          targetProjectId: target.projectId,
          targetTaskId: target.taskId,
          operation: form.operation,
          splitHours: form.operation === 'split' ? splitHours : null,
          reason: form.reason.trim()
        }
      );

      setStatusMessage(result.message ?? 'Time-entry coding correction completed.');
      setExpandedKey('');
      setForms((current) => {
        const next = { ...current };
        delete next[key];
        return next;
      });
      await loadCorrections();
    } catch (error) {
      setStatusMessage(
        error instanceof Error
          ? error.message
          : 'Unable to complete the time-entry correction.'
      );
    } finally {
      setBusyKey('');
    }
  }

  const correctableCount = state.entries.filter((entry) => entry.canCorrect).length;
  const protectedCount = state.entries.length - correctableCount;

  return (
    <section id="ptc-time-entry-corrections" className="ptc-correction-shell">
      <div className="manager-approval-header">
        <div>
          <p className="eyebrow">PTC Corrections</p>
          <h2>Move or split project-task time safely</h2>
          <p>
            Project Team Coordinators and administrators can correct project-task coding while preserving the engineer&apos;s total recorded hours.
          </p>
        </div>

        <div className="manager-toolbar">
          <button type="button" onClick={() => setWeekStart(shiftDate(weekStart, -7))}>← Previous</button>
          <button type="button" onClick={() => setWeekStart(getSundayForDate())}>Current week</button>
          <button type="button" onClick={() => setWeekStart(shiftDate(weekStart, 7))}>Next →</button>
          <button type="button" onClick={loadCorrections} disabled={state.loading || Boolean(busyKey)}>Refresh</button>
        </div>
      </div>

      <div className="manager-status-row">
        <span>Week starts: <strong>{weekStart}</strong></span>
        <span>Week ends: <strong>{weekEnd}</strong></span>
        <span>Correctable: <strong>{correctableCount}</strong></span>
        <span>Protected: <strong>{protectedCount}</strong></span>
        <span>Status: <strong>{statusMessage}</strong></span>
      </div>

      <div className="ptc-correction-safeguard">
        <strong>Total hours never increase.</strong>
        <span>
          Move transfers all hours. Split-copy transfers part of the hours and reduces the source by the same amount. Corrections to approved time return the day to manager review.
        </span>
      </div>

      <div className="manager-filter-row">
        <label>
          Search entries
          <input
            type="search"
            value={searchText}
            placeholder="Engineer, date, status, project, task, or description"
            onChange={(event) => setSearchText(event.target.value)}
          />
        </label>

        <label className="ptc-protected-toggle">
          <input
            type="checkbox"
            checked={showProtected}
            onChange={(event) => setShowProtected(event.target.checked)}
          />
          Show protected entries
        </label>
      </div>

      {state.error ? <div className="manager-empty-state error">{state.error}</div> : null}
      {state.loading ? <div className="manager-empty-state">Loading project-task time entries...</div> : null}

      {!state.loading && !state.error && visibleEntries.length === 0 ? (
        <div className="manager-empty-state">
          No project-task time entries match this week and the selected filters.
        </div>
      ) : null}

      <div className="ptc-correction-list">
        {visibleEntries.map((entry) => {
          const key = getEntryKey(entry);
          const expanded = expandedKey === key;
          const form = getForm(entry);
          const isBusy = busyKey === key;
          const selectedTarget = state.tasks.find((task) => task.taskId === form.targetTaskId) ?? null;

          return (
            <article className={`ptc-correction-card ${entry.canCorrect ? '' : 'protected'}`} key={key}>
              <div className="ptc-correction-card-header">
                <div>
                  <span className={`badge ${entry.canCorrect ? 'active' : 'warning'}`}>
                    {entry.canCorrect ? 'Correction available' : 'Protected'}
                  </span>
                  <h3>{entry.employeeName}</h3>
                  <p>{entry.employeeEmail}</p>
                  <small>{entry.workDate} • {titleCase(entry.workflowStatus)}</small>
                </div>

                <div className="ptc-correction-metrics">
                  <span>
                    <strong>{formatNumber(entry.hours)}</strong>
                    source hours
                  </span>
                  <span>
                    <strong>{entry.projectCode}</strong>
                    {entry.projectName}
                  </span>
                  <span>
                    <strong>{entry.taskCode}</strong>
                    {entry.taskName}
                  </span>
                </div>
              </div>

              {entry.description ? (
                <div className="pm-manager-comment">
                  <strong>Entry description</strong>
                  <p>{entry.description}</p>
                </div>
              ) : null}

              {!entry.canCorrect ? (
                <div className="ptc-correction-blocked">
                  {entry.blockedReason || 'This entry is protected from correction.'}
                </div>
              ) : (
                <div className="manager-row-actions">
                  <button
                    type="button"
                    onClick={() => toggleEntry(entry)}
                    disabled={Boolean(busyKey)}
                  >
                    {expanded ? 'Close correction' : 'Correct project/task'}
                  </button>
                </div>
              )}

              {expanded && entry.canCorrect ? (
                <div className="ptc-correction-form">
                  <div className="ptc-correction-form-grid">
                    <label>
                      Operation
                      <select
                        value={form.operation}
                        onChange={(event) => updateForm(entry, {
                          operation: event.target.value,
                          splitHours: event.target.value === 'move' ? '' : form.splitHours
                        })}
                      >
                        <option value="move">Move all hours</option>
                        <option value="split">Split-copy hours</option>
                      </select>
                    </label>

                    <label>
                      Destination project task
                      <select
                        value={form.targetTaskId}
                        onChange={(event) => updateForm(entry, {
                          targetTaskId: event.target.value
                        })}
                      >
                        <option value="">Select an active task</option>
                        {state.tasks.map((task) => {
                          const sameSource =
                            task.projectId === entry.projectId
                            && task.taskId === entry.taskId;

                          return (
                            <option
                              key={task.taskId}
                              value={task.taskId}
                              disabled={sameSource}
                            >
                              {task.projectCode} • {task.projectName} — {task.taskCode} • {task.taskName}
                            </option>
                          );
                        })}
                      </select>
                    </label>

                    {form.operation === 'split' ? (
                      <label>
                        Hours to split-copy
                        <input
                          type="number"
                          min="0.01"
                          max={entry.hours}
                          step="0.01"
                          value={form.splitHours}
                          onChange={(event) => updateForm(entry, {
                            splitHours: event.target.value
                          })}
                          placeholder={`Up to ${formatNumber(entry.hours)}`}
                        />
                      </label>
                    ) : null}
                  </div>

                  {selectedTarget ? (
                    <div className="ptc-correction-preview">
                      <strong>Destination:</strong>
                      <span>
                        {selectedTarget.projectCode} • {selectedTarget.projectName} — {selectedTarget.taskCode} • {selectedTarget.taskName}
                      </span>
                    </div>
                  ) : null}

                  <label>
                    Required correction reason
                    <textarea
                      value={form.reason}
                      placeholder="Explain why this time is being moved or split between project tasks."
                      onChange={(event) => updateForm(entry, {
                        reason: event.target.value
                      })}
                    />
                  </label>

                  <div className="manager-row-actions">
                    <button
                      type="button"
                      className="approve"
                      onClick={() => submitCorrection(entry)}
                      disabled={isBusy}
                    >
                      {isBusy
                        ? 'Applying correction...'
                        : form.operation === 'move'
                          ? 'Confirm move'
                          : 'Confirm split-copy'}
                    </button>
                    <button
                      type="button"
                      onClick={() => setExpandedKey('')}
                      disabled={isBusy}
                    >
                      Cancel
                    </button>
                  </div>
                </div>
              ) : null}
            </article>
          );
        })}
      </div>
    </section>
  );
}
