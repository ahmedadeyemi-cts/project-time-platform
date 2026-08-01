import { useCallback, useEffect, useMemo, useState } from 'react';
import { usSignalLogoDataUrl } from './assets/usSignalLogoData.js';
import './projectpulse-module-standard.css';
/* CRM_ERP_TOKEN_PERSISTENCE_PANEL_IMPORT */
import CrmErpTokenPersistencePanel from './CrmErpTokenPersistencePanel.jsx';
import './crm-erp-integration-center.css';

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
  isBuiltin: false,
  isPersisted: false,
  isEnabled: false,
  availabilityStatus: 'not_configured',
  credentialConfigured: false,
  oauthConnected: false,
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
    authModel: 'api_key',
    recommendedAuth: 'api_key',
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
      'Choose API key for a governed access token or OAuth 2.0 for delegated consent.',
      'Review the non-secret URLs, scopes, lookup template, and field mapping.',
      'Save configuration, then enter the write-only token or OAuth client secret.',
      'Enable and test the connection before another module consumes SELL data.',
    ],
  },
  salesforce: {
    providerKey: 'salesforce',
    providerName: 'Salesforce',
    shortName: 'Salesforce',
    providerType: 'crm',
    authModel: 'oauth2',
    recommendedAuth: 'oauth2',
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
  servicenow: {
    providerKey: 'servicenow',
    providerName: 'ServiceNow',
    shortName: 'ServiceNow',
    providerType: 'itsm_erp',
    authModel: 'oauth2',
    recommendedAuth: 'oauth2',
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
  certinia: {
    providerKey: 'certinia',
    providerName: 'Certinia',
    shortName: 'Certinia',
    providerType: 'erp_psa',
    authModel: 'oauth2',
    recommendedAuth: 'oauth2',
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
      'Add object-specific lookup and mapping details after the connection test passes.',
    ],
  },
});

const BUILTIN_ORDER = ['zendesk_sell', 'salesforce', 'servicenow', 'certinia'];

function sessionToken() {
  return window.localStorage.getItem('projectPulseSessionToken')
    ?? window.sessionStorage.getItem('projectPulseSessionToken')
    ?? '';
}

function words(value) {
  return String(value || 'not configured')
    .replaceAll('_', ' ')
    .replace(/\b\w/g, (letter) => letter.toUpperCase());
}

function statusLabel(provider) {
  if (!provider?.isPersisted) return 'Needs setup';
  if (!provider?.isEnabled && provider?.availabilityStatus === 'disabled') return 'Disabled';
  return words(provider?.availabilityStatus || 'not_configured');
}

function statusTone(provider) {
  const status = provider?.availabilityStatus;
  if (!provider?.isPersisted || status === 'not_configured') return 'pending';
  if (status === 'available') return 'available';
  if (status === 'authentication_failed') return 'authentication';
  if (status === 'unavailable') return 'unavailable';
  if (status === 'disabled') return 'disabled';
  return 'pending';
}

function formatDate(value) {
  if (!value) return 'Never';
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? 'Never' : date.toLocaleString();
}

function authLabel(authModel) {
  return authModel === 'oauth2' ? 'OAuth 2.0' : 'API key / access token';
}

