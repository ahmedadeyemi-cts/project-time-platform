import { unwrapApiPayload } from './api-json-response.js';

const MARKER = '__projectPulseRuntimeDataCompatibilityInstalled';
const RESPONSE_MARKER = 'projectpulse-runtime-data-2026-07-25';

function requestMethod(input, init) {
  return String(init?.method || (input instanceof Request ? input.method : 'GET')).toUpperCase();
}

function rewritePath(pathname) {
  if (pathname === '/api/role-policy/summary') return '/api/runtime/role-policy/summary';
  if (pathname === '/api/role-policy/catalog') return '/api/runtime/role-policy/catalog';
  if (pathname === '/api/role-policy/versions') return '/api/runtime/role-policy/versions';
  if (pathname === '/api/role-policy/matrix') return '/api/runtime/role-policy/matrix';
  if (/^\/api\/role-policy\/roles\/[^/]+$/.test(pathname)) {
    return pathname.replace('/api/role-policy/roles/', '/api/runtime/role-policy/roles/');
  }
  if (pathname === '/api/timesheet/ptc/users') return '/api/runtime/timesheet/steward/users';
  if (/^\/api\/timesheet\/ptc\/users\/[0-9a-f-]+\/entries$/i.test(pathname)) {
    return pathname
      .replace('/api/timesheet/ptc/users/', '/api/runtime/timesheet/steward/users/')
      .replace(/\/entries$/, '/workspace');
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
  if (pathname.endsWith('/workspace')) return ['user', 'User', 'assignments', 'Assignments'];
  return [];
}

function publishRuntimeData(pathname, payload) {
  if (pathname.endsWith('/users')) {
    window.__projectPulsePtcRuntimeUsers = payload;
    window.dispatchEvent(new CustomEvent('projectpulse:ptc-runtime-users', { detail: payload }));
  }
  if (pathname.endsWith('/workspace')) {
    window.__projectPulsePtcRuntimeWorkspace = payload;
    window.dispatchEvent(new CustomEvent('projectpulse:ptc-runtime-workspace', { detail: payload }));
  }
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
    const response = await previousFetch(`${rewrittenUrl.pathname}${rewrittenUrl.search}`, init);
    const raw = await response.text();
    const headers = new Headers(response.headers);
    headers.delete('content-length');
    headers.delete('content-encoding');
    headers.set('content-type', 'application/json; charset=utf-8');
    headers.set('x-projectpulse-runtime-data', RESPONSE_MARKER);

    let parsed;
    try {
      parsed = raw ? JSON.parse(raw) : {};
    } catch {
      return new Response(JSON.stringify({
        status: 'runtime_api_non_json_response',
        message: 'The ProjectPulse API returned web content instead of JSON. Refresh after the API deployment completes.',
        requestedPath: originalUrl.pathname,
        runtimePath: rewrittenPath
      }), { status: 502, headers });
    }

    const normalized = unwrapApiPayload(parsed, expectedKeys(rewrittenPath));
    if (response.ok) publishRuntimeData(rewrittenPath, normalized);
    return new Response(JSON.stringify(normalized), {
      status: response.status,
      statusText: response.statusText,
      headers
    });
  };

  window[MARKER] = true;
}
