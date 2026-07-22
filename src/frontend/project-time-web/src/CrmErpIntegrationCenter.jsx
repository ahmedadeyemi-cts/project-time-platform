import { useCallback, useEffect, useMemo, useState } from 'react';
import { usSignalLogoDataUrl } from './assets/usSignalLogoData.js';
import './crm-erp-integration-center.css';
import './projectpulse-module-standard.css';

const EMPTY_PROVIDER = {
  providerKey: '',
  providerName: '',
  providerType: 'crm',
  authModel: 'oauth2',
  baseUrl: '',
  healthCheckUrl: '',
  oauthAuthorizationUrl: '',
  oauthTokenUrl: '',
  oauthClientId: '',
  oauthScopes: '',
  apiKeyHeader: 'Authorization',
  apiKeyPrefix: 'Bearer',
  isEnabled: false,
  notes: '',
};

function words(value) {
  return String(value || 'not configured')
    .replaceAll('_', ' ')
    .replace(/\b\w/g, (letter) => letter.toUpperCase());
}

function formatDate(value) {
  if (!value) return 'Never';
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? 'Never' : date.toLocaleString();
}

function statusTone(status) {
  if (status === 'available') return 'available';
  if (status === 'authentication_failed') return 'authentication';
  if (status === 'unavailable') return 'unavailable';
  if (status === 'disabled') return 'disabled';
  return 'pending';
}

async function jsonRequest(url, options = {}) {
  const response = await fetch(url, {
    credentials: 'include',
    ...options,
    headers: {
      ...(options.body ? { 'Content-Type': 'application/json' } : {}),
      ...(options.headers || {}),
    },
  });
  const payload = await response.json().catch(() => ({}));
  if (!response.ok) throw new Error(payload.message || `Module 026 returned HTTP ${response.status}.`);
  return payload;
}

function providerPayload(provider) {
  return {
    providerKey: provider.providerKey,
    providerName: provider.providerName,
    providerType: provider.providerType,
    authModel: provider.authModel,
    baseUrl: provider.baseUrl,
    healthCheckUrl: provider.healthCheckUrl,
    oauthAuthorizationUrl: provider.oauthAuthorizationUrl,
    oauthTokenUrl: provider.oauthTokenUrl,
    oauthClientId: provider.oauthClientId,
    oauthScopes: provider.oauthScopes,
    apiKeyHeader: provider.apiKeyHeader,
    apiKeyPrefix: provider.apiKeyPrefix,
    isEnabled: Boolean(provider.isEnabled),
    notes: provider.notes,
  };
}

