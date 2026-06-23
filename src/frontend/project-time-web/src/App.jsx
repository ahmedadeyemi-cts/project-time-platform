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

async function fetchJson(path) {
  const response = await fetch(path);

  if (!response.ok) {
    throw new Error(`${path} returned HTTP ${response.status}`);
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

function SignalLogo() {
  return (
    <div className="brand-lockup" aria-label="US Signal Project Time Platform">
      <img className="brand-logo-image" src={usSignalLogoUrl} alt="US Signal" />
      <div>
        <strong>Project Time Platform</strong>
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
    setActiveRows((defaults.length > 0 ? defaults : fallback).map(categoryToRow));
    setEntries({});
    setSelectedCell(null);
    setSubmissionStatus('Draft');
  }, [timesheet.data?.weekStart]);

  const days = timesheet.data?.days ?? [];
  const categories = timesheet.data?.nonProjectCategories ?? [];
  const activePolicy = utilizationPolicies.data?.policies?.[0];

  const databaseSummary = useMemo(() => {
    if (dbHealth.loading) return 'Checking database connection...';
    if (dbHealth.error) return dbHealth.error;
    return `${dbHealth.data?.status ?? 'unknown'} as ${dbHealth.data?.user ?? 'unknown user'}`;
  }, [dbHealth]);

  function getEntry(rowId, date, type) {
    return entries[getEntryKey(rowId, date, type)] ?? {
      hours: '',
      comment: '',
      workLocationGroupId: locationGroups.data?.groups?.[0]?.id ?? '',
      workLocationId: locations.data?.locations?.[0]?.id ?? ''
    };
  }

  function updateEntry(rowId, date, type, patch) {
    const key = getEntryKey(rowId, date, type);
    setEntries((current) => ({
      ...current,
      [key]: {
        ...getEntry(rowId, date, type),
        ...patch
      }
    }));
  }

  function addCategory(category) {
    const row = categoryToRow(category);
    setActiveRows((current) => (current.some((item) => item.id === row.id) ? current : [...current, row]));
  }

  function removeRow(rowId) {
    setActiveRows((current) => current.filter((row) => row.id !== rowId));
    setEntries((current) => Object.fromEntries(Object.entries(current).filter(([key]) => !key.startsWith(`${rowId}|`))));
    setSelectedCell((current) => (current?.rowId === rowId ? null : current));
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

  function handleSubmit() {
    if (grandTotal <= 0) {
      setSubmissionStatus('Add time before submitting.');
      return;
    }

    setSubmissionStatus(`Submitted for manager approval (${formatNumber(grandTotal)} hours).`);
  }

  function resetTimesheet() {
    setEntries({});
    setSelectedCell(null);
    setSubmissionStatus('Draft');
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
        <p className="eyebrow">US Signal Project Time Platform</p>
        <h1>Time, approval, utilization, and accounting workflow foundation</h1>
        <p className="hero-copy">
          A focused internal platform for weekly time entry, task-based project assignment, manager approval, project validation, accounting reconciliation, and utilization reporting.
        </p>
      </section>

      <section className="status-grid" aria-label="Platform status">
        <article className="status-card">
          <span className="status-label">API</span>
          <strong>{apiHealth.loading ? 'Checking...' : apiHealth.error ? 'Unavailable' : apiHealth.data?.status}</strong>
          <small>{apiHealth.data?.service ?? apiHealth.error ?? 'Local API health endpoint'}</small>
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
            <button type="button" onClick={resetTimesheet}>Reset</button>
            <button type="button" className="primary-action" onClick={handleSubmit}>Submit</button>
          </div>
        </div>

        <div className="timesheet-status-bar">
          <span className="pill">Status: {submissionStatus}</span>
          <span>Normal: <strong>{formatNumber(normalTotal)}</strong></span>
          <span>Afterhours: <strong>{formatNumber(afterhoursTotal)}</strong></span>
          <span>Total: <strong>{formatNumber(grandTotal)}</strong></span>
        </div>

        <DataState loading={timesheet.loading} error={timesheet.error}>
          <div className="timesheet-workspace">
            <aside className="activities-panel" aria-label="Activities">
              <div className="panel-title-row">
                <h3>Activities</h3>
                <span>{categories.length}</span>
              </div>

              <div className="activity-group">
                <h4>Project tasks</h4>
                <p className="muted small-text">
                  Assigned project tasks will appear here once the project assignment API is connected to saved project/task data.
                </p>
              </div>

              <div className="activity-group">
                <h4>Non-project time</h4>
                {categories.map((category) => {
                  const alreadyAdded = activeRows.some((row) => row.categoryCode === category.code);
                  return (
                    <button
                      className="activity-card"
                      type="button"
                      key={category.code}
                      disabled={alreadyAdded}
                      onClick={() => addCategory(category)}
                    >
                      <strong>{category.name}</strong>
                      <span>{category.description ?? category.utilizationBucket}</span>
                      <small>{category.requiresApproval ? 'Approval required' : 'No approval required'}</small>
                    </button>
                  );
                })}
              </div>
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
                          return (
                            <input
                              aria-label={`${row.activity} ${day.date} ${type.label}`}
                              className={isSelected ? 'selected-time-input' : ''}
                              key={type.key}
                              min="0"
                              step="0.25"
                              type="number"
                              value={entry.hours}
                              placeholder="0.00"
                              onFocus={() => setSelectedCell({ rowId: row.id, date: day.date, type: type.key })}
                              onChange={(event) => updateEntry(row.id, day.date, type.key, { hours: event.target.value })}
                            />
                          );
                        })}
                      </div>
                    ))}
                    <div role="cell" className="row-total">{formatNumber(getRowTotal(row.id))}</div>
                    <div role="cell">
                      <button className="link-button" type="button" onClick={() => removeRow(row.id)}>Remove</button>
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

            <aside className="details-panel" aria-label="Details panel">
              <h3>Details</h3>
              {selectedCell && selectedRow && selectedEntry ? (
                <div className="detail-form">
                  <p className="muted small-text">
                    {selectedRow.activity} • {selectedCell.date} • {selectedCell.type === 'afterhours' ? 'Afterhours' : 'Normal time'}
                  </p>
                  <label>
                    Description / comment
                    <textarea
                      value={selectedEntry.comment}
                      placeholder="Enter the reportable comment for this time entry."
                      onChange={(event) => updateEntry(selectedCell.rowId, selectedCell.date, selectedCell.type, { comment: event.target.value })}
                    />
                  </label>
                  <label>
                    Work location group
                    <select
                      value={selectedEntry.workLocationGroupId}
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
                      onChange={(event) => updateEntry(selectedCell.rowId, selectedCell.date, selectedCell.type, { workLocationId: event.target.value })}
                    >
                      {(locations.data?.locations ?? []).map((location) => (
                        <option value={location.id} key={location.id}>{location.name}</option>
                      ))}
                    </select>
                  </label>
                </div>
              ) : (
                <p className="muted">Select a normal or afterhours cell to add a comment and work location details.</p>
              )}
            </aside>
          </div>
        </DataState>
      </section>

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
