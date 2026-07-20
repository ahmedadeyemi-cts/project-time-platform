import { useCallback, useEffect, useMemo, useState } from 'react';
import { usSignalLogoDataUrl } from './assets/usSignalLogoData.js';
import './entra-secret-administration-center.css';
import './projectpulse-module-standard.css';

function sessionToken(authSession) {
  return authSession?.sessionToken ?? authSession?.token ?? authSession?.accessToken
    ?? window.localStorage.getItem('projectPulseSessionToken')
    ?? window.sessionStorage.getItem('projectPulseSessionToken') ?? '';
}

async function requestJson(path, authSession) {
  const token = sessionToken(authSession);
  const response = await fetch(path, {
    method: 'GET',
    credentials: 'include',
    headers: token ? {
      Authorization: `Bearer ${token}`,
      'X-ProjectPulse-Session': token,
      'X-Project-Pulse-Session': token,
      'X-Session-Token': token
    } : {}
  });
  const payload = await response.json().catch(() => null);
  if (!response.ok) throw new Error(payload?.message ?? `Module 065 returned HTTP ${response.status}.`);
  return payload;
}

function display(value, fallback = 'Not configured') {
  return value === null || value === undefined || value === '' ? fallback : String(value);
}

function date(value) {
  if (!value) return 'Not recorded';
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? value : parsed.toLocaleString();
}

function sentence(value) {
  return display(value, 'unknown').replaceAll('_', ' ');
}

const EMPTY_STATE = {
  loading: true,
  capabilities: null,
  metadata: null,
  readiness: null,
  workflow: null,
  audit: null,
  error: ''
};

