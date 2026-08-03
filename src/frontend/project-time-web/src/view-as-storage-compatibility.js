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
