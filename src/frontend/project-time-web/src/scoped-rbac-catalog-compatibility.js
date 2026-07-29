const SCOPED_RBAC_CATALOG_PATH = '/api/role-policy/catalog';
const SCOPED_RBAC_CATALOG_MARKER = 'projectpulse-scoped-rbac-catalog-normalized';
const MODULE_NAME_OVERRIDES = Object.freeze({
  '006': 'Project Register'
});
const MODULE_ROUTE_OVERRIDES = Object.freeze({
  '006': 'project-register'
});

function asArray(value) {
  return Array.isArray(value) ? value : [];
}

function normalizeModule(module = {}) {
  const moduleCode = String(module.moduleCode ?? module.ModuleCode ?? '').trim().toUpperCase();
  const nameOverride = MODULE_NAME_OVERRIDES[moduleCode];
  const routeOverride = MODULE_ROUTE_OVERRIDES[moduleCode];
  if (!nameOverride && !routeOverride) return module;

  return {
    ...module,
    ...(nameOverride && Object.prototype.hasOwnProperty.call(module, 'ModuleName') ? { ModuleName: nameOverride } : {}),
    ...(nameOverride ? { moduleName: nameOverride, displayName: nameOverride } : {}),
    ...(routeOverride && Object.prototype.hasOwnProperty.call(module, 'Route') ? { Route: routeOverride } : {}),
    ...(routeOverride && Object.prototype.hasOwnProperty.call(module, 'RouteKey') ? { RouteKey: routeOverride } : {}),
    ...(routeOverride ? {
      route: routeOverride,
      routeKey: routeOverride,
      href: `#${routeOverride}`
    } : {})
  };
}

function normalizeCatalog(payload) {
  const source = payload && typeof payload === 'object' ? payload : {};
  return {
    ...source,
    actions: asArray(source.actions ?? source.Actions),
    scopes: asArray(source.scopes ?? source.Scopes),
    effects: asArray(source.effects ?? source.Effects).length
      ? asArray(source.effects ?? source.Effects)
      : ['GRANT', 'DENY'],
    policyStatuses: asArray(source.policyStatuses ?? source.PolicyStatuses),
    compatibilityMarker: SCOPED_RBAC_CATALOG_MARKER
  };
}

function normalizeRolePolicyPayload(payload, pathname) {
  const source = payload && typeof payload === 'object' ? payload : {};
  if (pathname === SCOPED_RBAC_CATALOG_PATH) return normalizeCatalog(source);

  const modules = asArray(source.modules ?? source.Modules).map(normalizeModule);
  const legacyFallback = asArray(source.legacyFallback ?? source.LegacyFallback).map((entry) => {
    const moduleCode = String(entry?.moduleCode ?? entry?.ModuleCode ?? '').trim().toUpperCase();
    const nameOverride = MODULE_NAME_OVERRIDES[moduleCode];
    const routeOverride = MODULE_ROUTE_OVERRIDES[moduleCode];
    if (!nameOverride && !routeOverride) return entry;
    return {
      ...entry,
      ...(nameOverride ? { moduleName: nameOverride, ModuleName: nameOverride, displayName: nameOverride } : {}),
      ...(routeOverride ? { route: routeOverride, Route: routeOverride, routeKey: routeOverride, href: `#${routeOverride}` } : {})
    };
  });

  return {
    ...source,
    ...(modules.length || Object.prototype.hasOwnProperty.call(source, 'modules') ? { modules } : {}),
    ...(Object.prototype.hasOwnProperty.call(source, 'Modules') ? { Modules: modules } : {}),
    ...(legacyFallback.length || Object.prototype.hasOwnProperty.call(source, 'legacyFallback') ? { legacyFallback } : {}),
    compatibilityMarker: SCOPED_RBAC_CATALOG_MARKER,
    moduleDisplayNameOverrides: MODULE_NAME_OVERRIDES,
    moduleRouteOverrides: MODULE_ROUTE_OVERRIDES
  };
}

function requestPath(input, init) {
  const method = String(init?.method || (input instanceof Request ? input.method : 'GET')).toUpperCase();
  if (method !== 'GET') return '';
  try {
    const raw = input instanceof Request ? input.url : String(input);
    const url = new URL(raw, window.location.origin);
    if (url.origin !== window.location.origin) return '';
    const supported = new Set([
      SCOPED_RBAC_CATALOG_PATH,
      '/api/role-policy/summary',
      '/api/role-policy/matrix',
      '/api/runtime/role-policy/summary',
      '/api/runtime/role-policy/matrix',
      '/api/runtime/v2/role-policy/summary',
      '/api/runtime/v2/role-policy/matrix',
      '/api/rbac/v1/bootstrap',
      '/api/rbac/v1/matrix',
      '/api/rbac/v1/modules'
    ]);
    return supported.has(url.pathname) ? url.pathname : '';
  } catch {
    return '';
  }
}

if (typeof window !== 'undefined' && typeof window.fetch === 'function') {
  const previousFetch = window.fetch.bind(window);

  window.fetch = async function projectPulseScopedRbacCatalogFetch(input, init) {
    const pathname = requestPath(input, init);
    const response = await previousFetch(input, init);
    if (!pathname || !response.ok) return response;

    try {
      const payload = await response.clone().json();
      const normalized = normalizeRolePolicyPayload(payload, pathname);
      const responseHeaders = new Headers(response.headers);
      responseHeaders.delete('content-length');
      responseHeaders.delete('content-encoding');
      responseHeaders.set('content-type', 'application/json; charset=utf-8');
      responseHeaders.set('x-projectpulse-compatibility', SCOPED_RBAC_CATALOG_MARKER);

      return new Response(JSON.stringify(normalized), {
        status: response.status,
        statusText: response.statusText,
        headers: responseHeaders
      });
    } catch {
      return response;
    }
  };
}

export {
  MODULE_NAME_OVERRIDES,
  MODULE_ROUTE_OVERRIDES,
  normalizeCatalog,
  normalizeModule,
  normalizeRolePolicyPayload,
  SCOPED_RBAC_CATALOG_MARKER
};
