/*
 * Work Register project-document response integrity.
 *
 * Backend authorization remains authoritative. This compatibility layer
 * prevents an empty document identity from being promoted as a successful
 * upload or converted into /projects/documents//download.
 */

const DOCUMENT_UPLOAD_PATH = '/api/work-register/projects/documents/upload';
const EMPTY_DOCUMENT_ROUTE = '/api/work-register/projects/documents//';
const UUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;

function cleanText(value) {
  return String(value ?? '').trim();
}

function responseJson(payload, status, statusText) {
  return new Response(JSON.stringify(payload), {
    status,
    statusText,
    headers: {
      'Content-Type': 'application/json; charset=utf-8',
      'Cache-Control': 'no-store, no-cache, must-revalidate',
      Pragma: 'no-cache'
    }
  });
}

function property(source, ...names) {
  if (!source || typeof source !== 'object') return undefined;
  for (const name of names) {
    if (Object.prototype.hasOwnProperty.call(source, name)) return source[name];
  }
  return undefined;
}

function documentId(payload) {
  const candidates = [
    property(payload, 'documentId', 'DocumentId', 'projectDocumentId', 'ProjectDocumentId'),
    property(payload?.document, 'documentId', 'DocumentId', 'id', 'Id'),
    property(payload?.data, 'documentId', 'DocumentId', 'projectDocumentId', 'ProjectDocumentId'),
    property(payload?.result, 'documentId', 'DocumentId', 'projectDocumentId', 'ProjectDocumentId')
  ];
  return candidates.map(cleanText).find((value) => UUID_PATTERN.test(value)) || '';
}

function installWorkRegisterDocumentIntegrity() {
  if (typeof window === 'undefined' || typeof window.fetch !== 'function') return;
  if (window.__projectPulseWorkRegisterDocumentIntegrityInstalled) return;

  const previousFetch = window.fetch.bind(window);
  window.fetch = async (input, init = {}) => {
    const rawUrl = typeof input === 'string' ? input : input?.url;
    let url;
    try {
      url = new URL(rawUrl, window.location.origin);
    } catch {
      return previousFetch(input, init);
    }

    const method = String(init?.method || (input instanceof Request ? input.method : 'GET')).toUpperCase();
    if (url.origin === window.location.origin && url.pathname.includes(EMPTY_DOCUMENT_ROUTE)) {
      return responseJson({
        status: 'missing_document_id',
        module: '055C',
        message: 'A valid project document ID is required before a document can be opened or downloaded.'
      }, 400, 'Bad Request');
    }

    const response = await previousFetch(input, init);
    if (url.origin !== window.location.origin
        || url.pathname !== DOCUMENT_UPLOAD_PATH
        || method !== 'POST'
        || !response.ok) {
      return response;
    }

    let payload;
    try {
      payload = await response.clone().json();
    } catch {
      return responseJson({
        status: 'document_identity_missing',
        module: '055C',
        message: 'The upload response could not be verified, so the document was not marked as available.'
      }, 502, 'Bad Gateway');
    }

    const stableDocumentId = documentId(payload);
    if (!stableDocumentId) {
      return responseJson({
        status: 'document_identity_missing',
        module: '055C',
        message: 'The upload did not return a stable document ID, so success was not recorded. Refresh the project before trying again.'
      }, 502, 'Bad Gateway');
    }

    return response;
  };

  window.fetch.__projectPulseWorkRegisterDocumentIntegrity = true;
  window.__projectPulseWorkRegisterDocumentIntegrityInstalled = true;
}

installWorkRegisterDocumentIntegrity();
