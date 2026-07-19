import { useCallback, useEffect, useMemo, useState } from 'react';
import './global-mail-configuration-center.css';

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
  return token
    ? {
        Authorization: `Bearer ${token}`,
        'X-ProjectPulse-Session': token,
        'X-Project-Pulse-Session': token,
        'X-Session-Token': token
      }
    : {};
}

async function readJson(path, authSession) {
  const response = await fetch(path, {
    method: 'GET',
    credentials: 'include',
    headers: requestHeaders(authSession)
  });
  const payload = await response.json().catch(() => null);
  if (!response.ok) {
    throw new Error(payload?.message ?? `Global Mail request returned HTTP ${response.status}.`);
  }
  return payload;
}

function words(value) {
  return String(value ?? 'unknown')
    .replace(/_/g, ' ')
    .replace(/\b\w/g, (letter) => letter.toUpperCase());
}

function stateTone(value) {
  const state = String(value ?? '').toLowerCase();
  if (['ready', 'ready_for_controlled_connectivity_validation'].includes(state)) return 'ready';
  if (['locked', 'not_observed', 'configured_not_selected'].includes(state)) return 'governed';
  if (['missing', 'configuration_incomplete', 'active_legacy_provider'].includes(state)) return 'attention';
  return 'neutral';
}

export default function GlobalMailConfigurationCenter({ authSession }) {
  const [state, setState] = useState({ loading: true, configuration: null, health: null, error: '' });

  const load = useCallback(async () => {
    setState((current) => ({ ...current, loading: true, error: '' }));
    try {
      const [configuration, health] = await Promise.all([
        readJson('/api/global-mail/configuration', authSession),
        readJson('/api/global-mail/health', authSession)
      ]);
      setState({ loading: false, configuration, health, error: '' });
    } catch (error) {
      setState((current) => ({
        ...current,
        loading: false,
        error: error?.message ?? 'Global Mail Configuration is temporarily unavailable.'
      }));
    }
  }, [authSession]);

  useEffect(() => {
    void load();
  }, [load]);

  const config = state.configuration?.configuration;
  const migration = state.configuration?.migration;
  const readyChecks = useMemo(
    () => (state.health?.checks ?? []).filter((check) => check.state === 'ready').length,
    [state.health]
  );

  return (
    <section
      id="global-mail-configuration"
      className="panel global-mail-center"
      data-module="067"
      data-mode="read-only-configuration"
      aria-labelledby="global-mail-title"
    >
      <header className="global-mail-hero">
        <div>
          <p className="eyebrow">Module 067 · Administrator-only configuration</p>
          <h1 id="global-mail-title">Global Mail Configuration Center</h1>
          <p>
            One non-secret view of ProjectPulse outbound-mail configuration,
            Microsoft 365 migration readiness, shared consumers, and the controls
            that must pass before an authorized provider cutover.
          </p>
        </div>
        <button type="button" className="secondary-action" onClick={load} disabled={state.loading}>
          {state.loading ? 'Refreshing…' : 'Refresh status'}
        </button>
      </header>

      {state.error ? <div className="global-mail-banner error" role="alert">{state.error}</div> : null}

      <div className="global-mail-banner governed">
        Secret rotation, activation, connectivity tests, and real email delivery
        are locked pending explicit Azure, Entra, and deployment authorization.
      </div>

      <div className="global-mail-summary">
        <article>
          <span>Selected provider</span>
          <strong>{words(config?.activeProvider)}</strong>
          <small>Approved target: Microsoft Graph</small>
        </article>
        <article>
          <span>Migration state</span>
          <strong className={stateTone(migration?.state)}>{words(migration?.state)}</strong>
          <small>Brevo must be disabled before cutover</small>
        </article>
        <article>
          <span>Readiness checks</span>
          <strong>{readyChecks}/{state.health?.checks?.length ?? 0}</strong>
          <small>{state.health?.blockingCheckCount ?? 0} blocking checks</small>
        </article>
        <article>
          <span>Provider calls</span>
          <strong>0</strong>
          <small>Configuration-only observation</small>
        </article>
      </div>

      <div className="global-mail-columns">
        <section className="global-mail-card">
          <div className="global-mail-heading">
            <div>
              <p className="eyebrow">Non-secret settings</p>
              <h2>Microsoft 365 delivery boundary</h2>
            </div>
          </div>
          <dl className="global-mail-settings">
            <div><dt>Authentication</dt><dd>{words(config?.authenticationMode)}</dd></div>
            <div><dt>Tenant</dt><dd>{config?.tenant?.maskedValue ?? 'Not configured'}</dd></div>
            <div><dt>Client</dt><dd>{config?.client?.maskedValue ?? 'Not configured'}</dd></div>
            <div><dt>Sender mailbox</dt><dd>{config?.senderMailbox?.value ?? 'Not configured'}</dd></div>
            <div><dt>Reply-To</dt><dd>{config?.replyTo?.value ?? 'Not configured'}</dd></div>
            <div><dt>Timeout / retries</dt><dd>{config?.timeoutSeconds ?? 0}s / {config?.retryLimit ?? 0}</dd></div>
            <div><dt>Recipient boundary</dt><dd>{config?.recipientEnvironment?.value ?? 'Not configured'}</dd></div>
            <div><dt>Password authentication</dt><dd>Not allowed</dd></div>
          </dl>
        </section>

        <section className="global-mail-card">
          <div className="global-mail-heading">
            <div>
              <p className="eyebrow">Write-only material</p>
              <h2>Secret metadata</h2>
            </div>
          </div>
          <div className="global-mail-secret-list">
            {(state.configuration?.secretMetadata ?? []).map((secret) => (
              <article key={secret.name}>
                <div>
                  <strong>{words(secret.name)}</strong>
                  <span className={`global-mail-state ${secret.configured ? 'ready' : 'attention'}`}>
                    {secret.configured ? 'Configured' : 'Missing'}
                  </span>
                </div>
                <p>Fingerprint: {secret.fingerprint}</p>
                <small>Source: {secret.source}</small>
              </article>
            ))}
          </div>
        </section>
      </div>

      <section className="global-mail-card">
        <div className="global-mail-heading">
          <div>
            <p className="eyebrow">Readiness</p>
            <h2>Controlled migration gates</h2>
          </div>
          <span className={`global-mail-state ${stateTone(state.health?.overallState)}`}>
            {words(state.health?.overallState)}
          </span>
        </div>
        <div className="global-mail-check-grid">
          {(state.health?.checks ?? []).map((check) => (
            <article key={check.id}>
              <span className={`global-mail-state ${stateTone(check.state)}`}>{words(check.state)}</span>
              <strong>{check.name}</strong>
              <p>{check.evidence}</p>
            </article>
          ))}
        </div>
      </section>

      <section className="global-mail-card">
        <div className="global-mail-heading">
          <div>
            <p className="eyebrow">Shared ownership</p>
            <h2>Outbound-mail consumer registry</h2>
          </div>
        </div>
        <div className="global-mail-table-wrap">
          <table>
            <thead><tr><th>Consumer</th><th>Owner</th><th>Purpose</th><th>Migration state</th></tr></thead>
            <tbody>
              {(state.configuration?.consumerRegistry ?? []).map((consumer) => (
                <tr key={consumer.id}>
                  <td>{words(consumer.id)}</td>
                  <td>{consumer.owner}</td>
                  <td>{consumer.purpose}</td>
                  <td><span className="global-mail-state governed">{words(consumer.state)}</span></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>
    </section>
  );
}
