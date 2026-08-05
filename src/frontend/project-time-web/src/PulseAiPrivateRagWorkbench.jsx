import { useCallback, useEffect, useMemo, useState } from 'react';
import './pulse-ai-private-rag-workbench.css';

const TABS = Object.freeze([
  { id: 'readiness', label: 'Private RAG Readiness', description: 'Schema, inference, embeddings, policy, and answer thresholds' },
  { id: 'help', label: 'Help & Search', description: 'Detailed private product and project-document answers' },
  { id: 'timesheet', label: 'Timesheet Suggestion', description: 'Engineer-reviewed SOW/GSD-grounded descriptions' },
  { id: 'flowhive', label: 'FlowHive Draft', description: 'Cited WBS, milestones, dependencies, risks, and assumptions' },
  { id: 'audit', label: 'Answer Audit & Feedback', description: 'Citations, confidence, source health, and controlled feedback' }
]);

const EMPTY_HELP = Object.freeze({ question: '', projectCode: '', projectName: '', detailLevel: 'comprehensive', includeAuthorizedProjectDocuments: true });
const EMPTY_TIME = Object.freeze({ workDate: '', timeType: 'normal', rowType: 'project', rowLabel: '', projectCode: '', projectName: '', taskCode: '', taskName: '', categoryCode: '', engineerNote: '' });
const EMPTY_FLOW = Object.freeze({ projectCode: '', projectName: '', requestedOutcome: '', detailLevel: 'comprehensive' });

function asArray(value) { return Array.isArray(value) ? value : []; }
function title(value) { return String(value ?? '').replaceAll('_', ' ').replace(/\b\w/g, (letter) => letter.toUpperCase()); }
function formatDate(value) {
  if (!value) return 'Not recorded';
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? String(value) : parsed.toLocaleString();
}

async function readJson(response) {
  const payload = await response.json().catch(() => ({}));
  if (!response.ok) throw new Error(payload.message || `${response.url || 'Request'} returned HTTP ${response.status}.`);
  return payload;
}
async function getJson(path) { return readJson(await fetch(path, { method: 'GET', cache: 'no-store', headers: { Accept: 'application/json' } })); }
async function postJson(path, body) { return readJson(await fetch(path, { method: 'POST', cache: 'no-store', headers: { Accept: 'application/json', 'Content-Type': 'application/json' }, body: JSON.stringify(body) })); }

function Status({ value }) {
  const normalized = String(value || 'unknown').toLowerCase();
  const ready = normalized.includes('ready') || normalized.includes('completed');
  const failed = normalized.includes('failed') || normalized.includes('blocked') || normalized.includes('insufficient');
  return <span className={`pulse-ai-rag-status ${ready ? 'is-ready' : failed ? 'is-failed' : 'is-partial'}`}>{title(normalized)}</span>;
}

function KeyValues({ values }) {
  return <dl className="pulse-ai-rag-key-values">{Object.entries(values ?? {}).filter(([, value]) => value !== undefined).map(([key, value]) => (
    <div key={key}><dt>{title(key)}</dt><dd>{typeof value === 'boolean' ? (value ? 'Yes' : 'No') : String(value ?? 'Not recorded')}</dd></div>
  ))}</dl>;
}

function ListBlock({ heading, values, empty = 'Nothing recorded.' }) {
  const rows = asArray(values);
  return <section className="pulse-ai-rag-list"><h5>{heading}</h5>{rows.length ? <ul>{rows.map((value, index) => <li key={`${heading}-${index}`}>{String(value)}</li>)}</ul> : <p>{empty}</p>}</section>;
}

