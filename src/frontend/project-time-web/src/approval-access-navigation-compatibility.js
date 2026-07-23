/*
 * Approval access and Modules navigation compatibility.
 *
 * The approval inbox is backend-authorized. This bridge does not grant any
 * permission; it makes the frontend consume the authoritative access endpoint
 * and avoids stale cached approval-count responses after web-only releases.
 */

const APPROVAL_COUNT_PATH = '/api/manager/approval-count';
const APPROVAL_ACCESS_PATH = '/api/approval-center/access';
const MODULES_ROUTE = 'modules';

function cleanText(value) {
  return String(value ?? '').replace(/\s+/g, ' ').trim();
}

function property(source, ...names) {
  if (!source || typeof source !== 'object') return undefined;
  for (const name of names) {
    if (Object.prototype.hasOwnProperty.call(source, name)) return source[name];
  }
  return undefined;
}

function booleanValue(value) {
  if (typeof value === 'boolean') return value;
  if (typeof value === 'number') return value !== 0;
  const normalized = cleanText(value).toLowerCase();
  return normalized === 'true' || normalized === '1' || normalized === 'yes';
}

function roleCodes(value) {
  if (Array.isArray(value)) {
    return value.map((role) => cleanText(role).toUpperCase()).filter(Boolean);
  }

  return cleanText(value)
    .split(/[;,|]/)
    .map((role) => role.trim().toUpperCase())
    .filter(Boolean);
}

function normalizeApprovalAccess(payload) {
  const source = property(payload, 'access', 'Access', 'approvalAccess', 'ApprovalAccess') ?? payload;
  if (!source || typeof source !== 'object') return null;

  const contractKeys = [
    'roleCodes', 'RoleCodes', 'roles', 'Roles',
    'canViewTimeApprovals', 'CanViewTimeApprovals',
    'canViewPasswordResetApprovals', 'CanViewPasswordResetApprovals',
    'canViewAllTimeApprovals', 'CanViewAllTimeApprovals'
  ];

  if (!contractKeys.some((key) => Object.prototype.hasOwnProperty.call(source, key))) return null;

  return {
    userId: property(source, 'userId', 'UserId') ?? null,
    email: cleanText(property(source, 'email', 'Email')),
    displayName: cleanText(property(source, 'displayName', 'DisplayName')),
    roleCodes: roleCodes(property(source, 'roleCodes', 'RoleCodes', 'roles', 'Roles')),
    canViewTimeApprovals: booleanValue(property(source, 'canViewTimeApprovals', 'CanViewTimeApprovals')),
    canViewPasswordResetApprovals: booleanValue(property(source, 'canViewPasswordResetApprovals', 'CanViewPasswordResetApprovals')),
    canViewAllTimeApprovals: booleanValue(property(source, 'canViewAllTimeApprovals', 'CanViewAllTimeApprovals')),
    canResolveStaleApprovals: booleanValue(property(source, 'canResolveStaleApprovals', 'CanResolveStaleApprovals')),
    scope: cleanText(property(source, 'scope', 'Scope')),
    scopeLabel: cleanText(property(source, 'scopeLabel', 'ScopeLabel')),
    primaryRoleLabel: cleanText(property(source, 'primaryRoleLabel', 'PrimaryRoleLabel'))
  };
}

async function jsonPayload(response) {
  const text = await response.clone().text();
  if (!text.trim()) return {};
  try {
    return JSON.parse(text);
  } catch {
    return { message: text };
  }
}

function responseWithJson(original, payload, status = original.status) {
  const headers = new Headers(original.headers);
  headers.set('Content-Type', 'application/json; charset=utf-8');
  headers.set('Cache-Control', 'no-store, no-cache, must-revalidate');
  headers.set('Pragma', 'no-cache');
  return new Response(JSON.stringify(payload), {
    status,
    statusText: original.statusText,
    headers
  });
}

function approvalRequestHeaders(input, init) {
  const headers = new Headers(init?.headers || (input instanceof Request ? input.headers : undefined));
  headers.set('Cache-Control', 'no-cache');
  headers.set('Pragma', 'no-cache');
  return headers;
}

function approvalRequestInit(input, init) {
  return {
    ...init,
    method: init?.method || (input instanceof Request ? input.method : 'GET'),
    headers: approvalRequestHeaders(input, init),
    credentials: init?.credentials || (input instanceof Request ? input.credentials : 'same-origin'),
    cache: 'no-store'
  };
}

