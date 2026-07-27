import { useEffect, useMemo, useState } from 'react';
import { createRoot } from 'react-dom/client';
import './audit-history.css';

function getProjectPulseAuthHeaders() {
  try {
    const raw = window.localStorage.getItem('projectPulseAuthSession');
    if (!raw) return {};
    const session = JSON.parse(raw);
    const token = session?.sessionToken || session?.token || session?.accessToken || '';
    if (!token) return {};

    return {
      'X-ProjectPulse-Session': token,
      'X-Project-Pulse-Session': token,
      'X-Session-Token': token,
      Authorization: `Bearer ${token}`
    };
  } catch {
    return {};
  }
}

async function readApiErrorMessage(response, path) {
  const raw = await response.text();
  if (!raw) return `${path} returned HTTP ${response.status}`;

  try {
    const parsed = JSON.parse(raw);
    return parsed.message || parsed.detail || parsed.status || raw;
  } catch {
    return raw;
  }
}

async function fetchJson(path) {
  const response = await fetch(path, { headers: getProjectPulseAuthHeaders() });
  if (!response.ok) throw new Error(await readApiErrorMessage(response, path));
  return response.json();
}

function formatDateTime(value) {
  if (!value) return 'Time not recorded';
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? String(value) : parsed.toLocaleString();
}

function humanize(value) {
  return String(value || 'System')
    .replaceAll('_', ' ')
    .replaceAll('-', ' ')
    .replace(/\b\w/g, (letter) => letter.toUpperCase());
}

function statusLabel(status) {
  const labels = {
    success: 'Success',
    failure: 'Failure',
    warning: 'Warning',
    pending: 'Pending',
    info: 'Information'
  };
  return labels[status] || humanize(status);
}

function compactValue(value) {
  if (value === null || value === undefined || value === '') return 'Not recorded';
  if (typeof value === 'object') return JSON.stringify(value, null, 2);
  return String(value);
}

