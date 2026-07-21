import { useCallback, useEffect, useMemo, useState } from 'react';
import { usSignalLogoDataUrl } from './assets/usSignalLogoData.js';
import './system-diagnostic-remediation-center.css';

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
  overview: '/api/system-diagnostics/overview', checks: '/api/system-diagnostics/checks',
  issues: '/api/system-diagnostics/issues', sessions: '/api/system-diagnostics/sessions',
  evidence: '/api/system-diagnostics/evidence-policy', remediation: '/api/system-diagnostics/remediation-policy',
  runbooks: '/api/system-diagnostics/runbooks', remediations: '/api/system-diagnostics/remediations'
};

function titleCase(value) { return String(value ?? 'unknown').replaceAll('_', ' ').replace(/\b\w/g, (letter) => letter.toUpperCase()); }
function when(value) { const date = value ? new Date(value) : null; return date && !Number.isNaN(date.getTime()) ? date.toLocaleString() : 'Not recorded'; }
function tone(value) {
  const normalized = String(value ?? '').toLowerCase();
  if (['failed', 'critical', 'high', 'attention_required'].includes(normalized)) return 'danger';
  if (['warning', 'medium', 'awaiting_approval', 'staged'].includes(normalized)) return 'warning';
  if (['healthy', 'completed', 'executed', 'verified', 'closed'].includes(normalized)) return 'success';
  return 'info';
}

const EMPTY_SESSION = { targetKind: 'platform', targetReference: 'ProjectPulse', incidentId: '', note: '' };
const EMPTY_REMEDIATION = { diagnosticSessionId: '', runbookCode: 'platform_health_refresh', actionCode: 'refresh_health_snapshot', targetReference: 'ProjectPulse', justification: '' };

