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
  recordLookupUrlTemplate: '',
  importMappingJson: '{}',
  isEnabled: false,
  notes: '',
};

const SELL_MAPPING = JSON.stringify({
  projectNamePath: 'data.name',
  quoteNumberPath: 'data.id',
  customerNamePath: 'data.organization_name',
  contractedAmountPath: 'data.value',
  rateLinesPath: 'data.custom_fields.pricing_rate_review',
  rateCodePath: 'sku',
  descriptionPath: 'description',
  unitRatePath: 'unit_rate',
  laborCategoryPath: 'labor_category',
  timeTypePath: 'time_type',
  unitTypePath: 'unit_type',
  billablePath: 'billable',
}, null, 2);

const PROVIDER_TEMPLATES = Object.freeze({
  zendesk_sell: {
    providerKey: 'zendesk_sell',
    providerName: 'SELL (Zendesk Sell)',
    shortName: 'SELL',
    providerType: 'crm',
    recommendedAuth: 'api_key',
    authModel: 'api_key',
    baseUrl: 'https://api.getbase.com',
    healthCheckUrl: 'https://api.getbase.com/v2/contacts?per_page=1',
    oauthAuthorizationUrl: 'https://api.getbase.com/oauth2/authorize',
    oauthTokenUrl: 'https://api.getbase.com/oauth2/token',
    oauthScopes: 'read profile',
    apiKeyHeader: 'Authorization',
    apiKeyPrefix: 'Bearer',
    recordLookupUrlTemplate: 'https://api.getbase.com/v2/deals/{recordId}',
    importMappingJson: SELL_MAPPING,
    description: 'Authoritative customer, organization, deal, quote, and pricing source for ProjectPulse.',
    consumes: ['Module 021 customer sync', 'Module 055D work intake', 'Customer and opportunity handoff'],
    setup: [
      'Choose API key for a governed single-user access token or OAuth 2.0 for delegated consent.',
      'Save the non-secret URLs and scopes first.',
      'Save the write-only token or OAuth client secret.',
      'Enable and test the connection before Module 021 pulls customers.',
    ],
  },
  salesforce: {
    providerKey: 'salesforce',
    providerName: 'Salesforce',
    shortName: 'Salesforce',
    providerType: 'crm',
    recommendedAuth: 'oauth2',
    authModel: 'oauth2',
    baseUrl: 'https://login.salesforce.com',
    healthCheckUrl: 'https://login.salesforce.com/services/oauth2/userinfo',
    oauthAuthorizationUrl: 'https://login.salesforce.com/services/oauth2/authorize',
    oauthTokenUrl: 'https://login.salesforce.com/services/oauth2/token',
    oauthScopes: 'api refresh_token',
    apiKeyHeader: 'Authorization',
    apiKeyPrefix: 'Bearer',
    recordLookupUrlTemplate: '',
    importMappingJson: '{}',
    description: 'CRM account, contact, opportunity, and pipeline integration using a Salesforce Connected App.',
    consumes: ['Account and contact reference', 'Opportunity handoff', 'Future bidirectional workflow'],
    setup: [
      'Create or select a Salesforce Connected App.',
      'Enter the client ID, authorization URL, token URL, and approved OAuth scopes.',
      'Save the write-only client secret and complete OAuth consent.',
      'Replace the login host with the approved My Domain host when required by policy.',
    ],
  },
  certinia: {
    providerKey: 'certinia',
    providerName: 'Certinia',
    shortName: 'Certinia',
    providerType: 'erp_psa',
    recommendedAuth: 'oauth2',
    authModel: 'oauth2',
    baseUrl: 'https://login.salesforce.com',
    healthCheckUrl: 'https://login.salesforce.com/services/oauth2/userinfo',
    oauthAuthorizationUrl: 'https://login.salesforce.com/services/oauth2/authorize',
    oauthTokenUrl: 'https://login.salesforce.com/services/oauth2/token',
    oauthScopes: 'api refresh_token',
    apiKeyHeader: 'Authorization',
    apiKeyPrefix: 'Bearer',
    recordLookupUrlTemplate: '',
    importMappingJson: '{}',
    description: 'ERP/PSA project, billing, resource, and financial integration through the Salesforce platform.',
    consumes: ['Project and resource reference', 'Billing and financial handoff', 'PSA synchronization'],
    setup: [
      'Use the Salesforce Connected App associated with the Certinia tenant.',
      'Enter the Salesforce or My Domain OAuth endpoints.',
      'Save the write-only client secret and complete OAuth consent.',
      'Add object-specific lookup and mapping details only after the connection test passes.',
    ],
  },
  servicenow: {
    providerKey: 'servicenow',
    providerName: 'ServiceNow',
    shortName: 'ServiceNow',
    providerType: 'itsm_erp',
    recommendedAuth: 'oauth2',
    authModel: 'oauth2',
    baseUrl: '',
    healthCheckUrl: '',
    oauthAuthorizationUrl: '',
    oauthTokenUrl: '',
    oauthScopes: '',
    apiKeyHeader: 'Authorization',
    apiKeyPrefix: 'Bearer',
    recordLookupUrlTemplate: '',
    importMappingJson: '{}',
    description: 'ITSM customer, request, incident, change, and service-delivery integration for an approved instance.',
    consumes: ['Customer service context', 'Request and incident reference', 'Delivery workflow handoff'],
    setup: [
      'Enter the approved ServiceNow instance URL, such as https://instance.service-now.com.',
      'Choose OAuth 2.0 or an approved API-key header according to the instance policy.',
      'For OAuth, enter the instance authorization and token URLs plus client ID.',
      'Save the write-only credential, enable the provider, and run an availability test.',
    ],
  },
});

