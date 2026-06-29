const PROJECTPULSE_PRODUCTION_UI_VERSION = '019M-BW-through-CD';

const PRODUCTION_ROUTE_CONFIG = {
  dashboard: {
    id: 'projectpulse-production-readiness-ui',
    eyebrow: 'Production Readiness',
    title: 'Production Readiness Command Center',
    description: 'Live operational readiness across users, projects, workflow, exports, audit evidence, route contracts, and module registry integrity.',
    endpoints: [
      { key: 'readiness', label: 'Readiness', url: '/api/production/readiness-command-center' },
      { key: 'registry', label: 'Registry Integrity', url: '/api/navigation/registry-integrity' },
      { key: 'visibility', label: 'Module Visibility', url: '/api/dashboard/module-visibility-smoke' }
    ]
  },
  workflow: {
    id: 'projectpulse-production-workflow-ui',
    eyebrow: 'Workflow Operations',
    title: 'Production Workflow Operations Center',
    description: 'Production preflight, export evidence, reconciliation readiness, validation rules, and audit evidence for approval/export operations.',
    endpoints: [
      { key: 'operations', label: 'Operations Summary', url: '/api/workflow/operations-ui-data' },
      { key: 'preflight', label: 'Preflight Validation', url: '/api/workflow/preflight-validation' },
      { key: 'preflightEvents', label: 'Preflight Evidence', url: '/api/workflow/preflight-events?limit=8' },
      { key: 'exports', label: 'Export Evidence', url: '/api/export-packages/evidence-summary' },
      { key: 'reconciliation', label: 'Reconciliation Workbench', url: '/api/workflow/reconciliation-workbench' },
      { key: 'audit', label: 'Audit Events', url: '/api/audit-history/events?limit=8' },
      { key: 'rules', label: 'Validation Rules', url: '/api/workflow/validation-rules' }
    ]
  },
  'role-admin': {
    id: 'projectpulse-route-contract-ui',
    eyebrow: 'Security Governance',
    title: 'Route Permission Contract Center',
    description: 'Production route contracts, restricted roles, and navigation registry controls for role enforcement.',
    endpoints: [
      { key: 'contracts', label: 'Route Contracts', url: '/api/security/route-permission-contracts' },
      { key: 'registry', label: 'Registry Integrity', url: '/api/navigation/registry-integrity' },
      { key: 'roleMatrix', label: 'Role Matrix', url: '/api/security/role-access-matrix' }
    ]
  }
};

