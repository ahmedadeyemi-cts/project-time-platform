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

    const body = await response.clone().json().catch(() => null);
    if (!body?.access?.canManage || !Array.isArray(body?.sowEvidence)) return response;

    const projectId = match[1];
    const candidates = body.sowEvidence.filter(isQueueCandidate).slice(0, 6);
    if (candidates.length === 0) return response;

    const results = await Promise.all(candidates.map((item) => queueEvidence(nativeFetch, projectId, item)));
    const queued = results.filter((result) => result.succeeded && result.body?.queued !== false).length;
    if (queued === 0) return response;

    window.dispatchEvent(new CustomEvent('pulse:flowhive-sow-evidence-admitted', {
      detail: {
        projectId,
        queuedDocumentCount: queued,
        privateProcessingRequested: true,
        rawDocumentSentExternally: false
      }
    }));

    response = await nativeFetch(cloneInput(input), init);
    return response;
  };

  window[INSTALL_MARKER] = true;
}
