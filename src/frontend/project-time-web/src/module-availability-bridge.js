import {
  PROJECTPULSE_MODULES,
  RETIRED_PROJECTPULSE_MODULES,
  currentProjectPulseRoute,
  moduleForNumber,
  moduleForRoute,
  rawModuleRoute,
  retiredModuleForRoute
} from './module-availability-registry.js';
import './permission-aware-more-menu.css';

const INSTALL_MARKER = '__projectPulseModuleAvailabilityFetchBridgeInstalled';
const PERMISSION_MARKER = '__projectPulsePermissionNavigationGuardInstalled';
const HIDDEN_ATTRIBUTE = 'data-projectpulse-permission-hidden';
const MORE_SEARCH_HIDDEN_ATTRIBUTE = 'data-projectpulse-more-search-hidden';
const RETIRED_ROUTE_NOTICE_KEY = 'projectPulseRetiredWorkTaskBuilderNotice';
const BODY_NOTICE_ID = 'projectpulse-module-011-retirement-notice';
const RETIRED_MODULE_NUMBERS = new Set(
  RETIRED_PROJECTPULSE_MODULES.map((module) => module.moduleNumber.toUpperCase())
);
const SUPER_ADMINISTRATOR_ROLE_CODES = new Set(['SUPER_ADMINISTRATOR', 'ADMINISTRATOR']);

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

function storedJson(key) {
  try {
    return JSON.parse(window.localStorage.getItem(key) || 'null');
  } catch {
    return null;
  }
}

function sessionToken() {
  const session = storedJson('projectPulseAuthSession');
  return session?.sessionToken || session?.token || session?.accessToken || '';
}

function activeViewAs() {
  const value = storedJson('projectPulseViewAsUser');
  return value?.userId ? value : null;
}

function permissionHeaders() {
  const token = sessionToken();
  const headers = token ? {
    'X-ProjectPulse-Session': token,
    'X-Project-Pulse-Session': token,
    'X-Session-Token': token,
    Authorization: `Bearer ${token}`,
    'Cache-Control': 'no-cache',
    Pragma: 'no-cache'
  } : {};
  const viewAs = activeViewAs();
  if (viewAs?.userId) headers['X-ProjectPulse-View-As-User'] = viewAs.userId;
  return headers;
}

function normalizedRoleCodes(value) {
  const source = Array.isArray(value)
    ? value
    : String(value || '').split(/[\s,;|]+/);
  return source
    .map((item) => String(item || '').trim().toUpperCase())
    .filter(Boolean);
}