const BUILTIN_ORDER = ['zendesk_sell', 'salesforce', 'servicenow', 'certinia'];

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

function authLabel(authModel) {
  return authModel === 'oauth2' ? 'OAuth 2.0' : 'API key / access token';
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
    recordLookupUrlTemplate: provider.recordLookupUrlTemplate,
    importMappingJson: provider.importMappingJson,
    isEnabled: Boolean(provider.isEnabled),
    notes: provider.notes,
  };
}

function templateProvider(templateKey, current = EMPTY_PROVIDER) {
  const template = PROVIDER_TEMPLATES[templateKey];
  if (!template) return { ...current };
  return {
    ...current,
    providerKey: current.providerKey || template.providerKey,
    providerName: template.providerName,
    providerType: template.providerType,
    authModel: template.authModel,
    baseUrl: template.baseUrl,
    healthCheckUrl: template.healthCheckUrl,
    oauthAuthorizationUrl: template.oauthAuthorizationUrl,
    oauthTokenUrl: template.oauthTokenUrl,
    oauthScopes: template.oauthScopes,
    apiKeyHeader: template.apiKeyHeader,
    apiKeyPrefix: template.apiKeyPrefix,
    recordLookupUrlTemplate: template.recordLookupUrlTemplate,
    importMappingJson: template.importMappingJson,
    notes: current.notes || `${template.shortName} connection managed by ProjectPulse Module 026.`,
  };
}

function serviceNowInstanceDefaults(value) {
  try {
    const origin = new URL(value).origin;
    return {
      baseUrl: origin,
      healthCheckUrl: `${origin}/api/now/table/sys_user?sysparm_limit=1`,
      oauthAuthorizationUrl: `${origin}/oauth_auth.do`,
      oauthTokenUrl: `${origin}/oauth_token.do`,
    };
  } catch {
    return null;
  }
}

