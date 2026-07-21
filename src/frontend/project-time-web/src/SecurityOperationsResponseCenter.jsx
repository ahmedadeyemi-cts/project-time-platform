import { useCallback, useEffect, useMemo, useState } from 'react';
import { usSignalLogoDataUrl } from './assets/usSignalLogoData.js';
import './security-operations-response-center.css';

function sessionToken(authSession) {
  return authSession?.sessionToken ?? authSession?.token ?? authSession?.accessToken
    ?? window.localStorage.getItem('projectPulseSessionToken')
    ?? window.sessionStorage.getItem('projectPulseSessionToken') ?? '';
}

function headers(authSession, json = false) {
  const token = sessionToken(authSession);
  return {
    ...(token ? { Authorization: `Bearer ${token}`, 'X-ProjectPulse-Session': token, 'X-Project-Pulse-Session': token, 'X-Session-Token': token } : {}),
    ...(json ? { 'Content-Type': 'application/json' } : {})
  };
}

async function api(path, authSession, options = {}) {
  const response = await fetch(path, {
    method: options.method ?? 'GET', credentials: 'include',
    headers: headers(authSession, options.body !== undefined),
    ...(options.body !== undefined ? { body: JSON.stringify(options.body) } : {})
  });
  const payload = await response.json().catch(() => null);
  if (!response.ok) {
    const error = new Error(payload?.message ?? `${path} returned HTTP ${response.status}.`);
    error.payload = payload;
    throw error;
  }
  return payload;
}

const SURFACES = {
  overview: '/api/security-operations/overview',
  alerts: '/api/security-operations/alerts',
  sessions: '/api/security-operations/sessions',
  incidents: '/api/security-operations/incidents',
  response: '/api/security-operations/response-policy',
  intelligence: '/api/security-operations/threat-intelligence',
  controls: '/api/security-operations/control-posture'
};

function titleCase(value) {
  return String(value ?? 'unknown').replaceAll('_', ' ').replace(/\b\w/g, (letter) => letter.toUpperCase());
}

function when(value) {
  const date = value ? new Date(value) : null;
  return date && !Number.isNaN(date.getTime()) ? date.toLocaleString() : 'Not recorded';
}

function tone(value) {
  const normalized = String(value ?? '').toLowerCase();
  if (['critical', 'high', 'failed'].includes(normalized)) return 'danger';
  if (['medium', 'warning', 'containment_pending', 'awaiting_approval'].includes(normalized)) return 'warning';
  if (['contained', 'resolved', 'closed', 'executed', 'healthy', 'connected'].includes(normalized)) return 'success';
  return 'info';
}

const EMPTY_DECLARE = { title: '', description: '', severity: 'medium', note: '' };
const EMPTY_CONTAIN = { incidentId: '', actionCode: 'revoke_session', targetReference: '', reason: '' };

