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

function storedSessionToken() {
  try {
    const value = JSON.parse(window.localStorage.getItem('projectPulseAuthSession') || '{}');
    if (value?.expiresAt && Date.now() >= Date.parse(value.expiresAt)) return '';
    return value?.sessionToken || value?.token || value?.accessToken || '';
  } catch {
    return '';
  }
}

async function requestJson(path, options = {}) {
  const token = storedSessionToken();
  const response = await fetch(path, {
    credentials: 'include',
    ...options,
    headers: {
      ...(token ? {
        Authorization: `Bearer ${token}`,
        'X-ProjectPulse-Session': token,
        'X-ProjectPulse-Module-Number': '033'
      } : {}),
      ...(options.headers || {})
    }
  });
  const body = (response.headers.get('content-type') || '').includes('application/json')
    ? await response.json()
    : null;
  if (!response.ok) {
    const error = new Error(body?.message || body?.detail || `${path} returned HTTP ${response.status}`);
    error.status = response.status;
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
    const hex = [...bytes].map((value) => value.toString(16).padStart(2, '0')).join('');
    return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
  }
  return `flowhive-${Date.now()}-${Math.random().toString(16).slice(2)}`;
}

function createTask(wbsNumber, parentWbsNumber, name, description) {
  return {
    clientTaskId: safeUuid(), canonicalTaskId: null, wbsNumber, parentWbsNumber, name, description,
    durationWorkingDays: 1, isMilestone: false, constraintType: 'ASAP', constraintDate: null,
    percentComplete: 0, remainingEffortHours: 8, status: 'not_started', isSummary: false,
    phase: PHASES.find((phase) => phase.wbs === String(parentWbsNumber))?.name || 'Implement',
    detailedSteps: [], inputs: [], outputs: [], acceptanceCriteria: [], validationSteps: [],
    customerResponsibilities: [], usSignalResponsibilities: [], prerequisites: [], risks: [],
    openQuestions: [], priority: 'normal', citationIds: [], comments: '', notes: ''
  };
}

function projectSelectFromForge() {
  return [...document.querySelectorAll('.project-forge .forge-header-controls label')]
    .find((label) => [...label.childNodes]
      .filter((node) => node.nodeType === Node.TEXT_NODE)
      .map((node) => String(node.textContent || '').trim())
      .filter(Boolean)
      .join(' ') === 'Project')
    ?.querySelector('select') || null;
}