function installPermissionNavigationGuard(nativeFetch) {
  if (window[PERMISSION_MARKER]) return;
  window[PERMISSION_MARKER] = true;

  let deniedModuleNumbers = new Set(RETIRED_MODULE_NUMBERS);
  let permissionEvidenceState = sessionToken() ? 'loading' : 'anonymous';
  let effectiveActor = {
    roleCodes: [],
    isViewAs: Boolean(activeViewAs()),
    permanentFullControl: false
  };
  let observer = null;
  let applyTimer = 0;
  let moreSearchValue = '';

  function routeOf(element) {
    const declared = element.getAttribute?.('data-route');
    if (declared) return rawModuleRoute(declared);
    const href = element.getAttribute?.('href');
    if (!href) return '';
    try {
      return rawModuleRoute(new URL(href, window.location.href).hash);
    } catch {
      return rawModuleRoute(href);
    }
  }

  function descriptorOf(element) {
    const declaredNumber = String(element.getAttribute?.('data-module-number') || '').trim().toUpperCase();
    const route = routeOf(element);
    const retired = retiredModuleForRoute(route);
    const module = declaredNumber
      ? moduleForNumber(declaredNumber)
      : retired || moduleForRoute(route);
    return { module, route, retired };
  }

  function isBlocked(descriptor) {
    if (descriptor.retired) return true;
    const moduleNumber = descriptor.module?.moduleNumber?.toUpperCase();
    return Boolean(moduleNumber && deniedModuleNumbers.has(moduleNumber));
  }

  function setAttributeIfChanged(element, name, value) {
    const next = String(value);
    if (element.getAttribute(name) !== next) element.setAttribute(name, next);
  }

  function restorePermissionVisibility(element) {
    if (element.hasAttribute(HIDDEN_ATTRIBUTE)) element.removeAttribute(HIDDEN_ATTRIBUTE);
    if (element.getAttribute('data-module-availability-hidden') !== 'true') {
      if (element.hidden) element.hidden = false;
      if (element.getAttribute('aria-hidden') === 'true') element.removeAttribute('aria-hidden');
    }
  }

  function applyElementVisibility() {
    document.querySelectorAll(`[${HIDDEN_ATTRIBUTE}="true"]`).forEach((element) => {
      if (!isBlocked(descriptorOf(element))) restorePermissionVisibility(element);
    });

    document.querySelectorAll('a[href], button[data-route], [data-module-number]').forEach((element) => {
      const descriptor = descriptorOf(element);
      if (!descriptor.module && !descriptor.retired) return;
      if (!isBlocked(descriptor)) return;
      if (!element.hidden) element.hidden = true;
      if (element.getAttribute(HIDDEN_ATTRIBUTE) !== 'true') element.setAttribute(HIDDEN_ATTRIBUTE, 'true');
      if (element.getAttribute('aria-hidden') !== 'true') element.setAttribute('aria-hidden', 'true');
    });
  }

  function enhanceMoreMenu() {
    const button = document.querySelector('.enterprise-more-button');
    if (button) {
      setAttributeIfChanged(button, 'aria-label', 'Open pages available to the current effective user');
      if (button.title !== 'Pages available to your current role or View-As identity') {
        button.title = 'Pages available to your current role or View-As identity';
      }
    }

    const dropdown = document.querySelector('#enterprise-more-navigation-menu.enterprise-more-dropdown');
    if (!dropdown) return;
    dropdown.classList.add('projectpulse-more-intuitive');
    setAttributeIfChanged(dropdown, 'role', 'menu');
    setAttributeIfChanged(dropdown, 'data-permission-evidence', permissionEvidenceState);
    setAttributeIfChanged(dropdown, 'aria-busy', permissionEvidenceState === 'loading' ? 'true' : 'false');

    const search = moreSearchValue.trim().toLowerCase();
    let visibleCount = 0;
    dropdown.querySelectorAll(':scope > .enterprise-more-group').forEach((group) => {
      const heading = group.querySelector(':scope > strong');
      const groupName = heading?.textContent?.trim() || 'Pages';
      if (heading) setAttributeIfChanged(heading, 'aria-label', `${groupName} pages`);
      let groupVisibleCount = 0;

      group.querySelectorAll('.enterprise-more-links > a[href]').forEach((link) => {
        const descriptor = descriptorOf(link);
        const module = descriptor.module;
        if (module && !descriptor.retired) {
          link.dataset.moduleNumber = module.moduleNumber;
          link.dataset.route = module.route;
          setAttributeIfChanged(link, 'role', 'menuitem');
          setAttributeIfChanged(link, 'aria-label', `Open ${module.displayName || link.dataset.pageName || link.textContent?.trim() || module.route}`);
          const title = module.description || module.displayName || module.route;
          if (link.title !== title) link.title = title;
        }

        const blocked = isBlocked(descriptor)
          || link.getAttribute(HIDDEN_ATTRIBUTE) === 'true'
          || link.getAttribute('data-module-availability-hidden') === 'true';
        const searchable = `${link.dataset.pageName || ''} ${module?.displayName || ''} ${module?.group || ''} ${groupName}`.toLowerCase();
        const matches = !search || searchable.includes(search);
        const permissionReady = permissionEvidenceState === 'ready';
        const searchHidden = blocked || !matches || !permissionReady;
        setAttributeIfChanged(link, MORE_SEARCH_HIDDEN_ATTRIBUTE, searchHidden ? 'true' : 'false');
        if (!searchHidden && !link.hidden) {
          groupVisibleCount += 1;
          visibleCount += 1;
        }
      });

      setAttributeIfChanged(
        group,
        'data-projectpulse-more-group-hidden',
        groupVisibleCount === 0 ? 'true' : 'false'
      );
    });

    dropdown.dataset.visiblePageCount = String(visibleCount);
    dropdown.dataset.searchActive = search ? 'true' : 'false';
  }

  function enhanceIntakeHandoff() {
    const section = document.querySelector('#intake-work-task-handoff');
    if (!section) return;
    // The source component owns its children. Keep only a non-structural marker
    // confirming the governed Module 020 → 055D/055C handoff.
    setAttributeIfChanged(section, 'data-projectpulse-work-management-handoff', '020-to-055d-055c');
  }

  function removeBodyOwnedRetirementNotice() {
    const notice = document.getElementById(BODY_NOTICE_ID);
    if (notice?.parentElement === document.body) notice.remove();
  }

  function showRetirementNotice() {
    if (rawModuleRoute(window.location.hash) !== 'work-register'
        || window.sessionStorage.getItem(RETIRED_ROUTE_NOTICE_KEY) !== 'true') {
      removeBodyOwnedRetirementNotice();
      return;
    }
    if (document.getElementById(BODY_NOTICE_ID)) return;

    // This notice is deliberately a direct child of body, outside #root. React
    // never owns this subtree, so its lifecycle cannot cause removeChild errors.
    const notice = document.createElement('aside');
    notice.id = BODY_NOTICE_ID;
    notice.className = 'projectpulse-work-management-retirement-notice projectpulse-body-owned-notice';
    notice.dataset.projectpulseBodyOwned = 'true';

    const copy = document.createElement('div');
    const title = document.createElement('strong');
    title.textContent = 'Module 011 Work Task Builder is retired.';
    const description = document.createElement('span');
    description.textContent = 'You were moved to Module 055C for existing project management. Use Module 055D when creating a new project.';
    copy.append(title, description);

    const actions = document.createElement('div');
    const createLink = document.createElement('a');
    createLink.href = '#create-work-register';
    createLink.textContent = 'Create New Project';
    const dismiss = document.createElement('button');
    dismiss.type = 'button';
    dismiss.textContent = 'Dismiss';
    dismiss.addEventListener('click', () => {
      window.sessionStorage.removeItem(RETIRED_ROUTE_NOTICE_KEY);
      removeBodyOwnedRetirementNotice();
    });
    actions.append(createLink, dismiss);
    notice.append(copy, actions);
    document.body.append(notice);
  }

  function enforceRouteBoundary() {
    const rawRoute = rawModuleRoute(window.location.hash || '#dashboard') || 'dashboard';
    const retired = retiredModuleForRoute(rawRoute);
    if (retired) {
      window.sessionStorage.setItem(RETIRED_ROUTE_NOTICE_KEY, 'true');
      if (rawRoute !== 'work-register') window.location.replace('#work-register');
      return;
    }

    const currentModule = moduleForRoute(rawRoute);
    if (currentModule && deniedModuleNumbers.has(currentModule.moduleNumber.toUpperCase())) {
      window.location.hash = '#dashboard';
      window.dispatchEvent(new CustomEvent('projectpulse:permission-route-denied', {
        detail: { moduleNumber: currentModule.moduleNumber, route: currentModule.route }
      }));
    }
  }

  function publishNavigationState() {
    const detail = {
      state: permissionEvidenceState,
      isViewAs: effectiveActor.isViewAs,
      roleCodes: [...effectiveActor.roleCodes],
      permanentFullControl: Boolean(effectiveActor.permanentFullControl),
      deniedModuleNumbers: [...deniedModuleNumbers],
      retiredModuleNumbers: [...RETIRED_MODULE_NUMBERS],
      evidenceContract: 'projectpulse-rbac-v1',
      reactDomOwnership: 'attributes-only-v1',
      secretValuesReturned: false
    };
    window.__projectPulseEffectiveNavigation = detail;
    window.dispatchEvent(new CustomEvent('projectpulse:permission-navigation-updated', { detail }));
  }

  function applyVisibility() {
    applyElementVisibility();
    enforceRouteBoundary();
    enhanceMoreMenu();
    enhanceIntakeHandoff();
    showRetirementNotice();
  }

  function scheduleApply() {
    window.clearTimeout(applyTimer);
    applyTimer = window.setTimeout(applyVisibility, 25);
  }

  window.ProjectPulseMoreNavigation = {
    filter(value = '') {
      moreSearchValue = String(value || '');
      scheduleApply();
    },
    clear() {
      moreSearchValue = '';
      scheduleApply();
    },
    get state() {
      return {
        permissionEvidenceState,
        search: moreSearchValue,
        isViewAs: effectiveActor.isViewAs,
        permanentFullControl: Boolean(effectiveActor.permanentFullControl)
      };
    }
  };

  async function refreshPermissions() {
    const token = sessionToken();
    if (!token) {
      deniedModuleNumbers = new Set(RETIRED_MODULE_NUMBERS);
      permissionEvidenceState = 'anonymous';
      effectiveActor = { roleCodes: [], isViewAs: false, permanentFullControl: false };
      applyVisibility();
      publishNavigationState();
      return;
    }

    permissionEvidenceState = 'loading';
    applyVisibility();
    try {
      const request = {
        method: 'GET',
        cache: 'no-store',
        credentials: 'include',
        headers: permissionHeaders()
      };
      const [bootstrapResponse, matrixResponse] = await Promise.all([
        nativeFetch('/api/rbac/v1/bootstrap', request),
        nativeFetch('/api/rbac/v1/matrix', request)
      ]);
      if (!bootstrapResponse.ok || !matrixResponse.ok) {
        throw new Error('Dynamic RBAC navigation evidence could not be loaded.');
      }

      const [bootstrap, matrix] = await Promise.all([
        bootstrapResponse.json(),
        matrixResponse.json()
      ]);
      if (!Array.isArray(bootstrap?.roles)
          || !Array.isArray(bootstrap?.modules)
          || !Array.isArray(matrix?.roles)
          || !Array.isArray(matrix?.modules)
          || !Array.isArray(matrix?.grants)) {
        throw new Error('Dynamic RBAC navigation evidence was incomplete.');
      }

      const viewAs = activeViewAs();
      let actorRoles = normalizedRoleCodes(bootstrap?.actor?.roleCodes);
      if (viewAs && actorRoles.length === 0) actorRoles = normalizedRoleCodes(viewAs.roleCodes);
      const roleSet = new Set(actorRoles);
      const actualSuperAdministrator = !viewAs
        && actorRoles.some((roleCode) => SUPER_ADMINISTRATOR_ROLE_CODES.has(roleCode));
      const activeModuleNumbers = new Set(matrix.modules
        .map((module) => String(module?.moduleCode || '').trim().toUpperCase())
        .filter(Boolean));
      const denied = new Set(RETIRED_MODULE_NUMBERS);

      if (!actualSuperAdministrator) {
        for (const module of PROJECTPULSE_MODULES) {
          const number = String(module.moduleNumber || '').trim().toUpperCase();
          if (number && !activeModuleNumbers.has(number)) denied.add(number);
        }

        matrix.grants
          .filter((grant) => roleSet.has(String(grant.roleCode || '').toUpperCase()))
          .filter((grant) => String(grant.actionCode || '').toUpperCase() === 'MODULE_ACCESS')
          .filter((grant) => String(grant.grantEffect || '').toUpperCase() === 'DENY')
          .forEach((grant) => denied.add(String(grant.moduleCode || '').toUpperCase()));
      }

      deniedModuleNumbers = denied;
      permissionEvidenceState = 'ready';
      effectiveActor = {
        roleCodes: actorRoles,
        isViewAs: Boolean(viewAs),
        permanentFullControl: actualSuperAdministrator
      };
      applyVisibility();
      publishNavigationState();
    } catch {
      deniedModuleNumbers = new Set(RETIRED_MODULE_NUMBERS);
      permissionEvidenceState = 'unavailable';
      effectiveActor = {
        roleCodes: [],
        isViewAs: Boolean(activeViewAs()),
        permanentFullControl: false
      };
      applyVisibility();
      publishNavigationState();
    }
  }

  const boot = () => {
    applyVisibility();
    void refreshPermissions();
    observer = new MutationObserver((mutations) => {
      if (mutations.some((mutation) => mutation.addedNodes.length || mutation.removedNodes.length)) {
        scheduleApply();
      }
    });
    observer.observe(document.body, { childList: true, subtree: true });
    document.addEventListener('click', (event) => {
      if (event.target.closest?.('.enterprise-more-button')) scheduleApply();
    }, true);
  };

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', boot, { once: true });
  } else {
    boot();
  }

  window.addEventListener('hashchange', () => {
    applyVisibility();
    void refreshPermissions();
  });
  window.addEventListener('storage', (event) => {
    if (event.key === 'projectPulseAuthSession' || event.key === 'projectPulseViewAsUser') {
      void refreshPermissions();
    }
  });
  window.addEventListener('projectpulse:auth-session-ready', refreshPermissions);
  window.addEventListener('projectpulse:view-as-changed', refreshPermissions);
  window.addEventListener('projectpulse:permissions-changed', refreshPermissions);
  window.addEventListener('projectpulse:module-availability-loaded', scheduleApply);
  window.addEventListener('projectpulse:module-availability-changed', scheduleApply);
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

    const headers = new Headers(
      init?.headers || (input instanceof Request ? input.headers : undefined)
    );
    if (!headers.has('X-ProjectPulse-Module-Number')) {
      headers.set('X-ProjectPulse-Module-Number', module.moduleNumber);
    }

    return nativeFetch(input, { ...init, headers });
  };

  window[INSTALL_MARKER] = true;
  installPermissionNavigationGuard(nativeFetch);
}
