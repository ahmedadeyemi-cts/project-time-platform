import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import {
  canonicalModuleRoute,
  currentProjectPulseRoute,
  moduleForRoute,
  replaceTimesheetLabel
} from './module-availability-registry.js';
import './module-availability.css';

const REFRESH_INTERVAL_MS = 30000;

async function readJson(response) {
  const raw = await response.text();
  if (!raw.trim()) return {};
  try {
    return JSON.parse(raw);
  } catch {
    return { message: raw };
  }
}

function messageFrom(payload, fallback) {
  return payload?.message || payload?.status || fallback;
}

function normalizeTimesheetLabels(root = document) {
  const targets = root.querySelectorAll?.('a[href="#timesheet"], [data-route="timesheet"]') || [];
  for (const target of targets) {
    const walker = document.createTreeWalker(target, NodeFilter.SHOW_TEXT);
    const textNodes = [];
    while (walker.nextNode()) textNodes.push(walker.currentNode);
    for (const textNode of textNodes) {
      const next = replaceTimesheetLabel(textNode.nodeValue);
      if (next !== textNode.nodeValue) textNode.nodeValue = next;
    }
    target.setAttribute('aria-label', replaceTimesheetLabel(target.getAttribute('aria-label') || ''));
    target.setAttribute('title', replaceTimesheetLabel(target.getAttribute('title') || ''));
  }
}

function authorizedRoutesFromNavigation() {
  return new Set(
    Array.from(document.querySelectorAll('.enterprise-sidebar a[href^="#"], .enterprise-top-navigation a[href^="#"]'))
      .map((anchor) => canonicalModuleRoute(anchor.getAttribute('href')))
      .filter(Boolean)
  );
}

function applyModuleNavigationState(modules, isSuperAdministrator) {
  for (const module of modules) {
    const selectors = [`a[href="#${module.route}"]`];
    if (module.route === 'project-workload') {
      selectors.push('a[href="#project-manager-workload"]', 'a[href="#project-management-workload"]');
    }
    if (module.route === 'signed-handoff') selectors.push('a[href="#resource-assignment-handoff"]');

    for (const element of document.querySelectorAll(selectors.join(','))) {
      const hiddenForAvailability = !module.isEnabled && !isSuperAdministrator;
      if (hiddenForAvailability) {
        element.hidden = true;
        element.dataset.moduleAvailabilityHidden = 'true';
      } else if (element.dataset.moduleAvailabilityHidden === 'true') {
        element.hidden = false;
        delete element.dataset.moduleAvailabilityHidden;
      }

      element.classList.toggle(
        'projectpulse-module-disabled',
        !module.isEnabled && isSuperAdministrator
      );
      if (!module.isEnabled && isSuperAdministrator) {
        element.dataset.moduleAvailabilityStatus = 'Disabled';
      } else {
        delete element.dataset.moduleAvailabilityStatus;
      }
    }
  }

  normalizeTimesheetLabels();
}

function currentDisabledModule(modules, isSuperAdministrator) {
  if (!isSuperAdministrator) return null;
  const current = moduleForRoute(currentProjectPulseRoute());
  if (!current) return null;
  return modules.find((module) => module.moduleNumber === current.moduleNumber && !module.isEnabled) || null;
}

