const INSTALL_MARKER = '__pulseFlowHiveSowEvidenceAutoadmissionInstalled';
const ENTERPRISE_PATH = /^\/api\/project-flowhive\/projects\/([0-9a-f-]{36})\/enterprise$/i;
const GENERATION_PATH = /^\/api\/project-flowhive\/ai\/production-generate$/i;
const DOCUMENT_OVERVIEW_PATH = '/api/project-workspace/overview';
const activeAdmissions = new Map();
const generationBlocks = new Map();

function storedSessionToken() {
  try {
    const session = JSON.parse(window.localStorage.getItem('projectPulseAuthSession') || 'null');
    return session?.sessionToken || session?.token || session?.accessToken || '';
  } catch {
    return '';
  }
}

function storedViewAsUserId() {
  try {
    const viewAs = JSON.parse(window.localStorage.getItem('projectPulseViewAsUser') || 'null');
    return String(viewAs?.userId || '').trim();
  } catch {
    return '';
  }
}

function authenticatedHeaders(extra = {}) {
  const token = storedSessionToken();
  const viewAsUserId = storedViewAsUserId();
  return {
    ...(token ? {
      Authorization: `Bearer ${token}`,
      'X-ProjectPulse-Session': token,
      'X-Project-Pulse-Session': token,
      'X-Session-Token': token
    } : {}),
    ...(viewAsUserId ? { 'X-ProjectPulse-View-As-User': viewAsUserId } : {}),
    ...extra
  };
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

async function requestJson(input, init) {
  try {
    if (typeof init?.body === 'string') return JSON.parse(init.body);
    if (input instanceof Request) return await input.clone().json();
  } catch {
    return null;
  }
  return null;
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

export function sourceChronology(item) {
  for (const value of [item?.sourceEffectiveAt, item?.effectiveAt, item?.uploadedAt]) {
    const parsed = Date.parse(String(value || ''));
    if (Number.isFinite(parsed)) return parsed;
  }
  return 0;
}

function evidenceRecency(item) {
  const sourceTime = sourceChronology(item);
  if (sourceTime > 0) return sourceTime;
  for (const value of [item?.processedAt, item?.processingUpdatedAt]) {
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

function filenameIdentity(item) {
  const category = String(item?.documentCategory || 'other').trim().toLowerCase();
  const file = String(item?.originalFileName || '').trim().toLowerCase();
  return `${category}|${file}`;
}

function preferredEvidence(left, right) {
  const scoreDifference = evidenceScore(right) - evidenceScore(left);
  if (scoreDifference !== 0) return scoreDifference > 0 ? right : left;
  return evidenceRecency(right) > evidenceRecency(left) ? right : left;
}

function authoritativeRows(evidence) {
  const rows = new Map();
  for (const item of evidence || []) {
    const key = evidenceIdentity(item);
    const current = rows.get(key);
    rows.set(key, current ? preferredEvidence(current, item) : item);
  }
  return [...rows.values()];
}

export function enrichEvidenceWithDocumentChronology(evidence, documents, projectId = '') {
  const normalizedProjectId = String(projectId || '').trim().toLowerCase();
  const byDocumentId = new Map();
  for (const document of documents || []) {
    const documentId = String(document?.documentId || document?.id || '').trim().toLowerCase();
    const documentProjectId = String(document?.projectId || '').trim().toLowerCase();
    if (!documentId || (normalizedProjectId && documentProjectId && documentProjectId !== normalizedProjectId)) continue;
    byDocumentId.set(documentId, document);
  }

  return (evidence || []).map((item) => {
    const documentId = String(item?.documentId || '').trim().toLowerCase();
    const document = byDocumentId.get(documentId);
    if (!document) return item;
    const uploadedAt = item?.uploadedAt || document?.uploadedAt || null;
    const sourceEffectiveAt = item?.sourceEffectiveAt || item?.effectiveAt || document?.effectiveAt || uploadedAt;
    return {
      ...item,
      uploadedAt,
      sourceEffectiveAt,
      chronologySource: 'module_019_authorized_document_inventory'
    };
  });
}

export function pendingReplacementEvidence(evidence) {
  const groups = new Map();
  for (const item of authoritativeRows(evidence)) {
    const key = filenameIdentity(item);
    const current = groups.get(key) || [];
    current.push(item);
    groups.set(key, current);
  }

  return [...groups.entries()].flatMap(([key, items]) => {
    const documentIds = [...new Set(items.map((item) => String(item?.documentId || '').trim()).filter(Boolean))];
    if (documentIds.length < 2) return [];

    // Replacement authority follows source chronology, not readiness score.
    // This prevents an older failed upload from blocking a newer processed SOW.
    const ordered = [...items]
      .filter((item) => sourceChronology(item) > 0)
      .sort((left, right) => sourceChronology(right) - sourceChronology(left)
        || String(right.documentId || '').localeCompare(String(left.documentId || '')));
    if (ordered.length !== items.length) return [];

    const newest = ordered[0];
    const olderReady = ordered.slice(1).some((item) => item?.readyForAiPlanner);
    if (newest?.readyForAiPlanner || !olderReady) return [];

    const newestDocumentId = String(newest?.documentId || '').trim();
    return [{
      key,
      originalFileName: newest?.originalFileName || items[0]?.originalFileName || 'SOW',
      documentIds,
      newestDocumentId,
      newestSourceEffectiveAt: newest?.sourceEffectiveAt || newest?.effectiveAt || newest?.uploadedAt || null,
      pendingDocumentIds: newestDocumentId ? [newestDocumentId] : []
    }];
  });
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
  const pendingReplacements = pendingReplacementEvidence(body.sowEvidence);
  const readyCount = sowEvidence.filter((item) => item.readyForAiPlanner).length;
  const approvedSowScopeReady = readyCount > 0 && pendingReplacements.length === 0;
  const duplicateRecordsConsolidated = body.sowEvidence.length - sowEvidence.length;
  return {
    ...body,
    sowEvidence,
    sowEvidenceSummary: {
      ...(body.sowEvidenceSummary || {}),
      candidateCount: sowEvidence.length,
      readyCount,
      approvedSowScopeReady,
      pendingReplacementCount: pendingReplacements.length,
      pendingReplacementDocumentIds: pendingReplacements.flatMap((item) => item.pendingDocumentIds),
      duplicateRecordsConsolidated,
      explanation: pendingReplacements.length > 0
        ? 'The newest same-name replacement SOW is still being privately processed. FlowHive has blocked generation so the older ready SOW cannot be used as stale contractual scope.'
        : approvedSowScopeReady
          ? `At least one approved, citation-ready SOW scope source is available to AI Planner.${duplicateRecordsConsolidated > 0 ? ` ${duplicateRecordsConsolidated} authoritative duplicate row(s) were consolidated.` : ''}`
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

function blockedGenerationResponse(blocks) {
  return new Response(JSON.stringify({
    module: '066',
    feature: 'project_flowhive_plan',
    status: 'flowhive_sow_evidence_not_ready',
    message: 'FlowHive blocked generation because the newest replacement SOW is still being privately processed. The older ready document was not used as stale contractual scope.',
    missingEvidence: blocks.map((item) => `${item.originalFileName}: newest replacement processing and citation authority are not ready.`),
    warnings: ['Wait for private scanning, extraction, indexing, and authority reconciliation to complete, then retry AI Planner.'],
    stateChanged: false
  }), {
    status: 422,
    headers: {
      'Content-Type': 'application/json; charset=utf-8',
      'X-Pulse-FlowHive-Stale-Sow-Blocked': 'true'
    }
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

function correlationId() {
  try {
    return crypto.randomUUID();
  } catch {
    return `flowhive-${Date.now()}-${Math.random().toString(36).slice(2)}`;
  }
}

async function queueEvidence(nativeFetch, projectId, item) {
  const key = `${projectId}:${item.documentId}`;
  if (activeAdmissions.has(key)) return activeAdmissions.get(key);

  const promise = nativeFetch(
    `/api/project-flowhive/projects/${projectId}/sow-evidence/${item.documentId}/prepare`,
    {
      method: 'POST',
      credentials: 'include',
      headers: authenticatedHeaders({ 'Content-Type': 'application/json' }),
      body: JSON.stringify({
        approveCurrentVersion: false,
        approvalNote: '',
        correlationId: correlationId()
      })
    }
  ).then(async (response) => ({
    succeeded: response.ok,
    status: response.status,
    body: response.headers.get('content-type')?.includes('application/json')
      ? await response.clone().json().catch(() => ({}))
      : {}
  })).catch(() => ({ succeeded: false, status: 0, body: {} }));

  activeAdmissions.set(key, promise);
  window.setTimeout(() => activeAdmissions.delete(key), 60_000);
  return promise;
}

async function readJsonBody(response) {
  return response.clone().json().catch(() => null);
}

async function loadAuthoritativeDocumentChronology(nativeFetch, projectId) {
  try {
    const response = await nativeFetch(DOCUMENT_OVERVIEW_PATH, {
      method: 'GET',
      credentials: 'include',
      headers: authenticatedHeaders({ 'Cache-Control': 'no-cache', Pragma: 'no-cache' })
    });
    if (!response.ok || !response.headers.get('content-type')?.includes('application/json')) return [];
    const body = await readJsonBody(response);
    const normalizedProjectId = String(projectId || '').trim().toLowerCase();
    return (body?.documents || []).filter((document) => {
      const documentProjectId = String(document?.projectId || '').trim().toLowerCase();
      return !normalizedProjectId || !documentProjectId || documentProjectId === normalizedProjectId;
    });
  } catch {
    return [];
  }
}

async function enrichEnterpriseEvidence(nativeFetch, body, projectId) {
  if (!body || !Array.isArray(body.sowEvidence)) return body;
  const documents = await loadAuthoritativeDocumentChronology(nativeFetch, projectId);
  if (documents.length === 0) return body;
  return {
    ...body,
    sowEvidence: enrichEvidenceWithDocumentChronology(body.sowEvidence, documents, projectId)
  };
}

function recordGenerationBlocks(projectId, evidence) {
  const key = String(projectId || '').trim().toLowerCase();
  if (!key) return [];
  const blocks = pendingReplacementEvidence(evidence);
  if (blocks.length > 0) generationBlocks.set(key, blocks);
  else generationBlocks.delete(key);
  return blocks;
}

async function loadEnterpriseEvidence(nativeFetch, projectId) {
  const response = await nativeFetch(`/api/project-flowhive/projects/${projectId}/enterprise`, {
    method: 'GET',
    credentials: 'include',
    headers: authenticatedHeaders({ 'Cache-Control': 'no-cache', Pragma: 'no-cache' })
  });
  if (!response.ok || !response.headers.get('content-type')?.includes('application/json')) return null;
  const body = await readJsonBody(response);
  return enrichEnterpriseEvidence(nativeFetch, body, projectId);
}

async function refreshGenerationBlocks(nativeFetch, projectId) {
  const key = String(projectId || '').trim().toLowerCase();
  if (!key) return { refreshed: false, blocks: [] };
  try {
    let body = await loadEnterpriseEvidence(nativeFetch, key);
    if (!body || !Array.isArray(body.sowEvidence)) {
      return { refreshed: false, blocks: generationBlocks.get(key) || [] };
    }

    const candidates = body?.access?.canManage
      ? queueCandidatesFromEvidence(body.sowEvidence, 6)
      : [];
    if (candidates.length > 0) {
      await Promise.all(candidates.map((item) => queueEvidence(nativeFetch, key, item)));
      body = await loadEnterpriseEvidence(nativeFetch, key) || body;
    }

    const blocks = recordGenerationBlocks(key, body.sowEvidence || []);
    return { refreshed: true, blocks };
  } catch {
    return { refreshed: false, blocks: generationBlocks.get(key) || [] };
  }
}

if (typeof window !== 'undefined' && !window[INSTALL_MARKER]) {
  const nativeFetch = window.fetch.bind(window);

  window.fetch = async (input, init = {}) => {
    const url = urlOf(input);
    const method = methodOf(input, init);

    if (url?.origin === window.location.origin
      && GENERATION_PATH.test(url.pathname)
      && method === 'POST') {
      const requestBody = await requestJson(input, init);
      const projectId = String(requestBody?.plan?.projectId || '').trim().toLowerCase();
      const refreshed = await refreshGenerationBlocks(nativeFetch, projectId);
      const blocks = refreshed.refreshed
        ? refreshed.blocks
        : generationBlocks.get(projectId) || [];
      if (blocks.length > 0) return blockedGenerationResponse(blocks);
      return nativeFetch(input, init);
    }

    const match = url?.origin === window.location.origin
      ? url.pathname.match(ENTERPRISE_PATH)
      : null;
    if (!match || method !== 'GET') return nativeFetch(input, init);

    let response = await nativeFetch(cloneInput(input), init);
    if (!response.ok || !response.headers.get('content-type')?.includes('application/json')) return response;

    const projectId = match[1].toLowerCase();
    let rawBody = await readJsonBody(response);
    if (!rawBody || !Array.isArray(rawBody.sowEvidence)) return response;
    rawBody = await enrichEnterpriseEvidence(nativeFetch, rawBody, projectId);
    recordGenerationBlocks(projectId, rawBody.sowEvidence);
    let body = normalizeEvidenceBody(rawBody);
    if (!body?.access?.canManage) return responseWithJson(response, body);

    // Admission is intentionally based on the raw authoritative records rather
    // than the normalized display collection. A replacement SOW that reuses an
    // older filename must still receive its own private processing request.
    const candidates = queueCandidatesFromEvidence(rawBody.sowEvidence, 6);
    if (candidates.length === 0) return responseWithJson(response, body);

    const results = await Promise.all(candidates.map((item) => queueEvidence(nativeFetch, projectId, item)));
    const queued = results.filter((result) => result.succeeded && result.body?.queued !== false).length;
    if (queued === 0) return responseWithJson(response, body);

    window.dispatchEvent(new CustomEvent('pulse:flowhive-sow-evidence-admitted', {
      detail: {
        projectId,
        queuedDocumentCount: queued,
        privateProcessingRequested: true,
        rawDocumentSentExternally: false
      }
    }));

    response = await nativeFetch(cloneInput(input), init);
    if (!response.ok || !response.headers.get('content-type')?.includes('application/json')) return response;
    rawBody = await readJsonBody(response);
    if (rawBody && Array.isArray(rawBody.sowEvidence)) {
      rawBody = await enrichEnterpriseEvidence(nativeFetch, rawBody, projectId);
      recordGenerationBlocks(projectId, rawBody.sowEvidence);
    }
    body = normalizeEvidenceBody(rawBody);
    return body ? responseWithJson(response, body) : response;
  };

  window[INSTALL_MARKER] = true;
}
