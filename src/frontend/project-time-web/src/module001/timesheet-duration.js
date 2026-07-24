export const QUARTER_HOUR_SECONDS = 15 * 60;
export const MAX_TIMER_SECONDS = 12 * 60 * 60;

function assertFiniteNonNegative(value, label) {
  if (!Number.isFinite(value) || value < 0) {
    throw new TypeError(`${label} must be a finite, non-negative number.`);
  }
}

export function capElapsedSeconds(actualSeconds) {
  assertFiniteNonNegative(actualSeconds, 'actualSeconds');
  return Math.min(Math.floor(actualSeconds), MAX_TIMER_SECONDS);
}

export function roundSecondsUpToQuarterHour(actualSeconds) {
  assertFiniteNonNegative(actualSeconds, 'actualSeconds');
  const capped = capElapsedSeconds(actualSeconds);
  if (capped === 0) return 0;
  return Math.min(
    Math.ceil(capped / QUARTER_HOUR_SECONDS) * 15,
    MAX_TIMER_SECONDS / 60
  );
}

export function calculateTimerDuration(startedAtUtc, nowUtc = new Date()) {
  const start = new Date(startedAtUtc);
  const now = new Date(nowUtc);
  if (Number.isNaN(start.getTime()) || Number.isNaN(now.getTime())) {
    throw new TypeError('startedAtUtc and nowUtc must be valid dates.');
  }
  const actualSeconds = Math.max(0, Math.floor((now.getTime() - start.getTime()) / 1000));
  const cappedSeconds = capElapsedSeconds(actualSeconds);
  return {
    actualSeconds,
    cappedSeconds,
    roundedMinutes: roundSecondsUpToQuarterHour(actualSeconds),
    isExpired: actualSeconds >= MAX_TIMER_SECONDS
  };
}

export function formatElapsedSeconds(value) {
  assertFiniteNonNegative(value, 'value');
  const seconds = capElapsedSeconds(value);
  const hours = Math.floor(seconds / 3600);
  const minutes = Math.floor((seconds % 3600) / 60);
  const remainingSeconds = seconds % 60;
  return [hours, minutes, remainingSeconds]
    .map((part) => String(part).padStart(2, '0'))
    .join(':');
}