export default function ModuleAvailabilityController() {
  const [availability, setAvailability] = useState(null);
  const [auditEvents, setAuditEvents] = useState([]);
  const [portalHost, setPortalHost] = useState(null);
  const [authorizedRoutes, setAuthorizedRoutes] = useState(() => new Set());
  const [search, setSearch] = useState('');
  const [group, setGroup] = useState('all');
  const [busyModule, setBusyModule] = useState('');
  const [statusMessage, setStatusMessage] = useState('');
  const [error, setError] = useState('');
  const refreshTimer = useRef(null);

  const load = useCallback(async ({ preserveMessage = false } = {}) => {
    try {
      const response = await fetch('/api/module-availability', { cache: 'no-store' });
      const body = await readJson(response);
      if (!response.ok) throw new Error(messageFrom(body, 'Module availability could not be loaded.'));
      setAvailability(body);
      setError('');
      if (!preserveMessage) setStatusMessage('');

      if (body?.access?.canManage) {
        const auditResponse = await fetch('/api/module-availability/audit', { cache: 'no-store' });
        const auditBody = await readJson(auditResponse);
        if (auditResponse.ok) setAuditEvents(Array.isArray(auditBody.events) ? auditBody.events : []);
      } else {
        setAuditEvents([]);
      }
    } catch (loadError) {
      setError(loadError?.message || 'Module availability could not be loaded.');
    }
  }, []);

  useEffect(() => {
    void load();
    const interval = window.setInterval(() => void load({ preserveMessage: true }), REFRESH_INTERVAL_MS);
    const refresh = () => void load({ preserveMessage: true });
    window.addEventListener('projectpulse:module-availability-changed', refresh);
    window.addEventListener('projectpulse:view-as-changed', refresh);
    return () => {
      window.clearInterval(interval);
      window.removeEventListener('projectpulse:module-availability-changed', refresh);
      window.removeEventListener('projectpulse:view-as-changed', refresh);
    };
  }, [load]);

  useEffect(() => {
    const ensureHost = () => {
      const page = document.querySelector('#modules-directory-page');
      const active = currentProjectPulseRoute() === 'modules';
      if (!page || !active) {
        setPortalHost((current) => {
          if (current?.isConnected) current.remove();
          return null;
        });
        return;
      }

      page.classList.add('module-availability-governed');
      let host = page.querySelector(':scope > #module-availability-directory-host');
      if (!host) {
        host = document.createElement('div');
        host.id = 'module-availability-directory-host';
        const hero = page.querySelector(':scope > .modules-directory-hero');
        if (hero) hero.insertAdjacentElement('afterend', host);
        else page.prepend(host);
      }
      setPortalHost((current) => current === host ? current : host);
      setAuthorizedRoutes(authorizedRoutesFromNavigation());
    };

    ensureHost();
    const observer = new MutationObserver(() => {
      window.clearTimeout(refreshTimer.current);
      refreshTimer.current = window.setTimeout(ensureHost, 40);
    });
    observer.observe(document.body, { childList: true, subtree: true });
    window.addEventListener('hashchange', ensureHost);
    return () => {
      observer.disconnect();
      window.removeEventListener('hashchange', ensureHost);
      window.clearTimeout(refreshTimer.current);
      document.querySelector('#modules-directory-page')?.classList.remove('module-availability-governed');
      document.getElementById('module-availability-directory-host')?.remove();
    };
  }, []);

  useEffect(() => {
    const modules = Array.isArray(availability?.modules) ? availability.modules : [];
    if (!modules.length) return undefined;

    const apply = () => {
      applyModuleNavigationState(modules, Boolean(availability?.access?.isSuperAdministrator));
      setAuthorizedRoutes(authorizedRoutesFromNavigation());

      const current = moduleForRoute(currentProjectPulseRoute());
      if (!current) return;
      const state = modules.find((module) => module.moduleNumber === current.moduleNumber);
      if (state && !state.isEnabled && !availability?.access?.isSuperAdministrator) {
        setStatusMessage(`${state.displayName} is disabled. You were returned to the Modules directory.`);
        window.location.hash = 'modules';
      }
    };

    apply();
    const observer = new MutationObserver(() => {
      window.clearTimeout(refreshTimer.current);
      refreshTimer.current = window.setTimeout(apply, 40);
    });
    observer.observe(document.body, { childList: true, subtree: true });
    window.addEventListener('hashchange', apply);
    return () => {
      observer.disconnect();
      window.removeEventListener('hashchange', apply);
      window.clearTimeout(refreshTimer.current);
    };
  }, [availability]);

  async function toggleModule(module) {
    if (!availability?.access?.canManage || busyModule) return;
    const nextEnabled = !module.isEnabled;
    const action = nextEnabled ? 'enable' : 'disable';
    const warning = nextEnabled
      ? `Enable Module ${module.moduleNumber} — ${module.displayName}? Normal role and permission rules will apply.`
      : `Disable Module ${module.moduleNumber} — ${module.displayName}? Regular users will lose access, but no source code or data will be deleted.`;

    if (!window.confirm(warning)) return;
    const reason = window.prompt(`Optional reason to ${action} this module:`, '') ?? '';
    setBusyModule(module.moduleNumber);
    setError('');
    setStatusMessage('');

    try {
      const response = await fetch(`/api/module-availability/${encodeURIComponent(module.moduleNumber)}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          isEnabled: nextEnabled,
          expectedRevision: Number(module.revision || 0),
          reason
        })
      });
      const body = await readJson(response);
      if (!response.ok) throw new Error(messageFrom(body, `Module ${module.moduleNumber} could not be updated.`));
      setStatusMessage(body.message || `Module ${module.moduleNumber} updated.`);
      await load({ preserveMessage: true });
      window.dispatchEvent(new CustomEvent('projectpulse:module-availability-changed', { detail: body }));
    } catch (updateError) {
      setError(updateError?.message || `Module ${module.moduleNumber} could not be updated.`);
    } finally {
      setBusyModule('');
    }
  }

  const modules = Array.isArray(availability?.modules) ? availability.modules : [];
  const isSuperAdministrator = Boolean(availability?.access?.isSuperAdministrator);
  const canManage = Boolean(availability?.access?.canManage);
  const visibleModules = useMemo(() => {
    const term = String(search || '').trim().toLowerCase();
    return modules.filter((module) => {
      if (!isSuperAdministrator && (!module.isEnabled || !authorizedRoutes.has(module.route))) return false;
      if (group !== 'all' && module.group !== group) return false;
      if (!term) return true;
      return [module.moduleNumber, module.displayName, module.route, module.group]
        .some((value) => String(value || '').toLowerCase().includes(term));
    });
  }, [modules, isSuperAdministrator, authorizedRoutes, search, group]);

  const groups = useMemo(
    () => Array.from(new Set(
      modules
        .filter((module) => isSuperAdministrator || (module.isEnabled && authorizedRoutes.has(module.route)))
        .map((module) => module.group)
    )).sort((left, right) => left.localeCompare(right)),
    [modules, isSuperAdministrator, authorizedRoutes]
  );

  const disabledCurrent = currentDisabledModule(modules, isSuperAdministrator);

  const directory = portalHost ? createPortal(
    <div className="module-availability-directory">
      <div className="module-availability-summary">
        <div>
          <p className="eyebrow">Governed module availability</p>
          <h2>{canManage ? 'Enable or disable modules safely' : 'Available modules'}</h2>
          <p>
            Disabled modules are preserved and remain visible only to Super Administrators.
            Enabled modules continue to follow existing role and permission rules.
          </p>
        </div>
        <div className="module-availability-summary-counts">
          <span><strong>{modules.filter((module) => module.isEnabled).length}</strong> enabled</span>
          <span><strong>{modules.filter((module) => !module.isEnabled).length}</strong> disabled</span>
        </div>
      </div>

      {availability?.access?.isViewAs ? (
        <div className="module-availability-notice warning">
          View-As is read-only. Exit preview to change module availability.
        </div>
      ) : null}

      <div className="module-availability-controls">
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
            {groups.map((groupName) => <option value={groupName} key={groupName}>{groupName}</option>)}
          </select>
        </label>
        {(search || group !== 'all') ? (
          <button type="button" onClick={() => { setSearch(''); setGroup('all'); }}>Clear filters</button>
        ) : null}
      </div>

      <div className="module-availability-grid">
        {visibleModules.map((module) => (
          <article
            className={module.isEnabled ? 'module-availability-card' : 'module-availability-card disabled'}
            key={module.moduleNumber}
          >
            <div className="module-availability-card-heading">
              <span>Module {module.moduleNumber}</span>
              <span className={module.isEnabled ? 'module-state enabled' : 'module-state disabled'}>
                {module.isEnabled ? 'Enabled' : 'Disabled'}
              </span>
            </div>
            <h3>{module.displayName}</h3>
            <p>{module.group} · <code>{module.route}</code></p>
            {!module.isEnabled ? (
              <small>Preserved and visible only to Super Administrators.</small>
            ) : (
              <small>Visible when the user also passes role and permission checks.</small>
            )}
            <div className="module-availability-card-actions">
              <a href={`#${module.route}`}>Open module</a>
              {canManage ? (
                <label className="module-availability-switch">
                  <input
                    type="checkbox"
                    checked={Boolean(module.isEnabled)}
                    disabled={Boolean(busyModule)}
                    onChange={() => void toggleModule(module)}
                    aria-label={`${module.isEnabled ? 'Disable' : 'Enable'} Module ${module.moduleNumber} ${module.displayName}`}
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

      {canManage && auditEvents.length ? (
        <section className="module-availability-audit">
          <div>
            <p className="eyebrow">Audit history</p>
            <h3>Recent availability changes</h3>
          </div>
          <div className="module-availability-audit-list">
            {auditEvents.slice(0, 12).map((event) => (
              <div key={event.auditId}>
                <strong>Module {event.moduleNumber} — {event.displayName}</strong>
                <span>{event.newEnabled ? 'Enabled' : 'Disabled'} · {new Date(event.changedAt).toLocaleString()}</span>
                <small>{event.reason || 'No reason recorded.'}</small>
              </div>
            ))}
          </div>
        </section>
      ) : null}
    </div>,
    portalHost
  ) : null;

  return (
    <>
      {disabledCurrent ? (
        <div className="module-availability-super-banner">
          Module {disabledCurrent.moduleNumber} — {disabledCurrent.displayName} is disabled.
          It is visible because you are a Super Administrator.
        </div>
      ) : null}
      {statusMessage ? <div className="module-availability-toast success">{statusMessage}</div> : null}
      {error ? <div className="module-availability-toast error">{error}</div> : null}
      {directory}
    </>
  );
}
