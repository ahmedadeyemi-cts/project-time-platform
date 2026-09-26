import { useEffect, useRef, useState } from 'react';
import AiOperationProgress from './ai/AiOperationProgress.jsx';
import AiPhaseProgress from './ai/AiPhaseProgress.jsx';
import './project-flowhive-automation.css';

const labels = {
  disabled: 'Off', waiting_documents: 'Waiting for documents', waiting_start_date: 'Start date needed',
  waiting_pm: 'Project Manager needed', preparation_paused: 'Preparation paused', generating: 'Building your plan',
  ready_for_review: 'Ready for review', existing_plan: 'Existing plan preserved',
  cancelled: 'Cancelled', archived: 'Archived', needs_attention: 'Needs attention'
};

function statusTone(status) {
  if (status === 'ready_for_review') return 'ready';
  if (status === 'generating') return 'working';
  if (['waiting_documents', 'waiting_start_date', 'waiting_pm'].includes(status)) return 'waiting';
  if (['needs_attention', 'preparation_paused', 'cancelled'].includes(status)) return 'attention';
  return 'neutral';
}

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

  async function save(enabled, scope = 'project') {
    if (busy || !state) return;
    const target = scope === 'organization' ? state.defaults : scope === 'personal' ? state.myDefault : state;
    if (!(scope === 'project' ? state.canManage : target?.canManage)) return;
    const current = ++epoch.current;
    setBusy(true); setSaveError('');
    try {
      const suffix = scope === 'organization' ? '/default' : scope === 'personal' ? '/my-default' : '';
      const value = await putJson(`/api/project-flowhive/projects/${projectId}/ai-planner/automation${suffix}`, {
        enabled, expectedVersion: target?.rowVersion ?? null
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
  const tone = statusTone(state?.status);
  return <section className="flowhive-automation flowhive-automation-top" aria-label="Automatic planning">
    <header>
      <div><span className="flowhive-automation-kicker">Automatic planning</span><h3>Let FlowHive prepare the first draft</h3>
        <p>FlowHive waits for the current SOW, project documents, assigned PM, and start date. You review the draft before anything becomes a baseline.</p></div>
      <strong className={`flowhive-automation-status ${tone}`} role="status">{state ? labels[state.status] || 'Checking status' : error ? 'Status unavailable' : 'Checking…'}</strong>
    </header>

    {error && <p className="flowhive-automation-alert" role="alert">{error}</p>}
    {saveError && <p className="flowhive-automation-alert" role="alert">{saveError}</p>}

    <div className="flowhive-automation-choice-grid">
      <label className="flowhive-automation-toggle flowhive-automation-choice">
        <input type="checkbox" checked={Boolean(state?.enabled)}
          disabled={!state?.canManage || busy} onChange={event => save(event.target.checked, 'project')} />
        <span><strong>Automatically create the first AI plan for this project</strong>
          <small>Recommended when this project has a current SOW. Existing plans and edits are never replaced automatically.</small></span>
      </label>

      {state?.myDefault?.canManage ? <label className="flowhive-automation-toggle flowhive-automation-choice">
        <input type="checkbox" checked={Boolean(state.myDefault.enabled)}
          disabled={busy} onChange={event => save(event.target.checked, 'personal')} />
        <span><strong>Use automatic planning for new projects assigned to me</strong>
          <small>This is your personal PM preference. It applies only to future projects assigned to you and does not change another PM’s projects.</small></span>
      </label> : null}
    </div>

    <div className="flowhive-automation-next">
      <strong>{state?.message || 'Checking this project’s planning readiness.'}</strong>
      {state && !state.canManage ? <small>Your assigned PM, PM lead, or administrator can change this project setting.</small> : null}
      {running ? <small>You can leave this page while FlowHive works. The plan continues on the server.</small> : null}
    </div>

    {state?.runId ? <div className="flowhive-automation-progress">
      <AiOperationProgress title="Automatic AI plan" startedAt={state.createdAt} completedAt={state.completedAt}
        active={running} stage={labels[state.status]} />
      <AiPhaseProgress phases={state.phases} terminal={!running} completedAt={state.completedAt} />
    </div> : null}

    <div className="flowhive-automation-actions">
      {state?.status === 'ready_for_review' && <button type="button" className="primary" onClick={onLoadDraft}>Review working draft</button>}
      <button type="button" disabled={busy} onClick={() => setRefresh(value => value + 1)}>Refresh status</button>
      {busy && <span role="status">Saving…</span>}
    </div>

    {state?.defaults?.canManage ? <details className="flowhive-automation-defaults">
      <summary>Organization default for administrators</summary>
      <label className="flowhive-automation-toggle"><input type="checkbox" checked={Boolean(state.defaults.enabled)} disabled={busy}
        onChange={event => save(event.target.checked, 'organization')} /><span><strong>Enable automatic planning for new projects when no PM preference is set</strong>
        <small>This organization-wide fallback applies only to future projects. An individual PM preference takes precedence.</small></span></label>
    </details> : null}
  </section>;
}
