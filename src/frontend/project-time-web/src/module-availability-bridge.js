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
const RETIRED_MODULE_NUMBERS = new Set(
  RETIRED_PROJECTPULSE_MODULES.map((module) => module.moduleNumber.toUpperCase())
);

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
  let effectiveActor = { roleCodes: [], isViewAs: Boolean(activeViewAs()) };
  let observer = null;
  let applyTimer = 0;
  let moreSearchValue = '';

  function routeOf(element) {
    const declared = element.getAttribute?.('data-route');
    if (declared) return rawModuleRoute(declared);
    const href = element.getAttribute?.('href');
    if (!href) return '';
    try {
      const url = new URL(href, window.location.href);
      return rawModuleRoute(url.hash);
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

  function visibleMoreLink(link) {
    return link.getAttribute(HIDDEN_ATTRIBUTE) !== 'true'
      && link.getAttribute('data-module-availability-hidden') !== 'true'
      && link.getAttribute(MORE_SEARCH_HIDDEN_ATTRIBUTE) !== 'true'
      && !link.hidden;
  }

  function ensureMoreTools(dropdown) {
    let tools = dropdown.querySelector(':scope > .projectpulse-more-menu-tools');
    if (tools) return tools;

    tools = document.createElement('div');
    tools.className = 'projectpulse-more-menu-tools';
    tools.innerHTML = `
      <label for="projectpulse-more-menu-search">Find an available page</label>
      <div class="projectpulse-more-menu-search-row">
        <span aria-hidden="true">⌕</span>
        <input id="projectpulse-more-menu-search" type="search" autocomplete="off" placeholder="Search module number or page name" />
        <button type="button" aria-label="Clear More menu search">Clear</button>
      </div>
      <p class="projectpulse-more-menu-status" role="status" aria-live="polite"></p>
    `;
    const input = tools.querySelector('input');
    const clear = tools.querySelector('button');
    input.value = moreSearchValue;
    input.addEventListener('input', () => {
      moreSearchValue = input.value;
      scheduleApply();
    });
    clear.addEventListener('click', () => {
      moreSearchValue = '';
      input.value = '';
      input.focus();
      scheduleApply();
    });
    dropdown.prepend(tools);
    return tools;
  }

  function decorateMoreLink(link, groupName) {
    const descriptor = descriptorOf(link);
    const module = descriptor.module;
    if (!module || descriptor.retired) return descriptor;

    link.dataset.moduleNumber = module.moduleNumber;
    link.dataset.route = module.route;
    link.setAttribute('role', 'menuitem');
    const title = module.displayName || link.textContent?.trim() || module.route;
    const expectedKey = `${module.moduleNumber}|${title}|${module.group || groupName}`;
    if (link.dataset.projectpulseMoreDecoration !== expectedKey) {
      const number = document.createElement('span');
      number.className = 'projectpulse-more-module-number';
      number.textContent = `MODULE ${module.moduleNumber}`;

      const copy = document.createElement('span');
      copy.className = 'projectpulse-more-link-copy';
      const strong = document.createElement('strong');
      strong.textContent = title;
      const small = document.createElement('small');
      small.textContent = module.description || module.group || groupName;
      copy.append(strong, small);

      link.replaceChildren(number, copy);
      link.dataset.projectpulseMoreDecoration = expectedKey;
      link.setAttribute('aria-label', `Module ${module.moduleNumber}, ${title}`);
      link.title = module.description || `${module.group || groupName} · Module ${module.moduleNumber}`;
    }
    return descriptor;
  }

  function enhanceMoreMenu() {
    const button = document.querySelector('.enterprise-more-button');
    if (button) {
      button.setAttribute('aria-label', 'Open pages available to the current effective user');
      button.title = 'Pages available to your current role or View-As identity';
    }

    const dropdown = document.querySelector('#enterprise-more-navigation-menu.enterprise-more-dropdown');
    if (!dropdown) return;
    dropdown.setAttribute('role', 'menu');
    dropdown.dataset.permissionEvidence = permissionEvidenceState;
    dropdown.setAttribute('aria-busy', permissionEvidenceState === 'loading' ? 'true' : 'false');

    const tools = ensureMoreTools(dropdown);
    const status = tools.querySelector('.projectpulse-more-menu-status');
    const search = moreSearchValue.trim().toLowerCase();
    let visibleCount = 0;

    dropdown.querySelectorAll(':scope > .enterprise-more-group').forEach((group) => {
      const heading = group.querySelector(':scope > strong');
      const groupName = heading?.textContent?.trim() || 'Pages';
      if (heading) heading.setAttribute('aria-label', `${groupName} pages`);
      let groupVisibleCount = 0;

      group.querySelectorAll('.enterprise-more-links > a[href]').forEach((link) => {
        const descriptor = decorateMoreLink(link, groupName);
        const module = descriptor.module;
        const blocked = isBlocked(descriptor)
          || link.getAttribute(HIDDEN_ATTRIBUTE) === 'true'
          || link.getAttribute('data-module-availability-hidden') === 'true';
        const searchable = `${module?.moduleNumber || ''} ${module?.displayName || ''} ${module?.group || ''} ${groupName}`.toLowerCase();
        const matches = !search || searchable.includes(search);
        const permissionReady = permissionEvidenceState === 'ready';
        const searchHidden = blocked || !matches || !permissionReady;
        link.setAttribute(MORE_SEARCH_HIDDEN_ATTRIBUTE, searchHidden ? 'true' : 'false');
        if (!searchHidden && visibleMoreLink(link)) {
          groupVisibleCount += 1;
          visibleCount += 1;
        }
      });

      group.dataset.projectpulseMoreGroupHidden = groupVisibleCount === 0 ? 'true' : 'false';
    });

    let empty = dropdown.querySelector(':scope > .projectpulse-more-menu-empty');
    if (!empty) {
      empty = document.createElement('div');
      empty.className = 'projectpulse-more-menu-empty';
      empty.setAttribute('role', 'status');
      dropdown.append(empty);
    }

    if (permissionEvidenceState === 'loading') {
      status.textContent = 'Checking the current user’s module permissions…';
      empty.textContent = 'Available pages will appear after permission verification.';
    } else if (permissionEvidenceState === 'unavailable') {
      status.textContent = 'Permission evidence is temporarily unavailable.';
      empty.textContent = 'The More menu is hidden until permissions can be verified. Refresh the page to try again.';
    } else if (permissionEvidenceState === 'anonymous') {
      status.textContent = 'Sign in to view available pages.';
      empty.textContent = 'No authenticated navigation is available.';
    } else {
      const identity = effectiveActor.isViewAs ? 'View-As identity' : 'current user';
      status.textContent = `${visibleCount} page${visibleCount === 1 ? '' : 's'} available to the ${identity}${search ? ' for this search' : ''}.`;
      empty.textContent = search
        ? 'No permitted pages match this search.'
        : 'No additional permitted pages are available.';
    }
    empty.dataset.visible = visibleCount === 0 ? 'true' : 'false';
  }

  function enhanceIntakeHandoff() {
    const section = document.querySelector('#intake-work-task-handoff');
    if (!section) return;
    const heading = section.querySelector('.section-heading h2');
    if (heading && heading.textContent !== 'Project Intake → Project Creation & Work Register Handoff') {
      heading.textContent = 'Project Intake → Project Creation & Work Register Handoff';
    }
    const copy = section.querySelector('.section-heading .section-copy');
    if (copy) {
      copy.textContent = 'Module 020 owns pre-project intake, signed-date aging, project-link confirmation, and resource handoff. Create the resulting project in Module 055D, then maintain project tasks, assignments, and delivery details in Module 055C. Module 011 is retired.';
    }
    section.querySelectorAll('.handoff-lifecycle-grid article').forEach((card) => {
      card.querySelectorAll('strong, p').forEach((element) => {
        const next = String(element.textContent || '')
          .replace(/Work Task Builder/gi, 'Create New Project / Manage Existing Projects')
          .replace(/work task builder/gi, 'Modules 055D and 055C');
        if (next !== element.textContent) element.textContent = next;
      });
    });

    if (!section.querySelector('.projectpulse-work-management-handoff-actions')) {
      const actions = document.createElement('div');
      actions.className = 'projectpulse-work-management-handoff-actions';
      actions.innerHTML = `
        <a href="#create-work-register"><span>MODULE 055D</span><strong>Create New Project</strong><small>Create the project from GSD or SELL after intake is ready.</small></a>
        <a href="#work-register"><span>MODULE 055C</span><strong>Manage Existing Projects</strong><small>Maintain project details, tasks, assignments, and audited changes.</small></a>
      `;
      const headingContainer = section.querySelector('.section-heading');
      headingContainer?.insertAdjacentElement('afterend', actions);
    }
  }

  function showRetirementNotice() {
    if (rawModuleRoute(window.location.hash) !== 'work-register') return;
    if (window.sessionStorage.getItem(RETIRED_ROUTE_NOTICE_KEY) !== 'true') return;
    const main = document.querySelector('main.app-shell, main');
    if (!main || document.getElementById('projectpulse-module-011-retirement-notice')) return;

    const notice = document.createElement('div');
    notice.id = 'projectpulse-module-011-retirement-notice';
    notice.className = 'projectpulse-work-management-retirement-notice';
    notice.innerHTML = `
      <div><strong>Module 011 Work Task Builder is retired.</strong><span>You were moved to Module 055C for existing project management. Use Module 055D when creating a new project.</span></div>
      <div><a href="#create-work-register">Create New Project</a><button type="button">Dismiss</button></div>
    `;
    notice.querySelector('button')?.addEventListener('click', () => {
      window.sessionStorage.removeItem(RETIRED_ROUTE_NOTICE_KEY);
      notice.remove();
    });
    main.prepend(notice);
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
      deniedModuleNumbers: [...deniedModuleNumbers],
      retiredModuleNumbers: [...RETIRED_MODULE_NUMBERS],
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

  async function refreshPermissions() {
    const token = sessionToken();
    if (!token) {
      deniedModuleNumbers = new Set(RETIRED_MODULE_NUMBERS);
      permissionEvidenceState = 'anonymous';
      effectiveActor = { roleCodes: [], isViewAs: false };
      applyVisibility();
      publishNavigationState();
      return;
    }

    permissionEvidenceState = 'loading';
    applyVisibility();
    try {
      const request = { method: 'GET', cache: 'no-store', credentials: 'include', headers: permissionHeaders() };
      const [summaryResponse, matrixResponse] = await Promise.all([
        nativeFetch('/api/role-policy/summary', request),
        nativeFetch('/api/role-policy/matrix', request)
      ]);
      if (!summaryResponse.ok || !matrixResponse.ok) {
        throw new Error('Role-policy navigation evidence could not be loaded.');
      }

      const [summary, matrix] = await Promise.all([summaryResponse.json(), matrixResponse.json()]);
      const viewAs = activeViewAs();
      let actorRoles = normalizedRoleCodes(summary?.actor?.roleCodes);
      if (viewAs && actorRoles.length === 0) actorRoles = normalizedRoleCodes(viewAs.roleCodes);
      const roleSet = new Set(actorRoles);
      const denied = new Set(RETIRED_MODULE_NUMBERS);
      const actualSuperAdministrator = !viewAs && roleSet.has('SUPER_ADMINISTRATOR');
      if (!actualSuperAdministrator) {
        (matrix?.grants || [])
          .filter((grant) => roleSet.has(String(grant.roleCode || '').toUpperCase()))
          .filter((grant) => String(grant.actionCode || '').toUpperCase() === 'MODULE_ACCESS')
          .filter((grant) => String(grant.grantEffect || '').toUpperCase() === 'DENY')
          .forEach((grant) => denied.add(String(grant.moduleCode || '').toUpperCase()));
      }

      deniedModuleNumbers = denied;
      permissionEvidenceState = 'ready';
      effectiveActor = { roleCodes: actorRoles, isViewAs: Boolean(viewAs) };
      applyVisibility();
      publishNavigationState();
    } catch {
      deniedModuleNumbers = new Set(RETIRED_MODULE_NUMBERS);
      permissionEvidenceState = 'unavailable';
      effectiveActor = { roleCodes: [], isViewAs: Boolean(activeViewAs()) };
      applyVisibility();
      publishNavigationState();
    }
  }

  const boot = () => {
    applyVisibility();
    void refreshPermissions();
    observer = new MutationObserver(scheduleApply);
    observer.observe(document.body, { childList: true, subtree: true, attributes: true, attributeFilter: ['hidden', 'class', 'href'] });
  };

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', boot, { once: true });
  else boot();

  window.addEventListener('hashchange', () => {
    applyVisibility();
    void refreshPermissions();
  });
  window.addEventListener('storage', (event) => {
    if (event.key === 'projectPulseAuthSession' || event.key === 'projectPulseViewAsUser') void refreshPermissions();
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

    const headers = new Headers(init?.headers || (input instanceof Request ? input.headers : undefined));
    if (!headers.has('X-ProjectPulse-Module-Number')) {
      headers.set('X-ProjectPulse-Module-Number', module.moduleNumber);
    }

    return nativeFetch(input, { ...init, headers });
  };

  window[INSTALL_MARKER] = true;
  installPermissionNavigationGuard(nativeFetch);
}
