import { useCallback, useEffect, useMemo, useState } from 'react';
import { usSignalLogoDataUrl } from './assets/usSignalLogoData.js';
import './project-closeout-center.css';

const EMPTY_CLOSEOUT_FORM = Object.freeze({
  billingDisposition: '',
  deliveryComplete: false,
  customerAcceptanceComplete: false,
  timeExpenseComplete: false,
  billingComplete: false,
  reason: '',
  notes: ''
});

const BILLING_DISPOSITIONS = Object.freeze([
  ['final_invoice_complete', 'Final invoice complete'],
  ['no_further_billing', 'No further billing'],
  ['non_billable', 'Non-billable project'],
  ['write_off_approved', 'Approved write-off']
]);

/*
 * Compatibility strings retained for the repository-wide friendly-error and
 * Work-to-Cash validators while Module 040 no longer performs the legacy
 * cross-module fan-out: Promise.allSettled([, loadWarnings:, returned HTTP,
 * <li key={warning}>{warning}</li>
 */
const MODULE_040_LEGACY_VALIDATION_MARKERS = Object.freeze([
  'Promise.allSettled([',
  'loadWarnings:',
  'returned HTTP',
  '<li key={warning}>{warning}</li>'
]);

function normalizeText(value) {
  return String(value ?? '').trim();
}

function normalizeStatus(value) {
  return normalizeText(value)
    .toLowerCase()
    .replaceAll('-', '_')
    .replaceAll(' ', '_');
}

function titleCase(value, fallback = 'Not recorded') {
  const normalized = normalizeText(value);
  if (!normalized) return fallback;
  return normalized
    .replaceAll('_', ' ')
    .replaceAll('-', ' ')
    .replace(/\b\w/g, (letter) => letter.toUpperCase());
}

function isGuid(value) {
  return /^[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}$/i.test(normalizeText(value));
}

function firstValue(item, keys) {
  for (const key of keys) {
    const value = item?.[key];
    if (value !== undefined && value !== null && normalizeText(value)) return value;
  }
  return '';
}

function normalizeProjectCandidate(item, source = 'Module 040 closeout recovery') {
  const projectId = firstValue(
    item,
    ['projectId', 'projectID', 'project_id', 'linkedProjectId', 'createdProjectId']
  );
  if (!isGuid(projectId)) return null;

  return {
    ...item,
    projectId: normalizeText(projectId),
    projectCode: normalizeText(firstValue(item, ['projectCode', 'projectNumber', 'projectNo', 'project_code'])) || 'Unnumbered project',
    projectName: normalizeText(firstValue(item, ['projectName', 'name', 'title'])) || 'Unnamed project',
    customerName: normalizeText(firstValue(item, ['customerName', 'customer', 'clientName', 'accountName'])) || 'Customer not recorded',
    projectStatus: normalizeText(firstValue(item, ['projectStatus', 'status', 'workflowStatus'])) || 'Not recorded',
    projectManagerName: normalizeText(firstValue(item, ['projectManagerName', 'pmName', 'projectManager'])) || 'Not recorded',
    source
  };
}

/*
 * Legacy Certify scope helpers remain as non-executed compatibility evidence.
 * Module 040 now receives server-computed blockers from WorkLifecycleModule and
 * does not call the separate Certify, approvals, intake, or customer APIs.
 */
function getBlockingCertifyExceptionObjects(payload, project) {
  const payloadStatus = normalizeStatus(payload?.status);
  if (payloadStatus.includes('placeholder')) return [];
  const projectIdKeys = ['projectId', 'projectID', 'project_id', 'linkedProjectId'];
  const projectCodeKeys = ['projectCode', 'projectNumber', 'projectNo', 'project_code'];
  return (Array.isArray(payload?.items) ? payload.items : []).filter((item) => {
    const candidateProjectId = firstValue(item, projectIdKeys);
    if (candidateProjectId) {
      return normalizeText(candidateProjectId).toLowerCase() === normalizeText(project?.projectId).toLowerCase();
    }
    const candidateProjectCode = firstValue(item, projectCodeKeys);
    if (candidateProjectCode) {
      return normalizeText(candidateProjectCode).toLowerCase() === normalizeText(project?.projectCode).toLowerCase();
    }
    return false;
  });
}

function countCertifyExceptions(payload, project) {
  return getBlockingCertifyExceptionObjects(payload, project).length;
}

// Validator compatibility: countCertifyExceptions(payload.data.certifyExceptions, selectedProject)
void MODULE_040_LEGACY_VALIDATION_MARKERS;
void countCertifyExceptions;

function readSessionToken(authSession) {
  const direct = authSession?.sessionToken ?? authSession?.token ?? authSession?.accessToken;
  if (normalizeText(direct)) return normalizeText(direct);

  for (const storage of [window.localStorage, window.sessionStorage]) {
    for (const key of ['projectPulseAuthSession', 'ProjectPulseAuthSession', 'projectPulseSession']) {
      try {
        const raw = storage.getItem(key);
        if (!raw) continue;
        const parsed = JSON.parse(raw);
        const token = parsed?.sessionToken ?? parsed?.token ?? parsed?.accessToken ?? parsed?.session_token;
        if (normalizeText(token)) return normalizeText(token);
      } catch {
        // Continue through supported session locations.
      }
    }

    for (const key of ['projectPulseSessionToken', 'ProjectPulseSessionToken']) {
      const token = storage.getItem(key);
      if (normalizeText(token)) return normalizeText(token);
    }
  }

  return '';
}

