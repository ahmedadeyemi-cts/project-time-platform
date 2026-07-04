import { useEffect, useMemo, useState } from 'react';
import './restore-validation-center.css';

const statusLabels = {
  ready: 'Ready',
  warning: 'Warning',
  action_required: 'Action Required',
  unknown: 'Unknown'
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
    <span className={`restore-validation-status-badge restore-validation-status-badge--${normalized}`}>
      {statusLabel(normalized)}
    </span>
  );
}

function SummaryCard({ title, value, detail, status }) {
  return (
    <article className="restore-validation-summary-card">
      <div className="restore-validation-summary-card__header">
        <span>{title}</span>
        {status ? <StatusBadge status={status} /> : null}
      </div>
      <strong>{value || 'Not available'}</strong>
      {detail ? <p>{detail}</p> : null}
    </article>
  );
}

export default function RestoreValidationCenter({ authSession }) {
  const [status, setStatus] = useState(null);
  const [loadState, setLoadState] = useState('loading');
  const [error, setError] = useState('');
  const [refreshing, setRefreshing] = useState(false);
  const [backups, setBackups] = useState([]);
  const [selectedBackup, setSelectedBackup] = useState('');
  const [restorePointState, setRestorePointState] = useState({ loading: true, saving: false, error: '', message: '' });

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
      const data = await fetchJson('/api/system/restore-validation/status', {
        headers: { Accept: 'application/json' }
      });

      setStatus(data);
      setLoadState('loaded');
    } catch (err) {
      setError(err?.message || 'Unable to load restore validation status.');
      setLoadState('error');
    } finally {
      setRefreshing(false);
    }
  }

  async function loadRestorePoints() {
    setRestorePointState((current) => ({ ...current, loading: true, error: '', message: '' }));

    try {
      const [backupData, settingsData] = await Promise.all([
        fetchJson('/api/system/restore-validation/backups', {
          headers: { Accept: 'application/json' }
        }),
        fetchJson('/api/system/restore-validation/settings', {
          headers: { Accept: 'application/json' }
        })
      ]);

      setBackups(backupData.backups || []);
      setSelectedBackup(settingsData.selectedBackup || '');
      setRestorePointState((current) => ({ ...current, loading: false }));
    } catch (err) {
      setRestorePointState((current) => ({
        ...current,
        loading: false,
        error: err?.message || 'Unable to load restore points.'
      }));
    }
  }

  async function saveRestorePoint(event) {
    event.preventDefault();

    setRestorePointState((current) => ({ ...current, saving: true, error: '', message: '' }));

    try {
      const payload = await fetchJson('/api/system/restore-validation/settings', {
        method: 'POST',
        headers: {
          Accept: 'application/json',
          'Content-Type': 'application/json'
        },
        body: JSON.stringify({
          selectedBackup
        })
      });

      setRestorePointState((current) => ({
        ...current,
        saving: false,
        message: payload.message || 'Restore point selection saved.'
      }));

      window.setTimeout(loadStatus, 2500);
    } catch (err) {
      setRestorePointState((current) => ({
        ...current,
        saving: false,
        error: err?.message || 'Unable to save restore point selection.'
      }));
    }
  }

  useEffect(() => {
    loadStatus();
    loadRestorePoints();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [authSession?.sessionToken, authSession?.token, authSession?.accessToken]);

  const checks = useMemo(() => status?.checks || [], [status]);

  return (
    <section id="restore-validation-center" className="panel timesheet-page restore-validation-page">
      <section className="restore-validation-hero">
        <div>
          <p className="restore-validation-eyebrow">System Operations</p>
          <h1>Restore Validation</h1>
          <p>
            Confirm that PHD backups are usable before a disaster recovery event.
            This validation does not restore over production.
          </p>
        </div>

        <div className="restore-validation-hero__actions">
          {status?.overallStatus ? <StatusBadge status={status.overallStatus} /> : null}
          <button type="button" onClick={loadStatus} disabled={refreshing}>
            {refreshing ? 'Refreshing...' : 'Refresh validation'}
          </button>
        </div>
      </section>

      {loadState === 'loading' ? (
        <section className="restore-validation-panel">
          <p>Loading restore validation status...</p>
        </section>
      ) : null}

      {error ? (
        <section className="restore-validation-alert">
          <strong>Unable to load restore validation.</strong>
          <p>{error}</p>
        </section>
      ) : null}

      {status ? (
        <>
          <section className="restore-validation-mode-banner">
            <div>
              <span>Safe validation mode</span>
              <h2>Backup inspected without touching production</h2>
              <p>
                PHD checks the backup bundle, checksum, database dump, configuration archive,
                application snapshot, and DR runbook. No production restore is performed.
              </p>
            </div>
            <StatusBadge status={status.overallStatus} />
          </section>

          <section className="restore-validation-panel">
            <div className="restore-validation-section-header">
              <div>
                <p className="restore-validation-eyebrow">Restore Point</p>
                <h2>Select Backup to Validate</h2>
                <p>
                  Choose a known working backup point before planning a restore. Selecting Latest backup validates the newest available bundle.
                </p>
              </div>
            </div>

            <form className="restore-validation-restore-point-form" onSubmit={saveRestorePoint}>
              <label>
                <span>Backup restore point</span>
                <select
                  value={selectedBackup}
                  onChange={(event) => setSelectedBackup(event.target.value)}
                  disabled={restorePointState.loading || restorePointState.saving}
                >
                  <option value="">Latest backup</option>
                  {backups.map((backup) => (
                    <option key={backup.name} value={backup.name}>
                      {backup.name} · {backup.ageHours} hours old · {formatBytes(backup.sizeBytes)}
                    </option>
                  ))}
                </select>
              </label>

              <div className="restore-validation-selected-backup">
                <strong>Currently validated:</strong>{' '}
                {status.restorePoint?.resolvedBackup || status.backup?.name || 'Not available'}
              </div>

              {restorePointState.error ? <div className="restore-validation-alert inline">{restorePointState.error}</div> : null}
              {restorePointState.message ? <div className="restore-validation-success inline">{restorePointState.message}</div> : null}

              <div className="restore-validation-restore-point-actions">
                <button type="submit" disabled={restorePointState.saving || restorePointState.loading}>
                  {restorePointState.saving ? 'Saving...' : 'Apply restore point'}
                </button>
                <button type="button" onClick={loadRestorePoints} disabled={restorePointState.loading || restorePointState.saving}>
                  Reload list
                </button>
              </div>
            </form>
          </section>

          <section className="restore-validation-summary-grid">
            <SummaryCard
              title="Validation status"
              value={statusLabel(status.overallStatus)}
              detail={`Ready: ${status.summary?.ready ?? 0} · Warning: ${status.summary?.warning ?? 0} · Action Required: ${status.summary?.actionRequired ?? 0}`}
              status={status.overallStatus}
            />

            <SummaryCard
              title={status.restorePoint?.mode === 'selected' ? 'Selected backup' : 'Backup bundle'}
              value={status.restorePoint?.resolvedBackup || status.backup?.name}
              detail={`Age: ${status.backup?.ageHours ?? 'N/A'} hours · Size: ${formatBytes(status.backup?.sizeBytes)}`}
              status={status.backup ? 'ready' : 'action_required'}
            />

            <SummaryCard
              title="Database dump"
              value="pg_restore inspection"
              detail="The latest database dump was inspected for restore readability."
              status={checks.find((check) => check.key === 'database_dump_readable')?.status || 'unknown'}
            />

            <SummaryCard
              title="Runbook"
              value={status.runbook?.exists ? 'Available' : 'Missing'}
              detail={status.runbook?.path}
              status={status.runbook?.exists ? 'ready' : 'warning'}
            />
          </section>

          <section className="restore-validation-panel">
            <div className="restore-validation-section-header">
              <div>
                <p className="restore-validation-eyebrow">Validation Checklist</p>
                <h2>Latest Restore Validation Results</h2>
                <p>
                  Generated {formatDate(status.generatedAt)} on {status.host?.hostname || 'this server'}.
                  Validation ID: {status.validationId || 'Not available'}.
                </p>
              </div>
            </div>

            <div className="restore-validation-check-list">
              {checks.map((check, index) => (
                <article className="restore-validation-check" key={`${check.key}-${index}`}>
                  <div>
                    <span className="restore-validation-check__category">{check.category}</span>
                    <h3>{check.name}</h3>
                    <p>{check.detail}</p>
                    {check.evidence ? <small>{check.evidence}</small> : null}
                  </div>
                  <StatusBadge status={check.status} />
                </article>
              ))}
            </div>
          </section>

          <section className="restore-validation-two-column">
            <section className="restore-validation-panel">
              <p className="restore-validation-eyebrow">Backup Detail</p>
              <h2>Validated Backup</h2>
              <dl className="restore-validation-details">
                <div>
                  <dt>Bundle</dt>
                  <dd>{status.backup?.name || 'Not available'}</dd>
                </div>
                <div>
                  <dt>Path</dt>
                  <dd>{status.backup?.path || 'Not available'}</dd>
                </div>
                <div>
                  <dt>Checksum</dt>
                  <dd>{status.backup?.checksumExists ? 'Present' : 'Missing'}</dd>
                </div>
                <div>
                  <dt>Size</dt>
                  <dd>{formatBytes(status.backup?.sizeBytes)}</dd>
                </div>
              </dl>
            </section>

            <section className="restore-validation-panel">
              <p className="restore-validation-eyebrow">Runbook</p>
              <h2>DR Restore Runbook</h2>
              <p>
                Use the runbook for controlled manual restore planning. Automated production restore
                should not be enabled until a non-production restore sandbox exists.
              </p>
              <dl className="restore-validation-details">
                <div>
                  <dt>Status</dt>
                  <dd>{status.runbook?.exists ? 'Available' : 'Missing'}</dd>
                </div>
                <div>
                  <dt>Location</dt>
                  <dd>{status.runbook?.path || 'Not available'}</dd>
                </div>
              </dl>
            </section>
          </section>
        </>
      ) : null}
    </section>
  );
}
