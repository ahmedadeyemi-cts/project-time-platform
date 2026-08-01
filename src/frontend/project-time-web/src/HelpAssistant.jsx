import { useEffect, useMemo, useRef, useState } from 'react';
import './help.css';
import './help-assistant.css';
import HelpGovernancePanel from './help/HelpGovernancePanel.jsx';
import { applyHelpAnswerPreferences } from './help/help-answer-preferences.js';
import './pulse-ai-system-chat.css';

const QUICK_QUESTIONS = Object.freeze([
  'What APIs are running on the system?',
  'Troubleshoot the current platform and show me the strongest evidence.',
  'Explain Celar AI and everything it can do.',
  'Design a future enhancement for Pulse using the current architecture.',
  'What is unhealthy, unavailable, unauthorized, or missing right now?',
  'How do Modules 013, 016, 078, and 998 work together for troubleshooting?'
]);

const WELCOME_MESSAGE = Object.freeze({
  id: 'welcome',
  role: 'assistant',
  text: 'Ask any question about Pulse. I can explain modules and workflows, discover the APIs registered in the running application, use authorized read-only troubleshooting evidence, analyze projects and private documents, explain reports and financials, and prepare detailed future-enhancement blueprints. Completed conversations remain available after closing or refreshing this page.'
});

function asArray(value) {
  return Array.isArray(value) ? value : [];
}

function unique(values) {
  return [...new Set(asArray(values).filter((value) => value !== null && value !== undefined && String(value).trim()))];
}

function rebrandCelarString(value) {
  return String(value ?? '')
    .replaceAll('CELAR AI', 'CELAR AI')
    .replaceAll('Celar AI', 'Celar AI');
}

