export const GENERATION_PHASES = ['Plan', 'Design', 'Implement', 'Validate', 'Release'];
export const generationIsActive = payload => Boolean(payload?.generationId && payload?.terminal !== true);

// Add only time since the response was received. The server's durable elapsed
// values remain authoritative even when the user's clock differs from the server.
export function generationSeconds(payload, receivedAt, now = Date.now(), observing = true) {
  const elapsed = Number(payload?.elapsedSeconds);
  const base = Number.isFinite(elapsed) ? Math.max(0, elapsed) : 0;
  return Math.floor(base + (generationIsActive(payload) && observing ? Math.max(0, now - receivedAt) / 1000 : 0));
}

export function generationPhases(payload, receivedAt, now = Date.now(), observing = true) {
  return GENERATION_PHASES.map((phase, index) => {
    const row = payload?.phaseTimeline?.find(item => item.phase === phase);
    const status = row?.status || ((payload?.completedPhases || []).includes(phase) ? 'completed' : 'pending');
    const elapsed = Number(row?.elapsedSeconds);
    return { phase, ordinal: index + 1, status, resumed: Boolean(row?.resumed || status === 'resumed'),
      seconds: row?.startedAt && row?.elapsedSeconds != null && Number.isFinite(elapsed)
        ? Math.floor(Math.max(0, elapsed) + (generationIsActive(payload) && observing && ['running', 'retrying'].includes(status) ? Math.max(0, now - receivedAt) / 1000 : 0))
        : null };
  });
}

export function generationDuration(seconds) {
  const value = Math.max(0, Math.floor(Number(seconds) || 0));
  return `${Math.floor(value / 60)}m ${String(value % 60).padStart(2, '0')}s`;
}