function cacheBustedUrl(rawUrl, marker) {
  const url = new URL(rawUrl, window.location.origin);
  url.searchParams.set('approval_contract', marker);
  return url;
}

function installApprovalAccessCompatibility() {
  if (typeof window === 'undefined' || typeof window.fetch !== 'function') return;
  if (window.__projectPulseApprovalAccessCompatibilityInstalled) return;

  const previousFetch = window.fetch.bind(window);
  let sequence = 0;

  window.fetch = async (input, init = {}) => {
    const rawUrl = typeof input === 'string' ? input : input?.url;
    let url;
    try {
      url = new URL(rawUrl, window.location.origin);
    } catch {
      return previousFetch(input, init);
    }

    const method = String(init?.method || (input instanceof Request ? input.method : 'GET')).toUpperCase();
    if (method !== 'GET' || url.origin !== window.location.origin || url.pathname !== APPROVAL_COUNT_PATH) {
      return previousFetch(input, init);
    }

    const marker = `${Date.now()}-${++sequence}`;
    const requestInit = approvalRequestInit(input, init);
    const summaryResponse = await previousFetch(cacheBustedUrl(url.toString(), marker).toString(), requestInit);
    if (!summaryResponse.ok) return summaryResponse;

    const summaryPayload = await jsonPayload(summaryResponse);
    let access = null;

    try {
      const accessUrl = cacheBustedUrl(APPROVAL_ACCESS_PATH, marker).toString();
      const accessResponse = await previousFetch(accessUrl, requestInit);
      const accessPayload = await jsonPayload(accessResponse);

      if (accessResponse.status === 401 || accessResponse.status === 403) {
        return responseWithJson(accessResponse, accessPayload, accessResponse.status);
      }

      if (accessResponse.ok) access = normalizeApprovalAccess(accessPayload);
    } catch {
      // Fall back to the summary's embedded access contract below.
    }

    access ??= normalizeApprovalAccess(summaryPayload);
    if (!access) return summaryResponse;

    return responseWithJson(summaryResponse, {
      ...summaryPayload,
      access
    });
  };

  window.fetch.__projectPulseApprovalAccessCompatibility = true;
  window.__projectPulseApprovalAccessCompatibilityInstalled = true;
}

function currentRoute() {
  return cleanText(window.location.hash || '#dashboard').replace(/^#/, '') || 'dashboard';
}

function navigationLabelForRoute(route) {
  const expectedHref = `#${route}`;
  const links = Array.from(document.querySelectorAll(
    '.enterprise-top-navigation a[href^="#"], .enterprise-sidebar a[href^="#"]'
  ));

  const link = links.find((candidate) => (
    candidate.getAttribute('href') === expectedHref
    && candidate.id !== 'projectpulse-modules-navigation-link'
  ));

  return cleanText(link?.querySelector('.enterprise-nav-label')?.textContent || link?.textContent);
}

function pageContextLabel() {
  return cleanText(document.querySelector('.page-context-guide summary strong')?.textContent);
}

function restoreWorkspaceTitle() {
  const route = currentRoute();
  if (route === MODULES_ROUTE) return;

  const heading = document.querySelector('.workspace-header-context h1');
  if (!heading || cleanText(heading.textContent) !== 'Modules') return;

  const label = navigationLabelForRoute(route) || pageContextLabel();
  if (label && label !== 'Modules') heading.textContent = label;
}

function installWorkspaceTitleCompatibility() {
  if (typeof window === 'undefined' || typeof document === 'undefined') return;
  if (window.__projectPulseWorkspaceTitleCompatibilityInstalled) return;

  let timer = 0;
  const schedule = () => {
    window.clearTimeout(timer);
    timer = window.setTimeout(restoreWorkspaceTitle, 40);
  };

  window.addEventListener('hashchange', schedule);
  window.addEventListener('projectpulse:view-as-changed', schedule);

  const root = document.getElementById('root');
  const observer = root ? new MutationObserver(schedule) : null;
  observer?.observe(root, {
    childList: true,
    subtree: true,
    characterData: true,
    attributes: true,
    attributeFilter: ['class']
  });

  schedule();
  window.setTimeout(schedule, 250);
  window.setTimeout(schedule, 1000);
  window.__projectPulseWorkspaceTitleCompatibilityInstalled = true;
}

installApprovalAccessCompatibility();
installWorkspaceTitleCompatibility();
