import { useCallback, useEffect, useMemo, useState } from 'react';
import './full-future-loop-automation-center.css';

const BASE = '/api/full-future-loop/automation';
const DEFAULT_REPOSITORY = 'ahmedadeyemi-cts/project-time-platform';
const TABS = Object.freeze([
  ['overview', 'Readiness'],
  ['simulate', 'Policy Simulator'],
  ['adapters', 'Adapters'],
  ['runs', 'Runs & Manifests'],
  ['approvals', 'Approvals'],
  ['evidence', 'Evidence']
]);
const OPERATIONS = Object.freeze(['observe', 'classify', 'create_issue', 'dispatch_ci', 'run_canary', 'deploy', 'verify', 'rollback', 'notify', 'propose_repair']);
const EMPTY_SIMULATION = Object.freeze({
  operation: 'deploy',
  environment: 'test',
  repository: DEFAULT_REPOSITORY,
  sourceCommit: '',
  riskClass: 'normal',
  changeType: 'application',
  includesMigration: false,
  includesSecurityChange: false,
  includesInfrastructureChange: false,
  includesSecretChange: false,
  isEmergencyRollback: false,
  productionApprovalSatisfied: false,
  migrationApprovalSatisfied: false,
  securityApprovalSatisfied: false,
  infrastructureApprovalSatisfied: false,
  secretChangeApprovalSatisfied: false,
  canaryPassed: true,
  cleanupProven: true,
  verificationSuitePassed: true,
  rollbackTargetProven: true,
  exactArtifactDigestsPresent: true,
  sbomPresent: true,
  provenancePresent: true,
  signaturesVerified: true,
  requestedByAi: false,
  assumePolicyEnabled: true,
  assumeKillSwitchReleased: true
});

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
    const error = new Error(value.message || value.code || `Request failed (${response.status}).`);
    error.status = response.status;
    error.payload = value;
    throw error;
  }
  return value;
}

function humanize(value) {
  return String(value || 'not recorded').replaceAll('_', ' ');
}

function formatDate(value) {
  if (!value) return 'Not recorded';
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? String(value) : date.toLocaleString();
}

function shortCommit(value) {
  const text = String(value || '');
  return text.length >= 12 ? text.slice(0, 12) : text || 'Not recorded';
}

function tone(value) {
  const normalized = String(value || '').toLowerCase();
  if (normalized.includes('blocked') || normalized.includes('rejected') || normalized.includes('critical')) return 'danger';
  if (normalized.includes('approval') || normalized.includes('pending') || normalized.includes('warning')) return 'warning';
  if (normalized.includes('complete') || normalized.includes('approved') || normalized.includes('ready') || normalized.includes('auto_execute')) return 'success';
  return 'neutral';
}

function Badge({ value }) {
  return <span className={`ffla-badge is-${tone(value)}`}>{humanize(value)}</span>;
}

function Field({ label, hint, full = false, children }) {
  return <label className={`ffla-field${full ? ' is-full' : ''}`}><span>{label}</span>{hint ? <small>{hint}</small> : null}{children}</label>;
}

function Check({ label, checked, onChange, disabled = false }) {
  return <label className="ffla-check"><input type="checkbox" checked={Boolean(checked)} disabled={disabled} onChange={(event) => onChange(event.target.checked)} /><span>{label}</span></label>;
}

function Empty({ children }) {
  return <div className="ffla-empty">{children}</div>;
}

function JsonBlock({ value }) {
  return <pre className="ffla-json">{JSON.stringify(value ?? {}, null, 2)}</pre>;
}

