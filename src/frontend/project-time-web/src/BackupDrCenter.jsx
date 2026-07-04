import { useEffect, useMemo, useState } from 'react';
import './backup-dr-center.css';

function statusClass(value) {
  const normalized = String(value ?? '').toLowerCase();
  if (['ready', 'healthy', 'ok', 'completed', 'settings_saved'].includes(normalized)) return 'healthy';
  if (['action_required', 'warning', 'degraded', 'backup_queued'].includes(normalized)) return 'warning';
  return 'critical';
}

function formatStatus(value) {
  const normalized = String(value ?? 'unknown');
  if (normalized === 'action_required') return 'Action required';
  return normalized.replace(/_/g, ' ');
}

function getSessionToken(authSession) {
  return authSession?.sessionToken
    ?? authSession?.token
    ?? authSession?.accessToken
    ?? window.localStorage.getItem('projectPulseSessionToken')
    ?? window.sessionStorage.getItem('projectPulseSessionToken')
    ?? '';
}

function buildAuthHeaders(authSession) {
  const token = getSessionToken(authSession);

  return token ? {
    Authorization: `Bearer ${token}`,
    'X-ProjectPulse-Session': token,
    'X-Project-Pulse-Session': token,
    'X-Session-Token': token
  } : {};
}

const defaultSettingsForm = {
  sftpEnabled: false,
  sftpAuthMode: 'private_key',
  sftpHost: '',
  sftpPort: '22',
  sftpUser: '',
  sftpRemotePath: '',
  sftpKeyPath: '',
  sftpPassword: '',
  azureEnabled: false,
  azureContainerSasUrl: '',
  azureBlobPrefix: 'projectpulse-backups',
  notifyOnSuccess: false,
  notifyOnFailure: true,
  successRecipients: '',
  failureRecipients: '',
  ccRecipients: '',
  scheduleEnabled: false,
  scheduleMode: 'daily',
  scheduleTimeUtc: '06:00',
  scheduleWeeklyDayUtc: '7',
  scheduleMonthlyDayUtc: '1',
  scheduleUploadToSftp: false,
  scheduleUploadToAzure: false
};

