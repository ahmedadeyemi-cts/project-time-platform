import { useCallback, useEffect, useMemo, useState } from 'react';
import USSignalLogo from './enterprise/USSignalLogo.jsx';
import AnalyticsMultiSelect from './analytics/AnalyticsMultiSelect.jsx';
import './analytics-center.css';

const LEGACY_COMPATIBLE_EXPORTS = ['xlsx', 'csv', 'json'];
const PRIMARY_EXPORTS = ['pdf', 'xlsx'];
const DEFAULT_REPORT = 'project_financial_health';
const ALL_LABELS = Object.freeze({
  customerIds: 'All customers',
  projectIds: 'All projects',
  engineerUserIds: 'All engineers',
  projectManagerUserIds: 'All Project Managers',
  teamIds: 'All teams',
  contractTypes: 'All contract types'
});
const NAVIGATION = Object.freeze([
  ['overview', '⌂', 'Home'],
  ['analytics', '▥', 'Analytics Center'],
  ['dashboards', '▦', 'Dashboards'],
  ['reports', '▤', 'Reports'],
  ['schedules', '□', 'Schedules'],
  ['data-explorer', '⌕', 'Data Explorer'],
  ['kpis', '⌁', 'KPIs & Metrics'],
  ['alerts', '♢', 'Alerts & Subscriptions'],
  ['data-quality', '◇', 'Data Quality'],
  ['admin', '⚙', 'Admin']
]);
const WORKSPACES = Object.freeze([
  ['my', 'My Workspace'],
  ['operations', 'Operations'],
  ['finance', 'Finance'],
  ['service-delivery', 'Service Delivery']
]);
const CATEGORY_ICONS = Object.freeze({
  Financials: '$',
  Projects: '▣',
  People: '♙',
  Customers: '▥',
  'Time & Utilization': '◷',
  Billing: '▤',
  Closeout: '✓',
  Operations: '⌁',
  Governance: '◇',
  Delivery: '⇢',
  Other: '◈'
});
const CADENCES = Object.freeze([
  ['daily', 'Daily'],
  ['weekdays', 'Weekdays'],
  ['weekly', 'Weekly'],
  ['monthly', 'Monthly'],
  ['quarterly', 'Quarterly'],
  ['yearly', 'Yearly']
]);
const WEEKDAYS = Object.freeze([
  [0, 'Sunday'], [1, 'Monday'], [2, 'Tuesday'], [3, 'Wednesday'],
  [4, 'Thursday'], [5, 'Friday'], [6, 'Saturday']
]);
const TIMEZONES = Object.freeze([
  ['America/New_York', 'Eastern Time (ET)'],
  ['America/Chicago', 'Central Time (CT)'],
  ['America/Denver', 'Mountain Time (MT)'],
  ['America/Los_Angeles', 'Pacific Time (PT)'],
  ['UTC', 'UTC']
]);

function token(authSession) {
  return authSession?.sessionToken
    ?? authSession?.token
    ?? authSession?.accessToken
    ?? window.localStorage.getItem('projectPulseSessionToken')
    ?? window.sessionStorage.getItem('projectPulseSessionToken')
    ?? '';
}

function headers(authSession, body = false) {
  const session = token(authSession);
  return {
    Accept: 'application/json',
    ...(body ? { 'Content-Type': 'application/json' } : {}),
    ...(session ? {
      Authorization: `Bearer ${session}`,
      'X-ProjectPulse-Session': session,
      'X-Project-Pulse-Session': session,
      'X-Session-Token': session
    } : {})
  };
}

