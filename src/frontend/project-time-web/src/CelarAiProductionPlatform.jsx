import { useCallback, useEffect, useMemo, useState } from 'react';
import CelarAiEnterprisePlatform from './CelarAiEnterprisePlatform.jsx';
import PulseAiPrivateDocumentPipelineWorkbench from './PulseAiPrivateDocumentPipelineWorkbench.jsx';
import PulseAiPrivateRagWorkbench from './PulseAiPrivateRagWorkbench.jsx';
import PulseAiPrivateRuntimeWorkbench from './PulseAiPrivateRuntimeWorkbench.jsx';
import PulseAiSystemIntelligenceWorkbench from './PulseAiSystemIntelligenceWorkbench.jsx';
import { usSignalLogoDataUrl } from './assets/usSignalLogoData.js';
import './celar-ai-production-platform.css';

const TABS = Object.freeze([
  ['overview', 'Overview', 'Architecture, readiness, trust, and solution composer'],
  ['knowledge', 'Knowledge & RAG', 'Private document processing, retrieval, citations, and revocation'],
  ['tools', 'Tools & Coverage', 'Live APIs, governed tools, troubleshooting, and system coverage'],
  ['datasets', 'Datasets', 'Reviewed immutable training and evaluation inputs'],
  ['training', 'Training', 'Private supervised fine-tuning, LoRA, QLoRA, and job evidence'],
  ['evaluations', 'Evaluations', 'Promotion-blocking correctness, privacy, and permission gates'],
  ['registry', 'Model Registry', 'Versioned models, artifacts, checksums, and approvals'],
  ['deployments', 'Deployments', 'Development, Test, Production planning, canary, and rollback'],
  ['governance', 'Governance', 'Permissions, routing, audit, answer trust, and operating rules']
]);

const EMPTY_DATASET = Object.freeze({ name: '', purpose: '', classification: 'internal', artifactUri: '', sha256: '', exampleCount: 0, state: 'reviewed' });
const EMPTY_TRAINING = Object.freeze({ datasetVersionId: '', method: 'lora', baseModel: '', configuration: '{\n  "epochs": 3,\n  "learningRate": 0.0002\n}' });
const EMPTY_MODEL = Object.freeze({ name: 'celar-ai', semanticVersion: '', baseModel: '', artifactUri: '', sha256: '', datasetVersionId: '', trainingJobId: '', evaluationRunId: '', state: 'draft' });
const EMPTY_DEPLOYMENT = Object.freeze({ modelVersionId: '', environment: 'test', capabilityCode: 'help_assistant', rollbackModelVersionId: '' });

function asArray(value) { return Array.isArray(value) ? value : []; }
function title(value) { return String(value ?? '').replaceAll('_', ' ').replace(/\b\w/g, (letter) => letter.toUpperCase()); }
function formatDate(value) { if (!value) return 'Not recorded'; const date = new Date(value); return Number.isNaN(date.getTime()) ? String(value) : date.toLocaleString(); }
function formatPercent(value) { const number = Number(value); return Number.isFinite(number) ? `${Math.round(number * 100)}%` : 'Not recorded'; }

async function readJson(response) {
  const payload = await response.json().catch(() => ({}));
  if (!response.ok) {
    const error = new Error(payload.message || payload.detail || `Request returned HTTP ${response.status}.`);
    error.status = response.status;
    throw error;
  }
  return payload;
}
async function getJson(path) { return readJson(await fetch(path, { method: 'GET', cache: 'no-store', headers: { Accept: 'application/json' } })); }
async function postJson(path, body) { return readJson(await fetch(path, { method: 'POST', cache: 'no-store', headers: { Accept: 'application/json', 'Content-Type': 'application/json' }, body: JSON.stringify(body ?? {}) })); }

