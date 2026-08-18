import {
  hasAnyEffectiveRole,
  readEffectiveRoleAuthority
} from './effective-role-authority.js';
import {
  PROJECTPULSE_MODULES,
  moduleForRoute
} from './module-availability-registry.js';

const INSTALL_MARKER = '__projectPulseBackgroundRequestRoleGateInstalled';
const MODULE_DIRECTORY_SNAPSHOT_CONTRACT = 'VISIBLE_AUTHORIZED_NAVIGATION_SNAPSHOT_V1';
const MODULE_DIRECTORY_SNAPSHOT_PREFIX = 'projectPulseModuleDirectorySnapshot:';
const MODULE_DIRECTORY_ROUTE = 'modules';
const OWNER_EVENT = 'projectpulse:module-owner-changed';
const OWNER_CATALOG_READ_CONTRACT = 'OWNER_CATALOG_READ_THROUGH_FOR_AUTHENTICATED_USERS_V1';
const MODULE_DIRECTORY_AUTHORITY_RETRY_MS = 100;
const MODULE_DIRECTORY_AUTHORITY_MAX_ATTEMPTS = 80;
const MODULE_DIRECTORY_PERMISSION_REFRESH_THROTTLE_MS = 1500;
const MODULE_DIRECTORY_SNAPSHOT_MAX_AGE_MS = 30 * 60 * 1000;

const PLATFORM_OPERATIONS_ROLES = new Set([
  'SUPER_ADMINISTRATOR',
  'ADMINISTRATOR'
]);

const AUDIT_SUMMARY_ROLES = new Set([
  'SUPER_ADMINISTRATOR',
  'ADMINISTRATOR',
  'AUDITOR',
  'SECURITY',
  'SECURITY_ADMINISTRATOR'
]);

const WORKFLOW_EXPORT_ROLES = new Set([
  'SUPER_ADMINISTRATOR',
  'ADMINISTRATOR',
  'PROJECT_TEAM_COORDINATOR',
  'PROJECT_COORDINATOR',
  'PROJECT_MANAGER',
  'PROJECT_MANAGEMENT',
  'PROJECT_MANAGEMENT_LEAD',
  'PROJECT_MANAGEMENT_TEAM_LEAD',
  'PM_TEAM_LEAD',
  'ACCOUNTING',
  'ACCOUNTING_BILLING',
  'BILLING',
  'FINANCE'
]);

const OPERATIONS_ACKNOWLEDGMENT_ROLES = new Set([
  'SUPER_ADMINISTRATOR',
  'ADMINISTRATOR',
  'PROJECT_TEAM_COORDINATOR'
]);

const MANAGER_APPROVAL_ROLES = new Set([
  'SUPER_ADMINISTRATOR',
  'ADMINISTRATOR',
  'MANAGER',
  'PEOPLE_MANAGER',
  'ENGINEERING_LEAD',
  'ENGINEERING_TEAM_LEAD',
  'ENGINEERING_MANAGER',
  'PROJECT_MANAGER',
  'PROJECT_MANAGEMENT',
  'PROJECT_MANAGEMENT_LEAD',
  'PROJECT_MANAGEMENT_TEAM_LEAD',
  'PM_TEAM_LEAD'
]);

const TIME_STEWARD_ROLES = new Set([
  'SUPER_ADMINISTRATOR',
  'ADMINISTRATOR',
  'PROJECT_TEAM_COORDINATOR'
]);

