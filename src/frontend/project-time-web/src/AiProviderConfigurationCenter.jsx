import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { usSignalLogoDataUrl } from './assets/usSignalLogoData.js';
import CelarAiProviderBridgePanel from './CelarAiProviderBridgePanel.jsx';
import CelarAiCapabilityRoutingPanel from './CelarAiCapabilityRoutingPanel.jsx';
import './ai-provider-configuration-center.css';
import './projectpulse-module-standard.css';
import CelarAiAvailabilityCard from './CelarAiAvailabilityCard.jsx';
import AiProviderReadinessPanel from './ai/AiProviderReadinessPanel.jsx';

const PROVIDER_LABELS = {
  deepseek_v4: 'DeepSeek v4',
  claude: 'Claude',
  openai: 'OpenAI',
  gemini: 'Gemini',
  copilot_studio: 'Microsoft Copilot Studio',
  local_template: 'Governed local template',
};
const AUTOMATIC_HEALTH_POLL_MS = 2000;
const AUTOMATIC_HEALTH_POLL_LIMIT = 10;

function formatDate(value) {
  if (!value) return 'Not recorded';
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? 'Not recorded' : parsed.toLocaleString();
}

function statusClass(status) {
  if (['available', 'ready'].includes(status)) return 'healthy';
  if (status === 'checking') return 'checking';
  if (['disabled', 'not_configured'].includes(status)) return 'inactive';
  return 'degraded';
}

function statusLabel(status) {
  if (status === 'checking') return 'Checking automatically';
  return String(status || 'checking').replaceAll('_', ' ');
}

async function readJson(response) {
  const payload = await response.json().catch(() => ({}));
  if (!response.ok) {
    throw new Error(payload.message || 'AI provider configuration could not be loaded.');
  }
  return payload;
}