function StatusCard({ label, value, detail, tone = 'neutral' }) {
  return <article className={`celar-production-status is-${tone}`}><span>{label}</span><strong>{value}</strong><small>{detail}</small></article>;
}
function Notice({ notice, error }) {
  if (!notice && !error) return null;
  return <div className={`celar-production-notice ${error ? 'is-error' : 'is-success'}`} role="status"><strong>{error ? 'Action did not complete' : 'Celar AI update'}</strong><span>{error || notice}</span></div>;
}
function DataTable({ rows, columns, empty }) {
  const values = asArray(rows);
  if (!values.length) return <div className="celar-production-empty"><strong>No records yet</strong><p>{empty}</p></div>;
  return <div className="celar-production-table-wrap"><table><thead><tr>{columns.map((column) => <th key={column.key}>{column.label}</th>)}</tr></thead><tbody>{values.map((row, index) => <tr key={row.datasetVersionId || row.trainingJobId || row.evaluationRunId || row.modelVersionId || row.deploymentId || index}>{columns.map((column) => <td key={column.key}>{column.render ? column.render(row[column.key], row) : String(row[column.key] ?? 'Not recorded')}</td>)}</tr>)}</tbody></table></div>;
}
function LifecycleHeading({ eyebrow, heading, copy, action }) {
  return <div className="celar-production-section-heading"><div><p>{eyebrow}</p><h2>{heading}</h2><span>{copy}</span></div>{action || null}</div>;
}

function DatasetWorkspace({ data, refresh, canManage }) {
  const [form, setForm] = useState({ ...EMPTY_DATASET });
  const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState('');
  const [error, setError] = useState('');
  async function submit(event) {
    event.preventDefault(); setBusy(true); setNotice(''); setError('');
    try {
      await postJson('/api/celar-ai/v1/production/datasets', { ...form, exampleCount: Number(form.exampleCount || 0) });
      setForm({ ...EMPTY_DATASET });
      setNotice('Immutable dataset metadata registered. No raw examples were uploaded through the browser.');
      await refresh();
    } catch (requestError) { setError(requestError.message); } finally { setBusy(false); }
  }
  return <section className="celar-production-workspace">
    <LifecycleHeading eyebrow="Training data governance" heading="Immutable, reviewed dataset versions" copy="Pulse stores approved artifact references and SHA-256 checksums—not raw customer documents or multi-gigabyte training files." />
    <Notice notice={notice} error={error} />
    <div className="celar-production-two-column">
      <form onSubmit={submit}><h3>Register dataset version</h3><label>Name<input value={form.name} onChange={(event) => setForm({ ...form, name: event.target.value })} required /></label><label>Purpose<textarea value={form.purpose} onChange={(event) => setForm({ ...form, purpose: event.target.value })} rows={4} required /></label><div className="celar-production-fields"><label>Classification<select value={form.classification} onChange={(event) => setForm({ ...form, classification: event.target.value })}><option>internal</option><option>confidential</option><option>restricted</option></select></label><label>Review state<select value={form.state} onChange={(event) => setForm({ ...form, state: event.target.value })}><option>draft</option><option>reviewed</option><option>approved</option></select></label></div><label>Approved private artifact URI<input value={form.artifactUri} onChange={(event) => setForm({ ...form, artifactUri: event.target.value })} placeholder="https://private-storage/..." required /></label><label>SHA-256<input value={form.sha256} onChange={(event) => setForm({ ...form, sha256: event.target.value })} maxLength={64} required /></label><label>Example count<input type="number" min="0" value={form.exampleCount} onChange={(event) => setForm({ ...form, exampleCount: event.target.value })} /></label><button type="submit" disabled={!canManage || busy}>{busy ? 'Registering…' : canManage ? 'Register immutable dataset' : 'Administrator authority required'}</button></form>
      <div><h3>Dataset versions</h3><DataTable rows={data} empty="Register a reviewed private dataset reference to begin governed evaluation or fine-tuning." columns={[{ key: 'name', label: 'Dataset' }, { key: 'classification', label: 'Classification', render: title }, { key: 'exampleCount', label: 'Examples' }, { key: 'state', label: 'State', render: title }, { key: 'sha256', label: 'Checksum', render: (value) => <code>{String(value).slice(0, 12)}…</code> }, { key: 'createdAt', label: 'Created', render: formatDate }]} /></div>
    </div>
  </section>;
}

