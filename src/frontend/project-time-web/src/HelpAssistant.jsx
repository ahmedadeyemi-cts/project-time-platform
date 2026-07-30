import { useMemo, useState } from 'react';
import './help.css';
import './help-assistant.css';
import './pulse-ai-system-answer.css';

const fallbackTopics = [
  {
    keywords: ['api', 'endpoint', 'route', 'system health', 'troubleshoot', 'diagnostic', 'correlation'],
    title: 'Use Pulse system operations evidence',
    summary:
      'Open Module 013 to inspect every API registered in the running application, its owning module, authentication, permission expectations, dependencies, latest status, latency, correlation evidence, and safe-retest eligibility. Use Module 016 for sanitized evidence, Module 998 for persistent diagnostics, Module 076 for reproducible defects, Module 077 for release evidence, and Module 078 for observability and SLOs.',
    navigationTargets: ['#service-control', '#backup-retention', '#system-diagnostics', '#defect-tracker']
  },
  {
    keywords: ['future', 'enhancement', 'roadmap', 'add feature', 'new feature', 'next phase', 'improve'],
    title: 'Plan a future Pulse enhancement',
    summary:
      'Pulse AI can prepare a comprehensive draft enhancement plan covering current capabilities, affected modules and APIs, capability gaps, proposed architecture, data and migration changes, permissions, privacy, integrations, testing, observability, risks, acceptance criteria, release sequence, and human approval gates.',
    navigationTargets: ['#work-task-builder', '#system-architecture', '#release-deployment-control']
  },
  {
    keywords: ['defect', 'bug', 'broken', 'issue', 'report a problem', 'module 076', '076'],
    title: 'Report a Pulse defect',
    summary:
      'Open Module 076 to prepare a governed defect report. Record the affected module or route, expected behavior, observed behavior, business impact, environment, effective role, reproducible steps, sanitized evidence, priority, ownership, comments, resolution, verification, and GitHub linkage.',
    navigationTargets: ['#defect-tracker', '#system-diagnostics']
  },
  {
    keywords: ['guide', 'help', 'manual', 'documentation', 'module 999', '999'],
    title: 'Use the complete Pulse guide',
    summary:
      'Module 999 is the authoritative user guide for global functions, installed modules, role expectations, page controls, step-by-step workflows, statuses, troubleshooting, and navigation. Pulse AI combines that knowledge with authorized private documents and governed live tools when available.',
    navigationTargets: ['#user-guide', '#work-task-builder']
  },
  {
    keywords: ['timesheet', 'time', 'hours', 'normal', 'afterhours', 'ot', 'overtime'],
    title: 'Prepare and submit an accurate timesheet',
    summary:
      'Module 001 supports Weekly Grid, Daily Focus, Guided Add, Quick Entry List, and Smart Work Log. Select the correct project task, request, or non-project category; enter Normal or Afterhours time; add a factual description; review any AI suggestion; save the draft; and submit only when the week is complete and eligible.',
    navigationTargets: ['#timesheet', '#manager-approval']
  },
  {
    keywords: ['submit', 'approval', 'manager', 'approve', 'reject', 'decline'],
    title: 'Understand the time approval lifecycle',
    summary:
      'Submitted time moves to Module 002 Approval Inbox. Authorized reviewers inspect the project, task, date, hours, description, and applicable scope before approving or declining for correction. Later governed states may include PM approval, accounting readiness, reconciliation, locking, reopening, and audit evidence.',
    navigationTargets: ['#manager-approval', '#workflow', '#audit-history']
  },
  {
    keywords: ['project', 'task', 'assignment', 'customer', 'intake'],
    title: 'Navigate the project delivery lifecycle',
    summary:
      'Module 020 owns pre-project intake and resource handoff. Module 055D creates approved new projects; Module 055C manages existing projects, tasks, assignments, delivery details, and audit history. Module 019 provides role-scoped project documents and engineering context. The retired Work Task Builder no longer owns project or task creation.',
    navigationTargets: ['#project-intake', '#create-work-register', '#work-register', '#project-workspace']
  },
  {
    keywords: ['utilization', 'target', 'billable', 'pto', 'vacation', 'capacity'],
    title: 'Understand utilization and capacity',
    summary:
      'Module 003 compares eligible billable time with approved targets. Modules 057 and 070 provide calendar, assignment, capacity, and pipeline context. A complete answer should identify the period, target definition, eligible hours, exclusions, current value, remaining hours, assignment load, and source freshness.',
    navigationTargets: ['#utilization', '#calendar-capacity', '#capacity-pipeline-forecast']
  },
  {
    keywords: ['access', 'permission', 'role', '403', 'denied', 'no access'],
    title: 'Understand Pulse access',
    summary:
      'Pulse evaluates the actual and effective user, role policy, module permission, requested action, and record-level scope. No Access hides the module and denies direct API access. View permits authorized reading only. HTTP 403 means the current effective identity is not authorized for that action or record.',
    navigationTargets: ['#roles-permissions-matrix', '#role-admin', '#user-admin']
  }
];

