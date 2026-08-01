import { useEffect, useMemo, useState } from 'react';
import './pulse-ai-deep-intelligence-workbench.css';

const WORKSPACES = Object.freeze([
  { id: 'readiness', label: 'Private Runtime', description: 'Document, model, embedding, and index readiness' },
  { id: 'timesheet', label: 'Timesheet Grounding', description: 'SOW/GSD evidence for Module 001' },
  { id: 'help', label: 'Help & Search', description: 'Detailed product and multi-tool answer planning' },
  { id: 'flowhive', label: 'FlowHive Planning', description: 'Project-plan source coverage and output contract' },
  { id: 'insight', label: 'Reports & Financials', description: 'Governed semantic query and calculation plan' },
  { id: 'privacy', label: 'Privacy Capsule', description: 'Preview external redaction without provider execution' },
  { id: 'tools', label: 'Tool Registry', description: 'Authorized read-only system data surfaces' }
]);

const INITIAL_TIMESHEET = Object.freeze({
  projectCode: '',
  projectName: '',
  taskCode: '',
  taskName: '',
  rowLabel: '',
  currentDescription: '',
  workDate: '',
  timeType: 'normal',
  rowType: 'project'
});

const INITIAL_FLOWHIVE = Object.freeze({
  projectCode: '',
  projectName: '',
  requestedOutcome: ''
});

const INITIAL_PRIVACY = Object.freeze({
  purpose: 'generic technical reasoning support',
  classification: 'restricted',
  sensitiveTerms: '',
  content: ''
});

function asArray(value) {
  return Array.isArray(value) ? value : [];
}

function title(value) {
  return String(value ?? '')
    .replaceAll('_', ' ')
    .replace(/\b\w/g, (letter) => letter.toUpperCase());
}

function formatDate(value) {
  if (!value) return 'Not recorded';
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? String(value) : parsed.toLocaleString();
}

function buildQuery(path, values) {
  const url = new URL(path, window.location.origin);
  Object.entries(values).forEach(([key, value]) => {
    const clean = String(value ?? '').trim();
    if (clean) url.searchParams.set(key, clean);
  });
  return `${url.pathname}${url.search}`;
}

async function readJson(response) {
  const payload = await response.json().catch(() => ({}));
  if (!response.ok) {
    throw new Error(payload.message || `${response.url || 'Request'} returned HTTP ${response.status}.`);
  }
  return payload;
}

async function getJson(path) {
  return readJson(await fetch(path, {
    method: 'GET',
    cache: 'no-store',
    headers: { Accept: 'application/json' }
  }));
}

async function postJson(path, body) {
  return readJson(await fetch(path, {
    method: 'POST',
    cache: 'no-store',
    headers: {
      Accept: 'application/json',
      'Content-Type': 'application/json'
    },
    body: JSON.stringify(body)
  }));
}

function ListBlock({ title: blockTitle, values, empty = 'Nothing recorded.' }) {
  const rows = asArray(values);
  return (
    <section className="pulse-ai-deep-list-block">
      <h5>{blockTitle}</h5>
      {rows.length ? (
        <ul>{rows.map((value, index) => <li key={`${blockTitle}-${index}`}>{String(value)}</li>)}</ul>
      ) : <p>{empty}</p>}
    </section>
  );
}

function KeyValueGrid({ values }) {
  const rows = Object.entries(values ?? {}).filter(([, value]) => value !== undefined);
  return (
    <dl className="pulse-ai-deep-key-value-grid">
      {rows.map(([key, value]) => (
        <div key={key}>
          <dt>{title(key)}</dt>
          <dd>{typeof value === 'boolean' ? (value ? 'Yes' : 'No') : String(value ?? 'Not recorded')}</dd>
        </div>
      ))}
    </dl>
  );
}

function FullEvidence({ payload }) {
  if (!payload) return null;
  return (
    <details className="pulse-ai-deep-full-evidence">
      <summary>View complete structured evidence</summary>
      <pre>{JSON.stringify(payload, null, 2)}</pre>
    </details>
  );
}

function ErrorState({ error }) {
  return error ? <div className="pulse-ai-deep-error" role="alert">{error}</div> : null;
}

function LoadingState({ active, label = 'Loading detailed evidence…' }) {
  return active ? <div className="pulse-ai-deep-loading" role="status">{label}</div> : null;
}

