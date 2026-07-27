const ACTIVE_ROUTE = 'entra-secret-administration';
const RETIRED_ROUTE = 'global-mail-configuration';
const ACTIVE_MODULE_NAME = 'Microsoft Integration Connection';
const CONFIG_MARKER = 'PROJECTPULSE_MICROSOFT_INTEGRATION_JSON:';
const DOCUMENT_PATH = '/api/native-administration/065/document';
const SERVICES_APPLY_PATH = '/api/microsoft-integration/services-apply-profile';
const PREVIEW_ROUTE = '/api/admin/azure/users/preview';
const LEGACY_IMPORT_ROUTE = '/api/admin/azure/users/import-selected';
const ACTIVE_IMPORT_ROUTE = '/api/microsoft-integration/directory-users/import-selected';
const ROLE_NORMALIZATION_ROUTES = new Set([
  LEGACY_IMPORT_ROUTE,
  ACTIVE_IMPORT_ROUTE,
  '/api/admin/azure/config',
  '/api/admin/azure/import-settings'
]);

function currentRoute() {
  return window.location.hash.replace(/^#/, '').split('?')[0].trim();
}

function runtimeEnvironmentMode() {
  const host = window.location.hostname.toLowerCase();
  if (host.includes('-test.') || host === 'localhost' || host === '127.0.0.1') return 'test';
  return 'production';
}

function redirectRetiredRoute() {
  if (currentRoute() === RETIRED_ROUTE) {
    window.location.replace(`#${ACTIVE_ROUTE}`);
  }
}

function closestModuleSurface(element) {
  return element.closest('a, button, li, article, section, .module-card, .workspace-card, .navigation-item, .sidebar-item');
}

function setTextIfChanged(element, value) {
  if (!element || element.textContent === value) return;
  element.textContent = value;
}

function suppressMovedSurface(element, reason) {
  if (!element) return;
  element.setAttribute('data-moved-to-module-065', reason || 'true');
  element.hidden = true;
  element.setAttribute('aria-hidden', 'true');
  element.style.setProperty('display', 'none', 'important');
}

function restoreModule010Preview(element) {
  if (!element) return;
  element.hidden = false;
  element.removeAttribute('aria-hidden');
  element.removeAttribute('data-moved-to-module-065');
  element.style.removeProperty('display');
  element.setAttribute('data-module-010-preview-preserved', 'true');
}

function activateAuthoritativeModule065() {
  const active = currentRoute() === ACTIVE_ROUTE;
  document.body.classList.toggle('projectpulse-microsoft-integration-active', active);
  if (!active) return;

  document.querySelectorAll([
    '.entra-secret-center[data-module="065"]',
    '.native-module-administration[data-module-administration="065"]',
    '.entra-secret-administration-route-panel',
    '[data-phase="065_COMPLETE_SOURCE_LOCKED_RUNTIME"]'
  ].join(',')).forEach((element) => {
    if (!element.closest('.microsoft-integration-portal')) {
      suppressMovedSurface(element, 'legacy-module-065-surface');
    }
  });

  const portal = document.querySelector('.microsoft-integration-portal[data-module="065"], .microsoft-integration-portal');
  if (portal) {
    portal.setAttribute('data-microsoft-integration-authoritative', 'true');
    portal.hidden = false;
    portal.removeAttribute('aria-hidden');
    portal.style.removeProperty('display');
    setTextIfChanged(portal.querySelector('.microsoft-integration-heading h1'), ACTIVE_MODULE_NAME);
  }
}

function normalizeModuleSurfaces() {
  document.querySelectorAll(`a[href="#${RETIRED_ROUTE}"], [data-route="${RETIRED_ROUTE}"]`).forEach((element) => {
    const surface = closestModuleSurface(element) || element;
    surface.setAttribute('data-module-067-retired', 'true');
    surface.hidden = true;
    surface.style.setProperty('display', 'none', 'important');
  });

  document.querySelectorAll('a, button, h1, h2, h3, h4, p, span, strong, div').forEach((element) => {
    const text = element.textContent?.trim() || '';
    if (!text || element.children.length > 4) return;

    if (/\bMODULE\s*067\b/i.test(text) || /^Global Mail Configuration(?: Center)?$/i.test(text)) {
      const surface = closestModuleSurface(element);
      if (surface && !surface.querySelector(`a[href="#${ACTIVE_ROUTE}"]`)) {
        surface.setAttribute('data-module-067-retired', 'true');
        surface.hidden = true;
        surface.style.setProperty('display', 'none', 'important');
      }
    }

    if (/^Entra Secret Administration(?: Metadata management)?$/i.test(text)
      || /^Microsoft Integration$/i.test(text)) {
      setTextIfChanged(element, ACTIVE_MODULE_NAME);
    }
  });

  const module010 = document.querySelector('#azure-admin');
  if (module010) {
    module010.querySelectorAll('.azure-config-card, .azure-sync-summary-card').forEach((element) => {
      suppressMovedSurface(element, 'tenant-sync-configuration');
    });

    module010.querySelectorAll('button').forEach((button) => {
      const label = button.textContent?.trim().toLowerCase();
      if (label === 'sync now' || label === 'reconcile inactive users' || label === 'save configuration') {
        suppressMovedSurface(button, 'configuration-action');
      }
    });

    const previewCard = module010.querySelector('.azure-preview-card');
    restoreModule010Preview(previewCard);
    previewCard?.querySelectorAll('button, .azure-admin-heading-actions, .azure-selection-toolbar, .azure-filter-grid, .azure-preview-table')
      .forEach(restoreModule010Preview);

    const eyebrow = module010.querySelector('.section-heading .eyebrow');
    const heading = module010.querySelector('.section-heading h1');
    const copy = module010.querySelector('.section-heading .section-copy');
    setTextIfChanged(eyebrow, 'MODULE 010 · AZURE / ENTRA DIRECTORY USERS');
    setTextIfChanged(heading, 'Preview and import Entra users');
    setTextIfChanged(copy, `Preview Entra directory users, filter the list, select the people to import, and confirm that imported users appear in ProjectPulse. Tenant, synchronization, identity, calendar, and Microsoft 365 mail settings are managed in Module 065 ${ACTIVE_MODULE_NAME}.`);
  }

  activateAuthoritativeModule065();
}

function canonicalRoleCode(value) {
  const normalized = String(value || '').trim().toUpperCase();
  if (!normalized || normalized === 'ENGINEERING') return 'ENGINEER';
  return normalized;
}

function normalizeRolePayload(pathname, init) {
  if (!ROLE_NORMALIZATION_ROUTES.has(pathname) || typeof init?.body !== 'string') return init;

  try {
    const payload = JSON.parse(init.body);
    payload.defaultRoleCode = canonicalRoleCode(payload.defaultRoleCode);
    return { ...init, body: JSON.stringify(payload) };
  } catch {
    return init;
  }
}

function activeServicesProfile(payload) {
  try {
    const notes = payload?.document?.configuration?.notes;
    if (typeof notes !== 'string' || !notes.startsWith(CONFIG_MARKER)) return null;
    const stored = JSON.parse(notes.slice(CONFIG_MARKER.length));
    const tenants = Array.isArray(stored?.tenants) ? stored.tenants : [];
    const runtimeEnvironment = runtimeEnvironmentMode();
    const active = tenants.find((tenant) => tenant?.environmentMode === runtimeEnvironment)
      || tenants.find((tenant) => tenant?.key === stored?.activeTenantKey && tenant?.environmentMode === runtimeEnvironment)
      || tenants.find((tenant) => tenant?.environmentMode === stored?.activeEnvironmentMode && tenant?.environmentMode === runtimeEnvironment);
    if (!active) return null;
    const services = active.services || active.servicesConnection || {};
    const clientId = services.clientId || services.applicationId || active.serviceClientId || active.clientId || '';
    if (!active.tenantId || !clientId) return null;
    return {
      environmentMode: active.environmentMode,
      tenantKey: active.key || active.tenantKey,
      tenantId: active.tenantId,
      clientId,
      graphScopes: services.graphScopes || services.scopes || active.graphScopes || '',
      senderMailbox: stored?.mail?.senderAddress || ''
    };
  } catch {
    return null;
  }
}

function responseFailure(status, payload, fallback) {
  return new Response(JSON.stringify({
    status: payload?.status || 'module_065_services_runtime_unavailable',
    message: payload?.message || fallback,
    module: '010',
    configurationSource: 'module_065'
  }), {
    status: status >= 400 && status <= 599 ? status : 503,
    headers: { 'Content-Type': 'application/json' }
  });
}

async function applyStoredServicesProfile(previousFetch, init) {
  const headers = new Headers(init?.headers || {});
  const documentResponse = await previousFetch(DOCUMENT_PATH, {
    method: 'GET',
    cache: 'no-store',
    credentials: 'include',
    headers
  });
  let documentPayload = {};
  try { documentPayload = await documentResponse.json(); } catch { /* controlled failure below */ }
  if (!documentResponse.ok) {
    return responseFailure(documentResponse.status, documentPayload, 'Module 065 Microsoft services configuration could not be loaded.');
  }

  const profile = activeServicesProfile(documentPayload);
  if (!profile) {
    return responseFailure(503, {
      status: 'module_065_services_profile_incomplete',
      message: `Complete and save the ${runtimeEnvironmentMode() === 'production' ? 'Production' : 'Test'} Microsoft services tenant ID and application/client ID in Module 065 before previewing Entra users.`
    });
  }

  headers.set('Content-Type', 'application/json');
  const applyResponse = await previousFetch(SERVICES_APPLY_PATH, {
    method: 'POST',
    cache: 'no-store',
    credentials: 'include',
    headers,
    body: JSON.stringify(profile)
  });
  let applyPayload = {};
  try { applyPayload = await applyResponse.json(); } catch { /* controlled failure below */ }
  if (!applyResponse.ok) {
    return responseFailure(applyResponse.status, applyPayload, 'Module 065 could not activate the Microsoft services profile for Entra preview.');
  }
  if (applyPayload?.runtimeActivated !== true) {
    return responseFailure(409, {
      status: 'module_065_services_profile_not_active',
      message: 'Module 010 preview requires the Module 065 services profile for the currently running ProjectPulse environment.'
    });
  }
  return null;
}

function installMicrosoftIntegrationCompatibility() {
  if (window.__projectPulseMicrosoftIntegrationCompatibilityInstalled) return;
  window.__projectPulseMicrosoftIntegrationCompatibilityInstalled = true;

  const previousFetch = window.fetch.bind(window);
  window.fetch = async (input, init = {}) => {
    const rawUrl = typeof input === 'string' ? input : input?.url;
    const method = String(init?.method || (input instanceof Request ? input.method : '') || 'GET').toUpperCase();

    if (!rawUrl || !['POST', 'PUT', 'PATCH'].includes(method)) return previousFetch(input, init);

    const url = new URL(rawUrl, window.location.origin);
    const normalizedInit = normalizeRolePayload(url.pathname, init);

    if (url.pathname === PREVIEW_ROUTE && method === 'POST') {
      const failure = await applyStoredServicesProfile(previousFetch, normalizedInit);
      if (failure) return failure;
      return previousFetch(input, normalizedInit);
    }

    if (url.pathname !== LEGACY_IMPORT_ROUTE) {
      return previousFetch(input, normalizedInit);
    }

    const replacement = new URL(ACTIVE_IMPORT_ROUTE, window.location.origin);
    const nextInput = input instanceof Request
      ? new Request(replacement.toString(), input)
      : replacement.pathname;

    return previousFetch(nextInput, normalizedInit);
  };
}

function refresh() {
  redirectRetiredRoute();
  normalizeModuleSurfaces();
}

installMicrosoftIntegrationCompatibility();
refresh();

window.addEventListener('hashchange', refresh);
window.addEventListener('projectpulse:auth-session-ready', refresh);

const observer = new MutationObserver(() => {
  window.clearTimeout(window.__projectPulseMicrosoftIntegrationRefreshTimer);
  window.__projectPulseMicrosoftIntegrationRefreshTimer = window.setTimeout(refresh, 25);
});
observer.observe(document.documentElement, { childList: true, subtree: true });