function getCurrentRouteKey() {
  const hash = window.location.hash || '#dashboard';
  return hash.replace(/^#\/?/, '').split(/[/?&]/)[0] || 'dashboard';
}

function getSessionToken() {
  try {
    const raw = window.localStorage.getItem('projectPulseAuthSession');
    if (!raw) return '';
    const parsed = JSON.parse(raw);
    return parsed.sessionToken || parsed.token || '';
  } catch {
    return '';
  }
}

function getViewAsUserId() {
  try {
    const raw = window.localStorage.getItem('projectPulseViewAsUser');
    if (!raw) return '';
    const parsed = JSON.parse(raw);
    if (typeof parsed === 'string') return parsed;
    return parsed.userId || parsed.id || parsed.value || '';
  } catch {
    const raw = window.localStorage.getItem('projectPulseViewAsUser');
    return raw || '';
  }
}

function getHeaders() {
  const headers = {
    Accept: 'application/json'
  };

  const token = getSessionToken();
  if (token) headers['X-ProjectPulse-Session'] = token;

  const viewAsUserId = getViewAsUserId();
  if (viewAsUserId) headers['X-ProjectPulse-View-As-User'] = viewAsUserId;

  return headers;
}

async function fetchProductionEndpoint(endpoint) {
  try {
    const response = await fetch(endpoint.url, {
      method: 'GET',
      headers: getHeaders(),
      credentials: 'same-origin'
    });

    let payload = null;
    const text = await response.text();

    try {
      payload = text ? JSON.parse(text) : null;
    } catch {
      payload = { raw: text };
    }

    return {
      ...endpoint,
      ok: response.ok,
      status: response.status,
      payload
    };
  } catch (error) {
    return {
      ...endpoint,
      ok: false,
      status: 0,
      payload: {
        status: 'request_failed',
        message: error?.message || 'Request failed.'
      }
    };
  }
}

function escapeHtml(value) {
  return String(value ?? '')
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#039;');
}

function formatValue(value) {
  if (value === null || value === undefined) return '—';
  if (typeof value === 'boolean') return value ? 'Yes' : 'No';
  if (typeof value === 'number') return Number.isInteger(value) ? String(value) : value.toFixed(2);
  return String(value);
}

function summarizePayload(result) {
  const payload = result.payload || {};

  if (!result.ok) {
    return {
      status: result.status,
      headline: payload.status || 'restricted',
      details: payload.message || 'This panel is not available for the selected role.'
    };
  }

  const summary = payload.summary || {};
  const moduleName = payload.module || result.label;

  const summaryEntries = Object.entries(summary)
    .slice(0, 6)
    .map(([key, value]) => ({
      label: key.replace(/([A-Z])/g, ' $1').replace(/^./, char => char.toUpperCase()),
      value
    }));

  if (summaryEntries.length > 0) {
    return {
      status: result.status,
      headline: moduleName,
      details: summaryEntries
    };
  }

  const count =
    payload.count ??
    payload.events?.length ??
    payload.packages?.length ??
    payload.contracts?.length ??
    payload.rules?.length ??
    payload.checks?.length ??
    payload.productionPanels?.length ??
    null;

  return {
    status: result.status,
    headline: moduleName,
    details: count === null ? 'Available' : `${count} record${count === 1 ? '' : 's'}`
  };
}

function renderSummaryDetails(details) {
  if (Array.isArray(details)) {
    return `
      <dl class="pp-prod-ui-metrics">
        ${details.map(item => `
          <div>
            <dt>${escapeHtml(item.label)}</dt>
            <dd>${escapeHtml(formatValue(item.value))}</dd>
          </div>
        `).join('')}
      </dl>
    `;
  }

  return `<p class="pp-prod-ui-detail">${escapeHtml(details)}</p>`;
}

function renderChecks(payload) {
  const checks = payload?.checks || [];
  if (!checks.length) return '';

  return `
    <div class="pp-prod-ui-subsection">
      <h4>Production Readiness Checks</h4>
      <div class="pp-prod-ui-list">
        ${checks.map(check => `
          <article class="pp-prod-ui-list-item">
            <strong>${escapeHtml(check.check)}</strong>
            <span>${escapeHtml(formatValue(check.value))}</span>
            <em data-status="${escapeHtml(check.status)}">${escapeHtml(check.status)}</em>
          </article>
        `).join('')}
      </div>
    </div>
  `;
}

function renderEvents(payload) {
  const events = payload?.events || [];
  if (!events.length) return '';

  return `
    <div class="pp-prod-ui-subsection">
      <h4>Recent Evidence</h4>
      <div class="pp-prod-ui-table-wrap">
        <table class="pp-prod-ui-table">
          <thead>
            <tr>
              <th>Action</th>
              <th>Actor</th>
              <th>Items</th>
              <th>Created</th>
            </tr>
          </thead>
          <tbody>
            ${events.slice(0, 8).map(event => `
              <tr>
                <td>${escapeHtml(event.preflightAction || event.action || event.entityType || 'Evidence')}</td>
                <td>${escapeHtml(event.actorName || 'System')}</td>
                <td>${escapeHtml(formatValue(event.eligibleItemCount ?? event.itemCount ?? event.count ?? '—'))}</td>
                <td>${escapeHtml(event.createdAt || '—')}</td>
              </tr>
            `).join('')}
          </tbody>
        </table>
      </div>
    </div>
  `;
}

function renderPackages(payload) {
  const packages = payload?.packages || payload?.exports || [];
  if (!packages.length) return '';

  return `
    <div class="pp-prod-ui-subsection">
      <h4>Export Package Evidence</h4>
      <div class="pp-prod-ui-table-wrap">
        <table class="pp-prod-ui-table">
          <thead>
            <tr>
              <th>Package</th>
              <th>Status</th>
              <th>Items</th>
              <th>Downloads</th>
              <th>Evidence</th>
            </tr>
          </thead>
          <tbody>
            ${packages.slice(0, 8).map(pkg => `
              <tr>
                <td>${escapeHtml(pkg.fileName || pkg.exportFormat || pkg.exportId || 'Export package')}</td>
                <td>${escapeHtml(pkg.exportStatus || pkg.status || '—')}</td>
                <td>${escapeHtml(formatValue(pkg.itemCount))}</td>
                <td>${escapeHtml(formatValue(pkg.packageDownloadCount ?? 0))}</td>
                <td>${escapeHtml(pkg.productionEvidenceReady ? 'Ready' : 'Pending')}</td>
              </tr>
            `).join('')}
          </tbody>
        </table>
      </div>
    </div>
  `;
}

function renderContracts(payload) {
  const contracts = payload?.contracts || [];
  if (!contracts.length) return '';

  return `
    <div class="pp-prod-ui-subsection">
      <h4>Route Permission Contracts</h4>
      <div class="pp-prod-ui-table-wrap">
        <table class="pp-prod-ui-table">
          <thead>
            <tr>
              <th>Route</th>
              <th>Module</th>
              <th>Allowed Roles</th>
              <th>Restricted Roles</th>
            </tr>
          </thead>
          <tbody>
            ${contracts.slice(0, 10).map(contract => `
              <tr>
                <td>${escapeHtml(contract.routePath || contract.routeKey)}</td>
                <td>${escapeHtml(contract.moduleName)}</td>
                <td>${escapeHtml((contract.allowedRoles || []).join(', '))}</td>
                <td>${escapeHtml((contract.restrictedRoles || []).join(', '))}</td>
              </tr>
            `).join('')}
          </tbody>
        </table>
      </div>
    </div>
  `;
}

function renderRules(payload) {
  const rules = payload?.rules || [];
  if (!rules.length) return '';

  return `
    <div class="pp-prod-ui-subsection">
      <h4>Workflow Validation Rules</h4>
      <div class="pp-prod-ui-list">
        ${rules.map(rule => `
          <article class="pp-prod-ui-list-item">
            <strong>${escapeHtml(rule.title || rule.ruleCode)}</strong>
            <span>${escapeHtml(rule.evidence || '')}</span>
            <em data-status="${escapeHtml(rule.status)}">${escapeHtml(rule.status || 'configured')}</em>
          </article>
        `).join('')}
      </div>
    </div>
  `;
}

function renderEndpointCard(result) {
  const summary = summarizePayload(result);
  const payload = result.payload || {};

  return `
    <article class="pp-prod-ui-card" data-endpoint-key="${escapeHtml(result.key)}" data-http-status="${escapeHtml(result.status)}">
      <div class="pp-prod-ui-card-head">
        <span>${escapeHtml(result.label)}</span>
        <strong class="${result.ok ? 'is-ready' : 'is-restricted'}">HTTP ${escapeHtml(result.status)}</strong>
      </div>
      <h4>${escapeHtml(summary.headline)}</h4>
      ${renderSummaryDetails(summary.details)}
      ${renderChecks(payload)}
      ${renderEvents(payload)}
      ${renderPackages(payload)}
      ${renderContracts(payload)}
      ${renderRules(payload)}
    </article>
  `;
}

function locateHost(routeKey) {
  const routeSpecificSelectors = {
    dashboard: [
      '[data-route="dashboard"]',
      '.dashboard-page',
      '.dashboard-shell',
      '.app-dashboard',
      'main'
    ],
    workflow: [
      '[data-route="workflow"]',
      '.approval-export-audit-workflow-center',
      '.workflow-center',
      '.workflow-page',
      'main'
    ],
    'role-admin': [
      '[data-route="role-admin"]',
      '.role-admin-directory-panel',
      '.role-admin-page',
      'main'
    ]
  };

  const selectors = routeSpecificSelectors[routeKey] || ['main'];

  for (const selector of selectors) {
    const target = document.querySelector(selector);
    if (target) return target;
  }

  return document.querySelector('#root') || document.body;
}

function removeInactivePanels(activeId) {
  Object.values(PRODUCTION_ROUTE_CONFIG).forEach(config => {
    const element = document.getElementById(config.id);
    if (element && config.id !== activeId) {
      element.remove();
    }
  });
}

async function renderProductionPanel(routeKey) {
  const config = PRODUCTION_ROUTE_CONFIG[routeKey];
  if (!config) {
    removeInactivePanels('');
    return;
  }

  const host = locateHost(routeKey);
  if (!host) return;

  removeInactivePanels(config.id);

  let panel = document.getElementById(config.id);
  if (!panel) {
    panel = document.createElement('section');
    panel.id = config.id;
    panel.className = 'pp-prod-ui-shell';
    panel.setAttribute('data-projectpulse-production-ui-version', PROJECTPULSE_PRODUCTION_UI_VERSION);

    if (host.firstElementChild) {
      host.insertBefore(panel, host.firstElementChild.nextSibling || null);
    } else {
      host.appendChild(panel);
    }
  }

  panel.innerHTML = `
    <div class="pp-prod-ui-header">
      <div>
        <p>${escapeHtml(config.eyebrow)}</p>
        <h2>${escapeHtml(config.title)}</h2>
        <span>${escapeHtml(config.description)}</span>
      </div>
      <button type="button" class="pp-prod-ui-refresh">Refresh</button>
    </div>
    <div class="pp-prod-ui-grid">
      <article class="pp-prod-ui-card">
        <div class="pp-prod-ui-card-head">
          <span>Loading</span>
          <strong>Pending</strong>
        </div>
        <h4>Loading production data...</h4>
        <p class="pp-prod-ui-detail">Checking role-scoped production endpoints.</p>
      </article>
    </div>
  `;

  const refreshButton = panel.querySelector('.pp-prod-ui-refresh');
  if (refreshButton) {
    refreshButton.addEventListener('click', () => renderProductionPanel(routeKey), { once: true });
  }

  const results = await Promise.all(config.endpoints.map(fetchProductionEndpoint));

  if (getCurrentRouteKey() !== routeKey) return;

  panel.innerHTML = `
    <div class="pp-prod-ui-header">
      <div>
        <p>${escapeHtml(config.eyebrow)}</p>
        <h2>${escapeHtml(config.title)}</h2>
        <span>${escapeHtml(config.description)}</span>
      </div>
      <button type="button" class="pp-prod-ui-refresh">Refresh</button>
    </div>
    <div class="pp-prod-ui-grid">
      ${results.map(renderEndpointCard).join('')}
    </div>
  `;

  const nextRefreshButton = panel.querySelector('.pp-prod-ui-refresh');
  if (nextRefreshButton) {
    nextRefreshButton.addEventListener('click', () => renderProductionPanel(routeKey), { once: true });
  }
}

let scheduledRender = null;

function scheduleRender() {
  window.clearTimeout(scheduledRender);
  scheduledRender = window.setTimeout(() => {
    renderProductionPanel(getCurrentRouteKey());
  }, 200);
}

function startProductionOperationsUi() {
  window.addEventListener('hashchange', scheduleRender);
  window.addEventListener('storage', event => {
    if (event.key === 'projectPulseAuthSession' || event.key === 'projectPulseViewAsUser') {
      scheduleRender();
    }
  });

  const observer = new MutationObserver(() => {
    const routeKey = getCurrentRouteKey();
    const config = PRODUCTION_ROUTE_CONFIG[routeKey];
    if (config && !document.getElementById(config.id)) {
      scheduleRender();
    }
  });

  observer.observe(document.body, {
    childList: true,
    subtree: true
  });

  scheduleRender();
}

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', startProductionOperationsUi, { once: true });
} else {
  startProductionOperationsUi();
}
