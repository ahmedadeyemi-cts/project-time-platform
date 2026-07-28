import { authoritativeApi } from './projectpulse-authoritative-api.js';

const INSTALL_MARKER = '__projectPulseRolePolicyAuthoritativeTransportInstalled';
const RESPONSE_MARKER = 'projectpulse-role-policy-authoritative-v3';
const SESSION_WAIT_MS = 5000;

const ROUTES = Object.freeze({
  summary: {
    moduleNumber: '012',
    requiredCollections: ['roles', 'modules'],
    paths: ['/api/runtime/v2/role-policy/summary', '/api/role-policy/summary', '/api/runtime/role-policy/summary']
  },
  catalog: {
    moduleNumber: '012',
    requiredCollections: ['actions', 'scopes'],
    paths: ['/api/runtime/v2/role-policy/catalog', '/api/role-policy/catalog', '/api/runtime/role-policy/catalog']
  },
  versions: {
    moduleNumber: '012',
    requiredCollections: ['versions'],
    paths: ['/api/runtime/v2/role-policy/versions', '/api/role-policy/versions', '/api/runtime/role-policy/versions']
  },
  matrix: {
    moduleNumber: '037',
    requiredCollections: ['roles', 'modules', 'grants'],
    paths: ['/api/runtime/v2/role-policy/matrix', '/api/role-policy/matrix', '/api/runtime/role-policy/matrix']
  }
});

function requestMethod(input, init = {}) {
  return String(init?.method || (input instanceof Request ? input.method : '') || 'GET').toUpperCase();
}

function routeContract(pathname) {
  for (const [name, contract] of Object.entries(ROUTES)) {
    if (new RegExp(`^/api/(?:runtime/(?:v2/)?)?role-policy/${name}$`, 'i').test(pathname)) {
      return contract;
    }
  }

  const roleMatch = pathname.match(/^\/api\/(?:runtime\/(?:v2\/)?)?role-policy\/roles\/([^/]+)$/i);
  if (!roleMatch) return null;

  const encodedRole = roleMatch[1];
  return {
    moduleNumber: '012',
    requiredCollections: ['assignedUsers', 'grants'],
    paths: [
      `/api/runtime/v2/role-policy/roles/${encodedRole}`,
      `/api/role-policy/roles/${encodedRole}`,
      `/api/runtime/role-policy/roles/${encodedRole}`
    ]
  };
}

function jsonResponse(payload, status = 200, authoritativePath = '') {
  return new Response(JSON.stringify(payload ?? {}), {
    status,
    headers: {
      'Content-Type': 'application/json; charset=utf-8',
      'Cache-Control': 'no-store',
      'X-ProjectPulse-Role-Policy-Transport': RESPONSE_MARKER,
      ...(authoritativePath ? { 'X-ProjectPulse-Authoritative-Path': authoritativePath } : {})
    }
  });
}

function validStatus(value, fallback = 502) {
  const status = Number(value);
  return status >= 400 && status <= 599 ? status : fallback;
}

function installRolePolicyAuthoritativeTransport() {
  if (typeof window === 'undefined' || typeof window.fetch !== 'function' || window[INSTALL_MARKER]) return;

  const previousFetch = window.fetch.bind(window);
  window.fetch = async (input, init = {}) => {
    if (requestMethod(input, init) !== 'GET') return previousFetch(input, init);

    let url;
    try {
      url = new URL(input instanceof Request ? input.url : String(input), window.location.origin);
    } catch {
      return previousFetch(input, init);
    }

    if (url.origin !== window.location.origin) return previousFetch(input, init);
    const contract = routeContract(url.pathname);
    if (!contract) return previousFetch(input, init);

    const attempts = [];
    for (const candidatePath of contract.paths) {
      const candidateUrl = new URL(candidatePath, window.location.origin);
      candidateUrl.search = url.search;
      const authoritativePath = `${candidateUrl.pathname}${candidateUrl.search}`;

      try {
        const payload = await authoritativeApi(authoritativePath, {
          method: 'GET',
          moduleNumber: contract.moduleNumber,
          requiredCollections: contract.requiredCollections,
          sessionWaitMs: SESSION_WAIT_MS,
          nativeFallback: true,
          headers: {
            'X-ProjectPulse-Role-Policy-Client': RESPONSE_MARKER
          }
        });

        return jsonResponse(payload, 200, authoritativePath);
      } catch (error) {
        const status = validStatus(error?.status, 502);
        attempts.push({
          path: candidateUrl.pathname,
          status,
          code: error?.code || error?.payload?.status || '',
          responseKeys: error?.diagnostic?.responseKeys || Object.keys(error?.payload || {})
        });

        if ([401, 403, 425].includes(status)) {
          return jsonResponse({
            module: contract.moduleNumber,
            status: error?.payload?.status || error?.code || 'role_policy_access_failed',
            message: error?.message || 'The role-policy request could not be authorized.',
            requestedPath: url.pathname,
            authoritativePath,
            requiredCollections: contract.requiredCollections,
            diagnostic: error?.diagnostic || null
          }, status, authoritativePath);
        }
      }
    }

    const last = attempts.at(-1) || {};
    return jsonResponse({
      module: contract.moduleNumber,
      status: 'role_policy_authoritative_transport_failed',
      message: `Role-policy data did not contain required collections: ${contract.requiredCollections.join(', ')}.`,
      requestedPath: url.pathname,
      requiredCollections: contract.requiredCollections,
      attempts
    }, validStatus(last.status, 502), last.path || '');
  };

  window[INSTALL_MARKER] = true;
}

installRolePolicyAuthoritativeTransport();
