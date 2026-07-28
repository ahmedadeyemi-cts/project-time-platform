import { useCallback, useEffect, useMemo, useState } from 'react';
import './service-control-center.css';

const REFRESH_MS = 30000;

function sessionToken(authSession) {
  return authSession?.sessionToken
    ?? authSession?.token
    ?? authSession?.accessToken
    ?? window.localStorage.getItem('projectPulseSessionToken')
    ?? window.sessionStorage.getItem('projectPulseSessionToken')
    ?? '';
}

function headers(authSession, extra = {}) {
  const token = sessionToken(authSession);
  return {
    ...extra,
    ...(token ? {
      Authorization: `Bearer ${token}`,
      'X-ProjectPulse-Session': token,
      'X-Project-Pulse-Session': token,
      'X-Session-Token': token
    } : {})
  };
}

async function readJson(path, authSession, options = {}) {
  const response = await fetch(path, {
    credentials: 'include',
    cache: 'no-store',
    ...options,
    headers: headers(authSession, options.headers ?? {})
  });
  const raw = await response.text();
  let payload = {};
  try {
    payload = raw ? JSON.parse(raw) : {};
  } catch {
    payload = { message: raw };
  }
  if (!response.ok) {
    throw new Error(payload?.message ?? `${path} returned HTTP ${response.status}.`);
  }
  return payload;
}

function title(value) {
  return String(value ?? 'unknown')
    .replaceAll('_', ' ')
    .replace(/\b\w/g, (letter) => letter.toUpperCase());
}

function bytes(value) {
  const number = Number(value);
  if (!Number.isFinite(number) || number < 0) return 'Not reported';
  const units = ['B', 'KB', 'MB', 'GB', 'TB'];
  let size = number;
  let index = 0;
  while (size >= 1024 && index < units.length - 1) {
    size /= 1024;
    index += 1;
  }
  return `${size.toFixed(index === 0 ? 0 : 1)} ${units[index]}`;
}

function duration(seconds) {
  const value = Number(seconds);
  if (!Number.isFinite(value)) return 'Not reported';
  const days = Math.floor(value / 86400);
  const hours = Math.floor((value % 86400) / 3600);
  const minutes = Math.floor((value % 3600) / 60);
  return [days ? `${days}d` : '', hours ? `${hours}h` : '', `${minutes}m`].filter(Boolean).join(' ');
}

function dateTime(value) {
  if (!value) return 'Not observed';
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? 'Not observed' : parsed.toLocaleString();
}

function tone(value) {
  const normalized = String(value ?? '').toLowerCase();
  if (['healthy', 'succeeded', 'active', 'supported', 'configured', 'source_managed', 'observed'].includes(normalized)) return 'healthy';
  if (['failed', 'critical', 'unavailable'].includes(normalized)) return 'critical';
  if (['rejected', 'warning', 'degraded', 'adapter_required', 'connector_required'].includes(normalized)) return 'warning';
  return 'neutral';
}

function Status({ value }) {
  return <span className={`platform-status ${tone(value)}`}>{title(value)}</span>;
}

