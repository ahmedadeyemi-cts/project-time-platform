import {
  useCallback,
  useEffect,
  useLayoutEffect,
  useMemo,
  useRef,
  useState
} from 'react';
import { createPortal } from 'react-dom';
import IdentityAvatar from './identity/IdentityAvatar.jsx';

/* PR698_ACCOUNT_CENTER
 * Reuses Pulse's existing authenticated session, Module 062 identity profile,
 * profile-preference endpoint, theme events, and View-As storage. It does not
 * create another identity, authentication, session, profile-photo, or theme
 * authority.
 */

const AUTH_SESSION_KEY = 'projectPulseAuthSession';
const VIEW_AS_STORAGE_KEY = 'projectPulseViewAsUser';
const THEME_STORAGE_KEY = 'ptp-theme';
const EXPERIENCE_STORAGE_KEY = 'pulse-enterprise-experience';
const THEME_EVENT = 'projectpulse:theme-changed';
const EXPERIENCE_EVENT = 'projectpulse:experience-changed';
const PROFILE_CHANGED_EVENT = 'projectpulse:identity-profile-changed';
const ACCOUNT_OVERLAY_EVENT = 'projectpulse:account-center-overlay-opened';

const MAX_PROFILE_IMAGE_BYTES = 2 * 1024 * 1024;
const ACCEPTED_PROFILE_IMAGE_TYPES = new Set(['image/jpeg', 'image/png']);

const ACCOUNT_SECTIONS = Object.freeze({
  profile: Object.freeze({
    route: 'account-profile',
    title: 'Profile',
    description: 'Update your personal information and professional achievements.'
  }),
  appearance: Object.freeze({
    route: 'account-appearance',
    title: 'Appearance',
    description: 'Customize how Pulse looks and behaves for you.'
  }),
  session: Object.freeze({
    route: 'account-session',
    title: 'Session',
    description: 'View information about your current session.'
  })
});

const SECTION_BY_ROUTE = new Map(
  Object.entries(ACCOUNT_SECTIONS).map(([section, metadata]) => [metadata.route, section])
);

const LEGACY_ACCOUNT_ROUTES = Object.freeze({
  profile: 'account-profile',
  'my-profile': 'account-profile',
  'profile-settings': 'account-profile',
  settings: 'account-appearance',
  'my-settings': 'account-appearance',
  preferences: 'account-appearance',
  session: 'account-session',
  'current-session': 'account-session'
});

const ICON_PATHS = Object.freeze({
  profile: 'M12 12a4 4 0 1 0 0-8 4 4 0 0 0 0 8Zm-7 8a7 7 0 0 1 14 0',
  appearance: 'M12 3a9 9 0 1 0 9 9c0-1.7-1.4-3-3.1-3H16a2 2 0 0 1-2-2V5.1C14 3.9 13.1 3 12 3Zm-4.5 9.5h.01m2.99-4h.01m4 7h.01',
  session: 'M4 5h16v12H4zM9 21h6M12 17v4',
  signout: 'M10 5H5v14h5M14 8l4 4-4 4M18 12H9',
  chevron: 'm9 18 6-6-6-6',
  camera: 'M4 8h3l2-3h6l2 3h3v11H4zM12 17a4 4 0 1 0 0-8 4 4 0 0 0 0 8Z',
  refresh: 'M20 11a8 8 0 1 0-2.3 5.7M20 5v6h-6',
  shield: 'M12 3 5 6v5c0 4.7 2.8 8 7 10 4.2-2 7-5.3 7-10V6l-7-3Z',
  info: 'M12 11v6M12 7h.01',
  close: 'm6 6 12 12M18 6 6 18',
  add: 'M12 5v14M5 12h14',
  trash: 'M5 7h14M9 7V4h6v3m-8 0 1 13h8l1-13M10 11v5M14 11v5'
});

function Icon({ name, className = '' }) {
  return (
    <svg
      className={className}
      viewBox="0 0 24 24"
      aria-hidden="true"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.8"
      strokeLinecap="round"
      strokeLinejoin="round"
    >
      <path d={ICON_PATHS[name] || ICON_PATHS.info} />
    </svg>
  );
}

function readJsonStorage(key) {
  try {
    const raw = window.localStorage.getItem(key);
    return raw ? JSON.parse(raw) : null;
  } catch {
    return null;
  }
}

function readAuthSession() {
  const session = readJsonStorage(AUTH_SESSION_KEY);
  if (!session?.sessionToken && !session?.token && !session?.accessToken) return null;
  if (session?.expiresAt && Date.now() >= Date.parse(session.expiresAt)) return null;
  return session;
}

function readViewAs() {
  const viewAs = readJsonStorage(VIEW_AS_STORAGE_KEY);
  return viewAs?.userId ? viewAs : null;
}

function sessionToken(session) {
  return session?.sessionToken || session?.token || session?.accessToken || '';
}

function authHeaders(session, includeJson = false) {
  const token = sessionToken(session);
  const headers = includeJson ? { 'Content-Type': 'application/json' } : {};
  if (!token) return headers;

  return {
    ...headers,
    Authorization: `Bearer ${token}`,
    'X-ProjectPulse-Session': token,
    'X-Project-Pulse-Session': token,
    'X-Session-Token': token
  };
}

function originalFetch() {
  return typeof window.__projectPulseOriginalFetch === 'function'
    ? window.__projectPulseOriginalFetch
    : window.fetch.bind(window);
}

async function readJsonResponse(response) {
  const text = await response.text();
  if (!text) return {};
  try {
    return JSON.parse(text);
  } catch {
    return { message: text };
  }
}

async function loadSignedInProfile(session, signal) {
  const response = await originalFetch()('/api/identity/profile', {
    headers: authHeaders(session),
    credentials: 'include',
    cache: 'no-store',
    signal
  });
  const payload = await readJsonResponse(response);
  if (!response.ok) {
    throw new Error(payload.message || `Identity profile returned HTTP ${response.status}`);
  }
  return payload.profile || payload;
}

function preferenceStorageKey(session) {
  const username = String(session?.username || session?.email || 'anonymous').toLowerCase();
  return `projectPulseUserPreferences:${username}`;
}

function currentResolvedTheme() {
  const declared = document.documentElement.dataset.theme
    || document.body?.dataset.theme
    || window.localStorage.getItem(THEME_STORAGE_KEY)
    || 'light';
  return String(declared).toLowerCase() === 'dark' ? 'dark' : 'light';
}

function currentExperience() {
  const declared = document.documentElement.dataset.pulseExperience
    || document.body?.dataset.pulseExperience
    || window.localStorage.getItem(EXPERIENCE_STORAGE_KEY)
    || 'enterprise';
  return String(declared).toLowerCase() === 'classic' ? 'classic' : 'enterprise';
}

