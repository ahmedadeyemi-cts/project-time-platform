import { useEffect, useRef, useState } from 'react';
import AiOperationProgress from './ai/AiOperationProgress.jsx';
import AiPhaseProgress from './ai/AiPhaseProgress.jsx';
import './project-flowhive-automation.css';

const labels = {
  disabled: 'Off', waiting_documents: 'Waiting for documents', waiting_start_date: 'Start date needed',
  waiting_pm: 'Project Manager needed', preparation_paused: 'Preparation paused', generating: 'Generating draft',
  ready_for_review: 'Ready for PM review', existing_plan: 'Existing plan preserved',
  cancelled: 'Cancelled', archived: 'Archived', needs_attention: 'Needs attention'
};

export default function ProjectFlowHiveAutomation({ projectId, getJson, putJson, onLoadDraft, onState }) {
  const [state, setState] = useState(null);
  const [error, setError] = useState('');
  const [saveError, setSaveError] = useState('');
  const [busy, setBusy] = useState(false);
  const [refresh, setRefresh] = useState(0);
  const epoch = useRef(0);
  const observedProject = useRef(null);
  const publish = useRef(onState);
  publish.current = onState;
  useEffect(() => {
    const current = ++epoch.current;
    const controller = new AbortController();
    let timer;
    if (observedProject.current !== projectId) {
      observedProject.current = projectId;
      setState(null); setError(''); setSaveError(''); setBusy(false); publish.current?.(null);
    }
    async function read() {
      let delay = 10000;
      try {
        const value = await getJson(`/api/project-flowhive/projects/${projectId}/ai-planner/automation`, controller.signal);
        if (controller.signal.aborted || current !== epoch.current) return;
        if (value.projectId !== projectId) throw new Error('The automatic plan status belongs to a different project.');
        setState(value); setError(''); publish.current?.(value);
        delay = value.status === 'generating' ? 3000 : 10000;
      } catch (failure) {
        if (controller.signal.aborted || current !== epoch.current) return;
        setState(null); publish.current?.(null);
        setError(failure.responseBody?.message || 'Automatic planning status is temporarily unavailable. Check again before changing settings.');
        delay = 30000;
      }
      if (!controller.signal.aborted) timer = window.setTimeout(read, delay);
    }
    read();
    return () => { epoch.current += 1; controller.abort(); window.clearTimeout(timer); };
  }, [projectId, getJson, refresh]);

  async function save(enabled, defaults = false) {
    if (busy || !state || !(defaults ? state.defaults?.canManage : state.canManage)) return;
    const current = ++epoch.current;
    setBusy(true); setSaveError('');
    try {
      const value = await putJson(`/api/project-flowhive/projects/${projectId}/ai-planner/automation${defaults ? '/default' : ''}`, {
        enabled, expectedVersion: defaults ? state.defaults.rowVersion : state.rowVersion
      });
      if (current !== epoch.current) return;
      if (value.projectId !== projectId) throw new Error('Project identity changed. Reload the setting.');
      setState(value); publish.current?.(value);
    } catch (failure) {
      if (current !== epoch.current) return;
      setSaveError(failure.responseBody?.message || failure.message || 'The setting could not be saved. Check again.');
    } finally {
      if (current === epoch.current) { setBusy(false); setRefresh(value => value + 1); }
    }
  }

  const running = state?.status === 'generating';
  return <section className="flowhive-automation" aria-label="AI Planner settings">
    <header><div><h3>AI Planner settings</h3><p>Prepare the first draft automatically, then review it with your project team.</p></div>
      <strong role="status">{state ? labels[state.status] || 'Checking status' : error ? 'Status unavailable' : 'Checking status…'}</strong>
    </header>
    {error && <p role="alert">{error}</p>}
    {saveError && <p role="alert">{saveError}</p>}
    <label className="flowhive-automation-toggle"><input type="checkbox" checked={Boolean(state?.enabled)}
      disabled={!state?.canManage || busy} onChange={event => save(event.target.checked)} />
      <span><strong>Automatically create the first AI plan</strong><small>Wait for the current SOW and supporting documents, an assigned PM and a project start date. Create one draft for review. Existing plans and edits are preserved.</small></span>
    </label>
    <p>{state?.message || 'Checking this project’s automatic planning setting.'}</p>
    {state && !state.canManage && <small>Your PM or an authorized administrator can change this setting.</small>}
    {running && <p>Turning this setting off stops this automatic run. Restarting a cancelled or failed run requires an explicit AI Planner action.</p>}
    {state?.runId && <>
      <AiOperationProgress title="Automatic AI plan" startedAt={state.createdAt} completedAt={state.completedAt}
        active={running} stage={labels[state.status]} />
      <AiPhaseProgress phases={state.phases} terminal={!running} completedAt={state.completedAt} />
    </>}
    <div className="flowhive-automation-actions">
      {state?.status === 'ready_for_review' && <button type="button" className="primary" onClick={onLoadDraft}>Review working draft</button>}
      <button type="button" disabled={busy} onClick={() => setRefresh(value => value + 1)}>Check automatic plan status</button>
      {busy && <span role="status">Saving setting…</span>}
    </div>
    {state?.defaults?.canManage && <details className="flowhive-automation-defaults"><summary>Administrator default for new projects</summary>
      <label className="flowhive-automation-toggle"><input type="checkbox" checked={Boolean(state.defaults.enabled)} disabled={busy}
        onChange={event => save(event.target.checked, true)} /><span><strong>Enable for new projects</strong>
        <small>Applies to projects created after this default is enabled. It does not opt existing projects in or change their individual settings. Turning the default off affects future enrollment only.</small></span></label>
      <p>A PM can turn automatic planning off for an individual project. Generation starts after its documents and scheduling information are ready.</p>
    </details>}
  </section>;
}