function CitationGrid({ citations }) {
  const rows = asArray(citations);
  return <section className="pulse-ai-rag-card"><div className="pulse-ai-rag-card-heading"><div><h5>Private source citations</h5><p>Current document version, page or section, and checksums. Raw chunk text and vectors are not returned.</p></div><span>{rows.length} source{rows.length === 1 ? '' : 's'}</span></div>
    {rows.length ? <div className="pulse-ai-rag-citation-grid">{rows.map((citation) => <article key={`${citation.citationId}-${citation.documentId}-${citation.citationAnchor}`}>
      <div><strong>[{citation.citationId}] {String(citation.documentCategory || 'document').toUpperCase()}</strong><span>{Math.round(Number(citation.relevanceScore || 0) * 100)}% relevance</span></div>
      <h6>{citation.originalFileName}</h6>
      <p>{citation.documentVersion}</p>
      <small>{citation.citationAnchor} · Page {citation.pageNumber || 'not recorded'} · Processed {formatDate(citation.processedAt)}</small>
    </article>)}</div> : <p className="pulse-ai-rag-empty">No private citation was returned.</p>}
  </section>;
}

function DetailedAnswer({ result }) {
  const answer = result?.answer;
  if (!answer) return null;
  return <div className="pulse-ai-rag-result-stack">
    <section className="pulse-ai-rag-conclusion"><p className="pulse-ai-rag-eyebrow">Direct conclusion</p><h4>{answer.directConclusion}</h4>{answer.executiveSummary ? <p>{answer.executiveSummary}</p> : null}</section>
    <KeyValues values={{ confidence: `${Math.round(Number(answer.confidence || 0) * 100)}%`, confidenceExplanation: answer.confidenceExplanation, dataAsOf: formatDate(answer.dataAsOf) }} />
    <div className="pulse-ai-rag-three-column"><ListBlock heading="Scope and filters" values={answer.scopeAndFilters} /><ListBlock heading="Source evidence" values={answer.sourceEvidence} /><ListBlock heading="Known, unknown, and stale values" values={answer.knownUnknownAndStaleValues} /></div>
    <ListBlock heading="Detailed analysis" values={answer.detailedAnalysis} />
    <div className="pulse-ai-rag-three-column"><ListBlock heading="Calculations" values={answer.calculations} /><ListBlock heading="Assumptions" values={answer.assumptions} /><ListBlock heading="Conflicts" values={answer.conflicts} /></div>
    <div className="pulse-ai-rag-three-column"><ListBlock heading="Limitations" values={answer.limitations} /><ListBlock heading="Risks and implications" values={answer.risksAndImplications} /><ListBlock heading="Recommended actions" values={answer.recommendedActions} /></div>
    <ListBlock heading="Pulse navigation" values={answer.navigationTargets} />
  </div>;
}

function FlowHivePlan({ result }) {
  const plan = result?.flowHivePlan;
  if (!plan) return null;
  return <div className="pulse-ai-rag-result-stack">
    <section className="pulse-ai-rag-conclusion"><p className="pulse-ai-rag-eyebrow">FlowHive draft objective</p><h4>{plan.objective}</h4><p>{plan.confidenceExplanation}</p></section>
    <KeyValues values={{ confidence: `${Math.round(Number(plan.confidence || 0) * 100)}%`, taskCount: asArray(plan.tasks).length, milestoneCount: asArray(plan.milestones).length }} />
    <section className="pulse-ai-rag-card"><h5>Draft work breakdown structure</h5><div className="pulse-ai-rag-task-grid">{asArray(plan.tasks).map((task, index) => <article key={`${task.wbs}-${index}`}>
      <div><strong>{task.wbs} · {task.name}</strong><span>{task.estimatedDurationDays} day(s){task.isAssumption ? ' · assumption' : ''}</span></div><p>{task.description}</p><small>Roles: {asArray(task.requiredRoles).join(', ') || 'Not specified'} · Predecessors: {asArray(task.predecessors).join(', ') || 'None'} · Citations: {asArray(task.citationIds).join(', ') || 'None'}</small>
    </article>)}</div></section>
    <section className="pulse-ai-rag-card"><h5>Draft milestones</h5><div className="pulse-ai-rag-task-grid">{asArray(plan.milestones).map((milestone, index) => <article key={`${milestone.name}-${index}`}><div><strong>{milestone.name}</strong><span>{milestone.proposedTiming}</span></div><p>{milestone.description}</p><small>Acceptance: {asArray(milestone.acceptanceEvidence).join('; ') || 'Not specified'} · Citations: {asArray(milestone.citationIds).join(', ') || 'None'}</small></article>)}</div></section>
    <div className="pulse-ai-rag-three-column"><ListBlock heading="Dependencies" values={plan.dependencies} /><ListBlock heading="Required roles" values={plan.requiredRoles} /><ListBlock heading="Assumptions" values={plan.assumptions} /></div>
    <div className="pulse-ai-rag-three-column"><ListBlock heading="Risks" values={plan.risks} /><ListBlock heading="Out of scope" values={plan.outOfScopeItems} /><ListBlock heading="Open questions" values={plan.openQuestions} /></div>
    <ListBlock heading="Source conflicts" values={plan.conflicts} />
  </div>;
}

