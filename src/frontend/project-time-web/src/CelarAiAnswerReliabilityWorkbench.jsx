import { useEffect, useMemo, useState } from 'react';
import './celar-ai-answer-reliability-workbench.css';

const SAMPLES = Object.freeze([
  'How many active projects does Kevin Damisch have?',
  'What deliverables are included in the approved SOW?',
  'Are all contracted deliverables represented in the current project tasks?',
  'Which projects are currently over budget?',
  'Who has not submitted a timesheet this week?',
  'Why is Module 082 returning an error?',
  'Who is the current US President?',
  'What should Celar AI do when no authoritative source exists?'
]);

function asArray(value) { return Array.isArray(value) ? value : []; }
function title(value) { return String(value ?? '').replaceAll('_', ' ').replace(/\b\w/g, (letter) => letter.toUpperCase()); }
async function readJson(response) {
  const payload = await response.json().catch(() => ({}));
  if (!response.ok) throw new Error(payload.message || payload.detail || `Request returned HTTP ${response.status}.`);
  return payload;
}

function Metric({ label, value, detail, tone = 'neutral' }) {
  return <article className={`celar-reliability-metric is-${tone}`}><span>{label}</span><strong>{value}</strong><small>{detail}</small></article>;
}

export default function CelarAiAnswerReliabilityWorkbench() {
  const [readiness, setReadiness] = useState(null);
  const [question, setQuestion] = useState(SAMPLES[0]);
  const [plan, setPlan] = useState(null);
  const [loading, setLoading] = useState(true);
  const [planning, setPlanning] = useState(false);
  const [error, setError] = useState('');

  async function loadReadiness() {
    setLoading(true); setError('');
    try {
      setReadiness(await readJson(await fetch('/api/celar-ai/v1/reliability/readiness', { cache: 'no-store', headers: { Accept: 'application/json' } })));
    } catch (requestError) { setError(requestError.message); } finally { setLoading(false); }
  }

  async function submit(event) {
    event.preventDefault();
    setPlanning(true); setError(''); setPlan(null);
    try {
      setPlan(await readJson(await fetch('/api/celar-ai/v1/reliability/plan', {
        method: 'POST', cache: 'no-store', headers: { Accept: 'application/json', 'Content-Type': 'application/json' },
        body: JSON.stringify({ question, includeRepositoryContext: false, attachmentCount: 0 })
      })));
    } catch (requestError) { setError(requestError.message); } finally { setPlanning(false); }
  }

  useEffect(() => { void loadReadiness(); }, []);

  const summary = readiness?.readiness || {};
  const tools = asArray(readiness?.tools);
  const domains = useMemo(() => {
    const grouped = new Map();
    tools.forEach((tool) => {
      const key = tool.domain || 'unclassified';
      grouped.set(key, [...(grouped.get(key) || []), tool]);
    });
    return [...grouped.entries()].sort(([left], [right]) => left.localeCompare(right));
  }, [tools]);
  const activeTools = Number(summary.existingAdapterCount || 0) + Number(summary.protectedTestRuntimeCount || 0);
  const coverage = summary.toolCount ? Math.min(100, Math.round((activeTools / summary.toolCount) * 100)) : 0;

  return <section className="celar-reliability-workbench" aria-labelledby="celar-reliability-heading">
    <header className="celar-reliability-heading">
      <div><p>Ask Celar AI · universal answer reliability</p><h2 id="celar-reliability-heading">Authoritative sources before fluent answers</h2><span>Every question is classified, mapped to governed evidence, checked for freshness and citations, and downgraded when the required source set is missing.</span></div>
      <button type="button" onClick={loadReadiness} disabled={loading}>{loading ? 'Checking…' : 'Refresh reliability'}</button>
    </header>

    {error ? <div className="celar-reliability-error" role="alert"><strong>Reliability request did not complete</strong><span>{error}</span></div> : null}

    <div className="celar-reliability-metrics" aria-label="Universal answer reliability summary">
      <Metric label="Reliability contract" value={summary.status ? 'Source ready' : 'Not checked'} detail={summary.contractVersion || 'Universal evidence quality gate'} tone={summary.status ? 'ready' : 'warning'} />
      <Metric label="Governed tools" value={String(summary.toolCount || 0)} detail={`${summary.domainCount || 0} evidence domains`} tone="ready" />
      <Metric label="Current adapter coverage" value={`${coverage}%`} detail={`${summary.existingAdapterCount || 0} existing · ${summary.catalogedAdapterGapCount || 0} adapter gaps`} tone={summary.catalogedAdapterGapCount ? 'warning' : 'ready'} />
      <Metric label="Frozen regression corpus" value={String(summary.evaluationCaseCount || 0)} detail="Correctness, citation, freshness, permission, privacy, and failure tests" tone={summary.evaluationCaseCount >= 100 ? 'ready' : 'warning'} />
    </div>

    <section className="celar-reliability-principles">
      <article><strong>Structured facts</strong><p>Projects, people, time, approvals, capacity, financials, risks, and audit questions must use current permission-scoped Pulse records and deterministic calculations.</p></article>
      <article><strong>Document facts</strong><p>SOW, GSD, design, and attachment claims require private malware scanning, extraction, OCR when needed, re-authorization, and citation-ready retrieval.</p></article>
      <article><strong>Cross-domain answers</strong><p>Delivery questions combine live operational data and authoritative document evidence. One source cannot silently substitute for the other.</p></article>
      <article><strong>Changing public facts</strong><p>Current officeholders, versions, regulations, news, schedules, and similar facts require retrieval-time public evidence rather than model memory.</p></article>
    </section>

    <div className="celar-reliability-layout">
      <form onSubmit={submit} className="celar-reliability-planner">
        <div><p>Read-only planner</p><h3>Preview the evidence contract for a question</h3><span>This preview calls no model, database, document service, or public provider and changes no state.</span></div>
        <label>Question<textarea value={question} onChange={(event) => setQuestion(event.target.value)} rows={5} maxLength={8000} /></label>
        <div className="celar-reliability-samples">{SAMPLES.map((sample) => <button type="button" key={sample} onClick={() => setQuestion(sample)}>{sample}</button>)}</div>
        <button type="submit" disabled={planning || !question.trim()}>{planning ? 'Planning…' : 'Build governed evidence plan'}</button>
      </form>

      <section className="celar-reliability-plan" aria-live="polite">
        <div><p>Resolved plan</p><h3>{plan?.plan?.questionClass ? title(plan.plan.questionClass) : 'Submit a sample question'}</h3><span>{plan?.status ? title(plan.status) : 'The plan will show required tools, evidence modes, source types, freshness, citations, calculations, and clarifications.'}</span></div>
        {plan?.plan ? <>
          <dl><div><dt>Intent</dt><dd>{title(plan.plan.intentCode)}</dd></div><div><dt>Minimum sources</dt><dd>{plan.plan.minimumAuthoritativeSources}</dd></div><div><dt>Freshness</dt><dd>{plan.plan.maximumEvidenceAgeSeconds} seconds</dd></div><div><dt>Citations</dt><dd>{plan.plan.requireCitations ? 'Required' : 'Optional'}</dd></div><div><dt>Deterministic calculation</dt><dd>{plan.plan.requireDeterministicCalculation ? 'Required' : 'Not required'}</dd></div><div><dt>External assistance</dt><dd>{plan.plan.permitSanitizedExternalAssistance ? 'Public-only route permitted' : 'Private/internal only'}</dd></div></dl>
          <div className="celar-reliability-tags"><strong>Required tools</strong>{asArray(plan.plan.requiredToolCodes).map((value) => <span key={value}>{title(value)}</span>)}</div>
          <div className="celar-reliability-tags"><strong>Evidence modes</strong>{asArray(plan.plan.requiredEvidenceModes).map((value) => <span key={value}>{title(value)}</span>)}</div>
          {asArray(plan.plan.clarificationsToRequest).length ? <div className="celar-reliability-clarifications"><strong>Clarifications to resolve</strong><ul>{asArray(plan.plan.clarificationsToRequest).map((value) => <li key={value}>{value}</li>)}</ul></div> : null}
          <div className="celar-reliability-fail-closed"><strong>Fail-closed conclusion</strong><p>{plan.plan.failClosedConclusion}</p></div>
        </> : <div className="celar-reliability-empty"><strong>No plan generated yet</strong><p>Select a sample or enter an internal, document, cross-domain, operational, or public question.</p></div>}
      </section>
    </div>

    <section className="celar-reliability-tool-matrix">
      <div><p>Authoritative source inventory</p><h3>{tools.length} governed tool capabilities across {domains.length} domains</h3><span>Cataloged does not mean universally active. Each row distinguishes an existing adapter, an Oracle protected-Test runtime capability, and an owning-module adapter still required.</span></div>
      {domains.map(([domain, domainTools]) => <details key={domain}><summary><strong>{title(domain)}</strong><span>{domainTools.length} tools</span></summary><div className="celar-reliability-table-wrap"><table><thead><tr><th>Capability</th><th>Modules</th><th>Availability</th><th>Authority</th><th>Access and freshness</th></tr></thead><tbody>{domainTools.map((tool) => <tr key={tool.code}><td><strong>{tool.displayName}</strong><code>{tool.code}</code></td><td>{asArray(tool.owningModules).join(', ')}</td><td><span className={`celar-reliability-state is-${String(tool.availability).includes('requires') ? 'gap' : 'ready'}`}>{title(tool.availability)}</span></td><td>{tool.authority}</td><td>{tool.accessPolicy}<small>{title(tool.freshnessClass)}</small></td></tr>)}</tbody></table></div></details>)}
    </section>

    <section className="celar-reliability-gates">
      <div><p>Promotion boundary</p><h3>What must pass before this becomes a Production claim</h3></div>
      <ul>{asArray(summary.activationGates).map((gate) => <li key={gate}>{gate}</li>)}</ul>
      <div className="celar-reliability-boundary"><strong>Not performed by this workspace</strong><span>No database migration, provider change, secret read, model download, Oracle mutation, deployment, Production activation, generated SQL, or autonomous action.</span></div>
    </section>
  </section>;
}