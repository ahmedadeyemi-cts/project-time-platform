import { useEffect, useMemo, useState } from 'react';
import usSignalLogoUrl from '../brand/ussignal.png';
import IdentityAvatar from './identity/IdentityAvatar.jsx';
import useIdentityProfile from './identity/useIdentityProfile.js';
import './project-flowhive-center.css';
import './project-flowhive-ai-confidence.css';
import './projectpulse-module-standard.css';

const views = [
  { id: 'portfolio', label: 'Portfolio' },
  { id: 'planner', label: 'Planner' },
  { id: 'timeline', label: 'Timeline & risk' },
  { id: 'ai', label: 'AI draft studio' },
  { id: 'exports', label: 'Branded exports' },
  { id: 'governance', label: 'Governance' }
];

function storedSession() {
  try {
    const raw = window.localStorage.getItem('projectPulseAuthSession');
    if (!raw) return null;
    const session = JSON.parse(raw);
    if (!session?.sessionToken) return null;
    if (session.expiresAt && Date.now() >= Date.parse(session.expiresAt)) return null;
    return session;
  } catch {
    return null;
  }
}

function authenticationHeaders(extra = {}) {
  const session = storedSession();
  return {
    ...(session?.sessionToken
      ? {
          Authorization: `Bearer ${session.sessionToken}`,
          'X-ProjectPulse-Session': session.sessionToken
        }
      : {}),
    ...extra
  };
}

async function parseResponse(response, path) {
  const contentType = response.headers.get('content-type') || '';
  if (!contentType.includes('application/json')) {
    if (!response.ok) throw new Error(`${path} returned HTTP ${response.status}`);
    return response;
  }
  const body = await response.json();
  if (!response.ok) {
    throw new Error(body.message || body.detail || `${path} returned HTTP ${response.status}`);
  }
  return body;
}

async function getJson(path) {
  return parseResponse(await fetch(path, { headers: authenticationHeaders() }), path);
}

async function postJson(path, body) {
  return parseResponse(await fetch(path, {
    method: 'POST',
    headers: authenticationHeaders({ 'Content-Type': 'application/json' }),
    body: JSON.stringify(body)
  }), path);
}

function formatDate(value) {
  if (!value) return 'Not scheduled';
  const date = new Date(`${value}T00:00:00`);
  if (Number.isNaN(date.getTime())) return value;
  return date.toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' });
}

function formatHours(value) {
  return Number(value ?? 0).toLocaleString(undefined, {
    minimumFractionDigits: 0,
    maximumFractionDigits: 2
  });
}

function formatPercent(value) {
  const number = Number(value);
  return Number.isFinite(number) ? `${Math.round(number * 100)}%` : 'Not recorded';
}

function labelFrom(value) {
  return String(value ?? '')
    .replaceAll('_', ' ')
    .replace(/\b\w/g, (letter) => letter.toUpperCase());
}

function statusTone(status) {
  const normalized = String(status ?? '').toLowerCase();
  if (['active', 'available', 'ready', 'production_ready', 'module_064_routed'].includes(normalized)) return 'ready';
  if (['locked', 'blocked', 'error'].includes(normalized)) return 'blocked';
  return 'planned';
}

function EmptyState({ children }) {
  return <div className="flowhive-empty-state">{children}</div>;
}

function currentIsoDate() {
  return new Date().toISOString().slice(0, 10);
}

function buildLocalDraft(project, tasks, assignments) {
  if (!project) return null;
  const projectTasks = tasks.filter((task) => task.projectId === project.projectId);
  const planTasks = projectTasks.map((task, index) => ({
    clientTaskId: task.taskId,
    canonicalTaskId: task.taskId,
    wbsNumber: String(index + 1),
    parentWbsNumber: '',
    name: task.taskName,
    description: task.taskDescription || '',
    durationWorkingDays: Math.max(1, Math.ceil(Number(task.assignedHours || task.remainingHours || 8) / 8)),
    isMilestone: false,
    constraintType: 'ASAP',
    constraintDate: null,
    percentComplete: task.assignedHours
      ? Math.min(100, Math.round((Number(task.usedHours || 0) / Number(task.assignedHours)) * 100))
      : 0,
    remainingEffortHours: Number(task.remainingHours || 0),
    status: Number(task.remainingHours || 0) <= 0 && Number(task.usedHours || 0) > 0 ? 'complete' : 'not_started'
  }));
  const wbsByTaskId = new Map(projectTasks.map((task, index) => [task.taskId, String(index + 1)]));
  const planAssignments = assignments
    .filter((assignment) => assignment.projectId === project.projectId && assignment.resourceUserId)
    .map((assignment) => ({
      taskWbs: wbsByTaskId.get(assignment.taskId) || planTasks[0]?.wbsNumber || '',
      resourceUserId: assignment.resourceUserId,
      resourceDisplayName: assignment.resourceName,
      allocationPercent: Number(assignment.allocationPercent || 100),
      plannedHours: Number(assignment.assignedHours || 0)
    }))
    .filter((assignment) => assignment.taskWbs);

  return {
    projectId: project.projectId,
    projectCode: project.projectCode,
    projectName: project.projectName,
    customerName: project.customerName,
    planName: `${project.projectCode} governed plan`,
    revisionLabel: 'Local draft 1',
    projectStartDate: project.startDate || currentIsoDate(),
    tasks: planTasks.length ? planTasks : [{
      clientTaskId: crypto.randomUUID(),
      canonicalTaskId: null,
      wbsNumber: '1',
      parentWbsNumber: '',
      name: 'Project kickoff',
      description: '',
      durationWorkingDays: 1,
      isMilestone: false,
      constraintType: 'ASAP',
      constraintDate: null,
      percentComplete: 0,
      remainingEffortHours: 8,
      status: 'not_started'
    }],
    dependencies: planTasks.slice(1).map((task, index) => ({
      predecessorWbs: planTasks[index].wbsNumber,
      successorWbs: task.wbsNumber,
      type: 'FS',
      lagWorkingDays: 0
    })),
    assignments: planAssignments,
    gsdVersion: '',
    sowVersion: '',
    notes: ''
  };
}

