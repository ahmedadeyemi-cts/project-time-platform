import { useEffect, useMemo, useState } from 'react';
import './celar-ai-ask-operations.css';

const DEFECT_STEPS = Object.freeze([
  ['location', 'Where is the problem?'],
  ['behavior', 'What happened?'],
  ['reproduction', 'Can it be reproduced?'],
  ['impact', 'What is the impact?'],
  ['evidence', 'Supporting evidence'],
  ['review', 'Review and create']
]);

const CATEGORIES = Object.freeze([
  'Bug', 'Regression', 'User Interface', 'API', 'Authentication',
  'Authorization', 'Data', 'Integration', 'Performance', 'Documentation',
  'Feature Gap', 'Availability', 'Security', 'Other'
]);
const PRIORITIES = Object.freeze(['Critical', 'High', 'Medium', 'Low']);
const SYNTHETIC_SCENARIOS = Object.freeze([
  'private_inference_timeout',
  'embedding_dimension_mismatch',
  'ocr_unavailable',
  'malware_scanner_unavailable',
  'all_ai_targets_unavailable',
  'module064_router_unavailable',
  'github_401',
  'github_403',
  'github_429',
  'github_500',
  'github_timeout',
  'github_actions_unavailable',
  'pulse_database_timeout',
  'module067_delivery_unavailable',
  'high_ai_latency',
  'recovery_flapping'
]);

const EMPTY_DRAFT = Object.freeze({
  title: '',
  description: '',
  category: 'Bug',
  priority: 'Medium',
  environment: 'test',
  affectedSystem: 'Pulse',
  affectedModule: '',
  affectedRoute: '',
  expectedBehavior: '',
  actualBehavior: '',
  reproductionStepsText: '',
  businessImpact: '',
  workaround: '',
  correlationId: '',
  releaseSha: ''
});

