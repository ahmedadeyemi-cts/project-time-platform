import { useCallback, useEffect, useMemo, useState } from 'react';
import './microsoft-integration-portal.css';
import './microsoft-integration-dual-connections.css';

const ACTIVE_ROUTE = 'entra-secret-administration';
const CONFIG_MARKER = 'PROJECTPULSE_MICROSOFT_INTEGRATION_JSON:';
const ENVIRONMENTS = ['test', 'production'];
const SSO_CALLBACK_PATH = '/api/auth/sso/callback';
const REQUIRED_SERVICES_SCOPES = ['Directory.Read.All', 'User.Read.All', 'Mail.Send'];

function routeFromHash() {
  return window.location.hash.replace(/^#/, '').split('?')[0].trim();
}

async function readJson(response) {
  const text = await response.text();
  if (!text.trim()) return {};
  try {
    return JSON.parse(text);
  } catch {
    return { status: 'invalid_json_response' };
  }
}

async function fetchJson(url, init) {
  const response = await fetch(url, init);
  const body = await readJson(response);
  if (!response.ok) throw new Error(body?.message || body?.status || `Request failed with HTTP ${response.status}.`);
  return body;
}

function runtimeEnvironmentMode() {
  const host = window.location.hostname.toLowerCase();
  if (host.includes('-test.') || host === 'localhost' || host === '127.0.0.1') return 'test';
  return 'production';
}

function currentCallbackUri() {
  return `${window.location.origin}${SSO_CALLBACK_PATH}`;
}

function isGuid(value) {
  return /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(String(value || '').trim());
}

function callbackPathMatches(value) {
  try {
    const url = new URL(String(value || '').trim());
    return url.protocol === 'https:' && url.pathname.replace(/\/$/, '') === SSO_CALLBACK_PATH;
  } catch {
    return false;
  }
}

function expectedRedirectFor(environmentMode) {
  return environmentMode === runtimeEnvironmentMode() ? currentCallbackUri() : '';
}

function redirectWithCurrentEnvironment(value, environmentMode) {
  const current = String(value || '').trim();
  const expected = expectedRedirectFor(environmentMode);
  if (!expected) return current;
  if (!current) return expected;
  try {
    const url = new URL(current);
    if (url.hostname.toLowerCase() === 'projectpulse-test.onenecklab.com') return expected;
  } catch {
    return expected;
  }
  return current;
}

function normalizeServicesScopes(value) {
  const values = String(value || '')
    .split(/[\s,;]+/)
    .map((item) => item.trim())
    .filter(Boolean);
  const map = new Map(values.map((item) => [item.toLowerCase(), item]));
  for (const required of REQUIRED_SERVICES_SCOPES) {
    if (!map.has(required.toLowerCase())) map.set(required.toLowerCase(), required);
  }
  return [...map.values()].join(' ');
}

function environmentDefaults(environmentMode) {
  const production = environmentMode === 'production';
  const tenantKey = production ? 'ussignal' : 'onenecklab';
  const tenantDomain = production ? 'ussignal.com' : 'onenecklab.com';
  return {
    key: tenantKey,
    name: production ? 'US Signal Production' : 'OneNeck Lab Test',
    environmentMode,
    tenantDomain,
    tenantId: '',
    sourceProvider: production ? 'ENTRA_ID' : 'ENTRA_ID_TEST',
    directorySyncEnabled: false,
    syncFrequencyHours: 24,
    defaultRoleCode: 'ENGINEERING',
    sso: {
      connectionPurpose: 'sso_app_registration',
      clientId: '',
      authorityUrl: '',
      redirectUri: expectedRedirectFor(environmentMode),
      allowedDomains: production ? 'ussignal.com' : 'onenecklab.com,onitdemo.com'
    },
    services: {
      connectionPurpose: 'microsoft_services_enterprise_application',
      clientId: '',
      graphScopes: REQUIRED_SERVICES_SCOPES.join(' ')
    }
  };
}

function normalizeTenant(raw, environmentMode) {
  const defaults = environmentDefaults(environmentMode);
  const sso = raw?.sso || raw?.ssoConnection || {};
  const services = raw?.services || raw?.servicesConnection || {};
  const tenantId = String(raw?.tenantId || '').trim();
  const authorityUrl = sso.authorityUrl || sso.authority || raw?.authorityUrl || (isGuid(tenantId) ? `https://login.microsoftonline.com/${tenantId}` : '');
  const redirectUri = redirectWithCurrentEnvironment(
    sso.redirectUri || sso.callbackUri || raw?.redirectUri || '',
    environmentMode
  );
  return {
    ...defaults,
    ...raw,
    key: raw?.key || raw?.tenantKey || defaults.key,
    name: raw?.name || raw?.tenantName || defaults.name,
    environmentMode,
    tenantDomain: raw?.tenantDomain || defaults.tenantDomain,
    tenantId,
    sourceProvider: raw?.sourceProvider || defaults.sourceProvider,
    directorySyncEnabled: Boolean(raw?.directorySyncEnabled ?? raw?.syncEnabled ?? false),
    syncFrequencyHours: Number(raw?.syncFrequencyHours || 24),
    defaultRoleCode: String(raw?.defaultRoleCode || 'ENGINEERING').toUpperCase(),
    sso: {
      ...defaults.sso,
      ...sso,
      clientId: sso.clientId || sso.applicationId || raw?.ssoClientId || '',
      authorityUrl,
      redirectUri,
      allowedDomains: sso.allowedDomains || raw?.ssoAllowedDomains || defaults.sso.allowedDomains
    },
    services: {
      ...defaults.services,
      ...services,
      // The original Module 065 clientId is intentionally carried forward as the services/Graph application.
      clientId: services.clientId || services.applicationId || raw?.serviceClientId || raw?.clientId || '',
      graphScopes: normalizeServicesScopes(services.graphScopes || services.scopes || raw?.graphScopes || raw?.graphScope || defaults.services.graphScopes)
    }
  };
}

function configurationWithBothEnvironments(stored, observedTenant) {
  const rawTenants = Array.isArray(stored?.tenants) ? stored.tenants : [];
  const tenants = ENVIRONMENTS.map((environmentMode) => {
    const existing = rawTenants.find((tenant) => {
      const mode = String(tenant?.environmentMode || '').toLowerCase();
      return mode === environmentMode
        || (environmentMode === 'test' && String(tenant?.sourceProvider || '').includes('TEST'))
        || (environmentMode === 'production' && mode === 'prod');
    });
    return normalizeTenant(existing, environmentMode);
  });

  if (rawTenants.length === 0 && observedTenant) {
    const observedMode = String(observedTenant.sourceProvider || '').includes('TEST') ? 'test' : 'production';
    const index = tenants.findIndex((tenant) => tenant.environmentMode === observedMode);
    tenants[index] = normalizeTenant({
      ...tenants[index],
      key: observedTenant.tenantKey || tenants[index].key,
      name: observedTenant.tenantName || tenants[index].name,
      tenantDomain: observedTenant.tenantDomain || tenants[index].tenantDomain,
      tenantId: observedTenant.tenantId || '',
      clientId: observedTenant.clientId || '',
      authorityUrl: observedTenant.authorityUrl || '',
      redirectUri: observedTenant.redirectUri || '',
      graphScopes: observedTenant.graphScopes || '',
      sourceProvider: observedTenant.sourceProvider || tenants[index].sourceProvider,
      directorySyncEnabled: Boolean(observedTenant.directorySyncEnabled),
      syncFrequencyHours: Number(observedTenant.syncFrequencyHours || 24),
      defaultRoleCode: observedTenant.defaultRoleCode || 'ENGINEERING'
    }, observedMode);
  }

  const activeMode = String(stored?.activeEnvironmentMode || '').toLowerCase();
  const activeTenantKey = stored?.activeTenantKey;
  const active = tenants.find((tenant) => tenant.key === activeTenantKey)
    || tenants.find((tenant) => tenant.environmentMode === activeMode)
    || tenants.find((tenant) => tenant.environmentMode === runtimeEnvironmentMode())
    || tenants[0];

  return {
    activeTenantKey: active.key,
    activeEnvironmentMode: active.environmentMode,
    tenants,
    mail: {
      providerTarget: stored?.mail?.providerTarget || 'microsoft_graph',
      smtpHost: stored?.mail?.smtpHost || 'smtp.office365.com',
      smtpPort: Number(stored?.mail?.smtpPort || 587),
      senderName: stored?.mail?.senderName || '',
      senderAddress: stored?.mail?.senderAddress || '',
      replyToAddress: stored?.mail?.replyToAddress || '',
      recipientBoundary: stored?.mail?.recipientBoundary || 'test_only'
    }
  };
}

function parseStoredConfiguration(document) {
  const notes = document?.configuration?.notes;
  if (typeof notes !== 'string' || !notes.startsWith(CONFIG_MARKER)) return null;
  try {
    return JSON.parse(notes.slice(CONFIG_MARKER.length));
  } catch {
    return null;
  }
}

function legacyMailFallback(document) {
  const configuration = document?.configuration || {};
  return {
    providerTarget: configuration.providerTarget || 'microsoft_graph',
    smtpHost: configuration.smtpHost || 'smtp.office365.com',
    smtpPort: Number(configuration.smtpPort || 587),
    senderName: configuration.senderName || '',
    senderAddress: configuration.senderAddress || '',
    replyToAddress: configuration.replyToAddress || '',
    recipientBoundary: configuration.recipientBoundary || 'test_only'
  };
}

function Field({ label, help, children }) {
  return (
    <label className="microsoft-integration-field">
      <span>{label}</span>
      {children}
      {help ? <small>{help}</small> : null}
    </label>
  );
}

function connectionStatusLabel(configured) {
  return configured ? 'Configured' : 'Not configured';
}

export default function MicrosoftIntegrationDualConnectionPortal() {
  const [active, setActive] = useState(routeFromHash() === ACTIVE_ROUTE);
  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [secretSaving, setSecretSaving] = useState('');
  const [testing, setTesting] = useState('');
  const [overview, setOverview] = useState(null);
  const [ssoReadiness, setSsoReadiness] = useState(null);
  const [nativeDocument, setNativeDocument] = useState({});
  const [revision, setRevision] = useState(0);
  const [legacy067, setLegacy067] = useState(null);
  const [configuration, setConfiguration] = useState(() => configurationWithBothEnvironments(null, null));
  const [secrets, setSecrets] = useState({ services: '', sso: '' });
  const [message, setMessage] = useState('');
  const [error, setError] = useState('');
  const [testResults, setTestResults] = useState({});

  useEffect(() => {
    const refresh = () => setActive(routeFromHash() === ACTIVE_ROUTE);
    window.addEventListener('hashchange', refresh);
    return () => window.removeEventListener('hashchange', refresh);
  }, []);

  const load = useCallback(async () => {
    if (!active) return;
    setLoading(true);
    setError('');
    try {
      const [overviewBody, ssoBody, module065Body, module067Body] = await Promise.all([
        fetchJson('/api/microsoft-integration/overview'),
        fetchJson('/api/microsoft-integration/sso-readiness').catch(() => null),
        fetchJson('/api/native-administration/065/document'),
        fetchJson('/api/native-administration/067/document').catch(() => null)
      ]);
      const module065Document = module065Body?.document || {};
      const stored = parseStoredConfiguration(module065Document);
      const next = configurationWithBothEnvironments(stored, overviewBody?.activeTenant);
      if (!stored && module067Body?.document) next.mail = legacyMailFallback(module067Body.document);
      setOverview(overviewBody);
      setSsoReadiness(ssoBody);
      setNativeDocument(module065Document);
      setRevision(Number(module065Body?.revision || 0));
      setLegacy067(module067Body);
      setConfiguration(next);
    } catch (loadError) {
      setError(loadError?.message || 'Microsoft Integration could not be loaded.');
    } finally {
      setLoading(false);
    }
  }, [active]);

  useEffect(() => { void load(); }, [load]);

  const activeTenant = useMemo(
    () => configuration.tenants.find((tenant) => tenant.key === configuration.activeTenantKey)
      || configuration.tenants[0],
    [configuration]
  );

  const ssoProfileStatus = useMemo(
    () => ssoReadiness?.profiles?.find((profile) => profile.environmentMode === activeTenant.environmentMode),
    [ssoReadiness, activeTenant.environmentMode]
  );

  const servicesSecretConfigured = Boolean(
    overview?.secretMetadata?.some((item) => item.tenantKey === activeTenant.key)
  );

  const expectedRedirectUri = expectedRedirectFor(activeTenant.environmentMode);
  const redirectMatchesEnvironment = !expectedRedirectUri || activeTenant.sso.redirectUri === expectedRedirectUri;

  function selectEnvironment(environmentMode) {
    setConfiguration((current) => {
      const tenant = current.tenants.find((item) => item.environmentMode === environmentMode) || current.tenants[0];
      return { ...current, activeTenantKey: tenant.key, activeEnvironmentMode: tenant.environmentMode };
    });
    setSecrets({ services: '', sso: '' });
    setTestResults({});
    setMessage('');
    setError('');
  }

  function updateTenant(field, value) {
    setConfiguration((current) => ({
      ...current,
      tenants: current.tenants.map((tenant) => tenant.key === current.activeTenantKey
        ? { ...tenant, [field]: value }
        : tenant)
    }));
  }

  function updateConnection(connection, field, value) {
    setConfiguration((current) => ({
      ...current,
      tenants: current.tenants.map((tenant) => tenant.key === current.activeTenantKey
        ? { ...tenant, [connection]: { ...tenant[connection], [field]: value } }
        : tenant)
    }));
  }

  function updateMail(field, value) {
    setConfiguration((current) => ({ ...current, mail: { ...current.mail, [field]: value } }));
  }

  function serializedConfiguration() {
    return {
      ...configuration,
      activeEnvironmentMode: activeTenant.environmentMode,
      tenants: configuration.tenants.map((tenant) => ({
        ...tenant,
        services: {
          ...tenant.services,
          graphScopes: normalizeServicesScopes(tenant.services.graphScopes)
        },
        // Legacy fields intentionally mirror the services and SSO profiles for Module 010 and existing readers.
        clientId: tenant.services.clientId,
        graphScopes: normalizeServicesScopes(tenant.services.graphScopes),
        authorityUrl: tenant.sso.authorityUrl,
        redirectUri: tenant.sso.redirectUri,
        ssoClientId: tenant.sso.clientId,
        ssoAllowedDomains: tenant.sso.allowedDomains
      }))
    };
  }

  function validateActiveConnection(purpose = 'integration') {
    if (!isGuid(activeTenant.tenantId)) {
      throw new Error('Tenant ID must be the Directory (tenant) ID GUID from the Entra App Registration overview.');
    }
    if ((purpose === 'integration' || purpose === 'services') && !isGuid(activeTenant.services.clientId)) {
      throw new Error('Services application/client ID must be the Application (client) ID GUID used for Graph and Module 010 preview.');
    }
    if ((purpose === 'integration' || purpose === 'sso') && activeTenant.sso.clientId) {
      if (!isGuid(activeTenant.sso.clientId)) throw new Error('SSO application/client ID must be an Application (client) ID GUID.');
      if (!callbackPathMatches(activeTenant.sso.redirectUri)) throw new Error(`SSO Redirect URI must use HTTPS and end with ${SSO_CALLBACK_PATH}.`);
      if (!redirectMatchesEnvironment) throw new Error(`This environment requires the redirect URI ${expectedRedirectUri}. Update the Entra App Registration and use that exact value here.`);
      if (!String(activeTenant.sso.allowedDomains || '').trim()) throw new Error('At least one allowed sign-in domain is required.');
    } else if (purpose === 'sso') {
      throw new Error('Enter the SSO application/client ID before saving or testing the SSO connection.');
    }
    if (purpose === 'services' || purpose === 'integration') {
      const scopes = normalizeServicesScopes(activeTenant.services.graphScopes);
      for (const required of REQUIRED_SERVICES_SCOPES.slice(0, 2)) {
        if (!scopes.toLowerCase().split(/\s+/).includes(required.toLowerCase())) {
          throw new Error(`Microsoft services requires ${required} application permission with tenant admin consent.`);
        }
      }
    }
  }

  async function persistConfiguration() {
    validateActiveConnection('integration');
    const persisted = serializedConfiguration();
    const document = {
      ...nativeDocument,
      configuration: {
        ...(nativeDocument?.configuration || {}),
        applicationId: activeTenant.services.clientId,
        tenantId: activeTenant.tenantId,
        ownerTeam: nativeDocument?.configuration?.ownerTeam || 'Platform Administration',
        notes: `${CONFIG_MARKER}${JSON.stringify(persisted)}`
      }
    };
    const saved = await fetchJson('/api/native-administration/065/document', {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ expectedRevision: revision, document })
    });

    // Module 010 continues to use the Microsoft services/Graph application, never the SSO App Registration.
    await Promise.all([
      fetchJson('/api/admin/azure/config', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          tenantId: activeTenant.tenantId,
          clientId: activeTenant.services.clientId,
          authorityUrl: activeTenant.sso.authorityUrl || `https://login.microsoftonline.com/${activeTenant.tenantId}`,
          redirectUri: activeTenant.sso.redirectUri || '',
          graphScope: normalizeServicesScopes(activeTenant.services.graphScopes),
          syncEnabled: Boolean(activeTenant.directorySyncEnabled),
          defaultRoleCode: activeTenant.defaultRoleCode,
          syncFrequencyHours: Number(activeTenant.syncFrequencyHours || 24)
        })
      }),
      fetchJson('/api/admin/azure/import-settings', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          environmentMode: activeTenant.environmentMode,
          tenantDomain: activeTenant.tenantDomain,
          sourceProvider: activeTenant.sourceProvider,
          tenantName: activeTenant.name,
          importSourceType: 'ALL_USERS',
          graphGroupId: '',
          graphFilter: '',
          defaultRoleCode: activeTenant.defaultRoleCode,
          disableMissingFromSource: false
        })
      })
    ]);

    setNativeDocument(saved.document || document);
    setRevision(Number(saved.revision || revision + 1));
    return saved;
  }

  function ssoRuntimePayload() {
    return {
      environmentMode: activeTenant.environmentMode,
      tenantId: activeTenant.tenantId,
      clientId: activeTenant.sso.clientId,
      redirectUri: activeTenant.sso.redirectUri,
      allowedDomains: activeTenant.sso.allowedDomains
    };
  }

  function servicesRuntimePayload() {
    return {
      environmentMode: activeTenant.environmentMode,
      tenantKey: activeTenant.key,
      tenantId: activeTenant.tenantId,
      clientId: activeTenant.services.clientId,
      graphScopes: normalizeServicesScopes(activeTenant.services.graphScopes),
      senderMailbox: configuration.mail.senderAddress
    };
  }

  function mailRuntimePayload() {
    return {
      environmentMode: activeTenant.environmentMode,
      providerTarget: configuration.mail.providerTarget,
      tenantId: activeTenant.tenantId,
      clientId: activeTenant.services.clientId,
      smtpHost: configuration.mail.smtpHost,
      smtpPort: Number(configuration.mail.smtpPort || 587),
      senderName: configuration.mail.senderName,
      senderAddress: configuration.mail.senderAddress,
      replyToAddress: configuration.mail.replyToAddress,
      recipientBoundary: configuration.mail.recipientBoundary
    };
  }

  async function applyConnectionRuntime(purpose) {
    if (purpose === 'sso') {
      return fetchJson('/api/microsoft-integration/sso-apply-profile', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(ssoRuntimePayload())
      });
    }
    const services = await fetchJson('/api/microsoft-integration/services-apply-profile', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(servicesRuntimePayload())
    });
    const mail = activeTenant.environmentMode === runtimeEnvironmentMode()
      ? await fetchJson('/api/microsoft-integration/mail-runtime', {
          method: 'PUT',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(mailRuntimePayload())
        })
      : null;
    return { services, mail };
  }

  async function saveConfiguration() {
    setSaving(true);
    setMessage('');
    setError('');
    try {
      await persistConfiguration();
      setMessage(`${activeTenant.environmentMode === 'production' ? 'Production' : 'Test'} SSO, Microsoft services, Module 010 preview, and Module 065 mail metadata saved. Runtime activation status appears above.`);
      await load();
    } catch (saveError) {
      setError(saveError?.message || 'Microsoft Integration configuration could not be saved.');
    } finally {
      setSaving(false);
    }
  }

  async function saveSecret(purpose) {
    const value = secrets[purpose]?.trim();
    if (!value) {
      setError(`Enter the ${purpose === 'sso' ? 'SSO' : 'Microsoft services'} client secret before saving.`);
      return;
    }
    setSecretSaving(purpose);
    setMessage('');
    setError('');
    try {
      validateActiveConnection(purpose);
      await persistConfiguration();
      const result = purpose === 'sso'
        ? await fetchJson('/api/microsoft-integration/sso-client-secret', {
          method: 'PUT',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            environmentMode: activeTenant.environmentMode,
            tenantKey: activeTenant.key,
            clientSecret: value
          })
        })
        : await fetchJson('/api/microsoft-integration/client-secret', {
          method: 'PUT',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ tenantKey: activeTenant.key, clientSecret: value })
        });
      await applyConnectionRuntime(purpose);
      setSecrets((current) => ({ ...current, [purpose]: '' }));
      setMessage(`${result.message || 'Client secret saved securely.'} The connection metadata was saved first and the selected runtime profile was reapplied.`);
      await load();
    } catch (secretError) {
      setError(secretError?.message || 'The client secret or connection metadata could not be saved.');
    } finally {
      setSecretSaving('');
    }
  }

  async function testConnection(purpose) {
    setTesting(purpose);
    setMessage('');
    setError('');
    try {
      validateActiveConnection(purpose);
      await persistConfiguration();
      await applyConnectionRuntime(purpose);
      const result = purpose === 'sso'
        ? await fetchJson('/api/microsoft-integration/sso-test', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            environmentMode: activeTenant.environmentMode,
            tenantKey: activeTenant.key,
            tenantId: activeTenant.tenantId,
            clientId: activeTenant.sso.clientId,
            authorityUrl: activeTenant.sso.authorityUrl,
            redirectUri: activeTenant.sso.redirectUri
          })
        })
        : await fetchJson('/api/microsoft-integration/test-connection', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            tenantKey: activeTenant.key,
            tenantId: activeTenant.tenantId,
            clientId: activeTenant.services.clientId,
            senderMailbox: configuration.mail.senderAddress
          })
        });
      setTestResults((current) => ({ ...current, [purpose]: result }));
      setMessage(result.message || 'Connection test completed.');
    } catch (testError) {
      setError(testError?.message || 'Connection test failed.');
    } finally {
      setTesting('');
    }
  }

  if (!active) return null;

  return (
    <section className="microsoft-integration-portal projectpulse-module-standard" data-module="065">
      <div className="microsoft-integration-heading">
        <div>
          <p className="eyebrow">MODULE 065</p>
          <h1>Microsoft Integration</h1>
          <p className="section-copy">
            Manage separate SSO and Microsoft services connections for Test and Production. Module 010 continues using the services connection for Entra preview and import.
          </p>
        </div>
        <div className="microsoft-integration-actions">
          <button type="button" className="secondary-action" onClick={() => void load()} disabled={loading || saving}>Refresh</button>
          <button type="button" className="primary-action" onClick={() => void saveConfiguration()} disabled={loading || saving}>{saving ? 'Saving…' : 'Save integration'}</button>
        </div>
      </div>

      {error ? <div className="microsoft-integration-banner error">{error}</div> : null}
      {message ? <div className="microsoft-integration-banner success">{message}</div> : null}
      {loading ? <div className="microsoft-integration-empty">Loading Microsoft Integration…</div> : null}

      {!loading ? (
        <>
          <div className="microsoft-integration-banner">
            Existing Test Graph/calendar/identity settings are carried forward as the Microsoft services connection. SSO is stored separately. Module 065 is the authoritative source for Microsoft 365 and SMTP mail runtime settings.
          </div>

          <div className="microsoft-environment-switcher" role="tablist" aria-label="Microsoft environment">
            {ENVIRONMENTS.map((environmentMode) => (
              <button
                type="button"
                key={environmentMode}
                className={activeTenant.environmentMode === environmentMode ? 'active' : ''}
                onClick={() => selectEnvironment(environmentMode)}
              >
                {environmentMode === 'production' ? 'Production' : 'Test'}
              </button>
            ))}
          </div>

          <div className="microsoft-integration-grid">
            <article className="microsoft-integration-card wide">
              <div className="microsoft-integration-card-heading">
                <div>
                  <p className="eyebrow">{activeTenant.environmentMode.toUpperCase()} ENVIRONMENT</p>
                  <h2>{activeTenant.name}</h2>
                </div>
                <span className="microsoft-connection-badge">Two independent connections</span>
              </div>
              <div className="microsoft-integration-form-grid">
                <Field label="Environment name"><input value={activeTenant.name} onChange={(event) => updateTenant('name', event.target.value)} /></Field>
                <Field label="Tenant key" help="Stable internal key used to find the correct encrypted services secret."><input value={activeTenant.key} disabled /></Field>
                <Field label="Tenant domain"><input value={activeTenant.tenantDomain} onChange={(event) => updateTenant('tenantDomain', event.target.value)} /></Field>
                <Field label="Tenant ID" help="Directory (tenant) ID from the Entra App Registration overview."><input value={activeTenant.tenantId} onChange={(event) => updateTenant('tenantId', event.target.value.trim())} /></Field>
                <Field label="Directory source"><input value={activeTenant.sourceProvider} disabled /></Field>
                <Field label="Default imported-user role"><input value={activeTenant.defaultRoleCode} onChange={(event) => updateTenant('defaultRoleCode', event.target.value.toUpperCase())} /></Field>
              </div>
              <div className="microsoft-compatibility-grid">
                <div><strong>Environment</strong><span>{activeTenant.environmentMode === 'production' ? 'Production' : 'Test'}</span></div>
                <div><strong>Tenant key</strong><span>{activeTenant.key || 'Not configured'}</span></div>
                <div><strong>Tenant GUID</strong><span>{activeTenant.tenantId || 'Not configured'}</span></div>
                <div><strong>SSO client GUID</strong><span>{activeTenant.sso.clientId || 'Not configured'}</span></div>
                <div><strong>Redirect URI</strong><span>{activeTenant.sso.redirectUri || 'Not configured'}</span></div>
              </div>
            </article>

            <article className="microsoft-integration-card microsoft-connection-card">
              <p className="eyebrow">CONNECTION 1 · APP REGISTRATION</p>
              <h2>Microsoft Entra SSO</h2>
              <p className="section-copy">Used only for interactive sign-in. It does not replace the Graph/calendar/identity application.</p>
              <Field label="SSO application / client ID" help="Application (client) ID from the SSO App Registration overview."><input value={activeTenant.sso.clientId} onChange={(event) => updateConnection('sso', 'clientId', event.target.value.trim())} /></Field>
              <div className="microsoft-integration-actions">
                <button type="button" className="secondary-action" disabled={!activeTenant.services.clientId} onClick={() => updateConnection('sso', 'clientId', activeTenant.services.clientId)}>Use services app ID</button>
              </div>
              <Field label="Authority URL"><input value={activeTenant.sso.authorityUrl} onChange={(event) => updateConnection('sso', 'authorityUrl', event.target.value)} placeholder={`https://login.microsoftonline.com/${activeTenant.tenantId || '<tenant-id>'}`} /></Field>
              <Field label="Redirect URI" help={expectedRedirectUri ? `This environment must use ${expectedRedirectUri}` : `Must end with ${SSO_CALLBACK_PATH}.`}><input value={activeTenant.sso.redirectUri} onChange={(event) => updateConnection('sso', 'redirectUri', event.target.value.trim())} /></Field>
              {expectedRedirectUri ? <div className="microsoft-integration-actions"><button type="button" className="secondary-action" onClick={() => updateConnection('sso', 'redirectUri', expectedRedirectUri)}>Use current callback</button></div> : null}
              <Field label="Allowed sign-in domains"><input value={activeTenant.sso.allowedDomains} onChange={(event) => updateConnection('sso', 'allowedDomains', event.target.value)} /></Field>
              <Field label="New SSO client secret" help="Save SSO connection stores the current client ID and redirect metadata before storing the write-only secret.">
                <input type="password" autoComplete="new-password" value={secrets.sso} onChange={(event) => setSecrets((current) => ({ ...current, sso: event.target.value }))} />
              </Field>
              <div className="microsoft-integration-actions">
                <button type="button" className="primary-action" onClick={() => void saveSecret('sso')} disabled={secretSaving === 'sso'}>{secretSaving === 'sso' ? 'Saving…' : 'Save SSO connection'}</button>
                <button type="button" className="secondary-action" onClick={() => void testConnection('sso')} disabled={testing === 'sso'}>{testing === 'sso' ? 'Testing…' : 'Test SSO readiness'}</button>
              </div>
              <div className="microsoft-integration-fact"><strong>Secret</strong><span>{connectionStatusLabel(Boolean(ssoProfileStatus?.secretConfigured))}</span></div>
              <div className="microsoft-integration-fact"><strong>Redirect match</strong><span>{redirectMatchesEnvironment ? 'Matches current environment' : 'Update required'}</span></div>
              <div className="microsoft-integration-fact"><strong>Required delegated permissions</strong><span>openid · profile · email · User.Read</span></div>
              <div className="microsoft-integration-fact"><strong>Final validation</strong><span>Interactive sign-in required</span></div>
              {testResults.sso ? <pre className="microsoft-integration-result">{JSON.stringify({ status: testResults.sso.status, discoveryReady: testResults.sso.discoveryReady, secretConfigured: testResults.sso.secretConfigured, interactiveSignInRequired: testResults.sso.interactiveSignInRequired }, null, 2)}</pre> : null}
            </article>

            <article className="microsoft-integration-card microsoft-connection-card">
              <p className="eyebrow">CONNECTION 2 · ENTERPRISE APPLICATION</p>
              <h2>Microsoft services and Graph</h2>
              <p className="section-copy">Used by Module 010 import, Module 057 calendar, Module 062 identity/profile/presence, and Microsoft 365 services.</p>
              <Field label="Services application / client ID" help="Application (client) ID whose application permissions have tenant admin consent."><input value={activeTenant.services.clientId} onChange={(event) => updateConnection('services', 'clientId', event.target.value.trim())} /></Field>
              <Field label="Graph application permissions" help="Module 010 needs Directory.Read.All and User.Read.All. Graph mail needs Mail.Send."><input value={activeTenant.services.graphScopes} onChange={(event) => updateConnection('services', 'graphScopes', normalizeServicesScopes(event.target.value))} /></Field>
              <Field label="New services client secret" help="The current Test secret and environment contract are preserved.">
                <input type="password" autoComplete="new-password" value={secrets.services} onChange={(event) => setSecrets((current) => ({ ...current, services: event.target.value }))} />
              </Field>
              <div className="microsoft-integration-actions">
                <button type="button" className="primary-action" onClick={() => void saveSecret('services')} disabled={secretSaving === 'services'}>{secretSaving === 'services' ? 'Saving…' : 'Save services connection'}</button>
                <button type="button" className="secondary-action" onClick={() => void testConnection('services')} disabled={testing === 'services'}>{testing === 'services' ? 'Testing…' : 'Test Graph connection'}</button>
              </div>
              <div className="microsoft-integration-fact"><strong>Secret</strong><span>{connectionStatusLabel(servicesSecretConfigured)}</span></div>
              <div className="microsoft-integration-fact"><strong>Required permissions</strong><span>Directory.Read.All · User.Read.All · Mail.Send</span></div>
              <div className="microsoft-integration-fact"><strong>Module 010 preview source</strong><span>Module 065 services profile</span></div>
              {testResults.services ? <pre className="microsoft-integration-result">{JSON.stringify({ status: testResults.services.status, directoryRead: testResults.services.directoryRead, senderMailbox: testResults.services.senderMailbox }, null, 2)}</pre> : null}
            </article>

            <article className="microsoft-integration-card wide">
              <p className="eyebrow">Microsoft 365 / SMTP</p>
              <h2>Sender and transport configuration</h2>
              <p className="section-copy">Module 065 is the authoritative mail source for every ProjectPulse email path. Microsoft Graph uses the services application and Mail.Send. SMTP host and port are active only when Microsoft 365 SMTP relay is selected.</p>
              <div className="microsoft-integration-form-grid">
                <Field label="Provider"><select value={configuration.mail.providerTarget} onChange={(event) => updateMail('providerTarget', event.target.value)}><option value="microsoft_graph">Microsoft Graph</option><option value="smtp_relay">Microsoft 365 SMTP relay</option><option value="locked">Locked</option></select></Field>
                <Field label="SMTP host"><input value={configuration.mail.smtpHost} onChange={(event) => updateMail('smtpHost', event.target.value)} /></Field>
                <Field label="SMTP port"><input type="number" value={configuration.mail.smtpPort} onChange={(event) => updateMail('smtpPort', Number(event.target.value))} /></Field>
                <Field label="Sender name"><input value={configuration.mail.senderName} onChange={(event) => updateMail('senderName', event.target.value)} /></Field>
                <Field label="Sender mailbox"><input type="email" value={configuration.mail.senderAddress} onChange={(event) => updateMail('senderAddress', event.target.value)} /></Field>
                <Field label="Reply-to address"><input type="email" value={configuration.mail.replyToAddress} onChange={(event) => updateMail('replyToAddress', event.target.value)} /></Field>
                <Field label="Recipient boundary"><select value={configuration.mail.recipientBoundary} onChange={(event) => updateMail('recipientBoundary', event.target.value)}><option value="test_only">Test only</option><option value="production_governed">Production governed</option><option value="locked">Locked</option></select></Field>
              </div>
              <div className="microsoft-integration-fact"><strong>Authoritative source</strong><span>Module 065 Microsoft Integration</span></div>
              <div className="microsoft-integration-fact"><strong>Current transport</strong><span>{configuration.mail.providerTarget === 'microsoft_graph' ? 'Microsoft Graph · SMTP fields inactive' : configuration.mail.providerTarget === 'smtp_relay' ? 'Microsoft 365 SMTP relay' : 'Locked'}</span></div>
            </article>

            <article className="microsoft-integration-card wide">
              <p className="eyebrow">Compatibility contract</p>
              <h2>Existing integrations remain connected</h2>
              <div className="microsoft-compatibility-grid">
                <div><strong>Module 010</strong><span>Uses the Module 065 services/Graph connection for preview and import.</span></div>
                <div><strong>Module 057</strong><span>Uses the active Module 065 services runtime for calendar and presence.</span></div>
                <div><strong>Module 062</strong><span>Uses explicit Test and Production services credentials by domain.</span></div>
                <div><strong>All email senders</strong><span>Use the provider, sender, boundary, and SMTP projection selected in Module 065.</span></div>
                <div><strong>Module 067</strong><span>{legacy067?.document ? 'Saved mail configuration preserved.' : 'No legacy document observed.'}</span></div>
              </div>
            </article>
          </div>
        </>
      ) : null}
    </section>
  );
}
