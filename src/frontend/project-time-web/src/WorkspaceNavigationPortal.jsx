import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { PROJECTPULSE_MODULES } from './module-availability-registry.js';
import {
  SHARED_WORKSPACE_MODULE_AUTHORITY_CONTRACT,
  authorizedModulesFromEffectiveNavigationState
} from './module-directory-authority.js';
import {
  PULSE_WORKSPACES,
  WORKSPACE_BY_NUMBER,
  groupWorkspacesByCategory,
  workspaceSearchText
} from './workspace-registry.js';
import module006CustomerBrands from './assets/module-006-customer-brands.svg';

const DIRECTORY_ROUTE = 'workspace-directory';
const FAVORITES_LIMIT = 8;
const RECENTS_LIMIT = 12;
const MORE_SELECTOR = [
  'button[aria-controls="enterprise-more-navigation-menu"]',
  '.enterprise-top-more-button',
  '.enterprise-more-button',
  '[data-projectpulse-more-trigger="true"]'
].join(',');

function clean(value) {
  return String(value ?? '').replace(/\s+/g, ' ').trim();
}

function currentRoute() {
  return clean(window.location.hash).replace(/^#/, '') || 'dashboard';
}

function readJsonStorage(key, fallback) {
  try {
    const value = JSON.parse(window.localStorage.getItem(key) || 'null');
    return value ?? fallback;
  } catch {
    return fallback;
  }
}

function sessionIdentity() {
  const session = readJsonStorage('projectPulseAuthSession', {});
  const viewAs = readJsonStorage('projectPulseViewAsUser', null);
  const navigation = window.__projectPulseEffectiveNavigation || {};
  const actualName = clean(session.displayName || session.name || session.username || session.email) || 'Signed-in user';
  const actualEmail = clean(session.email || session.username);
  const effectiveName = clean(viewAs?.displayName || viewAs?.email || navigation.displayName || actualName) || actualName;
  const roleCodes = Array.isArray(navigation.roleCodes) ? navigation.roleCodes.map(clean).filter(Boolean) : [];
  const roleLabel = clean(viewAs?.roleName || viewAs?.roleCodes || navigation.roleName || roleCodes[0]) || 'Authorized user';
  return {
    session,
    viewAs,
    actualName,
    actualEmail,
    effectiveName,
    roleCodes,
    roleLabel,
    isViewAs: Boolean(viewAs?.userId || navigation.isViewAs)
  };
}

function preferenceKey(kind) {
  const identity = sessionIdentity();
  const principal = clean(identity.actualEmail || identity.actualName).toLowerCase() || 'anonymous';
  return `projectPulseWorkspaceDirectory:${kind}:${principal}`;
}

function readStringList(kind) {
  const value = readJsonStorage(preferenceKey(kind), []);
  return Array.isArray(value) ? value.map(clean).filter(Boolean) : [];
}

function writeStringList(kind, values) {
  try {
    window.localStorage.setItem(preferenceKey(kind), JSON.stringify(values));
  } catch {
    // Hardened browser storage can be unavailable; navigation remains functional.
  }
}

function currentExperience() {
  return clean(
    document.documentElement.dataset.pulseLayout
      || document.body?.dataset.pulseLayout
      || document.documentElement.dataset.pulseExperience
      || document.body?.dataset.pulseExperience
      || window.localStorage.getItem('pulse-enterprise-experience')
      || 'enterprise'
  ).toLowerCase();
}

function isClassic() {
  return currentExperience() === 'classic';
}

async function readJson(response) {
  const raw = await response.text();
  if (!raw) return {};
  try { return JSON.parse(raw); } catch { return { message: raw }; }
}

function normalizeAvailability(body) {
  if (!Array.isArray(body?.states)) {
    throw new Error(body?.message || 'Workspace availability returned an invalid response.');
  }
  return new Map(body.states.map((state) => [clean(state?.moduleNumber).toUpperCase(), state?.isEnabled !== false]));
}

function iconFor(key) {
  const paths = {
    clock: 'M4 12a8 8 0 1 0 16 0 8 8 0 0 0-16 0Zm8-4v4l3 2',
    approval: 'M5 4h14v16H5zM8 9h8M8 13h5',
    chart: 'M5 19V9m7 10V5m7 14v-7',
    customers: 'M7 11a3 3 0 1 0 0-6 3 3 0 0 0 0 6Zm10 1a2.5 2.5 0 1 0 0-5M2 20c0-4 2-6 5-6s5 2 5 6m2-5c3 0 6 1.5 6 5',
    spark: 'm12 3 1.7 4.3L18 9l-4.3 1.7L12 15l-1.7-4.3L6 9l4.3-1.7L12 3Zm6 11 .8 2.2L21 17l-2.2.8L18 20l-.8-2.2L15 17l2.2-.8L18 14Z',
    admin: 'M12 3 4 7v5c0 5 3.4 8 8 9 4.6-1 8-4 8-9V7l-8-4Zm0 5v8m-4-4h8',
    shield: 'M12 3 5 6v6c0 4.5 2.8 7.5 7 9 4.2-1.5 7-4.5 7-9V6l-7-3Z',
    project: 'M4 6h6l2 2h8v11H4z',
    pulse: 'M3 12h4l2-5 4 10 2-5h6',
    help: 'M9.5 9a2.7 2.7 0 1 1 4.6 1.9c-1.4 1.1-2.1 1.5-2.1 3M12 18h.01',
    briefcase: 'M4 7h16v12H4zM9 7V5h6v2M4 12h16',
    workspace: 'M4 4h6v6H4zM14 4h6v6h-6zM4 14h6v6H4zM14 14h6v6h-6z'
  };
  return (
    <svg viewBox="0 0 24 24" aria-hidden="true">
      <path d={paths[key] || paths.workspace} />
    </svg>
  );
}

function WorkspaceTile({ workspace, compact = false, favorite, onOpen, onToggleFavorite }) {
  return (
    <article className={`workspace-directory-card ${compact ? 'is-compact' : ''} ${workspace.moduleNumber === '006' ? 'is-customer-programs' : ''}`}>
      <button className="workspace-directory-card__open-surface" type="button" onClick={() => onOpen(workspace)}>
        <span className="workspace-directory-card__icon">{iconFor(workspace.iconKey)}</span>
        <span className="workspace-directory-card__copy">
          <small>{workspace.category} · Module {workspace.moduleNumber}</small>
          <strong>{workspace.workspaceName}</strong>
          {!compact ? <span>{workspace.description}</span> : null}
        </span>
      </button>
      {workspace.moduleNumber === '006' && !compact ? (
        <img className="workspace-directory-card__brands" src={module006CustomerBrands} alt="Toyota, Hyundai, and Turion Space" />
      ) : null}
      <div className="workspace-directory-card__actions">
        <button type="button" onClick={() => onToggleFavorite(workspace)} aria-pressed={favorite} aria-label={`${favorite ? 'Remove' : 'Add'} ${workspace.workspaceName} ${favorite ? 'from' : 'to'} favorites`}>
          <span aria-hidden="true">{favorite ? '★' : '☆'}</span>
        </button>
        <button type="button" onClick={() => onOpen(workspace)}>Open <span aria-hidden="true">→</span></button>
      </div>
    </article>
  );
}

function publishWorkspaceAuthorization(workspaces, state) {
  if (typeof window === 'undefined') return;
  const viewAsUserId = clean(readJsonStorage('projectPulseViewAsUser', null)?.userId);
  const detail = Object.freeze({
    contract: SHARED_WORKSPACE_MODULE_AUTHORITY_CONTRACT,
    state: state === 'ready' && Array.isArray(workspaces) ? 'ready' : state,
    moduleNumbers: Array.isArray(workspaces)
      ? workspaces.map((workspace) => clean(workspace.moduleNumber).toUpperCase()).filter(Boolean)
      : [],
    viewAsUserId,
    authoritySource: 'workspace_directory_rbac_and_availability_v1'
  });
  window.__projectPulseAuthorizedWorkspaceNavigation = detail;
  window.dispatchEvent(new CustomEvent('projectpulse:workspace-authorization-updated', { detail }));
}

function useWorkspaceAuthority() {
  const [revision, setRevision] = useState(0);
  const [availability, setAvailability] = useState({ state: 'loading', map: new Map(), error: '' });

  const refresh = useCallback(async () => {
    const navigation = window.__projectPulseEffectiveNavigation;
    if (!navigation || navigation.state !== 'ready') {
      setAvailability({ state: 'loading', map: new Map(), error: '' });
      setRevision((value) => value + 1);
      return;
    }
    setAvailability((current) => ({ ...current, state: 'loading', error: '' }));
    try {
      const response = await fetch('/api/module-availability/overrides', { cache: 'no-store', credentials: 'include' });
      const body = await readJson(response);
      if (!response.ok) throw new Error(body?.message || `Workspace availability returned HTTP ${response.status}.`);
      setAvailability({ state: 'ready', map: normalizeAvailability(body), error: '' });
    } catch (error) {
      setAvailability({ state: 'error', map: new Map(), error: error instanceof Error ? error.message : 'Workspace access could not be verified.' });
    }
    setRevision((value) => value + 1);
  }, []);

  useEffect(() => {
    void refresh();
    const update = () => void refresh();
    window.addEventListener('projectpulse:view-as-changed', update);
    window.addEventListener('projectpulse:permission-navigation-updated', update);
    window.addEventListener('projectpulse:module-availability-changed', update);
    window.addEventListener('storage', update);
    return () => {
      window.removeEventListener('projectpulse:view-as-changed', update);
      window.removeEventListener('projectpulse:permission-navigation-updated', update);
      window.removeEventListener('projectpulse:module-availability-changed', update);
      window.removeEventListener('storage', update);
    };
  }, [refresh]);

  const authorized = useMemo(() => {
    const modules = authorizedModulesFromEffectiveNavigationState(PROJECTPULSE_MODULES, window.__projectPulseEffectiveNavigation);
    if (modules === null || availability.state !== 'ready') return null;
    const allowedNumbers = new Set(modules.map((module) => clean(module.moduleNumber).toUpperCase()));
    return PULSE_WORKSPACES.filter((workspace) => allowedNumbers.has(workspace.moduleNumber) && availability.map.get(workspace.moduleNumber) !== false);
  }, [availability, revision]);

  return { authorized, state: availability.state, error: availability.error, refresh };
}

function WorkspaceQuickLauncher({ workspaces, identity, favorites, recents, onClose, onOpen, onToggleFavorite, onViewAll, onCategory }) {
  const [search, setSearch] = useState('');
  const dialogRef = useRef(null);
  const closeRef = useRef(null);
  const normalized = search.trim().toLowerCase();
  const filtered = normalized ? workspaces.filter((workspace) => workspaceSearchText(workspace).includes(normalized)) : [];
  const favoriteItems = favorites.map((number) => WORKSPACE_BY_NUMBER.get(number)).filter((workspace) => workspace && workspaces.some((item) => item.moduleNumber === workspace.moduleNumber)).slice(0, 4);
  const recentItems = recents.map((number) => WORKSPACE_BY_NUMBER.get(number)).filter((workspace) => workspace && workspaces.some((item) => item.moduleNumber === workspace.moduleNumber)).slice(0, 4);
  const categories = groupWorkspacesByCategory(workspaces);

  useEffect(() => {
    const dialog = dialogRef.current;
    const focusable = () => Array.from(dialog?.querySelectorAll('button:not([disabled]), a[href], input:not([disabled]), select:not([disabled]), [tabindex]:not([tabindex="-1"])') || []);
    const keydown = (event) => {
      if (event.key === 'Escape') { event.preventDefault(); onClose(); return; }
      if (event.key !== 'Tab') return;
      const items = focusable();
      if (!items.length) return;
      const first = items[0];
      const last = items.at(-1);
      if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus(); }
      else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); }
    };
    document.addEventListener('keydown', keydown);
    window.setTimeout(() => closeRef.current?.focus(), 0);
    return () => document.removeEventListener('keydown', keydown);
  }, [onClose]);

  return createPortal(
    <div className="workspace-quick-launcher-backdrop" onMouseDown={(event) => { if (event.target === event.currentTarget) onClose(); }}>
      <section ref={dialogRef} className="workspace-quick-launcher" role="dialog" aria-modal="true" aria-labelledby="workspace-launcher-title" aria-describedby="workspace-launcher-subtitle">
        <header>
          <span className="workspace-quick-launcher__heading-icon">{iconFor('workspace')}</span>
          <div>
            <h2 id="workspace-launcher-title">Workspaces</h2>
            <p id="workspace-launcher-subtitle">Open a workspace available to your current access scope.</p>
            <small>Viewing as: {identity.effectiveName} · {identity.roleLabel}{identity.isViewAs ? ' · View-As active' : ''}</small>
          </div>
          <button ref={closeRef} type="button" onClick={onClose} aria-label="Close Workspaces">×</button>
        </header>
        <div className="workspace-quick-launcher__content">
          <label className="workspace-search-field">
            <span aria-hidden="true">⌕</span>
            <input type="search" value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Search workspaces" aria-label="Search workspaces" />
          </label>
          <p className="workspace-result-announcement" aria-live="polite">{normalized ? `${filtered.length} authorized workspace result${filtered.length === 1 ? '' : 's'}` : `${workspaces.length} authorized workspaces available`}</p>

          {normalized ? (
            <section className="workspace-launcher-section">
              <div className="workspace-section-heading"><h3>Search results</h3></div>
              <div className="workspace-compact-grid">
                {filtered.map((workspace) => <WorkspaceTile key={workspace.moduleNumber} workspace={workspace} compact favorite={favorites.includes(workspace.moduleNumber)} onOpen={onOpen} onToggleFavorite={onToggleFavorite} />)}
                {!filtered.length ? <p className="workspace-empty-state">No authorized workspaces match this search.</p> : null}
              </div>
            </section>
          ) : (
            <>
              <section className="workspace-launcher-section">
                <div className="workspace-section-heading"><h3>Recently used</h3></div>
                <div className="workspace-compact-grid">
                  {(recentItems.length ? recentItems : workspaces.slice(0, 4)).map((workspace) => <WorkspaceTile key={workspace.moduleNumber} workspace={workspace} compact favorite={favorites.includes(workspace.moduleNumber)} onOpen={onOpen} onToggleFavorite={onToggleFavorite} />)}
                </div>
              </section>
              <section className="workspace-launcher-section">
                <div className="workspace-section-heading"><h3>Favorites</h3><span>Manage with the star on any workspace</span></div>
                <div className="workspace-compact-grid">
                  {favoriteItems.map((workspace) => <WorkspaceTile key={workspace.moduleNumber} workspace={workspace} compact favorite onOpen={onOpen} onToggleFavorite={onToggleFavorite} />)}
                  {!favoriteItems.length ? <p className="workspace-empty-state">Pin a workspace to place it here.</p> : null}
                </div>
              </section>
              <section className="workspace-launcher-section">
                <div className="workspace-section-heading"><h3>Browse by category</h3></div>
                <div className="workspace-category-grid">
                  {categories.map((category) => (
                    <button type="button" key={category.name} onClick={() => onCategory(category.name)}>
                      <span>{iconFor(category.workspaces[0]?.iconKey)}</span>
                      <strong>{category.name}</strong>
                      <small>{category.workspaces.length} workspace{category.workspaces.length === 1 ? '' : 's'}</small>
                      <b aria-hidden="true">›</b>
                    </button>
                  ))}
                </div>
              </section>
            </>
          )}
        </div>
        <footer>
          <span>Tip: Pin your favorite workspaces for quick access.</span>
          <button type="button" onClick={() => onViewAll('all')}>View all workspaces <span aria-hidden="true">→</span></button>
        </footer>
      </section>
    </div>,
    document.body
  );
}

