import { useCallback, useEffect, useMemo, useState } from 'react';
import { usSignalLogoDataUrl } from './assets/usSignalLogoData.js';
import './financial-operations-recovery-workspace.css';
import './projectpulse-module-standard.css';

const moduleMetadata = Object.freeze({
  '039': {
    eyebrow: 'Module 039 · Billing readiness recovery',
    title: 'Billing Readiness & Reconciliation Recovery',
    summary: 'Review approved time, current expenses, package readiness, exact source failures, and retry evidence without losing healthy billing content.'
  },
  '040': {
    eyebrow: 'Module 040 · Project closeout recovery',
    title: 'Project Closeout Recovery',
    summary: 'Keep closeout decisions usable while showing the precise billing, time, financial, notification, or source blocker that still requires action.'
  },
  '041': {
    eyebrow: 'Module 041 · Closeout notification recovery',
    title: 'Closeout Notification Recovery',
    summary: 'Review Group 4 dispatch evidence and Module 065 delivery state. Mail routing and delivery are never reimplemented in this workspace.'
  },
  '042': {
    eyebrow: 'Module 042 · Billing recovery',
    title: 'Invoice & Billing Recovery',
    summary: 'Reconcile approved time with an intentional current-expense summary and source-level recovery. Module 005 remains a separate upload workspace.'
  }
});

function sessionToken(authSession) {
  return authSession?.sessionToken
    ?? authSession?.token
    ?? authSession?.accessToken
    ?? window.localStorage.getItem('projectPulseSessionToken')
    ?? window.sessionStorage.getItem('projectPulseSessionToken')
    ?? '';
}

function requestHeaders(authSession, body = false) {
  const token = sessionToken(authSession);
  return {
    Accept: 'application/json',
    ...(body ? { 'Content-Type': 'application/json' } : {}),
    ...(token ? {
      Authorization: `Bearer ${token}`,
      'X-ProjectPulse-Session': token,
      'X-Project-Pulse-Session': token,
      'X-Session-Token': token
    } : {})
  };
}

async function api(path, authSession, options = {}) {
  const response = await fetch(path, {
    credentials: 'include',
    cache: 'no-store',
    ...options,
    headers: {
      ...requestHeaders(authSession, Boolean(options.body)),
      ...(options.headers ?? {})
    }
  });
  const contentType = response.headers.get('content-type') ?? '';
  const payload = contentType.includes('application/json')
    ? await response.json().catch(() => null)
    : await response.text().catch(() => '');
  if (!response.ok) {
    const error = new Error(
      payload?.message
      ?? payload?.detail
      ?? payload?.status
      ?? (typeof payload === 'string' && payload)
      ?? `${path} returned HTTP ${response.status}`
    );
    error.status = response.status;
    error.payload = payload;
    throw error;
  }
  return { response, payload };
}

function words(value) {
  return String(value ?? '')
    .replaceAll('_', ' ')
    .replace(/\b\w/g, (character) => character.toUpperCase());
}

function text(value, fallback = 'Not recorded') {
  const normalized = String(value ?? '').trim();
  if (!normalized || ['not_recorded', 'not_available', 'not_configured'].includes(normalized.toLowerCase())) return fallback;
  return normalized;
}

function money(value) {
  const number = Number(value);
  return Number.isFinite(number)
    ? number.toLocaleString(undefined, { style: 'currency', currency: 'USD' })
    : 'Not available';
}

function number(value, digits = 2) {
  const parsed = Number(value);
  return Number.isFinite(parsed)
    ? parsed.toLocaleString(undefined, { maximumFractionDigits: digits })
    : 'Not available';
}

function date(value) {
  if (!value) return 'Not recorded';
  const parsed = new Date(`${String(value).slice(0, 10)}T00:00:00`);
  return Number.isNaN(parsed.getTime()) ? String(value) : parsed.toLocaleDateString();
}

function dateTime(value) {
  if (!value) return 'Not recorded';
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? String(value) : parsed.toLocaleString();
}

