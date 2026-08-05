import { useCallback, useEffect, useMemo, useState } from 'react';
import './pulse-ai-private-runtime-workbench.css';

const TABS = Object.freeze([
  { id: 'readiness', label: 'Runtime Readiness', description: 'Migration, scanner, worker, OCR, embeddings, and index health' },
  { id: 'jobs', label: 'Processing Jobs', description: 'Authorized queue, retries, cancellation, and immutable evidence' },
  { id: 'queue', label: 'Queue Document', description: 'Explicitly queue an authorized project document' },
  { id: 'document', label: 'Document State', description: 'Version, chunk, embedding, and recent job status' }
]);

const QUEUE_CONFIRMATION = 'QUEUE-PULSE-AI-PRIVATE-DOCUMENT-PROCESSING';
const RETRY_CONFIRMATION = 'RETRY-PULSE-AI-PRIVATE-DOCUMENT-PROCESSING';
const CANCEL_CONFIRMATION = 'CANCEL-PULSE-AI-PRIVATE-DOCUMENT-PROCESSING';
const APPROVE_VERSION_CONFIRMATION = 'APPROVE-PULSE-AI-PRIVATE-DOCUMENT-VERSION';

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

async function readJson(response) {
  const payload = await response.json().catch(() => ({}));
  if (!response.ok) {
    const error = new Error(payload.message || `${response.url || 'Request'} returned HTTP ${response.status}.`);
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
    body: JSON.stringify(body)
  }));
}

function Status({ value }) {
  const normalized = String(value || 'unknown').toLowerCase();
  const ready = normalized.includes('ready') || normalized.includes('succeeded') || normalized.includes('completed');
  const failed = normalized.includes('failed') || normalized.includes('quarantined') || normalized.includes('rejected');
  const waiting = normalized.includes('queued') || normalized.includes('waiting') || normalized.includes('retry') || normalized.includes('partial');
  return (
    <span className={`pulse-ai-runtime-status ${ready ? 'is-ready' : failed ? 'is-failed' : waiting ? 'is-waiting' : 'is-neutral'}`}>
      {title(normalized)}
    </span>
  );
}

function KeyValues({ values }) {
  return (
    <dl className="pulse-ai-runtime-key-values">
      {Object.entries(values ?? {}).filter(([, value]) => value !== undefined).map(([key, value]) => (
        <div key={key}>
          <dt>{title(key)}</dt>
          <dd>{typeof value === 'boolean' ? (value ? 'Yes' : 'No') : String(value ?? 'Not recorded')}</dd>
        </div>
      ))}
    </dl>
  );
}

function ListBlock({ heading, values, empty = 'Nothing recorded.' }) {
  const rows = asArray(values);
  return (
    <section className="pulse-ai-runtime-list-block">
      <h5>{heading}</h5>
      {rows.length ? <ul>{rows.map((value, index) => <li key={`${heading}-${index}`}>{String(value)}</li>)}</ul> : <p>{empty}</p>}
    </section>
  );
}

function FullEvidence({ payload }) {
  if (!payload) return null;
  return (
    <details className="pulse-ai-runtime-evidence">
      <summary>View complete structured evidence</summary>
      <pre>{JSON.stringify(payload, null, 2)}</pre>
    </details>
  );
}

function Readiness({ payload }) {
  const readiness = payload?.readiness;
  if (!readiness) return null;
  return (
    <div className="pulse-ai-runtime-result-stack">
      <section className="pulse-ai-runtime-result-hero">
        <div>
          <p className="pulse-ai-runtime-eyebrow">Durable private runtime</p>
          <h4>{title(readiness.status)}</h4>
          <p>Private processing can run only after migration, authorization, scanner, worker, and private-endpoint checks pass.</p>
        </div>
        <Status value={readiness.status} />
      </section>
      <KeyValues values={{
        migrationApplied: readiness.migrationApplied,
        workerEnabled: readiness.workerEnabled,
        processingTablesAvailable: readiness.processingTablesAvailable,
        clamAvConfigured: readiness.clamAvConfigured,
        preScanAttestationConfigured: readiness.preScanAttestationConfigured,
        ocrConfigured: readiness.ocrConfigured,
        ocrEndpointPrivate: readiness.ocrEndpointPrivate,
        embeddingConfigured: readiness.embeddingConfigured,
        embeddingEndpointPrivate: readiness.embeddingEndpointPrivate,
        lexicalIndexAvailable: readiness.lexicalIndexAvailable,
        embeddingStorageAvailable: readiness.embeddingStorageAvailable,
        queuedJobCount: readiness.queuedJobCount,
        runningJobCount: readiness.runningJobCount,
        failedJobCount: readiness.failedJobCount,
        readyDocumentCount: readiness.readyDocumentCount,
        generatedAt: formatDate(readiness.generatedAt)
      }} />
      <div className="pulse-ai-runtime-two-column">
        <ListBlock heading="Ready capabilities" values={readiness.readyCapabilities} />
        <ListBlock heading="Current blockers" values={readiness.blockers} empty="No blocker was reported." />
      </div>
      <ListBlock heading="Missing configuration" values={readiness.missingConfiguration} empty="No required configuration is missing." />
      <section className="pulse-ai-runtime-privacy-card">
        <h5>Private boundary</h5>
        <p>Raw documents, extracted sections, chunks, embeddings, scanner responses, and provider secrets are never returned to this browser. Claude and OpenAI are not part of the private processing path.</p>
      </section>
      <FullEvidence payload={payload} />
    </div>
  );
}

