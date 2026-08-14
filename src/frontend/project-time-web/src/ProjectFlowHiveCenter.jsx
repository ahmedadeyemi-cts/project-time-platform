import { Fragment, useEffect, useMemo, useState } from 'react';
import usSignalLogoUrl from '../brand/ussignal.png';
import IdentityAvatar from './identity/IdentityAvatar.jsx';
import useIdentityProfile from './identity/useIdentityProfile.js';
import { addFlowHiveTask, deleteFlowHiveTask, dependencyTypeHelp, deriveFlowHiveExecutiveSummary, moveFlowHiveTask, moveFlowHiveTaskByOffset, phaseDefinitions, workingDaysInclusive } from './flowhive-enterprise-helpers.js';
import { FlowHiveCustomerSharingPanel, FlowHiveEvidenceReadiness, FlowHiveFinancialsPanel, FlowHiveSaveBar, FlowHiveStatusRaidPanel } from './ProjectFlowHiveEnterprisePanels.jsx';
import './project-flowhive-center.css';
import './project-flowhive-ai-confidence.css';
import './projectpulse-module-standard.css';

const views = [
  { id: 'portfolio', label: 'Portfolio' },
  { id: 'planner', label: 'Planner' },
  { id: 'timeline', label: 'Timeline & risk' },
  { id: 'financials', label: 'Financials' },
  { id: 'status', label: 'Status & RAID' },
  { id: 'ai', label: 'AI draft studio' },
  { id: 'exports', label: 'Branded exports' },
  { id: 'governance', label: 'Governance' }
];

const plannerPhases = [
  { wbs: '1', name: 'Plan' },
  { wbs: '2', name: 'Design' },
  { wbs: '3', name: 'Implement' },
  { wbs: '4', name: 'Validate' },
  { wbs: '5', name: 'Release' }
];

