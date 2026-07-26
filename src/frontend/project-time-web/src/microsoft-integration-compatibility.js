const ACTIVE_ROUTE = 'entra-secret-administration';
const RETIRED_ROUTE = 'global-mail-configuration';
const LEGACY_IMPORT_ROUTE = '/api/admin/azure/users/import-selected';
const ACTIVE_IMPORT_ROUTE = '/api/microsoft-integration/directory-users/import-selected';
const ROLE_NORMALIZATION_ROUTES = new Set([
  LEGACY_IMPORT_ROUTE,
  ACTIVE_IMPORT_ROUTE,
  '/api/admin/azure/config',
  '/api/admin/azure/import-settings'
]);

function currentRoute() {
  return window.location.hash.replace(/^#/, '').split('?')[0].trim();
}

function redirectRetiredRoute() {
  if (currentRoute() === RETIRED_ROUTE) {
    window.location.replace(`#${ACTIVE_ROUTE}`);
  }
}

function closestModuleSurface(element) {
  return element.closest('a, button, li, article, section, .module-card, .workspace-card, .navigation-item, .sidebar-item');
}

function setTextIfChanged(element, value) {
  if (!element || element.textContent === value) return;
  element.textContent = value;
}

function normalizeModuleSurfaces() {
  document.querySelectorAll(`a[href="#${RETIRED_ROUTE}"], [data-route="${RETIRED_ROUTE}"]`).forEach((element) => {
    const surface = closestModuleSurface(element) || element;
    surface.setAttribute('data-module-067-retired', 'true');
    surface.hidden = true;
  });

  document.querySelectorAll('a, button, h1, h2, h3, h4, p, span, strong, div').forEach((element) => {
    const text = element.textContent?.trim() || '';
    if (!text || element.children.length > 4) return;

    if (/\bMODULE\s*067\b/i.test(text) || /^Global Mail Configuration(?: Center)?$/i.test(text)) {
      const surface = closestModuleSurface(element);
      if (surface && !surface.querySelector(`a[href="#${ACTIVE_ROUTE}"]`)) {
        surface.setAttribute('data-module-067-retired', 'true');
        surface.hidden = true;
      }
    }

    if (/^Entra Secret Administration(?: Metadata)?$/i.test(text)) {
      setTextIfChanged(element, 'Microsoft Integration');
    }
  });

  const module010 = document.querySelector('#azure-admin');
  if (module010) {
    module010.querySelectorAll('.azure-config-card, .azure-sync-summary-card').forEach((element) => {
      element.setAttribute('data-moved-to-module-065', 'true');
      element.hidden = true;
    });

    module010.querySelectorAll('button').forEach((button) => {
      const label = button.textContent?.trim().toLowerCase();
      if (label === 'sync now' || label === 'reconcile inactive users') {
        button.hidden = true;
      }
    });

    const eyebrow = module010.querySelector('.section-heading .eyebrow');
    const heading = module010.querySelector('.section-heading h1');
    const copy = module010.querySelector('.section-heading .section-copy');
    setTextIfChanged(eyebrow, 'Azure / Entra Directory Users');
    setTextIfChanged(heading, 'Preview and import Entra users');
    setTextIfChanged(copy, 'Preview Entra directory users, filter the list, select the people to import, and confirm that imported users appear in ProjectPulse. Tenant, secret, synchronization, identity, and Microsoft 365 mail settings are managed in Module 065 Microsoft Integration.');
  }

  document.body.classList.toggle('projectpulse-microsoft-integration-active', currentRoute() === ACTIVE_ROUTE);
}

function canonicalRoleCode(value) {
  const normalized = String(value || '').trim().toUpperCase();
  if (!normalized || normalized === 'ENGINEERING') return 'ENGINEER';
  return normalized;
}

function normalizeRolePayload(pathname, init) {
  if (!ROLE_NORMALIZATION_ROUTES.has(pathname) || typeof init?.body !== 'string') return init;

  try {
    const payload = JSON.parse(init.body);
    payload.defaultRoleCode = canonicalRoleCode(payload.defaultRoleCode);
    return { ...init, body: JSON.stringify(payload) };
  } catch {
    return init;
  }
}

function installMicrosoftIntegrationCompatibility() {
  if (window.__projectPulseMicrosoftIntegrationCompatibilityInstalled) return;
  window.__projectPulseMicrosoftIntegrationCompatibilityInstalled = true;

  const previousFetch = window.fetch.bind(window);
  window.fetch = async (input, init = {}) => {
    const rawUrl = typeof input === 'string' ? input : input?.url;
    const method = String(init?.method || (input instanceof Request ? input.method : '') || 'GET').toUpperCase();

    if (!rawUrl || !['POST', 'PUT', 'PATCH'].includes(method)) return previousFetch(input, init);

    const url = new URL(rawUrl, window.location.origin);
    const normalizedInit = normalizeRolePayload(url.pathname, init);

    if (url.pathname !== LEGACY_IMPORT_ROUTE) {
      return previousFetch(input, normalizedInit);
    }

    const replacement = new URL(ACTIVE_IMPORT_ROUTE, window.location.origin);
    const nextInput = input instanceof Request
      ? new Request(replacement.toString(), input)
      : replacement.pathname;

    return previousFetch(nextInput, normalizedInit);
  };
}

function refresh() {
  redirectRetiredRoute();
  normalizeModuleSurfaces();
}

installMicrosoftIntegrationCompatibility();
refresh();

window.addEventListener('hashchange', refresh);
window.addEventListener('projectpulse:auth-session-ready', refresh);

const observer = new MutationObserver(() => {
  window.clearTimeout(window.__projectPulseMicrosoftIntegrationRefreshTimer);
  window.__projectPulseMicrosoftIntegrationRefreshTimer = window.setTimeout(refresh, 25);
});
observer.observe(document.documentElement, { childList: true, subtree: true });
