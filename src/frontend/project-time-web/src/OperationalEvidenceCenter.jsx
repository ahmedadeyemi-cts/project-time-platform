import { useCallback, useEffect, useMemo, useState } from 'react';
import './operational-evidence-center.css';

function sessionToken(authSession) {
  return authSession?.sessionToken
    ?? authSession?.token
    ?? authSession?.accessToken
    ?? window.localStorage.getItem('projectPulseSessionToken')
    ?? window.sessionStorage.getItem('projectPulseSessionToken')
    ?? '';
}

function authHeaders(authSession) {
  const token = sessionToken(authSession);
  return token ? {
    Authorization: `Bearer ${token}`,
    'X-ProjectPulse-Session': token,
    'X-Project-Pulse-Session': token,
    'X-Session-Token': token
  } : {};
}

async function readJson(path, authSession) {
  const response = await fetch(path, {
    method: 'GET',
    cache: 'no-store',
    credentials: 'include',
    headers: authHeaders(authSession)
  });
  const payload = await response.json().catch(() => null);
  if (!response.ok) throw new Error(payload?.message ?? `Evidence request returned HTTP ${response.status}.`);
  return payload;
}

function title(value) {
  return String(value ?? 'unknown').replaceAll('_', ' ').replace(/\b\w/g, (letter) => letter.toUpperCase());
}

function dateTime(value) {
  if (!value) return 'Not observed';
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? 'Not observed' : parsed.toLocaleString();
}

function tone(value) {
  const normalized = String(value ?? '').toLowerCase();
  if (['succeeded', 'healthy', 'registered'].includes(normalized)) return 'healthy';
  if (['failed', 'critical'].includes(normalized)) return 'critical';
  if (['rejected', 'warning'].includes(normalized)) return 'warning';
  return 'neutral';
}

function Status({ value }) {
  return <span className={`operational-evidence-status ${tone(value)}`}>{title(value)}</span>;
}