function displayStatus(value) {
  return String(value || 'not started').replaceAll('_', ' ').replace(/\b\w/g, (letter) => letter.toUpperCase());
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
  const projectRef = useRef('');
  const dirtyRef = useRef(false);
  const editRevisionRef = useRef(0);
  const loadSequenceRef = useRef(0);
  const saveSequenceRef = useRef(0);

  const markDirty = (message) => {
    editRevisionRef.current += 1;
    dirtyRef.current = true;
    setDirty(true);
    setNotice(message);
  };
  const clearDirty = () => {
    dirtyRef.current = false;
    setDirty(false);
  };

  useEffect(() => {
    let activeSelect = null;
    function synchronize() {
      const onForge = String(window.location.hash || '').toLowerCase().includes('project-forge');
      if (!onForge) {
        setHost(null);
        return;
      }
      const forge = document.querySelector('.project-forge');
      if (!forge) return;
      let target = forge.querySelector('[data-project-forge-flowhive-sync-host]');
      if (!target) {
        target = document.createElement('div');
        target.dataset.projectForgeFlowhiveSyncHost = 'true';
        const anchor = forge.querySelector('.forge-workspace-banner');
        if (anchor?.parentElement) anchor.insertAdjacentElement('afterend', target);
        else forge.prepend(target);
      }
      setHost(target);
      const select = projectSelectFromForge();
      if (select !== activeSelect) {
        activeSelect?.removeEventListener('change', synchronize);
        activeSelect = select;
        activeSelect?.addEventListener('change', synchronize);
      }
      const nextProjectId = String(select?.value || '');
      if (nextProjectId !== projectRef.current) {
        projectRef.current = nextProjectId;
        loadSequenceRef.current += 1;
        editRevisionRef.current += 1;
        clearDirty();
        setProjectId(nextProjectId);
      }
    }
    const observer = new MutationObserver(synchronize);
    observer.observe(document.body, { childList: true, subtree: true });
    window.addEventListener('hashchange', synchronize);
    const interval = window.setInterval(synchronize, 1000);
    synchronize();
    return () => {
      observer.disconnect();
      window.removeEventListener('hashchange', synchronize);
      window.clearInterval(interval);
      activeSelect?.removeEventListener('change', synchronize);
    };
  }, []);

  async function loadSharedWorkspace(targetProjectId = projectId, options = {}) {
    const requestedProject = String(targetProjectId || '');
    if (!requestedProject) {
      loadSequenceRef.current += 1;
      setWorkspace(null); setPlan(null); setRowVersion(null); setWorkingRevision(0); clearDirty();
      return;
    }
    if (!options.silent && dirtyRef.current) {
      setNotice('Save the current shared-plan edits before refreshing from FlowHive.');
      return;
    }
    const sequence = ++loadSequenceRef.current;
    const startingRevision = editRevisionRef.current;
    if (!options.silent) { setLoading(true); setError(''); }
    try {
      const result = await requestJson(`/api/project-flowhive/projects/${requestedProject}/enterprise`);
      const current = sequence === loadSequenceRef.current
        && projectRef.current === requestedProject
        && editRevisionRef.current === startingRevision
        && !dirtyRef.current;
      if (!current) {
        if (!options.silent && projectRef.current === requestedProject) {
          setNotice('The refresh completed after local editing began, so the newer PM edits were preserved.');
        }
        return;
      }
      const workingCopy = result?.workingCopy || null;
      setWorkspace(result);
      setPlan(workingCopy?.plan ? clone(workingCopy.plan) : null);
      setRowVersion(workingCopy?.rowVersion || null);
      setWorkingRevision(Number(workingCopy?.workingRevision || 0));
      clearDirty();
      if (!options.silent) setNotice(workingCopy
        ? 'The shared PM working plan is synchronized with FlowHive.'
        : 'No FlowHive working copy has been saved for this project yet.');
    } catch (loadError) {
      if (sequence === loadSequenceRef.current && projectRef.current === requestedProject) {
        setError(loadError.message || 'The shared FlowHive working plan could not be loaded.');
      }
    } finally {
      if (!options.silent && sequence === loadSequenceRef.current) setLoading(false);
    }
  }

  useEffect(() => { void loadSharedWorkspace(projectId); }, [projectId]);
  useEffect(() => {
    if (!projectId || dirty) return undefined;
    const interval = window.setInterval(() => {
      if (!dirtyRef.current) void loadSharedWorkspace(projectId, { silent: true });
    }, 30000);
    return () => window.clearInterval(interval);
  }, [projectId, dirty]);

  const tasks = useMemo(() => (plan?.tasks || []).filter((task) => !task.isSummary), [plan]);
  const canManage = Boolean(workspace?.access?.canManage);
  const flowHiveHref = projectId
    ? `#project-flowhive?projectId=${encodeURIComponent(projectId)}`
    : '#project-flowhive';

  function updateTask(wbsNumber, patch) {
    markDirty('Unsaved shared-plan changes are pending.');
    setPlan((current) => current ? {
      ...current,
      tasks: current.tasks.map((task) => String(task.wbsNumber) === String(wbsNumber) ? { ...task, ...patch } : task)
    } : current);
  }
  function moveToPhase(task, phaseWbs) {
    markDirty('Task moved to another FlowHive phase.');
    setPlan((current) => moveFlowHiveTask(current, task.wbsNumber, '', phaseWbs, 'after'));
  }
  function addTask() {
    markDirty('A new shared-plan task was added.');
    setPlan((current) => addFlowHiveTask(current, phaseForNewTask, createTask));
  }
  function removeTask(task) {
    if (!window.confirm(`Delete ${task.wbsNumber} · ${task.name}?`)) return;
    markDirty('The task was removed and the shared WBS was renumbered.');
    setPlan((current) => deleteFlowHiveTask(current, task.wbsNumber));
  }
  function moveTask(task, offset) {
    markDirty('Task order changed.');
    setPlan((current) => moveFlowHiveTaskByOffset(current, task.wbsNumber, offset));
  }

  async function saveSharedPlan() {
    if (!projectId || !plan || !canManage) return;
    const requestedProject = projectId;
    const startingRevision = editRevisionRef.current;
    const sequence = ++saveSequenceRef.current;
    const normalizedPlan = renumberFlowHivePlan(plan);
    setSaving(true); setError(''); setNotice('');
    try {
      const result = await requestJson(`/api/project-flowhive/projects/${requestedProject}/working-copy`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ plan: normalizedPlan, expectedRowVersion: rowVersion || null })
      });
      if (sequence !== saveSequenceRef.current || projectRef.current !== requestedProject) return;
      setRowVersion(result?.rowVersion || null);
      setWorkingRevision(Number(result?.workingRevision || workingRevision + 1));
      if (editRevisionRef.current === startingRevision) {
        setPlan(normalizedPlan); clearDirty();
        setNotice('Shared PM working plan saved. FlowHive and Project Forge now reference this revision.');
      } else {
        setNotice('The submitted revision was saved; newer local edits remain unsaved.');
      }
      window.dispatchEvent(new CustomEvent('projectpulse:flowhive-working-copy-saved', {
        detail: { projectId: requestedProject, workingRevision: result?.workingRevision, rowVersion: result?.rowVersion }
      }));
    } catch (saveError) {
      if (sequence !== saveSequenceRef.current || projectRef.current !== requestedProject) return;
      setError(saveError.status === 409
        ? 'The shared plan changed after Project Forge loaded it. Refresh before saving again.'
        : saveError.message || 'The shared PM working plan could not be saved.');
    } finally {
      if (sequence === saveSequenceRef.current) setSaving(false);
    }
  }

  if (!host) return null;
  return createPortal(
    <section className={`forge-flowhive-sync ${expanded ? 'is-expanded' : ''}`} aria-label="Project Forge and FlowHive synchronized working plan">
      <header className="forge-flowhive-sync__header">
        <div><span>MODULE 033 + MODULE 066</span><h3>Project Forge + FlowHive synchronized PM workspace</h3><p>Both modules use the same PM-owned working-copy revision. Canonical project tasks remain unchanged until reviewed adoption or versioning.</p></div>
        <div className="forge-flowhive-sync__header-actions"><button type="button" onClick={() => void loadSharedWorkspace(projectId)} disabled={!projectId || loading || saving || dirty}>{loading ? 'Refreshing…' : 'Refresh shared plan'}</button><button type="button" onClick={() => setExpanded((value) => !value)}>{expanded ? 'Collapse' : 'Expand'}</button></div>
      </header>
      {error ? <div className="forge-flowhive-sync__message is-error" role="alert">{error}</div> : null}
      {notice ? <div className="forge-flowhive-sync__message is-success" role="status">{notice}</div> : null}
      {expanded ? <div className="forge-flowhive-sync__body">
        {!projectId ? <p className="forge-flowhive-sync__empty">Select a Project Forge project to load its FlowHive working plan.</p> : loading && !workspace ? <p className="forge-flowhive-sync__empty">Loading the shared PM working plan…</p> : !plan ? <div className="forge-flowhive-sync__empty"><strong>No shared PM working plan exists.</strong><span>Create and save a working copy in FlowHive first.</span><a href={flowHiveHref}>Open Project FlowHive</a></div> : <>
          <div className="forge-flowhive-sync__summary"><div><span>Project</span><strong>{workspace?.project?.projectCode || plan.projectCode}</strong></div><div><span>Working revision</span><strong>{workingRevision || 'Unsaved'}</strong></div><div><span>Tasks</span><strong>{tasks.length}</strong></div><div><span>Owner</span><strong>{workspace?.project?.projectManagerName || 'Unassigned'}</strong></div><div><span>Persistence</span><strong>{dirty ? 'Unsaved changes' : 'Synchronized'}</strong></div></div>
          <div className="forge-flowhive-sync__toolbar"><label>Add task to phase<select value={phaseForNewTask} onChange={(event) => setPhaseForNewTask(event.target.value)} disabled={!canManage || saving}>{PHASES.map((phase) => <option key={phase.wbs} value={phase.wbs}>{phase.name}</option>)}</select></label><button type="button" onClick={addTask} disabled={!canManage || saving}>Add task</button><button type="button" className="is-primary" onClick={saveSharedPlan} disabled={!canManage || !dirty || saving}>{saving ? 'Saving…' : 'Save shared PM working plan'}</button><a href={flowHiveHref}>Open FlowHive</a>{!canManage ? <span className="forge-flowhive-sync__readonly">Read-only for the current effective user.</span> : null}</div>
          <div className="forge-flowhive-sync__table-wrap"><table className="forge-flowhive-sync__table"><thead><tr><th>WBS</th><th>Phase</th><th>Task</th><th>Start constraint</th><th>Duration</th><th>Progress</th><th>Status</th><th>Actions</th></tr></thead><tbody>{tasks.map((task) => <tr key={task.clientTaskId || task.canonicalTaskId || task.wbsNumber}><td><strong>{task.wbsNumber}</strong></td><td><select value={String(task.parentWbsNumber || '3')} onChange={(event) => moveToPhase(task, event.target.value)} disabled={!canManage || saving}>{PHASES.map((phase) => <option key={phase.wbs} value={phase.wbs}>{phase.name}</option>)}</select></td><td><input value={task.name || ''} onChange={(event) => updateTask(task.wbsNumber, { name: event.target.value })} disabled={!canManage || saving} /></td><td><input type="date" value={task.constraintDate || ''} onChange={(event) => updateTask(task.wbsNumber, { constraintDate: event.target.value || null, constraintType: event.target.value ? 'SNET' : 'ASAP' })} disabled={!canManage || saving} /></td><td><input type="number" min="0" value={Number(task.durationWorkingDays || 0)} onChange={(event) => updateTask(task.wbsNumber, { durationWorkingDays: Math.max(0, Number(event.target.value || 0)) })} disabled={!canManage || saving} /></td><td><input type="number" min="0" max="100" value={Number(task.percentComplete || 0)} onChange={(event) => updateTask(task.wbsNumber, { percentComplete: Math.max(0, Math.min(100, Number(event.target.value || 0))) })} disabled={!canManage || saving} /></td><td><select value={task.status || 'not_started'} onChange={(event) => updateTask(task.wbsNumber, { status: event.target.value })} disabled={!canManage || saving}>{STATUS_OPTIONS.map((status) => <option key={status} value={status}>{displayStatus(status)}</option>)}</select></td><td><div className="forge-flowhive-sync__row-actions"><button type="button" onClick={() => moveTask(task, -1)} disabled={!canManage || saving}>↑</button><button type="button" onClick={() => moveTask(task, 1)} disabled={!canManage || saving}>↓</button><button type="button" className="is-danger" onClick={() => removeTask(task)} disabled={!canManage || saving}>Delete</button></div></td></tr>)}</tbody></table></div>
        </>}
      </div> : null}
    </section>,
    host
  );
}
