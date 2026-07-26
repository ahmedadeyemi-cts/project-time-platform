import { useCallback, useEffect, useMemo, useState } from 'react';
import './microsoft-integration-portal.css';

const ACTIVE_ROUTE = 'entra-secret-administration';
const CONFIG_MARKER = 'PROJECTPULSE_MICROSOFT_INTEGRATION_JSON:';

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
  if (!response.ok) {
    throw new Error(body?.message || body?.status || `Request failed with HTTP ${response.status}.`);
  }
  return body;
}

function createTenant(index = 1) {
  return {
    key: `tenant-${index}`,
    name: `Microsoft tenant ${index}`,
    environmentMode: index === 1 ? 'test' : 'production',
    tenantDomain: '',
    tenantId: '',
    clientId: '',
    authorityUrl: '',
    redirectUri: '',
    graphScopes: 'User.Read.All Directory.Read.All',
    sourceProvider: index === 1 ? 'ENTRA_ID_TEST' : 'ENTRA_ID',
    directorySyncEnabled: false,
    syncFrequencyHours: 24,
    defaultRoleCode: 'ENGINEERING'
  };
}

function defaultConfiguration() {
  return {
    activeTenantKey: 'tenant-1',
    tenants: [createTenant(1)],
    mail: {
      providerTarget: 'microsoft_graph',
      smtpHost: 'smtp.office365.com',
      smtpPort: 587,
      senderName: '',
      senderAddress: '',
      replyToAddress: '',
      recipientBoundary: 'test_only'
    }
  };
}

