import { useEffect, useMemo, useRef, useState } from 'react';
import './help.css';
import './help-assistant.css';
import HelpGovernancePanel from './help/HelpGovernancePanel.jsx';
import { applyHelpAnswerPreferences } from './help/help-answer-preferences.js';
import './pulse-ai-system-chat.css';
import './celar-ai-contextual-chat.css';

const QUICK_QUESTIONS = Object.freeze([
  'What is my team working on right now, based on authorized Pulse records?',
  'How do I create a project in Pulse?',
  'How do I upload a SOW or GSD and make it available to Celar AI?',
  'What APIs are running on the system?',
  'Troubleshoot the current platform and show me the strongest evidence.',
  'Explain Celar AI and everything it can do.',
  'Design a future enhancement for Pulse using the current architecture.'
]);

const WELCOME_MESSAGE = Object.freeze({
  id: 'welcome',
  role: 'assistant',
  text: 'Ask me anything about Pulse or a general topic. Pulse questions use authorized platform evidence; public general-knowledge questions use the governed Celar AI, Claude, OpenAI, then local fallback order without sharing Pulse or private context. Previous conversations remain in History and are not automatically inserted into this conversation.'
});

const CELAR_AI_CHAT_SIZES = Object.freeze(['compact', 'standard', 'wide', 'fullscreen']);
const EMPTY_QUESTION_CONTEXT = Object.freeze({ projectCode: '', projectName: '', personOrTeam: '', dateFrom: '', dateTo: '' });

function initialChatSize() {
  return 'standard';
}

function asArray(value) {
  return Array.isArray(value) ? value : [];
}

function unique(values) {
  return [...new Set(asArray(values).filter((value) => value !== null && value !== undefined && String(value).trim()))];
}

