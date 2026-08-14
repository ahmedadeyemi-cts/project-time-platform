import { useCallback, useEffect, useState } from 'react';
import { createPortal } from 'react-dom';
import './default-enterprise-view.css';

const VIEW_STORAGE_KEY = 'pulse-workspace-view';
const DEFAULT_VIEW = 'default';
const ALTERNATE_VIEW = 'alternate';

function currentRoute() {
  return String(window.location.hash || '#dashboard')
    .replace(/^#/, '')
    .split('?')[0]
    .trim() || 'dashboard';
}

function readStoredView() {
  try {
    return window.localStorage.getItem(VIEW_STORAGE_KEY) === ALTERNATE_VIEW
      ? ALTERNATE_VIEW
      : DEFAULT_VIEW;
  } catch {
    return DEFAULT_VIEW;
  }
}

function ensureHost(target, attributeName) {
  if (!target) return null;
  let host = target.querySelector(`:scope > [${attributeName}]`);
  if (!host) {
    host = document.createElement('div');
    host.setAttribute(attributeName, 'true');
    target.appendChild(host);
  }
  return host;
}

function managedText(element, replacement, enabled) {
  if (!element) return;
  if (!element.dataset.pulseDefaultOriginalText) {
    element.dataset.pulseDefaultOriginalText = String(element.textContent || '').trim();
  }
  const desired = enabled ? replacement : element.dataset.pulseDefaultOriginalText;
  if (element.textContent !== desired) element.textContent = desired;
}

function moduleIcon(route, group) {
  const normalizedRoute = String(route || '').toLowerCase();
  const normalizedGroup = String(group || '').toLowerCase();
  if (normalizedRoute.includes('timesheet')) return '◷';
  if (normalizedRoute.includes('approval')) return '✓';
  if (normalizedRoute.includes('utilization') || normalizedRoute.includes('capacity')) return '▥';
  if (normalizedRoute.includes('holiday') || normalizedRoute.includes('calendar')) return '□';
  if (normalizedRoute.includes('expense') || normalizedRoute.includes('billing') || normalizedGroup.includes('financial')) return '$';
  if (normalizedRoute.includes('pipeline') || normalizedRoute.includes('opportunit')) return '◇';
  if (normalizedRoute.includes('audit') || normalizedRoute.includes('history')) return '⌕';
  if (normalizedRoute.includes('user') || normalizedRoute.includes('role') || normalizedRoute.includes('identity')) return '◎';
  if (normalizedRoute.includes('azure') || normalizedRoute.includes('entra') || normalizedRoute.includes('microsoft')) return '▦';
  if (normalizedRoute.includes('ai') || normalizedRoute.includes('celar')) return 'AI';
  if (normalizedRoute.includes('security') || normalizedRoute.includes('risk')) return '◆';
  if (normalizedRoute.includes('project') || normalizedRoute.includes('flowhive') || normalizedRoute.includes('forge')) return '▤';
  if (normalizedRoute.includes('support') || normalizedRoute.includes('guide')) return '?';
  return '■';
}

export default function DefaultEnterpriseViewController() {
  const [view, setView] = useState(readStoredView);
  const [route, setRoute] = useState(currentRoute);
  const [selectorHost, setSelectorHost] = useState(null);
  const [filterHost, setFilterHost] = useState(null);
  const [accessFilter, setAccessFilter] = useState('all');
  const [statusFilter, setStatusFilter] = useState('all');
  const [filterCounts, setFilterCounts] = useState({ visible: 0, total: 0 });
  const isDefault = view === DEFAULT_VIEW;

  useEffect(() => {
    document.documentElement.dataset.pulseView = view;
    try {
      window.localStorage.setItem(VIEW_STORAGE_KEY, view);
    } catch {
      // A blocked storage write must not prevent the selected presentation.
    }
    window.dispatchEvent(new CustomEvent('projectpulse:workspace-view-changed', { detail: { view } }));
  }, [view]);

  useEffect(() => {
    const onHashChange = () => setRoute(currentRoute());
    window.addEventListener('hashchange', onHashChange);
    return () => window.removeEventListener('hashchange', onHashChange);
  }, []);

  const applyModuleFilters = useCallback(() => {
    const cards = [...document.querySelectorAll('#modules-directory-page .modules-directory-card')];
    let visible = 0;
    for (const card of cards) {
      const text = String(card.textContent || '').toLowerCase();
      const enabled = !card.classList.contains('disabled') && !text.includes('disabled');
      const fullControl = text.includes('full control');
      const readOnly = text.includes('read only') || (!fullControl && text.includes('organization-wide'));
      const accessMatch = accessFilter === 'all'
        || (accessFilter === 'full-control' && fullControl)
        || (accessFilter === 'read-only' && readOnly);
      const statusMatch = statusFilter === 'all'
        || (statusFilter === 'enabled' && enabled)
        || (statusFilter === 'disabled' && !enabled);
      const matches = !isDefault || route !== 'modules' || (accessMatch && statusMatch);
      const desired = matches ? 'true' : 'false';
      if (card.dataset.pulseDefaultVisible !== desired) card.dataset.pulseDefaultVisible = desired;
      const icon = moduleIcon(card.dataset.moduleRoute, text);
      if (card.dataset.pulseModuleIcon !== icon) card.dataset.pulseModuleIcon = icon;
      if (matches) visible += 1;
    }
    setFilterCounts((current) => (
      current.visible === visible && current.total === cards.length
        ? current
        : { visible, total: cards.length }
    ));
  }, [accessFilter, isDefault, route, statusFilter]);

  const synchronize = useCallback(() => {
    const modulesPage = document.getElementById('modules-directory-page');
    const modulesActive = route === 'modules' && Boolean(modulesPage);
    document.body.classList.toggle('pulse-default-enterprise-view', isDefault);
    document.body.classList.toggle('pulse-alternate-enterprise-view', !isDefault);

    if (modulesActive) {
      modulesPage.classList.toggle('pulse-default-module-management', isDefault);
      const hero = modulesPage.querySelector('.modules-directory-hero');
      managedText(hero?.querySelector('.eyebrow'), 'Workspace administration', isDefault);
      managedText(hero?.querySelector('h1'), 'Module Management', isDefault);
      managedText(
        hero?.querySelector('p:not(.eyebrow)'),
        'Control module availability and configure access for your organization.',
        isDefault
      );
      const nextSelectorHost = ensureHost(hero, 'data-pulse-workspace-view-host');
      setSelectorHost((current) => current === nextSelectorHost ? current : nextSelectorHost);

      const controls = modulesPage.querySelector('.modules-directory-controls');
      const nextFilterHost = ensureHost(controls, 'data-pulse-workspace-filter-host');
      setFilterHost((current) => current === nextFilterHost ? current : nextFilterHost);
    } else {
      const header = document.querySelector('.workspace-header-context')
        || document.querySelector('.enterprise-top-navigation')
        || document.querySelector('main.app-shell');
      const nextSelectorHost = ensureHost(header, 'data-pulse-workspace-view-host');
      setSelectorHost((current) => current === nextSelectorHost ? current : nextSelectorHost);
      setFilterHost(null);
    }

    applyModuleFilters();
  }, [applyModuleFilters, isDefault, route]);

  useEffect(() => {
    let scheduled = false;
    const schedule = () => {
      if (scheduled) return;
      scheduled = true;
      window.requestAnimationFrame(() => {
        scheduled = false;
        synchronize();
      });
    };
    schedule();
    const root = document.getElementById('root') || document.body;
    const observer = new MutationObserver(schedule);
    observer.observe(root, { childList: true, subtree: true, characterData: true, attributes: true });
    window.addEventListener('projectpulse:view-as-changed', schedule);
    window.addEventListener('projectpulse:module-availability-changed', schedule);
    return () => {
      observer.disconnect();
      window.removeEventListener('projectpulse:view-as-changed', schedule);
      window.removeEventListener('projectpulse:module-availability-changed', schedule);
      document.querySelectorAll('[data-pulse-workspace-view-host], [data-pulse-workspace-filter-host]')
        .forEach((host) => host.remove());
    };
  }, [synchronize]);

  const selector = selectorHost ? createPortal(
    <section className={`pulse-workspace-view-switcher ${route === 'modules' ? 'is-module-page' : 'is-compact'}`} aria-label="Workspace view options">
      <span>View options</span>
      <div role="group" aria-label="Select workspace presentation">
        <button
          type="button"
          className={isDefault ? 'is-active' : ''}
          aria-pressed={isDefault}
          onClick={() => setView(DEFAULT_VIEW)}
        >
          <span aria-hidden="true">▦</span> Default view
        </button>
        <button
          type="button"
          className={!isDefault ? 'is-active' : ''}
          aria-pressed={!isDefault}
          onClick={() => setView(ALTERNATE_VIEW)}
        >
          <span aria-hidden="true">☰</span> Alternate view
        </button>
      </div>
    </section>,
    selectorHost
  ) : null;

  const filters = filterHost && isDefault && route === 'modules' ? createPortal(
    <div className="pulse-default-module-filter-strip">
      <label>
        <span>Access scope</span>
        <select value={accessFilter} onChange={(event) => setAccessFilter(event.target.value)}>
          <option value="all">All access scope</option>
          <option value="full-control">Full control</option>
          <option value="read-only">Read only</option>
        </select>
      </label>
      <label>
        <span>Status</span>
        <select value={statusFilter} onChange={(event) => setStatusFilter(event.target.value)}>
          <option value="all">All status</option>
          <option value="enabled">Enabled</option>
          <option value="disabled">Disabled</option>
        </select>
      </label>
      <button type="button" onClick={() => { setAccessFilter('all'); setStatusFilter('all'); }}>
        <span aria-hidden="true">▽</span> Filters
      </button>
      <p><strong>{filterCounts.visible}</strong> of {filterCounts.total} modules shown on one page.</p>
    </div>,
    filterHost
  ) : null;

  return <>{selector}{filters}</>;
}
