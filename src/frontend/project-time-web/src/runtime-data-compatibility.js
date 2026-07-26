import { unwrapApiPayload } from './api-json-response.js';

const MARKER = '__projectPulseRuntimeDataCompatibilityInstalled';
const RESPONSE_MARKER = 'projectpulse-critical-runtime-direct-2026-07-26';

function requestMethod(input, init) {
  return String(init?.method || (input instanceof Request ? input.method : 'GET')).toUpperCase();
}

function sessionToken() {
  try {
    const session = JSON.parse(window.localStorage.getItem('projectPulseAuthSession') || 'null');
    return session?.sessionToken || session?.token || session?.accessToken || '';
  } catch {
    return '';
  }
}

function viewAsUserId() {
  try {
    const selected = JSON.parse(window.localStorage.getItem('projectPulseViewAsUser') || 'null');
    return selected?.userId || window.localStorage.getItem('projectPulseViewAsUserId') || '';
  } catch {
    return window.localStorage.getItem('projectPulseViewAsUserId') || '';
  }
}

function authenticatedInit(input, init = {}) {
  const token = sessionToken();
  const viewAs = viewAsUserId();
  const headers = new Headers(init?.headers || (input instanceof Request ? input.headers : undefined));
  if (token) {
    if (!headers.has('Authorization')) headers.set('Authorization', `Bearer ${token}`);
    if (!headers.has('X-ProjectPulse-Session')) headers.set('X-ProjectPulse-Session', token);
    if (!headers.has('X-Project-Pulse-Session')) headers.set('X-Project-Pulse-Session', token);
    if (!headers.has('X-Session-Token')) headers.set('X-Session-Token', token);
  }
  if (viewAs && !headers.has('X-ProjectPulse-View-As-User')) {
    headers.set('X-ProjectPulse-View-As-User', viewAs);
  }
  headers.set('Cache-Control', 'no-cache');
  headers.set('Pragma', 'no-cache');
  return { ...init, credentials: 'include', cache: 'no-store', headers };
}

function rewritePath(pathname) {
  const exact = {
    '/api/role-policy/summary': '/api/runtime/v2/role-policy/summary',
    '/api/runtime/role-policy/summary': '/api/runtime/v2/role-policy/summary',
    '/api/role-policy/catalog': '/api/runtime/v2/role-policy/catalog',
    '/api/runtime/role-policy/catalog': '/api/runtime/v2/role-policy/catalog',
    '/api/role-policy/versions': '/api/runtime/v2/role-policy/versions',
    '/api/runtime/role-policy/versions': '/api/runtime/v2/role-policy/versions',
    '/api/role-policy/matrix': '/api/runtime/v2/role-policy/matrix',
    '/api/runtime/role-policy/matrix': '/api/runtime/v2/role-policy/matrix',
    '/api/runtime/timesheet/steward/users': '/api/timesheet/ptc/users',
    '/api/runtime/v2/timesheet/steward/users': '/api/timesheet/ptc/users'
  };
  if (exact[pathname]) return exact[pathname];
  if (/^\/api\/(?:runtime\/)?role-policy\/roles\/[^/]+$/.test(pathname)) {
    return pathname
      .replace('/api/runtime/role-policy/roles/', '/api/runtime/v2/role-policy/roles/')
      .replace('/api/role-policy/roles/', '/api/runtime/v2/role-policy/roles/');
  }
  if (/^\/api\/runtime\/(?:v2\/)?timesheet\/steward\/users\/[0-9a-f-]+\/workspace$/i.test(pathname)) {
    return pathname
      .replace('/api/runtime/v2/timesheet/steward/users/', '/api/timesheet/ptc/users/')
      .replace('/api/runtime/timesheet/steward/users/', '/api/timesheet/ptc/users/')
      .replace(/\/workspace$/, '/entries');
  }
  return '';
}

function expectedKeys(pathname) {
  if (pathname.endsWith('/summary')) return ['roles', 'Roles', 'modules', 'Modules'];
  if (pathname.endsWith('/catalog')) return ['actions', 'Actions', 'scopes', 'Scopes'];
  if (pathname.endsWith('/versions')) return ['versions', 'Versions'];
  if (pathname.endsWith('/matrix')) return ['roles', 'Roles', 'modules', 'Modules', 'grants', 'Grants'];
  if (pathname.includes('/role-policy/roles/')) return ['role', 'Role', 'assignedUsers', 'AssignedUsers'];
  if (pathname.endsWith('/users')) return ['users', 'Users'];
  if (pathname.endsWith('/workspace') || pathname.endsWith('/entries')) return ['user', 'User', 'assignments', 'Assignments'];
  return [];
}

function hasExpected(payload, keys) {
  if (!keys.length) return true;
  return keys.some((key) => Object.prototype.hasOwnProperty.call(payload || {}, key));
}

function looksLikeRequestTask(task = {}) {
  const text = [
    task.groupLabel,
    task.taskCode,
    task.taskName,
    task.workTaskCategory,
    task.workType,
    task.serviceRequestNumber
  ].filter(Boolean).join(' ').toLowerCase();
  return Boolean(task.serviceRequestNumber || /service\s*request|\brequest\b|ticket|incident|case/.test(text));
}

