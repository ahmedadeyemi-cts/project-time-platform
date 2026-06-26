import { useEffect, useMemo, useState } from "react";
import "./replication-sync-status-center.css";

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


const statusLabels = {
  ready: "Ready",
  warning: "Warning",
  action_required: "Action Required",
  not_configured: "Not Configured",
  unknown: "Unknown"
};

function normalizeStatus(value) {
  return (value || "unknown").toString().toLowerCase();
}

function statusLabel(value) {
  return statusLabels[normalizeStatus(value)] || value || "Unknown";
}

function formatDate(value) {
  if (!value) return "Not available";
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return date.toLocaleString();
}

function formatBytes(value) {
  if (value === null || value === undefined) return "Not available";
  const number = Number(value);
  if (!Number.isFinite(number)) return "Not available";

  const units = ["B", "KB", "MB", "GB", "TB"];
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

function InfoCard({ title, value, detail, status }) {
  return (
    <section className="replication-sync-card">
      <div className="replication-sync-card__topline">
        <h3>{title}</h3>
        {status ? <StatusBadge status={status} /> : null}
      </div>
      <div className="replication-sync-card__value">{value || "Not available"}</div>
      {detail ? <p>{detail}</p> : null}
    </section>
  );
}

export default function ReplicationSyncStatusCenter({ authSession }) {
  const [status, setStatus] = useState(null);
  const [loadState, setLoadState] = useState("loading");
  const [error, setError] = useState("");
  const [refreshing, setRefreshing] = useState(false);

  async function loadStatus() {
    setRefreshing(true);
    setError("");

    try {
      const response = await fetch("/api/system/replication-sync/status", {
        credentials: "include",
        headers: buildAuthHeaders(authSession, {
          Accept: "application/json"
        })
      });

      if (!response.ok) {
        throw new Error(`Status request failed with HTTP ${response.status}`);
      }

      const data = await response.json();
      setStatus(data);
      setLoadState("loaded");
    } catch (err) {
      setError(err?.message || "Unable to load replication and sync status.");
      setLoadState("error");
    } finally {
      setRefreshing(false);
    }
  }

  useEffect(() => {
    loadStatus();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [authSession?.sessionToken, authSession?.token, authSession?.accessToken]);

  const checks = useMemo(() => status?.checks || [], [status]);
  const services = useMemo(() => status?.services || [], [status]);

  return (
    <section id="replication-sync-center" className="panel timesheet-page replication-sync-page">
      <section className="replication-sync-hero">
        <div>
          <p className="replication-sync-eyebrow">System Operations</p>
          <h1>Replication & Sync Status</h1>
          <p>
            Review ProjectPulse failover readiness, database role, service health,
            backup freshness, deployment state, and peer configuration.
          </p>
        </div>

        <div className="replication-sync-hero__actions">
          {status?.overallStatus ? <StatusBadge status={status.overallStatus} /> : null}
          <button type="button" onClick={loadStatus} disabled={refreshing}>
            {refreshing ? "Refreshing..." : "Refresh"}
          </button>
        </div>
      </section>

      {loadState === "loading" ? (
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
          <section className="replication-sync-summary-grid">
            <InfoCard
              title="Overall Status"
              value={statusLabel(status.overallStatus)}
              detail={`Ready: ${status.summary?.ready ?? 0} · Warning: ${status.summary?.warning ?? 0} · Planned: ${status.summary?.notConfigured ?? 0} · Action Required: ${status.summary?.actionRequired ?? 0}`}
              status={status.overallStatus}
            />

            <InfoCard
              title="Database Role"
              value={status.database?.role}
              detail={status.database?.detail}
              status={status.database?.status}
            />

            <InfoCard
              title="Latest Backup"
              value={status.backup?.latestBundle?.name}
              detail={
                status.backup?.latestBundle?.ageHours !== null &&
                status.backup?.latestBundle?.ageHours !== undefined
                  ? `Age: ${status.backup.latestBundle.ageHours} hours · Size: ${formatBytes(status.backup.latestBundle.sizeBytes)}`
                  : status.backup?.latestBundle?.detail
              }
              status={status.backup?.latestBundle?.status}
            />

            <InfoCard
              title="Git Deployment"
              value={`${status.git?.branch || "unknown"} @ ${status.git?.commit || "unknown"}`}
              detail={
                status.git?.dirtyFiles === 0
                  ? "Working tree is clean."
                  : `${status.git?.dirtyFiles ?? "Unknown"} uncommitted file(s).`
              }
              status={status.git?.status}
            />
          </section>

          <section className="replication-sync-panel">
            <div className="replication-sync-section-header">
              <div>
                <h2>Failover Readiness Checks</h2>
                <p>Generated {formatDate(status.generatedAt)} on {status.host?.hostname || "this server"}.</p>
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
              <h2>Database Replication</h2>
              <dl className="replication-sync-details">
                <div>
                  <dt>Role</dt>
                  <dd>{status.database?.role || "unknown"}</dd>
                </div>
                <div>
                  <dt>In recovery</dt>
                  <dd>{String(status.database?.isInRecovery ?? "unknown")}</dd>
                </div>
                <div>
                  <dt>Replication connections</dt>
                  <dd>{status.database?.replicationConnections ?? "Not available"}</dd>
                </div>
                <div>
                  <dt>WAL LSN</dt>
                  <dd>{status.database?.walLsn || "Not available"}</dd>
                </div>
                <div>
                  <dt>Replay lag</dt>
                  <dd>{status.database?.replayLagSeconds ?? "Not available"} seconds</dd>
                </div>
              </dl>
            </section>

            <section className="replication-sync-panel">
              <h2>Peer Server</h2>
              <dl className="replication-sync-details">
                <div>
                  <dt>Name</dt>
                  <dd>{status.peer?.name || "Not configured"}</dd>
                </div>
                <div>
                  <dt>Host</dt>
                  <dd>{status.peer?.host || "Not configured"}</dd>
                </div>
                <div>
                  <dt>URL</dt>
                  <dd>{status.peer?.url || "Not configured"}</dd>
                </div>
                <div>
                  <dt>Status</dt>
                  <dd><StatusBadge status={status.peer?.status} /></dd>
                </div>
              </dl>
            </section>
          </section>

          <section className="replication-sync-panel">
            <h2>Services</h2>
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
