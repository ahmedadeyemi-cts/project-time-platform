import { useCallback, useEffect, useMemo, useState } from 'react';
import {
  EnterpriseEmptyState,
  EnterpriseModulePage,
  EnterpriseStatusCard,
  EnterpriseSummaryStrip,
  EnterpriseTable,
  EnterpriseTabs,
  EnterpriseWarning
} from './enterprise/EnterpriseModulePresentation.jsx';
import './analytics-center.css';

function sessionToken(authSession) {
  if (authSession?.sessionToken) return authSession.sessionToken;
  if (authSession?.token) return authSession.token;
  if (authSession?.accessToken) return authSession.accessToken;
  try {
    const stored = JSON.parse(window.localStorage.getItem('projectPulseAuthSession') || 'null');
    return stored?.sessionToken || stored?.token || stored?.accessToken || '';
  } catch {
    return '';
  }
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
  const contentType = response.headers.get('content-type') || '';
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

function displayDate(value) {
  if (!value) return 'Not recorded';
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? String(value) : parsed.toLocaleString();
}

function displayValue(value, column) {
  if (value === null || value === undefined || value === '') return 'Not available';
  if (Array.isArray(value)) return value.join(', ');
  if (column.dataType === 'currency') {
    const number = Number(value);
    return Number.isFinite(number)
      ? number.toLocaleString(undefined, { style: 'currency', currency: 'USD' })
      : 'Not available';
  }
  if (column.dataType === 'number') {
    const number = Number(value);
    return Number.isFinite(number) ? number.toLocaleString(undefined, { maximumFractionDigits: 2 }) : 'Not available';
  }
  if (column.dataType === 'percent') {
    const number = Number(value);
    return Number.isFinite(number) ? `${number.toLocaleString(undefined, { maximumFractionDigits: 2 })}%` : 'Not available';
  }
  if (column.dataType === 'date') {
    const parsed = new Date(`${String(value).slice(0, 10)}T00:00:00`);
    return Number.isNaN(parsed.getTime()) ? String(value) : parsed.toLocaleDateString();
  }
  if (column.dataType === 'datetime') return displayDate(value);
  if (column.dataType === 'boolean') return value === true || String(value).toLowerCase() === 'true' ? 'Yes' : 'No';
  return String(value);
}

function tone(value) {
  const normalized = String(value ?? '').toLowerCase();
  if (['failed', 'unavailable', 'critical', 'over_budget', 'blocked', 'rejected'].some((item) => normalized.includes(item))) return 'critical';
  if (['partial', 'warning', 'pending', 'approaching', 'held', 'expiring'].some((item) => normalized.includes(item))) return 'warning';
  if (['complete', 'healthy', 'available', 'ready', 'approved', 'accepted', 'resolved'].some((item) => normalized.includes(item))) return 'healthy';
  return 'informational';
}

function Status({ value }) {
  return <span className={`analytics-status ${tone(value)}`}>{words(value || 'not run')}</span>;
}

const ALL_LABELS = Object.freeze({
  customerId: 'All customers',
  projectId: 'All projects',
  engineerUserId: 'All engineers',
  projectManagerUserId: 'All Project Managers',
  teamId: 'All teams',
  contractType: 'All contract types',
  projectStatus: 'All project statuses',
  budgetStatus: 'All budget states',
  workflowStatus: 'All workflow states',
  severity: 'All severities',
  moduleCode: 'All modules',
  sourceStatus: 'All source states'
});

function emptyCriteria(definition) {
  const next = { reportCode: definition?.code ?? '', limit: 500 };
  (definition?.filters ?? []).forEach((filter) => {
    if (filter.defaultValue !== null && filter.defaultValue !== undefined) next[filter.key] = filter.defaultValue;
    else if (filter.type === 'boolean') next[filter.key] = null;
    else next[filter.key] = '';
  });
  return next;
}

function FilterControl({ filter, value, options, onChange }) {
  const id = `analytics-filter-${filter.key}`;
  const setValue = (event) => {
    if (filter.type === 'boolean') {
      onChange(filter.key, event.target.value === '' ? null : event.target.value === 'true');
      return;
    }
    if (filter.type === 'number') {
      onChange(filter.key, Number(event.target.value || filter.defaultValue || 500));
      return;
    }
    onChange(filter.key, event.target.value);
  };

  let control;
  if (filter.type === 'select') {
    control = (
      <select id={id} value={value ?? ''} disabled={filter.locked} onChange={setValue}>
        <option value="">{ALL_LABELS[filter.key] || 'All available'}</option>
        {(options ?? []).map((option) => (
          <option
            key={option.value}
            value={option.value}
            disabled={option.locked && String(option.value) !== String(value)}
          >
            {option.label}{option.detail ? ` — ${option.detail}` : ''}
          </option>
        ))}
      </select>
    );
  } else if (filter.type === 'boolean') {
    control = (
      <select id={id} value={value ?? ''} disabled={filter.locked} onChange={setValue}>
        <option value="">All</option>
        <option value="true">Yes</option>
        <option value="false">No</option>
      </select>
    );
  } else {
    control = (
      <input
        id={id}
        value={value ?? ''}
        disabled={filter.locked}
        required={filter.required}
        type={filter.type === 'search' ? 'search' : filter.type}
        placeholder={filter.placeholder || ''}
        min={filter.type === 'number' ? 1 : undefined}
        max={filter.type === 'number' ? 5000 : undefined}
        onChange={setValue}
      />
    );
  }

  return (
    <label className={filter.locked ? 'analytics-filter is-locked' : 'analytics-filter'} htmlFor={id}>
      <span>{filter.label}{filter.required ? ' *' : ''}</span>
      {control}
      {filter.locked ? <small>{filter.lockedReason || 'Locked to your authorized reporting scope.'}</small> : null}
      {filter.type === 'select' && !filter.locked && (options ?? []).length === 0
        ? <small>No role-scoped choices are currently available.</small>
        : null}
    </label>
  );
}

function ResultsTable({ result }) {
  const rows = result?.rows ?? [];
  const columns = result?.columns ?? [];
  if (!rows.length) {
    return (
      <EnterpriseEmptyState
        title={result?.resultStatus === 'source_unavailable' ? 'A required source is unavailable' : 'No matching analytics rows'}
        message={result?.message || 'Select a report, choose its applicable criteria, and generate a preview.'}
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

function SourcePanel({ sources = [] }) {
  if (!sources.length) return null;
  return (
    <section className="analytics-source-panel" aria-label="Analytics source status">
      <div className="analytics-section-heading">
        <div>
          <p>Source accountability</p>
          <h3>Independent data sources</h3>
          <span>A source failure is isolated and does not erase usable results returned by healthy sources.</span>
        </div>
        <Status value={sources.some((source) => ['unavailable', 'restricted'].includes(source.status)) ? 'partial' : 'healthy'} />
      </div>
      <div className="analytics-source-grid">
        {sources.map((source) => (
          <article key={source.key} className={tone(source.status)}>
            <div><strong>{source.name}</strong><Status value={source.status} /></div>
            <p>{source.message}</p>
            <dl>
              <div><dt>Required</dt><dd>{source.required ? 'Yes' : 'No'}</dd></div>
              <div><dt>Records</dt><dd>{source.recordCount ?? 0}</dd></div>
              <div><dt>Observed</dt><dd>{displayDate(source.observedAt)}</dd></div>
              <div><dt>Diagnostic</dt><dd><code>{source.diagnosticCode || 'None'}</code></dd></div>
            </dl>
          </article>
        ))}
      </div>
    </section>
  );
}

export default function AnalyticsCenter({ authSession }) {
  const [catalog, setCatalog] = useState({ loading: true, data: null, error: '' });
  const [selectedCode, setSelectedCode] = useState('');
  const [reportSearch, setReportSearch] = useState('');
  const [category, setCategory] = useState('all');
  const [filterState, setFilterState] = useState({ loading: false, data: null, error: '' });
  const [criteria, setCriteria] = useState({ reportCode: '', limit: 500 });
  const [result, setResult] = useState({ loading: false, data: null, runId: '', error: '' });
  const [history, setHistory] = useState({ loading: true, rows: [], error: '' });
  const [activeTab, setActiveTab] = useState('build');
  const [exporting, setExporting] = useState('');

  const loadCatalog = useCallback(async () => {
    setCatalog((current) => ({ ...current, loading: true, error: '' }));
    try {
      const data = await api('/api/analytics/catalog', authSession);
      setCatalog({ loading: false, data, error: '' });
      setSelectedCode((current) => current || data.reports?.[0]?.code || '');
    } catch (error) {
      setCatalog({ loading: false, data: null, error: error.message || 'Analytics catalog is unavailable.' });
    }
  }, [authSession]);

  const loadHistory = useCallback(async () => {
    setHistory((current) => ({ ...current, loading: true, error: '' }));
    try {
      const data = await api('/api/analytics/history?limit=100', authSession);
      setHistory({ loading: false, rows: data.history ?? [], error: '' });
    } catch (error) {
      setHistory({ loading: false, rows: [], error: error.message || 'Analytics history is unavailable.' });
    }
  }, [authSession]);

  useEffect(() => {
    void loadCatalog();
    void loadHistory();
  }, [loadCatalog, loadHistory]);

  const reports = catalog.data?.reports ?? [];
  const categories = catalog.data?.categories ?? [];
  const selectedDefinition = useMemo(
    () => reports.find((report) => report.code === selectedCode) ?? null,
    [reports, selectedCode]
  );
  const visibleReports = useMemo(() => {
    const search = reportSearch.trim().toLowerCase();
    return reports.filter((report) => {
      if (category !== 'all' && report.category !== category) return false;
      if (!search) return true;
      return `${report.name} ${report.description} ${report.category}`.toLowerCase().includes(search);
    });
  }, [reports, category, reportSearch]);

  const loadFilterOptions = useCallback(async (reportCode, seedCriteria = null, preserve = false) => {
    if (!reportCode) return;
    setFilterState((current) => ({ ...current, loading: true, error: '' }));
    try {
      const payload = { ...(seedCriteria ?? {}), reportCode };
      const data = await api('/api/analytics/filter-options', authSession, {
        method: 'POST',
        body: JSON.stringify(payload)
      });
      setFilterState({ loading: false, data, error: '' });
      setCriteria((current) => {
        const next = emptyCriteria(data.definition);
        if (preserve) Object.assign(next, current, seedCriteria ?? {});
        Object.entries(data.options?.lockedValues ?? {}).forEach(([key, value]) => { next[key] = value; });
        next.reportCode = reportCode;
        return next;
      });
    } catch (error) {
      setFilterState({ loading: false, data: null, error: error.message || 'Analytics filter lists are unavailable.' });
    }
  }, [authSession]);

  useEffect(() => {
    if (!selectedCode) return;
    setResult({ loading: false, data: null, runId: '', error: '' });
    void loadFilterOptions(selectedCode, { reportCode: selectedCode }, false);
  }, [selectedCode, loadFilterOptions]);

  function updateCriterion(key, value) {
    const next = { ...criteria, [key]: value, reportCode: selectedCode };
    setCriteria(next);
    if (['customerId', 'projectId', 'teamId'].includes(key)) {
      window.clearTimeout(window.__projectPulseAnalyticsFilterRefreshTimer);
      window.__projectPulseAnalyticsFilterRefreshTimer = window.setTimeout(() => {
        void loadFilterOptions(selectedCode, next, true);
      }, 180);
    }
  }

  function resetCriteria() {
    const next = emptyCriteria(filterState.data?.definition ?? selectedDefinition);
    setCriteria(next);
    setResult({ loading: false, data: null, runId: '', error: '' });
  }

  async function execute(persisted) {
    if (!selectedCode) return;
    setResult((current) => ({ ...current, loading: true, error: '' }));
    try {
      const data = await api(`/api/analytics/${persisted ? 'run' : 'preview'}`, authSession, {
        method: 'POST',
        body: JSON.stringify({ ...criteria, reportCode: selectedCode })
      });
      setResult({ loading: false, data: data.result, runId: data.runId || '', error: '' });
      setActiveTab('results');
      if (persisted) await loadHistory();
    } catch (error) {
      setResult((current) => ({ ...current, loading: false, error: error.message || 'The analytics report could not be generated.' }));
    }
  }

  async function download(runId, format) {
    if (!runId) return;
    setExporting(`${runId}:${format}`);
    try {
      const response = await fetch(`/api/analytics/runs/${runId}/export?format=${format}`, {
        credentials: 'include',
        cache: 'no-store',
        headers: requestHeaders(authSession)
      });
      if (!response.ok) {
        const payload = await response.json().catch(() => ({}));
        throw new Error(payload.message || `Analytics export returned HTTP ${response.status}.`);
      }
      const blob = await response.blob();
      const disposition = response.headers.get('content-disposition') || '';
      const match = disposition.match(/filename\*?=(?:UTF-8''|")?([^";]+)/i);
      const fileName = match?.[1]
        ? decodeURIComponent(match[1].replaceAll('"', ''))
        : `analytics-center-${runId}.${format}`;
      const url = URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url;
      link.download = fileName;
      document.body.appendChild(link);
      link.click();
      link.remove();
      URL.revokeObjectURL(url);
    } catch (error) {
      setResult((current) => ({ ...current, error: error.message || 'The analytics export could not be created.' }));
    } finally {
      setExporting('');
    }
  }

  const tabs = [
    { key: 'build', label: 'Build report', description: selectedDefinition?.name || 'Select a report' },
    { key: 'results', label: 'Results', description: result.data ? `${result.data.rowCount} rows` : 'Preview or run' },
    { key: 'history', label: 'Run history', description: `${history.rows.length} recorded` }
  ];

  const filters = filterState.data?.definition?.filters ?? [];
  const filterOptions = filterState.data?.options?.options ?? {};
  const summary = catalog.data;

  return (
    <EnterpriseModulePage
      moduleCode="030"
      group="Analytics & Reporting"
      title="Analytics Center"
      summary="Select a report, then use only the criteria that apply to that report. Customer, project, Engineer, Project Manager, team, contract, financial, delivery, and operational choices are populated from your current server-authorized ProjectPulse scope."
      className="analytics-center"
      actions={(
        <button type="button" onClick={() => { void loadCatalog(); void loadHistory(); void loadFilterOptions(selectedCode, criteria, true); }}>
          Refresh Analytics
        </button>
      )}
    >
      <EnterpriseSummaryStrip ariaLabel="Analytics Center scope summary">
        <EnterpriseStatusCard label="Available reports" value={summary?.reportCount ?? 0} detail="Catalog changes by role and permission" tone="informational" />
        <EnterpriseStatusCard label="Visible projects" value={summary?.scope?.visibleProjectCount ?? 0} detail="Server-authorized project scope" tone="healthy" />
        <EnterpriseStatusCard label="Customer choices" value={summary?.scope?.customerDirectoryCount ?? 0} detail="Customer Directory source" tone="informational" />
        <EnterpriseStatusCard label="Team choices" value={summary?.scope?.teamDirectoryCount ?? 0} detail="Active team membership source" tone="informational" />
        <EnterpriseStatusCard
          label="Effective scope"
          value={summary?.scope?.engineerReportsLockedToSelf ? 'Engineer — self only' : summary?.scope?.projectManagerReportsLockedToOwnPortfolio ? 'PM — own portfolio' : summary?.access?.broad ? 'Organization-authorized' : 'Role-scoped'}
          detail="Analytics never expands record or financial-field access"
          tone="neutral"
        />
      </EnterpriseSummaryStrip>

      {catalog.error ? <EnterpriseWarning title="Analytics catalog unavailable" message={catalog.error} tone="critical" action={<button type="button" onClick={loadCatalog}>Retry</button>} /> : null}
      <EnterpriseTabs tabs={tabs} activeTab={activeTab} onChange={setActiveTab} ariaLabel="Analytics Center workspace" />

      {activeTab === 'build' ? (
        <div className="analytics-build-layout">
          <section className="analytics-card analytics-report-picker">
            <div className="analytics-step-heading"><span>1</span><div><p>Select report</p><h2>Choose what you need to analyze</h2></div></div>
            <div className="analytics-report-select-row">
              <label>
                <span>Report type</span>
                <select value={selectedCode} onChange={(event) => setSelectedCode(event.target.value)}>
                  {reports.map((report) => <option value={report.code} key={report.code}>{report.category} — {report.name}</option>)}
                </select>
              </label>
              <label>
                <span>Category</span>
                <select value={category} onChange={(event) => setCategory(event.target.value)}>
                  <option value="all">All categories</option>
                  {categories.map((item) => <option value={item} key={item}>{item}</option>)}
                </select>
              </label>
              <label>
                <span>Find a report</span>
                <input type="search" value={reportSearch} onChange={(event) => setReportSearch(event.target.value)} placeholder="Search reports" />
              </label>
            </div>
            <div className="analytics-report-cards">
              {visibleReports.map((report) => (
                <button
                  type="button"
                  key={report.code}
                  className={selectedCode === report.code ? 'active' : ''}
                  onClick={() => setSelectedCode(report.code)}
                >
                  <span>{report.category}</span>
                  <strong>{report.name}</strong>
                  <small>{report.description}</small>
                </button>
              ))}
              {!catalog.loading && visibleReports.length === 0 ? <EnterpriseEmptyState title="No matching reports" message="Change the report search or category filter." /> : null}
            </div>
          </section>

          <section className="analytics-card analytics-criteria-card">
            <div className="analytics-step-heading"><span>2</span><div><p>Set criteria</p><h2>{selectedDefinition?.name || 'Select a report'}</h2><small>{selectedDefinition?.scopeRule}</small></div></div>
            {filterState.error ? <EnterpriseWarning title="Filter lists unavailable" message={filterState.error} tone="critical" action={<button type="button" onClick={() => loadFilterOptions(selectedCode, criteria, true)}>Retry</button>} /> : null}
            {filterState.loading ? <EnterpriseEmptyState title="Loading applicable criteria" message="Reading customer, project, Engineer, Project Manager, team, and report-specific choices…" /> : null}
            {!filterState.loading ? (
              <div className="analytics-filter-grid">
                {filters.map((filter) => (
                  <FilterControl
                    key={filter.key}
                    filter={filter}
                    value={criteria[filter.key]}
                    options={filterOptions[filter.optionSource] ?? []}
                    onChange={updateCriterion}
                  />
                ))}
              </div>
            ) : null}
            <div className="analytics-scope-note">
              <strong>Scope enforced by the API</strong>
              <span>{filterState.data?.options?.scopeExplanation || selectedDefinition?.scopeRule || 'Only authorized data will be returned.'}</span>
            </div>
            <div className="analytics-step-actions">
              <button type="button" className="secondary" onClick={resetCriteria} disabled={filterState.loading}>Reset criteria</button>
              <button type="button" className="secondary" onClick={() => loadFilterOptions(selectedCode, criteria, true)} disabled={filterState.loading}>Refresh filter lists</button>
              <button type="button" className="primary" onClick={() => execute(false)} disabled={result.loading || filterState.loading || !selectedCode}>Preview report</button>
              <button type="button" className="success" onClick={() => execute(true)} disabled={result.loading || filterState.loading || !selectedCode}>{result.loading ? 'Running…' : 'Run & save'}</button>
            </div>
          </section>
        </div>
      ) : null}

      {activeTab === 'results' ? (
        <section className="analytics-card">
          <div className="analytics-section-heading">
            <div><p>Actual analytics results</p><h2>{result.data?.reportName || selectedDefinition?.name || 'Results'}</h2><span>{result.data?.message || 'Preview or run a report to populate this workspace.'}</span></div>
            <div className="analytics-result-actions">
              <Status value={result.data?.resultStatus || 'not_run'} />
              {result.runId ? ['xlsx', 'csv', 'json'].map((format) => (
                <button type="button" key={format} disabled={Boolean(exporting)} onClick={() => download(result.runId, format)}>
                  Export {format.toUpperCase()}
                </button>
              )) : null}
            </div>
          </div>
          {result.error ? <EnterpriseWarning title="Analytics operation failed" message={result.error} tone="critical" /> : null}
          <ResultsTable result={result.data} />
          <SourcePanel sources={result.data?.sources ?? []} />
        </section>
      ) : null}

      {activeTab === 'history' ? (
        <section className="analytics-card">
          <div className="analytics-section-heading">
            <div><p>Immutable run evidence</p><h2>Analytics run history</h2><span>Each recorded run preserves filters, scope evidence, sources, columns, and returned rows.</span></div>
            <button type="button" onClick={loadHistory}>Refresh history</button>
          </div>
          {history.error ? <EnterpriseWarning title="Analytics history unavailable" message={history.error} tone="critical" /> : null}
          <div className="analytics-history-list">
            {history.rows.map((run) => (
              <article key={run.runId}>
                <div><strong>{run.reportName}</strong><span>{displayDate(run.startedAt)} · {run.rowCount} rows</span><small>{run.reportCode}</small></div>
                <Status value={run.resultStatus} />
                <div>{['xlsx', 'csv', 'json'].map((format) => <button type="button" key={format} disabled={Boolean(exporting)} onClick={() => download(run.runId, format)}>{format.toUpperCase()}</button>)}</div>
              </article>
            ))}
            {!history.loading && history.rows.length === 0 ? <EnterpriseEmptyState title="No analytics history" message="Run & save a report to create immutable history." /> : null}
          </div>
        </section>
      ) : null}
    </EnterpriseModulePage>
  );
}