function ReadinessResult({ payload }) {
  const readiness = payload?.readiness;
  if (!readiness) return null;
  const interpretation = payload?.interpretation ?? {};
  return (
    <div className="pulse-ai-deep-result-stack">
      <section className="pulse-ai-deep-result-hero">
        <div>
          <p className="pulse-ai-deep-eyebrow">Private runtime readiness</p>
          <h4>{title(readiness.status)}</h4>
          <p>Readiness is evaluated without exposing database connection details, provider secrets, raw document text, or private endpoint values.</p>
        </div>
        <span className={`pulse-ai-deep-status ${String(readiness.status).includes('ready') ? 'is-ready' : 'is-partial'}`}>
          {title(readiness.status)}
        </span>
      </section>

      <KeyValueGrid values={{
        databaseConfigured: readiness.databaseConfigured,
        documentTableAvailable: readiness.documentTableAvailable,
        engineeringVisibilityAvailable: readiness.engineeringVisibilityAvailable,
        timesheetContextFlagAvailable: readiness.timesheetContextFlagAvailable,
        extractionStatusAvailable: readiness.extractionStatusAvailable,
        contextSummaryAvailable: readiness.contextSummaryAvailable,
        privateInferenceEndpointConfigured: readiness.privateInferenceEndpointConfigured,
        privateEmbeddingEndpointConfigured: readiness.privateEmbeddingEndpointConfigured,
        privateVectorIndexConfigured: readiness.privateVectorIndexConfigured,
        authorizedDocumentCount: readiness.authorizedDocumentCount,
        authorizedAiContextDocumentCount: readiness.authorizedAiContextDocumentCount,
        authorizedReadyContextDocumentCount: readiness.authorizedReadyContextDocumentCount,
        generatedAt: formatDate(readiness.generatedAt)
      }} />

      <div className="pulse-ai-deep-two-column">
        <ListBlock title="Ready capabilities" values={readiness.readyCapabilities} />
        <ListBlock title="Current blockers" values={readiness.blockers} empty="No blocker was reported." />
      </div>

      <section className="pulse-ai-deep-evidence-card">
        <h5>Runtime interpretation</h5>
        <KeyValueGrid values={interpretation} />
      </section>
      <FullEvidence payload={payload} />
    </div>
  );
}

