import { useEffect, useMemo, useRef, useState } from 'react';
import './pulse-ai-system-intelligence-workbench.css';

const TABS = Object.freeze([
  ['overview', 'System Intelligence', 'Readiness, capabilities, privacy, and operating boundary'],
  ['ask', 'Ask the System', 'Detailed answers using authorized live evidence'],
  ['apis', 'Running APIs', 'Registered routes, methods, ownership, and safe retests'],
  ['troubleshoot', 'Troubleshooting', 'Root-cause evidence and diagnostic sequence'],
  ['enhance', 'Future Enhancements', 'Architecture-aware implementation blueprints'],
  ['history', 'Conversations', 'Durable questions and responses']
]);

function asArray(value) { return Array.isArray(value) ? value : []; }
function rebrandCelarString(value) { return String(value ?? '').replaceAll('CELAR AI', 'CELAR AI').replaceAll('Celar AI', 'Celar AI'); }
function rebrandCelarValue(value) {
  if (typeof value === 'string') return rebrandCelarString(value);
  if (Array.isArray(value)) return value.map(rebrandCelarValue);
  if (!value || typeof value !== 'object') return value;
  return Object.fromEntries(Object.entries(value).map(([key, item]) => [key, rebrandCelarValue(item)]));
}

function title(value) { return String(value ?? '').replaceAll('_', ' ').replaceAll('-', ' ').replace(/\b\w/g, (letter) => letter.toUpperCase()); }
function formatDate(value) { const parsed = value ? new Date(value) : null; return parsed && !Number.isNaN(parsed.getTime()) ? parsed.toLocaleString() : 'Not recorded'; }
function formatPercent(value) { const number = Number(value); return Number.isFinite(number) ? `${Math.round(number * 100)}%` : 'Not recorded'; }

async function readJson(response) {
  const payload = await response.json().catch(() => ({}));
  if (!response.ok) throw new Error(payload.message || payload?.result?.answer?.directConclusion || `HTTP ${response.status}`);
  return payload;
}
async function getJson(path) { return readJson(await fetch(path, { method: 'GET', cache: 'no-store', headers: { Accept: 'application/json' } })); }
async function postJson(path, body) { return readJson(await fetch(path, { method: 'POST', cache: 'no-store', headers: { Accept: 'application/json', 'Content-Type': 'application/json' }, body: JSON.stringify(body ?? {}) })); }

function ListSection({ heading, values, open = false, ordered = false }) {
  const rows = [...new Set(asArray(values).filter(Boolean))];
  if (!rows.length) return null;
  const List = ordered ? 'ol' : 'ul';
  return (
    <details className="pulse-ai-system-workbench-section" open={open}>
      <summary><span>{heading}</span><small>{rows.length}</small></summary>
      <List>{rows.map((row, index) => <li key={`${heading}-${index}`}>{String(row)}</li>)}</List>
    </details>
  );
}

function Blueprint({ blueprint }) {
  if (!blueprint) return null;
  return (
    <section className="pulse-ai-system-workbench-card is-blueprint">
      <h4>Future enhancement blueprint</h4>
      <h5>{blueprint.requestedCapability}</h5>
      <p>{blueprint.businessOutcome}</p>
      <ListSection heading="Affected modules" values={blueprint.affectedModules} open />
      <ListSection heading="Current capabilities" values={blueprint.currentCapabilities} open />
      <ListSection heading="Gaps" values={blueprint.gaps} open />
      <ListSection heading="Proposed architecture" values={blueprint.proposedArchitecture} open />
      <ListSection heading="Proposed APIs" values={blueprint.proposedApis} />
      <ListSection heading="Data and migration" values={blueprint.dataAndMigrationConsiderations} />
      <ListSection heading="Security and privacy" values={blueprint.securityAndPrivacyControls} open />
      <ListSection heading="Operations and support" values={blueprint.operationalAndSupportControls} />
      <ListSection heading="Implementation phases" values={blueprint.implementationPhases} open ordered />
      <ListSection heading="Test strategy" values={blueprint.testStrategy} />
      <ListSection heading="Rollout and rollback" values={blueprint.rolloutAndRollback} />
      <ListSection heading="Risks" values={blueprint.risks} />
      <ListSection heading="Acceptance criteria" values={blueprint.acceptanceCriteria} open />
      <ListSection heading="Dependencies" values={blueprint.dependencies} />
    </section>
  );
}

