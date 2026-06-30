import { useEffect, useMemo, useState } from 'react';
import './production-readiness-center.css';
import ProductionReadinessBrowserValidationPanel from './ProductionReadinessBrowserValidationPanel.jsx';
import ProductionReadinessPurposeMapPanel from './ProductionReadinessPurposeMapPanel.jsx';
import ProductionReadinessReleaseCloseoutPanel from './ProductionReadinessReleaseCloseoutPanel.jsx';

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

function formatValue(value) {
  if (value === null || value === undefined) return 'Not available';
  if (typeof value === 'number') return value.toLocaleString();
  if (typeof value === 'boolean') return value ? 'Yes' : 'No';
  return String(value);
}

function normalizeStatus(status) {
  const value = String(status || '').toLowerCase();
  if (value.includes('ready')) return 'ready';
  if (value.includes('optional')) return 'optional';
  if (value.includes('review')) return 'review';
  if (value.includes('denied') || value.includes('blocked')) return 'blocked';
  return value || 'unknown';
}

export default function ProductionReadinessCenterPanel() {
  const [state, setState] = useState({
    loading: true,
    data: null,
    error: null,
    statusCode: null,
    loadedAt: null
  });

  const checks = Array.isArray(state.data?.checks) ? state.data.checks : [];
  const summary = state.data?.summary || {};

  const readinessCounts = useMemo(() => {
    return checks.reduce(
      (accumulator, check) => {
        const status = normalizeStatus(check.status);
        accumulator.total += 1;
        accumulator[status] = (accumulator[status] || 0) + 1;
        return accumulator;
      },
      { total: 0, ready: 0, optional: 0, review: 0, blocked: 0, unknown: 0 }
    );
  }, [checks]);

  async function loadReadiness() {
    const sessionToken = readProjectPulseSessionToken();

    setState((current) => ({
      ...current,
      loading: true,
      error: null
    }));

    try {
      const response = await fetch('/api/production/readiness-command-center', {
        headers: sessionToken ? { 'X-ProjectPulse-Session': sessionToken } : {}
      });

      const text = await response.text();
      const payload = text ? JSON.parse(text) : null;

      if (!response.ok) {
        setState({
          loading: false,
          data: payload,
          error: payload?.message || `Readiness endpoint returned HTTP ${response.status}`,
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
        error: error instanceof Error ? error.message : 'Unable to load production readiness status',
        statusCode: null,
        loadedAt: new Date().toISOString()
      });
    }
  }

  useEffect(() => {
    loadReadiness();
  }, []);

  return (
    <section className="production-readiness-center">
      <div className="production-readiness-hero">
        <div>
          <p className="eyebrow">Production Readiness</p>
          <h1>Production Readiness Center</h1>
          <p>
            Use this page to confirm the application has the core users, projects, time entries,
            audit evidence, export signals, and route governance needed before production release.
          </p>
        </div>

        <div className="production-readiness-actions">
          <button type="button" className="primary-action" onClick={loadReadiness} disabled={state.loading}>
            {state.loading ? 'Refreshing...' : 'Refresh readiness'}
          </button>
          <a className="secondary-action readiness-link-button" href="#workflow">
            Open workflow center
          </a>
        </div>
      </div>

      {state.error ? (
        <div className="production-readiness-alert">
          <strong>Readiness status unavailable</strong>
          <span>{state.error}</span>
          <small>
            HTTP status: {formatValue(state.statusCode)}. Confirm you are signed in with an Administrator,
            system administration, or approved workflow/reporting role.
          </small>
        </div>
      ) : null}

      <div className="production-readiness-grid">
        <article className="production-readiness-card">
          <span>Endpoint status</span>
          <strong>{state.loading ? 'Loading' : formatValue(state.statusCode)}</strong>
          <small>{state.loadedAt ? `Last checked ${new Date(state.loadedAt).toLocaleString()}` : 'Not checked yet'}</small>
        </article>

        <article className="production-readiness-card">
          <span>Ready checks</span>
          <strong>{formatValue(summary.readyCheckCount ?? readinessCounts.ready)}</strong>
          <small>{formatValue(summary.checkCount ?? readinessCounts.total)} total checks</small>
        </article>

        <article className="production-readiness-card">
          <span>Production ready</span>
          <strong>{formatValue(summary.productionReady ?? summary.ready ?? (readinessCounts.ready > 0 && readinessCounts.ready === readinessCounts.total))}</strong>
          <small>Based on backend readiness summary</small>
        </article>

        <article className="production-readiness-card">
          <span>Needs review</span>
          <strong>{formatValue((readinessCounts.review || 0) + (readinessCounts.blocked || 0) + (readinessCounts.unknown || 0))}</strong>
          <small>Review before release candidate closeout</small>
        </article>
      </div>

      <div className="production-readiness-content-grid">
        <section className="production-readiness-panel">
          <div className="production-readiness-panel-heading">
            <div>
              <p className="eyebrow">Backend checks</p>
              <h2>Readiness check results</h2>
            </div>
          </div>

          {state.loading ? (
            <div className="manager-empty-state">Loading production readiness status...</div>
          ) : checks.length === 0 ? (
            <div className="manager-empty-state">
              No readiness checks were returned. Confirm the backend endpoint is available and the current role has access.
            </div>
          ) : (
            <div className="production-readiness-table">
              <table>
                <thead>
                  <tr>
                    <th>Check</th>
                    <th>Value</th>
                    <th>Status</th>
                  </tr>
                </thead>
                <tbody>
                  {checks.map((check, index) => (
                    <tr key={`${check.check || 'check'}-${index}`}>
                      <td>{check.check || check.name || `Check ${index + 1}`}</td>
                      <td>{formatValue(check.value ?? check.count ?? check.result)}</td>
                      <td>
                        <span className={`readiness-status-pill status-${normalizeStatus(check.status)}`}>
                          {formatValue(check.status)}
                        </span>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </section>

        <section className="production-readiness-panel">
          <div className="production-readiness-panel-heading">
            <div>
              <p className="eyebrow">What to validate</p>
              <h2>Webpage validation checklist</h2>
            </div>
          </div>

          <div className="production-readiness-checklist">
            <a href="#dashboard">Dashboard loads and navigation works</a>
            <a href="#production-readiness">Production Readiness Center loads</a>
            <a href="#project-intake">Project Intake loads intake and handoff views</a>
            <a href="#project-workspace">Resource/project workspace views load</a>
            <a href="#workflow">Approval / Export / Audit workflows load</a>
            <a href="#manager-approval">Manager approvals load by role</a>
            <a href="#role-admin">Role / Security Administration remains controlled</a>
            <a href="#audit-history">Audit History loads evidence and filters</a>
          </div>
        </section>
      </div>


      <ProductionReadinessBrowserValidationPanel />

      <ProductionReadinessPurposeMapPanel />

      <ProductionReadinessReleaseCloseoutPanel />





      <section className="production-readiness-panel production-readiness-purpose">
        <p className="eyebrow">Backend purpose</p>
        <h2>What this page proves</h2>
        <p>
          This page connects the visible application to the backend readiness process. It confirms that
          production-readiness checks are protected by role, the readiness endpoint is reachable,
          and the system has enough operational evidence to support final release-candidate validation.
        </p>
      </section>
    </section>
  );
}
