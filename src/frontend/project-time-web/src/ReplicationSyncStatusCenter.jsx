import { useEffect, useMemo, useState } from 'react';
import './replication-sync-status-center.css';

const statusLabels = {
  ready: 'Ready',
  warning: 'Warning',
  action_required: 'Action Required',
  not_configured: 'Planned',
  unknown: 'Unknown'
};

const defaultSettings = {
  peerName: '',
  peerHost: '',
  peerUrl: '',
  staleBackupHours: 24
};

function getSessionToken(authSession) {
  return authSession?.sessionToken
    ?? authSession?.token
    ?? authSession?.accessToken
    ?? window.localStorage.getItem('projectPulseSessionToken')
    ?? window.sessionStorage.getItem('projectPulseSessionToken')
    ?? '';
}

function buildAuthHeaders(authSession, extraHeaders = {}) {
  const token = getSessionToken(authSession);

  return {
    ...extraHeaders,
    ...(token ? {
      Authorization: `Bearer ${token}`,
      'X-ProjectPulse-Session': token,
      'X-Project-Pulse-Session': token,
      'X-Session-Token': token
    } : {})
  };
}

function normalizeStatus(value) {
  return (value || 'unknown').toString().toLowerCase();
}

function statusLabel(value) {
  return statusLabels[normalizeStatus(value)] || value || 'Unknown';
}

function formatDate(value) {
  if (!value) return 'Not available';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return date.toLocaleString();
}

function formatBytes(value) {
  if (value === null || value === undefined) return 'Not available';
  const number = Number(value);
  if (!Number.isFinite(number)) return 'Not available';

  const units = ['B', 'KB', 'MB', 'GB', 'TB'];
  let size = number;
  let index = 0;

  while (size >= 1024 && index < units.length - 1) {
    size /= 1024;
    index += 1;
  }

  return `${size.toFixed(index === 0 ? 0 : 1)} ${units[index]}`;
}

function StatusBadge({ status }) {
  const normalized = normalizeStatus(status);

  return (
    <span className={`replication-sync-status-badge replication-sync-status-badge--${normalized}`}>
      {statusLabel(normalized)}
    </span>
  );
}

function ReadinessCard({ title, value, detail, status }) {
  return (
    <article className="replication-sync-readiness-card">
      <div className="replication-sync-readiness-card__header">
        <span>{title}</span>
        <StatusBadge status={status} />
      </div>
      <strong>{value || 'Not available'}</strong>
      {detail ? <p>{detail}</p> : null}
    </article>
  );
}

