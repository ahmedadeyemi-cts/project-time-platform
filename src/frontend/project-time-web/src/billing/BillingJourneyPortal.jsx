import { useCallback, useEffect, useMemo, useState } from 'react';
import { createPortal } from 'react-dom';
import BillingInvoiceAnalyticsPanel from './BillingInvoiceAnalyticsPanel.jsx';
import './billing-journey.css';

const STORAGE_KEY = 'projectPulseBillingJourneyContext';
const ACTIVE_ROUTES = new Set([
  'work-register',
  'project-allocation-info',
  'billing-readiness',
  'invoice-billing-center',
  'project-closeout',
  'reporting'
]);

const STEPS = Object.freeze([
  { key: 'project', module: '055C', route: 'work-register', label: 'Project record', shortLabel: 'Project' },
  { key: 'expenses', module: '005', route: 'project-allocation-info', label: 'Expenses', shortLabel: 'Expenses' },
  { key: 'readiness', module: '039', route: 'billing-readiness', label: 'Billing readiness', shortLabel: 'Readiness' },
  { key: 'invoice', module: '042', route: 'invoice-billing-center', label: 'Invoice', shortLabel: 'Invoice' },
  { key: 'closeout', module: '040', route: 'project-closeout', label: 'Closeout', shortLabel: 'Closeout' },
  { key: 'analytics', module: '030', route: 'reporting', label: 'Analytics', shortLabel: 'Analytics' }
]);

const TITLES = Object.freeze({
  'work-register': /manage existing projects|work register/i,
  'project-allocation-info': /project expense|expense upload|allocation/i,
  'billing-readiness': /billing readiness/i,
  'invoice-billing-center': /invoice.*billing/i,
  'project-closeout': /project closeout|closeout/i,
  reporting: /analytics center|reporting/i
});