function buildManifest(run) {
  const now = new Date();
  const expiry = new Date(now.getTime() + 60 * 60 * 1000);
  const zeroDigest = `sha256:${'0'.repeat(64)}`;
  return JSON.stringify({
    manifestVersion: 'module083-dry-run-manifest-v1',
    repository: run?.repository || DEFAULT_REPOSITORY,
    sourceCommit: run?.sourceCommit || '',
    pullRequestNumber: null,
    buildWorkflow: 'module-083-dry-run',
    buildRunId: null,
    buildRunAttempt: null,
    artifacts: [{
      component: 'pulse-dry-run',
      image: 'dry-run/pulse',
      digest: zeroDigest,
      sbomReference: 'urn:pulse:dry-run:sbom',
      provenanceReference: 'urn:pulse:dry-run:provenance',
      signatureReference: 'urn:pulse:dry-run:signature'
    }],
    migrations: [],
    targetEnvironment: run?.environment || 'test',
    canaryEvidenceReferences: ['urn:pulse:dry-run:canary'],
    verificationEvidenceReferences: ['urn:pulse:dry-run:verification'],
    approvalEvidenceReferences: [],
    rollbackArtifactDigests: [zeroDigest],
    configurationFingerprint: 'dry-run-no-secret-configuration',
    createdAt: now.toISOString(),
    expiresAt: expiry.toISOString()
  }, null, 2);
}

