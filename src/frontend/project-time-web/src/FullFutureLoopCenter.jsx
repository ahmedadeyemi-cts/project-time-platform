import { useCallback, useEffect, useMemo, useState } from 'react';
import FullFutureLoopAutomationCenter from './FullFutureLoopAutomationCenter.jsx';
import './full-future-loop-center.css';

const BASE = '/api/full-future-loop';

const ACTION_LABELS = Object.freeze({
  approve_governance: 'Approve STEER-IT packet',
  complete_private_build: 'Complete private build',
  run_canary_pass: 'Run passing canary',
  run_canary_fail: 'Run failing canary',
  retry_canary: 'Prepare canary retry',
  promote_sandbox: 'Promote to sandbox production',
  record_production_signal: 'Record production signal',
  relay_repair_issue: 'Relay private repair issue',
  complete_repair: 'Complete review and fix',
  run_repair_canary_pass: 'Run passing repair canary',
  run_repair_canary_fail: 'Run failing repair canary',
  retry_repair_canary: 'Prepare repair canary retry',
  promote_again: 'Promote repair again',
  verify_close: 'Verify and close loop'
});

const STAGE_RANK = Object.freeze({
  governance_pending: 0,
  private_development: 1,
  canary_ready: 2,
  canary_failed: 2,
  promotion_ready: 3,
  sandbox_production: 4,
  production_signal: 5,
  repair_open: 6,
  repair_canary_ready: 7,
  repair_canary_failed: 7,
  repromotion_ready: 8,
  sandbox_repromoted: 9,
  verified_closed: 10
});

const NODES = Object.freeze([
  { id: 'steer', area: 'steer', rank: 0, tone: 'governance', eyebrow: 'Selective governance', title: 'STEER-IT', description: 'Decision packets for major, complex, architecture, and security changes.', items: ['Major changes', 'Complex decisions', 'Architecture shifts', 'Security shifts'] },
  { id: 'private', area: 'private', rank: 1, tone: 'build', eyebrow: 'Private engineering source', title: 'Private Dev Repo', description: 'Plans, branches, pull requests, CI, reviews, approvals, internal evidence, and repair work.', items: ['Plans & roadmaps', 'Branches and CI', 'Review and approvals', 'Private repair issues'] },
  { id: 'promotion', area: 'promotion', rank: 3, tone: 'build', eyebrow: 'Curated release gate', title: 'Promotion / Production Project', description: 'A reproducible, private-data-free release manifest prepared for governed promotion.', items: ['Curated and validated', 'Private data removed', 'Reproducible build', 'Human-authorized'] },
  { id: 'production', area: 'production', rank: 4, tone: 'governance', eyebrow: 'Sandbox production source', title: 'Public / Prod Repo', description: 'Readable release identity, documentation, version history, packages, and running application evidence.', items: ['Curated source identity', 'Release and tags', 'Packages and artifacts', 'Running application'] },
  { id: 'agent', area: 'agent', rank: 5, tone: 'support', eyebrow: 'Read-only support', title: 'Agent Keep', description: 'Explains the loop, guides users, and opens governed support issues without private-source or deployment access.', items: ['Understands intent', 'Reads approved evidence', 'Guides users', 'Opens support issues'] },
  { id: 'evidence', area: 'evidence', rank: 5, tone: 'support', eyebrow: 'Read-only operational record', title: 'Production Evidence', description: 'Logs, telemetry summaries, release identity, and normalized user-reported signals.', items: ['Logs and telemetry', 'Release identity', 'Health observations', 'Public user signals'] },
  { id: 'watcher', area: 'watcher', rank: 5, tone: 'build', eyebrow: 'Issue normalization', title: 'Watcher / Issue Relay', description: 'Observes approved evidence and relays normalized signals into the private repair boundary.', items: ['Observe', 'Normalize', 'Deduplicate', 'Relay privately'] },
  { id: 'repair', area: 'repair', rank: 6, tone: 'build', eyebrow: 'Private repair boundary', title: 'Private Repair Issue', description: 'A private, evidence-linked issue for triage, repair, review, and closure.', items: ['Triage', 'Create repair', 'Preserve evidence', 'No public source leakage'] },
  { id: 'fix', area: 'fix', rank: 7, tone: 'build', eyebrow: 'Review and repair', title: 'Review & Fix', description: 'Review the repair, implement the fix, test it, update evidence, and prepare it for canary validation.', items: ['Verify reviews', 'Implement fix', 'Run tests', 'Update evidence'] },
  { id: 'again', area: 'again', rank: 8, tone: 'build', eyebrow: 'Curated repair release', title: 'Promote Again', description: 'The repair follows the same governed release gates before re-promotion.', items: ['No repair shortcut', 'Re-run gates', 'New release identity', 'Rollback retained'] },
  { id: 'canary', area: 'canary', rank: 2, tone: 'governance', eyebrow: 'Disposable verification', title: 'Verify via Canary Runs', description: 'Isolated throw-away verification with seeded scenarios, acceptance criteria, results, evidence, and cleanup.', items: ['Isolated test', 'Seed scenario', 'Verify outcomes', 'Clean up'] }
]);