export default function CrmErpIntegrationCenter() {
  const [state, setState] = useState({ loading: true, error: '', payload: null });
  const [selectedKey, setSelectedKey] = useState('');
  const [draft, setDraft] = useState(null);
  const [credential, setCredential] = useState('');
  const [newProvider, setNewProvider] = useState(EMPTY_PROVIDER);
  const [showAdd, setShowAdd] = useState(false);
  const [busy, setBusy] = useState('');
  const [notice, setNotice] = useState({ tone: '', message: '' });

  const load = useCallback(async () => {
    setState((current) => ({ ...current, loading: true, error: '' }));
    try {
      const payload = await jsonRequest('/api/integrations/026/providers');
      setState({ loading: false, error: '', payload });
      setSelectedKey((current) => current || payload.providers?.[0]?.providerKey || '');
    } catch (error) {
      setState({ loading: false, error: error?.message || 'Module 026 is unavailable.', payload: null });
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  useEffect(() => {
    const refreshOnFocus = () => void load();
    window.addEventListener('focus', refreshOnFocus);
    return () => window.removeEventListener('focus', refreshOnFocus);
  }, [load]);

  const providers = state.payload?.providers ?? [];
  const selected = useMemo(
    () => providers.find((provider) => provider.providerKey === selectedKey) ?? null,
    [providers, selectedKey],
  );

  useEffect(() => {
    setDraft(selected ? { ...selected } : null);
    setCredential('');
  }, [selected]);

  const canManage = Boolean(state.payload?.access?.canManage);
  const availableCount = providers.filter((provider) => provider.availabilityStatus === 'available').length;
  const configuredCount = providers.filter((provider) => provider.credentialConfigured || provider.oauthConnected).length;

  function updateDraft(field, value) {
    setDraft((current) => ({ ...current, [field]: value }));
  }

  async function saveConfiguration(event) {
    event.preventDefault();
    if (!draft) return;
    setBusy(`save:${draft.providerKey}`);
    setNotice({ tone: '', message: '' });
    try {
      const result = await jsonRequest(`/api/integrations/026/providers/${draft.providerKey}`, {
        method: 'PUT',
        body: JSON.stringify(providerPayload(draft)),
      });
      setNotice({ tone: 'success', message: result.message });
      await load();
    } catch (error) {
      setNotice({ tone: 'error', message: error?.message || 'Configuration could not be saved.' });
    } finally {
      setBusy('');
    }
  }

  async function saveCredential(event) {
    event.preventDefault();
    if (!draft || !credential.trim()) return;
    setBusy(`credential:${draft.providerKey}`);
    setNotice({ tone: '', message: '' });
    try {
      const result = await jsonRequest(`/api/integrations/026/providers/${draft.providerKey}/credential`, {
        method: 'PUT',
        body: JSON.stringify({ secret: credential.trim() }),
      });
      setCredential('');
      setNotice({ tone: 'success', message: result.message });
      await load();
    } catch (error) {
      setNotice({ tone: 'error', message: error?.message || 'Credential could not be saved.' });
    } finally {
      setBusy('');
    }
  }

  async function testConnection(providerKey) {
    setBusy(`test:${providerKey}`);
    setNotice({ tone: '', message: '' });
    try {
      const result = await jsonRequest(`/api/integrations/026/providers/${providerKey}/test`, { method: 'POST' });
      setNotice({
        tone: result.availabilityStatus === 'available' ? 'success' : 'warning',
        message: `${words(result.availabilityStatus)} · ${result.durationMs} ms${result.statusCode ? ` · HTTP ${result.statusCode}` : ''}`,
      });
      await load();
    } catch (error) {
      setNotice({ tone: 'error', message: error?.message || 'Connection test could not run.' });
    } finally {
      setBusy('');
    }
  }

  async function connectOAuth(providerKey) {
    setBusy(`oauth:${providerKey}`);
    setNotice({ tone: '', message: '' });
    try {
      const result = await jsonRequest(`/api/integrations/026/providers/${providerKey}/oauth/start`, { method: 'POST' });
      const popup = window.open(result.authorizationUrl, `projectpulse-oauth-${providerKey}`, 'popup,width=720,height=800');
      if (!popup) window.location.assign(result.authorizationUrl);
      setNotice({ tone: 'warning', message: 'Complete provider consent in the new window, then return here and refresh status.' });
    } catch (error) {
      setNotice({ tone: 'error', message: error?.message || 'OAuth connection could not start.' });
    } finally {
      setBusy('');
    }
  }

  async function addProvider(event) {
    event.preventDefault();
    setBusy('add');
    setNotice({ tone: '', message: '' });
    try {
      const result = await jsonRequest('/api/integrations/026/providers', {
        method: 'POST',
        body: JSON.stringify(providerPayload(newProvider)),
      });
      setNewProvider(EMPTY_PROVIDER);
      setShowAdd(false);
      setSelectedKey(result.providerKey);
      setNotice({ tone: 'success', message: result.message });
      await load();
    } catch (error) {
      setNotice({ tone: 'error', message: error?.message || 'Provider could not be added.' });
    } finally {
      setBusy('');
    }
  }

  return (
    <section className="crm-erp-center projectpulse-module-standard" data-module="026" data-brand="us-signal">
      <header className="crm-erp-hero">
        <div className="crm-erp-brand">
          <img src={usSignalLogoDataUrl} alt="US Signal" />
          <div>
            <p>Module 026 · CRM/ERP integrations</p>
            <h1>Integration Control Center</h1>
            <span>Connect SELL, Salesforce, Certinia, ServiceNow, and approved custom platforms, then see whether each service is available.</span>
          </div>
        </div>
        <div className="crm-erp-hero-actions">
          <button type="button" className="secondary-action" onClick={load} disabled={state.loading}>Refresh status</button>
          {canManage ? <button type="button" className="primary-action" onClick={() => setShowAdd((current) => !current)}>Add platform</button> : null}
        </div>
      </header>

      <div className="crm-erp-security-banner">
        <strong>Secure connection boundary</strong>
        <span>OAuth tokens and API keys are encrypted server-side, write-only, never shown after saving, and excluded from availability logs and audit evidence.</span>
      </div>

      {state.error ? <div className="crm-erp-notice error" role="alert">{state.error}</div> : null}
      {notice.message ? <div className={`crm-erp-notice ${notice.tone}`} role="status">{notice.message}</div> : null}

      <div className="crm-erp-summary">
        <article><span>Registered platforms</span><strong>{providers.length}</strong><small>Built-in and manually added</small></article>
        <article><span>Configured</span><strong>{configuredCount}</strong><small>Credential metadata only</small></article>
        <article><span>Available</span><strong>{availableCount}</strong><small>Latest explicit connection test</small></article>
        <article><span>Your access</span><strong>{canManage ? 'Configure' : 'View status'}</strong><small>{state.payload?.access?.isViewAs ? 'View-As is read-only' : 'Actual ProjectPulse session'}</small></article>
      </div>

      {showAdd && canManage ? (
        <form className="crm-erp-add-panel" onSubmit={addProvider}>
          <div><p>Manual CRM/ERP registration</p><h2>Add another platform</h2></div>
          <div className="crm-erp-form-grid">
            <label>Provider key<input required value={newProvider.providerKey} placeholder="example_erp" onChange={(event) => setNewProvider((current) => ({ ...current, providerKey: event.target.value }))} /></label>
            <label>Display name<input required value={newProvider.providerName} placeholder="Example ERP" onChange={(event) => setNewProvider((current) => ({ ...current, providerName: event.target.value }))} /></label>
            <label>Platform type<select value={newProvider.providerType} onChange={(event) => setNewProvider((current) => ({ ...current, providerType: event.target.value }))}><option value="crm">CRM</option><option value="erp">ERP</option><option value="erp_psa">ERP / PSA</option><option value="itsm_erp">ITSM / ERP</option><option value="other">Other</option></select></label>
            <label>Authentication<select value={newProvider.authModel} onChange={(event) => setNewProvider((current) => ({ ...current, authModel: event.target.value }))}><option value="oauth2">OAuth 2.0</option><option value="api_key">API key</option></select></label>
          </div>
          <div className="crm-erp-actions"><button type="submit" className="primary-action" disabled={busy === 'add'}>{busy === 'add' ? 'Adding…' : 'Add platform'}</button><button type="button" className="secondary-action" onClick={() => setShowAdd(false)}>Cancel</button></div>
        </form>
      ) : null}

      <div className="crm-erp-layout">
        <nav className="crm-erp-provider-list" aria-label="Integration providers">
          {state.loading && !providers.length ? <p>Loading integrations…</p> : null}
          {providers.map((provider) => (
            <button type="button" key={provider.providerKey} className={selectedKey === provider.providerKey ? 'active' : ''} onClick={() => setSelectedKey(provider.providerKey)}>
              <div><strong>{provider.providerName}</strong><small>{words(provider.providerType)} · {provider.authModel === 'oauth2' ? 'OAuth 2.0' : 'API key'}</small></div>
              <span className={`crm-erp-status ${statusTone(provider.availabilityStatus)}`}>{words(provider.availabilityStatus)}</span>
            </button>
          ))}
        </nav>

        {draft ? (
          <main className="crm-erp-detail">
            <section className="crm-erp-detail-heading">
              <div><p>{draft.isBuiltin ? 'Built-in platform' : 'Custom platform'}</p><h2>{draft.providerName}</h2><span>Last checked {formatDate(draft.lastCheckedAt)}</span></div>
              <span className={`crm-erp-status large ${statusTone(draft.availabilityStatus)}`}>{words(draft.availabilityStatus)}</span>
            </section>

            <div className="crm-erp-detail-metrics">
              <article><span>Enabled</span><strong>{draft.isEnabled ? 'Yes' : 'No'}</strong></article>
              <article><span>Credential</span><strong>{draft.credentialConfigured ? 'Saved' : 'Missing'}</strong></article>
              <article><span>OAuth consent</span><strong>{draft.authModel === 'oauth2' ? (draft.oauthConnected ? 'Connected' : 'Pending') : 'Not used'}</strong></article>
              <article><span>Last HTTP status</span><strong>{draft.lastStatusCode || '—'}</strong></article>
            </div>

            {canManage ? (
              <>
                <form className="crm-erp-configuration" onSubmit={saveConfiguration}>
                  <div className="crm-erp-section-heading"><div><p>Non-secret settings</p><h3>Connection configuration</h3></div><label className="crm-erp-toggle"><input type="checkbox" checked={Boolean(draft.isEnabled)} onChange={(event) => updateDraft('isEnabled', event.target.checked)} /> Enabled</label></div>
                  <div className="crm-erp-form-grid">
                    <label>Display name<input value={draft.providerName} onChange={(event) => updateDraft('providerName', event.target.value)} /></label>
                    <label>Platform type<select value={draft.providerType} onChange={(event) => updateDraft('providerType', event.target.value)}><option value="crm">CRM</option><option value="erp">ERP</option><option value="erp_psa">ERP / PSA</option><option value="itsm_erp">ITSM / ERP</option><option value="other">Other</option></select></label>
                    <label>Authentication<select value={draft.authModel} onChange={(event) => updateDraft('authModel', event.target.value)}><option value="oauth2">OAuth 2.0</option><option value="api_key">API key</option></select></label>
                    <label>Base URL<input type="url" placeholder="https://provider.example.com" value={draft.baseUrl} onChange={(event) => updateDraft('baseUrl', event.target.value)} /></label>
                    <label className="wide">Availability / health URL<input type="url" placeholder="https://provider.example.com/api/status" value={draft.healthCheckUrl} onChange={(event) => updateDraft('healthCheckUrl', event.target.value)} /></label>
                    {draft.authModel === 'oauth2' ? (
                      <>
                        <label className="wide">OAuth authorization URL<input type="url" value={draft.oauthAuthorizationUrl} onChange={(event) => updateDraft('oauthAuthorizationUrl', event.target.value)} /></label>
                        <label className="wide">OAuth token URL<input type="url" value={draft.oauthTokenUrl} onChange={(event) => updateDraft('oauthTokenUrl', event.target.value)} /></label>
                        <label>OAuth client ID<input value={draft.oauthClientId} onChange={(event) => updateDraft('oauthClientId', event.target.value)} /></label>
                        <label>OAuth scopes<input value={draft.oauthScopes} placeholder="api refresh_token" onChange={(event) => updateDraft('oauthScopes', event.target.value)} /></label>
                      </>
                    ) : (
                      <>
                        <label>API-key header<input value={draft.apiKeyHeader} onChange={(event) => updateDraft('apiKeyHeader', event.target.value)} /></label>
                        <label>Value prefix<input value={draft.apiKeyPrefix} placeholder="Bearer" onChange={(event) => updateDraft('apiKeyPrefix', event.target.value)} /></label>
                      </>
                    )}
                    <label className="wide">Notes<textarea value={draft.notes} onChange={(event) => updateDraft('notes', event.target.value)} /></label>
                  </div>
                  <button type="submit" className="primary-action" disabled={busy === `save:${draft.providerKey}`}>{busy === `save:${draft.providerKey}` ? 'Saving…' : 'Save configuration'}</button>
                </form>

                <form className="crm-erp-credential" onSubmit={saveCredential}>
                  <div><p>Write-only credential</p><h3>{draft.authModel === 'oauth2' ? 'OAuth client secret' : 'API key'}</h3><span>The saved value cannot be viewed later and is never returned by the API.</span></div>
                  <label><span className="sr-only">Write-only credential</span><input type="password" autoComplete="new-password" value={credential} placeholder={draft.credentialConfigured ? 'Replace saved credential' : 'Enter credential'} onChange={(event) => setCredential(event.target.value)} /></label>
                  <button type="submit" className="secondary-action" disabled={!credential.trim() || busy === `credential:${draft.providerKey}`}>{busy === `credential:${draft.providerKey}` ? 'Encrypting…' : 'Save credential'}</button>
                </form>

                <div className="crm-erp-actions-panel">
                  <div><p>Connection lifecycle</p><h3>Connect and verify</h3><span>Tests contact only the configured public HTTPS availability endpoint and store sanitized results.</span></div>
                  <div className="crm-erp-actions">
                    {draft.authModel === 'oauth2' ? <button type="button" className="secondary-action" onClick={() => connectOAuth(draft.providerKey)} disabled={busy === `oauth:${draft.providerKey}`}>{busy === `oauth:${draft.providerKey}` ? 'Preparing…' : draft.oauthConnected ? 'Reconnect OAuth' : 'Connect with OAuth'}</button> : null}
                    <button type="button" className="primary-action" onClick={() => testConnection(draft.providerKey)} disabled={busy === `test:${draft.providerKey}`}>{busy === `test:${draft.providerKey}` ? 'Testing…' : 'Test availability'}</button>
                  </div>
                </div>
              </>
            ) : (
              <div className="crm-erp-readonly"><strong>Read-only status access</strong><p>An Integration Administrator or Administrator must configure credentials, OAuth consent, and connection tests.</p></div>
            )}
          </main>
        ) : <main className="crm-erp-detail"><p>Select an integration provider.</p></main>}
      </div>
    </section>
  );
}
