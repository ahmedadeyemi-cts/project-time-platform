import { useMemo, useState } from 'react';
import './help.css';
import './help-assistant.css';
import HelpGovernancePanel from './help/HelpGovernancePanel.jsx';
import { applyHelpAnswerPreferences } from './help/help-answer-preferences.js';

const fallbackTopics = [
  {
    keywords: ['defect', 'bug', 'broken', 'issue', 'report a problem', 'module 076', '076'],
    title: 'Report a ProjectPulse defect',
    summary:
      'Open Module 076 to prepare a governed defect report. Record the affected module or route, expected behavior, observed behavior, business impact, environment, effective role, reproducible steps, sanitized evidence, priority, ownership, comments, resolution, verification, and GitHub linkage.',
    navigationTargets: ['#defect-tracker', '#system-diagnostics']
  },
  {
    keywords: ['guide', 'help', 'manual', 'documentation', 'module 999', '999'],
    title: 'Use the System User Guide',
    summary:
      'Module 999 is the authoritative user guide for global functions, installed modules, role expectations, page controls, step-by-step workflows, statuses, troubleshooting, and navigation. Pulse AI uses approved documentation and current permission evidence when the detailed Help service is available.',
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
    keywords: ['save', 'draft', 'refresh', 'lost', 'missing', 'not showing'],
    title: 'Protect unsaved ProjectPulse work',
    summary:
      'Wait for the save confirmation before refreshing, closing the tab, or changing pages. A successful save persists through the API; unsaved browser changes can be lost. If saved data does not reload, preserve the route, date, effective user, correlation evidence, and exact message before reporting a defect.',
    navigationTargets: ['#timesheet', '#defect-tracker']
  },
  {
    keywords: ['submit', 'approval', 'manager', 'approve', 'reject', 'decline'],
    title: 'Understand the time approval lifecycle',
    summary:
      'Submitted time moves to Module 002 Approval Inbox. Authorized reviewers inspect the project, task, date, hours, description, and applicable scope before approving or declining for correction. Later governed states may include PM approval, accounting readiness, reconciliation, locking, reopening, and audit evidence.',
    navigationTargets: ['#manager-approval', '#workflow', '#audit-history']
  },
  {
    keywords: ['opportunity', 'sales', 'presales', 'pipeline', 'won', 'lost'],
    title: 'Review opportunity and pipeline information',
    summary:
      'Module 063 tracks active and closed opportunities, ownership, customer context, estimated and actual revenue where available, shared Sales/Presales/Engineering tasks, completion accountability, and activity history. Access remains limited to the user’s authorized commercial scope.',
    navigationTargets: ['#opportunities', '#sales-insights']
  },
  {
    keywords: ['contract', 'prepaid', 'block of hours', 'balance', 'expiration'],
    title: 'Review contracts and block-of-hours balances',
    summary:
      'Module 060 manages authorized prepaid and block-of-hours records, credits, consumption, remaining balance, expiration, and Account Executive reporting. Financial values should be interpreted using the saved contract, rate, time, expense, billing, and reporting sources rather than model estimates.',
    navigationTargets: ['#contracts', '#reporting']
  },
  {
    keywords: ['project', 'task', 'assignment', 'customer', 'intake'],
    title: 'Navigate the project delivery lifecycle',
    summary:
      'Module 020 owns pre-project intake and resource handoff. Module 055D creates approved new projects; Module 055C manages existing projects, tasks, assignments, delivery details, and audit history. Module 019 provides role-scoped project documents and engineering context. The retired Work Task Builder no longer owns project or task creation.',
    navigationTargets: ['#project-intake', '#create-work-register', '#work-register', '#project-workspace']
  },
  {
    keywords: ['location', 'work location', 'timezone', 'resource profile'],
    title: 'Use work-location and resource context',
    summary:
      'Work-location and time-zone information supports timesheet, scheduling, capacity, and resource context. Select the correct work-location values where required. Authorized administration workflows maintain user, identity, directory, team, department, office, and profile information.',
    navigationTargets: ['#timesheet', '#user-admin', '#calendar-capacity']
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
    title: 'Understand ProjectPulse access',
    summary:
      'ProjectPulse evaluates the actual and effective user, role policy, module permission, requested action, and record-level scope. No Access hides the module and denies direct API access. View permits authorized reading only. HTTP 403 means the current effective identity is not authorized for that action or record.',
    navigationTargets: ['#roles-permissions-matrix', '#role-admin', '#user-admin']
  },
  {
    keywords: ['dark', 'light', 'theme', 'mode'],
    title: 'Change the ProjectPulse appearance',
    summary:
      'Use the appearance control in the top navigation or profile settings to switch between light and dark mode. The preference should apply across authenticated module pages and global application surfaces.',
    navigationTargets: ['#profile']
  }
];