function WorkspaceDirectory({ workspaces, identity, favorites, recents, initialCategory, onOpen, onToggleFavorite }) {
  const [view, setView] = useState(() => clean(window.localStorage.getItem(preferenceKey('view'))) || 'grid');
  const [scope, setScope] = useState(initialCategory || 'all');
  const [search, setSearch] = useState('');
  const [sort, setSort] = useState('recent');
  const categories = useMemo(() => groupWorkspacesByCategory(workspaces), [workspaces]);
  const recentRank = new Map(recents.map((number, index) => [number, index]));
  const visible = useMemo(() => {
    let next = workspaces.filter((workspace) => {
      if (scope === 'favorites' && !favorites.includes(workspace.moduleNumber)) return false;
      if (scope === 'recent' && !recents.includes(workspace.moduleNumber)) return false;
      if (!['all', 'favorites', 'recent'].includes(scope) && workspace.category !== scope) return false;
      return !search.trim() || workspaceSearchText(workspace).includes(search.trim().toLowerCase());
    });
    next = [...next].sort((left, right) => {
      if (sort === 'az') return left.workspaceName.localeCompare(right.workspaceName);
      if (sort === 'za') return right.workspaceName.localeCompare(left.workspaceName);
      if (sort === 'category') return `${left.category} ${left.workspaceName}`.localeCompare(`${right.category} ${right.workspaceName}`);
      return (recentRank.get(left.moduleNumber) ?? 999) - (recentRank.get(right.moduleNumber) ?? 999) || left.workspaceName.localeCompare(right.workspaceName);
    });
    return next;
  }, [favorites, recentRank, recents, scope, search, sort, workspaces]);
  const isAdmin = identity.roleCodes.some((role) => ['SUPER_ADMINISTRATOR', 'ADMINISTRATOR', 'GLOBAL_ADMINISTRATOR'].includes(role.toUpperCase()));

  function setPresentation(next) {
    setView(next);
    try { window.localStorage.setItem(preferenceKey('view'), next); } catch { /* optional persistence */ }
  }

  function exportVisible() {
    const escape = (value) => `"${String(value ?? '').replaceAll('"', '""')}"`;
    const rows = [['Module', 'Workspace', 'Category', 'Route'], ...visible.map((workspace) => [workspace.moduleNumber, workspace.workspaceName, workspace.category, workspace.route])];
    const blob = new Blob([rows.map((row) => row.map(escape).join(',')).join('\n')], { type: 'text/csv;charset=utf-8' });
    const link = document.createElement('a');
    link.href = URL.createObjectURL(blob);
    link.download = 'pulse-workspace-directory.csv';
    link.click();
    URL.revokeObjectURL(link.href);
  }

  return (
    <section className="workspace-directory-page" aria-labelledby="workspace-directory-title">
      <header className="workspace-directory-page__header">
        <div>
          <p>Pulse workspaces</p>
          <h1 id="workspace-directory-title">Workspace Directory</h1>
          <span>Open a workspace available to your current access scope.</span>
          <small>Viewing as: {identity.effectiveName} · {identity.roleLabel}{identity.isViewAs ? ' · View-As active' : ''}</small>
        </div>
        <div>
          <strong>{workspaces.length} workspaces available</strong>
          {isAdmin ? <button type="button" onClick={exportVisible}>Export</button> : null}
        </div>
      </header>
      <div className="workspace-directory-layout">
        <aside aria-label="Workspace directory navigation">
          <nav>
            <p>WORKSPACES</p>
            <button className={scope === 'all' ? 'active' : ''} type="button" onClick={() => setScope('all')}>Workspace Directory</button>
            <button className={scope === 'favorites' ? 'active' : ''} type="button" onClick={() => setScope('favorites')}>Favorites <span>{favorites.length}</span></button>
            <button className={scope === 'recent' ? 'active' : ''} type="button" onClick={() => setScope('recent')}>Recently Used <span>{recents.length}</span></button>
            <p>CATEGORIES</p>
            {categories.map((category) => <button className={scope === category.name ? 'active' : ''} type="button" key={category.name} onClick={() => setScope(category.name)}>{category.name}<span>{category.workspaces.length}</span></button>)}
          </nav>
          <div className="workspace-directory-utilities">
            <a href="#session-intelligence">US Signal Session Intelligence</a>
            <button type="button" onClick={() => window.dispatchEvent(new CustomEvent('projectpulse:open-account-center', { detail: { section: 'appearance' } }))}>Appearance</button>
            <a href="#role-admin">Administration</a>
          </div>
        </aside>
        <main>
          <div className="workspace-directory-toolbar">
            <label className="workspace-search-field"><span aria-hidden="true">⌕</span><input type="search" value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Search workspaces" aria-label="Search workspaces" /></label>
            <select value={scope} onChange={(event) => setScope(event.target.value)} aria-label="Filter workspace category">
              <option value="all">All categories</option>
              <option value="favorites">Favorites</option>
              <option value="recent">Recently used</option>
              {categories.map((category) => <option value={category.name} key={category.name}>{category.name}</option>)}
            </select>
            <select value={sort} onChange={(event) => setSort(event.target.value)} aria-label="Sort workspaces">
              <option value="recent">Recently used</option><option value="az">A–Z</option><option value="za">Z–A</option><option value="category">Category</option>
            </select>
            <div role="group" aria-label="Workspace presentation"><button type="button" className={view === 'grid' ? 'active' : ''} aria-pressed={view === 'grid'} onClick={() => setPresentation('grid')}>Grid</button><button type="button" className={view === 'list' ? 'active' : ''} aria-pressed={view === 'list'} onClick={() => setPresentation('list')}>List</button></div>
          </div>
          <p className="workspace-result-announcement" aria-live="polite">{visible.length} workspace{visible.length === 1 ? '' : 's'} shown</p>
          {scope === 'all' && !search.trim() ? (
            <>
              {recents.length ? <section className="workspace-directory-section"><div className="workspace-section-heading"><h2>Recently used</h2><button type="button" onClick={() => setScope('recent')}>View all</button></div><div className="workspace-compact-grid">{recents.slice(0, 4).map((number) => WORKSPACE_BY_NUMBER.get(number)).filter(Boolean).filter((workspace) => workspaces.some((item) => item.moduleNumber === workspace.moduleNumber)).map((workspace) => <WorkspaceTile key={workspace.moduleNumber} workspace={workspace} compact favorite={favorites.includes(workspace.moduleNumber)} onOpen={onOpen} onToggleFavorite={onToggleFavorite} />)}</div></section> : null}
              {categories.map((category) => <section className="workspace-directory-section" key={category.name}><div className="workspace-section-heading"><div><h2>{category.name}</h2><p>{category.description}</p></div><button type="button" onClick={() => setScope(category.name)}>View all</button></div><div className={`workspace-directory-grid is-${view}`}>{category.workspaces.slice(0, category.name === 'Customer Programs' ? 6 : 8).map((workspace) => <WorkspaceTile key={workspace.moduleNumber} workspace={workspace} favorite={favorites.includes(workspace.moduleNumber)} onOpen={onOpen} onToggleFavorite={onToggleFavorite} />)}</div></section>)}
            </>
          ) : (
            <div className={`workspace-directory-grid is-${view}`}>{visible.map((workspace) => <WorkspaceTile key={workspace.moduleNumber} workspace={workspace} favorite={favorites.includes(workspace.moduleNumber)} onOpen={onOpen} onToggleFavorite={onToggleFavorite} />)}{!visible.length ? <p className="workspace-empty-state">No authorized workspaces match the current filters.</p> : null}</div>
          )}
        </main>
      </div>
    </section>
  );
}

