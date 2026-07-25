import { currentProjectPulseRoute, moduleForRoute } from './module-availability-registry.js';

const INSTALL_MARKER = '__projectPulseModuleAvailabilityFetchBridgeInstalled';

function isSameOriginApiRequest(input) {
  try {
    const raw = typeof input === 'string' ? input : input?.url;
    if (!raw) return null;
    const url = new URL(raw, window.location.origin);
    if (url.origin !== window.location.origin || !url.pathname.startsWith('/api/')) return null;
    return url;
  } catch {
    return null;
  }
}

if (typeof window !== 'undefined' && !window[INSTALL_MARKER]) {
  const nativeFetch = window.fetch.bind(window);

  window.fetch = async (input, init = {}) => {
    const url = isSameOriginApiRequest(input);
    if (!url || url.pathname.startsWith('/api/module-availability')) {
      return nativeFetch(input, init);
    }

    const module = moduleForRoute(currentProjectPulseRoute());
    if (!module) return nativeFetch(input, init);

    const headers = new Headers(init?.headers || (input instanceof Request ? input.headers : undefined));
    if (!headers.has('X-ProjectPulse-Module-Number')) {
      headers.set('X-ProjectPulse-Module-Number', module.moduleNumber);
    }

    return nativeFetch(input, {
      ...init,
      headers
    });
  };

  window[INSTALL_MARKER] = true;
}