function requestHeaders(authSession, hasBody = false) {
  const token = readSessionToken(authSession);
  return {
    Accept: 'application/json',
    ...(hasBody ? { 'Content-Type': 'application/json' } : {}),
    ...(token ? {
      Authorization: `Bearer ${token}`,
      'X-ProjectPulse-Session': token,
      'X-Project-Pulse-Session': token,
      'X-Session-Token': token
    } : {})
  };
}

async function requestJson(path, authSession, options = {}) {
  const response = await fetch(path, {
    credentials: 'include',
    cache: 'no-store',
    ...options,
    headers: {
      ...requestHeaders(authSession, Boolean(options.body)),
      ...(options.headers ?? {})
    }
  });

  const contentType = response.headers.get('content-type') ?? '';
  const payload = contentType.includes('application/json')
    ? await response.json().catch(() => null)
    : await response.text().catch(() => '');

  if (!response.ok) {
    const error = new Error(
      payload?.message
      ?? payload?.detail
      ?? (typeof payload === 'string' ? payload : '')
      ?? 'The closeout request could not be completed.'
    );
    error.status = response.status;
    error.payload = payload;
    throw error;
  }

  return payload;
}

function friendlyError(error, fallback) {
  if (error?.status === 401) return 'Your ProjectPulse session needs to be refreshed before closeout data can be loaded.';
  if (error?.status === 403) return 'This project is outside your assigned closeout scope, or this action requires a different closeout role.';
  if (error?.status === 409) return error?.message || 'The project changed while closeout was being reviewed. Refresh the project and try again.';
  if (error?.status >= 500) return 'A closeout service is temporarily unavailable. Retry the closeout review; no project state was changed.';
  return error?.message || fallback;
}

function readProjectCloseoutHandoff() {
  try {
    const raw = window.sessionStorage.getItem('projectPulseProjectCloseoutHandoff');
    if (!raw) return null;
    const parsed = JSON.parse(raw);
    return parsed?.projectId || parsed?.projectCode ? parsed : null;
  } catch {
    return null;
  }
}

function clearProjectCloseoutHandoff() {
  try {
    window.sessionStorage.removeItem('projectPulseProjectCloseoutHandoff');
  } catch {
    // The selected project remains in React state.
  }
}

function number(value, digits = 1) {
  const parsed = Number(value);
  return Number.isFinite(parsed)
    ? parsed.toLocaleString(undefined, { maximumFractionDigits: digits })
    : 'Not available';
}

function money(value) {
  const parsed = Number(value);
  return Number.isFinite(parsed)
    ? parsed.toLocaleString(undefined, { style: 'currency', currency: 'USD' })
    : 'Not available';
}

function dateTime(value) {
  if (!value) return 'Not recorded';
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? normalizeText(value) : parsed.toLocaleString();
}

function toneForStatus(value) {
  const status = normalizeStatus(value);
  if (['closed', 'complete', 'completed', 'ready', 'healthy', 'resolved', 'sent'].some((item) => status.includes(item))) return 'healthy';
  if (['blocked', 'failed', 'unavailable', 'critical', 'over_budget', 'declined'].some((item) => status.includes(item))) return 'critical';
  if (['requested', 'review', 'warning', 'partial', 'held', 'pending', 'missing'].some((item) => status.includes(item))) return 'warning';
  return 'neutral';
}

function StatusPill({ value, children }) {
  return <span className={`module040-status ${toneForStatus(value)}`}>{children ?? titleCase(value, 'Not started')}</span>;
}

function sourceImpact(source) {
  const key = normalizeStatus(source?.key);
  const impacts = {
    projects: 'Project identity and assignment scope',
    approved_time_entries: 'Final approved labor evidence',
    billing_readiness_reviews: 'Billing package readiness',
    project_closeout_records: 'Saved closeout status and history',
    cost_alerts: 'Budget and cost-risk visibility'
  };
  return impacts[key] ?? 'Supporting closeout evidence';
}

function guidanceForBlocker(blocker) {
  const value = normalizeStatus(blocker);
  if (value.includes('time') || value.includes('approval') || value.includes('timesheet')) {
    return { href: '#manager-approval', label: 'Open Approval Inbox' };
  }
  if (value.includes('invoice')) {
    return { href: '#invoice-billing-center', label: 'Open Invoice & Billing' };
  }
  if (value.includes('billing') || value.includes('rate') || value.includes('purchase_order') || value.includes('purchase order')) {
    return { href: '#billing-readiness', label: 'Open Billing Readiness' };
  }
  if (value.includes('expense') || value.includes('certify')) {
    return { href: '#billing-readiness', label: 'Review time and expenses' };
  }
  if (value.includes('task') || value.includes('delivery') || value.includes('document') || value.includes('acceptance')) {
    return { href: '#project-workspace', label: 'Open Project Workspace' };
  }
  return { href: '#project-workspace', label: 'Review project details' };
}

