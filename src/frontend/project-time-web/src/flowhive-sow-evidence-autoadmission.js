const INSTALL_MARKER = '__pulseFlowHiveSowEvidenceAutoadmissionInstalled';
const ENTERPRISE_PATH = /^\/api\/project-flowhive\/projects\/([0-9a-f-]{36})\/enterprise$/i;
const PREPARE_PATH = /^\/api\/project-flowhive\/projects\/([0-9a-f-]{36})\/sow-evidence\/([0-9a-f-]{36})\/prepare$/i;
const activeAdmissions = new Map();

function storedSessionToken() {
  try {
    const session = JSON.parse(window.localStorage.getItem('projectPulseAuthSession') || 'null');
    return session?.sessionToken || session?.token || session?.accessToken || '';
  } catch {
    return '';
  }
}

function urlOf(input) {
  try {
    return new URL(typeof input === 'string' ? input : input?.url, window.location.origin);
  } catch {
    return null;
  }
}

function methodOf(input, init) {
  return String(init?.method || (input instanceof Request ? input.method : 'GET')).toUpperCase();
}

function cloneInput(input) {
  return input instanceof Request ? input.clone() : input;
}

function newCorrelationId() {
  try {
    return crypto.randomUUID();
  } catch {
    return `flowhive-${Date.now()}-${Math.random().toString(36).slice(2)}`;
  }
}

export function normalizePreparePayload(value = {}) {
  const approveCurrentVersion = value?.approveCurrentVersion === true;
  const correlationId = String(value?.correlationId || '').trim() || newCorrelationId();
  return {
    approveCurrentVersion,
    approvalNote: approveCurrentVersion
      ? String(value?.approvalNote || '').trim()
      : null,
    correlationId
  };
}

export function prepareRequestHeaderObject(existing = {}, token = '') {
  const headers = new Headers(existing || {});
  headers.set('Accept', 'application/json');
  headers.set('Content-Type', 'application/json; charset=utf-8');
  headers.set('X-ProjectPulse-Module-Number', '066');
  if (token) {
    headers.set('Authorization', `Bearer ${token}`);
    headers.set('X-ProjectPulse-Session', token);
  }
  return Object.fromEntries(headers.entries());
}

async function readPreparePayload(input, init = {}) {
  const providedBody = init?.body;
  if (typeof providedBody === 'string' && providedBody.trim()) {
    try {
      return JSON.parse(providedBody);
    } catch {
      return {};
    }
  }
  if (typeof Request !== 'undefined' && input instanceof Request) {
    try {
      const text = await input.clone().text();
      return text.trim() ? JSON.parse(text) : {};
    } catch {
      return {};
    }
  }
  return {};
}

function requestHeaders(input, init, token) {
  const existing = new Headers(
    init?.headers || (typeof Request !== 'undefined' && input instanceof Request ? input.headers : {})
  );
  return prepareRequestHeaderObject(existing, token);
}

async function responseJson(response) {
  if (!response?.headers?.get('content-type')?.includes('application/json')) return {};
  return response.clone().json().catch(() => ({}));
}

function shouldRetryQueueValidation(response, responseBody, payload) {
  if (payload.approveCurrentVersion || response.status !== 400) return false;
  const status = String(responseBody?.status || responseBody?.code || '').toLowerCase();
  const message = String(responseBody?.message || responseBody?.detail || '').toLowerCase();
  return !status
    || status.includes('request_validation')
    || status.includes('validation')
    || message.includes('request') && message.includes('valid');
}