function BackupDrCenter({ authSession }) {
  const [backupState, setBackupState] = useState({ loading: true, data: null, error: null });
  const [settingsState, setSettingsState] = useState({
    loading: true,
    saving: false,
    data: null,
    form: defaultSettingsForm,
    error: null,
    message: null
  });
  const [backupRunState, setBackupRunState] = useState({
    reason: '',
    uploadToSftp: false,
    uploadToAzure: false,
    loading: false,
    error: null,
    message: null,
    output: null
  });
  const [backupRunsState, setBackupRunsState] = useState({
    loading: true,
    data: null,
    error: null
  });

  function updateSettingsField(field, value) {
    setSettingsState((current) => ({
      ...current,
      message: null,
      error: null,
      form: {
        ...current.form,
        [field]: value
      }
    }));
  }

  async function loadBackupStatus() {
    try {
      const response = await fetch('/api/system/backup-dr/status', {
        credentials: 'include',
        headers: buildAuthHeaders(authSession)
      });

      const payload = await response.json().catch(() => null);

      if (!response.ok) {
        throw new Error(payload?.message ?? payload?.error ?? `Backup / DR status request failed with ${response.status}`);
      }

      setBackupState({ loading: false, data: payload, error: null });
    } catch (error) {
      setBackupState({ loading: false, data: null, error: error.message });
    }
  }

  async function loadBackupRuns() {
    try {
      const response = await fetch('/api/system/backup-dr/runs', {
        credentials: 'include',
        headers: buildAuthHeaders(authSession)
      });

      const payload = await response.json().catch(() => null);

      if (!response.ok) {
        throw new Error(payload?.message ?? payload?.error ?? `Backup run history request failed with ${response.status}`);
      }

      setBackupRunsState({ loading: false, data: payload, error: null });
    } catch (error) {
      setBackupRunsState({ loading: false, data: null, error: error.message });
    }
  }

  async function loadBackupSettings() {
    try {
      const response = await fetch('/api/system/backup-dr/settings', {
        credentials: 'include',
        headers: buildAuthHeaders(authSession)
      });

      const payload = await response.json().catch(() => null);

      if (!response.ok) {
        throw new Error(payload?.message ?? payload?.error ?? `Backup settings request failed with ${response.status}`);
      }

      setSettingsState({
        loading: false,
        saving: false,
        data: payload,
        error: null,
        message: null,
        form: {
          ...defaultSettingsForm,
          sftpEnabled: Boolean(payload?.sftp?.enabled),
          sftpAuthMode: payload?.sftp?.authMode ?? 'private_key',
          sftpHost: payload?.sftp?.host ?? '',
          sftpPort: payload?.sftp?.port ?? '22',
          sftpUser: payload?.sftp?.user ?? '',
          sftpRemotePath: payload?.sftp?.remotePath ?? '',
          sftpKeyPath: payload?.sftp?.keyPath ?? '',
          sftpPassword: '',
          azureEnabled: Boolean(payload?.azure?.enabled),
          azureContainerSasUrl: '',
          azureBlobPrefix: payload?.azure?.blobPrefix ?? 'projectpulse-backups',
          notifyOnSuccess: Boolean(payload?.notifications?.notifyOnSuccess),
          notifyOnFailure: payload?.notifications?.notifyOnFailure !== false,
          successRecipients: payload?.notifications?.successRecipients ?? '',
          failureRecipients: payload?.notifications?.failureRecipients ?? '',
          ccRecipients: payload?.notifications?.ccRecipients ?? '',
          scheduleEnabled: Boolean(payload?.schedule?.enabled),
          scheduleMode: payload?.schedule?.mode ?? 'daily',
          scheduleTimeUtc: payload?.schedule?.timeUtc ?? '06:00',
          scheduleWeeklyDayUtc: payload?.schedule?.weeklyDayUtc ?? '7',
          scheduleMonthlyDayUtc: payload?.schedule?.monthlyDayUtc ?? '1',
          scheduleUploadToSftp: Boolean(payload?.schedule?.uploadToSftp),
          scheduleUploadToAzure: Boolean(payload?.schedule?.uploadToAzure)
        }
      });
    } catch (error) {
      setSettingsState((current) => ({ ...current, loading: false, saving: false, error: error.message }));
    }
  }

  async function saveBackupSettings(event) {
    event.preventDefault();

    setSettingsState((current) => ({ ...current, saving: true, error: null, message: null }));

    try {
      const response = await fetch('/api/system/backup-dr/settings', {
        method: 'POST',
        credentials: 'include',
        headers: {
          ...buildAuthHeaders(authSession),
          'Content-Type': 'application/json'
        },
        body: JSON.stringify(settingsState.form)
      });

      const payload = await response.json().catch(() => null);

      if (!response.ok) {
        throw new Error(payload?.message ?? payload?.error ?? `Backup settings save failed with ${response.status}`);
      }

      setSettingsState((current) => ({
        ...current,
        saving: false,
        error: null,
        message: payload?.message ?? 'Backup settings saved.'
      }));

      await loadBackupSettings();
      await loadBackupStatus();
      await loadBackupRuns();
    } catch (error) {
      setSettingsState((current) => ({ ...current, saving: false, error: error.message, message: null }));
    }
  }

  async function deleteBackupRun(run) {
    const requestId = run?.requestId;

    if (!requestId) {
      window.alert('This backup run does not have a request ID and cannot be deleted from the UI.');
      return;
    }

    const confirmed = window.confirm(`Delete backup ${requestId}? This removes the local bundle, checksum, run folder, and result files from the server.`);
    if (!confirmed) return;

    const reason = window.prompt('Enter a deletion reason with at least 8 characters:');
    if (!reason || reason.trim().length < 8) {
      window.alert('Deletion cancelled. A reason with at least 8 characters is required.');
      return;
    }

    try {
      const response = await fetch('/api/system/backup-dr/runs/delete', {
        method: 'POST',
        credentials: 'include',
        headers: {
          ...buildAuthHeaders(authSession),
          'Content-Type': 'application/json'
        },
        body: JSON.stringify({
          requestId,
          reason: reason.trim()
        })
      });

      const payload = await response.json().catch(() => null);

      if (!response.ok) {
        throw new Error(payload?.message ?? payload?.error ?? `Backup delete request failed with ${response.status}`);
      }

      window.alert(payload?.message ?? 'Backup deleted.');
      await loadBackupRuns();
      await loadBackupStatus();
    } catch (error) {
      window.alert(error instanceof Error ? error.message : 'Backup delete failed.');
    }
  }

  async function runManualBackup(event) {
    event.preventDefault();

    if (!backupRunState.reason || backupRunState.reason.trim().length < 8) {
      setBackupRunState((current) => ({
        ...current,
        error: 'Please enter a backup reason with at least 8 characters.'
      }));
      return;
    }

    setBackupRunState((current) => ({ ...current, loading: true, error: null, message: null, output: null }));

    try {
      const response = await fetch('/api/system/backup-dr/run', {
        method: 'POST',
        credentials: 'include',
        headers: {
          ...buildAuthHeaders(authSession),
          'Content-Type': 'application/json'
        },
        body: JSON.stringify({
          uploadToSftp: backupRunState.uploadToSftp,
          uploadToAzure: backupRunState.uploadToAzure,
          reason: backupRunState.reason.trim()
        })
      });

      const payload = await response.json().catch(() => null);

      if (!response.ok) {
        throw new Error(payload?.message ?? payload?.error ?? `Backup request failed with ${response.status}`);
      }

      setBackupRunState((current) => ({
        ...current,
        loading: false,
        error: null,
        message: payload?.message ?? 'Backup request queued.',
        output: payload?.requestId ? `Request ID: ${payload.requestId}` : null
      }));

      await loadBackupStatus();
    } catch (error) {
      setBackupRunState((current) => ({
        ...current,
        loading: false,
        error: error.message,
        message: null
      }));
    }
  }

  useEffect(() => {
    loadBackupStatus();
    loadBackupSettings();
    loadBackupRuns();
  }, [authSession?.sessionToken, authSession?.token, authSession?.accessToken]);

  const groupedChecks = useMemo(() => {
    const groups = new Map();

    (backupState.data?.checks ?? []).forEach((check) => {
      const category = check.category ?? 'Other';
      if (!groups.has(category)) groups.set(category, []);
      groups.get(category).push(check);
    });

    return [...groups.entries()].map(([category, checks]) => ({ category, checks }));
  }, [backupState.data]);

  return (
    <section id="backup-dr-center" className="panel backup-dr-center">
      <div className="backup-dr-hero">
        <div>
          <p className="eyebrow">System Operations</p>
          <h2>Backup / Disaster Recovery Center</h2>
          <p>
            Configure backup targets, create local or external backup bundles, manage backup cadence, and review recovery readiness.
          </p>
        </div>

        <button type="button" className="secondary-action" onClick={() => { loadBackupStatus(); loadBackupSettings(); loadBackupRuns(); }}>
          Refresh readiness
        </button>
      </div>

      <div className="backup-dr-summary-grid">
        <article className="backup-dr-summary-card">
          <span>Readiness Checks</span>
          <strong>{backupState.loading ? 'Checking...' : `${backupState.data?.readyCount ?? 0}/${backupState.data?.totalCount ?? 0} ready`}</strong>
          <small>{backupState.data?.generatedAt ? `Checked ${new Date(backupState.data.generatedAt).toLocaleString()}` : 'Waiting for readiness check.'}</small>
        </article>

        <article className="backup-dr-summary-card">
          <span>Action Required</span>
          <strong>{backupState.loading ? 'Checking...' : backupState.data?.actionRequiredCount ?? 0}</strong>
          <small>Items that need backup, runbook, or recovery attention.</small>
        </article>

        <article className="backup-dr-summary-card">
          <span>Overall Status</span>
          <strong>{backupState.loading ? 'Checking...' : formatStatus(backupState.data?.status)}</strong>
          <small>Backups can be local, SFTP, Azure Blob, or scheduled.</small>
        </article>
      </div>

      {backupState.error ? <div className="backup-dr-alert critical">{backupState.error}</div> : null}

      <section className="backup-dr-card">
        <div className="backup-dr-card-header">
          <div>
            <p className="eyebrow">Settings</p>
            <h3>Backup Settings</h3>
          </div>
        </div>

        <form className="backup-settings-form" onSubmit={saveBackupSettings}>
          <div className="backup-settings-grid">
            <fieldset>
              <legend>SFTP Target</legend>

              <label className="backup-run-checkbox">
                <input
                  type="checkbox"
                  checked={settingsState.form.sftpEnabled}
                  onChange={(event) => updateSettingsField('sftpEnabled', event.target.checked)}
                />
                Enable SFTP backup target
              </label>

              <label>
                Authentication type
                <select value={settingsState.form.sftpAuthMode} onChange={(event) => updateSettingsField('sftpAuthMode', event.target.value)}>
                  <option value="private_key">Private key</option>
                  <option value="password">Username/password</option>
                </select>
              </label>

              <label>
                Host
                <input value={settingsState.form.sftpHost} onChange={(event) => updateSettingsField('sftpHost', event.target.value)} placeholder="sftp.example.com" />
              </label>

              <label>
                Port
                <input value={settingsState.form.sftpPort} onChange={(event) => updateSettingsField('sftpPort', event.target.value)} placeholder="22" />
              </label>

              <label>
                Username
                <input value={settingsState.form.sftpUser} onChange={(event) => updateSettingsField('sftpUser', event.target.value)} />
              </label>

              <label>
                Remote path
                <input value={settingsState.form.sftpRemotePath} onChange={(event) => updateSettingsField('sftpRemotePath', event.target.value)} placeholder="/backups/projectpulse" />
              </label>

              {settingsState.form.sftpAuthMode === 'private_key' ? (
                <label>
                  Private key path
                  <input value={settingsState.form.sftpKeyPath} onChange={(event) => updateSettingsField('sftpKeyPath', event.target.value)} placeholder="/opt/project-time-platform/config/keys/sftp_key" />
                </label>
              ) : (
                <label>
                  Password
                  <input type="password" value={settingsState.form.sftpPassword} onChange={(event) => updateSettingsField('sftpPassword', event.target.value)} placeholder={settingsState.data?.sftp?.passwordConfigured ? 'Password already configured. Leave blank to keep.' : ''} />
                </label>
              )}
            </fieldset>

            <fieldset>
              <legend>Azure Blob Target</legend>

              <label className="backup-run-checkbox">
                <input
                  type="checkbox"
                  checked={settingsState.form.azureEnabled}
                  onChange={(event) => updateSettingsField('azureEnabled', event.target.checked)}
                />
                Enable Azure Blob backup target
              </label>

              <label>
                Container SAS URL
                <input
                  type="password"
                  value={settingsState.form.azureContainerSasUrl}
                  onChange={(event) => updateSettingsField('azureContainerSasUrl', event.target.value)}
                  placeholder={settingsState.data?.azure?.containerSasUrlConfigured ? `Configured: ${settingsState.data.azure.containerSasUrlMasked}. Leave blank to keep.` : 'https://account.blob.core.windows.net/container?sv=...'}
                />
              </label>

              <label>
                Blob prefix
                <input value={settingsState.form.azureBlobPrefix} onChange={(event) => updateSettingsField('azureBlobPrefix', event.target.value)} placeholder="projectpulse-backups" />
              </label>
            </fieldset>

            <fieldset>
              <legend>Email Notifications</legend>

              <label className="backup-run-checkbox">
                <input type="checkbox" checked={settingsState.form.notifyOnSuccess} onChange={(event) => updateSettingsField('notifyOnSuccess', event.target.checked)} />
                Notify on successful backup
              </label>

              <label className="backup-run-checkbox">
                <input type="checkbox" checked={settingsState.form.notifyOnFailure} onChange={(event) => updateSettingsField('notifyOnFailure', event.target.checked)} />
                Notify on failed backup
              </label>

              <label>
                Success recipients
                <input value={settingsState.form.successRecipients} onChange={(event) => updateSettingsField('successRecipients', event.target.value)} placeholder="user1@example.com,user2@example.com" />
              </label>

              <label>
                Failure recipients
                <input value={settingsState.form.failureRecipients} onChange={(event) => updateSettingsField('failureRecipients', event.target.value)} placeholder="admin@example.com" />
              </label>

              <label>
                CC recipients
                <input value={settingsState.form.ccRecipients} onChange={(event) => updateSettingsField('ccRecipients', event.target.value)} />
              </label>
            </fieldset>

            <fieldset>
              <legend>Schedule</legend>

              <label className="backup-run-checkbox">
                <input type="checkbox" checked={settingsState.form.scheduleEnabled} onChange={(event) => updateSettingsField('scheduleEnabled', event.target.checked)} />
                Enable scheduled backups
              </label>

              <label>
                Cadence
                <select value={settingsState.form.scheduleMode} onChange={(event) => updateSettingsField('scheduleMode', event.target.value)}>
                  <option value="daily">Daily</option>
                  <option value="weekly">Weekly</option>
                  <option value="monthly">Monthly</option>
                </select>
              </label>

              <label>
                Time UTC
                <input value={settingsState.form.scheduleTimeUtc} onChange={(event) => updateSettingsField('scheduleTimeUtc', event.target.value)} placeholder="06:00" />
              </label>

              {settingsState.form.scheduleMode === 'weekly' ? (
                <label>
                  Weekly day UTC
                  <select value={settingsState.form.scheduleWeeklyDayUtc} onChange={(event) => updateSettingsField('scheduleWeeklyDayUtc', event.target.value)}>
                    <option value="1">Monday</option>
                    <option value="2">Tuesday</option>
                    <option value="3">Wednesday</option>
                    <option value="4">Thursday</option>
                    <option value="5">Friday</option>
                    <option value="6">Saturday</option>
                    <option value="7">Sunday</option>
                  </select>
                </label>
              ) : null}

              {settingsState.form.scheduleMode === 'monthly' ? (
                <label>
                  Monthly day UTC
                  <input value={settingsState.form.scheduleMonthlyDayUtc} onChange={(event) => updateSettingsField('scheduleMonthlyDayUtc', event.target.value)} placeholder="1" />
                </label>
              ) : null}

              <label className="backup-run-checkbox">
                <input type="checkbox" checked={settingsState.form.scheduleUploadToSftp} onChange={(event) => updateSettingsField('scheduleUploadToSftp', event.target.checked)} />
                Upload scheduled backup to SFTP
              </label>

              <label className="backup-run-checkbox">
                <input type="checkbox" checked={settingsState.form.scheduleUploadToAzure} onChange={(event) => updateSettingsField('scheduleUploadToAzure', event.target.checked)} />
                Upload scheduled backup to Azure Blob
              </label>
            </fieldset>
          </div>

          {settingsState.error ? <div className="backup-dr-alert critical">{settingsState.error}</div> : null}
          {settingsState.message ? <div className="backup-dr-alert healthy">{settingsState.message}</div> : null}

          <button type="submit" className="primary-action" disabled={settingsState.saving}>
            {settingsState.saving ? 'Saving settings...' : 'Save backup settings'}
          </button>
        </form>
      </section>

      <section className="backup-dr-card">
        <div className="backup-dr-card-header">
          <div>
            <p className="eyebrow">Manual Backup</p>
            <h3>Create Backup Bundle</h3>
          </div>
        </div>

        <form className="backup-run-form" onSubmit={runManualBackup}>
          <label>
            Backup reason
            <textarea
              rows={3}
              value={backupRunState.reason}
              onChange={(event) => setBackupRunState((current) => ({ ...current, reason: event.target.value }))}
              placeholder="Example: Creating backup before deployment or configuration change."
            />
          </label>

          <label className="backup-run-checkbox">
            <input type="checkbox" checked={backupRunState.uploadToSftp} onChange={(event) => setBackupRunState((current) => ({ ...current, uploadToSftp: event.target.checked }))} />
            Upload to configured SFTP target after local backup completes
          </label>

          <label className="backup-run-checkbox">
            <input type="checkbox" checked={backupRunState.uploadToAzure} onChange={(event) => setBackupRunState((current) => ({ ...current, uploadToAzure: event.target.checked }))} />
            Upload to configured Azure Blob target after local backup completes
          </label>

          {backupRunState.error ? <div className="backup-dr-alert critical">{backupRunState.error}</div> : null}
          {backupRunState.message ? <div className="backup-dr-alert healthy">{backupRunState.message}</div> : null}

          <button type="submit" className="primary-action" disabled={backupRunState.loading}>
            {backupRunState.loading ? 'Queuing backup...' : 'Create backup now'}
          </button>

          {backupRunState.output ? (
            <details className="backup-run-output">
              <summary>Backup request</summary>
              <pre>{backupRunState.output}</pre>
            </details>
          ) : null}
        </form>
      </section>


      <section className="backup-dr-card">
        <div className="backup-dr-card-header">
          <div>
            <p className="eyebrow">History</p>
            <h3>Recent Backup Runs</h3>
          </div>
        </div>

        {backupRunsState.error ? <div className="backup-dr-alert critical">{backupRunsState.error}</div> : null}

        <div className="backup-run-history-list">
          {(backupRunsState.data?.runs ?? []).map((run) => (
            <article className="backup-run-history-row" key={run.requestId || run.resultFile}>
              <div>
                <strong>{run.requestId || 'Unknown request'}</strong>
                <small>{run.reason || 'No reason captured'}</small>
              </div>

              <div>
                <span className={`backup-dr-status-pill ${statusClass(run.status)}`}>
                  {formatStatus(run.status)}
                </span>
                <small>Exit code: {run.exitCode}</small>
              </div>

              <div>
                <strong>Bundle</strong>
                <small>{run.backupBundle || 'Not captured'}</small>
              </div>

              <div>
                <strong>Database dump</strong>
                <small>{run.databaseDump || 'Not captured'}</small>
              </div>

              <div>
                <strong>External upload</strong>
                <small>SFTP: {run.sftpUploadStatus || 'not requested'} | Azure: {run.azureUploadStatus || 'not requested'}</small>
              </div>

              <div className="backup-run-history-actions">
                <button type="button" className="backup-delete-button" onClick={() => deleteBackupRun(run)}>
                  Delete backup
                </button>
              </div>

              <details>
                <summary>Output</summary>
                <pre>{run.output || 'No output captured.'}</pre>
              </details>
            </article>
          ))}

          {backupRunsState.loading ? <p className="backup-dr-muted">Loading backup run history...</p> : null}
          {!backupRunsState.loading && (backupRunsState.data?.runs ?? []).length === 0 ? (
            <p className="backup-dr-muted">No backup run results have been captured yet.</p>
          ) : null}
        </div>
      </section>

      <section className="backup-dr-card">
        <div className="backup-dr-card-header">
          <div>
            <p className="eyebrow">Readiness</p>
            <h3>Backup / DR Checks</h3>
          </div>
        </div>

        <div className="backup-dr-groups">
          {groupedChecks.map((group) => (
            <div className="backup-dr-group" key={group.category}>
              <h4>{group.category}</h4>

              <div className="backup-dr-check-list">
                {group.checks.map((check) => (
                  <article className="backup-dr-check-row" key={check.key}>
                    <div>
                      <strong>{check.name}</strong>
                      <small>{check.key}</small>
                    </div>

                    <p>{check.message}</p>

                    <span className={`backup-dr-status-pill ${statusClass(check.status)}`}>
                      {formatStatus(check.status)}
                    </span>

                    <details>
                      <summary>Details</summary>
                      <pre>{JSON.stringify(check.details ?? {}, null, 2)}</pre>
                    </details>
                  </article>
                ))}
              </div>
            </div>
          ))}

          {backupState.loading ? <p className="backup-dr-muted">Loading Backup / DR readiness...</p> : null}
        </div>
      </section>
    </section>
  );
}

export default BackupDrCenter;