function rebrandCelarString(value) {
  return String(value ?? '').replace(/\bPulse\s+AI\b/gi, 'Celar AI');
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

function projectOptionId(project) {
  return String(project?.id ?? project?.projectId ?? '');
}

function projectOptionCode(project) {
  return String(project?.projectCode ?? project?.code ?? '').trim();
}

function projectOptionName(project) {
  return String(project?.projectName ?? project?.name ?? '').trim();
}

function projectOptionLabel(project) {
  return [projectOptionCode(project), projectOptionName(project)].filter(Boolean).join(' — ');
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

async function deleteJson(path) {
  return readJson(await fetch(path, {
    method: 'DELETE',
    cache: 'no-store',
    headers: { Accept: 'application/json' }
  }));
}

function attachmentRows(payload) {
  return asArray(Array.isArray(payload) ? payload : payload?.attachments || payload?.items || payload?.result?.attachments);
}

function attachmentId(attachment) {
  return attachment?.attachmentId || attachment?.id || '';
}

function attachmentName(attachment) {
  return attachment?.fileName || attachment?.originalFileName || attachment?.name || 'Attached document';
}

function attachmentStatus(attachment) {
  return String(attachment?.status || attachment?.processingStatus || 'pending').toLowerCase();
}

function attachmentIsReady(attachment) {
  return ['ready', 'completed', 'indexed', 'available'].includes(attachmentStatus(attachment));
}

function attachmentIsProcessing(attachment) {
  return ['pending', 'uploaded', 'queued', 'scanning', 'extracting', 'processing', 'indexing'].includes(attachmentStatus(attachment));
}

function formatFileSize(value) {
  const bytes = Number(value);
  if (!Number.isFinite(bytes) || bytes < 0) return '';
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${Math.ceil(bytes / 1024)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

async function loadLegacyPlan(question) {
  const url = new URL('/api/celar-ai/v1/help-search/plan', window.location.origin);
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
  const apiRequested = result?.intentCode === 'api_inventory';
  return (
    <div className="pulse-ai-system-evidence-badges">
      <span>Status: {titleFrom(result?.status || 'unknown')}</span>
      <span>Intent: {titleFrom(result?.intentCode || 'general system')}</span>
      <span>Confidence: {formatPercent(answer.confidence)}</span>
      <span>Data as of: {formatDate(answer.dataAsOf)}</span>
      <span>Sources: {asArray(result?.sources).length}</span>
      {apiRequested ? <span>APIs: {asArray(result?.relevantApis).length}</span> : null}
      <span>Tools: {asArray(result?.toolResults).length}</span>
      <span>Saved: {result?.persisted ? 'Yes' : 'No'}</span>
    </div>
  );
}

function TrustSummary({ trust }) {
  if (!trust) return null;
  const reasons = asArray(trust.reasons);
  const confidence = Number.isFinite(Number(trust.confidence))
    ? `${Math.round(Number(trust.confidence) * 100)}%`
    : 'Not recorded';
  return (
    <div className={`celar-trust-banner is-${trust.classification || 'unknown'}`} role="status">
      <strong>{trust.label || titleFrom(trust.classification)}</strong>
      <span>{trust.questionAnswered ? 'Question answered' : 'Answer incomplete'}</span>
      <span>Confidence {confidence}</span>
      <span>{trust.successfulSourceCount || 0} successful source(s)</span>
      {trust.humanReviewRequired ? <span>Human review required</span> : null}
      {reasons.length ? <details><summary>Why this trust status</summary><ul>{reasons.map((reason, index) => <li key={index}>{reason}</li>)}</ul></details> : null}
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

function SourceEvidence({ sources, showTechnicalIdentifiers = false }) {
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
            <p>{showTechnicalIdentifiers
              ? `${source.moduleCode} · ${source.method} ${source.path}`
              : `Module ${source.moduleCode}`}</p>
            <small>{source.evidenceScope}</small>
            <small>Observed {formatDate(source.observedAt)} · {titleFrom(source.freshness)}</small>
          </article>
        ))}
      </div>
    </details>
  );
}

function ToolEvidence({ tools, showTechnicalIdentifiers = false }) {
  const rows = asArray(tools);
  if (!rows.length) return null;
  return (
    <details className="pulse-ai-system-tools">
      <summary>Governed tool execution <small>{rows.length}</small></summary>
      <div className="pulse-ai-system-tool-grid">
        {rows.map((tool) => (
          <article key={tool.toolCode} className={tool.status === 'succeeded' ? 'is-success' : 'is-warning'}>
            <div><strong>{tool.toolName}</strong><span>{showTechnicalIdentifiers ? `HTTP ${tool.statusCode || '—'}` : titleFrom(tool.status)}</span></div>
            <p>{showTechnicalIdentifiers
              ? `${tool.moduleCode} · ${tool.method} ${tool.path}`
              : `Module ${tool.moduleCode}`}</p>
            <small>{titleFrom(tool.status)} · {tool.durationMs} ms{showTechnicalIdentifiers ? ` · ${tool.diagnosticCode || 'No diagnostic code'}` : ''}</small>
            <ul>{asArray(tool.evidenceSummary).map((value, index) => <li key={`${tool.toolCode}-${index}`}>{value}</li>)}</ul>
          </article>
        ))}
      </div>
    </details>
  );
}

function SystemAnswer({ result, close }) {
  const answer = result?.answer ?? {};
  const externalUsedClosedTopic = asArray(result?.targetDecisions).some((decision) =>
    String(decision?.reasonCode || '').startsWith('generation_succeeded_with_sanitized_generic_problem'));
  /* GROUP_7_HELP_ANSWER_DETAIL_START */
  const detailLevel = result?.detailLevel ?? 'standard';
  /* GROUP_7_HELP_ANSWER_DETAIL_END */
  const detailedProfile = ['detailed', 'highly_detailed', 'technical', 'comprehensive', 'executive_and_detailed']
    .includes(detailLevel);
  const troubleshootingProfile = result?.intentCode === 'troubleshooting';
  const enhancementProfile = result?.intentCode === 'future_enhancement';
  const apiRequested = result?.intentCode === 'api_inventory';
  return (
    <div className="help-detailed-answer pulse-ai-system-answer" data-answer-detail={detailLevel}>
      <div className="help-answer-heading">
        <span>Celar AI answer</span>
        <strong>{answer.directConclusion || 'Celar AI completed the request.'}</strong>
      </div>
      {answer.executiveSummary ? <p className="help-answer-summary">{answer.executiveSummary}</p> : null}
      <TrustSummary trust={result?.trust} />
      <details className="celar-ai-answer-details">
        <summary><span>Detailed answer</span><small>Analysis, evidence, sources, and actions</small></summary>
        <div className="celar-ai-answer-details-body">
          <EvidenceBadges result={result} />
          <div className="help-answer-preference-evidence" role="note">
            <span>Answer detail: {titleFrom(detailLevel)}</span>
            <span>Source: saved profile, per-question command, or standard intent-aware default</span>
          </div>
          <AnswerList heading="Detailed analysis" values={answer.detailedAnalysis} open />
          <AnswerList heading="Scope and filters" values={answer.scopeAndFilters} />
          <AnswerList heading="Current state" values={answer.currentState} open={troubleshootingProfile} />
          {apiRequested ? <AnswerList heading="API findings" values={answer.apiFindings} /> : null}
          <AnswerList heading="Troubleshooting findings" values={answer.troubleshootingFindings} open={troubleshootingProfile} />
          <AnswerList heading="Root-cause hypotheses" values={answer.rootCauseHypotheses} open={troubleshootingProfile} />
          <AnswerList heading="Diagnostic steps" values={answer.diagnosticSteps} open={troubleshootingProfile} ordered />
          <AnswerList heading="Source evidence" values={answer.sourceEvidence} />
          <AnswerList heading="Known, unknown, stale, unavailable, and unauthorized values" values={answer.knownUnknownAndStaleValues} />
          <AnswerList heading="Assumptions" values={answer.assumptions} />
          <AnswerList heading="Conflicts" values={answer.conflicts} />
          <AnswerList heading="Limitations" values={answer.limitations} />
          <AnswerList heading="Risks and implications" values={answer.risksAndImplications} />
          <AnswerList heading="Recommended actions" values={answer.recommendedActions} open={troubleshootingProfile || enhancementProfile} ordered />
          <EnhancementBlueprint blueprint={answer.futureEnhancementBlueprint} />
          {apiRequested ? <ApiInventory apis={result?.relevantApis} /> : null}
          <ToolEvidence tools={result?.toolResults} showTechnicalIdentifiers={apiRequested} />
          <SourceEvidence sources={result?.sources} showTechnicalIdentifiers={apiRequested} />
          <AnswerList heading="Warnings" values={result?.warnings} />
          {result?.externalAssistance ? (
            <details className="pulse-ai-system-workbench-section">
              <summary><span>Supplementary external guidance (unverified)</span><small>{titleFrom(result?.modelProvider || 'external')}</small></summary>
              <p>{externalUsedClosedTopic
                ? 'The local Celar AI path could not complete this Pulse question. This optional guidance used only a closed server-owned topic plus a backend-owned purpose capsule; the user’s wording was not sent.'
                : 'The local Celar AI path could not complete the request. This optional guidance used only a backend-owned, identity-free purpose capsule.'}</p>
              <p>It is kept separate from the source-grounded answer above. No attachment text, private document content, tool results, customer or project context, people records, financial values, or identifiers were shared, so this guidance cannot establish enterprise-specific facts.</p>
              <p>{result.externalAssistance}</p>
            </details>
          ) : null}
          <div className="pulse-ai-system-answer-footer">
            <span>Correlation: <code>{result?.correlationId || 'Not recorded'}</code></span>
            <span>Selected route: {result?.modelName || titleFrom(result?.modelProvider || 'governed_local')}</span>
            <span>{answer.confidenceExplanation}</span>
          </div>
          <NavigationTargets targets={answer.navigationTargets} close={close} />
        </div>
      </details>
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
    return <div className="help-message assistant help-message-loading">Retrieving authorized evidence and preparing a direct answer…</div>;
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
  const [chatSize, setChatSize] = useState(initialChatSize);
  const [isMinimized, setIsMinimized] = useState(false);
  const [historyOpen, setHistoryOpen] = useState(false);
  const [contextOpen, setContextOpen] = useState(false);
  const [questionContext, setQuestionContext] = useState(EMPTY_QUESTION_CONTEXT);
  const [projectLookup, setProjectLookup] = useState('');
  const [projectOptions, setProjectOptions] = useState([]);
  const [projectOptionsLoaded, setProjectOptionsLoaded] = useState(false);
  const [projectOptionsLoading, setProjectOptionsLoading] = useState(false);
  const [projectOptionsError, setProjectOptionsError] = useState('');
  const [projectSuggestionsOpen, setProjectSuggestionsOpen] = useState(false);
  const [chatPosition, setChatPosition] = useState({ x: 0, y: 0 });
  const [attachments, setAttachments] = useState([]);
  const [selectedAttachmentIds, setSelectedAttachmentIds] = useState([]);
  const [attachmentBusy, setAttachmentBusy] = useState(false);
  const [attachmentError, setAttachmentError] = useState('');
  const [draggingFiles, setDraggingFiles] = useState(false);
  const inputRef = useRef(null);
  const fileInputRef = useRef(null);
  const messagesRef = useRef(null);
  const followLatestRef = useRef(true);
  const sendingRef = useRef(false);
  const chatDragRef = useRef(null);

  const activeConversation = useMemo(
    () => conversations.find((item) => item.conversationId === activeConversationId) ?? null,
    [activeConversationId, conversations]
  );

  async function refreshAttachments(conversationId = activeConversationId, autoSelectReady = true) {
    if (!conversationId) {
      setAttachments([]);
      setSelectedAttachmentIds([]);
      return [];
    }
    const payload = await getJson(`/api/celar-ai/v2/conversations/${encodeURIComponent(conversationId)}/attachments`);
    const rows = attachmentRows(payload);
    const previousReady = new Set((autoSelectReady ? attachments : rows).filter(attachmentIsReady).map(attachmentId));
    setAttachments(rows);
    setSelectedAttachmentIds((current) => {
      const ready = new Set(rows.filter(attachmentIsReady).map(attachmentId));
      const newlyReady = rows.filter((item) => attachmentIsReady(item) && !previousReady.has(attachmentId(item))).map(attachmentId);
      return [...new Set([...current.filter((id) => ready.has(id)), ...newlyReady])];
    });
    return rows;
  }

  async function refreshConversationList(selectId = '') {
    const payload = await getJson('/api/celar-ai/v1/system/conversations?limit=100');
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
    setAttachments([]);
    setSelectedAttachmentIds([]);
    setAttachmentError('');
    try {
      const payload = await getJson(`/api/celar-ai/v1/system/conversations/${encodeURIComponent(conversationId)}`);
      const rows = asArray(payload?.conversation?.messages).map(serverMessageToUi);
      setMessages(rows.length ? rows : [WELCOME_MESSAGE]);
      setActiveConversationId(conversationId);
      await refreshAttachments(conversationId, false).catch(() => []);
      followLatestRef.current = true;
    } finally {
      setHistoryLoading(false);
    }
  }

  async function createConversation(mode = 'system_help', resetMessages = true) {
    const payload = await postJson('/api/celar-ai/v1/system/conversations', {
      title: 'New Celar AI conversation',
      mode,
      scope: { source: 'global_help_chat' }
    });
    const conversation = payload.conversation;
    if (!conversation?.conversationId) throw new Error('Celar AI did not return a conversation identifier.');
    setActiveConversationId(conversation.conversationId);
    if (resetMessages) setMessages([WELCOME_MESSAGE]);
    await refreshConversationList(conversation.conversationId);
    return conversation.conversationId;
  }

  async function hydrate() {
    if (hydrated) return;
    setHistoryLoading(true);
    try {
      await refreshConversationList();
      setActiveConversationId('');
      setMessages([WELCOME_MESSAGE]);
      followLatestRef.current = true;
    } catch {
      setActiveConversationId('');
      setMessages([WELCOME_MESSAGE]);
    } finally {
      setHydrated(true);
      setHistoryLoading(false);
    }
  }

  function beginFreshConversation() {
    setActiveConversationId('');
    setMessages([WELCOME_MESSAGE]);
    setQuestion('');
    setQuestionContext(EMPTY_QUESTION_CONTEXT);
    setProjectLookup('');
    setProjectSuggestionsOpen(false);
    setAttachments([]);
    setSelectedAttachmentIds([]);
    setAttachmentError('');
    setHistoryOpen(false);
    setContextOpen(false);
    followLatestRef.current = true;
    window.setTimeout(() => inputRef.current?.focus(), 40);
  }

  useEffect(() => {
    if (!isOpen) return;
    void hydrate();
    window.setTimeout(() => inputRef.current?.focus(), 40);
  }, [isOpen]);

  useEffect(() => {
    if (!isOpen || !contextOpen || projectOptionsLoaded || projectOptionsLoading) return;
    let active = true;
    setProjectOptionsLoading(true);
    setProjectOptionsError('');
    void getJson('/api/project-workspace/overview')
      .then((payload) => {
        if (!active) return;
        const rows = asArray(payload?.projects)
          .filter((project) => projectOptionCode(project) || projectOptionName(project))
          .sort((left, right) => projectOptionLabel(left).localeCompare(projectOptionLabel(right)));
        setProjectOptions(rows);
        setProjectOptionsLoaded(true);
      })
      .catch((error) => {
        if (!active) return;
        setProjectOptions([]);
        setProjectOptionsLoaded(true);
        setProjectOptionsError(error instanceof Error
          ? error.message
          : 'Authorized project suggestions are temporarily unavailable.');
      })
      .finally(() => {
        if (active) setProjectOptionsLoading(false);
      });
    return () => { active = false; };
  }, [contextOpen, isOpen, projectOptionsLoaded, projectOptionsLoading]);

  useEffect(() => {
    if (!isOpen) return undefined;
    const keepChatVisible = () => {
      if (window.innerWidth <= 620) setChatPosition({ x: 0, y: 0 });
    };
    window.addEventListener('resize', keepChatVisible);
    return () => window.removeEventListener('resize', keepChatVisible);
  }, [isOpen]);

  useEffect(() => {
    const openChat = (event) => {
      setIsOpen(true);
      setIsMinimized(false);
      const suggestedQuestion = String(event?.detail?.question || '').trim();
      if (suggestedQuestion) setQuestion(suggestedQuestion);
      window.setTimeout(() => inputRef.current?.focus(), 40);
    };
    window.addEventListener('projectpulse:open-celar-ai-chat', openChat);
    return () => window.removeEventListener('projectpulse:open-celar-ai-chat', openChat);
  }, []);

  useEffect(() => {
    if (!isOpen || !activeConversationId || !attachments.some(attachmentIsProcessing)) return undefined;
    const timer = window.setInterval(() => {
      void refreshAttachments(activeConversationId).catch(() => undefined);
    }, 2500);
    return () => window.clearInterval(timer);
  }, [activeConversationId, attachments, isOpen]);

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

  const projectSuggestions = useMemo(() => {
    const query = projectLookup.trim().toLowerCase();
    const matches = query.length === 0
      ? projectOptions
      : projectOptions.filter((project) => projectOptionLabel(project).toLowerCase().includes(query));
    return matches.slice(0, 12);
  }, [projectLookup, projectOptions]);

  function updateProjectLookup(value) {
    setProjectLookup(value);
    setQuestionContext((current) => ({ ...current, projectCode: '', projectName: value.trim() }));
    setProjectSuggestionsOpen(true);
  }

  function selectProjectContext(project) {
    const code = projectOptionCode(project);
    const name = projectOptionName(project);
    setProjectLookup(projectOptionLabel(project));
    setQuestionContext((current) => ({ ...current, projectCode: code, projectName: name }));
    setProjectSuggestionsOpen(false);
  }

  function clearQuestionContext() {
    setQuestionContext(EMPTY_QUESTION_CONTEXT);
    setProjectLookup('');
    setProjectSuggestionsOpen(false);
  }

  function beginChatDrag(event) {
    if (event.button !== 0
      || chatSize === 'fullscreen'
      || isMinimized
      || event.target.closest('button, a, input, select, textarea, summary')) return;
    const panel = event.currentTarget.closest('#celar-ai-global-chat');
    if (!panel) return;
    const bounds = panel.getBoundingClientRect();
    chatDragRef.current = {
      pointerId: event.pointerId,
      startX: event.clientX,
      startY: event.clientY,
      originX: chatPosition.x,
      originY: chatPosition.y,
      bounds,
    };
    event.currentTarget.setPointerCapture?.(event.pointerId);
    event.preventDefault();
  }

  function moveChat(event) {
    const drag = chatDragRef.current;
    if (!drag || drag.pointerId !== event.pointerId) return;
    const margin = 8;
    const requestedX = drag.originX + event.clientX - drag.startX;
    const requestedY = drag.originY + event.clientY - drag.startY;
    const minimumX = drag.originX - drag.bounds.left + margin;
    const maximumX = drag.originX + window.innerWidth - drag.bounds.right - margin;
    const minimumY = drag.originY - drag.bounds.top + margin;
    const maximumY = drag.originY + window.innerHeight - drag.bounds.bottom - margin;
    setChatPosition({
      x: Math.min(Math.max(requestedX, minimumX), maximumX),
      y: Math.min(Math.max(requestedY, minimumY), maximumY),
    });
  }

  function endChatDrag(event) {
    if (chatDragRef.current?.pointerId !== event.pointerId) return;
    chatDragRef.current = null;
    event.currentTarget.releasePointerCapture?.(event.pointerId);
  }

  function selectChatSize(size) {
    if (!CELAR_AI_CHAT_SIZES.includes(size)) return;
    setChatSize(size);
    setIsMinimized(false);
    if (size === 'fullscreen') setChatPosition({ x: 0, y: 0 });
  }

  async function uploadAttachments(files) {
    const selectedFiles = [...(files || [])];
    if (!selectedFiles.length || attachmentBusy) return;
    setAttachmentBusy(true);
    setAttachmentError('');
    try {
      let conversationId = activeConversationId;
      if (!conversationId) conversationId = await createConversation('system_help', false);
      const body = new FormData();
      selectedFiles.forEach((file) => body.append('files', file, file.name));
      await readJson(await fetch(`/api/celar-ai/v2/conversations/${encodeURIComponent(conversationId)}/attachments`, {
        method: 'POST',
        cache: 'no-store',
        headers: { Accept: 'application/json' },
        body
      }));
      await refreshAttachments(conversationId);
    } catch (error) {
      setAttachmentError(error instanceof Error ? error.message : 'The documents could not be attached.');
    } finally {
      setAttachmentBusy(false);
      setDraggingFiles(false);
      if (fileInputRef.current) fileInputRef.current.value = '';
    }
  }

  async function removeAttachment(item) {
    const id = attachmentId(item);
    if (!activeConversationId || !id || attachmentBusy) return;
    setAttachmentBusy(true);
    setAttachmentError('');
    try {
      await deleteJson(`/api/celar-ai/v2/conversations/${encodeURIComponent(activeConversationId)}/attachments/${encodeURIComponent(id)}`);
      await refreshAttachments(activeConversationId);
    } catch (error) {
      setAttachmentError(error instanceof Error ? error.message : 'The attachment could not be removed.');
    } finally {
      setAttachmentBusy(false);
    }
  }

  function toggleAttachment(item) {
    const id = attachmentId(item);
    if (!id || !attachmentIsReady(item)) return;
    setSelectedAttachmentIds((current) => current.includes(id)
      ? current.filter((candidate) => candidate !== id)
      : [...current, id]);
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
          conversationId = await createConversation('system_help', false);
        } catch {
          conversationId = '';
        }
      }
      const path = '/api/celar-ai/v2/chat';
      const explicitContext = [
        questionContext.projectCode ? `Project code: ${questionContext.projectCode}` : '',
        questionContext.projectName ? `Project name: ${questionContext.projectName}` : '',
        questionContext.personOrTeam ? `Person or team: ${questionContext.personOrTeam}` : '',
        questionContext.dateFrom ? `Date from: ${questionContext.dateFrom}` : '',
        questionContext.dateTo ? `Date to: ${questionContext.dateTo}` : ''
      ].filter(Boolean);
      const questionWithContext = explicitContext.length
        ? `${clean}\n\nExplicit current-question context:\n- ${explicitContext.join('\n- ')}`
        : clean;
      const preferenceUrl = new URL(path, window.location.origin);
      const answerPreferences = applyHelpAnswerPreferences(preferenceUrl, clean);
      const readyAttachmentIds = new Set(attachments.filter(attachmentIsReady).map(attachmentId));
      const payload = await postJson(path, {
        conversationId: conversationId || null,
        question: questionWithContext,
        projectCode: questionContext.projectCode || null,
        projectName: questionContext.projectName || null,
        mode: 'system_help',
        detailLevel: answerPreferences.detailLevel,
        includeRepositoryContext: answerPreferences.includeRepositoryContext,
        includeAssumptions: answerPreferences.includeAssumptions,
        includeSourceCitations: answerPreferences.includeSourceCitations,
        answerPreferenceSource: answerPreferences.preferenceSource,
        includeApiInventory: true,
        includeTroubleshooting: true,
        includeFutureEnhancement: true,
        includeAuthorizedProjectDocuments: answerPreferences.includeRepositoryContext,
        usePrivateModelWhenAvailable: true,
        clientTimeZone: Intl.DateTimeFormat().resolvedOptions().timeZone,
        attachmentIds: selectedAttachmentIds.filter((id) => readyAttachmentIds.has(id))
      });
      const result = rebrandCelarValue(payload.result);
      if (result && payload.trust) result.trust = rebrandCelarValue(payload.trust);
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
      setMessages((current) => current.map((message) =>
        message.id === loadingId
          ? { id: loadingId, role: 'assistant', error: error instanceof Error ? error.message : 'Celar AI could not complete this question.' }
          : message
      ));
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
      if (!id) {
        beginFreshConversation();
        return;
      }
      await loadConversation(id);
      setHistoryOpen(false);
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
      <button type="button" className="help-launcher" aria-expanded={isOpen} aria-controls="celar-ai-global-chat" onClick={() => {
        setIsOpen((current) => !current);
        setIsMinimized(false);
      }}>
        Ask Celar AI
      </button>
      {isOpen ? (
        <aside
          id="celar-ai-global-chat"
          className={`help-panel pulse-ai-help-panel pulse-ai-system-chat celar-ai-contextual-chat is-size-${chatSize}${isMinimized ? ' is-minimized' : ''}`}
          aria-label="Celar AI system intelligence assistant"
          data-context-policy="current-conversation-only"
          data-history-policy="retained-not-auto-injected"
          data-project-context="authorized-typeahead"
          data-movable={chatSize !== 'fullscreen' && !isMinimized}
          style={{ '--celar-chat-x': `${chatPosition.x}px`, '--celar-chat-y': `${chatPosition.y}px` }}
        >
          <div
            className="help-header celar-ai-chat-drag-handle"
            onPointerDown={beginChatDrag}
            onPointerMove={moveChat}
            onPointerUp={endChatDrag}
            onPointerCancel={endChatDrag}
            onDoubleClick={() => setChatPosition({ x: 0, y: 0 })}
            title={chatSize === 'fullscreen' || isMinimized ? undefined : 'Drag to move Celar AI. Double-click to reset its position.'}
          >
            <div>
              <strong>Celar AI Help & Search</strong>
              <span>Platform guidance · authorized people/work answers · APIs · troubleshooting</span>
            </div>
            <div className="celar-ai-chat-window-controls" aria-label="Celar AI window controls">
              <button type="button" data-size="compact" className={chatSize === 'compact' ? 'is-active' : ''} aria-label="Compact chat" title="Compact" onClick={() => selectChatSize('compact')}>C</button>
              <button type="button" data-size="standard" className={chatSize === 'standard' ? 'is-active' : ''} aria-label="Standard chat" title="Standard" onClick={() => selectChatSize('standard')}>S</button>
              <button type="button" data-size="wide" className={chatSize === 'wide' ? 'is-active' : ''} aria-label="Wide chat" title="Wide" onClick={() => selectChatSize('wide')}>W</button>
              <button type="button" data-size="fullscreen" className={chatSize === 'fullscreen' ? 'is-active' : ''} aria-label="Fullscreen chat" title="Fullscreen" onClick={() => selectChatSize('fullscreen')}>□</button>
              <button type="button" aria-label={isMinimized ? 'Restore Celar AI' : 'Minimize Celar AI'} title={isMinimized ? 'Restore' : 'Minimize'} onClick={() => setIsMinimized((current) => !current)}>{isMinimized ? '▣' : '—'}</button>
              <button type="button" className="celar-ai-chat-close" aria-label="Close Celar AI" title="Close" onClick={() => setIsOpen(false)}>×</button>
            </div>
          </div>

          <div className="celar-ai-context-bar" role="note">
            <div>
              <strong>{activeConversationId ? 'Current conversation only' : 'Fresh chat — no previous conversation context'}</strong>
              <span>{activeConversationId ? 'This selected thread is retained for you. Other conversations are not merged into it.' : 'History remains available, but the most recent chat is not opened or injected automatically.'}</span>
            </div>
            <button type="button" onClick={() => setHistoryOpen((current) => !current)} aria-expanded={historyOpen}>
              {historyOpen ? 'Hide history' : `History (${conversations.length})`}
            </button>
          </div>

          <div className="celar-ai-question-context-toggle">
            <button type="button" onClick={() => setContextOpen((current) => !current)} aria-expanded={contextOpen}>
              {contextOpen ? 'Hide question context' : 'Add project, person/team, or date context'}
            </button>
            <span>Context applies only to the current question and selected thread. It is not copied from another conversation.</span>
          </div>

          <div className={`celar-ai-question-context${contextOpen ? ' is-open' : ''}`} aria-hidden={!contextOpen}>
            <label className="celar-ai-project-context-picker">
              Project
              <div className="celar-ai-project-combobox">
                <input
                  type="search"
                  role="combobox"
                  aria-autocomplete="list"
                  aria-expanded={projectSuggestionsOpen && projectSuggestions.length > 0}
                  aria-controls="celar-ai-project-suggestions"
                  autoComplete="off"
                  value={projectLookup}
                  onFocus={() => setProjectSuggestionsOpen(true)}
                  onBlur={() => window.setTimeout(() => setProjectSuggestionsOpen(false), 120)}
                  onChange={(event) => updateProjectLookup(event.target.value)}
                  placeholder={projectOptionsLoading ? 'Loading authorized projects…' : 'Type a project name or code'}
                />
                {projectSuggestionsOpen && projectSuggestions.length > 0 ? (
                  <div id="celar-ai-project-suggestions" className="celar-ai-project-suggestions" role="listbox">
                    {projectSuggestions.map((project) => (
                      <button
                        type="button"
                        role="option"
                        aria-selected={questionContext.projectCode === projectOptionCode(project)}
                        key={projectOptionId(project) || projectOptionLabel(project)}
                        onMouseDown={(event) => event.preventDefault()}
                        onClick={() => selectProjectContext(project)}
                      >
                        <strong>{projectOptionName(project) || projectOptionCode(project)}</strong>
                        <span>{[projectOptionCode(project), project?.clientName, project?.status].filter(Boolean).join(' · ')}</span>
                      </button>
                    ))}
                  </div>
                ) : null}
              </div>
              {questionContext.projectCode ? <small>Selected: {questionContext.projectCode} · {questionContext.projectName}</small> : null}
              {projectOptionsError ? <small className="celar-ai-project-context-error">Suggestions unavailable; you can still enter a project name manually.</small> : null}
            </label>
            <label>Person or team<input value={questionContext.personOrTeam} onChange={(event) => setQuestionContext((current) => ({ ...current, personOrTeam: event.target.value }))} placeholder="Authorized scope only" /></label>
            <label>Date from<input type="date" value={questionContext.dateFrom} onChange={(event) => setQuestionContext((current) => ({ ...current, dateFrom: event.target.value }))} /></label>
            <label>Date to<input type="date" value={questionContext.dateTo} onChange={(event) => setQuestionContext((current) => ({ ...current, dateTo: event.target.value }))} /></label>
            <button type="button" onClick={clearQuestionContext}>Clear context</button>
          </div>

          <div className={`pulse-ai-conversation-toolbar${historyOpen ? ' is-open' : ''}`}>
            <label>
              Conversation
              <select value={activeConversationId} onChange={selectConversation} disabled={historyLoading}>
                <option value="">Fresh chat — no previous context</option>
                {conversations.map((conversation) => (
                  <option key={conversation.conversationId} value={conversation.conversationId}>
                    {conversation.title} · {conversation.messageCount} messages
                  </option>
                ))}
              </select>
            </label>
            <button type="button" onClick={beginFreshConversation} disabled={historyLoading || sending}>New chat</button>
            <span>{activeConversation ? `Updated ${formatDate(activeConversation.updatedAt)}` : 'Server history loads when available'}</span>
          </div>

          <div className="help-quick-actions">
            <button type="button" className="help-full-guide-button" onClick={() => openRoute('user-guide')}>Module 999 — System User Guide</button>
            <button type="button" className="help-pulse-ai-button" onClick={() => openRoute('celar-ai')}>Celar AI Workbench</button>
            <button type="button" className="help-report-defect-button" onClick={openDefectTracker}>Report a defect — Module 076</button>
          </div>

          {/* GROUP_7_HELP_GOVERNANCE_PANEL_START */}
          <details className="celar-ai-chat-governance">
            <summary>Privacy, scope, and answer-detail controls</summary>
            <HelpGovernancePanel />
          </details>
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

          <section className="celar-ai-chat-attachments" aria-labelledby="celar-ai-chat-attachments-heading">
            <div className="celar-ai-chat-attachments-heading">
              <div><strong id="celar-ai-chat-attachments-heading">Documents for this conversation</strong><span>Files are privately scanned, extracted, and authorized before Celar AI can use them. Raw file contents are never stored in this browser.</span></div>
              <button type="button" onClick={() => fileInputRef.current?.click()} disabled={attachmentBusy}>{attachmentBusy ? 'Processing…' : 'Attach documents'}</button>
            </div>
            <input ref={fileInputRef} className="celar-ai-chat-file-input" type="file" multiple accept=".pdf,.docx,.pptx,.xlsx,.txt,.md,.csv,.json,.xml,.html,.htm" onChange={(event) => void uploadAttachments(event.target.files)} aria-label="Choose documents to attach to Celar AI" />
            <div
              className={`celar-ai-chat-dropzone${draggingFiles ? ' is-dragging' : ''}`}
              onDragEnter={(event) => { event.preventDefault(); setDraggingFiles(true); }}
              onDragOver={(event) => { event.preventDefault(); event.dataTransfer.dropEffect = 'copy'; }}
              onDragLeave={(event) => { event.preventDefault(); if (!event.currentTarget.contains(event.relatedTarget)) setDraggingFiles(false); }}
              onDrop={(event) => { event.preventDefault(); setDraggingFiles(false); void uploadAttachments(event.dataTransfer.files); }}
            >Drop approved documents here or use Attach documents.</div>
            {attachmentError ? <div className="celar-ai-chat-attachment-error" role="alert">{attachmentError}</div> : null}
            {attachments.length ? <ul className="celar-ai-chat-attachment-list">{attachments.map((item) => {
              const id = attachmentId(item);
              const ready = attachmentIsReady(item);
              return <li key={id || attachmentName(item)}>
                <label><input type="checkbox" checked={ready && selectedAttachmentIds.includes(id)} disabled={!ready || attachmentBusy} onChange={() => toggleAttachment(item)} /><span><strong>{attachmentName(item)}</strong><small>{[ready ? (selectedAttachmentIds.includes(id) ? 'Ready and selected for the next question' : 'Ready — not selected') : titleFrom(attachmentStatus(item)), formatFileSize(item.sizeBytes), item.diagnosticCode && item.diagnosticCode !== 'none' ? item.diagnosticCode : ''].filter(Boolean).join(' · ')}</small></span></label>
                <button type="button" onClick={() => void removeAttachment(item)} disabled={attachmentBusy} aria-label={`Remove ${attachmentName(item)}`}>Remove</button>
              </li>;
            })}</ul> : null}
          </section>

          <form className="help-input-row" onSubmit={submitQuestion}>
            <textarea
              ref={inputRef}
              value={question}
              onChange={(event) => setQuestion(event.target.value)}
              onKeyDown={onInputKeyDown}
              placeholder="Ask about Pulse or any general topic…"
              rows={3}
              aria-label="Ask Celar AI"
              aria-keyshortcuts="Enter Shift+Enter Escape"
              disabled={sending}
            />
            <button type="submit" disabled={sending || !question.trim()}>{sending ? 'Working…' : 'Ask'}</button>
            <span className="pulse-ai-help-keyboard-hint">Enter sends · Shift+Enter adds a line · Escape closes · completed responses remain in conversation history</span>
          </form>
          <span className="celar-ai-chat-resize-note" aria-hidden="true">Drag corner to resize</span>
        </aside>
      ) : null}
    </>
  );
}