const RESTRICTED_BACKGROUND_ROUTES = Object.freeze([
  {
    matches: (path) => path === '/api/production/readiness-command-center',
    roles: PLATFORM_OPERATIONS_ROLES,
    kind: 'readiness'
  },
  {
    matches: (path) => path === '/api/navigation/registry-integrity',
    roles: PLATFORM_OPERATIONS_ROLES,
    kind: 'registry'
  },
  {
    matches: (path) => path === '/api/dashboard/module-visibility-smoke',
    roles: PLATFORM_OPERATIONS_ROLES,
    kind: 'visibility'
  },
  {
    matches: (path) => path === '/api/audit-history/summary',
    roles: AUDIT_SUMMARY_ROLES,
    kind: 'audit'
  },
  {
    matches: (path) => path === '/api/workflow/approval-export-summary',
    roles: WORKFLOW_EXPORT_ROLES,
    kind: 'workflow'
  },
  {
    matches: (path) => path === '/api/production/operations-acknowledgments/summary',
    roles: OPERATIONS_ACKNOWLEDGMENT_ROLES,
    kind: 'acknowledgments'
  },
  {
    matches: (path) => path === '/api/manager/approvals',
    roles: MANAGER_APPROVAL_ROLES,
    kind: 'managerApprovals'
  },
  {
    matches: (path) => path === '/api/runtime/timesheet/steward/v2/users',
    roles: TIME_STEWARD_ROLES,
    kind: 'timeStewardUsers'
  }
]);

function clean(value) {
  return String(value ?? '').replace(/\s+/g, ' ').trim();
}

function normalizedModuleNumber(value) {
  return clean(value).toUpperCase();
}

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

function roleNotApplicableStatus(authority) {
  return authority?.ready ? 'role_not_applicable' : 'authorization_pending';
}

function neutralPayload(kind, authority) {
  const status = roleNotApplicableStatus(authority);
  const access = {
    applicable: false,
    canManage: false,
    isViewAs: authority?.viewAsActive === true,
    roleCodes: authority?.roleCodes || []
  };

  switch (kind) {
    case 'readiness':
      return {
        status,
        checks: [],
        commands: [],
        blockers: [],
        summary: { total: 0, ready: 0, blocked: 0 },
        access
      };
    case 'registry':
      return {
        status,
        issues: [],
        entries: [],
        summary: { total: 0, healthy: 0, warning: 0, failed: 0 },
        access
      };
    case 'visibility':
      return {
        status,
        results: [],
        modules: [],
        summary: { total: 0, visible: 0, hidden: 0 },
        access
      };
    case 'audit':
      return {
        status,
        events: [],
        recent: [],
        summary: { total: 0, changes: 0, security: 0 },
        access
      };
    case 'workflow':
      return {
        status,
        items: [],
        packages: [],
        summary: { total: 0, pending: 0, ready: 0 },
        access
      };
    case 'acknowledgments':
      return {
        status,
        acknowledgments: [],
        summary: { total: 0, acknowledged: 0, pending: 0 },
        access
      };
    case 'managerApprovals':
      return {
        status,
        approvals: [],
        items: [],
        count: 0,
        summary: { total: 0, pending: 0, approved: 0, declined: 0 },
        access
      };
    case 'timeStewardUsers':
      return {
        status,
        users: [],
        count: 0,
        total: 0,
        page: 1,
        pageSize: 0,
        access: {
          ...access,
          canManageOthers: false
        }
      };
    default:
      return { status, items: [], access };
  }
}

function restrictedRoute(path) {
  return RESTRICTED_BACKGROUND_ROUTES.find((policy) => policy.matches(path)) || null;
}

function readJsonStorage(storage, key) {
  try {
    const raw = storage.getItem(key);
    return raw ? JSON.parse(raw) : null;
  } catch {
    return null;
  }
}

function sessionIdentityFingerprint() {
  const session = readJsonStorage(window.localStorage, 'projectPulseAuthSession') || {};
  const viewAs = readJsonStorage(window.localStorage, 'projectPulseViewAsUser') || {};
  const actualIdentity = clean(
    session.userId
      || session.userID
      || session.email
      || session.username
      || session.userPrincipalName
  ).toLowerCase();
  const effectiveIdentity = clean(
    viewAs.userId
      || viewAs.email
      || actualIdentity
  ).toLowerCase();

  // The provisional cache is identity-scoped rather than role-keyed so it can
  // hydrate the Modules route before the asynchronous role authority is ready.
  // It is replaced by current server evidence immediately, and backend access
  // remains authoritative for every module route.
  return `${actualIdentity || 'anonymous'}|${effectiveIdentity || 'self'}`;
}

