import { useCallback, useEffect, useMemo, useState } from 'react';

const AUTH_STORAGE_KEY = 'projectPulseAuthSession';
const VIEW_AS_STORAGE_KEY = 'projectPulseViewAsUser';

const routeOperations = {
  dashboard: [
    {
      operationKey: 'production_readiness',
      operationTitle: 'Production Readiness Command Center',
      evidenceEndpoint: '/api/production/readiness-command-center'
    },
    {
      operationKey: 'navigation_registry_integrity',
      operationTitle: 'Navigation Registry Integrity',
      evidenceEndpoint: '/api/navigation/registry-integrity'
    },
    {
      operationKey: 'dashboard_module_visibility',
      operationTitle: 'Dashboard Module Visibility',
      evidenceEndpoint: '/api/dashboard/module-visibility-smoke'
    }
  ],
  workflow: [
    {
      operationKey: 'workflow_operations',
      operationTitle: 'Workflow Operations Center',
      evidenceEndpoint: '/api/workflow/operations-ui-data'
    },
    {
      operationKey: 'workflow_preflight',
      operationTitle: 'Workflow Preflight Validation',
      evidenceEndpoint: '/api/workflow/preflight-validation'
    },
    {
      operationKey: 'export_evidence',
      operationTitle: 'Export Evidence Summary',
      evidenceEndpoint: '/api/export-packages/evidence-summary'
    }
  ],
  'role-admin': [
    {
      operationKey: 'route_permission_contracts',
      operationTitle: 'Route Permission Contracts',
      evidenceEndpoint: '/api/security/route-permission-contracts'
    },
    {
      operationKey: 'role_access_matrix',
      operationTitle: 'Role Access Matrix',
      evidenceEndpoint: '/api/security/role-access-matrix'
    },
    {
      operationKey: 'security_registry_integrity',
      operationTitle: 'Security Registry Integrity',
      evidenceEndpoint: '/api/navigation/registry-integrity'
    }
  ]
};

function readJsonStorage(key) {
  try {
    const raw = window.localStorage.getItem(key);
    return raw ? JSON.parse(raw) : null;
  } catch {
    return null;
  }
}

function getSessionToken() {
  const session = readJsonStorage(AUTH_STORAGE_KEY);
  const token = session?.sessionToken || session?.token || session?.accessToken;
  return typeof token === 'string' ? token.trim() : '';
}

function getViewAsUserId() {
  const viewAs = readJsonStorage(VIEW_AS_STORAGE_KEY);

  if (typeof viewAs === 'string') {
    return viewAs.trim();
  }

  const userId = viewAs?.userId || viewAs?.id || viewAs?.value;
  return typeof userId === 'string' ? userId.trim() : '';
}

