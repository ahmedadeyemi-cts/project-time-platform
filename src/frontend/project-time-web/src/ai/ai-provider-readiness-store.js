const CACHE_KEY = 'projectPulse.aiProviderReadiness.v1';
const MIN_REFRESH_INTERVAL_MS = 15_000;
const BACKGROUND_REFRESH_INTERVAL_MS = 120_000;
const STALE_AFTER_MS = 10 * 60_000;

const listeners = new Set();
let inFlightRequest = null;
let backgroundStop = null;

const EMPTY_STATE = Object.freeze({
  phase: 'checking',
  overallStatus: 'checking',
  providers: [],
  lastCheckedAt: null,
  lastVerifiedAt: null,
  requestStartedAt: null,
  stale: true,
  source: 'startup',
  errorCode: '',
  message: 'AI provider readiness is being checked.'
});

function safeReadCache() {
  try {
    const parsed = JSON.parse(window.localStorage.getItem(CACHE_KEY) || 'null');
    if (!parsed || !Array.isArray(parsed.providers)) return EMPTY_STATE;
    const lastVerifiedAt = parsed.lastVerifiedAt || parsed.lastCheckedAt || null;
    const age = lastVerifiedAt ? Date.now() - Date.parse(lastVerifiedAt) : Number.POSITIVE_INFINITY;
    return {
      ...EMPTY_STATE,
      ...parsed,
      phase: 'idle',
      stale: !Number.isFinite(age) || age > STALE_AFTER_MS,
      source: 'verified_cache',
      errorCode: '',
      message: lastVerifiedAt
        ? 'The last verified non-secret provider status was restored while a background refresh is prepared.'
        : EMPTY_STATE.message
    };
  } catch {
    return EMPTY_STATE;
  }
}

let state = typeof window === 'undefined' ? EMPTY_STATE : safeReadCache();

function notify() {
  listeners.forEach((listener) => listener());
  if (typeof window !== 'undefined') {
    window.dispatchEvent(new CustomEvent('projectpulse:ai-provider-readiness-changed', { detail: state }));
  }
}

function setState(next) {
  state = Object.freeze({ ...state, ...next });
  notify();
}

function sanitizeProvider(provider = {}) {
  return {
    provider: String(provider.provider ?? provider.code ?? '').trim().toLowerCase(),
    displayName: String(provider.displayName ?? provider.provider ?? provider.code ?? 'Provider'),
    enabled: Boolean(provider.enabled),
    configured: Boolean(provider.configured),
    status: normalizeProviderStatus(provider),
    rawStatus: String(provider.probeStatus ?? provider.status ?? 'not_configured'),
    lastCheckedAt: provider.lastProbeAt ?? provider.lastCheckedAt ?? null,
    lastSuccessAt: provider.lastSuccessAt ?? null,
    diagnosticCode: String(provider.lastProbeFailureCode ?? provider.lastFailureCode ?? ''),
    requestId: String(provider.lastProbeRequestId ?? provider.lastRequestId ?? ''),
    rateLimits: {
      requestsRemaining: provider.rateLimits?.requestsRemaining ?? null,
      tokensRemaining: provider.rateLimits?.tokensRemaining ?? null,
      requestsReset: provider.rateLimits?.requestsReset ?? null,
      tokensReset: provider.rateLimits?.tokensReset ?? null
    }
  };
}

function normalizeProviderStatus(provider = {}) {
  const raw = `${provider.probeStatus ?? ''} ${provider.status ?? ''} ${provider.lastProbeFailureCode ?? ''} ${provider.lastFailureCode ?? ''}`.toLowerCase();
  if (!provider.enabled || !provider.configured || raw.includes('not_configured') || raw.includes('disabled')) return 'not_configured';
  if (raw.includes('checking') || raw.includes('not_checked')) return 'checking';
  if (raw.includes('401') || raw.includes('403') || raw.includes('auth') || raw.includes('credential') || raw.includes('api_key')) return 'authentication_failed';
  if (raw.includes('429') || raw.includes('rate_limit') || raw.includes('rate limit')) return 'rate_limited';
  if (raw.includes('timeout') || raw.includes('unreachable') || raw.includes('network') || raw.includes('unavailable')) return 'unavailable';
  if (raw.includes('available') || raw.includes('healthy') || raw.includes('ready') || raw.includes('success')) return 'available';
  return 'provider_error';
}

function overallStatus(providers) {
  if (!providers.length || providers.every((provider) => provider.status === 'not_configured')) return 'not_configured';
  if (providers.some((provider) => provider.status === 'available')) return 'available';
  if (providers.some((provider) => provider.status === 'checking')) return 'checking';
  if (providers.some((provider) => provider.status === 'authentication_failed')) return 'authentication_failed';
  if (providers.some((provider) => provider.status === 'rate_limited')) return 'rate_limited';
  if (providers.some((provider) => provider.status === 'unavailable')) return 'unavailable';
  return 'provider_error';
}