export default function ServiceControlCenter({ authSession }) {
  const [state, setState] = useState({ loading: true, overview: null, inventory: null, error: '' });
  const [filters, setFilters] = useState({ search: '', module: 'all', status: 'all' });
  const [selected, setSelected] = useState({ loading: false, data: null, error: '' });
  const [retest, setRetest] = useState({ apiId: '', loading: false, message: '', error: '' });
  const [volumeExpanded, setVolumeExpanded] = useState(false);

  const load = useCallback(async ({ quiet = false } = {}) => {
    if (!quiet) setState((current) => ({ ...current, loading: true, error: '' }));
    try {
      const [overview, inventory] = await Promise.all([
        readJson('/api/platform-operations/overview', authSession),
        readJson('/api/platform-operations/apis', authSession)
      ]);
      setState({ loading: false, overview, inventory, error: '' });
    } catch (error) {
      setState((current) => ({ ...current, loading: false, error: error?.message ?? 'System health is unavailable.' }));
    }
  }, [authSession]);

  useEffect(() => {
    void load();
    const timer = window.setInterval(() => void load({ quiet: true }), REFRESH_MS);
    return () => window.clearInterval(timer);
  }, [load]);

  const apis = state.inventory?.apis ?? [];
  const modules = useMemo(() => Array.from(new Map(
    apis.map((api) => [api.moduleCode, `${api.moduleCode} · ${api.moduleName}`])
  ).entries()).sort((left, right) => left[0].localeCompare(right[0], undefined, { numeric: true })), [apis]);

  const filteredApis = useMemo(() => {
    const search = filters.search.trim().toLowerCase();
    return apis.filter((api) => {
      if (filters.module !== 'all' && api.moduleCode !== filters.module) return false;
      if (filters.status !== 'all' && api.currentStatus !== filters.status) return false;
      if (!search) return true;
      return `${api.moduleCode} ${api.moduleName} ${api.method} ${api.path} ${api.purpose} ${api.routeGroup}`
        .toLowerCase()
        .includes(search);
    });
  }, [apis, filters]);

  async function openApi(api) {
    setSelected({ loading: true, data: { api }, error: '' });
    try {
      const detail = await readJson(`/api/platform-operations/apis/${encodeURIComponent(api.apiId)}`, authSession);
      setSelected({ loading: false, data: detail, error: '' });
    } catch (error) {
      setSelected({ loading: false, data: { api }, error: error?.message ?? 'API diagnostics are unavailable.' });
    }
  }

  async function retestApi(api) {
    setRetest({ apiId: api.apiId, loading: true, message: '', error: '' });
    try {
      const result = await readJson(`/api/platform-operations/apis/${encodeURIComponent(api.apiId)}/retest`, authSession, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: '{}'
      });
      setRetest({ apiId: api.apiId, loading: false, message: `${result.message} Correlation: ${result.correlationId}`, error: '' });
      await load({ quiet: true });
      await openApi(api);
    } catch (error) {
      setRetest({ apiId: api.apiId, loading: false, message: '', error: error?.message ?? 'Retest failed.' });
    }
  }

  const overview = state.overview ?? {};
  const platform = overview.platform ?? {};
  const runtime = overview.runtime ?? {};
  const resources = overview.resources ?? {};
  const drives = Array.isArray(resources.drives) ? resources.drives : [];
  const dependencies = overview.dependencies ?? {};
  const inventorySummary = state.inventory?.summary ?? {};

  return (
    <section id="service-control-center" className="panel service-control-center" data-module="013" data-contract="provider-neutral">
      <header className="service-control-hero">
        <div>
          <p className="eyebrow">Module 013 · First-response troubleshooting</p>
          <h1>System Health &amp; API Diagnostics</h1>
          <p>
            Provider-neutral visibility into the active platform, resource use, dependencies, integrations,
            workers, deployments, and every API registered in the running ProjectPulse application.
          </p>
        </div>
        <div className="service-control-hero-actions">
          <Status value={platform.adapterStatus ?? (state.loading ? 'checking' : 'unknown')} />
          <button type="button" className="primary-action" onClick={() => load()} disabled={state.loading}>
            {state.loading ? 'Refreshing…' : 'Refresh diagnostics'}
          </button>
        </div>
      </header>

      {state.error ? <div className="service-control-alert critical" role="alert">{state.error}</div> : null}

      <section className="platform-identity-strip" aria-label="Active platform">
        <article><span>Provider</span><strong>{platform.displayName ?? 'Checking…'}</strong><small>{title(platform.adapter)}</small></article>
        <article><span>Environment</span><strong>{title(platform.environment)}</strong><small>Generic environment contract</small></article>
        <article><span>Region</span><strong>{platform.region ?? 'Not reported'}</strong><small>{title(platform.workloadKind)}</small></article>
        <article><span>Release</span><strong title={runtime.releaseSha}>{runtime.releaseSha?.slice(0, 12) ?? 'Not recorded'}</strong><small>Version {runtime.applicationVersion ?? 'Not recorded'}</small></article>
        <article><span>Uptime</span><strong>{duration(runtime.uptimeSeconds)}</strong><small>Started {dateTime(runtime.processStartedAt)}</small></article>
        <article><span>Deployment</span><strong>{runtime.deployment ?? 'Not reported'}</strong><small>{runtime.lastDeploymentAt ? dateTime(runtime.lastDeploymentAt) : 'Deployment time not reported'}</small></article>
      </section>

      <section className="service-control-card">
        <div className="service-control-card-header">
          <div><p className="eyebrow">Resource usage</p><h2>Compute, memory, and storage</h2></div>
          <span>{runtime.logicalProcessorCount ?? 0} logical CPU</span>
        </div>
        <div className="resource-metric-grid">
          <article><span>CPU</span><strong>{Number(resources.cpuPercent ?? 0).toFixed(1)}%</strong><small>Process average since start</small></article>
          <article><span>Process memory</span><strong>{bytes(resources.processWorkingSetBytes)}</strong><small>{bytes(resources.processPrivateMemoryBytes)} private</small></article>
          <article><span>Container memory</span><strong>{bytes(resources.containerMemoryCurrentBytes)}</strong><small>{bytes(resources.containerMemoryLimitBytes)} limit</small></article>
          <article><span>Available RAM</span><strong>{bytes(resources.availableMemoryBytes)}</strong><small>{bytes(resources.totalMemoryBytes)} total reported</small></article>
          <article><span>Managed heap</span><strong>{bytes(resources.managedHeapBytes)}</strong><small>.NET managed memory</small></article>
        </div>
        <div className="service-control-card-header" data-module-013-volume-control="collapsed-by-default">
          <div>
            <p className="eyebrow">Storage volumes</p>
            <h2>Volume details</h2>
            <p>{drives.length === 1 ? '1 mounted volume reported.' : `${drives.length} mounted volumes reported.`} Expand only when disk-level detail is needed.</p>
          </div>
          <button
            type="button"
            className="secondary-action"
            aria-expanded={volumeExpanded}
            aria-controls="module-013-volume-details"
            onClick={() => setVolumeExpanded((current) => !current)}
          >
            {volumeExpanded ? 'Hide volume details' : 'Show volume details'}
          </button>
        </div>
        <div id="module-013-volume-details" className="platform-table-wrap" hidden={!volumeExpanded}>
          <table>
            <thead><tr><th>Volume</th><th>Type</th><th>File system</th><th>Used</th><th>Available</th><th>Total</th></tr></thead>
            <tbody>
              {drives.map((drive) => (
                <tr key={drive.volume}><td>{drive.volume}</td><td>{title(drive.type)}</td><td>{drive.fileSystem}</td><td>{bytes(drive.usedBytes)}</td><td>{bytes(drive.availableBytes)}</td><td>{bytes(drive.totalBytes)}</td></tr>
              ))}
              {!drives.length ? <tr><td colSpan="6">Disk metrics are not exposed by the active platform adapter.</td></tr> : null}
            </tbody>
          </table>
        </div>
      </section>

      <div className="service-control-two-column">
        <section className="service-control-card">
          <div className="service-control-card-header"><div><p className="eyebrow">Core dependencies</p><h2>Database and storage</h2></div></div>
          <div className="dependency-list">
            {Object.values(dependencies).map((dependency) => (
              <article key={dependency.key}>
                <div><strong>{dependency.name}</strong><Status value={dependency.status} /></div>
                <p>{dependency.message}</p>
                <small>{dependency.latencyMs == null ? 'Latency not available' : `${dependency.latencyMs} ms`} · Checked {dateTime(dependency.checkedAt)}</small>
              </article>
            ))}
          </div>
        </section>

        <section className="service-control-card">
          <div className="service-control-card-header"><div><p className="eyebrow">External dependencies</p><h2>Integrations</h2></div></div>
          <div className="dependency-list">
            {(overview.integrations ?? []).map((integration) => (
              <article key={integration.key}>
                <div><strong>{integration.name}</strong><Status value={integration.status} /></div>
                <p>{integration.capabilities?.join(' · ') || title(integration.type)}</p>
                <small>{integration.owner} · {integration.lastCheckedAt ? dateTime(integration.lastCheckedAt) : 'No live check recorded'}</small>
              </article>
            ))}
          </div>
        </section>
      </div>

      <section className="service-control-card api-inventory-card">
        <div className="service-control-card-header">
          <div>
            <p className="eyebrow">Running endpoint registry</p>
            <h2>API inventory</h2>
            <p>Click an API to review dependencies, failures, correlation evidence, troubleshooting steps, and supported actions.</p>
          </div>
          <div className="api-summary-pills">
            <span>{inventorySummary.total ?? 0} APIs</span>
            <span>{inventorySummary.healthy ?? 0} healthy</span>
            <span>{inventorySummary.failed ?? 0} failed</span>
            <span>{inventorySummary.notObserved ?? 0} not observed</span>
          </div>
        </div>

        <div className="api-filter-grid">
          <label><span>Search</span><input type="search" value={filters.search} onChange={(event) => setFilters((current) => ({ ...current, search: event.target.value }))} placeholder="Module, method, path, purpose" /></label>
          <label><span>Owning module</span><select value={filters.module} onChange={(event) => setFilters((current) => ({ ...current, module: event.target.value }))}><option value="all">All modules</option>{modules.map(([code, label]) => <option value={code} key={code}>{label}</option>)}</select></label>
          <label><span>Status</span><select value={filters.status} onChange={(event) => setFilters((current) => ({ ...current, status: event.target.value }))}><option value="all">All statuses</option><option value="healthy">Healthy</option><option value="failed">Failed</option><option value="rejected">Rejected</option><option value="not_observed">Not observed</option></select></label>
          <button type="button" className="secondary-action" onClick={() => setFilters({ search: '', module: 'all', status: 'all' })}>Clear filters</button>
        </div>

        <div className="platform-table-wrap api-table-wrap">
          <table>
            <thead><tr><th>Module</th><th>Method</th><th>API path</th><th>Purpose</th><th>Status</th><th>Latency</th><th>Last failure</th></tr></thead>
            <tbody>
              {filteredApis.map((api) => (
                <tr key={api.apiId} tabIndex="0" role="button" onClick={() => openApi(api)} onKeyDown={(event) => { if (event.key === 'Enter' || event.key === ' ') void openApi(api); }}>
                  <td><strong>{api.moduleCode}</strong><small>{api.moduleName}</small></td>
                  <td><code>{api.method}</code></td>
                  <td><code>{api.path}</code></td>
                  <td>{api.purpose}</td>
                  <td><Status value={api.currentStatus} /></td>
                  <td>{api.responseTimeMs == null ? '—' : `${api.responseTimeMs} ms`}</td>
                  <td>{dateTime(api.lastFailureAt)}</td>
                </tr>
              ))}
              {!filteredApis.length ? <tr><td colSpan="7">No registered APIs match these filters.</td></tr> : null}
            </tbody>
          </table>
        </div>
      </section>

      <section className="service-control-card">
        <div className="service-control-card-header"><div><p className="eyebrow">Capability-aware actions</p><h2>What this deployment can do</h2></div></div>
        <div className="capability-grid">
          {(overview.capabilities ?? []).map((capability) => (
            <article key={capability.key}><div><strong>{capability.name}</strong><Status value={capability.state} /></div><p>{capability.message}</p></article>
          ))}
        </div>
      </section>

      <section className="service-control-card">
        <div className="service-control-card-header"><div><p className="eyebrow">Runtime workers</p><h2>Background processes</h2></div></div>
        <div className="worker-grid">
          {(overview.workers ?? []).map((worker) => <article key={worker.key}><strong>{worker.name}</strong><Status value={worker.status} /><p>{worker.restartMessage}</p><small>{worker.source}</small></article>)}
          {!overview.workers?.length ? <p className="service-control-muted">No hosted workers were reported by the runtime.</p> : null}
        </div>
      </section>

      {selected.data || selected.loading ? (
        <div className="api-diagnostic-backdrop" role="presentation" onMouseDown={(event) => { if (event.target === event.currentTarget) setSelected({ loading: false, data: null, error: '' }); }}>
          <aside className="api-diagnostic-drawer" role="dialog" aria-modal="true" aria-label="API diagnostic details">
            <header>
              <div><p className="eyebrow">API diagnostic</p><h2>{selected.data?.api?.method} {selected.data?.api?.path}</h2><span>Module {selected.data?.api?.moduleCode} · {selected.data?.api?.moduleName}</span></div>
              <button type="button" className="secondary-action" onClick={() => setSelected({ loading: false, data: null, error: '' })}>Close</button>
            </header>
            {selected.loading ? <p>Loading diagnostic evidence…</p> : null}
            {selected.error ? <div className="service-control-alert critical">{selected.error}</div> : null}
            {selected.data?.api ? (
              <>
                <div className="api-detail-summary"><Status value={selected.data.api.currentStatus} /><span>{selected.data.api.responseTimeMs == null ? 'Latency not observed' : `${selected.data.api.responseTimeMs} ms`}</span><span>Correlation: {selected.data.api.correlationId || 'Not observed'}</span></div>
                <dl className="api-detail-list"><div><dt>Purpose</dt><dd>{selected.data.api.purpose}</dd></div><div><dt>Authentication</dt><dd>{selected.data.api.authenticationRequirement}</dd></div><div><dt>Permission</dt><dd>{selected.data.api.permissionRequirement}</dd></div><div><dt>Last success</dt><dd>{dateTime(selected.data.api.lastSuccessfulRequestAt)}</dd></div><div><dt>Last failure</dt><dd>{dateTime(selected.data.api.lastFailureAt)}</dd></div><div><dt>Error code</dt><dd>{selected.data.api.lastErrorCode || 'None observed'}</dd></div></dl>
                <section><h3>Dependencies</h3><div className="diagnostic-chip-row">{(selected.data.dependentServices ?? selected.data.api.dependencies ?? []).map((dependency) => <span key={dependency}>{dependency}</span>)}</div></section>
                <section><h3>Suggested troubleshooting</h3><ol className="troubleshooting-list">{(selected.data.suggestedTroubleshooting ?? []).map((step) => <li key={step.order}><strong>{step.action}</strong><span>{step.detail}</span></li>)}</ol></section>
                <section><h3>Recent failures and logs</h3><div className="diagnostic-event-list">{(selected.data.recentFailures ?? []).map((event) => <article key={event.evidenceId}><div><Status value={event.status} /><strong>{event.errorCode || `HTTP ${event.statusCode}`}</strong></div><p>{event.message}</p><small>{dateTime(event.observedAt)} · {event.correlationId}</small></article>)}{!selected.data.recentFailures?.length ? <p>No recent failure evidence is stored for this API.</p> : null}</div></section>
                <section className="api-action-panel"><h3>Actions</h3><p>{selected.data.api.retestReason}</p><button type="button" className="primary-action" disabled={selected.data.api.retestCapability !== 'supported' || retest.loading} onClick={() => retestApi(selected.data.api)}>{retest.loading && retest.apiId === selected.data.api.apiId ? 'Retesting…' : 'Retest API'}</button>{retest.message && retest.apiId === selected.data.api.apiId ? <div className="service-control-alert healthy">{retest.message}</div> : null}{retest.error && retest.apiId === selected.data.api.apiId ? <div className="service-control-alert critical">{retest.error}</div> : null}<div className="unsupported-route-restart"><strong>Restart this HTTP route</strong><span>Not supported by the current deployment model. Routes share one API process.</span></div></section>
              </>
            ) : null}
          </aside>
        </div>
      ) : null}
    </section>
  );
}
