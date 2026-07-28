const ACTIVE_ROUTE = 'entra-secret-administration';
const RETIRED_ROUTE = 'global-mail-configuration';
const CONFIG_MARKER = 'PROJECTPULSE_MICROSOFT_INTEGRATION_JSON:';
const DOCUMENT_PATH = '/api/native-administration/065/document';
const SERVICES_APPLY_PATH = '/api/microsoft-integration/services-apply-profile';
const PREVIEW_ROUTE = '/api/admin/azure/users/preview';
const LEGACY_IMPORT_ROUTE = '/api/admin/azure/users/import-selected';
const ACTIVE_IMPORT_ROUTE = '/api/microsoft-integration/directory-users/import-selected';
const LEGACY_SYNC_ROUTE = '/api/admin/azure/sync/run';
const ACTIVE_SYNC_ROUTE = '/api/microsoft-integration/directory-users/sync-now';
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
  if (host.includes('-test.') || host.endsWith('.onenecklab.com') || host === 'localhost' || host === '127.0.0.1') return 'test';
  return 'production';
}

function synchronizeRouteState() {
  const route = currentRoute();
  if (route === RETIRED_ROUTE) {
    window.location.replace(`#${ACTIVE_ROUTE}`);
    return;
  }
  document.body?.classList.toggle('projectpulse-microsoft-integration-active', route === ACTIVE_ROUTE);
  document.body?.classList.toggle('projectpulse-module010-directory-active', route === 'azure-admin');
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
    const active = tenants.find((tenant) => String(tenant?.environmentMode || '').toLowerCase() === runtimeEnvironment);
    if (!active) return null;
    const services = active.services || active.servicesConnection || {};
    const clientId = services.clientId || services.applicationId || active.serviceClientId || active.clientId || '';
    if (!active.tenantId || !clientId) return null;
    return {
      environmentMode: runtimeEnvironment,
      tenantKey: active.key || active.tenantKey,
      tenantId: active.tenantId,
      clientId,
      graphScopes: services.graphScopes || services.scopes || active.graphScopes || '',
      senderMailbox: active?.mail?.senderAddress || stored?.mail?.senderAddress || ''
    };
  } catch {
    return null;
  }
}

function responseFailure(status, payload, fallback, extra = {}) {
  return new Response(JSON.stringify({
    status: payload?.status || 'module_065_services_runtime_unavailable',
    message: payload?.message || fallback,
    module: '010',
    configurationSource: 'module_065',
    runtimeEnvironment: runtimeEnvironmentMode(),
    ...extra
  }), {
    status: status >= 400 && status <= 599 ? status : 503,
    headers: {
      'Content-Type': 'application/json',
      'Cache-Control': 'no-store'
    }
  });
}

async function readJson(response) {
  try { return await response.clone().json(); } catch { return {}; }
}

async function applyStoredServicesProfile(previousFetch, init) {
  const headers = new Headers(init?.headers || {});
  const documentResponse = await previousFetch(DOCUMENT_PATH, {
    method: 'GET',
    cache: 'no-store',
    credentials: 'include',
    headers
  });
  const documentPayload = await readJson(documentResponse);
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
  const applyPayload = await readJson(applyResponse);
  if (!applyResponse.ok) {
    return responseFailure(applyResponse.status, applyPayload, 'Module 065 could not activate the Microsoft services profile for Entra preview.', {
      selectedEnvironment: profile.environmentMode,
      returnedRuntimeEnvironment: applyPayload?.runtimeEnvironment || ''
    });
  }

  if (applyPayload?.runtimeActivated !== true
      || String(applyPayload?.runtimeEnvironment || '').toLowerCase() !== profile.environmentMode) {
    return responseFailure(409, {
      status: 'module_065_services_profile_not_active',
      message: 'Module 010 preview requires the Module 065 services profile for the currently running ProjectPulse environment.'
    }, '', {
      selectedEnvironment: profile.environmentMode,
      returnedRuntimeEnvironment: applyPayload?.runtimeEnvironment || '',
      runtimeActivated: Boolean(applyPayload?.runtimeActivated)
    });
  }
  return null;
}

function replacementInput(input, replacementPath) {
  const replacement = new URL(replacementPath, window.location.origin);
  return input instanceof Request
    ? new Request(replacement.toString(), input)
    : `${replacement.pathname}${replacement.search}`;
}

function installMicrosoftIntegrationCompatibility() {
  if (window.__projectPulseMicrosoftIntegrationCompatibilityInstalled) return;
  window.__projectPulseMicrosoftIntegrationCompatibilityInstalled = true;

  const previousFetch = window.fetch.bind(window);
  window.fetch = async (input, init = {}) => {
    const rawUrl = typeof input === 'string' ? input : input?.url;
    const method = String(init?.method || (input instanceof Request ? input.method : '') || 'GET').toUpperCase();
    if (!rawUrl || !['POST', 'PUT', 'PATCH'].includes(method)) return previousFetch(input, init);

    let url;
    try { url = new URL(rawUrl, window.location.origin); } catch { return previousFetch(input, init); }
    if (url.origin !== window.location.origin) return previousFetch(input, init);

    const normalizedInit = normalizeRolePayload(url.pathname, init);
    if (url.pathname === PREVIEW_ROUTE && method === 'POST') {
      const failure = await applyStoredServicesProfile(previousFetch, normalizedInit);
      if (failure) return failure;
      return previousFetch(input, normalizedInit);
    }

    if (url.pathname === LEGACY_SYNC_ROUTE && method === 'POST') {
      const nextInit = typeof normalizedInit.body === 'string'
        ? normalizedInit
        : {
            ...normalizedInit,
            headers: { ...(normalizedInit.headers || {}), 'Content-Type': 'application/json' },
            body: JSON.stringify({ environmentMode: runtimeEnvironmentMode() })
          };
      return previousFetch(replacementInput(input, ACTIVE_SYNC_ROUTE), nextInit);
    }

    if (url.pathname === LEGACY_IMPORT_ROUTE && method === 'POST') {
      return previousFetch(replacementInput(input, ACTIVE_IMPORT_ROUTE), normalizedInit);
    }

    return previousFetch(input, normalizedInit);
  };
}

installMicrosoftIntegrationCompatibility();

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', synchronizeRouteState, { once: true });
} else {
  synchronizeRouteState();
}
window.addEventListener('hashchange', synchronizeRouteState);
window.addEventListener('pageshow', synchronizeRouteState);
window.addEventListener('projectpulse:auth-session-ready', synchronizeRouteState);
