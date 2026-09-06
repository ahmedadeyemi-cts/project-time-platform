export function abortableDelay(milliseconds, signal) {
  return new Promise((resolve, reject) => {
    if (signal?.aborted) { reject(new DOMException('Observation stopped', 'AbortError')); return; }
    const done = () => { signal?.removeEventListener('abort', abort); resolve(); };
    const timer = setTimeout(done, milliseconds);
    const abort = () => { clearTimeout(timer); signal?.removeEventListener('abort', abort); reject(new DOMException('Observation stopped', 'AbortError')); };
    signal?.addEventListener('abort', abort, { once: true });
  });
}

/** Observes an existing durable run. It never creates, retries or cancels server work. */
export async function observePlanner({ projectId, initial, read, onUpdate, signal,
  delay = abortableDelay, now = Date.now, maximumObservationMs = 330000 }) {
  if (!projectId || !initial?.runId || initial.projectId !== projectId) throw new Error('Planner project/run identity mismatch.');
  const until = now() + maximumObservationMs;
  let result = initial;
  let failures = 0;
  onUpdate(result);
  while (!result.terminal && now() < until) {
    await delay(Math.min(8000, failures ? 1000 * (2 ** failures) : 2000), signal);
    if (signal?.aborted) throw new DOMException('Observation stopped', 'AbortError');
    try {
      const next = await read(`/api/project-flowhive/projects/${projectId}/ai-planner/runs/${initial.runId}`, signal);
      if (next.projectId !== projectId || next.runId !== initial.runId) throw new Error('Planner response identity mismatch.');
      if (signal?.aborted) throw new DOMException('Observation stopped', 'AbortError');
      result = next;
      failures = 0;
      onUpdate(result);
    } catch (error) {
      if (signal?.aborted || error.name === 'AbortError' || error.message.includes('identity mismatch')
        || [400, 401, 403, 404].includes(error.status) || ++failures >= 3) throw error;
    }
  }
  return result;
}

export function canApplyPlannerResult(projectId, currentProjectId, startedEdit, currentEdit, result) {
  return projectId === currentProjectId && startedEdit === currentEdit
    && result?.projectId === projectId && result.terminal === true
    && result.workingDraft?.persisted === true && result.plan?.projectId === projectId;
}

export async function boundedFetch(path, options = {}, fetcher = fetch, timeoutMs = 20000) {
  const controller = new AbortController();
  const abort = () => controller.abort(options.signal?.reason);
  if (options.signal?.aborted) abort();
  else options.signal?.addEventListener('abort', abort, { once: true });
  const timer = setTimeout(() => controller.abort(new DOMException('Request timed out', 'TimeoutError')), timeoutMs);
  try {
    // Read the body before clearing the timeout: stalled response bodies are also bounded.
    const response = await fetcher(path, { ...options, signal: controller.signal });
    const body = await response.text();
    return new Response([204, 205, 304].includes(response.status) ? null : body, { status: response.status, statusText: response.statusText, headers: response.headers });
  } finally {
    clearTimeout(timer);
    options.signal?.removeEventListener('abort', abort);
  }
}