export default function WorkspaceNavigationPortal() {
  const [route, setRoute] = useState(currentRoute);
  const [quickOpen, setQuickOpen] = useState(false);
  const [category, setCategory] = useState('all');
  const [favorites, setFavorites] = useState(readStringList('favorites'));
  const [recents, setRecents] = useState(readStringList('recents'));
  const [host, setHost] = useState(null);
  const triggerRef = useRef(null);
  const authority = useWorkspaceAuthority();
  const identity = sessionIdentity();

  useEffect(() => {
    publishWorkspaceAuthorization(authority.authorized, authority.state);
  }, [authority.authorized, authority.state, identity.viewAs?.userId]);

  const closeQuick = useCallback(() => {
    setQuickOpen(false);
    document.body.classList.remove('workspace-launcher-open');
    triggerRef.current?.classList.remove('active', 'workspace-launcher-trigger-active');
    window.setTimeout(() => triggerRef.current?.focus(), 0);
  }, []);

  const openQuick = useCallback((trigger) => {
    triggerRef.current = trigger;
    trigger?.classList.add('active', 'workspace-launcher-trigger-active');
    document.body.classList.add('workspace-launcher-open');
    window.dispatchEvent(new CustomEvent('projectpulse:close-header-overlays', { detail: { except: 'workspaces' } }));
    setQuickOpen(true);
  }, []);

  useEffect(() => {
    const onClick = (event) => {
      const button = event.target?.closest?.(MORE_SELECTOR) || [...document.querySelectorAll('.enterprise-top-navigation button')].find((candidate) => clean(candidate.textContent).toLowerCase() === 'more');
      if (!button || isClassic()) return;
      event.preventDefault();
      event.stopPropagation();
      event.stopImmediatePropagation();
      quickOpen ? closeQuick() : openQuick(button);
    };
    document.addEventListener('click', onClick, true);
    return () => document.removeEventListener('click', onClick, true);
  }, [closeQuick, openQuick, quickOpen]);

  useEffect(() => {
    const updateRoute = () => {
      setRoute(currentRoute());
      if (quickOpen) closeQuick();
    };
    window.addEventListener('hashchange', updateRoute);
    window.addEventListener('projectpulse:route-state-ready', updateRoute);
    return () => {
      window.removeEventListener('hashchange', updateRoute);
      window.removeEventListener('projectpulse:route-state-ready', updateRoute);
    };
  }, [closeQuick, quickOpen]);

  useEffect(() => {
    const root = document.getElementById('root');
    if (!root) return undefined;
    let current = null;
    const ensure = () => {
      const main = document.querySelector('main.app-shell.enterprise-nav-enabled');
      if (!main) { setHost(null); return; }
      let node = document.getElementById('workspace-navigation-portal-host');
      if (!node) { node = document.createElement('div'); node.id = 'workspace-navigation-portal-host'; main.appendChild(node); }
      if (node !== current) { current = node; setHost(node); }
    };
    ensure();
    const observer = new MutationObserver(ensure);
    observer.observe(root, { childList: true, subtree: true });
    return () => { observer.disconnect(); current?.remove(); };
  }, []);

  useEffect(() => {
    document.body.classList.toggle('workspace-directory-active', route === DIRECTORY_ROUTE);
    return () => document.body.classList.remove('workspace-directory-active');
  }, [route]);

  const openWorkspace = useCallback((workspace) => {
    const next = [workspace.moduleNumber, ...recents.filter((number) => number !== workspace.moduleNumber)].slice(0, RECENTS_LIMIT);
    setRecents(next); writeStringList('recents', next);
    closeQuick();
    window.location.hash = `#${workspace.route}`;
  }, [closeQuick, recents]);

  const toggleFavorite = useCallback((workspace) => {
    const exists = favorites.includes(workspace.moduleNumber);
    const next = exists ? favorites.filter((number) => number !== workspace.moduleNumber) : [workspace.moduleNumber, ...favorites].slice(0, FAVORITES_LIMIT);
    setFavorites(next); writeStringList('favorites', next);
  }, [favorites]);

  const viewAll = useCallback((nextCategory = 'all') => {
    setCategory(nextCategory);
    closeQuick();
    window.location.hash = `#${DIRECTORY_ROUTE}`;
  }, [closeQuick]);

  if (authority.state === 'loading' || authority.authorized === null) {
    return quickOpen ? createPortal(<div className="workspace-quick-launcher-backdrop"><section className="workspace-verification-state" role="status"><h2>Verifying workspace access for {identity.effectiveName}…</h2><div className="workspace-skeleton-grid">{Array.from({ length: 6 }).map((_, index) => <i key={index} />)}</div></section></div>, document.body) : null;
  }
  if (authority.state === 'error') {
    return quickOpen ? createPortal(<div className="workspace-quick-launcher-backdrop"><section className="workspace-verification-state is-error" role="alert"><h2>Workspace access could not be verified.</h2><p>Your available workspaces have not been displayed.</p><button type="button" onClick={authority.refresh}>Retry verification</button><button type="button" onClick={closeQuick}>Close</button></section></div>, document.body) : null;
  }

  return (
    <>
      {quickOpen ? <WorkspaceQuickLauncher workspaces={authority.authorized} identity={identity} favorites={favorites} recents={recents} onClose={closeQuick} onOpen={openWorkspace} onToggleFavorite={toggleFavorite} onViewAll={viewAll} onCategory={viewAll} /> : null}
      {route === DIRECTORY_ROUTE && host ? createPortal(<WorkspaceDirectory workspaces={authority.authorized} identity={identity} favorites={favorites} recents={recents} initialCategory={category} onOpen={openWorkspace} onToggleFavorite={toggleFavorite} />, host) : null}
    </>
  );
}