const plannerStatuses = ['not_started', 'in_progress', 'blocked', 'complete'];
const enterprisePhases = phaseDefinitions();
const defaultControls = { contractType: 'unknown', currencyCode: 'USD', approvedBudget: null, expenseBudget: null, contingencyBudget: null, forecastAtCompletion: null, percentCompleteMethod: 'task_weighted', statusReportCadence: 'weekly', customerSharingEnabled: false, financialNotes: '' };
const defaultRaid = { planId: null, itemType: 'risk', title: '', description: '', status: 'open', priority: 'medium', probability: null, impact: null, ownerUserId: null, dueDate: null, mitigation: '', sourceKind: 'manual', sourceReference: '' };
const defaultStatusDraft = { overallHealth: 'green', scheduleHealth: 'green', financialHealth: 'unknown', scopeHealth: 'green', executiveSummary: '', accomplishments: [], nextSteps: [], decisionsNeeded: [], keyRisks: [], generatedSource: 'deterministic' };
const defaultShareDraft = { planId: '', versionNumber: null, expirationDays: 30, customerLabel: '', shareNote: '', allowedArtifacts: ['view', 'pdf'] };

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
  const correlationId = response.headers.get('x-projectpulse-correlation-id') || response.headers.get('x-correlation-id') || '';
  if (body && typeof body === 'object' && correlationId && !body.correlationId) body.correlationId = correlationId;
  if (!response.ok) {
    const error = new Error(body.message || body.detail || body.issues?.[0]?.message || `${path} returned HTTP ${response.status}`);
    error.responseBody = body;
    throw error;
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

async function putJson(path, body) {
  return parseResponse(await fetch(path, {
    method: 'PUT',
    headers: authenticationHeaders({ 'Content-Type': 'application/json' }),
    body: JSON.stringify(body)
  }), path);
}

async function deleteJson(path, body = null) {
  return parseResponse(await fetch(path, {
    method: 'DELETE',
    headers: authenticationHeaders(body ? { 'Content-Type': 'application/json' } : {}),
    ...(body ? { body: JSON.stringify(body) } : {})
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

function addCalendarDays(value, days) {
  const date = new Date(`${value}T12:00:00Z`);
  date.setUTCDate(date.getUTCDate() + days);
  return date.toISOString().slice(0, 10);
}

function localTask(wbsNumber, parentWbsNumber, name, description, canonicalTaskId = null, durationWorkingDays = 1) {
  return {
    clientTaskId: canonicalTaskId || crypto.randomUUID(),
    canonicalTaskId,
    wbsNumber,
    parentWbsNumber,
    name,
    description,
    durationWorkingDays,
    isMilestone: false,
    constraintType: 'ASAP',
    constraintDate: null,
    percentComplete: 0,
    remainingEffortHours: durationWorkingDays * 8,
    status: 'not_started',
    isSummary: false,
    phase: plannerPhases.find((phase) => phase.wbs === parentWbsNumber)?.name || '',
    detailedSteps: [],
    inputs: [],
    outputs: [],
    acceptanceCriteria: [],
    validationSteps: [],
    customerResponsibilities: [],
    usSignalResponsibilities: [],
    prerequisites: [],
    risks: [],
    openQuestions: [],
    priority: 'normal',
    citationIds: [],
    comments: '',
    notes: ''
  };
}

function buildLocalDraft(project, tasks, assignments) {
  if (!project) return null;
  const projectTasks = tasks.filter((task) => task.projectId === project.projectId);
  const phaseRows = plannerPhases.map((phase) => ({
    ...localTask(phase.wbs, '', phase.name, `${phase.name} phase summary.`),
    durationWorkingDays: 0,
    remainingEffortHours: 0,
    isSummary: true,
    phase: phase.name,
    priority: 'summary'
  }));
  const implementTasks = projectTasks.map((task, index) => ({
    ...localTask(
      `3.${index + 1}`,
      '3',
      task.taskName,
      task.taskDescription || 'Review and complete the authorized canonical project task.',
      task.taskId,
      Math.max(1, Math.ceil(Number(task.assignedHours || task.remainingHours || 8) / 8))),
    percentComplete: task.assignedHours
      ? Math.min(100, Math.round((Number(task.usedHours || 0) / Number(task.assignedHours)) * 100))
      : 0,
    remainingEffortHours: Number(task.remainingHours || 0),
    status: Number(task.remainingHours || 0) <= 0 && Number(task.usedHours || 0) > 0 ? 'complete' : 'not_started'
  }));
  const childTasks = [
    localTask('1.1', '1', 'Review approved scope and delivery readiness', 'Confirm the approved scope, deliverables, exclusions, prerequisites, responsibilities, acceptance criteria, and open questions.'),
    localTask('2.1', '2', 'Validate solution design and work instructions', 'Translate the approved scope into a reviewable technical design, implementation procedure, validation plan, and rollback approach.'),
    ...(implementTasks.length ? implementTasks : [localTask('3.1', '3', 'Execute approved implementation work', 'Perform the reviewed scoped work in controlled stages and retain objective implementation evidence.', null, 2)]),
    localTask('4.1', '4', 'Validate outcomes and acceptance evidence', 'Execute the approved validation plan, remediate authorized defects, and map objective evidence to the acceptance criteria.'),
    localTask('5.1', '5', 'Complete handoff, release, and closeout', 'Finalize documentation, knowledge transfer, operational ownership, outstanding actions, acceptance evidence, and closeout review.')
  ];
  const planTasks = plannerPhases.flatMap((phase) => [
    phaseRows.find((row) => row.wbsNumber === phase.wbs),
    ...childTasks.filter((task) => task.parentWbsNumber === phase.wbs)
  ]);
  const wbsByTaskId = new Map(implementTasks.map((task) => [task.canonicalTaskId, task.wbsNumber]));
  const planAssignments = assignments
    .filter((assignment) => assignment.projectId === project.projectId && assignment.resourceUserId)
    .map((assignment) => ({
      taskWbs: wbsByTaskId.get(assignment.taskId) || '',
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
    projectEndDate: project.endDate || addCalendarDays(project.startDate || currentIsoDate(), 60),
    tasks: planTasks,
    dependencies: childTasks.slice(1).map((task, index) => ({
      predecessorWbs: childTasks[index].wbsNumber,
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

function taskDetailSections(task) {
  return [
    ['Ordered work steps', 'detailedSteps', task.detailedSteps],
    ['Required inputs', 'inputs', task.inputs],
    ['Expected outputs', 'outputs', task.outputs],
    ['Prerequisites', 'prerequisites', task.prerequisites],
    ['Validation steps', 'validationSteps', task.validationSteps],
    ['Acceptance criteria', 'acceptanceCriteria', task.acceptanceCriteria],
    ['Customer responsibilities', 'customerResponsibilities', task.customerResponsibilities],
    ['US Signal responsibilities', 'usSignalResponsibilities', task.usSignalResponsibilities],
    ['Risks', 'risks', task.risks],
    ['Open questions', 'openQuestions', task.openQuestions]
  ];
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
  const [collapsedPhases, setCollapsedPhases] = useState(() => new Set());
  const [expandedTaskWbs, setExpandedTaskWbs] = useState('');
  const [savedPlans, setSavedPlans] = useState([]);
  const [baselineNote, setBaselineNote] = useState('Reviewed with project delivery and engineering stakeholders.');
  const [gsdExcerpt, setGsdExcerpt] = useState('');
  const [sowExcerpt, setSowExcerpt] = useState('');
  const [requestedOutcome, setRequestedOutcome] = useState('Create a reviewable implementation plan with detailed tasks, dependencies, risks, assumptions, milestones, acceptance, operational handoff, and closeout.');
  const [enterprise, setEnterprise] = useState(null);
  const [enterpriseError, setEnterpriseError] = useState(null);
  const [financials, setFinancials] = useState(null);
  const [controls, setControls] = useState(defaultControls);
  const [dirty, setDirty] = useState(false);
  const [draggedTaskWbs, setDraggedTaskWbs] = useState('');
  const [newRaid, setNewRaid] = useState(defaultRaid);
  const [statusDraft, setStatusDraft] = useState(defaultStatusDraft);
  const [shareDraft, setShareDraft] = useState(defaultShareDraft);
  const [latestShareUrl, setLatestShareUrl] = useState('');
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

  async function loadEnterpriseWorkspace(projectId, applyWorkingCopy = false) {
    if (!projectId) {
      setEnterprise(null);
      setEnterpriseError(null);
      setFinancials(null);
      setControls(defaultControls);
      return;
    }
    setEnterpriseError(null);
    try {
      const result = await getJson(`/api/project-flowhive/projects/${projectId}/enterprise`);
      setEnterprise(result);
      setControls({ ...defaultControls, ...(result.controls || {}) });
      setShareDraft((current) => ({ ...current, customerLabel: result.project?.customerName || current.customerLabel }));
      if (applyWorkingCopy && result.workingCopy?.plan) {
        setDraftPlan(result.workingCopy.plan);
        setSchedule(null);
        setValidation(null);
        setDirty(false);
        setNotice(`Loaded PM working-copy revision ${result.workingCopy.workingRevision}.`);
      }
    } catch (workspaceError) {
      setEnterprise(null);
      const body = workspaceError.responseBody || {};
      setEnterpriseError({
        status: body.status || 'flowhive_enterprise_unavailable',
        message: body.message || workspaceError.message || 'The enterprise workspace is temporarily unavailable.',
        requiredMigration: body.requiredMigration || (body.status === 'migration_086_required' ? '086_module_066_flowhive_enterprise_pm' : ''),
        correlationId: body.correlationId || ''
      });
      setError('');
    }
    try {
      setFinancials(await getJson(`/api/project-financials/projects/${projectId}?workspace=project_management`));
    } catch (financialError) {
      setFinancials({ status: 'financial_data_unavailable', message: financialError.message, project: null });
    }
  }

  useEffect(() => {
    loadModule();
  }, []);

  useEffect(() => {
    if (selectedProjectId) loadEnterpriseWorkspace(selectedProjectId, false);
    else {
      setEnterprise(null);
      setEnterpriseError(null);
      setFinancials(null);
    }
  }, [selectedProjectId]);

  const projects = portfolio?.projects ?? [];
  const tasks = portfolio?.tasks ?? [];
  const assignments = portfolio?.assignments ?? [];
  const capabilities = capabilityResponse?.capabilities ?? [];
  const selectedProject = projects.find((project) => project.projectId === selectedProjectId) || null;
  const scheduleByWbs = useMemo(() => new Map(
    (schedule?.tasks || []).map((task) => [task.wbsNumber, task])
  ), [schedule]);

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
    setDirty(true);
    setSchedule(null);
    setValidation(null);
    setAiPreview(null);
    setCollapsedPhases(new Set());
    setExpandedTaskWbs('');
    setNotice('A new FlowHive draft is ready. Save it to create the first immutable version.');
    setActiveView('planner');
  }

  function updatePlan(field, value) {
    setDraftPlan((current) => current ? { ...current, [field]: value } : current);
    setSchedule(null);
    setDirty(true);
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
    setDirty(true);
  }

  function updateDependencyForTask(index, field, value) {
    if (!draftPlan || draftPlan.tasks[index]?.isSummary) return;
    const successorWbs = draftPlan.tasks[index].wbsNumber;
    setDraftPlan((current) => {
      if (field === 'predecessorWbs' && !value) {
        return {
          ...current,
          dependencies: current.dependencies.filter((dependency) => dependency.successorWbs !== successorWbs)
        };
      }
      const existing = current.dependencies.find((dependency) => dependency.successorWbs === successorWbs);
      const next = existing
        ? current.dependencies.map((dependency) => dependency.successorWbs === successorWbs
            ? { ...dependency, [field]: value }
            : dependency)
        : [...current.dependencies, {
            predecessorWbs: field === 'predecessorWbs' ? value : '',
            successorWbs,
            type: 'FS',
            lagWorkingDays: 0,
            [field]: value
          }];
      return { ...current, dependencies: next };
    });
    setSchedule(null);
    setDirty(true);
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
    setSchedule(null);
    setDirty(true);
  }

  function addTask(phaseWbs) {
    setDraftPlan((current) => addFlowHiveTask(current, phaseWbs, localTask));
    setSchedule(null);
    setValidation(null);
    setDirty(true);
    setCollapsedPhases((current) => { const next = new Set(current); next.delete(String(phaseWbs)); return next; });
    setNotice(`Added a new ${enterprisePhases.find((phase) => phase.wbs === String(phaseWbs))?.name || 'project'} task. Complete its details and save the working copy.`);
  }

  function deleteTask(wbsNumber) {
    const task = draftPlan?.tasks?.find((candidate) => !candidate.isSummary && candidate.wbsNumber === wbsNumber);
    if (!task || !window.confirm(`Delete WBS ${wbsNumber} — ${task.name}? Dependencies and assignments referencing this task will be repaired or removed.`)) return;
    setDraftPlan((current) => deleteFlowHiveTask(current, wbsNumber));
    setExpandedTaskWbs('');
    setSchedule(null);
    setValidation(null);
    setDirty(true);
    setNotice(`Deleted WBS ${wbsNumber}. Review the dependency chain, then recalculate the schedule.`);
  }

  function dropTask(targetWbs, targetPhaseWbs, placement = 'before') {
    if (!draggedTaskWbs || draggedTaskWbs === targetWbs) return;
    setDraftPlan((current) => moveFlowHiveTask(current, draggedTaskWbs, targetWbs, targetPhaseWbs, placement));
    setDraggedTaskWbs('');
    setSchedule(null);
    setValidation(null);
    setDirty(true);
    setNotice('Task moved and WBS values were renumbered. Review dependencies before saving.');
  }

  function changeTaskPhase(wbsNumber, phaseWbs) {
    setDraftPlan((current) => moveFlowHiveTask(current, wbsNumber, '', phaseWbs, 'after'));
    setSchedule(null);
    setValidation(null);
    setDirty(true);
  }

  function moveTaskOffset(wbsNumber, offset) {
    setDraftPlan((current) => moveFlowHiveTaskByOffset(current, wbsNumber, offset));
    setSchedule(null);
    setValidation(null);
    setDirty(true);
  }

  function updateTaskStartDate(index, value) {
    setDraftPlan((current) => {
      if (!current) return current;
      const nextTasks = current.tasks.map((task, taskIndex) => taskIndex === index
        ? { ...task, constraintType: value ? 'SNET' : 'ASAP', constraintDate: value || null }
        : task);
      return { ...current, tasks: nextTasks };
    });
    setSchedule(null);
    setDirty(true);
  }

  function updateTaskEndDate(index, value, scheduledStart) {
    if (!value) return;
    setDraftPlan((current) => {
      if (!current) return current;
      const task = current.tasks[index];
      const start = task.constraintDate || scheduledStart || current.projectStartDate;
      const durationWorkingDays = workingDaysInclusive(start, value);
      return { ...current, tasks: current.tasks.map((candidate, taskIndex) => taskIndex === index ? { ...candidate, durationWorkingDays, remainingEffortHours: Math.max(Number(candidate.remainingEffortHours || 0), durationWorkingDays * 8) } : candidate) };
    });
    setSchedule(null);
    setDirty(true);
  }

  async function saveWorkingCopy() {
    if (!draftPlan || !selectedProjectId) return;
    setBusy('working-copy');
    setError('');
    try {
      const result = await putJson(`/api/project-flowhive/projects/${selectedProjectId}/working-copy`, {
        plan: draftPlan,
        expectedRowVersion: enterprise?.workingCopy?.rowVersion || null
      });
      setDirty(false);
      setNotice(`PM working-copy revision ${result.workingRevision} saved. The canonical project and immutable plan history were not changed.`);
      await loadEnterpriseWorkspace(selectedProjectId, false);
    } catch (actionError) {
      setError(actionError.message);
    } finally {
      setBusy('');
    }
  }

  async function saveProjectControls(nextControls = controls) {
    if (!selectedProjectId) return;
    setBusy('controls');
    setError('');
    try {
      await putJson(`/api/project-flowhive/projects/${selectedProjectId}/controls`, nextControls);
      setControls(nextControls);
      setNotice('Project financial and reporting controls were saved.');
      await loadEnterpriseWorkspace(selectedProjectId, false);
    } catch (actionError) {
      setError(actionError.message);
    } finally {
      setBusy('');
    }
  }

  async function createRaidItem() {
    if (!selectedProjectId) return;
    setBusy('raid-create');
    setError('');
    try {
      await postJson(`/api/project-flowhive/projects/${selectedProjectId}/raid`, { ...newRaid, planId: draftPlan?.planId || null });
      setNewRaid(defaultRaid);
      setNotice('RAID item added.');
      await loadEnterpriseWorkspace(selectedProjectId, false);
    } catch (actionError) {
      setError(actionError.message);
    } finally {
      setBusy('');
    }
  }

  async function deleteRaidItem(item) {
    if (!selectedProjectId || !window.confirm(`Delete ${item.itemType}: ${item.title}?`)) return;
    setBusy(`raid-delete-${item.raidItemId}`);
    setError('');
    try {
      await deleteJson(`/api/project-flowhive/projects/${selectedProjectId}/raid/${item.raidItemId}`);
      setNotice('RAID item deleted.');
      await loadEnterpriseWorkspace(selectedProjectId, false);
    } catch (actionError) {
      setError(actionError.message);
    } finally {
      setBusy('');
    }
  }

  function generateStatusSummary() {
    setStatusDraft((current) => ({
      ...current,
      executiveSummary: deriveFlowHiveExecutiveSummary(draftPlan, schedule, enterprise, aiPreview, financials),
      generatedSource: aiPreview?.correlationId ? 'celar_ai' : 'deterministic',
      keyRisks: (enterprise?.raidItems || []).filter((item) => item.itemType === 'risk' && !['closed', 'resolved'].includes(item.status)).map((item) => item.title).slice(0, 12)
    }));
  }

  async function createStatusReport() {
    if (!selectedProjectId) return;
    setBusy('status-report');
    setError('');
    try {
      const saved = savedPlans.find((plan) => plan.planId === draftPlan?.planId);
      await postJson(`/api/project-flowhive/projects/${selectedProjectId}/status-reports`, {
        ...statusDraft,
        planId: draftPlan?.planId || null,
        planVersionNumber: saved?.currentVersion || null,
        statusDate: currentIsoDate(),
        financialSnapshot: financials?.project || {},
        scheduleSnapshot: schedule || {},
        celarAiCorrelationId: aiPreview?.correlationId || draftPlan?.celarAiCorrelationId || ''
      });
      setNotice('Immutable Project Manager status report created.');
      await loadEnterpriseWorkspace(selectedProjectId, false);
    } catch (actionError) {
      setError(actionError.message);
    } finally {
      setBusy('');
    }
  }

  async function prepareSowEvidence(item, approveCurrentVersion) {
    if (!selectedProjectId) return;
    const approvalNote = approveCurrentVersion
      ? window.prompt('Enter the reviewed SOW version approval note (at least 10 characters):', 'Reviewed by the assigned Project Manager for FlowHive planning evidence.')
      : '';
    if (approveCurrentVersion && (!approvalNote || approvalNote.trim().length < 10)) return;
    setBusy(`evidence-${item.documentId}`);
    setError('');
    try {
      const result = await postJson(`/api/project-flowhive/projects/${selectedProjectId}/sow-evidence/${item.documentId}/prepare`, {
        approveCurrentVersion,
        approvalNote,
        correlationId: aiPreview?.correlationId || crypto.randomUUID()
      });
      setNotice(result.message);
      await loadEnterpriseWorkspace(selectedProjectId, false);
    } catch (actionError) {
      setError(actionError.message);
    } finally {
      setBusy('');
    }
  }

  async function enableCustomerSharing() {
    const next = { ...controls, customerSharingEnabled: true };
    await saveProjectControls(next);
  }

  async function createCustomerShare() {
    if (!selectedProjectId) return;
    setBusy('customer-share');
    setError('');
    try {
      const result = await postJson(`/api/project-flowhive/projects/${selectedProjectId}/customer-shares`, shareDraft);
      setLatestShareUrl(result.share?.shareUrl || '');
      setNotice('Reviewed customer link created. The full token is displayed once.');
      await loadEnterpriseWorkspace(selectedProjectId, false);
    } catch (actionError) {
      setError(actionError.message);
    } finally {
      setBusy('');
    }
  }

  async function revokeCustomerShare(share) {
    if (!selectedProjectId || !window.confirm('Revoke this customer link immediately?')) return;
    setBusy(`share-revoke-${share.shareId}`);
    setError('');
    try {
      await deleteJson(`/api/project-flowhive/projects/${selectedProjectId}/customer-shares/${share.shareId}`, { reason: 'Revoked by the assigned Project Manager.' });
      setNotice('Customer link revoked.');
      await loadEnterpriseWorkspace(selectedProjectId, false);
    } catch (actionError) {
      setError(actionError.message);
    } finally {
      setBusy('');
    }
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
      if (actionError.responseBody?.issues) {
        setSchedule(actionError.responseBody);
        setValidation({ valid: false, issues: actionError.responseBody.issues });
        setActiveView('planner');
      }
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
      setDirty(false);
      setNotice(`FlowHive draft version ${result.version} was saved with immutable schedule and validation evidence.`);
      const plansResult = await getJson('/api/project-flowhive/plans');
      setSavedPlans(plansResult.plans || []);
      await loadEnterpriseWorkspace(selectedProjectId, false);
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
      await loadEnterpriseWorkspace(selectedProjectId, false);
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
      setDirty(false);
      setActiveView('planner');
    } catch (actionError) {
      setError(actionError.message);
    } finally {
      setBusy('');
    }
  }

  async function previewAiRequest() {
    if (!draftPlan) return;
    if (!draftPlan.projectStartDate || !draftPlan.projectEndDate) {
      setError('Select both the project start date and end date before running AI Planner.');
      return;
    }
    if (draftPlan.projectEndDate < draftPlan.projectStartDate) {
      setError('Project end date must be on or after the project start date.');
      return;
    }
    setBusy('ai-planner');
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
        setDirty(true);
      }
      if (result.schedule?.valid) setSchedule(result.schedule);
      else setSchedule(result.schedule || null);
      setValidation(result.schedule?.valid === false
        ? { valid: false, issues: result.schedule.issues || result.validation?.issues || [] }
        : result.validation || null);
      setCollapsedPhases(new Set());
      setExpandedTaskWbs('');
      setActiveView('planner');
      setNotice(result.schedule?.valid
        ? `AI Planner populated the five-phase task plan from authorized private evidence${result.planningEvidence?.scopeOfServicesLocated ? ', including the approved SOW Scope of Services' : ''}. Review it, assign owners, then save an immutable version.`
        : 'Celar AI produced a review draft that requires correction before it can be saved.');
    } catch (actionError) {
      if (actionError.responseBody?.status === 'flowhive_sow_evidence_not_ready') {
        const details = [...(actionError.responseBody.missingEvidence || []), ...(actionError.responseBody.warnings || [])].filter(Boolean).slice(0, 5);
        setError(`AI Planner is waiting for an approved, citation-ready SOW Scope of Services. ${details.join(' ') || 'Open AI draft studio to review each document readiness blocker.'}`);
        setActiveView('ai');
        await loadEnterpriseWorkspace(selectedProjectId, false);
      } else {
        setError(actionError.message);
      }
    } finally {
      setBusy('');
    }
  }

  function togglePhase(wbs) {
    setCollapsedPhases((current) => {
      const next = new Set(current);
      if (next.has(wbs)) next.delete(wbs);
      else next.add(wbs);
      return next;
    });
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
          artifactTitle: `${draftPlan.planName} — Project Management working plan`,
          audience: 'internal',
          excludeNotes: false,
          acknowledgeInternalDraft: true
        })
      }), path);
      const blob = await response.blob();
      const url = URL.createObjectURL(blob);
      const anchor = document.createElement('a');
      anchor.href = url;
      anchor.download = `${draftPlan.projectCode || 'project-flowhive'}-project-management-plan.${format === 'excel' ? 'xlsx' : 'pdf'}`;
      anchor.click();
      URL.revokeObjectURL(url);
      setNotice(`US Signal branded ${format === 'excel' ? 'Excel' : 'PDF'} Project Management working plan generated. Customer sharing remains a separate reviewed action.`);
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
        <span>Use the PM working copy for frequent updates, immutable versions for formal review, and an exact reviewed baseline for governed customer sharing.</span>
      </aside>

      {portfolio?.access ? (
        <div className="flowhive-access-banner">
          <div><span>Effective user</span><strong>{portfolio.access.displayName || portfolio.access.email}</strong></div>
          <div><span>Backend scope</span><strong>{labelFrom(portfolio.access.scope)}</strong></div>
          <div><span>View-As</span><strong>{portfolio.access.isViewAs ? 'Read-only preview' : 'Not active'}</strong></div>
          <div><span>Persistence</span><strong>{capabilityResponse?.databaseMutationEnabled ? 'Ready' : 'Unavailable'}</strong></div>
          <div><span>Customer links</span><strong>{enterprise?.access?.canShare ? (controls.customerSharingEnabled ? 'Enabled for reviewed baseline' : 'Available — enable in Financials') : 'Read-only / unavailable'}</strong></div>
        </div>
      ) : null}

      {enterpriseError ? <div className="flowhive-error flowhive-enterprise-readiness-error" role="alert"><div><strong>FlowHive enterprise controls are temporarily unavailable.</strong><span>{enterpriseError.message}</span>{enterpriseError.requiredMigration ? <small>Required database contract: {enterpriseError.requiredMigration}</small> : null}{enterpriseError.correlationId ? <small>Correlation ID: {enterpriseError.correlationId}</small> : null}</div><button type="button" onClick={() => loadEnterpriseWorkspace(selectedProjectId, false)} disabled={!selectedProjectId || busy}>Retry enterprise workspace</button></div> : null}
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
                <footer><button type="button" onClick={() => setSelectedProjectId(project.projectId)}>Select project</button><button type="button" className="primary" onClick={() => { setSelectedProjectId(project.projectId); setDraftPlan(buildLocalDraft(project, tasks, assignments)); setDirty(true); setSchedule(null); setValidation(null); setAiPreview(null); setCollapsedPhases(new Set()); setExpandedTaskWbs(''); setActiveView('planner'); }}>Open planner</button></footer>
              </article>
            ))}
          </div>
        </div>
      ) : null}

      {activeView === 'planner' ? (
        <div className="flowhive-view-panel">
          <div className="flowhive-planner-toolbar">
            <label>Canonical project<select value={selectedProjectId} onChange={(event) => { setSelectedProjectId(event.target.value); setDraftPlan(null); setSchedule(null); setValidation(null); setAiPreview(null); }}><option value="">Select a project</option>{projects.map((project) => <option key={project.projectId} value={project.projectId}>{project.projectCode} — {project.projectName}</option>)}</select></label>
            <button type="button" onClick={createLocalDraft} disabled={!selectedProject}>Create/reset draft</button><button type="button" onClick={() => loadEnterpriseWorkspace(selectedProjectId, true)} disabled={!enterprise?.workingCopy}>Load working copy</button>
            <button type="button" className="primary flowhive-ai-planner-button" onClick={previewAiRequest} disabled={!draftPlan || busy}>{busy === 'ai-planner' ? 'Building from SOW…' : 'AI Planner'}</button>
            <button type="button" onClick={validatePlan} disabled={!draftPlan || busy}>Validate</button>
            <button type="button" onClick={calculateSchedule} disabled={!draftPlan || busy}>{busy === 'schedule' ? 'Calculating…' : 'Calculate schedule'}</button>
            <button type="button" onClick={saveDraft} disabled={!draftPlan || busy || portfolio?.access?.isViewAs}>{busy === 'save' ? 'Saving…' : 'Save immutable version'}</button>
            <button type="button" onClick={establishBaseline} disabled={!draftPlan?.planId || busy || portfolio?.access?.isViewAs || baselineNote.trim().length < 10}>{busy === 'baseline' ? 'Approving…' : 'Establish reviewed baseline'}</button>
          </div>
          <FlowHiveSaveBar dirty={dirty} workingCopy={enterprise?.workingCopy} canManage={Boolean(enterprise?.access?.canManage)} busy={busy} onSaveWorkingCopy={saveWorkingCopy} onSaveVersion={saveDraft} />
          <div className="flowhive-plan-metadata">
            <label>Saved FlowHive plan<select value={draftPlan?.planId || ''} onChange={(event) => loadSavedPlan(event.target.value)}><option value="">New unsaved plan</option>{savedPlans.filter((plan) => !selectedProjectId || plan.projectId === selectedProjectId).map((plan) => <option key={plan.planId} value={plan.planId}>{plan.planName} · v{plan.currentVersion}{plan.baselineVersion ? ` · baseline v${plan.baselineVersion}` : ''}</option>)}</select></label>
            <label>Baseline review note<input value={baselineNote} onChange={(event) => setBaselineNote(event.target.value)} placeholder="Required reviewer decision note" /></label>
          </div>
          {!draftPlan ? <EmptyState>Select an authorized project and create or load a FlowHive draft.</EmptyState> : (
            <>
              <div className="flowhive-plan-metadata flowhive-planner-metadata">
                <label>Plan name<input value={draftPlan.planName} onChange={(event) => updatePlan('planName', event.target.value)} /></label>
                <label>Revision<input value={draftPlan.revisionLabel} onChange={(event) => updatePlan('revisionLabel', event.target.value)} /></label>
                <label>Start date<input type="date" value={draftPlan.projectStartDate || ''} onChange={(event) => updatePlan('projectStartDate', event.target.value)} /></label>
                <label>End date<input type="date" value={draftPlan.projectEndDate || ''} min={draftPlan.projectStartDate || undefined} onChange={(event) => updatePlan('projectEndDate', event.target.value)} /></label>
                <label>GSD version<input value={draftPlan.gsdVersion} onChange={(event) => updatePlan('gsdVersion', event.target.value)} placeholder="Approved GSD version" /></label>
                <label>SOW version<input value={draftPlan.sowVersion} onChange={(event) => updatePlan('sowVersion', event.target.value)} placeholder="Approved SOW version" /></label>
              </div>
              {aiPreview ? <aside className="flowhive-ai-planner-summary">
                <div><span>AI Planner result</span><strong>{labelFrom(aiPreview.status)}</strong><small>{aiPreview.planningEvidence?.scopeOfServicesLocated ? 'Approved SOW Scope of Services located' : 'SOW scope evidence requires review'}</small></div>
                <div><span>Private evidence</span><strong>{aiPreview.planningEvidence?.approvedSowCitationCount ?? 0} SOW citation(s)</strong><small>{aiPreview.planningEvidence?.scopeOfServicesCitationCount ?? 0} scope citation(s)</small></div>
                <div><span>Evidence score</span><strong>{formatPercent(aiPreview.confidence)}</strong><small>{labelFrom(aiPreview.executionPath)}</small></div>
                <div className="privacy"><span>External privacy</span><strong>No private SOW content sent</strong><small>Only a fixed identity-free planning blueprint is eligible for Claude/OpenAI.</small></div>
              </aside> : null}
              <div className="flowhive-table-heading"><div><h3>AI Planner work breakdown</h3><p>Expand each phase and task for complete steps, inputs, outputs, validation, acceptance, responsibilities, risks, questions, and private citations. Use the Add task action on the Plan, Design, Implement, Validate, or Release phase header. Drag tasks to reorder or move them between phases.</p></div></div>
              <div className="flowhive-table-wrap">
                <table className="flowhive-task-table flowhive-planner-table flowhive-smartsheet-table">
                  <thead><tr><th title="Work Breakdown Structure number. FlowHive renumbers child tasks after a move or deletion.">WBS</th><th title="The scoped activity or phase deliverable.">Task Name</th><th title="Calculated start date. Enter a date to set a Start No Earlier Than constraint.">Start Date</th><th title="Calculated finish date. Editing it recalculates task duration in working days.">End Date</th><th title="Weekday duration, excluding weekends.">Duration in Days</th><th title="Completion percentage from 0 through 100.">Progress</th><th title="The WBS task that controls this task. Start means no predecessor.">Predecessor</th><th title={`${dependencyTypeHelp.FS} ${dependencyTypeHelp.SS} ${dependencyTypeHelp.FF} ${dependencyTypeHelp.SF}`}>Type</th><th title="Review and collaboration comments.">Comments</th><th title="Internal task notes included in the PM working artifact, but excluded from customer links.">Notes</th><th title="Module 062 identity assigned to the task.">Assigned Identity</th></tr></thead>
                  <tbody>{draftPlan.tasks.filter((task) => task.isSummary || !collapsedPhases.has(task.parentWbsNumber)).map((task) => {
                    const index = draftPlan.tasks.indexOf(task);
                    const dependency = draftPlan.dependencies.find((item) => item.successorWbs === task.wbsNumber);
                    const assignment = draftPlan.assignments.find((item) => item.taskWbs === task.wbsNumber);
                    const scheduledTask = scheduleByWbs.get(task.wbsNumber);
                    const detailOpen = expandedTaskWbs === task.wbsNumber;
                    if (task.isSummary) {
                      return <tr key={task.clientTaskId || task.wbsNumber} className={`flowhive-phase-row phase-${String(task.phase || task.name).toLowerCase()}`} onDragOver={(event) => event.preventDefault()} onDrop={() => dropTask('', task.wbsNumber, 'after')}>
                        <td><button type="button" className="flowhive-phase-toggle" onClick={() => togglePhase(task.wbsNumber)} aria-expanded={!collapsedPhases.has(task.wbsNumber)}><span aria-hidden="true">{collapsedPhases.has(task.wbsNumber) ? '▸' : '▾'}</span>{task.wbsNumber}</button></td>
                        <td><div className="flowhive-phase-name-actions"><span><strong>{task.name}</strong><small>{draftPlan.tasks.filter((candidate) => candidate.parentWbsNumber === task.wbsNumber).length} detailed task(s)</small></span><button type="button" disabled={!enterprise?.access?.canManage} onClick={() => addTask(task.wbsNumber)}>Add task</button></div></td>
                        <td><span>{formatDate(scheduledTask?.startDate)}</span></td>
                        <td><span>{formatDate(scheduledTask?.endDate)}</span></td>
                        <td><strong>{scheduledTask?.durationWorkingDays ?? '—'}{scheduledTask ? 'd' : ''}</strong></td>
                        <td><strong>{Math.round(Number(scheduledTask?.percentComplete ?? task.percentComplete ?? 0))}%</strong></td>
                        <td><span>—</span></td>
                        <td><span>—</span></td>
                        <td><span>—</span></td>
                        <td><span>{draftPlan.tasks.filter((candidate) => candidate.parentWbsNumber === task.wbsNumber).length} detailed task(s)</span></td>
                        <td><span>Phase summary</span></td>
                      </tr>;
                    }
                    return (
                      <Fragment key={task.clientTaskId || `${task.wbsNumber}-${index}`}>
                        <tr className={`flowhive-work-row phase-${String(task.phase || '').toLowerCase()} ${draggedTaskWbs === task.wbsNumber ? 'dragging' : ''}`} draggable={Boolean(enterprise?.access?.canManage)} onDragStart={() => setDraggedTaskWbs(task.wbsNumber)} onDragEnd={() => setDraggedTaskWbs('')} onDragOver={(event) => event.preventDefault()} onDrop={() => dropTask(task.wbsNumber, task.parentWbsNumber, 'before')}>
                          <td><span className="flowhive-wbs-child" title="Drag this row to reorder or move it to another phase"><span aria-hidden="true">⋮⋮</span>{task.wbsNumber}</span></td>
                          <td><div className="flowhive-task-name-control"><input aria-label={`Task ${task.wbsNumber} name`} value={task.name} onChange={(event) => updateTask(index, 'name', event.target.value)} /><button type="button" className="flowhive-inline-detail-button" onClick={() => setExpandedTaskWbs(detailOpen ? '' : task.wbsNumber)} aria-expanded={detailOpen}>{detailOpen ? 'Close details' : 'Task details'}</button><button type="button" className="danger-quiet" disabled={!enterprise?.access?.canManage} onClick={() => deleteTask(task.wbsNumber)}>Delete</button></div><small>{task.description}</small></td>
                          <td><input className="flowhive-date-cell" aria-label={`Start date for ${task.name}`} type="date" value={task.constraintDate || scheduledTask?.startDate || ''} onChange={(event) => updateTaskStartDate(index, event.target.value)} /></td>
                          <td><input className="flowhive-date-cell" aria-label={`End date for ${task.name}`} type="date" min={task.constraintDate || scheduledTask?.startDate || draftPlan.projectStartDate || undefined} value={scheduledTask?.endDate || ''} onChange={(event) => updateTaskEndDate(index, event.target.value, scheduledTask?.startDate)} /></td>
                          <td><div className="flowhive-duration-cell"><input aria-label={`Duration for ${task.name}`} type="number" min="1" max="730" value={task.durationWorkingDays} onChange={(event) => updateTask(index, 'durationWorkingDays', Number(event.target.value))} /><span>day(s)</span></div></td>
                          <td><div className="flowhive-duration-cell"><input aria-label={`Progress for ${task.name}`} type="number" min="0" max="100" value={task.percentComplete || 0} onChange={(event) => updateTask(index, 'percentComplete', Number(event.target.value))} /><span>%</span></div></td>
                          <td><select value={dependency?.predecessorWbs || ''} onChange={(event) => updateDependencyForTask(index, 'predecessorWbs', event.target.value)}><option value="">Start</option>{draftPlan.tasks.filter((option) => !option.isSummary && option.wbsNumber !== task.wbsNumber).map((option) => <option key={option.wbsNumber} value={option.wbsNumber}>{option.wbsNumber}</option>)}</select></td>
                          <td><select aria-label={`Dependency type for ${task.name}`} value={dependency?.type || 'FS'} disabled={!dependency?.predecessorWbs} onChange={(event) => updateDependencyForTask(index, 'type', event.target.value)}>{['FS', 'SS', 'FF', 'SF'].map((type) => <option key={type} value={type}>{type}</option>)}</select></td>
                          <td><textarea className="flowhive-sheet-textarea" aria-label={`Comments for ${task.name}`} value={task.comments || ''} onChange={(event) => updateTask(index, 'comments', event.target.value)} rows="2" placeholder="Review comments" /></td>
                          <td><textarea className="flowhive-sheet-textarea" aria-label={`Notes for ${task.name}`} value={task.notes || ''} onChange={(event) => updateTask(index, 'notes', event.target.value)} rows="2" placeholder="Task notes" /></td>
                          <td><select aria-label={`Assigned identity for ${task.name}`} value={assignment?.resourceUserId || ''} onChange={(event) => updateTaskResource(task.wbsNumber, event.target.value)}><option value="">Unassigned</option>{identityOptions.map((identity) => <option key={identity.userId} value={identity.userId}>{identity.displayName}{identity.email ? ` — ${identity.email}` : ''}</option>)}</select></td>
                        </tr>
                        {detailOpen ? <tr className="flowhive-task-detail-row"><td colSpan="11"><div className="flowhive-task-detail-panel">
                          <header><div><span>{task.phase} · WBS {task.wbsNumber}</span><h4>{task.name}</h4></div><div>{(task.citationIds || []).map((citationId) => <span key={citationId} className="flowhive-citation-chip">Private source [{citationId}]</span>)}</div></header>
                          <div className="flowhive-task-control-grid">
                            <label>Status<select aria-label={`Status for ${task.name}`} value={task.status || 'not_started'} onChange={(event) => updateTask(index, 'status', event.target.value)}>{plannerStatuses.map((status) => <option key={status} value={status}>{labelFrom(status)}</option>)}</select></label>
                            <label>Lead / lag working days<input aria-label={`Lead or lag for ${task.name}`} type="number" min="-365" max="365" value={dependency?.lagWorkingDays || 0} disabled={!dependency?.predecessorWbs} onChange={(event) => updateDependencyForTask(index, 'lagWorkingDays', Number(event.target.value))} /></label><label>Move to phase<select value={task.parentWbsNumber} disabled={!enterprise?.access?.canManage} onChange={(event) => changeTaskPhase(task.wbsNumber, event.target.value)}>{enterprisePhases.map((phase) => <option key={phase.wbs} value={phase.wbs}>{phase.name}</option>)}</select></label><div className="flowhive-task-move-actions"><button type="button" disabled={!enterprise?.access?.canManage} onClick={() => moveTaskOffset(task.wbsNumber, -1)}>Move up</button><button type="button" disabled={!enterprise?.access?.canManage} onClick={() => moveTaskOffset(task.wbsNumber, 1)}>Move down</button><button type="button" className="danger-quiet" disabled={!enterprise?.access?.canManage} onClick={() => deleteTask(task.wbsNumber)}>Delete task</button></div>
                          </div>
                          <label className="flowhive-task-description">Task description<textarea value={task.description || ''} onChange={(event) => updateTask(index, 'description', event.target.value)} rows="3" /></label>
                          <div className="flowhive-task-detail-grid">{taskDetailSections(task).map(([label, field, values]) => <label key={field}>{label}<textarea value={(values || []).join('\n')} onChange={(event) => updateTask(index, field, event.target.value.split('\n').map((value) => value.trim()).filter(Boolean))} placeholder={`Add ${label.toLowerCase()}, one per line`} rows={field === 'detailedSteps' ? 6 : 4} /></label>)}</div>
                        </div></td></tr> : null}
                      </Fragment>
                    );
                  })}</tbody>
                </table>
              </div>
              {validation ? <div className={`flowhive-validation ${validation.valid ? 'valid' : 'invalid'}`}><strong>{validation.valid ? 'Plan contract valid' : 'Plan contract needs correction'}</strong>{validation.issues?.length ? <ul>{validation.issues.map((issue, index) => <li key={`${issue.code}-${index}`}><code>{issue.path}</code> {issue.message}</li>)}</ul> : <span>No validation issues returned.</span>}</div> : null}
            </>
          )}
        </div>
      ) : null}

      {activeView === 'financials' ? <FlowHiveFinancialsPanel enterprise={enterprise} financials={financials} controls={controls} setControls={setControls} canManage={Boolean(enterprise?.access?.canManage)} busy={busy} onSave={() => saveProjectControls()} /> : null}

      {activeView === 'status' ? <FlowHiveStatusRaidPanel enterprise={enterprise} draftPlan={draftPlan} statusDraft={statusDraft} setStatusDraft={setStatusDraft} newRaid={newRaid} setNewRaid={setNewRaid} canManage={Boolean(enterprise?.access?.canManage)} busy={busy} onCreateRaid={createRaidItem} onDeleteRaid={deleteRaidItem} onGenerateSummary={generateStatusSummary} onCreateStatusReport={createStatusReport} /> : null}

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
            <p>Celar AI retrieves the authorized private SOW and related project evidence, converts each supported scope line into a cited WBS task, estimates its working-day duration, and calculates a deterministic review timeline.</p>
            <ol><li>The exact stored Module 064 order is followed for this capability.</li><li>Private SOW, GSD, design, task, and assignment evidence stays inside the governed boundary.</li><li>A citation-ready private plan is required; an uncited generic template is never substituted.</li><li>Each task keeps its evidence citations, duration, estimated hours, dependencies, start date, and finish date in the review plan.</li><li>Every output requires PM and Engineering review before baseline approval or customer delivery.</li></ol>
          </div>
          <FlowHiveEvidenceReadiness enterprise={enterprise} canManage={Boolean(enterprise?.access?.canManage)} busy={busy} onPrepare={prepareSowEvidence} />
          {!draftPlan ? <EmptyState>Create or load a plan draft first.</EmptyState> : <div className="flowhive-ai-form">
            <label>Requested outcome<textarea value={requestedOutcome} onChange={(event) => setRequestedOutcome(event.target.value)} rows={5} /></label>
            <label>Optional approved GSD excerpt<textarea value={gsdExcerpt} onChange={(event) => setGsdExcerpt(event.target.value)} placeholder="Optional private supplemental excerpt; indexed project documents are also searched." /></label>
            <label>Optional approved SOW excerpt<textarea value={sowExcerpt} onChange={(event) => setSowExcerpt(event.target.value)} placeholder="Optional private supplemental excerpt; raw document text is never sent to a public provider." /></label>
            <button type="button" className="primary" onClick={previewAiRequest} disabled={busy}>{busy === 'ai-planner' ? 'Generating detailed Celar AI plan…' : 'Generate and auto-fill detailed plan'}</button>
            {aiPreview ? <section className="celar-flowhive-production-result">
              <header><div><span>Celar AI result</span><strong>{labelFrom(aiPreview.status)}</strong></div><div><span>Execution path</span><strong>{labelFrom(aiPreview.executionPath)}</strong></div></header>
              <div className="metrics"><div><span>Evidence score</span><strong>{formatPercent(aiPreview.confidence)}</strong></div><div><span>Tasks</span><strong>{aiPreview.plan?.tasks?.length || 0}</strong></div><div><span>Working days</span><strong>{aiPreview.schedule?.scheduledWorkingDays ?? 'Not calculated'}</strong></div><div><span>Critical tasks</span><strong>{aiPreview.schedule?.criticalTaskCount ?? 'Not calculated'}</strong></div></div>
              <p>{aiPreview.confidenceExplanation}</p>
              {aiPreview.plan?.tasks?.length ? <div className="tasks"><table><thead><tr><th>WBS</th><th>Task</th><th>Description &amp; citations</th><th>Duration</th><th>Start</th><th>Finish</th><th>Status</th></tr></thead><tbody>{aiPreview.plan.tasks.map((task, index) => {
                const scheduled = aiPreview.schedule?.tasks?.find((row) => row.wbsNumber === task.wbsNumber);
                return <tr key={task.clientTaskId || `${task.wbsNumber}-${index}`}><td><code>{task.wbsNumber}</code></td><td><strong>{task.name}</strong></td><td>{task.description}</td><td>{task.durationWorkingDays} day(s)</td><td>{formatDate(task.estimatedStartDate || scheduled?.startDate)}</td><td>{formatDate(task.estimatedFinishDate || scheduled?.endDate)}</td><td>{labelFrom(task.status)}</td></tr>;
              })}</tbody></table></div> : null}
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
          <div className="flowhive-export-hero"><img src={usSignalLogoUrl} alt="US Signal" /><div><h3>US Signal Project Management artifacts</h3><p>Professional PDF and Excel working plans include an executive summary, schedule, dependencies, assignments, comments, notes, and artifact control. Customer sharing remains a separate reviewed action.</p><code>Logo SHA-256: {artifactReadiness?.branding?.sha256 || 'Loading governed checksum…'}</code></div></div>
          <div className="flowhive-export-grid"><article><h4>Project schedule PDF</h4><p>US Signal-branded landscape schedule with the Planner columns, comments, notes, assigned identity, date range, and artifact-control footer.</p><button type="button" onClick={() => downloadArtifact('pdf')} disabled={!draftPlan || busy}>{busy === 'pdf' ? 'Generating…' : 'Download PM working-plan PDF'}</button></article><article><h4>Planning workbook</h4><p>US Signal-branded workbook with the exact Planner column order plus summary, dependencies, and artifact-control sheets.</p><button type="button" onClick={() => downloadArtifact('excel')} disabled={!draftPlan || busy}>{busy === 'excel' ? 'Generating…' : 'Download PM planning workbook'}</button></article><FlowHiveCustomerSharingPanel enterprise={enterprise} controls={controls} savedPlans={savedPlans} draftPlan={draftPlan} latestShareUrl={latestShareUrl} setLatestShareUrl={setLatestShareUrl} shareDraft={shareDraft} setShareDraft={setShareDraft} canManage={Boolean(enterprise?.access?.canManage)} busy={busy} onEnableSharing={enableCustomerSharing} onCreateShare={createCustomerShare} onRevoke={revokeCustomerShare} /></div>
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