export default function CrmErpIntegrationCenter() {
  const [state, setState] = useState({ loading: true, error: '', payload: null });
  const [selectedKey, setSelectedKey] = useState('');
  const [draft, setDraft] = useState(null);
  const [credential, setCredential] = useState('');
  const [newProvider, setNewProvider] = useState(EMPTY_PROVIDER);
  const [newProviderTemplate, setNewProviderTemplate] = useState('custom');
  const [showAdd, setShowAdd] = useState(false);
  const [busy, setBusy] = useState('');
  const [notice, setNotice] = useState({ tone: '', message: '' });

  const load = useCallback(async () => {
    setState((current) => ({ ...current, loading: true, error: '' }));
    try {
      const payload = await jsonRequest('/api/integrations/026/providers');
      setState({ loading: false, error: '', payload });
      setSelectedKey((current) => current || payload.providers?.find((provider) => provider.providerKey === 'zendesk_sell')?.providerKey || payload.providers?.[0]?.providerKey || '');
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

  const providers = useMemo(() => {
    const raw = state.payload?.providers ?? [];
    return [...raw].sort((left, right) => {
      const leftOrder = BUILTIN_ORDER.indexOf(left.providerKey);
      const rightOrder = BUILTIN_ORDER.indexOf(right.providerKey);
      if (leftOrder >= 0 || rightOrder >= 0) {
        if (leftOrder < 0) return 1;
        if (rightOrder < 0) return -1;
        return leftOrder - rightOrder;
      }
      return left.providerName.localeCompare(right.providerName);
    });
  }, [state.payload?.providers]);

  const selected = useMemo(
    () => providers.find((provider) => provider.providerKey === selectedKey) ?? null,
    [providers, selectedKey],
  );

  const selectedTemplate = PROVIDER_TEMPLATES[selectedKey] ?? null;

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

  function applySelectedTemplate() {
    if (!selectedTemplate || !draft) return;
    setDraft((current) => templateProvider(selectedKey, current));
    setNotice({ tone: 'warning', message: `Recommended ${selectedTemplate.shortName} non-secret defaults are staged. Review and save them before storing a credential.` });
  }

  function updateServiceNowInstance(value) {
    updateDraft('baseUrl', value);
    if (selectedKey !== 'servicenow') return;
    const defaults = serviceNowInstanceDefaults(value);
    if (!defaults) return;
    setDraft((current) => ({ ...current, ...defaults }));
  }

  function chooseNewProviderTemplate(templateKey) {
    setNewProviderTemplate(templateKey);
    setNewProvider(templateKey === 'custom'
      ? { ...EMPTY_PROVIDER }
      : templateProvider(templateKey, { ...EMPTY_PROVIDER }));
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
      setNewProvider({ ...EMPTY_PROVIDER });
      setNewProviderTemplate('custom');
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
            <span>Connect SELL, Salesforce, Certinia, ServiceNow, and approved custom platforms through one governed OAuth 2.0 or API key experience.</span>
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

      <section className="crm-erp-platform-overview" aria-label="Core integration platforms">
        <div className="crm-erp-section-copy">
          <p>Core connections</p>
          <h2>One pattern, provider-specific setup</h2>
          <span>Select a platform to view the correct authentication, endpoint, mapping, and downstream-consumer fields.</span>
        </div>
        <div className="crm-erp-platform-grid">
          {BUILTIN_ORDER.map((providerKey) => {
            const template = PROVIDER_TEMPLATES[providerKey];
            const provider = providers.find((item) => item.providerKey === providerKey);
            const status = provider?.availabilityStatus || 'not_configured';
            return (
              <button
                type="button"
                className={`crm-erp-platform-card ${selectedKey === providerKey ? 'active' : ''}`}
                key={providerKey}
                onClick={() => setSelectedKey(providerKey)}
              >
                <span className="crm-erp-platform-monogram">{template.shortName.slice(0, 2).toUpperCase()}</span>
                <div>
                  <strong>{template.shortName}</strong>
                  <small>{template.description}</small>
                </div>
                <span className={`crm-erp-status ${statusTone(status)}`}>{words(status)}</span>
              </button>
            );
          })}
        </div>
      </section>

      {showAdd && canManage ? (
        <form className="crm-erp-add-panel" onSubmit={addProvider}>
          <div className="crm-erp-section-heading">
            <div><p>Manual CRM/ERP registration</p><h2>Add another platform</h2><span>Start from a supported template or register another approved public HTTPS API.</span></div>
          </div>
          <div className="crm-erp-template-picker" role="group" aria-label="New provider template">
            <button type="button" className={newProviderTemplate === 'custom' ? 'active' : ''} onClick={() => chooseNewProviderTemplate('custom')}>Custom</button>
            {BUILTIN_ORDER.map((providerKey) => (
              <button type="button" className={newProviderTemplate === providerKey ? 'active' : ''} key={providerKey} onClick={() => chooseNewProviderTemplate(providerKey)}>{PROVIDER_TEMPLATES[providerKey].shortName}</button>
            ))}
          </div>
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
          <div className="crm-erp-provider-list-heading"><strong>Saved connections</strong><small>Select a platform to configure or test.</small></div>
          {state.loading && !providers.length ? <p>Loading integrations…</p> : null}
          {providers.map((provider) => (
            <button type="button" key={provider.providerKey} className={selectedKey === provider.providerKey ? 'active' : ''} onClick={() => setSelectedKey(provider.providerKey)}>
              <div><strong>{provider.providerName}</strong><small>{words(provider.providerType)} · {authLabel(provider.authModel)}</small></div>
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

            {selectedTemplate ? (
              <section className="crm-erp-provider-guide">
                <div>
                  <p>{selectedTemplate.shortName} integration profile</p>
                  <h3>{selectedTemplate.description}</h3>
                  <div className="crm-erp-consumer-list">
                    {selectedTemplate.consumes.map((consumer) => <span key={consumer}>{consumer}</span>)}
                  </div>
                </div>
                {canManage ? <button type="button" className="secondary-action" onClick={applySelectedTemplate}>Apply recommended template</button> : null}
                <ol>{selectedTemplate.setup.map((step) => <li key={step}>{step}</li>)}</ol>
                {selectedKey === 'zendesk_sell' ? (
                  <a className="crm-erp-inline-link" href="#customer-directory">Open Module 021 Customer Directory sync →</a>
                ) : null}
              </section>
            ) : null}

            <div className="crm-erp-detail-metrics">
              <article><span>Enabled</span><strong>{draft.isEnabled ? 'Yes' : 'No'}</strong></article>
              <article><span>Authentication</span><strong>{authLabel(draft.authModel)}</strong></article>
              <article><span>Credential</span><strong>{draft.credentialConfigured ? 'Saved' : 'Missing'}</strong></article>
              <article><span>OAuth consent</span><strong>{draft.authModel === 'oauth2' ? (draft.oauthConnected ? 'Connected' : 'Pending') : 'Not used'}</strong></article>
              <article><span>Last HTTP status</span><strong>{draft.lastStatusCode || '—'}</strong></article>
              <article><span>Record import</span><strong>{draft.recordLookupUrlTemplate ? 'Mapped' : 'Not mapped'}</strong></article>
            </div>

            {canManage ? (
              <>
                <form className="crm-erp-configuration" onSubmit={saveConfiguration}>
                  <div className="crm-erp-section-heading"><div><p>Non-secret settings</p><h3>Connection configuration</h3><span>Save configuration before adding the write-only credential or starting OAuth.</span></div><label className="crm-erp-toggle"><input type="checkbox" checked={Boolean(draft.isEnabled)} onChange={(event) => updateDraft('isEnabled', event.target.checked)} /> Enabled</label></div>
                  <div className="crm-erp-auth-switch" role="group" aria-label="Authentication method">
                    <button type="button" className={draft.authModel === 'oauth2' ? 'active' : ''} onClick={() => updateDraft('authModel', 'oauth2')}><strong>OAuth 2.0</strong><small>Client ID, write-only secret, and provider consent</small></button>
                    <button type="button" className={draft.authModel === 'api_key' ? 'active' : ''} onClick={() => updateDraft('authModel', 'api_key')}><strong>API key</strong><small>Write-only token sent through the configured header</small></button>
                  </div>
                  <div className="crm-erp-form-grid">
                    <label>Display name<input value={draft.providerName} onChange={(event) => updateDraft('providerName', event.target.value)} /></label>
                    <label>Platform type<select value={draft.providerType} onChange={(event) => updateDraft('providerType', event.target.value)}><option value="crm">CRM</option><option value="erp">ERP</option><option value="erp_psa">ERP / PSA</option><option value="itsm_erp">ITSM / ERP</option><option value="other">Other</option></select></label>
                    <label className="wide">Base URL<input type="url" placeholder={selectedKey === 'servicenow' ? 'https://instance.service-now.com' : 'https://provider.example.com'} value={draft.baseUrl} onChange={(event) => updateServiceNowInstance(event.target.value)} /><small>Only approved public HTTPS endpoints are accepted by the backend.</small></label>
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
                    <label className="wide">Record lookup URL template<input type="text" inputMode="url" placeholder="https://provider.example.com/api/records/{recordId}" value={draft.recordLookupUrlTemplate || ''} onChange={(event) => updateDraft('recordLookupUrlTemplate', event.target.value)} /><small>Required for source-record imports. Keep the literal {'{recordId}'} placeholder.</small></label>
                    <label className="wide">Import field mapping (JSON)<textarea rows={10} value={draft.importMappingJson || '{}'} onChange={(event) => updateDraft('importMappingJson', event.target.value)} /><small>Maps source fields into ProjectPulse. SELL customer sync uses its governed organization contract; Work Register intake uses project, quote, customer, pricing, and rate paths.</small></label>
                    <label className="wide">Notes<textarea value={draft.notes} onChange={(event) => updateDraft('notes', event.target.value)} /></label>
                  </div>
                  <button type="submit" className="primary-action" disabled={busy === `save:${draft.providerKey}`}>{busy === `save:${draft.providerKey}` ? 'Saving…' : 'Save configuration'}</button>
                </form>

                <form className="crm-erp-credential" onSubmit={saveCredential}>
                  <div><p>Write-only credential</p><h3>{draft.authModel === 'oauth2' ? 'OAuth client secret' : 'API key / access token'}</h3><span>The saved value cannot be viewed later and is never returned by the API.</span></div>
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