async function api(path, authSession, options = {}) {
  const response = await fetch(path, {
    credentials: 'include',
    cache: 'no-store',
    ...options,
    headers: {
      ...headers(authSession, Boolean(options.body)),
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
    .replaceAll('-', ' ')
    .replace(/\b\w/g, (character) => character.toUpperCase());
}

function text(value, fallback = 'Not available') {
  if (value === null || value === undefined || value === '') return fallback;
  return String(value);
}

function dateTime(value) {
  if (!value) return 'Not recorded';
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? String(value) : parsed.toLocaleString();
}

function statusTone(value) {
  const normalized = String(value ?? '').toLowerCase();
  if (['failed', 'critical', 'over_budget', 'unavailable', 'restricted', 'blocked'].some((item) => normalized.includes(item))) return 'critical';
  if (['partial', 'warning', 'approaching', 'queued', 'suppressed', 'stale'].some((item) => normalized.includes(item))) return 'warning';
  if (['healthy', 'complete', 'available', 'sent', 'ready', 'on_track'].some((item) => normalized.includes(item))) return 'healthy';
  return 'neutral';
}

function Status({ value, children }) {
  return <span className={`analytics-status ${statusTone(value)}`}>{children ?? words(value || 'unknown')}</span>;
}

function navigate(hash) {
  window.location.hash = hash;
}

function BlankState({ title, children, action }) {
  return (
    <div className="analytics-empty-state">
      <span aria-hidden="true">◇</span>
      <strong>{title}</strong>
      <p>{children}</p>
      {action}
    </div>
  );
}

function MetricCard({ metric }) {
  return (
    <article className={`analytics-kpi-card tone-${metric.tone ?? 'blue'}`}>
      <div className="analytics-kpi-heading">
        <span className="analytics-kpi-icon" aria-hidden="true">{metricIcon(metric.key)}</span>
        <span>{metric.label}</span>
      </div>
      <strong>{metric.value}</strong>
      <small>{metric.detail}</small>
      <div className="analytics-kpi-visual" aria-hidden="true">
        <span style={{ width: `${metric.progressPercentage ?? (metric.available ? 64 : 0)}%` }} />
      </div>
      <em>{metric.available ? 'Current authorized data' : 'Source not available'}</em>
    </article>
  );
}

function metricIcon(key) {
  return ({
    portfolioValue: '$', activeProjects: '▣', utilization: '♙', hoursUsed: '◷',
    forecastVariance: '◎', newCustomers: '+', pmWorkload: '♟', deliveryHealth: '✓'
  })[key] ?? '◇';
}

function ReportPreviewGraphic({ reportCode, category }) {
  const seed = [...String(reportCode)].reduce((total, character) => total + character.charCodeAt(0), 0);
  const heights = [0, 1, 2, 3, 4].map((index) => 22 + ((seed + index * 17) % 48));
  return (
    <div className={`analytics-report-thumbnail category-${String(category).toLowerCase().replaceAll(/[^a-z0-9]+/g, '-')}`} aria-hidden="true">
      <div className="analytics-thumbnail-bars">
        {heights.map((height, index) => <span key={index} style={{ height: `${height}%` }} />)}
      </div>
      <div className="analytics-thumbnail-lines"><i /><i /><i /></div>
    </div>
  );
}

function RecentCard({ item, onOpen, onFavorite }) {
  return (
    <article className="analytics-recent-card">
      <button type="button" className="analytics-recent-open" onClick={() => onOpen(item.reportCode)}>
        <ReportPreviewGraphic reportCode={item.reportCode} category={item.category} />
        <strong>{item.reportName}</strong>
        <span>{item.category}</span>
        <small>{item.lastViewedAt ? `Viewed ${dateTime(item.lastViewedAt)}` : 'Available report'}</small>
      </button>
      <button
        type="button"
        className={`analytics-favorite ${item.favorite ? 'is-favorite' : ''}`}
        onClick={() => onFavorite(item.reportCode, !item.favorite)}
        aria-label={`${item.favorite ? 'Remove' : 'Add'} ${item.reportName} ${item.favorite ? 'from' : 'to'} favorites`}
      >
        {item.favorite ? '★' : '☆'}
      </button>
    </article>
  );
}

function FilterControl({ filter, options, value, onChange }) {
  if (filter.type === 'multiselect') {
    return (
      <AnalyticsMultiSelect
        label={filter.label}
        options={options}
        values={Array.isArray(value) ? value : []}
        onChange={onChange}
        locked={filter.locked}
        lockedReason={filter.lockedReason}
        placeholder={ALL_LABELS[filter.key] ?? `All ${filter.label.toLowerCase()}`}
      />
    );
  }
  if (filter.type === 'select') {
    return (
      <label className="analytics-field">
        <span>{filter.label}</span>
        <select value={value ?? ''} disabled={filter.locked} onChange={(event) => onChange(event.target.value)}>
          <option value="">All {filter.label.toLowerCase()}</option>
          {options.map((option) => <option key={option.value} value={option.value} disabled={option.locked}>{option.label}</option>)}
        </select>
        {filter.lockedReason ? <small>{filter.lockedReason}</small> : null}
      </label>
    );
  }
  if (filter.type === 'boolean') {
    return (
      <label className="analytics-field">
        <span>{filter.label}</span>
        <select value={value ?? ''} disabled={filter.locked} onChange={(event) => onChange(event.target.value === '' ? '' : event.target.value === 'true')}>
          <option value="">All</option><option value="true">Yes</option><option value="false">No</option>
        </select>
      </label>
    );
  }
  return (
    <label className="analytics-field">
      <span>{filter.label}</span>
      <input
        type={filter.type === 'date' ? 'date' : filter.type === 'number' ? 'number' : 'text'}
        value={value ?? ''}
        min={filter.type === 'number' ? 1 : undefined}
        max={filter.type === 'number' ? 5000 : undefined}
        disabled={filter.locked}
        placeholder={filter.placeholder ?? ''}
        onChange={(event) => onChange(filter.type === 'number' ? Number(event.target.value || 500) : event.target.value)}
      />
      {filter.lockedReason ? <small>{filter.lockedReason}</small> : null}
    </label>
  );
}

function ResultTable({ result, definition }) {
  const rows = result?.rows ?? [];
  const columns = result?.columns ?? definition?.columns ?? [];
  if (!result) return <BlankState title="No report preview yet">Select criteria and choose Preview report or Run & save.</BlankState>;
  if (!rows.length) return <BlankState title={result.resultStatus === 'source_unavailable' ? 'Required source unavailable' : 'No matching analytics data'}>{result.message}</BlankState>;
  return (
    <div className="analytics-result-table-wrap">
      <table className="analytics-result-table">
        <thead><tr>{columns.map((column) => <th key={column.key} title={column.description}>{column.label}</th>)}</tr></thead>
        <tbody>
          {rows.map((row, index) => (
            <tr key={`${row.projectId ?? row.engineerUserId ?? row.dispatchId ?? 'row'}-${index}`}>
              {columns.map((column) => <td key={column.key}>{renderValue(row[column.key], column.dataType)}</td>)}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function renderValue(value, dataType) {
  if (value === null || value === undefined || value === '') return '—';
  if (dataType === 'currency') {
    const number = Number(value);
    return Number.isFinite(number) ? number.toLocaleString(undefined, { style: 'currency', currency: 'USD' }) : String(value);
  }
  if (dataType === 'percent') {
    const number = Number(value);
    return Number.isFinite(number) ? `${number.toLocaleString(undefined, { maximumFractionDigits: 2 })}%` : String(value);
  }
  if (dataType === 'date') return new Date(`${String(value).slice(0, 10)}T00:00:00`).toLocaleDateString();
  if (dataType === 'datetime') return dateTime(value);
  if (dataType === 'status') return <Status value={value} />;
  if (Array.isArray(value)) return value.join(', ');
  return String(value);
}

function SourceQuality({ sources = [] }) {
  return (
    <div className="analytics-source-grid">
      {sources.map((source) => (
        <article key={source.key}>
          <div><strong>{source.name}</strong><Status value={source.status} /></div>
          <p>{source.message}</p>
          <small>{source.required ? 'Required' : 'Optional'} · {source.recordCount ?? 0} records · {dateTime(source.observedAt)}</small>
          {source.diagnosticCode ? <code>{source.diagnosticCode}</code> : null}
        </article>
      ))}
      {!sources.length ? <BlankState title="No source evidence loaded">Preview a report to review source health.</BlankState> : null}
    </div>
  );
}

function ScheduleEditor({
  selectedReport,
  filters,
  schedules,
  recipientOptions,
  capabilities,
  onSaved,
  onRunNow,
  onDelete,
  busy
}) {
  const [selectedScheduleId, setSelectedScheduleId] = useState('');
  const [draft, setDraft] = useState(() => emptySchedule(selectedReport));
  const [manualEmail, setManualEmail] = useState('');
  const selectedSchedule = schedules.find((schedule) => schedule.scheduleId === selectedScheduleId);

  useEffect(() => {
    if (selectedSchedule) {
      setDraft({
        scheduleId: selectedSchedule.scheduleId,
        scheduleName: selectedSchedule.scheduleName,
        reportCode: selectedSchedule.reportCode,
        cadence: selectedSchedule.cadence,
        dayOfWeek: selectedSchedule.dayOfWeek ?? 1,
        dayOfMonth: selectedSchedule.dayOfMonth ?? 1,
        monthOfYear: selectedSchedule.monthOfYear ?? 1,
        localTime: String(selectedSchedule.localTime ?? '08:00').slice(0, 5),
        timezoneName: selectedSchedule.timezoneName,
        exportFormat: selectedSchedule.exportFormat,
        deliveryBoundary: selectedSchedule.deliveryBoundary,
        emailSubject: selectedSchedule.emailSubject,
        emailMessage: selectedSchedule.emailMessage,
        enabled: selectedSchedule.enabled,
        recipients: selectedSchedule.recipients ?? [],
        criteria: selectedSchedule.criteria ?? filters
      });
    }
  }, [selectedSchedule, filters]);

  useEffect(() => {
    if (!selectedScheduleId) setDraft((current) => ({ ...current, reportCode: selectedReport?.code ?? current.reportCode, criteria: filters }));
  }, [selectedReport, filters, selectedScheduleId]);

  function set(key, value) { setDraft((current) => ({ ...current, [key]: value })); }
  function selectRecipients(values) {
    const map = new Map(recipientOptions.filter((option) => option.userId).map((option) => [String(option.userId), option]));
    const retainedManual = draft.recipients.filter((recipient) => !recipient.userId);
    const selected = values.map((value) => map.get(String(value))).filter(Boolean).map((option) => ({
      userId: option.userId,
      displayName: option.displayName,
      email: option.email,
      recipientType: 'to'
    }));
    set('recipients', [...selected, ...retainedManual]);
  }
  function addManual() {
    const email = manualEmail.trim().toLowerCase();
    if (!email || !email.includes('@')) return;
    set('recipients', [...draft.recipients.filter((recipient) => recipient.email !== email), {
      userId: null,
      displayName: email,
      email,
      recipientType: 'to'
    }]);
    setManualEmail('');
  }
  function newSchedule() {
    setSelectedScheduleId('');
    setDraft(emptySchedule(selectedReport, filters));
  }

  const selectedRecipientIds = draft.recipients.filter((recipient) => recipient.userId).map((recipient) => String(recipient.userId));
  return (
    <section className="analytics-schedule-panel">
      <div className="analytics-panel-heading">
        <div><span>Scheduled Reports</span><h3>Recurring US Signal delivery</h3><p>Manage when, how, and to whom this role-scoped report is delivered.</p></div>
        <label className="analytics-toggle"><input type="checkbox" checked={draft.enabled} onChange={(event) => set('enabled', event.target.checked)} /><span>Enabled</span></label>
      </div>
      <div className="analytics-schedule-selector">
        <select value={selectedScheduleId} onChange={(event) => setSelectedScheduleId(event.target.value)}>
          <option value="">Create a new schedule</option>
          {schedules.map((schedule) => <option key={schedule.scheduleId} value={schedule.scheduleId}>{schedule.scheduleName}</option>)}
        </select>
        <button type="button" className="analytics-button secondary" onClick={newSchedule}>New</button>
      </div>
      <label className="analytics-field full"><span>Schedule name</span><input value={draft.scheduleName} onChange={(event) => set('scheduleName', event.target.value)} /></label>
      <div className="analytics-schedule-grid">
        <label className="analytics-field"><span>Cadence</span><select value={draft.cadence} onChange={(event) => set('cadence', event.target.value)}>{CADENCES.map(([value, label]) => <option key={value} value={value}>{label}</option>)}</select></label>
        {draft.cadence === 'weekly' ? <label className="analytics-field"><span>Day of week</span><select value={draft.dayOfWeek} onChange={(event) => set('dayOfWeek', Number(event.target.value))}>{WEEKDAYS.map(([value, label]) => <option key={value} value={value}>{label}</option>)}</select></label> : null}
        {['monthly', 'quarterly', 'yearly'].includes(draft.cadence) ? <label className="analytics-field"><span>Day of month</span><input type="number" min="1" max="31" value={draft.dayOfMonth} onChange={(event) => set('dayOfMonth', Number(event.target.value))} /></label> : null}
        {draft.cadence === 'yearly' ? <label className="analytics-field"><span>Month</span><select value={draft.monthOfYear} onChange={(event) => set('monthOfYear', Number(event.target.value))}>{Array.from({ length: 12 }, (_, index) => <option key={index + 1} value={index + 1}>{new Date(2026, index, 1).toLocaleString(undefined, { month: 'long' })}</option>)}</select></label> : null}
        <label className="analytics-field"><span>Time</span><input type="time" value={draft.localTime} onChange={(event) => set('localTime', event.target.value)} /></label>
        <label className="analytics-field"><span>Time zone</span><select value={draft.timezoneName} onChange={(event) => set('timezoneName', event.target.value)}>{TIMEZONES.map(([value, label]) => <option key={value} value={value}>{label}</option>)}</select></label>
      </div>
      <AnalyticsMultiSelect
        label="Recipients"
        options={recipientOptions.map((option) => ({ value: option.userId, label: option.displayName, detail: `${option.email}${option.jobTitle ? ` · ${option.jobTitle}` : ''}` }))}
        values={selectedRecipientIds}
        onChange={selectRecipients}
        placeholder="Select recipients"
      />
      {capabilities?.canDeliverMultipleRecipients ? (
        <div className="analytics-manual-recipient"><input type="email" value={manualEmail} onChange={(event) => setManualEmail(event.target.value)} placeholder="Additional @ussignal.com recipient" /><button type="button" onClick={addManual}>+ Add recipient</button></div>
      ) : null}
      <div className="analytics-recipient-chips">
        {draft.recipients.map((recipient) => <span key={`${recipient.userId ?? 'email'}-${recipient.email}`}>{recipient.displayName || recipient.email}<button type="button" onClick={() => set('recipients', draft.recipients.filter((item) => item.email !== recipient.email))}>×</button></span>)}
      </div>
      <p className="analytics-schedule-note">ⓘ Multiple active ProjectPulse users receive individual copies generated under each recipient’s own authorization scope. Module 065 owns Entra Secret Administration, SMTP/Graph configuration, sender identity, delivery boundaries, and transmission.</p>
      <div className="analytics-format-choice">
        <span>Format</span>
        <label><input type="radio" name="schedule-format" checked={draft.exportFormat === 'pdf'} onChange={() => set('exportFormat', 'pdf')} /> US Signal PDF</label>
        <label><input type="radio" name="schedule-format" checked={draft.exportFormat === 'xlsx'} onChange={() => set('exportFormat', 'xlsx')} /> Excel</label>
      </div>
      <label className="analytics-field full"><span>Email subject</span><input value={draft.emailSubject} onChange={(event) => set('emailSubject', event.target.value)} placeholder={`US Signal Analytics: ${selectedReport?.name ?? 'Scheduled report'}`} /></label>
      <label className="analytics-field full"><span>Email message</span><textarea value={draft.emailMessage} onChange={(event) => set('emailMessage', event.target.value)} placeholder="Optional delivery message" /></label>
      <div className="analytics-schedule-actions">
        {draft.scheduleId ? <button type="button" className="analytics-button danger" onClick={() => onDelete(draft.scheduleId)}>Delete schedule</button> : <span />}
        <div>
          {draft.scheduleId ? <button type="button" className="analytics-button secondary" disabled={busy} onClick={() => onRunNow(draft.scheduleId)}>Run now</button> : null}
          <button type="button" className="analytics-button primary" disabled={busy || !draft.recipients.length} onClick={() => onSaved({ ...draft, criteria: filters })}>{busy ? 'Saving…' : 'Save schedule'}</button>
        </div>
      </div>
    </section>
  );
}

function emptySchedule(report, filters = {}) {
  return {
    scheduleId: null,
    scheduleName: `${report?.name ?? 'Analytics report'} — Weekly`,
    reportCode: report?.code ?? DEFAULT_REPORT,
    cadence: 'weekly',
    dayOfWeek: 1,
    dayOfMonth: 1,
    monthOfYear: 1,
    localTime: '08:00',
    timezoneName: 'America/New_York',
    exportFormat: 'pdf',
    deliveryBoundary: 'test_only',
    emailSubject: '',
    emailMessage: '',
    enabled: true,
    recipients: [],
    criteria: filters
  };
}

export default function AnalyticsCenter({ authSession }) {
  const [section, setSection] = useState('analytics');
  const [workspace, setWorkspace] = useState('my');
  const [overview, setOverview] = useState(null);
  const [catalog, setCatalog] = useState([]);
  const [catalogSearch, setCatalogSearch] = useState('');
  const [expandedCategories, setExpandedCategories] = useState(() => new Set(['Financials']));
  const [selectedReportCode, setSelectedReportCode] = useState(DEFAULT_REPORT);
  const [reportTab, setReportTab] = useState('criteria');
  const [filterDefinition, setFilterDefinition] = useState(null);
  const [filterOptions, setFilterOptions] = useState({});
  const [filters, setFilters] = useState({ limit: 500 });
  const [result, setResult] = useState(null);
  const [runId, setRunId] = useState('');
  const [history, setHistory] = useState([]);
  const [schedules, setSchedules] = useState([]);
  const [scheduleRuns, setScheduleRuns] = useState([]);
  const [recipientOptions, setRecipientOptions] = useState([]);
  const [readiness, setReadiness] = useState(null);
  const [loading, setLoading] = useState({ bootstrap: true, filters: false, report: false, schedule: false });
  const [message, setMessage] = useState({ type: '', text: '' });
  const [sidebarCollapsed, setSidebarCollapsed] = useState(false);

  const selectedReport = useMemo(() => catalog.find((report) => report.code === selectedReportCode) ?? catalog[0] ?? null, [catalog, selectedReportCode]);
  const categories = useMemo(() => [...new Set(catalog.map((report) => report.category))], [catalog]);
  const reportsByCategory = useMemo(() => Object.fromEntries(categories.map((category) => [category, catalog.filter((report) => report.category === category)])), [catalog, categories]);
  const visibleReports = useMemo(() => {
    const query = catalogSearch.trim().toLowerCase();
    if (!query) return catalog;
    return catalog.filter((report) => `${report.name} ${report.category} ${report.description} ${(report.modules ?? []).join(' ')}`.toLowerCase().includes(query));
  }, [catalog, catalogSearch]);
  const capabilities = overview?.capabilities ?? {};
  const sources = result?.sources ?? overview?.sourceQuality?.sources ?? [];

  const loadHistory = useCallback(async () => {
    try {
      const { payload } = await api('/api/analytics/v2/history?limit=100', authSession);
      setHistory(payload?.history ?? []);
    } catch (error) {
      setMessage({ type: 'warning', text: `Run history: ${error.message}` });
    }
  }, [authSession]);

  const loadSchedules = useCallback(async () => {
    try {
      const [scheduleResponse, runResponse, readinessResponse, recipientResponse] = await Promise.allSettled([
        api('/api/analytics/v2/schedules', authSession),
        api('/api/analytics/v2/schedule-runs?limit=100', authSession),
        api('/api/analytics/v2/schedules/readiness', authSession),
        api('/api/analytics/v2/recipient-options', authSession)
      ]);
      if (scheduleResponse.status === 'fulfilled') setSchedules(scheduleResponse.value.payload?.schedules ?? []);
      if (runResponse.status === 'fulfilled') setScheduleRuns(runResponse.value.payload?.runs ?? []);
      if (readinessResponse.status === 'fulfilled') setReadiness(readinessResponse.value.payload);
      if (recipientResponse.status === 'fulfilled') setRecipientOptions(recipientResponse.value.payload?.recipients ?? []);
    } catch {
      // Individual settled responses preserve available schedule content.
    }
  }, [authSession]);

  const bootstrap = useCallback(async () => {
    setLoading((current) => ({ ...current, bootstrap: true }));
    const [overviewResponse, catalogResponse] = await Promise.allSettled([
      api('/api/analytics/v2/overview', authSession),
      api('/api/analytics/v2/catalog', authSession)
    ]);
    if (overviewResponse.status === 'fulfilled') setOverview(overviewResponse.value.payload);
    else setMessage({ type: 'critical', text: overviewResponse.reason?.message ?? 'Analytics overview is unavailable.' });
    if (catalogResponse.status === 'fulfilled') {
      const reports = catalogResponse.value.payload?.reports ?? [];
      setCatalog(reports);
      if (!reports.some((report) => report.code === selectedReportCode)) setSelectedReportCode(reports[0]?.code ?? '');
    } else setMessage({ type: 'critical', text: catalogResponse.reason?.message ?? 'Analytics catalog is unavailable.' });
    await Promise.allSettled([loadHistory(), loadSchedules()]);
    setLoading((current) => ({ ...current, bootstrap: false }));
  }, [authSession, loadHistory, loadSchedules, selectedReportCode]);

  useEffect(() => { void bootstrap(); }, [bootstrap]);

  const loadFilters = useCallback(async () => {
    if (!selectedReportCode) return;
    setLoading((current) => ({ ...current, filters: true }));
    try {
      const { payload } = await api('/api/analytics/v2/filter-options', authSession, {
        method: 'POST',
        body: JSON.stringify(buildRequest(selectedReportCode, filters))
      });
      const data = payload?.payload ?? {};
      setFilterDefinition(data.definition ?? null);
      setFilterOptions(data.options?.options ?? {});
      const locked = data.options?.lockedValues ?? {};
      setFilters((current) => ({ ...current, ...locked }));
    } catch (error) {
      setMessage({ type: 'critical', text: `Filter lists: ${error.message}` });
    } finally {
      setLoading((current) => ({ ...current, filters: false }));
    }
  }, [authSession, selectedReportCode, filters.customerIds, filters.projectIds, filters.teamIds]);

  useEffect(() => { void loadFilters(); }, [loadFilters]);

  useEffect(() => {
    if (!selectedReportCode) return;
    void api(`/api/analytics/v2/activity/${encodeURIComponent(selectedReportCode)}/view`, authSession, { method: 'POST', body: '{}' }).catch(() => {});
  }, [authSession, selectedReportCode]);

  function selectReport(code) {
    setSelectedReportCode(code);
    setResult(null);
    setRunId('');
    setFilters({ limit: 500 });
    setReportTab('criteria');
    setSection('reports');
    const report = catalog.find((item) => item.code === code);
    if (report) setExpandedCategories((current) => new Set([...current, report.category]));
  }

  function updateFilter(key, value) { setFilters((current) => ({ ...current, [key]: value })); }
  function clearCriteria() {
    const locked = {};
    for (const filter of filterDefinition?.filters ?? []) if (filter.locked) locked[filter.key] = filter.defaultValue;
    setFilters({ limit: 500, ...locked });
    setResult(null);
    setRunId('');
    setMessage({ type: 'neutral', text: 'Criteria cleared. Role-locked scope remains active.' });
  }
  function saveCriteria() {
    try {
      const key = `projectPulseAnalyticsCriteria:${selectedReportCode}`;
      window.localStorage.setItem(key, JSON.stringify(filters));
      setMessage({ type: 'healthy', text: 'Criteria saved in this browser for the selected report.' });
    } catch {
      setMessage({ type: 'warning', text: 'This browser did not allow criteria storage.' });
    }
  }
  function restoreCriteria() {
    try {
      const saved = JSON.parse(window.localStorage.getItem(`projectPulseAnalyticsCriteria:${selectedReportCode}`) ?? 'null');
      if (saved) {
        setFilters(saved);
        setMessage({ type: 'healthy', text: 'Saved criteria restored.' });
      }
    } catch {
      setMessage({ type: 'warning', text: 'Saved criteria could not be restored.' });
    }
  }

  async function execute(persisted) {
    if (!selectedReport) return;
    setLoading((current) => ({ ...current, report: true }));
    setMessage({ type: '', text: '' });
    try {
      const { payload } = await api(`/api/analytics/v2/${persisted ? 'run' : 'preview'}`, authSession, {
        method: 'POST',
        body: JSON.stringify(buildRequest(selectedReport.code, filters))
      });
      setResult(payload?.result ?? null);
      setRunId(payload?.runId ?? '');
      setSection('reports');
      setReportTab('criteria');
      setMessage({ type: statusTone(payload?.result?.resultStatus), text: payload?.result?.message ?? 'Analytics report completed.' });
      if (persisted) await Promise.allSettled([loadHistory(), bootstrap()]);
    } catch (error) {
      setMessage({ type: 'critical', text: error.message ?? 'The Analytics report could not be generated.' });
    } finally {
      setLoading((current) => ({ ...current, report: false }));
    }
  }

  async function exportRun(format) {
    if (!runId) return;
    setLoading((current) => ({ ...current, report: true }));
    try {
      const response = await fetch(`/api/analytics/v2/runs/${runId}/export?format=${format}`, { credentials: 'include', headers: headers(authSession) });
      if (!response.ok) {
        const payload = await response.json().catch(() => null);
        throw new Error(payload?.message ?? `Export returned HTTP ${response.status}.`);
      }
      const blob = await response.blob();
      const disposition = response.headers.get('content-disposition') ?? '';
      const match = disposition.match(/filename\*?=(?:UTF-8''|\")?([^\";]+)/i);
      const fileName = match?.[1] ? decodeURIComponent(match[1].replaceAll('"', '')) : `ussignal-analytics.${format}`;
      const url = URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url;
      link.download = fileName;
      document.body.appendChild(link);
      link.click();
      link.remove();
      URL.revokeObjectURL(url);
      setMessage({ type: 'healthy', text: `${format === 'pdf' ? 'US Signal PDF' : format.toUpperCase()} export created.` });
    } catch (error) {
      setMessage({ type: 'critical', text: error.message ?? 'The export could not be created.' });
    } finally {
      setLoading((current) => ({ ...current, report: false }));
    }
  }

  async function favoriteReport(code, favorite) {
    try {
      await api(`/api/analytics/v2/activity/${encodeURIComponent(code)}/favorite`, authSession, { method: 'PUT', body: JSON.stringify({ favorite }) });
      await bootstrap();
    } catch (error) { setMessage({ type: 'warning', text: error.message }); }
  }

  async function saveSchedule(draft) {
    setLoading((current) => ({ ...current, schedule: true }));
    try {
      const path = draft.scheduleId ? `/api/analytics/v2/schedules/${draft.scheduleId}` : '/api/analytics/v2/schedules';
      const { payload } = await api(path, authSession, {
        method: draft.scheduleId ? 'PUT' : 'POST',
        body: JSON.stringify({
          ...draft,
          localTime: `${draft.localTime}:00`,
          criteria: buildRequest(draft.reportCode, draft.criteria)
        })
      });
      setMessage({ type: 'healthy', text: payload?.message ?? 'Schedule saved.' });
      await Promise.allSettled([loadSchedules(), bootstrap()]);
    } catch (error) { setMessage({ type: 'critical', text: error.message }); }
    finally { setLoading((current) => ({ ...current, schedule: false })); }
  }
  async function deleteSchedule(id) {
    if (!window.confirm('Delete this recurring Analytics schedule? Immutable prior delivery evidence will remain.')) return;
    setLoading((current) => ({ ...current, schedule: true }));
    try {
      const { payload } = await api(`/api/analytics/v2/schedules/${id}`, authSession, { method: 'DELETE' });
      setMessage({ type: 'healthy', text: payload?.message ?? 'Schedule deleted.' });
      await loadSchedules();
    } catch (error) { setMessage({ type: 'critical', text: error.message }); }
    finally { setLoading((current) => ({ ...current, schedule: false })); }
  }
  async function runScheduleNow(id) {
    setLoading((current) => ({ ...current, schedule: true }));
    try {
      const { payload } = await api(`/api/analytics/v2/schedules/${id}/run-now`, authSession, { method: 'POST', body: '{}' });
      setMessage({ type: 'healthy', text: payload?.summary?.message ?? 'Schedule run completed.' });
      await Promise.allSettled([loadSchedules(), bootstrap()]);
    } catch (error) { setMessage({ type: 'critical', text: error.message }); }
    finally { setLoading((current) => ({ ...current, schedule: false })); }
  }

  const reportWorkspace = (
    <div className="analytics-report-workspace-grid">
      <section className="analytics-report-library analytics-build-layout">
        <div className="analytics-library-heading"><div><h2>Report Library</h2><p>Explore and run analytics across every authorized area of ProjectPulse.</p></div><input type="search" value={catalogSearch} onChange={(event) => setCatalogSearch(event.target.value)} placeholder="Search reports…" /></div>
        <div className="analytics-library-body">
          <aside className="analytics-report-categories">
            {categories.map((category) => {
              const expanded = expandedCategories.has(category);
              const matching = (reportsByCategory[category] ?? []).filter((report) => visibleReports.includes(report));
              if (catalogSearch && !matching.length) return null;
              return (
                <div key={category}>
                  <button type="button" className={selectedReport?.category === category ? 'active' : ''} onClick={() => setExpandedCategories((current) => { const next = new Set(current); if (next.has(category)) next.delete(category); else next.add(category); return next; })}><span>{CATEGORY_ICONS[category] ?? '◈'}</span><strong>{category}</strong><em>{expanded ? '⌃' : '⌄'}</em></button>
                  {expanded ? <div className="analytics-category-reports">{matching.map((report) => <button type="button" key={report.code} className={selectedReportCode === report.code ? 'active' : ''} onClick={() => selectReport(report.code)}>{report.name}</button>)}</div> : null}
                </div>
              );
            })}
          </aside>
          <div className="analytics-selected-report">
            {selectedReport ? (
              <>
                <div className="analytics-selected-report-heading">
                  <div><span>{selectedReport.category} · Modules {(selectedReport.modules ?? []).join(', ')}</span><h2>{selectedReport.name}</h2><p>{selectedReport.description}</p></div>
                  <div><button type="button" className="analytics-button secondary" disabled={loading.report} onClick={() => execute(false)}>◉ Preview Report</button><button type="button" className="analytics-button primary" disabled={loading.report} onClick={() => execute(true)}>▶ Run Report</button></div>
                </div>
                <div className="analytics-report-tabs"><button type="button" className={reportTab === 'criteria' ? 'active' : ''} onClick={() => setReportTab('criteria')}>Criteria</button><button type="button" className={reportTab === 'schedules' ? 'active' : ''} onClick={() => setReportTab('schedules')}>Schedules</button><button type="button" className={reportTab === 'about' ? 'active' : ''} onClick={() => setReportTab('about')}>About this Report</button></div>
                {reportTab === 'criteria' ? (
                  <div className="analytics-criteria-panel">
                    <div className="analytics-criteria-intro"><strong>Set criteria</strong><span>Only criteria relevant to {selectedReport.name} are shown.</span><button type="button" onClick={loadFilters} disabled={loading.filters}>{loading.filters ? 'Refreshing…' : 'Refresh filter lists'}</button></div>
                    <div className="analytics-filter-grid">{(filterDefinition?.filters ?? []).map((filter) => <FilterControl key={filter.key} filter={filter} options={filterOptions[filter.optionSource] ?? []} value={filters[filter.key] ?? filter.defaultValue ?? (filter.type === 'multiselect' ? [] : '')} onChange={(value) => updateFilter(filter.key, value)} />)}</div>
                    <div className="analytics-criteria-actions"><div><button type="button" className="analytics-button secondary" onClick={clearCriteria}>Clear Criteria</button><button type="button" className="analytics-button secondary" onClick={restoreCriteria}>Restore Criteria</button><button type="button" className="analytics-button secondary" onClick={saveCriteria}>Save Criteria</button></div><div><button type="button" className="analytics-button secondary" disabled={loading.report} onClick={() => execute(false)}>Preview report</button><button type="button" className="analytics-button primary" disabled={loading.report} onClick={() => execute(true)}>{loading.report ? 'Running…' : 'Run & save'}</button></div></div>
                    {result ? <div className="analytics-inline-results"><div className="analytics-result-heading"><div><span>Actual analytics results</span><h3>{result.reportName}</h3><p>{result.message}</p></div><Status value={result.resultStatus} /></div><ResultTable result={result} definition={filterDefinition} />{runId ? <div className="analytics-export-actions"><strong>Export this report</strong><button type="button" className="analytics-button pdf" onClick={() => exportRun('pdf')}>US Signal PDF</button><button type="button" className="analytics-button excel" onClick={() => exportRun('xlsx')}>Excel</button><details><summary>Additional formats</summary>{LEGACY_COMPATIBLE_EXPORTS.filter((format) => !PRIMARY_EXPORTS.includes(format)).map((format) => <button type="button" key={format} onClick={() => exportRun(format)}>{format.toUpperCase()}</button>)}</details></div> : null}</div> : null}
                  </div>
                ) : null}
                {reportTab === 'schedules' ? <ScheduleEditor selectedReport={selectedReport} filters={filters} schedules={schedules.filter((schedule) => schedule.reportCode === selectedReport.code)} recipientOptions={recipientOptions} capabilities={capabilities} onSaved={saveSchedule} onRunNow={runScheduleNow} onDelete={deleteSchedule} busy={loading.schedule} /> : null}
                {reportTab === 'about' ? <div className="analytics-about-report"><h3>About this report</h3><p>{selectedReport.description}</p><dl><div><dt>Audience</dt><dd>{(selectedReport.audience ?? []).join(', ')}</dd></div><div><dt>Scope rule</dt><dd>{selectedReport.scopeRule}</dd></div><div><dt>Required sources</dt><dd>{(selectedReport.requiredSources ?? []).join(', ') || 'None'}</dd></div><div><dt>Optional sources</dt><dd>{(selectedReport.optionalSources ?? []).join(', ') || 'None'}</dd></div><div><dt>Available exports</dt><dd>Official US Signal PDF, branded Excel, CSV, and JSON</dd></div></dl></div> : null}
              </>
            ) : <BlankState title="No report is available">The current role does not have an Analytics Center report in scope.</BlankState>}
          </div>
        </div>
      </section>
      {section === 'schedules' || reportTab === 'schedules' ? null : <ScheduleEditor selectedReport={selectedReport} filters={filters} schedules={schedules.filter((schedule) => schedule.reportCode === selectedReport?.code)} recipientOptions={recipientOptions} capabilities={capabilities} onSaved={saveSchedule} onRunNow={runScheduleNow} onDelete={deleteSchedule} busy={loading.schedule} />}
    </div>
  );

  return (
    <section className={`analytics-center analytics-enterprise-shell ${sidebarCollapsed ? 'sidebar-collapsed' : ''}`} data-projectpulse-module="030" data-enterprise-analytics="true">
      <aside className="analytics-sidebar">
        <div className="analytics-sidebar-brand"><USSignalLogo size="large" alt="US Signal" /><button type="button" onClick={() => setSidebarCollapsed((current) => !current)} aria-label={sidebarCollapsed ? 'Expand Analytics navigation' : 'Collapse Analytics navigation'}>{sidebarCollapsed ? '»' : '«'}</button></div>
        <nav aria-label="Analytics Center"><div>{NAVIGATION.map(([key, icon, label]) => <button type="button" key={key} className={(section === key || key === 'analytics' && section === 'overview') ? 'active' : ''} onClick={() => setSection(key)}><span>{icon}</span><strong>{label}</strong></button>)}</div><h3>Workspaces</h3><div>{WORKSPACES.map(([key, label]) => <button type="button" key={key} className={workspace === key ? 'active-workspace' : ''} onClick={() => setWorkspace(key)}><span>♙</span><strong>{label}</strong></button>)}</div></nav>
        <div className="analytics-sidebar-return"><button type="button" onClick={() => navigate('#modules')}>← <strong>Back to Modules</strong></button><button type="button" onClick={() => navigate('#dashboard')}>← <strong>Back to Dashboard</strong></button></div>
      </aside>
      <main className="analytics-main">
        <header className="analytics-topbar">
          <div><h1>Analytics Center</h1><p>Data-driven insights across your projects, people, financials, and performance.</p></div>
          <div className="analytics-topbar-actions"><label><span>⌕</span><input type="search" value={catalogSearch} onChange={(event) => setCatalogSearch(event.target.value)} placeholder="Search reports, dashboards, and more…" /></label><button type="button" onClick={bootstrap}>↻ Refresh</button><div className="analytics-profile"><span>{(overview?.access?.displayName ?? authSession?.displayName ?? 'User').split(/\s+/).map((part) => part[0]).join('').slice(0, 2).toUpperCase()}</span><div><strong>{overview?.access?.displayName ?? authSession?.displayName ?? 'ProjectPulse User'}</strong><small>{(overview?.access?.roles ?? []).map(words).join(', ') || 'Authorized user'}</small></div></div></div>
        </header>
        {message.text ? <div className={`analytics-message ${message.type}`} role="status"><span>{message.type === 'critical' ? '!' : message.type === 'healthy' ? '✓' : 'i'}</span><p>{message.text}</p><button type="button" onClick={() => setMessage({ type: '', text: '' })}>×</button></div> : null}
        {loading.bootstrap ? <div className="analytics-loading">Loading role-scoped Analytics Center data…</div> : null}

        {['overview', 'analytics', 'dashboards', 'kpis'].includes(section) ? (
          <>
            <div className="analytics-refresh-line"><span>◷ Data refreshed: {dateTime(overview?.dataAsOf ?? overview?.generatedAt)}</span><button type="button" onClick={() => setSection('data-quality')}>Filter Dashboard</button></div>
            <div className="analytics-kpi-grid">{(overview?.metrics ?? []).map((metric) => <MetricCard key={metric.key} metric={metric} />)}</div>
            {section !== 'kpis' ? <section className="analytics-recent-section"><div className="analytics-section-heading"><h2>Recently Viewed Dashboards & Reports</h2><button type="button" onClick={() => setSection('reports')}>View all</button></div><div className="analytics-recent-grid">{(overview?.recentlyViewed ?? []).map((item) => <RecentCard key={item.reportCode} item={item} onOpen={selectReport} onFavorite={favoriteReport} />)}</div></section> : null}
          </>
        ) : null}

        {['overview', 'analytics', 'dashboards', 'reports'].includes(section) ? reportWorkspace : null}
        {section === 'schedules' ? <div className="analytics-schedules-page"><div className="analytics-section-heading"><div><h2>Scheduled Reports</h2><p>Recurring individualized US Signal PDF or Excel delivery through Module 065.</p></div><Status value={readiness?.status ?? 'unknown'} /></div><div className="analytics-schedule-page-grid"><ScheduleEditor selectedReport={selectedReport} filters={filters} schedules={schedules} recipientOptions={recipientOptions} capabilities={capabilities} onSaved={saveSchedule} onRunNow={runScheduleNow} onDelete={deleteSchedule} busy={loading.schedule} /><section className="analytics-schedule-history"><h3>Immutable delivery history</h3><div className="analytics-history-list">{scheduleRuns.map((run) => <article key={run.scheduleRunId}><div><strong>{run.scheduleName}</strong><span>{dateTime(run.completedAt)}</span></div><Status value={run.runStatus} /><small>{run.sentCount} sent · {run.queuedCount} queued · {run.failedCount} failed</small></article>)}{!scheduleRuns.length ? <BlankState title="No recurring deliveries yet">Create a schedule or run one now.</BlankState> : null}</div></section></div></div> : null}
        {section === 'data-explorer' ? <section className="analytics-data-explorer"><div className="analytics-section-heading"><div><h2>Data Explorer</h2><p>Inspect the current report result and source evidence without changing report scope.</p></div>{result ? <Status value={result.resultStatus} /> : null}</div><ResultTable result={result} definition={filterDefinition} /><SourceQuality sources={sources} /></section> : null}
        {section === 'alerts' ? <section className="analytics-admin-page"><h2>Alerts & Subscriptions</h2><p>Recurring report schedules, failed deliveries, and source-quality warnings are consolidated here.</p><div className="analytics-summary-cards"><article><strong>{schedules.filter((item) => item.enabled).length}</strong><span>Enabled schedules</span></article><article><strong>{scheduleRuns.filter((item) => item.runStatus === 'failed').length}</strong><span>Failed schedule runs</span></article><article><strong>{sources.filter((item) => item.status !== 'healthy').length}</strong><span>Degraded sources</span></article></div><button type="button" className="analytics-button primary" onClick={() => setSection('schedules')}>Manage schedules</button></section> : null}
        {section === 'data-quality' ? <section className="analytics-data-quality"><div className="analytics-section-heading"><div><h2>Data Quality</h2><p>Every source reports independently so one unavailable source does not blank a complete report.</p></div><Status value={overview?.sourceQuality?.degraded ? 'partial' : 'healthy'} /></div><SourceQuality sources={sources} /></section> : null}
        {section === 'admin' ? <section className="analytics-admin-page"><h2>Analytics Administration</h2><div className="analytics-summary-cards"><article><strong>{catalog.length}</strong><span>Role-scoped reports</span></article><article><strong>{schedules.length}</strong><span>Visible schedules</span></article><article><strong>{readiness?.module065?.configuredProvider ?? 'Not checked'}</strong><span>Module 065 provider</span></article><article><strong>{readiness?.migration?.ready ? 'Ready' : 'Required'}</strong><span>Migration 060</span></article></div><dl><div><dt>Report scope</dt><dd>{overview?.access?.engineerSelfScope ? 'Engineer — self only' : overview?.access?.projectManagerOwnPortfolio ? 'PM — own portfolio' : 'Authorized organization/project scope'}</dd></div><div><dt>Module 065</dt><dd>{readiness?.module065?.message ?? 'Readiness not loaded.'}</dd></div><div><dt>Schedule execution</dt><dd>Multi-replica advisory lock, per-recipient authorization, immutable run and delivery evidence.</dd></div><div><dt>Export standards</dt><dd>Official US Signal PDF and branded Excel across every persisted report.</dd></div></dl></section> : null}

        {history.length && section === 'reports' ? <section className="analytics-history-panel"><div className="analytics-section-heading"><div><h2>Analytics run history</h2><p>Immutable executions and source evidence for your authorized scope.</p></div><button type="button" onClick={loadHistory}>Refresh history</button></div><div className="analytics-history-list">{history.map((run) => <article key={run.runId}><button type="button" onClick={() => { selectReport(run.reportCode); setRunId(run.runId); }}><strong>{run.reportName}</strong><span>{dateTime(run.completedAt)}</span><small>{run.rowCount} rows</small></button><Status value={run.resultStatus} /><div><button type="button" onClick={() => { setRunId(run.runId); setTimeout(() => exportRun('pdf'), 0); }}>PDF</button><button type="button" onClick={() => { setRunId(run.runId); setTimeout(() => exportRun('xlsx'), 0); }}>Excel</button></div></article>)}</div></section> : null}
        <footer className="analytics-coverage-footer"><span>ⓘ</span><p>Analytics across everything in ProjectPulse: {(overview?.coverage ?? ['Financials', 'Engineers', 'Project Managers', 'Customers', 'Projects', 'Teams', 'Billing', 'Time', 'Utilization', 'Closeout', 'Service Delivery']).join(' · ')}</p><button type="button" onClick={() => navigate('#system-user-guide')}>Learn more about Analytics ↗</button></footer>
      </main>
    </section>
  );
}

function buildRequest(reportCode, filters) {
  return {
    reportCode,
    search: filters.search || null,
    customerIds: filters.customerIds ?? [],
    projectIds: filters.projectIds ?? [],
    projectManagerUserIds: filters.projectManagerUserIds ?? [],
    engineerUserIds: filters.engineerUserIds ?? [],
    teamIds: filters.teamIds ?? [],
    contractTypes: filters.contractTypes ?? [],
    projectStatus: filters.projectStatus || null,
    budgetStatus: filters.budgetStatus || null,
    billable: filters.billable === '' ? null : filters.billable,
    dateFrom: filters.dateFrom || null,
    dateTo: filters.dateTo || null,
    workflowStatus: filters.workflowStatus || null,
    severity: filters.severity || null,
    moduleCode: filters.moduleCode || null,
    sourceStatus: filters.sourceStatus || null,
    limit: Number(filters.limit || 500)
  };
}
