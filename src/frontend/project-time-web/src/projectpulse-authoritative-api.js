const DIAGNOSTIC_EVENT = 'projectpulse:authoritative-api-diagnostic';
const DIAGNOSTIC_MARKER = 'projectpulse-authoritative-xhr-v1';

function sessionContext() {
  try {
    const session = JSON.parse(window.localStorage.getItem('projectPulseAuthSession') || 'null');
    const selected = JSON.parse(window.localStorage.getItem('projectPulseViewAsUser') || 'null');
    return {
      token: session?.sessionToken || session?.token || session?.accessToken || '',
      viewAsUserId: selected?.userId || window.localStorage.getItem('projectPulseViewAsUserId') || ''
    };
  } catch {
    return { token: '', viewAsUserId: '' };
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
