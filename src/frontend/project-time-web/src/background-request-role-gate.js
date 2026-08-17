import {
  hasAnyEffectiveRole,
  readEffectiveRoleAuthority
} from './effective-role-authority.js';

const OWNER_MANAGEMENT_ROLES = new Set([
  'SUPER_ADMINISTRATOR'
]);

const OPERATIONS_ACKNOWLEDGMENT_ROLES = new Set([
  'SUPER_ADMINISTRATOR',
  'ADMINISTRATOR',
  'PROJECT_TEAM_COORDINATOR'
]);

function sameOriginApiUrl(input) {
  try {
    const raw = typeof input === 'string' ? input : input?.url;
    if (!raw) return null;
    const url = new URL(raw, window.location.origin);
    return url.origin === window.location.origin && url.pathname.startsWith('/api/') ? url : null;
  } catch {
    return null;
  }
}

function requestMethod(input, init) {
  return String(init?.method || (input instanceof Request ? input.method : 'GET') || 'GET').toUpperCase();
}

function jsonResponse(payload) {
  return new Response(JSON.stringify(payload), {
    status: 200,
    headers: {
      'Content-Type': 'application/json',
      'Cache-Control': 'no-store',
      'X-ProjectPulse-Background-Request': 'role-not-applicable'
    }
  });
}

function installBackgroundRequestRoleGate() {
  if (typeof window === 'undefined' || window.__projectPulseBackgroundRequestRoleGateInstalled) return;

  const downstreamFetch = window.fetch.bind(window);

  window.fetch = async (input, init = {}) => {
    const url = sameOriginApiUrl(input);
    if (!url || requestMethod(input, init) !== 'GET') return downstreamFetch(input, init);

    const authority = readEffectiveRoleAuthority();

    if (url.pathname === '/api/module-catalog/owners'
        && !hasAnyEffectiveRole(authority, OWNER_MANAGEMENT_ROLES)) {
      return jsonResponse({
        status: authority.ready ? 'ownership_not_applicable' : 'authorization_pending',
        owners: [],
        ownerCandidates: [],
        access: {
          canManage: false,
          isViewAs: authority.viewAsActive === true
        },
        message: 'Module ownership administration is not required for this effective role.'
      });
    }

    if (url.pathname === '/api/production/operations-acknowledgments/summary'
        && !hasAnyEffectiveRole(authority, OPERATIONS_ACKNOWLEDGMENT_ROLES)) {
      return jsonResponse({
        status: authority.ready ? 'acknowledgments_not_applicable' : 'authorization_pending',
        acknowledgments: [],
        summary: {
          total: 0,
          acknowledged: 0,
          pending: 0
        },
        access: {
          canAcknowledge: false,
          isViewAs: authority.viewAsActive === true
        },
        message: 'Production operations acknowledgments are not applicable to this effective role.'
      });
    }

    return downstreamFetch(input, init);
  };

  window.__projectPulseBackgroundRequestRoleGateInstalled = true;
}

installBackgroundRequestRoleGate();