async function jsonRequest(url, options = {}) {
  const token = sessionToken();
  const response = await fetch(url, {
    credentials: 'include',
    cache: 'no-store',
    ...options,
    headers: {
      ...(options.body ? { 'Content-Type': 'application/json' } : {}),
      ...(token ? {
        Authorization: `Bearer ${token}`,
        'X-ProjectPulse-Session': token,
        'X-Project-Pulse-Session': token,
        'X-Session-Token': token,
      } : {}),
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
    ...EMPTY_PROVIDER,
    ...current,
    providerKey: current.providerKey || template.providerKey,
    providerName: current.providerName || template.providerName,
    providerType: current.providerType || template.providerType,
    authModel: current.authModel || template.authModel,
    baseUrl: current.baseUrl || template.baseUrl,
    healthCheckUrl: current.healthCheckUrl || template.healthCheckUrl,
    oauthAuthorizationUrl: current.oauthAuthorizationUrl || template.oauthAuthorizationUrl,
    oauthTokenUrl: current.oauthTokenUrl || template.oauthTokenUrl,
    oauthScopes: current.oauthScopes || template.oauthScopes,
    apiKeyHeader: current.apiKeyHeader || template.apiKeyHeader,
    apiKeyPrefix: current.apiKeyPrefix || template.apiKeyPrefix,
    recordLookupUrlTemplate: current.recordLookupUrlTemplate || template.recordLookupUrlTemplate,
    importMappingJson: current.importMappingJson && current.importMappingJson !== '{}'
      ? current.importMappingJson
      : template.importMappingJson,
    notes: current.notes || `${template.shortName} connection managed by ProjectPulse Module 026.`,
    isBuiltin: true,
    isPersisted: Boolean(current.isPersisted),
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
  const [selectedKey, setSelectedKey] = useState('zendesk_sell');
  const [draft, setDraft] = useState(null);
  const [editing, setEditing] = useState(false);
  const [credential, setCredential] = useState('');
  const [showCredential, setShowCredential] = useState(false);
  const [newProvider, setNewProvider] = useState({ ...EMPTY_PROVIDER });
  const [showAdd, setShowAdd] = useState(false);
  const [busy, setBusy] = useState('');
  const [notice, setNotice] = useState({ tone: '', message: '' });

  const load = useCallback(async (preferredKey = '') => {
    setState((current) => ({ ...current, loading: true, error: '' }));
    try {
      const payload = await jsonRequest('/api/integrations/026/providers');
      setState({ loading: false, error: '', payload });
      setSelectedKey((current) => preferredKey || current || 'zendesk_sell');
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
    const persisted = state.payload?.providers ?? [];
    const builtins = BUILTIN_ORDER.map((providerKey) => {
      const saved = persisted.find((item) => item.providerKey === providerKey);
      return templateProvider(providerKey, saved || { ...EMPTY_PROVIDER, providerKey });
    });
    const customs = persisted
      .filter((provider) => !BUILTIN_ORDER.includes(provider.providerKey))
      .sort((left, right) => left.providerName.localeCompare(right.providerName));
    return [...builtins, ...customs];
  }, [state.payload?.providers]);

  const selected = useMemo(
    () => providers.find((provider) => provider.providerKey === selectedKey) ?? providers[0] ?? null,
    [providers, selectedKey],
  );

  const selectedTemplate = PROVIDER_TEMPLATES[selected?.providerKey] ?? null;
  const canManage = Boolean(state.payload?.access?.canManage);
  const configuredCount = providers.filter((provider) => provider.credentialConfigured || provider.oauthConnected).length;
  const availableCount = providers.filter((provider) => provider.availabilityStatus === 'available').length;

  useEffect(() => {
    setDraft(selected ? { ...selected } : null);
    setCredential('');
    setShowCredential(false);
    setEditing(false);
  }, [selected?.providerKey, selected?.isPersisted, selected?.lastCheckedAt]);

  function selectProvider(providerKey) {
    setSelectedKey(providerKey);
    setNotice({ tone: '', message: '' });
  }

  function beginEditing() {
    if (!canManage || !selected) return;
    setDraft({ ...selected });
    setEditing(true);
    setNotice({ tone: '', message: '' });
  }

  function cancelEditing() {
    setDraft(selected ? { ...selected } : null);
    setCredential('');
    setEditing(false);
    setNotice({ tone: '', message: '' });
  }

  function updateDraft(field, value) {
    setDraft((current) => ({ ...current, [field]: value }));
  }

  function applySelectedTemplate() {
    if (!selectedTemplate || !draft) return;
    setDraft((current) => templateProvider(selected.providerKey, current));
    setNotice({ tone: 'warning', message: `Recommended ${selectedTemplate.shortName} non-secret defaults are staged. Review and save them before adding a credential.` });
  }

  function updateServiceNowInstance(value) {
    updateDraft('baseUrl', value);
    if (selected?.providerKey !== 'servicenow') return;
    const defaults = serviceNowInstanceDefaults(value);
    if (!defaults) return;
    setDraft((current) => ({ ...current, ...defaults }));
  }

  async function saveConfiguration(event) {
    event.preventDefault();
    if (!draft || !canManage) return;
    setBusy(`save:${draft.providerKey}`);
    setNotice({ tone: '', message: '' });
    try {
      const creating = !draft.isPersisted;
      const result = await jsonRequest(
        creating
          ? '/api/integrations/026/providers'
          : `/api/integrations/026/providers/${encodeURIComponent(draft.providerKey)}`,
        {
          method: creating ? 'POST' : 'PUT',
          body: JSON.stringify(providerPayload(draft)),
        },
      );
      setNotice({ tone: 'success', message: result.message || 'Connection configuration saved.' });
      await load(draft.providerKey);
      setEditing(false);
    } catch (error) {
      setNotice({ tone: 'error', message: error?.message || 'Configuration could not be saved.' });
    } finally {
      setBusy('');
    }
  }

  async function saveCredential(event) {
    event.preventDefault();
    if (!draft?.isPersisted || !credential.trim() || !canManage) return;
    setBusy(`credential:${draft.providerKey}`);
    setNotice({ tone: '', message: '' });
    try {
      const result = await jsonRequest(`/api/integrations/026/providers/${encodeURIComponent(draft.providerKey)}/credential`, {
        method: 'PUT',
        body: JSON.stringify({ secret: credential.trim() }),
      });
      setCredential('');
      setShowCredential(false);
      setNotice({ tone: 'success', message: result.message || 'Credential saved securely.' });
      await load(draft.providerKey);
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
      const result = await jsonRequest(`/api/integrations/026/providers/${encodeURIComponent(providerKey)}/test`, { method: 'POST' });
      setNotice({
        tone: result.availabilityStatus === 'available' ? 'success' : 'warning',
        message: `${words(result.availabilityStatus)} · ${result.durationMs} ms${result.statusCode ? ` · HTTP ${result.statusCode}` : ''}`,
      });
      await load(providerKey);
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
      const result = await jsonRequest(`/api/integrations/026/providers/${encodeURIComponent(providerKey)}/oauth/start`, { method: 'POST' });
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
    if (!canManage) return;
    setBusy('add');
    setNotice({ tone: '', message: '' });
    try {
      const result = await jsonRequest('/api/integrations/026/providers', {
        method: 'POST',
        body: JSON.stringify(providerPayload(newProvider)),
      });
      setNewProvider({ ...EMPTY_PROVIDER });
      setShowAdd(false);
      setNotice({ tone: 'success', message: result.message || 'CRM platform added.' });
      await load(result.providerKey);
      setEditing(true);
    } catch (error) {
      setNotice({ tone: 'error', message: error?.message || 'Provider could not be added.' });
    } finally {
      setBusy('');
    }
  }

  return (
    <section className="crm-erp-center projectpulse-module-standard" data-module="026" data-brand="us-signal">
      <header className="crm-erp-hero">
        <img className="projectpulse-module-standard__logo" src={usSignalLogoDataUrl} alt="US Signal" />
        <div className="crm-erp-hero-copy">
          <p className="crm-erp-eyebrow">Module 026 · CRM/ERP integrations</p>
          <h1>Integration Control Center</h1>
          <span>Configure SELL, Salesforce, ServiceNow, Certinia, and approved custom CRM or ERP platforms through one secure, consistent administration experience.</span>
        </div>
        <div className="crm-erp-hero-actions">
          <button type="button" className="secondary-action" onClick={() => load()} disabled={state.loading}>{state.loading ? 'Refreshing…' : 'Refresh status'}</button>
          <button type="button" className="primary-action" onClick={() => setShowAdd(true)} disabled={!canManage}>Add CRM platform</button>
        </div>
      </header>

      <div className="crm-erp-security-banner">
        <strong>Secure connection boundary</strong>
        <span>OAuth tokens and API keys are encrypted server-side, write-only, never displayed after saving, and excluded from availability logs and audit evidence.</span>
      </div>

      {!canManage && state.payload ? (
        <div className="crm-erp-access-banner" role="status">
          <div><strong>View-only access</strong><span>{state.payload?.access?.manageMessage || 'Your actual session does not currently have Module 026 configuration authority.'}</span></div>
          <small>Required permission: {state.payload?.access?.requiredPermission || 'MANAGE_INTEGRATIONS_026'} · Authority source: {words(state.payload?.access?.manageAuthoritySource)}</small>
        </div>
      ) : null}

      {state.error ? <div className="crm-erp-notice error" role="alert">{state.error}</div> : null}
      {notice.message ? <div className={`crm-erp-notice ${notice.tone}`} role="status">{notice.message}</div> : null}

      <div className="crm-erp-summary">
        <article><span>Registered platforms</span><strong>{providers.length}</strong><small>Built-in templates and custom connectors</small></article>
        <article><span>Configured</span><strong>{configuredCount}</strong><small>Credential metadata only</small></article>
        <article><span>Available</span><strong>{availableCount}</strong><small>Latest explicit connection test</small></article>
        <article><span>Your access</span><strong>{canManage ? 'Configure' : 'View status'}</strong><small>{state.payload?.access?.isViewAs ? 'View-As is read-only' : 'Actual ProjectPulse session'}</small></article>
      </div>

      {/* CRM_ERP_TOKEN_PERSISTENCE_PANEL_MOUNT */}
      <CrmErpTokenPersistencePanel
        provider={selected}
        canManage={canManage}
        onRefresh={() => load(selected?.providerKey)}
      />

      <section className="crm-erp-platform-overview" aria-label="Core integration platforms">
        <div className="crm-erp-section-copy">
          <p>Core connections</p>
          <h2>Select a connector, then choose Edit</h2>
          <span>Every provider opens the same governed configuration workspace while retaining its provider-specific endpoints, authentication, mapping, and downstream-consumer guidance.</span>
        </div>
        <div className="crm-erp-platform-grid">
          {BUILTIN_ORDER.map((providerKey) => {
            const template = PROVIDER_TEMPLATES[providerKey];
            const provider = providers.find((item) => item.providerKey === providerKey);
            return (
              <button type="button" className={`crm-erp-platform-card ${selected?.providerKey === providerKey ? 'active' : ''}`} key={providerKey} onClick={() => selectProvider(providerKey)}>
                <span className="crm-erp-platform-monogram">{template.shortName.slice(0, 2).toUpperCase()}</span>
                <div><strong>{template.shortName}</strong><small>{template.description}</small></div>
                <div className="crm-erp-platform-card-footer"><span className={`crm-erp-status ${statusTone(provider)}`}>{statusLabel(provider)}</span><span className="crm-erp-card-action">{provider?.isPersisted ? 'Open connection' : 'Configure connection'} →</span></div>
              </button>
            );
          })}
        </div>
      </section>

      <div className="crm-erp-layout">
        <nav className="crm-erp-provider-list" aria-label="Integration providers">
          <div className="crm-erp-provider-list-heading"><strong>Connections</strong><small>Built-in and custom platforms</small></div>
          {providers.map((provider) => (
            <button type="button" key={provider.providerKey} className={selected?.providerKey === provider.providerKey ? 'active' : ''} onClick={() => selectProvider(provider.providerKey)}>
              <div><strong>{PROVIDER_TEMPLATES[provider.providerKey]?.shortName || provider.providerName}</strong><small>{words(provider.providerType)} · {authLabel(provider.authModel)}</small></div>
              <span className={`crm-erp-status ${statusTone(provider)}`}>{statusLabel(provider)}</span>
            </button>
          ))}
        </nav>

        {draft ? (
          <main className="crm-erp-detail">
            <section className="crm-erp-detail-heading">
              <div><p>{selectedTemplate ? 'Built-in connector' : 'Custom connector'}</p><h2>{draft.providerName}</h2><span>{draft.isPersisted ? `Last checked ${formatDate(draft.lastCheckedAt)}` : 'Template is ready for its first save'}</span></div>
              <div className="crm-erp-detail-heading-actions"><span className={`crm-erp-status large ${statusTone(draft)}`}>{statusLabel(draft)}</span>{!editing ? <button type="button" className="primary-action" onClick={beginEditing} disabled={!canManage}>{draft.isPersisted ? 'Edit connection' : 'Configure connection'}</button> : null}</div>
            </section>

            {selectedTemplate ? (
              <section className="crm-erp-provider-guide">
                <div><p>{selectedTemplate.shortName} integration profile</p><h3>{selectedTemplate.description}</h3><div className="crm-erp-consumer-list">{selectedTemplate.consumes.map((consumer) => <span key={consumer}>{consumer}</span>)}</div></div>
                {editing ? <button type="button" className="secondary-action" onClick={applySelectedTemplate}>Apply recommended template</button> : null}
                <ol>{selectedTemplate.setup.map((step) => <li key={step}>{step}</li>)}</ol>
                {selected?.providerKey === 'zendesk_sell' ? <a className="crm-erp-inline-link" href="#customer-directory">Open Module 021 Customer Directory sync →</a> : null}
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

            {!editing ? (
              <section className="crm-erp-readonly-summary">
                <div><strong>Base URL</strong><span>{draft.baseUrl || 'Not configured'}</span></div>
                <div><strong>Health URL</strong><span>{draft.healthCheckUrl || 'Not configured'}</span></div>
                <div><strong>Authentication</strong><span>{authLabel(draft.authModel)}</span></div>
                <div><strong>Secret</strong><span>{draft.credentialConfigured ? 'Stored securely; value hidden' : 'Not yet stored'}</span></div>
                <div className="wide"><strong>Notes</strong><span>{draft.notes || 'No notes recorded.'}</span></div>
              </section>
            ) : null}

            {editing && canManage ? (
              <>
                <form className="crm-erp-configuration" onSubmit={saveConfiguration}>
                  <div className="crm-erp-section-heading"><div><p>Editable non-secret settings</p><h3>Connection configuration</h3><span>{draft.isPersisted ? 'Save changes before replacing the credential or testing.' : 'The first save registers this built-in template as an editable connection.'}</span></div><label className="crm-erp-toggle"><input type="checkbox" checked={Boolean(draft.isEnabled)} onChange={(event) => updateDraft('isEnabled', event.target.checked)} /> Enabled</label></div>
                  <div className="crm-erp-auth-switch" role="group" aria-label="Authentication method">
                    <button type="button" className={draft.authModel === 'oauth2' ? 'active' : ''} onClick={() => updateDraft('authModel', 'oauth2')}><strong>OAuth 2.0</strong><small>Client ID, write-only secret, and provider consent</small></button>
                    <button type="button" className={draft.authModel === 'api_key' ? 'active' : ''} onClick={() => updateDraft('authModel', 'api_key')}><strong>API key</strong><small>Write-only token sent through the configured header</small></button>
                  </div>
                  <div className="crm-erp-form-grid">
                    <label>Display name<input required value={draft.providerName} onChange={(event) => updateDraft('providerName', event.target.value)} /></label>
                    <label>Platform type<select value={draft.providerType} onChange={(event) => updateDraft('providerType', event.target.value)}><option value="crm">CRM</option><option value="erp">ERP</option><option value="erp_psa">ERP / PSA</option><option value="itsm_erp">ITSM / ERP</option><option value="other">Other</option></select></label>
                    <label className="wide">Base URL<input type="url" placeholder={selected?.providerKey === 'servicenow' ? 'https://instance.service-now.com' : 'https://provider.example.com'} value={draft.baseUrl} onChange={(event) => updateServiceNowInstance(event.target.value)} /><small>Only approved public HTTPS endpoints are accepted.</small></label>
                    <label className="wide">Availability / health URL<input type="url" placeholder="https://provider.example.com/api/status" value={draft.healthCheckUrl} onChange={(event) => updateDraft('healthCheckUrl', event.target.value)} /></label>
                    {draft.authModel === 'oauth2' ? <><label className="wide">OAuth authorization URL<input type="url" value={draft.oauthAuthorizationUrl} onChange={(event) => updateDraft('oauthAuthorizationUrl', event.target.value)} /></label><label className="wide">OAuth token URL<input type="url" value={draft.oauthTokenUrl} onChange={(event) => updateDraft('oauthTokenUrl', event.target.value)} /></label><label>OAuth client ID<input value={draft.oauthClientId} onChange={(event) => updateDraft('oauthClientId', event.target.value)} /></label><label>OAuth scopes<input value={draft.oauthScopes} placeholder="api refresh_token" onChange={(event) => updateDraft('oauthScopes', event.target.value)} /></label></> : <><label>API-key header<input value={draft.apiKeyHeader} onChange={(event) => updateDraft('apiKeyHeader', event.target.value)} /></label><label>Value prefix<input value={draft.apiKeyPrefix} placeholder="Bearer" onChange={(event) => updateDraft('apiKeyPrefix', event.target.value)} /></label></>}
                    <label className="wide">Record lookup URL template<input type="text" inputMode="url" placeholder="https://provider.example.com/api/records/{recordId}" value={draft.recordLookupUrlTemplate || ''} onChange={(event) => updateDraft('recordLookupUrlTemplate', event.target.value)} /><small>Keep the literal {'{recordId}'} placeholder.</small></label>
                    <label className="wide">Import field mapping (JSON)<textarea rows={10} value={draft.importMappingJson || '{}'} onChange={(event) => updateDraft('importMappingJson', event.target.value)} /><small>Maps approved source fields into ProjectPulse.</small></label>
                    <label className="wide">Notes<textarea value={draft.notes} onChange={(event) => updateDraft('notes', event.target.value)} /></label>
                  </div>
                  <div className="crm-erp-actions"><button type="submit" className="primary-action" disabled={busy === `save:${draft.providerKey}`}>{busy === `save:${draft.providerKey}` ? 'Saving…' : draft.isPersisted ? 'Save configuration' : 'Create connection'}</button><button type="button" className="secondary-action" onClick={cancelEditing}>Cancel</button></div>
                </form>

                <form className="crm-erp-credential" onSubmit={saveCredential}>
                  <div><p>Write-only credential</p><h3>{draft.authModel === 'oauth2' ? 'OAuth client secret' : 'API key / access token'}</h3><span>{draft.isPersisted ? 'The value is encrypted and cannot be viewed after saving.' : 'Create the connection first; then add the secret.'}</span></div>
                  <label><span className="sr-only">Write-only credential</span><div className="crm-erp-secret-input"><input type={showCredential ? 'text' : 'password'} autoComplete="new-password" value={credential} disabled={!draft.isPersisted} placeholder={draft.credentialConfigured ? 'Replace saved credential' : 'Enter credential'} onChange={(event) => setCredential(event.target.value)} /><button type="button" className="secondary-action" disabled={!draft.isPersisted} onClick={() => setShowCredential((current) => !current)}>{showCredential ? 'Hide while typing' : 'Show while typing'}</button></div></label>
                  <button type="submit" className="secondary-action" disabled={!draft.isPersisted || !credential.trim() || busy === `credential:${draft.providerKey}`}>{busy === `credential:${draft.providerKey}` ? 'Encrypting…' : 'Save credential securely'}</button>
                </form>

                <div className="crm-erp-actions-panel">
                  <div><p>Connection lifecycle</p><h3>Connect and verify</h3><span>Tests contact only the configured public HTTPS availability endpoint and store sanitized results.</span></div>
                  <div className="crm-erp-actions">{draft.authModel === 'oauth2' ? <button type="button" className="secondary-action" onClick={() => connectOAuth(draft.providerKey)} disabled={!draft.isPersisted || busy === `oauth:${draft.providerKey}`}>{busy === `oauth:${draft.providerKey}` ? 'Preparing…' : draft.oauthConnected ? 'Reconnect OAuth' : 'Connect with OAuth'}</button> : null}<button type="button" className="primary-action" onClick={() => testConnection(draft.providerKey)} disabled={!draft.isPersisted || busy === `test:${draft.providerKey}`}>{busy === `test:${draft.providerKey}` ? 'Testing…' : 'Test availability'}</button></div>
                </div>
              </>
            ) : null}
          </main>
        ) : <main className="crm-erp-detail"><p>Select an integration provider.</p></main>}
      </div>

      {showAdd && canManage ? (
        <div className="crm-erp-modal-backdrop" role="presentation" onMouseDown={(event) => { if (event.target === event.currentTarget) setShowAdd(false); }}>
          <form className="crm-erp-add-panel" onSubmit={addProvider} role="dialog" aria-modal="true" aria-labelledby="crm-erp-add-title">
            <div className="crm-erp-section-heading"><div><p>Custom connection</p><h2 id="crm-erp-add-title">Add another CRM or ERP platform</h2><span>Register a unique key and basic connection type. The new connector opens in the same editor for endpoint, mapping, credential, enablement, and test configuration.</span></div><button type="button" className="secondary-action" onClick={() => setShowAdd(false)}>Close</button></div>
            <div className="crm-erp-form-grid"><label>Provider key<input required pattern="[A-Za-z0-9_]{2,60}" value={newProvider.providerKey} placeholder="example_crm" onChange={(event) => setNewProvider((current) => ({ ...current, providerKey: event.target.value.toLowerCase().replace(/[^a-z0-9_]/g, '_') }))} /><small>Stable internal key; letters, numbers, and underscores.</small></label><label>Display name<input required value={newProvider.providerName} placeholder="Example CRM" onChange={(event) => setNewProvider((current) => ({ ...current, providerName: event.target.value }))} /></label><label>Platform type<select value={newProvider.providerType} onChange={(event) => setNewProvider((current) => ({ ...current, providerType: event.target.value }))}><option value="crm">CRM</option><option value="erp">ERP</option><option value="erp_psa">ERP / PSA</option><option value="itsm_erp">ITSM / ERP</option><option value="other">Other</option></select></label><label>Authentication<select value={newProvider.authModel} onChange={(event) => setNewProvider((current) => ({ ...current, authModel: event.target.value }))}><option value="oauth2">OAuth 2.0</option><option value="api_key">API key</option></select></label></div>
            <div className="crm-erp-actions"><button type="submit" className="primary-action" disabled={busy === 'add'}>{busy === 'add' ? 'Adding…' : 'Add platform and continue setup'}</button><button type="button" className="secondary-action" onClick={() => setShowAdd(false)}>Cancel</button></div>
          </form>
        </div>
      ) : null}
    </section>
  );
}
