const SCOPED_RBAC_CATALOG_PATH = '/api/role-policy/catalog';
const SCOPED_RBAC_CATALOG_MARKER = 'projectpulse-scoped-rbac-catalog-normalized';

function asArray(value) {
  return Array.isArray(value) ? value : [];
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

function isCatalogRequest(input, init) {
  const method = String(init?.method || (input instanceof Request ? input.method : 'GET')).toUpperCase();
  if (method !== 'GET') return false;

  try {
    const raw = input instanceof Request ? input.url : String(input);
    const url = new URL(raw, window.location.origin);
    return url.origin === window.location.origin && url.pathname === SCOPED_RBAC_CATALOG_PATH;
  } catch {
    return false;
  }
}

if (typeof window !== 'undefined' && typeof window.fetch === 'function') {
  const previousFetch = window.fetch.bind(window);

  window.fetch = async function projectPulseScopedRbacCatalogFetch(input, init) {
    const response = await previousFetch(input, init);
    if (!isCatalogRequest(input, init) || !response.ok) return response;

    try {
      const payload = await response.clone().json();
      const normalized = normalizeCatalog(payload);
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

export { normalizeCatalog, SCOPED_RBAC_CATALOG_MARKER };