function AnswerView({ result }) {
  const answer = result?.answer;
  if (!answer) return null;
  return (
    <div className="pulse-ai-system-workbench-answer">
      <section className="pulse-ai-system-workbench-answer-hero">
        <div><span>Direct conclusion</span><h4>{answer.directConclusion}</h4><p>{answer.executiveSummary}</p></div>
        <dl>
          <div><dt>Status</dt><dd>{title(result.status)}</dd></div>
          <div><dt>Intent</dt><dd>{title(result.intentCode)}</dd></div>
          <div><dt>Confidence</dt><dd>{formatPercent(answer.confidence)}</dd></div>
          <div><dt>Data as of</dt><dd>{formatDate(answer.dataAsOf)}</dd></div>
          <div><dt>APIs</dt><dd>{asArray(result.relevantApis).length}</dd></div>
          <div><dt>Tools</dt><dd>{asArray(result.toolResults).length}</dd></div>
          <div><dt>Saved</dt><dd>{result.persisted ? 'Yes' : 'No'}</dd></div>
          <div><dt>Correlation</dt><dd><code>{result.correlationId}</code></dd></div>
        </dl>
      </section>
      <ListSection heading="Scope and filters" values={answer.scopeAndFilters} open />
      <ListSection heading="Current state" values={answer.currentState} open />
      <ListSection heading="Detailed analysis" values={answer.detailedAnalysis} open />
      <ListSection heading="API findings" values={answer.apiFindings} open={result.intentCode === 'api_inventory'} />
      <ListSection heading="Troubleshooting findings" values={answer.troubleshootingFindings} open={result.intentCode === 'troubleshooting'} />
      <ListSection heading="Root-cause hypotheses" values={answer.rootCauseHypotheses} open={result.intentCode === 'troubleshooting'} />
      <ListSection heading="Diagnostic steps" values={answer.diagnosticSteps} open={result.intentCode === 'troubleshooting'} ordered />
      <ListSection heading="Source evidence" values={answer.sourceEvidence} />
      <ListSection heading="Known, unknown, stale, unavailable, and unauthorized" values={answer.knownUnknownAndStaleValues} />
      <ListSection heading="Assumptions" values={answer.assumptions} />
      <ListSection heading="Conflicts" values={answer.conflicts} />
      <ListSection heading="Limitations" values={answer.limitations} />
      <ListSection heading="Risks and implications" values={answer.risksAndImplications} />
      <ListSection heading="Recommended actions" values={answer.recommendedActions} open ordered />
      <Blueprint blueprint={answer.futureEnhancementBlueprint} />
      <ListSection heading="Warnings" values={result.warnings} />
    </div>
  );
}