function authHeaders(authSession, json = false) {
  const token = authSession?.sessionToken || authSession?.token || authSession?.accessToken || '';
  return {
    ...(token ? { Authorization: `Bearer ${token}`, 'X-ProjectPulse-Session': token } : {}),
    'X-ProjectPulse-Module-Number': '083',
    ...(json ? { 'Content-Type': 'application/json' } : {})
  };
}

async function responseBody(response) {
  const text = await response.text();
  let value = {};
  try { value = text ? JSON.parse(text) : {}; } catch { value = { message: text }; }
  if (!response.ok) {
    const message = value.message || value.code || `Request failed (${response.status}).`;
    throw new Error(value.correlationId ? `${message} Reference ${value.correlationId}.` : message);
  }
  return value;
}

function humanize(value) {
  return String(value || 'Not recorded').replaceAll('_', ' ');
}

function formatDate(value) {
  if (!value) return 'Not recorded';
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? String(value) : date.toLocaleString();
}

function statusForNode(node, loop, artifacts) {
  if (!loop) return 'pending';
  if (node.id === 'agent') return artifacts.some((item) => item.artifactType === 'agent_keep_interaction') ? 'complete' : STAGE_RANK[loop.currentStage] >= node.rank ? 'available' : 'pending';
  if (node.id === 'canary') {
    if (loop.lastCanaryStatus === 'failed') return 'attention';
    if (loop.lastCanaryStatus === 'passed') return STAGE_RANK[loop.currentStage] <= 3 ? 'complete' : 'available';
  }
  const rank = STAGE_RANK[loop.currentStage] ?? -1;
  if (rank > node.rank) return 'complete';
  if (rank === node.rank) return loop.currentStatus === 'attention_required' ? 'attention' : 'current';
  return 'pending';
}

function NodeCard({ node, loop, artifacts, selected, onSelect }) {
  const state = statusForNode(node, loop, artifacts);
  return <button type="button" className={`ffl-node is-${node.tone} state-${state}${selected ? ' selected' : ''}`} style={{ gridArea: node.area }} onClick={() => onSelect(node.id)}>
    <span className="ffl-node-state" aria-label={humanize(state)}>{state === 'complete' ? '✓' : state === 'attention' ? '!' : state === 'current' ? '●' : state === 'available' ? '◉' : '○'}</span>
    <span className="ffl-node-copy"><small>{node.eyebrow}</small><strong>{node.title}</strong><span>{node.description}</span></span>
    <span className="ffl-node-items">{node.items.map((item) => <span key={item}>{item}</span>)}</span>
  </button>;
}

function Kpi({ label, value, detail, attention = false }) {
  return <article className={`ffl-kpi${attention ? ' is-attention' : ''}`}><span>{label}</span><strong>{value ?? '—'}</strong><small>{detail}</small></article>;
}

