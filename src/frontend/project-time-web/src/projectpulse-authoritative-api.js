import { currentProjectPulseRoute, moduleForRoute } from './module-availability-registry.js';

const DIAGNOSTIC_EVENT = 'projectpulse:authoritative-api-diagnostic';
const DIAGNOSTIC_MARKER = 'projectpulse-authoritative-xhr-v1';
const SESSION_NOT_READY_STATUS = 425;
const SESSION_WAIT_MS = 1200;
const SESSION_KEYS = Object.freeze([
  'projectPulseAuthSession',
  'ProjectPulseAuthSession',
  'projectPulseSession'
]);
const PUBLIC_API_PREFIXES = Object.freeze([
  '/health',
  '/api/auth/',
  '/api/public/',
  '/api/bootstrap/',
  '/api/app-config',
  '/api/config'
]);

function parseStoredJson(storage, key) {
  try {
    const raw = storage?.getItem(key);
    return raw ? JSON.parse(raw) : null;
  } catch {
    return null;
  }
}

function sessionTokenFromValue(session) {
  return session?.sessionToken
    || session?.token
    || session?.accessToken
    || session?.session_token
    || '';
}

function sessionIsExpired(session) {
  if (!session?.expiresAt) return false;
  const expiresAt = Date.parse(session.expiresAt);
  return Number.isFinite(expiresAt) && Date.now() >= expiresAt;
}

function storedSessionContext() {
  for (const storage of [window.localStorage, window.sessionStorage]) {
    for (const key of SESSION_KEYS) {
      const session = parseStoredJson(storage, key);
      const token = sessionTokenFromValue(session);
      if (token && !sessionIsExpired(session)) return { session, token, key, storage };
    }
  }

  return { session: null, token: '', key: '', storage: null };
}

function readViewAsUserId() {
  try {
    const selected = JSON.parse(window.localStorage.getItem('projectPulseViewAsUser') || 'null');
    return selected?.userId || window.localStorage.getItem('projectPulseViewAsUserId') || '';
  } catch {
    return '';
  }
}

function sessionContext() {
  const { session, token } = storedSessionContext();
  return {
    session,
    token,
    viewAsUserId: readViewAsUserId()
  };
}

function activeModuleNumber(explicitModuleNumber = '') {
  const explicit = String(explicitModuleNumber || '').trim();
  if (explicit) return explicit;
  try {
    return moduleForRoute(currentProjectPulseRoute())?.moduleNumber || '';
  } catch {
    return '';
  }
}

function unwrap(payload) {
  let current = payload && typeof payload === 'object' && !Array.isArray(payload) ? payload : {};
  for (let depth = 0; depth < 3; depth += 1) {
    const key = ['data', 'Data', 'result', 'Result', 'value', 'Value', 'payload', 'Payload']
      .find((candidate) => current?.[candidate] && typeof current[candidate] === 'object' && !Array.isArray(current[candidate]));
    if (!key) break;
    current = current[key];
  }
  return current;
}

function normalizeApiPath(input) {
  try {
    const raw = typeof input === 'string' ? input : input?.url;
    if (!raw) return '';
    const url = new URL(raw, window.location.origin);
    return url.origin === window.location.origin ? url.pathname : '';
  } catch {
    return '';
  }
}

function isPublicApiPath(path = '') {
  const normalized = String(path || '').toLowerCase();
  return PUBLIC_API_PREFIXES.some((prefix) => (
    normalized === prefix || normalized.startsWith(prefix)
  ));
}

function normalizeHeaderToken(value = '') {
  const text = String(value || '').trim();
  if (!text) return '';
  return text.replace(/^Bearer\s+/i, '').trim();
}

function requestSessionToken(input, init = {}) {
  try {
    const headers = new Headers(
      init?.headers || (input instanceof Request ? input.headers : undefined)
    );

    for (const name of [
      'X-ProjectPulse-Session',
      'X-Project-Pulse-Session',
      'X-Session-Token',
      'Authorization'
    ]) {
      const token = normalizeHeaderToken(headers.get(name));
      if (token) return token;
    }
  } catch {
    // Malformed header input is treated as an unauthenticated request.
  }

  return '';
}

