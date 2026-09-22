// Presentation-only projection of the existing server-backed navigation authority.
// Nothing in this module grants a role, a module action, or access to records.
export function createJourneyIdentityGuard() {
  let initialized = false;
  let previousIdentity;
  let previousNavigation;
  let blocked = false;
  let revision = 0;
  return {
    observe(navigation, identity) {
      if (initialized && identity !== previousIdentity) {
        revision += 1;
        blocked = navigation === previousNavigation;
      } else if (blocked && navigation !== previousNavigation) {
        blocked = false;
      }
      initialized = true;
      previousIdentity = identity;
      previousNavigation = navigation;
      // Identity is deliberately retained only in this closure, never returned.
      return { current: !blocked, revision };
    }
  };
}

export function resolveJourneyAudience({ navigation, viewAsActive = false, allowedModules = [], navigationCurrent = true, revision = 0 } = {}) {
  const empty = (state) => ({ roleCodes: [], viewAsActive: Boolean(viewAsActive), accessReady: false, modules: [], scopeRevision: revision, state });
  if (!navigationCurrent) return empty('verifying');
  if (navigation?.state !== 'ready') return empty(navigation?.state === 'anonymous' ? 'anonymous' : 'verifying');
  if (navigation.evidenceContract !== 'projectpulse-rbac-v1' || navigation.refreshFailed === true) return empty('unavailable');
  if (Boolean(navigation.isViewAs) !== Boolean(viewAsActive)) return empty('verifying');
  // An explicit empty journey assignment must never fall back to another role.
  const source = Object.hasOwn(navigation, 'journeyRoleCodes') ? navigation.journeyRoleCodes : navigation.roleCodes;
  if (!Array.isArray(source)) return empty('unavailable');
  const roleCodes = [...new Set(source.filter((code) => typeof code === 'string')
    .map((code) => code.trim().toUpperCase().replace(/[\s-]+/g, '_'))
    .filter((code) => /^[A-Z][A-Z0-9_]{0,127}$/.test(code)))];
  if (!Array.isArray(allowedModules)) return empty('unavailable');
  const denied = new Set([...(navigation.deniedModuleNumbers || []), ...(navigation.retiredModuleNumbers || [])].map((code) => String(code).toUpperCase()));
  const seen = new Set();
  const modules = allowedModules.filter((module) => {
    if (!module || !/^[a-z0-9-]+$/.test(module.route || '') || seen.has(module.route)) return false;
    if (denied.has(String(module.moduleNumber || '').toUpperCase())) return false;
    seen.add(module.route);
    return true;
  }).map(({ route, displayName, moduleNumber, group, description }) => ({
    route, displayName, moduleNumber, group: group || 'Other workspaces', description: description || ''
  }));
  return { roleCodes, viewAsActive: Boolean(viewAsActive), accessReady: true, modules, scopeRevision: revision, state: 'ready' };
}