export function AiProviderModelSelector({ provider, onSaved, onNotice }) {
  const [model, setModel] = useState(provider.model);
  const [catalog, setCatalog] = useState(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');
  const [saving, setSaving] = useState(false);
  const requestSequence = useRef(0);
  const discoverable = provider.code === 'gemini';

  useEffect(() => { setModel(provider.model); }, [provider.model]);

  const loadCatalog = useCallback(async (refresh = false) => {
    const sequence = ++requestSequence.current;
    setLoading(true);
    setError('');
    try {
      const result = await readJson(await fetch(`/api/ai-configuration/providers/${encodeURIComponent(provider.code)}/models${refresh ? '?refresh=true' : ''}`, {
        credentials: 'include', cache: 'no-store',
      }));
      if (sequence === requestSequence.current) setCatalog(result);
    } catch (failure) {
      if (sequence === requestSequence.current) setError(failure instanceof Error ? failure.message : 'Available models could not be loaded.');
    } finally {
      if (sequence === requestSequence.current) setLoading(false);
    }
  }, [provider.code]);

  useEffect(() => {
    setCatalog(null);
    setError('');
    setLoading(false);
    if (discoverable && provider.configured) void loadCatalog();
    return () => { requestSequence.current += 1; };
  }, [discoverable, provider.configured, provider.secret?.version, loadCatalog]);

  const availableModels = discoverable
    ? catalog?.models ?? []
    : (provider.approvedModels || [provider.model]).map((id) => ({ id, displayName: id }));
  const selected = availableModels.find((item) => item.id === model);
  const options = availableModels.some((item) => item.id === provider.model)
    ? availableModels
    : [{ id: provider.model, displayName: catalog?.status === 'available' ? 'Current model; not listed in this catalog' : 'Current model' }, ...availableModels];
  const selectionUnavailable = model !== provider.model && !selected;
  const catalogUnverified = discoverable && (!catalog || error || catalog.status !== 'available');

  async function saveModel(event) {
    event.preventDefault();
    if (!model || model === provider.model || selectionUnavailable || catalogUnverified) return;
    setSaving(true);
    onNotice('');
    try {
      const result = await readJson(await fetch(`/api/ai-configuration/providers/${provider.code}/model`, {
        method: 'PUT', credentials: 'include', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ model }),
      }));
      onNotice(result.message || 'Model saved and tested.');
      await onSaved();
    } catch (failure) {
      onNotice(failure instanceof Error ? failure.message : 'The model could not be saved and tested.');
    } finally { setSaving(false); }
  }

  return (
    <form className="ai-provider-center__model-form" onSubmit={saveModel}>
      <label htmlFor={`provider-model-${provider.code}`}>Active model</label>
      <div>
        <select
          id={`provider-model-${provider.code}`}
          value={model}
          onChange={(event) => setModel(event.target.value)}
          disabled={!provider.configured || saving || loading}
        >
          {selectionUnavailable ? <option value={model}>{model} (no longer listed)</option> : null}
          {options.map((item) => <option value={item.id} key={item.id}>{item.displayName && item.displayName !== item.id ? `${item.displayName} — ${item.id}` : item.id}</option>)}
        </select>
        <button type="submit" disabled={!provider.configured || saving || loading || model === provider.model || selectionUnavailable || Boolean(catalogUnverified)}>
          {saving ? 'Testing…' : 'Save and test'}
        </button>
      </div>
      <small>{provider.configured ? 'The new model activates only after the saved key verifies it.' : 'Save an API key before changing the model.'}</small>
      {discoverable ? (
        <section className="ai-provider-center__model-catalog" aria-label={`${provider.displayName || 'Gemini'} model availability`} aria-busy={loading}>
          <div className="ai-provider-center__catalog-heading">
            <strong>Models available to your account</strong>
            <button type="button" onClick={() => loadCatalog(true)} disabled={!provider.configured || loading || saving}>
              {loading ? 'Loading models…' : 'Refresh models'}
            </button>
          </div>
          <p>Loaded using the saved server credential. Refreshing keeps the active model unchanged; choose a model, then save and test.</p>
          {error ? <p className="ai-provider-center__catalog-error" role="alert">{error} The active model is unchanged.</p> : null}
          {catalog ? <p role="status">{catalog.message || `${catalog.models?.length ?? 0} compatible models returned.`}{catalog.diagnostic ? ` Diagnostic: ${catalog.diagnostic}.` : ''} Checked {formatDate(catalog.checkedAt)}.</p> : null}
          {selectionUnavailable ? <p className="ai-provider-center__catalog-error" role="alert">The selected model is no longer listed. Choose another available model before saving.</p> : null}
          <p>{selected?.costGuidance || 'The model catalog does not provide prices. Compare current provider pricing before choosing a model.'}</p>
          <a href="https://ai.google.dev/gemini-api/docs/pricing" target="_blank" rel="noreferrer">Compare official Gemini pricing</a>
          {(selected?.inputTokenLimit || selected?.outputTokenLimit) ? <small>Input limit: {selected.inputTokenLimit?.toLocaleString() || 'Not reported'} tokens · Output limit: {selected.outputTokenLimit?.toLocaleString() || 'Not reported'} tokens</small> : null}
          <small>Model availability does not verify remaining quota or guarantee generation speed.</small>
        </section>
      ) : null}
    </form>
  );
}

export function GeminiHealthNotice({ health }) {
  const quotaFailure = (code) => String(code || '').replace(/^module025_external_/, '').startsWith('gemini_http_429');
  const probeLimited = health.probeStatus !== 'available' && quotaFailure(health.lastProbeFailureCode);
  const generationLimited = health.status !== 'available' && quotaFailure(health.lastFailureCode);
  if (!probeLimited && !generationLimited) return null;
  const retryAt = health.retryAfterUtc || health.circuitOpenUntil;
  return (
    <aside className="ai-provider-center__health-explanation" aria-label="Gemini quota or rate limit">
      <strong>Google is limiting requests (HTTP 429)</strong>
      <p>{(probeLimited ? health.lastProbeFailureMessage : health.lastFailureMessage) || 'This can mean a request or token rate limit, an exhausted quota, or no available quota for this project and model. The response does not identify which limit was reached.'}</p>
      {probeLimited ? <p>{health.probeFailureCount ?? 0} failed readiness probes are separate from SOW generation attempts. Generation failures: {health.failureCount ?? 0}.</p> : null}
      <p>{retryAt ? `Routing pauses until at least ${formatDate(retryAt)}. ` : ''}Check the Google project&apos;s model quota and billing status. Repeated health refreshes do not restore exhausted quota.</p>
      <a href="https://ai.google.dev/gemini-api/docs/rate-limits" target="_blank" rel="noreferrer">Review Gemini quota and rate limits</a>
    </aside>
  );
}

