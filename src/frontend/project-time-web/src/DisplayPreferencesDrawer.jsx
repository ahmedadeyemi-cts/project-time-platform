import { useCallback, useEffect, useRef, useState } from 'react';

const EXPERIENCE_STORAGE_KEY = 'pulse-enterprise-experience';
const THEME_STORAGE_KEY = 'ptp-theme';
const EXPERIENCE_EVENT = 'projectpulse:experience-changed';
const THEME_EVENT = 'projectpulse:theme-changed';
const TABLE_EXPERIENCE = 'table';
const ENTERPRISE_EXPERIENCE = 'enterprise';
const CLASSIC_EXPERIENCE = 'classic';
const EXPERIENCE_DEFAULT_VERSION_KEY = 'pulse-enterprise-experience-default-version';
const TABLE_DEFAULT_VERSION = 'table-v1';
const LIGHT_THEME = 'light';
const DARK_THEME = 'dark';

function normalizeExperience(value) {
  const normalized = String(value || '').toLowerCase();
  if ([TABLE_EXPERIENCE, ENTERPRISE_EXPERIENCE, CLASSIC_EXPERIENCE].includes(normalized)) {
    return normalized;
  }
  return TABLE_EXPERIENCE;
}

function normalizeTheme(value) {
  return String(value || '').toLowerCase() === DARK_THEME ? DARK_THEME : LIGHT_THEME;
}

function readStorage(key) {
  try {
    return window.localStorage.getItem(key) || '';
  } catch {
    return '';
  }
}

function readExperience() {
  const defaultVersion = readStorage(EXPERIENCE_DEFAULT_VERSION_KEY);
  if (defaultVersion !== TABLE_DEFAULT_VERSION) {
    try {
      window.localStorage.setItem(EXPERIENCE_STORAGE_KEY, TABLE_EXPERIENCE);
      window.localStorage.setItem(EXPERIENCE_DEFAULT_VERSION_KEY, TABLE_DEFAULT_VERSION);
    } catch {
      // Browser storage may be unavailable in hardened or private sessions.
    }
    return TABLE_EXPERIENCE;
  }
  return normalizeExperience(
    document.documentElement.dataset.pulseLayout
      || document.body?.dataset.pulseLayout
      || readStorage(EXPERIENCE_STORAGE_KEY)
      || document.documentElement.dataset.pulseExperience
      || document.body?.dataset.pulseExperience
  );
}

function readTheme() {
  return normalizeTheme(
    document.documentElement.dataset.theme
      || document.body?.dataset.theme
      || readStorage(THEME_STORAGE_KEY)
  );
}

function applyExperience(experience) {
  const normalized = normalizeExperience(experience);

  try {
    window.localStorage.setItem(EXPERIENCE_STORAGE_KEY, normalized);
    window.localStorage.setItem(EXPERIENCE_DEFAULT_VERSION_KEY, TABLE_DEFAULT_VERSION);
  } catch {
    // Browser storage may be unavailable in hardened or private sessions.
  }

  const presentationExperience = normalized === TABLE_EXPERIENCE
    ? ENTERPRISE_EXPERIENCE
    : normalized;
  document.documentElement.dataset.pulseExperience = presentationExperience;
  document.documentElement.dataset.pulseLayout = normalized;
  if (document.body) {
    document.body.dataset.pulseExperience = presentationExperience;
    document.body.dataset.pulseLayout = normalized;
  }

  window.dispatchEvent(new CustomEvent(EXPERIENCE_EVENT, {
    detail: { experience: normalized }
  }));

  return normalized;
}

function ViewIcon({ view }) {
  if (view === TABLE_EXPERIENCE) {
    return (
      <svg viewBox="0 0 24 24" aria-hidden="true">
        <rect x="3" y="4" width="18" height="16" rx="2" />
        <path d="M3 9h18M3 14h18M9 4v16" />
      </svg>
    );
  }
  if (view === ENTERPRISE_EXPERIENCE) {
    return (
      <svg viewBox="0 0 24 24" aria-hidden="true">
        <rect x="3" y="3" width="7" height="7" rx="1.5" />
        <rect x="14" y="3" width="7" height="7" rx="1.5" />
        <rect x="3" y="14" width="7" height="7" rx="1.5" />
        <rect x="14" y="14" width="7" height="7" rx="1.5" />
      </svg>
    );
  }
  return (
    <svg viewBox="0 0 24 24" aria-hidden="true">
      <path d="M4 6h16M4 12h16M4 18h16" />
    </svg>
  );
}

function ThemeIcon({ dark }) {
  return dark ? (
    <svg viewBox="0 0 24 24" aria-hidden="true">
      <path d="M20.2 15.1A8.4 8.4 0 0 1 8.9 3.8 8.5 8.5 0 1 0 20.2 15.1Z" />
    </svg>
  ) : (
    <svg viewBox="0 0 24 24" aria-hidden="true">
      <circle cx="12" cy="12" r="4" />
      <path d="M12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4" />
    </svg>
  );
}

