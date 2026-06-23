import { useEffect, useMemo, useState } from 'react';
import usSignalLogoUrl from '../brand/ussignal.png';
import './timesheet.css';

const workflowCards = [
  {
    title: 'Time Entry',
    description: 'Engineers enter weekly project-task, non-project, normal, and afterhours time before submission.',
    status: 'In progress'
  },
  {
    title: 'Manager Approval',
    description: 'Managers review submitted regular and OT hours by resource, task, and date.',
    status: 'Next phase'
  },
  {
    title: 'Project Approval',
    description: 'Project managers validate project and task allocation accuracy before accounting review.',
    status: 'Next phase'
  },
  {
    title: 'Accounting Reconciliation',
    description: 'Accounting reviews approved time and reconciles the period before lock.',
    status: 'Planned'
  },
  {
    title: 'Utilization',
    description: 'Monthly and quarterly summaries compare billable, PTO, and approved eligible time against target.',
    status: 'Policy loaded'
  },
  {
    title: 'Audit Trail',
    description: 'Role, approval, decline, reconciliation, and administrative actions are logged.',
    status: 'Planned'
  }
];

const timeTypes = [
  { key: 'normal', label: 'Normal' },
  { key: 'afterhours', label: 'Afterhours' }
];

const activitySourceOptions = [
  {
    key: 'nonProject',
    label: 'Non-project time',
    emptyTitle: 'No non-project time available.',
    emptyDescription: 'Non-project categories will appear here once they are loaded from the API.'
  },
  {
    key: 'openTasks',
    label: 'Open tasks',
    emptyTitle: 'No open tasks available.',
    emptyDescription: 'Assigned open project tasks will appear here after project-task assignments are connected.'
  },
  {
    key: 'regularTasks',
    label: 'Regular tasks',
    emptyTitle: 'No regular tasks available.',
    emptyDescription: 'Recurring or regular project tasks will appear here after the assignment workflow is connected.'
  },
  {
    key: 'requests',
    label: 'Requests / Service Requests',
    emptyTitle: 'No requests available.',
    emptyDescription: 'Service request activities will appear here after the request workflow is connected.'
  }
];

async function fetchJson(path) {
  const response = await fetch(path);

  if (!response.ok) {
    throw new Error(`${path} returned HTTP ${response.status}`);
  }

  return response.json();
}

async function postJson(path, payload) {
  const response = await fetch(path, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json'
    },
    body: JSON.stringify(payload)
  });

  if (!response.ok) {
    let details = '';
    try {
      const errorBody = await response.json();
      details = errorBody.message || errorBody.detail || JSON.stringify(errorBody);
    } catch {
      details = await response.text();
    }

    throw new Error(`${path} returned HTTP ${response.status}${details ? `: ${details}` : ''}`);
  }

  return response.json();
}