function Jobs({ payload, onAction }) {
  const jobs = asArray(payload?.jobs);
  const summary = payload?.summary ?? {};
  return (
    <div className="pulse-ai-runtime-result-stack">
      <section className="pulse-ai-runtime-result-hero">
        <div>
          <p className="pulse-ai-runtime-eyebrow">Authorized processing jobs</p>
          <h4>{jobs.length} job{jobs.length === 1 ? '' : 's'} in effective-user scope</h4>
          <p>Queue records and operational evidence are role- and project-filtered before they are returned.</p>
        </div>
        <Status value={payload?.status} />
      </section>
      <KeyValues values={summary} />
      {jobs.length ? (
        <div className="pulse-ai-runtime-job-grid">
          {jobs.map((job) => (
            <article key={job.jobId}>
              <div className="pulse-ai-runtime-job-heading">
                <div>
                  <strong>{job.originalFileName || 'Project document'}</strong>
                  <span>{job.projectCode} — {job.projectName}</span>
                </div>
                <Status value={job.status} />
              </div>
              <KeyValues values={{
                jobId: job.jobId,
                documentId: job.documentId,
                category: job.documentCategory,
                purpose: job.requestedPurpose,
                attempt: `${job.attemptCount}/${job.maximumAttempts}`,
                priority: job.priority,
                sourceSha256: job.sourceSha256 || 'Not recorded',
                extractionMethod: job.extractionMethod || 'Not recorded',
                malwareScanner: job.malwareScanner || 'Not recorded',
                ocrProvider: job.ocrProvider || 'Not used',
                embeddingModel: job.embeddingModel || 'Not used',
                embeddingDimension: job.embeddingDimension || 'Not recorded',
                diagnosticCode: job.diagnosticCode || 'None',
                requestedAt: formatDate(job.requestedAt),
                updatedAt: formatDate(job.updatedAt),
                completedAt: formatDate(job.completedAt)
              }} />
              {job.diagnosticMessage ? <p className="pulse-ai-runtime-diagnostic">{job.diagnosticMessage}</p> : null}
              <div className="pulse-ai-runtime-job-actions">
                <button type="button" onClick={() => onAction('cancel', job)}>Prepare cancellation</button>
                <button type="button" onClick={() => onAction('retry', job)}>Prepare retry</button>
              </div>
            </article>
          ))}
        </div>
      ) : <p className="pulse-ai-runtime-empty">No authorized processing job matched the current filter.</p>}
      <FullEvidence payload={payload} />
    </div>
  );
}