function ChoiceStatus() {
  return (
    <span className="pulse-display-choice__status" aria-hidden="true">
      <svg viewBox="0 0 24 24">
        <path d="m7 12 3 3 7-7" />
      </svg>
    </span>
  );
}

export default function DisplayPreferencesDrawer() {
  const [open, setOpen] = useState(false);
  const [activeSection, setActiveSection] = useState('view');
  const [experience, setExperience] = useState(() => readExperience());
  const [theme, setTheme] = useState(() => readTheme());
  const viewHandleRef = useRef(null);
  const closeButtonRef = useRef(null);
  const viewSectionRef = useRef(null);
  const themeSectionRef = useRef(null);
  const lastTriggerRef = useRef(null);

  const synchronizePreferences = useCallback(() => {
    setExperience(readExperience());
    setTheme(readTheme());
  }, []);

  useEffect(() => {
    const onExperienceChanged = (event) => {
      setExperience(normalizeExperience(event?.detail?.experience || readExperience()));
    };
    const onThemeChanged = (event) => {
      setTheme(normalizeTheme(event?.detail?.theme || readTheme()));
    };
    const onStorage = (event) => {
      if (!event.key || event.key === EXPERIENCE_STORAGE_KEY || event.key === THEME_STORAGE_KEY) {
        synchronizePreferences();
      }
    };

    window.addEventListener(EXPERIENCE_EVENT, onExperienceChanged);
    window.addEventListener(THEME_EVENT, onThemeChanged);
    window.addEventListener('storage', onStorage);
    window.addEventListener('pageshow', synchronizePreferences);

    return () => {
      window.removeEventListener(EXPERIENCE_EVENT, onExperienceChanged);
      window.removeEventListener(THEME_EVENT, onThemeChanged);
      window.removeEventListener('storage', onStorage);
      window.removeEventListener('pageshow', synchronizePreferences);
    };
  }, [synchronizePreferences]);

  const closeDrawer = useCallback(() => {
    setOpen(false);
    window.setTimeout(() => lastTriggerRef.current?.focus(), 0);
  }, []);

  const openDrawer = useCallback((section, trigger) => {
    lastTriggerRef.current = trigger;
    setActiveSection(section);
    setOpen(true);
  }, []);

  useEffect(() => {
    document.body.classList.toggle('pulse-display-preferences-open', open);

    if (open) {
      window.setTimeout(() => {
        closeButtonRef.current?.focus();
        const section = activeSection === 'theme' ? themeSectionRef.current : viewSectionRef.current;
        section?.scrollIntoView({ block: 'nearest' });
      }, 0);
    }

    const onKeyDown = (event) => {
      if (event.key === 'Escape' && open) closeDrawer();
    };

    window.addEventListener('keydown', onKeyDown);
    return () => {
      document.body.classList.remove('pulse-display-preferences-open');
      window.removeEventListener('keydown', onKeyDown);
    };
  }, [activeSection, closeDrawer, open]);

  const changeExperience = (nextExperience) => {
    const normalized = applyExperience(nextExperience);
    setExperience(normalized);
  };

  const experienceLabel = experience === TABLE_EXPERIENCE
    ? 'Table'
    : experience === ENTERPRISE_EXPERIENCE
      ? 'Enterprise'
      : 'Classic';
  const currentSummary = `${experienceLabel} view · ${theme === DARK_THEME ? 'Dark' : 'Light'} appearance`;

  return (
    <>
      <button
        ref={viewHandleRef}
        type="button"
        className="pulse-display-handle pulse-display-view-handle pulse-display-appearance-handle"
        onClick={() => openDrawer('view', viewHandleRef.current)}
        aria-expanded={open}
        aria-controls="pulse-display-preferences-drawer"
        title="Open interface and appearance settings"
      >
        Appearance
      </button>

      <aside
        id="pulse-display-preferences-drawer"
        className={`pulse-display-preferences-drawer ${open ? 'is-open' : ''}`}
        role="dialog"
        aria-modal="true"
        aria-hidden={!open}
        aria-labelledby="pulse-display-preferences-title"
        data-active-section={activeSection}
      >
        <header className="pulse-display-preferences-drawer__header">
          <div>
            <p>Pulse preferences</p>
            <h2 id="pulse-display-preferences-title">Display &amp; appearance</h2>
            <small>Choose the workspace presentation that is easiest for you to use.</small>
          </div>
          <button
            ref={closeButtonRef}
            type="button"
            onClick={closeDrawer}
            aria-label="Close display and appearance settings"
          >
            ×
          </button>
        </header>

        <div className="pulse-display-preferences-drawer__content">
          <section className="pulse-display-preferences-summary" aria-label="Current display selection">
            <span>Current selection</span>
            <strong>{currentSummary}</strong>
            <p>These choices affect your interface only. They do not change your permissions, role, or data access.</p>
          </section>

          <section
            ref={viewSectionRef}
            className={`pulse-display-preference-section ${activeSection === 'view' ? 'is-target' : ''}`}
            aria-labelledby="pulse-display-view-heading"
          >
            <div className="pulse-display-preference-section__heading">
              <span className="pulse-display-preference-section__icon" aria-hidden="true">
                <ViewIcon view={experience} />
              </span>
              <div>
                <p>Interface view</p>
                <h3 id="pulse-display-view-heading">Choose your workspace layout</h3>
              </div>
            </div>

            <div className="pulse-display-choice-grid">
              <button
                type="button"
                className={`pulse-display-choice ${experience === TABLE_EXPERIENCE ? 'is-selected' : ''}`}
                aria-pressed={experience === TABLE_EXPERIENCE}
                onClick={() => changeExperience(TABLE_EXPERIENCE)}
              >
                <span className="pulse-display-choice__icon" aria-hidden="true"><ViewIcon view={TABLE_EXPERIENCE} /></span>
                <span className="pulse-display-choice__copy">
                  <strong>Table</strong>
                  <small>Dense, table-first administration with sortable operational rows and inline ownership controls.</small>
                </span>
                {experience === TABLE_EXPERIENCE ? <ChoiceStatus /> : null}
              </button>

              <button
                type="button"
                className={`pulse-display-choice ${experience === ENTERPRISE_EXPERIENCE ? 'is-selected' : ''}`}
                aria-pressed={experience === ENTERPRISE_EXPERIENCE}
                onClick={() => changeExperience(ENTERPRISE_EXPERIENCE)}
              >
                <span className="pulse-display-choice__icon" aria-hidden="true"><ViewIcon view={ENTERPRISE_EXPERIENCE} /></span>
                <span className="pulse-display-choice__copy">
                  <strong>Enterprise</strong>
                  <small>Unified cards, page context, responsive workspaces, and enterprise styling.</small>
                </span>
                {experience === ENTERPRISE_EXPERIENCE ? <ChoiceStatus /> : null}
              </button>

              <button
                type="button"
                className={`pulse-display-choice ${experience === CLASSIC_EXPERIENCE ? 'is-selected' : ''}`}
                aria-pressed={experience === CLASSIC_EXPERIENCE}
                onClick={() => changeExperience(CLASSIC_EXPERIENCE)}
              >
                <span className="pulse-display-choice__icon" aria-hidden="true"><ViewIcon view={CLASSIC_EXPERIENCE} /></span>
                <span className="pulse-display-choice__copy">
                  <strong>Classic</strong>
                  <small>Use the established Pulse presentation while keeping the same role permissions.</small>
                </span>
                {experience === CLASSIC_EXPERIENCE ? <ChoiceStatus /> : null}
              </button>
            </div>
          </section>

          <section
            ref={themeSectionRef}
            className={`pulse-display-preference-section ${activeSection === 'theme' ? 'is-target' : ''}`}
            aria-labelledby="pulse-display-theme-heading"
          >
            <div className="pulse-display-preference-section__heading">
              <span className="pulse-display-preference-section__icon" aria-hidden="true">
                <ThemeIcon dark={theme === DARK_THEME} />
              </span>
              <div>
                <p>Appearance</p>
                <h3 id="pulse-display-theme-heading">Light or dark theme</h3>
              </div>
            </div>

            <div className="pulse-display-choice-grid pulse-display-theme-grid">
              <button
                type="button"
                className={`pulse-display-choice pulse-display-theme-choice ${theme === LIGHT_THEME ? 'is-selected active' : ''}`}
                data-pulse-theme-choice={LIGHT_THEME}
                aria-pressed={theme === LIGHT_THEME}
                onClick={() => setTheme(LIGHT_THEME)}
              >
                <span className="pulse-display-choice__icon" aria-hidden="true"><ThemeIcon dark={false} /></span>
                <span className="pulse-display-choice__copy">
                  <strong>Light</strong>
                  <small>Bright surfaces with high-contrast navy text.</small>
                </span>
                {theme === LIGHT_THEME ? <ChoiceStatus /> : null}
              </button>

              <button
                type="button"
                className={`pulse-display-choice pulse-display-theme-choice ${theme === DARK_THEME ? 'is-selected active' : ''}`}
                data-pulse-theme-choice={DARK_THEME}
                aria-pressed={theme === DARK_THEME}
                onClick={() => setTheme(DARK_THEME)}
              >
                <span className="pulse-display-choice__icon" aria-hidden="true"><ThemeIcon dark /></span>
                <span className="pulse-display-choice__copy">
                  <strong>Dark</strong>
                  <small>Reduced-glare surfaces with readable light text.</small>
                </span>
                {theme === DARK_THEME ? <ChoiceStatus /> : null}
              </button>
            </div>
          </section>

          <section className="pulse-display-preferences-note">
            <strong>Preference behavior</strong>
            <p>Your selections are saved for this browser. Signed-in theme preferences continue to use the existing governed profile-preference path.</p>
          </section>
        </div>

        <footer className="pulse-display-preferences-drawer__footer">
          <span>Pulse display settings</span>
          <span>{currentSummary}</span>
        </footer>
      </aside>

      {open ? (
        <button
          type="button"
          className="pulse-display-preferences-backdrop"
          onClick={closeDrawer}
          aria-label="Close display and appearance settings"
        />
      ) : null}
    </>
  );
}
