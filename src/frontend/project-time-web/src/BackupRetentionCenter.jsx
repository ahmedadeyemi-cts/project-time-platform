import { useEffect, useMemo, useState } from 'react';
import './backup-retention-center.css';

const statusLabels = {
  ready: 'Ready',
  warning: 'Warning',
  action_required: 'Action Required',
  running: 'Running',
  not_run: 'Not Run',
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
    <span className={`backup-retention-status-badge backup-retention-status-badge--${normalized}`}>
      {statusLabel(normalized)}
    </span>
  );
}

function SummaryCard({ title, value, detail, status }) {
  return (
    <article className="backup-retention-summary-card">
      <div className="backup-retention-summary-card__header">
        <span>{title}</span>
        {status ? <StatusBadge status={status} /> : null}
      </div>
      <strong>{value || 'Not available'}</strong>
      {detail ? <p>{detail}</p> : null}
    </article>
  );
}

export default function BackupRetentionCenter({ authSession }) {
  const [status, setStatus] = useState(null);
  const [loadState, setLoadState] = useState('loading');
  const [error, setError] = useState('');
  const [refreshing, setRefreshing] = useState(false);
  const [deleteReason, setDeleteReason] = useState('');
  const [deleteMessage, setDeleteMessage] = useState('');
  const [deleteError, setDeleteError] = useState('');
  const [deletingBackup, setDeletingBackup] = useState('');

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
      const data = await fetchJson('/api/system/backup-retention/status', {
        headers: { Accept: 'application/json' }
      });

      setStatus(data);
      setLoadState('loaded');
    } catch (err) {
      setError(err?.message || 'Unable to load backup retention status.');
      setLoadState('error');
    } finally {
      setRefreshing(false);
    }
  }

  async function deleteBackup(backup) {
    setDeleteMessage('');
    setDeleteError('');

    const confirmed = window.confirm(
      `Delete this backup point?\n\n${backup.name}\n\nThis will delete the .tgz file and matching .sha256 file.`
    );

    if (!confirmed) return;

    setDeletingBackup(backup.name);

    try {
      const payload = await fetchJson('/api/system/backup-retention/delete', {
        method: 'POST',
        headers: {
          Accept: 'application/json',
          'Content-Type': 'application/json'
        },
        body: JSON.stringify({
          backupName: backup.name,
          reason: deleteReason,
          confirm: true
        })
      });

      setDeleteMessage(payload.message || 'Backup deletion queued.');

      window.setTimeout(loadStatus, 2500);
    } catch (err) {
      setDeleteError(err?.message || 'Unable to delete backup.');
    } finally {
      setDeletingBackup('');
    }
  }

  useEffect(() => {
    loadStatus();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [authSession?.sessionToken, authSession?.token, authSession?.accessToken]);

  const backups = useMemo(() => status?.backups || [], [status]);
  const newestBackup = backups[0];
  const deleteStatus = status?.deleteStatus;

  return (
    <section id="backup-retention-center" className="panel timesheet-page backup-retention-page">
      <section className="backup-retention-hero">
        <div>
          <p className="backup-retention-eyebrow">System Operations</p>
          <h1>Backup Retention</h1>
          <p>
            Review ProjectPulse backup points and safely remove older backups when they are no longer needed.
          </p>
        </div>

        <div className="backup-retention-hero__actions">
          <button type="button" onClick={loadStatus} disabled={refreshing}>
            {refreshing ? 'Refreshing...' : 'Refresh'}
          </button>
        </div>
      </section>

      {loadState === 'loading' ? (
        <section className="backup-retention-panel">
          <p>Loading backup retention status...</p>
        </section>
      ) : null}

      {error ? (
        <section className="backup-retention-alert">
          <strong>Unable to load Backup Retention.</strong>
          <p>{error}</p>
        </section>
      ) : null}

      {status ? (
        <>
          <section className="backup-retention-mode-banner">
            <div>
              <span>Safe deletion controls</span>
              <h2>Manual cleanup with restore-point protection</h2>
              <p>
                ProjectPulse protects the last remaining backup and the restore point currently selected in Restore Validation.
              </p>
            </div>
            <StatusBadge status={status.backupCount > 0 ? 'ready' : 'warning'} />
          </section>

          <section className="backup-retention-summary-grid">
            <SummaryCard
              title="Backup points"
              value={String(status.backupCount ?? 0)}
              detail={status.canDelete ? 'Old backup points can be deleted.' : 'At least two backups are required before deletion is allowed.'}
              status={status.backupCount > 0 ? 'ready' : 'warning'}
            />

            <SummaryCard
              title="Newest backup"
              value={newestBackup?.name}
              detail={newestBackup ? `${newestBackup.ageHours} hours old · ${formatBytes(newestBackup.sizeBytes)}` : 'No backups found.'}
              status={newestBackup ? 'ready' : 'warning'}
            />

            <SummaryCard
              title="Selected restore point"
              value={status.selectedRestorePoint || 'Latest backup'}
              detail="Protected if a specific restore point is selected."
              status="ready"
            />

            <SummaryCard
              title="Last delete action"
              value={deleteStatus?.overallStatus ? statusLabel(deleteStatus.overallStatus) : 'Not run'}
              detail={deleteStatus?.message || 'No backup delete request has been processed yet.'}
              status={deleteStatus?.overallStatus || 'not_run'}
            />
          </section>

          <section className="backup-retention-panel">
            <div className="backup-retention-section-header">
              <div>
                <p className="backup-retention-eyebrow">Delete Reason</p>
                <h2>Retention Notes</h2>
                <p>
                  Optional note used when queueing a delete request.
                </p>
              </div>
            </div>

            <textarea
              value={deleteReason}
              onChange={(event) => setDeleteReason(event.target.value)}
              placeholder="Example: Removing older backup after newer restore point was validated."
              rows={3}
            />

            {deleteMessage ? <div className="backup-retention-success inline">{deleteMessage}</div> : null}
            {deleteError ? <div className="backup-retention-alert inline">{deleteError}</div> : null}
          </section>

          <section className="backup-retention-panel">
            <div className="backup-retention-section-header">
              <div>
                <p className="backup-retention-eyebrow">Backup Inventory</p>
                <h2>Available Backup Points</h2>
                <p>
                  Generated {formatDate(status.generatedAt)}.
                </p>
              </div>
            </div>

            <div className="backup-retention-table-wrap">
              <table className="backup-retention-table">
                <thead>
                  <tr>
                    <th>Backup</th>
                    <th>Age</th>
                    <th>Size</th>
                    <th>Checksum</th>
                    <th>Restore Point</th>
                    <th>Action</th>
                  </tr>
                </thead>
                <tbody>
                  {backups.map((backup) => {
                    const protectedByCount = !status.canDelete;
                    const protectedByRestorePoint = backup.isSelectedRestorePoint;
                    const disabled = protectedByCount || protectedByRestorePoint || deletingBackup === backup.name;

                    return (
                      <tr key={backup.name}>
                        <td>{backup.name}</td>
                        <td>{backup.ageHours} hours</td>
                        <td>{formatBytes(backup.sizeBytes)}</td>
                        <td>{backup.checksumExists ? 'Present' : 'Missing'}</td>
                        <td>{backup.isSelectedRestorePoint ? 'Selected' : '—'}</td>
                        <td>
                          <button
                            type="button"
                            className="backup-retention-delete-button"
                            disabled={disabled}
                            onClick={() => deleteBackup(backup)}
                            title={
                              protectedByCount
                                ? 'Cannot delete the last remaining backup.'
                                : protectedByRestorePoint
                                  ? 'Cannot delete the backup selected in Restore Validation.'
                                  : 'Delete backup point.'
                            }
                          >
                            {deletingBackup === backup.name ? 'Queueing...' : 'Delete'}
                          </button>
                        </td>
                      </tr>
                    );
                  })}

                  {backups.length === 0 ? (
                    <tr>
                      <td colSpan="6">No backup bundles were found.</td>
                    </tr>
                  ) : null}
                </tbody>
              </table>
            </div>
          </section>
        </>
      ) : null}
    </section>
  );
}