export default function ReplicationSyncStatusCenter({ authSession }) {
  const [status, setStatus] = useState(null);
  const [settings, setSettings] = useState(defaultSettings);
  const [settingsState, setSettingsState] = useState({ loading: true, saving: false, error: '', message: '' });
  const [loadState, setLoadState] = useState('loading');
  const [error, setError] = useState('');
  const [refreshing, setRefreshing] = useState(false);

  async function fetchJson(path, options = {}) {
    const response = await fetch(path, {
      credentials: 'include',
      ...options,
      headers: buildAuthHeaders(authSession, options.headers ?? {})
    });

    const payload = await response.json().catch(() => null);

    if (!response.ok) {
      throw new Error(payload?.message ?? payload?.error ?? `Request failed with HTTP ${response.status}`);
    }

    return payload;
  }

  async function loadStatus() {
    setRefreshing(true);
    setError('');

    try {
      const data = await fetchJson('/api/system/replication-sync/status', {
        headers: { Accept: 'application/json' }
      });

      setStatus(data);
      setLoadState('loaded');
    } catch (err) {
      setError(err?.message || 'Unable to load replication and sync status.');
      setLoadState('error');
    } finally {
      setRefreshing(false);
    }
  }

  async function loadSettings() {
    setSettingsState((current) => ({ ...current, loading: true, error: '', message: '' }));

    try {
      const data = await fetchJson('/api/system/replication-sync/settings', {
        headers: { Accept: 'application/json' }
      });

      setSettings({
        peerName: data.peerName || '',
        peerHost: data.peerHost || '',
        peerUrl: data.peerUrl || '',
        staleBackupHours: data.staleBackupHours || 24
      });

      setSettingsState((current) => ({ ...current, loading: false }));
    } catch (err) {
      setSettingsState((current) => ({
        ...current,
        loading: false,
        error: err?.message || 'Unable to load Replication / Sync settings.'
      }));
    }
  }

  async function saveSettings(event) {
    event.preventDefault();

    setSettingsState((current) => ({ ...current, saving: true, error: '', message: '' }));

    try {
      const payload = await fetchJson('/api/system/replication-sync/settings', {
        method: 'POST',
        headers: {
          Accept: 'application/json',
          'Content-Type': 'application/json'
        },
        body: JSON.stringify({
          peerName: settings.peerName,
          peerHost: settings.peerHost,
          peerUrl: settings.peerUrl,
          staleBackupHours: Number(settings.staleBackupHours || 24)
        })
      });

      setSettingsState((current) => ({
        ...current,
        saving: false,
        message: payload.message || 'Replication / Sync settings saved.'
      }));

      window.setTimeout(loadStatus, 1200);
    } catch (err) {
      setSettingsState((current) => ({
        ...current,
        saving: false,
        error: err?.message || 'Unable to save Replication / Sync settings.'
      }));
    }
  }

  function updateSetting(field, value) {
    setSettings((current) => ({
      ...current,
      [field]: value
    }));
  }

  useEffect(() => {
    loadStatus();
    loadSettings();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [authSession?.sessionToken, authSession?.token, authSession?.accessToken]);

  const checks = useMemo(() => status?.checks || [], [status]);
  const services = useMemo(() => status?.services || [], [status]);
  const peerConfigured = Boolean(status?.peer?.configured);

  return (
    <section id="replication-sync-center" className="panel timesheet-page replication-sync-page">
      <section className="replication-sync-hero">
        <div>
          <p className="replication-sync-eyebrow">System Operations</p>
          <h1>Replication & Sync Status</h1>
          <p>
            Validate whether this PHD node is ready for future failover,
            peer synchronization, and disaster recovery operations.
          </p>
        </div>

        <div className="replication-sync-hero__actions">
          {status?.overallStatus ? <StatusBadge status={status.overallStatus} /> : null}
          <button type="button" onClick={loadStatus} disabled={refreshing}>
            {refreshing ? 'Refreshing...' : 'Refresh status'}
          </button>
        </div>
      </section>

      {loadState === 'loading' ? (
        <section className="replication-sync-panel">
          <p>Loading replication and sync status...</p>
        </section>
      ) : null}

      {error ? (
        <section className="replication-sync-alert">
          <strong>Unable to load status.</strong>
          <p>{error}</p>
        </section>
      ) : null}

      {status ? (
        <>
          <section className="replication-sync-mode-banner">
            <div>
              <span>Current operating mode</span>
              <h2>{peerConfigured ? 'Peer-aware DR mode' : 'Single-server readiness mode'}</h2>
              <p>
                {peerConfigured
                  ? 'A peer server is configured. ProjectPulse will evaluate peer reachability and future sync readiness.'
                  : 'No redundant ProjectPulse server is configured yet. This is expected until the second or third server is built.'}
              </p>
            </div>
            <StatusBadge status={peerConfigured ? status.peer?.status : 'not_configured'} />
          </section>

          <section className="replication-sync-readiness-grid">
            <ReadinessCard
              title="Overall readiness"
              value={statusLabel(status.overallStatus)}
              detail={`Ready: ${status.summary?.ready ?? 0} · Warning: ${status.summary?.warning ?? 0} · Planned: ${status.summary?.notConfigured ?? 0} · Action Required: ${status.summary?.actionRequired ?? 0}`}
              status={status.overallStatus}
            />

            <ReadinessCard
              title="Database role"
              value={status.database?.role}
              detail={status.database?.detail}
              status={status.database?.status}
            />

            <ReadinessCard
              title="Latest backup"
              value={status.backup?.latestBundle?.name}
              detail={
                status.backup?.latestBundle?.ageHours !== null &&
                status.backup?.latestBundle?.ageHours !== undefined
                  ? `Age: ${status.backup.latestBundle.ageHours} hours · Size: ${formatBytes(status.backup.latestBundle.sizeBytes)}`
                  : status.backup?.latestBundle?.detail
              }
              status={status.backup?.latestBundle?.status}
            />

            <ReadinessCard
              title="Peer server"
              value={peerConfigured ? status.peer?.name || status.peer?.host : 'Not configured'}
              detail={status.peer?.detail}
              status={status.peer?.status}
            />
          </section>

          <section className="replication-sync-layout-grid">
            <section className="replication-sync-panel replication-sync-settings-panel">
              <div className="replication-sync-section-header">
                <div>
                  <p className="replication-sync-eyebrow">Configuration</p>
                  <h2>Replication / Sync Settings</h2>
                  <p>
                    Add the DR peer details later. Leaving these fields blank keeps this server in planned single-server mode.
                  </p>
                </div>
              </div>

              <form className="replication-sync-settings-form" onSubmit={saveSettings}>
                <label>
                  <span>Peer server name</span>
                  <input
                    type="text"
                    value={settings.peerName}
                    onChange={(event) => updateSetting('peerName', event.target.value)}
                    placeholder="Example: PHD DR Node"
                  />
                </label>

                <label>
                  <span>Peer host or IP</span>
                  <input
                    type="text"
                    value={settings.peerHost}
                    onChange={(event) => updateSetting('peerHost', event.target.value)}
                    placeholder="Example: 10.20.30.40"
                  />
                </label>

                <label>
                  <span>Peer PHD URL</span>
                  <input
                    type="url"
                    value={settings.peerUrl}
                    onChange={(event) => updateSetting('peerUrl', event.target.value)}
                    placeholder="https://projectpulse-dr.example.com"
                  />
                </label>

                <label>
                  <span>Backup freshness threshold, hours</span>
                  <input
                    type="number"
                    min="1"
                    max="720"
                    value={settings.staleBackupHours}
                    onChange={(event) => updateSetting('staleBackupHours', event.target.value)}
                  />
                </label>

                {settingsState.error ? <div className="replication-sync-alert inline">{settingsState.error}</div> : null}
                {settingsState.message ? <div className="replication-sync-success inline">{settingsState.message}</div> : null}

                <div className="replication-sync-settings-actions">
                  <button type="submit" disabled={settingsState.saving || settingsState.loading}>
                    {settingsState.saving ? 'Saving...' : 'Save settings'}
                  </button>
                </div>
              </form>
            </section>

            <section className="replication-sync-panel">
              <div className="replication-sync-section-header">
                <div>
                  <p className="replication-sync-eyebrow">Deployment</p>
                  <h2>Current Node</h2>
                  <p>Generated {formatDate(status.generatedAt)} on {status.host?.hostname || 'this server'}.</p>
                </div>
              </div>

              <dl className="replication-sync-details compact">
                <div>
                  <dt>Git branch</dt>
                  <dd>{status.git?.branch || 'unknown'}</dd>
                </div>
                <div>
                  <dt>Commit</dt>
                  <dd>{status.git?.commit || 'unknown'}</dd>
                </div>
                <div>
                  <dt>Working tree</dt>
                  <dd>{status.git?.dirtyFiles === 0 ? 'Clean' : `${status.git?.dirtyFiles ?? 'Unknown'} uncommitted file(s)`}</dd>
                </div>
                <div>
                  <dt>Host</dt>
                  <dd>{status.host?.hostname || 'unknown'}</dd>
                </div>
              </dl>
            </section>
          </section>

          <section className="replication-sync-panel">
            <div className="replication-sync-section-header">
              <div>
                <p className="replication-sync-eyebrow">Readiness</p>
                <h2>Failover Readiness Checklist</h2>
                <p>
                  Items marked Planned are intentionally waiting for the future DR node.
                </p>
              </div>
            </div>

            <div className="replication-sync-check-list">
              {checks.map((check, index) => (
                <article className="replication-sync-check" key={`${check.category}-${check.name}-${index}`}>
                  <div>
                    <span className="replication-sync-check__category">{check.category}</span>
                    <h3>{check.name}</h3>
                    <p>{check.detail}</p>
                  </div>
                  <StatusBadge status={check.status} />
                </article>
              ))}
            </div>
          </section>

          <section className="replication-sync-two-column">
            <section className="replication-sync-panel">
              <h2>Database Replication Detail</h2>
              <dl className="replication-sync-details">
                <div>
                  <dt>Role</dt>
                  <dd>{status.database?.role || 'unknown'}</dd>
                </div>
                <div>
                  <dt>In recovery</dt>
                  <dd>{String(status.database?.isInRecovery ?? 'unknown')}</dd>
                </div>
                <div>
                  <dt>Replication connections</dt>
                  <dd>{status.database?.replicationConnections ?? 'Not available'}</dd>
                </div>
                <div>
                  <dt>WAL LSN</dt>
                  <dd>{status.database?.walLsn || 'Not available'}</dd>
                </div>
                <div>
                  <dt>Replay lag</dt>
                  <dd>{status.database?.replayLagSeconds ?? 'Not available'} seconds</dd>
                </div>
              </dl>
            </section>

            <section className="replication-sync-panel">
              <h2>Peer Server Detail</h2>
              <dl className="replication-sync-details">
                <div>
                  <dt>Name</dt>
                  <dd>{status.peer?.name || 'Not configured'}</dd>
                </div>
                <div>
                  <dt>Host</dt>
                  <dd>{status.peer?.host || 'Not configured'}</dd>
                </div>
                <div>
                  <dt>URL</dt>
                  <dd>{status.peer?.url || 'Not configured'}</dd>
                </div>
                <div>
                  <dt>Status</dt>
                  <dd><StatusBadge status={status.peer?.status} /></dd>
                </div>
              </dl>
            </section>
          </section>

          <section className="replication-sync-panel">
            <h2>Core Services</h2>
            <div className="replication-sync-table-wrap">
              <table className="replication-sync-table">
                <thead>
                  <tr>
                    <th>Service</th>
                    <th>Active</th>
                    <th>Enabled</th>
                    <th>Status</th>
                  </tr>
                </thead>
                <tbody>
                  {services.map((service) => (
                    <tr key={service.name}>
                      <td>{service.name}</td>
                      <td>{service.activeState}</td>
                      <td>{service.enabledState}</td>
                      <td><StatusBadge status={service.status} /></td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </section>
        </>
      ) : null}
    </section>
  );
}