async function sendPrepareRequest(nativeFetch, input, init = {}) {
  const url = urlOf(input);
  const token = storedSessionToken();
  const payload = normalizePreparePayload(await readPreparePayload(input, init));
  const headers = requestHeaders(input, init, token);
  const requestInit = {
    ...init,
    method: 'POST',
    credentials: 'include',
    headers,
    body: JSON.stringify(payload)
  };
  let response = await nativeFetch(url?.href || input, requestInit);
  const body = await responseJson(response);
  if (!shouldRetryQueueValidation(response, body, payload)) return response;

  // Compatibility retry for older protected-Test revisions whose prepare
  // contract rejected an explicit null approval note. Approval requests are
  // never retried because their note validation is a governed user action.
  response = await nativeFetch(url?.href || input, {
    ...requestInit,
    body: JSON.stringify({
      approveCurrentVersion: false,
      correlationId: payload.correlationId
    })
  });
  return response;
}

export function evidenceScore(item) {
  const authority = String(item?.authorityStatus || '').toLowerCase();
  const processing = String(item?.processingStatus || '').toLowerCase();
  const index = String(item?.indexStatus || '').toLowerCase();
  return (item?.readyForAiPlanner ? 10_000 : 0)
    + (authority === 'canonical' ? 2_000 : authority === 'approved' ? 1_500 : 0)
    + (processing === 'ready' ? 1_000 : processing === 'indexing' ? 700 : processing === 'embedding' ? 600 : processing === 'extracting' ? 500 : processing === 'scanning' ? 400 : processing === 'queued' ? 300 : 0)
    + (item?.activeVersionId ? 200 : 0)
    + (['ready', 'embedding_ready', 'lexical_ready'].includes(index) ? 150 : 0)
    + (Number(item?.scopeCitationCount || 0) * 20)
    + Number(item?.citationCount || 0);
}

function evidenceRecency(item) {
  for (const value of [item?.uploadedAt, item?.processedAt, item?.processingUpdatedAt]) {
    const parsed = Date.parse(String(value || ''));
    if (Number.isFinite(parsed)) return parsed;
  }
  return 0;
}

export function evidenceIdentity(item) {
  const documentId = String(item?.documentId || '').trim().toLowerCase();
  if (documentId) return `document:${documentId}`;

  const activeVersionId = String(item?.activeVersionId || '').trim().toLowerCase();
  if (activeVersionId) return `version:${activeVersionId}`;

  const category = String(item?.documentCategory || 'other').trim().toLowerCase();
  const file = String(item?.originalFileName || '').trim().toLowerCase();
  const version = String(item?.documentVersion || '').trim().toLowerCase();
  const uploadedAt = String(item?.uploadedAt || '').trim().toLowerCase();
  return `fallback:${category}|${file}|${version}|${uploadedAt}`;
}

function preferredEvidence(left, right) {
  const scoreDifference = evidenceScore(right) - evidenceScore(left);
  if (scoreDifference !== 0) return scoreDifference > 0 ? right : left;
  return evidenceRecency(right) > evidenceRecency(left) ? right : left;
}

export function dedupeEvidence(evidence) {
  const groups = new Map();
  for (const item of evidence || []) {
    const key = evidenceIdentity(item);
    const current = groups.get(key);
    if (!current) {
      groups.set(key, { selected: item, all: [item] });
      continue;
    }
    current.all.push(item);
    current.selected = preferredEvidence(current.selected, item);
  }

  return [...groups.values()]
    .map(({ selected, all }) => ({
      ...selected,
      duplicateCount: all.length,
      duplicateDocumentIds: [...new Set(all.map((item) => item.documentId).filter(Boolean))],
      equivalentRecordNote: all.length > 1
        ? `${all.length} rows for the same authoritative document identity were consolidated; the strongest current private version is shown.`
        : ''
    }))
    .sort((left, right) => evidenceScore(right) - evidenceScore(left)
      || evidenceRecency(right) - evidenceRecency(left)
      || String(left.originalFileName || '').localeCompare(String(right.originalFileName || '')));
}

