import { useEffect, useMemo, useState } from 'react';

const workflowCards = [
  {
    title: 'Time Entry',
    description: 'Engineers enter weekly project and task hours before submission.',
    status: 'Planned'
  },
  {
    title: 'Manager Approval',
    description: 'Managers approve or decline submitted time before project review.',
    status: 'Planned'
  },
  {
    title: 'Project Approval',
    description: 'Project managers validate project and task allocation accuracy.',
    status: 'Planned'
  },
  {
    title: 'Accounting Reconciliation',
    description: 'Accounting reviews approved time and reconciles the period before lock.',
    status: 'Planned'
  },
  {
    title: 'Utilization',
    description: 'Monthly and quarterly utilization summaries show progress against target.',
    status: 'Planned'
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

export default function App() {
  const [apiHealth, setApiHealth] = useState({ loading: true, data: null, error: null });
  const [dbHealth, setDbHealth] = useState({ loading: true, data: null, error: null });
  const [schema, setSchema] = useState({ loading: true, data: null, error: null });

  useEffect(() => {
    let cancelled = false;

    async function loadStatus() {
      try {
        const [healthResult, dbResult, schemaResult] = await Promise.all([
          fetchJson('/health'),
          fetchJson('/api/db-health'),
          fetchJson('/api/schema/tables')
        ]);

        if (!cancelled) {
          setApiHealth({ loading: false, data: healthResult, error: null });
          setDbHealth({ loading: false, data: dbResult, error: null });
          setSchema({ loading: false, data: schemaResult, error: null });
        }
      } catch (error) {
        if (!cancelled) {
          const message = error instanceof Error ? error.message : 'Unknown error';
          setApiHealth((current) => ({ ...current, loading: false, error: message }));
          setDbHealth((current) => ({ ...current, loading: false, error: message }));
          setSchema((current) => ({ ...current, loading: false, error: message }));
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

  return (
    <main className="app-shell">
      <section className="hero">
        <p className="eyebrow">Project Time Platform</p>
        <h1>Time, approval, utilization, and accounting workflow foundation</h1>
        <p className="hero-copy">
          This first frontend build validates that the React application can load locally and communicate with the ASP.NET Core API and PostgreSQL-backed endpoints.
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
          <small>Initial platform schema validation</small>
        </article>
      </section>

      <section className="section-header">
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
