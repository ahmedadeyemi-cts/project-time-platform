import { useCallback, useEffect, useMemo, useState } from 'react';
import './microsoft-integration-portal.css';
import './microsoft-integration-dual-connections.css';

const ACTIVE_ROUTE = 'entra-secret-administration';
const CONFIG_MARKER = 'PROJECTPULSE_MICROSOFT_INTEGRATION_JSON:';
const ENVIRONMENTS = ['test', 'production'];
const SSO_CALLBACK_PATH = '/api/auth/sso/callback';
const REQUIRED_SERVICES_SCOPES = ['Directory.Read.All', 'User.Read.All', 'Mail.Send'];
const DEFAULT_SYNC_HOURS = 24;

function routeFromHash() {
  return window.location.hash.replace(/^#/, '').split('?')[0].trim();
}

async function readJson(response) {
  const text = await response.text();
  if (!text.trim()) return {};
  try { return JSON.parse(text); } catch { return { status: 'invalid_json_response' }; }
}

async function fetchJson(url, init) {
  const response = await fetch(url, init);
  const body = await readJson(response);
  if (!response.ok) throw new Error(body?.message || body?.status || `Request failed with HTTP ${response.status}.`);
  return body;
}

function runtimeEnvironmentMode() {
  const host = window.location.hostname.toLowerCase();
  if (host.includes('-test.') || host.endsWith('.onenecklab.com') || host === 'localhost' || host === '127.0.0.1') return 'test';
  return 'production';
}

function environmentLabel(value) {
  return value === 'production' ? 'Production' : 'Test';
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

function defaultMail(environmentMode) {
  return {
    providerTarget: 'microsoft_graph',
    smtpHost: 'smtp.office365.com',
    smtpPort: 587,
    senderName: environmentMode === 'production' ? 'US Signal Pulse' : 'US Signal Pulse Test',
    senderAddress: '',
    replyToAddress: '',
    recipientBoundary: environmentMode === 'production' ? 'locked' : 'test_only'
  };
}

function normalizeMail(raw, environmentMode, legacyMail = null) {
  const defaults = defaultMail(environmentMode);
  const source = raw || legacyMail || {};
  return {
    ...defaults,
    ...source,
    providerTarget: source.providerTarget || defaults.providerTarget,
    smtpHost: source.smtpHost || defaults.smtpHost,
    smtpPort: Number(source.smtpPort || defaults.smtpPort),
    senderName: source.senderName || defaults.senderName,
    senderAddress: source.senderAddress || '',
    replyToAddress: source.replyToAddress || '',
    recipientBoundary: source.recipientBoundary || defaults.recipientBoundary
  };
}

function environmentDefaults(environmentMode) {
  const production = environmentMode === 'production';
  return {
    key: production ? 'ussignal' : 'onenecklab',
    name: production ? 'US Signal Production' : 'OneNeck Lab Test',
    environmentMode,
    tenantDomain: production ? 'ussignal.com' : 'onenecklab.com',
    tenantId: '',
    sourceProvider: production ? 'ENTRA_ID' : 'ENTRA_ID_TEST',
    directorySyncEnabled: false,
    syncFrequencyHours: DEFAULT_SYNC_HOURS,
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
    },
    mail: defaultMail(environmentMode)
  };
}

function normalizeTenant(raw, environmentMode, legacyMail = null) {
  const defaults = environmentDefaults(environmentMode);
  const source = raw || {};
  const sso = source.sso || source.ssoConnection || {};
  const services = source.services || source.servicesConnection || {};
  const tenantId = String(source.tenantId || '').trim();
  const authorityUrl = sso.authorityUrl || sso.authority || source.authorityUrl || (isGuid(tenantId) ? `https://login.microsoftonline.com/${tenantId}` : '');
  return {
    ...defaults,
    ...source,
    key: source.key || source.tenantKey || defaults.key,
    name: source.name || source.tenantName || defaults.name,
    environmentMode,
    tenantDomain: source.tenantDomain || defaults.tenantDomain,
    tenantId,
    sourceProvider: source.sourceProvider || defaults.sourceProvider,
    directorySyncEnabled: Boolean(source.directorySyncEnabled ?? source.syncEnabled ?? false),
    syncFrequencyHours: Math.max(1, Number(source.syncFrequencyHours || DEFAULT_SYNC_HOURS)),
    defaultRoleCode: String(source.defaultRoleCode || 'ENGINEERING').toUpperCase(),
    sso: {
      ...defaults.sso,
      ...sso,
      clientId: sso.clientId || sso.applicationId || source.ssoClientId || '',
      authorityUrl,
      redirectUri: redirectWithCurrentEnvironment(sso.redirectUri || sso.callbackUri || source.redirectUri || '', environmentMode),
      allowedDomains: sso.allowedDomains || source.ssoAllowedDomains || defaults.sso.allowedDomains
    },
    services: {
      ...defaults.services,
      ...services,
      clientId: services.clientId || services.applicationId || source.serviceClientId || source.clientId || '',
      graphScopes: normalizeServicesScopes(services.graphScopes || services.scopes || source.graphScopes || source.graphScope || defaults.services.graphScopes)
    },
    mail: normalizeMail(source.mail, environmentMode, legacyMail)
  };
}

function configurationWithBothEnvironments(stored, observedTenant, legacy067Mail = null) {
  const rawTenants = Array.isArray(stored?.tenants) ? stored.tenants : [];
  const legacyMail = stored?.mail || legacy067Mail || null;
  const tenants = ENVIRONMENTS.map((environmentMode) => {
    const existing = rawTenants.find((tenant) => {
      const mode = String(tenant?.environmentMode || '').toLowerCase();
      return mode === environmentMode
        || (environmentMode === 'test' && String(tenant?.sourceProvider || '').includes('TEST'))
        || (environmentMode === 'production' && mode === 'prod');
    });
    return normalizeTenant(existing, environmentMode, legacyMail);
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
      syncFrequencyHours: Number(observedTenant.syncFrequencyHours || DEFAULT_SYNC_HOURS),
      defaultRoleCode: observedTenant.defaultRoleCode || 'ENGINEERING'
    }, observedMode, legacyMail);
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
    tenants
  };
}

