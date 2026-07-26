const INSTALL_MARKER = '__projectPulseMicrosoftSsoRuntimeActivationInstalled';
const CONFIG_MARKER = 'PROJECTPULSE_MICROSOFT_INTEGRATION_JSON:';
const DOCUMENT_PATH = '/api/native-administration/065/document';
const APPLY_PATH = '/api/microsoft-integration/sso-apply-profile';

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

function activeSsoProfile(bodyText) {
  try {
    const request = JSON.parse(bodyText || '{}');
    const notes = request?.document?.configuration?.notes;
    if (typeof notes !== 'string' || !notes.startsWith(CONFIG_MARKER)) return null;
    const configuration = JSON.parse(notes.slice(CONFIG_MARKER.length));
    const tenants = Array.isArray(configuration?.tenants) ? configuration.tenants : [];
    const active = tenants.find((tenant) => tenant?.key === configuration?.activeTenantKey)
      || tenants.find((tenant) => tenant?.environmentMode === configuration?.activeEnvironmentMode)
      || null;
    const sso = active?.sso || active?.ssoConnection || {};
    const clientId = sso.clientId || sso.applicationId || active?.ssoClientId || '';
    if (!active || !active.tenantId || !clientId || !(sso.redirectUri || active.redirectUri)) return null;
    return {
      environmentMode: active.environmentMode,
      tenantId: active.tenantId,
      clientId,
      redirectUri: sso.redirectUri || active.redirectUri,
      allowedDomains: sso.allowedDomains || active.ssoAllowedDomains || active.tenantDomain || ''
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

function activationFailure(response, payload) {
  const message = payload?.message || payload?.status || `SSO runtime activation failed with HTTP ${response.status}.`;
  return new Response(JSON.stringify({
    status: 'sso_runtime_activation_failed',
    message,
    persistedConfiguration: true,
    runtimeActivated: false
  }), {
    status: response.status >= 400 ? response.status : 502,
    headers: { 'Content-Type': 'application/json' }
  });
}

if (typeof window !== 'undefined' && !window[INSTALL_MARKER]) {
  const delegatedFetch = window.fetch.bind(window);
  window.fetch = async (input, init = {}) => {
    const url = requestUrl(input);
    const method = requestMethod(input, init);
    const profile = url?.origin === window.location.origin
      && url.pathname === DOCUMENT_PATH
      && method === 'PUT'
      ? activeSsoProfile(requestBody(input, init))
      : null;

    const response = await delegatedFetch(input, init);
    if (!profile || !response.ok) return response;

    const activationResponse = await delegatedFetch(APPLY_PATH, {
      method: 'POST',
      cache: 'no-store',
      headers: mergedHeaders(input, init),
      body: JSON.stringify(profile)
    });
    if (activationResponse.ok) return response;

    let payload = {};
    try { payload = await activationResponse.json(); } catch { /* sanitized fallback below */ }
    return activationFailure(activationResponse, payload);
  };
  window[INSTALL_MARKER] = true;
}
