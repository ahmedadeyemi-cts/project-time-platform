const CURRENT_VIEW_AS_KEY = 'projectPulseViewAsUser';
const LEGACY_VIEW_AS_KEY = 'projectPulseViewAsUserId';

function normalizeLegacyViewAsStorage() {
  if (typeof window === 'undefined') return;

  try {
    const current = window.localStorage.getItem(CURRENT_VIEW_AS_KEY);
    const legacyUserId = String(window.localStorage.getItem(LEGACY_VIEW_AS_KEY) || '').trim();

    if (current || !legacyUserId) return;

    window.localStorage.setItem(CURRENT_VIEW_AS_KEY, JSON.stringify({
      userId: legacyUserId,
      compatibilitySource: LEGACY_VIEW_AS_KEY
    }));

    window.dispatchEvent(new CustomEvent('projectpulse:view-as-changed', {
      detail: {
        userId: legacyUserId,
        compatibilitySource: LEGACY_VIEW_AS_KEY
      }
    }));
  } catch {
    // The consuming authority checks fail closed when browser storage cannot be
    // read or contains malformed state. This bridge never grants authority.
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