function fallbackAnswer(question) {
  const normalized = question.trim().toLowerCase();
  const match = fallbackTopics.find((topic) =>
    topic.keywords.some((keyword) => normalized.includes(keyword))
  );

  return match ?? {
    title: 'Detailed Pulse guidance is temporarily unavailable',
    summary:
      'The unified Pulse AI answer service could not be reached. Open Module 999 and search by module number, page, button, status, role, project, customer, workflow, API route, correlation ID, error, or business term. This fallback does not inspect live records or infer an answer from restricted data.',
    navigationTargets: ['#user-guide', '#work-task-builder', '#service-control']
  };
}

function titleFrom(value) {
  return String(value ?? '')
    .replaceAll('_', ' ')
    .replace(/\b\w/g, (letter) => letter.toUpperCase());
}

function unique(values) {
  return [...new Set((Array.isArray(values) ? values : []).filter(Boolean))];
}

function sessionHeaders(extra = {}) {
  const token = window.localStorage.getItem('projectPulseSessionToken')
    ?? window.sessionStorage.getItem('projectPulseSessionToken')
    ?? '';
  return {
    ...extra,
    ...(token ? {
      Authorization: `Bearer ${token}`,
      'X-ProjectPulse-Session': token,
      'X-Project-Pulse-Session': token,
      'X-Session-Token': token
    } : {})
  };
}

async function loadPulseAiAnswer(question) {
  const response = await fetch('/api/pulse-ai/v1/answer', {
    method: 'POST',
    credentials: 'include',
    cache: 'no-store',
    headers: sessionHeaders({
      Accept: 'application/json',
      'Content-Type': 'application/json'
    }),
    body: JSON.stringify({
      question,
      detailLevel: 'comprehensive',
      includeAuthorizedProjectDocuments: true,
      includeDirectProductKnowledge: true,
      maximumResults: 150
    })
  });
  const payload = await response.json().catch(() => ({}));
  if (!response.ok) {
    throw new Error(payload.message || `Pulse AI returned HTTP ${response.status}.`);
  }
  return payload;
}

function navigateTo(target, close) {
  if (!target) return;
  close();
  if (target.startsWith('#')) {
    window.location.hash = target.slice(1);
    return;
  }
  window.location.assign(target);
}

function NavigationTargets({ targets, close }) {
  const values = unique(targets);
  if (!values.length) return null;
  return (
    <div className="help-answer-navigation" aria-label="Relevant Pulse pages">
      {values.slice(0, 25).map((target) => (
        <button type="button" key={target} onClick={() => navigateTo(target, close)}>
          {target.startsWith('#') ? titleFrom(target.slice(1)) : target}
        </button>
      ))}
    </div>
  );
}

function AnswerList({ heading, values, ordered = false }) {
  const rows = unique(values);
  if (!rows.length) return null;
  const List = ordered ? 'ol' : 'ul';
  return (
    <section className="help-answer-section">
      <strong>{heading}</strong>
      <List>{rows.map((row, index) => <li key={`${heading}-${index}`}>{row}</li>)}</List>
    </section>
  );
}