function defaultPreferences(session) {
  const resolvedTheme = currentResolvedTheme();
  return {
    theme: resolvedTheme,
    themePreference: resolvedTheme,
    workspaceLayout: currentExperience(),
    profilePhotoDataUrl: '',
    profileImageRemoved: false,
    awardsAndCertificates: '',
    displayNameOverride: '',
    titleOverride: '',
    username: session?.username || session?.email || ''
  };
}

function readLocalPreferences(session) {
  const defaults = defaultPreferences(session);
  try {
    const raw = window.localStorage.getItem(preferenceStorageKey(session));
    const saved = raw ? JSON.parse(raw) : {};
    return {
      ...defaults,
      ...(saved || {}),
      themePreference: ['system', 'light', 'dark'].includes(saved?.themePreference)
        ? saved.themePreference
        : (saved?.theme === 'dark' ? 'dark' : saved?.theme === 'light' ? 'light' : defaults.themePreference),
      workspaceLayout: ['table', 'enterprise', 'classic'].includes(saved?.workspaceLayout)
        ? saved.workspaceLayout
        : defaults.workspaceLayout,
      username: defaults.username
    };
  } catch {
    return defaults;
  }
}

function writeLocalPreferences(session, preferences) {
  window.localStorage.setItem(preferenceStorageKey(session), JSON.stringify({
    ...preferences,
    username: session?.username || session?.email || ''
  }));
}

async function loadProfilePreferences(session, signal) {
  const local = readLocalPreferences(session);
  if (!sessionToken(session)) return local;

  try {
    const response = await originalFetch()('/api/profile/preferences', {
      headers: authHeaders(session),
      credentials: 'include',
      cache: 'no-store',
      signal
    });
    if (!response.ok) return local;
    const payload = await readJsonResponse(response);
    const server = payload.preferences || payload;
    return {
      ...local,
      ...(server || {}),
      themePreference: local.themePreference,
      workspaceLayout: local.workspaceLayout,
      username: local.username
    };
  } catch (error) {
    if (error?.name === 'AbortError') throw error;
    return local;
  }
}

async function persistProfilePhoto(session, preferences) {
  if (!sessionToken(session)) return preferences;

  const response = await originalFetch()('/api/profile/preferences', {
    method: 'POST',
    headers: authHeaders(session, true),
    credentials: 'include',
    cache: 'no-store',
    body: JSON.stringify({
      profilePhotoDataUrl: preferences.profileImageRemoved
        ? ''
        : (preferences.profilePhotoDataUrl || '')
    })
  });
  const payload = await readJsonResponse(response);
  if (!response.ok) {
    throw new Error(payload.message || `Profile preference save returned HTTP ${response.status}`);
  }

  return {
    ...preferences,
    profilePhotoDataUrl: payload.profilePhotoDataUrl ?? (preferences.profileImageRemoved ? '' : preferences.profilePhotoDataUrl),
    profileImageRemoved: false
  };
}

function fallbackProfile(session) {
  const email = session?.username || session?.email || '';
  return {
    email,
    displayName: session?.displayName || session?.name || email || 'Pulse user',
    jobTitle: session?.roleName || session?.role || 'Title not available',
    role: session?.roleName || session?.role || '',
    department: '',
    team: '',
    profilePhotoDataUrl: '',
    isMicrosoftIdentity: false,
    identitySource: 'session_fallback',
    authenticationProvider: session?.provider || session?.loginMethod || 'backend_resolved',
    directoryProvider: 'backend_resolved',
    presence: {
      availability: 'presenceUnknown',
      activity: 'presenceUnknown',
      supported: false,
      status: 'unavailable'
    }
  };
}

function resolvedProfile(profile, preferences, session) {
  const base = { ...fallbackProfile(session), ...(profile || {}) };
  const isMicrosoft = base.isMicrosoftIdentity === true;
  return {
    ...base,
    displayName: isMicrosoft
      ? (base.displayName || fallbackProfile(session).displayName)
      : (preferences.displayNameOverride || base.displayName || fallbackProfile(session).displayName),
    jobTitle: isMicrosoft
      ? (base.jobTitle || base.role || 'Title not available')
      : (preferences.titleOverride || base.jobTitle || base.role || 'Title not available'),
    profilePhotoDataUrl: isMicrosoft
      ? (base.profilePhotoDataUrl || '')
      : (preferences.profileImageRemoved ? '' : (preferences.profilePhotoDataUrl || base.profilePhotoDataUrl || ''))
  };
}