function Notice({ error, message, onRetry }) {
  if (!error && !message) return null;
  return <div className={`ffl-notice ${error ? 'is-error' : 'is-success'}`} role={error ? 'alert' : 'status'}><span>{error || message}</span>{error && onRetry ? <button type="button" onClick={onRetry}>Retry</button> : null}</div>;
}

export default function FullFutureLoopCenter({ authSession }) {
  const [access, setAccess] = useState(null);
  const [summary, setSummary] = useState(null);
  const [loops, setLoops] = useState([]);
  const [selectedId, setSelectedId] = useState('');
  const [detail, setDetail] = useState(null);
  const [selectedNode, setSelectedNode] = useState('steer');
  const [busy, setBusy] = useState('');
  const [error, setError] = useState('');
  const [message, setMessage] = useState('');
  const [createOpen, setCreateOpen] = useState(false);
  const [createForm, setCreateForm] = useState({ title: 'Full Future Loop Sandbox Test', description: 'Validate the complete governed development, promotion, evidence, support, repair, and verification lifecycle.', changeType: 'major', selectiveGovernance: true });
  const [agentQuestion, setAgentQuestion] = useState('What is the current status and next governed action?');
  const [agentAnswer, setAgentAnswer] = useState('');

  const request = useCallback(async (path, options = {}) => responseBody(await fetch(`${BASE}${path}`, {
    ...options,
    cache: 'no-store',
    headers: { ...authHeaders(authSession, Boolean(options.body)), ...(options.headers || {}) }
  })), [authSession]);

  const loadDetail = useCallback(async (loopId) => {
    if (!loopId) { setDetail(null); return; }
    const result = await request(`/loops/${loopId}`);
    setDetail(result);
  }, [request]);

  const refresh = useCallback(async (preferredId = '') => {
    setBusy('refresh'); setError('');
    try {
      const accessBody = await request('/access');
      setAccess(accessBody);
      if (!accessBody.dataReady) {
        setSummary(null); setLoops([]); setDetail(null);
        setError(accessBody.message || 'Module 083 data foundations are not ready.');
        return;
      }
      const [summaryBody, loopsBody] = await Promise.all([request('/summary'), request('/loops?limit=200')]);
      setSummary(summaryBody);
      const values = Array.isArray(loopsBody.loops) ? loopsBody.loops : [];
      setLoops(values);
      const target = preferredId || selectedId || values[0]?.loopId || '';
      setSelectedId(target);
      if (target) await loadDetail(target); else setDetail(null);
    } catch (caught) {
      setError(caught.message);
    } finally {
      setBusy('');
    }
  }, [loadDetail, request, selectedId]);

  useEffect(() => { void refresh(); }, [refresh]);

  const loop = detail?.loop || loops.find((item) => item.loopId === selectedId) || null;
  const artifacts = Array.isArray(detail?.artifacts) ? detail.artifacts : [];
  const events = Array.isArray(detail?.events) ? detail.events : [];
  const permissions = access?.permissions || detail?.permissions || {};
  const kpis = summary?.kpis || {};
  const selectedNodeData = NODES.find((node) => node.id === selectedNode) || NODES[0];
  const selectedArtifacts = useMemo(() => artifacts.filter((artifact) => {
    const types = {
      steer: ['intent_packet', 'decision_packet'], private: ['private_build'], promotion: ['release_manifest'], production: ['release_manifest', 'verification_report'],
      agent: ['agent_keep_interaction', 'support_issue'], evidence: ['production_evidence'], watcher: ['private_repair_issue'], repair: ['private_repair_issue'], fix: ['repair_resolution'], again: ['release_manifest'], canary: ['canary_run', 'canary_control']
    };
    return (types[selectedNode] || []).includes(artifact.artifactType);
  }), [artifacts, selectedNode]);

  async function createLoop(event) {
    event.preventDefault(); setBusy('create'); setError(''); setMessage('');
    try {
      const result = await request('/loops', { method: 'POST', body: JSON.stringify(createForm) });
      setCreateOpen(false);
      setMessage('The sandbox work item was created with its intent evidence.');
      await refresh(result.loop?.loopId || '');
    } catch (caught) { setError(caught.message); } finally { setBusy(''); }
  }

  async function runAction(action) {
    if (!loop) return;
    setBusy(action); setError(''); setMessage('');
    try {
      await request(`/loops/${loop.loopId}/actions`, { method: 'POST', body: JSON.stringify({ action, expectedRevision: loop.revision, notes: 'Executed from the Module 083 interactive test workspace.' }) });
      setMessage(`${ACTION_LABELS[action] || humanize(action)} completed with append-only evidence.`);
      await refresh(loop.loopId);
    } catch (caught) { setError(caught.message); } finally { setBusy(''); }
  }

  async function runFullLoop() {
    if (!loop) return;
    setBusy('full'); setError(''); setMessage('');
    try {
      const result = await request(`/loops/${loop.loopId}/run-full-sandbox`, { method: 'POST' });
      setMessage(`Complete sandbox loop finished. ${result.actionsExecuted?.length || 0} governed transitions were recorded.`);
      await refresh(loop.loopId);
    } catch (caught) { setError(caught.message); } finally { setBusy(''); }
  }

  async function resetLoop() {
    if (!loop || !window.confirm('Reset this sandbox for another test iteration? Existing evidence will remain immutable.')) return;
    setBusy('reset'); setError(''); setMessage('');
    try {
      await request(`/loops/${loop.loopId}/reset`, { method: 'POST' });
      setMessage('The sandbox was reset. Earlier evidence remains available in history.');
      await refresh(loop.loopId);
    } catch (caught) { setError(caught.message); } finally { setBusy(''); }
  }

  async function askAgent(openSupportIssue = false) {
    if (!loop) return;
    setBusy('agent'); setError(''); setAgentAnswer('');
    try {
      const result = await request(`/loops/${loop.loopId}/agent-keep`, { method: 'POST', body: JSON.stringify({ question: agentQuestion, openSupportIssue }) });
      setAgentAnswer(result.answer || 'Agent Keep completed without an answer body.');
      await loadDetail(loop.loopId);
    } catch (caught) { setError(caught.message); } finally { setBusy(''); }
  }

  return <main className="ffl-center" data-module="083" data-module-name="Full Future Loop" data-contract="083-full-future-loop-sandbox-v1">
    <header className="ffl-hero">
      <div className="ffl-hero-copy"><p>Module 083 · Governed platform operations</p><h1>Full Future Loop</h1><h2>How the systems work together</h2><span>Selective governance · private development · clean promotion · read-only support · repair and verification loop</span></div>
      <div className="ffl-hero-actions"><span className="ffl-safety-badge">Safe persistent sandbox</span><button type="button" className="secondary" disabled={Boolean(busy)} onClick={() => void refresh()}>Refresh</button><button type="button" className="primary" disabled={!permissions.canRunSandbox || Boolean(busy)} onClick={() => setCreateOpen(true)}>Create test loop</button></div>
    </header>

    {access?.scope?.isViewAs ? <div className="ffl-view-as">View-As is active. Module 083 remains read-only until you exit the preview.</div> : null}
    <Notice error={error} message={message} onRetry={() => void refresh()} />
    {busy ? <div className="ffl-progress" aria-label="Module 083 action in progress"><span /></div> : null}

    <section className="ffl-kpis" aria-label="Full Future Loop summary">
      <Kpi label="Total loops" value={kpis.totalLoops} detail="persistent sandbox records" />
      <Kpi label="Active" value={kpis.activeLoops} detail="still moving through gates" />
      <Kpi label="Attention" value={kpis.attentionRequired} detail="signal, repair, or failed canary" attention={Number(kpis.attentionRequired) > 0} />
      <Kpi label="Verified closed" value={kpis.verifiedClosed} detail="full evidence chain complete" />
      <Kpi label="Test iterations" value={kpis.testIterations} detail="resets preserve prior evidence" />
    </section>

    <section className="ffl-workspace">
      <aside className="ffl-loop-list" aria-label="Full Future Loop test work items">
        <header><div><small>Sandbox inventory</small><h2>Test loops</h2></div><button type="button" onClick={() => setCreateOpen(true)} disabled={!permissions.canRunSandbox}>+</button></header>
        {!loops.length ? <div className="ffl-empty"><strong>No test loops yet</strong><span>Create one to exercise the complete lifecycle.</span></div> : loops.map((item) => <button type="button" key={item.loopId} className={item.loopId === selectedId ? 'selected' : ''} onClick={() => { setSelectedId(item.loopId); void loadDetail(item.loopId); }}><span><strong>{item.loopNumber}</strong><small>{humanize(item.currentStage)}</small></span><b>{item.title}</b><em>{item.currentStatus === 'attention_required' ? 'Attention' : item.currentStatus === 'closed' ? 'Closed' : `Iteration ${item.iteration}`}</em></button>)}
      </aside>

      <div className="ffl-main">
        <section className="ffl-loop-header">
          <div><p>{loop?.loopNumber || 'Select or create a sandbox loop'}</p><h2>{loop?.title || 'Full Future Loop test workspace'}</h2><span>{loop?.description || 'The interactive operating map will activate after a test loop is selected.'}</span></div>
          <div className="ffl-loop-meta"><span>Stage <strong>{humanize(loop?.currentStage)}</strong></span><span>Status <strong>{humanize(loop?.currentStatus)}</strong></span><span>Revision <strong>{loop?.revision ?? '—'}</strong></span><span>Environment <strong>{loop?.environment || 'sandbox'}</strong></span></div>
          <div className="ffl-loop-actions"><button type="button" className="primary" disabled={!loop || !permissions.canRunSandbox || Boolean(busy) || !['governance_pending', 'private_development'].includes(loop.currentStage)} onClick={() => void runFullLoop()}>Run complete loop</button><button type="button" disabled={!loop || !permissions.canReset || Boolean(busy)} onClick={() => void resetLoop()}>Reset iteration</button></div>
        </section>

        <section className="ffl-board" aria-label="Interactive Full Future Loop system map">
          {NODES.map((node) => <NodeCard key={node.id} node={node} loop={loop} artifacts={artifacts} selected={selectedNode === node.id} onSelect={setSelectedNode} />)}
          <div className="ffl-flow flow-a" aria-hidden="true">→</div><div className="ffl-flow flow-b" aria-hidden="true">→</div><div className="ffl-flow flow-c" aria-hidden="true">→</div><div className="ffl-flow flow-d" aria-hidden="true">↓</div><div className="ffl-flow flow-e" aria-hidden="true">←</div>
        </section>

        <section className="ffl-action-strip">
          <div><small>Current governed gate</small><h3>{humanize(loop?.currentStage)}</h3><p>{loop?.nextActions?.length ? 'Choose one of the permitted state-machine actions or run the complete deterministic sandbox demonstration.' : loop ? 'This loop has reached a terminal verified state.' : 'Select a loop to see available actions.'}</p></div>
          <div>{(loop?.nextActions || []).map((action) => <button type="button" key={action} className={action.includes('fail') ? 'danger' : 'primary'} disabled={!permissions.canRunSandbox || Boolean(busy)} onClick={() => void runAction(action)}>{ACTION_LABELS[action] || humanize(action)}</button>)}</div>
        </section>

        <section className="ffl-detail-grid">
          <article className="ffl-detail-panel">
            <header><div><small>Selected system area</small><h3>{selectedNodeData.title}</h3></div><span className={`ffl-state state-${statusForNode(selectedNodeData, loop, artifacts)}`}>{humanize(statusForNode(selectedNodeData, loop, artifacts))}</span></header>
            <p>{selectedNodeData.description}</p>
            <div className="ffl-artifacts"><h4>Evidence</h4>{selectedArtifacts.length ? selectedArtifacts.slice().reverse().map((artifact) => <article key={artifact.artifactId}><span>{humanize(artifact.artifactType)}</span><strong>{artifact.title}</strong><p>{artifact.summary}</p><small>{humanize(artifact.status)} · {formatDate(artifact.createdAt)}</small></article>) : <div className="ffl-empty compact">No evidence has reached this area yet.</div>}</div>
          </article>

          <article className="ffl-detail-panel ffl-agent-panel">
            <header><div><small>Read-only support and guidance</small><h3>Agent Keep</h3></div><span className="ffl-state state-available">No private source access</span></header>
            <label>Ask about the selected loop<textarea rows="4" value={agentQuestion} onChange={(event) => setAgentQuestion(event.target.value)} placeholder="Ask about status, evidence, next action, or support guidance." /></label>
            <div className="ffl-agent-actions"><button type="button" className="primary" disabled={!loop || !permissions.canUseAgentKeep || Boolean(busy) || agentQuestion.trim().length < 2} onClick={() => void askAgent(false)}>Ask Agent Keep</button><button type="button" disabled={!loop || !permissions.canUseAgentKeep || access?.scope?.isViewAs || Boolean(busy) || agentQuestion.trim().length < 2} onClick={() => void askAgent(true)}>Ask and open support issue</button></div>
            {agentAnswer ? <div className="ffl-agent-answer"><strong>Answer</strong><p>{agentAnswer}</p></div> : null}
          </article>
        </section>

        <section className="ffl-history">
          <header><div><small>Append-only lifecycle record</small><h3>Timeline and verification evidence</h3></div><span>{events.length} events · {artifacts.length} artifacts</span></header>
          {!events.length ? <div className="ffl-empty">Create or advance a loop to populate the immutable timeline.</div> : <div className="ffl-timeline">{events.slice().reverse().map((event) => <article key={event.eventId}><span className={`ffl-event-dot ${event.outcome}`} /><div><small>{formatDate(event.occurredAt)} · {humanize(event.eventCode)}</small><strong>{event.summary}</strong><p>{humanize(event.fromStage)} → {humanize(event.toStage)}</p></div><b>{humanize(event.outcome)}</b></article>)}</div>}
        </section>
      </div>
    </section>

    <FullFutureLoopAutomationCenter authSession={authSession} selectedLoopId={loop?.loopId || null} />

    <footer className="ffl-mission"><strong>Mission</strong><span>Move work items from intent to live verification with maximum autonomy under human authority and verifiable evidence.</span><em>No external mutation occurs in Module 083 sandbox or durable dry-run mode.</em></footer>

    {createOpen ? <div className="ffl-modal-backdrop" onMouseDown={(event) => event.target === event.currentTarget && setCreateOpen(false)}><form className="ffl-modal" onSubmit={createLoop}><header><div><small>Module 083</small><h2>Create a Full Future Loop test</h2><p>This creates a persistent sandbox record only. It does not create a branch, pull request, deployment, cloud resource, or production change.</p></div><button type="button" onClick={() => setCreateOpen(false)}>Close</button></header><label>Test title<input required maxLength="200" value={createForm.title} onChange={(event) => setCreateForm((current) => ({ ...current, title: event.target.value }))} /></label><label>Purpose and expected outcome<textarea rows="5" maxLength="4000" value={createForm.description} onChange={(event) => setCreateForm((current) => ({ ...current, description: event.target.value }))} /></label><div className="ffl-form-row"><label>Change classification<select value={createForm.changeType} onChange={(event) => setCreateForm((current) => ({ ...current, changeType: event.target.value }))}><option value="standard">Standard</option><option value="major">Major</option><option value="complex">Complex</option><option value="architecture">Architecture</option><option value="security">Security</option></select></label><label className="ffl-checkbox"><input type="checkbox" checked={createForm.selectiveGovernance} onChange={(event) => setCreateForm((current) => ({ ...current, selectiveGovernance: event.target.checked }))} /><span>Require STEER-IT governance</span></label></div><footer><button type="button" onClick={() => setCreateOpen(false)}>Cancel</button><button type="submit" className="primary" disabled={busy === 'create' || createForm.title.trim().length < 3}>{busy === 'create' ? 'Creating…' : 'Create sandbox loop'}</button></footer></form></div> : null}
  </main>;
}