export default function FullFutureLoopAutomationCenter({ authSession, selectedLoopId = null }) {
  const [tab, setTab] = useState('overview');
  const [readiness, setReadiness] = useState(null);
  const [policy, setPolicy] = useState(null);
  const [adapters, setAdapters] = useState([]);
  const [runs, setRuns] = useState([]);
  const [approvals, setApprovals] = useState([]);
  const [evidence, setEvidence] = useState([]);
  const [selectedRunId, setSelectedRunId] = useState('');
  const [runDetail, setRunDetail] = useState(null);
  const [simulation, setSimulation] = useState({ ...EMPTY_SIMULATION });
  const [simulationResult, setSimulationResult] = useState(null);
  const [runtimeForm, setRuntimeForm] = useState({ automationEnabled: false, globalKillSwitch: true, reason: 'Reviewing the Module 083 dry-run control plane.' });
  const [manifestText, setManifestText] = useState('');
  const [migrationRequired, setMigrationRequired] = useState(null);
  const [busy, setBusy] = useState('');
  const [error, setError] = useState('');
  const [message, setMessage] = useState('');

  const request = useCallback(async (path, options = {}) => responseBody(await fetch(`${BASE}${path}`, {
    ...options,
    cache: 'no-store',
    headers: { ...authHeaders(authSession, Boolean(options.body)), ...(options.headers || {}) }
  })), [authSession]);

  const loadRun = useCallback(async (runId) => {
    if (!runId) { setRunDetail(null); setManifestText(''); return; }
    const result = await request(`/runs/${runId}`);
    setRunDetail(result);
    setManifestText(result.manifest ? JSON.stringify(result.manifest.document, null, 2) : buildManifest(result.run));
  }, [request]);

  const refresh = useCallback(async (preferredRunId = '') => {
    setBusy('refresh'); setError(''); setMigrationRequired(null);
    try {
      const ready = await request('/readiness');
      setReadiness(ready);
      setRuntimeForm((current) => ({
        ...current,
        automationEnabled: ready.runtime?.automationEnabled === true,
        globalKillSwitch: ready.runtime?.globalKillSwitch !== false
      }));
      const [policyBody, adapterBody, runBody, approvalBody, evidenceBody] = await Promise.all([
        request('/policy'), request('/adapters'), request('/runs?limit=200'),
        request('/approvals?limit=200'), request('/evidence?limit=300')
      ]);
      setPolicy(policyBody);
      setAdapters(Array.isArray(adapterBody.adapters) ? adapterBody.adapters : []);
      const nextRuns = Array.isArray(runBody.runs) ? runBody.runs : [];
      setRuns(nextRuns);
      setApprovals(Array.isArray(approvalBody.approvals) ? approvalBody.approvals : []);
      setEvidence(Array.isArray(evidenceBody.evidence) ? evidenceBody.evidence : []);
      const target = preferredRunId || selectedRunId || nextRuns[0]?.runId || '';
      setSelectedRunId(target);
      if (target) await loadRun(target); else { setRunDetail(null); setManifestText(''); }
    } catch (caught) {
      if (caught.status === 503 && caught.payload?.code === 'MODULE_083_AUTOMATION_MIGRATION_REQUIRED') {
        setMigrationRequired(caught.payload);
        setReadiness(null); setPolicy(null); setAdapters([]); setRuns([]); setApprovals([]); setEvidence([]); setRunDetail(null);
      } else {
        setError(caught.message);
      }
    } finally {
      setBusy('');
    }
  }, [loadRun, request, selectedRunId]);

  useEffect(() => { void refresh(); }, [refresh]);
  useEffect(() => {
    if (selectedLoopId) setSimulation((current) => ({ ...current, loopId: selectedLoopId }));
  }, [selectedLoopId]);

  const permissions = readiness?.permissions || policy?.permissions || {};
  const runtime = readiness?.runtime || policy?.runtime || {};
  const counts = readiness?.counts || {};
  const activePolicy = policy?.activePolicy || null;
  const selectedRun = runDetail?.run || runs.find((item) => item.runId === selectedRunId) || null;
  const pendingApprovals = useMemo(() => approvals.filter((item) => item.status === 'pending'), [approvals]);

  function changeSimulation(name, value) {
    setSimulation((current) => ({ ...current, [name]: value }));
  }

  async function simulatePolicy(event) {
    event.preventDefault(); setBusy('simulate'); setError(''); setMessage(''); setSimulationResult(null);
    try {
      const result = await request('/policy/simulate', { method: 'POST', body: JSON.stringify({ ...simulation, loopId: selectedLoopId || simulation.loopId || null }) });
      setSimulationResult(result);
      setMessage('Policy simulation completed without persisting a run or calling an external system.');
    } catch (caught) { setError(caught.message); } finally { setBusy(''); }
  }

  async function createDryRun() {
    setBusy('dry-run'); setError(''); setMessage('');
    try {
      const result = await request('/runs/dry-run', { method: 'POST', body: JSON.stringify({ ...simulation, assumePolicyEnabled: false, assumeKillSwitchReleased: false, loopId: selectedLoopId || simulation.loopId || null }) });
      const runId = result.run?.runId || result.runId || '';
      setMessage('Durable dry run created. No external execution was attempted.');
      setTab('runs');
      await refresh(runId);
    } catch (caught) { setError(caught.message); } finally { setBusy(''); }
  }

  async function updateRuntime(event) {
    event.preventDefault();
    if (!runtimeForm.globalKillSwitch && !window.confirm('Release the Module 083 kill switch for dry-run policy processing only? External execution remains disabled.')) return;
    setBusy('runtime'); setError(''); setMessage('');
    try {
      await request('/runtime', { method: 'POST', body: JSON.stringify({ ...runtimeForm, expectedRevision: runtime.revision }) });
      setMessage('Runtime state updated. The database remains dry-run-only and external execution remains disabled.');
      await refresh(selectedRunId);
    } catch (caught) { setError(caught.message); } finally { setBusy(''); }
  }

  async function setAdapterMode(adapter, mode) {
    const reason = window.prompt(`Enter the reason for setting ${adapter.displayName} to ${humanize(mode)}:`, 'Prepare provider-neutral dry-run planning without external access.');
    if (!reason) return;
    setBusy(`adapter-${adapter.adapterCode}`); setError(''); setMessage('');
    try {
      await request(`/adapters/${encodeURIComponent(adapter.adapterCode)}/mode`, { method: 'POST', body: JSON.stringify({ mode, expectedRevision: adapter.revision, reason }) });
      setMessage(`${adapter.displayName} is now ${humanize(mode)}. External execution remains disabled.`);
      await refresh(selectedRunId);
    } catch (caught) { setError(caught.message); } finally { setBusy(''); }
  }

  async function decideApproval(approval, decision) {
    const reason = window.prompt(`Enter the ${decision} reason for ${humanize(approval.approvalType)}:`, decision === 'approved' ? 'Reviewed and approved under the recorded authority.' : 'Rejected because the required authority or evidence is not satisfied.');
    if (!reason) return;
    setBusy(`approval-${approval.approvalId}`); setError(''); setMessage('');
    try {
      await request(`/approvals/${approval.approvalId}/decision`, { method: 'POST', body: JSON.stringify({ decision, expectedRevision: approval.revision, reason }) });
      setMessage(`Approval ${decision}. Append-only decision evidence was recorded.`);
      await refresh(approval.runId);
    } catch (caught) { setError(caught.message); } finally { setBusy(''); }
  }

  async function registerManifest(event) {
    event.preventDefault();
    if (!selectedRun) return;
    setBusy('manifest'); setError(''); setMessage('');
    try {
      let body;
      try { body = JSON.parse(manifestText); } catch { throw new Error('Release manifest must be valid JSON.'); }
      await request(`/runs/${selectedRun.runId}/manifest`, { method: 'POST', body: JSON.stringify(body) });
      setMessage('Immutable release manifest registered with its SHA-256 evidence.');
      await refresh(selectedRun.runId);
    } catch (caught) { setError(caught.message); } finally { setBusy(''); }
  }

  if (migrationRequired) {
    return <section className="ffla-center" data-module083-automation="migration-required">
      <header className="ffla-hero"><div><p>Module 083 · Autonomous control plane</p><h2>Durable orchestration is ready for migration review</h2><span>The existing Full Future Loop sandbox remains available. Migration 083 is required before policies, dry runs, approvals, manifests, and evidence can be persisted.</span></div><Badge value="migration required" /></header>
      <div className="ffla-migration"><strong>{migrationRequired.migration}</strong><p>{migrationRequired.message}</p><small>No migration, deployment, external adapter, secret, or infrastructure change is performed by this interface.</small></div>
    </section>;
  }

  return <section className="ffla-center" data-module083-automation="durable-dry-run">
    <header className="ffla-hero">
      <div><p>Module 083 · Enterprise automation</p><h2>Autonomous Control Plane</h2><span>Policy-driven orchestration, human authority, immutable release evidence, exact rollback planning, and provider-neutral adapters.</span></div>
      <div className="ffla-hero-state"><Badge value={runtime.globalKillSwitch ? 'kill switch active' : runtime.automationEnabled ? 'dry run enabled' : 'automation disabled'} /><strong>External execution: OFF</strong><button type="button" onClick={() => void refresh()} disabled={Boolean(busy)}>Refresh</button></div>
    </header>

    {error ? <div className="ffla-notice is-error" role="alert">{error}</div> : null}
    {message ? <div className="ffla-notice is-success" role="status">{message}</div> : null}
    {busy ? <div className="ffla-progress"><span /></div> : null}

    <section className="ffla-boundary">
      <div><strong>Durable dry-run boundary</strong><span>Policies, runs, approvals, manifests, adapter modes, outbox intentions, and evidence are persisted. GitHub, Azure, deployment, secret, telemetry, Module 065/076, and AI clients are not installed in this phase.</span></div>
      <div><span>Dry run only</span><span>View-As read-only</span><span>Requester ≠ approver</span><span>Append-only evidence</span></div>
    </section>

    <nav className="ffla-tabs" aria-label="Autonomous control-plane views">{TABS.map(([key, label]) => <button type="button" key={key} className={tab === key ? 'active' : ''} onClick={() => setTab(key)}>{label}{key === 'approvals' && pendingApprovals.length ? <b>{pendingApprovals.length}</b> : null}</button>)}</nav>

    {tab === 'overview' ? <div className="ffla-view">
      <section className="ffla-kpis">
        <article><span>Total dry runs</span><strong>{counts.totalRuns ?? '—'}</strong><small>idempotent durable plans</small></article>
        <article><span>Completed</span><strong>{counts.completedDryRuns ?? '—'}</strong><small>external execution not attempted</small></article>
        <article className={Number(counts.blockedRuns) > 0 ? 'attention' : ''}><span>Blocked</span><strong>{counts.blockedRuns ?? '—'}</strong><small>fail-closed decisions</small></article>
        <article className={Number(counts.pendingApprovals) > 0 ? 'attention' : ''}><span>Approvals</span><strong>{counts.pendingApprovals ?? '—'}</strong><small>separate authority required</small></article>
        <article><span>Manifests</span><strong>{counts.releaseManifests ?? '—'}</strong><small>immutable exact releases</small></article>
        <article><span>Dry-run adapters</span><strong>{counts.dryRunAdapters ?? '—'}</strong><small>active adapters prohibited</small></article>
      </section>

      <div className="ffla-two-column">
        <form className="ffla-panel" onSubmit={updateRuntime}>
          <header><div><small>Runtime authority</small><h3>Kill switch and dry-run processing</h3></div><Badge value={runtime.globalKillSwitch ? 'kill switch active' : 'kill switch released'} /></header>
          <Check label="Enable durable automation policy processing" checked={runtimeForm.automationEnabled} disabled={!permissions.canManage} onChange={(value) => setRuntimeForm((current) => ({ ...current, automationEnabled: value }))} />
          <Check label="Keep global kill switch active" checked={runtimeForm.globalKillSwitch} disabled={!permissions.canManage} onChange={(value) => setRuntimeForm((current) => ({ ...current, globalKillSwitch: value }))} />
          <Field label="Change reason" full><textarea rows="4" maxLength="2000" value={runtimeForm.reason} disabled={!permissions.canManage} onChange={(event) => setRuntimeForm((current) => ({ ...current, reason: event.target.value }))} /></Field>
          <div className="ffla-runtime-facts"><span>Revision <strong>{runtime.revision ?? '—'}</strong></span><span>Dry run only <strong>{runtime.dryRunOnly === false ? 'No' : 'Yes'}</strong></span><span>External execution <strong>Disabled</strong></span></div>
          <button type="submit" className="primary" disabled={!permissions.canManage || Boolean(busy) || runtimeForm.reason.trim().length < 3}>Save governed runtime state</button>
        </form>

        <article className="ffla-panel">
          <header><div><small>Active policy</small><h3>{activePolicy?.policyVersion || 'Loading policy'}</h3></div><Badge value={activePolicy?.globalKillSwitch ? 'fail closed' : activePolicy?.enabled ? 'enabled' : 'disabled'} /></header>
          <dl className="ffla-definition">
            <div><dt>Policy SHA-256</dt><dd><code>{activePolicy?.policySha256 || 'Not recorded'}</code></dd></div>
            <div><dt>Repositories</dt><dd>{activePolicy?.allowedRepositories?.join(', ') || 'None'}</dd></div>
            <div><dt>Environments</dt><dd>{activePolicy?.allowedEnvironments?.join(', ') || 'None'}</dd></div>
            <div><dt>Maximum concurrent runs</dt><dd>{activePolicy?.maximumConcurrentRuns ?? '—'}</dd></div>
            <div><dt>Maximum attempts</dt><dd>{activePolicy?.maximumStepAttempts ?? '—'}</dd></div>
            <div><dt>Evidence age</dt><dd>{activePolicy?.evidenceMaximumAgeMinutes ?? '—'} minutes</dd></div>
          </dl>
          <div className="ffla-policy-gates">
            {['Production approval', 'Migration approval', 'Security approval', 'Infrastructure approval', 'Secret-change approval'].map((item) => <span key={item}>✓ {item}</span>)}
          </div>
        </article>
      </div>
    </div> : null}

    {tab === 'simulate' ? <div className="ffla-view">
      <form className="ffla-panel" onSubmit={simulatePolicy}>
        <header><div><small>Non-persistent analysis</small><h3>Policy simulator and durable dry-run request</h3><p>Simulation can assume an enabled policy and released kill switch. Creating a dry run uses the real stored runtime state.</p></div><Badge value="no external calls" /></header>
        <div className="ffla-form-grid">
          <Field label="Operation"><select value={simulation.operation} onChange={(event) => changeSimulation('operation', event.target.value)}>{OPERATIONS.map((item) => <option key={item} value={item}>{humanize(item)}</option>)}</select></Field>
          <Field label="Environment"><select value={simulation.environment} onChange={(event) => changeSimulation('environment', event.target.value)}><option value="canary">Canary</option><option value="test">Test</option><option value="production">Production</option></select></Field>
          <Field label="Repository" full><input value={simulation.repository} maxLength="240" onChange={(event) => changeSimulation('repository', event.target.value)} /></Field>
          <Field label="Exact source commit" hint="Lowercase, 40 hexadecimal characters" full><input value={simulation.sourceCommit} maxLength="40" minLength="40" placeholder="0000000000000000000000000000000000000000" onChange={(event) => changeSimulation('sourceCommit', event.target.value.toLowerCase())} required /></Field>
          <Field label="Risk class"><select value={simulation.riskClass} onChange={(event) => changeSimulation('riskClass', event.target.value)}><option value="routine">Routine</option><option value="normal">Normal</option><option value="high">High</option><option value="critical">Critical</option></select></Field>
          <Field label="Change type"><input value={simulation.changeType} maxLength="80" onChange={(event) => changeSimulation('changeType', event.target.value)} /></Field>
        </div>
        <div className="ffla-check-grid">
          <Check label="Includes migration" checked={simulation.includesMigration} onChange={(value) => changeSimulation('includesMigration', value)} />
          <Check label="Includes security change" checked={simulation.includesSecurityChange} onChange={(value) => changeSimulation('includesSecurityChange', value)} />
          <Check label="Includes infrastructure change" checked={simulation.includesInfrastructureChange} onChange={(value) => changeSimulation('includesInfrastructureChange', value)} />
          <Check label="Includes secret change" checked={simulation.includesSecretChange} onChange={(value) => changeSimulation('includesSecretChange', value)} />
          <Check label="Emergency rollback" checked={simulation.isEmergencyRollback} onChange={(value) => changeSimulation('isEmergencyRollback', value)} />
          <Check label="Requested by AI" checked={simulation.requestedByAi} onChange={(value) => changeSimulation('requestedByAi', value)} />
        </div>
        <details className="ffla-details"><summary>Evidence and approval inputs</summary><div className="ffla-check-grid">
          <Check label="Production approval satisfied" checked={simulation.productionApprovalSatisfied} onChange={(value) => changeSimulation('productionApprovalSatisfied', value)} />
          <Check label="Migration approval satisfied" checked={simulation.migrationApprovalSatisfied} onChange={(value) => changeSimulation('migrationApprovalSatisfied', value)} />
          <Check label="Security approval satisfied" checked={simulation.securityApprovalSatisfied} onChange={(value) => changeSimulation('securityApprovalSatisfied', value)} />
          <Check label="Infrastructure approval satisfied" checked={simulation.infrastructureApprovalSatisfied} onChange={(value) => changeSimulation('infrastructureApprovalSatisfied', value)} />
          <Check label="Secret approval satisfied" checked={simulation.secretChangeApprovalSatisfied} onChange={(value) => changeSimulation('secretChangeApprovalSatisfied', value)} />
          <Check label="Canary passed" checked={simulation.canaryPassed} onChange={(value) => changeSimulation('canaryPassed', value)} />
          <Check label="Cleanup proven" checked={simulation.cleanupProven} onChange={(value) => changeSimulation('cleanupProven', value)} />
          <Check label="Verification passed" checked={simulation.verificationSuitePassed} onChange={(value) => changeSimulation('verificationSuitePassed', value)} />
          <Check label="Rollback target proven" checked={simulation.rollbackTargetProven} onChange={(value) => changeSimulation('rollbackTargetProven', value)} />
          <Check label="Exact digests present" checked={simulation.exactArtifactDigestsPresent} onChange={(value) => changeSimulation('exactArtifactDigestsPresent', value)} />
          <Check label="SBOM present" checked={simulation.sbomPresent} onChange={(value) => changeSimulation('sbomPresent', value)} />
          <Check label="Provenance present" checked={simulation.provenancePresent} onChange={(value) => changeSimulation('provenancePresent', value)} />
          <Check label="Signatures verified" checked={simulation.signaturesVerified} onChange={(value) => changeSimulation('signaturesVerified', value)} />
          <Check label="Simulation assumes enabled policy" checked={simulation.assumePolicyEnabled} onChange={(value) => changeSimulation('assumePolicyEnabled', value)} />
          <Check label="Simulation assumes released kill switch" checked={simulation.assumeKillSwitchReleased} onChange={(value) => changeSimulation('assumeKillSwitchReleased', value)} />
        </div></details>
        <div className="ffla-actions"><button type="submit" className="primary" disabled={Boolean(busy) || simulation.sourceCommit.length !== 40}>Simulate policy</button><button type="button" disabled={!permissions.canOperateDryRuns || Boolean(busy) || simulation.sourceCommit.length !== 40} onClick={() => void createDryRun()}>Create durable dry run</button></div>
      </form>
      {simulationResult ? <article className="ffla-panel ffla-decision"><header><div><small>Deterministic result</small><h3>{humanize(simulationResult.decision?.decisionCode)}</h3></div><Badge value={simulationResult.decision?.disposition} /></header><p>{simulationResult.decision?.summary}</p><div className="ffla-decision-columns"><div><strong>Reasons</strong>{(simulationResult.decision?.reasons || []).map((item) => <span key={item}>{item}</span>)}</div><div><strong>Required approvals</strong>{(simulationResult.decision?.requiredApprovals || []).length ? simulationResult.decision.requiredApprovals.map((item) => <span key={item}>{humanize(item)}</span>) : <span>None</span>}</div></div><small>Persisted: No · External execution attempted: No</small></article> : null}
    </div> : null}

    {tab === 'adapters' ? <div className="ffla-view"><section className="ffla-card-grid">{adapters.map((adapter) => <article className="ffla-adapter" key={adapter.adapterCode}><header><div><small>{adapter.adapterCode}</small><h3>{adapter.displayName}</h3></div><Badge value={adapter.mode} /></header><p>{adapter.detail}</p><dl><div><dt>Credential boundary</dt><dd>{adapter.credentialBoundary}</dd></div><div><dt>Writes externally</dt><dd>{adapter.writesExternally ? 'Designed to, but disabled' : 'No'}</dd></div><div><dt>Ready</dt><dd>{adapter.isReady ? 'Yes' : 'No'}</dd></div><div><dt>Circuit</dt><dd>{adapter.circuitOpen ? 'Open' : 'Closed'}</dd></div><div><dt>Revision</dt><dd>{adapter.revision}</dd></div></dl><div className="ffla-capabilities">{(adapter.capabilities || []).map((item) => <span key={item}>{humanize(item)}</span>)}</div><footer><button type="button" disabled={!permissions.canManage || Boolean(busy) || adapter.mode === 'disabled'} onClick={() => void setAdapterMode(adapter, 'disabled')}>Disable</button><button type="button" className="primary" disabled={!permissions.canManage || Boolean(busy) || adapter.mode === 'dry_run'} onClick={() => void setAdapterMode(adapter, 'dry_run')}>Dry-run mode</button></footer></article>)}</section></div> : null}

    {tab === 'runs' ? <div className="ffla-view ffla-run-layout">
      <aside className="ffla-run-list"><header><small>Durable orchestration</small><h3>Dry runs</h3></header>{runs.length ? runs.map((run) => <button type="button" key={run.runId} className={run.runId === selectedRunId ? 'active' : ''} onClick={() => { setSelectedRunId(run.runId); void loadRun(run.runId); }}><span><strong>{humanize(run.operation)}</strong><Badge value={run.status} /></span><b>{shortCommit(run.sourceCommit)}</b><small>{run.environment} · {formatDate(run.createdAt)}</small></button>) : <Empty>No dry runs have been created.</Empty>}</aside>
      <div className="ffla-run-detail">{selectedRun ? <>
        <article className="ffla-panel"><header><div><small>{selectedRun.runId}</small><h3>{humanize(selectedRun.operation)} · {selectedRun.environment}</h3></div><Badge value={selectedRun.status} /></header><dl className="ffla-definition"><div><dt>Repository</dt><dd>{selectedRun.repository}</dd></div><div><dt>Source commit</dt><dd><code>{selectedRun.sourceCommit}</code></dd></div><div><dt>Disposition</dt><dd>{humanize(selectedRun.disposition)}</dd></div><div><dt>Policy</dt><dd>{selectedRun.policyVersionId}</dd></div><div><dt>Idempotency key</dt><dd><code>{selectedRun.idempotencyKey}</code></dd></div><div><dt>Correlation</dt><dd>{selectedRun.correlationId}</dd></div></dl></article>
        <article className="ffla-panel"><header><div><small>Deterministic runbook</small><h3>Planned steps</h3></div><span>{runDetail?.steps?.length || 0} steps</span></header><div className="ffla-steps">{(runDetail?.steps || []).map((step) => <article key={step.stepId}><b>{step.sequence}</b><div><strong>{humanize(step.code)}</strong><small>{step.adapterCode ? `Adapter: ${step.adapterCode}` : 'Internal control-plane step'}</small></div><Badge value={step.status} /></article>)}</div></article>
        <form className="ffla-panel" onSubmit={registerManifest}><header><div><small>Exact release identity</small><h3>Immutable release manifest</h3></div><Badge value={runDetail?.manifest ? 'registered' : 'not registered'} /></header>{runDetail?.manifest ? <><p>Manifest SHA-256: <code>{runDetail.manifest.sha256}</code></p><JsonBlock value={runDetail.manifest.document} /></> : <><Field label="Manifest JSON" hint="The generated example is safe test evidence and does not publish or deploy an artifact." full><textarea className="ffla-manifest" rows="18" value={manifestText} onChange={(event) => setManifestText(event.target.value)} /></Field><button type="submit" className="primary" disabled={!permissions.canOperateDryRuns || Boolean(busy) || !manifestText.trim()}>Register immutable manifest</button></>}</form>
      </> : <Empty>Select a dry run to inspect its steps, approvals, and release manifest.</Empty>}</div>
    </div> : null}

    {tab === 'approvals' ? <div className="ffla-view"><section className="ffla-approval-list">{approvals.length ? approvals.map((approval) => <article key={approval.approvalId}><header><div><small>{approval.runId}</small><h3>{humanize(approval.approvalType)}</h3></div><Badge value={approval.status} /></header><p>{humanize(approval.operation)} · {approval.environment} · <code>{shortCommit(approval.sourceCommit)}</code></p><dl><div><dt>Requested by</dt><dd>{approval.requestedByUserId}</dd></div><div><dt>Decision reason</dt><dd>{approval.decisionReason || 'Pending decision'}</dd></div><div><dt>Separation of duties</dt><dd>{approval.separationOfDutiesSatisfied ? 'Current user is different from requester' : 'A different authorized user must decide'}</dd></div></dl>{approval.status === 'pending' ? <footer><button type="button" className="danger" disabled={!permissions.canApprove || !approval.separationOfDutiesSatisfied || Boolean(busy)} onClick={() => void decideApproval(approval, 'rejected')}>Reject</button><button type="button" className="primary" disabled={!permissions.canApprove || !approval.separationOfDutiesSatisfied || Boolean(busy)} onClick={() => void decideApproval(approval, 'approved')}>Approve</button></footer> : null}</article>) : <Empty>No approval records have been created.</Empty>}</section></div> : null}

    {tab === 'evidence' ? <div className="ffla-view"><section className="ffla-evidence-list">{evidence.length ? evidence.map((item) => <article key={item.evidenceId}><header><div><small>{formatDate(item.occurredAt)}</small><h3>{humanize(item.eventCode)}</h3></div><Badge value={item.severity} /></header><p>Run: {item.runId || 'Control-plane state'} · Loop: {item.loopId || 'Not linked'}</p><JsonBlock value={item.document} /></article>) : <Empty>No autonomous evidence has been recorded.</Empty>}</section></div> : null}
  </section>;
}
