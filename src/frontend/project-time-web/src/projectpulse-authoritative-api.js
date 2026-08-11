import { currentProjectPulseRoute, moduleForRoute } from './module-availability-registry.js';

const DIAGNOSTIC_EVENT = 'projectpulse:authoritative-api-diagnostic';
const DIAGNOSTIC_MARKER = 'projectpulse-authoritative-xhr-v1';
const NATIVE_FALLBACK_MARKER = 'projectpulse-authoritative-native-fetch-fallback-v1';
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
const ENVELOPE_KEYS = Object.freeze([
  'data',
  'Data',
  'result',
  'Result',
  'value',
  'Value',
  'payload',
  'Payload'
]);

// Captured before App.jsx and runtime compatibility layers install their wrappers.
// This gives the authoritative client one clean recovery path if a browser XHR
// completes with HTTP 200 but exposes an empty or envelope-corrupted JSON shape.
const CAPTURED_NATIVE_FETCH = typeof window !== 'undefined' && typeof window.fetch === 'function'
  ? window.fetch.bind(window)
  : null;

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

function isObjectRecord(value) {
  return Boolean(value && typeof value === 'object' && !Array.isArray(value));
}

function normalizeCollectionKeys(candidate, requiredCollections) {
  if (!isObjectRecord(candidate)) return {};
  if (!requiredCollections.length) return candidate;

  const normalized = { ...candidate };
  const actualKeys = Object.keys(candidate);

  for (const required of requiredCollections) {
    if (Array.isArray(normalized[required])) continue;
    const match = actualKeys.find((key) => key.toLowerCase() === required.toLowerCase());
    if (match && Array.isArray(candidate[match])) normalized[required] = candidate[match];
  }

  return normalized;
}

function payloadCandidates(payload, requiredCollections) {
  const candidates = [];
  const visited = new Set();

  const visit = (value, depth = 0) => {
    if (Array.isArray(value)) {
      if (requiredCollections.length === 1) {
        candidates.push({ [requiredCollections[0]]: value });
      }
      return;
    }

    if (!isObjectRecord(value) || visited.has(value)) return;
    visited.add(value);
    candidates.push(value);

    if (depth >= 3) return;
    for (const key of ENVELOPE_KEYS) {
      if (value[key] !== undefined && value[key] !== null) visit(value[key], depth + 1);
    }
  };

  visit(payload);
  return candidates;
}

function normalizePayload(payload, requiredCollections = []) {
  const required = Array.isArray(requiredCollections)
    ? requiredCollections.filter(Boolean)
    : [];
  const candidates = payloadCandidates(payload, required);

  if (required.length) {
    for (const candidate of candidates) {
      const normalized = normalizeCollectionKeys(candidate, required);
      if (required.every((name) => Array.isArray(normalized[name]))) return normalized;
    }
  }

  if (isObjectRecord(payload)) {
    const rootKeys = Object.keys(payload);
    const hasNonEnvelopeKey = rootKeys.some((key) => !ENVELOPE_KEYS.includes(key));
    if (hasNonEnvelopeKey || candidates.length === 0) return payload;

    // The root is only an envelope. Preserve the previous unwrap behavior by
    // returning the first populated nested object rather than the envelope root.
    const nestedCandidate = candidates
      .slice(1)
      .find((candidate) => Object.keys(candidate).length > 0);
    return nestedCandidate || payload;
  }

  return candidates.find((candidate) => Object.keys(candidate).length > 0) || {};
}

function payloadType(value) {
  if (Array.isArray(value)) return 'array';
  if (value === null) return 'null';
  return typeof value;
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
    message: 'Pulse session is not ready yet.',
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
    console.error('[Pulse authoritative API]', diagnostic);
  }
}

function collectionMissing(payload, requiredCollections) {
  return requiredCollections.filter((name) => !Array.isArray(payload?.[name]));
}

function globalXhrSessionBridgeInstalled() {
  return Boolean(window.XMLHttpRequest?.prototype?.__projectPulse050BFinalWrapped);
}

function globalXhrBridgeToken() {
  // This intentionally mirrors the exact token/storage contract in App.jsx's
  // 050B global XHR bridge. Do not broaden it without broadening that bridge.
  const session = parseStoredJson(window.localStorage, 'projectPulseAuthSession');
  return session?.sessionToken
    || session?.token
    || session?.accessToken
    || '';
}

function globalXhrBridgeCanSupplyToken(token) {
  return Boolean(
    token
      && globalXhrSessionBridgeInstalled()
      && globalXhrBridgeToken() === token
  );
}