function rebrandCelarValue(value) {
  if (typeof value === 'string') return rebrandCelarString(value);
  if (Array.isArray(value)) return value.map(rebrandCelarValue);
  if (!value || typeof value !== 'object') return value;
  return Object.fromEntries(Object.entries(value).map(([key, item]) => [key, rebrandCelarValue(item)]));
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

function formatPercent(value) {
  const numeric = Number(value);
  return Number.isFinite(numeric) ? `${Math.round(numeric * 100)}%` : 'Not recorded';
}

async function readJson(response) {
  const payload = await response.json().catch(() => ({}));
  if (!response.ok) {
    const error = new Error(payload.message || payload?.result?.answer?.directConclusion || `Request returned HTTP ${response.status}.`);
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

async function postJson(path, body) {
  return readJson(await fetch(path, {
    method: 'POST',
    cache: 'no-store',
    headers: {
      Accept: 'application/json',
      'Content-Type': 'application/json'
    },
    body: JSON.stringify(body ?? {})
  }));
}

async function loadLegacyPlan(question) {
  const url = new URL('/api/pulse-ai/v1/help-search/plan', window.location.origin);
  url.searchParams.set('question', question);
  const answerPreferences = applyHelpAnswerPreferences(url, question);
  const payload = await getJson(`${url.pathname}${url.search}`);
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
    <div className="help-answer-navigation" aria-label="Relevant Pulse pages">
      {values.slice(0, 16).map((target) => (
        <button type="button" key={target} onClick={() => navigateTo(target, close)}>
          {target.startsWith('#') ? titleFrom(target.slice(1)) : target}
        </button>
      ))}
    </div>
  );
}

function AnswerList({ heading, values, open = false, ordered = false }) {
  const rows = unique(values);
  if (!rows.length) return null;
  const List = ordered ? 'ol' : 'ul';
  return (
    <details className="pulse-ai-system-section" open={open}>
      <summary><span>{heading}</span><small>{rows.length}</small></summary>
      <List>{rows.map((row, index) => <li key={`${heading}-${index}`}>{String(row)}</li>)}</List>
    </details>
  );
}

function EvidenceBadges({ result }) {
  const answer = result?.answer ?? {};
  return (
    <div className="pulse-ai-system-evidence-badges">
      <span>Status: {titleFrom(result?.status || 'unknown')}</span>
      <span>Intent: {titleFrom(result?.intentCode || 'general system')}</span>
      <span>Confidence: {formatPercent(answer.confidence)}</span>
      <span>Data as of: {formatDate(answer.dataAsOf)}</span>
      <span>Sources: {asArray(result?.sources).length}</span>
      <span>APIs: {asArray(result?.relevantApis).length}</span>
      <span>Tools: {asArray(result?.toolResults).length}</span>
      <span>Saved: {result?.persisted ? 'Yes' : 'No'}</span>
    </div>
  );
}

function EnhancementBlueprint({ blueprint }) {
  if (!blueprint) return null;
  return (
    <details className="pulse-ai-system-blueprint" open>
      <summary>Future enhancement blueprint</summary>
      <div className="pulse-ai-system-blueprint-body">
        <h5>{blueprint.requestedCapability || 'Requested capability'}</h5>
        <p>{blueprint.businessOutcome}</p>
        <AnswerList heading="Affected modules" values={blueprint.affectedModules} open />
        <AnswerList heading="Current capabilities" values={blueprint.currentCapabilities} open />
        <AnswerList heading="Gaps" values={blueprint.gaps} open />
        <AnswerList heading="Proposed architecture" values={blueprint.proposedArchitecture} open />
        <AnswerList heading="Proposed APIs" values={blueprint.proposedApis} />
        <AnswerList heading="Data and migration considerations" values={blueprint.dataAndMigrationConsiderations} />
        <AnswerList heading="Security and privacy controls" values={blueprint.securityAndPrivacyControls} open />
        <AnswerList heading="Operational and support controls" values={blueprint.operationalAndSupportControls} />
        <AnswerList heading="Implementation phases" values={blueprint.implementationPhases} open ordered />
        <AnswerList heading="Test strategy" values={blueprint.testStrategy} />
        <AnswerList heading="Rollout and rollback" values={blueprint.rolloutAndRollback} />
        <AnswerList heading="Risks" values={blueprint.risks} />
        <AnswerList heading="Acceptance criteria" values={blueprint.acceptanceCriteria} open />
        <AnswerList heading="Dependencies" values={blueprint.dependencies} />
      </div>
    </details>
  );
}

function ApiInventory({ apis }) {
  const rows = asArray(apis);
  const [filter, setFilter] = useState('');
  const visible = useMemo(() => {
    const normalized = filter.trim().toLowerCase();
    if (!normalized) return rows;
    return rows.filter((api) => [
      api.method,
      api.routePattern,
      api.moduleCode,
      api.moduleName,
      api.purpose,
      api.registrationStatus
    ].some((value) => String(value ?? '').toLowerCase().includes(normalized)));
  }, [filter, rows]);
  if (!rows.length) return null;
  return (
    <details className="pulse-ai-system-api-inventory">
      <summary>Registered APIs returned for this answer <small>{rows.length}</small></summary>
      <div className="pulse-ai-system-api-toolbar">
        <label>
          Filter APIs
          <input
            value={filter}
            onChange={(event) => setFilter(event.target.value)}
            placeholder="module, method, route, purpose…"
          />
        </label>
        <span>{visible.length} shown</span>
      </div>
      <div className="pulse-ai-system-table-wrap">
        <table>
          <thead>
            <tr><th>Method</th><th>Route</th><th>Module</th><th>Purpose</th><th>Registered</th><th>Safe retest</th></tr>
          </thead>
          <tbody>
            {visible.map((api) => (
              <tr key={`${api.apiId}-${api.method}`}>
                <td><code>{api.method}</code></td>
                <td><code>{api.routePattern}</code></td>
                <td>{api.moduleCode} — {api.moduleName}</td>
                <td>{api.purpose}</td>
                <td>{titleFrom(api.registrationStatus)}</td>
                <td>{api.safeRetestSupported ? 'Yes' : api.safeRetestReason}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </details>
  );
}

function SourceEvidence({ sources }) {
  const rows = asArray(sources);
  if (!rows.length) return null;
  return (
    <details className="pulse-ai-system-sources">
      <summary>Source and freshness evidence <small>{rows.length}</small></summary>
      <div className="pulse-ai-system-source-grid">
        {rows.map((source) => (
          <article key={source.sourceId}>
            <div><strong>Source {source.sourceId}</strong><span>{titleFrom(source.status)}</span></div>
            <h6>{source.sourceName}</h6>
            <p>{source.moduleCode} · {source.method} {source.path}</p>
            <small>{source.evidenceScope}</small>
            <small>Observed {formatDate(source.observedAt)} · {titleFrom(source.freshness)}</small>
          </article>
        ))}
      </div>
    </details>
  );
}

function ToolEvidence({ tools }) {
  const rows = asArray(tools);
  if (!rows.length) return null;
  return (
    <details className="pulse-ai-system-tools">
      <summary>Governed tool execution <small>{rows.length}</small></summary>
      <div className="pulse-ai-system-tool-grid">
        {rows.map((tool) => (
          <article key={tool.toolCode} className={tool.status === 'succeeded' ? 'is-success' : 'is-warning'}>
            <div><strong>{tool.toolName}</strong><span>HTTP {tool.statusCode || '—'}</span></div>
            <p>{tool.moduleCode} · {tool.method} {tool.path}</p>
            <small>{titleFrom(tool.status)} · {tool.durationMs} ms · {tool.diagnosticCode || 'No diagnostic code'}</small>
            <ul>{asArray(tool.evidenceSummary).map((value, index) => <li key={`${tool.toolCode}-${index}`}>{value}</li>)}</ul>
          </article>
        ))}
      </div>
    </details>
  );
}

function SystemAnswer({ result, close }) {
  const answer = result?.answer ?? {};
  /* GROUP_7_HELP_ANSWER_DETAIL_START */
  const detailLevel = result?.detailLevel ?? 'comprehensive';
  /* GROUP_7_HELP_ANSWER_DETAIL_END */
  return (
    <div className="help-detailed-answer pulse-ai-system-answer" data-answer-detail={detailLevel}>
      <div className="help-answer-heading">
        <span>Celar AI comprehensive system answer</span>
        <strong>{answer.directConclusion || 'Celar AI completed the request.'}</strong>
      </div>
      {answer.executiveSummary ? <p className="help-answer-summary">{answer.executiveSummary}</p> : null}
      <EvidenceBadges result={result} />
      <div className="help-answer-preference-evidence" role="note">
        <span>Answer detail: {titleFrom(detailLevel)}</span>
        <span>Source: saved profile, per-question command, or comprehensive system default</span>
      </div>
      <AnswerList heading="Scope and filters" values={answer.scopeAndFilters} open />
      <AnswerList heading="Current state" values={answer.currentState} open />
      <AnswerList heading="Detailed analysis" values={answer.detailedAnalysis} open />
      <AnswerList heading="API findings" values={answer.apiFindings} open={result?.intentCode === 'api_inventory'} />
      <AnswerList heading="Troubleshooting findings" values={answer.troubleshootingFindings} open={result?.intentCode === 'troubleshooting'} />
      <AnswerList heading="Root-cause hypotheses" values={answer.rootCauseHypotheses} open={result?.intentCode === 'troubleshooting'} />
      <AnswerList heading="Diagnostic steps" values={answer.diagnosticSteps} open={result?.intentCode === 'troubleshooting'} ordered />
      <AnswerList heading="Source evidence" values={answer.sourceEvidence} />
      <AnswerList heading="Known, unknown, stale, unavailable, and unauthorized values" values={answer.knownUnknownAndStaleValues} />
      <AnswerList heading="Assumptions" values={answer.assumptions} />
      <AnswerList heading="Conflicts" values={answer.conflicts} />
      <AnswerList heading="Limitations" values={answer.limitations} />
      <AnswerList heading="Risks and implications" values={answer.risksAndImplications} />
      <AnswerList heading="Recommended actions" values={answer.recommendedActions} open ordered />
      <EnhancementBlueprint blueprint={answer.futureEnhancementBlueprint} />
      <ApiInventory apis={result?.relevantApis} />
      <ToolEvidence tools={result?.toolResults} />
      <SourceEvidence sources={result?.sources} />
      <AnswerList heading="Warnings" values={result?.warnings} />
      <div className="pulse-ai-system-answer-footer">
        <span>Correlation: <code>{result?.correlationId || 'Not recorded'}</code></span>
        <span>Model: {result?.modelName || 'Deterministic private system synthesis'}</span>
        <span>{answer.confidenceExplanation}</span>
      </div>
      <NavigationTargets targets={answer.navigationTargets} close={close} />
    </div>
  );
}

function LegacyPlanAnswer({ payload, close }) {
  const plan = rebrandCelarValue(payload?.plan ?? {});
  const direct = plan.directKnowledgeAnswer;
  if (direct) {
    return (
      <div className="help-detailed-answer is-fallback">
        <div className="help-answer-heading"><span>Pulse operating guidance</span><strong>{direct.title}</strong></div>
        <p className="help-answer-summary">{direct.summary}</p>
        <AnswerList heading="Detailed procedure" values={direct.detailedSteps} open />
        <AnswerList heading="Important rules" values={direct.importantRules} open />
        <NavigationTargets targets={direct.navigationTargets} close={close} />
      </div>
    );
  }
  return (
    <div className="help-detailed-answer is-fallback">
      <div className="help-answer-heading"><span>Limited compatibility response</span><strong>System intelligence is not active in this runtime</strong></div>
      <p className="help-answer-summary">The detailed system-intelligence API could not be reached. Automatic multi-tool execution is not yet enabled for this compatibility response, so Pulse prepared a read-only evidence plan and did not invent live values.</p>
      <AnswerList heading="Required evidence" values={plan.requiredEvidence} />
      <AnswerList heading="Execution sequence" values={plan.executionSteps} />
      <AnswerList heading="Missing inputs" values={plan.missingInputs} />
      <NavigationTargets targets={['#celar-ai', '#service-control', '#user-guide']} close={close} />
    </div>
  );
}

function AssistantMessage({ message, close }) {
  if (message.loading) {
    return <div className="help-message assistant help-message-loading">Retrieving authorized evidence and building a comprehensive answer…</div>;
  }
  if (message.payload?.answer) {
    return <div className="help-message assistant is-detailed"><SystemAnswer result={message.payload} close={close} /></div>;
  }
  if (message.legacyPayload) {
    return <div className="help-message assistant is-detailed"><LegacyPlanAnswer payload={message.legacyPayload} close={close} /></div>;
  }
  if (message.error) {
    return (
      <div className="help-message assistant is-detailed">
        <div className="help-detailed-answer is-fallback">
          <div className="help-answer-heading"><span>Celar AI request did not complete</span><strong>{message.error}</strong></div>
          <p className="help-answer-summary">The completed conversation remains visible. Retry the question or use Modules 013, 016, 076, 078, and 998 with the displayed correlation evidence.</p>
          <NavigationTargets targets={['#service-control', '#backup-retention', '#defect-tracker', '#observability-slo-health', '#system-diagnostics']} close={close} />
        </div>
      </div>
    );
  }
  return <div className="help-message assistant">{message.text}</div>;
}

function serverMessageToUi(message) {
  if (message.role === 'user') {
    return { id: message.messageId, role: 'user', text: message.text, createdAt: message.createdAt };
  }
  const structured = message.structuredResponse && typeof message.structuredResponse === 'object'
    ? rebrandCelarValue(message.structuredResponse)
    : null;
  return {
    id: message.messageId,
    role: 'assistant',
    payload: structured?.answer ? structured : null,
    text: rebrandCelarString(message.text),
    error: message.status === 'failed' && !structured?.answer ? rebrandCelarString(message.text) : '',
    createdAt: message.createdAt
  };
}

export default function HelpAssistant() {
  const [isOpen, setIsOpen] = useState(false);
  const [question, setQuestion] = useState('');
  const [messages, setMessages] = useState([WELCOME_MESSAGE]);
  const [conversations, setConversations] = useState([]);
  const [activeConversationId, setActiveConversationId] = useState('');
  const [hydrated, setHydrated] = useState(false);
  const [historyLoading, setHistoryLoading] = useState(false);
  const [sending, setSending] = useState(false);
  const inputRef = useRef(null);
  const messagesRef = useRef(null);
  const followLatestRef = useRef(true);
  const sendingRef = useRef(false);

  const activeConversation = useMemo(
    () => conversations.find((item) => item.conversationId === activeConversationId) ?? null,
    [activeConversationId, conversations]
  );

  async function refreshConversationList(selectId = '') {
    const payload = await getJson('/api/pulse-ai/v1/system/conversations?limit=100');
    const rows = asArray(payload.conversations).map((conversation) => ({
      ...conversation,
      title: rebrandCelarString(conversation.title)
    }));
    setConversations(rows);
    return selectId || activeConversationId || rows[0]?.conversationId || '';
  }

  async function loadConversation(conversationId) {
    if (!conversationId) {
      setMessages([WELCOME_MESSAGE]);
      return;
    }
    setHistoryLoading(true);
    try {
      const payload = await getJson(`/api/pulse-ai/v1/system/conversations/${encodeURIComponent(conversationId)}`);
      const rows = asArray(payload?.conversation?.messages).map(serverMessageToUi);
      setMessages(rows.length ? rows : [WELCOME_MESSAGE]);
      setActiveConversationId(conversationId);
      followLatestRef.current = true;
    } finally {
      setHistoryLoading(false);
    }
  }

  async function createConversation(mode = 'system_help') {
    const payload = await postJson('/api/pulse-ai/v1/system/conversations', {
      title: 'New Celar AI conversation',
      mode,
      scope: { source: 'global_help_chat' }
    });
    const conversation = payload.conversation;
    if (!conversation?.conversationId) throw new Error('Celar AI did not return a conversation identifier.');
    setActiveConversationId(conversation.conversationId);
    setMessages([WELCOME_MESSAGE]);
    await refreshConversationList(conversation.conversationId);
    return conversation.conversationId;
  }

  async function hydrate() {
    if (hydrated) return;
    setHistoryLoading(true);
    try {
      const selected = await refreshConversationList();
      if (selected) {
        await loadConversation(selected);
      } else {
        await createConversation();
      }
    } catch {
      setMessages([WELCOME_MESSAGE]);
    } finally {
      setHydrated(true);
      setHistoryLoading(false);
    }
  }

  useEffect(() => {
    if (!isOpen) return;
    void hydrate();
    window.setTimeout(() => inputRef.current?.focus(), 40);
  }, [isOpen]);

  useEffect(() => {
    if (!isOpen || !followLatestRef.current) return;
    const viewport = messagesRef.current;
    if (!viewport) return;
    window.requestAnimationFrame(() => {
      viewport.scrollTop = viewport.scrollHeight;
    });
  }, [messages, isOpen]);

  function onConversationScroll(event) {
    const element = event.currentTarget;
    followLatestRef.current = element.scrollHeight - element.scrollTop - element.clientHeight < 96;
  }

  async function submitQuestion(event) {
    event?.preventDefault?.();
    if (sendingRef.current) return;
    const clean = question.trim();
    if (!clean) return;
    sendingRef.current = true;
    setSending(true);
    setQuestion('');
    followLatestRef.current = true;
    const localUserId = `user-${Date.now()}`;
    const loadingId = `loading-${Date.now()}`;
    setMessages((current) => [
      ...current,
      { id: localUserId, role: 'user', text: clean },
      { id: loadingId, role: 'assistant', loading: true }
    ]);

    try {
      let conversationId = activeConversationId;
      if (!conversationId) {
        try {
          conversationId = await createConversation();
        } catch {
          conversationId = '';
        }
      }
      const path = '/api/celar-ai/v1/chat';
      const payload = await postJson(path, {
        conversationId: conversationId || null,
        question: clean,
        mode: 'system_help',
        detailLevel: 'comprehensive',
        includeApiInventory: true,
        includeTroubleshooting: true,
        includeFutureEnhancement: true,
        includeAuthorizedProjectDocuments: true,
        usePrivateModelWhenAvailable: true
      });
      const result = rebrandCelarValue(payload.result);
      setMessages((current) => current.map((message) =>
        message.id === loadingId
          ? { id: result?.assistantMessageId || loadingId, role: 'assistant', payload: result, text: result?.answer?.directConclusion || '' }
          : message
      ));
      const returnedConversationId = result?.conversationId || conversationId;
      if (returnedConversationId) setActiveConversationId(returnedConversationId);
      try {
        await refreshConversationList(returnedConversationId);
      } catch {
        // The completed answer remains in memory even if the history refresh is temporarily unavailable.
      }
    } catch (error) {
      try {
        const legacyPayload = await loadLegacyPlan(clean);
        setMessages((current) => current.map((message) =>
          message.id === loadingId
            ? { id: loadingId, role: 'assistant', legacyPayload }
            : message
        ));
      } catch {
        setMessages((current) => current.map((message) =>
          message.id === loadingId
            ? { id: loadingId, role: 'assistant', error: error instanceof Error ? error.message : 'Celar AI could not complete this question.' }
            : message
        ));
      }
    } finally {
      sendingRef.current = false;
      setSending(false);
      window.setTimeout(() => inputRef.current?.focus(), 40);
    }
  }

  function onInputKeyDown(event) {
    if (event.nativeEvent?.isComposing || event.isComposing) return;
    if (event.key === 'Escape') {
      event.preventDefault();
      setIsOpen(false);
      return;
    }
    if (event.key !== 'Enter' || event.shiftKey) return;
    if (event.defaultPrevented) return;
    event.preventDefault();
    event.currentTarget.form?.requestSubmit();
  }

  async function selectConversation(event) {
    const id = event.target.value;
    setActiveConversationId(id);
    try {
      await loadConversation(id);
    } catch (error) {
      setMessages([{ id: 'history-error', role: 'assistant', error: error instanceof Error ? error.message : 'Conversation history could not be loaded.' }]);
    }
  }

  function openRoute(route) {
    setIsOpen(false);
    window.location.hash = route;
  }

  function openDefectTracker() {
    setIsOpen(false);
    const destination = new URL(window.location.href);
    destination.searchParams.set('defectSource', 'help');
    destination.hash = 'defect-tracker';
    window.location.assign(destination.toString());
  }

  return (
    <>
      <button type="button" className="help-launcher" onClick={() => setIsOpen((current) => !current)}>
        Ask Celar AI
      </button>
      {isOpen ? (
        <aside className="help-panel pulse-ai-help-panel pulse-ai-system-chat" aria-label="Celar AI system intelligence assistant">
          <div className="help-header">
            <div>
              <strong>Celar AI Help & Search</strong>
              <span>Detailed answers · live APIs · troubleshooting · future enhancements</span>
            </div>
            <button type="button" aria-label="Close Celar AI" onClick={() => setIsOpen(false)}>×</button>
          </div>

          <div className="pulse-ai-conversation-toolbar">
            <label>
              Conversation
              <select value={activeConversationId} onChange={selectConversation} disabled={historyLoading}>
                {!activeConversationId ? <option value="">Current session</option> : null}
                {conversations.map((conversation) => (
                  <option key={conversation.conversationId} value={conversation.conversationId}>
                    {conversation.title} · {conversation.messageCount} messages
                  </option>
                ))}
              </select>
            </label>
            <button type="button" onClick={() => void createConversation()} disabled={historyLoading || sending}>New conversation</button>
            <span>{activeConversation ? `Updated ${formatDate(activeConversation.updatedAt)}` : 'Server history loads when available'}</span>
          </div>

          <div className="help-quick-actions">
            <button type="button" className="help-full-guide-button" onClick={() => openRoute('user-guide')}>Module 999 — System User Guide</button>
            <button type="button" className="help-pulse-ai-button" onClick={() => openRoute('celar-ai')}>Celar AI Workbench</button>
            <button type="button" className="help-report-defect-button" onClick={openDefectTracker}>Report a defect — Module 076</button>
          </div>

          {/* GROUP_7_HELP_GOVERNANCE_PANEL_START */}
          <HelpGovernancePanel />
          {/* GROUP_7_HELP_GOVERNANCE_PANEL_END */}

          <div
            ref={messagesRef}
            className="help-messages"
            role="log"
            aria-live="polite"
            aria-relevant="additions text"
            tabIndex={0}
            onScroll={onConversationScroll}
          >
            {historyLoading && messages.length === 1 ? <div className="help-message assistant help-message-loading">Loading durable conversation history…</div> : null}
            {messages.map((message) => message.role === 'user'
              ? <div key={message.id} className="help-message user">{message.text}</div>
              : <AssistantMessage key={message.id} message={message} close={() => setIsOpen(false)} />)}
          </div>

          <div className="help-suggestions" aria-label="Suggested Celar AI questions">
            {QUICK_QUESTIONS.map((suggestion) => (
              <button type="button" key={suggestion} onClick={() => { setQuestion(suggestion); inputRef.current?.focus(); }}>
                {suggestion}
              </button>
            ))}
          </div>

          <form className="help-input-row" onSubmit={submitQuestion}>
            <textarea
              ref={inputRef}
              value={question}
              onChange={(event) => setQuestion(event.target.value)}
              onKeyDown={onInputKeyDown}
              placeholder="Ask about any module, API, error, report, financial, document, workflow, architecture, or future enhancement…"
              rows={3}
              aria-label="Ask Celar AI"
              aria-keyshortcuts="Enter Shift+Enter Escape"
              disabled={sending}
            />
            <button type="submit" disabled={sending || !question.trim()}>{sending ? 'Working…' : 'Ask'}</button>
            <span className="pulse-ai-help-keyboard-hint">Enter sends · Shift+Enter adds a line · Escape closes · completed responses remain in conversation history</span>
          </form>
        </aside>
      ) : null}
    </>
  );
}
