import { useCallback, useEffect, useMemo, useState } from 'react';
import {
  EnterpriseEmptyState,
  EnterpriseFilterBar,
  EnterpriseModulePage,
  EnterpriseStatusCard,
  EnterpriseSummaryStrip,
  EnterpriseTable,
  EnterpriseTabs,
  EnterpriseWarning
} from './enterprise/EnterpriseModulePresentation.jsx';
import './enterprise-reporting-center.css';

function sessionToken(authSession) {
  return authSession?.sessionToken
    ?? authSession?.token
    ?? authSession?.accessToken
    ?? window.localStorage.getItem('projectPulseSessionToken')
    ?? window.sessionStorage.getItem('projectPulseSessionToken')
    ?? '';
}

function headers(authSession, body = false) {
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
    headers: { ...headers(authSession, Boolean(options.body)), ...(options.headers ?? {}) }
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
      ?? `${path} returned HTTP ${response.status}.`
    );
    error.status = response.status;
    error.payload = payload;
    throw error;
  }
  return payload;
}

function words(value) {
  return String(value ?? '')
    .replaceAll('_', ' ')
    .replace(/\b\w/g, (character) => character.toUpperCase());
}

function money(value) {
  const parsed = Number(value);
  return Number.isFinite(parsed)
    ? parsed.toLocaleString(undefined, { style: 'currency', currency: 'USD' })
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
  if (['critical', 'failed', 'unavailable', 'over_budget', 'rejected', 'expired', 'blocked'].some((token) => normalized.includes(token))) return 'critical';
  if (['partial', 'warning', 'approaching', 'pending', 'held', 'rate_limited', 'expiring'].some((token) => normalized.includes(token))) return 'warning';
  if (['complete', 'healthy', 'available', 'approved', 'accepted', 'ready', 'resolved', 'sent'].some((token) => normalized.includes(token))) return 'healthy';
  return 'informational';
}

function Status({ value }) {
  return <span className={`enterprise-reporting-status ${statusTone(value)}`}>{words(value || 'not recorded')}</span>;
}

function displayValue(value, column) {
  if (value === null || value === undefined || value === '') return 'Not available';
  if (column.dataType === 'currency') return money(value);
  if (column.dataType === 'number') return number(value);
  if (column.dataType === 'percent') return `${number(value)}%`;
  if (column.dataType === 'date') return date(value);
  if (column.dataType === 'datetime') return dateTime(value);
  if (column.dataType === 'boolean') return value === true || String(value).toLowerCase() === 'true' ? 'Yes' : 'No';
  if (column.dataType === 'status') return <Status value={value} />;
  if (Array.isArray(value)) return value.join(', ');
  return String(value);
}

function emptyFilters(definition) {
  const next = { reportCode: definition?.code ?? '', limit: 500 };
  (definition?.filters ?? []).forEach((filter) => {
    if (filter.defaultValue !== null && filter.defaultValue !== undefined) next[filter.key] = filter.defaultValue;
    else if (filter.type === 'boolean') next[filter.key] = null;
    else next[filter.key] = '';
  });
  return next;
}

function FilterControl({ filter, value, options, onChange }) {
  const common = {
    id: `enterprise-report-filter-${filter.key}`,
    disabled: filter.locked,
    value: value ?? '',
    onChange: (event) => onChange(filter.key, filter.type === 'boolean'
      ? event.target.value === '' ? null : event.target.value === 'true'
      : filter.type === 'number' ? Number(event.target.value || filter.defaultValue || 500)
        : event.target.value)
  };
  let control;
  if (filter.type === 'select') {
    control = (
      <select {...common}>
        <option value="">All available</option>
        {(options ?? []).filter((option) => !option.locked || String(option.value) === String(value)).map((option) => (
          <option value={option.value} disabled={option.locked} key={option.value}>
            {option.label}{option.detail ? ` — ${option.detail}` : ''}
          </option>
        ))}
      </select>
    );
  } else if (filter.type === 'boolean') {
    control = <select {...common}><option value="">All</option><option value="true">Yes</option><option value="false">No</option></select>;
  } else {
    control = (
      <input
        {...common}
        type={filter.type === 'search' ? 'search' : filter.type}
        required={filter.required}
        placeholder={filter.placeholder ?? ''}
        min={filter.type === 'number' ? 1 : undefined}
        max={filter.type === 'number' ? 5000 : undefined}
      />
    );
  }
  return (
    <label className={filter.locked ? 'is-locked' : ''} htmlFor={common.id}>
      <span>{filter.label}{filter.required ? ' *' : ''}</span>
      {control}
      {filter.locked ? <small>{filter.lockedReason ?? 'Locked to your reporting scope.'}</small> : null}
    </label>
  );
}

