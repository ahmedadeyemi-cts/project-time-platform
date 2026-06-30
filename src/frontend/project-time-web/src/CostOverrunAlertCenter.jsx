import { useEffect, useMemo, useState } from 'react';
import './cost-overrun-alert-center.css';

function getStoredAuthSession() {
  try {
    const rawSession = window.localStorage.getItem('projectPulseAuthSession');
    if (!rawSession) return null;
    return JSON.parse(rawSession);
  } catch {
    return null;
  }
}

function getProjectPulseAuthHeaders() {
  const session = getStoredAuthSession();
  return session?.sessionToken ? { 'X-ProjectPulse-Session': session.sessionToken } : {};
}

async function readApiErrorMessage(response, path) {
  const raw = await response.text();
  if (!raw) return `${path} returned HTTP ${response.status}`;

  try {
    const parsed = JSON.parse(raw);
    return `${path} returned HTTP ${response.status}: ${parsed.message || parsed.detail || parsed.status || raw}`;
  } catch {
    return `${path} returned HTTP ${response.status}: ${raw}`;
  }
}

async function fetchJson(path) {
  const response = await fetch(path, { headers: getProjectPulseAuthHeaders() });
  if (!response.ok) throw new Error(await readApiErrorMessage(response, path));
  return response.json();
}

async function postJson(path, payload) {
  const response = await fetch(path, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', ...getProjectPulseAuthHeaders() },
    body: JSON.stringify(payload ?? {})
  });

  if (!response.ok) throw new Error(await readApiErrorMessage(response, path));
  return response.json();
}

function fmtMoney(value) {
  return Number(value ?? 0).toLocaleString(undefined, { style: 'currency', currency: 'USD', maximumFractionDigits: 0 });
}

function fmtNumber(value) {
  return Number(value ?? 0).toLocaleString(undefined, { maximumFractionDigits: 2 });
}

function labelAlertType(value) {
  return String(value ?? '').replaceAll('_', ' ').replace(/\b\w/g, (match) => match.toUpperCase());
}

function fmtDateTime(value) {
  if (!value) return 'Not recorded';
  return new Date(value).toLocaleString();
}

