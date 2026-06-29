import { useCallback, useEffect, useMemo, useState } from 'react';

const AUTH_STORAGE_KEY = 'projectPulseAuthSession';
const VIEW_AS_STORAGE_KEY = 'projectPulseViewAsUser';

const routeConfigs = {
  dashboard: {
    title: 'Production Readiness Command Center',
    eyebrow: 'Production Readiness',
    description:
      'Live operational readiness across users, projects, workflow, exports, audit evidence, route contracts, and module registry integrity.',
    cards: [
      {
        key: 'readiness',
        label: 'Readiness',
        path: '/api/production/readiness-command-center',
        mapper: (data) => ({
          title: data?.summary?.productionReady ? 'production_ready' : 'review_required',
          detail: `Ready checks: ${data?.summary?.readyCheckCount ?? 0}/${data?.summary?.checkCount ?? 0}`,
          status: data?.summary?.productionReady ? 'Ready' : 'Review'
        })
      },
      {
        key: 'registry',
        label: 'Registry Integrity',
        path: '/api/navigation/registry-integrity',
        mapper: (data) => ({
          title: data?.summary?.registryStatus ?? 'unknown',
          detail: `Dashboard expectations: ${data?.summary?.dashboardModuleExpectationCount ?? 0}; route contracts: ${data?.summary?.routePermissionContractCount ?? 0}`,
          status: data?.summary?.registryStatus ?? 'Unknown'
        })
      },
      {
        key: 'visibility',
        label: 'Module Visibility',
        path: '/api/dashboard/module-visibility-smoke',
        mapper: (data) => ({
          title: data?.summary?.active ? 'visibility_ready' : 'visibility_review',
          detail: `Expectations: ${data?.summary?.expectationCount ?? 0}; roles: ${data?.summary?.roleCount ?? 0}`,
          status: data?.summary?.active ? 'Ready' : 'Review'
        })
      }
    ]
  },

  workflow: {
    title: 'Production Workflow Operations Center',
    eyebrow: 'Workflow Operations',
    description:
      'Production workflow preflight, export evidence, reconciliation, audit history, and validation guardrails.',
    cards: [
      {
        key: 'operations',
        label: 'Operations',
        path: '/api/workflow/operations-ui-data',
        mapper: (data) => ({
          title: data?.summary?.productionOperationsStatus ?? 'unknown',
          detail: `Audit events: ${data?.summary?.auditEvents ?? 0}; export packages: ${data?.summary?.exportPackages ?? 0}; route contracts: ${data?.summary?.routeContracts ?? 0}`,
          status: data?.summary?.productionOperationsStatus ?? 'Unknown'
        })
      },
      {
        key: 'preflight',
        label: 'Preflight Validation',
        path: '/api/workflow/preflight-validation',
        mapper: (data) => ({
          title: data?.summary?.productionReadyForExport ? 'export_ready' : 'blocked_items',
          detail: `Ready entries: ${data?.summary?.exportReadyEntries ?? 0}; blocked entries: ${data?.summary?.blockedEntries ?? 0}; issues: ${data?.summary?.issueCount ?? 0}`,
          status: data?.summary?.productionReadyForExport ? 'Ready' : 'Review'
        })
      },
      {
        key: 'exportEvidence',
        label: 'Export Evidence',
        path: '/api/export-packages/evidence-summary',
        mapper: (data) => ({
          title: 'evidence_summary',
          detail: `Packages: ${data?.summary?.packageCount ?? 0}; evidence ready: ${data?.summary?.evidenceReadyCount ?? 0}; downloaded: ${data?.summary?.downloadedPackageCount ?? 0}`,
          status: 'Ready'
        })
      }
    ]
  },

  'role-admin': {
    title: 'Route Permission Contract Center',
    eyebrow: 'Route Governance',
    description:
      'Route-level permission contracts, restricted roles, role matrix, and navigation registry guardrails.',
    cards: [
      {
        key: 'contracts',
        label: 'Route Contracts',
        path: '/api/security/route-permission-contracts',
        mapper: (data) => ({
          title: 'contracts_active',
          detail: `Contracts: ${data?.summary?.contractCount ?? 0}; active: ${data?.summary?.activeContractCount ?? 0}; engineer restricted: ${data?.summary?.engineerRestrictedContractCount ?? 0}`,
          status: 'Ready'
        })
      },
      {
        key: 'registry',
        label: 'Registry Integrity',
        path: '/api/navigation/registry-integrity',
        mapper: (data) => ({
          title: data?.summary?.registryStatus ?? 'unknown',
          detail: `Workflow modules: ${data?.summary?.workflowModuleCount ?? 0}; security modules: ${data?.summary?.securityModuleCount ?? 0}`,
          status: data?.summary?.registryStatus ?? 'Unknown'
        })
      },
      {
        key: 'roles',
        label: 'Role Matrix',
        path: '/api/security/role-access-matrix',
        mapper: (data) => ({
          title: 'role_matrix_ready',
          detail: `Roles: ${data?.summary?.roleCount ?? 0}`,
          status: 'Ready'
        })
      }
    ]
  }
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

function getCurrentRoute() {
  return (window.location.hash || '#dashboard').replace(/^#\/?/, '').split(/[/?]/)[0] || 'dashboard';
}

async function fetchProductionJson(path, token, viewAsUserId) {
  const headers = {
    Accept: 'application/json',
    'X-ProjectPulse-Session': token
  };

  if (viewAsUserId) {
    headers['X-ProjectPulse-View-As-User'] = viewAsUserId;
  }

  try {
    const response = await fetch(path, {
      method: 'GET',
      headers,
      credentials: 'same-origin'
    });

    const text = await response.text();

    let payload = {};
    try {
      payload = text ? JSON.parse(text) : {};
    } catch {
      payload = { raw: text };
    }

    return {
      ...payload,
      httpStatus: response.status,
      ok: response.ok
    };
  } catch (error) {
    return {
      httpStatus: 0,
      ok: false,
      status: 'request_failed',
      message: error?.message || 'Request failed.'
    };
  }
}

function ProductionCard({ card, data }) {
  if (!data) {
    return (
      <article className="production-workflow-card">
        <div className="production-workflow-card-header">
          <span>{card.label}</span>
          <strong>Loading</strong>
        </div>
        <h4>loading</h4>
        <p>Collecting production evidence.</p>
      </article>
    );
  }

  if (!data.ok && data.httpStatus) {
    return (
      <article className="production-workflow-card production-workflow-card-warning">
        <div className="production-workflow-card-header">
          <span>{card.label}</span>
          <strong>HTTP {data.httpStatus}</strong>
        </div>
        <h4>{data.status || 'request_failed'}</h4>
        <p>{data.message || 'The request did not complete successfully.'}</p>
      </article>
    );
  }

  const mapped = card.mapper(data || {});

  return (
    <article className="production-workflow-card">
      <div className="production-workflow-card-header">
        <span>{card.label}</span>
        <strong>{mapped.status || 'Ready'}</strong>
      </div>
      <h4>{mapped.title || 'ready'}</h4>
      <p>{mapped.detail || 'Production evidence is available.'}</p>
    </article>
  );
}

export default function ProductionOperationsPanel() {
  const [route, setRoute] = useState(() => getCurrentRoute());
  const [token, setToken] = useState(() => getSessionToken());
  const [viewAsUserId, setViewAsUserId] = useState(() => getViewAsUserId());
  const [results, setResults] = useState({});
  const [refreshKey, setRefreshKey] = useState(0);

  const config = routeConfigs[route];

  const syncRuntimeState = useCallback(() => {
    setRoute(getCurrentRoute());
    setToken(getSessionToken());
    setViewAsUserId(getViewAsUserId());
  }, []);

  useEffect(() => {
    const handleHashChange = () => syncRuntimeState();
    const handleFocus = () => syncRuntimeState();
    const handleStorage = (event) => {
      if (event.key === AUTH_STORAGE_KEY || event.key === VIEW_AS_STORAGE_KEY) {
        syncRuntimeState();
      }
    };

    window.addEventListener('hashchange', handleHashChange);
    window.addEventListener('focus', handleFocus);
    window.addEventListener('storage', handleStorage);

    const interval = window.setInterval(syncRuntimeState, 2000);

    return () => {
      window.removeEventListener('hashchange', handleHashChange);
      window.removeEventListener('focus', handleFocus);
      window.removeEventListener('storage', handleStorage);
      window.clearInterval(interval);
    };
  }, [syncRuntimeState]);

  useEffect(() => {
    let cancelled = false;

    if (!config || !token) {
      setResults({});
      return () => {
        cancelled = true;
      };
    }

    setResults({});

    Promise.all(
      config.cards.map(async (card) => {
        const data = await fetchProductionJson(card.path, token, viewAsUserId);
        return [card.key, data];
      })
    ).then((entries) => {
      if (cancelled) return;
      setResults(Object.fromEntries(entries));
    });

    return () => {
      cancelled = true;
    };
  }, [config, route, token, viewAsUserId, refreshKey]);

  const isVisible = useMemo(() => Boolean(config && token), [config, token]);

  if (!isVisible) {
    return null;
  }

  return (
    <section
      className="production-workflow-operations-shell"
      data-projectpulse-production-operations="true"
      data-production-route={route}
    >
      <div className="production-workflow-operations-heading">
        <div>
          <span className="production-workflow-eyebrow">{config.eyebrow}</span>
          <h2>{config.title}</h2>
          <p>{config.description}</p>
        </div>
        <button
          type="button"
          className="production-workflow-refresh-button"
          onClick={() => setRefreshKey((value) => value + 1)}
        >
          Refresh
        </button>
      </div>

      <div className="production-workflow-card-grid">
        {config.cards.map((card) => (
          <ProductionCard key={card.key} card={card} data={results[card.key]} />
        ))}
      </div>
    </section>
  );
}