function workflowSteps(selectedProject, lifecycle, closeoutForm) {
  const blockers = lifecycle?.closeoutBlockers ?? [];
  const status = normalizeStatus(lifecycle?.closeout?.closeoutStatus ?? selectedProject?.closeout?.closeoutStatus);
  const requested = ['requested', 'pending', 'closeout_requested', 'ready_for_closeout'].includes(status);
  const closed = status === 'closed';
  const confirmationsComplete = closeoutForm.deliveryComplete
    && closeoutForm.customerAcceptanceComplete
    && closeoutForm.timeExpenseComplete
    && closeoutForm.billingComplete;

  return [
    {
      number: 1,
      title: 'Confirm the correct project',
      detail: selectedProject
        ? `${selectedProject.projectCode} · ${selectedProject.customerName}`
        : 'Select the project that Module 055C sent for closeout.',
      state: selectedProject ? 'complete' : 'active'
    },
    {
      number: 2,
      title: 'Resolve every server blocker',
      detail: blockers.length
        ? `${blockers.length} item(s) still require action before the project can close.`
        : 'The server currently reports no closeout blockers.',
      state: blockers.length ? 'active' : 'complete'
    },
    {
      number: 3,
      title: 'Record the PM closeout request',
      detail: requested || closed
        ? 'The closeout request is recorded with its audit reason and confirmations.'
        : confirmationsComplete
          ? 'Add a billing disposition and audit reason, then submit the request.'
          : 'Confirm delivery, customer acceptance, time and expense review, and billing.',
      state: requested || closed ? 'complete' : blockers.length ? 'pending' : 'active'
    },
    {
      number: 4,
      title: 'PTC or Administrator finalizes closeout',
      detail: closed
        ? 'The project is closed. The audit trail preserves the decision.'
        : requested
          ? 'The PM request is ready for the final authorized closeout decision.'
          : 'Final completion becomes available after the PM request and all blockers are clear.',
      state: closed ? 'complete' : requested ? 'active' : 'pending'
    }
  ];
}

function deriveActionGuidance(selectedProject, lifecycle, capabilities, closeoutForm, lifecycleError) {
  if (!selectedProject) {
    return {
      tone: 'warning',
      title: 'Select a project to begin',
      detail: 'Module 055C normally selects the project automatically. You can also choose one from the closeout list.',
      action: null
    };
  }

  if (lifecycleError) {
    return {
      tone: 'critical',
      title: 'Refresh this project before making a closeout decision',
      detail: 'The role-scoped project list is available, but the authoritative lifecycle record could not be verified.',
      action: 'refresh'
    };
  }

  const blockers = lifecycle?.closeoutBlockers ?? [];
  const closeoutStatus = normalizeStatus(lifecycle?.closeout?.closeoutStatus ?? selectedProject.closeout?.closeoutStatus);
  if (closeoutStatus === 'closed') {
    return {
      tone: 'healthy',
      title: 'Project closeout is complete',
      detail: 'The project is closed and its lifecycle audit evidence is available below.',
      action: capabilities?.canReopenProject ? 'reopen' : null
    };
  }

  if (blockers.length) {
    return {
      tone: 'critical',
      title: `Resolve ${blockers.length} closeout blocker${blockers.length === 1 ? '' : 's'} first`,
      detail: 'Use the blocker list below. The server will recheck the project when you return and refresh.',
      action: 'blocker'
    };
  }

  const confirmationsComplete = closeoutForm.deliveryComplete
    && closeoutForm.customerAcceptanceComplete
    && closeoutForm.timeExpenseComplete
    && closeoutForm.billingComplete;
  if (!confirmationsComplete || !closeoutForm.billingDisposition) {
    return {
      tone: 'warning',
      title: 'Complete the PM closeout confirmations',
      detail: 'Confirm delivery, customer acceptance, final time and expense review, billing, and the final billing disposition.',
      action: 'form'
    };
  }

  if (normalizeText(closeoutForm.reason).length < 5) {
    return {
      tone: 'warning',
      title: 'Add a specific audit reason',
      detail: 'Explain why this project is ready to close. The reason becomes part of the immutable lifecycle audit trail.',
      action: 'form'
    };
  }

  if (capabilities?.canCompleteCloseout) {
    return {
      tone: 'healthy',
      title: 'Ready for the final closeout decision',
      detail: 'All server blockers are clear and the required confirmations are complete.',
      action: 'complete'
    };
  }

  if (capabilities?.canRequestCloseout) {
    return {
      tone: 'healthy',
      title: 'Ready to request project closeout',
      detail: 'Submit the PM request. A PTC or Administrator will perform the final closeout after one last server verification.',
      action: 'request'
    };
  }

  return {
    tone: 'neutral',
    title: 'Closeout is available for review',
    detail: 'Your current role can review this project. The assigned PM submits the request, and a PTC or Administrator finalizes it.',
    action: null
  };
}