function ReportCatalog({ reports, selectedCode, onSelect, category }) {
  const visible = reports.filter((report) => category === 'all' || report.category === category);
  return (
    <div className="enterprise-reporting-catalog-grid">
      {visible.map((report) => (
        <button
          type="button"
          className={selectedCode === report.code ? 'active' : ''}
          key={report.code}
          onClick={() => onSelect(report.code)}
        >
          <span>{report.category}</span>
          <strong>{report.name}</strong>
          <p>{report.description}</p>
          <small>Modules {report.modules.join(', ')} · {report.filters.length} report-specific filter(s)</small>
        </button>
      ))}
    </div>
  );
}

function SourcePanel({ sources = [] }) {
  return (
    <section className="enterprise-reporting-source-panel" aria-label="Report source health">
      <div className="enterprise-reporting-section-heading">
        <div><p>Source accountability</p><h3>Independent report sources</h3><span>One degraded source never clears results returned by healthy sources.</span></div>
        <Status value={sources.some((source) => ['unavailable', 'restricted'].includes(source.status)) ? 'partial' : 'healthy'} />
      </div>
      <div className="enterprise-reporting-source-grid">
        {sources.map((source) => (
          <article key={source.key} className={statusTone(source.status)}>
            <div><strong>{source.name}</strong><Status value={source.status} /></div>
            <p>{source.message}</p>
            <dl><div><dt>Required</dt><dd>{source.required ? 'Yes' : 'No'}</dd></div><div><dt>Records</dt><dd>{source.recordCount ?? 0}</dd></div><div><dt>Observed</dt><dd>{dateTime(source.observedAt)}</dd></div><div><dt>Diagnostic</dt><dd><code>{source.diagnosticCode || 'None'}</code></dd></div></dl>
          </article>
        ))}
      </div>
    </section>
  );
}

function ResultsTable({ result }) {
  const rows = result?.rows ?? [];
  const columns = result?.columns ?? [];
  if (!rows.length) {
    return (
      <EnterpriseEmptyState
        title={result?.resultStatus === 'source_unavailable' ? 'Required source unavailable' : 'No matching report rows'}
        message={result?.message ?? 'Select a report and run a preview.'}
      />
    );
  }
  return (
    <EnterpriseTable
      caption={`${result.reportName} — ${rows.length} role-scoped row(s)`}
      rowKey="projectId"
      columns={columns.map((column) => ({
        key: column.key,
        label: column.label,
        render: (row) => displayValue(row[column.key], column)
      }))}
      rows={rows}
    />
  );
}

