/*
 * Work Register project-document response integrity and Module 055C continuity.
 *
 * Backend authorization remains authoritative. This compatibility layer
 * prevents an empty document identity from being promoted as a successful
 * upload, merges the canonical Work Register document projection into the
 * existing project-details response, and exposes governed shared deletion only
 * beside an existing editable Archive control.
 */

const DOCUMENT_UPLOAD_PATH = '/api/work-register/projects/documents/upload';
const EMPTY_DOCUMENT_ROUTE = '/api/work-register/projects/documents//';
const UUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;
const DETAILS_PATTERN = /^\/api\/work-register\/projects\/([0-9a-f-]{36})\/details$/i;
const DOWNLOAD_PATTERN = /\/api\/work-register\/projects\/documents\/([0-9a-f-]{36})\/download/i;
const DELETE_BUTTON_MARKER = 'projectPulse055cSharedDelete';

function cleanText(value) {
  return String(value ?? '').trim();
}

function responseJson(payload, status, statusText, sourceHeaders = null) {
  const headers = new Headers(sourceHeaders || {});
  headers.set('Content-Type', 'application/json; charset=utf-8');
  headers.set('Cache-Control', 'no-store, no-cache, must-revalidate');
  headers.set('Pragma', 'no-cache');
  headers.delete('content-length');
  return new Response(JSON.stringify(payload), { status, statusText, headers });
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
    property(payload, 'documentId', 'DocumentId', 'projectDocumentId', 'ProjectDocumentId', 'workRegisterDocumentId', 'WorkRegisterDocumentId'),
    property(payload?.document, 'documentId', 'DocumentId', 'id', 'Id'),
    property(payload?.data, 'documentId', 'DocumentId', 'projectDocumentId', 'ProjectDocumentId', 'workRegisterDocumentId', 'WorkRegisterDocumentId'),
    property(payload?.result, 'documentId', 'DocumentId', 'projectDocumentId', 'ProjectDocumentId', 'workRegisterDocumentId', 'WorkRegisterDocumentId')
  ];
  return candidates.map(cleanText).find((value) => UUID_PATTERN.test(value)) || '';
}

function mergeCanonicalDocuments(details, canonical) {
  const canonicalDocuments = Array.isArray(canonical?.documents) ? canonical.documents : [];
  const fallbackDocuments = Array.isArray(details?.documents)
    ? details.documents.filter((item) => !['deleted', 'removed', 'purged'].includes(cleanText(item?.status).toLowerCase()))
    : [];
  const seen = new Set();
  const documents = [...canonicalDocuments, ...fallbackDocuments].filter((item) => {
    const id = cleanText(item?.documentId || item?.workRegisterDocumentId).toLowerCase();
    if (!id) return true;
    if (seen.has(id)) return false;
    seen.add(id);
    return true;
  });
  return {
    ...details,
    documents,
    documentContinuityContract: canonical?.contract || 'legacy_details_fallback'
  };
}

function forwardedHeaders(input, init) {
  const headers = new Headers(input instanceof Request ? input.headers : undefined);
  const override = new Headers(init?.headers || undefined);
  override.forEach((value, key) => headers.set(key, value));
  return headers;
}

function remember055cRequestContext(input, init) {
  const headers = forwardedHeaders(input, init);
  window.__projectPulse055cRequestHeaders = [...headers.entries()];
  window.__projectPulse055cCredentials = init?.credentials
    || (input instanceof Request ? input.credentials : undefined)
    || 'same-origin';
  return headers;
}

function currentProjectId() {
  return cleanText(window.__projectPulse055cCurrentProjectId || '');
}