function GroundingResult({ payload, mode }) {
  const grounding = payload?.grounding;
  if (!grounding) return null;
  const project = grounding.project ?? {};
  const work = grounding.selectedWork ?? {};
  const coverage = grounding.sourceCoverage ?? {};
  const documents = asArray(grounding.documents);
  const privacy = grounding.privacy ?? {};

  return (
    <div className="pulse-ai-deep-result-stack">
      <section className="pulse-ai-deep-result-hero">
        <div>
          <p className="pulse-ai-deep-eyebrow">{mode === 'timesheet' ? 'Module 001 grounding evidence' : 'Module 066 planning evidence'}</p>
          <h4>{project.projectCode || 'Project not resolved'} — {project.projectName || 'No project name'}</h4>
          <p>{project.customerName || 'Customer unavailable'} · {title(project.projectStatus || 'unknown status')} · {title(project.accessScope || 'unresolved scope')}</p>
        </div>
        <span className={`pulse-ai-deep-status ${grounding.status === 'private_document_context_ready' ? 'is-ready' : 'is-partial'}`}>
          {title(grounding.status)}
        </span>
      </section>

      <KeyValueGrid values={{
        authorized: project.authorized,
        projectResolved: project.resolved,
        coverageLevel: coverage.level,
        coverageScore: coverage.score === undefined ? 'Not recorded' : `${Math.round(Number(coverage.score) * 100)}%`,
        documentCount: coverage.documentCount,
        readyPrivateContextCount: coverage.readyPrivateContextCount,
        sowCount: coverage.sowCount,
        gsdCount: coverage.gsdCount,
        generatedAt: formatDate(grounding.generatedAt)
      }} />

      {(work.taskName || work.requestFunction) ? (
        <section className="pulse-ai-deep-evidence-card">
          <h5>Selected work resolution</h5>
          <KeyValueGrid values={{
            taskCode: work.taskCode,
            taskName: work.taskName,
            taskDescription: work.taskDescription,
            requestNumber: work.requestNumber,
            requestFunction: work.requestFunction,
            requestStatus: work.requestStatus
          }} />
        </section>
      ) : null}

      <div className="pulse-ai-deep-three-column">
        <ListBlock title="Scope themes" values={coverage.scopeThemes} empty="No approved context theme was derived." />
        <ListBlock title="Missing inputs" values={coverage.missingInputs} empty="No missing input was reported." />
        <ListBlock title="Conflicts and version questions" values={coverage.conflicts} empty="No conflict was reported." />
      </div>

      <section className="pulse-ai-deep-evidence-card">
        <div className="pulse-ai-deep-card-heading">
          <div><h5>Authorized document evidence</h5><p>Metadata and readiness only. Raw text and private context summaries are not returned.</p></div>
          <span>{documents.length} source{documents.length === 1 ? '' : 's'}</span>
        </div>
        {documents.length ? (
          <div className="pulse-ai-deep-document-grid">
            {documents.map((document) => (
              <article key={document.documentId}>
                <div><strong>{String(document.documentCategory || document.documentType).toUpperCase()}</strong><span>{document.summaryReady ? 'Context ready' : title(document.extractionStatus)}</span></div>
                <h6>{document.originalFileName}</h6>
                <p>{document.sourceVersion}</p>
                <small>Uploaded {formatDate(document.uploadedAt)} · Processed {formatDate(document.contextLastProcessedAt)}</small>
              </article>
            ))}
          </div>
        ) : <p className="pulse-ai-deep-empty">No eligible document evidence was returned.</p>}
      </section>

      <section className="pulse-ai-deep-privacy-evidence">
        <h5>Privacy enforcement</h5>
        <KeyValueGrid values={{
          boundary: privacy.boundary,
          externalProviderPolicy: privacy.externalProviderPolicy,
          rawDocumentTextReturned: privacy.rawDocumentTextReturned,
          rawDocumentTextSentExternally: privacy.rawDocumentTextSentExternally
        }} />
      </section>

      {mode === 'flowhive' ? (
        <div className="pulse-ai-deep-two-column">
          <ListBlock title="Planning process" values={payload.planningProcess} />
          <ListBlock title="Restrictions" values={payload.restrictions} />
        </div>
      ) : (
        <ListBlock title="Timesheet output rules" values={payload.outputRules} />
      )}

      <FullEvidence payload={payload} />
    </div>
  );
}

function KnowledgeAnswer({ answer }) {
  if (!answer) return null;
  return (
    <section className="pulse-ai-deep-knowledge-answer">
      <p className="pulse-ai-deep-eyebrow">Detailed ProjectPulse answer</p>
      <h4>{answer.title}</h4>
      <p className="pulse-ai-deep-answer-summary">{answer.summary}</p>
      <div className="pulse-ai-deep-two-column">
        <ListBlock title="Detailed procedure" values={answer.detailedSteps} />
        <ListBlock title="Important rules" values={answer.importantRules} />
      </div>
      <div className="pulse-ai-deep-answer-meta">
        <span>Source modules: {asArray(answer.sourceModules).join(', ') || 'Not recorded'}</span>
        <span>Navigation: {asArray(answer.navigationTargets).join(' · ') || 'Not recorded'}</span>
      </div>
    </section>
  );
}