function sessionNotReadyError(path) {
  const error = new Error('Pulse session is not ready yet.');
  error.status = SESSION_NOT_READY_STATUS;
  error.code = 'session_not_ready';
  error.path = path;
  error.silent = true;
  return error;
}

function sessionTransportConflictError(path) {
  const error = new Error('Pulse session transport is waiting for stale browser session state to be replaced.');
  error.status = SESSION_NOT_READY_STATUS;
  error.code = 'session_transport_conflict';
  error.path = path;
  error.silent = true;
  return error;
}

async function nativeFetchAuthoritative(path, options) {
  if (!CAPTURED_NATIVE_FETCH) {
    const error = new Error('The browser native fetch transport is unavailable.');
    error.code = 'native_fetch_unavailable';
    throw error;
  }

  const headers = applySessionHeaders(new Headers(options.headers || {}), options.token);
  headers.set('Accept', 'application/json');
  headers.set('Cache-Control', 'no-cache, no-store, max-age=0');
  headers.set('Pragma', 'no-cache');
  headers.set('X-ProjectPulse-Authoritative-Client', NATIVE_FALLBACK_MARKER);
  if (options.moduleNumber) headers.set('X-ProjectPulse-Module-Number', options.moduleNumber);
  if (options.viewAsUserId) headers.set('X-ProjectPulse-View-As-User', options.viewAsUserId);
  if (options.body != null) headers.set('Content-Type', 'application/json');

  const response = await CAPTURED_NATIVE_FETCH(path, {
    method: options.method,
    headers,
    body: options.body == null ? undefined : options.body,
    credentials: 'include',
    cache: 'no-store'
  });

  const raw = await response.text();
  let rawPayload;
  try {
    rawPayload = raw ? JSON.parse(raw) : {};
  } catch {
    const error = new Error(`${path} returned non-JSON content through the native fallback.`);
    error.status = response.status;
    error.responseText = raw;
    throw error;
  }

  return {
    status: response.status,
    ok: response.ok,
    raw,
    rawPayload,
    payload: normalizePayload(rawPayload, options.requiredCollections)
  };
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
  const bridgeToken = globalXhrBridgeToken();
  if (
    token
      && globalXhrSessionBridgeInstalled()
      && bridgeToken
      && bridgeToken !== token
  ) {
    // The App bridge would append its different token at send(). Stop locally
    // rather than transmitting a combined, guaranteed-invalid header value.
    throw sessionTransportConflictError(path);
  }

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

    // Defer only when App.jsx's global XHR bridge can supply this exact token.
    // Legacy/session-storage/session_token sessions use the direct fallback when
    // the bridge has no token, preserving compatibility without duplication.
    if (token && !globalXhrBridgeCanSupplyToken(token)) {
      request.setRequestHeader('Authorization', `Bearer ${token}`);
      request.setRequestHeader('X-ProjectPulse-Session', token);
      request.setRequestHeader('X-Project-Pulse-Session', token);
      request.setRequestHeader('X-Session-Token', token);
    }

    if (viewAsUserId) request.setRequestHeader('X-ProjectPulse-View-As-User', viewAsUserId);
    for (const [name, value] of Object.entries(options.headers || {})) {
      if (value != null) request.setRequestHeader(name, String(value));
    }

    const finishError = (message, status = 0, payload = null, responseText = '', extra = {}) => {
      const normalized = isObjectRecord(payload) ? payload : {};
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
        ...extra,
        at: new Date().toISOString()
      };
      publishDiagnostic(diagnostic);
      const error = new Error(message);
      error.status = status;
      error.payload = normalized;
      error.diagnostic = diagnostic;
      reject(error);
    };

    const finishSuccess = (payload, status, transport, extra = {}) => {
      const diagnostic = {
        marker: DIAGNOSTIC_MARKER,
        ok: true,
        method,
        path,
        moduleNumber,
        status,
        durationMs: Date.now() - startedAt,
        transport,
        responseKeys: Object.keys(payload || {}),
        collectionCounts: Object.fromEntries(requiredCollections.map((name) => [name, payload[name].length])),
        ...extra,
        at: new Date().toISOString()
      };
      publishDiagnostic(diagnostic);
      resolve(payload);
    };

    request.onload = async () => {
      const raw = request.responseText || '';
      let rawPayload;
      try {
        rawPayload = raw ? JSON.parse(raw) : {};
      } catch {
        finishError(`${path} returned non-JSON content instead of Pulse API data.`, request.status, null, raw, {
          transport: 'xhr',
          rawResponseType: 'non-json'
        });
        return;
      }

      const payload = normalizePayload(rawPayload, requiredCollections);
      if (request.status < 200 || request.status >= 300) {
        finishError(
          payload.message || payload.Message || payload.detail || payload.Detail || `${path} returned HTTP ${request.status}.`,
          request.status,
          payload,
          raw,
          {
            transport: 'xhr',
            rawResponseType: payloadType(rawPayload),
            rawResponseKeys: isObjectRecord(rawPayload) ? Object.keys(rawPayload) : []
          }
        );
        return;
      }

      const missingCollections = collectionMissing(payload, requiredCollections);
      if (missingCollections.length && method === 'GET' && options.nativeFallback !== false) {
        try {
          const fallback = await nativeFetchAuthoritative(path, {
            method,
            body: options.body == null ? null : options.body,
            headers: options.headers,
            token,
            viewAsUserId,
            moduleNumber,
            requiredCollections
          });
          const fallbackMissing = collectionMissing(fallback.payload, requiredCollections);

          if (fallback.ok && fallbackMissing.length === 0) {
            finishSuccess(fallback.payload, fallback.status, 'native-fetch-fallback', {
              recoveredFrom: 'xhr-success-missing-collections',
              xhrStatus: request.status,
              xhrRawResponseType: payloadType(rawPayload),
              xhrRawResponseKeys: isObjectRecord(rawPayload) ? Object.keys(rawPayload) : [],
              fallbackRawResponseType: payloadType(fallback.rawPayload),
              fallbackRawResponseKeys: isObjectRecord(fallback.rawPayload) ? Object.keys(fallback.rawPayload) : []
            });
            return;
          }

          finishError(
            fallback.payload?.message
              || `The authoritative response for ${path} did not contain required collections: ${fallbackMissing.join(', ') || missingCollections.join(', ')}.`,
            fallback.status,
            fallback.payload,
            fallback.raw,
            {
              transport: 'native-fetch-fallback',
              recoveredFrom: 'xhr-success-missing-collections',
              xhrStatus: request.status,
              xhrRawResponseType: payloadType(rawPayload),
              xhrRawResponseKeys: isObjectRecord(rawPayload) ? Object.keys(rawPayload) : [],
              fallbackRawResponseType: payloadType(fallback.rawPayload),
              fallbackRawResponseKeys: isObjectRecord(fallback.rawPayload) ? Object.keys(fallback.rawPayload) : []
            }
          );
          return;
        } catch (fallbackError) {
          finishError(
            fallbackError instanceof Error
              ? fallbackError.message
              : `The authoritative fallback for ${path} failed.`,
            Number(fallbackError?.status || request.status || 502),
            payload,
            raw,
            {
              transport: 'native-fetch-fallback',
              recoveredFrom: 'xhr-success-missing-collections',
              xhrStatus: request.status,
              xhrRawResponseType: payloadType(rawPayload),
              xhrRawResponseKeys: isObjectRecord(rawPayload) ? Object.keys(rawPayload) : [],
              fallbackErrorCode: fallbackError?.code || ''
            }
          );
          return;
        }
      }

      if (missingCollections.length) {
        finishError(
          `The authoritative response for ${path} did not contain required collections: ${missingCollections.join(', ')}.`,
          request.status,
          payload,
          raw,
          {
            transport: 'xhr',
            rawResponseType: payloadType(rawPayload),
            rawResponseKeys: isObjectRecord(rawPayload) ? Object.keys(rawPayload) : []
          }
        );
        return;
      }

      finishSuccess(payload, request.status, 'xhr', {
        rawResponseType: payloadType(rawPayload),
        rawResponseKeys: isObjectRecord(rawPayload) ? Object.keys(rawPayload) : []
      });
    };

    request.onerror = () => finishError(`${path} could not be reached.`, request.status || 0, null, request.responseText || '', { transport: 'xhr' });
    request.ontimeout = () => finishError(`${path} timed out.`, request.status || 0, null, request.responseText || '', { transport: 'xhr' });
    request.onabort = () => finishError(`${path} was cancelled.`, request.status || 0, null, request.responseText || '', { transport: 'xhr' });
    request.send(options.body == null ? null : options.body);
  });
}

if (typeof window !== 'undefined' && typeof window.fetch === 'function') {
  installProtectedFetchReadinessGate();
}