function DocumentState({ payload, approvalReason, approvalConfirmation, onApprovalReason, onApprovalConfirmation, onApprove, busy }) {
  const document = payload?.document;
  if (!document) return null;
  return (
    <div className="pulse-ai-runtime-result-stack">
      <section className="pulse-ai-runtime-result-hero">
        <div>
          <p className="pulse-ai-runtime-eyebrow">Private document runtime state</p>
          <h4>{document.originalFileName}</h4>
          <p>{document.projectCode} — {document.projectName} · {String(document.documentCategory || 'other').toUpperCase()}</p>
        </div>
        <Status value={document.processingStatus} />
      </section>
      <KeyValues values={{
        documentId: document.documentId,
        projectId: document.projectId,
        processingStatus: document.processingStatus,
        classification: document.classification,
        revision: document.revision || 'Not designated',
        effectiveAt: formatDate(document.effectiveAt),
        activeVersionId: document.activeVersionId || 'Not designated',
        errorCode: document.errorCode || 'None',
        versionCount: document.versionCount,
        activeChunkCount: document.activeChunkCount,
        embeddingReadyChunkCount: document.embeddingReadyChunkCount,
        lastProcessedAt: formatDate(document.lastProcessedAt),
        processingUpdatedAt: formatDate(document.processingUpdatedAt)
      }} />
      <section className="pulse-ai-runtime-card">
        <h5>Recent processing history</h5>
        <div className="pulse-ai-runtime-history">
          {asArray(document.recentJobs).map((job) => (
            <div key={job.jobId}>
              <Status value={job.status} />
              <strong>{formatDate(job.requestedAt)}</strong>
              <span>{job.diagnosticCode || 'No diagnostic'} · Attempt {job.attemptCount}/{job.maximumAttempts}</span>
            </div>
          ))}
        </div>
      </section>
      {document.processingStatus === 'ready' && document.activeVersionId && document.activeVersionSourceSha256 ? (
        <form className="pulse-ai-runtime-form" onSubmit={onApprove}>
          <div>
            <h4>Approve the active version for Celar AI retrieval</h4>
            <p>Processing readiness does not grant source authority. An authorized reviewer must approve the active SOW/GSD version before Timesheet retrieval can use it.</p>
          </div>
          <label>Approval reason<textarea rows="3" value={approvalReason} onChange={(event) => onApprovalReason(event.target.value)} placeholder="Confirm why this is the authoritative version for the project." /></label>
          <label>Exact confirmation<input required value={approvalConfirmation} onChange={(event) => onApprovalConfirmation(event.target.value)} placeholder={APPROVE_VERSION_CONFIRMATION} /><small>{APPROVE_VERSION_CONFIRMATION}</small></label>
          <button type="submit" disabled={busy || approvalConfirmation.trim() !== APPROVE_VERSION_CONFIRMATION}>Approve exact active version</button>
        </form>
      ) : null}
      <FullEvidence payload={payload} />
    </div>
  );
}

