import { authoritativeApi } from './projectpulse-authoritative-api.js';

const MARKER = '__projectPulseRuntimeDataCompatibilityInstalled';
const DIRECT_ROLE_POLICY_MARKER = 'projectpulse-role-policy-direct-fetch-v3';
const ROLE_POLICY_SESSION_WAIT_MS = 3500;

const LEGACY_TO_RUNTIME_PATH = Object.freeze({
  '/api/role-policy/summary': '/api/runtime/v2/role-policy/summary',
  '/api/runtime/role-policy/summary': '/api/runtime/v2/role-policy/summary',
  '/api/role-policy/catalog': '/api/runtime/v2/role-policy/catalog',
  '/api/runtime/role-policy/catalog': '/api/runtime/v2/role-policy/catalog',
  '/api/role-policy/versions': '/api/runtime/v2/role-policy/versions',
  '/api/runtime/role-policy/versions': '/api/runtime/v2/role-policy/versions',
  '/api/role-policy/matrix': '/api/runtime/v2/role-policy/matrix',
  '/api/runtime/role-policy/matrix': '/api/runtime/v2/role-policy/matrix'
});

const RUNTIME_TO_LEGACY_PATH = Object.freeze({
  '/api/runtime/v2/role-policy/summary': '/api/role-policy/summary',
  '/api/runtime/v2/role-policy/catalog': '/api/role-policy/catalog',
  '/api/runtime/v2/role-policy/versions': '/api/role-policy/versions',
  '/api/runtime/v2/role-policy/matrix': '/api/role-policy/matrix'
});

function requestMethod(input, init) {
  return String(init?.method || (input instanceof Request ? input.method : 'GET')).toUpperCase();
}