function currentRoute() {
  return window.location.hash.replace(/^#/, '').split('?')[0] || 'dashboard';
}

function currentHashParams() {
  const query = window.location.hash.split('?')[1] || '';
  return new URLSearchParams(query);
}

function readStoredContext() {
  try {
    const parsed = JSON.parse(window.sessionStorage.getItem(STORAGE_KEY) || 'null');
    return parsed?.projectId ? parsed : null;
  } catch {
    return null;
  }
}

function writeStoredContext(project) {
  if (!project?.projectId) {
    window.sessionStorage.removeItem(STORAGE_KEY);
    return;
  }
  window.sessionStorage.setItem(STORAGE_KEY, JSON.stringify({
    projectId: project.projectId,
    customerName: project.customerName || '',
    projectCode: project.projectCode || '',
    projectName: project.projectName || '',
    contractType: project.contractType || '',
    updatedAt: new Date().toISOString()
  }));
}

function initialProjectId() {
  const params = currentHashParams();
  const fromHash = params.get('billingProjectId') || params.get('projectId');
  if (fromHash) return fromHash;
  const stored = readStoredContext();
  if (stored?.projectId) return stored.projectId;
  try {
    const closeout = JSON.parse(window.sessionStorage.getItem('projectPulseProjectCloseoutHandoff') || 'null');
    if (closeout?.projectId) return closeout.projectId;
  } catch {
    // Ignore malformed legacy handoff data.
  }
  return '';
}

function normalizeProject(project) {
  if (!project || typeof project !== 'object') return null;
  const projectId = String(project.projectId || project.workId || project.id || '').trim();
  if (!projectId) return null;
  return {
    projectId,
    customerName: String(project.customerName || project.customer || '').trim(),
    projectCode: String(project.projectCode || project.workCode || project.code || '').trim(),
    projectName: String(project.projectName || project.workName || project.name || '').trim(),
    contractType: String(project.contractType || project.billingModel || '').trim(),
    status: String(project.status || project.projectStatus || '').trim()
  };
}

function projectArrays(payload) {
  const arrays = [
    payload?.projects,
    payload?.candidates,
    payload?.data?.projects,
    payload?.data?.candidates,
    payload?.billingCandidates,
    payload?.items
  ];
  return arrays.filter(Array.isArray).flat();
}

function mergeProjects(...payloads) {
  const map = new Map();
  for (const payload of payloads) {
    for (const candidate of projectArrays(payload)) {
      const project = normalizeProject(candidate);
      if (!project) continue;
      const existing = map.get(project.projectId) || {};
      map.set(project.projectId, { ...existing, ...project });
    }
  }
  return [...map.values()].sort((left, right) =>
    `${left.customerName} ${left.projectCode} ${left.projectName}`
      .localeCompare(`${right.customerName} ${right.projectCode} ${right.projectName}`));
}

async function fetchJson(path) {
  const response = await fetch(path, {
    credentials: 'include',
    cache: 'no-store',
    headers: { Accept: 'application/json' }
  });
  const payload = await response.json().catch(() => null);
  if (!response.ok) {
    throw new Error(payload?.message || payload?.status || `${path} returned HTTP ${response.status}.`);
  }
  return payload;
}

function isVisible(element) {
  if (!(element instanceof HTMLElement)) return false;
  const style = window.getComputedStyle(element);
  return style.display !== 'none' && style.visibility !== 'hidden';
}

function findRouteRoot(route) {
  const known = {
    'invoice-billing-center': '.m042-center',
    'billing-readiness': '.billing-readiness-center',
    'project-closeout': '.project-closeout-center',
    'work-register': '.work-register-center',
    reporting: '.analytics-center'
  }[route];
  if (known) {
    const root = document.querySelector(known);
    if (root && isVisible(root)) return root;
  }

  const titlePattern = TITLES[route];
  const heading = [...document.querySelectorAll('h1, h2')]
    .find((item) => isVisible(item) && titlePattern?.test(item.textContent || ''));
  if (!heading) return null;

  return heading.closest(
    '[data-module], [data-module-code], [data-module-number], [class$="-center"], [class*="-center "], [class$="-workspace"], [class*="-workspace "], [class$="-page"], [class*="-page "]'
  ) || heading.closest('main') || heading.parentElement?.parentElement || null;
}

function installHost(route) {
  const root = findRouteRoot(route);
  if (!root) return null;
  const existing = root.querySelector(':scope > [data-billing-journey-host="true"]');
  if (existing) return existing;

  const host = document.createElement('div');
  host.setAttribute('data-billing-journey-host', 'true');
  host.className = 'billing-journey-host';
  const header = root.querySelector(':scope > header') || root.querySelector('header');
  if (header?.parentElement === root) {
    header.insertAdjacentElement('afterend', host);
  } else {
    root.prepend(host);
  }
  return host;
}

function dispatchProjectContext(project) {
  if (!project?.projectId) return;
  window.dispatchEvent(new CustomEvent('projectpulse:project-context-changed', {
    detail: {
      projectId: project.projectId,
      projectCode: project.projectCode,
      projectName: project.projectName,
      customerName: project.customerName,
      source: 'billing-journey'
    }
  }));
}

function setSelectValue(select, value) {
  const option = [...select.options].find((item) => String(item.value) === String(value));
  if (!option || select.value === String(value)) return false;
  const setter = Object.getOwnPropertyDescriptor(HTMLSelectElement.prototype, 'value')?.set;
  setter?.call(select, String(value));
  select.dispatchEvent(new Event('input', { bubbles: true }));
  select.dispatchEvent(new Event('change', { bubbles: true }));
  return true;
}

function normalizeModule042Commands() {
  const detail = document.querySelector('.m042-invoice-detail-panel');
  if (!detail) return;

  const privacy = detail.querySelector('.m042-detail-privacy');
  if (privacy) privacy.setAttribute('data-billing-journey-redundant-output', 'true');

  const actions = detail.querySelector('.m042-detail-actions');
  if (actions) {
    [...actions.querySelectorAll(':scope > button')].forEach((button) => {
      if (/download\s+(pdf|excel)/i.test(button.textContent || '')) {
        button.setAttribute('data-billing-journey-redundant-output', 'true');
      }
    });
    actions.querySelector('.m042-popup-free-note')
      ?.setAttribute('data-billing-journey-redundant-output', 'true');
  }

  const heading = detail.querySelector('.m042-detail-heading h3');
  if (heading && /pdf|excel/i.test(heading.textContent || '')) {
    heading.textContent = 'Invoice detail and Certinia delivery';
  }
}

function adoptProjectContext(route, project) {
  if (!project?.projectId) return;

  if (route === 'work-register') {
    window.sessionStorage.setItem('projectPulseOpenWorkId', project.projectId);
  }
  if (route === 'project-closeout') {
    window.sessionStorage.setItem('projectPulseProjectCloseoutHandoff', JSON.stringify({
      projectId: project.projectId,
      projectCode: project.projectCode,
      projectName: project.projectName,
      customerName: project.customerName,
      requestedAt: new Date().toISOString(),
      source: 'billing-journey'
    }));
  }

  const projectSelectors = [...document.querySelectorAll('select')]
    .filter((select) => !select.closest('.billing-journey'))
    .filter((select) => [...select.options].some((option) => String(option.value) === project.projectId));
  projectSelectors.slice(0, 2).forEach((select) => setSelectValue(select, project.projectId));

  const directTarget = document.querySelector(`[data-project-id="${CSS.escape(project.projectId)}"]`);
  if (directTarget instanceof HTMLElement && !directTarget.closest('.billing-journey')) directTarget.click();

  if (route === 'invoice-billing-center') {
    const tokens = [project.projectCode, project.projectName].filter(Boolean).map((value) => value.toLowerCase());
    const row = [...document.querySelectorAll('.m042-card tbody tr')]
      .filter((candidate) => !candidate.classList.contains('m042-history-row'))
      .find((candidate) => {
        const content = (candidate.textContent || '').toLowerCase();
        return tokens.some((token) => token.length > 2 && content.includes(token));
      });
    if (row instanceof HTMLElement && !row.classList.contains('selected')) row.click();
    normalizeModule042Commands();
  }
}

function statusLabel(state) {
  return ({
    complete: 'Complete',
    available: 'Available',
    in_progress: 'In progress',
    attention: 'Needs attention',
    pending: 'Pending',
    locked: 'Locked',
    not_required: 'No action needed'
  })[state] || 'Not evaluated';
}

function money(value) {
  const amount = Number(value ?? 0);
  return Number.isFinite(amount)
    ? amount.toLocaleString(undefined, { style: 'currency', currency: 'USD' })
    : '—';
}

function dateTime(value) {
  if (!value) return 'Not recorded';
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? String(value) : parsed.toLocaleString();
}

export default function BillingJourneyPortal() {
  const [activeRoute, setActiveRoute] = useState(currentRoute);
  const [mountNode, setMountNode] = useState(null);
  const [collapsed, setCollapsed] = useState(false);
  const [projectsState, setProjectsState] = useState({ loading: false, error: '', projects: [] });
  const [selectedProjectId, setSelectedProjectId] = useState(initialProjectId);
  const [journeyState, setJourneyState] = useState({ loading: false, error: '', data: null });
  const [activityOpen, setActivityOpen] = useState(false);

  const active = ACTIVE_ROUTES.has(activeRoute);
  const selectedProject = useMemo(() => {
    const fromList = projectsState.projects.find((project) => project.projectId === selectedProjectId);
    if (fromList) return fromList;
    const stored = readStoredContext();
    return stored?.projectId === selectedProjectId ? normalizeProject(stored) : null;
  }, [projectsState.projects, selectedProjectId]);

  const refreshJourney = useCallback(async (projectId = selectedProjectId) => {
    if (!projectId) {
      setJourneyState({ loading: false, error: '', data: null });
      return;
    }
    setJourneyState((current) => ({ ...current, loading: true, error: '' }));
    try {
      const data = await fetchJson(`/api/billing-journey/projects/${projectId}`);
      setJourneyState({ loading: false, error: '', data });
    } catch (error) {
      setJourneyState({
        loading: false,
        error: error instanceof Error ? error.message : 'Unable to load the billing journey.',
        data: null
      });
    }
  }, [selectedProjectId]);

  useEffect(() => {
    const sync = () => setActiveRoute(currentRoute());
    window.addEventListener('hashchange', sync);
    sync();
    return () => window.removeEventListener('hashchange', sync);
  }, []);

  useEffect(() => {
    if (!active) {
      setMountNode(null);
      return undefined;
    }

    let frame = 0;
    const mount = () => {
      if (frame) return;
      frame = window.requestAnimationFrame(() => {
        frame = 0;
        const node = installHost(activeRoute);
        if (node) setMountNode(node);
        if (activeRoute === 'invoice-billing-center') normalizeModule042Commands();
      });
    };
    const observer = new MutationObserver(mount);
    observer.observe(document.getElementById('root') || document.body, { childList: true, subtree: true });
    mount();

    return () => {
      observer.disconnect();
      if (frame) window.cancelAnimationFrame(frame);
      document.querySelectorAll('[data-billing-journey-host="true"]').forEach((node) => node.remove());
      setMountNode(null);
    };
  }, [active, activeRoute]);

  useEffect(() => {
    if (!active) return undefined;
    let cancelled = false;
    setProjectsState((current) => ({ ...current, loading: true, error: '' }));

    Promise.allSettled([
      fetchJson('/api/project-expenses/context'),
      fetchJson('/api/billing/candidates')
    ]).then((results) => {
      if (cancelled) return;
      const payloads = results.filter((result) => result.status === 'fulfilled').map((result) => result.value);
      const projects = mergeProjects(...payloads);
      const failure = results.find((result) => result.status === 'rejected');
      setProjectsState({
        loading: false,
        error: projects.length ? '' : failure?.reason?.message || 'No accessible billing projects were returned.',
        projects
      });
      setSelectedProjectId((current) => {
        if (current && projects.some((project) => project.projectId === current)) return current;
        const requested = initialProjectId();
        return requested && projects.some((project) => project.projectId === requested) ? requested : '';
      });
    });

    return () => { cancelled = true; };
  }, [active]);

  useEffect(() => {
    if (!active || !selectedProjectId) return;
    const project = selectedProject || { projectId: selectedProjectId };
    writeStoredContext(project);
    dispatchProjectContext(project);
    void refreshJourney(selectedProjectId);

    let attempts = 0;
    const apply = () => {
      attempts += 1;
      adoptProjectContext(activeRoute, project);
      if (attempts < 12) window.setTimeout(apply, 250);
    };
    apply();
  }, [active, activeRoute, selectedProjectId, selectedProject, refreshJourney]);

  useEffect(() => {
    const handleContext = (event) => {
      const projectId = String(event?.detail?.projectId || '').trim();
      if (projectId) setSelectedProjectId(projectId);
    };
    const handleBillingChange = (event) => {
      const projectId = String(event?.detail?.projectId || selectedProjectId || '').trim();
      if (projectId) void refreshJourney(projectId);
    };
    window.addEventListener('projectpulse:project-context-changed', handleContext);
    window.addEventListener('projectpulse:billing-data-changed', handleBillingChange);
    window.addEventListener('projectpulse:billing-journey-refresh', handleBillingChange);
    return () => {
      window.removeEventListener('projectpulse:project-context-changed', handleContext);
      window.removeEventListener('projectpulse:billing-data-changed', handleBillingChange);
      window.removeEventListener('projectpulse:billing-journey-refresh', handleBillingChange);
    };
  }, [refreshJourney, selectedProjectId]);

  function selectProject(projectId) {
    setSelectedProjectId(projectId);
    const project = projectsState.projects.find((item) => item.projectId === projectId);
    writeStoredContext(project);
    if (project) dispatchProjectContext(project);
  }

  function navigate(stepOrRoute, projectOverride = selectedProject) {
    const step = typeof stepOrRoute === 'string'
      ? STEPS.find((item) => item.route === stepOrRoute || item.key === stepOrRoute)
      : stepOrRoute;
    if (!step) return;
    const project = projectOverride || (selectedProjectId ? { projectId: selectedProjectId } : null);
    if (project?.projectId) {
      writeStoredContext(project);
      if (step.route === 'work-register') window.sessionStorage.setItem('projectPulseOpenWorkId', project.projectId);
      if (step.route === 'project-closeout') {
        window.sessionStorage.setItem('projectPulseProjectCloseoutHandoff', JSON.stringify({
          projectId: project.projectId,
          projectCode: project.projectCode || '',
          projectName: project.projectName || '',
          customerName: project.customerName || '',
          requestedAt: new Date().toISOString(),
          source: 'billing-journey'
        }));
      }
    }
    const query = project?.projectId
      ? `?billingProjectId=${encodeURIComponent(project.projectId)}&billingSource=${encodeURIComponent(activeRoute)}`
      : '';
    window.location.hash = `${step.route}${query}`;
  }

  if (!active || !mountNode) return null;

  const data = journeyState.data;
  const summary = data?.summary ?? {};
  const stages = data?.stages ?? STEPS.map((step) => ({ ...step, state: 'pending', detail: 'Select a project to evaluate this stage.' }));
  const currentIndex = Math.max(0, STEPS.findIndex((step) => step.route === activeRoute));
  const previousStep = currentIndex > 0 ? STEPS[currentIndex - 1] : null;
  const recommendedStep = STEPS.find((step) => step.route === data?.recommended?.route)
    || (currentIndex < STEPS.length - 1 ? STEPS[currentIndex + 1] : null);
  const activity = data?.activity ?? [];

  return createPortal(
    <section className={`billing-journey ${collapsed ? 'is-collapsed' : ''}`} data-billing-journey="unified-v1">
      <header className="billing-journey__header">
        <div className="billing-journey__identity">
          <p className="billing-journey__eyebrow">Unified billing workflow · Modules 055C, 005, 039, 042, 040 &amp; 030</p>
          <h2>Billing &amp; Invoicing Journey</h2>
          {!collapsed ? (
            <p>
              Partial billing can repeat throughout delivery. A final invoice must include every remaining eligible source before governed project closeout.
            </p>
          ) : null}
        </div>
        <div className="billing-journey__controls">
          <label>
            <span>Billing project</span>
            <select value={selectedProjectId} onChange={(event) => selectProject(event.target.value)}>
              <option value="">Select a project to preserve context across modules</option>
              {projectsState.projects.map((project) => (
                <option key={project.projectId} value={project.projectId}>
                  {project.customerName || 'Customer'} — {project.projectCode || project.projectName || 'Project'}
                </option>
              ))}
            </select>
          </label>
          <button type="button" className="secondary" disabled={!selectedProjectId || journeyState.loading} onClick={() => void refreshJourney()}>
            {journeyState.loading ? 'Refreshing…' : 'Refresh status'}
          </button>
          <button type="button" className="secondary compact" onClick={() => setCollapsed((value) => !value)} aria-expanded={!collapsed}>
            {collapsed ? 'Expand' : 'Collapse'}
          </button>
        </div>
      </header>

      {!collapsed ? (
        <>
          {projectsState.error ? <div className="billing-journey__notice warning">{projectsState.error}</div> : null}
          {journeyState.error ? <div className="billing-journey__notice error" role="alert">{journeyState.error}</div> : null}

          <ol className="billing-journey__steps" aria-label="Billing and invoicing stages">
            {STEPS.map((step, index) => {
              const stage = stages.find((item) => item.key === step.key) || { state: 'pending', detail: '' };
              const activeStep = step.route === activeRoute;
              return (
                <li key={step.key} className={`${stage.state || 'pending'} ${activeStep ? 'active' : ''}`}>
                  <button type="button" onClick={() => navigate(step)} aria-current={activeStep ? 'step' : undefined}>
                    <span className="billing-journey__step-number">{index + 1}</span>
                    <span className="billing-journey__step-copy">
                      <small>Module {step.module}</small>
                      <strong>{step.label}</strong>
                      <em>{statusLabel(stage.state)}</em>
                    </span>
                  </button>
                  <p>{stage.detail}</p>
                </li>
              );
            })}
          </ol>

          {selectedProjectId && data ? (
            <div className="billing-journey__summary">
              <article><span>Billing mode</span><strong>{String(data.billingMode || 'not started').replaceAll('_', ' ')}</strong><small>Partial cycles remain separate from final billing.</small></article>
              <article><span>Ready sources</span><strong>{summary.remainingEligibleSourceCount ?? 0}</strong><small>{summary.eligibleUninvoicedTimeCount ?? 0} labor · {summary.readyUninvoicedNonLaborCount ?? 0} non-labor</small></article>
              <article><span>Partial invoices</span><strong>{summary.partialInvoiceCount ?? 0}</strong><small>Repeatable billing installments</small></article>
              <article><span>Final invoices</span><strong>{summary.finalInvoiceCount ?? 0}</strong><small>Required for final-invoice closeout</small></article>
              <article><span>Invoiced amount</span><strong>{money(summary.invoicedAmount)}</strong><small>{summary.latestInvoiceNumber || 'No invoice created'}</small></article>
              <article><span>Closeout</span><strong>{String(summary.closeoutStatus || 'not started').replaceAll('_', ' ')}</strong><small>{summary.billingDisposition ? String(summary.billingDisposition).replaceAll('_', ' ') : 'Billing disposition not recorded'}</small></article>
            </div>
          ) : (
            <div className="billing-journey__empty">
              <strong>Select one project once</strong>
              <span>The selection is retained while moving among project setup, expenses, readiness, invoicing, closeout, and Analytics.</span>
            </div>
          )}

          {data?.blockers?.length ? (
            <details className="billing-journey__blockers">
              <summary>{data.blockers.length} item(s) require attention</summary>
              <ul>{data.blockers.map((blocker) => <li key={blocker}>{blocker}</li>)}</ul>
            </details>
          ) : null}

          <div className="billing-journey__navigation">
            <div>
              {previousStep ? <button type="button" className="secondary" onClick={() => navigate(previousStep)}>← Back to {previousStep.shortLabel}</button> : null}
              <button type="button" className="secondary" onClick={() => navigate('project')}>Open project record</button>
            </div>
            <div>
              {data?.recommended ? <span>Recommended: {data.recommended.action}</span> : <span>Complete each governed stage in order.</span>}
              {recommendedStep ? <button type="button" className="primary" onClick={() => navigate(recommendedStep)}>{data?.recommended?.action || `Continue to ${recommendedStep.shortLabel}`} →</button> : null}
            </div>
          </div>

          {selectedProjectId ? (
            <section className="billing-journey__activity">
              <button type="button" className="billing-journey__activity-toggle" onClick={() => setActivityOpen((value) => !value)} aria-expanded={activityOpen}>
                <span><strong>Immutable billing activity</strong><small>{activity.length} recorded event(s) across project, expense, readiness, invoice, and closeout evidence</small></span>
                <span>{activityOpen ? 'Hide' : 'Show'}</span>
              </button>
              {activityOpen ? (
                <div className="billing-journey__activity-list">
                  {activity.slice(0, 30).map((event) => (
                    <article key={`${event.processArea}-${event.eventId}`}>
                      <div>
                        <span>{String(event.processArea || 'billing').replaceAll('_', ' ')}</span>
                        <strong>{event.summary || String(event.action || '').replaceAll('_', ' ')}</strong>
                        {event.reason ? <p>{event.reason}</p> : null}
                      </div>
                      <div>
                        <small>{event.actor || 'System'}</small>
                        <time>{dateTime(event.occurredAt)}</time>
                        <em>{event.immutable ? 'Immutable' : 'Recorded'}</em>
                      </div>
                    </article>
                  ))}
                  {!activity.length ? <div className="billing-journey__empty">No billing activity has been recorded for this project.</div> : null}
                </div>
              ) : null}
            </section>
          ) : null}

          {activeRoute === 'reporting' ? (
            <BillingInvoiceAnalyticsPanel
              projects={projectsState.projects}
              selectedProjectId={selectedProjectId}
              onProjectChange={selectProject}
              onOpenProject={(projectId) => {
                selectProject(projectId);
                const project = projectsState.projects.find((item) => item.projectId === projectId);
                navigate('invoice', project || { projectId });
              }}
            />
          ) : null}
        </>
      ) : null}
    </section>,
    mountNode
  );
}
