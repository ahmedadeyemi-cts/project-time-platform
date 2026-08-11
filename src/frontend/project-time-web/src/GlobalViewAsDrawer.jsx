import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import './global-view-as-drawer.css';

const VIEW_AS_STORAGE_KEY = 'projectPulseViewAsUser';
const AUTH_SESSION_STORAGE_KEY = 'projectPulseAuthSession';
const VIEW_AS_CHANGED_EVENT = 'projectpulse:view-as-changed';
const VIEW_AS_USERS_ENDPOINT = '/api/project-workspace/view-as/users';

function readStoredJson(key) {
  try {
    return JSON.parse(window.localStorage.getItem(key) || 'null');
  } catch {
    return null;
  }
}

function readAuthSession() {
  const session = readStoredJson(AUTH_SESSION_STORAGE_KEY);
  const token = session?.sessionToken || session?.token || session?.accessToken || '';

  if (!token) return null;
  if (session?.expiresAt && Date.now() >= Date.parse(session.expiresAt)) return null;

  return { ...session, sessionToken: token };
}

function readActiveViewAs() {
  const value = readStoredJson(VIEW_AS_STORAGE_KEY);
  return value?.userId ? value : null;
}

function roleLabel(user) {
  const roles = Array.isArray(user?.roleCodes)
    ? user.roleCodes
    : String(user?.roleCodes || '')
      .split(/[;,|]+/)
      .map((role) => role.trim())
      .filter(Boolean);

  return roles.length ? roles.join(', ') : 'No role assigned';
}

function userOptionLabel(user) {
  return [
    user?.displayName || user?.email || 'Unnamed user',
    roleLabel(user),
    user?.teamOrDepartment || ''
  ].filter(Boolean).join(' — ');
}