function applySessionHeaders(headers, token) {
  if (!token) return headers;
  headers.set('X-ProjectPulse-Session', token);
  headers.set('X-Project-Pulse-Session', token);
  headers.set('X-Session-Token', token);
  headers.set('Authorization', `Bearer ${token}`);
  return headers;
}

function waitForUsableSession(timeoutMs = SESSION_WAIT_MS) {
  const immediate = sessionContext();
  if (immediate.token) return Promise.resolve(immediate);

  return new Promise((resolve) => {
    let finished = false;
    let timeoutId = null;

    const finish = () => {
      if (finished) return;
      finished = true;
      if (timeoutId) window.clearTimeout(timeoutId);
      window.removeEventListener('storage', handleSignal);
      window.removeEventListener('projectpulse:auth-session-ready', handleSignal);
      resolve(sessionContext());
    };

    const handleSignal = () => {
      if (sessionContext().token) finish();
    };

    window.addEventListener('storage', handleSignal);
    window.addEventListener('projectpulse:auth-session-ready', handleSignal);
    timeoutId = window.setTimeout(finish, Math.max(0, Number(timeoutMs || 0)));
  });
}

function createSessionNotReadyResponse(path) {
  return new Response(JSON.stringify({
    status: 'session_not_ready',
    message: 'ProjectPulse session is not ready yet.',
    path
  }), {
    status: SESSION_NOT_READY_STATUS,
    headers: {
      'Content-Type': 'application/json',
      'Cache-Control': 'no-store'
    }
  });
}

function installProtectedFetchReadinessGate() {
  if (typeof window === 'undefined' || window.__projectPulseProtectedFetchReadinessGateInstalled) return;

  const originalFetch = window.fetch.bind(window);
  window.fetch = async (input, init = {}) => {
    const path = normalizeApiPath(input);
    if (!path || isPublicApiPath(path)) return originalFetch(input, init);

    let token = requestSessionToken(input, init);
    if (!token) token = (await waitForUsableSession()).token;
    if (!token) return createSessionNotReadyResponse(path);

    const headers = applySessionHeaders(
      new Headers(init?.headers || (input instanceof Request ? input.headers : undefined)),
      token
    );

    return originalFetch(input, {
      ...init,
      headers
    });
  };

  window.__projectPulseProtectedFetchReadinessGateInstalled = true;
}

function shouldPublishError(diagnostic) {
  const key = `${diagnostic.path}|${diagnostic.status}|${diagnostic.message}`;
  const now = Date.now();
  const previous = window.__projectPulseAuthoritativeApiLastError;
  window.__projectPulseAuthoritativeApiLastError = { key, at: now };
  return !previous || previous.key !== key || now - previous.at >= 15000;
}

function publishDiagnostic(diagnostic) {
  if (typeof window === 'undefined') return;
  window.__projectPulseAuthoritativeApiDiagnostics = {
    ...(window.__projectPulseAuthoritativeApiDiagnostics || {}),
    [diagnostic.path]: diagnostic
  };
  window.dispatchEvent(new CustomEvent(DIAGNOSTIC_EVENT, { detail: diagnostic }));
  if (!diagnostic.ok && shouldPublishError(diagnostic)) {
    console.error('[ProjectPulse authoritative API]', diagnostic);
  }
}

function collectionMissing(payload, requiredCollections) {
  return requiredCollections.filter((name) => !Array.isArray(payload?.[name]));
}

function globalXhrSessionBridgeInstalled() {
  return Boolean(window.XMLHttpRequest?.prototype?.__projectPulse050BFinalWrapped);
}

function sessionNotReadyError(path) {
  const error = new Error('ProjectPulse session is not ready yet.');
  error.status = SESSION_NOT_READY_STATUS;
  error.code = 'session_not_ready';
  error.path = path;
  error.silent = true;
  return error;
}

export function authoritativeApiDiagnostics() {
  return { ...(window.__projectPulseAuthoritativeApiDiagnostics || {}) };
}