export default function AuditHistoryPanel({ recoveryMode = false } = {}) {
  const [filters, setFilters] = useState({
    days: '14',
    category: 'all',
    status: 'all',
    source: 'all',
    search: ''
  });
  const [submittedSearch, setSubmittedSearch] = useState('');
  const [auditData, setAuditData] = useState({
    loading: true,
    data: null,
    error: ''
  });

  async function loadAuditHistory() {
    setAuditData((current) => ({ ...current, loading: true, error: '' }));
    const params = new URLSearchParams({
      days: filters.days,
      category: filters.category,
      status: filters.status,
      source: filters.source,
      search: submittedSearch,
      limit: '500'
    });

    try {
      const result = await fetchJson(`/api/admin/audit-history/events?${params.toString()}`);
      setAuditData({ loading: false, data: result, error: '' });
    } catch (error) {
      setAuditData({
        loading: false,
        data: null,
        error: error instanceof Error ? error.message : 'Audit and History could not be loaded.'
      });
    }
  }

  useEffect(() => {
    void loadAuditHistory();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [filters.days, filters.category, filters.status, filters.source, submittedSearch]);

  const events = auditData.data?.events ?? [];
  const summary = auditData.data?.summary ?? {
    total: 0,
    success: 0,
    failure: 0,
    warning: 0,
    pending: 0,
    info: 0,
    immutable: 0
  };
  const availableSources = useMemo(
    () => (auditData.data?.sourceStates ?? []).filter((source) => source.status === 'available'),
    [auditData.data]
  );

  function applySearch(event) {
    event.preventDefault();
    setSubmittedSearch(filters.search.trim());
  }

  return (
    <section
      id="audit-history"
      className="panel audit-history-panel"
      data-module-008-route-recovery={recoveryMode ? 'true' : undefined}
    >
      <div className="audit-history-hero">
        <div>
          <p className="eyebrow">Module 008 · Security & Audit</p>
          <h1>Audit and History</h1>
          <p>
            Review administrative changes, authentication activity, approvals, notifications,
            integrations, service actions, API lifecycle events, and other system history from one place.
            Select an event to open its sanitized evidence and source details.
          </p>
        </div>
        <div className="audit-history-hero-actions">
          <span className={auditData.data?.centralAudit?.immutable ? 'audit-ledger-state ready' : 'audit-ledger-state'}>
            {auditData.data?.centralAudit?.immutable ? 'Immutable ledger ready' : 'Immutable ledger pending migration'}
          </span>
          <button type="button" className="secondary-action" onClick={loadAuditHistory} disabled={auditData.loading}>
            {auditData.loading ? 'Refreshing…' : 'Refresh history'}
          </button>
        </div>
      </div>

      <div className="audit-summary-grid" aria-label="Audit summary">
        {[
          ['All events', summary.total, 'all'],
          ['Successful', summary.success, 'success'],
          ['Warnings', summary.warning, 'warning'],
          ['Failures', summary.failure, 'failure'],
          ['Pending', summary.pending, 'pending'],
          ['Immutable', summary.immutable, 'immutable']
        ].map(([label, value, tone]) => (
          <article className={`audit-summary-card ${tone}`} key={label}>
            <span>{label}</span>
            <strong>{value ?? 0}</strong>
          </article>
        ))}
      </div>

      <form className="audit-filter-bar" onSubmit={applySearch}>
        <label>
          Lookback
          <select value={filters.days} onChange={(event) => setFilters((current) => ({ ...current, days: event.target.value }))}>
            <option value="1">Last 24 hours</option>
            <option value="7">Last 7 days</option>
            <option value="14">Last 14 days</option>
            <option value="30">Last 30 days</option>
            <option value="90">Last 90 days</option>
            <option value="365">Last year</option>
            <option value="3650">All retained history</option>
          </select>
        </label>

        <label>
          Category
          <select value={filters.category} onChange={(event) => setFilters((current) => ({ ...current, category: event.target.value }))}>
            <option value="all">All categories</option>
            {(auditData.data?.categories ?? []).map((category) => (
              <option value={category} key={category}>{humanize(category)}</option>
            ))}
          </select>
        </label>

        <label>
          Status
          <select value={filters.status} onChange={(event) => setFilters((current) => ({ ...current, status: event.target.value }))}>
            <option value="all">All statuses</option>
            <option value="success">Success</option>
            <option value="warning">Warning</option>
            <option value="failure">Failure</option>
            <option value="pending">Pending</option>
            <option value="info">Information</option>
          </select>
        </label>

        <label>
          Source
          <select value={filters.source} onChange={(event) => setFilters((current) => ({ ...current, source: event.target.value }))}>
            <option value="all">All sources</option>
            {availableSources.map((source) => (
              <option value={source.source} key={source.source}>
                {source.label} ({source.eventCount})
              </option>
            ))}
          </select>
        </label>

        <label className="audit-search-field">
          Search history
          <input
            type="search"
            value={filters.search}
            placeholder="Person, event, target, source, correlation…"
            onChange={(event) => setFilters((current) => ({ ...current, search: event.target.value }))}
          />
        </label>

        <button type="submit" className="primary-action">Search</button>
      </form>

      {auditData.error ? <div className="audit-empty-state error">{auditData.error}</div> : null}
      {auditData.loading ? <div className="audit-empty-state">Loading unified audit history…</div> : null}
      {!auditData.loading && !auditData.error && events.length === 0 ? (
        <div className="audit-empty-state">No retained events match the selected filters.</div>
      ) : null}

      {events.length > 0 ? (
        <div className="audit-event-list">
          {events.map((event) => (
            <details className={`audit-event-card ${event.status}`} key={event.eventId}>
              <summary>
                <span className={`audit-status ${event.status}`}>{statusLabel(event.status)}</span>
                <span className="audit-event-summary-copy">
                  <strong>{event.eventType}</strong>
                  <small>{event.summary || 'No summary was recorded.'}</small>
                </span>
                <span className="audit-event-context">
                  <strong>{formatDateTime(event.eventTime)}</strong>
                  <small>{humanize(event.category)} · {event.source}</small>
                </span>
                <span className="audit-event-chevron" aria-hidden="true">⌄</span>
              </summary>

              <div className="audit-event-detail">
                <div className="audit-event-facts">
                  <div><span>Actor</span><strong>{event.actor || 'System / not recorded'}</strong></div>
                  <div><span>Target</span><strong>{event.target || 'Not specified'}</strong></div>
                  <div><span>Source table</span><strong>{event.sourceTable || 'Not recorded'}</strong></div>
                  <div><span>Source record</span><strong>{event.sourceRecordId || 'Not recorded'}</strong></div>
                  <div><span>Correlation ID</span><strong>{event.correlationId || 'Not recorded'}</strong></div>
                  <div><span>Client IP</span><strong>{event.ipAddress || 'Not recorded'}</strong></div>
                  <div><span>Evidence policy</span><strong>{event.immutable ? 'Immutable / append-only' : 'Source-controlled history'}</strong></div>
                  <div><span>Category</span><strong>{humanize(event.category)}</strong></div>
                </div>

                <div className="audit-evidence-block">
                  <div>
                    <p className="eyebrow">Sanitized evidence</p>
                    <h3>Recorded details</h3>
                  </div>
                  <pre>{compactValue(event.details)}</pre>
                </div>
              </div>
            </details>
          ))}
        </div>
      ) : null}

      <div className="audit-source-footnote">
        <strong>{availableSources.length}</strong> audit/history source{availableSources.length === 1 ? '' : 's'} available.
        Sensitive fields such as passwords, tokens, secrets, credentials, and connection strings are redacted by the API.
      </div>
    </section>
  );
}

function readModule008ActiveRoute() {
  return String(window.location.hash || '')
    .replace(/^#\/?/, '')
    .split('?')[0]
    .trim();
}

function installModule008RouteRecovery() {
  if (typeof window === 'undefined' || typeof document === 'undefined') return;
  if (window.__projectPulseModule008RouteRecoveryInstalled) return;
  window.__projectPulseModule008RouteRecoveryInstalled = true;

  let host = null;
  let root = null;
  let retryTimer = 0;
  let scheduled = false;

  const removeRecovery = () => {
    if (root) root.unmount();
    if (host?.isConnected) host.remove();
    root = null;
    host = null;
  };

  const findAppOwnedPanel = (shell) => Array.from(shell.querySelectorAll('#audit-history'))
    .find((panel) => !panel.closest('[data-module-008-route-recovery-host]'));

  const synchronize = () => {
    scheduled = false;
    if (retryTimer) {
      window.clearTimeout(retryTimer);
      retryTimer = 0;
    }

    if (readModule008ActiveRoute() !== 'audit-history') {
      removeRecovery();
      return;
    }

    const shell = document.querySelector('.app-shell.route-audit-history');
    if (!shell) {
      retryTimer = window.setTimeout(synchronize, 50);
      return;
    }

    if (findAppOwnedPanel(shell)) {
      removeRecovery();
      return;
    }

    if (!host) {
      host = document.createElement('div');
      host.setAttribute('data-module-008-route-recovery-host', 'true');
      shell.appendChild(host);
      root = createRoot(host);
      root.render(<AuditHistoryPanel recoveryMode />);
      return;
    }

    if (host.parentElement !== shell) shell.appendChild(host);
  };

  const schedule = () => {
    if (scheduled) return;
    scheduled = true;
    window.requestAnimationFrame(synchronize);
  };

  const observer = new MutationObserver(schedule);
  const begin = () => {
    observer.observe(document.body, { childList: true, subtree: true });
    schedule();
    window.setTimeout(schedule, 100);
    window.setTimeout(schedule, 500);
  };

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', begin, { once: true });
  } else {
    begin();
  }

  window.addEventListener('hashchange', schedule);
  window.addEventListener('projectpulse:auth-session-ready', schedule);
}

installModule008RouteRecovery();