export default function CostOverrunAlertCenter({ canManageCostAlerts = false }) {
  const [alertData, setAlertData] = useState({ loading: true, data: null, error: null });
  const [actionStatus, setActionStatus] = useState('');
  const [thresholdHours, setThresholdHours] = useState('8');
  const [queueNotifications, setQueueNotifications] = useState(false);
  const [alertNotes, setAlertNotes] = useState({});

  async function loadAlerts() {
    setAlertData((current) => ({ ...current, loading: true, error: null }));

    try {
      const result = await fetchJson('/api/projects/cost-alerts');
      setAlertData({ loading: false, data: result, error: null });
    } catch (error) {
      setAlertData({
        loading: false,
        data: null,
        error: error instanceof Error ? error.message : 'Unable to load cost alerts.'
      });
    }
  }

  useEffect(() => {
    loadAlerts();
  }, []);

  const alerts = alertData.data?.alerts ?? [];
  const candidates = alertData.data?.candidates ?? [];

  const summary = useMemo(() => {
    const openAlerts = alerts.filter((alert) => alert.alertStatus === 'open');
    const acknowledgedAlerts = alerts.filter((alert) => alert.alertStatus === 'acknowledged');
    const highAlerts = alerts.filter((alert) => alert.alertSeverity === 'high');
    const queuedAlerts = alerts.filter((alert) => alert.notificationQueuedAt);
    const heldAlerts = alerts.filter((alert) => alert.routingStatus === 'hold');

    return {
      openCount: openAlerts.length,
      acknowledgedCount: acknowledgedAlerts.length,
      highCount: highAlerts.length,
      queuedCount: queuedAlerts.length,
      heldCount: heldAlerts.length,
      candidateCount: candidates.length
    };
  }, [alerts, candidates]);

  function getAlertNote(alertId) {
    return alertNotes[alertId] ?? '';
  }

  function setAlertNote(alertId, value) {
    setAlertNotes((current) => ({ ...current, [alertId]: value }));
  }

  async function evaluateAlerts() {
    if (!canManageCostAlerts) {
      setActionStatus('Cost alert evaluation is restricted to administrators and project/team coordinators.');
      return;
    }

    try {
      setActionStatus('Evaluating project cost alerts...');
      const result = await postJson('/api/projects/cost-alerts/evaluate', {
        queueNotifications,
        assignmentWarningThresholdHours: Number(thresholdHours || 8)
      });

      setActionStatus(`${result.message} Open: ${result.openCount}. High: ${result.highCount}. Queued recipients: ${result.queuedRecipientCount}.`);
      await loadAlerts();
    } catch (error) {
      setActionStatus(error instanceof Error ? error.message : 'Unable to evaluate project cost alerts.');
    }
  }

  async function updateAlertStatus(alert, alertStatus) {
    if (!canManageCostAlerts) {
      setActionStatus('Cost alert status changes are restricted to administrators and project/team coordinators.');
      return;
    }

    try {
      setActionStatus(`Updating ${alert.projectCode}...`);
      const result = await postJson(`/api/projects/cost-alerts/${alert.alertId}/status`, {
        alertStatus,
        note: getAlertNote(alert.alertId)
      });

      setActionStatus(`${alert.projectCode} updated to ${result.alertStatus}.`);
      setAlertNote(alert.alertId, '');
      await loadAlerts();
    } catch (error) {
      setActionStatus(error instanceof Error ? error.message : 'Unable to update alert status.');
    }
  }

  async function releaseNotification(alert) {
    if (!canManageCostAlerts) {
      setActionStatus('Notification release is restricted to administrators and project/team coordinators.');
      return;
    }

    try {
      setActionStatus(`Checking notification routing for ${alert.projectCode}...`);
      const result = await postJson(`/api/projects/cost-alerts/${alert.alertId}/release-notification`, {
        routingNote: getAlertNote(alert.alertId)
      });

      setActionStatus(`${result.message} Recipient count: ${result.recipientCount ?? alert.notificationRecipientCount ?? 0}.`);
      setAlertNote(alert.alertId, '');
      await loadAlerts();
    } catch (error) {
      setActionStatus(error instanceof Error ? error.message : 'Unable to release notification.');
    }
  }

  return (
    <section className="cost-alert-center">
      <div className="cost-alert-header">
        <div>
          <p className="eyebrow">019M-AJ</p>
          <h2>Cost Alert Routing Controls</h2>
          <p className="muted">
            Evaluate project cost alerts, hold notifications by default, acknowledge alerts, resolve alerts, and release routing only when Admin/PTC is ready.
          </p>
        </div>
        <span className="cost-alert-mode">{canManageCostAlerts ? 'Routing controls enabled' : 'Read only'}</span>
      </div>

      {alertData.error && <div className="cost-alert-banner error">{alertData.error}</div>}
      {actionStatus && <div className="cost-alert-banner">{actionStatus}</div>}

      <div className="cost-alert-summary-grid">
        <article><span>Open alerts</span><strong>{alertData.loading ? '...' : summary.openCount}</strong><small>Alerts requiring action</small></article>
        <article><span>Acknowledged</span><strong>{alertData.loading ? '...' : summary.acknowledgedCount}</strong><small>Accepted for follow-up</small></article>
        <article><span>Held routing</span><strong>{alertData.loading ? '...' : summary.heldCount}</strong><small>Notifications not released</small></article>
        <article><span>Queued notices</span><strong>{alertData.loading ? '...' : summary.queuedCount}</strong><small>Outbox routing already prepared</small></article>
      </div>

      {canManageCostAlerts && (
        <article className="cost-alert-panel cost-alert-control-panel">
          <div>
            <h3>Evaluate Alerts</h3>
            <p className="muted">Evaluation records the latest project cost conditions. Notifications are held by default.</p>
          </div>
          <div className="cost-alert-controls">
            <label>
              Remaining-hours warning threshold
              <input type="number" min="0" step="0.25" value={thresholdHours} onChange={(event) => setThresholdHours(event.target.value)} />
            </label>
            <label className="checkbox-label">
              <input type="checkbox" checked={queueNotifications} onChange={(event) => setQueueNotifications(event.target.checked)} />
              Queue notifications during evaluation
            </label>
            <button type="button" className="primary-action" onClick={evaluateAlerts}>Evaluate cost alerts</button>
            <button type="button" className="secondary-action" onClick={loadAlerts}>Refresh</button>
          </div>
        </article>
      )}

      <div className="cost-alert-layout">
        <article className="cost-alert-panel">
          <h3>Current Alert Candidates</h3>
          <p className="muted">Live project cost conditions before or after evaluation.</p>
          <div className="cost-alert-card-list">
            {candidates.map((candidate) => (
              <div className={`cost-alert-card severity-${candidate.alertSeverity}`} key={`${candidate.projectId}-${candidate.alertType}`}>
                <div className="cost-alert-card-header">
                  <div>
                    <span>{labelAlertType(candidate.alertType)}</span>
                    <strong>{candidate.projectCode}</strong>
                    <small>{candidate.projectName}</small>
                  </div>
                  <em>{candidate.alertSeverity}</em>
                </div>
                <div className="cost-alert-metric-grid">
                  <span>Customer<strong>{candidate.clientName || 'No customer'}</strong></span>
                  <span>Plan<strong>{fmtMoney(candidate.plannedTotalProjectCost)}</strong></span>
                  <span>Assigned<strong>{fmtNumber(candidate.assignedHours)} hrs</strong></span>
                  <span>Used<strong>{fmtNumber(candidate.usedHours)} hrs</strong></span>
                  <span>Over<strong>{fmtNumber(candidate.overAssignedHours)} hrs</strong></span>
                  <span>Status<strong>{candidate.costStatus}</strong></span>
                </div>
              </div>
            ))}
            {!alertData.loading && candidates.length === 0 && <p className="muted">No current cost alert candidates were found.</p>}
          </div>
        </article>

        <article className="cost-alert-panel">
          <h3>Recorded Alerts</h3>
          <p className="muted">Stored alert history, acknowledgement state, and routing readiness.</p>
          <div className="cost-alert-card-list">
            {alerts.map((alert) => (
              <div className={`cost-alert-card severity-${alert.alertSeverity}`} key={alert.alertId}>
                <div className="cost-alert-card-header">
                  <div>
                    <span>{labelAlertType(alert.alertType)} · {alert.alertStatus}</span>
                    <strong>{alert.projectCode}</strong>
                    <small>{alert.alertSummary}</small>
                  </div>
                  <em>{alert.alertSeverity}</em>
                </div>

                <p>{alert.alertDetail}</p>

                <div className="cost-alert-routing-strip">
                  <span>Routing: <strong>{alert.routingStatus ?? 'hold'}</strong></span>
                  <span>Queued: <strong>{alert.notificationQueuedAt ? 'Yes' : 'No'}</strong></span>
                  <span>Recipients: <strong>{alert.notificationRecipientCount ?? 0}</strong></span>
                </div>

                <div className="cost-alert-metric-grid">
                  <span>Customer<strong>{alert.clientName || 'No customer'}</strong></span>
                  <span>Plan<strong>{fmtMoney(alert.plannedTotalProjectCost)}</strong></span>
                  <span>Assigned<strong>{fmtNumber(alert.assignedHours)} hrs</strong></span>
                  <span>Used<strong>{fmtNumber(alert.usedHours)} hrs</strong></span>
                  <span>Over<strong>{fmtNumber(alert.overAssignedHours)} hrs</strong></span>
                  <span>Status<strong>{alert.costStatus}</strong></span>
                </div>

                <div className="cost-alert-history">
                  <small>Last detected: {fmtDateTime(alert.lastDetectedAt)}</small>
                  <small>Acknowledged: {fmtDateTime(alert.acknowledgedAt)}{alert.acknowledgedByEmail ? ` by ${alert.acknowledgedByEmail}` : ''}</small>
                  <small>Notification released: {fmtDateTime(alert.notificationReleasedAt ?? alert.notificationQueuedAt)}</small>
                  <small>Last action: {fmtDateTime(alert.lastActionAt)}{alert.lastActionByEmail ? ` by ${alert.lastActionByEmail}` : ''}</small>
                </div>

                {alert.acknowledgementNote ? <p className="cost-alert-note">Acknowledgement note: {alert.acknowledgementNote}</p> : null}
                {alert.routingNote ? <p className="cost-alert-note">Routing note: {alert.routingNote}</p> : null}

                {canManageCostAlerts ? (
                  <div className="cost-alert-action-panel">
                    <textarea
                      value={getAlertNote(alert.alertId)}
                      onChange={(event) => setAlertNote(alert.alertId, event.target.value)}
                      placeholder="Optional acknowledgement, resolution, or routing note"
                    />
                    <div className="cost-alert-action-row">
                      <button type="button" className="secondary-action" onClick={() => updateAlertStatus(alert, 'acknowledged')}>Acknowledge</button>
                      <button type="button" className="secondary-action" onClick={() => updateAlertStatus(alert, 'resolved')}>Resolve</button>
                      <button type="button" className="secondary-action" onClick={() => updateAlertStatus(alert, 'open')}>Reopen</button>
                      <button
                        type="button"
                        className="primary-action"
                        onClick={() => releaseNotification(alert)}
                        disabled={Boolean(alert.notificationQueuedAt) || alert.alertStatus === 'resolved'}
                      >
                        {alert.notificationQueuedAt ? 'Already queued' : 'Release notification'}
                      </button>
                    </div>
                  </div>
                ) : null}
              </div>
            ))}
            {!alertData.loading && alerts.length === 0 && <p className="muted">No recorded alerts yet. Run evaluation to create the first alert set.</p>}
          </div>
        </article>
      </div>
    </section>
  );
}