async function readJson(response) {
  const payload = await response.json().catch(() => ({}));
  if (!response.ok) {
    const error = new Error(payload.message || payload.status || `Request returned HTTP ${response.status}.`);
    error.status = response.status;
    error.payload = payload;
    throw error;
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

async function sendJson(path, method, body) {
  return readJson(await fetch(path, {
    method,
    cache: 'no-store',
    headers: {
      Accept: 'application/json',
      'Content-Type': 'application/json'
    },
    body: JSON.stringify(body ?? {})
  }));
}

function titleFrom(value) {
  return String(value ?? '')
    .replaceAll('_', ' ')
    .replaceAll('-', ' ')
    .replace(/\b\w/g, (letter) => letter.toUpperCase());
}

function formatDate(value) {
  if (!value) return 'Not recorded';
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? String(value) : parsed.toLocaleString();
}

function draftFromSession(session) {
  const draft = session?.draft ?? {};
  return {
    ...EMPTY_DRAFT,
    ...draft,
    reproductionStepsText: Array.isArray(draft.reproductionSteps)
      ? draft.reproductionSteps.join('\n')
      : ''
  };
}

function evidenceFromOutcome(outcome) {
  return Array.isArray(outcome?.evidence) ? outcome.evidence : [];
}

function ProbeCard({ probe }) {
  return (
    <article className={`celar-ops-probe is-${probe.status || 'unknown'}`}>
      <header><strong>{probe.displayName}</strong><span>{titleFrom(probe.status)}</span></header>
      <p>{probe.detail || 'No additional detail was returned.'}</p>
      <dl>
        <div><dt>Failure code</dt><dd>{probe.failureCode || 'None'}</dd></div>
        <div><dt>HTTP</dt><dd>{probe.httpStatus ?? '—'}</dd></div>
        <div><dt>Latency</dt><dd>{probe.latencyMs == null ? '—' : `${probe.latencyMs} ms`}</dd></div>
        <div><dt>Observed</dt><dd>{formatDate(probe.observedAt)}</dd></div>
      </dl>
    </article>
  );
}

function DefectCard({ defect }) {
  return (
    <article className="celar-ops-defect-card">
      <header><strong>{defect.defectNumber}</strong><span>{defect.status}</span></header>
      <h4>{defect.title}</h4>
      <p>{defect.priority} · {defect.category} · {defect.environment}</p>
      <small>Assigned to {defect.assignee?.displayName || 'Ahmed Adeyemi'} · {formatDate(defect.dateAdded)}</small>
      <button type="button" onClick={() => { window.location.hash = `defect-tracker?defect=${encodeURIComponent(defect.defectNumber)}`; }}>
        Open Module 076 record
      </button>
    </article>
  );
}

export default function CelarAiAskOperations() {
  const [open, setOpen] = useState(false);
  const [view, setView] = useState('diagnostics');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [question, setQuestion] = useState('Troubleshoot the current platform and show me the strongest evidence.');
  const [context, setContext] = useState({});
  const [diagnostics, setDiagnostics] = useState(null);
  const [matches, setMatches] = useState([]);
  const [session, setSession] = useState(null);
  const [draft, setDraft] = useState(EMPTY_DRAFT);
  const [stepIndex, setStepIndex] = useState(0);
  const [createdDefect, setCreatedDefect] = useState(null);
  const [readiness, setReadiness] = useState(null);
  const [policies, setPolicies] = useState([]);
  const [policyCanManage, setPolicyCanManage] = useState(false);
  const [policyReason, setPolicyReason] = useState('Protected Test UAT of automatic defect thresholds');
  const [syntheticScenario, setSyntheticScenario] = useState(SYNTHETIC_SCENARIOS[0]);
  const [syntheticResult, setSyntheticResult] = useState(null);

  const currentStep = DEFECT_STEPS[stepIndex]?.[0] || 'review';
  const evidence = evidenceFromOutcome(diagnostics);
  const failedEvidence = useMemo(
    () => evidence.filter((item) => item.status === 'failed' || item.status === 'degraded'),
    [evidence]
  );

  useEffect(() => {
    const openDefect = (event) => {
      const detail = event?.detail ?? {};
      setContext(detail);
      setOpen(true);
      setView('defect');
      setCreatedDefect(null);
      setError('');
      void startDefect(detail);
    };
    const openDiagnostics = (event) => {
      const detail = event?.detail ?? {};
      setContext(detail);
      setQuestion(detail.question || 'Troubleshoot the current platform and show me the strongest evidence.');
      setOpen(true);
      setView('diagnostics');
      setError('');
      if (detail.autoRun !== false) void runDiagnostics(detail.question);
    };
    const openHealth = () => {
      setOpen(true);
      setView('health');
      setError('');
      void loadHealth();
    };
    window.addEventListener('projectpulse:celar-ai-open-defect-intake', openDefect);
    window.addEventListener('projectpulse:celar-ai-open-operations', openDiagnostics);
    window.addEventListener('projectpulse:celar-ai-open-health-automation', openHealth);
    return () => {
      window.removeEventListener('projectpulse:celar-ai-open-defect-intake', openDefect);
      window.removeEventListener('projectpulse:celar-ai-open-operations', openDiagnostics);
      window.removeEventListener('projectpulse:celar-ai-open-health-automation', openHealth);
    };
  }, []);

  async function runDiagnostics(explicitQuestion = '') {
    const clean = String(explicitQuestion || question).trim();
    if (!clean) return;
    setBusy(true);
    setError('');
    setMatches([]);
    try {
      const payload = await sendJson('/api/celar-ai/v1/operations/troubleshoot', 'POST', {
        question: clean,
        environment: context.environment || draft.environment || 'test',
        affectedSystem: context.affectedSystem || draft.affectedSystem || 'Pulse',
        affectedModule: context.affectedModule || draft.affectedModule || '',
        affectedRoute: context.affectedRoute || draft.affectedRoute || '',
        correlationId: context.correlationId || draft.correlationId || '',
        projectCode: context.projectCode || null,
        projectName: context.projectName || null,
        includeAiRuntime: true,
        includeDatabase: true,
        includeModule064: true,
        includeGitHub: /github/i.test(clean),
        includeNotifications: /mail|notification|module 067/i.test(clean)
      });
      setDiagnostics(payload.outcome);
      if (payload.outcome?.existingDefectSearchRecommended) {
        await searchMatches(payload.outcome);
      }
    } catch (requestError) {
      setError(requestError.message || 'The governed diagnostic did not complete.');
    } finally {
      setBusy(false);
    }
  }

  async function searchMatches(outcome = diagnostics) {
    const firstFailure = evidenceFromOutcome(outcome)
      .find((item) => item.status === 'failed' || item.status === 'degraded');
    const url = new URL('/api/celar-ai/v1/operations/defects/matches', window.location.origin);
    url.searchParams.set('environment', context.environment || draft.environment || 'test');
    if (context.affectedModule || draft.affectedModule) url.searchParams.set('affectedModule', context.affectedModule || draft.affectedModule);
    if (firstFailure?.componentCode) url.searchParams.set('componentCode', firstFailure.componentCode);
    if (firstFailure?.failureCode) url.searchParams.set('failureCode', firstFailure.failureCode);
    const payload = await getJson(`${url.pathname}${url.search}`);
    setMatches(Array.isArray(payload.defects) ? payload.defects : []);
  }

  async function startDefect(detail = {}) {
    setBusy(true);
    setError('');
    setSession(null);
    setStepIndex(0);
    try {
      const sourceDiagnostics = Array.isArray(detail.diagnosticEvidence)
        ? detail.diagnosticEvidence
        : evidenceFromOutcome(detail.outcome || diagnostics);
      const payload = await sendJson('/api/celar-ai/v1/operations/defects/intake-sessions', 'POST', {
        conversationId: detail.conversationId || null,
        triggerQuestion: detail.triggerQuestion || detail.question || '',
        environment: detail.environment || 'test',
        affectedSystem: detail.affectedSystem || 'Pulse',
        affectedModule: detail.affectedModule || '',
        affectedRoute: detail.affectedRoute || '',
        correlationId: detail.correlationId || diagnostics?.correlationId || '',
        releaseSha: detail.releaseSha || '',
        suggestedTitle: detail.suggestedTitle || diagnostics?.directConclusion || '',
        suggestedDescription: detail.suggestedDescription || failedEvidence.map((item) => `${item.displayName}: ${item.detail}`).join('\n'),
        suggestedCategory: detail.suggestedCategory || (sourceDiagnostics.length ? 'Availability' : 'Bug'),
        suggestedPriority: detail.suggestedPriority || (sourceDiagnostics.some((item) => item.status === 'failed') ? 'High' : 'Medium'),
        diagnosticEvidence: sourceDiagnostics
      });
      setSession(payload.session);
      setDraft(draftFromSession(payload.session));
      setView('defect');
    } catch (requestError) {
      setError(requestError.message || 'The guided defect questionnaire could not start.');
    } finally {
      setBusy(false);
    }
  }

  function updateDraft(field, value) {
    setDraft((current) => ({ ...current, [field]: value }));
  }

  function reproductionSteps() {
    return draft.reproductionStepsText
      .split(/\r?\n/)
      .map((value) => value.trim())
      .filter(Boolean);
  }

  async function saveStep(nextIndex = stepIndex) {
    if (!session) return null;
    const payload = await sendJson(
      `/api/celar-ai/v1/operations/defects/intake-sessions/${encodeURIComponent(session.intakeSessionId)}`,
      'PATCH',
      {
        expectedRevision: session.revisionNumber,
        currentStep: DEFECT_STEPS[nextIndex]?.[0] || 'review',
        title: draft.title,
        description: draft.description,
        category: draft.category,
        priority: draft.priority,
        environment: draft.environment,
        affectedSystem: draft.affectedSystem,
        affectedModule: draft.affectedModule,
        affectedRoute: draft.affectedRoute,
        expectedBehavior: draft.expectedBehavior,
        actualBehavior: draft.actualBehavior,
        reproductionSteps: reproductionSteps(),
        businessImpact: draft.businessImpact,
        workaround: draft.workaround,
        correlationId: draft.correlationId,
        releaseSha: draft.releaseSha,
        readyForReview: nextIndex === DEFECT_STEPS.length - 1
      }
    );
    setSession(payload.session);
    setDraft(draftFromSession(payload.session));
    return payload.session;
  }

  async function moveStep(delta) {
    const next = Math.max(0, Math.min(DEFECT_STEPS.length - 1, stepIndex + delta));
    setBusy(true);
    setError('');
    try {
      await saveStep(next);
      setStepIndex(next);
    } catch (requestError) {
      setError(requestError.message || 'The questionnaire could not be saved.');
    } finally {
      setBusy(false);
    }
  }

  async function submitDefect() {
    if (!session) return;
    setBusy(true);
    setError('');
    try {
      const reviewed = await saveStep(DEFECT_STEPS.length - 1);
      const payload = await sendJson(
        `/api/celar-ai/v1/operations/defects/intake-sessions/${encodeURIComponent(session.intakeSessionId)}/submit`,
        'POST',
        {
          expectedRevision: reviewed.revisionNumber,
          userConfirmed: true,
          confirmationText: 'CREATE DEFECT'
        }
      );
      setCreatedDefect(payload.defect);
      setSession(null);
    } catch (requestError) {
      setError(requestError.message || 'The defect could not be created.');
    } finally {
      setBusy(false);
    }
  }

  async function loadHealth() {
    setBusy(true);
    setError('');
    try {
      const [readinessPayload, policyPayload] = await Promise.all([
        getJson('/api/celar-ai/v1/operations/readiness'),
        getJson('/api/celar-ai/v1/operations/monitor-policies')
      ]);
      setReadiness(readinessPayload);
      setPolicies(Array.isArray(policyPayload.policies) ? policyPayload.policies : []);
      setPolicyCanManage(Boolean(policyPayload.canManage));
    } catch (requestError) {
      setError(requestError.message || 'Operational readiness could not be loaded.');
    } finally {
      setBusy(false);
    }
  }

  async function togglePolicy(policy, enabled) {
    setBusy(true);
    setError('');
    try {
      const payload = await sendJson(
        `/api/celar-ai/v1/operations/monitor-policies/${encodeURIComponent(policy.policyCode)}/automatic-defects`,
        'POST',
        {
          expectedRevision: policy.revisionNumber,
          enabled,
          confirmation: enabled ? 'ENABLE TEST AUTOMATIC DEFECTS' : 'DISABLE TEST AUTOMATIC DEFECTS',
          reason: policyReason
        }
      );
      setPolicies((current) => current.map((item) =>
        item.policyCode === policy.policyCode ? payload.policy : item));
    } catch (requestError) {
      setError(requestError.message || 'The monitor policy could not be changed.');
    } finally {
      setBusy(false);
    }
  }

  async function runSyntheticFailure() {
    setBusy(true);
    setError('');
    try {
      const payload = await sendJson('/api/celar-ai/v1/operations/synthetic-failures', 'POST', {
        scenario: syntheticScenario,
        occurrences: 3,
        confirmation: 'RUN TEST SYNTHETIC FAILURE'
      });
      setSyntheticResult(payload.result);
      await loadHealth();
    } catch (requestError) {
      setError(requestError.message || 'The Test-only synthetic failure could not run.');
    } finally {
      setBusy(false);
    }
  }

  function close() {
    setOpen(false);
    setError('');
  }

  if (!open) return null;

  return (
    <section className="celar-ask-operations" role="dialog" aria-modal="true" aria-label="Ask Celar AI operations">
      <div className="celar-ops-backdrop" onClick={close} />
      <div className="celar-ops-shell">
        <header className="celar-ops-header">
          <div>
            <span>Ask Celar AI · Governed operations</span>
            <h2>Troubleshoot, verify, and create a Module 076 defect</h2>
            <p>Every user action starts here. Module 076 remains the durable system of record.</p>
          </div>
          <button type="button" onClick={close} aria-label="Close Ask Celar AI operations">×</button>
        </header>

        <nav className="celar-ops-tabs" aria-label="Ask Celar AI operations views">
          <button type="button" className={view === 'diagnostics' ? 'is-active' : ''} onClick={() => setView('diagnostics')}>Troubleshoot</button>
          <button type="button" className={view === 'defect' ? 'is-active' : ''} onClick={() => { setView('defect'); if (!session && !createdDefect) void startDefect(context); }}>Defect questionnaire</button>
          <button type="button" className={view === 'health' ? 'is-active' : ''} onClick={() => { setView('health'); void loadHealth(); }}>Health & automation</button>
        </nav>

        {error ? <p className="celar-ops-banner is-error" role="alert">{error}</p> : null}
        {busy ? <p className="celar-ops-banner">Working inside the governed Pulse boundary…</p> : null}

        {view === 'diagnostics' ? (
          <div className="celar-ops-content">
            <section className="celar-ops-card">
              <div className="celar-ops-card-heading"><div><span>Read-only diagnosis</span><h3>What should Celar AI troubleshoot?</h3></div></div>
              <textarea value={question} rows={4} onChange={(event) => setQuestion(event.target.value)} placeholder="Describe what is not working, the affected module, route, environment, and when it started." />
              <div className="celar-ops-actions">
                <button type="button" className="is-primary" disabled={busy || !question.trim()} onClick={() => runDiagnostics()}>Run governed diagnostics</button>
                <button type="button" disabled={busy} onClick={() => startDefect({ ...context, question, outcome: diagnostics })}>Open questionnaire</button>
              </div>
            </section>

            {diagnostics ? (
              <>
                <section className={`celar-ops-conclusion is-${diagnostics.status}`}>
                  <span>{titleFrom(diagnostics.status)}</span>
                  <h3>{diagnostics.directConclusion}</h3>
                  <p>Confidence {Math.round(Number(diagnostics.confidence || 0) * 100)}% · Correlation <code>{diagnostics.correlationId}</code> · {formatDate(diagnostics.dataAsOf)}</p>
                </section>
                <div className="celar-ops-probe-grid">
                  {evidence.map((probe) => <ProbeCard key={`${probe.probeCode}-${probe.observedAt}`} probe={probe} />)}
                </div>
                <section className="celar-ops-card">
                  <h3>Next governed action</h3>
                  <div className="celar-ops-actions">
                    <button type="button" className="is-primary" onClick={() => startDefect({ ...context, question, outcome: diagnostics, correlationId: diagnostics.correlationId, diagnosticEvidence: evidence })}>Open new defect</button>
                    <button type="button" onClick={() => searchMatches()}>Search existing defects</button>
                    <button type="button" onClick={() => setDiagnostics(null)}>Continue troubleshooting</button>
                    <button type="button" onClick={close}>Dismiss</button>
                  </div>
                </section>
                {matches.length ? <div className="celar-ops-defect-grid">{matches.map((defect) => <DefectCard key={defect.defectId} defect={defect} />)}</div> : null}
              </>
            ) : null}
          </div>
        ) : null}

        {view === 'defect' ? (
          <div className="celar-ops-content">
            {createdDefect ? (
              <section className="celar-ops-created">
                <span>Module 076 defect created</span>
                <h3>{createdDefect.defectNumber}</h3>
                <p>{createdDefect.title}</p>
                <dl>
                  <div><dt>Status</dt><dd>{createdDefect.status}</dd></div>
                  <div><dt>Priority</dt><dd>{createdDefect.priority}</dd></div>
                  <div><dt>Assignee</dt><dd>{createdDefect.assignee?.displayName} · {createdDefect.assignee?.email}</dd></div>
                  <div><dt>Reporter</dt><dd>{createdDefect.reporter?.displayName}</dd></div>
                </dl>
                <div className="celar-ops-actions"><button type="button" className="is-primary" onClick={() => { window.location.hash = `defect-tracker?defect=${encodeURIComponent(createdDefect.defectNumber)}`; close(); }}>Open in Module 076</button><button type="button" onClick={close}>Done</button></div>
              </section>
            ) : session ? (
              <>
                <ol className="celar-ops-stepper">
                  {DEFECT_STEPS.map(([code, label], index) => (
                    <li key={code} className={index === stepIndex ? 'is-active' : index < stepIndex ? 'is-complete' : ''}><span>{index + 1}</span><small>{label}</small></li>
                  ))}
                </ol>
                <form className="celar-ops-questionnaire" onSubmit={(event) => event.preventDefault()}>
                  {currentStep === 'location' ? (
                    <section>
                      <h3>Where is the problem?</h3>
                      <div className="celar-ops-form-grid">
                        <label>Environment<input value={draft.environment} onChange={(event) => updateDraft('environment', event.target.value)} /></label>
                        <label>Affected system<input value={draft.affectedSystem} onChange={(event) => updateDraft('affectedSystem', event.target.value)} /></label>
                        <label>Module<input value={draft.affectedModule} onChange={(event) => updateDraft('affectedModule', event.target.value)} placeholder="Example: 076" /></label>
                        <label>Route<input value={draft.affectedRoute} onChange={(event) => updateDraft('affectedRoute', event.target.value)} placeholder="Example: /api/defect-tracker/report" /></label>
                        <label>Correlation ID<input value={draft.correlationId} onChange={(event) => updateDraft('correlationId', event.target.value)} /></label>
                        <label>Release SHA<input value={draft.releaseSha} onChange={(event) => updateDraft('releaseSha', event.target.value)} /></label>
                      </div>
                    </section>
                  ) : null}
                  {currentStep === 'behavior' ? (
                    <section>
                      <h3>What happened?</h3>
                      <label>Summary<input maxLength={180} value={draft.title} onChange={(event) => updateDraft('title', event.target.value)} /></label>
                      <label>Description<textarea rows={5} maxLength={8000} value={draft.description} onChange={(event) => updateDraft('description', event.target.value)} /></label>
                      <label>Expected behavior<textarea rows={3} value={draft.expectedBehavior} onChange={(event) => updateDraft('expectedBehavior', event.target.value)} /></label>
                      <label>Actual behavior<textarea rows={3} value={draft.actualBehavior} onChange={(event) => updateDraft('actualBehavior', event.target.value)} /></label>
                    </section>
                  ) : null}
                  {currentStep === 'reproduction' ? (
                    <section>
                      <h3>Can it be reproduced?</h3>
                      <label>One reproduction step per line<textarea rows={9} value={draft.reproductionStepsText} onChange={(event) => updateDraft('reproductionStepsText', event.target.value)} placeholder={'1. Open Module 076\n2. Select Create defect\n3. Observe the error'} /></label>
                      <p className="celar-ops-help">Include frequency, first observed time, whether it worked previously, and the release after which it started. Do not include secrets.</p>
                    </section>
                  ) : null}
                  {currentStep === 'impact' ? (
                    <section>
                      <h3>What is the impact?</h3>
                      <div className="celar-ops-form-grid">
                        <label>Category<select value={draft.category} onChange={(event) => updateDraft('category', event.target.value)}>{CATEGORIES.map((item) => <option key={item}>{item}</option>)}</select></label>
                        <label>Priority<select value={draft.priority} onChange={(event) => updateDraft('priority', event.target.value)}>{PRIORITIES.map((item) => <option key={item}>{item}</option>)}</select></label>
                      </div>
                      <label>Business or user impact<textarea rows={5} value={draft.businessImpact} onChange={(event) => updateDraft('businessImpact', event.target.value)} /></label>
                      <label>Known workaround<textarea rows={3} value={draft.workaround} onChange={(event) => updateDraft('workaround', event.target.value)} /></label>
                    </section>
                  ) : null}
                  {currentStep === 'evidence' ? (
                    <section>
                      <h3>Supporting evidence</h3>
                      <p>Celar AI will include only the sanitized probe summaries shown below. Tokens, cookies, connection strings, raw prompts, raw private documents, and embedding vectors are excluded.</p>
                      <div className="celar-ops-probe-grid">{(session.diagnosticEvidence || []).map((probe) => <ProbeCard key={`${probe.probeCode}-${probe.observedAt}`} probe={probe} />)}</div>
                      {!session.diagnosticEvidence?.length ? <p className="celar-ops-empty">No diagnostic evidence is attached. You can still create a user-reported defect.</p> : null}
                    </section>
                  ) : null}
                  {currentStep === 'review' ? (
                    <section>
                      <h3>Review and create</h3>
                      <div className="celar-ops-review-grid">
                        <div><span>Summary</span><strong>{draft.title || 'Required'}</strong></div>
                        <div><span>Environment</span><strong>{draft.environment}</strong></div>
                        <div><span>Module / route</span><strong>{[draft.affectedModule, draft.affectedRoute].filter(Boolean).join(' · ') || 'Not specified'}</strong></div>
                        <div><span>Category / priority</span><strong>{draft.category} · {draft.priority}</strong></div>
                        <div><span>Default assignee</span><strong>Ahmed Adeyemi · ahmed.adeyemi@ussignal.com</strong></div>
                        <div><span>System of record</span><strong>Module 076</strong></div>
                      </div>
                      <p className="celar-ops-confirmation">Selecting Create defect is your confirmation. Celar AI prepares the record; the authenticated user remains the requesting authority.</p>
                    </section>
                  ) : null}
                  <div className="celar-ops-actions">
                    <button type="button" disabled={busy || stepIndex === 0} onClick={() => moveStep(-1)}>Back</button>
                    {stepIndex < DEFECT_STEPS.length - 1 ? <button type="button" className="is-primary" disabled={busy} onClick={() => moveStep(1)}>Save and continue</button> : <button type="button" className="is-primary" disabled={busy} onClick={submitDefect}>Create defect in Module 076</button>}
                    <button type="button" disabled={busy} onClick={close}>Cancel</button>
                  </div>
                </form>
              </>
            ) : <p className="celar-ops-empty">Preparing the guided questionnaire…</p>}
          </div>
        ) : null}

        {view === 'health' ? (
          <div className="celar-ops-content">
            <section className="celar-ops-health-summary">
              <div><span>Operations contract</span><strong>{readiness?.readiness?.status || 'Loading'}</strong></div>
              <div><span>Migration</span><strong>{readiness?.readiness?.migrationReady ? '084 ready' : '084 required'}</strong></div>
              <div><span>Automatic monitoring</span><strong>{readiness?.readiness?.automaticMonitoringEnabled ? 'Enabled in Test' : 'Disabled'}</strong></div>
              <div><span>Default assignee</span><strong>{readiness?.readiness?.defaultAssignee?.email || 'ahmed.adeyemi@ussignal.com'}</strong></div>
            </section>
            <section className="celar-ops-card">
              <h3>Versioned availability policies</h3>
              <label>Activation reason<input value={policyReason} onChange={(event) => setPolicyReason(event.target.value)} /></label>
              <div className="celar-ops-policy-list">
                {policies.map((policy) => (
                  <article key={policy.policyCode}>
                    <div><strong>{policy.displayName}</strong><span>{policy.machineCreationEnabled ? 'Automatic defects on' : 'Observe only'}</span></div>
                    <p>{policy.consecutiveFailureThreshold} failures / {Math.round(policy.evaluationWindowSeconds / 60)} min · recovery {policy.consecutiveSuccessThreshold} successes + {Math.round(policy.recoveryStabilitySeconds / 60)} min stable · {policy.initialPriority}</p>
                    {policyCanManage ? <button type="button" disabled={busy} onClick={() => togglePolicy(policy, !policy.machineCreationEnabled)}>{policy.machineCreationEnabled ? 'Disable automatic defects' : 'Enable in protected Test'}</button> : null}
                  </article>
                ))}
              </div>
            </section>
            <section className="celar-ops-card">
              <h3>Protected-Test fault injection</h3>
              <p>These scenarios record synthetic probe evidence only. They do not turn off GitHub, Pulse, Oracle, or an AI provider.</p>
              <div className="celar-ops-synthetic-row">
                <select value={syntheticScenario} onChange={(event) => setSyntheticScenario(event.target.value)}>{SYNTHETIC_SCENARIOS.map((item) => <option key={item} value={item}>{titleFrom(item)}</option>)}</select>
                <button type="button" disabled={busy || !readiness?.readiness?.syntheticFailureEnabled} onClick={runSyntheticFailure}>Run three synthetic failures</button>
              </div>
              {syntheticResult ? <pre>{JSON.stringify(syntheticResult, null, 2)}</pre> : null}
            </section>
          </div>
        ) : null}
      </div>
    </section>
  );
}