export default function AiProviderConfigurationCenter() {
  const [state, setState] = useState({ loading: true, error: '', payload: null });
  const [refreshing, setRefreshing] = useState(false);
  const [notice, setNotice] = useState('');
  const [keys, setKeys] = useState({ deepseek_v4: '', claude: '', openai: '' });
  const [savingProvider, setSavingProvider] = useState('');
  const [changingState, setChangingState] = useState('');

  const load = useCallback(async ({ quiet = false } = {}) => {
    if (!quiet) setState((current) => ({ ...current, loading: true, error: '' }));
    try {
      const payload = await readJson(await fetch('/api/ai-configuration', {
        credentials: 'include',
        cache: 'no-store',
      }));
      setState({ loading: false, error: '', payload });
    } catch (error) {
      setState((current) => ({
        loading: false,
        error: error instanceof Error ? error.message : 'AI provider configuration could not be loaded.',
        payload: quiet ? current.payload : null,
      }));
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const configuration = state.payload?.configuration;
  const governance = state.payload?.governance;
  const providers = configuration?.providers ?? [];
  const healthByProvider = useMemo(
    () => new Map((state.payload?.health ?? []).map((item) => [item.provider, item])),
    [state.payload],
  );
  const pendingProviderKey = useMemo(() => providers
    .filter((provider) => provider.code !== 'local_template' && provider.enabled && provider.configured)
    .filter((provider) => ['checking', 'not_checked'].includes(healthByProvider.get(provider.code)?.probeStatus))
    .map((provider) => provider.code)
    .sort()
    .join(','), [providers, healthByProvider]);

  useEffect(() => {
    if (!pendingProviderKey) return undefined;
    let cancelled = false;
    let attempts = 0;
    let timer;

    const poll = async () => {
      attempts += 1;
      try {
        const result = await readJson(await fetch('/api/ai-configuration/health', {
          credentials: 'include',
          cache: 'no-store',
        }));
        if (cancelled) return;
        setState((current) => current.payload ? {
          ...current,
          payload: { ...current.payload, health: result.providers ?? current.payload.health },
        } : current);
        const stillChecking = (result.providers ?? []).some((provider) =>
          provider.enabled
          && provider.configured
          && ['checking', 'not_checked'].includes(provider.probeStatus));
        if (stillChecking && attempts < AUTOMATIC_HEALTH_POLL_LIMIT) {
          timer = window.setTimeout(poll, AUTOMATIC_HEALTH_POLL_MS);
        }
      } catch {
        if (!cancelled && attempts < AUTOMATIC_HEALTH_POLL_LIMIT) {
          timer = window.setTimeout(poll, AUTOMATIC_HEALTH_POLL_MS);
        }
      }
    };

    timer = window.setTimeout(poll, AUTOMATIC_HEALTH_POLL_MS);
    return () => {
      cancelled = true;
      if (timer) window.clearTimeout(timer);
    };
  }, [pendingProviderKey]);

  async function refreshHealth() {
    setRefreshing(true);
    setNotice('');
    try {
      const result = await readJson(await fetch('/api/ai-configuration/health/refresh', {
        method: 'POST',
        credentials: 'include',
      }));
      setNotice(result.message || 'Provider health checks completed.');
      await load({ quiet: true });
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
        credentials: 'include',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ apiKey }),
      }));
      setKeys((current) => ({ ...current, [providerCode]: '' }));
      setNotice(result.message || 'API key saved securely and checked automatically.');
      await load({ quiet: true });
    } catch (error) {
      setNotice(error instanceof Error ? error.message : 'The API key could not be saved.');
    } finally {
      setSavingProvider('');
    }
  }

  async function setProviderEnabled(providerCode, enabled) {
    setChangingState(providerCode);
    setNotice('');
    try {
      const result = await readJson(await fetch(`/api/ai-configuration/providers/${providerCode}/enabled`, {
        method: 'PUT',
        credentials: 'include',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ enabled }),
      }));
      setNotice(result.message || `Provider ${enabled ? 'enabled' : 'disabled'}.`);
      await load({ quiet: true });
    } catch (error) {
      setNotice(error instanceof Error ? error.message : 'The provider state could not be changed.');
    } finally {
      setChangingState('');
    }
  }

  return (
    <div className="ai-provider-center projectpulse-module-standard" data-module="064" data-brand="us-signal">
      <header className="ai-provider-center__header">
        <img className="projectpulse-module-standard__logo" src={usSignalLogoDataUrl} alt="US Signal" />
        <div>
          <p className="ai-provider-center__eyebrow">Module 064 · governed shared service</p>
          <h1>AI Provider Configuration Center</h1>
          <p>
            Celar AI uses Module 064 as the governed provider gateway. Module 064 checks provider health automatically,
            controls active models and each capability&apos;s saved provider order. Execution policies and blockers are shown
            alongside each route. Private content stays protected, and a safety refusal never triggers another provider.
          </p>
        </div>
        <button type="button" onClick={refreshHealth} disabled={refreshing || state.loading}>
          {refreshing ? 'Checking providers…' : 'Refresh provider health'}
        </button>
      </header>

      {/* GROUP_7_MODULE_064_READINESS_PANEL_START */}
      <AiProviderReadinessPanel />
      {/* GROUP_7_MODULE_064_READINESS_PANEL_END */}
      <div className="ai-provider-center__automatic-health" role="status">
        <strong>Automatic provider health is active.</strong>
        <span>
          Configured providers are checked when the API starts, after configuration changes, and every {configuration?.execution?.healthIntervalSeconds ?? 120} seconds. Checks respect provider retry windows; the button requests a recheck when the cooldown allows.
        </span>
      </div>
      {governance ? (
        <div
          className={`ai-provider-center__execution-policy ${governance.sanitizedExternalExecutionEnabled && governance.enterpriseSanitizedExternalFallbackEnabled ? 'is-enabled' : 'is-disabled'}`}
          role="status"
        >
          <strong>Routed generation policy: {governance.sanitizedExternalExecutionEnabled && governance.enterpriseSanitizedExternalFallbackEnabled ? 'Enabled' : 'Action required'}</strong>
          <span>
            {!governance.sanitizedExternalExecutionEnabled
              ? 'Provider probes can succeed while generation remains blocked. Set PROJECTPULSE_AI_ALLOW_SANITIZED_EXTERNAL_ESCALATION=true on the API runtime to allow eligible, deidentified external requests.'
              : !governance.enterpriseSanitizedExternalFallbackEnabled
                ? 'Timesheet generic fallback is enabled, but enterprise AI consumers remain blocked. Set PROJECTPULSE_CELAR_AI_SANITIZED_EXTERNAL_FALLBACK_ENABLED=true on the API runtime.'
                : 'Eligible external requests are enabled. Each capability below shows its effective provider order and any additional approval requirements. Private SOW and GSD text remains inside the private boundary.'}
          </span>
        </div>
      ) : null}

      {notice ? <div className="ai-provider-center__notice" role="status">{notice}</div> : null}
      {state.loading ? <div className="ai-provider-center__state">Loading shared AI configuration and checking provider readiness…</div> : null}
      {state.error ? (
        <div className="ai-provider-center__state ai-provider-center__state--error" role="alert">
          <p>{state.error}</p>
          <button type="button" onClick={() => load()}>Try again</button>
        </div>
      ) : null}

      {configuration ? (
        <>
          <section className="ai-provider-center__summary" aria-label="Shared routing summary">
            <article><span>Routing mode</span><strong>{configuration.mode?.replaceAll('_', ' ')}</strong></article>
            <article><span>Execution</span><strong>Sequential, no duplicate calls</strong></article>
            <article><span>Timeout / retries</span><strong>{configuration.execution?.requestTimeoutSeconds}s / {configuration.execution?.retryCount}</strong></article>
            <article><span>Output limit</span><strong>{configuration.execution?.maxOutputTokens} tokens</strong></article>
          </section>

          <CelarAiAvailabilityCard />

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
                      <span className={`ai-provider-center__status ai-provider-center__status--${statusClass(health.probeStatus)}`}>
                        {statusLabel(health.probeStatus)}
                      </span>
                    </div>
                    {provider.code === 'gemini' ? <GeminiHealthNotice health={health} /> : null}
                    <dl>
                      <div><dt>Enabled</dt><dd>{provider.enabled ? 'Yes' : 'No'}</dd></div>
                      <div><dt>Configured</dt><dd>{provider.configured ? 'Yes' : 'No'}</dd></div>
                      <div><dt>Endpoint</dt><dd>{provider.endpoint || 'Local only'}</dd></div>
                      <div><dt>API version</dt><dd>{provider.apiVersion || 'Not applicable'}</dd></div>
                      <div><dt>Generation route status</dt><dd>{statusLabel(health.status)}</dd></div>
                      <div><dt>Last check</dt><dd>{formatDate(health.lastCheckedAt)}</dd></div>
                      <div><dt>Last success</dt><dd>{formatDate(health.lastSuccessAt)}</dd></div>
                      <div><dt>Generations succeeded</dt><dd>{health.successCount ?? 0}</dd></div>
                      <div><dt>Generation failures / refusals</dt><dd>{health.failureCount ?? 0} / {health.refusalCount ?? 0}</dd></div>
                      <div><dt>Last generation failure</dt><dd>{health.lastFailureCode ?? 'None'}</dd></div>
                      <div><dt>Last generation request</dt><dd>{health.lastRequestId ?? 'Not reported'}</dd></div>
                      <div><dt>Probe status</dt><dd>{statusLabel(health.probeStatus)}</dd></div>
                      <div><dt>Last probe</dt><dd>{formatDate(health.lastProbeAt)}</dd></div>
                      <div><dt>Probe successes / failures</dt><dd>{health.probeSuccessCount ?? 0} / {health.probeFailureCount ?? 0}</dd></div>
                      <div><dt>Last probe failure</dt><dd>{health.lastProbeFailureCode ?? 'None'}</dd></div>
                      <div><dt>Last probe request</dt><dd>{health.lastProbeRequestId ?? 'Not reported'}</dd></div>
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
                          <div><strong>Provider routing</strong><small>Each provider is independent. Gemini and Copilot can both be enabled. Disabling preserves the saved credential and model.</small>{!provider.configured && <small>Save the credential below to unlock Enable.</small>}</div>
                          <button
                            type="button"
                            className={provider.enabled ? 'ai-provider-center__danger-button' : ''}
                            aria-label={`${provider.enabled ? 'Disable' : 'Enable'} ${provider.displayName || PROVIDER_LABELS[provider.code]}`}
                            onClick={() => setProviderEnabled(provider.code, !provider.enabled)}
                            disabled={changingState === provider.code || (!provider.configured && !provider.enabled)}
                          >
                            {changingState === provider.code ? 'Updating…' : provider.enabled ? 'Disable' : 'Enable'}
                          </button>
                        </div>
                        <AiProviderModelSelector provider={provider} onNotice={setNotice} onSaved={() => load({ quiet: true })} />
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
                          <label htmlFor={`provider-key-${provider.code}`}>{provider.code === 'copilot_studio' ? 'Copilot Studio Direct Line secret' : provider.configured ? 'Replace API key' : 'Add API key'}</label>
                          <div>
                            <input
                              id={`provider-key-${provider.code}`}
                              type="password"
                              value={keys[provider.code] || ''}
                              onChange={(event) => setKeys((current) => ({ ...current, [provider.code]: event.target.value }))}
                              autoComplete="new-password"
                              placeholder="Paste key once"
                              disabled={savingProvider === provider.code}
                            />
                            <button type="submit" disabled={!keys[provider.code]?.trim() || savingProvider === provider.code}>
                              {savingProvider === provider.code ? 'Saving and checking…' : 'Save securely'}
                            </button>
                          </div>
                          <small>The key is write-only, disappears after saving, and is health-checked automatically.</small>
                        </form>
                      </div>
                    ) : null}
                  </article>
                );
              })}
            </div>
          </section>

          <CelarAiProviderBridgePanel />
          <CelarAiCapabilityRoutingPanel />

          <section className="ai-provider-center__locked" aria-label="Controlled configuration boundary">
            <div>
              <p className="ai-provider-center__eyebrow">Protected change controls</p>
              <h2>Provider keys are write-only</h2>
              <p>
                Administrators can add or replace provider keys. Keys are encrypted before database
                storage, tested automatically, and never returned by the API after submission.
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