export async function authoritativeApi(path, options = {}) {
  const method = String(options.method || 'GET').toUpperCase();
  const requiredCollections = Array.isArray(options.requiredCollections) ? options.requiredCollections : [];
  const context = isPublicApiPath(path)
    ? sessionContext()
    : await waitForUsableSession(options.sessionWaitMs ?? SESSION_WAIT_MS);

  if (!isPublicApiPath(path) && !context.token) {
    throw sessionNotReadyError(path);
  }

  const { token, viewAsUserId } = context;
  const moduleNumber = activeModuleNumber(options.moduleNumber);
  const startedAt = Date.now();

  return await new Promise((resolve, reject) => {
    const request = new XMLHttpRequest();
    request.open(method, path, true);
    request.withCredentials = true;
    request.timeout = Number(options.timeoutMs || 60000);
    request.setRequestHeader('Accept', 'application/json');
    request.setRequestHeader('Cache-Control', 'no-cache, no-store, max-age=0');
    request.setRequestHeader('Pragma', 'no-cache');
    request.setRequestHeader('X-ProjectPulse-Authoritative-Client', DIAGNOSTIC_MARKER);
    if (moduleNumber) request.setRequestHeader('X-ProjectPulse-Module-Number', moduleNumber);
    if (options.body != null) request.setRequestHeader('Content-Type', 'application/json');

    // App.jsx installs the single global XHR session bridge before React effects run.
    // Directly applying the same headers here would append duplicate values and make
    // an otherwise valid token fail backend validation. Only provide a fallback when
    // the global bridge is genuinely unavailable.
    if (token && !globalXhrSessionBridgeInstalled()) {
      request.setRequestHeader('Authorization', `Bearer ${token}`);
      request.setRequestHeader('X-ProjectPulse-Session', token);
      request.setRequestHeader('X-Project-Pulse-Session', token);
      request.setRequestHeader('X-Session-Token', token);
    }

    if (viewAsUserId) request.setRequestHeader('X-ProjectPulse-View-As-User', viewAsUserId);
    for (const [name, value] of Object.entries(options.headers || {})) {
      if (value != null) request.setRequestHeader(name, String(value));
    }

    const finishError = (message, status = 0, payload = null, responseText = '') => {
      const normalized = payload && typeof payload === 'object' ? payload : {};
      const diagnostic = {
        marker: DIAGNOSTIC_MARKER,
        ok: false,
        method,
        path,
        moduleNumber,
        status,
        durationMs: Date.now() - startedAt,
        responseKeys: Object.keys(normalized),
        requiredCollections,
        message,
        responsePreview: String(responseText || '').slice(0, 240),
        at: new Date().toISOString()
      };
      publishDiagnostic(diagnostic);
      const error = new Error(message);
      error.status = status;
      error.payload = normalized;
      error.diagnostic = diagnostic;
      reject(error);
    };

    request.onload = () => {
      const raw = request.responseText || '';
      let payload;
      try {
        payload = raw ? JSON.parse(raw) : {};
      } catch {
        finishError(`${path} returned non-JSON content instead of ProjectPulse API data.`, request.status, null, raw);
        return;
      }
      payload = unwrap(payload);
      if (request.status < 200 || request.status >= 300) {
        finishError(
          payload.message || payload.Message || payload.detail || payload.Detail || `${path} returned HTTP ${request.status}.`,
          request.status,
          payload,
          raw
        );
        return;
      }
      const missingCollections = collectionMissing(payload, requiredCollections);
      if (missingCollections.length) {
        finishError(
          `The authoritative response for ${path} did not contain required collections: ${missingCollections.join(', ')}.`,
          request.status,
          payload,
          raw
        );
        return;
      }
      const diagnostic = {
        marker: DIAGNOSTIC_MARKER,
        ok: true,
        method,
        path,
        moduleNumber,
        status: request.status,
        durationMs: Date.now() - startedAt,
        responseKeys: Object.keys(payload || {}),
        collectionCounts: Object.fromEntries(requiredCollections.map((name) => [name, payload[name].length])),
        at: new Date().toISOString()
      };
      publishDiagnostic(diagnostic);
      resolve(payload);
    };

    request.onerror = () => finishError(`${path} could not be reached.`, request.status || 0, null, request.responseText || '');
    request.ontimeout = () => finishError(`${path} timed out.`, request.status || 0, null, request.responseText || '');
    request.onabort = () => finishError(`${path} was cancelled.`, request.status || 0, null, request.responseText || '');
    request.send(options.body == null ? null : options.body);
  });
}

if (typeof window !== 'undefined' && typeof window.fetch === 'function') {
  installProtectedFetchReadinessGate();
}