function TrainingWorkspace({ data, datasets, refresh, canManage, readiness }) {
  const [form, setForm] = useState({ ...EMPTY_TRAINING });
  const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState('');
  const [error, setError] = useState('');
  async function submit(event) {
    event.preventDefault(); setBusy(true); setNotice(''); setError('');
    try {
      let configuration = {};
      try { configuration = JSON.parse(form.configuration || '{}'); } catch { throw new Error('Training configuration must be valid JSON.'); }
      const result = await postJson('/api/celar-ai/v1/production/training-jobs', { datasetVersionId: form.datasetVersionId, method: form.method, baseModel: form.baseModel, configuration });
      setNotice(result.submission?.submitted ? 'Private training job submitted.' : 'Training job registered; private compute configuration is still required.');
      await refresh();
    } catch (requestError) { setError(requestError.message); } finally { setBusy(false); }
  }
  return <section className="celar-production-workspace">
    <LifecycleHeading eyebrow="Private compute boundary" heading="Fine-tuning and specialization jobs" copy="LoRA, QLoRA, supervised fine-tuning, and evaluated distillation use an approved private training endpoint. The request contains an immutable artifact URI and checksum, not raw browser-uploaded examples." />
    <Notice notice={notice} error={error} />
    <div className="celar-production-runtime-banner"><strong>{title(readiness?.status || 'training readiness unknown')}</strong><span>{readiness?.configured ? 'Private training endpoint configured.' : 'Configure the private training endpoint and allowlist before expecting job execution.'}</span></div>
    <div className="celar-production-two-column">
      <form onSubmit={submit}><h3>Submit governed job</h3><label>Dataset version<select value={form.datasetVersionId} onChange={(event) => setForm({ ...form, datasetVersionId: event.target.value })} required><option value="">Select reviewed dataset</option>{datasets.map((item) => <option key={item.datasetVersionId} value={item.datasetVersionId}>{item.name} · {item.state}</option>)}</select></label><label>Method<select value={form.method} onChange={(event) => setForm({ ...form, method: event.target.value })}><option value="evaluation_only">Evaluation only</option><option value="supervised_fine_tuning">Supervised fine-tuning</option><option value="lora">LoRA</option><option value="qlora">QLoRA</option><option value="distillation_candidate">Distillation candidate</option></select></label><label>Base model<input value={form.baseModel} onChange={(event) => setForm({ ...form, baseModel: event.target.value })} placeholder="Approved private base model" required /></label><label>Configuration JSON<textarea value={form.configuration} onChange={(event) => setForm({ ...form, configuration: event.target.value })} rows={8} /></label><button type="submit" disabled={!canManage || busy || !form.datasetVersionId}>{busy ? 'Submitting…' : canManage ? 'Create training job' : 'Administrator authority required'}</button></form>
      <div><h3>Training jobs</h3><DataTable rows={data} empty="No private training job has been registered." columns={[{ key: 'method', label: 'Method', render: title }, { key: 'baseModel', label: 'Base model' }, { key: 'status', label: 'Status', render: title }, { key: 'externalJobId', label: 'External job', render: (value) => value || 'Not submitted' }, { key: 'diagnosticCode', label: 'Diagnostic', render: (value) => value || 'None' }, { key: 'createdAt', label: 'Created', render: formatDate }]} /></div>
    </div>
  </section>;
}

function EvaluationWorkspace({ data, refresh, canManage, competency }) {
  const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState('');
  const [error, setError] = useState('');
  async function run() {
    setBusy(true); setNotice(''); setError('');
    try {
      const result = await postJson('/api/celar-ai/v1/production/evaluations', { suiteCode: 'basic_competency' });
      setNotice(result.evaluation?.passed ? 'All promotion-blocking basic competency routes passed.' : 'Evaluation failed; promotion remains blocked.');
      await refresh();
    } catch (requestError) { setError(requestError.message); } finally { setBusy(false); }
  }
  return <section className="celar-production-workspace">
    <LifecycleHeading eyebrow="Promotion gate" heading="Correctness, trust, privacy, and permission evaluations" copy="Simple utility and platform procedure questions must pass at 100%. Model promotion additionally requires citation, permission-isolation, leakage, structured-output, and feature-specific suites." action={<button type="button" onClick={run} disabled={!canManage || busy}>{busy ? 'Running…' : canManage ? 'Run basic competency suite' : 'Administrator authority required'}</button>} />
    <Notice notice={notice} error={error} />
    <div className="celar-production-score"><strong>{formatPercent(competency?.currentPassRate)}</strong><span>Current source-level competency routing</span><small>{competency?.passed || 0} of {competency?.total || 0} required cases pass</small></div>
    <div className="celar-production-competency-grid">{asArray(competency?.cases).map((test) => <article key={test.code} className={test.passed ? 'is-pass' : 'is-fail'}><strong>{test.question}</strong><span>{test.actualIntent}</span><small>{test.passed ? 'Passed' : `Expected ${test.expectedIntent}`}</small></article>)}</div>
    <h3>Recorded evaluation runs</h3><DataTable rows={data} empty="Run the frozen basic competency suite before registering an approved model version." columns={[{ key: 'suiteCode', label: 'Suite', render: title }, { key: 'status', label: 'Status', render: title }, { key: 'score', label: 'Score', render: formatPercent }, { key: 'passed', label: 'Promotion gate', render: (value) => value ? 'Passed' : 'Blocked' }, { key: 'completedAt', label: 'Completed', render: formatDate }]} />
  </section>;
}

