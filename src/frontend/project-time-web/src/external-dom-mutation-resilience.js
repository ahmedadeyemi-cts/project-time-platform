const WRITING_ASSISTANT_ATTRIBUTES = Object.freeze({
  'data-gramm': 'false',
  'data-gramm_editor': 'false',
  'data-enable-grammarly': 'false'
});

const RECOVERY_STORAGE_KEY = 'projectpulse:external-dom-mutation-recovery:v1';
export const EXTERNAL_DOM_RECOVERY_WINDOW_MS = 30_000;

function setWritingAssistantOptOut(node) {
  if (!node || typeof node.setAttribute !== 'function') return;
  Object.entries(WRITING_ASSISTANT_ATTRIBUTES).forEach(([name, value]) => {
    node.setAttribute(name, value);
  });
}

function safeSessionStorage() {
  try {
    return globalThis.sessionStorage ?? null;
  } catch {
    return null;
  }
}

function currentRouteKey(locationObject = globalThis.location) {
  if (!locationObject) return 'unknown-route';
  return `${locationObject.pathname || '/'}${locationObject.search || ''}${locationObject.hash || ''}`;
}

export function protectReactOwnedRoot(root, documentObject = globalThis.document) {
  if (!root || typeof root.setAttribute !== 'function') {
    throw new Error('Pulse root mount is unavailable.');
  }

  const protectedNodes = [documentObject?.documentElement, documentObject?.body, root];
  protectedNodes.forEach(setWritingAssistantOptOut);
  root.setAttribute('data-projectpulse-react-owned-root', 'true');

  globalThis.__projectPulseExternalDomMutationResilience = Object.freeze({
    reactOwnedRootProtected: true,
    writingAssistantOptOut: true,
    recoveryWindowMs: EXTERNAL_DOM_RECOVERY_WINDOW_MS
  });

  return root;
}

export function isRecoverableExternalDomMutationError(error) {
  const name = String(error?.name || '');
  const message = String(error?.message || error || '');
  const normalized = `${name} ${message}`.toLowerCase();

  const failedRemove = normalized.includes('removechild')
    && normalized.includes('not a child');
  const failedInsert = normalized.includes('insertbefore')
    && (normalized.includes('not a child') || normalized.includes('reference node'));
  const domException = name === 'NotFoundError'
    || name === 'HierarchyRequestError'
    || normalized.includes('notfounderror')
    || normalized.includes('domexception');

  return domException && (failedRemove || failedInsert);
}

export function claimExternalDomMutationRecovery(options = {}) {
  const storage = options.storage ?? safeSessionStorage();
  const routeKey = options.routeKey ?? currentRouteKey(options.locationObject);
  const now = Number.isFinite(options.now) ? options.now : Date.now();
  const windowMs = Number.isFinite(options.windowMs)
    ? options.windowMs
    : EXTERNAL_DOM_RECOVERY_WINDOW_MS;

  if (!storage || typeof storage.getItem !== 'function' || typeof storage.setItem !== 'function') {
    return true;
  }

  try {
    const previousRaw = storage.getItem(RECOVERY_STORAGE_KEY);
    const previous = previousRaw ? JSON.parse(previousRaw) : null;
    const previousAt = Number(previous?.at || 0);
    const sameRoute = previous?.routeKey === routeKey;

    if (sameRoute && now - previousAt >= 0 && now - previousAt < windowMs) {
      return false;
    }

    storage.setItem(RECOVERY_STORAGE_KEY, JSON.stringify({ routeKey, at: now }));
    return true;
  } catch {
    return true;
  }
}

export function publishExternalDomMutationRecovery(error, info, windowObject = globalThis.window) {
  const detail = Object.freeze({
    category: 'external_dom_mutation',
    fingerprint: 'PP-DOM-OWNERSHIP',
    route: currentRouteKey(windowObject?.location),
    errorName: String(error?.name || 'DOMException'),
    message: String(error?.message || error || '').slice(0, 240),
    componentStack: String(info?.componentStack || '').slice(0, 800),
    timestamp: new Date().toISOString()
  });

  try {
    const CustomEventConstructor = windowObject?.CustomEvent ?? globalThis.CustomEvent;
    if (windowObject && typeof windowObject.dispatchEvent === 'function' && CustomEventConstructor) {
      windowObject.dispatchEvent(new CustomEventConstructor('projectpulse:ui-recovery', { detail }));
    }
  } catch {
    // Recovery telemetry is best-effort and must never block the workspace retry.
  }

  console.warn('[Pulse UI recovery] external_dom_mutation · PP-DOM-OWNERSHIP', detail);
  return detail;
}