function rewritePtcPath(pathname) {
  const exact = {
    '/api/runtime/timesheet/steward/users': '/api/timesheet/ptc/users',
    '/api/runtime/v2/timesheet/steward/users': '/api/timesheet/ptc/users'
  };
  if (exact[pathname]) return exact[pathname];
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

function currentRoute() {
  return String(window.location.hash || '').replace(/^#/, '').split('?')[0];
}

function rolePolicyModuleNumber(pathname) {
  if (!pathname.includes('/role-policy/')) return '';
  if (pathname.endsWith('/matrix') || currentRoute() === 'roles-permissions-matrix') return '037';
  return '012';
}

function storedSession() {
  for (const storage of [window.localStorage, window.sessionStorage]) {
    for (const key of ['projectPulseAuthSession', 'ProjectPulseAuthSession', 'projectPulseSession']) {
      try {
        const session = JSON.parse(storage.getItem(key) || 'null');
        const token = session?.sessionToken || session?.token || session?.accessToken || session?.session_token || '';
        if (!token || (session?.expiresAt && Date.now() >= Date.parse(session.expiresAt))) continue;

        let viewAsUserId = '';
        try {
          const viewAs = JSON.parse(window.localStorage.getItem('projectPulseViewAsUser') || 'null');
          viewAsUserId = viewAs?.userId || window.localStorage.getItem('projectPulseViewAsUserId') || '';
        } catch {
          viewAsUserId = window.localStorage.getItem('projectPulseViewAsUserId') || '';
        }
        return { token, viewAsUserId };
      } catch {
        // Continue through the supported storage contracts.
      }
    }
  }
  return { token: '', viewAsUserId: '' };
}

function waitForSession(timeoutMs = ROLE_POLICY_SESSION_WAIT_MS) {
  const immediate = storedSession();
  if (immediate.token) return Promise.resolve(immediate);

  return new Promise((resolve) => {
    let completed = false;
    let timer = 0;
    const finish = () => {
      if (completed) return;
      completed = true;
      window.clearTimeout(timer);
      window.removeEventListener('projectpulse:auth-session-ready', check);
      window.removeEventListener('storage', check);
      resolve(storedSession());
    };
    const check = () => {
      if (storedSession().token) finish();
    };
    window.addEventListener('projectpulse:auth-session-ready', check);
    window.addEventListener('storage', check);
    timer = window.setTimeout(finish, Math.max(0, Number(timeoutMs || 0)));
  });
}

function findCaseInsensitive(source, key) {
  if (!source || typeof source !== 'object' || Array.isArray(source)) return undefined;
  const match = Object.keys(source).find((candidate) => candidate.toLowerCase() === key.toLowerCase());
  return match ? source[match] : undefined;
}

function objectCandidates(payload) {
  const queue = [payload];
  const seen = new Set();
  const candidates = [];
  while (queue.length && candidates.length < 24) {
    const current = queue.shift();
    if (!current || typeof current !== 'object' || Array.isArray(current) || seen.has(current)) continue;
    seen.add(current);
    candidates.push(current);
    for (const key of ['data', 'result', 'value', 'payload', 'response', 'body']) {
      const nested = findCaseInsensitive(current, key);
      if (nested && typeof nested === 'object' && !Array.isArray(nested)) queue.push(nested);
    }
  }
  return candidates;
}

function normalizeCollections(payload, collections) {
  if (Array.isArray(payload) && collections.length === 1) return { [collections[0]]: payload };
  const candidates = objectCandidates(payload);
  const selected = candidates.find((candidate) => collections.every((name) => Array.isArray(findCaseInsensitive(candidate, name))))
    || candidates[0]
    || {};
  const normalized = { ...selected };
  for (const name of collections) {
    const value = findCaseInsensitive(selected, name);
    if (Array.isArray(value)) normalized[name] = value;
  }
  return normalized;
}

function hasCollections(payload, collections) {
  return collections.every((name) => Array.isArray(payload?.[name]));
}

async function parseResponse(response) {
  const raw = await response.clone().text();
  if (!raw.trim()) return { raw, payload: {} };
  try {
    return { raw, payload: JSON.parse(raw) };
  } catch {
    return { raw, payload: {} };
  }
}

function sessionHeaders(init, session, moduleNumber) {
  const headers = new Headers(init?.headers || {});
  headers.set('Accept', 'application/json');
  headers.set('Cache-Control', 'no-cache, no-store, max-age=0');
  headers.set('Pragma', 'no-cache');
  headers.set('X-ProjectPulse-Session', session.token);
  headers.set('X-Project-Pulse-Session', session.token);
  headers.set('X-Session-Token', session.token);
  headers.set('Authorization', `Bearer ${session.token}`);
  headers.set('X-ProjectPulse-Role-Policy-Client', DIRECT_ROLE_POLICY_MARKER);
  if (moduleNumber) headers.set('X-ProjectPulse-Module-Number', moduleNumber);
  if (session.viewAsUserId) headers.set('X-ProjectPulse-View-As-User', session.viewAsUserId);
  return headers;
}

function responseFromPayload(response, payload, sourcePath) {
  const headers = new Headers(response.headers);
  headers.delete('content-length');
  headers.delete('content-encoding');
  headers.set('content-type', 'application/json; charset=utf-8');
  headers.set('cache-control', 'no-store');
  headers.set('x-projectpulse-role-policy-transport', DIRECT_ROLE_POLICY_MARKER);
  headers.set('x-projectpulse-role-policy-source', sourcePath);
  return new Response(JSON.stringify(payload), {
    status: response.status,
    statusText: response.statusText,
    headers
  });
}

function rolePolicyCandidatePaths(pathname) {
  const runtimeRole = pathname.match(/^\/api\/runtime\/v2\/role-policy\/roles\/(.+)$/);
  const legacyRole = pathname.match(/^\/api\/(?:runtime\/)?role-policy\/roles\/(.+)$/);
  if (runtimeRole) {
    return [`/api/role-policy/roles/${runtimeRole[1]}`, pathname];
  }
  if (legacyRole) {
    return [`/api/role-policy/roles/${legacyRole[1]}`, `/api/runtime/v2/role-policy/roles/${legacyRole[1]}`];
  }

  const legacyPath = RUNTIME_TO_LEGACY_PATH[pathname] || pathname;
  const runtimePath = LEGACY_TO_RUNTIME_PATH[legacyPath] || pathname;
  return [...new Set([legacyPath, runtimePath])];
}

async function fetchRolePolicyCandidate(previousFetch, path, search, init, session, moduleNumber, collections) {
  const response = await previousFetch(`${path}${search}`, {
    ...init,
    method: 'GET',
    cache: 'no-store',
    credentials: 'include',
    headers: sessionHeaders(init, session, moduleNumber)
  });
  const { raw, payload } = await parseResponse(response);
  const normalized = normalizeCollections(payload, collections);
  return {
    response,
    raw,
    payload: normalized,
    valid: response.ok && hasCollections(normalized, collections)
  };
}

async function directRolePolicyResponse(previousFetch, originalUrl, init) {
  const session = await waitForSession();
  if (!session.token) {
    return new Response(JSON.stringify({
      status: 'session_not_ready',
      message: 'Pulse session is not ready for role-policy data.'
    }), { status: 425, headers: { 'Content-Type': 'application/json' } });
  }

  const collections = expectedCollections(originalUrl.pathname);
  const moduleNumber = rolePolicyModuleNumber(originalUrl.pathname);
  const attempts = [];
  let lastCandidate = null;

  for (const path of rolePolicyCandidatePaths(originalUrl.pathname)) {
    const candidate = await fetchRolePolicyCandidate(
      previousFetch,
      path,
      originalUrl.search,
      init,
      session,
      moduleNumber,
      collections
    );
    lastCandidate = candidate;
    attempts.push({
      path,
      status: candidate.response.status,
      responseKeys: Object.keys(candidate.payload || {}),
      responsePreview: String(candidate.raw || '').slice(0, 180)
    });

    if (candidate.valid) return responseFromPayload(candidate.response, candidate.payload, path);
    if ([400, 401, 403, 409, 422].includes(candidate.response.status)) {
      return responseFromPayload(candidate.response, candidate.payload, path);
    }
  }

  if (lastCandidate && !lastCandidate.response.ok) {
    return responseFromPayload(lastCandidate.response, lastCandidate.payload, attempts.at(-1)?.path || originalUrl.pathname);
  }

  return new Response(JSON.stringify({
    status: 'role_policy_contract_mismatch',
    message: `Role-policy data did not contain required collections: ${collections.join(', ')}.`,
    requiredCollections: collections,
    moduleNumber,
    attempts
  }), {
    status: 502,
    headers: {
      'Content-Type': 'application/json',
      'Cache-Control': 'no-store',
      'X-ProjectPulse-Role-Policy-Transport': DIRECT_ROLE_POLICY_MARKER
    }
  });
}

function looksLikeRequestTask(task = {}) {
  const text = [task.groupLabel, task.taskCode, task.taskName, task.workTaskCategory, task.workType, task.serviceRequestNumber]
    .filter(Boolean)
    .join(' ')
    .toLowerCase();
  return Boolean(task.serviceRequestNumber || /service\s*request|\brequest\b|ticket|incident|case/.test(text));
}

function categoryKey(category = {}) {
  return String(category.nonProjectTimeCategoryId || category.nonProjectCategoryId || category.categoryId || category.id || category.categoryCode || category.code || category.categoryName || category.name || '');
}

function normalizePtcWorkspace(payload) {
  const assignments = Array.isArray(payload?.assignments)
    ? payload.assignments.map((assignment) => ({
        ...assignment,
        groupLabel: looksLikeRequestTask(assignment) ? 'Requests / Service Requests' : 'Project Tasks',
        selectionLabel: assignment.selectionLabel || [assignment.customerName, assignment.projectCode, assignment.taskCode, assignment.taskName].filter(Boolean).join(' · ')
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

function ptcResponse(payload, status, runtimePath) {
  return new Response(JSON.stringify(payload), {
    status,
    headers: {
      'Content-Type': 'application/json; charset=utf-8',
      'Cache-Control': 'no-store',
      'X-ProjectPulse-Runtime-Data': 'projectpulse-ptc-runtime-compatibility-v2',
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

    const isRolePolicy = Boolean(
      LEGACY_TO_RUNTIME_PATH[originalUrl.pathname]
      || RUNTIME_TO_LEGACY_PATH[originalUrl.pathname]
      || /^\/api\/(?:runtime\/(?:v2\/)?)?role-policy\/roles\/[^/]+$/.test(originalUrl.pathname)
    );
    if (isRolePolicy) return directRolePolicyResponse(previousFetch, originalUrl, init);

    const rewrittenPath = rewritePtcPath(originalUrl.pathname);
    if (!rewrittenPath) return previousFetch(input, init);

    const rewrittenUrl = new URL(originalUrl.toString());
    rewrittenUrl.pathname = rewrittenPath;
    const runtimePath = `${rewrittenUrl.pathname}${rewrittenUrl.search}`;

    try {
      let payload = await authoritativeApi(runtimePath, {
        method: 'GET',
        requiredCollections: expectedCollections(rewrittenPath),
        moduleNumber: '001',
        sessionWaitMs: ROLE_POLICY_SESSION_WAIT_MS
      });
      if (rewrittenPath.endsWith('/entries')) payload = normalizePtcWorkspace(payload);
      publishRuntimeData(rewrittenPath, payload);
      return ptcResponse(payload, 200, rewrittenPath);
    } catch (error) {
      const status = Number(error?.status || 502);
      return ptcResponse({
        status: error?.payload?.status || 'authoritative_runtime_request_failed',
        message: error?.message || `The authoritative request for ${rewrittenPath} failed.`,
        requestedPath: originalUrl.pathname,
        runtimePath: rewrittenPath,
        moduleNumber: error?.diagnostic?.moduleNumber || '001',
        responseKeys: error?.diagnostic?.responseKeys || Object.keys(error?.payload || {}),
        diagnostic: error?.diagnostic || null
      }, status >= 400 && status <= 599 ? status : 502, rewrittenPath);
    }
  };

  window[MARKER] = true;
}