export default function SecurityOperationsResponseCenter({ authSession }) {
  const [state, setState] = useState({ loading: true, data: {}, errors: {}, action: '', result: null });
  const [declareForm, setDeclareForm] = useState(EMPTY_DECLARE);
  const [containForm, setContainForm] = useState(EMPTY_CONTAIN);

  const load = useCallback(async () => {
    setState((current) => ({ ...current, loading: true, errors: {} }));
    const entries = Object.entries(SURFACES);
    const results = await Promise.allSettled(entries.map(([, path]) => api(path, authSession)));
    const data = {}; const errors = {};
    results.forEach((result, index) => {
      const key = entries[index][0];
      if (result.status === 'fulfilled') data[key] = result.value;
      else errors[key] = result.reason?.message ?? `Unable to load ${key}.`;
    });
    setState((current) => ({ ...current, loading: false, data, errors }));
  }, [authSession]);

  useEffect(() => { void load(); }, [load]);

  const run = useCallback(async (label, path, body) => {
    setState((current) => ({ ...current, action: label, result: null }));
    try {
      const result = await api(path, authSession, { method: 'POST', body });
      setState((current) => ({ ...current, action: '', result: { ok: true, message: titleCase(result.status), detail: result } }));
      await load();
      return result;
    } catch (error) {
      setState((current) => ({ ...current, action: '', result: { ok: false, message: error.message, detail: error.payload } }));
      return null;
    }
  }, [authSession, load]);

  const incidents = state.data.incidents?.activeIncidents ?? [];
  const requests = state.data.incidents?.responseRequests ?? [];
  const sessions = state.data.sessions?.sessions ?? [];
  const metrics = state.data.overview?.metrics ?? {};
  const canManage = state.data.overview?.access?.canManage === true && !state.data.overview?.access?.isViewAs;

  const activeSessions = useMemo(() => sessions.filter((session) => session.active), [sessions]);

  async function declareIncident(event) {
    event.preventDefault();
    const result = await run('Declaring incident', '/api/security-operations/incidents/declare', declareForm);
    if (result) setDeclareForm(EMPTY_DECLARE);
  }

  async function prepareContainment(event) {
    event.preventDefault();
    const result = await run('Preparing containment', '/api/security-operations/response/contain', {
      ...containForm,
      incidentId: containForm.incidentId || undefined
    });
    if (result) setContainForm(EMPTY_CONTAIN);
  }

  async function startDiagnostics(incident) {
    await run('Running diagnostics', '/api/system-diagnostics/sessions', {
      incidentId: incident.incidentId,
      targetKind: 'incident',
      targetReference: `INC-${incident.incidentNumber}: ${incident.title}`,
      note: 'Diagnostic session started from Module 997.'
    });
  }

  return (
    <section className="security-operations-center" data-module="997" data-contract-version={state.data.overview?.contractVersion ?? '2026-07-21.2'} data-execution-mode="governed-native">
      <header className="security-operations-hero">
        <div className="security-operations-brand">
          <img src={usSignalLogoDataUrl} alt="US Signal" />
          <div>
            <p className="security-operations-eyebrow">ProjectPulse · Module 997</p>
            <h1>Security Operations, Threat Intelligence &amp; Response Center</h1>
            <p>Monitor ProjectPulse authentication and audit signals, declare incidents, preserve timelines, request dual-controlled containment, and launch evidence-backed diagnostics.</p>
          </div>
        </div>
        <div className="security-operations-hero-actions">
          <span className="security-operations-live">Native security operations</span>
          <button type="button" onClick={load} disabled={state.loading}>{state.loading ? 'Refreshing…' : 'Refresh security posture'}</button>
        </div>
      </header>

      <div className="security-operations-boundary" role="status">
        <strong>What works now:</strong>
        <span>ProjectPulse telemetry, incidents, timelines, diagnostic handoff, approvals, audit evidence, and optionally native session revocation. External WAF, Entra, endpoint, and network actions still require approved adapters.</span>
      </div>

      {Object.keys(state.errors).length ? (
        <div className="security-operations-error" role="alert"><strong>Some security surfaces are unavailable.</strong><span>{Object.entries(state.errors).map(([key, value]) => `${titleCase(key)}: ${value}`).join(' · ')}</span></div>
      ) : null}
      {state.result ? <div className={`security-operations-result ${state.result.ok ? 'is-success' : 'is-failure'}`} role="status"><strong>{state.result.ok ? 'Completed' : 'Action stopped'}</strong><span>{state.result.message}</span></div> : null}
      {state.data.overview?.access?.isViewAs ? <div className="security-operations-view-as">View-As is active. Security writes remain blocked and authority stays with the actual session.</div> : null}

      <section className="security-operations-metrics" aria-label="Security summary">
        <article><span>Active incidents</span><strong>{metrics.activeIncidents ?? 0}</strong><small>{metrics.highCriticalIncidents ?? 0} high/critical</small></article>
        <article><span>Failed logins (24h)</span><strong>{metrics.failedLogins24h ?? 0}</strong><small>ProjectPulse authentication</small></article>
        <article><span>Containment approvals</span><strong>{metrics.containmentAwaitingApproval ?? 0}</strong><small>Separation of duties</small></article>
        <article><span>Active sessions</span><strong>{metrics.activeSessions ?? 0}</strong><small>Revocation switch: {state.data.overview?.posture?.nativeSessionRevocationEnabled ? 'enabled' : 'disabled'}</small></article>
      </section>

      <section className="security-operations-grid">
        <article className="security-operations-panel">
          <div className="security-operations-heading"><div><p className="security-operations-eyebrow">Live signal queue</p><h2>Authentication anomalies</h2></div><span>{state.data.alerts?.queue?.total ?? 0} findings</span></div>
          <div className="security-operations-queue">
            {(state.data.alerts?.authenticationSignals ?? []).map((signal) => (
              <article key={signal.signalId}>
                <div><span className={`ops-badge is-${tone(signal.severity)}`}>{titleCase(signal.severity)}</span><strong>{signal.count} authentication failures</strong></div>
                <p>{signal.username} · {signal.sourceIp}</p><small>{when(signal.firstSeenAt)} to {when(signal.lastSeenAt)}</small><small>{signal.recommendedAction}</small>
              </article>
            ))}
            {!(state.data.alerts?.authenticationSignals?.length) ? <p className="security-operations-empty">No repeated-failure signal crossed the current threshold. This does not assert that every external system is healthy.</p> : null}
          </div>
        </article>

        <article className="security-operations-panel">
          <div className="security-operations-heading"><div><p className="security-operations-eyebrow">Analyst action</p><h2>Declare an incident</h2></div><span>Durable case</span></div>
          <form className="security-operations-form" onSubmit={declareIncident}>
            <label>Title<input value={declareForm.title} maxLength={250} onChange={(event) => setDeclareForm((form) => ({ ...form, title: event.target.value }))} required /></label>
            <label>Severity<select value={declareForm.severity} onChange={(event) => setDeclareForm((form) => ({ ...form, severity: event.target.value }))}><option value="low">Low</option><option value="medium">Medium</option><option value="high">High</option><option value="critical">Critical</option></select></label>
            <label className="is-wide">Description<textarea value={declareForm.description} maxLength={4000} onChange={(event) => setDeclareForm((form) => ({ ...form, description: event.target.value }))} required /></label>
            <label className="is-wide">Initial evidence note<input value={declareForm.note} maxLength={2000} onChange={(event) => setDeclareForm((form) => ({ ...form, note: event.target.value }))} /></label>
            <button type="submit" disabled={!canManage || Boolean(state.action)}>{state.action === 'Declaring incident' ? 'Declaring…' : 'Declare incident'}</button>
          </form>
        </article>
      </section>

      <section className="security-operations-panel">
        <div className="security-operations-heading"><div><p className="security-operations-eyebrow">Incident command</p><h2>Active incident cases</h2></div><span>{incidents.length} cases</span></div>
        <div className="security-operations-table-wrap">
          <table className="security-operations-table"><thead><tr><th>Incident</th><th>Severity</th><th>Status</th><th>Owner</th><th>Evidence</th><th>Actions</th></tr></thead>
            <tbody>{incidents.map((incident) => (
              <tr key={incident.incidentId}>
                <td><strong>INC-{incident.incidentNumber}</strong><span>{incident.title}</span><small>{when(incident.updatedAt)}</small></td>
                <td><span className={`ops-badge is-${tone(incident.severity)}`}>{titleCase(incident.severity)}</span></td>
                <td><span className={`ops-badge is-${tone(incident.status)}`}>{titleCase(incident.status)}</span></td>
                <td>{incident.owner}</td><td>{incident.eventCount} timeline events</td>
                <td><div className="security-operations-row-actions">
                  <button type="button" disabled={!canManage || incident.status === 'closed' || Boolean(state.action)} onClick={() => run('Acknowledging incident', '/api/security-operations/incidents/acknowledge', { incidentId: incident.incidentId, note: 'Acknowledged from the Security Operations Center.' })}>Acknowledge</button>
                  <button type="button" disabled={!canManage || incident.status === 'closed' || Boolean(state.action)} onClick={() => startDiagnostics(incident)}>Run diagnostics</button>
                  <button type="button" disabled={!canManage || incident.status === 'closed'} onClick={() => setContainForm((form) => ({ ...form, incidentId: incident.incidentId }))}>Contain</button>
                </div></td>
              </tr>
            ))}{!incidents.length ? <tr><td colSpan="6">No incident has been declared. Use the form above when a signal or report requires investigation.</td></tr> : null}</tbody>
          </table>
        </div>
      </section>

      <section className="security-operations-grid">
        <article className="security-operations-panel">
          <div className="security-operations-heading"><div><p className="security-operations-eyebrow">Containment</p><h2>Prepare a controlled response</h2></div><span>Approval required</span></div>
          <form className="security-operations-form" onSubmit={prepareContainment}>
            <label>Incident<select value={containForm.incidentId} onChange={(event) => setContainForm((form) => ({ ...form, incidentId: event.target.value }))} required><option value="">Select incident</option>{incidents.filter((item) => item.status !== 'closed').map((item) => <option key={item.incidentId} value={item.incidentId}>INC-{item.incidentNumber} · {item.title}</option>)}</select></label>
            <label>Action<select value={containForm.actionCode} onChange={(event) => setContainForm((form) => ({ ...form, actionCode: event.target.value }))}><option value="revoke_session">Revoke ProjectPulse session</option><option value="suspend_user">Suspend user (adapter)</option><option value="restrict_role">Restrict role (adapter)</option><option value="quarantine_integration">Quarantine integration (adapter)</option><option value="block_indicator">Block indicator (adapter)</option></select></label>
            <label className="is-wide">Target reference<input value={containForm.targetReference} onChange={(event) => setContainForm((form) => ({ ...form, targetReference: event.target.value }))} placeholder="Select/copy a session ID below or enter the governed target" required /></label>
            <label className="is-wide">Reason<textarea value={containForm.reason} onChange={(event) => setContainForm((form) => ({ ...form, reason: event.target.value }))} required /></label>
            <button type="submit" disabled={!canManage || Boolean(state.action)}>Prepare containment</button>
          </form>
          <details className="security-operations-sessions"><summary>Recent ProjectPulse sessions ({activeSessions.length} active)</summary>{sessions.map((session) => <button key={session.sessionId} type="button" className="security-operations-session" onClick={() => setContainForm((form) => ({ ...form, actionCode: 'revoke_session', targetReference: session.sessionId }))}><strong>{session.user}</strong><span>{session.sessionId}</span><small>{session.sourceIp || 'IP unavailable'} · last seen {when(session.lastSeenAt)} · {session.active ? 'active' : 'inactive'}</small></button>)}</details>
        </article>

        <article className="security-operations-panel">
          <div className="security-operations-heading"><div><p className="security-operations-eyebrow">Approval queue</p><h2>Containment requests</h2></div><span>{requests.length} requests</span></div>
          <div className="security-operations-queue">
            {requests.map((request) => <article key={request.responseRequestId}>
              <div><span className={`ops-badge is-${tone(request.state)}`}>{titleCase(request.state)}</span><strong>{titleCase(request.action)}</strong></div>
              <p>{request.reason}</p><small>Target: {request.targetReference}</small>
              <div className="security-operations-row-actions">
                {request.state === 'awaiting_approval' ? <button type="button" disabled={!canManage || Boolean(state.action)} onClick={() => run('Approving containment', '/api/security-operations/response/approve', { responseRequestId: request.responseRequestId, note: 'Approved after reviewing scope and evidence.' })}>Approve as separate actor</button> : null}
                {request.state === 'approved' ? <button type="button" disabled={!canManage || Boolean(state.action)} onClick={() => run('Executing containment', '/api/security-operations/response/execute', { responseRequestId: request.responseRequestId, note: 'Executed approved containment.' })}>Execute approved action</button> : null}
              </div>
            </article>)}
            {!requests.length ? <p className="security-operations-empty">No containment request is waiting. Preparing a request never executes it.</p> : null}
          </div>
        </article>
      </section>

      <section className="security-operations-panel">
        <div className="security-operations-heading"><div><p className="security-operations-eyebrow">External controls</p><h2>Connector-bound actions</h2></div><span className="security-operations-locked">Adapters not configured</span></div>
        <p>These controls remain visible so an analyst knows what is missing. They do not pretend to work.</p>
        <div className="security-operations-locked-actions"><button type="button" disabled>Suspend Entra user</button><button type="button" disabled>Block WAF indicator</button><button type="button" disabled>Isolate endpoint</button><button type="button" disabled>Send external notification</button><button type="button" disabled>Export evidence package</button><button type="button" disabled>Run AI analysis</button></div>
      </section>
    </section>
  );
}