function snapshotStorageKey() {
  return `${MODULE_DIRECTORY_SNAPSHOT_PREFIX}${sessionIdentityFingerprint()}`;
}

function currentRoute() {
  return clean(window.location.hash).replace(/^#/, '') || 'dashboard';
}

function permissionHidden(element) {
  return element.hidden
    || element.getAttribute('aria-hidden') === 'true'
    || element.getAttribute('data-projectpulse-permission-hidden') === 'true'
    || element.getAttribute('data-module-availability-hidden') === 'true'
    || Boolean(element.closest(
      '[data-projectpulse-permission-hidden="true"], [data-module-availability-hidden="true"]'
    ));
}

function visibleAuthorizedModuleNumbers() {
  const moduleNumbers = new Set();
  const anchors = document.querySelectorAll([
    '.enterprise-sidebar a[href^="#"]',
    '.enterprise-top-navigation a[href^="#"]'
  ].join(','));

  for (const anchor of anchors) {
    if (permissionHidden(anchor)) continue;
    const href = clean(anchor.getAttribute('href'));
    const route = href.replace(/^#/, '');
    if (!route || route === 'dashboard' || route === MODULE_DIRECTORY_ROUTE) continue;
    const module = moduleForRoute(route);
    if (module?.moduleNumber) moduleNumbers.add(normalizedModuleNumber(module.moduleNumber));
  }

  return [...moduleNumbers];
}

function moduleNumbersFromNavigation(detail) {
  if (!detail || detail.state !== 'ready') return [];
  const denied = new Set((detail.deniedModuleNumbers || []).map(normalizedModuleNumber));
  const retired = new Set((detail.retiredModuleNumbers || []).map(normalizedModuleNumber));
  return PROJECTPULSE_MODULES
    .map((module) => normalizedModuleNumber(module.moduleNumber))
    .filter((moduleNumber) => moduleNumber && !denied.has(moduleNumber) && !retired.has(moduleNumber));
}

function saveReadyNavigationSnapshot(detail) {
  if (!detail || detail.state !== 'ready' || detail.provisionalModuleDirectorySnapshot === true) return;
  const moduleNumbers = moduleNumbersFromNavigation(detail);
  if (!moduleNumbers.length) return;

  try {
    window.sessionStorage.setItem(snapshotStorageKey(), JSON.stringify({
      contract: MODULE_DIRECTORY_SNAPSHOT_CONTRACT,
      identityFingerprint: sessionIdentityFingerprint(),
      moduleNumbers,
      roleCodes: detail.roleCodes || [],
      isViewAs: detail.isViewAs === true,
      savedAt: Date.now()
    }));
  } catch {
    // Browser storage can be unavailable. Visible authorized navigation remains usable.
  }
}

function readCachedModuleNumbers() {
  const snapshot = readJsonStorage(window.sessionStorage, snapshotStorageKey());
  if (!snapshot
      || snapshot.contract !== MODULE_DIRECTORY_SNAPSHOT_CONTRACT
      || snapshot.identityFingerprint !== sessionIdentityFingerprint()
      || !Array.isArray(snapshot.moduleNumbers)
      || Date.now() - Number(snapshot.savedAt || 0) > MODULE_DIRECTORY_SNAPSHOT_MAX_AGE_MS) {
    return [];
  }

  return snapshot.moduleNumbers.map(normalizedModuleNumber).filter(Boolean);
}

function publishImmediateNavigationSnapshot(moduleNumbers, authoritySource) {
  const allowed = new Set(moduleNumbers.map(normalizedModuleNumber).filter(Boolean));
  if (!allowed.size) return false;

  const current = window.__projectPulseEffectiveNavigation || {};
  if (current.state === 'ready' && current.provisionalModuleDirectorySnapshot !== true) return true;

  const authority = readEffectiveRoleAuthority();
  const retired = new Set((current.retiredModuleNumbers || []).map(normalizedModuleNumber));
  const deniedModuleNumbers = PROJECTPULSE_MODULES
    .map((module) => normalizedModuleNumber(module.moduleNumber))
    .filter((moduleNumber) => moduleNumber && (!allowed.has(moduleNumber) || retired.has(moduleNumber)));

  const detail = {
    ...current,
    state: 'ready',
    roleCodes: authority.roleCodes || [],
    isViewAs: authority.viewAsActive === true,
    permanentFullControl: false,
    authoritySource,
    deniedModuleNumbers,
    retiredModuleNumbers: [...retired],
    explicitDeniedModuleNumbers: current.explicitDeniedModuleNumbers || [],
    explicitGrantedModuleNumbers: current.explicitGrantedModuleNumbers || [],
    activeDynamicModuleNumbers: current.activeDynamicModuleNumbers || [],
    inactiveDynamicModuleNumbers: current.inactiveDynamicModuleNumbers || [],
    legacyFallbackModuleNumbers: current.legacyFallbackModuleNumbers || [],
    unregisteredLegacyModuleNumbers: current.unregisteredLegacyModuleNumbers || [],
    evidenceContract: current.evidenceContract || 'projectpulse-rbac-v1',
    provisionalModuleDirectorySnapshot: true,
    moduleDirectorySnapshotContract: MODULE_DIRECTORY_SNAPSHOT_CONTRACT
  };

  window.__projectPulseEffectiveNavigation = detail;
  window.dispatchEvent(new CustomEvent('projectpulse:permission-navigation-updated', { detail }));
  window.dispatchEvent(new CustomEvent('projectpulse:effective-navigation-changed', { detail }));
  return true;
}

function ensureImmediateModulesAuthority(source = 'visible_authorized_navigation_snapshot') {
  const current = window.__projectPulseEffectiveNavigation;
  if (current?.state === 'ready' && current.provisionalModuleDirectorySnapshot !== true) return true;

  const visible = visibleAuthorizedModuleNumbers();
  if (visible.length) return publishImmediateNavigationSnapshot(visible, source);

  const cached = readCachedModuleNumbers();
  if (cached.length) return publishImmediateNavigationSnapshot(cached, 'cached_authorized_navigation_snapshot');

  return false;
}

function requestModuleDirectoryPermissionRefresh(source) {
  const now = Date.now();
  const previous = Number(window.__projectPulseModuleDirectoryPermissionRefreshRequestedAt || 0);
  if (now - previous < MODULE_DIRECTORY_PERMISSION_REFRESH_THROTTLE_MS) return;
  window.__projectPulseModuleDirectoryPermissionRefreshRequestedAt = now;
  window.dispatchEvent(new CustomEvent('projectpulse:permissions-changed', {
    detail: { source, contract: MODULE_DIRECTORY_SNAPSHOT_CONTRACT }
  }));
}

function scheduleImmediateModulesAuthority(source, attempt = 0) {
  window.setTimeout(() => {
    if (currentRoute() !== MODULE_DIRECTORY_ROUTE) return;
    if (ensureImmediateModulesAuthority(source)) return;

    requestModuleDirectoryPermissionRefresh(source);
    if (attempt < MODULE_DIRECTORY_AUTHORITY_MAX_ATTEMPTS) {
      scheduleImmediateModulesAuthority(source, attempt + 1);
    }
  }, attempt === 0 ? 0 : MODULE_DIRECTORY_AUTHORITY_RETRY_MS);
}

function modulesNavigationTarget(event) {
  const target = event.target?.closest?.('a[href], button[data-route], [data-route]');
  if (!target) return false;
  const href = clean(target.getAttribute('href')).replace(/^#/, '');
  const route = clean(target.getAttribute('data-route')).replace(/^#/, '');
  return href === MODULE_DIRECTORY_ROUTE || route === MODULE_DIRECTORY_ROUTE;
}

function installImmediateModuleDirectoryAuthority() {
  document.addEventListener('click', (event) => {
    if (!modulesNavigationTarget(event)) return;
    ensureImmediateModulesAuthority('visible_authorized_navigation_snapshot');
  }, true);

  window.addEventListener('hashchange', () => {
    if (currentRoute() === MODULE_DIRECTORY_ROUTE) {
      scheduleImmediateModulesAuthority('modules_hash_navigation_snapshot');
    }
  });

  window.addEventListener('pageshow', () => {
    if (currentRoute() === MODULE_DIRECTORY_ROUTE) {
      scheduleImmediateModulesAuthority('modules_pageshow_navigation_snapshot');
    }
  });

  window.addEventListener('focus', () => {
    if (currentRoute() === MODULE_DIRECTORY_ROUTE
        && window.__projectPulseEffectiveNavigation?.state !== 'ready') {
      scheduleImmediateModulesAuthority('modules_focus_navigation_snapshot');
    }
  });

  window.addEventListener('projectpulse:auth-session-ready', () => {
    if (currentRoute() === MODULE_DIRECTORY_ROUTE) {
      scheduleImmediateModulesAuthority('modules_auth_session_navigation_snapshot');
    }
  });

  window.addEventListener('projectpulse:view-as-changed', () => {
    if (currentRoute() === MODULE_DIRECTORY_ROUTE) {
      scheduleImmediateModulesAuthority('modules_view_as_navigation_snapshot');
    }
  });

  window.addEventListener('projectpulse:permission-navigation-updated', (event) => {
    const detail = event?.detail || window.__projectPulseEffectiveNavigation;
    saveReadyNavigationSnapshot(detail);

    if (detail?.state === 'loading' && currentRoute() === MODULE_DIRECTORY_ROUTE) {
      scheduleImmediateModulesAuthority('navigation_refresh_visible_snapshot');
    }

    if (detail?.state === 'ready'
        && detail?.provisionalModuleDirectorySnapshot !== true) {
      const signature = `${sessionIdentityFingerprint()}|${clean(detail.authoritySource)}`;
      if (window.__projectPulseOwnerRefreshAuthoritySignature !== signature) {
        window.__projectPulseOwnerRefreshAuthoritySignature = signature;
        window.dispatchEvent(new CustomEvent(OWNER_EVENT, {
          detail: {
            source: 'owner_catalog_read_authority_ready',
            contract: OWNER_CATALOG_READ_CONTRACT
          }
        }));
      }
    }
  });

  const boot = () => {
    if (currentRoute() === MODULE_DIRECTORY_ROUTE) {
      scheduleImmediateModulesAuthority('modules_initial_navigation_snapshot');
    }
  };

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', boot, { once: true });
  } else {
    boot();
  }
}

function installBackgroundRequestRoleGate() {
  if (typeof window === 'undefined' || window[INSTALL_MARKER]) return;

  const downstreamFetch = window.fetch.bind(window);

  window.fetch = async (input, init = {}) => {
    const url = sameOriginApiUrl(input);
    if (!url || requestMethod(input, init) !== 'GET') return downstreamFetch(input, init);

    const policy = restrictedRoute(url.pathname);
    if (!policy) return downstreamFetch(input, init);

    const authority = readEffectiveRoleAuthority();
    if (hasAnyEffectiveRole(authority, policy.roles)) return downstreamFetch(input, init);

    return jsonResponse(neutralPayload(policy.kind, authority));
  };

  window[INSTALL_MARKER] = true;
  installImmediateModuleDirectoryAuthority();
}

installBackgroundRequestRoleGate();
