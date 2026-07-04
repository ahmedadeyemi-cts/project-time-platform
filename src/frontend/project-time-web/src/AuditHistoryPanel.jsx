import { useEffect, useMemo, useState } from 'react';
import './audit-history.css';

function getProjectPulseAuthHeaders() {
  try {
    const raw = window.localStorage.getItem('projectPulseAuthSession');
    if (!raw) return {};

    const session = JSON.parse(raw);
    if (!session?.sessionToken) return {};

    return {
      'X-Project Health Dashboard-Session': session.sessionToken
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
    return `${path} returned HTTP ${response.status}: ${parsed.message || parsed.detail || parsed.status || raw}`;
  } catch {
    return `${path} returned HTTP ${response.status}: ${raw}`;
  }
}

async function fetchJson(path) {
  const response = await fetch(path, {
    headers: getProjectPulseAuthHeaders()
  });

  if (!response.ok) {
    throw new Error(await readApiErrorMessage(response, path));
  }

  return response.json();
}

function formatDateTime(value) {
  if (!value) return 'Not available';

  try {
    return new Date(value).toLocaleString();
  } catch {
    return String(value);
  }
}

function statusLabel(status) {
  if (status === 'success') return 'Success';
  if (status === 'failure') return 'Failure';
  if (status === 'warning') return 'Warning';
  if (status === 'pending') return 'Pending';
  return status || 'Unknown';
}

function categoryLabel(category) {
  if (category === 'authentication') return 'Authentication';
  if (category === 'password_reset') return 'Password Reset';
  if (category === 'azure_sync') return 'Azure / Entra Sync';
  if (category === 'notification') return 'Notification';
  if (category === 'system_audit') return 'System Audit';
  return category || 'Unknown';
}

export default function AuditHistoryPanel() {
  const [days, setDays] = useState('14');
  const [category, setCategory] = useState('all');
  const [status, setStatus] = useState('all');
  const [search, setSearch] = useState('');
  const [querySearch, setQuerySearch] = useState('');
  const [auditData, setAuditData] = useState({ loading: true, data: null, error: null });

  async function loadAuditHistory() {
    setAuditData((current) => ({ ...current, loading: true, error: null }));

    const params = new URLSearchParams({
      days,
      category,
      status,
      search: querySearch
    });

    try {
      const result = await fetchJson(`/api/audit/history?${params.toString()}`);
      setAuditData({ loading: false, data: result, error: null });
    } catch (error) {
      setAuditData({
        loading: false,
        data: null,
        error: error instanceof Error ? error.message : 'Unable to load audit history.'
      });
    }
  }

  useEffect(() => {
    void loadAuditHistory();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [days, category, status, querySearch]);

  const events = auditData.data?.events ?? [];

  const summary = useMemo(() => {
    return events.reduce(
      (accumulator, event) => {
        accumulator.total += 1;
        accumulator[event.status] = (accumulator[event.status] ?? 0) + 1;
        return accumulator;
      },
      { total: 0, success: 0, failure: 0, warning: 0, pending: 0 }
    );
  }, [events]);

  function applySearch(event) {
    event.preventDefault();
    setQuerySearch(search.trim());
  }

  return (
    <section id="audit-history" className="panel audit-history-panel">
      <div className="section-header compact">
        <div>
          <p className="eyebrow">Security & Audit</p>
          <h2>Audit and failure history</h2>
          <p className="muted">
            Review successful logins, failed login attempts, password reset workflow history, Azure sync events,
            notification failures, and system audit events.
          </p>
        </div>
        <button type="button" className="secondary-action" onClick={loadAuditHistory} disabled={auditData.loading}>
          Refresh
        </button>
      </div>

      <div className="audit-summary-grid">
        <article>
          <span>Total events</span>
          <strong>{summary.total}</strong>
        </article>
        <article>
          <span>Success</span>
          <strong>{summary.success}</strong>
        </article>
        <article>
          <span>Failures</span>
          <strong>{summary.failure}</strong>
        </article>
        <article>
          <span>Warnings</span>
          <strong>{summary.warning}</strong>
        </article>
        <article>
          <span>Pending</span>
          <strong>{summary.pending}</strong>
        </article>
      </div>

      <form className="audit-filter-bar" onSubmit={applySearch}>
        <label>
          Lookback
          <select value={days} onChange={(event) => setDays(event.target.value)}>
            <option value="1">Last 24 hours</option>
            <option value="7">Last 7 days</option>
            <option value="14">Last 14 days</option>
            <option value="30">Last 30 days</option>
            <option value="90">Last 90 days</option>
            <option value="365">Last year</option>
          </select>
        </label>

        <label>
          Category
          <select value={category} onChange={(event) => setCategory(event.target.value)}>
            <option value="all">All categories</option>
            <option value="authentication">Authentication</option>
            <option value="password_reset">Password Reset</option>
            <option value="azure_sync">Azure / Entra Sync</option>
            <option value="notification">Notification</option>
            <option value="system_audit">System Audit</option>
          </select>
        </label>

        <label>
          Status
          <select value={status} onChange={(event) => setStatus(event.target.value)}>
            <option value="all">All statuses</option>
            <option value="success">Success</option>
            <option value="failure">Failure</option>
            <option value="warning">Warning</option>
            <option value="pending">Pending</option>
          </select>
        </label>

        <label>
          Search
          <input
            type="search"
            value={search}
            placeholder="Actor, target, event, details..."
            onChange={(event) => setSearch(event.target.value)}
          />
        </label>

        <button type="submit" className="primary-action">Apply</button>
      </form>

      {auditData.error ? (
        <div className="manager-empty-state error">{auditData.error}</div>
      ) : null}

      {auditData.loading ? (
        <div className="manager-empty-state">Loading audit history...</div>
      ) : null}

      {!auditData.loading && !auditData.error && events.length === 0 ? (
        <div className="manager-empty-state">No audit events match the selected filters.</div>
      ) : null}

      {events.length > 0 ? (
        <div className="audit-table-wrap">
          <table className="audit-table">
            <thead>
              <tr>
                <th>Time</th>
                <th>Category</th>
                <th>Status</th>
                <th>Event</th>
                <th>Actor</th>
                <th>Target</th>
                <th>Source</th>
                <th>Details</th>
                <th>IP</th>
              </tr>
            </thead>
            <tbody>
              {events.map((event) => (
                <tr key={event.eventId}>
                  <td>{formatDateTime(event.eventTime)}</td>
                  <td>{categoryLabel(event.category)}</td>
                  <td>
                    <span className={`audit-status ${event.status}`}>
                      {statusLabel(event.status)}
                    </span>
                  </td>
                  <td><strong>{event.eventType}</strong></td>
                  <td>{event.actor}</td>
                  <td>{event.target}</td>
                  <td>{event.source}</td>
                  <td className="audit-details">{event.details || 'No details recorded.'}</td>
                  <td>{event.ipAddress || '—'}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : null}
    </section>
  );
}
