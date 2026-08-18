const INSTALL_MARKER = '__pulseFlowHiveRuntimeResilienceInstalled';
const FLOWHIVE_API_PREFIX = '/api/project-flowhive/';
const RETRY_DELAYS_MS = Object.freeze([150, 450]);

function delay(milliseconds) {
  return new Promise((resolve) => window.setTimeout(resolve, milliseconds));
}

function requestUrl(input) {
  try {
    return new URL(typeof input === 'string' ? input : input?.url, window.location.origin);
  } catch {
    return null;
  }
}

function requestHeaders(input, init) {
  return new Headers(init?.headers || (input instanceof Request ? input.headers : undefined));
}

function isFlowHiveRequest(input, init) {
  const url = requestUrl(input);
  if (!url || url.origin !== window.location.origin) return false;
  if (url.pathname.startsWith(FLOWHIVE_API_PREFIX)) return true;
  return requestHeaders(input, init).get('X-ProjectPulse-Module-Number')?.trim().toUpperCase() === '066';
}

function attemptInput(input) {
  return input instanceof Request ? input.clone() : input;
}

async function isTransientAvailabilityFailure(response) {
  if (response.status !== 503) return false;
  const contentType = response.headers.get('content-type') || '';
  if (!contentType.includes('application/json')) return false;
  try {
    const body = await response.clone().json();
    return body?.status === 'module_availability_unavailable'
      && /module availability/i.test(String(body?.message || ''));
  } catch {
    return false;
  }
}

if (typeof window !== 'undefined' && !window[INSTALL_MARKER]) {
  const nativeFetch = window.fetch.bind(window);

  window.fetch = async (input, init = {}) => {
    if (!isFlowHiveRequest(input, init)) return nativeFetch(input, init);

    let response = await nativeFetch(attemptInput(input), init);
    if (!(await isTransientAvailabilityFailure(response))) return response;

    for (let attempt = 0; attempt < RETRY_DELAYS_MS.length; attempt += 1) {
      await delay(RETRY_DELAYS_MS[attempt]);
      response = await nativeFetch(attemptInput(input), init);
      if (!(await isTransientAvailabilityFailure(response))) {
        window.dispatchEvent(new CustomEvent('pulse:flowhive-availability-recovered', {
          detail: { attempts: attempt + 2, recovered: true }
        }));
        return response;
      }
    }

    window.dispatchEvent(new CustomEvent('pulse:flowhive-availability-degraded', {
      detail: { attempts: RETRY_DELAYS_MS.length + 1, recovered: false }
    }));
    return response;
  };

  window[INSTALL_MARKER] = true;
}