function getInitialTheme() {
  const savedTheme = window.localStorage.getItem('ptp-theme');
  if (savedTheme === 'dark' || savedTheme === 'light') return savedTheme;
  return window.matchMedia?.('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
}

function toIsoDate(date) {
  return date.toISOString().slice(0, 10);
}

function getSundayIso(date = new Date()) {
  const normalized = new Date(Date.UTC(date.getFullYear(), date.getMonth(), date.getDate()));
  normalized.setUTCDate(normalized.getUTCDate() - normalized.getUTCDay());
  return toIsoDate(normalized);
}

function addDaysIso(isoDate, numberOfDays) {
  const [year, month, day] = isoDate.split('-').map(Number);
  const date = new Date(Date.UTC(year, month - 1, day));
  date.setUTCDate(date.getUTCDate() + numberOfDays);
  return toIsoDate(date);
}

function getEntryKey(rowId, date, type) {
  return `${rowId}|${date}|${type}`;
}

function formatNumber(value) {
  return Number(value || 0).toFixed(2);
}

function formatHoursValue(value) {
  const parsed = Number.parseFloat(value);
  return Number.isFinite(parsed) ? parsed.toFixed(2) : '0.00';
}

function categoryToRow(category) {
  return {
    id: `non-project-${category.code}`,
    type: 'nonProject',
    state: 'Draft',
    activity: category.name,
    projectDescription: 'Non-project time',
    categoryCode: category.code,
    utilizationBucket: category.utilizationBucket,
    requiresApproval: category.requiresApproval
  };
}

function statusToLabel(status, totalHours = 0) {
  if (status === 'submitted') return `Submitted for manager approval (${formatNumber(totalHours)} hours).`;
  if (status === 'manager_declined') return 'Returned by manager for correction.';
  if (status === 'manager_approved') return 'Manager approved.';
  if (status === 'pm_approved') return 'Project manager approved.';
  if (status === 'accounting_ready') return 'Ready for accounting reconciliation.';
  if (status === 'reconciled') return 'Reconciled.';
  if (status === 'locked') return 'Locked.';
  return 'Draft';
}

function SignalLogo() {
  return (
    <div className="brand-lockup" aria-label="US Signal Project Pulse">
      <img className="brand-logo-image" src={usSignalLogoUrl} alt="US Signal" />
      <div>
        <strong>Project Pulse</strong>
        <small>Time • Approval • Utilization</small>
      </div>
    </div>
  );
}

function DataState({ loading, error, children }) {
  if (loading) return <span className="muted">Loading...</span>;
  if (error) return <span className="error-text">{error}</span>;
  return children;
}

export default function App() {
  const [theme, setTheme] = useState(getInitialTheme);
  const [selectedWeekStart, setSelectedWeekStart] = useState(getSundayIso);
  const [apiHealth, setApiHealth] = useState({ loading: true, data: null, error: null });
  const [dbHealth, setDbHealth] = useState({ loading: true, data: null, error: null });
  const [schema, setSchema] = useState({ loading: true, data: null, error: null });
  const [timesheet, setTimesheet] = useState({ loading: true, data: null, error: null });
  const [locationGroups, setLocationGroups] = useState({ loading: true, data: null, error: null });
  const [locations, setLocations] = useState({ loading: true, data: null, error: null });
  const [utilizationPolicies, setUtilizationPolicies] = useState({ loading: true, data: null, error: null });
  const [utilizationTargets, setUtilizationTargets] = useState({ loading: true, data: null, error: null });
  const [activeRows, setActiveRows] = useState([]);
  const [entries, setEntries] = useState({});
  const [selectedCell, setSelectedCell] = useState(null);
  const [submissionStatus, setSubmissionStatus] = useState('Draft');
  const [saveStatus, setSaveStatus] = useState('Not saved yet');
  const [isSaving, setIsSaving] = useState(false);
  const [activitySource, setActivitySource] = useState('nonProject');

  useEffect(() => {
    document.documentElement.dataset.theme = theme;
    window.localStorage.setItem('ptp-theme', theme);
  }, [theme]);

  useEffect(() => {
    let cancelled = false;

    async function loadStatus() {
      setTimesheet({ loading: true, data: null, error: null });

      try {
        const [healthResult, dbResult, schemaResult, timesheetResult, groupResult, locationsResult, policyResult, targetsResult] = await Promise.all([
          fetchJson('/health'),
          fetchJson('/api/db-health'),
          fetchJson('/api/schema/tables'),
          fetchJson(`/api/timesheets/week?weekStart=${selectedWeekStart}`),
          fetchJson('/api/work-location-groups'),
          fetchJson('/api/work-locations'),
          fetchJson('/api/utilization/policies'),
          fetchJson('/api/utilization/targets')
        ]);

        if (!cancelled) {
          setApiHealth({ loading: false, data: healthResult, error: null });
          setDbHealth({ loading: false, data: dbResult, error: null });
          setSchema({ loading: false, data: schemaResult, error: null });
          setTimesheet({ loading: false, data: timesheetResult, error: null });
          setLocationGroups({ loading: false, data: groupResult, error: null });
          setLocations({ loading: false, data: locationsResult, error: null });
          setUtilizationPolicies({ loading: false, data: policyResult, error: null });
          setUtilizationTargets({ loading: false, data: targetsResult, error: null });
        }
      } catch (error) {
        if (!cancelled) {
          const message = error instanceof Error ? error.message : 'Unknown error';
          setApiHealth((current) => ({ ...current, loading: false, error: message }));
          setDbHealth((current) => ({ ...current, loading: false, error: message }));
          setSchema((current) => ({ ...current, loading: false, error: message }));
          setTimesheet((current) => ({ ...current, loading: false, error: message }));
          setLocationGroups((current) => ({ ...current, loading: false, error: message }));
          setLocations((current) => ({ ...current, loading: false, error: message }));
          setUtilizationPolicies((current) => ({ ...current, loading: false, error: message }));
          setUtilizationTargets((current) => ({ ...current, loading: false, error: message }));
        }
      }
    }

    loadStatus();

    return () => {
      cancelled = true;
    };
  }, [selectedWeekStart]);

  useEffect(() => {
    const categories = timesheet.data?.nonProjectCategories ?? [];
    if (categories.length === 0) return;

    const defaults = categories.filter((category) => ['ADMINISTRATIVE', 'PEER_SUPPORT'].includes(category.code));
    const fallback = categories.slice(0, 2);
    const savedEntries = timesheet.data?.entries ?? [];
    const savedCategoryCodes = new Set(savedEntries.map((entry) => entry.categoryCode).filter(Boolean));
    const savedCategories = categories.filter((category) => savedCategoryCodes.has(category.code));
    const rowMap = new Map();

    [...(defaults.length > 0 ? defaults : fallback), ...savedCategories].forEach((category) => {
      rowMap.set(category.code, categoryToRow(category));
    });

    const entryMap = {};
    savedEntries.forEach((entry) => {
      if (entry.rowType !== 'nonProject' || !entry.categoryCode) return;

      const rowId = `non-project-${entry.categoryCode}`;
      entryMap[getEntryKey(rowId, entry.workDate, entry.timeType)] = {
        hours: entry.hours?.toString() ?? '',
        comment: entry.description ?? '',
        workLocationGroupId: entry.workLocationGroupId ?? '',
        workLocationId: entry.workLocationId ?? '',
        savedStatus: entry.status ?? 'draft'
      };
    });

    setActiveRows([...rowMap.values()]);
    setEntries(entryMap);
    setSelectedCell(null);

    const savedTotal = savedEntries.reduce((total, entry) => total + Number(entry.hours || 0), 0);
    setSubmissionStatus(statusToLabel(timesheet.data?.status, savedTotal));
    setSaveStatus(savedEntries.length > 0 ? 'Loaded saved entries' : 'Not saved yet');
  }, [timesheet.data?.weekStart, timesheet.data?.timesheetId, timesheet.data?.status]);

  const days = timesheet.data?.days ?? [];
  const categories = timesheet.data?.nonProjectCategories ?? [];
  const activePolicy = utilizationPolicies.data?.policies?.[0];
  const selectedActivitySource = activitySourceOptions.find((option) => option.key === activitySource) ?? activitySourceOptions[0];
  const currentTimesheetStatus = timesheet.data?.status ?? 'draft';
  const isAnyDayEditable = days.length === 0 || days.some((day) => isDayEditable(day.date));

  const databaseSummary = useMemo(() => {
    if (dbHealth.loading) return 'Checking database connection...';
    if (dbHealth.error) return dbHealth.error;
    return `${dbHealth.data?.status ?? 'unknown'} as ${dbHealth.data?.user ?? 'unknown user'}`;
  }, [dbHealth]);

  function getDayStatus(workDate) {
    const apiDayStatus = timesheet.data?.dayStatuses?.find((dayStatus) => dayStatus.workDate === workDate);
    if (apiDayStatus) return apiDayStatus;

    const submittedEntryExists = (timesheet.data?.entries ?? []).some(
      (entry) => entry.workDate === workDate && entry.status === 'submitted'
    );

    return {
      workDate,
      status: submittedEntryExists ? 'submitted' : 'draft',
      canEdit: !submittedEntryExists,
      canUnlock: submittedEntryExists && Boolean(timesheet.data?.canUnlock),
      unlockMessage: submittedEntryExists
        ? 'This submitted day is locked. Use Unlock if it is within the allowed correction window, or contact your manager.'
        : 'This day is open for time entry.'
    };
  }

  function isDayEditable(workDate) {
    return getDayStatus(workDate).canEdit !== false;
  }

  function getEntry(rowId, date, type) {
    return entries[getEntryKey(rowId, date, type)] ?? {
      hours: '',
      comment: '',
      workLocationGroupId: locationGroups.data?.groups?.[0]?.id ?? '',
      workLocationId: locations.data?.locations?.[0]?.id ?? '',
      savedStatus: 'draft'
    };
  }

  function updateEntry(rowId, date, type, patch) {
    if (!isDayEditable(date)) return;

    const key = getEntryKey(rowId, date, type);
    setEntries((current) => ({
      ...current,
      [key]: {
        ...getEntry(rowId, date, type),
        ...patch
      }
    }));
    setSaveStatus('Unsaved changes');
  }

  function addCategory(category) {
    if (!isAnyDayEditable) return;

    const row = categoryToRow(category);
    setActiveRows((current) => (current.some((item) => item.id === row.id) ? current : [...current, row]));
    setSaveStatus('Unsaved changes');
  }

  function removeRow(rowId) {
    if (!isAnyDayEditable) return;

    setActiveRows((current) => current.filter((row) => row.id !== rowId));
    setEntries((current) => Object.fromEntries(Object.entries(current).filter(([key]) => !key.startsWith(`${rowId}|`))));
    setSelectedCell((current) => (current?.rowId === rowId ? null : current));
    setSaveStatus('Unsaved changes');
  }

  function getCellHours(rowId, date, type) {
    return Number.parseFloat(getEntry(rowId, date, type).hours) || 0;
  }

  function getRowTotal(rowId) {
    return days.reduce((total, day) => total + timeTypes.reduce((subtotal, type) => subtotal + getCellHours(rowId, day.date, type.key), 0), 0);
  }

  function getDayTotal(date) {
    return activeRows.reduce((total, row) => total + timeTypes.reduce((subtotal, type) => subtotal + getCellHours(row.id, date, type.key), 0), 0);
  }

  const grandTotal = activeRows.reduce((total, row) => total + getRowTotal(row.id), 0);
  const afterhoursTotal = activeRows.reduce(
    (total, row) => total + days.reduce((subtotal, day) => subtotal + getCellHours(row.id, day.date, 'afterhours'), 0),
    0
  );
  const normalTotal = grandTotal - afterhoursTotal;

  const selectedRow = activeRows.find((row) => row.id === selectedCell?.rowId);
  const selectedEntry = selectedCell ? getEntry(selectedCell.rowId, selectedCell.date, selectedCell.type) : null;
  const selectedDayStatus = selectedCell ? getDayStatus(selectedCell.date) : null;

  function openEntryDetails(rowId, date, type) {
    setSelectedCell({ rowId, date, type });
  }

  async function closeEntryDetails({ autoSave = true } = {}) {
    const shouldAutoSave = autoSave && selectedCell && isDayEditable(selectedCell.date) && Object.keys(entries).length > 0;
    setSelectedCell(null);

    if (shouldAutoSave) {
      await autoSaveDraft('Auto-saving draft...');
    }
  }

  function buildTimesheetPayload() {
    const payloadEntries = Object.entries(entries)
      .map(([key, entry]) => {
        const [rowId, workDate, timeType] = key.split('|');
        const row = activeRows.find((item) => item.id === rowId);
        const hours = Number.parseFloat(entry.hours);

        if (!row || Number.isNaN(hours) || hours <= 0) return null;

        return {
          rowType: row.type,
          categoryCode: row.categoryCode ?? null,
          workDate,
          timeType,
          hours,
          description: entry.comment || null,
          workLocationGroupId: entry.workLocationGroupId || null,
          workLocationId: entry.workLocationId || null,
          projectId: row.projectId ?? null,
          taskId: row.taskId ?? null
        };
      })
      .filter(Boolean);

    return {
      weekStart: selectedWeekStart,
      entries: payloadEntries
    };
  }

  function getEntriesForDay(workDate) {
    return buildTimesheetPayload().entries.filter((entry) => entry.workDate === workDate);
  }

  function getSelectedDayTotal() {
    if (!selectedCell) return 0;
    return getDayTotal(selectedCell.date);
  }

  async function autoSaveDraft(statusMessage = 'Auto-saving draft...') {
    if (!isAnyDayEditable) return;

    const payload = buildTimesheetPayload();
    if (payload.entries.length === 0) return;

    setSaveStatus(statusMessage);

    try {
      const result = await postJson('/api/timesheets/week/draft', payload);
      setTimesheet({ loading: false, data: result.timesheet, error: null });
      setSubmissionStatus(statusToLabel(result.timesheet?.status, grandTotal));
      setSaveStatus('Draft autosaved');
    } catch (error) {
      setSaveStatus(error instanceof Error ? error.message : 'Autosave failed');
    }
  }

  async function saveDraft() {
    if (!isAnyDayEditable || isSaving) return;

    setIsSaving(true);
    setSaveStatus('Saving draft...');

    try {
      const result = await postJson('/api/timesheets/week/draft', buildTimesheetPayload());
      setTimesheet({ loading: false, data: result.timesheet, error: null });
      setSubmissionStatus(statusToLabel(result.timesheet?.status, grandTotal));
      setSaveStatus('Draft saved');
    } catch (error) {
      setSaveStatus(error instanceof Error ? error.message : 'Failed to save draft');
    } finally {
      setIsSaving(false);
    }
  }

  async function submitSelectedDay() {
    if (!selectedCell || isSaving) return;

    const dayTotal = getDayTotal(selectedCell.date);
    if (dayTotal < 8) {
      setSaveStatus(`A minimum of 8.00 hours is required before submitting ${selectedCell.date}. Current total is ${formatNumber(dayTotal)} hours.`);
      return;
    }

    setIsSaving(true);
    setSaveStatus(`Submitting ${selectedCell.date}...`);

    try {
      const result = await postJson('/api/timesheets/day/submit', {
        weekStart: selectedWeekStart,
        workDate: selectedCell.date,
        entries: getEntriesForDay(selectedCell.date)
      });
      setTimesheet({ loading: false, data: result.timesheet, error: null });
      setSubmissionStatus(`${selectedCell.date} submitted (${formatNumber(dayTotal)} hours).`);
      setSaveStatus(result.message ?? 'Day submitted');
      setSelectedCell(null);
    } catch (error) {
      setSaveStatus(error instanceof Error ? error.message : 'Failed to submit selected day');
    } finally {
      setIsSaving(false);
    }
  }

  async function unlockSelectedDay() {
    if (!selectedCell || isSaving) return;

    setIsSaving(true);
    setSaveStatus(`Requesting unlock for ${selectedCell.date}...`);

    try {
      const result = await postJson('/api/timesheets/day/unlock', {
        weekStart: selectedWeekStart,
        workDate: selectedCell.date
      });
      setTimesheet({ loading: false, data: result.timesheet, error: null });
      setSubmissionStatus('Draft');
      setSaveStatus(result.message ?? 'Day unlocked');
    } catch (error) {
      setSaveStatus(error instanceof Error ? error.message : 'Please contact your manager to unlock this submitted day.');
    } finally {
      setIsSaving(false);
    }
  }

  async function handleSubmit() {
    if (!isAnyDayEditable || isSaving) return;

    if (grandTotal <= 0) {
      setSubmissionStatus('Add time before submitting.');
      return;
    }

    setIsSaving(true);
    setSaveStatus('Saving weekly draft...');

    try {
      const result = await postJson('/api/timesheets/week/draft', buildTimesheetPayload());
      setTimesheet({ loading: false, data: result.timesheet, error: null });
      setSubmissionStatus(statusToLabel(result.timesheet?.status, grandTotal));
      setSaveStatus('Weekly draft saved. Submit each day from the time-entry window when the day reaches 8.00 hours.');
    } catch (error) {
      setSaveStatus(error instanceof Error ? error.message : 'Failed to save weekly draft');
    } finally {
      setIsSaving(false);
    }
  }

  function resetTimesheet() {
    if (!isAnyDayEditable) return;

    setEntries({});
    setSelectedCell(null);
    setSubmissionStatus('Draft');
    setSaveStatus('Unsaved changes');
  }

  return (
    <main className="app-shell">
      <header className="top-bar">
        <SignalLogo />
        <nav aria-label="Primary navigation">
          <a href="#dashboard">Dashboard</a>
          <a href="#timesheet">Timesheet</a>
          <a href="#utilization">Utilization</a>
          <a href="#workflow">Workflow</a>
        </nav>
        <button className="theme-toggle" type="button" onClick={() => setTheme(theme === 'dark' ? 'light' : 'dark')}>
          {theme === 'dark' ? 'Light mode' : 'Dark mode'}
        </button>
      </header>

      <section id="dashboard" className="hero">
        <p className="eyebrow">US Signal Project Pulse</p>
        <h1>Project Pulse: time, approval, utilization, and accounting workflow</h1>
        <p className="hero-copy">
          A focused internal platform for weekly time entry, task-based project assignment, manager approval, project validation, accounting reconciliation, and utilization reporting.
        </p>
      </section>

      <section className="status-grid" aria-label="Platform status">
        <article className="status-card">
          <span className="status-label">API</span>
          <strong>{apiHealth.loading ? 'Checking...' : apiHealth.error ? 'Unavailable' : apiHealth.data?.status}</strong>
          <small>{apiHealth.data?.service ?? apiHealth.error ?? 'Project Pulse API'}</small>
        </article>

        <article className="status-card">
          <span className="status-label">Database</span>
          <strong>{dbHealth.loading ? 'Checking...' : dbHealth.error ? 'Unavailable' : dbHealth.data?.database}</strong>
          <small>{databaseSummary}</small>
        </article>

        <article className="status-card">
          <span className="status-label">Schema</span>
          <strong>{schema.loading ? 'Checking...' : schema.error ? 'Unavailable' : `${schema.data?.count ?? 0} tables`}</strong>
          <small>PostgreSQL platform schema validation</small>
        </article>
      </section>

      <section id="timesheet" className="panel timesheet-page">
        <div className="timesheet-toolbar">
          <div>
            <p className="eyebrow">Timesheet</p>
            <h2>Weekly time entry</h2>
            <DataState loading={timesheet.loading} error={timesheet.error}>
              <p className="muted week-range">Week starts: {timesheet.data?.weekStart} • Week ends: {timesheet.data?.weekEnd}</p>
            </DataState>
          </div>

          <div className="toolbar-actions">
            <button type="button" onClick={() => setSelectedWeekStart(addDaysIso(selectedWeekStart, -7))}>← Previous</button>
            <button type="button" onClick={() => setSelectedWeekStart(getSundayIso())}>Current week</button>
            <button type="button" onClick={() => setSelectedWeekStart(addDaysIso(selectedWeekStart, 7))}>Next →</button>
            <button type="button" onClick={resetTimesheet} disabled={!isAnyDayEditable || isSaving}>Reset</button>
            <button type="button" onClick={saveDraft} disabled={!isAnyDayEditable || isSaving}>Save draft</button>
            <button type="button" className="primary-action" onClick={handleSubmit} disabled={!isAnyDayEditable || isSaving}>Save week</button>
          </div>
        </div>

        <div className="timesheet-status-bar">
          <span className="pill">Status: {submissionStatus}</span>
          <span>Save: <strong>{saveStatus}</strong></span>
          <span>Normal: <strong>{formatNumber(normalTotal)}</strong></span>
          <span>Afterhours: <strong>{formatNumber(afterhoursTotal)}</strong></span>
          <span>Total: <strong>{formatNumber(grandTotal)}</strong></span>
          {currentTimesheetStatus === 'submitted' ? (
            <span className="unlock-message">Submitted days are locked individually. Open days remain editable.</span>
          ) : null}
        </div>

        <DataState loading={timesheet.loading} error={timesheet.error}>
          <div className="timesheet-workspace">
            <aside className="activities-panel" aria-label="Activities">
              <div className="panel-title-row">
                <h3>Activities</h3>
                <span>{activitySource === 'nonProject' ? categories.length : 0}</span>
              </div>

              <div className="activity-selector-row">
                <label htmlFor="activity-source">Activity type</label>
                <select
                  id="activity-source"
                  value={activitySource}
                  onChange={(event) => setActivitySource(event.target.value)}
                >
                  {activitySourceOptions.map((option) => (
                    <option value={option.key} key={option.key}>{option.label}</option>
                  ))}
                </select>
              </div>

              {activitySource === 'nonProject' ? (
                <div className="activity-group activity-results">
                  <h4>Non-project time</h4>
                  {categories.map((category) => {
                    const alreadyAdded = activeRows.some((row) => row.categoryCode === category.code);
                    return (
                      <button
                        className="activity-card"
                        type="button"
                        key={category.code}
                        disabled={alreadyAdded || !isAnyDayEditable}
                        onClick={() => addCategory(category)}
                      >
                        <strong>{category.name}</strong>
                        <span>{category.description ?? category.utilizationBucket}</span>
                        <small>{category.requiresApproval ? 'Approval required' : 'No approval required'}</small>
                      </button>
                    );
                  })}
                </div>
              ) : (
                <div className="empty-activity-state">
                  <strong>{selectedActivitySource.emptyTitle}</strong>
                  <span>{selectedActivitySource.emptyDescription}</span>
                </div>
              )}
            </aside>

            <div className="entry-grid-wrap">
              <div className="entry-grid" role="table" aria-label="Weekly time entry grid">
                <div className="entry-grid-row entry-grid-header" role="row">
                  <div role="columnheader">State</div>
                  <div role="columnheader">Activity</div>
                  <div role="columnheader">Project / Description</div>
                  {days.map((day) => (
                    <div className="day-header" role="columnheader" key={day.date}>
                      <strong>{day.dayName.slice(0, 3)}</strong>
                      <span>{day.date.slice(5)}</span>
                      <em>N / AH</em>
                    </div>
                  ))}
                  <div role="columnheader">Total</div>
                  <div role="columnheader">Action</div>
                </div>

                {activeRows.map((row) => (
                  <div className="entry-grid-row" role="row" key={row.id}>
                    <div role="cell"><span className="state-dot">•</span> {row.state}</div>
                    <div role="cell" className="activity-name">{row.activity}</div>
                    <div role="cell">{row.projectDescription}</div>
                    {days.map((day) => (
                      <div className="time-cell-pair" role="cell" key={`${row.id}-${day.date}`}>
                        {timeTypes.map((type) => {
                          const entry = getEntry(row.id, day.date, type.key);
                          const isSelected = selectedCell?.rowId === row.id && selectedCell?.date === day.date && selectedCell?.type === type.key;
                          const dayIsEditable = isDayEditable(day.date);
                          return (
                            <button
                              aria-label={`${row.activity} ${day.date} ${type.label}`}
                              className={isSelected ? 'time-entry-button selected-time-input' : 'time-entry-button'}
                              key={type.key}
                              type="button"
                              title={`${type.label}: ${formatHoursValue(entry.hours)} hours`}
                              onClick={() => openEntryDetails(row.id, day.date, type.key)}
                              disabled={!dayIsEditable}
                            >
                              {formatHoursValue(entry.hours)}
                            </button>
                          );
                        })}
                      </div>
                    ))}
                    <div role="cell" className="row-total">{formatNumber(getRowTotal(row.id))}</div>
                    <div role="cell">
                      <button className="link-button" type="button" onClick={() => removeRow(row.id)} disabled={!isAnyDayEditable}>Remove</button>
                    </div>
                  </div>
                ))}

                <div className="entry-grid-row total-row" role="row">
                  <div role="cell">Total</div>
                  <div role="cell"></div>
                  <div role="cell"></div>
                  {days.map((day) => (
                    <div role="cell" key={`total-${day.date}`}>{formatNumber(getDayTotal(day.date))}</div>
                  ))}
                  <div role="cell">{formatNumber(grandTotal)}</div>
                  <div role="cell"></div>
                </div>
              </div>
            </div>
          </div>
        </DataState>
      </section>

      {selectedCell && selectedRow && selectedEntry ? (
        <div className="details-modal-backdrop" role="presentation" onMouseDown={(event) => {
          if (event.target === event.currentTarget) void closeEntryDetails({ autoSave: true });
        }}>
          <section className="details-modal" role="dialog" aria-modal="true" aria-label="Time entry details">
            <div className="modal-title-row">
              <div>
                <p className="eyebrow">Time entry details</p>
                <h2>{selectedRow.activity}</h2>
                <p className="muted small-text">
                  {selectedCell.date} • {selectedCell.type === 'afterhours' ? 'Afterhours' : 'Normal time'}
                </p>
              </div>
              <div className="modal-actions">
                {selectedDayStatus?.status === 'submitted' ? (
                  <button type="button" className="unlock-action" onClick={unlockSelectedDay} disabled={isSaving}>
                    Unlock this day
                  </button>
                ) : (
                  <button type="button" className="primary-action" onClick={submitSelectedDay} disabled={isSaving || getSelectedDayTotal() < 8}>
                    Submit this day
                  </button>
                )}
                <button type="button" className="modal-close-button" onClick={() => void closeEntryDetails({ autoSave: true })}>Close</button>
              </div>
            </div>

            <div className="detail-form modal-detail-form">
              <label>
                Hours
                <input
                  inputMode="decimal"
                  min="0"
                  step="0.25"
                  type="number"
                  value={selectedEntry.hours}
                  placeholder="0.00"
                  autoFocus
                  disabled={!isDayEditable(selectedCell.date)}
                  onChange={(event) => updateEntry(selectedCell.rowId, selectedCell.date, selectedCell.type, { hours: event.target.value })}
                />
              </label>
              <label>
                Description / comment
                <textarea
                  value={selectedEntry.comment}
                  placeholder="Enter the reportable comment for this time entry."
                  disabled={!isDayEditable(selectedCell.date)}
                  onChange={(event) => updateEntry(selectedCell.rowId, selectedCell.date, selectedCell.type, { comment: event.target.value })}
                />
              </label>
              <label>
                Work location group
                <select
                  value={selectedEntry.workLocationGroupId}
                  disabled={!isDayEditable(selectedCell.date)}
                  onChange={(event) => updateEntry(selectedCell.rowId, selectedCell.date, selectedCell.type, { workLocationGroupId: event.target.value })}
                >
                  {(locationGroups.data?.groups ?? []).map((group) => (
                    <option value={group.id} key={group.id}>{group.name}</option>
                  ))}
                </select>
              </label>
              <label>
                Work location
                <select
                  value={selectedEntry.workLocationId}
                  disabled={!isDayEditable(selectedCell.date)}
                  onChange={(event) => updateEntry(selectedCell.rowId, selectedCell.date, selectedCell.type, { workLocationId: event.target.value })}
                >
                  {(locations.data?.locations ?? []).map((location) => (
                    <option value={location.id} key={location.id}>{location.name}</option>
                  ))}
                </select>
              </label>
            </div>

            <div className="day-submit-actions">
              <span>
                Day total: <strong>{formatNumber(getSelectedDayTotal())}</strong> / minimum 8.00 hours
              </span>
              {selectedDayStatus?.status === 'submitted' ? (
                <small>{selectedDayStatus.unlockMessage}</small>
              ) : (
                <small>Use Submit this day once the day reaches at least 8.00 hours. Closing this window automatically saves your draft.</small>
              )}
              {isSaving ? <small className="modal-save-note">Saving...</small> : null}
            </div>
          </section>
        </div>
      ) : null}

      <section id="utilization" className="panel">
        <div className="section-header compact">
          <div>
            <p className="eyebrow">Utilization policy</p>
            <h2>Quarterly targets</h2>
          </div>
          <DataState loading={utilizationPolicies.loading} error={utilizationPolicies.error}>
            <span className="pill">{activePolicy?.standardPeriodHours ?? 0} standard hours</span>
          </DataState>
        </div>

        <DataState loading={utilizationTargets.loading} error={utilizationTargets.error}>
          <div className="target-grid">
            {utilizationTargets.data?.targets?.map((target) => (
              <article className="target-card" key={target.targetPercent}>
                <strong>{Number(target.targetPercent).toFixed(0)}%</strong>
                <span>{Number(target.targetHours).toFixed(1)} hrs</span>
                <small>{target.bonusReferenceAmount ? `$${Number(target.bonusReferenceAmount).toLocaleString()}` : 'No reference amount'}</small>
              </article>
            ))}
          </div>
        </DataState>
      </section>

      <section id="workflow" className="section-header">
        <h2>Core workflow areas</h2>
        <p>These modules reflect the approved platform direction and will be implemented incrementally.</p>
      </section>

      <section className="module-grid" aria-label="Core workflow modules">
        {workflowCards.map((card) => (
          <article className="module-card" key={card.title}>
            <div>
              <h3>{card.title}</h3>
              <p>{card.description}</p>
            </div>
            <span>{card.status}</span>
          </article>
        ))}
      </section>
    </main>
  );
}