function PlanResult({ payload, mode }) {
  const plan = payload?.plan;
  if (!plan) return null;
  const semanticQuery = plan.semanticQuery ?? {};
  const tools = asArray(payload.selectedTools);

  return (
    <div className="pulse-ai-deep-result-stack">
      <KnowledgeAnswer answer={plan.directKnowledgeAnswer} />

      <section className="pulse-ai-deep-result-hero">
        <div>
          <p className="pulse-ai-deep-eyebrow">{mode === 'insight' ? 'Governed analytical plan' : 'Permission-aware answer plan'}</p>
          <h4>{title(plan.status)}</h4>
          <p>{plan.question}</p>
        </div>
        <span className="pulse-ai-deep-status is-ready">{title(plan.detailLevel)}</span>
      </section>

      <div className="pulse-ai-deep-three-column">
        <ListBlock title="Classified domains" values={plan.domains} />
        <ListBlock title="Owning modules" values={plan.owningModules} />
        <ListBlock title="Required tools" values={plan.requiredTools} />
      </div>

      <div className="pulse-ai-deep-two-column">
        <ListBlock title="Required evidence" values={plan.requiredEvidence} />
        <ListBlock title="Filters to resolve" values={plan.filtersToResolve} />
      </div>

      <div className="pulse-ai-deep-two-column">
        <ListBlock title="Deterministic calculations" values={plan.deterministicCalculations} empty="No calculation is required for this question." />
        <ListBlock title="Required answer sections" values={plan.answerSections} />
      </div>

      <section className="pulse-ai-deep-evidence-card">
        <h5>Comprehensive execution sequence</h5>
        <ol className="pulse-ai-deep-numbered-list">
          {asArray(plan.executionSteps).map((step, index) => <li key={`execution-${index}`}>{step}</li>)}
        </ol>
      </section>

      <div className="pulse-ai-deep-two-column">
        <ListBlock title="Privacy controls" values={plan.privacyControls} />
        <ListBlock title="Missing inputs before exact execution" values={plan.missingInputs} empty="No additional input was identified by the planner." />
      </div>

      <section className="pulse-ai-deep-evidence-card">
        <h5>Semantic read plan</h5>
        <KeyValueGrid values={{
          queryType: semanticQuery.queryType,
          metrics: asArray(semanticQuery.metrics).join(', ') || 'None selected',
          dimensions: asArray(semanticQuery.dimensions).join(', ') || 'None selected',
          maximumRows: semanticQuery.maximumRows,
          arbitrarySqlAllowed: semanticQuery.arbitrarySqlAllowed,
          deterministicValuesRequired: semanticQuery.deterministicValuesRequired,
          unknownValuesPreserved: semanticQuery.unknownValuesPreserved,
          externalExecution: semanticQuery.externalExecution
        }} />
      </section>

      {tools.length ? (
        <section className="pulse-ai-deep-evidence-card">
          <h5>Selected tool contracts</h5>
          <div className="pulse-ai-deep-tool-grid compact">
            {tools.map((tool) => (
              <article key={tool.code}>
                <div><strong>{tool.displayName}</strong><span>{title(tool.availability)}</span></div>
                <p>{tool.evidencePolicy}</p>
                <small>{asArray(tool.routes).join(' · ')}</small>
              </article>
            ))}
          </div>
        </section>
      ) : null}

      {mode === 'insight' ? (
        <section className="pulse-ai-deep-dependency-note">
          <strong>Group 3 financial truth dependency</strong>
          <p>{payload?.financialTruthDependency?.rule}</p>
          <small>Runtime consumption: {title(payload?.financialTruthDependency?.runtimeConsumption)}</small>
        </section>
      ) : null}

      <FullEvidence payload={payload} />
    </div>
  );
}

function PrivacyResult({ payload }) {
  const result = payload?.result;
  if (!result) return null;
  return (
    <div className="pulse-ai-deep-result-stack">
      <section className="pulse-ai-deep-result-hero">
        <div>
          <p className="pulse-ai-deep-eyebrow">Sanitized reasoning capsule preview</p>
          <h4>{title(result.status)}</h4>
          <p>This preview performs local deterministic redaction only. It never executes an external provider request.</p>
        </div>
        <span className="pulse-ai-deep-status is-locked">External execution blocked</span>
      </section>

      <KeyValueGrid values={{
        purpose: result.purpose,
        classification: result.classification,
        originalLength: result.originalLength,
        sanitizedLength: result.sanitizedLength,
        externalExecutionAuthorized: result.externalExecutionAuthorized,
        generatedAt: formatDate(result.generatedAt)
      }} />

      <section className="pulse-ai-deep-capsule">
        <h5>Sanitized capsule</h5>
        <pre>{result.sanitizedCapsule || 'No useful context remains after redaction.'}</pre>
      </section>

      <div className="pulse-ai-deep-two-column">
        <ListBlock title="Removed categories" values={result.removedCategories} />
        <ListBlock title="Blocked reasons" values={result.blockedReasons} />
      </div>

      <section className="pulse-ai-deep-evidence-card">
        <h5>Redaction evidence</h5>
        <div className="pulse-ai-deep-redaction-grid">
          {asArray(result.redactions).map((row, index) => (
            <article key={`${row.category}-${index}`}>
              <strong>{title(row.category)}</strong>
              <span>{row.count} match{row.count === 1 ? '' : 'es'}</span>
              <small>{row.replacement}</small>
            </article>
          ))}
        </div>
      </section>
      <FullEvidence payload={payload} />
    </div>
  );
}

