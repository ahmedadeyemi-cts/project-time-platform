export const EFFECTIVE_ROLE_AUTHORITY_EVENTS = Object.freeze([
  'projectpulse:effective-navigation-state',
  'projectpulse:effective-navigation-changed',
  'projectpulse:authorized-workspace-navigation-changed',
  'projectpulse:view-as-changed',
  'projectpulse:auth-session-changed',
  'storage',
  'pageshow',
  'focus',
  'hashchange'
]);

function readJsonStorage(key) {
  if (typeof window === 'undefined') return null;

  try {
    const raw = window.localStorage.getItem(key);
    return raw ? JSON.parse(raw) : null;
  } catch {
    return null;
  }
}

export function normalizeProjectPulseRoleCodes(value) {
  const roles = [];

  function visit(candidate) {
    if (candidate === null || candidate === undefined) return;

    if (Array.isArray(candidate)) {
      candidate.forEach(visit);
      return;
    }

    if (typeof candidate === 'object') {
      visit(candidate.roleCodes);
      visit(candidate.roles);
      visit(candidate.roleCode);
      visit(candidate.roleNames);
      visit(candidate.effectiveRoleCodes);
      return;
    }

    String(candidate)
      .split(',')
      .map((role) => role.trim().toUpperCase())
      .filter(Boolean)
      .forEach((role) => roles.push(role));
  }

  visit(value);
  return [...new Set(roles)];
}

export function readEffectiveRoleAuthority() {
  if (typeof window === 'undefined') {
    return { ready: false, roleCodes: [], source: 'server', viewAsActive: false };
  }

  const viewAs = readJsonStorage('projectPulseViewAsUser');
  if (viewAs?.userId) {
    const roleCodes = normalizeProjectPulseRoleCodes(viewAs);
    return {
      ready: roleCodes.length > 0,
      roleCodes,
      source: 'view_as',
      viewAsActive: true
    };
  }

  const navigation = window.__projectPulseEffectiveNavigation;
  if (navigation?.state === 'ready') {
    return {
      ready: true,
      roleCodes: normalizeProjectPulseRoleCodes(navigation),
      source: 'effective_navigation',
      viewAsActive: false
    };
  }

  const session = readJsonStorage('projectPulseAuthSession');
  const roleCodes = normalizeProjectPulseRoleCodes(session);
  return {
    ready: roleCodes.length > 0,
    roleCodes,
    source: 'session',
    viewAsActive: false
  };
}

export function hasAnyEffectiveRole(authority, allowedRoles) {
  if (!authority?.ready) return false;

  const allowed = allowedRoles instanceof Set
    ? allowedRoles
    : new Set((allowedRoles || []).map((role) => String(role).trim().toUpperCase()));

  return (authority.roleCodes || []).some((role) => allowed.has(String(role).trim().toUpperCase()));
}