function dateTime(value) {
  if (!value) return 'Not recorded';
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? 'Not recorded' : parsed.toLocaleString();
}

function statusTone(value) {
  const status = String(value ?? '').toLowerCase();
  if (['healthy', 'completed', 'succeeded', 'ready', 'active', 'supported'].includes(status)) return 'healthy';
  if (['failed', 'critical', 'unavailable'].includes(status)) return 'critical';
  if (['partial', 'warning', 'rejected', 'degraded', 'blocked'].includes(status)) return 'warning';
  return 'neutral';
}

function ApiInventory({ apis }) {
  const rows = Array.isArray(apis) ? apis : [];
  if (!rows.length) return null;
  return (
    <section className="help-answer-section pulse-ai-answer-api-section">
      <strong>Relevant running APIs</strong>
      <div className="pulse-ai-answer-api-table-wrap">
        <table className="pulse-ai-answer-api-table">
          <thead>
            <tr>
              <th>Module</th>
              <th>Method</th>
              <th>Route</th>
              <th>Status</th>
              <th>Latency</th>
              <th>Safe retest</th>
            </tr>
          </thead>
          <tbody>
            {rows.slice(0, 100).map((api) => (
              <tr key={`${api.apiId || api.path}-${api.method}`}>
                <td>{api.moduleCode} · {api.moduleName}</td>
                <td><code>{api.method}</code></td>
                <td><code>{api.path}</code><small>{api.purpose}</small></td>
                <td><span className={`pulse-ai-answer-status ${statusTone(api.currentStatus)}`}>{titleFrom(api.currentStatus)}</span></td>
                <td>{api.responseTimeMs == null ? 'Not observed' : `${Number(api.responseTimeMs).toFixed(2)} ms`}</td>
                <td>{titleFrom(api.retestCapability)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      {rows.length > 100 ? <p className="pulse-ai-answer-note">Showing the first 100 of {rows.length} matching APIs. Open Module 013 for the complete searchable inventory.</p> : null}
    </section>
  );
}

function OperationalCitations({ citations }) {
  const rows = Array.isArray(citations) ? citations : [];
  if (!rows.length) return null;
  return (
    <details className="help-answer-contract pulse-ai-answer-citations">
      <summary>Operational citations ({rows.length})</summary>
      <div className="pulse-ai-answer-citation-list">
        {rows.slice(0, 100).map((citation) => (
          <article key={`${citation.citationId}-${citation.evidenceType}-${citation.path}`}>
            <div>
              <strong>[{citation.citationId}] {titleFrom(citation.evidenceType)}</strong>
              <span className={`pulse-ai-answer-status ${statusTone(citation.status)}`}>{titleFrom(citation.status)}</span>
            </div>
            <p>{citation.sourceModule ? `Module ${citation.sourceModule} · ` : ''}{citation.sourceName}</p>
            {citation.path ? <code>{citation.method} {citation.path}</code> : null}
            <small>
              {citation.statusCode != null ? `HTTP ${citation.statusCode} · ` : ''}
              {citation.responseTimeMs != null ? `${Number(citation.responseTimeMs).toFixed(2)} ms · ` : ''}
              {citation.errorCode ? `${citation.errorCode} · ` : ''}
              {citation.correlationId ? `Correlation ${citation.correlationId} · ` : ''}
              {dateTime(citation.observedAt)}
            </small>
          </article>
        ))}
      </div>
    </details>
  );
}

function ModuleKnowledge({ modules }) {
  const rows = Array.isArray(modules) ? modules : [];
  if (!rows.length) return null;
  return (
    <section className="help-answer-section">
      <strong>Relevant Pulse modules</strong>
      <div className="pulse-ai-answer-module-grid">
        {rows.map((module) => (
          <article key={module.moduleNumber}>
            <span>Module {module.moduleNumber}</span>
            <h4>{module.displayName}</h4>
            <p>{module.purpose}</p>
            <small>#{module.route} · {module.group}</small>
          </article>
        ))}
      </div>
    </section>
  );
}

function FutureEnhancementDetails({ result }) {
  if (!result || result.featureCode !== 'future_enhancement_planner') return null;
  return (
    <div className="pulse-ai-future-plan">
      <ModuleKnowledge modules={result.affectedModules} />
      <AnswerList heading="Current capabilities" values={result.currentCapabilities} />
      <AnswerList heading="Capability gaps" values={result.capabilityGaps} />
      <AnswerList heading="Proposed architecture" values={result.proposedArchitecture} ordered />
      <AnswerList heading="Data and migration changes" values={result.dataAndMigrationChanges} />
      <AnswerList heading="API and integration changes" values={result.apiAndIntegrationChanges} />
      <AnswerList heading="Permission and role changes" values={result.permissionAndRoleChanges} />
      <AnswerList heading="Privacy and security controls" values={result.privacyAndSecurityControls} />
      <AnswerList heading="Observability and audit" values={result.observabilityAndAudit} />
      <AnswerList heading="Testing strategy" values={result.testingStrategy} />
      <AnswerList heading="Release sequence" values={result.releaseSequence} ordered />
      <AnswerList heading="Acceptance criteria" values={result.acceptanceCriteria} />
      <AnswerList heading="Dependencies" values={result.dependencies} />
      <AnswerList heading="Estimated phases" values={result.estimatedPhases} ordered />
      <ApiInventory apis={result.currentApis} />
    </div>
  );
}

function DetailedAssistantAnswer({ payload, close }) {
  const envelope = payload?.response ?? payload ?? {};
  const result = envelope?.result ?? payload?.result ?? {};
  const answer = result?.answer ?? result?.result?.answer ?? null;
  const mode = envelope?.mode ?? result?.featureCode ?? 'governed_answer';
  const status = envelope?.status ?? result?.status ?? 'completed';
  const apis = result?.apis ?? result?.currentApis ?? [];
  const citations = result?.operationalCitations ?? [];
  const modules = result?.modules ?? result?.affectedModules ?? [];
  const warnings = result?.warnings ?? [];
  const missingEvidence = result?.missingEvidence ?? [];
  const conflicts = result?.conflicts ?? answer?.conflicts ?? [];

  if (!answer) {
    return (
      <div className="help-detailed-answer">
        <div className="help-answer-heading">
          <span>Pulse AI governed response</span>
          <strong>{titleFrom(status)}</strong>
        </div>
        <p className="help-answer-summary">Pulse AI returned a governed response, but no displayable detailed-answer contract was present.</p>
        <pre className="pulse-ai-answer-json">{JSON.stringify(result, null, 2)}</pre>
      </div>
    );
  }

  return (
    <div className="help-detailed-answer pulse-ai-unified-answer">
      <div className="pulse-ai-answer-mode-row">
        <span className="pulse-ai-answer-mode">{titleFrom(mode)}</span>
        <span className={`pulse-ai-answer-status ${statusTone(status)}`}>{titleFrom(status)}</span>
      </div>
      <div className="help-answer-heading">
        <span>Pulse AI direct answer</span>
        <strong>{answer.directConclusion}</strong>
      </div>
      {answer.executiveSummary ? <p className="help-answer-summary">{answer.executiveSummary}</p> : null}
      <AnswerList heading="Scope and filters" values={answer.scopeAndFilters} />
      <AnswerList heading="Detailed analysis" values={answer.detailedAnalysis} />
      <ApiInventory apis={apis} />
      <AnswerList heading="Source evidence" values={answer.sourceEvidence} />
      <OperationalCitations citations={citations} />
      <AnswerList heading="Calculations" values={answer.calculations} />
      <AnswerList heading="Known, unknown, unavailable, and stale values" values={answer.knownUnknownAndStaleValues} />
      <AnswerList heading="Root-cause hypotheses" values={result.rootCauseHypotheses} />
      <AnswerList heading="Assumptions" values={answer.assumptions} />
      <AnswerList heading="Conflicts" values={conflicts} />
      <AnswerList heading="Limitations" values={answer.limitations} />
      <AnswerList heading="Risks and implications" values={answer.risksAndImplications} />
      <AnswerList heading="Recommended actions" values={answer.recommendedActions} ordered />
      <AnswerList heading="Troubleshooting sequence" values={result.troubleshootingSequence} ordered />
      <AnswerList heading="Safe API retest candidates" values={result.safeRetestCandidates} />
      <AnswerList heading="Warnings" values={warnings} />
      <AnswerList heading="Missing evidence" values={missingEvidence} />
      <ModuleKnowledge modules={modules} />
      <FutureEnhancementDetails result={result} />
      <div className="help-answer-evidence pulse-ai-answer-evidence-grid">
        <span>Confidence: {answer.confidence == null ? 'Not recorded' : `${(Number(answer.confidence) * 100).toFixed(0)}%`}</span>
        <span>Data as of: {dateTime(answer.dataAsOf ?? result.dataAsOf ?? envelope.generatedAt)}</span>
        {result.correlationId ? <span>Correlation: {result.correlationId}</span> : null}
        {result.summary?.releaseSha ? <span>Release: {result.summary.releaseSha}</span> : null}
        {result.planId ? <span>Enhancement plan: {result.planId}</span> : null}
        {result.investigationId ? <span>Investigation: {result.investigationId}</span> : null}
      </div>
      {answer.confidenceExplanation ? <p className="pulse-ai-answer-note"><strong>Confidence explanation:</strong> {answer.confidenceExplanation}</p> : null}
      <NavigationTargets targets={answer.navigationTargets} close={close} />
      <details className="help-answer-contract">
        <summary>Answer and privacy contract</summary>
        <p><strong>Routing:</strong> {envelope.routingReason ?? 'Governed Pulse AI routing'}</p>
        <p><strong>Unsupported claims:</strong> Pulse AI must state when current authorized evidence is insufficient and must not fabricate a live value, API state, source, or completed action.</p>
        <p><strong>Privacy:</strong> Request bodies, query strings, raw logs, full exception messages, credentials, and provider secrets are not returned by the system-operations answer path.</p>
      </details>
    </div>
  );
}

function FallbackAssistantAnswer({ answer, warning, close }) {
  return (
    <div className="help-detailed-answer is-fallback">
      <div className="help-answer-heading">
        <span>Local governed fallback</span>
        <strong>{answer.title}</strong>
      </div>
      <p className="help-answer-summary">{answer.summary}</p>
      {warning ? <p className="help-answer-warning">{warning}</p> : null}
      <NavigationTargets targets={answer.navigationTargets} close={close} />
    </div>
  );
}

function AssistantMessage({ message, close }) {
  if (message.loading) {
    return <div className="help-message assistant help-message-loading">Collecting authorized evidence and building a comprehensive answer…</div>;
  }
  if (message.payload) {
    return <div className="help-message assistant is-detailed"><DetailedAssistantAnswer payload={message.payload} close={close} /></div>;
  }
  if (message.fallback) {
    return <div className="help-message assistant is-detailed"><FallbackAssistantAnswer answer={message.fallback} warning={message.warning} close={close} /></div>;
  }
  return <div className="help-message assistant">{message.text}</div>;
}

export default function HelpAssistant() {
  const [isOpen, setIsOpen] = useState(false);
  const [question, setQuestion] = useState('');
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [messages, setMessages] = useState([
    {
      role: 'assistant',
      text: 'Ask any authorized question about Pulse: modules, workflows, projects, documents, reports, financials, permissions, every running API, system health, troubleshooting, correlation evidence, or a future enhancement. Pulse AI will give a direct, detailed answer and clearly separate live evidence from unknowns.'
    }
  ]);

  const suggestions = useMemo(
    () => [
      'What APIs are running on the system right now?',
      'Which APIs are failing or being rejected, and how should I troubleshoot them?',
      'What is Pulse AI and what can it answer?',
      'How does Pulse AI use an SOW and GSD for a timesheet suggestion?',
      'How should FlowHive create a project plan?',
      'Why can a user not see a module?',
      'Create a future enhancement plan for proactive API failure detection and automated evidence collection.'
    ],
    []
  );

  async function submitQuestion(nextQuestion = question) {
    const cleanQuestion = nextQuestion.trim();
    if (!cleanQuestion || isSubmitting) return;

    const loadingId = `loading-${Date.now()}`;
    setMessages((current) => [
      ...current,
      { role: 'user', text: cleanQuestion },
      { role: 'assistant', loading: true, id: loadingId }
    ]);
    setQuestion('');
    setIsSubmitting(true);

    try {
      const payload = await loadPulseAiAnswer(cleanQuestion);
      setMessages((current) => current.map((message) =>
        message.id === loadingId
          ? { role: 'assistant', payload, id: `answer-${Date.now()}` }
          : message
      ));
    } catch (error) {
      const fallback = fallbackAnswer(cleanQuestion);
      setMessages((current) => current.map((message) =>
        message.id === loadingId
          ? {
              role: 'assistant',
              fallback,
              warning: error instanceof Error
                ? `The unified Pulse AI answer service was unavailable: ${error.message}`
                : 'The unified Pulse AI answer service was unavailable.',
              id: `fallback-${Date.now()}`
            }
          : message
      ));
    } finally {
      setIsSubmitting(false);
    }
  }

  function closePanel() {
    setIsOpen(false);
  }

  function openCompleteGuide() {
    closePanel();
    window.location.hash = 'user-guide';
  }

  function openPulseAi() {
    closePanel();
    window.location.hash = 'work-task-builder';
  }

  function openSystemHealth() {
    closePanel();
    window.location.hash = 'service-control';
  }

  function openDefectTracker() {
    closePanel();
    const destination = new URL(window.location.href);
    destination.searchParams.set('defectSource', 'help');
    destination.hash = 'defect-tracker';
    window.location.assign(destination.toString());
  }

  return (
    <>
      <button className="help-launcher" type="button" onClick={() => setIsOpen(true)}>
        Ask Pulse AI
      </button>

      {isOpen ? (
        <aside className="help-panel pulse-ai-help-panel" aria-label="Pulse AI help, live system search, and troubleshooting assistant">
          <div className="help-header">
            <div>
              <strong>Pulse AI Help, Search &amp; Operations</strong>
              <span>Direct, comprehensive, permission-aware answers</span>
            </div>
            <button type="button" onClick={closePanel} aria-label="Close help assistant">
              ×
            </button>
          </div>

          <div className="help-quick-actions">
            <button className="help-full-guide-button" type="button" onClick={openCompleteGuide}>
              Module 999 — Complete User Guide
            </button>
            <button className="help-pulse-ai-button" type="button" onClick={openPulseAi}>
              Module 011 — Pulse AI Workbench
            </button>
            <button className="help-system-health-button" type="button" onClick={openSystemHealth}>
              Module 013 — APIs &amp; System Health
            </button>
            <button className="help-report-defect-button" type="button" onClick={openDefectTracker}>
              Report a defect — Module 076
            </button>
          </div>

          <div className="help-messages">
            {messages.map((message, index) => (
              message.role === 'user' ? (
                <div className="help-message user" key={message.id || `user-${index}`}>{message.text}</div>
              ) : (
                <AssistantMessage message={message} close={closePanel} key={message.id || `assistant-${index}`} />
              )
            ))}
          </div>

          <div className="help-suggestions" aria-label="Suggested Pulse AI questions">
            {suggestions.map((suggestion) => (
              <button type="button" key={suggestion} onClick={() => submitQuestion(suggestion)} disabled={isSubmitting}>
                {suggestion}
              </button>
            ))}
          </div>

          <form
            className="help-input-row"
            onSubmit={(event) => {
              event.preventDefault();
              void submitQuestion();
            }}
          >
            <textarea
              value={question}
              rows={2}
              placeholder="Ask about any authorized Pulse function, API, problem, report, financial question, or future enhancement…"
              onChange={(event) => setQuestion(event.target.value)}
              disabled={isSubmitting}
            />
            <button type="submit" disabled={isSubmitting || !question.trim()}>
              {isSubmitting ? 'Analyzing…' : 'Ask'}
            </button>
          </form>
        </aside>
      ) : null}
    </>
  );
}