function ResultPanel({ payload }) {
  const result = payload?.result;
  if (!result) return null;
  return <div className="pulse-ai-rag-result-stack">
    <section className="pulse-ai-rag-result-hero"><div><p className="pulse-ai-rag-eyebrow">Private RAG result</p><h4>{title(result.featureCode)}</h4><p>{result.project?.projectCode || 'No project'} — {result.project?.projectName || 'Product and system scope'}</p></div><Status value={result.status} /></section>
    <KeyValues values={{ answerRunId: result.answerRunId, retrievalMode: result.retrievalMode, modelProvider: result.modelProvider || 'Not called', modelName: result.modelName || 'Not called', coverageScore: `${Math.round(Number(result.coverageScore || 0) * 100)}%`, citationCoverageScore: `${Math.round(Number(result.citationCoverageScore || 0) * 100)}%`, dataAsOf: formatDate(result.dataAsOf), diagnosticCode: result.diagnosticCode || 'None' }} />
    <DetailedAnswer result={result} /><FlowHivePlan result={result} />
    <div className="pulse-ai-rag-three-column"><ListBlock heading="Warnings" values={result.warnings} /><ListBlock heading="Missing evidence" values={result.missingEvidence} /><ListBlock heading="Conflicts" values={result.conflicts} /></div>
    <CitationGrid citations={result.citations} />
    <details className="pulse-ai-rag-evidence"><summary>View complete structured result</summary><pre>{JSON.stringify(payload, null, 2)}</pre></details>
  </div>;
}