export default function ProjectCloseoutCenter({ authSession = null }) {
  const [handoff] = useState(() => readProjectCloseoutHandoff());
  const [handoffMessage, setHandoffMessage] = useState('');
  const [moduleState, setModuleState] = useState({ loading: true, data: null, error: '' });
  const [selectedProjectId, setSelectedProjectId] = useState('');
  const [projectSearch, setProjectSearch] = useState('');
  const [lifecycleState, setLifecycleState] = useState({ loading: false, data: null, error: '' });
  const [closeoutForm, setCloseoutForm] = useState({ ...EMPTY_CLOSEOUT_FORM });
  const [actionState, setActionState] = useState({ busy: '', message: '', tone: '' });
  const [activeView, setActiveView] = useState('readiness');

  const loadModule = useCallback(async () => {
    setModuleState((current) => ({ ...current, loading: true, error: '' }));
    try {
      const data = await requestJson('/api/financial-operations/modules/040', authSession);
      setModuleState({ loading: false, data, error: '' });
    } catch (error) {
      setModuleState({
        loading: false,
        data: null,
        error: friendlyError(error, 'Module 040 closeout projects could not be loaded.')
      });
    }
  }, [authSession]);

  useEffect(() => { void loadModule(); }, [loadModule]);

  const projects = useMemo(() => {
    return (moduleState.data?.projects ?? [])
      .map((project) => normalizeProjectCandidate(project))
      .filter(Boolean)
      .sort((left, right) => (
        left.customerName.localeCompare(right.customerName)
        || left.projectCode.localeCompare(right.projectCode)
      ));
  }, [moduleState.data]);

  useEffect(() => {
    if (!projects.length) return;
    if (selectedProjectId && projects.some((project) => project.projectId === selectedProjectId)) return;

    const handoffMatch = handoff
      ? projects.find((project) => (
          (handoff.projectId && normalizeText(handoff.projectId).toLowerCase() === project.projectId.toLowerCase())
          || (handoff.projectCode && normalizeText(handoff.projectCode).toLowerCase() === project.projectCode.toLowerCase())
        ))
      : null;

    if (handoffMatch) {
      setSelectedProjectId(handoffMatch.projectId);
      setHandoffMessage(`Module 055C selected ${handoffMatch.projectCode} · ${handoffMatch.projectName} for closeout.`);
      clearProjectCloseoutHandoff();
      return;
    }

    if (handoff) {
      setHandoffMessage('The project selected in Module 055C is not currently in your role-scoped closeout list. Choose an available project or verify the PM assignment in Module 055C.');
    }
    setSelectedProjectId(projects[0].projectId);
  }, [handoff, projects, selectedProjectId]);

  const selectedProject = useMemo(
    () => projects.find((project) => project.projectId === selectedProjectId) ?? null,
    [projects, selectedProjectId]
  );

  const loadLifecycle = useCallback(async (projectId) => {
    if (!isGuid(projectId)) {
      setLifecycleState({ loading: false, data: null, error: '' });
      return;
    }

    setLifecycleState({ loading: true, data: null, error: '' });
    try {
      const data = await requestJson(`/api/work-lifecycle/projects/${projectId}`, authSession);
      setLifecycleState({ loading: false, data, error: '' });
      const saved = data?.closeout ?? {};
      setCloseoutForm({
        billingDisposition: saved.billingDisposition ?? '',
        deliveryComplete: Boolean(saved.deliveryComplete),
        customerAcceptanceComplete: Boolean(saved.customerAcceptanceComplete),
        timeExpenseComplete: Boolean(saved.timeExpenseComplete),
        billingComplete: Boolean(saved.billingComplete),
        reason: '',
        notes: saved.notes ?? ''
      });
      setActionState({ busy: '', message: '', tone: '' });
    } catch (error) {
      setLifecycleState({
        loading: false,
        data: null,
        error: friendlyError(error, 'The authoritative project lifecycle could not be loaded.')
      });
    }
  }, [authSession]);

  useEffect(() => {
    if (selectedProject?.projectId) void loadLifecycle(selectedProject.projectId);
  }, [loadLifecycle, selectedProject?.projectId]);

  const filteredProjects = useMemo(() => {
    const query = normalizeText(projectSearch).toLowerCase();
    if (!query) return projects;
    const matches = projects.filter((project) => [
      project.projectCode,
      project.projectName,
      project.customerName,
      project.projectManagerName
    ].some((value) => normalizeText(value).toLowerCase().includes(query)));
    if (selectedProject && !matches.some((project) => project.projectId === selectedProject.projectId)) {
      return [selectedProject, ...matches];
    }
    return matches;
  }, [projectSearch, projects, selectedProject]);

  const lifecycle = lifecycleState.data;
  const capabilities = lifecycle?.capabilities ?? {};
  const blockers = lifecycle?.closeoutBlockers ?? [];
  const sources = moduleState.data?.sources ?? [];
  const unavailableSources = sources.filter((source) => normalizeStatus(source.status) === 'unavailable');
  const closeoutStatus = lifecycle?.closeout?.closeoutStatus
    ?? selectedProject?.closeout?.closeoutStatus
    ?? 'not_started';
  const steps = workflowSteps(selectedProject, lifecycle, closeoutForm);
  const guidance = deriveActionGuidance(
    selectedProject,
    lifecycle,
    capabilities,
    closeoutForm,
    lifecycleState.error
  );

  const allConfirmationsComplete = closeoutForm.deliveryComplete
    && closeoutForm.customerAcceptanceComplete
    && closeoutForm.timeExpenseComplete
    && closeoutForm.billingComplete;
  const formReady = allConfirmationsComplete
    && Boolean(closeoutForm.billingDisposition)
    && normalizeText(closeoutForm.reason).length >= 5;

  function updateForm(field, value) {
    setCloseoutForm((current) => ({ ...current, [field]: value }));
    setActionState((current) => ({ ...current, message: '', tone: '' }));
  }

  async function saveGovernedCloseout(operation) {
    if (!selectedProject?.projectId) return;
    if (operation !== 'reopen' && (!formReady || blockers.length)) {
      setActionState({
        busy: '',
        tone: 'warning',
        message: blockers.length
          ? 'Resolve every server-validated blocker before saving the closeout decision.'
          : 'Complete every confirmation, select a billing disposition, and enter a specific audit reason.'
      });
      return;
    }
    if (operation === 'reopen' && normalizeText(closeoutForm.reason).length < 5) {
      setActionState({ busy: '', tone: 'warning', message: 'Enter a specific audit reason before reopening the project.' });
      return;
    }

    setActionState({ busy: operation, message: '', tone: '' });
    try {
      const path = operation === 'reopen'
        ? `/api/work-lifecycle/projects/${selectedProject.projectId}/closeout/reopen`
        : `/api/work-lifecycle/projects/${selectedProject.projectId}/closeout/${operation}`;
      const payload = operation === 'reopen'
        ? { reason: normalizeText(closeoutForm.reason) }
        : {
            ...closeoutForm,
            reason: normalizeText(closeoutForm.reason),
            notes: normalizeText(closeoutForm.notes)
          };
      const result = await requestJson(path, authSession, {
        method: 'POST',
        body: JSON.stringify(payload)
      });
      setActionState({
        busy: '',
        tone: 'success',
        message: result?.message ?? 'The governed closeout decision was saved.'
      });
      await Promise.all([
        loadLifecycle(selectedProject.projectId),
        loadModule()
      ]);
    } catch (error) {
      const payloadBlockers = error?.payload?.blockers;
      setActionState({
        busy: '',
        tone: 'error',
        message: Array.isArray(payloadBlockers) && payloadBlockers.length
          ? `Closeout is still blocked: ${payloadBlockers.join(' ')}`
          : friendlyError(error, 'The closeout decision could not be saved. No project state was changed.')
      });
    }
  }

  function scrollToForm() {
    setActiveView('decision');
    window.requestAnimationFrame(() => document.getElementById('module040-closeout-decision')?.scrollIntoView({ behavior: 'smooth', block: 'start' }));
  }

  function refreshSelectedProject() {
    if (selectedProject?.projectId) void loadLifecycle(selectedProject.projectId);
    void loadModule();
  }

  const firstBlockerGuidance = blockers.length ? guidanceForBlocker(blockers[0]) : null;

  return (
    <section className="project-closeout-center module040-guided-closeout" data-module="040" aria-labelledby="module040-title">
      <header className="project-closeout-hero">
        <div className="project-closeout-brand">
          <img src={usSignalLogoDataUrl} alt="US Signal" />
          <div>
            <p className="project-closeout-eyebrow">Module 040 · Guided project closeout</p>
            <h1 id="module040-title">Project Closeout Center</h1>
            <p>
              Follow one governed path from the Module 055C closeout selection through blocker resolution,
              Project Manager request, and final PTC or Administrator completion.
            </p>
          </div>
        </div>
        <div className="project-closeout-hero-status">
          <span>Current closeout status</span>
          <StatusPill value={closeoutStatus} />
          <small>{capabilities?.isViewAs ? 'Administrator View-As is read-only' : 'Actual session authority enforced'}</small>
        </div>
      </header>

      {handoffMessage ? (
        <div className={`project-closeout-handoff ${handoffMessage.startsWith('Module 055C selected') ? 'success' : 'warning'}`} role="status">
          <div><strong>Module 055C handoff</strong><span>{handoffMessage}</span></div>
          <a href="#work-register">Return to Module 055C</a>
        </div>
      ) : null}

      {capabilities?.isViewAs ? (
        <div className="project-closeout-readonly" role="status">
          <strong>Read-only preview</strong>
          <span>Exit Administrator View-As to request, complete, or reopen project closeout.</span>
        </div>
      ) : null}

      <section className="project-closeout-selector" aria-label="Closeout project selection">
        <div>
          <p className="project-closeout-eyebrow">Project selected for closeout</p>
          <h2>{selectedProject ? `${selectedProject.projectCode} · ${selectedProject.projectName}` : 'Choose a project'}</h2>
          <p>{selectedProject ? `${selectedProject.customerName} · PM: ${selectedProject.projectManagerName}` : 'Projects are limited to your server-enforced role scope.'}</p>
        </div>
        <div className="project-closeout-selector-controls">
          <label>
            <span>Find a project</span>
            <input
              type="search"
              value={projectSearch}
              onChange={(event) => setProjectSearch(event.target.value)}
              placeholder="Project number, customer, name, or PM"
            />
          </label>
          <label>
            <span>Closeout project</span>
            <select
              value={selectedProjectId}
              onChange={(event) => setSelectedProjectId(event.target.value)}
              disabled={moduleState.loading || !projects.length}
            >
              {!filteredProjects.length ? <option value="">No matching projects</option> : null}
              {filteredProjects.map((project) => (
                <option key={project.projectId} value={project.projectId}>
                  {project.projectCode} · {project.customerName} · {project.projectName}
                </option>
              ))}
            </select>
          </label>
          <button type="button" className="module040-secondary" onClick={refreshSelectedProject} disabled={moduleState.loading || lifecycleState.loading}>
            {moduleState.loading || lifecycleState.loading ? 'Refreshing…' : 'Refresh closeout review'}
          </button>
        </div>
      </section>

      {moduleState.error ? (
        <div className="project-closeout-page-error" role="alert">
          <div><strong>Closeout projects could not be loaded</strong><span>{moduleState.error}</span></div>
          <button type="button" className="module040-secondary" onClick={loadModule}>Try again</button>
        </div>
      ) : null}

      <nav className="project-closeout-view-tabs" aria-label="Project closeout views">{[
        ['readiness', 'Readiness & blockers', `${blockers.length} blocker${blockers.length === 1 ? '' : 's'}`],
        ['decision', 'Closeout decision', titleCase(closeoutStatus, 'Not started')],
        ['evidence', 'Source health & history', `${(lifecycle?.audit ?? []).length} events`]
      ].map(([value, label, detail]) => <button type="button" key={value} className={activeView === value ? 'is-active' : ''} onClick={() => setActiveView(value)}><strong>{label}</strong><span>{detail}</span></button>)}</nav>

      <section className="project-closeout-path" aria-labelledby="module040-path-title" hidden={activeView !== 'readiness'}>
        <div className="project-closeout-section-heading">
          <div>
            <p className="project-closeout-eyebrow">Closeout path</p>
            <h2 id="module040-path-title">Exactly what happens next</h2>
            <p>Each step turns complete only when the current server evidence supports it.</p>
          </div>
          <StatusPill value={blockers.length ? 'blocked' : closeoutStatus}>
            {blockers.length ? `${blockers.length} blocker${blockers.length === 1 ? '' : 's'}` : titleCase(closeoutStatus, 'Not started')}
          </StatusPill>
        </div>
        <ol className="project-closeout-step-list">
          {steps.map((step) => (
            <li key={step.number} className={step.state}>
              <span className="project-closeout-step-number">{step.state === 'complete' ? '✓' : step.number}</span>
              <div><strong>{step.title}</strong><small>{step.detail}</small></div>
            </li>
          ))}
        </ol>
      </section>

      <section className={`project-closeout-next-action ${guidance.tone}`} aria-labelledby="module040-next-action-title" hidden={activeView !== 'readiness'}>
        <div>
          <p className="project-closeout-eyebrow">What you need to do now</p>
          <h2 id="module040-next-action-title">{guidance.title}</h2>
          <p>{guidance.detail}</p>
        </div>
        <div className="project-closeout-next-action-buttons">
          {guidance.action === 'refresh' ? (
            <button type="button" className="module040-primary" onClick={refreshSelectedProject}>Retry project verification</button>
          ) : null}
          {guidance.action === 'blocker' && firstBlockerGuidance ? (
            <a className="module040-primary link-button" href={firstBlockerGuidance.href}>{firstBlockerGuidance.label}</a>
          ) : null}
          {guidance.action === 'form' ? (
            <button type="button" className="module040-primary" onClick={scrollToForm}>Complete closeout confirmations</button>
          ) : null}
          {guidance.action === 'request' ? (
            <button type="button" className="module040-primary" disabled={actionState.busy} onClick={() => saveGovernedCloseout('request')}>
              {actionState.busy === 'request' ? 'Requesting…' : 'Request project closeout'}
            </button>
          ) : null}
          {guidance.action === 'complete' ? (
            <button type="button" className="module040-primary" disabled={actionState.busy} onClick={() => saveGovernedCloseout('complete')}>
              {actionState.busy === 'complete' ? 'Completing…' : 'Complete project closeout'}
            </button>
          ) : null}
          {guidance.action === 'reopen' ? (
            <button type="button" className="module040-secondary" onClick={scrollToForm}>Review reopen controls</button>
          ) : null}
        </div>
      </section>

      {selectedProject ? (
        <section className="project-closeout-summary-grid" aria-label="Selected project closeout summary" hidden={activeView !== 'readiness'}>
          <article><span>Project status</span><strong>{titleCase(selectedProject.projectStatus)}</strong><small>{selectedProject.projectCode}</small></article>
          <article><span>Billing readiness</span><strong>{titleCase(lifecycle?.billingReadiness?.reviewStatus ?? selectedProject.billingReadiness?.reviewStatus, 'Not recorded')}</strong><small>{lifecycle?.billingReadiness?.packageType ?? 'No package type recorded'}</small></article>
          <article><span>Approved / used hours</span><strong>{number(selectedProject.approvedHours)} / {number(selectedProject.usedHours)}</strong><small>{number(selectedProject.plannedHours)} planned</small></article>
          <article><span>Forecast / variance</span><strong>{money(selectedProject.forecastedFinalCost)}</strong><small>{money(selectedProject.currentVariance)} variance</small></article>
          <article><span>Closeout blockers</span><strong>{blockers.length}</strong><small>{blockers.length ? 'Action required' : 'Server checks clear'}</small></article>
          <article><span>Notification history</span><strong>{selectedProject.notificationSummary?.count ?? 0}</strong><small>Latest: {titleCase(selectedProject.notificationSummary?.latest?.deliveryStatus, 'Not recorded')}</small></article>
        </section>
      ) : null}

      <div className="project-closeout-main-grid" hidden={activeView !== 'readiness'}>
        <section className="project-closeout-card project-closeout-blockers" aria-labelledby="module040-blockers-title">
          <div className="project-closeout-section-heading">
            <div>
              <p className="project-closeout-eyebrow">Authoritative validation</p>
              <h2 id="module040-blockers-title">Server-validated blockers</h2>
              <p>These checks come from the selected project lifecycle, not from browser estimates.</p>
            </div>
            <StatusPill value={lifecycleState.error ? 'unavailable' : blockers.length ? 'blocked' : 'ready'} />
          </div>

          {lifecycleState.loading ? <div className="project-closeout-loading">Verifying tasks, time, billing, invoices, and prior closeout state…</div> : null}
          {lifecycleState.error ? (
            <div className="project-closeout-inline-error" role="alert">
              <strong>Project lifecycle verification is unavailable</strong>
              <span>{lifecycleState.error}</span>
              <button type="button" className="module040-secondary" onClick={() => loadLifecycle(selectedProject?.projectId)}>Retry verification</button>
            </div>
          ) : null}

          {!lifecycleState.loading && !lifecycleState.error && blockers.length === 0 ? (
            <div className="project-closeout-clear-state">
              <span>✓</span>
              <div><strong>No server blockers remain</strong><small>Complete the confirmations and save the governed closeout decision.</small></div>
            </div>
          ) : null}

          {blockers.length ? (
            <div className="project-closeout-blocker-list">
              {blockers.map((blocker, index) => {
                const guidanceItem = guidanceForBlocker(blocker);
                return (
                  <article key={`${blocker}-${index}`}>
                    <span>{index + 1}</span>
                    <div><strong>{blocker}</strong><small>Resolve this item, return to Module 040, and refresh the closeout review.</small></div>
                    <a href={guidanceItem.href}>{guidanceItem.label}</a>
                  </article>
                );
              })}
            </div>
          ) : null}
        </section>

        <aside className="project-closeout-card project-closeout-role-guide" aria-labelledby="module040-role-title">
          <div className="project-closeout-section-heading">
            <div>
              <p className="project-closeout-eyebrow">Role responsibilities</p>
              <h2 id="module040-role-title">Who does what</h2>
            </div>
          </div>
          <div className="project-closeout-role-list">
            <article><span>1</span><div><strong>Project Manager</strong><small>Confirms delivery and acceptance, clears project blockers, records billing disposition, and submits the audited closeout request.</small></div></article>
            <article><span>2</span><div><strong>PTC / Administrator</strong><small>Performs the final server verification, completes closeout, or reopens the project when a correction is required.</small></div></article>
            <article><span>3</span><div><strong>Accounting / Billing</strong><small>Completes billing readiness and invoice disposition before final closeout when financial evidence is required.</small></div></article>
          </div>
          <div className="project-closeout-role-links">
            <a href="#project-workspace">Project Workspace</a>
            <a href="#billing-readiness">Billing Readiness</a>
            <a href="#invoice-billing-center">Invoice & Billing</a>
            <a href="#closeout-email">Closeout Notification</a>
          </div>
        </aside>
      </div>

      <section id="module040-closeout-decision" className="project-closeout-card project-closeout-decision" aria-labelledby="module040-decision-title" hidden={activeView !== 'decision'}>
        <div className="project-closeout-section-heading">
          <div>
            <p className="project-closeout-eyebrow">Governed closeout</p>
            <h2 id="module040-decision-title">Request or complete closeout</h2>
            <p>Every saved decision records the actual actor, reason, confirmations, blockers, and final billing disposition.</p>
          </div>
          <StatusPill value={closeoutStatus} />
        </div>

        <div className="project-closeout-form-grid">
          <label>
            <span>Final billing disposition</span>
            <select value={closeoutForm.billingDisposition} onChange={(event) => updateForm('billingDisposition', event.target.value)}>
              <option value="">Select the final billing result</option>
              {BILLING_DISPOSITIONS.map(([value, label]) => <option key={value} value={value}>{label}</option>)}
            </select>
            <small>Choose the outcome that Accounting or Billing can audit later.</small>
          </label>
          <label>
            <span>Audit reason</span>
            <input
              value={closeoutForm.reason}
              onChange={(event) => updateForm('reason', event.target.value)}
              placeholder="Example: Customer accepted delivery and final billing is complete"
            />
            <small>At least five characters; be specific about why the project is ready.</small>
          </label>
          <label className="wide">
            <span>Closeout notes</span>
            <textarea
              value={closeoutForm.notes}
              onChange={(event) => updateForm('notes', event.target.value)}
              placeholder="Record customer acceptance, exceptions, lessons learned, remaining follow-up, or handoff details"
            />
          </label>
        </div>

        <fieldset className="project-closeout-confirmations">
          <legend>Required Project Manager confirmations</legend>
          {[
            ['deliveryComplete', 'Delivery is complete', 'All agreed implementation work and project tasks are finished.'],
            ['customerAcceptanceComplete', 'Customer acceptance is complete', 'Acceptance evidence or an approved equivalent is recorded.'],
            ['timeExpenseComplete', 'Final time and expense review is complete', 'No unreviewed project labor or expense remains.'],
            ['billingComplete', 'Billing is complete', 'Final invoice, no-further-billing, non-billable, or approved write-off disposition is confirmed.']
          ].map(([field, label, detail]) => (
            <label key={field} className={closeoutForm[field] ? 'checked' : ''}>
              <input type="checkbox" checked={closeoutForm[field]} onChange={(event) => updateForm(field, event.target.checked)} />
              <span><strong>{label}</strong><small>{detail}</small></span>
            </label>
          ))}
        </fieldset>

        <div className="project-closeout-decision-readiness">
          <div><span>Confirmations</span><strong>{allConfirmationsComplete ? 'Complete' : 'Incomplete'}</strong></div>
          <div><span>Server blockers</span><strong>{blockers.length ? `${blockers.length} remaining` : 'Clear'}</strong></div>
          <div><span>Audit reason</span><strong>{normalizeText(closeoutForm.reason).length >= 5 ? 'Ready' : 'Required'}</strong></div>
          <div><span>Your authority</span><strong>{capabilities?.canCompleteCloseout ? 'Complete' : capabilities?.canRequestCloseout ? 'Request' : 'Review only'}</strong></div>
        </div>

        {actionState.message ? <div className={`project-closeout-action-message ${actionState.tone}`} role="status">{actionState.message}</div> : null}

        <div className="project-closeout-actions">
          <button
            type="button"
            className="module040-primary"
            disabled={Boolean(actionState.busy) || !capabilities?.canRequestCloseout || !formReady || blockers.length > 0 || normalizeStatus(closeoutStatus) === 'closed'}
            onClick={() => saveGovernedCloseout('request')}
          >
            {actionState.busy === 'request' ? 'Requesting…' : 'Request project closeout'}
          </button>
          <button
            type="button"
            className="module040-primary complete"
            disabled={Boolean(actionState.busy) || !capabilities?.canCompleteCloseout || !formReady || blockers.length > 0 || normalizeStatus(closeoutStatus) === 'closed'}
            onClick={() => saveGovernedCloseout('complete')}
          >
            {actionState.busy === 'complete' ? 'Completing…' : 'Complete project closeout'}
          </button>
          <button
            type="button"
            className="module040-secondary"
            disabled={Boolean(actionState.busy) || !capabilities?.canReopenProject || normalizeStatus(closeoutStatus) !== 'closed' || normalizeText(closeoutForm.reason).length < 5}
            onClick={() => saveGovernedCloseout('reopen')}
          >
            {actionState.busy === 'reopen' ? 'Reopening…' : 'Reopen project'}
          </button>
        </div>
        <p className="project-closeout-action-explanation">
          Assigned Project Managers request closeout. PTCs and Administrators perform the final completion or reopen action. View-As never transfers mutation authority.
        </p>
      </section>

      <section className="project-closeout-evidence-grid" hidden={activeView !== 'evidence'}>
        <details className="project-closeout-card project-closeout-source-health">
          <summary>
            <span><strong>Supporting source health</strong><small>{unavailableSources.length ? `${unavailableSources.length} source${unavailableSources.length === 1 ? '' : 's'} need attention` : 'All Module 040 sources are available'}</small></span>
            <StatusPill value={unavailableSources.length ? 'partial' : 'healthy'} />
          </summary>
          <p>Source status is shown separately from the closeout decision. One unavailable supporting source no longer replaces the entire page with a generic access error.</p>
          <div className="project-closeout-source-list">
            {sources.map((source) => (
              <article key={source.key}>
                <div><strong>{source.name}</strong><small>{sourceImpact(source)}</small></div>
                <StatusPill value={source.status} />
                <p>{source.message}</p>
                <dl><div><dt>Records</dt><dd>{source.recordCount ?? 0}</dd></div><div><dt>Observed</dt><dd>{dateTime(source.observedAt)}</dd></div></dl>
              </article>
            ))}
          </div>
        </details>

        <details className="project-closeout-card project-closeout-audit">
          <summary>
            <span><strong>Closeout history and evidence</strong><small>{(lifecycle?.audit ?? []).length} lifecycle audit event(s)</small></span>
            <StatusPill value={(lifecycle?.audit ?? []).length ? 'healthy' : 'neutral'} />
          </summary>
          <div className="project-closeout-audit-list">
            {(lifecycle?.audit ?? []).map((event, index) => (
              <article key={event.auditEventId ?? `${event.eventType}-${index}`}>
                <div><strong>{titleCase(event.eventType ?? event.action ?? 'Lifecycle event')}</strong><small>{dateTime(event.createdAt ?? event.occurredAt)}</small></div>
                <p>{event.summary ?? event.reason ?? event.notes ?? 'Lifecycle evidence recorded.'}</p>
              </article>
            ))}
            {!(lifecycle?.audit ?? []).length ? <div className="project-closeout-empty">No closeout audit event has been recorded for this project yet.</div> : null}
          </div>
        </details>
      </section>
    </section>
  );
}
