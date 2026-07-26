import { useEffect, useState } from 'react';
import './certify-integration-center.css';

function authHeaders(extra = {}) {
  const headers = { ...extra };
  try {
    const session = JSON.parse(window.localStorage.getItem('projectPulseAuthSession') || 'null');
    const token = session?.sessionToken || session?.token;
    if (token) headers['X-ProjectPulse-Session'] = token;
    const viewAs = JSON.parse(window.localStorage.getItem('projectPulseViewAsUser') || 'null');
    if (viewAs?.userId) headers['X-ProjectPulse-View-As-User'] = viewAs.userId;
  } catch {
    // Global session bridge remains available.
  }
  return headers;
}

async function api(path, options = {}) {
  const response = await fetch(path, {
    credentials: 'same-origin',
    cache: 'no-store',
    ...options,
    headers: authHeaders({ Accept: 'application/json', ...(options.body ? { 'Content-Type': 'application/json' } : {}), ...(options.headers || {}) })
  });
  const raw = await response.text();
  let body = null;
  try { body = raw ? JSON.parse(raw) : null; } catch { body = null; }
  if (!response.ok) throw new Error(body?.message || raw || `HTTP ${response.status}`);
  return body;
}

function dateTime(value) {
  if (!value) return 'Never';
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? String(value) : date.toLocaleString();
}

