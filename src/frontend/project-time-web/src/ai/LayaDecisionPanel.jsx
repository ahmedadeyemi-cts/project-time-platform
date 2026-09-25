import { useEffect, useRef, useState } from 'react';
import './laya-decisions.css';
import { processingStageLabel, processingMessage, shouldPollProcessing } from './laya-processing-state.js';

const ROOT = '/api/ai-configuration/decisions/laya';
const LABELS = { sow: 'Statement of work', invoice: 'Invoice', purchase_order: 'Purchase order', other: 'Other' };
const MESSAGES = {
  decision_schema_not_installed: 'The Laya application migration has not been deployed.',
  decision_deployment_not_allowed: 'This capability is available only in the approved Test runtime.',
  decision_document_admission_required: 'This document must pass the existing malware, format and text-extraction checks first.',
  decision_input_exceeds_model_budget: 'The excerpt exceeds the model budget. No classification was accepted. Review the document manually.',
  decision_configuration_changed: 'The configuration changed during this request. Refresh and try again.',
  decision_capability_disabled: 'Document classification is disabled in Module 064.',
  decision_source_changed: 'The document changed. Select its current version and classify again.',
  decision_already_reviewed_or_source_changed: 'This recommendation was already reviewed or its source changed. Refresh the history.',
  decision_busy: 'Laya is processing another request. No fallback provider was called.',
  decision_timeout: 'The local classification deadline was reached. No fallback provider was called.',
  decision_unavailable: 'The local decision service is unavailable. Existing generation settings are unchanged.',
  document_not_found_or_not_authorized: 'The document is no longer available in your authorized scope.'
};

async function api(path, method, body, signal) {
  const response = await fetch(ROOT + path, {
    method, credentials: 'include', cache: 'no-store', signal,
    headers: body === undefined ? {} : { 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body)
  });
  const data = await response.json().catch(() => ({}));
  if (!response.ok) {
    const error = new Error(MESSAGES[data.status] || 'The request could not be completed. No automated workflow action was taken.');
    error.status = response.status;
    error.processing = data.processing;
    throw error;
  }
  return data;
}