function parseStoredConfiguration(document) {
  const notes = document?.configuration?.notes;
  if (typeof notes !== 'string' || !notes.startsWith(CONFIG_MARKER)) return null;
  try {
    const parsed = JSON.parse(notes.slice(CONFIG_MARKER.length));
    if (!Array.isArray(parsed?.tenants) || parsed.tenants.length === 0) return null;
    return parsed;
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

export default function MicrosoftIntegrationPortal() {
  const [active, setActive] = useState(routeFromHash() === ACTIVE_ROUTE);
  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [testing, setTesting] = useState(false);
  const [secretSaving, setSecretSaving] = useState(false);
  const [overview, setOverview] = useState(null);
  const [nativeDocument, setNativeDocument] = useState({});
  const [revision, setRevision] = useState(0);
  const [legacy067, setLegacy067] = useState(null);
  const [configuration, setConfiguration] = useState(defaultConfiguration());
  const [clientSecret, setClientSecret] = useState('');
  const [message, setMessage] = useState('');
  const [error, setError] = useState('');
  const [testResult, setTestResult] = useState(null);

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
      const [overviewBody, module065Body, module067Body] = await Promise.all([
        fetchJson('/api/microsoft-integration/overview'),
        fetchJson('/api/native-administration/065/document'),
        fetchJson('/api/native-administration/067/document').catch(() => null)
      ]);

      const module065Document = module065Body?.document || {};
      const stored = parseStoredConfiguration(module065Document);
      const next = stored || defaultConfiguration();

      if (!stored && module067Body?.document) {
        next.mail = legacyMailFallback(module067Body.document);
      }

      const observedTenant = overviewBody?.activeTenant;
      if (!stored && observedTenant) {
        next.tenants[0] = {
          ...next.tenants[0],
          key: observedTenant.tenantKey || next.tenants[0].key,
          name: observedTenant.tenantName || next.tenants[0].name,
          tenantDomain: observedTenant.tenantDomain || '',
          tenantId: observedTenant.tenantId || '',
          clientId: observedTenant.clientId || '',
          authorityUrl: observedTenant.authorityUrl || '',
          redirectUri: observedTenant.redirectUri || '',
          graphScopes: observedTenant.graphScopes || next.tenants[0].graphScopes,
          sourceProvider: observedTenant.sourceProvider || next.tenants[0].sourceProvider,
          directorySyncEnabled: Boolean(observedTenant.directorySyncEnabled),
          syncFrequencyHours: Number(observedTenant.syncFrequencyHours || 24),
          defaultRoleCode: observedTenant.defaultRoleCode || 'ENGINEERING'
        };
        next.activeTenantKey = next.tenants[0].key;
      }

      setOverview(overviewBody);
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

  useEffect(() => {
    void load();
  }, [load]);

  const activeTenant = useMemo(
    () => configuration.tenants.find((tenant) => tenant.key === configuration.activeTenantKey) || configuration.tenants[0],
    [configuration]
  );

  function updateTenant(field, value) {
    setConfiguration((current) => ({
      ...current,
      tenants: current.tenants.map((tenant) => tenant.key === current.activeTenantKey
        ? { ...tenant, [field]: value }
        : tenant)
    }));
  }

  function updateMail(field, value) {
    setConfiguration((current) => ({
      ...current,
      mail: { ...current.mail, [field]: value }
    }));
  }

  function addTenant() {
    setConfiguration((current) => {
      const tenant = createTenant(current.tenants.length + 1);
      tenant.key = `tenant-${Date.now()}`;
      return { ...current, tenants: [...current.tenants, tenant], activeTenantKey: tenant.key };
    });
  }

  function removeTenant() {
    if (configuration.tenants.length <= 1) return;
    setConfiguration((current) => {
      const tenants = current.tenants.filter((tenant) => tenant.key !== current.activeTenantKey);
      return { ...current, tenants, activeTenantKey: tenants[0].key };
    });
  }

  async function saveConfiguration() {
    if (!activeTenant?.tenantId || !activeTenant?.clientId) {
      setError('Tenant ID and application/client ID are required for the active tenant.');
      return;
    }

    setSaving(true);
    setMessage('');
    setError('');
    try {
      const document = {
        ...nativeDocument,
        configuration: {
          ...(nativeDocument?.configuration || {}),
          applicationId: activeTenant.clientId,
          tenantId: activeTenant.tenantId,
          ownerTeam: nativeDocument?.configuration?.ownerTeam || 'Platform Administration',
          notes: `${CONFIG_MARKER}${JSON.stringify(configuration)}`
        }
      };

      const saved = await fetchJson('/api/native-administration/065/document', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ expectedRevision: revision, document })
      });

      const legacyConfig = {
        tenantId: activeTenant.tenantId,
        clientId: activeTenant.clientId,
        authorityUrl: activeTenant.authorityUrl || `https://login.microsoftonline.com/${activeTenant.tenantId}`,
        redirectUri: activeTenant.redirectUri || '',
        graphScope: activeTenant.graphScopes || 'User.Read.All Directory.Read.All',
        syncEnabled: Boolean(activeTenant.directorySyncEnabled),
        defaultRoleCode: activeTenant.defaultRoleCode || 'ENGINEERING',
        syncFrequencyHours: Number(activeTenant.syncFrequencyHours || 24)
      };

      await Promise.all([
        fetchJson('/api/admin/azure/config', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(legacyConfig)
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
            defaultRoleCode: activeTenant.defaultRoleCode || 'ENGINEERING',
            disableMissingFromSource: false
          })
        })
      ]);

      setNativeDocument(saved.document || document);
      setRevision(Number(saved.revision || revision + 1));
      setMessage(`Microsoft Integration configuration saved as revision ${saved.revision || revision + 1}. The active tenant is available to Module 010 and Module 062 identity services.`);
      await load();
    } catch (saveError) {
      setError(saveError?.message || 'Microsoft Integration configuration could not be saved.');
    } finally {
      setSaving(false);
    }
  }

  async function saveClientSecret() {
    if (!clientSecret.trim()) {
      setError('Enter a client secret before saving.');
      return;
    }
    setSecretSaving(true);
    setMessage('');
    setError('');
    try {
      const result = await fetchJson('/api/microsoft-integration/client-secret', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ tenantKey: activeTenant.key, clientSecret })
      });
      setClientSecret('');
      setMessage(result.message || 'Client secret saved securely. The value will not be displayed again.');
      await load();
    } catch (secretError) {
      setError(secretError?.message || 'The client secret could not be saved.');
    } finally {
      setSecretSaving(false);
    }
  }

  async function testConnection() {
    setTesting(true);
    setMessage('');
    setError('');
    setTestResult(null);
    try {
      const result = await fetchJson('/api/microsoft-integration/test-connection', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          tenantKey: activeTenant.key,
          tenantId: activeTenant.tenantId,
          clientId: activeTenant.clientId,
          senderMailbox: configuration.mail.senderAddress
        })
      });
      setTestResult(result);
      setMessage(result.message || 'Microsoft Graph connection test completed.');
    } catch (testError) {
      setError(testError?.message || 'Microsoft Graph connection test failed.');
    } finally {
      setTesting(false);
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
            Manage Microsoft tenants, Entra application configuration, write-only client secrets, identity integration, directory synchronization, and Microsoft 365 sender settings. Module 010 remains dedicated to previewing and importing directory users.
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
            Module 067 is retired as a standalone page. Its saved configuration remains intact and is loaded here as compatibility data. Existing Module 067 permissions are accepted by Module 065.
          </div>

          <div className="microsoft-integration-grid">
            <article className="microsoft-integration-card wide">
              <div className="microsoft-integration-card-heading">
                <div><p className="eyebrow">Tenant directory</p><h2>Microsoft tenants</h2></div>
                <div className="microsoft-integration-actions">
                  <button type="button" className="secondary-action" onClick={addTenant}>Add tenant</button>
                  <button type="button" className="danger-action" onClick={removeTenant} disabled={configuration.tenants.length <= 1}>Remove tenant</button>
                </div>
              </div>
              <div className="microsoft-integration-form-grid">
                <Field label="Active tenant">
                  <select value={configuration.activeTenantKey} onChange={(event) => setConfiguration((current) => ({ ...current, activeTenantKey: event.target.value }))}>
                    {configuration.tenants.map((tenant) => <option value={tenant.key} key={tenant.key}>{tenant.name || tenant.key}</option>)}
                  </select>
                </Field>
                <Field label="Tenant name"><input value={activeTenant.name} onChange={(event) => updateTenant('name', event.target.value)} /></Field>
                <Field label="Tenant key" help="Stable internal identifier used for secret storage."><input value={activeTenant.key} disabled /></Field>
                <Field label="Environment">
                  <select value={activeTenant.environmentMode} onChange={(event) => updateTenant('environmentMode', event.target.value)}>
                    <option value="test">Test</option><option value="production">Production</option><option value="custom">Custom</option>
                  </select>
                </Field>
                <Field label="Tenant domain"><input value={activeTenant.tenantDomain} onChange={(event) => updateTenant('tenantDomain', event.target.value)} placeholder="onenecklab.com" /></Field>
                <Field label="Tenant ID"><input value={activeTenant.tenantId} onChange={(event) => updateTenant('tenantId', event.target.value)} /></Field>
                <Field label="Application / client ID"><input value={activeTenant.clientId} onChange={(event) => updateTenant('clientId', event.target.value)} /></Field>
                <Field label="Redirect URI"><input value={activeTenant.redirectUri} onChange={(event) => updateTenant('redirectUri', event.target.value)} /></Field>
                <Field label="Directory source">
                  <select value={activeTenant.sourceProvider} onChange={(event) => updateTenant('sourceProvider', event.target.value)}>
                    <option value="ENTRA_ID_TEST">Entra ID test</option><option value="ENTRA_ID">Entra ID production</option>
                  </select>
                </Field>
                <Field label="Default imported-user role"><input value={activeTenant.defaultRoleCode} onChange={(event) => updateTenant('defaultRoleCode', event.target.value.toUpperCase())} /></Field>
                <Field label="Sync frequency (hours)"><input type="number" min="1" max="720" value={activeTenant.syncFrequencyHours} onChange={(event) => updateTenant('syncFrequencyHours', Number(event.target.value))} /></Field>
                <Field label="Directory synchronization"><span className="microsoft-integration-checkbox"><input type="checkbox" checked={activeTenant.directorySyncEnabled} onChange={(event) => updateTenant('directorySyncEnabled', event.target.checked)} /> Enabled</span></Field>
              </div>
            </article>

            <article className="microsoft-integration-card">
              <p className="eyebrow">API permissions</p>
              <h2>Identity permission readiness</h2>
              <ul className="microsoft-integration-check-list">
                <li><strong>Directory.Read.All</strong><span>Application · Admin consent granted</span></li>
                <li><strong>User.Read.All</strong><span>Application · Admin consent granted</span></li>
                <li><strong>User.Read</strong><span>Delegated · Sign-in/profile access granted</span></li>
              </ul>
              <p className="section-copy">Application permissions are used for background directory preview/import. Delegated User.Read continues to support signed-in identity profile behavior.</p>
            </article>

            <article className="microsoft-integration-card">
              <p className="eyebrow">Write-only credential</p>
              <h2>Client secret</h2>
              <Field label="Enter a new client secret" help="The value is encrypted, never returned by the API, and cleared from this form after saving.">
                <input type="password" autoComplete="new-password" value={clientSecret} onChange={(event) => setClientSecret(event.target.value)} />
              </Field>
              <button type="button" className="primary-action" onClick={() => void saveClientSecret()} disabled={secretSaving}>{secretSaving ? 'Saving secret…' : 'Save client secret'}</button>
              <div className="microsoft-integration-fact"><strong>Stored</strong><span>{overview?.secretMetadata?.some((item) => item.tenantKey === activeTenant.key) ? 'Yes' : 'Not observed'}</span></div>
            </article>

            <article className="microsoft-integration-card wide">
              <p className="eyebrow">Microsoft 365 / SMTP</p>
              <h2>Sender and transport configuration</h2>
              <div className="microsoft-integration-form-grid">
                <Field label="Provider">
                  <select value={configuration.mail.providerTarget} onChange={(event) => updateMail('providerTarget', event.target.value)}>
                    <option value="microsoft_graph">Microsoft Graph</option><option value="smtp_relay">Microsoft 365 SMTP relay</option><option value="locked">Locked</option>
                  </select>
                </Field>
                <Field label="SMTP host"><input value={configuration.mail.smtpHost} onChange={(event) => updateMail('smtpHost', event.target.value)} /></Field>
                <Field label="SMTP port"><input type="number" value={configuration.mail.smtpPort} onChange={(event) => updateMail('smtpPort', Number(event.target.value))} /></Field>
                <Field label="Sender name"><input value={configuration.mail.senderName} onChange={(event) => updateMail('senderName', event.target.value)} /></Field>
                <Field label="Sender mailbox"><input type="email" value={configuration.mail.senderAddress} onChange={(event) => updateMail('senderAddress', event.target.value)} /></Field>
                <Field label="Reply-to address"><input type="email" value={configuration.mail.replyToAddress} onChange={(event) => updateMail('replyToAddress', event.target.value)} /></Field>
                <Field label="Recipient boundary">
                  <select value={configuration.mail.recipientBoundary} onChange={(event) => updateMail('recipientBoundary', event.target.value)}>
                    <option value="test_only">Test only</option><option value="production_governed">Production governed</option><option value="locked">Locked</option>
                  </select>
                </Field>
              </div>
            </article>

            <article className="microsoft-integration-card">
              <p className="eyebrow">Connection validation</p>
              <h2>Microsoft Graph test</h2>
              <p className="section-copy">Acquires an application token and performs a sanitized directory read. When a sender mailbox is configured, the mailbox identity is also resolved. No token or secret is returned.</p>
              <button type="button" className="primary-action" onClick={() => void testConnection()} disabled={testing}>{testing ? 'Testing…' : 'Test connection'}</button>
              {testResult ? <pre className="microsoft-integration-result">{JSON.stringify({ status: testResult.status, directoryRead: testResult.directoryRead, senderMailbox: testResult.senderMailbox }, null, 2)}</pre> : null}
            </article>

            <article className="microsoft-integration-card">
              <p className="eyebrow">Readiness and sync</p>
              <h2>Integration status</h2>
              <div className="microsoft-integration-fact"><strong>Identity integration</strong><span>{overview?.identityIntegration?.status || 'Not observed'}</span></div>
              <div className="microsoft-integration-fact"><strong>Directory sync</strong><span>{overview?.directorySync?.status || (activeTenant.directorySyncEnabled ? 'Enabled' : 'Disabled')}</span></div>
              <div className="microsoft-integration-fact"><strong>Last sync</strong><span>{overview?.directorySync?.lastSyncAt || 'No recorded sync'}</span></div>
              <div className="microsoft-integration-fact"><strong>Module 067 data</strong><span>{legacy067?.document ? 'Preserved and available' : 'No saved document observed'}</span></div>
            </article>
          </div>
        </>
      ) : null}
    </section>
  );
}