export default function EnterpriseReportingCenter({ authSession }) {
  const [catalogState, setCatalogState] = useState({ loading: true, data: null, error: '' });
  const [selectedCode, setSelectedCode] = useState('');
  const [category, setCategory] = useState('all');
  const [filterState, setFilterState] = useState({ loading: false, data: null, error: '' });
  const [filters, setFilters] = useState({ reportCode: '', limit: 500 });
  const [resultState, setResultState] = useState({ loading: false, data: null, runId: '', error: '' });
  const [historyState, setHistoryState] = useState({ loading: true, data: [], error: '' });
  const [viewsState, setViewsState] = useState({ loading: true, data: [], error: '' });
  const [activeTab, setActiveTab] = useState('reports');
  const [savedViewName, setSavedViewName] = useState('');
  const [exporting, setExporting] = useState('');

  const loadCatalog = useCallback(async () => {
    setCatalogState((current) => ({ ...current, loading: true, error: '' }));
    try {
      const data = await api('/api/enterprise-reporting/catalog', authSession);
      setCatalogState({ loading: false, data, error: '' });
      setSelectedCode((current) => current || data.reports?.[0]?.code || '');
    } catch (error) {
      setCatalogState({ loading: false, data: null, error: error.message ?? 'The enterprise report catalog is unavailable.' });
    }
  }, [authSession]);

  const loadHistoryAndViews = useCallback(async () => {
    const [history, views] = await Promise.allSettled([
      api('/api/enterprise-reporting/history?limit=100', authSession),
      api('/api/enterprise-reporting/saved-views', authSession)
    ]);
    setHistoryState(history.status === 'fulfilled'
      ? { loading: false, data: history.value.history ?? [], error: '' }
      : { loading: false, data: [], error: history.reason?.message ?? 'Report history is unavailable.' });
    setViewsState(views.status === 'fulfilled'
      ? { loading: false, data: views.value.views ?? [], error: '' }
      : { loading: false, data: [], error: views.reason?.message ?? 'Saved report views are unavailable.' });
  }, [authSession]);

  useEffect(() => { void loadCatalog(); void loadHistoryAndViews(); }, [loadCatalog, loadHistoryAndViews]);

  const reports = catalogState.data?.reports ?? [];
  const categories = catalogState.data?.categories ?? [];
  const selectedDefinition = useMemo(
    () => reports.find((report) => report.code === selectedCode) ?? null,
    [reports, selectedCode]
  );

  useEffect(() => {
    if (!selectedCode) return;
    let cancelled = false;
    setFilterState({ loading: true, data: null, error: '' });
    setResultState({ loading: false, data: null, runId: '', error: '' });
    void api('/api/enterprise-reporting/filter-options', authSession, {
      method: 'POST',
      body: JSON.stringify({ reportCode: selectedCode })
    }).then((data) => {
      if (cancelled) return;
      setFilterState({ loading: false, data, error: '' });
      const next = emptyFilters(data.definition);
      Object.entries(data.options?.lockedValues ?? {}).forEach(([key, value]) => { next[key] = value; });
      setFilters(next);
    }).catch((error) => {
      if (cancelled) return;
      setFilterState({ loading: false, data: null, error: error.message ?? 'Report filters are unavailable.' });
    });
    return () => { cancelled = true; };
  }, [authSession, selectedCode]);

  function updateFilter(key, value) {
    setFilters((current) => ({ ...current, [key]: value }));
  }

  async function execute(persisted) {
    setResultState((current) => ({ ...current, loading: true, error: '' }));
    try {
      const data = await api(`/api/enterprise-reporting/${persisted ? 'run' : 'preview'}`, authSession, {
        method: 'POST', body: JSON.stringify({ ...filters, reportCode: selectedCode })
      });
      setResultState({ loading: false, data: data.result, runId: data.runId ?? '', error: '' });
      setActiveTab('results');
      if (persisted) await loadHistoryAndViews();
    } catch (error) {
      setResultState((current) => ({ ...current, loading: false, error: error.message ?? 'The report could not be run.' }));
    }
  }

  async function download(runId, format) {
    if (!runId) return;
    setExporting(`${runId}:${format}`);
    try {
      const response = await fetch(`/api/enterprise-reporting/runs/${runId}/export?format=${format}`, {
        credentials: 'include', cache: 'no-store', headers: headers(authSession)
      });
      if (!response.ok) {
        const payload = await response.json().catch(() => ({}));
        throw new Error(payload.message || `Report export returned HTTP ${response.status}.`);
      }
      const blob = await response.blob();
      const disposition = response.headers.get('content-disposition') ?? '';
      const match = disposition.match(/filename\*?=(?:UTF-8''|")?([^";]+)/i);
      const fileName = match?.[1] ? decodeURIComponent(match[1].replaceAll('"', '')) : `enterprise-report.${format}`;
      const url = URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url; link.download = fileName; document.body.appendChild(link); link.click(); link.remove();
      URL.revokeObjectURL(url);
    } catch (error) {
      setResultState((current) => ({ ...current, error: error.message ?? 'The report could not be exported.' }));
    } finally { setExporting(''); }
  }

  async function saveView() {
    if (!savedViewName.trim() || !selectedCode) return;
    try {
      await api('/api/enterprise-reporting/saved-views', authSession, {
        method: 'POST',
        body: JSON.stringify({ name: savedViewName.trim(), reportCode: selectedCode, filters: { ...filters, reportCode: selectedCode }, isDefault: false })
      });
      setSavedViewName('');
      await loadHistoryAndViews();
    } catch (error) {
      setViewsState((current) => ({ ...current, error: error.message ?? 'The report view could not be saved.' }));
    }
  }

  function applySavedView(view) {
    const saved = view.filters ?? {};
    setSelectedCode(view.reportCode);
    window.setTimeout(() => {
      setFilters((current) => ({ ...current, ...saved, reportCode: view.reportCode }));
      setActiveTab('reports');
    }, 0);
  }

  async function deleteSavedView(id) {
    try {
      await api(`/api/enterprise-reporting/saved-views/${id}`, authSession, { method: 'DELETE' });
      await loadHistoryAndViews();
    } catch (error) {
      setViewsState((current) => ({ ...current, error: error.message ?? 'The saved view could not be deleted.' }));
    }
  }

  const summary = catalogState.data;
  const tabs = [
    { key: 'reports', label: 'Report catalog', description: 'Select and filter' },
    { key: 'results', label: 'Results', description: resultState.data ? `${resultState.data.rowCount} rows` : 'Preview or run' },
    { key: 'history', label: 'Run history', description: `${historyState.data.length} recorded` },
    { key: 'saved', label: 'Saved views', description: `${viewsState.data.length} personal` }
  ];

  return (
    <EnterpriseModulePage
      moduleCode="030"
      group="Reports & Workflow"
      title="Enterprise Reporting Center"
      summary="Run role-scoped reports across projects, customers, financials, time, engineers, Project Managers, sales, delivery, operations, governance, and acceptance. Every report exposes only filters and records authorized for the effective user."
      className="enterprise-reporting-center"
      actions={<button type="button" onClick={() => { void loadCatalog(); void loadHistoryAndViews(); }}>Refresh reporting</button>}
    >
      <EnterpriseSummaryStrip ariaLabel="Enterprise reporting scope summary">
        <EnterpriseStatusCard label="Available reports" value={summary?.reportCount ?? 0} detail="Catalog changes by role and permissions" tone="informational" />
        <EnterpriseStatusCard label="Visible projects" value={summary?.scope?.visibleProjectCount ?? 0} detail="Server-authorized project scope" tone="healthy" />
        <EnterpriseStatusCard
          label="Effective scope"
          value={summary?.scope?.engineerReportsLockedToSelf ? 'Engineer — self only' : summary?.scope?.projectManagerReportsLockedToOwnPortfolio ? 'PM — own portfolio' : summary?.access?.broad ? 'Organization-authorized' : 'Role-scoped'}
          detail="Report permission never expands record or field access"
          tone="neutral"
        />
      </EnterpriseSummaryStrip>

      {catalogState.error ? <EnterpriseWarning title="Reporting catalog unavailable" message={catalogState.error} tone="critical" action={<button type="button" onClick={loadCatalog}>Retry</button>} /> : null}
      <EnterpriseTabs tabs={tabs} activeTab={activeTab} onChange={setActiveTab} ariaLabel="Enterprise reporting workspace" />

      {activeTab === 'reports' ? (
        <div className="enterprise-reporting-workspace">
          <section className="enterprise-reporting-card">
            <div className="enterprise-reporting-section-heading">
              <div><p>Report catalog</p><h2>Every authorized facet of ProjectPulse</h2><span>Choose a report first; only filters relevant to that report appear.</span></div>
              <label>Category<select value={category} onChange={(event) => setCategory(event.target.value)}><option value="all">All categories</option>{categories.map((value) => <option value={value} key={value}>{value}</option>)}</select></label>
            </div>
            {catalogState.loading ? <EnterpriseEmptyState title="Loading report catalog" message="Resolving the effective user's reporting scope…" /> : <ReportCatalog reports={reports} selectedCode={selectedCode} onSelect={setSelectedCode} category={category} />}
          </section>

          <section className="enterprise-reporting-card enterprise-reporting-command-card">
            <div className="enterprise-reporting-section-heading">
              <div><p>Report command</p><h2>{selectedDefinition?.name ?? 'Select a report'}</h2><span>{selectedDefinition?.scopeRule}</span></div>
              <Status value={resultState.data?.resultStatus ?? 'ready'} />
            </div>
            {filterState.error ? <EnterpriseWarning title="Report filters unavailable" message={filterState.error} tone="critical" /> : null}
            <EnterpriseFilterBar
              ariaLabel={`${selectedDefinition?.name ?? 'Report'} filters`}
              actions={(
                <div className="enterprise-reporting-action-row">
                  <button type="button" className="secondary-action" onClick={() => execute(false)} disabled={resultState.loading || filterState.loading}>Preview</button>
                  <button type="button" className="primary-action" onClick={() => execute(true)} disabled={resultState.loading || filterState.loading}>{resultState.loading ? 'Running…' : 'Run and record'}</button>
                </div>
              )}
            >
              {(filterState.data?.definition?.filters ?? []).map((filter) => (
                <FilterControl
                  filter={filter}
                  value={filters[filter.key]}
                  options={filterState.data?.options?.options?.[filter.optionSource] ?? []}
                  onChange={updateFilter}
                  key={filter.key}
                />
              ))}
            </EnterpriseFilterBar>
            <div className="enterprise-reporting-scope-note"><strong>Scope enforced by server</strong><span>{filterState.data?.options?.scopeExplanation ?? selectedDefinition?.scopeRule}</span></div>
            <div className="enterprise-reporting-save-view"><input value={savedViewName} onChange={(event) => setSavedViewName(event.target.value)} placeholder="Name this report view" /><button type="button" onClick={saveView} disabled={!savedViewName.trim()}>Save view</button></div>
          </section>
        </div>
      ) : null}

      {activeTab === 'results' ? (
        <section className="enterprise-reporting-card">
          <div className="enterprise-reporting-section-heading">
            <div><p>Actual report results</p><h2>{resultState.data?.reportName ?? selectedDefinition?.name ?? 'Report results'}</h2><span>{resultState.data?.message ?? 'Preview or run a report to populate this workspace.'}</span></div>
            <div className="enterprise-reporting-action-row">
              <Status value={resultState.data?.resultStatus ?? 'not_run'} />
              {resultState.runId ? ['xlsx', 'csv', 'json'].map((format) => <button type="button" key={format} disabled={Boolean(exporting)} onClick={() => download(resultState.runId, format)}>Export {format.toUpperCase()}</button>) : null}
            </div>
          </div>
          {resultState.error ? <EnterpriseWarning title="Report operation failed" message={resultState.error} tone="critical" /> : null}
          <ResultsTable result={resultState.data} />
          {resultState.data ? <SourcePanel sources={resultState.data.sources} /> : null}
        </section>
      ) : null}

      {activeTab === 'history' ? (
        <section className="enterprise-reporting-card">
          <div className="enterprise-reporting-section-heading"><div><p>Immutable execution history</p><h2>Recorded report runs</h2><span>Each run preserves effective filters, scope evidence, sources, columns, and returned rows.</span></div><Status value={historyState.error ? 'unavailable' : 'healthy'} /></div>
          {historyState.error ? <EnterpriseWarning title="Report history unavailable" message={historyState.error} /> : null}
          <div className="enterprise-reporting-history-list">
            {historyState.data.map((run) => (
              <article key={run.runId}>
                <div><strong>{run.reportName}</strong><span>{dateTime(run.startedAt)} · {run.rowCount} rows</span><small>{run.reportCode}</small></div>
                <Status value={run.resultStatus} />
                <div>{['xlsx', 'csv', 'json'].map((format) => <button type="button" key={format} disabled={Boolean(exporting)} onClick={() => download(run.runId, format)}>{format.toUpperCase()}</button>)}</div>
              </article>
            ))}
            {!historyState.loading && !historyState.data.length ? <EnterpriseEmptyState title="No report history" message="Run and record a report to create immutable history." /> : null}
          </div>
        </section>
      ) : null}

      {activeTab === 'saved' ? (
        <section className="enterprise-reporting-card">
          <div className="enterprise-reporting-section-heading"><div><p>Personal reporting productivity</p><h2>Saved report views</h2><span>Saved views remember report-specific filters but never bypass current authorization.</span></div><Status value={viewsState.error ? 'unavailable' : 'healthy'} /></div>
          {viewsState.error ? <EnterpriseWarning title="Saved views unavailable" message={viewsState.error} /> : null}
          <div className="enterprise-reporting-saved-list">
            {viewsState.data.map((view) => (
              <article key={view.savedViewId}>
                <div><strong>{view.name}</strong><span>{view.reportCode}</span><small>Version {view.version} · updated {dateTime(view.updatedAt)}</small></div>
                {view.isDefault ? <Status value="default" /> : null}
                <div><button type="button" onClick={() => applySavedView(view)}>Apply</button><button type="button" className="danger-action" onClick={() => deleteSavedView(view.savedViewId)}>Delete</button></div>
              </article>
            ))}
            {!viewsState.loading && !viewsState.data.length ? <EnterpriseEmptyState title="No saved views" message="Select a report, configure its filters, and save the view." /> : null}
          </div>
        </section>
      ) : null}
    </EnterpriseModulePage>
  );
}