export default function LayaDecisionPanel() {
  const [config, setConfig] = useState(null);
  const [hidden, setHidden] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');
  const [health, setHealth] = useState(null);
  const [documents, setDocuments] = useState([]);
  const [projectCode, setProjectCode] = useState('');
  const [documentId, setDocumentId] = useState('');
  const [history, setHistory] = useState([]);
  const [sourceHash, setSourceHash] = useState('');
  const [labels, setLabels] = useState({});
  const [processing, setProcessing] = useState(null);
  const [processingError, setProcessingError] = useState('');
  const [authorityEpoch, setAuthorityEpoch] = useState(0);
  const pending = useRef(null);
  const alive = useRef(false);
  const locked = useRef(false);

  async function run(work) {
    if (locked.current) return;
    locked.current = true;
    const controller = new AbortController();
    pending.current = controller;
    const timeout = setTimeout(() => controller.abort(), 90000);
    setBusy(true); setError(''); setNotice('');
    try { await work(controller.signal); }
    catch (e) {
      if (alive.current) {
        if (e.status === 401 || e.status === 403) setHidden(true);
        else setError(e.name === 'AbortError' ? 'The request ended before completion. Refresh the history before retrying.' : e.message);
        if (e.processing) setProcessing(e.processing);
        setHealth(null);
      }
    } finally {
      clearTimeout(timeout);
      if (alive.current) setBusy(false);
      locked.current = false;
    }
  }

  useEffect(() => {
    const events = ['projectpulse:view-as-changed', 'projectpulse:auth-session-changed', 'projectpulse:auth-session-cleared'];
    const reset = () => {
      pending.current?.abort(); setConfig(null); setDocuments([]); setDocumentId('');
      setHistory([]); setHealth(null); setSourceHash(''); setProcessing(null);
      setError(''); setNotice(''); setHidden(false); setAuthorityEpoch(value => value + 1);
    };
    const storageChanged = event => {
      if (event.key === null || ['projectPulseAuthSession','projectPulseViewAsUser'].includes(event.key)) reset();
    };
    events.forEach(event => window.addEventListener(event, reset));
    window.addEventListener('storage', storageChanged);
    return () => { events.forEach(event => window.removeEventListener(event, reset)); window.removeEventListener('storage', storageChanged); };
  }, []);

  useEffect(() => {
    alive.current = true;
    const controller = new AbortController();
    api('', 'GET', undefined, controller.signal).then(data => {
      if (alive.current && !controller.signal.aborted) setConfig(data);
    }).catch(e => {
      if (alive.current && e.name !== 'AbortError') {
        if (e.status === 401 || e.status === 403) setHidden(true);
        else setError(e.message);
      }
    });
    return () => { alive.current = false; controller.abort(); pending.current?.abort(); };
  }, [authorityEpoch]);

  useEffect(() => {
    setProcessing(null); setProcessingError('');
    if (!documentId || hidden) return undefined;
    const controller = new AbortController();
    let timer;
    let running = false;
    let activeRequest;
    let attempts = 0;
    let complete = false;
    async function refresh() {
      if (controller.signal.aborted || document.hidden || running || complete) return;
      running = true;
      activeRequest = new AbortController();
      const requestDeadline = setTimeout(() => activeRequest?.abort(), 20000);
      try {
        const data = await api(`/documents/${encodeURIComponent(documentId)}/processing-state`, 'GET', undefined, activeRequest.signal);
        if (controller.signal.aborted) return;
        setProcessing(data); setProcessingError('');
        complete = !shouldPollProcessing(data);
        if (!complete && ++attempts < 120) timer = setTimeout(refresh, 5000);
        else if (!complete) setProcessingError('Automatic refresh paused. Reselect this document to check again; background processing is unaffected.');
      } catch (e) {
        if (!controller.signal.aborted) {
          setProcessing(null); setProcessingError(e.name === 'AbortError' ? 'Processing verification timed out. Reselect this document to retry; no readiness was assumed.' : e.message);
          if (e.status === 401 || e.status === 403) { setHistory([]); setDocuments([]); setHidden(true); }
        }
      } finally { clearTimeout(requestDeadline); activeRequest = null; running = false; }
    }
    function visibility() {
      clearTimeout(timer);
      if (!document.hidden && !complete && attempts < 120) refresh();
    }
    document.addEventListener('visibilitychange', visibility);
    refresh();
    return () => { controller.abort(); activeRequest?.abort(); clearTimeout(timer); document.removeEventListener('visibilitychange', visibility); };
  }, [documentId, hidden, authorityEpoch]);

  async function refreshHistory(id, signal) {
    const data = await api(`/documents/${encodeURIComponent(id)}/classifications`, 'GET', undefined, signal);
    if (!alive.current || signal.aborted) return;
    setHistory(data.history || []); setSourceHash(data.sourceSha256 || '');
    setLabels(Object.fromEntries((data.history || []).map(item => [item.decisionId, item.review?.label || item.evidence?.predictedType || 'other'])));
  }

  if (hidden) return null;
  return <section className="laya-decisions" aria-labelledby="laya-title">
    <header><div><p>Module 064 · Local decision capability</p><h2 id="laya-title">Laya document classification</h2></div>
      <strong>{config ? (config.effectiveEnabled ? 'Enabled · human review' : 'Disabled') : 'Not yet verified'}</strong></header>
    <p>Uses the existing Celar runtime. Generative-provider order is unchanged. Recommendations never approve documents or trigger business actions.</p>
    {error && <p role="alert" className="laya-decisions__error">{error}</p>}
    {notice && <p role="status">{notice}</p>}
    <div className="laya-decisions__actions">
      <button type="button" disabled={busy} onClick={() => run(async signal => {
        const data = await api('', 'GET', undefined, signal);
        if (alive.current && !signal.aborted) setConfig(data);
      })}>Refresh configuration</button>
      <button type="button" disabled={busy || !config?.deploymentAllowed} onClick={() => run(async signal => {
        await api('', 'PUT', { enabled: !config.enabled, version: config.version }, signal);
        const data = await api('', 'GET', undefined, signal);
        if (alive.current && !signal.aborted) { setConfig(data); setNotice('Module 064 capability setting saved. Provider order was not changed.'); }
      })}>{config?.enabled ? 'Disable classifications' : 'Enable classifications'}</button>
      <button type="button" disabled={busy || !config?.deploymentAllowed} onClick={() => run(async signal => {
        const data = await api('/health', 'POST', {}, signal);
        if (alive.current && !signal.aborted) setHealth({ ...data, checkedAt: new Date().toLocaleString() });
      })}>Test gateway connection</button>
    </div>
    {config && !config.deploymentAllowed && <p>Deployment policy has not authorized this capability in this environment.</p>}
    {health && <p role="status">Gateway connection: {health.runtimeConnected ? 'ready' : 'unavailable'}. Checked {health.checkedAt}. {health.inferenceBusy ? 'Inference is currently busy.' : ''}</p>}
    <fieldset disabled={busy || !config?.deploymentAllowed}>
      <legend>Classify an authorized project document</legend>
      <label>Project code filter (optional)<input value={projectCode} maxLength={100} onChange={e => setProjectCode(e.target.value)} /></label>
      <button type="button" onClick={() => run(async signal => {
        const data = await api(`/documents?projectCode=${encodeURIComponent(projectCode)}`, 'GET', undefined, signal);
        if (alive.current && !signal.aborted) { setDocuments(data.documents || []); setDocumentId(''); setHistory([]); setNotice('Loaded up to 500 authorized documents. Use the project filter to narrow the list.'); }
      })}>Load documents</button>
      <label>Document<select value={documentId} onChange={e => { setDocumentId(e.target.value); setHistory([]); setSourceHash(''); }}>
        <option value="">Select a document</option>
        {documents.map(doc => <option key={doc.documentId} value={doc.documentId}>{doc.projectCode} · {doc.fileName} · {processingStageLabel(doc.processingStage)}</option>)}
      </select></label>
      {documentId && <p role="status">{processingMessage(processing)}</p>}
      {processingError && <p role="alert" className="laya-decisions__error">{processingError}</p>}
      <div className="laya-decisions__actions">
        <button type="button" disabled={!documentId || !config?.effectiveEnabled || !processing?.readyForClassification} onClick={() => run(async signal => {
          await api(`/documents/${encodeURIComponent(documentId)}/classifications`, 'POST', { requestId: crypto.randomUUID() }, signal);
          await refreshHistory(documentId, signal);
          if (alive.current && !signal.aborted) setNotice('Recommendation recorded. Review or correct it below.');
        })}>Classify document</button>
        <button type="button" disabled={!documentId} onClick={() => run(signal => refreshHistory(documentId, signal))}>Refresh review history</button>
      </div>
      <p>Excerpt-only assessment: the first 300 characters of the first nonempty extracted section. The service checks its exact token budget and rejects oversize input. This is not a whole-document completeness check.</p>
    </fieldset>
    {busy && <p role="status">Processing the request…</p>}
    {history.length > 0 && <h3>Recommendations and review history</h3>}
    {history.map(item => {
      const evidence = item.evidence || {};
      const stale = item.sourceSha256 !== sourceHash;
      return <article key={item.decisionId} className="laya-decisions__record">
        <h4>{LABELS[evidence.predictedType] || 'Unknown recommendation'}</h4>
        <p>{new Date(item.createdAt).toLocaleString()} · Model time {Number(evidence.serverLatencyMs).toFixed(0)} ms · {evidence.excerptCharacters} characters assessed</p>
        <p>Raw model score: {Number(evidence.rawModelConfidence).toFixed(3)}. This is not a probability that the answer is correct.</p>
        {stale && <p role="status">The source version changed. Create a new classification before review.</p>}
        {item.review ? <p>Reviewed as <strong>{LABELS[item.review.label]}</strong> on {new Date(item.review.reviewedAt).toLocaleString()}.</p> :
          <div className="laya-decisions__actions"><label>Reviewed classification<select disabled={busy || stale} value={labels[item.decisionId] || 'other'} onChange={e => setLabels(prev => ({ ...prev, [item.decisionId]: e.target.value }))}>
            {Object.entries(LABELS).map(([key, label]) => <option key={key} value={key}>{label}</option>)}
          </select></label><button type="button" disabled={busy || stale} onClick={() => run(async signal => {
            await api(`/documents/${encodeURIComponent(documentId)}/classifications/${item.decisionId}/review`, 'POST', { label: labels[item.decisionId] || 'other' }, signal);
            await refreshHistory(documentId, signal);
            if (alive.current && !signal.aborted) setNotice('Review recorded. The original recommendation is retained; the document category was not changed.');
          })}>Record review</button></div>}
        <details><summary>Version and audit evidence</summary><dl>
          <dt>Decision</dt><dd>{item.decisionId}</dd><dt>Source SHA-256</dt><dd>{item.sourceSha256}</dd>
          <dt>Excerpt SHA-256</dt><dd>{evidence.excerptSha256}</dd><dt>Model revision</dt><dd>{evidence.modelRevision}</dd>
          <dt>Policy version</dt><dd>{item.policyVersion}</dd><dt>Reviewer</dt><dd>{item.review?.reviewedBy || 'Awaiting review'}</dd>
        </dl></details>
      </article>;
    })}
  </section>;
}