function routeNameFromHash() {
  return String(window.location.hash || '')
    .replace(/^#\/?/, '')
    .split('?')[0]
    .trim()
    .toLowerCase();
}

function sectionFromHash() {
  const route = routeNameFromHash();
  return SECTION_BY_ROUTE.get(route) || null;
}

function humanizeProvider(value) {
  const normalized = String(value || '').toLowerCase();
  if (!normalized) return 'Not available';
  if (normalized.includes('microsoft') || normalized.includes('entra')) return 'Microsoft Entra ID';
  if (normalized.includes('local')) return 'Pulse Local';
  if (normalized.includes('backend')) return 'Pulse authenticated session';
  return String(value).replaceAll('_', ' ').replace(/\b\w/g, (letter) => letter.toUpperCase());
}

function dateTimeLabel(value) {
  if (!value) return 'Not available';
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? 'Not available' : date.toLocaleString();
}

function browserLabel() {
  const data = navigator.userAgentData;
  if (data?.brands?.length) {
    const brand = data.brands.find((item) => !/not.?a.?brand/i.test(item.brand)) || data.brands[0];
    return `${brand.brand} ${brand.version}${data.platform ? ` on ${data.platform}` : ''}`;
  }
  return navigator.userAgent || 'Not available';
}

function currentWorkspaceLabel() {
  const route = routeNameFromHash();
  const accountSection = SECTION_BY_ROUTE.get(route);
  if (accountSection) return `Account Center — ${ACCOUNT_SECTIONS[accountSection].title}`;
  const heading = document.querySelector('.workspace-heading h1, .workspace-title h1, [data-workspace-title]');
  if (heading?.textContent?.trim()) return heading.textContent.trim();
  if (!route) return 'Dashboard';
  return route.replaceAll('-', ' ').replace(/\b\w/g, (letter) => letter.toUpperCase());
}

function splitCredentials(raw) {
  return String(raw || '')
    .split(/\r?\n/)
    .map((value, index) => ({ value, index }))
    .filter((entry) => entry.value.trim().length > 0);
}

function credentialNewline(raw) {
  return String(raw || '').includes('\r\n') ? '\r\n' : '\n';
}

function preferenceSnapshot(preferences, fields) {
  return JSON.stringify(fields.reduce((result, field) => ({
    ...result,
    [field]: preferences?.[field] ?? ''
  }), {}));
}

function focusableElements(container) {
  if (!container) return [];
  return [...container.querySelectorAll(
    'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), details > summary, [tabindex]:not([tabindex="-1"])'
  )].filter((element) => !element.hidden && element.getClientRects().length > 0);
}

function closeOtherHeaderOverlays() {
  window.dispatchEvent(new CustomEvent('projectpulse:close-major-overlays', {
    detail: { source: 'account-center' }
  }));

  const searchClose = document.querySelector('.projectpulse-global-search-close');
  if (searchClose instanceof HTMLButtonElement) searchClose.click();

  document.querySelectorAll('button[aria-expanded="true"]').forEach((button) => {
    const label = `${button.textContent || ''} ${button.getAttribute('aria-label') || ''}`.trim();
    if (/\bmore\b/i.test(label) && !button.classList.contains('profile-avatar-button')) {
      button.click();
    }
  });
}

function applyThemeToDocument(themePreference, { persist = false } = {}) {
  const systemDark = window.matchMedia?.('(prefers-color-scheme: dark)').matches === true;
  const resolved = themePreference === 'system'
    ? (systemDark ? 'dark' : 'light')
    : (themePreference === 'dark' ? 'dark' : 'light');

  if (persist) window.localStorage.setItem(THEME_STORAGE_KEY, resolved);
  document.documentElement.dataset.theme = resolved;
  if (document.body) document.body.dataset.theme = resolved;
  window.dispatchEvent(new CustomEvent(THEME_EVENT, {
    detail: { theme: resolved, preference: themePreference }
  }));
  return resolved;
}

function applyWorkspaceLayout(layout) {
  const experience = layout === 'classic' ? 'classic' : 'enterprise';
  window.localStorage.setItem(EXPERIENCE_STORAGE_KEY, experience);
  document.documentElement.dataset.pulseExperience = experience;
  if (document.body) document.body.dataset.pulseExperience = experience;
  window.dispatchEvent(new CustomEvent(EXPERIENCE_EVENT, {
    detail: { experience, workspaceLayout: layout }
  }));
}

function ThemePreview({ mode }) {
  return (
    <span className={`account-theme-preview account-theme-preview--${mode}`} aria-hidden="true">
      <span className="account-theme-preview__rail" />
      <span className="account-theme-preview__header" />
      <span className="account-theme-preview__body">
        <i />
        <i />
        <i />
      </span>
    </span>
  );
}

function SectionNavigation({ activeSection, onNavigate }) {
  return (
    <nav className="account-center-navigation" aria-label="Account Center sections">
      <span className="account-center-navigation__label">Account</span>
      {Object.entries(ACCOUNT_SECTIONS).map(([section, metadata]) => (
        <button
          key={section}
          type="button"
          className={activeSection === section ? 'is-selected' : ''}
          aria-current={activeSection === section ? 'page' : undefined}
          onClick={() => onNavigate(section)}
        >
          <Icon name={section} />
          <span>{metadata.title}</span>
        </button>
      ))}
      <aside className="account-center-help">
        <strong>Need help?</strong>
        <p>Visit the Pulse help center for profile and account guidance.</p>
        <button type="button" onClick={() => { window.location.hash = '#user-guide'; }}>
          Open help center <span aria-hidden="true">↗</span>
        </button>
      </aside>
    </nav>
  );
}

function ProfileSection({
  profile,
  preferences,
  draft,
  setDraft,
  profileImageRemoved,
  setProfileImageRemoved,
  credentialInput,
  setCredentialInput,
  isDirty,
  isSaving,
  status,
  onSave,
  onCancel
}) {
  const inputRef = useRef(null);
  const editableIdentity = profile?.isMicrosoftIdentity !== true;
  const photoEditable = editableIdentity;
  const previewProfile = resolvedProfile(profile, {
    ...draft,
    profileImageRemoved
  }, readAuthSession());
  const credentials = splitCredentials(draft.awardsAndCertificates);

  const processFile = useCallback((file) => {
    if (!file) return;
    if (!ACCEPTED_PROFILE_IMAGE_TYPES.has(file.type)) {
      status.set('error', 'Select a JPG, JPEG, or PNG profile picture.');
      return;
    }
    if (file.size > MAX_PROFILE_IMAGE_BYTES) {
      status.set('error', 'Profile pictures must be 2 MB or smaller.');
      return;
    }

    const reader = new FileReader();
    reader.onload = () => {
      setDraft((current) => ({
        ...current,
        profilePhotoDataUrl: String(reader.result || '')
      }));
      setProfileImageRemoved(false);
      status.set('info', 'Profile picture ready. Save changes to keep it.');
    };
    reader.onerror = () => status.set('error', 'Pulse could not read the selected profile picture.');
    reader.readAsDataURL(file);
  }, [setDraft, setProfileImageRemoved, status]);

  const removeCredential = (targetIndex) => {
    const raw = String(draft.awardsAndCertificates || '');
    const separator = credentialNewline(raw);
    const values = raw.split(/\r?\n/);
    values.splice(targetIndex, 1);
    setDraft((current) => ({
      ...current,
      awardsAndCertificates: values.join(separator)
    }));
  };

  const addCredential = () => {
    const value = credentialInput.trim();
    if (!value) return;
    const current = String(draft.awardsAndCertificates || '');
    const separator = credentialNewline(current);
    setDraft((existing) => ({
      ...existing,
      awardsAndCertificates: current ? `${current}${separator}${value}` : value
    }));
    setCredentialInput('');
  };

  return (
    <form className="account-profile-form" onSubmit={onSave} noValidate>
      {readViewAs() ? (
        <div className="account-view-as-boundary" role="note">
          <Icon name="shield" />
          <div>
            <strong>Personal settings remain tied to your signed-in account.</strong>
            <span>View-As is active, but these changes never edit the viewed user.</span>
          </div>
        </div>
      ) : null}

      <section className="account-content-card account-profile-card">
        <div className="account-profile-photo-column">
          <h2>Profile picture</h2>
          <div className="account-profile-photo-preview">
            <IdentityAvatar profile={previewProfile} size="large" showPresence={false} />
            {photoEditable ? (
              <span className="account-profile-camera-badge" aria-hidden="true"><Icon name="camera" /></span>
            ) : null}
          </div>

          <input
            ref={inputRef}
            className="account-visually-hidden"
            type="file"
            accept="image/jpeg,image/png"
            onChange={(event) => processFile(event.target.files?.[0])}
            disabled={!photoEditable || isSaving}
            aria-label="Choose a new profile picture"
          />

          <div
            className={`account-profile-dropzone ${photoEditable ? '' : 'is-read-only'}`}
            onDragOver={(event) => {
              if (!photoEditable) return;
              event.preventDefault();
              event.dataTransfer.dropEffect = 'copy';
            }}
            onDrop={(event) => {
              if (!photoEditable) return;
              event.preventDefault();
              processFile(event.dataTransfer.files?.[0]);
            }}
          >
            <Icon name="camera" />
            <span>{photoEditable ? 'Drop a JPG or PNG here, or choose a file.' : 'Directory-managed profile image.'}</span>
          </div>

          <button
            type="button"
            className="account-secondary-button"
            onClick={() => inputRef.current?.click()}
            disabled={!photoEditable || isSaving}
          >
            Change photo
          </button>
          <button
            type="button"
            className="account-danger-button account-danger-button--quiet"
            onClick={() => {
              setDraft((current) => ({ ...current, profilePhotoDataUrl: '' }));
              setProfileImageRemoved(true);
              status.set('info', 'Profile picture will be removed after you save changes.');
            }}
            disabled={!photoEditable || isSaving || (!previewProfile.profilePhotoDataUrl && !preferences.profilePhotoDataUrl)}
          >
            Remove
          </button>
          <small>JPG or PNG, maximum 2 MB.</small>
        </div>

        <div className="account-profile-information-column">
          <div className="account-card-heading-row">
            <div>
              <h2>Profile information</h2>
              <p>Pulse profile values are editable only when Pulse is the source of authority.</p>
            </div>
            <span className={`account-source-badge ${editableIdentity ? 'is-local' : 'is-directory'}`}>
              {editableIdentity ? 'Pulse profile' : 'Directory managed'}
            </span>
          </div>

          <label className="account-field">
            <span>Display name</span>
            <input
              value={draft.displayNameOverride || profile?.displayName || ''}
              onChange={(event) => setDraft((current) => ({
                ...current,
                displayNameOverride: event.target.value
              }))}
              readOnly={!editableIdentity}
              aria-describedby={!editableIdentity ? 'account-display-name-authority' : undefined}
            />
            {!editableIdentity ? <small id="account-display-name-authority">Managed by Microsoft Entra ID.</small> : null}
          </label>

          <label className="account-field">
            <span>Title / role description</span>
            <input
              value={draft.titleOverride || profile?.jobTitle || profile?.role || ''}
              onChange={(event) => setDraft((current) => ({
                ...current,
                titleOverride: event.target.value
              }))}
              readOnly={!editableIdentity}
            />
            {!editableIdentity ? <small>Managed by the connected directory.</small> : null}
          </label>

          <div className="account-read-only-grid">
            <div>
              <span>Department</span>
              <strong>{profile?.department || 'Not available'}</strong>
            </div>
            <div>
              <span>Team</span>
              <strong>{profile?.team || 'Not available'}</strong>
            </div>
          </div>

          <fieldset className="account-credentials-fieldset">
            <legend>Awards and certificates</legend>
            <div className="account-credential-chips" aria-live="polite">
              {credentials.length ? credentials.map((credential) => (
                <span className="account-credential-chip" key={`${credential.index}-${credential.value}`}>
                  <span>{credential.value}</span>
                  <button
                    type="button"
                    aria-label={`Remove ${credential.value}`}
                    onClick={() => removeCredential(credential.index)}
                    disabled={isSaving}
                  >
                    <Icon name="close" />
                  </button>
                </span>
              )) : <span className="account-empty-inline">No credentials have been added.</span>}
            </div>
            <div className="account-add-credential-row">
              <input
                value={credentialInput}
                onChange={(event) => setCredentialInput(event.target.value)}
                onKeyDown={(event) => {
                  if (event.key === 'Enter') {
                    event.preventDefault();
                    addCredential();
                  }
                }}
                placeholder="Credential or certificate name"
                aria-label="Credential or certificate name"
                disabled={isSaving}
              />
              <button type="button" className="account-secondary-button" onClick={addCredential} disabled={!credentialInput.trim() || isSaving}>
                <Icon name="add" /> Add credential
              </button>
            </div>
            <small>Existing newline-separated values remain compatible with the current Pulse profile storage.</small>
          </fieldset>

          <details className="account-directory-details">
            <summary>Directory and authentication details</summary>
            <dl>
              <div><dt>Authentication source</dt><dd>{humanizeProvider(profile?.authenticationProvider)}</dd></div>
              <div><dt>Directory source</dt><dd>{humanizeProvider(profile?.directoryProvider || profile?.identitySource)}</dd></div>
              <div><dt>Domain</dt><dd>{profile?.domain || profile?.email?.split('@')[1] || 'Not available'}</dd></div>
              <div><dt>Last profile refresh</dt><dd>{dateTimeLabel(profile?.profileRetrievedAt)}</dd></div>
            </dl>
          </details>
        </div>
      </section>

      {status.message ? (
        <div className={`account-inline-status is-${status.type}`} role={status.type === 'error' ? 'alert' : 'status'}>
          {status.message}
        </div>
      ) : null}

      <footer className="account-form-actions">
        <button type="button" className="account-link-button" onClick={onCancel} disabled={!isDirty || isSaving}>Cancel</button>
        <button type="submit" className="account-primary-button" disabled={!isDirty || isSaving}>
          {isSaving ? 'Saving…' : 'Save changes'}
        </button>
      </footer>
    </form>
  );
}

function AppearanceSection({
  preferences,
  draft,
  setDraft,
  isDirty,
  isSaving,
  status,
  onSave,
  onCancel
}) {
  const tableAvailable = window.__projectPulseTableModeAvailable === true
    || document.documentElement.dataset.pulseTableAvailable === 'true';
  const layoutOptions = [
    { value: 'table', label: 'Enterprise table (recommended)', description: 'Dense table navigation for large workspace catalogs.', available: tableAvailable },
    { value: 'enterprise', label: 'Enterprise cards', description: 'Unified responsive cards and enterprise page context.', available: true },
    { value: 'classic', label: 'Classic', description: 'Use the established Pulse presentation.', available: true }
  ].filter((option) => option.available);

  const themePreference = ['system', 'light', 'dark'].includes(draft.themePreference)
    ? draft.themePreference
    : (draft.theme === 'dark' ? 'dark' : 'light');
  const selectedLayout = layoutOptions.some((option) => option.value === draft.workspaceLayout)
    ? draft.workspaceLayout
    : 'enterprise';

  const chooseTheme = (value) => {
    const resolved = applyThemeToDocument(value, { persist: false });
    setDraft((current) => ({
      ...current,
      themePreference: value,
      theme: resolved
    }));
  };

  return (
    <form className="account-appearance-form" onSubmit={onSave}>
      <section className="account-content-card">
        <div className="account-card-heading-row">
          <div>
            <h2>Theme preference</h2>
            <p>Choose your preferred Pulse appearance.</p>
          </div>
        </div>

        <div className="account-theme-options" role="radiogroup" aria-label="Theme preference">
          {[
            { value: 'system', label: 'System', description: 'Follow your system setting' },
            { value: 'light', label: 'Light', description: 'Bright workspace view' },
            { value: 'dark', label: 'Dark', description: 'Reduced brightness view' }
          ].map((option) => (
            <button
              key={option.value}
              type="button"
              role="radio"
              aria-checked={themePreference === option.value}
              className={themePreference === option.value ? 'is-selected' : ''}
              onClick={() => chooseTheme(option.value)}
              disabled={isSaving}
            >
              <span className="account-radio-indicator" aria-hidden="true" />
              <span className="account-theme-option-copy">
                <strong>{option.label}</strong>
                <small>{option.description}</small>
              </span>
              <ThemePreview mode={option.value} />
            </button>
          ))}
        </div>
        <p className="account-persistence-note">This preference applies to Pulse on this browser and continues using the existing signed-in profile-preference path where supported.</p>
      </section>

      <section className="account-content-card account-layout-preference-card">
        <h2>Default workspace layout</h2>
        <p>Choose the layout Pulse should open without changing your role or workspace authorization.</p>
        <label className="account-field">
          <span>Workspace layout</span>
          <select
            value={selectedLayout}
            onChange={(event) => setDraft((current) => ({ ...current, workspaceLayout: event.target.value }))}
            disabled={isSaving}
          >
            {layoutOptions.map((option) => <option key={option.value} value={option.value}>{option.label}</option>)}
          </select>
          <small>{layoutOptions.find((option) => option.value === selectedLayout)?.description}</small>
        </label>
      </section>

      {status.message ? (
        <div className={`account-inline-status is-${status.type}`} role={status.type === 'error' ? 'alert' : 'status'}>
          {status.message}
        </div>
      ) : null}

      <footer className="account-form-actions">
        <button type="button" className="account-link-button" onClick={() => {
          applyThemeToDocument(preferences.themePreference || preferences.theme || 'light', { persist: false });
          onCancel();
        }} disabled={!isDirty || isSaving}>Cancel</button>
        <button type="submit" className="account-primary-button" disabled={!isDirty || isSaving}>
          {isSaving ? 'Saving…' : 'Save changes'}
        </button>
      </footer>
    </form>
  );
}

function SessionSection({ session, profile, viewAs, refreshedAt, isRefreshing, onRefresh, onExitViewAs }) {
  const signedInName = profile?.displayName || session?.displayName || session?.username || 'Current user';
  const signedInEmail = profile?.email || session?.username || session?.email || 'Not available';
  const ipAddress = session?.ipAddress || session?.ip || '';

  return (
    <section className="account-session-section">
      {viewAs ? (
        <div className="account-view-as-boundary" role="status">
          <Icon name="shield" />
          <div>
            <strong>View-As is active</strong>
            <span>Signed-in account and effective viewing identity are shown separately.</span>
          </div>
          <button type="button" className="account-secondary-button" onClick={onExitViewAs}>Exit View-As</button>
        </div>
      ) : null}

      <section className="account-content-card account-session-card">
        <div className="account-card-heading-row">
          <div>
            <h2>Current session</h2>
            <p>Only information available from your authenticated Pulse session is displayed.</p>
          </div>
          <button type="button" className="account-secondary-button" onClick={onRefresh} disabled={isRefreshing}>
            <Icon name="refresh" /> {isRefreshing ? 'Refreshing…' : 'Refresh session info'}
          </button>
        </div>

        <div className="account-session-identity">
          <IdentityAvatar profile={{ ...profile, displayName: signedInName, email: signedInEmail }} size="medium" showPresence={false} />
          <div>
            <span>Signed-in identity</span>
            <strong>{signedInName}</strong>
            <small>{signedInEmail}</small>
          </div>
        </div>

        <dl className="account-session-details">
          <div><dt>Active workspace</dt><dd>{currentWorkspaceLabel()}</dd></div>
          <div><dt>Authentication source</dt><dd>{humanizeProvider(profile?.authenticationProvider || session?.provider || session?.loginMethod)}</dd></div>
          <div><dt>View-As</dt><dd>{viewAs ? 'Active' : 'Inactive'}</dd></div>
          {viewAs ? <div><dt>Viewing as</dt><dd>{viewAs.displayName || viewAs.email || 'Selected user'}{viewAs.roleCodes ? ` · ${viewAs.roleCodes}` : ''}</dd></div> : null}
          <div><dt>Last sign-in</dt><dd>{dateTimeLabel(session?.signedInAt || session?.lastSignInAt)}</dd></div>
          <div><dt>Session expiration</dt><dd>{dateTimeLabel(session?.expiresAt)}</dd></div>
          <div className="account-session-details__wide"><dt>Browser</dt><dd>{browserLabel()}</dd></div>
          {ipAddress ? <div><dt>IP address</dt><dd>{ipAddress}</dd></div> : null}
          <div><dt>Last refreshed</dt><dd>{dateTimeLabel(refreshedAt)}</dd></div>
        </dl>
      </section>
    </section>
  );
}

export default function AccountCenterPortal() {
  const [authSession, setAuthSession] = useState(() => readAuthSession());
  const [viewAs, setViewAs] = useState(() => readViewAs());
  const [profile, setProfile] = useState(() => fallbackProfile(readAuthSession()));
  const [preferences, setPreferences] = useState(() => readLocalPreferences(readAuthSession()));
  const [draft, setDraft] = useState(() => readLocalPreferences(readAuthSession()));
  const [profileImageRemoved, setProfileImageRemoved] = useState(false);
  const [credentialInput, setCredentialInput] = useState('');
  const [activeSection, setActiveSection] = useState(() => sectionFromHash());
  const [isPopoverOpen, setPopoverOpen] = useState(false);
  const [popoverPosition, setPopoverPosition] = useState({ top: 84, left: 16 });
  const [isLoading, setLoading] = useState(false);
  const [isRefreshing, setRefreshing] = useState(false);
  const [isSavingProfile, setSavingProfile] = useState(false);
  const [isSavingAppearance, setSavingAppearance] = useState(false);
  const [refreshedAt, setRefreshedAt] = useState(null);
  const [profileStatus, setProfileStatus] = useState({ type: 'info', message: '' });
  const [appearanceStatus, setAppearanceStatus] = useState({ type: 'info', message: '' });
  const [toast, setToast] = useState('');

  const avatarTriggerRef = useRef(null);
  const popoverRef = useRef(null);
  const firstPopoverActionRef = useRef(null);
  const requestRef = useRef(null);
  const profileDirtyRef = useRef(false);
  const appearanceDirtyRef = useRef(false);
  const activeSectionRef = useRef(activeSection);
  const lastAcceptedHashRef = useRef(window.location.hash || '#dashboard');
  const restoringHashRef = useRef(false);

  const resolved = useMemo(
    () => resolvedProfile(profile, { ...preferences, profileImageRemoved: false }, authSession),
    [authSession, preferences, profile]
  );

  const profileDirty = useMemo(() => (
    profileImageRemoved
    || preferenceSnapshot(draft, [
      'profilePhotoDataUrl',
      'displayNameOverride',
      'titleOverride',
      'awardsAndCertificates'
    ]) !== preferenceSnapshot(preferences, [
      'profilePhotoDataUrl',
      'displayNameOverride',
      'titleOverride',
      'awardsAndCertificates'
    ])
  ), [draft, preferences, profileImageRemoved]);

  const appearanceDirty = useMemo(() => (
    preferenceSnapshot(draft, ['themePreference', 'theme', 'workspaceLayout'])
      !== preferenceSnapshot(preferences, ['themePreference', 'theme', 'workspaceLayout'])
  ), [draft, preferences]);

  useEffect(() => { profileDirtyRef.current = profileDirty; }, [profileDirty]);
  useEffect(() => { appearanceDirtyRef.current = appearanceDirty; }, [appearanceDirty]);
  useEffect(() => { activeSectionRef.current = activeSection; }, [activeSection]);

  const profileStatusController = useMemo(() => ({
    ...profileStatus,
    set(type, message) {
      setProfileStatus({ type, message });
    }
  }), [profileStatus]);

  const updateHeaderOffset = useCallback(() => {
    const header = document.querySelector('.enterprise-top-bar, .app-header, .top-header, header');
    const bottom = Math.max(72, Math.ceil(header?.getBoundingClientRect?.().bottom || 96));
    document.documentElement.style.setProperty('--account-center-header-offset', `${bottom}px`);
  }, []);

  useLayoutEffect(() => {
    updateHeaderOffset();
    const observer = typeof ResizeObserver === 'function'
      ? new ResizeObserver(updateHeaderOffset)
      : null;
    const header = document.querySelector('.enterprise-top-bar, .app-header, .top-header, header');
    if (header && observer) observer.observe(header);
    window.addEventListener('resize', updateHeaderOffset);
    return () => {
      observer?.disconnect();
      window.removeEventListener('resize', updateHeaderOffset);
      document.documentElement.style.removeProperty('--account-center-header-offset');
    };
  }, [updateHeaderOffset]);

  const refreshAccountData = useCallback(async ({ preserveDraft = false } = {}) => {
    const session = readAuthSession();
    setAuthSession(session);
    setViewAs(readViewAs());
    if (!session) return;

    requestRef.current?.abort();
    const controller = new AbortController();
    requestRef.current = controller;
    setLoading(true);

    try {
      const [profileResult, preferenceResult] = await Promise.allSettled([
        loadSignedInProfile(session, controller.signal),
        loadProfilePreferences(session, controller.signal)
      ]);
      if (controller.signal.aborted) return;

      const nextProfile = profileResult.status === 'fulfilled'
        ? profileResult.value
        : fallbackProfile(session);
      const loadedPreferences = preferenceResult.status === 'fulfilled'
        ? preferenceResult.value
        : readLocalPreferences(session);
      const nextPreferences = nextProfile?.isMicrosoftIdentity === true
        ? loadedPreferences
        : {
            ...loadedPreferences,
            displayNameOverride: loadedPreferences.displayNameOverride || nextProfile?.displayName || '',
            titleOverride: loadedPreferences.titleOverride || nextProfile?.jobTitle || nextProfile?.role || ''
          };

      setProfile(nextProfile);
      setPreferences(nextPreferences);
      if (!preserveDraft) {
        setDraft(nextPreferences);
        setProfileImageRemoved(false);
        setCredentialInput('');
      }
      setRefreshedAt(new Date().toISOString());

      if (profileResult.status === 'rejected') {
        setProfileStatus({
          type: 'warning',
          message: 'Profile enrichment is temporarily unavailable. Pulse is displaying the authenticated session fallback.'
        });
      }
    } finally {
      setLoading(false);
      setRefreshing(false);
    }
  }, []);

  useEffect(() => {
    void refreshAccountData();
    const synchronizeSession = () => void refreshAccountData();
    window.addEventListener('pageshow', synchronizeSession);
    window.addEventListener('storage', synchronizeSession);
    window.addEventListener('projectpulse:auth-session-cleared', synchronizeSession);
    window.addEventListener('projectpulse:view-as-changed', synchronizeSession);
    return () => {
      requestRef.current?.abort();
      window.removeEventListener('pageshow', synchronizeSession);
      window.removeEventListener('storage', synchronizeSession);
      window.removeEventListener('projectpulse:auth-session-cleared', synchronizeSession);
      window.removeEventListener('projectpulse:view-as-changed', synchronizeSession);
    };
  }, [refreshAccountData]);

  const closePopover = useCallback(({ restoreFocus = true } = {}) => {
    setPopoverOpen(false);
    if (restoreFocus) window.setTimeout(() => avatarTriggerRef.current?.focus(), 0);
  }, []);

  const positionPopover = useCallback(() => {
    const trigger = avatarTriggerRef.current;
    if (!trigger) return;
    const rect = trigger.getBoundingClientRect();
    const width = Math.min(390, Math.max(320, window.innerWidth - 24));
    const left = Math.min(
      Math.max(12, rect.right - width),
      Math.max(12, window.innerWidth - width - 12)
    );
    setPopoverPosition({ top: Math.ceil(rect.bottom + 10), left: Math.ceil(left) });
  }, []);

  useEffect(() => {
    const interceptAvatar = (event) => {
      const trigger = event.target?.closest?.('.profile-avatar-button');
      if (!trigger) return;
      event.preventDefault();
      event.stopPropagation();
      event.stopImmediatePropagation?.();
      avatarTriggerRef.current = trigger;
      closeOtherHeaderOverlays();
      positionPopover();
      setPopoverOpen((current) => !current);
      window.dispatchEvent(new CustomEvent(ACCOUNT_OVERLAY_EVENT));
    };

    document.documentElement.dataset.accountCenterInstalled = 'true';
    document.addEventListener('click', interceptAvatar, true);
    return () => {
      delete document.documentElement.dataset.accountCenterInstalled;
      document.removeEventListener('click', interceptAvatar, true);
    };
  }, [positionPopover]);

  useEffect(() => {
    if (!isPopoverOpen) return undefined;
    document.body.classList.add('account-profile-popover-open');
    positionPopover();
    window.setTimeout(() => firstPopoverActionRef.current?.focus(), 0);

    const handlePointerDown = (event) => {
      if (popoverRef.current?.contains(event.target)) return;
      if (avatarTriggerRef.current?.contains(event.target)) return;
      closePopover();
    };
    const handleKeyDown = (event) => {
      if (event.key === 'Escape') {
        event.preventDefault();
        closePopover();
        return;
      }
      if (event.key !== 'Tab') return;
      const focusable = focusableElements(popoverRef.current);
      if (!focusable.length) return;
      const first = focusable[0];
      const last = focusable.at(-1);
      if (event.shiftKey && document.activeElement === first) {
        event.preventDefault();
        last.focus();
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault();
        first.focus();
      }
    };

    document.addEventListener('pointerdown', handlePointerDown, true);
    document.addEventListener('keydown', handleKeyDown, true);
    window.addEventListener('resize', positionPopover);
    window.addEventListener('scroll', positionPopover, true);

    return () => {
      document.body.classList.remove('account-profile-popover-open');
      document.removeEventListener('pointerdown', handlePointerDown, true);
      document.removeEventListener('keydown', handleKeyDown, true);
      window.removeEventListener('resize', positionPopover);
      window.removeEventListener('scroll', positionPopover, true);
    };
  }, [closePopover, isPopoverOpen, positionPopover]);

  const hasUnsavedForSection = useCallback((section) => (
    section === 'profile' ? profileDirtyRef.current : section === 'appearance' ? appearanceDirtyRef.current : false
  ), []);

  const confirmDiscard = useCallback((section) => (
    !hasUnsavedForSection(section)
    || window.confirm('Discard unsaved Account Center changes?')
  ), [hasUnsavedForSection]);

  const navigateAccount = useCallback((section) => {
    if (activeSection && activeSection !== section && hasUnsavedForSection(activeSection)) {
      if (!confirmDiscard(activeSection)) return;
      setDraft(preferences);
      setProfileImageRemoved(false);
      setCredentialInput('');
      setProfileStatus({ type: 'info', message: '' });
      setAppearanceStatus({ type: 'info', message: '' });
      applyThemeToDocument(preferences.themePreference || preferences.theme || 'light', { persist: false });
    }
    const metadata = ACCOUNT_SECTIONS[section];
    if (!metadata) return;
    closePopover({ restoreFocus: false });
    window.location.hash = `#${metadata.route}`;
  }, [activeSection, closePopover, confirmDiscard, hasUnsavedForSection, preferences]);

  useEffect(() => {
    const synchronizeRoute = () => {
      if (restoringHashRef.current) {
        restoringHashRef.current = false;
        return;
      }

      const route = routeNameFromHash();
      const legacyTarget = LEGACY_ACCOUNT_ROUTES[route];
      if (legacyTarget) {
        window.history.replaceState(null, '', `#${legacyTarget}`);
      }
      const nextSection = SECTION_BY_ROUTE.get(legacyTarget || route) || null;
      const previousSection = activeSectionRef.current;

      if (previousSection
          && previousSection !== nextSection
          && hasUnsavedForSection(previousSection)
          && !window.confirm('Leave this Account Center section and discard unsaved changes?')) {
        restoringHashRef.current = true;
        window.history.replaceState(null, '', lastAcceptedHashRef.current || `#${ACCOUNT_SECTIONS[previousSection].route}`);
        setActiveSection(previousSection);
        return;
      }

      if (previousSection && previousSection !== nextSection && hasUnsavedForSection(previousSection)) {
        setDraft(preferences);
        setProfileImageRemoved(false);
        setCredentialInput('');
        setProfileStatus({ type: 'info', message: '' });
        setAppearanceStatus({ type: 'info', message: '' });
        applyThemeToDocument(preferences.themePreference || preferences.theme || 'light', { persist: false });
      }

      setActiveSection(nextSection);
      activeSectionRef.current = nextSection;
      lastAcceptedHashRef.current = window.location.hash || '#dashboard';
      closePopover({ restoreFocus: false });
    };

    synchronizeRoute();
    window.addEventListener('hashchange', synchronizeRoute);
    window.addEventListener('popstate', synchronizeRoute);
    return () => {
      window.removeEventListener('hashchange', synchronizeRoute);
      window.removeEventListener('popstate', synchronizeRoute);
    };
  }, [closePopover, hasUnsavedForSection, preferences]);

  useEffect(() => {
    document.body.classList.toggle('account-center-active', Boolean(activeSection));
    if (activeSection) updateHeaderOffset();
    return () => document.body.classList.remove('account-center-active');
  }, [activeSection, updateHeaderOffset]);

  useEffect(() => {
    const beforeUnload = (event) => {
      if (!profileDirtyRef.current && !appearanceDirtyRef.current) return;
      event.preventDefault();
      event.returnValue = '';
    };
    const protectHashNavigation = (event) => {
      if (!activeSection || !hasUnsavedForSection(activeSection)) return;
      const anchor = event.target?.closest?.('a[href^="#"], button[data-route]');
      if (!anchor || anchor.closest('.account-center-shell')) return;
      if (window.confirm('Leave Account Center and discard unsaved changes?')) return;
      event.preventDefault();
      event.stopPropagation();
    };

    window.addEventListener('beforeunload', beforeUnload);
    document.addEventListener('click', protectHashNavigation, true);
    return () => {
      window.removeEventListener('beforeunload', beforeUnload);
      document.removeEventListener('click', protectHashNavigation, true);
    };
  }, [activeSection, hasUnsavedForSection]);

  useEffect(() => {
    const media = window.matchMedia?.('(prefers-color-scheme: dark)');
    if (!media) return undefined;
    const updateSystemTheme = () => {
      if ((preferences.themePreference || preferences.theme) === 'system') {
        applyThemeToDocument('system', { persist: true });
      }
    };
    media.addEventListener?.('change', updateSystemTheme);
    return () => media.removeEventListener?.('change', updateSystemTheme);
  }, [preferences.theme, preferences.themePreference]);

  useEffect(() => {
    if (!toast) return undefined;
    const timer = window.setTimeout(() => setToast(''), 4200);
    return () => window.clearTimeout(timer);
  }, [toast]);

  const saveProfile = async (event) => {
    event.preventDefault();
    if (!profileDirty || isSavingProfile) return;
    setSavingProfile(true);
    setProfileStatus({ type: 'info', message: 'Saving profile changes…' });

    const next = {
      ...preferences,
      ...draft,
      profileImageRemoved
    };

    try {
      const saved = await persistProfilePhoto(authSession, next);
      writeLocalPreferences(authSession, saved);
      setPreferences(saved);
      setDraft(saved);
      setProfileImageRemoved(false);
      setProfileStatus({ type: 'success', message: 'Profile changes saved.' });
      setToast('Profile changes saved.');
      window.dispatchEvent(new CustomEvent(PROFILE_CHANGED_EVENT));
      window.dispatchEvent(new CustomEvent('projectpulse:profile-preferences-changed', { detail: saved }));
      void refreshAccountData({ preserveDraft: true });
    } catch (error) {
      setProfileStatus({
        type: 'error',
        message: error instanceof Error ? error.message : 'Pulse could not save your profile changes.'
      });
    } finally {
      setSavingProfile(false);
    }
  };

  const saveAppearance = async (event) => {
    event.preventDefault();
    if (!appearanceDirty || isSavingAppearance) return;
    setSavingAppearance(true);
    setAppearanceStatus({ type: 'info', message: 'Saving appearance preferences…' });

    const resolvedTheme = applyThemeToDocument(draft.themePreference || draft.theme || 'light', { persist: true });
    const next = {
      ...preferences,
      ...draft,
      theme: resolvedTheme,
      themePreference: draft.themePreference || draft.theme || resolvedTheme,
      workspaceLayout: draft.workspaceLayout || 'enterprise'
    };

    try {
      writeLocalPreferences(authSession, next);
      applyWorkspaceLayout(next.workspaceLayout);
      setPreferences(next);
      setDraft(next);
      setAppearanceStatus({ type: 'success', message: 'Appearance preferences saved.' });
      setToast('Appearance preferences saved.');

      // Reuse the existing governed profile-preference path as a best-effort
      // synchronization without making browser-only appearance depend on it.
      if (sessionToken(authSession)) {
        void originalFetch()('/api/profile/preferences', {
          method: 'POST',
          headers: authHeaders(authSession, true),
          credentials: 'include',
          cache: 'no-store',
          body: JSON.stringify(next)
        }).catch(() => null);
      }
    } finally {
      setSavingAppearance(false);
    }
  };

  const cancelProfile = () => {
    setDraft(preferences);
    setProfileImageRemoved(false);
    setCredentialInput('');
    setProfileStatus({ type: 'info', message: '' });
  };

  const cancelAppearance = () => {
    setDraft(preferences);
    setAppearanceStatus({ type: 'info', message: '' });
  };

  const exitViewAs = () => {
    window.localStorage.removeItem(VIEW_AS_STORAGE_KEY);
    window.dispatchEvent(new CustomEvent('projectpulse:view-as-changed', { detail: null }));
    window.location.reload();
  };

  const signOut = async () => {
    const session = readAuthSession();
    try {
      await originalFetch()('/api/auth/session/logout', {
        method: 'POST',
        headers: authHeaders(session, true),
        credentials: 'include',
        cache: 'no-store',
        body: '{}'
      });
    } catch {
      // Preserve the existing Pulse behavior: local sign-out continues if the
      // server session has already expired.
    }
    window.localStorage.removeItem(AUTH_SESSION_KEY);
    window.localStorage.removeItem(VIEW_AS_STORAGE_KEY);
    window.dispatchEvent(new CustomEvent('projectpulse:auth-session-cleared'));
    window.location.hash = '#dashboard';
    window.location.reload();
  };

  const popover = isPopoverOpen && authSession ? createPortal(
    <aside
      ref={popoverRef}
      className="account-profile-popover"
      style={{ top: `${popoverPosition.top}px`, left: `${popoverPosition.left}px` }}
      role="menu"
      aria-label="Account menu"
    >
      <header className="account-profile-popover__summary">
        <IdentityAvatar profile={resolved} size="medium" showPresence={false} />
        <div>
          <strong>{resolved.displayName}</strong>
          <span>{resolved.jobTitle || resolved.role || 'Title not available'}</span>
          <small>{resolved.email || authSession.username || 'Current user'}</small>
          <p className="account-presence-unavailable" title="Pulse does not currently have reliable presence data for this identity.">
            <i aria-hidden="true" /> Presence unavailable
          </p>
        </div>
      </header>

      {viewAs ? (
        <div className="account-profile-popover__view-as" role="note">
          <span>Signed in as: <strong>{resolved.displayName}</strong></span>
          <span>Viewing as: <strong>{viewAs.displayName || viewAs.email || 'Selected user'}{viewAs.roleCodes ? ` · ${viewAs.roleCodes}` : ''}</strong></span>
        </div>
      ) : null}

      <div className="account-profile-popover__actions">
        <button ref={firstPopoverActionRef} type="button" role="menuitem" onClick={() => navigateAccount('profile')}>
          <span className="account-menu-icon"><Icon name="profile" /></span>
          <span>My profile</span>
          <Icon name="chevron" />
        </button>
        <button type="button" role="menuitem" onClick={() => navigateAccount('appearance')}>
          <span className="account-menu-icon"><Icon name="appearance" /></span>
          <span>Preferences</span>
          <Icon name="chevron" />
        </button>
        <button type="button" role="menuitem" onClick={() => navigateAccount('session')}>
          <span className="account-menu-icon"><Icon name="session" /></span>
          <span>Current session</span>
          <Icon name="chevron" />
        </button>
      </div>

      <div className="account-profile-popover__signout">
        <button type="button" role="menuitem" onClick={signOut}>
          <Icon name="signout" /> Sign out
        </button>
      </div>
    </aside>,
    document.body
  ) : null;

  const accountPage = activeSection && authSession ? createPortal(
    <div className="account-center-page-layer" data-account-section={activeSection}>
      <main className="account-center-shell" aria-labelledby="account-center-page-title">
        <header className="account-center-page-header">
          <nav className="account-center-breadcrumb" aria-label="Breadcrumb">
            <span>Account Center</span><span aria-hidden="true">›</span><strong>{ACCOUNT_SECTIONS[activeSection].title}</strong>
          </nav>
          <h1 id="account-center-page-title">{ACCOUNT_SECTIONS[activeSection].title}</h1>
          <p>{ACCOUNT_SECTIONS[activeSection].description}</p>
        </header>

        <div className="account-center-mobile-nav">
          <label htmlFor="account-center-mobile-section">Account section</label>
          <select
            id="account-center-mobile-section"
            value={activeSection}
            onChange={(event) => navigateAccount(event.target.value)}
          >
            {Object.entries(ACCOUNT_SECTIONS).map(([section, metadata]) => (
              <option key={section} value={section}>{metadata.title}</option>
            ))}
          </select>
        </div>

        <div className="account-center-layout">
          <SectionNavigation activeSection={activeSection} onNavigate={navigateAccount} />
          <section className="account-center-content" aria-busy={isLoading}>
            {isLoading && !refreshedAt ? (
              <div className="account-center-loading" role="status">
                <span className="account-loading-spinner" aria-hidden="true" />
                Loading your Account Center…
              </div>
            ) : null}

            {activeSection === 'profile' ? (
              <ProfileSection
                profile={profile}
                preferences={preferences}
                draft={draft}
                setDraft={setDraft}
                profileImageRemoved={profileImageRemoved}
                setProfileImageRemoved={setProfileImageRemoved}
                credentialInput={credentialInput}
                setCredentialInput={setCredentialInput}
                isDirty={profileDirty}
                isSaving={isSavingProfile}
                status={profileStatusController}
                onSave={saveProfile}
                onCancel={cancelProfile}
              />
            ) : null}

            {activeSection === 'appearance' ? (
              <AppearanceSection
                preferences={preferences}
                draft={draft}
                setDraft={setDraft}
                isDirty={appearanceDirty}
                isSaving={isSavingAppearance}
                status={appearanceStatus}
                onSave={saveAppearance}
                onCancel={cancelAppearance}
              />
            ) : null}

            {activeSection === 'session' ? (
              <SessionSection
                session={authSession}
                profile={resolved}
                viewAs={viewAs}
                refreshedAt={refreshedAt}
                isRefreshing={isRefreshing}
                onRefresh={() => {
                  setRefreshing(true);
                  void refreshAccountData({ preserveDraft: true });
                }}
                onExitViewAs={exitViewAs}
              />
            ) : null}
          </section>
        </div>
      </main>
    </div>,
    document.body
  ) : null;

  return (
    <>
      {popover}
      {accountPage}
      <div className="account-center-announcer account-visually-hidden" aria-live="polite" aria-atomic="true">
        {toast}
      </div>
      {toast ? createPortal(<div className="account-center-toast" role="status">{toast}</div>, document.body) : null}
    </>
  );
}
