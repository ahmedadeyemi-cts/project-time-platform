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

  async function save() {
    setStatus('Saving connection metadata…');
    setError('');
    try {
      const result = await api('/api/certify/connection', { method: 'PUT', body: JSON.stringify(form) });
      setStatus(result.message || 'Certify connection saved.');
      await load();
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : 'Unable to save Certify connection.');
    }
  }

  async function test() {
    setStatus('Testing the Certify API connection…');
    setError('');
    try {
      const result = await api('/api/certify/connection/test', { method: 'POST' });
      setStatus(result.message || 'Certify connection test completed.');
      await load();
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : 'Certify connection test failed.');
    }
  }

  const connection = data?.connection || {};
  const connected = connection.status === 'connected';
  const canManage = Boolean(data?.canManage);

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

      <section className="certify-connection-grid">
        <div className="certify-card">
          <p className="eyebrow">Connection profile</p>
          <h2>Certify API</h2>
          <label>Environment
            <select disabled={!canManage} value={form.environmentName} onChange={(event) => setForm((current) => ({ ...current, environmentName: event.target.value }))}>
              <option value="test">Test / sandbox</option>
              <option value="production">Production</option>
            </select>
          </label>
          <label>API base URL<input disabled={!canManage} value={form.baseUrl} onChange={(event) => setForm((current) => ({ ...current, baseUrl: event.target.value }))} /></label>
          <label>Company ID (optional)<input disabled={!canManage} value={form.companyId} onChange={(event) => setForm((current) => ({ ...current, companyId: event.target.value }))} /></label>
        </div>

        <div className="certify-card">
          <p className="eyebrow">Secret references</p>
          <h2>Server-side credentials</h2>
          <label>API key environment name<input disabled={!canManage} value={form.apiKeyEnvironmentName} onChange={(event) => setForm((current) => ({ ...current, apiKeyEnvironmentName: event.target.value }))} /></label>
          <div className={`certify-secret-state ${connection.apiKeyConfigured ? 'ready' : 'missing'}`}>API key: {connection.apiKeyConfigured ? 'configured' : 'missing'}</div>
          <label>API secret environment name<input disabled={!canManage} value={form.apiSecretEnvironmentName} onChange={(event) => setForm((current) => ({ ...current, apiSecretEnvironmentName: event.target.value }))} /></label>
          <div className={`certify-secret-state ${connection.apiSecretConfigured ? 'ready' : 'missing'}`}>API secret: {connection.apiSecretConfigured ? 'configured' : 'missing'}</div>
          <small>No credential value is stored in the ProjectPulse database or returned to the browser.</small>
        </div>

        <div className="certify-card">
          <p className="eyebrow">Synchronization</p>
          <h2>Manual first, automate later</h2>
          <label className="certify-switch"><input type="checkbox" disabled={!canManage || !connected} checked={form.automaticSyncEnabled} onChange={(event) => setForm((current) => ({ ...current, automaticSyncEnabled: event.target.checked, syncCadence: event.target.checked ? 'nightly' : 'manual' }))} />Enable automatic sync</label>
          <label>Cadence
            <select disabled={!canManage || !connected || !form.automaticSyncEnabled} value={form.syncCadence} onChange={(event) => setForm((current) => ({ ...current, syncCadence: event.target.value }))}>
              <option value="manual">Manual only</option>
              <option value="hourly">Hourly</option>
              <option value="nightly">Nightly</option>
            </select>
          </label>
          <p>Automatic sync is locked until a connection test succeeds. Module 005 can still accept CSV/Excel files while Certify is not connected.</p>
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
          <button type="button" className="secondary-action" onClick={() => void load()}>Refresh</button>
          <button type="button" className="secondary-action" disabled={!canManage} onClick={() => void save()}>Save configuration</button>
          <button type="button" className="primary-action" disabled={!canManage || !connection.apiKeyConfigured || !connection.apiSecretConfigured} onClick={() => void test()}>Test connection</button>
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