function statusTone(value) {
  const normalized = String(value ?? '').toLowerCase();
  if (['failed', 'unavailable', 'critical', 'over_budget', 'blocked', 'source_unavailable'].some((item) => normalized.includes(item))) return 'critical';
  if (['partial', 'warning', 'approaching', 'held', 'missing', 'acknowledged'].some((item) => normalized.includes(item))) return 'warning';
  if (['healthy', 'complete', 'ready', 'sent', 'resolved', 'succeeded'].some((item) => normalized.includes(item))) return 'healthy';
  return 'neutral';
}

function Status({ value, children }) {
  return <span className={`group5-status ${statusTone(value)}`}>{children ?? words(value || 'unknown')}</span>;
}

function EmptyState({ title, children }) {
  return (
    <div className="group5-empty-state">
      <strong>{title}</strong>
      <span>{children}</span>
    </div>
  );
}

function SourceGrid({ sources = [], busySource, onRetry, canRetry = false, compact = false }) {
  // PR467_COMPACT_SOURCE_HEALTH
  if (compact) {
    return (
      <section className="financial-operations-recovery-compact" data-module="039">
        <header>
          <div><p className="eyebrow">Source health & recovery</p><h3>Independent billing data sources</h3></div>
          <span className="badge active">Source-isolated</span>
        </header>
        <section className="group5-card">
      <div className="group5-section-heading">
        <div>
          <p className="group5-eyebrow">Source health and recovery</p>
          <h3>Independent data sources</h3>
          <p>An unavailable source does not clear successful project, report, closeout, or billing content.</p>
        </div>
        <Status value={sources.some((item) => item.status === 'unavailable') ? 'partial' : 'healthy'} />
      </div>
      <div className="group5-source-grid">
        {sources.map((source) => (
          <article key={source.key} className={`group5-source-card ${statusTone(source.status)}`}>
            <div className="group5-source-heading">
              <div>
                <span>{source.required ? 'Required source' : 'Optional source'}</span>
                <strong>{source.name}</strong>
              </div>
              <Status value={source.status} />
            </div>
            <p>{source.message}</p>
            <dl>
              <div><dt>Records</dt><dd>{source.recordCount ?? 0}</dd></div>
              <div><dt>Observed</dt><dd>{dateTime(source.observedAt)}</dd></div>
              <div><dt>Diagnostic</dt><dd><code>{text(source.diagnosticCode, 'None')}</code></dd></div>
            </dl>
            {canRetry ? (
              <button
                type="button"
                className="group5-secondary"
                disabled={busySource === source.key}
                onClick={() => onRetry(source.key)}
              >
                {busySource === source.key ? 'Retrying…' : `Retry ${source.name}`}
              </button>
            ) : null}
          </article>
        ))}
      </div>
    </section>
      </section>
    );
  }

  return (
    <section className="group5-card">
      <div className="group5-section-heading">
        <div>
          <p className="group5-eyebrow">Source health and recovery</p>
          <h3>Independent data sources</h3>
          <p>An unavailable source does not clear successful project, report, closeout, or billing content.</p>
        </div>
        <Status value={sources.some((item) => item.status === 'unavailable') ? 'partial' : 'healthy'} />
      </div>
      <div className="group5-source-grid">
        {sources.map((source) => (
          <article key={source.key} className={`group5-source-card ${statusTone(source.status)}`}>
            <div className="group5-source-heading">
              <div>
                <span>{source.required ? 'Required source' : 'Optional source'}</span>
                <strong>{source.name}</strong>
              </div>
              <Status value={source.status} />
            </div>
            <p>{source.message}</p>
            <dl>
              <div><dt>Records</dt><dd>{source.recordCount ?? 0}</dd></div>
              <div><dt>Observed</dt><dd>{dateTime(source.observedAt)}</dd></div>
              <div><dt>Diagnostic</dt><dd><code>{text(source.diagnosticCode, 'None')}</code></dd></div>
            </dl>
            {canRetry ? (
              <button
                type="button"
                className="group5-secondary"
                disabled={busySource === source.key}
                onClick={() => onRetry(source.key)}
              >
                {busySource === source.key ? 'Retrying…' : `Retry ${source.name}`}
              </button>
            ) : null}
          </article>
        ))}
      </div>
    </section>
  );
}

