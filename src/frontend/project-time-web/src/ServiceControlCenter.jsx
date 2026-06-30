import { useEffect, useMemo, useRef, useState } from 'react';
import './service-control-center.css';

const refreshMs = 30000;

function formatStatus(value) {
  if (!value) return 'Unknown';
  const normalized = String(value);
  const lower = normalized.toLowerCase();

  if (lower === 'unavailable') return 'Check failed';
  if (lower === 'check_failed') return 'Check failed';
  if (lower === 'check_timed_out') return 'Timed out';
  if (lower === 'not_detected') return 'Not detected';

  return normalized.replace(/_/g, ' ');
}

function statusClass(value) {
  const normalized = String(value ?? '').toLowerCase();

  if (['active', 'running', 'healthy', 'ok', 'service_status_loaded', 'detected', 'version_inventory_loaded'].includes(normalized)) {
    return 'healthy';
  }

  if (['degraded', 'pending', 'warning', 'unavailable', 'check_failed', 'check_timed_out', 'not_detected'].includes(normalized)) {
    return 'warning';
  }

  return 'critical';
}

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

function ServiceControlCenter({ authSession }) {
  const [serviceState, setServiceState] = useState({ loading: true, data: null, error: null });
  const [apiState, setApiState] = useState({ loading: true, data: null, error: null });
  const [versionState, setVersionState] = useState({ loading: true, data: null, error: null });
  const [restartHistoryState, setRestartHistoryState] = useState({ loading: true, data: null, error: null });
  const [restartState, setRestartState] = useState({ serviceKey: null, reason: '', loading: false, error: null, message: null });

  const versionInventoryRef = useRef(null);
  const managedServicesRef = useRef(null);
  const apiStatusRef = useRef(null);
  const restartHistoryRef = useRef(null);

  async function fetchJson(path, options = {}) {
    const response = await fetch(path, {
      credentials: 'include',
      ...options,
      headers: buildAuthHeaders(authSession, options.headers ?? {})
    });

    const payload = await response.json().catch(() => null);

    if (!response.ok) {
      throw new Error(payload?.message ?? payload?.error ?? `Request failed with ${response.status}`);
    }

    return payload;
  }

  function scrollToSection(sectionRef) {
    sectionRef.current?.scrollIntoView({ behavior: 'smooth', block: 'start' });
  }

  async function loadServiceStatus() {
    try {
      const payload = await fetchJson('/api/system/service-control/status');
      setServiceState({ loading: false, data: payload, error: null });
    } catch (error) {
      setServiceState({ loading: false, data: null, error: error.message });
    }
  }

  async function loadApiStatus() {
    try {
      const payload = await fetchJson('/api/system/api-status');
      setApiState({ loading: false, data: payload, error: null });
    } catch (error) {
      setApiState({ loading: false, data: null, error: error.message });
    }
  }

  async function loadVersionInventory() {
    try {
      const payload = await fetchJson('/api/system/version-inventory');
      setVersionState({ loading: false, data: payload, error: null });
    } catch (error) {
      setVersionState({ loading: false, data: null, error: error.message });
    }
  }

  async function loadRestartHistory() {
    try {
      const payload = await fetchJson('/api/audit/history?days=30&category=system_audit&search=service_restart');
      setRestartHistoryState({ loading: false, data: payload, error: null });
    } catch (error) {
      setRestartHistoryState({ loading: false, data: null, error: error.message });
    }
  }

  async function refreshAll() {
    await Promise.all([loadServiceStatus(), loadApiStatus(), loadVersionInventory(), loadRestartHistory()]);
  }

  useEffect(() => {
    refreshAll();

    const timer = window.setInterval(() => {
      Promise.all([loadServiceStatus(), loadApiStatus(), loadRestartHistory()]);
    }, refreshMs);
    return () => window.clearInterval(timer);
  }, [authSession?.sessionToken, authSession?.token, authSession?.accessToken]);

  const serviceSummary = useMemo(() => {
    const services = serviceState.data?.services ?? [];
    const active = services.filter((service) => String(service.activeState).toLowerCase() === 'active').length;

    return {
      total: services.length,
      active,
      attention: Math.max(services.length - active, 0)
    };
  }, [serviceState.data]);

  const apiSummary = useMemo(() => {
    const components = apiState.data?.components ?? [];
    const healthy = components.filter((component) => String(component.status).toLowerCase() === 'healthy').length;

    return {
      total: components.length,
      healthy,
      attention: Math.max(components.length - healthy, 0)
    };
  }, [apiState.data]);

  const versionSummary = useMemo(() => {
    const items = versionState.data?.items ?? [];
    const detected = items.filter((item) => String(item.status).toLowerCase() === 'detected').length;

    return {
      total: items.length,
      detected,
      attention: Math.max(items.length - detected, 0)
    };
  }, [versionState.data]);

  const restartHistory = useMemo(() => {
    const events = restartHistoryState.data?.events ?? restartHistoryState.data?.items ?? restartHistoryState.data?.history ?? [];
    return Array.isArray(events) ? events.slice(0, 12) : [];
  }, [restartHistoryState.data]);

  const versionGroups = useMemo(() => {
    const groups = new Map();

    (versionState.data?.items ?? []).forEach((item) => {
      const category = item.category ?? 'Other';
      if (!groups.has(category)) {
        groups.set(category, []);
      }

      groups.get(category).push(item);
    });

    return [...groups.entries()].map(([category, items]) => ({ category, items }));
  }, [versionState.data]);

  function beginRestart(service) {
    setRestartState({
      serviceKey: service.serviceKey,
      serviceName: service.displayName,
      reason: '',
      loading: false,
      error: null,
      message: null
    });
  }

  function cancelRestart() {
    setRestartState({ serviceKey: null, reason: '', loading: false, error: null, message: null });
  }

  async function submitRestart(event) {
    event.preventDefault();

    if (!restartState.serviceKey) return;

    if (!restartState.reason || restartState.reason.trim().length < 8) {
      setRestartState((current) => ({
        ...current,
        error: 'Please enter a reason with at least 8 characters before restarting the service.'
      }));
      return;
    }

    setRestartState((current) => ({ ...current, loading: true, error: null, message: null }));

    try {
      const payload = await fetchJson('/api/system/service-control/restart', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json'
        },
        body: JSON.stringify({
          serviceKey: restartState.serviceKey,
          reason: restartState.reason.trim()
        })
      });

      setRestartState((current) => ({
        ...current,
        loading: false,
        error: null,
        message: payload?.message ?? 'Restart completed.'
      }));

      await refreshAll();
    } catch (error) {
      setRestartState((current) => ({
        ...current,
        loading: false,
        error: error.message,
        message: null
      }));
    }
  }

  return (
    <section id="service-control-center" className="panel service-control-center">
      <div className="service-control-hero">
        <div>
          <p className="eyebrow">System Operations</p>
          <h2>Service Control Center</h2>
          <p>
            Monitor ProjectPulse services, API components, database health, integrations, runtime versions, and administrative restart actions.
          </p>
        </div>

        <button type="button" className="secondary-action" onClick={refreshAll}>
          Refresh status
        </button>
      </div>

      <div className="service-control-summary-grid">
        <button type="button" className="service-control-summary-card clickable" onClick={() => scrollToSection(managedServicesRef)}>
          <span>Managed Services</span>
          <strong>{serviceState.loading ? 'Checking...' : `${serviceSummary.active}/${serviceSummary.total} active`}</strong>
          <small>{serviceSummary.attention === 0 ? 'All monitored services are active.' : `${serviceSummary.attention} service(s) need attention.`}</small>
        </button>

        <button type="button" className="service-control-summary-card clickable" onClick={() => scrollToSection(apiStatusRef)}>
          <span>API Components</span>
          <strong>{apiState.loading ? 'Checking...' : `${apiSummary.healthy}/${apiSummary.total} healthy`}</strong>
          <small>{apiSummary.attention === 0 ? 'All monitored API components are healthy.' : `${apiSummary.attention} component(s) need attention.`}</small>
        </button>

        <button type="button" className="service-control-summary-card clickable" onClick={() => scrollToSection(versionInventoryRef)}>
          <span>Version Inventory</span>
          <strong>{versionState.loading ? 'Checking...' : `${versionSummary.detected}/${versionSummary.total} detected`}</strong>
          <small>{versionState.loading ? 'Collecting runtime versions...' : versionSummary.total === 0 ? 'Version inventory has not returned any items yet.' : versionSummary.attention === 0 ? 'All tracked versions were detected.' : `${versionSummary.attention} version check(s) need review, but this does not mean the service is down.`}</small>
        </button>
      </div>

      <nav className="service-control-section-nav" aria-label="Service Control sections">
        <button type="button" onClick={() => scrollToSection(versionInventoryRef)}>Version Inventory</button>
        <button type="button" onClick={() => scrollToSection(managedServicesRef)}>Managed Services</button>
        <button type="button" onClick={() => scrollToSection(apiStatusRef)}>API Status</button>
        <button type="button" onClick={() => scrollToSection(restartHistoryRef)}>Restart History</button>
      </nav>

      {serviceState.error ? <div className="service-control-alert critical">{serviceState.error}</div> : null}
      {apiState.error ? <div className="service-control-alert critical">{apiState.error}</div> : null}
      {versionState.error ? <div className="service-control-alert critical">{versionState.error}</div> : null}

      <section id="version-inventory" ref={versionInventoryRef} className="service-control-card">
        <div className="service-control-card-header">
          <div>
            <p className="eyebrow">Inventory</p>
            <h3>Automated Version Inventory</h3>
          </div>
          <small>{versionState.data?.generatedAt ? `Checked ${new Date(versionState.data.generatedAt).toLocaleString()}` : 'Waiting for version check...'}</small>
        </div>

        <div className="version-inventory-groups">
          {versionGroups.map((group) => (
            <div className="version-inventory-group" key={group.category}>
              <h4>{group.category}</h4>

              <div className="version-inventory-table">
                {group.items.map((item) => (
                  <article className="version-inventory-row" key={item.key}>
                    <div>
                      <strong>{item.name}</strong>
                      <small>{item.key}</small>
                    </div>

                    <code>{item.version}</code>

                    <span className={`service-status-pill ${statusClass(item.status)}`}>
                      {formatStatus(item.status)}
                    </span>

                    <details>
                      <summary>Details</summary>
                      <pre>{JSON.stringify(item.details ?? {}, null, 2)}</pre>
                    </details>
                  </article>
                ))}
              </div>
            </div>
          ))}

          {versionState.loading ? <p className="service-control-muted">Loading version inventory...</p> : null}
        </div>
      </section>

      <div className="service-control-grid">
        <section id="managed-services" ref={managedServicesRef} className="service-control-card">
          <div className="service-control-card-header">
            <div>
              <p className="eyebrow">Runtime</p>
              <h3>Managed Services</h3>
            </div>
          </div>

          <div className="service-list">
            {(serviceState.data?.services ?? []).map((service) => (
              <article className="service-row" key={service.serviceKey}>
                <div className="service-row-main">
                  <span className={`status-dot ${statusClass(service.activeState)}`} />
                  <div>
                    <h4>{service.displayName}</h4>
                    <p>{service.description}</p>
                    <small>{service.systemdName}</small>
                  </div>
                </div>

                <div className="service-row-meta">
                  <span className={`service-status-pill ${statusClass(service.activeState)}`}>
                    {formatStatus(service.activeState)}
                  </span>
                  <small>{service.subState || 'No sub-state'}</small>
                  <button type="button" className="danger-action" onClick={() => beginRestart(service)}>
                    Restart
                  </button>
                </div>

                {service.recentLogs?.length ? (
                  <details className="service-logs">
                    <summary>Recent logs</summary>
                    <pre>{service.recentLogs.join('\n')}</pre>
                  </details>
                ) : null}
              </article>
            ))}

            {serviceState.loading ? <p className="service-control-muted">Loading service status...</p> : null}
          </div>
        </section>

        <section id="api-status-dashboard" ref={apiStatusRef} className="service-control-card">
          <div className="service-control-card-header">
            <div>
              <p className="eyebrow">Application</p>
              <h3>API Status Dashboard</h3>
            </div>
          </div>

          <div className="api-component-grid">
            {(apiState.data?.components ?? []).map((component) => (
              <article className="api-component-card" key={component.key}>
                <div className="api-component-header">
                  <span className={`status-dot ${statusClass(component.status)}`} />
                  <div>
                    <h4>{component.name}</h4>
                    <small>{component.category}</small>
                  </div>
                </div>

                <span className={`service-status-pill ${statusClass(component.status)}`}>
                  {formatStatus(component.status)}
                </span>

                <pre>{JSON.stringify(component.details ?? {}, null, 2)}</pre>
              </article>
            ))}

            {apiState.loading ? <p className="service-control-muted">Loading API component status...</p> : null}
          </div>
        </section>
      </div>

      <section id="restart-history" ref={restartHistoryRef} className="service-control-card">
        <div className="service-control-card-header">
          <div>
            <p className="eyebrow">Audit</p>
            <h3>Restart History</h3>
          </div>
          <small>Last 30 days</small>
        </div>

        {restartHistoryState.error ? (
          <div className="service-control-alert critical">{restartHistoryState.error}</div>
        ) : null}

        <div className="restart-history-list">
          {restartHistory.map((event, index) => (
            <article className="restart-history-row" key={event.eventId ?? event.id ?? `${event.createdAt}-${index}`}>
              <div>
                <strong>{event.title ?? event.action ?? event.eventType ?? 'Service restart event'}</strong>
                <small>{event.createdAt ? new Date(event.createdAt).toLocaleString() : event.eventTime ? new Date(event.eventTime).toLocaleString() : 'Unknown time'}</small>
              </div>

              <p>{event.description ?? event.summary ?? event.message ?? 'Restart event recorded in audit history.'}</p>

              <span className={`service-status-pill ${statusClass(event.status ?? event.result ?? 'detected')}`}>
                {formatStatus(event.status ?? event.result ?? 'recorded')}
              </span>
            </article>
          ))}

          {restartHistoryState.loading ? (
            <p className="service-control-muted">Loading restart history...</p>
          ) : null}

          {!restartHistoryState.loading && restartHistory.length === 0 ? (
            <p className="service-control-muted">No service restart events were found in the last 30 days.</p>
          ) : null}
        </div>
      </section>

      {restartState.serviceKey ? (
        <div className="service-control-modal-backdrop">
          <form className="service-control-modal" onSubmit={submitRestart}>
            <p className="eyebrow">Restart Confirmation</p>
            <h3>Restart {restartState.serviceName}</h3>
            <p>
              Restarting a platform service can briefly affect availability. Enter the operational reason so the action is recorded in Audit History.
            </p>

            <label>
              Restart reason
              <textarea
                value={restartState.reason}
                onChange={(event) => setRestartState((current) => ({ ...current, reason: event.target.value }))}
                placeholder="Example: Restarting after configuration update validation."
                rows={4}
              />
            </label>

            {restartState.error ? <div className="service-control-alert critical">{restartState.error}</div> : null}
            {restartState.message ? <div className="service-control-alert healthy">{restartState.message}</div> : null}

            <div className="service-control-modal-actions">
              <button type="button" className="secondary-action" onClick={cancelRestart}>
                Close
              </button>
              <button type="submit" className="danger-action" disabled={restartState.loading}>
                {restartState.loading ? 'Restarting...' : 'Restart service'}
              </button>
            </div>
          </form>
        </div>
      ) : null}
    </section>
  );
}

export default ServiceControlCenter;