export default function EntraSecretAdministrationCenter({ authSession }) {
  const [state, setState] = useState(EMPTY_STATE);

  const load = useCallback(async () => {
    setState((current) => ({ ...current, loading: true, error: '' }));
    try {
      const [capabilities, metadata, readiness, workflow, audit] = await Promise.all([
        requestJson('/api/entra-secret-administration/capabilities', authSession),
        requestJson('/api/entra-secret-administration/metadata', authSession),
        requestJson('/api/entra-secret-administration/readiness', authSession),
        requestJson('/api/entra-secret-administration/workflow-contract', authSession),
        requestJson('/api/entra-secret-administration/audit-contract', authSession)
      ]);
      setState({ loading: false, capabilities, metadata, readiness, workflow, audit, error: '' });
    } catch (error) {
      setState((current) => ({
        ...current,
        loading: false,
        error: error?.message ?? 'Credential lifecycle readiness is unavailable.'
      }));
    }
  }, [authSession]);

  useEffect(() => { void load(); }, [load]);

  const health = state.metadata?.health ?? 'unknown';
  const gate = state.capabilities?.rotation ?? {};
  const access = state.capabilities?.access ?? {};
  const passedChecks = useMemo(
    () => (state.readiness?.checks ?? []).filter((check) => check.passed).length,
    [state.readiness]
  );
  const checkCount = state.readiness?.checks?.length ?? 0;

  return (
    <section
      className="panel entra-secret-center projectpulse-module-standard"
      data-module="065"
      data-brand="us-signal"
      data-phase="065_COMPLETE_SOURCE_LOCKED_RUNTIME"
      aria-labelledby="entra-secret-title"
    >
      <header className="entra-secret-hero">
        <div className="entra-secret-lockup">
          <img src={usSignalLogoDataUrl} alt="US Signal" />
          <div>
            <p>Module 065 · Identity security</p>
            <h1 id="entra-secret-title">Entra Secret Administration</h1>
            <span>Credential metadata, expiration health, and a governed rotation state machine built around the existing ProjectPulse identity platform.</span>
          </div>
        </div>
        <div className={`entra-secret-health ${health}`}>
          <strong>{sentence(health)}</strong>
          <small>Secret value is never displayed</small>
        </div>
      </header>

      <div className="entra-secret-stripe" aria-hidden="true"><i /><i /><i /></div>

      {state.error ? <div className="entra-secret-banner error" role="alert">{state.error}</div> : null}
      <div className={`entra-secret-banner ${gate.enabled ? 'warning' : 'locked'}`}>
        <strong>{gate.enabled ? 'Authorized adapter gate is ready' : 'Credential mutation is locked'}</strong>
        <span>
          {gate.enabled
            ? 'A recent server-established step-up context and workflow approval remain required for every action.'
            : 'No request body or secret is read until explicit Azure/Entra authorization, the mutation switch, and an approved adapter are all present.'}
        </span>
      </div>

      <section className="entra-secret-card">
        <div className="entra-secret-card-head">
          <div><p>Non-secret metadata</p><h2>Active application credential</h2></div>
          <button type="button" onClick={load} disabled={state.loading}>{state.loading ? 'Refreshing…' : 'Refresh metadata'}</button>
        </div>
        <dl className="entra-secret-facts">
          <div><dt>Application</dt><dd>{display(state.metadata?.applicationName)}</dd></div>
          <div><dt>Environment</dt><dd>{display(state.metadata?.environment)}</dd></div>
          <div><dt>Tenant ID</dt><dd><code>{display(state.metadata?.tenantId)}</code></dd></div>
          <div><dt>Client ID</dt><dd><code>{display(state.metadata?.clientId)}</code></dd></div>
          <div><dt>Credential type</dt><dd>{display(state.metadata?.credentialType)}</dd></div>
          <div><dt>Active version</dt><dd><code>{display(state.metadata?.activeVersion)}</code></dd></div>
          <div><dt>Fingerprint</dt><dd><code>{display(state.metadata?.fingerprint)}</code></dd></div>
          <div><dt>Last rotation</dt><dd>{date(state.metadata?.lastRotationAt)}</dd></div>
          <div><dt>Expires</dt><dd>{date(state.metadata?.expiresAt)}</dd></div>
          <div><dt>Days remaining</dt><dd>{display(state.metadata?.daysUntilExpiration, 'Unknown')}</dd></div>
        </dl>
        <div className="entra-secret-source-note">
          Tenant metadata source: <strong>{sentence(state.metadata?.tenantMetadataSource)}</strong>. Module 010 remains the owner of Entra tenant settings and user synchronization.
        </div>
      </section>

      <section className="entra-secret-gate-grid" aria-label="Credential mutation gates">
        <article className={gate.mutationSwitchEnabled ? 'ready' : ''}><span>01</span><div><strong>Mutation switch</strong><small>{gate.mutationSwitchEnabled ? 'Enabled' : 'Disabled'}</small></div></article>
        <article className={gate.externalAuthorizationRecorded ? 'ready' : ''}><span>02</span><div><strong>External authorization</strong><small>{gate.externalAuthorizationRecorded ? 'Recorded' : 'Not recorded'}</small></div></article>
        <article className={gate.approvedAdapterConfigured ? 'ready' : ''}><span>03</span><div><strong>Approved adapter</strong><small>{gate.approvedAdapterConfigured ? 'Configured' : 'Locked adapter'}</small></div></article>
        <article className={access.canRotate ? 'ready' : ''}><span>04</span><div><strong>Actual authority</strong><small>{access.canRotate ? 'Eligible' : 'Read only'}</small></div></article>
      </section>

      <div className="entra-secret-layout">
        <section className="entra-secret-card">
          <div className="entra-secret-card-head">
            <div><p>Preflight controls</p><h2>Rotation readiness</h2></div>
            <span className="entra-secret-score">{passedChecks}/{checkCount}</span>
          </div>
          <ul className="entra-secret-checks">
            {(state.readiness?.checks ?? []).map((check) => (
              <li key={check.code} className={check.passed ? 'passed' : 'pending'}>
                <span>{check.passed ? '✓' : '—'}</span>
                <div><strong>{sentence(check.code)}</strong><small>{check.description}</small></div>
              </li>
            ))}
          </ul>
        </section>

        <section className="entra-secret-card">
          <div className="entra-secret-card-head"><div><p>Controlled lifecycle</p><h2>Rotation state machine</h2></div></div>
          <ol className="entra-secret-steps">
            {(state.workflow?.stateMachine ?? []).map((step, index) => (
              <li key={step.code}>
                <span>{index + 1}</span>
                <div><strong>{sentence(step.code)}</strong><small>{step.description}</small></div>
              </li>
            ))}
          </ol>
        </section>
      </div>

      <section className="entra-secret-card entra-secret-controls">
        <div className="entra-secret-card-head"><div><p>Fail-closed action surface</p><h2>Privileged lifecycle controls</h2></div></div>
        <div className="entra-secret-action-grid">
          {['Prepare rotation', 'Second approval', 'Stage write-only value', 'Test token acquisition', 'Activate with overlap', 'Rollback prior version'].map((action) => (
            <button key={action} type="button" disabled title="Requires an approved adapter, external authorization, and recent step-up authentication.">
              <strong>{action}</strong><small>Locked pending external authorization</small>
            </button>
          ))}
        </div>
        <p>No secret entry field is rendered. A later separately authorized secure client must send the write-only value directly as <code>application/octet-stream</code>; it must never enter browser persistence.</p>
      </section>

      <section className="entra-secret-card entra-secret-audit">
        <div className="entra-secret-card-head"><div><p>Sanitized immutable evidence</p><h2>Audit contract</h2></div></div>
        <div><strong>Required</strong><span>{(state.audit?.requiredFields ?? []).join(' · ')}</span></div>
        <div><strong>Prohibited</strong><span>{(state.audit?.prohibitedFields ?? []).join(' · ')}</span></div>
      </section>

      <footer className="entra-secret-footer">
        <img src={usSignalLogoDataUrl} alt="" aria-hidden="true" />
        <span>US Signal · Secure by design</span>
        <small>Module 065 · Complete governed source · Runtime mutation locked</small>
      </footer>
    </section>
  );
}
