import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { PROJECTPULSE_MODULES, canonicalModuleRoute, moduleForRoute, replaceTimesheetLabel } from './module-availability-registry.js';
// MODULE_006_AUTHORITATIVE_MODULE_DIRECTORY_PATCH
import './modules-directory-page.css';
import './module-availability.css';

const MODULES_ROUTE = 'modules';
const MODULES_HASH = '#modules';
const AVAILABILITY_REFRESH_MS = 30000;

const CANONICAL_MODULE_NUMBER_BY_ROUTE = Object.freeze({
  timesheet: '001',
  'manager-approval': '002',
  utilization: '003',
  'holiday-admin': '004',
  'project-allocation-info': '005',
  'psa-modules': '006',
  workflow: '007',
  'audit-history': '008',
  'user-admin': '009',
  'azure-admin': '010',
  'work-task-builder': '011',
  'role-admin': '012',
  'service-control': '013',
  'backup-dr': '014',
  'restore-validation': '015',
  'backup-retention': '016',
  'replication-sync': '017',
  'project-workload': '018',
  'project-manager-workload': '018',
  'project-management-workload': '018',
  'project-workspace': '019',
  'project-intake': '020',
  'customer-directory': '021',
  'cost-alerts': '022',
  'time-compliance': '023',
  'sales-intake': '024',
  'sow-generator': '025',
  'crm-integration': '026',
  'signed-handoff': '027',
  'resource-assignment-handoff': '027',
  'ai-time-entry': '028',
  'uat-validation': '029',
  reporting: '030',
  'sales-insights': '036',
  'roles-permissions-matrix': '037',
  'certify-integration': '038',
  'billing-readiness': '039',
  'project-closeout': '040',
  'closeout-email': '041',
  'invoice-billing-center': '042',
  'rate-card-administration': '055B',
  'work-register': '055C',
  'create-work-register': '055D',
  'calendar-capacity': '057',
  'cicd-pipeline': '058',
  contracts: '060',
  opportunities: '063',
  'ai-provider-configuration': '064',
  'entra-secret-administration': '065',
  'project-flowhive': '066',
  'global-mail-configuration': '067',
  'system-architecture': '068',
  'qualifications-certifications': '069',
  'capacity-pipeline-forecast': '070',
  'oncall-scheduling': '071',
  'oneassist-routing-directory': '072',
  'sales-coverage-alignment': '073',
  'oem-vendor-directory': '074',
  'integration-event-gateway': '075',
  'defect-tracker': '076',
  'release-deployment-control': '077',
  'observability-slo-health': '078',
  'data-governance-retention': '079',
  'customer-delivery-acceptance': '080',
  'security-operations': '997',
  'system-diagnostics': '998',
  'user-guide': '999'
});

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

function moduleNumberForRoute(route, source) {
  return moduleForRoute(route)?.moduleNumber
    || moduleNumberFromLabel(source)
    || CANONICAL_MODULE_NUMBER_BY_ROUTE[route]
    || '';
}

function canonicalDisplayLabel(route, label) {
  if (route === 'timesheet') return 'Timesheet';
  return replaceTimesheetLabel(label);
}

async function readJson(response) {
  const raw = await response.text();
  if (!raw.trim()) return {};
  try {
    return JSON.parse(raw);
  } catch {
    return { message: raw };
  }
}

function responseMessage(payload, fallback) {
  return payload?.message || payload?.status || fallback;
}

