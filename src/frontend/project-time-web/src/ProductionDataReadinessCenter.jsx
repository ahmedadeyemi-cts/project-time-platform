import { useEffect, useMemo, useState } from 'react';
import './production-data-readiness-center.css';

function readProjectPulseSessionToken() {
  try {
    const raw = window.localStorage.getItem('projectPulseAuthSession');
    if (!raw) return '';
    const parsed = JSON.parse(raw);
    return parsed?.sessionToken || '';
  } catch {
    return '';
  }
}

function normalizeStatus(status) {
  const value = String(status || '').toLowerCase();
  if (value.includes('ready')) return 'ready';
  if (value.includes('missing')) return 'missing';
  if (value.includes('need')) return 'review';
  return value || 'unknown';
}

function formatValue(value) {
  if (value === null || value === undefined) return 'Not available';
  if (typeof value === 'number') return value.toLocaleString();
  if (typeof value === 'boolean') return value ? 'Yes' : 'No';
  return String(value);
}

export default function ProductionDataReadinessCenter() {
  const [state, setState] = useState({
    loading: true,
    data: null,
    error: null,
    statusCode: null,
    loadedAt: null
  });

  const checks = Array.isArray(state.data?.checks) ? state.data.checks : [];
  const summary = state.data?.summary || {};

  const derivedSummary = useMemo(() => {
    return checks.reduce(
      (accumulator, check) => {
        const status = normalizeStatus(check.status);
        accumulator.total += 1;
        accumulator[status] = (accumulator[status] || 0) + 1;
        return accumulator;
      },
      { total: 0, ready: 0, review: 0, missing: 0, unknown: 0 }
    );
  }, [checks]);

  async function loadDataReadiness() {
    const sessionToken = readProjectPulseSessionToken();

    setState((current) => ({
      ...current,
      loading: true,
      error: null
    }));

    try {
      const response = await fetch('/api/production/data-readiness', {
        headers: sessionToken ? { 'X-ProjectPulse-Session': sessionToken } : {}
      });

      const text = await response.text();
      const payload = text ? JSON.parse(text) : null;

      if (!response.ok) {
        setState({
          loading: false,
          data: payload,
          error: payload?.message || payload?.detail || `Data readiness endpoint returned HTTP ${response.status}`,
          statusCode: response.status,
          loadedAt: new Date().toISOString()
        });
        return;
      }

      setState({
        loading: false,
        data: payload,
        error: null,
        statusCode: response.status,
        loadedAt: new Date().toISOString()
      });
    } catch (error) {
      setState({
        loading: false,
        data: null,
        error: error instanceof Error ? error.message : 'Unable to load production data readiness',
        statusCode: null,
        loadedAt: new Date().toISOString()
      });
    }
  }

  useEffect(() => {
    loadDataReadiness();
  }, []);

  return (
    <section className="production-data-readiness-center">
      <div className="production-data-hero">
        <div>
          <p className="eyebrow">Production Data Readiness</p>
          <h1>Production Data Readiness Center</h1>
          <p>
            Use this page to confirm the system has the real operational data needed before go-live:
            users, roles, customers, projects, tasks, time, approvals, exports, audit evidence, and notification evidence.
          </p>
        </div>

        <div className="production-data-actions">
          <button type="button" className="primary-action" onClick={loadDataReadiness} disabled={state.loading}>
            {state.loading ? 'Refreshing...' : 'Refresh data readiness'}
          </button>
          <a className="secondary-action data-readiness-link-button" href="#production-readiness">
            Open production readiness
          </a>
        </div>
      </div>

      {state.error ? (
        <div className="production-data-alert">
          <strong>Data readiness status unavailable</strong>
          <span>{state.error}</span>
          <small>
            HTTP status: {formatValue(state.statusCode)}. Confirm you are signed in with an authorized role.
          </small>
        </div>
      ) : null}

      <div className="production-data-grid">
        <article className="production-data-card">
          <span>Endpoint status</span>
          <strong>{state.loading ? 'Loading' : formatValue(state.statusCode)}</strong>
          <small>{state.loadedAt ? `Last checked ${new Date(state.loadedAt).toLocaleString()}` : 'Not checked yet'}</small>
        </article>

        <article className="production-data-card">
          <span>Ready checks</span>
          <strong>{formatValue(summary.readyCount ?? derivedSummary.ready)}</strong>
          <small>{formatValue(summary.checkCount ?? derivedSummary.total)} total checks</small>
        </article>

        <article className="production-data-card">
          <span>Needs data</span>
          <strong>{formatValue(summary.needsDataCount ?? derivedSummary.review)}</strong>
          <small>Rows below explain what is missing</small>
        </article>

        <article className="production-data-card">
          <span>Missing tables</span>
          <strong>{formatValue(summary.missingTableCount ?? derivedSummary.missing)}</strong>
          <small>Should be 0 before go-live</small>
        </article>
      </div>

      <section className="production-data-panel">
        <div className="production-data-panel-heading">
          <div>
            <p className="eyebrow">Data checks</p>
            <h2>Operational data readiness</h2>
            <p>
              Each row shows the backend table/process, current count, readiness status, why it matters,
              and what you should verify on the webpage.
            </p>
          </div>
        </div>

        {state.loading ? (
          <div className="manager-empty-state">Loading production data readiness...</div>
        ) : checks.length === 0 ? (
          <div className="manager-empty-state">
            No data readiness checks were returned. Confirm the backend endpoint is available and your role has access.
          </div>
        ) : (
          <div className="production-data-table">
            <table>
              <thead>
                <tr>
                  <th>Data area</th>
                  <th>Backend table</th>
                  <th>Count</th>
                  <th>Status</th>
                  <th>Why it matters</th>
                  <th>What to check</th>
                </tr>
              </thead>
              <tbody>
                {checks.map((check) => (
                  <tr key={check.key || check.label}>
                    <td>
                      <strong>{check.label}</strong>
                    </td>
                    <td>
                      <code>{check.tableName}</code>
                    </td>
                    <td>{formatValue(check.count)}</td>
                    <td>
                      <span className={`data-readiness-status status-${normalizeStatus(check.status)}`}>
                        {formatValue(check.status)}
                      </span>
                    </td>
                    <td>{check.purpose}</td>
                    <td>{check.webpageCheck}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      <section className="production-data-panel production-data-validation">
        <p className="eyebrow">Webpage validation</p>
        <h2>What you should click next</h2>
        <div className="production-data-validation-grid">
          <a href="#user-admin">User Administration</a>
          <a href="#role-admin">Role / Security Administration</a>
          <a href="#customer-directory">Customer Directory</a>
          <a href="#project-intake">Project Intake</a>
          <a href="#project-workspace">Project Workspace</a>
          <a href="#workflow">Approval / Export / Audit Workflows</a>
          <a href="#manager-approval">Manager Approvals</a>
          <a href="#audit-history">Audit History</a>
        </div>
      </section>
    </section>
  );
}
