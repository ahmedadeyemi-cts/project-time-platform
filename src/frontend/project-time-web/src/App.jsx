import { useEffect, useMemo, useState } from 'react';

const workflowCards = [
  {
    title: 'Time Entry',
    description: 'Enter weekly project-task, non-project, normal, and afterhours time.',
    status: 'Foundation ready'
  },
  {
    title: 'Manager Approval',
    description: 'Review submitted regular and OT hours by resource, task, and date.',
    status: 'Next phase'
  },
  {
    title: 'Project Approval',
    description: 'Project managers validate task-level allocation before accounting review.',
    status: 'Next phase'
  },
  {
    title: 'Accounting Reconciliation',
    description: 'Accounting reconciles approved time before period lock and reporting.',
    status: 'Planned'
  },
  {
    title: 'Utilization',
    description: 'Track billable, OT, PTO, and approved presales/training against target.',
    status: 'Policy loaded'
  },
  {
    title: 'Audit Trail',
    description: 'Role, approval, decline, reconciliation, and administrative actions are logged.',
    status: 'Planned'
  }
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

function SignalLogo() {
  return (
    <div className="brand-lockup" aria-label="US Signal Project Time Platform">
      <div className="signal-mark" aria-hidden="true">
        <span />
        <span />
        <span />
      </div>
      <div>
        <strong>US Signal</strong>
        <small>Project Time Platform</small>
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
  const [apiHealth, setApiHealth] = useState({ loading: true, data: null, error: null });
  const [dbHealth, setDbHealth] = useState({ loading: true, data: null, error: null });
  const [schema, setSchema] = useState({ loading: true, data: null, error: null });
  const [timesheet, setTimesheet] = useState({ loading: true, data: null, error: null });
  const [locations, setLocations] = useState({ loading: true, data: null, error: null });
  const [utilizationPolicies, setUtilizationPolicies] = useState({ loading: true, data: null, error: null });
  const [utilizationTargets, setUtilizationTargets] = useState({ loading: true, data: null, error: null });

  useEffect(() => {
    document.documentElement.dataset.theme = theme;
    window.localStorage.setItem('ptp-theme', theme);
  }, [theme]);

  useEffect(() => {
    let cancelled = false;

    async function loadStatus() {
      try {
        const [healthResult, dbResult, schemaResult, timesheetResult, locationsResult, policyResult, targetsResult] = await Promise.all([
          fetchJson('/health'),
          fetchJson('/api/db-health'),
          fetchJson('/api/schema/tables'),
          fetchJson('/api/timesheets/week?weekStart=2026-06-21'),
          fetchJson('/api/work-locations'),
          fetchJson('/api/utilization/policies'),
          fetchJson('/api/utilization/targets')
        ]);

        if (!cancelled) {
          setApiHealth({ loading: false, data: healthResult, error: null });
          setDbHealth({ loading: false, data: dbResult, error: null });
          setSchema({ loading: false, data: schemaResult, error: null });
          setTimesheet({ loading: false, data: timesheetResult, error: null });
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
  }, []);

  const databaseSummary = useMemo(() => {
    if (dbHealth.loading) return 'Checking database connection...';
    if (dbHealth.error) return dbHealth.error;
    return `${dbHealth.data?.status ?? 'unknown'} as ${dbHealth.data?.user ?? 'unknown user'}`;
  }, [dbHealth]);

  const activePolicy = utilizationPolicies.data?.policies?.[0];
  const locationText = locations.data?.locations?.[0]
    ? `${locations.data.locations[0].name} (${locations.data.locations[0].timeZone})`
    : 'No work locations loaded';

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

      <section id="timesheet" className="panel timesheet-panel">
        <div className="section-header compact">
          <div>
            <p className="eyebrow">Timesheet shell</p>
            <h2>Weekly time entry</h2>
          </div>
          <DataState loading={timesheet.loading} error={timesheet.error}>
            <span className="pill">{timesheet.data?.weekStart} to {timesheet.data?.weekEnd}</span>
          </DataState>
        </div>

        <DataState loading={timesheet.loading} error={timesheet.error}>
          <div className="timesheet-grid" role="table" aria-label="Weekly timesheet shell">
            <div className="timesheet-row header-row" role="row">
              <div role="columnheader">Entry type</div>
              {timesheet.data?.days?.map((day) => (
                <div role="columnheader" key={day.date}>{day.dayName.slice(0, 3)}<span>{day.date.slice(5)}</span></div>
              ))}
              <div role="columnheader">Total</div>
            </div>
            {timesheet.data?.timeTypes?.map((type) => (
              <div className="timesheet-row" role="row" key={type}>
                <div role="cell" className="row-title">{type === 'afterhours' ? 'Afterhours / OT' : 'Normal time'}</div>
                {timesheet.data.days.map((day) => (
                  <div role="cell" key={`${type}-${day.date}`}>0.00</div>
                ))}
                <div role="cell" className="row-total">0.00</div>
              </div>
            ))}
          </div>
        </DataState>

        <div className="two-column-detail">
          <article>
            <h3>Non-project categories</h3>
            <DataState loading={timesheet.loading} error={timesheet.error}>
              <div className="tag-list">
                {timesheet.data?.nonProjectCategories?.map((category) => (
                  <span key={category.code}>{category.name}</span>
                ))}
              </div>
            </DataState>
          </article>
          <article>
            <h3>Work location</h3>
            <DataState loading={locations.loading} error={locations.error}>
              <p>{locationText}</p>
            </DataState>
          </article>
        </div>
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