function normalizeOverrideResponse(body) {
  if (!Array.isArray(body?.states)) {
    throw new Error('Module availability returned an invalid override response. Existing modules remain available.');
  }

  const states = new Map();
  for (const state of body.states) {
    const moduleNumber = cleanText(state?.moduleNumber).toUpperCase();
    if (!moduleNumber) continue;
    states.set(moduleNumber, {
      isEnabled: state?.isEnabled !== false,
      revision: Number(state?.revision || 0),
      reason: cleanText(state?.reason),
      updatedAt: state?.updatedAt || null
    });
  }

  return {
    loaded: true,
    states,
    access: body?.access || {},
    registeredModuleCount: Number(body?.registeredModuleCount || 0),
    error: ''
  };
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

function groupKey(toggle) {
  return cleanText(toggle.querySelector('.enterprise-nav-label')?.textContent || toggle.textContent);
}

function expandAuthorizedNavigationGroups(expandedForDirectory) {
  const toggles = Array.from(document.querySelectorAll('.enterprise-sidebar-group-toggle'));
  for (const toggle of toggles) {
    if (toggle.getAttribute('aria-expanded') === 'false') {
      expandedForDirectory.add(groupKey(toggle));
      toggle.click();
    }
  }
}

function restoreNavigationGroups(expandedForDirectory) {
  if (!expandedForDirectory.size) return;
  const toggles = Array.from(document.querySelectorAll('.enterprise-sidebar-group-toggle'));
  for (const toggle of toggles) {
    if (expandedForDirectory.has(groupKey(toggle)) && toggle.getAttribute('aria-expanded') === 'true') {
      toggle.click();
    }
  }
  expandedForDirectory.clear();
}

function addAuthorizedModule(modules, seenRoutes, anchor, groupName) {
  const href = anchor.getAttribute('href') || '';
  const route = href.replace(/^#/, '').trim();
  if (!route || route === 'dashboard' || route === MODULES_ROUTE || seenRoutes.has(route)) return;

  const rawLabel = cleanText(anchor.querySelector('.enterprise-nav-label')?.textContent || anchor.textContent);
  const registryModule = moduleForRoute(route);
  const label = registryModule?.displayName || canonicalDisplayLabel(route, rawLabel);
  if (!label) return;

  const moduleNumberSource = [
    anchor.getAttribute('aria-label'),
    anchor.getAttribute('title'),
    anchor.dataset.moduleNumber,
    label
  ].filter(Boolean).join(' ');

  seenRoutes.add(route);
  modules.push({
    route,
    href,
    label,
    description: registryModule?.description || '',
    moduleNumber: moduleNumberForRoute(route, moduleNumberSource),
    group: registryModule?.group || groupName,
    order: modules.length
  });
}

function collectAuthorizedModules() {
  const modules = [];
  const seenRoutes = new Set();
  const sections = Array.from(document.querySelectorAll('.enterprise-sidebar-section'));
  const pinnedSection = sections.find((section) => (
    cleanText(section.querySelector('.enterprise-sidebar-section-title')?.textContent).toLowerCase() === 'pinned'
  ));

  const pinnedAnchors = Array.from(
    pinnedSection?.querySelectorAll('.enterprise-sidebar-links:not(.nested) > a[href^="#"]') ?? []
  );
  for (const anchor of pinnedAnchors) addAuthorizedModule(modules, seenRoutes, anchor, 'Pinned');

  const groups = Array.from(document.querySelectorAll('.enterprise-sidebar-group'));
  for (const groupElement of groups) {
    const groupName = cleanText(
      groupElement.querySelector('.enterprise-sidebar-group-toggle .enterprise-nav-label')?.textContent
    ) || 'Modules';

    const anchors = Array.from(groupElement.querySelectorAll('.enterprise-sidebar-links.nested a[href^="#"]'));
    for (const anchor of anchors) addAuthorizedModule(modules, seenRoutes, anchor, groupName);
  }

  return modules;
}

function superAdministratorModuleCatalog(authorizedModules) {
  const authorizedByRoute = new Map(
    authorizedModules.map((module) => [canonicalModuleRoute(module.route), module])
  );
  return PROJECTPULSE_MODULES.map((registryModule, index) => {
    const current = authorizedByRoute.get(registryModule.route);
    return {
      ...(current || {}),
      route: registryModule.route,
      href: current?.href || `#${registryModule.route}`,
      label: registryModule.displayName,
      description: registryModule.description || current?.description || '',
      moduleNumber: registryModule.moduleNumber,
      group: registryModule.group,
      order: index
    };
  });
}

function moduleListsMatch(left, right) {
  if (left.length !== right.length) return false;
  return left.every((item, index) => (
    item.route === right[index]?.route
    && item.label === right[index]?.label
    && item.group === right[index]?.group
    && item.moduleNumber === right[index]?.moduleNumber
  ));
}

function updateWorkspaceHeading(active) {
  if (!active) return;
  const heading = document.querySelector('.workspace-header-context h1');
  if (heading && heading.textContent !== 'Modules') heading.textContent = 'Modules';
}

function mutationOriginatesInsidePortal(mutation) {
  const target = mutation.target instanceof Element ? mutation.target : mutation.target.parentElement;
  return Boolean(target?.closest('#modules-directory-portal-host'));
}

function effectiveModuleState(module, availability) {
  const stored = availability.states.get(module.moduleNumber);
  return {
    ...module,
    isEnabled: stored?.isEnabled !== false,
    revision: Number(stored?.revision || 0),
    reason: stored?.reason || '',
    updatedAt: stored?.updatedAt || null
  };
}

export default function ModulesDirectoryPortal() {
  const [route, setRoute] = useState(currentRoute);
  const [portalHost, setPortalHost] = useState(null);
  const [modules, setModules] = useState([]);
  const [search, setSearch] = useState('');
  const [group, setGroup] = useState('all');
  const [availability, setAvailability] = useState({
    loaded: false,
    states: new Map(),
    access: {},
    registeredModuleCount: 0,
    error: ''
  });
  const [busyModule, setBusyModule] = useState('');
  const [statusMessage, setStatusMessage] = useState('');
  const refreshTimer = useRef(null);
  const expandedForDirectory = useRef(new Set());
  const active = route === MODULES_ROUTE;

  const loadAvailability = useCallback(async ({ preserveMessage = false } = {}) => {
    try {
      const response = await fetch('/api/module-availability/overrides', { cache: 'no-store' });
      const body = await readJson(response);
      if (!response.ok) throw new Error(responseMessage(body, 'Module availability controls could not be loaded.'));
      setAvailability(normalizeOverrideResponse(body));
      if (!preserveMessage) setStatusMessage('');
    } catch (error) {
      setAvailability((current) => ({
        ...current,
        loaded: false,
        states: new Map(),
        error: error?.message || 'Module availability controls could not be loaded.'
      }));
    }
  }, []);

  useEffect(() => {
    const handleHashChange = () => setRoute(currentRoute());
    window.addEventListener('hashchange', handleHashChange);
    return () => window.removeEventListener('hashchange', handleHashChange);
  }, []);

  useEffect(() => {
    const root = document.getElementById('root');
    if (!root) return undefined;

    let currentHost = null;
    const ensurePortalHost = () => {
      const main = document.querySelector('main.app-shell.enterprise-nav-enabled');
      if (!main) {
        if (currentHost?.isConnected) currentHost.remove();
        currentHost = null;
        setPortalHost(null);
        return;
      }

      let host = main.querySelector(':scope > #modules-directory-portal-host');
      if (!host) {
        document.getElementById('modules-directory-portal-host')?.remove();
        host = document.createElement('div');
        host.id = 'modules-directory-portal-host';
        main.appendChild(host);
      }

      if (currentHost !== host) {
        currentHost = host;
        setPortalHost(host);
      }
    };

    ensurePortalHost();
    const rootObserver = new MutationObserver(ensurePortalHost);
    rootObserver.observe(root, { childList: true, subtree: true, attributes: true, attributeFilter: ['class'] });

    return () => {
      rootObserver.disconnect();
      if (currentHost?.isConnected) currentHost.remove();
    };
  }, []);

  useEffect(() => {
    const refresh = () => {
      ensurePersistentModulesLink(active);
      updateWorkspaceHeading(active);
      if (!active) return;

      expandAuthorizedNavigationGroups(expandedForDirectory.current);
      window.clearTimeout(refreshTimer.current);
      refreshTimer.current = window.setTimeout(() => {
        const nextModules = collectAuthorizedModules();
        setModules((current) => moduleListsMatch(current, nextModules) ? current : nextModules);
      }, 80);
    };

    refresh();
    const root = document.getElementById('root');
    const observer = root ? new MutationObserver((mutations) => {
      if (mutations.every(mutationOriginatesInsidePortal)) return;
      refresh();
    }) : null;

    observer?.observe(root, {
      childList: true,
      subtree: true,
      characterData: true,
      attributes: true,
      attributeFilter: ['aria-expanded', 'class', 'hidden']
    });

    window.addEventListener('projectpulse:view-as-changed', refresh);
    window.addEventListener('projectpulse:module-availability-changed', refresh);

    return () => {
      observer?.disconnect();
      window.removeEventListener('projectpulse:view-as-changed', refresh);
      window.removeEventListener('projectpulse:module-availability-changed', refresh);
      window.clearTimeout(refreshTimer.current);
      if (active) restoreNavigationGroups(expandedForDirectory.current);
    };
  }, [active]);

  useEffect(() => {
    if (!active) {
      setSearch('');
      setGroup('all');
      return undefined;
    }

    void loadAvailability();
    const interval = window.setInterval(() => void loadAvailability({ preserveMessage: true }), AVAILABILITY_REFRESH_MS);
    const refresh = () => void loadAvailability({ preserveMessage: true });
    window.addEventListener('projectpulse:view-as-changed', refresh);
    window.addEventListener('projectpulse:module-availability-changed', refresh);
    return () => {
      window.clearInterval(interval);
      window.removeEventListener('projectpulse:view-as-changed', refresh);
      window.removeEventListener('projectpulse:module-availability-changed', refresh);
    };
  }, [active, loadAvailability]);

  async function toggleModule(module) {
    if (!availability.access?.canManage || busyModule) return;
    const nextEnabled = !module.isEnabled;
    const action = nextEnabled ? 'enable' : 'disable';
    const warning = nextEnabled
      ? `Enable Module ${module.moduleNumber} — ${module.label}? Normal role and permission rules will apply.`
      : `Disable Module ${module.moduleNumber} — ${module.label}? Regular users will lose access, but no source code or data will be deleted.`;

    if (!window.confirm(warning)) return;
    const reason = window.prompt(`Optional reason to ${action} this module:`, '') ?? '';
    setBusyModule(module.moduleNumber);
    setStatusMessage('');

    try {
      const response = await fetch(`/api/module-availability/${encodeURIComponent(module.moduleNumber)}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          isEnabled: nextEnabled,
          expectedRevision: module.revision,
          reason
        })
      });
      const body = await readJson(response);
      if (!response.ok) throw new Error(responseMessage(body, `Module ${module.moduleNumber} could not be updated.`));
      setStatusMessage(body.message || `Module ${module.moduleNumber} updated.`);
      await loadAvailability({ preserveMessage: true });
      window.dispatchEvent(new CustomEvent('projectpulse:module-availability-changed', { detail: body }));
    } catch (error) {
      setAvailability((current) => ({
        ...current,
        error: error?.message || `Module ${module.moduleNumber} could not be updated.`
      }));
    } finally {
      setBusyModule('');
    }
  }

  const isSuperAdministrator = Boolean(availability.access?.isSuperAdministrator);
  const canManage = Boolean(availability.access?.canManage);
  const effectiveRoles = Array.isArray(availability.access?.effectiveRoles)
    ? availability.access.effectiveRoles
    : [];

  const directoryModules = useMemo(
    () => isSuperAdministrator ? superAdministratorModuleCatalog(modules) : modules,
    [isSuperAdministrator, modules]
  );

  const enrichedModules = useMemo(
    () => directoryModules.map((module) => effectiveModuleState(module, availability)),
    [directoryModules, availability]
  );

  const visibleModules = useMemo(() => {
    const term = cleanText(search).toLowerCase();
    return enrichedModules.filter((module) => {
      if (availability.loaded && !isSuperAdministrator && !module.isEnabled) return false;
      if (group !== 'all' && module.group !== group) return false;
      if (!term) return true;
      return [module.moduleNumber, module.label, module.route, module.group]
        .some((value) => cleanText(value).toLowerCase().includes(term));
    });
  }, [enrichedModules, availability.loaded, isSuperAdministrator, search, group]);

  const groups = useMemo(
    () => Array.from(new Set(
      enrichedModules
        .filter((module) => !availability.loaded || isSuperAdministrator || module.isEnabled)
        .map((module) => module.group)
    )).sort((left, right) => left.localeCompare(right)),
    [enrichedModules, availability.loaded, isSuperAdministrator]
  );

  const disabledCount = availability.loaded
    ? enrichedModules.filter((module) => !module.isEnabled).length
    : 0;

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
          <strong>{visibleModules.length}</strong>
          <span>{visibleModules.length === 1 ? 'module available' : 'modules available'}</span>
        </div>
      </header>

      <div className={availability.error ? 'modules-directory-availability-bar warning' : 'modules-directory-availability-bar'}>
        <div>
          <strong>{canManage ? 'Module availability controls' : 'Module availability'}</strong>
          {availability.error ? (
            <span>{availability.error} Existing module cards remain available and no module is treated as disabled.</span>
          ) : availability.loaded ? (
            <span>
              Missing overrides default to Enabled. {canManage
                ? 'Use the switches on each card to enable or disable a module safely.'
                : `Toggle controls require SUPER_ADMINISTRATOR. Effective roles: ${effectiveRoles.join(', ') || 'none reported'}.`}
            </span>
          ) : (
            <span>Loading availability overrides. Existing module cards remain available.</span>
          )}
        </div>
        {availability.loaded ? (
          <div className="modules-directory-availability-counts">
            <span><strong>{Math.max(enrichedModules.length - disabledCount, 0)}</strong> enabled</span>
            <span><strong>{disabledCount}</strong> disabled</span>
          </div>
        ) : null}
      </div>

      {availability.access?.isViewAs ? (
        <div className="module-availability-notice warning">View-As is read-only. Exit preview to change module availability.</div>
      ) : null}
      {statusMessage ? <div className="module-availability-notice success">{statusMessage}</div> : null}

      <div className="modules-directory-controls">
        <label>
          <span>Search modules</span>
          <input
            type="search"
            value={search}
            onChange={(event) => setSearch(event.target.value)}
            placeholder="Search by module number, name, route, or category"
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

      {visibleModules.length ? (
        <div className="modules-directory-grid">
          {visibleModules.map((module) => (
            <article
              className={module.isEnabled ? 'modules-directory-card' : 'modules-directory-card disabled'}
              data-module-number={module.moduleNumber}
              data-module-route={module.route}
              key={module.route}
            >
              <div className="modules-directory-card-heading">
                <span>{module.moduleNumber ? `Module ${module.moduleNumber}` : 'Module number unavailable'}</span>
                {availability.loaded ? (
                  <small className={module.isEnabled ? 'module-state enabled' : 'module-state disabled'}>
                    {module.isEnabled ? 'Enabled' : 'Disabled'}
                  </small>
                ) : <small>{module.group}</small>}
              </div>
              <h2>{module.label}</h2>
              <p>{module.description || `Open the ${module.label} workspace available to your current access scope.`}</p>
              {isSuperAdministrator ? <div className="module-authority-full-control">Full Control · Organization-wide</div> : null}
              <div className="modules-directory-card-actions">
                <a
                  className="modules-directory-open-link"
                  data-module-open-route={module.route}
                  href={module.href || `#${module.route}`}
                  aria-label={`Open Module ${module.moduleNumber} — ${module.label}`}
                >
                  Open module →
                </a>
                {canManage ? (
                  <label className="module-availability-switch">
                    <input
                      type="checkbox"
                      checked={Boolean(module.isEnabled)}
                      disabled={Boolean(busyModule)}
                      onChange={() => void toggleModule(module)}
                      aria-label={`${module.isEnabled ? 'Disable' : 'Enable'} Module ${module.moduleNumber} ${module.label}`}
                    />
                    <span aria-hidden="true" />
                    <strong>{busyModule === module.moduleNumber ? 'Saving…' : module.isEnabled ? 'On' : 'Off'}</strong>
                  </label>
                ) : null}
              </div>
              {module.reason ? <div className="module-availability-reason">Reason: {module.reason}</div> : null}
            </article>
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