function parseStoredConfiguration(document) {
  const notes = document?.configuration?.notes;
  if (typeof notes !== 'string' || !notes.startsWith(CONFIG_MARKER)) return null;
  try { return JSON.parse(notes.slice(CONFIG_MARKER.length)); } catch { return null; }
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

function Field({ label, help, children, className = '' }) {
  return (
    <label className={`microsoft-integration-field ${className}`.trim()}>
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
      const legacyMail = !stored && module067Body?.document ? legacyMailFallback(module067Body.document) : null;
      setOverview(overviewBody);
      setSsoReadiness(ssoBody);
      setNativeDocument(module065Document);
      setRevision(Number(module065Body?.revision || 0));
      setLegacy067(module067Body);
      setConfiguration(configurationWithBothEnvironments(stored, overviewBody?.activeTenant, legacyMail));
    } catch (loadError) {
      setError(loadError?.message || 'Microsoft Integration could not be loaded.');
    } finally {
      setLoading(false);
    }
  }, [active]);

  useEffect(() => { void load(); }, [load]);

  const activeTenant = useMemo(
    () => configuration.tenants.find((tenant) => tenant.key === configuration.activeTenantKey) || configuration.tenants[0],
    [configuration]
  );
  const activeMail = activeTenant.mail;
  const expectedRedirectUri = expectedRedirectFor(activeTenant.environmentMode);
  const redirectMatchesEnvironment = !expectedRedirectUri || activeTenant.sso.redirectUri === expectedRedirectUri;
  const ssoProfileStatus = ssoReadiness?.profiles?.find((profile) => profile.environmentMode === activeTenant.environmentMode);
  const servicesSecretConfigured = Boolean(overview?.secretMetadata?.some((item) => item.tenantKey === activeTenant.key));
  const runtimeEnvironment = runtimeEnvironmentMode();
  const selectedEnvironmentIsRuntime = activeTenant.environmentMode === runtimeEnvironment;

  function selectEnvironment(environmentMode) {
    setConfiguration((current) => {
      const tenant = current.tenants.find((item) => item.environmentMode === environmentMode) || current.tenants[0];
      return { ...current, activeTenantKey: tenant.key, activeEnvironmentMode: tenant.environmentMode };
    });
    setSecrets({ services: '', sso: '' });
    setTestResults({});
    setMessage('');
    setError('');
    window.dispatchEvent(new CustomEvent('projectpulse:microsoft-environment-changed', { detail: { environmentMode } }));
  }

  function updateTenant(field, value) {
    setConfiguration((current) => ({
      ...current,
      tenants: current.tenants.map((tenant) => tenant.key === current.activeTenantKey ? { ...tenant, [field]: value } : tenant)
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
    setConfiguration((current) => ({
      ...current,
      tenants: current.tenants.map((tenant) => tenant.key === current.activeTenantKey
        ? { ...tenant, mail: { ...tenant.mail, [field]: value } }
        : tenant)
    }));
  }

  function serializedConfiguration() {
    const runtimeTenant = configuration.tenants.find((tenant) => tenant.environmentMode === runtimeEnvironment) || activeTenant;
    return {
      ...configuration,
      activeEnvironmentMode: activeTenant.environmentMode,
      tenants: configuration.tenants.map((tenant) => ({
        ...tenant,
        syncMode: tenant.directorySyncEnabled ? 'automatic' : 'manual',
        services: { ...tenant.services, graphScopes: normalizeServicesScopes(tenant.services.graphScopes) },
        clientId: tenant.services.clientId,
        graphScopes: normalizeServicesScopes(tenant.services.graphScopes),
        authorityUrl: tenant.sso.authorityUrl,
        redirectUri: tenant.sso.redirectUri,
        ssoClientId: tenant.sso.clientId,
        ssoAllowedDomains: tenant.sso.allowedDomains,
        mail: { ...tenant.mail }
      })),
      // Legacy projection retained for existing readers. The environment-specific tenant.mail is authoritative.
      mail: { ...runtimeTenant.mail },
      mailConfigurationScope: 'per_environment'
    };
  }

  function validateActiveConnection(purpose = 'integration') {
    if (!isGuid(activeTenant.tenantId)) throw new Error('Tenant ID must be the Directory (tenant) ID GUID from the Entra App Registration overview.');
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
      const scopes = normalizeServicesScopes(activeTenant.services.graphScopes).toLowerCase().split(/\s+/);
      for (const required of REQUIRED_SERVICES_SCOPES.slice(0, 2)) {
        if (!scopes.includes(required.toLowerCase())) throw new Error(`Microsoft services requires ${required} application permission with tenant admin consent.`);
      }
    }
    if (activeTenant.directorySyncEnabled && (!Number.isFinite(Number(activeTenant.syncFrequencyHours)) || Number(activeTenant.syncFrequencyHours) < 1 || Number(activeTenant.syncFrequencyHours) > 168)) {
      throw new Error('Automatic directory synchronization frequency must be between 1 and 168 hours.');
    }
  }

  async function persistConfiguration(purpose = 'integration') {
    validateActiveConnection(purpose);
    const persisted = serializedConfiguration();
    const document = {
      ...nativeDocument,
      configuration: {
        ...(nativeDocument?.configuration || {}),
        applicationId: activeTenant.services.clientId || nativeDocument?.configuration?.applicationId || '',
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

    if (purpose !== 'sso') {
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
            syncFrequencyHours: Number(activeTenant.syncFrequencyHours || DEFAULT_SYNC_HOURS)
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
    }

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
      senderMailbox: activeMail.senderAddress
    };
  }

  function mailRuntimePayload() {
    return {
      environmentMode: activeTenant.environmentMode,
      providerTarget: activeMail.providerTarget,
      tenantId: activeTenant.tenantId,
      clientId: activeTenant.services.clientId,
      smtpHost: activeMail.smtpHost,
      smtpPort: Number(activeMail.smtpPort || 587),
      senderName: activeMail.senderName,
      senderAddress: activeMail.senderAddress,
      replyToAddress: activeMail.replyToAddress,
      recipientBoundary: activeMail.recipientBoundary
    };
  }

  async function applyConnectionRuntime(purpose) {
    if (purpose === 'sso') {
      return fetchJson('/api/microsoft-integration/sso-apply-profile', {
        method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(ssoRuntimePayload())
      });
    }
    const services = await fetchJson('/api/microsoft-integration/services-apply-profile', {
      method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(servicesRuntimePayload())
    });
    const mail = selectedEnvironmentIsRuntime
      ? await fetchJson('/api/microsoft-integration/mail-runtime', {
          method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(mailRuntimePayload())
        })
      : null;
    return { services, mail };
  }

  async function saveConfiguration() {
    setSaving(true);
    setMessage('');
    setError('');
    try {
      await persistConfiguration('integration');
      if (selectedEnvironmentIsRuntime) await applyConnectionRuntime('services');
      setMessage(`${environmentLabel(activeTenant.environmentMode)} SSO, Microsoft services, directory sync, and mail settings saved.${selectedEnvironmentIsRuntime ? ' The running environment was activated immediately.' : ' This profile will activate in its matching environment.'}`);
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
      await persistConfiguration(purpose);
      const result = purpose === 'sso'
        ? await fetchJson('/api/microsoft-integration/sso-client-secret', {
            method: 'PUT', headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ environmentMode: activeTenant.environmentMode, tenantKey: activeTenant.key, clientSecret: value })
          })
        : await fetchJson('/api/microsoft-integration/client-secret', {
            method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ tenantKey: activeTenant.key, clientSecret: value })
          });
      await applyConnectionRuntime(purpose);
      setSecrets((current) => ({ ...current, [purpose]: '' }));
      setMessage(`${result.message || 'Client secret saved securely.'} The selected ${environmentLabel(activeTenant.environmentMode)} profile was reapplied.`);
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
      await persistConfiguration(purpose);
      await applyConnectionRuntime(purpose);
      const result = purpose === 'sso'
        ? await fetchJson('/api/microsoft-integration/sso-test', {
            method: 'POST', headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ environmentMode: activeTenant.environmentMode, tenantKey: activeTenant.key, tenantId: activeTenant.tenantId, clientId: activeTenant.sso.clientId, authorityUrl: activeTenant.sso.authorityUrl, redirectUri: activeTenant.sso.redirectUri })
          })
        : await fetchJson('/api/microsoft-integration/test-connection', {
            method: 'POST', headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ tenantKey: activeTenant.key, tenantId: activeTenant.tenantId, clientId: activeTenant.services.clientId, senderMailbox: activeMail.senderAddress })
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

  const syncMode = activeTenant.directorySyncEnabled ? 'automatic' : 'manual';
  const providerDescription = activeMail.providerTarget === 'microsoft_graph'
    ? `Microsoft Graph is configured for ${environmentLabel(activeTenant.environmentMode)}. SMTP metadata is preserved but inactive because Graph is selected.`
    : activeMail.providerTarget === 'smtp_relay'
      ? `Microsoft 365 SMTP relay is configured for ${environmentLabel(activeTenant.environmentMode)}. Graph directory services remain available, but Graph mail is not the selected transport.`
      : `${environmentLabel(activeTenant.environmentMode)} mail delivery is locked.`;

  return (
    <section className="microsoft-integration-portal projectpulse-module-standard" data-module="065">
      <div className="microsoft-integration-heading">
        <div>
          <p className="eyebrow">MODULE 065</p>
          <h1>Microsoft Integration</h1>
          <p className="section-copy">Manage separate SSO, Microsoft services, directory synchronization, and mail settings for Test and Production. Module 010 uses the services connection for Entra preview and import.</p>
        </div>
        <div className="microsoft-integration-actions">
          <button type="button" className="secondary-action" onClick={() => void load()} disabled={loading || saving}>Refresh</button>
          <button type="button" className="primary-action" onClick={() => void saveConfiguration()} disabled={loading || saving}>{saving ? 'Saving…' : `Save ${environmentLabel(activeTenant.environmentMode)} integration`}</button>
        </div>
      </div>

      {error ? <div className="microsoft-integration-banner error">{error}</div> : null}
      {message ? <div className="microsoft-integration-banner success">{message}</div> : null}
      {loading ? <div className="microsoft-integration-empty">Loading Microsoft Integration…</div> : null}

      {!loading ? <>
        <div className="microsoft-integration-banner">Test and Production maintain independent SSO, services, directory-sync, sender, provider, SMTP, and recipient-boundary settings. Module 065 is the authoritative source for every Pulse Microsoft and mail runtime.</div>
        <div className="microsoft-environment-switcher" role="tablist" aria-label="Microsoft environment">
          {ENVIRONMENTS.map((environmentMode) => <button type="button" key={environmentMode} className={activeTenant.environmentMode === environmentMode ? 'active' : ''} onClick={() => selectEnvironment(environmentMode)}>{environmentLabel(environmentMode)}</button>)}
        </div>

        <div className="microsoft-integration-grid">
          <article className="microsoft-integration-card wide">
            <div className="microsoft-integration-card-heading"><div><p className="eyebrow">{activeTenant.environmentMode.toUpperCase()} ENVIRONMENT</p><h2>{activeTenant.name}</h2></div><span className="microsoft-connection-badge">{selectedEnvironmentIsRuntime ? 'Running environment' : 'Saved environment profile'}</span></div>
            <div className="microsoft-integration-form-grid">
              <Field label="Environment name"><input value={activeTenant.name} onChange={(event) => updateTenant('name', event.target.value)} /></Field>
              <Field label="Tenant key" help="Stable internal key used to find the correct encrypted services secret."><input value={activeTenant.key} disabled /></Field>
              <Field label="Tenant domain"><input value={activeTenant.tenantDomain} onChange={(event) => updateTenant('tenantDomain', event.target.value)} /></Field>
              <Field label="Tenant ID" help="Directory (tenant) ID from the Entra App Registration overview."><input value={activeTenant.tenantId} onChange={(event) => updateTenant('tenantId', event.target.value.trim())} /></Field>
              <Field label="Directory source"><input value={activeTenant.sourceProvider} disabled /></Field>
              <Field label="Default imported-user role"><input value={activeTenant.defaultRoleCode} onChange={(event) => updateTenant('defaultRoleCode', event.target.value.toUpperCase())} /></Field>
            </div>
            <div className="microsoft-compatibility-grid">
              <div><strong>Environment</strong><span>{environmentLabel(activeTenant.environmentMode)}</span></div>
              <div><strong>Tenant key</strong><span>{activeTenant.key || 'Not configured'}</span></div>
              <div><strong>Tenant GUID</strong><span>{activeTenant.tenantId || 'Not configured'}</span></div>
              <div><strong>SSO client GUID</strong><span>{activeTenant.sso.clientId || 'Not configured'}</span></div>
              <div><strong>Redirect URI</strong><span>{activeTenant.sso.redirectUri || 'Not configured'}</span></div>
            </div>
          </article>

          <article className="microsoft-integration-card wide microsoft-directory-sync-card">
            <p className="eyebrow">MODULE 010 DIRECTORY SYNCHRONIZATION</p>
            <h2>{environmentLabel(activeTenant.environmentMode)} Entra import schedule</h2>
            <p className="section-copy">Manual mode imports only when an administrator selects Sync Now or imports selected users. Automatic mode retains the same controls and schedules recurring synchronization using the saved frequency.</p>
            <div className="microsoft-integration-form-grid">
              <Field label="Synchronization mode"><select value={syncMode} onChange={(event) => updateTenant('directorySyncEnabled', event.target.value === 'automatic')}><option value="manual">Manual only</option><option value="automatic">Automatic and manual</option></select></Field>
              <Field label="Automatic frequency" help="Choose any interval from 1 to 168 hours. Common values are 12 or 24 hours."><div className="microsoft-sync-frequency-control"><input type="number" min="1" max="168" disabled={!activeTenant.directorySyncEnabled} value={activeTenant.syncFrequencyHours} onChange={(event) => updateTenant('syncFrequencyHours', Math.max(1, Math.min(168, Number(event.target.value) || DEFAULT_SYNC_HOURS)))} /><span>hours</span></div></Field>
              <Field label="Current schedule"><input value={activeTenant.directorySyncEnabled ? `Automatic every ${activeTenant.syncFrequencyHours} hour(s)` : 'Manual only'} disabled /></Field>
            </div>
            <div className="microsoft-integration-actions"><a className="secondary-action" href="#azure-admin">Open Module 010 preview and import</a><a className="primary-action" href="#azure-admin">Sync Now in Module 010</a></div>
          </article>

          <article className="microsoft-integration-card microsoft-connection-card">
            <p className="eyebrow">CONNECTION 1 · APP REGISTRATION</p><h2>Microsoft Entra SSO</h2><p className="section-copy">Used only for interactive sign-in. It does not replace the Graph/calendar/identity application.</p>
            <Field label="SSO application / client ID" help="Application (client) ID from the SSO App Registration overview."><input value={activeTenant.sso.clientId} onChange={(event) => updateConnection('sso', 'clientId', event.target.value.trim())} /></Field>
            <div className="microsoft-integration-actions"><button type="button" className="secondary-action" disabled={!activeTenant.services.clientId} onClick={() => updateConnection('sso', 'clientId', activeTenant.services.clientId)}>Use services app ID</button></div>
            <Field label="Authority URL"><input value={activeTenant.sso.authorityUrl} onChange={(event) => updateConnection('sso', 'authorityUrl', event.target.value)} placeholder={`https://login.microsoftonline.com/${activeTenant.tenantId || '<tenant-id>'}`} /></Field>
            <Field label="Redirect URI" help={expectedRedirectUri ? `This environment must use ${expectedRedirectUri}` : `Must end with ${SSO_CALLBACK_PATH}.`}><input value={activeTenant.sso.redirectUri} onChange={(event) => updateConnection('sso', 'redirectUri', event.target.value.trim())} /></Field>
            {expectedRedirectUri ? <div className="microsoft-integration-actions"><button type="button" className="secondary-action" onClick={() => updateConnection('sso', 'redirectUri', expectedRedirectUri)}>Use current callback</button></div> : null}
            <Field label="Allowed sign-in domains"><input value={activeTenant.sso.allowedDomains} onChange={(event) => updateConnection('sso', 'allowedDomains', event.target.value)} /></Field>
            <Field label="New SSO client secret" help="The write-only secret is stored separately for Test and Production."><input type="password" autoComplete="new-password" value={secrets.sso} onChange={(event) => setSecrets((current) => ({ ...current, sso: event.target.value }))} /></Field>
            <div className="microsoft-integration-actions"><button type="button" className="primary-action" onClick={() => void saveSecret('sso')} disabled={secretSaving === 'sso'}>{secretSaving === 'sso' ? 'Saving…' : 'Save SSO connection'}</button><button type="button" className="secondary-action" onClick={() => void testConnection('sso')} disabled={testing === 'sso'}>{testing === 'sso' ? 'Testing…' : 'Test SSO readiness'}</button></div>
            <div className="microsoft-integration-fact"><strong>Secret</strong><span>{connectionStatusLabel(Boolean(ssoProfileStatus?.secretConfigured))}</span></div>
            <div className="microsoft-integration-fact"><strong>Redirect match</strong><span>{redirectMatchesEnvironment ? 'Matches current environment' : 'Update required'}</span></div>
            <div className="microsoft-integration-fact"><strong>Required delegated permissions</strong><span>openid · profile · email · User.Read</span></div>
            <div className="microsoft-integration-fact"><strong>Final validation</strong><span>Interactive sign-in required</span></div>
            {testResults.sso ? <pre className="microsoft-integration-result">{JSON.stringify({ status: testResults.sso.status, discoveryReady: testResults.sso.discoveryReady, secretConfigured: testResults.sso.secretConfigured, interactiveSignInRequired: testResults.sso.interactiveSignInRequired }, null, 2)}</pre> : null}
          </article>

          <article className="microsoft-integration-card microsoft-connection-card">
            <p className="eyebrow">CONNECTION 2 · ENTERPRISE APPLICATION</p><h2>Microsoft services and Graph</h2><p className="section-copy">Used by Module 010 import, Module 057 calendar, Module 062 identity/profile/presence, and Microsoft 365 services.</p>
            <Field label="Services application / client ID" help="Application (client) ID whose application permissions have tenant admin consent."><input value={activeTenant.services.clientId} onChange={(event) => updateConnection('services', 'clientId', event.target.value.trim())} /></Field>
            <Field label="Graph application permissions" help="Module 010 needs Directory.Read.All and User.Read.All. Graph mail needs Mail.Send."><input value={activeTenant.services.graphScopes} onChange={(event) => updateConnection('services', 'graphScopes', normalizeServicesScopes(event.target.value))} /></Field>
            <Field label="New services client secret" help={`The current ${environmentLabel(activeTenant.environmentMode)} secret is preserved until a replacement is saved.`}><input type="password" autoComplete="new-password" value={secrets.services} onChange={(event) => setSecrets((current) => ({ ...current, services: event.target.value }))} /></Field>
            <div className="microsoft-integration-actions"><button type="button" className="primary-action" onClick={() => void saveSecret('services')} disabled={secretSaving === 'services'}>{secretSaving === 'services' ? 'Saving…' : 'Save services connection'}</button><button type="button" className="secondary-action" onClick={() => void testConnection('services')} disabled={testing === 'services'}>{testing === 'services' ? 'Testing…' : 'Test Graph connection'}</button></div>
            <div className="microsoft-integration-fact"><strong>Secret</strong><span>{connectionStatusLabel(servicesSecretConfigured)}</span></div>
            <div className="microsoft-integration-fact"><strong>Required permissions</strong><span>Directory.Read.All · User.Read.All · Mail.Send</span></div>
            <div className="microsoft-integration-fact"><strong>Module 010 preview source</strong><span>Module 065 {environmentLabel(activeTenant.environmentMode)} services profile</span></div>
            {testResults.services ? <pre className="microsoft-integration-result">{JSON.stringify({ status: testResults.services.status, directoryRead: testResults.services.directoryRead, senderMailbox: testResults.services.senderMailbox }, null, 2)}</pre> : null}
          </article>

          <article className="microsoft-integration-card wide microsoft-mail-environment-card" data-mail-environment={activeTenant.environmentMode}>
            <p className="eyebrow">{activeTenant.environmentMode.toUpperCase()} · MICROSOFT 365 / SMTP</p><h2>{environmentLabel(activeTenant.environmentMode)} sender and transport configuration</h2><p className="section-copy">Graph and Microsoft 365 SMTP relay are alternative mail transports. Select one for this environment. Test-only keeps live delivery disabled while still allowing a real non-delivery readiness test of the configured transport.</p>
            <div className="microsoft-integration-form-grid">
              <Field label="Provider"><select value={activeMail.providerTarget} onChange={(event) => updateMail('providerTarget', event.target.value)}><option value="microsoft_graph">Microsoft Graph</option><option value="smtp_relay">Microsoft 365 SMTP relay</option><option value="locked">Locked</option></select></Field>
              <Field label="SMTP host" help={activeMail.providerTarget === 'smtp_relay' ? 'Active SMTP endpoint.' : 'Preserved for later use; inactive while Graph or Locked is selected.'}><input disabled={activeMail.providerTarget !== 'smtp_relay'} value={activeMail.smtpHost} onChange={(event) => updateMail('smtpHost', event.target.value)} /></Field>
              <Field label="SMTP port"><input type="number" disabled={activeMail.providerTarget !== 'smtp_relay'} value={activeMail.smtpPort} onChange={(event) => updateMail('smtpPort', Number(event.target.value))} /></Field>
              <Field label="Sender name"><input value={activeMail.senderName} onChange={(event) => updateMail('senderName', event.target.value)} /></Field>
              <Field label="Sender mailbox"><input type="email" value={activeMail.senderAddress} onChange={(event) => updateMail('senderAddress', event.target.value)} /></Field>
              <Field label="Reply-to address"><input type="email" value={activeMail.replyToAddress} onChange={(event) => updateMail('replyToAddress', event.target.value)} /></Field>
              <Field label="Recipient boundary"><select value={activeMail.recipientBoundary} onChange={(event) => updateMail('recipientBoundary', event.target.value)}><option value="test_only">Test only — no live delivery</option><option value="production_governed">Production governed — live delivery allowed</option><option value="locked">Locked</option></select></Field>
            </div>
            <div className="microsoft-integration-fact"><strong>Authoritative source</strong><span>Module 065 · {environmentLabel(activeTenant.environmentMode)}</span></div>
            <div className="microsoft-integration-fact"><strong>Configured transport</strong><span>{providerDescription}</span></div>
            <div className="microsoft-integration-fact"><strong>Live delivery</strong><span>{activeMail.recipientBoundary === 'production_governed' && activeMail.providerTarget !== 'locked' ? 'Eligible after readiness validation' : 'Disabled by recipient boundary; readiness testing remains available'}</span></div>
          </article>

          <article className="microsoft-integration-card wide">
            <p className="eyebrow">Compatibility contract</p><h2>Existing integrations remain connected</h2>
            <div className="microsoft-compatibility-grid">
              <div><strong>Module 010</strong><span>Uses the matching Module 065 services profile for preview/import and the saved manual or automatic sync schedule.</span></div>
              <div><strong>Module 057</strong><span>Uses the active Module 065 services runtime for calendar and presence.</span></div>
              <div><strong>Module 062</strong><span>Uses explicit Test and Production services credentials by domain.</span></div>
              <div><strong>All email senders</strong><span>Use the provider, sender, boundary, and SMTP projection selected for their Module 065 environment.</span></div>
              <div><strong>Module 067</strong><span>{legacy067?.document ? 'Saved legacy mail configuration preserved as an environment fallback.' : 'No legacy document observed.'}</span></div>
            </div>
          </article>
        </div>
      </> : null}
    </section>
  );
}
