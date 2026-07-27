import { authoritativeApi } from './projectpulse-authoritative-api.js';

const MARKER = '__projectPulseRuntimeDataCompatibilityInstalled';
const RESPONSE_MARKER = 'projectpulse-authoritative-xhr-compatibility-v2';
const ROLE_POLICY_SESSION_WAIT_MS = 3500;

function requestMethod(input, init) {
  return String(init?.method || (input instanceof Request ? input.method : 'GET')).toUpperCase();
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

function expectedCollections(pathname) {
  if (pathname.endsWith('/summary')) return ['roles', 'modules'];
  if (pathname.endsWith('/catalog')) return ['actions', 'scopes'];
  if (pathname.endsWith('/versions')) return ['versions'];
  if (pathname.endsWith('/matrix')) return ['roles', 'modules', 'grants'];
  if (pathname.includes('/role-policy/roles/')) return ['assignedUsers'];
  if (pathname.endsWith('/users')) return ['users'];
  if (pathname.endsWith('/entries')) return ['assignments'];
  return [];
}

function rolePolicyModuleNumber(pathname) {
  if (!pathname.includes('/role-policy/')) return '';
  if (pathname.endsWith('/matrix')) return '037';
  if (
    pathname.endsWith('/summary')
    || pathname.endsWith('/versions')
    || pathname.includes('/role-policy/roles/')
  ) return '012';
  return '';
}

function rolePolicySessionWaitMs(pathname) {
  return pathname.includes('/role-policy/') ? ROLE_POLICY_SESSION_WAIT_MS : undefined;
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
  if (pathname.endsWith('/entries')) {
    window.__projectPulsePtcRuntimeWorkspace = payload;
    window.dispatchEvent(new CustomEvent('projectpulse:ptc-runtime-workspace', { detail: payload }));
  }
}

function responseFromPayload(payload, status, runtimePath) {
  return new Response(JSON.stringify(payload), {
    status,
    headers: {
      'Content-Type': 'application/json; charset=utf-8',
      'Cache-Control': 'no-store',
      'X-ProjectPulse-Runtime-Data': RESPONSE_MARKER,
      'X-ProjectPulse-Authoritative-Path': runtimePath
    }
  });
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
    const runtimePath = `${rewrittenUrl.pathname}${rewrittenUrl.search}`;
    const moduleNumber = rolePolicyModuleNumber(rewrittenPath);

    try {
      let payload = await authoritativeApi(runtimePath, {
        method: 'GET',
        requiredCollections: expectedCollections(rewrittenPath),
        moduleNumber,
        sessionWaitMs: rolePolicySessionWaitMs(rewrittenPath)
      });
      if (rewrittenPath.endsWith('/entries')) payload = normalizePtcWorkspace(payload);
      publishRuntimeData(rewrittenPath, payload);
      return responseFromPayload(payload, 200, rewrittenPath);
    } catch (error) {
      const status = Number(error?.status || 502);
      const payload = {
        status: error?.payload?.status || 'authoritative_runtime_request_failed',
        message: error?.message || `The authoritative request for ${rewrittenPath} failed.`,
        requestedPath: originalUrl.pathname,
        runtimePath: rewrittenPath,
        moduleNumber: error?.diagnostic?.moduleNumber || moduleNumber || '',
        responseKeys: error?.diagnostic?.responseKeys || Object.keys(error?.payload || {}),
        diagnostic: error?.diagnostic || null
      };
      return responseFromPayload(payload, status >= 400 && status <= 599 ? status : 502, rewrittenPath);
    }
  };

  window[MARKER] = true;
}