export default function OperationalEvidenceCenter({ authSession }) {
  const [filters, setFilters] = useState({ search: '', module: '', status: '', correlationId: '' });
  const [appliedFilters, setAppliedFilters] = useState(filters);
  const [state, setState] = useState({ loading: true, data: null, error: '' });

  const load = useCallback(async () => {
    setState((current) => ({ ...current, loading: true, error: '' }));
    const query = new URLSearchParams({ limit: '300' });
    Object.entries(appliedFilters).forEach(([key, value]) => {
      if (String(value ?? '').trim()) query.set(key, String(value).trim());
    });
    try {
      const data = await readJson(`/api/platform-operations/evidence?${query.toString()}`, authSession);
      setState({ loading: false, data, error: '' });
    } catch (error) {
      setState((current) => ({ ...current, loading: false, error: error?.message ?? 'Operational evidence is unavailable.' }));
    }
  }, [appliedFilters, authSession]);

  useEffect(() => {
    void load();
  }, [load]);

  const events = state.data?.events ?? [];
  const moduleOptions = useMemo(() => Array.from(new Map(
    events.filter((event) => event.moduleCode).map((event) => [event.moduleCode, `${event.moduleCode} · ${event.moduleName}`])
  ).entries()).sort((left, right) => left[0].localeCompare(right[0], undefined, { numeric: true })), [events]);

  function apply(event) {
    event.preventDefault();
    setAppliedFilters(filters);
  }

  function clear() {
    const empty = { search: '', module: '', status: '', correlationId: '' };
    setFilters(empty);
    setAppliedFilters(empty);
  }

  function exportEvidence() {
    const anchor = document.createElement('a');
    anchor.href = '/api/platform-operations/evidence/export';
    anchor.download = '';
    document.body.appendChild(anchor);
    anchor.click();
    anchor.remove();
  }

  const summary = state.data?.summary ?? {};

  return (
    <section className="operational-evidence-center" data-module="016" data-mode="provider-neutral-evidence">
      <header className="operational-evidence-hero">
        <div>
          <p className="eyebrow">Module 016 · Deep operational evidence</p>
          <h1>Operational Evidence &amp; Diagnostic History</h1>
          <p>
            Search sanitized API observations, failures, dependency timelines, background workers,
            scheduled-job readiness, and correlation evidence without exposing request bodies, secrets,
            query strings, or raw exception messages.
          </p>
        </div>
        <div className="operational-evidence-actions">
          <button type="button" className="secondary-action" onClick={load} disabled={state.loading}>{state.loading ? 'Refreshing…' : 'Refresh evidence'}</button>
          <button type="button" className="primary-action" onClick={exportEvidence}>Export JSON</button>
        </div>
      </header>

      {state.error ? <div className="operational-evidence-alert" role="alert">{state.error}</div> : null}

      <section className="operational-evidence-summary">
        <article><span>Returned</span><strong>{summary.returned ?? 0}</strong><small>Sanitized evidence events</small></article>
        <article><span>Failed</span><strong>{summary.failed ?? 0}</strong><small>Server or dependency failures</small></article>
        <article><span>Rejected</span><strong>{summary.rejected ?? 0}</strong><small>Authorization or validation outcomes</small></article>
        <article><span>Succeeded</span><strong>{summary.succeeded ?? 0}</strong><small>Completed requests and probes</small></article>
        <article><span>Workers</span><strong>{summary.workerCount ?? 0}</strong><small>Runtime-registered hosted services</small></article>
      </section>

      <form className="operational-evidence-filter" onSubmit={apply}>
        <label><span>Search evidence</span><input type="search" value={filters.search} onChange={(event) => setFilters((current) => ({ ...current, search: event.target.value }))} placeholder="Path, module, message, or error code" /></label>
        <label><span>Module</span><select value={filters.module} onChange={(event) => setFilters((current) => ({ ...current, module: event.target.value }))}><option value="">All modules</option>{moduleOptions.map(([code, label]) => <option value={code} key={code}>{label}</option>)}</select></label>
        <label><span>Status</span><select value={filters.status} onChange={(event) => setFilters((current) => ({ ...current, status: event.target.value }))}><option value="">All statuses</option><option value="failed">Failed</option><option value="rejected">Rejected</option><option value="succeeded">Succeeded</option><option value="healthy">Healthy</option></select></label>
        <label><span>Correlation ID</span><input value={filters.correlationId} onChange={(event) => setFilters((current) => ({ ...current, correlationId: event.target.value }))} placeholder="Exact or partial correlation ID" /></label>
        <div><button type="submit" className="primary-action">Apply filters</button><button type="button" className="secondary-action" onClick={clear}>Clear</button></div>
      </form>

      <section className="operational-evidence-panel">
        <div className="operational-evidence-heading"><div><p className="eyebrow">Request and failure history</p><h2>Evidence timeline</h2></div><span>{events.length} shown</span></div>
        <div className="operational-evidence-table-wrap">
          <table>
            <thead><tr><th>Observed</th><th>Module</th><th>Event</th><th>API</th><th>Status</th><th>Latency</th><th>Error</th><th>Correlation</th></tr></thead>
            <tbody>
              {events.map((event) => (
                <tr key={event.evidenceId}>
                  <td>{dateTime(event.observedAt)}</td>
                  <td><strong>{event.moduleCode}</strong><small>{event.moduleName}</small></td>
                  <td>{title(event.eventType)}<small>{event.message}</small></td>
                  <td><code>{event.method} {event.path}</code></td>
                  <td><Status value={event.status} /></td>
                  <td>{Number.isFinite(Number(event.responseTimeMs)) ? `${event.responseTimeMs} ms` : '—'}</td>
                  <td>{event.errorCode || 'None'}</td>
                  <td><code>{event.correlationId}</code></td>
                </tr>
              ))}
              {!state.loading && !events.length ? <tr><td colSpan="8">No operational evidence matches the current filters. Open Module 013 or another module to generate current request observations.</td></tr> : null}
              {state.loading ? <tr><td colSpan="8">Loading operational evidence…</td></tr> : null}
            </tbody>
          </table>
        </div>
      </section>

      <div className="operational-evidence-two-column">
        <section className="operational-evidence-panel">
          <div className="operational-evidence-heading"><div><p className="eyebrow">Failure concentration</p><h2>Dependency timeline</h2></div></div>
          <div className="dependency-timeline-list">
            {(state.data?.dependencyTimeline ?? []).map((item) => (
              <article key={item.moduleCode}><div><strong>Module {item.moduleCode}</strong><span>{item.failureCount} failure(s)</span></div><p>Latest: {item.latestErrorCode || 'No error code'}</p><small>{dateTime(item.latestObservedAt)} · {item.latestCorrelationId}</small></article>
            ))}
            {!state.data?.dependencyTimeline?.length ? <p>No failed dependency timeline is currently stored.</p> : null}
          </div>
        </section>

        <section className="operational-evidence-panel">
          <div className="operational-evidence-heading"><div><p className="eyebrow">Background execution</p><h2>Workers and scheduled jobs</h2></div></div>
          <div className="operational-worker-list">
            {(state.data?.workers ?? []).map((worker) => <article key={worker.key}><div><strong>{worker.name}</strong><Status value={worker.status} /></div><p>{worker.restartMessage}</p><small>{worker.source}</small></article>)}
            {(state.data?.scheduledJobs ?? []).map((job) => <article key={job.key}><div><strong>{job.name}</strong><Status value={job.status} /></div><p>{job.message}</p></article>)}
          </div>
        </section>
      </div>

      <section className="operational-evidence-boundary">
        <div><strong>Evidence boundary</strong><span>Request bodies, query strings, provider credentials, access tokens, and raw exception messages are never included.</span></div>
        <div><strong>Persistence boundary</strong><span>Runtime evidence is bounded and resets when the API process restarts. Durable observability remains a future Module 078 adapter responsibility.</span></div>
      </section>
    </section>
  );
}