function installDeleteControls() {
  const projectId = currentProjectId();
  if (!UUID_PATTERN.test(projectId)) return;

  document.querySelectorAll('a[href*="/api/work-register/projects/documents/"][href*="/download"]').forEach((anchor) => {
    const match = cleanText(anchor.getAttribute('href')).match(DOWNLOAD_PATTERN);
    const id = match?.[1] || '';
    if (!UUID_PATTERN.test(id)) return;

    const actionContainer = anchor.parentElement;
    if (!actionContainer || actionContainer.querySelector(`[data-${DELETE_BUTTON_MARKER}]`)) return;

    const archiveButton = [...actionContainer.querySelectorAll('button')]
      .find((button) => cleanText(button.textContent).toLowerCase() === 'archive');
    if (!archiveButton) return;

    const button = document.createElement('button');
    button.type = 'button';
    button.className = `${archiveButton.className || 'secondary-action'} danger`;
    button.textContent = 'Delete from 055C and 019';
    button.dataset[DELETE_BUTTON_MARKER] = 'true';
    button.addEventListener('click', async () => {
      const label = cleanText(anchor.textContent) || 'this document';
      const reason = window.prompt(`Delete ${label}? Enter the required audit reason:`);
      if (!reason || !reason.trim()) return;
      if (!window.confirm('This removes the shared document from Module 055C, Module 019, and active FlowHive/Project Forge evidence. Continue?')) return;

      button.disabled = true;
      button.textContent = 'Deleting…';
      try {
        const deleteHeaders = new Headers(window.__projectPulse055cRequestHeaders || []);
        deleteHeaders.set('Content-Type', 'application/json');
        const response = await window.fetch(`/api/work-register/projects/${projectId}/documents/${id}`, {
          method: 'DELETE',
          headers: deleteHeaders,
          credentials: window.__projectPulse055cCredentials || 'same-origin',
          body: JSON.stringify({ reason: reason.trim() })
        });
        const result = await response.json().catch(() => ({}));
        if (!response.ok) throw new Error(result.message || `HTTP ${response.status}`);

        const card = actionContainer.closest('article, li, .document-card, .work-register-document-card, .detail-card, .drawer-card');
        if (card) card.remove();
        else {
          anchor.remove();
          archiveButton.remove();
          button.remove();
        }
        window.dispatchEvent(new CustomEvent('projectpulse-work-register-document-deleted', {
          detail: { projectId, documentId: id, result }
        }));
      } catch (error) {
        button.disabled = false;
        button.textContent = 'Delete from 055C and 019';
        window.alert(error instanceof Error ? error.message : 'Unable to delete shared project document.');
      }
    });
    actionContainer.appendChild(button);
  });
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
    if (url.origin !== window.location.origin) return response;

    const detailsMatch = method === 'GET' ? url.pathname.match(DETAILS_PATTERN) : null;
    if (detailsMatch && response.ok) {
      const projectId = detailsMatch[1].toLowerCase();
      window.__projectPulse055cCurrentProjectId = projectId;
      const requestHeaders = remember055cRequestContext(input, init);
      let details;
      try {
        details = await response.clone().json();
      } catch {
        return response;
      }
      try {
        const canonicalResponse = await previousFetch(`/api/work-register/projects/${projectId}/documents`, {
          method: 'GET',
          headers: requestHeaders,
          credentials: window.__projectPulse055cCredentials || 'same-origin'
        });
        if (canonicalResponse.ok) {
          const canonical = await canonicalResponse.json();
          queueMicrotask(installDeleteControls);
          return responseJson(
            mergeCanonicalDocuments(details, canonical),
            response.status,
            response.statusText,
            response.headers
          );
        }
      } catch {
        // Fail back to the legacy detail response; the backend remains authoritative.
      }
      queueMicrotask(installDeleteControls);
      return response;
    }

    if (url.pathname === DOCUMENT_UPLOAD_PATH && method === 'POST' && response.ok) {
      remember055cRequestContext(input, init);
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
      queueMicrotask(installDeleteControls);
    }

    return response;
  };

  const observer = new MutationObserver(() => installDeleteControls());
  observer.observe(document.documentElement, { childList: true, subtree: true });
  window.fetch.__projectPulseWorkRegisterDocumentIntegrity = true;
  window.__projectPulseWorkRegisterDocumentIntegrityInstalled = true;
}

installWorkRegisterDocumentIntegrity();