export default function GlobalViewAsDrawer() {
  const [open, setOpen] = useState(false);
  const [users, setUsers] = useState([]);
  const [loadState, setLoadState] = useState('idle');
  const [loadError, setLoadError] = useState('');
  const [activeViewAs, setActiveViewAs] = useState(() => readActiveViewAs());
  const requestSequence = useRef(0);
  const handleRef = useRef(null);
  const closeButtonRef = useRef(null);

  const loadUsers = useCallback(async () => {
    const session = readAuthSession();
    const active = readActiveViewAs();
    setActiveViewAs(active);

    if (!session?.sessionToken) {
      setUsers([]);
      setLoadState(active ? 'error' : 'hidden');
      setLoadError(active ? 'Your administrator session is unavailable. Exit View-As and sign in again.' : '');
      return;
    }

    const sequence = ++requestSequence.current;
    setLoadState((current) => current === 'ready' ? 'refreshing' : 'loading');
    setLoadError('');

    try {
      const requestFetch = window.__projectPulseOriginalFetch || window.fetch.bind(window);
      const response = await requestFetch(VIEW_AS_USERS_ENDPOINT, {
        method: 'GET',
        credentials: 'include',
        cache: 'no-store',
        headers: {
          'X-ProjectPulse-Session': session.sessionToken,
          'Cache-Control': 'no-cache, no-store',
          Pragma: 'no-cache'
        }
      });

      if (sequence !== requestSequence.current) return;

      if (!response.ok) {
        setUsers([]);
        setLoadState(active ? 'error' : 'hidden');
        setLoadError(active
          ? 'The eligible-user list could not be refreshed. You can still exit the active preview.'
          : '');
        return;
      }

      const body = await response.json().catch(() => ({}));
      const eligibleUsers = Array.isArray(body?.users)
        ? body.users.filter((user) => user?.userId)
        : [];

      setUsers(eligibleUsers);
      setLoadState(eligibleUsers.length || active ? 'ready' : 'hidden');
    } catch {
      if (sequence !== requestSequence.current) return;
      setUsers([]);
      setLoadState(active ? 'error' : 'hidden');
      setLoadError(active
        ? 'The eligible-user list could not be refreshed. You can still exit the active preview.'
        : '');
    }
  }, []);

  useEffect(() => {
    const timers = [250, 1200, 3000].map((delay) => window.setTimeout(loadUsers, delay));
    const synchronize = () => {
      setActiveViewAs(readActiveViewAs());
      void loadUsers();
    };
    const onStorage = (event) => {
      if (event.key === VIEW_AS_STORAGE_KEY || event.key === AUTH_SESSION_STORAGE_KEY) synchronize();
    };

    window.addEventListener('storage', onStorage);
    window.addEventListener('hashchange', loadUsers);
    window.addEventListener('projectpulse:auth-session-ready', loadUsers);
    window.addEventListener(VIEW_AS_CHANGED_EVENT, synchronize);

    return () => {
      requestSequence.current += 1;
      timers.forEach((timer) => window.clearTimeout(timer));
      window.removeEventListener('storage', onStorage);
      window.removeEventListener('hashchange', loadUsers);
      window.removeEventListener('projectpulse:auth-session-ready', loadUsers);
      window.removeEventListener(VIEW_AS_CHANGED_EVENT, synchronize);
    };
  }, [loadUsers]);

  const closeDrawer = useCallback(() => {
    setOpen(false);
    window.setTimeout(() => handleRef.current?.focus(), 0);
  }, []);

  useEffect(() => {
    document.body.classList.toggle('projectpulse-view-as-drawer-open', open);

    if (open) {
      window.setTimeout(() => closeButtonRef.current?.focus(), 0);
    }

    const onKeyDown = (event) => {
      if (event.key === 'Escape' && open) closeDrawer();
    };

    window.addEventListener('keydown', onKeyDown);
    return () => {
      document.body.classList.remove('projectpulse-view-as-drawer-open');
      window.removeEventListener('keydown', onKeyDown);
    };
  }, [closeDrawer, open]);

  const selectableUsers = useMemo(() => {
    if (!activeViewAs?.userId || users.some((user) => user.userId === activeViewAs.userId)) return users;
    return [activeViewAs, ...users];
  }, [activeViewAs, users]);

  const clearViewAs = () => {
    window.localStorage.removeItem(VIEW_AS_STORAGE_KEY);
    window.dispatchEvent(new CustomEvent(VIEW_AS_CHANGED_EVENT));
    window.location.reload();
  };

  const selectViewAs = (userId) => {
    if (!userId) {
      clearViewAs();
      return;
    }

    const selectedUser = selectableUsers.find((user) => user.userId === userId);
    if (!selectedUser) return;

    window.localStorage.setItem(VIEW_AS_STORAGE_KEY, JSON.stringify(selectedUser));
    window.dispatchEvent(new CustomEvent(VIEW_AS_CHANGED_EVENT, { detail: selectedUser }));
    window.location.reload();
  };

  const visible = Boolean(activeViewAs) || loadState === 'ready' || loadState === 'refreshing';
  if (!visible) return null;

  const activeName = activeViewAs?.displayName || activeViewAs?.email || 'Selected user';
  const handleText = activeViewAs ? 'View-As Active' : 'Administrator View-As';

  return (
    <>
      <button
        ref={handleRef}
        type="button"
        className={`projectpulse-view-as-handle ${activeViewAs ? 'is-active' : ''}`}
        onClick={() => setOpen((current) => !current)}
        aria-expanded={open}
        aria-controls="projectpulse-view-as-drawer"
        title={activeViewAs ? `Previewing ${activeName}` : 'Open Administrator View-As'}
      >
        {handleText}
      </button>

      <aside
        id="projectpulse-view-as-drawer"
        className={`projectpulse-view-as-drawer ${open ? 'is-open' : ''} ${activeViewAs ? 'is-active' : ''}`}
        role="dialog"
        aria-modal="true"
        aria-hidden={!open}
        aria-labelledby="projectpulse-view-as-title"
      >
        <header className="projectpulse-view-as-drawer__header">
          <div>
            <p>Super Administrator tool</p>
            <h2 id="projectpulse-view-as-title">Administrator View-As</h2>
            <small>Read-only effective-user preview</small>
          </div>
          <button
            ref={closeButtonRef}
            type="button"
            onClick={closeDrawer}
            aria-label="Close Administrator View-As"
          >
            ×
          </button>
        </header>

        <div className="projectpulse-view-as-drawer__content">
          <section className="projectpulse-view-as-intro">
            <strong>Preview the application exactly as another user sees it.</strong>
            <p>
              Module visibility, permissions, and eligible data use the selected identity. Your underlying
              Super Administrator role does not override that user&apos;s restrictions.
            </p>
          </section>

          {loadError ? <p className="projectpulse-view-as-error" role="alert">{loadError}</p> : null}

          <label className="projectpulse-view-as-selector">
            <span>Effective user</span>
            <select
              value={activeViewAs?.userId || ''}
              onChange={(event) => selectViewAs(event.target.value)}
              disabled={loadState === 'loading' && !activeViewAs}
            >
              <option value="">My Administrator view</option>
              {selectableUsers.map((user) => (
                <option key={user.userId} value={user.userId}>{userOptionLabel(user)}</option>
              ))}
            </select>
            <small>Selecting a user reloads Pulse in read-only preview mode.</small>
          </label>

          {activeViewAs ? (
            <section className="projectpulse-view-as-active-card" aria-label="Active View-As identity">
              <span>View-As preview active</span>
              <strong>{activeName}</strong>
              <p>{activeViewAs.email || 'Email not reported'}</p>
              <small>{roleLabel(activeViewAs)}</small>
              <button type="button" onClick={clearViewAs}>Exit preview</button>
            </section>
          ) : (
            <section className="projectpulse-view-as-admin-card">
              <span>Current effective identity</span>
              <strong>My Administrator view</strong>
              <p>Normal Super Administrator access is active.</p>
            </section>
          )}

          <section className="projectpulse-view-as-safety">
            <strong>Safety boundary</strong>
            <p>Write operations remain blocked while a View-As identity is active.</p>
          </section>
        </div>

        <footer className="projectpulse-view-as-drawer__footer">
          <span>Pulse access preview</span>
          <span>{activeViewAs ? 'Read only' : 'Administrator'}</span>
        </footer>
      </aside>

      {open ? (
        <button
          type="button"
          className="projectpulse-view-as-backdrop"
          onClick={closeDrawer}
          aria-label="Close Administrator View-As"
        />
      ) : null}
    </>
  );
}
