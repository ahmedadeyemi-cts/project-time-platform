import { useCallback, useEffect, useMemo, useState } from 'react';
import { usSignalLogoDataUrl } from './assets/usSignalLogoData.js';
import './security-operations-response-center.css';

function sessionToken(authSession) {
  return authSession?.sessionToken
    ?? authSession?.token
    ?? authSession?.accessToken
    ?? window.localStorage.getItem('projectPulseSessionToken')
    ?? window.sessionStorage.getItem('projectPulseSessionToken')
    ?? '';
}

function requestHeaders(authSession) {
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
    credentials: 'include',
    headers: requestHeaders(authSession)
  });
  const payload = await response.json().catch(() => null);
  if (!response.ok) {
    throw new Error(payload?.message ?? `Module 997 returned HTTP ${response.status}.`);
  }
  return payload;
}

function titleCase(value) {
  return String(value ?? 'unknown')
    .replaceAll('_', ' ')
    .replace(/\b\w/g, (letter) => letter.toUpperCase());
}

function observedTime(value) {
  if (!value) return 'Not observed';
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? 'Not observed' : parsed.toLocaleString();
}

function statusTone(value) {
  const status = String(value ?? '').toLowerCase();
  if (status.includes('governed') || status === 'delegated') return 'governed';
  if (status.includes('locked') || status.includes('not_configured')) return 'locked';
  if (status === 'guidance_only' || status === 'contract_only') return 'guidance';
  return 'unknown';
}

const INITIAL_STATE = {
  loading: true,
  overview: null,
  alerts: null,
  incidents: null,
  intelligence: null,
  controls: null,
  response: null,
  reporting: null,
  integration: null,
  error: ''
};