function fallbackAnswer(question) {
  const normalized = question.trim().toLowerCase();
  const match = fallbackTopics.find((topic) =>
    topic.keywords.some((keyword) => normalized.includes(keyword))
  );

  return match ?? {
    title: 'Detailed ProjectPulse guidance is temporarily unavailable',
    summary:
      'The Pulse AI Help planning service could not be reached. Open Module 999 and search by module number, page, button, status, role, project, customer, workflow, error, or business term. This fallback does not inspect live records or infer an answer from restricted data.',
    navigationTargets: ['#user-guide', '#work-task-builder']
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

async function loadPulseAiPlan(question) {
  const url = new URL('/api/pulse-ai/v1/help-search/plan', window.location.origin);
  url.searchParams.set('question', question);
  const answerPreferences = applyHelpAnswerPreferences(url, question);
  const response = await fetch(`${url.pathname}${url.search}`, {
    method: 'GET',
    cache: 'no-store',
    headers: { Accept: 'application/json' }
  });
  const payload = await response.json().catch(() => ({}));
  if (!response.ok) {
    throw new Error(payload.message || `Pulse AI Help returned HTTP ${response.status}.`);
  }
  return { ...payload, answerPreferences };
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
    <div className="help-answer-navigation" aria-label="Relevant ProjectPulse pages">
      {values.slice(0, 8).map((target) => (
        <button type="button" key={target} onClick={() => navigateTo(target, close)}>
          {target.startsWith('#') ? titleFrom(target.slice(1)) : target}
        </button>
      ))}
    </div>
  );
}

function AnswerList({ heading, values }) {
  const rows = unique(values);
  if (!rows.length) return null;
  return (
    <section className="help-answer-section">
      <strong>{heading}</strong>
      <ul>{rows.map((row, index) => <li key={`${heading}-${index}`}>{row}</li>)}</ul>
    </section>
  );
}

function DetailedAssistantAnswer({ payload, close }) {
  const plan = payload?.plan ?? {};
  const direct = plan.directKnowledgeAnswer;
  const semanticQuery = plan.semanticQuery ?? {};
  const runtime = payload?.runtimeExecution ?? {};
  const answerContract = payload?.answerContract ?? {};
  /* GROUP_7_HELP_ANSWER_DETAIL_START */
  const answerPreferences = payload?.answerPreferences ?? { detailLevel: 'standard' };
  const detailLevel = answerPreferences.detailLevel ?? 'standard';
  const conciseAnswer = detailLevel === 'concise';
  const executiveAnswer = detailLevel === 'executive';
  const expandedAnswer = ['detailed', 'highly_detailed', 'technical', 'step_by_step'].includes(detailLevel);
  const technicalAnswer = ['highly_detailed', 'technical'].includes(detailLevel);
  /* GROUP_7_HELP_ANSWER_DETAIL_END */

  return (
    <div className="help-detailed-answer" data-answer-detail={detailLevel}>
      {direct ? (
        <>
          <div className="help-answer-heading">
            <span>Pulse AI detailed guidance</span>
            <strong>{direct.title}</strong>
          </div>
          <p className="help-answer-summary">{direct.summary}</p>
          {!executiveAnswer ? <AnswerList heading="Detailed procedure" values={direct.detailedSteps} /> : null}
          {!conciseAnswer ? <AnswerList heading="Important rules" values={direct.importantRules} /> : null}
          <div className="help-answer-evidence">
            <span>Source modules: {unique(direct.sourceModules).join(', ') || 'Not recorded'}</span>
            <span>Generated: {plan.generatedAt ? new Date(plan.generatedAt).toLocaleString() : 'Not recorded'}</span>
          </div>
          <NavigationTargets targets={direct.navigationTargets} close={close} />
        </>
      ) : (
        <>
          <div className="help-answer-heading">
            <span>Pulse AI comprehensive answer plan</span>
            <strong>{titleFrom(plan.status || 'governed plan ready')}</strong>
          </div>
          <p className="help-answer-summary">
            Pulse AI classified this question across {unique(plan.domains).length || 1} relevant domain(s) and prepared the evidence,
            filters, read-only tools, calculations, privacy controls, and answer structure required for a source-grounded response.
            Automatic multi-tool execution is not yet enabled for this question, so this response does not invent live values.
          </p>
          <AnswerList heading="Relevant business and system domains" values={unique(plan.domains).map(titleFrom)} />
          <AnswerList heading="Required evidence" values={plan.requiredEvidence} />
          <AnswerList heading="Filters that must be resolved" values={plan.filtersToResolve} />
          <AnswerList heading="Deterministic calculations" values={plan.deterministicCalculations} />
          <AnswerList heading="Required answer sections" values={plan.answerSections} />
          <AnswerList heading="Detailed execution sequence" values={plan.executionSteps} />
          <AnswerList heading="Privacy controls" values={plan.privacyControls} />
          <AnswerList heading="Missing inputs before exact execution" values={plan.missingInputs} />
          <section className="help-answer-section">
            <strong>Governed semantic query</strong>
            <dl className="help-answer-query-grid">
              <div><dt>Metrics</dt><dd>{unique(semanticQuery.metrics).join(', ') || 'No exact metric selected'}</dd></div>
              <div><dt>Dimensions</dt><dd>{unique(semanticQuery.dimensions).join(', ') || 'Project'}</dd></div>
              <div><dt>Required tools</dt><dd>{unique(plan.requiredTools).join(', ') || 'Product knowledge'}</dd></div>
              <div><dt>Arbitrary SQL</dt><dd>{semanticQuery.arbitrarySqlAllowed ? 'Allowed' : 'Not allowed'}</dd></div>
              <div><dt>Unknown values</dt><dd>{semanticQuery.unknownValuesPreserved ? 'Preserved' : 'Not recorded'}</dd></div>
              <div><dt>External execution</dt><dd>{titleFrom(semanticQuery.externalExecution || 'not authorized')}</dd></div>
            </dl>
          </section>
          <div className="help-answer-evidence">
            <span>Owning modules: {unique(plan.owningModules).join(', ') || 'Multiple registered modules'}</span>
            <span>Live execution: {runtime.automaticMultiToolExecutionEnabled ? 'Enabled' : 'Not yet enabled'}</span>
          </div>
          <NavigationTargets targets={['#work-task-builder', '#user-guide']} close={close} />
        </>
      )}

      <div className="help-answer-preference-evidence">
        <span>Answer detail: {titleFrom(detailLevel)}</span>
        <span>Preference source: {titleFrom(answerPreferences.preferenceSource ?? 'saved_preference')}</span>
        {answerPreferences.includeRepositoryContext ? <span>Repository context requested</span> : null}
        {answerPreferences.includeAssumptions ? <span>Assumptions requested</span> : null}
        {answerPreferences.includeSourceCitations ? <span>Source citations requested</span> : null}
      </div>
      {technicalAnswer ? (
      <details className="help-answer-contract">
        <summary>Answer quality contract</summary>
        <AnswerList heading="Minimum sections" values={answerContract.minimumSections} />
        <AnswerList heading="Required qualities" values={answerContract.mustInclude} />
        {answerContract.unsupportedClaimPolicy ? <p><strong>Unsupported claims:</strong> {answerContract.unsupportedClaimPolicy}</p> : null}
      </details>
      ) : null}
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
    return <div className="help-message assistant help-message-loading">Building a detailed, permission-aware answer plan…</div>;
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
      text: 'Ask a ProjectPulse product, workflow, project, document, reporting, financial, security, or operations question. Pulse AI will provide detailed guidance or show the exact evidence and tools required for a trustworthy live answer.'
    }
  ]);

  const suggestions = useMemo(
    () => [
      'How does Pulse AI use an SOW and GSD for a timesheet suggestion?',
      'How do I create and maintain a project?',
      'Why can a user not see a module?',
      'How should FlowHive create a project plan?',
      'How should I analyze project budget variance?',
      'How do I report a defect?'
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
      const payload = await loadPulseAiPlan(cleanQuestion);
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
                ? `The detailed Pulse AI Help service was unavailable: ${error.message}`
                : 'The detailed Pulse AI Help service was unavailable.',
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
        <aside className="help-panel pulse-ai-help-panel" aria-label="Pulse AI ProjectPulse help and search assistant">
          <div className="help-header">
            <div>
              <strong>Pulse AI Help & Search</strong>
              <span>Detailed, permission-aware ProjectPulse guidance</span>
            </div>
            <button type="button" onClick={closePanel} aria-label="Close help assistant">
              ×
            </button>
          </div>

          <div className="help-quick-actions">
            <button className="help-full-guide-button" type="button" onClick={openCompleteGuide}>
              Module 999 — System User Guide
            </button>
            <button className="help-pulse-ai-button" type="button" onClick={openPulseAi}>
              Module 011 — Pulse AI Workbench
            </button>
            <button className="help-report-defect-button" type="button" onClick={openDefectTracker}>
              Report a defect — Module 076
            </button>
          </div>

          {/* GROUP_7_HELP_GOVERNANCE_PANEL_START */}
          <HelpGovernancePanel />
          {/* GROUP_7_HELP_GOVERNANCE_PANEL_END */}
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
              placeholder="Ask a detailed question about any authorized ProjectPulse function…"
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