function persistVerified(next) {
  try {
    window.localStorage.setItem(CACHE_KEY, JSON.stringify({
      overallStatus: next.overallStatus,
      providers: next.providers,
      lastCheckedAt: next.lastCheckedAt,
      lastVerifiedAt: next.lastVerifiedAt
    }));
  } catch {
    // Readiness remains available in process memory when browser persistence is unavailable.
  }
}

async function readPayload(response) {
  const payload = await response.json().catch(() => ({}));
  if (!response.ok) {
    const error = new Error(payload.message || `AI provider readiness returned HTTP ${response.status}.`);
    error.status = response.status;
    error.code = payload.code || payload.status || `HTTP_${response.status}`;
    throw error;
  }
  return payload;
}

export function subscribeAiProviderReadiness(listener) {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

export function getAiProviderReadinessSnapshot() {
  return state;
}

export async function refreshAiProviderReadiness({ force = false, reason = 'background' } = {}) {
  if (inFlightRequest) return inFlightRequest;
  const lastChecked = state.lastCheckedAt ? Date.parse(state.lastCheckedAt) : Number.NaN;
  if (!force && Number.isFinite(lastChecked) && Date.now() - lastChecked < MIN_REFRESH_INTERVAL_MS) return state;

  const previousVerifiedStatus = state.overallStatus;
  setState({
    phase: 'checking',
    requestStartedAt: new Date().toISOString(),
    source: reason,
    errorCode: '',
    message: state.lastVerifiedAt
      ? `Refreshing provider readiness. Last verified status: ${previousVerifiedStatus}.`
      : 'Checking AI provider readiness.'
  });

  inFlightRequest = (async () => {
    try {
      const endpoint = force
        ? '/api/ai-configuration/health/refresh'
        : '/api/ai-configuration/health';
      const payload = await readPayload(await fetch(endpoint, {
        method: force ? 'POST' : 'GET',
        credentials: 'include',
        cache: 'no-store',
        headers: { Accept: 'application/json' }
      }));
      const providers = (payload.providers ?? payload.health ?? [])
        .map(sanitizeProvider)
        .filter((provider) => provider.provider);
      const checkedAt = new Date().toISOString();
      const next = {
        phase: 'idle',
        overallStatus: overallStatus(providers),
        providers,
        lastCheckedAt: checkedAt,
        lastVerifiedAt: checkedAt,
        requestStartedAt: null,
        stale: false,
        source: force ? 'manual_retest' : reason,
        errorCode: '',
        message: payload.message || 'AI provider readiness was verified.'
      };
      state = Object.freeze(next);
      persistVerified(next);
      notify();
      return state;
    } catch (error) {
      const status = error?.status === 401 || error?.status === 403
        ? 'authentication_failed'
        : error?.status === 429
          ? 'rate_limited'
          : state.lastVerifiedAt
            ? state.overallStatus
            : 'unavailable';
      setState({
        phase: 'idle',
        overallStatus: status,
        requestStartedAt: null,
        stale: true,
        source: reason,
        errorCode: String(error?.code || error?.name || 'PROVIDER_READINESS_UNAVAILABLE'),
        message: state.lastVerifiedAt
          ? `The refresh failed; the last verified non-secret status remains visible. ${error?.message || ''}`.trim()
          : error?.message || 'AI provider readiness is unavailable.'
      });
      return state;
    } finally {
      inFlightRequest = null;
    }
  })();

  return inFlightRequest;
}

export function startAiProviderReadinessMonitoring() {
  if (backgroundStop) return backgroundStop;
  let stopped = false;
  const refresh = () => {
    if (!stopped && document.visibilityState !== 'hidden') {
      void refreshAiProviderReadiness({ reason: 'authenticated_background' });
    }
  };
  void refreshAiProviderReadiness({ reason: 'authenticated_startup' });
  const timer = window.setInterval(refresh, BACKGROUND_REFRESH_INTERVAL_MS);
  window.addEventListener('focus', refresh);
  document.addEventListener('visibilitychange', refresh);
  backgroundStop = () => {
    stopped = true;
    window.clearInterval(timer);
    window.removeEventListener('focus', refresh);
    document.removeEventListener('visibilitychange', refresh);
    backgroundStop = null;
  };
  return backgroundStop;
}

export function stopAiProviderReadinessMonitoring() {
  backgroundStop?.();
}

export const AI_PROVIDER_READINESS_STATES = Object.freeze([
  'checking',
  'available',
  'unavailable',
  'not_configured',
  'authentication_failed',
  'rate_limited',
  'provider_error'
]);
