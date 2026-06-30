import { useCallback, useEffect, useMemo, useState } from 'react';

const AUTH_STORAGE_KEY = 'projectPulseAuthSession';
const VIEW_AS_STORAGE_KEY = 'projectPulseViewAsUser';

const routeConfigs = {
  dashboard: {
    title: 'Production Readiness Command Center',
    eyebrow: 'Production Readiness',
    description:
      'Live operational readiness across users, customers, project intake, workflow, exports, audit evidence, route contracts, module registry integrity, and dashboard reporting coverage.',
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
      },
      {
        key: 'customerReporting',
        label: 'Customer Reporting',
        path: '/api/customers/overview',
        mapper: (data) => ({
          title: 'customer_directory',
          detail: `Customers: ${data?.customers?.length ?? 0}; contacts: ${data?.contacts?.length ?? 0}`,
          status: (data?.customers?.length ?? 0) > 0 ? 'Ready' : 'Review'
        })
      },
      {
        key: 'intakeReporting',
        label: 'Project Intake Reporting',
        path: '/api/project-intake/summary',
        mapper: (data) => ({
          title: data?.module ?? 'project_intake_summary',
          detail: `Intake records: ${data?.summary?.intakeCount ?? data?.count ?? 0}; open: ${data?.summary?.openIntakeCount ?? 0}`,
          status: 'Ready'
        })
      },
      {
        key: 'workflowReporting',
        label: 'Workflow Reporting',
        path: '/api/workflow/approval-export-summary',
        mapper: (data) => ({
          title: data?.module ?? 'workflow_summary',
          detail: `PM approvals: ${data?.summary?.pendingProjectApprovals ?? 0}; accounting: ${data?.summary?.pendingAccountingReview ?? 0}; exports 30d: ${data?.summary?.exportsLast30Days ?? 0}`,
          status: 'Ready'
        })
      },
      {
        key: 'auditReporting',
        label: 'Audit Reporting',
        path: '/api/audit-history/summary',
        mapper: (data) => ({
          title: data?.module ?? 'audit_summary',
          detail: `Events: ${data?.summary?.eventCount ?? data?.summary?.auditEventCount ?? 0}; actors: ${data?.summary?.actorCount ?? 0}`,
          status: 'Ready'
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

function normalizeOperationalText(value) {
  return String(value ?? '').toLowerCase();
}

function getOperationalTone(view) {
  const haystack = normalizeOperationalText(`${view?.status ?? ''} ${view?.title ?? ''} ${view?.detail ?? ''}`);

  if (haystack.includes('http 4') || haystack.includes('http 5') || haystack.includes('failed') || haystack.includes('blocked') || haystack.includes('error')) {
    return 'blocked';
  }

  if (haystack.includes('review') || haystack.includes('warning') || haystack.includes('issue') || haystack.includes('unknown')) {
    return 'attention';
  }

  if (haystack.includes('ready') || haystack.includes('active') || haystack.includes('ok')) {
    return 'ready';
  }

  if (haystack.includes('loading')) {
    return 'loading';
  }

  return 'neutral';
}

function buildProductionCardView(card, data) {
  if (!data) {
    return {
      status: 'Loading',
      title: 'loading',
      detail: 'Collecting production evidence.',
      httpStatus: null,
      tone: 'loading'
    };
  }

  if (!data.ok && data.httpStatus) {
    const view = {
      status: `HTTP ${data.httpStatus}`,
      title: data.status || 'request_failed',
      detail: data.message || 'The request did not complete successfully.',
      httpStatus: data.httpStatus,
      tone: 'blocked'
    };

    return view;
  }

  const mapped = card.mapper(data || {});
  const view = {
    status: mapped.status || 'Ready',
    title: mapped.title || 'ready',
    detail: mapped.detail || 'Production evidence is available.',
    httpStatus: data.httpStatus,
    tone: 'neutral'
  };

  return {
    ...view,
    tone: getOperationalTone(view)
  };
}

function ProductionCard({ card, data }) {
  const view = buildProductionCardView(card, data);

  return (
    <article className={`production-workflow-card production-workflow-card-${view.tone}`}>
      <div className="production-workflow-card-header">
        <span>{card.label}</span>
        <strong>{view.status}</strong>
      </div>
      <h4>{view.title}</h4>
      <p>{view.detail}</p>
      <small>{card.path} {view.httpStatus ? `· HTTP ${view.httpStatus}` : ''}</small>
    </article>
  );
}

export default function ProductionOperationsPanel() {
  const [route, setRoute] = useState(() => getCurrentRoute());
  const [token, setToken] = useState(() => getSessionToken());
  const [viewAsUserId, setViewAsUserId] = useState(() => getViewAsUserId());
  const [results, setResults] = useState({});
  const [cardToneFilter, setCardToneFilter] = useState('all');
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

  const cardViews = useMemo(() => {
    if (!config) return [];

    return config.cards.map((card) => ({
      card,
      data: results[card.key],
      view: buildProductionCardView(card, results[card.key])
    }));
  }, [config, results]);

  const operationsSummary = useMemo(() => {
    return cardViews.reduce((summary, item) => {
      const tone = item.view.tone || 'neutral';

      return {
        ...summary,
        total: summary.total + 1,
        [tone]: (summary[tone] ?? 0) + 1
      };
    }, {
      total: 0,
      ready: 0,
      attention: 0,
      blocked: 0,
      loading: 0,
      neutral: 0
    });
  }, [cardViews]);

  const filteredCardViews = useMemo(() => {
    if (cardToneFilter === 'all') return cardViews;
    return cardViews.filter((item) => item.view.tone === cardToneFilter);
  }, [cardViews, cardToneFilter]);

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
        <div className="production-workflow-heading-actions">
          <label>
            Card filter
            <select value={cardToneFilter} onChange={(event) => setCardToneFilter(event.target.value)}>
              <option value="all">All cards</option>
              <option value="ready">Ready</option>
              <option value="attention">Needs review</option>
              <option value="blocked">Blocked / failed</option>
              <option value="loading">Loading</option>
              <option value="neutral">Neutral</option>
            </select>
          </label>
          <button
            type="button"
            className="production-workflow-refresh-button"
            onClick={() => setRefreshKey((value) => value + 1)}
          >
            Refresh
          </button>
        </div>
      </div>

      <div className="production-workflow-summary-grid">
        <article><span>Total controls</span><strong>{operationsSummary.total}</strong><small>Configured for this route</small></article>
        <article><span>Ready</span><strong>{operationsSummary.ready}</strong><small>Operational/reporting evidence is healthy</small></article>
        <article><span>Needs review</span><strong>{operationsSummary.attention}</strong><small>Review, unknown, or warning status</small></article>
        <article><span>Blocked</span><strong>{operationsSummary.blocked}</strong><small>Failed or denied evidence checks</small></article>
      </div>

      {viewAsUserId ? (
        <div className="production-workflow-viewas-banner">
          View-As preview is active. Production write actions remain protected by read-only View-As enforcement.
        </div>
      ) : null}

      <div className="production-workflow-card-grid">
        {filteredCardViews.map(({ card, data }) => (
          <ProductionCard key={card.key} card={card} data={data} />
        ))}

        {filteredCardViews.length === 0 ? (
          <article className="production-workflow-card production-workflow-card-neutral">
            <div className="production-workflow-card-header">
              <span>No cards</span>
              <strong>Filtered</strong>
            </div>
            <h4>No production controls match the selected filter.</h4>
            <p>Change the card filter to review all configured operational controls for this route.</p>
          </article>
        ) : null}
      </div>
    </section>
  );
}