function ToolRegistry({ payload }) {
  const tools = asArray(payload?.tools);
  if (!payload) return null;
  return (
    <div className="pulse-ai-deep-result-stack">
      <section className="pulse-ai-deep-result-hero">
        <div>
          <p className="pulse-ai-deep-eyebrow">Governed semantic tool gateway</p>
          <h4>{tools.length} registered read-tool contract{tools.length === 1 ? '' : 's'}</h4>
          <p>Every tool remains subject to its owning module, role, project, customer, environment, and record-level authorization.</p>
        </div>
        <span className="pulse-ai-deep-status is-ready">Read-only registry</span>
      </section>
      <div className="pulse-ai-deep-tool-grid">
        {tools.map((tool) => (
          <article key={tool.code}>
            <div><strong>{tool.displayName}</strong><span>{title(tool.availability)}</span></div>
            <p>{tool.domain} · Modules {asArray(tool.owningModules).join(', ')}</p>
            <dl>
              <div><dt>Access</dt><dd>{tool.accessPolicy}</dd></div>
              <div><dt>Classification</dt><dd>{tool.dataClassification}</dd></div>
              <div><dt>Calculations</dt><dd>{tool.calculationPolicy}</dd></div>
              <div><dt>Mutation</dt><dd>{tool.mutationPolicy}</dd></div>
              <div><dt>Evidence</dt><dd>{tool.evidencePolicy}</dd></div>
            </dl>
            <small>{asArray(tool.routes).join(' · ')}</small>
          </article>
        ))}
      </div>
      <ListBlock title="Registry rules" values={payload.rules} />
      <FullEvidence payload={payload} />
    </div>
  );
}

