import { useCallback, useEffect, useMemo, useState } from 'react';
import { usSignalLogoDataUrl } from './assets/usSignalLogoData.js';
import './ai-provider-configuration-center.css';
import './projectpulse-module-standard.css';

const PROVIDER_LABELS = {
  claude: 'Claude',
  openai: 'OpenAI',
  local_template: 'Governed local template',
};

function formatDate(value) {
  if (!value) return 'Not recorded';
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? 'Not recorded' : parsed.toLocaleString();
}

function statusClass(status) {
  if (['available', 'ready'].includes(status)) return 'healthy';
  if (['disabled', 'not_configured'].includes(status)) return 'inactive';
  return 'degraded';
}

async function readJson(response) {
  const payload = await response.json().catch(() => ({}));
  if (!response.ok) {
    throw new Error(payload.message || 'AI provider configuration could not be loaded.');
  }
  return payload;
}

export default function AiProviderConfigurationCenter() {
  const [state, setState] = useState({ loading: true, error: '', payload: null });
  const [refreshing, setRefreshing] = useState(false);
  const [notice, setNotice] = useState('');
  const [keys, setKeys] = useState({ claude: '', openai: '' });
  const [models, setModels] = useState({});
  const [savingProvider, setSavingProvider] = useState('');
  const [savingModel, setSavingModel] = useState('');
  const [changingState, setChangingState] = useState('');

  const load = useCallback(async () => {
    setState((current) => ({ ...current, loading: true, error: '' }));
    try {
      const payload = await readJson(await fetch('/api/ai-configuration'));
      setState({ loading: false, error: '', payload });
    } catch (error) {
      setState({
        loading: false,
        error: error instanceof Error ? error.message : 'AI provider configuration could not be loaded.',
        payload: null,
      });
    }
  }, []);

  useEffect(() => {
    load();
  }, [load]);

  const configuration = state.payload?.configuration;
  const providers = configuration?.providers ?? [];
  const healthByProvider = useMemo(
    () => new Map((state.payload?.health ?? []).map((item) => [item.provider, item])),
    [state.payload],
  );

  async function refreshHealth() {
    setRefreshing(true);
    setNotice('');
    try {
      const result = await readJson(await fetch('/api/ai-configuration/health/refresh', { method: 'POST' }));
      setNotice(result.message || 'Provider health checks completed.');
      await load();
    } catch (error) {
      setNotice(error instanceof Error ? error.message : 'Provider health checks could not be completed.');
    } finally {
      setRefreshing(false);
    }
  }

  async function saveKey(event, providerCode) {
    event.preventDefault();
    const apiKey = keys[providerCode]?.trim();
    if (!apiKey) return;
    setSavingProvider(providerCode);
    setNotice('');
    try {
      const result = await readJson(await fetch(`/api/ai-configuration/providers/${providerCode}/secret`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ apiKey }),
      }));
      setKeys((current) => ({ ...current, [providerCode]: '' }));
      setNotice(result.message || 'API key saved securely.');
      await load();
    } catch (error) {
      setNotice(error instanceof Error ? error.message : 'The API key could not be saved.');
    } finally {
      setSavingProvider('');
    }
  }

  async function saveModel(event, providerCode, activeModel) {
    event.preventDefault();
    const model = models[providerCode] || activeModel;
    if (!model || model === activeModel) return;
    setSavingModel(providerCode);
    setNotice('');
    try {
      const result = await readJson(await fetch(`/api/ai-configuration/providers/${providerCode}/model`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ model }),
      }));
      setNotice(result.message || 'Model saved and tested.');
      await load();
    } catch (error) {
      setNotice(error instanceof Error ? error.message : 'The model could not be saved and tested.');
    } finally {
      setSavingModel('');
    }
  }

  async function setProviderEnabled(providerCode, enabled) {
    setChangingState(providerCode);
    setNotice('');
    try {
      const result = await readJson(await fetch(`/api/ai-configuration/providers/${providerCode}/enabled`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ enabled }),
      }));
      setNotice(result.message || `Provider ${enabled ? 'enabled' : 'disabled'}.`);
      await load();
    } catch (error) {
      setNotice(error instanceof Error ? error.message : 'The provider state could not be changed.');
    } finally {
      setChangingState('');
    }
  }

  return (
    <div className="ai-provider-center projectpulse-module-standard" data-module="064"
      data-brand="us-signal">
      <header className="ai-provider-center__header">
        <img
          className="projectpulse-module-standard__logo"
          src={usSignalLogoDataUrl}
          alt="US Signal"
        />
        <div>
          <p className="ai-provider-center__eyebrow">Module 064 · governed shared service</p>
          <h1>AI Provider Configuration Center</h1>
          <p>
            ProjectPulse checks provider health and routes each AI request once through Claude,
            then OpenAI, then the governed local fallback. A safety refusal never triggers another provider.
          </p>
        </div>
        <button type="button" onClick={refreshHealth} disabled={refreshing || state.loading}>
          {refreshing ? 'Checking providers…' : 'Refresh provider health'}
        </button>
      </header>

      {notice ? <div className="ai-provider-center__notice" role="status">{notice}</div> : null}
      {state.loading ? <div className="ai-provider-center__state">Loading shared AI configuration…</div> : null}
      {state.error ? (
        <div className="ai-provider-center__state ai-provider-center__state--error" role="alert">
          <p>{state.error}</p>
          <button type="button" onClick={load}>Try again</button>
        </div>
      ) : null}

      {configuration ? (
        <>
          <section className="ai-provider-center__summary" aria-label="Shared routing summary">
            <article>
              <span>Routing mode</span>
              <strong>{configuration.mode?.replaceAll('_', ' ')}</strong>
            </article>
            <article>
              <span>Execution</span>
              <strong>Sequential, no duplicate calls</strong>
            </article>
            <article>
              <span>Timeout / retries</span>
              <strong>{configuration.execution?.requestTimeoutSeconds}s / {configuration.execution?.retryCount}</strong>
            </article>
            <article>
              <span>Output limit</span>
              <strong>{configuration.execution?.maxOutputTokens} tokens</strong>
            </article>
          </section>

          <section className="ai-provider-center__section">
            <div className="ai-provider-center__section-heading">
              <div>
                <p className="ai-provider-center__eyebrow">Provider status</p>
                <h2>Availability, configuration, and usage</h2>
              </div>
              <span>Keys are never returned to this page</span>
            </div>

            <div className="ai-provider-center__providers">
              {providers.map((provider) => {
                const health = healthByProvider.get(provider.code) ?? {};
                return (
                  <article className="ai-provider-center__provider" key={provider.code}>
                    <div className="ai-provider-center__provider-heading">
                      <div>
                        <h3>{provider.displayName || PROVIDER_LABELS[provider.code] || provider.code}</h3>
                        <p>{provider.model}</p>
                      </div>
                      <span className={`ai-provider-center__status ai-provider-center__status--${statusClass(health.status)}`}>
                        {(health.status || 'not checked').replaceAll('_', ' ')}
                      </span>
                    </div>
                    <dl>
                      <div><dt>Enabled</dt><dd>{provider.enabled ? 'Yes' : 'No'}</dd></div>
                      <div><dt>Configured</dt><dd>{provider.configured ? 'Yes' : 'No'}</dd></div>
                      <div><dt>Endpoint</dt><dd>{provider.endpoint || 'Local only'}</dd></div>
                      <div><dt>API version</dt><dd>{provider.apiVersion || 'Not applicable'}</dd></div>
                      <div><dt>Last check</dt><dd>{formatDate(health.lastCheckedAt)}</dd></div>
                      <div><dt>Last success</dt><dd>{formatDate(health.lastSuccessAt)}</dd></div>
                      <div><dt>Requests succeeded</dt><dd>{health.successCount ?? 0}</dd></div>
                      <div><dt>Failures / refusals</dt><dd>{health.failureCount ?? 0} / {health.refusalCount ?? 0}</dd></div>
                      <div><dt>Last failure code</dt><dd>{health.lastFailureCode ?? 'None'}</dd></div>
                      <div><dt>Last provider request</dt><dd>{health.lastRequestId ?? 'Not reported'}</dd></div>
                      <div><dt>Input / output tokens</dt><dd>{health.inputTokens ?? 0} / {health.outputTokens ?? 0}</dd></div>
                      <div><dt>Requests remaining</dt><dd>{health.rateLimits?.requestsRemaining ?? 'Not reported'}</dd></div>
                      <div><dt>Tokens remaining</dt><dd>{health.rateLimits?.tokensRemaining ?? 'Not reported'}</dd></div>
                      <div><dt>Request reset</dt><dd>{health.rateLimits?.requestsReset ?? 'Not reported'}</dd></div>
                      <div><dt>Token reset</dt><dd>{health.rateLimits?.tokensReset ?? 'Not reported'}</dd></div>
                      <div><dt>Circuit open until</dt><dd>{formatDate(health.circuitOpenUntil)}</dd></div>
                    </dl>
                    {provider.code !== 'local_template' ? (
                      <div className="ai-provider-center__provider-controls">
                      <div className="ai-provider-center__enable-control">
                        <div><strong>Provider routing</strong><small>Disabling preserves the saved key and model.</small></div>
                        <button
                          type="button"
                          className={provider.enabled ? 'ai-provider-center__danger-button' : ''}
                          onClick={() => setProviderEnabled(provider.code, !provider.enabled)}
                          disabled={changingState === provider.code || (!provider.configured && !provider.enabled)}
                        >
                          {changingState === provider.code ? 'Updating…' : provider.enabled ? 'Disable' : 'Enable'}
                        </button>
                      </div>
                      <form className="ai-provider-center__model-form" onSubmit={(event) => saveModel(event, provider.code, provider.model)}>
                        <label htmlFor={`provider-model-${provider.code}`}>Active model</label>
                        <div>
                          <select
                            id={`provider-model-${provider.code}`}
                            value={models[provider.code] || provider.model}
                            onChange={(event) => setModels((current) => ({ ...current, [provider.code]: event.target.value }))}
                            disabled={!provider.configured || savingModel === provider.code}
                          >
                            {(provider.approvedModels || [provider.model]).map((model) => <option value={model} key={model}>{model}</option>)}
                          </select>
                          <button type="submit" disabled={!provider.configured || savingModel === provider.code || (models[provider.code] || provider.model) === provider.model}>
                            {savingModel === provider.code ? 'Testing…' : 'Save and test'}
                          </button>
                        </div>
                        <small>{provider.configured ? 'The new model activates only after the saved key verifies it.' : 'Save an API key before changing the model.'}</small>
                      </form>
                      </div>
                    ) : null}
                    {provider.secret ? (
                      <div className="ai-provider-center__secret">
                        <strong>Write-only secret metadata</strong>
                        <span>Source: {provider.secret.source || 'Not recorded'}</span>
                        <span>Version: {provider.secret.version || 'Not recorded'}</span>
                        <span>Fingerprint: {provider.secret.fingerprint || 'Not configured'}</span>
                        <span>Rotation: {formatDate(provider.secret.rotatedAt)}</span>
                        <span>Expiry: {formatDate(provider.secret.expiresAt)}</span>
                        <form className="ai-provider-center__secret-form" onSubmit={(event) => saveKey(event, provider.code)}>
                          <label htmlFor={`provider-key-${provider.code}`}>
                            {provider.configured ? 'Replace API key' : 'Add API key'}
                          </label>
                          <div>
                            <input
                              id={`provider-key-${provider.code}`}
                              type="password"
                              value={keys[provider.code] || ''}
                              onChange={(event) => setKeys((current) => ({ ...current, [provider.code]: event.target.value }))}
                              autoComplete="new-password"
                              spellCheck="false"
                              maxLength={8192}
                              placeholder="Paste key once"
                              disabled={savingProvider === provider.code}
                            />
                            <button type="submit" disabled={!keys[provider.code]?.trim() || savingProvider === provider.code}>
                              {savingProvider === provider.code ? 'Saving…' : 'Save securely'}
                            </button>
                          </div>
                          <small>The key is write-only and disappears from this form immediately after saving.</small>
                        </form>
                      </div>
                    ) : null}
                  </article>
                );
              })}
            </div>
          </section>

          <section className="ai-provider-center__section">
            <div className="ai-provider-center__section-heading">
              <div>
                <p className="ai-provider-center__eyebrow">Feature routing</p>
                <h2>One governed route per AI capability</h2>
              </div>
              <span>Local fallback is always last</span>
            </div>
            <div className="ai-provider-center__routes">
              {(configuration.featureRoutes ?? []).map((route) => (
                <article key={route.feature}>
                  <strong>{route.feature.replaceAll('_', ' ')}</strong>
                  <span>{route.providers.map((provider) => PROVIDER_LABELS[provider] || provider).join(' → ')}</span>
                  <small>Duplicate requests: {route.duplicateRequests ? 'enabled' : 'blocked'}</small>
                </article>
              ))}
            </div>
          </section>

          <section className="ai-provider-center__locked" aria-label="Controlled configuration boundary">
            <div>
              <p className="ai-provider-center__eyebrow">Protected change controls</p>
              <h2>Provider keys are write-only</h2>
              <p>
                Administrators can add or replace Claude and OpenAI keys. Keys are encrypted before database
                storage, activate immediately, and are never returned by the API after submission.
              </p>
            </div>
            <ul>
              <li>API key values are never returned.</li>
              <li>No browser or repository secret storage is permitted.</li>
              <li>Only administrators with an active ProjectPulse session may replace keys.</li>
              <li>Every replacement creates sanitized audit evidence without the key value.</li>
            </ul>
          </section>
        </>
      ) : null}
    </div>
  );
}
