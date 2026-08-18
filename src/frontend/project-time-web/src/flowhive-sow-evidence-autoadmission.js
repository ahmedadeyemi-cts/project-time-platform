const INSTALL_MARKER = '__pulseFlowHiveSowEvidenceAutoadmissionInstalled';
const ENTERPRISE_PATH = /^\/api\/project-flowhive\/projects\/([0-9a-f-]{36})\/enterprise$/i;
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

function evidenceScore(item) {
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

function evidenceKey(item) {
  const category = String(item?.documentCategory || 'other').trim().toLowerCase();
  const file = String(item?.originalFileName || item?.documentId || '').trim().toLowerCase();
  return `${category}|${file}`;
}

function dedupeEvidence(evidence) {
  const groups = new Map();
  for (const item of evidence || []) {
    const key = evidenceKey(item);
    const current = groups.get(key);
    if (!current) {
      groups.set(key, { selected: item, all: [item] });
      continue;
    }
    current.all.push(item);
    if (evidenceScore(item) > evidenceScore(current.selected)) current.selected = item;
  }

  return [...groups.values()]
    .map(({ selected, all }) => ({
      ...selected,
      duplicateCount: all.length,
      duplicateDocumentIds: all.map((item) => item.documentId).filter(Boolean),
      equivalentRecordNote: all.length > 1
        ? `${all.length} equivalent active records were consolidated; the strongest current private version is shown.`
        : ''
    }))
    .sort((left, right) => evidenceScore(right) - evidenceScore(left)
      || String(left.originalFileName || '').localeCompare(String(right.originalFileName || '')));
}

function normalizeEvidenceBody(body) {
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
      explanation: approvedSowScopeReady
        ? `At least one approved, citation-ready SOW scope source is available to AI Planner.${duplicateRecordsConsolidated > 0 ? ` ${duplicateRecordsConsolidated} equivalent duplicate record(s) were consolidated.` : ''}`
        : `AI Planner is automatically preparing the strongest active Work Register SOW. It requires private processing, an active authoritative version, citation indexing, and Scope of Services citations before generation.${duplicateRecordsConsolidated > 0 ? ` ${duplicateRecordsConsolidated} equivalent duplicate record(s) were consolidated.` : ''}`
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

function isQueueCandidate(item) {
  const category = String(item?.documentCategory || '').trim().toLowerCase();
  const status = String(item?.processingStatus || 'not_requested').trim().toLowerCase();
  return !item?.readyForAiPlanner
    && ['sow', 'statement_of_work'].includes(category)
    && ['', 'not_requested', 'not_started'].includes(status)
    && Boolean(item?.documentId);
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

  const token = storedSessionToken();
  const promise = nativeFetch(
    `/api/project-flowhive/projects/${projectId}/sow-evidence/${item.documentId}/prepare`,
    {
      method: 'POST',
      credentials: 'include',
      headers: {
        'Content-Type': 'application/json',
        ...(token ? {
          Authorization: `Bearer ${token}`,
          'X-ProjectPulse-Session': token
        } : {})
      },
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

async function readNormalizedBody(response) {
  const body = await response.clone().json().catch(() => null);
  return normalizeEvidenceBody(body);
}

if (typeof window !== 'undefined' && !window[INSTALL_MARKER]) {
  const nativeFetch = window.fetch.bind(window);

  window.fetch = async (input, init = {}) => {
    const url = urlOf(input);
    const match = url?.origin === window.location.origin
      ? url.pathname.match(ENTERPRISE_PATH)
      : null;
    if (!match || methodOf(input, init) !== 'GET') return nativeFetch(input, init);

    let response = await nativeFetch(cloneInput(input), init);
    if (!response.ok || !response.headers.get('content-type')?.includes('application/json')) return response;

    let body = await readNormalizedBody(response);
    if (!body || !Array.isArray(body.sowEvidence)) return response;
    if (!body?.access?.canManage) return responseWithJson(response, body);

    const projectId = match[1];
    const candidates = body.sowEvidence.filter(isQueueCandidate).slice(0, 6);
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
    body = await readNormalizedBody(response);
    return body ? responseWithJson(response, body) : response;
  };

  window[INSTALL_MARKER] = true;
}