function RegistryWorkspace({ data, datasets, jobs, evaluations, refresh, canManage }) {
  const [form, setForm] = useState({ ...EMPTY_MODEL });
  const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState('');
  const [error, setError] = useState('');
  async function submit(event) {
    event.preventDefault(); setBusy(true); setNotice(''); setError('');
    try {
      await postJson('/api/celar-ai/v1/production/models', { ...form, datasetVersionId: form.datasetVersionId || null, trainingJobId: form.trainingJobId || null, evaluationRunId: form.evaluationRunId || null });
      setForm({ ...EMPTY_MODEL }); setNotice('Model version registered with immutable artifact evidence. No route was changed.'); await refresh();
    } catch (requestError) { setError(requestError.message); } finally { setBusy(false); }
  }
  return <section className="celar-production-workspace">
    <LifecycleHeading eyebrow="Model evidence" heading="Versioned Celar AI model registry" copy="Every model or adapter is linked to its base model, artifact URI, checksum, dataset, training job, evaluation, state, and eventual rollback evidence." />
    <Notice notice={notice} error={error} />
    <div className="celar-production-two-column">
      <form onSubmit={submit}><h3>Register model artifact</h3><div className="celar-production-fields"><label>Name<input value={form.name} onChange={(event) => setForm({ ...form, name: event.target.value })} required /></label><label>Semantic version<input value={form.semanticVersion} onChange={(event) => setForm({ ...form, semanticVersion: event.target.value })} placeholder="1.0.0" required /></label></div><label>Base model<input value={form.baseModel} onChange={(event) => setForm({ ...form, baseModel: event.target.value })} required /></label><label>Private artifact URI<input value={form.artifactUri} onChange={(event) => setForm({ ...form, artifactUri: event.target.value })} required /></label><label>SHA-256<input value={form.sha256} onChange={(event) => setForm({ ...form, sha256: event.target.value })} maxLength={64} required /></label><div className="celar-production-fields"><label>Dataset<select value={form.datasetVersionId} onChange={(event) => setForm({ ...form, datasetVersionId: event.target.value })}><option value="">None</option>{datasets.map((item) => <option key={item.datasetVersionId} value={item.datasetVersionId}>{item.name}</option>)}</select></label><label>Training job<select value={form.trainingJobId} onChange={(event) => setForm({ ...form, trainingJobId: event.target.value })}><option value="">None</option>{jobs.map((item) => <option key={item.trainingJobId} value={item.trainingJobId}>{item.method} · {item.status}</option>)}</select></label></div><div className="celar-production-fields"><label>Evaluation<select value={form.evaluationRunId} onChange={(event) => setForm({ ...form, evaluationRunId: event.target.value })}><option value="">None</option>{evaluations.map((item) => <option key={item.evaluationRunId} value={item.evaluationRunId}>{item.suiteCode} · {formatPercent(item.score)}</option>)}</select></label><label>State<select value={form.state} onChange={(event) => setForm({ ...form, state: event.target.value })}><option>draft</option><option>evaluating</option><option>approved_test</option><option>test</option><option>approved_production</option><option>production</option><option>retired</option><option>rejected</option></select></label></div><button type="submit" disabled={!canManage || busy}>{busy ? 'Registering…' : canManage ? 'Register model version' : 'Administrator authority required'}</button></form>
      <div><h3>Registered versions</h3><DataTable rows={data} empty="No Celar AI model or adapter artifact is registered." columns={[{ key: 'name', label: 'Model' }, { key: 'semanticVersion', label: 'Version' }, { key: 'baseModel', label: 'Base model' }, { key: 'state', label: 'State', render: title }, { key: 'sha256', label: 'Checksum', render: (value) => <code>{String(value).slice(0, 12)}…</code> }, { key: 'createdAt', label: 'Registered', render: formatDate }]} /></div>
    </div>
  </section>;
}

