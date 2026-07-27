const INSTALL_MARKER = '__projectPulseMicrosoftSsoRuntimeActivationInstalled';
const CONFIG_MARKER = 'PROJECTPULSE_MICROSOFT_INTEGRATION_JSON:';
const DOCUMENT_PATH = '/api/native-administration/065/document';
const SSO_APPLY_PATH = '/api/microsoft-integration/sso-apply-profile';
const SERVICES_APPLY_PATH = '/api/microsoft-integration/services-apply-profile';
const STATUS_ID = 'projectpulse-microsoft-connection-runtime-status';

function requestUrl(input) {
  try {
    return new URL(typeof input === 'string' ? input : input?.url, window.location.origin);
  } catch {
    return null;
  }
}

function requestMethod(input, init) {
  return String(init?.method || input?.method || 'GET').toUpperCase();
}

function requestBody(input, init) {
  if (typeof init?.body === 'string') return init.body;
  return null;
}

function activeRuntimeProfiles(bodyText) {
  try {
    const request = JSON.parse(bodyText || '{}');
    const notes = request?.document?.configuration?.notes;
    if (typeof notes !== 'string' || !notes.startsWith(CONFIG_MARKER)) return null;
    const configuration = JSON.parse(notes.slice(CONFIG_MARKER.length));
    const tenants = Array.isArray(configuration?.tenants) ? configuration.tenants : [];
    const active = tenants.find((tenant) => tenant?.key === configuration?.activeTenantKey)
      || tenants.find((tenant) => tenant?.environmentMode === configuration?.activeEnvironmentMode)
      || null;
    if (!active) return null;

    const sso = active.sso || active.ssoConnection || {};
    const services = active.services || active.servicesConnection || {};
    const mail = configuration.mail || {};
    const ssoClientId = sso.clientId || sso.applicationId || active.ssoClientId || '';
    const servicesClientId = services.clientId || services.applicationId || active.serviceClientId || active.clientId || '';

    return {
      sso: active.tenantId && ssoClientId && (sso.redirectUri || active.redirectUri)
        ? {
            environmentMode: active.environmentMode,
            tenantId: active.tenantId,
            clientId: ssoClientId,
            redirectUri: sso.redirectUri || active.redirectUri,
            allowedDomains: sso.allowedDomains || active.ssoAllowedDomains || active.tenantDomain || ''
          }
        : null,
      services: active.tenantId && servicesClientId
        ? {
            environmentMode: active.environmentMode,
            tenantKey: active.key || active.tenantKey,
            tenantId: active.tenantId,
            clientId: servicesClientId,
            graphScopes: services.graphScopes || services.scopes || active.graphScopes || '',
            senderMailbox: mail.senderAddress || ''
          }
        : null
    };
  } catch {
    return null;
  }
}

function mergedHeaders(input, init) {
  const headers = new Headers(input instanceof Request ? input.headers : undefined);
  new Headers(init?.headers || {}).forEach((value, name) => headers.set(name, value));
  headers.set('Content-Type', 'application/json');
  return headers;
}

function presentStatus(detail) {
  window.dispatchEvent(new CustomEvent('projectpulse:microsoft-connection-runtime-status', { detail }));
  const portal = document.querySelector('.microsoft-integration-portal');
  if (!portal) return;
  let status = document.getElementById(STATUS_ID);
  if (!status) {
    status = document.createElement('div');
    status.id = STATUS_ID;
    status.setAttribute('role', 'status');
    const heading = portal.querySelector('.microsoft-integration-heading');
    if (heading?.nextSibling) portal.insertBefore(status, heading.nextSibling);
    else portal.prepend(status);
  }
  status.className = `microsoft-integration-banner ${detail.runtimeActivated ? 'success' : 'error'}`;
  status.textContent = detail.message;
}

async function applyProfile(delegatedFetch, path, profile, input, init) {
  if (!profile) return { ok: true, skipped: true, payload: {} };
  const response = await delegatedFetch(path, {
    method: 'POST',
    cache: 'no-store',
    headers: mergedHeaders(input, init),
    body: JSON.stringify(profile)
  });
  let payload = {};
  try { payload = await response.json(); } catch { /* sanitized fallback below */ }
  return { ok: response.ok, skipped: false, payload, status: response.status };
}

if (typeof window !== 'undefined' && !window[INSTALL_MARKER]) {
  const delegatedFetch = window.fetch.bind(window);
  window.fetch = async (input, init = {}) => {
    const url = requestUrl(input);
    const method = requestMethod(input, init);
    const profiles = url?.origin === window.location.origin
      && url.pathname === DOCUMENT_PATH
      && method === 'PUT'
      ? activeRuntimeProfiles(requestBody(input, init))
      : null;

    const response = await delegatedFetch(input, init);
    if (!profiles || !response.ok) return response;

    try {
      const servicesResult = await applyProfile(
        delegatedFetch,
        SERVICES_APPLY_PATH,
        profiles.services,
        input,
        init
      );
      const ssoResult = servicesResult.ok
        ? await applyProfile(delegatedFetch, SSO_APPLY_PATH, profiles.sso, input, init)
        : { ok: false, skipped: true, payload: {} };
      const runtimeActivated = servicesResult.ok && ssoResult.ok;
      const failure = !servicesResult.ok ? servicesResult : !ssoResult.ok ? ssoResult : null;
      presentStatus({
        status: failure?.payload?.status || (runtimeActivated ? 'microsoft_runtime_profiles_applied' : 'microsoft_runtime_activation_pending'),
        message: failure?.payload?.message || (runtimeActivated
          ? 'Module 065 connection metadata was saved and applied to the running SSO and Microsoft services paths.'
          : 'Module 065 connection metadata was saved. Runtime activation is still pending.'),
        persistedConfiguration: true,
        runtimeActivated,
        ssoRuntimeActivated: Boolean(ssoResult.ok && !ssoResult.skipped),
        servicesRuntimeActivated: Boolean(servicesResult.ok && !servicesResult.skipped),
        secretValuesReturned: false
      });
    } catch {
      presentStatus({
        status: 'microsoft_runtime_activation_pending',
        message: 'Module 065 connection metadata was saved. Runtime activation could not be confirmed yet.',
        persistedConfiguration: true,
        runtimeActivated: false,
        ssoRuntimeActivated: false,
        servicesRuntimeActivated: false,
        secretValuesReturned: false
      });
    }

    // The authoritative document save remains successful. Runtime activation is
    // reported independently so the saved revision is never lost to the client.
    return response;
  };
  window[INSTALL_MARKER] = true;
}