function ApiTable({ apis, onRetest }) {
  const rows = asArray(apis);
  if (!rows.length) return <p className="pulse-ai-system-workbench-empty">No registered API matched the current filters.</p>;
  return (
    <div className="pulse-ai-system-workbench-table-wrap">
      <table>
        <thead><tr><th>Method</th><th>Route</th><th>Module</th><th>Purpose</th><th>Session</th><th>Safe retest</th><th /></tr></thead>
        <tbody>
          {rows.map((api) => (
            <tr key={`${api.apiId}-${api.method}`}>
              <td><code>{api.method}</code></td>
              <td><code>{api.routePattern}</code></td>
              <td>{api.moduleCode} — {api.moduleName}</td>
              <td>{api.purpose}</td>
              <td>{api.requiresApplicationSession ? 'Required' : 'Public/anonymous'}</td>
              <td>{api.safeRetestSupported ? 'Supported' : api.safeRetestReason}</td>
              <td>{api.safeRetestSupported ? <button type="button" onClick={() => onRetest(api)}>Retest</button> : null}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

export default function PulseAiSystemIntelligenceWorkbench() {
  const [tab, setTab] = useState('overview');
  const [readiness, setReadiness] = useState(null);
  const [question, setQuestion] = useState('');
  const [result, setResult] = useState(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [apiFilters, setApiFilters] = useState({ search: '', module: '', method: '', safeRetest: '', limit: '500' });
  const [apiPayload, setApiPayload] = useState(null);
  const [retestResult, setRetestResult] = useState(null);
  const [conversations, setConversations] = useState([]);
  const [conversationDetail, setConversationDetail] = useState(null);
  const inputRef = useRef(null);

  async function refreshReadiness() {
    setError('');
    try { setReadiness(await getJson('/api/pulse-ai/v1/system/readiness')); }
    catch (requestError) { setError(requestError instanceof Error ? requestError.message : 'Readiness could not be loaded.'); }
  }

  async function loadApis(event) {
    event?.preventDefault?.();
    setBusy(true); setError('');
    try {
      const url = new URL('/api/pulse-ai/v1/system/apis', window.location.origin);
      Object.entries(apiFilters).forEach(([key, value]) => { if (String(value).trim()) url.searchParams.set(key, String(value).trim()); });
      setApiPayload(await getJson(`${url.pathname}${url.search}`));
    } catch (requestError) { setError(requestError instanceof Error ? requestError.message : 'API inventory could not be loaded.'); }
    finally { setBusy(false); }
  }

  async function loadConversations() {
    try {
      const payload = await getJson('/api/pulse-ai/v1/system/conversations?limit=100');
      setConversations(asArray(payload.conversations));
    } catch {
      setConversations([]);
    }
  }

  useEffect(() => { void refreshReadiness(); void loadApis(); void loadConversations(); }, []);

  async function ask(event, mode = 'system_help') {
    event?.preventDefault?.();
    const clean = question.trim();
    if (!clean || busy) return;
    setBusy(true); setError(''); setResult(null);
    try {
      const payload = await postJson('/api/celar-ai/v1/chat', {
        question: clean,
        mode,
        detailLevel: 'comprehensive',
        includeApiInventory: true,
        includeTroubleshooting: true,
        includeFutureEnhancement: true,
        includeAuthorizedProjectDocuments: true,
        usePrivateModelWhenAvailable: true
      });
      setResult(rebrandCelarValue(payload.result));
      await loadConversations();
    } catch (requestError) { setError(requestError instanceof Error ? requestError.message : 'Celar AI could not complete the question.'); }
    finally { setBusy(false); }
  }

  function onQuestionKeyDown(event) {
    if (event.nativeEvent?.isComposing || event.isComposing) return;
    if (event.key === 'Enter' && !event.shiftKey) {
      event.preventDefault();
      event.currentTarget.form?.requestSubmit();
    }
  }

  async function retest(api) {
    const approved = window.confirm(`Run one safe read-only GET retest for ${api.method} ${api.routePattern}? No response body or state-changing action will be returned.`);
    if (!approved) return;
    setBusy(true); setError('');
    try {
      const payload = await postJson(`/api/pulse-ai/v1/system/apis/${encodeURIComponent(api.apiId)}/retest`, { confirmation: 'RETEST-PULSE-AI-SAFE-API' });
      setRetestResult(payload);
    } catch (requestError) { setError(requestError instanceof Error ? requestError.message : 'Safe API retest failed.'); }
    finally { setBusy(false); }
  }

  async function openConversation(id) {
    setBusy(true); setError('');
    try { setConversationDetail(rebrandCelarValue(await getJson(`/api/pulse-ai/v1/system/conversations/${encodeURIComponent(id)}`))); }
    catch (requestError) { setError(requestError instanceof Error ? requestError.message : 'Conversation could not be loaded.'); }
    finally { setBusy(false); }
  }

  const activeTab = TABS.find(([id]) => id === tab) ?? TABS[0];
  return (
    <section className="pulse-ai-system-workbench" data-pulse-ai-system-intelligence="v1">
      <header>
        <div><p>Module 011 · Celar AI</p><h2>System Intelligence, Live API Discovery & Troubleshooting</h2><span>Ask comprehensive questions about every authorized Pulse module, current APIs, runtime evidence, errors, architecture, and future enhancements.</span></div>
        <button type="button" onClick={() => void refreshReadiness()}>Refresh readiness</button>
      </header>
      <nav aria-label="Celar AI system intelligence workspaces">
        {TABS.map(([id, label, description]) => <button type="button" key={id} className={tab === id ? 'is-active' : ''} onClick={() => setTab(id)}><strong>{label}</strong><span>{description}</span></button>)}
      </nav>
      {error ? <div className="pulse-ai-system-workbench-error" role="alert">{error}</div> : null}
      <div className="pulse-ai-system-workbench-body">
        <div className="pulse-ai-system-workbench-title"><p>Celar AI workspace</p><h3>{activeTab[1]}</h3><span>{activeTab[2]}</span></div>

        {tab === 'overview' ? (
          <div className="pulse-ai-system-workbench-stack">
            <section className="pulse-ai-system-workbench-card">
              <h4>{title(readiness?.readiness?.status || 'Loading readiness')}</h4>
              <p>Celar AI discovers running APIs from the live ASP.NET endpoint registry, executes only source-controlled same-origin GET tools, preserves owning-module authorization, and saves completed conversations when migration 054 is available.</p>
              <dl className="pulse-ai-system-workbench-kpis">
                <div><dt>Live APIs</dt><dd>{readiness?.readiness?.liveApiCatalog?.summary?.total ?? '—'}</dd></div>
                <div><dt>API modules</dt><dd>{readiness?.readiness?.liveApiCatalog?.summary?.modules ?? '—'}</dd></div>
                <div><dt>Registered tools</dt><dd>{readiness?.readiness?.toolRegistry?.total ?? '—'}</dd></div>
                <div><dt>Authorized tools</dt><dd>{readiness?.readiness?.toolRegistry?.authorized ?? '—'}</dd></div>
                <div><dt>Durable conversations</dt><dd>{readiness?.readiness?.repository?.durableConversations ? 'Ready' : 'Migration required'}</dd></div>
                <div><dt>Private RAG</dt><dd>{title(readiness?.readiness?.privateRag?.status || 'not recorded')}</dd></div>
              </dl>
              <ListSection heading="Operating guarantees" values={readiness?.readiness?.guarantees} open />
            </section>
          </div>
        ) : null}

        {tab === 'ask' || tab === 'troubleshoot' || tab === 'enhance' ? (
          <div className="pulse-ai-system-workbench-stack">
            <form className="pulse-ai-system-workbench-question" onSubmit={(event) => ask(event, tab === 'troubleshoot' ? 'troubleshooting' : tab === 'enhance' ? 'future_enhancement' : 'system_help')}>
              <label>
                {tab === 'troubleshoot' ? 'Describe the issue, error, API, module, time, environment, and impact' : tab === 'enhance' ? 'Describe the future capability and business outcome' : 'Ask any question about Pulse'}
                <textarea ref={inputRef} rows={5} value={question} onChange={(event) => setQuestion(event.target.value)} onKeyDown={onQuestionKeyDown} placeholder="Press Enter to submit. Use Shift+Enter for a new line." />
              </label>
              <div><span>Enter sends · Shift+Enter adds a line · responses are saved when durable conversations are ready</span><button type="submit" disabled={busy || !question.trim()}>{busy ? 'Analyzing…' : 'Ask Celar AI'}</button></div>
            </form>
            <AnswerView result={result} />
          </div>
        ) : null}

        {tab === 'apis' ? (
          <div className="pulse-ai-system-workbench-stack">
            <form className="pulse-ai-system-workbench-api-filters" onSubmit={loadApis}>
              <label>Search<input value={apiFilters.search} onChange={(event) => setApiFilters((current) => ({ ...current, search: event.target.value }))} placeholder="route, module, purpose…" /></label>
              <label>Module<input value={apiFilters.module} onChange={(event) => setApiFilters((current) => ({ ...current, module: event.target.value }))} placeholder="013" /></label>
              <label>Method<select value={apiFilters.method} onChange={(event) => setApiFilters((current) => ({ ...current, method: event.target.value }))}><option value="">All</option><option>GET</option><option>POST</option><option>PUT</option><option>PATCH</option><option>DELETE</option></select></label>
              <label>Safe retest<select value={apiFilters.safeRetest} onChange={(event) => setApiFilters((current) => ({ ...current, safeRetest: event.target.value }))}><option value="">All</option><option value="true">Supported</option><option value="false">Not supported</option></select></label>
              <button type="submit" disabled={busy}>Load running APIs</button>
            </form>
            <section className="pulse-ai-system-workbench-card"><h4>Live registered API inventory</h4><p>The table is generated from the running application endpoint registry. Registration does not by itself prove dependency health.</p><ApiTable apis={apiPayload?.apis} onRetest={retest} /></section>
            {retestResult ? <pre className="pulse-ai-system-workbench-json">{JSON.stringify(retestResult, null, 2)}</pre> : null}
          </div>
        ) : null}

        {tab === 'history' ? (
          <div className="pulse-ai-system-workbench-history">
            <aside>{conversations.length ? conversations.map((conversation) => <button type="button" key={conversation.conversationId} onClick={() => void openConversation(conversation.conversationId)}><strong>{conversation.title}</strong><span>{conversation.messageCount} messages · {formatDate(conversation.updatedAt)}</span></button>) : <p>No durable conversation is available.</p>}</aside>
            <section>
              {conversationDetail?.conversation?.messages?.length ? conversationDetail.conversation.messages.map((message) => (
                <article key={message.messageId} className={`is-${message.role}`}><div><strong>{title(message.role)}</strong><span>{formatDate(message.createdAt)}</span></div>{message.role === 'assistant' && message.structuredResponse?.answer ? <AnswerView result={message.structuredResponse} /> : <p>{message.text}</p>}</article>
              )) : <p>Select a conversation to view every saved question and response.</p>}
            </section>
          </div>
        ) : null}
      </div>
    </section>
  );
}