function DeploymentWorkspace({ data, models, refresh, canManage }) {
  const [form, setForm] = useState({ ...EMPTY_DEPLOYMENT });
  const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState('');
  const [error, setError] = useState('');
  async function submit(event) {
    event.preventDefault(); setBusy(true); setNotice(''); setError('');
    try {
      await postJson('/api/celar-ai/v1/production/deployments', { ...form, rollbackModelVersionId: form.rollbackModelVersionId || null });
      setNotice('Deployment plan registered. Endpoint activation and Module 064 route changes still require separate human approval.'); await refresh();
    } catch (requestError) { setError(requestError.message); } finally { setBusy(false); }
  }
  return <section className="celar-production-workspace">
    <LifecycleHeading eyebrow="Controlled activation" heading="Development, Test, Production, canary, and rollback plans" copy="This workspace records reviewed deployment intent. It does not silently create infrastructure, activate endpoints, or change a Module 064 route." />
    <Notice notice={notice} error={error} />
    <div className="celar-production-two-column">
      <form onSubmit={submit}><h3>Plan deployment</h3><label>Model version<select value={form.modelVersionId} onChange={(event) => setForm({ ...form, modelVersionId: event.target.value })} required><option value="">Select model</option>{models.map((item) => <option key={item.modelVersionId} value={item.modelVersionId}>{item.name} {item.semanticVersion} · {item.state}</option>)}</select></label><div className="celar-production-fields"><label>Environment<select value={form.environment} onChange={(event) => setForm({ ...form, environment: event.target.value })}><option>development</option><option>test</option><option>production</option></select></label><label>Capability<input value={form.capabilityCode} onChange={(event) => setForm({ ...form, capabilityCode: event.target.value })} /></label></div><label>Rollback model<select value={form.rollbackModelVersionId} onChange={(event) => setForm({ ...form, rollbackModelVersionId: event.target.value })}><option value="">Not selected</option>{models.map((item) => <option key={item.modelVersionId} value={item.modelVersionId}>{item.name} {item.semanticVersion}</option>)}</select></label><button type="submit" disabled={!canManage || busy || !form.modelVersionId}>{busy ? 'Planning…' : canManage ? 'Register deployment plan' : 'Administrator authority required'}</button></form>
      <div><h3>Deployment plans</h3><DataTable rows={data} empty="No Celar AI model deployment is planned." columns={[{ key: 'environment', label: 'Environment', render: title }, { key: 'capabilityCode', label: 'Capability', render: title }, { key: 'status', label: 'Status', render: title }, { key: 'endpointFingerprint', label: 'Endpoint', render: (value) => value || 'Not activated' }, { key: 'createdAt', label: 'Created', render: formatDate }]} /></div>
    </div>
  </section>;
}