function valueForColumn(row, column) {
  const value = row?.[column.key];
  if (column.dataType === 'currency') return money(value);
  if (column.dataType === 'number') return number(value);
  if (column.dataType === 'percent') return value === null || value === undefined ? 'Not available' : `${number(value)}%`;
  if (column.dataType === 'date') return date(value);
  if (column.dataType === 'datetime') return dateTime(value);
  if (column.dataType === 'status') return <Status value={value} />;
  return text(value);
}

function ReportTable({ definition, result }) {
  const rows = result?.rows ?? [];
  const columns = definition?.columns ?? [];
  if (!rows.length) {
    return (
      <EmptyState title={result?.resultStatus === 'source_unavailable' ? 'Required source unavailable' : 'No matching report data'}>
        {result?.message ?? 'Run a report or adjust the filters.'}
      </EmptyState>
    );
  }
  return (
    <div className="group5-table-wrap">
      <table className="group5-table">
        <thead>
          <tr>{columns.map((column) => <th key={column.key} title={column.description}>{column.label}</th>)}</tr>
        </thead>
        <tbody>
          {rows.map((row, index) => (
            <tr key={`${row.projectId ?? row.dispatchId ?? 'row'}-${index}`}>
              {columns.map((column) => <td key={column.key}>{valueForColumn(row, column)}</td>)}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function ReportCenter({ authSession }) {
  const [catalogState, setCatalogState] = useState({ loading: true, data: null, error: '' });
  const [historyState, setHistoryState] = useState({ loading: true, data: null, error: '' });
  const [selectedCode, setSelectedCode] = useState('project_financial_health');
  const [filters, setFilters] = useState({ search: '', customer: '', status: '', dateFrom: '', dateTo: '' });
  const [resultState, setResultState] = useState({ loading: false, persisted: false, runId: '', data: null, error: '' });

  const load = useCallback(async () => {
    setCatalogState((current) => ({ ...current, loading: true, error: '' }));
    setHistoryState((current) => ({ ...current, loading: true, error: '' }));
    const [catalog, history] = await Promise.allSettled([
      api('/api/financial-operations/reports/catalog', authSession),
      api('/api/financial-operations/reports/history?limit=50', authSession)
    ]);
    if (catalog.status === 'fulfilled') {
      setCatalogState({ loading: false, data: catalog.value.payload, error: '' });
      const first = catalog.value.payload?.reports?.[0]?.code;
      if (first && !selectedCode) setSelectedCode(first);
    } else {
      setCatalogState({ loading: false, data: null, error: catalog.reason?.message ?? 'Unable to load the report catalog.' });
    }
    if (history.status === 'fulfilled') {
      setHistoryState({ loading: false, data: history.value.payload, error: '' });
    } else {
      setHistoryState({ loading: false, data: null, error: history.reason?.message ?? 'Report history is unavailable.' });
    }
  }, [authSession, selectedCode]);

  useEffect(() => { void load(); }, [load]);

  const reports = catalogState.data?.reports ?? [];
  const selectedDefinition = reports.find((report) => report.code === selectedCode) ?? reports[0] ?? null;
  const customerOptions = useMemo(() => {
    const values = resultState.data?.rows?.map((row) => row.customer).filter(Boolean) ?? [];
    return [...new Set(values)].sort((a, b) => a.localeCompare(b));
  }, [resultState.data]);

  async function execute(persisted) {
    setResultState((current) => ({ ...current, loading: true, error: '' }));
    try {
      const { payload } = await api(
        `/api/financial-operations/reports/${persisted ? 'run' : 'preview'}`,
        authSession,
        {
          method: 'POST',
          body: JSON.stringify({
            reportCode: selectedDefinition?.code,
            search: filters.search,
            customer: filters.customer,
            status: filters.status,
            dateFrom: filters.dateFrom || null,
            dateTo: filters.dateTo || null,
            limit: 500
          })
        }
      );
      setResultState({
        loading: false,
        persisted: Boolean(payload?.persisted),
        runId: payload?.runId ?? '',
        data: payload?.result ?? null,
        error: ''
      });
      if (persisted) {
        const history = await api('/api/financial-operations/reports/history?limit=50', authSession);
        setHistoryState({ loading: false, data: history.payload, error: '' });
      }
    } catch (error) {
      setResultState((current) => ({ ...current, loading: false, error: error.message ?? 'Unable to run the report.' }));
    }
  }

  /* GROUP_5_AUTHENTICATED_REPORT_EXPORT_START */
  async function downloadRun(runId) {
    if (!runId) return;
    setResultState((current) => ({ ...current, error: '' }));
    try {
      const response = await fetch(`/api/financial-operations/reports/runs/${runId}/export`, {
        method: 'GET',
        credentials: 'include',
        cache: 'no-store',
        headers: requestHeaders(authSession)
      });
      if (!response.ok) {
        const contentType = response.headers.get('content-type') ?? '';
        const payload = contentType.includes('application/json')
          ? await response.json().catch(() => null)
          : await response.text().catch(() => '');
        throw new Error(
          payload?.message
          ?? payload?.detail
          ?? payload?.status
          ?? (typeof payload === 'string' && payload)
          ?? `Report export returned HTTP ${response.status}.`
        );
      }

      const blob = await response.blob();
      const disposition = response.headers.get('content-disposition') ?? '';
      const match = disposition.match(/filename\*?=(?:UTF-8''|")?([^";]+)/i);
      const fileName = match?.[1]
        ? decodeURIComponent(match[1].replaceAll('"', ''))
        : `projectpulse-financial-report-${runId}.csv`;
      const url = URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url;
      link.download = fileName;
      document.body.appendChild(link);
      link.click();
      link.remove();
      URL.revokeObjectURL(url);
    } catch (error) {
      setResultState((current) => ({
        ...current,
        error: error instanceof Error ? error.message : 'Unable to export the report.'
      }));
    }
  }
  /* GROUP_5_AUTHENTICATED_REPORT_EXPORT_END */

  return (
    <div className="group5-report-layout">
      <section className="group5-card group5-report-catalog">
        <div className="group5-section-heading">
          <div>
            <p className="group5-eyebrow">Actual report catalog</p>
            <h3>Search, preview, run, and export</h3>
            <p>Every result is generated from current role-scoped ProjectPulse sources. Placeholder reports are excluded.</p>
          </div>
          <button type="button" className="group5-secondary" onClick={load} disabled={catalogState.loading}>Refresh</button>
        </div>
        {catalogState.error ? <div className="group5-alert critical">{catalogState.error}</div> : null}
        <div className="group5-report-picker">
          {reports.map((report) => (
            <button
              type="button"
              className={selectedDefinition?.code === report.code ? 'active' : ''}
              key={report.code}
              onClick={() => setSelectedCode(report.code)}
            >
              <strong>{report.name}</strong>
              <span>{report.description}</span>
              <small>Modules {report.modules.join(', ')}</small>
            </button>
          ))}
        </div>
      </section>

      <section className="group5-card group5-report-command">
        <div className="group5-section-heading">
          <div>
            <p className="group5-eyebrow">Report filters</p>
            <h3>{selectedDefinition?.name ?? 'Select a report'}</h3>
            <p>{selectedDefinition?.description}</p>
          </div>
          {resultState.data ? <Status value={resultState.data.resultStatus} /> : null}
        </div>
        <div className="group5-filter-grid">
          <label>Search<input type="search" value={filters.search} onChange={(event) => setFilters((current) => ({ ...current, search: event.target.value }))} placeholder="Customer, project, PM, contract, SELL…" /></label>
          <label>Customer<select value={filters.customer} onChange={(event) => setFilters((current) => ({ ...current, customer: event.target.value }))}><option value="">All role-scoped customers</option>{customerOptions.map((value) => <option key={value}>{value}</option>)}</select></label>
          <label>Status<input value={filters.status} onChange={(event) => setFilters((current) => ({ ...current, status: event.target.value }))} placeholder="Project or budget status" /></label>
          <label>Date from<input type="date" value={filters.dateFrom} onChange={(event) => setFilters((current) => ({ ...current, dateFrom: event.target.value }))} /></label>
          <label>Date to<input type="date" value={filters.dateTo} onChange={(event) => setFilters((current) => ({ ...current, dateTo: event.target.value }))} /></label>
        </div>
        <div className="group5-action-row">
          <button type="button" className="group5-secondary" disabled={!selectedDefinition || resultState.loading} onClick={() => execute(false)}>Preview</button>
          <button type="button" className="group5-primary" disabled={!selectedDefinition || resultState.loading} onClick={() => execute(true)}>{resultState.loading ? 'Running…' : 'Run and record history'}</button>
          <button type="button" className="group5-secondary" disabled={!resultState.runId} onClick={() => downloadRun(resultState.runId)}>Export CSV</button>
        </div>
        {resultState.error ? <div className="group5-alert critical">{resultState.error}</div> : null}
        {resultState.data ? <div className="group5-result-message"><Status value={resultState.data.resultStatus} /><span>{resultState.data.message}</span></div> : null}
        <ReportTable definition={selectedDefinition} result={resultState.data} />
      </section>

      <SourceGrid
        sources={resultState.data?.sources ?? catalogState.data?.sources ?? []}
        canRetry={false}
      />

      <section className="group5-card">
        <div className="group5-section-heading">
          <div>
            <p className="group5-eyebrow">Report run history</p>
            <h3>Recorded report execution</h3>
            <p>History stores the filters, exact source states, result status, and returned rows for later export.</p>
          </div>
          <Status value={historyState.error ? 'unavailable' : 'healthy'} />
        </div>
        {historyState.error ? <div className="group5-alert warning">{historyState.error}</div> : null}
        <div className="group5-history-list">
          {(historyState.data?.history ?? []).map((run) => (
            <article key={run.runId}>
              <div><strong>{run.reportName}</strong><span>{dateTime(run.startedAt)} · {run.rowCount} rows</span></div>
              <Status value={run.resultStatus} />
              <button type="button" className="group5-secondary" onClick={() => downloadRun(run.runId)}>Export</button>
            </article>
          ))}
          {!historyState.loading && !(historyState.data?.history ?? []).length ? <EmptyState title="No recorded report runs">Run a report to create role-scoped history.</EmptyState> : null}
        </div>
      </section>
    </div>
  );
}

function Workbench({ authSession }) {
  const [state, setState] = useState({ loading: true, data: null, error: '' });
  const [busy, setBusy] = useState('');
  const [notes, setNotes] = useState({});
  const [statusFilter, setStatusFilter] = useState('open');
  const [search, setSearch] = useState('');
  const [priorityFilter, setPriorityFilter] = useState('');
  const [page, setPage] = useState(1);

  const load = useCallback(async () => {
    setState((current) => ({ ...current, loading: true, error: '' }));
    try {
      const { payload } = await api(`/api/financial-operations/workbench?status=${encodeURIComponent(statusFilter)}&limit=500`, authSession);
      setState({ loading: false, data: payload, error: '' });
    } catch (error) {
      setState({ loading: false, data: null, error: error.message ?? 'Unable to load the recovery queue.' });
    }
  }, [authSession, statusFilter]);

  useEffect(() => { void load(); }, [load]);

  async function refresh() {
    setBusy('refresh');
    try {
      await api('/api/financial-operations/workbench/refresh', authSession, { method: 'POST', body: '{}' });
      await load();
    } catch (error) {
      setState((current) => ({ ...current, error: error.message ?? 'Unable to refresh the recovery queue.' }));
    } finally { setBusy(''); }
  }

  async function act(item, action) {
    const note = notes[item.workItemId] ?? '';
    setBusy(`${item.workItemId}:${action}`);
    try {
      await api(`/api/financial-operations/workbench/${item.workItemId}/${action}`, authSession, { method: 'POST', body: JSON.stringify({ note }) });
      setNotes((current) => ({ ...current, [item.workItemId]: '' }));
      await load();
    } catch (error) {
      setState((current) => ({ ...current, error: error.message ?? 'Unable to update the recovery work item.' }));
    } finally { setBusy(''); }
  }

  async function retrySource(key) {
    setBusy(`source:${key}`);
    try {
      await api(`/api/financial-operations/sources/${encodeURIComponent(key)}/retry`, authSession, { method: 'POST', body: '{}' });
      await load();
    } catch (error) {
      setState((current) => ({ ...current, error: error.message ?? 'Unable to retry the source.' }));
    } finally { setBusy(''); }
  }

  const items = (state.data?.items ?? []).filter((item) => {
    if (priorityFilter && item.priority !== priorityFilter) return false;
    const query = search.trim().toLowerCase();
    return !query || [item.title, item.detail, item.moduleCode, item.itemType, item.ownerName, item.sourceKey].some((value) => String(value ?? '').toLowerCase().includes(query));
  });
  const pageSize = 25;
  const pageCount = Math.max(1, Math.ceil(items.length / pageSize));
  const visibleItems = items.slice((Math.min(page, pageCount) - 1) * pageSize, Math.min(page, pageCount) * pageSize);
  return (
    <div className="group5-workbench-layout">
      <section className="group5-card">
        <div className="group5-section-heading">
          <div>
            <p className="group5-eyebrow">Module 031 · Productivity workspace</p>
            <h3>Financial Operations Workbench</h3>
            <p>One accountable queue for source failures, budget risk, billing blockers, closeout blockers, reconciliation exceptions, and notification failures.</p>
          </div>
          <div className="group5-action-row">
            <input type="search" value={search} onChange={(event) => { setSearch(event.target.value); setPage(1); }} placeholder="Search recovery queue" aria-label="Search financial recovery queue" />
            <select aria-label="Work item priority" value={priorityFilter} onChange={(event) => { setPriorityFilter(event.target.value); setPage(1); }}><option value="">All priorities</option><option value="critical">Critical</option><option value="high">High</option><option value="medium">Medium</option><option value="low">Low</option></select>
            <select aria-label="Work item status" value={statusFilter} onChange={(event) => setStatusFilter(event.target.value)}>
              <option value="open">Open</option><option value="acknowledged">Acknowledged</option><option value="resolved">Resolved</option><option value="dismissed">Dismissed</option><option value="">All</option>
            </select>
            <button type="button" className="group5-primary" disabled={busy === 'refresh'} onClick={refresh}>{busy === 'refresh' ? 'Refreshing…' : 'Refresh recovery queue'}</button>
          </div>
        </div>
        {state.error ? <div className="group5-alert critical">{state.error}</div> : null}
        <div className="group5-summary-grid">
          <article><span>Total</span><strong>{state.data?.summary?.total ?? 0}</strong><small>Current filter</small></article>
          <article><span>Critical</span><strong>{state.data?.summary?.critical ?? 0}</strong><small>Immediate attention</small></article>
          <article><span>High</span><strong>{state.data?.summary?.high ?? 0}</strong><small>Priority recovery</small></article>
          <article><span>Source failures</span><strong>{state.data?.summary?.sourceFailures ?? 0}</strong><small>Retry independently</small></article>
        </div>
        <div className="group5-work-item-list">
          {visibleItems.map((item) => (
            <article key={item.workItemId} className={`group5-work-item ${item.priority}`}>
              <div className="group5-work-item-main">
                <div className="group5-work-item-heading"><div><span>Module {item.moduleCode} · {words(item.itemType)}</span><strong>{item.title}</strong></div><Status value={item.priority}>{words(item.priority)}</Status></div>
                <p>{item.detail}</p>
                <dl><div><dt>Owner</dt><dd>{text(item.ownerName, 'Not assigned')}</dd></div><div><dt>Source</dt><dd>{text(item.sourceKey)}</dd></div><div><dt>First detected</dt><dd>{dateTime(item.firstDetectedAt)}</dd></div><div><dt>Last detected</dt><dd>{dateTime(item.lastDetectedAt)}</dd></div></dl>
              </div>
              <div className="group5-work-item-actions">
                <textarea value={notes[item.workItemId] ?? ''} onChange={(event) => setNotes((current) => ({ ...current, [item.workItemId]: event.target.value }))} placeholder="Required recovery or resolution note" />
                <div><button type="button" className="group5-secondary" disabled={busy.startsWith(item.workItemId)} onClick={() => act(item, 'acknowledged')}>Acknowledge</button><button type="button" className="group5-primary" disabled={busy.startsWith(item.workItemId)} onClick={() => act(item, 'resolved')}>Resolve</button></div>
              </div>
            </article>
          ))}
          {!state.loading && !items.length ? <EmptyState title="No work items in this view">Refresh the queue or select another status.</EmptyState> : null}
        </div>
        {items.length > pageSize ? <div className="group5-pagination"><button type="button" className="group5-secondary" disabled={page <= 1} onClick={() => setPage((value) => Math.max(1, value - 1))}>Previous</button><span>Page {Math.min(page, pageCount)} of {pageCount} · {items.length} items</span><button type="button" className="group5-secondary" disabled={page >= pageCount} onClick={() => setPage((value) => Math.min(pageCount, value + 1))}>Next</button></div> : null}
      </section>
      <SourceGrid sources={state.data?.sources ?? []} canRetry={Boolean(state.data?.capabilities?.canRetrySources)} busySource={busy.replace('source:', '')} onRetry={retrySource} />
    </div>
  );
}

function ModuleRecovery({ moduleCode, authSession, compact = false }) {
  const metadata = moduleMetadata[moduleCode] ?? moduleMetadata['039'];
  const [state, setState] = useState({ loading: true, data: null, error: '' });
  const [busySource, setBusySource] = useState('');

  const load = useCallback(async () => {
    setState((current) => ({ ...current, loading: true, error: '' }));
    try {
      const { payload } = await api(`/api/financial-operations/modules/${moduleCode}`, authSession);
      setState({ loading: false, data: payload, error: '' });
    } catch (error) {
      setState({ loading: false, data: null, error: error.message ?? `Unable to load Module ${moduleCode} recovery context.` });
    }
  }, [authSession, moduleCode]);

  useEffect(() => { void load(); }, [load]);

  async function retrySource(key) {
    setBusySource(key);
    try {
      await api(`/api/financial-operations/sources/${encodeURIComponent(key)}/retry`, authSession, { method: 'POST', body: '{}' });
      await load();
    } catch (error) {
      setState((current) => ({ ...current, error: error.message ?? 'Unable to retry the source.' }));
    } finally { setBusySource(''); }
  }

  const projects = state.data?.projects ?? [];
  return (
    <div className="group5-module-layout" data-module-code={moduleCode}>
      <section className="group5-card group5-module-summary">
        <div className="group5-section-heading">
          <div><p className="group5-eyebrow">{metadata.eyebrow}</p><h3>{metadata.title}</h3><p>{metadata.summary}</p></div>
          <div className="group5-action-row"><Status value={state.data?.status ?? (state.error ? 'unavailable' : 'healthy')} /><button type="button" className="group5-secondary" onClick={load} disabled={state.loading}>{state.loading ? 'Refreshing…' : 'Refresh module sources'}</button></div>
        </div>
        {state.error ? <div className="group5-alert critical">{state.error}</div> : null}
        {moduleCode === '041' ? <div className="group5-boundary-note"><strong>Mail ownership</strong><span>{state.data?.module041MailOwner ?? 'Group 4 routing and Module 065 delivery.'}</span></div> : null}
        {moduleCode === '042' ? <div className="group5-boundary-note"><strong>Expense boundary</strong><span>{state.data?.module042ExpenseBoundary ?? 'Current expense summary only; Module 005 remains separate.'}</span></div> : null}
        <div className="group5-module-project-grid">
          {projects.slice(0, 50).map((project) => (
            <article key={project.projectId}>
              <div className="group5-project-heading"><div><span>{project.customerName}</span><strong>{project.projectCode} · {project.projectName}</strong></div><Status value={project.budgetStatus} /></div>
              <dl>
                <div><dt>Project Manager</dt><dd>{text(project.projectManagerName)}</dd></div>
                <div><dt>Approved hours</dt><dd>{number(project.approvedHours)}</dd></div>
                <div><dt>Used / planned</dt><dd>{number(project.usedHours)} / {number(project.plannedHours)}</dd></div>
                <div><dt>Current expenses</dt><dd>{money(project.expenseSummary?.total)}</dd></div>
                <div><dt>Forecast</dt><dd>{money(project.forecastedFinalCost)}</dd></div>
                <div><dt>Variance</dt><dd>{money(project.currentVariance)}</dd></div>
                <div><dt>Billing readiness</dt><dd><Status value={project.billingReadiness?.reviewStatus ?? 'not_recorded'} /></dd></div>
                <div><dt>Closeout</dt><dd><Status value={project.closeout?.closeoutStatus ?? 'not_started'} /></dd></div>
                <div><dt>Notifications</dt><dd><Status value={project.notificationSummary?.latest?.deliveryStatus ?? 'not_recorded'} /></dd></div>
              </dl>
              {moduleCode === '042' && (project.expenseSummary?.latest ?? []).length ? (
                <details><summary>Current expense drill-down ({project.expenseSummary.count})</summary><div className="group5-expense-list">{project.expenseSummary.latest.map((expense) => <div key={expense.uploadId}><strong>{expense.ownerName}</strong><span>{date(expense.periodStart)}–{date(expense.periodEnd)}</span><span>{money(expense.totalAmount)}</span><small>{words(expense.billingTreatment)}</small></div>)}</div></details>
              ) : null}
              {project.missing?.length ? <p className="group5-missing">Missing: {project.missing.join(', ')}</p> : null}
            </article>
          ))}
        </div>
        {!state.loading && !projects.length ? <EmptyState title="No role-scoped project data">No projects matched the current server-enforced access scope.</EmptyState> : null}
      </section>
      <SourceGrid sources={state.data?.sources ?? []} canRetry busySource={busySource} onRetry={retrySource} compact={compact} />
    </div>
  );
}

export default function FinancialOperationsRecoveryWorkspace({ mode = 'reporting', moduleCode = null, authSession , compact = false }) {
  const title = mode === 'workbench'
    ? 'Financial Operations Workbench'
    : moduleCode
      ? moduleMetadata[moduleCode]?.title ?? 'Financial Recovery'
      : 'Financial Report Center';
  const summary = mode === 'workbench'
    ? 'Resolve project-financial exceptions through one accountable queue with exact source attribution, priority, ownership, retry, and immutable action evidence.'
    : moduleCode
      ? moduleMetadata[moduleCode]?.summary
      : 'Search, preview, run, export, and revisit actual ProjectPulse financial reports while every source reports its own health and retry path.';

  return (
    <section className="group5-financial-operations projectpulse-module-standard" data-projectpulse-group5="financial-recovery" data-mode={mode} data-module-code={moduleCode ?? ''}>
      <header className="group5-hero">
        <div className="group5-brand-lockup">
          <img src={usSignalLogoDataUrl} alt="US Signal" />
          <div><p className="group5-eyebrow">{mode === 'workbench' ? 'Module 031 · Financial operations' : moduleCode ? moduleMetadata[moduleCode]?.eyebrow : 'Module 030 · Reporting and analytics'}</p><h2>{title}</h2><p>{summary}</p></div>
        </div>
        <div className="group5-hero-badges"><Status value="authoritative_session_verified">Authoritative session</Status><Status value="source_isolated">Source-isolated</Status></div>
      </header>
      <aside className="group5-enterprise-note"><strong>US Signal financial operations</strong><span>Friendly page messages remain separate from sanitized diagnostic codes. One unavailable source never blanks otherwise complete work.</span></aside>
      {mode === 'reporting' && !moduleCode ? <ReportCenter authSession={authSession} /> : null}
      {mode === 'workbench' ? <Workbench authSession={authSession} /> : null}
      {moduleCode ? <ModuleRecovery moduleCode={moduleCode} authSession={authSession} compact={compact} /> : null}
    </section>
  );
}
