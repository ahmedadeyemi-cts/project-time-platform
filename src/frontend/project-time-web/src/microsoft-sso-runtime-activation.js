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
  return typeof init?.body === 'string' ? init.body : null;
}

function runtimeEnvironmentMode() {
  const host = window.location.hostname.toLowerCase();
  if (host.includes('-test.') || host.endsWith('.onenecklab.com') || host === 'localhost' || host === '127.0.0.1') return 'test';
  return 'production';
}

function activeRuntimeProfiles(bodyText) {
  try {
    const request = JSON.parse(bodyText || '{}');
    const notes = request?.document?.configuration?.notes;
    if (typeof notes !== 'string' || !notes.startsWith(CONFIG_MARKER)) return null;
    const configuration = JSON.parse(notes.slice(CONFIG_MARKER.length));
    const tenants = Array.isArray(configuration?.tenants) ? configuration.tenants : [];
    const selected = tenants.find((tenant) => tenant?.key === configuration?.activeTenantKey)
      || tenants.find((tenant) => tenant?.environmentMode === configuration?.activeEnvironmentMode)
      || null;
    if (!selected) return null;

    const sso = selected.sso || selected.ssoConnection || {};
    const services = selected.services || selected.servicesConnection || {};
    const selectedMail = selected.mail || configuration.mail || {};
    const ssoClientId = sso.clientId || sso.applicationId || selected.ssoClientId || '';
    const servicesClientId = services.clientId || services.applicationId || selected.serviceClientId || selected.clientId || '';

    return {
      environmentMode: String(selected.environmentMode || '').toLowerCase(),
      sso: selected.tenantId && ssoClientId && (sso.redirectUri || selected.redirectUri)
        ? {
            environmentMode: selected.environmentMode,
            tenantId: selected.tenantId,
            clientId: ssoClientId,
            redirectUri: sso.redirectUri || selected.redirectUri,
            allowedDomains: sso.allowedDomains || selected.ssoAllowedDomains || selected.tenantDomain || ''
          }
        : null,
      services: selected.tenantId && servicesClientId
        ? {
            environmentMode: selected.environmentMode,
            tenantKey: selected.key || selected.tenantKey,
            tenantId: selected.tenantId,
            clientId: servicesClientId,
            graphScopes: services.graphScopes || services.scopes || selected.graphScopes || '',
            senderMailbox: selectedMail.senderAddress || ''
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
  status.className = `microsoft-integration-banner ${detail.runtimeActivated ? 'success' : detail.persistedConfiguration ? '' : 'error'}`;
  status.textContent = detail.message;
}

async function applyProfile(delegatedFetch, path, profile, input, init) {
  if (!profile) return { ok: true, skipped: true, activated: true, payload: {} };
  const response = await delegatedFetch(path, {
    method: 'POST',
    cache: 'no-store',
    headers: mergedHeaders(input, init),
    body: JSON.stringify(profile)
  });
  let payload = {};
  try { payload = await response.json(); } catch { /* sanitized fallback below */ }
  return {
    ok: response.ok,
    skipped: false,
    activated: response.ok && payload?.runtimeActivated !== false,
    payload,
    status: response.status
  };
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
      const servicesResult = await applyProfile(delegatedFetch, SERVICES_APPLY_PATH, profiles.services, input, init);
      const ssoResult = servicesResult.ok
        ? await applyProfile(delegatedFetch, SSO_APPLY_PATH, profiles.sso, input, init)
        : { ok: false, skipped: true, activated: false, payload: {} };
      const requestSucceeded = servicesResult.ok && ssoResult.ok;
      const runtimeActivated = requestSucceeded && servicesResult.activated && ssoResult.activated;
      const failure = !servicesResult.ok ? servicesResult : !ssoResult.ok ? ssoResult : null;
      const selectedEnvironment = profiles.environmentMode || 'unknown';
      const runningEnvironment = runtimeEnvironmentMode();
      const savedForOtherEnvironment = requestSucceeded && !runtimeActivated && selectedEnvironment !== runningEnvironment;

      presentStatus({
        status: failure?.payload?.status
          || (runtimeActivated
            ? 'microsoft_runtime_profiles_applied'
            : savedForOtherEnvironment
              ? 'microsoft_profiles_saved_for_other_environment'
              : 'microsoft_runtime_activation_pending'),
        message: failure?.payload?.message
          || (runtimeActivated
            ? `Module 065 ${selectedEnvironment === 'production' ? 'Production' : 'Test'} connection metadata was saved and applied to the running SSO and Microsoft services paths.`
            : savedForOtherEnvironment
              ? `Module 065 ${selectedEnvironment === 'production' ? 'Production' : 'Test'} connection metadata was saved. It will activate only in its matching environment.`
              : 'Module 065 connection metadata was saved. Runtime activation is still pending.'),
        persistedConfiguration: true,
        selectedEnvironment,
        runningEnvironment,
        runtimeActivated,
        ssoRuntimeActivated: Boolean(ssoResult.activated && !ssoResult.skipped),
        servicesRuntimeActivated: Boolean(servicesResult.activated && !servicesResult.skipped),
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