function GovernanceWorkspace({ readiness, canManage, initialize, busy }) {
  const lifecycle = readiness?.lifecycle || {};
  const trust = [
    ['Verified current fact', 'Current authoritative source or deterministic runtime fact.'],
    ['Verified document fact', 'Permission-scoped approved document version with citation.'],
    ['Calculated or verified', 'Deterministic formula or schedule engine result.'],
    ['Procedure', 'Versioned source-controlled operating guidance.'],
    ['Reviewable draft', 'SOW, Timesheet, plan, timeline, diagram, or enhancement requiring human review.'],
    ['Insufficient evidence', 'The required source failed, returned no records, was stale, or was unauthorized.']
  ];
  return <section className="celar-production-workspace">
    <LifecycleHeading eyebrow="Operating contract" heading="Trust, privacy, routing, and human authority" copy="Celar AI is useful only when users can distinguish verified facts, calculations, procedures, drafts, and missing evidence." action={!lifecycle.schemaReady ? <button type="button" disabled={!canManage || busy} onClick={initialize}>{busy ? 'Initializing…' : canManage ? 'Initialize production lifecycle schema' : 'Administrator authority required'}</button> : null} />
    <div className="celar-production-governance-grid">{trust.map(([name, copy]) => <article key={name}><strong>{name}</strong><p>{copy}</p></article>)}</div>
    <div className="celar-production-policy"><h3>Non-negotiable boundaries</h3><ul><li>Authorization and owning-module scope are resolved before retrieval.</li><li>Raw SOW, GSD, IQS, customer, employee, financial, credential, and architecture content is not sent to a public provider.</li><li>Module 064 owns provider credentials, health, routing, circuit breakers, usage, and optional sanitized fallback.</li><li>A provider safety refusal ends the route.</li><li>Fine-tuning uses reviewed immutable private artifacts and never learns automatically from every conversation.</li><li>View-As is read-only and cannot initialize schema, register datasets, submit training, register models, or create deployment plans.</li><li>Celar AI cannot submit time, publish a SOW, baseline a plan, assign resources, commit customer dates, change financials, grant permissions, apply a deployment, or promote its own model.</li></ul></div>
    <pre className="celar-production-json">{JSON.stringify({ lifecycle, privateTraining: readiness?.privateTraining, capabilityRouting: readiness?.capabilityRouting, access: readiness?.access }, null, 2)}</pre>
  </section>;
}

