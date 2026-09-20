import { useCallback, useEffect, useRef, useState } from 'react';
import { generationIsActive } from './generation-progress.js';

const POLL_INTERVAL_MS = 5000;
// The worker currently permits forty minutes. Keep monitoring through its
// durable deadline plus two minutes for terminal persistence, with a bound if
// an older response omits deadline metadata.
const MAX_POLL_ATTEMPTS = 504;
const empty = engagementId => ({ engagementId, payload: null, receivedAt: Date.now(), checking: Boolean(engagementId), error: '', observing: false });

// This hook only reads durable job state. Resuming an AI job always requires an
// explicit /generate POST from the authoring workspace.
export default function useGenerationMonitor({ engagementId, revision, identityKey, request, onComplete }) {
  const [state, setState] = useState(() => empty(engagementId));
  const [checkKey, setCheckKey] = useState(0);
  const epoch = useRef(0);
  const callbacks = useRef({ request, onComplete });
  callbacks.current = { request, onComplete };
  const recheck = useCallback(() => {
    setState(current => ({ ...current, checking: true, error: '', observing: false }));
    setCheckKey(value => value + 1);
  }, []);

  useEffect(() => {
    const token = ++epoch.current;
    const controller = new AbortController();
    const relevant = () => epoch.current === token && !controller.signal.aborted;
    setState(empty(engagementId));
    if (!engagementId) return () => controller.abort();
    (async () => {
      try {
        const payload = await callbacks.current.request(`/api/module025/sow-gsd/${engagementId}/generations/latest`, { signal: controller.signal });
        if (!relevant()) return;
        setState({ engagementId, payload, receivedAt: Date.now(), checking: false, observing: generationIsActive(payload), error: '' });
        // The job can commit between the engagement read and this discovery.
        // Refresh once only when the current saved revision is newer.
        if (payload?.status === 'module025_detailed_scope_generated' && Number(payload.currentRevision) > Number(revision))
          callbacks.current.onComplete?.(payload, engagementId);
      } catch (error) {
        if (relevant()) setState({ ...empty(engagementId), checking: false, error: error.message || 'Generation status could not be checked.' });
      }
    })();
    return () => { controller.abort(); };
  }, [engagementId, revision, identityKey, checkKey]);

  const track = useCallback(payload => {
    ++epoch.current; // Ignore a slower discovery response if a new job was just queued.
    setState({ engagementId, payload, receivedAt: Date.now(), checking: false, observing: generationIsActive(payload), error: '' });
  }, [engagementId]);

  const generationId = state.engagementId === engagementId && !state.checking && !state.error && generationIsActive(state.payload) ? state.payload.generationId : null;
  useEffect(() => {
    if (!generationId || !engagementId) return undefined;
    const controller = new AbortController();
    let timer;
    let attempts = 0;
    const token = epoch.current;
    const relevant = () => token === epoch.current && !controller.signal.aborted;
    const poll = async () => {
      try {
        const payload = await callbacks.current.request(`/api/module025/sow-gsd/${engagementId}/generations/${generationId}`, { signal: controller.signal });
        if (!relevant()) return;
        setState({ engagementId, payload, receivedAt: Date.now(), checking: false, observing: generationIsActive(payload), error: '' });
        if (payload?.terminal === true) {
          callbacks.current.onComplete?.(payload, engagementId);
          return;
        }
        const serverNow = Date.parse(payload?.serverNow || '');
        const deadline = Date.parse(payload?.deadlineAt || '');
        const expired = Number.isFinite(serverNow) && Number.isFinite(deadline) && serverNow > deadline + 120000;
        if (++attempts >= MAX_POLL_ATTEMPTS || expired) throw new Error('Status monitoring reached its time limit. Completion has not been verified. Check generation status to reconnect before retrying.');
        timer = window.setTimeout(poll, POLL_INTERVAL_MS);
      } catch (error) {
        if (relevant()) setState(current => ({ ...current, checking: false, observing: false, error: error.message || 'Generation monitoring was interrupted. Check status to reconnect.' }));
      }
    };
    void poll();
    return () => { controller.abort(); window.clearTimeout(timer); };
  }, [engagementId, generationId, identityKey, checkKey]);

  const visible = state.engagementId === engagementId ? state : empty(engagementId);
  return { ...visible, track, recheck, busy: Boolean(engagementId && (visible.checking || visible.error || generationIsActive(visible.payload))) };
}