export default function PulseAiDeepIntelligenceWorkbench() {
  const [active, setActive] = useState('readiness');
  const [overview, setOverview] = useState(null);
  const [readiness, setReadiness] = useState({ loading: true, payload: null, error: '' });
  const [tools, setTools] = useState({ loading: true, payload: null, error: '' });
  const [timesheet, setTimesheet] = useState({ ...INITIAL_TIMESHEET });
  const [timesheetResult, setTimesheetResult] = useState({ loading: false, payload: null, error: '' });
  const [helpQuestion, setHelpQuestion] = useState('How does Celar AI use an SOW and GSD to generate a timesheet suggestion?');
  const [helpResult, setHelpResult] = useState({ loading: false, payload: null, error: '' });
  const [flowhive, setFlowhive] = useState({ ...INITIAL_FLOWHIVE });
  const [flowhiveResult, setFlowhiveResult] = useState({ loading: false, payload: null, error: '' });
  const [insightQuestion, setInsightQuestion] = useState('Which projects are approaching budget and what evidence explains the variance this quarter?');
  const [insightResult, setInsightResult] = useState({ loading: false, payload: null, error: '' });
  const [privacy, setPrivacy] = useState({ ...INITIAL_PRIVACY });
  const [privacyResult, setPrivacyResult] = useState({ loading: false, payload: null, error: '' });

  const activeDefinition = useMemo(
    () => WORKSPACES.find((workspace) => workspace.id === active) ?? WORKSPACES[0],
    [active]
  );

  async function loadFoundation() {
    setReadiness({ loading: true, payload: null, error: '' });
    setTools({ loading: true, payload: null, error: '' });
    const [overviewResult, readinessResult, toolsResult] = await Promise.allSettled([
      getJson('/api/pulse-ai/v1/overview'),
      getJson('/api/pulse-ai/v1/private-runtime/readiness'),
      getJson('/api/pulse-ai/v1/tools')
    ]);
    if (overviewResult.status === 'fulfilled') setOverview(overviewResult.value);
    if (readinessResult.status === 'fulfilled') setReadiness({ loading: false, payload: readinessResult.value, error: '' });
    else setReadiness({ loading: false, payload: null, error: readinessResult.reason?.message || 'Private runtime readiness could not be loaded.' });
    if (toolsResult.status === 'fulfilled') setTools({ loading: false, payload: toolsResult.value, error: '' });
    else setTools({ loading: false, payload: null, error: toolsResult.reason?.message || 'Tool registry could not be loaded.' });
  }

  useEffect(() => {
    void loadFoundation();
  }, []);

  async function runTimesheet(event) {
    event.preventDefault();
    setTimesheetResult({ loading: true, payload: null, error: '' });
    try {
      const payload = await getJson(buildQuery('/api/pulse-ai/v1/timesheet/context-preview', timesheet));
      setTimesheetResult({ loading: false, payload, error: '' });
    } catch (error) {
      setTimesheetResult({ loading: false, payload: null, error: error instanceof Error ? error.message : 'Timesheet grounding failed.' });
    }
  }

  async function runHelp(event) {
    event.preventDefault();
    setHelpResult({ loading: true, payload: null, error: '' });
    try {
      const payload = await getJson(buildQuery('/api/pulse-ai/v1/help-search/plan', { question: helpQuestion }));
      setHelpResult({ loading: false, payload, error: '' });
    } catch (error) {
      setHelpResult({ loading: false, payload: null, error: error instanceof Error ? error.message : 'Help/Search planning failed.' });
    }
  }

  async function runFlowhive(event) {
    event.preventDefault();
    setFlowhiveResult({ loading: true, payload: null, error: '' });
    try {
      const payload = await getJson(buildQuery('/api/pulse-ai/v1/flowhive/context-preview', flowhive));
      setFlowhiveResult({ loading: false, payload, error: '' });
    } catch (error) {
      setFlowhiveResult({ loading: false, payload: null, error: error instanceof Error ? error.message : 'FlowHive grounding failed.' });
    }
  }

  async function runInsight(event) {
    event.preventDefault();
    setInsightResult({ loading: true, payload: null, error: '' });
    try {
      const payload = await getJson(buildQuery('/api/pulse-ai/v1/insights/plan', { question: insightQuestion }));
      setInsightResult({ loading: false, payload, error: '' });
    } catch (error) {
      setInsightResult({ loading: false, payload: null, error: error instanceof Error ? error.message : 'Insight planning failed.' });
    }
  }

  async function runPrivacy(event) {
    event.preventDefault();
    setPrivacyResult({ loading: true, payload: null, error: '' });
    try {
      const payload = await postJson('/api/pulse-ai/v1/external-escalation/sanitize-preview', {
        purpose: privacy.purpose,
        classification: privacy.classification,
        content: privacy.content,
        sensitiveTerms: privacy.sensitiveTerms.split(',').map((value) => value.trim()).filter(Boolean),
        acknowledgePreviewOnly: true
      });
      setPrivacyResult({ loading: false, payload, error: '' });
    } catch (error) {
      setPrivacyResult({ loading: false, payload: null, error: error instanceof Error ? error.message : 'Sanitization preview failed.' });
    }
  }

  return (
    <section className="pulse-ai-deep-workbench" aria-labelledby="pulse-ai-deep-workbench-title">
      <header className="pulse-ai-deep-header">
        <div>
          <p className="pulse-ai-deep-eyebrow">Module 011 deep intelligence workbench</p>
          <h2 id="pulse-ai-deep-workbench-title">Build comprehensive, source-grounded answers—not surface summaries</h2>
          <p>
            Inspect the private runtime, test document-grounding evidence, design detailed Help/Search and analytical answers,
            and verify the privacy capsule before any future external reasoning path is considered.
          </p>
        </div>
        <div className="pulse-ai-deep-header-actions">
          <button type="button" onClick={loadFoundation}>Refresh evidence</button>
          <span>{overview?.status ? title(overview.status) : 'Loading runtime contract'}</span>
        </div>
      </header>

      <div className="pulse-ai-deep-boundary" role="note">
        <strong>Runtime boundary</strong>
        <p>All workspaces are read-only previews. No document is uploaded, extracted, indexed, trained, externally transmitted, or used to change ProjectPulse state from this workbench.</p>
      </div>

      <div className="pulse-ai-deep-layout">
        <nav className="pulse-ai-deep-tabs" aria-label="Celar AI deep intelligence workspaces">
          {WORKSPACES.map((workspace) => (
            <button
              type="button"
              key={workspace.id}
              className={active === workspace.id ? 'is-active' : ''}
              onClick={() => setActive(workspace.id)}
            >
              <strong>{workspace.label}</strong>
              <span>{workspace.description}</span>
            </button>
          ))}
        </nav>

        <main className="pulse-ai-deep-panel">
          <div className="pulse-ai-deep-current-heading">
            <p className="pulse-ai-deep-eyebrow">Active workspace</p>
            <h3>{activeDefinition.label}</h3>
            <p>{activeDefinition.description}</p>
          </div>

          {active === 'readiness' ? (
            <>
              <LoadingState active={readiness.loading} />
              <ErrorState error={readiness.error} />
              <ReadinessResult payload={readiness.payload} />
            </>
          ) : null}

          {active === 'timesheet' ? (
            <div className="pulse-ai-deep-workspace-stack">
              <form className="pulse-ai-deep-form" onSubmit={runTimesheet}>
                <div className="pulse-ai-deep-form-heading">
                  <h4>Preview authorized SOW/GSD grounding</h4>
                  <p>Use the same project, task, row, and rough-note context Module 001 supplies. Raw document content is never returned.</p>
                </div>
                <div className="pulse-ai-deep-form-grid">
                  <label>Project code<input value={timesheet.projectCode} onChange={(event) => setTimesheet({ ...timesheet, projectCode: event.target.value })} placeholder="PRJ-1001" /></label>
                  <label>Project name<input value={timesheet.projectName} onChange={(event) => setTimesheet({ ...timesheet, projectName: event.target.value })} placeholder="Project name" /></label>
                  <label>Task code<input value={timesheet.taskCode} onChange={(event) => setTimesheet({ ...timesheet, taskCode: event.target.value })} placeholder="ENG-001" /></label>
                  <label>Task name<input value={timesheet.taskName} onChange={(event) => setTimesheet({ ...timesheet, taskName: event.target.value })} placeholder="Assigned task" /></label>
                  <label>Row or request label<input value={timesheet.rowLabel} onChange={(event) => setTimesheet({ ...timesheet, rowLabel: event.target.value })} placeholder="SR-100 or activity label" /></label>
                  <label>Work date<input type="date" value={timesheet.workDate} onChange={(event) => setTimesheet({ ...timesheet, workDate: event.target.value })} /></label>
                  <label>Time type<select value={timesheet.timeType} onChange={(event) => setTimesheet({ ...timesheet, timeType: event.target.value })}><option value="normal">Normal</option><option value="afterhours">Afterhours</option></select></label>
                  <label>Row type<select value={timesheet.rowType} onChange={(event) => setTimesheet({ ...timesheet, rowType: event.target.value })}><option value="project">Regular Task</option><option value="request">Request / Service Request</option><option value="nonProject">Non-project</option></select></label>
                </div>
                <label>Engineer rough note<textarea rows={5} value={timesheet.currentDescription} onChange={(event) => setTimesheet({ ...timesheet, currentDescription: event.target.value })} placeholder="Describe the work actually performed. This remains the primary source of truth." /></label>
                <button type="submit" disabled={timesheetResult.loading}>{timesheetResult.loading ? 'Resolving private evidence…' : 'Analyze grounding evidence'}</button>
              </form>
              <ErrorState error={timesheetResult.error} />
              <GroundingResult payload={timesheetResult.payload} mode="timesheet" />
            </div>
          ) : null}

          {active === 'help' ? (
            <div className="pulse-ai-deep-workspace-stack">
              <form className="pulse-ai-deep-form" onSubmit={runHelp}>
                <div className="pulse-ai-deep-form-heading"><h4>Ask a detailed ProjectPulse question</h4><p>The planner classifies every relevant domain and returns the evidence, filters, tools, calculations, answer sections, and privacy controls needed for a comprehensive answer.</p></div>
                <label>Question<textarea rows={5} value={helpQuestion} onChange={(event) => setHelpQuestion(event.target.value)} /></label>
                <button type="submit" disabled={helpResult.loading}>{helpResult.loading ? 'Building comprehensive plan…' : 'Build detailed answer plan'}</button>
              </form>
              <ErrorState error={helpResult.error} />
              <PlanResult payload={helpResult.payload} mode="help" />
            </div>
          ) : null}

          {active === 'flowhive' ? (
            <div className="pulse-ai-deep-workspace-stack">
              <form className="pulse-ai-deep-form" onSubmit={runFlowhive}>
                <div className="pulse-ai-deep-form-heading"><h4>Preview the private FlowHive source package</h4><p>Resolve authorized project documents and show the detailed planning contract before any private-model execution is implemented.</p></div>
                <div className="pulse-ai-deep-form-grid">
                  <label>Project code<input value={flowhive.projectCode} onChange={(event) => setFlowhive({ ...flowhive, projectCode: event.target.value })} placeholder="PRJ-1001" /></label>
                  <label>Project name<input value={flowhive.projectName} onChange={(event) => setFlowhive({ ...flowhive, projectName: event.target.value })} placeholder="Project name" /></label>
                </div>
                <label>Requested planning outcome<textarea rows={5} value={flowhive.requestedOutcome} onChange={(event) => setFlowhive({ ...flowhive, requestedOutcome: event.target.value })} placeholder="Create a detailed implementation plan with milestones, dependencies, resource roles, risks, and an engineer-review checklist." /></label>
                <button type="submit" disabled={flowhiveResult.loading}>{flowhiveResult.loading ? 'Resolving planning evidence…' : 'Analyze FlowHive context'}</button>
              </form>
              <ErrorState error={flowhiveResult.error} />
              <GroundingResult payload={flowhiveResult.payload} mode="flowhive" />
            </div>
          ) : null}

          {active === 'insight' ? (
            <div className="pulse-ai-deep-workspace-stack">
              <form className="pulse-ai-deep-form" onSubmit={runInsight}>
                <div className="pulse-ai-deep-form-heading"><h4>Design a governed analytical answer</h4><p>Exact financial, reporting, utilization, and project values must come from deterministic source contracts. The model explains; it does not invent calculations.</p></div>
                <label>Reporting or financial question<textarea rows={5} value={insightQuestion} onChange={(event) => setInsightQuestion(event.target.value)} /></label>
                <button type="submit" disabled={insightResult.loading}>{insightResult.loading ? 'Building semantic plan…' : 'Build detailed insight plan'}</button>
              </form>
              <ErrorState error={insightResult.error} />
              <PlanResult payload={insightResult.payload} mode="insight" />
            </div>
          ) : null}

          {active === 'privacy' ? (
            <div className="pulse-ai-deep-workspace-stack">
              <form className="pulse-ai-deep-form" onSubmit={runPrivacy}>
                <div className="pulse-ai-deep-form-heading"><h4>Preview a sanitized reasoning capsule</h4><p>This tool shows what deterministic redaction removes. It cannot authorize or execute Claude, OpenAI, or another external provider.</p></div>
                <div className="pulse-ai-deep-form-grid">
                  <label>Purpose<input value={privacy.purpose} onChange={(event) => setPrivacy({ ...privacy, purpose: event.target.value })} /></label>
                  <label>Classification<select value={privacy.classification} onChange={(event) => setPrivacy({ ...privacy, classification: event.target.value })}><option value="restricted">Restricted</option><option value="confidential">Confidential</option><option value="internal">Internal</option></select></label>
                </div>
                <label>Explicit sensitive terms<input value={privacy.sensitiveTerms} onChange={(event) => setPrivacy({ ...privacy, sensitiveTerms: event.target.value })} placeholder="Comma-separated project, customer, or internal terms" /></label>
                <label>Internal problem statement<textarea rows={8} value={privacy.content} onChange={(event) => setPrivacy({ ...privacy, content: event.target.value })} placeholder="Paste a test statement. Do not paste usable credentials." /></label>
                <button type="submit" disabled={privacyResult.loading}>{privacyResult.loading ? 'Applying local redaction…' : 'Create preview capsule'}</button>
              </form>
              <ErrorState error={privacyResult.error} />
              <PrivacyResult payload={privacyResult.payload} />
            </div>
          ) : null}

          {active === 'tools' ? (
            <>
              <LoadingState active={tools.loading} />
              <ErrorState error={tools.error} />
              <ToolRegistry payload={tools.payload} />
            </>
          ) : null}
        </main>
      </div>
    </section>
  );
}