export default function CelarAiProductionPlatform() {
  const [activeTab, setActiveTab] = useState('overview');
  const [readiness, setReadiness] = useState(null);
  const [records, setRecords] = useState({ datasets: [], trainingJobs: [], evaluations: [], models: [], deployments: [] });
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');

  const loadReadiness = useCallback(async () => {
    setLoading(true); setError('');
    try { setReadiness(await getJson('/api/celar-ai/v1/production/readiness')); }
    catch (requestError) { setError(requestError.message); } finally { setLoading(false); }
  }, []);
  const loadRecords = useCallback(async (schemaReady = readiness?.lifecycle?.schemaReady) => {
    if (!schemaReady) { setRecords({ datasets: [], trainingJobs: [], evaluations: [], models: [], deployments: [] }); return; }
    try {
      const [datasets, trainingJobs, evaluations, models, deployments] = await Promise.all([
        getJson('/api/celar-ai/v1/production/datasets'),
        getJson('/api/celar-ai/v1/production/training-jobs'),
        getJson('/api/celar-ai/v1/production/evaluations'),
        getJson('/api/celar-ai/v1/production/models'),
        getJson('/api/celar-ai/v1/production/deployments')
      ]);
      setRecords({ datasets: asArray(datasets.datasets), trainingJobs: asArray(trainingJobs.trainingJobs), evaluations: asArray(evaluations.evaluations), models: asArray(models.models), deployments: asArray(deployments.deployments) });
    } catch (requestError) { setError(requestError.message); }
  }, [readiness?.lifecycle?.schemaReady]);
  const refresh = useCallback(async () => {
    setLoading(true); setError('');
    try {
      const next = await getJson('/api/celar-ai/v1/production/readiness');
      setReadiness(next);
      await loadRecords(next?.lifecycle?.schemaReady);
    } catch (requestError) { setError(requestError.message); } finally { setLoading(false); }
  }, [loadRecords]);
  useEffect(() => { void loadReadiness(); }, [loadReadiness]);
  useEffect(() => { void loadRecords(); }, [loadRecords]);

  async function initialize() {
    setBusy(true); setError(''); setNotice('');
    try { await postJson('/api/celar-ai/v1/production/schema/initialize', {}); setNotice('Celar AI production lifecycle schema initialized.'); await refresh(); }
    catch (requestError) { setError(requestError.message); } finally { setBusy(false); }
  }

  const canManage = readiness?.access?.canManage === true;
  const lifecycle = readiness?.lifecycle || {};
  const rag = readiness?.privateRag || {};
  const training = readiness?.privateTraining || {};
  const competency = readiness?.competency || {};
  const counts = lifecycle?.counts || {};
  const activeDefinition = TABS.find(([id]) => id === activeTab) || TABS[0];

  const tabContent = useMemo(() => {
    if (activeTab === 'overview') return <CelarAiEnterprisePlatform />;
    if (activeTab === 'knowledge') return <div className="celar-production-stack"><PulseAiPrivateDocumentPipelineWorkbench /><PulseAiPrivateRagWorkbench /><PulseAiPrivateRuntimeWorkbench /></div>;
    if (activeTab === 'tools') return <PulseAiSystemIntelligenceWorkbench />;
    if (activeTab === 'datasets') return <DatasetWorkspace data={records.datasets} refresh={refresh} canManage={canManage} />;
    if (activeTab === 'training') return <TrainingWorkspace data={records.trainingJobs} datasets={records.datasets} refresh={refresh} canManage={canManage} readiness={training} />;
    if (activeTab === 'evaluations') return <EvaluationWorkspace data={records.evaluations} refresh={refresh} canManage={canManage} competency={competency} />;
    if (activeTab === 'registry') return <RegistryWorkspace data={records.models} datasets={records.datasets} jobs={records.trainingJobs} evaluations={records.evaluations} refresh={refresh} canManage={canManage} />;
    if (activeTab === 'deployments') return <DeploymentWorkspace data={records.deployments} models={records.models} refresh={refresh} canManage={canManage} />;
    return <GovernanceWorkspace readiness={readiness} canManage={canManage} initialize={initialize} busy={busy} />;
  }, [activeTab, busy, canManage, competency, readiness, records, refresh, training]);

  return <main className="celar-production-platform projectpulse-module-standard" data-module="011" data-authoritative-celar-ai-platform="production-v1">
    <header className="celar-production-hero"><div className="celar-production-brand"><img src={usSignalLogoDataUrl} alt="US Signal" /><div><p>Module 011 · Celar AI production platform</p><h1>Celar AI</h1><span>Private operational intelligence, answer correctness, knowledge and RAG, governed tools, fine-tuning, evaluations, model registry, deployments, and reviewable delivery artifacts.</span></div></div><div className="celar-production-actions"><button type="button" onClick={refresh} disabled={loading}>{loading ? 'Checking…' : 'Refresh platform'}</button><a href="#ai-provider-configuration">Open Module 064</a></div></header>
    <div className="celar-production-identity"><strong>Created by Dr. Ahmed Adeyemi</strong><span>Manager of Professional Services</span><span>Speed of light. Speed of delivery.</span></div>
    <Notice notice={notice} error={error} />
    <section className="celar-production-summary" aria-label="Celar AI production readiness"><StatusCard label="Lifecycle schema" value={lifecycle.schemaReady ? 'Ready' : title(lifecycle.status || 'Not checked')} detail={lifecycle.schemaVersion || 'Production runtime schema'} tone={lifecycle.schemaReady ? 'ready' : 'warning'} /><StatusCard label="Basic competency" value={formatPercent(competency.currentPassRate)} detail={`${competency.passed || 0}/${competency.total || 0} required intent routes`} tone={competency.currentPassRate === 1 ? 'ready' : 'warning'} /><StatusCard label="Private RAG" value={title(rag.status || 'Not checked')} detail={rag.inferenceConfigured ? 'Private model configured' : 'Private model configuration may be required'} tone={rag.status === 'private_rag_ready' ? 'ready' : 'warning'} /><StatusCard label="Private training" value={title(training.status || 'Not checked')} detail={training.configured ? 'Private training endpoint configured' : 'Optional private compute not configured'} tone={training.status === 'private_training_route_ready' ? 'ready' : 'neutral'} /><StatusCard label="Lifecycle records" value={String(Object.values(counts).reduce((sum, value) => sum + Number(value || 0), 0))} detail="Datasets, jobs, evaluations, models, deployments, and quality evidence" tone="neutral" /></section>
    <div className="celar-production-layout"><nav aria-label="Celar AI production workspaces">{TABS.map(([id, label, description]) => <button type="button" key={id} className={activeTab === id ? 'is-active' : ''} onClick={() => setActiveTab(id)}><strong>{label}</strong><span>{description}</span></button>)}</nav><section className="celar-production-content" aria-labelledby={`celar-tab-${activeTab}`}><div className="celar-production-current-tab"><p>Celar AI workspace</p><h2 id={`celar-tab-${activeTab}`}>{activeDefinition[1]}</h2><span>{activeDefinition[2]}</span></div>{tabContent}</section></div>
  </main>;
}