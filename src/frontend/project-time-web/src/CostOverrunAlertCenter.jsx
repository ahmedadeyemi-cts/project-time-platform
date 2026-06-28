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
  const response = await fetch(path, {
    headers: getProjectPulseAuthHeaders()
  });

  if (!response.ok) throw new Error(await readApiErrorMessage(response, path));
  return response.json();
}

async function postJson(path, payload) {
  const response = await fetch(path, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', ...getProjectPulseAuthHeaders() },
    body: JSON.stringify(payload)
  });

  if (!response.ok) throw new Error(await readApiErrorMessage(response, path));
  return response.json();
}

function fmtMoney(value) {
  const amount = Number(value ?? 0);
  return amount.toLocaleString(undefined, { style: 'currency', currency: 'USD', maximumFractionDigits: 0 });
}

function fmtNumber(value) {
  return Number(value ?? 0).toLocaleString(undefined, { maximumFractionDigits: 2 });
}

function labelAlertType(value) {
  return String(value ?? '')
    .replaceAll('_', ' ')
    .replace(/\b\w/g, (match) => match.toUpperCase());
}

export default function CostOverrunAlertCenter({ canManageCostAlerts = false }) {
  const [alertData, setAlertData] = useState({ loading: true, data: null, error: null });
  const [actionStatus, setActionStatus] = useState('');
  const [thresholdHours, setThresholdHours] = useState('8');
  const [queueNotifications, setQueueNotifications] = useState(true);

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
    const highAlerts = openAlerts.filter((alert) => alert.alertSeverity === 'high');
    const queuedAlerts = alerts.filter((alert) => alert.notificationQueuedAt);

    return {
      openCount: openAlerts.length,
      highCount: highAlerts.length,
      queuedCount: queuedAlerts.length,
      candidateCount: candidates.length
    };
  }, [alerts, candidates]);

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

  return (
    <section className="cost-alert-center">
      <div className="cost-alert-header">
        <div>
          <p className="eyebrow">019M-AI</p>
          <h2>Cost Overrun Alerts</h2>
          <p className="muted">
            Detect projects missing cost plans, projects with activity against missing plans, and projects using more hours than assigned.
          </p>
        </div>
        <span className="cost-alert-mode">{canManageCostAlerts ? 'Evaluation enabled' : 'Read only'}</span>
      </div>

      {alertData.error && <div className="cost-alert-banner error">{alertData.error}</div>}
      {actionStatus && <div className="cost-alert-banner">{actionStatus}</div>}

      <div className="cost-alert-summary-grid">
        <article><span>Open alerts</span><strong>{alertData.loading ? '...' : summary.openCount}</strong><small>Active project cost/readiness alerts</small></article>
        <article><span>High severity</span><strong>{alertData.loading ? '...' : summary.highCount}</strong><small>Missing plan with work or hours over plan</small></article>
        <article><span>Candidates</span><strong>{alertData.loading ? '...' : summary.candidateCount}</strong><small>Current projects requiring attention</small></article>
        <article><span>Queued notices</span><strong>{alertData.loading ? '...' : summary.queuedCount}</strong><small>Alerts routed to PM, manager, and PTC</small></article>
      </div>

      {canManageCostAlerts && (
        <article className="cost-alert-panel cost-alert-control-panel">
          <div>
            <h3>Evaluate Alerts</h3>
            <p className="muted">
              Evaluation records current alerts and queues notification outbox entries for project managers, resource managers, and Project Team Coordinators.
            </p>
          </div>
          <div className="cost-alert-controls">
            <label>
              Remaining-hours warning threshold
              <input
                type="number"
                min="0"
                step="0.25"
                value={thresholdHours}
                onChange={(event) => setThresholdHours(event.target.value)}
              />
            </label>
            <label className="checkbox-label">
              <input
                type="checkbox"
                checked={queueNotifications}
                onChange={(event) => setQueueNotifications(event.target.checked)}
              />
              Queue notifications
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
          <p className="muted">Stored alert history and routing readiness.</p>
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
                <div className="cost-alert-metric-grid">
                  <span>Customer<strong>{alert.clientName || 'No customer'}</strong></span>
                  <span>Plan<strong>{fmtMoney(alert.plannedTotalProjectCost)}</strong></span>
                  <span>Assigned<strong>{fmtNumber(alert.assignedHours)} hrs</strong></span>
                  <span>Used<strong>{fmtNumber(alert.usedHours)} hrs</strong></span>
                  <span>Over<strong>{fmtNumber(alert.overAssignedHours)} hrs</strong></span>
                  <span>Recipients<strong>{alert.notificationRecipientCount}</strong></span>
                </div>
                <small className="cost-alert-timestamp">
                  Last detected: {alert.lastDetectedAt ? new Date(alert.lastDetectedAt).toLocaleString() : 'Not recorded'}
                  {alert.notificationQueuedAt ? ` · Notification queued: ${new Date(alert.notificationQueuedAt).toLocaleString()}` : ' · Notification not queued'}
                </small>
              </div>
            ))}
            {!alertData.loading && alerts.length === 0 && <p className="muted">No recorded alerts yet. Run evaluation to create the first alert set.</p>}
          </div>
        </article>
      </div>
    </section>
  );
}
