const CURRENT_VIEW_AS_KEY = 'projectPulseViewAsUser';
const LEGACY_VIEW_AS_KEY = 'projectPulseViewAsUserId';

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
        // The malformed current record already fails closed. Consume the stale
        // legacy key so a later Exit View-As cannot recreate it.
        consumeLegacyViewAsKey();
        return;
      }
    }

    const compatibilityOwned =
      currentRecord?.compatibilitySource === LEGACY_VIEW_AS_KEY;

    if (currentRaw && !compatibilityOwned) {
      // The current contract is authoritative. Remove the stale legacy value
      // without replacing the active selection.
      consumeLegacyViewAsKey();
      return;
    }

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

normalizeLegacyViewAsStorage();

if (typeof window !== 'undefined') {
  window.addEventListener('storage', (event) => {
    if (event.key === CURRENT_VIEW_AS_KEY || event.key === LEGACY_VIEW_AS_KEY) {
      normalizeLegacyViewAsStorage();
    }
  });

  window.addEventListener('projectpulse:auth-session-ready', normalizeLegacyViewAsStorage);
}

export { normalizeLegacyViewAsStorage };