export default function SecurityOperationsResponseCenter({ authSession }) {
  const [state, setState] = useState(INITIAL_STATE);

  const load = useCallback(async () => {
    setState((current) => ({ ...current, loading: true, error: '' }));

    try {
      const [
        overview,
        alerts,
        incidents,
        intelligence,
        controls,
        response,
        reporting,
        integration
      ] = await Promise.all([
        readJson('/api/security-operations/overview', authSession),
        readJson('/api/security-operations/alerts', authSession),
        readJson('/api/security-operations/incidents', authSession),
        readJson('/api/security-operations/threat-intelligence', authSession),
        readJson('/api/security-operations/control-posture', authSession),
        readJson('/api/security-operations/response-policy', authSession),
        readJson('/api/security-operations/reporting-policy', authSession),
        readJson('/api/security-operations/integration-policy', authSession)
      ]);

      setState({
        loading: false,
        overview,
        alerts,
        incidents,
        intelligence,
        controls,
        response,
        reporting,
        integration,
        error: ''
      });
    } catch (error) {
      setState((current) => ({
        ...current,
        loading: false,
        error: error?.message ?? 'Security operations are temporarily unavailable.'
      }));
    }
  }, [authSession]);

  useEffect(() => {
    void load();
  }, [load]);

  const queue = state.alerts?.queue ?? {};
  const sourceSummary = useMemo(() => {
    const sources = state.intelligence?.intelligence?.sources ?? [];
    return {
      total: sources.length,
      connected: sources.filter((source) => source.status === 'connected').length
    };
  }, [state.intelligence]);

  return (
    <section
      className="security-operations-center"
      data-module="997"
      data-contract-version={state.overview?.contractVersion ?? '2026-07-20.1'}
      data-execution-mode="fail-closed"
    >
      <header className="security-operations-hero">
        <div className="security-operations-brand">
          <img src={usSignalLogoDataUrl} alt="US Signal" />
          <div>
            <p className="security-operations-eyebrow">ProjectPulse · Module 997</p>
            <h1>Security Operations, Threat Intelligence &amp; Response Center</h1>
            <p>
              A governed security command view for signal readiness, alert and
              incident contracts, threat intelligence, control posture, response
              policy, and reporting boundaries.
            </p>
          </div>
        </div>
        <div className="security-operations-hero-actions">
          <span>Fail-closed security source</span>
          <button type="button" onClick={load} disabled={state.loading}>
            {state.loading ? 'Refreshing…' : 'Refresh safe posture'}
          </button>
        </div>
      </header>

      <div className="security-operations-boundary" role="status">
        <strong>No live response execution</strong>
        <span>
          Telemetry and threat-feed connectors, AI, containment, eradication,
          recovery, external notifications, evidence export, and secret access
          are disabled.
        </span>
      </div>

      {state.error ? (
        <div className="security-operations-error" role="alert">
          <strong>Security source unavailable</strong>
          <span>{state.error}</span>
        </div>
      ) : null}

      {state.overview?.access?.isViewAs ? (
        <div className="security-operations-view-as">
          View-As is active. Module 997 still authorizes the actual ProjectPulse
          session and never transfers security authority.
        </div>
      ) : null}

      <section className="security-operations-metrics" aria-label="Security summary">
        <article>
          <span>Live alerts</span>
          <strong>{queue.total ?? 0}</strong>
          <small>Connector not configured</small>
        </article>
        <article>
          <span>Active incidents</span>
          <strong>{state.incidents?.activeIncidents?.length ?? 0}</strong>
          <small>Durable store not configured</small>
        </article>
        <article>
          <span>Threat sources</span>
          <strong>{sourceSummary.connected}/{sourceSummary.total}</strong>
          <small>Connected / governed inventory</small>
        </article>
        <article>
          <span>Response execution</span>
          <strong>Locked</strong>
          <small>Separate authority required</small>
        </article>
      </section>

      <section className="security-operations-grid security-operations-grid-wide">
        <article className="security-operations-panel">
          <div className="security-operations-heading">
            <div>
              <p className="security-operations-eyebrow">Operating posture</p>
              <h2>Security domains and authoritative owners</h2>
            </div>
            <span>{titleCase(state.overview?.posture?.mode)}</span>
          </div>
          <div className="security-operations-domain-grid">
            {(state.overview?.operatingDomains ?? []).map((domain) => (
              <div key={domain.id}>
                <span className={`tone-${statusTone(domain.status)}`}>
                  {titleCase(domain.status)}
                </span>
                <strong>{domain.name}</strong>
                <small>{domain.owner}</small>
              </div>
            ))}
          </div>
        </article>

        <article className="security-operations-panel">
          <div className="security-operations-heading">
            <div>
              <p className="security-operations-eyebrow">Alert queue</p>
              <h2>Sanitized signal intake contract</h2>
            </div>
            <span>{observedTime(state.alerts?.observedAt)}</span>
          </div>
          <p className="security-operations-copy">{state.alerts?.statement}</p>
          <div className="security-operations-severity-bar">
            {(state.overview?.severityModel ?? []).map((severity) => (
              <div key={severity.code} className={`severity-${severity.code}`}>
                <span>{severity.order}</span>
                <strong>{titleCase(severity.code)}</strong>
                <small>{severity.meaning}</small>
              </div>
            ))}
          </div>
        </article>
      </section>

      <section className="security-operations-panel">
        <div className="security-operations-heading">
          <div>
            <p className="security-operations-eyebrow">Incident response</p>
            <h2>Separated lifecycle — execution gates remain locked</h2>
          </div>
          <span className="security-operations-locked">Containment disabled</span>
        </div>
        <div className="security-operations-lifecycle">
          {(state.response?.lifecycle ?? []).map((step) => (
            <article key={step.code}>
              <span>{step.step}</span>
              <strong>{titleCase(step.code)}</strong>
              <p>{step.purpose}</p>
              <b className={`tone-${statusTone(step.state)}`}>{titleCase(step.state)}</b>
            </article>
          ))}
        </div>
        <div className="security-operations-locked-actions" aria-label="Locked security response controls">
          <button type="button" disabled>Declare incident</button>
          <button type="button" disabled>Acknowledge case</button>
          <button type="button" disabled>Contain threat</button>
          <button type="button" disabled>Eradicate cause</button>
          <button type="button" disabled>Recover service</button>
          <button type="button" disabled>Send notification</button>
          <button type="button" disabled>Export evidence</button>
          <button type="button" disabled>Run AI analysis</button>
          <button type="button" disabled>Close case</button>
        </div>
      </section>

      <section className="security-operations-grid">
        <article className="security-operations-panel">
          <div className="security-operations-heading">
            <div>
              <p className="security-operations-eyebrow">Threat intelligence</p>
              <h2>Approved-source readiness</h2>
            </div>
          </div>
          <div className="security-operations-source-list">
            {(state.intelligence?.intelligence?.sources ?? []).map((source) => (
              <div key={source.code}>
                <div>
                  <strong>{source.name}</strong>
                  <small>{source.code}</small>
                </div>
                <span className={`tone-${statusTone(source.status)}`}>
                  {titleCase(source.status)}
                </span>
              </div>
            ))}
          </div>
          <h3>Confidence handling</h3>
          <ul className="security-operations-confidence">
            {(state.intelligence?.intelligence?.confidenceScale ?? []).map((item) => (
              <li key={item.code}>
                <strong>{titleCase(item.code)} · {item.score}</strong>
                <span>{item.action}</span>
              </li>
            ))}
          </ul>
        </article>

        <article className="security-operations-panel">
          <div className="security-operations-heading">
            <div>
              <p className="security-operations-eyebrow">Control posture</p>
              <h2>Delegated evidence map</h2>
            </div>
          </div>
          <div className="security-operations-control-list">
            {(state.controls?.controls ?? []).map((control) => (
              <div key={control.id}>
                <div>
                  <strong>{control.framework}</strong>
                  <small>{control.owner}</small>
                </div>
                <span className={`tone-${statusTone(control.status)}`}>
                  {titleCase(control.status)}
                </span>
              </div>
            ))}
          </div>
        </article>
      </section>

      <section className="security-operations-grid">
        <article className="security-operations-panel">
          <div className="security-operations-heading">
            <div>
              <p className="security-operations-eyebrow">Reporting</p>
              <h2>Restricted evidence boundary</h2>
            </div>
          </div>
          <dl className="security-operations-policy">
            <div><dt>Classification</dt><dd>{titleCase(state.reporting?.reporting?.classification)}</dd></div>
            <div><dt>External notice</dt><dd>Disabled</dd></div>
            <div><dt>Evidence export</dt><dd>Disabled</dd></div>
            <div><dt>Secret access</dt><dd>Disabled</dd></div>
          </dl>
          <h3>Prohibited content</h3>
          <ul className="security-operations-tags">
            {(state.reporting?.reporting?.prohibitedFields ?? []).map((field) => (
              <li key={field}>{field}</li>
            ))}
          </ul>
        </article>

        <article className="security-operations-panel">
          <div className="security-operations-heading">
            <div>
              <p className="security-operations-eyebrow">Integrations</p>
              <h2>Explicit future adapters</h2>
            </div>
          </div>
          <p className="security-operations-copy">{state.integration?.statement}</p>
          <div className="security-operations-integration-list">
            {(state.integration?.connectors ?? []).map((connector) => (
              <div key={connector.code}>
                <strong>{titleCase(connector.code)}</strong>
                <span>{connector.owner}</span>
                <b>{titleCase(connector.status)}</b>
              </div>
            ))}
          </div>
        </article>
      </section>
    </section>
  );
}
