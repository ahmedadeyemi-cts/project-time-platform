import { useEffect, useMemo, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import {
  addFlowHiveTask,
  deleteFlowHiveTask,
  moveFlowHiveTask,
  moveFlowHiveTaskByOffset,
  phaseDefinitions,
  renumberFlowHivePlan
} from './flowhive-enterprise-helpers.js';
import './project-forge-flowhive-sync.css';

const PHASES = phaseDefinitions();
const STATUS_OPTIONS = ['not_started', 'in_progress', 'blocked', 'complete'];
const REQUESTED_PROJECT_KEY = 'projectPulseFlowHiveRequestedProject';

function storedSessionToken() {
  try {
    const raw = window.localStorage.getItem('projectPulseAuthSession');
    if (!raw) return '';
    const session = JSON.parse(raw);
    if (session?.expiresAt && Date.now() >= Date.parse(session.expiresAt)) return '';
    return session?.sessionToken || session?.token || session?.accessToken || session?.session_token || '';
  } catch {
    return '';
  }
}

function authenticatedHeaders(extra = {}) {
  const token = storedSessionToken();
  return {
    ...(token
      ? {
          Authorization: `Bearer ${token}`,
          'X-ProjectPulse-Session': token,
          'X-ProjectPulse-Module-Number': '033'
        }
      : {}),
    ...extra
  };
}

async function requestJson(path, options = {}) {
  const response = await fetch(path, {
    credentials: 'include',
    ...options,
    headers: authenticatedHeaders(options.headers || {})
  });
  const contentType = response.headers.get('content-type') || '';
  const body = contentType.includes('application/json') ? await response.json() : null;
  if (!response.ok) {
    const error = new Error(body?.message || body?.detail || `${path} returned HTTP ${response.status}`);
    error.status = response.status;
    error.responseBody = body;
    throw error;
  }
  return body;
}

function clone(value) {
  if (globalThis.structuredClone) return globalThis.structuredClone(value);
  return JSON.parse(JSON.stringify(value));
}

function safeUuid() {
  if (globalThis.crypto?.randomUUID) return globalThis.crypto.randomUUID();
  if (globalThis.crypto?.getRandomValues) {
    const bytes = new Uint8Array(16);
    globalThis.crypto.getRandomValues(bytes);
    bytes[6] = (bytes[6] & 0x0f) | 0x40;
    bytes[8] = (bytes[8] & 0x3f) | 0x80;
    const hex = Array.from(bytes, (value) => value.toString(16).padStart(2, '0')).join('');
    return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
  }
  return `flowhive-${Date.now()}-${Math.random().toString(16).slice(2)}`;
}

function createTask(wbsNumber, parentWbsNumber, name, description) {
  return {
    clientTaskId: safeUuid(),
    canonicalTaskId: null,
    wbsNumber,
    parentWbsNumber,
    name,
    description,
    durationWorkingDays: 1,
    isMilestone: false,
    constraintType: 'ASAP',
    constraintDate: null,
    percentComplete: 0,
    remainingEffortHours: 8,
    status: 'not_started',
    isSummary: false,
    phase: PHASES.find((phase) => phase.wbs === String(parentWbsNumber))?.name || 'Implement',
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

function directLabelText(label) {
  return [...label.childNodes]
    .filter((node) => node.nodeType === 3)
    .map((node) => String(node.textContent || '').trim())
    .filter(Boolean)
    .join(' ');
}

function projectSelectFromForge() {
  const labels = [...document.querySelectorAll('.project-forge .forge-header-controls label')];
  return labels.find((candidate) => directLabelText(candidate) === 'Project')?.querySelector('select') || null;
}

function onProjectForgeRoute() {
  return String(window.location.hash || '').toLowerCase().includes('project-forge');
}

function onProjectFlowHiveRoute() {
  return String(window.location.hash || '').toLowerCase().includes('project-flowhive');
}

function selectRequestedFlowHiveProject() {
  if (!onProjectFlowHiveRoute()) return false;
  const requestedProjectId = window.sessionStorage.getItem(REQUESTED_PROJECT_KEY);
  if (!requestedProjectId) return false;

  const flowHiveRoot = document.querySelector('.project-flowhive-center, .project-flowhive');
  if (!flowHiveRoot) return false;

  const plannerButton = [...flowHiveRoot.querySelectorAll('button')]
    .find((button) => String(button.textContent || '').trim() === 'Planner');
  if (plannerButton && plannerButton.getAttribute('aria-selected') !== 'true') plannerButton.click();

  const labels = [...flowHiveRoot.querySelectorAll('.flowhive-planner-toolbar label, label')];
  const projectLabel = labels.find((label) => directLabelText(label) === 'Canonical project');
  const projectSelect = projectLabel?.querySelector('select') || null;
  if (!projectSelect) return false;

  const optionExists = [...projectSelect.options].some((option) => String(option.value) === String(requestedProjectId));
  if (!optionExists) return false;

  if (String(projectSelect.value) !== String(requestedProjectId)) {
    projectSelect.value = requestedProjectId;
    projectSelect.dispatchEvent(new Event('change', { bubbles: true }));
  }
  window.sessionStorage.removeItem(REQUESTED_PROJECT_KEY);
  return true;
}

function displayStatus(value) {
  return String(value || 'not started')
    .replaceAll('_', ' ')
    .replace(/\b\w/g, (letter) => letter.toUpperCase());
}

export default function ProjectForgeFlowHiveSyncPortal() {
  const [host, setHost] = useState(null);
  const [projectId, setProjectId] = useState('');
  const [workspace, setWorkspace] = useState(null);
  const [plan, setPlan] = useState(null);
  const [rowVersion, setRowVersion] = useState(null);
  const [workingRevision, setWorkingRevision] = useState(0);
  const [dirty, setDirty] = useState(false);
  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [expanded, setExpanded] = useState(true);
  const [phaseForNewTask, setPhaseForNewTask] = useState('1');
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');

  const projectIdRef = useRef('');
  const dirtyRef = useRef(false);
  const editRevisionRef = useRef(0);
  const requestSequenceRef = useRef(0);
  const loadAbortRef = useRef(null);

  useEffect(() => {
    projectIdRef.current = projectId;
  }, [projectId]);

  useEffect(() => {
    dirtyRef.current = dirty;
  }, [dirty]);

  useEffect(() => {
    let activeSelect = null;

    function synchronizeHostAndProject() {
      selectRequestedFlowHiveProject();

      if (!onProjectForgeRoute()) {
        setHost(null);
        setProjectId('');
        return;
      }

      const forge = document.querySelector('.project-forge');
      if (!forge) return;

      let portalHost = forge.querySelector('[data-project-forge-flowhive-sync-host]');
      if (!portalHost) {
        portalHost = document.createElement('div');
        portalHost.dataset.projectForgeFlowhiveSyncHost = 'true';
        const anchor = forge.querySelector('.forge-workspace-banner');
        if (anchor?.parentElement) anchor.insertAdjacentElement('afterend', portalHost);
        else forge.prepend(portalHost);
      }
      setHost(portalHost);

      const nextSelect = projectSelectFromForge();
      if (nextSelect !== activeSelect) {
        activeSelect?.removeEventListener('change', synchronizeHostAndProject);
        activeSelect = nextSelect;
        activeSelect?.addEventListener('change', synchronizeHostAndProject);
      }
      setProjectId(String(activeSelect?.value || ''));
    }

    const observer = new MutationObserver(synchronizeHostAndProject);
    observer.observe(document.body, { childList: true, subtree: true });
    window.addEventListener('hashchange', synchronizeHostAndProject);
    const interval = window.setInterval(synchronizeHostAndProject, 750);
    synchronizeHostAndProject();

    return () => {
      observer.disconnect();
      window.removeEventListener('hashchange', synchronizeHostAndProject);
      window.clearInterval(interval);
      activeSelect?.removeEventListener('change', synchronizeHostAndProject);
    };
  }, []);

  async function loadSharedWorkspace(selectedProjectId = projectId, options = {}) {
    const silent = Boolean(options.silent);
    if (!selectedProjectId) {
      loadAbortRef.current?.abort();
      setWorkspace(null);
      setPlan(null);
      setRowVersion(null);
      setWorkingRevision(0);
      dirtyRef.current = false;
      setDirty(false);
      return;
    }

    loadAbortRef.current?.abort();
    const controller = new AbortController();
    loadAbortRef.current = controller;
    const requestSequence = ++requestSequenceRef.current;
    const editRevisionAtStart = editRevisionRef.current;

    if (!silent) setLoading(true);
    if (!silent) setError('');
    try {
      const result = await requestJson(`/api/project-flowhive/projects/${selectedProjectId}/enterprise`, {
        signal: controller.signal
      });
      if (controller.signal.aborted) return;
      if (requestSequence !== requestSequenceRef.current) return;
      if (String(projectIdRef.current) !== String(selectedProjectId)) return;
      if (silent && (dirtyRef.current || editRevisionRef.current !== editRevisionAtStart)) return;

      const workingCopy = result?.workingCopy || null;
      setWorkspace(result);
      setPlan(workingCopy?.plan ? clone(workingCopy.plan) : null);
      setRowVersion(workingCopy?.rowVersion || null);
      setWorkingRevision(Number(workingCopy?.workingRevision || 0));
      dirtyRef.current = false;
      setDirty(false);
      if (!silent) {
        setNotice(workingCopy
          ? 'The shared PM working plan is synchronized with FlowHive.'
          : 'No FlowHive working copy has been saved for this project yet.');
      }
    } catch (loadError) {
      if (loadError?.name === 'AbortError') return;
      if (!silent) setError(loadError.message || 'The shared FlowHive working plan could not be loaded.');
    } finally {
      if (!silent && requestSequence === requestSequenceRef.current) setLoading(false);
    }
  }

  useEffect(() => {
    projectIdRef.current = projectId;
    loadAbortRef.current?.abort();
    editRevisionRef.current += 1;
    dirtyRef.current = false;
    setDirty(false);
    void loadSharedWorkspace(projectId);
  }, [projectId]);

  useEffect(() => {
    if (!projectId) return undefined;
    const interval = window.setInterval(() => {
      if (!dirtyRef.current) void loadSharedWorkspace(projectId, { silent: true });
    }, 30000);
    return () => window.clearInterval(interval);
  }, [projectId]);

  useEffect(() => () => loadAbortRef.current?.abort(), []);

  const executableTasks = useMemo(
    () => (plan?.tasks || []).filter((task) => !task.isSummary),
    [plan]
  );
  const canManage = Boolean(workspace?.access?.canManage);

  function markDirty(message) {
    loadAbortRef.current?.abort();
    editRevisionRef.current += 1;
    dirtyRef.current = true;
    setDirty(true);
    setNotice(message);
  }

  function changeTask(wbsNumber, patch) {
    setPlan((current) => {
      if (!current) return current;
      return {
        ...current,
        tasks: current.tasks.map((task) => String(task.wbsNumber) === String(wbsNumber) ? { ...task, ...patch } : task)
      };
    });
    markDirty('Unsaved shared-plan changes are pending.');
  }

  function changeTaskPhase(task, phaseWbs) {
    setPlan((current) => moveFlowHiveTask(current, task.wbsNumber, '', phaseWbs, 'after'));
    markDirty('Task moved to a different FlowHive phase. Save the shared PM working plan to synchronize it.');
  }

  function addTask() {
    setPlan((current) => addFlowHiveTask(current, phaseForNewTask, createTask));
    markDirty(`A new ${PHASES.find((phase) => phase.wbs === phaseForNewTask)?.name || 'project'} task was added.`);
  }

  function deleteTask(task) {
    if (!window.confirm(`Delete ${task.wbsNumber} · ${task.name}? Dependencies and assignments tied to this task will be repaired or removed.`)) return;
    setPlan((current) => deleteFlowHiveTask(current, task.wbsNumber));
    markDirty('The task was removed and the shared WBS was renumbered.');
  }

  function moveTask(task, offset) {
    setPlan((current) => moveFlowHiveTaskByOffset(current, task.wbsNumber, offset));
    markDirty('Task order changed. Save the shared PM working plan to synchronize it.');
  }

  async function refreshSharedPlan() {
    if (dirtyRef.current && !window.confirm('Discard unsaved Project Forge edits and reload the latest shared FlowHive working copy?')) return;
    editRevisionRef.current += 1;
    dirtyRef.current = false;
    setDirty(false);
    await loadSharedWorkspace(projectId);
  }

  async function saveSharedPlan() {
    if (!projectId || !plan || !canManage) return;
    setSaving(true);
    setError('');
    setNotice('');
    loadAbortRef.current?.abort();
    try {
      const normalizedPlan = renumberFlowHivePlan(plan);
      const result = await requestJson(`/api/project-flowhive/projects/${projectId}/working-copy`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          plan: normalizedPlan,
          expectedRowVersion: rowVersion || null
        })
      });
      if (String(projectIdRef.current) !== String(projectId)) return;
      setPlan(normalizedPlan);
      setRowVersion(result?.rowVersion || null);
      setWorkingRevision(Number(result?.workingRevision || workingRevision + 1));
      dirtyRef.current = false;
      setDirty(false);
      setNotice('Shared PM working plan saved. FlowHive and Project Forge now reference this same revision.');
      window.dispatchEvent(new CustomEvent('projectpulse:flowhive-working-copy-saved', {
        detail: { projectId, workingRevision: result?.workingRevision, rowVersion: result?.rowVersion }
      }));
    } catch (saveError) {
      if (saveError.status === 409) {
        setError('The shared plan changed after Project Forge loaded it. Refresh the shared plan before saving again.');
      } else {
        setError(saveError.message || 'The shared PM working plan could not be saved.');
      }
    } finally {
      setSaving(false);
    }
  }

  function openFlowHive() {
    if (projectId) window.sessionStorage.setItem(REQUESTED_PROJECT_KEY, projectId);
    window.location.hash = '#project-flowhive';
    window.setTimeout(selectRequestedFlowHiveProject, 0);
  }

  if (!host) return null;

  return createPortal(
    <section className={`forge-flowhive-sync ${expanded ? 'is-expanded' : ''}`} aria-label="Project Forge and FlowHive synchronized working plan">
      <header className="forge-flowhive-sync__header">
        <div>
          <span>MODULE 033 + MODULE 066</span>
          <h3>Project Forge + FlowHive synchronized PM workspace</h3>
          <p>Both modules read and save the same PM-owned working-copy revision for the selected project. Canonical project tasks remain unchanged until a reviewed action explicitly adopts or versions the plan.</p>
        </div>
        <div className="forge-flowhive-sync__header-actions">
          <button type="button" onClick={() => void refreshSharedPlan()} disabled={!projectId || loading || saving}>{loading ? 'Refreshing…' : 'Refresh shared plan'}</button>
          <button type="button" onClick={() => setExpanded((value) => !value)} aria-expanded={expanded}>{expanded ? 'Collapse' : 'Expand'}</button>
        </div>
      </header>

      {error ? <div className="forge-flowhive-sync__message is-error" role="alert">{error}</div> : null}
      {notice ? <div className="forge-flowhive-sync__message is-success" role="status">{notice}</div> : null}

      {expanded ? (
        <div className="forge-flowhive-sync__body">
          {!projectId ? (
            <p className="forge-flowhive-sync__empty">Select a project in Project Forge to load its shared FlowHive PM working plan.</p>
          ) : loading && !workspace ? (
            <p className="forge-flowhive-sync__empty">Loading the shared PM working plan…</p>
          ) : !plan ? (
            <div className="forge-flowhive-sync__empty">
              <strong>No shared PM working plan exists for this project.</strong>
              <span>Create or reset a FlowHive draft, then use Save working copy. Project Forge will load the same revision here.</span>
              <button type="button" onClick={openFlowHive}>Open Project FlowHive</button>
            </div>
          ) : (
            <>
              <div className="forge-flowhive-sync__summary">
                <div><span>Project</span><strong>{workspace?.project?.projectCode || plan.projectCode || 'Selected project'}</strong></div>
                <div><span>Working revision</span><strong>{workingRevision || 'Unsaved'}</strong></div>
                <div><span>Tasks</span><strong>{executableTasks.length}</strong></div>
                <div><span>Owner</span><strong>{workspace?.project?.projectManagerName || 'Unassigned'}</strong></div>
                <div><span>Persistence</span><strong>{dirty ? 'Unsaved changes' : 'Synchronized'}</strong></div>
              </div>

              <div className="forge-flowhive-sync__toolbar">
                <label>
                  Add task to phase
                  <select value={phaseForNewTask} onChange={(event) => setPhaseForNewTask(event.target.value)} disabled={!canManage || saving}>
                    {PHASES.map((phase) => <option key={phase.wbs} value={phase.wbs}>{phase.name}</option>)}
                  </select>
                </label>
                <button type="button" onClick={addTask} disabled={!canManage || saving}>Add task</button>
                <button type="button" className="is-primary" onClick={saveSharedPlan} disabled={!canManage || !dirty || saving}>{saving ? 'Saving…' : 'Save shared PM working plan'}</button>
                <button type="button" onClick={openFlowHive}>Open FlowHive</button>
                {!canManage ? <span className="forge-flowhive-sync__readonly">Read-only: only the assigned Project Manager or governed administrator can save this project.</span> : null}
              </div>

              <div className="forge-flowhive-sync__table-wrap">
                <table className="forge-flowhive-sync__table">
                  <thead>
                    <tr>
                      <th>WBS</th>
                      <th>Phase</th>
                      <th>Task</th>
                      <th>Start constraint</th>
                      <th>Duration</th>
                      <th>Progress</th>
                      <th>Status</th>
                      <th>Actions</th>
                    </tr>
                  </thead>
                  <tbody>
                    {executableTasks.map((task) => (
                      <tr key={task.clientTaskId || task.canonicalTaskId || task.wbsNumber}>
                        <td><strong>{task.wbsNumber}</strong></td>
                        <td>
                          <select value={String(task.parentWbsNumber || '3')} onChange={(event) => changeTaskPhase(task, event.target.value)} disabled={!canManage || saving}>
                            {PHASES.map((phase) => <option key={phase.wbs} value={phase.wbs}>{phase.name}</option>)}
                          </select>
                        </td>
                        <td><input value={task.name || ''} onChange={(event) => changeTask(task.wbsNumber, { name: event.target.value })} disabled={!canManage || saving} /></td>
                        <td><input type="date" value={task.constraintDate || ''} onChange={(event) => changeTask(task.wbsNumber, { constraintDate: event.target.value || null, constraintType: event.target.value ? 'SNET' : 'ASAP' })} disabled={!canManage || saving} /></td>
                        <td><input type="number" min="0" step="1" value={Number(task.durationWorkingDays || 0)} onChange={(event) => changeTask(task.wbsNumber, { durationWorkingDays: Math.max(0, Number(event.target.value || 0)) })} disabled={!canManage || saving} /></td>
                        <td><input type="number" min="0" max="100" step="1" value={Number(task.percentComplete || 0)} onChange={(event) => changeTask(task.wbsNumber, { percentComplete: Math.max(0, Math.min(100, Number(event.target.value || 0))) })} disabled={!canManage || saving} /></td>
                        <td>
                          <select value={task.status || 'not_started'} onChange={(event) => changeTask(task.wbsNumber, { status: event.target.value })} disabled={!canManage || saving}>
                            {STATUS_OPTIONS.map((status) => <option key={status} value={status}>{displayStatus(status)}</option>)}
                          </select>
                        </td>
                        <td>
                          <div className="forge-flowhive-sync__row-actions">
                            <button type="button" title="Move task up" onClick={() => moveTask(task, -1)} disabled={!canManage || saving}>↑</button>
                            <button type="button" title="Move task down" onClick={() => moveTask(task, 1)} disabled={!canManage || saving}>↓</button>
                            <button type="button" className="is-danger" onClick={() => deleteTask(task)} disabled={!canManage || saving}>Delete</button>
                          </div>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </>
          )}
        </div>
      ) : null}
    </section>,
    host
  );
}