export function normalizeEvidenceBody(body) {
  if (!body || !Array.isArray(body.sowEvidence)) return body;
  const sowEvidence = dedupeEvidence(body.sowEvidence);
  const readyCount = sowEvidence.filter((item) => item.readyForAiPlanner).length;
  const approvedSowScopeReady = readyCount > 0;
  const duplicateRecordsConsolidated = body.sowEvidence.length - sowEvidence.length;
  return {
    ...body,
    sowEvidence,
    sowEvidenceSummary: {
      ...(body.sowEvidenceSummary || {}),
      candidateCount: sowEvidence.length,
      readyCount,
      approvedSowScopeReady,
      duplicateRecordsConsolidated,
      freshnessAuthority: 'project_scoped_server_gate',
      explanation: approvedSowScopeReady
        ? `At least one approved, citation-ready SOW scope source is available. The server will verify newest-source authority again before and after generation.${duplicateRecordsConsolidated > 0 ? ` ${duplicateRecordsConsolidated} authoritative duplicate row(s) were consolidated.` : ''}`
        : `AI Planner is automatically preparing the active Work Register SOW records. It requires private processing, an active authoritative version, citation indexing, and Scope of Services citations before generation.${duplicateRecordsConsolidated > 0 ? ` ${duplicateRecordsConsolidated} authoritative duplicate row(s) were consolidated.` : ''}`
    }
  };
}

function responseWithJson(response, body) {
  const headers = new Headers(response.headers);
  headers.set('Content-Type', 'application/json; charset=utf-8');
  headers.set('X-Pulse-FlowHive-Evidence-Normalized', 'true');
  return new Response(JSON.stringify(body), {
    status: response.status,
    statusText: response.statusText,
    headers
  });
}

export function isQueueCandidate(item) {
  const category = String(item?.documentCategory || '').trim().toLowerCase();
  const status = String(item?.processingStatus || 'not_requested').trim().toLowerCase();
  return !item?.readyForAiPlanner
    && ['sow', 'statement_of_work'].includes(category)
    && ['', 'not_requested', 'not_started'].includes(status)
    && Boolean(item?.documentId);
}

export function queueCandidatesFromEvidence(evidence, maximum = 6) {
  const candidatesByDocument = new Map();
  for (const item of evidence || []) {
    if (!isQueueCandidate(item)) continue;
    const documentId = String(item.documentId).trim().toLowerCase();
    const current = candidatesByDocument.get(documentId);
    if (!current
      || evidenceRecency(item) > evidenceRecency(current)
      || (evidenceRecency(item) === evidenceRecency(current) && evidenceScore(item) > evidenceScore(current))) {
      candidatesByDocument.set(documentId, item);
    }
  }

  return [...candidatesByDocument.values()]
    .sort((left, right) => evidenceRecency(right) - evidenceRecency(left)
      || evidenceScore(right) - evidenceScore(left)
      || String(left.documentId || '').localeCompare(String(right.documentId || '')))
    .slice(0, Math.max(0, maximum));
}

async function queueEvidence(nativeFetch, projectId, item) {
  const key = `${projectId}:${item.documentId}`;
  if (activeAdmissions.has(key)) return activeAdmissions.get(key);

  const promise = sendPrepareRequest(
    nativeFetch,
    `/api/project-flowhive/projects/${projectId}/sow-evidence/${item.documentId}/prepare`,
    {
      method: 'POST',
      body: JSON.stringify(normalizePreparePayload())
    }
  ).then(async (response) => ({
    succeeded: response.ok,
    status: response.status,
    body: await responseJson(response)
  })).catch(() => ({ succeeded: false, status: 0, body: {} }));

  activeAdmissions.set(key, promise);
  window.setTimeout(() => activeAdmissions.delete(key), 60_000);
  return promise;
}

async function readJsonBody(response) {
  return response.clone().json().catch(() => null);
}

// FlowHive private evidence admission is now owned by the server-side AI Planner
// operation. This module retains pure normalization helpers for compatibility
// and regression tests, but it no longer intercepts the browser's global fetch.
export const serverOwnedAiPlannerAdmission = true;