export default function CertifyIntegrationCenter() {
  const [data, setData] = useState(null);
  const [form, setForm] = useState({
    environmentName: 'test',
    baseUrl: 'https://api.certify.com/v1/',
    apiKeyEnvironmentName: 'PROJECTPULSE_CERTIFY_API_KEY',
    apiSecretEnvironmentName: 'PROJECTPULSE_CERTIFY_API_SECRET',
    companyId: '',
    automaticSyncEnabled: false,
    syncCadence: 'manual'
  });
  const [status, setStatus] = useState('Loading Certify connection…');
  const [error, setError] = useState('');
  const [busy, setBusy] = useState('');

  async function load() {
    setError('');
    try {
      const result = await api('/api/certify/connection');
      setData(result);
      const connection = result.connection || {};
      setForm({
        environmentName: connection.environmentName || 'test',
        baseUrl: connection.baseUrl || 'https://api.certify.com/v1/',
        apiKeyEnvironmentName: connection.apiKeyEnvironmentName || 'PROJECTPULSE_CERTIFY_API_KEY',
        apiSecretEnvironmentName: connection.apiSecretEnvironmentName || 'PROJECTPULSE_CERTIFY_API_SECRET',
        companyId: connection.companyId || '',
        automaticSyncEnabled: Boolean(connection.automaticSyncEnabled),
        syncCadence: connection.syncCadence || 'manual'
      });
      setStatus(`Connection status: ${connection.status || 'not configured'}`);
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : 'Unable to load Certify connection.');
    }
  }

  useEffect(() => { void load(); }, []);

  async function save(message = 'Certify connection saved.') {
    setBusy('save');
    setStatus('Saving connection metadata…');
    setError('');
    try {
      const result = await api('/api/certify/connection', { method: 'PUT', body: JSON.stringify(form) });
      setStatus(result.message || message);
      await load();
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : 'Unable to save Certify connection.');
    } finally {
      setBusy('');
    }
  }

  async function test() {
    setBusy('test');
    setStatus('Testing the Certify API connection…');
    setError('');
    try {
      const result = await api('/api/certify/connection/test', { method: 'POST' });
      setStatus(`${result.message || 'Certify connection test completed.'} Automatic sync is now available.`);
      await load();
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : 'Certify connection test failed.');
    } finally {
      setBusy('');
    }
  }

  const connection = data?.connection || {};
  const connected = connection.status === 'connected';
  const canManage = Boolean(data?.canManage);
  const automationAllowed = Boolean(data?.automation?.allowed) || connected;
  const credentialsReady = Boolean(connection.apiKeyConfigured && connection.apiSecretConfigured);
  const syncLockedReason = !canManage
    ? 'Accounting or Super Administrator access is required.'
    : !credentialsReady
      ? 'Configure both server-side Certify credential environment values first.'
      : !automationAllowed
        ? 'Run a successful connection test to unlock automatic sync.'
        : '';

  return (
    <div className="certify-integration-center certify-connection-v2">
      <header className="certify-hero">
        <div>
          <p className="eyebrow">MODULE 038</p>
          <h1>Certify Connection &amp; Sync Center</h1>
          <p>Configure the governed Certify API connection used by Module 005 Project Expense Upload. Secret values remain in environment configuration and are never displayed in the browser.</p>
        </div>
        <span className={`certify-connection-badge ${connection.status || 'not_configured'}`}>{connection.status || 'Not configured'}</span>
      </header>

      {error ? <div className="certify-error"><strong>Connection action failed</strong><span>{error}</span></div> : null}
      <div className="certify-status-line">{status}</div>

      <section className="certify-sync-control-card" aria-labelledby="certify-sync-heading">
        <div>
          <p className="eyebrow">Synchronization</p>
          <h2 id="certify-sync-heading">Automatic sync</h2>
          <p>The control stays visible at the top of the page. A successful connection test is required before ProjectPulse can enable scheduled synchronization.</p>
          {syncLockedReason ? <div className="certify-sync-lock"><strong>Locked</strong><span>{syncLockedReason}</span></div> : <div className="certify-sync-ready"><strong>Ready</strong><span>Choose automatic sync and save the selected cadence.</span></div>}
        </div>
        <div className="certify-sync-controls">
          <label className="certify-switch">
            <input
              type="checkbox"
              disabled={!canManage || !automationAllowed || busy !== ''}
              checked={form.automaticSyncEnabled}
              onChange={(event) => setForm((current) => ({
                ...current,
                automaticSyncEnabled: event.target.checked,
                syncCadence: event.target.checked ? (current.syncCadence === 'manual' ? 'nightly' : current.syncCadence) : 'manual'
              }))}
            />
            <span>Enable automatic sync</span>
          </label>
          <label>Cadence
            <select disabled={!canManage || !automationAllowed || !form.automaticSyncEnabled || busy !== ''} value={form.syncCadence} onChange={(event) => setForm((current) => ({ ...current, syncCadence: event.target.value }))}>
              <option value="hourly">Hourly</option>
              <option value="nightly">Nightly</option>
            </select>
          </label>
          <div className="certify-sync-actions">
            {!automationAllowed ? <button type="button" className="primary-action" disabled={!canManage || !credentialsReady || busy !== ''} onClick={() => void test()}>{busy === 'test' ? 'Testing…' : 'Test connection to unlock'}</button> : null}
            <button type="button" className="secondary-action" disabled={!canManage || !automationAllowed || busy !== ''} onClick={() => void save('Automatic sync settings saved.')}>{busy === 'save' ? 'Saving…' : 'Save sync settings'}</button>
          </div>
        </div>
      </section>

      <section className="certify-connection-grid">
        <div className="certify-card">
          <p className="eyebrow">Connection profile</p>
          <h2>Certify API</h2>
          <label>Environment
            <select disabled={!canManage || busy !== ''} value={form.environmentName} onChange={(event) => setForm((current) => ({ ...current, environmentName: event.target.value }))}>
              <option value="test">Test / sandbox</option>
              <option value="production">Production</option>
            </select>
          </label>
          <label>API base URL<input disabled={!canManage || busy !== ''} value={form.baseUrl} onChange={(event) => setForm((current) => ({ ...current, baseUrl: event.target.value }))} /></label>
          <label>Company ID (optional)<input disabled={!canManage || busy !== ''} value={form.companyId} onChange={(event) => setForm((current) => ({ ...current, companyId: event.target.value }))} /></label>
        </div>

        <div className="certify-card">
          <p className="eyebrow">Secret references</p>
          <h2>Server-side credentials</h2>
          <label>API key environment name<input disabled={!canManage || busy !== ''} value={form.apiKeyEnvironmentName} onChange={(event) => setForm((current) => ({ ...current, apiKeyEnvironmentName: event.target.value }))} /></label>
          <div className={`certify-secret-state ${connection.apiKeyConfigured ? 'ready' : 'missing'}`}>API key: {connection.apiKeyConfigured ? 'configured' : 'missing'}</div>
          <label>API secret environment name<input disabled={!canManage || busy !== ''} value={form.apiSecretEnvironmentName} onChange={(event) => setForm((current) => ({ ...current, apiSecretEnvironmentName: event.target.value }))} /></label>
          <div className={`certify-secret-state ${connection.apiSecretConfigured ? 'ready' : 'missing'}`}>API secret: {connection.apiSecretConfigured ? 'configured' : 'missing'}</div>
          <small>No credential value is stored in the ProjectPulse database or returned to the browser.</small>
        </div>
      </section>

      <section className="certify-actions-card">
        <div>
          <h2>Connection evidence</h2>
          <p><strong>Last tested:</strong> {dateTime(connection.lastTestedAt)}</p>
          <p><strong>Last result:</strong> {connection.lastTestResult || 'No connection test has been recorded.'}</p>
          <p><strong>Last successful sync/import:</strong> {dateTime(connection.lastSuccessfulSyncAt)}</p>
        </div>
        <div className="certify-actions">
          <button type="button" className="secondary-action" disabled={busy !== ''} onClick={() => void load()}>Refresh</button>
          <button type="button" className="secondary-action" disabled={!canManage || busy !== ''} onClick={() => void save()}>{busy === 'save' ? 'Saving…' : 'Save configuration'}</button>
          <button type="button" className="primary-action" disabled={!canManage || !credentialsReady || busy !== ''} onClick={() => void test()}>{busy === 'test' ? 'Testing…' : connected ? 'Test again' : 'Test connection'}</button>
        </div>
      </section>

      <section className="certify-module005-handoff">
        <p className="eyebrow">Module 005 handoff</p>
        <h2>Project Expense Upload</h2>
        <p>{connected
          ? 'The connection is ready. Engineers and Project Managers may select Import from Certify in Module 005 for projects within their role scope.'
          : 'CSV/Excel upload remains available in Module 005. Certify import will unlock after this connection is tested successfully.'}</p>
        <a href="#project-allocation-info">Open Module 005 Project Expense Upload</a>
      </section>
    </div>
  );
}
