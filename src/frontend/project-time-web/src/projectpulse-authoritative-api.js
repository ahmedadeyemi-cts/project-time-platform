import { currentProjectPulseRoute, moduleForRoute } from './module-availability-registry.js';

const DIAGNOSTIC_EVENT = 'projectpulse:authoritative-api-diagnostic';
const DIAGNOSTIC_MARKER = 'projectpulse-authoritative-xhr-v1';
const SESSION_INVALIDATED_EVENT = 'projectpulse:session-invalidated';
const SESSION_INVALIDATION_MARKER = 'projectpulse-authoritative-session-invalidation-v1';
const SESSION_KEYS = Object.freeze([
  'projectPulseAuthSession',
  'ProjectPulseAuthSession',
  'projectPulseSession'
]);
const VIEW_AS_KEYS = Object.freeze([
  'projectPulseViewAsUser',
  'projectPulseViewAsUserId'
]);
const SESSION_REJECTION_STATUS_CODES = new Set([
  'session_required',
  'session_expired',
  'session_invalid',
  'invalid_session'
]);
const SESSION_REJECTION_MESSAGE = /(?:session|token).*(?:expired|invalid|missing|required|could not be verified)|missing session token|sign in again/i;

function parseStoredJson(storage, key) {
  try {
    const raw = storage?.getItem(key);
    return raw ? JSON.parse(raw) : null;
  } catch {
    return null;
  }
}

function storedSessionContext() {
  for (const storage of [window.localStorage, window.sessionStorage]) {
    for (const key of SESSION_KEYS) {
      const session = parseStoredJson(storage, key);
      const token = session?.sessionToken
        || session?.token
        || session?.accessToken
        || session?.session_token
        || '';
      if (token) return { session, token, key, storage };
    }
  }

  return { session: null, token: '', key: '', storage: null };
}

function sessionContext() {
  const { token } = storedSessionContext();
  try {
    const selected = JSON.parse(window.localStorage.getItem('projectPulseViewAsUser') || 'null');
    return {
      token,
      viewAsUserId: selected?.userId || window.localStorage.getItem('projectPulseViewAsUserId') || ''
    };
  } catch {
    return { token, viewAsUserId: '' };
  }
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

function isSessionRejection(status, payload = {}, responseText = '') {
  if (Number(status) !== 401 || !storedSessionContext().token) return false;

  const normalized = unwrap(payload);
  const statusCode = String(
    normalized?.status
      || normalized?.Status
      || normalized?.code
      || normalized?.Code
      || ''
  ).trim().toLowerCase();
  const message = String(
    normalized?.message
      || normalized?.Message
      || normalized?.detail
      || normalized?.Detail
      || responseText
      || ''
  ).trim();

  return SESSION_REJECTION_STATUS_CODES.has(statusCode)
    || SESSION_REJECTION_MESSAGE.test(message);
}

function clearSessionStorage() {
  for (const storage of [window.localStorage, window.sessionStorage]) {
    for (const key of [...SESSION_KEYS, ...VIEW_AS_KEYS]) {
      try {
        storage.removeItem(key);
      } catch {
        // Continue clearing every supported storage location.
      }
    }
  }
}

function invalidateProjectPulseSession(path, payload = {}, responseText = '') {
  if (!storedSessionContext().token) return false;
  if (window.__projectPulseSessionInvalidationStarted) return true;

  window.__projectPulseSessionInvalidationStarted = true;
  const detail = {
    marker: SESSION_INVALIDATION_MARKER,
    path,
    message: String(payload?.message || payload?.Message || responseText || 'Session expired or invalid.'),
    at: new Date().toISOString()
  };

  try {
    window.sessionStorage.setItem('projectPulseSessionInvalidatedAt', detail.at);
  } catch {
    // Session invalidation still works when auxiliary storage is unavailable.
  }

  clearSessionStorage();
  window.dispatchEvent(new CustomEvent(SESSION_INVALIDATED_EVENT, { detail }));

  window.setTimeout(() => {
    window.location.hash = '#dashboard';
    window.location.reload();
  }, 0);

  return true;
}

async function inspectFetchSessionRejection(input, response) {
  const path = normalizeApiPath(input);
  if (!path || response?.status !== 401 || !storedSessionContext().token) return;

  let payload = {};
  let raw = '';
  try {
    raw = await response.clone().text();
    payload = raw ? JSON.parse(raw) : {};
  } catch {
    payload = {};
  }

  if (isSessionRejection(response.status, payload, raw)) {
    invalidateProjectPulseSession(path, payload, raw);
  }
}

function installGlobalFetchSessionInvalidation() {
  if (typeof window === 'undefined' || window.__projectPulseGlobalSessionInvalidationInstalled) return;

  const originalFetch = window.fetch.bind(window);
  window.fetch = async (input, init = {}) => {
    const response = await originalFetch(input, init);
    void inspectFetchSessionRejection(input, response);
    return response;
  };

  window.__projectPulseGlobalSessionInvalidationInstalled = true;
}

function publishDiagnostic(diagnostic) {
  if (typeof window === 'undefined') return;
  window.__projectPulseAuthoritativeApiDiagnostics = {
    ...(window.__projectPulseAuthoritativeApiDiagnostics || {}),
    [diagnostic.path]: diagnostic
  };
  window.dispatchEvent(new CustomEvent(DIAGNOSTIC_EVENT, { detail: diagnostic }));
  if (!diagnostic.ok) console.error('[ProjectPulse authoritative API]', diagnostic);
}

function collectionMissing(payload, requiredCollections) {
  return requiredCollections.filter((name) => !Array.isArray(payload?.[name]));
}

export function authoritativeApiDiagnostics() {
  return { ...(window.__projectPulseAuthoritativeApiDiagnostics || {}) };
}

export async function authoritativeApi(path, options = {}) {
  const method = String(options.method || 'GET').toUpperCase();
  const requiredCollections = Array.isArray(options.requiredCollections) ? options.requiredCollections : [];
  const { token, viewAsUserId } = sessionContext();
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
    if (token) {
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
        if (isSessionRejection(request.status, payload, raw)) {
          invalidateProjectPulseSession(path, payload, raw);
        }
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

installGlobalFetchSessionInvalidation();
