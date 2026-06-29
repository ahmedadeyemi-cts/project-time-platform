(() => {
  const PANEL_ID = 'projectpulse-production-operations-panel';
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

  let syncTimer = 0;
  let renderSequence = 0;

  function escapeHtml(value) {
    return String(value ?? '')
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;')
      .replace(/'/g, '&#039;');
  }

  function getSessionToken() {
    try {
      const raw = window.localStorage.getItem(AUTH_STORAGE_KEY);
      if (!raw) return '';

      const parsed = JSON.parse(raw);
      const token = parsed?.sessionToken || parsed?.token || parsed?.accessToken;

      return typeof token === 'string' ? token.trim() : '';
    } catch {
      return '';
    }
  }

  function getViewAsUserId() {
    try {
      const raw = window.localStorage.getItem(VIEW_AS_STORAGE_KEY);
      if (!raw) return '';

      const parsed = JSON.parse(raw);

      if (typeof parsed === 'string') {
        return parsed.trim();
      }

      const userId = parsed?.userId || parsed?.id || parsed?.value;

      return typeof userId === 'string' ? userId.trim() : '';
    } catch {
      return '';
    }
  }

  function hasSession() {
    return getSessionToken().length > 0;
  }

  function removePanel() {
    document.getElementById(PANEL_ID)?.remove();

    document
      .querySelectorAll('[data-projectpulse-production-operations="true"]')
      .forEach((element) => {
        if (element.id !== PANEL_ID) {
          element.remove();
        }
      });
  }

  function currentRoute() {
    return (window.location.hash || '#dashboard').replace(/^#\/?/, '').split(/[/?]/)[0] || 'dashboard';
  }

  function getMountNode() {
    return (
      document.querySelector('main') ||
      document.querySelector('.app-shell') ||
      document.querySelector('.enterprise-shell') ||
      document.querySelector('#root > div') ||
      document.getElementById('root') ||
      document.body
    );
  }

  function isSupportedRoute(route) {
    return Object.prototype.hasOwnProperty.call(routeConfigs, route);
  }

  function shouldSuppress() {
    if (!hasSession()) {
      removePanel();
      return true;
    }

    return false;
  }

  function scheduleSync(force = false) {
    window.clearTimeout(syncTimer);
    syncTimer = window.setTimeout(() => sync(force), 150);
  }

  async function fetchJson(path) {
    if (!hasSession()) {
      return {
        suppressed: true,
        httpStatus: 0,
        status: 'signed_out_suppressed',
        message: 'Production operations panels are hidden until sign-in.'
      };
    }

    const headers = {
      Accept: 'application/json',
      'X-ProjectPulse-Session': getSessionToken()
    };

    const viewAsUserId = getViewAsUserId();
    if (viewAsUserId) {
      headers['X-ProjectPulse-View-As-User'] = viewAsUserId;
    }

    try {
      const response = await window.fetch(path, {
        method: 'GET',
        headers,
        credentials: 'same-origin'
      });

      const text = await response.text();
      let payload;

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

  function cardHtml(card, data) {
    if (data?.suppressed) {
      return '';
    }

    if (!data?.ok && data?.httpStatus) {
      return `
        <article class="production-workflow-card">
          <div class="production-workflow-card-header">
            <span>${escapeHtml(card.label)}</span>
            <strong>HTTP ${escapeHtml(data.httpStatus)}</strong>
          </div>
          <h4>${escapeHtml(data?.status || 'request_failed')}</h4>
          <p>${escapeHtml(data?.message || 'The request did not complete successfully.')}</p>
        </article>
      `;
    }

    const mapped = card.mapper(data || {});

    return `
      <article class="production-workflow-card">
        <div class="production-workflow-card-header">
          <span>${escapeHtml(card.label)}</span>
          <strong>${escapeHtml(mapped.status || 'Ready')}</strong>
        </div>
        <h4>${escapeHtml(mapped.title || 'ready')}</h4>
        <p>${escapeHtml(mapped.detail || 'Production evidence is available.')}</p>
      </article>
    `;
  }

  function loadingPanelHtml(config) {
    return `
      <section id="${PANEL_ID}" class="production-workflow-operations-shell" data-projectpulse-production-operations="true">
        <div class="production-workflow-operations-heading">
          <div>
            <span class="production-workflow-eyebrow">${escapeHtml(config.eyebrow)}</span>
            <h2>${escapeHtml(config.title)}</h2>
            <p>${escapeHtml(config.description)}</p>
          </div>
          <button type="button" class="production-workflow-refresh-button" data-projectpulse-production-refresh="true">Refresh</button>
        </div>
        <div class="production-workflow-card-grid">
          ${config.cards
            .map(
              (card) => `
                <article class="production-workflow-card">
                  <div class="production-workflow-card-header">
                    <span>${escapeHtml(card.label)}</span>
                    <strong>Loading</strong>
                  </div>
                  <h4>loading</h4>
                  <p>Collecting production evidence.</p>
                </article>
              `
            )
            .join('')}
        </div>
      </section>
    `;
  }

  async function renderPanel(route, force = false) {
    if (shouldSuppress()) return;

    const config = routeConfigs[route];
    if (!config) {
      removePanel();
      return;
    }

    const mount = getMountNode();
    if (!mount) return;

    const existing = document.getElementById(PANEL_ID);
    if (existing && existing.dataset.route === route && !force) {
      return;
    }

    removePanel();

    const wrapper = document.createElement('div');
    wrapper.innerHTML = loadingPanelHtml(config).trim();

    const panel = wrapper.firstElementChild;
    panel.dataset.route = route;
    panel.dataset.projectpulseProductionOperations = 'true';

    mount.appendChild(panel);

    panel
      .querySelector('[data-projectpulse-production-refresh="true"]')
      ?.addEventListener('click', () => renderPanel(route, true));

    const sequence = ++renderSequence;
    const results = await Promise.all(config.cards.map((card) => fetchJson(card.path)));

    if (sequence !== renderSequence) return;
    if (shouldSuppress()) return;

    const currentPanel = document.getElementById(PANEL_ID);
    if (!currentPanel) return;

    const grid = currentPanel.querySelector('.production-workflow-card-grid');
    if (!grid) return;

    grid.innerHTML = config.cards
      .map((card, index) => cardHtml(card, results[index]))
      .filter(Boolean)
      .join('');

    if (!grid.innerHTML.trim()) {
      removePanel();
    }
  }

  function sync(force = false) {
    const route = currentRoute();

    if (!hasSession()) {
      removePanel();
      return;
    }

    if (!isSupportedRoute(route)) {
      removePanel();
      return;
    }

    renderPanel(route, force);
  }

  window.addEventListener('hashchange', () => scheduleSync(true));
  window.addEventListener('focus', () => scheduleSync(false));
  window.addEventListener('storage', (event) => {
    if (event.key === AUTH_STORAGE_KEY || event.key === VIEW_AS_STORAGE_KEY) {
      scheduleSync(true);
    }
  });

  const observer = new MutationObserver(() => scheduleSync(false));
  observer.observe(document.documentElement, {
    childList: true,
    subtree: true
  });

  window.setInterval(() => scheduleSync(false), 2000);

  scheduleSync(true);
})();