function currentRoute() {
  return (window.location.hash || '#dashboard').replace(/^#\/?/, '').split(/[/?]/)[0] || 'dashboard';
}

async function readResponse(response, path) {
  const text = await response.text();

  let payload = {};
  try {
    payload = text ? JSON.parse(text) : {};
  } catch {
    payload = { raw: text };
  }

  if (!response.ok) {
    const message = payload.message || payload.detail || payload.status || `${path} returned HTTP ${response.status}`;
    throw new Error(message);
  }

  return payload;
}

async function fetchJson(path, token, viewAsUserId, options = {}) {
  const headers = {
    Accept: 'application/json',
    'Content-Type': 'application/json',
    'X-ProjectPulse-Session': token,
    ...(options.headers || {})
  };

  if (viewAsUserId) {
    headers['X-ProjectPulse-View-As-User'] = viewAsUserId;
  }

  const response = await fetch(path, {
    ...options,
    headers,
    credentials: 'same-origin'
  });

  return readResponse(response, path);
}

export default function ProductionOperationsAcknowledgmentsPanel() {
  const [route, setRoute] = useState(() => currentRoute());
  const [token, setToken] = useState(() => getSessionToken());
  const [viewAsUserId, setViewAsUserId] = useState(() => getViewAsUserId());
  const [summary, setSummary] = useState(null);
  const [events, setEvents] = useState([]);
  const [message, setMessage] = useState('');
  const [busyKey, setBusyKey] = useState('');
  const [refreshKey, setRefreshKey] = useState(0);

  const operations = useMemo(() => routeOperations[route] || [], [route]);
  const visible = Boolean(token && operations.length > 0 && !viewAsUserId);

  const syncRuntimeState = useCallback(() => {
    setRoute(currentRoute());
    setToken(getSessionToken());
    setViewAsUserId(getViewAsUserId());
  }, []);

  const refreshAcknowledgments = useCallback(async () => {
    if (!visible) {
      setSummary(null);
      setEvents([]);
      return;
    }

    const data = await fetchJson(
      `/api/production/operations-acknowledgments/summary?routeKey=${encodeURIComponent(route)}`,
      token,
      ''
    );

    setSummary(data.summary || null);
    setEvents(Array.isArray(data.acknowledgments) ? data.acknowledgments : []);
  }, [route, token, visible]);

  useEffect(() => {
    const onHashChange = () => syncRuntimeState();
    const onFocus = () => syncRuntimeState();
    const onStorage = (event) => {
      if (event.key === AUTH_STORAGE_KEY || event.key === VIEW_AS_STORAGE_KEY) {
        syncRuntimeState();
      }
    };

    window.addEventListener('hashchange', onHashChange);
    window.addEventListener('focus', onFocus);
    window.addEventListener('storage', onStorage);

    const interval = window.setInterval(syncRuntimeState, 2000);

    return () => {
      window.removeEventListener('hashchange', onHashChange);
      window.removeEventListener('focus', onFocus);
      window.removeEventListener('storage', onStorage);
      window.clearInterval(interval);
    };
  }, [syncRuntimeState]);

  useEffect(() => {
    let cancelled = false;

    if (!visible) {
      setSummary(null);
      setEvents([]);
      setMessage('');
      return () => {
        cancelled = true;
      };
    }

    refreshAcknowledgments()
      .then(() => {
        if (!cancelled) setMessage('');
      })
      .catch((error) => {
        if (!cancelled) {
          setMessage(error?.message || 'Unable to load production operation acknowledgments.');
          setSummary(null);
          setEvents([]);
        }
      });

    return () => {
      cancelled = true;
    };
  }, [visible, refreshKey, refreshAcknowledgments]);

  async function acknowledgeOperation(operation) {
    if (!token || viewAsUserId || busyKey) return;

    const note = window.prompt(
      `Add an acknowledgment note for ${operation.operationTitle}:`,
      'Reviewed and acknowledged for production operations.'
    );

    if (note === null) return;

    setBusyKey(operation.operationKey);
    setMessage('');

    try {
      const evidence = await fetchJson(operation.evidenceEndpoint, token, '');

      const result = await fetchJson('/api/production/operations-acknowledgments', token, '', {
        method: 'POST',
        body: JSON.stringify({
          routeKey: route,
          operationKey: operation.operationKey,
          operationTitle: operation.operationTitle,
          acknowledgmentNote: note,
          evidenceSnapshot: {
            capturedAt: new Date().toISOString(),
            endpoint: operation.evidenceEndpoint,
            evidence
          }
        })
      });

      setMessage(result.message || 'Production operation acknowledgment was recorded.');
      setRefreshKey((value) => value + 1);
    } catch (error) {
      setMessage(error?.message || 'Unable to record production operation acknowledgment.');
    } finally {
      setBusyKey('');
    }
  }

  if (!visible) {
    return null;
  }

  return (
    <section className="production-ops-ack-panel">
      <div className="production-ops-ack-heading">
        <div>
          <span className="production-ops-ack-eyebrow">Production Sign-Off Evidence</span>
          <h2>Operations acknowledgments</h2>
          <p>
            Record sign-off evidence for the active production operations route. View-As mode hides sign-off controls.
          </p>
          {message ? <p className="production-ops-ack-message">{message}</p> : null}
        </div>

        <div className="production-ops-ack-summary">
          <span>Total route acknowledgments</span>
          <strong>{events.length}</strong>
          <small>All acknowledgments: {summary?.totalAcknowledgments ?? 0}</small>
        </div>
      </div>

      <div className="production-ops-ack-grid">
        {operations.map((operation) => {
          const lastEvent = events.find((event) => event.operationKey === operation.operationKey);

          return (
            <article className="production-ops-ack-card" key={operation.operationKey}>
              <div>
                <span>{operation.operationTitle}</span>
                <h3>{lastEvent ? 'Acknowledged' : 'Awaiting acknowledgment'}</h3>
                <p>
                  {lastEvent
                    ? `Last acknowledged by ${lastEvent.acknowledgedByEmail || 'unknown'}`
                    : 'No sign-off evidence has been recorded for this operation yet.'}
                </p>
              </div>

              <button
                type="button"
                className="production-ops-ack-button"
                disabled={Boolean(busyKey)}
                onClick={() => acknowledgeOperation(operation)}
              >
                {busyKey === operation.operationKey ? 'Recording…' : 'Acknowledge'}
              </button>
            </article>
          );
        })}
      </div>
    </section>
  );
}