export default function SystemDiagnosticRemediationCenter({ authSession }) {
  const [state, setState] = useState({ loading: true, data: {}, errors: {}, action: '', result: null, selectedSession: null });
  const [sessionForm, setSessionForm] = useState(EMPTY_SESSION);
  const [remediationForm, setRemediationForm] = useState(EMPTY_REMEDIATION);

  const load = useCallback(async () => {
    setState((current) => ({ ...current, loading: true, errors: {} }));
    const entries = Object.entries(SURFACES);
    const results = await Promise.allSettled(entries.map(([, path]) => api(path, authSession)));
    const data = {}; const errors = {};
    results.forEach((result, index) => { const key = entries[index][0]; if (result.status === 'fulfilled') data[key] = result.value; else errors[key] = result.reason?.message ?? `Unable to load ${key}.`; });
    setState((current) => ({ ...current, loading: false, data, errors }));
  }, [authSession]);

  useEffect(() => { void load(); }, [load]);

  const run = useCallback(async (label, path, body) => {
    setState((current) => ({ ...current, action: label, result: null }));
    try {
      const result = await api(path, authSession, { method: 'POST', body });
      setState((current) => ({ ...current, action: '', result: { ok: true, message: titleCase(result.status), detail: result }, selectedSession: result.sessionId ? { ...current.selectedSession, sessionId: result.sessionId } : current.selectedSession }));
      await load();
      return result;
    } catch (error) {
      const missing = error.payload?.requiredConfiguration ?? error.payload?.configurationPath;
      setState((current) => ({ ...current, action: '', result: { ok: false, message: missing ? `${error.message} ${missing}` : error.message, detail: error.payload } }));
      return null;
    }
  }, [authSession, load]);

  const sessions = state.data.sessions?.sessions ?? [];
  const remediations = state.data.remediations?.remediations ?? [];
  const checks = state.data.checks?.checks ?? [];
  const metrics = state.data.overview?.metrics ?? {};
  const canManage = state.data.overview?.access?.canManage === true && !state.data.overview?.access?.isViewAs;
  const runbooks = state.data.runbooks?.runbooks ?? [];
  const selectedRunbook = useMemo(() => runbooks.find((item) => item.id === remediationForm.runbookCode), [runbooks, remediationForm.runbookCode]);

  async function createSession(event) {
    event.preventDefault();
    const result = await run('Running diagnostic session', '/api/system-diagnostics/sessions', {
      ...sessionForm,
      incidentId: sessionForm.incidentId || null
    });
    if (result) {
      setSessionForm(EMPTY_SESSION);
      setRemediationForm((form) => ({ ...form, diagnosticSessionId: result.sessionId }));
    }
  }

  async function inspectSession(sessionId) {
    setState((current) => ({ ...current, action: 'Loading evidence' }));
    try {
      const selectedSession = await api(`/api/system-diagnostics/sessions/${sessionId}`, authSession);
      setState((current) => ({ ...current, action: '', selectedSession }));
      setRemediationForm((form) => ({ ...form, diagnosticSessionId: sessionId, targetReference: selectedSession.session?.targetReference ?? form.targetReference }));
    } catch (error) {
      setState((current) => ({ ...current, action: '', result: { ok: false, message: error.message } }));
    }
  }

  function chooseRunbook(code) {
    const runbook = runbooks.find((item) => item.id === code);
    setRemediationForm((form) => ({ ...form, runbookCode: code, actionCode: runbook?.actions?.[0] ?? '', targetReference: form.targetReference || 'ProjectPulse' }));
  }

  async function prepareRemediation(event) {
    event.preventDefault();
    const result = await run('Preparing remediation', '/api/system-diagnostics/remediation/prepare', remediationForm);
    if (result) setRemediationForm((form) => ({ ...EMPTY_REMEDIATION, diagnosticSessionId: form.diagnosticSessionId }));
  }

  return (
    <section className="system-diagnostic-center" data-module="998" data-contract-version={state.data.overview?.contractVersion ?? '2026-07-21.2'} data-execution-mode="governed-native">
      <header className="system-diagnostic-hero">
        <div className="system-diagnostic-brand"><img src={usSignalLogoDataUrl} alt="US Signal" /><div><p className="system-diagnostic-eyebrow">ProjectPulse · Module 998</p><h1>System Diagnostic &amp; Controlled Remediation Center</h1><p>Run safe checks, correlate incidents and platform evidence, rank probable issues, prepare runbooks, require separate approval, execute enabled actions, and verify recovery.</p></div></div>
        <div className="system-diagnostic-hero-actions"><span className="system-diagnostic-mode is-live">Native diagnostics active</span><button type="button" onClick={load} disabled={state.loading}>{state.loading ? 'Refreshing…' : 'Refresh operations'}</button></div>
      </header>

      <div className="system-diagnostic-boundary" role="status"><strong>What works now:</strong><span>Persistent diagnostic sessions, sanitized findings, incident correlation, runbook previews, dual approvals, native health refresh, verification, and audit evidence. Infrastructure-changing actions identify the exact adapter still required.</span></div>
      {Object.keys(state.errors).length ? <div className="system-diagnostic-error" role="alert"><strong>Some diagnostic surfaces are unavailable.</strong><span>{Object.entries(state.errors).map(([key, value]) => `${titleCase(key)}: ${value}`).join(' · ')}</span></div> : null}
      {state.result ? <div className={`system-diagnostic-result ${state.result.ok ? 'is-success' : 'is-failure'}`} role="status"><strong>{state.result.ok ? 'Completed' : 'Action stopped'}</strong><span>{state.result.message}</span></div> : null}
      {state.data.overview?.access?.isViewAs ? <div className="system-diagnostic-view-as">View-As is active. Diagnostics remain visible, but session and remediation writes are blocked.</div> : null}

      <section className="system-diagnostic-metrics" aria-label="Diagnostic summary">
        <article><span>Active sessions</span><strong>{metrics.activeSessions ?? 0}</strong><small>{metrics.attentionRequired ?? 0} need attention</small></article>
        <article><span>Failed findings</span><strong>{metrics.failedFindings ?? 0}</strong><small>Across retained sessions</small></article>
        <article><span>Awaiting approval</span><strong>{metrics.awaitingApproval ?? 0}</strong><small>Separate actor required</small></article>
        <article><span>Verified remediations</span><strong>{metrics.verifiedRemediations ?? 0}</strong><small>Before/after evidence retained</small></article>
      </section>

      <section className="system-diagnostic-grid">
        <article className="system-diagnostic-panel">
          <div className="system-diagnostic-heading"><div><p className="system-diagnostic-eyebrow">Start here</p><h2>Run a diagnostic session</h2></div><span>Safe checks</span></div>
          <form className="system-diagnostic-form" onSubmit={createSession}>
            <label>Target type<select value={sessionForm.targetKind} onChange={(event) => setSessionForm((form) => ({ ...form, targetKind: event.target.value }))}><option value="platform">Entire platform</option><option value="api">API</option><option value="web">Web</option><option value="database">Database</option><option value="identity">Identity</option><option value="integration">Integration</option><option value="deployment">Deployment</option><option value="incident">Security incident</option></select></label>
            <label>Target reference<input value={sessionForm.targetReference} maxLength={250} onChange={(event) => setSessionForm((form) => ({ ...form, targetReference: event.target.value }))} required /></label>
            <label>Incident ID (optional)<input value={sessionForm.incidentId} onChange={(event) => setSessionForm((form) => ({ ...form, incidentId: event.target.value }))} placeholder="Paste a Module 997 incident UUID" /></label>
            <label>Investigation note<input value={sessionForm.note} maxLength={2000} onChange={(event) => setSessionForm((form) => ({ ...form, note: event.target.value }))} /></label>
            <button type="submit" disabled={!canManage || Boolean(state.action)}>{state.action === 'Running diagnostic session' ? 'Running…' : 'Run diagnostics'}</button>
          </form>
        </article>

        <article className="system-diagnostic-panel">
          <div className="system-diagnostic-heading"><div><p className="system-diagnostic-eyebrow">Current snapshot</p><h2>Live native checks</h2></div><span>{state.data.checks?.summary?.total ?? 0} checks</span></div>
          <div className="system-diagnostic-checks">{checks.map((check) => <article key={check.checkCode}><span className={`diagnostic-badge is-${tone(check.status)}`}>{titleCase(check.status)}</span><div><strong>{titleCase(check.checkCode)}</strong><p>{check.summary}</p><small>{titleCase(check.category)} · {titleCase(check.severity)}</small></div></article>)}</div>
        </article>
      </section>

      <section className="system-diagnostic-panel">
        <div className="system-diagnostic-heading"><div><p className="system-diagnostic-eyebrow">Troubleshooting history</p><h2>Diagnostic sessions</h2></div><span>{sessions.length} retained</span></div>
        <div className="system-diagnostic-table-wrap"><table className="system-diagnostic-table"><thead><tr><th>Target</th><th>Status</th><th>Severity</th><th>Findings</th><th>Incident</th><th>Action</th></tr></thead><tbody>
          {sessions.map((session) => <tr key={session.sessionId}><td><strong>{titleCase(session.targetKind)}</strong><span>{session.targetReference}</span><small>{when(session.updatedAt)}</small></td><td><span className={`diagnostic-badge is-${tone(session.status)}`}>{titleCase(session.status)}</span></td><td>{titleCase(session.severity)}</td><td>{session.findingCount} total · {session.failedCount} failed · {session.warningCount} warning</td><td>{session.incidentId || 'Not linked'}</td><td><button type="button" disabled={Boolean(state.action)} onClick={() => inspectSession(session.sessionId)}>Inspect evidence</button></td></tr>)}
          {!sessions.length ? <tr><td colSpan="6">No diagnostic session exists. Run one above to create retained evidence.</td></tr> : null}
        </tbody></table></div>
      </section>

      {state.selectedSession ? <section className="system-diagnostic-panel"><div className="system-diagnostic-heading"><div><p className="system-diagnostic-eyebrow">Selected evidence</p><h2>{state.selectedSession.session?.targetReference}</h2></div><span>{state.selectedSession.findings?.length ?? 0} findings</span></div><div className="system-diagnostic-evidence-grid">{(state.selectedSession.findings ?? []).map((finding) => <article key={finding.findingId}><span className={`diagnostic-badge is-${tone(finding.status)}`}>{titleCase(finding.status)}</span><strong>{titleCase(finding.checkCode)}</strong><p>{finding.summary}</p><small>{when(finding.observedAt)}</small></article>)}</div></section> : null}

      <section className="system-diagnostic-grid">
        <article className="system-diagnostic-panel">
          <div className="system-diagnostic-heading"><div><p className="system-diagnostic-eyebrow">Controlled remediation</p><h2>Prepare a runbook</h2></div><span>Preview first</span></div>
          <form className="system-diagnostic-form" onSubmit={prepareRemediation}>
            <label>Diagnostic session<select value={remediationForm.diagnosticSessionId} onChange={(event) => setRemediationForm((form) => ({ ...form, diagnosticSessionId: event.target.value }))} required><option value="">Select session</option>{sessions.filter((session) => session.status !== 'closed').map((session) => <option key={session.sessionId} value={session.sessionId}>{titleCase(session.targetKind)} · {session.targetReference}</option>)}</select></label>
            <label>Runbook<select value={remediationForm.runbookCode} onChange={(event) => chooseRunbook(event.target.value)}>{runbooks.map((runbook) => <option key={runbook.id} value={runbook.id}>{runbook.name}</option>)}</select></label>
            <label>Action<select value={remediationForm.actionCode} onChange={(event) => setRemediationForm((form) => ({ ...form, actionCode: event.target.value }))}>{(selectedRunbook?.actions ?? []).map((action) => <option key={action} value={action}>{titleCase(action)}</option>)}</select></label>
            <label>Target reference<input value={remediationForm.targetReference} maxLength={250} onChange={(event) => setRemediationForm((form) => ({ ...form, targetReference: event.target.value }))} required /></label>
            <label className="is-wide">Justification<textarea value={remediationForm.justification} maxLength={2000} onChange={(event) => setRemediationForm((form) => ({ ...form, justification: event.target.value }))} required /></label>
            {selectedRunbook ? <div className="system-diagnostic-preview is-wide"><strong>Preview</strong><span>{selectedRunbook.preview}</span><strong>Rollback</strong><span>{selectedRunbook.rollback}</span><small>Adapter: {titleCase(selectedRunbook.adapter)}</small></div> : null}
            <button type="submit" disabled={!canManage || Boolean(state.action)}>Prepare remediation</button>
          </form>
        </article>

        <article className="system-diagnostic-panel">
          <div className="system-diagnostic-heading"><div><p className="system-diagnostic-eyebrow">Approval and execution</p><h2>Remediation queue</h2></div><span>{remediations.length} requests</span></div>
          <div className="system-diagnostic-remediation-queue">{remediations.map((item) => <article key={item.remediationRequestId}><div><span className={`diagnostic-badge is-${tone(item.state)}`}>{titleCase(item.state)}</span><strong>{titleCase(item.action)}</strong></div><p>{titleCase(item.runbook)} · {item.targetReference}</p><small>Requested {when(item.requestedAt)}</small><div className="system-diagnostic-row-actions">
            {item.state === 'awaiting_approval' ? <button type="button" disabled={!canManage || Boolean(state.action)} onClick={() => run('Approving remediation', '/api/system-diagnostics/remediation/approve', { remediationRequestId: item.remediationRequestId, note: 'Approved after reviewing target, impact, and rollback.' })}>Approve as separate actor</button> : null}
            {item.state === 'approved' ? <button type="button" disabled={!canManage || Boolean(state.action)} onClick={() => run('Staging remediation', '/api/system-diagnostics/remediation/stage', { remediationRequestId: item.remediationRequestId, note: 'Target and rollback readiness confirmed.' })}>Stage</button> : null}
            {item.state === 'staged' ? <button type="button" disabled={!canManage || Boolean(state.action)} onClick={() => run('Executing remediation', '/api/system-diagnostics/remediation/promote', { remediationRequestId: item.remediationRequestId, note: 'Execute approved remediation.' })}>Execute</button> : null}
            {item.state === 'executed' ? <button type="button" disabled={!canManage || Boolean(state.action)} onClick={() => run('Verifying remediation', '/api/system-diagnostics/remediation/verify', { remediationRequestId: item.remediationRequestId, note: 'Rerun post-action checks.' })}>Verify</button> : null}
            {item.state === 'verified' ? <button type="button" disabled={!canManage || Boolean(state.action)} onClick={() => run('Closing remediation', '/api/system-diagnostics/remediation/close', { remediationRequestId: item.remediationRequestId, note: 'Verification passed and evidence is complete.' })}>Close</button> : null}
          </div></article>)}{!remediations.length ? <p>No remediation has been prepared. Select a diagnostic session and runbook.</p> : null}</div>
        </article>
      </section>

      <section className="system-diagnostic-panel"><div className="system-diagnostic-heading"><div><p className="system-diagnostic-eyebrow">Adapter readiness</p><h2>Production-changing automation</h2></div><span className="system-diagnostic-locked-label">Connector required</span></div><p>Restart, scale, rollback, replay, configuration refresh, and database repair can be planned and approved here. Execution returns the precise missing adapter until that authority is configured.</p><div className="system-diagnostic-locked-actions"><button type="button" disabled>Restart service</button><button type="button" disabled>Scale service</button><button type="button" disabled>Rollback deployment</button><button type="button" disabled>Replay integration event</button><button type="button" disabled>Refresh configuration</button><button type="button" disabled>Run database repair</button><button type="button" disabled>Execute rollback</button><button type="button" disabled>Run AI analysis</button></div></section>
    </section>
  );
}