export default function PulseAiPrivateRuntimeWorkbench() {
  const [activeTab, setActiveTab] = useState('readiness');
  const [readiness, setReadiness] = useState(null);
  const [jobs, setJobs] = useState(null);
  const [documentState, setDocumentState] = useState(null);
  const [jobStatus, setJobStatus] = useState('');
  const [documentId, setDocumentId] = useState('');
  const [approvalReason, setApprovalReason] = useState('');
  const [approvalConfirmation, setApprovalConfirmation] = useState('');
  const [queueForm, setQueueForm] = useState({ documentId: '', purpose: 'private_document_indexing', priority: '50', maximumAttempts: '3', confirmation: '' });
  const [jobAction, setJobAction] = useState(null);
  const [actionReason, setActionReason] = useState('');
  const [actionConfirmation, setActionConfirmation] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');

  const tab = useMemo(() => TABS.find((item) => item.id === activeTab) ?? TABS[0], [activeTab]);

  const loadReadiness = useCallback(async () => {
    setBusy(true); setError('');
    try { setReadiness(await getJson('/api/pulse-ai/v1/documents/runtime/readiness')); }
    catch (loadError) { setError(loadError instanceof Error ? loadError.message : 'Runtime readiness could not be loaded.'); }
    finally { setBusy(false); }
  }, []);

  const loadJobs = useCallback(async () => {
    setBusy(true); setError('');
    try {
      const url = new URL('/api/pulse-ai/v1/documents/runtime/jobs', window.location.origin);
      if (jobStatus.trim()) url.searchParams.set('status', jobStatus.trim());
      url.searchParams.set('limit', '100');
      setJobs(await getJson(`${url.pathname}${url.search}`));
    } catch (loadError) { setError(loadError instanceof Error ? loadError.message : 'Processing jobs could not be loaded.'); }
    finally { setBusy(false); }
  }, [jobStatus]);

  useEffect(() => { void loadReadiness(); }, [loadReadiness]);
  useEffect(() => { if (activeTab === 'jobs') void loadJobs(); }, [activeTab, loadJobs]);

  async function queueDocument(event) {
    event.preventDefault();
    setBusy(true); setError(''); setNotice('');
    try {
      const payload = await postJson(`/api/pulse-ai/v1/documents/${encodeURIComponent(queueForm.documentId.trim())}/processing-jobs`, {
        purpose: queueForm.purpose.trim(),
        priority: Number(queueForm.priority),
        maximumAttempts: Number(queueForm.maximumAttempts),
        confirmation: queueForm.confirmation.trim()
      });
      setNotice(`Processing job ${payload?.job?.jobId || ''} was queued. Worker execution remains controlled by private runtime configuration.`);
      setQueueForm((current) => ({ ...current, confirmation: '' }));
      await loadReadiness();
    } catch (actionError) { setError(actionError instanceof Error ? actionError.message : 'The document could not be queued.'); }
    finally { setBusy(false); }
  }

  async function loadDocument(event) {
    event.preventDefault();
    setBusy(true); setError(''); setNotice('');
    try { setDocumentState(await getJson(`/api/pulse-ai/v1/documents/${encodeURIComponent(documentId.trim())}/runtime-state`)); }
    catch (loadError) { setError(loadError instanceof Error ? loadError.message : 'Document runtime state could not be loaded.'); }
    finally { setBusy(false); }
  }

  async function approveActiveVersion(event) {
    event.preventDefault();
    const document = documentState?.document;
    if (!document?.documentId || !document?.activeVersionId || !document?.activeVersionSourceSha256) return;
    setBusy(true); setError(''); setNotice('');
    try {
      const payload = await postJson(`/api/pulse-ai/v1/documents/${encodeURIComponent(document.documentId)}/versions/${encodeURIComponent(document.activeVersionId)}/approve`, {
        reason: approvalReason.trim(),
        expectedSourceSha256: document.activeVersionSourceSha256,
        confirmation: approvalConfirmation.trim()
      });
      setNotice(title(payload.status));
      setApprovalReason('');
      setApprovalConfirmation('');
      setDocumentState(await getJson(`/api/pulse-ai/v1/documents/${encodeURIComponent(document.documentId)}/runtime-state`));
      await loadReadiness();
    } catch (actionError) { setError(actionError instanceof Error ? actionError.message : 'The active document version could not be approved.'); }
    finally { setBusy(false); }
  }

  function prepareAction(action, job) {
    setJobAction({ action, job });
    setActionReason('');
    setActionConfirmation('');
    setNotice('');
    setError('');
  }

  async function submitJobAction(event) {
    event.preventDefault();
    if (!jobAction) return;
    setBusy(true); setError(''); setNotice('');
    try {
      const endpoint = jobAction.action === 'cancel' ? 'cancel' : 'retry';
      const payload = await postJson(`/api/pulse-ai/v1/documents/runtime/jobs/${jobAction.job.jobId}/${endpoint}`, {
        reason: actionReason.trim(),
        confirmation: actionConfirmation.trim()
      });
      setNotice(title(payload.status));
      setJobAction(null);
      await loadJobs();
      await loadReadiness();
    } catch (actionError) { setError(actionError instanceof Error ? actionError.message : 'The job action could not be completed.'); }
    finally { setBusy(false); }
  }

  const requiredActionConfirmation = jobAction?.action === 'cancel' ? CANCEL_CONFIRMATION : RETRY_CONFIRMATION;

  return (
    <section className="pulse-ai-runtime-workbench" data-pulse-ai-private-runtime="v1">
      <header className="pulse-ai-runtime-header">
        <div>
          <p className="pulse-ai-runtime-eyebrow">Module 011 · Phase 011C</p>
          <h2>Private Document Runtime & Permission-Scoped Hybrid Index</h2>
          <p>Operate durable malware scanning, private extraction and OCR, citation-preserving chunks, private embeddings, PostgreSQL hybrid retrieval evidence, retries, cancellation, version history, and immutable processing audit.</p>
        </div>
        <span className="pulse-ai-runtime-private-pill">Private by default</span>
      </header>

      <div className="pulse-ai-runtime-warning">
        <strong>Controlled activation:</strong> Queue, retry, and cancellation require explicit permissions, exact confirmations, current project authorization, and a non-View-As session. Raw document content and vectors never leave the private runtime through this UI.
      </div>

      <nav className="pulse-ai-runtime-tabs" aria-label="Celar AI private runtime workspaces">
        {TABS.map((item) => (
          <button type="button" className={activeTab === item.id ? 'is-active' : ''} key={item.id} onClick={() => setActiveTab(item.id)}>
            <strong>{item.label}</strong><span>{item.description}</span>
          </button>
        ))}
      </nav>

      <div className="pulse-ai-runtime-panel">
        <div className="pulse-ai-runtime-panel-heading">
          <div><p className="pulse-ai-runtime-eyebrow">Private runtime workspace</p><h3>{tab.label}</h3><p>{tab.description}</p></div>
          <button type="button" onClick={activeTab === 'jobs' ? loadJobs : loadReadiness} disabled={busy}>Refresh evidence</button>
        </div>
        {busy ? <div className="pulse-ai-runtime-loading" role="status">Processing the authorized private runtime request…</div> : null}
        {error ? <div className="pulse-ai-runtime-error" role="alert">{error}</div> : null}
        {notice ? <div className="pulse-ai-runtime-notice" role="status">{notice}</div> : null}

        {activeTab === 'readiness' ? <Readiness payload={readiness} /> : null}
        {activeTab === 'jobs' ? (
          <>
            <form className="pulse-ai-runtime-filter-form" onSubmit={(event) => { event.preventDefault(); void loadJobs(); }}>
              <label>Job status<input value={jobStatus} onChange={(event) => setJobStatus(event.target.value)} placeholder="queued, failed, succeeded…" /></label>
              <button type="submit" disabled={busy}>Load jobs</button>
            </form>
            <Jobs payload={jobs} onAction={prepareAction} />
          </>
        ) : null}

        {activeTab === 'queue' ? (
          <form className="pulse-ai-runtime-form" onSubmit={queueDocument}>
            <div><h4>Queue an authorized document</h4><p>Paste the document UUID from the Authorized Inventory. A duplicate active job is rejected.</p></div>
            <label>Document ID<input required value={queueForm.documentId} onChange={(event) => setQueueForm((current) => ({ ...current, documentId: event.target.value }))} placeholder="00000000-0000-0000-0000-000000000000" /></label>
            <label>Purpose<input value={queueForm.purpose} onChange={(event) => setQueueForm((current) => ({ ...current, purpose: event.target.value }))} /></label>
            <div className="pulse-ai-runtime-form-grid">
              <label>Priority<input type="number" min="1" max="100" value={queueForm.priority} onChange={(event) => setQueueForm((current) => ({ ...current, priority: event.target.value }))} /></label>
              <label>Maximum attempts<input type="number" min="1" max="20" value={queueForm.maximumAttempts} onChange={(event) => setQueueForm((current) => ({ ...current, maximumAttempts: event.target.value }))} /></label>
            </div>
            <label>Exact confirmation<input required value={queueForm.confirmation} onChange={(event) => setQueueForm((current) => ({ ...current, confirmation: event.target.value }))} placeholder={QUEUE_CONFIRMATION} /><small>{QUEUE_CONFIRMATION}</small></label>
            <button type="submit" disabled={busy || queueForm.confirmation.trim() !== QUEUE_CONFIRMATION}>Queue private processing</button>
          </form>
        ) : null}

        {activeTab === 'document' ? (
          <>
            <form className="pulse-ai-runtime-filter-form" onSubmit={loadDocument}>
              <label>Document ID<input required value={documentId} onChange={(event) => setDocumentId(event.target.value)} placeholder="00000000-0000-0000-0000-000000000000" /></label>
              <button type="submit" disabled={busy}>Load document state</button>
            </form>
            <DocumentState
              payload={documentState}
              approvalReason={approvalReason}
              approvalConfirmation={approvalConfirmation}
              onApprovalReason={setApprovalReason}
              onApprovalConfirmation={setApprovalConfirmation}
              onApprove={approveActiveVersion}
              busy={busy}
            />
          </>
        ) : null}
      </div>

      {jobAction ? (
        <div className="pulse-ai-runtime-action-overlay" role="dialog" aria-modal="true" aria-labelledby="pulse-ai-runtime-action-title">
          <form className="pulse-ai-runtime-action-dialog" onSubmit={submitJobAction}>
            <h3 id="pulse-ai-runtime-action-title">{title(jobAction.action)} processing job</h3>
            <p>{jobAction.job.originalFileName} · {jobAction.job.jobId}</p>
            <label>Reason<textarea rows="4" value={actionReason} onChange={(event) => setActionReason(event.target.value)} placeholder="Describe the reviewed blocker or cancellation reason." /></label>
            <label>Exact confirmation<input required value={actionConfirmation} onChange={(event) => setActionConfirmation(event.target.value)} placeholder={requiredActionConfirmation} /><small>{requiredActionConfirmation}</small></label>
            <div><button type="button" onClick={() => setJobAction(null)}>Close</button><button type="submit" disabled={busy || actionConfirmation.trim() !== requiredActionConfirmation}>Confirm {jobAction.action}</button></div>
          </form>
        </div>
      ) : null}
    </section>
  );
}