function categoryKey(category = {}) {
  return String(
    category.nonProjectTimeCategoryId
    || category.nonProjectCategoryId
    || category.categoryId
    || category.id
    || category.categoryCode
    || category.code
    || category.categoryName
    || category.name
    || ''
  );
}

function normalizePtcWorkspace(payload) {
  const assignments = Array.isArray(payload?.assignments)
    ? payload.assignments.map((assignment) => ({
        ...assignment,
        groupLabel: looksLikeRequestTask(assignment) ? 'Requests / Service Requests' : 'Project Tasks',
        selectionLabel: assignment.selectionLabel
          || [assignment.customerName, assignment.projectCode, assignment.taskCode, assignment.taskName].filter(Boolean).join(' · ')
      }))
    : [];

  const providedCategories = Array.isArray(payload?.nonProjectCategories) ? payload.nonProjectCategories : [];
  const snapshotCategories = Array.isArray(window.__projectPulseModule001Snapshot?.nonProjectCategories)
    ? window.__projectPulseModule001Snapshot.nonProjectCategories
    : [];
  const categoryMap = new Map();
  for (const category of [...snapshotCategories, ...providedCategories]) {
    const key = categoryKey(category);
    if (!key) continue;
    categoryMap.set(key, {
      ...category,
      targetType: 'category',
      groupLabel: 'Non-Project Time',
      nonProjectTimeCategoryId: category.nonProjectTimeCategoryId || category.nonProjectCategoryId || category.categoryId || category.id || null,
      categoryCode: category.categoryCode || category.code || '',
      categoryName: category.categoryName || category.name || category.categoryCode || category.code || 'Non-project activity',
      selectionLabel: category.selectionLabel || category.categoryName || category.name || category.categoryCode || category.code || 'Non-project activity'
    });
  }

  return {
    ...payload,
    assignments,
    nonProjectCategories: [...categoryMap.values()],
    allActiveUsersAllowed: true
  };
}

function publishRuntimeData(pathname, payload) {
  if (pathname.endsWith('/users')) {
    window.__projectPulsePtcRuntimeUsers = payload;
    window.dispatchEvent(new CustomEvent('projectpulse:ptc-runtime-users', { detail: payload }));
  }
  if (pathname.endsWith('/workspace') || pathname.endsWith('/entries')) {
    window.__projectPulsePtcRuntimeWorkspace = payload;
    window.dispatchEvent(new CustomEvent('projectpulse:ptc-runtime-workspace', { detail: payload }));
  }
}

function directTransport(previousFetch) {
  return typeof window.__projectPulseOriginalFetch === 'function'
    ? window.__projectPulseOriginalFetch.bind(window)
    : previousFetch;
}

if (typeof window !== 'undefined' && typeof window.fetch === 'function' && !window[MARKER]) {
  const previousFetch = window.fetch.bind(window);

  window.fetch = async function projectPulseRuntimeDataCompatibility(input, init = {}) {
    if (requestMethod(input, init) !== 'GET') return previousFetch(input, init);

    let originalUrl;
    try {
      originalUrl = new URL(input instanceof Request ? input.url : String(input), window.location.origin);
    } catch {
      return previousFetch(input, init);
    }

    if (originalUrl.origin !== window.location.origin) return previousFetch(input, init);
    const rewrittenPath = rewritePath(originalUrl.pathname);
    if (!rewrittenPath) return previousFetch(input, init);

    const rewrittenUrl = new URL(originalUrl.toString());
    rewrittenUrl.pathname = rewrittenPath;
    const response = await directTransport(previousFetch)(
      `${rewrittenUrl.pathname}${rewrittenUrl.search}`,
      authenticatedInit(input, init)
    );
    const raw = await response.text();
    const headers = new Headers(response.headers);
    headers.delete('content-length');
    headers.delete('content-encoding');
    headers.set('content-type', 'application/json; charset=utf-8');
    headers.set('x-projectpulse-runtime-data', RESPONSE_MARKER);
    headers.set('x-projectpulse-authoritative-path', rewrittenPath);

    let parsed;
    try {
      parsed = raw ? JSON.parse(raw) : {};
    } catch {
      return new Response(JSON.stringify({
        status: 'runtime_api_non_json_response',
        message: 'The ProjectPulse API returned web content instead of JSON.',
        requestedPath: originalUrl.pathname,
        runtimePath: rewrittenPath,
        responsePreview: raw.slice(0, 160)
      }), { status: 502, headers });
    }

    const keys = expectedKeys(rewrittenPath);
    let normalized = unwrapApiPayload(parsed, keys);
    if (rewrittenPath.endsWith('/entries')) normalized = normalizePtcWorkspace(normalized);
    if (response.ok && !hasExpected(normalized, keys)) {
      return new Response(JSON.stringify({
        status: 'runtime_api_contract_incomplete',
        message: `The direct authoritative response for ${rewrittenPath} did not contain ${keys.join(', ')}.`,
        requestedPath: originalUrl.pathname,
        runtimePath: rewrittenPath,
        responseKeys: Object.keys(normalized || {})
      }), { status: 502, headers });
    }

    if (response.ok) publishRuntimeData(rewrittenPath, normalized);
    return new Response(JSON.stringify(normalized), {
      status: response.status,
      statusText: response.statusText,
      headers
    });
  };

  window[MARKER] = true;
}
