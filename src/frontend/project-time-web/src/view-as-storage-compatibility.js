const CURRENT_VIEW_AS_KEY = 'projectPulseViewAsUser';
const LEGACY_VIEW_AS_KEY = 'projectPulseViewAsUserId';
const VIEW_AS_REQUEST_BRIDGE_MARKER = '__projectPulseViewAsRequestBridgeInstalled';
const VIEW_AS_XHR_BRIDGE_MARKER = '__projectPulseViewAsXhrBridgeInstalled';
const ADMINISTRATOR_FETCH_KEY = '__projectPulseAdministratorFetch';
const READ_ONLY_METHODS = new Set(['GET', 'HEAD', 'OPTIONS']);
const ADMINISTRATOR_BYPASS_PATHS = new Set([
  '/api/project-workspace/view-as/users'
]);

function publishCompatibilityChange(userId) {
  window.dispatchEvent(new CustomEvent('projectpulse:view-as-changed', {
    detail: {
      userId: userId || null,
      active: Boolean(userId),
      compatibilitySource: LEGACY_VIEW_AS_KEY
    }
  }));
}

function consumeLegacyViewAsKey() {
  window.localStorage.removeItem(LEGACY_VIEW_AS_KEY);
}

function readCurrentViewAs() {
  try {
    const value = JSON.parse(window.localStorage.getItem(CURRENT_VIEW_AS_KEY) || 'null');
    const userId = String(value?.userId || '').trim();
    return userId ? { ...value, userId } : null;
  } catch {
    return null;
  }
}

function normalizeLegacyViewAsStorage() {
  if (typeof window === 'undefined') return;

  try {
    const currentRaw = window.localStorage.getItem(CURRENT_VIEW_AS_KEY);
    const legacyUserId = String(window.localStorage.getItem(LEGACY_VIEW_AS_KEY) || '').trim();

    if (!legacyUserId) return;

    let currentRecord = null;
    if (currentRaw) {
      try {
        currentRecord = JSON.parse(currentRaw);
      } catch {
        // A malformed current record has no usable View-As identity. Preserve
        // the valid legacy restriction by replacing it below.
        currentRecord = null;
      }
    }

    const currentUserId = String(currentRecord?.userId || '').trim();
    if (currentUserId) {
      // A usable current selection is authoritative. Consume the stale legacy
      // value so Exit View-As cannot recreate a prior selection.
      consumeLegacyViewAsKey();
      return;
    }

    // Missing, null, malformed, or otherwise unusable current state must not
    // discard a valid legacy View-As selection. Migrate it before App renders.
    window.localStorage.setItem(CURRENT_VIEW_AS_KEY, JSON.stringify({
      userId: legacyUserId,
      compatibilitySource: LEGACY_VIEW_AS_KEY
    }));
    consumeLegacyViewAsKey();
    publishCompatibilityChange(legacyUserId);
  } catch {
    // The consuming authority checks fail closed when browser storage cannot be
    // read. This bridge only preserves an existing View-As restriction and
    // never grants administrator authority.
  }
}

function requestDescriptor(input, init = {}) {
  try {
    const rawUrl = typeof input === 'string' ? input : input?.url;
    if (!rawUrl) return null;
    const url = new URL(rawUrl, window.location.origin);
    const method = String(
      init?.method || (input instanceof Request ? input.method : '') || 'GET'
    ).toUpperCase();
    return { url, method };
  } catch {
    return null;
  }
}

function isAdministratorBypassPath(pathname) {
  return pathname.startsWith('/api/auth/') || ADMINISTRATOR_BYPASS_PATHS.has(pathname);
}

function viewAsReadOnlyResponse() {
  return new Response(JSON.stringify({
    status: 'view_as_read_only',
    message: 'Write actions are disabled while using Administrator View-As preview. Exit preview to make changes.',
    contract: 'VIEW_AS_EFFECTIVE_REQUEST_BRIDGE_V1'
  }), {
    status: 403,
    headers: {
      'Content-Type': 'application/json; charset=utf-8',
      'Cache-Control': 'no-store'
    }
  });
}

