import { useEffect, useMemo, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import './modules-directory-page.css';

const MODULES_ROUTE = 'modules';
const MODULES_HASH = '#modules';

function currentRoute() {
  return String(window.location.hash || '#dashboard').replace(/^#/, '').trim() || 'dashboard';
}

function cleanText(value) {
  return String(value ?? '').replace(/\s+/g, ' ').trim();
}

function moduleNumberFromLabel(label) {
  const match = cleanText(label).match(/\b(?:module\s*)?(\d{3}|\d{2}[a-z])\b/i);
  return match ? match[1].toUpperCase() : '';
}

function ensurePersistentModulesLink(active) {
  const navigation = document.querySelector('.enterprise-top-navigation');
  if (!navigation) return null;

  let link = navigation.querySelector('#projectpulse-modules-navigation-link');
  if (!link) {
    link = document.createElement('a');
    link.id = 'projectpulse-modules-navigation-link';
    link.href = MODULES_HASH;
    link.textContent = 'Modules';
    link.setAttribute('aria-label', 'Open Modules directory');

    const dashboardLink = Array.from(navigation.querySelectorAll(':scope > a'))
      .find((candidate) => candidate.getAttribute('href') === '#dashboard');

    if (dashboardLink) dashboardLink.insertAdjacentElement('afterend', link);
    else navigation.prepend(link);
  }

  link.classList.toggle('active', active);
  link.setAttribute('aria-current', active ? 'page' : 'false');
  return link;
}

function expandAuthorizedNavigationGroups() {
  const toggles = Array.from(document.querySelectorAll('.enterprise-sidebar-group-toggle'));
  for (const toggle of toggles) {
    if (toggle.getAttribute('aria-expanded') === 'false') toggle.click();
  }
}

function collectAuthorizedModules() {
  const modules = [];
  const seenRoutes = new Set();
  const groups = Array.from(document.querySelectorAll('.enterprise-sidebar-group'));

  for (const groupElement of groups) {
    const groupName = cleanText(
      groupElement.querySelector('.enterprise-sidebar-group-toggle .enterprise-nav-label')?.textContent
    ) || 'Modules';

    const anchors = Array.from(groupElement.querySelectorAll('.enterprise-sidebar-links.nested a[href^="#"]'));
    for (const anchor of anchors) {
      const href = anchor.getAttribute('href') || '';
      const route = href.replace(/^#/, '').trim();
      if (!route || route === 'dashboard' || route === MODULES_ROUTE || seenRoutes.has(route)) continue;

      const label = cleanText(anchor.querySelector('.enterprise-nav-label')?.textContent || anchor.textContent);
      if (!label) continue;

      seenRoutes.add(route);
      modules.push({
        route,
        href,
        label,
        moduleNumber: moduleNumberFromLabel(label),
        group: groupName,
        order: modules.length
      });
    }
  }

  return modules;
}

function updateWorkspaceHeading(active) {
  if (!active) return;
  const heading = document.querySelector('.workspace-header-context h1');
  if (heading && heading.textContent !== 'Modules') heading.textContent = 'Modules';
}

export default function ModulesDirectoryPortal() {
  const [route, setRoute] = useState(currentRoute);
  const [portalHost, setPortalHost] = useState(null);
  const [modules, setModules] = useState([]);
  const [search, setSearch] = useState('');
  const [group, setGroup] = useState('all');
  const refreshTimer = useRef(null);
  const active = route === MODULES_ROUTE;

  useEffect(() => {
    const handleHashChange = () => setRoute(currentRoute());
    window.addEventListener('hashchange', handleHashChange);
    return () => window.removeEventListener('hashchange', handleHashChange);
  }, []);

  useEffect(() => {
    let host = document.getElementById('modules-directory-portal-host');
    const main = document.querySelector('main.app-shell');
    if (!main) return undefined;

    if (!host) {
      host = document.createElement('div');
      host.id = 'modules-directory-portal-host';
      main.appendChild(host);
    }
    setPortalHost(host);

    return () => {
      if (host?.isConnected) host.remove();
    };
  }, []);

  useEffect(() => {
    const refresh = () => {
      ensurePersistentModulesLink(active);
      updateWorkspaceHeading(active);
      if (!active) return;

      expandAuthorizedNavigationGroups();
      window.clearTimeout(refreshTimer.current);
      refreshTimer.current = window.setTimeout(() => {
        setModules(collectAuthorizedModules());
      }, 80);
    };

    refresh();
    const observer = new MutationObserver(refresh);
    observer.observe(document.body, { childList: true, subtree: true, attributes: true, attributeFilter: ['aria-expanded', 'class'] });
    window.addEventListener('projectpulse:view-as-changed', refresh);

    return () => {
      observer.disconnect();
      window.removeEventListener('projectpulse:view-as-changed', refresh);
      window.clearTimeout(refreshTimer.current);
    };
  }, [active]);

  useEffect(() => {
    if (!active) {
      setSearch('');
      setGroup('all');
    }
  }, [active]);

  const groups = useMemo(
    () => Array.from(new Set(modules.map((module) => module.group))).sort((left, right) => left.localeCompare(right)),
    [modules]
  );

  const filteredModules = useMemo(() => {
    const term = cleanText(search).toLowerCase();
    return modules.filter((module) => {
      if (group !== 'all' && module.group !== group) return false;
      if (!term) return true;
      return [module.moduleNumber, module.label, module.route, module.group]
        .some((value) => cleanText(value).toLowerCase().includes(term));
    });
  }, [modules, search, group]);

  if (!portalHost || !active) return null;

  return createPortal(
    <section id="modules-directory-page" className="modules-directory-page" aria-labelledby="modules-directory-title">
      <header className="modules-directory-hero">
        <div>
          <p className="eyebrow">ProjectPulse workspace directory</p>
          <h1 id="modules-directory-title">Modules</h1>
          <p>Open the modules authorized for your current role or View-As identity.</p>
        </div>
        <div className="modules-directory-count">
          <strong>{filteredModules.length}</strong>
          <span>{filteredModules.length === 1 ? 'module available' : 'modules available'}</span>
        </div>
      </header>

      <div className="modules-directory-controls">
        <label>
          <span>Search modules</span>
          <input
            type="search"
            value={search}
            onChange={(event) => setSearch(event.target.value)}
            placeholder="Search by module name, route, or category"
          />
        </label>

        <label>
          <span>Category</span>
          <select value={group} onChange={(event) => setGroup(event.target.value)}>
            <option value="all">All categories</option>
            {groups.map((groupName) => (
              <option value={groupName} key={groupName}>{groupName}</option>
            ))}
          </select>
        </label>

        {(search || group !== 'all') ? (
          <button type="button" onClick={() => { setSearch(''); setGroup('all'); }}>Clear filters</button>
        ) : null}
      </div>

      {filteredModules.length ? (
        <div className="modules-directory-grid">
          {filteredModules.map((module) => (
            <a className="modules-directory-card" href={module.href} key={module.route}>
              <div className="modules-directory-card-heading">
                <span>{module.moduleNumber ? `Module ${module.moduleNumber}` : module.group}</span>
                <small>{module.group}</small>
              </div>
              <h2>{module.label}</h2>
              <p>Open the {module.label} workspace available to your current access scope.</p>
              <strong>Open module →</strong>
            </a>
          ))}
        </div>
      ) : (
        <div className="modules-directory-empty">
          <h2>No modules match the current filters</h2>
          <p>Clear the filters or confirm the selected View-As user has module access.</p>
          <button type="button" onClick={() => { setSearch(''); setGroup('all'); }}>Show authorized modules</button>
        </div>
      )}
    </section>,
    portalHost
  );
}