function identityKey(profile) {
  return profile?.userId || profile?.effectiveUserId || profile?.id || '';
}

export default function ProjectFlowHiveCenter() {
  const [activeView, setActiveView] = useState('portfolio');
  const [capabilityResponse, setCapabilityResponse] = useState(null);
  const [readiness, setReadiness] = useState(null);
  const [artifactReadiness, setArtifactReadiness] = useState(null);
  const [portfolio, setPortfolio] = useState(null);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState('');
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');
  const [search, setSearch] = useState('');
  const [customer, setCustomer] = useState('all');
  const [projectStatus, setProjectStatus] = useState('all');
  const [selectedProjectId, setSelectedProjectId] = useState('');
  const [draftPlan, setDraftPlan] = useState(null);
  const [schedule, setSchedule] = useState(null);
  const [validation, setValidation] = useState(null);
  const [aiPreview, setAiPreview] = useState(null);
  const [savedPlans, setSavedPlans] = useState([]);
  const [baselineNote, setBaselineNote] = useState('Reviewed with project delivery and engineering stakeholders.');
  const [gsdExcerpt, setGsdExcerpt] = useState('');
  const [sowExcerpt, setSowExcerpt] = useState('');
  const [requestedOutcome, setRequestedOutcome] = useState('Create a reviewable implementation plan with detailed tasks, dependencies, risks, assumptions, milestones, acceptance, operational handoff, and closeout.');
  const { profile: identityProfile } = useIdentityProfile({ refreshSeconds: 90 });

  async function loadModule() {
    setLoading(true);
    setError('');
    try {
      const [capabilities, portfolioResult, readinessResult, artifactResult, plansResult] = await Promise.all([
        getJson('/api/project-flowhive/capabilities'),
        getJson('/api/project-flowhive/portfolio'),
        getJson('/api/project-flowhive/readiness'),
        getJson('/api/project-flowhive/artifacts/readiness'),
        getJson('/api/project-flowhive/plans')
      ]);
      setCapabilityResponse(capabilities);
      setPortfolio(portfolioResult);
      setReadiness(readinessResult);
      setArtifactReadiness(artifactResult);
      setSavedPlans(plansResult.plans || []);
      setSelectedProjectId((current) => current || portfolioResult.projects?.[0]?.projectId || '');
    } catch (loadError) {
      setError(loadError.message || 'Project FlowHive could not be loaded.');
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    loadModule();
  }, []);

  const projects = portfolio?.projects ?? [];
  const tasks = portfolio?.tasks ?? [];
  const assignments = portfolio?.assignments ?? [];
  const capabilities = capabilityResponse?.capabilities ?? [];
  const selectedProject = projects.find((project) => project.projectId === selectedProjectId) || null;

  const identityOptions = useMemo(() => {
    const values = new Map();
    assignments.forEach((assignment) => {
      if (!assignment.resourceUserId) return;
      values.set(assignment.resourceUserId, {
        userId: assignment.resourceUserId,
        displayName: assignment.resourceName,
        email: assignment.resourceEmail || ''
      });
    });
    const currentId = identityKey(identityProfile);
    if (currentId) {
      values.set(currentId, {
        userId: currentId,
        displayName: identityProfile.displayName || identityProfile.email || 'Current identity',
        email: identityProfile.email || ''
      });
    }
    return [...values.values()].sort((left, right) => left.displayName.localeCompare(right.displayName));
  }, [assignments, identityProfile]);

  const customerOptions = useMemo(() => [...new Set(projects.map((project) => project.customerName).filter(Boolean))]
    .sort((left, right) => left.localeCompare(right)), [projects]);
  const statusOptions = useMemo(() => [...new Set(projects.map((project) => project.status).filter(Boolean))]
    .sort((left, right) => left.localeCompare(right)), [projects]);

  const filteredProjects = useMemo(() => {
    const query = search.trim().toLowerCase();
    return projects.filter((project) => {
      if (customer !== 'all' && project.customerName !== customer) return false;
      if (projectStatus !== 'all' && project.status !== projectStatus) return false;
      if (!query) return true;
      return [project.projectCode, project.projectName, project.customerName, project.projectManagerName, project.status]
        .some((value) => String(value ?? '').toLowerCase().includes(query));
    });
  }, [customer, projectStatus, projects, search]);

  function createLocalDraft() {
    if (!selectedProject) return;
    setDraftPlan(buildLocalDraft(selectedProject, tasks, assignments));
    setSchedule(null);
    setValidation(null);
    setAiPreview(null);
    setNotice('A new FlowHive draft is ready. Save it to create the first immutable version.');
    setActiveView('planner');
  }

  function updatePlan(field, value) {
    setDraftPlan((current) => current ? { ...current, [field]: value } : current);
    setSchedule(null);
  }

  function updateTask(index, field, value) {
    setDraftPlan((current) => {
      if (!current) return current;
      const nextTasks = current.tasks.map((task, taskIndex) => taskIndex === index
        ? { ...task, [field]: value }
        : task);
      return { ...current, tasks: nextTasks };
    });
    setSchedule(null);
  }

  function updateDependencyForTask(index, field, value) {
    if (!draftPlan || index === 0) return;
    const successorWbs = draftPlan.tasks[index].wbsNumber;
    setDraftPlan((current) => {
      const existing = current.dependencies.find((dependency) => dependency.successorWbs === successorWbs);
      const next = existing
        ? current.dependencies.map((dependency) => dependency.successorWbs === successorWbs
            ? { ...dependency, [field]: value }
            : dependency)
        : [...current.dependencies, {
            predecessorWbs: current.tasks[index - 1]?.wbsNumber || '',
            successorWbs,
            type: 'FS',
            lagWorkingDays: 0,
            [field]: value
          }];
      return { ...current, dependencies: next };
    });
    setSchedule(null);
  }

  function updateTaskResource(taskWbs, resourceUserId) {
    const identity = identityOptions.find((option) => option.userId === resourceUserId);
    setDraftPlan((current) => {
      if (!current) return current;
      const withoutTask = current.assignments.filter((assignment) => assignment.taskWbs !== taskWbs);
      return {
        ...current,
        assignments: resourceUserId
          ? [...withoutTask, {
              taskWbs,
              resourceUserId,
              resourceDisplayName: identity?.displayName || '',
              allocationPercent: 100,
              plannedHours: Number(current.tasks.find((task) => task.wbsNumber === taskWbs)?.remainingEffortHours || 0)
            }]
          : withoutTask
      };
    });
  }

  function addTask() {
    setDraftPlan((current) => {
      if (!current) return current;
      const wbsNumber = String(current.tasks.length + 1);
      return {
        ...current,
        tasks: [...current.tasks, {
          clientTaskId: crypto.randomUUID(),
          canonicalTaskId: null,
          wbsNumber,
          parentWbsNumber: '',
          name: 'New planning task',
          description: '',
          durationWorkingDays: 1,
          isMilestone: false,
          constraintType: 'ASAP',
          constraintDate: null,
          percentComplete: 0,
          remainingEffortHours: 8,
          status: 'not_started'
        }],
        dependencies: current.tasks.length
          ? [...current.dependencies, {
              predecessorWbs: current.tasks.at(-1).wbsNumber,
              successorWbs: wbsNumber,
              type: 'FS',
              lagWorkingDays: 0
            }]
          : current.dependencies
      };
    });
  }

  async function validatePlan() {
    if (!draftPlan) return;
    setBusy('validate');
    setError('');
    try {
      const result = await postJson('/api/project-flowhive/planning/validate', draftPlan);
      setValidation(result);
      setNotice(result.valid ? 'Plan contract is valid. Nothing was persisted.' : 'Plan validation found issues.');
    } catch (actionError) {
      setError(actionError.message);
    } finally {
      setBusy('');
    }
  }

  async function calculateSchedule() {
    if (!draftPlan) return;
    setBusy('schedule');
    setError('');
    try {
      const result = await postJson('/api/project-flowhive/schedule/calculate', draftPlan);
      setSchedule(result);
      setValidation({ valid: true, issues: result.issues || [] });
      setNotice('Weekday schedule preview calculated. Module 057 holiday authority is not applied.');
      setActiveView('timeline');
    } catch (actionError) {
      setError(actionError.message);
    } finally {
      setBusy('');
    }
  }

  async function saveDraft() {
    if (!draftPlan) return;
    setBusy('save');
    setError('');
    try {
      const result = await postJson('/api/project-flowhive/plans/drafts', draftPlan);
      setDraftPlan((current) => current ? { ...current, planId: result.planId } : current);
      setNotice(`FlowHive draft version ${result.version} was saved with immutable schedule and validation evidence.`);
      const plansResult = await getJson('/api/project-flowhive/plans');
      setSavedPlans(plansResult.plans || []);
    } catch (actionError) {
      setError(actionError.message);
    } finally {
      setBusy('');
    }
  }

  async function establishBaseline() {
    if (!draftPlan?.planId) return;
    const current = savedPlans.find((plan) => plan.planId === draftPlan.planId);
    setBusy('baseline');
    setError('');
    try {
      const result = await postJson(`/api/project-flowhive/plans/${draftPlan.planId}/baseline`, {
        approvalNote: baselineNote,
        expectedVersion: current?.currentVersion || null
      });
      setNotice(`FlowHive version ${result.version} is now the reviewer-approved baseline.`);
      const plansResult = await getJson('/api/project-flowhive/plans');
      setSavedPlans(plansResult.plans || []);
    } catch (actionError) {
      setError(actionError.message);
    } finally {
      setBusy('');
    }
  }

  async function loadSavedPlan(planId) {
    if (!planId) return;
    setBusy('load-plan');
    setError('');
    try {
      const result = await getJson(`/api/project-flowhive/plans/${planId}`);
      setDraftPlan(result.plan);
      setSchedule(result.schedule);
      setValidation(result.validation);
      setSelectedProjectId(result.summary.projectId);
      setNotice(`Loaded immutable FlowHive version ${result.summary.currentVersion}.`);
      setActiveView('planner');
    } catch (actionError) {
      setError(actionError.message);
    } finally {
      setBusy('');
    }
  }

  async function previewAiRequest() {
    if (!draftPlan) return;
    setBusy('ai');
    setError('');
    try {
      const result = await postJson('/api/project-flowhive/ai/production-generate', {
        plan: draftPlan,
        gsdExcerpt,
        sowExcerpt,
        requestedOutcome,
        detailLevel: 'comprehensive',
        diagramType: 'flowchart',
        allowSanitizedExternalFallback: true
      });
      setAiPreview(result);
      if (result.plan) {
        setDraftPlan({
          ...result.plan,
          planId: draftPlan.planId || null,
          sourceKind: 'celar_ai',
          celarAiProviderCode: result.executionPath || '',
          celarAiCorrelationId: result.correlationId || '',
          celarAiConfidence: result.confidence ?? null
        });
      }
      if (result.schedule?.valid) setSchedule(result.schedule);
      setValidation(result.validation || null);
      setNotice(result.schedule?.valid
        ? 'Celar AI produced and auto-filled a detailed private review plan. Save it to create an immutable version.'
        : 'Celar AI produced a review draft that requires correction before it can be saved.');
    } catch (actionError) {
      setError(actionError.message);
    } finally {
      setBusy('');
    }
  }

  async function downloadArtifact(format) {
    if (!draftPlan) return;
    setBusy(format);
    setError('');
    const path = `/api/project-flowhive/artifacts/${format}-preview`;
    try {
      const response = await parseResponse(await fetch(path, {
        method: 'POST',
        headers: authenticationHeaders({ 'Content-Type': 'application/json' }),
        body: JSON.stringify({
          plan: draftPlan,
          artifactTitle: `${draftPlan.planName} — internal preview`,
          audience: 'internal',
          excludeNotes: true,
          acknowledgeInternalDraft: true
        })
      }), path);
      const blob = await response.blob();
      const url = URL.createObjectURL(blob);
      const anchor = document.createElement('a');
      anchor.href = url;
      anchor.download = `${draftPlan.projectCode || 'project-flowhive'}-internal-draft.${format === 'excel' ? 'xlsx' : 'pdf'}`;
      anchor.click();
      URL.revokeObjectURL(url);
      setNotice(`US Signal branded ${format === 'excel' ? 'Excel' : 'PDF'} internal draft generated. No external link was created.`);
    } catch (actionError) {
      setError(actionError.message);
    } finally {
      setBusy('');
    }
  }

  const timelineMaximum = Math.max(1, ...(schedule?.tasks || []).map((task) => task.earliestStartIndex + Math.max(1, task.durationWorkingDays)));

  return (
    <section
      className="project-flowhive-center projectpulse-module-standard"
      data-module="066"
      data-brand="us-signal"
      data-phase="066A.1-066E"
      data-mode="production"
    >
      <header className="flowhive-hero">
        <div className="flowhive-brand-lockup">
          <img src={usSignalLogoUrl} alt="US Signal" />
          <div>
            <p className="flowhive-eyebrow">Module 066 · Project planning command center</p>
            <h2>Project FlowHive</h2>
            <p>Production portfolio planning with immutable versions, deterministic schedules, Celar AI drafting, reviewer baselines, and governed artifacts.</p>
          </div>
        </div>
        <div className="flowhive-hero-actions">
          <div className="flowhive-user-chip">
            <IdentityAvatar profile={identityProfile} size="small" />
            <span>{identityProfile?.displayName || portfolio?.access?.displayName || 'ProjectPulse user'}</span>
          </div>
          <span className="flowhive-phase-badge">Production connected</span>
          <button type="button" onClick={loadModule} disabled={loading}>{loading ? 'Refreshing…' : 'Refresh'}</button>
        </div>
      </header>

      <aside className="flowhive-foundation-notice" aria-label="Governed production boundary">
        <strong>Project FlowHive is connected to Celar AI, Module 064 routing, and immutable production persistence.</strong>
        <span>Saving creates a separate governed plan version and never changes canonical tasks. Customer delivery still requires an explicit reviewed action.</span>
      </aside>

      {portfolio?.access ? (
        <div className="flowhive-access-banner">
          <div><span>Effective user</span><strong>{portfolio.access.displayName || portfolio.access.email}</strong></div>
          <div><span>Backend scope</span><strong>{labelFrom(portfolio.access.scope)}</strong></div>
          <div><span>View-As</span><strong>{portfolio.access.isViewAs ? 'Read-only preview' : 'Not active'}</strong></div>
          <div><span>Persistence</span><strong>{capabilityResponse?.databaseMutationEnabled ? 'Ready' : 'Unavailable'}</strong></div>
          <div><span>Customer links</span><strong>Disabled</strong></div>
        </div>
      ) : null}

      {error ? <div className="flowhive-error" role="alert"><strong>Project FlowHive needs attention.</strong><span>{error}</span></div> : null}
      {notice ? <div className="flowhive-notice" role="status"><span>{notice}</span><button type="button" onClick={() => setNotice('')}>Dismiss</button></div> : null}

      <nav className="flowhive-view-tabs" aria-label="Project FlowHive views">
        {views.map((view) => (
          <button type="button" key={view.id} aria-pressed={activeView === view.id} className={activeView === view.id ? 'active' : ''} onClick={() => setActiveView(view.id)}>
            {view.label}
          </button>
        ))}
      </nav>

      {activeView === 'portfolio' ? (
        <div className="flowhive-view-panel">
          <div className="flowhive-filter-bar">
            <label>Search<input type="search" value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Project, customer, manager, or status" /></label>
            <label>Customer<select value={customer} onChange={(event) => setCustomer(event.target.value)}><option value="all">All authorized customers</option>{customerOptions.map((value) => <option key={value} value={value}>{value}</option>)}</select></label>
            <label>Project status<select value={projectStatus} onChange={(event) => setProjectStatus(event.target.value)}><option value="all">All statuses</option>{statusOptions.map((value) => <option key={value} value={value}>{labelFrom(value)}</option>)}</select></label>
          </div>
          <div className="flowhive-summary-grid">
            <article><span>Authorized projects</span><strong>{portfolio?.summary?.projectCount ?? 0}</strong><small>{filteredProjects.length} match filters</small></article>
            <article><span>Visible tasks</span><strong>{portfolio?.summary?.taskCount ?? 0}</strong><small>Canonical task records</small></article>
            <article><span>Assignments</span><strong>{portfolio?.summary?.assignmentCount ?? 0}</strong><small>{formatHours(portfolio?.summary?.assignedHours)} assigned hours</small></article>
            <article><span>Controlled baselines</span><strong>{portfolio?.summary?.controlledBaselineCount ?? 0}</strong><small>Reviewer-approved versions</small></article>
          </div>
          {loading ? <EmptyState>Loading authorized portfolio…</EmptyState> : null}
          {!loading && !error && filteredProjects.length === 0 ? <EmptyState>No authorized projects match the filters.</EmptyState> : null}
          <div className="flowhive-project-grid">
            {filteredProjects.map((project) => (
              <article className={`flowhive-project-card ${selectedProjectId === project.projectId ? 'selected' : ''}`} key={project.projectId}>
                <div className="flowhive-project-card-heading"><div><span>{project.customerName}</span><h3>{project.projectCode} · {project.projectName}</h3></div><span className={`flowhive-status ${statusTone(project.status)}`}>{labelFrom(project.status)}</span></div>
                <dl><div><dt>Project Manager</dt><dd>{project.projectManagerName}</dd></div><div><dt>Current dates</dt><dd>{formatDate(project.startDate)} – {formatDate(project.endDate)}</dd></div><div><dt>Tasks</dt><dd>{project.taskCount}</dd></div><div><dt>Assignments</dt><dd>{project.assignmentCount}</dd></div></dl>
                <footer><button type="button" onClick={() => setSelectedProjectId(project.projectId)}>Select project</button><button type="button" className="primary" onClick={() => { setSelectedProjectId(project.projectId); setDraftPlan(buildLocalDraft(project, tasks, assignments)); setActiveView('planner'); }}>Open planner</button></footer>
              </article>
            ))}
          </div>
        </div>
      ) : null}

      {activeView === 'planner' ? (
        <div className="flowhive-view-panel">
          <div className="flowhive-planner-toolbar">
            <label>Canonical project<select value={selectedProjectId} onChange={(event) => { setSelectedProjectId(event.target.value); setDraftPlan(null); setSchedule(null); }}><option value="">Select a project</option>{projects.map((project) => <option key={project.projectId} value={project.projectId}>{project.projectCode} — {project.projectName}</option>)}</select></label>
            <button type="button" onClick={createLocalDraft} disabled={!selectedProject}>Create/reset draft</button>
            <button type="button" onClick={validatePlan} disabled={!draftPlan || busy}>Validate</button>
            <button type="button" className="primary" onClick={calculateSchedule} disabled={!draftPlan || busy}>{busy === 'schedule' ? 'Calculating…' : 'Calculate schedule'}</button>
            <button type="button" onClick={saveDraft} disabled={!draftPlan || busy || portfolio?.access?.isViewAs}>{busy === 'save' ? 'Saving…' : 'Save immutable version'}</button>
            <button type="button" onClick={establishBaseline} disabled={!draftPlan?.planId || busy || portfolio?.access?.isViewAs || baselineNote.trim().length < 10}>{busy === 'baseline' ? 'Approving…' : 'Establish reviewed baseline'}</button>
          </div>
          <div className="flowhive-plan-metadata">
            <label>Saved FlowHive plan<select value={draftPlan?.planId || ''} onChange={(event) => loadSavedPlan(event.target.value)}><option value="">New unsaved plan</option>{savedPlans.filter((plan) => !selectedProjectId || plan.projectId === selectedProjectId).map((plan) => <option key={plan.planId} value={plan.planId}>{plan.planName} · v{plan.currentVersion}{plan.baselineVersion ? ` · baseline v${plan.baselineVersion}` : ''}</option>)}</select></label>
            <label>Baseline review note<input value={baselineNote} onChange={(event) => setBaselineNote(event.target.value)} placeholder="Required reviewer decision note" /></label>
          </div>
          {!draftPlan ? <EmptyState>Select an authorized project and create or load a FlowHive draft.</EmptyState> : (
            <>
              <div className="flowhive-plan-metadata">
                <label>Plan name<input value={draftPlan.planName} onChange={(event) => updatePlan('planName', event.target.value)} /></label>
                <label>Revision<input value={draftPlan.revisionLabel} onChange={(event) => updatePlan('revisionLabel', event.target.value)} /></label>
                <label>Project start<input type="date" value={draftPlan.projectStartDate} onChange={(event) => updatePlan('projectStartDate', event.target.value)} /></label>
                <label>GSD version<input value={draftPlan.gsdVersion} onChange={(event) => updatePlan('gsdVersion', event.target.value)} placeholder="Approved GSD version" /></label>
                <label>SOW version<input value={draftPlan.sowVersion} onChange={(event) => updatePlan('sowVersion', event.target.value)} placeholder="Approved SOW version" /></label>
              </div>
              <div className="flowhive-table-heading"><div><h3>Controlled draft task grid</h3><p>Identity choices use Module 062-backed user IDs. Save creates an immutable FlowHive version without modifying canonical tasks.</p></div><button type="button" onClick={addTask}>Add planning task</button></div>
              <div className="flowhive-table-wrap">
                <table className="flowhive-task-table flowhive-planner-table">
                  <thead><tr><th>WBS</th><th>Task</th><th>Duration</th><th>Progress</th><th>Predecessor</th><th>Type</th><th>Lead/lag</th><th>Assigned identity</th></tr></thead>
                  <tbody>{draftPlan.tasks.map((task, index) => {
                    const dependency = draftPlan.dependencies.find((item) => item.successorWbs === task.wbsNumber);
                    const assignment = draftPlan.assignments.find((item) => item.taskWbs === task.wbsNumber);
                    return (
                      <tr key={task.clientTaskId || `${task.wbsNumber}-${index}`}>
                        <td><input aria-label={`WBS for ${task.name}`} value={task.wbsNumber} onChange={(event) => updateTask(index, 'wbsNumber', event.target.value)} /></td>
                        <td><input aria-label={`Task ${task.wbsNumber} name`} value={task.name} onChange={(event) => updateTask(index, 'name', event.target.value)} /></td>
                        <td><input type="number" min="0" max="730" value={task.durationWorkingDays} onChange={(event) => updateTask(index, 'durationWorkingDays', Number(event.target.value))} /></td>
                        <td><input type="number" min="0" max="100" value={task.percentComplete} onChange={(event) => updateTask(index, 'percentComplete', Number(event.target.value))} /></td>
                        <td>{index === 0 ? <span>Start</span> : <select value={dependency?.predecessorWbs || ''} onChange={(event) => updateDependencyForTask(index, 'predecessorWbs', event.target.value)}><option value="">None</option>{draftPlan.tasks.filter((_, otherIndex) => otherIndex !== index).map((option) => <option key={option.wbsNumber} value={option.wbsNumber}>{option.wbsNumber}</option>)}</select>}</td>
                        <td>{index === 0 ? '—' : <select value={dependency?.type || 'FS'} onChange={(event) => updateDependencyForTask(index, 'type', event.target.value)}>{['FS', 'SS', 'FF', 'SF'].map((type) => <option key={type}>{type}</option>)}</select>}</td>
                        <td>{index === 0 ? '—' : <input type="number" min="-365" max="365" value={dependency?.lagWorkingDays || 0} onChange={(event) => updateDependencyForTask(index, 'lagWorkingDays', Number(event.target.value))} />}</td>
                        <td><select value={assignment?.resourceUserId || ''} onChange={(event) => updateTaskResource(task.wbsNumber, event.target.value)}><option value="">Unassigned</option>{identityOptions.map((identity) => <option key={identity.userId} value={identity.userId}>{identity.displayName}{identity.email ? ` — ${identity.email}` : ''}</option>)}</select></td>
                      </tr>
                    );
                  })}</tbody>
                </table>
              </div>
              {validation ? <div className={`flowhive-validation ${validation.valid ? 'valid' : 'invalid'}`}><strong>{validation.valid ? 'Plan contract valid' : 'Plan contract needs correction'}</strong>{validation.issues?.length ? <ul>{validation.issues.map((issue, index) => <li key={`${issue.code}-${index}`}><code>{issue.path}</code> {issue.message}</li>)}</ul> : <span>No validation issues returned.</span>}</div> : null}
            </>
          )}
        </div>
      ) : null}

      {activeView === 'timeline' ? (
        <div className="flowhive-view-panel">
          <div className="flowhive-table-heading"><div><h3>Schedule, critical path, and float</h3><p>Deterministic weekday preview. Company holidays and individual calendars require Module 057 authority.</p></div>{schedule ? <span>{formatDate(schedule.projectStartDate)} – {formatDate(schedule.projectFinishDate)}</span> : null}</div>
          {!schedule ? <EmptyState>Calculate a valid local draft to create the timeline.</EmptyState> : (
            <>
              <div className="flowhive-summary-grid"><article><span>Scheduled working days</span><strong>{schedule.scheduledWorkingDays}</strong></article><article><span>Critical tasks</span><strong>{schedule.criticalTaskCount}</strong></article><article><span>Planned hours</span><strong>{formatHours(schedule.plannedHours)}</strong></article><article><span>Calendar authority</span><strong>Preview</strong><small>Module 057 not applied</small></article></div>
              <div className="flowhive-timeline" role="list" aria-label="Schedule preview">
                {schedule.tasks.map((task) => (
                  <article key={task.wbsNumber} className={task.isCritical ? 'critical' : ''} role="listitem">
                    <div className="flowhive-timeline-label"><strong>{task.wbsNumber} · {task.name}</strong><span>{formatDate(task.startDate)} – {formatDate(task.endDate)} · Float {task.totalFloatWorkingDays}d</span></div>
                    <div className="flowhive-timeline-track"><span style={{ marginLeft: `${(task.earliestStartIndex / timelineMaximum) * 100}%`, width: `${Math.max(2, (Math.max(1, task.durationWorkingDays) / timelineMaximum) * 100)}%` }} /></div>
                  </article>
                ))}
              </div>
            </>
          )}
        </div>
      ) : null}

      {activeView === 'ai' ? (
        <div className="flowhive-view-panel flowhive-ai-layout">
          <div className="flowhive-ai-copy">
            <h3>Celar AI governed Project FlowHive generation</h3>
            <p>Celar AI retrieves authorized private project evidence, auto-fills a detailed customer-ready delivery plan, validates the WBS and dependency network, and calculates the deterministic schedule.</p>
            <ol><li>The exact stored Module 064 order is followed for this capability.</li><li>Private SOW, GSD, design, task, and assignment evidence stays inside the governed boundary.</li><li>Any eligible external fallback receives only a backend-owned identity-free capsule.</li><li>The local governed target remains the mandatory final fallback.</li><li>Every output requires PM and Engineering review before baseline approval or customer delivery.</li></ol>
          </div>
          {!draftPlan ? <EmptyState>Create or load a plan draft first.</EmptyState> : <div className="flowhive-ai-form">
            <label>Requested outcome<textarea value={requestedOutcome} onChange={(event) => setRequestedOutcome(event.target.value)} rows={5} /></label>
            <label>Optional approved GSD excerpt<textarea value={gsdExcerpt} onChange={(event) => setGsdExcerpt(event.target.value)} placeholder="Optional private supplemental excerpt; indexed project documents are also searched." /></label>
            <label>Optional approved SOW excerpt<textarea value={sowExcerpt} onChange={(event) => setSowExcerpt(event.target.value)} placeholder="Optional private supplemental excerpt; raw document text is never sent to a public provider." /></label>
            <button type="button" className="primary" onClick={previewAiRequest} disabled={busy}>{busy === 'ai' ? 'Generating detailed Celar AI plan…' : 'Generate and auto-fill detailed plan'}</button>
            {aiPreview ? <section className="celar-flowhive-production-result">
              <header><div><span>Celar AI result</span><strong>{labelFrom(aiPreview.status)}</strong></div><div><span>Execution path</span><strong>{labelFrom(aiPreview.executionPath)}</strong></div></header>
              <div className="metrics"><div><span>Confidence</span><strong>{formatPercent(aiPreview.confidence)}</strong></div><div><span>Tasks</span><strong>{aiPreview.plan?.tasks?.length || 0}</strong></div><div><span>Working days</span><strong>{aiPreview.schedule?.scheduledWorkingDays ?? 'Not calculated'}</strong></div><div><span>Critical tasks</span><strong>{aiPreview.schedule?.criticalTaskCount ?? 'Not calculated'}</strong></div></div>
              <p>{aiPreview.confidenceExplanation}</p>
              {Number(aiPreview.confidence || 0) <= .35 && !aiPreview.citations?.length ? <aside className="flowhive-confidence-help"><strong>Why confidence is 35%</strong><span>This is the governed deterministic floor: Celar AI had no citation-ready private project evidence to interpret. Add approved SOW/GSD excerpts above or process and index the project documents in Module 011, then generate again. Confidence rises from evidence coverage and validation—not from changing the displayed score.</span></aside> : null}
              {aiPreview.plan?.tasks?.length ? <div className="tasks"><table><thead><tr><th>WBS</th><th>Task</th><th>Description</th><th>Duration</th><th>Status</th></tr></thead><tbody>{aiPreview.plan.tasks.map((task, index) => <tr key={task.clientTaskId || `${task.wbsNumber}-${index}`}><td><code>{task.wbsNumber}</code></td><td><strong>{task.name}</strong></td><td>{task.description}</td><td>{task.durationWorkingDays} day(s)</td><td>{labelFrom(task.status)}</td></tr>)}</tbody></table></div> : null}
              {aiPreview.citations?.length ? <details open><summary>Private source citations ({aiPreview.citations.length})</summary><ul>{aiPreview.citations.map((citation) => <li key={citation.citationId}><strong>[{citation.citationId}] {citation.originalFileName}</strong> · {citation.documentVersion} · {citation.citationAnchor}</li>)}</ul></details> : null}
              {aiPreview.missingEvidence?.length ? <details open><summary>Missing evidence</summary><ul>{aiPreview.missingEvidence.map((value, index) => <li key={index}>{value}</li>)}</ul></details> : null}
              {aiPreview.conflicts?.length ? <details open><summary>Conflicts</summary><ul>{aiPreview.conflicts.map((value, index) => <li key={index}>{value}</li>)}</ul></details> : null}
              {aiPreview.warnings?.length ? <details><summary>Warnings and review controls</summary><ul>{aiPreview.warnings.map((value, index) => <li key={index}>{value}</li>)}</ul></details> : null}
              <footer><span>Configured order: {aiPreview.providerOrder?.join(' → ')}</span><span>Correlation <code>{aiPreview.correlationId}</code></span></footer>
            </section> : null}
          </div>}
        </div>
      ) : null}

      {activeView === 'exports' ? (
        <div className="flowhive-view-panel">
          <div className="flowhive-export-hero"><img src={usSignalLogoUrl} alt="US Signal" /><div><h3>US Signal branded internal artifacts</h3><p>PDF and Excel source embeds the governed logo. Every artifact is watermarked as an internal draft and creates no customer link.</p><code>Logo SHA-256: {artifactReadiness?.branding?.sha256 || 'Loading governed checksum…'}</code></div></div>
          <div className="flowhive-export-grid"><article><h4>Project schedule PDF</h4><p>Branded summary, paginated task schedule, critical indicators, date range, and artifact-control footer.</p><button type="button" onClick={() => downloadArtifact('pdf')} disabled={!draftPlan || busy}>{busy === 'pdf' ? 'Generating…' : 'Download internal PDF draft'}</button></article><article><h4>Planning workbook</h4><p>Branded summary, schedule, dependencies, and artifact-control sheets with logo checksum evidence.</p><button type="button" onClick={() => downloadArtifact('excel')} disabled={!draftPlan || busy}>{busy === 'excel' ? 'Generating…' : 'Download internal Excel draft'}</button></article><article className="locked"><h4>Customer sharing link</h4><p>Expiration, customer isolation, delivery, and access auditing require a separately authorized external-sharing phase.</p><button type="button" disabled>Create customer link — locked</button></article></div>
        </div>
      ) : null}

      {activeView === 'governance' ? (
        <div className="flowhive-view-panel">
          <div className="flowhive-phase-grid">{(readiness?.phases || []).map((phase) => <article key={phase.phase}><span>{phase.phase}</span><h3>{phase.capability}</h3><p className={`flowhive-status ${statusTone(phase.status)}`}>{labelFrom(phase.status)}</p></article>)}</div>
          <div className="flowhive-capability-grid">{capabilities.map((capability) => <article key={capability.code}><div><span>{capability.priority}</span><span className={`flowhive-status ${statusTone(capability.status)}`}>{labelFrom(capability.status)}</span></div><h3>{labelFrom(capability.code)}</h3><p>{capability.evidence}</p></article>)}</div>
          <div className="flowhive-governance-checks"><h3>Protected boundaries</h3><ul><li>Canonical project, task, and assignment records remain read only from FlowHive.</li><li>Every saved draft is an immutable version with validation, schedule, actor, source, and Celar correlation evidence.</li><li>A baseline names an exact reviewed version and requires a reviewer decision note.</li><li>View-As cannot save or approve. External customer delivery remains a separate governed action.</li></ul></div>
        </div>
      ) : null}
    </section>
  );
}