function installViewAsFetchBridge() {
  if (typeof window === 'undefined'
      || typeof window.fetch !== 'function'
      || window[VIEW_AS_REQUEST_BRIDGE_MARKER]) {
    return;
  }

  const administratorFetch = window.fetch.bind(window);
  if (typeof window[ADMINISTRATOR_FETCH_KEY] !== 'function') {
    // This bypass is reserved for the View-As selector's administrator-only
    // identity discovery. Normal application reads continue through window.fetch.
    window[ADMINISTRATOR_FETCH_KEY] = administratorFetch;
  }

  window.fetch = async (input, init = {}) => {
    const descriptor = requestDescriptor(input, init);
    const viewAs = readCurrentViewAs();

    if (!descriptor
        || !viewAs
        || descriptor.url.origin !== window.location.origin
        || !descriptor.url.pathname.startsWith('/api/')
        || isAdministratorBypassPath(descriptor.url.pathname)) {
      return administratorFetch(input, init);
    }

    if (!READ_ONLY_METHODS.has(descriptor.method)) {
      return viewAsReadOnlyResponse();
    }

    const headers = new Headers(
      init?.headers || (input instanceof Request ? input.headers : undefined)
    );
    headers.set('X-ProjectPulse-View-As-User', viewAs.userId);

    return administratorFetch(input, {
      ...init,
      credentials: init?.credentials || 'include',
      headers
    });
  };

  window[VIEW_AS_REQUEST_BRIDGE_MARKER] = true;
  window.__projectPulseViewAsRequestBridge = Object.freeze({
    contract: 'VIEW_AS_EFFECTIVE_REQUEST_BRIDGE_V1',
    storageKey: CURRENT_VIEW_AS_KEY,
    header: 'X-ProjectPulse-View-As-User',
    writesBlocked: true,
    administratorBypassPaths: [...ADMINISTRATOR_BYPASS_PATHS]
  });
}

function installViewAsXhrBridge() {
  if (typeof window === 'undefined'
      || typeof window.XMLHttpRequest === 'undefined'
      || window[VIEW_AS_XHR_BRIDGE_MARKER]) {
    return;
  }

  const prototype = window.XMLHttpRequest.prototype;
  const nativeOpen = prototype.open;
  const nativeSend = prototype.send;
  const nativeSetRequestHeader = prototype.setRequestHeader;

  prototype.open = function projectPulseViewAsXhrOpen(method, url, async, user, password) {
    this.__projectPulseViewAsDescriptor = requestDescriptor(url, { method });
    this.__projectPulseViewAsHeaderNames = new Set();
    return nativeOpen.call(this, method, url, async, user, password);
  };

  prototype.setRequestHeader = function projectPulseViewAsXhrSetHeader(name, value) {
    if (this.__projectPulseViewAsHeaderNames instanceof Set) {
      this.__projectPulseViewAsHeaderNames.add(String(name || '').trim().toLowerCase());
    }
    return nativeSetRequestHeader.call(this, name, value);
  };

  prototype.send = function projectPulseViewAsXhrSend(body) {
    const descriptor = this.__projectPulseViewAsDescriptor;
    const viewAs = readCurrentViewAs();
    const headerNames = this.__projectPulseViewAsHeaderNames instanceof Set
      ? this.__projectPulseViewAsHeaderNames
      : new Set();

    if (descriptor
        && viewAs
        && descriptor.url.origin === window.location.origin
        && descriptor.url.pathname.startsWith('/api/')
        && !isAdministratorBypassPath(descriptor.url.pathname)
        && !headerNames.has('x-projectpulse-view-as-user')) {
      try {
        this.setRequestHeader('X-ProjectPulse-View-As-User', viewAs.userId);
      } catch {
        // The backend remains authoritative if a browser disallows mutation.
      }
    }

    return nativeSend.call(this, body);
  };

  window[VIEW_AS_XHR_BRIDGE_MARKER] = true;
}

normalizeLegacyViewAsStorage();
installViewAsFetchBridge();
installViewAsXhrBridge();

if (typeof window !== 'undefined') {
  window.addEventListener('storage', (event) => {
    if (event.key === CURRENT_VIEW_AS_KEY || event.key === LEGACY_VIEW_AS_KEY) {
      normalizeLegacyViewAsStorage();
    }
  });

  window.addEventListener('projectpulse:auth-session-ready', normalizeLegacyViewAsStorage);
}

export {
  installViewAsFetchBridge,
  installViewAsXhrBridge,
  normalizeLegacyViewAsStorage,
  readCurrentViewAs
};
