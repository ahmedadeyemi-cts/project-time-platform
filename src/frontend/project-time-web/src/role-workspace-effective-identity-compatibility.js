const INSTALL_MARKER = '__projectPulseEffectiveIdentityCompatibilityInstalled';
const AUTH_SESSION_KEY = 'projectPulseAuthSession';

function hasActiveSession() {
  try {
    const session = JSON.parse(window.localStorage.getItem(AUTH_SESSION_KEY) || 'null');
    const token = session?.sessionToken || session?.token || session?.accessToken || '';
    if (!token) return false;
    if (session?.expiresAt && Date.now() >= Date.parse(session.expiresAt)) return false;
    return true;
  } catch {
    return false;
  }
}

function requestDescriptor(input, init = {}) {
  try {
    const rawUrl = typeof input === 'string' ? input : input?.url;
    const url = new URL(rawUrl || '', window.location.origin);
    const method = String(init?.method || (typeof input !== 'string' ? input?.method : '') || 'GET').toUpperCase();
    return { url, method };
  } catch {
    return null;
  }
}

function notApplicableUtilizationResponse() {
  return new Response(JSON.stringify({
    status: 'not_applicable_for_effective_role',
    applicable: false,
    quarter: null,
    targetPercent: 0,
    targetHours: 0,
    currentUtilizationPercent: 0,
    currentBillableHours: 0,
    hoursLeftToTarget: 0,
    standardPeriodHours: 0,
    message: 'Utilization is not part of the current effective role. Identity and authorized modules remain available.'
  }), {
    status: 200,
    headers: {
      'Content-Type': 'application/json',
      'Cache-Control': 'no-store',
      'X-ProjectPulse-Compatibility': 'effective-identity-load-isolation-v1'
    }
  });
}

function installEffectiveIdentityCompatibility() {
  if (typeof window === 'undefined' || window[INSTALL_MARKER]) return;
  window[INSTALL_MARKER] = true;

  const nativeFetch = window.fetch.bind(window);
  window.fetch = async (input, init = {}) => {
    const descriptor = requestDescriptor(input, init);
    const response = await nativeFetch(input, init);

    if (!descriptor
        || descriptor.method !== 'GET'
        || descriptor.url.origin !== window.location.origin
        || descriptor.url.pathname !== '/api/utilization/current-quarter'
        || response.status !== 403
        || !hasActiveSession()) {
      return response;
    }

    // A role-inapplicable dashboard panel must not reject the independent
    // /api/security/me request that establishes the selected View-As identity.
    // This compatibility response grants no utilization data or permission.
    return notApplicableUtilizationResponse();
  };

  window.__projectPulseEffectiveIdentityCompatibility = Object.freeze({
    contractVersion: 'effective-identity-load-isolation-v1',
    utilizationDenialBehavior: 'not_applicable_without_identity_loss',
    grantsUtilizationAccess: false,
    viewAsMutationAuthority: false
  });
}

installEffectiveIdentityCompatibility();
