/*
 * Work Register project-document response integrity and Module 055C continuity.
 *
 * Backend authorization remains authoritative. This compatibility layer
 * prevents an empty document identity from being promoted as a successful
 * upload, merges the canonical Work Register document projection into the
 * existing project-details response, and exposes governed shared deletion beside
 * the native editable Archive control used by Manage Existing Project.
 */

const DOCUMENT_UPLOAD_PATH = '/api/work-register/projects/documents/upload';
const EMPTY_DOCUMENT_ROUTE = '/api/work-register/projects/documents//';
const UUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;
const DETAILS_PATTERN = /^\/api\/work-register\/projects\/([0-9a-f-]{36})\/details$/i;
const DOWNLOAD_PATTERN = /\/api\/work-register\/projects\/documents\/([0-9a-f-]{36})\/download/i;
const DELETE_BUTTON_ATTRIBUTE = 'data-projectpulse-055c-shared-delete';

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
  window.__projectPulse055cCanonicalDocuments = canonicalDocuments;
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

function canonicalDocumentNames(document) {
  return [
    document?.fileName,
    document?.documentName,
    document?.originalFileName
  ].map(cleanText).filter(Boolean);
}

function canonicalDocumentForCard(card) {
  const title = cleanText(card?.querySelector('strong')?.textContent);
  if (!title) return null;
  const canonicalDocuments = Array.isArray(window.__projectPulse055cCanonicalDocuments)
    ? window.__projectPulse055cCanonicalDocuments
    : [];
  const matches = canonicalDocuments.filter((document) => canonicalDocumentNames(document).includes(title));
  return matches.length === 1 ? matches[0] : null;
}

function hasDeleteControl(actionContainer) {
  return Boolean(actionContainer?.querySelector(`[${DELETE_BUTTON_ATTRIBUTE}="true"]`));
}

function appendDeleteControl({ actionContainer, archiveButton, projectId, id, label, documentType = '' }) {
  if (!actionContainer || !archiveButton || hasDeleteControl(actionContainer)) return;
  if (!UUID_PATTERN.test(projectId) || !UUID_PATTERN.test(id)) return;

  const normalizedType = cleanText(documentType).toUpperCase();
  const button = document.createElement('button');
  button.type = 'button';
  button.className = `${archiveButton.className || 'secondary-action'} danger`;
  button.textContent = normalizedType === 'SOW' || normalizedType === 'GSD'
    ? `Delete ${normalizedType}`
    : 'Delete';
  button.setAttribute(DELETE_BUTTON_ATTRIBUTE, 'true');
  button.setAttribute('aria-label', `Delete ${cleanText(label) || 'project document'}`);
  button.addEventListener('click', async () => {
    const displayLabel = cleanText(label) || 'this document';
    const reason = window.prompt(`Delete ${displayLabel}? Enter the required audit reason:`);
    if (!reason || !reason.trim()) return;
    if (!window.confirm('This removes the shared document from Manage Existing Project, Project Workspace, and active FlowHive/Project Forge evidence while retaining immutable audit history. Continue?')) return;

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

      const card = actionContainer.closest('.work-register-document-card, article, li, .document-card, .drawer-card');
      if (card) card.remove();
      else button.remove();
      window.dispatchEvent(new CustomEvent('projectpulse-work-register-document-deleted', {
        detail: { projectId, documentId: id, result }
      }));
    } catch (error) {
      button.disabled = false;
      button.textContent = normalizedType === 'SOW' || normalizedType === 'GSD'
        ? `Delete ${normalizedType}`
        : 'Delete';
      window.alert(error instanceof Error ? error.message : 'Unable to delete shared project document.');
    }
  });
  actionContainer.appendChild(button);
}

function installDeleteControls() {
  const projectId = currentProjectId();
  if (!UUID_PATTERN.test(projectId)) return;

  // Current Manage Existing Project cards use React buttons rather than download anchors.
  // Resolve their stable document identity from the canonical 055C projection that was
  // merged into the details response, then place Delete beside the native Archive action.
  document.querySelectorAll('.work-register-document-card').forEach((card) => {
    const actionContainer = card.querySelector('.work-register-document-actions');
    if (!actionContainer || hasDeleteControl(actionContainer)) return;
    const archiveButton = [...actionContainer.querySelectorAll('button')]
      .find((button) => cleanText(button.textContent).toLowerCase() === 'archive');
    if (!archiveButton) return;

    const canonicalDocument = canonicalDocumentForCard(card);
    if (!canonicalDocument || canonicalDocument.canDelete === false) return;
    const id = documentId(canonicalDocument);
    if (!id) return;
    appendDeleteControl({
      actionContainer,
      archiveButton,
      projectId,
      id,
      label: card.querySelector('strong')?.textContent,
      documentType: canonicalDocument.documentType
    });
  });

  // Keep continuity with legacy document surfaces that still render a direct
  // download anchor. The native-card path above is authoritative for 055C.
  document.querySelectorAll('a[href*="/api/work-register/projects/documents/"][href*="/download"]').forEach((anchor) => {
    const match = cleanText(anchor.getAttribute('href')).match(DOWNLOAD_PATTERN);
    const id = match?.[1] || '';
    if (!UUID_PATTERN.test(id)) return;

    const actionContainer = anchor.parentElement;
    if (!actionContainer || hasDeleteControl(actionContainer)) return;

    const archiveButton = [...actionContainer.querySelectorAll('button')]
      .find((button) => cleanText(button.textContent).toLowerCase() === 'archive');
    if (!archiveButton) return;

    appendDeleteControl({
      actionContainer,
      archiveButton,
      projectId,
      id,
      label: anchor.textContent
    });
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
          const merged = mergeCanonicalDocuments(details, canonical);
          queueMicrotask(installDeleteControls);
          return responseJson(
            merged,
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