export default function PulseAiPrivateRagWorkbench() {
  const [activeTab, setActiveTab] = useState('readiness');
  const [readiness, setReadiness] = useState(null);
  const [help, setHelp] = useState({ ...EMPTY_HELP });
  const [timesheet, setTimesheet] = useState({ ...EMPTY_TIME });
  const [flowHive, setFlowHive] = useState({ ...EMPTY_FLOW });
  const [result, setResult] = useState(null);
  const [answerRunId, setAnswerRunId] = useState('');
  const [audit, setAudit] = useState(null);
  const [feedback, setFeedback] = useState({ feedbackType: 'accepted', feedbackReason: '' });
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');
  const tab = useMemo(() => TABS.find((item) => item.id === activeTab) ?? TABS[0], [activeTab]);

  const loadReadiness = useCallback(async () => { setBusy(true); setError(''); try { setReadiness(await getJson('/api/pulse-ai/v1/rag/readiness')); } catch (loadError) { setError(loadError instanceof Error ? loadError.message : 'Private RAG readiness could not be loaded.'); } finally { setBusy(false); } }, []);
  useEffect(() => { void loadReadiness(); }, [loadReadiness]);

  async function run(endpoint, body) {
    setBusy(true); setError(''); setNotice(''); setResult(null);
    try { const payload = await postJson(endpoint, body); setResult(payload); const id = payload?.result?.answerRunId; if (id) setAnswerRunId(id); }
    catch (runError) { setError(runError instanceof Error ? runError.message : 'The private RAG request could not be completed.'); }
    finally { setBusy(false); }
  }

  async function loadAudit(event) { event.preventDefault(); setBusy(true); setError(''); setNotice(''); try { setAudit(await getJson(`/api/pulse-ai/v1/rag/answers/${encodeURIComponent(answerRunId.trim())}`)); } catch (loadError) { setError(loadError instanceof Error ? loadError.message : 'Answer audit could not be loaded.'); } finally { setBusy(false); } }
  async function submitFeedback(event) { event.preventDefault(); setBusy(true); setError(''); setNotice(''); try { const payload = await postJson(`/api/pulse-ai/v1/rag/answers/${encodeURIComponent(answerRunId.trim())}/feedback`, { feedbackType: feedback.feedbackType, feedbackReason: feedback.feedbackReason, requestTrainingCandidate: false }); setNotice(title(payload.status)); } catch (actionError) { setError(actionError instanceof Error ? actionError.message : 'Feedback could not be recorded.'); } finally { setBusy(false); } }

  const readinessValue = readiness?.readiness;
  return <section className="pulse-ai-rag-workbench" data-pulse-ai-private-rag="v1">
    <header className="pulse-ai-rag-header"><div><p className="pulse-ai-rag-eyebrow">Module 011 · Phase 011D</p><h2>Live Private RAG for Timesheet, Help/Search & FlowHive</h2><p>Retrieve current authorized document evidence, reason with the approved private Celar AI model, verify citations, preserve comprehensive answer evidence, and keep raw internal context out of Claude and OpenAI.</p></div><span>Private model first</span></header>
    <div className="pulse-ai-rag-warning"><strong>Human-controlled results:</strong> Timesheet descriptions require Engineer review. FlowHive output remains a PM/Engineering draft. Help/Search answers expose sources, missing evidence, limitations, and confidence.</div>
    <nav className="pulse-ai-rag-tabs" aria-label="Celar AI private RAG workspaces">{TABS.map((item) => <button type="button" className={activeTab === item.id ? 'is-active' : ''} key={item.id} onClick={() => setActiveTab(item.id)}><strong>{item.label}</strong><span>{item.description}</span></button>)}</nav>
    <div className="pulse-ai-rag-panel"><div className="pulse-ai-rag-panel-heading"><div><p className="pulse-ai-rag-eyebrow">Private RAG workspace</p><h3>{tab.label}</h3><p>{tab.description}</p></div>{activeTab === 'readiness' ? <button type="button" onClick={loadReadiness} disabled={busy}>Refresh readiness</button> : null}</div>
      {busy ? <div className="pulse-ai-rag-loading" role="status">Running the authorized private Celar AI request…</div> : null}{error ? <div className="pulse-ai-rag-error" role="alert">{error}</div> : null}{notice ? <div className="pulse-ai-rag-notice" role="status">{notice}</div> : null}
      {activeTab === 'readiness' && readinessValue ? <div className="pulse-ai-rag-result-stack"><section className="pulse-ai-rag-result-hero"><div><p className="pulse-ai-rag-eyebrow">Private RAG readiness</p><h4>{title(readinessValue.status)}</h4><p>Source deployment alone does not enable private model execution.</p></div><Status value={readinessValue.status} /></section><KeyValues values={readinessValue} /><ListBlock heading="Current blockers" values={readinessValue.blockers} empty="No blocker was reported." /><details className="pulse-ai-rag-evidence"><summary>View complete readiness evidence</summary><pre>{JSON.stringify(readiness, null, 2)}</pre></details></div> : null}
      {activeTab === 'help' ? <><form className="pulse-ai-rag-form" onSubmit={(event) => { event.preventDefault(); void run('/api/pulse-ai/v1/rag/help-search', help); }}><label>Question<textarea required rows="5" value={help.question} onChange={(event) => setHelp((current) => ({ ...current, question: event.target.value }))} placeholder="Ask a detailed question about Pulse, a project, authorized documents, workflows, reports, or system behavior." /></label><div className="pulse-ai-rag-form-grid"><label>Project code<input value={help.projectCode} onChange={(event) => setHelp((current) => ({ ...current, projectCode: event.target.value }))} /></label><label>Project name<input value={help.projectName} onChange={(event) => setHelp((current) => ({ ...current, projectName: event.target.value }))} /></label></div><label>Detail level<select value={help.detailLevel} onChange={(event) => setHelp((current) => ({ ...current, detailLevel: event.target.value }))}><option value="comprehensive">Comprehensive</option><option value="executive_and_detailed">Executive and detailed</option><option value="detailed">Detailed</option><option value="standard">Standard</option></select></label><button type="submit" disabled={busy}>Ask private Celar AI</button></form><ResultPanel payload={result} /></> : null}
      {activeTab === 'timesheet' ? <><form className="pulse-ai-rag-form" onSubmit={(event) => { event.preventDefault(); void run('/api/pulse-ai/v1/rag/timesheet-suggestion', { ...timesheet, workDate: timesheet.workDate || null }); }}><div className="pulse-ai-rag-form-grid"><label>Project code<input required value={timesheet.projectCode} onChange={(event) => setTimesheet((current) => ({ ...current, projectCode: event.target.value }))} /></label><label>Project name<input value={timesheet.projectName} onChange={(event) => setTimesheet((current) => ({ ...current, projectName: event.target.value }))} /></label><label>Task code<input value={timesheet.taskCode} onChange={(event) => setTimesheet((current) => ({ ...current, taskCode: event.target.value }))} /></label><label>Task name<input value={timesheet.taskName} onChange={(event) => setTimesheet((current) => ({ ...current, taskName: event.target.value }))} /></label><label>Work date<input type="date" value={timesheet.workDate} onChange={(event) => setTimesheet((current) => ({ ...current, workDate: event.target.value }))} /></label><label>Row label<input value={timesheet.rowLabel} onChange={(event) => setTimesheet((current) => ({ ...current, rowLabel: event.target.value }))} /></label></div><label>Engineer rough note<textarea rows="5" value={timesheet.engineerNote} onChange={(event) => setTimesheet((current) => ({ ...current, engineerNote: event.target.value }))} placeholder="Describe the work actually performed. The SOW/GSD cannot prove unreported activity." /></label><button type="submit" disabled={busy}>Generate private suggestion</button></form><ResultPanel payload={result} /></> : null}
      {activeTab === 'flowhive' ? <><form className="pulse-ai-rag-form" onSubmit={(event) => { event.preventDefault(); void run('/api/pulse-ai/v1/rag/flowhive-plan', flowHive); }}><div className="pulse-ai-rag-form-grid"><label>Project code<input required value={flowHive.projectCode} onChange={(event) => setFlowHive((current) => ({ ...current, projectCode: event.target.value }))} /></label><label>Project name<input value={flowHive.projectName} onChange={(event) => setFlowHive((current) => ({ ...current, projectName: event.target.value }))} /></label></div><label>Requested planning outcome<textarea rows="5" value={flowHive.requestedOutcome} onChange={(event) => setFlowHive((current) => ({ ...current, requestedOutcome: event.target.value }))} placeholder="Describe the project-plan outcome the PM and Engineering need to review." /></label><button type="submit" disabled={busy}>Generate private FlowHive draft</button></form><ResultPanel payload={result} /></> : null}
      {activeTab === 'audit' ? <><form className="pulse-ai-rag-filter" onSubmit={loadAudit}><label>Answer run ID<input required value={answerRunId} onChange={(event) => setAnswerRunId(event.target.value)} placeholder="00000000-0000-0000-0000-000000000000" /></label><button type="submit" disabled={busy}>Load answer audit</button></form>{audit ? <details open className="pulse-ai-rag-evidence"><summary>Authorized answer audit</summary><pre>{JSON.stringify(audit, null, 2)}</pre></details> : null}<form className="pulse-ai-rag-form" onSubmit={submitFeedback}><label>Feedback type<select value={feedback.feedbackType} onChange={(event) => setFeedback((current) => ({ ...current, feedbackType: event.target.value }))}><option value="accepted">Accepted</option><option value="accepted_with_edits">Accepted with edits</option><option value="rejected">Rejected</option><option value="incorrect">Incorrect</option><option value="incomplete">Incomplete</option><option value="unsafe">Unsafe</option><option value="unauthorized_source">Unauthorized source</option><option value="other">Other</option></select></label><label>Feedback reason<textarea rows="4" value={feedback.feedbackReason} onChange={(event) => setFeedback((current) => ({ ...current, feedbackReason: event.target.value }))} /></label><button type="submit" disabled={busy || !answerRunId.trim()}>Record feedback</button><p>Feedback is not automatically approved as training data. Dataset review, redaction, versioning, and approval remain separate.</p></form></> : null}
    </div>
  </section>;
}
