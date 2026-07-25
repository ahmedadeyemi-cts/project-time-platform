import { PROJECTPULSE_MODULES, currentProjectPulseRoute, moduleForRoute } from './module-availability-registry.js';

const INSTALL_MARKER = '__projectPulseModuleAvailabilityFetchBridgeInstalled';
const PERMISSION_MARKER = '__projectPulsePermissionNavigationGuardInstalled';
const HIDDEN_ATTRIBUTE = 'data-projectpulse-permission-hidden';

function isSameOriginApiRequest(input) {
  try {
    const raw = typeof input === 'string' ? input : input?.url;
    if (!raw) return null;
    const url = new URL(raw, window.location.origin);
    if (url.origin !== window.location.origin || !url.pathname.startsWith('/api/')) return null;
    return url;
  } catch {
    return null;
  }
}

function sessionToken() {
  try {
    const session = JSON.parse(window.localStorage.getItem('projectPulseAuthSession') || 'null');
    return session?.sessionToken || session?.token || session?.accessToken || '';
  } catch {
    return '';
  }
}

function permissionHeaders() {
  const token = sessionToken();
  return token ? { 'X-ProjectPulse-Session': token, 'Cache-Control': 'no-cache', Pragma: 'no-cache' } : {};
}

function installPermissionNavigationGuard(nativeFetch) {
  if (window[PERMISSION_MARKER]) return;
  window[PERMISSION_MARKER] = true;

  let deniedModuleNumbers = new Set();
  let observer = null;

  function routeOf(element) {
    const declared = element.getAttribute?.('data-route');
    if (declared) return String(declared).replace(/^#/, '').trim();
    const href = element.getAttribute?.('href');
    if (!href) return '';
    try {
      const url = new URL(href, window.location.href);
      return String(url.hash || '').replace(/^#/, '').trim();
    } catch {
      return String(href).replace(/^#/, '').trim();
    }
  }

  function applyVisibility() {
    document.querySelectorAll(`[${HIDDEN_ATTRIBUTE}="true"]`).forEach((element) => {
      element.hidden = false;
      element.removeAttribute(HIDDEN_ATTRIBUTE);
      element.removeAttribute('aria-hidden');
    });

    document.querySelectorAll('a[href], button[data-route], [data-module-number]').forEach((element) => {
      const declaredNumber = String(element.getAttribute?.('data-module-number') || '').trim().toUpperCase();
      const module = declaredNumber
        ? PROJECTPULSE_MODULES.find((item) => item.moduleNumber.toUpperCase() === declaredNumber)
        : moduleForRoute(routeOf(element));
      if (!module || !deniedModuleNumbers.has(module.moduleNumber.toUpperCase())) return;
      element.hidden = true;
      element.setAttribute(HIDDEN_ATTRIBUTE, 'true');
      element.setAttribute('aria-hidden', 'true');
    });

    const currentModule = moduleForRoute(currentProjectPulseRoute());
    if (currentModule && deniedModuleNumbers.has(currentModule.moduleNumber.toUpperCase())) {
      window.location.hash = '#dashboard';
      window.dispatchEvent(new CustomEvent('projectpulse:permission-route-denied', {
        detail: { moduleNumber: currentModule.moduleNumber, route: currentModule.route }
      }));
    }
  }

  async function refreshPermissions() {
    if (!sessionToken()) {
      deniedModuleNumbers = new Set();
      applyVisibility();
      return;
    }

    try {
      const request = { method: 'GET', cache: 'no-store', headers: permissionHeaders() };
      const [summaryResponse, matrixResponse] = await Promise.all([
        nativeFetch('/api/role-policy/summary', request),
        nativeFetch('/api/role-policy/matrix', request)
      ]);
      if (!summaryResponse.ok || !matrixResponse.ok) return;

      const [summary, matrix] = await Promise.all([summaryResponse.json(), matrixResponse.json()]);
      const actorRoles = new Set((summary?.actor?.roleCodes || []).map((value) => String(value).toUpperCase()));
      if (actorRoles.has('SUPER_ADMINISTRATOR')) {
        deniedModuleNumbers = new Set();
      } else {
        deniedModuleNumbers = new Set((matrix?.grants || [])
          .filter((grant) => actorRoles.has(String(grant.roleCode || '').toUpperCase()))
          .filter((grant) => String(grant.actionCode || '').toUpperCase() === 'MODULE_ACCESS')
          .filter((grant) => String(grant.grantEffect || '').toUpperCase() === 'DENY')
          .map((grant) => String(grant.moduleCode || '').toUpperCase()));
      }
      applyVisibility();
    } catch {
      // Preserve existing navigation if permission evidence cannot be loaded.
    }
  }

  const boot = () => {
    applyVisibility();
    void refreshPermissions();
    observer = new MutationObserver(applyVisibility);
    observer.observe(document.body, { childList: true, subtree: true });
  };

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', boot, { once: true });
  else boot();

  window.addEventListener('hashchange', refreshPermissions);
  window.addEventListener('storage', (event) => {
    if (event.key === 'projectPulseAuthSession') void refreshPermissions();
  });
  window.addEventListener('projectpulse:auth-session-ready', refreshPermissions);
  window.addEventListener('projectpulse:permissions-changed', refreshPermissions);
}

if (typeof window !== 'undefined' && !window[INSTALL_MARKER]) {
  const nativeFetch = window.fetch.bind(window);

  window.fetch = async (input, init = {}) => {
    const url = isSameOriginApiRequest(input);
    if (!url || url.pathname.startsWith('/api/module-availability')) {
      return nativeFetch(input, init);
    }

    const module = moduleForRoute(currentProjectPulseRoute());
    if (!module) return nativeFetch(input, init);

    const headers = new Headers(init?.headers || (input instanceof Request ? input.headers : undefined));
    if (!headers.has('X-ProjectPulse-Module-Number')) {
      headers.set('X-ProjectPulse-Module-Number', module.moduleNumber);
    }

    return nativeFetch(input, { ...init, headers });
  };

  window[INSTALL_MARKER] = true;
  installPermissionNavigationGuard(nativeFetch);
}
