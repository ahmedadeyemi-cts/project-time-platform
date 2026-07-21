import { useCallback, useEffect, useMemo, useState } from 'react';
import { usSignalLogoDataUrl } from './assets/usSignalLogoData.js';
import './system-diagnostic-remediation-center.css';

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
    throw new Error(payload?.message ?? `Module 998 returned HTTP ${response.status}.`);
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

function statusTone(status) {
  const normalized = String(status ?? '').toLowerCase();
  if (normalized === 'healthy') return 'healthy';
  if (normalized === 'delegated' || normalized === 'governed') return 'governed';
  if (normalized === 'locked') return 'locked';
  return 'unknown';
}

const INITIAL_STATE = {
  loading: true,
  overview: null,
  checks: null,
  issues: null,
  evidence: null,
  remediation: null,
  runbooks: null,
  error: ''
};

export default function SystemDiagnosticRemediationCenter({ authSession }) {
  const [state, setState] = useState(INITIAL_STATE);

  const load = useCallback(async () => {
    setState((current) => ({ ...current, loading: true, error: '' }));

    try {
      const [overview, checks, issues, evidence, remediation, runbooks] = await Promise.all([
        readJson('/api/system-diagnostics/overview', authSession),
        readJson('/api/system-diagnostics/checks', authSession),
        readJson('/api/system-diagnostics/issues', authSession),
        readJson('/api/system-diagnostics/evidence-policy', authSession),
        readJson('/api/system-diagnostics/remediation-policy', authSession),
        readJson('/api/system-diagnostics/runbooks', authSession)
      ]);

      setState({
        loading: false,
        overview,
        checks,
        issues,
        evidence,
        remediation,
        runbooks,
        error: ''
      });
    } catch (error) {
      setState((current) => ({
        ...current,
        loading: false,
        error: error?.message ?? 'System diagnostics are temporarily unavailable.'
      }));
    }
  }, [authSession]);

  useEffect(() => {
    void load();
  }, [load]);

  const summary = state.checks?.summary ?? {};
  const categories = useMemo(() => {
    const categoryMap = new Map(
      (state.overview?.categories ?? []).map((category) => [category.id, {
        ...category,
        checks: []
      }])
    );

    (state.checks?.checks ?? []).forEach((check) => {
      const category = categoryMap.get(check.category);
      if (category) category.checks.push(check);
    });

    return [...categoryMap.values()];
  }, [state.overview, state.checks]);

  return (
    <section
      className="system-diagnostic-center"
      data-module="998"
      data-contract-version={state.overview?.contractVersion ?? '2026-07-20.1'}
      data-execution-mode="fail-closed"
    >
      <header className="system-diagnostic-hero">
        <div className="system-diagnostic-brand">
          <img src={usSignalLogoDataUrl} alt="US Signal" />
          <div>
            <p className="system-diagnostic-eyebrow">ProjectPulse · Module 998</p>
            <h1>System Diagnostic &amp; Controlled Remediation Center</h1>
            <p>
              A governed view of platform diagnostics, issue classification,
              ownership, evidence, and remediation readiness. Production actions
              stay locked until they receive separate authorization.
            </p>
          </div>
        </div>
        <div className="system-diagnostic-hero-actions">
          <span className="system-diagnostic-mode">Fail-closed source checkpoint</span>
          <button type="button" onClick={load} disabled={state.loading}>
            {state.loading ? 'Refreshing…' : 'Refresh safe checks'}
          </button>
        </div>
      </header>

      <div className="system-diagnostic-boundary" role="status">
        <strong>No production execution</strong>
        <span>
          Remediation, containment, telemetry connectors, external notifications,
          AI execution, promotion, rollback, and secret access are disabled.
        </span>
      </div>

      {state.error ? (
        <div className="system-diagnostic-error" role="alert">
          <strong>Diagnostics unavailable</strong>
          <span>{state.error}</span>
        </div>
      ) : null}

      {state.overview?.access?.isViewAs ? (
        <div className="system-diagnostic-view-as">
          View-As is active. Module 998 authorization still uses the actual
          ProjectPulse session and never transfers privileged authority.
        </div>
      ) : null}

      <section className="system-diagnostic-metrics" aria-label="Diagnostic summary">
        <article>
          <span>Checks</span>
          <strong>{summary.total ?? 0}</strong>
          <small>Direct and delegated</small>
        </article>
        <article>
          <span>Direct healthy</span>
          <strong>{summary.healthy ?? 0}</strong>
          <small>Observed this request</small>
        </article>
        <article>
          <span>Delegated / governed</span>
          <strong>{(summary.delegated ?? 0) + (summary.governed ?? 0)}</strong>
          <small>Verify with source owner</small>
        </article>
        <article>
          <span>Live issues</span>
          <strong>{state.issues?.activeIssues?.length ?? 0}</strong>
          <small>Telemetry not connected</small>
        </article>
      </section>

      <section className="system-diagnostic-panel">
        <div className="system-diagnostic-heading">
          <div>
            <p className="system-diagnostic-eyebrow">Diagnostic coverage</p>
            <h2>Safe checks and authoritative owners</h2>
          </div>
          <span>Observed {observedTime(state.checks?.observedAt)}</span>
        </div>

        <div className="system-diagnostic-category-grid">
          {categories.map((category) => (
            <article key={category.id} className="system-diagnostic-category">
              <header>
                <div>
                  <h3>{category.name}</h3>
                  <p>{category.description}</p>
                </div>
                <span>{category.owner}</span>
              </header>
              <ul>
                {category.checks.map((check) => (
                  <li key={check.id}>
                    <div>
                      <strong>{check.name}</strong>
                      <small>{check.detail}</small>
                    </div>
                    {check.route ? (
                      <a href={check.route}>{check.owner}</a>
                    ) : (
                      <span>{check.owner}</span>
                    )}
                    <b className={`tone-${statusTone(check.status)}`}>
                      {titleCase(check.status)}
                    </b>
                  </li>
                ))}
              </ul>
            </article>
          ))}
        </div>
      </section>

      <section className="system-diagnostic-two-column">
        <article className="system-diagnostic-panel">
          <div className="system-diagnostic-heading">
            <div>
              <p className="system-diagnostic-eyebrow">Issue intelligence</p>
              <h2>Sanitized classification contract</h2>
            </div>
          </div>
          <p className="system-diagnostic-copy">{state.issues?.statement}</p>
          <div className="system-diagnostic-severity-list">
            {(state.issues?.classifiers ?? []).map((classifier) => (
              <div key={classifier.severity}>
                <span>{classifier.order}</span>
                <div>
                  <strong>{titleCase(classifier.severity)}</strong>
                  <small>{classifier.definition}</small>
                </div>
                <b>{classifier.responseExpectation}</b>
              </div>
            ))}
          </div>
        </article>

        <article className="system-diagnostic-panel">
          <div className="system-diagnostic-heading">
            <div>
              <p className="system-diagnostic-eyebrow">Evidence boundary</p>
              <h2>Redacted operational metadata</h2>
            </div>
          </div>
          <dl className="system-diagnostic-policy">
            <div><dt>Classification</dt><dd>{titleCase(state.evidence?.evidence?.classification)}</dd></div>
            <div><dt>Raw logs</dt><dd>Disabled</dd></div>
            <div><dt>Secret access</dt><dd>Disabled</dd></div>
            <div><dt>Export</dt><dd>Disabled</dd></div>
          </dl>
          <h3>Required metadata</h3>
          <ul className="system-diagnostic-tag-list">
            {(state.evidence?.evidence?.requiredFields ?? []).map((field) => (
              <li key={field}>{field}</li>
            ))}
          </ul>
        </article>
      </section>

      <section className="system-diagnostic-panel">
        <div className="system-diagnostic-heading">
          <div>
            <p className="system-diagnostic-eyebrow">Controlled remediation</p>
            <h2>Separated lifecycle — every execution gate locked</h2>
          </div>
          <span className="system-diagnostic-locked-label">Execution disabled</span>
        </div>

        <div className="system-diagnostic-lifecycle">
          {(state.remediation?.lifecycle ?? []).map((step) => (
            <article key={step.code}>
              <span>{step.step}</span>
              <div>
                <strong>{titleCase(step.code)}</strong>
                <p>{step.purpose}</p>
              </div>
              <b>{titleCase(step.state)}</b>
            </article>
          ))}
        </div>

        <div className="system-diagnostic-locked-actions" aria-label="Locked remediation controls">
          <button type="button" disabled>Prepare remediation</button>
          <button type="button" disabled>Request approval</button>
          <button type="button" disabled>Stage safely</button>
          <button type="button" disabled>Promote to production</button>
          <button type="button" disabled>Verify outcome</button>
          <button type="button" disabled>Execute rollback</button>
          <button type="button" disabled>Close remediation</button>
          <button type="button" disabled>Run AI analysis</button>
        </div>
      </section>

      <section className="system-diagnostic-panel">
        <div className="system-diagnostic-heading">
          <div>
            <p className="system-diagnostic-eyebrow">Guidance only</p>
            <h2>Governed diagnostic runbooks</h2>
          </div>
          <span>{titleCase(state.runbooks?.executionMode)}</span>
        </div>
        <div className="system-diagnostic-runbooks">
          {(state.runbooks?.runbooks ?? []).map((runbook) => (
            <article key={runbook.id}>
              <header>
                <h3>{runbook.name}</h3>
                {runbook.route ? <a href={runbook.route}>{runbook.owner}</a> : <span>{runbook.owner}</span>}
              </header>
              <ol>
                {(runbook.steps ?? []).map((step) => <li key={step}>{step}</li>)}
              </ol>
            </article>
          ))}
        </div>
      </section>
    </section>
  );
}
